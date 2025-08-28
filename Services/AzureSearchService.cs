using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AzureRag.Models;
using System.Runtime.CompilerServices;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using System.Linq;
using System.Threading;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AzureRag.Services
{
    public class AzureSearchService : IAzureSearchService
    {
        private SearchClient _searchClient;
        private readonly SearchIndexClient _searchIndexClient;
        private readonly ILogger<AzureSearchService> _logger;
        private readonly string _indexName;
        private readonly HttpClient _httpClient;
        private readonly string _searchEndpoint;
        private readonly string _searchKey;
        private readonly string _apiVersion;
        private string _mainIndexName;
        private string _sentenceIndexName; // ベクトル検索用インデックス
        private readonly string _azureOpenAIEndpoint;
        private readonly string _azureOpenAIKey;
        private readonly string _azureOpenAIApiVersion;
        private readonly string _embeddingModelDeployment;
        private readonly Services.IAuthorizationService _authorizationService;

        public AzureSearchService(
            ILogger<AzureSearchService> logger,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            Services.IAuthorizationService authorizationService)
        {
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient();
            _authorizationService = authorizationService;
            
            // デフォルトはDataIngestionの設定を使用（oec、oec-sentenceインデックス用）
            var dataIngestionConfig = configuration.GetSection("DataIngestion");
            _searchEndpoint = dataIngestionConfig["AzureSearchEndpoint"];
            _searchKey = dataIngestionConfig["AzureSearchKey"];
            _apiVersion = dataIngestionConfig["AzureSearchApiVersion"] ?? "2024-07-01";
            _mainIndexName = dataIngestionConfig["MainIndexName"] ?? "oec"; // 通常検索用インデックス（設定なしの場合はoecをフォールバック）
            _sentenceIndexName = dataIngestionConfig["SentenceIndexName"] ?? "oec-sentence"; // ベクトル検索用インデックス（設定なしの場合はoec-sentenceをフォールバック）
            
            // Azure OpenAI設定
            _azureOpenAIEndpoint = dataIngestionConfig["AzureOpenAIEndpoint"];
            _azureOpenAIKey = dataIngestionConfig["AzureOpenAIKey"];
            _azureOpenAIApiVersion = dataIngestionConfig["AzureOpenAIApiVersion"] ?? "2023-05-15";
            _embeddingModelDeployment = dataIngestionConfig["EmbeddingModelDeployment"] ?? "text-embedding-3-large";

            _logger.LogInformation("AzureSearchService初期化完了 - エンドポイント: {Endpoint}, メインインデックス: {MainIndex}, ベクトルインデックス: {VectorIndex}", 
                _searchEndpoint, _mainIndexName, _sentenceIndexName);
            
            // 検索クライアントを初期化
            _searchClient = new SearchClient(
                new Uri(_searchEndpoint),
                _mainIndexName,
                new AzureKeyCredential(_searchKey));
                
            // インデックスクライアントを初期化
            _searchIndexClient = new SearchIndexClient(
                new Uri(_searchEndpoint),
                new AzureKeyCredential(_searchKey));
        }

        /// <summary>
        /// ユーザー固有のインデックス設定（動的解決版）
        /// </summary>
        public async void SetUserSpecificIndexes(string username)
        {
            if (string.IsNullOrEmpty(username))
            {
                _logger.LogWarning("ユーザー名が空のため、デフォルトインデックスを使用します");
                return;
            }

            try
            {
                // AuthorizationServiceからユーザーのインデックスペアを動的取得
                var authorizationService = GetAuthorizationService();
                if (authorizationService != null)
                {
                    var userIndexPairs = await authorizationService.GetUserIndexPairsAsync(username);
                    
                    if (userIndexPairs?.Any() == true)
                    {
                        // 最初のインデックスペアを使用
                        var firstPair = userIndexPairs.First();
                        _mainIndexName = firstPair.MainIndex;
                        _sentenceIndexName = firstPair.SentenceIndex;
                        
                        _logger.LogInformation("✅ ユーザー '{Username}' のインデックス設定を動的取得: メイン={MainIndex}, センテンス={SentenceIndex}", 
                            username, _mainIndexName, _sentenceIndexName);
                    }
                    else
                    {
                        // フォールバック: 設定ファイルからデフォルト値を取得
                        _logger.LogWarning("⚠️ ユーザー '{Username}' のインデックスペアが見つからないため、設定ファイルのデフォルト値を使用", username);
                        // デフォルト値は既にコンストラクタで設定済み
                    }
                }
                else
                {
                    _logger.LogWarning("AuthorizationServiceが利用できないため、デフォルトインデックスを使用: ユーザー={Username}", username);
                }
                
                // 検索クライアントを再初期化
                _searchClient = new SearchClient(
                    new Uri(_searchEndpoint),
                    _mainIndexName,
                    new AzureKeyCredential(_searchKey));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ユーザーインデックス設定の動的取得でエラー: ユーザー={Username}", username);
                // エラー時はデフォルト値を維持
            }
        }

        /// <summary>
        /// AuthorizationServiceの取得（DI経由）
        /// </summary>
        private Services.IAuthorizationService GetAuthorizationService()
        {
            return _authorizationService;
        }

        /// <summary>
        /// クエリに基づいてドキュメントを検索します
        /// </summary>
        /// <param name="query">検索クエリ</param>
        /// <returns>検索結果のリスト</returns>
        public async Task<List<AzureRag.Models.DocumentSearchResult>> SearchDocumentsAsync(string query)
        {
            try
            {
                _logger.LogInformation($"検索クエリ: {query}");

                // 検索オプションを設定
                SearchOptions options = new SearchOptions
                {
                    IncludeTotalCount = true,
                    Size = 10, // 上位10件の結果を取得（以前の5件から増加）
                    QueryType = SearchQueryType.Simple,
                    HighlightPreTag = "", // ハイライトタグなし
                    HighlightPostTag = ""
                    // 注: QueryLanguage プロパティは古いバージョンでは使用できない場合があります
                    // また、SetMaximumTextLength メソッドもSDKバージョンによっては利用できない場合があります
                };
                
                // 以下の行はLSPエラーのためコメントアウトしました
                // テキスト切り詰め問題の完全な解決は、小さなチャンクへの分割とインデックスやJSON設定の最適化が必要です
                // options.SetMaximumTextLength(100000); // 10万文字まで許可

                // タイトルとコンテンツの両方を検索対象に
                options.Select.Add("id");
                options.Select.Add("title");
                options.Select.Add("content");

                // 検索を実行
                SearchResults<SearchDocument> response = await _searchClient.SearchAsync<SearchDocument>(query, options);
                
                // 結果を取得
                List<DocumentSearchResult> results = new List<DocumentSearchResult>();
                // C# 8.0以上を使用している場合は await foreach が使えます
                // しかし、下位互換性のために従来の方法でイテレーションします
                var searchResultsEnumerable = response.GetResultsAsync();
                var searchResults = new List<SearchResult<SearchDocument>>();
                
                await foreach (var item in searchResultsEnumerable)
                {
                    searchResults.Add(item);
                }
                
                foreach (SearchResult<SearchDocument> result in searchResults)
                {
                    results.Add(new DocumentSearchResult
                    {
                        Id = result.Document["id"]?.ToString(),
                        Title = result.Document["title"]?.ToString(),
                        Content = result.Document["content"]?.ToString(),
                        Score = result.Score ?? 0
                    });
                }

                _logger.LogInformation($"検索結果数: {results.Count}");
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ドキュメント検索中にエラーが発生しました");
                throw;
            }
        }

        /// <summary>
        /// チャンクドキュメントをインデックスに追加します（workId、ページ番号、チャンク番号付き）
        /// </summary>
        public async Task<bool> IndexChunkDocumentAsync(string id, string workId, string title, string content, int pageNumber, int chunkNumber, string description = "")
        {
            try
            {
                _logger.LogInformation($"チャンクドキュメントをインデックスに追加: {id}, workId={workId}, page={pageNumber}, chunk={chunkNumber}");

                // インデックスが存在することを確認
                bool indexExists = await EnsureIndexExistsAsync();
                if (!indexExists)
                {
                    _logger.LogError($"インデックスの作成に失敗しました: {_mainIndexName}");
                    return false;
                }
                
                // チャンク用のドキュメントを作成（Azure Searchの実際のフィールド名に合わせる）
                var document = new SearchDocument
                {
                    ["id"] = id,
                    ["workId"] = workId,
                    ["title"] = title,
                    ["content"] = content,
                    ["page_no"] = pageNumber,  // pageNumber → page_no
                    ["file_name"] = $"WorkID: {workId}",  // documentName → file_name
                    ["created_at"] = DateTime.UtcNow  // last_updated → created_at
                };

                // コンテンツの概要をログに記録
                string contentPreview = content != null && content.Length > 100 ? 
                    content.Substring(0, 100) + "..." : 
                    content ?? "";
                    
                _logger.LogInformation($"チャンクドキュメント情報: ID={id}, workId={workId}, ページ={pageNumber + 1}, チャンク={chunkNumber}, コンテンツ長={content?.Length ?? 0}");

                // インデックスに追加
                var batch = IndexDocumentsBatch.Upload(new[] { document });
                IndexDocumentsResult result = await _searchClient.IndexDocumentsAsync(batch);
                
                // 成功したかを確認
                bool success = !result.Results.Any(r => r.Succeeded == false);
                if (success)
                {
                    _logger.LogInformation($"チャンクドキュメントをインデックスに追加しました: {id}");
                }
                else
                {
                    foreach (var itemResult in result.Results)
                    {
                        if (!itemResult.Succeeded)
                        {
                            _logger.LogWarning($"チャンクドキュメントインデックス化失敗: キー={itemResult.Key}, エラー={itemResult.ErrorMessage}");
                        }
                    }
                }
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"チャンクドキュメントのインデックス追加中にエラーが発生: {id}");
                return false;
            }
        }

        /// <summary>
        /// ドキュメントをインデックスに追加します
        /// </summary>
        public async Task<bool> IndexDocumentAsync(string id, string workId, string title, string content, string description = "")
        {
            try
            {
                // ドキュメントIDをより詳細にログ記録
                _logger.LogInformation($"ドキュメントをインデックスに追加: {id}, {title}");
                
                // IDにページ情報が含まれているか確認
                string pageInfo = id.Contains("_page") ? 
                    $"[ページ情報検出: {id.Split("_page")[1].Split("_")[0]}]" : 
                    "[ページ情報なし]";
                
                _logger.LogInformation($"ドキュメントID詳細: {id} {pageInfo}");

                // インデックスが存在することを確認
                bool indexExists = await EnsureIndexExistsAsync();
                if (!indexExists)
                {
                    _logger.LogError($"インデックスの作成に失敗しました: {_mainIndexName}");
                    return false;
                }
                
                // 既存のインデックス定義を取得
                _logger.LogInformation("現在のインデックス定義を確認します");
                SearchIndex indexDef = null;
                try 
                {
                    indexDef = await _searchIndexClient.GetIndexAsync(_mainIndexName);
                    _logger.LogInformation($"インデックス定義を取得: {_mainIndexName}, フィールド数: {indexDef.Fields.Count}");
                    
                    // 現在のフィールドを表示
                    foreach (var field in indexDef.Fields)
                    {
                        _logger.LogInformation($"フィールド: {field.Name}, タイプ: {field.Type}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"インデックス定義の取得に失敗: {ex.Message}");
                }
                
                // 同じIDのドキュメントが既に存在するか確認
                SearchOptions options = new SearchOptions
                {
                    Filter = $"id eq '{id}'",
                    Size = 1
                };
                var existingResults = await _searchClient.SearchAsync<SearchDocument>("*", options);
                bool documentExists = existingResults.Value.TotalCount > 0;
                
                if (documentExists)
                {
                    _logger.LogWarning($"同じIDのドキュメントが既に存在します: {id}、上書きします");
                }
                
                // ドキュメントを作成（Azure Searchの実際のフィールド名に合わせる）
                var document = new SearchDocument
                {
                    ["id"] = id,
                    ["workId"] = workId,
                    ["title"] = title,
                    ["content"] = content,
                    ["file_name"] = $"WorkID: {workId}",  // file_nameフィールドを追加
                    ["created_at"] = DateTime.UtcNow  // last_updated → created_at
                };

                // コンテンツの概要をログに記録（最初の100文字）
                string contentPreview = content != null && content.Length > 100 ? 
                    content.Substring(0, 100) + "..." : 
                    content ?? "";
                    
                _logger.LogInformation($"インデックス追加するドキュメント情報: ID={id}, タイトル={title}, コンテンツ長={content?.Length ?? 0}");
                _logger.LogInformation($"コンテンツ概要: {contentPreview}");

                // 一度に1つのドキュメントをインデックスに追加
                var batch = IndexDocumentsBatch.Upload(new[] { document });
                IndexDocumentsResult result = await _searchClient.IndexDocumentsAsync(batch);
                
                // 成功したかを確認
                bool success = !result.Results.Any(r => r.Succeeded == false);
                if (success)
                {
                    _logger.LogInformation($"ドキュメントをインデックスに追加しました: {id}");
                }
                else
                {
                    foreach (var itemResult in result.Results)
                    {
                        if (!itemResult.Succeeded)
                        {
                            _logger.LogWarning($"ドキュメントインデックス化失敗: キー={itemResult.Key}, エラー={itemResult.ErrorMessage}");
                        }
                    }
                    _logger.LogWarning($"ドキュメントのインデックス追加が失敗しました: {id}");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"ドキュメントのインデックス追加中にエラーが発生: {id}");
                return false;
            }
        }

        /// <summary>
        /// 指定されたIDに関連するドキュメントをインデックスから削除します
        /// </summary>
        public async Task<bool> DeleteDocumentsAsync(List<string> workIds)
        {
            try
            {
                _logger.LogInformation($"インデックスからドキュメントを削除: workIds=[{string.Join(", ", workIds)}]");
                
                List<string> allDocIds = new List<string>();
                
                // 各workIdについて削除対象のドキュメントを検索
                foreach (string workId in workIds)
                {
                    SearchOptions options = new SearchOptions
                    {
                        Filter = $"workId eq '{workId}'"
                    };
                    
                    // 削除対象のドキュメントを検索
                    SearchResults<SearchDocument> response = await _searchClient.SearchAsync<SearchDocument>("*", options);
                    
                    // 非同期イテレーション
                    var resultsEnumerable = response.GetResultsAsync();
                    var results = new List<SearchResult<SearchDocument>>();
                    
                    await foreach (var item in resultsEnumerable)
                    {
                        results.Add(item);
                    }
                    
                    foreach (SearchResult<SearchDocument> searchResult in results)
                    {
                        string id = searchResult.Document["id"].ToString();
                        allDocIds.Add(id);
                    }
                }

                if (allDocIds.Count == 0)
                {
                    _logger.LogWarning($"削除対象のドキュメントが見つかりません: workIds=[{string.Join(", ", workIds)}]");
                    return false;
                }

                // ドキュメントを削除
                var batch = IndexDocumentsBatch.Delete("id", allDocIds);
                IndexDocumentsResult result = await _searchClient.IndexDocumentsAsync(batch);
                
                // 成功したかを確認
                bool success = !result.Results.Any(r => r.Succeeded == false);
                if (success)
                {
                    _logger.LogInformation($"インデックスからドキュメントを削除しました: workIds=[{string.Join(", ", workIds)}], 削除数: {allDocIds.Count}");
                }
                else
                {
                    _logger.LogWarning($"ドキュメントの削除が失敗しました: workIds=[{string.Join(", ", workIds)}]");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"ドキュメント削除中にエラーが発生: workIds=[{string.Join(", ", workIds)}]");
                return false;
            }
        }

        /// <summary>
        /// インデックスが存在するか確認します。存在しない場合は作成します。
        /// </summary>
        public async Task<bool> EnsureIndexExistsAsync()
        {
            try
            {
                // インデックスが存在するか確認
                var indexes = _searchIndexClient.GetIndexes();
                bool exists = false;
                
                foreach (var index in indexes)
                {
                    if (index.Name == _mainIndexName)
                    {
                        exists = true;
                        _logger.LogInformation($"既存のインデックスが見つかりました: {_mainIndexName}");
                        break;
                    }
                }
                
                if (!exists)
                {
                    _logger.LogInformation($"インデックスが存在しないため、作成します: {_mainIndexName}");
                    
                    // フィールド定義
                    var fields = new List<SearchField>
                    {
                        new SearchField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true, IsSortable = true },
                        new SearchField("workId", SearchFieldDataType.String) { IsFilterable = true, IsSortable = true },
                        new SearchField("title", SearchFieldDataType.String) { IsSearchable = true, IsFilterable = true, IsSortable = true },
                        new SearchField("content", SearchFieldDataType.String) { IsSearchable = true, IsFilterable = false },
                        new SearchField("pageNumber", SearchFieldDataType.Int32) { IsFilterable = true, IsSortable = true },
                        new SearchField("chunkNumber", SearchFieldDataType.Int32) { IsFilterable = true, IsSortable = true },
                        new SearchField("category", SearchFieldDataType.String) { IsFilterable = true, IsSortable = true },
                        new SearchField("documentName", SearchFieldDataType.String) { IsSearchable = true, IsFilterable = true },
                        new SearchField("last_updated", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true }
                    };
                    
                    // インデックスを作成
                    var definition = new SearchIndex(_mainIndexName, fields);
                    await _searchIndexClient.CreateOrUpdateIndexAsync(definition);
                    
                    _logger.LogInformation($"インデックスを作成しました: {_mainIndexName}");
                    return true;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"インデックス確認/作成中にエラーが発生: {_mainIndexName}");
                return false;
            }
        }
        
        /// <summary>
        /// 既存のインデックスを削除します
        /// </summary>
        public async Task<bool> DeleteIndexAsync()
        {
            try
            {
                _logger.LogInformation($"インデックスを削除します: {_mainIndexName}");
                
                // インデックスが存在するか確認
                var indexes = _searchIndexClient.GetIndexes();
                bool exists = false;
                
                foreach (var index in indexes)
                {
                    if (index.Name == _mainIndexName)
                    {
                        exists = true;
                        break;
                    }
                }
                
                if (!exists)
                {
                    _logger.LogInformation($"インデックスが存在しないため、削除は不要です: {_mainIndexName}");
                    return true;
                }
                
                // インデックスを削除
                await _searchIndexClient.DeleteIndexAsync(_mainIndexName);
                _logger.LogInformation($"インデックスを削除しました: {_mainIndexName}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"インデックス削除中にエラーが発生: {_mainIndexName}");
                return false;
            }
        }
        
        /// <summary>
        /// インデックスを再作成します
        /// </summary>
        public async Task<bool> RecreateIndexAsync()
        {
            try
            {
                // 既存のインデックスを削除
                await DeleteIndexAsync();
                
                // 新しいインデックスを作成
                _logger.LogInformation($"インデックスを再作成します: {_mainIndexName}");
                
                // フィールド定義
                var fields = new List<SearchField>
                {
                    new SearchField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true, IsSortable = true },
                    new SearchField("workId", SearchFieldDataType.String) { IsFilterable = true, IsSortable = true },
                    new SearchField("title", SearchFieldDataType.String) { IsSearchable = true, IsFilterable = true, IsSortable = true },
                    new SearchField("content", SearchFieldDataType.String) { IsSearchable = true, IsFilterable = false },
                    new SearchField("pageNumber", SearchFieldDataType.Int32) { IsFilterable = true, IsSortable = true },
                    new SearchField("chunkNumber", SearchFieldDataType.Int32) { IsFilterable = true, IsSortable = true },
                    new SearchField("category", SearchFieldDataType.String) { IsFilterable = true, IsSortable = true },
                    new SearchField("documentName", SearchFieldDataType.String) { IsSearchable = true, IsFilterable = true },
                    new SearchField("last_updated", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true }
                };
                
                // インデックスを作成
                var definition = new SearchIndex(_mainIndexName, fields);
                await _searchIndexClient.CreateOrUpdateIndexAsync(definition);
                
                _logger.LogInformation($"インデックスを再作成しました: {_mainIndexName}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"インデックス再作成中にエラーが発生: {_mainIndexName}");
                return false;
            }
        }

        /// <summary>
        /// oec-sentenceインデックス（ベクトル検索用）が存在するか確認し、存在しない場合は作成します
        /// </summary>
        public async Task<bool> EnsureSentenceIndexExistsAsync()
        {
            try
            {
                // インデックスが存在するか確認
                var indexes = _searchIndexClient.GetIndexes();
                bool exists = false;
                
                foreach (var index in indexes)
                {
                    if (index.Name == _sentenceIndexName)
                    {
                        exists = true;
                        _logger.LogInformation($"既存のoec-sentenceインデックスが見つかりました: {_sentenceIndexName}");
                        break;
                    }
                }
                
                if (!exists)
                {
                    _logger.LogInformation($"oec-sentenceインデックスが存在しないため、作成します: {_sentenceIndexName}");
                    return await CreateSentenceIndexAsync();
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"oec-sentenceインデックス確認/作成中にエラーが発生: {_sentenceIndexName}");
                return false;
            }
        }

        /// <summary>
        /// oec-sentenceインデックス（ベクトル検索用）を作成します
        /// </summary>
        private async Task<bool> CreateSentenceIndexAsync()
        {
            try
            {
                // ベクトル検索プロファイルを定義
                var vectorSearchProfile = new VectorSearchProfile("default-vector-profile", "default-algorithm");
                var vectorSearchAlgorithm = new HnswAlgorithmConfiguration("default-algorithm");

                // ベクトル検索設定を作成
                var vectorSearch = new VectorSearch
                {
                    Profiles = { vectorSearchProfile },
                    Algorithms = { vectorSearchAlgorithm }
                };

                // フィールド定義（oec-sentence-index.jsonに合わせて）
                var fields = new List<SearchField>
                {
                    new SearchField("id", SearchFieldDataType.String) 
                    { 
                        IsKey = true, 
                        IsFilterable = false, 
                        IsSortable = false, 
                        IsFacetable = false,
                        IsSearchable = true,
                        IsHidden = false
                    },
                    new SearchField("workId", SearchFieldDataType.String) 
                    { 
                        IsFilterable = true, 
                        IsSortable = false, 
                        IsFacetable = false,
                        IsSearchable = true,
                        IsHidden = false
                    },
                    new SearchField("chunk_text", SearchFieldDataType.String) 
                    { 
                        IsSearchable = true, 
                        IsFilterable = false,
                        IsSortable = false, 
                        IsFacetable = false,
                        IsHidden = false
                    },
                    new SearchField("chunk_embeddings", SearchFieldDataType.Collection(SearchFieldDataType.Single)) 
                    { 
                        IsSearchable = true,
                        IsFilterable = false,
                        IsSortable = false,
                        IsFacetable = false,
                        IsHidden = true, // ベクトルフィールドは通常非表示
                        VectorSearchDimensions = 3072, // text-embedding-3-large用
                        VectorSearchProfileName = "default-vector-profile"
                    },
                    new SearchField("document_id", SearchFieldDataType.String) 
                    { 
                        IsSearchable = false,
                        IsFilterable = true,
                        IsSortable = false,
                        IsFacetable = false,
                        IsHidden = false
                    },
                    new SearchField("file_name", SearchFieldDataType.String) 
                    { 
                        IsSearchable = true,
                        IsFilterable = true,
                        IsSortable = false,
                        IsFacetable = false,
                        IsHidden = false
                    },
                    new SearchField("page_no", SearchFieldDataType.Int32) 
                    { 
                        IsSearchable = false,
                        IsFilterable = true,
                        IsSortable = false,
                        IsFacetable = false,
                        IsHidden = false
                    },
                    new SearchField("chunk_index", SearchFieldDataType.Int32) 
                    { 
                        IsSearchable = false,
                        IsFilterable = true,
                        IsSortable = false,
                        IsFacetable = false,
                        IsHidden = false
                    },
                    new SearchField("created_at", SearchFieldDataType.DateTimeOffset) 
                    { 
                        IsFilterable = true, 
                        IsSortable = true,
                        IsFacetable = false,
                        IsSearchable = false,
                        IsHidden = false
                    }
                };
                
                // インデックス定義を作成
                var definition = new SearchIndex(_sentenceIndexName, fields)
                {
                    VectorSearch = vectorSearch
                };
                
                await _searchIndexClient.CreateOrUpdateIndexAsync(definition);
                
                _logger.LogInformation($"oec-sentenceインデックスを作成しました: {_sentenceIndexName}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"oec-sentenceインデックス作成中にエラーが発生: {_sentenceIndexName}");
                return false;
            }
        }

        /// <summary>
        /// oec-sentenceインデックス（ベクトル検索用）を削除します
        /// </summary>
        public async Task<bool> DeleteSentenceIndexAsync()
        {
            try
            {
                _logger.LogInformation($"oec-sentenceインデックスを削除します: {_sentenceIndexName}");
                
                // インデックスが存在するか確認
                var indexes = _searchIndexClient.GetIndexes();
                bool exists = false;
                
                foreach (var index in indexes)
                {
                    if (index.Name == _sentenceIndexName)
                    {
                        exists = true;
                        break;
                    }
                }
                
                if (!exists)
                {
                    _logger.LogInformation($"oec-sentenceインデックスが存在しないため、削除は不要です: {_sentenceIndexName}");
                    return true;
                }
                
                // インデックスを削除
                await _searchIndexClient.DeleteIndexAsync(_sentenceIndexName);
                _logger.LogInformation($"oec-sentenceインデックスを削除しました: {_sentenceIndexName}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"oec-sentenceインデックス削除中にエラーが発生: {_sentenceIndexName}");
                return false;
            }
        }

        /// <summary>
        /// oec-sentenceインデックス（ベクトル検索用）を再作成します
        /// </summary>
        public async Task<bool> RecreateSentenceIndexAsync()
        {
            try
            {
                _logger.LogInformation($"oec-sentenceインデックスを再作成します: {_sentenceIndexName}");
                
                // 既存のインデックスを削除
                await DeleteSentenceIndexAsync();
                
                // 新しいインデックスを作成
                return await CreateSentenceIndexAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"oec-sentenceインデックス再作成中にエラーが発生: {_sentenceIndexName}");
                return false;
            }
        }

        /// <summary>
        /// Azure Search APIを使用してハイブリッドセマンティック検索
        /// </summary>
        public async Task<List<SearchResult>> SearchDocumentsAsync(string query, List<string> workIds = null, int top = 10)
        {
            try
            {
                _logger.LogInformation("ハイブリッドセマンティック検索開始: クエリ={Query}, workIds={WorkIds}, 件数={Top}", 
                    query, workIds != null ? string.Join(",", workIds) : "全て", top);

                // oecインデックスはベクトルフィールドがないため、シンプル検索を実行
                _logger.LogInformation("oecインデックスに対してシンプル検索を実行");
                return await SimpleSearchAsync(query, top);


            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ハイブリッドセマンティック検索でエラーが発生");
                return new List<SearchResult>();
            }
        }

        /// <summary>
        /// ハイブリッド検索（ベクトル検索 + キーワード検索）
        /// </summary>
        public async Task<List<SearchResult>> SemanticSearchAsync(string query, List<string> workIds = null, int top = 10)
        {
            _logger.LogInformation("🚀 [HYBRID] ハイブリッド検索開始: クエリ='{Query}', WorkIDs={WorkIdCount}件, Top={Top}", 
                query, workIds?.Count ?? 0, top);

            try
            {
                // ベクトル検索が利用可能かチェック
                _logger.LogInformation("🔸 [HYBRID] Step1: Embedding生成チェック");
                var queryVector = await GetQueryEmbeddingAsync(query);
                
                if (queryVector != null && queryVector.Count > 0)
                {
                    _logger.LogInformation("✅ [HYBRID] Step2: ベクトル検索とキーワード検索のハイブリッド検索を実行");
                    
                    // ハイブリッド検索リクエストを構築（既存の実装を使用）
                    var hybridSearchRequest = BuildHybridSearchRequest(query, queryVector, workIds, top);
                    _logger.LogInformation("🔸 [HYBRID] Step3: ハイブリッド検索リクエスト構築完了");
                    
                    var results = await ExecuteSearchRequest(hybridSearchRequest, useVectorIndex: true);
                    _logger.LogInformation("✅ [HYBRID] ハイブリッド検索完了: 結果={Count}件", results.Count);
                    return results;
                }
                else
                {
                    _logger.LogWarning("⚠️ [HYBRID] Step2: ベクトル化失敗 → キーワード検索フォールバック");
                    
                    // ベクトル検索が利用できない場合はキーワード検索にフォールバック
                    var keywordSearchRequest = new
                    {
                        search = query,
                        searchMode = "any",
                        queryType = "simple",
                        filter = BuildWorkIdFilter(workIds),
                        top = top,
                        highlight = "chunk_text",
                        highlightPreTag = "<mark>",
                        highlightPostTag = "</mark>",
                        select = "id,workId,chunk_text,page_no,file_name"
                    };
                    
                    _logger.LogInformation("🔸 [HYBRID] Step3: キーワード検索実行");
                    var results = await ExecuteSearchRequest(keywordSearchRequest, useVectorIndex: false);
                    _logger.LogInformation("✅ [HYBRID] キーワード検索完了: 結果={Count}件", results.Count);
                    return results;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [HYBRID] ハイブリッド検索でエラーが発生: {Message}", ex.Message);
                
                // エラー時もキーワード検索にフォールバック
                try
                {
                    _logger.LogInformation("🔄 [HYBRID] 緊急フォールバック: KeywordSearchAsync実行");
                    var results = await KeywordSearchAsync(new List<string> { query }, workIds, top);
                    _logger.LogInformation("✅ [HYBRID] 緊急フォールバック完了: 結果={Count}件", results.Count);
                    return results;
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogError(fallbackEx, "❌ [HYBRID] フォールバック検索でもエラーが発生: {Message}", fallbackEx.Message);
                    return new List<SearchResult>();
                }
            }
        }

        /// <summary>
        /// キーワード検索（フルテキスト検索）
        /// </summary>
        public async Task<List<SearchResult>> KeywordSearchAsync(List<string> keywords, List<string> workIds = null, int top = 10)
        {
            try
            {
                _logger.LogInformation("キーワード検索開始: キーワード={Keywords}", string.Join(",", keywords));

                var searchQuery = string.Join(" ", keywords);
                
                var searchRequest = new
                {
                    search = searchQuery,
                    searchMode = "any", // いずれかのキーワードを含む（より多くの結果を取得）
                    filter = BuildWorkIdFilter(workIds),
                    top = top,
                    select = "id,workId,title,content,page_no,file_name"  // 最小限のフィールドに絞る
                };

                return await ExecuteSearchRequest(searchRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "キーワード検索でエラーが発生");
                return new List<SearchResult>();
            }
        }

        /// <summary>
        /// シンプルな検索テスト（デバッグ用）
        /// </summary>
        public async Task<List<SearchResult>> SimpleSearchAsync(string query, int top = 5)
        {
            try
            {
                _logger.LogInformation($"シンプル検索テスト開始: {query}");

                var searchRequest = new
                {
                    search = query,
                    top = top
                };

                return await ExecuteSearchRequest(searchRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "シンプル検索テストでエラーが発生");
                return new List<SearchResult>();
            }
        }

        /// <summary>
        /// Azure Searchインデックスの接続テスト
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                _logger.LogInformation("Azure Search接続テスト開始");

                var url = $"{_searchEndpoint}/indexes/{_mainIndexName}?api-version={_apiVersion}";
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("api-key", _searchKey);

                var response = await _httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Azure Search接続テスト成功");
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Azure Search接続テスト失敗: {StatusCode} - {Error}", response.StatusCode, errorContent);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Azure Search接続テストでエラーが発生");
                return false;
            }
        }

        /// <summary>
        /// 指定されたworkIdがAzure Searchインデックスに既に存在するかチェック
        /// </summary>
        public async Task<bool> IsWorkIdIndexedAsync(string workId)
        {
            try
            {
                _logger.LogInformation($"workId存在チェック: {workId}");
                
                var searchRequest = new
                {
                    search = "*",
                    filter = $"workId eq '{workId}'",
                    top = 1,
                    count = true
                };

                var url = $"{_searchEndpoint}/indexes/{_mainIndexName}/docs/search?api-version={_apiVersion}";
                var jsonContent = JsonSerializer.Serialize(searchRequest, new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
                });

                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("api-key", _searchKey);

                var response = await _httpClient.PostAsync(url, content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    
                    if (result.TryGetProperty("@odata.count", out var countElement))
                    {
                        int count = countElement.GetInt32();
                        bool exists = count > 0;
                        
                        _logger.LogInformation($"workId存在チェック結果: {workId} = {exists} (ドキュメント数: {count})");
                        return exists;
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("workId存在チェック失敗: {StatusCode} - {Error}", response.StatusCode, errorContent);
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"workId存在チェック中にエラーが発生: {workId}");
                return false;
            }
        }

        /// <summary>
        /// 指定されたworkIdがoec-sentenceインデックスに既に存在するかチェック
        /// </summary>
        public async Task<bool> IsWorkIdIndexedInSentenceAsync(string workId)
        {
            try
            {
                _logger.LogInformation($"oec-sentenceインデックスでworkId存在チェック: {workId}");

                var searchRequest = new
                {
                    search = "*",
                    filter = $"workId eq '{workId}'",
                    top = 1,
                    count = true
                };

                var url = $"{_searchEndpoint}/indexes/{_sentenceIndexName}/docs/search?api-version={_apiVersion}";
                var jsonContent = JsonSerializer.Serialize(searchRequest, new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });
                
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("api-key", _searchKey);

                var response = await _httpClient.PostAsync(url, content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseText = await response.Content.ReadAsStringAsync();
                    var responseJson = JsonSerializer.Deserialize<JsonElement>(responseText);
                    
                    if (responseJson.TryGetProperty("@odata.count", out var countElement))
                    {
                        int count = countElement.GetInt32();
                        bool exists = count > 0;
                        
                        _logger.LogInformation($"oec-sentenceインデックス存在チェック結果: workId={workId}, 存在={exists}, 件数={count}");
                        return exists;
                    }
                }
                else
                {
                    var errorText = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"oec-sentenceインデックス存在チェック失敗: workId={workId}, ステータス={response.StatusCode}, エラー={errorText}");
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"oec-sentenceインデックス存在チェック中にエラー: workId={workId}");
                return false;
            }
        }

        /// <summary>
        /// 両インデックスでworkIdの存在確認
        /// </summary>
        public async Task<(bool existsInMain, bool existsInSentence)> CheckWorkIdInBothIndexesAsync(string workId)
        {
            try
            {
                _logger.LogInformation("両インデックス存在チェック開始: workId={WorkId}, メイン={MainIndex}, センテンス={SentenceIndex}", 
                    workId, _mainIndexName, _sentenceIndexName);

                // 現在のユーザー設定に基づくインデックス名を使用
                bool existsInMain = await CheckWorkIdInIndexAsync(workId, _mainIndexName);
                bool existsInSentence = await CheckWorkIdInIndexAsync(workId, _sentenceIndexName);

                _logger.LogInformation("両インデックス存在チェック結果: workId={WorkId}, メイン存在={MainExists}, センテンス存在={SentenceExists}", 
                    workId, existsInMain, existsInSentence);

                return (existsInMain, existsInSentence);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "両インデックス存在チェック中にエラーが発生しました: workId={WorkId}", workId);
                return (false, false);
            }
        }

        /// <summary>
        /// 特定のインデックスでworkIdの存在確認
        /// </summary>
        public async Task<bool> CheckWorkIdInIndexAsync(string workId, string indexName)
        {
            try
            {
                _logger.LogInformation("特定インデックス存在チェック開始: workId={WorkId}, インデックス={Index}", workId, indexName);

                var searchUrl = $"{_searchEndpoint}/indexes/{indexName}/docs/search?api-version={_apiVersion}";
                
                var requestBody = new
                {
                    count = true,
                    top = 1,
                    filter = $"workId eq '{workId}'"
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("api-key", _searchKey);

                var response = await _httpClient.PostAsync(searchUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    using var document = JsonDocument.Parse(responseContent);
                    
                    if (document.RootElement.TryGetProperty("@odata.count", out var countElement))
                    {
                        int count = countElement.GetInt32();
                        bool exists = count > 0;
                        
                        _logger.LogInformation("特定インデックス存在チェック結果: workId={WorkId}, インデックス={Index}, 存在={Exists}, 件数={Count}", 
                            workId, indexName, exists, count);
                        
                        return exists;
                    }
                    else
                    {
                        _logger.LogWarning("特定インデックス存在チェック: カウント情報が取得できませんでした - workId={WorkId}, インデックス={Index}", workId, indexName);
                        return false;
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("特定インデックス存在チェック失敗: workId={WorkId}, インデックス={Index}, ステータス={Status}, エラー={Error}", 
                        workId, indexName, response.StatusCode, errorContent);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "特定インデックス存在チェック中にエラーが発生しました: workId={WorkId}, インデックス={Index}", workId, indexName);
                return false;
            }
        }

        /// <summary>
        /// 複数のworkIdがAzure Searchインデックスに既に存在するかチェック
        /// </summary>
        public async Task<List<string>> GetExistingWorkIdsAsync(List<string> workIds)
        {
            var existingWorkIds = new List<string>();
            
            try
            {
                _logger.LogInformation($"複数workId存在チェック開始: {workIds.Count}件");
                
                foreach (var workId in workIds)
                {
                    bool exists = await IsWorkIdIndexedAsync(workId);
                    if (exists)
                    {
                        existingWorkIds.Add(workId);
                    }
                }
                
                _logger.LogInformation($"複数workId存在チェック完了: {existingWorkIds.Count}/{workIds.Count}件が既存");
                return existingWorkIds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "複数workId存在チェック中にエラーが発生");
                return existingWorkIds;
            }
        }

        /// <summary>
        /// 指定されたworkIdのドキュメント数を取得
        /// </summary>
        public async Task<Dictionary<string, int>> GetDocumentCountsByWorkIdAsync(List<string> workIds)
        {
            var counts = new Dictionary<string, int>();
            
            try
            {
                foreach (var workId in workIds)
                {
                    var searchRequest = new
                    {
                        search = "*",
                        filter = $"workId eq '{workId}'",
                        top = 0,
                        count = true
                    };

                    var url = $"{_searchEndpoint}/indexes/{_mainIndexName}/docs/search?api-version={_apiVersion}";
                    var jsonContent = JsonSerializer.Serialize(searchRequest, new JsonSerializerOptions 
                    { 
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
                    });

                    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                    _httpClient.DefaultRequestHeaders.Clear();
                    _httpClient.DefaultRequestHeaders.Add("api-key", _searchKey);

                    var response = await _httpClient.PostAsync(url, content);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
                        
                        if (result.TryGetProperty("@odata.count", out var countElement))
                        {
                            counts[workId] = countElement.GetInt32();
                        }
                    }
                    else
                    {
                        counts[workId] = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ドキュメント数取得でエラーが発生");
            }

            return counts;
        }

        /// <summary>
        /// クエリをベクトル化（Azure OpenAI Embedding API使用）
        /// </summary>
        private async Task<List<float>> GetQueryEmbeddingAsync(string query)
        {
            _logger.LogInformation("🔸 [VECTOR] Embedding API呼び出し開始: クエリ='{Query}', エンドポイント='{Endpoint}', モデル='{Model}'", 
                query, _azureOpenAIEndpoint, _embeddingModelDeployment);
            
            try
            {
                var url = $"{_azureOpenAIEndpoint}/openai/deployments/{_embeddingModelDeployment}/embeddings?api-version={_azureOpenAIApiVersion}";
                
                var requestBody = new
                {
                    input = query,
                    encoding_format = "float"
                };

                var jsonContent = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("api-key", _azureOpenAIKey);

                _logger.LogInformation("🔸 [VECTOR] Embedding API リクエスト送信: URL='{Url}'", url);
                var response = await _httpClient.PostAsync(url, content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var embeddingResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    
                    if (embeddingResponse.TryGetProperty("data", out var dataArray) && 
                        dataArray.GetArrayLength() > 0)
                    {
                        var firstEmbedding = dataArray[0];
                        if (firstEmbedding.TryGetProperty("embedding", out var embeddingArray))
                        {
                            var vector = new List<float>();
                            foreach (var element in embeddingArray.EnumerateArray())
                            {
                                vector.Add(element.GetSingle());
                            }
                            _logger.LogInformation("✅ [VECTOR] Embedding生成成功: 次元数={Dimensions}", vector.Count);
                            return vector;
                        }
                    }
                    _logger.LogWarning("⚠️ [VECTOR] Embedding レスポンス形式エラー: data配列またはembedding配列が見つからない");
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("❌ [VECTOR] Embedding API エラー: StatusCode={StatusCode}, Error='{Error}'", 
                        response.StatusCode, errorContent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [VECTOR] Embedding API例外: {Message}", ex.Message);
            }

            _logger.LogWarning("⚠️ [VECTOR] Embedding生成失敗 → キーワード検索にフォールバック");
            return new List<float>();
        }

        /// <summary>
        /// ハイブリッド検索リクエスト構築
        /// </summary>
        private object BuildHybridSearchRequest(string query, List<float> queryVector, List<string> workIds, int top)
        {
            _logger.LogInformation("🔸 [HYBRID] リクエスト構築開始: Vector次元={VectorDimensions}, WorkIdFilter={WorkIdCount}件", 
                queryVector?.Count ?? 0, workIds?.Count ?? 0);
            
            var filter = BuildWorkIdFilter(workIds);
            _logger.LogDebug("🔸 [HYBRID] フィルター構築: {Filter}", filter ?? "なし");
            
            // ベクトル検索 + キーワード検索のハイブリッド検索リクエスト
            if (queryVector != null && queryVector.Count > 0)
            {
                _logger.LogInformation("✅ [HYBRID] ベクトル + キーワードのハイブリッド検索リクエスト構築");
                
                var request = new
                {
                    search = query, // キーワード検索
                    vectorQueries = new[] // ベクトル検索
                    {
                        new
                        {
                            kind = "vector", // Azure Search ベクトルクエリに必須
                            vector = queryVector,
                            fields = "chunk_embeddings", // ベクトルフィールド名（oec-sentenceインデックス用）
                            k = top * 2, // ベクトル検索の候補数（最終結果のtop数より多めに取得）
                            exhaustive = false // パフォーマンス重視
                        }
                    },
                    searchMode = "any",
                    queryType = "simple", // シンプル検索（セマンティック設定不要）
                    filter = filter,
                    top = top,
                    // 🔥 orderByを削除（Azure Searchはデフォルトでスコア順ソート）
                    highlight = "chunk_text",
                    highlightPreTag = "<mark>",
                    highlightPostTag = "</mark>",
                    select = "id,workId,chunk_text,page_no,file_name"
                };
                
                _logger.LogInformation("🎯 [HYBRID] ハイブリッド検索設定: ベクトルフィールド=chunk_embeddings, インデックス={Index}, クエリタイプ=simple, ソート=スコア降順", _sentenceIndexName);
                return request;
            }
            else
            {
                _logger.LogWarning("⚠️ [HYBRID] ベクトルが無効 → キーワード検索のみにフォールバック");
                
                var request = new
                {
                    search = query,
                    searchMode = "any",
                    queryType = "simple",
                    filter = filter,
                    top = top,
                    // 🔥 orderByを削除（Azure Searchはデフォルトでスコア順ソート）
                    highlight = "chunk_text",
                    highlightPreTag = "<mark>",
                    highlightPostTag = "</mark>",
                    select = "id,workId,chunk_text,page_no,file_name"
                };
                
                return request;
            }
        }

        /// <summary>
        /// workIdフィルター構築
        /// </summary>
        private string BuildWorkIdFilter(List<string> workIds)
        {
            if (workIds == null || workIds.Count == 0)
            {
                return null;
            }

            if (workIds.Count == 1)
            {
                return $"workId eq '{workIds[0]}'";
            }

            var filters = workIds.Select(id => $"workId eq '{id}'");
            return $"({string.Join(" or ", filters)})";
        }

        /// <summary>
        /// 指定workIdの全ドキュメントをメイン/センテンス両インデックスから削除
        /// </summary>
        public async Task<(int deletedMain, int deletedSentence)> DeleteWorkIdAsync(string workId, string username = null)
        {
            try
            {
                if (!string.IsNullOrEmpty(username))
                {
                    // ユーザー固有のインデックスへ切替
                    SetUserSpecificIndexes(username);
                }

                // 内部関数: あるインデックスから workId を削除
                async Task<int> DeleteFromIndexAsync(string indexName)
                {
                    int totalDeleted = 0;
                    string searchUrl = $"{_searchEndpoint}/indexes/{indexName}/docs/search?api-version={_apiVersion}";
                    _httpClient.DefaultRequestHeaders.Clear();
                    _httpClient.DefaultRequestHeaders.Add("api-key", _searchKey);

                    while (true)
                    {
                        // ページングでIDを回収
                        var searchRequest = new
                        {
                            search = "*",
                            filter = $"workId eq '{workId}'",
                            top = 1000,
                            select = "id"
                        };

                        var json = JsonSerializer.Serialize(searchRequest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                        var content = new StringContent(json, Encoding.UTF8, "application/json");
                        var response = await _httpClient.PostAsync(searchUrl, content);

                        if (!response.IsSuccessStatusCode)
                        {
                            var err = await response.Content.ReadAsStringAsync();
                            _logger.LogError("DeleteWorkIdAsync 検索失敗: Index={Index} Status={Status} Error={Error}", indexName, response.StatusCode, err);
                            break;
                        }

                        var text = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(text);
                        var ids = new List<string>();
                        if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in arr.EnumerateArray())
                            {
                                if (item.TryGetProperty("id", out var idProp))
                                {
                                    var id = idProp.GetString();
                                    if (!string.IsNullOrEmpty(id)) ids.Add(id);
                                }
                            }
                        }

                        if (ids.Count == 0)
                        {
                            // これ以上なし
                            break;
                        }

                        // バッチ削除（1,000件まで）
                        var batch = IndexDocumentsBatch.Delete("id", ids);
                        try
                        {
                            var delResult = await _searchClient.IndexDocumentsAsync(batch);
                            int success = delResult.Results.Count(r => r.Succeeded);
                            totalDeleted += success;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "DeleteWorkIdAsync バッチ削除エラー: Index={Index}", indexName);
                        }

                        // レート制限回避のため短いインターバル
                        await Task.Delay(200);
                    }

                    _logger.LogInformation("DeleteWorkIdAsync 完了: Index={Index}, Deleted={Deleted}", indexName, totalDeleted);
                    return totalDeleted;
                }

                // メイン
                _searchClient = new SearchClient(new Uri(_searchEndpoint), _mainIndexName, new AzureKeyCredential(_searchKey));
                var deletedMain = await DeleteFromIndexAsync(_mainIndexName);
                // センテンス
                var sentenceClient = new SearchClient(new Uri(_searchEndpoint), _sentenceIndexName, new AzureKeyCredential(_searchKey));
                _searchClient = sentenceClient; // 再利用
                var deletedSentence = await DeleteFromIndexAsync(_sentenceIndexName);

                return (deletedMain, deletedSentence);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteWorkIdAsync 処理中エラー: workId={WorkId}", workId);
                return (0, 0);
            }
        }

        /// <summary>
        /// 検索リクエスト実行
        /// </summary>
        private async Task<List<SearchResult>> ExecuteSearchRequest(object searchRequest, bool useVectorIndex = false)
        {
            var indexName = useVectorIndex ? _sentenceIndexName : _mainIndexName;
            var url = $"{_searchEndpoint}/indexes/{indexName}/docs/search?api-version={_apiVersion}";
            var jsonContent = JsonSerializer.Serialize(searchRequest, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true  // 🔥 読みやすくするためにインデント追加
            });

            _logger.LogInformation("🔸 [SEARCH] Azure Search リクエスト送信: Index='{Index}', Type='{Type}', URL='{Url}'", 
                indexName, useVectorIndex ? "Vector" : "Keyword", url);
            
            // 🔥 リクエストJSONの要約ログを出力（ベクトルデータは除外）
            var requestSummary = ExtractRequestSummary(jsonContent);
            _logger.LogInformation("🔸 [SEARCH] 送信リクエスト要約:\n{RequestSummary}", requestSummary);

            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("api-key", _searchKey);

            var response = await _httpClient.PostAsync(url, content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("✅ [SEARCH] Azure Search 成功: StatusCode={StatusCode}", response.StatusCode);
                
                // 🔥 レスポンス内容の要約ログを出力（長すぎる場合は省略）
                var responseSummary = ExtractResponseSummary(responseContent);
                _logger.LogInformation("🔸 [SEARCH] 受信レスポンス要約:\n{ResponseSummary}", responseSummary);
                
                var results = ParseSearchResponse(responseContent);
                _logger.LogInformation("✅ [SEARCH] レスポンス解析完了: 結果={Count}件", results.Count);
                
                // デバッグ用: 検索結果の詳細ログ出力
                _logger.LogInformation("🔍 [SEARCH] 検索結果詳細（上位10件）:");
                for (int i = 0; i < Math.Min(10, results.Count); i++)
                {
                    var result = results[i];
                    _logger.LogInformation("  [{Index}] WorkID: {WorkId}, Score: {Score:F4}, ID: {Id}", 
                        i + 1, result.Filepath, result.Score, result.Id);
                    _logger.LogInformation("       Title: {Title}", result.Title);
                    _logger.LogInformation("       Content: {Content}", 
                        result.Content?.Length > 200 ? result.Content.Substring(0, 200) + "..." : result.Content);
                    _logger.LogInformation("       ---");
                }
                
                return results;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("❌ [SEARCH] Azure Search API エラー: StatusCode={StatusCode}, Error='{Error}'", 
                    response.StatusCode, errorContent);
                
                // ベクトル検索関連のエラーをチェック
                if (IsVectorSearchRelatedError(errorContent))
                {
                    _logger.LogWarning("⚠️ [SEARCH] ベクトル検索関連エラー検出 → キーワード検索にフォールバック");
                    return await FallbackToKeywordSearch(searchRequest);
                }
                
                return new List<SearchResult>();
            }
        }
        
        /// <summary>
        /// ベクトル検索関連のエラーかどうか判定
        /// </summary>
        private bool IsVectorSearchRelatedError(string errorContent)
        {
            if (string.IsNullOrEmpty(errorContent)) return false;
            
            var vectorErrorKeywords = new[]
            {
                "vectorQueries",
                "chunk_embeddings",
                "contentVector",
                "content_vector",
                "semanticConfiguration", 
                "vector field",
                "embedding",
                "semantic search"
            };
            
            return vectorErrorKeywords.Any(keyword => 
                errorContent.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }
        
        /// <summary>
        /// キーワード検索にフォールバック
        /// </summary>
        private async Task<List<SearchResult>> FallbackToKeywordSearch(object originalRequest)
        {
            try
            {
                _logger.LogInformation("🔄 [SEARCH] キーワード検索フォールバック実行");
                
                // オリジナルリクエストからキーワード検索部分を抽出
                var requestJson = JsonSerializer.Serialize(originalRequest);
                var requestObj = JsonSerializer.Deserialize<JsonElement>(requestJson);
                
                string searchQuery = "";
                if (requestObj.TryGetProperty("search", out var searchProp))
                {
                    searchQuery = searchProp.GetString() ?? "";
                }
                
                // シンプルなキーワード検索リクエストを構築（oecインデックス用）
                var fallbackRequest = new
                {
                    search = searchQuery,
                    searchMode = "any",
                    queryType = "simple",
                    top = requestObj.TryGetProperty("top", out var topProp) ? topProp.GetInt32() : 10,
                    highlight = "content",
                    highlightPreTag = "<mark>",
                    highlightPostTag = "</mark>",
                    select = "id,workId,title,content,page_no,file_name"
                };
                
                return await ExecuteKeywordOnlySearch(fallbackRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [SEARCH] フォールバック処理でもエラー: {Message}", ex.Message);
                return new List<SearchResult>();
            }
        }
        
        /// <summary>
        /// キーワード検索のみ実行（無限ループ防止のため別メソッド）
        /// </summary>
        private async Task<List<SearchResult>> ExecuteKeywordOnlySearch(object searchRequest)
        {
            var url = $"{_searchEndpoint}/indexes/{_mainIndexName}/docs/search?api-version={_apiVersion}";
            var jsonContent = JsonSerializer.Serialize(searchRequest, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            _logger.LogInformation("🔄 [SEARCH] キーワード検索のみ実行: リクエスト送信");
            _logger.LogDebug("🔸 [SEARCH] フォールバックリクエストJSON: {RequestJson}", jsonContent);

            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("api-key", _searchKey);

            var response = await _httpClient.PostAsync(url, content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("✅ [SEARCH] キーワード検索フォールバック成功: StatusCode={StatusCode}", response.StatusCode);
                
                var results = ParseSearchResponse(responseContent);
                _logger.LogInformation("✅ [SEARCH] フォールバック結果: {Count}件", results.Count);
                return results;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("❌ [SEARCH] キーワード検索フォールバックもエラー: StatusCode={StatusCode}, Error='{Error}'", 
                    response.StatusCode, errorContent);
                return new List<SearchResult>();
            }
        }

        /// <summary>
        /// リクエストJSONの要約を抽出（ベクトルデータを除外）
        /// </summary>
        private string ExtractRequestSummary(string jsonContent)
        {
            try
            {
                var doc = JsonDocument.Parse(jsonContent);
                var root = doc.RootElement;
                
                var summary = new StringBuilder();
                summary.AppendLine("{");
                
                if (root.TryGetProperty("search", out var searchProp))
                    summary.AppendLine($"  \"search\": \"{searchProp.GetString()}\",");
                
                if (root.TryGetProperty("searchMode", out var searchModeProp))
                    summary.AppendLine($"  \"searchMode\": \"{searchModeProp.GetString()}\",");
                
                if (root.TryGetProperty("queryType", out var queryTypeProp))
                    summary.AppendLine($"  \"queryType\": \"{queryTypeProp.GetString()}\",");
                
                if (root.TryGetProperty("filter", out var filterProp))
                {
                    var filterStr = filterProp.GetString();
                    if (filterStr.Length > 200)
                        filterStr = filterStr.Substring(0, 200) + "...";
                    summary.AppendLine($"  \"filter\": \"{filterStr}\",");
                }
                
                if (root.TryGetProperty("top", out var topProp))
                    summary.AppendLine($"  \"top\": {topProp.GetInt32()},");
                
                if (root.TryGetProperty("vectorQueries", out var vectorQueriesProp))
                {
                    summary.AppendLine("  \"vectorQueries\": [");
                    summary.AppendLine("    {");
                    summary.AppendLine("      \"kind\": \"vector\",");
                    summary.AppendLine("      \"fields\": \"chunk_embeddings\",");
                    summary.AppendLine("      \"vector\": \"[3072次元ベクトル - 省略]\",");
                    summary.AppendLine("      \"k\": 20,");
                    summary.AppendLine("      \"exhaustive\": false");
                    summary.AppendLine("    }");
                    summary.AppendLine("  ]");
                }
                
                summary.AppendLine("}");
                return summary.ToString();
            }
            catch (Exception ex)
            {
                return $"リクエスト要約の生成に失敗: {ex.Message}";
            }
        }

        /// <summary>
        /// レスポンスJSONの要約を抽出
        /// </summary>
        private string ExtractResponseSummary(string responseContent)
        {
            try
            {
                var doc = JsonDocument.Parse(responseContent);
                var root = doc.RootElement;
                
                var summary = new StringBuilder();
                summary.AppendLine("{");
                
                if (root.TryGetProperty("@odata.context", out var contextProp))
                    summary.AppendLine($"  \"@odata.context\": \"{contextProp.GetString()}\",");
                
                if (root.TryGetProperty("@odata.count", out var countProp))
                    summary.AppendLine($"  \"@odata.count\": {countProp.GetInt32()},");
                
                if (root.TryGetProperty("value", out var valueProp) && valueProp.ValueKind == JsonValueKind.Array)
                {
                    summary.AppendLine($"  \"value\": [");
                    var count = 0;
                    foreach (var item in valueProp.EnumerateArray())
                    {
                        if (count >= 3) // 最初の3件のみ表示
                        {
                            summary.AppendLine("    ... (残りの結果は省略)");
                            break;
                        }
                        
                        summary.AppendLine("    {");
                        
                        if (item.TryGetProperty("@search.score", out var scoreProp))
                            summary.AppendLine($"      \"@search.score\": {scoreProp.GetDouble():F4},");
                        
                        if (item.TryGetProperty("id", out var idProp))
                            summary.AppendLine($"      \"id\": \"{idProp.GetString()}\",");
                        
                        if (item.TryGetProperty("workId", out var workIdProp))
                            summary.AppendLine($"      \"workId\": \"{workIdProp.GetString()}\",");
                        
                        if (item.TryGetProperty("chunk_text", out var chunkTextProp))
                        {
                            var chunkText = chunkTextProp.GetString();
                            if (chunkText.Length > 100)
                                chunkText = chunkText.Substring(0, 100) + "...";
                            summary.AppendLine($"      \"chunk_text\": \"{chunkText}\"");
                        }
                        
                        summary.AppendLine("    },");
                        count++;
                    }
                    summary.AppendLine("  ]");
                }
                
                summary.AppendLine("}");
                return summary.ToString();
            }
            catch (Exception ex)
            {
                return $"レスポンス要約の生成に失敗: {ex.Message}";
            }
        }

        /// <summary>
        /// Azure Search レスポンス解析
        /// </summary>
        private List<SearchResult> ParseSearchResponse(string responseContent)
        {
            var results = new List<SearchResult>();

            try
            {
                var response = JsonSerializer.Deserialize<JsonElement>(responseContent);
                
                if (response.TryGetProperty("value", out var valueArray))
                {
                    foreach (var item in valueArray.EnumerateArray())
                    {
                        var result = new SearchResult();

                        if (item.TryGetProperty("id", out var idElement))
                            result.Id = idElement.GetString();

                        if (item.TryGetProperty("workId", out var workIdElement))
                            result.Filepath = workIdElement.GetString(); // workIdをFilepathに格納

                        if (item.TryGetProperty("title", out var titleElement))
                            result.Title = titleElement.GetString();

                        // インデックスによって異なるcontentフィールドを処理
                        if (item.TryGetProperty("content", out var contentElement))
                            result.Content = contentElement.GetString();
                        else if (item.TryGetProperty("chunk_text", out var chunkTextElement))
                            result.Content = chunkTextElement.GetString();

                        if (item.TryGetProperty("@search.score", out var scoreElement))
                            result.Score = scoreElement.GetDouble();

                        if (item.TryGetProperty("page_no", out var pageElement))
                            result.PageNumber = pageElement.GetInt32();

                        // chunk_indexフィールドを処理（oec-sentenceインデックスのみ）
                        if (item.TryGetProperty("chunk_index", out var chunkIndexElement))
                            result.ChunkNumber = chunkIndexElement.GetInt32();
                        else
                            result.ChunkNumber = 0; // oecインデックスにはchunk_indexがない

                        // ハイライト情報を処理（インデックスによってフィールドが異なる）
                        if (item.TryGetProperty("@search.highlights", out var highlightsElement))
                        {
                            var highlights = new List<string>();
                            
                            // oecインデックスの場合：contentフィールド
                            if (highlightsElement.TryGetProperty("content", out var contentHighlights))
                            {
                                foreach (var highlight in contentHighlights.EnumerateArray())
                                {
                                    highlights.Add(highlight.GetString());
                                }
                            }
                            // oec-sentenceインデックスの場合：chunk_textフィールド
                            else if (highlightsElement.TryGetProperty("chunk_text", out var chunkTextHighlights))
                            {
                                foreach (var highlight in chunkTextHighlights.EnumerateArray())
                                {
                                    highlights.Add(highlight.GetString());
                                }
                            }
                            
                            if (highlights.Count > 0)
                            {
                                result.MatchedKeywords = string.Join("; ", highlights);
                            }
                        }

                        results.Add(result);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "検索レスポンス解析でエラーが発生");
            }

            return results;
        }
    }
}