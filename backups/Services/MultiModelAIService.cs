using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AzureRag.Models.Settings;

namespace AzureRag.Services
{
    /// <summary>
    /// 複数モデル対応AI処理サービス実装
    /// </summary>
    public class MultiModelAIService : IMultiModelAIService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<MultiModelAIService> _logger;
        private readonly OpenAIClient _openAIClient;
        private readonly AzureOpenAISettings _openAISettings;
        private readonly ClaudeSettings _claudeSettings;
        private readonly HttpClient _httpClient;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public MultiModelAIService(IConfiguration configuration, ILogger<MultiModelAIService> logger, HttpClient httpClient)
        {
            _configuration = configuration;
            _logger = logger;
            _httpClient = httpClient;

            // Azure OpenAI設定
            _openAISettings = new AzureOpenAISettings
            {
                Endpoint = _configuration["AzureOpenAI:Endpoint"],
                ApiKey = _configuration["AzureOpenAI:ApiKey"],
                ApiVersion = _configuration["AzureOpenAI:ApiVersion"],
                Deployments = new DeploymentSettings
                {
                    Chat = _configuration["AzureOpenAI:Deployments:Chat"],
                    Embedding = _configuration["AzureOpenAI:Deployments:Embedding"]
                }
            };

            // Claude設定
            _claudeSettings = new ClaudeSettings
            {
                ApiKey = _configuration["Claude:ApiKey"],
                Model = _configuration["Claude:Model"]
            };

            // 設定が有効か確認
            if (string.IsNullOrEmpty(_openAISettings.Endpoint) || string.IsNullOrEmpty(_openAISettings.ApiKey))
            {
                throw new ArgumentException("Azure OpenAI configuration is incomplete");
            }

            // OpenAIクライアント初期化
            _openAIClient = new OpenAIClient(
                new Uri(_openAISettings.Endpoint),
                new AzureKeyCredential(_openAISettings.ApiKey));
        }

        /// <summary>
        /// 検索結果と質問に基づいて回答を生成
        /// </summary>
        public async Task<string> GenerateAnswerAsync(string query, string context)
        {
            try
            {
                _logger.LogInformation("質問に対する回答を生成中...");

                string systemPrompt = @"
あなたは保険に関する正確で有用な情報を提供するアシスタントです。
与えられたコンテキスト情報のみに基づいて質問に答えてください。
コンテキスト情報に答えがない場合は、「提供された情報からは回答できません」と正直に伝えてください。
与えられた情報の範囲内で、簡潔かつ明確に回答してください。
";

                // チャット完了リクエストを作成
                var chatCompletionsOptions = new ChatCompletionsOptions
                {
                    DeploymentName = _openAISettings.Deployments.Chat,
                    Messages =
                    {
                        new ChatMessage(ChatRole.System, systemPrompt),
                        new ChatMessage(ChatRole.User, $"以下の質問に答えてください: {query}\n\n参考情報:\n{context}")
                    },
                    Temperature = 0.7f,
                    MaxTokens = 500
                };

                // APIを呼び出し
                Response<ChatCompletions> response = await _openAIClient.GetChatCompletionsAsync(chatCompletionsOptions);

                // 応答から回答を抽出
                string answer = response.Value.Choices[0].Message.Content;
                _logger.LogInformation("回答生成完了");

                return answer;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "回答生成中にエラーが発生しました");
                return "回答の生成中にエラーが発生しました。";
            }
        }

        /// <summary>
        /// テキストをエンベディングベクトルに変換
        /// </summary>
        public async Task<IReadOnlyList<float>> GenerateEmbeddingAsync(string text)
        {
            try
            {
                _logger.LogInformation("テキストからエンベディングを生成中...");

                // エンベディングリクエストを作成
                var embeddingOptions = new EmbeddingsOptions
                {
                    DeploymentName = _openAISettings.Deployments.Embedding,
                    Input = { text }
                };

                // APIを呼び出し
                Response<Embeddings> response = await _openAIClient.GetEmbeddingsAsync(embeddingOptions);

                // エンベディングベクトルを取得
                IReadOnlyList<float> embeddings = response.Value.Data[0].Embedding;
                _logger.LogInformation($"エンベディング生成完了: {embeddings.Count}次元ベクトル");

                return embeddings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "エンベディング生成中にエラーが発生しました");
                throw;
            }
        }

        /// <summary>
        /// 画像からテキストを抽出（OCR）
        /// </summary>
        public async Task<string> ExtractTextFromImageAsync(string imagePath)
        {
            try
            {
                _logger.LogInformation($"画像からテキストを抽出中: {imagePath}");

                // Claude APIキーがない場合はエラー
                if (string.IsNullOrEmpty(_claudeSettings.ApiKey))
                {
                    _logger.LogError("Claude API configuration is incomplete");
                    return "Claude APIの設定が不完全なため、テキスト抽出を実行できません。";
                }

                // 画像ファイルを読み込み
                if (!File.Exists(imagePath))
                {
                    _logger.LogError($"画像ファイルが存在しません: {imagePath}");
                    return "画像ファイルが見つかりません。";
                }

                // 画像ファイルをBase64エンコード
                byte[] imageBytes = await File.ReadAllBytesAsync(imagePath);
                string base64Image = Convert.ToBase64String(imageBytes);

                // Claude APIリクエストボディを作成
                var requestBody = new
                {
                    model = _claudeSettings.Model,
                    max_tokens = 4096,
                    system = "あなたはOCRツールです。与えられた画像からすべてのテキストを抽出し、元のレイアウトをできるだけ保持してください。表や箇条書きなどの構造も維持してください。テキスト以外の説明は不要です。",
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = new object[]
                            {
                                new
                                {
                                    type = "image",
                                    source = new
                                    {
                                        type = "base64",
                                        media_type = "image/png",
                                        data = base64Image
                                    }
                                },
                                new
                                {
                                    type = "text",
                                    text = "この画像内のすべてのテキストを抽出してください。元のレイアウトをできるだけ保持し、表や箇条書きなどの構造も維持してください。"
                                }
                            }
                        }
                    }
                };

                // HTTP POSTリクエストを送信
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _claudeSettings.ApiKey);
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var content = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json");

                var response = await _httpClient.PostAsync("https://api.anthropic.com/v1/messages", content);

                // レスポンスを処理
                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    using var responseDoc = JsonDocument.Parse(jsonResponse);

                    // Claudeからの応答テキストを抽出
                    var content0 = responseDoc.RootElement
                        .GetProperty("content")
                        .EnumerateArray()
                        .First();

                    if (content0.TryGetProperty("text", out var textElement))
                    {
                        string extractedText = textElement.GetString();
                        _logger.LogInformation($"テキスト抽出完了: {extractedText.Length}文字");
                        return extractedText;
                    }
                }

                _logger.LogError($"Claude APIからのレスポンスエラー: {response.StatusCode}");
                return "画像からのテキスト抽出に失敗しました。";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "画像からのテキスト抽出中にエラーが発生しました");
                return "画像からのテキスト抽出中にエラーが発生しました。";
            }
        }
    }
}