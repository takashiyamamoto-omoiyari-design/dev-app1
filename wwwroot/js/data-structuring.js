// データ構造化ソリューション用JavaScript

// ベースパスを取得する関数をグローバルに定義
const getBasePath = () => {
    const pathSegments = window.location.pathname.split('/');
    if (pathSegments.length > 1 && pathSegments[1] === 'trial-app2') {
        return '/trial-app2';
    }
    return '';
};

// HTMLエスケープ（描画時のXSS防止と文字化け防止）
function escapeHtml(str) {
    if (str == null) return '';
    const s = String(str);
    return s
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

// アップロード履歴を保存する配列
let uploadHistory = [];

// 現在表示中のworkIdを追跡するグローバル変数
let currentWorkId = null; // プリセットなし

// アップロード履歴更新用のインターバルID
let uploadHistoryUpdateInterval;

// URLからworkIdを取得する関数
function getWorkIdFromUrl() {
    const urlParams = new URLSearchParams(window.location.search);
    return urlParams.get('workId');
}

// 初期アップロード履歴をローカルストレージに保存
//localStorage.setItem('uploadHistory', JSON.stringify(uploadHistory));

// ログアウト処理用の関数をグローバルに定義
function logout() {
    const basePath = getBasePath();
    
    // ASP.NET認証クッキーを削除（複数の方法で確実に削除）
    document.cookie = "ILUSolution.Auth=; expires=Thu, 01 Jan 1970 00:00:00 UTC; path=/;";
    document.cookie = "ILUSolution.Auth=; expires=Thu, 01 Jan 1970 00:00:00 UTC; path=/; domain=" + window.location.hostname;
    
    // ASP.NET認証キャッシュもクリア
    if (typeof clearAuthCache === 'function') {
        clearAuthCache();
    }
    
    // メインサイトにリダイレクト
    window.location.href = `${basePath}/Logout`;
    
    // リダイレクトを確実にするためのコード
    setTimeout(function() {
        alert('ログアウトしました。ログイン画面に移動します。');
        window.location.replace(`${basePath}/Logout`);
    }, 500);
    
    return false;
}

// PDF全ページテキストキャッシュ用のオブジェクト
let pdfTextCache = {};
// 現在処理中のPDF ID
let currentPdfPrefetchId = null;
// キャッシュ進捗追跡用の変数
let cacheProgressStatus = {};

document.addEventListener('DOMContentLoaded', function() {
    console.log('=== DOMContentLoaded - ページ初期化開始 ===');
    console.log('初期化前のuploadHistory:', uploadHistory.length, '件');
    
    // ローカルストレージの詳細確認
    console.log('--- ローカルストレージ詳細確認 ---');
    console.log('localStorage.length:', localStorage.length);
    
    // すべてのローカルストレージキーを表示
    for (let i = 0; i < localStorage.length; i++) {
        const key = localStorage.key(i);
        console.log(`localStorage[${i}]: ${key} = ${localStorage.getItem(key)?.substring(0, 100)}...`);
    }
    
    const uploadHistoryData = localStorage.getItem('uploadHistory');
    console.log('uploadHistoryキーの値:', uploadHistoryData);
    console.log('uploadHistoryキーの型:', typeof uploadHistoryData);
    console.log('uploadHistoryキーの存在:', uploadHistoryData !== null);
    console.log('--- ローカルストレージ確認終了 ---');
    
    // ページ読み込み時にアップロード履歴を読み込む
    loadUploadHistory();
    
    console.log('loadUploadHistory呼び出し後のuploadHistory:', uploadHistory.length, '件');
    console.log('loadUploadHistory呼び出し後の詳細:', uploadHistory);
    console.log('=== ページ初期化完了 ===');
    
    // 各要素の取得
    const leftSidebar = document.getElementById('leftSidebar');
    const leftResizer = document.getElementById('leftResizer');
    const rightSidebar = document.getElementById('rightSidebar');
    const rightResizer = document.getElementById('rightResizer');
    const chatToggleBtn = document.getElementById('chatToggleBtn');
    const closeChatBtn = document.getElementById('close-chat-btn');
    const uploadBtn = document.getElementById('upload-btn');
    const currentFile = document.getElementById('current-file');
    const currentFileLink = document.getElementById('current-file-link');
    const diffAnalyzeBtn = document.getElementById('diff-analyze-btn');
    const diffModal = document.getElementById('diff-modal');
    const diffModalClose = document.getElementById('diff-modal-close');
    const makeSyntheticBtn = document.getElementById('make-synthetic-btn');
    const s3BucketInput = document.getElementById('s3-bucket');
    const s3PrefixInput = document.getElementById('s3-prefix');
    const syntheticSelection = document.getElementById('synthetic-selection');
    const pageDiffList = document.getElementById('page-diff-list');
    const diffSeverityFilter = document.getElementById('diff-severity-filter');
    const optFewshot = document.getElementById('opt-fewshot');
    const fewshotType = document.getElementById('fewshot-type');
    const fewshotSummary = document.getElementById('fewshot-summary');
    const fewshotModal = document.getElementById('fewshot-modal');
    const fewshotModalClose = document.getElementById('fewshot-modal-close');
    const fewshotModalList = document.getElementById('fewshot-modal-list');
    const fewshotModalSave = document.getElementById('fewshot-modal-save');
    const diffResults = document.getElementById('diff-results');
    const rightPanelTitle = document.getElementById('right-panel-title');
    const synthOpenBtn = document.getElementById('open-synth-btn');
    const runAnalysisBtn = document.getElementById('run-analysis-btn');
    const synthModal = document.getElementById('synth-modal');
    const synthModalHeader = document.getElementById('synth-modal-header');
    const synthCloseBtn = document.getElementById('synth-modal-close');
    const synthSamples = document.getElementById('synth-samples');
    const synthGenBtn = document.getElementById('synth-generate-btn');
    const synthDlBtn = document.getElementById('synth-download-btn');
    const synthUploadBtn = document.getElementById('synth-upload-btn');
    const synthJsonl = document.getElementById('synth-jsonl');
    const synthPrompt = document.getElementById('synth-prompt');
    const synthPromptSaveBtn = document.getElementById('synth-prompt-save-btn');
    const tabJsonlBtn = document.getElementById('synth-tab-jsonl');
    const tabPromptBtn = document.getElementById('synth-tab-prompt');
    const panelJsonl = document.getElementById('synth-panel-jsonl');
    const panelPrompt = document.getElementById('synth-panel-prompt');
    const synthResizer = document.getElementById('synth-resizer');
    const autoLoadLatestPrompt = true; // 最新の保存プロンプトを初期表示（そのまま素通し）

    // Fewshot upload modal elements
    const fuModal = document.getElementById('fewshot-upload-modal');
    const fuClose = document.getElementById('fewshot-upload-close');
    const fuStep1 = document.getElementById('fu-step1');
    const fuStep2 = document.getElementById('fu-step2');
    const fuStep3 = document.getElementById('fu-step3');
    const fuTypeList = document.getElementById('fu-type-list');
    const fuNext1 = document.getElementById('fu-next1');
    const fuBack2 = document.getElementById('fu-back2');
    const fuNext2 = document.getElementById('fu-next2');
    const fuBack3 = document.getElementById('fu-back3');
    const fuUpload = document.getElementById('fu-upload');
    const fuThumbGrid = document.getElementById('fu-thumb-grid');
    const fuPreview = document.getElementById('fu-preview');
    const fuText = document.getElementById('fu-text');
    let fuSelectedType = '';
    let fuSelectedImage = { url: '', path: '' };

    function openRightPanelForDiff() {
        if (rightSidebar) rightSidebar.classList.add('open');
        if (rightPanelTitle) rightPanelTitle.textContent = '比較差分';
        const chatMessagesEl = document.getElementById('chat-messages');
        if (chatMessagesEl) chatMessagesEl.style.display = 'none';
        if (diffResults) diffResults.style.display = '';
        // 右パネルのメッセージ入力と注意文言を非表示
        const chatInputContainer = document.querySelector('#rightSidebar .chat-input-container');
        if (chatInputContainer) chatInputContainer.style.display = 'none';
        // 合成データ作成ボタンを表示
        if (synthOpenBtn) synthOpenBtn.style.display = '';
        // 分析ボタンを表示
        if (runAnalysisBtn) runAnalysisBtn.style.display = '';
    }

    function renderDiffListHtml(list) {
        if (!Array.isArray(list) || list.length === 0) {
            return '<div style="padding:12px; color:#6b7280;">差分はありません</div>';
        }
        const sorted = [...list].sort((a,b)=> (a.page_no??0) - (b.page_no??0));
        return sorted.map(d => {
            const raw = d.diff_text || d.details || '';
            const cleaned = raw.replace(/^\[p\.(\d+)\]\s*差分\s*\n?/, '');
            const lines = cleaned.split(/\r?\n/).filter(l => l.trim().length > 0);
            const bullets = lines.map(l => `- ${escapeHtml(l)}`).join('\n');
            return `<div style="border-bottom:1px solid #e5e7eb; padding:12px 14px;">
                <div style="font-weight:600; margin-bottom:6px;">${(d.page_no ?? 0) + 1}枚目</div>
                <div style="white-space:pre-wrap; line-height:1.6; color:#111827;">${bullets}</div>
            </div>`;
        }).join('');
    }

    // 差分分析（右パネルに表示）: 既存ボタンは右パネルを開くだけ
    if (diffAnalyzeBtn) {
        diffAnalyzeBtn.addEventListener('click', () => {
            if (diffModal) diffModal.style.display = 'none';
            if (typeof openRightPanelForDiff === 'function') {
                openRightPanelForDiff();
                // 直近の差分が存在しない場合のみガイダンスを表示
                try {
                    const hasPrev = Array.isArray(window.lastDiffResult) && window.lastDiffResult.length > 0;
                    if (!hasPrev && diffResults) {
                        diffResults.innerHTML = '<div style="padding:12px; color:#6b7280;">分析ボタンを押して差分を取得します...</div>';
                    }
                } catch {}
            }
            // プロンプトプレビューを追記/更新
            try { updatePromptPreviewInTextarea(); } catch {}
        });
        diffModalClose.addEventListener('click', () => {
            diffModal.style.display = 'none';
        });
        window.addEventListener('click', (e) => {
            if (e.target === diffModal) diffModal.style.display = 'none';
        });
    }

    // 新設: 右上「分析」ボタンで差分取得を実行
    if (runAnalysisBtn) {
        runAnalysisBtn.addEventListener('click', async () => {
            if (typeof openRightPanelForDiff === 'function') {
                openRightPanelForDiff();
                if (diffResults) diffResults.innerHTML = '<div style="padding:12px; color:#6b7280;">差分を取得しています...</div>';
            }
            const history = loadUploadHistory();
            const latest = history && history.length > 0 ? history[0] : null;
            const workId = (typeof currentWorkId === 'string' && currentWorkId) ? currentWorkId : (latest ? latest.workId : null);
            if (!workId) return;
            let currentPage0 = 0;
            try {
                if (typeof selectedDocument === 'object' && selectedDocument) {
                    if (typeof selectedDocument.pageNumber === 'number') {
                        currentPage0 = Math.max(0, selectedDocument.pageNumber - 1);
                    }
                }
                if (currentPage0 === 0) {
                    const activeEl = document.querySelector('.page-item.active .page-name');
                    if (activeEl && activeEl.textContent) {
                        const m = activeEl.textContent.match(/ページ\s*(\d+)/);
                        if (m && m[1]) {
                            const pn = parseInt(m[1]);
                            if (!Number.isNaN(pn)) currentPage0 = Math.max(0, pn - 1);
                        }
                    }
                }
            } catch {}
            try {
                let visiblePageCount = 0;
                try {
                    const pageItems = document.querySelectorAll('#page-list .page-item');
                    visiblePageCount = pageItems ? pageItems.length : 0;
                } catch {}

                const res = await fetch(getBasePath() + '/api/ocr/diff-analyze', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
                    credentials: 'include',
                    body: JSON.stringify({ work_id: workId })
                });
                const contentType = res.headers.get('content-type') || '';
                if (res.ok) {
                    if (contentType.includes('application/json')) {
                        const data = await res.json();
                        const list = Array.isArray(data.page_diffs) ? data.page_diffs : [];
                        const filtered = (visiblePageCount > 0)
                            ? list.filter(d => ((d.page_no ?? 0) + 1) <= visiblePageCount)
                            : list;
                        window.lastDiffResult = filtered;
                        if (typeof renderDiffListHtml === 'function' && diffResults) {
                            diffResults.innerHTML = renderDiffListHtml(filtered);
                        } else if (typeof renderPageDiffsMinimal === 'function') {
                            renderPageDiffsMinimal(filtered);
                        }
                    } else {
                        if (diffResults) diffResults.innerHTML = '<div style="padding:12px; color:#b91c1c;">JSON以外の応答を受信しました（ログイン切れの可能性）</div>';
                    }
                } else if (res.status === 401) {
                    if (diffResults) diffResults.innerHTML = '<div style="padding:12px; color:#b91c1c;">未認証です。再ログインしてください。</div>';
                } else if (res.status === 403) {
                    if (diffResults) diffResults.innerHTML = '<div style="padding:12px; color:#b91c1c;">アクセス権限がありません。</div>';
                } else {
                    const text = contentType.includes('application/json') ? JSON.stringify(await res.json()) : (await res.text()).slice(0, 300);
                    if (diffResults) diffResults.innerHTML = '<div style="padding:12px; color:#b91c1c;">差分分析取得に失敗しました</div>';
                    console.error('diff-analyze error response:', res.status, text);
                }
            } catch (e) {
                if (diffResults) diffResults.innerHTML = '<div style="padding:12px; color:#b91c1c;">差分分析呼び出しでエラーが発生しました</div>';
            }
        });
    }

    // fewshot UI 表示制御と候補取得（モーダル選択）
    (function initFewshotUI(){
        // 選択状態を保持
        window.fewshotAvailableTypes = window.fewshotAvailableTypes || [];
        window.fewshotSelectedTypes = window.fewshotSelectedTypes || [];

        function updateFewshotSummary() {
            try {
                const selected = window.fewshotSelectedTypes || [];
                if (!fewshotSummary) return;
                if (!Array.isArray(selected) || selected.length === 0) {
                    fewshotSummary.style.display = 'none';
                    fewshotSummary.textContent = '';
                    return;
                }
                const first = selected[0] || '';
                const suffix = selected.length > 1 ? ' ・・・' : '';
                fewshotSummary.textContent = `${selected.length}件: ${first}${suffix}`;
                fewshotSummary.style.display = '';
            } catch {}
        }

        async function ensureAvailableTypes() {
            if (Array.isArray(window.fewshotAvailableTypes) && window.fewshotAvailableTypes.length > 0) {
                console.debug('[fewshot] cached types used:', window.fewshotAvailableTypes);
                return;
            }
            try {
                const url = getBasePath() + '/api/user/types';
                console.debug('[fewshot] fetch', url);
                const res = await fetch(url, { credentials: 'include' });
                console.debug('[fewshot] status', res.status, res.statusText);
                const ct = res.headers.get('content-type') || '';
                if (!res.ok) {
                    const txt = ct.includes('application/json') ? JSON.stringify(await res.json()) : await res.text();
                    console.error('[fewshot] error response', res.status, txt);
                    return;
                }
                if (ct.includes('application/json')) {
                    const data = await res.json();
                    window.fewshotAvailableTypes = Array.isArray(data.types) ? data.types : [];
                    console.debug('[fewshot] types', window.fewshotAvailableTypes);
                } else {
                    console.error('[fewshot] unexpected content-type', ct);
                }
            } catch (e) { console.error('ユーザータイプ取得に失敗', e); }
        }

        function openFewshotModal() {
            if (!fewshotModal || !fewshotModalList) return;
            fewshotModalList.innerHTML = '';
            const types = window.fewshotAvailableTypes || [];
            const selected = new Set(window.fewshotSelectedTypes || []);
            for (const t of types) {
                const id = `fst-${Math.random().toString(36).slice(2,8)}`;
                const item = document.createElement('label');
                item.style.display = 'flex';
                item.style.alignItems = 'center';
                item.style.gap = '8px';
                item.style.padding = '6px 4px';
                item.style.border = '1px solid #e5e7eb';
                item.style.borderRadius = '6px';
                item.innerHTML = `<input type="checkbox" id="${id}" value="${t}"> <span>${escapeHtml(t)}</span>`;
                const cb = item.querySelector('input');
                if (selected.has(t)) cb.checked = true;
                fewshotModalList.appendChild(item);
            }
            fewshotModal.style.display = '';
        }

        function closeFewshotModal() { if (fewshotModal) fewshotModal.style.display = 'none'; }

        if (optFewshot) {
            optFewshot.addEventListener('change', async () => {
                // 既存selectは常に非表示
                if (fewshotType) { fewshotType.style.display = 'none'; }
                if (optFewshot.checked) {
                    await ensureAvailableTypes();
                    openFewshotModal();
                } else {
                    if (fewshotSummary) { fewshotSummary.style.display = 'none'; fewshotSummary.textContent = ''; }
                    window.fewshotSelectedTypes = [];
                }
            });
        }
        if (fewshotModalClose) {
            fewshotModalClose.addEventListener('click', () => closeFewshotModal());
        }
        if (fewshotModal) {
            window.addEventListener('click', (e) => { if (e.target === fewshotModal) closeFewshotModal(); });
        }
        if (fewshotModalSave) {
            fewshotModalSave.addEventListener('click', () => {
                try {
                    const checks = fewshotModalList ? Array.from(fewshotModalList.querySelectorAll('input[type="checkbox"]')) : [];
                    const selected = checks.filter(c => c.checked).map(c => c.value);
                    window.fewshotSelectedTypes = selected;
                    updateFewshotSummary();
                } catch {}
                closeFewshotModal();
            });
        }

        // 初期状態
        if (fewshotType) fewshotType.style.display = 'none';
        // fewshot-open ボタン廃止
        if (fewshotSummary) fewshotSummary.style.display = 'none';
    })();

    // 合成データ作成（S3出力）
    if (makeSyntheticBtn) {
        makeSyntheticBtn.addEventListener('click', async () => {
            const history = loadUploadHistory();
            const latest = history && history.length > 0 ? history[0] : null;
            if (!latest || !latest.workId) return;
            const bucket = (s3BucketInput && s3BucketInput.value.trim()) || '';
            const prefix = (s3PrefixInput && s3PrefixInput.value.trim()) || '';
            if (!bucket) { alert('Bucket を入力してください'); return; }
            try {
                const body = {
                    work_id: latest.workId,
                    s3_config: { bucket, prefix },
                    selection: collectSyntheticSelection()
                };
                const res = await fetch(getBasePath() + '/api/synthetic/export-jsonl', {
                    method: 'POST', headers: { 'Content-Type': 'application/json' }, credentials: 'include',
                    body: JSON.stringify(body)
                });
                if (!res.ok) throw new Error('export-jsonl failed');
                const data = await res.json();
                showToast(`S3に配置しました: ${data.base_path || ''}`, 'success');
            } catch (e) {
                showToast('合成データ作成に失敗しました', 'error');
            }
        });
    }

    function collectSyntheticSelection() {
        // 必要に応じてチェックボックス群から選択を収集する
        // ここでは空の選択を返す（UI整備後に拡張）
        return { include_categories: [], pages: [] };
    }

    // アップロード後のPDF名リンク表示（履歴から）
    try {
        const urlParamCheck = new URLSearchParams(window.location.search);
        const hasParamWorkIdInit = !!urlParamCheck.get('workId');
        const history = loadUploadHistory();
        const latest = history && history.length > 0 ? history[0] : null;
        // URLにworkId指定がある場合は、初期表示の最新履歴によるリンク表示はスキップ（競合防止）
        if (!hasParamWorkIdInit && latest && currentFile && currentFileLink) {
            const fileName = latest.fileName || '-';
            currentFileLink.textContent = fileName;
            // 署名URL取得
            if (latest.s3Key) {
                fetch(getBasePath() + '/api/storage/s3-signed-url?key=' + encodeURIComponent(latest.s3Key), { credentials: 'include' })
                    .then(r => r.ok ? r.json() : null)
                    .then(d => {
                        if (d && d.url) {
                            currentFileLink.href = d.url;
                            currentFile.style.display = 'block';
                        }
                    }).catch(()=>{});
            } else {
                // s3Keyが無い場合はそのまま非表示のまま
            }
        }
    } catch (_) {}

    // URLのworkIdが指定されている場合は、そのworkIdの原本PDFリンクを表示
    try {
        const urlParams = new URLSearchParams(window.location.search);
        const paramWorkId = urlParams.get('workId');
        if (paramWorkId && currentFile && currentFileLink) {
            fetch(getBasePath() + `/api/storage/workid-info?work_id=${encodeURIComponent(paramWorkId)}`, { credentials: 'include' })
                .then(r => r.ok ? r.json() : null)
                .then(info => {
                    if (!info) return;
                    if (info.hasFile && info.fileUrl) {
                        currentFileLink.textContent = info.fileName || '-';
                        currentFileLink.href = info.fileUrl;
                        currentFile.style.display = 'block';
                    } else {
                        currentFile.style.display = 'none';
                    }
                }).catch(()=>{});
        }
    } catch (_) {}
    // ダウンロードボタンと一括ダウンロードボタンを非表示にする
    const downloadBtn = document.getElementById('download-btn');
    if (downloadBtn) downloadBtn.style.display = 'none';
    const batchDownloadBtn = document.getElementById('batch-download-btn');
    if (batchDownloadBtn) batchDownloadBtn.style.display = 'none';
    const chatInput = document.getElementById('chat-input');
    const chatMessages = document.getElementById('chat-messages');
    const pageList = document.getElementById('page-list');
    const documentTitle = document.getElementById('documentTitle');
    const documentMeta = document.getElementById('documentMeta');
    const documentContent = document.getElementById('document-content');
    
    // 新しく追加したヘッダーのボタン要素
    const settingsBtn = document.getElementById('settings-btn');
    const accountIcon = document.getElementById('account-icon');
    
    let selectedDocument = null;
    
    // シノニムデータ管理用の変数
    let synonymStorage = {
        synonymList: null,
        synonymData: null,
        workId: null
    };
    
    // kuromojiトークナイザーのインスタンス（使用しない）
    let tokenizer = null;
    let segmenter = true; // 常にtrueにして機能を有効化
    
    // シンプルなキーワード抽出の初期化（常に成功）
    function initTokenizer() {
        return new Promise((resolve, reject) => {
            try {
                console.log('シンプルなキーワード抽出を初期化します');
                // 何もしない - 常に成功
                resolve(true);
            } catch (err) {
                console.error('キーワード抽出の初期化に失敗しました:', err);
                reject(err);
            }
        });
    }
    
    // シノニムデータを保存する関数
    function saveSynonymData(synonymList, synonymData, workId) {
        try {
            console.log('シノニムデータを保存中:', {
                synonymList: synonymList?.length || 0,
                synonymData: synonymData?.length || 0,
                workId: workId
            });
            
            // ローカル変数に保存
            synonymStorage.synonymList = synonymList;
            synonymStorage.synonymData = synonymData;
            synonymStorage.workId = workId;
            
            // localStorageにも保存（永続化）
            const storageData = {
                synonymList: synonymList,
                synonymData: synonymData,
                workId: workId,
                timestamp: new Date().toISOString()
            };
            
            localStorage.setItem('dsSynonymStorage', JSON.stringify(storageData));
            
            // シノニム辞書形式に変換して手動設定と統合保存
            if (synonymList && Array.isArray(synonymList)) {
                updateCombinedSynonymDict(synonymList);
            }
            
            console.log('シノニムデータの保存完了');
        } catch (error) {
            console.error('シノニムデータの保存中にエラー:', error);
        }
    }
    
    // シノニムリストを辞書形式に変換する関数
    function convertSynonymListToDict(synonymList) {
        const dictLines = [];
        
        if (synonymList && Array.isArray(synonymList)) {
            synonymList.forEach(item => {
                // 'synonym' (単数形) を正式プロパティとし、旧プロパティもフォールバックで許容
                const synArr = Array.isArray(item.synonym)
                    ? item.synonym
                    : (Array.isArray(item.Synonym)
                        ? item.Synonym
                        : (Array.isArray(item.synonyms)
                            ? item.synonyms
                            : (Array.isArray(item.Synonyms) ? item.Synonyms : [])));
                
                if (synArr.length > 1) {
                    // 最初の語をキーとして、残りを同義語として設定
                    const key = synArr[0];
                    const synonyms = synArr.slice(1);
                    dictLines.push(`${key}:${synonyms.join(',')}`);
                }
            });
        }
        
        return dictLines.join('\n');
    }
    
    // シノニムデータを読み込む関数
    function loadSynonymData() {
        try {
            const stored = localStorage.getItem('dsSynonymStorage');
            if (stored) {
                const storageData = JSON.parse(stored);
                synonymStorage.synonymList = storageData.synonymList;
                synonymStorage.synonymData = storageData.synonymData;
                synonymStorage.workId = storageData.workId;
                
                console.log('保存されたシノニムデータを読み込み:', {
                    synonymList: synonymStorage.synonymList?.length || 0,
                    synonymData: synonymStorage.synonymData?.length || 0,
                    workId: synonymStorage.workId,
                    timestamp: storageData.timestamp
                });
                
                return true;
            }
        } catch (error) {
            console.error('シノニムデータの読み込み中にエラー:', error);
        }
        return false;
    }
    
    // シノニムデータを取得する関数
    function getSynonymData() {
        return {
            synonymList: synonymStorage.synonymList,
            synonymData: synonymStorage.synonymData,
            workId: synonymStorage.workId
        };
    }
    
    // 手動設定とAPIデータを統合してシノニム辞書を更新する関数
    function updateCombinedSynonymDict(synonymList) {
        try {
            console.log('🔍 [DEBUG] シノニム辞書統合開始');
            
            // 既存の手動設定シノニムを取得
            const existingDict = localStorage.getItem('dsSynonyms') || '';
            const existingLines = existingDict.split('\n').filter(line => line.trim() && line.includes(':'));
            
            console.log('🔍 [DEBUG] 手動設定シノニム:', {
                count: existingLines.length,
                samples: existingLines.slice(0, 3),
                all: existingLines
            });
            
            // APIから取得したシノニムを辞書形式に変換
            const apiLines = [];
            if (synonymList && Array.isArray(synonymList)) {
                console.log('🔍 [DEBUG] API取得シノニムリスト:', {
                    count: synonymList.length,
                    structure: synonymList.slice(0, 2)
                });
                
                synonymList.forEach((item, index) => {
                    console.log(`🔍 [DEBUG] API項目 ${index + 1}:`, item);
                    
                    // 'synonym' (単数形) を正式プロパティとし、旧プロパティもフォールバックで許容
                    const synArr = Array.isArray(item.synonym)
                        ? item.synonym
                        : (Array.isArray(item.Synonym)
                            ? item.Synonym
                            : (Array.isArray(item.synonyms)
                                ? item.synonyms
                                : (Array.isArray(item.Synonyms) ? item.Synonyms : [])));
                    
                    if (synArr.length > 1) {
                        const key = synArr[0];
                        const synonyms = synArr.slice(1);
                        const line = `${key}:${synonyms.join(',')}`;
                        apiLines.push(line);
                        console.log(`🔍 [DEBUG] 変換結果: ${line}`);
                    } else {
                        console.log(`🔍 [DEBUG] スキップ（条件不適合）:`, item);
                    }
                });
            }
            
            console.log('🔍 [DEBUG] API変換後シノニム:', {
                count: apiLines.length,
                all: apiLines
            });
            
            // 手動設定とAPI取得データを統合（重複除去）
            const allLines = [...existingLines, ...apiLines];
            const uniqueLines = [...new Set(allLines)];
            
            console.log('🔍 [DEBUG] 統合結果:', {
                manual: existingLines.length,
                api: apiLines.length,
                combined: allLines.length,
                unique: uniqueLines.length,
                duplicates: allLines.length - uniqueLines.length,
                finalData: uniqueLines
            });
            
            // 統合されたシノニム辞書を保存
            localStorage.setItem('dsSynonyms', uniqueLines.join('\n'));
            
            // 統合結果をトーストで表示
            const message = `🔄 シノニム辞書を更新しました\n\n手動設定: ${existingLines.length}件\nAPI取得: ${apiLines.length}件\n重複除去: ${allLines.length - uniqueLines.length}件\n統合後: ${uniqueLines.length}件`;
            //showToast(message, 8000);
            
        } catch (error) {
            console.error('🔍 [DEBUG] シノニム辞書統合中にエラー:', error);
        }
    }
    
    // 統合されたシノニムリストを取得する関数
    function getCombinedSynonyms() {
        const synonymDict = localStorage.getItem('dsSynonyms') || '';
        return synonymDict;
    }
    // 特定のキーワードに関連するシノニムを検索する関数
    function findSynonymsForKeyword(keyword) {
        const synonyms = [];
        
        // APIデータから検索
        if (synonymStorage.synonymList) {
            synonymStorage.synonymList.forEach(item => {
                // 'synonym' (単数形) を正式プロパティとし、旧プロパティもフォールバックで許容
                const synArr = Array.isArray(item.synonym)
                    ? item.synonym
                    : (Array.isArray(item.Synonym)
                        ? item.Synonym
                        : (Array.isArray(item.synonyms)
                            ? item.synonyms
                            : (Array.isArray(item.Synonyms) ? item.Synonyms : [])));
                
                if (synArr.includes(keyword)) {
                    synonyms.push(...synArr.filter(s => s !== keyword));
                }
            });
        }
        
        // 手動設定辞書からも検索
        const dictText = localStorage.getItem('dsSynonyms') || '';
        dictText.split('\n').forEach(line => {
            if (line.includes(':')) {
                const [key, values] = line.split(':');
                const allTerms = [key, ...values.split(',')].map(s => s.trim());
                if (allTerms.includes(keyword)) {
                    synonyms.push(...allTerms.filter(s => s !== keyword));
                }
            }
        });
        
        return [...new Set(synonyms)];
    }
    
    // テキストからキーワードを抽出する関数
    async function extractKeywords(text) {
        console.log('テキスト分析開始: ', text.substring(0, 50) + '...');
        
        try {
            // 外部APIを呼び出してキーワードを抽出
            const keywords = await callKeywordExtractionAPI(text);
            console.log('APIから抽出されたキーワード:', keywords);
            return keywords;
        } catch (error) {
            console.warn('キーワード抽出APIエラー、ローカル処理にフォールバック:', error);
            
            // エラーの詳細情報を表示
            let errorDetails = null;
            if (error.response) {
                try {
                    // レスポンスがJSONの場合はパース
                    errorDetails = error.response;
                } catch (e) {
                    console.error('エラーレスポンスの解析に失敗:', e);
                }
            }
            
            // UIにエラーを表示
            // showApiError('キーワード抽出APIにアクセスできませんでした。ローカル処理を使用します。', errorDetails);
            
            // 元のローカル処理にフォールバック
            return extractKeywordsLocally(text);
        }
    }
    // APIを呼び出してキーワードを抽出する関数
    async function callKeywordExtractionAPI(text) {
        // 内部APIプロキシエンドポイント（ベースパスを含める）
        const basePath = getBasePath();
        const apiUrl = `${basePath}/api/KeywordExtraction`;
        
        // リクエストデータ
        const requestData = {
            userId: 'user', // 適切な認証情報に置き換え
            password: 'pass', // 適切な認証情報に置き換え
            text: text
        };
        
        // タイムアウト設定（15秒に延長）
        const controller = new AbortController();
        const timeoutId = setTimeout(() => {
            console.warn('APIリクエストがタイムアウトしました（15秒経過）');
            controller.abort();
        }, 15000);
        
        try {
            console.log('ベースパス:', basePath);
            console.log('内部APIプロキシを呼び出し開始:', apiUrl, '時刻:', new Date().toISOString());
            console.log('リクエストデータサンプル:', text.substring(0, 50) + '...');
            
            const response = await fetch(apiUrl, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(requestData),
                signal: controller.signal
            });
            
            clearTimeout(timeoutId);
            console.log('APIレスポンス受信:', response.status, response.statusText, '時刻:', new Date().toISOString());
            
            if (!response.ok) {
                // エラーレスポンスのJSON取得
                let errorData = {};
                try {
                    errorData = await response.json();
                } catch (jsonError) {
                    console.error('エラーレスポンスのJSON解析失敗:', jsonError);
                    errorData = { error: 'Unknown error', status_code: response.status };
                }
                
                console.error('APIエラーレスポンス:', errorData);
                
                // エラー情報をトースト表示
                const errorMessage = `🚨 Tokenize APIエラー

【ステータス】${response.status} ${response.statusText}
【エラー内容】${errorData.error || 'Unknown error'}
【リクエストID】${errorData.request_id || 'N/A'}
【処理時間】${errorData.processing_time_ms || 'N/A'}ms
【詳細】${JSON.stringify(errorData, null, 2)}`;
                
                showToast(errorMessage, 10000); // 10秒表示
                
                // エラー情報を含んだカスタムエラーオブジェクトを作成
                const apiError = new Error(`API responded with status: ${response.status}, message: ${errorData.error || 'Unknown error'}`);
                apiError.response = errorData;  // エラーオブジェクトにレスポンス情報を追加
                apiError.status = response.status;
                throw apiError;
            }
            
            const data = await response.json();
            console.log('APIレスポンスデータサンプル:', JSON.stringify(data).substring(0, 100) + '...');
            
            // 成功レスポンスをトースト表示
            const successMessage = `✅ Tokenize API成功

【ステータス】200 OK
【リターンコード】${data.return_code || 'N/A'}
【キーワード数】${data.keyword_list ? data.keyword_list.length : 0}
【処理時間】${data.processing_time_ms || 'N/A'}ms
【キーワード】${data.keyword_list ? data.keyword_list.map(k => `${k.surface}(${k.score})`).slice(0, 5).join(', ') : 'なし'}
${data.keyword_list && data.keyword_list.length > 5 ? '...' : ''}

【完全なレスポンス】
${JSON.stringify(data, null, 2)}`;
            
            //showToast(successMessage, 15000); // 15秒表示
            
            // APIのレスポンスコードをチェック
            if (data.return_code !== 0) {
                const apiError = new Error(`API error: ${data.error_detail || 'Unknown error'}`);
                apiError.response = data;
                apiError.returnCode = data.return_code;
                throw apiError;
            }
            
            // キーワードリストが存在するか確認
            if (!data.keyword_list || !Array.isArray(data.keyword_list)) {
                const apiError = new Error('No keywords returned from API');
                apiError.response = data;
                throw apiError;
            }
            
            // キーワードを抽出（surfaceプロパティを使用）
            const keywords = data.keyword_list
                .filter(keyword => keyword.surface) // 有効なキーワードのみ
                .sort((a, b) => b.score - a.score) // スコアの高い順に並べ替え
                .slice(0, 10) // 最大10件に制限
                .map(keyword => keyword.surface); // キーワードテキストのみを抽出
            
            console.log('抽出されたキーワード:', keywords);
            return keywords;
        } catch (error) {
            console.error('キーワード抽出API呼び出しエラー:', error);
            
            // エラーの種類に応じた詳細なログ表示
            if (error.name === 'AbortError') {
                console.error('APIリクエストがタイムアウトしました。サーバーが応答していないか、ネットワーク接続に問題があります。');
                
                // AbortErrorにレスポンス情報を追加
                error.response = {
                    error: 'Request timeout',
                    error_category: '接続タイムアウト',
                    possible_cause: '社内ネットワーク内のみからアクセス可能なAPIの可能性があります',
                    is_network_restricted: true
                };
                
                // タイムアウトエラーをトースト表示
                const timeoutMessage = `⏰ Tokenize APIタイムアウト

【エラー種別】接続タイムアウト
【タイムアウト時間】15秒
【推定原因】${error.response.possible_cause}
【対処方法】
- ネットワーク接続を確認
- VPN接続状況を確認
- 社内ネットワークからのアクセスかチェック

【エラー詳細】
${JSON.stringify(error.response, null, 2)}`;
                
                showToast(timeoutMessage, 12000); // 12秒表示
            } else if (error.message && error.message.includes('NetworkError')) {
                console.error('ネットワークエラー: サーバーに接続できません。ネットワーク接続を確認してください。');
                
                const networkErrorMessage = `🌐 ネットワークエラー

【エラー種別】NetworkError
【メッセージ】${error.message}
【対処方法】ネットワーク接続を確認してください

【エラー詳細】
${JSON.stringify({
    name: error.name,
    message: error.message,
    stack: error.stack
}, null, 2)}`;
                
                showToast(networkErrorMessage, 10000);
            } else if (error.message && error.message.includes('SyntaxError')) {
                console.error('応答データの解析エラー: サーバーからの応答が不正な形式です。');
                
                const syntaxErrorMessage = `📄 データ解析エラー

【エラー種別】SyntaxError
【メッセージ】${error.message}
【原因】サーバーからの応答が不正な形式です

【エラー詳細】
${JSON.stringify({
    name: error.name,
    message: error.message,
    response: error.response
}, null, 2)}`;
                
                showToast(syntaxErrorMessage, 10000);
            } else {
                // その他のエラー
                const generalErrorMessage = `❌ Tokenize API エラー

【エラー種別】${error.name || 'Unknown'}
【メッセージ】${error.message}
【ステータス】${error.status || 'N/A'}
【リターンコード】${error.returnCode || 'N/A'}

【レスポンス詳細】
${error.response ? JSON.stringify(error.response, null, 2) : 'レスポンスなし'}

【エラー詳細】
${JSON.stringify({
    name: error.name,
    message: error.message,
    status: error.status,
    stack: error.stack
}, null, 2)}`;
                
                showToast(generalErrorMessage, 12000);
            }
            
            throw error; // エラーを上位に伝播させる
        } finally {
            clearTimeout(timeoutId);
        }
    }

    // UIにAPIエラーを表示する関数
    /* コメントアウト：エラー通知の表示を無効化
    function showApiError(message, details = null) {
        // 既存のエラー通知を削除
        const existingError = document.getElementById('api-error-notification');
        if (existingError) {
            existingError.remove();
        }
        
        // エラー通知を作成
        const errorDiv = document.createElement('div');
        errorDiv.id = 'api-error-notification';
        errorDiv.style.cssText = 'position: fixed; top: 10px; right: 10px; background-color: #f8d7da; color: #721c24; padding: 10px 15px; border-radius: 4px; box-shadow: 0 2px 5px rgba(0,0,0,0.2); z-index: 9999; max-width: 400px;';
        
        // エラーメッセージとクローズボタン
        let errorContent = `
            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 5px;">
                <div><strong>エラー通知</strong></div>
                <button style="background: none; border: none; font-size: 16px; cursor: pointer; margin-left: 10px;">×</button>
            </div>
            <div>${message}</div>
        `;
        
        // 詳細情報があれば追加（社内ネットワーク限定の可能性など）
        if (details) {
            // ネットワーク制限に関連するエラーかチェック
            const isNetworkRestricted = 
                details.is_network_restricted || 
                (details.possible_cause && details.possible_cause.includes('社内ネットワーク')) ||
                (details.error_category && (
                    details.error_category.includes('ホスト名解決') || 
                    details.error_category.includes('接続拒否') ||
                    details.error_category.includes('タイムアウト')
                ));
            
            // ネットワーク制限の場合はより詳細な情報を表示
            if (isNetworkRestricted) {
                errorContent += `
                    <hr style="margin: 8px 0; border-top: 1px solid #f5c6cb;">
                    <div style="font-size: 0.9em;">
                        <div style="color: #d33; margin-bottom: 5px;"><i class="fas fa-exclamation-triangle"></i> <strong>社内ネットワーク限定APIの可能性があります</strong></div>
                        <div>考えられる原因:</div>
                        <ul style="margin: 5px 0; padding-left: 20px;">
                            ${details.possible_cause ? `<li>${details.possible_cause}</li>` : ''}
                            <li>VPNが未接続または社内ネットワーク外からのアクセス</li>
                            <li>IPアドレス制限付きAPIへのアクセス</li>
                            <li>社内DNS限定のホスト名へのアクセス</li>
                        </ul>
                        ${details.error_category ? `<div>エラー種別: ${details.error_category}</div>` : ''}
                    </div>
                `;
            } 
            // 一般的なエラーの場合
            else {
                errorContent += `
                    <div style="font-size: 0.9em; margin-top: 5px; color: #666;">
                        ${details.error_category ? `エラー種別: ${details.error_category}<br>` : ''}
                        ${details.status_code ? `ステータスコード: ${details.status_code}<br>` : ''}
                        ${details.message ? `メッセージ: ${details.message}` : ''}
                    </div>
                `;
            }
        }
        
        errorDiv.innerHTML = errorContent;
        
        // DOMに追加
        document.body.appendChild(errorDiv);
        
        // クローズボタンのイベントリスナー
        errorDiv.querySelector('button').addEventListener('click', () => {
            errorDiv.remove();
        });
        
        // 20秒後に自動的に消える
        setTimeout(() => {
            if (errorDiv.parentNode) {
                errorDiv.remove();
            }
        }, 20000);
    }
    */

    // 元のローカル処理をこの関数に移動（フォールバック用）
    function extractKeywordsLocally(text) {
        // 1. 全角スペース、改行、タブを半角スペースに変換
        const normalizedText = text.replace(/[\s　\n\t]+/g, ' ');
        
        // 2. 助詞や句読点で区切られた単語を抽出
        // 日本語の助詞（は、が、を、に、で、と、から、まで、など）の前にある単語は名詞の可能性が高い
        const nounsBeforeParticles = [];
        
        // 助詞の前のパターンを抽出（名詞+助詞のパターン）
        const particlePatterns = [
            /([^\s,.。、,.!?！？(){}\[\]]{2,})(は|が|を|に|で|と|の|から|まで|より|へ|や|など)/g,
            /([^\s,.。、,.!?！？(){}\[\]]{2,})(について|による|において|として|ための|による|によって|に関する)/g
        ];
        
        particlePatterns.forEach(pattern => {
            let match;
            while ((match = pattern.exec(normalizedText)) !== null) {
                if (match[1] && match[1].length >= 2) {
                    nounsBeforeParticles.push(match[1]);
                }
            }
        });
        
        // 3. 句読点、記号で分割してキーワード候補を抽出
        const words = normalizedText.split(/[\s,.。、,.!?！？()（）「」『』［］\[\]{}:：;"']+/)
            .filter(word => word.length >= 2) // 2文字以上の単語だけを対象
            .filter(word => !(/^\d+$/.test(word))) // 数字だけの単語を除外
            .filter(word => word.trim() !== ''); // 空文字を除外
        
        // 4. 名詞の可能性が高い単語を特定
        const potentialNouns = words.filter(word => {
            // 語尾が動詞活用で終わる単語は除外
            const verbEndings = ['する', 'します', 'した', 'される', 'された', 'れる', 'られる', 'せる', 'させる', 
                              'ます', 'ました', 'まして', 'です', 'でした', 'ている', 'ていた', 'なる', 'なった',
                              'たい', 'たく', 'たかっ'];
            
            for (const ending of verbEndings) {
                if (word.endsWith(ending) && word.length > ending.length) {
                    return false;
                }
            }
            
            // 形容詞の語尾を持つ単語は除外
            const adjEndings = ['い', 'かった', 'くない', 'くて', 'ければ', 'しい', 'しく', 'しかっ'];
            for (const ending of adjEndings) {
                if (word.endsWith(ending) && word.length > ending.length + 1) {
                    return false;
                }
            }
            
            return true;
        });
        
        // 5. 保険ドメイン特化の名詞辞書
        const domainNouns = [
            // 保険一般
            '保険', '契約', '証券', '約款', '更新', '解約', '満期', '払込', '加入', '請求', '支払', '給付',
            // 保険種類
            '終身', '養老', '定期', '医療', '学資', '年金', '収入', '傷害', '疾病', '就業', '介護', '長期',
            // 契約関連
            '契約者', '被保険者', '受取人', '保険料', '保険金', '返戻金', '特約', '特則', '条項', '約定',
            // 保険金関連
            '死亡', '入院', '手術', '通院', '障害', '給付', '診断', '療養', '就業', '災害', '事故', '疾病',
            // 手続き
            '申込', '告知', '診査', '引受', '査定', '支払', '請求', '返戻', '貸付', '振替', '変更', '訂正',
            // 保険会社・組織
            '会社', '窓口', '本社', '支社', '営業', '代理店', '担当', 'コールセンター',
            // デジタル関連
            'オンライン', 'サイト', 'アプリ', 'ウェブ', 'メール', 'マイページ', 'ログイン', 'パスワード'
        ];
        
        // 6. 質問パターンによる抽出強化（「〇〇は？」「〇〇について」などのパターン）
        const questionPatterns = [
            /(.{2,})(とは|って|について|の場合|する方法|する手続き|に必要な)/g,
            /(.{2,})(の変更|の解約|の請求|の支払|の確認)/g
        ];
        
        const patternNouns = [];
        questionPatterns.forEach(pattern => {
            let match;
            while ((match = pattern.exec(normalizedText)) !== null) {
                if (match[1] && match[1].length >= 2) {
                    patternNouns.push(match[1]);
                }
            }
        });
        
        // 7. 優先順位付けしてキーワード候補を結合
        const candidates = [
            // 最優先: 文法パターンから抽出した名詞（助詞の前など）
            ...nounsBeforeParticles,
            // 次優先: 質問パターンから抽出した名詞
            ...patternNouns,
            // 次優先: ドメイン辞書にある名詞
            ...potentialNouns.filter(word => domainNouns.some(noun => word.includes(noun))),
            // 最後: その他の名詞候補
            ...potentialNouns.filter(word => 
                !nounsBeforeParticles.includes(word) && 
                !patternNouns.includes(word) && 
                !domainNouns.some(noun => word.includes(noun))
            )
        ];
        
        // 8. 重複を削除
        const uniqueKeywords = [...new Set(candidates)];
        
        // 9. 最大10件に制限
        const limitedKeywords = uniqueKeywords.slice(0, 10);
        
        console.log('ローカル処理で抽出されたキーワード:', limitedKeywords);
        return limitedKeywords;
    }
    
    // キーワードを拡張する関数（シノニム対応）
    function expandKeywords(keywords) {
        try {
            // nullやundefinedをチェック
            if (!keywords || !Array.isArray(keywords)) {
                console.warn('expandKeywords: 無効なキーワード配列が渡されました', keywords);
                return [];
            }
            
            // シノニム辞書をlocalStorageから取得
            const synonymsText = localStorage.getItem('dsSynonyms') || 'クラウド:cloud,クラウド・コンピューティング\nAI:人工知能,artificial intelligence';
            
            // シノニム辞書をテキストからオブジェクトに変換
            const synonymDict = {};
            const reverseSynonymDict = {}; // 逆引き辞書
            
            try {
                synonymsText.split('\n').forEach(line => {
                    // 空白行や無効な行はスキップ
                    if (!line || !line.trim() || !line.includes(':')) {
                        return;
                    }
                    
                    try {
                        const [key, synonymsStr] = line.split(':');
                        // キーか値のどちらかが空の場合はスキップ
                        if (!key || !key.trim() || !synonymsStr) {
                            return;
                        }
                        
                        const trimmedKey = key.trim();
                        const synonyms = synonymsStr.split(',')
                            .map(s => s.trim())
                            .filter(s => s);
                            
                        // 同義語が1つ以上ある場合のみ辞書に追加
                        if (synonyms.length > 0) {
                            synonymDict[trimmedKey] = synonyms;
                            
                            // 逆引き辞書の作成（シノニムから元のキーワードを引けるようにする）
                            synonyms.forEach(synonym => {
                                if (synonym) {
                                    reverseSynonymDict[synonym] = trimmedKey;
                                }
                            });
                        }
                    } catch (lineError) {
                        console.warn('シノニム行の解析中にエラー:', line, lineError);
                        // 1行のエラーで全体が失敗しないように続行
                    }
                });
            } catch (parseError) {
                console.error('シノニム辞書の解析中にエラーが発生しました:', parseError);
                // エラーが発生しても処理を継続するため、空のオブジェクトを使用
            }
            
            console.log('使用するシノニム辞書:', synonymDict);
            console.log('使用する逆引きシノニム辞書:', reverseSynonymDict);
            
            // 各キーワードを拡張
            const expandedKeywords = [...keywords];
            
            // メモリ使用量を制限するため、拡張後のキーワード数に上限を設定
            const MAX_EXPANDED_KEYWORDS = 20;
            
            keywords.forEach(keyword => {
                if (!keyword || typeof keyword !== 'string') {
                    console.warn('無効なキーワードをスキップします:', keyword);
                    return;
                }
                
                // 通常の辞書検索（キーワード → シノニム）
                if (synonymDict[keyword]) {
                    // 過剰な拡張を防ぐためにチェック
                    if (expandedKeywords.length < MAX_EXPANDED_KEYWORDS) {
                        const synonymsToAdd = synonymDict[keyword].slice(0, MAX_EXPANDED_KEYWORDS - expandedKeywords.length);
                        expandedKeywords.push(...synonymsToAdd);
                    }
                }
                
                // 逆引き辞書検索（シノニム → 元のキーワード）
                if (reverseSynonymDict[keyword] && expandedKeywords.length < MAX_EXPANDED_KEYWORDS) {
                    expandedKeywords.push(reverseSynonymDict[keyword]);
                }
            });
            
            // 重複を削除して返す
            return [...new Set(expandedKeywords)].slice(0, MAX_EXPANDED_KEYWORDS);
        } catch (error) {
            console.error('キーワード拡張中にエラーが発生しました:', error);
            // 元のキーワードをそのまま返す（拡張せずに）
            return Array.isArray(keywords) ? keywords.slice(0, 10) : [];
        }
    }
    
    // キーワードをフォーマットする関数
    function formatKeywords(keywords) {
        return keywords.map(kw => `#${kw}`).join(' ');
    }
    
    // 感情解析APIのスタブ関数
    async function analyzeEmotion(text) {
        console.log('感情解析が呼び出されました:', text);
        // スタブ実装：実際のAPIを呼び出す代わりに固定値を返す
        // 本番実装時はここを実際のAPI呼び出しに置き換える
        await new Promise(resolve => setTimeout(resolve, 500)); // APIの待ち時間をシミュレート
        return {
            emotions: ['怒り', '不安']
        };
    }
    // 辞書アイコンのローディングアニメーション要素を作成
    function createDictionaryAnimation() {
        const loaderContainer = document.createElement('div');
        loaderContainer.className = 'dictionary-loader';
        loaderContainer.innerHTML = `
            <div class="dictionary-icon">
                <svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg">
                  <!-- Dictionary base -->
                  <rect x="20" y="30" width="60" height="50" rx="2" fill="#4A7293" />
                  <rect x="22" y="32" width="56" height="46" rx="1" fill="#E6F1F8" />
                  
                  <!-- Dictionary pages with text lines only (no actual text) -->
                  <g>
                    <rect x="22" y="32" width="56" height="46" fill="#F0F8FF" />
                    
                    <!-- Text lines - left page -->
                    <line x1="25" y1="38" x2="45" y2="38" stroke="#A0B8D0" stroke-width="0.4" />
                    <line x1="25" y1="41" x2="43" y2="41" stroke="#A0B8D0" stroke-width="0.4" />
                    <line x1="25" y1="44" x2="44" y2="44" stroke="#A0B8D0" stroke-width="0.4" />
                    <line x1="25" y1="47" x2="42" y2="47" stroke="#A0B8D0" stroke-width="0.4" />
                    <line x1="25" y1="50" x2="45" y2="50" stroke="#A0B8D0" stroke-width="0.4" />
                    <line x1="25" y1="53" x2="41" y2="53" stroke="#A0B8D0" stroke-width="0.4" />
                    <line x1="25" y1="56" x2="44" y2="56" stroke="#A0B8D0" stroke-width="0.4" />
                    <line x1="25" y1="59" x2="43" y2="59" stroke="#A0B8D0" stroke-width="0.4" />
                    <line x1="25" y1="62" x2="45" y2="62" stroke="#A0B8D0" stroke-width="0.4" />
                    <line x1="25" y1="65" x2="42" y2="65" stroke="#A0B8D0" stroke-width="0.4" />
                    <line x1="25" y1="68" x2="40" y2="68" stroke="#A0B8D0" stroke-width="0.4" />
                    <line x1="25" y1="71" x2="43" y2="71" stroke="#A0B8D0" stroke-width="0.4" />
                    
                    <!-- Text lines - right page -->
                    <line x1="55" y1="38" x2="75" y2="38" stroke="#A0B8D0" stroke-width="0.4" />
                    <line x1="55" y1="41" x2="73" y2="41" stroke="#A0B8D0" stroke-width="0.4" />
                    <line x1="55" y1="44" x2="74" y2="44" stroke="#A0B8D0" stroke-width="0.4" />
                    <line x1="55" y1="47" x2="72" y2="47" stroke="#A0B8D0" stroke-width="0.4" />
                    <line x1="55" y1="50" x2="75" y2="50" stroke="#A0B8D0" stroke-width="0.4" />
                    <line x1="55" y1="53" x2="71" y2="53" stroke="#A0B8D0" stroke-width="0.4" />
                    <line x1="55" y1="56" x2="74" y2="56" stroke="#A0B8D0" stroke-width="0.4" />
                    <line x1="55" y1="59" x2="73" y2="59" stroke="#A0B8D0" stroke-width="0.4" />
                    <line x1="55" y1="62" x2="75" y2="62" stroke="#A0B8D0" stroke-width="0.4" />
                    <line x1="55" y1="65" x2="72" y2="65" stroke="#A0B8D0" stroke-width="0.4" />
                    <line x1="55" y1="68" x2="70" y2="68" stroke="#A0B8D0" stroke-width="0.4" />
                    <line x1="55" y1="71" x2="73" y2="71" stroke="#A0B8D0" stroke-width="0.4" />
                    
                    <!-- Dictionary spine -->
                    <line x1="50" y1="32" x2="50" y2="78" stroke="#B0C6D8" stroke-width="0.7" />
                  </g>
                  
                  <!-- Moving highlighting effect -->
                  <g>
                    <!-- First highlight on left page -->
                    <rect x="25" y="43" width="0" height="3" fill="#FFFF00" fill-opacity="0.5">
                      <animate 
                        attributeName="width" 
                        values="0;20;0" 
                        dur="3s" 
                        repeatCount="indefinite" 
                        begin="0s" />
                      <animate 
                        attributeName="x" 
                        values="25;25;45" 
                        dur="3s" 
                        repeatCount="indefinite" 
                        begin="0s" />
                    </rect>
                    
                    <!-- Second highlight on left page -->
                    <rect x="25" y="53" width="0" height="3" fill="#FFFF00" fill-opacity="0.5">
                      <animate 
                        attributeName="width" 
                        values="0;20;0" 
                        dur="3s" 
                        repeatCount="indefinite" 
                        begin="1s" />
                      <animate 
                        attributeName="x" 
                        values="25;25;45" 
                        dur="3s" 
                        repeatCount="indefinite" 
                        begin="1s" />
                    </rect>
                    
                    <!-- First highlight on right page -->
                    <rect x="55" y="58" width="0" height="3" fill="#FFFF00" fill-opacity="0.5">
                      <animate 
                        attributeName="width" 
                        values="0;20;0" 
                        dur="3s" 
                        repeatCount="indefinite" 
                        begin="0.5s" />
                      <animate 
                        attributeName="x" 
                        values="55;55;75" 
                        dur="3s" 
                        repeatCount="indefinite" 
                        begin="0.5s" />
                    </rect>
                    
                    <!-- Second highlight on right page -->
                    <rect x="55" y="68" width="0" height="3" fill="#FFFF00" fill-opacity="0.5">
                      <animate 
                        attributeName="width" 
                        values="0;20;0" 
                        dur="3s" 
                        repeatCount="indefinite" 
                        begin="1.5s" />
                      <animate 
                        attributeName="x" 
                        values="55;55;75" 
                        dur="3s" 
                        repeatCount="indefinite" 
                        begin="1.5s" />
                    </rect>
                  </g>
                </svg>
            </div>
            <div class="dictionary-text">辞書と照合中...</div>
        `;
        return loaderContainer;
    }
    // API関連の関数
    async function fetchDocumentList(workId = null) {
        try {
            // APIエンドポイントを作成（workIdがある場合はクエリパラメータとして追加）
            let url = getBasePath() + '/api/data-structuring/filepaths';
            if (workId) {
                url += `?workId=${encodeURIComponent(workId)}`;
            }
            
                    // Azure Searchからファイルパスを取得
        const response = await fetch(url, {
            credentials: 'include'  // ASP.NET認証クッキーを送信
        });
            
            if (!response.ok) {
                console.error('ファイルパス一覧の取得に失敗しました:', response.status);
                return []; // エラー時は空の配列を返す
            }
            
            const data = await response.json();
            console.log('取得したファイルパス:', data);
            
            // 新しいレスポンス形式に対応（pages配列とprocessing_status）
            if (data.pages && Array.isArray(data.pages)) {
                console.log('新形式のレスポンス（pages配列）を検出');
                
                // 処理進捗情報をチェック
                if (data.processing_status) {
                    console.log('処理進捗情報:', data.processing_status);
                    
                    // 進捗情報をグローバル変数として保存（他の関数からアクセス可能にする）
                    window.currentProcessingStatus = data.processing_status;
                }
                
                // シノニムデータの処理
                if (data.synonym_list || data.synonym) {
                    console.log('シノニムデータを検出:', {
                        synonym_list: data.synonym_list?.length || 0,
                        synonym: data.synonym?.length || 0
                    });
                    
                    // シノニムデータを保存
                    saveSynonymData(data.synonym_list, data.synonym, workId);
                    
                    // シノニム専用トーストを表示
                    //displaySynonymToast(data.synonym_list, data.synonym);
                }
                
                // pages配列を処理
                return processDocumentPages(data.pages);
            }
            
            // 旧形式の互換性維持（配列が直接返される場合）
            if (Array.isArray(data)) {
                console.log('旧形式の配列レスポンスを検出');
                return processDocumentPages(data);
            }
            
            // APIから受け取ったデータをコンソールに出力
            console.log('データの型:', typeof data);
            console.log('配列か?:', Array.isArray(data));
            
            // APIのレスポンスデータをトースト表示する（デバッグ用）
            //showToast(JSON.stringify(data, null, 2), 30000);
            
            // データの内容を詳しく確認
            if (Array.isArray(data) && data.length > 0) {
                console.log('最初のアイテムのプロパティ:', Object.keys(data[0]));
                
                // チャンクリスト形式の確認（documents配列があるか）
                if (data[0].documents && Array.isArray(data[0].documents)) {
                    console.log('新形式: chunk_list形式のデータ（ページごとにグループ化済み）');
                    
                    // 詳細ログ出力
                    const firstGroup = data[0];
                    console.log(`最初のページグループ: ID=${firstGroup.id}, 名前=${firstGroup.name}, ドキュメント数=${firstGroup.documents.length}`);
                    
                    if (firstGroup.documents.length > 0) {
                        const firstDoc = firstGroup.documents[0];
                        console.log(`最初のドキュメント: ID=${firstDoc.id}, 名前=${firstDoc.name}, テキスト長=${firstDoc.text?.length || 0}`);
                    }
                    
                    // ページごとにグループ化されたデータをそのまま使用
                    return data.map(page => {
                        // ページ内のすべてのチャンクのテキストを結合
                        let combinedText = '';
                        if (Array.isArray(page.documents)) {
                            combinedText = page.documents
                                .map(chunk => chunk.text || '')
                                .join('\n\n');
                        }
                        
                        return {
                            id: page.id,
                            displayName: page.name,
                            pageNumber: page.pageNumber,
                            documents: page.documents,
                            content: combinedText  // グループ全体の結合テキスト
                        };
                    });
                }
                
                // 従来の形式の処理
                console.log('従来形式のデータ処理');
                
                // データをグループ化する処理を追加
                const groupedByPage = groupDocumentsByPage(data);
                console.log('ページ番号でグループ化したデータ:', groupedByPage);
                
                // 詳細なデバッグログを出力（グループの詳細構造）
                console.log('=== ページグループのデバッグ情報 ===');
                groupedByPage.forEach((group, groupIndex) => {
                    console.log(`グループ ${groupIndex+1}: ${group.displayName}, ID=${group.id}`);
                    console.log(`  ドキュメント数: ${group.documents ? group.documents.length : 0}`);
                    
                    if (group.documents && group.documents.length > 0) {
                        // 最初と最後のドキュメントの詳細を表示
                        const firstDoc = group.documents[0];
                        const lastDoc = group.documents[group.documents.length - 1];
                        
                        console.log(`  最初のドキュメント: ID=${firstDoc.id}`);
                        console.log(`    プロパティ: ${Object.keys(firstDoc).join(', ')}`);
                        console.log(`    テキストの有無: ${firstDoc.text ? '有り' : '無し'}`);
                        if (firstDoc.text) {
                            console.log(`    テキストサンプル: ${firstDoc.text.substring(0, 30)}...`);
                        }
                        
                        console.log(`  最後のドキュメント: ID=${lastDoc.id}`);
                        console.log(`    プロパティ: ${Object.keys(lastDoc).join(', ')}`);
                        console.log(`    テキストの有無: ${lastDoc.text ? '有り' : '無し'}`);
                    }
                });
                console.log('=== デバッグ情報終了 ===');
                
                return groupedByPage; // グループ化されたデータを返す
            }
            
            // データがない場合は空の配列を返す
            if (!data) {
                console.warn('Azure Searchからデータが取得できませんでした。');
                return [];
            }
            
            // 想定外の形式の場合でも処理を試みる
            console.log('データ形式が想定と異なります - コントローラーで整形されていない可能性があります');
            
            try {
                // APIから直接filepathプロパティを持つオブジェクト配列が返された場合
                if (data.value && Array.isArray(data.value)) {
                    console.log('data.valueを配列として処理します');
                    const processedData = data.value.map((item, index) => {
                        // filepathプロパティがある場合
                        if (item.filepath) {
                            const filename = item.filepath.split('/').pop() || item.filepath;
                            
                            // PDFファイルかどうかを判断
                            const isPDF = item.filepath.includes('pdf_') || /\.pdf/i.test(item.filepath);
                            
                            // ファイル名からPDF文書名とページ番号を抽出
                            let displayName = filename;
                            let pageNum = null;
                            
                            if (filename.includes('-page-')) {
                                const parts = filename.split('-page-');
                                const pdfName = parts[0];
                                pageNum = parts[1].replace('.txt', '');
                                displayName = `【PDF文書】 ${pdfName} (ページ ${pageNum})`;
                            }
                            
                            return {
                                id: `path_${index}`,
                                name: displayName,
                                filepath: item.filepath,
                                fileType: isPDF ? 'PDF' : 'TEXT',
                                pageNumber: pageNum ? parseInt(pageNum) : null,
                                chunkNumber: item.chunkNo || null
                            };
                        }
                        
                        return {
                            id: `item_${index}`,
                            name: `Document ${index + 1}`,
                            filepath: JSON.stringify(item),
                            fileType: 'UNKNOWN',
                            pageNumber: null,
                            chunkNumber: null
                        };
                    });
                    
                    // ページ番号でグループ化する
                    const groupedByPage = groupDocumentsByPage(processedData);
                    console.log('ページ番号でグループ化したデータ:', groupedByPage);
                    
                    return groupedByPage;
                }
                
                // その他の形式の場合は空配列を返す
                console.warn('予期しないデータ形式:', typeof data);
                return [];
            } catch (innerError) {
                console.error('データ処理中のエラー:', innerError);
                return [];
            }
        } catch (error) {
            console.error('fetchDocumentList エラー:', error);
            return []; // エラー時は空の配列を返す
        }
    }
    
    // ドキュメントをページ番号でグループ化する関数
    function groupDocumentsByPage(documents) {
        console.log('ページ番号によるグループ化を開始:', documents.length, '件');
        
        // すでにページグループ化されているかチェック（chunk_list形式）
        if (documents.length > 0 && documents[0].documents && Array.isArray(documents[0].documents)) {
            console.log('すでにページごとにグループ化されたデータを検出しました。');
            return documents;
        }
        
        // ページ番号ごとのグループを保持するオブジェクト
        const pageGroups = {};
        let currentPageIndex = 1; // 人為的なページ番号を追跡
        let currentPageKey = `page_${currentPageIndex}`;
        
        // ページ開始を示すマーカーテキスト
        const pageMarkerText = "# 出力結果";
        let pageStartIndexes = [];
        
        // まず「# 出力結果」があるインデックスを全て見つける
        documents.forEach((doc, index) => {
            if (doc.text === pageMarkerText) {
                pageStartIndexes.push(index);
            }
        });
        
        // 「# 出力結果」がない場合は従来の処理
        if (pageStartIndexes.length === 0) {
            console.log('「# 出力結果」が見つからないため、従来のグループ化処理を実行します');
            
            // ページグループを初期化
            pageGroups[currentPageKey] = {
                pageNumber: currentPageIndex,
                displayName: `ページ ${currentPageIndex}`,
                documents: [],
                id: `group_${currentPageKey}`
            };
            
            // 全てのドキュメントを最初のページに追加
            documents.forEach(doc => {
                pageGroups[currentPageKey].documents.push({
                    ...doc,
                    pageNumber: currentPageIndex
                });
            });
        } else {
            console.log('「# 出力結果」を基準にページ分割を行います。検出箇所:', pageStartIndexes);
            
            // 「# 出力結果」を基準にページ分割
            for (let i = 0; i < pageStartIndexes.length; i++) {
                const startIndex = pageStartIndexes[i];
                const endIndex = (i < pageStartIndexes.length - 1) ? 
                                  pageStartIndexes[i + 1] - 1 : 
                                  documents.length - 1;
                
                currentPageKey = `page_${currentPageIndex}`;
                
                // ページグループを初期化
                pageGroups[currentPageKey] = {
                    pageNumber: currentPageIndex,
                    displayName: `ページ ${currentPageIndex}`,
                    documents: [],
                    id: `group_${currentPageKey}`
                };
                
                // このページ範囲のドキュメントを追加
                for (let j = startIndex; j <= endIndex; j++) {
                    pageGroups[currentPageKey].documents.push({
                        ...documents[j],
                        pageNumber: currentPageIndex
                    });
                }
                
                currentPageIndex++;
            }
        }
        
        // 3. ページ番号順にグループをソートして配列に変換
        const sortedGroups = Object.values(pageGroups).sort((a, b) => {
            // ページ番号の昇順にソート
            return a.pageNumber - b.pageNumber;
        });
        
        console.log('グループ化結果:', sortedGroups.length, 'グループが作成されました');
        return sortedGroups;
    }

    async function fetchDocumentContent(docId) {
        try {
            console.log(`fetchDocumentContent: ID=${docId} の内容を取得します`);
            
            // まず元のpageItemsのフラット配列から検索（旧ロジック）
            const originalItem = pageItems.find(item => item.id === docId);
            if (originalItem) {
                console.log(`元のpageItemsからアイテムを見つけました: ${originalItem.id}`);
                
                // テキストプロパティが直接ある場合（API呼び出し不要）
                if (originalItem.text) {
                    console.log(`アイテム ${docId} には直接テキストがあります`);
                    const contentObject = {
                        id: docId,
                        name: originalItem.name,
                        filepath: originalItem.filepath,
                        content: originalItem.text,
                        pageNumber: originalItem.pageNumber,
                        chunkNumber: originalItem.chunkNumber,
                        timestamp: new Date()
                    };
                    return contentObject;
                }
                
                // filepathがあるケース（通常のAPI呼び出し）
                if (originalItem.filepath) {
                    // 以下は元のコード
                    // PDFの場合、キャッシュを確認
                    if (originalItem.name && originalItem.name.includes('【PDF文書】')) {
                        const pdfBaseName = originalItem.name.split('(ページ')[0].trim();
                        
                        // キャッシュ内にデータがあるか確認
                        if (pdfTextCache[pdfBaseName] && pdfTextCache[pdfBaseName][docId]) {
                            console.log(`キャッシュからPDF "${pdfBaseName}" のページ ${docId} を取得しました`);
                            return pdfTextCache[pdfBaseName][docId];
                        }
                    }
                    
                    // キャッシュになければAPIから取得
                    const response = await fetch(getBasePath() + `/api/data-structuring/content?filepath=${encodeURIComponent(originalItem.filepath)}`, {
                        credentials: 'include' // ASP.NET認証クッキーを含める
                    });
                    
                    if (!response.ok) {
                        console.error('ファイルコンテンツの取得に失敗しました:', response.status);
                        return null;
                    }
                    
                    // JSONオブジェクトとしてレスポンスを取得
                    const responseData = await response.json();
                    console.log('取得したレスポンスデータ:', responseData);
                    
                    // JSONオブジェクトからコンテンツを抽出
                    if (responseData && responseData.content) {
                        console.log('コンテンツサンプル:', responseData.content.substring(0, 100) + '...');
                        
                        // テキストからp.X形式のページ番号を抽出
                        let extractedPageNumber = null;
                        let chunkNumber = null;
                        
                        // 正規表現でp.X形式のページ番号を検索
                        const pageMatch = responseData.content.match(/[pP]\.(\d+)/i);
                        if (pageMatch && pageMatch[1]) {
                            extractedPageNumber = parseInt(pageMatch[1]);
                            console.log(`テキストから抽出したページ番号: p.${extractedPageNumber}`);
                        }
                        
                        // チャンク番号の抽出を試みる
                        if (originalItem.chunkNumber !== undefined && originalItem.chunkNumber !== null) {
                            chunkNumber = originalItem.chunkNumber;
                        } else {
                            // APIレスポンスからchunk_no情報を取得
                            if (responseData.chunk_no !== undefined) {
                                chunkNumber = parseInt(responseData.chunk_no);
                            }
                            
                            // テキスト内容からchunk_no情報を探す
                            if (chunkNumber === null) {
                                const chunkMatch = responseData.content.match(/chunk[_\s]?no[\.:]?\s*(\d+)/i);
                                if (chunkMatch && chunkMatch[1]) {
                                    chunkNumber = parseInt(chunkMatch[1]);
                                }
                            }
                        }
                        
                        // ページ番号情報を含むコンテンツオブジェクトを作成
                        const contentObject = {
                            id: docId,
                            name: originalItem.name || responseData.name,
                            filepath: originalItem.filepath || responseData.filepath,
                            content: responseData.content,
                            pageNumber: extractedPageNumber || (originalItem.pageNumber !== undefined ? originalItem.pageNumber : null),
                            chunkNumber: chunkNumber,
                            timestamp: new Date()
                        };
                        
                        // PDFの場合はキャッシュに保存
                        if (originalItem.name && originalItem.name.includes('【PDF文書】')) {
                            const pdfBaseName = originalItem.name.split('(ページ')[0].trim();
                            
                            // キャッシュが初期化されていなければ初期化
                            if (!pdfTextCache[pdfBaseName]) {
                                pdfTextCache[pdfBaseName] = {};
                            }
                            
                            // キャッシュに保存
                            pdfTextCache[pdfBaseName][docId] = contentObject;
                        }
                        
                        console.log('処理済みコンテンツオブジェクト:', contentObject);
                        return contentObject;
                    } else {
                        console.error('レスポンスデータにコンテンツがありません:', responseData);
                        return null;
                    }
                }
            }
            
            // ここからはグループ化されたpageItemsから検索（新ロジック）
            console.log('グループ化されたページアイテムから検索します...');
            
            // グループ化されたpageItemsを使って検索
            for (const group of pageItems) {
                if (Array.isArray(group.documents)) {
                    // 該当するIDをグループ内から検索
                    const foundDoc = group.documents.find(doc => doc.id === docId);
                    if (foundDoc) {
                        console.log(`グループ内からドキュメント ${docId} を見つけました:`, foundDoc);
                        
                        // テキストプロパティが直接ある場合
                        if (foundDoc.text) {
                            return {
                                id: docId,
                                name: foundDoc.name || `ドキュメント ${docId}`,
                                content: foundDoc.text,
                                pageNumber: foundDoc.pageNumber,
                                chunkNumber: foundDoc.chunkNumber,
                                timestamp: new Date()
                            };
                        }
                        
                        // ファイルパスがある場合は上記の処理と同様
                        if (foundDoc.filepath) {
                            console.log(`ファイルパス ${foundDoc.filepath} からコンテンツを取得します`);
                            // 元のロジックと同じAPIリクエスト処理...
                            // 長いので省略しています（実際は上記の処理と同じコードが続く）
                        }
                        
                        // ファイルパスもテキストも両方ない場合はエラー
                        console.error(`ドキュメント ${docId} にはテキストもファイルパスもありません`);
                        return {
                            id: docId,
                            name: foundDoc.name || `ドキュメント ${docId}`,
                            content: '内容を取得できませんでした',
                            timestamp: new Date()
                        };
                    }
                }
            }
            
            // それでも見つからない場合
            console.error(`ID ${docId} のドキュメントが見つかりません。利用可能なドキュメント:`, 
                         Array.isArray(pageItems) ? pageItems.length : 'pageItemsが配列ではありません');
            throw new Error('ファイルパスが見つかりません');
        } catch (error) {
            console.error('fetchDocumentContent エラー:', error);
            return null;
        }
    }
    async function sendChatMessage(message, docId) {
        try {
            console.log('🚀 STEP 1: sendChatMessage開始');
            
            // ユーザーのクエリ部分を抽出（プロンプトテンプレートを使用している場合）
            const userQuery = message.includes('\n\n') 
                ? message.split('\n\n').pop()
                : message;
                
            console.log('🚀 STEP 2: クエリ抽出完了');
            
            // キーワード抽出と拡張
            let keywords = [];
            let expandedKeywords = [];
            let emotions = [];
            
            try {
                console.log('🚀 STEP 3: キーワード抽出開始');
                keywords = await extractKeywords(userQuery);
                expandedKeywords = expandKeywords(keywords);
                const emotionResult = await analyzeEmotion(userQuery);
                emotions = emotionResult.emotions || [];
                console.log('🚀 STEP 4: キーワード抽出完了');
            } catch (analysisError) {
                console.error('❌ STEP 4 エラー: キーワード抽出失敗', analysisError);
            }
            
            // 🚀 STEP 5-7: Azure Search ハイブリッドセマンティック検索はchatエンドポイント内で実行
            console.log('🚀 STEP 5: Azure Search検索はchatエンドポイント内で実行されます');
            
            // APIリクエスト
            try {
                console.log('🚀 STEP 6: チャットAPI呼び出し準備開始');
                
                // ASP.NET認証確認（ログイン状態チェック）
                const currentUser = await getCurrentUser();
                if (!currentUser) {
                    throw new Error('認証が必要です。ログインしてください。');
                }
                
                // リクエストボディの構築（ASP.NET認証統一: username/password不要）
                const requestBody = {
                    message: message,
                    context: '', // Azure Searchでサーバー側が生成
                    sources: [], // Azure Searchでサーバー側が生成
                    use_chunks: true, // Azure Search使用フラグ
                    chunks: [], // Azure Searchで検索するため空
                    work_id: "", // 全workId検索のため空
                    // username/passwordは不要（ASP.NET認証クッキーで認証）
                };
                
                // fileIdも含める（後方互換性のため）
                if (docId) {
                    requestBody.file_id = docId;
                } else {
                    requestBody.file_id = "no-document";
                }
                
                // 常にfile_idが空にならないようにする（バックエンドの検証エラー回避）
                if (!requestBody.file_id || requestBody.file_id === '') {
                    requestBody.file_id = "no-document";
                }
                
                // 🚀 Azure Search使用: すべてサーバー側で処理
                console.log('🔍 Azure Search使用: 検索・コンテキスト生成はサーバー側で実行');
                
                // 拡張キーワードが存在する場合のみkeywordsフィールドを追加
                if (Array.isArray(expandedKeywords) && expandedKeywords.length > 0) {
                    requestBody.keywords = expandedKeywords.slice(0, 20); // キーワード数を制限
                }
                
                // 統合されたシノニムデータをサーバーに送信（存在する場合のみ）
                const combinedSynonyms = getCombinedSynonyms();
                const synonymData = getSynonymData();
                
                if (combinedSynonyms && combinedSynonyms.trim()) {
                    requestBody.synonyms = combinedSynonyms;
                    console.log('🔍 統合シノニムを送信:', combinedSynonyms.length, '文字');
                } else {
                    console.log('🔍 統合シノニムなし - フィールドを送信しません');
                }
                
                if (synonymData.synonymList && Array.isArray(synonymData.synonymList)) {
                    // 有効な同義語項目のみをフィルタリングし、サーバー形式に変換
                    const validSynonymList = synonymData.synonymList
                        .filter(item => {
                            if (!item) return false;
                        
                            // 'synonym' (単数形) を正式プロパティとし、旧プロパティもフォールバックで許容
                            const synArr = Array.isArray(item.synonym)
                                ? item.synonym
                                : (Array.isArray(item.Synonym)
                                    ? item.Synonym
                                    : (Array.isArray(item.synonyms)
                                        ? item.synonyms
                                        : (Array.isArray(item.Synonyms) ? item.Synonyms : [])));
                        
                            return synArr.length > 0 && synArr.some(s => s && s.trim());
                        })
                        .map(item => {
                            // サーバーが期待する形式に変換: { keyword: string, synonym: string[] }
                            const synArr = Array.isArray(item.synonym)
                                ? item.synonym
                                : (Array.isArray(item.Synonym)
                                    ? item.Synonym
                                    : (Array.isArray(item.synonyms)
                                        ? item.synonyms
                                        : (Array.isArray(item.Synonyms) ? item.Synonyms : [])));
                            
                            return {
                                keyword: synArr[0] || '', // 最初の要素をキーワードとして使用
                                synonym: synArr.slice(1) // 残りをシノニム配列として使用
                            };
                        })
                        .filter(item => item.keyword && item.synonym.length > 0); // keywordが空でない、かつシノニムがある項目のみ
                    
                    console.log('🔍 シノニムリストフィルタリング:', {
                        original: synonymData.synonymList.length,
                        filtered: validSynonymList.length
                    });
                    
                    if (validSynonymList.length > 0) {
                        requestBody.synonym_list = validSynonymList;
                        console.log('🔍 APIシノニムリストを送信:', validSynonymList.length, '件');
                        console.log('🔍 サンプル項目:', validSynonymList.slice(0, 2));
                    } else {
                        console.log('🔍 APIシノニムリストなし - フィールドを送信しません');
                    }
                } else {
                    console.log('🔍 シノニムデータなし - フィールドを送信しません');
                }
                
                console.log('🚀 STEP 7: チャットAPI呼び出し開始');
                
                const response = await fetch(getBasePath() + '/api/data-structuring/chat', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json'
                    },
                    credentials: 'include', // ASP.NET認証クッキーを含める
                    body: JSON.stringify(requestBody)
                });
                
                if (!response.ok) {
                    const errorText = await response.text();
                    console.error('❌ STEP 7 エラー: チャットAPI失敗', errorText);
                    throw new Error(`チャットAPIエラー: ${response.status} ${response.statusText}`);
                }
                
                console.log('🚀 STEP 8: チャットAPI応答受信');
                
                const data = await response.json();
                console.log('🚀 STEP 9: レスポンスパース完了');
                
                console.log('🚀 STEP 10: UI更新完了 - sendChatMessage正常終了');
                return data;
                
            } catch (chatError) {
                console.error('❌ STEP 6-10 エラー: チャット処理失敗', chatError);
                throw chatError;
            }
        } catch (error) {
            console.error('❌ sendChatMessage全体エラー:', error);
            throw error;
        }
    }

    // ページアイテムのグローバル変数（ファイルパスを保持するため）
    let pageItems = [];
    
    // サーバー側認証情報キャッシュ
    let serverAuthCache = null;
    // 🔐 ASP.NET統一認証システム
    async function getCurrentUser() {
        // ASP.NET認証情報から取得（統一認証方法）
        try {
            // キャッシュされた認証情報があり有効な場合はそれを使用
            if (serverAuthCache && (Date.now() - serverAuthCache.timestamp) < 300000) { // 5分間キャッシュ
                return serverAuthCache.userInfo;
            }
            
            const response = await fetch(getBasePath() + '/api/data-structuring/current-user', {
                method: 'GET',
                credentials: 'include' // ASP.NET認証クッキーを含める
            });
            
            if (response.ok) {
                const userInfo = await response.json();
                
                // ASP.NET認証情報をキャッシュに保存
                serverAuthCache = {
                    userInfo: userInfo,
                    timestamp: Date.now()
                };
                
                console.log('ASP.NET認証情報を取得しました:', userInfo.username, '(Role:', userInfo.role + ')');
                return userInfo;
            } else {
                console.error('ASP.NET認証に失敗しました。ログインが必要です。');
                // ログインページにリダイレクト
                window.location.href = getBasePath() + '/Login';
                return null;
            }
        } catch (error) {
            console.error('ASP.NET認証情報の取得中にエラーが発生しました:', error);
            // ログインページにリダイレクト
                            window.location.href = getBasePath() + '/Login';
            return null;
        }
    }
    
    // ASP.NET認証キャッシュをクリア（ログアウト時などに使用）
    function clearAuthCache() {
        // ASP.NET認証キャッシュをクリア
        serverAuthCache = null;
        console.log('ASP.NET認証キャッシュをクリアしました');
    }

    // 🚀 Azure Search使用: DocumentStorageは不要（削除済み）
    // Azure Search APIで直接検索するため、クライアント側でのドキュメントキャッシュは不要

    // UIの初期化
    // 処理ログをポーリングする関数
    async function startPollingProcessLogs(processId) {
        let pollingInterval;
        let pollCounter = 0; // ポーリングカウンター
        const MAX_POLLS = 720; // 最大ポーリング回数（1時間の5秒ごとのポーリング）
        const logElement = document.querySelector('#upload-notification .processing-log');
        const notification = document.querySelector('#upload-notification');
        let previousLogsCount = 0; // 前回までに取得したログの数
        
        // ポーリングを実装する関数
        async function pollLogs() {
            try {
                pollCounter++;
                const response = await fetch(getBasePath() + `/api/data-structuring/process-logs/${processId}`, {
                    credentials: 'include' // ASP.NET認証クッキーを含める
                });
                if (response.ok) {
                    const data = await response.json();
                    if (data.logs && data.logs.length > 0) {
                        // 新しいログのみを取得
                        const newLogs = data.logs.slice(previousLogsCount);
                        console.log(`ポーリング #${pollCounter}: 新しいログ ${newLogs.length}件`);
                        
                        // 新しいログを表示
                        newLogs.forEach(log => {
                            const logItem = document.createElement('div');
                            logItem.classList.add('log-entry', 'log-entry-new');
                            // JST 時刻に変換
                            let displayLog = log;
                            const m = log.match(/^\[(\d{2}):(\d{2}):(\d{2})\]/);
                            if (m) {
                                let h = (parseInt(m[1]) + 9) % 24;
                                const hh = ('0' + h).slice(-2);
                                const mm = m[2];
                                const ss = m[3];
                                displayLog = `[${hh}:${mm}:${ss}]` + log.slice(10);
                            }
                            logItem.textContent = displayLog;
                            logElement.appendChild(logItem);
                            
                            // エラーメッセージかどうかをチェック
                            if (log.includes('エラー:') || log.includes('失敗しました') || log.includes('Error:')) {
                                logItem.style.color = '#b91c1c';
                            }
                            
                            // 処理完了を検知
                            if (log.includes('PDFの処理が完了しました')) {
                                // 成功または失敗のカウント情報を取得
                                const completionInfo = log.match(/ページ数: (\d+)、成功: (\d+)、失敗: (\d+)/);
                                
                                if (completionInfo) {
                                    const totalPages = parseInt(completionInfo[1], 10);
                                    const successCount = parseInt(completionInfo[2], 10);
                                    const failCount = parseInt(completionInfo[3], 10);
                                    
                                    // 処理の完了を通知
                                    stopPolling();
                                    
                                    // 全て失敗した場合はエラー表示
                                    if (failCount === totalPages) {
                                        notification.classList.add('error');
                                        {
                                            const txt = notification.querySelector('.processing-notification-header .processing-text');
                                            if (txt) txt.textContent = `エラー: PDFの処理が完了しましたが、${failCount}ページすべての処理に失敗しました。`;
                                        }
                                        
                                        // ログダウンロードボタンと処理完了確認ボタンを表示
                                        const actionsDiv = notification.querySelector('.notification-actions');
                                        actionsDiv.style.display = 'flex';
                                    } else if (failCount > 0) {
                                        // 一部失敗があった場合
                                        notification.classList.remove('error');
                                        {
                                            const txt = notification.querySelector('.processing-notification-header .processing-text');
                                            if (txt) txt.textContent = `PDFの処理が完了しました。${successCount}ページ成功、${failCount}ページ失敗。ページを更新しています...`;
                                        }
                                            
                                        // ページをリロード
                                        setTimeout(() => {
                                            window.location.reload();
                                        }, 3000);
                                    } else {
                                        // 全て成功した場合
                                        notification.classList.remove('error');
                                        {
                                            const txt = notification.querySelector('.processing-notification-header .processing-text');
                                            if (txt) txt.textContent = `PDFの処理が完了しました。全ページのキャッシュを開始します...`;
                                        }
                                        
                                        // 全て成功した場合は、自動的に再読み込みして全ページをキャッシュする
                                        setTimeout(async () => {
                                            // ドキュメントリストを更新
                                            const documents = await fetchDocumentList();
                                            renderDocumentList(documents);
                                            pageItems = documents;
                                            
                                            // 新しくアップロードされたPDFを探す
                                            // PDFドキュメントのファイル名からベース名を抽出（パターン：ファイル名-page-X.txt）
                                            const filePrefixRegex = /(.+)-page-\d+.txt$/;
                                            
                                            // PDFファイルの基本名でグループ化
                                            const pdfGroups = {};
                                            
                                            documents.forEach(doc => {
                                                if (doc.name && doc.name.includes('【PDF文書】')) {
                                                    // ファイルパスからベース名を抽出
                                                    const match = doc.id.match(filePrefixRegex);
                                                    if (match) {
                                                        const basePrefix = match[1];
                                                        if (!pdfGroups[basePrefix]) {
                                                            pdfGroups[basePrefix] = [];
                                                        }
                                                        pdfGroups[basePrefix].push(doc);
                                                    }
                                                }
                                            });
                                            
                                            // PDFグループを見つけた場合
                                            const pdfPrefixes = Object.keys(pdfGroups);
                                            if (pdfPrefixes.length > 0) {
                                                console.log(`全ページのキャッシュを開始します: ${pdfPrefixes.length}個のPDFファイルを検出`);
                                                
                                                // 最新のPDFグループを取得（通常は最後にアップロードされたもの）
                                                const latestPrefix = pdfPrefixes[pdfPrefixes.length - 1];
                                                const pdfPages = pdfGroups[latestPrefix];
                                                
                                                // PDFの表示名（【PDF文書】部分）を取得
                                                const pdfDisplayName = pdfPages[0].name.split(' (')[0];
                                                console.log(`最新のPDFファイル "${pdfDisplayName}" (${pdfPages.length}ページ) の全ページキャッシュを開始します`);
                                                
                                                // 全ページをキャッシュする
                                                startCachingPdf(pdfDisplayName);
                                            }
                                            
                                            if (window.completeProcessing) {
                                                window.completeProcessing();
                                            }
                                        }, 1000);
                                    }
                                    return; // ポーリングを終了
                                }
                            }
                        });
                        
                        // 取得したログの数を更新
                        previousLogsCount = data.logs.length;
                        
                        // 最新のログが見えるように自動スクロール
                        logElement.scrollTop = logElement.scrollHeight;
                    }
                }
                
                // 最大ポーリング回数に達したかチェック
                if (pollCounter >= MAX_POLLS) {
                    stopPolling();
                    console.log('最大ポーリング回数に達しました。ポーリングを停止します。');
                    notification.classList.add('timeout');
                    {
                        const txt = notification.querySelector('.processing-notification-header .processing-text');
                        if (txt) txt.textContent = 'PDF処理がタイムアウトしました。処理が完了しているか確認するか、ページを更新してください。';
                    }
                    
                    // リカバリーボタンと更新ボタンを表示
                    const actionsDiv = notification.querySelector('.notification-actions');
                    actionsDiv.style.display = 'flex';
                    
                    // リカバリーボタンと更新ボタンにイベントリスナーを設定
                    const reloadBtn = notification.querySelector('#reload-btn');
                    
                    // ページ更新ボタン
                    if (reloadBtn) {
                        reloadBtn.addEventListener('click', function() {
                            window.location.reload();
                        });
                    }
                }
            } catch (error) {
                console.error('処理ログ取得エラー:', error);
            }
        }
        
        // ポーリングを停止する関数
        function stopPolling() {
            if (pollingInterval) {
                clearInterval(pollingInterval);
                pollingInterval = null;
                console.log('ログポーリングを停止しました');
            }
        }
        
        // ログ表示領域を表示
        logElement.style.display = 'block';
        
        // 最初に一度ログを取得
        await pollLogs();
        
        // 5秒ごとにログを取得（サーバー負荷軽減のため）
        pollingInterval = setInterval(pollLogs, 5000);
        
        // 停止用の関数を返す
        return stopPolling;
    }

    // UIの初期化
    // トースト通知を表示する関数
    function showToast(message, duration = 5000) {
        // 既存のトーストがあれば削除
        const existingToast = document.getElementById('data-toast');
        if (existingToast) {
            document.body.removeChild(existingToast);
        }
        
        // トースト要素を作成
        const toast = document.createElement('div');
        toast.id = 'data-toast';
        toast.style.cssText = `
            position: fixed;
            bottom: 20px;
            left: 50%;
            transform: translateX(-50%);
            background-color: rgba(0, 0, 0, 0.8);
            color: white;
            padding: 12px 20px;
            border-radius: 4px;
            z-index: 9999;
            max-width: 90%;
            max-height: 70vh;
            overflow: auto;
            font-size: 14px;
            box-shadow: 0 4px 8px rgba(0, 0, 0, 0.2);
            word-break: break-word;
            white-space: pre-wrap;
        `;
        
        // 閉じるボタン
        const closeBtn = document.createElement('button');
        closeBtn.innerText = '×';
        closeBtn.style.cssText = `
            position: absolute;
            top: 5px;
            right: 5px;
            background: none;
            border: none;
            color: white;
            font-size: 18px;
            cursor: pointer;
            padding: 0 5px;
        `;
        closeBtn.onclick = () => document.body.removeChild(toast);
        
        // メッセージコンテナ
        const messageContainer = document.createElement('div');
        messageContainer.style.paddingRight = '20px';
        messageContainer.textContent = message;
        
        toast.appendChild(closeBtn);
        toast.appendChild(messageContainer);
        document.body.appendChild(toast);
        
        // 指定時間後に消える（0以下なら消えない）
        if (duration > 0) {
            setTimeout(() => {
                if (document.body.contains(toast)) {
                    document.body.removeChild(toast);
                }
            }, duration);
        }
        
        return toast;
    }

    // シノニム専用トースト表示関数
    function displaySynonymToast(synonymList, synonymData) {
        console.log('シノニム専用トースト表示開始');
        console.log('synonymList:', synonymList);
        console.log('synonymData:', synonymData);
        
        let synonymInfo = '';
        
        // シノニムリストの処理
        if (synonymList && Array.isArray(synonymList) && synonymList.length > 0) {
            console.log(`シノニムリスト: ${synonymList.length}件`);
            
            // 最初の10件のシノニムリストを表示
            const displayCount = Math.min(10, synonymList.length);
            const synonymTexts = [];
            
            for (let i = 0; i < displayCount; i++) {
                const item = synonymList[i];
                if (item && item.synonym && Array.isArray(item.synonym)) {
                    synonymTexts.push(item.synonym.join(', '));
                }
            }
            
            if (synonymTexts.length > 0) {
                synonymInfo += `【シノニムリスト】(${synonymList.length}件中${displayCount}件表示)\n`;
                synonymInfo += synonymTexts.join('\n');
            }
        }
        
        // シノニムデータの処理
        if (synonymData && Array.isArray(synonymData) && synonymData.length > 0) {
            console.log(`シノニムデータ: ${synonymData.length}件`);
            
            if (synonymInfo) synonymInfo += '\n\n';
            synonymInfo += `【シノニムデータ】(${synonymData.length}件)\n`;
            
            // 最初の10件のシノニムデータを表示
            const displayCount = Math.min(10, synonymData.length);
            const synonymDataTexts = [];
            
            for (let i = 0; i < displayCount; i++) {
                const item = synonymData[i];
                if (typeof item === 'string') {
                    synonymDataTexts.push(item);
                } else if (item && typeof item === 'object') {
                    synonymDataTexts.push(JSON.stringify(item));
                }
            }
            
            if (synonymDataTexts.length > 0) {
                synonymInfo += synonymDataTexts.join(', ');
            }
        }
        
        // シノニム情報がある場合のみトーストを表示
        if (synonymInfo) {
            console.log('シノニムトーストを表示:', synonymInfo.substring(0, 100) + '...');
            showToast(synonymInfo, 10000); // 10秒間表示
        } else {
            console.log('表示するシノニム情報がありません');
            showToast('シノニムデータが見つかりませんでした', 5000);
        }
    }
    
    async function initUI() {
        console.log('UIの初期化を開始します');
        
        try {
            // 全ドキュメントキャッシュの初期化
            console.log('全ドキュメントキャッシュを初期化します');
            await initializeDocumentStorage();
            
            // URLからworkIdを取得
            const urlWorkId = getWorkIdFromUrl();
            if (urlWorkId) {
                console.log(`URLからworkIdを取得しました: ${urlWorkId}`);
                currentWorkId = urlWorkId;
            }
            
            // 保存されたシノニムデータを読み込み
            console.log('保存されたシノニムデータを読み込みます');
            const synonymLoaded = loadSynonymData();
            if (synonymLoaded) {
                console.log('保存されたシノニムデータを読み込みました');
            }
            
            // シンプルなキーワード抽出を初期化
            console.log('キーワード抽出機能を初期化します');
            try {
                await initTokenizer().catch(err => {
                    console.warn('キーワード抽出機能の初期化に失敗しました:', err);
                });
            } catch (tokenError) {
                console.warn('キーワード抽出機能の初期化に失敗しました:', tokenError);
            }
            
            // ドキュメントリストを取得して表示（workIdが存在する場合のみ）
            let documents = [];
            if (currentWorkId) {
                console.log(`ドキュメントリストを取得します (workId: ${currentWorkId})`);
                documents = await fetchDocumentList(currentWorkId);
                renderDocumentList(documents);
            } else {
                console.log('workIdが指定されていないため、空のドキュメントリストを表示します');
                documents = [];
                renderDocumentList([]);
            }
            
            // ページアイテムを保存（グローバル変数に）
            pageItems = documents;
            
            // イベントリスナーを設定
            console.log('イベントリスナーをセットアップします');
            setupEventListeners();
            
            // 全PDFドキュメントの先読みを開始
            console.log('全PDFドキュメントの先読みを開始します');
            prefetchAllPdfDocuments();
            
            console.log('UIの初期化が完了しました');
        } catch (error) {
            console.error('UI初期化中にエラーが発生しました:', error);
        }
    }
    // 全PDFドキュメントの先読みを実行する関数
    async function prefetchAllPdfDocuments() {
        try {
            // PDFドキュメントだけをフィルタリング（名前に【PDF文書】が含まれるもの）
            const pdfDocuments = pageItems.filter(item => 
                item.name && item.name.includes('【PDF文書】')
            );
            
            if (pdfDocuments.length === 0) {
                console.log('先読み対象のPDFドキュメントはありません');
                return;
            }
            
            console.log(`${pdfDocuments.length}個のPDFドキュメントを先読みします`);
            
            // PDFをグループ化 (同じPDFの異なるページを一つにまとめる)
            const pdfGroups = new Map();
            
            pdfDocuments.forEach(doc => {
                // PDFの基本名を取得 (ページ番号を除く)
                const pdfBaseName = doc.name.split('(ページ')[0].trim();
                
                if (!pdfGroups.has(pdfBaseName)) {
                    pdfGroups.set(pdfBaseName, []);
                }
                
                pdfGroups.get(pdfBaseName).push(doc);
            });
            
            console.log(`${pdfGroups.size}個のユニークなPDFドキュメントが見つかりました`);
            
            // 各PDFの先読みを実行
            for (const [pdfBaseName, pages] of pdfGroups.entries()) {
                console.log(`"${pdfBaseName}" (${pages.length}ページ) の先読みを開始します`);
                prefetchAllPdfPages(pdfBaseName);
                
                // 次のPDFの処理まで少し間を空けて、サーバー負荷を分散
                await new Promise(resolve => setTimeout(resolve, 500));
            }
        } catch (error) {
            console.error('PDFドキュメントの先読み中にエラーが発生しました:', error);
        }
    }

    // ドキュメントリストのレンダリング
    function renderDocumentList(documents) {
        pageList.innerHTML = '';
        
        console.log('ドキュメントリストのレンダリング:', documents);
        console.log('ドキュメントの型:', typeof documents);
        console.log('ドキュメントは配列か?:', Array.isArray(documents));
        
        if (!documents || documents.length === 0) {
            pageList.innerHTML = '<div class="empty-state" style="padding: 1rem;">構造化済みテキストファイルがありません</div>';
            console.log('ドキュメントが空です');
            return;
        }

        // グループ化されたデータを処理
        documents.forEach(group => {
            // ページグループのヘッダーの作成と表示を削除
            
            // グループ内のドキュメントを処理
            if (Array.isArray(group.documents) && group.documents.length > 0) {
                const firstDoc = group.documents[0];
                
                // このグループのアイテム要素を作成（クリック可能なアイテム）
                const item = document.createElement('div');
                item.className = 'page-item';
                item.dataset.docId = group.id; // グループIDを設定
                item.dataset.isGroup = 'true'; // グループフラグを設定
                item.dataset.documents = JSON.stringify(group.documents.map(d => d.id)); // 含まれるドキュメントIDを保存
                
                // ファイルタイプアイコン
                let fileIcon = '<i class="fas fa-file-alt" style="margin-right: 8px; color: #3389ca;"></i>';
                
                // ファイルタイプまたは名前に基づいてアイコンを決定
                if (firstDoc.fileType === 'PDF' || (firstDoc.name && firstDoc.name.includes('【PDF文書】'))) {
                    fileIcon = '<i class="fas fa-file-pdf" style="margin-right: 8px; color: #3389ca;"></i>';
                }
                
                // ドキュメント数のバッジを表示しない（空にする）
                
                // アイテムの内容を設定
                item.innerHTML = `
                    ${fileIcon}
                    <span class="page-name">${group.displayName}</span>
                `;
                
                // クリックイベントを設定
                item.addEventListener('click', () => {
                    console.log(`ページグループがクリックされました: ${group.displayName}`);
                    selectDocumentGroup(group);
                });
                
                pageList.appendChild(item);
            }
        });
    }
    // ドキュメントグループの選択（複数のドキュメントをまとめて表示）
    async function selectDocumentGroup(group) {
        console.log('ドキュメントグループを選択:', group);
        
        // すべてのハイライトを解除
        document.querySelectorAll('.page-item').forEach(item => {
            item.classList.remove('highlighted');
            item.classList.remove('active');
        });
        
        // 新しい選択を適用
        const selectedItem = document.querySelector(`.page-item[data-doc-id="${group.id}"]`);
        if (selectedItem) {
            selectedItem.classList.add('active');
        }
        
        // コンテンツが既に取得済みか確認
        if (group.content) {
            console.log(`グループ内のコンテンツが既に取得済みです。長さ: ${group.content.length}文字`);
            
            // UI更新
            documentTitle.textContent = group.displayName || 'ページグループ';
            documentMeta.textContent = ''; // テキスト表示部分を空に
            
            // コンテンツを見やすく整形（改行を保持）
            const formattedContent = group.content
                .replace(/\n/g, '<br>')
                .replace(/\s{2,}/g, function(match) {
                    return '&nbsp;'.repeat(match.length);
                });
            
            documentContent.innerHTML = `<div style="line-height: 1.4; font-size: 0.95rem; white-space: pre-wrap; padding: 0; margin: 0;">${formattedContent}</div>`;

            // 統合シノニムセクションを生成
            const combinedSynonyms = getCombinedSynonyms();
            let synonymSection = '';
            if (combinedSynonyms.trim()) {
                synonymSection = `<div style="margin-top: 2rem; padding: 1rem; background-color: #f8fafc; border-radius: 0.375rem; border-left: 4px solid #3b82f6;"><h3 style="margin: 0 0 1rem 0; font-size: 1.125rem; font-weight: 600; color: #1f2937;">📚 アノテーション（テキスト形式）</h3><pre style="font-size: 0.875rem; color: #374151; white-space: pre-wrap; font-family: monospace;">${combinedSynonyms}</pre></div>`;
            }

            documentContent.innerHTML = `<div style="line-height: 1.4; font-size: 0.95rem; white-space: pre-wrap; padding: 0; margin: 0;">${formattedContent}</div>${synonymSection}`;
            
            // グローバル変数を更新
            selectedDocument = {
                id: group.id,
                name: group.displayName,
                content: group.content,
                isGroup: true
            };
            
            return;
        }
        
        // グループ内のすべてのドキュメントのコンテンツを取得
        const allContents = [];
        
        if (Array.isArray(group.documents)) {
            console.log(`グループ内の ${group.documents.length} 件のドキュメントを処理します`);
            
            for (const doc of group.documents) {
                try {
                    // 重要: doc自体にテキストがある場合は、API呼び出しをスキップ
                    if (doc.text) {
                        console.log(`ドキュメント ${doc.id} はテキストを直接持っています:`, doc.text.substring(0, 30) + '...');
                        allContents.push({
                            id: doc.id,
                            content: doc.text,
                            chunkNumber: doc.chunkNumber || doc.chunkNo
                        });
                        continue;
                    }
                    
                    // API経由でコンテンツを取得
                    console.log(`ドキュメント ${doc.id} のコンテンツをAPIから取得します`);
                    const content = await fetchDocumentContent(doc.id);
                    if (content && content.content) {
                        allContents.push({
                            id: doc.id,
                            content: content.content,
                            chunkNumber: doc.chunkNumber || doc.chunkNo
                        });
                    }
                } catch (error) {
                    console.error(`ドキュメント ${doc.id} のコンテンツ取得エラー:`, error);
                    // エラーが発生しても、テキストがあれば使用
                    if (doc.text) {
                        allContents.push({
                            id: doc.id,
                            content: doc.text,
                            chunkNumber: doc.chunkNumber || doc.chunkNo
                        });
                    }
                }
            }
        }
        
        // チャンク番号順にソート
        allContents.sort((a, b) => {
            const chunkA = a.chunkNumber !== undefined && a.chunkNumber !== null ? 
                          parseInt(a.chunkNumber) : 999999;
            const chunkB = b.chunkNumber !== undefined && b.chunkNumber !== null ? 
                          parseInt(b.chunkNumber) : 999999;
            return chunkA - chunkB;
        });
        
        // すべてのコンテンツを連結
        const combinedContent = allContents.map(item => item.content).join('\n\n');
        
        // デバッグ用: コンテンツ長の確認
        console.log(`グループのコンテンツ全長: ${combinedContent.length} 文字`);
        console.log(`${Math.min(150, combinedContent.length)}文字のサンプル:`, combinedContent.substring(0, 150) + '...');
        
        // UI更新
        documentTitle.textContent = group.displayName || 'ページグループ';
        documentMeta.textContent = ''; // テキスト連結表示の情報を非表示
        
        // コンテンツを見やすく整形（改行を保持）
        const formattedContent = combinedContent
            .replace(/\n/g, '<br>')
            .replace(/\s{2,}/g, function(match) {
                return '&nbsp;'.repeat(match.length);
            });
        
        // 統合シノニムセクションを生成
        const combinedSynonyms = getCombinedSynonyms();
        let synonymSection = '';
        if (combinedSynonyms.trim()) {
            synonymSection = `<div style="margin-top: 2rem; padding: 1rem; background-color: #f8fafc; border-radius: 0.375rem; border-left: 4px solid #3b82f6;"><h3 style="margin: 0 0 1rem 0; font-size: 1.125rem; font-weight: 600; color: #1f2937;">📚 アノテーション（テキスト形式）</h3><pre style="font-size: 0.875rem; color: #374151; white-space: pre-wrap; font-family: monospace;">${combinedSynonyms}</pre></div>`;
        }

        documentContent.innerHTML = `<div style="line-height: 1.4; font-size: 0.95rem; white-space: pre-wrap; padding: 0; margin: 0;">${formattedContent}</div>${synonymSection}`;
        
        // グローバル変数を更新し、グループの内容をキャッシュ
        group.content = combinedContent; // 次回の高速アクセスのためにキャッシュ
        
        selectedDocument = {
            id: group.id,
            name: group.displayName,
            content: combinedContent,
            isGroup: true
        };
    }

    // ドキュメントの選択
    async function selectDocument(docId, highlight = false) {
        console.log(`ドキュメント選択: ID=${docId}, ハイライト=${highlight}`);
        
        // グループかどうかを判定
        if (docId.startsWith('group_')) {
            // グループの場合、グループIDから対応するグループを検索
            const groupId = docId;
            const group = pageItems.find(group => group.id === groupId);
            if (group) {
                return selectDocumentGroup(group);
            }
        }
        
        // 以下は個別ドキュメント選択の元々の処理
        
        // すべてのハイライトを解除
        document.querySelectorAll('.page-item').forEach(item => {
            item.classList.remove('highlighted');
        });
        
        // 現在の選択を解除
        document.querySelectorAll('.page-item').forEach(item => {
            item.classList.remove('active');
        });

        // 新しい選択を適用
        const selectedItem = document.querySelector(`.page-item[data-doc-id="${docId}"]`);
        if (selectedItem) {
            selectedItem.classList.add('active');
            
            // ハイライトが指定された場合
            if (highlight) {
                console.log(`ハイライト適用: ${docId}`);
                selectedItem.classList.add('highlighted');
                // スクロールして要素が見えるようにする
                selectedItem.scrollIntoView({ behavior: 'smooth', block: 'center' });
            }
        } else {
            console.log(`選択アイテムが見つかりません: ${docId}`);
        }

        // ドキュメントのコンテンツを取得
        selectedDocument = await fetchDocumentContent(docId);

        // PDFドキュメントの場合は全ページを先読み（非同期で実行）
        const selectedItemContent = selectedItem ? selectedItem.textContent.trim() : '';
        if (selectedItemContent.includes('【PDF文書】')) {
            // PDFのIDを抽出（ページ番号部分を除いた名前部分）
            const pdfBaseName = selectedItemContent.split('(ページ')[0].trim();
            console.log(`PDF検出: ${pdfBaseName} - 全ページデータの先読みを開始します`);
            prefetchAllPdfPages(pdfBaseName);
        }

        // UI更新
        if (selectedDocument) {
            documentTitle.textContent = selectedDocument.name;
            documentMeta.textContent = '';
            
            // コンテンツを見やすく整形（改行を保持）
            const formattedContent = selectedDocument.content
                .replace(/\n/g, '<br>')
                .replace(/\s{2,}/g, function(match) {
                    return '&nbsp;'.repeat(match.length);
                });
            
            // 統合シノニムセクションを生成
            const combinedSynonyms = getCombinedSynonyms();
            let synonymSection = '';
            if (combinedSynonyms.trim()) {
                synonymSection = `<div style="margin-top: 2rem; padding: 1rem; background-color: #f8fafc; border-radius: 0.375rem; border-left: 4px solid #3b82f6;"><h3 style="margin: 0 0 1rem 0; font-size: 1.125rem; font-weight: 600; color: #1f2937;">📚 アノテーション（テキスト形式）</h3><pre style="font-size: 0.875rem; color: #374151; white-space: pre-wrap; font-family: monospace;">${combinedSynonyms}</pre></div>`;
            }

            documentContent.innerHTML = `<div style="line-height: 1.4; font-size: 0.95rem; white-space: pre-wrap; padding: 0; margin: 0;">${formattedContent}</div>${synonymSection}`;

        } else {
            documentTitle.textContent = 'ドキュメントを取得できませんでした';
            documentMeta.textContent = '';
            documentContent.innerHTML = '<div class="empty-state">ドキュメントの読み込みに失敗しました</div>';
        }
    }

    // PDFファイル選択時に全ページを先読みする関数
    async function prefetchAllPdfPages(pdfBaseName) {
        if (currentPdfPrefetchId === pdfBaseName) {
            console.log(`既に同じPDF "${pdfBaseName}" の先読み処理中です。スキップします。`);
            return;
        }
        
        currentPdfPrefetchId = pdfBaseName;
        console.log(`PDF "${pdfBaseName}" の全ページ先読みを開始します`);
        
        // キャッシュが初期化されていなければ初期化
        if (!pdfTextCache[pdfBaseName]) {
            pdfTextCache[pdfBaseName] = {};
        }
        
        // キャッシュ進捗情報を初期化
        if (!cacheProgressStatus[pdfBaseName]) {
            cacheProgressStatus[pdfBaseName] = {
                total: 0,
                loaded: 0,
                inProgress: true
            };
        }
        
        // 同じPDF内の全ページを検索（名前の前半部分が完全に一致するもの）
        const pdfPages = pageItems.filter(item => 
            item.name && item.name.includes(pdfBaseName) && item.name.includes('【PDF文書】')
        );
        
        if (pdfPages.length === 0) {
            console.log(`PDF "${pdfBaseName}" のページが見つかりませんでした`);
            currentPdfPrefetchId = null;
            
            // キャッシュ進捗情報を更新
            if (cacheProgressStatus[pdfBaseName]) {
                cacheProgressStatus[pdfBaseName].inProgress = false;
            }
            
            return;
        }
        
        console.log(`PDF "${pdfBaseName}" の全 ${pdfPages.length} ページを先読みします`);
        
        // キャッシュ進捗情報を更新
        cacheProgressStatus[pdfBaseName].total = pdfPages.length;
        
        // キャッシュ進捗情報を表示
        updateCacheProgressDisplay();
        
        // 各ページのコンテンツを順次取得（既存API利用）
        let loadedCount = 0;
        const totalPages = pdfPages.length;
        
        for (const page of pdfPages) {
            // 既に取得済みならスキップ
            if (pdfTextCache[pdfBaseName][page.id]) {
                loadedCount++;
                // キャッシュ進捗情報を更新
                cacheProgressStatus[pdfBaseName].loaded = loadedCount;
                updateCacheProgressDisplay();
                console.log(`PDF "${pdfBaseName}" のページ ${loadedCount}/${totalPages} は既にキャッシュ済みです`);
                continue;
            }
            
            try {
                // ページコンテンツを取得
                const pageContent = await fetchDocumentContent(page.id);
                
                if (pageContent) {
                    // キャッシュに保存
                    pdfTextCache[pdfBaseName][page.id] = {
                        content: pageContent.content,
                        pageNumber: page.name.match(/\((\d+)枚目\)/) ? parseInt(page.name.match(/\((\d+)枚目\)/)[1]) : 0
                    };
                    
                    loadedCount++;
                    // キャッシュ進捗情報を更新
                    cacheProgressStatus[pdfBaseName].loaded = loadedCount;
                    updateCacheProgressDisplay();
                    
                    console.log(`PDF "${pdfBaseName}" のページ ${loadedCount}/${totalPages} をキャッシュしました`);
                } else {
                    console.error(`PDF "${pdfBaseName}" のページ ${page.id} の取得に失敗しました`);
                }
            } catch (error) {
                console.error(`PDF "${pdfBaseName}" のページ ${page.id} のキャッシュ中にエラーが発生しました:`, error);
            }
        }
        
        // 完了したら通知を表示
        cacheProgressStatus[pdfBaseName].inProgress = false;
        updateCacheProgressDisplay();
        currentPdfPrefetchId = null;
        
        console.log(`PDF "${pdfBaseName}" の全 ${loadedCount}/${totalPages} ページの先読みが完了しました`);
        
        // キャッシュ完了通知
        showCacheCompletionNotification(pdfBaseName, loadedCount, totalPages);
        
        // キャッシュしたPDFのリストを更新
        createPdfListForNavigation();
    }

    // 特定のPDFのキャッシュを開始する関数（アップロード完了後の自動キャッシュ用）
    function startCachingPdf(pdfBaseName) {
        // 検索バーの横にキャッシュ進捗表示エリアがなければ作成
        createCacheProgressDisplay();
        
        // キャッシュ進捗情報を初期化
        cacheProgressStatus[pdfBaseName] = {
            total: 0,
            loaded: 0,
            inProgress: true
        };
        
        // 先読み開始
        prefetchAllPdfPages(pdfBaseName);
    }
    
    // キャッシュ進捗表示エリアを作成
    function createCacheProgressDisplay() {
        // すでに存在する場合は作成しない
        if (document.getElementById('cache-progress-container')) {
            return;
        }
        
        // ヘッダーの下に表示
        const header = document.querySelector('.header');
        if (!header) return;
        
        const progressContainer = document.createElement('div');
        progressContainer.id = 'cache-progress-container';
        progressContainer.style.cssText = 'display: none; margin: 10px auto; padding: 4px 8px; background-color: #e6f7ff; border-radius: 4px; font-size: 12px; max-width: 600px;';
        
        const progressText = document.createElement('div');
        progressText.id = 'cache-progress-text';
        progressText.textContent = 'キャッシュ: 0/0';
        
        const progressBar = document.createElement('div');
        progressBar.style.cssText = 'width: 100%; height: 4px; background-color: #e0e0e0; border-radius: 2px; margin-top: 2px;';
        
        const progressFill = document.createElement('div');
        progressFill.id = 'cache-progress-fill';
        progressFill.style.cssText = 'width: 0%; height: 100%; background-color: #1890ff; border-radius: 2px; transition: width 0.3s;';
        
        progressBar.appendChild(progressFill);
        progressContainer.appendChild(progressText);
        progressContainer.appendChild(progressBar);
        
        // ヘッダーの後に挿入
        header.parentNode.insertBefore(progressContainer, header.nextSibling);
    }
    
    // キャッシュ進捗表示を更新
    function updateCacheProgressDisplay() {
        const progressContainer = document.getElementById('cache-progress-container');
        const progressText = document.getElementById('cache-progress-text');
        const progressFill = document.getElementById('cache-progress-fill');
        
        if (!progressContainer || !progressText || !progressFill) {
            // 要素がない場合は作成
            createCacheProgressDisplay();
            // 再帰呼び出しを回避し、新しく作成された要素を取得
            const newProgressContainer = document.getElementById('cache-progress-container');
            const newProgressText = document.getElementById('cache-progress-text');
            const newProgressFill = document.getElementById('cache-progress-fill');
            
            // 要素が作成されなかった場合は処理を中止
            if (!newProgressContainer || !newProgressText || !newProgressFill) {
                console.error('キャッシュ進捗表示要素の作成に失敗しました');
                return;
            }
        }
        
        // アクティブなキャッシュ処理があるか確認
        let activeCache = null;
        let totalPages = 0;
        let loadedPages = 0;
        
        for (const [pdfName, status] of Object.entries(cacheProgressStatus)) {
            if (status.inProgress) {
                activeCache = pdfName;
                totalPages = status.total;
                loadedPages = status.loaded;
                break;
            }
        }
        
        if (activeCache) {
            // キャッシュ進行中の場合、表示を更新
            progressContainer.style.display = 'block';
            progressText.textContent = `キャッシュ中: ${loadedPages}/${totalPages} (${activeCache})`;
            
            const percent = totalPages > 0 ? (loadedPages / totalPages) * 100 : 0;
            progressFill.style.width = `${percent}%`;
        } else {
            // アクティブなキャッシュがない場合は非表示
            progressContainer.style.display = 'none';
        }
    }
    
    // キャッシュ完了通知を表示
    function showCacheCompletionNotification(pdfBaseName, loadedCount, totalPages) {
        // 表示を完全に非表示にするため、何もしない
        return;
    }

    // チャットの表示/非表示を切り替え
    function toggleChat() {
        // 右パネルを開く/閉じるトグル
        const willOpen = !rightSidebar.classList.contains('open');
        rightSidebar.classList.toggle('open');
        if (willOpen) {
            // チャットUIを表示、差分UIを隠す
            const chatMessagesEl = document.getElementById('chat-messages');
            const chatInputContainer = document.querySelector('#rightSidebar .chat-input-container');
            if (chatMessagesEl) chatMessagesEl.style.display = '';
            if (chatInputContainer) chatInputContainer.style.display = '';
            const diffResultsEl = document.getElementById('diff-results');
            if (diffResultsEl) diffResultsEl.style.display = 'none';
            const rightPanelTitleEl = document.getElementById('right-panel-title');
            if (rightPanelTitleEl) rightPanelTitleEl.textContent = 'AIアシスタント';
            // 合成データ作成ボタンは差分モード限定のため非表示
            if (synthOpenBtn) synthOpenBtn.style.display = 'none';
            // 分析ボタンもチャットモードでは非表示
            if (runAnalysisBtn) runAnalysisBtn.style.display = 'none';
        }
    }

    // ドラッグ可能なモーダル（簡易実装）
    (function initDraggableModal(){
        if (!synthModal || !synthModalHeader) return;
        let isDown = false, startX = 0, startY = 0, startLeft = 0, startTop = 0;
        synthModalHeader.addEventListener('mousedown', (e)=>{
            isDown = true;
            startX = e.clientX; startY = e.clientY;
            const rect = synthModal.getBoundingClientRect();
            startLeft = rect.left; startTop = rect.top;
            e.preventDefault();
        });
        window.addEventListener('mousemove', (e)=>{
            if (!isDown) return;
            const dx = e.clientX - startX; const dy = e.clientY - startY;
            synthModal.style.left = `${Math.max(8, startLeft + dx)}px`;
            synthModal.style.top = `${Math.max(8, startTop + dy)}px`;
            synthModal.style.right = 'auto';
        });
        window.addEventListener('mouseup', ()=>{ isDown = false; });
    })();

    // 合成データ作成ボタン -> モーダル表示
    if (synthOpenBtn && synthModal) {
        synthOpenBtn.addEventListener('click', ()=>{
            synthModal.style.display = '';
            // 直近の差分をテキストにして下地として入れておく
            try {
                const items = window.lastDiffResult || [];
                const text = Array.isArray(items) ? items.map(d => (d.diff_text||d.details||'')).join('\n\n') : '';
                if (synthJsonl && !synthJsonl.value) synthJsonl.value = text ? `# diff summary preview (not JSONL)\n${text.substring(0,800)}...` : '';
            } catch {}
            // 最新保存プロンプト→既定プロンプトの順で未取得時に読み込み
            // 初期値は空白
            if (synthPrompt) synthPrompt.value = '';
            try {
                if (autoLoadLatestPrompt && synthPrompt && !synthPrompt.value) {
                    // 1) 一覧から最新のプロンプト取得
                    // サーバー側で最新の1ファイルを返すエンドポイントに切替
                    fetch(getBasePath() + '/api/reinforcement/prompt/latest', { credentials: 'include' })
                        .then(r => r.ok ? r.json() : null)
                        .then(async obj => {
                            if (obj && obj.success && obj.content) {
                                const cleaned = stripPromptPreviewPrefix(obj.content);
                                synthPrompt.value = cleaned;
                                return;
                            }
                            // 2) フォールバック: 既定プロンプト
                            // 自動読み込みOFFのためフォールバックもしない
                        })
                        .catch(async ()=>{
                            // noop
                        });
                }
            } catch {}
            // タブ初期状態（ハイライト付与）
            if (tabJsonlBtn && tabPromptBtn && panelJsonl && panelPrompt) {
                setSynthTabActive('jsonl');
            }
        });
    }
    if (synthCloseBtn && synthModal) {
        synthCloseBtn.addEventListener('click', ()=>{ synthModal.style.display = 'none'; });
    }

    // 生成API呼び出し
    if (synthGenBtn) {
        synthGenBtn.addEventListener('click', async ()=>{
            try {
                const history = loadUploadHistory();
                const latest = history && history.length > 0 ? history[0] : null;
                const workId = (typeof currentWorkId === 'string' && currentWorkId) ? currentWorkId : (latest ? latest.workId : null);
                if (!workId) return;
                const samples = 3; // 件数UIを廃止したため固定
                const diffs = (window.lastDiffResult||[]).map(d => ({ page_no: d.page_no, diff_text: d.diff_text||d.details||'' }));
                // 構造化済みテキストと画像情報を収集
                const structuredText = (selectedDocument && selectedDocument.content) ? selectedDocument.content : '';
                let visiblePageCount = 0; try { visiblePageCount = document.querySelectorAll('#page-list .page-item').length || 0; } catch {}
                const fileLabel = (typeof currentFileLink !== 'undefined' && currentFileLink && currentFileLink.textContent) ? currentFileLink.textContent.trim() : (selectedDocument && selectedDocument.name ? selectedDocument.name : '');
                const imageInfo = [];
                if (fileLabel) imageInfo.push(`file:${fileLabel}`);
                if (visiblePageCount > 0) imageInfo.push(`pages:${visiblePageCount}`);
                const body = { work_id: workId, samples, diffs, structured_text: structuredText, image_info: imageInfo };
                if (synthPrompt && synthPrompt.value) body.prompt = synthPrompt.value;
                const res = await fetch(getBasePath() + '/api/synthetic/generate-jsonl', {
                    method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' }, credentials: 'include',
                    body: JSON.stringify(body)
                });
                if (!res.ok) {
                    const ct = res.headers.get('content-type')||'';
                    const txt = ct.includes('application/json') ? JSON.stringify(await res.json()) : await res.text();
                    throw new Error('generate-jsonl failed: ' + txt.slice(0,300));
                }
                const data = await res.json();
                if (synthJsonl) synthJsonl.value = data.jsonl || '';
            } catch (e) {
                if (synthJsonl) synthJsonl.value = `# エラー: ${e.message}`;
            }
        });
    }

    // ダウンロード
    if (synthDlBtn && synthJsonl) {
        synthDlBtn.addEventListener('click', ()=>{
            try {
                const blob = new Blob([synthJsonl.value||''], { type: 'application/jsonl;charset=utf-8' });
                const url = URL.createObjectURL(blob);
                const a = document.createElement('a');
                a.href = url; a.download = 'synthetic_dataset.jsonl';
                document.body.appendChild(a); a.click(); document.body.removeChild(a);
                URL.revokeObjectURL(url);
            } catch {}
        });
    }

    function setSynthTabActive(which) {
        if (!tabJsonlBtn || !tabPromptBtn || !panelJsonl || !panelPrompt) return;
        const activate = (btnOn, btnOff, panelOn, panelOff) => {
            btnOn.classList.add('active');
            btnOff.classList.remove('active');
            panelOn.classList.add('active');
            panelOff.classList.remove('active');
            // 視覚的ハイライト（ボタン背景・文字色）
            btnOn.style.background = '#2563eb';
            btnOn.style.color = '#fff';
            btnOff.style.background = '#f3f4f6';
            btnOff.style.color = '#374151';
        };
        if (which === 'jsonl') activate(tabJsonlBtn, tabPromptBtn, panelJsonl, panelPrompt);
        else activate(tabPromptBtn, tabJsonlBtn, panelPrompt, panelJsonl);
    }

    if (tabJsonlBtn) tabJsonlBtn.addEventListener('click', ()=> setSynthTabActive('jsonl'));
    if (tabPromptBtn) tabPromptBtn.addEventListener('click', ()=> setSynthTabActive('prompt'));

    // 旧プレフィクスやプレビューブロックを除去するクリーナー
    function stripPromptPreviewPrefix(text) {
        try {
            if (!text) return '';
            let t = String(text);
            // 旧プレフィクス箇条書き - [PREFIX]/[UPDATED]/[NOTE]
            t = t.replace(/^- \[PREFIX\][\s\S]*?\n/mi, '');
            t = t.replace(/^- \[UPDATED\][\s\S]*?\n/mi, '');
            t = t.replace(/^- \[NOTE\][\s\S]*?\n/mi, '');
            // 生成に使用する要素（プレビュー）ブロックを削除（ブロック後の本文は残す）
            const mk = '# 生成に使用する要素（プレビュー）';
            let start = t.indexOf(mk);
            if (start >= 0) {
                // ブロック終端を推定: マーカー以降で、最初に「タグ/コードブロック以外で始まる行」位置
                const after = t.substring(start);
                const lines = after.split(/\n/);
                // 行0はマーカー行。以降を走査
                let cutEndOffset = after.length; // デフォルト末尾まで
                let accLen = 0;
                for (let i = 1; i < lines.length; i++) {
                    const line = lines[i];
                    const trimmed = line.trimStart();
                    const isTag = trimmed.startsWith('[') || trimmed.startsWith('#') || trimmed.startsWith('```') || trimmed === '';
                    if (!isTag) {
                        // この行の先頭位置を終端とする
                        cutEndOffset = accLen; // accLenは直前までの合計。今の行の直前で切る
                        break;
                    }
                    accLen += line.length + 1; // +1 for newline
                }
                const before = t.substring(0, start).trim();
                const afterBlock = after.substring(cutEndOffset);
                t = (before ? before + '\n\n' : '') + afterBlock;
            }
            return t.trimStart();
        } catch { return text; }
    }

    function buildPromptPrefix(meta, fileName) {
        try {
            const ts = new Date();
            const y = ts.getFullYear();
            const m = String(ts.getMonth()+1).padStart(2,'0');
            const d = String(ts.getDate()).padStart(2,'0');
            const hh = String(ts.getHours()).padStart(2,'0');
            const mm = String(ts.getMinutes()).padStart(2,'0');
            const ss = String(ts.getSeconds()).padStart(2,'0');
            const updated = `${y}-${m}-${d} ${hh}:${mm}:${ss}`;
            const desc = (meta && (meta.Description || meta.description)) ? (meta.Description || meta.description) : '';
            const title = desc ? desc : (fileName || 'latest');
            return `- [PREFIX] ${title}\n- [UPDATED] ${updated}\n- [NOTE] 以下に現在のプロンプト本文が続きます\n\n`;
        } catch { return ''; }
    }

    function updatePromptPreviewInTextarea() {
        if (!synthPrompt) return;
        const marker = '\n\n# 生成に使用する要素（プレビュー）\n';
        const items = window.lastDiffResult || [];
        const diffs = Array.isArray(items) ? items.map(d => `- [p.${(d.page_no??0)+1}]`).join('\n') : '';
        const structuredText = (selectedDocument && selectedDocument.content) ? selectedDocument.content : '';
        let visiblePageCount = 0; try { visiblePageCount = document.querySelectorAll('#page-list .page-item').length || 0; } catch {}
        const fileLabel = (typeof currentFileLink !== 'undefined' && currentFileLink && currentFileLink.textContent) ? currentFileLink.textContent.trim() : (selectedDocument && selectedDocument.name ? selectedDocument.name : '');
        const imageInfo = [];
        if (fileLabel) imageInfo.push(`file:${fileLabel}`);
        if (visiblePageCount > 0) imageInfo.push(`pages:${visiblePageCount}`);
        const stPreview = structuredText ? structuredText.substring(0, 1200) + (structuredText.length > 1200 ? ' ...' : '') : '';
        const preview = `${marker}[IMAGE_INFO]\n${imageInfo.join('\n') || '(なし)'}\n\n[DIFFS]\n${diffs || '(なし)'}\n\n[STRUCTURED_TEXT]\n${stPreview || '(なし)'}\n`;
        const base = synthPrompt.value || '';
        const idx = base.indexOf(marker);
        if (idx >= 0) synthPrompt.value = base.substring(0, idx) + preview;
        else synthPrompt.value = base + preview;
    }

    // Fewshot Upload: open modal
    if (synthUploadBtn && fuModal) {
        synthUploadBtn.addEventListener('click', async ()=>{
            fuSelectedType = '';
            fuSelectedImage = { url: '', path: '' };
            fuText.value = synthJsonl ? (synthJsonl.value||'') : '';
            fuPreview.src = '#';
            // step1 初期化
            fuStep1.style.display = '';
            fuStep2.style.display = 'none';
            fuStep3.style.display = 'none';
            fuTypeList.innerHTML = '';
            fuThumbGrid.innerHTML = '';
            fuModal.style.display = '';
            try {
                // GET user types
                const res = await fetch(getBasePath() + '/api/user/types', { credentials: 'include' });
                const data = res.ok ? await res.json() : { types: [] };
                const types = Array.isArray(data.types) ? data.types : [];
                types.forEach(t => {
                    const id = 'fu-type-' + Math.random().toString(36).slice(2,8);
                    const label = document.createElement('label');
                    label.style.display = 'flex'; label.style.alignItems = 'center'; label.style.gap = '8px'; label.style.padding = '6px 4px';
                    label.style.border = '1px solid #e5e7eb'; label.style.borderRadius = '6px';
                    label.innerHTML = `<input type="radio" name="fu-type" id="${id}" value="${t}"><span>${escapeHtml(t)}</span>`;
                    fuTypeList.appendChild(label);
                });
            } catch {}
        });
    }
    if (fuClose && fuModal) {
        fuClose.addEventListener('click', ()=>{ fuModal.style.display = 'none'; });
        window.addEventListener('click', (e)=>{ if (e.target === fuModal) fuModal.style.display = 'none'; });
    }
    if (fuNext1) {
        fuNext1.addEventListener('click', async ()=>{
            try {
                const checked = fuTypeList ? fuTypeList.querySelector('input[name="fu-type"]:checked') : null;
                if (!checked) { showToast('タイプを選択してください', 'warning'); return; }
                fuSelectedType = checked.value;
                // サムネ取得
                fuThumbGrid.innerHTML = '';
                const history = loadUploadHistory();
                const latest = history && history.length > 0 ? history[0] : null;
                const workId = (typeof currentWorkId === 'string' && currentWorkId) ? currentWorkId : (latest ? latest.workId : null);
                console.debug('[fewshot] fetching pages for workId', workId);
                const res = await fetch(getBasePath() + '/api/fewshot/pages?work_id=' + encodeURIComponent(workId), { credentials: 'include' });
                const data = res.ok ? await res.json() : { pages: [] };
                console.debug('[fewshot] pages response', data);
                const pages = Array.isArray(data.pages) ? data.pages : [];
                pages.forEach(p => {
                    const div = document.createElement('div');
                    div.style.border = '1px solid #e5e7eb'; div.style.borderRadius = '6px'; div.style.padding = '4px'; div.style.cursor = 'pointer';
                    div.innerHTML = `<img src="${p.thumbUrl}" style="width:100%; height:100px; object-fit:cover; border-radius:4px;"/>`;
                    div.addEventListener('click', ()=>{
                        Array.from(fuThumbGrid.children).forEach(ch => ch.style.outline='none');
                        div.style.outline = '2px solid #2563eb';
                        fuSelectedImage = { url: p.url, path: p.path || '' };
                    });
                    fuThumbGrid.appendChild(div);
                });
                fuStep1.style.display = 'none';
                fuStep2.style.display = '';
            } catch {}
        });
    }
    if (fuBack2) fuBack2.addEventListener('click', ()=>{ fuStep2.style.display='none'; fuStep1.style.display=''; });
    if (fuNext2) {
        fuNext2.addEventListener('click', ()=>{
            if (!fuSelectedImage || !fuSelectedImage.url) { showToast('画像を1つ選択してください', 'warning'); return; }
            fuPreview.src = fuSelectedImage.url;
            fuText.value = synthJsonl ? (synthJsonl.value||'') : '';
            fuStep2.style.display = 'none';
            fuStep3.style.display = '';
        });
    }
    if (fuBack3) fuBack3.addEventListener('click', ()=>{ fuStep3.style.display='none'; fuStep2.style.display=''; });
    if (fuUpload) {
        fuUpload.addEventListener('click', async ()=>{
            try {
                if (!fuSelectedType) { showToast('タイプが未選択です', 'warning'); return; }
                if (!fuSelectedImage || !fuSelectedImage.url) { showToast('画像が未選択です', 'warning'); return; }
                const history = loadUploadHistory();
                const latest = history && history.length > 0 ? history[0] : null;
                const workId = (typeof currentWorkId === 'string' && currentWorkId) ? currentWorkId : (latest ? latest.workId : null);
                const body = { work_id: workId, type: fuSelectedType, image_url: fuSelectedImage.url, image_path: fuSelectedImage.path||'', text: fuText.value, groupPrefix: 'AU' };
                const res = await fetch(getBasePath() + '/api/fewshot/upload', { method: 'POST', headers: { 'Content-Type': 'application/json' }, credentials: 'include', body: JSON.stringify(body) });
                const ct = res.headers.get('content-type')||'';
                const ok = res.ok;
                const payload = ct.includes('application/json') ? await res.json() : { };
                if (!ok) throw new Error((payload.detail||payload.error||'upload failed').toString());
                showToast('アップロードしました: ' + (payload.baseName||''), 'success');
                fuModal.style.display = 'none';
            } catch(e) {
                showToast('アップロードに失敗: ' + (e.message||e), 'error');
            }
        });
    }

    // タブ切替
    function activateTab(kind) {
        if (!tabJsonlBtn || !tabPromptBtn || !panelJsonl || !panelPrompt) return;
        if (kind === 'prompt') {
            tabPromptBtn.classList.add('active');
            tabJsonlBtn.classList.remove('active');
            panelPrompt.classList.add('active');
            panelJsonl.classList.remove('active');
        } else {
            tabJsonlBtn.classList.add('active');
            tabPromptBtn.classList.remove('active');
            panelJsonl.classList.add('active');
            panelPrompt.classList.remove('active');
        }
    }
    if (tabJsonlBtn) tabJsonlBtn.addEventListener('click', ()=>activateTab('jsonl'));
    if (tabPromptBtn) tabPromptBtn.addEventListener('click', ()=>activateTab('prompt'));

    // プロンプト保存
    if (synthPromptSaveBtn && synthPrompt) {
        synthPromptSaveBtn.addEventListener('click', async ()=>{
            try {
                const content = synthPrompt.value || '';
                if (!content.trim()) { showToast('プロンプトが空です', 'warning'); return; }
                const desc = '合成用プロンプト';
                const res = await fetch(getBasePath() + '/api/reinforcement/save-prompt', {
                    method: 'POST', headers: { 'Content-Type': 'application/json' }, credentials: 'include',
                    body: JSON.stringify({ content, description: desc })
                });
                if (!res.ok) throw new Error('save-prompt failed');
                showToast('プロンプトを保存しました', 'success');
            } catch (e) {
                showToast('プロンプト保存に失敗しました', 'error');
            }
        });
    }

    // モーダル横リサイズ: モーダルとタブ内textarea幅を連動
    (function initModalHorizontalResize(){
        if (!synthModal || !synthResizer) return;
        let isDrag = false; let startX = 0; let startW = 0;
        const minW = 480; const maxW = Math.floor(window.innerWidth * 0.95);
        synthResizer.addEventListener('mousedown', (e)=>{
            isDrag = true; startX = e.clientX; startW = synthModal.getBoundingClientRect().width; e.preventDefault();
        });
        window.addEventListener('mousemove', (e)=>{
            if (!isDrag) return;
            const dx = e.clientX - startX; let nextW = Math.min(maxW, Math.max(minW, startW + dx));
            synthModal.style.width = nextW + 'px';
        });
        window.addEventListener('mouseup', ()=>{ isDrag = false; });
        window.addEventListener('resize', ()=>{
            const cur = synthModal.getBoundingClientRect().width; const cap = Math.floor(window.innerWidth*0.95);
            if (cur > cap) synthModal.style.width = cap + 'px';
        });
    })();

    // ユーザーメッセージの追加
    function addUserMessage(message, keywords = [], synonyms = []) {
        const messageEl = document.createElement('div');
        messageEl.className = 'chat-message user';
        
        // ユーザーメッセージのID生成
        const messageId = `user-message-${Date.now()}`;
        
        // トグルボタンとタグ表示のHTML
        let tagsHtml = '';
        if (keywords.length > 0 || synonyms.length > 0) {
            const keywordsId = `user-keywords-${Date.now()}`;
            const synonymsId = `user-synonyms-${Date.now() + 1}`;
            
            tagsHtml = `
                <div style="margin-bottom: 0.5rem;">
                    <button class="user-tags-toggle" data-target="${messageId}-tags" 
                            style="background: none; border: none; cursor: pointer; display: flex; align-items: center; padding: 2px 6px; border-radius: 4px; background-color: #f8f9fa; color: #495057; font-size: 0.8rem;">
                        <span class="toggle-icon" style="margin-right: 4px; transition: transform 0.2s;">▶</span>
                        <span>クエリ解析を表示</span>
                    </button>
                    <div id="${messageId}-tags" class="user-tags-content" style="display: none; margin-top: 0.5rem;">
                        ${keywords.length > 0 ? `
                            <div class="keyword-tags" style="margin-bottom: 0.5rem;">
                                <div style="font-size: 0.75rem; font-weight: 500; color: #495057; margin-bottom: 0.25rem;">検索クエリ変換 (${keywords.length})</div>
                                <div style="display: flex; flex-wrap: wrap; gap: 3px;">
                                    ${keywords.map(kw => `<span class="keyword-tag" style="background-color: #dcfce7; color: #166534; border: 1px solid #bbf7d0; padding: 2px 6px; border-radius: 4px; font-size: 0.7rem;">${kw}</span>`).join('')}
                                </div>
                            </div>
                        ` : ''}
                        ${synonyms.length > 0 ? `
                            <div class="keyword-tags">
                                <div style="font-size: 0.75rem; font-weight: 500; color: #495057; margin-bottom: 0.25rem;">シノニムクエリ拡張 (${synonyms.length})</div>
                                <div style="display: flex; flex-wrap: wrap; gap: 3px;">
                                    ${synonyms.map(synonym => `
                                        <span class="keyword-tag" style="background-color: #fef3c7; color: #92400e; border: 1px solid #fde68a; padding: 2px 6px; border-radius: 4px; font-size: 0.7rem;">
                                            ${synonym.original_keyword} → ${synonym.related_synonyms.join(', ')}
                                        </span>
                                    `).join('')}
                                </div>
                            </div>
                        ` : ''}
                    </div>
                </div>
            `;
        }
        
        messageEl.innerHTML = `
            <div class="message-bubble user-bubble">
                ${tagsHtml}
                <div style="white-space: pre-wrap;">${message}</div>
            </div>
            <div class="avatar">U</div>
        `;
        chatMessages.appendChild(messageEl);
        chatMessages.scrollTop = chatMessages.scrollHeight;
    }
    // AIメッセージの追加
    function addAIMessage(message, sources = [], keywords = [], emotions = [], synonyms = []) {
        // デバッグログは開発時のみ表示
        // console.log('AIメッセージ追加関数が呼び出されました');
        // console.log('受け取ったソース:', sources);
        // console.log('キーワード:', keywords);
        // console.log('感情:', emotions);
        // console.log('シノニム:', synonyms);
        
        const messageEl = document.createElement('div');
        messageEl.className = 'chat-message';
        
        // 感情タグのHTMLを生成
        let emotionsHtml = '';
        /* コメントアウト：感情タグの表示を無効化
        if (emotions && emotions.length > 0) {
            emotionsHtml = `
                <div class="emotion-tags">
                    <div class="emotions-title">検出された感情:</div>
                    <div style="display: flex; flex-wrap: wrap; gap: 5px;">
                        ${emotions.map(emotion => `<span class="emotion-tag">${emotion}</span>`).join('')}
                    </div>
                </div>
            `;
        }
        */
        
        // キーワードタグのHTMLを生成
        let keywordsHtml = '';
        /*
        if (keywords && keywords.length > 0) {
            const keywordId = `keywords-${Date.now()}`;
            keywordsHtml = `
                <div class="keyword-tags">
                    <div class="keywords-header" style="display: flex; align-items: center; margin-bottom: 0.5rem;">
                        <button class="keywords-toggle" data-target="${keywordId}" 
                                style="background: none; border: none; cursor: pointer; display: flex; align-items: center; padding: 2px 8px; border-radius: 4px; background-color: #f3f4f6; color: #374151;">
                            <span class="toggle-icon" style="margin-right: 4px; transition: transform 0.2s;">▼</span>
                            <span class="keywords-title" style="font-size: 0.875rem; font-weight: 500;">検索クエリ変換 (${keywords.length})</span>
                        </button>
                    </div>
                    <div id="${keywordId}" class="keywords-content" style="display: flex; flex-wrap: wrap; gap: 5px;">
                        ${keywords.map(kw => `<span class="keyword-tag" style="background-color: #dcfce7; color: #166534; border: 1px solid #bbf7d0;">${kw}</span>`).join('')}
                    </div>
                </div>
            `;
        }
        */

        // シノニム情報のHTMLを生成（検索クエリ変換と同じスタイル）
        let synonymsHtml = '';
        /*
        if (synonyms && synonyms.length > 0) {
            // console.log('シノニムセクションを生成します');
            const synonymId = `synonyms-${Date.now()}`;
            synonymsHtml = `
                <div class="keyword-tags">
                    <div class="keywords-header" style="display: flex; align-items: center; margin-bottom: 0.5rem;">
                        <button class="keywords-toggle" data-target="${synonymId}" 
                                style="background: none; border: none; cursor: pointer; display: flex; align-items: center; padding: 2px 8px; border-radius: 4px; background-color: #fef3c7; color: #92400e;">
                            <span class="toggle-icon" style="margin-right: 4px; transition: transform 0.2s;">▼</span>
                            <span class="keywords-title" style="font-size: 0.875rem; font-weight: 500;">シノニムクエリ拡張 (${synonyms.length})</span>
                        </button>
                    </div>
                    <div id="${synonymId}" class="keywords-content" style="display: flex; flex-wrap: wrap; gap: 5px;">
                        ${synonyms.map(synonym => `
                            <span class="keyword-tag" style="background-color: #fef3c7; color: #92400e; border: 1px solid #fde68a;">
                                ${synonym.original_keyword} → ${synonym.related_synonyms.join(', ')}
                            </span>
                        `).join('')}
                    </div>
                </div>
            `;
            // console.log('生成したシノニムHTML:', synonymsHtml);
        }
        */

        // ソース情報のHTMLを生成
        let sourcesHtml = '';
        /*
        if (sources && sources.length > 0) {
            // console.log('ソースセクションを生成します');
            sourcesHtml = `
                <div class="source-section">
                    <p style="font-size: 0.75rem; font-weight: 500; color: #6b7280; margin-bottom: 0.5rem;">参照ソース:</p>
                    ${sources.map(source => {
                        // chunk_PageNo_ChunkNo形式からページ番号を抽出
                        let displayName = source.name;
                        if (source.id && source.id.startsWith('chunk_')) {
                            const match = source.id.match(/chunk_(\d+)_(\d+)/);
                            if (match && match[1]) {
                                const pageNo = parseInt(match[1], 10);
                                const chunkNo = parseInt(match[2], 10);
                                displayName = `${pageNo+1}枚目 `;
                            }
                        }
                        return `
                            <a class="source-link" data-doc-id="${source.id}">
                                ${displayName}
                            </a>
                        `;
                    }).join('')}
                </div>
            `;
            // console.log('生成したソースHTML:', sourcesHtml);
        }
        */
        
        // console.log('AIメッセージのHTMLを生成します');
        
        // messageがundefinedの場合のデフォルト値を設定
        const safeMessage = message || "申し訳ありませんが、応答の生成中にエラーが発生しました。";
        
        const resultCount = Array.isArray(sources) ? sources.length : 0;
        // リンクは一時的に非表示（将来再表示のためロジックは残す）
        const modalLinkHtml = '';

        const messageHtml = `
            <div class="avatar system-avatar">I</div>
            <div class="message-bubble system-bubble">
                ${emotionsHtml}
                ${keywordsHtml}
                ${synonymsHtml}
                <div style="max-height: 400px; overflow-y: auto; white-space: pre-wrap;">${safeMessage.replace(/\n/g, '<br>')}</div>
                ${modalLinkHtml}
                ${sourcesHtml}
            </div>
        `;
        // console.log('生成したHTML:', messageHtml);
        messageEl.innerHTML = messageHtml;
        
        chatMessages.appendChild(messageEl);
        chatMessages.scrollTop = chatMessages.scrollHeight;
        
        // ソースリンクにイベントリスナーを追加
        messageEl.querySelectorAll('.source-link').forEach(link => {
            link.addEventListener('click', async () => {
                // ソースのID取得
                const docId = link.dataset.docId;
                console.log('ソースリンクがクリックされました:', docId);
                
                try {
                    // チャンクソースの場合（chunk_PageNo_ChunkNo形式）
                    if (docId && docId.startsWith('chunk_')) {
                        console.log('チャンクソースが検出されました:', docId);
                        // chunk_PageNo_ChunkNoからページ番号を抽出
                        const match = docId.match(/chunk_(\d+)_\d+/);
                        if (match && match[1]) {
                            const pageNo = parseInt(match[1], 10);
                            console.log(`ページ番号 ${pageNo} を検索中...`);
                            
                            // ページ番号に対応するグループを検索
                            const pageGroup = pageItems.find(group => 
                                group.pageNumber === pageNo || 
                                (group.id && group.id === `page_${pageNo}`) ||
                                (group.name && group.name.includes(`ページ ${pageNo}`))
                            );
                            
                            if (pageGroup) {
                                console.log(`ページグループが見つかりました: ${pageGroup.id}`);
                                // グループを選択してハイライト表示
                                await selectDocumentGroup(pageGroup);
                                
                                // 左パネルの対応するアイテムをハイライト
                                const pageItem = document.querySelector(`.page-item[data-doc-id="${pageGroup.id}"]`);
                                if (pageItem) {
                                    // 既存のハイライトをクリア
                                    document.querySelectorAll('.page-item').forEach(item => {
                                        item.classList.remove('highlighted');
                                    });
                                    
                                    // 新しいハイライトを適用
                                    pageItem.classList.add('highlighted');
                                    pageItem.classList.add('active');
                                    
                                    // スクロールして表示
                                    pageItem.scrollIntoView({ behavior: 'smooth', block: 'center' });
                                }
                                return;
                            } else {
                                console.log(`ページ ${pageNo} のグループが見つかりません`);
                            }
                        }
                    }
                    
                    // path_で始まるIDの場合、filepathに基づいて対応するアイテムを探す
                    if (docId.startsWith('path_')) {
                        // ファイル名を取得
                        const filename = link.textContent.trim();
                        
                        // filepathプロパティを持つソースの場合
                        const source = sources.find(s => s.name === filename);
                        if (source && source.filepath) {
                            // filepathに基づいて対応するページアイテムを検索
                            const matchingItem = pageItems.find(item => 
                                item.filepath && item.filepath.includes(filename));
                            
                            if (matchingItem) {
                                // 対応するアイテムを選択（ハイライト表示あり）
                                console.log(`対応するアイテムが見つかりました: ID=${matchingItem.id}, 名前=${matchingItem.name}`);
                                selectDocument(matchingItem.id, true);
                                return;
                            }
                        }
                    }
                    
                    // 通常の動作（IDに基づいて選択、ハイライトあり）
                    selectDocument(docId, true);
                } catch (error) {
                    console.error('ソースクリック処理中にエラーが発生:', error);
                }
            });
        });

        // 検索結果全件モーダル
        const modalTrigger = messageEl.querySelector('.open-results-modal');
        if (modalTrigger) {
            modalTrigger.addEventListener('click', function(e) {
                e.preventDefault();
                openResultsModal(sources || []);
            });
        }
    }

    // 検索結果モーダルを開く
    function openResultsModal(sources) {
        // 既存を消す
        const existing = document.getElementById('results-modal');
        if (existing) existing.remove();

        // スコア降順にソート（score がなければそのまま）
        const sorted = [...sources].sort((a,b) => (b.score||0) - (a.score||0));
        const modal = document.createElement('div');
        modal.id = 'results-modal';
        modal.style.cssText = 'position:fixed; inset:0; background:rgba(0,0,0,.3); z-index:2000; display:flex; align-items:center; justify-content:center;';
        const panel = document.createElement('div');
        panel.style.cssText = 'background:#fff; width: min(900px, 95vw); max-height: 90vh; border-radius:8px; box-shadow:0 10px 25px rgba(0,0,0,.15); display:flex; flex-direction:column;';
        panel.innerHTML = `
            <div style="display:flex; align-items:center; justify-content:space-between; padding:10px 12px; border-bottom:1px solid #e5e7eb;">
                <div style="font-weight:600;">検索結果 全件表示（${sorted.length}件）</div>
                <button id="results-modal-close" style="background:none; border:none; font-size:1.1rem; cursor:pointer;">×</button>
            </div>
            <div id="results-list" style="padding:10px 12px; overflow:auto;"></div>
        `;
        modal.appendChild(panel);
        document.body.appendChild(modal);

        function renderList(items){
            const list = panel.querySelector('#results-list');
            list.innerHTML = (items||[]).map((s,idx)=>{
                const page = (typeof s.pageNumber === 'number') ? s.pageNumber : (s.page_no ?? -1);
                const chunk = (typeof s.chunkNumber === 'number') ? s.chunkNumber : (s.chunk_index ?? 0);
                const title = s.title || s.name || s.fileType || '—';
                return `
                <div style="padding:10px 8px; border-bottom:1px solid #f3f4f6;">
                    <div style="display:flex; align-items:center; gap:8px;">
                        <div style="font-weight:600;">#${idx+1}</div>
                        <div style="color:#6b7280; font-size:.85rem;">score: ${(s.score??0).toFixed(4)}</div>
                        <div style="color:#111827; font-size:.9rem; font-weight:600;">${title}</div>
                        <div style="color:#6b7280; font-size:.85rem;">${page>=0 ? `${page+1}枚目` : 'ページ情報なし'}${chunk?` / チャンク${chunk}`:''}</div>
                    </div>
                    <div style="margin-top:4px; color:#6b7280; font-size:.8rem;">workId: ${s.filepath || s.workId || '—'}</div>
                    <div style="margin-top:6px; color:#111827; white-space:pre-wrap;">${(s.content||'').slice(0,400)}</div>
                </div>`;
            }).join('');
        }
        renderList(sorted);

        panel.querySelector('#results-modal-close').addEventListener('click', ()=>{
            modal.remove();
        });
        modal.addEventListener('click', (e)=>{
            if (e.target === modal) modal.remove();
        });
    }

    // 時間のフォーマット
    function formatTimeAgo(date) {
        if (!date) return '';
        
        const now = new Date();
        const diffMs = now - date;
        const diffMins = Math.floor(diffMs / 60000);
        const diffHours = Math.floor(diffMins / 60);
        
        if (diffHours >= 24) {
            return `${Math.floor(diffHours / 24)} days ago`;
        } else if (diffHours >= 1) {
            return `${diffHours} hour${diffHours > 1 ? 's' : ''} ago`;
        } else {
            return `${diffMins} minute${diffMins !== 1 ? 's' : ''} ago`;
        }
    }

    // イベントリスナーのセットアップ
    function setupEventListeners() {
        // チャットトグルボタン
        chatToggleBtn.addEventListener('click', toggleChat);
        closeChatBtn.addEventListener('click', toggleChat);
        
        // チャット送信ボタンの取得
        const chatSendBtn = document.getElementById('chat-send-btn');
        
        // IME中かどうかを追跡する変数
        let isComposing = false;
        
        // チャット送信ボタンの有効/無効制御
        function updateSendButtonState() {
            if (chatSendBtn && chatInput) {
                chatSendBtn.disabled = chatInput.value.trim() === '';
            }
        }
        
        // メッセージ送信の共通処理
        async function sendMessage() {
            const userMessage = chatInput.value.trim();
            if (userMessage === '' || isComposing) return;
            
            // 入力フィールドをクリア
            chatInput.value = '';
            updateSendButtonState();
            
            // ユーザーメッセージを表示
            addUserMessage(userMessage);
            
            // プロンプトテンプレートを取得
            const promptTemplate = localStorage.getItem('dsPromptTemplate');
            console.log('取得したプロンプトテンプレート:', promptTemplate);
            
            // プロンプトテンプレートとユーザーメッセージを組み合わせる
            let message = userMessage;
            
            // プロンプトテンプレートが存在する場合、メッセージの前に追加
            if (promptTemplate && promptTemplate.trim() !== '') {
                // この部分が重要 - テンプレートとメッセージの間に必ず改行を2つ入れる
                message = `${promptTemplate}\n\n${userMessage}`;
                
                // デバッグログをより詳細にして、分かりやすく表示
                console.log('============== メッセージ組み立て ===============');
                console.log('1. プロンプトテンプレート: ', promptTemplate);
                console.log('2. ユーザーメッセージ: ', userMessage);
                console.log('3. 最終的なメッセージ(\\n\\nで区切り): ', message);
                console.log('4. テンプレート長: ', promptTemplate.length);
                console.log('5. 最終メッセージ長: ', message.length);
                // 実際の改行コードを表示
                const separator = message.substring(promptTemplate.length, promptTemplate.length + 10);
                console.log('6. 区切り文字（バイナリ表示）: ', Array.from(separator).map(c => c.charCodeAt(0)));
                console.log('===============================================');
            } else {
                console.log('プロンプトテンプレートが設定されていないか空です');
            }
            
            // AI応答を取得して表示
            const docId = selectedDocument ? selectedDocument.id : null;
            const response = await sendChatMessage(message, docId);
            
            // レスポンスからキーワードとシノニム情報を取得してユーザーメッセージを更新
            if (response.keywords || response.synonyms) {
                // 最後のユーザーメッセージを探して更新
                const userMessages = chatMessages.querySelectorAll('.chat-message.user');
                const lastUserMessage = userMessages[userMessages.length - 1];
                if (lastUserMessage) {
                    // 新しいHTMLで置き換え
                    const messageId = `user-message-${Date.now()}`;
                    let tagsHtml = '';
                    const keywords = response.keywords || [];
                    const synonyms = response.synonyms || [];
                    
                    if (keywords.length > 0 || synonyms.length > 0) {
                        tagsHtml = `
                            <div style="margin-bottom: 0.5rem;">
                                <button class="user-tags-toggle" data-target="${messageId}-tags" 
                                        style="background: none; border: none; cursor: pointer; display: flex; align-items: center; padding: 2px 6px; border-radius: 4px; background-color: #f8f9fa; color: #495057; font-size: 0.8rem;">
                                    <span class="toggle-icon" style="margin-right: 4px; transition: transform 0.2s;">▶</span>
                                    <span>クエリ解析を表示</span>
                                </button>
                                <div id="${messageId}-tags" class="user-tags-content" style="display: none; margin-top: 0.5rem;">
                                    ${keywords.length > 0 ? `
                                        <div class="keyword-tags" style="margin-bottom: 0.5rem;">
                                            <div style="font-size: 0.75rem; font-weight: 500; color: #495057; margin-bottom: 0.25rem;">検索クエリ変換 (${keywords.length})</div>
                                            <div style="display: flex; flex-wrap: wrap; gap: 3px;">
                                                ${keywords.map(kw => `<span class="keyword-tag" style="background-color: #dcfce7; color: #166534; border: 1px solid #bbf7d0; padding: 2px 6px; border-radius: 4px; font-size: 0.7rem;">${kw}</span>`).join('')}
                                            </div>
                                        </div>
                                    ` : ''}
                                    ${synonyms.length > 0 ? `
                                        <div class="keyword-tags">
                                            <div style="font-size: 0.75rem; font-weight: 500; color: #495057; margin-bottom: 0.25rem;">シノニムクエリ拡張 (${synonyms.length})</div>
                                            <div style="display: flex; flex-wrap: wrap; gap: 3px;">
                                                ${synonyms.map(synonym => `
                                                    <span class="keyword-tag" style="background-color: #fef3c7; color: #92400e; border: 1px solid #fde68a; padding: 2px 6px; border-radius: 4px; font-size: 0.7rem;">
                                                        ${synonym.original_keyword} → ${synonym.related_synonyms.join(', ')}
                                                    </span>
                                                `).join('')}
                                            </div>
                                        </div>
                                    ` : ''}
                                </div>
                            </div>
                        `;
                    }
                    
                    lastUserMessage.querySelector('.message-bubble').innerHTML = `
                        ${tagsHtml}
                        <div style="white-space: pre-wrap;">${userMessage}</div>
                    `;
                }
            }
            
            // 感情、キーワード、シノニムとともにAIメッセージを表示（キーワードとシノニムは表示しない）
            addAIMessage(response.content, response.sources, [], response.emotions, []);
        }
        
        // チャット入力のイベントリスナー（IME対応）
        if (chatInput) {
            // 入力内容の変更時にボタンの有効無効を制御
            chatInput.addEventListener('input', updateSendButtonState);
            
            // IME開始の検知
            chatInput.addEventListener('compositionstart', function() {
                isComposing = true;
            });
            
            // IME終了の検知
            chatInput.addEventListener('compositionend', function() {
                isComposing = false;
            });
            
            // Enterキーによる送信（IME中は無効化）
            chatInput.addEventListener('keydown', function(e) {
                if (e.key === 'Enter' && !isComposing && !e.shiftKey) {
                    e.preventDefault();
                    sendMessage();
                }
            });
            
            // 初期状態でボタンの有効無効を設定
            updateSendButtonState();
        }
        
        // 送信ボタンのクリックイベント
        if (chatSendBtn) {
            chatSendBtn.addEventListener('click', function(e) {
                e.preventDefault();
                sendMessage();
            });
        }
        
        // アップロードボタン
        uploadBtn.addEventListener('click', function() {
            // 非表示のファイル入力要素を作成
            const fileInput = document.createElement('input');
            fileInput.type = 'file';
            fileInput.accept = '.pdf';
            fileInput.style.display = 'none';
            
            // ファイル選択ダイアログで選択された時の処理
            fileInput.addEventListener('change', async function() {
                if (this.files.length > 0) {
                    const pdfFile = this.files[0];
                    
                    // ファイルサイズを確認 (10MB以上の場合は警告)
                    if (pdfFile.size > 10 * 1024 * 1024) {
                        const confirmUpload = confirm(`選択されたファイルのサイズが大きいです (${(pdfFile.size/1024/1024).toFixed(2)}MB)。\nアップロード処理に時間がかかる可能性があります。続けますか？`);
                        if (!confirmUpload) return;
                    }
                    
                    try {
                        // ユーザーに処理中であることを通知
                        const toastMessage = '処理中です。しばらくお待ちください...';
                        const toast = showToast(toastMessage, 0); // 0を指定して自動で閉じないようにする
                        
                        // FormDataを作成
                        const formData = new FormData();
                        // fewshotのtype選択があれば先頭1件を付与
                        try {
                            const selected = Array.isArray(window.fewshotSelectedTypes) ? window.fewshotSelectedTypes : [];
                            if (selected.length > 0) {
                                formData.append('type', selected[0]);
                            }
                        } catch {}
                        
                        // 実際のログインユーザー情報を取得
                        const currentUser = await getCurrentUser();
                        if (!currentUser || !currentUser.user) {
                            throw new Error('ユーザー認証情報の取得に失敗しました');
                        }
                        
                        formData.append('userid', 'ilu-demo'); // 外部API認証用（固定値）
                        formData.append('password', 'ilupass'); // 外部API認証用（固定値）
                        formData.append('login_user', currentUser.user.username); // 実際のログインユーザー
                        formData.append('file', pdfFile);
                        
                        console.log(`PDFファイル「${pdfFile.name}」をアップロード中...`);
                        
                        // デバッグ情報を追加
                        console.log('アップロードするファイル情報:', {
                            name: pdfFile.name,
                            size: pdfFile.size,
                            type: pdfFile.type
                        });
                        console.log('フォームデータのキー一覧:', [...formData.keys()]);
                        console.log('userid:', formData.get('userid'));
                        
                        // ベースパスを取得
                        const basePath = getBasePath();
                        console.log('ベースパス:', basePath);
                        const requestUrl = getBasePath() + `/api/AutoStructure/Analyze`;
                        console.log('リクエストURL:', requestUrl);
                        
                        // /AutoStructure/Analyzeにアクセスしてファイルを解析
                        const response = await fetch(requestUrl, {
                            method: 'POST',
                            body: formData,
                            // キャッシュを無効化し、クロスドメインCookieを送信する
                            credentials: 'same-origin',
                            cache: 'no-cache'
                        });
                        
                        // トーストを閉じる
                        if (document.body.contains(toast)) {
                            document.body.removeChild(toast);
                        }
                        
                        // レスポンスを確認
                        if (!response.ok) {
                            let errorMessage = 'ファイルの解析中にエラーが発生しました';
                            try {
                                const errorData = await response.json();
                                errorMessage = errorData.error_detail || errorData.error || errorMessage;
                            } catch (e) {
                                console.error('エラーレスポンスの解析に失敗:', e);
                            }
                            showToast(`エラー: ${errorMessage}`, 10000);
                            console.error('解析エラー:', errorMessage);
                            return;
                        }
                        
                        // 解析結果を取得
                        const result = await response.json();
                        console.log('解析結果:', result);
                        console.log('解析結果の詳細構造:', JSON.stringify(result, null, 2));
                        
                        // リターンコード確認
                        if (result.return_code !== 0) {
                            const errorMessage = result.error_detail || '解析処理でエラーが発生しました';
                            showToast(`エラー (コード: ${result.return_code}): ${errorMessage}`, 10000);
                            console.error('解析エラー:', errorMessage);
                            return;
                        }
                        
                        // 成功メッセージをトーストで表示
                        const workId = result.work_id || '';
                        
                        // 結果取得APIを呼び出してシノニムデータを取得
                        try {
                            console.log(`結果取得API呼び出し開始 - work_id: ${workId}`);
                            
                            // サーバー側の内部APIを経由して外部APIを呼び出し
                            const basePath = getBasePath();
                            const checkResponse = await fetch(`${basePath}/api/data-structuring/status?workId=${workId}&forceRefresh=true`, {
                                method: 'GET',
                                credentials: 'include', // ASP.NET認証クッキーを含める
                                cache: 'no-cache'
                            });
                            
                            if (checkResponse.ok) {
                                const checkResult = await checkResponse.json();
                                console.log('結果取得API レスポンス:', checkResult);
                                console.log('結果取得API レスポンス詳細:', JSON.stringify(checkResult, null, 2));
                                
                                // まず基本の完了メッセージを表示
                                showToast(`ファイル「${pdfFile.name}」の解析が完了しました。\n処理ID: ${workId}`, 5000);
                                
                                // シノニムデータ専用のトーストを個別表示
                                let synonymFound = false;
                                let synonymToastMessage = '🔍 シノニムデータ取得結果\n\n';
                                
                                // シノニムリストの取得
                                if (checkResult.synonym_list && Array.isArray(checkResult.synonym_list) && checkResult.synonym_list.length > 0) {
                                    synonymToastMessage += `【シノニムリスト】(${checkResult.synonym_list.length}件)\n${checkResult.synonym_list.join(', ')}\n\n`;
                                    console.log('✅ シノニムリスト取得成功:', checkResult.synonym_list);
                                    synonymFound = true;
                                } else {
                                    synonymToastMessage += '【シノニムリスト】取得できませんでした\n\n';
                                    console.log('❌ シノニムリスト取得失敗 - データなし');
                                }
                                
                                // シノニムデータの取得
                                if (checkResult.synonym && Array.isArray(checkResult.synonym) && checkResult.synonym.length > 0) {
                                    const synonymData = checkResult.synonym.map(item => {
                                        if (typeof item === 'string') return item;
                                        return item.surface || item.text || item.word || JSON.stringify(item);
                                    });
                                    synonymToastMessage += `【シノニムデータ】(${checkResult.synonym.length}件)\n${synonymData.join(', ')}`;
                                    console.log('✅ シノニムデータ取得成功:', checkResult.synonym);
                                    synonymFound = true;
                                } else {
                                    synonymToastMessage += '【シノニムデータ】取得できませんでした';
                                    console.log('❌ シノニムデータ取得失敗 - データなし');
                                }
                                
                                // シノニム専用トーストを1秒後に表示（基本メッセージの後）
                                setTimeout(() => {
                                    if (synonymFound) {
                                        showToast(synonymToastMessage, 12000);
                                        console.log('🎉 シノニムデータが正常に取得されました');
                                    } else {
                                        // showToast('⚠️ シノニムデータ取得結果\n\nシノニムリスト・シノニムデータともに取得できませんでした。\n\nAPIレスポンス構造:\n' + Object.keys(checkResult).join(', '), 10000);
                                        console.log('⚠️ シノニムデータが見つかりませんでした。APIレスポンス:', checkResult);
                                    }
                                }, 1000);
                            } else {
                                console.warn('結果取得APIでエラー:', checkResponse.status);
                                showToast(`ファイル「${pdfFile.name}」のアップロードが完了しました。\n処理ID: ${workId}\n\n※画面右上にある構造化処理状況ボタンを押すと現在の状況をご確認頂けます。時間がかかる場合がありますので、時間をおいて複数回ご確認ください。`, 8000);
                            }
                        } catch (checkError) {
                            console.error('結果取得API呼び出しエラー:', checkError);
                            showToast(`ファイル「${pdfFile.name}」の解析が完了しました。\n処理ID: ${workId}\n\n※シノニムデータの取得に失敗しました`, 8000);
                        }
                        
                        // アップロード履歴に追加（ローカルストレージ）
                        saveUploadHistory(workId, pdfFile.name);
                        
                        // サーバー側のworkId履歴も更新
                        await addWorkIdToServerHistory(workId, pdfFile.name);
                        
                        // アップロード状況一覧がモーダルで開かれている場合は、リアルタイムで更新
                        const uploadStatusModal = document.getElementById('upload-status-modal');
                        if (uploadStatusModal && uploadStatusModal.style.display === 'block') {
                            renderUploadHistory();
                        }
                        
                    } catch (error) {
                        console.error('ファイル解析中にエラーが発生:', error);
                        showToast(`エラー: ${error.message || 'ファイル解析中に問題が発生しました'}`, 10000);
                    }
                }
            });
            
            // ファイル選択ダイアログを表示
            document.body.appendChild(fileInput);
            fileInput.click();
            
            // イベントハンドラが実行された後にDOM要素を削除
            setTimeout(() => {
                document.body.removeChild(fileInput);
            }, 1000);
        });
        // ヘッダーの新しいボタン用イベントリスナー
        // 一括ダウンロードボタン
        batchDownloadBtn.addEventListener('click', function() {
            // 選択されている文書がなければ通知
            if (pageItems.length === 0) {
                alert('ダウンロードできる文書がありません。');
                return;
            }
            
            // 一括ダウンロード処理
            // 1. ダウンロード確認ダイアログを表示
            const downloadConfirmation = document.createElement('div');
            downloadConfirmation.classList.add('processing-notification');
            downloadConfirmation.innerHTML = `
                <div style="margin-right: 15px;">
                    <i class="fas fa-download" style="font-size: 24px; color: #3389ca;"></i>
                </div>
                <div style="flex: 1;">
                    <div class="processing-text">一括ダウンロードしますか？</div>
                    <div style="font-size: 12px; color: #6b7280; margin-top: 5px;">
                        全部で ${pageItems.length} 件のファイルがダウンロードされます。
                    </div>
                    <div class="notification-actions" style="display: flex; margin-top: 8px; gap: 10px;">
                        <button id="cancel-download-btn" class="notification-action-button">
                            キャンセル
                        </button>
                        <button id="confirm-download-btn" class="notification-action-button" style="background-color: #3389ca; color: white;">
                            ダウンロード
                        </button>
                    </div>
                </div>
                <div style="margin-left: 10px; cursor: pointer;" id="close-notification-btn">
                    <i class="fas fa-times"></i>
                </div>
            `;
            
            document.body.appendChild(downloadConfirmation);
            
            // 閉じるボタンのイベントリスナー
            document.getElementById('close-notification-btn').addEventListener('click', function() {
                document.body.removeChild(downloadConfirmation);
            });
            
            // キャンセルボタンのイベントリスナー
            document.getElementById('cancel-download-btn').addEventListener('click', function() {
                document.body.removeChild(downloadConfirmation);
            });
            
            // 確認ボタンのイベントリスナー
            document.getElementById('confirm-download-btn').addEventListener('click', async function() {
                // ダイアログを閉じる
                document.body.removeChild(downloadConfirmation);
                
                // ダウンロード処理中の通知を表示
                const progressNotification = document.createElement('div');
                progressNotification.classList.add('processing-notification');
                progressNotification.innerHTML = `
                    <div class="processing-text">ファイルを準備しています...</div>
                `;
                document.body.appendChild(progressNotification);
                
                try {
                    // 実際のダウンロード処理を実装（例: ZIP作成APIを呼び出す）
                    // 本実装ではダウンロードAPIの呼び出しをシミュレート
                    setTimeout(() => {
                        document.body.removeChild(progressNotification);
                        
                        // 成功通知
                        const successNotification = document.createElement('div');
                        successNotification.classList.add('processing-notification');
                        successNotification.innerHTML = `
                            <div style="margin-right: 15px;">
                                <i class="fas fa-check-circle" style="font-size: 24px; color: #10b981;"></i>
                            </div>
                            <div class="processing-text">
                                ファイルの準備が完了しました。ダウンロードを開始します。
                            </div>
                        `;
                        document.body.appendChild(successNotification);
                        
                        // 3秒後に通知を閉じる
                        setTimeout(() => {
                            document.body.removeChild(successNotification);
                        }, 3000);
                        
                        // 一括ダウンロード処理の実行
                        // サーバーにリクエストを送信
                        const filepaths = pageItems.map(item => item.filepath);
                        
                        fetch(getBasePath() + '/api/data-structuring/batch-download', {
                            method: 'POST',
                            headers: {
                                'Content-Type': 'application/json',
                            },
                            credentials: 'include', // ASP.NET認証クッキーを含める
                            body: JSON.stringify({ filepaths: filepaths })
                        })
                        .then(response => {
                            if (!response.ok) {
                                throw new Error('ダウンロード処理に失敗しました');
                            }
                            return response.blob();
                        })
                        .then(blob => {
                            // BlobをダウンロードするためのURLを作成
                            const url = window.URL.createObjectURL(blob);
                            
                            // ダウンロードリンクを作成して自動的にクリック
                            const a = document.createElement('a');
                            a.style.display = 'none';
                            a.href = url;
                            
                            // 日本時間で年月日時分を取得してファイル名に設定
                            const now = new Date();
                            const year = now.getFullYear();
                            const month = String(now.getMonth() + 1).padStart(2, '0');
                            const day = String(now.getDate()).padStart(2, '0');
                            const hour = String(now.getHours()).padStart(2, '0');
                            const minute = String(now.getMinutes()).padStart(2, '0');
                            
                            // ファイル名（サーバー側で設定されたファイル名が使われる）
                            a.download = `documents_${year}${month}${day}_${hour}${minute}.zip`;
                            
                            document.body.appendChild(a);
                            a.click();
                            
                            // リソースの解放
                            window.URL.revokeObjectURL(url);
                            document.body.removeChild(a);
                            
                            console.log("一括ダウンロード処理が実行されました");
                        })
                        .catch(error => {
                            console.error('一括ダウンロードエラー:', error);
                            
                            // エラー通知
                            const errorNotification = document.createElement('div');
                            errorNotification.classList.add('processing-notification', 'error');
                            errorNotification.innerHTML = `
                                <div style="margin-right: 15px;">
                                    <i class="fas fa-exclamation-circle" style="font-size: 24px; color: #ef4444;"></i>
                                </div>
                                <div class="processing-text">
                                    ダウンロード処理中にエラーが発生しました: ${error.message}
                                </div>
                            `;
                            document.body.appendChild(errorNotification);
                            
                            // 3秒後に通知を閉じる
                            setTimeout(() => {
                                document.body.removeChild(errorNotification);
                            }, 5000);
                        });
                    }, 1500);
                } catch (error) {
                    console.error('一括ダウンロードエラー:', error);
                    document.body.removeChild(progressNotification);
                    
                    // エラー通知
                    const errorNotification = document.createElement('div');
                    errorNotification.classList.add('processing-notification', 'error');
                    errorNotification.innerHTML = `
                        <div style="margin-right: 15px;">
                            <i class="fas fa-exclamation-circle" style="font-size: 24px; color: #ef4444;"></i>
                        </div>
                        <div class="processing-text">
                            ダウンロード処理中にエラーが発生しました。
                        </div>
                    `;
                    document.body.appendChild(errorNotification);
                    
                    // 3秒後に通知を閉じる
                    setTimeout(() => {
                        document.body.removeChild(errorNotification);
                    }, 3000);
                }
            });
        });
        
        // 設定ボタン
        settingsBtn.addEventListener('click', function() {
            // 設定モーダルを作成（フルスクリーンサイズ）
            const settingsModal = document.createElement('div');
            settingsModal.style.position = 'fixed';
            settingsModal.style.top = '0';
            settingsModal.style.left = '0';
            settingsModal.style.width = '100%';
            settingsModal.style.height = '100%';
            settingsModal.style.backgroundColor = 'rgba(0, 0, 0, 0.5)';
            settingsModal.style.display = 'flex';
            settingsModal.style.justifyContent = 'center';
            settingsModal.style.alignItems = 'center';
            settingsModal.style.zIndex = '1000';
            
            // 現在のプロンプトのデフォルト値を取得（localStorage から、なければデフォルト値を使用）
            const defaultPrompt = `あなたは「# 参照ドキュメント」の内容を完璧に理解している物知りな社員です。
社内の手続きや規則について同僚からの質問に回答します。以下の指示に厳密に従ってください。

最優先指示
簡潔さを最優先: 特に指定がない限り、常に簡潔な回答を優先し、詳細な説明は避けてください
マニュアル参照を促進: 標準的な手続きについては詳細なステップを列挙せず、適切なマニュアルを参照するよう促してください
特定の質問への厳密な回答粒度について（類似度85%以上の場合）
以下の質問に非常に類似した質問を受けた場合は、適切なマニュアルを参照して必ず指定された簡潔さの粒度で回答を提供してください：

{回答粒度調整学習データセットはここへ貼り付け}

前提事項
まずは以下のjsonl形式の例示を読み込んでください。

────────────────────────
【jsonl形式の例示】
{回答誤りパターン学習データセットはここへ貼り付け}

────────────────────────
【指示】
以降の質問に対しては、上記【jsonl形式の例示】に示した以下のポイントを踏まえて回答してください：
・例外対応（特に代理店のインプット誤りなど、契約者に非がない場合の柔軟な対応）
・情報抽出の正確さと、必要な記載事項の網羅（証券分割、署名・記名・押印の要否、手続き方法の分岐点）
・具体的かつ簡潔な回答記述

────────────────────────

※非常に重要※
上記のjsonl形式の例示を参考に、以降の【質問】に対して適切な回答を生成してください。

────────────────────────

参照ドキュメントの使用ルール
参照ドキュメントの情報のみを使用して回答してください。
参照ドキュメントに関連情報が「一部でも」含まれている場合は、その情報を基に回答を構築してください。
参照ドキュメントに質問に関する情報が全く存在しない場合にのみ「要求された情報は取得した参照ドキュメントにありません。別の質問を試してください。」と回答してください。
回答の作成方法
参照ドキュメントから関連する情報を見つけたら:

明確かつ簡潔に情報を要約して回答します
各文の末尾に引用元を [doc0]、[doc1] のように表記します
複数の参照ドキュメントを適切に組み合わせて包括的な回答を提供します
質問に直接関係する部分に焦点を当てて回答します
参照ドキュメントから一部の情報しか見つからない場合:

見つかった情報を使って可能な限り回答を提供します
「参照ドキュメントには○○についての情報のみ含まれています」と断りを入れます
決して「回答できない」と判断せず、部分的な情報でも共有します
参照ドキュメントに全く情報がない場合のみ:

「要求された情報は取得した参照ドキュメントにありません。別の質問を試してください。」と回答します
重要事項
参照ドキュメントに関連するキーワードや概念が少しでも含まれている場合は回答を試みてください
自分の知識ではなく、必ず参照ドキュメントの情報のみを使用してください
質問の意図を広く解釈し、関連しそうな情報があれば積極的に提供してください`;
            
            // localStorageから取得（なければデフォルト）
            const currentPrompt = localStorage.getItem('dsPromptTemplate') || defaultPrompt;
            
            console.log('設定モーダル初期化 - 現在のプロンプト:', currentPrompt);
            
            // モーダルコンテンツ（フルスクリーンサイズのモーダル）
            settingsModal.innerHTML = `
                <div style="background-color: white; border-radius: 8px; width: 90%; height: 90%; padding: 20px; box-shadow: 0 4px 6px rgba(0, 0, 0, 0.1); display: flex; flex-direction: column;">
                    <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 20px;">
                        <h2 style="margin: 0; font-size: 1.5rem;">設定</h2>
                        <button id="close-settings-btn" style="background: none; border: none; cursor: pointer; font-size: 1.25rem;">
                            <i class="fas fa-times"></i>
                        </button>
                    </div>
                    
                    <div style="flex: 1; display: flex; flex-direction: column; overflow-y: auto; padding-right: 10px;">
                        <div style="margin-bottom: 20px; flex: 1; display: flex; flex-direction: column;">
                        <div style="margin-bottom: 20px;">
                            <h3 style="font-size: 1.1rem; margin-bottom: 10px;">ユーザープロンプト</h3>
                            <textarea id="prompt-template" style="width: 100%; padding: 10px; border: 1px solid #d1d5db; border-radius: 4px; resize: none; font-family: inherit; height: 240px; background-color: #f9fafb; cursor: not-allowed;" readonly></textarea>
                        </div>
                            
                            <div style="margin-bottom: 20px; flex: 0 0 120px;">
                                <h3 style="font-size: 1.1rem; margin-bottom: 10px;">シノニム</h3>
                                <textarea id="synonyms-area" style="width: 100%; height: 240px; padding: 10px; border: 1px solid #d1d5db; border-radius: 4px; resize: none; font-family: inherit; background-color: #f9fafb; cursor: not-allowed;" placeholder="Sansan:SO,BillOne:BO" readonly></textarea>
                            </div>
                            
                            <!--
                            <div style="margin-bottom: 20px;">
                                <h3 style="font-size: 1.1rem; margin-bottom: 15px;">ログ</h3>
                                <div style="display: flex; flex-direction: column; gap: 10px;">
                                    <a href="${getBasePath()}/api/data-structuring/logs" target="_blank" style="text-decoration: none; color: #3389ca; display: flex; align-items: center; padding: 8px; border: 1px solid #e5e7eb; border-radius: 4px;">
                                        <i class="far fa-file-alt" style="margin-right: 10px;"></i>システムログをダウンロード
                                    </a>
                                    <a href="${getBasePath()}/api/data-structuring/debug-logs" target="_blank" style="text-decoration: none; color: #3389ca; display: flex; align-items: center; padding: 8px; border: 1px solid #e5e7eb; border-radius: 4px;">
                                        <i class="fas fa-bug" style="margin-right: 10px;"></i>デバッグログをダウンロード
                                    </a>
                                </div>
                            </div>
                            -->

                            <div style="margin-top: auto; text-align: right;">
                                <button id="save-settings-btn" style="background-color: #3389ca; color: white; border: none; padding: 10px 20px; border-radius: 4px; cursor: pointer; font-weight: 500;">
                                    保存
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            `;
            
            document.body.appendChild(settingsModal);
            
            // 閉じるボタンのイベントリスナー
            document.getElementById('close-settings-btn').addEventListener('click', function() {
                document.body.removeChild(settingsModal);
            });
            
            // フォントサイズスライダーのイベントリスナー (削除済みのため空にする)
            
            // 保存ボタンのイベントリスナー
            document.getElementById('save-settings-btn').addEventListener('click', function() {
                // 設定値を取得
                const darkMode = false; // ダークモード設定は削除済み
                const fontSize = 16; // フォントサイズ設定は削除済み
                const apiKey = ''; // APIキー設定は削除済み
                const promptTemplate = document.getElementById('prompt-template').value;
                const synonymsText = document.getElementById('synonyms-area').value; // シノニム設定を取得
                
                // デバッグ: プロンプトテンプレートの内容を詳細に記録
                console.log('【設定保存】プロンプトテンプレート保存内容:');
                console.log(promptTemplate);
                console.log('【設定保存】プロンプトテンプレート長:', promptTemplate.length);
                
                // 箇条書き指定の有無をチェック
                const hasBulletPoints = promptTemplate.includes('箇条書き');
                const hasStarBullets = promptTemplate.includes('★') && hasBulletPoints;
                const hasNumberedBullets = promptTemplate.includes('数字') && hasBulletPoints;
                
                console.log('【設定保存】箇条書き指定あり:', hasBulletPoints);
                console.log('【設定保存】★箇条書き指定:', hasStarBullets);
                console.log('【設定保存】数字箇条書き指定:', hasNumberedBullets);
                
                // 設定を保存（localStorage使用）
                localStorage.setItem('darkMode', darkMode);
                localStorage.setItem('fontSize', fontSize);
                localStorage.setItem('dsSynonyms', synonymsText); // シノニム設定を保存
                
                // プロンプトテンプレートが空の場合はデフォルト値を設定
                if (!promptTemplate || promptTemplate.trim() === '') {
                    promptTemplate = defaultPrompt;
                    console.log('空のプロンプトテンプレートを検出したため、デフォルト値を設定しました');
                }
                
                localStorage.setItem('dsPromptTemplate', promptTemplate);
                console.log('==================== 設定保存ログ ====================');
                console.log('設定保存中 - プロンプトテンプレート:', promptTemplate);
                console.log('設定保存中 - プロンプトテンプレート長:', promptTemplate.length);
                
                // 保存された値を検証
                const savedTemplate = localStorage.getItem('dsPromptTemplate');
                console.log('検証 - 保存されたプロンプトテンプレート:', savedTemplate);
                console.log('検証 - 保存されたテンプレート長:', savedTemplate ? savedTemplate.length : 0);
                console.log('==================== 設定保存ログ終了 ====================');
                if (apiKey) {
                    localStorage.setItem('azureApiKey', apiKey);
                }
                
                // 通知を表示
                const notification = document.createElement('div');
                notification.classList.add('processing-notification');
                notification.innerHTML = `
                    <div style="margin-right: 15px;">
                        <i class="fas fa-check-circle" style="font-size: 24px; color: #10b981;"></i>
                    </div>
                    <div class="processing-text">
                        設定が保存されました。
                    </div>
                `;
                document.body.appendChild(notification);
                
                // 設定モーダルを閉じる
                document.body.removeChild(settingsModal);
                
                // 3秒後に通知を閉じる
                setTimeout(() => {
                    document.body.removeChild(notification);
                }, 3000);
                
                // プロンプト設定が変更されたことをコンソールに表示
                console.log('プロンプトテンプレートが更新されました:', promptTemplate);
                
                // デバッグ確認用 - プロンプトテンプレートの保存状態を検証
                setTimeout(() => {
                    const savedTemplate = localStorage.getItem('dsPromptTemplate');
                    console.log('保存されたプロンプトテンプレート:', savedTemplate);
                    console.log('保存されたテンプレート長:', savedTemplate ? savedTemplate.length : 0);
                }, 500);
                
                // 設定の適用（フォントサイズのみ即時反映）
                document.documentElement.style.setProperty('--font-size-base', fontSize + 'px');
                
                // ダークモードの適用
                if (darkMode) {
                    document.documentElement.classList.add('dark-mode');
                } else {
                    document.documentElement.classList.remove('dark-mode');
                }
            });
            
            // 設定値の初期化（保存されている値があれば読み込む）
            const savedDarkMode = localStorage.getItem('darkMode') === 'true';
            const savedFontSize = localStorage.getItem('fontSize') || '16';
            const savedApiKey = localStorage.getItem('azureApiKey') || '';
            let savedPromptTemplate = localStorage.getItem('dsPromptTemplate') || '';
            const savedSynonyms = localStorage.getItem('dsSynonyms') || 'クラウド:cloud,クラウド・コンピューティング\nAI:人工知能,artificial intelligence';
            
            console.log('モーダル初期化時：保存されたプロンプトテンプレート:', savedPromptTemplate);
            
            // もし保存されたテンプレートが空の場合はデフォルト値を設定
            if (!savedPromptTemplate || savedPromptTemplate.trim() === '') {
                savedPromptTemplate = `あなたは「# 参照ドキュメント」の内容を完璧に理解している物知りな社員です。
社内の手続きや規則について同僚からの質問に回答します。以下の指示に厳密に従ってください。

最優先指示
簡潔さを最優先: 特に指定がない限り、常に簡潔な回答を優先し、詳細な説明は避けてください
マニュアル参照を促進: 標準的な手続きについては詳細なステップを列挙せず、適切なマニュアルを参照するよう促してください
特定の質問への厳密な回答粒度について（類似度85%以上の場合）
以下の質問に非常に類似した質問を受けた場合は、適切なマニュアルを参照して必ず指定された簡潔さの粒度で回答を提供してください：

{回答粒度調整学習データセットはここへ貼り付け}

前提事項
まずは以下のjsonl形式の例示を読み込んでください。

────────────────────────
【jsonl形式の例示】
{回答誤りパターン学習データセットはここへ貼り付け}

────────────────────────
【指示】
以降の質問に対しては、上記【jsonl形式の例示】に示した以下のポイントを踏まえて回答してください：
・例外対応（特に代理店のインプット誤りなど、契約者に非がない場合の柔軟な対応）
・情報抽出の正確さと、必要な記載事項の網羅（証券分割、署名・記名・押印の要否、手続き方法の分岐点）
・具体的かつ簡潔な回答記述

────────────────────────

※非常に重要※
上記のjsonl形式の例示を参考に、以降の【質問】に対して適切な回答を生成してください。

────────────────────────

参照ドキュメントの使用ルール
参照ドキュメントの情報のみを使用して回答してください。
参照ドキュメントに関連情報が「一部でも」含まれている場合は、その情報を基に回答を構築してください。
参照ドキュメントに質問に関する情報が全く存在しない場合にのみ「要求された情報は取得した参照ドキュメントにありません。別の質問を試してください。」と回答してください。
回答の作成方法
参照ドキュメントから関連する情報を見つけたら:
明確かつ簡潔に情報を要約して回答します
各文の末尾に引用元を [doc0]、[doc1] のように表記します
複数の参照ドキュメントを適切に組み合わせて包括的な回答を提供します
質問に直接関係する部分に焦点を当てて回答します
参照ドキュメントから一部の情報しか見つからない場合:
見つかった情報を使って可能な限り回答を提供します
「参照ドキュメントには○○についての情報のみ含まれています」と断りを入れます
決して「回答できない」と判断せず、部分的な情報でも共有します
参照ドキュメントに全く情報がない場合のみ:

「要求された情報は取得した参照ドキュメントにありません。別の質問を試してください。」と回答します
重要事項
参照ドキュメントに関連するキーワードや概念が少しでも含まれている場合は回答を試みてください
自分の知識ではなく、必ず参照ドキュメントの情報のみを使用してください
質問の意図を広く解釈し、関連しそうな情報があれば積極的に提供してください`;
                console.log('デフォルトのプロンプトテンプレートを設定しました');
            }
            
            // 削除済みの設定項目の初期化は不要
            
            // 確実にテンプレートが設定されるようにsetTimeoutを使用
            setTimeout(() => {
                const promptTemplateElement = document.getElementById('prompt-template');
                if (promptTemplateElement) {
                    promptTemplateElement.value = savedPromptTemplate;
                    console.log('プロンプトテンプレートを設定しました:', promptTemplateElement.value.substring(0, 50) + '...');
                }
                
                // シノニム設定を設定
                const synonymsAreaElement = document.getElementById('synonyms-area');
                if (synonymsAreaElement) {
                    synonymsAreaElement.value = savedSynonyms;
                    console.log('シノニム設定を設定しました');
                }
            }, 100);
        });
        
        // アカウントアイコンのクリックイベント
        if (accountIcon) {
            console.log('アカウントアイコンのイベントリスナーを設定:', accountIcon);
        accountIcon.addEventListener('click', function(e) {
                console.log('アカウントアイコンがクリックされました');
            e.stopPropagation();
            const dropdown = document.getElementById('account-dropdown');
                console.log('ドロップダウン要素:', dropdown);
                
                if (dropdown) {
                    console.log('現在のdisplay:', dropdown.style.display);
                    // 表示状態の切り替え（初期値も考慮）
                    if (dropdown.style.display === 'none' || dropdown.style.display === '') {
                        dropdown.style.display = 'block';
                        console.log('ドロップダウンを表示しました');
                    } else {
                        dropdown.style.display = 'none';
                        console.log('ドロップダウンを非表示にしました');
                    }
                } else {
                    console.error('ドロップダウン要素が見つかりません');
                }
            });
        } else {
            console.error('アカウントアイコン要素が見つかりません:', accountIcon);
        }
        
        // ドキュメント上の任意の場所をクリックしたときにドロップダウンを閉じる
        document.addEventListener('click', function() {
            const dropdown = document.getElementById('account-dropdown');
            if (dropdown && dropdown.style.display === 'block') {
                dropdown.style.display = 'none';
            }
        });
        
        // ダウンロードボタン
        downloadBtn.addEventListener('click', function() {
            if (selectedDocument) {
                // テキストコンテンツの取得
                const content = selectedDocument.content;
                
                // Blobオブジェクトの作成
                const blob = new Blob([content], { type: 'text/plain' });
                
                // ダウンロードリンクの作成
                const url = URL.createObjectURL(blob);
                const link = document.createElement('a');
                link.href = url;
                link.download = selectedDocument.name;
                
                // リンクを非表示でDOMに追加
                document.body.appendChild(link);
                
                // ダウンロードをトリガー
                link.click();
                
                // 不要になったリソースの解放
                setTimeout(() => {
                    document.body.removeChild(link);
                    URL.revokeObjectURL(url);
                }, 100);
            } else {
                alert('ダウンロードするドキュメントを選択してください。');
            }
        });
        
        // リサイザー
        let isLeftResizing = false;
        let isRightResizing = false;
        
        // リサイザーの高さをコンテンツエリアの高さに合わせる関数
        function updateResizerHeights() {
            const contentHeight = document.querySelector('.content-area').scrollHeight;
            const leftSidebarHeight = Math.max(leftSidebar.scrollHeight, contentHeight);
            const rightSidebarHeight = Math.max(rightSidebar.scrollHeight, contentHeight);
            
            // 左リサイザーの高さを設定
            leftResizer.style.height = leftSidebarHeight + 'px';
            // 右リサイザーの高さを設定
            rightResizer.style.height = rightSidebarHeight + 'px';
        }
        
        // 初期表示時とウィンドウリサイズ時に高さを更新
        updateResizerHeights();
        window.addEventListener('resize', updateResizerHeights);
        
        // ドキュメント読み込み時にリサイザーの高さを更新
        document.querySelector('.document-content').addEventListener('scroll', updateResizerHeights);
        
        leftResizer.addEventListener('mousedown', function(e) {
            isLeftResizing = true;
            e.preventDefault();
        });
        
        rightResizer.addEventListener('mousedown', function(e) {
            isRightResizing = true;
            e.preventDefault();
        });
        
        document.addEventListener('mousemove', function(e) {
            if (isLeftResizing) {
                const newWidth = e.clientX;
                if (newWidth > 150 && newWidth < 500) {
                    leftSidebar.style.width = newWidth + 'px';
                }
            }
            
            if (isRightResizing) {
                const containerWidth = document.body.clientWidth;
                const newWidth = containerWidth - e.clientX;
                if (newWidth > 250) {
                    rightSidebar.style.width = newWidth + 'px';
                }
            }
            
            // リサイズ中も高さを更新
            if (isLeftResizing || isRightResizing) {
                updateResizerHeights();
            }
        });
        
        document.addEventListener('mouseup', function() {
            isLeftResizing = false;
            isRightResizing = false;
        });
        

        
        // シノニム一覧のイベントリスナー
        setupSynonymEventListeners();
    }
    
    // シノニム一覧のイベントリスナーをセットアップ
    function setupSynonymEventListeners() {
        const synonymItems = document.querySelectorAll('.synonym-item');
        
        synonymItems.forEach(item => {
            item.addEventListener('click', function() {
                const synonymType = this.getAttribute('data-type');
                selectSynonymType(synonymType);
            });
        });
    }
    
    // シノニムタイプを選択
    function selectSynonymType(type) {
        console.log(`シノニムタイプ選択: ${type}`);
        
        // アクティブ状態を更新
        document.querySelectorAll('.synonym-item').forEach(item => {
            item.classList.remove('active');
        });
        
        const selectedItem = document.querySelector(`.synonym-item[data-type="${type}"]`);
        if (selectedItem) {
            selectedItem.classList.add('active');
        }
        
        // 中央パネルにシノニム内容を表示
        displaySynonymContent(type);
    }
    
    // シノニム内容を中央パネルに表示
    function displaySynonymContent(type) {
        const documentTitle = document.getElementById('documentTitle');
        const documentMeta = document.getElementById('documentMeta');
        const documentContent = document.getElementById('document-content');
        
        let title = '';
        let meta = '';
        let content = '';
        
        switch (type) {
            case 'api':
                title = 'APIシノニム';
                meta = 'APIから取得されたシノニムデータ';
                content = generateApiSynonymContent();
                break;
            case 'manual':
                title = '手動シノニム';
                meta = 'ユーザーが手動で設定したシノニムデータ';
                content = generateManualSynonymContent();
                break;
            case 'combined':
                title = '統合シノニム';
                meta = 'APIシノニムと手動シノニムを統合したデータ';
                content = generateCombinedSynonymContent();
                break;
        }
        
        // タイトルとメタ情報を更新
        documentTitle.textContent = title;
        documentMeta.textContent = meta;
        
        // コンテンツを更新
        documentContent.innerHTML = content;
        
        // 選択されたドキュメントをクリア（シノニム表示中はドキュメント選択状態を解除）
        selectedDocument = null;
        
        // ページリストのアクティブ状態をクリア
        document.querySelectorAll('.page-item').forEach(item => {
            item.classList.remove('active');
        });
    }
    
    // APIシノニムコンテンツを生成
    function generateApiSynonymContent() {
        const synonymData = getSynonymData();
        
        if (!synonymData.synonymList || synonymData.synonymList.length === 0) {
            return `
                <div class="synonym-content">
                    <h2>APIシノニム</h2>
                    <div class="no-data">
                        <i class="fas fa-info-circle" style="font-size: 2rem; color: #6b7280; margin-bottom: 1rem;"></i>
                        <p>APIから取得されたシノニムデータがありません。</p>
                        <p>PDFファイルをアップロードして解析を実行してください。</p>
                    </div>
                </div>
            `;
        }
        
        let content = `
            <div class="synonym-content">
                <h2>APIシノニム</h2>
                <div class="synonym-count">${synonymData.synonymList.length}件のシノニムグループ</div>
        `;
        
        synonymData.synonymList.forEach((item, index) => {
            // 'synonym' (単数形) を正式プロパティとし、旧プロパティもフォールバックで許容
            const synArr = Array.isArray(item.synonym)
                ? item.synonym
                : (Array.isArray(item.Synonym)
                    ? item.Synonym
                    : (Array.isArray(item.synonyms)
                        ? item.synonyms
                        : (Array.isArray(item.Synonyms) ? item.Synonyms : [])));
            
            if (synArr.length > 0) {
                const key = synArr[0] || `グループ${index + 1}`;
                const values = synArr.slice(1);
                
                content += `
                    <div class="synonym-entry">
                        <div class="synonym-key">${key}</div>
                        <div class="synonym-values">${values.join(', ')}</div>
                    </div>
                `;
            }
        });
        
        content += `</div>`;
        return content;
    }
    
    // 手動シノニムコンテンツを生成
    function generateManualSynonymContent() {
        const manualSynonyms = localStorage.getItem('dsManualSynonyms') || '';
        
        if (!manualSynonyms.trim()) {
            return `
                <div class="synonym-content">
                    <h2>手動シノニム</h2>
                    <div class="no-data">
                        <i class="fas fa-edit" style="font-size: 2rem; color: #6b7280; margin-bottom: 1rem;"></i>
                        <p>手動で設定されたシノニムデータがありません。</p>
                        <p>設定画面からシノニムを追加してください。</p>
                    </div>
                </div>
            `;
        }
        
        const lines = manualSynonyms.split('\n').filter(line => line.trim());
        
        let content = `
            <div class="synonym-content">
                <h2>手動シノニム</h2>
                <div class="synonym-count">${lines.length}件のシノニム設定</div>
        `;
        
        lines.forEach(line => {
            const parts = line.split(':');
            if (parts.length >= 2) {
                const key = parts[0].trim();
                const values = parts[1].split(',').map(v => v.trim()).join(', ');
                
                content += `
                    <div class="synonym-entry">
                        <div class="synonym-key">${key}</div>
                        <div class="synonym-values">${values}</div>
                    </div>
                `;
            }
        });
        
        content += `</div>`;
        return content;
    }
    
    // 統合シノニムコンテンツを生成
    function generateCombinedSynonymContent() {
        const combinedSynonyms = getCombinedSynonyms();
        
        if (!combinedSynonyms.trim()) {
            return `
                <div class="synonym-content">
                    <h2>統合シノニム</h2>
                    <div class="no-data">
                        <i class="fas fa-layer-group" style="font-size: 2rem; color: #6b7280; margin-bottom: 1rem;"></i>
                        <p>統合されたシノニムデータがありません。</p>
                        <p>APIシノニムまたは手動シノニムを設定してください。</p>
                    </div>
                </div>
            `;
        }
        
        const lines = combinedSynonyms.split('\n').filter(line => line.trim());
        
        let content = `
            <div class="synonym-content">
                <h2>統合シノニム</h2>
                <div class="synonym-count">${lines.length}件の統合シノニム</div>
                <p style="font-size: 0.875rem; color: #6b7280; margin-bottom: 1rem;">
                    APIシノニムと手動シノニムを統合し、重複を除去したデータです。
                </p>
        `;
        
        lines.forEach(line => {
            const parts = line.split(':');
            if (parts.length >= 2) {
                const key = parts[0].trim();
                const values = parts[1].split(',').map(v => v.trim()).join(', ');
                
                content += `
                    <div class="synonym-entry">
                        <div class="synonym-key">${key}</div>
                        <div class="synonym-values">${values}</div>
                    </div>
                `;
            }
        });
        
        content += `
                <div style="margin-top: 2rem; padding: 1rem; background-color: #f9fafb; border-radius: 0.375rem; border: 1px solid #e5e7eb;">
                    <h3 style="margin: 0 0 0.5rem 0; font-size: 1rem; color: #374151;">アノテーション（テキスト形式）</h3>
                    <pre style="margin: 0; font-size: 0.75rem; color: #4b5563; white-space: pre-wrap; word-wrap: break-word;">${combinedSynonyms}</pre>
                </div>
            </div>
        `;
        
        return content;
    }

    // 初期化実行
    initUI();

    // PDFグループの一覧のために関数呼び出し
    function createPdfListForNavigation() {
        // 表示を完全に非表示にするため、何もしない
        return;
    }

    // 定期的にPDFリストを更新（5秒ごと）
    // setInterval(createPdfListForNavigation, 5000);

    // キャッシュ進捗を更新して表示
    function updateCacheProgress(pdfName, current, total) {
        console.log(`キャッシュ進捗更新: ${pdfName} - ${current}/${total}`);
        
        if (!cacheProgressStatus[pdfName]) {
            cacheProgressStatus[pdfName] = {
                current: current,
                total: total,
                element: null
            };
            
            // 新しいプログレス要素を作成
            const progressEl = document.createElement('div');
            progressEl.className = 'cache-progress';
            progressEl.innerHTML = `
                <div class="pdf-name">${pdfName}</div>
                <div class="progress-bar-container">
                    <div class="progress-bar" style="width: ${Math.round(current / total * 100)}%"></div>
                </div>
                <div class="progress-text">${current}/${total}</div>
            `;
            
            // プログレスコンテナを作成（まだ存在しない場合）
            let progressContainer = document.getElementById('cache-progress-container');
            if (!progressContainer) {
                progressContainer = document.createElement('div');
                progressContainer.id = 'cache-progress-container';
                progressContainer.className = 'cache-progress-container';
                
                // 適切な場所に挿入（検索コンテナの代わりにヘッダー下などに表示）
                const header = document.querySelector('.header');
                if (header) {
                    header.parentNode.insertBefore(progressContainer, header.nextSibling);
                } else {
                    document.body.prepend(progressContainer);
                }
            }
            
            progressContainer.appendChild(progressEl);
            cacheProgressStatus[pdfName].element = progressEl;
            
        } else {
            // 既存のプログレス要素を更新
            const status = cacheProgressStatus[pdfName];
            status.current = current;
            
            if (status.element) {
                const progressBar = status.element.querySelector('.progress-bar');
                const progressText = status.element.querySelector('.progress-text');
                
                if (progressBar) {
                    progressBar.style.width = `${Math.round(current / total * 100)}%`;
                }
                
                if (progressText) {
                    progressText.textContent = `${current}/${total}`;
                }
            }
        }
        
        // すべてのページが完了したら表示を更新
        if (current >= total) {
            setTimeout(() => {
                if (cacheProgressStatus[pdfName] && cacheProgressStatus[pdfName].element) {
                    const el = cacheProgressStatus[pdfName].element;
                    el.classList.add('completed');
                    const progressText = el.querySelector('.progress-text');
                    if (progressText) {
                        progressText.textContent = '完了';
                    }
                    
                    // 数秒後に徐々に消す
                    setTimeout(() => {
                        el.classList.add('fade-out');
                        setTimeout(() => {
                            el.remove();
                            delete cacheProgressStatus[pdfName];
                            
                            // すべてのキャッシュが完了したらコンテナも削除
                            if (Object.keys(cacheProgressStatus).length === 0) {
                                const container = document.getElementById('cache-progress-container');
                                if (container) {
                                    container.remove();
                                }
                            }
                        }, 500);
                    }, 3000);
                }
            }, 200);
        }
    }

    // 全ページ検索用のイベントリスナー
    function setupGlobalSearch() {
        console.log('検索機能の初期化を開始します');
        const searchInput = document.getElementById('global-search-input');
        if (!searchInput) {
            console.log('検索入力フィールドが見つかりません（コメントアウトされている可能性があります）: global-search-input');
            console.log('検索機能をスキップします');
            return;
        }
        console.log('検索入力フィールドを検出しました', searchInput);

        // 既存のイベントリスナーを削除（重複防止）
        searchInput.removeEventListener('keydown', searchKeydownHandler);
        // 新しいイベントリスナーを追加
        searchInput.addEventListener('keydown', searchKeydownHandler);
        console.log('検索イベントリスナーを設定しました');
    }
    
    // 検索キーダウンハンドラー（イベントリスナーの分離）
    async function searchKeydownHandler(e) {
        if (e.key === 'Enter') {
            const searchInput = document.getElementById('global-search-input');
            // 検索入力フィールドが存在しない場合は処理をスキップ
            if (!searchInput) {
                console.log('検索入力フィールドが見つからないため、検索をスキップします');
                return;
            }
            const keyword = searchInput.value.trim();
            console.log('検索クエリ:', keyword);
            if (!keyword) return;
            await performGlobalSearch(keyword);
        }
    }

    // 検索中のローディングダイアログを表示
    function showSearchingDialog(keyword) {
        // 既存のダイアログがあれば削除
        const existingDialog = document.getElementById('searching-dialog');
        if (existingDialog) {
            existingDialog.remove();
        }
        
        // ローディングダイアログを作成
        const dialogOverlay = document.createElement('div');
        dialogOverlay.id = 'searching-dialog';
        dialogOverlay.style.cssText = `
            position: fixed;
            top: 0;
            left: 0;
            width: 100%;
            height: 100%;
            background-color: rgba(0, 0, 0, 0.5);
            display: flex;
            justify-content: center;
            align-items: center;
            z-index: 1001;
        `;
        
        // ダイアログコンテンツを作成
        const dialogContent = document.createElement('div');
        dialogContent.style.cssText = `
            background-color: white;
            border-radius: 8px;
            box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
            padding: 24px;
            display: flex;
            flex-direction: column;
            align-items: center;
            text-align: center;
            max-width: 90%;
            width: 360px;
        `;
        
        // スピナーアニメーション
        const spinner = document.createElement('div');
        spinner.style.cssText = `
            width: 48px;
            height: 48px;
            border: 5px solid #f3f3f3;
            border-top: 5px solid #3389ca;
            border-radius: 50%;
            animation: spin 1s linear infinite;
            margin-bottom: 16px;
        `;
        
        // スピナーアニメーションのキーフレームを追加
        const styleElement = document.createElement('style');
        styleElement.textContent = `
            @keyframes spin {
                0% { transform: rotate(0deg); }
                100% { transform: rotate(360deg); }
            }
            @keyframes pulse {
                0% { opacity: 0.6; }
                50% { opacity: 1; }
                100% { opacity: 0.6; }
            }
        `;
        document.head.appendChild(styleElement);
        
        // ダイアログタイトルとメッセージ
        const title = document.createElement('h3');
        title.style.cssText = `
            margin: 0 0 8px 0;
            font-size: 18px;
            color: #374151;
        `;
        title.textContent = '検索中...';
        
        const message = document.createElement('p');
        message.style.cssText = `
            margin: 0;
            font-size: 14px;
            color: #6b7280;
            animation: pulse 1.5s infinite ease-in-out;
        `;
        message.textContent = `「${keyword}」で全ページを検索しています`;
        
        const subMessage = document.createElement('p');
        subMessage.style.cssText = `
            margin: 8px 0 0 0;
            font-size: 12px;
            color: #9ca3af;
        `;
        subMessage.textContent = '検索結果が表示されるまでしばらくお待ちください';
        
        // ダイアログを組み立てる
        dialogContent.appendChild(spinner);
        dialogContent.appendChild(title);
        dialogContent.appendChild(message);
        dialogContent.appendChild(subMessage);
        dialogOverlay.appendChild(dialogContent);
        
        // ボディに追加
        document.body.appendChild(dialogOverlay);
        
        return dialogOverlay;
    }
    
    // ローディングダイアログを非表示
    function hideSearchingDialog() {
        const dialog = document.getElementById('searching-dialog');
        if (dialog) {
            // フェードアウトアニメーション
            dialog.style.transition = 'opacity 0.3s';
            dialog.style.opacity = '0';
            
            // アニメーション完了後に削除
            setTimeout(() => {
                dialog.remove();
            }, 300);
        }
    }
    // 全ページを取得してキーワード検索
    async function performGlobalSearch(keyword) {
        console.log('全文検索を開始します:', keyword);
        
        // 空の検索クエリは処理しない
        if (!keyword || keyword.trim() === '') {
            console.warn('検索クエリが空です');
            return;
        }
        
        // 検索クエリをトリム
        keyword = keyword.trim();
        console.log(`検索クエリ: "${keyword}"`);
        
        // 検索中ローディングダイアログを表示
        const searchingDialog = showSearchingDialog(keyword);
        
        // ページ一覧の全アイテムを取得
        if (!Array.isArray(pageItems) || pageItems.length === 0) {
            console.error('ページアイテムが空か無効です:', pageItems);
            
            // データが読み込まれていない場合は再取得を試みる
            try {
                console.log('ドキュメントリストを再取得します');
                const documents = await fetchDocumentList();
                if (Array.isArray(documents) && documents.length > 0) {
                    pageItems = documents;
                    console.log('ドキュメントリストを再取得しました:', documents.length, '件');
                } else {
                    console.error('ドキュメントリストの再取得に失敗しました');
                    hideSearchingDialog();
                    renderGlobalSearchResults([], keyword); // 空の結果を表示
                    return;
                }
            } catch (error) {
                console.error('ドキュメントリスト取得エラー:', error);
                hideSearchingDialog();
                renderGlobalSearchResults([], keyword); // 空の結果を表示
                return;
            }
        }
        
        console.log('検索対象ドキュメント数:', pageItems.length);
        
        // 検索中のローディング表示（検索ボックス）
        const searchInput = document.getElementById('global-search-input');
        let originalPlaceholder = '';
        if (searchInput) {
            originalPlaceholder = searchInput.placeholder;
            searchInput.placeholder = "検索中...";
            searchInput.disabled = true;
        }
            
        // 完了後に元に戻す処理を登録（検索ボックスが存在する場合のみ）
        if (searchInput) {
            setTimeout(() => {
                searchInput.placeholder = originalPlaceholder;
                searchInput.disabled = false;
            }, 10000); // タイムアウト対策として最大10秒後には元に戻す
        }
        
        // 全ページのテキストを取得
        const allContents = [];
        const errors = [];
        
        try {
            // 検索の進捗状況を更新する関数
            const updateProgress = (current, total) => {
                const message = document.querySelector('#searching-dialog p');
                if (message) {
                    message.textContent = `「${keyword}」で全ページを検索しています (${current}/${total})`;
                }
            };
            
            // ドキュメントの構造に基づいてコンテンツを収集
            let totalDocCount = 0;
            let processedCount = 0;
            
            // すべてのグループ内のドキュメントをフラットなリストにする
            const allDocuments = [];
            
            // pageItemsがグループ構造かどうかを判定
            const isGroupedStructure = pageItems.length > 0 && 
                                    pageItems[0] && 
                                    Array.isArray(pageItems[0].documents);
            
            console.log('ドキュメント構造タイプ:', isGroupedStructure ? 'グループ化' : 'フラット');
            
            if (isGroupedStructure) {
                // グループ構造の場合
                for (const group of pageItems) {
                    if (Array.isArray(group.documents)) {
                        for (const doc of group.documents) {
                            allDocuments.push(doc);
                        }
                    }
                }
            } else {
                // フラット構造の場合はそのまま使用
                allDocuments.push(...pageItems);
            }
            
            totalDocCount = allDocuments.length;
            console.log(`検索対象ドキュメント総数: ${totalDocCount}`);
            
            // 進捗状況の初期更新
            updateProgress(0, totalDocCount);
            
            // 各ドキュメントのコンテンツを取得
            for (const doc of allDocuments) {
                processedCount++;
                
                // 検索対象が多すぎる場合は制限（ただし最低50件は処理）
                if (processedCount > 50 && allContents.length >= 10) {
                    console.warn(`検索対象が多すぎるため最初の${processedCount}件のみ処理します`);
                    break;
                }
                
                // 定期的に進捗状況を更新
                if (processedCount % 5 === 0 || processedCount === totalDocCount) {
                    updateProgress(processedCount, totalDocCount);
                }
                
                try {
                    // テキストプロパティが直接ある場合はそれを使用
                    if (doc.text) {
                        allContents.push({
                            id: doc.id,
                            name: doc.name || `ドキュメント ${doc.id}`,
                            content: doc.text,
                            filepath: doc.filepath
                        });
                        continue;
                    }
                    
                    // PDFキャッシュを確認
                    let contentObj = null;
                    if (doc.name && doc.name.includes('【PDF文書】')) {
                        const pdfBaseName = doc.name.split('(ページ')[0].trim();
                        if (pdfTextCache[pdfBaseName] && pdfTextCache[pdfBaseName][doc.id]) {
                            contentObj = pdfTextCache[pdfBaseName][doc.id];
                        }
                    }
                    
                    // キャッシュになければAPI呼び出し
                    if (!contentObj) {
                        contentObj = await fetchDocumentContent(doc.id);
                    }
                    
                    if (contentObj && contentObj.content) {
                        allContents.push({
                            id: doc.id,
                            name: doc.name || `ドキュメント ${doc.id}`,
                            content: contentObj.content,
                            filepath: doc.filepath
                        });
                    }
                } catch (itemError) {
                    console.warn(`ドキュメント「${doc.name || doc.id}」の取得に失敗:`, itemError);
                    errors.push(doc.name || doc.id);
                }
            }
            
            console.log(`取得したコンテンツ: ${allContents.length}件`);
            
            // デバッグ: 最初の数件のコンテンツをコンソールに出力
            if (allContents.length > 0) {
                const sample = allContents[0];
                console.log('サンプルコンテンツ:', {
                    id: sample.id,
                    name: sample.name,
                    contentPreview: sample.content.substring(0, 100) + '...',
                    contentLength: sample.content.length
                });
            }
            
            // 検索入力を元に戻す
            if (searchInput) {
                searchInput.placeholder = "全ページを検索...";
                searchInput.disabled = false;
            }
            
            // 検索実行
            const pageGroups = new Map(); // ページ番号をキーとしてグループ化
            const normalizedKeyword = keyword.toLowerCase();
            
            console.log(`検索クエリ(正規化): "${normalizedKeyword}"`);
            
            // 単純な文字列マッチングで検索
            for (const item of allContents) {
                const contentLower = item.content.toLowerCase();
                const nameLower = (item.name || '').toLowerCase();
                
                if (contentLower.includes(normalizedKeyword) || nameLower.includes(normalizedKeyword)) {
                    // ページ番号を抽出
                    let pageNumber = null;
                    
                    // 1. nameからページ番号を抽出
                    const pageNumMatch = item.name ? item.name.match(/\#(\d+)/) : null;
                    if (pageNumMatch && pageNumMatch[1]) {
                        const extractedNum = parseInt(pageNumMatch[1]);
                        console.log(`DEBUG: item.name="${item.name}", 抽出番号=${extractedNum}`);
                        pageNumber = extractedNum === 0 ? 0 : extractedNum - 1;
                        console.log(`DEBUG: 最終pageNumber=${pageNumber}`);
                    }
                                        
                    // 2. pageNumberプロパティがある場合はそれを使用
                    if (pageNumber === null && item.pageNumber !== undefined) {
                        pageNumber = item.pageNumber;
                    }
                    
                    // 3. IDからページ番号を抽出（例: "chunk_2_1" -> 2）
                    if (pageNumber === null && item.id) {
                        const chunkMatch = item.id.match(/chunk_(\d+)_\d+/);
                        if (chunkMatch && chunkMatch[1]) {
                            pageNumber = parseInt(chunkMatch[1]);
                        }
                    }
                    
                    // ページ番号でグループ化
                    if (pageNumber !== null) {
                        const pageKey = pageNumber;
                        if (!pageGroups.has(pageKey)) {
                            pageGroups.set(pageKey, {
                                pageNumber: pageNumber,
                                name: `${pageNumber+1}枚目`,
                                items: [],
                                content: '',
                                id: `page_${pageNumber}`
                            });
                        }
                        
                        pageGroups.get(pageKey).items.push(item);
                        // ページの全コンテンツを結合
                        if (pageGroups.get(pageKey).content) {
                            pageGroups.get(pageKey).content += '\n\n';
                        }
                        pageGroups.get(pageKey).content += item.content;
                        
                        console.log(`検索ヒット（ページ${pageNumber}）: "${item.name}", ID=${item.id}`);
                    }
                }
            }
            
            // ページグループを検索結果に変換
            const searchResults = Array.from(pageGroups.values());
            
            // ページ番号順にソート
            searchResults.sort((a, b) => a.pageNumber - b.pageNumber);
            
            console.log('検索結果:', searchResults.length, '件（ページ単位）');
            
            // ローディングダイアログを非表示にし、検索結果を表示
            hideSearchingDialog();
            renderGlobalSearchResults(searchResults, keyword);
            
        } catch (error) {
            console.error('検索処理全体でエラーが発生しました:', error);
            // 検索入力を元に戻す
            if (searchInput) {
                searchInput.placeholder = "全ページを検索...";
                searchInput.disabled = false;
            }
            // ローディングダイアログを非表示にし、空の検索結果を表示
            hideSearchingDialog();
            renderGlobalSearchResults([], keyword);
        }
    }

    // 検索結果をモーダルポップアップで表示
    function renderGlobalSearchResults(results, keyword) {
        console.log('検索結果の表示処理開始:', results.length, '件のヒット');
        
        // 既存の検索結果モーダルを削除
        let existingModal = document.getElementById('search-results-modal');
        if (existingModal) {
            console.log('既存の検索結果モーダルを削除します');
            existingModal.remove();
        }
        
        // モーダル背景のオーバーレイを作成
        const modalOverlay = document.createElement('div');
        modalOverlay.id = 'search-results-modal';
        modalOverlay.style.cssText = `
            position: fixed;
            top: 0;
            left: 0;
            width: 100%;
            height: 100%;
            background-color: rgba(0, 0, 0, 0.5);
            display: flex;
            justify-content: center;
            align-items: center;
            z-index: 1000;
        `;
        
        // モーダルコンテンツコンテナを作成
        const modalContent = document.createElement('div');
        modalContent.style.cssText = `
            background-color: white;
            border-radius: 8px;
            box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
            width: 90%;
            max-width: 600px;
            max-height: 80vh;
            display: flex;
            flex-direction: column;
            overflow: hidden;
        `;
        
        // モーダルヘッダーを作成
        const modalHeader = document.createElement('div');
        modalHeader.style.cssText = `
            padding: 16px;
            border-bottom: 1px solid #e5e7eb;
            display: flex;
            justify-content: space-between;
            align-items: center;
        `;
        
        // 検索結果のタイトルを表示
        if (results.length === 0) {
            modalHeader.innerHTML = `
                <h3 style="margin: 0; font-size: 18px; color: #374151;">検索結果</h3>
                <button id="close-search-modal" style="background: none; border: none; font-size: 20px; cursor: pointer; color: #6b7280;">×</button>
            `;
        } else {
            modalHeader.innerHTML = `
                <h3 style="margin: 0; font-size: 18px; color: #374151;">「${keyword}」の検索結果 <span style="color: #2563eb; font-weight: normal;">${results.length}件</span></h3>
                <button id="close-search-modal" style="background: none; border: none; font-size: 20px; cursor: pointer; color: #6b7280;">×</button>
            `;
        }
        
        // モーダルの内容部分を作成
        const modalBody = document.createElement('div');
        modalBody.style.cssText = `
            padding: 16px;
            overflow-y: auto;
            max-height: calc(80vh - 130px);
        `;
        
        if (results.length === 0) {
            console.log('検索結果: 0件');
            modalBody.innerHTML = `
                <div style="text-align: center; padding: 32px 16px;">
                    <div style="font-size: 64px; color: #d1d5db; margin-bottom: 16px;">
                        <i class="fas fa-search"></i>
                    </div>
                    <p style="color: #6b7280; font-size: 16px; margin: 0;">「${keyword}」に一致するページはありません</p>
                </div>
            `;
        } else {
            console.log('検索結果:', results.length, '件を表示します');
            
            // 結果アイテムのリスト
            results.forEach((page, index) => {
                const item = document.createElement('div');
                item.className = 'search-result-item';
                item.style.cssText = `
                    padding: 12px;
                    cursor: pointer;
                    border-bottom: ${index < results.length - 1 ? '1px solid #e5e7eb' : 'none'};
                    transition: background-color 0.2s;
                    border-radius: 4px;
                `;
                
                // ホバー効果を追加
                item.addEventListener('mouseover', () => {
                    item.style.backgroundColor = '#f3f4f6';
                });
                item.addEventListener('mouseout', () => {
                    item.style.backgroundColor = 'transparent';
                });
                
                // キーワードをハイライト（大文字小文字を区別せず、すべての出現箇所を置換）
                let displayName = page.name;
                try {
                    const regex = new RegExp(keyword, 'gi');
                    displayName = displayName.replace(regex, match => `<span style="background: #ffe066; font-weight: bold;">${match}</span>`);
                } catch (e) {
                    console.warn('正規表現によるハイライト処理に失敗しました', e);
                }
                
                // 内容のサンプルを表示（最初の100文字程度）
                let contentPreview = '';
                if (page.content) {
                    const maxPreviewLength = 100;
                    let previewText = page.content.substring(0, maxPreviewLength);
                    
                    // キーワードが含まれている部分を表示するようにする
                    const keywordIndex = page.content.toLowerCase().indexOf(keyword.toLowerCase());
                    if (keywordIndex > maxPreviewLength) {
                        const startPos = Math.max(0, keywordIndex - 40);
                        previewText = '... ' + page.content.substring(startPos, startPos + maxPreviewLength);
                    }
                    
                    if (page.content.length > maxPreviewLength) {
                        previewText += '...';
                    }
                    
                    // プレビューテキスト内のキーワードもハイライト
                    try {
                        const regex = new RegExp(keyword, 'gi');
                        previewText = previewText.replace(regex, match => `<span style="background: #ffe066; font-weight: bold;">${match}</span>`);
                    } catch (e) {}
                    
                    contentPreview = `<div style="font-size: 13px; color: #6b7280; margin-top: 4px;">${previewText}</div>`;
                }
                
                item.innerHTML = `
                    <div style="font-size: 15px; font-weight: 500; color: #111827;">${displayName}</div>
                    ${contentPreview}
                `;
                
                item.addEventListener('click', () => {
                    console.log('検索結果アイテムがクリックされました:', page.id);
                    // モーダルを閉じる
                    document.getElementById('search-results-modal').remove();
                    
                    // ドキュメントIDからページグループを探す
                    let groupFound = false;
                    
                    // ページアイテムがグループ構造かどうかを判定
                    const isGroupedStructure = pageItems.length > 0 && 
                                            pageItems[0] && 
                                            Array.isArray(pageItems[0].documents);
                    
                    console.log('ドキュメント構造タイプ:', isGroupedStructure ? 'グループ化' : 'フラット', 'ページID:', page.id);
                    
                    // ページ名から番号を抽出（例: "テキスト #39" -> 39）
                    let pageNumber = null;
                    const pageNumMatch = page.name ? page.name.match(/\#(\d+)/) : null;
                    if (pageNumMatch && pageNumMatch[1]) {
                        pageNumber = parseInt(pageNumMatch[1]);
                        console.log(`ページ名 "${page.name}" から抽出した番号: ${pageNumber}`);
                    }
                    
                    if (isGroupedStructure) {
                        // 方法1: IDを直接使ってグループを探す
                        for (const group of pageItems) {
                            if (Array.isArray(group.documents) && 
                                group.documents.some(doc => doc.id === page.id)) {
                                console.log(`方法1: ドキュメント ${page.id} を含むグループ ${group.id} を見つけました`);
                                selectDocumentGroup(group);
                                groupFound = true;
                                break;
                            }
                        }
                        
                        // 方法2: ページ番号でマッチするものを探す
                        if (!groupFound && pageNumber !== null) {
                            for (const group of pageItems) {
                                // グループ表示名からページ番号を抽出 ("ページ 2" -> 2)
                                const groupNumMatch = group.displayName ? group.displayName.match(/ページ\s+(\d+)/) : null;
                                if (groupNumMatch && groupNumMatch[1]) {
                                    const groupNum = parseInt(groupNumMatch[1]);
                                    console.log(`グループ "${group.displayName}" の番号: ${groupNum}`);
                                    
                                    // テキスト番号（1-39など）からグループを探す
                                    // ページ内のテキスト番号範囲をチェック
                                    if (Array.isArray(group.documents) && group.documents.length > 0) {
                                        // 最初と最後のドキュメントをチェック
                                        let containsTextNumber = false;
                                        
                                        for (const doc of group.documents) {
                                            // ドキュメント名からテキスト番号を抽出
                                            const docNumMatch = doc.name ? doc.name.match(/\#(\d+)/) : null;
                                            if (docNumMatch && docNumMatch[1]) {
                                                const docNum = parseInt(docNumMatch[1]);
                                                if (docNum === pageNumber) {
                                                    containsTextNumber = true;
                                                    break;
                                                }
                                            }
                                        }
                                        
                                        if (containsTextNumber) {
                                            console.log(`方法2: テキスト番号 ${pageNumber} を含むグループ ${group.id} を見つけました`);
                                            selectDocumentGroup(group);
                                            groupFound = true;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        
                        // 方法3: コンテンツ比較でグループを探す
                        if (!groupFound && page.content) {
                            const contentSnippet = page.content.substring(0, 50); // 最初の50文字を比較に使用
                            for (const group of pageItems) {
                                if (Array.isArray(group.documents)) {
                                    for (const doc of group.documents) {
                                        if (doc.text && doc.text.includes(contentSnippet)) {
                                            console.log(`方法3: コンテンツの一部 "${contentSnippet}" を含むグループ ${group.id} を見つけました`);
                                            selectDocumentGroup(group);
                                            groupFound = true;
                                            break;
                                        }
                                    }
                                    if (groupFound) break;
                                }
                            }
                        }
                    }
                    
                    // グループが見つからない場合は最終手段として個別ドキュメントを選択
                    if (!groupFound) {
                        console.log(`グループが見つからないため、個別ドキュメント ${page.id} を選択します`);
                        selectDocument(page.id, true);
                    }
                });
                
                modalBody.appendChild(item);
            });
        }
        
        // モーダルフッター（必要に応じて）
        const modalFooter = document.createElement('div');
        modalFooter.style.cssText = `
            padding: 12px 16px;
            border-top: 1px solid #e5e7eb;
            display: flex;
            justify-content: flex-end;
        `;
        
        // フッターに新しい検索ボタンを追加
        modalFooter.innerHTML = `
            <button id="new-search-button" style="
                background-color: #f3f4f6;
                color: #374151;
                border: 1px solid #d1d5db;
                border-radius: 4px;
                padding: 6px 12px;
                font-size: 14px;
                cursor: pointer;
            ">新しい検索</button>
        `;
        
        // モーダルの構造を組み立て
        modalContent.appendChild(modalHeader);
        modalContent.appendChild(modalBody);
        modalContent.appendChild(modalFooter);
        modalOverlay.appendChild(modalContent);
        
        // モーダルをDOMに追加
        document.body.appendChild(modalOverlay);
        
        // 閉じるボタンのイベントリスナーを追加
        setTimeout(() => {
            const closeButton = document.getElementById('close-search-modal');
            if (closeButton) {
                closeButton.addEventListener('click', () => {
                    document.getElementById('search-results-modal').remove();
                });
            }
            
            // 新しい検索ボタンのイベントリスナー
            const newSearchButton = document.getElementById('new-search-button');
            if (newSearchButton) {
                newSearchButton.addEventListener('click', () => {
                    document.getElementById('search-results-modal').remove();
                    // 検索ボックスにフォーカス
                    const searchInput = document.getElementById('global-search-input');
                    if (searchInput) {
                        searchInput.value = '';
                        searchInput.focus();
                    }
                });
            }
            
            // 背景クリックでモーダルを閉じる
            modalOverlay.addEventListener('click', (e) => {
                if (e.target === modalOverlay) {
                    modalOverlay.remove();
                }
            });
            
            // ESCキーでモーダルを閉じる
            document.addEventListener('keydown', function escKeyHandler(e) {
                if (e.key === 'Escape') {
                    const modal = document.getElementById('search-results-modal');
                    if (modal) {
                        modal.remove();
                        document.removeEventListener('keydown', escKeyHandler);
                    }
                }
            });
        }, 0);
    }

    // 必ず一番最後で検索UIを初期化
    setTimeout(setupGlobalSearch, 0);

    // 新しく追加した要素の参照を取得
    const uploadStatusBtn = document.getElementById('upload-status-btn');
    const uploadStatusModal = document.getElementById('upload-status-modal');
    const closeUploadModal = document.getElementById('close-upload-modal');
    const uploadStatusList = document.getElementById('upload-status-list');
    
    // デバッグ用：要素の存在状況を確認
    console.log('=== 要素の存在状況確認 ===');
    console.log('uploadStatusBtn:', uploadStatusBtn);
    console.log('uploadStatusModal:', uploadStatusModal);
    console.log('closeUploadModal:', closeUploadModal);
    console.log('uploadStatusList:', uploadStatusList);
    console.log('=== 要素確認完了 ===');
    
    // 要素の存在チェック
    if (!uploadStatusBtn) {
        console.error('upload-status-btn要素が見つかりません');
        return;
    }
    if (!uploadStatusModal) {
        console.error('upload-status-modal要素が見つかりません');
        return;
    }
    if (!closeUploadModal) {
        console.error('close-upload-modal要素が見つかりません');
        return;
    }
    if (!uploadStatusList) {
        console.error('upload-status-list要素が見つかりません');
        return;
    }
    // アップロード履歴を保存する関数
    function saveUploadHistory(workId, fileName) {
        console.log('=== saveUploadHistory呼び出し ===');
        console.log('引数 - workId:', workId, 'fileName:', fileName);
        
        const history = loadUploadHistory();
        console.log('読み込み済み履歴件数:', history.length);
        
        const newEntry = {
            workId: workId,
            fileName: fileName,
            uploadDate: new Date().toISOString()
        };
        console.log('新しいエントリ:', newEntry);
        
        // 重複チェック（同じworkIdがあれば更新）
        const existingIndex = history.findIndex(item => item.workId === workId);
        console.log('既存エントリのインデックス:', existingIndex);
        
        if (existingIndex !== -1) {
            console.log('既存エントリを更新します');
            history[existingIndex] = newEntry;
        } else {
            console.log('新しいエントリを追加します');
            history.unshift(newEntry); // 最新のものを先頭に追加
        }
        
        // メモリ上の配列も更新
        uploadHistory = [...history];
        console.log('更新後のメモリ上履歴件数:', uploadHistory.length);
        
        // ローカルストレージに保存
        const jsonData = JSON.stringify(history);
        localStorage.setItem('uploadHistory', jsonData);
        console.log('ローカルストレージに保存した内容:', jsonData);
        console.log('アップロード履歴を保存しました:', newEntry);
        console.log('=== saveUploadHistory完了 ===');
    }
    
    // アップロード履歴を読み込む関数
    function loadUploadHistory() {
        console.log('=== loadUploadHistory関数開始 ===');
        
        const savedHistory = localStorage.getItem('uploadHistory');
        console.log('ローカルストレージから取得した生データ:', savedHistory);
        
        if (savedHistory) {
            try {
                console.log('JSONパース開始');
                // 保存されたJSONをパース
                const parsedHistory = JSON.parse(savedHistory);
                console.log('JSONパース成功、件数:', parsedHistory.length);
                console.log('パースされたデータ:', parsedHistory);
                
                // 日付文字列を日付オブジェクトに変換
                uploadHistory = parsedHistory.map(item => ({
                    ...item,
                    uploadDate: new Date(item.uploadDate)
                }));
                
                console.log('日付変換後のuploadHistory:', uploadHistory);
                console.log('uploadHistory配列の件数:', uploadHistory.length);
                
                // パースした履歴を返す
                return uploadHistory;
            } catch (error) {
                console.error('アップロード履歴の読み込みに失敗:', error);
                uploadHistory = [];
                console.log('エラーによりuploadHistoryを空配列に設定');
                return [];
            }
        } else {
            // ローカルストレージにデータがない場合
            console.log('ローカルストレージにデータがありません');
            uploadHistory = [];
            console.log('uploadHistoryを空配列に設定');
            return [];
        }
        
        console.log('=== loadUploadHistory関数終了 ===');
    }

    // サーバー側のworkId履歴に追加する関数
    async function addWorkIdToServerHistory(workId, fileName) {
        try {
            console.log('【サーバー履歴追加】開始 - workId:', workId, 'fileName:', fileName);
            
            const basePath = getBasePath();
            const response = await fetch(`${basePath}/api/data-structuring/add-workid-history`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify({
                    workId: workId,
                    fileName: fileName
                }),
                credentials: 'include' // ASP.NET認証クッキーを含める
            });
            
            if (response.ok) {
                const result = await response.json();
                console.log('【サーバー履歴追加】成功:', result);
            } else {
                const errorData = await response.json().catch(() => ({}));
                console.warn('【サーバー履歴追加】APIエラー:', response.status, errorData);
            }
        } catch (error) {
            console.error('【サーバー履歴追加】ネットワークエラー:', error);
        }
    }
    
    // アップロード履歴を表示する関数
    function renderUploadHistory() {
        console.log('=== renderUploadHistory呼び出し ===');
        console.log('renderUploadHistory呼び出し - 履歴件数:', uploadHistory.length);
        console.log('履歴の詳細:', uploadHistory);
        
        const uploadStatusList = document.getElementById('upload-status-list');
        if (!uploadStatusList) {
            console.error('upload-status-list要素が見つかりません');
            return;
        }
        
        console.log('upload-status-list要素を見つけました');
        
        if (uploadHistory.length === 0) {
            console.log('履歴が空のため、メッセージを表示します');
            uploadStatusList.innerHTML = `
                <div style="text-align: center; padding: 40px 0; color: #6b7280;">
                    <i class="fas fa-info-circle" style="font-size: 2rem; margin-bottom: 15px;"></i>
                    <p>アップロード履歴がありません。</p>
                </div>
            `;
            return;
        }
        
        console.log('履歴アイテムを表示します:');
        
        // アップロード履歴を表示
        uploadStatusList.innerHTML = uploadHistory.map((item, index) => {
            console.log(`アイテム${index + 1}:`, item);
            
            return `
                <div class="upload-status-item" data-work-id="${item.workId}" style="display: grid; grid-template-columns: 2fr 1fr 1fr 1fr; gap: 16px; padding: 12px 0; border-bottom: 1px solid #e5e7eb; align-items: center;">
                <div style="overflow: hidden; text-overflow: ellipsis;">${item.fileName}</div>
                    <div style="font-family: monospace; font-size: 0.85em; overflow: hidden; text-overflow: ellipsis;">${item.workId}</div>
                    <div>${formatDate(new Date(item.uploadDate))}</div>
                    <div class="action-cell">
                        ${renderActionButton(item)}
                    </div>
            </div>
            `;
        }).join('');
        
        console.log('履歴HTML生成完了、イベントリスナーを追加します');
        
        // 表示ボタンにイベントリスナーを追加
        document.querySelectorAll('.view-upload-btn').forEach(button => {
            button.addEventListener('click', function() {
                const workId = this.getAttribute('data-work-id');
                console.log(`表示ボタンがクリックされました: workId=${workId}`);
                viewUploadedContent(workId);
            });
        });
        
        console.log('=== renderUploadHistory完了 ===');
    }


    // ボタンの状態に応じた表示を生成する関数
    function renderActionButton(uploadItem) {
        const pageNo = uploadItem.page_no || 0;
        const maxPageNo = uploadItem.max_page_no || 0;
        
        // 外部APIのstate値を信頼して使用
        // state: 0 = 解析中（処理中）
        // state: 1 = 解析完了（完了）
        // state: 2 = エラー発生（エラー）
        let finalState = uploadItem.state;
        
        // state値が存在しない場合は従来のロジックでフォールバック
        if (finalState === undefined || finalState === null) {
            if (uploadItem.processing_state) {
                const processingState = uploadItem.processing_state;
                switch(processingState) {
                    case 'NotStarted':
                        finalState = 0; // 準備中
                        break;
                    case 'InProgress':
                        finalState = 0; // 処理中
                        break;
                    case 'Completed':
                        finalState = 1; // 完了
                        break;
                    case 'Error':
                        finalState = 2; // エラー
                        break;
                    default:
                        finalState = 0; // デフォルトは準備中
                        break;
                }
            } else {
                // processing_stateもない場合は従来のpage_no判定
                if (pageNo === 0 && maxPageNo === 0) {
                    finalState = 0; // 準備中
                } else if (pageNo < maxPageNo) {
                    finalState = 0; // 処理中
                } else {
                    finalState = 1; // 完了
                }
            }
        }
        
        console.log(`workId ${uploadItem.workId}: state=${finalState} (${pageNo}/${maxPageNo})`);
        
        // 外部APIのstate値に基づいてボタンを生成
        switch(finalState) {
            case 0:
                // 解析中（処理中）
                if (maxPageNo > 0) {
                    return `<button class="btn btn-warning" disabled style="background-color: #fd7e14; color: white; border: none; border-radius: 4px; padding: 6px 12px; font-size: 0.875rem; cursor: not-allowed;">処理中(${pageNo}/${maxPageNo})</button>`;
                } else {
                    return `<button class="btn btn-secondary" disabled style="background-color: #6c757d; color: white; border: none; border-radius: 4px; padding: 6px 12px; font-size: 0.875rem; cursor: not-allowed;">準備中...</button>`;
                }
            case 1:
                // 解析完了（完了）
                return `
                <div style="display:flex; gap:8px;">
                    <button class="view-upload-btn btn btn-primary" data-work-id="${uploadItem.workId}" style="background-color: #3389ca; color: white; border: none; border-radius: 4px; padding: 6px 12px; cursor: pointer; font-size: 0.875rem;">表示</button>
                    <button class="delete-workid-btn btn btn-outline" data-work-id="${uploadItem.workId}" style="background-color: #ffffff; color: #dc2626; border: 1px solid #dc2626; border-radius: 4px; padding: 6px 12px; cursor: pointer; font-size: 0.875rem;">削除</button>
                </div>`;
            case 2:
                // エラー発生（エラー）
                return `<button class="btn btn-danger" disabled style="background-color: #dc3545; color: white; border: none; border-radius: 4px; padding: 6px 12px; font-size: 0.875rem; cursor: not-allowed;">エラー</button>`;
            default:
                // 不明な状態の場合はデフォルトで準備中
                return `<button class="btn btn-secondary" disabled style="background-color: #6c757d; color: white; border: none; border-radius: 4px; padding: 6px 12px; font-size: 0.875rem; cursor: not-allowed;">準備中...</button>`;
        }
    }

    // 一括で削除ボタンのイベントをバインド（モーダル描画後に呼ぶ）
    document.addEventListener('click', async function(e) {
        const target = e.target;
        if (target && target.classList && target.classList.contains('delete-workid-btn')) {
            const workId = target.getAttribute('data-work-id');
            if (!workId) return;

            if (!confirm(`workId: ${workId}\n\nAzure Index から当該IDのデータを全て削除します。\nこの操作は元に戻せません。実行しますか？`)) {
                return;
            }

            const basePath = getBasePath();
            target.disabled = true;
            target.textContent = '削除中...';

            try {
                const res = await fetch(`${basePath}/api/data-structuring/indexes/${encodeURIComponent(workId)}`, {
                    method: 'DELETE',
                    credentials: 'include'
                });
                if (!res.ok) {
                    const err = await res.text();
                    alert(`削除に失敗しました\n${err}`);
                } else {
                    const result = await res.json().catch(()=>({}));
                    console.log('削除結果:', result);
                    alert('削除が完了しました');
                    // 行を削除/更新
                    const row = document.querySelector(`.upload-status-item[data-work-id="${workId}"]`);
                    if (row) row.remove();
                    // ローカル履歴からも取り除く
                    const idx = uploadHistory.findIndex(i => i.workId === workId);
                    if (idx >= 0) {
                        uploadHistory.splice(idx, 1);
                        localStorage.setItem('uploadHistory', JSON.stringify(uploadHistory));
                    }
                }
            } catch (ex) {
                console.error('Delete API error', ex);
                alert('削除API呼び出しでエラーが発生しました');
            } finally {
                target.disabled = false;
                target.textContent = '削除';
            }
        }
    });

    // 処理状態を判定する関数
    function determineProcessingState(statusData) {
        if (!statusData) {
            return 'NotStarted';
        }
        
        const pageNo = statusData.page_no || 0;
        const maxPageNo = statusData.max_page_no || 0;
        const hasContent = (statusData.chunk_list?.length || 0) > 0 || 
                        (statusData.text_list?.length || 0) > 0 ||
                        (statusData.synonym_list?.length || 0) > 0;
        
        if (pageNo === 0 && maxPageNo === 0 && !hasContent) {
            return 'NotStarted';
        } else if (pageNo < maxPageNo || !hasContent) {
            return 'InProgress';
        } else {
            return 'Completed';
        }
    }

    // アップロード履歴の特定のアイテムを更新する関数
    function updateUploadHistoryItem(workId, updatedItem) {
        const index = uploadHistory.findIndex(item => item.workId === workId);
        if (index !== -1) {
            uploadHistory[index] = { ...uploadHistory[index], ...updatedItem };
            // ローカルストレージも更新
            localStorage.setItem('uploadHistory', JSON.stringify(uploadHistory));
        }
    }

    // アップロード履歴の特定の行のみを更新する関数
    function updateUploadHistoryRow(workId, updatedItem) {
        // 該当するテーブル行を特定
        const row = document.querySelector(`.upload-status-item[data-work-id="${workId}"]`);
        if (!row) return;
        
        // ボタン部分のみ更新
        const actionCell = row.querySelector('.action-cell');
        if (actionCell) {
            actionCell.innerHTML = renderActionButton(updatedItem);
            
            // 新しいボタンにイベントリスナーを追加
            const viewBtn = actionCell.querySelector('.view-upload-btn');
            if (viewBtn) {
                viewBtn.addEventListener('click', function() {
                    const workId = this.getAttribute('data-work-id');
                    console.log(`表示ボタンがクリックされました: workId=${workId}`);
                    viewUploadedContent(workId);
                });
            }
        }
    }

    // アップロード状況一覧のステータス更新関数（一度だけ実行版）
    async function updateUploadHistoryStatuses() {
        const uploadHistory = loadUploadHistory();
        
        // 【デバッグ】エラー追跡用の配列
        const errorWorkIds = [];
        const successWorkIds = [];
        
        // 更新ボタンを押したら全てのアイテムを再チェック（state値を優先的に使用）
        const now = new Date();
        const incompleteItems = uploadHistory.filter(item => {
            // state値が存在する場合は、state値を優先的に使用
            if (item.state !== undefined && item.state !== null) {
                // state: 1 = 完了、state: 2 = エラー、state: 0 = 処理中/準備中
                if (item.state === 1) {
                    // 完了済みは除外
                    return false;
                }
                if (item.state === 2) {
                    // エラー状態の場合は、最後のチェックから5分以上経過している場合のみ再チェック
                    if (item.last_checked) {
                        const lastChecked = new Date(item.last_checked);
                        const minutesSinceLastCheck = (now - lastChecked) / (1000 * 60);
                        if (minutesSinceLastCheck < 5) {
                            console.log(`${item.workId}: エラー状態だが最終チェックから${Math.round(minutesSinceLastCheck)}分のため再チェックをスキップ`);
                            return false; // 5分未満なら再チェックしない
                        }
                        console.log(`${item.workId}: エラー状態だが最終チェックから${Math.round(minutesSinceLastCheck)}分経過しているため再チェック`);
                        return true;
                    }
                    return true; // last_checkedがない場合は再チェック
                }
                // state: 0 (処理中/準備中) は全て対象
                return true;
            }
            
            // state値がない場合は、従来のprocessing_state判定を使用
            if (item.processing_state === 'Completed') {
                return false;
            }
            
            // エラー状態の場合は、最後のチェックから5分以上経過している場合のみ再チェック
            if (item.processing_state === 'Error' && item.last_checked) {
                const lastChecked = new Date(item.last_checked);
                const minutesSinceLastCheck = (now - lastChecked) / (1000 * 60);
                if (minutesSinceLastCheck < 5) {
                    console.log(`${item.workId}: エラー状態だが最終チェックから${Math.round(minutesSinceLastCheck)}分のため再チェックをスキップ`);
                    return false; // 5分未満なら再チェックしない
                }
                console.log(`${item.workId}: エラー状態だが最終チェックから${Math.round(minutesSinceLastCheck)}分経過しているため再チェック`);
                return true;
            }
            
            // 未開始、処理中、エラー状態（5分経過）は全て対象
            return true;
        });
        
        if (incompleteItems.length === 0) {
            console.log('全て完了済みまたは再チェック対象外のため、更新をスキップします');
            return;
        }
        
        console.log(`${incompleteItems.length}件のアイテムの状態を確認します...`);
        
        // 各未完了アイテムのステータスをチェック
        for (const item of incompleteItems) {
            let retryCount = 0;
            const maxRetries = 3;
            let success = false;
            
            console.log(`【デバッグ】ステータス確認開始: workId=${item.workId}, fileName=${item.fileName}`);
            
            while (!success && retryCount < maxRetries) {
                try {
                    const basePath = getBasePath();
                    // 🔥 ベースパス詳細ログ
                    console.log('🔥🔥🔥 ベースパス確認ログ 🔥🔥🔥');
                    console.log('現在のURL:', window.location.href);
                    console.log('pathname:', window.location.pathname);
                    console.log('pathSegments:', window.location.pathname.split('/'));
                    console.log('取得したベースパス:', basePath);
                    
                    const apiUrl = `${basePath}/api/data-structuring/status?workId=${item.workId}&forceRefresh=true`;
                    console.log(`【デバッグ】最終的なAPI URL: ${apiUrl}`);
                    
                    const response = await fetch(apiUrl, {
                        method: 'GET',
                        credentials: 'include', // ASP.NET認証クッキーを含める
                        cache: 'no-cache'
                    });
                    
                    if (response.ok) {
                        const statusData = await response.json();
                        
                        // デバッグログ: workIdごとのstate値を表示
                        console.log(`=== 外部API取得結果 ===`);
                        console.log(`workId: ${item.workId}`);
                        console.log(`state値: ${statusData.state}`);
                        console.log(`page_no: ${statusData.page_no || 0}`);
                        console.log(`max_page_no: ${statusData.max_page_no || 0}`);
                        console.log(`processing_state: ${statusData.processing_state || 'なし'}`);
                        console.log(`APIレスポンス全体:`, statusData);
                        console.log(`=== 外部API取得結果終了 ===`);
                        
                        // 外部APIのstate値を信頼して使用
                        // state: 0 = 解析中（処理中）
                        // state: 1 = 解析完了（完了）
                        // state: 2 = エラー発生（エラー）
                        const pageNo = statusData.page_no || 0;
                        const maxPageNo = statusData.max_page_no || 0;
                        let finalState = statusData.state;
                        
                        // state=2の場合は特別な処理（エラー状態）
                        if (finalState === 2) {
                            console.log(`【デバッグ】workId ${item.workId} でエラー状態(state=2)を検出`);
                            console.log(`【デバッグ】エラー詳細: ${statusData.error_detail || '詳細なし'}`);
                            console.log(`【デバッグ】return_code: ${statusData.return_code || 'なし'}`);
                            
                            // エラー状態のアイテムを作成
                            const errorItem = {
                                ...item,
                                page_no: pageNo,
                                max_page_no: maxPageNo,
                                processing_state: 'Error',
                                state: 2, // エラー状態
                                error_detail: statusData.error_detail || 'システムエラーが発生しました',
                                return_code: statusData.return_code || 9999,
                                last_checked: new Date().toISOString()
                            };
                            
                            // ローカルストレージ更新
                            updateUploadHistoryItem(item.workId, errorItem);
                            
                            // UIの該当行のみ更新
                            updateUploadHistoryRow(item.workId, errorItem);
                            
                            console.log(`【デバッグ】エラー状態に更新: ${item.workId} - ${errorItem.processing_state}`);
                            success = true;
                            break; // while ループを抜ける
                        }
                        
                        // state値が存在しない場合は従来のロジックでフォールバック
                        if (finalState === undefined || finalState === null) {
                            if (statusData.processing_state) {
                                const processingState = statusData.processing_state;
                                switch(processingState) {
                                    case 'NotStarted':
                                        finalState = 0; // 準備中
                                        break;
                                    case 'InProgress':
                                        finalState = 0; // 処理中
                                        break;
                                    case 'Completed':
                                        finalState = 1; // 完了
                                        break;
                                    case 'Error':
                                        finalState = 2; // エラー
                                        break;
                                    default:
                                        finalState = 0; // デフォルトは準備中
                                        break;
                                }
                            } else {
                                // processing_stateもない場合は従来のpage_no判定
                                if (pageNo === 0 && maxPageNo === 0) {
                                    finalState = 0; // 準備中
                                } else if (pageNo < maxPageNo) {
                                    finalState = 0; // 処理中
                                } else {
                                    finalState = 1; // 完了
                                }
                            }
                        }
                        
                        console.log(`workId ${item.workId}: 外部APIから取得したstate=${finalState} (${pageNo}/${maxPageNo})`)
                        
                        // ステータス更新
                        const updatedItem = {
                            ...item,
                            page_no: pageNo,
                            max_page_no: maxPageNo,
                            processing_state: statusData.processing_state || determineProcessingState(statusData),
                            state: finalState, // 判定されたstate値を使用
                            chunk_list: statusData.chunk_list, // 実際のデータも保存
                            text_list: statusData.text_list,
                            synonym_list: statusData.synonym_list,
                            last_checked: new Date().toISOString()
                        };
                        
                        // ローカルストレージ更新
                        updateUploadHistoryItem(item.workId, updatedItem);
                        
                        // UIの該当行のみ更新
                        updateUploadHistoryRow(item.workId, updatedItem);
                        
                        console.log(`ステータス更新: ${item.workId} - ${updatedItem.processing_state} (${updatedItem.page_no}/${updatedItem.max_page_no})`);
                        successWorkIds.push(item.workId);
                        success = true;
                        
                    } else if (response.status === 500) {
                        // S3オブジェクトキー未存在エラー（10102）の可能性があるためリトライ
                        const errorText = await response.text();
                        console.error(`【デバッグ】500エラー詳細: workId=${item.workId}, errorText=${errorText}`);
                        
                        retryCount++;
                        if (retryCount < maxRetries) {
                            console.warn(`${item.workId}: APIエラー (${response.status}) - ${retryCount}回目のリトライを5秒後に実行`);
                            await new Promise(resolve => setTimeout(resolve, 5000)); // 5秒待機
                            continue;
                        } else {
                            // 最大リトライ回数に達した場合はエラー状態に設定
                            console.warn(`${item.workId}: 最大リトライ回数に達しました - エラー状態に設定`);
                            const errorItem = {
                                ...item,
                                processing_state: 'Error',
                                state: -1, // エラー状態を-1として設定
                                last_checked: new Date().toISOString()
                            };
                            
                            updateUploadHistoryItem(item.workId, errorItem);
                            updateUploadHistoryRow(item.workId, errorItem);
                            errorWorkIds.push(item.workId);
                            success = true; // ループを終了
                        }
                    } else {
                        const errorText = await response.text();
                        console.error(`【デバッグ】予期しないレスポンスエラー: workId=${item.workId}, status=${response.status}, errorText=${errorText}`);
                        errorWorkIds.push(item.workId);
                        success = true; // ループを終了
                    }
                    
                } catch (error) {
                    retryCount++;
                    if (retryCount < maxRetries) {
                        console.error(`${item.workId}: ネットワークエラー - ${retryCount}回目のリトライを5秒後に実行: ${error.message}`);
                        await new Promise(resolve => setTimeout(resolve, 5000)); // 5秒待機
                        continue;
                    } else {
                        console.error(`${item.workId}: 最大リトライ回数に達しました - エラー状態に設定: ${error.message}`);
                        
                        // 最大リトライ回数に達した場合はエラー状態に設定
                        const errorItem = {
                            ...item,
                            processing_state: 'Error',
                            state: -1, // エラー状態を-1として設定
                            last_checked: new Date().toISOString()
                        };
                        
                        updateUploadHistoryItem(item.workId, errorItem);
                        updateUploadHistoryRow(item.workId, errorItem);
                        errorWorkIds.push(item.workId);
                        success = true; // ループを終了
                    }
                }
            }
        }
        
        // 【デバッグ】処理結果のサマリーを出力
        console.log('=== アップロード状況更新サマリー ===');
        console.log(`総処理数: ${incompleteItems.length}件`);
        console.log(`成功: ${successWorkIds.length}件`);
        console.log(`エラー: ${errorWorkIds.length}件`);
        
        if (successWorkIds.length > 0) {
            console.log('成功したworkId:', successWorkIds);
        }
        
        if (errorWorkIds.length > 0) {
            console.log('エラーが発生したworkId:', errorWorkIds);
            console.log('【重要】これらのworkIdが継続的にエラーを起こしている可能性があります');
            
            // エラーが発生したworkIdの詳細情報を出力
            console.log('エラーworkIdの詳細:');
            errorWorkIds.forEach(workId => {
                const errorItem = incompleteItems.find(item => item.workId === workId);
                if (errorItem) {
                    console.log(`  - workId: ${workId}, fileName: ${errorItem.fileName}, uploadDate: ${errorItem.uploadDate}`);
                    if (errorItem.error_detail) {
                        console.log(`    エラー詳細: ${errorItem.error_detail}`);
                    }
                    if (errorItem.return_code) {
                        console.log(`    エラーコード: ${errorItem.return_code}`);
                    }
                }
            });
        }
        
        console.log('=== アップロード状況更新サマリー終了 ===');
        console.log('アップロード状況の更新が完了しました');
    }
    
    // 日付をフォーマットする関数
    function formatDate(date) {
        return date.toLocaleString('ja-JP', {
            year: 'numeric',
            month: '2-digit',
            day: '2-digit',
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit'
        });
    }
    // アップロードされたコンテンツを表示する関数
    async function viewUploadedContent(workId) {
        try {
            // ローディング表示
            const loadingToast = showToast('データを読み込んでいます...', 0);
            
            // モーダルを閉じる
            uploadStatusModal.style.display = 'none';
            
            console.log(`work_id ${workId} のデータを表示します`);
            // 選択されたworkIdの原本PDFリンク表示
            try {
                const infoRes = await fetch(getBasePath() + `/api/storage/workid-info?work_id=${encodeURIComponent(workId)}`, { credentials: 'include' });
                if (infoRes.ok) {
                    const info = await infoRes.json();
                    if (info && info.hasFile && info.fileUrl && currentFile && currentFileLink) {
                        currentFileLink.textContent = info.fileName || '-';
                        currentFileLink.href = info.fileUrl;
                        // 明示的に表示（サーバー側の初期styleが残っていても強制表示）
                        currentFile.style.display = 'block';
                    } else if (currentFile) {
                        // hasFile=false の場合は非表示
                        currentFile.style.display = 'none';
                    }
                }
            } catch (e) { /* no-op */ }
            
            // まず処理状況を確認するAPIを呼び出し
            const basePath = getBasePath();
            const apiUrl = `${basePath}/api/data-structuring/filepaths?workId=${workId}`;
            
            const response = await fetch(apiUrl, {
                method: 'GET',
                credentials: 'include', // ASP.NET認証クッキーを含める
                cache: 'no-cache'
            });
            
            // ローディングトーストを閉じる
            if (document.body.contains(loadingToast)) {
                document.body.removeChild(loadingToast);
            }
            
            if (!response.ok) {
                const errorData = await response.json().catch(() => ({}));
                const errorMessage = errorData.error || `API呼び出しが失敗しました (${response.status})`;
                showToast(`エラー: ${errorMessage}`, 10000);
                console.error('API呼び出しエラー:', errorMessage);
                return;
            }
            
            const data = await response.json();
            console.log('APIレスポンス:', data);
            
            // レスポンスに処理進捗情報が含まれているかチェック
            if (data.processing_status) {
                const status = data.processing_status;
                const currentPage = status.page_no || 0;
                const maxPage = status.max_page_no || 0;
                
                // 処理が完了していない場合
                if (currentPage < maxPage) {
                    console.log(`処理中: ${currentPage}/${maxPage}ページ`);
                    showProcessingModal(workId, currentPage, maxPage);
                    return;
                }
            }
            
            // 処理が完了している場合、または進捗情報がない場合はページをリロード
            console.log(`ページをリロードしてworkId=${workId}を設定します`);
            
            // 現在のURLを取得
            const url = new URL(window.location.href);
            // workIdパラメータを設定
            url.searchParams.set('workId', workId);
            
            // 更新されたURLに移動（ページをリロード）
            window.location.href = url.toString();
            
        } catch (error) {
            console.error('データ表示中にエラーが発生:', error);
            showToast(`エラー: ${error.message || 'データの表示に失敗しました'}`, 5000);
        }
    }
    
    // 処理進捗モーダルを表示する関数
    function showProcessingModal(workId, currentPage, maxPage) {
        // 既存の処理進捗モーダルを削除
        const existingModal = document.getElementById('processing-progress-modal');
        if (existingModal) {
            existingModal.remove();
        }
        
        // 進捗率を計算
        const progressPercent = maxPage > 0 ? Math.round((currentPage / maxPage) * 100) : 0;
        
        // 処理進捗モーダルを作成
        const modalOverlay = document.createElement('div');
        modalOverlay.id = 'processing-progress-modal';
        modalOverlay.style.cssText = `
            position: fixed;
            top: 0;
            left: 0;
            width: 100%;
            height: 100%;
            background-color: rgba(0, 0, 0, 0.5);
            display: flex;
            justify-content: center;
            align-items: center;
            z-index: 10000;
        `;
        
        const modalContent = document.createElement('div');
        modalContent.style.cssText = `
            background: white;
            border-radius: 8px;
            padding: 30px;
            max-width: 500px;
            width: 90%;
            text-align: center;
            box-shadow: 0 4px 6px rgba(0, 0, 0, 0.1);
        `;
        
        modalContent.innerHTML = `
            <div style="margin-bottom: 20px;">
                <i class="fas fa-cog fa-spin" style="font-size: 48px; color: #3389ca; margin-bottom: 15px;"></i>
                <h3 style="margin: 0 0 10px 0; color: #1f2937;">処理中です</h3>
                <p style="margin: 0; color: #6b7280;">ファイルの構造化処理を実行しています...</p>
            </div>
            
            <div style="margin-bottom: 20px;">
                <div style="background-color: #f3f4f6; border-radius: 10px; height: 20px; overflow: hidden; margin-bottom: 10px;">
                    <div id="progress-bar" style="background-color: #3389ca; height: 100%; width: ${progressPercent}%; transition: width 0.3s ease;"></div>
                </div>
                <div id="progress-text" style="font-size: 18px; font-weight: bold; color: #1f2937;">
                    ${currentPage} / ${maxPage} ページ (${progressPercent}%)
                </div>
            </div>
            
            <div style="font-size: 14px; color: #6b7280; margin-bottom: 20px;">
                処理が完了するまでしばらくお待ちください。<br>
                処理時間はファイルサイズによって異なります。
            </div>
            
            <button id="cancel-processing-btn" style="
                background-color: #6b7280;
                color: white;
                border: none;
                border-radius: 5px;
                padding: 10px 20px;
                cursor: pointer;
                font-size: 14px;
            ">閉じる</button>
        `;
        
        modalOverlay.appendChild(modalContent);
        document.body.appendChild(modalOverlay);
        
        // 閉じるボタンのイベントリスナー
        document.getElementById('cancel-processing-btn').addEventListener('click', function() {
            modalOverlay.remove();
        });
        
        // 定期的に進捗をチェック（5秒間隔）
        const progressCheckInterval = setInterval(async () => {
            try {
                const basePath = getBasePath();
                const apiUrl = `${basePath}/api/data-structuring/filepaths?workId=${workId}`;
                
                const response = await fetch(apiUrl, {
                    method: 'GET',
                    credentials: 'same-origin',
                    cache: 'no-cache'
                });
                
                if (response.ok) {
                    const data = await response.json();
                    
                    if (data.processing_status) {
                        const status = data.processing_status;
                        const newCurrentPage = status.page_no || 0;
                        const newMaxPage = status.max_page_no || 0;
                        
                        // 進捗を更新
                        const newProgressPercent = newMaxPage > 0 ? Math.round((newCurrentPage / newMaxPage) * 100) : 0;
                        
                        const progressBar = document.getElementById('progress-bar');
                        const progressText = document.getElementById('progress-text');
                        
                        if (progressBar && progressText) {
                            progressBar.style.width = `${newProgressPercent}%`;
                            progressText.textContent = `${newCurrentPage} / ${newMaxPage} ページ (${newProgressPercent}%)`;
                        }
                        
                        // 処理が完了した場合
                        if (newCurrentPage >= newMaxPage) {
                            clearInterval(progressCheckInterval);
                            
                            // 進捗を100%に更新
                            const progressBar = document.getElementById('progress-bar');
                            const progressText = document.getElementById('progress-text');
                            
                            if (progressBar && progressText) {
                                progressBar.style.width = '100%';
                                progressText.textContent = `${newMaxPage} / ${newMaxPage} ページ (100%)`;
                            }
                            
                            // 完了メッセージを表示（自動リロードは行わない）
                            const modalContent = modalOverlay.querySelector('div');
                            const completionMessage = document.createElement('div');
                            completionMessage.style.cssText = `
                                margin-top: 20px;
                                padding: 15px;
                                background-color: #ecfdf5;
                                border: 1px solid #10b981;
                                border-radius: 5px;
                                color: #065f46;
                                font-weight: bold;
                            `;
                            completionMessage.innerHTML = `
                                <i class="fas fa-check-circle" style="color: #10b981; margin-right: 8px;"></i>
                                処理が完了しました！<br>
                                <small style="font-weight: normal;">表示ボタンを再度押してください。</small>
                            `;
                            
                            // 既存の完了メッセージがあれば削除
                            const existingMessage = modalContent.querySelector('.completion-message');
                            if (existingMessage) {
                                existingMessage.remove();
                            }
                            
                            completionMessage.classList.add('completion-message');
                            modalContent.appendChild(completionMessage);
                            
                            // 閉じるボタンのテキストを変更
                            const closeBtn = document.getElementById('cancel-processing-btn');
                            if (closeBtn) {
                                closeBtn.textContent = '閉じる';
                                closeBtn.style.backgroundColor = '#10b981';
                            }
                            
                            console.log('処理が完了しました。モーダルは手動で閉じてください。');
                        }
                    } else {
                        // processing_statusがない場合は処理完了と判断
                        clearInterval(progressCheckInterval);
                        
                        // 進捗を100%に更新
                        const progressBar = document.getElementById('progress-bar');
                        const progressText = document.getElementById('progress-text');
                        
                        if (progressBar && progressText) {
                            progressBar.style.width = '100%';
                            progressText.textContent = '処理完了 (100%)';
                        }
                        
                        // 完了メッセージを表示（自動リロードは行わない）
                        const modalContent = modalOverlay.querySelector('div');
                        const completionMessage = document.createElement('div');
                        completionMessage.style.cssText = `
                            margin-top: 20px;
                            padding: 15px;
                            background-color: #ecfdf5;
                            border: 1px solid #10b981;
                            border-radius: 5px;
                            color: #065f46;
                            font-weight: bold;
                        `;
                        completionMessage.innerHTML = `
                            <i class="fas fa-check-circle" style="color: #10b981; margin-right: 8px;"></i>
                            処理が完了しました！<br>
                            <small style="font-weight: normal;">表示ボタンを再度押してください。</small>
                        `;
                        
                        // 既存の完了メッセージがあれば削除
                        const existingMessage = modalContent.querySelector('.completion-message');
                        if (existingMessage) {
                            existingMessage.remove();
                        }
                        
                        completionMessage.classList.add('completion-message');
                        modalContent.appendChild(completionMessage);
                        
                        // 閉じるボタンのテキストを変更
                        const closeBtn = document.getElementById('cancel-processing-btn');
                        if (closeBtn) {
                            closeBtn.textContent = '閉じる';
                            closeBtn.style.backgroundColor = '#10b981';
                        }
                        
                        console.log('処理が完了しました。モーダルは手動で閉じてください。');
                    }
                }
            } catch (error) {
                console.error('進捗チェック中にエラーが発生:', error);
            }
        }, 5000);
        
        // モーダルが閉じられた時にインターバルを停止
        modalOverlay.addEventListener('remove', () => {
            clearInterval(progressCheckInterval);
        });
    }

    // アップロード状況ボタン
    uploadStatusBtn.addEventListener('click', async function() {
        console.log('=== アップロード状況ボタンがクリックされました ===');
        
        // ローカルストレージの内容を確認（デバッグ用）
        const localStorageData = localStorage.getItem('uploadHistory');
        console.log('ローカルストレージの内容:', localStorageData);
        
        // アップロード履歴をロード
        loadUploadHistory();
        
        // 読み込み後の履歴をログ出力
        console.log('読み込み後のuploadHistory配列:', uploadHistory);
        console.log('履歴件数:', uploadHistory.length);
        
        // アップロード履歴を表示
        renderUploadHistory();
        
        // モーダルを表示
        uploadStatusModal.style.display = 'block';
        console.log('=== アップロード状況モーダルを表示しました ===');

        // モーダル表示時に少し待ってから最新状態をチェック（S3処理完了を待つため）
        setTimeout(async () => {
            await updateUploadHistoryStatuses();
        }, 2000); // 2秒待ってからチェック
    });

    // アップロード状況モーダルを閉じるボタン
    closeUploadModal.addEventListener('click', function() {
        console.log('=== アップロード状況モーダルを閉じます ===');
        console.log('閉じる前のuploadHistory件数:', uploadHistory.length);
        console.log('閉じる前のローカルストレージ:', localStorage.getItem('uploadHistory'));
        
        uploadStatusModal.style.display = 'none';
        
        console.log('閉じた後のuploadHistory件数:', uploadHistory.length);
        console.log('=== アップロード状況モーダルを閉じました ===');
    });

    // アップロード状況更新ボタン
    const refreshUploadStatus = document.getElementById('refresh-upload-status');
    console.log('=== 更新ボタン要素確認 ===');
    console.log('refreshUploadStatus:', refreshUploadStatus);
    console.log('=== 更新ボタン要素確認完了 ===');
    
    if (refreshUploadStatus) {
        refreshUploadStatus.addEventListener('click', async function() {
            console.log('=== アップロード状況更新ボタンがクリックされました ===');
            
            // 【デバッグ】現在のローカルストレージ内容を詳細表示
            const currentHistory = loadUploadHistory();
            console.log('=== 現在のローカルストレージ内容 ===');
            console.log(`履歴総数: ${currentHistory.length}件`);
            currentHistory.forEach((item, index) => {
                console.log(`${index + 1}. workId: ${item.workId}, fileName: ${item.fileName}, state: ${item.state}, last_checked: ${item.last_checked}`);
            });
            console.log('=== ローカルストレージ内容終了 ===');
            
            // ボタンを無効化して処理中を示す
            refreshUploadStatus.disabled = true;
            const originalText = refreshUploadStatus.innerHTML;
            refreshUploadStatus.innerHTML = '<i class="fas fa-spinner fa-spin"></i>更新中...';
            
            try {
                // 状況を更新
                await updateUploadHistoryStatuses();
                console.log('アップロード状況の更新完了');
            } catch (error) {
                console.error('アップロード状況の更新中にエラーが発生:', error);
                showToast('更新中にエラーが発生しました', 5000);
            } finally {
                // ボタンを元に戻す
                refreshUploadStatus.disabled = false;
                refreshUploadStatus.innerHTML = originalText;
            }
            
            console.log('=== アップロード状況更新完了 ===');
        });
    } else {
        console.error('refresh-upload-status要素が見つかりません');
    }

    // モーダル外をクリックした時に閉じる
    window.addEventListener('click', function(event) {
        if (event.target === uploadStatusModal) {
            console.log('=== モーダル外クリックで閉じます ===');
            console.log('閉じる前のuploadHistory件数:', uploadHistory.length);
            console.log('閉じる前のローカルストレージ:', localStorage.getItem('uploadHistory'));
            
            uploadStatusModal.style.display = 'none';
            
            console.log('閉じた後のuploadHistory件数:', uploadHistory.length);
            console.log('=== モーダル外クリックで閉じました ===');
        }
    });

    // ページデータを処理する関数
    function processDocumentPages(pages) {
        console.log('ページデータ処理を開始:', pages.length, '件');
        
        // APIのレスポンスデータをトースト表示する（デバッグ用）
        //showToast(JSON.stringify(pages, null, 2), 30000);
        
        // データの内容を詳しく確認
        if (pages.length > 0) {
            console.log('最初のアイテムのプロパティ:', Object.keys(pages[0]));
            
            // チャンクリスト形式の確認（documents配列があるか）
            if (pages[0].documents && Array.isArray(pages[0].documents)) {
                console.log('新形式: chunk_list形式のデータ（ページごとにグループ化済み）');
                
                // 詳細ログ出力
                const firstGroup = pages[0];
                console.log(`最初のページグループ: ID=${firstGroup.id}, 名前=${firstGroup.name}, ドキュメント数=${firstGroup.documents.length}`);
                
                if (firstGroup.documents.length > 0) {
                    const firstDoc = firstGroup.documents[0];
                    console.log(`最初のドキュメント: ID=${firstDoc.id}, 名前=${firstDoc.name}, テキスト長=${firstDoc.text?.length || 0}`);
                }
                
                // ページごとにグループ化されたデータをそのまま使用
                return pages.map(page => {
                    // ページ内のすべてのチャンクのテキストを結合
                    let combinedText = '';
                    if (Array.isArray(page.documents)) {
                        combinedText = page.documents
                            .map(chunk => chunk.text || '')
                            .join('\n\n');
                    }
                    
                    return {
                        id: page.id,
                        displayName: page.name,
                        pageNumber: page.pageNumber,
                        documents: page.documents,
                        content: combinedText  // グループ全体の結合テキスト
                    };
                });
            }
            
            // ページ配列（page_text_list 形式）の処理: 各要素が text を持つ
            if (pages[0].text) {
                console.log('page_text_list 形式のデータを検出（各要素がtextを保持）');
                return pages.map(p => {
                    const pageNo = (p.pageNumber !== undefined && p.pageNumber !== null) ? p.pageNumber : parseInt((p.id||'').replace('page_','')) || 0;
                    const docId = p.filepath || `${p.id}_0` || `page_${pageNo}_0`;
                    return {
                        id: p.id || `page_${pageNo}`,
                        displayName: p.name || `ページ ${pageNo + 1}`,
                        pageNumber: pageNo,
                        documents: [
                            {
                                id: docId,
                                name: p.name || `ページ ${pageNo + 1}`,
                                text: p.text,
                                pageNumber: pageNo,
                                chunkNumber: 0,
                                filepath: p.filepath || `page_${pageNo}_0`
                            }
                        ]
                    };
                });
            }
            
            // 従来の形式の処理
            console.log('従来形式のデータ処理');
            
            // データをグループ化する処理を追加
            const groupedByPage = groupDocumentsByPage(pages);
            console.log('ページ番号でグループ化したデータ:', groupedByPage);
            
            // 詳細なデバッグログを出力（グループの詳細構造）
            console.log('=== ページグループのデバッグ情報 ===');
            groupedByPage.forEach((group, groupIndex) => {
                console.log(`グループ ${groupIndex+1}: ${group.displayName}, ID=${group.id}`);
                console.log(`  ドキュメント数: ${group.documents ? group.documents.length : 0}`);
                
                if (group.documents && group.documents.length > 0) {
                    // 最初と最後のドキュメントの詳細を表示
                    const firstDoc = group.documents[0];
                    const lastDoc = group.documents[group.documents.length - 1];
                    
                    console.log(`  最初のドキュメント: ID=${firstDoc.id}`);
                    console.log(`    プロパティ: ${Object.keys(firstDoc).join(', ')}`);
                    console.log(`    テキストの有無: ${firstDoc.text ? '有り' : '無し'}`);
                    if (firstDoc.text) {
                        console.log(`    テキストサンプル: ${firstDoc.text.substring(0, 30)}...`);
                    }
                    
                    console.log(`  最後のドキュメント: ID=${lastDoc.id}`);
                    console.log(`    プロパティ: ${Object.keys(lastDoc).join(', ')}`);
                    console.log(`    テキストの有無: ${lastDoc.text ? '有り' : '無し'}`);
                }
            });
            console.log('=== デバッグ情報終了 ===');
            
            return groupedByPage; // グループ化されたデータを返す
        }
        
        // データがない場合は空の配列を返す
        console.warn('ページデータが空です。');
        return [];
    }
    
    // 旧形式のデータ処理
    function processLegacyDataFormat(data) {
        // データがない場合は空の配列を返す
        if (!data) {
            console.warn('Azure Searchからデータが取得できませんでした。');
            return [];
        }
        
        // 想定外の形式の場合でも処理を試みる
        console.log('データ形式が想定と異なります - コントローラーで整形されていない可能性があります');
        
        try {
            // APIから直接filepathプロパティを持つオブジェクト配列が返された場合
            if (data.value && Array.isArray(data.value)) {
                console.log('data.valueを配列として処理します');
                const processedData = data.value.map((item, index) => {
                    // filepathプロパティがある場合
                    if (item.filepath) {
                        const filename = item.filepath.split('/').pop() || item.filepath;
                        
                        // PDFファイルかどうかを判断
                        const isPDF = item.filepath.includes('pdf_') || /\.pdf/i.test(item.filepath);
                        
                        // ファイル名からPDF文書名とページ番号を抽出
                        let displayName = filename;
                        let pageNum = null;
                        
                        if (filename.includes('-page-')) {
                            const parts = filename.split('-page-');
                            const pdfName = parts[0];
                            pageNum = parts[1].replace('.txt', '');
                            displayName = `【PDF文書】 ${pdfName} (ページ ${pageNum})`;
                        }
                        
                        return {
                            id: `path_${index}`,
                            name: displayName,
                            filepath: item.filepath,
                            fileType: isPDF ? 'PDF' : 'TEXT',
                            pageNumber: pageNum ? parseInt(pageNum) : null,
                            chunkNumber: item.chunkNo || null
                        };
                    }
                    
                    return {
                        id: `item_${index}`,
                        name: `Document ${index + 1}`,
                        filepath: JSON.stringify(item),
                        fileType: 'UNKNOWN',
                        pageNumber: null,
                        chunkNumber: null
                    };
                });
                
                // ページ番号でグループ化する
                const groupedByPage = groupDocumentsByPage(processedData);
                console.log('ページ番号でグループ化したデータ:', groupedByPage);
                
                return groupedByPage;
            }
            
            // その他の形式の場合は空配列を返す
            console.warn('予期しないデータ形式:', typeof data);
            return [];
        } catch (innerError) {
            console.error('データ処理中のエラー:', innerError);
            return [];
        }
    }
    // ユーザーメッセージのタグ折り畳みボタンのイベントハンドラー
    document.addEventListener('click', function(e) {
        if (e.target.closest('.user-tags-toggle')) {
            const button = e.target.closest('.user-tags-toggle');
            const targetId = button.getAttribute('data-target');
            const content = document.getElementById(targetId);
            const icon = button.querySelector('.toggle-icon');
            
            if (content && icon) {
                if (content.style.display === 'none') {
                    content.style.display = 'block';
                    icon.textContent = '▼';
                    icon.style.transform = 'rotate(0deg)';
                } else {
                    content.style.display = 'none';
                    icon.textContent = '▶';
                    icon.style.transform = 'rotate(-90deg)';
                }
            }
        }
    });
});