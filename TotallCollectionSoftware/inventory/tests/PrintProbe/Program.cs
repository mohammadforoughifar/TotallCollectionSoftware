using System.Text;
using Inventory.Client.Pages.Invoicing;
using Inventory.Client.Pages.Orders;
using Inventory.Client.Pages.Operations;
using Inventory.Client.Pages;
using Inventory.Shared;
using Inventory.Shared.Dtos;

// ============================================================
// آزمون واقعی سازنده‌های چاپ — همان کدی که در برنامه اجرا می‌شود:
//   ۱) SaleInvoicePrint         → چاپ سند «فروش» و «خرید» سفارش‌ها
//   ۲) FacInvoicePrint          → چاپ فاکتور ماژول فاکتورها (فروش/خرید/برگشتی)
//   ۳) RepairDeliveryReceiptPrint → فیش تحویل تعمیرات (بررسی «۱۰ روز» گارانتی)
// خروجی HTML در همان پوشه ذخیره می‌شود: out-sale.html / out-purchase.html
// / out-fac.html / out-fac-a5.html / out-receipt.html
// ============================================================

var outDir = AppContext.BaseDirectory;
int failures = 0;

void Check(string name, bool ok, string extra = "")
{
    Console.WriteLine($"   [{(ok ? "OK  " : "FAIL")}] {name}{(extra.Length > 0 ? "  → " + extra : "")}");
    if (!ok) failures++;
}

// اعداد فارسی: ۱۴,۷۰۰,۰۰۰ (ارقام لاتین هم پذیرفته می‌شود)
string Both(string ascii) =>
    ascii + "|" + new string(ascii.Select(c => char.IsDigit(c) ? "۰۱۲۳۴۵۶۷۸۹"[c - '0'] : c).ToArray());

bool Has(string html, string asciiNumber) => html.Contains(asciiNumber) || html.Contains(Both(asciiNumber).Split('|')[1]);

string Save(string file, string html)
{
    File.WriteAllText(Path.Combine(outDir, file), html, Encoding.UTF8);
    return html;
}

var profile = new CompanyProfileDto
{
    Name = "فروغ آریا",
    Phone = "021-12345678",
    Address = "تهران، خیابان نمونه",
    EconomicCode = "411234567890",
    TaxId = "10101234567",
    AccountNumber = "1234567890",
    PrintPaperSize = "A4",
    PrintShowSignatures = true,
    PrintHeaderLines = "فروشگاه تخصصی قطعات\r\nنمایندگی رسمی",
    PrintFooterLines = "رضایت شما، هدف ماست"
};

// ---------------------------------------------------------- ۱) سند فروش (صفحهٔ «فروش»)
Console.WriteLine("۱) SaleInvoicePrint — فاکتور فروش صفحهٔ «فروش»:");
var sale = new Order
{
    Id = 7,
    Number = "S-1405-0042",
    Type = TransactionType.Sale,
    Date = new DateTime(2026, 10, 13, 10, 30, 0),
    WarehouseName = "انبار مرکزی",
    PartyName = "شرکت نمونه خریدار",
    PaymentMethod = PaymentMethod.Credit,
    Description = "تحویل در محل مشتری",
    ReferrerName = "آقای معرف",
    Lines =
    {
        new OrderLine { Id = 1, ProductName = "کارت شبکه ۱ گیگ", Unit = "عدد", Quantity = 3, Price = 4_500_000 },
        new OrderLine { Id = 2, ProductName = "نصب و راه‌اندازی", Unit = "سرویس", Quantity = 1, Price = 1_200_000, IsService = true }
    }
};
sale.TotalAmount = 3 * 4_500_000m + 1_200_000m;

