// Synthetic fixture only. The full real unified page is mounted, not just the incoming child panel.
const { chromium, expect } = require('playwright/test');
const fs = require('node:fs');
const path = require('node:path');
const base = process.env.SECRETARIAT_TEST_URL || 'http://127.0.0.1:5135';
const screenshots = process.env.SECRETARIAT_SCREENSHOTS;
let checks = 0;
const pass = text => { checks++; console.log('PASS: ' + text); };
const photo = (scope, id) => scope.locator(`img.user-avatar[src*="/api/auth/users/photo/${id}?"]`);
async function loaded(image) {
    await expect(image).toHaveCount(1);
    await expect.poll(() => image.evaluate(e => e.complete && e.naturalWidth > 0)).toBeTruthy();
}
async function people(scope, ids) { for (const id of ids) await loaded(photo(scope, id).first()); }
async function state(page) { return (await page.request.get(base + '/fixture/state')).json(); }
async function shot(page, name) {
    if (!screenshots) return;
    fs.mkdirSync(screenshots, { recursive: true });
    await page.screenshot({ path: path.join(screenshots, name + '.png') });
}
async function fits(page, element) {
    await element.evaluate(async e => { await Promise.all(e.getAnimations().map(a => a.finished)); });
    const box = await element.boundingBox();
    expect(box.x).toBeGreaterThanOrEqual(0);
    expect(box.x + box.width).toBeLessThanOrEqual(page.viewportSize().width + 1);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(page.viewportSize().width);
}
async function outgoingTab(page, tab) {
    const titles = { registered: 'ثبت شده', pending: 'در انتظار ثبت', archived: 'بایگانی‌شده' };
    await page.locator(`.unified-secretariat .rail button[title="${titles[tab]}"]`).click();
}
async function incomingType(page) {
    await page.locator('.secretariat-heading').getByRole('button', { name: /نامه‌های وارده/ }).click();
    await expect(page.locator('.iin-row')).toHaveCount(12);
}
async function incomingArchive(page) {
    await page.locator('.unified-secretariat .rail button[title="نامه‌های بایگانی‌شده"]').click();
    await expect(page.locator('.iin-row')).toHaveCount(4);
}
async function closeModal(modal) { await modal.locator('.gm-h > .x').click(); }
(async () => {
    const browser = await chromium.launch({ args: ['--no-sandbox'], headless: true });
    const errors = [], requests = [];
    const watch = page => {
        page.on('pageerror', e => errors.push(e.message));
        page.on('request', r => { if (r.url().includes('/photo/')) requests.push(r.url()); });
    };
    try {
        const desktop = await browser.newContext({ viewport: { width: 1365, height: 960 } });
        const page = await desktop.newPage(); watch(page);
        const marker = await page.request.get(base + '/fixture/info');
        expect(marker.ok(), 'Refuse to change any server lacking the synthetic fixture marker').toBeTruthy();
        expect(await marker.json()).toEqual({ fixture: 'UnifiedSecretariatAvatars', syntheticData: true });
        expect((await page.request.post(base + '/fixture/reset')).ok()).toBeTruthy();
        await page.goto(base);
        const outRows = page.locator('.secretariat-outgoing-letter');
        await expect(outRows).toHaveCount(15);
        await loaded(photo(outRows.nth(0).locator('.secretariat-creator'), 11));
        await loaded(photo(outRows.nth(1).locator('.secretariat-creator'), 12));
        await expect(outRows.nth(0).locator('.letter-person-name')).toHaveText('ایجادکننده هم‌نام');
        await expect(outRows.nth(1).locator('.letter-person-name')).toHaveText('ایجادکننده هم‌نام');
        pass('full unified page: same-name outgoing creators load distinct login-user pictures');
        await loaded(outRows.nth(2).locator('img[src$="/uploads/users/default.jpg"]'));
        await loaded(outRows.nth(3).locator('img.user-avatar-default'));
        await loaded(outRows.nth(4).locator('img.user-avatar-default'));
        await expect(outRows.nth(3)).toContainText('کاربر نامشخص');
        pass('outgoing pending rows: missing image, null identity and negative identity all show loaded defaults');
        await expect(outRows.first().locator('.letter-external-party img')).toHaveCount(0);
        await expect(outRows.first().locator('.secretariat-registrar')).toHaveCount(0);
        expect(await state(page)).toEqual({ incomingDetailReads: 0, outgoingReadWrites: 0 });
        pass('external recipients are not assigned user photos and pending registration invents no clerk');
        await page.locator('.unified-secretariat .lt-pager button[title="بعدی"]').click();
        await expect(outRows).toHaveCount(6); await loaded(photo(outRows.first(), 90));
        pass('outgoing server paging displays the next page creator, not a stale recycled image');
        await outgoingTab(page, 'registered'); await expect(outRows).toHaveCount(5);
        await loaded(photo(outRows.first().locator('.secretariat-creator'), 11));
        await loaded(photo(outRows.first().locator('.secretariat-registrar'), 51));
        await loaded(photo(outRows.nth(1).locator('.secretariat-registrar'), 52));
        await expect(outRows.first().locator('.secretariat-registrar')).toContainText('ثبت دبیرخانه:');
        pass('registered outgoing rows distinguish original creator from the separate registering clerk');
        await loaded(outRows.nth(2).locator('.secretariat-registrar img.user-avatar-default'));
        await expect(photo(outRows.nth(2).locator('.secretariat-registrar'), 13)).toHaveCount(0);
        await loaded(outRows.nth(3).locator('.secretariat-registrar img[src$="/uploads/users/default.jpg"]'));
        pass('unknown registrar is not inferred from the creator name; missing registrar photo falls back');
        await shot(page, 'unified-outgoing-registered-desktop');
        await page.getByRole('button', { name: /جستجوی پیشرفته/ }).click();
        await page.getByText('فرستنده (ثبت‌کننده)', { exact: true }).locator('..').locator('select').selectOption('12');
        await expect(outRows).toHaveCount(1); await people(outRows.first(), [12, 52]);
        await page.getByRole('button', { name: /پاک کردن فیلترها/ }).click();
        await expect(outRows).toHaveCount(5);
        await page.getByRole('button', { name: /جستجوی پیشرفته/ }).click();
        expect(await state(page)).toEqual({ incomingDetailReads: 0, outgoingReadWrites: 0 });
        pass('advanced creator filtering and clearing retain matching photos without reading/marking letters');
        await outRows.first().locator('button[title="نمایش گردش نامه"]').click();
        const outgoing = page.locator('.outgoing-letter-gardesh');
        await people(outgoing, [11, 61, 31, 41, 42]);
        await expect(photo(outgoing, 51)).toHaveCount(0);
        await loaded(photo(outgoing.locator('.tl-i.start'), 11));
        for (const name of [/درختواره/, /فلوچارتی/]) {
            await outgoing.locator('.gm-seg').getByRole('button', { name }).click();
            await people(outgoing, [11, 61, 31, 41, 42]);
        }
        expect((await state(page)).outgoingReadWrites).toBe(0);
        await closeModal(outgoing);
        pass('all outgoing circulation views keep actual creator/referrer/recipient/signer photos, without a fictitious clerk referral');
        await outRows.first().locator('button[title="نمایش نامه"]').click();
        await expect(page).toHaveURL(/\/outgoing-letters\/view\/1200$/);
        await loaded(photo(page.locator('.paper-h'), 11));
        await loaded(photo(page.locator('.outgoing-secretariat-status .secretariat-registrar'), 51));
        expect((await state(page)).outgoingReadWrites).toBe(1);
        pass('outgoing display navigation preserves letter ID, both user roles and its normal mark-read behavior');
        await page.goto(base); await outgoingTab(page, 'archived');
        const leaves = page.locator('.secretariat-archive-letter');
        await expect(leaves).toHaveCount(4);
        await loaded(photo(leaves.nth(0), 11)); await loaded(photo(leaves.nth(1), 12));
        await loaded(leaves.nth(2).locator('img.user-avatar-default')); await loaded(photo(leaves.nth(3), 15));
        await expect(photo(page.locator('.secretariat-archive'), 777)).toHaveCount(0);
        pass('outgoing archive tree shows each letter creator, never a folder/owner ID or a name-matched legacy user');
        await shot(page, 'unified-outgoing-archive-desktop');

        await incomingType(page);
        const inRows = page.locator('.iin-row');
        await loaded(photo(inRows.nth(0), 21)); await loaded(photo(inRows.nth(1), 22));
        await expect(inRows.first().locator('.iin-identity')).toContainText('ثبت‌کننده:');
        await expect(inRows.first().locator('.letter-external-party')).toHaveText('فرستندهٔ مبدأ: سازمان بیرونی آزمایشی');
        await expect(inRows.first().locator('.letter-external-party img')).toHaveCount(0);
        await loaded(inRows.nth(2).locator('img[src$="/uploads/users/default.jpg"]'));
        await loaded(inRows.nth(3).locator('img.user-avatar-default'));
        pass('incoming registered tab inside the unified parent pairs each real user photo/name separately from external origin');
        await shot(page, 'unified-incoming-registered-desktop');
        await page.locator('.iin-pager button[title="بعدی"]').click();
        await expect(inRows).toHaveCount(4); await loaded(photo(inRows.first(), 118));
        await inRows.first().locator('input[type="checkbox"]').check();
        await expect(page.locator('.iin-actions')).toContainText('بایگانی گروهی');
        expect((await state(page)).incomingDetailReads).toBe(0);
        pass('incoming pagination and selection keep the letter/user identities separate without detail fetches');
        await incomingArchive(page);
        await loaded(photo(inRows.nth(0), 25)); await loaded(photo(inRows.nth(1), 61));
        await expect(inRows.nth(0).locator('.iin-identity')).toContainText('ثبت‌کننده:');
        await expect(inRows.nth(1).locator('.iin-identity')).toContainText('ارجاع‌دهنده:');
        pass('parent navigation rail actually re-renders archived rows with correct registrar/referrer roles and new photos');
        await loaded(inRows.nth(2).locator('img.user-avatar-default'));
        await expect(inRows.nth(2).locator('.iin-person-name')).toHaveText('کاربر نامشخص');
        await loaded(inRows.nth(3).locator('img.user-avatar-default'));
        pass('legacy direct archive text is not mistaken for an internal user; invalid referral IDs use defaults');
        const before = await state(page);
        await inRows.nth(1).getByRole('button', { name: /گردش$/ }).click();
        const incoming = page.locator('.incoming-letter-gardesh');
        await people(incoming, [21, 61, 31]);
        await expect(incoming.locator('.letter-origin img')).toHaveCount(0);
        for (const name of [/درختواره/, /فلوچارتی/]) {
            await incoming.locator('.gm-seg').getByRole('button', { name }).click();
            await people(incoming, [21, 61, 31]);
        }
        expect(await state(page)).toEqual(before); await closeModal(incoming);
        pass('incoming circulation from unified archive displays every participant without calling mutating detail');
        await inRows.nth(1).getByRole('button', { name: /نمایش$/ }).click();
        const detail = page.locator('.incoming-letter-view');
        await people(detail, [21, 61, 31]);
        expect((await state(page)).incomingDetailReads).toBe(1);
        await detail.getByRole('button', { name: /گردش کامل/ }).click();
        await people(incoming, [21, 61, 31]);
        expect((await state(page)).incomingDetailReads).toBe(1);
        await closeModal(incoming); await detail.getByRole('button', { name: 'بستن', exact: true }).click();
        pass('incoming full view shows registrar/history photos and its circulation callback does not read the letter again');
        await page.locator('.unified-secretariat .rail button[title="نامه‌های ثبت‌شده"]').click();
        await expect(inRows).toHaveCount(12);
        await page.locator('.iin-search').fill('2001'); await expect(inRows).toHaveCount(1);
        await loaded(photo(inRows.first(), 22));
        await page.locator('.iin-search').fill(''); await expect(inRows).toHaveCount(12);
        pass('returning through parent rail and searching keeps source counts and exact matching user photo');
        await desktop.close();

        const mobile = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });
        const phone = await mobile.newPage(); watch(phone);
        expect((await phone.request.post(base + '/fixture/reset')).ok()).toBeTruthy();
        await phone.goto(base);
        const phoneOut = phone.locator('.secretariat-outgoing-letter');
        await loaded(photo(phoneOut.first(), 11)); await fits(phone, photo(phoneOut.first(), 11));
        pass('mobile real unified layout keeps outgoing photos and header within the viewport');
        await outgoingTab(phone, 'registered');
        await people(phoneOut.first(), [11, 51]);
        await phoneOut.first().scrollIntoViewIfNeeded();
        await expect(photo(phoneOut.first(), 11)).toBeInViewport({ ratio: 0.99 });
        await expect(photo(phoneOut.first(), 51)).toBeInViewport({ ratio: 0.99 });
        await fits(phone, phoneOut.first());
        await shot(phone, 'unified-outgoing-mobile');
        pass('mobile registered row wraps both roles while retaining distinct creator/clerk pictures');
        await outgoingTab(phone, 'archived');
        const phoneLeaves = phone.locator('.secretariat-archive-letter');
        await expect(phoneLeaves).toHaveCount(4); await people(phone.locator('.secretariat-archive'), [11, 12, 15]);
        await phoneLeaves.last().scrollIntoViewIfNeeded();
        await expect(photo(phoneLeaves.last(), 15)).toBeInViewport({ ratio: 0.99 });
        await fits(phone, phoneLeaves.last());
        pass('mobile archive keeps long creator names/photos readable without horizontal page overflow');
        await incomingType(phone);
        const phoneIn = phone.locator('.iin-row');
        await loaded(photo(phoneIn.first(), 21));
        await phoneIn.first().scrollIntoViewIfNeeded();
        await expect(photo(phoneIn.first(), 21)).toBeInViewport({ ratio: 0.99 });
        await fits(phone, phoneIn.first());
        expect(await phoneIn.first().evaluate(row => {
            const bounds = row.getBoundingClientRect();
            return [...row.querySelectorAll('.iin-row-actions button')].every(button => {
                const b = button.getBoundingClientRect(); return b.left >= bounds.left && b.right <= bounds.right;
            });
        })).toBeTruthy();
        expect((await photo(phoneIn.first(), 21).boundingBox()).width).toBeGreaterThanOrEqual(23);
        await shot(phone, 'unified-incoming-mobile');
        pass('mobile incoming rows display full-sized registrar avatars inside the real parent page');
        await incomingArchive(phone); await people(phone.locator('.iin'), [25, 61]);
        await phoneIn.nth(1).scrollIntoViewIfNeeded();
        await expect(photo(phoneIn.nth(1), 61)).toBeInViewport({ ratio: 0.99 });
        await fits(phone, phoneIn.nth(1));
        await shot(phone, 'unified-incoming-archive-mobile');
        pass('mobile parent archive navigation replaces registered photos with the correct archive actors');
        await phoneIn.nth(1).getByRole('button', { name: /گردش$/ }).tap();
        const phoneModal = phone.locator('.incoming-letter-gardesh');
        await people(phoneModal, [21, 61, 31]); await fits(phone, phoneModal.locator('.gmodal'));
        await phoneModal.locator('.gm-seg').getByRole('button', { name: /درختواره/ }).tap();
        await people(phoneModal, [21, 61, 31]);
        await phoneModal.locator('.gm-seg').getByRole('button', { name: /فلوچارتی/ }).tap();
        await people(phoneModal, [21, 61, 31]);
        expect(await state(phone)).toEqual({ incomingDetailReads: 0, outgoingReadWrites: 0 });
        await closeModal(phoneModal);
        pass('mobile incoming circulation opens from unified rows and retains all photos without changing read state');
        await phoneIn.nth(1).getByRole('button', { name: /نمایش$/ }).tap();
        const phoneDetail = phone.locator('.incoming-letter-view');
        await people(phoneDetail, [21, 61, 31]); await fits(phone, phoneDetail.locator('.sheet'));
        await phoneDetail.getByRole('button', { name: /گردش کامل/ }).tap();
        await people(phoneModal, [21, 61, 31]);
        expect((await state(phone)).incomingDetailReads).toBe(1);
        pass('mobile full-view/history and circulation actions preserve their intended read behavior');
        await mobile.close();
        expect(requests.some(u => u.includes('/api/auth/users/photo/99?'))).toBeTruthy();
        expect(requests.some(u => u.includes('/api/SystemUsers/photo/') || /\/photo\/(0|-\d|900|777|800\d)\?/.test(u))).toBeFalsy();
        pass('photo network requests never use system-user, invalid, logged-in, folder or archive-row IDs');
        expect(errors).toEqual([]);
        pass('full desktop/mobile unified-secretariat flows finish without JavaScript page errors');
        console.log(`ALL ${checks} UNIFIED-SECRETARIAT BROWSER CHECKS PASSED.`);
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
