// توابع تعامل جاوااسکریپت ماژول پروژه‌ها (چاپ گزارش / دانلود و نمایش پیوست‌ها)
// نکته: این کد عمداً در فایل جداگانه نگهداری می‌شود تا رشته‌های حاوی تگ‌های HTML
// (مانند تگ بستهٔ style و body) بلاک inline script را در index.html نشکنند.

(function () {
    'use strict';

    // ---------- ابزار: اسکیپ HTML ----------
    function esc(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    // ---------- ابزار: خواندن مقدار با هر دو حالت camelCase و PascalCase ----------
    // JSInterop بلیزور بسته به تنظیمات سریالایزر ممکن است Title یا title بفرستد؛
    // این تابع هر دو را می‌پذیرد تا چاپ در هیچ حالتی خالی نشود.
    function pick(obj, name) {
        if (!obj) return undefined;
        if (obj[name] !== undefined) return obj[name];
        var pascal = name.charAt(0).toUpperCase() + name.slice(1);
        if (obj[pascal] !== undefined) return obj[pascal];
        var camel = name.charAt(0).toLowerCase() + name.slice(1);
        return obj[camel];
    }

    // ---------- ساخت HTML سند چاپ ----------
    function buildReportHtml(opt) {
        var title = pick(opt, 'title') || 'گزارش';
        var subTitle = pick(opt, 'subTitle') || '';
        var metaList = pick(opt, 'meta') || [];
        var headers = pick(opt, 'headers') || [];
        var rows = pick(opt, 'rows') || [];
        var footerTotal = pick(opt, 'footerTotal') || '';
        var printedAt = pick(opt, 'printedAt') || '';
        var printedBy = pick(opt, 'printedBy') || '';

        var meta = metaList.map(function (m) {
            return '<span class="chip">' + esc(m) + '</span>';
        }).join('');

        var head = headers.map(function (h) {
            return '<th>' + esc(h) + '</th>';
        }).join('');

        var body = rows.map(function (r) {
            var cells = r || [];
            var tds = cells.map(function (c, i) {
                var cls = (i === cells.length - 1) ? 'desc' : 'num';
                return '<td class="' + cls + '">' + esc(c) + '</td>';
            }).join('');
            return '<tr>' + tds + '</tr>';
        }).join('');

        var foot = footerTotal
            ? '<tfoot><tr><td colspan="' + headers.length + '">' + esc(footerTotal) + '</td></tr></tfoot>'
            : '';

        // استایل‌ها به‌صورت رشته‌های جدا نگه داشته می‌شوند؛ تگ‌های بسته با اتصال ساخته می‌شوند
        // تا حتی اگر این فایل روزی inline شد، پارسر HTML را نشکنند.
        var css = [
            '* { box-sizing: border-box; }',
            'body { font-family: Vazirmatn, Tahoma, "Segoe UI", sans-serif; color: #1e293b; margin: 0; padding: 18px 22px; font-size: 11.5px; }',
            '.brand { display: flex; justify-content: space-between; align-items: baseline; border-bottom: 2.5px solid #4f46e5; padding-bottom: 8px; margin-bottom: 12px; }',
            '.brand .sys { font-size: 10px; color: #64748b; }',
            'h1 { font-size: 19px; margin: 0; color: #312e81; }',
            '.sub { font-size: 13px; color: #475569; margin: 2px 0 10px; font-weight: 700; }',
            '.chips { margin-bottom: 12px; line-height: 2.1; }',
            '.chip { display: inline-block; background: #eef2ff; border: 1px solid #c7d2fe; color: #3730a3; border-radius: 999px; padding: 1px 12px; margin-left: 6px; font-size: 10.5px; font-weight: 700; }',
            'table { width: 100%; border-collapse: collapse; }',
            'th { background: #312e81; color: #fff; font-weight: 700; padding: 6px 7px; border: 1px solid #312e81; font-size: 11px; white-space: nowrap; }',
            'td { border: 1px solid #cbd5e1; padding: 5px 7px; vertical-align: top; }',
            'td.num { text-align: center; white-space: nowrap; }',
            'td.desc { text-align: right; }',
            'tbody tr:nth-child(even) td { background: #f8fafc; }',
            'tfoot td { background: #fef3c7; border: 1px solid #d4a106; font-weight: 800; padding: 7px; font-size: 12px; color: #78350f; }',
            '.sign { margin-top: 34px; display: flex; justify-content: space-between; color: #475569; }',
            '.sign .line { display: inline-block; width: 170px; border-top: 1px dashed #94a3b8; padding-top: 4px; text-align: center; }',
            '.footer-meta { margin-top: 12px; font-size: 9.5px; color: #94a3b8; border-top: 1px solid #e2e8f0; padding-top: 6px; display: flex; justify-content: space-between; }',
            '@page { size: A4 portrait; margin: 10mm; }',
            '@media print { body { padding: 0; } thead { display: table-header-group; } tr { break-inside: avoid; } }'
        ].join('\n');

        var parts = [
            '<!DOCTYPE html>',
            '<html lang="fa" dir="rtl">',
            '<head>',
            '<meta charset="utf-8" />',
            '<title>' + esc(title) + '<' + '/title>',
            '<style>' + css + '<' + '/style>',
            '<' + '/head>',
            '<body>',
            '<div class="brand"><h1>' + esc(title) + '<' + '/h1><span class="sys">سامانه انبار و فروش — فروغ آریا<' + '/span><' + '/div>',
            subTitle ? '<div class="sub">' + esc(subTitle) + '<' + '/div>' : '',
            meta ? '<div class="chips">' + meta + '<' + '/div>' : '',
            '<table><thead><tr>' + head + '<' + '/tr><' + '/thead><tbody>' + body + '<' + '/tbody>' + foot + '<' + '/table>',
            '<div class="sign"><span>تحویل‌دهنده: <span class="line"><' + '/span><' + '/span>' +
            '<span>تأییدکننده: <span class="line"><' + '/span><' + '/span><' + '/div>',
            '<div class="footer-meta"><span>تاریخ چاپ: ' + esc(printedAt) + '<' + '/span>' +
            '<span>چاپ‌کننده: ' + esc(printedBy) + '<' + '/span><' + '/div>',
            '<' + '/body>',
            '<' + '/html>'
        ];
        return parts.join('\n');
    }

    // ---------- چاپ با iframe پنهان (روش اصلی) ----------
    // چرا iframe و نه window.open؟
    // این تابع از یک ادامهٔ async در C# صدا زده می‌شود (بعد از await روی فراخوانی API)،
    // بنابراین مرورگر آن را «خارج از تعامل کاربر» می‌بیند و پاپ‌آپ را بلاک می‌کند —
    // نتیجه: هیچ اتفاقی نمی‌افتاد و چاپ انجام نمی‌شد. iframe هم‌مبدأ بلاک نمی‌شود.
    function printViaIframe(html) {
        var old = document.getElementById('app-print-frame');
        if (old && old.parentNode) old.parentNode.removeChild(old);

        var frame = document.createElement('iframe');
        frame.id = 'app-print-frame';
        frame.setAttribute('aria-hidden', 'true');
        frame.style.position = 'fixed';
        frame.style.right = '0';
        frame.style.bottom = '0';
        frame.style.width = '0';
        frame.style.height = '0';
        frame.style.border = '0';
        frame.style.visibility = 'hidden';
        document.body.appendChild(frame);

        var doc = frame.contentWindow || frame.contentDocument;
        if (doc && doc.document) doc = doc.document;
        if (!doc) throw new Error('iframe-unavailable');

        doc.open();
        doc.write(html);
        doc.close();

        var cleanedUp = false;
        function cleanup() {
            if (cleanedUp) return;
            cleanedUp = true;
            setTimeout(function () {
                if (frame && frame.parentNode) frame.parentNode.removeChild(frame);
            }, 1000);
        }

        function doPrint() {
            try {
                var win = frame.contentWindow;
                win.focus();
                if (win.matchMedia) {
                    var mq = win.matchMedia('print');
                    if (mq.addEventListener) {
                        mq.addEventListener('change', function (e) { if (!e.matches) cleanup(); });
                    }
                }
                win.onafterprint = cleanup;
                win.print();
                // شبکهٔ ایمنی: اگر onafterprint هرگز شلیک نشد (بعضی مرورگرها)
                setTimeout(cleanup, 60000);
            } catch (e) {
                console.error('printTableReport (iframe print):', e);
                cleanup();
                throw e;
            }
        }

        // کمی صبر تا محتوا و فونت‌ها آماده شوند
        if (frame.contentWindow && frame.contentWindow.document.readyState === 'complete') {
            setTimeout(doPrint, 150);
        } else {
            frame.onload = function () { setTimeout(doPrint, 150); };
            setTimeout(doPrint, 700); // فال‌بک اگر onload شلیک نشد
        }
    }

    // ---------- چاپ با پنجرهٔ جدا (فال‌بک) ----------
    function printViaWindow(html) {
        var w = window.open('', '_blank', 'width=1020,height=760');
        if (!w) return false;
        w.document.open();
        w.document.write(html);
        w.document.close();
        w.focus();
        setTimeout(function () {
            try { w.print(); } catch (e) { console.error(e); }
        }, 350);
        w.onafterprint = function () { try { w.close(); } catch (e) { } };
        return true;
    }

    /**
     * چاپ گزارش جدولی (ماژول پروژه‌ها).
     * @param {object} opt گزینه‌ها: title, subTitle, meta[], headers[], rows[][], footerTotal, printedAt, printedBy
     */
    window.printTableReport = function (opt) {
        try {
            if (!opt) { alert('داده‌ای برای چاپ ارسال نشد.'); return false; }
            var html = buildReportHtml(opt);
            try {
                printViaIframe(html);
                return true;
            } catch (inner) {
                // اگر iframe به هر دلیلی کار نکرد، پنجرهٔ جدا را امتحان کن
                if (printViaWindow(html)) return true;
                alert('پنجره چاپ باز نشد — لطفاً مسدودکنندهٔ پاپ‌آپ مرورگر را برای این سایت غیرفعال کنید.');
                return false;
            }
        } catch (e) {
            console.error('printTableReport error:', e);
            alert('چاپ گزارش با خطا مواجه شد: ' + (e && e.message ? e.message : e));
            return false;
        }
    };

    // ---------- تبدیل Base64 به Blob ----------
    function base64ToBlob(base64, contentType) {
        var bin = atob(base64);
        var len = bin.length;
        var bytes = new Uint8Array(len);
        for (var i = 0; i < len; i++) bytes[i] = bin.charCodeAt(i);
        return new Blob([bytes], { type: contentType || 'application/octet-stream' });
    }

    /** دانلود فایل از داده Base64 — برای پیوست‌های رمزنگاری‌شده و خروجی اکسل */
    window.downloadBlob = function (fileName, contentType, base64) {
        try {
            var url = URL.createObjectURL(base64ToBlob(base64, contentType));
            var a = document.createElement('a');
            a.href = url;
            a.download = fileName || 'file';
            document.body.appendChild(a);
            a.click();
            a.remove();
            setTimeout(function () { URL.revokeObjectURL(url); }, 4000);
            return true;
        } catch (e) {
            console.error('downloadBlob error:', e);
            return false;
        }
    };

    /** باز کردن فایل (تصاویر و…) در تب جدید — از داده Base64 */
    window.openBlob = function (contentType, base64) {
        try {
            var url = URL.createObjectURL(base64ToBlob(base64, contentType));
            var win = window.open(url, '_blank');
            if (!win) {
                // پاپ‌آپ بلاک شد → به‌جای هیچ‌کاری، فایل را دانلود کن
                var a = document.createElement('a');
                a.href = url;
                a.target = '_blank';
                a.rel = 'noopener';
                document.body.appendChild(a);
                a.click();
                a.remove();
            }
            setTimeout(function () { URL.revokeObjectURL(url); }, 60000);
            return true;
        } catch (e) {
            console.error('openBlob error:', e);
            return false;
        }
    };
})();
