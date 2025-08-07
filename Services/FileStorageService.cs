using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using AzureRag.Models.File;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using FileInfo = AzureRag.Models.File.FileInfo;

namespace AzureRag.Services
{
    public class FileStorageService : IFileStorageService
    {
        private readonly ILogger<FileStorageService> _logger;
        private readonly IFileSystem _fileSystem;
        private readonly string _fileStorageDirectory;
        private readonly string _fileInfoDirectory;

        public FileStorageService(
            ILogger<FileStorageService> logger,
            IFileSystem fileSystem)
        {
            _logger = logger;
            _fileSystem = fileSystem;
            
            // ファイルの保存先ディレクトリを設定
            _fileStorageDirectory = Path.Combine(Directory.GetCurrentDirectory(), "storage", "files");
            _fileInfoDirectory = Path.Combine(Directory.GetCurrentDirectory(), "storage", "fileinfo");
            
            // 必要なディレクトリが存在するか確認し、なければ作成
            EnsureDirectoriesExist();
        }

        /// <summary>
        /// 必要なディレクトリが存在するか確認し、なければ作成
        /// </summary>
        private void EnsureDirectoriesExist()
        {
            try
            {
                if (!_fileSystem.Directory.Exists(_fileStorageDirectory))
                {
                    _fileSystem.Directory.CreateDirectory(_fileStorageDirectory);
                    _logger.LogInformation($"ストレージディレクトリを作成しました: {_fileStorageDirectory}");
                }
                
                if (!_fileSystem.Directory.Exists(_fileInfoDirectory))
                {
                    _fileSystem.Directory.CreateDirectory(_fileInfoDirectory);
                    _logger.LogInformation($"ファイル情報ディレクトリを作成しました: {_fileInfoDirectory}");
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning($"ディレクトリ作成権限がありません。ファイル操作時に遅延作成を試行します。\n" +
                    $"手動で権限を設定する場合は以下のコマンドを実行してください:\n" +
                    $"sudo mkdir -p {_fileStorageDirectory}\n" +
                    $"sudo mkdir -p {_fileInfoDirectory}\n" +
                    $"sudo chown -R $(whoami):$(whoami) $(dirname {_fileStorageDirectory})\n" +
                    $"sudo chmod -R 755 $(dirname {_fileStorageDirectory})\n" +
                    $"エラー詳細: {ex.Message}");
                // アプリケーションの起動を継続（例外を投げない）
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"ディレクトリ作成中にエラーが発生しました。ファイル操作時に遅延作成を試行します。エラー: {ex.Message}");
                // アプリケーションの起動を継続（例外を投げない）
            }
        }

