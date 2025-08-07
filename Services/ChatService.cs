using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AzureRag.Models;
using AzureRag.Models.Chat;
using Microsoft.Extensions.Logging;

namespace AzureRag.Services
{
    /// <summary>
    /// チャット機能を提供するサービスクラス
    /// </summary>
    public class ChatService : IChatService
    {
        private readonly IAzureSearchService _searchService;
        private readonly IAzureOpenAIService _openAIService;
        private readonly ILogger<ChatService> _logger;
        private readonly string _sessionsDirectory = "chat_sessions";

        public ChatService(
            IAzureSearchService searchService,
            IAzureOpenAIService openAIService,
            ILogger<ChatService> logger)
        {
            _searchService = searchService;
            _openAIService = openAIService;
            _logger = logger;
            
            // ストレージディレクトリの確認・作成 (権限問題を回避するため一時的にコメントアウト)
            // EnsureStorageDirectoryExists();
        }

        /// <summary>
        /// チャットリクエストに対する回答を生成する
        /// </summary>
        public async Task<AzureRag.Models.Chat.ChatResponse> GenerateResponseAsync(ChatRequest request)
        {
            _logger.LogInformation($"チャットリクエスト処理開始: クエリ「{request.Query}」, セッションID: {request.SessionId ?? "新規"}");
            
            // セッション処理
            ChatSession session;
            if (string.IsNullOrEmpty(request.SessionId))
            {
                // 新規セッションの作成
                session = await CreateSessionAsync();
                _logger.LogInformation($"新規セッション作成: {session.Id}");
            }
            else
            {
                // 既存セッションの取得
                session = await GetSessionByIdAsync(request.SessionId);
                if (session == null)
                {
                    _logger.LogWarning($"セッションが見つかりません: {request.SessionId}, 新規作成します");
                    session = await CreateSessionAsync();
                }
            }
            
            // ユーザーからのメッセージを追加
            var userMessage = new ChatMessage
            {
                SessionId = session.Id,
                Role = "user",
                Content = request.Query
            };
            session.Messages.Add(userMessage);
            
            // Azure Searchでコンテンツを検索
            var searchResults = await _searchService.SearchDocumentsAsync(request.Query);
            _logger.LogInformation($"検索結果: {searchResults.Count}件");
            
            // 検索結果を基にAIの回答を生成
            string systemPromptText = !string.IsNullOrEmpty(request.SystemPrompt) 
                ? request.SystemPrompt 
                : "あなたは親切なアシスタントです。ユーザーの質問に対して、与えられた情報源を基に正確に回答してください。情報源に含まれていない内容については、「情報が見つかりません」と伝えてください。";
            
            // 履歴を構築（最大5往復まで）
            var chatHistory = new List<(string role, string content)>();
            
            // 過去の会話履歴を取得（最大5往復分）
            var recentMessages = session.Messages
                .OrderByDescending(m => m.CreatedAt)
                .Take(10) // 最大5往復（ユーザー5 + AI5）
                .OrderBy(m => m.CreatedAt)
                .ToList();
            
            foreach (var message in recentMessages)
            {
                chatHistory.Add((message.Role, message.Content));
            }
            
            // AIの回答を生成
            var answer = await _openAIService.GenerateAnswerWithHistoryAsync(
                request.Query,
                searchResults.Select(r => r.Content).ToList(),
                systemPromptText,
                chatHistory);
            
            _logger.LogInformation($"AIの回答を生成: {answer.Substring(0, Math.Min(50, answer.Length))}...");
            
            // AIの回答をセッションに追加
            var assistantMessage = new ChatMessage
            {
                SessionId = session.Id,
                Role = "assistant",
                Content = answer
            };
            session.Messages.Add(assistantMessage);
            
            // セッションの更新日時を更新
            session.LastUpdatedAt = DateTime.UtcNow;
            
            // 最初のメッセージでセッション名を更新
            if (session.Messages.Count <= 2)
            {
                // 最初の質問をセッション名に設定（最大30文字まで）
                string name = request.Query;
                if (name.Length > 30)
                {
                    name = name.Substring(0, 27) + "...";
                }
                session.Name = name;
                _logger.LogInformation($"セッション名を更新: {session.Name}");
            }
            
            // セッションを保存
            await SaveSessionAsync(session);
            
            // レスポンスを構築
            // 検索結果をスコア（関連性）が高い順にソートし、DocumentSearchResultに変換
            var sortedSources = searchResults
                .OrderByDescending(r => r.Score)
                .Select(r => new AzureRag.Models.DocumentSearchResult
                {
                    Id = r.Id,
                    Title = r.Title ?? "無題",
                    Content = r.Content,
                    Score = r.Score
                })
                .ToList();
            
            var response = new AzureRag.Models.Chat.ChatResponse
            {
                SessionId = session.Id,
                Answer = answer,
                Sources = sortedSources,  // ソート済みのソースを使用
                History = session.Messages
            };
            
            return response;
        }

