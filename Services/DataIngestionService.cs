using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AzureRag.Models;
using static AzureRag.Services.AutoStructureService;

namespace AzureRag.Services
{
    public class DataIngestionService : IDataIngestionService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<DataIngestionService> _logger;
        private readonly IAutoStructureService _autoStructureService;
        private readonly Services.IAuthorizationService _authorizationService;
        private readonly IAzureSearchService _azureSearchService;
        
        // データ投入設定
        private readonly string _azureSearchEndpoint;
        private readonly string _azureSearchKey;
        private readonly string _azureSearchApiVersion;
        private readonly string _mainIndexName;
        private readonly string _sentenceIndexName;
        private readonly string _externalApiBaseUrl;
        private readonly string _externalApiUserId;
        private readonly string _externalApiPassword;
        
        // Azure OpenAI設定（ベクトル埋め込み用）
        private readonly string _azureOpenAIEndpoint;
        private readonly string _azureOpenAIKey;
        private readonly string _azureOpenAIApiVersion;
        private readonly string _embeddingModelDeployment;

        public DataIngestionService(
            IConfiguration configuration,
            ILogger<DataIngestionService> logger,
            IHttpClientFactory httpClientFactory,
            IAutoStructureService autoStructureService,
            Services.IAuthorizationService authorizationService,
            IAzureSearchService azureSearchService)
        {
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient();
            _autoStructureService = autoStructureService;
            _authorizationService = authorizationService;
            _azureSearchService = azureSearchService;

            // データ投入設定を取得
            var dataIngestionConfig = configuration.GetSection("DataIngestion");
            _azureSearchEndpoint = dataIngestionConfig["AzureSearchEndpoint"];
            _azureSearchKey = dataIngestionConfig["AzureSearchKey"];
            _azureSearchApiVersion = dataIngestionConfig["AzureSearchApiVersion"] ?? "2024-07-01";
            _mainIndexName = dataIngestionConfig["MainIndexName"] ?? "oec";
            _sentenceIndexName = dataIngestionConfig["SentenceIndexName"] ?? "oec-sentence";
            _externalApiBaseUrl = dataIngestionConfig["ExternalApiBaseUrl"];
            _externalApiUserId = dataIngestionConfig["ExternalApiUserId"];
            _externalApiPassword = dataIngestionConfig["ExternalApiPassword"];

            // Azure OpenAI設定を取得
            _azureOpenAIEndpoint = dataIngestionConfig["AzureOpenAIEndpoint"];
            _azureOpenAIKey = dataIngestionConfig["AzureOpenAIKey"];
            _azureOpenAIApiVersion = dataIngestionConfig["AzureOpenAIApiVersion"] ?? "2023-05-15";
            _embeddingModelDeployment = dataIngestionConfig["EmbeddingModelDeployment"];

            _logger.LogInformation("DataIngestionService初期化完了 - インデックス: {MainIndex}, {SentenceIndex}", _mainIndexName, _sentenceIndexName);
        }

        /// <summary>
        /// 外部APIからチャンクデータを取得（認証付き）
        /// </summary>
        public async Task<List<ChunkItem>> GetChunksFromExternalApiAsync(string username, string workId)
        {
            try
            {
                _logger.LogInformation("認証付きチャンクデータ取得開始: ユーザー={Username}, workId={WorkId}", username, workId);

                // アクセス権限チェック
                var hasAccess = await _authorizationService.CanAccessWorkIdAsync(username, workId);
                if (!hasAccess)
                {
                    _logger.LogWarning("アクセス拒否: ユーザー={Username}, workId={WorkId}", username, workId);
                    return new List<ChunkItem>();
                }

                // AutoStructureServiceを使用してチャンクデータを取得
                var structuredData = await _autoStructureService.GetStructuredDataAsync(workId);

                if (structuredData?.ChunkList != null && structuredData.ChunkList.Count > 0)
                {
                    _logger.LogInformation("認証付きチャンクデータ取得成功: ユーザー={Username}, workId={WorkId}, チャンク数={ChunkCount}", 
                        username, workId, structuredData.ChunkList.Count);
                    return structuredData.ChunkList;
                }
                else
                {
                    _logger.LogWarning("チャンクデータが見つかりません: ユーザー={Username}, workId={WorkId}", username, workId);
                    return new List<ChunkItem>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "認証付きチャンクデータ取得でエラー: ユーザー={Username}, workId={WorkId}", username, workId);
                return new List<ChunkItem>();
            }
        }

