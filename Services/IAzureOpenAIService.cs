using System.Collections.Generic;
using System.Threading.Tasks;
using AzureRag.Models;

namespace AzureRag.Services
{
    public interface IAzureOpenAIService
    {
        /// <summary>
        /// 単一のクエリに対する回答を生成する
        /// </summary>
        /// <param name="query">ユーザーからのクエリ</param>
        /// <param name="searchResults">検索結果ドキュメント</param>
        /// <returns>生成された回答</returns>
        Task<ChatAnswerResponse> GenerateAnswerAsync(string query, List<AzureRag.Models.DocumentSearchResult> searchResults);
        
        /// <summary>
        /// チャット履歴を考慮した回答を生成する
        /// </summary>
        /// <param name="query">最新のユーザーからのクエリ</param>
        /// <param name="contexts">検索結果コンテキスト</param>
        /// <param name="systemPrompt">システムプロンプト</param>
        /// <param name="history">過去のチャット履歴</param>
        /// <returns>生成された回答</returns>
        Task<string> GenerateAnswerWithHistoryAsync(
            string query, 
            List<string> contexts, 
            string systemPrompt, 
            List<(string role, string content)> history);
    }
}
