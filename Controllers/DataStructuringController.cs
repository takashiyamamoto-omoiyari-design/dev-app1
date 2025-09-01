using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using IOFileInfo = System.IO.FileInfo; // 明示的な別名を設定して曖昧さを解消
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using AzureRag.Services;
using AzureRag.Models;
using AzureRag.Models.File;
using static AzureRag.Services.AutoStructureService; // ChunkItemクラスをインポート

using System.Collections.Concurrent;
using System.Net.Http;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Amazon;
using Amazon.ElasticLoadBalancingV2;
using Amazon.ElasticLoadBalancingV2.Model;


namespace AzureRag.Controllers
{
    [ApiController]
    [Route("api/data-structuring")]
    [Authorize]
    public class DataStructuringController : ControllerBase
    {
        // プロセスログを保存するための辞書
        private static readonly ConcurrentDictionary<string, List<string>> _processingLogs = new ConcurrentDictionary<string, List<string>>();

        // 【追加】メモリキャッシュ用の静的辞書
        private static readonly ConcurrentDictionary<string, CachedStructuredData> _dataCache = new ConcurrentDictionary<string, CachedStructuredData>();
        private static readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(2); // 2時間キャッシュ
        private static readonly int _maxCacheSize = 100; // 最大100件まで

        /// <summary>
        /// 構造化データのキャッシュ用クラス
        /// </summary>
        public class CachedStructuredData
        {
            public AutoStructureResponse Data { get; set; }
            public DateTime CachedAt { get; set; }
            public DateTime LastAccessed { get; set; }
            public bool IsExpired => DateTime.UtcNow - CachedAt > _cacheExpiration;
        }

        /// <summary>
        /// キャッシュ付きで構造化データを取得
        /// </summary>
        private async Task<AutoStructureResponse> GetStructuredDataWithCache(string workId)
        {
            try
            {
                _logger.LogInformation($"【デバッグ】GetStructuredDataWithCache開始: workId={workId}");
                
                // キャッシュチェック
                if (_dataCache.TryGetValue(workId, out var cached) && !cached.IsExpired)
                {
                    // アクセス時刻を更新（LRU用）
                    cached.LastAccessed = DateTime.UtcNow;
                    _logger.LogInformation($"workId {workId} のデータをキャッシュから取得（キャッシュ年齢: {(DateTime.UtcNow - cached.CachedAt).TotalMinutes:F1}分）");
                    return cached.Data;
                }

                // キャッシュミス → 外部API呼び出し
                _logger.LogInformation($"【デバッグ】キャッシュミス - 外部API呼び出し開始: workId={workId}");
                var data = await _autoStructureService.GetStructuredDataAsync(workId);
                
                // 外部API取得結果を詳細ログ出力
                if (data != null)
                {
                    _logger.LogInformation($"=== 外部API /Check取得結果 (workId: {workId}) ===");
                    _logger.LogInformation($"State: {data.State}");
                    _logger.LogInformation($"PageNo: {data.PageNo}");
                    _logger.LogInformation($"MaxPageNo: {data.MaxPageNo}");
                    _logger.LogInformation($"ReturnCode: {data.ReturnCode}");
                    _logger.LogInformation($"ErrorDetail: {data.ErrorDetail ?? "なし"}");
                    _logger.LogInformation($"ChunkList件数: {data.ChunkList?.Count ?? 0}");
                    _logger.LogInformation($"TextList件数: {data.TextList?.Count ?? 0}");
                    _logger.LogInformation($"SynonymList件数: {data.SynonymList?.Count ?? 0}");
                    
                    // チャンクの詳細（最初の3件のみ）
                    if (data.ChunkList != null && data.ChunkList.Count > 0)
                    {
                        _logger.LogInformation($"ChunkList最初の3件:");
                        for (int i = 0; i < Math.Min(3, data.ChunkList.Count); i++)
                        {
                            var chunk = data.ChunkList[i];
                            var preview = chunk.Chunk?.Length > 100 ? chunk.Chunk.Substring(0, 100) + "..." : chunk.Chunk;
                            _logger.LogInformation($"  Chunk[{i}] - ChunkNo:{chunk.ChunkNo}, PageNo:{chunk.PageNo}, Content:{preview}");
                        }
                    }
                    
                    // テキストの詳細（最初の3件のみ）
                    if (data.TextList != null && data.TextList.Count > 0)
                    {
                        _logger.LogInformation($"TextList最初の3件:");
                        for (int i = 0; i < Math.Min(3, data.TextList.Count); i++)
                        {
                            var text = data.TextList[i];
                            var preview = text.Text?.Length > 100 ? text.Text.Substring(0, 100) + "..." : text.Text;
                            _logger.LogInformation($"  Text[{i}] - PageNo:{text.PageNo}, Content:{preview}");
                        }
                    }
                    
                    // シノニムの詳細（最初の5件のみ）
                    if (data.SynonymList != null && data.SynonymList.Count > 0)
                    {
                        _logger.LogInformation($"SynonymList最初の5件:");
                        for (int i = 0; i < Math.Min(5, data.SynonymList.Count); i++)
                        {
                            var synonym = data.SynonymList[i];
                            var synonymsText = string.Join(", ", synonym.Synonyms?.Take(3) ?? new List<string>());
                            _logger.LogInformation($"  Synonym[{i}] - Keyword:{synonym.Keyword}, Synonyms:[{synonymsText}]");
                        }
                    }
                    
                    _logger.LogInformation($"=== 外部API /Check取得結果終了 (workId: {workId}) ===");
                    
                    // キャッシュサイズ制限チェック
                    if (_dataCache.Count >= _maxCacheSize)
                    {
                        await CleanupCache();
                    }

                    // キャッシュに保存
                    var now = DateTime.UtcNow;
                    _dataCache[workId] = new CachedStructuredData 
                    { 
                        Data = data, 
                        CachedAt = now,
                        LastAccessed = now
                    };
                    
                    _logger.LogInformation($"workId {workId} のデータをキャッシュに保存しました");
                }
                else
                {
                    _logger.LogWarning($"外部API /Check でworkId {workId} のデータがnullでした");
                }
                
                return data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"workId {workId} のキャッシュ処理でエラーが発生しました。直接API呼び出しを試行します");
                // エラー時は直接API呼び出しを試行
                var directApiData = await _autoStructureService.GetStructuredDataAsync(workId);
                if (directApiData != null)
                {
                    _logger.LogInformation($"workId {workId} の直接API呼び出しが成功しました");
                }
                else
                {
                    _logger.LogWarning($"workId {workId} の直接API呼び出しもnullでした");
                }
                return directApiData;
            }
        }

        /// <summary>
        /// キャッシュのクリーンアップ（LRU方式）
        /// </summary>
        private async Task CleanupCache()
        {
            try
            {
                
                // 期限切れのキャッシュを削除
                var expiredKeys = _dataCache
                    .Where(kvp => kvp.Value.IsExpired)
                    .Select(kvp => kvp.Key)
                    .ToList();
                
                foreach (var key in expiredKeys)
                {
                    _dataCache.TryRemove(key, out _);
                }
                

                // まだサイズ制限を超えている場合は、LRU方式で削除
                if (_dataCache.Count >= _maxCacheSize)
                {
                    var removeCount = _dataCache.Count - _maxCacheSize + 10; // 10件余裕を持って削除
                    var lruKeys = _dataCache
                        .OrderBy(kvp => kvp.Value.LastAccessed)
                        .Take(removeCount)
                        .Select(kvp => kvp.Key)
                        .ToList();
                    
                    foreach (var key in lruKeys)
                    {
                        _dataCache.TryRemove(key, out _);
                    }
                    
                }
                
            }
            catch (Exception ex)
            {
            }
        }

        /// <summary>
        /// 特定のworkIdのキャッシュを無効化
        /// </summary>
        private void InvalidateCache(string workId)
        {
            if (_dataCache.TryRemove(workId, out _))
            {
            }
        }

        /// <summary>
        /// 全キャッシュを無効化
        /// </summary>
        private void ClearAllCache()
        {
            var count = _dataCache.Count;
            _dataCache.Clear();
        }
        
        // ログメッセージを追加するヘルパーメソッド
        private void AddProcessingLog(string processId, string message, bool isError = false)
        {
            // ログを記録
            if (isError)
            {
            }
            else
            {
            }
            
            // ログリストに追加
            if (_processingLogs.TryGetValue(processId, out var logs))
            {
                // タイムスタンプを付けてメッセージを追加
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                string logEntry = $"[{timestamp}] {(isError ? "エラー: " : "")}{message}";
                logs.Add(logEntry);
                
                // 古いログは削除（最大100件まで保持）
                if (logs.Count > 100)
                {
                    logs.RemoveAt(0);
                }
            }
        }
        
        // 処理状況を更新するヘルパーメソッド
        private void UpdateProcessingStatus(string message, string processId = null)
        {
            // 処理状況をログに記録
            
            // プロセスIDが指定されている場合はログに追加
            if (!string.IsNullOrEmpty(processId))
            {
                AddProcessingLog(processId, message);
            }
        }
        /// <summary>
        /// ファイルパスから表示名を生成するヘルパーメソッド
        /// </summary>
        private string GetDisplayNameFromPath(string path)
        {
            try
            {
                // パスからファイル名と親ディレクトリ名を取得
                string fileName = Path.GetFileName(path);
                string dirName = Path.GetDirectoryName(path);

                // PDFファイルのパターンを検出
                // 例: "pdf_20250415_baeda4a3-page-1.txt" のようなパターンからPDF名とページ番号を抽出
                if (fileName.Contains("-page-"))
                {
                    // "pdf_20250415_baeda4a3" のような部分を抽出
                    string fileId = fileName.Split("-page-")[0];

                    // ページ番号部分を抽出 ("1.txt" -> "1")
                    string pageNumber = fileName.Split("-page-")[1].Replace(".txt", "");

                    // メタデータファイルを探す
                    string metadataPath = Path.Combine(dirName, $"{fileId}_metadata.json");
                    string originalFileName = null;

                    if (System.IO.File.Exists(metadataPath))
                    {
                        try
                        {
                            string metadataJson = System.IO.File.ReadAllText(metadataPath);
                            var metadata = JsonDocument.Parse(metadataJson).RootElement;

                            if (metadata.TryGetProperty("OriginalFileName", out var fileNameElement))
                            {
                                originalFileName = fileNameElement.GetString();
                            }
                        }
                        catch (Exception ex)
                        {
                        }
                    }
                    else
                    {
                    }

                    // 元のファイル名があれば使用、なければID使用
                    var displayName = originalFileName ?? fileId;
                    return $"【PDF文書】 {displayName} ({pageNumber + 1}枚目)";
                }

                return fileName;
            }
            catch (Exception ex)
            {
                return path;
            }
        }

        /// <summary>
        /// Forbiddenレスポンスを返すヘルパーメソッド
        /// </summary>
        private IActionResult Forbidden(object value)
        {
            return StatusCode(403, value);
        }

        private readonly ILogger<DataStructuringController> _logger;
        private readonly IFileStorageService _fileStorageService;
        private readonly IConfiguration _configuration;
        private readonly IDocumentChatService _documentChatService;
        private readonly IAutoStructureService _autoStructureService;
        private readonly Services.IAuthorizationService _authorizationService;
        private readonly IAzureSearchService _azureSearchService;
        private readonly IDataIngestionService _dataIngestionService;

        // DataIngestion設定
        private readonly string _azureSearchEndpoint;
        private readonly string _azureSearchKey;
        private readonly string _azureSearchApiVersion;
        private readonly string _mainIndexName;
        private readonly string _sentenceIndexName;
        private readonly string _azureOpenAIEndpoint;
        private readonly string _azureOpenAIKey;
        private readonly string _azureOpenAIApiVersion;
        private readonly string _chatModelDeployment;

        public DataStructuringController(
            ILogger<DataStructuringController> logger,
            IConfiguration configuration,
            IDocumentChatService documentChatService,
            IAutoStructureService autoStructureService,
            Services.IAuthorizationService authorizationService,
            IAzureSearchService azureSearchService,
            IDataIngestionService dataIngestionService,
            IFileStorageService fileStorageService = null)
        {
            _logger = logger;
            _fileStorageService = fileStorageService;
            _configuration = configuration;
            _documentChatService = documentChatService;
            _autoStructureService = autoStructureService;
            _authorizationService = authorizationService;
            _azureSearchService = azureSearchService;
            _dataIngestionService = dataIngestionService;

            // DataIngestion設定を読み込み（oec、oec-sentenceインデックス用）
            var config = _configuration.GetSection("DataIngestion");
            _azureSearchEndpoint = config["AzureSearchEndpoint"];
            _azureSearchKey = config["AzureSearchKey"];
            _azureSearchApiVersion = config["AzureSearchApiVersion"];
            _mainIndexName = config["MainIndexName"];
            _sentenceIndexName = config["SentenceIndexName"];
            _azureOpenAIEndpoint = config["AzureOpenAIEndpoint"];
            _azureOpenAIKey = config["AzureOpenAIKey"];
            _azureOpenAIApiVersion = config["AzureOpenAIApiVersion"];
            _chatModelDeployment = config["ChatModelDeployment"];
        }

        // 全ドキュメント一覧を取得（全ドキュメント検索用）
        [HttpGet("all-documents")]
        public async Task<IActionResult> GetAllDocuments()
        {
            try
            {
                
                // 利用可能な全workIdを取得（実際の実装では外部APIから取得）
                var allWorkIds = await GetAllAvailableWorkIds();
                
                var allDocuments = new List<object>();
                var totalProcessed = 0;
                
                foreach (var workId in allWorkIds)
                {
                    try
                    {
                        
                        // キャッシュ付きでAutoStructureServiceからデータを取得
                        var structuredData = await GetStructuredDataWithCache(workId);
                        
                        if (structuredData?.ChunkList != null && structuredData.ChunkList.Count > 0)
                        {
                            // チャンクデータを個別ドキュメントとして追加
                            foreach (var chunk in structuredData.ChunkList)
                            {
                                allDocuments.Add(new
                                {
                                    id = $"{workId}_chunk_{chunk.PageNo}_{chunk.ChunkNo}",
                                    workId = workId,
                                    title = $"ページ{chunk.PageNo + 1} チャンク{chunk.ChunkNo}",
                                    content = chunk.Chunk,
                                    pageNumber = chunk.PageNo,
                                    chunkNumber = chunk.ChunkNo,
                                    source = $"WorkID: {workId}",
                                    lastUpdated = DateTime.UtcNow
                                });
                            }
                            totalProcessed += structuredData.ChunkList.Count;
                        }
                        else if (structuredData?.TextList != null && structuredData.TextList.Count > 0)
                        {
                            // TextListからドキュメントを追加（フォールバック）
                            for (int i = 0; i < structuredData.TextList.Count; i++)
                            {
                                var textItem = structuredData.TextList[i];
                                allDocuments.Add(new
                                {
                                    id = $"{workId}_text_{i}",
                                    workId = workId,
                                    title = $"テキスト {i + 1}",
                                    content = textItem.Text,
                                    source = $"WorkID: {workId}",
                                    lastUpdated = DateTime.UtcNow
                                });
                            }
                            totalProcessed += structuredData.TextList.Count;
                        }
                        else
                        {
                        }
                    }
                    catch (Exception ex)
                    {
                        // 個別workIdのエラーは継続
                    }
                }
                
                
                return Ok(new
                {
                    documents = allDocuments,
                    totalCount = allDocuments.Count,
                    processedWorkIds = allWorkIds.Count,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "全ドキュメントの取得に失敗しました", details = ex.Message });
            }
        }

        // 全ドキュメント検索エンドポイント
        /// <summary>
        /// DataIngestionに対応した新しいチャット回答生成メソッド
        /// </summary>
        private async Task<(string answer, List<Models.SearchResult> sources)> GenerateAnswerWithDataIngestionAsync(string message, string documentContext = null, string customSystemPrompt = null)
        {
            try
            {
                _logger.LogInformation("🤖 DataIngestion対応チャット回答生成開始: メッセージ='{Message}'", message);

                // 🔍 Step 1: Azure Searchでドキュメント検索
                var searchResults = new List<Models.SearchResult>();
                
                if (string.IsNullOrEmpty(documentContext))
                {
                    // ユーザーがアクセス可能なworkIdを取得
                    var currentUsername = User.Identity.Name;
                    var allowedWorkIds = await _authorizationService.GetAllowedWorkIdsAsync(currentUsername);
                    
                    // ユーザー固有のインデックスを設定
                    _azureSearchService.SetUserSpecificIndexes(currentUsername);
                    
                    // Azure Searchで関連ドキュメントを検索
                    searchResults = await _azureSearchService.SemanticSearchAsync(message, allowedWorkIds, 10);
                    _logger.LogInformation("🔍 Azure Search検索完了: {Count}件取得", searchResults.Count);
                }

                // 🧩 Step 2: コンテキストを構築
                string context;
                if (!string.IsNullOrEmpty(documentContext))
                {
                    context = documentContext;
                    _logger.LogInformation("📄 クライアント提供コンテキスト使用");
                }
                else if (searchResults.Any())
                {
                    context = string.Join("\n\n", searchResults.Take(10).Select(r => 
                        $"【文書ID: {r.Id}】\n{r.Content}"));
                    _logger.LogInformation("🔍 Azure Search結果からコンテキスト構築(Top10): {Length}文字", context.Length);
                }
                else
                {
                    context = "関連する情報が見つかりませんでした。";
                    _logger.LogWarning("⚠️ 検索結果なし、デフォルトコンテキスト使用");
                }

                // 🤖 Step 3: DocumentChatServiceで回答生成
                string answer = await GenerateAIResponseAsync(message, context, customSystemPrompt);
                
                _logger.LogInformation("✅ DataIngestion対応チャット回答生成完了");
                return (answer, searchResults);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ DataIngestion対応チャット回答生成エラー");
                return ("申し訳ありませんが、回答の生成中にエラーが発生しました。", new List<Models.SearchResult>());
            }
        }

