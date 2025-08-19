using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using AzureRag.Services;

namespace AzureRag.Controllers
{
    [ApiController]
    [Route("api/storage")] 
    [Authorize]
    public class StorageController : ControllerBase
    {
        private readonly ILogger<StorageController> _logger;
        private readonly IWorkIdManagementService _workIdManagementService;
        private readonly Services.IAuthorizationService _authorizationService;

        public StorageController(
            ILogger<StorageController> logger,
            IWorkIdManagementService workIdManagementService,
            Services.IAuthorizationService authorizationService)
        {
            _logger = logger;
            _workIdManagementService = workIdManagementService;
            _authorizationService = authorizationService;
        }

        /// <summary>
        /// workIdの原本PDFをストリーム配信（inline）
        /// </summary>
        [HttpGet("original")]
        public async Task<IActionResult> GetOriginal([FromQuery] string work_id)
        {
            try
            {
                if (string.IsNullOrEmpty(work_id))
                {
                    return BadRequest(new { error = "work_idが必要です" });
                }

                var user = User?.Identity?.Name;
                if (string.IsNullOrEmpty(user)) return Unauthorized();

                // アクセス権
                if (!await _authorizationService.CanAccessWorkIdAsync(user, work_id))
                {
                    return StatusCode(403, new { error = "アクセス権限がありません" });
                }

                var info = await _workIdManagementService.GetWorkIdInfoAsync(work_id);
                if (info == null || string.IsNullOrEmpty(info.SavedRelativePath))
                {
                    return NotFound(new { error = "保存ファイルが見つかりません" });
                }

                var path = Path.IsPathRooted(info.SavedRelativePath)
                    ? info.SavedRelativePath
                    : Path.Combine(Directory.GetCurrentDirectory(), info.SavedRelativePath);

                if (!System.IO.File.Exists(path))
                {
                    return NotFound(new { error = "ファイルが存在しません" });
                }

                var stream = System.IO.File.OpenRead(path);
                var fileName = info.OriginalFileName ?? Path.GetFileName(path);
                return File(stream, "application/pdf", fileName, enableRangeProcessing: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "原本PDF配信中にエラー");
                return StatusCode(500, new { error = "内部エラー" });
            }
        }

        /// <summary>
        /// workIdのファイルメタ情報を返す
        /// </summary>
        [HttpGet("workid-info")]
        public async Task<IActionResult> GetWorkIdInfo([FromQuery] string work_id)
        {
            try
            {
                if (string.IsNullOrEmpty(work_id))
                {
                    return BadRequest(new { error = "work_idが必要です" });
                }

                var user = User?.Identity?.Name;
                if (string.IsNullOrEmpty(user)) return Unauthorized();

                if (!await _authorizationService.CanAccessWorkIdAsync(user, work_id))
                {
                    return StatusCode(403, new { error = "アクセス権限がありません" });
                }

                var info = await _workIdManagementService.GetWorkIdInfoAsync(work_id);
                var hasFile = info != null && !string.IsNullOrEmpty(info.SavedRelativePath);
                var fileUrl = hasFile ? Url.Content($"~/api/storage/original?work_id={Uri.EscapeDataString(work_id)}") : null;

                return Ok(new {
                    workId = work_id,
                    fileName = info?.OriginalFileName ?? info?.Name,
                    hasFile,
                    fileUrl
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "workIdメタ情報取得中にエラー");
                return StatusCode(500, new { error = "内部エラー" });
            }
        }
    }
}

