// Letter print flow: the preview is shown first and the browser print dialog opens ONLY on
// an explicit user action.
//
// Two paths are covered:
//   A. shared preview  — app-shell.js routes letters into shop-print-preview.js (the same
//                        component the shop invoices and the reports use).
//   B. legacy surface  — used only when that component / PDF.js is unavailable.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const SRC_DIR = path.resolve(__dirname, '../../../TotallCollectionSoftware/inventory/src/Inventory.Client/wwwroot');
const APP_SHELL = path.join(SRC_DIR, 'js/app-shell.js');
const SHOP_PREVIEW = path.join(SRC_DIR, 'js/shop-print-preview.js');

function printSection(code) {
  // Only the print section is exercised; the rest of app-shell.js touches APIs (fetch, signalR…)
  // that are irrelevant here.
  const start = code.indexOf('function ltxPdfBytes(payload) {');
  const end = code.indexOf('// ================== آدرس سرور API');
  assert.ok(start > 0 && end > start, 'print section markers found');
  return code.slice(start, end);
}

const stripComments = (s) => s.replace(/^\s*\/\/.*$/gm, '');

function baseWindow() {
  const dom = new JSDOM('<!doctype html><html><body></body></html>',
    { url: 'https://app.test/', runScripts: 'outside-only', pretendToBeVisual: true });
  const { window } = dom;
  window.URL.createObjectURL = () => 'blob:fake-' + Math.random().toString(36).slice(2);
  window.URL.revokeObjectURL = () => { };
  if (!window.requestAnimationFrame) window.requestAnimationFrame = (cb) => setTimeout(() => cb(Date.now()), 0);
  return window;
}

const pdfPayload = (window) => window.eval("new TextEncoder().encode('%PDF-1.7\\n% letter bytes\\n%%EOF')");

