// نمایش‌دهندهٔ ریموت دسکتاپ — ماژول RemoteDesktop
// تصویر JPEG روی canvas + ارسال موس/کیبورد به Blazor از طریق DotNetObjectReference.
window.remoteViewer = (function () {
    const states = new Map();

    function detach(canvasId) {
        const st = states.get(canvasId);
        if (!st) return;
        st.disposed = true;
        try { st.detach(); } catch { }
        states.delete(canvasId);
    }

    function attach(canvasId, dotnetRef, allowControl) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;
        detach(canvasId);
        canvas.tabIndex = 0;
        canvas.style.outline = 'none';
        const st = { dotnetRef, allowControl: !!allowControl, lastMove: 0, pressed: new Set(), disposed: false, detach: null };

        function send(obj) {
            if (st.disposed || !st.allowControl) return;
            try { dotnetRef.invokeMethodAsync('OnRemoteInput', JSON.stringify(obj)); } catch { }
        }
        function norm(e) {
            const r = canvas.getBoundingClientRect();
            const x = r.width > 0 ? (e.clientX - r.left) / r.width : 0;
            const y = r.height > 0 ? (e.clientY - r.top) / r.height : 0;
            return { x: Math.min(1, Math.max(0, x)), y: Math.min(1, Math.max(0, y)) };
        }

        const onMove = e => {
            const now = performance.now();
            if (now - st.lastMove < 33) return; // حدود ۳۰Hz
            st.lastMove = now;
            const p = norm(e);
            send({ type: 'mouse', action: 'move', x: p.x, y: p.y });
        };
        const onDown = e => {
            canvas.focus();
            const p = norm(e);
            send({ type: 'mouse', action: 'down', button: e.button, x: p.x, y: p.y });
            e.preventDefault();
        };
        const onUp = e => {
            const p = norm(e);
            send({ type: 'mouse', action: 'up', button: e.button, x: p.x, y: p.y });
            e.preventDefault();
        };
        const onWheel = e => {
            send({ type: 'mouse', action: 'wheel', deltaY: e.deltaY });
            e.preventDefault();
        };
        const onContext = e => e.preventDefault();
        const onKeyDown = e => {
            if (!st.allowControl) return;
            st.pressed.add(e.code);
            send({ type: 'key', action: 'down', key: e.key, code: e.code, ctrl: e.ctrlKey, alt: e.altKey, shift: e.shiftKey, meta: e.metaKey });
            e.preventDefault();
        };
        const onKeyUp = e => {
            if (!st.allowControl) return;
            st.pressed.delete(e.code);
            send({ type: 'key', action: 'up', key: e.key, code: e.code, ctrl: e.ctrlKey, alt: e.altKey, shift: e.shiftKey, meta: e.metaKey });
            e.preventDefault();
        };
        const onBlur = () => {
            for (const code of st.pressed) send({ type: 'key', action: 'up', code: code });
            st.pressed.clear();
        };

        canvas.addEventListener('mousemove', onMove);
        canvas.addEventListener('mousedown', onDown);
        canvas.addEventListener('mouseup', onUp);
        canvas.addEventListener('wheel', onWheel, { passive: false });
        canvas.addEventListener('contextmenu', onContext);
        canvas.addEventListener('keydown', onKeyDown);
        canvas.addEventListener('keyup', onKeyUp);
        canvas.addEventListener('blur', onBlur);
        st.detach = () => {
            canvas.removeEventListener('mousemove', onMove);
            canvas.removeEventListener('mousedown', onDown);
            canvas.removeEventListener('mouseup', onUp);
            canvas.removeEventListener('wheel', onWheel);
            canvas.removeEventListener('contextmenu', onContext);
            canvas.removeEventListener('keydown', onKeyDown);
            canvas.removeEventListener('keyup', onKeyUp);
            canvas.removeEventListener('blur', onBlur);
        };
        states.set(canvasId, st);
        canvas.focus();
    }

    async function drawFrame(canvasId, bytes, width, height) {
        const canvas = document.getElementById(canvasId);
        if (!canvas || !width || !height) return;
        if (canvas.width !== width) canvas.width = width;
        if (canvas.height !== height) canvas.height = height;
        let bmp = null;
        try {
            const blob = new Blob([bytes], { type: 'image/jpeg' });
            bmp = await createImageBitmap(blob);
            const ctx = canvas.getContext('2d');
            if (ctx) ctx.drawImage(bmp, 0, 0, width, height);
        } catch { }
        finally { if (bmp && bmp.close) bmp.close(); }
    }

    return { attach, detach, drawFrame };
})();