        /// <summary>
        /// チャンクデータをAzure Searchインデックスに登録（認証付き・両インデックス対応）
        /// </summary>
        public async Task<bool> IndexChunksToAzureSearchAsync(string username, string workId, List<ChunkItem> chunks)
        {
            try
            {
                _logger.LogInformation("認証付きAzure Searchインデックス登録開始: ユーザー={Username}, workId={WorkId}, チャンク数={ChunkCount}", 
                    username, workId, chunks.Count);

                // アクセス権限チェック
                var hasAccess = await _authorizationService.CanAccessWorkIdAsync(username, workId);
                if (!hasAccess)
                {
                    _logger.LogWarning("インデックス登録アクセス拒否: ユーザー={Username}, workId={WorkId}", username, workId);
                    return false;
                }

                if (chunks.Count == 0)
                {
                    _logger.LogWarning("登録するチャンクがありません: ユーザー={Username}, workId={WorkId}", username, workId);
                    return true; // 空の場合は成功とみなす
                }

                // workIdメタデータを取得
                var metadata = await _authorizationService.GetWorkIdMetadataAsync(workId);

                // Step 1: oecインデックス（キーワード検索用）に登録
                bool oecSuccess = await IndexToMainIndexAsync(username, workId, chunks, metadata);
                
                // Step 2: oec-sentenceインデックス（ベクトル検索用）に登録
                bool sentenceSuccess = await IndexToSentenceIndexAsync(username, workId, chunks, metadata);

                if (oecSuccess && sentenceSuccess)
                {
                    _logger.LogInformation("✅ 両インデックス登録成功: ユーザー={Username}, workId={WorkId}", username, workId);
                    return true;
                }
                else
                {
                    _logger.LogWarning("⚠️ インデックス登録部分失敗: ユーザー={Username}, workId={WorkId}, oec={OecSuccess}, sentence={SentenceSuccess}", 
                        username, workId, oecSuccess, sentenceSuccess);
                    // 一方でも成功していれば部分的成功とみなす
                    return oecSuccess || sentenceSuccess;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "認証付きAzure Searchインデックス登録でエラー: ユーザー={Username}, workId={WorkId}", username, workId);
                return false;
            }
        }

