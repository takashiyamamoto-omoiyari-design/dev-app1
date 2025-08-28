using System.Collections.Generic;
using System.Threading.Tasks;
using AzureRag.Models;

namespace AzureRag.Services
{
    public interface IAzureSearchService
    {
        /// <summary>
        /// Azure Search APIを使用してドキュメントを検索
        /// </summary>
        /// <param name="query">検索クエリ</param>
        /// <param name="workIds">検索対象のworkIdリスト（nullの場合は全て）</param>
        /// <param name="top">取得件数（デフォルト10件）</param>
        /// <returns>検索結果のリスト</returns>
        Task<List<SearchResult>> SearchDocumentsAsync(string query, List<string> workIds = null, int top = 10);
        
        /// <summary>
        /// Azure Search APIを使用してセマンティック検索
        /// </summary>
        /// <param name="query">検索クエリ</param>
        /// <param name="workIds">検索対象のworkIdリスト（nullの場合は全て）</param>
        /// <param name="top">取得件数（デフォルト10件）</param>
        /// <returns>検索結果のリスト</returns>
        Task<List<SearchResult>> SemanticSearchAsync(string query, List<string> workIds = null, int top = 10);
        
        /// <summary>
        /// キーワードベースの検索（従来のCalculateRelevanceScoreの代替）
        /// </summary>
        /// <param name="keywords">検索キーワードリスト</param>
        /// <param name="workIds">検索対象のworkIdリスト（nullの場合は全て）</param>
        /// <param name="top">取得件数（デフォルト10件）</param>
        /// <returns>検索結果のリスト</returns>
        Task<List<SearchResult>> KeywordSearchAsync(List<string> keywords, List<string> workIds = null, int top = 10);
        
        /// <summary>
        /// Azure Searchインデックスの接続テスト
        /// </summary>
        /// <returns>接続成功かどうか</returns>
        Task<bool> TestConnectionAsync();
        
        /// <summary>
        /// 指定されたworkIdのドキュメント数を取得
        /// </summary>
        /// <param name="workIds">対象workIdリスト</param>
        /// <returns>workId別ドキュメント数</returns>
        Task<Dictionary<string, int>> GetDocumentCountsByWorkIdAsync(List<string> workIds);
        
        /// <summary>
        /// チャンクドキュメントをインデックスに追加（workId、ページ番号、チャンク番号付き）
        /// </summary>
        /// <param name="id">ドキュメントID</param>
        /// <param name="workId">workId</param>
        /// <param name="title">タイトル</param>
        /// <param name="content">コンテンツ</param>
        /// <param name="pageNumber">ページ番号</param>
        /// <param name="chunkNumber">チャンク番号</param>
        /// <param name="description">説明</param>
        /// <returns>成功かどうか</returns>
        Task<bool> IndexChunkDocumentAsync(string id, string workId, string title, string content, int pageNumber, int chunkNumber, string description = "");
        
        /// <summary>
        /// 指定されたworkIdがAzure Searchインデックスに既に存在するかチェック
        /// </summary>
        /// <param name="workId">チェック対象のworkId</param>
        /// <returns>存在する場合はtrue</returns>
        Task<bool> IsWorkIdIndexedAsync(string workId);
        
        /// <summary>
        /// 指定されたworkIdがoec-sentenceインデックスに既に存在するかチェック
        /// </summary>
        /// <param name="workId">チェック対象のworkId</param>
        /// <returns>存在する場合はtrue</returns>
        Task<bool> IsWorkIdIndexedInSentenceAsync(string workId);
        
        /// <summary>
        /// 両インデックスでworkIdの存在確認
        /// </summary>
        Task<(bool existsInMain, bool existsInSentence)> CheckWorkIdInBothIndexesAsync(string workId);
        
        /// <summary>
        /// 特定のインデックスでworkIdの存在確認
        /// </summary>
        Task<bool> CheckWorkIdInIndexAsync(string workId, string indexName);
        
        /// <summary>
        /// 複数のworkIdがAzure Searchインデックスに既に存在するかチェック
        /// </summary>
        /// <param name="workIds">チェック対象のworkIdリスト</param>
        /// <returns>既に存在するworkIdのリスト</returns>
        Task<List<string>> GetExistingWorkIdsAsync(List<string> workIds);
        
        /// <summary>
        /// 単一ドキュメントをインデックスに追加
        /// </summary>
        /// <param name="id">ドキュメントID</param>
        /// <param name="workId">workId</param>
        /// <param name="title">タイトル</param>
        /// <param name="content">コンテンツ</param>
        /// <param name="description">説明</param>
        /// <returns>成功かどうか</returns>
        Task<bool> IndexDocumentAsync(string id, string workId, string title, string content, string description = "");
        
        /// <summary>
        /// 指定されたworkIdのドキュメントを削除
        /// </summary>
        /// <param name="workIds">削除対象のworkIdリスト</param>
        /// <returns>成功かどうか</returns>
        Task<bool> DeleteDocumentsAsync(List<string> workIds);
        
        /// <summary>
        /// インデックスが存在するかチェックし、存在しない場合は作成
        /// </summary>
        /// <returns>成功かどうか</returns>
        Task<bool> EnsureIndexExistsAsync();
        
        /// <summary>
        /// インデックスを再作成
        /// </summary>
        /// <returns>成功かどうか</returns>
        Task<bool> RecreateIndexAsync();
        
        /// <summary>
        /// oec-sentenceインデックス（ベクトル検索用）が存在するか確認し、存在しない場合は作成
        /// </summary>
        /// <returns>成功かどうか</returns>
        Task<bool> EnsureSentenceIndexExistsAsync();
        
        /// <summary>
        /// oec-sentenceインデックス（ベクトル検索用）を削除
        /// </summary>
        /// <returns>成功かどうか</returns>
        Task<bool> DeleteSentenceIndexAsync();
        
        /// <summary>
        /// oec-sentenceインデックス（ベクトル検索用）を再作成
        /// </summary>  
        /// <returns>成功かどうか</returns>
        Task<bool> RecreateSentenceIndexAsync();
        
        /// <summary>
        /// ユーザーに応じてインデックス名を動的に設定
        /// </summary>
        /// <param name="username">ユーザー名</param>
        void SetUserSpecificIndexes(string username);

        /// <summary>
        /// 指定workIdの全ドキュメントを、メイン/センテンス両インデックスから削除
        /// </summary>
        /// <param name="workId">削除対象のworkId</param>
        /// <param name="username">ユーザー名（ユーザー固有インデックス切替に使用、任意）</param>
        /// <returns>削除件数（メイン, センテンス）</returns>
        Task<(int deletedMain, int deletedSentence)> DeleteWorkIdAsync(string workId, string username = null);
    }
}