        /// <summary>
        /// ファイル操作時にディレクトリの存在確認と作成を試行
        /// </summary>
        private void TryEnsureDirectoriesExistOnDemand()
        {
            try
            {
                if (!_fileSystem.Directory.Exists(_fileStorageDirectory))
                {
                    _logger.LogInformation($"ストレージディレクトリが存在しません。作成を試行します: {_fileStorageDirectory}");
                    _fileSystem.Directory.CreateDirectory(_fileStorageDirectory);
                    _logger.LogInformation($"ストレージディレクトリを作成しました: {_fileStorageDirectory}");
                }
                
                if (!_fileSystem.Directory.Exists(_fileInfoDirectory))
                {
                    _logger.LogInformation($"ファイル情報ディレクトリが存在しません。作成を試行します: {_fileInfoDirectory}");
                    _fileSystem.Directory.CreateDirectory(_fileInfoDirectory);
                    _logger.LogInformation($"ファイル情報ディレクトリを作成しました: {_fileInfoDirectory}");
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError($"ディレクトリ作成権限がありません。管理者に権限設定を依頼してください。エラー: {ex.Message}");
                throw new InvalidOperationException($"ストレージディレクトリの作成権限がありません: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError($"ディレクトリ作成中にエラーが発生しました: {ex.Message}");
                throw new InvalidOperationException($"ディレクトリ作成中にエラーが発生しました: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// ファイルをアップロードして保存します
        /// </summary>
        public async Task<List<Models.File.FileInfo>> UploadAndSaveFilesAsync(IFormFileCollection files)
        {
            _logger.LogInformation($"ファイルのアップロード処理を開始: {files.Count} ファイル");
            
            // ディレクトリの遅延作成を試行
            TryEnsureDirectoriesExistOnDemand();
            
            List<Models.File.FileInfo> savedFiles = new List<Models.File.FileInfo>();
            
            foreach (var file in files)
            {
                try
                {
                    // ファイル情報を作成
                    string fileId = Guid.NewGuid().ToString();
                    string filePath = Path.Combine(_fileStorageDirectory, fileId);
                    
                    // ファイルの拡張子を取得
                    string extension = Path.GetExtension(file.FileName);
                    
                    // ファイルパスに拡張子を追加
                    filePath = $"{filePath}{extension}";
                    
                    // ファイル情報を作成
                    var fileInfo = new Models.File.FileInfo
                    {
                        Id = fileId,
                        FileName = file.FileName,
                        ContentType = file.ContentType,
                        Size = file.Length,
                        FilePath = filePath,
                        UploadDate = DateTime.UtcNow,
                        IsIndexed = false
                    };
                    
                    // ファイルを保存
                    using (var stream = _fileSystem.File.Create(filePath))
                    {
                        await file.CopyToAsync(stream);
                    }
                    
                    // ファイル情報をJSON形式で保存
                    await SaveFileInfoAsync(fileInfo);
                    
                    savedFiles.Add(fileInfo);
                    _logger.LogInformation($"ファイルを保存しました: {fileInfo.FileName}, ID: {fileInfo.Id}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"ファイル保存中にエラーが発生: {file.FileName}");
                }
            }
            
            return savedFiles;
        }

        /// <summary>
        /// ファイル情報を取得します
        /// </summary>
        public async Task<Models.File.FileInfo> GetFileInfoAsync(string fileId)
        {
            _logger.LogInformation($"ファイル情報を取得: {fileId}");
            
            string fileInfoPath = GetFileInfoPath(fileId);
            
            if (!_fileSystem.File.Exists(fileInfoPath))
            {
                _logger.LogWarning($"ファイル情報が見つかりません: {fileId}");
                return null;
            }
            
            try
            {
                string json = await _fileSystem.File.ReadAllTextAsync(fileInfoPath);
                var fileInfo = JsonConvert.DeserializeObject<Models.File.FileInfo>(json);
                
                // ファイル本体が存在するか確認
                if (!_fileSystem.File.Exists(fileInfo.FilePath))
                {
                    _logger.LogWarning($"ファイルが見つかりません: {fileInfo.FilePath}");
                    return null;
                }
                
                return fileInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"ファイル情報の読み込み中にエラーが発生: {fileId}");
                return null;
            }
        }

        /// <summary>
        /// ファイル一覧を取得します
        /// </summary>
        public async Task<List<Models.File.FileInfo>> GetFileListAsync()
        {
            _logger.LogInformation("ファイル一覧を取得");
            
            List<Models.File.FileInfo> fileList = new List<Models.File.FileInfo>();
            
            try
            {
                // ディレクトリ内のすべてのJSONファイルを取得
                var fileInfoFiles = _fileSystem.Directory.GetFiles(_fileInfoDirectory, "*.json");
                
                foreach (var filePath in fileInfoFiles)
                {
                    try
                    {
                        string json = await _fileSystem.File.ReadAllTextAsync(filePath);
                        var fileInfo = JsonConvert.DeserializeObject<Models.File.FileInfo>(json);
                        
                        // ファイル本体が存在するか確認
                        if (_fileSystem.File.Exists(fileInfo.FilePath))
                        {
                            fileList.Add(fileInfo);
                        }
                        else
                        {
                            _logger.LogWarning($"ファイルが見つかりません: {fileInfo.FilePath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"ファイル情報の読み込み中にエラーが発生: {filePath}");
                    }
                }
                
                // アップロード日時の降順でソート
                fileList = fileList.OrderByDescending(f => f.UploadDate).ToList();
                
                _logger.LogInformation($"ファイル一覧を取得しました: {fileList.Count} ファイル");
                return fileList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ファイル一覧の取得中にエラーが発生");
                return fileList;
            }
        }

        /// <summary>
        /// ファイルの内容を取得します
        /// </summary>
        public async Task<Stream> GetFileContentAsync(string fileId)
        {
            _logger.LogInformation($"ファイル内容を取得: {fileId}");
            
            var fileInfo = await GetFileInfoAsync(fileId);
            
            if (fileInfo == null)
            {
                _logger.LogWarning($"ファイル情報が見つかりません: {fileId}");
                return null;
            }
            
            try
            {
                if (!_fileSystem.File.Exists(fileInfo.FilePath))
                {
                    _logger.LogWarning($"ファイルが見つかりません: {fileInfo.FilePath}");
                    return null;
                }
                
                // メモリストリームにファイルを読み込む
                var memoryStream = new MemoryStream();
                using (var fileStream = _fileSystem.File.OpenRead(fileInfo.FilePath))
                {
                    await fileStream.CopyToAsync(memoryStream);
                }
                
                // ストリームの位置を先頭に戻す
                memoryStream.Position = 0;
                
                return memoryStream;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"ファイル内容の取得中にエラーが発生: {fileId}");
                return null;
            }
        }

        /// <summary>
        /// ファイルを削除します
        /// </summary>
        public async Task<bool> DeleteFileAsync(string fileId)
        {
            _logger.LogInformation($"ファイルを削除: {fileId}");
            
            var fileInfo = await GetFileInfoAsync(fileId);
            
            if (fileInfo == null)
            {
                _logger.LogWarning($"ファイル情報が見つかりません: {fileId}");
                return false;
            }
            
            try
            {
                bool success = true;
                
                // ファイル本体を削除
                if (_fileSystem.File.Exists(fileInfo.FilePath))
                {
                    _fileSystem.File.Delete(fileInfo.FilePath);
                    _logger.LogInformation($"ファイルを削除しました: {fileInfo.FilePath}");
                }
                else
                {
                    _logger.LogWarning($"ファイルが見つかりません: {fileInfo.FilePath}");
                    success = false;
                }
                
                // ファイル情報を削除
                string fileInfoPath = GetFileInfoPath(fileId);
                if (_fileSystem.File.Exists(fileInfoPath))
                {
                    _fileSystem.File.Delete(fileInfoPath);
                    _logger.LogInformation($"ファイル情報を削除しました: {fileInfoPath}");
                }
                else
                {
                    _logger.LogWarning($"ファイル情報が見つかりません: {fileInfoPath}");
                    success = false;
                }
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"ファイル削除中にエラーが発生: {fileId}");
                return false;
            }
        }

        /// <summary>
        /// ファイルのインデックス状態を更新します
        /// </summary>
        public async Task<bool> UpdateIndexStatusAsync(string fileId, bool isIndexed)
        {
            _logger.LogInformation($"ファイルのインデックス状態を更新: {fileId}, インデックス状態: {isIndexed}");
            
            var fileInfo = await GetFileInfoAsync(fileId);
            
            if (fileInfo == null)
            {
                _logger.LogWarning($"ファイル情報が見つかりません: {fileId}");
                return false;
            }
            
            try
            {
                // インデックス状態を更新
                fileInfo.IsIndexed = isIndexed;
                
                if (isIndexed)
                {
                    fileInfo.IndexedDate = DateTime.UtcNow;
                }
                else
                {
                    fileInfo.IndexedDate = null;
                }
                
                // ファイル情報を保存
                await SaveFileInfoAsync(fileInfo);
                
                _logger.LogInformation($"ファイルのインデックス状態を更新しました: {fileId}, インデックス状態: {isIndexed}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"ファイルのインデックス状態更新中にエラーが発生: {fileId}");
                return false;
            }
        }

        /// <summary>
        /// ファイル情報をJSONとして保存
        /// </summary>
        private async Task SaveFileInfoAsync(Models.File.FileInfo fileInfo)
        {
            string fileInfoPath = GetFileInfoPath(fileInfo.Id);
            string json = JsonConvert.SerializeObject(fileInfo, Formatting.Indented);
            await _fileSystem.File.WriteAllTextAsync(fileInfoPath, json);
        }

        /// <summary>
        /// ファイル情報のパスを取得
        /// </summary>
        private string GetFileInfoPath(string fileId)
        {
            return Path.Combine(_fileInfoDirectory, $"{fileId}.json");
        }
    }
}