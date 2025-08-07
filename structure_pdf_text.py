import os
import sys
import json
import anthropic
from concurrent.futures import ThreadPoolExecutor, as_completed

def structure_text_with_claude(text_path, api_key):
    """Claudeを使用してテキストを構造化します"""
    page_number = os.path.basename(text_path).replace('.txt', '').split('-')[-1]
    print(f"ページ {page_number} のテキストを構造化中...")
    
    # テキストファイルを読み込み
    with open(text_path, 'r', encoding='utf-8') as f:
        text_content = f.read()
    
    # Anthropicクライアントを初期化
    client = anthropic.Anthropic(api_key=api_key)
    
    # システムプロンプト
    system_prompt = """
    あなたはPDFから抽出されたテキストを整理し構造化する専門家です。
    
    【重要なルール】
    1. 抽出されたテキストを読みやすく整理してください
    2. 段落、見出し、箇条書きなどの構造を適切に整えてください
    3. テキストの内容は変更せず、原文の意味をそのまま維持してください
    4. 不要な空白や改行を整理し、読みやすいフォーマットにしてください
    5. レイアウト崩れによる文章の断片は適切につなげてください
    6. テキストが乱れて意味が不明な場合は「[判読不能テキスト]」と記載してください
    """
    
    # ユーザープロンプト
    user_prompt = f"""
    以下はPDFから抽出されたテキスト（ページ{page_number}）です。このテキストを読みやすく構造化してください。
    段落や見出し、箇条書きなどの構造を整え、不要な空白や改行を整理してください。
    レイアウト崩れによる文章の断片は適切につなげてください。
    
    抽出テキスト:
    {text_content}
    """
    
    # Claudeに送信
    response = client.messages.create(
        model="claude-3-7-sonnet-20250219",
        system=system_prompt,
        messages=[{"role": "user", "content": user_prompt}],
        max_tokens=10000,
    )
    
    # レスポンスからテキストを取得
    structured_text = response.content[0].text
    
    return structured_text, page_number

def process_text_files(input_dir, output_dir, file_id, api_key, max_workers=3):
    """指定されたディレクトリ内の全テキストファイルを処理します"""
    # 出力ディレクトリが存在することを確認
    os.makedirs(output_dir, exist_ok=True)
    
    # 処理対象のファイルを検索
    files_to_process = []
    for file in os.listdir(input_dir):
        if file.startswith(f"{file_id}-page-") and file.endswith(".txt"):
            files_to_process.append(os.path.join(input_dir, file))
    
    # ファイルをページ番号でソート
    files_to_process.sort(key=lambda x: int(os.path.basename(x).replace('.txt', '').split('-')[-1]))
    
    print(f"処理するファイル数: {len(files_to_process)}")
    
    # 結果を格納するリスト
    structured_file_paths = []
    
    # 並列処理でファイルを処理
    with ThreadPoolExecutor(max_workers=max_workers) as executor:
        # 各ファイルに対してClaudeでテキスト構造化を実行
        future_to_file = {executor.submit(structure_text_with_claude, file_path, api_key): file_path for file_path in files_to_process}
        
        for future in as_completed(future_to_file):
            file_path = future_to_file[future]
            try:
                structured_text, page_number = future.result()
                
                # 構造化されたテキストを保存
                output_path = os.path.join(output_dir, f"{file_id}-page-{page_number}.txt")
                with open(output_path, 'w', encoding='utf-8') as f:
                    f.write(structured_text)
                
                structured_file_paths.append(output_path)
                print(f"ページ {page_number} の構造化テキストを保存: {output_path}")
                
            except Exception as e:
                print(f"ファイル {file_path} の処理中にエラー発生: {str(e)}")
    
    return structured_file_paths

def main():
    if len(sys.argv) < 2:
        print("使用方法: python structure_pdf_text.py <ファイルID>")
        sys.exit(1)
    
    file_id = sys.argv[1]
    
    # 環境変数からAPIキーを取得
    api_key = os.environ.get("ANTHROPIC_API_KEY")
    if not api_key:
        print("環境変数ANTHROPIC_API_KEYが設定されていません")
        sys.exit(1)
    
    # 入力/出力ディレクトリ
    input_dir = "storage/tmp"
    
    # すべてのファイルを処理
    print(f"ファイルID '{file_id}' のテキストファイルを構造化します...")
    structured_file_paths = process_text_files(input_dir, input_dir, file_id, api_key)
    
    print(f"処理完了: {len(structured_file_paths)}ページの構造化テキストを生成しました")
    
    # 結果のJSONを出力（後続の処理用）
    result = {
        "fileId": file_id,
        "totalPages": len(structured_file_paths),
        "structuredTextPaths": structured_file_paths
    }
    
    # 結果をJSON形式で出力
    print(json.dumps(result))

if __name__ == "__main__":
    main()