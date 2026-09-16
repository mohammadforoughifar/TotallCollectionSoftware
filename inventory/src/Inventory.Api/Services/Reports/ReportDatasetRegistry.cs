using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Inventory.Shared.Entities;

namespace Inventory.Api.Services.Reports;

// =====================================================================
//  رجیستری دیتاست‌های گزارش‌ساز
//
//  هر دیتاست = یک کوئری projection از یک یا چند جدول join‌شده + فهرست
//  ستون‌ها (بعد/سنجه) + ماژول مجوز. کاربر در UI ستون، فیلتر،
//  گروه‌بندی، سنجه و نوع خروجی را انتخاب می‌کند.
//
//  نکتهٔ مجوز: Module هر دیتاست با ماژول‌های RBAC یکی است؛ هم فهرست
//  دیتاست‌ها و هم اجرای گزارش بر اساس آن فیلتر/کنترل می‌شود.
// =====================================================================

public interface IReportDatasetProvider
{
    IReadOnlyList<IReportDataset> Datasets { get; }
    IReportDataset? Find(string key);
}

public sealed class ReportDatasetProvider : IReportDatasetProvider
{
    private readonly AppDbContext _db;
    private List<IReportDataset>? _all;

    public ReportDatasetProvider(AppDbContext db) => _db = db;

    public IReadOnlyList<IReportDataset> Datasets => _all ??= Build();

