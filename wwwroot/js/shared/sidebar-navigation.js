// サイドバーナビゲーション共通処理
document.addEventListener('DOMContentLoaded', function() {
    // サイドバーメニューを完全に再構築する関数
    function rebuildSidebarMenu() {
        const sidebarMenu = document.querySelector('.sidebar-menu');
        
        if (sidebarMenu) {
            console.log('サイドバーメニューを再構築します');
            
            // 現在のページパスを取得
            const currentPath = window.location.pathname.toLowerCase();
            console.log('現在のパス:', currentPath);
            
            // ベースパスを取得
            const getBasePath = () => {
                // APP_BASE_PATH環境変数に対応: /demo-app2 などが設定されている場合は
                // それを使用し、設定されていない場合は空文字列（ルート）を使用
                // このコードはクライアントサイドでのみ実行されるため、サーバー側の
                // 環境変数を直接取得できません。そのため、現在のパスを解析して推測します。
                const pathSegments = window.location.pathname.split('/');
                    if (pathSegments.length > 1 && pathSegments[1] === 'demo-app2') {
        return '/demo-app2';
                }
                return '';
            };
            
            const basePath = getBasePath();
            console.log(`現在のベースパス: ${basePath}`);
            
            // メニュー構造の定義
            const menuStructure = [
                {
                    id: 'chat-menu-item',
                    url: `${basePath}/`,
                    icon: 'fa-comments',
                    text: 'チャット'
                },
                {
                    id: 'upload-menu-item',
                    url: `${basePath}/upload.html`,
                    icon: 'fa-upload',
                    text: 'アップロード'
                },
                {
                    id: 'multi-index-upload-menu-item',
                    url: `${basePath}/multi-index-upload.html`,
                    icon: 'fa-file-pdf',
                    text: 'マルチインデックス'
                },
                {
                    id: 'data-structuring-menu-item',
                    url: `${basePath}/DataStructuring`,
                    icon: 'fa-layer-group',
                    text: 'データ構造化'
                },
                {
                    id: 'index-menu-item',
                    url: '#',
                    icon: 'fa-database',
                    text: 'インデックス'
                },
                {
                    id: 'settings-menu-item',
                    url: '#',
                    icon: 'fa-cog',
                    text: '設定'
                },
                {
                    id: 'reinforcement-learning-menu',
                    url: `${basePath}/ReinforcementLearning`,
                    icon: 'fa-brain',
                    text: 'データセット'
                }
            ];
            
            // まず既存のメニュー項目をすべて削除
            sidebarMenu.innerHTML = '';
            
            // メニュー項目を再構築
            menuStructure.forEach(item => {
                // メニュー項目のHTML
                const isActive = 
                    (item.id === 'chat-menu-item' && (currentPath === '/' || currentPath === '')) ||
                    (item.id === 'upload-menu-item' && currentPath.includes('upload') && !currentPath.includes('multi-index')) ||
                    (item.id === 'multi-index-upload-menu-item' && currentPath.includes('multi-index')) ||
                    (item.id === 'data-structuring-menu-item' && currentPath.includes('datastructuring')) ||
                    (item.id === 'reinforcement-learning-menu' && currentPath.includes('reinforcement'));
                
                const activeClass = isActive ? 'active' : '';
                
                const menuHtml = `
                    <a href="${item.url}" class="sidebar-menu-item ${activeClass}" id="${item.id}">
                        <i class="fas ${item.icon}"></i> ${item.text}
                    </a>
                `;
                
                // メニューに追加
                sidebarMenu.insertAdjacentHTML('beforeend', menuHtml);
                
                if (isActive) {
                    console.log(`アクティブメニュー: ${item.id}`);
                }
            });
            
            // クリック処理を追加（サイドバーメニューの処理を一元化）
            document.querySelectorAll('.sidebar-menu-item').forEach(menuItem => {
                menuItem.addEventListener('click', function(e) {
                    // 標準のリンク動作は維持（href属性に任せる）
                    console.log(`メニュークリック: ${this.id} - ${this.getAttribute('href')}`);
                });
            });
            
            console.log('サイドバーメニューの再構築完了');
        } else {
            console.error('サイドバーメニュー要素が見つかりません');
        }
    }
    
    // ロゴリンクが正しく機能するようにする
    function ensureLogoLink() {
        const sidebarLogo = document.querySelector('.sidebar-logo');
        
        if (sidebarLogo) {
            // ベースパスを取得
            const pathSegments = window.location.pathname.split('/');
                        let basePath = '';
                    if (pathSegments.length > 1 && pathSegments[1] === 'demo-app2') {
            basePath = '/demo-app2';
            }
            
            // リンクを常に最新に設定
            sidebarLogo.setAttribute('href', `${basePath}/`);
            sidebarLogo.style.textDecoration = 'none';
            sidebarLogo.style.color = 'inherit';
            console.log(`ロゴリンクを設定しました: ${basePath}/`);
        }
    }
    
    // 初回実行
    rebuildSidebarMenu();
    ensureLogoLink();
    
    // ページ状態変化時にも実行（特定のイベントをトリガーとして）
    window.addEventListener('load', function() {
        rebuildSidebarMenu();
        ensureLogoLink();
    });
    
    // 定期的に確認して修復（不必要になったら削除）
    setInterval(function() {
        // チャットメニューの存在確認
        const chatMenuItem = document.getElementById('chat-menu-item');
        if (!chatMenuItem) {
            console.log('チャットメニューが消失したため再構築します');
            rebuildSidebarMenu();
        }
    }, 1000); // 1秒ごとに確認
});