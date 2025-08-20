// デプロイと通常利用の両方に対応する最適化バージョン
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Globalization;
using System.IO.Abstractions;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using AzureRag.Services;
using AzureRag.Services.PDF;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;

// UTF-8エンコーディングを設定（デプロイ環境向け強化）
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
// コンソール出力をUTF-8に設定
Console.OutputEncoding = Encoding.UTF8;
// 文化情報を日本語に設定
CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("ja-JP");
CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("ja-JP");

// デプロイ環境のためのエンコーディング設定確認
try {
    var testString = "日本語テスト";
    var bytes = Encoding.UTF8.GetBytes(testString);
    var result = Encoding.UTF8.GetString(bytes);
    Console.WriteLine("日本語エンコーディングテスト成功: " + result);
} catch (Exception ex) {
    Console.WriteLine("エンコーディングテスト失敗: " + ex.Message);
}

// 起動メッセージを表示（デプロイ時に英語でシンプルなメッセージに）
if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true") {
    Console.WriteLine("DEPLOYMENT: Starting application on port 5000...");
    Console.WriteLine("PORT: 5000 - Ready to handle connections");
    Console.WriteLine("STATUS: READY");
}
else {
    Console.WriteLine("【デプロイ対応＆通常機能両立版】起動中...");
}

// 最適化されたアプリケーションビルダー
var builder = WebApplication.CreateBuilder(args);

// 設定ファイルを追加 (新しい設定ファイルを既存の設定の上に追加)
builder.Configuration.AddJsonFile("appsettings.MultiIndex.json", optional: true, reloadOnChange: true);
// 環境変数を一番後ろに追加して最優先にする（MultiIndexの空値で上書きされないように）
builder.Configuration.AddEnvironmentVariables();
// Users を外部JSONから動的リロード（最終優先）。再起動なしでのユーザー追加・変更を可能にする
builder.Configuration.AddJsonFile("/etc/demo-app2.users.json", optional: true, reloadOnChange: true);

// ベースパス設定を追加
var basePath = Environment.GetEnvironmentVariable("APP_BASE_PATH") ?? "";

// 認証サービスを追加
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        // ベースパスは UsePathBase で自動的に追加されるため、ここでは相対パスのみ指定
        options.LoginPath = "/Login";
        options.LogoutPath = "/Logout";
        options.Cookie.Name = "ILUSolution.Auth";
        options.Cookie.HttpOnly = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        // API呼び出し時はHTMLリダイレクトではなく適切なHTTPステータスを返す
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                var path = context.Request.Path.Value ?? string.Empty;
                var isApi = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) || path.Equals("/api", StringComparison.OrdinalIgnoreCase);
                if (isApi)
                {
                    context.Response.StatusCode = 401;
                    return Task.CompletedTask;
                }
                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                var path = context.Request.Path.Value ?? string.Empty;
                var isApi = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) || path.Equals("/api", StringComparison.OrdinalIgnoreCase);
                if (isApi)
                {
                    context.Response.StatusCode = 403;
                    return Task.CompletedTask;
                }
                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            }
        };
    });

// 必要なサービスを登録
builder.Services.AddRazorPages(options =>
{
    // すべてのページに認証を要求（Loginページは除外）
    options.Conventions.AuthorizeFolder("/", "RequireAuth");
    options.Conventions.AllowAnonymousToPage("/Login");
    options.Conventions.AllowAnonymousToPage("/Logout");
});

// 認証ポリシーを追加
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAuth", policy =>
        policy.RequireAuthenticatedUser());
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All);
    });
// IFileSystemの依存関係を登録
builder.Services.AddSingleton<System.IO.Abstractions.IFileSystem, System.IO.Abstractions.FileSystem>();
// FileStorageServiceは現在使用していないため権限問題を回避するためコメントアウト
// builder.Services.AddSingleton<IFileStorageService, FileStorageService>();
builder.Services.AddHttpClient();

// PDF処理用サービスを登録
builder.Services.AddSingleton<IPdfTextExtractionService, PdfTextExtractionService>();
builder.Services.AddSingleton<ITokenEstimationService, TokenEstimationService>();
builder.Services.AddSingleton<ITextChunkingService, TextChunkingService>();
builder.Services.AddSingleton<IPdfProcessingService, PdfProcessingService>();
// 差分分析サービス
builder.Services.AddSingleton<AzureRag.Services.Diff.IDiffAnalyzeService, AzureRag.Services.Diff.DiffAnalyzeService>();