    public IReportDataset? Find(string key) =>
        Datasets.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));

    private static AppDbContext Db(object o) => (AppDbContext)o;

    private List<IReportDataset> Build() => new()
    {
        // ================================================== فروش و درآمد
        SaleLine(), PurchaseLine(), InvoiceHead(),

        // ================================================== خزانه‌داری
        Cheques(), TreasuryVouchers(), TreasuryLines(),

        // ================================================== حسابداری
        AccVouchers(), AccLines(),

        // ================================================== هزینه و بودجه
        Expenses(), BudgetItems(),

        // ================================================== دارایی ثابت
        FixedAssets(),

        // ================================================== انبار و موجودی
        Stocks(), InvStocks(), InvDocs(), InvDocLines(), Stocktake(),

        // ================================================== کالا و اشخاص
        Products(), Parties(),

        // ================================================== عملیات و پشتیبانی
        WorkOrders(), ItRequests(), RepairOrders(), RepairItems(),

        // ================================================== منابع انسانی
        Employees(), AttDaily(), AttRecords(), Leaves(), PaySlips(),

        // ================================================== اسناد و مکاتبات و پروژه
        ArchiveDocs(), InnerLetters(), OutLetters(), ProjectWorks()
    };

    // =====================================================================
    //  فروش و خرید
    // =====================================================================

    private IReportDataset SaleLine() => Line("fac-sale-line", "فروش — قلم فاکتور",
        "فروش و درآمد", InvoiceKind.Sale, "bi-cart-check",
        "هر ردیف = یک قلم فاکتور فروش با مشخصات فاکتور، مشتری، کالا و انبار. برای تحلیل فروش بر اساس کالا/مشتری/ماه.");

    private IReportDataset PurchaseLine() => Line("fac-purchase-line", "خرید — قلم فاکتور",
        "فروش و درآمد", InvoiceKind.Purchase, "bi-bag-check",
        "هر ردیف = یک قلم فاکتور خرید با مشخصات تأمین‌کننده، کالا و انبار.");

    private IReportDataset Line(string key, string title, string category, InvoiceKind kind,
        string icon, string desc) =>
        new ReportDataset<InvoiceLineRow>(key, title, category, "FacInvoices",
            o => Db(o).FacInvoiceLines
                .Where(l => l.Invoice != null && l.Invoice.Kind == kind)
                .Select(l => new InvoiceLineRow
                {
                    Date = l.Invoice!.Date,
                    InvoiceNo = l.Invoice.Number,
                    Customer = l.Invoice.Party != null ? l.Invoice.Party.Name : null,
                    Product = l.Product != null ? l.Product.Name : null,
                    ProductCode = l.Product != null ? l.Product.Code : null,
                    Category = l.Product != null ? l.Product.Category : null,
                    Warehouse = l.Invoice.Warehouse != null ? l.Invoice.Warehouse.Name : null,
                    Unit = l.Product != null ? l.Product.Unit : null,
                    Status = (int)l.Invoice.Status,
                    Settlement = (int)l.Invoice.Settlement,
                    Qty = l.Quantity,
                    Price = l.UnitPrice,
                    Discount = l.Discount,
                    Vat = l.VatAmount,
                    Total = l.Total
                }),
            new ColumnSet<InvoiceLineRow>()
                .Date("date", "تاریخ", r => r.Date, anchor: true, def: true)
                .Int("inv", "شماره فاکتور", r => r.InvoiceNo)
                .Text("customer", kind == InvoiceKind.Sale ? "مشتری" : "تأمین‌کننده", r => r.Customer, def: true)
                .Text("product", "کالا", r => r.Product, def: true)
                .Text("pcode", "کد کالا", r => r.ProductCode)
                .Text("category", "دسته کالا", r => r.Category)
                .Text("warehouse", "انبار", r => r.Warehouse)
                .Text("unit", "واحد", r => r.Unit)
                .Enum("status", "وضعیت", r => r.Status, EnumFa.DocStatus, def: true)
                .Enum("settlement", "نوع تسویه", r => r.Settlement, EnumFa.Settlement)
                .Qty("qty", "مقدار", r => r.Qty, 1, null, hint: "تعداد/مقدار قلم")
                .Money("price", "فی", r => r.Price)
                .Money("discount", "تخفیف", r => r.Discount)
                .Money("vat", "مالیات", r => r.Vat)
                .Money("total", "مبلغ کل", r => r.Total, def: true),
            icon, desc, "date", "total");

    private IReportDataset InvoiceHead() =>
        new ReportDataset<InvoiceRow>("fac-invoice", "فاکتورها — سرجمع", "فروش و درآمد", "FacInvoices",
            o => Db(o).FacInvoices.Select(i => new InvoiceRow
            {
                Date = i.Date,
                Number = i.Number,
                Kind = (int)i.Kind,
                Status = (int)i.Status,
                Settlement = (int)i.Settlement,
                Party = i.Party != null ? i.Party.Name : null,
                Warehouse = i.Warehouse != null ? i.Warehouse.Name : null,
                LineCount = i.Lines.Count,
                TotalGross = i.TotalGross,
                Discount = i.TotalLineDiscount + i.InvoiceDiscount,
                Vat = i.TotalVat,
                Shipping = i.ShippingCost,
                TotalNet = i.TotalNet
            }),
            new ColumnSet<InvoiceRow>()
                .Date("date", "تاریخ", r => r.Date, anchor: true, def: true)
                .Int("no", "شماره", r => r.Number)
                .Enum("kind", "نوع", r => r.Kind, EnumFa.InvoiceKind, def: true)
                .Enum("status", "وضعیت", r => r.Status, EnumFa.DocStatus, def: true)
                .Enum("settlement", "تسویه", r => r.Settlement, EnumFa.Settlement)
                .Text("party", "طرف حساب", r => r.Party, def: true)
                .Text("warehouse", "انبار", r => r.Warehouse)
                .IntM("lines", "تعداد قلم", r => r.LineCount, "قلم")
                .Money("gross", "مبلغ ناخالص", r => r.TotalGross)
                .Money("discount", "تخفیف", r => r.Discount)
                .Money("vat", "مالیات", r => r.Vat)
                .Money("shipping", "هزینه ارسال", r => r.Shipping)
                .Money("net", "مبلغ خالص", r => r.TotalNet, def: true),
            "bi-receipt", "سرجمع هر فاکتور (یک ردیف به ازای هر فاکتور).", "date", "net");

    // =====================================================================
    //  خزانه‌داری
    // =====================================================================

    private IReportDataset Cheques() =>
        new ReportDataset<ChequeRow>("trs-cheque", "چک‌ها", "خزانه‌داری", "TrsCheques",
            o => Db(o).TrsCheques.Select(c => new ChequeRow
            {
                IssueDate = c.IssueDate,
                DueDate = c.DueDate,
                Number = c.Number,
                Kind = (int)c.Kind,
                Status = (int)c.Status,
                Party = c.Party != null ? c.Party.Name : null,
                Bank = c.BankName,
                Branch = c.BranchName,
                Account = c.TrsAccount != null && c.TrsAccount.Account != null ? c.TrsAccount.Account.Name : null,
                Amount = c.Amount
            }),
            new ColumnSet<ChequeRow>()
                .Date("issue", "تاریخ صدور", r => r.IssueDate, anchor: true, def: true)
                .Date("due", "سررسید", r => r.DueDate, def: true)
                .Text("no", "شماره چک", r => r.Number)
                .Enum("kind", "نوع", r => r.Kind, EnumFa.ChequeKind, def: true)
                .Enum("status", "وضعیت", r => r.Status, EnumFa.ChequeStatus, def: true)
                .Text("party", "طرف حساب", r => r.Party, def: true)
                .Text("bank", "بانک", r => r.Bank)
                .Text("branch", "شعبه", r => r.Branch)
                .Text("account", "حساب خزانه", r => r.Account)
                .Money("amount", "مبلغ", r => r.Amount, def: true),
            "bi-ticket-perforated", "چک‌های دریافتی و صادره با وضعیت و سررسید.", "issue", "amount");

    private IReportDataset TreasuryVouchers() =>
        new ReportDataset<TreasuryRow>("trs-voucher", "اسناد خزانه", "خزانه‌داری", "TrsVouchers",
            o => Db(o).TrsVouchers.Select(v => new TreasuryRow
            {
                Date = v.Date,
                Number = v.Number,
                Kind = (int)v.Kind,
                Status = (int)v.Status,
                Party = v.Party != null ? v.Party.Name : null,
                FromAccount = v.FromAccount != null && v.FromAccount.Account != null ? v.FromAccount.Account.Name : null,
                ToAccount = v.ToAccount != null && v.ToAccount.Account != null ? v.ToAccount.Account.Name : null,
                TotalAmount = v.TotalAmount,
                CashAmount = v.CashAmount,
                ChequeAmount = v.ChequeAmount,
                FeeAmount = v.FeeAmount,
                DiscountAmount = v.DiscountAmount
            }),
            new ColumnSet<TreasuryRow>()
                .Date("date", "تاریخ", r => r.Date, anchor: true, def: true)
                .Int("no", "شماره", r => r.Number)
                .Enum("kind", "نوع", r => r.Kind, EnumFa.TreasuryKind, def: true)
                .Enum("status", "وضعیت", r => r.Status, EnumFa.DocStatus, def: true)
                .Text("party", "طرف حساب", r => r.Party, def: true)
                .Text("from", "از حساب", r => r.FromAccount)
                .Text("to", "به حساب", r => r.ToAccount)
                .Money("total", "مبلغ کل", r => r.TotalAmount, def: true)
                .Money("cash", "نقد", r => r.CashAmount)
                .Money("cheque", "چک", r => r.ChequeAmount)
                .Money("fee", "کارمزد", r => r.FeeAmount)
                .Money("discount", "تخفیف", r => r.DiscountAmount),
            "bi-cash-coin", "دریافت‌ها، پرداخت‌ها و انتقال‌های خزانه.", "date", "total");

    private IReportDataset TreasuryLines() =>
        new ReportDataset<TreasuryLineRow>("trs-payment-line", "ردیف پرداخت/دریافت", "خزانه‌داری", "TrsVouchers",
            o => Db(o).TrsVoucherLines.Select(l => new TreasuryLineRow
            {
                Date = l.TrsVoucher != null ? l.TrsVoucher.Date : default,
                VoucherNo = l.TrsVoucher != null ? l.TrsVoucher.Number : 0,
                VoucherKind = l.TrsVoucher != null ? (int)l.TrsVoucher.Kind : 0,
                Method = (int)l.Method,
                Account = l.TrsAccount != null && l.TrsAccount.Account != null ? l.TrsAccount.Account.Name : null,
                Party = l.TrsVoucher != null && l.TrsVoucher.Party != null ? l.TrsVoucher.Party.Name : null,
                ChequeNo = l.Cheque != null ? l.Cheque.Number : null,
                Amount = l.Amount
            }),
            new ColumnSet<TreasuryLineRow>()
                .Date("date", "تاریخ سند", r => r.Date, anchor: true, def: true)
                .Int("no", "شماره سند", r => r.VoucherNo)
                .Enum("kind", "نوع سند", r => r.VoucherKind, EnumFa.TreasuryKind)
                .Enum("method", "روش پرداخت", r => r.Method, EnumFa.PayMethod, def: true)
                .Text("account", "حساب", r => r.Account, def: true)
                .Text("party", "طرف حساب", r => r.Party)
                .Text("cheque", "شماره چک", r => r.ChequeNo)
                .Money("amount", "مبلغ", r => r.Amount, def: true),
            "bi-list-ul", "جزئیات روش‌های پرداخت هر سند خزانه.", "date", "amount");

    // =====================================================================
    //  حسابداری
    // =====================================================================

    private IReportDataset AccVouchers() =>
        new ReportDataset<AccVoucherRow>("acc-voucher", "اسناد حسابداری — سرجمع", "حسابداری", "AccVouchers",
            o => Db(o).AccVouchers.Select(v => new AccVoucherRow
            {
                Date = v.Date,
                Number = v.Number,
                Status = (int)v.Status,
                Source = (int)v.Source,
                FiscalYear = v.FiscalYear != null ? v.FiscalYear.Title : null,
                LineCount = v.Lines.Count,
                TotalDebit = v.TotalDebit,
                TotalCredit = v.TotalCredit
            }),
            new ColumnSet<AccVoucherRow>()
                .Date("date", "تاریخ", r => r.Date, anchor: true, def: true)
                .Int("no", "شماره سند", r => r.Number, def: true)
                .Enum("status", "وضعیت", r => r.Status, EnumFa.DocStatus, def: true)
                .Int("source", "منبع سند", r => r.Source, hint: "۰=دستی، بقیه=خودکار از ماژول‌ها")
                .Text("fy", "سال مالی", r => r.FiscalYear)
                .IntM("lines", "تعداد ردیف", r => r.LineCount, "ردیف")
                .Money("debit", "بدهکار", r => r.TotalDebit, def: true)
                .Money("credit", "بستانکار", r => r.TotalCredit),
            "bi-journal-text", "سرجمع اسناد حسابداری.", "date", "debit");

    private IReportDataset AccLines() =>
        new ReportDataset<AccLineRow>("acc-voucher-line", "ردیف سند حسابداری", "حسابداری", "AccVouchers",
            o =>
            {
                var db = Db(o);
                return db.AccVoucherLines.Select(l => new AccLineRow
                {
                    Date = l.Voucher != null ? l.Voucher.Date : default,
                    VoucherNo = l.Voucher != null ? l.Voucher.Number : 0,
                    Status = l.Voucher != null ? (int)l.Voucher.Status : 0,
                    AccountCode = l.Account != null ? l.Account.Code : null,
                    AccountName = l.Account != null ? l.Account.Name : null,
                    AccountLevel = l.Account != null ? (int)l.Account.Level : 0,
                    AccountType = l.Account != null ? (int)l.Account.Type : 0,
                    Party = db.Parties.Where(p => p.Id == l.PartyId).Select(p => p.Name).FirstOrDefault(),
                    FiscalYear = l.Voucher != null && l.Voucher.FiscalYear != null ? l.Voucher.FiscalYear.Title : null,
                    Debit = l.Debit,
                    Credit = l.Credit
                });
            },
            new ColumnSet<AccLineRow>()
                .Date("date", "تاریخ سند", r => r.Date, anchor: true, def: true)
                .Int("no", "شماره سند", r => r.VoucherNo)
                .Enum("status", "وضعیت", r => r.Status, EnumFa.DocStatus)
                .Text("acode", "کد حساب", r => r.AccountCode, def: true)
                .Text("aname", "نام حساب", r => r.AccountName, def: true)
                .Int("level", "سطح حساب", r => r.AccountLevel)
                .Enum("type", "نوع حساب", r => r.AccountType, EnumFa.AccountType)
                .Text("party", "طرف حساب", r => r.Party)
                .Text("fy", "سال مالی", r => r.FiscalYear)
                .Money("debit", "بدهکار", r => r.Debit, def: true)
                .Money("credit", "بستانکار", r => r.Credit, def: true),
            "bi-journal-minus", "تراز آزمایشی و گزارش حساب: هر ردیف = یک گردش روی یک حساب.", "date", "debit");

    // =====================================================================
    //  هزینه و بودجه
    // =====================================================================

    private IReportDataset Expenses() =>
        new ReportDataset<ExpenseRow>("exp-expense", "هزینه‌ها", "هزینه و بودجه", "Expenses",
            o =>
            {
                var db = Db(o);
                return db.Expenses.Select(e => new ExpenseRow
                {
                    Date = e.Date,
                    Number = e.Number,
                    Category = db.ExpenseCategories.Where(c => c.Id == e.CategoryId).Select(c => c.Name).FirstOrDefault(),
                    PayType = (int)e.PayType,
                    Payee = e.Payee,
                    Description = e.Description,
                    Amount = e.Amount
                });
            },
            new ColumnSet<ExpenseRow>()
                .Date("date", "تاریخ", r => r.Date, anchor: true, def: true)
                .Text("no", "شماره", r => r.Number)
                .Text("category", "دسته هزینه", r => r.Category, def: true)
                .Enum("pay", "روش پرداخت", r => r.PayType, EnumFa.CashType)
                .Text("payee", "پرداخت‌کننده", r => r.Payee)
                .Text("desc", "شرح", r => r.Description)
                .Money("amount", "مبلغ", r => r.Amount, def: true),
            "bi-wallet2", "هزینه‌های ثبت‌شده با دسته و روش پرداخت.", "date", "amount");

    private IReportDataset BudgetItems() =>
        new ReportDataset<BudgetItemRow>("bud-budget-item", "ردیف بودجه", "هزینه و بودجه", "Budgets",
            o =>
            {
                var db = Db(o);
                return db.BudgetItems.Select(b => new BudgetItemRow
                {
                    FiscalYear = b.Budget != null && b.Budget.FiscalYear != null ? b.Budget.FiscalYear.Title : null,
                    AccountCode = b.AccAccount != null ? b.AccAccount.Code : null,
                    AccountName = b.AccAccount != null ? b.AccAccount.Name : null,
                    Planned = b.PlannedAmount,
                    Committed = b.CommittedAmount,
                    Actual = b.ActualAmount
                });
            },
            new ColumnSet<BudgetItemRow>()
                .Text("fy", "سال مالی", r => r.FiscalYear, def: true)
                .Text("acode", "کد حساب", r => r.AccountCode)
                .Text("aname", "نام حساب", r => r.AccountName, def: true)
                .Money("planned", "بودجه مصوب", r => r.Planned, def: true)
                .Money("committed", "تعهدشده", r => r.Committed)
                .Money("actual", "کارکرد واقعی", r => r.Actual, def: true),
            "bi-pie-chart", "مقایسهٔ بودجهٔ مصوب، تعهدشده و کارکرد.", null, "planned");

    // =====================================================================
    //  دارایی ثابت
    // =====================================================================

    private IReportDataset FixedAssets() =>
        new ReportDataset<AssetRow>("fa-asset", "دارایی ثابت", "دارایی ثابت", "FixedAssets",
            o => Db(o).FixedAssets.Select(a => new AssetRow
            {
                PurchaseDate = a.PurchaseDate,
                Code = a.Code,
                Name = a.Name,
                Category = a.Category != null ? a.Category.Name : null,
                Location = a.Location,
                Vendor = a.Vendor,
                Status = (int)a.Status,
                Method = (int)a.DepreciationMethod,
                UsefulLifeMonths = a.UsefulLifeMonths,
                PurchasePrice = a.PurchasePrice,
                SalvageValue = a.SalvageValue,
                AccumulatedDepreciation = a.AccumulatedDepreciation
            }),
            new ColumnSet<AssetRow>()
                .Date("buy", "تاریخ خرید", r => r.PurchaseDate, anchor: true, def: true)
                .Text("code", "کد دارایی", r => r.Code)
                .Text("name", "نام دارایی", r => r.Name, def: true)
                .Text("cat", "دسته", r => r.Category, def: true)
                .Text("loc", "محل استقرار", r => r.Location)
                .Text("vendor", "فروشنده", r => r.Vendor)
                .Enum("status", "وضعیت", r => r.Status, EnumFa.AssetStatus)
                .Enum("method", "روش استهلاک", r => r.Method, EnumFa.Depreciation)
                .IntM("life", "عمر مفید", r => r.UsefulLifeMonths, "ماه")
                .Money("price", "بهای تمام‌شده", r => r.PurchasePrice, def: true)
                .Money("salvage", "ارزش اسقاط", r => r.SalvageValue)
                .Money("accdep", "استهلاک انباشته", r => r.AccumulatedDepreciation, def: true),
            "bi-building", "دارایی‌های ثابت، بها و استهلاک انباشته.", "buy", "price");

    // =====================================================================
    //  انبار و موجودی
    // =====================================================================

    private IReportDataset Stocks() =>
        new ReportDataset<StockRow>("inv-stock", "موجودی انبار", "انبار و موجودی", "Stock",
            o =>
            {
                var db = Db(o);
                return db.Stocks.Select(s => new StockRow
                {
                    Product = db.Products.Where(p => p.Id == s.ProductId).Select(p => p.Name).FirstOrDefault(),
                    ProductCode = db.Products.Where(p => p.Id == s.ProductId).Select(p => p.Code).FirstOrDefault(),
                    Category = db.Products.Where(p => p.Id == s.ProductId).Select(p => p.Category).FirstOrDefault(),
                    Unit = db.Products.Where(p => p.Id == s.ProductId).Select(p => p.Unit).FirstOrDefault(),
                    Warehouse = db.Warehouses.Where(w => w.Id == s.WarehouseId).Select(w => w.Name).FirstOrDefault(),
                    Qty = s.Quantity,
                    AvgCost = s.AvgCost,
                    ReorderPoint = db.Products.Where(p => p.Id == s.ProductId).Select(p => p.ReorderPoint).FirstOrDefault()
                });
            },
            new ColumnSet<StockRow>()
                .Text("product", "کالا", r => r.Product, def: true)
                .Text("pcode", "کد کالا", r => r.ProductCode)
                .Text("cat", "دسته", r => r.Category, def: true)
                .Text("unit", "واحد", r => r.Unit)
                .Text("wh", "انبار", r => r.Warehouse, def: true)
                .Qty("qty", "موجودی", r => r.Qty, 1, null, def: true)
                .Money("cost", "بهای تمام‌شدهٔ میانگین", r => r.AvgCost)
                .Qty("reorder", "نقطه سفارش", r => r.ReorderPoint, 1),
            "bi-box-seam", "موجودی لحظه‌ای هر کالا در هر انبار.", null, "qty");

    private IReportDataset InvStocks() =>
        new ReportDataset<InvStockRow>("inv-stock-value", "ارزش موجودی", "انبار و موجودی", "InvDocs",
            o =>
            {
                var db = Db(o);
                return db.InvStocks.Select(s => new InvStockRow
                {
                    UpdatedAt = s.UpdatedAt,
                    Product = db.Products.Where(p => p.Id == s.ProductId).Select(p => p.Name).FirstOrDefault(),
                    Category = db.Products.Where(p => p.Id == s.ProductId).Select(p => p.Category).FirstOrDefault(),
                    Warehouse = db.Warehouses.Where(w => w.Id == s.WarehouseId).Select(w => w.Name).FirstOrDefault(),
                    Qty = s.Quantity,
                    AvgCost = s.AvgCost,
                    Value = s.Value
                });
            },
            new ColumnSet<InvStockRow>()
                .Date("upd", "آخرین به‌روزرسانی", r => r.UpdatedAt, anchor: true)
                .Text("product", "کالا", r => r.Product, def: true)
                .Text("cat", "دسته", r => r.Category, def: true)
                .Text("wh", "انبار", r => r.Warehouse, def: true)
                .Qty("qty", "مقدار", r => r.Qty, 1)
                .Money("cost", "میانگین بها", r => r.AvgCost)
                .Money("value", "ارزش ریالی", r => r.Value, def: true),
            "bi-cash-stack", "ارزش ریالی موجودی بر اساس کالا و انبار.", "upd", "value");

    private IReportDataset InvDocs() =>
        new ReportDataset<InvDocRow>("inv-doc", "اسناد انبار", "انبار و موجودی", "InvDocs",
            o =>
            {
                var db = Db(o);
                return db.InvDocs.Select(d => new InvDocRow
                {
                    Date = d.Date,
                    DocType = db.InvDocTypes.Where(t => t.Id == d.DocTypeId).Select(t => t.Name).FirstOrDefault(),
                    Nature = db.InvDocTypes.Where(t => t.Id == d.DocTypeId).Select(t => (int)t.Nature).FirstOrDefault(),
                    Status = (int)d.Status,
                    Warehouse = db.Warehouses.Where(w => w.Id == d.WarehouseId).Select(w => w.Name).FirstOrDefault(),
                    Party = db.Parties.Where(p => p.Id == d.PartyId).Select(p => p.Name).FirstOrDefault(),
                    Description = d.Description,
                    LineCount = d.Lines.Count,
                    TotalQuantity = d.TotalQuantity,
                    TotalValue = d.TotalValue
                });
            },
            new ColumnSet<InvDocRow>()
                .Date("date", "تاریخ", r => r.Date, anchor: true, def: true)
                .Text("type", "نوع سند", r => r.DocType, def: true)
                .Enum("nature", "اثر روی موجودی", r => r.Nature, EnumFa.StockNature)
                .Enum("status", "وضعیت", r => r.Status, EnumFa.DocStatus, def: true)
                .Text("wh", "انبار", r => r.Warehouse, def: true)
                .Text("party", "طرف حساب", r => r.Party)
                .Text("desc", "شرح", r => r.Description)
                .IntM("lines", "تعداد ردیف", r => r.LineCount, "ردیف")
                .Qty("qty", "مقدار کل", r => r.TotalQuantity, 1)
                .Money("value", "ارزش کل", r => r.TotalValue, def: true),
            "bi-arrow-left-right", "رسیدها، حواله‌ها و انتقال‌های انبار.", "date", "value");

    private IReportDataset InvDocLines() =>
        new ReportDataset<InvDocLineRow>("inv-doc-line", "ردیف سند انبار", "انبار و موجودی", "InvDocs",
            o =>
            {
                var db = Db(o);
                return db.InvDocLines.Select(l => new InvDocLineRow
                {
                    Date = l.Doc != null ? l.Doc.Date : default,
                    DocType = l.Doc != null
                        ? db.InvDocTypes.Where(t => t.Id == l.Doc.DocTypeId).Select(t => t.Name).FirstOrDefault()
                        : null,
                    Warehouse = l.Doc != null
                        ? db.Warehouses.Where(w => w.Id == l.Doc.WarehouseId).Select(w => w.Name).FirstOrDefault()
                        : null,
                    Product = db.Products.Where(p => p.Id == l.ProductId).Select(p => p.Name).FirstOrDefault(),
                    Unit = db.Products.Where(p => p.Id == l.ProductId).Select(p => p.Unit).FirstOrDefault(),
                    ExpiryDate = l.ExpiryDate,
                    Qty = l.Quantity,
                    UnitPrice = l.UnitPrice,
                    Discount = l.Discount
                });
            },
            new ColumnSet<InvDocLineRow>()
                .Date("date", "تاریخ سند", r => r.Date, anchor: true, def: true)
                .Text("type", "نوع سند", r => r.DocType, def: true)
                .Text("wh", "انبار", r => r.Warehouse, def: true)
                .Text("product", "کالا", r => r.Product, def: true)
                .Text("unit", "واحد", r => r.Unit)
                .DateN("exp", "تاریخ انقضا", r => r.ExpiryDate)
                .Qty("qty", "مقدار", r => r.Qty, 1, null, def: true)
                .Money("price", "فی", r => r.UnitPrice)
                .Money("discount", "تخفیف", r => r.Discount),
            "bi-list-nested", "ریز گردش کالا در اسناد انبار (کاردکس).", "date", "qty");

    private IReportDataset Stocktake() =>
        new ReportDataset<StkCountRow>("stk-count", "انبارگردانی", "انبار و موجودی", "StkSessions",
            o =>
            {
                var db = Db(o);
                return db.StkLines.Select(l => new StkCountRow
                {
                    Date = l.Session != null ? l.Session.Date : default,
                    Session = l.Session != null ? l.Session.Title : null,
                    Warehouse = l.Session != null && l.Session.Warehouse != null ? l.Session.Warehouse.Name : null,
                    Product = l.Product != null ? l.Product.Name : null,
                    IsCounted = l.IsCounted,
                    SystemQty = l.SystemQty,
                    CountedQty = l.CountedQty,
                    UnitCost = l.UnitCost,
                    ScanCount = l.ScanCount
                });
            },
            new ColumnSet<StkCountRow>()
                .Date("date", "تاریخ جلسه", r => r.Date, anchor: true, def: true)
                .Text("session", "جلسه انبارگردانی", r => r.Session, def: true)
                .Text("wh", "انبار", r => r.Warehouse)
                .Text("product", "کالا", r => r.Product, def: true)
                .Bool("counted", "شمارش شده", r => r.IsCounted)
                .Qty("sys", "موجودی سیستم", r => r.SystemQty, 1, null, def: true)
                .Qty("cnt", "موجودی شمارش‌شده", r => r.CountedQty, 1, null, def: true)
                .Money("cost", "بهای واحد", r => r.UnitCost)
                .IntM("scans", "تعداد اسکن", r => r.ScanCount, "بار")
                ,
            "bi-clipboard-check", "مقایسهٔ موجودی سیستم و شمارش واقعی.", "date", "sys");

    // =====================================================================
    //  کالا و اشخاص
    // =====================================================================

    private IReportDataset Products() =>
        new ReportDataset<ProductRow>("prod-catalog", "کالاها", "کالا و اشخاص", "Products",
            o =>
            {
                var db = Db(o);
                return db.Products.Select(p => new ProductRow
                {
                    Code = p.Code,
                    Name = p.Name,
                    Unit = p.Unit,
                    Category = p.CategoryId != null
                        ? db.ProductCategories.Where(c => c.Id == p.CategoryId).Select(c => c.Name).FirstOrDefault()
                        : p.Category,
                    Barcode = p.Barcode,
                    Brand = p.Brand,
                    IsActive = p.IsActive,
                    IsService = p.IsService,
                    SalePrice = p.SalePrice,
                    PurchasePrice = p.PurchasePrice,
                    ReorderPoint = p.ReorderPoint,
                    MaxStock = p.MaxStock
                });
            },
            new ColumnSet<ProductRow>()
                .Text("code", "کد کالا", r => r.Code, def: true)
                .Text("name", "نام کالا", r => r.Name, def: true)
                .Text("unit", "واحد", r => r.Unit)
                .Text("cat", "دسته", r => r.Category, def: true)
                .Text("barcode", "بارکد", r => r.Barcode)
                .Text("brand", "برند", r => r.Brand)
                .Bool("active", "فعال", r => r.IsActive)
                .Bool("service", "خدماتی", r => r.IsService)
                .Money("sale", "قیمت فروش", r => r.SalePrice, def: true)
                .Money("buy", "قیمت خرید", r => r.PurchasePrice)
                .Qty("reorder", "نقطه سفارش", r => r.ReorderPoint, 1)
                .Qty("max", "حداکثر موجودی", r => r.MaxStock, 1),
            "bi-box", "فهرست کالاها با قیمت و نقطه سفارش.", null, "sale");

    private IReportDataset Parties() =>
        new ReportDataset<PartyRow>("party-list", "اشخاص (مشتری/تأمین‌کننده)", "کالا و اشخاص", "Parties",
            o =>
            {
                var db = Db(o);
                return db.Parties.Select(p => new PartyRow
                {
                    Name = p.Name,
                    Type = (int)p.Type,
                    Phone = p.Phone,
                    Mobile = p.Mobile,
                    Address = p.Address,
                    IsActive = p.IsActive,
                    Referrer = db.Referrers.Where(r => r.Id == p.ReferrerId).Select(r => r.Name).FirstOrDefault()
                });
            },
            new ColumnSet<PartyRow>()
                .Text("name", "نام", r => r.Name, def: true)
                .Enum("type", "نوع", r => r.Type, EnumFa.PartyType, def: true)
                .Text("phone", "تلفن", r => r.Phone)
                .Text("mobile", "موبایل", r => r.Mobile)
                .Text("addr", "آدرس", r => r.Address)
                .Bool("active", "فعال", r => r.IsActive)
                .Text("ref", "معرف", r => r.Referrer),
            "bi-people", "طرف‌های حساب: مشتری‌ها و تأمین‌کننده‌ها.", null, null);

    // =====================================================================
    //  عملیات و پشتیبانی
    // =====================================================================

    private IReportDataset WorkOrders() =>
        new ReportDataset<WorkOrderRow>("wo-order", "دستورهای کار", "عملیات و پشتیبانی", "WorkOrders",
            o =>
            {
                var db = Db(o);
                return db.WorkOrders.Where(w => w.DeletedAt == null).Select(w => new WorkOrderRow
                {
                    CreatedAt = w.CreatedAt,
                    DueAt = w.DueAt,
                    ClosedAt = w.ClosedAt,
                    Number = w.Number,
                    Title = w.Title,
                    Owner = w.OwnerName,
                    Status = w.Status,
                    SourceModule = w.SourceModule,
                    Tags = w.Tags,
                    Priority = w.Priority,
                    AssigneeCount = db.WorkOrderAssignees.Count(a => a.OrderId == w.Id),
                    ExtensionCount = w.ExtensionCount
                });
            },
            new ColumnSet<WorkOrderRow>()
                .Date("created", "تاریخ ایجاد", r => r.CreatedAt, anchor: true, def: true)
                .Date("due", "مهلت", r => r.DueAt, def: true)
                .DateN("closed", "تاریخ بستن", r => r.ClosedAt)
                .Text("no", "شماره", r => r.Number)
                .Text("title", "عنوان", r => r.Title, def: true)
                .Text("owner", "درخواست‌دهنده", r => r.Owner, def: true)
                .Text("status", "وضعیت", r => r.Status, def: true, labels: EnumFa.WorkOrderStatus)
                .Text("src", "ماژول مبدأ", r => r.SourceModule)
                .Text("tags", "برچسب‌ها", r => r.Tags)
                .Int("prio", "اولویت", r => r.Priority)
                .IntM("assignees", "تعداد مسئول", r => r.AssigneeCount, "نفر")
                .IntM("ext", "تعداد تمدید", r => r.ExtensionCount, "بار"),
            "bi-tools", "دستورهای کار با وضعیت، مهلت و مسئولان.", "created", null);

    private IReportDataset ItRequests() =>
        new ReportDataset<ItRequestRow>("it-request", "درخواست‌های IT", "عملیات و پشتیبانی", "ItRequests",
            o => Db(o).ItRequests.Select(r => new ItRequestRow
            {
                CreatedAt = r.CreatedAt,
                AssignedAt = r.AssignedAt,
                CompletedAt = r.CompletedAt,
                Number = r.Number,
                Title = r.Title,
                Requester = r.RequesterName,
                System = r.SystemLabel,
                RequestType = r.RequestType,
                Status = r.Status
            }),
            new ColumnSet<ItRequestRow>()
                .Date("created", "تاریخ ثبت", r => r.CreatedAt, anchor: true, def: true)
                .DateN("assigned", "تاریخ ارجاع", r => r.AssignedAt)
                .DateN("done", "تاریخ تکمیل", r => r.CompletedAt)
                .Text("no", "شماره", r => r.Number)
                .Text("title", "عنوان", r => r.Title, def: true)
                .Text("req", "درخواست‌کننده", r => r.Requester, def: true)
                .Text("sys", "سامانه", r => r.System)
                .Text("type", "نوع درخواست", r => r.RequestType, def: true, labels: EnumFa.ItRequestType)
                .Text("status", "وضعیت", r => r.Status, def: true, labels: EnumFa.ItRequestStatus),
            "bi-pc-display", "درخواست‌های پشتیبانی IT.", "created", null);

    private IReportDataset RepairOrders() =>
        new ReportDataset<RepairOrderRow>("rep-order", "تعمیرات — سفارش", "عملیات و پشتیبانی", "Repairs",
            o =>
            {
                var db = Db(o);
                return db.RepairOrders.Select(r => new RepairOrderRow
                {
                    ReceivedAt = r.ReceivedAt,
                    DeliveredAt = r.DeliveredAt,
                    Number = r.Number,
                    Party = db.Parties.Where(p => p.Id == r.PartyId).Select(p => p.Name).FirstOrDefault(),
                    Technician = db.Technicians.Where(t => t.Id == r.TechnicianId).Select(t => t.Name).FirstOrDefault(),
                    DeviceType = r.DeviceType,
                    DeviceModel = r.DeviceModel,
                    Status = (int)r.Status,
                    ItemCount = db.RepairItems.Count(i => i.RepairOrderId == r.Id),
                    QuotedPrice = r.QuotedPrice
                });
            },
            new ColumnSet<RepairOrderRow>()
                .Date("recv", "تاریخ پذیرش", r => r.ReceivedAt, anchor: true, def: true)
                .DateN("deliv", "تاریخ تحویل", r => r.DeliveredAt)
                .Text("no", "شماره", r => r.Number)
                .Text("party", "مشتری", r => r.Party, def: true)
                .Text("tech", "تکنسین", r => r.Technician, def: true)
                .Text("dtype", "نوع دستگاه", r => r.DeviceType, def: true)
                .Text("model", "مدل", r => r.DeviceModel)
                .Enum("status", "وضعیت", r => r.Status, EnumFa.RepairStatus, def: true)
                .IntM("items", "تعداد قلم", r => r.ItemCount, "قلم")
                .Money("price", "مبلغ اعلامی", r => r.QuotedPrice, def: true),
            "bi-wrench", "سفارش‌های تعمیرات.", "recv", "price");

    private IReportDataset RepairItems() =>
        new ReportDataset<RepairItemRow>("rep-item", "تعمیرات — اقلام", "عملیات و پشتیبانی", "Repairs",
            o =>
            {
                var db = Db(o);
                return db.RepairItems.Select(i => new RepairItemRow
                {
                    ReceivedAt = db.RepairOrders.Where(r => r.Id == i.RepairOrderId).Select(r => r.ReceivedAt).FirstOrDefault(),
                    OrderNumber = db.RepairOrders.Where(r => r.Id == i.RepairOrderId).Select(r => r.Number).FirstOrDefault(),
                    OrderStatus = db.RepairOrders.Where(r => r.Id == i.RepairOrderId).Select(r => (int)r.Status).FirstOrDefault(),
                    Description = i.Description,
                    Product = db.Products.Where(p => p.Id == i.ProductId).Select(p => p.Name).FirstOrDefault(),
                    Qty = i.Quantity,
                    Cost = i.Cost,
                    Price = i.Price
                });
            },
            new ColumnSet<RepairItemRow>()
                .Date("recv", "تاریخ پذیرش", r => r.ReceivedAt, anchor: true, def: true)
                .Text("no", "شماره سفارش", r => r.OrderNumber)
                .Enum("status", "وضعیت سفارش", r => r.OrderStatus, EnumFa.RepairStatus)
                .Text("desc", "شرح خدمت", r => r.Description, def: true)
                .Text("product", "قطعه/کالا", r => r.Product, def: true)
                .Qty("qty", "تعداد", r => r.Qty, 1)
                .Money("cost", "بهای تمام‌شده", r => r.Cost)
                .Money("price", "مبلغ فروش", r => r.Price, def: true),
            "bi-wrench-adjustable", "اقلام و خدمات هر سفارش تعمیر.", "recv", "price");

    // =====================================================================
    //  منابع انسانی
    // =====================================================================

    private IReportDataset Employees() =>
        new ReportDataset<EmployeeRow>("hr-employee", "پرسنل", "منابع انسانی", "HrCore",
            o =>
            {
                var db = Db(o);
                return db.HrEmployees.Select(e => new EmployeeRow
                {
                    HireDate = e.HireDate,
                    Code = e.Code,
                    FullName = e.FirstName + " " + e.LastName,
                    OrgUnit = db.HrOrgUnits.Where(u => u.Id == e.OrgUnitId).Select(u => u.Name).FirstOrDefault(),
                    PostTitle = e.PostTitle,
                    Mobile = e.Mobile,
                    Degree = e.Degree,
                    EmploymentType = (int)e.EmploymentType,
                    Status = (int)e.Status,
                    IsActive = e.IsActive,
                    BaseSalary = e.BaseSalary
                });
            },
            new ColumnSet<EmployeeRow>()
                .Date("hire", "تاریخ استخدام", r => r.HireDate, anchor: true, def: true)
                .Text("code", "کد پرسنلی", r => r.Code, def: true)
                .Text("name", "نام و نام خانوادگی", r => r.FullName, def: true)
                .Text("unit", "واحد سازمانی", r => r.OrgUnit, def: true)
                .Text("post", "عنوان پست", r => r.PostTitle)
                .Text("mobile", "موبایل", r => r.Mobile)
                .Text("degree", "تحصیلات", r => r.Degree)
                .Enum("emp", "نوع استخدام", r => r.EmploymentType, EnumFa.EmploymentType)
                .Enum("status", "وضعیت", r => r.Status, EnumFa.EmployeeStatus, def: true)
                .Bool("active", "فعال", r => r.IsActive)
                .Money("base", "حقوق پایه", r => r.BaseSalary, def: true),
            "bi-person-badge", "پروندهٔ پرسنل با واحد سازمانی و حقوق پایه.", "hire", "base");

    private IReportDataset AttDaily() =>
        new ReportDataset<AttDailyRow>("fa-att-daily", "تردد روزانه پرسنل", "منابع انسانی", "FaAtt",
            o =>
            {
                var db = Db(o);
                return db.FaAttDailies.Select(d => new AttDailyRow
                {
                    Date = d.Date,
                    Employee = db.HrEmployees.Where(e => e.Id == d.EmployeeId)
                        .Select(e => e.FirstName + " " + e.LastName).FirstOrDefault(),
                    EmployeeCode = db.HrEmployees.Where(e => e.Id == d.EmployeeId).Select(e => e.Code).FirstOrDefault(),
                    FirstIn = d.FirstIn,
                    LastOut = d.LastOut,
                    Status = (int)d.Status,
                    IsIncomplete = d.IsIncomplete,
                    WorkMinutes = d.WorkMinutes,
                    LateMinutes = d.LateMinutes,
                    EarlyMinutes = d.EarlyMinutes,
                    OvertimeMinutes = d.OvertimeMinutes,
                    NightMinutes = d.NightMinutes
                });
            },
            new ColumnSet<AttDailyRow>()
                .Date("date", "تاریخ", r => r.Date, anchor: true, def: true)
                .Text("emp", "پرسنل", r => r.Employee, def: true)
                .Text("code", "کد پرسنلی", r => r.EmployeeCode)
                .DateN("in", "اولین ورود", r => r.FirstIn)
                .DateN("out", "آخرین خروج", r => r.LastOut)
                .Enum("status", "وضعیت روز", r => r.Status, EnumFa.AttDayStatus, def: true)
                .Bool("incomplete", "تردد ناقص", r => r.IsIncomplete)
                .IntM("work", "دقیقه کارکرد", r => r.WorkMinutes, "دقیقه", def: true)
                .IntM("late", "دقیقه تأخیر", r => r.LateMinutes, "دقیقه", def: true)
                .IntM("early", "دقیقه تعجیل", r => r.EarlyMinutes, "دقیقه")
                .IntM("ot", "دقیقه اضافه‌کاری", r => r.OvertimeMinutes, "دقیقه", def: true)
                .IntM("night", "دقیقه شب‌کاری", r => r.NightMinutes, "دقیقه"),
            "bi-clock-history", "کارکرد روزانهٔ هر پرسنل (محاسبه‌شده از تردد).", "date", "work");

    private IReportDataset AttRecords() =>
        new ReportDataset<AttRecordRow>("att-record", "تردد کاربران", "منابع انسانی", "Attendance",
            o => Db(o).AttendanceRecords.Select(a => new AttRecordRow
            {
                WorkDate = a.WorkDate,
                UserName = a.UserName,
                ShiftGroup = a.ShiftGroup != null ? a.ShiftGroup.Name : null,
                EnterAt = a.EnterAt,
                ExitAt = a.ExitAt,
                FinalStatus = a.FinalStatus,
                HasApprovedLeave = a.HasApprovedLeave,
                LateMinutes = a.LateMinutes,
                EarlyLeaveMinutes = a.EarlyLeaveMinutes,
                WorkMinutes = a.WorkMinutes,
                OvertimeMinutes = a.OvertimeMinutes,
                DeficitMinutes = a.DeficitMinutes
            }),
            new ColumnSet<AttRecordRow>()
                .Date("date", "تاریخ", r => r.WorkDate, anchor: true, def: true)
                .Text("user", "کاربر", r => r.UserName, def: true)
                .Text("shift", "شیفت", r => r.ShiftGroup)
                .DateN("in", "ورود", r => r.EnterAt)
                .DateN("out", "خروج", r => r.ExitAt)
                .Text("final", "وضعیت نهایی", r => r.FinalStatus, def: true)
                .Bool("leave", "مرخصی مصوب", r => r.HasApprovedLeave)
                .IntM("work", "دقیقه کارکرد", r => r.WorkMinutes, "دقیقه", def: true)
                .IntM("late", "دقیقه تأخیر", r => r.LateMinutes, "دقیقه")
                .IntM("early", "دقیقه خروج زودهنگام", r => r.EarlyLeaveMinutes, "دقیقه")
                .IntM("ot", "دقیقه اضافه‌کاری", r => r.OvertimeMinutes, "دقیقه")
                .IntM("def", "دقیقه کسری", r => r.DeficitMinutes, "دقیقه"),
            "bi-door-open", "تردد کاربران سامانه (وب‌کلک/موبایل).", "date", "work");

    private IReportDataset Leaves() =>
        new ReportDataset<LeaveRow>("leave-request", "مرخصی‌ها", "منابع انسانی", "LeaveRequests",
            o => Db(o).LeaveRequests.Select(l => new LeaveRow
            {
                StartDate = l.StartDate,
                EndDate = l.EndDate,
                ApprovedAt = l.ApprovedAt,
                Number = l.Number,
                Type = l.Type,
                Requester = l.RequesterName,
                Status = l.Status,
                ApprovedBy = l.ApprovedByName,
                Days = l.Days,
                Hours = l.Hours
            }),
            new ColumnSet<LeaveRow>()
                .Date("start", "از تاریخ", r => r.StartDate, anchor: true, def: true)
                .Date("end", "تا تاریخ", r => r.EndDate, def: true)
                .DateN("approved", "تاریخ تصویب", r => r.ApprovedAt)
                .Text("no", "شماره", r => r.Number)
                .Text("type", "نوع مرخصی", r => r.Type, def: true, labels: EnumFa.LeaveType)
                .Text("req", "درخواست‌کننده", r => r.Requester, def: true)
                .Text("status", "وضعیت", r => r.Status, def: true, labels: EnumFa.LeaveStatus)
                .Text("by", "تصویب‌کننده", r => r.ApprovedBy)
                .RealM("days", "تعداد روز", r => r.Days, 1, "روز", def: true)
                .RealM("hours", "تعداد ساعت", r => r.Hours, 1, "ساعت"),
            "bi-calendar-x", "درخواست‌های مرخصی با وضعیت و مدت.", "start", "days");

    private IReportDataset PaySlips() =>
        new ReportDataset<PaySlipRow>("hr-payslip", "فیش حقوق", "منابع انسانی", "HrPay",
            o =>
            {
                var db = Db(o);
                return db.HrPaySlips.Select(s => new PaySlipRow
                {
                    CreatedAt = s.CreatedAt,
                    Employee = db.HrEmployees.Where(e => e.Id == s.EmployeeId)
                        .Select(e => e.FirstName + " " + e.LastName).FirstOrDefault(),
                    EmployeeCode = db.HrEmployees.Where(e => e.Id == s.EmployeeId).Select(e => e.Code).FirstOrDefault(),
                    Year = db.HrPayRuns.Where(r => r.Id == s.RunId).Select(r => r.Year).FirstOrDefault(),
                    Month = db.HrPayRuns.Where(r => r.Id == s.RunId).Select(r => r.Month).FirstOrDefault(),
                    DaysPaid = s.DaysPaid,
                    BaseAmount = s.BaseAmount,
                    GrossEarnings = s.GrossEarnings,
                    TaxAmount = s.TaxAmount,
                    InsuranceAmount = s.InsuranceAmount,
                    OtherDeductions = s.OtherDeductions,
                    NetPay = s.NetPay
                });
            },
            new ColumnSet<PaySlipRow>()
                .Date("created", "تاریخ صدور", r => r.CreatedAt, anchor: true, def: true)
                .Text("emp", "پرسنل", r => r.Employee, def: true)
                .Text("code", "کد پرسنلی", r => r.EmployeeCode)
                .Int("year", "سال", r => r.Year, def: true)
                .Int("month", "ماه", r => r.Month, def: true)
                .RealM("days", "روزهای کارکرد", r => r.DaysPaid, 1, "روز", def: true)
                .Money("base", "حقوق پایه", r => r.BaseAmount)
                .Money("gross", "ناخالص", r => r.GrossEarnings, def: true)
                .Money("tax", "مالیات", r => r.TaxAmount)
                .Money("ins", "بیمه", r => r.InsuranceAmount)
                .Money("other", "سایر کسور", r => r.OtherDeductions)
                .Money("net", "خالص", r => r.NetPay, def: true),
            "bi-cash-coin", "فیش‌های حقوق با ناخالص، کسور و خالص.", "created", "net");

    // =====================================================================
    //  اسناد، نامه‌ها، پروژه
    // =====================================================================

    private IReportDataset ArchiveDocs() =>
        new ReportDataset<DocRow>("doc-archive", "مدارک آرشیو", "اسناد و مکاتبات", "DocArchive",
            o =>
            {
                var db = Db(o);
                return db.Documents.Where(d => !d.IsDeleted).Select(d => new DocRow
                {
                    CreatedAt = d.CreatedAt,
                    ExpireDate = d.ExpireDate,
                    Title = d.Title,
                    Code = d.Code,
                    Folder = db.DocFolders.Where(f => f.Id == d.FolderId).Select(f => f.Name).FirstOrDefault(),
                    CustomerCode = d.CustomerCode,
                    CreatedBy = d.CreatedByName,
                    IsActive = d.IsActive,
                    IsDeleted = d.IsDeleted
                });
            },
            new ColumnSet<DocRow>()
                .Date("created", "تاریخ ثبت", r => r.CreatedAt, anchor: true, def: true)
                .DateN("exp", "تاریخ انقضا", r => r.ExpireDate, def: true)
                .Text("title", "عنوان مدرک", r => r.Title, def: true)
                .Text("code", "کد مدرک", r => r.Code, def: true)
                .Text("folder", "پوشه", r => r.Folder, def: true)
                .Text("cust", "کد مشتری", r => r.CustomerCode)
                .Text("by", "ثبت‌کننده", r => r.CreatedBy)
                .Bool("active", "فعال", r => r.IsActive),
            "bi-archive", "مدارک بایگانی‌شده با پوشه و انقضا.", "created", null);

    private IReportDataset InnerLetters() =>
        new ReportDataset<InnerLetterRow>("inner-letter", "نامه‌های داخلی", "اسناد و مکاتبات", "InnerLetters",
            o =>
            {
                var db = Db(o);
                return db.InnerLetters.Where(l => !l.IsDelete).Select(l => new InnerLetterRow
                {
                    DateSabt = l.DateSabt,
                    LetterNumber = l.LetterNumber,
                    Title = l.Title,
                    Creator = db.Users.Where(u => u.Id == l.CreatorUserId).Select(u => u.Username).FirstOrDefault(),
                    Confidentiality = l.Mahramanegi,
                    Urgency = l.Foriat,
                    IsNeshan = l.IsNeshan
                });
            },
            new ColumnSet<InnerLetterRow>()
                .Date("date", "تاریخ ثبت", r => r.DateSabt, anchor: true, def: true)
                .Text("no", "شماره نامه", r => r.LetterNumber, def: true)
                .Text("title", "موضوع", r => r.Title, def: true)
                .Text("creator", "ایجادکننده", r => r.Creator)
                .Text("conf", "محرمانگی", r => r.Confidentiality)
                .Text("urg", "فوریت", r => r.Urgency)
                .Bool("star", "نشان‌دار", r => r.IsNeshan),
            "bi-envelope", "نامه‌های داخلی ثبت‌شده.", "date", null);

    private IReportDataset OutLetters() =>
        new ReportDataset<OutLetterRow>("out-letter", "نامه‌های صادره", "اسناد و مکاتبات", "InnerLetters",
            o =>
            {
                var db = Db(o);
                return db.OutgoingLetters.Where(l => !l.IsDelete).Select(l => new OutLetterRow
                {
                    DateSabt = l.DateSabt,
                    DateSadere = l.DateSadere,
                    Number = l.Number,
                    Status = l.Status,
                    Creator = db.Users.Where(u => u.Id == l.CreatorUserId).Select(u => u.Username).FirstOrDefault(),
                    IsNeshan = l.IsNeshan,
                    SignerCount = 0
                });
            },
            new ColumnSet<OutLetterRow>()
                .Date("date", "تاریخ ثبت", r => r.DateSabt, anchor: true, def: true)
                .DateN("sent", "تاریخ صدور", r => r.DateSadere, def: true)
                .Int("no", "شماره", r => r.Number, def: true)
                .Int("status", "وضعیت", r => r.Status, hint: "وضعیت گردش نامه")
                .Text("creator", "ایجادکننده", r => r.Creator)
                .Bool("star", "نشان‌دار", r => r.IsNeshan),
            "bi-send", "نامه‌های صادره از دبیرخانه.", "date", null);

    private IReportDataset ProjectWorks() =>
        new ReportDataset<ReportWorkRow>("prj-report-work", "گزارش کار پروژه", "پروژه‌ها", "ReportWorks",
            o =>
            {
                var db = Db(o);
                return db.ReportWorks.Select(w => new ReportWorkRow
                {
                    ReportDate = w.ReportDate,
                    CodeProject = w.CodeProject,
                    User = db.Users.Where(u => u.Id == w.UserId).Select(u => u.Username).FirstOrDefault(),
                    Operator = db.Users.Where(u => u.Id == w.OperatorId).Select(u => u.Username).FirstOrDefault(),
                    Description = w.WorkDescription
                });
            },
            new ColumnSet<ReportWorkRow>()
                .Date("date", "تاریخ گزارش", r => r.ReportDate, anchor: true, def: true)
                .Text("proj", "کد پروژه", r => r.CodeProject, def: true)
                .Text("user", "ثبت‌کننده", r => r.User, def: true)
                .Text("op", "اپراتور", r => r.Operator)
                .Text("desc", "شرح کار", r => r.Description, def: true),
            "bi-diagram-3", "گزارش‌های کار روزانهٔ پروژه‌ها.", "date", null);
}
