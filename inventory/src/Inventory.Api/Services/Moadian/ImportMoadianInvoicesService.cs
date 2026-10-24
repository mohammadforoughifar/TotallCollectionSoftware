using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Enums;
using OfficeOpenXml;

namespace Inventory.Api.Services.Moadian
{
    /// <summary>
    /// سرویس Import فاکتورهای قدیمی سامانه مودیان به دیتابیس جدید (FacInvoice / FacInvoiceLine)
    ///
    ///ستون‌های مورد نیاز اکسل (سطح فاکتور - هر سطر یک فاکتور کامل):
    ///  A: نوع صورتحساب (فروش/خرید/برگشت)
    ///  B: وضعیت صورتحساب (پیش‌نویس/قطعی/ابطال)
    ///  C: وضعیت ارسال
    ///  D: تاریخ ثبت صورتحساب (YYYY/MM/DD یا عدد سریالی اکسل)
    ///  E: شناسه مالیاتی (کد ملی/شناسه اقتصادی مشتری)
    ///  F: مبلغ کالا (مجموع ناخالص)
    ///  G: تخفیف کالا
    ///  H: مالیات کالا (مالیت بر ارزش افزوده)
    ///  I: مبلغ نقد (تسویه نقدی)
    ///  J: مبلغ نسیه (تسویه نسیه)
    ///  K: مبلغ صورتحساب (ریال - جمع نهایی)
    ///
    /// هر سطر = یک فاکتور کامل با یک قلم داخلی.
    /// </summary>
    public class ImportMoadianInvoicesService
    {
        private readonly AppDbContext _db;
        private readonly int _defaultCompanyId;
        private readonly int _defaultWarehouseId;

        public ImportMoadianInvoicesService(AppDbContext db, int defaultCompanyId, int defaultWarehouseId)
        {
            _db = db;
            _defaultCompanyId = defaultCompanyId;
            _defaultWarehouseId = defaultWarehouseId;
        }

        /// <summary>
        /// Import فاکتورها از فایل اکسل
        /// </summary>
        /// <param name="excelFilePath">مسیر فایل اکسل</param>
        /// <param name="createdBy">کاربر واردکننده</param>
        /// <returns>تعداد فاکتورهای import شده</returns>
        public async Task<int> ImportFromExcelAsync(string excelFilePath, string createdBy)
        {
            if (!File.Exists(excelFilePath))
                throw new FileNotFoundException("فایل اکسل پیدا نشد.", excelFilePath);

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using var package = new ExcelPackage(new FileInfo(excelFilePath));
            var worksheet = package.Workbook.Worksheets.FirstOrDefault();
            if (worksheet == null)
                throw new InvalidOperationException("ورقه‌ای در فایل اکسل یافت نشد.");

            var rowCount = worksheet.Dimension.Rows;
            if (rowCount < 2)
                return 0;

            // خواندن هدر (ستون‌ها) - شناسایی خودکار بر اساس نام ستون
            var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var col = 1; col <= worksheet.Dimension.Columns; col++)
            {
                var headerText = worksheet.Cells[1, col].Text?.Trim();
                if (!string.IsNullOrWhiteSpace(headerText))
                    headers[headerText] = col;
            }

            // اعتبارسنجی ستون‌های الزامی
            var requiredHeaders = new[]
            {
                "نوع صورتحساب", "وضعیت صورتحساب", "تاریخ ثبت صورتحساب",
                "شناسه مالیاتی", "مبلغ کالا", "مبلغ صورتحساب (ریال)"
            };
            foreach (var req in requiredHeaders)
            {
                if (!headers.ContainsKey(req))
                    throw new InvalidOperationException($"ستون الزامی '{req}' در فایل اکسل یافت نشد.");
            }

            var importedCount = 0;
            var skippedCount = 0;