        /// <summary>
        /// DataIngestionインデックスから一意のファイルパス（workId）を取得
        /// </summary>
        private async Task<List<string>> GetUniqueFilePathsFromDataIngestionAsync()
        {
            try
            {
                // ユーザーがアクセス可能なworkIdを取得
                var currentUsername = User.Identity.Name;
                var allowedWorkIds = await _authorizationService.GetAllowedWorkIdsAsync(currentUsername);
                
                _logger.LogInformation("📋 DataIngestionから一意ファイルパス取得: {Count}件", allowedWorkIds.Count);
                return allowedWorkIds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ DataIngestionファイルパス取得エラー");
                return new List<string>();
            }
        }

        /// <summary>
        /// DataIngestionインデックスから指定ファイルパスのドキュメント内容を取得
        /// </summary>
        private async Task<string> GetDocumentsByFilePathFromDataIngestionAsync(string filepath)
        {
            try
            {
                // filepathがworkIdとして扱われるケースを想定
                var workId = filepath;
                
                // Azure Searchで該当workIdのドキュメントを検索
                var searchResults = await _azureSearchService.SearchDocumentsAsync("*", new List<string> { workId }, 100);
                
                if (searchResults?.Any() == true)
                {
                    // 検索結果を統合してテキストとして返す
                    var combinedContent = string.Join("\n\n", searchResults.Select(r => 
                        $"【ページ {r.PageNumber + 1}, チャンク {r.ChunkNumber}】\n{r.Content}"));
                    
                    _logger.LogInformation("📄 DataIngestionドキュメント取得成功: workId={WorkId}, 内容長={Length}文字", workId, combinedContent.Length);
                    return combinedContent;
                }
                else
                {
                    _logger.LogWarning("⚠️ DataIngestionドキュメント未発見: workId={WorkId}", workId);
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ DataIngestionドキュメント取得エラー: filepath={FilePath}", filepath);
                return string.Empty;
            }
        }

        /// <summary>
        /// DocumentChatServiceを使用してAI回答を生成
        /// </summary>
        private async Task<string> GenerateAIResponseAsync(string question, string context, string customSystemPrompt = null)
        {
            try
            {
                // システムプロンプト構築
                string systemPrompt = customSystemPrompt ?? @"
あなたは日本語で回答するAIアシスタントです。
提供された文書コンテキストに基づいて、正確で有用な回答を提供してください。
- 簡潔で分かりやすい回答を心がける
- 文書に記載がない情報については推測せず、「文書には記載されていません」と回答する
- 必要に応じて箇条書きや番号付きリストを使用する";

                // 現在のユーザー名を取得
                var currentUsername = User.Identity.Name;
                
                // DocumentChatServiceを使用してClaude APIで回答生成
                var (answer, sources) = await _documentChatService.GenerateAnswer(question, context, systemPrompt, currentUsername);
                
                _logger.LogInformation("🤖 DocumentChatService回答生成成功");
                return answer ?? "回答を生成できませんでした。";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ DocumentChatService呼び出し中にエラー");
                return "申し訳ありませんが、回答の生成中にエラーが発生しました。";
            }
        }

        /// <summary>
        /// 共通のAzure Search ハイブリッドセマンティック検索メソッド
        /// </summary>
        private async Task<List<Models.SearchResult>> PerformAzureSearchAsync(string query, List<string> targetWorkIds = null, int maxResults = 10)
        {
            try
            {
                Console.WriteLine($"🚀 [MAIN] Azure Search検索開始: クエリ='{query}', 最大結果数={maxResults}");
                Console.WriteLine($"🔧 [MAIN] 現在の実装状況: キーワード検索のみ（ベクトル検索実装予定）");
                
                // ユーザー固有のインデックスを設定
                var currentUsername = User.Identity.Name;
                _azureSearchService.SetUserSpecificIndexes(currentUsername);
                
                // 検索対象のworkIdを決定
                if (targetWorkIds == null || !targetWorkIds.Any())
                {
                    targetWorkIds = await GetAllAvailableWorkIds();
                }
                
                Console.WriteLine($"🔍 [MAIN] 対象workId一覧: [{string.Join(", ", targetWorkIds)}]");
                
                // Azure Search ハイブリッドセマンティック検索を実行（注入されたサービスを使用）
                Console.WriteLine($"🔄 [MAIN] SemanticSearchAsync呼び出し（ベクトル検索 + キーワード検索のハイブリッド）");
                // デバッグ用: 検索結果を多めに取得（50件に増加）
                var searchResults = await _azureSearchService.SemanticSearchAsync(query, targetWorkIds, Math.Max(maxResults, 50));
                
                Console.WriteLine($"✅ [MAIN] Azure Search検索完了: {searchResults.Count}件取得");
                
                // 🔍 検索結果をデバッグログ出力（50件まで）
                Console.WriteLine("📋 === Azure Search検索結果詳細（上位50件） ===");
                for (int i = 0; i < Math.Min(50, searchResults.Count); i++)
                {
                    var result = searchResults[i];
                    Console.WriteLine($"📄 [{i + 1}] ID: {result.Id}");
                    Console.WriteLine($"    WorkID: {result.Filepath}");
                    Console.WriteLine($"    タイトル: {result.Title}");
                    Console.WriteLine($"    ページ: {result.PageNumber + 1}, チャンク: {result.ChunkNumber}");
                    Console.WriteLine($"    スコア: {result.Score:F4}");
                    Console.WriteLine($"    マッチキーワード: {result.MatchedKeywords ?? "なし"}");
                    Console.WriteLine($"    コンテンツ（先頭100文字）: {(result.Content?.Length > 100 ? result.Content.Substring(0, 100) + "..." : result.Content)}");
                    Console.WriteLine("    ---");
                }
                Console.WriteLine("📋 === 検索結果詳細終了 ===");
                
                // 🔍 特定のworkIdが含まれているかチェック（詳細情報付き）
                var targetWorkId = "90a1db69fbd6174549afea9da68caff1";
                var targetResults = searchResults.Where(r => r.Filepath == targetWorkId).ToList();
                Console.WriteLine($"🎯 [DEBUG] 特定workId '{targetWorkId}' の検索結果: {targetResults.Count}件");
                
                if (targetResults.Count > 0)
                {
                    Console.WriteLine("🎯 [DEBUG] 該当workIdの詳細:");
                    foreach (var result in targetResults.Take(3))
                    {
                        Console.WriteLine($"  - ID: {result.Id}");
                        Console.WriteLine($"    スコア: {result.Score:F4}");
                        Console.WriteLine($"    ランキング: {searchResults.IndexOf(result) + 1}位");
                        Console.WriteLine($"    コンテンツ: {(result.Content?.Length > 200 ? result.Content.Substring(0, 200) + "..." : result.Content)}");
                        Console.WriteLine("    ---");
                    }
                }
                else
                {
                    Console.WriteLine("🎯 [DEBUG] 該当workIdは検索結果に含まれていません");
                    Console.WriteLine("🎯 [DEBUG] 検索対象workIds一覧:");
                    foreach (var workId in targetWorkIds.Take(10))
                    {
                        Console.WriteLine($"  - {workId}");
                    }
                }
                
                return searchResults;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [MAIN] Azure Search検索エラー: {ex.Message}");
                Console.WriteLine($"❌ [MAIN] スタックトレース: {ex.StackTrace}");
                return new List<Models.SearchResult>();
            }
        }

        [HttpPost("search-all-documents")]
        public async Task<IActionResult> SearchAllDocuments([FromBody] SearchAllDocumentsRequest request)
        {
            try
            {
                // 🔐 ASP.NET認証チェック（統一認証）
                if (!User?.Identity?.IsAuthenticated ?? true)
                {
                    return Unauthorized(new { message = "認証が必要です" });
                }

                var currentUsername = User.Identity.Name;
                var currentRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "User";

                _logger.LogInformation("検索認証（ASP.NET統一）: ユーザー={Username}, ロール={Role}", currentUsername, currentRole);

                // ユーザーがアクセス可能なworkIdを取得（ASP.NET認証ユーザーで）
                var allowedWorkIds = await _authorizationService.GetAllowedWorkIdsAsync(currentUsername);
                
                // ユーザー固有のインデックスを設定
                _azureSearchService.SetUserSpecificIndexes(currentUsername);
                
                // 検索対象のworkIdを決定（ユーザー権限に基づく）
                List<string> targetWorkIds;
                if (string.IsNullOrEmpty(request.WorkId))
                {
                    // workId指定なし → ユーザーがアクセス可能な全workId
                    targetWorkIds = allowedWorkIds;
                }
                else
                {
                    // workId指定あり → アクセス権限チェック
                    if (!allowedWorkIds.Contains(request.WorkId))
                    {
                        return Forbidden(new { message = $"workId '{request.WorkId}' へのアクセス権限がありません" });
                    }
                    targetWorkIds = new List<string> { request.WorkId };
                }
                
                // 共通の検索メソッドを使用
                var searchResults = await PerformAzureSearchAsync(request.Query, targetWorkIds, 10);
                
                // 結果をフロントエンド用形式に変換
                var documents = searchResults.Select(result => new
                {
                    id = result.Id,
                    workId = result.Filepath, // workIdがFilepathに格納されている
                    title = result.Title,
                    content = result.Content,
                    pageNumber = result.PageNumber,
                    chunkNumber = result.ChunkNumber,
                    source = $"WorkID: {result.Filepath}, ページ{result.PageNumber + 1}",
                    relevanceScore = result.Score,
                    matchedKeywords = result.MatchedKeywords
                }).ToList();
                
                Console.WriteLine($"🔍 検索結果変換完了: {documents.Count}件を返却");
                
                return Ok(new
                {
                    documents = documents,
                    totalFound = searchResults.Count,
                    query = request.Query,
                    searchTime = DateTime.UtcNow,
                    searchMethod = "Azure Search ハイブリッドセマンティック検索"
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ search-all-documents エラー: {ex.Message}");
                return StatusCode(500, new { error = "Azure Search検索に失敗しました", details = ex.Message });
            }
        }

        // 関連度スコア計算（改良版）
        private double CalculateRelevanceScore(string content, string[] keywords)
        {
            var score = 0.0;
            var contentLower = content.ToLower();
            
            foreach (var keyword in keywords)
            {
                var keywordLower = keyword.ToLower();
                
                // 1. 完全一致の検索
                var exactMatches = (contentLower.Length - contentLower.Replace(keywordLower, "").Length) / keywordLower.Length;
                score += exactMatches * keywordLower.Length * 2.0; // 完全一致には高い重み
                
                // 2. 単語レベルでの部分一致検索（より細かく分割）
                var words = keywordLower.Split(new char[] { ' ', '　', '、', '。', '？', '！', '?', '!', 'の', 'が', 'を', 'に', 'で', 'と', 'は', 'も' }, 
                    StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var word in words)
                {
                    if (word.Length >= 2) // 2文字以上の単語のみ検索
                    {
                        var wordMatches = (contentLower.Length - contentLower.Replace(word, "").Length) / word.Length;
                        score += wordMatches * word.Length * 0.5; // 部分一致には低い重み
                    }
                }
                
                // 3. 数字や年度の部分一致（特別処理）
                var numberPattern = @"\d{4}年\d+月期|\d{4}年|\d+四半期|\d+期";
                var keywordNumbers = System.Text.RegularExpressions.Regex.Matches(keywordLower, numberPattern);
                var contentNumbers = System.Text.RegularExpressions.Regex.Matches(contentLower, numberPattern);
                
                foreach (System.Text.RegularExpressions.Match keywordMatch in keywordNumbers)
                {
                    foreach (System.Text.RegularExpressions.Match contentMatch in contentNumbers)
                    {
                        if (keywordMatch.Value == contentMatch.Value)
                        {
                            score += keywordMatch.Value.Length * 1.0; // 数字一致には中程度の重み
                        }
                    }
                }
            }
            
            return score;
        }

        // キーワード抽出APIを呼び出すヘルパーメソッド
        private async Task<List<string>> ExtractKeywordsFromQuery(string query)
        {
            try
            {
                Console.WriteLine($"🔍 キーワード抽出API呼び出し開始: {query}");
                
                // AWS ALBからプライベートIPアドレスを取得してTokenize APIを呼び出し
                try
                {
                    Console.WriteLine("🔍 AWS ALBからTokenize API用プライベートIPアドレスを取得中...");
                    
                    var healthyEndpoints = await GetHealthyEndpointsFromTokenizeAPI();
                    
                    if (healthyEndpoints != null && healthyEndpoints.Count > 0)
                    {
                        Console.WriteLine($"✅ 取得したTokenize APIプライベートエンドポイント: {string.Join(", ", healthyEndpoints)}");
                        
                        // 各プライベートエンドポイントを試行
                        foreach (var endpoint in healthyEndpoints)
                        {
                            try
                            {
                                var apiUrl = $"{endpoint}/api/Tokenize";
                                Console.WriteLine($"🔍 プライベートTokenize API呼び出し: {apiUrl}");
                                
                                var requestData = new
                                {
                                    userId = _configuration["DataIngestion:ExternalApiUserId"] ?? "ilu-demo",
                                    password = _configuration["DataIngestion:ExternalApiPassword"] ?? "ilupass",
                                    type = "",
                                    text = query
                                };
                                
                                var jsonContent = JsonSerializer.Serialize(requestData);
                                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                                
                                using var httpClient = new HttpClient();
                                httpClient.Timeout = TimeSpan.FromSeconds(30);
                                
                                var response = await httpClient.PostAsync(apiUrl, content);
                                Console.WriteLine($"🔍 プライベートTokenize API レスポンスステータス: {(int)response.StatusCode} {response.StatusCode}");
                                
                                if (response.IsSuccessStatusCode)
                                {
                                    var responseContent = await response.Content.ReadAsStringAsync();
                                    Console.WriteLine($"🔍 プライベートTokenize API レスポンス（先頭200文字）: {responseContent.Substring(0, Math.Min(200, responseContent.Length))}");
                                    
                                    var tokenizeResponse = JsonSerializer.Deserialize<TokenizeApiResponse>(responseContent, new JsonSerializerOptions
                                    {
                                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                                    });
                                    
                                    if (tokenizeResponse?.TokenList != null && tokenizeResponse.TokenList.Count > 0)
                                    {
                                        var keywords = tokenizeResponse.TokenList
                                            .Where(token => !string.IsNullOrEmpty(token.Text) && token.Text.Length >= 2)
                                            .OrderByDescending(token => token.BoostScore)
                                            .Take(10)
                                            .Select(token => token.Text)
                                            .Distinct()
                                            .ToList();
                                        
                                        Console.WriteLine($"✅ プライベートTokenize API キーワード抽出成功: {keywords.Count}件");
                                        Console.WriteLine($"🔍 抽出キーワード: [{string.Join(", ", keywords)}]");
                                        return keywords;
                                    }
                                    else
                                    {
                                        Console.WriteLine("❌ プライベートTokenize API: TokenListがnullまたは空");
                                    }
                                }
                                else
                                {
                                    var errorContent = await response.Content.ReadAsStringAsync();
                                    Console.WriteLine($"❌ プライベートTokenize API エラー: {response.StatusCode} - {errorContent}");
                                }
                            }
                            catch (Exception endpointEx)
                            {
                                Console.WriteLine($"❌ プライベートエンドポイント {endpoint} 呼び出しエラー: {endpointEx.Message}");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("❌ プライベートエンドポイントが取得できませんでした");
                    }
                }
                catch (Exception awsEx)
                {
                    Console.WriteLine($"❌ AWS ALBエンドポイント取得エラー: {awsEx.Message}");
                }
                
                Console.WriteLine("❌ プライベートTokenize API呼び出しが失敗、フォールバック処理");
                return ExtractKeywordsFallback(query);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ キーワード抽出エラー: {ex.Message}");
                Console.WriteLine($"❌ スタックトレース: {ex.StackTrace}");
                return ExtractKeywordsFallback(query);
            }
        }

        /// <summary>
        /// Tokenize API用のAWS ALBから健全なエンドポイントを取得
        /// </summary>
        private async Task<List<string>> GetHealthyEndpointsFromTokenizeAPI()
        {
            try
            {
                Console.WriteLine("🔍 Tokenize API用 AWS ALBエンドポイント取得開始");
                
                // AWS設定 - Tokenize API専用のターゲットグループARN
                var awsRegion = Amazon.RegionEndpoint.APNortheast1;
                var targetGroupArn = "arn:aws:elasticloadbalancing:ap-northeast-1:311141529894:targetgroup/ilurag-tokenizer2/d770879f3d19c662";
                
                Console.WriteLine($"🔍 使用するターゲットグループARN: {targetGroupArn}");
                
                var client = new Amazon.ElasticLoadBalancingV2.AmazonElasticLoadBalancingV2Client(awsRegion);
                
                var request = new Amazon.ElasticLoadBalancingV2.Model.DescribeTargetHealthRequest
                {
                    TargetGroupArn = targetGroupArn
                };
                
                var response = await client.DescribeTargetHealthAsync(request);
                var healthyEndpoints = new List<string>();
                
                foreach (var description in response.TargetHealthDescriptions)
                {
                    var target = description.Target;
                    var health = description.TargetHealth;
                    
                    Console.WriteLine($"🔍 Tokenize API ターゲット: {target.Id}, ポート: {target.Port}, 状態: {health.State}, 理由: {health.Reason ?? "N/A"}");
                    
                    if (health.State == Amazon.ElasticLoadBalancingV2.TargetHealthStateEnum.Healthy)
                    {
                        // ポート9926を強制使用
                        var endpoint = $"http://{target.Id}:9926";
                        healthyEndpoints.Add(endpoint);
                        Console.WriteLine($"✅ Tokenize API 健全なエンドポイント: {endpoint}");
                    }
                }
                
                if (healthyEndpoints.Count > 0)
                {
                    Console.WriteLine($"✅ Tokenize API 健全なエンドポイント {healthyEndpoints.Count}件取得");
                }
                else
                {
                    Console.WriteLine("❌ Tokenize API 健全なエンドポイントが見つかりません");
                    // フォールバック - 指定されたIPアドレスを使用
                    healthyEndpoints.Add("http://10.24.142.213:9926");
                    Console.WriteLine("🔄 フォールバック: http://10.24.142.213:9926 を使用");
                }
                
                return healthyEndpoints;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Tokenize API AWS ALBエンドポイント取得エラー: {ex.Message}");
                Console.WriteLine($"❌ エラー詳細: {ex.StackTrace}");
                // フォールバック - 指定されたIPアドレスを使用
                var fallbackEndpoints = new List<string> { "http://10.24.142.213:9926" };
                Console.WriteLine("🔄 フォールバック: http://10.24.142.213:9926 を使用");
                return fallbackEndpoints;
            }
        }

        // フォールバック用のキーワード抽出
        private List<string> ExtractKeywordsFallback(string query)
        {
            var keywords = new List<string>();
            
            // 1. 基本的な分割
            var basicWords = query.Split(new char[] { ' ', '　', '、', '。', '？', '！', '?', '!', 'の', 'が', 'を', 'に', 'で', 'と', 'は', 'も' }, 
                StringSplitOptions.RemoveEmptyEntries)
                .Where(word => word.Length >= 2)
                .ToList();
            
            keywords.AddRange(basicWords);
            
            // 2. 数字や年度の抽出
            var numberPattern = @"\d{4}年\d*月期|\d{4}年|\d+四半期|\d+期|第\d+四半期|通期|業績|見通し|実績";
            var matches = System.Text.RegularExpressions.Regex.Matches(query, numberPattern);
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                keywords.Add(match.Value);
            }
            
            // 3. 重要な名詞の抽出（簡易版）
            var importantWords = new[] { "業績", "実績", "見通し", "通期", "四半期", "決算", "売上", "利益", "収益" };
            foreach (var word in importantWords)
            {
                if (query.Contains(word))
                {
                    keywords.Add(word);
                }
            }
            
            return keywords.Distinct().ToList();
        }

        // シノニムキーワードを取得するヘルパーメソッド
        private async Task<List<string>> GetSynonymKeywords(List<string> keywords, List<string> workIds)
        {
            try
            {
                Console.WriteLine($"🔍 シノニム取得開始: {keywords.Count}件のキーワード");
                
                var synonymKeywords = new List<string>();
                
                // 各workIdからシノニムデータを取得
                foreach (var workId in workIds)
                {
                    try
                    {
                        var structuredData = await GetStructuredDataWithCache(workId);
                        if (structuredData?.SynonymList != null)
                        {
                            // キーワードに対応するシノニムを検索
                            foreach (var keyword in keywords)
                            {
                                var synonymMatches = this.FindSynonymsForKeywords(new List<string> { keyword }, structuredData.SynonymList);
                                foreach (var match in synonymMatches)
                                {
                                    synonymKeywords.AddRange(match.RelatedSynonyms);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ workId {workId} のシノニム取得エラー: {ex.Message}");
                    }
                }
                
                var uniqueSynonyms = synonymKeywords.Distinct().ToList();
                Console.WriteLine($"✅ シノニム取得完了: {uniqueSynonyms.Count}件");
                
                return uniqueSynonyms;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ シノニム取得エラー: {ex.Message}");
                return new List<string>();
            }
        }

        // 利用可能なworkIdリストを取得（実装例）
        private async Task<List<string>> GetAllAvailableWorkIds()
        {
            try
            {
                Console.WriteLine("🔍 GetAllAvailableWorkIds開始（認証システム使用）");
                
                // 現在のユーザー名を取得
                var currentUser = User.Identity?.Name;
                Console.WriteLine($"🔍 取得したユーザー名: {currentUser}");
                
                if (string.IsNullOrEmpty(currentUser))
                {
                    Console.WriteLine("❌ ユーザーが認証されていないため、空のリストを返却");
                    return new List<string>();
                }
                
                // 認証システムからユーザーがアクセス可能なworkIdを取得
                var allowedWorkIds = await _authorizationService.GetAllowedWorkIdsAsync(currentUser);
                
                Console.WriteLine($"✅ 認証システムから取得したworkId数: {allowedWorkIds.Count}");
                
                // 先頭の数件をログ出力（デバッグ用）
                for (int i = 0; i < Math.Min(5, allowedWorkIds.Count); i++)
                {
                    Console.WriteLine($"🔍 許可workId[{i}]: {allowedWorkIds[i]}");
                }
                
                // 管理者の場合の特別ログ
                var currentRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "User";
                if (currentRole == "Admin")
                {
                    Console.WriteLine($"✅ 管理者ユーザー '{currentUser}' が全workId({allowedWorkIds.Count}件)にアクセス");
                }
                else
                {
                    Console.WriteLine($"✅ 一般ユーザー '{currentUser}' が{allowedWorkIds.Count}件のworkIdにアクセス");
                }
                
                return allowedWorkIds;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ GetAllAvailableWorkIdsでエラー: {ex.Message}");
                _logger.LogError(ex, "workId取得中にエラーが発生しました");
                // エラー時は空のリストを返す
                return new List<string>();
            }
        }

        /// <summary>
        /// 現在の認証済みユーザー情報を取得
        /// </summary>
        [HttpGet("current-user")]
        public IActionResult GetCurrentUser()
        {
            try
            {
                if (!User?.Identity?.IsAuthenticated ?? true)
                {
                    return Unauthorized(new { error = "認証が必要です" });
                }

                var username = User.Identity.Name;
                var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "User";

                return Ok(new
                {
                    success = true,
                    user = new
                    {
                        username = username,
                        role = role,
                        isAuthenticated = true
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "現在のユーザー情報取得でエラーが発生しました");
                return StatusCode(500, new { error = "内部サーバーエラーが発生しました" });
            }
        }

        [HttpGet("allowed-indexes")]
        public async Task<IActionResult> GetAllowedIndexes()
        {
            try
            {
                if (!User?.Identity?.IsAuthenticated ?? true)
                {
                    return Unauthorized(new { error = "認証が必要です" });
                }

                var username = User.Identity.Name;
                _logger.LogInformation("許可インデックス一覧API呼び出し: ユーザー={Username}", username);

                var allowedIndexes = await _authorizationService.GetAllowedIndexesAsync(username);

                return Ok(new
                {
                    success = true,
                    username = username,
                    allowedIndexes = allowedIndexes,
                    indexingEnabled = allowedIndexes.Count > 0
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "許可インデックス一覧取得でエラーが発生しました");
                return StatusCode(500, new { error = "内部サーバーエラーが発生しました" });
            }
        }

        /// <summary>
        /// 現在のユーザーIDを取得
        /// </summary>
        private string GetCurrentUserId()
        {
            try
            {
                // 認証済みユーザーのIDを取得
                var userId = User?.Identity?.Name;
                
                // ユーザーIDが取得できない場合はクレームから取得を試行
                if (string.IsNullOrEmpty(userId))
                {
                    userId = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                }
                
                // それでも取得できない場合はIPアドレスベースのフォールバック（開発用）
                if (string.IsNullOrEmpty(userId))
                {
                    var ipAddress = HttpContext?.Connection?.RemoteIpAddress?.ToString();
                    userId = $"guest_{ipAddress?.Replace(".", "_").Replace(":", "_") ?? "unknown"}";
                }
                
                // セキュリティのためファイル名として安全な文字列に変換
                userId = userId.Replace("/", "_").Replace("\\", "_").Replace(":", "_");
                
                return userId;
            }
            catch (Exception ex)
            {
                return "guest_error";
            }
        }

        /// <summary>
        /// workId履歴にエントリを追加
        /// </summary>
        private async Task AddWorkIdToHistory(string workId, string fileName)
        {
            try
            {
                
                // 現在のユーザーIDを取得
                var userId = GetCurrentUserId();
                if (string.IsNullOrEmpty(userId))
                {
                    return;
                }
                
                // ストレージディレクトリの確認・作成
                // ユーザー専用の履歴ファイルパス（ルートディレクトリに作成）
                var historyFilePath = $"user_{userId}_workid_history.json";
                
                // 既存の履歴を読み込み
                List<WorkIdHistoryItem> historyData;
                if (System.IO.File.Exists(historyFilePath))
                {
                    var existingJson = await System.IO.File.ReadAllTextAsync(historyFilePath);
                    historyData = JsonSerializer.Deserialize<List<WorkIdHistoryItem>>(existingJson) ?? new List<WorkIdHistoryItem>();
                }
                else
                {
                    historyData = new List<WorkIdHistoryItem>();
                }
                
                // 重複チェック（同じworkIdがあれば更新）
                var existingIndex = historyData.FindIndex(item => item.WorkId == workId);
                var newEntry = new WorkIdHistoryItem
                {
                    WorkId = workId,
                    FileName = fileName,
                    UploadDate = DateTime.UtcNow
                };
                
                if (existingIndex >= 0)
                {
                    historyData[existingIndex] = newEntry;
                }
                else
                {
                    historyData.Insert(0, newEntry); // 最新のものを先頭に追加
                }
                
                // 履歴サイズ制限（最新100件まで）
                if (historyData.Count > 100)
                {
                    historyData = historyData.Take(100).ToList();
                }
                
                // ファイルに保存
                var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
                var updatedJson = JsonSerializer.Serialize(historyData, jsonOptions);
                await System.IO.File.WriteAllTextAsync(historyFilePath, updatedJson);
                
            }
            catch (Exception ex)
            {
            }
        }

        // 構造化テキストファイルの一覧を取得
        [HttpGet("files")]
        public async Task<IActionResult> GetFiles()
        {
            try
            {
                if (_fileStorageService == null)
                {
                    return Ok(new List<object>());
                }
                
                var files = await _fileStorageService.GetFileListAsync();

                // テキストファイルのみをフィルタリング
                var textFiles = files
                    .Where(f => f.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) &&
                           (f.FileName.Contains("構造化") || f.FileName.Contains("structur")))
                    .Select(f => new
                    {
                        id = f.Id,
                        name = f.FileName,
                        timestamp = f.UploadDate
                    })
                    .OrderByDescending(f => f.timestamp)
                    .ToList();


                return Ok(textFiles);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "ファイル一覧の取得に失敗しました" });
            }
        }

        // ファイルパスのリストを取得（AutoStructureServiceを使用）
        [HttpGet("filepaths")]
        public async Task<IActionResult> GetFilePaths([FromQuery] string workId = null)
        {
            try
            {
                // 【セキュリティ強化】現在のユーザーの認証制御を追加
                var currentUser = User.Identity?.Name;
                if (string.IsNullOrEmpty(currentUser))
                {
                    return Unauthorized("ユーザーが認証されていません");
                }
                
                // 現在のユーザーがアクセス可能なworkIdを取得
                var allowedWorkIds = await GetAllAvailableWorkIds();
                
                // workIdが指定されている場合は権限チェック
                if (!string.IsNullOrEmpty(workId))
                {
                    if (!allowedWorkIds.Contains(workId))
                    {
                        _logger.LogWarning($"ユーザー {currentUser} が認可されていないworkId {workId} にアクセスを試行しました");
                        return Forbidden(new { message = "指定されたworkIdへのアクセス権限がありません", workId });
                    }
                    _logger.LogInformation($"ユーザー {currentUser} がworkId {workId} へのアクセスを許可しました");
                }
                else
                {
                    // workIdが指定されていない場合は、ユーザーの最初のworkIdを使用
                    if (!allowedWorkIds.Any())
                    {
                        return Ok(new { pages = new List<object>(), processing_status = new { }, synonym_list = new List<object>(), synonym = new List<object>() });
                    }
                    workId = allowedWorkIds.First();
                    _logger.LogInformation($"ユーザー {currentUser} のデフォルトworkId {workId} を使用します");
                }
                
                try
                {
                    // キャッシュ付きでAutoStructureServiceからデータを取得
                    var structuredData = await GetStructuredDataWithCache(workId);

                    // 1) page_text_list を最優先で使用
                    if (structuredData.PageTextList != null && structuredData.PageTextList.Count > 0)
                    {
                        var pageItems = structuredData.PageTextList
                            .OrderBy(p => p.PageNo)
                            .Select(p => new
                            {
                                id = $"page_{p.PageNo}",
                                name = $"{p.PageNo + 1}枚目",
                                filepath = $"page_{p.PageNo}_0",
                                text = p.Text,
                                pageNumber = p.PageNo,
                                originalIndex = 0
                            }).ToList();

                        var response = new
                        {
                            pages = pageItems,
                            processing_status = new
                            {
                                page_no = structuredData.PageNo,
                                max_page_no = structuredData.MaxPageNo,
                                processing_state = structuredData.GetProcessingState().ToString(),
                                return_code = structuredData.ReturnCode,
                                error_detail = structuredData.ErrorDetail
                            },
                            synonym_list = structuredData.SynonymList,
                            synonym = structuredData.SynonymData
                        };
                        return Ok(response);
                    }
                    // 2) text_list を使用（従来フォールバック）
                    else if (structuredData.TextList != null && structuredData.TextList.Count > 0)
                    {
                        
                        // 最初の数個のテキストサンプルをログに出力
                        for (int i = 0; i < Math.Min(3, structuredData.TextList.Count); i++)
                        {
                            var sampleText = structuredData.TextList[i].Text;
                        }
                        
                        // ヘルパー関数: Pythonスクリプトと同様のロジックでテキストからページ番号を抽出
                        int ExtractPageNumber(string text)
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(text, @"p\.(\d+)");
                            if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                            {
                                return num;
                            }
                            return int.MaxValue; // ページ番号が見つからない場合は最後に配置
                        }
                        
                        // ダブルクォーテーションを除去する関数
                        string CleanText(string text)
                        {
                            var originalLength = text?.Length ?? 0;
                            var cleaned = text?.Trim('"') ?? "";
                            var newLength = cleaned.Length;
                            
                            if (originalLength != newLength)
                            {
                            }
                            return cleaned;
                        }
                        
                        
                        // テキストアイテムをページ番号でソート
                        var sortedItems = new List<dynamic>();
                        foreach (var item in structuredData.TextList.Select((value, index) => new { Value = value, Index = index }))
                        {
                            var pageNumber = ExtractPageNumber(item.Value.Text);
                            var cleanText = CleanText(item.Value.Text);
                            
                            
                            sortedItems.Add(new
                            {
                                id = $"page_{item.Index}",
                                originalText = item.Value.Text,
                                text = cleanText,
                                pageNumber = pageNumber,
                                originalIndex = item.Index
                            });
                        }
                        
                        // ページ番号でソート
                        sortedItems = sortedItems.OrderBy(item => item.pageNumber).ToList();
                        
                        // ソート後のページ番号順序をログに出力
                        var pageSequence = string.Join(", ", sortedItems.Take(10).Select(item => item.pageNumber));
                        
                        
                        // 結果を生成
                        
                        var result = new List<dynamic>();
                        foreach (var entry in sortedItems.Select((value, index) => new { Value = value, Index = index }))
                        {
                            string displayName;
                            if (entry.Value.pageNumber == int.MaxValue)
                            {
                                displayName = $"テキスト #{entry.Index + 1}";
                            }
                            else
                            {
                                displayName = $"{entry.Value.pageNumber + 1}枚目";
                            }
                            
                            var id = $"page_{entry.Value.pageNumber}_{entry.Value.originalIndex}";
                            
                            var resultItem = new
                            {
                                id = id,
                                name = displayName,
                                filepath = id,
                                text = entry.Value.text,
                                pageNumber = entry.Value.pageNumber,
                                originalIndex = entry.Value.originalIndex
                            };
                            
                            result.Add(resultItem);
                        }
                        
                        
                        // 最初の数件のデータをサンプルとしてログ出力
                        for (int i = 0; i < Math.Min(3, result.Count); i++)
                        {
                        }
                        
                        
                        // シノニムデータの詳細ログ出力（TextList用レスポンス）
                        if (structuredData.SynonymList != null && structuredData.SynonymList.Count > 0)
                        {
                            for (int i = 0; i < Math.Min(3, structuredData.SynonymList.Count); i++)
                            {
                                var synonymItem = structuredData.SynonymList[i];
                                if (synonymItem?.Synonyms != null)
                                {
                                }
                            }
                        }
                        
                        if (structuredData.SynonymData != null && structuredData.SynonymData.Count > 0)
                        {
                            for (int i = 0; i < Math.Min(3, structuredData.SynonymData.Count); i++)
                            {
                                var synonymDataItem = structuredData.SynonymData[i];
                            }
                        }
                        else
                        {
                        }
                        
                        // 処理進捗情報を含めたレスポンスを返す
                        var response = new
                        {
                            pages = result,
                            processing_status = new
                            {
                                page_no = structuredData.PageNo,
                                max_page_no = structuredData.MaxPageNo,
                                processing_state = structuredData.GetProcessingState().ToString(),
                                return_code = structuredData.ReturnCode,
                                error_detail = structuredData.ErrorDetail
                            },
                            synonym_list = structuredData.SynonymList,
                            synonym = structuredData.SynonymData
                        };
                        
                        
                        return Ok(response);
                    }
                    // 3) chunk_list を最後のフォールバック（ページ毎に結合せず、従来通り）
                    else if (structuredData.ChunkList != null && structuredData.ChunkList.Count > 0)
                    {
                        // ページ番号でグルーピング（簡易）
                        var pageGroups = structuredData.ChunkList
                            .GroupBy(chunk => chunk.PageNo)
                            .Select(group => new
                            {
                                id = $"page_{group.Key}",
                                name = $"{group.Key + 1}枚目",
                                pageNumber = group.Key,
                                documents = group.Select(chunk => new
                                {
                                    id = $"chunk_{chunk.PageNo}_{chunk.ChunkNo}",
                                    name = $"チャンク #{chunk.ChunkNo}",
                                    text = chunk.Chunk,
                                    filepath = $"chunk_{chunk.PageNo}_{chunk.ChunkNo}",
                                    pageNumber = chunk.PageNo,
                                    chunkNumber = chunk.ChunkNo,
                                    originalIndex = chunk.ChunkNo
                                }).OrderBy(c => c.chunkNumber).ToList()
                            })
                            .OrderBy(g => g.pageNumber)
                            .ToList();

                        var response = new
                        {
                            pages = pageGroups,
                            processing_status = new
                            {
                                page_no = structuredData.PageNo,
                                max_page_no = structuredData.MaxPageNo,
                                processing_state = structuredData.GetProcessingState().ToString(),
                                return_code = structuredData.ReturnCode,
                                error_detail = structuredData.ErrorDetail
                            },
                            synonym_list = structuredData.SynonymList,
                            synonym = structuredData.SynonymData
                        };
                        return Ok(response);
                    }
                    else
                    {
                        return Ok(new List<object>());
                    }
                }
                catch (Exception innerEx)
                {
                    
                    if (innerEx.InnerException != null)
                    {
                    }
                    
                    throw; // 元の例外を再スロー
                }
            }
            catch (Exception ex)
            {
                
                if (ex.InnerException != null)
                {
                }
                
                return StatusCode(500, new { error = "ファイルパス一覧の取得に失敗しました" });
            }
        }

        // 特定のファイルパスのドキュメント内容を取得（AutoStructureServiceを使用）
        [HttpGet("content")]
        public async Task<IActionResult> GetFilePathContent([FromQuery] string filepath)
        {
            try
            {
                // 【セキュリティ強化】現在のユーザーの認証制御を追加
                var currentUser = User.Identity?.Name;
                if (string.IsNullOrEmpty(currentUser))
                {
                    return Unauthorized("ユーザーが認証されていません");
                }

                if (string.IsNullOrEmpty(filepath))
                {
                    return BadRequest(new { error = "ファイルパスが指定されていません" });
                }

                // 現在のユーザーがアクセス可能なworkIdを取得
                var allowedWorkIds = await GetAllAvailableWorkIds();
                if (!allowedWorkIds.Any())
                {
                    return Forbidden(new { error = "アクセス可能なworkIdがありません" });
                }
                
                // ファイルパスからインデックスを取得
                string content = "";
                string displayName = "";
                
                // AutoStructureServiceからデータを取得してキャッシュ
                if (!filepath.StartsWith("text_"))
                {
                    // 既存のファイルパスで探す
                                            content = await GetDocumentsByFilePathFromDataIngestionAsync(filepath);
                    displayName = GetDisplayNameFromPath(filepath);
                }
                else
                {
                    // ユーザーの最初のworkIdを使用（固定値ではなく）
                    var userWorkId = allowedWorkIds.First();
                    _logger.LogInformation($"ユーザー {currentUser} がworkId {userWorkId} でコンテンツにアクセス");
                    var structuredData = await GetStructuredDataWithCache(userWorkId);
                    
                    if (structuredData != null && structuredData.TextList != null && structuredData.TextList.Count > 0)
                    {
                        if (filepath.StartsWith("page_"))
                        {
                            
                            // page_X_Y形式のパスの場合、X=ページ番号、Y=オリジナルインデックス
                            string[] parts = filepath.Replace("page_", "").Split('_');
                            
                            if (parts.Length >= 2 && int.TryParse(parts[0], out int pageNum) && int.TryParse(parts[1], out int originalIndex))
                            {
                                
                                // 先頭3件のテキストを確認のためログ出力
                                for (int i = 0; i < Math.Min(3, structuredData.TextList.Count); i++)
                                {
                                    var checkText = structuredData.TextList[i].Text;
                                    var checkPageNum = ExtractPageNumber(checkText);
                                }
                                
                                // ヘルパー関数の定義（ローカル関数として）
                                int ExtractPageNumber(string text)
                                {
                                    var match = System.Text.RegularExpressions.Regex.Match(text, @"p\.(\d+)");
                                    if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                                    {
                                        return num;
                                    }
                                    return int.MaxValue;
                                }
                                
                                // 指定されたページ番号とオリジナルインデックスのテキストを探す
                                
                                var targetItem = structuredData.TextList
                                    .Select((item, index) => new 
                                    { 
                                        Text = item.Text, 
                                        PageNumber = ExtractPageNumber(item.Text), 
                                        OriginalIndex = index 
                                    })
                                    .FirstOrDefault(item => item.PageNumber == pageNum && item.OriginalIndex == originalIndex);
                                
                                if (targetItem != null)
                                {
                                    
                                    // テキストを取得して、ダブルクォーテーションを除去
                                    var originalText = targetItem.Text;
                                    content = originalText?.Trim('"') ?? "";
                                    
                                    
                                    if (pageNum == int.MaxValue)
                                    {
                                        displayName = $"テキスト #{originalIndex + 1}";
                                    }
                                    else
                                    {
                                        displayName = $"{pageNum + 1}枚目";
                                    }
                                    
                                }
                                else
                                {
                                    
                                    // 利用可能なページ番号のリストを出力して診断に役立てる
                                    var availablePageNumbers = structuredData.TextList
                                        .Select((item, index) => new { PageNumber = ExtractPageNumber(item.Text), OriginalIndex = index })
                                        .Take(10)
                                        .Select(item => $"{item.PageNumber}_{item.OriginalIndex}")
                                        .ToList();
                                    
                                }
                            }
                            // 古い形式（page_X）との互換性のため
                            else if (parts.Length >= 1 && int.TryParse(parts[0], out pageNum))
                            {
                                
                                // ヘルパー関数の定義（ローカル関数として）
                                int ExtractPageNumber(string text)
                                {
                                    var match = System.Text.RegularExpressions.Regex.Match(text, @"p\.(\d+)");
                                    if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                                    {
                                        return num;
                                    }
                                    return int.MaxValue;
                                }
                                
                                // 指定されたページ番号のテキストを全て探す
                                var pageTexts = structuredData.TextList
                                    .Select((item, index) => new 
                                    { 
                                        Text = item.Text, 
                                        PageNumber = ExtractPageNumber(item.Text), 
                                        OriginalIndex = index 
                                    })
                                    .Where(item => item.PageNumber == pageNum)
                                    .OrderBy(item => item.OriginalIndex)
                                    .ToList();
                                
                                if (pageTexts.Any())
                                {
                                    // 同じページ番号のテキストを全て取得（ダブルクォーテーションを除去）
                                    content = string.Join("\n\n", pageTexts.Select(item => item.Text?.Trim('"') ?? ""));
                                    
                                    // 最初のアイテムのインデックスを取得するか、デフォルト値を使用
                                    int firstItemIndex = pageTexts.FirstOrDefault()?.OriginalIndex ?? 0;
                                    
                                    if (pageNum == int.MaxValue)
                                    {
                                        displayName = $"テキスト #{firstItemIndex + 1}";
                                    }
                                    else
                                    {
                                        displayName = $"{pageNum + 1}枚目";
                                    }
                                    
                                }
                                else
                                {
                                }
                            }
                        }
                        else if (filepath.StartsWith("text_"))
                        {
                            // 互換性のためにtext_X形式のパスも引き続きサポート
                            if (int.TryParse(filepath.Replace("text_", ""), out int index) && 
                                index >= 0 && index < structuredData.TextList.Count)
                            {
                                content = structuredData.TextList[index].Text;
                                
                                // ページ番号があれば表示名に含める
                                var pageMatch = System.Text.RegularExpressions.Regex.Match(content, @"p\.(\d+)");
                                if (pageMatch.Success)
                                {
                                    if (int.TryParse(pageMatch.Groups[1].Value, out int pageNum))
                                    {
                                        displayName = $"{pageNum + 1}枚目";
                                    }
                                    else
                                    {
                                        displayName = $"{pageMatch.Groups[1].Value}枚目";
                                    }
                                }
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(content))
                {
                    return NotFound(new { error = "ファイルパスに関連するコンテンツが見つかりません" });
                }

                return Ok(new
                {
                    name = displayName,
                    filepath = filepath,
                    content = content
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "ファイルパスコンテンツの取得に失敗しました" });
            }
        }

        // 特定のファイルの内容を取得
        [HttpGet("files/{fileId}")]
        public async Task<IActionResult> GetFileContent(string fileId)
        {
            try
            {
                // 【セキュリティ強化】現在のユーザーの認証制御を追加
                var currentUser = User.Identity?.Name;
                if (string.IsNullOrEmpty(currentUser))
                {
                    return Unauthorized("ユーザーが認証されていません");
                }
                
                if (_fileStorageService == null)
                {
                    return NotFound(new { error = "ファイルストレージサービスが利用できません" });
                }
                
                _logger.LogInformation($"ユーザー {currentUser} がファイルID {fileId} にアクセス");
                
                var fileInfo = await _fileStorageService.GetFileInfoAsync(fileId);
                if (fileInfo == null)
                {
                    return NotFound(new { error = "ファイルが見つかりません" });
                }

                // ファイルの内容を取得
                using var fileStream = await _fileStorageService.GetFileContentAsync(fileId);
                if (fileStream == null)
                {
                    return NotFound(new { error = "ファイルが存在しません" });
                }

                // ストリームをテキストに変換
                using var reader = new StreamReader(fileStream);
                var content = await reader.ReadToEndAsync();

                return Ok(new
                {
                    id = fileId,
                    name = fileInfo.FileName,
                    content = content,
                    timestamp = fileInfo.UploadDate
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "ファイル内容の取得に失敗しました" });
            }
        }

        // PDFファイルをアップロードして処理する
        [HttpPost("process-pdf")]
        public async Task<IActionResult> ProcessPdf(IFormFile file, [FromForm] string login_user = null)
        {
            // 処理ID（プロセスID）を生成
            string processId = Guid.NewGuid().ToString();
            _processingLogs[processId] = new List<string>();
            
            AddProcessingLog(processId, "アップロードを受け付けました");

            try
            {
                if (file == null || file.Length == 0)
                {
                    AddProcessingLog(processId, "ファイルが指定されていないか、空ファイルです", isError: true);
                    return BadRequest(new { error = "ファイルが指定されていないか、空ファイルです", processId = processId });
                }

                if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { error = "PDFファイルのみをアップロードしてください" });
                }


                // 注意: ファイルの保存や処理は行わない
                // 代わりに、ワークIDを生成して直接返す
                string workId = Guid.NewGuid().ToString("N");
                
                AddProcessingLog(processId, $"ファイル処理方法を変更: 直接workIdを返します ({workId})");
                
                // 【重要】アップロード時点でユーザー-workId紐付けを実行
                await AddWorkIdToHistory(workId, file.FileName);
                
                // 【修正】認証システムにworkIdアクセス権限を登録
                // FormDataから実際のログインユーザーを取得
                var actualUser = login_user;
                
                // フォールバック処理: login_userが取得できない場合
                if (string.IsNullOrEmpty(actualUser))
                {
                    // Cookie認証から取得を試行
                    actualUser = User?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
                    
                    // それでも取得できない場合はUser.Identity?.Nameを使用（非推奨だが最後の手段）
                    if (string.IsNullOrEmpty(actualUser))
                    {
                        actualUser = User.Identity?.Name;
                    }
                }
                
                var apiUser = User.Identity?.Name; // デバッグ用（参考値）
                _logger.LogInformation("ファイルアップロード認証情報: FormDataから取得したログインユーザー={ActualUser}, 参考値={ApiUser}", actualUser, apiUser);
                
                if (!string.IsNullOrEmpty(actualUser))
                {
                    var workIdRegistered = await _authorizationService.AddWorkIdToUserAsync(
                        actualUser, 
                        workId, 
                        file.FileName, 
                        $"ユーザー {actualUser} がアップロードしたPDFファイル: {file.FileName}"
                    );
                    
                    if (workIdRegistered)
                    {
                        _logger.LogInformation("ユーザーworkId登録成功: ユーザー={Username}, workId={WorkId}", actualUser, workId);
                    }
                    else
                    {
                        _logger.LogWarning("ユーザーworkId登録失敗: ユーザー={Username}, workId={WorkId}", actualUser, workId);
                    }
                }
                
                // 成功レスポンスを返す
                return Ok(new
                {
                    success = true,
                    message = "PDFファイルを受信しました",
                    details = new
                    {
                        fileId = workId,
                        totalPages = 0,
                        processedPages = 0,
                        errorPages = 0,
                        processId = processId,
                        work_id = workId  // クライアント側のJavaScriptとの互換性のために追加
                    }
                });
            }
            catch (Exception ex)
            {
                
                AddProcessingLog(processId, $"PDF処理中にエラーが発生しました: {ex.Message}", isError: true);
                
                // 詳細なエラー情報を生成
                var errorDetails = new StringBuilder();
                errorDetails.AppendLine("=== 例外詳細情報 ===");
                errorDetails.AppendLine($"例外の種類: {ex.GetType().FullName}");
                errorDetails.AppendLine($"メッセージ: {ex.Message}");
                errorDetails.AppendLine($"ソース: {ex.Source}");
                errorDetails.AppendLine($"ターゲットサイト: {ex.TargetSite}");
                errorDetails.AppendLine($"HResult: {ex.HResult}");
                errorDetails.AppendLine();
                
                // 内部例外があれば追加
                if (ex.InnerException != null)
                {
                    errorDetails.AppendLine("=== 内部例外 (レベル 1) ===");
                    errorDetails.AppendLine($"種類: {ex.InnerException.GetType().FullName}");
                    errorDetails.AppendLine($"メッセージ: {ex.InnerException.Message}");
                    errorDetails.AppendLine($"スタックトレース: {ex.InnerException.StackTrace}");
                }
                
                
                return StatusCode(500, new { 
                    error = "PDFの処理中にエラーが発生しました", 
                    message = ex.Message,
                    processId = processId
                });
            }
        }

        // デバッグログを取得する
        [HttpGet("debug-logs")]
        public IActionResult GetDebugLogs()
        {
            try
            {
                // 【セキュリティ強化】管理者権限チェック
                var currentUser = User.Identity?.Name;
                if (string.IsNullOrEmpty(currentUser))
                {
                    return Unauthorized("ユーザーが認証されていません");
                }
                
                var currentRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "User";
                if (currentRole != "Admin")
                {
                    return Forbidden(new { error = "管理者権限が必要です" });
                }
                
                _logger.LogInformation($"管理者 {currentUser} がデバッグログを取得");
                
                // デバッグログの検索ディレクトリ
                string storageDir = Path.Combine(Directory.GetCurrentDirectory(), "storage", "tmp");
                
                // 利用可能なデバッグログファイルを検索（複数ディレクトリを対象）
                StringBuilder logContent = new StringBuilder();
                logContent.AppendLine($"=== デバッグログ一覧 - {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")} ===\n");
                
                // すべてのデバッグログファイルを検索（サブディレクトリを含む）
                var debugLogFiles = Directory.GetFiles(storageDir, "debug_*.json", SearchOption.AllDirectories)
                    .OrderByDescending(f => new IOFileInfo(f).LastWriteTime)
                    .Take(20) // 直近20件に制限
                    .ToList();
                
                if (debugLogFiles.Count == 0)
                {
                    logContent.AppendLine("デバッグログファイルが見つかりません。");
                }
                else
                {
                    logContent.AppendLine($"{debugLogFiles.Count}件のデバッグログファイルが見つかりました:\n");
                    
                    foreach (var logFile in debugLogFiles)
                    {
                        var fileInfoObj = new IOFileInfo(logFile);
                        logContent.AppendLine($"- {Path.GetFileName(logFile)} ({fileInfoObj.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")})");
                        
                        // ファイルの内容を追加
                        try
                        {
                            string logFileContent = System.IO.File.ReadAllText(logFile);
                            
                            // JSONの整形（可能であれば）
                            try
                            {
                                var jsonDoc = JsonDocument.Parse(logFileContent);
                                using var ms = new MemoryStream();
                                using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true });
                                jsonDoc.WriteTo(writer);
                                writer.Flush();
                                logFileContent = Encoding.UTF8.GetString(ms.ToArray());
                            }
                            catch
                            {
                                // JSONとして解析できない場合は元のテキストをそのまま使用
                            }
                            
                            logContent.AppendLine("\nファイル内容:\n");
                            logContent.AppendLine("```json");
                            logContent.AppendLine(logFileContent);
                            logContent.AppendLine("```\n");
                            logContent.AppendLine(new string('-', 80) + "\n");
                        }
                        catch (Exception ex)
                        {
                            logContent.AppendLine($"ファイル読み取りエラー: {ex.Message}\n");
                        }
                    }
                }
                
                return Content(logContent.ToString(), "text/plain; charset=utf-8");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"デバッグログの取得中にエラーが発生しました: {ex.Message}" });
            }
        }
        
