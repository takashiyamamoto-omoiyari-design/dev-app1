using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AzureRag.Models.Index;
using Microsoft.Extensions.Logging;
using System.IO.Abstractions;
using Newtonsoft.Json;
using AzureRag.Models;

namespace AzureRag.Services
{
    public class IndexManagementService : IIndexManagementService
    {
        private readonly ILogger<IndexManagementService> _logger;
        private readonly IFileSystem _fileSystem;
        private readonly IPdfProcessingService _pdfProcessingService;
        private readonly IFileStorageService _fileStorageService;
        private readonly IAzureSearchService _azureSearchService;
        private readonly string _indexDirectory;

        public IndexManagementService(
            ILogger<IndexManagementService> logger,
            IFileSystem fileSystem,
            IPdfProcessingService pdfProcessingService,
            IFileStorageService fileStorageService,
            IAzureSearchService azureSearchService)
        {
            _logger = logger;
            _fileSystem = fileSystem;
            _pdfProcessingService = pdfProcessingService;
            _fileStorageService = fileStorageService;
            _azureSearchService = azureSearchService;
            
            // インデックスディレクトリを設定
            _indexDirectory = Path.Combine(Directory.GetCurrentDirectory(), "indexes");
            if (!_fileSystem.Directory.Exists(_indexDirectory))
            {
                _fileSystem.Directory.CreateDirectory(_indexDirectory);
            }
        }

        /// <summary>
        /// PDFファイルからインデックスを作成します
        /// </summary>
        public async Task<IndexInfo> CreateIndexFromPdfAsync(string fileId)
        {
            _logger.LogInformation($"PDFファイルからインデックスを作成します: {fileId}");
            
            try
            {
                // ファイルが存在するか確認
                var fileInfo = await _fileStorageService.GetFileInfoAsync(fileId);
                if (fileInfo == null)
                {
                    _logger.LogWarning($"ファイルが見つかりません: {fileId}");
                    throw new FileNotFoundException($"ファイルが見つかりません: {fileId}");
                }
                
                // ファイル内容を取得
                using (var fileStream = await _fileStorageService.GetFileContentAsync(fileId))
                {
                    if (fileStream == null)
                    {
                        _logger.LogWarning($"ファイルストリームを取得できません: {fileId}");
                        throw new IOException($"ファイルストリームを取得できません: {fileId}");
                    }
                    
                    // PDF処理方法の変更: Claude 3.7 Sonnetを使用してページごとに構造化処理
                    _logger.LogInformation($"PDFをページごとに分割して構造化処理します: {fileId}");
                    
                    // PDFからページごとにテキストを抽出
                    var pageTexts = await _pdfProcessingService.ExtractTextByPageAsync(fileStream);
                    _logger.LogInformation($"PDFから {pageTexts.Count} ページのテキストを抽出しました");
                    
                    // ページテキストをClaudeで構造化し、tmpフォルダに保存
                    var structuredTextPaths = await _pdfProcessingService.StructureAndSavePageTextsAsync(pageTexts, fileId);
                    _logger.LogInformation($"Claude 3.7 Sonnetで {structuredTextPaths.Count} ページのテキストを構造化しました");
                    
                    // 構造化されたページテキストからチャンクを作成
                    var chunks = await _pdfProcessingService.CreateChunksFromStructuredTextsAsync(structuredTextPaths, fileId);
                    _logger.LogInformation($"構造化されたテキストから {chunks.Count} チャンクを作成しました");
                    
                    // チャンクをファイルに保存
                    var chunksFilePath = await _pdfProcessingService.SaveChunksToFileAsync(chunks, fileId);
                    
                    // Azure Searchにインデックス登録
                    foreach (var chunk in chunks)
                    {
                        await _azureSearchService.IndexDocumentAsync(
                            chunk.Id,
                            fileId, // workIdとしてfileIdを使用
                            fileInfo.FileName,
                            chunk.Content,
                            $"PDF Document - {fileInfo.FileName} (Chunk {chunk.ChunkNumber})"
                        );
                    }
                    
                    // インデックス情報を作成
                    var indexInfo = new IndexInfo
                    {
                        FileId = fileId,
                        IndexId = Guid.NewGuid().ToString(),
                        ChunkCount = chunks.Count,
                        TextContent = new List<string>(),
                        CreatedAt = DateTime.UtcNow
                    };
                    
                    // 各チャンクのコンテンツを追加
                    foreach (var chunk in chunks)
                    {
                        indexInfo.TextContent.Add(chunk.Content);
                    }
                    
                    // インデックス情報を保存
                    await SaveIndexInfoAsync(indexInfo);
                    
                    // ファイルのインデックス状態を更新
                    await _fileStorageService.UpdateIndexStatusAsync(fileId, true);
                    
                    _logger.LogInformation($"インデックスが作成されました: {fileId}, チャンク数: {chunks.Count}");
                    return indexInfo;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"インデックス作成中にエラーが発生: {fileId}");
                throw;
            }
        }

