// جریان چاپ نامه (صفحهٔ چاپ) — بدون پکیج، مرورگر، دات‌نت یا دیتابیس.
// node --test inventory/tests/ClientStartup/print-flow.test.cjs
//
// پوشش:
//  ۱) قرارداد نام‌های interop بین LetterPrintWorkflow.cs (C#) و app-shell.js (JS)
//  ۲) گارد «app-shell.js قدیمی از کش» (ltxPrintHasRuntime + پیام درست به‌جای «popup را مجاز کنید»)
//  ۳) رزرو پنجره هنگام کلیک + نبودِ «تب خالی یتیم» بعد از دابل‌کلیک (ltxPrintReleasePending)
//  ۴) watchdog/heartbeat پنجرهٔ چاپ — پنجره هرگز بی‌پاسخ روی «در حال آماده‌سازی…» نمی‌ماند
//  ۵) تقسیم بودجهٔ زمانی دانلود (۹۰ثانیه) و تحویل به مرورگر (۱۵ثانیه) در سمت C#
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.resolve(__dirname, '../../src');
const webroot = path.join(root, 'Inventory.Client/wwwroot');
const shell = fs.readFileSync(path.join(webroot, 'js/app-shell.js'), 'utf8');
const index = fs.readFileSync(path.join(webroot, 'index.html'), 'utf8');
const workflow = fs.readFileSync(
    path.join(root, 'Inventory.Client/Services/LetterPrintWorkflow.cs'), 'utf8');
const cartable = fs.readFileSync(
    path.join(root, 'Inventory.Client/Pages/Office/Letters/Outgoing/OutgoingLetterCartable.razor'), 'utf8');

function stubElement() {
    const el = {
        style: {}, dataset: {}, hidden: true, textContent: '', className: '', id: '', title: '',
        classList: { add() {}, remove() {}, contains: () => false },
        setAttribute() {}, getAttribute: () => null,
        appendChild(child) { return child; },
        removeChild() {}, remove() {}, focus() {}, click() {},
        addEventListener() {}, removeEventListener() {},
        querySelector: () => stubElement(),
        querySelectorAll: () => [],
        closest: () => null,
    };
    return el;
}

// نسخهٔ زمان‌دار: تایمرهای واقعی تا watchdog/heartbeat قابل تست باشند.
function setupWithTimers({ popupFactory } = {}) {
    const timers = new Set();
    const window = {
        location: new URL('http://localhost:5210/'),
        addEventListener() {}, removeEventListener() {},
        openCalls: 0,
        open() {
            this.openCalls++;
            if (!popupFactory) return null; // popup مسدود
            return popupFactory();
        },
    };
    const document = {
        baseURI: 'http://localhost:5210/',
        addEventListener() {}, removeEventListener() {},
        getElementById: () => null,
        createElement: () => stubElement(),
        body: stubElement(),
        head: stubElement(),
    };
    const context = {
        window, document, URL, console,
        setTimeout: (fn, ms) => { const t = setTimeout(fn, ms); timers.add(t); return t; },
        clearTimeout: t => { clearTimeout(t); timers.delete(t); },
        setInterval: (fn, ms) => { const t = setInterval(fn, ms); timers.add(t); return t; },
        clearInterval: t => { clearInterval(t); timers.delete(t); },
    };
    vm.runInNewContext(shell, context, { filename: 'app-shell.js' });
    return { window, context, timers };
}

function makePopupDocRecorder() {
    const writes = [];
    return {
        writes,
        popup() {
            const doc = {
                open() {}, write: html => writes.push(html), close() {},
                getElementById: () => stubElement(),
            };
            return { closed: false, close() { this.closed = true; }, document: doc, addEventListener() {} };
        },
    };
}

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

test('every interop function invoked from LetterPrintWorkflow.cs exists in app-shell.js', () => {
    const invoked = [...workflow.matchAll(/"(ltx[A-Za-z]+)"/g)].map(m => m[1]);
    assert.ok(invoked.length >= 8, 'the workflow should invoke the print interop surface');
    const missing = [...new Set(invoked)].filter(name =>
        !new RegExp(`window\\.${name}\\s*=`).test(shell));
    assert.deepEqual(missing, [], `missing in app-shell.js: ${missing.join(', ')}`);
});

test('stale app-shell.js (old cache) is detected by the runtime probe instead of a misleading popup hint', () => {
    assert.match(workflow, /InvokeAsync<bool>\("ltxPrintHasRuntime"\)/);
    assert.match(shell, /window\.ltxPrintHasRuntime = function/);
    assert.match(shell, /typeof window\.ltxPrintBegin === 'function'/);
    // پیام راهنمای کش برای کاربر
    assert.match(workflow, /Ctrl\+Shift\+R/);
    // قبل از هر مسیر خروج زودهنگام (probe ناموفق / begin خطا / begin خالی)، پنجرهٔ رزروشده بسته شود
    const releaseCalls = (workflow.match(/ltxPrintReleasePending/g) || []).length;
    assert.ok(releaseCalls >= 3, 'release pending must run on every early-exit path');
    assert.match(shell, /window\.ltxPrintReleasePending = function/);
});

