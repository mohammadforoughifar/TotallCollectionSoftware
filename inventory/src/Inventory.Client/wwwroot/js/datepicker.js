/* ============================================================
   کمکی تقویم شمسی (DatePicker) — جای‌گذاری منو روی ویوپورت
   ------------------------------------------------------------
   مشکل: منوی تقویم به‌صورت absolute داخل کارت باز می‌شد و به‌خاطر
   ترتیب چیدمان/overflow کارت‌های بعدی صفحه (مثلاً در صفحه‌ی مرخصی و
   ماموریت) زیر گرید کارت‌ها می‌رفت و روزها قابل انتخاب نبودند.
   راه‌حل: Popover API منو را بدون جابه‌جایی DOM به Top Layer می‌برد و
   position:fixed هم fallback مرورگرهای قدیمی است. مختصات نسبت به ورودی
   روی اسکرول/تغییر اندازه به‌روزرسانی می‌شود؛ کلیک بیرون و Esc نیز آن را می‌بندد.
   ============================================================ */
(function () {
    'use strict';

    var reg = {}; // key -> handlers

    function place(anchor, pop) {
        if (!anchor || !pop || !document.body.contains(pop)) return;
        var r = anchor.getBoundingClientRect();

        // اندازه‌گیری بدون تأثیر روی نمایش
        var pw = pop.offsetWidth || 292;
        var ph = pop.offsetHeight || 330;
        var vw = window.innerWidth, vh = window.innerHeight;

        var top = r.bottom + 6;
        if (top + ph > vh - 8) {
            var above = r.top - ph - 6;
            top = above >= 8 ? above : Math.max(8, vh - ph - 8);
        }
        var left = r.right - pw;               // هم‌تراز با لبه‌ی راست ورودی (RTL)
        if (left < 8) left = 8;
        if (left + pw > vw - 8) left = Math.max(8, vw - pw - 8);

        pop.style.position = 'fixed';
        pop.style.inset = 'auto';
        pop.style.top = top + 'px';
        pop.style.left = left + 'px';
        pop.style.right = 'auto';
        pop.style.bottom = 'auto';
        pop.style.margin = '0';
        pop.style.zIndex = '20050';
        pop.style.visibility = 'visible';
    }

    /**
     * فعال‌سازی منوی باز‌شده: جای‌گذاری روی ویوپورت + رصد اسکرول/کلیک بیرون/Esc
     * @param {string} key کلید یکتا
     * @param {Element} anchor ورودی تاریخ
     * @param {Element} pop منوی تقویم
     * @param {object} dotnet ارجاع کامپوننت (متد CloseFromJs)
     */
    window.dpOpen = function (key, anchor, pop, dotnet) {
        if (!anchor || !pop) return;
        window.dpClose(key);

        // Popover API عنصر را بدون جابه‌جایی DOM به Top Layer می‌برد؛ این راه‌حل
        // قطعیِ stacking-context کارت‌هاست. position:fixed پایین fallback مرورگر قدیمی است.
        if (typeof pop.showPopover === 'function') {
            try {
                if (!pop.matches(':popover-open')) pop.showPopover();
            } catch (err) { }
        }
        place(anchor, pop);

        var onScroll = function () { place(anchor, pop); };
        var onDown = function (e) {
            if (pop.contains(e.target) || anchor.contains(e.target)) return;
            try { dotnet && dotnet.invokeMethodAsync('CloseFromJs'); } catch (err) { }
        };
        var onKey = function (e) {
            if (e.key === 'Escape') { try { dotnet && dotnet.invokeMethodAsync('CloseFromJs'); } catch (err) { } }
        };

        window.addEventListener('scroll', onScroll, true);
        window.addEventListener('resize', onScroll);
        setTimeout(function () { document.addEventListener('mousedown', onDown, true); }, 0);
        document.addEventListener('keydown', onKey, true);

        reg[key] = { pop: pop, anchor: anchor, onScroll: onScroll, onDown: onDown, onKey: onKey };
    };

    /** جای‌گذاری مجدد (بعد از تغییر ماه/سال که ارتفاع عوض می‌شود) */
    window.dpReposition = function (key) {
        var r = reg[key];
        if (r) place(r.anchor, r.pop);
    };

    /** پاک‌کردن رصدها */
    window.dpClose = function (key) {
        var r = reg[key];
        if (!r) return;
        window.removeEventListener('scroll', r.onScroll, true);
        window.removeEventListener('resize', r.onScroll);
        document.removeEventListener('mousedown', r.onDown, true);
        document.removeEventListener('keydown', r.onKey, true);
        if (r.pop && typeof r.pop.hidePopover === 'function') {
            try {
                if (r.pop.matches(':popover-open')) r.pop.hidePopover();
            } catch (err) { }
        }
        delete reg[key];
    };
})();
