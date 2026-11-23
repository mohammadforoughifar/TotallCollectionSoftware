'use strict';

// Simulated-DOM test of the invoice combo placement (no browser needed).
// It loads the production js/order-entry.js and checks that the bank list always
// lands under its own field — with 1 item row and with 12 item rows — instead of
// wherever the stale inline coordinates point.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');

const source = fs.readFileSync(path.join(__dirname, '../../src/Inventory.Client/wwwroot/js/order-entry.js'), 'utf8');

function setup({ width = 1280, height = 720 } = {}) {
    const listeners = {};
    const observed = [];
    const state = { menus: [] };
    const documentElement = { getAttribute: name => (name === 'dir' ? 'rtl' : null) };
    const document = {
        activeElement: null,
        documentElement,
        addEventListener(type, cb) { listeners[type] = cb; },
        querySelectorAll(selector) { return selector === '.ss-menu[data-order-constrained="true"]' ? state.menus : []; },
        querySelector() { return null; }
    };
    const window = {
        innerWidth: width, innerHeight: height,
        addEventListener() {},
        requestAnimationFrame(cb) { cb(); return 1; },
        getComputedStyle() { return { direction: 'rtl' }; },
        ResizeObserver: function (cb) { this.observe = el => observed.push({ el, cb }); this.unobserve = () => {}; }
    };
    const context = { window, document, setTimeout, console };
    vm.runInNewContext(source, context);
    return { window, document, state, observed, api: context.window.appMenu };
}

// Anchor and menu in *viewport* coordinates, like getBoundingClientRect gives.
function makeAnchor(rect, { visible = true, modal = null } = {}) {
    return {
        rect,
        getClientRects: () => (visible ? [rect] : []),
        getBoundingClientRect: () => rect,
        ownerDocument: { documentElement: { getAttribute: () => 'rtl' } },
        closest: sel => (sel === '.modal-content' ? modal : null)
    };
}

function makeMenu(rect, { height = 240 } = {}) {
    const attrs = {};
    return {
        style: { visibility: 'hidden' },
        scrollHeight: height,
        getBoundingClientRect: () => rect,
        setAttribute: (k, v) => { attrs[k] = v; },
        removeAttribute: k => { delete attrs[k]; },
        getAttribute: k => attrs[k],
        attrs,
        closest: sel => (sel === '.modal-content' ? menuModal : null)
    };
}

let menuModal = null;

test('menu sits under its own field, not in the middle of the page', () => {
    const f = setup();
    const modal = { getBoundingClientRect: () => ({ left: 60, top: 40, right: 1220, bottom: 680 }) };
    menuModal = modal;
    const field = { left: 900, top: 300, right: 1140, bottom: 338, width: 240, height: 38 };
    const anchor = makeAnchor(field, { modal });
    const menu = makeMenu({ left: 0, top: 0, right: 0, bottom: 0 });

    assert.equal(f.api.place(anchor, menu), true);
    assert.equal(menu.style.visibility, 'visible');
    // top: right below the field (bottom + 4)
    assert.equal(menu.style.top, '342px');
    // RTL: right edge of the menu aligns with the right edge of the field
    const width = Math.min(Math.max(field.width, 230), 1220 - 8 - 68);
    assert.equal(menu.style.left, String(Math.round(field.right - width)) + 'px');
    assert.equal(menu.style.width, String(Math.round(width)) + 'px');
    assert.equal(menu.attrs['data-menu-placed'], 'true');
});

test('placement does not depend on how many item rows the invoice has', () => {
    const f = setup();
    const modal = { getBoundingClientRect: () => ({ left: 60, top: 40, right: 1220, bottom: 680 }) };
    menuModal = modal;
    for (const rows of [1, 3, 5, 8]) {
        // هرچه ردیف بیشتر، فیلد پایین‌تر (بدنهٔ مودال اسکرول شده) — صفحه ثابت است.
        const top = 120 + rows * 46;
        const field = { left: 900, top, right: 1140, bottom: top + 38, width: 240, height: 38 };
        const anchor = makeAnchor(field, { modal });
        const menu = makeMenu({ left: 0, top: 0, right: 0, bottom: 0 }, { height: 250 });
        const ok = f.api.place(anchor, menu);
        assert.equal(ok, true, `rows=${rows}`);
        const placedTop = Number(menu.style.top.replace('px', ''));
        const distance = placedTop - field.bottom;
        // either right below (4px) or right above when there is no room below
        assert.ok(distance === 4 || placedTop + 250 <= field.top,
            `rows=${rows}: menu must stay attached to its field (top=${menu.style.top}, field=${field.top}..${field.bottom})`);
        assert.ok(placedTop >= 48 && placedTop + 250 <= 672, `rows=${rows}: menu must stay inside the modal box`);
    }
});

