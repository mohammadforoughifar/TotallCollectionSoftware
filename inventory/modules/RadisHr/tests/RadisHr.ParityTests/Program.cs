using System.Text.Json;
using RadisHr.Shared.Calculations;
using RadisHr.Shared.Models;

// آزمون تطابق عددی: مقایسهٔ موتور C# با خروجی مرجع تولیدشده از statutory-rules.js اصلی
var jsonPath = Path.Combine(AppContext.BaseDirectory, "../../../../parity/expected.json");
jsonPath = Path.GetFullPath(jsonPath);
if (!File.Exists(jsonPath))
{
    Console.WriteLine($"فایل مرجع پیدا نشد: {jsonPath}");
    return 2;
}

using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
var root = doc.RootElement;
var rules = StatutoryRules.CreateDefault();

int passed = 0, failed = 0;
var failures = new List<string>();

void Check(string id, string field, decimal expected, decimal actual)
{
    if (expected == actual) { passed++; return; }
    failed++;
    if (failures.Count < 25) failures.Add($"{id} :: {field} — انتظار {expected} ولی {actual}");
}

foreach (var c in root.GetProperty("payroll").EnumerateArray())
{
    var id = c.GetProperty("id").GetString()!;
    var emp = c.GetProperty("employee");
    var employee = new Employee
    {
        Code = emp.GetProperty("code").GetString()!,
        Married = emp.GetProperty("married").GetString()!,
        Children = emp.GetProperty("children").GetInt32(),
        Salary = emp.GetProperty("salary").GetDecimal(),
        Hire = emp.GetProperty("hire").GetString()!
    };

    var inp = c.GetProperty("input");
    var input = new PayrollInput
    {
        Year = inp.GetProperty("year").GetInt32(),
        Month = inp.GetProperty("month").GetInt32(),
        Days = inp.GetProperty("days").GetInt32(),
        Base = inp.GetProperty("base").GetDecimal(),
        Overtime = inp.GetProperty("overtime").GetDecimal(),
        ShortfallPay = inp.GetProperty("shortfallPay").GetDecimal(),
        Mission = inp.GetProperty("mission").GetDecimal()
    };

    var r = PayrollEngine.CalculatePayroll(employee, rules, input);
    var e = c.GetProperty("expected");

    Check(id, "days", e.GetProperty("days").GetDecimal(), r.Legal.Days);
    Check(id, "base", e.GetProperty("base").GetDecimal(), r.Base);
    Check(id, "seniority", e.GetProperty("seniority").GetDecimal(), r.Seniority);
    Check(id, "housing", e.GetProperty("housing").GetDecimal(), r.Housing);
    Check(id, "food", e.GetProperty("food").GetDecimal(), r.Food);
    Check(id, "marriage", e.GetProperty("marriage").GetDecimal(), r.Marriage);
    Check(id, "children", e.GetProperty("children").GetDecimal(), r.Children);
    Check(id, "overtime", e.GetProperty("overtime").GetDecimal(), r.Overtime);
    Check(id, "mission", e.GetProperty("mission").GetDecimal(), r.Mission);
    Check(id, "gross", e.GetProperty("gross").GetDecimal(), r.Gross);
    Check(id, "insuranceBase", e.GetProperty("insuranceBase").GetDecimal(), r.InsuranceBase);
    Check(id, "maximumInsuranceBase", e.GetProperty("maximumInsuranceBase").GetDecimal(), r.MaximumInsuranceBase);
    Check(id, "insurance", e.GetProperty("insurance").GetDecimal(), r.Insurance);
    Check(id, "employerInsurance", e.GetProperty("employerInsurance").GetDecimal(), r.EmployerInsurance);
    Check(id, "taxableIncome", e.GetProperty("taxableIncome").GetDecimal(), r.TaxableIncome);
    Check(id, "tax", e.GetProperty("tax").GetDecimal(), r.Tax);
    Check(id, "net", e.GetProperty("net").GetDecimal(), r.Net);
}

foreach (var c in root.GetProperty("overtime").EnumerateArray())
{
    var salary = c.GetProperty("salary").GetDecimal();
    var minutes = c.GetProperty("minutes").GetInt32();
    var id = $"ot-{salary}-{minutes}";
    Check(id, "otPay", c.GetProperty("otPay").GetDecimal(), PayrollEngine.OvertimePay(salary, minutes, rules));
    Check(id, "shortfallPay", c.GetProperty("shortfallPay").GetDecimal(), PayrollEngine.ShortfallPay(salary, minutes));
}

// آزمون تقویم: روزهای ماه در فرمول حقوق
foreach (var y in new[] { 1403, 1404, 1405, 1406, 1407, 1408 })
foreach (var m in Enumerable.Range(1, 12))
{
    var expectedDays = m <= 6 ? 31 : m <= 11 ? 30 : (y % 4 == 3 ? 30 : 29);
    Check($"cal-{y}-{m}", "payrollMonthDays", expectedDays, PersianCalendarUtil.PayrollMonthDays(y, m));
}

Console.WriteLine("══════════════════════════════════════════════");
Console.WriteLine("   آزمون تطابق عددی موتور حقوق (C# ↔ JS)");
Console.WriteLine("══════════════════════════════════════════════");
Console.WriteLine($"  موفق : {passed}");
Console.WriteLine($"  ناموفق: {failed}");
if (failures.Count > 0)
{
    Console.WriteLine("\nنمونهٔ مغایرت‌ها:");
    foreach (var f in failures) Console.WriteLine("  ✘ " + f);
}
else Console.WriteLine("\n  ✔ تطابق کامل — هیچ اختلاف عددی وجود ندارد.");
return failed == 0 ? 0 : 1;
