// ردیابی نشست مرورگر برای بک‌آپ خودکار «آخرین کاربر فعال».
// این اسکریپت فقط heartbeat/close می‌فرستد؛ خود بک‌آپ سمت سرور گرفته می‌شود.
(function () {
    var state = { timer: null, sessionId: null, apiBase: null, token: null, stopped: false };

    function url(path) {
        var b = (state.apiBase || '').replace(/\/+$/, '');
        return b + path;
    }

    function post(path, keepalive) {
        if (!state.sessionId || !state.token) return Promise.resolve(false);
        try {
            return fetch(url(path), {
                method: 'POST',
                keepalive: !!keepalive,
                cache: 'no-store',
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': 'Bearer ' + state.token
                },
                body: JSON.stringify({ sessionId: state.sessionId })
            }).then(function (r) { return r.ok; }).catch(function () { return false; });
        } catch (e) { return Promise.resolve(false); }
    }

    function onPageHide() { post('/api/backup/session/close', true); }
    function onBeforeUnload() { post('/api/backup/session/close', true); }
    function onVisibility() {
        if (document.visibilityState === 'visible') post('/api/backup/session/heartbeat', false);
    }

    window.backupSession = {
        start: function (apiBase, token, sessionId) {
            window.backupSession.stop();
            state.stopped = false;
            state.apiBase = apiBase || '';
            state.token = token || '';
            state.sessionId = sessionId || (window.crypto && crypto.randomUUID ? crypto.randomUUID().replace(/-/g, '') : String(Date.now()));
            post('/api/backup/session/heartbeat', false);
            state.timer = setInterval(function () { post('/api/backup/session/heartbeat', false); }, 30000);
            window.addEventListener('pagehide', onPageHide);
            window.addEventListener('beforeunload', onBeforeUnload);
            document.addEventListener('visibilitychange', onVisibility);
            return state.sessionId;
        },
        stop: function () {
            state.stopped = true;
            if (state.timer) { clearInterval(state.timer); state.timer = null; }
            window.removeEventListener('pagehide', onPageHide);
            window.removeEventListener('beforeunload', onBeforeUnload);
            document.removeEventListener('visibilitychange', onVisibility);
            if (state.sessionId && state.token) post('/api/backup/session/close', true);
            state.sessionId = null;
            state.token = null;
        },
        heartbeat: function () { return post('/api/backup/session/heartbeat', false); }
    };
})();
