/**
 * SkyziBackup Service Worker
 * PWA対応とオフライン機能
 */

const CACHE_NAME = 'skyzibackup-v1.0.0';
const STATIC_CACHE = 'skyzibackup-static-v1.0.0';
const DYNAMIC_CACHE = 'skyzibackup-dynamic-v1.0.0';

// キャッシュするリソース
const STATIC_ASSETS = [
  '/',
  '/index.html',
  '/manual',
  '/manual.html',
  '/faq',
  '/faq.html',
  '/encryption',
  '/encryption.html',
  '/privacy',
  '/privacy.html',
  '/screenshots',
  '/screenshots.html',
  '/assets/css/style.css',
  '/assets/js/main.js',
  '/favicon.ico',
  '/manifest.json'
];

// ネットワーク優先のリソース
const NETWORK_FIRST_URLS = [
  '/api/',
  '/.netlify/',
  '/.github/'
];

// キャッシュ優先のリソース
const CACHE_FIRST_URLS = [
  '/images/',
  '/assets/fonts/',
  'https://fonts.googleapis.com/',
  'https://fonts.gstatic.com/'
];

// インストール時
self.addEventListener('install', (event) => {
  console.log('Service Worker: Installing...');
  
  event.waitUntil(
    Promise.all([
      // 静的アセットのキャッシュ
      caches.open(STATIC_CACHE).then((cache) => {
        console.log('Service Worker: Caching static assets');
        return cache.addAll(STATIC_ASSETS);
      }),
      
      // すぐにアクティベート
      self.skipWaiting()
    ])
  );
});

// アクティベート時
self.addEventListener('activate', (event) => {
  console.log('Service Worker: Activating...');
  
  event.waitUntil(
    Promise.all([
      // 古いキャッシュの削除
      caches.keys().then((cacheNames) => {
        return Promise.all(
          cacheNames.map((cacheName) => {
            if (cacheName !== STATIC_CACHE && 
                cacheName !== DYNAMIC_CACHE && 
                cacheName !== CACHE_NAME) {
              console.log('Service Worker: Deleting old cache', cacheName);
              return caches.delete(cacheName);
            }
          })
        );
      }),
      
      // すべてのクライアントを制御
      self.clients.claim()
    ])
  );
});

// フェッチイベント
self.addEventListener('fetch', (event) => {
  const { request } = event;
  const url = new URL(request.url);
  
  // 同一オリジンのリクエストのみ処理
  if (url.origin !== self.location.origin) {
    // 外部リソースの処理
    if (shouldCacheFirst(request.url)) {
      event.respondWith(cacheFirst(request));
    }
    return;
  }
  
  // GET リクエストのみ処理
  if (request.method !== 'GET') {
    return;
  }
  
  // 戦略の決定
  if (shouldNetworkFirst(request.url)) {
    event.respondWith(networkFirst(request));
  } else if (shouldCacheFirst(request.url)) {
    event.respondWith(cacheFirst(request));
  } else {
    event.respondWith(staleWhileRevalidate(request));
  }
});

// ネットワーク優先戦略
async function networkFirst(request) {
  try {
    const networkResponse = await fetch(request);
    
    if (networkResponse && networkResponse.status === 200) {
      const cache = await caches.open(DYNAMIC_CACHE);
      cache.put(request, networkResponse.clone());
    }
    
    return networkResponse;
  } catch (error) {
    console.log('Service Worker: Network failed, trying cache', error);
    const cachedResponse = await caches.match(request);
    
    if (cachedResponse) {
      return cachedResponse;
    }
    
    // オフラインページの提供
    if (request.destination === 'document') {
      return caches.match('/offline.html') || new Response(
        getOfflineHTML(), 
        { headers: { 'Content-Type': 'text/html' } }
      );
    }
    
    throw error;
  }
}

// キャッシュ優先戦略
async function cacheFirst(request) {
  const cachedResponse = await caches.match(request);
  
  if (cachedResponse) {
    return cachedResponse;
  }
  
  try {
    const networkResponse = await fetch(request);
    
    if (networkResponse && networkResponse.status === 200) {
      const cache = await caches.open(DYNAMIC_CACHE);
      cache.put(request, networkResponse.clone());
    }
    
    return networkResponse;
  } catch (error) {
    console.log('Service Worker: Cache and network failed', error);
    throw error;
  }
}

