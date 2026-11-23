using Inventory.Client.Shared;
using Inventory.Shared;

static void Check(bool ok, string message)
{
    if (!ok) throw new Exception("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
}

// ---------- قاعدهٔ «صفر خالی است» ----------
Check(InvoiceRateInput.Text(0) == "0", "zero renders as 0, not an empty field");
Check(InvoiceRateInput.AfterBlur("", 0) == "0", "blur with no typing restores 0");
Check(InvoiceRateInput.AfterBlur(null, 0) == "0", "null typed text also restores 0");
Check(InvoiceRateInput.AfterBlur("", 25) == "0", "cleared field keeps its zero value");
Check(InvoiceRateInput.AfterBlur("25", 25) == "25", "typed value is kept on blur");
Check(InvoiceRateInput.AfterBlur("1200000", 1200000, grouped: true) == "1,200,000",
      "rial amount is grouped after blur");
Check(InvoiceRateInput.Text(0, grouped: true) == "0", "zero stays 0 even when grouped");
Check(InvoiceRateInput.Text(10) == "10", "plain percentage without grouping");
Check(InvoiceRateInput.Text(9.5m) == "9.5", "decimal percentage keeps its fraction");
Check(InvoiceRateInput.Text(1200000, grouped: true) == "1,200,000", "rial amount is grouped for reading");
Check(InvoiceRateInput.Text(-5) == "0", "negative values never render");
Check(InvoiceRateInput.Focused(0) == "", "focusing a zero field empties it");
Check(InvoiceRateInput.Focused(10) == "10", "focusing a filled field keeps its number");

// ---------- خواندنِ متنِ در حال تایپ ----------
Check(InvoiceRateInput.Value("") == 0, "empty text is zero for live calculation");
Check(InvoiceRateInput.Value(null) == 0, "null text is zero for live calculation");
Check(InvoiceRateInput.Value("۱۲۳") == 123, "Persian digits parse while typing");
Check(InvoiceRateInput.Value("١٢٣") == 123, "Arabic-Indic digits parse while typing");
Check(InvoiceRateInput.Value("1,200,000") == 1200000, "grouped rial text parses");
Check(InvoiceRateInput.Value("abc") == 0, "invalid text falls back to zero");
Check(InvoiceRateInput.Value("-7") == 0, "negative typing is clamped to zero");

// ---------- رفت‌وبرگشت با FormatTyping (همان قالب فیلدهای مبلغ) ----------
Check(Fa.FormatTyping("1200000") == "1,200,000", "money typing formatter groups digits");
Check(InvoiceRateInput.Value(Fa.FormatTyping("1200000")) == 1200000, "grouped text round-trips to a value");
Check(Fa.ToEn("۹.۵") == "9.5", "Persian decimal separator converts");


// ---------- متن حین تایپ ----------
Check(InvoiceRateInput.Typing("") == "", "empty typing stays empty");
Check(InvoiceRateInput.Typing("0") == "0", "a typed zero stays a real zero");
Check(InvoiceRateInput.Typing("00") == "0", "repeated zeros collapse to a single zero");
Check(InvoiceRateInput.Typing("0.0") == "0", "zero with a fraction still counts as zero");
Check(InvoiceRateInput.Typing("10") == "10", "typed percentage is kept as digits");
Check(InvoiceRateInput.Typing("۱۰") == "10", "Persian digits convert while typing");
Check(InvoiceRateInput.Typing("abc") == "abc", "unparsable text stays visible for correction");
Check(InvoiceRateInput.Typing("1,200,000", grouped: true) == "1,200,000", "rial amount is regrouped while typing");
Check(InvoiceRateInput.Typing("1000", grouped: true) == "1,000", "typing groups thousands for rial fields");
Check(InvoiceRateInput.Typing("1000") == "1000", "percentage fields stay ungrouped");

Console.WriteLine("ALL PASS");
