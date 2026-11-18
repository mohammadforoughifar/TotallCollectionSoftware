const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync('src/Inventory.Client/wwwroot/js/project-interop.js', 'utf8');
const script = source.slice(source.lastIndexOf('(function () {'));
const handlers = {};
const context = { document: { addEventListener: (name, fn) => handlers[name] = fn }, Event: class { constructor(type) { this.type = type; } } };
vm.runInNewContext(script, context);
function field(value, max, iban = false, start = value.length, end = start) {
  const attrs = { 'data-digits': 'digits', 'data-shop-max-digits': String(max), ...(iban ? { 'data-shop-iban': 'true' } : {}) };
  return { tagName: 'INPUT', value, selectionStart: start, selectionEnd: end,
    hasAttribute: k => k in attrs, getAttribute: k => attrs[k] ?? null,
    setRangeText(text, a, b) { this.value = this.value.slice(0, a) + text + this.value.slice(b); this.selectionStart = this.selectionEnd = a + text.length; },
    setSelectionRange(a, b) { this.selectionStart = a; this.selectionEnd = b; },
    dispatchEvent(e) { handlers[e.type]({ target: this }); } };
}
function paste(el, text) { let prevented = false; handlers.paste({ target: el, clipboardData: { getData: () => text }, preventDefault: () => prevented = true }); assert.ok(prevented); }
test('paste Persian digits and separators is normalized and limited', () => {
  const el = field('', 16); paste(el, '۶۰۳۷-۹۹۱۲-۳۴۵۶-۷۸۹۰-۱۱'); assert.equal(el.value, '6037991234567890');
});
test('IBAN prefix and Arabic digits are handled before the limit', () => {
  const el = field('', 24, true); paste(el, 'IR٠١٢٣ ٤٥٦٧٨٩٠١٢٣٤٥٦٧٨٩٠١٢٣٤٥'); assert.equal(el.value, '012345678901234567890123');
});
test('paste replaces selection and respects remaining capacity', () => {
  const el = field('09121234567', 11, false, 3, 6); paste(el, '۹۸۷۶۵'); assert.equal(el.value, '09198734567');
});
test('input converts Persian and preserves zero', () => {
  const el = field('۰۹۱۲', 11); handlers.input({ target: el }); assert.equal(el.value, '0912');
});
