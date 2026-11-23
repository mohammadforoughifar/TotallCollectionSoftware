// Run only against the synthetic fixture host, never a production letter server.
const { chromium, expect } = require('playwright/test');
const fs = require('node:fs');
const path = require('node:path');
const base = process.env.OUTGOING_TEST_URL || 'http://127.0.0.1:5131';
const screenshots = process.env.OUTGOING_SCREENSHOTS;
let checks = 0;
const pass = text => { checks++; console.log('PASS: ' + text); };
const photo = (scope, id) => scope.locator(`img.user-avatar[src*="/api/auth/users/photo/${id}?"]`);
async function loaded(image) {
    await expect(image).toHaveCount(1);
    await expect.poll(() => image.evaluate(e => e.complete && e.naturalWidth > 0)).toBeTruthy();
}
async function allPhotos(scope, ids) {
    for (const id of ids) await loaded(photo(scope, id).first());
    await expect(scope.locator('img[src*="/api/SystemUsers/photo/"]')).toHaveCount(0);
}
async function saveShot(page, name) {
    if (!screenshots) return;
    fs.mkdirSync(screenshots, { recursive: true });
    await page.screenshot({ path: path.join(screenshots, name + '.png') });
}
async function openFromList(page) {
    const row = page.locator('.pane .mail').first();
    await row.locator('.dd > button').click();
    await row.locator('.ltmenu').getByRole('button', { name: /گردش نامه/ }).click();
    // The outer wrapper has zero layout height because its overlay is position:fixed.
    await expect(page.locator('.outgoing-letter-gardesh .gmodal')).toBeVisible();
    await expect(page.locator('.outgoing-letter-gardesh .gmodal')).toHaveCSS('opacity', '1');
}
(async () => {
    const browser = await chromium.launch({ args: ['--no-sandbox'], headless: true });
    const errors = [], requests = [];
    try {
        const desktop = await browser.newContext({ viewport: { width: 1365, height: 960 } });
        const page = await desktop.newPage();
        page.on('pageerror', e => errors.push(e.message));
        page.on('request', r => { if (r.url().includes('/photo/')) requests.push(r.url()); });
        const marker = await page.request.get(base + '/fixture/info');
        expect(marker.ok(), 'Refuse to mutate any server without the fixture marker').toBeTruthy();
        expect(await marker.json()).toEqual({ fixture: 'OutgoingLetterAvatars', syntheticData: true });
        expect((await page.request.post(base + '/fixture/reset')).ok()).toBeTruthy();
        await page.goto(base);
        const rows = page.locator('.pane .mail');
        await expect(rows).toHaveCount(5);
        await loaded(photo(rows.nth(0), 21));
        await expect(photo(rows.nth(0), 11)).toHaveCount(0);
        await expect(rows.nth(0).locator('.who-val')).toHaveText('کاربر هم‌نام');
        pass('desktop inbox: the loaded image belongs to the direct sender displayed on the row');
        await loaded(photo(rows.nth(1), 22));
        await expect(rows.nth(1).locator('.who-val')).toHaveText('کاربر هم‌نام');
        pass('desktop inbox: identical display names load distinct user-ID pictures');
        await loaded(rows.nth(2).locator('img[src$="/uploads/users/default.jpg"]'));
        await loaded(rows.nth(3).locator('img.user-avatar-default'));
        await expect(rows.nth(3)).toContainText('فرستنده نامشخص');
        pass('desktop inbox: missing photo and missing identity both render a loaded fallback image');
        await loaded(photo(rows.nth(4), 11));
        pass('desktop inbox: a signer-only row without an ErjaId still displays its sender photo');
        await saveShot(page, 'outgoing-avatars-inbox-desktop');

        await openFromList(page);
        const modal = page.locator('.outgoing-letter-gardesh');
        await loaded(photo(modal.locator('.tl-i.start'), 11));
        await expect(modal.locator('.tl-i.start')).toContainText('ایجادکننده اصلی');
        await allPhotos(modal, [11, 21, 31, 32, 41, 42]);
        pass('circulation list: creator, referrers, recipients and both signers display loaded login photos');
        const calls = await (await page.request.get(base + '/fixture/calls')).json();
        expect(calls.every(c => c.method.startsWith('Get'))).toBeTruthy();
        pass('opening circulation does not mark read, refer, sign or otherwise mutate the letter');
        await modal.locator('.tl-i.gnode-click').filter({ hasText: 'گیرنده ارجاع' }).click();
        await loaded(photo(modal.locator('.gdetail'), 31));
        await loaded(photo(modal.locator('.gdetail'), 21));
        await expect(modal.locator('.gdetail')).toContainText('پاراف آزمایشی');
        await expect(modal.locator('.gdetail')).toContainText('پاسخ آزمایشی');
        pass('selected station: recipient and direct-referrer photos accompany the unchanged referral/answer');
        await saveShot(page, 'outgoing-avatars-circulation-list');

        await modal.locator('.gm-seg').getByRole('button', { name: /درختواره/ }).click();
        await allPhotos(modal, [11, 21, 31, 32, 41, 42]);
        await loaded(photo(modal.locator('.gtree .tnode').first(), 11));
        await expect(modal.locator('.gtree')).toContainText('امضا شد');
        await expect(modal.locator('.gtree')).toContainText('در انتظار');
        pass('circulation tree: correct root and signer photos preserve signature status and nesting');
        await saveShot(page, 'outgoing-avatars-circulation-tree');
        await modal.locator('.gm-seg').getByRole('button', { name: /فلوچارتی/ }).click();
        await allPhotos(modal, [11, 21, 31, 32, 41, 42]);
        await loaded(photo(modal.locator('.fnode.root'), 11));
        await modal.locator('.fnode.gnode-click').filter({ hasText: 'گیرنده رونوشت' }).click();
        await loaded(photo(modal.locator('.gdetail'), 32));
        await loaded(photo(modal.locator('.gdetail'), 21));
        pass('circulation flowchart: nested recipient selection displays the correct recipient/referrer pair');
        await saveShot(page, 'outgoing-avatars-circulation-flow');
        await modal.locator('.gm-h > .x').click();
        await expect(modal).toHaveCount(0);

        await rows.first().click();
        await loaded(photo(page.locator('.paper-h'), 11));
        const reader = page.locator('main');
        await allPhotos(reader, [11, 21, 31, 32, 41, 42]);
        await expect(photo(reader, 41)).toHaveCount(2);
        await expect(photo(reader, 42)).toHaveCount(2);
        pass('reader: original sender, internal recipients and signer badges show the matching photos');
        await reader.locator('button[title="مشاهدهٔ گردش نامه"]').click();
        await loaded(photo(modal.locator('.tl-i.start'), 11));
        pass('reader circulation entry agrees with the inbox menu on the original sender identity');
        await modal.locator('.gm-h > .x').click();
        expect((await page.request.post(base + '/fixture/detail-error/true')).ok()).toBeTruthy();
        await reader.locator('button[title="مشاهدهٔ گردش نامه"]').click();
        await expect(modal.getByRole('alert')).toBeVisible();
        await expect(modal.locator('img.user-avatar')).toHaveCount(0);
        await modal.locator('.gm-h > .x').click();
        expect((await page.request.post(base + '/fixture/detail-error/false')).ok()).toBeTruthy();
        await reader.locator('button[title="مشاهدهٔ گردش نامه"]').click();
        await loaded(photo(modal.locator('.tl-i.start'), 11));
        pass('metadata failure shows an explicit error, never stale photos; reopening recovers');
        expect((await page.request.post(base + '/fixture/reset')).ok()).toBeTruthy();
        await desktop.close();

        const mobile = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });
        const phone = await mobile.newPage();
        phone.on('pageerror', e => errors.push(e.message));
        phone.on('request', r => { if (r.url().includes('/photo/')) requests.push(r.url()); });
        await phone.goto(base);
        const first = phone.locator('.pane .mail').first();
        await loaded(photo(first, 21));
        const box = await photo(first, 21).boundingBox();
        expect(box.width).toBeGreaterThanOrEqual(39);
        expect(box.x).toBeGreaterThanOrEqual(0);
        expect(box.x + box.width).toBeLessThanOrEqual(391);
        pass('mobile inbox: sender photo is visible at the intended size, without horizontal clipping');
        await saveShot(phone, 'outgoing-avatars-inbox-mobile');
        await first.locator('.dd > button').tap();
        await first.locator('.ltmenu').getByRole('button', { name: /گردش نامه/ }).tap();
        const phoneModal = phone.locator('.outgoing-letter-gardesh');
        await allPhotos(phoneModal, [11, 21, 31, 32, 41, 42]);
        await phoneModal.locator('.tl-i.gnode-click').filter({ hasText: 'گیرنده ارجاع' }).tap();
        await loaded(photo(phoneModal.locator('.gdetail'), 31));
        await loaded(photo(phoneModal.locator('.gdetail'), 21));
        const modalBounds = await phoneModal.locator('.gmodal').boundingBox();
        expect(modalBounds.x).toBeGreaterThanOrEqual(0);
        expect(modalBounds.x + modalBounds.width).toBeLessThanOrEqual(391);
        expect(await phone.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
        pass('mobile circulation: touch opens station details and the photo/name layout fits the viewport');
        await saveShot(phone, 'outgoing-avatars-circulation-mobile');
        await phoneModal.locator('.gm-seg').getByRole('button', { name: /درختواره/ }).tap();
        await loaded(photo(phoneModal.locator('.gtree .tnode').first(), 11));
        await phoneModal.locator('.gm-seg').getByRole('button', { name: /فلوچارتی/ }).tap();
        await loaded(photo(phoneModal.locator('.fnode.root'), 11));
        pass('mobile circulation: switching all three views retains the original sender picture');
        await mobile.close();
        expect(requests.some(u => u.includes('/api/auth/users/photo/99?'))).toBeTruthy();
        expect(requests.some(u => u.includes('/api/SystemUsers/photo/') || /\/photo\/(0|-\d)/.test(u))).toBeFalsy();
        pass('browser requests use only real login IDs, never SystemUsers, zero or temporary picker IDs');
        expect(errors).toEqual([]);
        pass('desktop and mobile complete with no JavaScript page errors');
        console.log(`ALL ${checks} OUTGOING-LETTER BROWSER CHECKS PASSED.`);
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