        /// <summary>
        /// oecインデックス（キーワード検索用）に登録
        /// </summary>
        private async Task<bool> IndexToMainIndexAsync(string username, string workId, List<ChunkItem> chunks, WorkIdMetadata metadata)
        {
            try
            {
                _logger.LogInformation("🔸 [MAIN] oecインデックス登録開始: workId={WorkId}, チャンク数={ChunkCount}", workId, chunks.Count);

                // oecインデックス用のドキュメントを作成
                var documents = chunks.Select((chunk, index) => new
                {
                    id = $"{workId}_chunk_{chunk.PageNo}_{chunk.ChunkNo}",
                    workId = workId,
                    content = chunk.Chunk,
                    title = $"{metadata?.Name ?? workId} - ページ{chunk.PageNo + 1} チャンク{chunk.ChunkNo}",
                    file_name = metadata?.Name ?? $"WorkID: {workId}",
                    page_no = chunk.PageNo,
                    created_at = DateTime.UtcNow
                }).ToList();

                var url = $"{_azureSearchEndpoint}/indexes/{_mainIndexName}/docs/index?api-version={_azureSearchApiVersion}";
                
                // API version 2024-07-01対応：@search.action形式でドキュメント構築
                var documentsForUpload = documents.Select(doc => new Dictionary<string, object>
                {
                    ["@search.action"] = "mergeOrUpload",
                    ["id"] = doc.id,
                    ["workId"] = doc.workId,
                    ["content"] = doc.content,
                    ["title"] = doc.title,
                    ["file_name"] = doc.file_name,
                    ["page_no"] = doc.page_no,
                    ["created_at"] = doc.created_at
                }).ToArray();
                
                var requestBody = new
                {
                    value = documentsForUpload
                };

                var jsonContent = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions 
                { 
                    WriteIndented = false,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("api-key", _azureSearchKey);

                var response = await _httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseText = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation("✅ [MAIN] oecインデックス登録成功: workId={WorkId}", workId);
                    return true;
                }
                else
                {
                    var errorText = await response.Content.ReadAsStringAsync();
                    _logger.LogError("❌ [MAIN] oecインデックス登録失敗: workId={WorkId}, ステータス={StatusCode}, エラー={Error}", 
                        workId, response.StatusCode, errorText);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [MAIN] oecインデックス登録でエラー: workId={WorkId}", workId);
                return false;
            }
        }

        /// <summary>
        /// oec-sentenceインデックス（ベクトル検索用）に登録
        /// </summary>
        private async Task<bool> IndexToSentenceIndexAsync(string username, string workId, List<ChunkItem> chunks, WorkIdMetadata metadata)
        {
            try
            {
                _logger.LogInformation("🔸 [SENTENCE] oec-sentenceインデックス登録開始: workId={WorkId}, チャンク数={ChunkCount}", workId, chunks.Count);

                var url = $"{_azureSearchEndpoint}/indexes/{_sentenceIndexName}/docs/index?api-version={_azureSearchApiVersion}";
                
                // API version 2024-07-01対応：@search.action形式でドキュメント構築
                var documentsForUpload = new List<object>();
                
                foreach (var chunk in chunks)
                {
                    _logger.LogDebug("🔸 [SENTENCE] ベクトル生成中: workId={WorkId}, page={PageNo}, chunk={ChunkNo}", 
                        workId, chunk.PageNo, chunk.ChunkNo);

                    // ベクトル埋め込みを生成
                    var embedding = await GetTextEmbeddingAsync(chunk.Chunk);
                    
                    if (embedding.Count == 0)
                    {
                        _logger.LogWarning("⚠️ [SENTENCE] ベクトル生成失敗、スキップ: workId={WorkId}, page={PageNo}, chunk={ChunkNo}", 
                            workId, chunk.PageNo, chunk.ChunkNo);
                        continue;
                    }

                    // 新API形式：@search.actionを含むドキュメント
                    var document = new Dictionary<string, object>
                    {
                        ["@search.action"] = "mergeOrUpload",
                        ["id"] = $"{workId}_sentence_{chunk.PageNo}_{chunk.ChunkNo}",
                        ["workId"] = workId,
                        ["chunk_text"] = chunk.Chunk,
                        ["chunk_embeddings"] = embedding.ToArray(),
                        ["document_id"] = $"{workId}_chunk_{chunk.PageNo}_{chunk.ChunkNo}",
                        ["file_name"] = metadata?.Name ?? $"WorkID: {workId}",
                        ["page_no"] = chunk.PageNo,
                        ["chunk_index"] = chunk.ChunkNo,
                        ["created_at"] = DateTime.UtcNow
                    };

                    documentsForUpload.Add(document);
                    
                    _logger.LogDebug("✅ [SENTENCE] ベクトル生成完了: workId={WorkId}, page={PageNo}, chunk={ChunkNo}, 次元数={Dimensions}", 
                        workId, chunk.PageNo, chunk.ChunkNo, embedding.Count);
                }

                if (documentsForUpload.Count == 0)
                {
                    _logger.LogWarning("⚠️ [SENTENCE] ベクトル生成に成功したチャンクがありません: workId={WorkId}", workId);
                    return false;
                }

                var requestBody = new
                {
                    value = documentsForUpload.ToArray()
                };

                var jsonContent = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions 
                { 
                    WriteIndented = false,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("api-key", _azureSearchKey);

                var response = await _httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseText = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation("✅ [SENTENCE] oec-sentenceインデックス登録成功: workId={WorkId}, 登録件数={Count}", 
                        workId, documentsForUpload.Count);
                    return true;
                }
                else
                {
                    var errorText = await response.Content.ReadAsStringAsync();
                    _logger.LogError("❌ [SENTENCE] oec-sentenceインデックス登録失敗: workId={WorkId}, ステータス={StatusCode}, エラー={Error}", 
                        workId, response.StatusCode, errorText);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [SENTENCE] oec-sentenceインデックス登録でエラー: workId={WorkId}", workId);
                return false;
            }
        }

        /// <summary>
        /// 外部APIからチャンク取得→Azure Search登録の完全なパイプライン実行（認証付き）
        /// </summary>
        public async Task<(bool success, int processedChunks, string errorMessage)> ProcessWorkIdAsync(string username, string workId)
        {
            try
            {
                _logger.LogInformation("認証付きデータ投入パイプライン開始: ユーザー={Username}, workId={WorkId}", username, workId);

                // アクセス権限チェック
                var hasAccess = await _authorizationService.CanAccessWorkIdAsync(username, workId);
                if (!hasAccess)
                {
                    return (false, 0, $"ユーザー '{username}' はworkId '{workId}' にアクセスする権限がありません");
                }

                // Step 1: 外部APIからチャンクデータを取得
                var chunks = await GetChunksFromExternalApiAsync(username, workId);
                
                if (chunks.Count == 0)
                {
                    return (false, 0, $"workId '{workId}' のチャンクデータが取得できませんでした");
                }

                // 🆕 Step 2: ユーザーの許可インデックスペアを取得
                var userIndexPairs = await _authorizationService.GetUserIndexPairsAsync(username);
                
                if (userIndexPairs.Count == 0)
                {
                    _logger.LogWarning("🚫 ユーザーの許可インデックスペアが存在しません: ユーザー={Username}", username);
                    return (false, 0, "データベース登録権限が設定されていません（管理者にお問い合わせください）");
                }

                // 🆕 Step 3: スマート登録前に権限チェック
                var hasAnyIndexAccess = false;
                foreach (var pair in userIndexPairs)
                {
                    var (canMain, _) = await _authorizationService.CanUserIndexToWithReasonAsync(username, pair.MainIndex);
                    var (canSentence, _) = await _authorizationService.CanUserIndexToWithReasonAsync(username, pair.SentenceIndex);
                    
                    if (canMain && canSentence)
                    {
                        hasAnyIndexAccess = true;
                        break;
                    }
                }

                if (!hasAnyIndexAccess)
                {
                    _logger.LogError("🚫 セキュリティエラー: ユーザー={Username} は許可されたインデックスペアが存在しません", username);
                    return (false, 0, "セキュリティエラー: 指定されたインデックスへのアクセス権限がありません");
                }

                // 🆕 Step 4: スマート登録（動的インデックスペア対応）
                var (success, indexingErrorMessage) = await IndexChunksSmartAsync(username, workId, chunks, false, false);

                if (success)
                {
                    _logger.LogInformation("✅ データ投入パイプライン成功: ユーザー={Username}, workId={WorkId}, チャンク数={ChunkCount}", 
                        username, workId, chunks.Count);
                    return (true, chunks.Count, indexingErrorMessage);
                }
                else
                {
                    return (false, 0, indexingErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "データ投入パイプラインでエラー: ユーザー={Username}, workId={WorkId}", username, workId);
                return (false, 0, $"データ投入中にエラーが発生しました: {ex.Message}");
            }
        }

        /// <summary>
        /// Azure Searchサービス経由で両インデックスの存在チェック
        /// </summary>
        private async Task<(bool existsInMain, bool existsInSentence)> CheckWorkIdInBothIndexesAsync(string workId)
        {
            try
            {
                // 両インデックスをチェック
                return await _azureSearchService.CheckWorkIdInBothIndexesAsync(workId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "両インデックス存在チェックでエラー: workId={WorkId}", workId);
                return (false, false);
            }
        }

        /// <summary>
        /// 特定のインデックスペアでの存在チェック
        /// </summary>
        private async Task<(bool existsInMain, bool existsInSentence)> CheckWorkIdInSpecificIndexesAsync(string workId, string mainIndexName, string sentenceIndexName)
        {
            try
            {
                _logger.LogInformation("特定インデックスペア存在チェック: workId={WorkId}, メイン={Main}, センテンス={Sentence}", workId, mainIndexName, sentenceIndexName);
                
                // メインインデックス存在チェック
                bool existsInMain = await _azureSearchService.CheckWorkIdInIndexAsync(workId, mainIndexName);
                
                // センテンスインデックス存在チェック  
                bool existsInSentence = await _azureSearchService.CheckWorkIdInIndexAsync(workId, sentenceIndexName);
                
                _logger.LogInformation("特定インデックスペア存在チェック結果: workId={WorkId}, メイン存在={MainExists}, センテンス存在={SentenceExists}", 
                    workId, existsInMain, existsInSentence);
                
                return (existsInMain, existsInSentence);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "特定インデックスペア存在チェックでエラー: workId={WorkId}", workId);
                return (false, false);
            }
        }

        /// <summary>
        /// 特定のメインインデックスに登録
        /// </summary>
        private async Task<bool> IndexToSpecificMainIndexAsync(string username, string workId, List<ChunkItem> chunks, WorkIdMetadata metadata, string indexName)
        {
            try
            {
                _logger.LogInformation("特定メインインデックス登録開始: ユーザー={Username}, workId={WorkId}, インデックス={Index}, チャンク数={Count}", 
                    username, workId, indexName, chunks.Count);

                if (chunks == null || chunks.Count == 0)
                {
                    _logger.LogWarning("特定メインインデックス登録スキップ: チャンクが空です");
                    return true;
                }

                var documents = new List<object>();

                foreach (var chunk in chunks)
                {
                    var document = new
                    {
                        id = $"{workId}_{chunk.ChunkNo}_{chunk.PageNo}",
                        workId = workId,
                        content = chunk.Chunk,
                        title = metadata?.Name ?? "未設定",
                        file_name = $"WorkID_{workId}",
                        page_no = chunk.PageNo,
                        created_at = DateTime.UtcNow
                    };
                    documents.Add(document);
                }

                // Azure Searchに登録
                var url = $"{_azureSearchEndpoint}/indexes/{indexName}/docs/index?api-version={_azureSearchApiVersion}";
                var requestBody = new
                {
                    value = documents.Select(doc => new Dictionary<string, object>
                    {
                        ["@search.action"] = "mergeOrUpload",
                        ["id"] = ((dynamic)doc).id,
                        ["workId"] = ((dynamic)doc).workId,
                        ["content"] = ((dynamic)doc).content,
                        ["title"] = ((dynamic)doc).title,
                        ["file_name"] = ((dynamic)doc).file_name,
                        ["page_no"] = ((dynamic)doc).page_no,
                        ["created_at"] = ((dynamic)doc).created_at
                    })
                };

                var jsonContent = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("api-key", _azureSearchKey);

                _logger.LogDebug("特定メインインデックス登録リクエスト送信: URL={Url}", url);

                var response = await _httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("✅ 特定メインインデックス登録成功: インデックス={Index}, チャンク数={Count}", indexName, chunks.Count);
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("❌ 特定メインインデックス登録失敗: インデックス={Index}, ステータス={Status}, エラー={Error}", 
                        indexName, response.StatusCode, errorContent);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "特定メインインデックス登録中にエラー: インデックス={Index}", indexName);
                return false;
            }
        }

        /// <summary>
        /// 特定のセンテンスインデックスに登録
        /// </summary>
        private async Task<bool> IndexToSpecificSentenceIndexAsync(string username, string workId, List<ChunkItem> chunks, WorkIdMetadata metadata, string indexName)
        {
            try
            {
                _logger.LogInformation("特定センテンスインデックス登録開始: ユーザー={Username}, workId={WorkId}, インデックス={Index}, チャンク数={Count}", 
                    username, workId, indexName, chunks.Count);

                if (chunks == null || chunks.Count == 0)
                {
                    _logger.LogWarning("特定センテンスインデックス登録スキップ: チャンクが空です");
                    return true;
                }

                var documents = new List<object>();

                foreach (var chunk in chunks)
                {
                    // ベクトル埋め込み生成
                    var embedding = await GetTextEmbeddingAsync(chunk.Chunk);
                    if (embedding.Count == 0)
                    {
                        _logger.LogWarning("ベクトル埋め込み生成失敗: チャンクNo={ChunkNo}, ページNo={PageNo}, スキップします", chunk.ChunkNo, chunk.PageNo);
                        continue;
                    }

                    var document = new
                    {
                        id = $"{workId}_{chunk.ChunkNo}_{chunk.PageNo}",
                        workId = workId,
                        chunk_text = chunk.Chunk,
                        chunk_embeddings = embedding,
                        document_id = workId,
                        file_name = $"WorkID_{workId}",
                        page_no = chunk.PageNo,
                        chunk_index = chunk.ChunkNo,
                        created_at = DateTime.UtcNow
                    };
                    documents.Add(document);
                }

                if (documents.Count == 0)
                {
                    _logger.LogWarning("特定センテンスインデックス登録スキップ: 有効なドキュメントがありません");
                    return false;
                }

                // Azure Searchに登録
                var url = $"{_azureSearchEndpoint}/indexes/{indexName}/docs/index?api-version={_azureSearchApiVersion}";
                var requestBody = new
                {
                    value = documents.Select(doc => new Dictionary<string, object>
                    {
                        ["@search.action"] = "mergeOrUpload",
                        ["id"] = ((dynamic)doc).id,
                        ["workId"] = ((dynamic)doc).workId,
                        ["chunk_text"] = ((dynamic)doc).chunk_text,
                        ["chunk_embeddings"] = ((dynamic)doc).chunk_embeddings,
                        ["document_id"] = ((dynamic)doc).document_id,
                        ["file_name"] = ((dynamic)doc).file_name,
                        ["page_no"] = ((dynamic)doc).page_no,
                        ["chunk_index"] = ((dynamic)doc).chunk_index,
                        ["created_at"] = ((dynamic)doc).created_at
                    })
                };

                var jsonContent = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("api-key", _azureSearchKey);

                _logger.LogDebug("特定センテンスインデックス登録リクエスト送信: URL={Url}", url);

                var response = await _httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("✅ 特定センテンスインデックス登録成功: インデックス={Index}, ドキュメント数={Count}", indexName, documents.Count);
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("❌ 特定センテンスインデックス登録失敗: インデックス={Index}, ステータス={Status}, エラー={Error}", 
                        indexName, response.StatusCode, errorContent);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "特定センテンスインデックス登録中にエラー: インデックス={Index}", indexName);
                return false;
            }
        }

