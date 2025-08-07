#!/usr/bin/env python3
# -*- coding: utf-8 -*-

"""
Azure OpenAI GPT-4o (gpt4o-forRAG)を使用してPDF画像からテキストを構造化するスクリプト
"""

import os
import sys
import json
import base64
import argparse
import time
import requests
from concurrent.futures import ThreadPoolExecutor, as_completed
from PIL import Image
import io

# APIキーを環境変数から取得
# Azure OpenAIの設定をOpenAIの設定に変更
OPENAI_API_TYPE = os.environ.get("OPENAI_API_TYPE", "openai")
OPENAI_API_KEY = os.environ.get("OPENAI_API_KEY")
OPENAI_API_BASE = os.environ.get("OPENAI_API_BASE", "https://api.openai.com/v1")
OPENAI_MODEL = os.environ.get("OPENAI_MODEL", "gpt-4o-mini")

# 従来のAzure OpenAI変数も取得（後方互換性のため）
AZURE_OPENAI_ENDPOINT = os.environ.get("AZURE_OPENAI_ENDPOINT")
AZURE_OPENAI_KEY = os.environ.get("AZURE_OPENAI_KEY")
AZURE_OPENAI_DEPLOYMENT = os.environ.get("AZURE_OPENAI_DEPLOYMENT", "gpt4o-forRAG")
AZURE_OPENAI_API_VERSION = os.environ.get("AZURE_OPENAI_API_VERSION", "2024-02-15-preview")

def get_image_base64(image_path):
    """
    画像ファイルをBase64エンコードして返します
    
    Parameters:
    -----------
    image_path : str
        画像ファイルのパス
    
    Returns:
    --------
    str
        Base64エンコードされた画像データ
    """
    try:
        # 画像ファイルを開く
        img = Image.open(image_path)
        
        # 大きすぎる画像はリサイズ (最大幅/高さを4000pxに制限)
        max_size = 4000
        if img.width > max_size or img.height > max_size:
            # アスペクト比を維持しながらリサイズ
            if img.width > img.height:
                new_width = max_size
                new_height = int(img.height * (max_size / img.width))
            else:
                new_height = max_size
                new_width = int(img.width * (max_size / img.height))
            img = img.resize((new_width, new_height))
        
        # PNGに変換（透明度を持たないフォーマットの場合のため）
        img_byte_arr = io.BytesIO()
        img.save(img_byte_arr, format='PNG')
        img_byte_arr.seek(0)
        
        # Base64エンコード
        base64_encoded = base64.b64encode(img_byte_arr.getvalue()).decode('utf-8')
        return base64_encoded
    except Exception as e:
        print(f"エラー: 画像のBase64エンコードに失敗しました: {str(e)}", file=sys.stderr)
        return None

