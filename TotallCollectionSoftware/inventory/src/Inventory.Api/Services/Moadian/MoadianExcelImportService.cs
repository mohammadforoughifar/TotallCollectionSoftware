using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;
using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos.Moadian;

namespace Inventory.Api.Services.Moadian;

/// <summary>قالب‌سازی و ورود گروهی اطلاعات پایهٔ مودیان از فایل XLSX.</summary>
public interface IMoadianExcelImportService
{
    byte[] BuildGoodsTemplate();
    byte[] BuildUnitsTemplate();
    byte[] BuildCustomersTemplate();
    Task<ApiModel> ImportGoodsAsync(int cpInfoId, Stream excelStream);
    Task<ApiModel> ImportUnitsAsync(int cpInfoId, Stream excelStream);
    Task<ApiModel> ImportCustomersAsync(int cpInfoId, Stream excelStream);
}

/// <summary>
/// خواندن فایل اکسل در سمت سرور و ثبت یکجای سطرهای معتبر.
/// کنترل دسترسی خدمات‌دهنده با GuardCpInfo سرویس اصلی انجام می‌شود؛ سطرهای معتبر
/// در یک SaveChanges ذخیره و سطرهای نامعتبر/تکراری در نتیجهٔ ورود گزارش می‌شوند.
/// </summary>
public sealed class MoadianExcelImportService(IConnectService connectService, AppDbContext context) : IMoadianExcelImportService
{
    private const int MaxDataRows = 10_000;
    private const int MaxErrorDetails = 200;

    private sealed record ColumnSpec(string Key, string Caption, bool Required, string[] Aliases);

    private static readonly ColumnSpec[] GoodsColumns =
    {
        new("name", "نام کالا/خدمت", true, new[] { "نام کالا/خدمت", "نام کالا یا خدمت", "نام کالا", "نام خدمت", "نام" }),
        new("cpc", "شناسه CPC", true, new[] { "شناسه CPC", "کد CPC", "شناسه کالا", "شناسه خدمت", "شناسه یکتای کالا/خدمت" }),
        new("unit", "واحد سنجش", false, new[] { "واحد سنجش (نام یا کد)", "واحد سنجش", "نام واحد", "کد واحد", "واحد" }),
        new("price", "قیمت واحد", true, new[] { "قیمت واحد (ریال)", "قیمت واحد", "قیمت فروش", "قیمت" }),
        new("vat", "نرخ ارزش افزوده", false, new[] { "نرخ ارزش افزوده (%)", "نرخ ارزش افزوده", "ارزش افزوده", "مالیات بر ارزش افزوده" })
    };

    private static readonly ColumnSpec[] UnitColumns =
    {
        new("name", "نام واحد", true, new[] { "نام واحد", "نام" }),
        new("code", "کد واحد", true, new[] { "کد واحد", "کد", "شناسه واحد" })
    };

    private static readonly ColumnSpec[] CustomerColumns =
    {
        new("name", "نام مشتری", true, new[] { "نام مشتری", "نام" }),
        new("type", "نوع شخص", false, new[] { "نوع شخص", "نوع مشتری", "نوع" }),
        new("nationalId", "کد ملی/شماره ثبت", false, new[] { "کد ملی/شماره ثبت", "کد ملی", "شماره ثبت", "شناسه ملی" }),
        new("economic", "شماره اقتصادی", false, new[] { "شماره اقتصادی", "کد اقتصادی" }),
        new("postal", "کد پستی", false, new[] { "کد پستی", "پست کد", "پستکد" }),
        new("phone", "تلفن", false, new[] { "شماره تلفن", "شماره تماس", "شماره موبایل", "موبایل", "تلفن" }),
        new("address", "نشانی", false, new[] { "نشانی", "آدرس", "نشانی مشتری" })
    };

