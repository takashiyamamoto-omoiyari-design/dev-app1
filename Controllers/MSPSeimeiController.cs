using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AzureRag.Services;

namespace AzureRag.Controllers
{
    [ApiController]
    [Route("api/mspseimei")]
    public class MSPSeimeiController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly ILogger<MSPSeimeiController> _logger;
        private readonly IAutoStructureService _autoStructureService;
        private readonly string _azureOpenAIEndpoint;
        private readonly string _azureOpenAIKey;
        private readonly string _azureOpenAIApiVersion;
        private readonly string _chatModelDeployment;

        public MSPSeimeiController(
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<MSPSeimeiController> logger,
            IAutoStructureService autoStructureService)
        {
            _configuration = configuration;
            _httpClient = httpClientFactory.CreateClient();
            _logger = logger;
            _autoStructureService = autoStructureService;

            // 設定を読み込み
            var config = _configuration.GetSection("MSPSeimei");
            _azureOpenAIEndpoint = config["AzureOpenAIEndpoint"];
            _azureOpenAIKey = config["AzureOpenAIKey"];
            _azureOpenAIApiVersion = config["AzureOpenAIApiVersion"];
            _chatModelDeployment = config["ChatModelDeployment"];
        }

        [HttpPost("generate-answer")]
        public async Task<IActionResult> GenerateAnswer([FromBody] GenerateAnswerRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.Query))
                {
                    return BadRequest(new { success = false, error = "質問が指定されていません" });
                }

                _logger.LogInformation($"質問を受信: {request.Query}");
                _logger.LogInformation($"カスタムプロンプト長: {request.Prompt?.Length ?? 0}文字");

                // AutoStructure APIからデータを取得
                var structuredData = await _autoStructureService.GetStructuredDataAsync("ff3bfb43437a02fde082fdc2af4a90e8");

                // 検索結果をフォーマット
                string context = FormatStructuredData(structuredData);

                // AIで回答を生成
                string answer = await GenerateAnswerWithAzureOpenAI(request.Query, context, request.Prompt);

                return Ok(new
                {
                    success = true,
                    answer = answer
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"回答生成中にエラーが発生しました: {ex.Message}");
                return StatusCode(500, new { success = false, error = "回答の生成中にエラーが発生しました" });
            }
        }

        private string FormatStructuredData(AutoStructureResponse data)
        {
            if (data == null || (data.TextList == null && data.SynonymList == null))
            {
                return "データはありませんでした。";
            }

            var formattedText = new StringBuilder("以下は関連する情報です:\n\n");

            // テキストリストの処理
            if (data.TextList != null && data.TextList.Count > 0)
            {
                formattedText.AppendLine("=== テキストリスト ===");
                foreach (var item in data.TextList)
                {
                    formattedText.AppendLine(item.Text);
                }
            }

            // 同義語リストの処理
            if (data.SynonymList != null && data.SynonymList.Count > 0)
            {
                formattedText.AppendLine("\n=== 同義語リスト ===");
                foreach (var item in data.SynonymList)
                {
                    formattedText.AppendLine(string.Join(", ", item.Synonyms));
                }
            }

            return formattedText.ToString();
        }

        private async Task<string> GenerateAnswerWithAzureOpenAI(string query, string context, string customPrompt)
        {
            _logger.LogInformation("回答を生成中...");

            try
            {
                // システムプロンプト
                string systemPrompt;
                
                if (!string.IsNullOrEmpty(customPrompt))
                {
                    // カスタムプロンプトを使用
                    systemPrompt = customPrompt;
                }
                else
                {
                    // デフォルトのシステムプロンプト
                    systemPrompt = @"
あなたは保険に関する正確で有用な情報を提供するアシスタントです。
与えられたコンテキスト情報のみに基づいて質問に答えてください。
コンテキスト情報に答えがない場合は、「提供された情報からは回答できません」と正直に伝えてください。
与えられた情報の範囲内で、簡潔かつ明確に回答してください。
";
                }

                // Azure OpenAI APIのエンドポイント
                string endpoint = $"{_azureOpenAIEndpoint}/openai/deployments/{_chatModelDeployment}/chat/completions?api-version={_azureOpenAIApiVersion}";

                // リクエストボディ
                var requestBody = new
                {
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = $"以下の質問に答えてください: {query}\n\n参考情報:\n{context}" }
                    },
                    temperature = 0.7,
                    max_tokens = 500
                };

                var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                // ヘッダーを設定
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("api-key", _azureOpenAIKey);

                // APIリクエストを送信
                var response = await _httpClient.PostAsync(endpoint, content);

                // レスポンスを処理
                if (response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    var responseJson = JsonSerializer.Deserialize<JsonElement>(responseBody);

                    if (responseJson.TryGetProperty("choices", out var choices) && 
                        choices.ValueKind == JsonValueKind.Array && 
                        choices.GetArrayLength() > 0)
                    {
                        var firstChoice = choices[0];
                        if (firstChoice.TryGetProperty("message", out var message) &&
                            message.TryGetProperty("content", out var contentElement))
                        {
                            return contentElement.GetString();
                        }
                    }

                    _logger.LogError("APIレスポンスから回答を抽出できませんでした");
                    return "回答の生成に失敗しました。";
                }
                else
                {
                    _logger.LogError($"APIリクエストが失敗しました: {response.StatusCode}");
                    _logger.LogError($"エラーレスポンス: {await response.Content.ReadAsStringAsync()}");
                    return "回答の生成に失敗しました。";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"回答生成中にエラーが発生しました: {ex.Message}");
                return "回答の生成中にエラーが発生しました。";
            }
        }
    }

    public class GenerateAnswerRequest
    {
        public string Query { get; set; }
        public string Prompt { get; set; }
    }
}