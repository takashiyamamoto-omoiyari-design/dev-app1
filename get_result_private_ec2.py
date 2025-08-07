#!/usr/bin/env python3
import requests
import json
import sys
from datetime import datetime
import re
import argparse

# 出力エンコーディングを明示
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

# プライベートIPを使用したエンドポイント（同一VPC内）
API_URL = "http://10.24.128.70:51000/AutoStructure/Check"
DEFAULT_USER_ID = "goto"
DEFAULT_PASSWORD = "ilupass3"

def extract_page_number(text):
    # テキストからページ番号を抽出する関数
    match = re.search(r'p\.(\d+)', text)
    if match:
        return int(match.group(1))
    return float('inf')  # ページ番号が見つからない場合は最後に配置

def check_status(work_id, user_id, password):
    """
    APIエンドポイントに接続して結果を取得する
    """
    # 出力ファイル名を現在時刻で生成
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    output_file = f"result_ec2_{timestamp}.txt"
    
    data = {
        "work_id": work_id,
        "userid": user_id,
        "password": password
    }
    headers = {"Content-Type": "application/json"}
    
    try:
        print(f"APIエンドポイント {API_URL} に接続中...")
        print(f"リクエストデータ: {json.dumps(data, ensure_ascii=False)}")
        
        response = requests.post(API_URL, headers=headers, data=json.dumps(data))
        
        with open(output_file, 'w', encoding='utf-8') as f:
            f.write(f"ステータスコード: {response.status_code}\n")
            
            try:
                resp_json = response.json()
                # status情報の表示
                if "status" in resp_json:
                    f.write("\n=== ステータス情報 ===\n")
                    status_info = resp_json["status"]
                    if isinstance(status_info, list) and len(status_info) > 0:
                        f.write(f"処理状況: ページ {status_info[0].get('page_no', '不明')} / {status_info[0].get('max_page_no', '不明')}\n")
                    f.write("\n")
                
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
                
                # リターンコードの確認
                return_code = resp_json.get("return_code")
                if return_code is not None:
                    f.write(f"\n=== リターンコード ===\n")
                    f.write(f"return_code: {return_code}\n")
                    
                    if return_code == 0:
                        if not resp_json.get("text_list"):
                            f.write("処理中: 処理はまだ完了していません。後ほど再試行してください。\n")
                        else:
                            f.write("処理完了: 正常に結果を取得しました。\n")
                    else:
                        f.write(f"エラー詳細: {resp_json.get('error_detail', '詳細不明')}\n")
                        
            except Exception as e:
                f.write(f"デコード処理中にエラー: {e}\n")
                
            f.write(f"\n=== 完全なレスポンス内容 ===\n{response.text}\n")
        
        print(f"結果を {output_file} に保存しました。")
        return True
        
    except requests.RequestException as e:
        print(f"リクエストエラー: {e}")
        return False

def main():
    parser = argparse.ArgumentParser(description='AutoStructureのCheck APIにアクセスして結果を取得')
    parser.add_argument('work_id', help='取得する対象のワークID')
    parser.add_argument('--userid', default=DEFAULT_USER_ID, help=f'ユーザーID (デフォルト: {DEFAULT_USER_ID})')
    parser.add_argument('--password', default=DEFAULT_PASSWORD, help=f'パスワード (デフォルト: {DEFAULT_PASSWORD})')
    
    args = parser.parse_args()
    
    if not args.work_id:
        print("エラー: work_idを指定してください。")
        parser.print_help()
        return

    check_status(args.work_id, args.userid, args.password)

if __name__ == "__main__":
    main() 