# workid_management.json 設定ガイド

## 概要

`workid_management.json` は、ユーザーごとのファイルアクセス権限とworkId管理を行うための重要なファイルです。

## 初期設定

### 1. サンプルファイルのコピー

```bash
cp workid_management.json.example workid_management.json
```

### 2. ファイルの配置場所

アプリケーションは以下の優先順位でファイルを探します：

1. `~/.azurerag/workid_management.json` （推奨）
2. システム一時ディレクトリ: `%TEMP%/azurerag/workid_management.json`
3. `storage/workid_management.json`
4. プロジェクトルート: `workid_management.json`

### 3. ファイルが存在しない場合

アプリケーションは自動的に新規ファイルを作成します。

## データ構造

### users オブジェクト

各ユーザーのアクセス権限を定義します。

```json
{
  "users": {
    "username": {
      "username": "ユーザー名",
      "allowedWorkIds": ["アクセス可能なworkIdのリスト"],
      "role": "Admin または User",
      "lastUpdated": "最終更新日時（ISO 8601形式）"
    }
  }
}
```

#### role の種類

- `Admin`: すべてのworkIdにアクセス可能（`allowedWorkIds: ["*"]`）
- `User`: `allowedWorkIds` で指定されたworkIdのみアクセス可能

### workIds オブジェクト

各workId（ファイル識別子）の情報を定義します。

```json
{
  "workIds": {
    "workid-hash": {
      "name": "ファイルの表示名",
      "originalFileName": "元のファイル名（オプション）",
      "savedRelativePath": "保存パス（オプション）",
      "savedAt": "保存日時（オプション）",
      "savedFileSize": "ファイルサイズ（オプション）"
    }
  }
}
```

## 運用上の注意

### ファイルアップロード時の自動登録

ユーザーがファイルをアップロードすると、以下の処理が自動的に行われます：

1. ファイルにユニークなworkIdが割り当てられる
2. ユーザーの `allowedWorkIds` にworkIdが追加される
3. `workIds` にファイル情報が追加される
4. ファイルが自動保存される

### 動的更新

- このファイルは実行時に動的に更新されます
- `FileSystemWatcher` により、外部からの変更も自動的に反映されます
- 変更時は自動的にバックアップが作成されます

### バックアップ

アプリケーションは自動的に以下のバックアップを作成します：

- `workid_management.json.backup`

## セキュリティ

- このファイルには機密情報（ユーザー名、アクセス権限）が含まれます
- **Gitリポジトリにコミットしないでください**（`.gitignore` に追加済み）
- 本番環境では適切なファイル権限を設定してください

## トラブルシューティング

### ファイルが見つからない場合

アプリケーションログを確認してください：

```
書き込み可能なパスを発見: {Path}
```

### 権限エラーが発生する場合

1. ファイルの読み書き権限を確認
2. ディレクトリの書き込み権限を確認
3. 別の配置場所を試す

### データが反映されない場合

1. ファイルのJSON構文が正しいか確認
2. アプリケーションを再起動
3. バックアップから復元

## 例

### 管理者ユーザーの追加

```json
{
  "users": {
    "admin": {
      "username": "admin",
      "allowedWorkIds": ["*"],
      "role": "Admin",
      "lastUpdated": "2025-01-01T00:00:00Z"
    }
  }
}
```

### 一般ユーザーの追加

```json
{
  "users": {
    "user1": {
      "username": "user1",
      "allowedWorkIds": [
        "abc123",
        "def456"
      ],
      "role": "User",
      "lastUpdated": "2025-01-01T00:00:00Z"
    }
  }
}
```

### 複数ユーザー間でのファイル共有

同じworkIdを複数ユーザーの `allowedWorkIds` に追加することで、ファイル共有が可能です：

```json
{
  "users": {
    "user1": {
      "username": "user1",
      "allowedWorkIds": ["shared-doc-123"],
      "role": "User",
      "lastUpdated": "2025-01-01T00:00:00Z"
    },
    "user2": {
      "username": "user2",
      "allowedWorkIds": ["shared-doc-123"],
      "role": "User",
      "lastUpdated": "2025-01-01T00:00:00Z"
    }
  },
  "workIds": {
    "shared-doc-123": {
      "name": "共有ドキュメント"
    }
  }
}
```