    public byte[] BuildGoodsTemplate() => BuildTemplate(
        "ورود کالاها",
        new[] { "نام کالا/خدمت", "شناسه CPC", "واحد سنجش (نام یا کد)", "قیمت واحد (ریال)", "نرخ ارزش افزوده (%)" },
        new[] { "کالای نمونه - فقط جهت راهنما", "1234567890123", "عدد", "100000", "10" },
        "فقط برگهٔ «ورود کالاها» خوانده می‌شود. نام، شناسه CPC و قیمت واحد الزامی است. واحد اختیاری است؛ اگر وارد شود باید نام یا کد آن قبلاً در تب «واحدهای سنجش» ثبت شده باشد. نرخ ارزش افزوده عدد صحیح ۰ تا ۱۰۰ است. شناسهٔ ردیف نمونه فرضی است و باید با شناسهٔ واقعی جایگزین شود.",
        2, 3);

    public byte[] BuildUnitsTemplate() => BuildTemplate(
        "ورود واحدها",
        new[] { "نام واحد", "کد واحد" },
        new[] { "عدد", "C62" },
        "فقط برگهٔ «ورود واحدها» خوانده می‌شود. هر دو ستون نام و کد الزامی‌اند؛ نام یا کدی که قبلاً برای این خدمات‌دهنده ثبت شده باشد وارد نمی‌شود.",
        2);

    public byte[] BuildCustomersTemplate() => BuildTemplate(
        "ورود مشتریان",
        new[] { "نام مشتری", "نوع شخص", "کد ملی/شماره ثبت", "شماره اقتصادی", "کد پستی", "تلفن", "نشانی" },
        new[] { "مشتری نمونه - فقط جهت راهنما", "حقیقی", "1234567890", "12345678901", "1234567890", "09120000000", "نشانی نمونه" },
        "فقط برگهٔ «ورود مشتریان» خوانده می‌شود. نام الزامی است؛ نوع خالی، حقیقی در نظر گرفته می‌شود. کد پستی و تلفن اختیاری‌اند و در صورت ورود اعتبارسنجی می‌شوند. برای حفظ صفر ابتدای شماره‌ها، ستون‌های کد را به‌صورت Text وارد کنید.",
        3, 4, 5, 6);

