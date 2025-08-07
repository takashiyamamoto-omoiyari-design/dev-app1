# 環境変数設定ガイド

このアプリケーションでは、機密情報（APIキー、パスワード等）を環境変数で管理しています。

## 設定手順

### 1. 環境変数ファイルの準備

```bash
# config.env を .env にコピー
cp config.env .env
```

### 2. 実際の値を設定

`.env`ファイルを編集して、以下の項目に実際の値を設定してください：

#### 必須設定項目

- `MSPSeimei__AzureSearchKey` - Azure Search のアクセスキー
- `MSPSeimei__AzureOpenAIKey` - Azure OpenAI のAPIキー
- `DataIngestion__AzureSearchKey` - データ取り込み用 Azure Search キー
- `DataIngestion__AzureOpenAIKey` - データ取り込み用 Azure OpenAI キー
- `Users__admin__Password` - 管理者パスワード

#### オプション設定項目

- AWS関連の設定（Claude使用時）
- 外部API設定
- その他のユーザーパスワード

### 3. デプロイ実行

```bash
# デプロイスクリプトが自動的に .env ファイルを読み込みます
./deploy.sh
```

## セキュリティ注意事項

- `.env` ファイルは `.gitignore` に含まれており、Gitにコミットされません
- `config.env` ファイルにも実際の機密情報は記載しないでください
- 本番環境では適切なアクセス権限を設定してください

## トラブルシューティング

### 環境変数が読み込まれない場合

1. `.env` ファイルが正しい場所（プロジェクトルート）にあることを確認
2. ファイル内の形式が正しいことを確認（`KEY=value` 形式）
3. デプロイスクリプトが `.env` を読み込んでいることを確認

### 認証エラーが発生する場合

1. APIキーが正しく設定されていることを確認
2. キーの有効期限を確認
3. Azure/AWS のサービスが有効になっていることを確認

## 環境変数の形式

ASP.NET Core では、設定の階層構造を `__` （アンダースコア2つ）で表現します：

```bash
# appsettings.json の構造:
# {
#   "MSPSeimei": {
#     "AzureSearchKey": "value"
#   }
# }

# 環境変数での設定:
MSPSeimei__AzureSearchKey=actual_value
```