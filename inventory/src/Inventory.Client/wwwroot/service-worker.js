// In development, always fetch from the network and do not enable offline support.
// This is because caching would make development more difficult (changes would not
// be reflected on the first load after each change).
self.addEventListener('fetch', () => { });

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
        // tag یکتاست تا نوتیف‌های تکراری روی هم انباشته نشوند
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