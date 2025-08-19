using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;


namespace AzureRag.Services
{
    /// <summary>
    /// ユーザーのworkId管理情報
    /// </summary>
    public class UserWorkIdInfo
    {
        public string Username { get; set; }
        public List<string> AllowedWorkIds { get; set; } = new List<string>();
        public string Role { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    /// <summary>
    /// workIdの詳細情報（簡素化版）
    /// </summary>
    public class WorkIdInfo
    {
        public string Name { get; set; }
        public string OriginalFileName { get; set; }
        public string SavedRelativePath { get; set; }
        public DateTime? SavedAt { get; set; }
        public long? SavedFileSize { get; set; }
    }

    /// <summary>
    /// 動的workId管理データ
    /// </summary>
    public class WorkIdManagementData
    {
        public Dictionary<string, UserWorkIdInfo> Users { get; set; } = new Dictionary<string, UserWorkIdInfo>();
        public Dictionary<string, WorkIdInfo> WorkIds { get; set; } = new Dictionary<string, WorkIdInfo>();
        public DateTime LastUpdated { get; set; }
    }

    public interface IWorkIdManagementService : IDisposable
    {
        /// <summary>
        /// ユーザーの新しいworkIdを追加
        /// </summary>
        Task<bool> AddWorkIdToUserAsync(string username, string workId, string fileName = null, string description = null);
        
        /// <summary>
        /// ユーザーの利用可能workIdリストを取得（動的）
        /// </summary>
        Task<List<string>> GetUserWorkIdsAsync(string username);
        
        /// <summary>
        /// workId情報を取得
        /// </summary>
        Task<WorkIdInfo> GetWorkIdInfoAsync(string workId);
        
        /// <summary>
        /// ユーザーがworkIdにアクセス可能かチェック（動的）
        /// </summary>
        Task<bool> CanUserAccessWorkIdAsync(string username, string workId);
        
        /// <summary>
        /// workId管理データを保存
        /// </summary>
        Task<bool> SaveWorkIdDataAsync();
        
        /// <summary>
        /// workId管理データを読み込み
        /// </summary>
        Task<bool> LoadWorkIdDataAsync();
        
        /// <summary>
        /// workId管理データを外部から強制的に再読み込み
        /// </summary>
        Task<bool> ReloadDataAsync();

        /// <summary>
        /// バックアップファイルから復元
        /// </summary>
        Task<bool> RestoreFromBackupAsync();

        /// <summary>
        /// バックアップファイルの存在確認
        /// </summary>
        Task<bool> BackupExistsAsync();

        /// <summary>
        /// workIdに対応する保存済みファイル情報を更新
        /// </summary>
        Task<bool> UpdateSavedFileInfoAsync(string workId, string originalFileName, string savedRelativePath, long? sizeBytes);
    }

    public class WorkIdManagementService : IWorkIdManagementService, IDisposable
    {
        private readonly ILogger<WorkIdManagementService> _logger;
        private readonly IConfiguration _configuration;
        private string _dataFilePath;
        private WorkIdManagementData _workIdData;
        private readonly object _lockObject = new object();
        private bool _isFileAccessible = false; // ファイルアクセス可能フラグ
        private FileSystemWatcher _fileWatcher; // ファイル監視追加

        public WorkIdManagementService(
            ILogger<WorkIdManagementService> logger,
            IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            
            _workIdData = new WorkIdManagementData();
            
            // 初期化時にデータを読み込み
            Task.Run(async () => await InitializeAsync());
        }

        /// <summary>
        /// 書き込み可能なファイルパスを決定する
        /// </summary>
        private string DetermineWritableFilePath()
        {
            var fileName = "workid_management.json";
            var possiblePaths = new List<string>();

            try
            {
                // 1. ユーザーホームディレクトリ（最優先）
                var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(homeDir))
                {
                    var appDataDir = Path.Combine(homeDir, ".azurerag");
                    possiblePaths.Add(Path.Combine(appDataDir, fileName));
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "ユーザーホームディレクトリの取得に失敗");
            }

            try
            {
                // 2. システム一時ディレクトリ
                var tempDir = Path.GetTempPath();
                var appTempDir = Path.Combine(tempDir, "azurerag");
                possiblePaths.Add(Path.Combine(appTempDir, fileName));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "一時ディレクトリの取得に失敗");
            }

            // 3. 現在のstorageディレクトリ（既存の方法）
            try
            {
                var storageDir = Path.Combine(Directory.GetCurrentDirectory(), "storage");
                possiblePaths.Add(Path.Combine(storageDir, fileName));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "storageディレクトリパスの取得に失敗");
            }

