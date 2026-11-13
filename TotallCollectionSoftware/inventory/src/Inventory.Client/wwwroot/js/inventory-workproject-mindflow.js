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
