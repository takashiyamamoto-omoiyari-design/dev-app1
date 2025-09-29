using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AzureRag.Models.Settings;

namespace AzureRag.Services.UserDirectory
{
    public sealed class S3UserDirectory : IUserDirectory
    {
        private readonly ILogger<S3UserDirectory> _logger;
        private readonly IAmazonS3 _s3;
        private readonly UserDirectorySettings _settings;

        private readonly object _lock = new object();
        private DateTime _lastLoadedUtc = DateTime.MinValue;
        private string? _lastEtag;
        private Dictionary<string, UserDirectoryRecord> _nameToRecord = new();

        public S3UserDirectory(ILogger<S3UserDirectory> logger, IAmazonS3 s3, IConfiguration configuration)
        {
            _logger = logger;
            _s3 = s3;
            _settings = new UserDirectorySettings
            {
                S3Bucket = configuration["UserInfo:Bucket"] ?? configuration["UserDirectory:S3Bucket"] ?? string.Empty,
                S3Key = configuration["UserInfo:Key"] ?? configuration["UserDirectory:S3Key"] ?? string.Empty,
                CacheTtlSeconds = int.TryParse(configuration["UserInfo:CacheTtlSeconds"] ?? configuration["UserDirectory:CacheTtlSeconds"], out var ttl) ? ttl : 600
            };
        }

        public string? GetLastCsvETag() => _lastEtag;

        public async Task<bool> TryValidateUserAsync(string name, string password)
        {
            var rec = await GetUserRecordAsync(name);
            return rec != null && rec.Password == password;
        }

        public async Task<UserDirectoryRecord?> GetUserRecordAsync(string name)
        {
            await EnsureLoadedAsync();
            _nameToRecord.TryGetValue(name, out var rec);
            return rec;
        }

        private async Task EnsureLoadedAsync()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastLoadedUtc).TotalSeconds < Math.Max(60, _settings.CacheTtlSeconds))
                return;

            lock (_lock)
            {
                if ((DateTime.UtcNow - _lastLoadedUtc).TotalSeconds < Math.Max(60, _settings.CacheTtlSeconds))
                    return;
                _lastLoadedUtc = now; // 競合抑止
            }

            try
            {
                var req = new GetObjectRequest { BucketName = _settings.S3Bucket, Key = _settings.S3Key };            
                using var resp = await _s3.GetObjectAsync(req);
                _lastEtag = resp.ETag?.Trim('"');
                using var reader = new StreamReader(resp.ResponseStream, Encoding.UTF8);
                var text = await reader.ReadToEndAsync();
                var map = ParseCsv(text);
                if (map.Count > 0)
                {
                    _nameToRecord = map;
                    _logger.LogInformation("UserDirectory CSV 読込成功: count={Count}, etag={ETag}", map.Count, _lastEtag);
                }
                else
                {
                    _logger.LogWarning("UserDirectory CSV が空または解析に失敗しました（既存キャッシュを維持）");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UserDirectory CSV 読込エラー（既存キャッシュを継続使用）");
            }
        }

        private static Dictionary<string, UserDirectoryRecord> ParseCsv(string csv)
        {
            var map = new Dictionary<string, UserDirectoryRecord>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(csv)) return map;

            using var sr = new StringReader(csv);
            string? header = sr.ReadLine();
            if (header == null) return map;
            var cols = header.Split(',').Select(c => c.Trim()).ToArray();
            int idxName = Array.FindIndex(cols, c => string.Equals(c, "name", StringComparison.OrdinalIgnoreCase));
            int idxPass = Array.FindIndex(cols, c => string.Equals(c, "pass", StringComparison.OrdinalIgnoreCase));
            int idxTrial = Array.FindIndex(cols, c => string.Equals(c, "trial", StringComparison.OrdinalIgnoreCase));
            int idxType = Array.FindIndex(cols, c => string.Equals(c, "type", StringComparison.OrdinalIgnoreCase));

            if (idxName < 0 || idxPass < 0) return map;

            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = SplitCsvLine(line);
                if (parts.Length <= Math.Max(idxName, idxPass)) continue;
                var name = parts[idxName].Trim();
                var pass = parts[idxPass].Trim();
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(pass)) continue;
                var rec = new UserDirectoryRecord
                {
                    Name = name,
                    Password = pass,
                    Trial = idxTrial >= 0 && idxTrial < parts.Length ? parts[idxTrial].Trim() : string.Empty,
                    Type = idxType >= 0 && idxType < parts.Length ? parts[idxType].Trim() : string.Empty
                };
                map[name] = rec;
            }
            return map;
        }

        private static string[] SplitCsvLine(string line)
        {
            // シンプルCSV分割（ダブルクオート対応軽量版）
            var result = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }
                if (c == ',' && !inQuotes)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
            result.Add(sb.ToString());
            return result.ToArray();
        }
    }
}


