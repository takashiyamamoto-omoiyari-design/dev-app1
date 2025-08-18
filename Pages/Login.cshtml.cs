using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AzureRag.Pages
{
    public class LoginModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<LoginModel> _logger;
        private readonly Dictionary<string, UserCredentials> _users;

        [TempData]
        public string ErrorMessage { get; set; }

        public LoginModel(IConfiguration configuration, ILogger<LoginModel> logger)
        {
            _configuration = configuration;
            _logger = logger;
            
            // 設定ファイルからユーザー情報を読み込み
            _users = new Dictionary<string, UserCredentials>();
            var usersSection = _configuration.GetSection("Users");
            
            foreach (var userSection in usersSection.GetChildren())
            {
                var username = userSection.Key;
                var password = userSection["Password"];
                var role = userSection["Role"];
                
                if (!string.IsNullOrEmpty(password) && !string.IsNullOrEmpty(role))
                {
                    _users[username] = new UserCredentials 
                    { 
                        Password = password, 
                        Role = role 
                    };
                    _logger.LogInformation($"設定ファイルからユーザーを読み込み: {username} (Role: {role})");
                }
                else
                {
                    _logger.LogWarning($"無効なユーザー設定をスキップ: {username}");
                }
            }
            
            _logger.LogInformation($"合計 {_users.Count} 人のユーザーを設定ファイルから読み込みました");
        }

        private string GetBasePath()
        {
            var path = HttpContext.Request.Path.Value;
            var pathBase = HttpContext.Request.PathBase.Value;
            _logger.LogInformation($"GetBasePath: 現在のパス = {path}, PathBase = {pathBase}");
            
            // PathBaseがある場合はそれを使用
            if (!string.IsNullOrEmpty(pathBase))
            {
                _logger.LogInformation($"GetBasePath: PathBaseを返します = {pathBase}");
                return pathBase;
            }
            
            // フォールバック: パスから判定
            if (path != null && path.StartsWith("/demo-app2"))
            {
                _logger.LogInformation($"GetBasePath: /demo-app2 を返します");
                return "/demo-app2";
            }
            
            _logger.LogInformation($"GetBasePath: 空文字を返します");
            return "";
        }

        public void OnGet()
        {
            // すでにログインしていればリダイレクト
            if (User.Identity.IsAuthenticated)
            {
                var basePath = GetBasePath();
                Response.Redirect($"{basePath}/DataStructuring");
            }
        }

        public async Task<IActionResult> OnPostAsync(string username, string password)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ErrorMessage = "ユーザー名とパスワードを入力してください。";
                return Page();
            }

            // ユーザー認証情報を検証
            if (_users.TryGetValue(username, out var userCredentials) && password == userCredentials.Password)
            {
                // 認証クレームを作成
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, username),
                    new Claim(ClaimTypes.Role, userCredentials.Role)
                };

                var claimsIdentity = new ClaimsIdentity(
                    claims, CookieAuthenticationDefaults.AuthenticationScheme);

                var authProperties = new AuthenticationProperties
                {
                    // 認証持続時間を設定
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8),
                    IsPersistent = true
                };

                // サインイン
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity),
                    authProperties);

                // データ構造化ページにリダイレクト
                var basePath = GetBasePath();
                return Redirect($"{basePath}/DataStructuring");
            }
            else
            {
                ErrorMessage = "ユーザー名またはパスワードが正しくありません。";
                return Page();
            }
        }
    }

    // ユーザー認証情報を保持するクラス
    public class UserCredentials
    {
        public string Password { get; set; }
        public string Role { get; set; }
    }
} 