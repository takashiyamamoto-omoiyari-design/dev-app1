using System.Collections.Generic;
using System.Threading.Tasks;

namespace AzureRag.Services
{
    /// <summary>
    /// ユーザー情報クラス
    /// </summary>
    public class UserInfo
    {
        public string Username { get; set; }
        public string Role { get; set; }
        public List<string> AllowedWorkIds { get; set; } = new List<string>();
        public List<string> AllowedIndexes { get; set; } = new List<string>();
        public bool IsAdmin => Role == "Admin";
    }

    /// <summary>
    /// workIdメタデータクラス
    /// </summary>
    public class WorkIdMetadata
    {
        public string WorkId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string CreatedDate { get; set; }
        public string CreatedBy { get; set; }
        public string OriginalFileName { get; set; }
        public string Status { get; set; }
    }

    public class IndexConfiguration
    {
        public string IndexName { get; set; }
        public string Type { get; set; } // "Main" or "Sentence"
        public string PairedMainIndex { get; set; }
        public string PairedSentenceIndex { get; set; }
    }

    public class UserIndexPair
    {
        public string MainIndex { get; set; }
        public string SentenceIndex { get; set; }
    }

    public interface IAuthorizationService
    {
        /// <summary>
        /// ユーザー認証
        /// </summary>
        Task<UserInfo> AuthenticateUserAsync(string username, string password);
        
        /// <summary>
        /// ユーザーがworkIdにアクセス可能かチェック（動的）
        /// </summary>
        Task<bool> CanAccessWorkIdAsync(string username, string workId);
        
        /// <summary>
        /// ユーザーが利用可能なworkIdリストを取得（動的）
        /// </summary>
        Task<List<string>> GetAllowedWorkIdsAsync(string username);
        
        /// <summary>
        /// workIdのメタデータ情報を取得（動的）
        /// </summary>
        Task<WorkIdMetadata> GetWorkIdMetadataAsync(string workId);
        
        /// <summary>
        /// ユーザーが利用可能なworkIdのメタデータリストを取得（動的）
        /// </summary>
        Task<List<WorkIdMetadata>> GetAllowedWorkIdMetadataAsync(string username);
        
        /// <summary>
        /// ユーザーの新しいworkIdを追加（アップロード後に呼び出し）
        /// </summary>
        Task<bool> AddWorkIdToUserAsync(string username, string workId, string fileName = null, string description = null);
        
        /// <summary>
        /// ユーザーが指定されたインデックスに登録可能かチェック
        /// </summary>
        Task<bool> CanUserIndexToAsync(string username, string indexName);
        
        /// <summary>
        /// ユーザーが指定されたインデックスに登録可能かチェック（詳細理由付き）
        /// </summary>
        Task<(bool hasPermission, string reason)> CanUserIndexToWithReasonAsync(string username, string indexName);
        
        /// <summary>
        /// ユーザーが登録可能なインデックス一覧を取得
        /// </summary>
        Task<List<string>> GetAllowedIndexesAsync(string username);
        
        /// <summary>
        /// インデックス設定情報を取得
        /// </summary>
        Task<IndexConfiguration> GetIndexConfigurationAsync(string indexName);
        
        /// <summary>
        /// ユーザーの許可されたメイン/センテンスインデックスペアを取得
        /// </summary>
        Task<List<UserIndexPair>> GetUserIndexPairsAsync(string username);

        /// <summary>
        /// workId管理データを再読み込み（外部呼び出し用）
        /// </summary>
        Task<bool> ReloadWorkIdDataAsync();

        /// <summary>
        /// workId管理データをバックアップから復元（外部呼び出し用）
        /// </summary>
        Task<bool> RestoreFromBackupAsync();

        /// <summary>
        /// workId管理データのバックアップ存在確認（外部呼び出し用）
        /// </summary>
        Task<bool> BackupExistsAsync();
    }
} 