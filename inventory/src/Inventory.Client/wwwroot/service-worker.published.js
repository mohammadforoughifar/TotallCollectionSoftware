// Caution! Be sure you understand the caveats before publishing an application with
// offline support. See https://aka.ms/blazor-offline-considerations

self.importScripts('./service-worker-assets.js');
// ⚠️ بدون skipWaiting، نسخهٔ جدید بعد از هر پابلیش «در انتظار» می‌ماند و کاربر تا بستن کامل
// همهٔ تب‌ها/PWA همچنان نسخهٔ قدیمیِ کش‌شده را می‌بیند (مثلاً تقویم کاری جدید لود نمی‌شد).
self.addEventListener('install', event => { self.skipWaiting(); event.waitUntil(onInstall(event)); });
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/ ];
const offlineAssetsExclude = [ /^service-worker\.js$/ ];

// Replace with your base path if you are hosting on a subfolder. Ensure there is a trailing '/'.
const base = "/";
const baseUrl = new URL(base, self.origin);
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

async function onInstall(event) {
    console.info('Service worker: Install');

    // Fetch and cache all matching items from the assets manifest
    // ⚠️ قبلاً cache.addAll با integrity استفاده می‌شد؛ اگر حتی «یک» فایل هنگام پابلیش
    // روی سرور کمی تغییر می‌کرد (فشرده‌سازی/CRLF) کل install بی‌صدا شکست می‌خورد و
    // کاربران برای همیشه روی نسخهٔ قدیمی می‌ماندند. حالا هر فایل جداگانه و بدون
    // شکستن کل نصب کش می‌شود.
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    const cache = await caches.open(cacheName);
    await Promise.all(assetsRequests.map(async request => {
        try {
            const response = await fetch(request);
            if (response.ok) await cache.put(request.url, response);
        } catch {
            // فایل با integrity قابل دریافت نبود — بدون integrity تلاش مجدد
            try {
                const response = await fetch(new Request(request.url, { cache: 'no-cache' }));
                if (response.ok) await cache.put(request.url, response);
            } catch { /* آفلاین‌کش این فایل ممکن نشد؛ از شبکه سرو می‌شود */ }
        }
    }));
}

async function onActivate(event) {
    console.info('Service worker: Activate');

    // Delete unused caches
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));

    // کنترل فوری همهٔ تب‌های باز — تا بعد از پابلیش، همان لحظه نسخهٔ جدید فعال شود
    await self.clients.claim();
}

async function onFetch(event) {
    const requestUrl = new URL(event.request.url);

    // ⚠️ مسیرهای داده/فایل (API، SignalR و هر URL دارای access_token) هرگز نباید
    // از کش سرو یا با index.html پاسخ داده شوند: درخواست دانلود/پیش‌نمایش پیوست چت
    // وقتی در تب جدید (یا نوار آدرس) باز می‌شود باید مستقیم به سرور برسد؛ وگرنه
    // PWA صفحهٔ برنامه را نشان می‌دهد و دانلود فقط بعد از Ctrl+Shift+R انجام می‌شود.
    const isDataUrl = requestUrl.pathname.startsWith('/api/')
        || requestUrl.pathname.startsWith('/hubs/')
        || requestUrl.pathname.startsWith('/signalr/')
        || requestUrl.searchParams.has('access_token');
    if (isDataUrl) {
        return fetch(event.request);
    }

    let cachedResponse = null;
    if (event.request.method === 'GET') {
        // For all navigation requests, try to serve index.html from cache,
        // unless that request is for an offline resource.
        // If you need some URLs to be server-rendered, edit the following check to exclude those URLs
        const shouldServeIndexHtml = event.request.mode === 'navigate'
            // RADIS-HR یک SPA مستقل زیر همین Origin است؛ index اصلی نباید جای آن برگردد.
            && !new URL(event.request.url).pathname.startsWith('/radis-hr/')
            && !manifestUrlList.some(url => url === event.request.url);

        const request = shouldServeIndexHtml ? 'index.html' : event.request;
        const cache = await caches.open(cacheName);
        cachedResponse = await cache.match(request);
    }

    return cachedResponse || fetch(event.request);
}

// ================== نوتیفیکیشن گوشی/تبلت (Web Push) ==================
// وقتی اعلان از سرور می‌رسد، مثل نوتیف گوشی از بالای صفحه نمایش داده می‌شود
// و در نوار اعلان سیستم/مرورگر می‌ماند تا کاربر با آن تعامل کند.

self.addEventListener('push', event => {
    let data = {};
    try { data = event.data ? event.data.json() : {}; } catch { /* payload غیر JSON */ }

    const title = data.title || '📢 اعلان جدید';
    const options = {
        body: data.body || '',
        icon: data.icon || 'icon-192.png',
        badge: data.badge || 'icon-192.png',
        data: { url: data.link || '/' },
        dir: 'rtl',
        lang: 'fa',
        vibrate: [100, 50, 100],
        tag: `inv-${Date.now()}`
    };

    event.waitUntil(self.registration.showNotification(title, options));
});

// کلیک روی نوتیف → باز کردن لینک مربوطه (یا صفحه اصلی)
self.addEventListener('notificationclick', event => {
    event.notification.close();
    const url = (event.notification.data && event.notification.data.url) || '/';

    event.waitUntil(
        clients.matchAll({ type: 'window', includeUncontrolled: true }).then(clientList => {
            for (const client of clientList) {
                if ('focus' in client) {
                    client.navigate(url);
                    return client.focus();
                }
            }
            return clients.openWindow(url);
        })
    );
});
