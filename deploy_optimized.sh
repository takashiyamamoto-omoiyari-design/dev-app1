#!/bin/bash

# デプロイ最適化スクリプト - ポート開放を最速化

echo "=== デプロイ最適化スクリプト実行中 ==="

# リリースビルドを作成
echo "リリースビルドを作成しています..."
dotnet build AzureRag.csproj -c Release

# 既存プロセスをクリーンアップ
echo "既存のドットネットプロセスをクリーンアップしています..."
pkill -f "dotnet" || true

# 環境変数を設定してアプリケーションを起動
echo "最適化された設定でアプリケーションを起動します..."
ASPNETCORE_ENVIRONMENT=Production DOTNET_RUNNING_IN_CONTAINER=true dotnet bin/Release/net8.0/AzureRag.dll --urls="http://0.0.0.0:5000"

echo "=== デプロイスクリプト完了 ==="