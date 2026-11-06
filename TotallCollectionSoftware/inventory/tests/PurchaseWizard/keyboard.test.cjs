const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');

const source = fs.readFileSync(path.join(__dirname, '../../src/Inventory.Client/wwwroot/js/order-entry.js'), 'utf8');
function setup() {
    let handler;
    const listeners = {};
    const state = { modals: [], shop: null };
    const document = {
        activeElement: null,
        addEventListener(type, cb, capture) { assert.ok(type === 'keydown' || type === 'focusin' || type === 'pointerdown'); assert.equal(capture, true); listeners[type] = cb; if (type === 'keydown') handler = cb; },
        querySelectorAll(selector) { return selector === '.modal.show, [data-shop-dialog]' ? state.modals : []; },
        querySelector(selector) { return selector === '.tabpane.on[data-shop-scope="true"]' ? state.shop : null; }
    };
    const context = { window: { addEventListener() {}, innerWidth: 800, innerHeight: 650 }, document, setTimeout };
    vm.runInNewContext(source, context);
    return { state, handler, listeners, api: context.window.orderEntry, document };
}
const visible = () => [1];
function button() { return { disabled: false, getClientRects: visible, clicked: 0, click() { this.clicked++; }, focus() { this.focused = true; } }; }
function event(values = {}) {
    return { isComposing: false, repeat: false, key: '', code: '', altKey: false, ctrlKey: false,
        metaKey: false, shiftKey: false, prevented: false, stopped: false,
        preventDefault() { this.prevented = true; }, stopImmediatePropagation() { this.stopped = true; }, ...values };
}
function fixture() {
    const f = setup();
    const add = button(), tab = button(), f1 = button(), back = button(), print = button();
    const marker = { getAttribute: name => name === 'data-order-wizard' ? 'form-key' : null };
    const makeField = () => ({ tagName: 'INPUT', focused: 0, selected: 0, disabled: false,
        getClientRects: visible, matches: selector => selector !== ':disabled' && selector !== '[data-order-last-field]',
        closest: () => null, hasAttribute: () => false, focus() { this.focused++; f.document.activeElement = this; }, select() { this.selected++; } });
    const first = makeField(), second = makeField();
    const body = { querySelectorAll: () => [first, second] };
    const row1 = {}, row2 = { querySelector: () => second };
    const grid = { querySelectorAll: () => [row1, row2] };
    const section = { querySelectorAll: () => [first, second] };
    const modal = { getClientRects: visible, contains: el => [grid, first, second].includes(el),
        querySelectorAll(selector) {
            if (selector === '[data-order-line]') return [row2];
            if (selector === '[data-hotkey="f1"]') return [f1];
            if (selector === '[data-hotkey="f2"]') return [add];
            if (selector === '[data-hotkey="f3"]') return [back];
            if (selector === '[data-hotkey="f4"]') return [print];
            return [];
        },
        querySelector(selector) {
            if (selector === '[data-order-wizard]') return marker;
            if (selector === '[data-order-wizard], [data-order-calculator], [data-order-child]') return marker;
            if (selector === '[data-order-entry]') return grid;
            if (selector === '.modal-body') return body;
            if (selector === '[data-order-initial]') return section;
            if (selector === '[data-order-add-line]') return add;
            if (selector === '[data-hotkey="f1"]') return f1;
            if (selector === '[data-pay-comp="cash"]') return tab;
            if (selector === '[data-order-shortcut="back"]') return back;
            if (selector === '[data-order-shortcut="print"]') return print;
            return null;
        }
    };
    row2.getAttribute = () => 'line-key';
    f.state.modals = [modal];
    const target = { matches: sel => sel === '[data-order-last-field]',
        closest: sel => sel === '[data-order-line]' ? row1 : sel === '[data-order-entry]' ? grid : null };
    return { ...f, modal, grid, body, section, row1, row2, first, second, target, add, tab, f1, back, print };
}

