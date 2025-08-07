using Microsoft.AspNetCore.Mvc;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using Amazon;
using Amazon.ElasticLoadBalancingV2;
using Amazon.ElasticLoadBalancingV2.Model;
using Amazon.Runtime.CredentialManagement;

namespace AzureRag.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class KeywordExtractionController : ControllerBase
    {
                private readonly IHttpClientFactory _clientFactory;
        private readonly ILogger<KeywordExtractionController> _logger;
        private readonly IConfiguration _configuration;

        // AWS ALB設定
        private readonly string _awsProfile;
        private readonly RegionEndpoint _awsRegion;
        private readonly string _targetGroupArn;
        private readonly bool _inAwsEc2;
        private List<string> _cachedHealthyEndpoints;
        private DateTime _lastEndpointRefresh;
        private readonly TimeSpan _endpointCacheTimeout = TimeSpan.FromMinutes(5); // 5分間キャッシュ

        public KeywordExtractionController(IHttpClientFactory clientFactory, ILogger<KeywordExtractionController> logger, IConfiguration configuration)
        {
            _clientFactory = clientFactory;
            _logger = logger;
            _configuration = configuration;
            
            // AWS ALB設定 - Tokenize API用
            _awsProfile = "ILURAG";
            _awsRegion = RegionEndpoint.APNortheast1;
            _targetGroupArn = "arn:aws:elasticloadbalancing:ap-northeast-1:311141529894:targetgroup/ilurag-tokenizer2/d770879f3d19c662";
            _inAwsEc2 = true; // EC2上で実行するためtrueに設定
            _cachedHealthyEndpoints = new List<string>();
            _lastEndpointRefresh = DateTime.MinValue;
        }

        /// <summary>
        /// キャッシュを無効化してAWS ALBから最新のエンドポイントを取得（複数回リトライ付き）
        /// </summary>
        private async Task<List<string>> RefreshEndpointsAsync()
        {
            _logger.LogWarning("キャッシュを無効化して、AWS ALBから最新のエンドポイントを取得します");
            _cachedHealthyEndpoints.Clear();
            _lastEndpointRefresh = DateTime.MinValue;
            
            // 複数回リトライしてより確実に最新情報を取得
            var maxRetries = 3;
            var retryDelay = TimeSpan.FromSeconds(2);
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                _logger.LogInformation($"AWS ALB問い合わせ試行 {attempt}/{maxRetries}");
                
                var endpoints = await GetHealthyEndpointsFromALBAsync(forceRefresh: true);
                
                if (endpoints.Any())
                {
                    // 取得したエンドポイントのヘルスチェックを実行
                    var validEndpoints = await ValidateEndpointsAsync(endpoints);
                    
                    if (validEndpoints.Any())
                    {
                        _logger.LogInformation($"試行 {attempt} で {validEndpoints.Count} 個の有効なエンドポイントを確認しました");
                        return validEndpoints;
                    }
                    else
                    {
                        _logger.LogWarning($"試行 {attempt}: 取得したエンドポイントはすべて無効でした");
                    }
                }
                else
                {
                    _logger.LogWarning($"試行 {attempt}: AWS ALBから健全なエンドポイントが取得できませんでした");
                }
                
                // 最後の試行でない場合は待機
                if (attempt < maxRetries)
                {
                    _logger.LogInformation($"{retryDelay.TotalSeconds}秒待機してから再試行します");
                    await Task.Delay(retryDelay);
                }
            }
            
            _logger.LogError("すべての試行が失敗しました。フォールバックエンドポイントを使用します。");
            return new List<string> { "http://10.24.152.66:9926" };
        }

        /// <summary>
        /// エンドポイントの有効性を検証
        /// </summary>
        private async Task<List<string>> ValidateEndpointsAsync(List<string> endpoints)
        {
            var validEndpoints = new List<string>();
            var httpClient = _clientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5); // 短いタイムアウト
            
            foreach (var endpoint in endpoints)
            {
                try
                {
                    _logger.LogDebug($"エンドポイント検証中: {endpoint}");
                    
                    // 軽量なヘルスチェック（HEADリクエストまたは簡単なGET）
                    var healthCheckUrl = $"{endpoint}/api/Tokenize";
                    
                    // 簡単なPOSTリクエストでヘルスチェック
                    var testRequest = new TokenizeApiRequestModel
                    {
                        UserId = _configuration["DataIngestion:ExternalApiUserId"] ?? "ilu-demo",
                        Password = _configuration["DataIngestion:ExternalApiPassword"] ?? "ilupass",
                        Type = "",
                        Text = "test" // 最小限のテストデータ
                    };
                    
                    var jsonContent = JsonSerializer.Serialize(testRequest, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });
                    
                    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                    var response = await httpClient.PostAsync(healthCheckUrl, content);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        validEndpoints.Add(endpoint);
                        _logger.LogInformation($"✅ エンドポイント有効: {endpoint}");
                    }
                    else
                    {
                        _logger.LogWarning($"❌ エンドポイント無効 (HTTP {response.StatusCode}): {endpoint}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"❌ エンドポイント検証失敗: {endpoint} - {ex.Message}");
                }
            }
            
            return validEndpoints;
        }

        /// <summary>
        /// AWS ALBターゲットグループから健全なエンドポイントを取得
        /// </summary>
        private async Task<List<string>> GetHealthyEndpointsFromALBAsync(bool forceRefresh = false)
        {
            // 強制更新でない場合はキャッシュをチェック
            if (!forceRefresh && _cachedHealthyEndpoints.Any() && 
                DateTime.Now - _lastEndpointRefresh < _endpointCacheTimeout)
            {
                _logger.LogInformation($"キャッシュされたエンドポイントを使用: {string.Join(", ", _cachedHealthyEndpoints)}");
                return _cachedHealthyEndpoints;
            }

            try
            {
                _logger.LogInformation("AWS ALBターゲットグループから健全なエンドポイントを取得中...");
                
                AmazonElasticLoadBalancingV2Client client;

                // AWS認証設定
                if (!_inAwsEc2)
                {
                    // AWSのプロファイルを指定してクライアントを作成
                    var chain = new CredentialProfileStoreChain();
                    if (!chain.TryGetAWSCredentials(_awsProfile, out var credentials))
                    {
                        _logger.LogWarning($"AWSプロファイル '{_awsProfile}' が見つかりません。フォールバックエンドポイントを使用します。");
                        return new List<string> { "http://10.24.152.66:9926" };
                    }
                    client = new AmazonElasticLoadBalancingV2Client(credentials, _awsRegion);
                }
                else
                {
                    // AWS EC2上で実行する場合は、IAMロールを使用
                    client = new AmazonElasticLoadBalancingV2Client(_awsRegion);
                }

                // ターゲットグループの詳細を取得
                var request = new DescribeTargetHealthRequest
                {
                    TargetGroupArn = _targetGroupArn
                };

                var response = await client.DescribeTargetHealthAsync(request);
                var healthyEndpoints = new List<string>();

                foreach (var description in response.TargetHealthDescriptions)
                {
                    var target = description.Target;
                    var health = description.TargetHealth;
                    
                    _logger.LogInformation($"ターゲット: {target.Id}, ポート: {target.Port}, 状態: {health.State}, 理由: {health.Reason}");
                    
                    // 健全なターゲットのみを追加
                    if (health.State == TargetHealthStateEnum.Healthy)
                    {
                        var endpoint = $"http://{target.Id}:{target.Port}";
                        healthyEndpoints.Add(endpoint);
                        _logger.LogInformation($"健全なエンドポイントを発見: {endpoint}");
                    }
                }

                if (healthyEndpoints.Any())
                {
                    // 強制更新でない場合のみキャッシュを更新
                    if (!forceRefresh)
                    {
                        _cachedHealthyEndpoints = healthyEndpoints;
                        _lastEndpointRefresh = DateTime.Now;
                    }
                    _logger.LogInformation($"AWS ALBから{healthyEndpoints.Count}個の健全なエンドポイントを取得しました");
                    return healthyEndpoints;
                }
                else
                {
                    _logger.LogWarning("AWS ALBから健全なエンドポイントが見つかりませんでした。フォールバックエンドポイントを使用します。");
                    return new List<string> { "http://10.24.152.66:9926" };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"AWS ALBエンドポイント取得中にエラーが発生: {ex.Message}");
                _logger.LogError($"スタックトレース: {ex.StackTrace}");
                _logger.LogWarning("フォールバックエンドポイントを使用します。");
                return new List<string> { "http://10.24.152.66:9926" };
            }
        }

        [HttpPost]
        public async Task<IActionResult> ExtractKeywords([FromBody] KeywordExtractionRequest request)
        {
            var stopwatch = Stopwatch.StartNew();
            var requestId = Guid.NewGuid().ToString("N")[..8]; // 短いリクエストID
            
            try
            {
                _logger.LogInformation($"[{requestId}] ========== キーワード抽出リクエスト開始 ==========");
                _logger.LogInformation($"[{requestId}] リクエスト受信時刻: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                _logger.LogInformation($"[{requestId}] テキスト長: {request?.Text?.Length ?? 0}");
                
                if (request == null || string.IsNullOrEmpty(request.Text))
                {
                    _logger.LogWarning($"[{requestId}] バリデーションエラー: テキストが未指定");
                    return BadRequest(new { error = "テキストが指定されていません", request_id = requestId });
                }

                // AWS ALBから健全なエンドポイントを取得
                var healthyEndpoints = await GetHealthyEndpointsFromALBAsync();
                
                // リクエスト内容の詳細ログ
                _logger.LogDebug($"[{requestId}] リクエスト詳細:");
                _logger.LogDebug($"[{requestId}]   - UserId: {request.UserId ?? "null"}");
                _logger.LogDebug($"[{requestId}]   - Password: {(string.IsNullOrEmpty(request.Password) ? "null" : "[MASKED]")}");
                _logger.LogDebug($"[{requestId}]   - Text(最初の100文字): {request.Text.Substring(0, Math.Min(100, request.Text.Length))}");
                if (request.Text.Length > 100)
                {
                    _logger.LogDebug($"[{requestId}]   - Text(続き): ...省略 (合計{request.Text.Length}文字)");
                }
                
                Exception lastException = null;
                
                // 各健全なエンドポイントを順番に試す
                foreach (var baseEndpoint in healthyEndpoints)
                {
                    string apiUrl = $"{baseEndpoint}/api/Tokenize";
                    string apiHost = new Uri(apiUrl).Host;
                    int apiPort = new Uri(apiUrl).Port;
                    
                    _logger.LogInformation($"[{requestId}] API接続先情報:");
                    _logger.LogInformation($"[{requestId}]   - URL: {apiUrl}");
                    _logger.LogInformation($"[{requestId}]   - Host: {apiHost}");
                    _logger.LogInformation($"[{requestId}]   - Port: {apiPort}");
                    
                    try
                    {
                        // ネットワーク接続テスト（DNS解決とPing）
                        var networkTestStopwatch = Stopwatch.StartNew();
                        await CheckNetworkConnectivity(apiHost, apiPort, requestId);
                        networkTestStopwatch.Stop();
                        _logger.LogInformation($"[{requestId}] ネットワーク接続テスト完了: {networkTestStopwatch.ElapsedMilliseconds}ms");

                        // クライアントを設定してタイムアウトを30秒に設定
                        var client = _clientFactory.CreateClient();
                        client.Timeout = TimeSpan.FromSeconds(30);
                        
                        _logger.LogInformation($"[{requestId}] HTTPクライアント設定:");
                        _logger.LogInformation($"[{requestId}]   - タイムアウト: {client.Timeout.TotalSeconds}秒");
                        _logger.LogInformation($"[{requestId}]   - BaseAddress: {client.BaseAddress?.ToString() ?? "未設定"}");
                        
                        // 新しいTokenize APIリクエスト作成
                        var apiRequest = new TokenizeApiRequestModel
                        {
                            UserId = _configuration["DataIngestion:ExternalApiUserId"] ?? "ilu-demo",
                            Password = _configuration["DataIngestion:ExternalApiPassword"] ?? "ilupass",
                            Type = "",
                            Text = request.Text
                        };
                        
                        _logger.LogInformation($"[{requestId}] API用リクエストデータ作成:");
                        _logger.LogInformation($"[{requestId}]   - UserId: {apiRequest.UserId}");
                        _logger.LogInformation($"[{requestId}]   - Password: [MASKED]");
                        _logger.LogInformation($"[{requestId}]   - Type: '{apiRequest.Type}'");
                        _logger.LogInformation($"[{requestId}]   - Text長: {apiRequest.Text.Length}文字");
                        
                        var options = new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                            WriteIndented = false
                        };
                        
                        var serializationStopwatch = Stopwatch.StartNew();
                        var serializedRequest = JsonSerializer.Serialize(apiRequest, options);
                        serializationStopwatch.Stop();
                        
                        _logger.LogInformation($"[{requestId}] JSONシリアライゼーション完了: {serializationStopwatch.ElapsedMilliseconds}ms");
                        _logger.LogDebug($"[{requestId}] シリアライズ後のJSONサイズ: {serializedRequest.Length}バイト");
                        _logger.LogDebug($"[{requestId}] リクエストJSON(最初の500文字): {serializedRequest.Substring(0, Math.Min(500, serializedRequest.Length))}");
                        if (serializedRequest.Length > 500)
                        {
                            _logger.LogDebug($"[{requestId}] リクエストJSON(続き): ...省略");
                        }
                        
                        var content = new StringContent(
                            serializedRequest, 
                            Encoding.UTF8, 
                            "application/json");
                        
                        // Content-Typeヘッダーの確認
                        _logger.LogDebug($"[{requestId}] HTTPコンテンツ情報:");
                        _logger.LogDebug($"[{requestId}]   - Content-Type: {content.Headers.ContentType}");
                        _logger.LogDebug($"[{requestId}]   - Content-Length: {content.Headers.ContentLength}");
                        _logger.LogDebug($"[{requestId}]   - Encoding: UTF-8");
                        
                        _logger.LogInformation($"[{requestId}] ---------- HTTP POST リクエスト送信開始 ----------");
                        var httpStopwatch = Stopwatch.StartNew();
                        
                        var response = await client.PostAsync(apiUrl, content);
                        
                        httpStopwatch.Stop();
                        _logger.LogInformation($"[{requestId}] HTTP通信完了: {httpStopwatch.ElapsedMilliseconds}ms");
                        _logger.LogInformation($"[{requestId}] レスポンス受信時刻: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                        
                        // レスポンス詳細情報
                        _logger.LogInformation($"[{requestId}] レスポンス基本情報:");
                        _logger.LogInformation($"[{requestId}]   - StatusCode: {(int)response.StatusCode} ({response.StatusCode})");
                        _logger.LogInformation($"[{requestId}]   - ReasonPhrase: {response.ReasonPhrase ?? "null"}");
                        _logger.LogInformation($"[{requestId}]   - IsSuccessStatusCode: {response.IsSuccessStatusCode}");
                        _logger.LogInformation($"[{requestId}]   - Version: {response.Version}");
                        
                        // レスポンスヘッダー情報
                        _logger.LogDebug($"[{requestId}] レスポンスヘッダー:");
                        foreach (var header in response.Headers)
                        {
                            _logger.LogDebug($"[{requestId}]   - {header.Key}: {string.Join(", ", header.Value)}");
                        }
                        
                        if (response.Content?.Headers != null)
                        {
                            _logger.LogDebug($"[{requestId}] コンテンツヘッダー:");
                            foreach (var header in response.Content.Headers)
                            {
                                _logger.LogDebug($"[{requestId}]   - {header.Key}: {string.Join(", ", header.Value)}");
                            }
                        }
                        
                        if (response.IsSuccessStatusCode)
                        {
                            var responseReadStopwatch = Stopwatch.StartNew();
                            var responseContent = await response.Content.ReadAsStringAsync();
                            responseReadStopwatch.Stop();
                            
                            _logger.LogInformation($"[{requestId}] レスポンス読み取り完了: {responseReadStopwatch.ElapsedMilliseconds}ms");
                            _logger.LogInformation($"[{requestId}] レスポンスサイズ: {responseContent?.Length ?? 0}バイト");
                            
                            _logger.LogDebug($"[{requestId}] レスポンス本文(最初の1000文字): {responseContent?.Substring(0, Math.Min(1000, responseContent?.Length ?? 0))}");
                            if ((responseContent?.Length ?? 0) > 1000)
                            {
                                _logger.LogDebug($"[{requestId}] レスポンス本文(続き): ...省略");
                            }
                            
                            // Tokenize APIのレスポンスをパースして、従来の形式に変換
                            var deserializationStopwatch = Stopwatch.StartNew();
                            try
                            {
                                var tokenizeResponse = JsonSerializer.Deserialize<TokenizeApiResponse>(responseContent, options);
                                deserializationStopwatch.Stop();
                                
                                _logger.LogInformation($"[{requestId}] JSONデシリアライゼーション完了: {deserializationStopwatch.ElapsedMilliseconds}ms");
                                
                                if (tokenizeResponse?.TokenList != null)
                                {
                                    _logger.LogInformation($"[{requestId}] パース結果:");
                                    _logger.LogInformation($"[{requestId}]   - TokenList要素数: {tokenizeResponse.TokenList.Count}");
                                    
                                    // 各トークンの詳細ログ
                                    for (int i = 0; i < Math.Min(10, tokenizeResponse.TokenList.Count); i++)
                                    {
                                        var token = tokenizeResponse.TokenList[i];
                                        _logger.LogDebug($"[{requestId}]   - Token[{i}]: text='{token.Text}', boostScore={token.BoostScore}");
                                    }
                                    if (tokenizeResponse.TokenList.Count > 10)
                                    {
                                        _logger.LogDebug($"[{requestId}]   - 他 {tokenizeResponse.TokenList.Count - 10} 件のトークン...");
                                    }
                                    
                                    // 従来の形式に変換
                                    var keywordList = tokenizeResponse.TokenList
                                        .Select(token => new KeywordItem
                                        {
                                            Surface = token.Text,
                                            Score = token.BoostScore
                                        })
                                        .ToList();

                                    stopwatch.Stop();
                                    _logger.LogInformation($"[{requestId}] ========== キーワード抽出完了 ==========");
                                    _logger.LogInformation($"[{requestId}] 総処理時間: {stopwatch.ElapsedMilliseconds}ms");
                                    _logger.LogInformation($"[{requestId}] 抽出キーワード数: {keywordList.Count}");
                                    
                                    return Ok(new
                                    {
                                        return_code = 0,
                                        keyword_list = keywordList,
                                        request_id = requestId,
                                        processing_time_ms = stopwatch.ElapsedMilliseconds
                                    });
                                }
                                else
                                {
                                    _logger.LogWarning($"[{requestId}] Tokenize APIからtokenListが返されませんでした");
                                    _logger.LogWarning($"[{requestId}] レスポンス構造: tokenizeResponse={tokenizeResponse != null}, TokenList={tokenizeResponse?.TokenList != null}");
                                    
                                    stopwatch.Stop();
                                    return Ok(new
                                    {
                                        return_code = 1,
                                        error_detail = "tokenListが空です",
                                        keyword_list = new List<KeywordItem>(),
                                        request_id = requestId,
                                        processing_time_ms = stopwatch.ElapsedMilliseconds
                                    });
                                }
                            }
                            catch (JsonException jsonEx)
                            {
                                deserializationStopwatch.Stop();
                                _logger.LogError($"[{requestId}] JSONデシリアライゼーションエラー:");
                                _logger.LogError($"[{requestId}]   - エラーメッセージ: {jsonEx.Message}");
                                _logger.LogError($"[{requestId}]   - Path: {jsonEx.Path ?? "不明"}");
                                _logger.LogError($"[{requestId}]   - LineNumber: {jsonEx.LineNumber}");
                                _logger.LogError($"[{requestId}]   - BytePositionInLine: {jsonEx.BytePositionInLine}");
                                _logger.LogError($"[{requestId}]   - スタックトレース: {jsonEx.StackTrace}");
                                
                                lastException = jsonEx;
                                continue; // 次のエンドポイントを試す
                            }
                        }
                        else
                        {
                            // エラーレスポンスの詳細読み取り
                            var errorContent = "";
                            try
                            {
                                errorContent = await response.Content.ReadAsStringAsync();
                                _logger.LogError($"[{requestId}] エラーレスポンス本文: {errorContent}");
                            }
                            catch (Exception readEx)
                            {
                                _logger.LogError($"[{requestId}] エラーレスポンス読み取り失敗: {readEx.Message}");
                            }
                            
                            _logger.LogWarning($"[{requestId}] Tokenize API呼び出しエラー:");
                            _logger.LogWarning($"[{requestId}]   - StatusCode: {(int)response.StatusCode} ({response.StatusCode})");
                            _logger.LogWarning($"[{requestId}]   - ReasonPhrase: {response.ReasonPhrase}");
                            _logger.LogWarning($"[{requestId}]   - エラー内容: {errorContent}");
                            
                            lastException = new Exception($"API request failed with status code: {response.StatusCode}, Response: {errorContent}");
                            continue; // 次のエンドポイントを試す
                        }
                    }
                    catch (TaskCanceledException ex)
                    {
                        _logger.LogError($"[{requestId}] タイムアウトエラー:");
                        _logger.LogError($"[{requestId}]   - IsCanceled: {ex.CancellationToken.IsCancellationRequested}");
                        _logger.LogError($"[{requestId}]   - Message: {ex.Message}");
                        _logger.LogError($"[{requestId}]   - InnerException: {ex.InnerException?.Message ?? "null"}");
                        
                        if (ex.InnerException is TimeoutException)
                        {
                            _logger.LogError($"[{requestId}] HTTP通信タイムアウト (30秒)");
                        }
                        else
                        {
                            _logger.LogError($"[{requestId}] リクエストキャンセル");
                        }
                        
                        lastException = ex;
                        
                        // エラー発生時に即座にキャッシュ更新と再試行を実行
                        if (_cachedHealthyEndpoints.Any())
                        {
                            _logger.LogWarning($"[{requestId}] エラーが発生しました。キャッシュを更新して再試行します。");
                            var refreshedEndpoints = await RefreshEndpointsAsync();
                            
                            // 更新されたエンドポイントで再試行
                            var retryResult = await RetryWithRefreshedEndpoints(refreshedEndpoints, request, requestId, stopwatch);
                            if (retryResult != null) return retryResult;
                        }
                        
                        continue; // 次のエンドポイントを試す
                    }
                    catch (HttpRequestException ex)
                    {
                        _logger.LogError($"[{requestId}] HTTP通信エラー ({apiUrl}): {ex.Message}");
                        _logger.LogError($"[{requestId}] HTTP通信エラースタックトレース: {ex.StackTrace}");
                        
                        if (ex.InnerException != null)
                        {
                            _logger.LogError($"[{requestId}] HTTP通信内部例外: {ex.InnerException.Message}");
                        }
                        
                        // ネットワークエラーの詳細分析
                        string httpErrorCategory = "不明なネットワークエラー";
                        string httpPossibleCause = "ネットワーク接続に問題があります";
                        
                        if (ex.Message.Contains("No route to host"))
                        {
                            httpErrorCategory = "ルーティングエラー";
                            httpPossibleCause = "対象ホストへのネットワークルートが存在しません";
                        }
                        else if (ex.Message.Contains("Connection refused"))
                        {
                            httpErrorCategory = "接続拒否エラー";
                            httpPossibleCause = "対象ポートでサービスが起動していない可能性があります";
                        }
                        else if (ex.Message.Contains("Name or service not known"))
                        {
                            httpErrorCategory = "DNS解決エラー";
                            httpPossibleCause = "ホスト名の解決に失敗しました";
                        }
                        else if (ex.Message.Contains("Network is unreachable"))
                        {
                            httpErrorCategory = "ネットワーク到達不可エラー";
                            httpPossibleCause = "ネットワークインフラに問題があります";
                        }
                        
                        _logger.LogError($"[{requestId}] エラー分類: {httpErrorCategory}");
                        _logger.LogError($"[{requestId}] 推定原因: {httpPossibleCause}");
                        
                        lastException = ex;
                        
                        // エラー発生時に即座にキャッシュ更新と再試行を実行
                        if (_cachedHealthyEndpoints.Any())
                        {
                            _logger.LogWarning($"[{requestId}] エラーが発生しました。キャッシュを更新して再試行します。");
                            var refreshedEndpoints = await RefreshEndpointsAsync();
                            
                            // 更新されたエンドポイントで再試行
                            var retryResult = await RetryWithRefreshedEndpoints(refreshedEndpoints, request, requestId, stopwatch);
                            if (retryResult != null) return retryResult;
                        }
                        
                        continue; // 次のエンドポイントを試す
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[{requestId}] 予期しないエラーが発生 ({apiUrl}): {ex.Message}");
                        _logger.LogError($"[{requestId}] 例外のスタックトレース: {ex.StackTrace}");
                        
                        if (ex.InnerException != null)
                        {
                            _logger.LogError($"[{requestId}] 内部例外: {ex.InnerException.Message}");
                        }
                        
                        lastException = ex;
                        continue; // 次のエンドポイントを試す
                    }
                }
                
                // すべてのエンドポイントが失敗した場合
                _logger.LogError($"[{requestId}] すべてのAPIエンドポイントでの接続に失敗しました");
                
                // エラー分析
                string errorCategory = "不明なネットワークエラー";
                string possibleCause = "ネットワーク接続に問題があります";
                
                if (lastException is HttpRequestException httpEx)
                {
                    if (httpEx.Message.Contains("No route to host"))
                    {
                        errorCategory = "ルーティングエラー";
                        possibleCause = "対象ホストへのネットワークルートが存在しません";
                    }
                    else if (httpEx.Message.Contains("Connection refused"))
                    {
                        errorCategory = "接続拒否エラー";
                        possibleCause = "対象ポートでサービスが起動していない可能性があります";
                    }
                    else if (httpEx.Message.Contains("Name or service not known"))
                    {
                        errorCategory = "DNS解決エラー";
                        possibleCause = "ホスト名の解決に失敗しました";
                    }
                }
                
                stopwatch.Stop();
                return StatusCode(502, new { 
                    error = "Tokenize APIとの通信中にエラーが発生しました", 
                    message = lastException?.Message,
                    status_code = lastException?.Data["StatusCode"],
                    error_category = errorCategory,
                    possible_cause = possibleCause,
                    inner_error = lastException?.InnerException?.Message,
                    is_network_restricted = true,
                    request_id = requestId,
                    processing_time_ms = stopwatch.ElapsedMilliseconds
                });
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError($"[{requestId}] 予期しないエラーが発生:");
                _logger.LogError($"[{requestId}]   - 例外タイプ: {ex.GetType().FullName}");
                _logger.LogError($"[{requestId}]   - メッセージ: {ex.Message}");
                _logger.LogError($"[{requestId}]   - HResult: 0x{ex.HResult:X8}");
                _logger.LogError($"[{requestId}]   - Source: {ex.Source ?? "不明"}");
                _logger.LogError($"[{requestId}]   - TargetSite: {ex.TargetSite?.ToString() ?? "不明"}");
                _logger.LogError($"[{requestId}]   - Data.Count: {ex.Data?.Count ?? 0}");
                
                if (ex.InnerException != null)
                {
                    _logger.LogError($"[{requestId}]   - InnerException: {ex.InnerException.GetType().FullName} - {ex.InnerException.Message}");
                }
                
                _logger.LogError($"[{requestId}]   - スタックトレース: {ex.StackTrace}");
                
                return StatusCode(500, new { 
                    error = "内部サーバーエラーが発生しました", 
                    message = ex.Message,
                    request_id = requestId,
                    processing_time_ms = stopwatch.ElapsedMilliseconds
                });
            }
        }
        
        /// <summary>
        /// 更新されたエンドポイントで再試行を実行
        /// </summary>
        private async Task<IActionResult> RetryWithRefreshedEndpoints(List<string> refreshedEndpoints, KeywordExtractionRequest request, string requestId, Stopwatch stopwatch)
        {
            foreach (var baseEndpoint in refreshedEndpoints)
            {
                string apiUrl = $"{baseEndpoint}/api/Tokenize";
                
                _logger.LogInformation($"[{requestId}] 更新されたエンドポイントで再試行: {apiUrl}");
                
                try
                {
                    var client = _clientFactory.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(30);
                    
                    var apiRequest = new TokenizeApiRequestModel
                    {
                        UserId = _configuration["DataIngestion:ExternalApiUserId"] ?? "ilu-demo",
                        Password = _configuration["DataIngestion:ExternalApiPassword"] ?? "ilupass",
                        Type = "",
                        Text = request.Text
                    };
                    
                    var options = new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = false
                    };
                    
                    var serializedRequest = JsonSerializer.Serialize(apiRequest, options);
                    var content = new StringContent(serializedRequest, Encoding.UTF8, "application/json");
                    
                    _logger.LogInformation($"[{requestId}] 再試行HTTP POST リクエスト送信開始");
                    var response = await client.PostAsync(apiUrl, content);
                    _logger.LogInformation($"[{requestId}] 再試行レスポンス: {(int)response.StatusCode} ({response.StatusCode})");
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        try
                        {
                            var tokenizeResponse = JsonSerializer.Deserialize<TokenizeApiResponse>(responseContent, options);
                            
                            if (tokenizeResponse?.TokenList != null)
                            {
                                var keywordList = tokenizeResponse.TokenList
                                    .Select(token => new KeywordItem
                                    {
                                        Surface = token.Text,
                                        Score = token.BoostScore
                                    })
                                    .ToList();
                                
                                stopwatch.Stop();
                                _logger.LogInformation($"[{requestId}] 再試行成功: 抽出キーワード数={keywordList.Count}");
                                
                                return Ok(new
                                {
                                    return_code = 0,
                                    keyword_list = keywordList,
                                    request_id = requestId,
                                    processing_time_ms = stopwatch.ElapsedMilliseconds
                                });
                            }
                        }
                        catch (JsonException jsonEx)
                        {
                            _logger.LogError($"[{requestId}] 再試行時のデシリアライズに失敗: {jsonEx.Message}");
                            continue;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[{requestId}] 再試行中にエラーが発生 ({apiUrl}): {ex.Message}");
                    continue;
                }
            }
            
            return null; // 再試行も失敗した場合はnullを返す
        }

        // ネットワーク接続テスト
        private async Task CheckNetworkConnectivity(string host, int port, string requestId)
        {
            try
            {
                _logger.LogInformation($"[{requestId}] ========== ネットワーク接続テスト開始 ==========");
                
                // 1. DNSに解決できるか確認
                IPHostEntry hostEntry = null;
                var dnsStopwatch = Stopwatch.StartNew();
                try
                {
                    hostEntry = await Dns.GetHostEntryAsync(host);
                    dnsStopwatch.Stop();
                    
                    var ipAddresses = string.Join(", ", hostEntry.AddressList.Select(ip => ip.ToString()));
                    _logger.LogInformation($"[{requestId}] DNS解決成功: {host} → {ipAddresses} ({dnsStopwatch.ElapsedMilliseconds}ms)");
                    
                    _logger.LogDebug($"[{requestId}] DNS詳細情報:");
                    _logger.LogDebug($"[{requestId}]   - HostName: {hostEntry.HostName}");
                    _logger.LogDebug($"[{requestId}]   - Aliases: {string.Join(", ", hostEntry.Aliases)}");
                    foreach (var ip in hostEntry.AddressList)
                    {
                        _logger.LogDebug($"[{requestId}]   - IP: {ip} (AddressFamily: {ip.AddressFamily})");
                    }
                }
                catch (SocketException ex)
                {
                    dnsStopwatch.Stop();
                    _logger.LogError($"[{requestId}] DNS解決エラー ({dnsStopwatch.ElapsedMilliseconds}ms):");
                    _logger.LogError($"[{requestId}]   - Host: {host}");
                    _logger.LogError($"[{requestId}]   - Message: {ex.Message}");
                    _logger.LogError($"[{requestId}]   - ErrorCode: {ex.ErrorCode}");
                    _logger.LogError($"[{requestId}]   - SocketErrorCode: {ex.SocketErrorCode}");
                    
                    // 11001はホスト名が解決できない場合
                    if (ex.ErrorCode == 11001)
                    {
                        _logger.LogError($"[{requestId}] DNS解決失敗: 社内DNSでのみ解決可能なホスト名である可能性があります");
                    }
                    return;
                }

                // 2. TCP接続テスト
                var tcpStopwatch = Stopwatch.StartNew();
                try
                {
                    using (var client = new TcpClient())
                    {
                        _logger.LogDebug($"[{requestId}] TCP接続テスト開始: {host}:{port}");
                        
                        var connectTask = client.ConnectAsync(host, port);
                        var timeoutTask = Task.Delay(5000); // 5秒タイムアウト
                        var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                        
                        tcpStopwatch.Stop();
                        
                        if (completedTask == connectTask && client.Connected)
                        {
                            _logger.LogInformation($"[{requestId}] TCP接続テスト成功: {host}:{port} ({tcpStopwatch.ElapsedMilliseconds}ms)");
                            _logger.LogDebug($"[{requestId}]   - LocalEndPoint: {client.Client.LocalEndPoint}");
                            _logger.LogDebug($"[{requestId}]   - RemoteEndPoint: {client.Client.RemoteEndPoint}");
                        }
                        else
                        {
                            _logger.LogWarning($"[{requestId}] TCP接続テスト失敗: {host}:{port} ({tcpStopwatch.ElapsedMilliseconds}ms)");
                            _logger.LogWarning($"[{requestId}]   - 原因: タイムアウト(5秒)または接続拒否");
                        }
                    }
                }
                catch (Exception ex)
                {
                    tcpStopwatch.Stop();
                    _logger.LogError($"[{requestId}] TCP接続テストエラー ({tcpStopwatch.ElapsedMilliseconds}ms):");
                    _logger.LogError($"[{requestId}]   - Host:Port: {host}:{port}");
                    _logger.LogError($"[{requestId}]   - Exception: {ex.GetType().Name}");
                    _logger.LogError($"[{requestId}]   - Message: {ex.Message}");
                    
                    if (ex is SocketException socketEx)
                    {
                        _logger.LogError($"[{requestId}]   - SocketErrorCode: {socketEx.SocketErrorCode}");
                        _logger.LogError($"[{requestId}]   - ErrorCode: {socketEx.ErrorCode}");
                    }
                }
                
                // 3. Pingテスト（オプション）
                var pingStopwatch = Stopwatch.StartNew();
                try
                {
                    using (var ping = new Ping())
                    {
                        _logger.LogDebug($"[{requestId}] Pingテスト開始: {host}");
                        
                        var reply = await ping.SendPingAsync(host, 3000);
                        pingStopwatch.Stop();
                        
                        if (reply.Status == IPStatus.Success)
                        {
                            _logger.LogInformation($"[{requestId}] Pingテスト成功: {host} (RoundTrip: {reply.RoundtripTime}ms, 測定時間: {pingStopwatch.ElapsedMilliseconds}ms)");
                            _logger.LogDebug($"[{requestId}]   - Address: {reply.Address}");
                            _logger.LogDebug($"[{requestId}]   - Options.Ttl: {reply.Options?.Ttl}");
                            _logger.LogDebug($"[{requestId}]   - Options.DontFragment: {reply.Options?.DontFragment}");
                            _logger.LogDebug($"[{requestId}]   - Buffer.Length: {reply.Buffer?.Length ?? 0}");
                        }
                        else
                        {
                            _logger.LogWarning($"[{requestId}] Pingテスト失敗: {host} - Status: {reply.Status} ({pingStopwatch.ElapsedMilliseconds}ms)");
                        }
                    }
                }
                catch (Exception ex)
                {
                    pingStopwatch.Stop();
                    _logger.LogError($"[{requestId}] Pingテストエラー ({pingStopwatch.ElapsedMilliseconds}ms):");
                    _logger.LogError($"[{requestId}]   - Host: {host}");
                    _logger.LogError($"[{requestId}]   - Exception: {ex.GetType().Name}");
                    _logger.LogError($"[{requestId}]   - Message: {ex.Message}");
                }
                
                _logger.LogInformation($"[{requestId}] ========== ネットワーク接続テスト完了 ==========");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{requestId}] ネットワーク接続テスト中に予期しないエラー:");
                _logger.LogError($"[{requestId}]   - Exception: {ex.GetType().FullName}");
                _logger.LogError($"[{requestId}]   - Message: {ex.Message}");
                _logger.LogError($"[{requestId}]   - StackTrace: {ex.StackTrace}");
            }
        }
    }

    // 新しいTokenize APIのリクエストモデル
    internal class TokenizeApiRequestModel
    {
        [JsonPropertyName("userId")]
        public string UserId { get; set; }
        
        [JsonPropertyName("password")]
        public string Password { get; set; }
        
        [JsonPropertyName("type")]
        public string Type { get; set; }
        
        [JsonPropertyName("text")]
        public string Text { get; set; }
    }

    // 従来形式のキーワードアイテム（互換性のため）
    internal class KeywordItem
    {
        [JsonPropertyName("surface")]
        public string Surface { get; set; }
        
        [JsonPropertyName("score")]
        public double Score { get; set; }
    }

    // 従来のリクエストモデル（互換性のため）
    public class KeywordExtractionRequest
    {
        public string UserId { get; set; } = "user";
        
        public string Password { get; set; } = "pass";
        
        public string Text { get; set; }
    }
} 