// Verifies the letter print flow: preview first, print ONLY on explicit user click.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { JSDOM } = require('jsdom');

const path = require('node:path');
const SRC = path.resolve(__dirname, '../../../TotallCollectionSoftware/inventory/src/Inventory.Client/wwwroot/js/app-shell.js');

function extractPrintModule(code) {
  // Only the print section is exercised; the rest of app-shell.js touches APIs (fetch, signalR…)
  // that are irrelevant here.
  const start = code.indexOf('function ltxPdfBytes(payload) {');
  const end = code.indexOf('// ================== آدرس سرور API');
  assert.ok(start > 0 && end > start, 'print section markers found');
  return code.slice(start, end);
}

function makeEnv({ popupBlocked = false } = {}) {
  const dom = new JSDOM('<!doctype html><html><body></body></html>', { url: 'https://app.test/', runScripts: 'outside-only' });
  const { window } = dom;
  const printCalls = [];

  // A popup window is itself a document; jsdom cannot window.open a real one, so a second
  // JSDOM instance stands in for it.
  let popupDom = null;
  window.open = function () {
    if (popupBlocked) return null;
    popupDom = new JSDOM('<!doctype html><html><body></body></html>', { url: 'https://app.test/popup', runScripts: 'outside-only' });
    const pw = popupDom.window;
    pw.closed = false;
    pw.close = function () { pw.closed = true; };
    pw.focus = function () { };
    pw.opener = window;
    pw.requestAnimationFrame = function (cb) { return setTimeout(() => cb(Date.now()), 0); };
    // jsdom's document.write on a fresh document works; keep the real implementation.
    return pw;
  };
  window.focus = function () { };
  window.print = function () { printCalls.push('main-window'); };
  window.URL.createObjectURL = function () { return 'blob:fake-' + Math.random().toString(36).slice(2); };
  window.URL.revokeObjectURL = function () { };
  window.requestAnimationFrame = function (cb) { return setTimeout(() => cb(Date.now()), 0); };

  const code = extractPrintModule(fs.readFileSync(SRC, 'utf8'));
  window.eval(code);
  return { dom, window, printCalls, getPopup: () => popupDom && popupDom.window };
}

// The iframe inside the print surface is a PDF viewer; jsdom will not load a blob URL,
// so the load event and a printable contentWindow are simulated.
function wireFrame(frame, printCalls, label) {
  Object.defineProperty(frame, 'contentWindow', {
    configurable: true,
    value: {
      focus() { },
      print() { printCalls.push(label); }
    }
  });
  Object.defineProperty(frame, 'contentDocument', { configurable: true, value: null });
  const srcDescriptor = { configurable: true, set(v) { this._src = v; setTimeout(() => this.onload && this.onload(), 0); }, get() { return this._src; } };
  Object.defineProperty(frame, 'src', srcDescriptor);
}

