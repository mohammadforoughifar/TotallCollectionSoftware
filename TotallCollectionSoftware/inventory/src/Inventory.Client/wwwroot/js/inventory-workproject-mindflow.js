// نگار ذهنی پروژه — جابه‌جایی گره‌ها، انتخاب با کلیک، و رهاکردن روی گرهٔ مقصد (تغییر والد).
//
// این فایل عمداً «اسکریپت کلاسیک» است (بدون export) تا با
//   <script src="js/inventory-workproject-mindflow.js"></script>
// در index.html مستقیم بالا بیاید. نسخهٔ ماژولی برای import پویا در فایل
// inventory-workproject-mindflow.module.js است.
//
// نکته: خواندن/نوشتن data-* روی عناصر SVG عمداً با getAttribute/setAttribute انجام می‌شود
// (نه dataset)؛ dataset روی SVGElement در بعضی مرورگرها/وب‌ویوهای قدیمی پشتیبانی نمی‌شود
// و باعث می‌شود گره‌ها با مقادیر undefined کشیده شوند.

function attachDrag(svgId, dotnet) {
    // مبدأ واحد در window تا نمونه‌های تکراری (اسکریپت + import) یک کنترلر ببینند.
    window.inventoryWorkProjectMindFlowAttached =
        window.inventoryWorkProjectMindFlowAttached || { attachDrag };

    const svg = document.getElementById(svgId);
    if (!svg) return false;

    const existing = svg._wpMindFlowDragController;
    if (existing) {
        existing.dotnet = dotnet;
        return true;
    }

    const controller = { dotnet };
    svg._wpMindFlowDragController = controller;
    svg.setAttribute('data-wp-mind-flow-drag', '1');

    const attr = (el, name) => el.getAttribute('data-' + name);
    const setAttr = (el, name, value) => el.setAttribute('data-' + name, value);
    const num = (el, name) => Number(attr(el, name));

    let active = null;
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
        try { svg.setPointerCapture(e.pointerId); } catch { /* رویدادهای window کشیدن را کامل می‌کنند */ }
        e.preventDefault();
        e.stopPropagation();
    }, true);

    // دوبار کلیک روی گره = ویرایش عنوان در همان کادر.
    // pointerdown بوم را preventDefault می‌کند و dblclick مرورگر از بین می‌رود؛
    // پس شمارش کلیک‌ها داخل همان pointerup ای انجام می‌شود که کشیدن را می‌بندد
    // (شنوندهٔ جدا روی svg دیر اجرا می‌شد و drag هم «کلیک» حساب می‌شد).
    let lastClick = { id: null, at: 0 };

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

        const dx = p.x - a.start.x, dy = p.y - a.start.y;
        try {
            await controller.dotnet.invokeMethodAsync('CommitDrag', a.rootId,
                a.originals.map(n => ({ id: n.id, x: n.x + dx, y: n.y + dy })), targetId);
        } catch (err) {
            // برگرداندن گره‌ها به جای اول (سرور/اینتراپ نپذیرفت)
            for (const n of a.originals) {
                setAttr(n.el, 'x', n.x); setAttr(n.el, 'y', n.y);
                n.el.setAttribute('transform', `translate(${n.x},${n.y})`);
            }
            refreshLinks();
            console.warn('[MindFlow] ثبت جابه‌جایی گره انجام نشد', err);
        }
    };
    const cancel = e => {
        if (!active || active.pointerId !== e.pointerId) return;
        const a = active; active = null; clearDropTargets();
        for (const n of a.originals) {
            setAttr(n.el, 'x', n.x); setAttr(n.el, 'y', n.y);
            n.el.setAttribute('transform', `translate(${n.x},${n.y})`);
        }
        refreshLinks();
        try { svg.releasePointerCapture(e.pointerId); } catch { }
    };

    on(window, 'pointerup', end, true);
    on(window, 'pointercancel', cancel, true);
    svg.addEventListener('lostpointercapture', cancel, true);

    controller.detach = () => {
        for (const [target, type, handler, opts] of listeners) {
            try { target.removeEventListener(type, handler, opts); } catch { }
        }
        listeners.length = 0;
        svg.removeEventListener('lostpointercapture', cancel, true);
        delete svg._wpMindFlowDragController;
        svg.removeAttribute('data-wp-mind-flow-drag');
    };
    return true;
}

window.inventoryWorkProjectMindFlow = window.inventoryWorkProjectMindFlow || {};
window.inventoryWorkProjectMindFlow.attachDrag = attachDrag;
window.inventoryWorkProjectMindFlow.detachDrag = function (svgId) {
    const svg = document.getElementById(svgId);
    const c = svg?._wpMindFlowDragController;
    if (c?.detach) c.detach();
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
