// ============================================================
//  ابزارهای نوار تب‌های کاری
//  • پیمایش افقی با دکمه‌های کنار نوار
//  • چرخ‌موشی عمودی به پیمایش افقی تبدیل می‌شود
//  • تبِ فعال هنگام جابه‌جایی به دید می‌آید
//  • کشیدن و رها کردن برای تغییر ترتیب تب‌ها:
//      - دسکتاپ (ماوس): HTML5 Drag & Drop — dataTransfer در همین‌جا ست می‌شود،
//        چون Blazor به dataTransfer دسترسی ندارد و بدون setData در فایرفاکس
//        درگ اصلاً شروع نمی‌شود.
//      - لمسی/قلمی: Pointer Events از روی دستگیرهٔ ⠿ — HTML5 DnD روی لمسی کار نمی‌کند.
// ============================================================
window.appTabs = {

    scrollBy: function (el, dx) {
        if (!el) return;
        el.scrollBy({ left: dx, behavior: 'smooth' });
    },

    scrollToActive: function (el) {
        if (!el) return;
        var a = el.querySelector('.tab.on');
        if (!a) return;

        var r = a.getBoundingClientRect();
        var c = el.getBoundingClientRect();
        if (r.left < c.left) {
            el.scrollBy({ left: r.left - c.left - 12, behavior: 'smooth' });
        } else if (r.right > c.right) {
            el.scrollBy({ left: r.right - c.right + 12, behavior: 'smooth' });
        }
    },

    enableWheel: function (el) {
        if (!el || el.__tabWheel) return;
        el.__tabWheel = true;
        el.addEventListener('wheel', function (e) {
            // فقط وقتی حرکتِ عمودی غالب است آن را به افقی تبدیل می‌کنیم
            if (Math.abs(e.deltaY) > Math.abs(e.deltaX)) {
                e.preventDefault();
                el.scrollLeft += e.deltaY;
            }
        }, { passive: false });
    },

    // ------------------------------------------------------------
    //  کشیدن و رها کردن — یک‌بار روی ردیف تب‌ها نصب می‌شود.
    //  dotNetRef: ارجاع به کامپوننت TabBar برای فراخوانی OnTabDropped
    // ------------------------------------------------------------
    enableDrag: function (el, dotNetRef) {
        if (!el || el.__tabDrag) return;
        el.__tabDrag = true;

        // ---------- HTML5 DnD (ماوس/دسکتاپ) ----------
        // Blazor به dataTransfer دسترسی ندارد؛ بدون setData، فایرفاکس درگ را
        // شروع نمی‌کند و در کروم هم drop قابل‌اعتماد نیست. پس داده را همین‌جا می‌گذاریم.
        el.addEventListener('dragstart', function (e) {
            var tab = e.target && e.target.closest ? e.target.closest('.tab') : null;
            if (!tab || !el.contains(tab)) return;
            var key = tab.getAttribute('data-tab-key') || '';
            try {
                e.dataTransfer.setData('text/plain', key);
                e.dataTransfer.effectAllowed = 'move';
            } catch (err) { /* مرورگرهای قدیمی — درگِ Blazor همچنان تلاش می‌کند */ }
        });

        // اجازهٔ drop روی کلِ ردیف (پشتیبانِ preventDefaultِ Blazor).
        // بدون این، drop اصلاً رخ نمی‌دهد و کاربر هیچ تغییری نمی‌بیند.
        el.addEventListener('dragover', function (e) {
            e.preventDefault();
            try { e.dataTransfer.dropEffect = 'move'; } catch (err) {}
        });

        el.addEventListener('drop', function (e) {
            e.preventDefault();
        });

        // ---------- Pointer DnD (لمسی/قلمی) ----------
        // فقط از روی دستگیرهٔ ⠿ شروع می‌شود تا کشیدنِ افقیِ بقیهٔ تب با
        // اسکرولِ ردیف و کلیک اشتباه گرفته نشود. ماوس از HTML5 استفاده می‌کند.
        var drag = null; // { el, key, startX, startY, dx, active, pointerId, targetIndex }

        function tabsInOrder() {
            return Array.prototype.slice.call(el.querySelectorAll('.tab'));
        }

        // ایندکسِ درج در نوارِ RTL: تب‌های سمتِ راست (ایندکسِ کوچک‌تر) نقطهٔ
        // میانیِ بزرگ‌تری دارند؛ هرچه نقطهٔ میانی از نشانگر بزرگ‌تر باشد،
        // نشانگر بعدِ آن تب است.
        function insertionIndex(clientX, sourceEl) {
            var tabs = tabsInOrder().filter(function (t) { return t !== sourceEl; });
            var idx = 0;
            for (var i = 0; i < tabs.length; i++) {
                var r = tabs[i].getBoundingClientRect();
                var mid = r.left + r.width / 2;
                if (mid > clientX) idx++;
            }
            return idx;
        }

        function clearIndicators() {
            tabsInOrder().forEach(function (t) {
                t.classList.remove('dragover', 'pointer-dragging');
                t.style.transform = '';
            });
        }

        function suppressNextClick() {
            // بعد از درگ، مرورگر یک click هم شلیک می‌کند که تب را فعال می‌کند؛
            // آن را یک‌بار می‌بلعیم تا فقط ترتیب عوض شود.
            function swallow(e) {
                e.stopPropagation();
                e.preventDefault();
                el.removeEventListener('click', swallow, true);
            }
            el.addEventListener('click', swallow, true);
        }

        el.addEventListener('pointerdown', function (e) {
            if (e.pointerType === 'mouse') return; // ماوس → HTML5
            if (e.button !== undefined && e.button !== 0) return;
            var grip = e.target && e.target.closest ? e.target.closest('.tab-grip') : null;
            if (!grip) return;
            var tab = grip.closest('.tab');
            if (!tab || !el.contains(tab)) return;
            if (e.target.closest && e.target.closest('.tab-x')) return;

            drag = {
                el: tab,
                key: tab.getAttribute('data-tab-key') || '',
                startX: e.clientX,
                startY: e.clientY,
                active: false,
                pointerId: e.pointerId,
                targetIndex: -1
            };
        });

        el.addEventListener('pointermove', function (e) {
            if (!drag || e.pointerId !== drag.pointerId) return;
            var dx = e.clientX - drag.startX;
            var dy = e.clientY - drag.startY;

            if (!drag.active) {
                // آستانهٔ ۶ پیکسل: کلیکِ ساده نباید درگ شود.
                if (Math.abs(dx) < 6 && Math.abs(dy) < 6) return;
                drag.active = true;
                drag.el.classList.add('pointer-dragging');
                try { drag.el.setPointerCapture(e.pointerId); } catch (err) {}
            }

            e.preventDefault();
            // تبِ کشیده‌شده نشانگر را دنبال می‌کند (translateX در هر دو جهتِ RTL/LTR درست است).
            drag.el.style.transform = 'translateX(' + dx + 'px)';
            drag.el.style.zIndex = '5';

            drag.targetIndex = insertionIndex(e.clientX, drag.el);

            // نشانگرِ درج: روی تبِ همسایه‌ای که درج قبل/بعدِ آن است.
            clearIndicatorsKeepSource();
            var others = tabsInOrder().filter(function (t) { return t !== drag.el; });
            var target = others[drag.targetIndex] || others[others.length - 1];
            if (target) target.classList.add('dragover');

            // اسکرولِ خودکارِ ردیف وقتی نشانگر به لبه نزدیک است.
            var c = el.getBoundingClientRect();
            if (e.clientX < c.left + 36) el.scrollLeft -= 8;
            else if (e.clientX > c.right - 36) el.scrollLeft += 8;
        });

        function clearIndicatorsKeepSource() {
            tabsInOrder().forEach(function (t) {
                if (t !== drag.el) t.classList.remove('dragover');
            });
        }

        function endDrag(commit) {
            if (!drag) return;
            var wasActive = drag.active;
            var key = drag.key;
            var idx = drag.targetIndex;
            var elRef = drag.el;
            drag = null;
            clearIndicators();
            if (elRef) { elRef.style.transform = ''; elRef.style.zIndex = ''; }
            if (!commit || !wasActive || idx < 0 || !dotNetRef) return;
            suppressNextClick();
            try {
                dotNetRef.invokeMethodAsync('OnTabDropped', key, idx);
            } catch (err) { /* کامپوننت جدا شده — ترتیب فقط در همین نشست می‌ماند */ }
        }

        el.addEventListener('pointerup', function (e) {
            if (!drag || e.pointerId !== drag.pointerId) return;
            endDrag(true);
        });

        el.addEventListener('pointercancel', function (e) {
            if (!drag || e.pointerId !== drag.pointerId) return;
            endDrag(false);
        });
    }
};