            for (var row = 2; row <= rowCount; row++)
            {
                var invoiceType = GetCellValue(worksheet, row, headers, "نوع صورتحساب")?.Trim();
                if (string.IsNullOrWhiteSpace(invoiceType))
                    continue;

                var statusText = GetCellValue(worksheet, row, headers, "وضعیت صورتحساب")?.Trim();
                var sendStatus = GetCellValue(worksheet, row, headers, "وضعیت ارسال")?.Trim();
                var dateText = GetCellValue(worksheet, row, headers, "تاریخ ثبت صورتحساب")?.Trim();
                var taxId = GetCellValue(worksheet, row, headers, "شناسه مالیاتی")?.Trim();

                var goodsAmount = ParseDecimal(GetCellValue(worksheet, row, headers, "مبلغ کالا"));
                var goodsDiscount = ParseDecimal(GetCellValue(worksheet, row, headers, "تخفیف کالا"));
                var goodsVat = ParseDecimal(GetCellValue(worksheet, row, headers, "مالیات کالا"));
                var cashAmount = ParseDecimal(GetCellValue(worksheet, row, headers, "مبلغ نقد"));
                var creditAmount = ParseDecimal(GetCellValue(worksheet, row, headers, "مبلغ نسیه"));
                var totalAmount = ParseDecimal(GetCellValue(worksheet, row, headers, "مبلغ صورتحساب (ریال)"));

                // چک کردن تکراری نبودن فاکتور (بر اساس تاریخ + مبلغ + شناسه مالیاتی)
                var invoiceDate = ParseDate(dateText) ?? DateTime.Now;
                var existingInvoice = await _db.FacInvoices
                    .FirstOrDefaultAsync(f =>
                        f.Party.TaxId == taxId &&
                        f.Date == invoiceDate &&
                        f.TotalNet == totalAmount);
                if (existingInvoice != null)
                {
                    skippedCount++;
                    continue;
                }

                // ایجاد/获取 مشتری بر اساس شناسه مالیاتی
                var party = await GetOrCreatePartyAsync(taxId);

                // تبدیل نوع صورتحساب به InvoiceKind
                var kind = ParseInvoiceKind(invoiceType);

                // تبدیل وضعیت صورتحساب به InvoiceStatus
                var status = ParseInvoiceStatus(statusText);

                // Determine settlement type
                var settlement = cashAmount > creditAmount ? SettlementType.Cash : SettlementType.Credit;

                // محاسبه مبالغ
                var totalGross = goodsAmount;
                var totalDiscount = goodsDiscount;
                var totalTaxable = goodsAmount - goodsDiscount;
                var totalVat = goodsVat;
                var totalNet = totalAmount;

                var newInvoice = new FacInvoice
                {
                    Number = ParseInvoiceNumberFromDateAndTax(taxId, invoiceDate),
                    RefNumber = $"{taxId}_{invoiceDate:yyyyMMdd}",
                    Kind = kind,
                    Date = invoiceDate,
                    DueDate = null,
                    PartyId = party.Id,
                    WarehouseId = _defaultWarehouseId,
                    Settlement = settlement,
                    Description = $"Imported from Moadian — SendStatus: {sendStatus}",
                    Status = status,
                    TotalGross = totalGross,
                    TotalLineDiscount = totalDiscount,
                    InvoiceDiscount = 0,
                    TotalTaxable = totalTaxable,
                    TotalVat = totalVat,
                    ShippingCost = 0,
                    TotalNet = totalNet,
                    SettledAmount = cashAmount + creditAmount,
                    RemainingAmount = totalNet - (cashAmount + creditAmount),
                    CreatedBy = createdBy,
                    CreatedAt = DateTime.Now,
                    Lines = new List<FacInvoiceLine>()
                };

                // یک قلم داخلی برای کل مبلغ فاکتور
                var lineGross = goodsAmount;
                var lineDiscount = goodsDiscount;
                var lineTaxable = goodsAmount - goodsDiscount;
                var lineVat = goodsVat;
                var lineTotal = totalAmount;

                newInvoice.Lines.Add(new FacInvoiceLine
                {
                    InvoiceId = newInvoice.Id,
                    RowNo = 1,
                    ProductId = 0, // 제품 bilanید نشده — باید در UI تکمیل شود
                    ProductCode = "IMPORTED",
                    ProductName = "کالای واردشده از مودیان",
                    Unit = "",
                    TaxCode = "",
                    Quantity = 1,
                    UnitPrice = lineGross,
                    DiscountPercent = lineGross > 0 ? (lineDiscount / lineGross) * 100 : 0,
                    Discount = lineDiscount,
                    VatRate = lineTaxable > 0 ? (lineVat / lineTaxable) * 100 : 9,
                    VatAmount = lineVat,
                    Taxable = lineTaxable,
                    Total = lineTotal,
                    Description = $"Imported from Moudian — {invoiceType}",
                    CurrentStock = 0
                });

                _db.FacInvoices.Add(newInvoice);
                await _db.SaveChangesAsync();
                importedCount++;
            }

