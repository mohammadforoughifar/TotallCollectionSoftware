/*
 * Optional real-Chromium smoke tests for the production menu JS/CSS. The small
 * fixture stands in for Blazor rendering/events; it is NOT the real app or API.
 * See README.md for the Playwright/Chromium prerequisites.
 */
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

let chromium;
try { ({ chromium } = require('playwright-core')); } catch { /* optional */ }
const executablePath = process.env.CHROMIUM_PATH;
const skip = !chromium || !executablePath
    ? 'install playwright-core and set CHROMIUM_PATH (see README)' : false;
const root = path.resolve(__dirname, '../..');
const www = path.join(root, 'src/Inventory.Client/wwwroot');
const read = file => fs.readFileSync(path.join(www, file), 'utf8');
const b64 = file => fs.readFileSync(path.join(www, file)).toString('base64');

const markup = `<!doctype html><html lang="fa" dir="rtl"><body>
    <div class="app-shell"><main class="app-main"><div class="app-content"><div class="tabpane on">
        <button id="before" type="button">قبل</button><button id="outside" type="button">بیرون منو</button>
        <div class="card"><div class="card-body p-0">
            <div class="table-responsive grid-desktop" style="max-height:320px; overflow:auto">
                <table class="table table-hover align-middle" style="min-width:1050px">
                    <thead><tr><th>پذیرش</th><th>دستگاه</th><th>مشتری</th><th>تعمیرکار</th><th>ورود</th><th>خروج</th><th>دریافتی</th><th>سود</th><th>وضعیت</th><th>عملیات</th></tr></thead>
                    <tbody>
                        <tr><td>RP-۱</td><td>لپ‌تاپ</td><td>مشتری اول</td><td>—</td><td>۱۴۰۵/۰۸/۱۰</td><td>—</td><td>۱,۰۰۰,۰۰۰</td><td>۳۰۰,۰۰۰</td>
                            <td><span class="repair-status-control"><button id="desk-a" class="badge repair-status-badge repair-status-button repair-status-ready" data-status="ready" data-number="RP-1" aria-haspopup="menu" aria-expanded="false"><span class="repair-status-content"><i class="bi bi-hourglass-split"></i><span>آماده تحویل</span></span><i class="bi bi-chevron-down repair-status-chevron"></i></button></span><div class="fs-8 text-muted mt-1">پیامک آماده تحویل: در صف</div></td>
                            <td class="text-end text-nowrap"><button id="desk-details-a" class="btn btn-sm btn-light">جزئیات</button><button class="btn btn-sm btn-light">فیش</button><button class="btn btn-sm btn-light">ویرایش</button><button class="btn btn-sm btn-light">تحویل</button><button class="btn btn-sm btn-light">حذف</button></td>
                        </tr>
                        <tr><td>RP-۲</td><td>موبایل</td><td>مشتری دوم</td><td>—</td><td>۱۴۰۵/۰۸/۱۰</td><td>—</td><td>۰</td><td>۰</td>
                            <td><span class="repair-status-control"><button id="desk-b" class="badge repair-status-badge repair-status-button badge-soft-primary" data-status="received" data-number="RP-2" aria-haspopup="menu" aria-expanded="false"><span class="repair-status-content"><span>پذیرش شده</span></span><i class="bi bi-chevron-down repair-status-chevron"></i></button></span></td><td><button class="btn btn-light">جزئیات</button></td></tr>
                    </tbody>
                </table>
            </div>
            <div class="cards-mobile">
                <div class="m-card repair-card st-ready"><div class="m-card-head"><div class="m-card-icon"><i class="bi bi-laptop"></i></div><div class="flex-grow-1 min-w-0"><div class="d-flex align-items-center gap-2"><span class="doc-number">RP-۱</span><span class="repair-status-control"><button id="mobile-a" class="badge repair-status-badge repair-status-button repair-status-ready" data-status="ready" data-number="RP-1" aria-haspopup="menu" aria-expanded="false"><span class="repair-status-content"><i class="bi bi-hourglass-split"></i><span>آماده تحویل</span></span><i class="bi bi-chevron-down repair-status-chevron"></i></button></span></div><div>لپ‌تاپ</div><div>مشتری اول</div><div class="fs-8 text-muted">پیامک آماده تحویل: در صف</div></div></div>
                <div class="m-card-body"><div class="m-card-row"><span>تعمیرکار</span><b>—</b></div><div class="m-card-row"><span>ورود</span><b>۱۴۰۵/۰۸/۱۰</b></div><div class="m-card-money"><div>دریافتی ۱,۰۰۰,۰۰۰</div><div>سود ۳۰۰,۰۰۰</div></div></div>
                <div class="m-card-actions repair-card-actions"><button id="mobile-details-a" class="btn btn-light">جزئیات</button><button class="btn btn-light">فیش تحویل</button><button class="btn btn-light">ویرایش</button><button class="btn btn-light">تبدیل به فروش</button><button class="btn btn-light">حذف</button></div></div>
                <div class="m-card repair-card st-cancelled"><div class="m-card-head"><div>RP-۲</div><span class="repair-status-control"><button id="mobile-b" class="badge repair-status-badge repair-status-button badge-soft-danger" data-status="cancelled" data-number="RP-2" aria-haspopup="menu" aria-expanded="false"><span class="repair-status-content"><span>انصراف</span></span><i class="bi bi-chevron-down repair-status-chevron"></i></button></span></div></div>
                <div class="m-card repair-card st-delivered"><div class="m-card-head"><span id="delivered" class="badge repair-status-badge repair-status-delivered"><span class="repair-status-content"><i class="bi bi-check-circle-fill"></i><span>تحویل شده</span></span></span></div></div>
            </div>
        </div></div>
        <div id="menu-root"></div>
        <button id="after" type="button">بعد</button>
        <div id="lab" hidden><button id="lab-anchor" class="badge repair-status-badge repair-status-button repair-status-ready" data-status="ready" data-number="RP-3" aria-haspopup="menu" aria-expanded="false">آماده تحویل</button></div>
    </div></div></main></div>
</body></html>`;

