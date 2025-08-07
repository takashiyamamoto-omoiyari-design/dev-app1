#!/usr/bin/env python3
# -*- coding: utf-8 -*-

"""
PDF to Images コンバーター
PDFファイルの各ページを個別の画像ファイルに変換します。
"""

import os
import sys
import argparse
from pdf2image import convert_from_path
from PIL import Image

def convert_pdf_to_images(pdf_path, output_dir, file_id, original_filename=None, dpi=200, output_format='png'):
    """
    PDFファイルの各ページを画像に変換し、指定されたディレクトリに保存します。
    
    Parameters:
    -----------
    pdf_path : str
        変換するPDFファイルのパス
    output_dir : str
        画像を保存するディレクトリ
    file_id : str
        ファイルの一意識別子（出力ファイル名の一部として使用）
    original_filename : str, optional
        元のPDFファイル名（ファイル名の一部として使用）
    dpi : int, optional
        画像の解像度（デフォルト: 200）
    output_format : str, optional
        出力画像のフォーマット（デフォルト: 'png'）
    
    Returns:
    --------
    list
        生成された画像ファイルのパスのリスト
    """
    # 出力ディレクトリが存在しない場合は作成
    os.makedirs(output_dir, exist_ok=True)
    
    # フォーマットの小文字化（拡張子として使用）
    output_format = output_format.lower()
    
    try:
        # PDFファイルをページごとに画像に変換
        print(f"PDFファイル '{pdf_path}' の変換を開始します...")
        images = convert_from_path(pdf_path, dpi=dpi)
        
        # 各ページを保存
        image_paths = []
        for i, image in enumerate(images):
            # ページ番号は1から始まる
            page_num = i + 1
            
            # 元のファイル名が指定されている場合はそれを使用
            if original_filename:
                filename = f"{original_filename}_page_{page_num}"
            else:
                filename = f"{file_id}_page_{page_num}"
                
            # ファイルパスを作成
            image_path = os.path.join(output_dir, f"{filename}.{output_format}")
            
            # 画像を保存
            image.save(image_path, format=output_format.upper())
            image_paths.append(image_path)
            print(f"ページ {page_num}/{len(images)} を変換しました: {os.path.basename(image_path)}")
        
        print(f"変換完了: {len(image_paths)} ページを画像に変換しました。")
        return image_paths
    
    except Exception as e:
        print(f"エラー: PDFの変換に失敗しました: {str(e)}", file=sys.stderr)
        return []

def main():
    # コマンドライン引数の解析
    parser = argparse.ArgumentParser(description='PDFファイルをページごとに画像に変換します')
    parser.add_argument('pdf_path', help='変換するPDFファイルのパス')
    parser.add_argument('output_dir', help='画像の出力ディレクトリ')
    parser.add_argument('file_id', help='ファイルID（出力ファイル名の一部として使用）')
    parser.add_argument('original_filename', nargs='?', default=None, help='元のPDFファイル名（ファイル名の一部として使用）')
    parser.add_argument('--dpi', type=int, default=200, help='出力画像の解像度（デフォルト: 200）')
    parser.add_argument('--format', default='png', choices=['png', 'jpg', 'jpeg', 'tiff'], help='出力画像のフォーマット（デフォルト: png）')
    
    args = parser.parse_args()
    
    # 元のファイル名を取得
    original_filename = args.original_filename
    if original_filename:
        print(f"元のPDFファイル名: {original_filename}")
    
    # PDFを画像に変換
    image_paths = convert_pdf_to_images(
        args.pdf_path, 
        args.output_dir, 
        args.file_id,
        original_filename,
        dpi=args.dpi,
        output_format=args.format
    )
    
    # 結果の出力
    if image_paths:
        print(f"{len(image_paths)}ページの画像を生成しました")
        return 0
    else:
        print("画像の生成に失敗しました", file=sys.stderr)
        return 1

if __name__ == "__main__":
    sys.exit(main())