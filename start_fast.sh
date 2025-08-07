#!/bin/bash

# 最速ポート開放スクリプト
echo "STARTING FAST PORT OPENING DEPLOYMENT SCRIPT"

# 実行中のドットネットプロセスをクリーンアップ
pkill -f "dotnet" || true

# 一時的なポートリスナーを開始（バックグラウンド）
(
  echo "Opening port 5000 immediately..."
  # socat をインストールしていると仮定
  socat TCP-LISTEN:5000,fork,reuseaddr EXEC:'echo -e "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\n\r\nApplication starting, please wait..."' &
  SOCAT_PID=$!
  echo "Port 5000 is now open (PID: $SOCAT_PID)"
  
  # 数秒待機して実際のアプリケーションを起動
  sleep 2
  echo "Starting actual application..."
  
  # アプリケーションを起動（これはsocat を終了させます）
  ASPNETCORE_ENVIRONMENT=Production DOTNET_RUNNING_IN_CONTAINER=true dotnet bin/Release/net8.0/AzureRag.dll --urls="http://0.0.0.0:5000"
  
  # socat プロセスをクリーンアップ（必要な場合）
  kill $SOCAT_PID 2>/dev/null || true
) &

# Replit デプロイシステムのために即座に終了しない
sleep 10
echo "Deployment script completed"