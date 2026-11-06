'use strict';

// Optional real-Chromium layout check. The fixture mirrors the rendered item table;
// it reads the column weights, labels and styles from the Razor/CSS source. It does
// not replace a Blazor build or an interactive test of the application.
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
    ? 'Set CHROME_BIN and install puppeteer-core to run the browser layout test.'
    : false;

const root = path.resolve(__dirname, '../..');
const www = path.join(root, 'src/Inventory.Client/wwwroot');
const form = fs.readFileSync(path.join(root, 'src/Inventory.Client/Pages/Orders/OrderForm.razor'), 'utf8');
// ::deep attaches the component scope to the parent in the actual Blazor build.
const css = fs.readFileSync(path.join(root, 'src/Inventory.Client/Pages/Orders/OrderForm.razor.css'), 'utf8')
    .replace(/::deep\s*/g, '');
const items = form.split('<div data-order-entry', 2)[1];
const colsSource = items.split('<colgroup>', 2)[1].split('</colgroup>', 1)[0];
const headsSource = items.split('<thead>', 2)[1].split('</thead>', 1)[0];
const rowSource = items.split('<tr @key="l.Key" data-order-line="@l.Key">', 2)[1].split('</tr>', 1)[0];
const heads = [...headsSource.matchAll(/<th(?:\s[^>]*)?>[\s\S]*?<\/th>/g)].map(m => m[0]);
const cells = [...rowSource.matchAll(/<td(?:\s[^>]*)?>[\s\S]*?<\/td>/g)].map(m => m[0]);
const baseWeight = Number(form.match(/SaleBaseColumnWeight = (\d+)/)[1]);
const extraWeight = Number(form.match(/SaleExtraColumnWeight = (\d+)/)[1]);
const actionsRem = Number(form.match(/SaleActionsColumnRem = ([\d.]+)m/)[1]);
const weights = [...colsSource.matchAll(/InvoiceColumnWidth\(([^)]+)\)/g)].map(m =>
    m[1] === 'SaleExtraColumnWeight' ? [extraWeight] : m[1].split(',').map(x => Number(x.trim())));

function openCell(index) { return cells[index].match(/^<td\b[^>]*>/)[0]; }
function input(index, value) {
    return cells[index].match(/<input[\s\S]*?\/>/)[0]
        .replace(/@on\w+="[^"]*"/g, '')
        .replace(/value="[^"]*"/, `value="${value}"`);
}
function rateCell(index, value, unit) {
    const subtitleClass = cells[index].match(/<div class="(fs-9[^\"]*order-line-sub)">/)[1];
    return `${openCell(index)}<div class="input-group input-group-sm order-line-rate">
        ${input(index, value)}<span class="input-group-text order-line-unit">${unit}</span>
        </div><div class="${subtitleClass}">۱۰۰,۰۰۰ ریال</div></td>`;
}

function fixture({ sale, profit, commission, service, amount, purchasePrice }) {
    const extras = Number(profit) + Number(commission);
    const visibleWeights = [
        ...weights.slice(0, 3),
        ...(purchasePrice ? [weights[3]] : []),
        ...weights.slice(4, 9),
        ...(profit ? [weights[9]] : []),
        ...(commission ? [weights[10]] : [])
    ];
    const colTags = visibleWeights.map(spec => {
        const weight = sale ? (spec[1] ?? spec[0]) : spec[0];
        const width = sale ? `${(100 * weight / (baseWeight + extraWeight * extras)).toFixed(6)}%` : `${(100 * weight / (purchasePrice ? 111 : 100)).toFixed(6)}%`;
        return `<col style="width:${width}">`;
    }).join('') + `<col style="width:${sale ? `${actionsRem}rem` : `${(500 / (purchasePrice ? 111 : 100)).toFixed(6)}%`}">`;
    const header = [
        ...heads.slice(0, 3),
        ...(purchasePrice ? [heads[3]] : []),
        ...heads.slice(4, 9),
        ...(profit ? [heads[9]] : []),
        ...(commission ? [heads[10]] : []), heads[11]
    ].join('');
    const units = amount ? 'ریال' : '٪';
    const rateValue = amount ? '100,000' : '100';
    const lastPurchase = sale && !service
        ? '<button class="btn btn-sm btn-light" aria-label="آخرین فاکتور خرید این کالا"><i class="bi bi-receipt text-primary"></i></button>'
        : '';
    const row = [
        `${openCell(0)}<div class="ss"><input class="form-control order-line-control" value="P-0001 — روغن ترمز DOT4"><button class="ss-clear"><i class="bi bi-x-lg"></i></button></div>
            <div class="fs-8 text-muted mt-1 d-flex gap-2 flex-wrap"><span>واحد: لیتر</span><span class="text-success">· موجودی انبار: <b class="num">۱۰۰</b></span></div></td>`,
        `${openCell(1)}${input(1, '12.5')}</td>`,
        `${openCell(2)}${input(2, '1,000,000')}</td>`,
        ...(purchasePrice ? [`${openCell(3)}${input(3, '1,100,000')}</td>`] : []),
        `${openCell(4)}<span class="order-line-value">۱,۰۰۰,۰۰۰</span></td>`,
        rateCell(5, rateValue, units),
        `${openCell(6)}<span class="order-line-value">۱,۰۰۰,۰۰۰</span></td>`,
        rateCell(7, rateValue, units),
        `${openCell(8)}<span class="order-line-value">۱,۱۰۰,۰۰۰</span></td>`,
        ...(profit ? [`${openCell(9)}<div class="order-line-value"><span dir="ltr" class="fw-500 text-danger">-۱۰,۰۰۰,۰۰۰</span></div></td>`] : []),
        ...(commission ? [`${openCell(10)}<div class="order-line-value"><span class="text-primary">۱۰۰,۰۰۰</span></div></td>`] : []),
        `${openCell(11)}<div class="order-line-actions">${lastPurchase}<button class="btn btn-sm btn-light" aria-label="حذف سطر"><i class="bi bi-x-lg text-danger"></i></button></div></td>`
    ].join('');
    return `<!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <link rel="stylesheet" href="/css/bootstrap.min.css"><link rel="stylesheet" href="/icons/bootstrap-icons.css">
        <link rel="stylesheet" href="/css/app.css"><style>${css}</style></head><body>
        <div class="modal fade show d-block"><div class="modal-dialog modal-dialog-centered modal-xl invoice-dialog">
        <div class="modal-content"><div class="modal-header"><h5 class="modal-title">فاکتور</h5></div><div class="modal-body">
        <div data-order-entry class="purchase-items ${sale ? 'sale-items' : ''}">
        <table class="table table-hover align-middle mb-0"><colgroup>${colTags}</colgroup><thead><tr>${header}</tr></thead>
        <tbody><tr data-order-line="test">${row}</tr></tbody></table></div>
        </div></div></div></div></body></html>`;
}