var saleHtml = Save("out-sale.html", SaleInvoicePrint.Build(sale, profile));
Check("HTML ساخته شد", saleHtml.Length > 2000, $"{saleHtml.Length} بایت");
Check("عنوان «فاکتور فروش»", saleHtml.Contains("فاکتور فروش"));
Check("شماره سند", saleHtml.Contains("S-1405-0042"));
Check("نام مشتری", saleHtml.Contains("شرکت نمونه خریدار"));
Check("ردیف خدمات", saleHtml.Contains("نصب و راه‌اندازی"));
Check("مبلغ کل سند (۱۴,۷۰۰,۰۰۰)", Has(saleHtml, "14,700,000"));
Check("جمع سطر (۱۳,۵۰۰,۰۰۰)", Has(saleHtml, "13,500,000"));
Check("سربرگ شرکت", saleHtml.Contains("فروغ آریا"));
Check("شماره حساب در پاورقی", saleHtml.Contains("1234567890"));
Check("کادر امضا", saleHtml.Contains("امضای") || saleHtml.Contains("مهر"));
Check("محل فونت برای iframe چاپ", saleHtml.Contains("__FONT_BASE__"));
Check("جهت RTL", saleHtml.Contains("dir=\"rtl\""));

// Per-print paper choice overrides (but never modifies) the company default.
var compactSale = new Order
{
    Type = TransactionType.Sale, Number = "<S-42>", PartyName = "<مشتری>", Date = sale.Date,
    TotalAmount = 1150m, ChequeIncomeAmount = 50m, HasChequeCharge = true,
    Lines = { new OrderLine { Id = 1, ProductName = "کالا <script>", Unit = "عدد", Quantity = 2, Price = 600,
        DiscountAmount = 200, VatValue = 10, VatAmount = 100 } },
    Cheques = { new ChequeDto { Number = "CHK<1>", BankName = "بانک", Amount = 1150m, DueDate = sale.Date.AddMonths(1) } }
};
var a5Html = SaleInvoicePrint.Build(compactSale, profile, "A5");
Check("A5 شامل اقلام، سود چک و جدول سررسید", a5Html.Contains("size:A5 portrait") &&
    a5Html.Contains("compact-items") && a5Html.Contains("تاخیر تادیه چک") && a5Html.Contains("CHK&lt;1&gt;"));
Check("مقادیر فاکتور HTML امن هستند", !a5Html.Contains("کالا <script>") && a5Html.Contains("&lt;script&gt;") &&
    a5Html.Contains("&lt;مشتری&gt;"));
Check("انتخاب کاغذ تنظیم شرکت را تغییر نمی‌دهد", profile.PrintPaperSize == "A4" &&
    SaleInvoicePrint.Build(compactSale, new CompanyProfileDto { PrintPaperSize = "A5" }, "A4").Contains("size:A4 portrait"));
compactSale.IsCancelled = true;
Check("فاکتور ابطال‌شده برچسب دارد", SaleInvoicePrint.Build(compactSale, profile).Contains("این فاکتور ابطال شده است"));
var fractional = new Order { Type = TransactionType.Purchase, Number = "F", Date = sale.Date, TotalAmount = 3m,
    Lines = { new OrderLine { ProductName = "جزئی", Quantity = 2m, Price = 1.5m } } };
var fractionalHtml = SaleInvoicePrint.Build(fractional, profile);
Check("فی اعشاری با جمع صحیح نیز حفظ می‌شود", Has(fractionalHtml, "1.5") || fractionalHtml.Contains("۱٫۵"));

var statement = new PartyStatementDetailDto
{
    PartyName = "<مشتری>", OpeningBalance = 100, SaleGoodsTotal = 1100, SaleIssuedDiscountTotal = 200,
    SaleChequeIncomeTotal = 50, TotalDebit = 1150, TotalCredit = 400, ClosingBalance = 850,
    Rows = { new PartyStatementRowDto { Date = sale.Date, Title = "فاکتور فروش", Description = "<unsafe>",
        DocumentNumber = "S-42", SaleInvoiceDiscount = 200, Debit = 1100, Balance = 1200 },
        new PartyStatementRowDto { Date = sale.Date, Title = "تاخیر تادیه چک", Debit = 50, Balance = 1250 },
        new PartyStatementRowDto { Date = sale.Date.AddDays(1), Title = "وصول چک", Credit = 400, Balance = 850 } }
};
var statementHtml = Save("out-customer-statement.html", CustomerStatementPrint.Build(statement, profile, null, null));
Check("گردش مشتری شامل تخفیف، سود چک، وصول و مانده", statementHtml.Contains("تخفیف فاکتور") &&
    statementHtml.Contains("تاخیر تادیه چک") && statementHtml.Contains("وصول چک") && statementHtml.Contains("مانده پایان دوره"));
