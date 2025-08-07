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
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon;
using System.IO;

namespace AzureRag.Services
{
    /// <summary>
    /// シノニムマッチ結果を表すクラス
    /// </summary>
    public class SynonymMatch
    {
        public string OriginalKeyword { get; set; }
        public string MatchedSynonym { get; set; }
        public List<string> RelatedSynonyms { get; set; } = new List<string>();
    }

    public class DocumentChatService : IDocumentChatService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<DocumentChatService> _logger;
        private readonly string _searchEndpoint;
        private readonly string _searchKey;
        private readonly string _apiVersion;
        private readonly string _mainIndexName;
        private readonly string _sentenceIndexName;
        
        // AWS Bedrock Claude API 用の設定
        private readonly AmazonBedrockRuntimeClient _bedrockClient;
        private readonly string _claudeModel;
        private readonly string _claudeModelFallback;
        private readonly string _awsRegion;
        
        // 🆕 動的インデックス対応のため追加
        private readonly Services.IAuthorizationService _authorizationService;

        public DocumentChatService(IConfiguration configuration, ILogger<DocumentChatService> logger, Services.IAuthorizationService authorizationService)
        {
            _logger = logger;
            _httpClient = new HttpClient();
            _authorizationService = authorizationService;

            // 🆕 DataIngestion設定を取得（MSPSeimeiではなく）
            var dataIngestionConfig = configuration.GetSection("DataIngestion");
            _searchEndpoint = dataIngestionConfig["AzureSearchEndpoint"];
            _searchKey = dataIngestionConfig["AzureSearchKey"];
            _apiVersion = dataIngestionConfig["AzureSearchApiVersion"] ?? "2024-07-01";
            _mainIndexName = dataIngestionConfig["MainIndexName"] ?? "oec";
            _sentenceIndexName = dataIngestionConfig["SentenceIndexName"] ?? "oec-sentence";
            
            // AWS Bedrock Claude API設定をDataIngestionから取得
            _claudeModel = dataIngestionConfig["ClaudeModel"] ?? "apac.anthropic.claude-sonnet-4-20250514-v1:0";
            _claudeModelFallback = dataIngestionConfig["ClaudeModelFallback"] ?? "anthropic.claude-3-5-sonnet-20241022-v2:0";
            _awsRegion = dataIngestionConfig["AwsRegion"] ?? "ap-northeast-1";

            // AWS Bedrock RuntimeクライアントをEC2 IAMロールで初期化
            var regionEndpoint = RegionEndpoint.GetBySystemName(_awsRegion);
            _bedrockClient = new AmazonBedrockRuntimeClient(regionEndpoint);

            _logger.LogInformation("🔧 DocumentChatService初期化完了 (AWS Bedrock):");
            _logger.LogInformation("  📊 メインモデル: {ClaudeModel}", _claudeModel);
            _logger.LogInformation("  🔄 フォールバックモデル: {ClaudeModelFallback}", _claudeModelFallback);
            _logger.LogInformation("  🌏 AWSリージョン: {Region}", _awsRegion);
            _logger.LogInformation("  🧪 テスト用無効モデル設定: {IsInvalid}", _claudeModel.Contains("INVALID"));
            _logger.LogInformation("  📝 MainIndex: {MainIndex}, SentenceIndex: {SentenceIndex}", _mainIndexName, _sentenceIndexName);

        }

        /// <summary>
        /// ユーザーの権限に基づいて適切なメインインデックス名を動的取得
        /// </summary>
        /// <param name="username">ユーザー名</param>
        /// <returns>アクセス可能なメインインデックス名</returns>
        /// <exception cref="UnauthorizedAccessException">権限がないユーザーの場合</exception>
        private async Task<string> GetUserMainIndexAsync(string username)
        {
            try
            {
                if (string.IsNullOrEmpty(username))
                {
                    throw new UnauthorizedAccessException("ユーザー名が指定されていません");
                }

                var indexPairs = await _authorizationService.GetUserIndexPairsAsync(username);
                if (indexPairs?.Any() == true)
                {
                    var mainIndex = indexPairs.First().MainIndex;
                    _logger.LogInformation("✅ ユーザー権限確認済みメインインデックス: {Username} → {MainIndex}", username, mainIndex);
                    return mainIndex;
                }
                else
                {
                    _logger.LogError("❌ ユーザー権限なし: {Username} はメインインデックスにアクセスできません", username);
                    throw new UnauthorizedAccessException($"ユーザー '{username}' はメインインデックスにアクセスする権限がありません");
                }
            }
            catch (UnauthorizedAccessException)
            {
                throw; // 権限エラーはそのまま再スロー
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ ユーザーメインインデックス取得エラー: {Username}", username);
                throw new UnauthorizedAccessException($"ユーザー '{username}' のインデックス権限確認中にエラーが発生しました", ex);
            }
        }

        /// <summary>
        /// ユーザーの権限に基づいて適切なセンテンスインデックス名を動的取得
        /// </summary>
        /// <param name="username">ユーザー名</param>
        /// <returns>アクセス可能なセンテンスインデックス名</returns>
        /// <exception cref="UnauthorizedAccessException">権限がないユーザーの場合</exception>
        private async Task<string> GetUserSentenceIndexAsync(string username)
        {
            try
            {
                if (string.IsNullOrEmpty(username))
                {
                    throw new UnauthorizedAccessException("ユーザー名が指定されていません");
                }

                var indexPairs = await _authorizationService.GetUserIndexPairsAsync(username);
                if (indexPairs?.Any() == true)
                {
                    var sentenceIndex = indexPairs.First().SentenceIndex;
                    _logger.LogInformation("✅ ユーザー権限確認済みセンテンスインデックス: {Username} → {SentenceIndex}", username, sentenceIndex);
                    return sentenceIndex;
                }
                else
                {
                    _logger.LogError("❌ ユーザー権限なし: {Username} はセンテンスインデックスにアクセスできません", username);
                    throw new UnauthorizedAccessException($"ユーザー '{username}' はセンテンスインデックスにアクセスする権限がありません");
                }
            }
            catch (UnauthorizedAccessException)
            {
                throw; // 権限エラーはそのまま再スロー
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ ユーザーセンテンスインデックス取得エラー: {Username}", username);
                throw new UnauthorizedAccessException($"ユーザー '{username}' のインデックス権限確認中にエラーが発生しました", ex);
            }
        }