        /// <summary>
        /// 両インデックスの存在状況に応じて適切に登録（スマート登録）
        /// </summary>
        private async Task<(bool success, string errorMessage)> IndexChunksSmartAsync(string username, string workId, List<ChunkItem> chunks, bool existsInMain, bool existsInSentence)
        {
            try
            {
                _logger.LogInformation("🧠 スマート登録開始: ユーザー={Username}, workId={WorkId}", username, workId);

                // workIdメタデータを取得
                var metadata = await _authorizationService.GetWorkIdMetadataAsync(workId);

                // ユーザーの許可インデックスペアを取得
                var userIndexPairs = await _authorizationService.GetUserIndexPairsAsync(username);
                
                if (userIndexPairs.Count == 0)
                {
                    _logger.LogWarning("🚫 ユーザーの許可インデックスペアが存在しません: ユーザー={Username}", username);
                    return (false, "データベース登録権限が設定されていません（管理者にお問い合わせください）");
                }

                bool overallSuccess = true;
                var allErrorMessages = new List<string>();

                // 各インデックスペアで処理
                foreach (var indexPair in userIndexPairs)
                {
                    _logger.LogInformation("🔄 インデックスペア処理開始: メイン={Main}, センテンス={Sentence}", indexPair.MainIndex, indexPair.SentenceIndex);

                    // 現在のペアでのインデックス存在チェック
                    var (existsInThisMain, existsInThisSentence) = await CheckWorkIdInSpecificIndexesAsync(workId, indexPair.MainIndex, indexPair.SentenceIndex);

                    bool mainSuccess = true;
                    bool sentenceSuccess = true;
                    var pairErrorMessages = new List<string>();

                    // ケース分析
                    if (!existsInThisMain && !existsInThisSentence)
                    {
                        // ケース1: 両インデックスに存在しない → 両方に登録（権限チェック付き）
                        _logger.LogInformation("🔥 [CASE1] 両インデックスに未登録 → 両方に新規登録: {Main}/{Sentence}", indexPair.MainIndex, indexPair.SentenceIndex);
                        
                        // メインインデックス登録権限チェック
                        var (canIndexToMain, mainReason) = await _authorizationService.CanUserIndexToWithReasonAsync(username, indexPair.MainIndex);
                        if (canIndexToMain)
                        {
                            mainSuccess = await IndexToSpecificMainIndexAsync(username, workId, chunks, metadata, indexPair.MainIndex);
                            if (!mainSuccess)
                            {
                                pairErrorMessages.Add($"メインデータベース({indexPair.MainIndex})登録に技術的問題が発生しました");
                            }
                        }
                        else
                        {
                            _logger.LogWarning("🚫 メインインデックス登録権限なし: ユーザー={Username}, インデックス={Index}, 理由={Reason}", username, indexPair.MainIndex, mainReason);
                            mainSuccess = false;
                            pairErrorMessages.Add($"メインデータベース({indexPair.MainIndex})登録エラー: {mainReason}");
                        }
                        
                        // センテンスインデックス登録権限チェック
                        var (canIndexToSentence, sentenceReason) = await _authorizationService.CanUserIndexToWithReasonAsync(username, indexPair.SentenceIndex);
                        if (canIndexToSentence)
                        {
                            sentenceSuccess = await IndexToSpecificSentenceIndexAsync(username, workId, chunks, metadata, indexPair.SentenceIndex);
                            if (!sentenceSuccess)
                            {
                                pairErrorMessages.Add($"検索データベース({indexPair.SentenceIndex})登録に技術的問題が発生しました");
                            }
                        }
                        else
                        {
                            _logger.LogWarning("🚫 センテンスインデックス登録権限なし: ユーザー={Username}, インデックス={Index}, 理由={Reason}", username, indexPair.SentenceIndex, sentenceReason);
                            sentenceSuccess = false;
                            pairErrorMessages.Add($"検索データベース({indexPair.SentenceIndex})登録エラー: {sentenceReason}");
                        }
                    }
                    else if (existsInThisMain && !existsInThisSentence)
                    {
                        // ケース2: メインのみ存在 → センテンスのみに登録（権限チェック付き）
                        _logger.LogInformation("🔸 [CASE2] メインインデックスのみ存在 → センテンスのみに登録: {Sentence}", indexPair.SentenceIndex);
                        
                        var (canIndexToSentence, sentenceReason) = await _authorizationService.CanUserIndexToWithReasonAsync(username, indexPair.SentenceIndex);
                        if (canIndexToSentence)
                        {
                            sentenceSuccess = await IndexToSpecificSentenceIndexAsync(username, workId, chunks, metadata, indexPair.SentenceIndex);
                            if (!sentenceSuccess)
                            {
                                pairErrorMessages.Add($"検索データベース({indexPair.SentenceIndex})登録に技術的問題が発生しました");
                            }
                        }
                        else
                        {
                            _logger.LogWarning("🚫 センテンスインデックス登録権限なし: ユーザー={Username}, インデックス={Index}, 理由={Reason}", username, indexPair.SentenceIndex, sentenceReason);
                            sentenceSuccess = false;
                            pairErrorMessages.Add($"検索データベース({indexPair.SentenceIndex})登録エラー: {sentenceReason}");
                        }
                    }
                    else if (!existsInThisMain && existsInThisSentence)
                    {
                        // ケース3: センテンスのみ存在 → メインのみに登録（権限チェック付き）
                        _logger.LogInformation("🔹 [CASE3] センテンスインデックスのみ存在 → メインのみに登録: {Main}", indexPair.MainIndex);
                        
                        var (canIndexToMain, mainReason) = await _authorizationService.CanUserIndexToWithReasonAsync(username, indexPair.MainIndex);
                        if (canIndexToMain)
                        {
                            mainSuccess = await IndexToSpecificMainIndexAsync(username, workId, chunks, metadata, indexPair.MainIndex);
                            if (!mainSuccess)
                            {
                                pairErrorMessages.Add($"メインデータベース({indexPair.MainIndex})登録に技術的問題が発生しました");
                            }
                        }
                        else
                        {
                            _logger.LogWarning("🚫 メインインデックス登録権限なし: ユーザー={Username}, インデックス={Index}, 理由={Reason}", username, indexPair.MainIndex, mainReason);
                            mainSuccess = false;
                            pairErrorMessages.Add($"メインデータベース({indexPair.MainIndex})登録エラー: {mainReason}");
                        }
                    }
                    else
                    {
                        // ケース4: 両方に存在 → スキップ
                        _logger.LogInformation("✅ [CASE4] 両インデックスに既存 → スキップ: {Main}/{Sentence}", indexPair.MainIndex, indexPair.SentenceIndex);
                    }

                    // ペアの結果を評価
                    bool pairSuccess = mainSuccess && sentenceSuccess;
                    if (!pairSuccess)
                    {
                        overallSuccess = false;
                        allErrorMessages.AddRange(pairErrorMessages);
                    }

                    _logger.LogInformation("🔄 インデックスペア処理完了: メイン={Main}, センテンス={Sentence}, 成功={Success}", 
                        indexPair.MainIndex, indexPair.SentenceIndex, pairSuccess);
                }

                // 最終結果をまとめる
                if (overallSuccess)
                {
                    _logger.LogInformation("✅ スマート登録成功: workId={WorkId}, ユーザー={Username}", workId, username);
                    return (true, "データベース登録が完了しました");
                }
                else if (allErrorMessages.Count > 0)
                {
                    var combinedErrorMessage = string.Join("; ", allErrorMessages);
                    _logger.LogError("❌ workId {WorkId}: スマート登録失敗 - {ErrorMessage}", workId, combinedErrorMessage);
                    return (false, combinedErrorMessage);
                }
                else
                {
                    _logger.LogError("❌ workId {WorkId}: スマート登録失敗 - 原因不明", workId);
                    return (false, "データベース登録中に予期しないエラーが発生しました");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "スマート登録中にエラーが発生しました: workId={WorkId}", workId);
                return (false, "システムエラーが発生しました（管理者にお問い合わせください）");
            }
        }

