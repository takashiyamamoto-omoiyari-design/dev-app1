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

        /// <summary>
        /// 画像+テキストを入力としてClaudeに投げ、テキスト応答を取得します（Bedrock経由）
        /// </summary>
        /// <param name="systemPrompt">システムプロンプト</param>
        /// <param name="userText">ユーザーテキスト（画像の説明や比較対象テキストなど）</param>
        /// <param name="imageBytes">画像（PNG/JPEG推奨）のバイト配列</param>
        /// <param name="imageFormat">"png" or "jpeg" 等</param>
        Task<string> GenerateVisionAsync(string systemPrompt, string userText, byte[] imageBytes, string imageFormat = "png");
    }
}