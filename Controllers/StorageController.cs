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

                // 保存時は Directory.GetCurrentDirectory() 基準で相対パスを記録しているが、
                // 環境により CurrentDirectory が変わるケースに備え、複数候補を試す
                string ResolvePath(string relativeOrAbsolute)
                {
                    if (Path.IsPathRooted(relativeOrAbsolute)) return relativeOrAbsolute;
                    var cand1 = Path.Combine(Directory.GetCurrentDirectory(), relativeOrAbsolute);
                    if (System.IO.File.Exists(cand1)) return cand1;
                    var cand2 = Path.Combine(AppContext.BaseDirectory, relativeOrAbsolute);
                    if (System.IO.File.Exists(cand2)) return cand2;
                    // 旧ルート（publishの1階層上）も念のため
                    var publishDir = AppContext.BaseDirectory?.TrimEnd(Path.DirectorySeparatorChar);
                    var rootDir = string.IsNullOrEmpty(publishDir) ? null : Directory.GetParent(publishDir)?.FullName;
                    if (!string.IsNullOrEmpty(rootDir))
                    {
                        var cand3 = Path.Combine(rootDir, relativeOrAbsolute);
                        if (System.IO.File.Exists(cand3)) return cand3;
                    }
                    return cand1; // 既定を返す
                }

                // 実ファイルパスを解決
                var path = ResolvePath(info.SavedRelativePath);

                if (!System.IO.File.Exists(path))
                {
                    return NotFound(new { error = "ファイルが存在しません" });
                }

                var stream = System.IO.File.OpenRead(path);
                var fileName = info.OriginalFileName ?? Path.GetFileName(path);
                // ブラウザでのインライン表示（日本語ファイル名は RFC 5987 形式で安全に指定）
                var encodedFileName = Uri.EscapeDataString(fileName);
                Response.Headers["Content-Disposition"] = $"inline; filename=\"file.pdf\"; filename*=UTF-8''{encodedFileName}";
                return new FileStreamResult(stream, "application/pdf") { EnableRangeProcessing = true };
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
                // できる限りプロキシヘッダを優先してスキーム/ホスト/ポート/プレフィックスを決定
                string scheme = Request.Headers["X-Forwarded-Proto"].ToString();
                if (string.IsNullOrWhiteSpace(scheme)) scheme = Request.Scheme;
                string host = Request.Headers["X-Forwarded-Host"].ToString();
                string port = Request.Headers["X-Forwarded-Port"].ToString();
                if (string.IsNullOrWhiteSpace(host)) host = Request.Host.Host;
                // ポート付与（X-Forwarded-Host にポートが含まれていない場合）
                string hostWithPort = host;
                bool hostHasPort = host.Contains(":");
                if (!hostHasPort)
                {
                    if (!string.IsNullOrWhiteSpace(port))
                    {
                        // 既定ポートは付与しない
                        bool isHttps = string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase);
                        if (!(isHttps && port == "443") && !(!isHttps && port == "80"))
                        {
                            hostWithPort = host + ":" + port;
                        }
                    }
                    else
                    {
                        // Request.Host のポートを利用
                        if (Request.Host.Port.HasValue)
                        {
                            var p = Request.Host.Port.Value;
                            if (!(scheme == "https" && p == 443) && !(scheme == "http" && p == 80))
                            {
                                hostWithPort = host + ":" + p.ToString();
                            }
                        }
                    }
                }
                // プレフィックス（例: /dev-app1）
                string prefix = Request.Headers["X-Forwarded-Prefix"].ToString();
                if (string.IsNullOrWhiteSpace(prefix)) prefix = Request.PathBase.Value;
                // 最低限のフォールバック: 既知ドメインならhttpsを既定に
                if (string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase) && hostWithPort.Contains("ilu.co.jp"))
                {
                    scheme = "https";
                }
                if (string.IsNullOrEmpty(prefix)) prefix = string.Empty;
                var fileUrl = hasFile ? $"{scheme}://{hostWithPort}{prefix}/api/storage/original?work_id={Uri.EscapeDataString(work_id)}" : null;

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

