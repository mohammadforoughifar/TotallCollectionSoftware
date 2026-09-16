// راستی‌آزمایی بصری + ممیزی کنتراست/سرریز در مرورگر واقعی (Playwright/Chromium)
// اجرا: node /home/user/pw/audit.js
const { chromium } = require('playwright');
const fs = require('fs');

const OUT = '/home/user/shots';
const CLIENT = process.env.CLIENT_URL || 'http://localhost:5210';
fs.mkdirSync(OUT, { recursive: true });

const log = (...a) => { const s = a.join(' '); console.log(s); fs.appendFileSync(OUT + '/run.log', s + '\n'); };

/* ---------- ممیزی کنتراست روی همهٔ گره‌های متنیِ دیده‌شونده ---------- */
const CONTRAST_JS = () => {
  const parse = (c) => {
    if (!c) return null;
    const m = c.match(/rgba?\(([^)]+)\)/i);
    if (!m) return null;
    const p = m[1].split(/[,\s\/]+/).map(parseFloat).filter(x => !isNaN(x));
    if (p.length < 3) return null;
    return { r: p[0], g: p[1], b: p[2], a: p.length > 3 ? p[3] : 1 };
  };
  const lum = (c) => {
    const f = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); };
    return 0.2126 * f(c.r) + 0.7152 * f(c.g) + 0.0722 * f(c.b);
  };
  const over = (fg, bg) => ({ r: fg.r * fg.a + bg.r * (1 - fg.a), g: fg.g * fg.a + bg.g * (1 - fg.a), b: fg.b * fg.a + bg.b * (1 - fg.a), a: 1 });
  const describe = (el) => {
    const cls = (el.className && typeof el.className === 'string') ? el.className.trim().split(/\s+/).slice(0, 3).join('.') : '';
    return el.tagName.toLowerCase() + (el.id ? '#' + el.id : '') + (cls ? '.' + cls : '');
  };
  const darkestStop = (bi) => {
    const stops = (bi.match(/rgba?\([^)]*\)/gi) || []).map(parse).filter(Boolean);
    if (!stops.length) return null;
    return stops.reduce((a, b) => (lum(a) <= lum(b) ? a : b));
  };
  // لایه‌های نیمه‌شفاف (مثل rgba(255,255,255,.02) روی سایدبار تیره) باید روی لایهٔ زیرینِ
  // خود کامپوزیت شوند؛ و گرادیان، هرچه زیرش است را می‌پوشاند پس همان‌جا توقف می‌کنیم.
  const bgOf = (el) => {
    const stack = [];
    let n = el, base = null, image = null;
    while (n && n.nodeType === 1) {
      const cs = getComputedStyle(n);
      const bg = parse(cs.backgroundColor);
      if (bg && bg.a >= 1) { base = bg; break; }
      if (bg && bg.a > 0) stack.push(bg);
      const bi = cs.backgroundImage;
      if (bi && bi !== 'none') {
        image = { value: bi.slice(0, 90), from: describe(n), stop: darkestStop(bi) };
        base = image.stop;
        break;
      }
      n = n.parentElement;
    }
    if (!base) base = { r: 255, g: 255, b: 255, a: 1 };
    let acc = base;
    for (let k = stack.length - 1; k >= 0; k--) acc = over(stack[k], acc);
    if (image && stack.length === 0) return { kind: 'image', value: image.value, from: image.from };
    return { kind: 'color', color: acc };
  };

  const bad = [], gradients = [], tiny = [];
  const w = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
  let node;
  while ((node = w.nextNode())) {
    const text = (node.textContent || '').replace(/\s+/g, ' ').trim();
    if (!text) continue;
    const el = node.parentElement;
    if (!el) continue;
    const cs = getComputedStyle(el);
    if (cs.display === 'none' || cs.visibility === 'hidden' || parseFloat(cs.opacity) < 0.05) continue;
    const r = el.getBoundingClientRect();
    if (r.width < 1 || r.height < 1) continue;
    if (r.bottom < 0 || r.top > innerHeight + 400) continue;

    const fs = parseFloat(cs.fontSize);
    const bold = parseInt(cs.fontWeight, 10) >= 700;
    if (fs < 11) tiny.push({ text: text.slice(0, 40), sel: describe(el), fontSize: fs });

    const fgRaw = parse(cs.color);
    if (!fgRaw) continue;
    const bgInfo = bgOf(el);
    if (bgInfo.kind === 'image') { gradients.push({ text: text.slice(0, 40), sel: describe(el), color: cs.color, bg: bgInfo.value, from: bgInfo.from }); continue; }
    const fg = fgRaw.a < 1 ? over(fgRaw, bgInfo.color) : fgRaw;
    const l1 = lum(fg), l2 = lum(bgInfo.color);
    const ratio = (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
    const need = (fs >= 24 || (fs >= 18.66 && bold)) ? 3 : 4.5;
    if (ratio < need) bad.push({
      text: text.slice(0, 40), sel: describe(el), color: cs.color,
      bg: `rgb(${Math.round(bgInfo.color.r)},${Math.round(bgInfo.color.g)},${Math.round(bgInfo.color.b)})`,
      ratio: +ratio.toFixed(2), need, fontSize: fs
    });
  }
  const uniq = a => { const s = new Set(); return a.filter(x => { const k = JSON.stringify(x); if (s.has(k)) return false; s.add(k); return true; }); };
  return { bad: uniq(bad).slice(0, 60), gradients: uniq(gradients).slice(0, 30), tiny: uniq(tiny).slice(0, 30) };
};