def structure_text_with_azure_openai(image_path, page_number, retry_count=3, retry_delay=5):
    """
    OpenAIのVision APIを使用して画像からテキストを構造化します
    
    Parameters:
    -----------
    image_path : str
        画像ファイルのパス
    page_number : int
        ページ番号
    retry_count : int, optional
        リトライ回数（デフォルト: 3）
    retry_delay : int, optional
        リトライ間の待機時間（秒）（デフォルト: 5）
    
    Returns:
    --------
    tuple
        (成功したかどうか, 構造化テキスト)
    """
    # OpenAI直接アクセスを優先、なければAzureを試行
    if OPENAI_API_TYPE == "openai" and OPENAI_API_KEY:
        # 直接OpenAI APIを使用
        api_base = OPENAI_API_BASE
        api_key = OPENAI_API_KEY
        model = OPENAI_MODEL
        is_azure = False
        print(f"直接OpenAI APIを使用します: モデル={model}")
    elif AZURE_OPENAI_ENDPOINT and AZURE_OPENAI_KEY:
        # Azure OpenAI APIを使用
        api_base = AZURE_OPENAI_ENDPOINT
        api_key = AZURE_OPENAI_KEY
        model = AZURE_OPENAI_DEPLOYMENT
        is_azure = True
        print(f"Azure OpenAI APIを使用します: デプロイメント={model}")
    else:
        print("エラー: OpenAI API認証情報が設定されていません", file=sys.stderr)
        return False, "OpenAI APIの認証情報が設定されていません"
    
    # 画像をBase64エンコード
    image_base64 = get_image_base64(image_path)
    if not image_base64:
        return False, "画像のエンコードに失敗しました"
    
    # API URLを構築
    if is_azure:
        api_url = f"{api_base}/openai/deployments/{model}/chat/completions?api-version={AZURE_OPENAI_API_VERSION}"
    else:
        api_url = f"{api_base}/chat/completions"
    
    # リクエストヘッダー
    headers = {
        "Content-Type": "application/json"
    }
    
    # APIキーヘッダー設定
    if is_azure:
        headers["api-key"] = api_key
    else:
        headers["Authorization"] = f"Bearer {api_key}"
    
    # プロンプト
    system_prompt = """あなたはPDF画像からテキストを抽出し構造化するエキスパートです。
画像内のすべてのテキスト情報を抽出し、元のレイアウトを維持しつつ、以下のルールに従って構造化してください：

1. 段落、リスト、表、見出しなどの構造を維持すること
2. テキストの順序を正確に保持すること
3. 複数列のレイアウトがある場合、左から右、上から下の順に処理すること
4. 表は可能な限りMarkdownテーブル形式で再現すること
5. 画像についての説明は [画像: 簡単な説明] のように記載すること
6. 数式やシンボルは可能な限り正確に表現すること
7. フォーマットはマークダウン形式で出力すること

出力は純粋なテキスト内容のみとし、説明や分析は含めないでください。
"""
    
    user_prompt = "この画像からすべてのテキスト内容を抽出し、マークダウン形式で構造化してください。"
    
    # リクエストボディ
    request_body = {
        "messages": [
            {"role": "system", "content": system_prompt},
            {
                "role": "user",
                "content": [
                    {"type": "text", "text": user_prompt},
                    {
                        "type": "image_url",
                        "image_url": {
                            "url": f"data:image/png;base64,{image_base64}"
                        }
                    }
                ]
            }
        ],
        "max_tokens": 4000
    }
    
    # OpenAI直接アクセスの場合はモデル名を指定
    if not is_azure:
        request_body["model"] = model
    
    # リトライロジック
    for attempt in range(retry_count):
        try:
            print(f"ページ {page_number} の処理中... (試行 {attempt + 1}/{retry_count}) {'OpenAI' if not is_azure else 'Azure OpenAI'} 使用")
            
            # API呼び出し
            response = requests.post(api_url, headers=headers, json=request_body)
            
            if response.status_code == 200:
                response_data = response.json()
                extracted_text = response_data["choices"][0]["message"]["content"]
                return True, extracted_text
            else:
                print(f"APIエラー: ステータスコード {response.status_code}", file=sys.stderr)
                print(f"レスポンス: {response.text}", file=sys.stderr)
                
                if attempt < retry_count - 1:
                    print(f"{retry_delay}秒後にリトライします...")
                    time.sleep(retry_delay)
                else:
                    return False, f"APIエラー: {response.status_code} - {response.text}"
        
        except Exception as e:
            print(f"エラー (試行 {attempt + 1}/{retry_count}): {str(e)}", file=sys.stderr)
            if attempt < retry_count - 1:
                print(f"{retry_delay}秒後にリトライします...")
                time.sleep(retry_delay)
    
    return False, f"ページ {page_number} の処理に失敗しました"

def process_single_image(args):
    """
    単一の画像を処理するヘルパー関数（並列処理用）
    
    Parameters:
    -----------
    args : tuple
        (image_path, file_id, page_number, output_dir, original_filename)
    
    Returns:
    --------
    tuple
        (page_number, 成功したかどうか, 出力ファイルパス)
    """
    image_path, file_id, page_number, output_dir, original_filename = args
    
    print(f"ページ {page_number} の処理を開始...")
    success, text = structure_text_with_azure_openai(image_path, page_number)
    
    if success:
        # 出力ディレクトリが存在しない場合は作成
        os.makedirs(output_dir, exist_ok=True)
        
        # テキストファイルに保存（コントローラーの検索パターンに合わせる）
        # 元のファイル名が指定されている場合はそれを使用
        if original_filename:
            output_filename = f"{original_filename}-page-{page_number}.txt"
        else:
            output_filename = f"{file_id}-page-{page_number}.txt"
            
        output_path = os.path.join(output_dir, output_filename)
        with open(output_path, "w", encoding="utf-8") as f:
            f.write(text)
        
        print(f"ページ {page_number} の処理が完了しました: {output_path}")
        return page_number, True, output_path
    else:
        print(f"ページ {page_number} の処理に失敗しました: {text}", file=sys.stderr)
        return page_number, False, text

