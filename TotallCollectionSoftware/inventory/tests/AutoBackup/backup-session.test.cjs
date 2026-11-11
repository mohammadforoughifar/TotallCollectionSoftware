const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.resolve(__dirname, '../..');
const js = fs.readFileSync(path.join(root, 'src/Inventory.Client/wwwroot/js/backup-session.js'), 'utf8');

function makeEnv() {
    const calls = [];
    const listeners = {};
    const env = {
        console,
        setInterval: (fn, ms) => { calls.push(['setInterval', ms]); return 123; },
        clearInterval: (id) => calls.push(['clearInterval', id]),
        fetch: (url, opts) => { calls.push(['fetch', url, opts]); return Promise.resolve({ ok: true }); },
        window: {
            addEventListener: (n, f) => { (listeners[n] ||= []).push(f); },
            removeEventListener: (n, f) => { listeners[n] = (listeners[n] || []).filter(x => x !== f); },
            crypto: { randomUUID: () => '00000000-0000-0000-0000-000000000000' }
        },
        document: {
            visibilityState: 'visible',
            addEventListener: (n, f) => { (listeners[n] ||= []).push(f); },
            removeEventListener: (n, f) => { listeners[n] = (listeners[n] || []).filter(x => x !== f); }
        }
    };
    env.window.window = env.window;
    vm.createContext(env);
    vm.runInContext(js, env);
    return { env, calls, listeners };
}

test('backupSession sends heartbeat and registers close listeners', async () => {
    const { env, calls, listeners } = makeEnv();
    const id = env.window.backupSession.start('https://api.example.com/', 'token-1', 'session-1');
    assert.equal(id, 'session-1');
    await new Promise(r => setImmediate(r));
    const heart = calls.find(c => c[0] === 'fetch' && c[1].endsWith('/api/backup/session/heartbeat'));
    assert.ok(heart);
    assert.equal(heart[2].headers.Authorization, 'Bearer token-1');
    assert.equal(JSON.parse(heart[2].body).sessionId, 'session-1');
    assert.ok(listeners.pagehide?.length);
    assert.ok(listeners.beforeunload?.length);
    assert.ok(calls.some(c => c[0] === 'setInterval' && c[1] === 30000));
});

test('pagehide sends keepalive close', async () => {
    const { env, calls, listeners } = makeEnv();
    env.window.backupSession.start('https://api.example.com', 'token-1', 'session-1');
    await new Promise(r => setImmediate(r));
    listeners.pagehide[0]();
    await new Promise(r => setImmediate(r));
    const close = calls.find(c => c[0] === 'fetch' && c[1].endsWith('/api/backup/session/close'));
    assert.ok(close);
    assert.equal(close[2].keepalive, true);
});

test('stop clears timer and removes listeners', async () => {
    const { env, calls, listeners } = makeEnv();
    env.window.backupSession.start('https://api.example.com', 'token-1', 'session-1');
    env.window.backupSession.stop();
    await new Promise(r => setImmediate(r));
    assert.ok(calls.some(c => c[0] === 'clearInterval'));
    assert.equal(listeners.pagehide.length, 0);
    assert.equal(listeners.beforeunload.length, 0);
});
