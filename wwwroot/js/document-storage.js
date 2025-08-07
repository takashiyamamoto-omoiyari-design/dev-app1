/**
 * IndexedDB + LRU管理による全ドキュメントキャッシュ
 */
class DocumentStorageManager {
    constructor() {
        this.dbName = 'DocumentRAG';
        this.version = 1;
        this.db = null;
        this.maxDocuments = 1000;
        this.maxSize = 50 * 1024 * 1024; // 50MB
        this.currentSize = 0;
    }

    /**
     * IndexedDBを初期化
     */
    async init() {
        return new Promise((resolve, reject) => {
            console.log('【DocumentStorage】IndexedDB初期化開始');
            
            const request = indexedDB.open(this.dbName, this.version);
            
            request.onerror = () => {
                console.error('【DocumentStorage】IndexedDB初期化エラー:', request.error);
                reject(request.error);
            };
            
            request.onsuccess = () => {
                this.db = request.result;
                console.log('【DocumentStorage】IndexedDB初期化完了');
                resolve();
            };
            
            request.onupgradeneeded = (event) => {
                console.log('【DocumentStorage】IndexedDBスキーマアップグレード');
                const db = event.target.result;
                
                // documents ストア（メインデータ）
                if (!db.objectStoreNames.contains('documents')) {
                    const documentsStore = db.createObjectStore('documents', { keyPath: 'id' });
                    documentsStore.createIndex('workId', 'workId', { unique: false });
                    documentsStore.createIndex('lastAccessed', 'lastAccessed', { unique: false });
                    console.log('【DocumentStorage】documentsストア作成完了');
                }
                
                // metadata ストア（メタ情報）
                if (!db.objectStoreNames.contains('metadata')) {
                    const metadataStore = db.createObjectStore('metadata', { keyPath: 'key' });
                    console.log('【DocumentStorage】metadataストア作成完了');
                }
            };
        });
    }

    /**
     * 全ドキュメントをキャッシュに保存
     */
    async saveAllDocuments(documents) {
        try {
            console.log(`【DocumentStorage】全ドキュメント保存開始 - ${documents.length}件`);
            
            const transaction = this.db.transaction(['documents', 'metadata'], 'readwrite');
            const documentsStore = transaction.objectStore('documents');
            const metadataStore = transaction.objectStore('metadata');
            
            // 容量チェック
            const totalSize = this.calculateDocumentsSize(documents);
            console.log(`【DocumentStorage】保存予定サイズ: ${(totalSize / 1024 / 1024).toFixed(2)}MB`);
            
            if (totalSize > this.maxSize) {
                throw new Error(`データサイズが制限を超えています: ${(totalSize / 1024 / 1024).toFixed(2)}MB > ${(this.maxSize / 1024 / 1024).toFixed(2)}MB`);
            }
            
            // 古いデータをクリア
            await this.clearOldDocuments();
            
            // 新しいドキュメントを保存
            const savePromises = documents.map(doc => {
                const documentWithMeta = {
                    ...doc,
                    savedAt: new Date(),
                    lastAccessed: new Date(),
                    size: JSON.stringify(doc.content).length
                };
                return documentsStore.put(documentWithMeta);
            });
            
            await Promise.all(savePromises);
            
            // メタデータ更新
            await metadataStore.put({
                key: 'lastSync',
                value: new Date(),
                totalDocuments: documents.length
            });
            
            console.log(`【DocumentStorage】全ドキュメント保存完了 - ${documents.length}件`);
            return true;
            
        } catch (error) {
            console.error('【DocumentStorage】保存エラー:', error);
            throw error;
        }
    }

    /**
     * ドキュメントサイズを計算
     */
    calculateDocumentsSize(documents) {
        return documents.reduce((total, doc) => {
            return total + JSON.stringify(doc).length * 2; // UTF-16 文字数 × 2バイト
        }, 0);
    }

