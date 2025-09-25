#!/bin/bash

# ALB設定をシミュレートするための開発環境スクリプト
# このスクリプトは開発環境でALBのベースパス設定をシミュレートします
# 使用方法: ./dev_alb_simulate.sh

# ベースパスを設定 - ALBの設定に合わせる
export APP_BASE_PATH="/dev-app1"
echo "ベースパス設定: $APP_BASE_PATH"

# アプリケーションを実行
echo "ALB設定をシミュレートしてアプリケーションを起動します..."
echo "アプリケーションは次のURLでアクセスできます: http://localhost:5000$APP_BASE_PATH"
echo ""

# 本番環境フラグを設定（オプション）
export ASPNETCORE_ENVIRONMENT=Production
export DOTNET_RUNNING_IN_CONTAINER=true

# アプリケーションを起動
dotnet run --urls=http://0.0.0.0:5000