            // 各パスを順番に試して書き込み可能な場所を見つける
            foreach (var path in possiblePaths)
            {
                try
                {
                    var directory = Path.GetDirectoryName(path);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // 書き込みテスト
                    var testFile = Path.Combine(directory, "write_test.tmp");
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);

                    _logger.LogInformation("書き込み可能なパスを発見: {Path}", path);
                    return path;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "パス {Path} への書き込みテスト失敗", path);
                }
            }

            _logger.LogWarning("書き込み可能なパスが見つかりません。メモリ内でのみ動作します。");
            return null; // 書き込み不可
        }

        /// <summary>
        /// 初期化処理（完全な権限エラー対応版）
        /// </summary>
        private async Task InitializeAsync()
        {
            try
            {
                // 書き込み可能なファイルパスを決定
                _dataFilePath = DetermineWritableFilePath();
                _isFileAccessible = !string.IsNullOrEmpty(_dataFilePath);

                if (!_isFileAccessible)
                {
                    _logger.LogWarning("ファイル永続化が利用できません。メモリ内でのみ動作します。");
                    return;
                }

                // 既存データの移行処理
                await MigrateExistingDataAsync();

                // データファイルを読み込み
                await LoadWorkIdDataAsync();
                _logger.LogInformation("workId管理データファイルを正常に読み込みました: {FilePath}", _dataFilePath);
                
                // ファイル監視を開始
                StartFileWatcher();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WorkIdManagementService初期化中にエラーが発生しました。メモリ内でのみ動作します。");
                _isFileAccessible = false;
            }
        }

        /// <summary>
        /// 既存データの移行処理
        /// </summary>
        private async Task MigrateExistingDataAsync()
        {
            if (string.IsNullOrEmpty(_dataFilePath))
                return;

            // 新しいパス（書き込み可能場所）にファイルが存在するかチェック
            if (File.Exists(_dataFilePath))
                return;

            // 既存の可能な場所からデータを探して移行
            var possibleOldPaths = new[]
            {
                "workid_management.json", // ルートディレクトリ
                Path.Combine("storage", "workid_management.json") // storageディレクトリ
            };

            foreach (var oldPath in possibleOldPaths)
            {
                try
                {
                    if (File.Exists(oldPath))
                    {
                        _logger.LogInformation("既存のworkId管理ファイルを検出。新しい場所に移行します: {OldPath} -> {NewPath}", oldPath, _dataFilePath);
                        
                        var content = await File.ReadAllTextAsync(oldPath);
                        var directory = Path.GetDirectoryName(_dataFilePath);
                        if (!Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }
                        await File.WriteAllTextAsync(_dataFilePath, content);
                        
                        _logger.LogInformation("workId管理ファイルの移行完了: {NewPath}", _dataFilePath);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "既存ファイル {OldPath} の移行に失敗", oldPath);
                }
            }

            _logger.LogInformation("既存のworkId管理ファイルが見つかりませんでした。新規作成します: {Path}", _dataFilePath);
        }

        /// <summary>
        /// ファイル監視を開始
        /// </summary>
        private void StartFileWatcher()
        {
            try
            {
                if (string.IsNullOrEmpty(_dataFilePath) || !File.Exists(_dataFilePath))
                {
                    _logger.LogDebug("ファイル監視開始不可: ファイルパスが無効または存在しません");
                    return;
                }

                var directory = Path.GetDirectoryName(_dataFilePath);
                var fileName = Path.GetFileName(_dataFilePath);

                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                {
                    _logger.LogDebug("ファイル監視開始不可: ディレクトリが存在しません");
                    return;
                }

                _fileWatcher = new FileSystemWatcher(directory, fileName)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };

                _fileWatcher.Changed += OnFileChanged;
                _logger.LogInformation("ファイル監視開始: {FilePath}", _dataFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ファイル監視の開始に失敗しました");
            }
        }

        /// <summary>
        /// ファイル変更時の処理
        /// </summary>
        private async void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                _logger.LogInformation("workId管理ファイルの変更を検出: {FilePath}", e.FullPath);
                
                // 少し待機してファイルロックを回避
                await Task.Delay(500);
                
                // データを再読み込み
                var success = await LoadWorkIdDataAsync();
                
                if (success)
                {
                    _logger.LogInformation("workId管理データの自動再読み込み完了");
                }
                else
                {
                    _logger.LogWarning("workId管理データの自動再読み込みに失敗");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ファイル変更時の自動再読み込みでエラーが発生");
            }
        }

        /// <summary>
        /// リソースの解放
        /// </summary>
        public void Dispose()
        {
            try
            {
                _fileWatcher?.Dispose();
                _logger.LogInformation("ファイル監視を停止しました");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ファイル監視の停止中にエラーが発生");
            }
        }

        /// <summary>
        /// ユーザーの新しいworkIdを追加
        /// </summary>
        public async Task<bool> AddWorkIdToUserAsync(string username, string workId, string fileName = null, string description = null)
        {
            try
            {
                lock (_lockObject)
                {
                    _logger.LogInformation("ユーザーworkId追加: ユーザー={Username}, workId={WorkId}, ファイル={FileName}", 
                        username, workId, fileName);

                    // ユーザー情報の更新
                    if (!_workIdData.Users.ContainsKey(username))
                    {
                        _workIdData.Users[username] = new UserWorkIdInfo
                        {
                            Username = username,
                            Role = "User", // デフォルトはUser
                            AllowedWorkIds = new List<string>()
                        };
                    }

                    var userInfo = _workIdData.Users[username];
                    if (!userInfo.AllowedWorkIds.Contains(workId))
                    {
                        userInfo.AllowedWorkIds.Add(workId);
                        userInfo.LastUpdated = DateTime.UtcNow;
                    }

                    // workId情報の追加（簡素化版）
                    _workIdData.WorkIds[workId] = new WorkIdInfo
                    {
                        Name = fileName ?? $"Document_{workId[..8]}",
                        OriginalFileName = fileName
                    };

                    _workIdData.LastUpdated = DateTime.UtcNow;
                }

                // データを保存
                var saved = await SaveWorkIdDataAsync();
                
                if (saved)
                {
                    _logger.LogInformation("ユーザーworkId追加完了: ユーザー={Username}, workId={WorkId}", username, workId);
                }
                else
                {
                    _logger.LogWarning("ユーザーworkId追加後の保存に失敗しましたが、メモリ内では更新されています: ユーザー={Username}, workId={WorkId}", username, workId);
                }

                return true; // メモリ内で更新されているので、ファイル保存に失敗してもtrueを返す
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ユーザーworkId追加中にエラー: ユーザー={Username}, workId={WorkId}", username, workId);
                return false;
            }
        }

        /// <summary>
        /// 保存済みファイル情報を更新
        /// </summary>
        public async Task<bool> UpdateSavedFileInfoAsync(string workId, string originalFileName, string savedRelativePath, long? sizeBytes)
        {
            try
            {
                lock (_lockObject)
                {
                    if (!_workIdData.WorkIds.ContainsKey(workId))
                    {
                        _workIdData.WorkIds[workId] = new WorkIdInfo
                        {
                            Name = originalFileName ?? $"Document_{workId[..8]}"
                        };
                    }
                    var info = _workIdData.WorkIds[workId];
                    if (!string.IsNullOrEmpty(originalFileName))
                    {
                        info.OriginalFileName = originalFileName;
                        if (string.IsNullOrEmpty(info.Name))
                        {
                            info.Name = originalFileName;
                        }
                    }
                    info.SavedRelativePath = savedRelativePath;
                    info.SavedAt = DateTime.UtcNow;
                    info.SavedFileSize = sizeBytes;
                    _workIdData.LastUpdated = DateTime.UtcNow;
                }
                return await SaveWorkIdDataAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存済みファイル情報の更新中にエラー: workId={WorkId}", workId);
                return false;
            }
        }

        /// <summary>
        /// ユーザーの利用可能workIdリストを取得（動的）
        /// </summary>
        public async Task<List<string>> GetUserWorkIdsAsync(string username)
        {
            try
            {
                lock (_lockObject)
                {
                    if (_workIdData.Users.ContainsKey(username))
                    {
                        var userInfo = _workIdData.Users[username];
                        
                        // 管理者の場合は全workIdを返す
                        if (userInfo.Role == "Admin")
                        {
                            var allWorkIds = _workIdData.WorkIds.Keys.ToList();
                            _logger.LogDebug("管理者workIdリスト取得: ユーザー={Username}, 件数={Count}", username, allWorkIds.Count);
                            return allWorkIds;
                        }
                        
                        // 一般ユーザーは許可されたworkIdのみ
                        _logger.LogDebug("ユーザーworkIdリスト取得: ユーザー={Username}, 件数={Count}", username, userInfo.AllowedWorkIds.Count);
                        return userInfo.AllowedWorkIds.ToList();
                    }
                    else
                    {
                        _logger.LogWarning("ユーザーが見つかりません: {Username}", username);
                        return new List<string>();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ユーザーworkIdリスト取得中にエラー: ユーザー={Username}", username);
                return new List<string>();
            }
        }

        /// <summary>
        /// workId情報を取得
        /// </summary>
        public async Task<WorkIdInfo> GetWorkIdInfoAsync(string workId)
        {
            try
            {
                lock (_lockObject)
                {
                    if (_workIdData.WorkIds.ContainsKey(workId))
                    {
                        return _workIdData.WorkIds[workId];
                    }
                    else
                    {
                        _logger.LogWarning("workId情報が見つかりません: {WorkId}", workId);
                        return new WorkIdInfo
                        {
                            Name = $"WorkID: {workId}"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "workId情報取得中にエラー: workId={WorkId}", workId);
                return null;
            }
        }

        /// <summary>
        /// ユーザーがworkIdにアクセス可能かチェック（動的）
        /// </summary>
        public async Task<bool> CanUserAccessWorkIdAsync(string username, string workId)
        {
            try
            {
                var userWorkIds = await GetUserWorkIdsAsync(username);
                var hasAccess = userWorkIds.Contains(workId);
                
                _logger.LogDebug("workIdアクセスチェック: ユーザー={Username}, workId={WorkId}, アクセス={HasAccess}", 
                    username, workId, hasAccess);
                
                return hasAccess;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "workIdアクセスチェック中にエラー: ユーザー={Username}, workId={WorkId}", username, workId);
                return false;
            }
        }

        /// <summary>
        /// 既存ファイルのバックアップを作成
        /// </summary>
        private async Task<bool> CreateBackupAsync()
        {
            try
            {
                if (!File.Exists(_dataFilePath))
                {
                    _logger.LogDebug("バックアップ対象ファイルが存在しません: {FilePath}", _dataFilePath);
                    return true; // 新規作成の場合はバックアップ不要
                }

                var backupPath = $"{_dataFilePath}.backup";
                
                // 前回のバックアップが存在する場合は削除
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                    _logger.LogDebug("前回のバックアップファイルを削除: {BackupPath}", backupPath);
                }

                // 現在のファイルをバックアップとしてコピー
                File.Copy(_dataFilePath, backupPath);
                _logger.LogInformation("workId管理データのバックアップを作成: {BackupPath}", backupPath);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "バックアップ作成中にエラーが発生しましたが、処理を続行します");
                return false; // バックアップ失敗でも処理は継続
            }
        }

        /// <summary>
        /// workId管理データを保存
        /// </summary>
        public async Task<bool> SaveWorkIdDataAsync()
        {
            try
            {
                // ファイルアクセス不可の場合はメモリ内でのみ動作
                if (!_isFileAccessible || string.IsNullOrEmpty(_dataFilePath))
                {
                    _logger.LogDebug("ファイル永続化は利用できません。メモリ内でのみ動作します。");
                    return true; // メモリ内での動作は成功として扱う
                }

                // 保存前にバックアップを作成
                await CreateBackupAsync();

                var json = JsonSerializer.Serialize(_workIdData, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await File.WriteAllTextAsync(_dataFilePath, json);
                
                _logger.LogDebug("workId管理データ保存完了: ファイル={FilePath}", _dataFilePath);
                return true;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning("ファイル書き込み権限がありません。メモリ内でのみ動作します。ファイル={FilePath}, エラー={Error}", _dataFilePath, ex.Message);
                _isFileAccessible = false; // ファイルアクセス不可フラグを設定
                return true; // メモリ内での動作は成功として扱う
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "workId管理データ保存中にエラー: ファイル={FilePath}", _dataFilePath);
                return false;
            }
        }

        /// <summary>
        /// workId管理データを読み込み
        /// </summary>
        public async Task<bool> LoadWorkIdDataAsync()
        {
            try
            {
                // ファイルアクセス不可の場合はスキップ
                if (!_isFileAccessible || string.IsNullOrEmpty(_dataFilePath))
                {
                    _logger.LogDebug("ファイル永続化が利用できません。メモリ内でのみ動作します。");
                    return true; // メモリ内での動作は成功として扱う
                }

                if (!File.Exists(_dataFilePath))
                {
                    _logger.LogWarning("workId管理データファイルが存在しません: {FilePath}", _dataFilePath);
                    return false;
                }

                var json = await File.ReadAllTextAsync(_dataFilePath);
                var data = JsonSerializer.Deserialize<WorkIdManagementData>(json, new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                if (data != null)
                {
                    lock (_lockObject)
                    {
                        _workIdData = data;
                    }
                    
                    _logger.LogInformation("workId管理データ読み込み完了: ユーザー数={UserCount}, workId数={WorkIdCount}", 
                        _workIdData.Users.Count, _workIdData.WorkIds.Count);
                    return true;
                }
                else
                {
                    _logger.LogError("workId管理データの読み込みに失敗しました");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "workId管理データ読み込み中にエラー: ファイル={FilePath}", _dataFilePath);
                return false;
            }
        }

        /// <summary>
        /// workId管理データを外部から強制的に再読み込み
        /// </summary>
        public async Task<bool> ReloadDataAsync()
        {
            try
            {
                _logger.LogInformation("workId管理データの強制リロードを開始します");
                var result = await LoadWorkIdDataAsync();
                
                if (result)
                {
                    _logger.LogInformation("workId管理データの強制リロードが完了しました");
                }
                else
                {
                    _logger.LogWarning("workId管理データの強制リロードに失敗しました");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "workId管理データの強制リロード中にエラーが発生しました");
                return false;
            }
        }

        /// <summary>
        /// バックアップファイルから復元
        /// </summary>
        public async Task<bool> RestoreFromBackupAsync()
        {
            try
            {
                if (!_isFileAccessible || string.IsNullOrEmpty(_dataFilePath))
                {
                    _logger.LogWarning("ファイルアクセスが利用できないため、バックアップから復元できません");
                    return false;
                }

                var backupPath = $"{_dataFilePath}.backup";
                
                if (!File.Exists(backupPath))
                {
                    _logger.LogWarning("バックアップファイルが存在しません: {BackupPath}", backupPath);
                    return false;
                }

                _logger.LogInformation("バックアップファイルから復元を開始: {BackupPath}", backupPath);

                // バックアップファイルを現在のファイルにコピー
                File.Copy(backupPath, _dataFilePath, overwrite: true);

                // データを再読み込み
                var loadResult = await LoadWorkIdDataAsync();
                
                if (loadResult)
                {
                    _logger.LogInformation("バックアップファイルからの復元が完了しました");
                    return true;
                }
                else
                {
                    _logger.LogError("バックアップファイルからの復元後、データの読み込みに失敗しました");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "バックアップファイルからの復元中にエラーが発生しました");
                return false;
            }
        }

        /// <summary>
        /// バックアップファイルの存在確認
        /// </summary>
        public async Task<bool> BackupExistsAsync()
        {
            try
            {
                if (!_isFileAccessible || string.IsNullOrEmpty(_dataFilePath))
                {
                    return false;
                }

                var backupPath = $"{_dataFilePath}.backup";
                return File.Exists(backupPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "バックアップファイル存在確認中にエラーが発生しました");
                return false;
            }
        }
    }
} 