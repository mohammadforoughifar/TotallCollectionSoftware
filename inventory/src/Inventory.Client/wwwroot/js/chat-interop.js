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

export function scrollToMessage(id) {
    document.getElementById('msg-' + Number(id))?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}

export function dispose(id) {
    const state = instances.get(id);
    if (!state) return;
    document.removeEventListener('visibilitychange', state.visibility);
    window.removeEventListener('focus', state.visibility);
    window.removeEventListener('blur', state.visibility);
    if (state.element) state.element.removeEventListener('keydown', state.keydown);
    instances.delete(id);
}
