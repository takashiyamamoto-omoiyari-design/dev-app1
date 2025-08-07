import os
import sys
import json
import pymupdf

def main():
    if len(sys.argv) < 2:
        print("使用方法: python extract_pdf_text.py <PDFファイルパス> <ファイルID>")
        sys.exit(1)
    
    pdf_path = sys.argv[1]
    file_id = sys.argv[2] if len(sys.argv) > 2 else os.path.basename(pdf_path).split('.')[0]
    
    # 出力ディレクトリ
    tmp_dir = "storage/tmp"
    os.makedirs(tmp_dir, exist_ok=True)
    
    print(f"PDFファイル '{pdf_path}' からテキストを抽出します...")
    
    try:
        # PyMuPDFを使用してPDFを開く
        doc = pymupdf.open(pdf_path)
        
        # ページ数を表示
        page_count = len(doc)
        print(f"PDFページ数: {page_count}")
        
        # 各ページのテキストを抽出して保存
        for page_num in range(page_count):
            # ページオブジェクトを取得
            page = doc.load_page(page_num)
            
            # テキストを抽出
            text = page.get_text()
            
            # ページ番号（1から始まる）
            page_number = page_num + 1
            
            # テキストをファイルに保存
            output_path = os.path.join(tmp_dir, f"{file_id}-page-{page_number}.txt")
            with open(output_path, "w", encoding="utf-8") as f:
                f.write(text)
            
            print(f"ページ {page_number}/{page_count} のテキストを保存: {output_path}")
        
        print(f"処理完了: {page_count}ページのテキストを抽出しました")
        
    except Exception as e:
        print(f"エラーが発生しました: {str(e)}")
        sys.exit(1)

if __name__ == "__main__":
    main()