        /// <summary>
        /// チャットセッション一覧を取得する
        /// </summary>
        public async Task<List<ChatSession>> GetSessionsAsync()
        {
            _logger.LogInformation("チャットセッション一覧を取得");
            
            List<ChatSession> sessions = new List<ChatSession>();
            
            try
            {
                // セッションディレクトリが存在しない場合は空のリストを返す
                if (!Directory.Exists(_sessionsDirectory))
                {
                    return sessions;
                }
                
                // セッションファイル一覧を取得
                string[] sessionFiles = Directory.GetFiles(_sessionsDirectory, "*.json");
                
                foreach (var file in sessionFiles)
                {
                    try
                    {
                        string json = await File.ReadAllTextAsync(file);
                        var session = JsonSerializer.Deserialize<ChatSession>(json);
                        if (session != null)
                        {
                            sessions.Add(session);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"セッションファイルの読み込みエラー: {file}");
                    }
                }
                
                // 更新日時の降順でソート
                sessions = sessions.OrderByDescending(s => s.LastUpdatedAt).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "セッション一覧の取得中にエラーが発生しました");
            }
            
            _logger.LogInformation($"セッション一覧を取得しました: {sessions.Count}件");
            return sessions;
        }

        /// <summary>
        /// チャットセッションをIDで取得する
        /// </summary>
        public async Task<ChatSession> GetSessionByIdAsync(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return null;
            }
            
            _logger.LogInformation($"セッションを取得: {sessionId}");
            
            try
            {
                string filePath = GetSessionFilePath(sessionId);
                if (!File.Exists(filePath))
                {
                    _logger.LogWarning($"セッションファイルが見つかりません: {filePath}");
                    return null;
                }
                
                string json = await File.ReadAllTextAsync(filePath);
                var session = JsonSerializer.Deserialize<ChatSession>(json);
                
                _logger.LogInformation($"セッションを取得しました: {sessionId}, メッセージ数: {session?.Messages?.Count ?? 0}");
                return session;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"セッションの取得中にエラーが発生しました: {sessionId}");
                return null;
            }
        }

        /// <summary>
        /// 新しいチャットセッションを作成する
        /// </summary>
        public async Task<ChatSession> CreateSessionAsync(string name = null)
        {
            var session = new ChatSession();
            
            if (!string.IsNullOrEmpty(name))
            {
                session.Name = name;
            }
            
            _logger.LogInformation($"新規セッションを作成: {session.Id}, 名前: {session.Name}");
            
            // セッションを保存
            await SaveSessionAsync(session);
            
            return session;
        }

        /// <summary>
        /// チャットセッションを削除する
        /// </summary>
        public async Task<bool> DeleteSessionAsync(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return false;
            }
            
            _logger.LogInformation($"セッションを削除: {sessionId}");
            
            try
            {
                string filePath = GetSessionFilePath(sessionId);
                if (!File.Exists(filePath))
                {
                    _logger.LogWarning($"削除対象のセッションファイルが見つかりません: {filePath}");
                    return false;
                }
                
                File.Delete(filePath);
                _logger.LogInformation($"セッションを削除しました: {sessionId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"セッションの削除中にエラーが発生しました: {sessionId}");
                return false;
            }
        }

        /// <summary>
        /// セッションをファイルに保存する
        /// </summary>
        private async Task SaveSessionAsync(ChatSession session)
        {
            if (session == null)
            {
                return;
            }
            
            try
            {
                string filePath = GetSessionFilePath(session.Id);
                string json = JsonSerializer.Serialize(session, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                await File.WriteAllTextAsync(filePath, json);
                _logger.LogInformation($"セッションを保存しました: {session.Id}, ファイルパス: {filePath}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"セッションの保存中にエラーが発生しました: {session.Id}");
            }
        }

        /// <summary>
        /// セッションファイルのパスを取得する
        /// </summary>
        private string GetSessionFilePath(string sessionId)
        {
            return Path.Combine(_sessionsDirectory, $"{sessionId}.json");
        }

        /// <summary>
        /// ストレージディレクトリの存在を確認し、存在しない場合は作成する
        /// </summary>
        private void EnsureStorageDirectoryExists()
        {
            try
            {
                if (!Directory.Exists(_sessionsDirectory))
                {
                    Directory.CreateDirectory(_sessionsDirectory);
                    _logger.LogInformation($"セッションディレクトリを作成しました: {_sessionsDirectory}");
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning($"セッションディレクトリ作成権限がありません。チャット機能時に遅延作成を試行します。\n" +
                    $"手動で権限を設定する場合: sudo mkdir -p {_sessionsDirectory} && sudo chown -R $(whoami):$(whoami) {_sessionsDirectory}\n" +
                    $"エラー詳細: {ex.Message}");
                // アプリケーションの起動を継続（例外を投げない）
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"セッションディレクトリ作成中にエラーが発生しました。チャット機能時に遅延作成を試行します。エラー: {ex.Message}");
                // アプリケーションの起動を継続（例外を投げない）
            }
        }
    }
}