using System.Collections.Generic;
using System.Threading.Tasks;
using AzureRag.Models.Chat;

namespace AzureRag.Services
{
    /// <summary>
    /// チャット機能を提供するサービスのインターフェース
    /// </summary>
    public interface IChatService
    {
        /// <summary>
        /// チャットリクエストに対する回答を生成する
        /// </summary>
        /// <param name="request">チャットリクエスト</param>
        /// <returns>チャットレスポンス</returns>
        Task<ChatResponse> GenerateResponseAsync(ChatRequest request);
        
        /// <summary>
        /// チャットセッション一覧を取得する
        /// </summary>
        /// <returns>チャットセッション一覧</returns>
        Task<List<ChatSession>> GetSessionsAsync();
        
        /// <summary>
        /// チャットセッションをIDで取得する
        /// </summary>
        /// <param name="sessionId">セッションID</param>
        /// <returns>チャットセッション</returns>
        Task<ChatSession> GetSessionByIdAsync(string sessionId);
        
        /// <summary>
        /// 新しいチャットセッションを作成する
        /// </summary>
        /// <param name="name">セッション名（任意）</param>
        /// <returns>作成されたチャットセッション</returns>
        Task<ChatSession> CreateSessionAsync(string name = null);
        
        /// <summary>
        /// チャットセッションを削除する
        /// </summary>
        /// <param name="sessionId">セッションID</param>
        /// <returns>削除が成功したかどうか</returns>
        Task<bool> DeleteSessionAsync(string sessionId);
    }
}