// No npm dependencies: verify the browser API-base helper in the real Inventory index.
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const html = fs.readFileSync(path.join(__dirname, '../../inventory/src/Inventory.Client/wwwroot/index.html'), 'utf8');
const helper = html.match(/window\.inventoryApiBase = function \([^)]*\) \{[\s\S]*?\n        \};/)[0];
function resolve(url, stored) {
    const context = { window: { location: new URL(url) }, URL, localStorage: { getItem: () => stored } };
    vm.runInNewContext(helper, context);
    return context.window.inventoryApiBase();
}
assert.equal(resolve('https://5100-sandbox.e2b.app/hr'), 'https://5100-sandbox.e2b.app');
assert.equal(resolve('https://9000-sandbox.e2b.app/hr', 'http://localhost:5100'), 'https://9000-sandbox.e2b.app');
assert.equal(resolve('https://erp.example.com/hr', 'http://127.0.0.1:5100'), 'https://erp.example.com');
assert.equal(resolve('http://localhost:5100/hr'), 'http://localhost:5100');
assert.equal(resolve('http://localhost:5210/hr'), 'http://localhost:5100');
assert.equal(resolve('http://127.0.0.1:5210/hr'), 'http://127.0.0.1:5100');
assert.equal(resolve('http://localhost:5210/hr', 'http://localhost:6000'), 'http://localhost:6000/');
console.log('API-base helper: 7 checks passed.');
