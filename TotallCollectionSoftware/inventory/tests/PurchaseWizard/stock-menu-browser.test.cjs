'use strict';

// Optional Chromium fixture using the production SearchSelect and invoice CSS.
// It mirrors rendered markup, not a running Blazor component or stock API.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const http = require('node:http');
const path = require('node:path');

let puppeteer;
try { puppeteer = require('puppeteer-core'); }
catch (error) {
    if (error.code !== 'MODULE_NOT_FOUND' || !error.message.includes("'puppeteer-core'")) throw error;
}
const chrome = process.env.CHROME_BIN;
const skip = !puppeteer || !chrome
    ? 'Set CHROME_BIN and install puppeteer-core to run the browser stock-menu fixture.'
    : false;
const root = path.resolve(__dirname, '../..');
const www = path.join(root, 'src/Inventory.Client/wwwroot');
const formCss = fs.readFileSync(path.join(root, 'src/Inventory.Client/Pages/Orders/OrderForm.razor.css'), 'utf8')
    .replace(/::deep\s*/g, '');

function stockItem(id, state, qty, unit, warning) {
    return `<div id="${id}" class="ss-item ${state}"><span class="ss-item-copy">
        <span class="ss-item-name">کالای آزمایشی ${id}</span>
        <small class="ss-item-info invoice-stock-info">موجودی این انبار:
            <span class="invoice-stock-quantity ${warning ? 'is-warning' : ''}">
                ${warning ? '<i class="bi bi-exclamation-triangle-fill" aria-hidden="true"></i><span class="visually-hidden">هشدار موجودی صفر یا منفی: </span>' : ''}
                <bdi dir="ltr">${qty}</bdi>
            </span> ${unit}
        </small>
    </span></div>`;
}
const fixture = `<!doctype html><html dir="rtl" lang="fa"><head>
<meta name="viewport" content="width=device-width, initial-scale=1">
<link rel="stylesheet" href="/css/bootstrap.min.css"><link rel="stylesheet" href="/icons/bootstrap-icons.css">
<link rel="stylesheet" href="/css/app.css"><link rel="stylesheet" href="/form.css">
<style>body { padding: 12px; } .ss-menu { position: relative; width: min(100%, 350px); max-height: 250px; }</style>
</head><body><div class="ss-menu">
${stockItem('zero', '', '۰', 'عدد', true)}
${stockItem('positive', '', '۱۲.۳۴۵', 'کیلو', false)}
${stockItem('selected-zero', 'selected', '۰', 'عدد', true)}
${stockItem('highlighted-zero', 'highlighted', '۰', 'عدد', true)}
${stockItem('selected-highlighted-zero', 'selected highlighted', '۰', 'عدد', true)}
${stockItem('fraction', '', '۰.۰۰۱', 'لیتر', false)}
<div id="service" class="ss-item"><span class="ss-item-copy"><span class="ss-item-name">خدمات</span><small class="ss-item-info">خدمات</small></span></div>
<div id="unknown" class="ss-item"><span class="ss-item-copy"><span class="ss-item-name">کالای نامشخص</span><small class="ss-item-info">موجودی انبار نامشخص است</small></span></div>
</div></body></html>`;

function contrast(rgb1, rgb2) {
    const luminance = rgb => {
        const channels = rgb.match(/\d+/g).slice(0, 3).map(Number).map(value => {
            const channel = value / 255;
            return channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;
        });
        return 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];
    };
    const a = luminance(rgb1), b = luminance(rgb2);
    return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
}

function createServer() {
    return http.createServer((req, res) => {
        const url = new URL(req.url, 'http://localhost');
        if (url.pathname === '/') { res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' }); return res.end(fixture); }
        if (url.pathname === '/form.css') { res.writeHead(200, { 'content-type': 'text/css; charset=utf-8' }); return res.end(formCss); }
        const filename = path.resolve(www, '.' + url.pathname);
        if (!filename.startsWith(www + path.sep) || !fs.existsSync(filename) || !fs.statSync(filename).isFile()) {
            res.writeHead(404); return res.end();
        }
        const type = filename.endsWith('.css') ? 'text/css' : filename.endsWith('.woff2') ? 'font/woff2' : 'application/octet-stream';
        res.writeHead(200, { 'content-type': type });
        fs.createReadStream(filename).pipe(res);
    });
}

test('invoice stock menu warning remains readable in Chromium', { skip, timeout: 30000 }, async t => {
    const server = createServer();
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    t.after(() => new Promise(resolve => server.close(resolve)));
    const browser = await puppeteer.launch({ executablePath: chrome, headless: true, args: ['--no-sandbox', '--disable-setuid-sandbox'] });
    t.after(() => browser.close());
    for (const width of [320, 390, 1280]) await t.test(`stock labels and warning states at ${width}px`, async () => {
        const page = await browser.newPage();
        try {
            await page.setViewport({ width, height: 800, deviceScaleFactor: 1 });
            await page.goto(`http://127.0.0.1:${server.address().port}/`, { waitUntil: 'load' });
            await page.evaluate(() => document.fonts.ready);
            const result = await page.evaluate(() => {
                const $ = selector => document.querySelector(selector);
                const menu = $('.ss-menu');
                const box = menu.getBoundingClientRect();
                const warnings = [...document.querySelectorAll('.invoice-stock-quantity.is-warning')].map(node => {
                    const style = getComputedStyle(node);
                    return {
                        color: style.color, background: style.backgroundColor, weight: Number(style.fontWeight),
                        opacity: getComputedStyle(node.closest('small')).opacity,
                        value: node.querySelector('bdi')?.textContent.trim(),
                        icon: !!node.querySelector('i[aria-hidden="true"]'),
                        spokenWarning: !!node.querySelector('.visually-hidden')
                    };
                });
                return {
                    warnings,
                    positiveColor: getComputedStyle($('#positive .invoice-stock-quantity')).color,
                    fraction: $('#fraction bdi').textContent.trim(),
                    positive: $('#positive bdi').textContent.trim(),
                    serviceWarnings: $('#service').querySelectorAll('.is-warning').length,
                    unknownWarnings: $('#unknown').querySelectorAll('.is-warning').length,
                    unknown: $('#unknown').textContent,
                    scrollable: menu.scrollHeight > menu.clientHeight,
                    left: box.left, right: box.right, viewport: window.innerWidth,
                    horizontalOverflow: document.documentElement.scrollWidth > window.innerWidth
                };
            });
            assert.equal(result.warnings.length, 4);
            for (const warning of result.warnings) {
                assert.equal(warning.color, 'rgb(185, 28, 28)');
                assert.equal(warning.background, 'rgb(255, 241, 242)');
                assert.ok(contrast(warning.color, warning.background) >= 4.5, 'warning text meets small-text contrast');
                assert.ok(warning.weight >= 700);
                assert.equal(warning.opacity, '1');
                assert.equal(warning.value, '۰');
                assert.ok(warning.icon && warning.spokenWarning);
            }
            assert.notEqual(result.positiveColor, 'rgb(185, 28, 28)');
            assert.equal(result.positive, '۱۲.۳۴۵');
            assert.equal(result.fraction, '۰.۰۰۱');
            assert.equal(result.serviceWarnings, 0);
            assert.equal(result.unknownWarnings, 0);
            assert.match(result.unknown, /نامشخص/);
            assert.ok(result.scrollable);
            assert.ok(result.left >= 0 && result.right <= result.viewport);
            assert.equal(result.horizontalOverflow, false);
        } finally { await page.close(); }
    });
});
