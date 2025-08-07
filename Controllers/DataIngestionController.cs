using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using AzureRag.Services;

namespace AzureRag.Controllers
{
    [ApiController]
[Route("api/[controller]")]
[Authorize]
public class DataIngestionController : ControllerBase
{
    private readonly ILogger<DataIngestionController> _logger;
    private readonly IDataIngestionService _dataIngestionService;
    private readonly Services.IAuthorizationService _authorizationService;

    public DataIngestionController(
        ILogger<DataIngestionController> logger,
        IDataIngestionService dataIngestionService,
        Services.IAuthorizationService authorizationService)
    {
        _logger = logger;
        _dataIngestionService = dataIngestionService;
        _authorizationService = authorizationService;
    }

        /// <summary>
        /// ユーザー認証
        /// </summary>
        [HttpPost("auth/login")]
        [AllowAnonymous]
        public async Task<IActionResult> LoginAsync([FromBody] LoginRequest request)
        {
            try
            {
                _logger.LogInformation("ログイン試行: ユーザー名={Username}", request.Username);

                if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
                {
                    return BadRequest(new { message = "ユーザー名とパスワードを入力してください" });
                }

                var userInfo = await _authorizationService.AuthenticateUserAsync(request.Username, request.Password);

                if (userInfo == null)
                {
                    _logger.LogWarning("ログイン失敗: ユーザー名={Username}", request.Username);
                    return Unauthorized(new { message = "ユーザー名またはパスワードが間違っています" });
                }

                _logger.LogInformation("ログイン成功: ユーザー名={Username}, ロール={Role}", userInfo.Username, userInfo.Role);

                return Ok(new
                {
                    success = true,
                    user = new
                    {
                        username = userInfo.Username,
                        role = userInfo.Role,
                        allowedWorkIds = userInfo.AllowedWorkIds,
                        isAdmin = userInfo.IsAdmin
                    },
                    message = "ログインに成功しました"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ログイン処理でエラーが発生しました");
                return StatusCode(500, new { message = "サーバーエラーが発生しました" });
            }
        }

        /// <summary>
        /// ユーザーの新しいworkIdを登録（アップロード後に呼び出し）
        /// </summary>
        [HttpPost("workids/register")]
        public async Task<IActionResult> RegisterWorkIdAsync([FromBody] RegisterWorkIdRequest request)
        {
            try
            {
                _logger.LogInformation("workId登録: ユーザー名={Username}, workId={WorkId}, ファイル={FileName}", 
                    request.Username, request.WorkId, request.FileName);

                if (string.IsNullOrEmpty(request.Username))
                {
                    return BadRequest(new { message = "ユーザー名が必要です" });
                }

                if (string.IsNullOrEmpty(request.WorkId))
                {
                    return BadRequest(new { message = "workIdが必要です" });
                }

                var result = await _authorizationService.AddWorkIdToUserAsync(
                    request.Username, 
                    request.WorkId, 
                    request.FileName, 
                    request.Description
                );

                if (result)
                {
                    return Ok(new
                    {
                        success = true,
                        message = $"workId '{request.WorkId}' をユーザー '{request.Username}' に登録しました",
                        workId = request.WorkId,
                        username = request.Username
                    });
                }
                else
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = $"workId '{request.WorkId}' の登録に失敗しました"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "workId登録でエラーが発生しました");
                return StatusCode(500, new { message = "サーバーエラーが発生しました" });
            }
        }

        /// <summary>
        /// 利用可能なworkIdリストを取得（認証必須）
        /// </summary>
        [HttpGet("workids")]
        public async Task<IActionResult> GetAvailableWorkIdsAsync([FromQuery] string username)
        {
            try
            {
                _logger.LogInformation("利用可能workIdリスト取得: ユーザー名={Username}", username);

                if (string.IsNullOrEmpty(username))
                {
                    return BadRequest(new { message = "ユーザー名が必要です" });
                }

                var workIds = await _dataIngestionService.GetAvailableWorkIdsAsync(username);

                return Ok(new
                {
                    success = true,
                    workIds = workIds,
                    count = workIds.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "workIdリスト取得でエラーが発生しました");
                return StatusCode(500, new { message = "サーバーエラーが発生しました" });
            }
        }

        /// <summary>
        /// 利用可能なworkIdメタデータリストを取得（認証必須）
        /// </summary>
        [HttpGet("workids/metadata")]
        public async Task<IActionResult> GetAvailableWorkIdMetadataAsync([FromQuery] string username)
        {
            try
            {
                _logger.LogInformation("利用可能workIdメタデータリスト取得: ユーザー名={Username}", username);

                if (string.IsNullOrEmpty(username))
                {
                    return BadRequest(new { message = "ユーザー名が必要です" });
                }

                var workIdMetadata = await _dataIngestionService.GetAvailableWorkIdMetadataAsync(username);

                return Ok(new
                {
                    success = true,
                    workIdMetadata = workIdMetadata,
                    count = workIdMetadata.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "workIdメタデータリスト取得でエラーが発生しました");
                return StatusCode(500, new { message = "サーバーエラーが発生しました" });
            }
        }

        /// <summary>
        /// 指定されたworkIdからチャンクデータを取得（認証必須）
        /// </summary>
        [HttpGet("chunks/{workId}")]
        public async Task<IActionResult> GetChunksAsync(string workId, [FromQuery] string username)
        {
            try
            {
                _logger.LogInformation("チャンクデータ取得: ユーザー名={Username}, workId={WorkId}", username, workId);

                if (string.IsNullOrEmpty(username))
                {
                    return BadRequest(new { message = "ユーザー名が必要です" });
                }

                if (string.IsNullOrEmpty(workId))
                {
                    return BadRequest(new { message = "workIdが必要です" });
                }

                var chunks = await _dataIngestionService.GetChunksFromExternalApiAsync(username, workId);

                return Ok(new
                {
                    success = true,
                    workId = workId,
                    chunks = chunks,
                    count = chunks.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "チャンクデータ取得でエラーが発生しました");
                return StatusCode(500, new { message = "サーバーエラーが発生しました" });
            }
        }

        /// <summary>
        /// 指定されたworkIdのチャンクデータをAzure Searchに登録（認証必須・差分登録対応）
        /// </summary>
        [HttpPost("index/{workId}")]
        public async Task<IActionResult> IndexWorkIdAsync(string workId, [FromBody] IndexRequest request)
        {
            try
            {
                _logger.LogInformation("Azure Searchインデックス登録: ユーザー名={Username}, workId={WorkId}", request.Username, workId);

                if (string.IsNullOrEmpty(request.Username))
                {
                    return BadRequest(new { message = "ユーザー名が必要です" });
                }

                if (string.IsNullOrEmpty(workId))
                {
                    return BadRequest(new { message = "workIdが必要です" });
                }

                // 🔍 差分登録: 既にAzure Searchに登録済みかチェック
                var azureSearchService = HttpContext.RequestServices.GetRequiredService<IAzureSearchService>();
                var isAlreadyIndexed = await azureSearchService.IsWorkIdIndexedAsync(workId);
                
                if (isAlreadyIndexed)
                {
                    _logger.LogInformation("workId {WorkId}: 既に登録済みのためスキップ", workId);
                    return Ok(new
                    {
                        success = true,
                        workId = workId,
                        processedChunks = 0,
                        skipped = true,
                        message = $"workId '{workId}' は既にAzure Searchに登録済みです"
                    });
                }

                var result = await _dataIngestionService.ProcessWorkIdAsync(request.Username, workId);

                if (result.success)
                {
                    return Ok(new
                    {
                        success = true,
                        workId = workId,
                        processedChunks = result.processedChunks,
                        skipped = false,
                        message = $"workId '{workId}' のインデックス登録が完了しました（{result.processedChunks}件のチャンクを処理）"
                    });
                }
                else
                {
                    return BadRequest(new
                    {
                        success = false,
                        workId = workId,
                        processedChunks = result.processedChunks,
                        skipped = false,
                        message = result.errorMessage
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "インデックス登録でエラーが発生しました");
                return StatusCode(500, new { message = "サーバーエラーが発生しました" });
            }
        }

        /// <summary>
        /// 指定されたworkIdを強制的に両インデックスに再登録（認証必須・既存データも対象）
        /// </summary>
        [HttpPost("reindex/{workId}")]
        public async Task<IActionResult> ReindexWorkIdAsync(string workId, [FromBody] IndexRequest request)
        {
            try
            {
                _logger.LogInformation("Azure Search強制再登録: ユーザー名={Username}, workId={WorkId}", request.Username, workId);

                if (string.IsNullOrEmpty(request.Username))
                {
                    return BadRequest(new { message = "ユーザー名が必要です" });
                }

                if (string.IsNullOrEmpty(workId))
                {
                    return BadRequest(new { message = "workIdが必要です" });
                }

                // 🔥 強制再登録: 既存データチェックをスキップして両インデックスに登録
                var result = await _dataIngestionService.ProcessWorkIdAsync(request.Username, workId);

                if (result.success)
                {
                    return Ok(new
                    {
                        success = true,
                        workId = workId,
                        processedChunks = result.processedChunks,
                        forced = true,
                        message = $"workId '{workId}' を両インデックス（oec + oec-sentence）に強制再登録しました（{result.processedChunks}件のチャンクを処理）"
                    });
                }
                else
                {
                    return BadRequest(new
                    {
                        success = false,
                        workId = workId,
                        processedChunks = result.processedChunks,
                        forced = true,
                        message = result.errorMessage
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "強制再登録でエラーが発生しました");
                return StatusCode(500, new { message = "サーバーエラーが発生しました" });
            }
        }

        /// <summary>
        /// 複数のworkIdを一括でインデックス登録（認証必須・差分登録対応）
        /// </summary>
        [HttpPost("index/batch")]
        public async Task<IActionResult> IndexBatchAsync([FromBody] BatchIndexRequest request)
        {
            try
            {
                _logger.LogInformation("一括インデックス登録: ユーザー名={Username}, workId数={Count}", request.Username, request.WorkIds?.Count ?? 0);

                if (string.IsNullOrEmpty(request.Username))
                {
                    return BadRequest(new { message = "ユーザー名が必要です" });
                }

                if (request.WorkIds == null || request.WorkIds.Count == 0)
                {
                    return BadRequest(new { message = "処理するworkIdが必要です" });
                }

                // 🔍 差分登録: 既にAzure Searchに登録済みのworkIdを除外
                var azureSearchService = HttpContext.RequestServices.GetRequiredService<IAzureSearchService>();
                var existingWorkIds = await azureSearchService.GetExistingWorkIdsAsync(request.WorkIds);
                var newWorkIds = request.WorkIds.Except(existingWorkIds).ToList();
                
                _logger.LogInformation("workId差分チェック結果: 既存={ExistingCount}件, 新規={NewCount}件", existingWorkIds.Count, newWorkIds.Count);

                var results = new List<object>();
                int totalProcessedChunks = 0;
                int successCount = 0;
                int failureCount = 0;
                int skippedCount = 0;

                // 既存workIdはスキップ
                foreach (var existingWorkId in existingWorkIds)
                {
                    results.Add(new
                    {
                        workId = existingWorkId,
                        success = true,
                        processedChunks = 0,
                        skipped = true,
                        message = "既にAzure Searchに登録済みのためスキップ"
                    });
                    skippedCount++;
                    _logger.LogInformation("workId {WorkId}: 既に登録済みのためスキップ", existingWorkId);
                }

                // 新規workIdのみ処理
                foreach (var workId in newWorkIds)
                {
                    var result = await _dataIngestionService.ProcessWorkIdAsync(request.Username, workId);
                    
                    results.Add(new
                    {
                        workId = workId,
                        success = result.success,
                        processedChunks = result.processedChunks,
                        skipped = false,
                        errorMessage = result.errorMessage
                    });

                    if (result.success)
                    {
                        successCount++;
                        totalProcessedChunks += result.processedChunks;
                    }
                    else
                    {
                        failureCount++;
                    }
                }

                return Ok(new
                {
                    success = failureCount == 0,
                    totalWorkIds = request.WorkIds.Count,
                    successCount = successCount,
                    failureCount = failureCount,
                    skippedCount = skippedCount,
                    totalProcessedChunks = totalProcessedChunks,
                    results = results,
                    message = $"一括インデックス登録完了: 成功 {successCount}件, 失敗 {failureCount}件, スキップ {skippedCount}件, 総チャンク数 {totalProcessedChunks}件"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "一括インデックス登録でエラーが発生しました");
                return StatusCode(500, new { message = "サーバーエラーが発生しました" });
            }
        }

        /// <summary>
        /// workIdアクセス権限チェック（認証必須）
        /// </summary>
        [HttpGet("access-check")]
        public async Task<IActionResult> CheckAccessAsync([FromQuery] string username, [FromQuery] string workId)
        {
            try
            {
                _logger.LogInformation("アクセス権限チェック: ユーザー名={Username}, workId={WorkId}", username, workId);

                if (string.IsNullOrEmpty(username))
                {
                    return BadRequest(new { message = "ユーザー名が必要です" });
                }

                if (string.IsNullOrEmpty(workId))
                {
                    return BadRequest(new { message = "workIdが必要です" });
                }

                var hasAccess = await _authorizationService.CanAccessWorkIdAsync(username, workId);

                return Ok(new
                {
                    success = true,
                    hasAccess = hasAccess,
                    username = username,
                    workId = workId,
                    message = hasAccess ? "アクセス許可" : "アクセス拒否"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "アクセス権限チェックでエラーが発生しました");
                return StatusCode(500, new { message = "サーバーエラーが発生しました" });
            }
        }

        /// <summary>
        /// workId管理データを再読み込み（開発効率向上のため）
        /// </summary>
        [HttpPost("reload-workid-data")]
        public async Task<IActionResult> ReloadWorkIdData()
        {
            try
            {
                _logger.LogInformation("workId管理データの再読み込み要求");

                var result = await _authorizationService.ReloadWorkIdDataAsync();

                if (result)
                {
                    return Ok(new
                    {
                        success = true,
                        message = "workId管理データの再読み込みが完了しました"
                    });
                }
                else
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "workId管理データの再読み込みに失敗しました"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "workId管理データの再読み込みでエラーが発生しました");
                return StatusCode(500, new { message = "サーバーエラーが発生しました" });
            }
        }

        /// <summary>
        /// 現在のworkId管理データの状態を取得
        /// </summary>
        [HttpGet("workid-data-status")]
        public async Task<IActionResult> GetWorkIdDataStatus([FromQuery] string username)
        {
            try
            {
                _logger.LogInformation("workId管理データの状態取得: ユーザー名={Username}", username);

                if (string.IsNullOrEmpty(username))
                {
                    return BadRequest(new { message = "ユーザー名が必要です" });
                }

                var workIds = await _dataIngestionService.GetAvailableWorkIdsAsync(username);
                var metadata = await _authorizationService.GetAllowedWorkIdMetadataAsync(username);

                return Ok(new
                {
                    success = true,
                    username = username,
                    workIdCount = workIds.Count,
                    workIds = workIds,
                    metadata = metadata
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "workId管理データの状態取得でエラーが発生しました");
                return StatusCode(500, new { message = "サーバーエラーが発生しました" });
            }
        }

        /// <summary>
        /// workId管理データのバックアップ状態を確認
        /// </summary>
        [HttpGet("workid-backup-status")]
        public async Task<IActionResult> GetWorkIdBackupStatus()
        {
            try
            {
                _logger.LogInformation("workId管理データのバックアップ状態確認");

                var backupExists = await _authorizationService.BackupExistsAsync();

                return Ok(new
                {
                    success = true,
                    backupExists = backupExists,
                    message = backupExists ? "バックアップファイルが存在します" : "バックアップファイルが存在しません"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "workId管理データのバックアップ状態確認でエラーが発生しました");
                return StatusCode(500, new { message = "サーバーエラーが発生しました" });
            }
        }

        /// <summary>
        /// workId管理データをバックアップから復元
        /// </summary>
        [HttpPost("restore-workid-from-backup")]
        public async Task<IActionResult> RestoreWorkIdFromBackup()
        {
            try
            {
                _logger.LogInformation("workId管理データのバックアップからの復元要求");

                var result = await _authorizationService.RestoreFromBackupAsync();

                if (result)
                {
                    return Ok(new
                    {
                        success = true,
                        message = "バックアップからの復元が完了しました"
                    });
                }
                else
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "バックアップからの復元に失敗しました"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "workId管理データのバックアップからの復元でエラーが発生しました");
                return StatusCode(500, new { message = "サーバーエラーが発生しました" });
            }
        }
    }

    /// <summary>
    /// ログインリクエスト用のDTOクラス
    /// </summary>
    public class LoginRequest
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }

    /// <summary>
    /// workId登録リクエスト用のDTOクラス
    /// </summary>
    public class RegisterWorkIdRequest
    {
        public string Username { get; set; }
        public string WorkId { get; set; }
        public string FileName { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// インデックス登録リクエスト用のDTOクラス
    /// </summary>
    public class IndexRequest
    {
        public string Username { get; set; }
    }

    /// <summary>
    /// 一括インデックス登録リクエスト用のDTOクラス
    /// </summary>
    public class BatchIndexRequest
    {
        public string Username { get; set; }
        public List<string> WorkIds { get; set; }
    }
} 