test('Enter on the last product field advances or adds a row', () => {
    const f = fixture();
    let e = event({ key: 'Enter', target: f.target });
    f.handler(e); assert.equal(f.second.focused, 1); assert.equal(f.add.clicked, 0); assert.ok(e.prevented && e.stopped);
    f.grid.querySelectorAll = () => [f.row1];
    e = event({ key: 'Enter', target: f.target });
    f.handler(e); assert.equal(f.add.clicked, 1); assert.ok(e.prevented && e.stopped);
});

test('initial and row focus are scoped to the active invoice', () => {
    const f = fixture();
    f.api.focusSection('form-key', '[data-order-initial]');
    assert.equal(f.first.focused, 1);
    f.api.focusSection('other-form', '[data-order-initial]');
    assert.equal(f.first.focused, 1);
    f.api.focusLine('form-key', 'line-key');
    assert.equal(f.second.focused, 1);
});

test('Enter and completed search selection advance through fields without activating save', () => {
    const f = fixture();
    f.document.activeElement = f.first;
    let e = event({ key: 'Enter', target: f.first }); f.handler(e);
    assert.equal(f.second.focused, 1); assert.ok(e.prevented && e.stopped);
    f.document.activeElement = f.first;
    f.api.focusNextField(f.first); assert.equal(f.second.focused, 2);
    f.document.activeElement = f.second;
    f.api.focusNextField(f.first); assert.equal(f.second.focused, 2); // user moved elsewhere
    e = event({ key: 'Enter', target: f.second }); f.handler(e);
    assert.equal(f.f1.focused, true); assert.equal(f.f1.clicked, 0);
});

test('open search select receives Enter and calendar controls are not intercepted', () => {
    const f = fixture();
    f.first.closest = sel => sel === '.ss' ? { querySelector: () => ({}) } : null;
    let e = event({ key: 'Enter', target: f.first }); f.handler(e);
    assert.equal(e.prevented, true); assert.equal(e.stopped, false); assert.equal(f.second.focused, 0);
    f.first.closest = sel => sel === '.dp-popover' ? {} : null;
    e = event({ key: 'Enter', target: f.first }); f.handler(e); assert.equal(e.prevented, false);
    f.first.closest = () => null;
    e = event({ key: 'Enter', target: f.first, shiftKey: true });
    f.handler(e); assert.equal(e.prevented, false); // Shift+Enter retains multiline behavior
    e = event({ key: 'Enter', target: { matches: () => false, closest: () => null } });
    f.handler(e); assert.equal(e.prevented, false);
});

test('payment choices have no shortcuts, including the former Alt keys', () => {
    const f = fixture();
    for (const key of ['F6', 'F9', 'F10']) {
        const e = event({ key }); f.handler(e); assert.equal(e.prevented, false); assert.equal(f.tab.clicked, 0);
    }
    for (const code of ['Digit1', 'Digit2', 'Digit3', 'Digit4', 'KeyC', 'KeyI', 'KeyJ', 'KeyK', 'KeyB', 'KeyN', 'KeyL', 'KeyU', 'KeyP']) {
        const e = event({ code, altKey: true }); f.handler(e); assert.equal(e.prevented, false); assert.equal(f.tab.clicked, 0);
    }
});