        /// <summary>
        /// ユーザー権限に基づいてユニークなファイルパスを動的取得
        /// </summary>
        /// <param name="username">ユーザー名</param>
        /// <exception cref="UnauthorizedAccessException">権限がないユーザーの場合</exception>
        public async Task<List<string>> GetUniqueFilePaths(string username)
        {
            try
            {
                // 🆕 ユーザー権限に基づく動的インデックス選択
                var userMainIndex = await GetUserMainIndexAsync(username);
                _logger.LogInformation("🔍 ユニークファイルパス取得: {Username} → {MainIndex}", username, userMainIndex);
                
                var url = $"{_searchEndpoint}/indexes/{userMainIndex}/docs/search?api-version={_apiVersion}";
                var searchRequest = new
                {
                    search = "*",
                    select = "filepath",
                    top = 1000 // 十分な数のドキュメントを取得
                };

                // リクエストの詳細をログに出力
                var searchRequestJson = JsonSerializer.Serialize(searchRequest);

                // リクエストヘッダーを再設定
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("api-key", _searchKey);

                var content = new StringContent(searchRequestJson, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    
                    using (JsonDocument document = JsonDocument.Parse(jsonString))
                    {
                        var filepathSet = new HashSet<string>(); // 重複を避けるためにHashSetを使用
                        
                        // "value"プロパティが存在するか確認
                        if (document.RootElement.TryGetProperty("value", out var documentsElement))
                        {
                            var documents = documentsElement;
                            int documentCount = documents.GetArrayLength();
                            
                            foreach (var doc in documents.EnumerateArray())
                            {
                                if (doc.TryGetProperty("filepath", out var filepath) && 
                                    !string.IsNullOrEmpty(filepath.GetString()))
                                {
                                    filepathSet.Add(filepath.GetString());
                                }
                            }
                            
                            var filepaths = filepathSet.ToList();
                            
                            // 最初の数個のファイルパスをログに出力
                            if (filepaths.Count > 0)
                            {
                                int logCount = Math.Min(5, filepaths.Count);
                                for (int i = 0; i < logCount; i++)
                                {
                                }
                            }
                            
                            return filepaths;
                        }
                        else
                        {
                            return new List<string>();
                        }
                    }
                }
                else
                {
                    return new List<string>();
                }
            }
            catch (Exception ex)
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// ユーザー権限に基づいて指定ファイルパスのドキュメントを動的取得
        /// </summary>
        /// <param name="filepath">ファイルパス</param>
        /// <param name="username">ユーザー名</param>
        /// <exception cref="UnauthorizedAccessException">権限がないユーザーの場合</exception>
        public async Task<string> GetDocumentsByFilePath(string filepath, string username)
        {
            try
            {
                // 🆕 ユーザー権限に基づく動的インデックス選択
                var userMainIndex = await GetUserMainIndexAsync(username);
                _logger.LogInformation("🔍 ファイルパスドキュメント取得: {Username} → {MainIndex}, FilePath={FilePath}", username, userMainIndex, filepath);
                
                // ファイルパスからファイル名のみを抽出
                string fileName = System.IO.Path.GetFileName(filepath);
                
                var url = $"{_searchEndpoint}/indexes/{userMainIndex}/docs/search?api-version={_apiVersion}";
                var searchRequest = new
                {
                    // ファイル名のみで検索
                    search = $"\"{fileName}\"",
                    searchFields = "filepath",
                    select = "id,title,filepath,content",
                    top = 10
                };

                // リクエストの詳細をログに出力
                var searchRequestJson = JsonSerializer.Serialize(searchRequest);

                // リクエストヘッダーを再設定
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("api-key", _searchKey);

                var content = new StringContent(searchRequestJson, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    
                    using (JsonDocument document = JsonDocument.Parse(jsonString))
                    {
                        // "value"プロパティが存在するか確認
                        if (document.RootElement.TryGetProperty("value", out var documentsElement))
                        {
                            var documents = documentsElement;
                            int documentCount = documents.GetArrayLength();
                            
                            var sb = new StringBuilder();
                            var foundExactMatch = false;

                            // ファイル名が一致するドキュメントを探す
                            foreach (var doc in documents.EnumerateArray())
                            {
                                if (doc.TryGetProperty("filepath", out var pathElement) && 
                                    pathElement.GetString().Contains(fileName) &&
                                    doc.TryGetProperty("content", out var contentElement))
                                {
                                    var contentStr = contentElement.GetString();
                                    sb.Append(contentStr);
                                    foundExactMatch = true;
                                    
                                    break; // 一致が見つかったら終了
                                }
                            }

                            // 一致が見つからなかった場合は、最初に見つかったドキュメントを使用
                            if (!foundExactMatch && documents.GetArrayLength() > 0)
                            {
                                var firstDoc = documents[0];
                                if (firstDoc.TryGetProperty("content", out var contentElement))
                                {
                                    var contentStr = contentElement.GetString();
                                    sb.Append(contentStr);
                                    
                                }
                            }

                            var result = sb.ToString();
                            
                            if (result.Length > 0)
                            {
                                // 内容の一部をログに出力
                                int logLength = Math.Min(200, result.Length);
                            }
                            
                            return result;
                        }
                        else
                        {
                            return string.Empty;
                        }
                    }
                }
                else
                {
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                return string.Empty;
            }
        }
        
        /// <summary>
        /// クエリに基づいて回答を生成する（ユーザー権限制御対応）
        /// </summary>
        /// <param name="message">ユーザーからのメッセージ</param>
        /// <param name="documentContext">追加のドキュメントコンテキスト（オプション）</param>
        /// <param name="customSystemPrompt">カスタムシステムプロンプト（オプション）</param>
        /// <param name="username">認証ユーザー名</param>
        /// <returns>生成された回答と検索結果のソースのタプル</returns>
        /// <exception cref="UnauthorizedAccessException">権限がないユーザーの場合</exception>
        public async Task<(string answer, List<Models.SearchResult> sources)> GenerateAnswer(string message, string documentContext, string customSystemPrompt = null, string username = null)
        {
            // usernameが必須であることを確認
            if (string.IsNullOrEmpty(username))
            {
                throw new UnauthorizedAccessException("ユーザー名が指定されていません。チャット機能にはユーザー認証が必要です。");
            }

            // ユーザー権限チェック（メインインデックスアクセス権限で確認）
            await GetUserMainIndexAsync(username); // 権限がなければここで例外がスローされる

            var (answer, sources, _) = await GenerateAnswerInternal(message, documentContext, customSystemPrompt, null, username);
            return (answer, sources);
        }

        /// <summary>
        /// 内部実装：クエリに基づいて回答を生成する（シノニム対応版）
        /// </summary>
        /// <param name="message">ユーザーからのメッセージ</param>
        /// <param name="documentContext">追加のドキュメントコンテキスト（オプション）</param>
        /// <param name="customSystemPrompt">カスタムシステムプロンプト（オプション）</param>
        /// <param name="synonymList">シノニムリスト（オプション）</param>
        /// <param name="username">認証ユーザー名</param>
        /// <returns>生成された回答、検索結果のソース、使用されたシノニムのタプル</returns>
        private async Task<(string answer, List<Models.SearchResult> sources, List<SynonymMatch> usedSynonyms)> GenerateAnswerInternal(string message, string documentContext, string customSystemPrompt = null, List<SynonymItem> synonymList = null, string username = null)
        {
            try
            {
                
                // 🔍 [DEBUG] Claude呼び出し前のシノニム処理デバッグ
                
                List<SynonymMatch> usedSynonyms = new List<SynonymMatch>();
                string originalMessage = message;
                
                // シノニム処理
                if (synonymList != null && synonymList.Count > 0)
                {
                    
                    // キーワード抽出
                    var keywords = ExtractKeywords(message);
                    
                    // シノニム検索
                    usedSynonyms = FindSynonymsForKeywords(keywords, synonymList);
                    
                    // マッチしたシノニムの詳細ログ
                    if (usedSynonyms.Count > 0)
                    {
                        foreach (var match in usedSynonyms)
                        {
                        }
                        
                        // クエリ拡張
                        
                        var expandedQuery = ExpandQueryWithSynonyms(message, usedSynonyms);
                        message = expandedQuery;
                        
                    }
                    else
                    {
                    }
                }
                else
                {
                }
                
                // 【TRACING】Add detailed logging for debugging
                
                // コンテキスト情報とソース情報を初期化
                string contextText = "";
                List<Models.SearchResult> searchResults = new List<Models.SearchResult>();
                
                // クライアントから送られたコンテキストを使用
                if (!string.IsNullOrEmpty(documentContext))
                {
                    contextText = documentContext;
                    
                    // ダミーの検索結果を作成（ソース情報のみ使用するため）
                    searchResults.Add(new Models.SearchResult
                    {
                        Id = "client-context",
                        Filepath = "client-context",
                        Title = "クライアントのコンテキスト",
                        Content = contextText.Length > 100 ? contextText.Substring(0, 100) + "..." : contextText
                    });
                    
                }
                else
                {
                    // コンテキストがない場合は「情報なし」メッセージを返す

                    // 注意: コンテキストがなくても処理を継続
                    // 空のコンテキストを作成して処理を続行
                    contextText = ""; // 空のコンテキストでも処理を続行
                }
                
                // 【TRACE】Source check
                if (searchResults.Count > 0)
                {
                    var firstResult = searchResults[0];
                    
                    if (firstResult.Id?.StartsWith("chunk_") == true)
                    {
                    }
                    else
                    {
                    }
                }
                
                // コンテキストの大きさをログに出力
                if (contextText.Length > 0) {
                    // 最初の200文字だけログに出力（長すぎるとログを圧迫するため）
                }
                
                // Claude AIを使用して回答を生成
                
                string answer = "";
                
                if (!string.IsNullOrEmpty(contextText))
                {
                    // 検索結果が取得できた場合はClaude APIを呼び出す
                    try
                    {
                        // message自体はユーザーの質問として扱う
                        string actualQuestion = message;
                        
                        // フォールバック用のデフォルトプロンプト（箇条書きスタイルは指定しない）
                        var defaultSystemPrompt = "あなたは文書を分析するアシスタントです。提供された情報に基づいて、ユーザーの指示通りの形式で回答してください。";
                        
                        // パラメータで受け取ったカスタムプロンプトがあれば使用
                        var systemPrompt = !string.IsNullOrEmpty(customSystemPrompt) 
                            ? customSystemPrompt 
                            : defaultSystemPrompt;
                        
                        
                        // プロンプトの作成
                        // ユーザーから送信されたメッセージをそのまま使用し、コンテキストのみを追加
                        var promptTemplate = @"
{0}

{1}

情報に基づいて回答を日本語で作成してください。関連する情報がない場合は「申し訳ありませんが、この質問に関する具体的な情報がありません」と回答してください。
";

                        // contextTextが空でない場合のみ、参考情報として表示
                        string formattedContext = string.IsNullOrEmpty(contextText.Trim()) 
                            ? "" 
                            : $"【参考情報】\n{contextText}";

                        // promptを更新（ユーザーの質問を含む新しいプロンプトを作成）
                        var prompt = string.Format(promptTemplate, actualQuestion, formattedContext);
                            
                        // 明示的なメッセージを出力
                        
                        // プロンプトをデバッグ出力（詳細情報）
                        
                        // Claude APIへのリクエスト直前に最終内容を詳細ログ出力
                        
                        // シノニム組み込み状況の最終確認
                        if (usedSynonyms.Count > 0)
                        {
                            
                            // プロンプト内にシノニム情報が含まれているかチェック
                            bool synonymsInPrompt = prompt.Contains("【関連語・シノニム】");
                        }
                        else
                        {
                        }
                        
                        
                        // AWS Bedrock Claude APIリクエストの準備
                        var requestData = new
                        {
                            anthropic_version = "bedrock-2023-05-31",
                            max_tokens = 1000,
                            system = systemPrompt,
                            messages = new[]
                            {
                                new { role = "user", content = prompt }
                            }
                        };
                        
                        var requestBody = JsonSerializer.Serialize(requestData);
                        
                        var invokeRequest = new InvokeModelRequest()
                        {
                            ModelId = _claudeModel,
                            Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody)),
                            ContentType = "application/json"
                        };
                        
                                                _logger.LogInformation("🤖 AWS Bedrock DocumentChat Claude API呼び出し開始:");
                        _logger.LogInformation("  📊 使用モデル: {Model}", _claudeModel);
                        _logger.LogInformation("  📊 AWSリージョン: {Region}", _awsRegion);
                        _logger.LogInformation("  📊 Bedrockクライアント状態: {ClientState}", _bedrockClient?.ToString() ?? "null");
                        _logger.LogInformation("  📊 メッセージ長: {MessageLength}", message.Length);
                        _logger.LogInformation("  📊 システムプロンプト長: {SystemPromptLength}", systemPrompt.Length);
                        
                        // AWS Bedrock APIリクエスト送信
                        var response = await _bedrockClient.InvokeModelAsync(invokeRequest);
                        
                        using var responseStream = response.Body;
                        var responseJson = await new StreamReader(responseStream).ReadToEndAsync();
                        
                        // 🔍 デバッグ: 実際のBedrock応答内容を確認
                        _logger.LogInformation("🔍 AWS Bedrock生レスポンス: {Response}", responseJson);
                        
                        var responseObject = JsonSerializer.Deserialize<BedrockClaudeResponse>(responseJson);
                        
                        // 🔍 デバッグ: デシリアライズ結果を確認
                        _logger.LogInformation("🔍 デシリアライズ結果: Content={HasContent}, ContentCount={Count}", 
                            responseObject?.Content != null, responseObject?.Content?.Count ?? 0);
                        
                        answer = responseObject?.Content?[0]?.Text ?? "申し訳ありませんが、回答の生成に失敗しました。";
                        _logger.LogInformation("✅ AWS Bedrock DocumentChat Claude API回答生成成功: {Length}文字", answer.Length);

                    }
                    catch (Exception ex)
                    {
                        // 🔍 詳細デバッグログ: 内側catchブロック
                        _logger.LogError("🔍 【詳細デバッグ】内側catchブロックでエラー捕捉:");
                        _logger.LogError("  📌 例外タイプ: {ExceptionType}", ex.GetType().Name);
                        _logger.LogError("  📌 エラーメッセージ: {ErrorMessage}", ex.Message);
                        _logger.LogError("  📌 使用モデル: {ModelId}", _claudeModel);
                        _logger.LogError("  📌 AWSリージョン: {AwsRegion}", _awsRegion);
                        _logger.LogError("  📌 スタックトレース: {StackTrace}", ex.StackTrace);
                        
                        answer = "申し訳ありませんが、回答の生成中にエラーが発生しました。";
                    }
                }
                else
                {
                    answer = "申し訳ありませんが、質問に関連する情報が見つかりませんでした。";
                }
                
                // 参照ソースとして含めるものを選択
                var sources = new List<Models.SearchResult>();
                
                // クライアントから送られたコンテキストを使用している場合は簡潔なソース情報を返す
                if (!string.IsNullOrEmpty(documentContext) && searchResults.Count == 1 && searchResults[0].Id == "client-context")
                {
                    // 表示時はクライアントのソース情報を優先的に返すためソースは空にする
                    sources = new List<Models.SearchResult>();
                }
                // チャンク検索結果がある場合、それをソースとして返す
                else if (searchResults.Count > 0 && searchResults.Exists(r => r.Id.StartsWith("chunk_")))
                {
                    sources = searchResults.Take(10).ToList();
                }
                else
                {
                    // 空のソースリストを返す（Azure Searchは使用しない）
                    sources = new List<Models.SearchResult>();
                }
                
                // 🔧 デバッグ: AWS Bedrock アジアリージョンモデル使用中であることを応答に含める
                if (!string.IsNullOrEmpty(answer) && _claudeModel.Contains("apac.anthropic"))
                {
                    answer += $"\n\n---\n*🌏 AWS Bedrock Asia Pacific Claude 4 Sonnet を使用中 ({_claudeModel})*";
                }
                
                return (answer, sources, usedSynonyms);
            }
                    catch (Exception ex)
        {
            // 🔍 詳細デバッグログ: 外側catchブロック（フォールバック処理）
            _logger.LogError("🔍 【詳細デバッグ】外側catchブロック - フォールバック処理開始:");
            _logger.LogError("  📌 例外タイプ: {ExceptionType}", ex.GetType().Name);
            _logger.LogError("  📌 原因メッセージ: {ErrorMessage}", ex.Message);
            _logger.LogError("  📌 失敗したメインモデル: {MainModel}", _claudeModel);
            _logger.LogError("  📌 フォールバックモデル: {FallbackModel}", _claudeModelFallback);
            _logger.LogError("  📌 現在のAWSリージョン: {AwsRegion}", _awsRegion);
            _logger.LogError("  📌 詳細スタックトレース: {StackTrace}", ex.StackTrace);
            
            _logger.LogError(ex, "❌ AWS Bedrock DocumentChat Claude API呼び出し失敗: {Error}. Fallbackモデルで再試行します", ex.Message);
                
                // AWS Bedrock Claude 3.7 フォールバック処理
                if (!string.IsNullOrEmpty(_claudeModelFallback) && _claudeModelFallback != _claudeModel)
                {
                    try
                    {
                        _logger.LogInformation("🔄 AWS Bedrock DocumentChat Fallback実行: Model={FallbackModel}", _claudeModelFallback);
                        
                        // 簡単なシステムプロンプト（フォールバック用）
                        var fallbackSystemPrompt = @"あなたは日本語での質問に対して、正確で有用な回答を提供するアシスタントです。
与えられた参照文書の情報に基づいて回答してください。
参照文書に関連情報がない場合は、その旨を明記してください。
回答は簡潔で理解しやすい形で提供してください。";

                        var fallbackUserMessage = string.IsNullOrEmpty(documentContext) 
                            ? message 
                            : $"参照文書:\n{documentContext}\n\n質問: {message}";

                        var fallbackRequestData = new
                        {
                            anthropic_version = "bedrock-2023-05-31",
                            max_tokens = 1000,
                            system = fallbackSystemPrompt,
                            messages = new[]
                            {
                                new { role = "user", content = fallbackUserMessage }
                            }
                        };
                        
                        var fallbackRequestBody = JsonSerializer.Serialize(fallbackRequestData);
                        
                        var fallbackInvokeRequest = new InvokeModelRequest()
                        {
                            ModelId = _claudeModelFallback,
                            Body = new MemoryStream(Encoding.UTF8.GetBytes(fallbackRequestBody)),
                            ContentType = "application/json"
                        };

                        var fallbackResponse = await _bedrockClient.InvokeModelAsync(fallbackInvokeRequest);
                        
                        using var fallbackResponseStream = fallbackResponse.Body;
                        var fallbackResponseJson = await new StreamReader(fallbackResponseStream).ReadToEndAsync();
                        var fallbackResponseObject = JsonSerializer.Deserialize<BedrockClaudeResponse>(fallbackResponseJson);

                        var fallbackAnswer = fallbackResponseObject?.Content?[0]?.Text ?? "回答を生成できませんでした。";
                        _logger.LogInformation("✅ AWS Bedrock DocumentChat Fallback成功: {Length}文字", fallbackAnswer.Length);
                        
                        // フォールバックモデル使用中であることを表示
                        fallbackAnswer += $"\n\n---\n*⚠️ AWS Bedrock Fallback Claude 3.5 Sonnet を使用 ({_claudeModelFallback})*";
                        
                        return (fallbackAnswer, new List<Models.SearchResult>(), new List<SynonymMatch>());
                    }
                                    catch (Exception fallbackEx)
                {
                    // 🔍 詳細デバッグログ: フォールバック処理内のエラー
                    _logger.LogError("🔍 【詳細デバッグ】フォールバック処理内でエラー発生:");
                    _logger.LogError("  📌 フォールバック例外タイプ: {ExceptionType}", fallbackEx.GetType().Name);
                    _logger.LogError("  📌 フォールバックエラーメッセージ: {ErrorMessage}", fallbackEx.Message);
                    _logger.LogError("  📌 使用したフォールバックモデル: {FallbackModel}", _claudeModelFallback);
                    _logger.LogError("  📌 フォールバック用AWSリージョン: {AwsRegion}", _awsRegion);
                    _logger.LogError("  📌 フォールバック詳細スタックトレース: {StackTrace}", fallbackEx.StackTrace);
                    
                    _logger.LogError(fallbackEx, "❌ AWS Bedrock DocumentChat Fallbackも失敗: {Error}", fallbackEx.Message);
                    }
                }
                
                var errorAnswer = "申し訳ありませんが、回答の生成中にエラーが発生しました。";
                
                // エラー時のモデル名表示
                if (_claudeModel.Contains("apac.anthropic"))
                {
                    errorAnswer += $"\n\n---\n*🌏 AWS Bedrock Asia Pacific Claude 4 Sonnet を使用中 ({_claudeModel})*";
                }
                
                return (errorAnswer, new List<Models.SearchResult>(), new List<SynonymMatch>());
            }
        }
        
