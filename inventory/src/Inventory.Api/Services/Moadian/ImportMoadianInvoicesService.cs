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
    ///ستون‌های مورد نیاز اکسل:
    ///  A: InvoiceNumber (شماره فاکتور)
    ///  B: DateOrder (تاریخ فاکتور - YYYY/MM/DD یا عدد سریالی اکسل)
    ///  C: CustomerName (نام مشتری)
    ///  D: CustomerNationalID (کد ملی/شناسه اقتصادی مشتری)
    ///  E: ProductName (نام کالا/خدمت)
    ///  F: ProductCode (کد کالا/شناسه یکتا)
    ///  G: Quantity (تعداد)
    ///  H: UnitPrice (فی واحد)
    ///  I: DiscinctPercent (درصد تخفیف - اختیاری)
    ///  J: VatRate (نرخ مالیات % - اختیاری، پیش‌فرض ۹)
    ///  K: Description (توضیحات - اختیاری)
    ///
    /// هر سطر = یک قلم فاکتور. فاکتورها بر اساس InvoiceNumber گروه‌بندی می‌شوند.
    /// </summary>
    public class ImportMoadianInvoicesService
    {
        private readonly AppDbContext _db;
        private readonly int _defaultCompanyId;

        public ImportMoadianInvoicesService(AppDbContext db, int defaultCompanyId)
        {
            _db = db;
            _defaultCompanyId = defaultCompanyId;
        }

        /// <summary>
        /// Import فاکتورها از فایل اکسل
        /// </summary>
        /// <param name="excelFilePath">مسیر فایل اکسل</param>
        /// <param name="createdBy">کاربر واردکننده</param>
        /// <param name="warehouseId">انبار پیش‌فرض برای فاکتورهای importing شده</param>
        /// <returns>تعداد فاکتورهای import شده</returns>
        public async Task<int> ImportFromExcelAsync(string excelFilePath, string createdBy, int warehouseId)
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

            // خواندن هدر (ستون‌ها)
            var headers = new Dictionary<string, int>();
            for (var col = 1; col <= worksheet.Dimension.Columns; col++)
            {
                var headerText = worksheet.Cells[1, col].Text?.Trim();
                if (!string.IsNullOrWhiteSpace(headerText))
                    headers[headerText.ToLowerInvariant()] = col;
            }

            // اعتبارسنجی ستون‌های الزامی
            var requiredHeaders = new[] { "invoicenumber", "dateorder", "customername", "productname", "quantity", "unitprice" };
            foreach (var req in requiredHeaders)
            {
                if (!headers.ContainsKey(req))
                    throw new InvalidOperationException($"ستون الزامی '{req}' در فایل اکسل یافت نشد.");
            }

            // گروه‌بندی سطرها بر اساس InvoiceNumber
            var invoiceGroups = new Dictionary<string, List<InvoiceRowData>>();

            for (var row = 2; row <= rowCount; row++)
            {
                var invoiceNumber = GetCellValue(worksheet, row, headers, "InvoiceNumber");
                if (string.IsNullOrWhiteSpace(invoiceNumber))
                    continue;

                var rowData = new InvoiceRowData
                {
                    InvoiceNumber = invoiceNumber.Trim(),
                    DateOrder = ParseDate(GetCellValue(worksheet, row, headers, "DateOrder")),
                    CustomerName = GetCellValue(worksheet, row, headers, "CustomerName") ?? "",
                    CustomerNationalId = GetCellValue(worksheet, row, headers, "CustomerNationalID"),
                    ProductName = GetCellValue(worksheet, row, headers, "ProductName") ?? "",
                    ProductCode = GetCellValue(worksheet, row, headers, "ProductCode"),
                    Quantity = ParseDecimal(GetCellValue(worksheet, row, headers, "Quantity")),
                    UnitPrice = ParseDecimal(GetCellValue(worksheet, row, headers, "UnitPrice")),
                    DiscountPercent = ParseDecimal(GetCellValue(worksheet, row, headers, "DiscountPercent")),
                    VatRate = ParseDecimal(GetCellValue(worksheet, row, headers, "VatRate")) != 0
                        ? ParseDecimal(GetCellValue(worksheet, row, headers, "VatRate"))
                        : 9, // پیش‌فرض ۹٪ برای ایران
                    Description = GetCellValue(worksheet, row, headers, "Description")
                };

                if (!invoiceGroups.ContainsKey(rowData.InvoiceNumber))
                    invoiceGroups[rowData.InvoiceNumber] = new List<InvoiceRowData>();

                invoiceGroups[rowData.InvoiceNumber].Add(rowData);
            }

            var importedCount = 0;

            foreach (var group in invoiceGroups)
            {
                var invoiceNumber = group.Key;
                var rows = group.Value;

                // چک کردن تکراری نبودن فاکتور (بر اساس RefNumber)
                var existingInvoice = await _db.FacInvoices
                    .FirstOrDefaultAsync(f => f.RefNumber == invoiceNumber);
                if (existingInvoice != null)
                    continue;

                // ایجاد/获取 مشتری (PartyType.Customer برای فاکتور فروش)
                var party = await GetOrCreatePartyAsync(rows.First().CustomerName, rows.First().CustomerNationalId);

                // محاسبه مبالغ فاکتور
                var totalGross = rows.Sum(r => r.Quantity * r.UnitPrice);
                var totalDiscount = rows.Sum(r => (r.Quantity * r.UnitPrice) * (r.DiscountPercent / 100));
                var totalTaxable = totalGross - totalDiscount;
                var totalVat = rows.Sum(r => ((r.Quantity * r.UnitPrice) * (1 - r.DiscountPercent / 100)) * (r.VatRate / 100));
                var totalNet = totalTaxable + totalVat;

                var newInvoice = new FacInvoice
                {
                    Number = ParseInvoiceNumber(invoiceNumber),
                    RefNumber = invoiceNumber,
                    Kind = InvoiceKind.Sale,
                    Date = rows.First().DateOrder ?? DateTime.Now,
                    PartyId = party.Id,
                    WarehouseId = warehouseId,
                    Settlement = SettlementType.Credit,
                    Description = $"Imported from Moadian system — Invoice {invoiceNumber}",
                    Status = InvoiceStatus.Confirmed,
                    TotalGross = totalGross,
                    TotalLineDiscount = totalDiscount,
                    InvoiceDiscount = 0,
                    TotalTaxable = totalTaxable,
                    TotalVat = totalVat,
                    ShippingCost = 0,
                    TotalNet = totalNet,
                    SettledAmount = 0,
                    RemainingAmount = totalNet,
                    CreatedBy = createdBy,
                    CreatedAt = DateTime.Now,
                    Lines = new List<FacInvoiceLine>()
                };

                var rowNo = 1;
                foreach (var row in rows)
                {
                    // ایجاد/获取 کالا
                    var product = await GetOrCreateProductAsync(row.ProductName, row.ProductCode, row.VatRate);

                    var lineGross = row.Quantity * row.UnitPrice;
                    var lineDiscount = lineGross * (row.DiscountPercent / 100);
                    var lineTaxable = lineGross - lineDiscount;
                    var lineVat = lineTaxable * (row.VatRate / 100);
                    var lineTotal = lineTaxable + lineVat;

                    newInvoice.Lines.Add(new FacInvoiceLine
                    {
                        InvoiceId = newInvoice.Id,
                        RowNo = rowNo++,
                        ProductId = product.Id,
                        ProductCode = row.ProductCode ?? "",
                        ProductName = row.ProductName,
                        Unit = "",
                        TaxCode = "",
                        Quantity = row.Quantity,
                        UnitPrice = row.UnitPrice,
                        DiscountPercent = row.DiscountPercent,
                        Discount = lineDiscount,
                        VatRate = row.VatRate,
                        VatAmount = lineVat,
                        Taxable = lineTaxable,
                        Total = lineTotal,
                        Description = row.Description,
                        CurrentStock = 0
                    });
                }

                _db.FacInvoices.Add(newInvoice);
                await _db.SaveChangesAsync();
                importedCount++;
            }

            return importedCount;
        }

        #region Helper Methods

        private static string? GetCellValue(ExcelWorksheet sheet, int row, Dictionary<string, int> headers, string headerName)
        {
            var key = headerName.ToLowerInvariant();
            if (!headers.TryGetValue(key, out var col))
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

        private static int ParseInvoiceNumber(string invoiceNumber)
        {
            var digits = new string(invoiceNumber.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out var num) ? num : 0;
        }

        private async Task<Party> GetOrCreatePartyAsync(string name, string? taxId)
        {
            var party = await _db.Parties
                .FirstOrDefaultAsync(p => p.Name == name ||
                                         (!string.IsNullOrWhiteSpace(taxId) && p.TaxId == taxId));

            if (party != null)
                return party;

            party = new Party
            {
                Name = name,
                TaxId = taxId,
                Type = PartyType.Customer,
                CompanyId = _defaultCompanyId,
                IsActive = true
            };
            _db.Parties.Add(party);
            await _db.SaveChangesAsync();
            return party;
        }

        private async Task<Product> GetOrCreateProductAsync(string name, string? code, decimal vatRate)
        {
            var product = await _db.Products
                .FirstOrDefaultAsync(p => p.Name == name ||
                                         (!string.IsNullOrWhiteSpace(code) && p.Code == code));

            if (product != null)
                return product;

            product = new Product
            {
                Name = name,
                Code = code ?? "",
                VatRate = vatRate,
                CompanyId = _defaultCompanyId,
                IsActive = true,
                Unit = "عدد"
            };
            _db.Products.Add(product);
            await _db.SaveChangesAsync();
            return product;
        }

        #endregion
    }

    #region Data Models

    public class InvoiceRowData
    {
        public string InvoiceNumber { get; set; } = "";
        public DateTime? DateOrder { get; set; }
        public string CustomerName { get; set; } = "";
        public string? CustomerNationalId { get; set; }
        public string ProductName { get; set; } = "";
        public string? ProductCode { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal VatRate { get; set; }
        public string? Description { get; set; }
    }

    #endregion
}