test('F8 opens only the focused invoice row last purchase and respects covering dialogs', () => {
    const f = fixture(), firstPurchase = button(), secondPurchase = button();
    const firstRow = { querySelector: selector => selector === '[data-order-last-purchase]' ? firstPurchase : null };
    const secondRow = { querySelector: selector => selector === '[data-order-last-purchase]' ? secondPurchase : null };
    f.modal.contains = el => el === firstRow || el === secondRow;
    const target = row => ({ closest: selector => selector === '[data-order-line]' ? row : null });
    let e = event({ key: 'F8', target: target(firstRow) }); f.handler(e);
    assert.ok(e.prevented && e.stopped); assert.equal(firstPurchase.clicked, 1); assert.equal(secondPurchase.clicked, 0);
    e = event({ key: 'F8', target: target(secondRow) }); f.handler(e);
    assert.equal(secondPurchase.clicked, 1);
    secondPurchase.disabled = true;
    e = event({ key: 'F8', target: target(secondRow) }); f.handler(e); assert.equal(secondPurchase.clicked, 1);
    e = event({ key: 'F8', target: target(null) }); f.handler(e); assert.ok(e.prevented); assert.equal(firstPurchase.clicked, 1);
    const child = { getClientRects: visible, querySelector: () => null, querySelectorAll: () => [] };
    f.state.modals.push(child);
    e = event({ key: 'F8', target: target(firstRow) }); f.handler(e);
    assert.ok(e.prevented && e.stopped); assert.equal(firstPurchase.clicked, 1);
    f.state.modals = [];
    f.state.shop = { querySelectorAll: () => [] };
    e = event({ key: 'F8', target: target(firstRow) }); f.handler(e);
    assert.equal(e.prevented, false); assert.equal(firstPurchase.clicked, 1);
});

test('invoice F1/F2/F3/F4 act only on the top dialog, never on a covered form', () => {
    const f = fixture();
    for (const [key, action] of [['F1', f.f1], ['F2', f.add], ['F3', f.back], ['F4', f.print]]) {
        const e = event({ key }); f.handler(e); assert.ok(e.prevented && e.stopped); assert.equal(action.clicked, 1);
    }
    // F7 in an invoice without a copy action must not pick an arbitrary document.
    let e = event({ key: 'F7' }); f.handler(e); assert.ok(e.prevented && e.stopped);
    const own = button();
    const child = { getClientRects: visible, querySelector: () => null,
        querySelectorAll: selector => selector === '[data-hotkey="f2"]' ? [own] : [] };
    f.state.modals.push(child);
    e = event({ key: 'F1' }); f.handler(e); assert.ok(e.prevented && e.stopped); assert.equal(f.f1.clicked, 1);
    e = event({ key: 'F2' }); f.handler(e); assert.ok(e.prevented && e.stopped); assert.equal(own.clicked, 1); assert.equal(f.add.clicked, 1);
    for (const key of ['F3', 'F4', 'F7', 'Escape']) {
        e = event({ key }); f.handler(e); assert.ok(e.prevented && e.stopped);
    }
    assert.equal(f.back.clicked, 1); assert.equal(f.print.clicked, 1);
});

test('shop-page shortcuts choose an unambiguous enabled action and ignore repeats/modifiers', () => {
    const f = setup(), save = button(), add = button(), duplicate = button();
    f.state.shop = { querySelectorAll(selector) {
        if (selector === '[data-hotkey="f1"]') return [save];
        if (selector === '[data-hotkey="f2"]') return [add];
        if (selector === '[data-hotkey="f4"]') return [duplicate, button()];
        return [];
    } };
    let e = event({ key: 'F1' }); f.handler(e); assert.ok(e.prevented && e.stopped); assert.equal(save.clicked, 1);
    e = event({ key: 'F2', repeat: true }); f.handler(e); assert.ok(e.prevented && e.stopped); assert.equal(add.clicked, 0);
    e = event({ key: 'F2', ctrlKey: true }); f.handler(e); assert.equal(e.prevented, false); assert.equal(e.stopped, true); assert.equal(add.clicked, 0);
    e = event({ key: 'F2' }); f.handler(e); assert.equal(add.clicked, 1);
    e = event({ key: 'F4' }); f.handler(e); assert.ok(e.prevented); assert.equal(duplicate.clicked, 0);
});