        /// <summary>
        /// クエリに基づいてドキュメントを検索する（ユーザー権限対応）
        /// </summary>
        private async Task<List<Models.SearchResult>> SearchDocumentsAsync(string query, string username = null)
        {
            try
            {
                
                // 【重要・追跡】Azure Search APIの呼び出しをトレース
                
                // 🆕 ユーザー権限に基づく動的インデックス選択
                var userMainIndex = await GetUserMainIndexAsync(username);
                var url = $"{_searchEndpoint}/indexes/{userMainIndex}/docs/search?api-version={_apiVersion}";
                var searchRequest = new
                {
                    search = query,
                    searchFields = "content,title,filepath",
                    select = "id,filepath,title,content",
                    top = 10,
                    // 通常の検索モードを使用
                    queryType = "simple",
                    // 🚀 コンテンツサイズ制限でパフォーマンス改善
                    highlight = "content",
                    highlightPreTag = "<mark>",
                    highlightPostTag = "</mark>"
                };
                
                // リクエストの詳細をログに出力
                var searchRequestJson = JsonSerializer.Serialize(searchRequest);
                
                // リクエストヘッダーを再設定
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("api-key", _searchKey);
                
                var content = new StringContent(searchRequestJson, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync(url, content);
                
                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    
                    using (JsonDocument document = JsonDocument.Parse(jsonString))
                    {
                        var results = new List<Models.SearchResult>();
                        
                        // "value"プロパティが存在するか確認
                        if (document.RootElement.TryGetProperty("value", out var documentsElement))
                        {
                            var documents = documentsElement;
                            int documentCount = documents.GetArrayLength();
                            
                            foreach (var doc in documents.EnumerateArray())
                            {
                                var result = new Models.SearchResult();
                                
                                if (doc.TryGetProperty("id", out var id))
                                {
                                    result.Id = id.GetString();
                                }
                                
                                if (doc.TryGetProperty("filepath", out var filepath))
                                {
                                    result.Filepath = filepath.GetString();
                                }
                                
                                if (doc.TryGetProperty("title", out var title))
                                {
                                    result.Title = title.GetString();
                                }
                                
                                if (doc.TryGetProperty("content", out var contentElem))
                                {
                                    var fullContent = contentElem.GetString();
                                    // 🚀 コンテンツサイズ制限（最大2000文字）でパフォーマンス改善
                                    result.Content = fullContent?.Length > 2000 
                                        ? fullContent.Substring(0, 2000) + "..." 
                                        : fullContent;
                                }
                                
                                results.Add(result);
                                
                                // 最初のドキュメントの内容をログに出力（デバッグ用・サイズ制限）
                                if (results.Count == 1)
                                {
                                    if (!string.IsNullOrEmpty(result.Content))
                                    {
                                        var logContent = result.Content.Length > 200 
                                            ? result.Content.Substring(0, 200) + "..." 
                                            : result.Content;
                                        _logger.LogDebug("📄 検索結果コンテンツ（先頭200文字）: {Content}", logContent);
                                    }
                                }
                            }
                            
                        }
                        else
                        {
                        }
                        
                        return results;
                    }
                }
                else
                {
                    return new List<Models.SearchResult>();
                }
            }
            catch (Exception ex)
            {
                return new List<Models.SearchResult>();
            }
        }
        