/* ---------- ممیزی سرریز/چیدمان ---------- */
const OVERFLOW_JS = () => {
  const describe = (el) => {
    const cls = (el.className && typeof el.className === 'string') ? el.className.trim().split(/\s+/).slice(0, 3).join('.') : '';
    return el.tagName.toLowerCase() + (el.id ? '#' + el.id : '') + (cls ? '.' + cls : '');
  };
  const vw = document.documentElement.clientWidth;
  const outside = [], hOverflow = [], rtlIssues = [];
  document.querySelectorAll('body *').forEach(el => {
    const cs = getComputedStyle(el);
    if (cs.display === 'none' || cs.visibility === 'hidden') return;
    const r = el.getBoundingClientRect();
    if (r.width < 1 || r.height < 1) return;
    if (r.bottom < 0 || r.top > innerHeight + 800) return;
    const inModal = !!el.closest('.modal-content');
    if (!inModal && (r.right > vw + 2 || r.left < -2))
      outside.push({ sel: describe(el), left: Math.round(r.left), right: Math.round(r.right), vw });
    const ox = cs.overflowX;
    if (el.scrollWidth > el.clientWidth + 2 && !['auto', 'scroll', 'hidden'].includes(ox))
      hOverflow.push({ sel: describe(el), scrollWidth: el.scrollWidth, clientWidth: el.clientWidth });
    // چک‌باکس/رادیو: ورودی باید در RTL سمت «راست» برچسب باشد
    if (el.classList.contains('form-check')) {
      const input = el.querySelector('.form-check-input');
      const label = el.querySelector('.form-check-label');
      if (input && label) {
        const a = input.getBoundingClientRect(), b = label.getBoundingClientRect();
        const sameLine = Math.abs((a.top + a.height / 2) - (b.top + b.height / 2)) < Math.max(a.height, b.height);
        if (!sameLine || a.left < b.left - 4)
          rtlIssues.push({ sel: describe(el), inputLeft: Math.round(a.left), labelLeft: Math.round(b.left), sameLine });
      }
    }
  });
  const modal = document.querySelector('.modal-content');
  let modalInfo = null;
  if (modal) {
    const body = modal.querySelector('.modal-body');
    const cs = getComputedStyle(modal);
    modalInfo = {
      maxHeight: cs.maxHeight, height: Math.round(modal.getBoundingClientRect().height),
      innerHeight, bodyOverflowY: body ? getComputedStyle(body).overflowY : null,
      bodyScrolls: body ? body.scrollHeight > body.clientHeight + 2 : null,
      footerVisible: (() => { const f = modal.querySelector('.modal-footer'); if (!f) return null; const r = f.getBoundingClientRect(); return r.bottom <= innerHeight + 2 && r.height > 0; })()
    };
  }
  const uniq = a => { const s = new Set(); return a.filter(x => { const k = JSON.stringify(x); if (s.has(k)) return false; s.add(k); return true; }); };
  return {
    vw, docScrollWidth: document.documentElement.scrollWidth,
    pageHasHorizontalScroll: document.documentElement.scrollWidth > vw + 1,
    outside: uniq(outside).slice(0, 25), hOverflow: uniq(hOverflow).slice(0, 25), rtlIssues: uniq(rtlIssues).slice(0, 25), modal: modalInfo
  };
};

const VIEWPORTS = {
  desktop: { width: 1440, height: 900 },
  mobile: { width: 390, height: 844, isMobile: true, hasTouch: true, deviceScaleFactor: 2 }
};

