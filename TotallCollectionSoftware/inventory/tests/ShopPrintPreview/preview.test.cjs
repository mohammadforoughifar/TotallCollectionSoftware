'use strict';

// Exercises the production preview in Chromium; this is not a Blazor/.NET integration test.
// Run with CHROME_BIN=/path/to/chromium and puppeteer-core in NODE_PATH.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
let puppeteer;
try { puppeteer = require('puppeteer-core'); }
catch (error) {
    if (error.code !== 'MODULE_NOT_FOUND' || !error.message.includes("'puppeteer-core'")) throw error;
}
const skip = !puppeteer || !process.env.CHROME_BIN ? 'Set CHROME_BIN and install puppeteer-core.' : false;
const www = path.resolve(__dirname, '../../src/Inventory.Client/wwwroot');
const fixture = `<!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<link rel="stylesheet" href="/css/bootstrap.min.css"><link rel="stylesheet" href="/css/app.css">
<link rel="stylesheet" href="/css/shop-print-preview.css">
<link rel="stylesheet" href="/css/letters.css">
<style>@media print { .no-print { display:none!important } .report-title { color:rgb(90, 10, 20) } }</style></head><body>
<div class="tabpane on" data-shop-scope="true">
  <button id="under-save" data-hotkey="f1" onclick="window.saves++">ذخیره</button>
  <button id="under-close" data-hotkey="esc" onclick="window.closedUnderlying++">بستن فرم</button>
  <div id="report" class="fin-page-container">
    <h1 class="report-title">صورت‌حساب نمونه</h1><p>مانده: ۲۰٬۰۰۰٬۰۰۰ ریال</p><span>REPORT-VISIBLE-20000000</span>
    <span class="no-print">HIDE-NO-PRINT</span><span class="d-print-none">HIDE-D-PRINT</span>
    <input id="amount" value="old"><input id="checked" type="checkbox">
    <select id="choice"><option>A</option><option>B</option></select>
    <textarea id="description">old</textarea><canvas id="chart" width="12" height="8"></canvas>
  </div>
  <section id="guide"><details id="faq"><summary>سؤال</summary><p>پاسخ</p></details></section>
  <div id="card" class="bcard"><div class="bcard-name">معرف</div><div class="bcard-qr"><canvas width="5" height="5"></canvas></div></div>
</div>
<script>window.saves=0;window.closedUnderlying=0;window.prints=0;window.print=()=>window.prints++;</script>
<script src="/js/order-entry.js"></script><script src="/js/file-preview.js"></script>
<script src="/js/shop-print-preview.js"></script></body></html>`;

function pdfFixture(landscape = false, mixed = false) {
    // Small, valid two-page PDF; all offsets are calculated, not hard-coded.
    const width = landscape ? 842 : 595, height = landscape ? 595 : 842;
    const width2 = mixed ? height : width, height2 = mixed ? width : height;
    const stream1 = `BT /F1 18 Tf 40 ${height - 62} Td (First page) Tj ET\n`;
    const stream2 = `BT /F1 18 Tf 40 ${height2 - 62} Td (Second page) Tj ET\n`;
    const objects = [
        '<< /Type /Catalog /Pages 2 0 R >>',
        '<< /Type /Pages /Kids [3 0 R 5 0 R] /Count 2 >>',
        `<< /Type /Page /Parent 2 0 R /MediaBox [0 0 ${width} ${height}] /Resources << /Font << /F1 7 0 R >> >> /Contents 4 0 R >>`,
        `<< /Length ${Buffer.byteLength(stream1)} >>\nstream\n${stream1}endstream`,
        `<< /Type /Page /Parent 2 0 R /MediaBox [0 0 ${width2} ${height2}] /Resources << /Font << /F1 7 0 R >> >> /Contents 6 0 R >>`,
        `<< /Length ${Buffer.byteLength(stream2)} >>\nstream\n${stream2}endstream`,
        '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>'
    ];
    let text = '%PDF-1.4\n';
    const offsets = [0];
    objects.forEach((object, i) => { offsets.push(Buffer.byteLength(text)); text += `${i + 1} 0 obj\n${object}\nendobj\n`; });
    const xref = Buffer.byteLength(text);
    text += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
    offsets.slice(1).forEach(offset => { text += `${String(offset).padStart(10, '0')} 00000 n \n`; });
    text += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
    return Buffer.from(text).toString('base64');
}