    public async Task<ApiModel> ImportGoodsAsync(int cpInfoId, Stream excelStream)
    {
        if (cpInfoId <= 0) return Error(400, "ابتدا یک خدمات‌دهندهٔ مودیان انتخاب کنید.");
        try
        {
            using var workbook = OpenWorkbook(excelStream);
            var sheet = GetFirstSheet(workbook);
            var lastRow = GetDataLastRow(sheet);
            var columns = ResolveColumns(sheet, GoodsColumns);

            // این دو فراخوانی، هم داده‌های فعلی برای جلوگیری از تکرار را می‌گیرند و هم
            // تعلق خدمات‌دهنده به شرکت فعال را با GuardCpInfo سرویس مودیان بررسی می‌کنند.
            var goodsResponse = await connectService.GetGoodOrServiceForComboAsync(cpInfoId);
            if (!goodsResponse.Success) return goodsResponse;
            var unitsResponse = await connectService.GetUnitOfMeasurementForComboAsync(cpInfoId);
            if (!unitsResponse.Success) return unitsResponse;

            var existingGoods = ReadList<VMGoodsOrServicesList>(goodsResponse, "فهرست کالاها");
            var units = ReadList<VMUnitOfMeasurement>(unitsResponse, "فهرست واحدها");
            var identifiers = new HashSet<string>(existingGoods.Select(g => NormalizeKey(g.UniqueIdentifier)).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
            var result = new MoadianExcelImportResult();
            var toInsert = new List<GoodsOrService>();

            for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
            {
                var row = sheet.Row(rowNumber);
                if (row.IsEmpty()) continue;
                try
                {
                    var name = ReadText(row, columns, "name");
                    var identifier = CompactText(ReadText(row, columns, "cpc"));
                    var unitValue = ReadText(row, columns, "unit");
                    var price = ReadDecimal(row, columns, "price", required: true, caption: "قیمت واحد");
                    var vat = ReadVat(row, columns);

                    if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("نام کالا/خدمت الزامی است.");
                    if (string.IsNullOrWhiteSpace(identifier)) throw new InvalidOperationException("شناسه CPC الزامی است.");
                    if (price <= 0) throw new InvalidOperationException("قیمت واحد باید بیشتر از صفر باشد.");
                    if (vat < 0 || vat > 100 || decimal.Truncate(vat) != vat)
                        throw new InvalidOperationException("نرخ ارزش افزوده باید عدد صحیح بین ۰ تا ۱۰۰ باشد.");
                    if (identifiers.Contains(NormalizeKey(identifier)))
                        throw new InvalidOperationException("شناسه CPC تکراری است؛ این شناسه قبلاً ثبت شده یا در همین فایل تکرار شده است.");

                    var unit = string.IsNullOrWhiteSpace(unitValue) ? null : ResolveUnit(units, unitValue);
                    toInsert.Add(new GoodsOrService
                    {
                        CpInfo = cpInfoId,
                        Name = name.Trim(),
                        UniqueIdentifier = identifier,
                        UnitOfMeasurement = unit?.Name,
                        ValueAddedPercentage = (byte)vat,
                        Price = price
                    });
                    identifiers.Add(NormalizeKey(identifier));
                    result.Imported++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    AddRowError(result, rowNumber, ex.Message);
                }
            }

            if (toInsert.Count > 0)
            {
                context.GoodsOrServices.AddRange(toInsert);
                await context.SaveChangesAsync();
            }
            return Success(result);
        }
        catch (InvalidDataException ex)
        {
            return Error(400, ex.Message);
        }
        catch (Exception ex)
        {
            return Error(500, "خطا در پردازش فایل اکسل کالاها/خدمات: " + ex.Message);
        }
    }

    public async Task<ApiModel> ImportUnitsAsync(int cpInfoId, Stream excelStream)
    {
        if (cpInfoId <= 0) return Error(400, "ابتدا یک خدمات‌دهندهٔ مودیان انتخاب کنید.");
        try
        {
            using var workbook = OpenWorkbook(excelStream);
            var sheet = GetFirstSheet(workbook);
            var lastRow = GetDataLastRow(sheet);
            var columns = ResolveColumns(sheet, UnitColumns);

            var unitsResponse = await connectService.GetUnitOfMeasurementForComboAsync(cpInfoId);
            if (!unitsResponse.Success) return unitsResponse;
            var existingUnits = ReadList<VMUnitOfMeasurement>(unitsResponse, "فهرست واحدها");
            var codes = new HashSet<string>(existingUnits.Select(u => NormalizeKey(u.Code)).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>(existingUnits.Select(u => NormalizeKey(u.Name)).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
            var result = new MoadianExcelImportResult();
            var toInsert = new List<UnitOfMeasurement>();

            for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
            {
                var row = sheet.Row(rowNumber);
                if (row.IsEmpty()) continue;
                try
                {
                    var name = ReadText(row, columns, "name").Trim();
                    var code = CompactText(ReadText(row, columns, "code"));
                    if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("نام واحد الزامی است.");
                    if (string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("کد واحد الزامی است.");
                    if (codes.Contains(NormalizeKey(code))) throw new InvalidOperationException("کد واحد تکراری است.");
                    if (names.Contains(NormalizeKey(name))) throw new InvalidOperationException("نام واحد تکراری است.");

                    toInsert.Add(new UnitOfMeasurement { CpInfo = cpInfoId, Name = name, Code = code });
                    codes.Add(NormalizeKey(code));
                    names.Add(NormalizeKey(name));
                    result.Imported++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    AddRowError(result, rowNumber, ex.Message);
                }
            }

            if (toInsert.Count > 0)
            {
                context.UnitOfMeasurements.AddRange(toInsert);
                await context.SaveChangesAsync();
            }
            return Success(result);
        }
        catch (InvalidDataException ex)
        {
            return Error(400, ex.Message);
        }
        catch (Exception ex)
        {
            return Error(500, "خطا در پردازش فایل اکسل واحدها: " + ex.Message);
        }
    }

    public async Task<ApiModel> ImportCustomersAsync(int cpInfoId, Stream excelStream)
    {
        if (cpInfoId <= 0) return Error(400, "ابتدا یک خدمات‌دهندهٔ مودیان انتخاب کنید.");
        try
        {
            using var workbook = OpenWorkbook(excelStream);
            var sheet = GetFirstSheet(workbook);
            var lastRow = GetDataLastRow(sheet);
            var columns = ResolveColumns(sheet, CustomerColumns);

            var customersResponse = await connectService.GetCustomerForComboAsync(cpInfoId);
            if (!customersResponse.Success) return customersResponse;
            var existingCustomers = ReadList<VMCustomerList>(customersResponse, "فهرست مشتریان");
            var nationalIds = new HashSet<string>(existingCustomers.Select(c => NormalizeIdentity(c.NationalID)).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
            var economicNumbers = new HashSet<string>(existingCustomers.Select(c => NormalizeIdentity(c.EconomicNumber)).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
            var result = new MoadianExcelImportResult();
            var toInsert = new List<Customer>();

            for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
            {
                var row = sheet.Row(rowNumber);
                if (row.IsEmpty()) continue;
                try
                {
                    var name = ReadText(row, columns, "name").Trim();
                    var typeText = ReadText(row, columns, "type").Trim();
                    var nationalId = NormalizeIdentity(ReadText(row, columns, "nationalId"));
                    var economicNumber = NormalizeIdentity(ReadText(row, columns, "economic"));
                    var postalCode = ReadText(row, columns, "postal");
                    var phone = ReadText(row, columns, "phone");
                    var address = ReadText(row, columns, "address").Trim();

                    if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("نام مشتری الزامی است.");
                    var type = NormalizeCustomerType(typeText);
                    var contactError = ShopIdentifierRules.ValidateContact(phone, postalCode, "شماره تلفن", "کد پستی");
                    if (contactError is not null) throw new InvalidOperationException(contactError);
                    if (nationalId.Length > 0 && nationalIds.Contains(nationalId))
                        throw new InvalidOperationException("کد ملی/شماره ثبت تکراری است.");
                    if (economicNumber.Length > 0 && economicNumbers.Contains(economicNumber))
                        throw new InvalidOperationException("شماره اقتصادی تکراری است.");
                    toInsert.Add(new Customer
                    {
                        CpInfoId = cpInfoId,
                        Name = name,
                        Type = type,
                        NationalID = EmptyToNull(nationalId),
                        EconomicNumber = EmptyToNull(economicNumber),
                        PostalCode = ShopIdentifierRules.Normalize(postalCode),
                        Phone = ShopIdentifierRules.NormalizePhone(phone),
                        Address = EmptyToNull(address)
                    });
                    if (nationalId.Length > 0) nationalIds.Add(nationalId);
                    if (economicNumber.Length > 0) economicNumbers.Add(economicNumber);
                    result.Imported++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    AddRowError(result, rowNumber, ex.Message);
                }
            }

            if (toInsert.Count > 0)
            {
                context.Customers.AddRange(toInsert);
                await context.SaveChangesAsync();
            }
            return Success(result);
        }
        catch (InvalidDataException ex)
        {
            return Error(400, ex.Message);
        }
        catch (Exception ex)
        {
            return Error(500, "خطا در پردازش فایل اکسل مشتریان: " + ex.Message);
        }
    }

    private static XLWorkbook OpenWorkbook(Stream stream)
    {
        try
        {
            return new XLWorkbook(stream);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("فایل اکسل قابل خواندن نیست؛ لطفاً یک فایل معتبر با فرمت xlsx انتخاب کنید.", ex);
        }
    }

    private static IXLWorksheet GetFirstSheet(XLWorkbook workbook)
        => workbook.Worksheets.FirstOrDefault()
           ?? throw new InvalidDataException("فایل اکسل فاقد برگه (Sheet) است.");

    private static int GetDataLastRow(IXLWorksheet sheet)
    {
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        if (lastRow < 2)
            throw new InvalidDataException("فایل اکسل داده‌ای ندارد؛ عنوان ستون‌ها باید در سطر اول و اطلاعات از سطر دوم باشد.");
        if (lastRow - 1 > MaxDataRows)
            throw new InvalidDataException($"حداکثر {Fa.Digits(MaxDataRows)} سطر داده در هر فایل قابل ورود است.");
        return lastRow;
    }

    private static Dictionary<string, int> ResolveColumns(IXLWorksheet sheet, IReadOnlyList<ColumnSpec> specs)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        var headerRow = sheet.Row(1);
        var lastColumn = Math.Min(100, Math.Max(1, headerRow.LastCellUsed()?.Address.ColumnNumber ?? 1));
        var headers = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var column = 1; column <= lastColumn; column++)
        {
            var header = NormalizeHeader(ReadCellText(headerRow.Cell(column)));
            if (header.Length > 0 && !headers.ContainsKey(header)) headers[header] = column;
        }

        foreach (var spec in specs)
        {
            foreach (var alias in spec.Aliases)
            {
                if (!headers.TryGetValue(NormalizeHeader(alias), out var column)) continue;
                result[spec.Key] = column;
                break;
            }
            if (spec.Required && !result.ContainsKey(spec.Key))
                throw new InvalidDataException($"ستون الزامی «{spec.Caption}» در سطر اول فایل پیدا نشد؛ از قالب اکسل نمونه استفاده کنید.");
        }
        return result;
    }

    private static string ReadText(IXLRow row, IReadOnlyDictionary<string, int> columns, string key)
        => columns.TryGetValue(key, out var column) ? ReadCellText(row.Cell(column)) : "";

    private static string ReadCellText(IXLCell cell)
    {
        if (cell.IsEmpty()) return "";
        if (cell.DataType == XLDataType.Number && cell.TryGetValue<decimal>(out var number))
            return number.ToString("0.############################", CultureInfo.InvariantCulture).Trim();
        return cell.GetString().Trim();
    }

    private static decimal ReadDecimal(IXLRow row, IReadOnlyDictionary<string, int> columns, string key, bool required, string caption)
    {
        if (!columns.TryGetValue(key, out var column))
        {
            if (required) throw new InvalidDataException($"ستون «{caption}» در فایل پیدا نشد.");
            return 0;
        }
        var cell = row.Cell(column);
        if (cell.IsEmpty())
        {
            if (required) throw new InvalidOperationException($"{caption} الزامی است.");
            return 0;
        }
        if (cell.DataType == XLDataType.Number && cell.TryGetValue<decimal>(out var number)) return number;

        var raw = Fa.ToEn(cell.GetString()).Trim()
            .Replace('٬', ',').Replace('٫', '.').Replace(",", "");
        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out number))
            throw new InvalidOperationException($"مقدار «{cell.GetString()}» در ستون {caption} عدد معتبر نیست.");
        return number;
    }

    private static decimal ReadVat(IXLRow row, IReadOnlyDictionary<string, int> columns)
    {
        if (!columns.TryGetValue("vat", out var column)) return 0;
        var cell = row.Cell(column);
        if (cell.IsEmpty()) return 0;
        var raw = ReadCellText(cell).Replace("٪", "").Replace("%", "").Trim();
        if (raw.Length == 0) return 0;
        if (cell.DataType == XLDataType.Number && cell.TryGetValue<decimal>(out var number)) return number;
        raw = Fa.ToEn(raw).Replace('٬', ',').Replace('٫', '.').Replace(",", "");
        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out number))
            throw new InvalidOperationException($"نرخ ارزش افزودهٔ «{cell.GetString()}» عدد معتبر نیست.");
        return number;
    }

