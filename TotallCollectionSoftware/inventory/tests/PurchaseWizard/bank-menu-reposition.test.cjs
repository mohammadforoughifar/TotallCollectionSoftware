'use strict';

// Reproduction test for the "لیست بانک" misposition bug:
// with more than three line items, .modal-body scrolls internally; the fixed
// SearchSelect menu must reposition on that internal scroll, not only on window scroll.

const { JSDOM } = require('jsdom');
const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '../..');
const script = fs.readFileSync(path.join(root, 'src/Inventory.Client/wwwroot/js/order-entry.js'), 'utf8');

function buildDom() {
    const dom = new JSDOM(`<!doctype html><html><body>
        <div class="modal fade show d-block">
            <div class="modal-dialog modal-dialog-centered">
                <div class="modal-content" id="modal-content">
                    <div class="modal-header">header</div>
                    <div class="modal-body" id="modal-body" style="overflow-y:auto">
                        <div data-order-wizard="key">
                            <div class="ss">
                                <input id="bank-input" class="form-control" />
                                <div class="ss-menu" id="bank-menu" style="position:fixed"
                                     data-order-constrained="true"></div>
                            </div>
                        </div>
                    </div>
                    <div class="modal-footer">footer</div>
                </div>
            </div>
        </div>
    </body></html>`, { runScripts: 'outside-only' });

    const { window } = dom;

    // Stub geometry: input near the top of the scrollable body; body has 2000px content.
    const input = window.document.getElementById('bank-input');
    const menu = window.document.getElementById('bank-menu');
    const body = window.document.getElementById('modal-body');
    const content = window.document.getElementById('modal-content');

    let bodyScroll = 0;
    const vh = 800, vw = 1200;
    window.innerHeight = vh;
    window.innerWidth = vw;

    function rect(top, left, width, height) {
        return { left, top, right: left + width, bottom: top + height, width, height, x: left, y: top,
                 toJSON() { return this; } };
    }

    input.getBoundingClientRect = () => rect(200 - bodyScroll, 300, 200, 32);
    menu.getBoundingClientRect = () => {
        // rendered menu: 200 wide, ~180 tall (natural height capped at 250)
        return rect(0, 0, 200, 180);
    };
    body.getBoundingClientRect = () => rect(50, 20, vw - 60, vh - 120);
    content.getBoundingClientRect = () => rect(40, 10, vw - 40, vh - 80);
    Object.defineProperty(body, 'scrollTop', {
        get: () => bodyScroll,
        set: v => { bodyScroll = v; },
        configurable: true,
    });
    Object.defineProperty(menu, 'isConnected', {
        get: () => menu.parentNode && menu.parentNode.nodeType === 1,
        configurable: true,
    });

    // Execute the production script in this window.
    window.eval(script);

    return { window, input, menu, body, content, getBodyScroll: () => bodyScroll,
             setBodyScroll: v => { bodyScroll = v; } };
}

function placeMenu(env, scrollTop) {
    env.setBodyScroll(scrollTop);
    env.window.orderEntry.placeSearchMenu(env.input, env.menu);
    return { left: env.menu.style.left, top: env.menu.style.top, vis: env.menu.style.visibility };
}

// ---- Scenario ----
const env = buildDom();
const { window, input, menu, body } = env;

// 1) Open at scrollTop=0 (menu placed below the input at top ~200).
let placed = placeMenu(env, 0);
console.log('initial placement:', placed);
if (placed.top !== '236px') {
    console.error('FAIL: expected menu below the input (top=236px), got', placed.top);
    process.exit(1);
}

// 2) User scrolls the modal body down (like scrolling past >3 line items).
env.setBodyScroll(400);

// 3) Scroll event fires on .modal-body (capture listener on .modal-content must catch it).
const ev = new window.Event('scroll', { bubbles: false });
body.dispatchEvent(ev);

const after = { left: menu.style.left, top: menu.style.top, vis: menu.style.visibility };
console.log('after internal scroll:', after);

// The input has scrolled out of the modal viewport, so the menu must be hidden
// (repositioned). Without the patch, the stale focus-time menu stays visible.
if (after.vis !== 'hidden') {
    console.error('FAIL: menu stayed visible at stale focus-time position (bug reproduced).');
    process.exit(1);
}
console.log('PASS: menu repositioned on .modal-body scroll (fixed coordinates re-evaluated).');
