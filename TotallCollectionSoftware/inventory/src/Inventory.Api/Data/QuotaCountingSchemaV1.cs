using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// اسکیمای «شمارش سهمیه» — ستون <c>CreatedAt</c> روی جدول‌های کسب‌وکاری که سقف ماهانه
/// دارند، به‌همراه پرکردن ردیف‌های قدیمی و ایندکس جست‌وجو.
///
/// <para>
/// <b>چرا این فایل وجود دارد؟</b> شمارش سقف ماهانه قبلاً روی تاریخ‌هایی انجام می‌شد که
/// کاربر تعیین می‌کند (<c>FacInvoices.Date</c>، <c>OutgoingLetters.DateSabt</c>، …).
/// این تاریخ‌ها <b>قابل ویرایش</b>اند؛ پس مشتری می‌توانست با تغییر تاریخ یک رکورد، آن را
/// از ماه جاری بیرون ببرد و سهمیه آزاد کند — بدون حذف هیچ چیزی. مبنای درست، زمان ثبت
/// سیستمی است که فقط در لحظهٔ درج نوشته می‌شود.
/// </para>
///
/// <para>
/// ⚠️ <b>همان قرارداد <see cref="LicenseSchemaV1"/>:</b> فقط افزایشی. هیچ
/// <c>DROP</c>/<c>DELETE</c>/بازنویسی دادهٔ موجود در این فایل نیست؛ تنها استثنا
/// «پرکردن ستون تازه‌ای است که خودمان همین حالا ساخته‌ایم» و فقط جایی که <c>NULL</c> است.
/// </para>
///
/// <para>
/// 🔁 idempotent است و در هر بالا آمدن اجرا می‌شود؛ اجرای دوباره هیچ کاری نمی‌کند
/// (نه ستون تازه می‌سازد، نه ردیفی را دوباره پر می‌کند).
/// </para>
/// </summary>
public static class QuotaCountingSchemaV1
{
    /// <summary>
    /// (جدول، ستون تاریخِ کاربر) — ستون سیستمی روی همین‌ها اضافه می‌شود.
    ///
    /// <para>
    /// فقط جدول‌هایی که متریک سهمیه دارند و probe دارند: فاکتور و سه نوع نامه.
    /// </para>
    /// </summary>
    private static readonly (string Table, string UserDateColumn)[] Targets =
    {
        ("FacInvoices", "Date"),
        ("IncomingLetters", "Date"),
        ("OutgoingLetters", "DateSabt"),
        ("InnerLetters", "DateSabt"),
    };

    /// <summary>اجرای کامل: ستون‌ها، پرکردن، ایندکس‌ها.</summary>
    /// <returns>تعداد ردیف‌هایی که در این اجرا پر شدند (۰ در اجراهای بعدی).</returns>
    public static async Task<int> EnsureAsync(DbContext db, ILogger? log = null,
        CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        var opened = false;

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
            opened = true;
        }

        var isSqlServer = connection is Microsoft.Data.SqlClient.SqlConnection;
        var backfilled = 0;

