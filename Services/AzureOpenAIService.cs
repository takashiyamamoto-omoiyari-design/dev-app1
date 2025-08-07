using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AzureRag.Models;


namespace AzureRag.Services
{
    public class AzureOpenAIService : IAzureOpenAIService
    {
        private readonly OpenAIClient _openAIClient;
        private readonly string _deploymentName;
        private readonly ILogger<AzureOpenAIService> _logger;

        public AzureOpenAIService(IConfiguration configuration, ILogger<AzureOpenAIService> logger)
        {
            _logger = logger;
            
            // MSPSeimeiの設定を使用する
            var config = configuration.GetSection("MSPSeimei");
            string? openAIEndpoint = config["AzureOpenAIEndpoint"];
            string? openAIKey = config["AzureOpenAIKey"];
            _deploymentName = config["ChatModelDeployment"] ?? "";
            
            if (string.IsNullOrEmpty(openAIEndpoint) || string.IsNullOrEmpty(openAIKey) || string.IsNullOrEmpty(_deploymentName))
            {
                _logger.LogWarning("MSPSeimei設定が不完全なため、デフォルト設定を使用します");
                
                // デフォルト設定にフォールバック
                openAIEndpoint = configuration["AzureOpenAIEndpoint"];
                openAIKey = configuration["AzureOpenAIKey"];
                _deploymentName = configuration["AzureOpenAIDeploymentName"] ?? "";
                
                if (string.IsNullOrEmpty(openAIEndpoint) || string.IsNullOrEmpty(openAIKey) || string.IsNullOrEmpty(_deploymentName))
                {
                    throw new ArgumentException("Azure OpenAI configuration is incomplete");
                }
            }
            else
            {
                _logger.LogInformation("MSPSeimei設定を使用します: エンドポイント={0}, デプロイメント={1}", openAIEndpoint, _deploymentName);
            }

            _openAIClient = new OpenAIClient(
                new Uri(openAIEndpoint),
                new AzureKeyCredential(openAIKey));
        }

        /// <summary>
        /// 単一のクエリに対する回答を生成する
        /// </summary>
        public async Task<ChatAnswerResponse> GenerateAnswerAsync(string query, List<DocumentSearchResult> searchResults)
        {
            try
            {
                _logger.LogInformation($"回答生成のためのクエリ: {query}");

                // コンテキスト情報を構築
                StringBuilder contextBuilder = new StringBuilder();
                contextBuilder.AppendLine("以下の情報が参考として提供されています:");
                
                foreach (var result in searchResults)
                {
                    contextBuilder.AppendLine($"- タイトル: {result.Title}");
                    contextBuilder.AppendLine($"  内容: {result.Content}");
                    contextBuilder.AppendLine();
                }

                // ChatCompletionsを使用
                var chatCompletionsOptions = new ChatCompletionsOptions
                {
                    DeploymentName = _deploymentName,
                    MaxTokens = 500,
                    Temperature = 0.7f,
                    Messages =
                    {
                        new ChatRequestSystemMessage("あなたは与えられた情報に基づいて質問に答えるAIアシスタントです。提供された情報だけを元に適切な回答を生成してください。もし情報がない場合は、わからないと正直に答えてください。"),
                        new ChatRequestUserMessage($"以下の情報を元に質問に答えてください。\n\nコンテキスト情報:\n{contextBuilder}\n\n質問: {query}")
                    }
                };

                Response<ChatCompletions> chatResponse = await _openAIClient.GetChatCompletionsAsync(chatCompletionsOptions);
                string answer = chatResponse.Value.Choices[0].Message.Content;
                _logger.LogInformation($"生成された回答: {answer}");

                return new Models.ChatAnswerResponse
                {
                    Answer = answer
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "回答生成中にエラーが発生しました");
                throw;
            }
        }
        
        /// <summary>
        /// チャット履歴を考慮した回答を生成する
        /// </summary>
        public async Task<string> GenerateAnswerWithHistoryAsync(
            string query, 
            List<string> contexts, 
            string systemPrompt, 
            List<(string role, string content)> history)
        {
            try
            {
                _logger.LogInformation($"履歴を含む回答生成のためのクエリ: {query}, 履歴数: {history.Count}");
                
                // コンテキスト情報を構築
                StringBuilder contextBuilder = new StringBuilder();
                contextBuilder.AppendLine("以下の情報が参考として提供されています:");
                
                foreach (var context in contexts)
                {
                    // コンテキストの長さをチェック
                    string truncatedContext = context;
                    if (truncatedContext.Length > 1000)
                    {
                        truncatedContext = truncatedContext.Substring(0, 1000) + "...";
                    }
                    
                    contextBuilder.AppendLine($"- {truncatedContext}");
                    contextBuilder.AppendLine();
                }

                // ChatCompletionsの設定
                var chatCompletionsOptions = new ChatCompletionsOptions
                {
                    DeploymentName = _deploymentName,
                    MaxTokens = 800,
                    Temperature = 0.7f
                };
                
                // システムプロンプトを追加
                chatCompletionsOptions.Messages.Add(new ChatRequestSystemMessage(systemPrompt));
                
                // 過去の会話履歴を追加
                foreach (var (role, content) in history)
                {
                    if (role.Equals("user", StringComparison.OrdinalIgnoreCase))
                    {
                        chatCompletionsOptions.Messages.Add(new ChatRequestUserMessage(content));
                    }
                    else if (role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
                    {
                        chatCompletionsOptions.Messages.Add(new ChatRequestAssistantMessage(content));
                    }
                    // システムメッセージは既に追加済みなので無視
                }
                
                // 最後に現在のクエリとコンテキストを追加
                chatCompletionsOptions.Messages.Add(new ChatRequestUserMessage(
                    $"以下の情報を参考にして質問に答えてください。\n\n情報源:\n{contextBuilder}\n\n質問: {query}"));
                
                // ログ出力
                _logger.LogInformation($"会話履歴メッセージ数: {chatCompletionsOptions.Messages.Count}");
                
                // 回答を生成
                Response<ChatCompletions> chatResponse = await _openAIClient.GetChatCompletionsAsync(chatCompletionsOptions);
                string answer = chatResponse.Value.Choices[0].Message.Content;
                
                _logger.LogInformation($"生成された回答: {answer.Substring(0, Math.Min(50, answer.Length))}...");
                
                return answer;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "履歴を含む回答生成中にエラーが発生しました");
                throw;
            }
        }
    }
}
