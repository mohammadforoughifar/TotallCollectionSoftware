// نگار ذهنی پروژه — جابه‌جایی گره‌ها، انتخاب با کلیک، و رهاکردن روی گرهٔ مقصد (تغییر والد).
//
// این فایل عمداً «اسکریپت کلاسیک» است (بدون export) تا با
//   <script src="js/inventory-workproject-mindflow.js"></script>
// در index.html مستقیم بالا بیاید. نسخهٔ ماژولی برای import پویا در فایل
// inventory-workproject-mindflow.module.js است.
//
// ⚠️ این فایل باید در index.html لود شود؛ اگر تگ script نباشد و فایل ماژول هم پیدا نشود،
// کامپوننت MindFlow پیام «کشیدن فعال نشد» نشان می‌دهد و هیچ گره‌ای جابه‌جا نمی‌شود.
//
// نکته: خواندن/نوشتن data-* روی عناصر SVG عمداً با getAttribute/setAttribute انجام می‌شود
// (نه dataset)؛ dataset روی SVGElement در بعضی مرورگرها/وب‌ویوهای قدیمی پشتیبانی نمی‌شود
// و باعث می‌شود گره‌ها با مقادیر undefined کشیده شوند.

function attachDrag(svgId, dotnet) {
    const svg = document.getElementById(svgId);
    if (!svg) return false;

    // دفتر کنترلرها در window: اگر SVG با رندر دوبارهٔ Blazor از DOM حذف و با همان id
    // از نو ساخته شود، کنترلر قبلی باید آزاد شود؛ وگرنه شنونده‌های window روی svg قدیمی
    // باقی می‌مانند و انباشته می‌شوند.
    const registry = window.inventoryWorkProjectMindFlowControllers ||
        (window.inventoryWorkProjectMindFlowControllers = {});
    const stale = registry[svgId];
    if (stale && stale.svg !== svg && typeof stale.detach === 'function') stale.detach();

    const existing = svg._wpMindFlowDragController;
    if (existing) {
        existing.dotnet = dotnet;
        return true;
    }

    const controller = { dotnet, svg };
    svg._wpMindFlowDragController = controller;
    svg.setAttribute('data-wp-mind-flow-drag', '1');
    registry[svgId] = controller;

    const attr = (el, name) => el.getAttribute('data-' + name);
    const setAttr = (el, name, value) => el.setAttribute('data-' + name, value);
    const num = (el, name) => Number(attr(el, name));

    let active = null;
    // کلیک بلافاصله پس از یک کشیدن واقعی نباید «انتخاب گره» حساب شود؛
    // وگرنه Blazor پیش از رسیدن CommitDrag با مختصات قدیمی رندر می‌کند و گره می‌پرد.
    let suppressClickUntil = 0;

    const groups = () => [...svg.querySelectorAll('.wpmf-node')];
    const byId = id => groups().find(g => attr(g, 'node-id') === id);
    const eventElement = e => {
        const path = typeof e.composedPath === 'function' ? e.composedPath() : [];
        return path.find(x => x instanceof Element && x.matches?.('.wpmf-node'))
            || e.target?.closest?.('.wpmf-node') || null;
    };
    // ویرایشگر عنوانِ داخل کادر گره: این ناحیه هرگز نباید کشیدن گره را شروع کند،
    // وگرنه preventDefault در pointerdown، تایپ و dblclick را می‌بلعد.
    const FORM_SELECTOR = 'input, textarea, select, button, a, .wpmf-node-title-input, .wpmf-node-action';
    const isFormField = e => {
        const path = typeof e.composedPath === 'function' ? e.composedPath() : [];
        return path.some(x => x instanceof Element && x.matches?.(FORM_SELECTOR))
            || !!e.target?.closest?.(FORM_SELECTOR);
    };
    const pointOf = e => {
        const matrix = svg.getScreenCTM();
        if (!matrix) return null;
        const p = svg.createSVGPoint(); p.x = e.clientX; p.y = e.clientY;
        return p.matrixTransform(matrix.inverse());
    };
    const refreshLinks = () => svg.querySelectorAll('.wpmf-link').forEach(path => {
        const a = byId(attr(path, 'parent-id')), b = byId(attr(path, 'node-id'));
        if (!a || !b) return;
        const ax = num(a, 'x'), ay = num(a, 'y'), bx = num(b, 'x'), by = num(b, 'y');
        const dir = bx >= ax ? 1 : -1;
        const sx = ax + dir * (num(a, 'width') / 2), ex = bx - dir * (num(b, 'width') / 2), mid = (sx + ex) / 2;
        path.setAttribute('d', `M ${sx} ${ay} C ${mid} ${ay}, ${mid} ${by}, ${ex} ${by}`);
    });
    const clearDropTargets = () => groups().forEach(n => n.classList.remove('drop-target'));
    const nodeAt = (x, y) => document.elementFromPoint(x, y)?.closest?.('.wpmf-node') || null;

    // گره‌های در حال کشیدن دقیقاً زیر نشانگر می‌مانند؛ اگر همچنان قابل‌برخورد (hit-test) باشند،
    // elementFromPoint همیشه خودِ گرهٔ کشیده را برمی‌گرداند و «رهاکردن روی گرهٔ مقصد» هرگز
    // تشخیص داده نمی‌شود. پس در طول کشیدن، نشانگر برای آن‌ها شفاف می‌شود.
    const setHitTest = (list, enabled) => list.forEach(n => {
        try { n.el.style.pointerEvents = enabled ? '' : 'none'; } catch { }
    });
    const restore = a => {
        setHitTest(a.originals, true);
        for (const n of a.originals) {
            setAttr(n.el, 'x', n.x); setAttr(n.el, 'y', n.y);
            n.el.setAttribute('transform', `translate(${n.x},${n.y})`);
        }
        refreshLinks();
    };

    // شنونده‌ها روی window (فاز capture) ثبت می‌شوند تا حتی اگر نشانگر از SVG بیرون برود،
    // جابه‌جایی ادامه پیدا کند و pointerup از دست نرود.
    const listeners = [];
    const on = (target, type, handler, opts) => {
        target.addEventListener(type, handler, opts);
        listeners.push([target, type, handler, opts]);
    };

    on(svg, 'pointerdown', e => {
        const g = eventElement(e);
        if (!g || e.button !== 0 || (attr(svg, 'readonly') || '').toLowerCase() === 'true') return;
        if (isFormField(e)) return;
        const start = pointOf(e);
        if (!start) return;

        const rootId = attr(g, 'node-id'), subtree = new Set([rootId]);
        let again = true;
        while (again) {
            again = false;
            for (const n of groups()) {
                if (subtree.has(attr(n, 'parent-id')) && !subtree.has(attr(n, 'node-id'))) {
                    subtree.add(attr(n, 'node-id')); again = true;
                }
            }
        }
        active = {
            rootId, subtree, start, pointerId: e.pointerId, moved: false,
            originals: groups().filter(n => subtree.has(attr(n, 'node-id'))).map(n => ({
                el: n, id: attr(n, 'node-id'), x: num(n, 'x'), y: num(n, 'y')
            }))
        };
        setHitTest(active.originals, false);
        try { svg.setPointerCapture(e.pointerId); } catch { /* رویدادهای window کشیدن را کامل می‌کنند */ }
        e.preventDefault();
        e.stopPropagation();
    }, true);

    // دوبار کلیک روی گره = ویرایش عنوان در همان کادر.
    // pointerdown بوم را preventDefault می‌کند و dblclick مرورگر از بین می‌رود؛
    // پس شمارش کلیک‌ها داخل همان pointerup ای انجام می‌شود که کشیدن را می‌بندد
    // (شنوندهٔ جدا روی svg دیر اجرا می‌شد و drag هم «کلیک» حساب می‌شد).
    let lastClick = { id: null, at: 0 };

    // کلیکِ بعد از کشیدن را ببلع تا نه انتخابِ ناخواسته رخ دهد و نه رندرِ زودهنگام Blazor
    // گره را لحظه‌ای به جای قبلی برگرداند.
    on(window, 'click', e => {
        if (Date.now() > suppressClickUntil) return;
        suppressClickUntil = 0;
        e.preventDefault();
        e.stopPropagation();
    }, true);

    on(window, 'pointermove', e => {
        if (!active || active.pointerId !== e.pointerId) return;
        const p = pointOf(e);
        if (!p) return;
        const dx = p.x - active.start.x, dy = p.y - active.start.y;
        if (Math.abs(dx) + Math.abs(dy) > 4) active.moved = true;
        if (active.moved) {
            for (const n of active.originals) {
                const x = n.x + dx, y = n.y + dy;
                setAttr(n.el, 'x', x); setAttr(n.el, 'y', y);
                n.el.setAttribute('transform', `translate(${x},${y})`);
            }
            refreshLinks();
        }
        clearDropTargets();
        if (!active.moved) return;
        const el = nodeAt(e.clientX, e.clientY);
        const id = el && attr(el, 'node-id');
        if (el && id && !active.subtree.has(id)) el.classList.add('drop-target');
    }, true);

    const end = async e => {
        if (!active || active.pointerId !== e.pointerId) return;
        const a = active;
        const p = pointOf(e);
        const candidate = nodeAt(e.clientX, e.clientY);
        const candidateId = candidate && attr(candidate, 'node-id');
        const targetId = candidate && candidateId && !a.subtree.has(candidateId) ? candidateId : null;
        active = null; clearDropTargets();
        setHitTest(a.originals, true);
        try { svg.releasePointerCapture(e.pointerId); } catch { }

        if (!a.moved || !p) {
            // کلیک ساده = انتخاب گره؛ کلیک دومِ سریع روی همان گره = ویرایش عنوان.
            const now = Date.now();
            const isDouble = lastClick.id === a.rootId && now - lastClick.at < 400;
            lastClick = isDouble ? { id: null, at: 0 } : { id: a.rootId, at: now };

            if (isDouble) {
                try { await controller.dotnet.invokeMethodAsync('BeginNodeTitleEditFromCanvas', a.rootId); }
                catch (err) { console.warn('[MindFlow] ویرایش عنوان از بوم انجام نشد', err); }
                return;
            }

            try { await controller.dotnet.invokeMethodAsync('SelectNodeFromCanvas', a.rootId); }
            catch (err) { console.warn('[MindFlow] انتخاب گره از بوم انجام نشد', err); }
            return;
        }
        // کشیدن واقعی بود: شمارندهٔ دوبار کلیک را پاک کن تا رهاکردن، کلیک حساب نشود.
        lastClick = { id: null, at: 0 };
        suppressClickUntil = Date.now() + 400;

        const dx = p.x - a.start.x, dy = p.y - a.start.y;
        try {
            await controller.dotnet.invokeMethodAsync('CommitDrag', a.rootId,
                a.originals.map(n => ({ id: n.id, x: n.x + dx, y: n.y + dy })), targetId);
        } catch (err) {
            // برگرداندن گره‌ها به جای اول (سرور/اینتراپ نپذیرفت)
            restore(a);
            console.warn('[MindFlow] ثبت جابه‌جایی گره انجام نشد', err);
        }
    };
    const cancel = e => {
        if (!active || active.pointerId !== e.pointerId) return;
        const a = active; active = null; clearDropTargets();
        restore(a);
        try { svg.releasePointerCapture(e.pointerId); } catch { }
    };

    on(window, 'pointerup', end, true);
    on(window, 'pointercancel', cancel, true);
    svg.addEventListener('lostpointercapture', cancel, true);

    controller.detach = () => {
        if (active) { const a = active; active = null; clearDropTargets(); restore(a); }
        for (const [target, type, handler, opts] of listeners) {
            try { target.removeEventListener(type, handler, opts); } catch { }
        }
        listeners.length = 0;
        svg.removeEventListener('lostpointercapture', cancel, true);
        delete svg._wpMindFlowDragController;
        svg.removeAttribute('data-wp-mind-flow-drag');
        if (registry[svgId] === controller) delete registry[svgId];
    };
    return true;
}

