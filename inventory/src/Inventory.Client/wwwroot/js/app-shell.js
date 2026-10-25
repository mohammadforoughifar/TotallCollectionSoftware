// Shared page interop. Keep generated print HTML outside the HTML script parser.
// تعیین آدرس سرور API در زمان اجرا
// ================== ادیتور متن غنی ==================
// اجرای دستور روی ادیتورِ متمرکز/اول (برای سازگاری با کدهای قبلی)
// ============ کپی متن (با فالبک برای http بدون گواهی) ============
window.invCopyText = function (text) {
    try {
        if (navigator.clipboard && window.isSecureContext) { navigator.clipboard.writeText(text); return true; }
        var ta = document.createElement('textarea');
        ta.value = text; ta.setAttribute('readonly', '');
        ta.style.position = 'fixed'; ta.style.top = '-1000px'; ta.style.opacity = '0';
        document.body.appendChild(ta); ta.select(); ta.setSelectionRange(0, ta.value.length);
        var ok = document.execCommand('copy');
        document.body.removeChild(ta);
        return ok;
    } catch (e) { return false; }
};

window.rteExec = function (cmd, value) {
    document.execCommand(cmd, false, value === undefined ? null : value);
};
// یافتن ریشه ادیتور
window.rteGetHtml = function (id) { var el = document.getElementById(id); return el ? el.innerHTML : ""; };
window.rteSetHtml = function (id, html) { var el = document.getElementById(id); if (el) el.innerHTML = html || ""; };
window.rteGetEl = function (el) { return el ? el.innerHTML : ""; };
window.rteSetEl = function (el, html) { if (el) el.innerHTML = html || ""; };
window.rteExecEl = function (el, cmd, value) {
    if (!el) return;
    el.focus();
    document.execCommand("styleWithCSS", false, true);
    document.execCommand(cmd, false, value === undefined ? null : value);
};

// نگهداری آخرین انتخاب متن (برای اعمال فونت/رنگ/اندازه وقتی فوکوس به نوار ابزار می‌رود)
window.__rteSavedRange = null;
window.rteSaveSelection = function (id) {
    var el = document.getElementById(id);
    if (!el) return;
    var sel = window.getSelection();
    if (sel.rangeCount > 0 && el.contains(sel.anchorNode))
        window.__rteSavedRange = sel.getRangeAt(0).cloneRange();
};
window.rteRestoreSelection = function (id) {
    var el = document.getElementById(id);
    if (!el || !window.__rteSavedRange) return;
    el.focus();
    var sel = window.getSelection();
    sel.removeAllRanges();
    sel.addRange(window.__rteSavedRange);
};

