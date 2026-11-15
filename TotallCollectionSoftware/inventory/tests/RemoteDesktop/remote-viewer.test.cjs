const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const js = fs.readFileSync(path.join(root, 'src/Inventory.Client/wwwroot/js/remote-viewer.js'), 'utf8');

function makeEnv() {
    const listeners = {};
    const canvas = {
        id: 'remote-canvas', width: 0, height: 0, style: {}, tabIndex: 0,
        addEventListener: (n, f) => { (listeners[n] ||= []).push(f); },
        removeEventListener: (n, f) => { listeners[n] = (listeners[n] || []).filter(x => x !== f); },
        getBoundingClientRect: () => ({ left: 10, top: 20, width: 200, height: 100 }),
        getContext: () => ({ drawImage: () => {} }),
        focus: () => {}
    };
    const calls = [];
    const env = {
        window: {},
        document: { getElementById: id => id === 'remote-canvas' ? canvas : null },
        performance: { now: () => 1000 },
        Blob: class { constructor(parts, opts) { this.parts = parts; this.opts = opts; } },
        createImageBitmap: async () => ({ close: () => {} }),
        console
    };
    env.window = env;
    vm.createContext(env);
    vm.runInContext(js, env);
    return { env, canvas, listeners, calls };
}

test('remoteViewer attaches input listeners and sends normalized mouse coordinates', () => {
    const { env, listeners, calls } = makeEnv();
    const ref = { invokeMethodAsync: (m, json) => { calls.push([m, JSON.parse(json)]); return Promise.resolve(); } };
    env.window.remoteViewer.attach('remote-canvas', ref, true);
    assert.ok(listeners.mousemove?.length);
    assert.ok(listeners.keydown?.length);
    listeners.mousemove[0]({ clientX: 110, clientY: 70 });
    const move = calls.find(c => c[0] === 'OnRemoteInput' && c[1].type === 'mouse' && c[1].action === 'move');
    assert.ok(move);
    assert.equal(move[1].x, 0.5);
    assert.equal(move[1].y, 0.5);
});

test('view-only session does not send input', () => {
    const { env, listeners, calls } = makeEnv();
    const ref = { invokeMethodAsync: (m, json) => { calls.push([m, JSON.parse(json)]); return Promise.resolve(); } };
    env.window.remoteViewer.attach('remote-canvas', ref, false);
    listeners.mousemove[0]({ clientX: 110, clientY: 70 });
    listeners.keydown[0]({ code: 'KeyA', key: 'a', preventDefault: () => {} });
    assert.equal(calls.length, 0);
});

test('detach removes listeners', () => {
    const { env, listeners } = makeEnv();
    const ref = { invokeMethodAsync: () => Promise.resolve() };
    env.window.remoteViewer.attach('remote-canvas', ref, true);
    env.window.remoteViewer.detach('remote-canvas');
    assert.equal(listeners.mousemove.length, 0);
    assert.equal(listeners.keydown.length, 0);
});
