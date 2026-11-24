// Synthetic-only browser regression. Run against EmailDeliveryUi --serve, never production.
const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const base = process.env.EMAIL_TEST_URL || 'http://127.0.0.1:5144';
const output = process.env.TEST_OUTPUT || path.join(__dirname, 'artifacts');
fs.mkdirSync(output, { recursive: true });
let checks = 0;
function check(value, name) { assert.ok(value, name); checks++; console.log('PASS: ' + name); }
(async () => {
  const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
  const errors = [];
  async function fixture(mode, viewport = { width: 1365, height: 960 }, route = '/probe/email') {
    const context = await browser.newContext({ viewport });
    const page = await context.newPage(); page.on('pageerror', e => errors.push(e.message));
    await context.request.post(base + '/fixture/reset');
    await context.request.post(base + '/fixture/mode/' + mode);
    await page.goto(base + route);
    await page.getByText('داده', { exact: true }).count(); // no action, no arbitrary timed sleep
    return { page, context, state: async () => (await context.request.get(base + '/fixture/state')).json(), mode: async m => context.request.post(base + '/fixture/mode/' + m) };
  }
  async function compose(page) {
    await page.getByRole('button', { name: 'ایمیل جدید' }).last().click();
    const modal = page.locator('.email-compose-modal'); await modal.locator('.sheet').waitFor({ state: 'visible' });
    await modal.locator('select').selectOption('10');
    const fields = modal.locator('input.tin');
    await fields.nth(0).fill('receiver@example.invalid'); await fields.nth(2).fill('موضوع ساختگی آزمون');
    await modal.locator('textarea').fill('متن ساختگی، هیچ ارسال خارجی انجام نمی‌شود.');
    return modal;
  }
  async function submit(modal) { await modal.locator('.sheet-f > button.pri').click(); await modal.locator('.email-delivery-result').waitFor(); }
  try {
    let f = await fixture('failed'); let modal = await compose(f.page); await submit(modal);
    check((await modal.innerText()).includes('ناموفق — ارسال انجام نشد'), 'desktop shows failed outcome rather than success');
    check(await modal.locator('fieldset input.tin').first().isDisabled(), 'payload is locked after a completed request');
    check(await modal.locator('.sheet-f > button.pri').isDisabled(), 'repeat submit is disabled');
    const failedKey = (await f.state()).keys[0];
    await modal.locator('.delivery-retry').click();
    check((await f.state()).sendCalls === 1, 'preparing a retry itself does not send');
    await f.mode('warning'); await submit(modal);
    let state = await f.state();
    check(state.sendCalls === 2 && state.keys[1] !== failedKey, 'explicit retry gets a separate UUID');
    check((await modal.innerText()).includes('CONTACTS_SAVE_WARNING'), 'accepted-with-warning retains post-send reason');
    await modal.locator('summary').filter({ hasText: 'ارسال دوبارهٔ آگاهانه' }).click();
    check(await modal.locator('.delivery-explicit-resend').isDisabled(), 'accepted message resend requires confirmation');
    await modal.getByRole('checkbox').check();
    await f.page.waitForFunction(() => { const b = document.querySelector('.delivery-explicit-resend'); return b && !b.disabled; });
    check(await modal.locator('.delivery-explicit-resend').isEnabled(), 'confirmation unlocks only explicit preparation');
    await f.page.screenshot({ path: path.join(output, 'email-result-desktop.png'), fullPage: true });
    await modal.locator('.sheet-f > button.ghost').click();
    await f.page.getByRole('button', { name: 'نمایش نتیجه و پیگیری' }).click();
    check((await modal.innerText()).includes(state.keys[1]) && (await f.state()).sendCalls === 2, 'close/reopen retains previous tracking result');
    await modal.locator('.compose-fresh').click();
    await modal.locator('.email-delivery-result').waitFor({ state: 'detached' });
    check((await modal.locator('input.tin').evaluateAll(inputs => inputs.every(i => i.value === ''))) && await modal.locator('textarea').inputValue() === '',
      'new unrelated email opens a genuinely empty form rather than copying the old message');
    check((await f.state()).sendCalls === 2 && !(await modal.innerText()).includes(state.keys[1]), 'blank-message action changes tracking key without sending');
    await f.context.close();

    f = await fixture('http-loss'); modal = await compose(f.page); await submit(modal);
    const lostKey = (await f.state()).keys[0];
    check((await modal.innerText()).includes('نتیجه نامشخص'), 'lost HTTP result is not labelled definite failure');
    check(await modal.locator('.compose-fresh').count() === 0, 'unresolved request cannot be silently forgotten by starting a blank message');
    await modal.locator('.compose-replay').click();
    await f.page.waitForFunction(() => document.querySelector('.email-delivery-result')?.textContent.includes('فراخوانی ایمیل تازه‌ای نفرستاد'));
    state = await f.state();
    check(state.sendCalls === 2 && state.keys.every(k => k === lostKey), 'manual HTTP recovery resubmits same immutable UUID');
    check((await modal.innerText()).includes('پذیرفته‌شده توسط سرور ایمیل'), 'safe replay resolves prior SMTP acceptance');
    await f.context.close();

    f = await fixture('http-loss'); modal = await compose(f.page); await submit(modal);
    const recoverKey = (await f.state()).keys[0];
    await f.context.request.post(base + '/fixture/status-fails/true'); await f.page.reload();
    await f.page.locator('.email-recovery-banner').waitFor();
    check((await f.page.locator('.email-recovery-banner').innerText()).includes(recoverKey), 'session storage restores unresolved request after full reload');
    check((await f.state()).sendCalls === 1, 'reload/recovery never sends automatically');
    await f.context.request.post(base + '/fixture/status-fails/false');
    await f.page.getByRole('button', { name: 'نمایش نتیجه و پیگیری' }).click();
    modal = f.page.locator('.email-compose-modal'); await modal.locator('.delivery-check').click();
    await f.page.waitForFunction(() => document.querySelector('.email-delivery-result')?.textContent.includes('پذیرفته‌شده توسط سرور ایمیل'));
    check((await f.state()).sendCalls === 1, 'status-only recovery confirms outcome without HTTP send');
    await f.context.close();

    f = await fixture('accept'); modal = await compose(f.page);
    await modal.locator('input[type=file]').setInputFiles({ name: 'too-big.bin', mimeType: 'application/octet-stream', buffer: Buffer.alloc(21 * 1024 * 1024) });
    await modal.locator('.compose-attachment-error').waitFor();
    check(await modal.locator('.sheet-f > button.pri').isDisabled() && (await f.state()).sendCalls === 0, 'oversized upload blocks the whole email instead of silently dropping a file');
    await f.context.close();

    f = await fixture('warning', { width: 390, height: 844 }, '/probe/dabirkhane');
    await f.page.getByRole('button', { name: 'ثبت و ارسال' }).first().click();
    modal = f.page.locator('.secretariat-register-modal'); await modal.locator('.sheet').waitFor();
    await modal.locator('select').first().selectOption('ایمیل');
    await modal.locator('select').nth(1).waitFor();
    const sender = modal.locator('select').last();
    check(await sender.inputValue() === '0', 'secretariat mobile requires explicit official account choice');
    await modal.getByPlaceholder('مثلا office@target-org.ir…').fill('receiver@example.invalid');
    await modal.getByRole('button', { name: 'ثبت و ارسال' }).click();
    check((await f.state()).registrationCalls === 0, 'missing official sender blocks registration request');
    await sender.selectOption('10'); await modal.getByRole('button', { name: 'ثبت و ارسال' }).click();
    await modal.locator('.email-delivery-result').waitFor();
    check((await modal.locator('.registration-result').innerText()).includes('ثبت دبیرخانه ذخیره شد'), 'mobile registration outcome is visibly separate');
    await f.page.waitForFunction(() => { const el = document.getElementById('registration-email-outcome'); if (!el) return false; const r = el.getBoundingClientRect(); return r.top >= 50 && r.top < innerHeight / 2; });
    check(true, 'mobile submit automatically scrolls to the separated result instead of hiding it above the form');
    check((await modal.locator('.email-delivery-result').innerText()).includes('پذیرفته‌شده با هشدار تکمیلی'), 'mobile email warning does not erase saved registration');
    check(await f.page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'secretariat result has no mobile horizontal page overflow');
    await f.page.screenshot({ path: path.join(output, 'secretariat-result-mobile.png'), fullPage: true });
    await modal.getByRole('button', { name: 'مشاهدهٔ همهٔ تلاش‌های ایمیل این نامه' }).click();
    const history = f.page.locator('.email-delivery-history-modal'); await history.locator('.sheet').waitFor();
    await history.locator('.delivery-attempt summary').first().click();
    check((await history.innerText()).includes('CONTACTS_SAVE_WARNING'), 'mobile per-letter history includes diagnostic event codes');
    const before = await f.state(); await history.locator('.delivery-refresh').click();
    check((await f.state()).registrationCalls === before.registrationCalls && (await f.state()).sendCalls === before.sendCalls, 'history refresh has no sending side effect');
    check(await f.page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'tracking GUID and history event details wrap on narrow screens');
    await f.page.screenshot({ path: path.join(output, 'email-history-mobile.png'), fullPage: true });
    await f.context.close();

    f = await fixture('accept', { width: 390, height: 844 }, '/probe/personal-email');
    await f.page.getByRole('button', { name: 'وضعیت ارسال و خطاها' }).last().click();
    await f.page.locator('.email-delivery-history').waitFor();
    check(!(await f.page.locator('.delivery-account-filter').innerText()).includes('official@example.invalid'), 'personal history account picker excludes official accounts');
    check((await f.state()).sendCalls === 0 && (await f.state()).readWrites === 0, 'personal report does not send or mark messages read');
    check(await f.page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'personal history fits mobile viewport');
    await f.context.close();
    check(errors.length === 0, 'browser execution has no unhandled JavaScript errors');
    console.log(`ALL ${checks} EMAIL BROWSER CHECKS PASSED.`);
  } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exit(1); });
