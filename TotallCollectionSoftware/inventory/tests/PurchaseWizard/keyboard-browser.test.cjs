'use strict';

// Real-DOM fixture, not a substitute for testing the running Blazor application.
// Exercises the production shortcut handler, the legacy handler, and real CSS.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
let puppeteer;
try { puppeteer = require('puppeteer-core'); }
catch (error) {
    if (error.code !== 'MODULE_NOT_FOUND' || !error.message.includes("'puppeteer-core'")) throw error;
}
const chrome = process.env.CHROME_BIN;
const skip = !puppeteer || !chrome ? 'Set CHROME_BIN and install puppeteer-core.' : false;
const root = path.resolve(__dirname, '../..');
const www = path.join(root, 'src/Inventory.Client/wwwroot');
const read = relative => fs.readFileSync(path.join(www, relative), 'utf8');
const orderScript = read('js/order-entry.js');
const index = read('index.html');
const legacyScript = [...index.matchAll(/<script>([\s\S]*?)<\/script>/g)]
    .map(match => match[1]).find(script => script.includes('var addLineBtn'));
assert.ok(legacyScript, 'legacy key handler fixture must be present');
const css = read('css/bootstrap.min.css') + '\n' + read('css/app.css') + '\n' +
    fs.readFileSync(path.join(root, 'src/Inventory.Client/Pages/Orders/OrderForm.razor.css'), 'utf8').replace(/::deep\s*/g, '');

const fixture = `<!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1"><style>${css}
.tabpane:not(.on){display:none!important} .vis-modal-back{position:fixed;inset:0;z-index:3600;background:#0008}
</style></head><body>
<div class="tabpane on" data-shop-scope="true" id="shop">
  <input id="shop-field"><button id="page-save" data-hotkey="f1">ثبت</button>
  <button id="page-add" data-hotkey="f2">ردیف</button><button id="page-back" data-hotkey="f3">قبل</button>
  <button id="page-print" data-hotkey="f4">چاپ</button>
  <table><tbody><tr id="row-a" tabindex="0" data-order-copy-context><td>فاکتور الف</td><td><button id="copy-a" data-order-copy>کپی</button></td></tr>
  <tr id="row-b" tabindex="0" data-order-copy-context><td>فاکتور ب</td><td><button id="copy-b" data-order-copy>کپی</button></td></tr></tbody></table>
  <div class="modal fade" id="detail"><div class="modal-dialog"><div class="modal-content">
    <div class="modal-body"><input id="detail-field"></div>
    <div class="modal-footer"><button id="detail-copy" data-hotkey="f7">کپی فاکتور</button></div>
  </div></div></div>
  <div class="modal fade" id="invoice"><div class="modal-dialog modal-dialog-centered modal-xl"><div class="modal-content">
    <div class="modal-header"><button id="invoice-close" data-hotkey="esc">بستن</button></div>
    <div class="modal-body"><div data-order-wizard="invoice-key">
       <input id="invoice-field"><div class="ss" id="search"><input id="search-field"></div>
       <div class="date-picker"><input id="date-field"></div>
       <div data-order-line="a"><input id="line-a"><button id="last-a" data-order-last-purchase>آخرین خرید الف</button></div>
       <div data-order-line="b"><input id="line-b"><button id="last-b" data-order-last-purchase>آخرین خرید ب</button></div>
       <div data-order-line="service"><input id="line-service"></div>
    </div></div><div class="modal-footer">
       <button id="invoice-add" data-hotkey="f2">افزودن</button><button id="invoice-back" data-hotkey="f3">قبل</button>
       <button id="invoice-print" data-hotkey="f4">چاپ</button><button id="invoice-save" data-hotkey="f1">ثبت</button>
    </div></div></div></div>
  <div class="modal fade" id="child"><div class="modal-dialog"><div class="modal-content">
    <div class="modal-header"><button id="child-close" data-hotkey="esc">بستن</button></div>
    <div class="modal-body"><div data-order-child="invoice-key"><input id="child-field"></div></div>
    <div class="modal-footer"><button id="child-save" data-hotkey="f1">ثبت فرزند</button></div>
  </div></div></div>
  <div class="vis-modal-back" data-shop-dialog id="custom-overlay" style="display:none">
    <input id="overlay-field"><button id="overlay-save" data-hotkey="f1">تأیید</button><button id="overlay-close" data-hotkey="esc">انصراف</button>
  </div>
</div>
<div class="tabpane" id="other"><input id="other-field"><button id="other-save" data-hotkey="f1">ماژول دیگر</button></div>
<div class="toast-container toast-container-top-right"><div class="toast-item error" id="toast">پیام ثبت</div></div>
<script>
window.actions=[];
document.addEventListener('click',e=>{ const el=e.target.closest('button,a'); if(el&&el.id) actions.push(el.id); });
document.getElementById('search-field').addEventListener('keydown',e=>{
  if(e.key==='Escape') document.querySelector('#search .ss-menu')?.remove();
});
</script></body></html>`;