window.inventoryWorkProjectMindFlow = window.inventoryWorkProjectMindFlow || {};
window.inventoryWorkProjectMindFlow.attachDrag = attachDrag;
window.inventoryWorkProjectMindFlow.detachDrag = function (svgId) {
    // اول دفتر کنترلرها؛ چون svg ممکن است با رندر دوبارهٔ Blazor از DOM حذف شده باشد
    // و getElementById دیگر همان عنصرِ دارای کنترلر را برنگرداند.
    const registry = window.inventoryWorkProjectMindFlowControllers || {};
    const controller = registry[svgId] || document.getElementById(svgId)?._wpMindFlowDragController;
    if (controller?.detach) controller.detach();
};

/**
 * فوکوس و انتخاب‌کردن کامل عنوانِ گره‌ای که در حال ویرایش است.
 * Blazor پس از ساخت input آن را در رندر بعدی می‌آورد؛ این تابع باید در OnAfterRender صدا زده شود.
 */
window.inventoryWorkProjectMindFlow.focusNodeTitle = function (svgId) {
    const svg = document.getElementById(svgId);
    const input = svg?.querySelector('[data-wpmf-title-input]');
    if (!input) return false;
    input.focus({ preventScroll: true });
    try { input.select(); } catch { }
    return true;
};