Check("گردش مشتری سود و پورسانت فروشگاه را افشا نمی‌کند", !statementHtml.Contains("پورسانت") &&
    !statementHtml.Contains("سود خالص") && !statementHtml.Contains("سود ناخالص"));
Check("متن گردش HTML امن است", statementHtml.Contains("&lt;unsafe&gt;") && !statementHtml.Contains("<unsafe>"));

// رسید خرید با همان سازنده
var purchase = new Order
{
    Number = "P-1405-0011", Type = TransactionType.Purchase, Date = DateTime.Now,
    WarehouseName = "انبار مرکزی", PartyName = "تأمین‌کننده الف", PaymentMethod = PaymentMethod.Cash,
    Lines = { new OrderLine { ProductName = "هارد اکسترنال", Unit = "عدد", Quantity = 2, Price = 8_000_000 } },
    TotalAmount = 16_000_000
};
var purchaseHtml = Save("out-purchase.html", SaleInvoicePrint.Build(purchase, profile));
Check("رسید خرید ساخته شد", purchaseHtml.Contains("رسید خرید"));
Check("مبلغ رسید خرید (۱۶,۰۰۰,۰۰۰)", Has(purchaseHtml, "16,000,000"));

// ---------------------------------------------------------- ۲) فاکتور ماژول فاکتورها
Console.WriteLine();
Console.WriteLine("۲) FacInvoicePrint — فاکتور فروشِ ماژول فاکتورها:");
var inv = new FacInvoice
{
    Id = 12,
    Number = 1042,
    Kind = InvoiceKind.Sale,
    Date = new DateTime(2026, 10, 13, 11, 0, 0),
    DueDate = new DateTime(2026, 11, 13),
    PartyName = "بازرگانی الفبا",
    WarehouseName = "انبار مرکزی",
    Settlement = SettlementType.Credit,
    Status = InvoiceStatus.Confirmed,
    RefNumber = "REF-9988",
    Description = "فروش تجهیزات شبکه",
    Lines =
    {
        new FacInvoiceLine { Id = 1, RowNo = 1, ProductCode = "NW-100", ProductName = "سوییچ ۲۴ پورت", Unit = "عدد",
            Quantity = 2, UnitPrice = 12_000_000, Discount = 500_000, VatRate = 10, VatAmount = 2_350_000 },
        new FacInvoiceLine { Id = 2, RowNo = 2, ProductCode = "SV-01", ProductName = "خدمات پیکربندی", Unit = "سرویس",
            Quantity = 1, UnitPrice = 3_000_000, Discount = 0, VatRate = 10, VatAmount = 300_000 }
    },
    TotalGross = 2 * 12_000_000m + 3_000_000m,
    TotalLineDiscount = 500_000,
    InvoiceDiscount = 100_000,
    TotalTaxable = (2 * 12_000_000m + 3_000_000m) - 500_000m - 100_000m,
    TotalVat = 2_650_000,
    ShippingCost = 200_000,
    SettledAmount = 5_000_000
};
inv.TotalNet = inv.TotalTaxable + inv.TotalVat + inv.ShippingCost;
inv.RemainingAmount = inv.TotalNet - inv.SettledAmount;
inv.LineCount = 2;
inv.TotalQuantity = 3;

