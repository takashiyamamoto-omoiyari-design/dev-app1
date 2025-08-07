using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AzureRag.Models;
using AzureRag.Models.Settings;

namespace AzureRag.Services
{
    /// <summary>
    /// 複数インデックス対応Azure検索サービス実装
    /// </summary>
    public class MultiIndexSearchService : IMultiIndexSearchService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<MultiIndexSearchService> _logger;
        private readonly AzureSearchSettings _searchSettings;
        private readonly SearchIndexClient _searchIndexClient;
        private readonly SearchClient _mainSearchClient;
        private readonly SearchClient _sentenceSearchClient;

        /// <summary>
        /// メインインデックス名を取得
        /// </summary>
        public string MainIndexName => _searchSettings.Indexes.Main;

        /// <summary>
        /// 文章インデックス名を取得
        /// </summary>
        public string SentenceIndexName => _searchSettings.Indexes.Sentence;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public MultiIndexSearchService(IConfiguration configuration, ILogger<MultiIndexSearchService> logger)
        {
            _configuration = configuration;
            _logger = logger;

            // 設定を読み込み
            _searchSettings = new AzureSearchSettings
            {
                Endpoint = _configuration["AzureSearch:Endpoint"],
                ApiKey = _configuration["AzureSearch:ApiKey"],
                ApiVersion = _configuration["AzureSearch:ApiVersion"],
                Indexes = new IndexSettings
                {
                    Main = _configuration["AzureSearch:Indexes:Main"],
                    Sentence = _configuration["AzureSearch:Indexes:Sentence"]
                }
            };

            // エンドポイントとAPIキーの確認
            if (string.IsNullOrEmpty(_searchSettings.Endpoint) || string.IsNullOrEmpty(_searchSettings.ApiKey))
            {
                throw new ArgumentException("Azure Search configuration is incomplete");
            }

            // インデックスクライアントを初期化
            _searchIndexClient = new SearchIndexClient(
                new Uri(_searchSettings.Endpoint),
                new AzureKeyCredential(_searchSettings.ApiKey));

            // メインインデックス用検索クライアントを初期化
            _mainSearchClient = new SearchClient(
                new Uri(_searchSettings.Endpoint),
                _searchSettings.Indexes.Main,
                new AzureKeyCredential(_searchSettings.ApiKey));

            // 文章インデックス用検索クライアントを初期化
            _sentenceSearchClient = new SearchClient(
                new Uri(_searchSettings.Endpoint),
                _searchSettings.Indexes.Sentence,
                new AzureKeyCredential(_searchSettings.ApiKey));
        }