async function serve() {
    const server = http.createServer((req, res) => {
        const url = new URL(req.url, 'http://127.0.0.1');
        if (url.pathname === '/') {
            const sale = url.searchParams.get('sale') === '1';
            const on = key => url.searchParams.get(key) === '1';
            res.setHeader('Content-Type', 'text/html; charset=utf-8');
            res.end(fixture({ sale, profit: sale && on('profit'), commission: sale && on('commission'), service: on('service'), amount: on('amount'), purchasePrice: !sale && on('purchasePrice') }));
            return;
        }
        const file = path.resolve(www, `.${url.pathname}`);
        if (!file.startsWith(`${www}${path.sep}`) || !fs.existsSync(file) || !fs.statSync(file).isFile()) {
            res.writeHead(404).end();
            return;
        }
        res.setHeader('Content-Type', file.endsWith('.css') ? 'text/css' : file.endsWith('.woff2') ? 'font/woff2' : 'application/octet-stream');
        fs.createReadStream(file).pipe(res);
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    return server;
}

async function inspect(page) {
    return page.evaluate(() => {
        const box = el => {
            const r = el.getBoundingClientRect();
            return { left: r.left, right: r.right, top: r.top, width: r.width };
        };
        const textBoxes = el => {
            const range = document.createRange();
            range.selectNodeContents(el);
            return [...range.getClientRects()].map(r => ({ left: r.left, right: r.right, top: r.top }));
        };
        const table = document.querySelector('.purchase-items table');
        const wrapper = document.querySelector('.purchase-items');
        const body = document.querySelector('.modal-body');
        const headers = [...table.querySelectorAll('thead th')];
        const cells = [...table.querySelectorAll('tbody td')];
        const actions = [...table.querySelectorAll('.order-line-actions button')];
        const values = [...table.querySelectorAll('.order-line-value')];
        const controls = [...table.querySelectorAll('input.order-line-control')];
        const rates = [...table.querySelectorAll('.order-line-rate')].map(rate => {
            const field = rate.querySelector('input');
            const unit = rate.querySelector('.order-line-unit');
            const style = getComputedStyle(field);
            const measure = document.createElement('span');
            for (const prop of ['font-family', 'font-size', 'font-weight', 'font-style', 'font-variant-numeric', 'letter-spacing']) {
                measure.style.setProperty(prop, style.getPropertyValue(prop));
            }
            measure.style.cssText += ';position:fixed;white-space:pre;visibility:hidden;';
            measure.textContent = field.value;
            document.body.appendChild(measure);
            const textWidth = measure.getBoundingClientRect().width;
            measure.remove();
            return {
                innerWidth: field.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight),
                textWidth, field: box(field), unit: box(unit), unitText: textBoxes(unit)
            };
        });
        const priceInputs = controls.filter(field => field.closest('td')?.getAttribute('data-label')?.includes('قیمت')).map(field => {
            const style = getComputedStyle(field);
            const measure = document.createElement('span');
            for (const prop of ['font-family', 'font-size', 'font-weight', 'font-style', 'font-variant-numeric', 'letter-spacing'])
                measure.style.setProperty(prop, style.getPropertyValue(prop));
            measure.style.cssText += ';position:fixed;white-space:pre;visibility:hidden;';
            measure.textContent = field.value;
            document.body.appendChild(measure);
            const textWidth = measure.getBoundingClientRect().width;
            measure.remove();
            return { textWidth, innerWidth: field.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight) };
        });
        return {
            priceInputs,
            documentWidth: document.documentElement.scrollWidth, viewportWidth: innerWidth,
            bodyScroll: body.scrollWidth, bodyWidth: body.clientWidth,
            wrapper: box(wrapper), table: box(table), row: box(table.querySelector('tbody tr')),
            headerDisplay: getComputedStyle(table.querySelector('thead')).display,
            rowDisplay: getComputedStyle(table.querySelector('tbody tr')).display,
            headers: headers.map(h => ({ ...box(h), paddingRight: getComputedStyle(h).paddingRight, text: h.querySelector('span') ? textBoxes(h.querySelector('span')) : [] })),
            cells: cells.map(c => ({ ...box(c), paddingRight: getComputedStyle(c).paddingRight })),
            actions: actions.map(box), controls: controls.map(box), rates,
            values: values.map(v => ({ ...box(v), text: textBoxes(v.querySelector('span') || v) }))
        };
    });
}

