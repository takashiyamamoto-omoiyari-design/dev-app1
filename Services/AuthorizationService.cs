using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AzureRag.Services
{
    public class AuthorizationService : IAuthorizationService
    {
        private readonly ILogger<AuthorizationService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IWorkIdManagementService _workIdManagementService;

        public AuthorizationService(
            ILogger<AuthorizationService> logger,
            IConfiguration configuration,
            IWorkIdManagementService workIdManagementService)
        {
            _logger = logger;
            _configuration = configuration;
            _workIdManagementService = workIdManagementService;
        }

        /// <summary>
        /// ユーザー認証（appsettings.jsonの固定認証情報を使用）
        /// </summary>
        public async Task<UserInfo> AuthenticateUserAsync(string username, string password)
        {
            try
            {
                _logger.LogInformation("ユーザー認証試行: ユーザー名={Username}", username);

                if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                {
                    _logger.LogWarning("認証失敗: ユーザー名またはパスワードが空です");
                    return null;
                }

                // パスワード認証はappsettings.jsonで行う（固定）
                var usersSection = _configuration.GetSection("Users");
                var userSection = usersSection.GetSection(username);

                if (!userSection.Exists())
                {
                    _logger.LogWarning("認証失敗: ユーザーが存在しません - {Username}", username);
                    return null;
                }

                var configPassword = userSection["Password"];
                if (configPassword != password)
                {
                    _logger.LogWarning("認証失敗: パスワードが間違っています - {Username}", username);
                    return null;
                }

                var role = userSection["Role"];

                // workIdリストは動的に取得
                var allowedWorkIds = await _workIdManagementService.GetUserWorkIdsAsync(username);
                
                // インデックス登録権限を取得
                var allowedIndexes = new List<string>();
                var indexesSection = userSection.GetSection("AllowedIndexes");
                if (indexesSection.Exists())
                {
                    allowedIndexes = indexesSection.Get<List<string>>() ?? new List<string>();
                }

                var userInfo = new UserInfo
                {
                    Username = username,
                    Role = role,
                    AllowedWorkIds = allowedWorkIds,
                    AllowedIndexes = allowedIndexes
                };

                _logger.LogInformation("認証成功: ユーザー名={Username}, ロール={Role}, 許可WorkId数={WorkIdCount}, 許可インデックス数={IndexCount}", 
                    username, role, allowedWorkIds.Count, allowedIndexes.Count);

                return userInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ユーザー認証中にエラーが発生しました");
                return null;
            }
        }

        /// <summary>
        /// ユーザーがworkIdにアクセス可能かチェック（動的）
        /// </summary>
        public async Task<bool> CanAccessWorkIdAsync(string username, string workId)
        {
            try
            {
                var hasAccess = await _workIdManagementService.CanUserAccessWorkIdAsync(username, workId);
                
                if (hasAccess)
                {
                    _logger.LogDebug("ユーザーアクセス許可: {Username} -> {WorkId}", username, workId);
                }
                else
                {
                    _logger.LogWarning("ユーザーアクセス拒否: {Username} -> {WorkId}", username, workId);
                }

                return hasAccess;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "workIdアクセスチェック中にエラーが発生しました");
                return false;
            }
        }

        /// <summary>
        /// ユーザーが利用可能なworkIdリストを取得（動的）
        /// </summary>
        public async Task<List<string>> GetAllowedWorkIdsAsync(string username)
        {
            try
            {
                var allowedWorkIds = await _workIdManagementService.GetUserWorkIdsAsync(username);
                _logger.LogInformation("ユーザーの許可workIdリスト取得: {Username}, 件数={Count}", username, allowedWorkIds.Count);
                return allowedWorkIds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "許可workIdリスト取得中にエラーが発生しました");
                return new List<string>();
            }
        }

        /// <summary>
        /// workIdのメタデータ情報を取得（動的）
        /// </summary>
        public async Task<WorkIdMetadata> GetWorkIdMetadataAsync(string workId)
        {
            try
            {
                var workIdInfo = await _workIdManagementService.GetWorkIdInfoAsync(workId);
                
                if (workIdInfo != null)
                {
                    return new WorkIdMetadata
                    {
                        WorkId = workId,
                        Name = workIdInfo.Name,
                        Description = "簡素化により削除",
                        Category = "不明",
                        CreatedDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        CreatedBy = "不明",
                        OriginalFileName = null,
                        Status = "Active"
                    };
                }
                else
                {
                    _logger.LogWarning("workIdメタデータが見つかりません: {WorkId}", workId);
                    return new WorkIdMetadata
                    {
                        WorkId = workId,
                        Name = $"WorkID: {workId}",
                        Description = "メタデータ不明",
                        Category = "不明",
                        CreatedDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        CreatedBy = "不明",
                        Status = "Unknown"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "workIdメタデータ取得中にエラーが発生しました");
                return null;
            }
        }

        /// <summary>
        /// ユーザーが利用可能なworkIdのメタデータリストを取得（動的）
        /// </summary>
        public async Task<List<WorkIdMetadata>> GetAllowedWorkIdMetadataAsync(string username)
        {
            try
            {
                var allowedWorkIds = await GetAllowedWorkIdsAsync(username);
                var metadataList = new List<WorkIdMetadata>();

                foreach (var workId in allowedWorkIds)
                {
                    var metadata = await GetWorkIdMetadataAsync(workId);
                    if (metadata != null)
                    {
                        metadataList.Add(metadata);
                    }
                }

                _logger.LogInformation("ユーザーの許可workIdメタデータリスト取得: {Username}, 件数={Count}", username, metadataList.Count);
                return metadataList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "許可workIdメタデータリスト取得中にエラーが発生しました");
                return new List<WorkIdMetadata>();
            }
        }

        /// <summary>
        /// ユーザーの新しいworkIdを追加（アップロード後に呼び出し）
        /// </summary>
        public async Task<bool> AddWorkIdToUserAsync(string username, string workId, string fileName = null, string description = null)
        {
            try
            {
                var hasAccess = await _workIdManagementService.AddWorkIdToUserAsync(username, workId, fileName, description);
                
                if (hasAccess)
                {
                    _logger.LogInformation("ユーザーworkId追加成功: {Username} -> {WorkId}", username, workId);
                }
                else
                {
                    _logger.LogWarning("ユーザーworkId追加失敗: {Username} -> {WorkId}", username, workId);
                }

                return hasAccess;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ユーザーworkId追加中にエラーが発生しました");
                return false;
            }
        }

        /// <summary>
        /// ユーザーが指定されたインデックスに登録可能かチェック
        /// </summary>
        public async Task<bool> CanUserIndexToAsync(string username, string indexName)
        {
            var (hasPermission, _) = await CanUserIndexToWithReasonAsync(username, indexName);
            return hasPermission;
        }

        /// <summary>
        /// ユーザーが指定されたインデックスに登録可能かチェック（詳細理由付き）
        /// </summary>
        public async Task<(bool hasPermission, string reason)> CanUserIndexToWithReasonAsync(string username, string indexName)
        {
            try
            {
                _logger.LogInformation("詳細権限チェック: ユーザー={Username}, インデックス={IndexName}", username, indexName);

                if (string.IsNullOrEmpty(username))
                {
                    return (false, "ユーザー名が指定されていません");
                }

                if (string.IsNullOrEmpty(indexName))
                {
                    return (false, "インデックス名が指定されていません");
                }

                // appsettings.jsonからユーザー情報を取得
                var userSection = _configuration.GetSection($"Users:{username}");
                if (!userSection.Exists())
                {
                    return (false, $"ユーザー '{username}' が見つかりません");
                }

                var role = userSection["Role"];
                
                // インデックス権限を取得
                var allowedIndexes = new List<string>();
                var indexesSection = userSection.GetSection("AllowedIndexes");
                if (indexesSection.Exists())
                {
                    allowedIndexes = indexesSection.Get<List<string>>() ?? new List<string>();
                }

                // 管理者で設定が空の場合のみ、全インデックスにアクセス可能
                if (role == "Admin" && allowedIndexes.Count == 0)
                {
                    return (true, "管理者権限（設定なし）");
                }

                if (allowedIndexes.Contains("*"))
                {
                    return (true, "全インデックスアクセス権限");
                }

                if (allowedIndexes.Contains(indexName))
                {
                    return (true, $"インデックス '{indexName}' への直接権限");
                }

                return (false, $"インデックス '{indexName}' へのアクセス権限がありません。許可されたインデックス: [{string.Join(", ", allowedIndexes)}]");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "詳細権限チェック中にエラーが発生しました");
                return (false, "権限チェック中にエラーが発生しました");
            }
        }

        /// <summary>
        /// ユーザーが登録可能なインデックス一覧を取得
        /// </summary>
        public async Task<List<string>> GetAllowedIndexesAsync(string username)
        {
            try
            {
                _logger.LogInformation("許可インデックス一覧取得: ユーザー={Username}", username);

                if (string.IsNullOrEmpty(username))
                {
                    _logger.LogWarning("許可インデックス一覧取得失敗: ユーザー名が空です");
                    return new List<string>();
                }

                // ユーザー設定からインデックス登録権限を取得
                var usersSection = _configuration.GetSection("Users");
                var userSection = usersSection.GetSection(username);

                if (!userSection.Exists())
                {
                    _logger.LogWarning("許可インデックス一覧取得失敗: ユーザーが存在しません - {Username}", username);
                    return new List<string>();
                }

                var role = userSection["Role"];
                
                // ユーザーは設定されたインデックスのみ（管理者でも設定を尊重）
                var allowedIndexes = new List<string>();
                var indexesSection = userSection.GetSection("AllowedIndexes");
                if (indexesSection.Exists())
                {
                    allowedIndexes = indexesSection.Get<List<string>>() ?? new List<string>();
                }

                // 管理者で設定が空の場合のみ、全インデックスにアクセス可能
                if (role == "Admin" && allowedIndexes.Count == 0)
                {
                    // 設定ファイルからインデックス一覧を動的に取得
                    var allIndexes = GetAllIndexesFromConfiguration();
                    _logger.LogInformation("許可インデックス一覧取得: 管理者権限（設定なし） - {Username}, インデックス数={Count}", username, allIndexes.Count);
                    return allIndexes;
                }

                // ワイルドカード (*) の場合は全インデックスを返す
                if (allowedIndexes.Contains("*"))
                {
                    // 設定ファイルからインデックス一覧を動的に取得
                    var allIndexes = GetAllIndexesFromConfiguration();
                    _logger.LogInformation("許可インデックス一覧取得: ワイルドカード - {Username}, インデックス数={Count}", username, allIndexes.Count);
                    return allIndexes;
                }

                _logger.LogInformation("許可インデックス一覧取得: {Username}, インデックス数={Count}", username, allowedIndexes.Count);
                return allowedIndexes;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "許可インデックス一覧取得中にエラーが発生しました");
                return new List<string>();
            }
        }
        
        /// <summary>
        /// インデックス設定情報を取得
        /// </summary>
        public async Task<IndexConfiguration> GetIndexConfigurationAsync(string indexName)
        {
            try
            {
                _logger.LogInformation("インデックス設定取得: インデックス={IndexName}", indexName);

                if (string.IsNullOrEmpty(indexName))
                {
                    _logger.LogWarning("インデックス設定取得失敗: インデックス名が空です");
                    return null;
                }

                var indexConfigSection = _configuration.GetSection("IndexConfiguration");
                var indexSection = indexConfigSection.GetSection(indexName);

                if (!indexSection.Exists())
                {
                    _logger.LogWarning("インデックス設定取得失敗: インデックス設定が存在しません - {IndexName}", indexName);
                    return null;
                }

                var config = new IndexConfiguration
                {
                    IndexName = indexName,
                    Type = indexSection["Type"],
                    PairedMainIndex = indexSection["PairedMainIndex"],
                    PairedSentenceIndex = indexSection["PairedSentenceIndex"]
                };

                _logger.LogInformation("インデックス設定取得成功: {IndexName}, タイプ={Type}", indexName, config.Type);
                return config;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "インデックス設定取得中にエラーが発生しました");
                return null;
            }
        }
        
        /// <summary>
        /// ユーザーの許可されたメイン/センテンスインデックスペアを取得
        /// </summary>
        public async Task<List<UserIndexPair>> GetUserIndexPairsAsync(string username)
        {
            try
            {
                _logger.LogInformation("ユーザーインデックスペア取得: ユーザー={Username}", username);

                var allowedIndexes = await GetAllowedIndexesAsync(username);
                var indexPairs = new List<UserIndexPair>();
                var processedMainIndexes = new HashSet<string>();

                foreach (var indexName in allowedIndexes)
                {
                    var config = await GetIndexConfigurationAsync(indexName);
                    if (config == null) continue;

                    // メインインデックスの場合
                    if (config.Type == "Main" && !processedMainIndexes.Contains(indexName))
                    {
                        var pairedSentenceIndex = config.PairedSentenceIndex;
                        
                        // センテンスインデックスも許可されているかチェック
                        if (!string.IsNullOrEmpty(pairedSentenceIndex) && allowedIndexes.Contains(pairedSentenceIndex))
                        {
                            indexPairs.Add(new UserIndexPair
                            {
                                MainIndex = indexName,
                                SentenceIndex = pairedSentenceIndex
                            });
                            processedMainIndexes.Add(indexName);
                            _logger.LogInformation("インデックスペア追加: メイン={Main}, センテンス={Sentence}", indexName, pairedSentenceIndex);
                        }
                        else
                        {
                            _logger.LogWarning("ペアされたセンテンスインデックスが許可されていません: メイン={Main}, センテンス={Sentence}", indexName, pairedSentenceIndex);
                        }
                    }
                }

                _logger.LogInformation("ユーザーインデックスペア取得完了: ユーザー={Username}, ペア数={Count}", username, indexPairs.Count);
                return indexPairs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ユーザーインデックスペア取得中にエラーが発生しました");
                return new List<UserIndexPair>();
            }
        }

        /// <summary>
        /// workId管理データを再読み込み（外部呼び出し用）
        /// </summary>
        public async Task<bool> ReloadWorkIdDataAsync()
        {
            try
            {
                _logger.LogInformation("AuthorizationServiceからworkId管理データの再読み込みを要求");
                var result = await _workIdManagementService.ReloadDataAsync();
                
                if (result)
                {
                    _logger.LogInformation("workId管理データの再読み込みが完了しました");
                }
                else
                {
                    _logger.LogWarning("workId管理データの再読み込みに失敗しました");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "workId管理データ再読み込み中にエラーが発生しました");
                return false;
            }
        }

        /// <summary>
        /// workId管理データをバックアップから復元（外部呼び出し用）
        /// </summary>
        public async Task<bool> RestoreFromBackupAsync()
        {
            try
            {
                _logger.LogInformation("AuthorizationServiceからworkId管理データのバックアップ復元を要求");
                var result = await _workIdManagementService.RestoreFromBackupAsync();
                
                if (result)
                {
                    _logger.LogInformation("workId管理データのバックアップ復元が完了しました");
                }
                else
                {
                    _logger.LogWarning("workId管理データのバックアップ復元に失敗しました");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "workId管理データバックアップ復元中にエラーが発生しました");
                return false;
            }
        }

        /// <summary>
        /// workId管理データのバックアップ存在確認（外部呼び出し用）
        /// </summary>
        public async Task<bool> BackupExistsAsync()
        {
            try
            {
                var result = await _workIdManagementService.BackupExistsAsync();
                _logger.LogDebug("workId管理データバックアップ存在確認: {BackupExists}", result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "workId管理データバックアップ存在確認中にエラーが発生しました");
                return false;
            }
        }

        private List<string> GetAllIndexesFromConfiguration()
        {
            var allIndexes = new List<string>();
            var indexConfigSection = _configuration.GetSection("IndexConfiguration");
            foreach (var key in indexConfigSection.GetChildren().Select(k => k.Key))
            {
                allIndexes.Add(key);
            }
            return allIndexes;
        }
    }
} 