        /// <summary>
        /// 文章インデックスでクエリに基づいてドキュメントを検索します
        /// </summary>
        public async Task<List<DocumentSearchResult>> SearchSentencesAsync(string query, int top = 10)
        {
            try
            {
                _logger.LogInformation($"文章インデックス '{SentenceIndexName}' で検索: {query}");

                // 検索オプションを設定
                SearchOptions options = new SearchOptions
                {
                    IncludeTotalCount = true,
                    Size = top,
                    QueryType = SearchQueryType.Simple,
                    HighlightPreTag = "",
                    HighlightPostTag = ""
                };

                // 返却するフィールドを指定
                options.Select.Add("id");
                options.Select.Add("documentId");
                options.Select.Add("sentence");

                // 検索を実行
                SearchResults<SearchDocument> response = await _sentenceSearchClient.SearchAsync<SearchDocument>(query, options);

                // 結果を取得
                List<DocumentSearchResult> results = new List<DocumentSearchResult>();
                await foreach (SearchResult<SearchDocument> result in response.GetResultsAsync())
                {
                    results.Add(new DocumentSearchResult
                    {
                        Id = result.Document["id"]?.ToString(),
                        Title = result.Document.ContainsKey("documentId") ? result.Document["documentId"]?.ToString() : "",
                        Content = result.Document["sentence"]?.ToString(),
                        Score = result.Score ?? 0
                    });
                }

                _logger.LogInformation($"文章インデックス検索結果数: {results.Count}");
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "文章インデックス検索中にエラーが発生しました");
                throw;
            }
        }

        /// <summary>
        /// メインインデックスでクエリに基づいてドキュメントを検索します
        /// </summary>
        public async Task<List<DocumentSearchResult>> SearchDocumentsAsync(string query, int top = 3)
        {
            try
            {
                _logger.LogInformation($"メインインデックス '{MainIndexName}' で検索: {query}");

                // 検索オプションを設定
                SearchOptions options = new SearchOptions
                {
                    IncludeTotalCount = true,
                    Size = top,
                    QueryType = SearchQueryType.Simple,
                    HighlightPreTag = "",
                    HighlightPostTag = ""
                };

                // 検索対象フィールドを指定
                options.SearchFields.Add("content");
                options.SearchFields.Add("title");

                // 返却するフィールドを指定
                options.Select.Add("id");
                options.Select.Add("title");
                options.Select.Add("content");

                // 検索を実行
                SearchResults<SearchDocument> response = await _mainSearchClient.SearchAsync<SearchDocument>(query, options);

                // 結果を取得
                List<DocumentSearchResult> results = new List<DocumentSearchResult>();
                await foreach (SearchResult<SearchDocument> result in response.GetResultsAsync())
                {
                    results.Add(new DocumentSearchResult
                    {
                        Id = result.Document["id"]?.ToString(),
                        Title = result.Document["title"]?.ToString(),
                        Content = result.Document["content"]?.ToString(),
                        Score = result.Score ?? 0
                    });
                }

                _logger.LogInformation($"メインインデックス検索結果数: {results.Count}");
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "メインインデックス検索中にエラーが発生しました");
                throw;
            }
        }

        /// <summary>
        /// 文書をメインインデックスに追加します
        /// </summary>
        public async Task<bool> IndexDocumentAsync(string id, string title, string content)
        {
            try
            {
                _logger.LogInformation($"文書をメインインデックスに追加: {id}, {title}");

                // インデックスが存在することを確認
                bool indexExists = await EnsureMainIndexExistsAsync();
                if (!indexExists)
                {
                    _logger.LogError($"メインインデックスの作成に失敗しました: {MainIndexName}");
                    return false;
                }

                // ドキュメントを作成
                var document = new SearchDocument
                {
                    ["id"] = id,
                    ["title"] = title,
                    ["content"] = content,
                    ["last_updated"] = DateTime.UtcNow
                };

                // 一度に1つのドキュメントをインデックスに追加
                var batch = IndexDocumentsBatch.Upload(new[] { document });
                IndexDocumentsResult result = await _mainSearchClient.IndexDocumentsAsync(batch);

                // 成功したかを確認
                bool success = !result.Results.Any(r => r.Succeeded == false);
                if (success)
                {
                    _logger.LogInformation($"文書をメインインデックスに追加しました: {id}");
                }
                else
                {
                    foreach (var itemResult in result.Results)
                    {
                        if (!itemResult.Succeeded)
                        {
                            _logger.LogWarning($"文書インデックス化失敗: キー={itemResult.Key}, エラー={itemResult.ErrorMessage}");
                        }
                    }
                    _logger.LogWarning($"文書のメインインデックス追加が失敗しました: {id}");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"文書のメインインデックス追加中にエラーが発生: {id}");
                return false;
            }
        }

        /// <summary>
        /// 文章を文章インデックスに追加します
        /// </summary>
        public async Task<bool> IndexSentenceAsync(string id, string documentId, string sentence)
        {
            try
            {
                _logger.LogInformation($"文章を文章インデックスに追加: {id}, ドキュメントID: {documentId}");

                // インデックスが存在することを確認
                bool indexExists = await EnsureSentenceIndexExistsAsync();
                if (!indexExists)
                {
                    _logger.LogError($"文章インデックスの作成に失敗しました: {SentenceIndexName}");
                    return false;
                }

                // ドキュメントを作成
                var document = new SearchDocument
                {
                    ["id"] = id,
                    ["documentId"] = documentId,
                    ["sentence"] = sentence,
                    ["last_updated"] = DateTime.UtcNow
                };

                // 一度に1つのドキュメントをインデックスに追加
                var batch = IndexDocumentsBatch.Upload(new[] { document });
                IndexDocumentsResult result = await _sentenceSearchClient.IndexDocumentsAsync(batch);

                // 成功したかを確認
                bool success = !result.Results.Any(r => r.Succeeded == false);
                if (success)
                {
                    _logger.LogInformation($"文章を文章インデックスに追加しました: {id}");
                }
                else
                {
                    foreach (var itemResult in result.Results)
                    {
                        if (!itemResult.Succeeded)
                        {
                            _logger.LogWarning($"文章インデックス化失敗: キー={itemResult.Key}, エラー={itemResult.ErrorMessage}");
                        }
                    }
                    _logger.LogWarning($"文章の文章インデックス追加が失敗しました: {id}");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"文章の文章インデックス追加中にエラーが発生: {id}");
                return false;
            }
        }

        /// <summary>
        /// 指定されたIDに関連するドキュメントをメインインデックスから削除します
        /// </summary>
        public async Task<bool> DeleteDocumentAsync(string documentId)
        {
            try
            {
                _logger.LogInformation($"メインインデックスからドキュメントを削除: {documentId}");

                // 前方一致でIDを検索
                SearchOptions options = new SearchOptions
                {
                    Filter = $"id eq '{documentId}'"
                };

                // 削除対象のドキュメントを検索
                SearchResults<SearchDocument> response = await _mainSearchClient.SearchAsync<SearchDocument>("*", options);
                List<string> docIds = new List<string>();

                await foreach (SearchResult<SearchDocument> result in response.GetResultsAsync())
                {
                    string id = result.Document["id"].ToString();
                    docIds.Add(id);
                }

                if (docIds.Count == 0)
                {
                    _logger.LogWarning($"削除対象のドキュメントが見つかりません: {documentId}");
                    return false;
                }

                // ドキュメントを削除
                var batch = IndexDocumentsBatch.Delete("id", docIds);
                IndexDocumentsResult result = await _mainSearchClient.IndexDocumentsAsync(batch);

                // 成功したかを確認
                bool success = !result.Results.Any(r => r.Succeeded == false);
                if (success)
                {
                    _logger.LogInformation($"メインインデックスからドキュメントを削除しました: {documentId}, 削除数: {docIds.Count}");
                }
                else
                {
                    _logger.LogWarning($"ドキュメントの削除が失敗しました: {documentId}");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"ドキュメント削除中にエラーが発生: {documentId}");
                return false;
            }
        }

        /// <summary>
        /// 指定されたドキュメントIDに関連する文章を文章インデックスから削除します
        /// </summary>
        public async Task<bool> DeleteSentencesAsync(string documentId)
        {
            try
            {
                _logger.LogInformation($"文章インデックスからドキュメントIDに関連する文章を削除: {documentId}");

                // DocumentIDでフィルタリング
                SearchOptions options = new SearchOptions
                {
                    Filter = $"documentId eq '{documentId}'"
                };

                // 削除対象の文章を検索
                SearchResults<SearchDocument> response = await _sentenceSearchClient.SearchAsync<SearchDocument>("*", options);
                List<string> sentenceIds = new List<string>();

                await foreach (SearchResult<SearchDocument> result in response.GetResultsAsync())
                {
                    string id = result.Document["id"].ToString();
                    sentenceIds.Add(id);
                }

                if (sentenceIds.Count == 0)
                {
                    _logger.LogWarning($"削除対象の文章が見つかりません: ドキュメントID {documentId}");
                    return false;
                }

                // 文章を削除
                var batch = IndexDocumentsBatch.Delete("id", sentenceIds);
                IndexDocumentsResult result = await _sentenceSearchClient.IndexDocumentsAsync(batch);

                // 成功したかを確認
                bool success = !result.Results.Any(r => r.Succeeded == false);
                if (success)
                {
                    _logger.LogInformation($"文章インデックスから文章を削除しました: ドキュメントID {documentId}, 削除数: {sentenceIds.Count}");
                }
                else
                {
                    _logger.LogWarning($"文章の削除が失敗しました: ドキュメントID {documentId}");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"文章削除中にエラーが発生: ドキュメントID {documentId}");
                return false;
            }
        }

        /// <summary>
        /// メインインデックスが存在するか確認します。存在しない場合は作成します。
        /// </summary>
        public async Task<bool> EnsureMainIndexExistsAsync()
        {
            try
            {
                // インデックスが存在するか確認
                var indexes = _searchIndexClient.GetIndexes();
                bool exists = false;

                await foreach (var index in indexes)
                {
                    if (index.Name == MainIndexName)
                    {
                        exists = true;
                        _logger.LogInformation($"既存のメインインデックスが見つかりました: {MainIndexName}");
                        break;
                    }
                }

                if (!exists)
                {
                    _logger.LogInformation($"メインインデックスが存在しないため、作成します: {MainIndexName}");

                    // フィールド定義
                    var fields = new List<SearchField>
                    {
                        new SearchField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true, IsSortable = true },
                        new SearchField("title", SearchFieldDataType.String) { IsSearchable = true, IsFilterable = true, IsSortable = true },
                        new SearchField("content", SearchFieldDataType.String) { IsSearchable = true, IsFilterable = false },
                        new SearchField("last_updated", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true }
                    };

                    // インデックスを作成
                    var definition = new SearchIndex(MainIndexName, fields);
                    await _searchIndexClient.CreateOrUpdateIndexAsync(definition);

                    _logger.LogInformation($"メインインデックスを作成しました: {MainIndexName}");
                    return true;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"メインインデックス確認/作成中にエラーが発生: {MainIndexName}");
                return false;
            }
        }

        /// <summary>
        /// 文章インデックスが存在するか確認します。存在しない場合は作成します。
        /// </summary>
        public async Task<bool> EnsureSentenceIndexExistsAsync()
        {
            try
            {
                // インデックスが存在するか確認
                var indexes = _searchIndexClient.GetIndexes();
                bool exists = false;

                await foreach (var index in indexes)
                {
                    if (index.Name == SentenceIndexName)
                    {
                        exists = true;
                        _logger.LogInformation($"既存の文章インデックスが見つかりました: {SentenceIndexName}");
                        break;
                    }
                }

                if (!exists)
                {
                    _logger.LogInformation($"文章インデックスが存在しないため、作成します: {SentenceIndexName}");

                    // フィールド定義
                    var fields = new List<SearchField>
                    {
                        new SearchField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true, IsSortable = true },
                        new SearchField("documentId", SearchFieldDataType.String) { IsSearchable = false, IsFilterable = true, IsSortable = true },
                        new SearchField("sentence", SearchFieldDataType.String) { IsSearchable = true, IsFilterable = false },
                        new SearchField("last_updated", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true }
                    };

                    // インデックスを作成
                    var definition = new SearchIndex(SentenceIndexName, fields);
                    await _searchIndexClient.CreateOrUpdateIndexAsync(definition);

                    _logger.LogInformation($"文章インデックスを作成しました: {SentenceIndexName}");
                    return true;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"文章インデックス確認/作成中にエラーが発生: {SentenceIndexName}");
                return false;
            }
        }
    }
}