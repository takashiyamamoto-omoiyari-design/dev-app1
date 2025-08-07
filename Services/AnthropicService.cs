using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon;
using System.IO;

namespace AzureRag.Services
{
    public class AnthropicService : IAnthropicService
    {
        private readonly ILogger<AnthropicService> _logger;
        private readonly AmazonBedrockRuntimeClient _bedrockClient;
        private readonly string _model;
        private readonly string _modelFallback;
        private readonly string _awsRegion;

        public AnthropicService(IConfiguration configuration, ILogger<AnthropicService> logger)
        {
            _logger = logger;
            
            // DataIngestion設定からClaude設定を取得
            var config = configuration.GetSection("DataIngestion");
            _model = config["ClaudeModel"] ?? "apac.anthropic.claude-sonnet-4-20250514-v1:0";
            _modelFallback = config["ClaudeModelFallback"] ?? "anthropic.claude-3-5-sonnet-20241022-v2:0"; 
            _awsRegion = config["AwsRegion"] ?? "ap-northeast-1";
            
            // AWS Bedrock RuntimeクライアントをEC2 IAMロールで初期化
            var regionEndpoint = RegionEndpoint.GetBySystemName(_awsRegion);
            _bedrockClient = new AmazonBedrockRuntimeClient(regionEndpoint);
            
            _logger.LogInformation("🔧 AnthropicService初期化 (AWS Bedrock):");
            _logger.LogInformation("  📊 メインモデル: {Model}", _model);
            _logger.LogInformation("  🔄 フォールバックモデル: {ModelFallback}", _modelFallback);
            _logger.LogInformation("  🌏 AWSリージョン: {Region}", _awsRegion);
            _logger.LogInformation("  🧪 テスト用無効モデル設定: {IsInvalid}", _model.Contains("INVALID"));
        }

        /// <summary>
        /// テキストをClaude AIで構造化します（AWS Bedrock版）
        /// </summary>
        public async Task<string> StructureTextAsync(string content, string systemPrompt = null)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("構造化するコンテンツが空です");
                return string.Empty;
            }

            // デフォルトのシステムプロンプト（スコープをメソッド全体に拡張）
            string effectiveSystemPrompt = systemPrompt ?? 
                "あなたはPDFコンテンツを構造化して整理するエキスパートです。提供されたPDFのテキストを分析し、以下の形式で整理してください：" +
                "1. 明確な段落分け " +
                "2. 箇条書きリストの適切なフォーマット " +
                "3. 表組みの適切な再構成 " +
                "4. 見出しの識別と階層付け " +
                "5. 余分な改行やスペースの除去 " +
                "オリジナルのテキスト内容は完全に保持し、内容を要約したり変更したりせず、純粋に構造と読みやすさを改善してください。";