// اجرای دستور روی ادیتور مشخص — فوکوس و dispatch
window.rteExecId = function (id, cmd, value) {
    var el = document.getElementById(id);
    if (!el) return;
    window.rteRestoreSelection(id); el.focus();
    document.execCommand("styleWithCSS", false, true);
    document.execCommand(cmd, false, value === undefined ? null : value);
};
// تنظیم قلم
window.rteFont = function (id, family) {
    var el = document.getElementById(id); if (!el) return;
    window.rteRestoreSelection(id); el.focus();
    document.execCommand("styleWithCSS", false, true);
    document.execCommand("fontName", false, family);
};
// تنظیم اندازه قلم (به پیکسل) — با ترفند font[size=7]
window.rteSize = function (id, px) {
    var el = document.getElementById(id); if (!el) return;
    window.rteRestoreSelection(id); el.focus();
    // برای ساخت تگ فونت خام، styleWithCSS را موقتاً خاموش می‌کنیم
    document.execCommand("styleWithCSS", false, false);
    document.execCommand("fontSize", false, "7");
    document.execCommand("styleWithCSS", false, true);
    var fac = (el.querySelectorAll('font[size="7"]'));
    for (var i = 0; i < fac.length; i++) {
        var s = document.createElement("span");
        s.style.fontSize = px;
        s.innerHTML = fac[i].innerHTML;
        fac[i].replaceWith(s);
    }
};
// رنگ متن
window.rteColor = function (id, color) {
    var el = document.getElementById(id); if (!el) return;
    window.rteRestoreSelection(id); el.focus();
    document.execCommand("styleWithCSS", false, true);
    document.execCommand("foreColor", false, color);
};
// رنگ پس‌زمینه (هایلایت)
window.rteHilite = function (id, color) {
    var el = document.getElementById(id); if (!el) return;
    window.rteRestoreSelection(id); el.focus();
    document.execCommand("styleWithCSS", false, true);
    document.execCommand("hiliteColor", false, color);
};
// درج لینک
window.rteLink = function (id) {
    var url = window.prompt("آدرس لینک را وارد کنید:", "https://");
    if (!url) return;
    var el = document.getElementById(id); if (!el) return;
    window.rteRestoreSelection(id); el.focus();
    document.execCommand("styleWithCSS", false, true);
    document.execCommand("createLink", false, url);
};
// درج جدول
window.rteTable = function (id, rows, cols) {
    var el = document.getElementById(id); if (!el) return;
    window.rteRestoreSelection(id); el.focus();
    rows = parseInt(rows) || 2; cols = parseInt(cols) || 2;
    var html = '<table class="table table-bordered my-2" style="width:100%; border-collapse:collapse; border:1px solid #cbd5e1;"><tbody>';
    for (var r = 0; r < rows; r++) {
        html += '<tr>';
        for (var c = 0; c < cols; c++) {
            html += '<td style="border:1px solid #cbd5e1; padding:8px;">&nbsp;</td>';
        }
        html += '</tr>';
    }
    html += '</tbody></table><p><br></p>';
    document.execCommand("insertHTML", false, html);
};
// درج محتوای HTML سفارشی
window.rteInsertHtml = function (id, html) {
    var el = document.getElementById(id); if (!el) return;
    window.rteRestoreSelection(id); el.focus();
    document.execCommand("insertHTML", false, html);
};
// تغییر جهت متن (RTL / LTR)
window.rteToggleDir = function (id) {
    var el = document.getElementById(id); if (!el) return;
    var current = el.getAttribute("dir") || "rtl";
    var next = current === "rtl" ? "ltr" : "rtl";
    el.setAttribute("dir", next);
    el.style.textAlign = next === "rtl" ? "right" : "left";
};
// چاپ نامه (اتوماسیون اداری)
window.ltxPrint = function () { window.print(); };
window.printWorkOrderDetail = function () { window.print(); };
// ---------- چاپ سند در یک iframe جداگانه ----------
// سند چاپ باید مستقل از استایل‌های صفحه و پوستهٔ اپ باشد؛ وگرنه قواعد
// @media print اپ (مخفی‌کردن سایدبار/محتوا و …) محتوای سند را هم می‌پوشاند
// و خروجی سفید می‌شود. اینجا یک سند کامل و خودبسنده در iframe می‌سازیم و
// پنجرهٔ چاپِ همان iframe را باز می‌کنیم.
// __FONT_BASE__ در سند با آدرس واقعی پوشهٔ فونت‌ها جایگزین می‌شود.
window.giPrint = function (html) {
    try {
        var base = document.baseURI.replace(/[^/]*$/, '');
        html = (html || '').split('__FONT_BASE__').join(base);

        var f = document.getElementById('gi-print-frame');
        if (!f) {
            f = document.createElement('iframe');
            f.id = 'gi-print-frame';
            f.setAttribute('aria-hidden', 'true');
            // صفر-بعد اما نه display:none — مرورگرهای Some از iframe پنهان چاپ نمی‌کنند
            f.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0;opacity:0;';
            document.body.appendChild(f);
        }
        var w = f.contentWindow;
        w.document.open();
        w.document.write(html);
        w.document.close();
        w.focus();
        // کمی صبر تا مرورگر چیدمان و فونت‌ها را آماده کند
        setTimeout(function () { try { w.print(); } catch (e) { } }, 80);
        return true;
    } catch (e) {
        console.error('giPrint:', e);
        return false;
    }
};
// فوکوس روی یک المان با شناسه — ناوبری جدول سطرهای حواله با کلید Enter
// (select=true متن موجود را انتخاب می‌کند تا تایپ قدرت، قالب را جعل کند)
window.focusById = function (id, select) {
    var el = document.getElementById(id);
    if (!el || el.disabled) return false;
    el.focus();
    if (select && el.select) { try { el.select(); } catch (e) { } }
    return true;
};
// باز کردن PDF نامه (چاپ روی سربرگ) در تب جدید — ورودی base64
window.ltxOpenPdf = function (base64, fileName) {
    try {
        var bin = atob(base64);
        var arr = new Uint8Array(bin.length);
        for (var i = 0; i < bin.length; i++) arr[i] = bin.charCodeAt(i);
        var blob = new Blob([arr], { type: 'application/pdf' });
        var url = URL.createObjectURL(blob);
        var w = window.open(url, '_blank');
        if (!w) { // popup blocked → download
            var a = document.createElement('a');
            a.href = url; a.download = fileName || 'letter.pdf';
            document.body.appendChild(a); a.click(); a.remove();
        }
        setTimeout(function () { URL.revokeObjectURL(url); }, 60000);
    } catch (e) { console.error(e); }
};