async function createPage(browser, width = 1280) {
    const page = await browser.newPage();
    await page.setViewport({ width, height: 844, deviceScaleFactor: 1 });
    await page.setContent(fixture, { waitUntil: 'load' });
    await page.addScriptTag({ content: orderScript });
    await page.addScriptTag({ content: legacyScript });
    return page;
}
const actions = page => page.evaluate(() => window.actions.splice(0));
const show = (page, id) => page.evaluate(id => document.getElementById(id).className = 'modal fade show d-block', id);

// Wait until ResizeObserver / MutationObserver have updated the notice stack.
async function settled(page) {
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
}

test('shop shortcuts and notification stacking in Chromium', { skip, timeout: 30000 }, async t => {
    const browser = await puppeteer.launch({ executablePath: chrome, headless: true, args: ['--no-sandbox', '--disable-setuid-sandbox'] });
    t.after(() => browser.close());

    await t.test('F1/F2/F3/F4 are scoped and F7 copies the focused row only', async () => {
        const page = await createPage(browser);
        try {
            await page.focus('#shop-field');
            for (const key of ['F1', 'F2', 'F3', 'F4', 'F7']) await page.keyboard.press(key);
            assert.deepEqual(await actions(page), ['page-save', 'page-add', 'page-back', 'page-print']);
            await page.keyboard.press('Tab');
            await page.focus('#row-b');
            assert.equal(await page.$eval('#row-b', row => getComputedStyle(row).outlineStyle), 'solid');
            await page.keyboard.press('F7');
            await page.focus('#row-a');
            await page.keyboard.press('F7');
            assert.deepEqual(await actions(page), ['copy-b', 'copy-a']);
            await page.evaluate(() => { document.getElementById('copy-a').disabled = true; });
            await page.keyboard.press('F7');
            assert.deepEqual(await actions(page), []);

            // A hidden shop tab must not steal legacy F1 from the active non-shop tab.
            await page.evaluate(() => { shop.classList.remove('on'); other.classList.add('on'); });
            await page.focus('#other-field');
            await page.keyboard.press('F1');
            assert.deepEqual(await actions(page), ['other-save']);
        } finally { await page.close(); }
    });

    await t.test('F7 still copies an invoice from its details dialog', async () => {
        const page = await createPage(browser);
        try {
            await show(page, 'detail');
            await page.focus('#detail-field'); await page.keyboard.press('F7');
            assert.deepEqual(await actions(page), ['detail-copy']);
            await page.evaluate(() => { document.getElementById('detail-copy').disabled = true; });
            await page.keyboard.press('F7');
            assert.deepEqual(await actions(page), []);
        } finally { await page.close(); }
    });

    await t.test('F7 and F8 use the focused product row, never an unrelated or covered row', async () => {
        const page = await createPage(browser);
        try {
            await show(page, 'invoice');
            await page.focus('#line-a'); await page.keyboard.press('F7');
            await page.focus('#line-b'); await page.keyboard.press('F8');
            assert.deepEqual(await actions(page), ['last-a', 'last-b']);
            await page.evaluate(() => { document.getElementById('last-b').disabled = true; });
            await page.keyboard.press('F7');
            await page.focus('#line-service'); await page.keyboard.press('F7');
            await page.focus('#invoice-field'); await page.keyboard.press('F7');
            assert.deepEqual(await actions(page), []);
            await show(page, 'child'); await page.focus('#child-field'); await page.keyboard.press('F7');
            await page.evaluate(() => document.getElementById('line-a').dispatchEvent(
                new KeyboardEvent('keydown', { key:'F7', bubbles:true, cancelable:true })));
            assert.deepEqual(await actions(page), []);
        } finally { await page.close(); }
    });

    await t.test('nested invoice, child and custom dialog never trigger obscured actions', async () => {
        const page = await createPage(browser);
        try {
            await show(page, 'invoice');
            await page.focus('#invoice-field');
            for (const key of ['F1', 'F2', 'F3', 'F4', 'Escape']) await page.keyboard.press(key);
            assert.deepEqual(await actions(page), ['invoice-save', 'invoice-add', 'invoice-back', 'invoice-print', 'invoice-close']);
            await page.evaluate(() => {
                document.getElementById('invoice-save').disabled = true;
                document.getElementById('invoice-field').dispatchEvent(new KeyboardEvent('keydown', { key:'F1', repeat:true, bubbles:true, cancelable:true }));
            });
            await page.keyboard.press('F1');
            await page.keyboard.down('Control'); await page.keyboard.press('F2'); await page.keyboard.up('Control');
            assert.deepEqual(await actions(page), []);
            await show(page, 'child');
            await page.focus('#child-field');
            for (const key of ['F1', 'F2', 'F3', 'F4', 'F7', 'Escape']) await page.keyboard.press(key);
            assert.deepEqual(await actions(page), ['child-save', 'child-close']);
            await page.evaluate(() => document.getElementById('custom-overlay').style.display = 'block');
            await page.focus('#overlay-field');
            for (const key of ['F1', 'F2', 'Escape']) await page.keyboard.press(key);
            assert.deepEqual(await actions(page), ['overlay-save', 'overlay-close']);
        } finally { await page.close(); }
    });

    await t.test('Escape dismisses an open search or calendar before the invoice', async () => {
        const page = await createPage(browser);
        try {
            await show(page, 'invoice');
            await page.evaluate(() => {
                const menu = document.createElement('div'); menu.className = 'ss-menu'; menu.textContent = 'گزینه';
                document.getElementById('search').appendChild(menu);
            });
            await page.focus('#search-field');
            await page.keyboard.press('Escape');
            assert.equal(await page.$('#search .ss-menu'), null);
            assert.deepEqual(await actions(page), []);
            await page.keyboard.press('Escape');
            assert.deepEqual(await actions(page), ['invoice-close']);
            await page.evaluate(() => {
                const cal = document.createElement('div'); cal.className = 'dp-popover'; cal.textContent = 'تقویم';
                cal.addEventListener('dpclose', () => { actions.push('calendar-close'); cal.remove(); });
                document.querySelector('.date-picker').appendChild(cal);
            });
            await page.focus('#date-field');
            await page.keyboard.press('Escape');
            assert.deepEqual(await actions(page), ['calendar-close']);
            await page.keyboard.press('Escape');
            assert.deepEqual(await actions(page), ['invoice-close']);
        } finally { await page.close(); }
    });

    for (const width of [1280, 390]) await t.test(`invoice errors and toasts do not overlap at ${width}px`, async () => {
        const page = await createPage(browser, width);
        try {
            await show(page, 'invoice');
            await page.evaluate(() => {
                const error = document.createElement('div'); error.className = 'order-form-error';
                const text = document.createElement('span'); text.textContent = 'لطفاً اطلاعات فاکتور را بررسی و تکمیل کنید. '.repeat(12);
                const close = document.createElement('button'); close.id = 'dismiss-error'; close.textContent = '×';
                close.addEventListener('click', () => error.remove());
                error.append(text, close);
                document.querySelector('#invoice .modal-body').prepend(error);
            });
            await settled(page);
            const geometry = await page.evaluate(() => {
                const e = document.querySelector('.order-form-error').getBoundingClientRect();
                const t = document.getElementById('toast').getBoundingClientRect();
                return { error: { top:e.top, bottom:e.bottom, left:e.left, right:e.right }, toast: { top:t.top, bottom:t.bottom, left:t.left, right:t.right }, width:innerWidth, height:innerHeight };
            });
            assert.ok(geometry.error.top >= 0 && geometry.error.right <= geometry.width, JSON.stringify(geometry));
            assert.ok(geometry.toast.top >= geometry.error.bottom + 8, JSON.stringify(geometry));
            assert.ok(geometry.toast.right <= geometry.width && geometry.toast.left >= 0 && geometry.toast.bottom <= geometry.height, JSON.stringify(geometry));
            await page.evaluate(() => {
                const host = document.querySelector('.toast-container-top-right');
                for (let i = 0; i < 5; i++) {
                    const toast = document.createElement('div'); toast.className = 'toast-item error';
                    toast.textContent = 'پیام طولانی ثبت فاکتور و توضیحات تکمیلی '.repeat(10); host.appendChild(toast);
                }
            });
            await settled(page);
            assert.ok(await page.$eval('.toast-container-top-right', host => host.scrollHeight > host.clientHeight));
            await page.evaluate(() => { shop.classList.remove('on'); other.classList.add('on'); });
            await settled(page);
            assert.equal(await page.evaluate(() => document.documentElement.style.getPropertyValue('--invoice-error-stack-offset')), '0px');
            await page.evaluate(() => { other.classList.remove('on'); shop.classList.add('on'); });
            await settled(page);
            assert.ok(await page.evaluate(() => parseInt(document.documentElement.style.getPropertyValue('--invoice-error-stack-offset'), 10)) > 0);
            await page.click('#dismiss-error');
            await settled(page);
            assert.equal(await page.evaluate(() => document.documentElement.style.getPropertyValue('--invoice-error-stack-offset')), '0px');
            assert.ok(await page.$eval('#toast', t => t.getBoundingClientRect().top) < 25);
        } finally { await page.close(); }
    });
});
