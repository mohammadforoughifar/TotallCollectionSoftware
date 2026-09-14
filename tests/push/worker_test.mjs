import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const source = fs.readFileSync('inventory/src/Inventory.Client/wwwroot/push-worker.js', 'utf8');
const handlers = {}, store = new Map(), notices = [], navigations = [], opened = [];
let windows = [];
const registration = {
    getNotifications: async () => notices.filter(n => !n.closed),
    showNotification: async (title, options) => {
        const prev = notices.find(n => n.tag === options.tag); if (prev) prev.closed = true;
        notices.push({ title, ...options, closed: false, close() { this.closed = true; } });
    }
};
const self = { location: new URL('https://app.example.test/'), registration,
    addEventListener: (name, fn) => { handlers[name] = fn; },
    clients: { matchAll: async () => windows, openWindow: async url => opened.push(url) } };
vm.runInNewContext(source, { self, URL, Response, Date, Number, caches: {
    open: async () => ({ match: async key => store.get(key)?.clone(), put: async (key, response) => store.set(key, response.clone()) })
} });
async function dispatch(type, event) { let done; handlers[type]({ ...event, waitUntil: promise => { done = promise; } }); await done; }
async function owner(userId) { let ack; await dispatch('message', { data: { type: 'push-owner', userId }, source: { url: 'https://app.example.test/' }, ports: [{ postMessage: x => { ack = x; } }] }); assert.equal(ack.ok, true); }
const payload = { title: 'پیام جدید', body: 'بدنه', userId: 1, tag: 'inv-unique', link: '/workorders/42', expiresAtUtc: new Date(Date.now() + 60000).toISOString() };
await dispatch('push', { data: { json: () => payload } }); assert.equal(notices.length, 0, 'No owner means no private notification');
await owner(1);
await dispatch('push', { data: { json: () => payload } }); assert.equal(notices.length, 1); assert.equal(notices[0].data.url, 'https://app.example.test/workorders/42'); assert.equal(notices[0].dir, 'rtl');
await dispatch('push', { data: { json: () => payload } }); assert.equal(notices.filter(n => !n.closed).length, 1, 'Retry replaces stable tag');
await dispatch('push', { data: { json: () => ({ ...payload, userId: 2 }) } }); assert.equal(notices.length, 2);
await dispatch('push', { data: { json: () => ({ ...payload, expiresAtUtc: '2020-01-01T00:00:00Z' }) } }); assert.equal(notices.length, 2);
await dispatch('push', { data: { json: () => { throw new Error('bad JSON'); } } }); assert.equal(notices.length, 2);
for (const bad of ['https://evil.test/x', '//evil.test/x', 'javascript:alert(1)', 'https://user:pass@app.example.test/x', '/\\evil.test/x']) {
    await dispatch('push', { data: { json: () => ({ ...payload, link: bad }) } }); assert.equal(notices.at(-1).data.url, 'https://app.example.test/');
}
await dispatch('push', { data: { json: () => payload } });
await dispatch('notificationclick', { notification: notices.at(-1) }); assert.equal(opened.at(-1), 'https://app.example.test/workorders/42');
windows = [{ url: 'https://app.example.test/', navigate: async url => { navigations.push(url); return { focus: async () => navigations.push('focus') }; } }];
await dispatch('notificationclick', { notification: notices.at(-1) }); assert.deepEqual(navigations, ['https://app.example.test/workorders/42', 'focus']);
await owner(0); assert.equal(notices.filter(n => !n.closed).length, 0);
const before = notices.length; await dispatch('push', { data: { json: () => payload } }); assert.equal(notices.length, before);
await dispatch('notificationclick', { notification: notices.at(-1) }); assert.equal(opened.length, 1);
await owner(2); await dispatch('push', { data: { json: () => payload } }); assert.equal(notices.length, before);
console.log('PASS: worker ownership/logout/account switching, expiry, malformed payload, stable tags, safe links, closed-app open and focus/navigation');
// Published worker: versioned static JS stays available offline, but API/token URLs bypass cache.
const matched = [], fetched = [], publishedHandlers = {};
const prodSelf = { location: new URL('https://app.example.test/'), origin: 'https://app.example.test',
    assetsManifest: { version: 'test', assets: [{ url: 'js/push-notifications.js' }, { url: 'index.html' }] },
    importScripts: () => {}, addEventListener: (name, fn) => { publishedHandlers[name] = fn; } };
vm.runInNewContext(fs.readFileSync('inventory/src/Inventory.Client/wwwroot/service-worker.published.js', 'utf8'), {
    self: prodSelf, URL, Request, console,
    caches: { open: async () => ({ match: async req => { matched.push(req); return new Response('cached'); } }) },
    fetch: async req => { fetched.push(req.url); return new Response('network'); }
});
async function request(url, mode='cors') {
    let result; publishedHandlers.fetch({ request: { url, mode, method: 'GET' }, respondWith: promise => { result = promise; } });
    return await (await result).text();
}
assert.equal(await request('https://app.example.test/js/push-notifications.js?v=1'), 'cached');
assert.equal(matched.at(-1), 'https://app.example.test/js/push-notifications.js');
assert.equal(await request('https://app.example.test/api/attachments/download/1','navigate'), 'network');
assert.equal(await request('https://app.example.test/anything?access_token=secret','navigate'), 'network');
assert.equal(fetched.length, 2);
console.log('PASS: published offline asset query handling; downloads/API/token cache bypass preserved');
