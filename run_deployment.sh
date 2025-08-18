#!/bin/bash

# 最適化されたデプロイメントランナー
echo "### STARTING OPTIMIZED DEPLOYMENT RUNNER ###"

# リリースビルドを強制
echo "Building release version..."
dotnet build AzureRag.csproj -c Release

# 既存のドットネットプロセスをクリーンアップ
pkill -f "dotnet" || true

# 最適化された環境でアプリ起動
echo "Starting application with optimized settings..."
APP_BASE_PATH="/demo-app2" ASPNETCORE_ENVIRONMENT=Production DOTNET_RUNNING_IN_CONTAINER=true dotnet bin/Release/net8.0/AzureRag.dll --urls="http://0.0.0.0:5000"