// ================== صفحهٔ چاپ (باز شدن هم‌زمان با کلیک) ==================
// چرا؟ window.open باید «هم‌زمان با کلیک کاربر» اجرا شود؛ اگر بعد از دریافت
// PDF از سرور (چند ثانیه بعد) باز شود، مرورگر آن را popup محسوب و مسدود می‌کند
// و در نتیجه به‌جای صفحهٔ چاپ، فایل دانلود می‌شد.
// الگو: ltxPrintBegin همان لحظهٔ کلیک پنجره را باز می‌کند (با پیام «در حال آماده‌سازی»)،
// سپس ltxPrintReady پس از رسیدن PDF، آن را داخل همان پنجره با iframe نمایش می‌دهد
// (نمایشگر PDF مرورگر دکمهٔ چاپ خودش را دارد)؛ ltxPrintFail هم خطا را نشان می‌دهد.
window.ltxPrintWindows = {};
window.ltxPrintPendingWindows = [];
window.ltxReservePrintWindow = function () {
    try {
        // Blazor dispatches @onclick through .NET first; reserve the popup in the
        // native click's capture phase so browser popup blockers still recognize
        // the user's gesture when the PDF request starts asynchronously.
        var w = window.open('about:blank', '_blank');
        if (w) window.ltxPrintPendingWindows.push(w);
        return !!w;
    } catch (e) {
        return false;
    }
};
document.addEventListener('click', function (event) {
    var target = event.target instanceof Element ? event.target.closest('[data-ltx-print-option]') : null;
    if (target) window.ltxReservePrintWindow();
}, true);
window.ltxPrintBegin = function () {
    try {
        var id = 'pw' + Date.now().toString(36) + Math.random().toString(36).slice(2, 7);
        var w = window.ltxPrintPendingWindows.shift();
        if (!w || w.closed) w = window.open('about:blank', '_blank');
        if (!w) return null; // popup blocked → caller falls back to download
        w.document.open();
        w.document.write('<!doctype html><html dir="rtl"><head><meta charset="utf-8"><title>در حال آماده‌سازی چاپ…</title></head>' +
            '<body style="margin:0;height:100vh;display:flex;align-items:center;justify-content:center;font-family:Tahoma,sans-serif;background:#f1f5f9;color:#334155">' +
            '<div style="text-align:center"><div style="font-size:18px;margin-bottom:8px">در حال آماده‌سازی نسخهٔ چاپ…</div>' +
            '<div style="font-size:13px;color:#64748b">چند لحظه صبر کنید</div></div></body></html>');
        w.document.close();
        window.ltxPrintWindows[id] = w;
        return id;
    } catch (e) { return null; }
};
window.ltxPrintReady = function (id, base64, fileName) {
    try {
        var w = window.ltxPrintWindows[id];
        if (!w || w.closed) return false;
        var bin = atob(base64);
        var arr = new Uint8Array(bin.length);
        for (var i = 0; i < bin.length; i++) arr[i] = bin.charCodeAt(i);
        var url = URL.createObjectURL(new Blob([arr], { type: 'application/pdf' }));
        var safeName = String(fileName || 'letter.pdf').replace(/[<>&]/g, '');
        w.document.open();
        w.document.write('<!doctype html><html><head><title>' + safeName + '</title>' +
            '<style>html,body{margin:0;padding:0;height:100%;overflow:hidden}iframe{border:0;display:block;width:100%;height:100%}</style></head>' +
            '<body><iframe src="' + url + '" title="print"></iframe></body></html>');
        w.document.close();
        setTimeout(function () { URL.revokeObjectURL(url); }, 120000);
        delete window.ltxPrintWindows[id];
        return true;
    } catch (e) { return false; }
};
window.ltxPrintFail = function (id, message) {
    try {
        var w = window.ltxPrintWindows[id];
        delete window.ltxPrintWindows[id];
        if (!w || w.closed) return;
        var msg = String(message || 'خطا در آماده‌سازی چاپ').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
        w.document.open();
        w.document.write('<!doctype html><html dir="rtl"><head><meta charset="utf-8"><title>خطای چاپ</title></head>' +
            '<body style="margin:0;height:100vh;display:flex;align-items:center;justify-content:center;font-family:Tahoma,sans-serif;background:#fef2f2;color:#b91c1c">' +
            '<div style="max-width:560px;text-align:center;padding:24px"><div style="font-size:40px;margin-bottom:12px">⚠️</div>' +
            '<div style="font-size:14px;line-height:2">' + msg + '</div></div></body></html>');
        w.document.close();
    } catch (e) { }
};