        /// <summary>
        /// テキストのベクトル埋め込みを生成（Azure OpenAI使用）
        /// </summary>
        private async Task<List<float>> GetTextEmbeddingAsync(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text))
                {
                    _logger.LogWarning("ベクトル埋め込み生成: 空のテキストが入力されました");
                    return new List<float>();
                }

                var url = $"{_azureOpenAIEndpoint}/openai/deployments/{_embeddingModelDeployment}/embeddings?api-version={_azureOpenAIApiVersion}";
                
                var requestBody = new
                {
                    input = text,
                    user = "DataIngestionService"
                };

                var jsonContent = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // リクエストヘッダーを設定（共有HttpClientを使用）
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("api-key", _azureOpenAIKey);

                _logger.LogDebug("🔸 [VECTOR] Embedding API リクエスト送信: URL='{Url}'", url);

                var response = await _httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var embeddingResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);

                    if (embeddingResponse.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == JsonValueKind.Array)
                    {
                        var firstItem = dataArray.EnumerateArray().FirstOrDefault();
                        if (firstItem.TryGetProperty("embedding", out var embeddingArray))
                        {
                            var embedding = new List<float>();
                            foreach (var value in embeddingArray.EnumerateArray())
                            {
                                if (value.ValueKind == JsonValueKind.Number)
                                {
                                    embedding.Add(value.GetSingle());
                                }
                            }

                            _logger.LogDebug("✅ [VECTOR] Embedding生成成功: 次元数={Dimensions}", embedding.Count);
                            return embedding;
                        }
                    }

                    _logger.LogError("❌ [VECTOR] レスポンス解析失敗: {Response}", responseContent);
                    return new List<float>();
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("❌ [VECTOR] Embedding API エラー: {StatusCode}, {Error}", response.StatusCode, errorContent);
                    return new List<float>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [VECTOR] Embedding生成中にエラーが発生");
                return new List<float>();
            }
        }

        /// <summary>
        /// ユーザーが利用可能なworkIdリストを取得（認証付き）
        /// </summary>
        public async Task<List<string>> GetAvailableWorkIdsAsync(string username)
        {
            try
            {
                _logger.LogInformation("認証付き利用可能workIdリスト取得開始: ユーザー={Username}", username);

                // 認証サービスからユーザーの許可されたworkIdリストを取得
                var allowedWorkIds = await _authorizationService.GetAllowedWorkIdsAsync(username);

                _logger.LogInformation("認証付き利用可能workIdリスト取得完了: ユーザー={Username}, 件数={Count}", username, allowedWorkIds.Count);
                return allowedWorkIds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "認証付きworkIdリスト取得でエラー: ユーザー={Username}", username);
                return new List<string>();
            }
        }

        /// <summary>
        /// ユーザーが利用可能なworkIdメタデータリストを取得（認証付き）
        /// </summary>
        public async Task<List<WorkIdMetadata>> GetAvailableWorkIdMetadataAsync(string username)
        {
            try
            {
                _logger.LogInformation("認証付き利用可能workIdメタデータリスト取得開始: ユーザー={Username}", username);

                // 認証サービスからユーザーの許可されたworkIdメタデータリストを取得
                var allowedWorkIdMetadata = await _authorizationService.GetAllowedWorkIdMetadataAsync(username);

                _logger.LogInformation("認証付き利用可能workIdメタデータリスト取得完了: ユーザー={Username}, 件数={Count}", username, allowedWorkIdMetadata.Count);
                return allowedWorkIdMetadata;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "認証付きworkIdメタデータリスト取得でエラー: ユーザー={Username}", username);
                return new List<WorkIdMetadata>();
            }
        }
    }
} 