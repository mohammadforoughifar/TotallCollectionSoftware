/* ============================================================
   ادیتور متن غنی (شبیه Word) — بدون هیچ وابستگی خارجی
   ------------------------------------------------------------
   امکانات: فونت/اندازه/رنگ متن و هایلایت، سرتیترها، بولد/ایتالیک/
   زیرخط/خط‌خورده، بالانویس/زیرنویس، لیست نقطه‌ای و شماره‌ای،
   تورفتگی، ترازها، راست‌چین/چپ‌چین (جهت متن)، جدول، تصویر، لینک،
   خط افقی، نقل‌قول، کد، پاک‌کردن قالب، واگرد/ازنو، جستجو و
   جایگزینی، شمارش کلمات، تمام‌صفحه و چاپ.
   ============================================================ */
(function () {
    'use strict';

    var saved = {};      // آخرین Range هر ادیتور (برای بازگردانی بعد از کلیک روی نوار ابزار)
    var inited = {};
    var lastId = null;   // آخرین ادیتور فعال (سازگاری با فراخوانی‌های قدیمی بدون id)

    function el(id) { return document.getElementById(id); }

    function isInside(node, root) {
        while (node) { if (node === root) return true; node = node.parentNode; }
        return false;
    }

    function saveSel(id) {
        var root = el(id); if (!root) return;
        lastId = id;
        var sel = window.getSelection();
        if (sel && sel.rangeCount > 0) {
            var r = sel.getRangeAt(0);
            if (isInside(r.commonAncestorContainer, root)) saved[id] = r.cloneRange();
        }
    }

    function restoreSel(id) {
        var root = el(id); if (!root) return false;
        root.focus();
        var r = saved[id];
        var sel = window.getSelection();
        if (r) {
            try { sel.removeAllRanges(); sel.addRange(r); return true; } catch (e) { }
        }
        // اگر انتخابی ذخیره نشده، مکان‌نما را انتهای متن بگذار
        try {
            var rr = document.createRange();
            rr.selectNodeContents(root);
            rr.collapse(false);
            sel.removeAllRanges();
            sel.addRange(rr);
        } catch (e) { }
        return false;
    }

    /** راه‌اندازی ادیتور: ذخیره‌ی انتخاب، چسباندن متن تمیز، تب، و کلیدهای میانبر */
    window.rteInit = function (id) {
        var root = el(id);
        if (!root || inited[id]) return;
        inited[id] = true;

        try { document.execCommand('styleWithCSS', false, true); } catch (e) { }

        root.addEventListener('keyup', function () { saveSel(id); });
        root.addEventListener('mouseup', function () { saveSel(id); });
        root.addEventListener('input', function () { saveSel(id); });
        root.addEventListener('blur', function () { saveSel(id); });

        // چسباندن: HTML ساده و امن (حذف اسکریپت/استایل و رویدادها)
        root.addEventListener('paste', function (e) {
            var cd = e.clipboardData || window.clipboardData;
            if (!cd) return;
            var html = cd.getData('text/html');
            e.preventDefault();
            if (html) {
                document.execCommand('insertHTML', false, sanitize(html));
            } else {
                var txt = cd.getData('text/plain') || '';
                document.execCommand('insertText', false, txt);
            }
            saveSel(id);
        });

        // Tab = تورفتگی به‌جای خروج از ادیتور
        root.addEventListener('keydown', function (e) {
            if (e.key === 'Tab') {
                e.preventDefault();
                document.execCommand(e.shiftKey ? 'outdent' : 'indent', false, null);
            }
        });
    };

    function sanitize(html) {
        var d = document.createElement('div');
        d.innerHTML = html;
        d.querySelectorAll('script,style,meta,link,iframe,object,embed').forEach(function (n) { n.remove(); });
        d.querySelectorAll('*').forEach(function (n) {
            for (var i = n.attributes.length - 1; i >= 0; i--) {
                var a = n.attributes[i];
                if (/^on/i.test(a.name)) n.removeAttribute(a.name);
                if (a.name === 'href' && /^\s*javascript:/i.test(a.value)) n.removeAttribute(a.name);
                if (a.name === 'src' && /^\s*javascript:/i.test(a.value)) n.removeAttribute(a.name);
            }
        });
        return d.innerHTML;
    }

    function resolveId(id) {
        if (id) { lastId = id; return id; }
        // ادیتور فعال (contenteditable حاوی انتخاب جاری)
        var sel = window.getSelection();
        if (sel && sel.rangeCount > 0) {
            var n = sel.getRangeAt(0).commonAncestorContainer;
            while (n) {
                if (n.nodeType === 1 && n.getAttribute && n.getAttribute('contenteditable') === 'true' && n.id) return n.id;
                n = n.parentNode;
            }
        }
        return lastId;
    }

    /** اجرای فرمان با بازگردانی انتخاب قبلی */
    window.rteExec = function (cmd, value, id) {
        id = resolveId(id);
        if (!id) { try { document.execCommand(cmd, false, (value === undefined ? null : value)); } catch (e) { } return; }
        restoreSel(id);
        try { document.execCommand('styleWithCSS', false, cmd === 'foreColor' || cmd === 'hiliteColor' || cmd === 'backColor'); } catch (e) { }
        try { document.execCommand(cmd, false, (value === undefined ? null : value)); } catch (e) { }
        saveSel(id);
    };

    /** فونت (خانواده) */
    window.rteFontName = function (id, family) {
        restoreSel(id);
        try { document.execCommand('fontName', false, family); } catch (e) { }
        saveSel(id);
    };

    /** اندازه‌ی قلم بر حسب pt — با پیچیدن انتخاب در span */
    window.rteFontSize = function (id, pt) {
        restoreSel(id);
        try {
            document.execCommand('fontSize', false, '7');
            var root = el(id);
            root.querySelectorAll('font[size="7"]').forEach(function (f) {
                var s = document.createElement('span');
                s.style.fontSize = pt + 'pt';
                s.innerHTML = f.innerHTML;
                f.parentNode.replaceChild(s, f);
            });
        } catch (e) { }
        saveSel(id);
    };

    /** سرتیتر / پاراگراف */
    window.rteBlock = function (id, tag) {
        restoreSel(id);
        try { document.execCommand('formatBlock', false, tag); } catch (e) { }
        saveSel(id);
    };

    /** جهت متن پاراگراف جاری (rtl / ltr) */
    window.rteDir = function (id, dir) {
        restoreSel(id);
        var sel = window.getSelection();
        var root = el(id);
        if (!sel || sel.rangeCount === 0 || !root) return;
        var node = sel.getRangeAt(0).commonAncestorContainer;
        if (node.nodeType === 3) node = node.parentNode;
        while (node && node !== root && !/^(P|DIV|LI|H[1-6]|BLOCKQUOTE|TD|TH)$/.test(node.tagName)) node = node.parentNode;
        if (!node || node === root) { root.style.direction = dir; root.style.textAlign = dir === 'rtl' ? 'right' : 'left'; }
        else { node.style.direction = dir; node.style.textAlign = dir === 'rtl' ? 'right' : 'left'; }
        saveSel(id);
    };

    /** درج جدول با ردیف/ستون دلخواه (با هدر اختیاری) */
    window.rteTable = function (id, rows, cols, header) {
        rows = Math.max(1, Math.min(30, rows | 0));
        cols = Math.max(1, Math.min(15, cols | 0));
        var h = '<table class="rte-table" style="border-collapse:collapse;width:100%"><tbody>';
        for (var r = 0; r < rows; r++) {
            h += '<tr>';
            for (var c = 0; c < cols; c++) {
                var isH = header && r === 0;
                var tag = isH ? 'th' : 'td';
                var st = 'border:1px solid #94a3b8;padding:6px 8px;' + (isH ? 'background:#f1f5f9;font-weight:700;' : '');
                h += '<' + tag + ' style="' + st + '">' + (isH ? 'عنوان' : '&nbsp;') + '</' + tag + '>';
            }
            h += '</tr>';
        }
        h += '</tbody></table><p><br></p>';
        window.rteExec('insertHTML', h, id);
    };

    /** درج تصویر از dataURL (با عرض حداکثر ۱۰۰٪) */
    window.rteImage = function (id, dataUrl, width) {
        if (!dataUrl) return;
        var w = width ? (' width="' + width + '"') : '';
        window.rteExec('insertHTML', '<img src="' + dataUrl + '"' + w + ' style="max-width:100%;height:auto" />', id);
    };

    /** درج/ویرایش لینک */
    window.rteLink = function (id, url, text) {
        if (!url) return;
        if (!/^([a-z]+:)?\/\//i.test(url) && !/^mailto:|^tel:/i.test(url)) url = 'http://' + url;
        var sel = window.getSelection();
        restoreSel(id);
        var hasSelection = sel && sel.toString().length > 0;
        if (hasSelection && !text) {
            window.rteExec('createLink', url, id);
        } else {
            var label = text || url;
            window.rteExec('insertHTML', '<a href="' + url + '" target="_blank">' + label + '</a>&nbsp;', id);
        }
    };

    /** خط افقی */
    window.rteHr = function (id) {
        window.rteExec('insertHTML', '<hr style="border:0;border-top:1px solid #cbd5e1;margin:10px 0" /><p><br></p>', id);
    };

    /** نقل‌قول */
    window.rteQuote = function (id) {
        window.rteBlock(id, 'blockquote');
        var root = el(id);
        if (root) root.querySelectorAll('blockquote:not([style])').forEach(function (b) {
            b.setAttribute('style', 'border-inline-start:3px solid #8e5cff;padding:4px 12px;margin:8px 0;color:#475569;background:#f8fafc');
        });
    };

    /** جستجو و جایگزینی ساده در متن ادیتور */
    window.rteReplace = function (id, find, replace, all) {
        var root = el(id);
        if (!root || !find) return 0;
        var count = 0;
        var walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, null, false);
        var nodes = [];
        while (walker.nextNode()) nodes.push(walker.currentNode);
        for (var i = 0; i < nodes.length; i++) {
            var n = nodes[i];
            var idx = n.nodeValue.indexOf(find);
            while (idx !== -1) {
                n.nodeValue = n.nodeValue.slice(0, idx) + replace + n.nodeValue.slice(idx + find.length);
                count++;
                if (!all) return count;
                idx = n.nodeValue.indexOf(find, idx + replace.length);
            }
        }
        return count;
    };

    /** شمارش کلمه/کاراکتر */
    window.rteStats = function (id) {
        var root = el(id);
        if (!root) return { words: 0, chars: 0 };
        var t = (root.innerText || '').replace(/\u200c/g, ' ').trim();
        return { words: t ? t.split(/\s+/).length : 0, chars: (root.innerText || '').length };
    };

    /** تمام‌صفحه کردن قاب ادیتور */
    window.rteFullscreen = function (wrapId) {
        var w = el(wrapId);
        if (!w) return false;
        var on = w.classList.toggle('rte-fs');
        document.body.classList.toggle('rte-fs-open', on);
        return on;
    };

    /** چاپ فقط محتوای ادیتور */
    window.rtePrintContent = function (id, title) {
        var root = el(id);
        if (!root) return;
        var w = window.open('', '_blank');
        if (!w) return;
        w.document.write('<html dir="rtl" lang="fa"><head><meta charset="utf-8"><title>' + (title || 'چاپ') +
            '</title><style>body{font-family:Vazirmatn,Tahoma,sans-serif;line-height:2;padding:24px} table{border-collapse:collapse;width:100%} td,th{border:1px solid #94a3b8;padding:6px 8px} img{max-width:100%}</style></head><body>' +
            root.innerHTML + '</body></html>');
        w.document.close();
        w.focus();
        setTimeout(function () { w.print(); }, 250);
    };

    /** فاصله‌ی خطوط کل بدنه */
    window.rteLineHeight = function (id, lh) {
        var e = el(id);
        if (e) e.style.lineHeight = lh;
    };

    /** کلیک برنامه‌ای روی input فایل مخفی (درج تصویر) */
    window.rteClickInput = function (inputId) {
        var i = el(inputId);
        if (i) i.click();
    };

    window.rteGetHtml = function (id) { var e = el(id); return e ? e.innerHTML : ''; };
    window.rteSetHtml = function (id, html) { var e = el(id); if (e) { e.innerHTML = html || ''; saved[id] = null; } };
    window.rteFocus = function (id) { var e = el(id); if (e) e.focus(); };

    /** وضعیت فعال بودن دکمه‌ها (bold/italic/…) برای هایلایت نوار ابزار */
    window.rteState = function (id) {
        var s = {};
        ['bold', 'italic', 'underline', 'strikeThrough', 'insertUnorderedList', 'insertOrderedList',
            'justifyRight', 'justifyCenter', 'justifyLeft', 'justifyFull', 'subscript', 'superscript']
            .forEach(function (c) { try { s[c] = document.queryCommandState(c); } catch (e) { s[c] = false; } });
        return s;
    };
})();