    private static VMUnitOfMeasurement ResolveUnit(IReadOnlyList<VMUnitOfMeasurement> units, string value)
    {
        var key = NormalizeKey(value);
        var codeMatches = units.Where(u => NormalizeKey(u.Code) == key).ToList();
        var matches = codeMatches.Count > 0
            ? codeMatches
            : units.Where(u => NormalizeKey(u.Name) == key).ToList();
        if (matches.Count == 0)
            throw new InvalidOperationException($"واحد «{value}» در اطلاعات پایه ثبت نشده است؛ ابتدا آن را در تب واحدهای سنجش وارد کنید.");
        if (matches.Count > 1)
            throw new InvalidOperationException($"واحد «{value}» مبهم است؛ در فایل، کد واحد سنجش را وارد کنید.");
        return matches[0];
    }

    private static List<T> ReadList<T>(ApiModel response, string label)
    {
        if (response.Data is List<T> list) return list;
        if (response.Data is IEnumerable<T> enumerable) return enumerable.ToList();
        if (response.Data is JsonElement element && element.ValueKind == JsonValueKind.Array)
            return element.Deserialize<List<T>>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        throw new InvalidOperationException($"{label} از سرویس مودیان قابل خواندن نیست.");
    }

    private static string NormalizeHeader(string? value)
    {
        var normalized = Fa.ToEn(value ?? "").Replace("ي", "ی").Replace("ك", "ک").Replace("ۀ", "ه").Replace("\u200c", "");
        return new string(normalized.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    private static string NormalizeKey(string? value)
    {
        var normalized = Fa.ToEn(value ?? "").Trim().Replace("ي", "ی").Replace("ك", "ک").Replace("\u200c", "");
        return new string(normalized.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
    }

    private static string CompactText(string? value) =>
        new string(Fa.ToEn(value ?? "").Trim().Where(c => !char.IsWhiteSpace(c) && c != '\u200c').ToArray());

    private static string NormalizeIdentity(string? value) =>
        new string(Fa.ToEn(value ?? "").Trim().Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '\u200c').ToArray());

    private static string NormalizeCustomerType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "حقیقی";
        var normalized = value.Trim().Replace("ي", "ی").Replace("ك", "ک").Replace("\u200c", "");
        return normalized switch
        {
            "حقیقی" or "شخص حقیقی" => "حقیقی",
            "حقوقی" or "شخص حقوقی" => "حقوقی",
            _ => throw new InvalidOperationException("نوع شخص باید «حقیقی» یا «حقوقی» باشد.")
        };
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void AddRowError(MoadianExcelImportResult result, int rowNumber, string message)
    {
        result.Failed++;
        if (result.Errors.Count < MaxErrorDetails)
            result.Errors.Add($"سطر {Fa.Digits(rowNumber)}: {message}");
        else
            result.ErrorDetailsOmitted++;
    }

    private static ApiModel Success(MoadianExcelImportResult result) => new()
    {
        Data = result,
        Status = 200,
        Success = true,
        Message = "پردازش فایل اکسل انجام شد."
    };

    private static ApiModel Error(int status, string message) => new()
    {
        Data = null,
        Status = status,
        Success = false,
        Message = message
    };

    private static byte[] BuildTemplate(string inputSheetName, string[] headers, string[] sampleValues, string guide, params int[] textColumns)
    {
        using var workbook = new XLWorkbook();
        var input = workbook.Worksheets.Add(inputSheetName);
        input.RightToLeft = true;
        WriteHeaders(input, 1, headers);
        ApplyColumnWidths(input, headers);
        foreach (var column in textColumns) input.Column(column).Style.NumberFormat.Format = "@";

        // برگهٔ دوم صرفاً راهنماست؛ ورودی فقط از برگهٔ اول خوانده می‌شود تا
        // مثال‌های آموزشی ناخواسته وارد دیتابیس نشوند.
        var example = workbook.Worksheets.Add("نمونه");
        example.RightToLeft = true;
        var titleRange = example.Range(1, 1, 1, headers.Length);
        titleRange.Merge();
        example.Cell(1, 1).Value = "این برگه فقط نمونه است؛ داده‌های آن هنگام ورود به دیتابیس خوانده نمی‌شود.";
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF2CC");
        titleRange.Style.Alignment.WrapText = true;
        titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

        var guideRange = example.Range(2, 1, 2, headers.Length);
        guideRange.Merge();
        example.Cell(2, 1).Value = guide;
        guideRange.Style.Alignment.WrapText = true;
        guideRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        example.Row(2).Height = 48;

        WriteHeaders(example, 4, headers);
        for (var i = 0; i < headers.Length; i++)
            example.Cell(5, i + 1).Value = sampleValues[i];
        ApplyColumnWidths(example, headers);
        foreach (var column in textColumns) example.Column(column).Style.NumberFormat.Format = "@";
        example.Range(5, 1, 5, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#E2F0D9");
        example.Range(5, 1, 5, headers.Length).Style.Alignment.WrapText = true;
        example.Row(4).Height = 30;

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
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
        sheet.Row(row).Height = 30;
    }

    private static void ApplyColumnWidths(IXLWorksheet sheet, IReadOnlyList<string> headers)
    {
        for (var column = 1; column <= headers.Count; column++)
            sheet.Column(column).Width = Math.Clamp(headers[column - 1].Length + 8, 18, 38);
    }
}