function inside(box, container, tolerance = 1) {
    return box.left >= container.left - tolerance && box.right <= container.right + tolerance;
}

async function check(page, base, width, params) {
    await page.setViewport({ width, height: 900 });
    await page.goto(`${base}/?${new URLSearchParams(params)}`, { waitUntil: 'load' });
    await page.evaluate(() => document.fonts.ready);
    const m = await inspect(page);
    const label = `${width}px ${JSON.stringify(params)}`;
    assert.ok(m.documentWidth <= m.viewportWidth + 1, `${label}: viewport overflow`);
    assert.ok(m.bodyScroll <= m.bodyWidth + 1, `${label}: horizontal modal overflow`);
    assert.ok(inside(m.table, m.wrapper), `${label}: table exceeds its container`);
    assert.equal(m.actions.length, params.sale === '1' && params.service !== '1' ? 2 : 1, label);
    for (const action of m.actions) assert.ok(inside(action, width >= 992 ? m.cells.at(-1) : m.row), `${label}: action escaped its column/row`);
    for (const rate of m.rates) {
        assert.ok(inside(rate.unit, rate.field), `${label}: unit escaped the input`);
        for (const text of rate.unitText) assert.ok(inside(text, rate.unit), `${label}: unit label clipped`);
        assert.ok(rate.textWidth <= rate.innerWidth + 1, `${label}: entered rate does not fit`);
    }
    if (width < 992) {
        assert.equal(m.headerDisplay, 'none', label);
        assert.equal(m.rowDisplay, 'grid', label);
        return;
    }
    assert.equal(m.headers.length, m.cells.length, `${label}: column count`);
    for (let i = 0; i < m.cells.length; i++) {
        const h = m.headers[i], c = m.cells[i];
        assert.ok(Math.abs(h.left - c.left) < 1 && Math.abs(h.right - c.right) < 1, `${label}: column ${i} misaligned`);
        assert.equal(h.paddingRight, c.paddingRight, `${label}: column ${i} content padding`);
        for (const text of h.text) assert.ok(inside(text, h), `${label}: header ${i} overlaps its neighbour`);
    }
    for (const value of m.values) {
        assert.equal(value.text.length, 1, `${label}: sample amount wrapped`);
        assert.ok(inside(value.text[0], value), `${label}: amount overlaps its neighbour`);
    }
    for (const control of m.controls) assert.ok(Math.abs(control.top - m.controls[0].top) < 1, `${label}: fields not on one line`);
    for (const price of m.priceInputs) assert.ok(price.textWidth <= price.innerWidth + 1, `${label}: sample price clipped`);
    if (params.sale === '1') assert.ok(m.cells[0].width > m.cells[1].width * 1.75, `${label}: product column lost its intended width`);
}

test('purchase and sale item columns align without overflow', { skip, timeout: 90000 }, async () => {
    const server = await serve();
    let browser;
    try {
        browser = await puppeteer.launch({ executablePath: chrome, headless: true, args: ['--no-sandbox', '--disable-dev-shm-usage', '--no-zygote', '--single-process'] });
        const page = await browser.newPage();
        const base = `http://127.0.0.1:${server.address().port}`;
        const variants = [
            { sale: '0' }, { sale: '0', purchasePrice: '1' }, { sale: '1' }, { sale: '1', profit: '1' },
            { sale: '1', commission: '1' }, { sale: '1', profit: '1', commission: '1' }
        ];
        for (const width of [992, 1024, 1280, 1568, 991, 768, 390]) {
            for (const variant of variants) await check(page, base, width, variant);
        }
        await check(page, base, 992, { sale: '1', profit: '1', commission: '1', amount: '1' });
        await check(page, base, 992, { sale: '1', profit: '1', commission: '1', service: '1' });
        await check(page, base, 390, { sale: '1', profit: '1', commission: '1', service: '1' });
    } finally {
        if (browser) await browser.close();
        await new Promise(resolve => server.close(resolve));
    }
});
