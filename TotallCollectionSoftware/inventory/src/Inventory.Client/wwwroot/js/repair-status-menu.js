// The status menu is rendered outside the repair table/cards so it cannot be
// clipped by their scroll/overflow containers. Blazor owns the menu and its
// actions; this helper only places it and handles focus/dismissal.
(function () {
    'use strict';

    let active = null;
    const margin = 8;
    const gap = 6;

    function visible(element) {
        if (!element || !element.isConnected || !element.getClientRects().length) return false;
        const r = element.getBoundingClientRect();
        return r.bottom > 0 && r.top < window.innerHeight && r.right > 0 && r.left < window.innerWidth;
    }

    function place(anchor, menu) {
        if (!visible(anchor)) return false;
        const r = anchor.getBoundingClientRect();
        const viewportHeight = window.innerHeight;
        const viewportWidth = window.innerWidth;
        menu.style.maxHeight = Math.max(1, viewportHeight - 2 * margin) + 'px';
        const menuRect = menu.getBoundingClientRect();
        const height = menuRect.height;
        const below = Math.max(0, viewportHeight - margin - r.bottom - gap);
        const above = Math.max(0, r.top - gap - margin);
        let top;
        if (height <= below) {
            top = r.bottom + gap;
        } else if (height <= above) {
            top = r.top - gap - height;
        } else if (below >= above) {
            menu.style.maxHeight = Math.max(1, below) + 'px';
            top = r.bottom + gap;
        } else {
            menu.style.maxHeight = Math.max(1, above) + 'px';
            top = r.top - gap - Math.min(height, above);
        }
        const width = menuRect.width;
        const left = Math.max(margin, Math.min(r.right - width, viewportWidth - margin - width));
        menu.style.left = Math.round(left) + 'px';
        menu.style.top = Math.max(margin, Math.round(top)) + 'px';
        menu.style.visibility = 'visible';
        return true;
    }

    function menuItems(menu) {
        return Array.from(menu.querySelectorAll('[role="menuitem"], [role="menuitemradio"]'))
            .filter(item => !item.disabled && item.getClientRects().length);
    }

    function focusItem(menu, item) {
        for (const candidate of menuItems(menu)) candidate.tabIndex = candidate === item ? 0 : -1;
        if (!item) return;
        item.focus({ preventScroll: true });
        // Keep the active option visible inside a height-limited menu without
        // scrolling the page (page scrolling intentionally dismisses it).
        if (item.offsetTop < menu.scrollTop) menu.scrollTop = item.offsetTop;
        else if (item.offsetTop + item.offsetHeight > menu.scrollTop + menu.clientHeight)
            menu.scrollTop = item.offsetTop + item.offsetHeight - menu.clientHeight;
    }

    // The popover is at the end of the list rather than beside the trigger in
    // DOM order. Tab should continue from the trigger, not jump past the list.
    function adjacentFocusable(anchor, menu, backwards) {
        const selector = 'a[href], area[href], button, input, select, textarea, [tabindex]';
        const elements = Array.from(document.querySelectorAll(selector)).filter(element =>
            !menu.contains(element) && !element.disabled && element.tabIndex >= 0 &&
            element.getClientRects().length && window.getComputedStyle(element).visibility !== 'hidden');
        const index = elements.indexOf(anchor);
        return index < 0 ? null : elements[index + (backwards ? -1 : 1)] || null;
    }

    function detach(state) {
        if (active !== state) return;
        document.removeEventListener(state.pointerEvent, state.onOutside, true);
        if (state.pointerEvent === 'mousedown') document.removeEventListener('touchstart', state.onOutside, true);
        document.removeEventListener('focusin', state.onFocus, true);
        window.removeEventListener('keydown', state.onKey, true);
        window.removeEventListener('scroll', state.onScroll, true);
        window.removeEventListener('resize', state.onResize);
        state.observer.disconnect();
        active = null;
    }

    function dismiss(state, restoreFocus) {
        if (active !== state) return;
        const close = state.menu.querySelector('[data-repair-status-dismiss]');
        detach(state);
        if (restoreFocus && visible(state.anchor) && !state.anchor.disabled)
            state.anchor.focus({ preventScroll: true });
        // The hidden button dispatches a normal Blazor event. Its versioned
        // callback cannot close a newer menu opened by the same outside click.
        if (close && close.isConnected) close.click();
    }

    window.repairStatusMenu = {
        open: function (anchorId, menuId) {
            const anchor = document.getElementById(anchorId);
            const menu = document.getElementById(menuId);
            if (!menu || !visible(anchor)) return false;
            if (active) {
                if (active.menu.isConnected) dismiss(active, false);
                else detach(active);
            }
            if (!place(anchor, menu)) return false;

            const position = anchor.getBoundingClientRect();
            const state = {
                anchor, menu, pointerEvent: 'PointerEvent' in window ? 'pointerdown' : 'mousedown',
                left: position.left, top: position.top, right: position.right, bottom: position.bottom,
                width: window.innerWidth, height: window.innerHeight
            };
            state.onOutside = function (event) {
                if (!menu.contains(event.target) && !anchor.contains(event.target)) dismiss(state, false);
            };
            state.onFocus = function (event) {
                if (!menu.contains(event.target) && !anchor.contains(event.target)) dismiss(state, false);
            };
            state.onKey = function (event) {
                if (event.key === 'Escape') {
                    event.preventDefault();
                    event.stopPropagation(); // Do not also close a surrounding app dialog/hotkey.
                    dismiss(state, true);
                    return;
                }
                if (!menu.contains(event.target)) return;
                if (event.key === 'Tab') {
                    const next = adjacentFocusable(anchor, menu, event.shiftKey);
                    event.preventDefault();
                    dismiss(state, false);
                    if (next) next.focus({ preventScroll: true });
                    else if (visible(anchor) && !anchor.disabled) anchor.focus({ preventScroll: true });
                    return;
                }
                if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) return;
                const items = menuItems(menu);
                if (!items.length) return;
                const current = items.indexOf(document.activeElement);
                let next = current;
                if (event.key === 'Home') next = 0;
                else if (event.key === 'End') next = items.length - 1;
                else if (event.key === 'ArrowDown') next = (current + 1 + items.length) % items.length;
                else next = (current < 0 ? 0 : current - 1 + items.length) % items.length;
                event.preventDefault();
                event.stopPropagation();
                focusItem(menu, items[next]);
            };
            state.onScroll = function (event) {
                // A short viewport can scroll inside the menu itself. A scroll
                // event queued before opening must not immediately close it.
                if (event.target !== window && event.target !== document && menu.contains(event.target)) return;
                if (!visible(anchor)) { dismiss(state, false); return; }
                const r = anchor.getBoundingClientRect();
                if (Math.abs(r.left - state.left) > .5 || Math.abs(r.top - state.top) > .5 ||
                    Math.abs(r.right - state.right) > .5 || Math.abs(r.bottom - state.bottom) > .5)
                    dismiss(state, false);
            };
            state.onResize = function () {
                if (window.innerWidth !== state.width || window.innerHeight !== state.height) dismiss(state, false);
            };
            state.observer = new MutationObserver(function () {
                if (!menu.isConnected) detach(state);
                else if (!visible(anchor)) dismiss(state, false);
            });
            active = state;
            document.addEventListener(state.pointerEvent, state.onOutside, true);
            if (state.pointerEvent === 'mousedown') document.addEventListener('touchstart', state.onOutside, true);
            document.addEventListener('focusin', state.onFocus, true);
            window.addEventListener('keydown', state.onKey, true);
            window.addEventListener('scroll', state.onScroll, true);
            window.addEventListener('resize', state.onResize);
            state.observer.observe(document.documentElement, {
                childList: true, subtree: true, attributes: true, attributeFilter: ['class', 'style', 'hidden']
            });
            const items = menuItems(menu);
            focusItem(menu, items.find(item => item.getAttribute('aria-checked') === 'true') || items[0]);
            return true;
        },
        release: function (menuId, restoreFocus) {
            if (!active || active.menu.id !== menuId) return;
            const state = active;
            detach(state);
            if (restoreFocus && visible(state.anchor) && !state.anchor.disabled)
                state.anchor.focus({ preventScroll: true });
        }
    };
}());
