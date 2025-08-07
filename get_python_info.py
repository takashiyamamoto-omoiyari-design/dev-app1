#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Pythonの環境情報とインストールされているライブラリのバージョンを出力するスクリプト
エラーハンドリングを強化し、I/Oエラーが発生しても最低限の情報を返す
"""

import sys
import platform
import json
import os
import traceback
import subprocess
from datetime import datetime

# 直接importを避け、try-exceptで囲む
try:
    import importlib
    from importlib.metadata import distributions
except ImportError:
    pass

# 使用する主要ライブラリのリスト
IMPORTANT_LIBRARIES = [
    "anthropic",
    "fitz",
    "pdf2image", 
    "pillow",
    "pymupdf",
    "requests"
]

def safe_execute(func):
    """関数を安全に実行するデコレータ"""
    def wrapper(*args, **kwargs):
        try:
            return func(*args, **kwargs)
        except Exception as e:
            error_info = {
                "error": str(e),
                "traceback": traceback.format_exc(),
                "timestamp": datetime.now().isoformat()
            }
            return {"error_occurred": True, "error_details": error_info}
    return wrapper

@safe_execute
def get_python_version():
    """Pythonのバージョン情報を取得"""
    result = {}
    
    # 個別に例外処理を行い、可能な限り情報を取得
    try:
        result["python_version"] = sys.version
    except:
        result["python_version"] = "取得エラー"
    
    try:
        result["python_implementation"] = platform.python_implementation()
    except:
        result["python_implementation"] = "取得エラー"
    
    try:
        result["platform"] = platform.platform()
    except:
        result["platform"] = "取得エラー"
    
    try:
        result["os_name"] = os.name
    except:
        result["os_name"] = "取得エラー"
    
    try:
        result["system"] = platform.system()
    except:
        result["system"] = "取得エラー"
    
    return result

@safe_execute
def get_pip_list():
    """pipコマンドを使用して直接パッケージリストを取得"""
    try:
        # pipを直接実行してパッケージ一覧を取得
        pip_output = subprocess.check_output([sys.executable, "-m", "pip", "list", "--format=json"], 
                                            stderr=subprocess.STDOUT, 
                                            universal_newlines=True)
        return json.loads(pip_output)
    except subprocess.CalledProcessError as e:
        return {"pip_error": e.output}
    except Exception as e:
        return {"error": str(e)}

@safe_execute
def get_library_versions():
    """主要ライブラリのバージョン情報を取得"""
    library_versions = {}
    
    # pip listを使用してパッケージ情報を取得
    pip_packages = get_pip_list()
    if isinstance(pip_packages, dict) and "error" in pip_packages:
        # pip listが失敗した場合、別の方法を試みる
        for lib in IMPORTANT_LIBRARIES:
            try:
                # 各ライブラリをインポートしてみる
                module = __import__(lib, fromlist=[''])
                if hasattr(module, "__version__"):
                    library_versions[lib] = module.__version__
                elif hasattr(module, "version"):
                    library_versions[lib] = module.version
                else:
                    library_versions[lib] = "インストール済み (バージョン不明)"
            except ImportError:
                library_versions[lib] = "インポートエラー"
            except Exception as e:
                library_versions[lib] = f"エラー: {str(e)}"
    else:
        # pip listが成功した場合、結果を使用
        pip_dict = {pkg["name"].lower(): pkg["version"] for pkg in pip_packages}
        
        for lib in IMPORTANT_LIBRARIES:
            lib_lower = lib.lower()
            if lib_lower in pip_dict:
                library_versions[lib] = pip_dict[lib_lower]
            else:
                library_versions[lib] = "見つかりません"
    
    return library_versions

@safe_execute
def check_file_permissions():
    """ファイルシステムのパーミッションをチェック"""
    permissions = {}
    
    # スクリプトディレクトリの権限をチェック
    try:
        script_dir = os.path.dirname(os.path.abspath(__file__))
        permissions["script_dir"] = {
            "path": script_dir,
            "readable": os.access(script_dir, os.R_OK),
            "writable": os.access(script_dir, os.W_OK),
            "executable": os.access(script_dir, os.X_OK)
        }
    except:
        permissions["script_dir"] = "取得エラー"
    
    # /tmpディレクトリの権限をチェック
    try:
        tmp_dir = "/tmp"
        if os.path.exists(tmp_dir):
            permissions["tmp_dir"] = {
                "readable": os.access(tmp_dir, os.R_OK),
                "writable": os.access(tmp_dir, os.W_OK),
                "executable": os.access(tmp_dir, os.X_OK)
            }
    except:
        permissions["tmp_dir"] = "取得エラー"
    
    return permissions

def main():
    """メイン処理"""
    try:
        # 結果を格納する辞書
        result = {
            "timestamp": datetime.now().isoformat(),
            "system_info": None,
            "library_versions": None,
            "file_permissions": None
        }
        
        # 段階的に情報を収集し、エラーが発生しても続行
        result["system_info"] = get_python_version()
        result["library_versions"] = get_library_versions()
        result["file_permissions"] = check_file_permissions()
        
        # JSON形式で出力
        print(json.dumps(result, indent=2, ensure_ascii=False))
        
    except Exception as e:
        # 致命的なエラーが発生した場合でも最低限の情報を出力
        error_result = {
            "fatal_error": True,
            "error_message": str(e),
            "traceback": traceback.format_exc(),
            "timestamp": datetime.now().isoformat()
        }
        print(json.dumps(error_result, indent=2, ensure_ascii=False))

if __name__ == "__main__":
    main()