const snap = async (page, name, opts = {}) => { try { await page.screenshot({ path: `${OUT}/${name}.png`, ...opts }); log('  📸', name); } catch (e) { log('  !! screenshot fail', name, e.message.split('\n')[0]); } };
const audit = async (page, label) => {
  const c = await page.evaluate(CONTRAST_JS).catch(e => ({ error: e.message }));
  const o = await page.evaluate(OVERFLOW_JS).catch(e => ({ error: e.message }));
  log(`  🔎 ${label}: کنتراست بد=${(c.bad || []).length} گرادیان=${(c.gradients || []).length} فونت‌ریز=${(c.tiny || []).length} | بیرون‌زدگی=${(o.outside || []).length} سرریزافقی=${(o.hOverflow || []).length} فرم‌چک=${(o.rtlIssues || []).length} اسکرول‌افقی‌صفحه=${o.pageHasHorizontalScroll}`);
  return { contrast: c, overflow: o };
};
const clickText = async (page, sel, ms = 1200) => { try { const l = page.locator(sel).first(); if (await l.count()) { await l.click({ timeout: 8000 }); await page.waitForTimeout(ms); return true; } } catch (e) { log('  !! click fail', sel, e.message.split('\n')[0]); } return false; };

(async () => {
  const browser = await chromium.launch({ args: ['--no-sandbox', '--disable-dev-shm-usage', '--font-render-hinting=none'] });
  const report = { client: CLIENT, generatedAt: new Date().toISOString(), viewports: {} };

  for (const [name, vp] of Object.entries(VIEWPORTS)) {
    log('==================', name, JSON.stringify(vp));
    const ctx = await browser.newContext({ viewport: { width: vp.width, height: vp.height }, isMobile: !!vp.isMobile, hasTouch: !!vp.hasTouch, deviceScaleFactor: vp.deviceScaleFactor || 1, locale: 'fa-IR' });
    const page = await ctx.newPage();
    page.setDefaultTimeout(90000);
    page.on('pageerror', e => log('  ⚠ pageerror:', e.message.split('\n')[0]));
    const R = report.viewports[name] = {};

    try {
      await page.goto(CLIENT + '/', { waitUntil: 'domcontentloaded' });
      await page.waitForSelector('input[type="password"]', { timeout: 240000 });
      await page.locator('input[dir="ltr"]').first().fill('admin');
      await page.locator('input[type="password"]').fill('admin');
      await snap(page, `${name}-01-login`);
      await page.locator('button.btn-primary.w-100').first().click();
      await page.waitForSelector('input[type="password"]', { state: 'detached', timeout: 90000 });
      log('  ✅ ورود موفق');

      await page.goto(CLIENT + '/doc-archive', { waitUntil: 'domcontentloaded' });
      await page.waitForSelector('.da-page-actions, .da-workspace', { timeout: 120000 });
      await page.waitForTimeout(3000);
      await snap(page, `${name}-02-explorer`, { fullPage: true });
      R.explorer = await audit(page, 'صفحهٔ آرشیو');

      // ---- مودال «پوشه جدید» (شامل ویرایشگر دسترسی) ----
      if (await clickText(page, 'button:has-text("پوشه جدید")')) {
        await page.waitForSelector('.modal-content');
        await page.waitForTimeout(1200);
        await snap(page, `${name}-03-folder-modal`);
        R.folderModal = await audit(page, 'مودال پوشه جدید');
        if (await clickText(page, 'details.da-folder-permissions > summary', 900)) {
          await snap(page, `${name}-04-folder-permissions`, { fullPage: false });
          R.folderPermissions = await audit(page, 'ویرایشگر دسترسی پوشه');
          // یک نفر اضافه کنیم تا جدول دسترسی هم دیده شود
          const lvl = page.locator('.doc-perm-editor select').first();
          if (await lvl.count()) await lvl.selectOption({ index: 2 }).catch(() => { });
        }
        // نام پوشه + ذخیره
        const folderName = 'پوشهٔ آزمایش ' + Date.now().toString().slice(-5);
        await page.locator('.modal-body input.form-control').first().fill(folderName);
        await page.waitForTimeout(400);
        await snap(page, `${name}-05-folder-filled`);
        if (await clickText(page, '.modal-footer button:has-text("ذخیره")', 2500)) log('  ✅ پوشه ساخته شد');
        await page.waitForSelector('.modal-content', { state: 'detached', timeout: 30000 }).catch(() => log('  ⚠ مودال پوشه بسته نشد'));
      }

      // ---- انتخاب پوشهٔ تازه و بازکردن «مدرک جدید» ----
      await clickText(page, `.da-folders >> text=${'پوشهٔ آزمایش '}` , 1500).catch(()=>{});
      const nd = page.locator('button.da-new-document');
      if (await nd.count()) {
        const enabled = await nd.isEnabled();
        log('  دکمهٔ مدرک جدید:', enabled ? 'فعال' : 'غیرفعال');
        if (enabled) {
          await nd.click();
          await page.waitForSelector('.modal-content');
          await page.waitForTimeout(2000);
          await snap(page, `${name}-06-docform`);
          R.docForm = await audit(page, 'فرم مدرک جدید');

          // همهٔ بخش‌های بازشو را باز کن
          const sums = page.locator('.modal-body details:not([open]) > summary');
          const n = await sums.count();
          for (let i = 0; i < n; i++) { try { await sums.nth(0).click({ timeout: 5000 }); await page.waitForTimeout(500); } catch { break; } }
          await page.waitForTimeout(600);
          await snap(page, `${name}-07-docform-expanded`);
          R.docFormExpanded = await audit(page, 'فرم مدرک (بخش‌ها باز)');

          // بدنه را تا آخر اسکرول کن — سربرگ/فوتر باید ثابت بمانند
          await page.evaluate(() => { const b = document.querySelector('.modal-body'); if (b) b.scrollTop = b.scrollHeight; });
          await page.waitForTimeout(700);
          await snap(page, `${name}-08-docform-scrolled`);
          R.docFormScrolled = await audit(page, 'فرم مدرک (اسکرول‌شده)');

          // برچسب‌های فرم
          await page.evaluate(() => { const b = document.querySelector('.modal-body'); if (b) b.scrollTop = 0; });
          if (await clickText(page, '.modal-body button:has-text("برچسب")', 1800)) {
            await snap(page, `${name}-09-docform-tags`);
            R.docFormTags = await audit(page, 'انتخاب برچسب در فرم مدرک');
          }
          // برای رسیدن به مرحلهٔ بعد با حالت پاک، صفحه را تازه می‌کنیم
          await page.goto(CLIENT + '/doc-archive', { waitUntil: 'domcontentloaded' });
          await page.waitForSelector('.da-page-actions', { timeout: 60000 });
          await page.waitForTimeout(1500);
        }
      }

      // ---- مودال برچسب‌ها (رنگ‌های پیش‌فرض + رنگ دلخواه) ----
      if (await clickText(page, '.da-tools-trigger', 800)) {
        if (await clickText(page, '.da-tools-menu button:has-text("برچسب")', 1500)) {
          await page.waitForSelector('.modal-content');
          await snap(page, `${name}-10-tags-modal`);
          R.tagsModal = await audit(page, 'مودال برچسب‌ها');
          // ساختن برچسب با رنگ روشن (زرد) و رنگ میانه (آبی) برای دیدن متن
          for (const [i, color] of ['#fde047', '#0284c7', '#4f46e5'].entries()) {
            const nameInput = page.locator('.modal-body input[type="text"], .modal-body input.form-control:not([type])').first();
            const colorInput = page.locator('.modal-body input[type="color"]').first();
            if (await nameInput.count()) await nameInput.fill('برچسب آزمایش ' + (i + 1));
            if (await colorInput.count()) { await colorInput.evaluate((el, c) => { el.value = c; el.dispatchEvent(new Event('change', { bubbles: true })); el.dispatchEvent(new Event('input', { bubbles: true })); }, color); await page.waitForTimeout(500); }
            if (await clickText(page, '.modal-footer button:has-text("افزودن"), .modal-body button:has-text("افزودن")', 1200)) log('  ✅ برچسب ساخته شد:', color);
          }
          await snap(page, `${name}-11-tags-created`);
          R.tagsCreated = await audit(page, 'مودال برچسب‌ها (با رنگ‌های روشن/میانه)');
          await clickText(page, '.modal-footer button:has-text("بستن"), .modal-footer button:has-text("انصراف")', 1200);
        }
      }
      await snap(page, `${name}-12-final`, { fullPage: true });
      R.final = await audit(page, 'صفحهٔ نهایی');
    } catch (e) {
      log('  ❌ خطا در', name, ':', e.message.split('\n')[0]);
      R.error = e.message.split('\n').slice(0, 3).join(' | ');
      await snap(page, `${name}-99-error`);
    }
    await ctx.close();
  }

  await browser.close();
  fs.writeFileSync(OUT + '/report.json', JSON.stringify(report, null, 2));
  log('=== گزارش نوشته شد:', OUT + '/report.json');
})();
