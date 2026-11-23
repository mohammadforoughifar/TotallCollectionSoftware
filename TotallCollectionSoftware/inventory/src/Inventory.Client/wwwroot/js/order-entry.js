// Invoice navigation and scoped operational shortcuts for shop pages.
// Only the active shop tab and its topmost dialog may receive a shortcut.
(function () {
    const fieldSelector = 'input:not([disabled]):not([readonly]):not([type="hidden"]):not([type="button"]):not([type="submit"]):not([type="checkbox"]):not([type="radio"]):not([type="file"]):not([tabindex="-1"]), select:not([disabled]):not([tabindex="-1"]), textarea:not([disabled]):not([readonly]):not([tabindex="-1"])';
    function visibleModals() {
        return Array.from(document.querySelectorAll('.modal.show, [data-shop-dialog]')).filter(modal => modal.getClientRects().length > 0);
    }
    function topModal() {
        const modals = visibleModals();
        return modals[modals.length - 1];
    }
    function editableFields(root) {
        return Array.from(root.querySelectorAll(fieldSelector)).filter(field =>
            field.getClientRects().length > 0 && !field.closest('.dp-popover') && !field.matches(':disabled'));
    }
    function focusField(field) {
        if (!field) return false;
        field.focus();
        if ((field.tagName === 'INPUT' || field.tagName === 'TEXTAREA') && !field.closest('.ss') && typeof field.select === 'function') {
            try { field.select(); } catch (_) { /* e.g. input[type=number] */ }
        }
        return true;
    }
    function belongsToInvoice(modal, key) {
        const marker = modal && modal.querySelector('[data-order-wizard], [data-order-calculator], [data-order-child]');
        return marker && (marker.getAttribute('data-order-wizard') === key || marker.getAttribute('data-order-calculator') === key || marker.getAttribute('data-order-child') === key);
    }
    function nextField(input, modal) {
        const body = modal.querySelector('.modal-body');
        if (!body) return false;
        const fields = editableFields(body);
        const index = fields.indexOf(input);
        if (index < 0) return false;
        if (index + 1 < fields.length) return focusField(fields[index + 1]);
        // Focus the action but never activate it without explicit F1/Enter on the button.
        const action = modal.querySelector('[data-hotkey="f1"]');
        if (action && !action.disabled && action.getClientRects().length) return focusField(action);
        return false;
    }

    const MENU_MAX_HEIGHT = 250;

    // منوی کمبوباکس داخل فرم فاکتور همیشه باید به «فیلد خودش» چسبیده باشد.
    // مختصات inline که سمت C# (Popover.Below) ساخته می‌شود فقط عکسِ لحظهٔ فوکوس است؛
    // وقتی فرم بلند می‌شود (بیش از سه ردیف کالا) بدنهٔ مودال اسکرول می‌خورد و همان
    // مختصاتِ کهنه روی صفحه می‌ماند، در نتیجه منو وسط صفحه/جای دیگری باز می‌شود.
    // قاعده: تا این تابع جای درست را حساب نکند منو پنهان است، و پس از هر تغییر
    // چیدمان (اسکرول، تغییر اندازه، اضافه‌شدن ردیف) دوباره از روی خود فیلد حساب می‌شود.
    function measurable(el) {
        if (!el || typeof el.getBoundingClientRect !== 'function') return false;
        // المانِ پنهان (تب غیرفعال) هیچ مستطیلِ دیدنی‌ای ندارد.
        if (typeof el.getClientRects === 'function' && el.getClientRects().length === 0) return false;
        const r = el.getBoundingClientRect();
        if (!r) return false;
        const w = r.width != null ? r.width : r.right - r.left;
        const h = r.height != null ? r.height : r.bottom - r.top;
        return w > 0 && h > 0;
    }

    function hideMenu(menu) {
        if (!menu) return;
        if (menu.style) menu.style.visibility = 'hidden';
        if (typeof menu.removeAttribute === 'function') menu.removeAttribute('data-menu-placed');
    }

    // کادرِ مجاز = خودِ مودال بریده‌شده به viewport؛ بیرون از آن منو باز نمی‌شود.
    function menuClipBox(anchor, menu) {
        const modal = anchor.closest('.modal-content') || (menu.closest && menu.closest('.modal-content'));
        const box = modal ? modal.getBoundingClientRect() : { left: 0, top: 0, right: window.innerWidth, bottom: window.innerHeight };
        const margin = 8;
        return {
            left: Math.max(margin, box.left + margin),
            right: Math.min(window.innerWidth - margin, box.right - margin),
            top: Math.max(margin, box.top + margin),
            bottom: Math.min(window.innerHeight - margin, box.bottom - margin)
        };
    }

    function isRtl(el) {
        try {
            const doc = el.ownerDocument && el.ownerDocument.documentElement;
            if (doc && doc.getAttribute && doc.getAttribute('dir') === 'rtl') return true;
            return window.getComputedStyle(el).direction === 'rtl';
        } catch (_) { return false; }
    }

    function placeSearchMenu(anchor, menu) {
        if (!anchor || !menu) return false;
        menu._orderAnchor = anchor;
        // فیلدِ پنهان (تب غیرفعال، فیلد display:none) مختصات صفر می‌دهد؛ در این حالت
        // منو نباید با مختصات پیش‌فرض گوشهٔ صفحه ظاهر شود، باید پنهان بماند.
        if (!measurable(anchor)) { hideMenu(menu); return false; }
        const a = anchor.getBoundingClientRect();
        const box = menuClipBox(anchor, menu);
        if (box.right <= box.left || box.bottom <= box.top) { hideMenu(menu); return false; }
        // فیلد از کادرِ دیدنی بیرون رفته (زیر هدر اسکرول شده) → منو هم جایی برای نشستن ندارد.
        if (a.bottom < box.top || a.top > box.bottom) { hideMenu(menu); return false; }

        const width = Math.round(Math.min(Math.max(a.width, 230), box.right - box.left));
        // position:fixed نسبت به نزدیک‌ترین جد دارای transform است، نه viewport.
        const origin = window.fixedOrigin ? window.fixedOrigin(menu) : { left: 0, top: 0 };

        // RTL: لبهٔ راست منو روی لبهٔ راست فیلد می‌نشیند تا «زیر برچسب» دیده شود.
        let left = isRtl(anchor) ? a.right - width : a.left;
        left = Math.max(box.left, Math.min(left, box.right - width));

        menu.style.width = width + 'px';
        menu.style.maxHeight = MENU_MAX_HEIGHT + 'px';
        // اندازه‌گیری پیش از نمایش تا منو با ارتفاع قبلی/جای قبلی نپرد.
        menu.style.visibility = 'hidden';
        const naturalHeight = Math.min(MENU_MAX_HEIGHT, Math.ceil(menu.scrollHeight || menu.getBoundingClientRect().height || 0));
        if (naturalHeight <= 0) { hideMenu(menu); return false; }
        const below = Math.max(0, box.bottom - a.bottom - 4);
        const above = Math.max(0, a.top - box.top - 4);
        const placeBelow = below >= naturalHeight || below >= above;
        const height = Math.max(0, Math.min(naturalHeight, placeBelow ? below : above));
        if (height <= 0) { hideMenu(menu); return false; }
        const top = placeBelow ? a.bottom + 4 : a.top - height - 4;

        menu.style.left = Math.round(left - origin.left) + 'px';
        menu.style.top = Math.round(top - origin.top) + 'px';
        menu.style.maxHeight = Math.round(height) + 'px';
        menu.style.visibility = 'visible';
        if (typeof menu.setAttribute === 'function') menu.setAttribute('data-menu-placed', 'true');
        return true;
    }

    // نخستین تلاش ممکن است پیش از کامل‌شدن چیدمان (سطر تازه‌اضافه‌شده) ناکام بماند؛
    // یک فریم بعد دوباره تلاش می‌شود تا منو در جای درست ظاهر شود.
    function placeSearchMenuSoon(anchor, menu) {
        if (placeSearchMenu(anchor, menu)) return;
        if (!window.requestAnimationFrame) return;
        window.requestAnimationFrame(function () { placeSearchMenu(anchor, menu); });
    }

    function placeOpenMenus() {
        document.querySelectorAll('.ss-menu[data-order-constrained="true"]').forEach(function (menu) {
            if (menu._orderAnchor) placeSearchMenu(menu._orderAnchor, menu);
        });
    }

    // با بلندشدن فرم (افزودن کالا) یا اسکرول بدنهٔ مودال، منوی باز باید فوراً
    // به فیلد خودش برگردد؛ این سه شنونده همان چیزی است که وابستگی به تعداد
    // کالاها را از بین می‌برد.
    let menuLayoutObserver = null;
    function watchMenuLayout(menu) {
        if (!window.ResizeObserver || !menu || !menu.closest) return;
        const modal = menu.closest('.modal-content');
        if (!modal) return;
        if (!menuLayoutObserver) menuLayoutObserver = new window.ResizeObserver(placeOpenMenus);
        // هر بار مودالِ جاری مشاهده می‌شود (فرم فاکتور هر بار از نو ساخته می‌شود و
        // observe روی همان المان تکرارپذیر است). بدون این، اسکرول/بلندشدن فرم بعد از
        // باز شدن دوبارهٔ دیالوگ دیگر منو را جابه‌جا نمی‌کرد.
        try { menuLayoutObserver.observe(modal); } catch (_) { }
        const body = modal.querySelector('.modal-body');
        if (body) { try { menuLayoutObserver.observe(body); } catch (_) { } }
    }

    window.addEventListener('scroll', placeOpenMenus, true);
    window.addEventListener('resize', placeOpenMenus);

    // API عمومی برای کامپوننت‌ها (SearchSelect / SearchSelectStr).
    window.appMenu = {
        place: placeSearchMenu,
        placeSoon: function (anchor, menu) { watchMenuLayout(menu); placeSearchMenuSoon(anchor, menu); },
        placeAll: placeOpenMenus,
        reveal: function (menu) { if (menu) menu.style.visibility = 'visible'; },
        hide: function (menu) { if (menu) hideMenu(menu); },
        MENU_MAX_HEIGHT: MENU_MAX_HEIGHT
    };

    // Persistent invoice validation and temporary notifications share the top-right
    // corner. Measure only visible errors so the toast stack starts below them,
    // including after wrapping, tab changes, resize and Blazor re-rendering.
    if (window.MutationObserver && window.ResizeObserver && window.requestAnimationFrame && document.documentElement) {
        let frame = 0, lastOffset = -1;
        const observed = new Set();
        function scheduleNoticeLayout() {
            if (!frame) frame = window.requestAnimationFrame(updateNoticeLayout);
        }
        const resizeObserver = new window.ResizeObserver(scheduleNoticeLayout);
        function updateNoticeLayout() {
            frame = 0;
            const errors = Array.from(document.querySelectorAll('.order-form-error'));
            for (const item of observed) {
                if (!errors.includes(item)) { resizeObserver.unobserve(item); observed.delete(item); }
            }
            errors.forEach(item => {
                if (!observed.has(item)) { observed.add(item); resizeObserver.observe(item); }
            });
            const visibleBottoms = errors.filter(item => item.getClientRects().length > 0)
                .map(item => item.getBoundingClientRect().bottom);
            const margin = parseFloat(window.getComputedStyle(document.documentElement).fontSize) || 16;
            const offset = visibleBottoms.length ? Math.max(0, Math.ceil(Math.max(...visibleBottoms) + 12 - margin)) : 0;
            if (offset !== lastOffset) {
                document.documentElement.style.setProperty('--invoice-error-stack-offset', offset + 'px');
                lastOffset = offset;
            }
        }
        new window.MutationObserver(scheduleNoticeLayout).observe(document.documentElement, {
            subtree: true, childList: true, attributes: true, attributeFilter: ['class', 'hidden']
        });
        window.addEventListener('resize', scheduleNoticeLayout);
        window.addEventListener('scroll', scheduleNoticeLayout, true);
        scheduleNoticeLayout();
    }

    window.orderEntry = {
        placeSearchMenu: placeSearchMenu,
        placeSearchMenuSoon: placeSearchMenuSoon,
        isFocusWithin: function (root) { return !!root && root.contains(document.activeElement); },
        focusLine: function (formKey, lineKey) {
            const modal = topModal();
            if (!belongsToInvoice(modal, formKey)) return;
            const row = Array.from(modal.querySelectorAll('[data-order-line]'))
                .find(line => line.getAttribute('data-order-line') === lineKey);
            focusField(row && row.querySelector('.ss input'));
        },
        focusSection: function (formKey, selector) {
            const modal = topModal();
            if (!belongsToInvoice(modal, formKey)) return;
            const section = modal.querySelector(selector);
            if (!section) return;
            focusField(editableFields(section)[0]);
        },
        focusNextField: function (input) {
            const modal = topModal();
            // An async selection must not steal focus if the user moved elsewhere.
            if (!input || document.activeElement !== input || !modal || !modal.contains(input)) return;
            if (!modal.querySelector('[data-order-wizard], [data-order-calculator], [data-order-child]')) return;
            nextField(input, modal);
        }
    };

    // Constrained invoice menus do not use a click-blocking overlay: a click on
    // an adjacent add button must close the menu and reach the button in one step.
    document.addEventListener('pointerdown', function (event) {
        const active = document.activeElement;
        const root = active && active.closest && active.closest('.ss');
        if (root && root.querySelector('.ss-menu[data-order-constrained="true"]') && !root.contains(event.target)) active.blur();
    }, true);

    // Native month/year lists remain native; use the browser picker when it permits it.
    document.addEventListener('focusin', function (event) {
        const target = event.target, modal = topModal();
        if (!modal || !modal.querySelector('[data-order-wizard], [data-order-calculator], [data-order-child]') ||
            !target || target.tagName !== 'SELECT' || !modal.contains(target)) return;
        if (typeof target.showPicker === 'function') {
            try { target.showPicker(); } catch (_) { /* browser may require a fresh user gesture */ }
        }
    }, true);

    document.addEventListener('keydown', function (event) {
        if (event.isComposing) return;
        const visible = visibleModals();
        const modal = visible[visible.length - 1];
        const consume = () => { event.preventDefault(); event.stopImmediatePropagation(); };
        const isInvoice = !!(modal && modal.querySelector('[data-order-wizard]'));
        const isCalculator = !!(modal && modal.querySelector('[data-order-calculator]'));
        const childMarker = modal && modal.querySelector('[data-order-child]');
        const isInvoiceChild = !!(childMarker && childMarker.getAttribute('data-order-child'));
        const invoiceCovered = !isInvoice && visible.slice(0, -1).some(item => item.querySelector('[data-order-wizard]'));
        const shop = document.querySelector && document.querySelector('.tabpane.on[data-shop-scope="true"]');
        const shortcuts = { F1: 'f1', F2: 'f2', F3: 'f3', F4: 'f4', F7: 'f7', F8: 'f8', Escape: 'esc' };
        const hotkey = shortcuts[event.key];
        const target = event.target;

        // F8 belongs only to an invoice or its covering dialog, not the shop list.
        if (event.key === 'F8' && !isInvoice && !invoiceCovered) return;
        if ((shop || isInvoice || invoiceCovered) && hotkey) {
            // Don't send modified keys to the legacy global handler either; leave
            // their native browser behavior untouched. Repeats never submit twice.
            if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) {
                event.stopImmediatePropagation();
                return;
            }
            if (event.repeat) { consume(); return; }
            if (event.key === 'Escape' && target && target.closest) {
                const search = target.closest('.ss');
                if (search && search.querySelector('.ss-menu')) return; // let the combo close first
                const date = target.closest('.date-picker');
                const calendar = date && date.querySelector('.dp-popover');
                if (calendar) {
                    consume();
                    calendar.dispatchEvent(new CustomEvent('dpclose', { bubbles: true }));
                    return;
                }
            }

            let actions = [];
            if (event.key === 'F7' && !modal) {
                // A list never has a default invoice: F7 only copies the row/card
                // that actually owns keyboard focus.
                const row = target && target.closest && target.closest('[data-order-copy-context]');
                if (row && shop && shop.contains(row)) {
                    const copy = row.querySelector('[data-order-copy]');
                    if (copy) actions = [copy];
                }
            } else {
                const root = modal || shop;
                if (root) actions = Array.from(root.querySelectorAll('[data-hotkey="' + hotkey + '"]'));
            }
            actions = actions.filter(action => !action.disabled && action.getClientRects().length > 0);
            // Ambiguous actions are never guessed. Each form marks one action for
            // each shortcut; nested/hidden dialogs cannot reach an underlying form.
            if (hotkey !== 'esc' || modal || actions.length) consume();
            if (actions.length === 1) actions[0].click();
            return;
        }
        if (event.repeat || !modal || (!isInvoice && !isCalculator && !isInvoiceChild)) return;

        if (event.key === 'Enter' && !event.altKey && !event.ctrlKey && !event.metaKey && !event.shiftKey &&
            target && typeof target.matches === 'function') {
            if (isInvoice && target.matches('[data-order-last-field]')) {
                const row = target.closest('[data-order-line]');
                const grid = target.closest('[data-order-entry]');
                if (!row || !grid || !modal.contains(grid)) return;
                const rows = Array.from(grid.querySelectorAll('[data-order-line]'));
                const next = rows[rows.indexOf(row) + 1];
                if (next) {
                    const input = next.querySelector('.ss input');
                    if (!input) return;
                    consume(); focusField(input);
                } else {
                    const add = modal.querySelector('[data-order-add-line]');
                    if (!add || add.disabled) return;
                    consume(); add.click();
                }
                return;
            }
            if (!target.matches(fieldSelector) || target.closest('.dp-popover') || !modal.contains(target)) return;
            const search = target.closest('.ss');
            if (search && search.querySelector('.ss-menu')) {
                // Prevent form submission, but let the component select the highlighted option.
                event.preventDefault();
                return;
            }
            // Let the browser finish choosing a native select/datalist option first.
            if (target.tagName === 'SELECT' || (target.tagName === 'INPUT' && target.hasAttribute('list'))) {
                event.stopImmediatePropagation();
                setTimeout(() => {
                    if (document.activeElement === target && topModal() === modal) nextField(target, modal);
                }, 0);
                return;
            }
            const date = target.closest('.date-picker');
            const calendar = date && date.querySelector('.dp-popover');
            if (calendar) calendar.dispatchEvent(new CustomEvent('dpclose', { bubbles: true }));
            consume();
            nextField(target, modal);
            return;
        }
    }, true);
})();