async function setup(browser, viewport) {
    const page = await browser.newPage({ viewport });
    page.on('pageerror', error => console.error('browser fixture error:', error.message));
    await page.setContent(markup);
    await page.addStyleTag({ content: read('css/bootstrap.min.css') });
    await page.addStyleTag({ content: read('icons/bootstrap-icons.css') });
    await page.addStyleTag({ content: read('css/app.css') });
    await page.addStyleTag({ content: `
        @font-face { font-family: Vazirmatn; src: url(data:font/woff2;base64,${b64('fonts/Vazirmatn-Regular.woff2')}); font-weight:400; }
        @font-face { font-family: Vazirmatn; src: url(data:font/woff2;base64,${b64('fonts/Vazirmatn-Bold.woff2')}); font-weight:700; }
        @font-face { font-family: bootstrap-icons; src: url(data:font/woff2;base64,${b64('icons/fonts/bootstrap-icons.woff2')}); }
        .app-content { animation: none !important; } .tabpane.on { animation: none !important; }
        #lab { position:fixed; top:105px; right:20px; z-index:1; }
    ` });
    await page.addScriptTag({ path: path.join(www, 'js/repair-status-menu.js') });
    await page.evaluate(() => {
        let current = null, serial = 0;
        window.closeDelay = 0;
        window.outsideClicks = 0;
        window.globalEscapes = 0;
        window.choices = [];
        window.fixtureEvents = [];
        for (const name of ['scroll', 'resize', 'focusin', 'pointerdown'])
            window.addEventListener(name, e => window.fixtureEvents.push(name + ':' + (e.target.id || e.target.className || e.target.tagName || 'window')), true);
        document.getElementById('outside').addEventListener('click', () => window.outsideClicks++);
        document.addEventListener('keydown', e => { if (e.key === 'Escape') window.globalEscapes++; }, true);
        function removeCurrent() {
            if (!current) return;
            window.repairStatusMenu.release(current.menu.id, false);
            current.anchor.setAttribute('aria-expanded', 'false');
            current.menu.remove();
            current = null;
        }
        function open(anchor) {
            if (current && current.anchor === anchor) { removeCurrent(); return; }
            removeCurrent();
            const version = ++serial;
            const menu = document.createElement('div');
            menu.id = 'status-menu-' + version;
            menu.className = 'repair-status-menu';
            menu.setAttribute('role', 'menu');
            menu.style.visibility = 'hidden';
            menu.innerHTML = `<button type="button" hidden tabindex="-1" aria-hidden="true" data-repair-status-dismiss></button>
                <div class="repair-status-menu-head" role="presentation">تغییر وضعیت <span>${anchor.dataset.number}</span></div>`;
            const statuses = [['received','پذیرش شده','bi-inbox'], ['progress','در حال تعمیر','bi-tools'],
                ['ready','آماده تحویل','bi-hourglass-split'], ['delivery','تحویل دستگاه و تسویه','bi-box-arrow-up'],
                ['cancelled','انصراف','bi-x-circle']];
            for (const [value, label, icon] of statuses) {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'repair-status-option' + (value === anchor.dataset.status ? ' is-current' : '') + (value === 'delivery' ? ' repair-status-delivery' : '');
                button.setAttribute('role', value === 'delivery' ? 'menuitem' : 'menuitemradio');
                button.tabIndex = -1;
                if (value !== 'delivery') button.setAttribute('aria-checked', String(value === anchor.dataset.status));
                if (value === 'delivery' && anchor.dataset.status === 'cancelled') button.disabled = true;
                button.dataset.value = value;
                button.innerHTML = `<i class="bi ${icon}"></i><span>${label}</span>` + (value === anchor.dataset.status ? '<i class="bi bi-check2 repair-status-option-end"></i>' : '');
                button.addEventListener('click', () => {
                    window.choices.push(value);
                    window.repairStatusMenu.release(menu.id, true);
                    removeCurrent();
                });
                menu.append(button);
            }
            const close = menu.querySelector('[data-repair-status-dismiss]');
            close.addEventListener('click', () => setTimeout(() => {
                if (current && current.version === version) removeCurrent();
            }, window.closeDelay));
            document.getElementById('menu-root').append(menu);
            current = { anchor, menu, version };
            anchor.setAttribute('aria-expanded', 'true');
            if (!window.repairStatusMenu.open(anchor.id, menu.id)) removeCurrent();
        }
        document.querySelectorAll('[data-status]').forEach(anchor => anchor.addEventListener('click', () => open(anchor)));
    });
    return page;
}

