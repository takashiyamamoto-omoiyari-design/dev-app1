// ファイルアップロード処理を管理するモジュール
const fileUploader = (function() {
    // 設定
    const config = {
        userId: "ilu-demo",
        password: "ilupass",
        // APIエンドポイント（/Checkと同じプライベートIPを使用）
        apiUrl: "http://10.24.130.200:51000/AutoStructure/Analyze"
    };

    // ユーザー認証情報取得
    async function getCurrentUser() {
        const getBasePath = () => {
            const pathSegments = window.location.pathname.split('/');
            if (pathSegments.length > 1 && pathSegments[1] === 'trial-app2') {
                return '/trial-app2';
            }
            return '';
        };
        try {
            const response = await fetch(getBasePath() + '/api/data-structuring/current-user', {
                method: 'GET',
                credentials: 'include' // ASP.NET認証クッキーを含める
            });
            
            if (response.ok) {
                const userInfo = await response.json();
                console.log('ユーザー認証情報を取得しました:', userInfo.user?.username);
                return userInfo;
            } else {
                console.error('認証に失敗しました。ログインが必要です。');
                throw new Error('認証に失敗しました');
            }
        } catch (error) {
            console.error('認証情報の取得中にエラーが発生しました:', error);
            throw error;
        }
    }

    // 初期化処理
    function init() {
        // アップロードボタンのイベントリスナーを設定
        const uploadButton = document.getElementById("uploadButton");
        if (uploadButton) {
            uploadButton.addEventListener("click", handleUploadClick);
        }
    }

    // アップロードボタンクリック時の処理
    function handleUploadClick() {
        // ファイル選択ダイアログを表示
        const fileInput = document.createElement("input");
        fileInput.type = "file";
        fileInput.accept = ".pdf";
        fileInput.style.display = "none";
        document.body.appendChild(fileInput);

        // ファイルが選択された時の処理
        fileInput.addEventListener("change", async function() {
            if (fileInput.files.length > 0) {
                const selectedFile = fileInput.files[0];
                // ファイルをアップロード
                await uploadFile(selectedFile);
            }
            // 一時的なinput要素を削除
            document.body.removeChild(fileInput);
        });

        // ファイル選択ダイアログを開く
        fileInput.click();
    }

    // ファイルアップロード処理
    async function uploadFile(file) {
        try {
            // プログレスインジケーターを表示
            showLoading("PDFをアップロード中...");

            // 実際のログインユーザー情報を取得
            const currentUser = await getCurrentUser();
            if (!currentUser || !currentUser.user) {
                throw new Error('ユーザー認証情報の取得に失敗しました');
            }

            // FormDataオブジェクトを作成
            const formData = new FormData();
            formData.append("file", file);
            formData.append("userId", config.userId); // 外部API認証用（固定値）
            formData.append("password", config.password); // 外部API認証用（固定値）
            formData.append("login_user", currentUser.user.username); // 実際のログインユーザー

            // XMLHttpRequestを使用してファイルをアップロード
            const xhr = new XMLHttpRequest();
            
            // 指定したプライベートIPのURLを使用
            xhr.open("POST", config.apiUrl, true);
            
            // レスポンス処理
            xhr.onload = function() {
                hideLoading();
                
                if (xhr.status === 200) {
                    try {
                        const response = JSON.parse(xhr.responseText);
                        const workId = response.work_id;
                        
                        if (workId) {
                            showToast(`アップロード成功: work_id = ${workId}`, "success");
                            console.log(`work_id: ${workId}`);
                        } else {
                            showToast("work_idが見つかりませんでした", "warning");
                            console.log("work_idが見つかりませんでした。レスポンス:", xhr.responseText);
                        }
                    } catch (e) {
                        showToast("レスポンスの解析に失敗しました", "error");
                        console.error("JSONの解析に失敗しました。レスポンス:", xhr.responseText);
                    }
                } else {
                    showToast(`エラー: ステータスコード ${xhr.status}`, "error");
                    console.error("エラー。レスポンス内容:", xhr.responseText);
                }
            };
            
            // エラー処理
            xhr.onerror = function() {
                hideLoading();
                showToast("ネットワークエラーが発生しました", "error");
                console.error("ネットワークエラーが発生しました");
            };
            
            // アップロード進捗の処理
            xhr.upload.onprogress = function(event) {
                if (event.lengthComputable) {
                    const percentComplete = (event.loaded / event.total) * 100;
                    updateLoadingProgress(percentComplete);
                }
            };
            
            // リクエスト送信
            xhr.send(formData);
            
        } catch (error) {
            hideLoading();
            showToast(`エラー: ${error.message}`, "error");
            console.error("ファイルアップロードエラー:", error);
        }
    }

    // ローディング表示
    function showLoading(message) {
        // すでに存在する場合は削除
        hideLoading();
        
        // ローディング要素を作成
        const loadingOverlay = document.createElement("div");
        loadingOverlay.id = "loadingOverlay";
        loadingOverlay.style.position = "fixed";
        loadingOverlay.style.top = "0";
        loadingOverlay.style.left = "0";
        loadingOverlay.style.width = "100%";
        loadingOverlay.style.height = "100%";
        loadingOverlay.style.backgroundColor = "rgba(0, 0, 0, 0.5)";
        loadingOverlay.style.display = "flex";
        loadingOverlay.style.flexDirection = "column";
        loadingOverlay.style.alignItems = "center";
        loadingOverlay.style.justifyContent = "center";
        loadingOverlay.style.zIndex = "9999";
        
        // メッセージ要素
        const messageElement = document.createElement("div");
        messageElement.textContent = message;
        messageElement.style.color = "white";
        messageElement.style.marginBottom = "20px";
        loadingOverlay.appendChild(messageElement);
        
        // プログレスバー
        const progressContainer = document.createElement("div");
        progressContainer.style.width = "300px";
        progressContainer.style.height = "20px";
        progressContainer.style.backgroundColor = "#ddd";
        progressContainer.style.borderRadius = "10px";
        progressContainer.style.overflow = "hidden";
        
        const progressBar = document.createElement("div");
        progressBar.id = "progressBar";
        progressBar.style.width = "0%";
        progressBar.style.height = "100%";
        progressBar.style.backgroundColor = "#4CAF50";
        progressBar.style.transition = "width 0.3s";
        
        progressContainer.appendChild(progressBar);
        loadingOverlay.appendChild(progressContainer);
        
        document.body.appendChild(loadingOverlay);
    }

    // ローディング進捗更新
    function updateLoadingProgress(percent) {
        const progressBar = document.getElementById("progressBar");
        if (progressBar) {
            progressBar.style.width = percent + "%";
        }
    }

    // ローディング非表示
    function hideLoading() {
        const loadingOverlay = document.getElementById("loadingOverlay");
        if (loadingOverlay) {
            document.body.removeChild(loadingOverlay);
        }
    }

    // トースト通知表示
    function showToast(message, type = "info") {
        // すでに存在する場合は削除
        const existingToast = document.getElementById("toast");
        if (existingToast) {
            document.body.removeChild(existingToast);
        }
        
        // トースト要素を作成
        const toast = document.createElement("div");
        toast.id = "toast";
        toast.textContent = message;
        
        // スタイルを設定
        toast.style.position = "fixed";
        toast.style.bottom = "20px";
        toast.style.right = "20px";
        toast.style.padding = "12px 20px";
        toast.style.borderRadius = "4px";
        toast.style.fontSize = "14px";
        toast.style.zIndex = "10000";
        toast.style.boxShadow = "0 2px 5px rgba(0, 0, 0, 0.2)";
        
        // タイプによって色を変更
        switch (type) {
            case "success":
                toast.style.backgroundColor = "#4CAF50";
                toast.style.color = "white";
                break;
            case "warning":
                toast.style.backgroundColor = "#FFC107";
                toast.style.color = "black";
                break;
            case "error":
                toast.style.backgroundColor = "#F44336";
                toast.style.color = "white";
                break;
            default:
                toast.style.backgroundColor = "#2196F3";
                toast.style.color = "white";
        }
        
        // DOMに追加
        document.body.appendChild(toast);
        
        // 数秒後に自動的に消える
        setTimeout(function() {
            if (document.body.contains(toast)) {
                document.body.removeChild(toast);
            }
        }, 5000);
    }

    // 公開メソッド
    return {
        init: init,
        uploadFile: uploadFile
    };
})();

// DOMコンテンツロード時に初期化
document.addEventListener("DOMContentLoaded", fileUploader.init); 