using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using AzureRag.Models.Chat;
using AzureRag.Services;

namespace AzureRag.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ChatController : ControllerBase
    {
        private readonly IChatService _chatService;
        private readonly ILogger<ChatController> _logger;

        public ChatController(IChatService chatService, ILogger<ChatController> logger)
        {
            _chatService = chatService;
            _logger = logger;
        }

        /// <summary>
        /// チャットでのメッセージ送信とレスポンス生成
        /// </summary>
        /// <param name="request">チャットリクエスト</param>
        /// <returns>チャットレスポンス</returns>
        [HttpPost("send")]
        public async Task<ActionResult<ChatResponse>> SendMessage([FromBody] ChatRequest request)
        {
            _logger.LogInformation($"メッセージAPI呼び出し: クエリ「{request?.Query}」, セッションID: {request?.SessionId ?? "新規"}");
            
            // ユーザー認証チェック
            if (!User?.Identity?.IsAuthenticated ?? true)
            {
                _logger.LogWarning("未認証ユーザーによるチャット試行");
                return Unauthorized(new { error = "認証が必要です" });
            }
            
            if (request == null)
            {
                _logger.LogWarning("リクエストがnullです");
                return BadRequest(new { error = "リクエストデータが無効です" });
            }
            
            if (string.IsNullOrEmpty(request.Query))
            {
                _logger.LogWarning("クエリが空です");
                return BadRequest(new { error = "クエリは必須です" });
            }
            
            var currentUsername = User.Identity.Name;
            _logger.LogInformation($"認証済みユーザー '{currentUsername}' によるチャット実行");
            
            // カスタムプロンプトの処理（デバッグ出力）
            string customPrompt = request.SystemPrompt;
            if (string.IsNullOrEmpty(customPrompt) && !string.IsNullOrEmpty(request.Query) && request.Query.Contains("\n\n"))
            {
                var parts = request.Query.Split(new[] { "\n\n" }, 2, StringSplitOptions.None);
                if (parts.Length == 2)
                {
                    customPrompt = parts[0];
                    _logger.LogInformation("=================================================================");
                    _logger.LogInformation($"【ChatController】カスタムプロンプト検出: {customPrompt}");
                    _logger.LogInformation($"【ChatController】実際の質問: {parts[1]}");
                    _logger.LogInformation("=================================================================");
                }
            }
            
            // SessionIdとSystemPromptがnullの場合は空文字列に設定
            request.SessionId ??= string.Empty;
            request.SystemPrompt ??= string.Empty;
            
            try
            {
                _logger.LogInformation("ChatService.GenerateResponseAsyncを呼び出します...");
                
                // ユーザー情報をリクエストに追加
                request.Username = currentUsername;
                
                var response = await _chatService.GenerateResponseAsync(request);
                _logger.LogInformation($"ユーザー '{currentUsername}' のレスポンス生成完了: セッションID={response?.SessionId}, 回答文字数={response?.Answer?.Length ?? 0}, ソース数={response?.Sources?.Count ?? 0}");
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"メッセージ処理中にエラーが発生しました: {ex.Message}");
                _logger.LogError($"スタックトレース: {ex.StackTrace}");
                
                // 内部例外があれば記録
                var innerEx = ex.InnerException;
                while (innerEx != null)
                {
                    _logger.LogError($"内部エラー: {innerEx.Message}");
                    _logger.LogError($"内部スタックトレース: {innerEx.StackTrace}");
                    innerEx = innerEx.InnerException;
                }
                
                return StatusCode(500, new { error = $"エラーが発生しました: {ex.Message}" });
            }
        }

        /// <summary>
        /// チャットセッション一覧を取得
        /// </summary>
        /// <returns>チャットセッション一覧</returns>
        [HttpGet("sessions")]
        public async Task<ActionResult<List<ChatSession>>> GetSessions()
        {
            _logger.LogInformation("セッション一覧API呼び出し");
            
            try
            {
                var sessions = await _chatService.GetSessionsAsync();
                return Ok(sessions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "セッション一覧取得中にエラーが発生しました");
                return StatusCode(500, new { error = $"エラーが発生しました: {ex.Message}" });
            }
        }

        /// <summary>
        /// チャットセッションを取得
        /// </summary>
        /// <param name="id">セッションID</param>
        /// <returns>チャットセッション</returns>
        [HttpGet("sessions/{id}")]
        public async Task<ActionResult<ChatSession>> GetSession(string id)
        {
            _logger.LogInformation($"セッション取得API呼び出し: {id}");
            
            if (string.IsNullOrEmpty(id))
            {
                return BadRequest(new { error = "セッションIDは必須です" });
            }
            
            try
            {
                var session = await _chatService.GetSessionByIdAsync(id);
                if (session == null)
                {
                    return NotFound(new { error = $"セッションが見つかりません: {id}" });
                }
                
                return Ok(session);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"セッション取得中にエラーが発生しました: {id}");
                return StatusCode(500, new { error = $"エラーが発生しました: {ex.Message}" });
            }
        }

        /// <summary>
        /// 新しいチャットセッションを作成
        /// </summary>
        /// <param name="request">セッション作成リクエスト</param>
        /// <returns>作成されたチャットセッション</returns>
        [HttpPost("sessions")]
        public async Task<ActionResult<ChatSession>> CreateSession([FromBody] CreateSessionRequest request)
        {
            _logger.LogInformation("セッション作成API呼び出し");
            
            try
            {
                var session = await _chatService.CreateSessionAsync(request?.Name);
                return Ok(session);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "セッション作成中にエラーが発生しました");
                return StatusCode(500, new { error = $"エラーが発生しました: {ex.Message}" });
            }
        }

        /// <summary>
        /// チャットセッションを削除
        /// </summary>
        /// <param name="id">セッションID</param>
        /// <returns>削除結果</returns>
        [HttpDelete("sessions/{id}")]
        public async Task<ActionResult> DeleteSession(string id)
        {
            _logger.LogInformation($"セッション削除API呼び出し: {id}");
            
            if (string.IsNullOrEmpty(id))
            {
                return BadRequest(new { error = "セッションIDは必須です" });
            }
            
            try
            {
                var result = await _chatService.DeleteSessionAsync(id);
                if (!result)
                {
                    return NotFound(new { error = $"セッションが見つかりません: {id}" });
                }
                
                return Ok(new { success = true, message = $"セッションを削除しました: {id}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"セッション削除中にエラーが発生しました: {id}");
                return StatusCode(500, new { error = $"エラーが発生しました: {ex.Message}" });
            }
        }
    }

    /// <summary>
    /// セッション作成リクエスト
    /// </summary>
    public class CreateSessionRequest
    {
        /// <summary>
        /// セッション名（任意）
        /// </summary>
        public string? Name { get; set; }
    }
}