async function assertInViewportAndClickable(page, anchorId) {
    await page.locator('#' + anchorId).click();
    const menu = page.locator('.repair-status-menu');
    try { await menu.waitFor({ state: 'visible', timeout: 5000 }); }
    catch (error) {
        console.error('fixture state:', await page.evaluate(() => ({ html: document.getElementById('menu-root').innerHTML, events: window.fixtureEvents, active: document.activeElement.id })));
        throw error;
    }
    const geometry = await menu.evaluate(el => {
        const box = el.getBoundingClientRect();
        const option = el.querySelector('[role="menuitemradio"]');
        const o = option.getBoundingClientRect();
        return { left: box.left, right: box.right, top: box.top, bottom: box.bottom,
            hit: document.elementFromPoint(o.left + o.width / 2, o.top + o.height / 2)?.closest('.repair-status-menu') === el,
            width: innerWidth, height: innerHeight };
    });
    assert.ok(geometry.left >= 7 && geometry.right <= geometry.width - 7, JSON.stringify(geometry));
    assert.ok(geometry.top >= 7 && geometry.bottom <= geometry.height - 7, JSON.stringify(geometry));
    assert.equal(geometry.hit, true, 'menu option is clickable above table/card');
    assert.equal(await menu.locator('[role="menuitemradio"]').count(), 4);
    assert.equal(await menu.locator('[role="menuitem"]').count(), 1);
    assert.equal(await menu.getByText('در انتظار مشتری').count(), 0);
}