/* ============================================================
   دانلود / مشاهده‌ی فایل‌های محافظت‌شده (پیوست‌ها)
   فایل با هدر Authorization در سمت #C خوانده و اینجا به‌صورت
   Blob دانلود یا در تب جدید نمایش داده می‌شود.
   ============================================================ */
(function () {
    'use strict';

    function toBlob(base64, contentType) {
        var bin = atob(base64);
        var arr = new Uint8Array(bin.length);
        for (var i = 0; i < bin.length; i++) arr[i] = bin.charCodeAt(i);
        return new Blob([arr], { type: contentType || 'application/octet-stream' });
    }

    /** ذخیره‌ی فایل روی دستگاه کاربر */
    window.saveBlobFile = function (fileName, base64, contentType) {
        try {
            var url = URL.createObjectURL(toBlob(base64, contentType));
            var a = document.createElement('a');
            a.href = url;
            a.download = fileName || 'file';
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            setTimeout(function () { URL.revokeObjectURL(url); }, 3000);
        } catch (e) { console.error('saveBlobFile', e); }
    };

    /** باز کردن فایل در تب جدید (پیش‌نمایش تصویر/PDF/متن) */
    window.openBlobFile = function (base64, contentType) {
        try {
            var url = URL.createObjectURL(toBlob(base64, contentType));
            var w = window.open(url, '_blank');
            if (!w) { // مسدود شدن پاپ‌آپ → دانلود
                var a = document.createElement('a');
                a.href = url; a.target = '_blank'; a.rel = 'noopener';
                document.body.appendChild(a); a.click(); document.body.removeChild(a);
            }
            setTimeout(function () { URL.revokeObjectURL(url); }, 60000);
        } catch (e) { console.error('openBlobFile', e); }
    };
})();
