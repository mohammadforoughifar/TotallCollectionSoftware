// ================== پیش‌نمایش فایل داخل برنامه ==================
// فایل با fetch و هدر Authorization گرفته می‌شود (بدون Base64 و بدون توکن در URL)
// تا فایل‌های بزرگ PDF/عکس نشکنند و مرورگر پنجره دانلود باز نکند.
window.filePreview = {
    // Download through the authorized API without window.open or tokens in URLs.
    // A failed API response must never become a downloaded HTML/JSON error file.
    download: async function (url, token, fallbackName) {
        try {
            const headers = {};
            if (token) headers.Authorization = 'Bearer ' + token;
            const response = await fetch(url, { headers: headers, cache: 'no-store' });
            if (!response.ok) {
                let message = response.status === 401 ? 'نشست شما منقضی شده است؛ دوباره وارد شوید.'
                    : response.status === 403 ? 'اجازه دانلود این فایل را ندارید.'
                    : response.status === 404 ? 'فایل در سرور یافت نشد.'
                    : 'دانلود فایل ناموفق بود (خطای ' + response.status + ').';
                let error = {};
                try { error = await response.json(); } catch (_) { }
                if (error && typeof error.message === 'string' && error.message.trim()) message = error.message;
                return { success: false, message: message,
                    code: error && typeof error.code === 'string' ? error.code : null,
                    documentId: error && Number.isInteger(error.documentId) ? error.documentId : 0 };
            }
            const disposition = response.headers.get('Content-Disposition') || '';
            if (!/^\s*attachment\b/i.test(disposition)) {
                return { success: false, message: 'سرور به‌جای فایل پاسخ نامعتبر برگرداند؛ صفحه را تازه کنید و دوباره تلاش کنید.' };
            }
            let fileName = fallbackName || 'download';
            const encoded = /filename\*\s*=\s*UTF-8''([^;]+)/i.exec(disposition);
            const quoted = /filename\s*=\s*"((?:[^"\\]|\\.)*)"/i.exec(disposition);
            const plain = /filename\s*=\s*([^;]+)/i.exec(disposition);
            if (encoded) {
                try { fileName = decodeURIComponent(encoded[1].trim()); } catch (_) { }
            } else if (quoted) fileName = quoted[1].replace(/\\(.)/g, '$1');
            else if (plain) fileName = plain[1].trim();
            fileName = fileName.split(/[\\/]/).pop().replace(/[\x00-\x1f\x7f]/g, '') || 'download';
            const blob = await response.blob();
            const objectUrl = URL.createObjectURL(blob);
            const anchor = document.createElement('a');
            try {
                anchor.href = objectUrl;
                anchor.download = fileName;
                anchor.style.display = 'none';
                document.body.appendChild(anchor);
                anchor.click();
            } finally {
                anchor.remove();
                // Keep the object URL alive until browsers have accepted the download.
                setTimeout(() => URL.revokeObjectURL(objectUrl), 60000);
            }
            return { success: true };
        } catch (_) {
            return { success: false, message: 'دریافت فایل انجام نشد؛ اتصال شبکه و دسترسی به سرور را بررسی کنید.' };
        }
    },
    fetchBlobUrl: async function (url, token, mimeHint) {
        const headers = {};
        if (token) headers['Authorization'] = 'Bearer ' + token;
        const r = await fetch(url, { headers: headers });
        if (!r.ok) {
            let msg = 'خطای ' + r.status;
            try {
                const t = await r.text();
                try {
                    const j = JSON.parse(t);
                    if (j.message) msg = j.message;
                    else if (t) msg = t;
                } catch (_) {
                    if (t) msg = t;
                }
            } catch (_) { }
            throw new Error(msg);
        }
        const buf = await r.arrayBuffer();
        const raw = (r.headers.get('content-type') || mimeHint || 'application/octet-stream').split(';')[0].trim();
        return URL.createObjectURL(new Blob([buf], { type: raw || 'application/octet-stream' }));
    },
    fetchText: async function (url, token) {
        const headers = {};
        if (token) headers['Authorization'] = 'Bearer ' + token;
        const r = await fetch(url, { headers: headers });
        if (!r.ok) {
            let msg = 'خطای ' + r.status;
            try {
                const t = await r.text();
                try {
                    const j = JSON.parse(t);
                    if (j.message) msg = j.message;
                    else if (t) msg = t;
                } catch (_) {
                    if (t) msg = t;
                }
            } catch (_) { }
            throw new Error(msg);
        }
        return await r.text();
    },
    htmlToBlobUrl: function (html) {
        return URL.createObjectURL(new Blob([html], { type: 'text/html;charset=utf-8' }));
    },
    toBlobUrl: function (base64, mime) {
        try {
            const bin = atob(base64);
            const len = bin.length;
            const buf = new Uint8Array(len);
            for (let i = 0; i < len; i++) buf[i] = bin.charCodeAt(i);
            return URL.createObjectURL(new Blob([buf], { type: mime || 'application/octet-stream' }));
        } catch (e) {
            console.error('filePreview.toBlobUrl', e);
            return null;
        }
    },
    revoke: function (url) {
        try { if (url) URL.revokeObjectURL(url); } catch (e) { }
    }
};
window.fpFetchBlobUrl = window.filePreview.fetchBlobUrl;
window.fpFetchText = window.filePreview.fetchText;
window.fpHtmlToBlobUrl = window.filePreview.htmlToBlobUrl;
window.fpRevoke = window.filePreview.revoke;
