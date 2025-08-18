#!/bin/bash
set -e

# ==============================
# Replit専用デプロイスクリプト
# 環境変数を直接設定して.envファイルに依存しない
# ==============================

echo "=== Replit直接デプロイスクリプト ==="
echo "このスクリプトはReplit環境専用で、環境変数を直接設定します"

# 環境変数を直接設定
export APP_BASE_PATH="/demo-app2"
export ASPNETCORE_ENVIRONMENT="Production"
export REPLIT_DEPLOYMENT="true"

# 1. ヘルスチェック設定確認
echo "ヘルスチェックエンドポイント設定を確認中..."
if grep -q "/demo-app2/health" Program.cs; then
echo "✅ Replit用ヘルスチェック設定済み (/demo-app2/health専用)"
echo "指定されたヘルスチェックエンドポイント: https://[replit-domain]/demo-app2/health"
else
  echo "❌ Replit用ヘルスチェック設定が見つかりません"
  echo "Program.csを適切に修正してください"
  exit 1
fi

# 2. 直接実行コマンド
echo "アプリケーションを直接実行します..."
echo "起動コマンド: dotnet run --urls=\"http://0.0.0.0:5000\" --environment Production"
echo "====================================="

# 3. アプリケーション実行
dotnet run --urls="http://0.0.0.0:5000" --environment Production