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

    function placeSearchMenu(anchor, menu) {
        if (!anchor || !menu) return;
        const modal = anchor.closest('.modal-content');
        if (!modal) return;
        const a = anchor.getBoundingClientRect(), box = modal.getBoundingClientRect();
        const margin = 8;
        const leftEdge = Math.max(margin, box.left + margin);
        const rightEdge = Math.min(window.innerWidth - margin, box.right - margin);
        const topEdge = Math.max(margin, box.top + margin);
        const bottomEdge = Math.min(window.innerHeight - margin, box.bottom - margin);
        if (rightEdge <= leftEdge || bottomEdge <= topEdge) return;
        if (a.bottom < topEdge || a.top > bottomEdge) { menu.style.visibility = 'hidden'; return; }
        const width = Math.min(Math.max(a.width, 230), rightEdge - leftEdge);
        const left = Math.max(leftEdge, Math.min(a.left, rightEdge - width));
        const below = Math.max(0, bottomEdge - a.bottom - 4);
        const above = Math.max(0, a.top - topEdge - 4);
        const placeBelow = below >= 180 || below >= above;
        const height = Math.max(0, Math.min(250, placeBelow ? below : above));
        menu.style.width = Math.round(width) + 'px';
        menu.style.left = Math.round(left) + 'px';
        menu.style.top = Math.round(placeBelow ? a.bottom + 4 : a.top - height - 4) + 'px';
        menu.style.maxHeight = Math.round(height) + 'px';
        menu.style.visibility = height > 0 ? 'visible' : 'hidden';
        menu._orderAnchor = anchor;
    }
    window.addEventListener('scroll', function () {
        document.querySelectorAll('.ss-menu[data-order-constrained="true"]').forEach(menu => placeSearchMenu(menu._orderAnchor, menu));
    }, true);
    window.addEventListener('resize', function () {
        document.querySelectorAll('.ss-menu[data-order-constrained="true"]').forEach(menu => placeSearchMenu(menu._orderAnchor, menu));
    });

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
        const shortcuts = { F1: 'f1', F2: 'f2', F3: 'f3', F4: 'f4', F7: 'f7', Escape: 'esc' };
        const hotkey = shortcuts[event.key];
        const target = event.target;

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