// ================== آدرس سرور API (مهم برای ورود موفق) ==================
// آدرس پیش‌فرض این استقرار: اگر هیچ نشانهٔ دیگری نبود، از همین استفاده می‌شود.
window.DEFAULT_API_BASE = 'http://192.168.30.104:8181';
// مسیر بررسی سلامت سرور — برای آزمایش واقعی «آیا API در دسترس است؟»
window.API_HEALTH_PATH = '/api/health';

// آیا نشانیِ ذخیره‌شده در مرورگر قابل اعتماد است؟
// ⚠️ ریشهٔ خطای «پس از زدن رمز، ارتباط با سرور برقرار نشد»:
// اگر روی خود سرور یک‌بار برنامه باز شده باشد، مقدار «http://localhost:5100» در localStorage
// ذخیره می‌شد و بعداً روی کامپیوتر کاربران همان نشانی محلی استفاده می‌شد؛ یعنی درخواست‌ها به
// کامپیوتر خودِ کاربر می‌رفت، نه به سرور. اینجا نشانی محلی روی دستگاه غیرمحلی نامعتبر اعلام می‌شود.
window.apiBaseIsSane = function (url) {
    if (!url) return false;
    try {
        var u = new URL(url, window.location.href);
        if (u.protocol !== 'http:' && u.protocol !== 'https:') return false;
        var pageHost = window.location.hostname;
        var pageIsLocal = (pageHost === 'localhost' || pageHost === '127.0.0.1');
        var urlIsLocal = (u.hostname === 'localhost' || u.hostname === '127.0.0.1');
        if (urlIsLocal && !pageIsLocal) return false;
        return true;
    } catch (e) { return false; }
};

// پورت‌های اجرای مستقیم پروژهٔ Blazor در Visual Studio. در این حالت رابط و API
// دو process مستقل هستند و UI نباید به‌علت یک مقدار قدیمی در localStorage به API
// سرور/نسخهٔ قبلی وصل شود؛ در غیر این صورت با F5 فقط Client بالا می‌آید و نتیجهٔ
// تغییرات جدید API دیده نمی‌شود.
window.inventoryIsLocalDevelopmentClient = function () {
    var host = window.location.hostname;
    var port = window.location.port;
    return (host === 'localhost' || host === '127.0.0.1') &&
        (port === '5210' || port === '7232');
};

window.inventoryApiBase = function (fallback) {
    var dflt = window.DEFAULT_API_BASE || fallback || 'http://localhost:5100';
    // ذخیرهٔ API فقط برای نصب‌های معمولی معتبر است. در Client توسعه، API محلیِ
    // متناظر باید قطعی انتخاب شود تا اجرای start.bat یا پاک‌سازی دستی مرورگر لازم نباشد.
    if (!window.inventoryIsLocalDevelopmentClient()) {
        try {
            var stored = localStorage.getItem('apiBaseUrl');
            if (window.apiBaseIsSane(stored)) return stored;
        } catch (e) { }
    }
    try {
        var h = window.location.host;
        // محیط پیش‌نمایش ابری: تبدیل پورت جاری به پورت API (5100)
        if (h.indexOf('.e2b.app') !== -1) {
            return window.location.protocol + '//' + h.replace(/^[0-9]+-/, '5100-');
        }
        // مسیر پایهٔ برنامه از base href — روی IIS اگر برنامه زیر یک Application/Virtual Directory
        // نصب شود (مثلاً http://server/erp/) سرور base href را «/erp/» می‌کند و API هم زیر همان مسیر است.
        var basePath = '';
        try { basePath = new URL(document.baseURI).pathname.replace(/\/+$/, ''); } catch (e) { }
        var sameOrigin = window.location.origin + basePath;
        var port = window.location.port;
        var host = window.location.hostname;
        if (host === 'localhost' || host === '127.0.0.1') {
            // روی خود سرور: انتشار تک‌سروره (IIS روی 80/443 یا پورت 5100/8181) هم‌مبدأ است.
            if (port === '' || port === '5100' || port === '8181') return sameOrigin;

            // Visual Studio: کلاینت روی 5210/7232 اجرا می‌شود و API روی 5100/7021.
            // قبلاً اینجا dflt (سرور 192.168...) برمی‌گشت؛ در نتیجه با وجود F5 کردن
            // API محلی، رابط توسعه همچنان به API قدیمی سرور وصل می‌شد.
            return window.location.protocol === 'https:'
                ? 'https://localhost:7021'
                : 'http://localhost:5100';
        }
        // هر میزبان دیگر (آی‌پی یا دامنه، با پورت یا بدون پورت — مثل IIS روی 80/443 یا سرور روی 8181):
        // UI و API هم‌مبدأ هستند. قبلاً حالت «بدون پورت» به http://localhost:5100 مرورگر کاربر می‌رفت
        // و روی IIS با binding پیش‌فرض هیچ درخواستی (از جمله ورود) به سرور نمی‌رسید.
        return sameOrigin;
    } catch (e) { }
    return dflt;
};