test('index.html ships the new app-shell version so browsers drop the stale copy', () => {
    assert.match(index, /<script src="js\/app-shell\.js\?v=3"><\/script>/);
});

test('double-click reserves at most one popup and leaves no orphan blank tab', () => {
    const { window } = setupWithTimers({ popupFactory: makePopupDocRecorder().popup });
    const first = { closed: false, close() { this.closed = true; } };
    const second = { closed: false, close() { this.closed = true; } };
    let opened = 0;
    window.open = () => { opened++; return opened === 1 ? first : second; };

    assert.equal(window.ltxPrintReserveByClick(), true);
    assert.equal(window.ltxPrintPendingWindows.length, 1);
    assert.equal(window.ltxPrintReserveByClick(), true, 'second click still reserves a fresh popup');
    assert.equal(window.ltxPrintPendingWindows.length, 1, 'only one pending window remains');
    assert.equal(first.closed, true, 'the superseded reservation is closed, not leaked');
    assert.equal(second.closed, false);

    // ltxPrintBegin پنجرهٔ رزروشده را مصرف و بقیه را می‌بندد
    const id = window.ltxPrintBegin();
    assert.ok(id);
    assert.equal(window.ltxPrintPendingWindows.length, 0);
    window.ltxPrintCancel(id);
});

test('cartable print guard closes the just-reserved popup instead of orphaning a blank tab', () => {
    assert.match(cartable, /if \(printingPdf\)\s*\{[\s\S]*?ltxPrintReleasePending[\s\S]*?return;\s*\}/);
});

test('print window arms a watchdog so it never sits silently on "preparing"', async () => {
    const recorder = makePopupDocRecorder();
    const { window } = setupWithTimers({ popupFactory: recorder.popup });
    window.ltxPrintReserveByClick();
    const id = window.ltxPrintBegin();
    const session = window.ltxPrintSessions[id];
    assert.ok(session.watchdog, 'watchdog timer must be armed at begin');
    assert.ok(session.heartbeat, 'heartbeat must be armed at begin');

    // heartbeat: شمارش ثانیه روی نوار وضعیت
    await sleep(1100);
    assert.match(session.status.textContent, /ثانیه/);

    // انقضای watchdog (کف ۱ ثانیه برای تست؛ در تولید ۹۰ ثانیه)
    const { window: w2 } = setupWithTimers({ popupFactory: makePopupDocRecorder().popup });
    w2.ltxPrintWatchdogMs = 1000;
    const id2 = w2.ltxPrintBegin();
    const s2 = w2.ltxPrintSessions[id2];
    await sleep(1300);
    assert.match(s2.error.textContent, /پاسخی از برنامه دریافت نکرده/);
    assert.equal(s2.error.hidden, false);
    // دکمه‌های بازیابی در دسترس‌اند
    const wait = w2.ltxPrintWaitForAction(id2);
    s2.resolveAction && s2.resolveAction('retry');
    assert.equal(await wait, 'retry');
    w2.ltxPrintCancel(id2);
    window.ltxPrintCancel(id);
});

test('watchdog disarms when the PDF is ready or the flow fails', async () => {
    const recorder = makePopupDocRecorder();
    const { window } = setupWithTimers({ popupFactory: recorder.popup });
    const id = window.ltxPrintBegin();
    const session = window.ltxPrintSessions[id];
    assert.ok(session.watchdog);
    window.ltxPrintFail(id, 'سربرگ شرکت پیدا نشد.');
    assert.equal(session.watchdog, null, 'fail must stop the watchdog');
    assert.match(session.error.textContent, /سربرگ شرکت پیدا نشد/);
    window.ltxPrintCancel(id);
});

test('download and browser hand-off have separate time budgets (90s / 15s)', () => {
    assert.match(workflow, /DownloadTimeout = TimeSpan\.FromSeconds\(90\)/);
    assert.match(workflow, /HandoffTimeout = TimeSpan\.FromSeconds\(15\)/);
    assert.match(workflow, /loadPdf\(downloadCts\.Token\)/);
    assert.match(workflow, /"ltxPrintReady", handoffCts\.Token/);
    // پیام‌های تایم‌اوت دقیق و جدا از هم
    assert.match(workflow, /دریافت فایل چاپ از سرور بیش از ۹۰ ثانیه/);
    assert.match(workflow, /نمایش PDF در مرورگر بیش از ۱۵ ثانیه/);
    // مدیریت خطا/تلاش مجدد سر جایش است
    assert.match(workflow, /"ltxPrintFail", printId, message/);
    assert.match(workflow, /"ltxPrintWaitForAction", printId/);
});