async function run() {
  // ---------- 0. static guard: ltxPrintReady must never call the print dialog itself ----------
  {
    const code = fs.readFileSync(SRC, 'utf8');
    const start = code.indexOf('window.ltxPrintReady = async function');
    const end = code.indexOf('window.ltxPrintFail = function', start);
    assert.ok(start > 0 && end > start, 'ltxPrintReady located');
    // Comments mention ltxTriggerPrint on purpose; only executable code is checked.
    const body = code.slice(start, end).replace(/^\s*\/\/.*$/gm, '');
    assert.ok(!/ltxTriggerPrint/.test(body),
      'REGRESSION: ltxPrintReady must not trigger printing — the preview is shown and nothing else.');
    assert.ok(!/\.print\(\)/.test(body),
      'REGRESSION: ltxPrintReady must not call print() — only the user\u2019s «چاپ» click may.');
  }

  // ---------- 1. popup preview: PDF ready must NOT open the browser print dialog ----------
  {
    const { window, printCalls, getPopup } = makeEnv();
    const id = window.ltxPrintBegin();
    const popup = getPopup();
    assert.ok(id, 'session id returned');
    assert.ok(popup, 'preview window opened');

    const frame = popup.document.getElementById('ltx-print-frame');
    assert.ok(frame, 'preview iframe exists');
    wireFrame(frame, printCalls, 'preview-iframe');

    const ok = await window.ltxPrintReady(id, window.eval('new Uint8Array([1,2,3])'), 'letter-42.pdf', 5000);
    assert.equal(ok, true, 'ltxPrintReady resolves true');

    assert.deepEqual(printCalls, [], 'NO print dialog is opened when the preview becomes ready');

    const status = popup.document.getElementById('ltx-print-status').textContent;
    assert.match(status, /پیش‌نمایش نامه آماده است/, 'status invites the user to print: ' + status);
    assert.equal(popup.document.getElementById('ltx-print-now').hidden, false, 'print button visible');
    assert.equal(popup.document.getElementById('ltx-print-download').hidden, false, 'download button visible');
    assert.equal(popup.document.title, 'letter-42.pdf', 'window titled with the file');
    assert.equal(popup.closed, false, 'preview stays open');

    // ---------- 2. clicking «چاپ» opens the print dialog exactly once ----------
    popup.document.getElementById('ltx-print-now').click();
    assert.deepEqual(printCalls, ['preview-iframe'], 'user click triggers the print dialog');
    assert.equal(popup.closed, false, 'preview still open while printing');

    // ---------- 3. after the print dialog closes (print OR cancel) the preview survives ----------
    const after = popup.document.createEvent('Event');
    after.initEvent('afterprint', false, false);
    popup.dispatchEvent(after);
    assert.equal(popup.closed, false, 'preview is NOT closed by afterprint');
    assert.ok(window.ltxPrintSessions[id], 'session kept alive for another print');
    assert.equal(popup.document.getElementById('ltx-print-now').disabled, false, 'print button re-enabled');

    // ---------- 4. the user can print again ----------
    popup.document.getElementById('ltx-print-now').click();
    assert.deepEqual(printCalls, ['preview-iframe', 'preview-iframe'], 'second print works');
    popup.dispatchEvent(after);

    // ---------- 5. «بستن» closes the preview and frees the session ----------
    popup.document.getElementById('ltx-print-close').click();
    assert.equal(popup.closed, true, 'close button closes the preview window');
    assert.equal(window.ltxPrintSessions[id], undefined, 'session cleaned up');
    assert.equal(window.ltxPrintWasCancelled(id), true, 'closing is reported as a user cancel, not a failure');
  }

  // ---------- 6. popup blocked → in-page overlay, same rules ----------
  {
    const { window, printCalls } = makeEnv({ popupBlocked: true });
    const id = window.ltxPrintBegin();
    const overlay = window.document.querySelector('.ltx-print-overlay');
    assert.ok(overlay, 'in-page overlay created when popups are blocked');

    const frame = overlay.querySelector('.ltx-print-frame');
    wireFrame(frame, printCalls, 'overlay-iframe');

    const ok = await window.ltxPrintReady(id, window.eval('new Uint8Array([1,2,3])'), 'letter-7.pdf', 5000);
    assert.equal(ok, true, 'overlay preview ready');
    assert.deepEqual(printCalls, [], 'overlay preview does not auto-print');
    assert.equal(overlay.querySelector('.ltx-print-now').hidden, false, 'overlay print button visible');

    overlay.querySelector('.ltx-print-now').click();
    assert.deepEqual(printCalls, ['overlay-iframe'], 'overlay print button works');

    overlay.querySelector('.ltx-print-close').click();
    assert.equal(window.document.querySelector('.ltx-print-overlay'), null, 'overlay removed on close');
    assert.equal(window.ltxPrintSessions[id], undefined, 'overlay session cleaned up');
  }

  // ---------- 7. failures still offer retry / direct print, and hide the print button ----------
  {
    const { window, getPopup } = makeEnv();
    const id = window.ltxPrintBegin();
    const popup = getPopup();
    assert.equal(window.ltxPrintFail(id, 'سرور پاسخ نداد'), true);
    const doc = popup.document;
    assert.equal(doc.getElementById('ltx-print-error').hidden, false, 'error shown');
    assert.match(doc.getElementById('ltx-print-error').textContent, /سرور پاسخ نداد/);
    assert.equal(doc.getElementById('ltx-print-retry').hidden, false, 'retry offered');
    assert.equal(doc.getElementById('ltx-print-direct').hidden, false, 'direct print offered');
    assert.equal(doc.getElementById('ltx-print-now').hidden, true, 'print button hidden while failed');

    const waiting = window.ltxPrintWaitForAction(id);
    doc.getElementById('ltx-print-retry').click();
    assert.equal(await waiting, 'retry', 'retry action reaches the C# side');
  }

  // ---------- 8. closing during a failure resolves the pending C# wait ----------
  {
    const { window, getPopup } = makeEnv();
    const id = window.ltxPrintBegin();
    const popup = getPopup();
    window.ltxPrintFail(id, 'خطا');
    const waiting = window.ltxPrintWaitForAction(id);
    popup.document.getElementById('ltx-print-close').click();
    assert.equal(await waiting, 'close', 'close action resolves the pending wait');
    window.ltxPrintCancel(id);
    assert.equal(window.ltxPrintSessions[id], undefined, 'cancel cleans up');
  }

  // ---------- 9. status updates during a slow server render ----------
  {
    const { window, getPopup } = makeEnv();
    const id = window.ltxPrintBegin();
    assert.equal(window.ltxPrintStatus(id, 'در حال ساخت پیش‌نمایش…'), true);
    assert.equal(getPopup().document.getElementById('ltx-print-status').textContent, 'در حال ساخت پیش‌نمایش…');
    window.ltxPrintCancel(id);
  }

  console.log('ALL PRINT-FLOW CHECKS PASSED');
}

run().then(() => process.exit(0), err => { console.error(err && err.stack || err); process.exit(1); });