// ===========================================================================================
// 0. Static guards — neither preview may call print() while merely becoming ready.
// ===========================================================================================
function staticGuards() {
  const app = fs.readFileSync(APP_SHELL, 'utf8');
  const legacyReady = stripComments(app.slice(
    app.indexOf('async function ltxLegacyPrintReady'), app.indexOf('function ltxLegacyPrintFail')));
  assert.ok(legacyReady.length > 200, 'legacy ready body located');
  assert.ok(!/ltxTriggerPrint/.test(legacyReady),
    'REGRESSION: showing the preview must not trigger printing');
  assert.ok(!/\.print\(\)/.test(legacyReady),
    'REGRESSION: showing the preview must not call print()');

  const shop = stripComments(fs.readFileSync(SHOP_PREVIEW, 'utf8'));
  const printCalls = shop.split('\n').filter(l => /\.print\(\)/.test(l));
  assert.equal(printCalls.length, 1, 'shared preview has exactly one print() call site');
  const printNow = shop.slice(shop.indexOf('async function printNow'), shop.indexOf('function keyboard'));
  assert.ok(printNow.includes('target.print()'), 'the only print() lives in printNow (the button handler)');

  const loader = shop.slice(shop.indexOf('function loadPdfIntoSession'), shop.indexOf('function openPdf'));
  assert.ok(!/\.print\(\)/.test(loader), 'REGRESSION: loading the PDF must not print');
  assert.ok(!/createElement\('iframe'\)/.test(loader),
    'PERF REGRESSION: loadPdfIntoSession must not build the second-decode print iframe');

  // Signature ink is turned into one vector rect per pixel-run, so the raster cap drives the
  // size of every letter PDF. The slot is 104x52pt, i.e. 300 DPI needs only ~433px.
  const SERVICE = path.resolve(SRC_DIR, '../../Inventory.Api/Services/Office/Outgoing/OutgoingLetterPrintService.cs');
  const cs = fs.readFileSync(SERVICE, 'utf8');
  const capW = Number(/SignatureMaxWidth = (\d+)/.exec(cs)[1]);
  const capH = Number(/SignatureMaxHeight = (\d+)/.exec(cs)[1]);
  const dpi = capW / (104 / 72);
  assert.ok(dpi >= 300, `signature cap ${capW}px = ${Math.round(dpi)} DPI, below print quality`);
  assert.ok(dpi <= 500, `signature cap ${capW}px = ${Math.round(dpi)} DPI, oversampled — it bloats every PDF`);
  assert.ok(capH >= 180 && capH <= 300, 'signature height cap out of range: ' + capH);
  console.log(`  ✓ static guards (signature cap ${capW}x${capH} = ${Math.round(dpi)} DPI)`);

  // --- inner-letter bidi: AlignRight() sets alignment, NOT the bidi base direction ---
  // Without an explicit direction the paragraph base came from the page's
  // ContentFromLeftToRight(), so a Persian line rendered LTR: the full stop jumped to the
  // right edge and the list number to the left edge. Body text needs both the explicit
  // direction and the RLM wrapping.
  const OFFICE = path.resolve(SRC_DIR, '../../Inventory.Api/Services/Office');
  const inner = fs.readFileSync(path.join(OFFICE, 'InnerLetterPdfRenderer.cs'), 'utf8');
  const tmpl = fs.readFileSync(path.join(OFFICE, 'LetterPrintTemplate.cs'), 'utf8');

  assert.ok(/RtlPlainText\(PlainText\(data\.Body\)\)/.test(inner),
    'BIDI REGRESSION: inner-letter body must be wrapped line-by-line with RtlPlainText');
  for (const m of inner.matchAll(/\.Text\((?:body|LetterPrintTemplate\.Rtl[A-Za-z]*\()[^;]*;/g)) {
    assert.ok(/DirectionFromRightToLeft\(\)/.test(m[0]),
      'BIDI REGRESSION: body/label text without DirectionFromRightToLeft(): ' + m[0].slice(0, 90));
  }
  const align = (inner.match(/\.AlignRight\(\)/g) || []).length;
  const rtl = (inner.match(/DirectionFromRightToLeft\(\)/g) || []).length;
  assert.ok(rtl >= align, `BIDI REGRESSION: ${align} AlignRight() vs only ${rtl} DirectionFromRightToLeft()`);

  // RLM/LRI/PDI are zero-width, so the wrapper must not alter the visible text.
  assert.ok(tmpl.includes('\\u200F') && /IsolateLatinRuns/.test(tmpl),
    'LetterPrintTemplate must own the RLM + Latin-isolation helpers');
  assert.ok(/\u2066/.test(tmpl) || tmpl.includes('\\u2066'), 'Latin runs must be isolated with LRI');
  console.log(`  ✓ inner-letter bidi (${rtl} explicit RTL directions, ${align} AlignRight)`);
}

// ===========================================================================================
// A. shared preview (shop-print-preview) — what letters now use
// ===========================================================================================
async function sharedPreview() {
  const window = baseWindow();
  const printed = [];

  const calls = { warm: 0, openPdf: 0 };
  window.filePreview = {
    warm: async () => { calls.warm++; return true; },
    openPdf: async () => { calls.openPdf++; return { success: true, pages: 3 }; },
    renderPdf: async () => ({ success: true }),
    destroyPdf: async () => { }
  };
  // requestIdleCallback drives the print-frame pre-warm; make it observable and manual.
  let idleJob = null;
  window.requestIdleCallback = (cb) => { idleJob = cb; return 1; };
  window.eval(fs.readFileSync(SHOP_PREVIEW, 'utf8'));
  window.eval(printSection(fs.readFileSync(APP_SHELL, 'utf8')));

  const doc = window.document;
  const TITLE = 'نامهٔ صادره ۱۲۷ — A4 با هامش';

  // --- the preview opens immediately, before the server has produced anything ---
  const id = await window.ltxPrintBegin(TITLE);
  assert.ok(String(id).startsWith('shop:'), 'letters route into the shared preview: ' + id);
  const overlay = doc.querySelector('.shop-print-preview');
  assert.ok(overlay, 'the shop preview overlay is used');
  assert.equal(overlay.querySelector('.shop-print-heading-text strong').textContent, 'پیش‌نمایش چاپ');
  assert.equal(overlay.querySelector('.shop-print-document-title').textContent, TITLE);

  const printBtn = overlay.querySelector('[data-action="print"]');
  const closeBtn = overlay.querySelector('[data-action="close"]');
  const retryBtn = overlay.querySelector('[data-action="retry"]');
  const download = overlay.querySelector('.shop-print-download');
  const status = overlay.querySelector('.shop-print-status');

  assert.equal(printBtn.disabled, true, 'print is disabled while the server renders');
  assert.equal(retryBtn.hidden, true, 'no retry button before a failure');
  assert.equal(download.hidden, true, 'no download link before the PDF exists');
  assert.match(status.textContent, /در حال ساخت/, 'status explains the wait: ' + status.textContent);
  // PDF.js (1 MB + a 2.3 MB worker) must start downloading during the server wait, not after.
  assert.equal(calls.warm, 1, 'PDF.js pre-warmed while the server renders');
  assert.equal(calls.openPdf, 0, 'nothing parsed yet — no bytes');

  // --- slow-render status updates reach the same strip ---
  assert.equal(await window.ltxPrintStatus(id, 'هنوز در حال ساخت…'), true);
  assert.equal(status.textContent, 'هنوز در حال ساخت…');

  // --- the PDF arrives: preview renders, print becomes available, nothing prints ---
  const shown = await window.ltxPrintReady(id, pdfPayload(window), 'letter-127-A4.pdf', 30000);
  assert.equal(shown, true, 'ltxPrintReady reports the preview is shown');
  await new Promise(r => setTimeout(r, 0));

  assert.deepEqual(printed, [], 'NO print dialog opened when the preview became ready');
  assert.equal(printBtn.disabled, false, 'print is now enabled');
  assert.equal(download.hidden, false, 'دریافت PDF offered');
  assert.equal(download.download, 'letter-127-A4.pdf');
  assert.equal(overlay.querySelector('.shop-print-pages').hidden, false, 'page navigation shown');
  assert.match(status.textContent, /پیش‌نمایش آماده است/, status.textContent);
  assert.equal(status.classList.contains('is-error'), false);

  // --- the heavy native-viewer iframe must not compete with the visible render ---
  assert.equal(doc.querySelector('.shop-print-pdf-frame'), null,
    'PERF REGRESSION: the print iframe decodes the PDF a second time; it must not be built eagerly');
  assert.equal(typeof idleJob, 'function', 'print frame is scheduled for idle time instead');

  // --- idle pre-warm builds it, so the later click stays instant ---
  idleJob();
  const hidden = doc.querySelector('.shop-print-pdf-frame');
  assert.ok(hidden, 'idle pre-warm created the print frame');
  hidden.dispatchEvent(new window.Event('load'));
  Object.defineProperty(hidden.contentWindow, 'print', { configurable: true, value: () => printed.push('pdf') });
  hidden.contentWindow.focus = () => { };

  // --- only an explicit click prints ---
  printBtn.click();
  await new Promise(r => setTimeout(r, 0));
  assert.deepEqual(printed, ['pdf'], 'the click opened the print dialog exactly once');
  assert.ok(doc.querySelector('.shop-print-preview'), 'preview survives printing');

  // --- closing is a choice, not an error ---
  assert.equal(await window.ltxPrintWasCancelled(id), false);
  closeBtn.click();
  assert.equal(doc.querySelector('.shop-print-preview'), null, 'close removes the overlay');
  assert.equal(await window.ltxPrintWasCancelled(id), true, 'closing reports as user cancel');
  console.log('  ✓ shared preview: opens empty → fills → prints only on click → closes clean');
}

async function sharedPreviewFailure() {
  const window = baseWindow();
  window.filePreview = { warm: async () => true, openPdf: async () => ({ success: true, pages: 1 }), renderPdf: async () => ({ success: true }), destroyPdf: async () => { } };
  window.requestIdleCallback = (cb) => { setTimeout(cb, 0); return 1; };
  window.eval(fs.readFileSync(SHOP_PREVIEW, 'utf8'));
  window.eval(printSection(fs.readFileSync(APP_SHELL, 'utf8')));

  const id = await window.ltxPrintBegin('نامهٔ صادره ۹۹');
  const overlay = window.document.querySelector('.shop-print-preview');
  const status = overlay.querySelector('.shop-print-status');
  const retryBtn = overlay.querySelector('[data-action="retry"]');

  assert.equal(await window.ltxPrintFail(id, 'سرور پاسخ نداد'), true);
  assert.equal(status.textContent, 'سرور پاسخ نداد');
  assert.equal(status.classList.contains('is-error'), true, 'error styling applied');
  assert.equal(retryBtn.hidden, false, 'retry offered inside the preview');
  assert.equal(overlay.querySelector('[data-action="print"]').disabled, true, 'still not printable');

  const waiting = window.ltxPrintWaitForAction(id);
  retryBtn.click();
  assert.equal(await waiting, 'retry', 'retry reaches the waiting C# flow');
  assert.equal(retryBtn.hidden, true, 'retry hides once taken');

  // closing while the flow waits must resolve it, never hang
  window.ltxPrintFail(id, 'باز هم نشد');
  const waiting2 = window.ltxPrintWaitForAction(id);
  overlay.querySelector('[data-action="close"]').click();
  assert.equal(await waiting2, 'close', 'closing resolves the pending wait');
  console.log('  ✓ shared preview: failure shows retry in-place and never hangs');
}

// ===========================================================================================
// B. legacy surface — only when the shared component is missing
// ===========================================================================================
function wireFrame(frame, printed, label) {
  Object.defineProperty(frame, 'contentWindow', {
    configurable: true,
    value: { focus() { }, print() { printed.push(label); } }
  });
  Object.defineProperty(frame, 'contentDocument', { configurable: true, value: null });
  Object.defineProperty(frame, 'src', {
    configurable: true,
    set(v) { this._src = v; setTimeout(() => this.onload && this.onload(), 0); },
    get() { return this._src; }
  });
}

async function legacyFallback() {
  const window = baseWindow();           // no window.shopPrintPreview at all
  const printed = [];
  let popupDom = null;
  window.open = function () {
    popupDom = new JSDOM('<!doctype html><html><body></body></html>',
      { url: 'https://app.test/popup', runScripts: 'outside-only' });
    const pw = popupDom.window;
    pw.closed = false;
    pw.close = () => { pw.closed = true; };
    pw.focus = () => { };
    pw.opener = window;
    pw.requestAnimationFrame = (cb) => setTimeout(() => cb(Date.now()), 0);
    return pw;
  };
  window.eval(printSection(fs.readFileSync(APP_SHELL, 'utf8')));

  const id = await window.ltxPrintBegin('نامهٔ داخلی ۵');
  assert.ok(!String(id).startsWith('shop:'), 'falls back to the legacy surface');
  const popup = popupDom.window;
  const frame = popup.document.getElementById('ltx-print-frame');
  wireFrame(frame, printed, 'legacy');

  assert.equal(await window.ltxPrintReady(id, pdfPayload(window), 'letter-5.pdf', 5000), true);
  assert.deepEqual(printed, [], 'legacy surface does not auto-print either');
  assert.equal(popup.document.getElementById('ltx-print-now').hidden, false);

  popup.document.getElementById('ltx-print-now').click();
  assert.deepEqual(printed, ['legacy'], 'legacy print button works');

  popup.document.getElementById('ltx-print-close').click();
  assert.equal(popup.closed, true);
  assert.equal(await window.ltxPrintWasCancelled(id), true);
  console.log('  ✓ legacy fallback still preview-first');
}

(async () => {
  staticGuards();
  await sharedPreview();
  await sharedPreviewFailure();
  await legacyFallback();
  console.log('ALL PRINT-FLOW CHECKS PASSED');
})().then(() => process.exit(0), err => { console.error(err && err.stack || err); process.exit(1); });
