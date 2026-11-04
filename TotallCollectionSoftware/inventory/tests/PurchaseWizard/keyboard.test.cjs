const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');

const source = fs.readFileSync(path.join(__dirname, '../../src/Inventory.Client/wwwroot/js/order-entry.js'), 'utf8');
function setup() {
    let handler;
    const state = { modals: [] };
    const document = {
        activeElement: null,
        addEventListener(type, cb, capture) { assert.equal(type, 'keydown'); assert.equal(capture, true); handler = cb; },
        querySelectorAll(selector) { return selector === '.modal.show' ? state.modals : []; }
    };
    const context = { window: {}, document, setTimeout };
    vm.runInNewContext(source, context);
    return { state, handler, api: context.window.orderEntry, document };
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
    const add = button(), tab = button(), f1 = button();
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
        querySelectorAll(selector) { return selector === '[data-order-line]' ? [row2] : []; },
        querySelector(selector) {
            if (selector === '[data-order-wizard]') return marker;
            if (selector === '[data-order-wizard], [data-order-calculator], [data-order-child]') return marker;
            if (selector === '[data-order-entry]') return grid;
            if (selector === '.modal-body') return body;
            if (selector === '[data-order-initial]') return section;
            if (selector === '[data-hotkey="f2"]') return add;
            if (selector === '[data-hotkey="f1"]') return f1;
            if (selector === '[data-pay-comp="cash"]') return tab;
            return null;
        }
    };
    row2.getAttribute = () => 'line-key';
    f.state.modals = [modal];
    const target = { matches: sel => sel === '[data-order-last-field]',
        closest: sel => sel === '[data-order-line]' ? row1 : sel === '[data-order-entry]' ? grid : null };
    return { ...f, modal, grid, body, section, row1, row2, first, second, target, add, tab, f1 };
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

test('payment shortcut is scoped and does not consume browser navigation or modified keys', () => {
    const f = fixture();
    let e = event({ key: '1', code: 'Digit1', altKey: true });
    f.handler(e); assert.equal(f.tab.clicked, 1); assert.ok(e.prevented && e.stopped);
    e = event({ key: 'ArrowLeft', code: 'ArrowLeft', altKey: true });
    f.handler(e); assert.equal(e.prevented, false);
    e = event({ key: '1', code: 'Digit1', altKey: true, ctrlKey: true });
    f.handler(e); assert.equal(f.tab.clicked, 1); assert.equal(e.prevented, false);
});

test('child dialogs block invoice F1 and F2 but keep their own hotkeys', () => {
    const f = fixture();
    const child = { getClientRects: visible, querySelector: () => null };
    f.state.modals.push(child);
    let e = event({ key: 'F2' }); f.handler(e); assert.ok(e.prevented && e.stopped); assert.equal(f.add.clicked, 0);
    const own = button();
    child.querySelector = selector => selector === '[data-hotkey="f2"]' ? own : null;
    e = event({ key: 'F2' }); f.handler(e); assert.ok(e.prevented && e.stopped); assert.equal(own.clicked, 1);
    e = event({ key: 'Escape' }); f.handler(e); assert.equal(e.prevented, false);
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

test('composition and other modals are not intercepted', () => {
    const f = fixture();
    let e = event({ key: '1', code: 'Digit1', altKey: true, isComposing: true }); f.handler(e); assert.equal(f.tab.clicked, 0);
    f.state.modals = [{ getClientRects: visible, querySelector: () => null }];
    e = event({ key: '1', code: 'Digit1', altKey: true }); f.handler(e); assert.equal(e.prevented, false);
});