test('F7 on an invoice list requires the focused row; a child dialog is isolated', () => {
    const f = setup(), copy = button(), childSave = button(), childClose = button();
    const row = { querySelector: selector => selector === '[data-order-copy]' ? copy : null };
    f.state.shop = { contains: el => el === row, querySelectorAll: () => [] };
    let e = event({ key: 'F7', target: { closest: () => null } });
    f.handler(e); assert.ok(e.prevented && e.stopped); assert.equal(copy.clicked, 0);
    e = event({ key: 'F7', target: { closest: selector => selector === '[data-order-copy-context]' ? row : null } });
    f.handler(e); assert.equal(copy.clicked, 1);
    f.state.modals = [{ getClientRects: visible, querySelector: () => null, querySelectorAll(selector) {
        if (selector === '[data-hotkey="f1"]') return [childSave];
        if (selector === '[data-hotkey="esc"]') return [childClose];
        return [];
    } }];
    e = event({ key: 'F7', target: { closest: () => row } }); f.handler(e); assert.equal(copy.clicked, 1);
    e = event({ key: 'F1' }); f.handler(e); assert.equal(childSave.clicked, 1);
    e = event({ key: 'Escape' }); f.handler(e); assert.equal(childClose.clicked, 1);
});

test('Escape first lets an open search menu close, without cancelling its form', () => {
    const f = fixture(), close = button();
    f.modal.querySelectorAll = selector => selector === '[data-hotkey="esc"]' ? [close] : [];
    const target = { closest: selector => selector === '.ss' ? { querySelector: () => ({}) } : null };
    let e = event({ key: 'Escape', target }); f.handler(e); assert.equal(e.prevented, false); assert.equal(close.clicked, 0);
    e = event({ key: 'Escape', target: { closest: () => null } }); f.handler(e);
    assert.ok(e.prevented && e.stopped); assert.equal(close.clicked, 1);
});

test('invoice quick forms support multiline Enter navigation without affecting unrelated dialogs', () => {
    const f = fixture(), childField = f.first, next = f.second;
    const marker = { getAttribute: name => name === 'data-order-child' ? 'form-key' : null };
    const body = { querySelectorAll: () => [childField, next] };
    const section = { querySelectorAll: () => [childField, next] };
    const child = { getClientRects: visible, contains: el => el === childField || el === next,
        querySelector(selector) {
            if (selector === '[data-order-child]' || selector === '[data-order-wizard], [data-order-calculator], [data-order-child]') return marker;
            if (selector === '.modal-body') return body;
            if (selector === '[data-order-child]') return section;
            return null;
        } };
    f.state.modals.push(child);
    childField.tagName = 'TEXTAREA';
    let e = event({ key: 'Enter', target: childField, shiftKey: true }); f.handler(e);
    assert.equal(e.prevented, false); assert.equal(next.focused, 0);
    e = event({ key: 'Enter', target: childField }); f.handler(e);
    assert.equal(e.prevented, true); assert.equal(next.focused, 1);
});

test('search menu placement stays within the modal and viewport', () => {
    const f = fixture();
    const box = { getBoundingClientRect: () => ({ left: 100, right: 500, top: 40, bottom: 600 }) };
    const anchor = { closest: () => box, getBoundingClientRect: () => ({ left: 420, right: 490, top: 510, bottom: 550, width: 70 }) };
    const menu = { style: {} };
    f.api.placeSearchMenu(anchor, menu);
    assert.equal(menu.style.left, '262px'); assert.equal(menu.style.width, '230px');
    assert.equal(menu.style.top, '256px'); assert.equal(menu.style.maxHeight, '250px');
});

test('clicking next to an open invoice combo blurs it without blocking the click', () => {
    const f = fixture();
    let blurred = 0;
    const inside = {};
    const root = { querySelector: () => ({}), contains: target => target === inside };
    f.document.activeElement = { closest: () => root, blur: () => blurred++ };
    f.listeners.pointerdown({ target: inside }); assert.equal(blurred, 0);
    f.listeners.pointerdown({ target: {} }); assert.equal(blurred, 1);
});

test('composition and other modals are not intercepted', () => {
    const f = fixture();
    let e = event({ key: '1', code: 'Digit1', altKey: true, isComposing: true }); f.handler(e); assert.equal(f.tab.clicked, 0);
    f.state.modals = [{ getClientRects: visible, querySelector: () => null }];
    e = event({ key: '1', code: 'Digit1', altKey: true }); f.handler(e); assert.equal(e.prevented, false);
});