    /**
     * 古いドキュメントをクリア（LRU方式）
     */
    async clearOldDocuments() {
        try {
            console.log('【DocumentStorage】古いドキュメントクリア開始');
            
            const transaction = this.db.transaction(['documents'], 'readwrite');
            const store = transaction.objectStore('documents');
            const index = store.index('lastAccessed');
            
            const documents = await this.getAllDocumentsFromStore();
            
            if (documents.length > this.maxDocuments / 2) {
                // 古い順にソートして半分削除
                const sortedDocs = documents.sort((a, b) => 
                    new Date(a.lastAccessed) - new Date(b.lastAccessed)
                );
                
                const toDelete = sortedDocs.slice(0, Math.floor(documents.length / 2));
                
                for (const doc of toDelete) {
                    await store.delete(doc.id);
                }
                
                console.log(`【DocumentStorage】${toDelete.length}件の古いドキュメントを削除`);
            }
            
        } catch (error) {
            console.error('【DocumentStorage】クリアエラー:', error);
        }
    }

    /**
     * ストアから全ドキュメントを取得
     */
    async getAllDocumentsFromStore() {
        return new Promise((resolve, reject) => {
            const transaction = this.db.transaction(['documents'], 'readonly');
            const store = transaction.objectStore('documents');
            const request = store.getAll();
            
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
    }

    /**
     * キャッシュされた全ドキュメントを取得
     */
    async getCachedDocuments() {
        try {
            console.log('【DocumentStorage】キャッシュ取得開始');
            
            const documents = await this.getAllDocumentsFromStore();
            console.log(`【DocumentStorage】キャッシュから${documents.length}件取得`);
            
            return documents;
            
        } catch (error) {
            console.error('【DocumentStorage】取得エラー:', error);
            return [];
        }
    }

    /**
     * ドキュメントアクセス時刻を更新（LRU用）
     */
    async updateLastAccessed(documentIds) {
        try {
            const transaction = this.db.transaction(['documents'], 'readwrite');
            const store = transaction.objectStore('documents');
            
            for (const id of documentIds) {
                const docRequest = await store.get(id);
                if (docRequest) {
                    const doc = docRequest;
                    doc.lastAccessed = new Date();
                    await store.put(doc);
                }
            }
            
        } catch (error) {
            console.error('【DocumentStorage】アクセス時刻更新エラー:', error);
        }
    }

    /**
     * 同期状態を確認
     */
    async needsSync() {
        try {
            const transaction = this.db.transaction(['metadata'], 'readonly');
            const store = transaction.objectStore('metadata');
            const request = await store.get('lastSync');
            
            if (!request) {
                console.log('【DocumentStorage】初回同期が必要');
                return true;
            }
            
            const lastSync = new Date(request.value);
            const hoursSinceSync = (Date.now() - lastSync.getTime()) / (1000 * 60 * 60);
            
            const needSync = hoursSinceSync > 24; // 24時間以上経過で再同期
            console.log(`【DocumentStorage】最終同期: ${lastSync.toLocaleString()}, 再同期必要: ${needSync}`);
            
            return needSync;
            
        } catch (error) {
            console.error('【DocumentStorage】同期チェックエラー:', error);
            return true;
        }
    }

    /**
     * キャッシュサイズを取得
     */
    async getCacheSize() {
        try {
            const documents = await this.getAllDocumentsFromStore();
            const totalSize = this.calculateDocumentsSize(documents);
            return {
                totalDocuments: documents.length,
                totalSize: totalSize,
                totalSizeMB: (totalSize / 1024 / 1024).toFixed(2)
            };
        } catch (error) {
            console.error('【DocumentStorage】サイズ取得エラー:', error);
            return { totalDocuments: 0, totalSize: 0, totalSizeMB: '0.00' };
        }
    }
}

// グローバル変数として初期化
window.documentStorageManager = new DocumentStorageManager();

/**
 * ドキュメントストレージを初期化するグローバル関数
 * data-structuring.js から呼び出される
 */
async function initializeDocumentStorage() {
    try {
        console.log('【DocumentStorage】グローバル初期化開始');
        await window.documentStorageManager.init();
        console.log('【DocumentStorage】グローバル初期化完了');
        return true;
    } catch (error) {
        console.error('【DocumentStorage】グローバル初期化エラー:', error);
        // エラーが発生してもアプリケーションを継続するためfalseを返す
        return false;
    }
} 