using System;
using System.Linq;
using System.Threading.Tasks;
using AzureRag.Services;
using AzureRag.Services.Diff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AzureRag.Controllers
{
    [ApiController]
    [Route("api/ocr")]
    [Authorize]
    public class OcrController : ControllerBase
    {
        private readonly ILogger<OcrController> _logger;
        private readonly IDiffAnalyzeService _diffService;
        private readonly AzureRag.Services.IAuthorizationService _authorizationService;

        public OcrController(
            ILogger<OcrController> logger,
            IDiffAnalyzeService diffService,
            AzureRag.Services.IAuthorizationService authorizationService)
        {
            _logger = logger;
            _diffService = diffService;
            _authorizationService = authorizationService;
        }

        public class DiffAnalyzeRequest { public string work_id { get; set; } }

        [HttpPost("diff-analyze")]
        public async Task<IActionResult> DiffAnalyze([FromBody] DiffAnalyzeRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.work_id))
                {
                    return BadRequest(new { error = "work_idが必要です" });
                }

                var user = User?.Identity?.Name;
                if (string.IsNullOrEmpty(user)) return Unauthorized();

                if (!await _authorizationService.CanAccessWorkIdAsync(user, request.work_id))
                {
                    return StatusCode(403, new { error = "アクセス権限がありません" });
                }

                var result = await _diffService.AnalyzeAsync(request.work_id, user);

                return Ok(new
                {
                    summary = (object)null, // ミニマルUIのため未使用
                    page_diffs = result.PageDiffs.Select(p => new { page_no = p.PageNo, diff_text = p.DiffText, details = p.Details }).ToList()
                });
            }
            catch (System.IO.FileNotFoundException)
            {
                return NotFound(new { error = "保存された原本PDFが見つかりません" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "差分分析中にエラー");
                return StatusCode(500, new { error = "内部エラー" });
            }
        }
    }
}


