#!/bin/bash

# 改良版デプロイスクリプト
# デプロイ失敗を防ぐため、タイムアウト対策とエラーハンドリングを強化

echo "=== 改良版デプロイスクリプト実行開始 ==="
echo "実行環境: $(uname -a)"
echo "カレントディレクトリ: $(pwd)"

# すべてのdotnetプロセスを終了
echo "既存のdotnetプロセスを終了します..."
pkill -f "dotnet" || true

# 事前に存在するビルド済みDLLを使用して直接起動
if [ -f bin/Release/net8.0/AzureRag.dll ]; then
  echo "既存のリリースビルドを使用します"
  
  # 環境変数の設定
  export ASPNETCORE_ENVIRONMENT=Production
  export DOTNET_RUNNING_IN_CONTAINER=true
  export APP_BASE_PATH="/trial-app1"
  
  # 環境変数ファイルが存在する場合は読み込む
  if [ -f .env ]; then
    echo "環境変数ファイルを読み込みます"
    source .env
  fi
  
  # 環境変数は .env または systemd EnvironmentFile から読み込む前提に統一
  
  echo "アプリケーションを起動します..."
  exec dotnet bin/Release/net8.0/AzureRag.dll --urls="http://0.0.0.0:5000"
else
  echo "リリースビルドが見つかりません。最小限のビルドを実行します..."
  
  # 最小限のビルドを実行
  dotnet build --configuration Release --no-restore || {
    echo "ビルドに失敗しました。既存のDLLを探します..."
    find / -name AzureRag.dll 2>/dev/null | grep Release
    
    if [ -f bin/Release/net8.0/AzureRag.dll ]; then
      echo "DLLが見つかりました。起動を試みます..."
      export ASPNETCORE_ENVIRONMENT=Production
      export DOTNET_RUNNING_IN_CONTAINER=true
      export APP_BASE_PATH="/trial-app1"
      
      # 環境変数ファイルが存在する場合は読み込む
      if [ -f .env ]; then
        echo "環境変数ファイルを読み込みます"
        source .env
      fi
      
      # 環境変数は .env または systemd EnvironmentFile から読み込む前提に統一
      
      exec dotnet bin/Release/net8.0/AzureRag.dll --urls="http://0.0.0.0:5000"
    else
      echo "起動可能なDLLが見つかりません。デプロイに失敗しました。"
      exit 1
    fi
  }
fi

echo "=== デプロイスクリプト終了 ==="