// ============================================================================
//  اسکرول بوم — نگه‌داشتن «مرکز دید» هنگام بزرگ‌نمایی
//
//  بزرگ‌نمایی نقشه با بزرگ‌کردن خودِ بوم انجام می‌شود (نه با برش viewBox)،
//  پس سطح قابل اسکرول هم بزرگ می‌شود و کاربر می‌تواند در هر دو جهت حرکت کند.
//  برای اینکه با هر پلهٔ بزرگ‌نمایی نقشه به گوشه نپرد، نقطهٔ مرکز دید قبل و بعد
//  از تغییر بزرگ‌نمایی محاسبه و حفظ می‌شود.
// ============================================================================
function resolveElement(ref) {
    if (!ref) return null;
    if (typeof ref === 'string') return document.getElementById(ref);
    if (ref.nodeType === 1) return ref;
    return null;
}

window.inventoryWorkProjectMindFlow.getCanvasView = function (wrap) {
    const el = resolveElement(wrap);
    if (!el) return null;
    return { left: el.scrollLeft, top: el.scrollTop, w: el.clientWidth, h: el.clientHeight };
};

window.inventoryWorkProjectMindFlow.setCanvasView = function (wrap, left, top) {
    const el = resolveElement(wrap);
    if (!el) return false;
    el.scrollLeft = left;
    el.scrollTop = top;
    return true;
};

