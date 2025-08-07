import os
import sys
import json
from datetime import datetime

def main():
    if len(sys.argv) < 2:
        print("使用方法: python process_pdf.py <PDFファイルパス> [<ファイルID>]")
        sys.exit(1)
    
    pdf_path = sys.argv[1]
    
    # ファイルIDが指定されていなければ引数から取得、なければファイル名から生成
    file_id = sys.argv[2] if len(sys.argv) > 2 else os.path.basename(pdf_path).split('.')[0]
    
    print(f"処理するPDFファイル: {pdf_path}")
    print(f"ファイルID: {file_id}")
    
    # PDFファイルが存在するか確認
    if not os.path.exists(pdf_path):
        print(f"エラー: PDFファイル '{pdf_path}' が見つかりません")
        sys.exit(1)
    
    # pdf_image_extractor.pyを実行
    cmd = f"python pdf_image_extractor.py \"{pdf_path}\" \"{file_id}\""
    print(f"コマンド実行: {cmd}")
    os.system(cmd)

if __name__ == "__main__":
    main()