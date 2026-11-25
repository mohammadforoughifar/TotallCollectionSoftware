using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos.Moadian;
using Microsoft.EntityFrameworkCore;
using InvoiceVM = Inventory.Shared.Dtos.Moadian.VmInvoiceItems.InvoiceVM;

namespace Inventory.Api.Services.Moadian;

/// <summary>قالب و ورود گروهی پیش‌نویس صورتحساب مودیان از فایل اکسل.</summary>
public interface IMoadianInvoiceExcelImportService
{
    byte[] BuildTemplate();
    Task<ApiModel> ImportAsync(int cpInfoId, Stream excelStream);
}

/// <summary>
/// ورود امن و قابل تکرار صورتحساب‌ها: هر سربرگ با همهٔ اقلامش یک واحد اعتبارسنجی است؛
/// فقط صورتحساب‌های کامل ثبت می‌شوند و هیچ صورتحسابی در این مسیر به سامانه ارسال نمی‌شود.
/// </summary>
public sealed class MoadianInvoiceExcelImportService(IConnectService connectService, AppDbContext context)
    : IMoadianInvoiceExcelImportService
{
    private const int MaxInvoices = 500;
    private const int MaxInvoiceLines = 10_000;
    private const int MaxErrorDetails = 200;

    private sealed record ColumnSpec(string Key, string Caption, bool Required, string[] Aliases);

    private sealed class HeaderRow
    {
        public int RowNumber { get; init; }
        public string? ImportReference { get; set; }
        public DateOnly? DateOrder { get; set; }
        public int? TypeCode { get; set; }
        public string? PaymentType { get; set; }
        public int? CustomerId { get; set; }
        public int? FiscalYearId { get; set; }
        public string? Description { get; set; }
        public bool Invalid { get; set; }
        public bool HasInvalidLine { get; set; }
        public List<VmInvoiceItems> Items { get; } = new();
    }

    private static readonly ColumnSpec[] InvoiceColumns =
    {
        new("key", "کد فاکتور در فایل", true, new[] { "کد فاکتور در فایل", "کلید فاکتور", "شناسه فاکتور اکسل" }),
        new("date", "تاریخ فاکتور", true, new[] { "تاریخ فاکتور (شمسی/میلادی)", "تاریخ فاکتور", "تاریخ" }),
        new("type", "نوع صورتحساب", true, new[] { "نوع صورتحساب", "نوع" }),
        new("payment", "نوع پرداخت", true, new[] { "نوع پرداخت" }),
        new("nationalId", "کد ملی/شماره ثبت مشتری", false, new[] { "کد ملی/شماره ثبت مشتری", "کد ملی مشتری", "شماره ثبت مشتری" }),
        new("economic", "شماره اقتصادی مشتری", false, new[] { "شماره اقتصادی مشتری", "کد اقتصادی مشتری" }),
        new("customerName", "نام مشتری", false, new[] { "نام مشتری", "مشتری" }),
        new("description", "توضیحات", false, new[] { "توضیحات", "شرح فاکتور" })
    };

    private static readonly ColumnSpec[] LineColumns =
    {
        new("key", "کد فاکتور در فایل", true, new[] { "کد فاکتور در فایل", "کلید فاکتور", "شناسه فاکتور اکسل" }),
        new("cpc", "شناسه CPC کالا/خدمت", true, new[] { "شناسه CPC کالا/خدمت", "شناسه CPC", "شناسه کالا/خدمت", "کد CPC" }),
        new("goodsName", "نام کالا/خدمت", false, new[] { "نام کالا/خدمت", "نام کالا", "نام خدمت" }),
        new("count", "تعداد", true, new[] { "تعداد", "مقدار" }),
        new("price", "فی واحد (ریال)", true, new[] { "فی واحد (ریال)", "قیمت واحد (ریال)", "فی واحد", "قیمت واحد" }),
        new("discount", "تخفیف کل ردیف (ریال)", false, new[] { "تخفیف کل ردیف (ریال)", "تخفیف ردیف (ریال)", "تخفیف", "مبلغ تخفیف" }),
        new("vat", "نرخ ارزش افزوده (%)", false, new[] { "نرخ ارزش افزوده (%)", "نرخ ارزش افزوده", "ارزش افزوده" }),
        new("description", "شرح ردیف", false, new[] { "شرح ردیف", "توضیحات ردیف" })
    };

    public byte[] BuildTemplate()
    {
        using var workbook = new XLWorkbook();
        var invoiceHeaders = new[]
        {
            "کد فاکتور در فایل *", "تاریخ فاکتور (شمسی/میلادی) *", "نوع صورتحساب *", "نوع پرداخت *",
            "کد ملی/شماره ثبت مشتری", "شماره اقتصادی مشتری", "نام مشتری", "توضیحات"
        };
        var lineHeaders = new[]
        {
            "کد فاکتور در فایل *", "شناسه CPC کالا/خدمت *", "نام کالا/خدمت",
            "تعداد *", "فی واحد (ریال) *", "تخفیف کل ردیف (ریال)", "نرخ ارزش افزوده (%)", "شرح ردیف"
        };

        var invoices = workbook.Worksheets.Add("فاکتورها");
        invoices.RightToLeft = true;
        WriteHeaders(invoices, 1, invoiceHeaders);
        ApplyColumnWidths(invoices, invoiceHeaders);
        invoices.SheetView.FreezeRows(1);
        invoices.Column(1).Style.NumberFormat.Format = "@";
        invoices.Column(2).Style.NumberFormat.Format = "@";
        invoices.Column(5).Style.NumberFormat.Format = "@";
        invoices.Column(6).Style.NumberFormat.Format = "@";

        var lines = workbook.Worksheets.Add("اقلام");
        lines.RightToLeft = true;
        WriteHeaders(lines, 1, lineHeaders);
        ApplyColumnWidths(lines, lineHeaders);
        lines.SheetView.FreezeRows(1);
        lines.Column(1).Style.NumberFormat.Format = "@";
        lines.Column(2).Style.NumberFormat.Format = "@";
        lines.Column(4).Style.NumberFormat.Format = "0.##";
        lines.Column(5).Style.NumberFormat.Format = "#,##0";
        lines.Column(6).Style.NumberFormat.Format = "#,##0";

        var guide = workbook.Worksheets.Add("راهنما");
        guide.RightToLeft = true;
        var guideLines = new[]
        {
            "راهنمای ورود اکسل صورتحساب مودیان",
            "۱) در برگهٔ «فاکتورها» برای هر صورتحساب یک سطر و یک کد فاکتور در فایلِ یکتا وارد کنید.",
            "۲) در برگهٔ «اقلام»، برای هر ردیف کالا/خدمت یک سطر بسازید و همان کد فاکتور را در ستون اول تکرار کنید.",
            "۳) برای صورتحساب نوع اول، کد ملی/شماره ثبت یا شماره اقتصادی مشتری را وارد کنید. نام مشتری فقط برای رفع ابهام است؛ نام به‌تنهایی کافی نیست.",
            "۴) در صورتحساب نوع دوم/سوم، اطلاعات خریدار باید خالی و نوع پرداخت حتماً «نقدی» باشد.",
            "۵) تاریخ باید شمسی مثل ۱۴۰۵/۰۱/۱۵ یا میلادی مثل ۲۰۲۶/۰۴/۰۴ باشد و در بازهٔ دقیقاً یک سال مالی تعریف‌شده و حذف‌نشده قرار بگیرد؛ سال مالی از روی تاریخ تعیین می‌شود.",
            "۶) شناسهٔ CPC باید به کالای/خدمت فعال همان شرکت مودیان تعلق داشته باشد. اگر چند مورد شناسهٔ یکسان دارند، نام کالا/خدمت را دقیقاً مطابق اطلاعات پایه وارد کنید.",
            "۷) تعداد بیشتر از صفر و حداکثر با دو رقم اعشار باشد. فی و تخفیف به ریالِ صحیح (بدون اعشار) هستند؛ تخفیف، مبلغ کل همان ردیف است و نباید از مبلغ ردیف بیشتر شود.",
            "۸) نرخ ارزش افزوده اختیاری است؛ اگر خالی باشد نرخ ثبت‌شدهٔ کالا/خدمت استفاده می‌شود. اگر وارد شود عدد صحیح ۰ تا ۱۰۰ باشد.",
            "۹) هر فاکتور فقط وقتی ثبت می‌شود که سربرگ و همهٔ اقلام آن معتبر باشند. خطای یک قلم باعث ثبت‌نشدن کل همان فاکتور می‌شود؛ فاکتورهای سالم دیگر ثبت خواهند شد.",
            "۱۰) برای جلوگیری از ثبت دوباره، کد فاکتور در فایل به‌عنوان کلید یکتا برای همان شرکت ذخیره می‌شود؛ پس از ثبت موفق، همان کد را دوباره استفاده نکنید.",
            "۱۱) فایل فقط پیش‌نویس با وضعیت «ارسال نشده» می‌سازد؛ هیچ فاکتوری خودکار به سامانهٔ مودیان ارسال نمی‌شود.",
            "۱۲) حداکثر ۵۰۰ فاکتور و ۱۰٬۰۰۰ قلم در هر فایل پذیرفته می‌شود. برای حفظ صفر ابتدای شناسه‌ها، ستون‌های کد را به‌صورت Text وارد کنید.",
            "۱۳) برگهٔ «نمونه» فقط راهنماست و خوانده نمی‌شود. شناسه‌ها و اعداد نمونه فرضی‌اند؛ فقط کدهای موجود در اطلاعات پایهٔ خودتان را وارد کنید."
        };
        guide.Column(1).Width = 110;
        for (var i = 0; i < guideLines.Length; i++)
        {
            var row = i + 1;
            var range = guide.Range(row, 1, row, 4);
            range.Merge();
            guide.Cell(row, 1).Value = guideLines[i];
            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            range.Style.Alignment.WrapText = true;
            guide.Row(row).Height = i == 0 ? 30 : 34;
        }
        guide.Range(1, 1, 1, 4).Style.Font.Bold = true;
        guide.Range(1, 1, 1, 4).Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEBF7");

        var sample = workbook.Worksheets.Add("نمونه");
        sample.RightToLeft = true;
        var note = sample.Range(1, 1, 1, Math.Max(invoiceHeaders.Length, lineHeaders.Length));
        note.Merge();
        sample.Cell(1, 1).Value = "این برگه صرفاً نمونه است؛ اطلاعات آن هنگام ورود خوانده نمی‌شود. کدها فرضی‌اند و باید با اطلاعات پایهٔ واقعی جایگزین شوند.";
        note.Style.Font.Bold = true;
        note.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF2CC");
        note.Style.Alignment.WrapText = true;
        note.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        sample.Row(1).Height = 34;

        WriteHeaders(sample, 3, invoiceHeaders);
        var invoiceSample = new object?[]
        {
            "F-0001", "1405/01/15", "نوع اول", "نقدی", "1234567890", "12345678901", "مشتری نمونه", "فاکتور نمونه"
        };
        for (var i = 0; i < invoiceSample.Length; i++) SetCellValue(sample.Cell(4, i + 1), invoiceSample[i]);

        WriteHeaders(sample, 7, lineHeaders);
        var lineSample = new object?[]
        {
            "F-0001", "1234567890123", "کالای نمونه", 1, 100000, 0, 10, "شرح نمونه"
        };
        for (var i = 0; i < lineSample.Length; i++) SetCellValue(sample.Cell(8, i + 1), lineSample[i]);
        ApplyColumnWidths(sample, lineHeaders);
        sample.Range(4, 1, 4, invoiceHeaders.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#E2F0D9");
        sample.Range(8, 1, 8, lineHeaders.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#E2F0D9");
        sample.Column(1).Style.NumberFormat.Format = "@";
        sample.Column(2).Style.NumberFormat.Format = "@";
        sample.Column(5).Style.NumberFormat.Format = "@";
        sample.Column(6).Style.NumberFormat.Format = "@";
        sample.Row(3).Height = 34;
        sample.Row(7).Height = 34;

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    public async Task<ApiModel> ImportAsync(int cpInfoId, Stream excelStream)
    {
        if (cpInfoId <= 0) return Error(400, "ابتدا یک خدمات‌دهندهٔ مودیان انتخاب کنید.");
        try
        {
            using var workbook = OpenWorkbook(excelStream);
            var invoiceSheet = GetSheet(workbook, "فاکتورها");
            var lineSheet = GetSheet(workbook, "اقلام");
            var invoiceLastRow = GetDataLastRow(invoiceSheet, MaxInvoices, "فاکتورها");
            var lineLastRow = GetDataLastRow(lineSheet, MaxInvoiceLines, "اقلام");
            var invoiceColumns = ResolveColumns(invoiceSheet, InvoiceColumns);
            var lineColumns = ResolveColumns(lineSheet, LineColumns);

            // این سرویس، cpInfoId را با شرکت فعال کاربر تطبیق می‌دهد؛ پیش از هر خواندن/نوشتن مستقیم DB.
            var scopeCheck = await connectService.NextInvoiceNumberAsync(cpInfoId);
            if (!scopeCheck.Success) return scopeCheck;

            var goods = await context.GoodsOrServices.AsNoTracking()
                .Where(g => g.CpInfo == cpInfoId && !g.IsDelete)
                .OrderBy(g => g.Name)
                .ToListAsync();
            var customers = await context.Customers.AsNoTracking()
                .Where(c => c.CpInfoId == cpInfoId && !c.IsDelete)
                .OrderBy(c => c.Name)
                .ToListAsync();
            var fiscalYears = await context.FiscalYears.AsNoTracking()
                .Where(f => f.CpInfo == cpInfoId && !f.IsDelete)
                .OrderBy(f => f.StartDate)
                .ToListAsync();
            var existingReferences = (await context.Invoices.AsNoTracking()
                    .Where(i => i.CpInfo == cpInfoId && i.ImportReference != null)
                    .Select(i => i.ImportReference!)
                    .ToListAsync())
                .Select(CanonicalizeImportReference)
                .Where(reference => reference.Length > 0)
                .ToHashSet(StringComparer.Ordinal);

            var result = new MoadianExcelImportResult();
            var failedKeys = new HashSet<string>(StringComparer.Ordinal);
            var failedUnkeyedHeaders = 0;
            var orphanKeys = new HashSet<string>(StringComparer.Ordinal);
            var orphanUnkeyedLines = 0;
            var headerRows = new List<HeaderRow>();

            for (var rowNumber = 2; rowNumber <= invoiceLastRow; rowNumber++)
            {
                var row = invoiceSheet.Row(rowNumber);
                if (row.IsEmpty()) continue;
                var header = new HeaderRow { RowNumber = rowNumber };
                headerRows.Add(header);
                try
                {
                    header.ImportReference = NormalizeImportReference(ReadText(row, invoiceColumns, "key"));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    header.Invalid = true;
                    failedUnkeyedHeaders++;
                    AddError(result, "فاکتورها", rowNumber, null, ex.Message);
                    continue;
                }
            }

            if (headerRows.Count == 0)
                throw new InvalidDataException("در برگهٔ «فاکتورها» سطر صورتحسابی وارد نشده است.");

            var duplicateKeys = headerRows.Where(h => h.ImportReference is not null)
                .GroupBy(h => h.ImportReference!, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var duplicateKey in duplicateKeys)
            {
                failedKeys.Add(duplicateKey);
                foreach (var duplicate in headerRows.Where(h => h.ImportReference == duplicateKey))
                {
                    duplicate.Invalid = true;
                    AddError(result, "فاکتورها", duplicate.RowNumber, duplicateKey,
                        "کد فاکتور در فایل در این برگه تکراری است؛ برای هر فاکتور یک کد یکتا وارد کنید.");
                }
            }

            var uniqueHeaders = headerRows.Where(h => h.ImportReference is not null && !duplicateKeys.Contains(h.ImportReference))
                .ToDictionary(h => h.ImportReference!, StringComparer.Ordinal);

            foreach (var header in uniqueHeaders.Values)
            {
                var key = header.ImportReference!;
                if (existingReferences.Contains(key))
                    FailHeader(result, header, failedKeys, "این کد فاکتور قبلاً برای همین خدمات‌دهنده وارد شده است؛ برای ثبت دوباره از کد جدید استفاده کنید.");

                var row = invoiceSheet.Row(header.RowNumber);
                try { header.DateOrder = ParseInvoiceDate(row.Cell(invoiceColumns["date"])); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { FailHeader(result, header, failedKeys, ex.Message); }

                try { header.TypeCode = ParseInvoiceType(ReadText(row, invoiceColumns, "type")); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { FailHeader(result, header, failedKeys, ex.Message); }

                try { header.PaymentType = ParsePaymentType(ReadText(row, invoiceColumns, "payment")); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { FailHeader(result, header, failedKeys, ex.Message); }

                var nationalId = NormalizeIdentity(ReadText(row, invoiceColumns, "nationalId"));
                var economicNumber = NormalizeIdentity(ReadText(row, invoiceColumns, "economic"));
                var customerName = ReadText(row, invoiceColumns, "customerName").Trim();
                header.Description = EmptyToNull(ReadText(row, invoiceColumns, "description"));

                if (header.DateOrder is { } invoiceDate)
                {
                    var years = fiscalYears.Where(f =>
                    {
                        var from = DateOnly.FromDateTime(f.StartDate);
                        var to = DateOnly.FromDateTime(f.EndDate);
                        return from <= invoiceDate && invoiceDate <= to;
                    }).ToList();
                    if (years.Count == 1) header.FiscalYearId = years[0].Id;
                    else if (years.Count == 0)
                        FailHeader(result, header, failedKeys, "تاریخ فاکتور در بازهٔ هیچ سال مالی فعالِ این خدمات‌دهنده نیست.");
                    else
                        FailHeader(result, header, failedKeys, "تاریخ فاکتور در چند سال مالی قرار می‌گیرد؛ بازه‌های سال مالی را اصلاح کنید.");
                }

                if (header.TypeCode is 2 or 3)
                {
                    if (nationalId.Length > 0 || economicNumber.Length > 0 || customerName.Length > 0)
                        FailHeader(result, header, failedKeys, "در صورتحساب نوع دوم/سوم، اطلاعات مشتری باید خالی باشد.");
                    header.CustomerId = null;
                    if (header.PaymentType == "نسیه")
                        FailHeader(result, header, failedKeys, "صورتحساب نوع دوم/سوم فقط با نوع پرداخت «نقدی» ثبت می‌شود.");
                }
                else if (header.TypeCode == 1)
                {
                    try { header.CustomerId = ResolveCustomer(customers, nationalId, economicNumber, customerName); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    { FailHeader(result, header, failedKeys, ex.Message); }
                }
            }

            for (var rowNumber = 2; rowNumber <= lineLastRow; rowNumber++)
            {
                var row = lineSheet.Row(rowNumber);
                if (row.IsEmpty()) continue;

                string key;
                try { key = NormalizeImportReference(ReadText(row, lineColumns, "key")); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    orphanUnkeyedLines++;
                    AddError(result, "اقلام", rowNumber, null, ex.Message);
                    continue;
                }

                if (duplicateKeys.Contains(key))
                {
                    AddError(result, "اقلام", rowNumber, key,
                        "به‌علت تکراری‌بودن کد در برگهٔ فاکتورها، این قلم به یک فاکتور مشخص قابل انتساب نیست.");
                    continue;
                }
                if (!uniqueHeaders.TryGetValue(key, out var header))
                {
                    AddError(result, "اقلام", rowNumber, key, "سربرگ فاکتوری با این کد در برگهٔ «فاکتورها» پیدا نشد.");
                    if (!failedKeys.Contains(key)) orphanKeys.Add(key);
                    continue;
                }

                try
                {
                    var cpc = CompactText(ReadText(row, lineColumns, "cpc"));
                    if (cpc.Length == 0) throw new InvalidOperationException("شناسه CPC کالا/خدمت الزامی است.");
                    var goodsName = ReadText(row, lineColumns, "goodsName").Trim();
                    var goodsMatches = goods.Where(g => NormalizeKey(g.UniqueIdentifier) == NormalizeKey(cpc)).ToList();
                    if (goodsName.Length > 0)
                        goodsMatches = goodsMatches.Where(g => NormalizeKey(g.Name) == NormalizeKey(goodsName)).ToList();
                    if (goodsMatches.Count == 0)
                        throw new InvalidOperationException($"کالا/خدمت با شناسهٔ CPC «{cpc}» در اطلاعات پایهٔ فعال این خدمات‌دهنده پیدا نشد.");
                    if (goodsMatches.Count > 1)
                        throw new InvalidOperationException($"شناسهٔ CPC «{cpc}» برای چند کالا/خدمت ثبت شده است؛ نام دقیق کالا/خدمت را هم وارد کنید.");
                    var selectedGoods = goodsMatches[0];

                    var count = ReadDecimal(row.Cell(lineColumns["count"]), "تعداد", required: true);
                    if (count <= 0) throw new InvalidOperationException("تعداد باید بیشتر از صفر باشد.");
                    if (decimal.Round(count, 2) != count)
                        throw new InvalidOperationException("تعداد حداکثر می‌تواند دو رقم اعشار داشته باشد.");

                    var price = ReadDecimal(row.Cell(lineColumns["price"]), "فی واحد", required: true);
                    if (price <= 0) throw new InvalidOperationException("فی واحد باید بیشتر از صفر باشد.");
                    if (decimal.Truncate(price) != price)
                        throw new InvalidOperationException("فی واحد باید به ریال صحیح و بدون اعشار وارد شود.");

                    var discount = ReadOptionalDecimal(row, lineColumns, "discount", "تخفیف کل ردیف") ?? 0m;
                    if (discount < 0) throw new InvalidOperationException("تخفیف ردیف نمی‌تواند منفی باشد.");
                    if (decimal.Truncate(discount) != discount)
                        throw new InvalidOperationException("تخفیف باید به ریال صحیح و بدون اعشار وارد شود.");
                    var gross = checked(count * price);
                    if (discount > gross) throw new InvalidOperationException("تخفیف کل ردیف نباید از مبلغ تعداد × فی واحد بیشتر باشد.");

                    var rate = ReadOptionalRate(row, lineColumns, "vat") ?? selectedGoods.ValueAddedPercentage;
                    if (rate < 0 || rate > 100 || decimal.Truncate(rate) != rate)
                        throw new InvalidOperationException("نرخ ارزش افزوده باید عدد صحیح بین ۰ تا ۱۰۰ باشد.");

                    var net = gross - discount;
                    var vatAmount = decimal.Truncate(net * rate / 100m);
                    var total = decimal.Truncate(net) + vatAmount;
                    header.Items.Add(new VmInvoiceItems
                    {
                        GoodsOrServicesId = selectedGoods.Id,
                        UniqueIdentifier = selectedGoods.UniqueIdentifier,
                        Name = selectedGoods.Name,
                        UnitOfMeasurement = selectedGoods.UnitOfMeasurement,
                        Count = count,
                        PriceUnit = price,
                        Discount = discount,
                        ValueAddedPercentage = (double)rate,
                        DescriptionKala = EmptyToNull(ReadText(row, lineColumns, "description")),
                        ValueAddedAmount = vatAmount,
                        CalculatedPay = total,
                        TotalPrice = total
                    });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    header.HasInvalidLine = true;
                    AddError(result, "اقلام", rowNumber, key, ex.Message);
                }
            }

            foreach (var header in uniqueHeaders.Values)
            {
                if (header.Items.Count == 0)
                    FailHeader(result, header, failedKeys, "برای این فاکتور هیچ قلم معتبری در برگهٔ «اقلام» ثبت نشده است.");
                else if (header.HasInvalidLine)
                {
                    header.Invalid = true;
                    failedKeys.Add(header.ImportReference!);
                    AddError(result, "فاکتورها", header.RowNumber, header.ImportReference,
                        "این صورتحساب به‌علت وجود قلم نامعتبر، به‌صورت کامل ثبت نشد.");
                }
            }

            var ready = uniqueHeaders.Values
                .Where(h => !h.Invalid && !h.HasInvalidLine && h.Items.Count > 0)
                .OrderBy(h => h.RowNumber)
                .ToList();

            for (var i = 0; i < ready.Count; i++)
            {
                var header = ready[i];
                var grossTotal = header.Items.Sum(item => (item.Count ?? 0) * (item.PriceUnit ?? 0));
                var netTotal = grossTotal - header.Items.Sum(item => item.Discount ?? 0);
                var vatTotal = header.Items.Sum(item => item.ValueAddedAmount ?? 0);
                var total = header.Items.Sum(item => item.CalculatedPay ?? 0);
                var invoice = new InvoiceVM
                {
                    CPInfoId = cpInfoId,
                    IsNew = true,
                    DateOrder = header.DateOrder,
                    FiscalYearId = header.FiscalYearId?.ToString(CultureInfo.InvariantCulture),
                    TypePattern = header.TypeCode switch { 2 => "نوع دوم", 3 => "نوع سوم", _ => "نوع اول" },
                    PaymentType = header.PaymentType,
                    CustomerId = header.CustomerId,
                    Description = header.Description,
                    StatusSentToSystem = "0",
                    StatusSentToSystemInt = "0",
                    Subject = "اصلی",
                    ShowButtons = true,
                    ImportReference = header.ImportReference,
                    InvoiceItems = new ObservableCollection<VmInvoiceItems>(header.Items),
                    TotallMoney = total,
                    PriceNaghd = header.PaymentType == "نقدی" ? total : null,
                    PriceNesye = header.PaymentType == "نسیه" ? total : null,
                    TotallValueAddedPercentage = netTotal == 0 ? 0 : (double)(vatTotal / netTotal * 100m)
                };

                var saved = await connectService.SabtInvoiceAsync(invoice);
                if (saved.Success)
                {
                    result.Imported++;
                    context.ChangeTracker.Clear();
                    continue;
                }

                failedKeys.Add(header.ImportReference!);
                AddError(result, "فاکتورها", header.RowNumber, header.ImportReference,
                    "ثبت صورتحساب انجام نشد: " + (saved.Message ?? "خطای نامشخص سرور"));
                context.ChangeTracker.Clear();

                if (saved.Status >= 500)
                {
                    for (var remaining = i + 1; remaining < ready.Count; remaining++)
                    {
                        var unattempted = ready[remaining];
                        failedKeys.Add(unattempted.ImportReference!);
                        AddError(result, "فاکتورها", unattempted.RowNumber, unattempted.ImportReference,
                            "به‌علت خطای سرور در ثبت فاکتور قبلی، این فاکتور پردازش نشد؛ فایل را دوباره بررسی و بارگذاری کنید.");
                    }
                    break;
                }
            }

            result.Failed = failedKeys.Count + failedUnkeyedHeaders + orphanKeys.Count + orphanUnkeyedLines;
            return Success(result);
        }
        catch (InvalidDataException ex)
        {
            return Error(400, ex.Message);
        }
        catch (Exception ex)
        {
            return Error(500, "خطا در پردازش فایل اکسل صورتحساب: " + ex.Message);
        }
    }

    private static void FailHeader(MoadianExcelImportResult result, HeaderRow header, HashSet<string> failedKeys, string message)
    {
        header.Invalid = true;
        if (header.ImportReference is not null) failedKeys.Add(header.ImportReference);
        AddError(result, "فاکتورها", header.RowNumber, header.ImportReference, message);
    }

    private static void AddError(MoadianExcelImportResult result, string sheetName, int row, string? key, string message)
    {
        if (result.Errors.Count < MaxErrorDetails)
            result.Errors.Add($"برگهٔ «{sheetName}»، سطر {Fa.Digits(row)}{(string.IsNullOrWhiteSpace(key) ? "" : $" (کد {key})")}: {message}");
        else
            result.ErrorDetailsOmitted++;
    }

    private static int ResolveCustomer(IReadOnlyList<Customer> customers, string nationalId, string economicNumber, string name)
    {
        if (nationalId.Length == 0 && economicNumber.Length == 0)
            throw new InvalidOperationException("برای صورتحساب نوع اول، کد ملی/شماره ثبت یا شماره اقتصادی مشتری الزامی است.");

        List<Customer>? matches = null;
        if (nationalId.Length > 0)
        {
            matches = customers.Where(c => NormalizeIdentity(c.NationalID) == nationalId).ToList();
            if (matches.Count == 0) throw new InvalidOperationException("مشتری با این کد ملی/شماره ثبت در اطلاعات پایهٔ همین خدمات‌دهنده پیدا نشد.");
        }
        if (economicNumber.Length > 0)
        {
            var economicMatches = customers.Where(c => NormalizeIdentity(c.EconomicNumber) == economicNumber).ToList();
            if (economicMatches.Count == 0) throw new InvalidOperationException("مشتری با این شماره اقتصادی در اطلاعات پایهٔ همین خدمات‌دهنده پیدا نشد.");
            matches = matches is null ? economicMatches : matches.Where(c => economicMatches.Any(e => e.Id == c.Id)).ToList();
            if (matches.Count == 0) throw new InvalidOperationException("کد ملی/شماره ثبت و شماره اقتصادی به دو مشتری متفاوت تعلق دارند.");
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            matches = matches!.Where(c => NormalizeKey(c.Name) == NormalizeKey(name)).ToList();
            if (matches.Count == 0) throw new InvalidOperationException("نام مشتری با شناسهٔ واردشده تطابق ندارد؛ نام را دقیقاً مطابق اطلاعات پایه وارد کنید.");
        }
        if (matches is null || matches.Count == 0)
            throw new InvalidOperationException("مشتری متناظر در اطلاعات پایه پیدا نشد.");
        if (matches.Count > 1)
            throw new InvalidOperationException("شناسهٔ مشتری مبهم است؛ نام دقیق مشتری را نیز وارد کنید.");

        var customer = matches[0];
        var type = NormalizeHeader(customer.Type);
        if (type is not ("حقیقی" or "حقوقی" or "شخصحقیقی" or "شخصحقوقی"))
            throw new InvalidOperationException("نوع شخص مشتری در اطلاعات پایه مشخص نیست؛ پیش از ورود، نوع «حقیقی» یا «حقوقی» را اصلاح کنید.");
        return customer.Id;
    }

    private static DateOnly ParseInvoiceDate(IXLCell cell)
    {
        if (cell.IsEmpty()) throw new InvalidOperationException("تاریخ فاکتور الزامی است.");
        if (cell.DataType == XLDataType.DateTime && cell.TryGetValue<DateTime>(out var excelDate))
            return DateOnly.FromDateTime(excelDate);

        var raw = Fa.ToEn(ReadCellText(cell)).Trim()
            .Replace('-', '/').Replace('.', '/').Replace('٫', '/');
        var parts = raw.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var month)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
            throw new InvalidOperationException($"تاریخ «{ReadCellText(cell)}» معتبر نیست؛ نمونهٔ قابل قبول ۱۴۰۵/۰۱/۱۵ است.");

        try
        {
            if (year is >= 1300 and <= 1499)
            {
                var persian = new PersianCalendar().ToDateTime(year, month, day, 0, 0, 0, 0);
                return DateOnly.FromDateTime(persian);
            }
            if (year is >= 1900 and <= 2100)
                return new DateOnly(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new InvalidOperationException($"تاریخ «{ReadCellText(cell)}» معتبر نیست.");
        }
        throw new InvalidOperationException($"سال تاریخ «{ReadCellText(cell)}» معتبر نیست؛ تاریخ را شمسی یا میلادی وارد کنید.");
    }

    private static int ParseInvoiceType(string? value)
    {
        var normalized = NormalizeHeader(value);
        if (normalized is "1" or "نوعاول" or "اول" || normalized.Contains("نوعاول", StringComparison.Ordinal)) return 1;
        if (normalized is "2" or "نوعدوم" or "دوم" or "نوع2" || normalized.Contains("نوعدوم", StringComparison.Ordinal)) return 2;
        if (normalized is "3" or "نوعسوم" or "سوم" or "نوع3" || normalized.Contains("نوعسوم", StringComparison.Ordinal)) return 3;
        throw new InvalidOperationException("نوع صورتحساب باید «نوع اول»، «نوع دوم» یا «نوع سوم» باشد.");
    }

    private static string ParsePaymentType(string? value)
    {
        var normalized = NormalizeHeader(value);
        if (normalized is "نقدی" or "cash") return "نقدی";
        if (normalized is "نسیه" or "اعتباری" or "credit") return "نسیه";
        throw new InvalidOperationException("نوع پرداخت باید «نقدی» یا «نسیه» باشد.");
    }

    private static decimal? ReadOptionalRate(IXLRow row, IReadOnlyDictionary<string, int> columns, string key)
    {
        if (!columns.TryGetValue(key, out var column) || row.Cell(column).IsEmpty()) return null;
        var cell = row.Cell(column);
        var raw = ReadCellText(cell).Replace("٪", "").Replace("%", "").Trim();
        if (raw.Length == 0) return null;
        if (cell.DataType == XLDataType.Number && cell.TryGetValue<decimal>(out var number)) return number;
        raw = Fa.ToEn(raw).Replace('٬', ',').Replace('٫', '.').Replace(",", "");
        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out number))
            throw new InvalidOperationException($"نرخ ارزش افزودهٔ «{ReadCellText(cell)}» عدد معتبر نیست.");
        return number;
    }

    private static decimal? ReadOptionalDecimal(IXLRow row, IReadOnlyDictionary<string, int> columns, string key, string caption)
        => columns.TryGetValue(key, out var column) ? ReadDecimal(row.Cell(column), caption, required: false) : null;

    private static decimal ReadDecimal(IXLCell cell, string caption, bool required)
    {
        if (cell.IsEmpty())
        {
            if (required) throw new InvalidOperationException($"{caption} الزامی است.");
            return 0;
        }
        if (cell.DataType == XLDataType.Number && cell.TryGetValue<decimal>(out var number)) return number;
        var raw = Fa.ToEn(cell.GetString()).Trim().Replace('٬', ',').Replace('٫', '.').Replace(",", "");
        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out number))
            throw new InvalidOperationException($"مقدار «{cell.GetString()}» در ستون {caption} عدد معتبر نیست.");
        return number;
    }

    private static string CanonicalizeImportReference(string? value)
        => Fa.ToEn((value ?? string.Empty).Normalize(NormalizationForm.FormKC).Trim()
            .Replace("ي", "ی").Replace("ك", "ک").Replace("\u200c", "")).ToUpperInvariant();

    private static string NormalizeImportReference(string? value)
    {
        var key = CanonicalizeImportReference(value);
        if (key.Length == 0) throw new InvalidOperationException("کد فاکتور در فایل الزامی است.");
        if (key.Length > 128) throw new InvalidOperationException("کد فاکتور در فایل نباید بیشتر از ۱۲۸ نویسه باشد.");
        if (key.Any(char.IsWhiteSpace) || key.Any(char.IsControl))
            throw new InvalidOperationException("کد فاکتور در فایل نباید فاصله یا نویسهٔ کنترلی داشته باشد.");
        return key;
    }

    private static string NormalizeIdentity(string? value)
        => new string(Fa.ToEn(value ?? "").Trim().Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '\u200c').ToArray()).ToUpperInvariant();

    private static string NormalizeKey(string? value)
    {
        var normalized = Fa.ToEn(value ?? "").Trim().Replace("ي", "ی").Replace("ك", "ک").Replace("\u200c", "");
        return new string(normalized.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
    }

    private static string NormalizeHeader(string? value)
    {
        var normalized = Fa.ToEn(value ?? "").Replace("ي", "ی").Replace("ك", "ک").Replace("ۀ", "ه").Replace("\u200c", "");
        return new string(normalized.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    private static string CompactText(string? value)
        => new string(Fa.ToEn(value ?? "").Trim().Where(c => !char.IsWhiteSpace(c) && c != '\u200c').ToArray());

    private static string ReadText(IXLRow row, IReadOnlyDictionary<string, int> columns, string key)
        => columns.TryGetValue(key, out var column) ? ReadCellText(row.Cell(column)) : "";

    private static string ReadCellText(IXLCell cell)
    {
        if (cell.IsEmpty()) return "";
        if (cell.DataType == XLDataType.Number && cell.TryGetValue<decimal>(out var number))
            return number.ToString("0.############################", CultureInfo.InvariantCulture).Trim();
        return cell.GetString().Trim();
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Dictionary<string, int> ResolveColumns(IXLWorksheet sheet, IReadOnlyList<ColumnSpec> specs)
    {
        var headers = new Dictionary<string, int>(StringComparer.Ordinal);
        var headerRow = sheet.Row(1);
        var lastColumn = Math.Min(100, Math.Max(1, headerRow.LastCellUsed()?.Address.ColumnNumber ?? 1));
        for (var column = 1; column <= lastColumn; column++)
        {
            var raw = ReadCellText(headerRow.Cell(column));
            var normalized = NormalizeHeader(raw);
            if (normalized.Length == 0) continue;
            if (!headers.TryAdd(normalized, column))
                throw new InvalidDataException($"عنوان ستون «{raw}» در برگهٔ «{sheet.Name}» تکراری است.");
        }

        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var spec in specs)
        {
            var matches = spec.Aliases.Select(NormalizeHeader)
                .Where(headers.ContainsKey)
                .Select(alias => headers[alias])
                .Distinct()
                .ToList();
            if (matches.Count > 1)
                throw new InvalidDataException($"بیش از یک ستون برای «{spec.Caption}» در برگهٔ «{sheet.Name}» پیدا شد؛ فقط یک ستون نگه دارید.");
            if (matches.Count == 1) result[spec.Key] = matches[0];
            if (spec.Required && !result.ContainsKey(spec.Key))
                throw new InvalidDataException($"ستون الزامی «{spec.Caption}» در برگهٔ «{sheet.Name}» پیدا نشد؛ از قالب نمونه استفاده کنید.");
        }
        return result;
    }

    private static XLWorkbook OpenWorkbook(Stream stream)
    {
        try { return new XLWorkbook(stream); }
        catch (Exception ex)
        {
            throw new InvalidDataException("فایل اکسل قابل خواندن نیست؛ یک فایل معتبر با فرمت xlsx انتخاب کنید.", ex);
        }
    }

    private static IXLWorksheet GetSheet(XLWorkbook workbook, string name)
        => workbook.Worksheets.FirstOrDefault(s => NormalizeHeader(s.Name) == NormalizeHeader(name))
           ?? throw new InvalidDataException($"برگهٔ «{name}» در فایل پیدا نشد؛ از قالب نمونه استفاده کنید.");

    private static int GetDataLastRow(IXLWorksheet sheet, int maxRows, string label)
    {
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        if (lastRow < 2) throw new InvalidDataException($"برگهٔ «{label}» داده‌ای ندارد؛ اطلاعات باید از سطر دوم شروع شود.");
        if (lastRow - 1 > maxRows)
            throw new InvalidDataException($"حداکثر {Fa.Digits(maxRows)} سطر داده در برگهٔ «{label}» پذیرفته می‌شود.");
        return lastRow;
    }

    private static void SetCellValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null: cell.Clear(); break;
            case string text: cell.Value = text; break;
            case int integer: cell.Value = integer; break;
            case decimal amount: cell.Value = amount; break;
            case double ratio: cell.Value = ratio; break;
            default: cell.Value = value.ToString() ?? ""; break;
        }
    }

    private static void WriteHeaders(IXLWorksheet sheet, int row, IReadOnlyList<string> headers)
    {
        for (var column = 1; column <= headers.Count; column++)
        {
            var cell = sheet.Cell(row, column);
            cell.Value = headers[column - 1];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEBF7");
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.WrapText = true;
        }
        sheet.Row(row).Height = 34;
    }

    private static void ApplyColumnWidths(IXLWorksheet sheet, IReadOnlyList<string> headers)
    {
        for (var column = 1; column <= headers.Count; column++)
            sheet.Column(column).Width = Math.Clamp(headers[column - 1].Length + 8, 18, 42);
    }

    private static ApiModel Success(MoadianExcelImportResult result) => new()
    {
        Data = result,
        Status = 200,
        Success = true,
        Message = "پردازش فایل اکسل صورتحساب انجام شد."
    };

    private static ApiModel Error(int status, string message) => new()
    {
        Data = null,
        Status = status,
        Success = false,
        Message = message
    };
}