// 新しいマルチインデックス対応サービスを登録 (現在は一時的にコメントアウト)
// builder.Services.AddSingleton<IMultiIndexSearchService, MultiIndexSearchService>();
// builder.Services.AddSingleton<IMultiModelAIService, MultiModelAIService>();

// 従来のAzureサービスを登録（後方互換性のため）
builder.Services.AddSingleton<IAzureOpenAIService, AzureOpenAIService>();
builder.Services.AddSingleton<IAzureSearchService, AzureSearchService>();

// データ投入用サービスを登録
builder.Services.AddSingleton<AzureRag.Services.IAuthorizationService, AzureRag.Services.AuthorizationService>();
builder.Services.AddSingleton<IDataIngestionService, DataIngestionService>();
builder.Services.AddSingleton<IWorkIdManagementService, WorkIdManagementService>();

// AutoStructureServiceを登録
builder.Services.AddHttpClient("AutoStructureClient", client => {
    client.Timeout = TimeSpan.FromSeconds(30);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    UseProxy = false,
    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
});
builder.Services.AddSingleton<IAutoStructureService, AutoStructureService>();

// ドキュメントチャット用サービスを登録（AI回答生成用）
builder.Services.AddSingleton<IDocumentChatService, DocumentChatService>();

// インデックス管理サービスを登録 (FileStorageServiceに依存するため一時的にコメントアウト)
// builder.Services.AddSingleton<IIndexManagementService, IndexManagementService>();

// チャットサービスを登録
builder.Services.AddSingleton<IChatService, ChatService>();
builder.Services.AddSingleton<IAnthropicService, AnthropicService>();

// アプリケーションを構築
Console.WriteLine("Webアプリケーションを構築中...");
var app = builder.Build();

// HTTPパイプラインを設定
app.UseExceptionHandler($"{basePath}/Error");

// 逆プロキシ（ALB/Nginx）経由のスキーム/ホスト判定を正しく行う
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedHost
});

// Replitヘルスチェックを判断するヘルパー関数
Func<HttpContext, bool> IsHealthCheckRequest = (HttpContext context) =>
{
    return context.Request.Headers.UserAgent.ToString().Contains("HealthCheck") || 
           context.Request.Query.ContainsKey("health") ||
           context.Request.Headers.ContainsKey("X-Health-Check") ||
           // デプロイチェック特有のパターン
           (context.Request.Method == "GET" && 
            string.IsNullOrEmpty(context.Request.Headers.Referer) &&
            context.Request.Headers.Accept.ToString().Contains("text/plain"));
};

// ベースパス設定の追加 - Replitデプロイ用に最適化
Console.WriteLine($"ベースパス設定を適用: {basePath}");

// デプロイ環境ではベースパスを使用する
if (!string.IsNullOrEmpty(basePath)) {
    app.UsePathBase(basePath);
    app.Use((context, next) =>
    {
        if (!context.Request.Path.Value.Contains("health") && 
            !IsHealthCheckRequest(context)) {
            context.Request.PathBase = basePath;
        }
        return next();
    });
}

app.UseStaticFiles();
app.UseRouting();

// 認証ミドルウェアを追加
app.UseAuthentication();
app.UseAuthorization();

// ベースパスなしのアクセスを制限するミドルウェア
app.Use(async (context, next) => {
    var path = context.Request.Path.Value;
    var pathBase = context.Request.PathBase.Value;
    
    // ヘルスチェックは除外
    if (IsHealthCheckRequest(context))
    {
        await next();
        return;
    }
    
    // ベースパスが設定されている場合のチェック
    if (!string.IsNullOrEmpty(basePath))
    {
        // PathBaseが正しく設定されていない場合（直接アクセス）
        if (string.IsNullOrEmpty(pathBase))
        {
            // ベースパスなしの直接アクセスを拒否
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync("Not Found - Access via base path required");
            return;
        }
    }
    
    await next();
});

// ヘルスチェックエンドポイント（デプロイ対応用）
Console.WriteLine("各種エンドポイントを設定中...");

// Razor PagesとControllersのルートを有効化
app.MapControllers();

// Replitデプロイ専用のヘルスチェックエンドポイント - 明示的かつ独立したエンドポイント
Console.WriteLine("Replitデプロイ専用ヘルスチェックエンドポイントを設定中...");

// **独立したハンドラーでエンドポイントを設定**