        // ログを取得する
        [HttpGet("logs")]
        public IActionResult GetLogs()
        {
            try
            {
                // 【セキュリティ強化】管理者権限チェック
                var currentUser = User.Identity?.Name;
                if (string.IsNullOrEmpty(currentUser))
                {
                    return Unauthorized("ユーザーが認証されていません");
                }
                
                var currentRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "User";
                if (currentRole != "Admin")
                {
                    return Forbidden(new { error = "管理者権限が必要です" });
                }
                
                _logger.LogInformation($"管理者 {currentUser} がアプリケーションログを取得");
                
                // アプリケーションログを文字列として構築
                StringBuilder logContent = new StringBuilder();
                logContent.AppendLine($"=== アプリケーションログ - {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")} ===");

                // Pythonスクリプトのパスを取得
                string pythonScriptsDir = Path.Combine(Directory.GetCurrentDirectory());

                // 関連するPythonスクリプトのファイル名とパスを列挙
                logContent.AppendLine("\n=== 関連ファイルの一覧 ===");
                string[] relevantFiles = {
                    "pdf_to_images.py",
                    "azure_image_to_text.py"
                };

                foreach (var scriptFileName in relevantFiles)
                {
                    string fullPath = Path.Combine(pythonScriptsDir, scriptFileName);
                    logContent.AppendLine($"{scriptFileName}: {(System.IO.File.Exists(fullPath) ? "存在します" : "見つかりません")}");
                }

                // アプリケーションのディレクトリ構造を詳細に確認
                // 1. ルートディレクトリとその中身
                string rootDir = Directory.GetCurrentDirectory();
                logContent.AppendLine("\n=== アプリケーションディレクトリ構造 ===");
                logContent.AppendLine($"ルートディレクトリ: {rootDir}");
                
                // ルートディレクトリの主要フォルダを確認
                string[] expectedRootFolders = {
                    "Controllers", "Models", "Pages", "Services", "Utils", 
                    "wwwroot", "attached_assets", "storage", "bin", "obj"
                };
                logContent.AppendLine("\n--- ルートディレクトリの主要フォルダ ---");
                foreach (var folder in expectedRootFolders)
                {
                    string path = Path.Combine(rootDir, folder);
                    logContent.AppendLine($"{folder}: {(Directory.Exists(path) ? "存在します" : "存在しません")}");
                }
                
                // 2. ストレージディレクトリの構造を確認
                string storageDir = Path.Combine(rootDir, "storage");
                string tmpDir = Path.Combine(storageDir, "tmp");
                string indexesDir = Path.Combine(rootDir, "indexes");

                logContent.AppendLine("\n=== ストレージディレクトリ情報 ===");
                logContent.AppendLine($"ストレージパス: {storageDir}");
                logContent.AppendLine($"存在: {Directory.Exists(storageDir)}");

                if (Directory.Exists(storageDir))
                {
                    // ストレージディレクトリの中身を確認
                    try 
                    {
                        string[] storageFolders = Directory.GetDirectories(storageDir);
                        logContent.AppendLine($"ストレージサブディレクトリ数: {storageFolders.Length}");
                        foreach (var dir in storageFolders)
                        {
                            logContent.AppendLine($"- {Path.GetFileName(dir)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        logContent.AppendLine($"ストレージディレクトリ一覧取得エラー: {ex.Message}");
                    }
                    
                    // 一時ディレクトリの情報
                    logContent.AppendLine($"\n一時ディレクトリパス: {tmpDir}");
                    logContent.AppendLine($"存在: {Directory.Exists(tmpDir)}");

                    if (Directory.Exists(tmpDir))
                    {
                        // tmp内のPDFディレクトリを一覧
                        var pdfDirs = Directory.GetDirectories(tmpDir, "pdf_*");
                        logContent.AppendLine($"\n=== PDF処理ディレクトリ ({pdfDirs.Length}) ===");

                        foreach (var pdfDir in pdfDirs.Take(5)) // 最大5つまで表示
                        {
                            logContent.AppendLine($"- {Path.GetFileName(pdfDir)}");

                            // 各ディレクトリ内のファイル数を表示
                            try
                            {
                                var files = Directory.GetFiles(pdfDir);
                                logContent.AppendLine($"  ファイル数: {files.Length}");

                                // 先頭5ファイルだけ表示
                                foreach (var f in files.Take(5))
                                {
                                    logContent.AppendLine($"  - {Path.GetFileName(f)}");
                                }
                            }
                            catch (Exception ex)
                            {
                                logContent.AppendLine($"  エラー: {ex.Message}");
                            }
                        }
                    }
                }
                
                // 3. インデックスディレクトリの確認
                logContent.AppendLine("\n=== インデックスディレクトリ情報 ===");
                logContent.AppendLine($"インデックスパス: {indexesDir}");
                logContent.AppendLine($"存在: {Directory.Exists(indexesDir)}");
                
                if (Directory.Exists(indexesDir))
                {
                    try
                    {
                        string[] indexFiles = Directory.GetFiles(indexesDir);
                        logContent.AppendLine($"インデックスファイル数: {indexFiles.Length}");
                        foreach (var file in indexFiles.Take(5))
                        {
                            logContent.AppendLine($"- {Path.GetFileName(file)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        logContent.AppendLine($"インデックスファイル一覧取得エラー: {ex.Message}");
                    }
                }
                
                // 4. attached_assetsディレクトリの確認
                string assetsDir = Path.Combine(rootDir, "attached_assets");
                logContent.AppendLine("\n=== 添付アセットディレクトリ情報 ===");
                logContent.AppendLine($"添付アセットパス: {assetsDir}");
                logContent.AppendLine($"存在: {Directory.Exists(assetsDir)}");
                
                if (Directory.Exists(assetsDir))
                {
                    try
                    {
                        string[] assetPyFiles = Directory.GetFiles(assetsDir, "*.py");
                        logContent.AppendLine($"Pythonスクリプト数: {assetPyFiles.Length}");
                        foreach (var file in assetPyFiles)
                        {
                            logContent.AppendLine($"- {Path.GetFileName(file)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        logContent.AppendLine($"添付アセットファイル一覧取得エラー: {ex.Message}");
                    }
                }

                // 環境情報の追加
                logContent.AppendLine("\n=== 環境情報 ===");
                logContent.AppendLine($"OS: {Environment.OSVersion}");
                logContent.AppendLine($"プロセス作業ディレクトリ: {Directory.GetCurrentDirectory()}");
                logContent.AppendLine($"ホームディレクトリ: {Environment.GetEnvironmentVariable("HOME")}");

                // Python環境情報の取得
                logContent.AppendLine("\n=== Python環境情報 ===");
                try
                {
                    // Python情報取得スクリプトを実行
                    string pythonInfoScript = Path.Combine(Directory.GetCurrentDirectory(), "get_python_info.py");
                    if (System.IO.File.Exists(pythonInfoScript))
                    {
                        using var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "python3",
                                Arguments = pythonInfoScript,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true,
                            }
                        };
                        
                        process.Start();
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        process.WaitForExit();
                        
                        if (string.IsNullOrEmpty(error) && !string.IsNullOrEmpty(output))
                        {
                            // JSONをフォーマット
                            try
                            {
                                var jsonDoc = JsonDocument.Parse(output);
                                using var ms = new MemoryStream();
                                using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true });
                                jsonDoc.WriteTo(writer);
                                writer.Flush();
                                output = Encoding.UTF8.GetString(ms.ToArray());
                            }
                            catch
                            {
                                // JSONとして解析できない場合は元のテキストをそのまま使用
                            }
                            
                            logContent.AppendLine("```json");
                            logContent.AppendLine(output);
                            logContent.AppendLine("```");
                        }
                        else
                        {
                            logContent.AppendLine($"Python情報取得エラー: {error}");
                        }
                    }
                    else
                    {
                        logContent.AppendLine("Python情報取得スクリプトが見つかりません: " + pythonInfoScript);
                    }
                }
                catch (Exception ex)
                {
                    logContent.AppendLine($"Python環境情報取得エラー: {ex.Message}");
                }

                return Content(logContent.ToString(), "text/plain; charset=utf-8");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"ログの取得中にエラーが発生しました: {ex.Message}" });
            }
        }

        // 処理ログを取得するエンドポイント
        [HttpGet("process-logs/{processId}")]
        public IActionResult GetProcessLogs(string processId)
        {
            try
            {
                // 【セキュリティ強化】現在のユーザーの認証制御を追加
                var currentUser = User.Identity?.Name;
                if (string.IsNullOrEmpty(currentUser))
                {
                    return Unauthorized("ユーザーが認証されていません");
                }
                
                if (string.IsNullOrEmpty(processId))
                {
                    return BadRequest(new { error = "処理IDが指定されていません" });
                }
                
                _logger.LogInformation($"ユーザー {currentUser} がプロセスID {processId} のログを取得");
                
                // 処理ログを検索
                if (_processingLogs.TryGetValue(processId, out var logs))
                {
                    return Ok(new
                    {
                        processId = processId,
                        logs = logs.ToArray()
                    });
                }
                else
                {
                    return NotFound(new { error = "指定された処理IDのログが見つかりません" });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"処理ログの取得中にエラーが発生しました: {ex.Message}" });
            }
        }
        
        // チャットメッセージに応答する
        // 複数ドキュメントをZIPでダウンロード
        [HttpPost("batch-download")]
        public async Task<IActionResult> BatchDownload([FromBody] BatchDownloadRequest request)
        {
            try
            {
                // 【セキュリティ強化】現在のユーザーの認証制御を追加
                var currentUser = User.Identity?.Name;
                if (string.IsNullOrEmpty(currentUser))
                {
                    return Unauthorized("ユーザーが認証されていません");
                }
                
                if (request.FilePaths == null || !request.FilePaths.Any())
                {
                    return BadRequest(new { error = "ダウンロードするファイルが指定されていません" });
                }
                
                // 現在のユーザーがアクセス可能なworkIdを取得
                var allowedWorkIds = await GetAllAvailableWorkIds();
                if (!allowedWorkIds.Any())
                {
                    return Forbidden(new { error = "アクセス可能なworkIdがありません" });
                }
                
                _logger.LogInformation($"ユーザー {currentUser} が {request.FilePaths.Count} ファイルのバッチダウンロードを実行");
                
                // 一時的なZIPファイル用のフォルダ
                string tempDir = Path.Combine(Directory.GetCurrentDirectory(), "storage", "tmp");
                if (!Directory.Exists(tempDir))
                {
                    Directory.CreateDirectory(tempDir);
                }
                
                // 日本時間で年月日時分を取得
                DateTime japanTime;
                try 
                {
                    // WindowsとLinuxでタイムゾーンIDが異なるため、まずWindows形式を試す
                    var japanTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
                    japanTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, japanTimeZone);
                }
                catch 
                {
                    try 
                    {
                        // Linux形式を試す
                        var japanTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
                        japanTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, japanTimeZone);
                    }
                    catch 
                    {
                        // どちらも失敗した場合は、UTCに9時間を足して日本時間にする
                        japanTime = DateTime.UtcNow.AddHours(9);
                    }
                }
                
                // 一時的なZIPファイルのパス（日本時間形式）
                string zipFileName = $"documents_{japanTime:yyyyMMdd_HHmm}.zip";
                string zipFilePath = Path.Combine(tempDir, zipFileName);
                
                // 一時的なフォルダを作成（ZIP作成用）
                string batchDir = Path.Combine(tempDir, $"batch_{japanTime:yyyyMMdd_HHmm}");
                if (!Directory.Exists(batchDir))
                {
                    Directory.CreateDirectory(batchDir);
                }
                
                // 成功したファイル数とエラーのあったファイル数をカウント
                int successCount = 0;
                int errorCount = 0;
                
                // リクエストされた各ファイルパスからコンテンツを取得して一時ファイルに保存
                foreach (var filepath in request.FilePaths)
                {
                    try
                    {
                        // ファイル内容を取得
                        string content = await GetDocumentsByFilePathFromDataIngestionAsync(filepath);
                        
                        if (string.IsNullOrEmpty(content))
                        {
                            errorCount++;
                            continue;
                        }
                        
                        // 左パネルの表示名をファイル名として使用するが、ファイル名に使用できない文字は置換
                        // 表示名を取得
                        string displayName = GetDisplayNameFromPath(filepath);
                        
                        // ファイル名に使用できない文字を置換 (半角スペースはアンダースコアに)
                        string safeFileName = displayName;
                        foreach (char c in Path.GetInvalidFileNameChars())
                        {
                            safeFileName = safeFileName.Replace(c, '_');
                        }
                        
                        // 特殊な記号も置換
                        safeFileName = safeFileName.Replace('【', '(').Replace('】', ')');
                        safeFileName = safeFileName.Replace('（', '(').Replace('）', ')');
                        safeFileName = safeFileName.Replace(' ', '_');
                        
                        // 表示名ベースのファイル名を生成
                        // 不正な文字を取り除いた表示名をそのまま使用
                        // 拡張子を追加（テキストファイルとして保存）
                        if (!safeFileName.EndsWith(".txt"))
                        {
                            safeFileName += ".txt";
                        }
                        
                        // 一時ファイルに保存
                        string tempFilePath = Path.Combine(batchDir, safeFileName);
                        await System.IO.File.WriteAllTextAsync(tempFilePath, content);
                        
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                    }
                }
                
                // 一時ファイルが存在しない場合はエラーを返す
                if (successCount == 0)
                {
                    return NotFound(new { error = "一括ダウンロード用の有効なファイルがありません" });
                }
                
                // ZIPファイルを作成
                if (System.IO.File.Exists(zipFilePath))
                {
                    System.IO.File.Delete(zipFilePath);
                }
                
                // ファイル名をマッピングするテーブルを作成
                Dictionary<string, string> fileToDisplayMapping = new Dictionary<string, string>();
                
                // ファイル番号カウンター
                int fileCounter = 1;
                
                foreach (var filepath in request.FilePaths)
                {
                    // オリジナルの表示名を取得（ロギング用）
                    string displayName = GetDisplayNameFromPath(filepath);
                    
                    // 表示名をファイル名として使用
                    string safeName;

                    // 表示名から拡張子付きの安全なファイル名を生成
                    string safeDisplayName = displayName;
                    
                    // ファイル名に使用できない文字を置換
                    foreach (char c in Path.GetInvalidFileNameChars())
                    {
                        safeDisplayName = safeDisplayName.Replace(c, '_');
                    }
                    
                    // 特殊な記号も置換
                    safeDisplayName = safeDisplayName.Replace('【', '(').Replace('】', ')');
                    safeDisplayName = safeDisplayName.Replace('（', '(').Replace('）', ')');
                    safeDisplayName = safeDisplayName.Replace(' ', '_');
                    
                    // 拡張子を追加
                    if (!safeDisplayName.EndsWith(".txt"))
                    {
                        safeDisplayName += ".txt";
                    }
                    
                    safeName = safeDisplayName;
                    
                    fileToDisplayMapping[filepath] = safeName;
                    
                    // ログに出力して確認
                }
                
                // ZIPファイル作成の準備（Shift-JISエンコーディングを使用）
                
                try 
                {
                    // DotNetZipライブラリを使用してShift-JISエンコーディングでZIPファイルを作成
                    using (var zipFile = new Ionic.Zip.ZipFile())
                    {
                        // Shift-JISエンコーディングを強制的に使用
                        zipFile.AlternateEncodingUsage = Ionic.Zip.ZipOption.Always;
                        // 日本語Windowsで標準的なShift-JISを使用
                        zipFile.AlternateEncoding = Encoding.GetEncoding(932); // Shift-JIS
                        
                        // ディレクトリ内の各ファイルをZIPに追加
                        foreach (string filePath in Directory.GetFiles(batchDir))
                        {
                            // ファイル名のみを取得
                            string fileName = Path.GetFileName(filePath);
                            
                            // 対応するファイルパスを検索
                            string originalFilePath = null;
                            foreach (var mapping in fileToDisplayMapping)
                            {
                                if (Path.GetFileName(filePath).Contains(mapping.Value.Replace(".txt", "")))
                                {
                                    originalFilePath = mapping.Key;
                                    break;
                                }
                            }
                            
                            // 元のファイル名をそのまま使用（日本語ファイル名対応）
                            string zipEntryName;
                            if (originalFilePath != null && fileToDisplayMapping.ContainsKey(originalFilePath))
                            {
                                // 元の表示名をそのまま使用
                                zipEntryName = fileToDisplayMapping[originalFilePath];
                            }
                            else
                            {
                                zipEntryName = Path.GetFileName(filePath);
                            }
                            
                            // ファイル名をログに出力（デバッグ用）
                            
                            // Shift-JISでエンコードされた名前でファイルを追加
                            zipFile.AddFile(filePath, "").FileName = zipEntryName;
                        }
                        
                        // ZIPファイルを保存
                        zipFile.Save(zipFilePath);
                    }
                }
                catch (Exception ex)
                {
                    
                    // エラー発生時は代替手段として標準的なZIP作成方法を使用
                    using (var fileStream = new FileStream(zipFilePath, FileMode.Create))
                    using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, false))
                    {
                        foreach (string filePath in Directory.GetFiles(batchDir))
                        {
                            string fileName = Path.GetFileName(filePath);
                            
                            var entry = archive.CreateEntry(fileName);
                            using (var entryStream = entry.Open())
                            using (var fileToCompressStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                            {
                                fileToCompressStream.CopyTo(entryStream);
                            }
                        }
                    }
                }
                
                
                // 処理完了ログ
                
                // 一時フォルダを削除（クリーンアップ）
                try
                {
                    Directory.Delete(batchDir, true);
                }
                catch (Exception ex)
                {
                }
                
                // ファイルをストリームで返す
                byte[] fileBytes = System.IO.File.ReadAllBytes(zipFilePath);
                return File(fileBytes, "application/zip", zipFileName);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "一括ダウンロード処理中にエラーが発生しました" });
            }
        }

        [HttpPost("chat")]
        public async Task<IActionResult> GenerateAnswer([FromBody] DataStructuringChatRequest request)
        {
            try
            {
                // 🔐 ASP.NET認証チェック（統一認証）
                if (!User?.Identity?.IsAuthenticated ?? true)
                {
                    return Unauthorized(new { error = "認証が必要です" });
                }

                var currentUsername = User.Identity.Name;
                var currentRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "User";

                _logger.LogInformation("チャット認証（ASP.NET統一）: ユーザー={Username}, ロール={Role}", currentUsername, currentRole);

                // ユーザーがアクセス可能なworkIdを取得（ASP.NET認証ユーザーで）
                var allowedWorkIds = await _authorizationService.GetAllowedWorkIdsAsync(currentUsername);

                if (string.IsNullOrEmpty(request.Message))
                {
                    return BadRequest(new { error = "メッセージが指定されていません" });
                }
                
                // FileIdが指定されていない場合はデフォルト値を設定
                if (string.IsNullOrEmpty(request.FileId))
                {
                    request.FileId = "default";
                }

                // リクエストボディの完全なダンプを取得
                string rawRequestBody = string.Empty;
                Request.EnableBuffering();
                using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8, true, 1024, true))
                {
                    rawRequestBody = await reader.ReadToEndAsync();
                    Request.Body.Position = 0;
                }
                
                // JSONプロパティの解析結果を詳細に出力
                bool rawUseChunksValue = false;
                try {
                    using (JsonDocument doc = JsonDocument.Parse(rawRequestBody))
                    {
                        if (doc.RootElement.TryGetProperty("use_chunks", out JsonElement useChunksElement))
                        {
                            rawUseChunksValue = useChunksElement.GetBoolean();
                        }
                        
                        // chunksプロパティの確認
                        if (doc.RootElement.TryGetProperty("chunks", out JsonElement chunksElement))
                        {
                            if (chunksElement.ValueKind == JsonValueKind.Array)
                            {
                                int chunksCount = chunksElement.GetArrayLength();
                                
                                // 最初の数件のチャンクをログ出力
                                for (int i = 0; i < Math.Min(2, chunksCount); i++)
                                {
                                    var chunkElement = chunksElement[i];
                                    string chunkText = "不明";
                                    int pageNo = -1;
                                    int chunkNo = -1;
                                    
                                    if (chunkElement.TryGetProperty("Chunk", out JsonElement chunkTextElement))
                                    {
                                        chunkText = chunkTextElement.GetString() ?? "null";
                                    }
                                    if (chunkElement.TryGetProperty("PageNo", out JsonElement pageNoElement))
                                    {
                                        pageNo = pageNoElement.GetInt32();
                                    }
                                    if (chunkElement.TryGetProperty("ChunkNo", out JsonElement chunkNoElement))
                                    {
                                        chunkNo = chunkNoElement.GetInt32();
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                }
                
                // 重要: チャンク検索を常に有効に保つ
                if (!request.UseChunks)
                {
                    request.UseChunks = true;
                }
                
                // メッセージ内容のデバッグ情報を詳細に出力
                
                // メッセージの長さに応じて、部分的に出力
                if (request.Message.Length > 500)
                {
                    // 長いメッセージは先頭と末尾を分けて出力
                }
                else
                {
                    // 短いメッセージはそのまま出力
                }
                
                // 重要: デバッグのためリクエスト内容をダンプ

                // WorkIdのログ出力を追加

                
                // 統合シノニムデータの確認
                if (!string.IsNullOrEmpty(request.Synonyms))
                {
                    var synonymLines = request.Synonyms.Split('\n').Where(l => l.Trim().Length > 0).ToList();
                    
                    // 冒頭10件のみログ出力
                    var topSynonyms = synonymLines.Take(10).ToList();
                    foreach (var line in topSynonyms)
                    {
                    }
                    if (synonymLines.Count > 10)
                    {
                    }
                }
                else
                {
                }
                
                // APIシノニムリストの確認と有効性検証
                if (request.SynonymList != null && request.SynonymList.Count > 0)
                {
                    // 有効な同義語項目のみをフィルタリング
                    var validSynonymList = request.SynonymList.Where(item => 
                        !string.IsNullOrWhiteSpace(item?.Keyword) && 
                        item.Synonyms != null && 
                        item.Synonyms.Count > 0 &&
                        item.Synonyms.Any(synonym => !string.IsNullOrWhiteSpace(synonym))
                    ).ToList();
                    
                    _logger.LogInformation($"シノニムリストフィルタリング: 元の件数={request.SynonymList.Count}, 有効な件数={validSynonymList.Count}");
                    
                    // 有効な項目のみをリクエストに設定
                    request.SynonymList = validSynonymList;
                    
                    for (int i = 0; i < Math.Min(3, validSynonymList.Count); i++)
                    {
                        var item = validSynonymList[i];
                        _logger.LogInformation($"有効シノニム項目[{i}]: Keyword={item.Keyword}, Synonyms={string.Join(", ", item.Synonyms?.Take(3) ?? new List<string>())}");
                    }
                }
                else
                {
                    _logger.LogInformation("シノニムリストが空またはnullです");
                }

                // プロンプトテンプレートと実際の質問を分離する処理（フロントエンドからの入力形式）
                string customSystemPrompt = null;
                string actualQuestion = request.Message;
                
                
                // 【デバッグ追加】完全なリクエストメッセージをログに出力
                
                // MessageにプロンプトテンプレートがCombineされている場合（\n\nで区切られている場合）
                // 改行コードをより厳密に検索（\r\n、\n\nの両方に対応）
                var parts = request.Message.Split(new[] { "\r\n\r\n", "\n\n" }, 2, StringSplitOptions.None);
                
                if (parts.Length == 2)
                {
                    customSystemPrompt = parts[0]; // 前半部分がシステムプロンプト
                    actualQuestion = parts[1];     // 後半部分が実際の質問
                    
                    
                    // プロンプトの内容を詳細にデバッグ（箇条書き指示が含まれているか）
                    if (!string.IsNullOrEmpty(customSystemPrompt))
                    {
                        bool hasBulletPointInstruction = customSystemPrompt.Contains("箇条書き");
                        bool hasStarBulletPoint = customSystemPrompt.Contains("★") && hasBulletPointInstruction;
                        bool hasNumberedBulletPoint = customSystemPrompt.Contains("数字") && hasBulletPointInstruction;
                        
                        
                        // 重要: カスタムシステムプロンプト全文をログに出力
                        
                        if (hasStarBulletPoint)
                        {
                        }
                        if (hasNumberedBulletPoint)
                        {
                        }
                    }
                }
                else
                {
                }
                
                // 【緊急追加】デバッグ用ログの強化 - プロンプト内容を再度分析
                if (!string.IsNullOrEmpty(customSystemPrompt))
                {
                    
                    // 全テキストエンコーディング情報
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(customSystemPrompt);
                    
                    if (customSystemPrompt.Contains("★"))
                    {
                        int starIndex = customSystemPrompt.IndexOf("★");
                        int contextStart = Math.Max(0, starIndex - 20);
                        int contextLength = Math.Min(50, customSystemPrompt.Length - contextStart);
                        
                        // ★付近のバイトをHEX表示
                        byte[] contextBytes = System.Text.Encoding.UTF8.GetBytes(customSystemPrompt.Substring(contextStart, contextLength));
                    }
                }

                // クライアントから送られたコンテキストを使用
                string documentContext = request.Context;
                
                // クライアントから送られたソース情報をログに出力
                if (request.Sources != null && request.Sources.Count > 0)
                {
                    foreach (var source in request.Sources.Take(3)) // 最初の3件だけログに出力
                    {
                    }
                }

                // 関連するコンテキストを検索（AutoStructureServiceから取得したチャンクから検索）
                List<Models.SearchResult> searchResults = new List<Models.SearchResult>();
                
                // 【TRACE】Log the chunks flag status

                // 重要: チャンク検索を常に有効にする（クライアント設定に関わらず）
                if (!request.UseChunks)
                {
                    request.UseChunks = true;
                }

                try
                {
                    // クライアントから送信されたチャンクデータを使用するか、APIから取得
                    List<ChunkItem> chunkList;
                    
                    if (request.Chunks != null && request.Chunks.Count > 0)
                    {
                        // クライアントから送信されたチャンクデータを使用
                        chunkList = request.Chunks;
                        
                        // チャンクデータの内容をより詳細にログ出力
                        
                        // チャンクデータの型情報を詳細にログ出力
                        if (chunkList.Count > 0)
                        {
                            var firstChunk = chunkList[0];
                            
                            // 最初のチャンクをJSON形式でダンプ
                            try
                            {
                                string chunkJson = JsonSerializer.Serialize(firstChunk, new JsonSerializerOptions { WriteIndented = true });
                            }
                            catch (Exception ex)
                            {
                            }
                        }
                        
                        // 最初の数件のチャンクサンプルをログに出力
                        for (int i = 0; i < Math.Min(3, chunkList.Count); i++)
                        {
                            var chunk = chunkList[i];
                        }
                        
                        // デバッグ用ログ（最初の3件のチャンクを表示）
                        for (int i = 0; i < Math.Min(3, chunkList.Count); i++)
                        {
                            var chunk = chunkList[i];
                        }
                        
                        // チャンク内のキーワードを含むか確認（デバッグ用）
                        bool containsKeyword = false;
                        string searchKeyword = actualQuestion; // ハードコードされた文字列ではなく実際のユーザー質問を使用
                        if (!string.IsNullOrEmpty(searchKeyword) && searchKeyword.Length > 5) // 検索キーワードが短すぎる場合は確認しない
                        {
                            foreach (var chunk in chunkList)
                            {
                                if (chunk.Chunk != null && chunk.Chunk.Contains(searchKeyword, StringComparison.OrdinalIgnoreCase))
                                {
                                    containsKeyword = true;
                                    break;
                                }
                            }
                            
                            if (!containsKeyword)
                            {
                                // 個別のキーワードでも検索
                                var splitKeywords = searchKeyword.Split(new[] { ' ', '　', '、', '。' }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var keyword in splitKeywords)
                                {
                                    if (keyword.Length < 2) continue; // 短すぎるキーワードはスキップ
                                    
                                    bool foundKeyword = false;
                                    foreach (var chunk in chunkList)
                                    {
                                        if (chunk.Chunk != null && chunk.Chunk.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                                        {
                                            foundKeyword = true;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // クライアントからのチャンクデータがない場合は、APIから取得
                        // 複数workIdに対応（search-all-documentsと同じパターン）
                        List<string> targetWorkIds;
                        if (string.IsNullOrEmpty(request.WorkId))
                        {
                            // workId指定なし → ユーザーがアクセス可能な全workId
                            targetWorkIds = allowedWorkIds;
                        }
                        else
                        {
                            // workId指定あり → アクセス権限チェック
                            if (!allowedWorkIds.Contains(request.WorkId))
                            {
                                return Forbidden(new { error = $"workId '{request.WorkId}' へのアクセス権限がありません" });
                            }
                            targetWorkIds = new List<string> { request.WorkId };
                        }

                        Console.WriteLine($"🔍 チャット対象workId一覧: [{string.Join(", ", targetWorkIds)}]");

                        // 🔍 スマート差分登録: 両インデックスをチェックして不足分のみ登録
                        var azureSearchService = HttpContext.RequestServices.GetRequiredService<IAzureSearchService>();
                        var dataIngestionService = HttpContext.RequestServices.GetRequiredService<IDataIngestionService>();
                        
                        // 各workIdの両インデックス存在状況をチェック
                        var workIdProcessingPlan = new List<(string workId, bool needsProcessing, string reason)>();
                        
                        foreach (var workId in targetWorkIds)
                        {
                            var (existsInMain, existsInSentence) = await azureSearchService.CheckWorkIdInBothIndexesAsync(workId);
                            
                            if (!existsInMain || !existsInSentence)
                            {
                                string reason = (!existsInMain && !existsInSentence) ? "両インデックスに未登録" :
                                               (!existsInMain) ? "oecインデックスのみ不足" :
                                               "oec-sentenceインデックスのみ不足";
                                workIdProcessingPlan.Add((workId, true, reason));
                            }
                            else
                            {
                                workIdProcessingPlan.Add((workId, false, "両インデックスに登録済み"));
                            }
                        }
                        
                        var workIdsNeedingProcessing = workIdProcessingPlan.Where(p => p.needsProcessing).Select(p => p.workId).ToList();
                        var workIdsSkipped = workIdProcessingPlan.Where(p => !p.needsProcessing).Select(p => p.workId).ToList();
                        
                        Console.WriteLine($"🧠 スマート差分登録計画:");
                        foreach (var plan in workIdProcessingPlan)
                        {
                            string status = plan.needsProcessing ? "🔄 処理対象" : "✅ スキップ";
                            Console.WriteLine($"   - {plan.workId}: {status} ({plan.reason})");
                        }
                        
                        // 全workIdからチャンクデータを取得（検索用）
                        var allChunksList = new List<ChunkItem>();
                        foreach (var workId in targetWorkIds)
                        {
                            try
                            {
                                var structuredData = await GetStructuredDataWithCache(workId);
                                if (structuredData?.ChunkList != null && structuredData.ChunkList.Count > 0)
                                {
                                    allChunksList.AddRange(structuredData.ChunkList);
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"❌ workId {workId}: チャンクデータ取得エラー - {ex.Message}");
                            }
                        }

                        chunkList = allChunksList;
                        Console.WriteLine($"🔍 統合チャンク総数: {chunkList.Count}件 (検索対象)");
                        Console.WriteLine($"🔍 インデックス登録対象: {workIdsNeedingProcessing.Count}件 (不足分のみ)");
                        
                        // 🧠 スマート登録: 不足分のみをDataIngestionServiceで処理
                        if (workIdsNeedingProcessing.Count > 0)
                        {
                            Console.WriteLine($"🧠 スマート登録開始: {workIdsNeedingProcessing.Count}件のworkId");
                            
                            int successCount = 0;
                            foreach (var workId in workIdsNeedingProcessing)
                            {
                                try
                                {
                                    // DataIngestionServiceのスマート登録機能を使用（ASP.NET認証ユーザーを使用）
                                    var result = await _dataIngestionService.ProcessWorkIdAsync(currentUsername, workId);
                                    
                                    if (result.success)
                                    {
                                        successCount++;
                                        Console.WriteLine($"✅ workId {workId}: スマート登録成功 ({result.processedChunks}件)");
                                    }
                                    else
                                    {
                                        Console.WriteLine($"❌ workId {workId}: スマート登録失敗 - {result.errorMessage}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"❌ workId {workId}: スマート登録エラー - {ex.Message}");
                                }
                            }
                            
                            Console.WriteLine($"🧠 スマート登録完了: {successCount}/{workIdsNeedingProcessing.Count}件成功");
                            
                            // インデックス登録後、少し待機してからAzure Search検索を実行
                            await Task.Delay(2000); // 2秒待機
                        }
                        else
                        {
                            Console.WriteLine($"✅ 全workIdが両インデックスに登録済み → インデックス登録をスキップ");
                        }
                    }
                    
                    if (chunkList.Count > 0)
                    {
                        
                        // キーワードの取得（リクエストに含まれている場合）
                        string queryForSearch = actualQuestion;
                        if (request.Keywords != null && request.Keywords.Count > 0)
                        {
                            // 後でキーワードを使った検索を実装する場合に使用
                        }
                        
                        // 🔒 セキュリティ: チャット認証で取得したallowedWorkIdsを使用（ASP.NET認証のworkIdは使用しない）
                        var searchTargetWorkIds = allowedWorkIds;
                        
                        // 共通のAzure Search ハイブリッドセマンティック検索を実行（チャット認証ユーザーのworkIdのみ対象）
                        Console.WriteLine($"🚀 チャット用Azure Search検索開始: クエリ={queryForSearch}");
                        Console.WriteLine($"🔍 検索対象workId（チャット認証ユーザーのみ）: [{string.Join(", ", searchTargetWorkIds)}]");
                        searchResults = await PerformAzureSearchAsync(queryForSearch, searchTargetWorkIds, 10);
                        Console.WriteLine($"✅ チャット用Azure Search検索完了: {searchResults.Count}件取得");
                        
                        
                        if (searchResults.Count > 0)
                        {
                            // 検索結果からコンテキストを構築
                            var contextBuilder = new StringBuilder();
                            
                            foreach (var result in searchResults)
                            {
                                contextBuilder.AppendLine($"--- {result.PageNumber + 1}枚目 (チャンク {result.ChunkNumber}) キーワード: {result.MatchedKeywords} ---");
                                contextBuilder.AppendLine(result.Content);
                                contextBuilder.AppendLine();
                            }
                            
                            documentContext = contextBuilder.ToString();
                            
                            
                            // 検索結果のID形式をログで確認
                            if (searchResults.Count > 0)
                            {
                                var firstResultId = searchResults[0].Id;
                            }
                        }
                        else
                        {
                        }
                    }
                    else
                    {
                    }
                }
                catch (Exception ex)
                {
                }

                
                List<SynonymItem> synonymList = null;
                List<SynonymMatch> usedSynonyms = new List<SynonymMatch>();
                string originalQuery = actualQuestion;
                List<string> keywords = new List<string>(); // スコープを上位に移動
                
                try
                {
                    
                    // 🎯 クライアントから送信されたキーワードを最優先使用（画面のクエリ変換タグと完全一致）
                    if (request.Keywords != null && request.Keywords.Count > 0)
                    {
                        keywords = request.Keywords;
                        
                        // クエリ変換タグの詳細情報をログ出力
                        for (int i = 0; i < keywords.Count; i++)
                        {
                        }
                    }
                    else
                    {
                        // ⚠️ クライアントからクエリ変換タグが送信されていない場合はシノニム処理をスキップ
                        keywords = new List<string>(); // 空のリストでシノニム処理をスキップ
                    }
                    
                    
                    // クエリ変換タグの詳細情報をログ出力
                    if (request.Keywords != null && request.Keywords.Count > 0)
                    {
                        for (int i = 0; i < keywords.Count; i++)
                        {
                        }
                    }
                    
                    // クライアントから送信されたシノニムリストを優先使用
                    if (request.SynonymList != null && request.SynonymList.Count > 0)
                    {
                        synonymList = request.SynonymList;
                        
                        // 詳細ログ
                        for (int i = 0; i < Math.Min(3, synonymList.Count); i++)
                        {
                            var item = synonymList[i];
                        }
                    }
                    else
                    {
                        // APIからシノニムリストを取得
                        string workId = !string.IsNullOrEmpty(request.WorkId)
                            ? request.WorkId
                            : (!string.IsNullOrEmpty(request.FileId) && request.FileId != "no-document"
                                ? request.FileId
                                : "ff3bfb43437a02fde082fdc2af4a90e8");
                        
                        var structuredData = await GetStructuredDataWithCache(workId);
                        
                        if (structuredData?.SynonymList != null && structuredData.SynonymList.Count > 0)
                        {
                            synonymList = structuredData.SynonymList;
                        }
                        else
                        {
                        }
                    }
                    
                    // シノニム検索実行
                    if (synonymList != null && synonymList.Count > 0 && keywords.Count > 0)
                    {
                        
                        for (int i = 0; i < Math.Min(10, synonymList.Count); i++)
                        {
                            var group = synonymList[i];
                            if (group.Synonyms != null && group.Synonyms.Count > 0)
                            {
                                
                                // Sansan関連のグループかチェック
                                bool hasSansan = group.Synonyms.Any(s => s.Contains("Sansan") || s.Contains("sansan") || s.Contains("SANSAN"));
                                bool hasBillOne = group.Synonyms.Any(s => s.Contains("Bill One") || s.Contains("ビルワン") || s.Contains("bill one"));
                                
                                if (hasSansan || hasBillOne)
                                {
                                }
                            }
                        }
                        
                        foreach (var keyword in keywords)
                        {
                        }
                        
                        usedSynonyms = this.FindSynonymsForKeywords(keywords, synonymList);
                        
                        
                        // マッチしたシノニムの詳細ログ
                        foreach (var match in usedSynonyms)
                        {
                        }
                        
                        // クエリ拡張実行
                        if (usedSynonyms.Count > 0)
                        {
                            
                            var expandedQuery = this.ExpandQueryWithSynonyms(actualQuestion, usedSynonyms);
                            actualQuestion = expandedQuery;
                            
                        }
                        else
                        {
                        }
                    }
                    else
                    {
                    }
                }
                catch (Exception ex)
                {
                    // エラーが発生してもチャット処理は継続
                }

                // 🆕 AzureSearchServiceとClaude APIを使用して回答を生成（DataIngestion対応）
                var (answer, sources) = await GenerateAnswerWithDataIngestionAsync(actualQuestion, documentContext, customSystemPrompt);

                // 回答の内容を一部ログに出力 (最初の100文字)
                if (!string.IsNullOrEmpty(answer) && answer.Length > 0)
                {
                    int logLength = Math.Min(answer.Length, 100);
                }
                else
                {
                }

                // 直前に本メソッド内で検索済みの結果がある場合はそれを優先（documentContextを生成した検索結果）
                var effectiveSources = (searchResults != null && searchResults.Any()) ? searchResults : sources;
                // 上位30件のみをフロントに返す（表示最適化）
                var topSources = effectiveSources?.Take(30).ToList() ?? new List<Models.SearchResult>();

                // ソース情報をレスポンス用に変換
                var sourcesResponse = topSources.Select((source, index) =>
                {
                    var filepath = source.Filepath;

                    // filepathを一意のIDとして使用（検索しやすくするため）
                    // 重要: JavaScriptのfetchDocumentContentで処理できるように、filepathそのものをIDとして使用
                    var fileId = filepath;

                    // GetDisplayNameFromPathを使用して、メタデータから元のファイル名（あれば）を取得
                    var displayName = GetDisplayNameFromPath(filepath);


                    return new
                    {
                        name = displayName,
                        id = fileId,
                        filepath = filepath,
                        fileType = "PDF", // ファイルタイプ情報を追加
                        // 追加フィールド（モーダル表示用）
                        score = source.Score,
                        pageNumber = source.PageNumber,
                        chunkNumber = source.ChunkNumber,
                        title = source.Title,
                        content = source.Content
                    };
                }).ToArray();

                // シノニム情報を準備（応答チャットでの表示用）
                var synonymInfo = usedSynonyms.Select(s => new
                {
                    original_keyword = s.OriginalKeyword,
                    matched_synonym = s.MatchedSynonym,
                    related_synonyms = s.RelatedSynonyms,
                    display_text = $"「{s.OriginalKeyword}」の関連語: {string.Join(", ", s.RelatedSynonyms)}"
                }).ToArray();

                // 返却前ログ（sources件数）
                _logger.LogInformation("🧾 CHAT返却: sources件数(上位30件に制限)={Count}", sourcesResponse.Length);

                // レスポンスを組み立て（常に同じ構造）
                var response = new
                {
                    content = answer,
                    sources = sourcesResponse,
                    keywords = keywords,
                    synonyms = synonymInfo,
                    debug_info = new { 
                        timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        synonyms_used = usedSynonyms.Count,
                        search_results_count = sourcesResponse.Length
                    }
                };

                if (usedSynonyms.Count > 0)
                {
                    foreach (var synonym in usedSynonyms)
                    {
                    }
                }

                return Ok(response);
            }
            catch (Exception ex)
            {
                
                // 例外の詳細情報をログに出力
                StringBuilder exceptionDetails = new StringBuilder();
                exceptionDetails.AppendLine($"=== チャット応答生成 例外詳細情報 ===");
                exceptionDetails.AppendLine($"例外の種類: {ex.GetType().FullName}");
                exceptionDetails.AppendLine($"メッセージ: {ex.Message}");
                exceptionDetails.AppendLine($"ソース: {ex.Source}");
                exceptionDetails.AppendLine($"ターゲットサイト: {ex.TargetSite}");
                
                // InnerException情報の出力
                Exception inner = ex.InnerException;
                int innerLevel = 1;
                while (inner != null)
                {
                    exceptionDetails.AppendLine($"\n=== 内部例外 (レベル {innerLevel}) ===");
                    exceptionDetails.AppendLine($"種類: {inner.GetType().FullName}");
                    exceptionDetails.AppendLine($"メッセージ: {inner.Message}");
                    exceptionDetails.AppendLine($"スタックトレース: {inner.StackTrace}");
                    inner = inner.InnerException;
                    innerLevel++;
                }
                
                return StatusCode(500, new { error = "チャット応答の生成に失敗しました" });
            }
        }

        // 全PDFファイルから検索を実行するエンドポイント
        [HttpGet("search")]
        public async Task<IActionResult> SearchAllPdfs([FromQuery] string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest(new { error = "検索クエリが指定されていません" });
            }

            try
            {

                // 検索結果を格納するリスト
                List<SearchResult> results = new List<SearchResult>();

                // 🆕 DataIngestionのインデックスから全てのworkIdを取得
                var filepaths = await GetUniqueFilePathsFromDataIngestionAsync();

                // PDFファイルのみをフィルタリング
                var pdfFilepaths = filepaths.Where(path => 
                    path.Contains("pdf_") || 
                    (path.Contains("-page-") && path.EndsWith(".txt"))
                ).ToList();


                // 各ファイルのコンテンツを取得して検索
                foreach (var filepath in pdfFilepaths)
                {
                    try
                    {
                        // ファイルの内容を取得
                        var fileStream = await _fileStorageService.GetFileContentAsync(filepath);
                        if (fileStream == null)
                        {
                            continue;
                        }

                        // ストリームをテキストに変換
                        string fileContent;
                        using (var reader = new StreamReader(fileStream))
                        {
                            fileContent = await reader.ReadToEndAsync();
                        }

                        if (string.IsNullOrEmpty(fileContent))
                        {
                            continue;
                        }

                        // ファイル名を表示形式に整形
                        var displayName = GetDisplayNameFromPath(filepath);

                        // PDFグループ名を抽出
                        string pdfName = displayName;
                        if (displayName.Contains("("))
                        {
                            pdfName = displayName.Split(" (")[0];
                        }

                        // 大文字小文字を区別せずに検索
                        int index = 0;
                        while ((index = fileContent.IndexOf(query, index, StringComparison.OrdinalIgnoreCase)) != -1)
                        {
                            // マッチしたテキストの前後の文脈を抽出（最大50文字）
                            int start = Math.Max(0, index - 50);
                            int end = Math.Min(fileContent.Length, index + query.Length + 50);
                            string snippet = fileContent.Substring(start, end - start);

                            // 検索結果を追加
                            results.Add(new SearchResult
                            {
                                PageId = filepath,
                                PdfName = pdfName,
                                PageName = displayName,
                                Position = index,
                                MatchLength = query.Length,
                                Snippet = snippet
                            });

                            // 次の検索位置を設定
                            index += query.Length;
                        }
                    }
                    catch (Exception ex)
                    {
                        // エラーがあっても処理を続行
                    }
                }

                // 検索結果をページ番号順にソート
                var sortedResults = results
                    .OrderBy(r => r.PdfName)
                    .ThenBy(r => ExtractPageNumber(r.PageName))
                    .ToList();


                return Ok(new { results = sortedResults });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "検索処理中にエラーが発生しました" });
            }
        }

        // ページ名からページ番号を抽出するヘルパーメソッド
        private int ExtractPageNumber(string pageName)
        {
            try
            {
                if (pageName.Contains("(") && pageName.Contains("枚目)"))
                {
                    var pageStr = pageName.Split("(")[1].Replace("枚目)", "").Trim();
                    if (int.TryParse(pageStr, out int pageNum))
                    {
                        return pageNum;
                    }
                }
            }
            catch
            {
                // 解析エラーの場合はデフォルト値を返す
            }
            return 999; // 解析できない場合は大きな値を返して最後に表示
        }

        // 検索結果モデル
        public class SearchResult
        {
            public string PageId { get; set; }
            public string PdfName { get; set; }
            public string PageName { get; set; }
            public int Position { get; set; }
            public int MatchLength { get; set; }
            public string Snippet { get; set; }
        }

        // 処理状況のみを取得するエンドポイント
        [HttpGet("status")]
        public async Task<IActionResult> GetStatus([FromQuery] string workId, [FromQuery] bool forceRefresh = false)
        {
            try
            {
                // 🔥 API呼び出し確認ログ（常に出力）
                _logger.LogInformation("🔥🔥🔥 GetStatusメソッドが呼び出されました！ 🔥🔥🔥");
                _logger.LogInformation($"📋 Parameters -> workId: {workId}, forceRefresh: {forceRefresh}");
                
                // 【セキュリティ強化】現在のユーザーの認証制御を追加
                var currentUser = User.Identity?.Name;
                if (string.IsNullOrEmpty(currentUser))
                {
                    _logger.LogError("❌ 認証エラー: ユーザーが認証されていません");
                    return Unauthorized("ユーザーが認証されていません");
                }
                
                if (string.IsNullOrEmpty(workId))
                {
                    return BadRequest(new { error = "workIdが必要です" });
                }
                
                // 現在のユーザーがアクセス可能なworkIdを取得
                var allowedWorkIds = await GetAllAvailableWorkIds();
                if (!allowedWorkIds.Contains(workId))
                {
                    _logger.LogWarning($"ユーザー {currentUser} が認可されていないworkId {workId} のステータスにアクセスを試行しました");
                    return Forbidden(new { error = "指定されたworkIdへのアクセス権限がありません", workId });
                }
                
                _logger.LogInformation($"ユーザー {currentUser} がworkId {workId} のステータスを取得 (forceRefresh={forceRefresh})");
                _logger.LogInformation($"【デバッグ】GetStatus処理開始: workId={workId}, currentUser={currentUser}");
                
                // 🔥🔥🔥 アップロード状況ボタンが押されました！ 🔥🔥🔥
                if (forceRefresh)
                {
                    _logger.LogInformation("🚀🚀🚀 【アップロード状況ボタン】が押されました！ 🚀🚀🚀");
                    _logger.LogInformation($"📊 対象workId: {workId}, ユーザー: {currentUser}");
                }
                
                // 強制更新が指定されている場合はキャッシュを無効化
                if (forceRefresh)
                {
                    InvalidateCache(workId);
                    _logger.LogInformation($"workId {workId} のキャッシュを無効化しました（更新ボタンによる強制更新）");
                }
                
                // キャッシュ付きでAutoStructureServiceを使ってステータスを取得
                _logger.LogInformation($"【デバッグ】GetStructuredDataWithCache呼び出し直前: workId={workId}");
                var result = await GetStructuredDataWithCache(workId);
                
                if (result == null)
                {
                    _logger.LogWarning($"workId {workId} のデータが見つかりませんでした");
                    return NotFound(new { error = "指定されたworkIdのデータが見つかりませんでした" });
                }
                
                // 処理状況を判定
                var processingState = result.GetProcessingState();
                
                // 更新ボタンによる強制更新の場合は、レスポンス詳細もログ出力
                if (forceRefresh)
                {
                    _logger.LogInformation($"=== 更新ボタンによるレスポンス詳細 (workId: {workId}) ===");
                    _logger.LogInformation($"処理状況: {processingState}");
                    _logger.LogInformation($"State: {result.State}");
                    _logger.LogInformation($"ページ進捗: {result.PageNo}/{result.MaxPageNo}");
                    _logger.LogInformation($"ReturnCode: {result.ReturnCode}");
                    _logger.LogInformation($"ErrorDetail: {result.ErrorDetail ?? "なし"}");
                    _logger.LogInformation($"データ件数 - Chunk:{result.ChunkList?.Count ?? 0}, Text:{result.TextList?.Count ?? 0}, Synonym:{result.SynonymList?.Count ?? 0}");
                    _logger.LogInformation($"=== 更新ボタンによるレスポンス詳細終了 (workId: {workId}) ===");
                }
                
                return Ok(new
                {
                    work_id = workId,
                    page_no = result.PageNo,
                    max_page_no = result.MaxPageNo,
                    processing_state = processingState.ToString(),
                    state = result.State,
                    return_code = result.ReturnCode,
                    error_detail = result.ErrorDetail,
                    chunk_list = result.ChunkList,
                    text_list = result.TextList,
                    synonym_list = result.SynonymList
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"GetStatus処理中にエラーが発生 workId: {workId}");
                
                // 外部APIエラー状態の場合は適切にレスポンスを返す
                if (ex.Message.Contains("API request failed") && ex.Message.Contains("state\":2"))
                {
                    _logger.LogWarning($"workId {workId}: 外部APIがエラー状態（state: 2）を返しました");
                    
                    // エラー状態として正常にレスポンス
                    return Ok(new
                    {
                        work_id = workId,
                        page_no = -1,
                        max_page_no = -1,
                        processing_state = "Error",
                        state = 2,
                        return_code = 9999,
                        error_detail = "システムで問題が発生しています。（Error：10102）",
                        chunk_list = new List<object>(),
                        text_list = new List<object>(),
                        synonym_list = new List<object>()
                    });
                }
                
                return StatusCode(500, new { error = "ステータス確認中にエラーが発生しました", details = ex.Message });
            }
        }

        /// <summary>
        /// キャッシュ統計情報を取得するエンドポイント
        /// </summary>
        [HttpGet("cache-stats")]
        public IActionResult GetCacheStats()
        {
            try
            {
                // 【セキュリティ強化】管理者権限チェック
                var currentUser = User.Identity?.Name;
                if (string.IsNullOrEmpty(currentUser))
                {
                    return Unauthorized("ユーザーが認証されていません");
                }
                
                var currentRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "User";
                if (currentRole != "Admin")
                {
                    return Forbidden(new { error = "管理者権限が必要です" });
                }
                
                _logger.LogInformation($"管理者 {currentUser} がキャッシュ統計を取得");
                
                var now = DateTime.UtcNow;
                var totalCached = _dataCache.Count;
                var expiredCount = _dataCache.Count(kvp => kvp.Value.IsExpired);
                var validCount = totalCached - expiredCount;
                
                var oldestCache = _dataCache.Values.OrderBy(v => v.CachedAt).FirstOrDefault();
                var newestCache = _dataCache.Values.OrderByDescending(v => v.CachedAt).FirstOrDefault();
                
                return Ok(new
                {
                    totalCachedItems = totalCached,
                    validItems = validCount,
                    expiredItems = expiredCount,
                    maxCacheSize = _maxCacheSize,
                    cacheExpirationHours = _cacheExpiration.TotalHours,
                    oldestCacheAge = oldestCache != null ? (now - oldestCache.CachedAt).TotalMinutes : 0,
                    newestCacheAge = newestCache != null ? (now - newestCache.CachedAt).TotalMinutes : 0,
                    memoryUsagePercentage = Math.Round((double)totalCached / _maxCacheSize * 100, 2),
                    lastCleanupTime = now,
                    cacheHitRate = "実装予定" // 今後実装
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "キャッシュ統計の取得に失敗しました" });
            }
        }

        /// <summary>
        /// キャッシュを手動でクリアするエンドポイント
        /// </summary>
        [HttpPost("clear-cache")]
        public IActionResult ClearCache([FromQuery] string workId = null)
        {
            try
            {
                // 【セキュリティ強化】管理者権限チェック
                var currentUser = User.Identity?.Name;
                if (string.IsNullOrEmpty(currentUser))
                {
                    return Unauthorized("ユーザーが認証されていません");
                }
                
                var currentRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "User";
                if (currentRole != "Admin")
                {
                    return Forbidden(new { error = "管理者権限が必要です" });
                }
                
                _logger.LogInformation($"管理者 {currentUser} がキャッシュクリアを実行: workId={workId ?? "全て"}");
                
                if (string.IsNullOrEmpty(workId))
                {
                    // 全キャッシュをクリア
                    ClearAllCache();
                    return Ok(new { message = "全キャッシュを削除しました" });
                }
                else
                {
                    // 特定のworkIdキャッシュをクリア
                    InvalidateCache(workId);
                    return Ok(new { message = $"workId {workId} のキャッシュを削除しました" });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "キャッシュクリアに失敗しました" });
            }
        }

        /// <summary>
        /// workId履歴に追加するエンドポイント
        /// </summary>
        [HttpPost("add-workid-history")]
        public async Task<IActionResult> AddWorkIdHistory([FromBody] AddWorkIdHistoryRequest request)
        {
            try
            {
                // 【セキュリティ強化】現在のユーザーの認証制御を追加
                var currentUser = User.Identity?.Name;
                if (string.IsNullOrEmpty(currentUser))
                {
                    return Unauthorized("ユーザーが認証されていません");
                }
                
                if (string.IsNullOrEmpty(request.WorkId))
                {
                    return BadRequest(new { error = "workIdが必要です" });
                }
                
                if (string.IsNullOrEmpty(request.FileName))
                {
                    return BadRequest(new { error = "fileNameが必要です" });
                }
                
                _logger.LogInformation($"ユーザー {currentUser} がworkId {request.WorkId} を履歴に追加");
                
                // workId履歴に追加
                await AddWorkIdToHistory(request.WorkId, request.FileName);
                
                // 新しいworkIdのキャッシュを無効化（最新データを取得するため）
                InvalidateCache(request.WorkId);
                
                
                return Ok(new { 
                    success = true, 
                    message = "workId履歴に追加しました",
                    workId = request.WorkId,
                    fileName = request.FileName
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { 
                    error = "workId履歴の追加中にエラーが発生しました", 
                    details = ex.Message 
                });
            }
        }

        /// <summary>
        /// Azure Search接続テスト
        /// </summary>
        [HttpGet("test-azure-search")]
        public async Task<IActionResult> TestAzureSearch()
        {
            try
            {
                // 【セキュリティ強化】管理者権限チェック
                var currentUser = User.Identity?.Name;
                if (string.IsNullOrEmpty(currentUser))
                {
                    return Unauthorized("ユーザーが認証されていません");
                }
                
                var currentRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "User";
                if (currentRole != "Admin")
                {
                    return Forbidden(new { error = "管理者権限が必要です" });
                }
                
                _logger.LogInformation($"管理者 {currentUser} がAzure Search接続テストを実行");
                
                Console.WriteLine("🔍 Azure Search接続テスト開始");
                
                var azureSearchService = HttpContext.RequestServices.GetRequiredService<IAzureSearchService>();
                var isConnected = await azureSearchService.TestConnectionAsync();
                
                if (isConnected)
                {
                    // 簡単な検索テストも実行
                    var testResults = await azureSearchService.SearchDocumentsAsync("テスト", null, 3);
                    
                    return Ok(new
                    {
                        success = true,
                        message = "Azure Search接続成功",
                        connectionTest = true,
                        searchTest = true,
                        sampleResultsCount = testResults.Count,
                        timestamp = DateTime.UtcNow
                    });
                }
                else
                {
                    return Ok(new
                    {
                        success = false,
                        message = "Azure Search接続失敗",
                        connectionTest = false,
                        timestamp = DateTime.UtcNow
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Azure Search接続テストエラー: {ex.Message}");
                return StatusCode(500, new { 
                    success = false,
                    error = "Azure Search接続テストに失敗しました", 
                    details = ex.Message,
                    timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpGet("test-search-query")]
        public async Task<IActionResult> TestSearchQuery([FromQuery] string workId = "90a1db69fbd6174549afea9da68caff1")
        {
            try
            {
                // 🔐 認証チェック
                if (!User?.Identity?.IsAuthenticated ?? true)
                {
                    return Unauthorized(new { message = "認証が必要です" });
                }

                var currentUsername = User.Identity.Name;
                var allowedWorkIds = await _authorizationService.GetAllowedWorkIdsAsync(currentUsername);
                
                Console.WriteLine($"🔍 [TEST] 検索クエリ最適化テスト開始 - 対象workId: {workId}");
                
                // 複数のクエリパターンをテスト
                var testQueries = new[]
                {
                    "B経費の担当・本部長の経常予算は？",
                    "経費 担当 本部長 経常予算",
                    "経費 予算",
                    "経常予算",
                    "B経費",
                    "担当 本部長",
                    "予算活用",
                    "経費・投資"
                };
                
                var results = new List<object>();
                
                foreach (var query in testQueries)
                {
                    Console.WriteLine($"🔍 [TEST] クエリテスト: '{query}'");
                    
                    var searchResults = await _azureSearchService.SemanticSearchAsync(query, allowedWorkIds, 50);
                    var targetResults = searchResults.Where(r => r.Filepath == workId).ToList();
                    
                    var result = new
                    {
                        query = query,
                        totalResults = searchResults.Count,
                        targetWorkIdFound = targetResults.Count > 0,
                        targetWorkIdCount = targetResults.Count,
                        topRanking = targetResults.Count > 0 ? searchResults.IndexOf(targetResults.First()) + 1 : -1,
                        topScore = targetResults.Count > 0 ? targetResults.First().Score : 0,
                        targetResults = targetResults.Take(3).Select(r => new
                        {
                            id = r.Id,
                            score = r.Score,
                            ranking = searchResults.IndexOf(r) + 1,
                            content = r.Content?.Length > 100 ? r.Content.Substring(0, 100) + "..." : r.Content
                        }).ToList()
                    };
                    
                    results.Add(result);
                    
                    Console.WriteLine($"✅ [TEST] '{query}' → 総結果: {result.totalResults}件, 対象workId: {(result.targetWorkIdFound ? $"{result.topRanking}位 (スコア: {result.topScore:F4})" : "見つからず")}");
                }
                
                return Ok(new
                {
                    testWorkId = workId,
                    testResults = results,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [TEST] 検索クエリテストエラー: {ex.Message}");
                return StatusCode(500, new { error = "検索クエリテストに失敗しました", details = ex.Message });
            }
        }

        /// <summary>
        /// 指定workIdのインデックスデータをメイン/センテンス両方から削除（管理系）
        /// </summary>
        [HttpDelete("indexes/{workId}")]
        public async Task<IActionResult> DeleteWorkIdIndexes(string workId)
        {
            try
            {
                // 認証チェック
                if (!User?.Identity?.IsAuthenticated ?? true)
                {
                    return Unauthorized(new { error = "認証が必要です" });
                }

                var currentUsername = User.Identity.Name;
                var currentRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "User";

                // 権限: Admin は全削除可、一般は自分の workId のみ
                var allowedWorkIds = await _authorizationService.GetAllowedWorkIdsAsync(currentUsername);
                if (currentRole != "Admin" && !allowedWorkIds.Contains(workId))
                {
                    return Forbidden(new { error = "指定されたworkIdへの削除権限がありません", workId });
                }

                // ユーザー固有のインデックス切替
                _azureSearchService.SetUserSpecificIndexes(currentUsername);

                // 削除実行
                var (deletedMain, deletedSentence) = await _azureSearchService.DeleteWorkIdAsync(workId, currentUsername);

                return Ok(new {
                    success = true,
                    workId = workId,
                    deleted_main = deletedMain,
                    deleted_sentence = deletedSentence,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "workId削除中にエラーが発生: {WorkId}", workId);
                return StatusCode(500, new { error = "インデックス削除に失敗しました", details = ex.Message });
            }
        }
    }

    public class DataStructuringChatRequest
    {
        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("file_id")]
        public string? FileId { get; set; }

        [JsonPropertyName("context")]
        public string? Context { get; set; }

        [JsonPropertyName("sources")]
        public List<SourceReference> Sources { get; set; } = new List<SourceReference>();

        [JsonPropertyName("use_chunks")]
        public bool UseChunks { get; set; } = false;

        [JsonPropertyName("keywords")]
        public List<string> Keywords { get; set; } = new List<string>();

        [JsonPropertyName("chunks")]
        public List<ChunkItem> Chunks { get; set; } = new List<ChunkItem>();

        [JsonPropertyName("work_id")]
        public string? WorkId { get; set; }

        [JsonPropertyName("synonyms")]
        public string? Synonyms { get; set; }

        [JsonPropertyName("synonym_list")]
        public List<SynonymItem> SynonymList { get; set; } = new List<SynonymItem>();
    }

    public class SourceReference
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
        
        [JsonPropertyName("name")]
        public string Name { get; set; }
        
        [JsonPropertyName("filepath")]
        public string Filepath { get; set; }
    }

    /// <summary>
    /// Tokenize APIのレスポンスモデル
    /// </summary>
    public class TokenizeApiResponse
    {
        [JsonPropertyName("tokenList")]
        public List<TokenItem> TokenList { get; set; } = new List<TokenItem>();
    }

    /// <summary>
    /// トークンアイテム
    /// </summary>
    public class TokenItem
    {
        [JsonPropertyName("text")]
        public string Text { get; set; }

        [JsonPropertyName("boostScore")]
        public double BoostScore { get; set; }
    }

    /// <summary>
    /// シノニムマッチ結果を表すクラス
    /// </summary>
    public class SynonymMatch
    {
        public string OriginalKeyword { get; set; }
        public string MatchedSynonym { get; set; }
        public List<string> RelatedSynonyms { get; set; } = new List<string>();
    }

    /// <summary>
    /// DataStructuringControllerの拡張メソッド
    /// </summary>
    public static class DataStructuringControllerExtensions
    {
        /// <summary>
        /// メッセージからキーワードを抽出（Tokenize APIを使用）
        /// </summary>
        public static async Task<List<string>> ExtractKeywordsFromMessageAsync(this DataStructuringController controller, string message)
        {
            var keywords = new List<string>();
            
            if (string.IsNullOrEmpty(message))
                return keywords;
            
            try
            {
                // Tokenize APIを呼び出し
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(30);
                
                var apiRequest = new
                {
                    userId = "ilu-demo", // 設定値を直接使用
                    password = "ilupass", // 設定値を直接使用
                    type = "",
                    text = message
                };
                
                var jsonContent = JsonSerializer.Serialize(apiRequest, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                
                var response = await httpClient.PostAsync("http://10.24.152.66:9926/api/Tokenize", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var tokenizeResponse = JsonSerializer.Deserialize<TokenizeApiResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });
                    
                    if (tokenizeResponse?.TokenList != null)
                    {
                        var logger = controller.HttpContext?.RequestServices?.GetService<ILogger<DataStructuringController>>();
                        
                        // 🎯 画面表示と同じフィルタリングロジックを適用
                        var filteredKeywords = tokenizeResponse.TokenList
                            .Where(token => !string.IsNullOrEmpty(token.Text)) // 有効なキーワードのみ
                            .Where(token => !IsSymbolOnly(token.Text)) // 記号のみを除外
                            .OrderByDescending(token => token.BoostScore) // スコアの高い順に並べ替え
                            .Take(10) // 最大10件に制限（画面表示と同じ）
                            .Select(token => token.Text) // キーワードテキストのみを抽出
                            .ToList();
                        
                        
                        return filteredKeywords;
                    }
                }
                else
                {
                    var logger = controller.HttpContext?.RequestServices?.GetService<ILogger<DataStructuringController>>();
                }
            }
            catch (Exception ex)
            {
                var logger = controller.HttpContext?.RequestServices?.GetService<ILogger<DataStructuringController>>();
            }
            
            // フォールバック: 従来の方式
            return ExtractKeywordsFromMessageFallback(message);
        }
        
        /// <summary>
        /// 一般的すぎる語かどうかをチェック
        /// </summary>
        private static bool IsCommonWord(string word)
        {
            var commonWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "こと", "もの", "ため", "場合", "とき", "時", "際", "上", "中", "下", "前", "後", "内", "外",
                "全て", "すべて", "各", "毎", "その", "この", "あの", "どの", "など", "なお", "また", "さらに",
                "ただし", "しかし", "でも", "けれど", "それで", "そして", "または", "及び", "並びに"
            };
            
            return commonWords.Contains(word);
        }
        
        /// <summary>
        /// 低品質なシノニムグループかどうかをチェック
        /// </summary>
        private static bool IsLowQualitySynonymGroup(List<string> synonyms, HashSet<string> meaninglessWords)
        {
            if (synonyms == null || synonyms.Count < 2)
                return true;
            
            // グループ内の意味のない語の割合をチェック
            int meaninglessCount = synonyms.Count(s => meaninglessWords.Contains(s) || s.Length < 2);
            double meaninglessRatio = (double)meaninglessCount / synonyms.Count;
            
            // 50%以上が意味のない語の場合は低品質とみなす
            return meaninglessRatio >= 0.5;
        }
        
        /// <summary>
        /// 数字のみかどうかをチェック
        /// </summary>
        private static bool IsNumericOnly(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.All(c => char.IsDigit(c) || c == '.' || c == ',');
        }

        /// <summary>
        /// シノニムを含めたクエリ拡張
        /// </summary>
        public static string ExpandQueryWithSynonyms(this DataStructuringController controller, string originalQuery, List<SynonymMatch> synonymMatches)
        {
            if (synonymMatches == null || synonymMatches.Count == 0)
                return originalQuery;
            
            var expandedQuery = new StringBuilder(originalQuery);
            expandedQuery.AppendLine("\n\n【🔍 シノニム検索結果】");
            expandedQuery.AppendLine("以下のキーワードについて、関連語・シノニムが発見されました：");
            expandedQuery.AppendLine();
            
            foreach (var match in synonymMatches)
            {
                expandedQuery.AppendLine($"✅ 「{match.OriginalKeyword}」の関連語:");
                expandedQuery.AppendLine($"   → {string.Join(", ", match.RelatedSynonyms)}");
                expandedQuery.AppendLine();
            }
            
            expandedQuery.AppendLine("【検索指示】");
            expandedQuery.AppendLine("上記の関連語・シノニムも含めて文書を検索し、より包括的な回答を提供してください。");
            expandedQuery.AppendLine("シノニムマッチした用語については、回答の最後に「シノニム情報」として表示してください。");
            
            return expandedQuery.ToString();
        }

        /// <summary>
        /// 記号のみかどうかをチェック
        /// </summary>
        private static bool IsSymbolOnly(string text)
        {
            return text.All(c => char.IsSymbol(c) || char.IsPunctuation(c));
        }

        /// <summary>
        /// フォールバック用のキーワード抽出（従来の方式）
        /// </summary>
        private static List<string> ExtractKeywordsFromMessageFallback(string message)
        {
            var keywords = new List<string>();
            
            if (string.IsNullOrEmpty(message))
                return keywords;
            
            // 日本語の区切り文字で分割
            var separators = new char[] { ' ', '　', '、', '。', '！', '？', '（', '）', '(', ')', '\n', '\r', '\t' };
            var words = message.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var word in words)
            {
                var cleanWord = word.Trim();
                
                // 記号のみの単語は除外
                if (IsSymbolOnly(cleanWord))
                    continue;
                
                // 英語と日本語を区別して長さをチェック
                bool isEnglish = IsEnglishWord(cleanWord);
                bool isValidLength = (isEnglish && cleanWord.Length >= 1) || (!isEnglish && cleanWord.Length >= 2);
                
                if (isValidLength)
                {
                    keywords.Add(cleanWord);
                }
            }
            
            return keywords;
        }

        /// <summary>
        /// メッセージからキーワードを抽出（従来の方式）
        /// </summary>
        public static List<string> ExtractKeywordsFromMessage(this DataStructuringController controller, string message)
        {
            var keywords = new List<string>();
            
            if (string.IsNullOrEmpty(message))
                return keywords;
            
            // 日本語の区切り文字で分割
            var separators = new char[] { ' ', '　', '、', '。', '！', '？', '（', '）', '(', ')', '\n', '\r', '\t' };
            var words = message.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var word in words)
            {
                var cleanWord = word.Trim();
                
                // 記号のみの単語は除外
                if (IsSymbolOnly(cleanWord))
                    continue;
                
                // 英語と日本語を区別して長さをチェック
                bool isEnglish = IsEnglishWord(cleanWord);
                bool isValidLength = (isEnglish && cleanWord.Length >= 1) || (!isEnglish && cleanWord.Length >= 2);
                
                if (isValidLength)
                {
                    keywords.Add(cleanWord);
                }
            }
            
            return keywords;
        }
        
        /// <summary>
        /// 英語の単語かどうかを判定
        /// </summary>
        private static bool IsEnglishWord(string word)
        {
            if (string.IsNullOrEmpty(word)) return false;
            
            // 英語のアルファベットのみで構成されているかチェック
            return word.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'));
        }

        /// <summary>
        /// シノニムリストからキーワードに関連するシノニムを検索
        /// </summary>
        public static List<SynonymMatch> FindSynonymsForKeywords(this DataStructuringController controller, List<string> keywords, List<SynonymItem> synonymList)
        {
            var synonymMatches = new List<SynonymMatch>();
            
            if (synonymList == null || synonymList.Count == 0)
            {
                var logger = controller.HttpContext?.RequestServices?.GetService<ILogger<DataStructuringController>>();
                return synonymMatches;
            }
            
            var logger2 = controller.HttpContext?.RequestServices?.GetService<ILogger<DataStructuringController>>();
            
            // シノニムリストの最初の数件をデバッグ出力
            for (int i = 0; i < Math.Min(3, synonymList.Count); i++)
            {
                var group = synonymList[i];
                if (group.Synonyms != null && group.Synonyms.Count > 0)
                {
                }
                else
                {
                }
            }
            
            foreach (var keyword in keywords)
            {
                bool foundMatch = false;
                
                for (int groupIndex = 0; groupIndex < synonymList.Count; groupIndex++)
                {
                    var synonymGroup = synonymList[groupIndex];
                    
                    if (synonymGroup.Synonyms == null)
                    {
                        continue;
                    }
                    
                    if (synonymGroup.Synonyms.Count == 0)
                    {
                        continue;
                    }
                    
                    // 特定のキーワード（ビルワン）の場合は詳細ログ
                    if (keyword.Equals("ビルワン", StringComparison.OrdinalIgnoreCase))
                    {
                        
                        foreach (var synonym in synonymGroup.Synonyms)
                        {
                            if (string.Equals(synonym, keyword, StringComparison.OrdinalIgnoreCase))
                            {
                            }
                        }
                    }
                    
                    // キーワードがシノニムグループに含まれているかチェック（大文字小文字を区別しない）
                    var matchedSynonym = synonymGroup.Synonyms.FirstOrDefault(s => 
                        string.Equals(s, keyword, StringComparison.OrdinalIgnoreCase));
                    
                    if (matchedSynonym != null)
                    {
                        foundMatch = true;
                        
                        // マッチしたシノニムグループの全ての語を取得（元のキーワードは除く）
                        var relatedSynonyms = synonymGroup.Synonyms
                            .Where(s => !string.Equals(s, keyword, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        
                        
                        if (relatedSynonyms.Count > 0)
                        {
                            synonymMatches.Add(new SynonymMatch
                            {
                                OriginalKeyword = keyword,
                                MatchedSynonym = matchedSynonym,
                                RelatedSynonyms = relatedSynonyms
                            });
                            
                            logger2?.LogInformation($"【DataStructuring】シノニム発見: '{keyword}' → [{string.Join(", ", relatedSynonyms)}]");
                        }
                        break; // マッチが見つかったらこのキーワードの検索を終了
                    }
                }
                
                if (!foundMatch)
                {
                    
                    // 特定のキーワード（ビルワン）の場合は、なぜマッチしなかったかを詳細調査
                    if (keyword.Equals("ビルワン", StringComparison.OrdinalIgnoreCase))
                    {
                        
                        // ビルワンを含むグループを探す
                        bool foundBillOneGroup = false;
                        for (int i = 0; i < synonymList.Count; i++)
                        {
                            var group = synonymList[i];
                            if (group.Synonyms != null && group.Synonyms.Any(s => s.Contains("ビルワン") || s.Contains("Bill One")))
                            {
                                foundBillOneGroup = true;
                            }
                        }
                        
                        if (!foundBillOneGroup)
                        {
                        }
                    }
                }
            }
            
            return synonymMatches;
        }
    }

    public class BatchDownloadRequest
    {
        public List<string> FilePaths { get; set; } = new List<string>();
    }

    // 全ドキュメント検索リクエストモデル
    public class SearchAllDocumentsRequest
    {
        [JsonPropertyName("query")]
        [Required(ErrorMessage = "検索クエリは必須です")]
        public string Query { get; set; }
        
        [JsonPropertyName("workId")]
        public string? WorkId { get; set; } // nullable にして明示的にオプショナル
    }

    /// <summary>
    /// workId履歴管理用のクラス
    /// </summary>
    public class WorkIdHistoryItem
    {
        [JsonPropertyName("workId")]
        public string WorkId { get; set; }
        
        [JsonPropertyName("fileName")]
        public string FileName { get; set; }
        
        [JsonPropertyName("uploadDate")]
        public DateTime UploadDate { get; set; }
    }

    /// <summary>
    /// workId履歴追加リクエスト用のクラス
    /// </summary>
    public class AddWorkIdHistoryRequest
    {
        [JsonPropertyName("workId")]
        public string WorkId { get; set; }
        
        [JsonPropertyName("fileName")]
        public string FileName { get; set; }
    }
}