        try
        {
            foreach (var (table, userDate) in Targets)
            {
                if (isSqlServer)
                {
                    // ستون تازه (nullable) — ردیف‌های موجود NULL می‌مانند.
                    await Exec(connection, $@"
IF OBJECT_ID(N'dbo.[{table}]', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.[{table}]', 'CreatedAt') IS NULL
    ALTER TABLE [dbo].[{table}] ADD [CreatedAt] datetime2 NULL;", cancellationToken);

                    // پرکردن ردیف‌های قدیمی از تاریخ کاربر — یک‌بار.
                    //
                    // COALESCE: اگر تاریخِ کاربر هم NULL باشد، مقدار ثابت 1900-01-01 گذاشته
                    // می‌شود تا ردیف هرگز NULL نماند. چرا؟ ردیفِ NULL در هیچ بازه‌ای نمی‌افتد،
                    // پس در سقف ماهانه/سالانه شمرده نمی‌شود ولی در «کل عمر» هم شمرده نمی‌شد —
                    // یعنی عملاً از هر شمارشی ناپدید می‌شد. با 1900-01-01:
                    //   • در سقف ماهانه/سالانهٔ جاری نمی‌افتد (مصرف ماه را الکی پر نمی‌کند)
                    //   • در سقف «دائمی» شمرده می‌شود (داده دیده می‌شود)
                    backfilled += await Exec(connection, $@"
IF OBJECT_ID(N'dbo.[{table}]', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.[{table}]', 'CreatedAt') IS NOT NULL
    UPDATE TOP (50000) [dbo].[{table}]
       SET [CreatedAt] = COALESCE([{userDate}], CONVERT(datetime2, '1900-01-01', 120))
     WHERE [CreatedAt] IS NULL;", cancellationToken);

                    // ایندکس جست‌وجوی بازه‌ای ماه — همان ستونی که سقف روی آن حساب می‌شود.
                    await Exec(connection, $@"
IF OBJECT_ID(N'dbo.[{table}]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{table}_CreatedAt'
                   AND object_id = OBJECT_ID(N'dbo.[{table}]'))
    CREATE INDEX [IX_{table}_CreatedAt] ON [dbo].[{table}] ([CreatedAt]);", cancellationToken);
                }
                else
                {
                    await AddColumnIfMissingAsync(connection, table, "CreatedAt", "TEXT NULL", cancellationToken);
                    // همان قاعدهٔ SQL Server: تاریخ کاربر، و در نبود آن 1900-01-01 (نه NULL).
                    backfilled += await Exec(connection,
                        $"UPDATE \"{table}\" SET \"CreatedAt\" = COALESCE(\"{userDate}\", '1900-01-01 00:00:00') " +
                        "WHERE \"CreatedAt\" IS NULL",
                        cancellationToken, optional: true);
                    await Exec(connection,
                        $"CREATE INDEX IF NOT EXISTS \"IX_{table}_CreatedAt\" ON \"{table}\" (\"CreatedAt\")",
                        cancellationToken, optional: true);
                }
            }
        }
        finally
        {
            if (opened) await connection.CloseAsync();
        }

        if (backfilled > 0)
            log?.LogInformation(
                "[License] ستون CreatedAt برای {Count} ردیف قدیمی از تاریخ کاربر پر شد " +
                "(مبنای شمارش سقف ماهانه). اجرای بعدی کاری انجام نمی‌دهد.", backfilled);

        // تشخیص باقی‌ماندهٔ NULL: پرکردن با TOP (50000) در هر اجرا انجام می‌شود، پس در
        // جدول‌های خیلی بزرگ ممکن است چند استارت طول بکشد. عدد را چاپ می‌کنیم تا کسی
        // خیال نکند «صفر شد» در حالی که هنوز ردیف بی‌تاریخ مانده است.
        var nulls = await CountNullsAsync(connection, isSqlServer, cancellationToken);
        if (nulls > 0)
            log?.LogWarning(
                "[License] {Count} ردیف هنوز CreatedAt ندارد؛ در شمارش سقف ماهانه نمی‌آیند. " +
                "در استارت بعدی پر می‌شوند (سقف ۵۰۰۰۰ ردیف در هر اجرا).", nulls);

        return backfilled;
    }

    /// <summary>
    /// چند ردیف هنوز <c>CreatedAt</c> خالی دارند؟ برای پایش — اگر بزرگ‌تر از صفر بود،
    /// یعنی پرکردن نیمه‌کاره مانده و باید دوباره اجرا شود (اجرای خودکار در استارتاپ).
    /// </summary>
    public static async Task<long> CountUnfilledAsync(DbContext db, CancellationToken cancellationToken = default)
    {
        long total = 0;
        foreach (var (table, _) in Targets)
        {
            try
            {
                var sql = db.Database.IsSqlite()
                    ? $"SELECT COUNT(*) FROM \"{table}\" WHERE \"CreatedAt\" IS NULL"
                    : $"SELECT COUNT(*) FROM [dbo].[{table}] WHERE [CreatedAt] IS NULL";

                await using var cmd = db.Database.GetDbConnection().CreateCommand();
                cmd.CommandText = sql;

                if (cmd.Connection!.State != ConnectionState.Open)
                    await cmd.Connection.OpenAsync(cancellationToken);

                var value = await cmd.ExecuteScalarAsync(cancellationToken);
                if (value is not null and not DBNull) total += Convert.ToInt64(value);
            }
            catch
            {
                // جدول در این نصب نیست (یا ستون هنوز اضافه نشده) — بی‌خطر.
            }
        }

        return total;
    }

    private static async Task AddColumnIfMissingAsync(System.Data.Common.DbConnection connection,
        string table, string column, string sqlType, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info('{table}')";

        var exists = false;
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }
        catch
        {
            return;   // جدول نیست
        }

        if (exists) return;

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {sqlType}";
        try { await alter.ExecuteNonQueryAsync(cancellationToken); } catch { /* جدول نیست */ }
    }

    /// <summary>
    /// شمارش ردیف‌های بدون تاریخ در هر چهار جدول — فقط برای تشخیص/لاگ، هیچ تغییری نمی‌دهد.
    /// </summary>
    private static async Task<int> CountNullsAsync(System.Data.Common.DbConnection connection,
        bool isSqlServer, CancellationToken cancellationToken)
    {
        var total = 0;
        foreach (var (table, _) in Targets)
        {
            var sql = isSqlServer
                ? $@"IF OBJECT_ID(N'dbo.[{table}]', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.[{table}]', 'CreatedAt') IS NOT NULL
                     SELECT COUNT_BIG(*) FROM [dbo].[{table}] WHERE [CreatedAt] IS NULL
                     ELSE SELECT CAST(0 AS bigint)"
                : $"SELECT COUNT(*) FROM \"{table}\" WHERE \"CreatedAt\" IS NULL";

            try
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                var value = await cmd.ExecuteScalarAsync(cancellationToken);
                total += value is null or DBNull ? 0 : Convert.ToInt32(value);
            }
            catch
            {
                // جدول/ستون در این نصب نیست — صفر، نه خطا.
            }
        }
        return total;
    }

    private static async Task<int> Exec(System.Data.Common.DbConnection connection, string sql,
        CancellationToken cancellationToken, bool optional = false)
    {
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            return await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch when (optional)
        {
            return 0;
        }
    }
}
