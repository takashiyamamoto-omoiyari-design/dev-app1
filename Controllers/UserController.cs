using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AzureRag.Controllers
{
    [ApiController]
    [Route("api/user")]
    [Authorize]
    public class UserController : ControllerBase
    {
        private readonly ILogger<UserController> _logger;
        private readonly IConfiguration _configuration;

        public UserController(ILogger<UserController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        // GET /api/user/types
        [HttpGet("types")]
        public async Task<IActionResult> GetUserTypes()
        {
            try
            {
                var username = User?.Identity?.Name;
                if (string.IsNullOrWhiteSpace(username))
                {
                    return Unauthorized(new { error = "認証が必要です" });
                }

                // 設定からS3の場所を取得
                var bucket = _configuration["UserInfo:Bucket"] ?? _configuration["DataIngestion:UserInfoBucket"];
                var key = _configuration["UserInfo:Key"] ?? _configuration["DataIngestion:UserInfoKey"] ?? "user-info/userinfo.csv";

                var regionName = Environment.GetEnvironmentVariable("AWS_REGION") ?? "ap-northeast-1";
                _logger.LogInformation("[UserTypes] START username={Username}, bucket={Bucket}, key={Key}, region={Region}", username, bucket, key, regionName);

                if (string.IsNullOrWhiteSpace(bucket))
                {
                    _logger.LogWarning("UserInfo:Bucket が未設定です。空配列を返します。");
                    return Ok(new { types = Array.Empty<string>() });
                }

                var sw = Stopwatch.StartNew();
                var (csv, contentLength, eTag, lastModified) = await ReadObjectAsStringWithMetaAsync(bucket, key, regionName);
                _logger.LogInformation("[UserTypes] S3 get ok: contentLength={Len}, eTag={ETag}, lastModified={LastMod:o}, elapsedMs={Ms}", contentLength, eTag, lastModified, sw.ElapsedMilliseconds);
                if (!string.IsNullOrEmpty(csv))
                {
                    var preview = csv.Replace("\r\n", "⏎").Replace("\n", "⏎");
                    if (preview.Length > 200) preview = preview.Substring(0, 200) + " …";
                    _logger.LogDebug("[UserTypes] csv preview: {Preview}", preview);
                }
                if (string.IsNullOrWhiteSpace(csv))
                {
                    _logger.LogWarning("userinfo.csv が取得できませんでした（空）。");
                    return Ok(new { types = Array.Empty<string>() });
                }

                // 簡易CSVパース（1行目ヘッダ、1列目:ユーザーID、4列目:type想定。フィールド内カンマ非対応）
                var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                _logger.LogInformation("[UserTypes] csv lines={LineCount}", lines.Length);
                if (lines.Length <= 1)
                {
                    return Ok(new { types = Array.Empty<string>() });
                }

                // ヘッダ解析
                var header = lines[0];
                var headerCols = header.Split(',');
                _logger.LogInformation("[UserTypes] header columns={Count} -> {Cols}", headerCols.Length, string.Join("|", headerCols.Select(c => c.Trim())));

                var result = new List<string>();
                for (int i = 1; i < lines.Length; i++)
                {
                    var cols = lines[i].Split(',');
                    if (cols.Length < 4)
                    {
                        _logger.LogDebug("[UserTypes] skip line {Index}: column count={Count}", i, cols.Length);
                        continue;
                    }
                    var userId = cols[0].Trim();
                    if (!string.Equals(userId, username, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var typeField = cols[3].Trim();
                    if (string.IsNullOrEmpty(typeField)) break;
                    var types = typeField.Replace('\u25B2', '▲') // 念の為置換
                                          .Split('▲', StringSplitOptions.RemoveEmptyEntries)
                                          .Select(t => t.Trim())
                                          .Where(t => t.Length > 0)
                                          .Distinct(StringComparer.OrdinalIgnoreCase)
                                          .ToList();
                    result.AddRange(types);
                    _logger.LogInformation("[UserTypes] matched user. parsed types count={Count}", types.Count);
                    break;
                }

                _logger.LogInformation("[UserTypes] END return types count={Count}", result.Count);
                return Ok(new { types = result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() });
            }
            catch (AmazonS3Exception s3ex)
            {
                _logger.LogError(s3ex, "[UserTypes] S3 error: code={ErrorCode}, status={Status}, requestId={RequestId}", s3ex.ErrorCode, s3ex.StatusCode, s3ex.RequestId);
                return StatusCode(500, new { error = "S3取得エラー" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[UserTypes] unexpected error during types fetch");
                return StatusCode(500, new { error = "内部エラー" });
            }
        }

        private async Task<(string content, long? contentLength, string eTag, DateTime? lastModified)> ReadObjectAsStringWithMetaAsync(string bucket, string key, string regionName)
        {
            var region = Amazon.RegionEndpoint.GetBySystemName(regionName);
            using var s3 = new AmazonS3Client(region);
            var req = new GetObjectRequest { BucketName = bucket, Key = key };
            using var res = await s3.GetObjectAsync(req);
            var len = res.Headers.ContentLength;
            var etag = res.ETag;
            var last = res.LastModified;
            using var sr = new StreamReader(res.ResponseStream, Encoding.UTF8);
            var text = await sr.ReadToEndAsync();
            return (text, len, etag, last);
        }
    }
}


