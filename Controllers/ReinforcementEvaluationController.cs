using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using AzureRag.Models.Reinforcement;
using AzureRag.Utils;

namespace AzureRag.Controllers
{
    [ApiController]
    [Route("api/reinforcement/evaluation")]
    public class ReinforcementEvaluationController : BaseReinforcementController
    {
        public ReinforcementEvaluationController(ILogger<ReinforcementEvaluationController> logger = null) 
            : base(logger)
        {
        }
        
        [HttpPost("compare-responses")]
        public async Task<IActionResult> CompareResponses([FromBody] CompareResponsesRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.Query))
                {
                    return BadRequest(new { success = false, error = "質問が空です" });
                }
                
                // ここでは実際のAI回答生成は実装していません
                // 実際の実装では、標準プロンプトとFewShotプロンプトの両方でAIに質問を送信し、回答を取得します
                
                // ダミーの回答を生成
                var standardResponse = "これは標準プロンプトでの回答例です。実際の実装では、AIモデルからの回答が返されます。";
                var fewshotResponse = "これはFewShotプロンプトでの回答例です。学習データを活用することで、より精度の高い回答が期待できます。";
                
                // 日本時間（JST）の年月日-時分秒形式でファイル名を生成
                var jstNow = TimeZoneHelper.GetCurrentJapanTime();
                var timestamp = jstNow.ToString("yyyyMMdd-HHmmss");
                
                // レスポンスの比較データを作成
                var comparison = new ResponseComparison
                {
                    Query = request.Query,
                    StandardResponse = standardResponse,
                    FewshotResponse = fewshotResponse,
                    PromptId = string.IsNullOrEmpty(request.PromptId) ? "" : request.PromptId,
                    CreatedAt = jstNow
                };
                
                // 一意のファイル名を生成
                var fileName = $"{timestamp}.json";
                var filePath = Path.Combine(_rlStorageDirectory, "responses", fileName);
                
                // ファイルに保存
                await System.IO.File.WriteAllTextAsync(
                    filePath, 
                    JsonSerializer.Serialize(comparison, new JsonSerializerOptions { WriteIndented = true })
                );
                
                return Ok(new { 
                    success = true, 
                    fileName = fileName,
                    standardResponse = standardResponse,
                    fewshotResponse = fewshotResponse
                });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
        
        [HttpPost("calculate-score")]
        public async Task<IActionResult> CalculateScore([FromBody] CalculateScoreRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.JsonlId) || string.IsNullOrEmpty(request.ResponseId))
                {
                    return BadRequest(new { success = false, error = "JSONLまたは応答IDが空です" });
                }
                
                // ここでは簡易的な実装として、ダミーの評価結果を返します
                // 実際の実装では、JSONLの正解テキストとAI回答を比較して類似度を計算します
                
                // 日本時間（JST）の年月日-時分秒形式で現在時刻を取得
                var jstNow = TimeZoneHelper.GetCurrentJapanTime();
                
                // ダミーの評価結果
                var result = new EvaluationResult
                {
                    JsonlId = request.JsonlId,
                    ResponseId = request.ResponseId,
                    SimilarityScore = 0.85, // ダミー値
                    AccuracyScore = 0.78,   // ダミー値
                    CorrectText = "これは正解テキストの例です。",
                    AIResponse = "これはAIモデルの回答例です。",
                    AnalysisDetails = new Dictionary<string, double>
                    {
                        { "文法正確性", 0.92 },
                        { "内容一致度", 0.75 },
                        { "フォーマット", 0.88 }
                    },
                    CreatedAt = jstNow
                };
                
                // 一意のファイル名を生成
                var fileName = $"{jstNow.ToString("yyyyMMdd-HHmmss")}.json";
                var filePath = Path.Combine(_rlStorageDirectory, "evaluations", fileName);
                
                // ファイルに保存
                await System.IO.File.WriteAllTextAsync(
                    filePath, 
                    JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
                );
                
                return Ok(new { 
                    success = true, 
                    fileName = fileName,
                    result = result
                });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
    }
}