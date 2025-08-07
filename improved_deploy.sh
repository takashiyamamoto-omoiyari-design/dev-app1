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
  
  # MSPSeimei関連の環境変数を明示的に設定
  export MSPSeimei__AzureSearchEndpoint="https://iluragsearch.search.windows.net"
  export MSPSeimei__AzureSearchKey="Gt6kwOhyZKs0ICnOV17JBljGDwiacTHxKstsIaS7BDAzSeCR55N9"
  export MSPSeimei__AzureSearchApiVersion="2020-06-30"
  export MSPSeimei__MainIndexName="mspseimei"
  export MSPSeimei__SentenceIndexName="mspseimei-sentence"
  export MSPSeimei__AzureOpenAIEndpoint="https://openai-gpt-forrag.openai.azure.com"
  export MSPSeimei__AzureOpenAIKey="5c05fc27ca534abbae6a6cee6d9d0b41"
  export MSPSeimei__AzureOpenAIApiVersion="2023-05-15"
  export MSPSeimei__ChatModelDeployment="gpt4o-forRAG"
  export MSPSeimei__EmbeddingModelDeployment="text-embedding-3-large"
  
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
      
      # MSPSeimei関連の環境変数を明示的に設定
      export MSPSeimei__AzureSearchEndpoint="https://iluragsearch.search.windows.net"
      export MSPSeimei__AzureSearchKey="Gt6kwOhyZKs0ICnOV17JBljGDwiacTHxKstsIaS7BDAzSeCR55N9"
      export MSPSeimei__AzureSearchApiVersion="2020-06-30"
      export MSPSeimei__MainIndexName="mspseimei"
      export MSPSeimei__SentenceIndexName="mspseimei-sentence"
      export MSPSeimei__AzureOpenAIEndpoint="https://openai-gpt-forrag.openai.azure.com"
      export MSPSeimei__AzureOpenAIKey="5c05fc27ca534abbae6a6cee6d9d0b41"
      export MSPSeimei__AzureOpenAIApiVersion="2023-05-15"
      export MSPSeimei__ChatModelDeployment="gpt4o-forRAG"
      export MSPSeimei__EmbeddingModelDeployment="text-embedding-3-large"
      
      exec dotnet bin/Release/net8.0/AzureRag.dll --urls="http://0.0.0.0:5000"
    else
      echo "起動可能なDLLが見つかりません。デプロイに失敗しました。"
      exit 1
    fi
  }
fi

echo "=== デプロイスクリプト終了 ==="