def process_images(image_dir, file_id, output_dir, original_filename=None, max_workers=3):
    """
    指定されたディレクトリ内の画像ファイルを処理します
    
    Parameters:
    -----------
    image_dir : str
        画像ファイルが保存されているディレクトリ
    file_id : str
        ファイルID
    output_dir : str
        テキストファイルの出力先ディレクトリ
    original_filename : str, optional
        元のPDFファイル名
    max_workers : int, optional
        同時に処理するスレッド数（デフォルト: 3）
    
    Returns:
    --------
    dict
        処理結果の辞書
    """
    # 指定されたディレクトリ内の画像ファイルを検索
    image_files = []
    print(f"画像検索ディレクトリ: {image_dir}")
    print(f"ファイルID: {file_id}")
    
    if os.path.exists(image_dir):
        for filename in os.listdir(image_dir):
            print(f"検出されたファイル: {filename}")
            # 複数のパターンをチェック
            # 1. file_id_page_X.png/jpg パターン
            # 2. original_filename_page_X.png/jpg パターン（元のファイル名使用時）
            is_valid_image = False
            
            # パターン1: file_id_page_X形式
            if (filename.startswith(f"{file_id}_page_") and (filename.endswith(".png") or filename.endswith(".jpg"))):
                is_valid_image = True
                
            # パターン2: 元のファイル名を使用している場合
            if original_filename and ((filename.startswith(f"{original_filename}_page_") and 
                (filename.endswith(".png") or filename.endswith(".jpg")))):
                is_valid_image = True
                
            if is_valid_image:
                image_files.append(filename)
                print(f"有効な画像ファイルを検出: {filename}")
    else:
        print(f"ディレクトリが存在しません: {image_dir}")
    
    # ページ番号でソート
    def extract_page_number(filename):
        try:
            # ファイル名から "page_X" の部分を抽出してページ番号を取得
            return int(filename.split("_page_")[1].split(".")[0])
        except (IndexError, ValueError):
            # 解析できない場合は大きな値を返して後ろに並べる
            print(f"ファイル名からページ番号を抽出できませんでした: {filename}")
            return 9999
    
    image_files.sort(key=extract_page_number)
    
    if not image_files:
        print(f"エラー: {image_dir} にファイルID '{file_id}' に対応する画像が見つかりません", file=sys.stderr)
        return {"success": False, "processed_pages": 0, "total_pages": 0, "error": "画像が見つかりません"}
    
    print(f"処理する画像ファイル数: {len(image_files)}")
    
    # 処理タスクのリストを作成
    tasks = []
    for filename in image_files:
        image_path = os.path.join(image_dir, filename)
        page_number = extract_page_number(filename)
        tasks.append((image_path, file_id, page_number, output_dir, original_filename))
    
    # 元のファイル名があれば表示
    if original_filename:
        print(f"元のPDFファイル名: {original_filename}")
    
    # 並列処理で画像を処理
    results = []
    with ThreadPoolExecutor(max_workers=max_workers) as executor:
        futures = {executor.submit(process_single_image, task): task for task in tasks}
        for future in as_completed(futures):
            results.append(future.result())
    
    # 結果を集計
    successful_pages = [result for result in results if result[1]]
    failed_pages = [result for result in results if not result[1]]
    
    result = {
        "success": len(failed_pages) == 0,
        "processed_pages": len(successful_pages),
        "total_pages": len(tasks),
        "failed_pages": [{"page": result[0], "error": result[2]} for result in failed_pages] if failed_pages else []
    }
    
    return result

def main():
    # コマンドライン引数の解析
    parser = argparse.ArgumentParser(description='PDF画像からテキストを構造化します')
    parser.add_argument('image_dir', help='画像ファイルのディレクトリ')
    parser.add_argument('file_id', help='処理するファイルID')
    parser.add_argument('output_dir', help='出力テキストファイルのディレクトリ')
    parser.add_argument('original_filename', nargs='?', default=None, help='元のPDFファイル名')
    parser.add_argument('--max_workers', type=int, default=3, help='同時処理するスレッド数（デフォルト: 3）')
    
    args = parser.parse_args()
    
    # 元のファイル名を取得
    original_filename = args.original_filename
    if original_filename:
        print(f"元のPDFファイル名: {original_filename}")
    
    # 画像を処理
    result = process_images(
        args.image_dir, 
        args.file_id,
        args.output_dir,
        original_filename,
        max_workers=args.max_workers
    )
    
    # 結果を出力
    print(json.dumps(result, indent=2, ensure_ascii=False))
    
    # 終了コード
    return 0 if result["success"] else 1

if __name__ == "__main__":
    sys.exit(main())