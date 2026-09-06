// ================== پیش‌نمایش فایل داخل برنامه ==================
// فایل با هدر احراز هویت گرفته می‌شود و به blob تبدیل می‌گردد تا
// توکن در URL افشا نشود و مرورگر پنجره دانلود باز نکند.
window.filePreview = {
    toBlobUrl: function (base64, mime) {
        try {
            const bin = atob(base64);
            const len = bin.length;
            const buf = new Uint8Array(len);
            for (let i = 0; i < len; i++) buf[i] = bin.charCodeAt(i);
            const blob = new Blob([buf], { type: mime || 'application/octet-stream' });
            return URL.createObjectURL(blob);
        } catch (e) {
            console.error('filePreview.toBlobUrl', e);
            return null;
        }
    },
    revoke: function (url) {
        try { if (url) URL.revokeObjectURL(url); } catch (e) { }
    }
};
