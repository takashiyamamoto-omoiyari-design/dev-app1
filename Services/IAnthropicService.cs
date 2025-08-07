using System.Threading.Tasks;

namespace AzureRag.Services
{
    public interface IAnthropicService
    {
        /// <summary>
        /// テキストコンテンツをClaude AIで構造化します
        /// </summary>
        /// <param name="content">構造化するコンテンツ</param>
        /// <param name="systemPrompt">システムプロンプト（オプション）</param>
        /// <returns>構造化されたテキスト</returns>
        Task<string> StructureTextAsync(string content, string systemPrompt = null);

        /// <summary>
        /// Claude AIを使用してチャット回答を生成します
        /// </summary>
        /// <param name="question">質問</param>
        /// <param name="context">参照文書コンテキスト</param>
        /// <param name="systemPrompt">システムプロンプト（オプション）</param>
        /// <returns>チャット回答</returns>
        Task<string> GenerateChatResponseAsync(string question, string context, string systemPrompt = null);
    }
}