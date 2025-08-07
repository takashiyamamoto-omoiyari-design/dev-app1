using System.Collections.Generic;
using System.Threading.Tasks;
using AzureRag.Models;

namespace AzureRag.Services
{
    /// <summary>
    /// 複数インデックス対応Azure検索サービスインターフェース
    /// </summary>
    public interface IMultiIndexSearchService
    {
        /// <summary>
        /// メインインデックス名を取得
        /// </summary>
        string MainIndexName { get; }
        
        /// <summary>
        /// 文章インデックス名を取得
        /// </summary>
        string SentenceIndexName { get; }
        
        /// <summary>
        /// 文章インデックスでクエリに基づいてドキュメントを検索します
        /// </summary>
        /// <param name="query">検索クエリ</param>
        /// <param name="top">取得する最大結果数</param>
        /// <returns>検索結果のリスト</returns>
        Task<List<DocumentSearchResult>> SearchSentencesAsync(string query, int top = 10);
        
        /// <summary>
        /// メインインデックスでクエリに基づいてドキュメントを検索します
        /// </summary>
        /// <param name="query">検索クエリ</param>
        /// <param name="top">取得する最大結果数</param>
        /// <returns>検索結果のリスト</returns>
        Task<List<DocumentSearchResult>> SearchDocumentsAsync(string query, int top = 3);
        
        /// <summary>
        /// 文書をメインインデックスに追加します
        /// </summary>
        /// <param name="id">ドキュメントID</param>
        /// <param name="title">ドキュメントのタイトル</param>
        /// <param name="content">ドキュメントの内容</param>
        /// <returns>インデックス登録が成功したかどうか</returns>
        Task<bool> IndexDocumentAsync(string id, string title, string content);
        
        /// <summary>
        /// 文章を文章インデックスに追加します
        /// </summary>
        /// <param name="id">文章ID</param>
        /// <param name="documentId">所属ドキュメントID</param>
        /// <param name="sentence">文章内容</param>
        /// <returns>インデックス登録が成功したかどうか</returns>
        Task<bool> IndexSentenceAsync(string id, string documentId, string sentence);
        
        /// <summary>
        /// 指定されたIDに関連するドキュメントをメインインデックスから削除します
        /// </summary>
        /// <param name="documentId">ドキュメントID</param>
        /// <returns>削除が成功したかどうか</returns>
        Task<bool> DeleteDocumentAsync(string documentId);
        
        /// <summary>
        /// 指定されたドキュメントIDに関連する文章を文章インデックスから削除します
        /// </summary>
        /// <param name="documentId">ドキュメントID</param>
        /// <returns>削除が成功したかどうか</returns>
        Task<bool> DeleteSentencesAsync(string documentId);
        
        /// <summary>
        /// メインインデックスが存在するか確認します。存在しない場合は作成します。
        /// </summary>
        /// <returns>インデックスが存在するか、作成されたかどうか</returns>
        Task<bool> EnsureMainIndexExistsAsync();
        
        /// <summary>
        /// 文章インデックスが存在するか確認します。存在しない場合は作成します。
        /// </summary>
        /// <returns>インデックスが存在するか、作成されたかどうか</returns>
        Task<bool> EnsureSentenceIndexExistsAsync();
    }
}