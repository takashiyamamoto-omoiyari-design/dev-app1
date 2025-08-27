using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AzureRag.Controllers
{
    [ApiController]
    [Route("api/fewshot")]
    [Authorize]
    public class FewshotController : ControllerBase
    {
        private readonly ILogger<FewshotController> _logger;
        private readonly IConfiguration _config;

        public FewshotController(ILogger<FewshotController> logger, IConfiguration config)
        {
            _logger = logger;
            _config = config;
        }

        // 全ページ画像の簡易列挙（生成済みPNG想定）
        // GET /api/fewshot/pages?work_id=...  -> { pages:[{index,url,thumbUrl}] }
        [HttpGet("pages")]
        public IActionResult GetPages([FromQuery] string work_id)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(work_id)) return BadRequest(new { error = "work_idが必要です" });

                // 既存の保存先（storage/images など）を走査。なければ /var/lib/demo-app2/tmp/pdf_* も併用
                var candidates = new List<string>();
                // 相対（現在ディレクトリ直下）
                candidates.Add(Path.Combine("storage", "images"));
                candidates.Add(Path.Combine("storage", "tmp"));
                // 発行ディレクトリ配下
                candidates.Add(Path.Combine("/opt/app/demo-app2/publish", "storage", "images"));
                candidates.Add(Path.Combine("/opt/app/demo-app2/publish", "storage", "tmp"));
                // 外部作業用
                candidates.Add(Path.Combine("/var/lib/demo-app2/tmp"));

                var items = new List<object>();
                _logger.LogInformation("[FewshotPages] START work_id={WorkId}", work_id);
                _logger.LogInformation("[FewshotPages] candidates: {Dirs}", string.Join(", ", candidates.Distinct()));

                foreach (var baseDir in candidates)
                {
                    if (!Directory.Exists(baseDir)) continue;
                    try
                    {
                        // work_id を含むサブディレクトリ配下の png を拾う
                        var files = Directory.GetFiles(baseDir, "*.png", SearchOption.AllDirectories)
                            .Where(f => f.IndexOf(work_id, StringComparison.OrdinalIgnoreCase) >= 0)
                            .OrderBy(f => f)
                            .ToList();
                        _logger.LogInformation("[FewshotPages] scan {Base} matched={Count}", baseDir, files.Count);
                        if (files.Count == 0) { continue; }

                        for (int i = 0; i < files.Count; i++)
                        {
                            var f = files[i];
                            // 画像配信用のAPI URLに変換（PathBase対応）
                            var pb = Request?.PathBase.HasValue == true ? Request.PathBase.Value : string.Empty;
                            var url = $"{pb}/api/fewshot/image?work_id={Uri.EscapeDataString(work_id)}&path={Uri.EscapeDataString(f)}";
                            items.Add(new { index = i, url, thumbUrl = url, path = f });
                        }
                    }
                    catch { }
                }

                // fallback: work_idに一致しない場合は、候補ディレクトリ内のPNGのうち新しい順に上位60件を返す
                if (items.Count == 0)
                {
                    _logger.LogWarning("[FewshotPages] work_id一致の画像が見つかりません。フォールバックで最近の画像を返します");
                    var allPngs = new List<string>();
                    foreach (var baseDir in candidates.Distinct())
                    {
                        try
                        {
                            if (!Directory.Exists(baseDir)) continue;
                            var fs = Directory.GetFiles(baseDir, "*.png", SearchOption.AllDirectories);
                            allPngs.AddRange(fs);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "[FewshotPages] fallback列挙失敗 base={Base}", baseDir);
                        }
                    }
                    var top = allPngs
                        .Select(p => new FileInfo(p))
                        .OrderByDescending(fi => fi.LastWriteTimeUtc)
                        .Take(60)
                        .Select((fi, idx) =>
                        {
                            var pb = Request?.PathBase.HasValue == true ? Request.PathBase.Value : string.Empty;
                            var url = $"{pb}/api/fewshot/image?work_id={Uri.EscapeDataString(work_id)}&path={Uri.EscapeDataString(fi.FullName)}";
                            return new { index = idx, url, thumbUrl = url, path = fi.FullName };
                        })
                        .ToList();
                    _logger.LogInformation("[FewshotPages] fallback returned={Count}", top.Count);
                    return Ok(new { pages = top });
                }

                _logger.LogInformation("[FewshotPages] END returned={Count}", items.Count);
                return Ok(new { pages = items });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "fewshot pages 列挙でエラー");
                return StatusCode(500, new { error = "内部エラー" });
            }
        }

        // 画像を配信
        // GET /api/fewshot/image?work_id=...&path=...
        [HttpGet("image")]
        public IActionResult GetImage([FromQuery] string work_id, [FromQuery] string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                    return NotFound();
                var bytes = System.IO.File.ReadAllBytes(path);
                return File(bytes, "image/png");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "fewshot image 配信でエラー");
                return StatusCode(500);
            }
        }

        public class FewshotUploadRequest
        {
            public string work_id { get; set; }
            public string type { get; set; } // ilu-demo1 等
            public string image_url { get; set; } // 内部APIの画像URL
            public string image_path { get; set; } // サーバ側の実ファイルパス（優先）
            public string text { get; set; } // 合成データ本文
            public string groupPrefix { get; set; } // 先頭2文字: [A-Z][A-Z]（任意、未指定は "AU"）
        }

        // アップロード: 画像とテキストをS3へ保存
        [HttpPost("upload")]
        public async Task<IActionResult> Upload([FromBody] FewshotUploadRequest req)
        {
            try
            {
                var user = _config["DataIngestion:ExternalApiUserId"] ?? User?.Identity?.Name ?? "unknown";
                if (string.IsNullOrWhiteSpace(user)) return Unauthorized();
                if (req == null || string.IsNullOrWhiteSpace(req.type)) return BadRequest(new { error = "typeが必要です" });

                // 画像取得
                byte[] imageBytes = null;
                if (!string.IsNullOrWhiteSpace(req.image_path) && System.IO.File.Exists(req.image_path))
                {
                    imageBytes = await System.IO.File.ReadAllBytesAsync(req.image_path);
                }
                else if (!string.IsNullOrWhiteSpace(req.image_url))
                {
                    using var hc = new System.Net.Http.HttpClient();
                    imageBytes = await hc.GetByteArrayAsync(req.image_url);
                }
                if (imageBytes == null || imageBytes.Length == 0) return BadRequest(new { error = "画像取得に失敗" });

                // S3設定
                var bucket = _config["Fewshot:Bucket"] ?? _config["UserInfo:Bucket"];
                var regionName = Environment.GetEnvironmentVariable("AWS_REGION") ?? "ap-northeast-1";
                if (string.IsNullOrWhiteSpace(bucket)) return StatusCode(500, new { error = "S3バケット未設定" });

                // 命名規則の生成: [A-Z][A-Z]C0-XXX（Claude3.7固定 → C0）。
                // groupPrefixが不正な場合は自動補正し、必ず2文字の英大文字にする。
                var grp = (req.groupPrefix ?? "AU");
                grp = Regex.Replace(grp.ToUpperInvariant(), "[^A-Z]", "");
                if (grp.Length < 2) grp = (grp + "AU").Substring(0, 2);
                else if (grp.Length > 2) grp = grp.Substring(0, 2);

                var prefix = $"analyze-structure/individual/{user}/{req.type}/";

                // 既存の採番を取得して次番号を算出。さらにHEAD相当のメタデータ取得で衝突回避。
                var region = Amazon.RegionEndpoint.GetBySystemName(regionName);
                using var s3 = new AmazonS3Client(region);

                // まずは既存一覧から最大番号を推定
                var listReq = new ListObjectsV2Request { BucketName = bucket, Prefix = prefix };
                var existing = new List<string>();
                ListObjectsV2Response listRes;
                do
                {
                    listRes = await s3.ListObjectsV2Async(listReq);
                    existing.AddRange(listRes.S3Objects.Select(o => o.Key));
                    listReq.ContinuationToken = listRes.NextContinuationToken;
                } while (listRes.IsTruncated);

                int maxNo = 0;
                var re = new Regex($@"^{Regex.Escape(prefix)}{grp}C0-(\\d{{3}})\\.png$", RegexOptions.IgnoreCase);
                foreach (var key in existing)
                {
                    var m = re.Match(key);
                    if (m.Success && int.TryParse(m.Groups[1].Value, out var n) && n > maxNo) maxNo = n;
                }

                // 推定値+1から開始し、存在チェックで未使用の連番を確定
                int candidate = Math.Max(0, maxNo) + 1;
                string baseName;
                while (true)
                {
                    var serial = candidate.ToString("D3");
                    baseName = $"{grp}C0-{serial}";
                    var tryImgKey = prefix + baseName + ".png";
                    try
                    {
                        var metaReq = new GetObjectMetadataRequest { BucketName = bucket, Key = tryImgKey };
                        var _ = await s3.GetObjectMetadataAsync(metaReq);
                        // 存在する → 次の番号
                        candidate++;
                        continue;
                    }
                    catch (AmazonS3Exception metaEx) when (metaEx.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        // 存在しない → 採用
                    }
                    break;
                }

                // 確定キー
                var imgKey = prefix + baseName + ".png";
                var txtKey = prefix + baseName + ".txt";

                await s3.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = bucket,
                    Key = imgKey,
                    InputStream = new MemoryStream(imageBytes),
                    ContentType = "image/png"
                });

                var textBytes = Encoding.UTF8.GetBytes(req.text ?? string.Empty);
                await s3.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = bucket,
                    Key = txtKey,
                    InputStream = new MemoryStream(textBytes),
                    ContentType = "text/plain; charset=utf-8"
                });

                _logger.LogInformation("fewshot upload ok: s3://{Bucket}/{ImgKey} / {TxtKey}", bucket, imgKey, txtKey);
                return Ok(new { bucket, imgKey, txtKey, baseName });
            }
            catch (AmazonS3Exception s3ex)
            {
                _logger.LogError(s3ex, "fewshot upload S3 error: {Code} {Status}", s3ex.ErrorCode, s3ex.StatusCode);
                return StatusCode(500, new { error = "S3エラー", detail = s3ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "fewshot upload error");
                return StatusCode(500, new { error = "内部エラー", detail = ex.Message });
            }
        }
    }
}


