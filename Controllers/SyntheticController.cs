using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AzureRag.Controllers
{
    [ApiController]
    [Route("api/synthetic")]
    [Authorize]
    public class SyntheticController : ControllerBase
    {
        private readonly ILogger<SyntheticController> _logger;
        private readonly Services.IAuthorizationService _authorizationService;
        private readonly Services.IAnthropicService _anthropic;

        public SyntheticController(
            ILogger<SyntheticController> logger,
            Services.IAuthorizationService authorizationService,
            Services.IAnthropicService anthropic)
        {
            _logger = logger;
            _authorizationService = authorizationService;
            _anthropic = anthropic;
        }

        public class DiffItem { public int page_no { get; set; } public string diff_text { get; set; } }
        public class GenerateJsonlRequest
        {
            public string work_id { get; set; }
            public int samples { get; set; } = 3;
            public DiffItem[] diffs { get; set; } = Array.Empty<DiffItem>();
            public string prompt { get; set; }
        }

        [HttpPost("generate-jsonl")]
        public async Task<IActionResult> GenerateJsonl([FromBody] GenerateJsonlRequest request)
        {
            try
            {
                var user = User?.Identity?.Name;
                if (string.IsNullOrWhiteSpace(user)) return Unauthorized();
                if (request == null || string.IsNullOrWhiteSpace(request.work_id)) return BadRequest(new { error = "work_idが必要です" });
                if (!await _authorizationService.CanAccessWorkIdAsync(user, request.work_id)) return StatusCode(403, new { error = "アクセス権限がありません" });

                var diffsJoined = string.Join("\n\n", (request.diffs ?? Array.Empty<DiffItem>()).OrderBy(d=>d.page_no).Select(d => $"[p.{d.page_no+1}]\n{d.diff_text}"));
                var n = Math.Max(1, request.samples);

                var system = "あなたはRAG向けfew-shot学習用の合成データ（JSONL）を作成するエキスパートです。" +
                             "出力は厳密にJSONL（1行1 JSON）とし、コードフェンスや説明文は含めないでください。";

                // 既定のユーザープロンプト
                var defaultUserPrompt = $@"以下はPDFのページごとの差分説明です。これを必ず参照し、比較差分の内容を踏まえて、同様の文書をRAGインデックス用にテキスト構造化する際に、画像などの認識・構造化精度が向上するfewshot学習用データセットを{n}行のJSONLで作成してください。
- 各行のJSONは次のキーを必ず含めます: task, instruction, input_text, target_structured
- task は 'pdf_structuring' 固定
- instruction は日本語で1-2文、対象タスクを明確に
- input_text は未知文書の想定断片（日本語）
- target_structured は所望の構造化出力例（日本語）
- 差分は参考情報であり、元文書に存在しない事実は生成しないこと
- JSON以外の文字（コードフェンス/注釈）は出力しないこと

差分まとめ:\n\n{diffsJoined}";

                // リクエストにpromptがあれば優先。{{DIFFS}} や {diffs} をプレースホルダとして置換
                var userPrompt = string.IsNullOrWhiteSpace(request.prompt)
                    ? defaultUserPrompt
                    : (request.prompt
                        .Replace("{{DIFFS}}", diffsJoined)
                        .Replace("{diffs}", diffsJoined));

                // Claude 4 Sonnetの最大トークンを優先（10,000希望。許容未満の場合はサービス側の上限に丸め込まれる）
                var content = await _anthropic.GenerateChatResponseAsync(userPrompt, context: new string(' ', 0), systemPrompt: system);

                string jsonl = content?.Trim() ?? string.Empty;
                // 念のため先頭の不要行を軽く除去（コードフェンス等）
                if (jsonl.StartsWith("```"))
                {
                    var idx = jsonl.IndexOf('\n');
                    if (idx > 0) jsonl = jsonl.Substring(idx + 1);
                    jsonl = jsonl.Replace("```", string.Empty);
                }

                return Ok(new { jsonl });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Synthetic] generate-jsonl 失敗");
                return StatusCode(500, new { error = "内部エラー", error_detail = ex.Message });
            }
        }

        // 既定プロンプトを返す（UI初期表示用）
        [HttpGet("default-prompt")]
        [AllowAnonymous]
        public IActionResult GetDefaultPrompt([FromQuery]int samples = 3)
        {
            var n = Math.Max(1, samples);
            var system = "あなたはRAG向けfew-shot学習用の合成データ（JSONL）を作成するエキスパートです。出力は厳密にJSONL（1行1 JSON）とし、コードフェンスや説明文は含めないでください。";
            var user = $@"以下はPDFのページごとの差分説明です。これを必ず参照し、比較差分の内容を踏まえて、同様の文書をRAGインデックス用にテキスト構造化する際に、画像などの認識・構造化精度が向上するfewshot学習用データセットを{n}行のJSONLで作成してください。
- 各行のJSONは次のキーを必ず含めます: task, instruction, input_text, target_structured
- task は 'pdf_structuring' 固定
- instruction は日本語で1-2文、対象タスクを明確に
- input_text は未知文書の想定断片（日本語）
- target_structured は所望の構造化出力例（日本語）
- 差分は参考情報であり、元文書に存在しない事実は生成しないこと
- JSON以外の文字（コードフェンス/注釈）は出力しないこと

差分まとめ:\n\n{{DIFFS}}";
            return Ok(new { system_prompt = system, user_prompt = user });
        }
    }
}


