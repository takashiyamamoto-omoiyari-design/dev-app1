using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using System.IO;
using Amazon;
using Amazon.ElasticLoadBalancingV2;
using Amazon.ElasticLoadBalancingV2.Model;
using Amazon.Runtime.CredentialManagement;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace AzureRag.Services
{
    public interface IAutoStructureService
    {
        Task<AutoStructureResponse> GetStructuredDataAsync(string workId);
        Task<AnalyzeResponse> AnalyzeFileAsync(IFormFile file, string userId, string password, string type = null);
    }

    public class AutoStructureService : IAutoStructureService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<AutoStructureService> _logger;
        private string _apiUrl;
        private string _userId;
        private string _password;
        
        // AWS ALB設定
        private readonly string _awsProfile;
        private readonly RegionEndpoint _awsRegion;
        private readonly string _targetGroupArn;
        private readonly bool _inAwsEc2;
        private List<string> _cachedHealthyEndpoints;
        private DateTime _lastEndpointRefresh;
        private readonly TimeSpan _endpointCacheTimeout = TimeSpan.FromMinutes(1); // 1分間キャッシュ（短縮）

        private readonly IConfiguration _configuration;

        private readonly Microsoft.AspNetCore.Http.IHttpContextAccessor _httpContextAccessor;
        private readonly AzureRag.Services.UserDirectory.IExternalCredentialResolver _credentialResolver;

        public AutoStructureService(
            IHttpClientFactory httpClientFactory,
            ILogger<AutoStructureService> logger,
            IConfiguration configuration,
            Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor,
            AzureRag.Services.UserDirectory.IExternalCredentialResolver credentialResolver)
        {
            _httpClient = httpClientFactory.CreateClient("AutoStructureClient");
            _httpClient.Timeout = TimeSpan.FromSeconds(120); // タイムアウトを120秒に設定
            _logger = logger;
            _configuration = configuration;
            _httpContextAccessor = httpContextAccessor;
            _credentialResolver = credentialResolver;
            _apiUrl = "http://10.24.157.174:51000/AutoStructure/Check"; // フォールバック用
            _userId = _configuration["DataIngestion:ExternalApiUserId"] ?? "ilu-demo";
            _password = _configuration["DataIngestion:ExternalApiPassword"] ?? "ilupass";
            
            // AWS ALB設定
            _awsProfile = "ILURAG";
            _awsRegion = RegionEndpoint.APNortheast1;
            _targetGroupArn = "arn:aws:elasticloadbalancing:ap-northeast-1:311141529894:targetgroup/reception-tg/41aaeefba2c79028";
            _inAwsEc2 = true; // EC2上で実行するためtrueに設定
            _cachedHealthyEndpoints = new List<string>();
            _lastEndpointRefresh = DateTime.MinValue;
        }

        /// <summary>
        /// キャッシュを無効化してAWS ALBから最新のエンドポイントを取得
        /// </summary>
        private async Task<List<string>> RefreshEndpointsAsync()
        {
            _logger.LogWarning("キャッシュを無効化して、AWS ALBから最新のエンドポイントを取得します");
            _cachedHealthyEndpoints.Clear();
            _lastEndpointRefresh = DateTime.MinValue;
            return await GetHealthyEndpointsFromALBAsync();
        }

        /// <summary>
        /// EC2メタデータAPIから現在のインスタンスのプライベートIPアドレスを動的に取得
        /// </summary>
        private async Task<string> GetCurrentInstanceEndpointAsync()
        {
            try
            {
                _logger.LogInformation("EC2メタデータAPIから現在のプライベートIPアドレスを取得中...");
                
                using var metadataClient = new HttpClient();
                metadataClient.Timeout = TimeSpan.FromSeconds(5); // タイムアウトを短く設定
                
                // EC2メタデータAPIからプライベートIPアドレスを取得
                var response = await metadataClient.GetStringAsync("http://169.254.169.254/latest/meta-data/local-ipv4");
                var privateIp = response.Trim();
                
                _logger.LogInformation($"EC2メタデータから取得したプライベートIP: {privateIp}");
                
                // デフォルトポート51000でエンドポイントを構築
                return $"http://{privateIp}:51000";
            }
            catch (Exception ex)
            {
                _logger.LogError($"EC2メタデータAPIからのIP取得に失敗: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// AWS ALBターゲットグループから健全なエンドポイントを取得
        /// </summary>
        private async Task<List<string>> GetHealthyEndpointsFromALBAsync()
        {
            // キャッシュが有効な場合はキャッシュを返す
            if (_cachedHealthyEndpoints.Any() && 
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
                        _logger.LogWarning($"AWSプロファイル '{_awsProfile}' が見つかりません。EC2のIAMロールで再試行します。");
                        // AWSプロファイルが使えない場合はEC2のIAMロールを試す
                        client = new AmazonElasticLoadBalancingV2Client(_awsRegion);
                    }
                    else
                    {
                        client = new AmazonElasticLoadBalancingV2Client(credentials, _awsRegion);
                    }
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
                    _cachedHealthyEndpoints = healthyEndpoints;
                    _lastEndpointRefresh = DateTime.Now;
                    _logger.LogInformation($"AWS ALBから{healthyEndpoints.Count}個の健全なエンドポイントを取得しました");
                    return healthyEndpoints;
                }
                else
                {
                    _logger.LogWarning("AWS ALBから健全なエンドポイントが見つかりませんでした。EC2メタデータから動的IPを取得します。");
                    // フォールバック: EC2メタデータAPIから現在のプライベートIPを動的に取得
                    var dynamicEndpoint = await GetCurrentInstanceEndpointAsync();
                    return new List<string> { dynamicEndpoint };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"AWS ALBエンドポイント取得中にエラーが発生: {ex.Message}");
                _logger.LogError($"スタックトレース: {ex.StackTrace}");
                _logger.LogWarning("EC2メタデータから動的IPを取得してフォールバックします。");
                // フォールバック: EC2メタデータAPIから現在のプライベートIPを動的に取得
                try
                {
                    var dynamicEndpoint = await GetCurrentInstanceEndpointAsync();
                    return new List<string> { dynamicEndpoint };
                }
                catch (Exception metadataEx)
                {
                    _logger.LogError($"EC2メタデータ取得にも失敗: {metadataEx.Message}");
                    // 最後の手段として、設定から取得またはデフォルト値を使用
                    var fallbackIp = _configuration["AutoStructure:FallbackPrivateIP"] ?? "10.24.157.174";
                    _logger.LogWarning($"最終フォールバック: {fallbackIp}:51000 を使用します。");
                    return new List<string> { $"http://{fallbackIp}:51000" };
                }
            }
        }

        public async Task<AnalyzeResponse> AnalyzeFileAsync(IFormFile file, string userId, string password, string type = null)
        {
            // 可能ならサーバ側で現在ユーザーの資格に置換
            try
            {
                var name = _httpContextAccessor?.HttpContext?.User?.Identity?.Name;
                if (!string.IsNullOrEmpty(name))
                {
                    var resolved = await _credentialResolver.ResolveAsync(name);
                    if (resolved != null)
                    {
                        userId = resolved.ApiUser;
                        password = resolved.ApiPass;
                        _logger.LogInformation("[AnalyzeFileAsync] 資格解決: user={Name}, prefix={Prefix}", name, resolved.S3Prefix);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "資格解決に失敗。引数の資格/既定値を使用します");
            }
            _logger.LogInformation($"[AnalyzeFileAsync] 受信パラメータ確認: file={file?.FileName}, size={file?.Length ?? 0}B, userId={userId}, type={(string.IsNullOrWhiteSpace(type) ? "(none)" : type)}");
            // AWS ALBから健全なエンドポイントを取得
            var healthyEndpoints = await GetHealthyEndpointsFromALBAsync();
            
            Exception lastException = null;
            bool shouldRefreshCache = false;
            
            // 各健全なエンドポイントを順番に試す
            foreach (var baseEndpoint in healthyEndpoints)
            {
                string apiEndpoint = $"{baseEndpoint}/AutoStructure/Analyze";
                
                _logger.LogInformation($"外部APIのAnalyzeエンドポイントにアクセス開始: {apiEndpoint}");
                
                try
                {
                    // マルチパートフォームデータを作成
                    using var formContent = new MultipartFormDataContent();
                    
                    // ファイルの内容を追加
                    using var fileStream = file.OpenReadStream();
                    using var streamContent = new StreamContent(fileStream);
                    // コンテントタイプを明示的に設定
                    streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
                    // ファイル名を適切に設定（ファイル名に空白やUnicode文字が含まれる場合、問題が発生する可能性があるため）
                    var safeFileName = Uri.EscapeDataString(file.FileName);
                    formContent.Add(streamContent, "file", safeFileName);
                    
                    // 認証情報を追加 - 外部APIが期待するパラメータ名を正確に使用
                    formContent.Add(new StringContent(userId), "userid");
                    formContent.Add(new StringContent(password), "password");
                    if (!string.IsNullOrWhiteSpace(type))
                    {
                        formContent.Add(new StringContent(type), "type");
                        _logger.LogInformation($"[AnalyzeFileAsync] 送信フォームに type を追加: '{type}'");
                    }
                    else
                    {
                        _logger.LogInformation("[AnalyzeFileAsync] type は未指定のため送信フォームに含めません");
                    }
                    
                    _logger.LogInformation($"外部API呼び出し開始 - ファイル: {file.FileName}, サイズ: {file.Length}バイト");
                    _logger.LogInformation($"送信パラメータ: userid={userId}, password=***, type={(string.IsNullOrWhiteSpace(type) ? "(none)" : type)}");
                    _logger.LogInformation($"APIリクエスト実行開始: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                    
                    // POSTリクエスト送信
                    var response = await _httpClient.PostAsync(apiEndpoint, formContent);
                    
                    _logger.LogInformation($"APIリクエスト完了: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                    _logger.LogInformation($"APIレスポンスステータス: {(int)response.StatusCode} {response.StatusCode}");
                    
                    // レスポンスの処理
                    var responseContent = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation($"APIレスポンスの先頭500文字: {responseContent.Substring(0, Math.Min(500, responseContent.Length))}...");
                    
                    if (response.IsSuccessStatusCode)
                    {
                        try
                        {
                            var options = new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            };
                            
                            var result = JsonSerializer.Deserialize<AnalyzeResponse>(responseContent, options);
                            _logger.LogInformation($"Analyzeレスポンスのデシリアライズ成功: work_id={result.WorkId}, return_code={result.ReturnCode}");
                            
                            return result;
                        }
                        catch (JsonException jsonEx)
                        {
                            _logger.LogError($"Analyzeレスポンスのデシリアライズに失敗: {jsonEx.Message}");
                            _logger.LogError($"受信したJSON: {responseContent}");
                            lastException = new Exception($"APIレスポンスのパースに失敗しました: {jsonEx.Message}", jsonEx);
                            continue; // 次のエンドポイントを試す
                        }
                    }
                    else
                    {
                        _logger.LogError($"外部API呼び出しが失敗 - ステータスコード: {(int)response.StatusCode} {response.StatusCode}");
                        _logger.LogError($"エラーレスポンス内容: {responseContent}");
                        
                        // エラーレスポンスをパースして返す
                        try
                        {
                            var errorResponse = JsonSerializer.Deserialize<AnalyzeResponse>(responseContent);
                            return errorResponse;
                        }
                        catch
                        {
                            lastException = new Exception($"API呼び出しが失敗しました: {response.StatusCode}");
                            shouldRefreshCache = true; // HTTPエラーの場合はキャッシュ更新をマーク
                            continue; // 次のエンドポイントを試す
                        }
                    }
                }
                catch (HttpRequestException httpEx)
                {
                    _logger.LogError($"HTTP通信エラー ({apiEndpoint}): {httpEx.Message}");
                    _logger.LogError($"HTTP通信エラースタックトレース: {httpEx.StackTrace}");
                    
                    if (httpEx.InnerException != null)
                    {
                        _logger.LogError($"HTTP通信内部例外: {httpEx.InnerException.Message}");
                    }
                    
                    lastException = httpEx;
                    shouldRefreshCache = true; // 接続エラーの場合はキャッシュ更新をマーク
                    continue; // 次のエンドポイントを試す
                }
                catch (Exception ex)
                {
                    _logger.LogError($"外部API呼び出し中にエラーが発生 ({apiEndpoint}): {ex.Message}");
                    _logger.LogError($"例外のスタックトレース: {ex.StackTrace}");
                    
                    if (ex.InnerException != null)
                    {
                        _logger.LogError($"内部例外: {ex.InnerException.Message}");
                    }
                    
                    lastException = ex;
                    continue; // 次のエンドポイントを試す
                }
            }
            
            // すべてのエンドポイントが失敗し、キャッシュ更新が必要な場合
            if (shouldRefreshCache && _cachedHealthyEndpoints.Any())
            {
                _logger.LogWarning("すべてのキャッシュされたエンドポイントが失敗しました。キャッシュを更新して再試行します。");
                var refreshedEndpoints = await RefreshEndpointsAsync();
                
                // 更新されたエンドポイントで再試行
                foreach (var baseEndpoint in refreshedEndpoints)
                {
                    string apiEndpoint = $"{baseEndpoint}/AutoStructure/Analyze";
                    
                    _logger.LogInformation($"更新されたエンドポイントで再試行: {apiEndpoint}");
                    
                    try
                    {
                        // マルチパートフォームデータを作成
                        using var formContent = new MultipartFormDataContent();
                        
                        // ファイルの内容を追加
                        using var fileStream = file.OpenReadStream();
                        using var streamContent = new StreamContent(fileStream);
                        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
                        var safeFileName = Uri.EscapeDataString(file.FileName);
                        formContent.Add(streamContent, "file", safeFileName);
                        
                        // 認証情報を追加
                        formContent.Add(new StringContent(userId), "userid");
                        formContent.Add(new StringContent(password), "password");
                        if (!string.IsNullOrWhiteSpace(type))
                        {
                            formContent.Add(new StringContent(type), "type");
                            _logger.LogInformation($"[AnalyzeFileAsync][再試行] 送信フォームに type を追加: '{type}'");
                        }
                        else
                        {
                            _logger.LogInformation("[AnalyzeFileAsync][再試行] type は未指定のため送信フォームに含めません");
                        }
                        
                        _logger.LogInformation($"再試行APIリクエスト実行開始: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                        
                        // POSTリクエスト送信
                        var response = await _httpClient.PostAsync(apiEndpoint, formContent);
                        
                        _logger.LogInformation($"再試行APIリクエスト完了: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                        _logger.LogInformation($"再試行APIレスポンスステータス: {(int)response.StatusCode} {response.StatusCode}");
                        
                        if (response.IsSuccessStatusCode)
                        {
                            var responseContent = await response.Content.ReadAsStringAsync();
                            try
                            {
                                var options = new JsonSerializerOptions
                                {
                                    PropertyNameCaseInsensitive = true
                                };
                                
                                var result = JsonSerializer.Deserialize<AnalyzeResponse>(responseContent, options);
                                _logger.LogInformation($"再試行成功: work_id={result.WorkId}, return_code={result.ReturnCode}");
                                
                                return result;
                            }
                            catch (JsonException jsonEx)
                            {
                                _logger.LogError($"再試行時のデシリアライズに失敗: {jsonEx.Message}");
                                continue;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"再試行中にエラーが発生 ({apiEndpoint}): {ex.Message}");
                        continue;
                    }
                }
            }
            
            // すべてのエンドポイントが失敗した場合
            _logger.LogError("すべてのAPIエンドポイントでの接続に失敗しました");
            return new AnalyzeResponse
            {
                ReturnCode = 1,
                ErrorDetail = $"API呼び出し中にエラーが発生しました: {lastException?.Message ?? "不明なエラー"}"
            };
        }

        public async Task<AutoStructureResponse> GetStructuredDataAsync(string workId)
        {
            // 可能ならサーバ側で現在ユーザーの資格を解決
            try
            {
                var name = _httpContextAccessor?.HttpContext?.User?.Identity?.Name;
                if (!string.IsNullOrEmpty(name))
                {
                    var resolved = await _credentialResolver.ResolveAsync(name);
                    if (resolved != null)
                    {
                        _userId = resolved.ApiUser;
                        _password = resolved.ApiPass;
                        _logger.LogInformation("[GetStructuredDataAsync] 資格解決: user={Name}, prefix={Prefix}", name, resolved.S3Prefix);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "資格解決に失敗。既定値を使用します");
            }
            // 引数で渡されたwork_idを使用する
            // work_idが空または無効の場合はデフォルト値を使用
            if (string.IsNullOrEmpty(workId))
            {
                workId = "ff3bfb43437a02fde082fdc2af4a90e8";
                _logger.LogWarning($"無効なwork_idが指定されました。デフォルト値を使用します: {workId}");
            }
            
            _logger.LogInformation($"GetStructuredDataAsync呼び出し - work_id: {workId}");
            _logger.LogInformation($"【デバッグ】GetStructuredDataAsync開始: workId={workId}, userId={_userId}");
            
            // AWS ALBから健全なエンドポイントを取得
            _logger.LogInformation($"【デバッグ】AWS ALBから健全なエンドポイントを取得開始");
            var healthyEndpoints = await GetHealthyEndpointsFromALBAsync();
            _logger.LogInformation($"【デバッグ】健全なエンドポイント数: {healthyEndpoints.Count}");
            
            Exception lastException = null;
            bool shouldRefreshCache = false;
            
            // 各健全なエンドポイントを順番に試す
            _logger.LogInformation($"【デバッグ】エンドポイント試行開始: 対象数={healthyEndpoints.Count}");
            foreach (var baseEndpoint in healthyEndpoints)
            {
                string endpoint = $"{baseEndpoint}/AutoStructure/Check";
                
                try
                {
                    _logger.LogInformation($"APIエンドポイントを試行: {endpoint}");
                    _logger.LogInformation($"【デバッグ】エンドポイント試行開始 - baseEndpoint: {baseEndpoint}, workId: {workId}");
                    // ここで3秒待機してからチェック（外部APIの直後で未反映を吸収）
                    try { await Task.Delay(3000); } catch {}
                    
                    // JSONリクエストを生成
                    var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                    
                    // JSONデータを作成
                    var jsonData = new
                    {
                        work_id = workId,
                        userId = _userId,
                        password = _password
                    };
                    
                    var jsonContent = JsonSerializer.Serialize(jsonData);
                    _logger.LogInformation($"送信するJSONデータ: {jsonContent}");
                    
                    request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                    
                    // 明示的にヘッダーを設定
                    request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                    
                    _logger.LogInformation($"APIリクエスト実行開始: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}");
                    var response = await _httpClient.SendAsync(request);
                    _logger.LogInformation($"APIリクエスト完了: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}");
                    _logger.LogInformation($"APIレスポンスステータス: {(int)response.StatusCode} {response.StatusCode}");

                    if (response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        _logger.LogInformation($"APIレスポンスの先頭500文字: {responseContent.Substring(0, Math.Min(500, responseContent.Length))}...");
                        
                        try
                        {
                            var options = new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            };
                            
                            var result = JsonSerializer.Deserialize<AutoStructureResponse>(responseContent, options);
                            
                            _logger.LogInformation($"デシリアライズ成功: TextList={result.TextList?.Count ?? 0}件, ChunkList={result.ChunkList?.Count ?? 0}件, SynonymList={result.SynonymList?.Count ?? 0}件, SynonymData={result.SynonymData?.Count ?? 0}件");
                            
                            // シノニムデータの詳細ログ出力
                            if (result.SynonymList != null && result.SynonymList.Count > 0)
                            {
                                _logger.LogInformation($"【シノニムリスト詳細】全{result.SynonymList.Count}件");
                                for (int i = 0; i < Math.Min(5, result.SynonymList.Count); i++)
                                {
                                    var synonymItem = result.SynonymList[i];
                                    if (synonymItem?.Synonyms != null)
                                    {
                                        _logger.LogInformation($"  シノニム[{i}]: [{string.Join(", ", synonymItem.Synonyms)}]");
                                    }
                                }
                                if (result.SynonymList.Count > 5)
                                {
                                    _logger.LogInformation($"  ... 他{result.SynonymList.Count - 5}件");
                                }
                            }
                            
                            if (result.SynonymData != null && result.SynonymData.Count > 0)
                            {
                                _logger.LogInformation($"【シノニムデータ詳細】全{result.SynonymData.Count}件");
                                for (int i = 0; i < Math.Min(5, result.SynonymData.Count); i++)
                                {
                                    var synonymDataItem = result.SynonymData[i];
                                    _logger.LogInformation($"  シノニムデータ[{i}]: {JsonSerializer.Serialize(synonymDataItem)}");
                                }
                                if (result.SynonymData.Count > 5)
                                {
                                    _logger.LogInformation($"  ... 他{result.SynonymData.Count - 5}件");
                                }
                            }
                            
                            // 成功したエンドポイントを記憶（次回使用のため）
                            _apiUrl = endpoint;
                            
                            return result;
                        }
                        catch (JsonException jsonEx)
                        {
                            _logger.LogError($"APIレスポンスのJSONデシリアライズに失敗: {jsonEx.Message}");
                            _logger.LogError($"JSONデシリアライズエラーのスタックトレース: {jsonEx.StackTrace}");
                            _logger.LogError($"受信したJSON: {responseContent}");
                            lastException = jsonEx;
                            continue; // 次のエンドポイントを試す
                        }
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        _logger.LogError($"API呼び出しが失敗 - ステータスコード: {(int)response.StatusCode} {response.StatusCode}");
                        _logger.LogError($"エラーレスポンス内容: {errorContent}");
                        _logger.LogError($"【デバッグ】エラー発生エンドポイント: {endpoint}");
                        _logger.LogError($"【デバッグ】送信したworkId: {workId}");
                        _logger.LogError($"【デバッグ】送信したuserId: {_userId}");
                        _logger.LogError($"【デバッグ】レスポンスヘッダー: {response.Headers}");
                        
                        // 500エラーでもJSONレスポンスが返されている場合は、それを処理する
                        if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError && 
                            !string.IsNullOrEmpty(errorContent) && 
                            errorContent.TrimStart().StartsWith("{"))
                        {
                            try
                            {
                                _logger.LogInformation($"500エラーですが、JSONレスポンスが返されているため、エラー状態として処理します: {workId}");
                                var options = new JsonSerializerOptions
                                {
                                    PropertyNameCaseInsensitive = true
                                };
                                
                                var errorResult = JsonSerializer.Deserialize<AutoStructureResponse>(errorContent, options);
                                if (errorResult != null)
                                {
                                    _logger.LogInformation($"エラー状態のworkId {workId} を処理: state={errorResult.State}, return_code={errorResult.ReturnCode}");
                                    return errorResult; // エラー状態のレスポンスを返す
                                }
                            }
                            catch (JsonException jsonEx)
                            {
                                _logger.LogError($"500エラーレスポンスのJSONデシリアライズに失敗: {jsonEx.Message}");
                            }
                        }
                        
                        lastException = new Exception($"API request failed with status code: {response.StatusCode}, Response: {errorContent}");
                        shouldRefreshCache = true; // HTTPエラーの場合はキャッシュ更新をマーク
                        continue; // 次のエンドポイントを試す
                    }
                }
                catch (HttpRequestException httpEx)
                {
                    _logger.LogError($"HTTP通信エラー ({endpoint}): {httpEx.Message}");
                    _logger.LogError($"HTTP通信エラースタックトレース: {httpEx.StackTrace}");
                    
                    if (httpEx.InnerException != null)
                    {
                        _logger.LogError($"HTTP通信内部例外: {httpEx.InnerException.Message}");
                        _logger.LogError($"HTTP通信内部例外スタックトレース: {httpEx.InnerException.StackTrace}");
                    }
                    
                    lastException = httpEx;
                    shouldRefreshCache = true; // 接続エラーの場合はキャッシュ更新をマーク
                    continue; // 次のエンドポイントを試す
                }
                catch (Exception ex)
                {
                    _logger.LogError($"AutoStructure API呼び出し中に一般エラー発生 ({endpoint}): {ex.Message}");
                    _logger.LogError($"一般エラースタックトレース: {ex.StackTrace}");
                    
                    if (ex.InnerException != null)
                    {
                        _logger.LogError($"一般エラー内部例外: {ex.InnerException.Message}");
                        _logger.LogError($"一般エラー内部例外スタックトレース: {ex.InnerException.StackTrace}");
                    }
                    
                    lastException = ex;
                    continue; // 次のエンドポイントを試す
                }
            }
            
            // すべてのエンドポイントが失敗し、キャッシュ更新が必要な場合
            if (shouldRefreshCache && _cachedHealthyEndpoints.Any())
            {
                _logger.LogWarning("すべてのキャッシュされたエンドポイントが失敗しました。キャッシュを更新して再試行します。");
                var refreshedEndpoints = await RefreshEndpointsAsync();
                
                // 更新されたエンドポイントで再試行
                foreach (var baseEndpoint in refreshedEndpoints)
                {
                    string endpoint = $"{baseEndpoint}/AutoStructure/Check";
                    
                    _logger.LogInformation($"更新されたエンドポイントで再試行: {endpoint}");
                    
                    try
                    {
                        // JSONリクエストを生成
                        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                        
                        // JSONデータを作成
                        var jsonData = new
                        {
                            work_id = workId,
                            userId = _userId,
                            password = _password
                        };
                        
                        var jsonContent = JsonSerializer.Serialize(jsonData);
                        request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                        
                        _logger.LogInformation($"再試行APIリクエスト実行開始: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}");
                        var response = await _httpClient.SendAsync(request);
                        _logger.LogInformation($"再試行APIリクエスト完了: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}");
                        _logger.LogInformation($"再試行APIレスポンスステータス: {(int)response.StatusCode} {response.StatusCode}");

                        if (response.IsSuccessStatusCode)
                        {
                            var responseContent = await response.Content.ReadAsStringAsync();
                            try
                            {
                                var options = new JsonSerializerOptions
                                {
                                    PropertyNameCaseInsensitive = true
                                };
                                
                                var result = JsonSerializer.Deserialize<AutoStructureResponse>(responseContent, options);
                                _logger.LogInformation($"再試行成功: TextList={result.TextList?.Count ?? 0}件, ChunkList={result.ChunkList?.Count ?? 0}件");
                                
                                return result;
                            }
                            catch (JsonException jsonEx)
                            {
                                _logger.LogError($"再試行時のデシリアライズに失敗: {jsonEx.Message}");
                                continue;
                            }
                        }
                        else
                        {
                            // 500エラーでもJSONレスポンスが返されている場合は、それを処理する
                            var errorContent = await response.Content.ReadAsStringAsync();
                            _logger.LogError($"再試行API呼び出しが失敗 - ステータスコード: {(int)response.StatusCode} {response.StatusCode}");
                            _logger.LogError($"再試行エラーレスポンス内容: {errorContent}");
                            
                            if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError && 
                                !string.IsNullOrEmpty(errorContent) && 
                                errorContent.TrimStart().StartsWith("{"))
                            {
                                try
                                {
                                    _logger.LogInformation($"再試行500エラーですが、JSONレスポンスが返されているため、エラー状態として処理します: {workId}");
                                    var options = new JsonSerializerOptions
                                    {
                                        PropertyNameCaseInsensitive = true
                                    };
                                    
                                    var errorResult = JsonSerializer.Deserialize<AutoStructureResponse>(errorContent, options);
                                    if (errorResult != null)
                                    {
                                        _logger.LogInformation($"再試行エラー状態のworkId {workId} を処理: state={errorResult.State}, return_code={errorResult.ReturnCode}");
                                        return errorResult; // エラー状態のレスポンスを返す
                                    }
                                }
                                catch (JsonException jsonEx)
                                {
                                    _logger.LogError($"再試行500エラーレスポンスのJSONデシリアライズに失敗: {jsonEx.Message}");
                                }
                            }
                            
                            continue; // 次のエンドポイントを試す
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"再試行中にエラーが発生 ({endpoint}): {ex.Message}");
                        continue;
                    }
                }
            }
            
            // すべてのエンドポイントが失敗した場合
            _logger.LogError("すべてのAPIエンドポイントでの接続に失敗しました");
            _logger.LogError($"【デバッグ】試行したエンドポイント数: {healthyEndpoints.Count}");
            _logger.LogError($"【デバッグ】最後の例外: {lastException?.Message}");
            _logger.LogError($"【デバッグ】最後の例外スタックトレース: {lastException?.StackTrace}");
            _logger.LogError($"【デバッグ】workId: {workId}");
            _logger.LogError($"【デバッグ】userId: {_userId}");
            _logger.LogError($"【デバッグ】キャッシュ更新必要フラグ: {shouldRefreshCache}");
            
            if (lastException != null)
            {
                throw lastException;
            }
            else
            {
                throw new Exception("すべてのAPIエンドポイントでの接続に失敗しましたが、具体的なエラーはありません");
            }
        }
    }

    public enum ProcessingState 
    {
        NotStarted,    // 0/0状態
        InProgress,    // 処理中
        Completed      // 完了
    }

    public class AutoStructureResponse
    {
        [JsonPropertyName("text_list")]
        public List<TextItem> TextList { get; set; }

        // ページ単位のテキスト（1ページ=1要素）
        [JsonPropertyName("page_text_list")]
        public List<TextItem> PageTextList { get; set; }

        [JsonPropertyName("chunk_list")]
        public List<ChunkItem> ChunkList { get; set; }

        [JsonPropertyName("synonym_list")]
        public List<SynonymItem> SynonymList { get; set; }
        
        [JsonPropertyName("synonym")]
        public List<object> SynonymData { get; set; }
        
        [JsonPropertyName("status")]
        public List<StatusItem> Status { get; set; }
        
        [JsonPropertyName("return_code")]
        public int ReturnCode { get; set; }
        
        [JsonPropertyName("error_detail")]
        public string ErrorDetail { get; set; }
        
        // statusオブジェクトから値を取得するプロパティ
        public int State => Status?.FirstOrDefault()?.State ?? 0;
        public int PageNo => Status?.FirstOrDefault()?.PageNo ?? 0;
        public int MaxPageNo => Status?.FirstOrDefault()?.MaxPageNo ?? 0;

        /// <summary>
        /// 処理状態を判定します
        /// </summary>
        public ProcessingState GetProcessingState()
        {
            int pageNo = PageNo;
            int maxPageNo = MaxPageNo;
            bool hasContent = (ChunkList?.Count ?? 0) > 0 || 
                             (TextList?.Count ?? 0) > 0 ||
                             (SynonymList?.Count ?? 0) > 0 ||
                             (SynonymData?.Count ?? 0) > 0;
            
            if (pageNo == 0 && maxPageNo == 0 && !hasContent)
            {
                return ProcessingState.NotStarted;
            }
            else if (pageNo < maxPageNo || !hasContent)
            {
                return ProcessingState.InProgress;
            }
            else
            {
                return ProcessingState.Completed;
            }
        }
    }

    public class TextItem
    {
        [JsonPropertyName("text")]
        public string Text { get; set; }
        
        [JsonPropertyName("page_no")]
        public int PageNo { get; set; }
    }

        public class ChunkItem
    {
        [JsonPropertyName("chunk")]
        public string Chunk { get; set; }

        [JsonPropertyName("chunk_no")]
        public int ChunkNo { get; set; }

        [JsonPropertyName("page_no")]
        public int PageNo { get; set; }

        [JsonPropertyName("work_id")]
        public string WorkId { get; set; }
    }

    public class SynonymItem
    {
        [JsonPropertyName("keyword")]
        public string Keyword { get; set; }
        
        [JsonPropertyName("synonym")]
        public List<string> Synonyms { get; set; } = new List<string>();
    }

    public class StatusItem
    {
        [JsonPropertyName("state")]
        public int State { get; set; }
        
        [JsonPropertyName("page_no")]
        public int PageNo { get; set; }
        
        [JsonPropertyName("max_page_no")]
        public int MaxPageNo { get; set; }
    }

    public class AnalyzeResponse
    {
        [JsonPropertyName("work_id")]
        public string WorkId { get; set; }
        
        [JsonPropertyName("return_code")]
        public int ReturnCode { get; set; }
        
        [JsonPropertyName("error_detail")]
        public string ErrorDetail { get; set; }
    }
} 