test('production status popover in desktop and mobile Chromium', { skip }, async t => {
    const browser = await chromium.launch({ executablePath, headless: true,
        args: ['--no-sandbox', '--disable-dev-shm-usage', '--no-zygote', '--single-process'] });
    t.after(() => browser.close());

    await t.test('desktop table: positioned outside clipping, keyboard and outside click', async () => {
        const page = await setup(browser, { width: 1280, height: 700 });
        try {
            await assertInViewportAndClickable(page, 'desk-a');
            if (process.env.STATUS_SCREENSHOTS_DIR)
                await page.screenshot({ path: path.join(process.env.STATUS_SCREENSHOTS_DIR, 'desktop.png') });
            assert.equal(await page.evaluate(() => document.activeElement.dataset.value), 'ready');
            await page.keyboard.press('ArrowDown');
            assert.equal(await page.evaluate(() => document.activeElement.dataset.value), 'delivery');
            await page.keyboard.press('End');
            assert.equal(await page.evaluate(() => document.activeElement.dataset.value), 'cancelled');
            await page.keyboard.press('Home');
            assert.equal(await page.evaluate(() => document.activeElement.dataset.value), 'received');
            await page.keyboard.press('Escape');
            await page.waitForSelector('.repair-status-menu', { state: 'detached' });
            assert.equal(await page.evaluate(() => document.activeElement.id), 'desk-a');
            assert.equal(await page.evaluate(() => window.globalEscapes), 0);

            await page.keyboard.press('Enter');
            await page.locator('.repair-status-menu').waitFor({ state: 'visible' });
            await page.keyboard.press('Tab');
            await page.waitForSelector('.repair-status-menu', { state: 'detached' });
            assert.equal(await page.evaluate(() => document.activeElement.id), 'desk-details-a');
            await page.locator('#desk-a').click();
            await page.keyboard.press('Shift+Tab');
            await page.waitForSelector('.repair-status-menu', { state: 'detached' });
            assert.equal(await page.evaluate(() => document.activeElement.id), 'outside');

            await page.locator('#desk-a').click();
            await page.locator('#outside').click();
            await page.waitForSelector('.repair-status-menu', { state: 'detached' });
            assert.equal(await page.evaluate(() => window.outsideClicks), 1, 'outside click is not swallowed');

            await page.evaluate(() => { window.closeDelay = 40; });
            await page.locator('#desk-a').click();
            await page.locator('#desk-b').click();
            await page.waitForTimeout(80);
            assert.equal((await page.locator('.repair-status-menu-head').innerText()).replace(/\s+/g, ' ').trim(), 'تغییر وضعیت RP-2');
            assert.equal(await page.locator('#desk-b').getAttribute('aria-expanded'), 'true');
            await page.locator('#desk-b').click();
            assert.equal(await page.locator('.repair-status-menu').count(), 0, 'same badge toggles closed');

            await page.evaluate(() => { window.closeDelay = 0; });
            await page.locator('#desk-a').click();
            await page.evaluate(() => document.querySelector('.grid-desktop').dispatchEvent(new Event('scroll')));
            await page.waitForTimeout(30);
            assert.equal(await page.locator('.repair-status-menu').isVisible(), true,
                'a queued table scroll with an unchanged anchor does not close the menu');
            const moved = await page.evaluate(() => {
                const table = document.querySelector('.grid-desktop');
                const before = table.scrollLeft;
                table.scrollLeft = -40;
                return table.scrollLeft !== before;
            });
            assert.equal(moved, true, 'fixture table is horizontally scrollable');
            await page.waitForSelector('.repair-status-menu', { state: 'detached' });

            await page.locator('#desk-a').click();
            await page.locator('[data-value="progress"]').click();
            assert.deepEqual(await page.evaluate(() => window.choices), ['progress']);
            assert.equal(await page.locator('.repair-status-menu').count(), 0);
            assert.equal(await page.evaluate(() => document.activeElement.id), 'desk-a');
        } finally { /* Browser cleanup closes the fixture pages. */ }
    });

    await t.test('mobile card: wrapping, placement, disabled delivery and resize dismissal', async () => {
        const page = await setup(browser, { width: 320, height: 640 });
        try {
            const layout = await page.locator('.repair-card-actions').first().evaluate(el => {
                const box = el.getBoundingClientRect();
                const buttons = Array.from(el.querySelectorAll('button')).map(x => x.getBoundingClientRect());
                return { box: { left: box.left, right: box.right }, buttons: buttons.map(b => ({ left: b.left, right: b.right, top: b.top })) };
            });
            assert.ok(layout.buttons.every(b => b.left >= layout.box.left - 1 && b.right <= layout.box.right + 1), JSON.stringify(layout));
            assert.ok(new Set(layout.buttons.map(b => b.top)).size > 1, 'actions wrap instead of overflowing');
            assert.equal(await page.locator('#delivered i').evaluate(el => getComputedStyle(el).color), 'rgb(22, 131, 77)');
            await assertInViewportAndClickable(page, 'mobile-a');
            if (process.env.STATUS_SCREENSHOTS_DIR)
                await page.screenshot({ path: path.join(process.env.STATUS_SCREENSHOTS_DIR, 'mobile.png') });
            await page.setViewportSize({ width: 390, height: 640 });
            await page.waitForSelector('.repair-status-menu', { state: 'detached' });

            await assertInViewportAndClickable(page, 'mobile-b');
            assert.equal(await page.locator('[data-value="delivery"]').isDisabled(), true);
            assert.equal(await page.evaluate(() => document.activeElement.dataset.value), 'cancelled');
            await page.keyboard.press('ArrowUp');
            assert.equal(await page.evaluate(() => document.activeElement.dataset.value), 'ready', 'disabled delivery is skipped');
            await page.keyboard.press('Escape');
            await page.waitForSelector('.repair-status-menu', { state: 'detached' });
            await page.keyboard.press('Enter');
            await page.keyboard.press('ArrowUp');
            await page.keyboard.press('Enter');
            await page.waitForSelector('.repair-status-menu', { state: 'detached' });
            assert.deepEqual(await page.evaluate(() => window.choices), ['ready'], 'keyboard can select a status');
            assert.equal(await page.evaluate(() => document.activeElement.id), 'mobile-b');
        } finally { /* Browser cleanup closes the fixture pages. */ }
    });

    await t.test('short viewport: menu scrolls and active item remains visible', async () => {
        const page = await setup(browser, { width: 320, height: 230 });
        try {
            await page.evaluate(() => { document.getElementById('lab').hidden = false; });
            await page.locator('#lab-anchor').click();
            const menu = page.locator('.repair-status-menu');
            await menu.waitFor({ state: 'visible' });
            const state = await menu.evaluate(el => {
                const b = el.getBoundingClientRect(), a = document.activeElement.getBoundingClientRect();
                return { top: b.top, bottom: b.bottom, h: innerHeight, scroll: el.scrollHeight > el.clientHeight,
                    activeTop: a.top, activeBottom: a.bottom, active: document.activeElement.dataset.value };
            });
            assert.ok(state.top >= 7 && state.bottom <= state.h - 7, JSON.stringify(state));
            assert.equal(state.scroll, true, 'menu height is bounded');
            assert.equal(state.active, 'ready');
            assert.ok(state.activeTop >= state.top - 1 && state.activeBottom <= state.bottom + 1, JSON.stringify(state));
            await page.keyboard.press('End');
            assert.equal(await page.evaluate(() => document.activeElement.dataset.value), 'cancelled');
            await page.waitForTimeout(30);
            assert.equal(await menu.isVisible(), true, 'scroll within menu does not dismiss it');
            await page.evaluate(() => { document.getElementById('lab').hidden = true; });
            await page.waitForSelector('.repair-status-menu', { state: 'detached' });
        } finally { /* Browser cleanup closes the fixture pages. */ }
    });
});