/**
 * پیمایش بوم با کلیدهای جهت — در خودِ DOM انجام می‌شود (بدون رفت‌وبرگشت به سرور)
 * تا حرکت نرم و بدون تأخیر باشد. جلوی اسکرول پیش‌فرض مرورگر گرفته می‌شود تا
 * نقشه دو برابر جابه‌جا نشود. روی فیلدهای ورودی (مثل عنوانِ در حال ویرایشِ گره) اثری ندارد.
 */
window.inventoryWorkProjectMindFlow.bindCanvasKeys = function (wrap) {
    const el = resolveElement(wrap);
    if (!el) return false;
    if (el._wpmfCanvasKeys) return true;
    el._wpmfCanvasKeys = true;
    el.addEventListener('keydown', e => {
        if (e.ctrlKey || e.metaKey || e.altKey) return;
        const t = e.target;
        if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.tagName === 'SELECT' || t.isContentEditable)) return;
        const step = e.shiftKey ? 260 : 80;
        let dx = 0, dy = 0;
        switch (e.key) {
            case 'ArrowLeft': dx = -step; break;
            case 'ArrowRight': dx = step; break;
            case 'ArrowUp': dy = -step; break;
            case 'ArrowDown': dy = step; break;
            default: return;
        }
        e.preventDefault();
        el.scrollLeft += dx;
        el.scrollTop += dy;
    });
    return true;
};

// ============================================================================
//  چاپ نقشه
//
//  برگهٔ چاپ در یک iframe جدا ساخته می‌شود تا چاپ، مستقل از چیدمان صفحهٔ برنامه
//  (منو، نوار بالا، پنل تنظیمات) و دقیقاً در اندازهٔ A4 افقی باشد.
//
//  نکتهٔ مهم: برچسب گره‌ها روی صفحه با foreignObject (HTML داخل SVG) نوشته می‌شود.
//  foreignObject در بعضی مرورگرها موقع چاپ ناپایدار است؛ برای چاپ، همان متن‌ها
//  به <text> واقعی SVG تبدیل می‌شوند تا خروجی چاپی همیشه درست و تیز باشد.
// ============================================================================
const SVG_NS = 'http://www.w3.org/2000/svg';