// Stale While Revalidate戦略
async function staleWhileRevalidate(request) {
  const cachedResponse = await caches.match(request);
  
  const networkResponsePromise = fetch(request).then((networkResponse) => {
    if (networkResponse && networkResponse.status === 200) {
      const cache = caches.open(DYNAMIC_CACHE);
      cache.then(c => c.put(request, networkResponse.clone()));
    }
    return networkResponse;
  }).catch(() => {
    console.log('Service Worker: Network failed for', request.url);
  });
  
  return cachedResponse || networkResponsePromise;
}

// URL戦略判定
function shouldNetworkFirst(url) {
  return NETWORK_FIRST_URLS.some(pattern => url.includes(pattern));
}

function shouldCacheFirst(url) {
  return CACHE_FIRST_URLS.some(pattern => url.includes(pattern));
}

// オフラインHTML
function getOfflineHTML() {
  return `
    <!DOCTYPE html>
    <html lang="ja">
    <head>
        <meta charset="UTF-8">
        <meta name="viewport" content="width=device-width, initial-scale=1.0">
        <title>オフライン - SkyziBackup</title>
        <style>
            body {
                font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
                margin: 0;
                padding: 2rem;
                background: linear-gradient(135deg, #ABF4FF 0%, #FF45A3 100%);
                min-height: 100vh;
                display: flex;
                align-items: center;
                justify-content: center;
                color: white;
                text-align: center;
            }
            .offline-container {
                max-width: 500px;
                background: rgba(255, 255, 255, 0.1);
                backdrop-filter: blur(10px);
                border-radius: 20px;
                padding: 3rem;
                border: 1px solid rgba(255, 255, 255, 0.2);
            }
            h1 { font-size: 2.5rem; margin-bottom: 1rem; }
            p { font-size: 1.1rem; line-height: 1.6; margin-bottom: 2rem; }
            .retry-btn {
                background: rgba(255, 255, 255, 0.2);
                border: 2px solid white;
                color: white;
                padding: 1rem 2rem;
                border-radius: 50px;
                font-size: 1rem;
                cursor: pointer;
                transition: all 0.3s ease;
            }
            .retry-btn:hover {
                background: rgba(255, 255, 255, 0.3);
                transform: translateY(-2px);
            }
        </style>
    </head>
    <body>
        <div class="offline-container">
            <h1>📡 オフラインです</h1>
            <p>
                現在インターネットに接続されていません。<br>
                接続を確認してから再度お試しください。
            </p>
            <button class="retry-btn" onclick="window.location.reload()">
                再試行
            </button>
        </div>
    </body>
    </html>
  `;
}

// プッシュ通知（将来の拡張用）
self.addEventListener('push', (event) => {
  if (!event.data) return;
  
  const options = {
    body: event.data.text(),
    icon: '/images/icon-192.png',
    badge: '/images/badge-72.png',
    vibrate: [100, 50, 100],
    data: {
      dateOfArrival: Date.now(),
      primaryKey: '1'
    },
    actions: [
      {
        action: 'explore',
        title: '詳細を見る',
        icon: '/images/checkmark.png'
      },
      {
        action: 'close',
        title: '閉じる',
        icon: '/images/xmark.png'
      }
    ]
  };
  
  event.waitUntil(
    self.registration.showNotification('SkyziBackup', options)
  );
});

// 通知クリック処理
self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  
  if (event.action === 'explore') {
    event.waitUntil(
      clients.openWindow('/')
    );
  }
});

// バックグラウンド同期（将来の拡張用）
self.addEventListener('sync', (event) => {
  if (event.tag === 'background-sync') {
    event.waitUntil(doBackgroundSync());
  }
});

async function doBackgroundSync() {
  console.log('Service Worker: Background sync triggered');
  // バックグラウンド同期の処理
}

// メッセージ処理
self.addEventListener('message', (event) => {
  if (event.data && event.data.type === 'SKIP_WAITING') {
    self.skipWaiting();
  }
  
  if (event.data && event.data.type === 'GET_VERSION') {
    event.ports[0].postMessage({ version: CACHE_NAME });
  }
});

console.log('Service Worker: Script loaded');