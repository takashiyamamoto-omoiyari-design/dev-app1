using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AzureRag.Services.PDF;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace AzureRag.Services.Diff
{
    public class DiffAnalyzeService : IDiffAnalyzeService
    {
        private readonly ILogger<DiffAnalyzeService> _logger;
        private readonly IPdfTextExtractionService _pdfTextExtraction;
        private readonly IWorkIdManagementService _workIdService;
        private readonly IAutoStructureService _autoService;
        private readonly IAnthropicService _anthropic;

        public DiffAnalyzeService(
            ILogger<DiffAnalyzeService> logger,
            IPdfTextExtractionService pdfTextExtraction,
            IWorkIdManagementService workIdService,
            IAutoStructureService autoService,
            IAnthropicService anthropic)
        {
            _logger = logger;
            _pdfTextExtraction = pdfTextExtraction;
            _workIdService = workIdService;
            _autoService = autoService;
            _anthropic = anthropic;
        }

        public async Task<DiffAnalyzeResult> AnalyzeAsync(string workId, string username)
        {
            _logger?.LogInformation("[Diff] AnalyzeAsync start: user={User}, work_id={WorkId}", username, workId);
            if (string.IsNullOrWhiteSpace(workId)) throw new ArgumentException("workId is required");

            // 1) 原文PDFをローカル保存から取得
            _logger?.LogInformation("[Diff] Fetch WorkIdInfo");
            var info = await _workIdService.GetWorkIdInfoAsync(workId);
            string ResolvePath(string relativeOrAbsolute)
            {
                if (string.IsNullOrEmpty(relativeOrAbsolute)) return null;
                if (Path.IsPathRooted(relativeOrAbsolute)) return relativeOrAbsolute;
                var cand1 = Path.Combine(Directory.GetCurrentDirectory(), relativeOrAbsolute);
                if (File.Exists(cand1)) return cand1;
                var cand2 = Path.Combine(AppContext.BaseDirectory, relativeOrAbsolute);
                if (File.Exists(cand2)) return cand2;
                var publishDir = AppContext.BaseDirectory?.TrimEnd(Path.DirectorySeparatorChar);
                var rootDir = string.IsNullOrEmpty(publishDir) ? null : Directory.GetParent(publishDir)?.FullName;
                if (!string.IsNullOrEmpty(rootDir))
                {
                    var cand3 = Path.Combine(rootDir, relativeOrAbsolute);
                    if (File.Exists(cand3)) return cand3;
                }
                return relativeOrAbsolute; // 最後にそのまま返す
            }

            var path = ResolvePath(info?.SavedRelativePath);
            if (info == null || string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                _logger?.LogWarning("[Diff] Original PDF not found: rawPath={Raw}, resolved={Resolved}", info?.SavedRelativePath, path);
                throw new FileNotFoundException("保存された原本PDFが見つかりません");
            }

            // 2) PDFページテキスト抽出
            _logger?.LogInformation("[Diff] ExtractTextByPageAsync: path={Path}", path);
            Dictionary<int, string> originalByPage;
            using (var fs = File.OpenRead(path))
            {
                originalByPage = await _pdfTextExtraction.ExtractTextByPageAsync(fs);
            }
            _logger?.LogInformation("[Diff] Original pages extracted: count={Count}", originalByPage?.Count ?? 0);

            // 3) 構造化データ取得（page_text_list優先）
            _logger?.LogInformation("[Diff] Get structured data: work_id={WorkId}", workId);
            var structured = await _autoService.GetStructuredDataAsync(workId);
            var extractedByPage = new Dictionary<int, string>(); // key: 1-based
            if (structured?.PageTextList != null && structured.PageTextList.Count > 0)
            {
                foreach (var p in structured.PageTextList)
                {
                    extractedByPage[p.PageNo + 1] = p.Text ?? string.Empty; // APIは0-based想定なので+1
                }
                _logger?.LogInformation("[Diff] Using page_text_list: pages={Count}", extractedByPage.Count);
            }
            else if (structured?.TextList != null && structured.TextList.Count > 0)
            {
                // TextListを結合し1ページ扱い
                var text = string.Join("\n\n", structured.TextList.Select(t => t.Text ?? string.Empty));
                extractedByPage[1] = text;
                _logger?.LogInformation("[Diff] Using text_list fallback: length={Len}", text?.Length ?? 0);
            }
            else
            {
                _logger?.LogWarning("[Diff] No structured text available for work_id={WorkId}", workId);
            }

            // 4) 正規化
            string Normalize(string s)
            {
                if (string.IsNullOrEmpty(s)) return string.Empty;
                var t = s.Replace("\r\n", "\n").Replace("\r", "\n");
                // ```mermaid ... ``` ブロック除去
                t = Regex.Replace(t, @"```mermaid[\s\S]*?```", string.Empty, RegexOptions.IgnoreCase);
                // 余計な空白を畳み込み
                t = Regex.Replace(t, @"[\t\u00A0]+", " ");
                return t.Trim();
            }

            var pageMax = Math.Max(originalByPage.Keys.DefaultIfEmpty(1).Max(), extractedByPage.Keys.DefaultIfEmpty(1).Max());
            var result = new DiffAnalyzeResult();

            // 画像生成出力ルートと一回生成の制御
            var genBaseOut = Environment.GetEnvironmentVariable("DEMOAPP_TMP_DIR");
            if (string.IsNullOrWhiteSpace(genBaseOut))
            {
                // 既定の永続領域
                genBaseOut = "/var/lib/demo-app2/tmp";
            }
            Directory.CreateDirectory(genBaseOut);
            bool triedGeneration = false;
            string generationDir = null; // この分析実行内で固定
            string genStamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

            // 画像自動生成に備え、必要依存が無ければ可能な範囲で導入を試みる
            await EnsurePdfImageDependenciesAsync();

            for (int page = 1; page <= pageMax; page++)
            {
                var orig = Normalize(originalByPage.ContainsKey(page) ? originalByPage[page] : string.Empty);
                var ext = Normalize(extractedByPage.ContainsKey(page) ? extractedByPage[page] : string.Empty);

                // ローカル行差分は実施せず、Claude Visionに委譲

                // Claude補助: 原文ページ画像を添付して説明（ベッドロックvision）
                string visionText = string.Empty;
                try
                {
                    // 画像パスはストレージ/tmp配下に生成済みのPNGがあればそれを使う。無ければスキップ。
                    // 既存パイプラインでは images ディレクトリや tmp/pdf_*/ に出力されることがある。
                    // ここでは単純に見つかった最初の候補を使う。
                    // この分析実行内での探索ディレクトリ: 生成済みであれば generationDir、無ければ共有ルート
                    var dir = generationDir ?? genBaseOut;
                    string[] candidates = Array.Empty<string>();
                    if (Directory.Exists(dir))
                    {
                        candidates = Directory.GetFiles(dir, "*page_" + page + ".png", SearchOption.AllDirectories);
                    }
                    // 画像が無い場合は自動生成（pdf_to_images.py）
                    if (candidates.Length == 0 && !triedGeneration)
                    {
                        try
                        {
                            generationDir = generationDir ?? Path.Combine(genBaseOut, "pdf_gen_" + genStamp);
                            Directory.CreateDirectory(generationDir);
                            // python3 pdf_to_images.py <pdf> <outDir> <fileId> <original>
                            var scriptPath = Path.Combine(AppContext.BaseDirectory, "pdf_to_images.py");
                            if (!File.Exists(scriptPath)) scriptPath = "pdf_to_images.py";
                            var start = new ProcessStartInfo
                            {
                                FileName = "python3",
                                Arguments = $"\"{scriptPath}\" \"{path}\" \"{generationDir}\" \"{Path.GetFileNameWithoutExtension(info.OriginalFileName ?? workId)}\" \"{info.OriginalFileName ?? "document"}\" --dpi 200 --format png",
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };
                            _logger?.LogInformation("[Diff] 画像未検出のため自動生成を実行: {Args}", start.Arguments);
                            using var proc = Process.Start(start);
                            if (proc != null)
                            {
                                var so = await proc.StandardOutput.ReadToEndAsync();
                                var se = await proc.StandardError.ReadToEndAsync();
                                proc.WaitForExit(60_000);
                                _logger?.LogInformation("[Diff] pdf_to_images.py stdout: {Out}", so);
                                if (!string.IsNullOrEmpty(se)) _logger?.LogWarning("[Diff] pdf_to_images.py stderr: {Err}", se);
                            }
                            triedGeneration = true; // この分析実行では一度だけ生成
                            if (Directory.Exists(generationDir))
                            {
                                candidates = Directory.GetFiles(generationDir, "*page_" + page + ".png", SearchOption.AllDirectories);
                            }
                        }
                        catch (Exception pex)
                        {
                            _logger?.LogWarning(pex, "[Diff] 画像自動生成に失敗");
                        }
                    }
                    // すでに一度生成済みで、dir が共有ルートのままなら、生成先固定ディレクトリも探索
                    if (candidates.Length == 0 && triedGeneration && !string.IsNullOrEmpty(generationDir) && Directory.Exists(generationDir))
                    {
                        candidates = Directory.GetFiles(generationDir, "*page_" + page + ".png", SearchOption.AllDirectories);
                    }
                    if (candidates.Length > 0)
                    {
                        var png = candidates[0];
                        var bytes = await File.ReadAllBytesAsync(png);
                        var sys = "あなたはPDFページ画像と抽出テキストの差分を日本語で簡潔に説明するアシスタントです。抽出テキストに欠けている重要箇所、過剰に追加された箇所を列挙してください。";
                        var user = $"これはPDFのp.{page}です。以下は抽出テキストです:\n\n" + ext + "\n\n差分の要点だけを箇条書きで示してください。";
                        visionText = await _anthropic.GenerateVisionAsync(sys, user, bytes, "png");
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "[Diff] Vision補助に失敗 p={Page}", page);
                }

                var sb = new StringBuilder();
                sb.AppendLine($"[p.{page}] 差分");
                if (!string.IsNullOrEmpty(visionText))
                {
                    sb.AppendLine(visionText);
                }
                else
                {
                    sb.AppendLine("画像生成またはClaude分析に失敗したため、このページの差分は生成できませんでした。");
                }

                result.PageDiffs.Add(new PageDiff
                {
                    PageNo = page - 1,
                    DiffText = sb.ToString(),
                    Details = sb.ToString()
                });
            }

            _logger?.LogInformation("[Diff] AnalyzeAsync done: pages={Pages}", result.PageDiffs.Count);
            return result;
        }

        private async Task EnsurePdfImageDependenciesAsync()
        {
            try
            {
                // python3
                bool pythonOk = await ExecOkAsync("python3", "--version");
                if (!pythonOk)
                {
                    _logger?.LogWarning("[Deps] python3 が見つかりません。画像生成はスキップされます");
                    return;
                }

                // pdf2image / Pillow
                bool pdf2imageOk = await ExecOkAsync("python3", "-c \"import pdf2image, PIL; print('ok')\"");
                if (!pdf2imageOk)
                {
                    _logger?.LogInformation("[Deps] pdf2image/Pillow を --user でインストール試行");
                    await ExecLoggedAsync("python3", "-m pip install --user pdf2image pillow");
                }

                // poppler-utils (pdftoppm)
                bool popplerOk = await ExecOkAsync("bash", "-lc \"which pdftoppm || command -v pdftoppm\"");
                if (!popplerOk)
                {
                    var osRelease = File.Exists("/etc/os-release") ? await File.ReadAllTextAsync("/etc/os-release") : string.Empty;
                    string cmd = null;
                    if (osRelease.Contains("amzn") || osRelease.Contains("rhel") || osRelease.Contains("centos"))
                    {
                        cmd = "sudo -n yum install -y poppler-utils";
                    }
                    else if (osRelease.Contains("ubuntu") || osRelease.Contains("debian"))
                    {
                        cmd = "sudo -n bash -lc 'apt-get update -y && apt-get install -y poppler-utils'";
                    }
                    if (cmd != null)
                    {
                        _logger?.LogInformation("[Deps] poppler-utils 自動導入を試行: {Cmd}", cmd);
                        await ExecLoggedAsync("bash", "-lc \"" + cmd + "\"");
                    }
                    else
                    {
                        _logger?.LogWarning("[Deps] 未対応OSのためpoppler自動導入をスキップしました");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[Deps] 依存性チェック/導入中に例外");
            }
        }

        private async Task<bool> ExecOkAsync(string fileName, string args)
        {
            try
            {
                var psi = new ProcessStartInfo { FileName = fileName, Arguments = args, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi);
                if (p == null) return false;
                await p.WaitForExitAsync();
                return p.ExitCode == 0;
            }
            catch { return false; }
        }

        private async Task ExecLoggedAsync(string fileName, string args)
        {
            var psi = new ProcessStartInfo { FileName = fileName, Arguments = args, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            if (p == null) return;
            var so = await p.StandardOutput.ReadToEndAsync();
            var se = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            if (!string.IsNullOrEmpty(so)) _logger?.LogInformation("[Exec] {Cmd} out: {Out}", fileName + " " + args, so);
            if (!string.IsNullOrEmpty(se)) _logger?.LogWarning("[Exec] {Cmd} err: {Err}", fileName + " " + args, se);
        }
    }
}


