import os
import sys
import json
import base64
import fitz  # pymupdf
from PIL import Image
import io
import re
from datetime import datetime
import anthropic

def extract_images_from_pdf(pdf_path, output_dir, file_id):
    """PDFファイルから画像を抽出し、出力ディレクトリに保存します"""
    # 出力ディレクトリを作成
    os.makedirs(output_dir, exist_ok=True)
    
    image_paths = []
    
    # PDFを開く
    pdf_document = fitz.open(pdf_path)
    
    # ページ数を取得
    total_pages = len(pdf_document)
    print(f"PDFページ数: {total_pages}")
    
    # 各ページを画像として保存
    for page_num in range(total_pages):
        page = pdf_document.load_page(page_num)
        
        # ページをPIXマップにレンダリング（解像度を高く設定：300 DPI）
        pix = page.get_pixmap(matrix=fitz.Matrix(300/72, 300/72))
        
        # PIXマップをPIL画像に変換
        img = Image.frombytes("RGB", (pix.width, pix.height), pix.samples)
        
        # 画像ファイルのパスを設定
        image_path = os.path.join(output_dir, f"{file_id}-page-{page_num+1}.png")
        
        # 画像を保存
        img.save(image_path, "PNG")
        image_paths.append(image_path)
        
        print(f"ページ {page_num+1}/{total_pages} を画像として保存: {image_path}")
    
    # PDFを閉じる
    pdf_document.close()
    
    return image_paths

def get_image_base64(image_path):
    """画像ファイルをBase64エンコードして返します"""
    with open(image_path, "rb") as image_file:
        return base64.b64encode(image_file.read()).decode('utf-8')

def structure_text_with_claude(image_path, page_number, api_key):
    """ClaudeにPDF画像を送信し、構造化テキストを取得します"""
    print(f"ページ {page_number} の画像をClaudeに送信して構造化")
    
    # 画像をBase64エンコード
    base64_image = get_image_base64(image_path)
    
    # Anthropicクライアントを初期化
    client = anthropic.Anthropic(api_key=api_key)
    
    # システムプロンプト
    system_prompt = """
    あなたはPDF画像からテキストを抽出し構造化する専門家です。
    
    【重要なルール】
    1. 画像に表示されているテキストを正確に抽出してください
    2. 段落、見出し、箇条書きなどの構造を維持してください
    3. テキストの内容は変更せず、原文をそのまま抽出してください
    4. 画像内に表示されている実際のテキストのみを抽出し、説明や解釈は追加しないでください
    5. テキストが読み取れない場合は「[読み取り不能テキスト]」と記載してください
    """
    
    # メッセージを作成
    messages = [
        {
            "role": "user",
            "content": [
                {
                    "type": "image",
                    "source": {
                        "type": "base64",
                        "media_type": "image/png",
                        "data": base64_image
                    }
                },
                {
                    "type": "text",
                    "text": f"この画像はPDFのページ{page_number}です。画像からすべてのテキストを抽出し、元の構造（段落、見出し、箇条書きなど）を維持しながら構造化したテキストを提供してください。テキストのみを抽出し、説明や解釈は追加しないでください。"
                }
            ]
        }
    ]
    
    # Claudeに送信
    response = client.messages.create(
        model="claude-3-7-sonnet-20250219",
        system=system_prompt,
        messages=messages,
        max_tokens=10000,
    )
    
    # レスポンスからテキストを取得
    extracted_text = response.content[0].text
    
    return extracted_text

def main():
    if len(sys.argv) < 2:
        print("使用方法: python pdf_image_extractor.py <PDFファイルパス> <ファイルID>")
        sys.exit(1)
    
    pdf_path = sys.argv[1]
    file_id = sys.argv[2]
    
    # 環境変数からAPIキーを取得
    api_key = os.environ.get("ANTHROPIC_API_KEY")
    if not api_key:
        print("環境変数ANTHROPIC_API_KEYが設定されていません")
        sys.exit(1)
    
    # 出力ディレクトリ
    output_dir = "storage/images"
    tmp_dir = "storage/tmp"
    
    # ディレクトリが存在することを確認
    os.makedirs(output_dir, exist_ok=True)
    os.makedirs(tmp_dir, exist_ok=True)
    
    # PDFから画像を抽出
    print(f"PDFファイル'{pdf_path}'から画像を抽出します...")
    image_paths = extract_images_from_pdf(pdf_path, output_dir, file_id)
    
    # 構造化テキストファイルのパスリスト
    structured_text_paths = []
    
    # 各画像をClaudeに送信して構造化テキストを取得
    for i, image_path in enumerate(image_paths):
        page_number = i + 1
        print(f"ページ {page_number}/{len(image_paths)} の処理中...")
        
        # Claudeを使用してテキストを構造化
        structured_text = structure_text_with_claude(image_path, page_number, api_key)
        
        # 構造化テキストをファイルに保存
        output_text_path = os.path.join(tmp_dir, f"{file_id}-page-{page_number}.txt")
        with open(output_text_path, "w", encoding="utf-8") as f:
            f.write(structured_text)
        
        structured_text_paths.append(output_text_path)
        print(f"ページ {page_number} の構造化テキストを保存: {output_text_path}")
    
    print(f"処理完了: {len(structured_text_paths)}ページの構造化テキストを生成しました")
    
    # 結果のJSONを出力（後続の処理用）
    result = {
        "fileId": file_id,
        "totalPages": len(structured_text_paths),
        "structuredTextPaths": structured_text_paths
    }
    
    # 結果をJSON形式で出力
    print(json.dumps(result))

if __name__ == "__main__":
    main()