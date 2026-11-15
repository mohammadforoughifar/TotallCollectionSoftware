'use strict';
// Production CSS geometry test, NOT a rendered Blazor component or persistence test.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
let puppeteer;
try { puppeteer = require('puppeteer-core'); } catch (_) { }
const skip = !puppeteer || !process.env.CHROME_BIN ? 'Requires puppeteer-core and CHROME_BIN' : false;
const www = path.resolve(__dirname, '../../src/Inventory.Client/wwwroot');
const cssFile = path.resolve(__dirname, '../../src/Inventory.Client/Pages/Orders/OrderForm.razor.css');
function fixture(type) {
    const noun = type === 'sale' ? 'فروش' : 'خرید';
    return `<!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<link rel="stylesheet" href="/css/bootstrap.min.css"><link rel="stylesheet" href="/css/app.css"><link rel="stylesheet" href="/icons/bootstrap-icons.css"><link rel="stylesheet" href="/invoice.css"></head><body>
<div class="modal fade show d-block" tabindex="-1"><div class="modal-dialog modal-dialog-centered modal-xl invoice-dialog"><div class="modal-content">
<div class="modal-header purchase-order-header"><h5>ثبت فاکتور ${noun}</h5><button class="btn-close" aria-label="بستن"></button></div>
<div class="modal-body"><section class="purchase-summary"><div class="purchase-summary-main"><div><span>طرف حساب</span><strong>مشتری نمونه</strong></div><div><span>انبار</span><strong>مرکزی</strong></div><div><span>اقلام</span><strong>۱ سطر</strong></div><div class="purchase-summary-total"><span>مبلغ نهایی فاکتور</span><strong class="num">۵٬۱۲۴٬۲۴۴</strong></div></div><div class="purchase-summary-pay"><div><span>نقد</span><strong class="num">۵٬۱۲۴٬۲۴۴</strong></div><div><span>چک</span><strong class="num">۰</strong></div><div><span>اعلامیه پرداخت</span><strong class="num">۰</strong></div><div class="purchase-summary-remaining"><span>باقی‌مانده</span><strong class="num">۰</strong></div></div></section><div style="height:700px">اطلاعات پرداخت</div></div>
<div class="modal-footer"><div class="invoice-footer-actions"><button class="btn btn-light">انصراف (Esc)</button><button class="btn btn-outline-secondary"><i class="bi bi-arrow-right"></i> مرحله قبل (F3)</button><button class="btn btn-outline-primary"><i class="bi bi-printer"></i> ثبت و چاپ (F4)</button><button class="btn btn-primary"><i class="bi bi-check2"></i> ثبت فاکتور ${noun} (F1)</button></div></div>
</div></div></div></body></html>`;
}
let server, browser, url;
test.before(async () => {
    if (skip) return;
    server = http.createServer((req, res) => {
        const pathname = new URL(req.url, 'http://fixture').pathname;
        if (pathname === '/fixture') { res.setHeader('Content-Type', 'text/html; charset=utf-8'); res.end(fixture(new URL(req.url, 'http://fixture').searchParams.get('type'))); return; }
        const file = pathname === '/invoice.css' ? cssFile : path.resolve(www, '.' + pathname);
        if (file !== cssFile && !file.startsWith(www + path.sep)) { res.writeHead(403).end(); return; }
        if (!fs.existsSync(file) || !fs.statSync(file).isFile()) { res.writeHead(404).end(); return; }
        res.setHeader('Content-Type', file.endsWith('.css') ? 'text/css' : 'application/octet-stream'); res.end(fs.readFileSync(file));
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    url = `http://127.0.0.1:${server.address().port}/fixture`;
    browser = await puppeteer.launch({ executablePath: process.env.CHROME_BIN, headless: true, args: ['--no-sandbox', '--disable-dev-shm-usage', '--disable-gpu'] });
});
test.after(async () => { if (browser) await browser.close(); if (server) await new Promise(resolve => server.close(resolve)); });
for (const type of ['purchase', 'sale']) {
    for (const width of [320, 360, 390, 600, 768, 1280]) {
        test(`${type} payment footer at ${width}px has equal cells and visible bold totals`, { skip }, async () => {
            const page = await browser.newPage();
            try {
                await page.setViewport({ width, height: 740 });
                await page.goto(`${url}?type=${type}`, { waitUntil: 'networkidle0' });
                const result = await page.evaluate(() => {
                    const rect = e => { const r = e.getBoundingClientRect(); return { x:r.x, y:r.y, width:r.width, height:r.height, right:r.right, bottom:r.bottom }; };
                    return {
                        buttons: [...document.querySelectorAll('.invoice-footer-actions .btn')].map(rect),
                        footer: rect(document.querySelector('.modal-footer')),
                        totalWeight: +getComputedStyle(document.querySelector('.purchase-summary-total strong')).fontWeight,
                        paidWeight: +getComputedStyle(document.querySelector('.purchase-summary-pay strong')).fontWeight,
                        overflow: document.documentElement.scrollWidth > innerWidth
                    };
                });
                assert.equal(result.overflow, false);
                assert.ok(result.totalWeight >= 800 && result.paidWeight >= 800);
                for (const button of result.buttons) {
                    assert.ok(button.x >= -1 && button.right <= width + 1, JSON.stringify(result));
                    assert.ok(button.y >= 0 && button.bottom <= 741, JSON.stringify(result));
                    assert.ok(Math.abs(button.width - result.buttons[0].width) < 1, JSON.stringify(result));
                    assert.ok(Math.abs(button.height - result.buttons[0].height) < 1, JSON.stringify(result));
                }
                if (width <= 600) {
                    assert.ok(Math.abs(result.buttons[0].y - result.buttons[1].y) < 1);
                    assert.ok(Math.abs(result.buttons[2].y - result.buttons[3].y) < 1);
                    assert.ok(result.buttons[2].y > result.buttons[0].y);
                }
            } finally { await page.close(); }
        });
    }
}