        /// <summary>
        /// メッセージからキーワードを抽出する
        /// </summary>
        private List<string> ExtractKeywords(string message)
        {
            // 実際の質問を抽出（可能であれば）
            string actualQuestion = ExtractActualQuestion(message);
            
            // 簡易的なキーワード抽出（実際にはより高度な形態素解析などを使うとよい）
            var keywords = new List<string>();
            
            // 記号や一般的な助詞などを除去
            string cleanedMessage = actualQuestion
                .Replace("？", " ").Replace("?", " ")
                .Replace("。", " ").Replace("、", " ")
                .Replace(".", " ").Replace(",", " ")
                .Replace("「", " ").Replace("」", " ")
                .Replace("(", " ").Replace(")", " ")
                .Replace("（", " ").Replace("）", " ");
                
            // 分かち書き（単純なスペース区切り）
            string[] words = cleanedMessage.Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries);
            
            // ストップワード（除外する語）
            string[] stopWords = { "は", "が", "の", "に", "と", "で", "を", "や", "へ", "から", "より", "など", 
                "です", "ます", "でした", "ました", "について", "とは", "何", "どう", "こと", "もの",
                "ため", "する", "される", "した", "された", "いる", "ある", "れる", "なる", "できる",
                "年", "月", "日", "期", "第", "四半期", "2024", "2025" }; // 年月日関連の語を追加
            