function escapeHtml(value) {
    return String(value == null ? '' : value).replace(/[&<>"']/g, c => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[c]));
}

/** شکستن متن روی چند خط، بر پایهٔ عرض گره (بدون اندازه‌گیری در DOM). */
function wrapLabel(text, widthPx, fontSize) {
    const words = String(text || '').trim().split(/\s+/).filter(Boolean);
    if (!words.length) return [];
    const perLine = Math.max(6, Math.floor((widthPx - 16) / (fontSize * 0.52)));
    const lines = [];
    let line = '';
    for (const word of words) {
        if (!line) { line = word; continue; }
        if ((line + ' ' + word).length <= perLine) line += ' ' + word;
        else { lines.push(line); line = word; }
    }
    if (line) lines.push(line);
    // حداکثر سه خط؛ بقیه با «…» کوتاه می‌شود تا از کادر بیرون نزند.
    if (lines.length > 3) {
        const kept = lines.slice(0, 3);
        kept[2] = kept[2].replace(/.$/, '') + '…';
        return kept;
    }
    return lines;
}

/** نسخهٔ «چاپی» نقشه: برچسب‌ها به <text> تبدیل و دکمه‌های تعاملی حذف می‌شوند. */
function buildPrintSvg(srcSvg) {
    const clone = srcSvg.cloneNode(true);
    clone.removeAttribute('id');
    clone.removeAttribute('data-readonly');
    clone.removeAttribute('style');           // اندازه را برگهٔ چاپ تعیین می‌کند
    clone.removeAttribute('width');
    clone.removeAttribute('height');
    clone.setAttribute('preserveAspectRatio', 'xMidYMid meet');

    // نسبت ابعاد از viewBox گرفته می‌شود تا با height:auto برگهٔ چاپ درست مقیاس بگیرد.
    const vb = (clone.getAttribute('viewBox') || '0 0 1600 900').split(/\s+/).map(Number);
    if (vb.length === 4 && vb[2] > 0 && vb[3] > 0) {
        clone.style.aspectRatio = `${vb[2]} / ${vb[3]}`;
    }

    clone.querySelectorAll('.wpmf-node').forEach(group => {
        const rect = group.querySelector('.wpmf-node-bg');
        const boxW = Number(rect?.getAttribute('width') || 180);
        const boxH = Number(rect?.getAttribute('height') || 70);
        const labelHost = group.querySelector('.wpmf-node-label');
        const text = (labelHost?.textContent || '').trim();
        // گره ریشه پس‌زمینهٔ رنگی دارد (بقیه سفید) — متنش هم باید سفید بماند.
        const isRoot = !!rect && rect.getAttribute('fill') !== '#fff';

        // حلقهٔ پیشرفت و برچسب درصد (که HTML است) را نگه می‌داریم و به SVG تبدیل می‌کنیم.
        const ringValue = group.querySelector('.wpmf-ring-value');
        const ringText = group.querySelector('.wpmf-ring-label')?.textContent?.trim() || '';

        group.querySelectorAll('foreignObject').forEach(fo => fo.remove());

        const fontSize = isRoot ? 20 : 18;
        const lines = wrapLabel(text, boxW, fontSize);
        const lineHeight = fontSize * 1.3;
        const startY = lines.length > 1 ? -((lines.length - 1) * lineHeight) / 2 : 0;
        const label = document.createElementNS(SVG_NS, 'text');
        label.setAttribute('class', 'wpmf-print-node-label' + (isRoot ? ' root' : ''));
        label.setAttribute('x', '0');
        label.setAttribute('y', String(startY));
        label.setAttribute('text-anchor', 'middle');
        label.setAttribute('dominant-baseline', 'central');
        // متن کامل روی خودِ عنصر می‌ماند تا در کپی/خواندن، شکستِ خطوط گم نشود.
        label.setAttribute('aria-label', text);
        lines.forEach((line, i) => {
            const span = document.createElementNS(SVG_NS, 'tspan');
            span.setAttribute('x', '0');
            span.setAttribute('y', String(startY + i * lineHeight));
            span.textContent = line;
            label.appendChild(span);
        });
        group.appendChild(label);

        if (ringValue && ringText) {
            const ring = document.createElementNS(SVG_NS, 'text');
            ring.setAttribute('class', 'wpmf-print-ring-label');
            ring.setAttribute('x', ringValue.getAttribute('cx') || '0');
            ring.setAttribute('y', ringValue.getAttribute('cy') || '0');
            ring.setAttribute('text-anchor', 'middle');
            ring.setAttribute('dominant-baseline', 'central');
            ring.textContent = ringText;
            group.appendChild(ring);
        }
        void boxH;
    });

    return clone;
}

/** کپیِ چاپی نقشه را در ظرف پیش‌نمایش می‌گذارد. */
window.inventoryWorkProjectMindFlow.mountPrintPreview = function (srcSvgId, targetId) {
    const src = document.getElementById(srcSvgId);
    const target = document.getElementById(targetId);
    if (!src || !target) return false;
    target.innerHTML = '';
    target.appendChild(buildPrintSvg(src));
    return true;
};

window.inventoryWorkProjectMindFlow.focusPrintOverlay = function (overlayId) {
    const el = document.getElementById(overlayId);
    if (!el) return false;
    el.focus({ preventScroll: true });
    return true;
};

/** ساخت برگهٔ چاپ در iframe جدا و باز کردن پنجرهٔ چاپ مرورگر. */
window.inventoryWorkProjectMindFlow.printPreview = function (targetId, title, subtitle, meta, srcSvgId) {
    const host = document.getElementById(targetId);
    let svg = host && host.querySelector('svg');
    // اگر پیش‌نمایش هنوز ساخته نشده بود، همین‌جا نسخهٔ چاپی را می‌سازیم.
    if (!svg && srcSvgId) {
        window.inventoryWorkProjectMindFlow.mountPrintPreview(srcSvgId, targetId);
        svg = host && host.querySelector('svg');
    }
    if (!svg) return false;

    const frame = document.createElement('iframe');
    frame.className = 'wpmf-print-frame';
    frame.setAttribute('aria-hidden', 'true');
    frame.setAttribute('title', 'wpmf-print-frame');
    document.body.appendChild(frame);

    const doc = frame.contentDocument || frame.contentWindow.document;
    const html = `<!doctype html>
<html lang="fa" dir="rtl"><head><meta charset="utf-8">
<base href="/">
<title>${escapeHtml(title)}</title>
<link rel="stylesheet" href="css/mindflow-print.css">
<link rel="stylesheet" href="Inventory.Client.styles.css">
<style>@page{size:A4 landscape;margin:8mm}html,body{margin:0;padding:0;background:#fff}</style>
</head><body class="wpmf-print-body">
<div class="wpmf-print-sheet">
<header class="wpmf-print-sheet-head">
<div class="wpmf-print-sheet-title"><strong>${escapeHtml(title)}</strong><span>${escapeHtml(subtitle)}</span></div>
<div class="wpmf-print-sheet-meta">${escapeHtml(meta)}</div>
</header>
<div class="wpmf-print-sheet-canvas">${svg.outerHTML}</div>
<footer class="wpmf-print-sheet-foot">نرم‌افزار یکپارچه فروغ آریا — نگار ذهنی</footer>
</div>
</body></html>`;

    const win = frame.contentWindow;
    let printed = false;
    const doPrint = () => {
        if (printed) return;
        printed = true;
        // صبر تا آماده‌شدن فونت‌ها؛ وگرنه متن با فونت جایگزین چاپ می‌شود.
        const fonts = doc.fonts && doc.fonts.ready ? doc.fonts.ready : Promise.resolve();
        Promise.resolve(fonts).then(() => {
            try { win.focus(); win.print(); }
            catch (err) { console.warn('[MindFlow] پنجرهٔ چاپ باز نشد', err); }
        }).finally(() => setTimeout(() => frame.remove(), 1500));
    };

    frame.addEventListener('load', () => setTimeout(doPrint, 80), { once: true });
    doc.open();
    doc.write(html);
    doc.close();
    // اگر رویداد load نرسید، بعد از کمی صبر خودمان چاپ می‌کنیم.
    setTimeout(doPrint, 1500);
    return true;
};
