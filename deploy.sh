#!/bin/bash

# 元の.replitファイルの設定（バックアップ）
# [deployment]
# run = ["sh", "-c", "dotnet run --urls=&quot;http://0.0.0.0:5000&quot; --environment Production"]

# 実行環境を出力
echo "実行環境: $(uname -a)"
echo "カレントディレクトリ: $(pwd)"
echo "ファイル一覧: $(ls -la)"

# アプリケーションをビルド
echo "アプリケーションをビルドしています..."
dotnet build --configuration Release

# アプリケーションを実行
echo "アプリケーションを起動しています..."
# 環境変数設定が必要な場合に備えて、ファイルが存在するか確認
if [ -f .env ]; then
  echo "環境変数ファイルが見つかりました。読み込みます..."
  source .env
fi

# ベースパス設定を確保
if [ -z "$APP_BASE_PATH" ]; then
  echo "APP_BASE_PATH環境変数が未設定のため、/trial-app1を設定します"
export APP_BASE_PATH="/trial-app1"
fi
echo "ベースパス設定: $APP_BASE_PATH"

# プロジェクトファイルがあるパスを確認
if [ -f AzureRag.csproj ]; then
  echo "プロジェクトファイルを発見: AzureRag.csproj"
  APP_BASE_PATH="$APP_BASE_PATH" ASPNETCORE_ENVIRONMENT=Production DOTNET_RUNNING_IN_CONTAINER=true dotnet run --project AzureRag.csproj --urls=http://0.0.0.0:5000
else
  echo "デフォルトプロジェクトとして実行します..."
  APP_BASE_PATH="$APP_BASE_PATH" ASPNETCORE_ENVIRONMENT=Production DOTNET_RUNNING_IN_CONTAINER=true dotnet run --urls=http://0.0.0.0:5000
fi