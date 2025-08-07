using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using AzureRag.Models.Reinforcement;

namespace AzureRag.Controllers
{
    [ApiController]
    [Route("api/reinforcement")]
    public class ReinforcementController : ControllerBase
    {
        private readonly ReinforcementJsonlController _jsonlController;
        private readonly ReinforcementPromptController _promptController;
        private readonly ReinforcementEvaluationController _evaluationController;
        
        public ReinforcementController(
            ILogger<ReinforcementController> logger,
            ILogger<ReinforcementJsonlController> jsonlLogger,
            ILogger<ReinforcementPromptController> promptLogger,
            ILogger<ReinforcementEvaluationController> evaluationLogger)
        {
            _jsonlController = new ReinforcementJsonlController(jsonlLogger);
            _promptController = new ReinforcementPromptController(promptLogger);
            _evaluationController = new ReinforcementEvaluationController(evaluationLogger);
        }
        
        // 後方互換性のためのリダイレクトメソッド群
        
        // JSONL関連
        [HttpPost("save-jsonl")]
        public async Task<IActionResult> SaveJsonl([FromBody] SaveJsonlRequest request)
        {
            return await _jsonlController.SaveJsonl(request);
        }
        
        [HttpGet("list-jsonl")]
        public IActionResult ListJsonl()
        {
            return _jsonlController.ListJsonl();
        }
        
        [HttpGet("get-jsonl/{id}")]
        public IActionResult GetJsonl(string id)
        {
            return _jsonlController.GetJsonl(id);
        }
        
        [HttpGet("download-jsonl/{id}")]
        public IActionResult DownloadJsonl(string id)
        {
            return _jsonlController.DownloadJsonl(id);
        }
        
        // プロンプト関連
        [HttpPost("save-prompt")]
        [Consumes("application/json")]
        public async Task<IActionResult> SavePrompt([FromBody] SavePromptRequest request)
        {
            try {
                // デバッグログを追加
                var logger = HttpContext.RequestServices.GetService(typeof(ILogger<ReinforcementController>)) as ILogger<ReinforcementController>;
                logger?.LogInformation($"SavePrompt called on ReinforcementController with request: {request != null}");
                
                if (request == null)
                {
                    logger?.LogWarning("SavePrompt: request is null");
                    return BadRequest(new { success = false, error = "リクエストが無効です" });
                }
                
                logger?.LogInformation($"Content length: {request.Content?.Length ?? 0}, Description: {request.Description ?? "(null)"}");
                
                // リクエストの内容をログに出力（最初の100文字まで）
                if (request.Content != null && request.Content.Length > 0)
                {
                    logger?.LogInformation($"Content (first 100 chars): {request.Content.Substring(0, Math.Min(100, request.Content.Length))}");
                }
                
                try {
                    return await _promptController.SavePrompt(request);
                }
                catch (Exception innerEx) {
                    logger?.LogError(innerEx, "Error from _promptController.SavePrompt");
                    return StatusCode(500, new { success = false, error = innerEx.Message });
                }
            }
            catch (Exception ex)
            {
                var logger = HttpContext.RequestServices.GetService(typeof(ILogger<ReinforcementController>)) as ILogger<ReinforcementController>;
                logger?.LogError(ex, "SavePrompt controller method threw an exception");
                return StatusCode(500, new { success = false, error = "Internal server error: " + ex.Message });
            }
        }
        
        [HttpGet("list-prompts")]
        public IActionResult ListPrompts()
        {
            return _promptController.ListPrompts();
        }
        
        [HttpGet("download-prompt/{id}")]
        public IActionResult DownloadPrompt(string id)
        {
            return _promptController.DownloadPrompt(id);
        }
        
        [HttpPost("upload-prompt")]
        public async Task<IActionResult> UploadPrompt([FromForm] IFormFile file, [FromForm] string description = "", 
                                                [FromForm] string systemPrompt = "", [FromForm] string jsonlId = "")
        {
            return await _promptController.UploadPrompt(file, description, systemPrompt, jsonlId);
        }
        
        // 評価関連
        [HttpPost("compare-responses")]
        public async Task<IActionResult> CompareResponses([FromBody] CompareResponsesRequest request)
        {
            return await _evaluationController.CompareResponses(request);
        }
        
        [HttpPost("calculate-score")]
        public async Task<IActionResult> CalculateScore([FromBody] CalculateScoreRequest request)
        {
            return await _evaluationController.CalculateScore(request);
        }
    }
}