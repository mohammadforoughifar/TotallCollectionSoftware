/* هارنس تطابق: فایل statutory-rules.js اصلی را در یک محیط شبیه‌سازی‌شدهٔ مرورگر اجرا می‌کند
   و خروجی calculatePayroll را برای مجموعه‌ای از سناریوها تولید می‌کند. */
const fs = require("fs");
const path = require("path");
const vm = require("vm");

const SRC = "/home/user/hrapp/RADIS-HR-V019-Source-Code/assets/statutory-rules.js";
const code = fs.readFileSync(SRC, "utf8");

// --- شبیه‌سازی حداقلی DOM و localStorage ---
const store = {};
const localStorage = {
  getItem: k => (k in store ? store[k] : null),
  setItem: (k, v) => { store[k] = String(v); },
  removeItem: k => { delete store[k]; }
};
const noopEl = new Proxy(function () {}, {
  get: (t, p) => {
    if (p === "value" || p === "textContent" || p === "innerHTML" || p === "className") return "";
    if (p === "classList") return { add() {}, remove() {}, toggle() {}, contains() { return false; } };
    if (p === "dataset") return {};
    if (p === "checked") return false;
    if (p === "style") return {};
    return noopEl;
  },
  set: () => true,
  apply: () => noopEl
});
const document = {
  querySelector: () => noopEl,
  querySelectorAll: () => [],
  createElement: () => noopEl,
  addEventListener: () => {},
  body: noopEl,
  documentElement: noopEl
};
const windowObj = {
  localStorage,
  document,
  addEventListener: () => {},
  dispatchEvent: () => {},
  crypto: { getRandomValues: a => a, subtle: {} },
  Intl,
  employees: []
};
windowObj.window = windowObj;

const ctx = vm.createContext(Object.assign(windowObj, {
  console, localStorage, document,
  CustomEvent: class { constructor(t, o) { this.type = t; Object.assign(this, o); } },
  TextEncoder, setTimeout, clearTimeout, Date, Math, JSON, Number, String, Array, Object
}));

try { vm.runInContext(code, ctx); }
catch (e) { console.error("خطا در اجرای فایل قواعد:", e.message); process.exit(1); }

const R = ctx.window.RADIS_RULES;
if (!R) { console.error("RADIS_RULES تعریف نشد"); process.exit(1); }

// --- سناریوهای آزمون: ۱۰ پرسنل نمونه × چند ماه × حالت‌های اضافه‌کار/کسرکار ---
const sampleEmployees = [
  { first: "میثم", last: "تاباق", code: "101", married: "متاهل", children: 2, salary: 450000000, hire: "1401/10/01" },
  { first: "محمد رضا", last: "سلطانی", code: "102", married: "متاهل", children: 1, salary: 400000000, hire: "1401/09/10" },
  { first: "محمد", last: "قیصری", code: "103", married: "مجرد", children: 0, salary: 328759102, hire: "1402/02/05" },
  { first: "مسلم", last: "اسماعیلی", code: "104", married: "متاهل", children: 1, salary: 460000000, hire: "1402/02/06" },
  { first: "عمران", last: "مقدسی", code: "105", married: "متاهل", children: 3, salary: 320000000, hire: "1403/05/25" },
  { first: "محمد", last: "بیرقی", code: "106", married: "مجرد", children: 0, salary: 345000000, hire: "1402/03/18" },
  { first: "محمدرضا", last: "حبیب خانی", code: "107", married: "متاهل", children: 2, salary: 370000000, hire: "1402/03/19" },
  { first: "شاهین", last: "محمدی", code: "108", married: "مجرد", children: 2, salary: 395000000, hire: "1402/03/20" },
  { first: "حمید رضا", last: "عرب عامری", code: "109", married: "متاهل", children: 3, salary: 420000000, hire: "1402/03/21" },
  { first: "محسن", last: "کمانی", code: "110", married: "متاهل", children: 0, salary: 320000000, hire: "1402/03/22" },
  // موارد مرزی
  { first: "کارمند", last: "تازه", code: "201", married: "مجرد", children: 0, salary: 120000000, hire: "1405/01/01" },
  { first: "کارمند", last: "پردرآمد", code: "202", married: "متاهل", children: 4, salary: 2500000000, hire: "1390/01/01" },
  { first: "کارمند", last: "سالگرد", code: "203", married: "متاهل", children: 1, salary: 300000000, hire: "1404/03/31" },
  { first: "کارمند", last: "بدون تاریخ", code: "204", married: "مجرد", children: 0, salary: 200000000, hire: "" }
];

const cases = [];
const months = [1, 3, 6, 7, 11, 12];
const years = [1404, 1405, 1406, 1407];
const extras = [
  { overtime: 0, shortfallPay: 0, mission: 0 },
  { overtime: 25000000, shortfallPay: 0, mission: 0 },
  { overtime: 0, shortfallPay: 18000000, mission: 0 },
  { overtime: 40000000, shortfallPay: 9000000, mission: 12000000 }
];

for (const e of sampleEmployees) {
  for (const y of years) {
    for (const m of months) {
      for (let i = 0; i < extras.length; i++) {
        const days = R.persianMonthDays(y, m);
        const x = extras[i];
        const input = { year: y, month: m, days, base: Number(e.salary), overtime: x.overtime, shortfallPay: x.shortfallPay, mission: x.mission };
        const out = R.calculatePayroll(e, input);
        cases.push({
          id: `${e.code}-${y}-${m}-${i}`,
          employee: { code: e.code, married: e.married, children: e.children, salary: e.salary, hire: e.hire },
          input,
          expected: {
            days: out.legal.days,
            base: out.base, seniority: out.seniority, housing: out.housing, food: out.food,
            marriage: out.marriage, children: out.children, overtime: out.overtime, mission: out.mission,
            gross: out.gross, insuranceBase: out.insuranceBase, maximumInsuranceBase: out.maximumInsuranceBase,
            insurance: out.insurance, employerInsurance: out.employerInsurance,
            taxableIncome: out.taxableIncome, tax: out.tax, net: out.net
          }
        });
      }
    }
  }
}

// آزمون‌های نرخ ساعتی/اضافه‌کار مطابق attendance-engine.js
const rules = R.getRules(1405);
const otCases = [];
for (const e of sampleEmployees) {
  for (const minutes of [0, 60, 95, 137, 600, 1234]) {
    const hourly = Number(e.salary) / 220;
    otCases.push({
      salary: e.salary, minutes,
      otPay: Math.round(hourly * (1 + rules.overtimePremiumRate / 100) * minutes / 60),
      shortfallPay: Math.round(hourly * minutes / 60)
    });
  }
}

const outPath = path.join(__dirname, "expected.json");
fs.writeFileSync(outPath, JSON.stringify({ generatedBy: "statutory-rules.js (اصل)", payroll: cases, overtime: otCases }, null, 1), "utf8");
console.log(`✔ ${cases.length} سناریوی حقوق و ${otCases.length} سناریوی اضافه‌کار در ${outPath} نوشته شد`);