var invHtml = Save("out-fac.html", FacInvoicePrint.Build(inv, profile));
Check("HTML ساخته شد", invHtml.Length > 2000, $"{invHtml.Length} بایت");
Check("عنوان «فاکتور فروش»", invHtml.Contains("فاکتور فروش"));
Check("شماره فاکتور (۱۰۴۲)", invHtml.Contains("۱۰۴۲"));
Check("طرف حساب", invHtml.Contains("بازرگانی الفبا"));
Check("شرح کالا", invHtml.Contains("سوییچ ۲۴ پورت"));
Check("کد کالا", invHtml.Contains("NW-100"));
Check("ستون تخفیف", invHtml.Contains("تخفیف اقلام"));
Check("ستون مالیات", invHtml.Contains("مالیات و عوارض"));
Check("هزینه حمل", invHtml.Contains("هزینه حمل"));
Check("مبلغ قابل پرداخت (۲۹,۲۵۰,۰۰۰)", Has(invHtml, "29,250,000"));
Check("جمع سطر اول (۲۵,۸۵۰,۰۰۰)", Has(invHtml, "25,850,000"));
Check("تسویه‌شده و مانده", invHtml.Contains("تسویه‌شده") && invHtml.Contains("مانده"));
Check("ماندهٔ درست (۲۴,۲۵۰,۰۰۰)", Has(invHtml, "24,250,000"));
Check("شماره عطف", invHtml.Contains("REF-9988"));
Check("استایل درون‌سند", invHtml.Contains("<style>") && invHtml.Contains(".fac-totals"));
Check("بازاستفاده از CSS فاکتور سفارش‌ها", invHtml.Contains(".invoice-stamp"));

var a5Html = Save("out-fac-a5.html", FacInvoicePrint.Build(inv, new CompanyProfileDto { Name = "فروغ آریا", PrintPaperSize = "A5" }));
Check("حالت کاغذ A5", a5Html.Contains("A5 portrait") && a5Html.Contains("134mm"));

var ret = new FacInvoice
{
    Number = 55, Kind = InvoiceKind.SaleReturn, Status = InvoiceStatus.Confirmed, Date = DateTime.Now,
    Lines = { new FacInvoiceLine { ProductName = "مرجوعی", Unit = "عدد", Quantity = 1, UnitPrice = 1000 } }, TotalNet = 1000
};
Check("برگشت از فروش", FacInvoicePrint.Build(ret, profile).Contains("برگشت از فروش"));

// ---------------------------------------------------------- ۳) فیش تحویل تعمیرات
Console.WriteLine();
Console.WriteLine("۳) RepairDeliveryReceiptPrint — فیش تحویل دستگاه (گارانتی ۱۰ روز):");
var repair = new RepairOrderDto
{
    Id = 3,
    Number = "R-1405-0003",
    ReceivedAt = new DateTime(2026, 10, 10, 9, 0, 0),
    DeliveredAt = new DateTime(2026, 10, 13, 12, 0, 0),
    PartyName = "آقای نمونه",
    PartyMobile = "09120000000",
    TechnicianName = "کارشناس فنی",
    Note = "ترخیص با تأیید مشتری",
    TotalPrice = 5_000_000,
    Items = { new RepairItemDto { Id = 1, Description = "تعویض برد تغذیه", Quantity = 1, Price = 5_000_000 } },
    Devices = { new RepairDeviceDto { Id = 1, DeviceType = "پرینتر لیزری", DeviceModel = "HP-1102", SerialNumber = "SN-12345", Accessories = "کابل برق، کارتریج" } }
};
var receiptHtml = Save("out-receipt.html", RepairDeliveryReceiptPrint.Build(repair, "فروغ آریا", profile));
Check("HTML ساخته شد", receiptHtml.Length > 2000, $"{receiptHtml.Length} بایت");
Check("عنوان «فیش تحویل دستگاه»", receiptHtml.Contains("فیش تحویل دستگاه"));
Check("گارانتی «۱۰ روز»", receiptHtml.Contains("۱۰ روز مهلت تست"));
Check("گارانتی «۳۰ روز» حذف شده", !receiptHtml.Contains("۳۰ روز مهلت تست"));
Check("مبلغ کل خدمات (۵,۰۰۰,۰۰۰)", Has(receiptHtml, "5,000,000"));
Check("سریال دستگاه", receiptHtml.Contains("SN-12345"));

Console.WriteLine();
Console.WriteLine(failures == 0
    ? $"همهٔ بررسی‌ها موفق ✅  (خروجی‌های HTML در {outDir} ذخیره شد)"
    : $"{failures} بررسی ناموفق ❌");
return failures == 0 ? 0 : 1;