            // 抽出されたキーワード候補をログに出力
            
            foreach (var word in words)
            {
                // 英語の場合は1文字以上、日本語の場合は2文字以上
                bool isValidLength = (IsEnglishWord(word) && word.Length >= 1) || (!IsEnglishWord(word) && word.Length >= 2);
                
                if (isValidLength && !stopWords.Contains(word))
                {
                    keywords.Add(word);
                }
                else if (isValidLength)
                {
                }
                else
                {
                }
            }
            
            // 英語は1文字以上、日本語は2文字以上のキーワードのみを取得し、重複を除去
            var result = keywords.Where(k => (IsEnglishWord(k) && k.Length >= 1) || (!IsEnglishWord(k) && k.Length >= 2))
                                .Distinct().ToList();
            
            // 【デバッグログ】最終的に抽出されたキーワード
            
            // 特に「cloud」キーワードの抽出状況をチェック
            if (actualQuestion.ToLower().Contains("cloud"))
            {
                bool cloudExtracted = result.Any(k => k.ToLower() == "cloud");
                
                if (!cloudExtracted)
                {
                    result.Add("cloud");
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// 英語の単語かどうかを判定
        /// </summary>
        private bool IsEnglishWord(string word)
        {
            if (string.IsNullOrEmpty(word)) return false;
            
            // 英語のアルファベットのみで構成されているかチェック
            return word.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'));
        }
        
        /// <summary>
        /// プロンプト全体から実際のユーザー質問部分を抽出する
        /// </summary>
        private string ExtractActualQuestion(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return string.Empty;
                
            try
            {
                // 1. 改行で終わる最後の行を質問とみなす
                string[] lines = message.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 0)
                {
                    string lastLine = lines[lines.Length - 1].Trim();
                    if (lastLine.EndsWith("？") || lastLine.EndsWith("?"))
                    {
                        return lastLine;
                    }
                }
                
                // 2. 文末が "？" で終わる最後の文を探す
                int lastQuestionMark = message.LastIndexOf('？');
                if (lastQuestionMark < 0)
                    lastQuestionMark = message.LastIndexOf('?');
                    
                if (lastQuestionMark >= 0)
                {
                    // 質問マークの前の最後の句点を見つける
                    int lastPeriod = message.LastIndexOf('。', lastQuestionMark);
                    if (lastPeriod < 0)
                        lastPeriod = message.LastIndexOf('.', lastQuestionMark);
                        
                    string question;
                    if (lastPeriod >= 0)
                    {
                        question = message.Substring(lastPeriod + 1, lastQuestionMark - lastPeriod).Trim();
                    }
                    else
                    {
                        // 質問マークの前のテキストで、長すぎない範囲で
                        int startPos = Math.Max(0, lastQuestionMark - 100);
                        question = message.Substring(startPos, lastQuestionMark - startPos + 1).Trim();
                    }
                    
                    return question;
                }
                
                // 3. どちらにも当てはまらない場合、末尾の50文字を使用
                if (message.Length <= 50)
                {
                    return message;
                }
                else
                {
                    string shortMessage = message.Substring(message.Length - 50);
                    return shortMessage;
                }
            }
            catch (Exception ex)
            {
                return message; // エラー時は元のメッセージを返す
            }
        }
        
        /// <summary>
        /// チャンクテキストからキーワードに基づいて検索を行う
        /// </summary>
        /// <summary>
        /// チャンクテキストからキーワードに基づいて検索を行う（ユーザー権限制御対応）
        /// </summary>
        /// <param name="message">ユーザーからのメッセージ</param>
        /// <param name="chunks">検索対象のチャンクリスト</param>
        /// <param name="username">認証ユーザー名</param>
        /// <returns>検索結果のリスト</returns>
        /// <exception cref="UnauthorizedAccessException">権限がないユーザーの場合</exception>
        public async Task<List<Models.SearchResult>> SearchChunksWithKeywords(string message, List<ChunkItem> chunks, string username)
        {
            try
            {
                // 🔐 ユーザー権限チェック
                if (string.IsNullOrEmpty(username))
                {
                    throw new UnauthorizedAccessException("ユーザー名が指定されていません。検索機能にはユーザー認証が必要です。");
                }

                // ユーザー権限チェック（メインインデックスアクセス権限で確認）
                await GetUserMainIndexAsync(username); // 権限がなければここで例外がスローされる
                
                _logger.LogInformation("✅ ユーザー権限確認済み、チャンク検索開始: {Username}", username);

                // 【重要：詳細ログ】開始ログ
                
                // チャンクデータの構造チェック - 最初の数件を詳細にログ出力
                if (chunks != null && chunks.Count > 0)
                {
                    for (int i = 0; i < Math.Min(3, chunks.Count); i++)
                    {
                        var chunk = chunks[i];
                        bool hasChunkContent = !string.IsNullOrEmpty(chunk.Chunk);
                        string chunkPreview = hasChunkContent ? chunk.Chunk.Substring(0, Math.Min(100, chunk.Chunk.Length)) : "<空>";
                        
                    }
                }
                
                // メッセージからキーワードを抽出
                var keywords = ExtractKeywords(message);
                
                // 前処理: メッセージ全体も検索対象として使用（複合語検索用）
                string fullMessage = message.Trim();
                // 実際の質問部分だけを抽出して完全一致検索に使用
                string actualQuestion = ExtractActualQuestion(message);
                
                if (keywords.Count == 0 || chunks == null || chunks.Count == 0)
                {
                    return new List<Models.SearchResult>();
                }
                
                // 各チャンクでキーワードの一致度をスコアリング
                var results = new List<(ChunkItem chunk, int score, List<string> matchedKeywords)>();
                
                // 【重要：追加】日本語のエンコーディングをUTF-8で適切に処理することを確認
                
                // 【デバッグ】チャンク検索の詳細ログ
                
                // 緊急対応：チャンクが空でなく、検索対象テキストが含まれる場合のカウント
                int validChunkCount = 0;
                
                foreach (var chunk in chunks)
                {
                    if (string.IsNullOrEmpty(chunk.Chunk))
                    {
                        continue;
                    }
                    
                    validChunkCount++;
                    int score = 0;
                    var matchedKeywords = new List<string>();
                    
                    // 【デバッグ】チャンク内容を詳細にログ
                    string chunkPreview = chunk.Chunk.Length > 100 ? chunk.Chunk.Substring(0, 100) + "..." : chunk.Chunk;
                    
                    // 標準的なキーワード検索 - より緩やかな比較を行う
                    foreach (var keyword in keywords)
                    {
                        // 1. 完全一致検索（大文字小文字を区別せず）
                        bool exactMatchFound = chunk.Chunk.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
                        
                        // 2. 部分一致検索（より緩やかな比較）- キーワードの文字が連続で含まれているか
                        bool partialMatch = false;
                        if (!exactMatchFound && keyword.Length >= 3)
                        {
                            // キーワードの文字が80%以上連続で含まれているか確認
                            for (int i = 0; i <= chunk.Chunk.Length - 3; i++)
                            {
                                if (i + keyword.Length > chunk.Chunk.Length) break;
                                
                                string subChunk = chunk.Chunk.Substring(i, Math.Min(keyword.Length, chunk.Chunk.Length - i));
                                if (IsSimilarText(subChunk, keyword, 0.8))
                                {
                                    partialMatch = true;
                                    break;
                                }
                            }
                        }
                        
                        if (exactMatchFound || partialMatch)
                        {
                            score += exactMatchFound ? 2 : 1; // 完全一致はより高いスコア
                            matchedKeywords.Add(keyword + (partialMatch ? "(部分一致)" : ""));
                            // 【デバッグ】キーワードマッチのログ
                        }
                    }
                    
                    // 実際の質問との類似度検索 - より柔軟に
                    if (!string.IsNullOrEmpty(actualQuestion))
                    {
                        // 1. 完全一致検索
                        bool exactQuestionMatch = chunk.Chunk.IndexOf(actualQuestion, StringComparison.OrdinalIgnoreCase) >= 0;
                        
                        // 2. 部分一致検索 - 質問文の主要部分が含まれているか
                        bool partialQuestionMatch = false;
                        if (!exactQuestionMatch)
                        {
                            // 質問の文字が60%以上連続で含まれているか確認（より緩やかな基準）
                            string[] questionWords = actualQuestion.Split(new[] { ' ', '　', '、', '。' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var word in questionWords.Where(w => w.Length >= 3))
                            {
                                if (chunk.Chunk.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    partialQuestionMatch = true;
                                    break;
                                }
                            }
                        }
                        
                        if (exactQuestionMatch)
                        {
                            // 実際の質問との完全一致は非常に重要
                            score += 10;
                            matchedKeywords.Add($"【完全一致】{actualQuestion.Substring(0, Math.Min(30, actualQuestion.Length))}...");
                            
                            // 完全一致が見つかった場合は詳細にログ出力
                        }
                        else if (partialQuestionMatch)
                        {
                            // 部分一致も価値あり
                            score += 5;
                            matchedKeywords.Add($"【部分一致】質問文の重要キーワードを含む");
                        }
                    }
                    
                    if (score > 0)
                    {
                        results.Add((chunk, score, matchedKeywords));
                        // 【重要：詳細ログ】マッチしたチャンクの情報をログ出力
                    }
                    else
                    {
                        // 【デバッグ】マッチしなかったチャンクのログ
                    }
                }
                
                // 有効なチャンク数をログ出力
                
                // スコア降順でソート（スコアが同じ場合はページ番号、チャンク番号で昇順）
                results = results.OrderByDescending(r => r.score)
                    .ThenBy(r => r.chunk.PageNo)
                    .ThenBy(r => r.chunk.ChunkNo)
                    .ToList();
                
                // 【重要：詳細ログ】ソート結果
                
                // 上位10件を選択して検索結果に変換
                var searchResults = results.Take(10).Select(r => 
                {
                    // マッチしたキーワードの文字列
                    string matchedKeywordsStr = string.Join(", ", r.matchedKeywords);
                    
                    // 【重要：詳細ログ】検索結果の詳細
                    
                    return new Models.SearchResult
                    {
                        Id = $"chunk_{r.chunk.PageNo}_{r.chunk.ChunkNo}",
                        Filepath = $"chunk_{r.chunk.PageNo}_{r.chunk.ChunkNo}",
                        Title = $"ページ {r.chunk.PageNo} (チャンク {r.chunk.ChunkNo}) - スコア: {r.score}",
                        Content = r.chunk.Chunk,
                        // 追加メタデータ
                        PageNumber = r.chunk.PageNo,
                        ChunkNumber = r.chunk.ChunkNo,
                        MatchedKeywords = matchedKeywordsStr
                    };
                }).ToList();

                // 検索結果がない場合は、キーワードをさらに分解して再検索
                if (searchResults.Count == 0 && keywords.Count > 0)
                {
                    
                    // キーワードをさらに細かく分解
                    var simpleKeywords = new List<string>();
                    foreach (var keyword in keywords)
                    {
                        // 3文字以上のキーワードは分解して1文字ずつ追加
                        if (keyword.Length >= 3)
                        {
                            for (int i = 0; i < keyword.Length - 1; i++)
                            {
                                string subKeyword = keyword.Substring(i, 2);
                                if (!simpleKeywords.Contains(subKeyword) && !string.IsNullOrWhiteSpace(subKeyword))
                                {
                                    simpleKeywords.Add(subKeyword);
                                }
                            }
                        }
                    }
                    
                    if (simpleKeywords.Count > 0)
                    {
                        
                        // シンプルキーワードでの検索結果
                        var simpleResults = new List<(ChunkItem chunk, int score, List<string> matchedKeywords)>();
                        
                        foreach (var chunk in chunks)
                        {
                            if (string.IsNullOrEmpty(chunk.Chunk))
                                continue;
                                
                            int score = 0;
                            var matchedKeywords = new List<string>();
                            
                            foreach (var keyword in simpleKeywords)
                            {
                                if (chunk.Chunk.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    score++;
                                    matchedKeywords.Add(keyword);
                                }
                            }
                            
                            if (score > 0)
                            {
                                simpleResults.Add((chunk, score, matchedKeywords));
                            }
                        }
                        
                        // シンプルキーワード検索の結果をソート
                        simpleResults = simpleResults.OrderByDescending(r => r.score)
                            .ThenBy(r => r.chunk.PageNo)
                            .ThenBy(r => r.chunk.ChunkNo)
                            .ToList();
                        
                        
                        // 上位5件を検索結果に変換
                        var simpleSearchResults = simpleResults.Take(5).Select(r => 
                        {
                            string matchedKeywordsStr = string.Join(", ", r.matchedKeywords);
                            
                            return new Models.SearchResult
                            {
                                Id = $"chunk_{r.chunk.PageNo}_{r.chunk.ChunkNo}",
                                Filepath = $"chunk_{r.chunk.PageNo}_{r.chunk.ChunkNo}",
                                Title = $"ページ {r.chunk.PageNo} (チャンク {r.chunk.ChunkNo}) - シンプル検索スコア: {r.score}",
                                Content = r.chunk.Chunk,
                                PageNumber = r.chunk.PageNo,
                                ChunkNumber = r.chunk.ChunkNo,
                                MatchedKeywords = matchedKeywordsStr + " (シンプル検索)"
                            };
                        }).ToList();
                        
                        // シンプル検索結果があれば追加
                        if (simpleSearchResults.Count > 0)
                        {
                            searchResults.AddRange(simpleSearchResults);
                        }
                    }
                }
                
                // 【重要：詳細ログ】最終検索結果
                foreach (var result in searchResults.Take(3)) // トップ3件だけログ出力
                {
                    
                    // 【重要：追加】日本語テキストが正しくUTF-8エンコードされていることを確認
                    byte[] contentBytes = Encoding.UTF8.GetBytes(result.Content.Substring(0, Math.Min(20, result.Content.Length)));
                }
                
                return searchResults;
            }
            catch (Exception ex)
            {
                return new List<Models.SearchResult>();
            }
        }
        
        /// <summary>
        /// 2つのテキストの類似度を計算する（0.0～1.0）
        /// </summary>
        private bool IsSimilarText(string text1, string text2, double threshold)
        {
            // 単純な部分一致チェック - より高速
            if (text1.Contains(text2) || text2.Contains(text1))
                return true;
                
            // 両方とも3文字未満なら、完全一致のみを考慮
            if (text1.Length < 3 || text2.Length < 3)
                return text1.Equals(text2, StringComparison.OrdinalIgnoreCase);
                
            // 文字の一致率を計算
            int matchCount = 0;
            string shorter = text1.Length <= text2.Length ? text1 : text2;
            string longer = text1.Length > text2.Length ? text1 : text2;
            
            for (int i = 0; i < shorter.Length; i++)
            {
                if (longer.IndexOf(shorter[i]) >= 0)
                    matchCount++;
            }
            
            double similarity = (double)matchCount / shorter.Length;
            return similarity >= threshold;
        }

        /// <summary>
        /// シノニムリストからキーワードに関連するシノニムを検索
        /// </summary>
        private List<SynonymMatch> FindSynonymsForKeywords(List<string> keywords, List<SynonymItem> synonymList)
        {
            var synonymMatches = new List<SynonymMatch>();
            
            if (synonymList == null || synonymList.Count == 0)
            {
                return synonymMatches;
            }
            
            
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

        /// <summary>
        /// シノニムを含めたクエリ拡張
        /// </summary>
        private string ExpandQueryWithSynonyms(string originalQuery, List<SynonymMatch> synonymMatches)
        {
            if (synonymMatches == null || synonymMatches.Count == 0)
            {
                return originalQuery;
            }
            
            var expandedQuery = new StringBuilder(originalQuery);
            expandedQuery.AppendLine("\n\n【関連語・シノニム】");
            
            foreach (var match in synonymMatches)
            {
                expandedQuery.AppendLine($"「{match.OriginalKeyword}」の関連語: {string.Join(", ", match.RelatedSynonyms)}");
            }
            
            
            return expandedQuery.ToString();
        }

    }
}