// Keyboard navigation for the active purchase/sale invoice and its calculator.
// Other dialogs keep their own keyboard behavior.
(function () {
    const fieldSelector = 'input:not([disabled]):not([readonly]):not([type="hidden"]):not([type="button"]):not([type="submit"]):not([type="checkbox"]):not([type="radio"]):not([type="file"]):not([tabindex="-1"]), select:not([disabled]):not([tabindex="-1"]), textarea:not([disabled]):not([readonly]):not([tabindex="-1"])';
    function visibleModals() {
        return Array.from(document.querySelectorAll('.modal.show')).filter(modal => modal.getClientRects().length > 0);
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
        if (event.isComposing || event.repeat) return;
        const visible = visibleModals();
        const modal = visible[visible.length - 1];
        if (!modal) return;
        const consume = () => { event.preventDefault(); event.stopImmediatePropagation(); };
        const isInvoice = !!modal.querySelector('[data-order-wizard]');
        const isCalculator = !!modal.querySelector('[data-order-calculator]');
        const childMarker = modal.querySelector('[data-order-child]');
        const isInvoiceChild = !!(childMarker && childMarker.getAttribute('data-order-child'));
        const invoiceCovered = !isInvoice && visible.slice(0, -1).some(item => item.querySelector('[data-order-wizard]'));
        if ((isInvoice || invoiceCovered) && !event.altKey && !event.ctrlKey && !event.metaKey && !event.shiftKey) {
            if (event.key === 'F1') {
                const ownButton = modal.querySelector('[data-hotkey="f1"]');
                consume();
                if (ownButton && !ownButton.disabled && ownButton.getClientRects().length) ownButton.click();
                return;
            }
            if (event.key === 'F2' || event.key === 'F3') {
                // Don't let a global handler activate an obscured form. Preserve a child dialog's own shortcut.
                const ownButton = invoiceCovered && modal.querySelector('[data-hotkey="' + event.key.toLowerCase() + '"]');
                if (ownButton && !ownButton.disabled && ownButton.getClientRects().length) {
                    consume(); ownButton.click();
                } else {
                    event.stopImmediatePropagation();
                }
                return;
            }
        }
        if (!isInvoice && !isCalculator && !isInvoiceChild) return;

        const target = event.target;
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

        if (!isInvoice || !event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
        const mapping = {
            KeyP: '[data-order-shortcut="print"]', KeyU: '[data-order-shortcut="back"]'
        };
        const selector = mapping[event.code];
        const button = selector && modal.querySelector(selector);
        if (!button || button.disabled || !button.getClientRects().length) return;
        consume(); button.click();
    }, true);
})();