test('menu above the field when there is no room below (tall invoice)', () => {
    const f = setup();
    const modal = { getBoundingClientRect: () => ({ left: 60, top: 40, right: 1220, bottom: 680 }) };
    menuModal = modal;
    const field = { left: 900, top: 600, right: 1140, bottom: 638, width: 240, height: 38 };
    const menu = makeMenu({ left: 0, top: 0, right: 0, bottom: 0 }, { height: 250 });
    assert.equal(f.api.place(makeAnchor(field, { modal }), menu), true);
    const top = Number(menu.style.top.replace('px', ''));
    assert.ok(top + Number(menu.style.maxHeight.replace('px', '')) <= field.top + 1,
        'منو نباید روی فیلد بیفتد');
    assert.ok(top >= 48, 'منو نباید از بالای مودال بیرون بزند');
});

test('hidden field (inactive tab / scrolled away) keeps the menu hidden', () => {
    const f = setup();
    const modal = { getBoundingClientRect: () => ({ left: 60, top: 40, right: 1220, bottom: 680 }) };
    menuModal = modal;
    const menu = makeMenu({ left: 0, top: 0, right: 0, bottom: 0 });
    menu.closest = sel => (sel === '.modal-content' ? modal : null);

    const invisible = makeAnchor({ left: 900, top: 300, right: 1140, bottom: 338, width: 240, height: 38 }, { modal, visible: false });
    assert.equal(f.api.place(invisible, menu), false);
    assert.equal(menu.style.visibility, 'hidden');

    // فیلدی که بالای کادر مودال اسکرول شده (پشت سربرگ مودال)
    const offscreen = makeAnchor({ left: 900, top: -60, right: 1140, bottom: 8, width: 240, height: 38 }, { modal });
    menu.style.visibility = 'visible';
    assert.equal(f.api.place(offscreen, menu), false);
    assert.equal(menu.style.visibility, 'hidden');
    assert.equal(menu.attrs['data-menu-placed'], undefined);
});

test('fixed-origin offsets are removed so a transformed ancestor cannot shift the menu', () => {
    const f = setup();
    f.window.fixedOrigin = () => ({ left: 60, top: 40 });
    const modal = { getBoundingClientRect: () => ({ left: 60, top: 40, right: 1220, bottom: 680 }) };
    menuModal = modal;
    const field = { left: 900, top: 300, right: 1140, bottom: 338, width: 240, height: 38 };
    const menu = makeMenu({ left: 0, top: 0, right: 0, bottom: 0 });
    assert.equal(f.api.place(makeAnchor(field, { modal }), menu), true);
    assert.equal(menu.style.top, '302px');  // 342 - 40
    assert.equal(Number(menu.style.left.replace('px', '')) + 60 >= 60, true);
});

test('layout changes re-place the open menu and the modal is observed', () => {
    const f = setup();
    const body = { getBoundingClientRect: () => ({}) };
    const modal = { getBoundingClientRect: () => ({ left: 60, top: 40, right: 1220, bottom: 680 }), querySelector: () => body };
    menuModal = modal;
    const field = { left: 900, top: 300, right: 1140, bottom: 338, width: 240, height: 38 };
    const menu = makeMenu({ left: 0, top: 0, right: 0, bottom: 0 });
    menu.closest = sel => (sel === '.modal-content' ? modal : null);
    const anchor = makeAnchor(field, { modal });
    anchor.closest = sel => (sel === '.modal-content' ? modal : null);
    f.api.placeSoon(anchor, menu);
    assert.equal(f.observed.length, 2, 'مودال و بدنهٔ آن باید مشاهده شوند');
    const before = menu.style.top;
    // با بلندشدن فرم (ردیف تازه)، همان ناظر منو را از نو جای‌گذاری می‌کند
    field.top = 420; field.bottom = 458;
    f.state.menus = [Object.assign(menu, { _orderAnchor: anchor })];
    f.observed[0].cb();
    const after = Number(menu.style.top.replace('px', ''));
    assert.notEqual(menu.style.top, before, 'منو نباید روی مختصات کهنه بماند');
    // منو یا زیر فیلد تازه است یا (اگر جا نبود) درست بالای آن — نه جای قبلی
    assert.ok(after >= 462 || after + Number(menu.style.maxHeight.replace('px', '')) <= 421,
        `منو باید به فیلد تازه بچسبد (top=${menu.style.top})`);
});
