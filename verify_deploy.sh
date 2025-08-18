#!/bin/bash

# Replitデプロイ検証用スクリプト
echo "=== Replitデプロイ環境検証スクリプト ==="

# 環境変数の設定
export ASPNETCORE_ENVIRONMENT=Production
export DOTNET_RUNNING_IN_CONTAINER=true
export APP_BASE_PATH="/demo-app2"

# 環境変数の表示
echo "環境変数設定:"
echo "APP_BASE_PATH=$APP_BASE_PATH"
echo "ASPNETCORE_ENVIRONMENT=$ASPNETCORE_ENVIRONMENT"
echo "DOTNET_RUNNING_IN_CONTAINER=$DOTNET_RUNNING_IN_CONTAINER"

# ベースパスの設定検証
echo ""
echo "Programクラスでのベースパス設定検証:"
grep -A 10 "app.UsePathBase" Program.cs

# 該当箇所の表示
echo ""
echo "プログラム内でのベースパス使用箇所:"
grep -n "basePath" Program.cs

echo ""
echo "=== 検証完了 ==="