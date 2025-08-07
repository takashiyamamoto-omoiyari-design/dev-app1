import requests
import json
import sys
from datetime import datetime
import re

# 出力エンコーディングを明示
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

# プライベートIPを使用したエンドポイント
API_URL = "http://10.24.128.5:51000/AutoStructure/Check"
USER_ID = "goto"
PASSWORD = "ilupass3"
WORK_ID = "ff3bfb43437a02fde082fdc2af4a90e8"  # 現在の値を継続利用

def extract_page_number(text):
    # テキストからページ番号を抽出する関数
    match = re.search(r'p\.(\d+)', text)
    if match:
        return int(match.group(1))
    return float('inf')  # ページ番号が見つからない場合は最後に配置

def main():
    # 出力ファイル名を現在時刻で生成
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    output_file = f"result_private_{timestamp}.txt"
    
    data = {
        "work_id": WORK_ID,
        "userId": USER_ID,
        "password": PASSWORD
    }
    headers = {"Content-Type": "application/json"}
    
    try:
        response = requests.post(API_URL, headers=headers, data=json.dumps(data))
        
        with open(output_file, 'w', encoding='utf-8') as f:
            f.write(f"ステータスコード: {response.status_code}\n")
            try:
                resp_json = response.json()
                # text_listの表示
                if "text_list" in resp_json:
                    f.write("\n=== text_list ===\n")
                    # ページ番号でソート
                    sorted_texts = sorted(resp_json["text_list"], 
                                       key=lambda x: extract_page_number(x.get("text", "")))
                    for item in sorted_texts:
                        text = item.get("text", "")
                        # ダブルコーテーションを除去
                        text = text.strip('"')
                        f.write(f"{text}\n")
                else:
                    f.write("text_listがレスポンスに含まれていません。\n")
                
                # synonym_listの表示
                if "synonym_list" in resp_json:
                    f.write("\n=== synonym_list ===\n")
                    for item in resp_json["synonym_list"]:
                        synonym = item.get("synonym", [])
                        # リスト内の各要素からダブルコーテーションを除去
                        synonym = [s.strip('"') for s in synonym]
                        f.write(f"{', '.join(synonym)}\n")
                else:
                    f.write("synonym_listがレスポンスに含まれていません。\n")
            except Exception as e:
                f.write(f"デコード処理中にエラー: {e}\n")
            f.write(f"\n=== 完全なレスポンス内容 ===\n{response.text}\n")
        
        print(f"結果を {output_file} に保存しました。")
        
    except requests.RequestException as e:
        print(f"リクエストエラー: {e}")

if __name__ == "__main__":
    main() 