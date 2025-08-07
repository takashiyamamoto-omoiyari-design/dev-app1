using System.Collections.Generic;
using System.Threading.Tasks;
using AzureRag.Models.Index;

namespace AzureRag.Services.PDF
{
    public interface ITextChunkingService
    {
        /// <summary>
        /// ページテキストをClaudeでテキスト構造化し、tmpフォルダに保存します
        /// </summary>
        /// <param name="pageTexts">ページごとのテキスト</param>
        /// <param name="fileId">ファイルID</param>
        /// <returns>構造化したテキストファイルのパスのリスト</returns>
        Task<List<string>> StructureAndSavePageTextsAsync(Dictionary<int, string> pageTexts, string fileId);
        
        /// <summary>
        /// 構造化されたページテキストからチャンクを作成します
        /// </summary>
        /// <param name="structuredTextPaths">構造化されたテキストファイルのパス</param>
        /// <param name="fileId">ファイルID</param>
        /// <param name="maxTokens">最大トークン数（デフォルト800）</param>
        /// <returns>テキストチャンクのリスト</returns>
        Task<List<TextChunk>> CreateChunksFromStructuredTextsAsync(List<string> structuredTextPaths, string fileId, int maxTokens = 800);
        
        /// <summary>
        /// テキストをチャンクに分割します
        /// </summary>
        /// <param name="text">分割対象のテキスト</param>
        /// <param name="fileId">ファイルID</param>
        /// <param name="maxTokens">最大トークン数（デフォルト800）</param>
        /// <returns>テキストチャンクのリスト</returns>
        Task<List<TextChunk>> ChunkTextAsync(string text, string fileId, int maxTokens = 800);
        
        /// <summary>
        /// チャンクをファイルに保存します
        /// </summary>
        /// <param name="chunks">保存するチャンク</param>
        /// <param name="fileId">ファイルID</param>
        /// <returns>保存先のパス</returns>
        Task<string> SaveChunksToFileAsync(List<TextChunk> chunks, string fileId);
    }
}