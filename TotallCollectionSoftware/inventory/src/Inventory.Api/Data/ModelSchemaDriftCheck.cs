using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// پایش «هم‌خوانی مدل EF با اسکیمای واقعی دیتابیس» — عمداً سبک و بی‌خطر.
/// <para>
/// چرا؟ باگ واقعی: موجودیت <see cref="AppAttachment"/> ستونی (<c>DocumentId</c>) داشت که
/// هیچ مایگریشن/خودتعمیری نمی‌ساخت؛ EF آن را در هر SELECT/INSERT می‌آورد و ماژول
/// ویزیتوری با «Invalid column name» و ۴۰۰ می‌شکست، بدون هیچ نشانه‌ای در لاگ راه‌اندازی.
/// </para>
/// <para>
/// این بررسی فقط <b>لاگ</b> می‌کند و هیچ‌وقت راه‌اندازی را متوقف نمی‌کند؛ اگر ستونی
/// غایب باشد، صریح در کنسول/لاگ نوشته می‌شود تا معلوم شود مشکل «اسکیمای دیتابیس» است
/// نه کد/فرانت. عمداً به یک جدول پرترافیک محدود است تا هزینهٔ راه‌اندازی ناچیز بماند.
/// </para>
/// </summary>
public static class ModelSchemaDriftCheck
{
    /// <summary>آخرین متن هشدار — برای نمایش در سلامت سیستم؛ null یعنی هم‌خوان.</summary>
    public static string? LastWarning { get; private set; }

    private const string Table = "AppAttachments";

    public static async Task RunAsync(AppDbContext db)
    {
        LastWarning = null;
        try
        {
            var model = db.Model.FindEntityType(typeof(AppAttachment));
            if (model is null) return;

            var mapped = model.GetProperties()
                .Where(p => p.PropertyInfo is not null && !p.IsForeignKey())
                .Select(p => p.GetColumnName())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct()
                .ToList();
            if (mapped.Count == 0) return;

            var existing = await ExistingColumnsAsync(db);
            if (existing.Count == 0) return;   // جدول نیست یا نسنجیدیم — هشدار بی‌مورد ندهیم

            var missing = mapped.Where(c => !existing.Contains(c)).ToList();
            if (missing.Count == 0) return;

            LastWarning =
                $"ستون‌های مدل در جدول {Table} وجود ندارند: {string.Join(", ", missing)}. " +
                "این ستون‌ها در هر کوئری خوانده/نوشته می‌شوند و باعث خطای «Invalid column name» " +
                "می‌گردند؛ خودتعمیر اسکیمای مربوطه (Data/*SchemaV1.cs) را کامل کنید.";
            Console.WriteLine($"[DB] ⚠ هم‌خوانی مدل/اسکیما — {LastWarning}");
        }
        catch (Exception ex)
        {
            // پایش نباید هیچ‌وقت مانع بالا آمدن سرویس شود.
            Console.WriteLine($"[DB] ModelSchemaDriftCheck: {ex.Message}");
        }
    }

    private static async Task<HashSet<string>> ExistingColumnsAsync(AppDbContext db)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = db.Database.IsSqlite()
            ? $"SELECT name FROM pragma_table_info('{Table}');"
            : $@"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
                 WHERE TABLE_NAME = '{Table}' AND TABLE_SCHEMA = 'dbo';";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (!reader.IsDBNull(0)) columns.Add(reader.GetString(0));
        }
        return columns;
    }
}