function manyPagePdfFixture(pages) {
    const kids = Array.from({ length: pages }, (_, i) => `${i + 5} 0 R`).join(' ');
    const stream = 'BT /F1 18 Tf 40 530 Td (Products) Tj ET\n';
    const objects = [
        '<< /Type /Catalog /Pages 2 0 R >>',
        `<< /Type /Pages /Kids [${kids}] /Count ${pages} >>`,
        `<< /Length ${Buffer.byteLength(stream)} >>\nstream\n${stream}endstream`,
        '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',
        ...Array.from({ length: pages }, () => '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 842 595] /Resources << /Font << /F1 4 0 R >> >> /Contents 3 0 R >>'),
    ];
    let text = '%PDF-1.4\n';
    const offsets = [0];
    objects.forEach((object, i) => { offsets.push(Buffer.byteLength(text)); text += `${i + 1} 0 obj\n${object}\nendobj\n`; });
    const xref = Buffer.byteLength(text);
    text += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
    offsets.slice(1).forEach(offset => { text += `${String(offset).padStart(10, '0')} 00000 n \n`; });
    text += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
    return Buffer.from(text).toString('base64');
}

let browser, server, url;
test.before(async () => {
    if (skip) return;
    server = http.createServer((req, res) => {
        const pathname = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
        if (pathname === '/fixture') { res.setHeader('Content-Type', 'text/html; charset=utf-8'); res.end(fixture); return; }
        const file = path.resolve(www, '.' + pathname);
        if (!file.startsWith(www + path.sep) || !fs.existsSync(file) || !fs.statSync(file).isFile()) { res.writeHead(404); res.end(); return; }
        const types = { '.js': 'text/javascript', '.css': 'text/css', '.woff2': 'font/woff2', '.wasm': 'application/wasm' };
        res.setHeader('Content-Type', types[path.extname(file)] || 'application/octet-stream');
        fs.createReadStream(file).pipe(res);
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    url = `http://127.0.0.1:${server.address().port}/fixture`;
    browser = await puppeteer.launch({ executablePath: process.env.CHROME_BIN, headless: true,
        args: ['--no-sandbox', '--disable-dev-shm-usage', '--disable-gpu'] });
});
test.after(async () => {
    if (browser) await browser.close();
    if (server) await new Promise(resolve => server.close(resolve));
});

async function setup(t, width = 1280) {
    const page = await browser.newPage();
    t.after(() => page.close());
    await page.setViewport({ width, height: 800 });
    if (process.env.PREVIEW_DEBUG) {
        page.on('console', message => console.log('browser:', message.type(), message.text()));
        page.on('pageerror', error => console.error('browser error:', error));
    }
    await page.goto(url, { waitUntil: 'networkidle0' });
    return page;
}
async function ready(page) {
    try {
        await page.waitForFunction(() => {
            const b = document.querySelector('.shop-print-preview [data-action="print"]');
            return b && !b.disabled;
        }, { timeout: 25000 });
    } catch (error) {
        const status = await page.$eval('.shop-print-status', el => el.textContent).catch(() => 'preview missing');
        throw new Error(`${error.message}; status: ${status}`);
    }
}
async function htmlFrame(page) {
    return await (await page.$('.shop-print-frame')).contentFrame();
}
async function interceptFramePrint(page, selector = '.shop-print-frame') {
    await page.evaluate(sel => {
        const target = document.querySelector(sel).contentWindow;
        target.print = () => { window.prints++; };
    }, selector);
}

const a5 = '<!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8"><title>رسید خرید</title>' +
    '<style>@page{size:A5 portrait;margin:7mm}body{margin:0}.sheet{max-width:134mm;margin:auto}' +
    '@media print{.sheet{width:134mm} .not-for-print{display:none}}</style></head>' +
    '<body><main class="sheet"><h1>رسید خرید</h1><p>جمع ۱۲۳٬۴۵۶ ریال</p><p class="not-for-print">مخفی</p></main></body></html>';

test('HTML preview opens without printing; explicit print and Escape stay isolated from the form', { skip }, async t => {
    const page = await setup(t);
    await page.focus('#under-save');
    assert.equal(await page.evaluate(html => shopPrintPreview.openHtml(html), a5), true);
    await ready(page);
    if (process.env.PREVIEW_SCREENSHOT) await page.screenshot({ path: '/tmp/shop-preview-desktop.png' });
    await interceptFramePrint(page);
    const frame = await htmlFrame(page);
    assert.equal(await frame.title(), 'رسید خرید');
    assert.equal(await frame.$eval('.not-for-print', el => getComputedStyle(el).display), 'none');
    assert.match(await frame.$eval('main', el => el.textContent), /۱۲۳٬۴۵۶/);
    assert.equal(await page.evaluate(() => window.prints), 0);
    assert.equal(await page.evaluate(html => shopPrintPreview.openHtml(html), a5), false);
    await page.keyboard.press('F1');
    assert.equal(await page.evaluate(() => window.saves), 0);
    await page.click('.shop-print-preview [data-action="print"]');
    assert.equal(await page.evaluate(() => window.prints), 1);
    await page.keyboard.press('F4');
    assert.equal(await page.evaluate(() => window.prints), 2);
    await page.keyboard.press('Escape');
    await page.waitForFunction(() => !document.querySelector('.shop-print-preview'));
    assert.equal(await page.evaluate(() => window.closedUnderlying), 0);
    assert.equal(await page.evaluate(() => document.activeElement.id), 'under-save');
});

test('the isolated HTML frame allows the real browser print lifecycle only on request', { skip }, async t => {
    const page = await setup(t);
    assert.equal(await page.evaluate(html => shopPrintPreview.openHtml(html), a5), true);
    await ready(page);
    await page.evaluate(() => {
        document.querySelector('.shop-print-frame').contentWindow.addEventListener('beforeprint',
            () => { window.beforePrint = (window.beforePrint || 0) + 1; });
    });
    assert.equal(await page.evaluate(() => window.beforePrint || 0), 0);
    await page.click('.shop-print-preview [data-action="print"]');
    await page.waitForFunction(() => window.beforePrint === 1, { timeout: 5000 });
    assert.ok(await page.$('.shop-print-preview'), 'preview remains available after the print dialog');
});

test('report snapshot keeps live values and printed styles while leaving the original unchanged', { skip }, async t => {
    const page = await setup(t);
    const ok = await page.evaluate(() => {
        document.querySelector('#amount').value = '20000000';
        document.querySelector('#checked').checked = true;
        document.querySelector('#choice').selectedIndex = 1;
        document.querySelector('#description').value = 'توضیح زنده';
        const canvas = document.querySelector('#chart');
        canvas.getContext('2d').fillRect(0, 0, 12, 8);
        return shopPrintPreview.openElement(document.querySelector('#report'), 'صورت‌حساب');
    });
    assert.equal(ok, true);
    await ready(page);
    const frame = await htmlFrame(page);
    assert.equal(await frame.$eval('.report-title', el => getComputedStyle(el).visibility), 'visible');
    assert.equal(await frame.$$eval('link', links => links.some(link => new URL(link.href).pathname.endsWith('/css/letters.css'))), false);
    assert.equal(await page.$$eval('link', links => links.some(link => new URL(link.href).pathname.endsWith('/css/letters.css'))), true);
    assert.equal(await frame.$eval('#amount', el => el.value), '20000000');
    assert.equal(await frame.$eval('#checked', el => el.checked), true);
    assert.equal(await frame.$eval('#choice', el => el.value), 'B');
    assert.equal(await frame.$eval('#description', el => el.value), 'توضیح زنده');
    assert.equal(await frame.$eval('#chart', el => el.tagName), 'IMG');
    assert.equal(await frame.$eval('.no-print', el => getComputedStyle(el).display), 'none');
    assert.equal(await frame.$eval('.d-print-none', el => getComputedStyle(el).display), 'none');
    assert.equal(await frame.$eval('.report-title', el => getComputedStyle(el).color), 'rgb(90, 10, 20)');
    await page.click('.shop-print-preview [data-action="close"]');
    assert.equal(await page.$eval('#amount', el => el.value), '20000000');
    assert.equal(await page.$eval('#chart', el => el.tagName), 'CANVAS');
    assert.equal(await page.evaluate(() => window.prints), 0);
});

test('guide expands only the preview and card mode preserves the QR snapshot', { skip }, async t => {
    const page = await setup(t);
    assert.equal(await page.evaluate(() => shopPrintPreview.openElement(document.querySelector('#guide'), 'راهنما', 'guide')), true);
    await ready(page);
    let frame = await htmlFrame(page);
    assert.equal(await frame.$eval('#faq', el => el.open), true);
    assert.equal(await page.$eval('#faq', el => el.open), false);
    await page.click('.shop-print-preview [data-action="close"]');
    assert.equal(await page.evaluate(() => shopPrintPreview.openElement(document.querySelector('#card'), 'کارت', 'card')), true);
    await ready(page);
    frame = await htmlFrame(page);
    assert.equal(await frame.$eval('body', el => el.classList.contains('print-card-mode')), true);
    assert.equal(await frame.$eval('.bcard-qr img', el => el.src.startsWith('data:image/png')), true);
});

test('mobile preview fits the paper, supports zoom and does not run embedded scripts', { skip }, async t => {
    const page = await setup(t, 375);
    const html = a5.replace('</body>', '<script>parent.saves = 99;<\/script></body>');
    assert.equal(await page.evaluate(value => shopPrintPreview.openHtml(value), html), true);
    await ready(page);
    if (process.env.PREVIEW_SCREENSHOT) await page.screenshot({ path: '/tmp/shop-preview-mobile.png' });
    assert.equal(await page.evaluate(() => window.saves), 0);
    const dims = await page.evaluate(() => {
        const stage = document.querySelector('.shop-print-stage').getBoundingClientRect();
        const frame = document.querySelector('.shop-print-frame').getBoundingClientRect();
        return { stage: stage.width, frame: frame.width, left: frame.left, right: frame.right, screen: innerWidth };
    });
    assert.ok(dims.frame <= dims.stage, JSON.stringify(dims));
    assert.ok(dims.left >= 0 && dims.right <= dims.screen, JSON.stringify(dims));
    const before = await page.$eval('.shop-print-frame', el => el.getBoundingClientRect().width);
    await page.click('.shop-print-preview [data-action="zoom-in"]');
    assert.ok(await page.$eval('.shop-print-frame', el => el.getBoundingClientRect().width) > before);
    assert.equal(await page.evaluate(() => window.prints), 0);
});

test('PDF preview renders both pages; printing and download are explicit', { skip }, async t => {
    const page = await setup(t);
    assert.equal(await page.evaluate(pdf => shopPrintPreview.openPdf(pdf, 'products.pdf'), pdfFixture()), true);
    await ready(page);
    await page.waitForSelector('.shop-print-pdf-host canvas', { timeout: 25000 });
    if (process.env.PREVIEW_SCREENSHOT) await page.screenshot({ path: '/tmp/shop-preview-pdf.png' });
    assert.match(await page.$eval('.shop-print-page-label', el => el.textContent), /۱\s*\/\s*۲/);
    assert.equal(await page.$eval('.shop-print-download', el => el.download), 'products.pdf');
    assert.equal(await page.evaluate(() => window.prints), 0);
    await page.click('.shop-print-preview [data-action="next"]');
    await page.waitForFunction(() => /۲\s*\/\s*۲/.test(document.querySelector('.shop-print-page-label').textContent));
    await ready(page);
    // This headless Chromium has no native PDF viewer, so exercise the PDF.js
    // fallback and intercept the print of its newly created, isolated frame.
    await page.evaluate(() => {
        const append = document.body.appendChild;
        document.body.appendChild = function (child) {
            const result = append.call(this, child);
            if (child.classList && child.classList.contains('shop-print-pdf-render-frame')) {
                child.contentWindow.__nativePrint = child.contentWindow.print.bind(child.contentWindow);
                child.contentWindow.print = () => { window.prints++; };
            }
            return result;
        };
    });
    await page.click('.shop-print-preview [data-action="print"]');
    await page.waitForFunction(() => window.prints === 1, { timeout: 25000 });
    const frame = await (await page.$('.shop-print-pdf-render-frame')).contentFrame();
    assert.equal(await frame.$$eval('.page img', images => images.length), 2);
    assert.match(await frame.$eval('style', el => el.textContent), /size:595\.000pt 842\.000pt/);
    await page.evaluate(() => {
        const win = document.querySelector('.shop-print-pdf-render-frame').contentWindow;
        win.print = win.__nativePrint;
        win.addEventListener('beforeprint', () => { window.beforePdfPrint = (window.beforePdfPrint || 0) + 1; });
    });
    await ready(page);
    await page.click('.shop-print-preview [data-action="print"]');
    await page.waitForFunction(() => window.beforePdfPrint === 1, { timeout: 5000 });
    await page.click('.shop-print-preview [data-action="close"]');
    assert.equal(await page.$('.shop-print-pdf-frame'), null);
    await page.waitForFunction(() => filePreview.diagnostics().pdfViewers === 0);
});

test('long documents stay scrollable and A5 paper width is retained', { skip }, async t => {
    const page = await setup(t);
    const longHtml = a5.replace('</main>', '<div style="height:2200px">پایان گزارش</div></main>');
    assert.equal(await page.evaluate(html => shopPrintPreview.openHtml(html), longHtml), true);
    await ready(page);
    const width = await page.$eval('.shop-print-frame', el => parseFloat(el.style.width));
    assert.ok(Math.abs(width - 148 * 96 / 25.4) < 1);
    const frame = await htmlFrame(page);
    const range = await frame.evaluate(() => ({ height: document.documentElement.scrollHeight, client: document.documentElement.clientHeight }));
    assert.ok(range.height > range.client, JSON.stringify(range));
    await frame.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
    assert.ok(await frame.evaluate(() => window.scrollY) > 0);
});

test('PDF native-viewer path prints the original file rather than the raster fallback', { skip }, async t => {
    const page = await setup(t);
    await page.evaluate(() => Object.defineProperty(navigator, 'pdfViewerEnabled', { configurable: true, value: true }));
    assert.equal(await page.evaluate(pdf => shopPrintPreview.openPdf(pdf, 'products.pdf'), pdfFixture()), true);
    await ready(page);
    await page.evaluate(() => document.querySelector('.shop-print-pdf-frame').dispatchEvent(new Event('load')));
    await interceptFramePrint(page, '.shop-print-pdf-frame');
    await page.click('.shop-print-preview [data-action="print"]');
    await page.waitForFunction(() => window.prints === 1, { timeout: 3000 });
    assert.equal(await page.$('.shop-print-pdf-render-frame'), null);
});

test('closing a PDF preview during print preparation prevents a late print or leaked frame', { skip }, async t => {
    const page = await setup(t);
    assert.equal(await page.evaluate(pdf => shopPrintPreview.openPdf(pdf, 'products.pdf'), pdfFixture()), true);
    await ready(page);
    await page.evaluate(() => {
        const append = document.body.appendChild;
        document.body.appendChild = function (child) {
            const result = append.call(this, child);
            if (child.classList && child.classList.contains('shop-print-pdf-render-frame'))
                child.contentWindow.print = () => { window.prints++; };
            return result;
        };
    });
    // Dispatch synchronously so closure races the asynchronous PDF preparation.
    await page.evaluate(() => {
        document.querySelector('.shop-print-preview [data-action="print"]').click();
        document.querySelector('.shop-print-preview [data-action="close"]').click();
    });
    await new Promise(resolve => setTimeout(resolve, 800));
    assert.equal(await page.evaluate(() => window.prints), 0);
    assert.equal(await page.$('.shop-print-preview'), null);
    assert.equal(await page.$('.shop-print-pdf-frame'), null);
    await page.waitForFunction(() => filePreview.diagnostics().pdfViewers === 0);
});

test('missing report styles show an error and never start printing', { skip }, async t => {
    const page = await setup(t);
    await page.evaluate(() => {
        const link = document.createElement('link'); link.rel = 'stylesheet'; link.href = '/missing-shop-style.css';
        document.head.appendChild(link);
    });
    assert.equal(await page.evaluate(() => shopPrintPreview.openElement(document.querySelector('#report'), 'گزارش')), true);
    await page.waitForFunction(() => document.querySelector('.shop-print-status.is-error'), { timeout: 10000 });
    assert.equal(await page.$eval('.shop-print-preview [data-action="print"]', el => el.disabled), true);
    assert.equal(await page.evaluate(() => window.prints), 0);
    await page.keyboard.press('Escape');
    assert.equal(await page.$('.shop-print-preview'), null);
});

test('PDF fallback retains all landscape pages and their physical page size', { skip }, async t => {
    const page = await setup(t);
    assert.equal(await page.evaluate(pdf => shopPrintPreview.openPdf(pdf, 'products.pdf'), pdfFixture(true)), true);
    await ready(page);
    await page.evaluate(() => {
        const append = document.body.appendChild;
        document.body.appendChild = function (child) {
            const result = append.call(this, child);
            if (child.classList && child.classList.contains('shop-print-pdf-render-frame'))
                child.contentWindow.print = () => { window.prints++; };
            return result;
        };
    });
    await page.click('.shop-print-preview [data-action="print"]');
    await page.waitForFunction(() => window.prints === 1, { timeout: 25000 });
    const frame = await (await page.$('.shop-print-pdf-render-frame')).contentFrame();
    const html = await frame.evaluate(() => '<!doctype html>' + document.documentElement.outerHTML);
    const printable = await browser.newPage();
    t.after(() => printable.close());
    await printable.goto(url, { waitUntil: 'networkidle0' });
    await printable.setContent(html);
    await printable.waitForFunction(() => Array.from(document.images).every(img => img.complete && img.naturalWidth > 0));
    const bytes = await printable.pdf({ printBackground: true, preferCSSPageSize: true });
    const result = await page.evaluate(async data => {
        const lib = await import('/lib/pdfjs/pdf.js?v=6.3.289');
        lib.GlobalWorkerOptions.workerSrc = '/lib/pdfjs/pdf.worker.js?v=6.3.289';
        const task = lib.getDocument({ data: Uint8Array.from(data), isEvalSupported: false });
        const doc = await task.promise;
        const first = await doc.getPage(1);
        const size = first.getViewport({ scale: 1 });
        const answer = { pages: doc.numPages, width: size.width, height: size.height };
        await task.destroy();
        return answer;
    }, Array.from(bytes));
    assert.equal(result.pages, 2, JSON.stringify(result));
    assert.ok(Math.abs(result.width - 842) < 2 && Math.abs(result.height - 595) < 2, JSON.stringify(result));
});

test('report snapshot produces printable data without the non-print controls', { skip }, async t => {
    const page = await setup(t);
    assert.equal(await page.evaluate(() => shopPrintPreview.openElement(document.querySelector('#report'), 'گزارش')), true);
    await ready(page);
    const frame = await htmlFrame(page);
    const html = await frame.evaluate(() => '<!doctype html>' + document.documentElement.outerHTML);
    const printable = await browser.newPage();
    t.after(() => printable.close());
    await printable.goto(url, { waitUntil: 'networkidle0' });
    await printable.setContent(html, { waitUntil: 'domcontentloaded' });
    await printable.waitForFunction(() => Array.from(document.querySelectorAll('link[rel="stylesheet"]')).every(link => link.sheet));
    await printable.evaluate(() => document.fonts.ready);
    const bytes = await printable.pdf({ printBackground: true, preferCSSPageSize: true });
    const text = await page.evaluate(async data => {
        const lib = await import('/lib/pdfjs/pdf.js?v=6.3.289');
        lib.GlobalWorkerOptions.workerSrc = '/lib/pdfjs/pdf.worker.js?v=6.3.289';
        const task = lib.getDocument({ data: Uint8Array.from(data), isEvalSupported: false });
        const doc = await task.promise;
        let output = '';
        for (let i = 1; i <= doc.numPages; i++) {
            const content = await (await doc.getPage(i)).getTextContent();
            output += content.items.map(item => item.str).join('');
        }
        await task.destroy();
        return output;
    }, Array.from(bytes));
    assert.match(text, /REPORT-VISIBLE-20000000/);
    assert.doesNotMatch(text, /HIDE-NO-PRINT|HIDE-D-PRINT/);
});

test('closing a PDF immediately also disposes a late-created viewer', { skip }, async t => {
    const page = await setup(t);
    const result = await page.evaluate(async pdf => {
        const open = window.filePreview.openPdf;
        let opening;
        window.filePreview.openPdf = (...args) => (opening = open(...args));
        const started = shopPrintPreview.openPdf(pdf, 'products.pdf');
        document.querySelector('.shop-print-preview [data-action="close"]').click();
        await opening;
        await new Promise(resolve => setTimeout(resolve, 0));
        return { started, viewers: filePreview.diagnostics().pdfViewers };
    }, pdfFixture());
    assert.deepEqual(result, { started: true, viewers: 0 });
});

test('a PDF page render failure cannot silently show the previous page as ready', { skip }, async t => {
    const page = await setup(t);
    assert.equal(await page.evaluate(pdf => shopPrintPreview.openPdf(pdf, 'products.pdf'), pdfFixture()), true);
    await ready(page);
    await page.evaluate(() => {
        window.originalRenderPdf = filePreview.renderPdf;
        filePreview.renderPdf = async () => ({ success: false, code: 'FAILED', message: 'failed page' });
    });
    await page.click('.shop-print-preview [data-action="next"]');
    await page.waitForFunction(() => document.querySelector('.shop-print-status').classList.contains('is-error'));
    assert.equal(await page.$eval('.shop-print-preview [data-action="print"]', el => el.disabled), true);
    await page.evaluate(() => { filePreview.renderPdf = window.originalRenderPdf; });
    await page.click('.shop-print-preview [data-action="prev"]');
    await ready(page);
    assert.equal(await page.$eval('.shop-print-status', el => el.classList.contains('is-error')), false);
    assert.match(await page.$eval('.shop-print-page-label', el => el.textContent), /۱\s*\/\s*۲/);
});

test('mixed-size PDF fallback refuses to crop and explains how to print the original', { skip }, async t => {
    const page = await setup(t);
    assert.equal(await page.evaluate(pdf => shopPrintPreview.openPdf(pdf, 'mixed.pdf'), pdfFixture(false, true)), true);
    await ready(page);
    await page.click('.shop-print-preview [data-action="print"]');
    await page.waitForFunction(() => document.querySelector('.shop-print-status').classList.contains('is-error'), { timeout: 25000 });
    assert.match(await page.$eval('.shop-print-status', el => el.textContent), /اندازه/);
    assert.equal(await page.evaluate(() => window.prints), 0);
    assert.equal(await page.$('.shop-print-pdf-render-frame'), null);
    assert.equal(await page.$eval('.shop-print-download', el => el.hidden), false);
});

test('A5 invoice preview preserves page size and invoice content in print output', { skip }, async t => {
    const page = await setup(t);
    const html = a5.replace('<h1>رسید خرید</h1>', '<h1>رسید خرید INVOICE-123</h1>');
    assert.equal(await page.evaluate(markup => shopPrintPreview.openHtml(markup), html), true);
    await ready(page);
    const frame = await htmlFrame(page);
    const snapshot = await frame.evaluate(() => '<!doctype html>' + document.documentElement.outerHTML);
    const printable = await browser.newPage();
    t.after(() => printable.close());
    await printable.goto(url, { waitUntil: 'networkidle0' });
    await printable.setContent(snapshot);
    const bytes = await printable.pdf({ printBackground: true, preferCSSPageSize: true });
    const result = await page.evaluate(async data => {
        const lib = await import('/lib/pdfjs/pdf.js?v=6.3.289');
        lib.GlobalWorkerOptions.workerSrc = '/lib/pdfjs/pdf.worker.js?v=6.3.289';
        const task = lib.getDocument({ data: Uint8Array.from(data), isEvalSupported: false });
        const doc = await task.promise;
        const first = await doc.getPage(1), size = first.getViewport({ scale: 1 });
        const content = await first.getTextContent();
        const output = { pages: doc.numPages, width: size.width, height: size.height,
            text: content.items.map(item => item.str).join('') };
        await task.destroy();
        return output;
    }, Array.from(bytes));
    assert.equal(result.pages, 1);
    assert.ok(Math.abs(result.width - 419.5) < 2 && Math.abs(result.height - 595.3) < 2, JSON.stringify(result));
    assert.match(result.text, /INVOICE-123/);
});

test('a failed canvas snapshot never silently prints a blank QR or chart', { skip }, async t => {
    const page = await setup(t);
    const result = await page.evaluate(() => {
        const original = HTMLCanvasElement.prototype.toDataURL;
        HTMLCanvasElement.prototype.toDataURL = function () { throw new Error('tainted canvas'); };
        const opened = shopPrintPreview.openElement(document.querySelector('#card'), 'کارت', 'card');
        HTMLCanvasElement.prototype.toDataURL = original;
        return { opened, overlay: !!document.querySelector('.shop-print-preview'), prints: window.prints };
    });
    assert.deepEqual(result, { opened: false, overlay: false, prints: 0 });
});

test('oversized raster fallback offers the original instead of exhausting browser memory', { skip }, async t => {
    const page = await setup(t);
    assert.equal(await page.evaluate(pdf => shopPrintPreview.openPdf(pdf, 'large-products.pdf'), manyPagePdfFixture(100)), true);
    await ready(page);
    await page.click('.shop-print-preview [data-action="print"]');
    await page.waitForFunction(() => document.querySelector('.shop-print-status').classList.contains('is-error'), { timeout: 25000 });
    assert.match(await page.$eval('.shop-print-status', el => el.textContent), /بزرگ/);
    assert.equal(await page.evaluate(() => window.prints), 0);
    assert.equal(await page.$('.shop-print-pdf-render-frame'), null);
    assert.equal(await page.$eval('.shop-print-download', el => el.hidden), false);
});


test('letter print isolation hides surrounding UI but preserves paper and office guides', { skip }, async t => {
    const page = await setup(t);
    await page.evaluate(() => {
        const letter = document.createElement('section');
        letter.className = 'ltx';
        letter.innerHTML = '<nav class="rail">Letter tools</nav><article class="paper"><p id="letter-text">LETTER-VISIBLE</p></article>';
        document.body.appendChild(letter);
    });
    await page.emulateMediaType('print');
    assert.equal(await page.$eval('#letter-text', el => getComputedStyle(el).visibility), 'visible');
    assert.equal(await page.$eval('.ltx .rail', el => getComputedStyle(el).visibility), 'hidden');
    assert.equal(await page.$eval('.report-title', el => getComputedStyle(el).visibility), 'hidden');
    await page.evaluate(() => {
        const letter = document.querySelector('.ltx');
        letter.classList.add('guide-open');
        const guide = document.createElement('div');
        guide.className = 'guide-workspace';
        guide.innerHTML = '<p id="office-guide-text">GUIDE-VISIBLE</p>';
        letter.appendChild(guide);
    });
    assert.equal(await page.$eval('#office-guide-text', el => getComputedStyle(el).visibility), 'visible');
    // The office stylesheet and its original global isolation are unchanged.
    await page.evaluate(() => document.querySelector('.ltx').remove());
    assert.equal(await page.$eval('.report-title', el => getComputedStyle(el).visibility), 'hidden');
});