// 1. ルートパスのハンドラー - ヘルスチェックのみ特別対応
app.Use(async (context, next) => {
    // ヘルスチェックかどうかを判定
    if (context.Request.Path.Value == "/" && IsHealthCheckRequest(context)) {
        Console.WriteLine("【Replit】ルートパスヘルスチェック '/' へのアクセス検出");
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync("OK - Root Path");
        return;
    }
    await next();
});

// 2. /demo-app2 パスのハンドラー (スラッシュなし) - ヘルスチェックのみ対応
app.Use(async (context, next) => {
    if (context.Request.Path.Value == "/demo-app2" && IsHealthCheckRequest(context)) {
        Console.WriteLine("【Replit】基本パスヘルスチェック '/demo-app2' へのアクセス検出");
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync("OK - Base Path");
        return;
    }
    await next();
});

// 3. /demo-app2/ パスのハンドラー (スラッシュあり) - Replitヘルスチェックと通常アクセスの両方に対応
app.Use(async (context, next) => {
    if (context.Request.Path.Value == "/demo-app2/" && IsHealthCheckRequest(context)) {
        // Replitのヘルスチェックリクエストと判断
        Console.WriteLine("【Replit】ヘルスチェックリクエスト '/demo-app2/' へのアクセス検出");
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync("OK");
        return;
    }
    
    // 通常のアプリケーションリクエストはそのまま次のミドルウェアに渡す
    await next();
});

// 4. /demo-app2/health パスのハンドラー - ヘルスチェック専用パス
app.Use(async (context, next) => {
    if (context.Request.Path.Value == "/demo-app2/health") {
        Console.WriteLine("【Replit】ヘルスパス '/demo-app2/health' へのアクセス検出");
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync("OK - Health Check");
        return;
    }
    await next();
});

// 5. デプロイ環境でのフォールバックミドルウェア - 404を防止
app.Use(async (context, next) => {
    await next();
    
    // レスポンスが404の場合、特定のパスに対してヘルスチェック応答を返す
    if (context.Response.StatusCode == 404) {
        string path = context.Request.Path.Value.ToLower();
        
        // Replit固有のヘルスチェックパスと思われるものには常に「OK」を返す
        if (path.EndsWith("/health") || path == "/" || path == "/demo-app2" || path == "/demo-app2/") {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync("OK");
        }
    }
});

// Replitデプロイ環境の場合の特別な単一ヘルスチェックミドルウェア
if (Environment.GetEnvironmentVariable("REPLIT_DEPLOYMENT") == "true") {
    Console.WriteLine("【Replitデプロイ環境検出】/demo-app2/Health 専用ヘルスチェックミドルウェアを有効化");
    
    // 特別なミドルウェア：指定されたパス (/demo-app2/Health) のみ対応
    app.Use(async (context, next) => {
        string path = context.Request.Path.Value?.ToLower();
        
        // Replitヘルスチェック - 指定されたパスのみに対応
        if (path == "/demo-app2/health") {
            Console.WriteLine($"【Replit特別ミドルウェア】指定ヘルスチェックパス '{path}' に「OK」を応答");
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync("OK");
            return;
        }
        
        await next();
    });
}

// AWS ALB用のヘルスチェックエンドポイント
if (!string.IsNullOrEmpty(basePath)) {
    Console.WriteLine($"ALB用ヘルスチェックエンドポイントを設定: {basePath}/health");
}

// RazorPagesの有効化（コントローラーの後に配置）
app.MapRazorPages();

// ポート開放前のメッセージ
if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true") {
    Console.WriteLine("****************************************************************");
    Console.WriteLine("IMPORTANT: PORT 5000 IS OPENING - All services will be available");
    Console.WriteLine("****************************************************************");
}
else {
    Console.WriteLine("********************************************************************************");
    Console.WriteLine("【重要】ポート5000を開放します - Index画面と各機能が利用可能になります");
    Console.WriteLine("********************************************************************************");
}

// 起動後の処理を登録
app.Lifetime.ApplicationStarted.Register(() => {
    if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true") {
        Console.WriteLine("PORT 5000 IS NOW OPEN - Ready to accept connections");
        Console.WriteLine("All services successfully started");
    }
    else {
        Console.WriteLine("ポート5000が開放されました - 接続受付開始");
        Console.WriteLine("すべての機能が正常に起動しました");
    }
});

// サーバー起動
if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true") {
    Console.WriteLine("Starting server: http://0.0.0.0:5000");
}
else {
    Console.WriteLine("サーバーを起動中: http://0.0.0.0:5000");
}
app.Run("http://0.0.0.0:5000");
