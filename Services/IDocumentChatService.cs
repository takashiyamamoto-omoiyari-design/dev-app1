using System.Collections.Generic;
using System.Threading.Tasks;
using AzureRag.Models;
using static AzureRag.Services.AutoStructureService; // ChunkItemクラスをインポート

namespace AzureRag.Services
{
    public interface IDocumentChatService
    {
        /// <summary>
        /// クエリに基づいて回答を生成する（ユーザー権限制御対応）
        /// </summary>
        /// <param name="message">ユーザーからのメッセージ</param>
        /// <param name="documentContext">追加のドキュメントコンテキスト（オプション）</param>
        /// <param name="customSystemPrompt">カスタムシステムプロンプト（オプション）</param>
        /// <param name="username">認証ユーザー名</param>
        /// <returns>生成された回答と検索結果のソースのタプル</returns>
        /// <exception cref="UnauthorizedAccessException">権限がないユーザーの場合</exception>
        Task<(string answer, List<SearchResult> sources)> GenerateAnswer(string message, string documentContext, string customSystemPrompt = null, string username = null);

        /// <summary>
        /// ユーザー権限に基づいてインデックスから一意のファイルパスのリストを取得する
        /// </summary>
        /// <param name="username">認証ユーザー名</param>
        /// <returns>ファイルパスのリスト</returns>
        /// <exception cref="UnauthorizedAccessException">権限がないユーザーの場合</exception>
        Task<List<string>> GetUniqueFilePaths(string username);

        /// <summary>
        /// ユーザー権限に基づいて指定されたファイルパスに対応するドキュメントの内容を取得する
        /// </summary>
        /// <param name="filepath">ファイルパス</param>
        /// <param name="username">認証ユーザー名</param>
        /// <returns>ドキュメントの内容</returns>
        /// <exception cref="UnauthorizedAccessException">権限がないユーザーの場合</exception>
        Task<string> GetDocumentsByFilePath(string filepath, string username);
        
        /// <summary>
        /// チャンクテキストからキーワードに基づいて検索を行う
        /// </summary>
        /// <param name="message">ユーザーからのメッセージ</param>
        /// <param name="chunks">検索対象のチャンクリスト</param>
        /// <param name="username">認証ユーザー名</param>
        /// <returns>検索結果のリスト</returns>
        /// <exception cref="UnauthorizedAccessException">権限がないユーザーの場合</exception>
        Task<List<SearchResult>> SearchChunksWithKeywords(string message, List<ChunkItem> chunks, string username);
    }
}