// آزمایش واقعی در دسترس بودن یک آدرس API (اگر سرور پاسخ ندهد، false برمی‌گردد)
window.apiBaseIsReachable = async function (base) {
    try {
        var url = String(base).replace(/\/+$/, '') + (window.API_HEALTH_PATH || '/api/health');
        var ctrl = ('AbortController' in window) ? new AbortController() : null;
        var timer = setTimeout(function () { try { ctrl && ctrl.abort(); } catch (e) { } }, 3000);
        var res = await fetch(url, { method: 'GET', cache: 'no-store', signal: ctrl ? ctrl.signal : undefined });
        clearTimeout(timer);
        if (!res || !res.ok) return false;
        var ct = res.headers.get('content-type') || '';
        return ct.indexOf('application/json') !== -1;
    } catch (e) { return false; }
};

// انتخاب آدرس API با «آزمایش موازی» همهٔ گزینه‌ها:
//   ۱) تشخیص خودکار (هم‌مبدأ)   ۲) نشانی ذخیره‌شده   ۳) آدرس پیش‌فرض سرور
// در اجرای توسعهٔ Client (5210/7232) فقط API محلی آزمایش می‌شود؛ fallback به
// 192.168... باعث می‌شد بدون اجرای API جدید، رابط بی‌صدا به نسخهٔ قدیمی سرور متصل شود.
window.inventoryResolveApiBase = async function (fallback) {
    var candidates = [];
    function push(u) {
        if (!u) return;
        u = String(u).trim().replace(/\/+$/, '');
        if (u && candidates.indexOf(u) === -1) candidates.push(u);
    }
    var localDevelopment = window.inventoryIsLocalDevelopmentClient();
    push(window.inventoryApiBase(fallback));
    if (!localDevelopment) {
        try { push(localStorage.getItem('apiBaseUrl')); } catch (e) { }
        push(window.DEFAULT_API_BASE);
        push(fallback);
        push('http://localhost:5100');
    }

    var results = await Promise.all(candidates.map(function (c) { return window.apiBaseIsReachable(c); }));
    for (var i = 0; i < candidates.length; i++) {
        if (results[i]) {
            try { localStorage.setItem('apiBaseUrl', candidates[i]); } catch (e) { }
            return candidates[i];
        }
    }
    // هیچ‌کدام پاسخ ندادند: حدس اول را نگه می‌داریم (پیام خطای دقیق‌تر به کاربر نشان داده می‌شود)
    return candidates[0] || window.DEFAULT_API_BASE || fallback || '';
};

// تشخیص صفحه کوچک‌تر از ~۱۲ اینچ (حالت موبایل/اندروید)
window.appShell = {
    isMobile: function () {
        return window.matchMedia('(max-width: 1279.98px)').matches;
    },
    _watching: [],
    watch: function (ref) {
        var mq = window.matchMedia('(max-width: 1279.98px)');
        var handler = function () {
            // پس از Dispose، مرجع .NET آزاد شده و فراخوانی آن خطای
            // «no tracked object with id» در کنسول می‌دهد؛ پس با پرچم متوقف می‌شود.
            if (handler.stopped) return;
            try { ref.invokeMethodAsync('OnScreenChanged', mq.matches); } catch (e) { handler.stopped = true; }
        };
        if (mq.addEventListener) mq.addEventListener('change', handler);
        else mq.addListener(handler);
        window.appShell._watching.push({ mq: mq, handler: handler });
    },
    unwatch: function () {
        var list = window.appShell._watching || [];
        for (var i = 0; i < list.length; i++) {
            var w = list[i];
            w.handler.stopped = true;
            try {
                if (w.mq.removeEventListener) w.mq.removeEventListener('change', w.handler);
                else w.mq.removeListener(w.handler);
            } catch (e) { }
        }
        window.appShell._watching = [];
    }
};