        /// <summary>
        /// インデックスを削除します
        /// </summary>
        public async Task<bool> DeleteIndexAsync(string fileId)
        {
            _logger.LogInformation($"インデックスを削除します: {fileId}");
            
            try
            {
                // インデックスファイルのパス
                var indexFilePath = GetIndexFilePath(fileId);
                
                // インデックス情報ファイルが存在するか確認
                if (!_fileSystem.File.Exists(indexFilePath))
                {
                    _logger.LogWarning($"インデックスファイルが見つかりません: {fileId}");
                    return false;
                }
                
                // インデックス情報を読み込み
                var indexInfo = await LoadIndexInfoAsync(fileId);
                
                // Azure Searchからインデックスを削除
                // 注意: 実際には複数のチャンクのIDを記憶して削除する必要があります
                // ここでは簡易的にfileIdに関連するドキュメントを全て削除する方法を想定
                var deleteResult = await _azureSearchService.DeleteDocumentsAsync(new List<string> { fileId });
                
                // インデックスファイルを削除
                _fileSystem.File.Delete(indexFilePath);
                
                // ファイルのインデックス状態を更新
                await _fileStorageService.UpdateIndexStatusAsync(fileId, false);
                
                _logger.LogInformation($"インデックスを削除しました: {fileId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"インデックス削除中にエラーが発生: {fileId}");
                return false;
            }
        }
        
        /// <summary>
        /// Pythonで抽出されたテキストからインデックスを作成します
        /// </summary>
        public async Task<IndexInfo> CreateIndexFromPythonExtractedTextsAsync(string fileId)
        {
            _logger.LogInformation($"Pythonで抽出されたテキストからインデックスを作成します: {fileId}");
            
            try
            {
                // ファイルが存在するか確認
                var fileInfo = await _fileStorageService.GetFileInfoAsync(fileId);
                if (fileInfo == null)
                {
                    _logger.LogWarning($"ファイルが見つかりません: {fileId}");
                    throw new FileNotFoundException($"ファイルが見つかりません: {fileId}");
                }
                
                // Pythonで抽出されたテキストファイルからチャンクを作成
                var chunks = await _pdfProcessingService.CreateChunksFromPythonExtractedTextsAsync(fileId);
                
                if (chunks.Count == 0)
                {
                    _logger.LogWarning($"テキストチャンクが作成できませんでした: {fileId}");
                    throw new InvalidOperationException($"テキストチャンクが作成できませんでした: {fileId}");
                }
                
                _logger.LogInformation($"Python抽出テキストから {chunks.Count} チャンクを作成しました");
                
                // チャンクをファイルに保存
                var chunksFilePath = await _pdfProcessingService.SaveChunksToFileAsync(chunks, fileId);
                
                // Azure Searchにインデックス登録
                foreach (var chunk in chunks)
                {
                    await _azureSearchService.IndexDocumentAsync(
                        chunk.Id,
                        fileId, // workIdとしてfileIdを使用
                        fileInfo.FileName,
                        chunk.Content,
                        $"PDF Document - {fileInfo.FileName} (Chunk {chunk.ChunkNumber}, Page {chunk.PageNumber})"
                    );
                }
                
                // インデックス情報を作成
                var indexInfo = new IndexInfo
                {
                    FileId = fileId,
                    IndexId = Guid.NewGuid().ToString(),
                    ChunkCount = chunks.Count,
                    TextContent = new List<string>(),
                    CreatedAt = DateTime.UtcNow
                };
                
                // 各チャンクのコンテンツを追加
                foreach (var chunk in chunks)
                {
                    indexInfo.TextContent.Add(chunk.Content);
                }
                
                // インデックス情報を保存
                await SaveIndexInfoAsync(indexInfo);
                
                // ファイルのインデックス状態を更新
                await _fileStorageService.UpdateIndexStatusAsync(fileId, true);
                
                _logger.LogInformation($"Pythonテキストからインデックスが作成されました: {fileId}, チャンク数: {chunks.Count}");
                return indexInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Pythonテキストからのインデックス作成中にエラーが発生: {fileId}");
                throw;
            }
        }

        /// <summary>
        /// インデックスの内容を取得します
        /// </summary>
        public async Task<IndexInfo> GetIndexContentAsync(string fileId)
        {
            _logger.LogInformation($"インデックス内容を取得します: {fileId}");
            
            try
            {
                return await LoadIndexInfoAsync(fileId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"インデックス内容取得中にエラーが発生: {fileId}");
                return null;
            }
        }

        /// <summary>
        /// インデックスが存在するか確認します
        /// </summary>
        public async Task<bool> IndexExistsAsync(string fileId)
        {
            var indexFilePath = GetIndexFilePath(fileId);
            return _fileSystem.File.Exists(indexFilePath);
        }
        
        /// <summary>
        /// 全てのインデックス情報を取得します
        /// </summary>
        public async Task<List<IndexInfo>> GetAllIndexesAsync()
        {
            _logger.LogInformation("全てのインデックス情報を取得します");
            
            try
            {
                List<IndexInfo> indexList = new List<IndexInfo>();
                
                // インデックスディレクトリ内のすべてのJSONファイルを取得
                if (!_fileSystem.Directory.Exists(_indexDirectory))
                {
                    _logger.LogWarning("インデックスディレクトリが存在しません");
                    return indexList;
                }
                
                var indexFiles = _fileSystem.Directory.GetFiles(_indexDirectory, "*.json");
                
                foreach (var indexFilePath in indexFiles)
                {
                    try
                    {
                        var json = await _fileSystem.File.ReadAllTextAsync(indexFilePath);
                        var indexInfo = JsonConvert.DeserializeObject<IndexInfo>(json);
                        
                        if (indexInfo != null)
                        {
                            indexList.Add(indexInfo);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"インデックス情報の読み込み中にエラーが発生: {indexFilePath}");
                    }
                }
                
                // 作成日時の降順でソート
                indexList = indexList.OrderByDescending(i => i.CreatedAt).ToList();
                
                _logger.LogInformation($"インデックス情報を取得しました: {indexList.Count} 個");
                return indexList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "インデックス情報の取得中にエラーが発生");
                return new List<IndexInfo>();
            }
        }

        /// <summary>
        /// インデックス情報ファイルのパスを取得します
        /// </summary>
        private string GetIndexFilePath(string fileId)
        {
            return Path.Combine(_indexDirectory, $"{fileId}.json");
        }

        /// <summary>
        /// インデックス情報を保存します
        /// </summary>
        private async Task SaveIndexInfoAsync(IndexInfo indexInfo)
        {
            var indexFilePath = GetIndexFilePath(indexInfo.FileId);
            var json = JsonConvert.SerializeObject(indexInfo, Formatting.Indented);
            await _fileSystem.File.WriteAllTextAsync(indexFilePath, json);
        }

        /// <summary>
        /// インデックス情報を読み込みます
        /// </summary>
        private async Task<IndexInfo> LoadIndexInfoAsync(string fileId)
        {
            var indexFilePath = GetIndexFilePath(fileId);
            
            if (!_fileSystem.File.Exists(indexFilePath))
            {
                return null;
            }
            
            var json = await _fileSystem.File.ReadAllTextAsync(indexFilePath);
            return JsonConvert.DeserializeObject<IndexInfo>(json);
        }
    }
}