            return importedCount;
        }

        #region Helper Methods

        private static string? GetCellValue(ExcelWorksheet sheet, int row, Dictionary<string, int> headers, string headerName)
        {
            if (!headers.TryGetValue(headerName, out var col))
                return null;
            return sheet.Cells[row, col].Text?.Trim();
        }

        private static DateTime? ParseDate(string? dateText)
        {
            if (string.IsNullOrWhiteSpace(dateText))
                return null;

            // تلاش برای پارس تاریخ فارسی (مثلاً 1405/06/31)
            if (dateText.Contains("/"))
            {
                var parts = dateText.Split('/');
                if (parts.Length == 3 &&
                    int.TryParse(parts[0], out var year) &&
                    int.TryParse(parts[1], out var month) &&
                    int.TryParse(parts[2], out var day))
                {
                    var gregorianYear = year + 621;
                    try
                    {
                        return new DateTime(gregorianYear, month, day);
                    }
                    catch { return null; }
                }
            }

            // تلاش برای پارس عددی (سریالی اکسل)
            if (double.TryParse(dateText, out var excelSerial))
            {
                try
                {
                    return DateTime.FromOADate(excelSerial);
                }
                catch { return null; }
            }

            // تلاش برای پارس استاندارد
            if (DateTime.TryParse(dateText, out var parsed))
                return parsed;

            return null;
        }

        private static decimal ParseDecimal(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            var cleaned = value.Replace(",", "").Replace("،", "").Replace(" ", "");
            return decimal.TryParse(cleaned, out var result) ? result : 0;
        }

        private static string ParseInvoiceNumberFromDateAndTax(string? taxId, DateTime date)
        {
            var taxDigits = new string((taxId ?? "").Where(char.IsDigit).ToArray());
            return $"{date:yyyyMMdd}{taxDigits}";
        }

        private static InvoiceKind ParseInvoiceKind(string? typeText)
        {
            if (string.IsNullOrWhiteSpace(typeText))
                return InvoiceKind.Sale;

            var lower = typeText.Trim().ToLowerInvariant();
            if (lower.Contains("خرید") || lower.Contains("purchase"))
                return InvoiceKind.Purchase;
            if (lower.Contains("برگشت از خرید"))
                return InvoiceKind.PurchaseReturn;
            if (lower.Contains("برگشت از فروش"))
                return InvoiceKind.SaleReturn;
            return InvoiceKind.Sale; // پیش‌فرض فروش
        }

        private static InvoiceStatus ParseInvoiceStatus(string? statusText)
        {
            if (string.IsNullOrWhiteSpace(statusText))
                return InvoiceStatus.Draft;

            var lower = statusText.Trim().ToLowerInvariant();
            if (lower.Contains("قطعی") || lower.Contains("confirmed"))
                return InvoiceStatus.Confirmed;
            if (lower.Contains("ابطال") || lower.Contains("cancel"))
                return InvoiceStatus.Cancelled;
            return InvoiceStatus.Draft; // پیش‌فرض پیش‌نویس
        }

        private async Task<Party> GetOrCreatePartyAsync(string? taxId)
        {
            if (string.IsNullOrWhiteSpace(taxId))
                taxId = "UNKNOWN";

            var party = await _db.Parties
                .FirstOrDefaultAsync(p => p.TaxId == taxId);

            if (party != null)
                return party;

            party = new Party
            {
                Name = $"مشتری {taxId}",
                TaxId = taxId,
                Type = PartyType.Customer,
                CompanyId = _defaultCompanyId,
                IsActive = true
            };
            _db.Parties.Add(party);
            await _db.SaveChangesAsync();
            return party;
        }

        #endregion
    }
}