            try
            {

                // AWS Bedrockリクエスト形式
                var request = new
                {
                    anthropic_version = "bedrock-2023-05-31",
                    max_tokens = 4096,
                    system = effectiveSystemPrompt,
                    messages = new[]
                    {
                        new { role = "user", content = content }
                    }
                };

                var requestBody = JsonSerializer.Serialize(request);
                
                var invokeRequest = new InvokeModelRequest()
                {
                    ModelId = _model,
                    Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody)),
                    ContentType = "application/json"
                };

                _logger.LogInformation("🤖 AWS Bedrock Claude API呼び出し開始 (StructureText):");
                _logger.LogInformation("  📊 使用モデル: {Model}", _model);
                _logger.LogInformation("  🧪 無効モデルテスト中: {IsInvalid}", _model.Contains("INVALID"));
                
                var response = await _bedrockClient.InvokeModelAsync(invokeRequest);
                
                using var responseStream = response.Body;
                var responseJson = await new StreamReader(responseStream).ReadToEndAsync();
                var responseObject = JsonSerializer.Deserialize<BedrockClaudeResponse>(responseJson);

                var result = responseObject?.Content?[0]?.Text ?? string.Empty;
                _logger.LogInformation("✅ AWS Bedrock Claude API構造化成功: {Length}文字", result.Length);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AWS Bedrock テキスト構造化中にエラーが発生しました: {Error}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Claude AIを使用してチャット回答を生成します（AWS Bedrock版）
        /// </summary>
        public async Task<string> GenerateChatResponseAsync(string question, string context, string systemPrompt = null)
        {
            if (string.IsNullOrWhiteSpace(question))
            {
                _logger.LogWarning("質問が空です");
                return "質問を入力してください。";
            }

            // デフォルトのシステムプロンプト（スコープをメソッド全体に拡張）
            string effectiveSystemPrompt = systemPrompt ?? @"
あなたは日本語での質問に対して、正確で有用な回答を提供するアシスタントです。
与えられた参照文書の情報に基づいて回答してください。
参照文書に関連情報がない場合は、その旨を明記してください。
回答は簡潔で理解しやすい形で提供してください。";

            string userMessage = string.IsNullOrEmpty(context) 
                ? question 
                : $"参照文書:\n{context}\n\n質問: {question}";

            try
            {

                // AWS Bedrockリクエスト形式
                var request = new
                {
                    anthropic_version = "bedrock-2023-05-31",
                    max_tokens = 1000,
                    system = effectiveSystemPrompt,
                    messages = new[]
                    {
                        new { role = "user", content = userMessage }
                    }
                };

                var requestBody = JsonSerializer.Serialize(request);
                
                var invokeRequest = new InvokeModelRequest()
                {
                    ModelId = _model,
                    Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody)),
                    ContentType = "application/json"
                };

                _logger.LogInformation("🤖 AWS Bedrock Claude API呼び出し開始 (GenerateChat):");
                _logger.LogInformation("  📊 使用モデル: {Model}", _model);
                _logger.LogInformation("  🧪 無効モデルテスト中: {IsInvalid}", _model.Contains("INVALID"));
                
                var response = await _bedrockClient.InvokeModelAsync(invokeRequest);
                
                using var responseStream = response.Body;
                var responseJson = await new StreamReader(responseStream).ReadToEndAsync();
                var responseObject = JsonSerializer.Deserialize<BedrockClaudeResponse>(responseJson);

                var answer = responseObject?.Content?[0]?.Text ?? "回答を生成できませんでした。";
                _logger.LogInformation("✅ AWS Bedrock Claude API回答生成成功: {Length}文字", answer.Length);
                
                // 🔧 デバッグ: アジアリージョンモデル使用中であることを応答に含める
                if (_model.Contains("apac.anthropic"))
                {
                    answer += $"\n\n---\n*🌏 AWS Bedrock Asia Pacific Claude 4 Sonnet を使用中 ({_model})*";
                }
                
                return answer;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ AWS Bedrock Claude API呼び出し失敗: {Error}. Fallbackモデルで再試行します", ex.Message);

                // AWS Bedrock Claude 3.7 フォールバック処理
                if (!string.IsNullOrEmpty(_modelFallback) && _modelFallback != _model)
                {
                    try
                    {
                        _logger.LogInformation("🔄 AWS Bedrock Fallback実行: Model={FallbackModel}", _modelFallback);
                        
                        var fallbackRequest = new
                        {
                            anthropic_version = "bedrock-2023-05-31",
                            max_tokens = 1000,
                            system = effectiveSystemPrompt,
                            messages = new[]
                            {
                                new { role = "user", content = userMessage }
                            }
                        };

                        var fallbackRequestBody = JsonSerializer.Serialize(fallbackRequest);
                        
                        var fallbackInvokeRequest = new InvokeModelRequest()
                        {
                            ModelId = _modelFallback,
                            Body = new MemoryStream(Encoding.UTF8.GetBytes(fallbackRequestBody)),
                            ContentType = "application/json"
                        };

                        var fallbackResponse = await _bedrockClient.InvokeModelAsync(fallbackInvokeRequest);
                        
                        using var fallbackResponseStream = fallbackResponse.Body;
                        var fallbackResponseJson = await new StreamReader(fallbackResponseStream).ReadToEndAsync();
                        var fallbackResponseObject = JsonSerializer.Deserialize<BedrockClaudeResponse>(fallbackResponseJson);

                        var fallbackAnswer = fallbackResponseObject?.Content?[0]?.Text ?? "回答を生成できませんでした。";
                        _logger.LogInformation("✅ AWS Bedrock Fallback成功: {Length}文字", fallbackAnswer.Length);
                        
                        // フォールバックモデル使用中であることを表示
                        fallbackAnswer += $"\n\n---\n*⚠️ AWS Bedrock Fallback Claude 3.5 Sonnet を使用 ({_modelFallback})*";
                        
                        return fallbackAnswer;
                    }
                    catch (Exception fallbackEx)
                    {
                        _logger.LogError(fallbackEx, "❌ AWS Bedrock Fallbackも失敗: {Error}", fallbackEx.Message);
                        return "申し訳ありませんが、AIサービスに接続できませんでした。";
                    }
                }
                else
                {
                    return "申し訳ありませんが、AIサービスに接続できませんでした。";
                }
            }
        }

        public void Dispose()
        {
            _bedrockClient?.Dispose();
        }
    }

    /// <summary>
    /// AWS Bedrock Claude APIレスポンス用モデル
    /// </summary>
    public class BedrockClaudeResponse
    {
        [JsonPropertyName("content")]
        public List<BedrockClaudeContent>? Content { get; set; }
        
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        
        [JsonPropertyName("model")]
        public string? Model { get; set; }
        
        [JsonPropertyName("role")]
        public string? Role { get; set; }
        
        [JsonPropertyName("stop_reason")]
        public string? StopReason { get; set; }
        
        [JsonPropertyName("stop_sequence")]
        public string? StopSequence { get; set; }
        
        [JsonPropertyName("type")]
        public string? Type { get; set; }
        
        [JsonPropertyName("usage")]
        public BedrockClaudeUsage? Usage { get; set; }
    }

    /// <summary>
    /// AWS Bedrock Claude APIレスポンスのコンテンツ用モデル
    /// </summary>
    public class BedrockClaudeContent
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
        
        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }

    /// <summary>
    /// AWS Bedrock Claude APIレスポンスの使用量用モデル
    /// </summary>
    public class BedrockClaudeUsage
    {
        [JsonPropertyName("input_tokens")]
        public int? InputTokens { get; set; }
        
        [JsonPropertyName("output_tokens")]
        public int? OutputTokens { get; set; }
    }
}