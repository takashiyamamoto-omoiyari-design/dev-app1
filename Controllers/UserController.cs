using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

                if (string.IsNullOrWhiteSpace(bucket))
                {
                    _logger.LogWarning("UserInfo:Bucket が未設定です。空配列を返します。");
                    return Ok(new { types = Array.Empty<string>() });
                }

                var csv = await ReadObjectAsStringAsync(bucket, key);
                if (string.IsNullOrWhiteSpace(csv))
                {
                    _logger.LogWarning("userinfo.csv が取得できませんでした（空）。");
                    return Ok(new { types = Array.Empty<string>() });
                }

                // 簡易CSVパース（1行目ヘッダ、1列目:ユーザーID、4列目:type想定。フィールド内カンマ非対応）
                var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length <= 1)
                {
                    return Ok(new { types = Array.Empty<string>() });
                }

                var result = new List<string>();
                for (int i = 1; i < lines.Length; i++)
                {
                    var cols = lines[i].Split(',');
                    if (cols.Length < 4) continue;
                    var userId = cols[0].Trim();
                    if (!string.Equals(userId, username, StringComparison.OrdinalIgnoreCase)) continue;
                    var typeField = cols[3].Trim();
                    if (string.IsNullOrEmpty(typeField)) break;
                    var types = typeField.Replace('\u25B2', '▲') // 念の為置換
                                          .Split('▲', StringSplitOptions.RemoveEmptyEntries)
                                          .Select(t => t.Trim())
                                          .Where(t => t.Length > 0)
                                          .Distinct(StringComparer.OrdinalIgnoreCase)
                                          .ToList();
                    result.AddRange(types);
                    break;
                }

                return Ok(new { types = result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() });
            }
            catch (AmazonS3Exception s3ex)
            {
                _logger.LogError(s3ex, "S3からuserinfo.csv取得中にエラー");
                return StatusCode(500, new { error = "S3取得エラー" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ユーザータイプ取得中にエラー");
                return StatusCode(500, new { error = "内部エラー" });
            }
        }

        private async Task<string> ReadObjectAsStringAsync(string bucket, string key)
        {
            using var s3 = new AmazonS3Client(Amazon.RegionEndpoint.APNortheast1);
            var req = new GetObjectRequest { BucketName = bucket, Key = key };
            using var res = await s3.GetObjectAsync(req);
            using var sr = new StreamReader(res.ResponseStream, Encoding.UTF8);
            return await sr.ReadToEndAsync();
        }
    }
}


