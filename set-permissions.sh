#!/bin/bash

# Demo App 2 権限設定スクリプト
echo "=== Demo App 2 権限設定開始 ==="

# 基本パス
BASE_PATH="/opt/app/dev-app1/publish"
STORAGE_PATH="${BASE_PATH}/storage"

echo "アプリケーション実行ユーザーを確認中..."
APP_USER=$(ps aux | grep -E '[d]otnet.*dev-app1' | awk '{print $1}' | head -1)

if [ -z "$APP_USER" ]; then
    echo "アプリケーションが実行されていないため、ec2-userを使用します"
    APP_USER="ec2-user"
else
    echo "検出されたアプリケーション実行ユーザー: $APP_USER"
fi

echo "必要なディレクトリを作成中..."
sudo mkdir -p "${STORAGE_PATH}/files"
sudo mkdir -p "${STORAGE_PATH}/fileinfo"
sudo mkdir -p "${STORAGE_PATH}/chat_sessions"
sudo mkdir -p "${STORAGE_PATH}/tmp"
sudo mkdir -p "${STORAGE_PATH}/images"
sudo mkdir -p "${STORAGE_PATH}/chunks"
sudo mkdir -p "${STORAGE_PATH}/reinforcement/jsonl"
sudo mkdir -p "${STORAGE_PATH}/reinforcement/prompts"
sudo mkdir -p "${STORAGE_PATH}/reinforcement/responses"
sudo mkdir -p "${STORAGE_PATH}/reinforcement/evaluations"
sudo mkdir -p "${BASE_PATH}/indexes"

echo "所有者とグループを設定中..."
sudo chown -R "${APP_USER}:${APP_USER}" "${STORAGE_PATH}"
sudo chown -R "${APP_USER}:${APP_USER}" "${BASE_PATH}/indexes"

echo "権限を設定中..."
sudo chmod -R 755 "${STORAGE_PATH}"
sudo chmod -R 755 "${BASE_PATH}/indexes"

echo "権限設定結果を確認中..."
ls -la "${STORAGE_PATH}"
ls -la "${BASE_PATH}"

echo "=== Demo App 2 権限設定完了 ==="
echo "実行ユーザー: ${APP_USER}"
echo "ストレージパス: ${STORAGE_PATH}"
echo ""
echo "サービスを再起動してください:"
echo "sudo systemctl restart dev-app1" 