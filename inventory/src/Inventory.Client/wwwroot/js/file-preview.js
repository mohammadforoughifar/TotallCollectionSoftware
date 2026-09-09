// ================== پیش‌نمایش فایل داخل برنامه ==================
// فایل با fetch و هدر Authorization گرفته می‌شود (بدون Base64 و بدون توکن در URL)
// تا فایل‌های بزرگ PDF/عکس نشکنند و مرورگر پنجره دانلود باز نکند.
window.filePreview = {
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
