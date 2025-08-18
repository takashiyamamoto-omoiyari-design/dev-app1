// サイトの初期化
document.addEventListener('DOMContentLoaded', function() {
    // DOM要素
    const tabAnswer = document.getElementById('tab-answer');
    const tabSource = document.getElementById('tab-source');
    const contentAnswer = document.getElementById('content-answer');
    const contentSource = document.getElementById('content-source');
    const chatMessages = document.getElementById('chat-messages');
    const sourcesList = document.getElementById('sources-list');
    const sourcesCount = document.getElementById('sources-count');
    const emptySources = document.getElementById('empty-sources');
    const queryInput = document.getElementById('query-input');
    const sendButton = document.getElementById('send-button');
    const loadingIndicator = document.getElementById('loading-indicator');
    
    // エレメントのロギング（デバッグ用）
    console.log("DOM要素の取得状況：", {
        tabAnswer: !!tabAnswer,
        tabSource: !!tabSource,
        contentAnswer: !!contentAnswer,
        contentSource: !!contentSource,
        chatMessages: !!chatMessages,
        sourcesList: !!sourcesList,
        sourcesCount: !!sourcesCount,
        emptySources: !!emptySources,
        queryInput: !!queryInput,
        sendButton: !!sendButton,
        loadingIndicator: !!loadingIndicator
    });
    
    // サイドバートグル処理
    const sidebarToggle = document.getElementById('sidebar-toggle');
    const sidebar = document.getElementById('sidebar');
    const mainContent = document.getElementById('main-content');
    const toggleIcon = document.getElementById('toggle-icon');
    const chatFooter = document.querySelector('.chat-footer');
    const clearChatButton = document.getElementById('clear-chat-button');
    
    // サイドバー関連の要素が存在する場合のみ初期化
    if (sidebarToggle && sidebar && mainContent) {
        // サイドバーの開閉状態
        let sidebarCollapsed = false;
        
        // サイドバークリアボタンの処理
        if (clearChatButton) {
            clearChatButton.addEventListener('click', clearChat);
        }
        
        // サイドバートグルのクリックイベント
        sidebarToggle.addEventListener('click', toggleSidebar);
        
        // サイドバー開閉関数
        function toggleSidebar() {
            sidebarCollapsed = !sidebarCollapsed;
            
            if (sidebarCollapsed) {
                sidebar.classList.add('collapsed');
                mainContent.classList.add('expanded');
                if (chatFooter) {
                    chatFooter.classList.add('expanded');
                    console.log("フッター拡張");
                }
                toggleIcon.classList.remove('fa-chevron-left');
                toggleIcon.classList.add('fa-chevron-right');
            } else {
                sidebar.classList.remove('collapsed');
                mainContent.classList.remove('expanded');
                if (chatFooter) {
                    chatFooter.classList.remove('expanded');
                    console.log("フッター標準");
                }
                toggleIcon.classList.remove('fa-chevron-right');
                toggleIcon.classList.add('fa-chevron-left');
            }
        };
    } else {
        console.log("サイドバー機能の初期化に失敗しました:", {
            sidebarToggle: !!sidebarToggle,
            sidebar: !!sidebar,
            mainContent: !!mainContent
        });
    }
    
    // 要素が存在しない場合は処理を終了（古いページ構造の場合）
    if (!tabAnswer || !tabSource || !queryInput || !sendButton) {
        console.log('新しいチャットUIが見つかりません。ページをリロードしてください。');
        return;
    }
    
    // セッションIDを保持する変数（マルチターン対応）
    let currentSessionId = null;
    
    // タブクリック時の処理
    tabAnswer.addEventListener('click', function() {
        tabAnswer.classList.add('active');
        tabSource.classList.remove('active');
        contentAnswer.style.display = 'block';
        contentSource.style.display = 'none';
    });
    
    tabSource.addEventListener('click', function() {
        tabSource.classList.add('active');
        tabAnswer.classList.remove('active');
        contentSource.style.display = 'block';
        contentAnswer.style.display = 'none';
    });
    
    // 送信ボタンのクリックイベント
    sendButton.addEventListener('click', sendMessage);
    
    // Enterキーでの送信
    queryInput.addEventListener('keydown', function(event) {
        if (event.key === 'Enter' && !event.shiftKey) {
            event.preventDefault();
            sendMessage();
        }
    });
    
    // チャットをクリア
    function clearChat() {
        // チャットメッセージをクリア
        if (chatMessages) {
            while (chatMessages.firstChild) {
                chatMessages.removeChild(chatMessages.firstChild);
            }
            
            // 初期のAIメッセージを追加（画面初期表示時と同様）
            const messageDiv = document.createElement('div');
            messageDiv.className = 'chat-message';
            messageDiv.innerHTML = `
                <div class="chat-message-header">
                    <div class="chat-avatar avatar-ai">AI</div>
                    <strong>AI</strong>
                </div>
                <div class="chat-message-content">
                    <p>こんにちは！ILU データ構造化ソリューションへようこそ。</p>
                </div>
            `;
            chatMessages.appendChild(messageDiv);
        }
        
        // ソースリストをクリア
        if (sourcesList) {
            while (sourcesList.firstChild) {
                sourcesList.removeChild(sourcesList.firstChild);
            }
        }
        
        // 表示状態をリセット
        if (emptySources) {
            emptySources.style.display = 'block';
        }
        
        if (sourcesCount) {
            sourcesCount.textContent = '0';
        }
        
        // 入力フィールドをクリア
        queryInput.value = '';
        
        // セッションIDをリセット（新しいチャットを開始）
        currentSessionId = null;
        
        // フォーカスを入力フィールドに戻す
        queryInput.focus();
        
        // 回答タブに切り替え
        if (tabAnswer && contentAnswer && tabSource && contentSource) {
            tabAnswer.classList.add('active');
            tabSource.classList.remove('active');
            contentAnswer.style.display = 'block';
            contentSource.style.display = 'none';
        }
    }
    
    // メッセージを送信する関数
    function sendMessage() {
        const message = queryInput.value.trim();
        if (!message) return;
        
        // 入力フィールドをクリア
        queryInput.value = '';
        
        // ユーザーメッセージをUIに追加
        addUserMessage(message);
        
        // ローディングを表示
        if (loadingIndicator) {
            loadingIndicator.style.display = 'flex';
        }
        
        // 回答タブに移動
        tabAnswer.classList.add('active');
        tabSource.classList.remove('active');
        contentAnswer.style.display = 'block';
        contentSource.style.display = 'none';
        
        // ベースパスを取得
        const getBasePath = () => {
            const pathSegments = window.location.pathname.split('/');
                if (pathSegments.length > 1 && pathSegments[1] === 'demo-app2') {
        return '/demo-app2';
            }
            return '';
        };
        
        // サーバーにリクエスト
        fetch(getBasePath() + "/api/chat/send", {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                query: message,  // ここを正しく 'query' として送信
                sessionId: currentSessionId,
                systemPrompt: "あなたは株式会社言語理解研究所(ILU)のRAGアシスタントです。" // 必須フィールド
            })
        })
        .then(response => {
            if (!response.ok) {
                throw new Error('APIリクエストエラー: ' + response.status);
            }
            return response.json();
        })
        .then(data => {
            console.log("API応答データ:", data); // デバッグ用
            
            // ローディングを非表示
            if (loadingIndicator) {
                loadingIndicator.style.display = 'none';
            }
            
            // セッションIDを保存（マルチターン対応）
            if (data.sessionId) {
                currentSessionId = data.sessionId;
            }
            
            // AIの回答をUIに追加
            addAIMessage(data.answer);
            
            // ソースの更新
            updateSources(data.sources || []);
            
            // 下にスクロール
            scrollToBottom();
        })
        .catch(error => {
            console.error('Error:', error);
            if (loadingIndicator) {
                loadingIndicator.style.display = 'none';
            }
            addErrorMessage('通信エラーが発生しました: ' + error.message);
        });
    }
    
    // ユーザーメッセージを追加
    function addUserMessage(message) {
        if (!chatMessages) return;
        
        const messageDiv = document.createElement('div');
        messageDiv.className = 'chat-message';
        messageDiv.innerHTML = `
            <div class="chat-message-header">
                <div class="chat-avatar avatar-user">U</div>
                <strong>あなた</strong>
            </div>
            <div class="chat-message-content">
                <p>${escapeHtml(message)}</p>
            </div>
        `;
        chatMessages.appendChild(messageDiv);
        scrollToBottom();
    }
    
    // AIメッセージを追加
    function addAIMessage(message) {
        if (!chatMessages) return;
        
        const messageDiv = document.createElement('div');
        messageDiv.className = 'chat-message';
        messageDiv.innerHTML = `
            <div class="chat-message-header">
                <div class="chat-avatar avatar-ai">AI</div>
                <strong>AI</strong>
            </div>
            <div class="chat-message-content">
                <p>${message.replace(/\n/g, '<br>')}</p>
            </div>
        `;
        chatMessages.appendChild(messageDiv);
        scrollToBottom();
    }
    
    // エラーメッセージを追加
    function addErrorMessage(message) {
        if (!chatMessages) return;
        
        const errorDiv = document.createElement('div');
        errorDiv.className = 'error-message';
        errorDiv.innerHTML = `
            <i class="fas fa-exclamation-circle"></i>
            ${escapeHtml(message)}
        `;
        chatMessages.appendChild(errorDiv);
        scrollToBottom();
    }
    
    // ソース一覧を更新
    function updateSources(sources) {
        console.log("ソース更新:", sources.length, "件", sourcesList ? "要素あり" : "要素なし");
        
        // 必須要素がない場合はエラーを表示して終了
        if (!sourcesList || !sourcesCount) {
            console.error("ソースリスト要素が見つかりません");
            return;
        }
        
        // カウンターの更新（表示件数上限に更新）
        sourcesCount.textContent = sources.length;
        
        // ソースリストのクリア
        while (sourcesList.firstChild) {
            sourcesList.removeChild(sourcesList.firstChild);
        }
        
        // ソースが0件の場合
        if (sources.length === 0) {
            if (emptySources) {
                emptySources.style.display = 'block';
            }
            return;
        }
        
        if (emptySources) {
            emptySources.style.display = 'none';
        }
        
        // スコア順に並べられていることを前提とする（サーバー側でソート済み）
        // ソースの追加
        sources.forEach((source, index) => {
            // スコア表示用のHTMLを生成（企画書サンプル.pdfの場合はスコア表示を削除）
            let scoreHtml = '';
            if (source.score && !isNaN(source.score) && source.title && !source.title.includes('企画書サンプル.pdf')) {
                // スコアを小数点第2位までで表示用に整形
                const scoreFormatted = Math.round(source.score * 100) / 100;
                scoreHtml = `<span style="color: #666; font-size: 0.8rem;">(スコア: ${scoreFormatted})</span>`;
            }
            
            // [TRUNCATED]の検出
            const isTruncated = source.content && source.content.includes("[TRUNCATED]");
            const truncatedClass = isTruncated ? "truncated-content" : "";
            
            // ソースアイテムの作成
            const sourceItem = document.createElement('div');
            sourceItem.className = 'source-list-item';
            
            // テキストが3行以上ある場合は折りたたみUIを使用
            const textContent = source.content || '';
            const lines = textContent.split("\n");
            const hasMoreLines = lines.length > 3;
            
            sourceItem.innerHTML = `
                <div style="font-size: 0.9rem; width: 1.5rem; height: 1.5rem; display: flex; align-items: center; justify-content: center; color: white !important; background-color: black !important; border-radius: 50%; margin-right: 0.75rem; font-weight: 600; min-width: 1.5rem; text-align: center;">${index + 1}</div>
                <div class="source-details">
                    <div class="source-title">${escapeHtml(source.title || '')} ${scoreHtml}</div>
                    <div class="source-description collapsed" data-full-text="${escapeHtml(textContent)}">${getFirstThreeLines(textContent)}</div>
                    ${hasMoreLines ? '<div class="source-more-link"><a href="#" class="show-full-text-link">くわしくみる</a></div>' : ''}
                </div>
            `;
            sourcesList.appendChild(sourceItem);
        });
    }
    
    // 最下部にスクロール
    function scrollToBottom() {
        window.scrollTo(0, document.body.scrollHeight);
        if (contentAnswer) {
            contentAnswer.scrollTop = contentAnswer.scrollHeight;
        }
    }
    
    // HTMLエスケープ
    function escapeHtml(unsafe) {
        if (typeof unsafe !== 'string') return '';
        return unsafe
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#039;");
    }
    
    // テキスト表示を改善：最初は3行に制限（リンクの追加はHTMLテンプレートに移動しました）
    function getFirstThreeLines(text) {
        if (!text) return "";
        
        // [TRUNCATED]が含まれていないか確認（Azure Searchのテキスト切り詰め対策）
        const isTruncated = text.includes("[TRUNCATED]");
        if (isTruncated) {
            console.log("切り詰められたテキストを検出:", text.length);
        }
        
        const lines = text.split("\n");
        const firstThreeLines = lines.slice(0, 3).join("\n");
        
        // 最初の3行だけを返す（HTMLエスケープ済み）
        return escapeHtml(firstThreeLines);
    }
    
    // 「くわしくみる」リンク処理のイベント設定を一元化（イベント委任）
    document.addEventListener('click', function(event) {
        // くわしくみるリンクのクリックをキャプチャ
        if (event.target && event.target.classList.contains('show-full-text-link')) {
            event.preventDefault();
            
            const linkElement = event.target;
            const parentDiv = linkElement.closest('.source-details');
            
            if (parentDiv) {
                const descriptionDiv = parentDiv.querySelector('.source-description');
                
                if (descriptionDiv) {
                    // data-full-text属性から全文を取得
                    const fullText = descriptionDiv.getAttribute('data-full-text') || '';
                    const isShortened = linkElement.textContent === 'くわしくみる';
                    
                    if (isShortened) {
                        // フルテキストを表示（改行を保持して表示）しスクロール可能に
                        descriptionDiv.innerHTML = fullText.replace(/\n/g, '<br>');
                        descriptionDiv.classList.remove('collapsed');
                        descriptionDiv.classList.add('expanded');
                        linkElement.textContent = '折りたたむ';
                    } else {
                        // 元の3行に戻す
                        const lines = fullText.split("\n");
                        const firstThreeLines = escapeHtml(lines.slice(0, 3).join("\n"));
                        
                        // 元の状態に戻す
                        descriptionDiv.innerHTML = firstThreeLines;
                        descriptionDiv.classList.remove('expanded');
                        descriptionDiv.classList.add('collapsed');
                        linkElement.textContent = 'くわしくみる';
                    }
                }
            }
            
            return false;
        }
        
        // アップロードボタンのクリックイベント
        if (event.target && (
            event.target.textContent.trim() === 'アップロード' || 
            event.target.className.includes('upload-btn') || 
            event.target.id === 'uploadBtn'
        )) {
            event.preventDefault();
            event.stopPropagation();
            
            // 疎通確認エンドポイントを呼び出す
            callHealthCheckEndpoint();
            
            return false;
        }
    });
    
    // 疎通確認エンドポイントを呼び出す関数
    function callHealthCheckEndpoint() {
        console.log('疎通確認エンドポイントを呼び出します');
        
        // ローディングUI表示（簡易版）
        const loadingEl = document.createElement('div');
        loadingEl.id = 'health-check-loading';
        loadingEl.style.position = 'fixed';
        loadingEl.style.top = '0';
        loadingEl.style.left = '0';
        loadingEl.style.width = '100%';
        loadingEl.style.height = '100%';
        loadingEl.style.backgroundColor = 'rgba(0, 0, 0, 0.5)';
        loadingEl.style.display = 'flex';
        loadingEl.style.alignItems = 'center';
        loadingEl.style.justifyContent = 'center';
        loadingEl.style.zIndex = '10000';
        
        const msgEl = document.createElement('div');
        msgEl.textContent = 'サーバーと通信中...';
        msgEl.style.color = 'white';
        msgEl.style.padding = '20px';
        msgEl.style.backgroundColor = 'rgba(0, 0, 0, 0.7)';
        msgEl.style.borderRadius = '5px';
        
        loadingEl.appendChild(msgEl);
        document.body.appendChild(loadingEl);
        
        // フェッチAPIで疎通確認エンドポイントを呼び出す
                    fetch('http://10.24.130.200:51000/AutoStructure/HealthCheck')
            .then(response => {
                if (!response.ok) {
                    throw new Error('Network response was not ok');
                }
                return response.json();
            })
            .then(data => {
                // ローディング削除
                document.body.removeChild(loadingEl);
                
                // トースト表示
                showDirectUploadToast(data.message || 'サーバー疎通完了', 'success');
                console.log('疎通確認成功:', data);
            })
            .catch(error => {
                // ローディング削除
                if (document.getElementById('health-check-loading')) {
                    document.body.removeChild(loadingEl);
                }
                
                // エラー表示
                showDirectUploadToast('サーバー接続エラー', 'error');
                console.error('疎通確認エラー:', error);
            });
    }
    
    // 直接アップロード用のトースト通知
    function showDirectUploadToast(message, type = 'info') {
        // 既存のトーストを削除
        const existingToast = document.getElementById('direct-upload-toast');
        if (existingToast) {
            document.body.removeChild(existingToast);
        }
        
        // トースト要素を作成
        const toast = document.createElement('div');
        toast.id = 'direct-upload-toast';
        toast.innerHTML = message; // textContentからinnerHTMLに変更して<br>タグをサポート
        toast.style.position = 'fixed';
        toast.style.bottom = '20px';
        toast.style.right = '20px';
        toast.style.padding = '12px 20px';
        toast.style.borderRadius = '4px';
        toast.style.fontSize = '14px';
        toast.style.zIndex = '10000';
        toast.style.boxShadow = '0 2px 10px rgba(0, 0, 0, 0.2)';
        
        // タイプによって色を変更
        switch (type) {
            case 'success':
                toast.style.backgroundColor = '#4CAF50';
                toast.style.color = 'white';
                break;
            case 'error':
                toast.style.backgroundColor = '#F44336';
                toast.style.color = 'white';
                break;
            case 'warning':
                toast.style.backgroundColor = '#FF9800';
                toast.style.color = 'black';
                break;
            default:
                toast.style.backgroundColor = '#2196F3';
                toast.style.color = 'white';
        }
        
        document.body.appendChild(toast);
        
        // 5秒後に消える
        setTimeout(function() {
            if (document.body.contains(toast)) {
                document.body.removeChild(toast);
            }
        }, 5000);
    }
});
