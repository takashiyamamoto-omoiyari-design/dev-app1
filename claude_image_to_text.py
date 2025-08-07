#!/usr/bin/env python3
# -*- coding: utf-8 -*-

"""
Claude 3.7を使用してPDF画像からテキストを構造化するスクリプト
"""

import os
import sys
import json
import base64
import argparse
import time
from concurrent.futures import ThreadPoolExecutor, as_completed
import anthropic
from PIL import Image
import io

# APIキーを環境変数から取得
ANTHROPIC_API_KEY = os.environ.get("ANTHROPIC_API_KEY")

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

def structure_text_with_claude(image_path, page_number, retry_count=3, retry_delay=5):
    """
    Claudeを使用して画像からテキストを構造化します
    
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
    if not ANTHROPIC_API_KEY:
        print("エラー: ANTHROPIC_API_KEYが設定されていません", file=sys.stderr)
        return False, "APIキーが設定されていません"
    
    # 画像をBase64エンコード
    image_base64 = get_image_base64(image_path)
    if not image_base64:
        return False, "画像のエンコードに失敗しました"
    
    # Anthropicクライアントを初期化
    client = anthropic.Anthropic(api_key=ANTHROPIC_API_KEY)
    
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
    
    # リトライロジック
    for attempt in range(retry_count):
        try:
            print(f"ページ {page_number} の処理中... (試行 {attempt + 1}/{retry_count})")
            
            # Claude 3.7 Sonnetに送信
            message = client.messages.create(
                model="claude-3-7-sonnet-20250219",
                max_tokens=10000,
                system=system_prompt,
                messages=[
                    {
                        "role": "user",
                        "content": [
                            {
                                "type": "text",
                                "text": user_prompt
                            },
                            {
                                "type": "image",
                                "source": {
                                    "type": "base64",
                                    "media_type": "image/png",
                                    "data": image_base64
                                }
                            }
                        ]
                    }
                ]
            )
            
            # レスポンスからテキストを取得
            extracted_text = message.content[0].text
            return True, extracted_text
        
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
        (image_path, file_id, page_number, output_dir)
    
    Returns:
    --------
    tuple
        (page_number, 成功したかどうか, 出力ファイルパス)
    """
    image_path, file_id, page_number, output_dir = args
    
    print(f"ページ {page_number} の処理を開始...")
    success, text = structure_text_with_claude(image_path, page_number)
    
    if success:
        # 出力ディレクトリが存在しない場合は作成
        os.makedirs(output_dir, exist_ok=True)
        
        # テキストファイルに保存
        output_path = os.path.join(output_dir, f"{file_id}-page-{page_number}.txt")
        with open(output_path, "w", encoding="utf-8") as f:
            f.write(text)
        
        print(f"ページ {page_number} の処理が完了しました: {output_path}")
        return page_number, True, output_path
    else:
        print(f"ページ {page_number} の処理に失敗しました: {text}", file=sys.stderr)
        return page_number, False, text

def process_images(image_dir, file_id, output_dir, max_workers=3):
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
    max_workers : int, optional
        同時に処理するスレッド数（デフォルト: 3）
    
    Returns:
    --------
    dict
        処理結果の辞書
    """
    # 指定されたファイルIDに対応する画像ファイルを検索
    image_files = []
    for filename in os.listdir(image_dir):
        if filename.startswith(f"{file_id}-page-") and (filename.endswith(".png") or filename.endswith(".jpg")):
            image_files.append(filename)
    
    # ページ番号でソート
    image_files.sort(key=lambda x: int(x.split("-page-")[1].split(".")[0]))
    
    if not image_files:
        print(f"エラー: {image_dir} にファイルID '{file_id}' に対応する画像が見つかりません", file=sys.stderr)
        return {"success": False, "processed_pages": 0, "total_pages": 0, "error": "画像が見つかりません"}
    
    print(f"処理する画像ファイル数: {len(image_files)}")
    
    # 処理タスクのリストを作成
    tasks = []
    for filename in image_files:
        image_path = os.path.join(image_dir, filename)
        page_number = int(filename.split("-page-")[1].split(".")[0])
        tasks.append((image_path, file_id, page_number, output_dir))
    
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
    parser = argparse.ArgumentParser(description='画像からテキストを構造化します')
    parser.add_argument('file_id', help='処理するファイルID')
    parser.add_argument('--image_dir', default='storage/images', help='画像ファイルのディレクトリ（デフォルト: storage/images）')
    parser.add_argument('--output_dir', default='storage/tmp', help='出力テキストファイルのディレクトリ（デフォルト: storage/tmp）')
    parser.add_argument('--max_workers', type=int, default=3, help='同時処理するスレッド数（デフォルト: 3）')
    
    args = parser.parse_args()
    
    # 画像を処理
    result = process_images(
        args.image_dir, 
        args.file_id,
        args.output_dir,
        max_workers=args.max_workers
    )
    
    # 結果を出力
    print(json.dumps(result, indent=2, ensure_ascii=False))
    
    # 終了コード
    return 0 if result["success"] else 1

if __name__ == "__main__":
    sys.exit(main())