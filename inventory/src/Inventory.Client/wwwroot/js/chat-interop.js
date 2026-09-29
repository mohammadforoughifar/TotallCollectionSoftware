// Native keyboard handling is needed to prevent only Enter (not all typing).
// Chat content is never inserted with innerHTML/eval.
const instances = new Map();
let sequence = 0;

export function initialize(dotnet) {
    const id = ++sequence;
    const state = { dotnet, element: null, keydown: null };
    state.visibility = () => dotnet.invokeMethodAsync('ChatVisibilityChanged',
        !document.hidden && document.hasFocus()).catch(() => {});
    document.addEventListener('visibilitychange', state.visibility);
    window.addEventListener('focus', state.visibility);
    window.addEventListener('blur', state.visibility);
    instances.set(id, state);
    state.visibility();
    return id;
}

export function attachComposer(id, element) {
    const state = instances.get(id);
    if (!state || !element || state.element === element) return;
    if (state.element) state.element.removeEventListener('keydown', state.keydown);
    state.element = element;
    state.keydown = event => {
        if (event.key === 'Enter' && !event.shiftKey && !event.isComposing && !event.repeat) {
            event.preventDefault();
            state.dotnet.invokeMethodAsync('SendFromComposer', element.value).catch(() => {});
        }
    };
    element.addEventListener('keydown', state.keydown);
}

export function scrollToBottom() {
    const element = document.getElementById('chatMessagesScroll');
    if (element) element.scrollTop = element.scrollHeight;
}

// =====================================================================
// ضبط پیام صوتی (MediaRecorder) — خروجی برای آپلود به endpoint پیوست چت
// =====================================================================
let recStream = null;
let recRecorder = null;
let recChunks = [];
let recStartedAt = 0;

function recPickMime() {
    if (!window.MediaRecorder) return '';
    const candidates = ['audio/webm;codecs=opus', 'audio/webm', 'audio/mp4', 'audio/ogg;codecs=opus', 'audio/ogg'];
    for (const mime of candidates) {
        try { if (MediaRecorder.isTypeSupported(mime)) return mime; } catch (e) { /* ادامه */ }
    }
    return '';
}

function recRelease() {
    try { if (recStream) recStream.getTracks().forEach(t => t.stop()); } catch (e) { /* نادیده */ }
    recStream = null;
    recRecorder = null;
    recChunks = [];
    recStartedAt = 0;
}

export function isRecordingSupported() {
    return !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia && window.MediaRecorder);
}

export async function startRecording() {
    if (recRecorder && recRecorder.state === 'recording') return true;
    if (!isRecordingSupported()) return false;
    try {
        recRelease();
        recStream = await navigator.mediaDevices.getUserMedia({ audio: true });
        const mime = recPickMime();
        recRecorder = mime ? new MediaRecorder(recStream, { mimeType: mime }) : new MediaRecorder(recStream);
        recChunks = [];
        recRecorder.ondataavailable = e => { if (e.data && e.data.size) recChunks.push(e.data); };
        recRecorder.start(250);
        recStartedAt = Date.now();
        return true;
    } catch (err) {
        recRelease();
        return false;
    }
}

export function cancelRecording() {
    try { if (recRecorder && recRecorder.state !== 'inactive') recRecorder.stop(); } catch (e) { /* نادیده */ }
    recRelease();
}

/** پایان ضبط → Promise با base64 یا null (خیلی کوتاه/خطا). */
export function stopRecording() {
    return new Promise(resolve => {
        if (!recRecorder || recRecorder.state === 'inactive') { recRelease(); resolve(null); return; }
        const rec = recRecorder;
        const durationMs = Math.max(0, Date.now() - recStartedAt);
        rec.onstop = async () => {
            try {
                const type = (rec.mimeType || 'audio/webm').split(';')[0];
                const blob = new Blob(recChunks, { type: type });
                recRelease();
                if (!blob.size || durationMs < 500) { resolve(null); return; }
                const bytes = new Uint8Array(await blob.arrayBuffer());
                let binary = '';
                for (let i = 0; i < bytes.length; i += 0x8000) {
                    binary += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
                }
                const ext = type.indexOf('mp4') >= 0 ? 'm4a' : (type.indexOf('ogg') >= 0 ? 'ogg' : 'webm');
                resolve({ base64: btoa(binary), ext: ext, contentType: type, sizeBytes: blob.size, durationMs: durationMs });
            } catch (err) {
                recRelease();
                resolve(null);
            }
        };
        try { rec.stop(); } catch (e) { recRelease(); resolve(null); }
    });
}

export function scrollToMessage(id) {
    document.getElementById('msg-' + Number(id))?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}

const mobileQuery = '(max-width: 767.98px)';

export function isMobileView() {
    return window.matchMedia(mobileQuery).matches;
}

export function watchMobile(id) {
    const state = instances.get(id);
    if (!state || state.mobileWatch) return;
    const mq = window.matchMedia(mobileQuery);
    state.mobileWatch = event => {
        state.dotnet.invokeMethodAsync('MobileViewChanged', !!event.matches).catch(() => {});
    };
    if (mq.addEventListener) mq.addEventListener('change', state.mobileWatch);
    else mq.addListener(state.mobileWatch);
    state.mobileQuery = mq;
}

export function dispose(id) {
    const state = instances.get(id);
    if (!state) return;
    // آزادسازی میکروفون اگر صفحهٔ چت هنگام ضبط بسته شود
    cancelRecording();
    document.removeEventListener('visibilitychange', state.visibility);
    window.removeEventListener('focus', state.visibility);
    window.removeEventListener('blur', state.visibility);
    if (state.mobileWatch && state.mobileQuery) {
        if (state.mobileQuery.removeEventListener) state.mobileQuery.removeEventListener('change', state.mobileWatch);
        else state.mobileQuery.removeListener(state.mobileWatch);
    }
    if (state.element) state.element.removeEventListener('keydown', state.keydown);
    instances.delete(id);
}
