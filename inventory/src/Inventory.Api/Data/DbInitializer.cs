using Inventory.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>ساخت خودکار دیتابیس و بارگذاری داده اولیه در زمان راه‌اندازی.</summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var provider = config["Database:Provider"] ?? "SqlServer";
        // داده نمونه (دمو) فقط وقتی ساخته می‌شود که صریحاً فعال شده باشد — پیش‌فرض: دیتابیس تمیز
        var seedDemo = string.Equals(config["Database:SeedDemoData"], "true", StringComparison.OrdinalIgnoreCase);

        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
                {
                    // حالت توسعه/تست: بدون مایگریشن
                    db.Database.EnsureCreated();
                    // EnsureCreated ستون‌های جدید را به دیتابیسِ موجود اضافه نمی‌کند؛ اینجا خودتعمیر می‌کنیم
                    EnsureSqliteWorkCalendarSchema(db);
                }
                else
                {
                    // SQL Server: اعمال مایگریشن‌ها (در صورت نبود دیتابیس، خودش می‌سازد)
                    db.Database.Migrate();
                }

                if (seedDemo && !db.Products.Any())
                {
                    Console.WriteLine("[DB] حالت دمو فعال است؛ در حال بارگذاری داده نمونه...");
                    Seeder.Seed(db);
                    Console.WriteLine("[DB] داده نمونه با موفقیت بارگذاری شد.");
                }

                // ==================== RBAC Seed ====================
                await RbacSeeder.SeedAsync(db);

                // انبار پیش‌فرض (در اولین اجرا — قابل ویرایش/تغییر نام از بخش انبارها)
                if (!db.Warehouses.Any())
                {
                    db.Warehouses.Add(new Warehouse { Name = "انبار مرکزی" });
                    db.SaveChanges();
                    Console.WriteLine("[DB] انبار پیش‌فرض «انبار مرکزی» ساخته شد.");
                }

                // بازسازی گروه‌های کالا برای دیتابیس‌های قدیمی:
                // هر گروهی که روی کالاها ثبت شده ولی در جدول گروه‌ها نیست، اضافه می‌شود.
                var existing = db.ProductCategories.Select(c => c.Name).ToHashSet();
                var missing = db.Products.Where(p => p.Category != null)
                    .Select(p => p.Category!).Distinct().ToList()
                    .Where(n => !existing.Contains(n)).ToList();
                if (missing.Count > 0)
                {
                    foreach (var name in missing)
                        db.ProductCategories.Add(new ProductCategory { Name = name, CreatedAt = DateTime.Now });
                    db.SaveChanges();
                    Console.WriteLine($"[DB] {missing.Count} گروه کالا از روی کالاهای موجود ساخته شد.");
                }

                // بازسازی واحدهای شمارش برای دیتابیس‌های قدیمی:
                // واحدهای پیش‌فرض + واحدهای استفاده‌شده روی کالاها که در جدول نیستند اضافه می‌شوند.
                var knownUnits = db.MeasureUnits.Select(u => u.Name).ToHashSet();
                var wanted = Seeder.DefaultUnits
                    .Concat(db.Products.Select(p => p.Unit).Distinct().ToList())
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct()
                    .Where(n => !knownUnits.Contains(n))
                    .ToList();
                if (wanted.Count > 0)
                {
                    foreach (var name in wanted)
                        db.MeasureUnits.Add(new MeasureUnit { Name = name, CreatedAt = DateTime.Now });
                    db.SaveChanges();
                    Console.WriteLine($"[DB] {wanted.Count} واحد شمارش اضافه شد.");
                }

                // کاربر پیش‌فرض مدیر: admin / admin (در اولین اجرا)
                if (!db.Users.Any())
                {
                    db.Users.Add(new User
                    {
                        Username = "admin",
                        PasswordHash = Services.AuthService.HashPassword("admin"),
                        Role = "Admin",
                        CreatedAt = DateTime.Now
                    });
                    db.SaveChanges();
                    Console.WriteLine("[DB] کاربر پیش‌فرض ساخته شد: admin / admin — حتماً رمز را تغییر دهید.");
                }

                // ============ اتوماسیون اداری: عملگرهای پیش‌فرض ارجاع ============
                // عملگر (Amalgar) تعیین می‌کند ارجاع «جهت اطلاع» است یا «جهت اقدام/تایید و امضا».
                if (!db.Amalgars.Any())
                {
                    db.Amalgars.AddRange(
                        new Amalgar { Title = "جهت اطلاع", TaeedEmza = "" },
                        new Amalgar { Title = "جهت اقدام", TaeedEmza = "" },
                        new Amalgar { Title = "جهت بررسی و اعلام نظر", TaeedEmza = "" },
                        new Amalgar { Title = "جهت تایید و امضا", TaeedEmza = "تایید" },
                        new Amalgar { Title = "جهت پاسخگویی", TaeedEmza = "" },
                        new Amalgar { Title = "جهت بایگانی", TaeedEmza = "" });
                    db.SaveChanges();
                    Console.WriteLine("[DB] عملگرهای پیش‌فرض ارجاع نامه ساخته شدند.");
                }

                // کاربران نمونه برای دموی کارتابل نامه (فقط در حالت دمو)
                if (seedDemo && db.Users.Count() <= 1)
                {
                    db.Users.AddRange(
                        new User { Username = "ali", PasswordHash = Services.AuthService.HashPassword("ali123"), Role = "Operator", FirstName = "علی", LastName = "رضایی", CreatedAt = DateTime.Now },
                        new User { Username = "sahar", PasswordHash = Services.AuthService.HashPassword("sahar123"), Role = "Operator", FirstName = "سحر", LastName = "محمدی", CreatedAt = DateTime.Now },
                        new User { Username = "fatemeh", PasswordHash = Services.AuthService.HashPassword("fatemeh123"), Role = "Operator", FirstName = "فاطمه", LastName = "کریمی", CreatedAt = DateTime.Now });
                    db.SaveChanges();
                    Console.WriteLine("[DB] کاربران دمو ساخته شدند: ali/ali123 — sahar/sahar123 — fatemeh/fatemeh123");
                }

                // گروه‌های نمونه گیرندگان نامه (فقط در حالت دمو)
                if (seedDemo && !db.LetterGroups.Any())
                {
                    var demoAdmin = db.Users.FirstOrDefault(u => u.Username == "admin");
                    var demoUsers = db.Users.Where(u => u.Username != "admin").Take(3).ToList();
                    if (demoAdmin != null && demoUsers.Count > 0)
                    {
                        var g1 = new LetterGroup { NameGroup = "کارشناسان اداری", CreatorUserId = demoAdmin.Id };
                        foreach (var u in demoUsers) g1.Members.Add(new LetterGroupMember { UserId = u.Id });
                        var g2 = new LetterGroup { NameGroup = "مدیران", CreatorUserId = demoAdmin.Id };
                        g2.Members.Add(new LetterGroupMember { UserId = demoAdmin.Id });
                        g2.Members.Add(new LetterGroupMember { UserId = demoUsers[0].Id });
                        db.LetterGroups.AddRange(g1, g2);
                        db.SaveChanges();
                        Console.WriteLine("[DB] گروه‌های دمو گیرندگان نامه ساخته شدند.");
                    }
                }

                // ==================== تقویم کاری: تنظیمات پیش‌فرض ====================
                if (!db.WorkCalendarSettings.Any())
                {
                    db.WorkCalendarSettings.Add(new WorkCalendarSettings());
                    db.SaveChanges();
                    Console.WriteLine("[DB] تنظیمات پیش‌فرض تقویم کاری ساخته شد (۰۸:۰۰–۱۶:۳۰، جمعه تعطیل).");
                }

                // ==================== تقویم کاری: تعطیلات رسمی سال جاری ====================
                if (!db.CompanyHolidays.Any())
                {
                    var (jy, _, _) = Shared.PersianDate.FromGregorian(DateTime.Now);
                    var added = SeedOfficialHolidays(db, jy);
                    Console.WriteLine(added > 0
                        ? $"[DB] {added} تعطیل رسمی سال {jy} از کاتالوگ وارد شد."
                        : "[DB] تعطیل رسمی برای سال جاری یافت نشد.");
                }

                // دسته‌های هزینه پیش‌فرض (در اولین اجرا — کاربر می‌تواند مدیریتشان کند)
                if (!db.ExpenseCategories.Any())
                {
                    foreach (var name in new[] { "اجاره", "حقوق و دستمزد", "قبوض (آب/برق/گاز/تلفن)", "حمل و نقل", "ملزومات و اداری", "پذیرایی", "تعمیر و نگهداری", "تبلیغات و بازاریابی", "متفرقه" })
                        db.ExpenseCategories.Add(new ExpenseCategory { Name = name, CreatedAt = DateTime.Now });
                    db.SaveChanges();
                    Console.WriteLine("[DB] دسته‌های هزینه پیش‌فرض ساخته شدند.");
                }

                // دیتابیس‌های قدیمی: کالاهای بدون انبار اختصاصی به «انبار مرکزی» اختصاص می‌یابند
                var centralWh = db.Warehouses.FirstOrDefault(w => w.Name.Contains("مرکزی"))
                                ?? db.Warehouses.OrderBy(w => w.Id).FirstOrDefault();
                if (centralWh is not null)
                {
                    var orphan = db.Products.Where(p => p.WarehouseId == null && !p.IsService).ToList();
                    if (orphan.Count > 0)
                    {
                        foreach (var p in orphan) p.WarehouseId = centralWh.Id;
                        db.SaveChanges();
                        Console.WriteLine($"[DB] {orphan.Count} کالا به «{centralWh.Name}» اختصاص یافت.");
                    }
                }

                Console.WriteLine("[DB] اتصال و آماده‌سازی دیتابیس با موفقیت انجام شد ✔");
                return;
            }
            catch (Exception ex) when (!provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase)
                                        && (ex.GetType().Name == "SqlException" || ex is InvalidOperationException))
            {
                Console.WriteLine($"[DB] تلاش {attempt} از {maxAttempts} برای اتصال به دیتابیس ناموفق بود: {ex.Message}");

                if (attempt == maxAttempts)
                {
                    Console.WriteLine();
                    Console.WriteLine("================= خطای اتصال به SQL Server =================");
                    Console.WriteLine("برنامه نتوانست به SQL Server وصل شود. موارد زیر را بررسی کنید:");
                    Console.WriteLine("  1) سرویس SQL Server در حال اجرا باشد:");
                    Console.WriteLine("     services.msc → SQL Server (MSSQLSERVER) → Start");
                    Console.WriteLine("  2) اگر نسخه Express دارید، آدرس سرور باید .\\SQLEXPRESS باشد؛");
                    Console.WriteLine("     در appsettings.json مقدار Server=. را به Server=.\\SQLEXPRESS تغییر دهید.");
                    Console.WriteLine("  3) پروتکل TCP/IP یا Shared Memory در SQL Server Configuration Manager فعال باشد.");
                    Console.WriteLine("  4) رشته اتصال فعلی:");
                    Console.WriteLine($"     {config.GetConnectionString("Default")}");
                    Console.WriteLine("  5) برای تست بدون SQL Server، در appsettings.json مقدار");
                    Console.WriteLine("     Database:Provider را روی \"Sqlite\" و ConnectionStrings:Default را روی");
                    Console.WriteLine("     \"Data Source=inventory.db\" بگذارید.");
                    Console.WriteLine("=============================================================");
                    throw;
                }

                Thread.Sleep(TimeSpan.FromSeconds(3));
            }
        }
    }

    /// <summary>
    /// خودتعمیرِ دیتابیس‌های SQLite که با نسخه‌های قدیمی ساخته شده‌اند:
    /// EnsureCreated() جدول/ستونِ جدید اضافه نمی‌کند؛ پس بخش تقویم کاری را به‌صورت دستی تکمیل می‌کنیم.
    /// </summary>
    private static void EnsureSqliteWorkCalendarSchema(AppDbContext db)
    {
        try
        {
            if (db.Database.GetDbConnection() is not Microsoft.Data.Sqlite.SqliteConnection sqlConn)
                return;

            using var raw = new Microsoft.Data.Sqlite.SqliteConnection(sqlConn.ConnectionString);
            raw.Open();

            int TableCount(string sql)
            {
                using var c = raw.CreateCommand();
                c.CommandText = sql;
                return Convert.ToInt32(c.ExecuteScalar());
            }

            var tableExists = TableCount("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='WorkCalendarDays'") > 0;
            if (!tableExists)
            {
                using (var c = raw.CreateCommand())
                {
                    c.CommandText = @"
                        CREATE TABLE WorkCalendarDays (
                            Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                            Date TEXT NOT NULL,
                            IsWorkday INTEGER NOT NULL,
                            StartTime TEXT,
                            EndTime TEXT,
                            GraceMinutes INTEGER NOT NULL DEFAULT 0,
                            OvertimeHours REAL NOT NULL DEFAULT 0,
                            OvertimeMode INTEGER NOT NULL DEFAULT 0,
                            OvertimeStart TEXT,
                            OvertimeEnd TEXT,
                            Note TEXT,
                            CreatedAt TEXT NOT NULL,
                            UpdatedAt TEXT
                        );
                        CREATE UNIQUE INDEX IF NOT EXISTS IX_WorkCalendarDays_Date ON WorkCalendarDays (Date);";
                    c.ExecuteNonQuery();
                }
                Console.WriteLine("[DB] SQLite: جدول WorkCalendarDays ساخته شد.");
                return;
            }

            var cols = new List<string>();
            using (var c = raw.CreateCommand())
            {
                c.CommandText = "SELECT name FROM pragma_table_info('WorkCalendarDays')";
                using var rd = c.ExecuteReader();
                while (rd.Read()) cols.Add(rd.GetString(0));
            }

            void AddIfMissing(string name, string type, string? def = null)
            {
                if (!cols.Contains(name))
                {
                    using var c = raw.CreateCommand();
                    c.CommandText = $"ALTER TABLE WorkCalendarDays ADD COLUMN {name} {type}{(def == null ? "" : $" DEFAULT {def}")}";
                    c.ExecuteNonQuery();
                    Console.WriteLine($"[DB] SQLite: ستون {name} به WorkCalendarDays اضافه شد.");
                }
            }
            AddIfMissing("GraceMinutes", "INTEGER", "0");
            AddIfMissing("OvertimeHours", "REAL", "0");
            AddIfMissing("OvertimeMode", "INTEGER", "0");
            AddIfMissing("OvertimeStart", "TEXT");
            AddIfMissing("OvertimeEnd", "TEXT");

            // ---- ShiftGroups: ستون‌های شیفت دوپاره ----
            if (TableCount("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ShiftGroups'") > 0)
            {
                var shCols = new List<string>();
                using (var c = raw.CreateCommand())
                {
                    c.CommandText = "SELECT name FROM pragma_table_info('ShiftGroups')";
                    using var rd = c.ExecuteReader();
                    while (rd.Read()) shCols.Add(rd.GetString(0));
                }
                foreach (var colName in new[] { "StartTime2", "EndTime2" })
                {
                    if (!shCols.Contains(colName))
                    {
                        using var c = raw.CreateCommand();
                        c.CommandText = $"ALTER TABLE ShiftGroups ADD COLUMN {colName} TEXT";
                        c.ExecuteNonQuery();
                        Console.WriteLine($"[DB] SQLite: ستون {colName} به ShiftGroups اضافه شد.");
                    }
                }
            }

            // ---- AttendanceSegments: ستون دستگاه ورود ----
            if (TableCount("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='AttendanceSegments'") > 0)
            {
                var segCols = new List<string>();
                using (var c = raw.CreateCommand())
                {
                    c.CommandText = "SELECT name FROM pragma_table_info('AttendanceSegments')";
                    using var rd = c.ExecuteReader();
                    while (rd.Read()) segCols.Add(rd.GetString(0));
                }
                if (!segCols.Contains("EnterDevice"))
                {
                    using var c = raw.CreateCommand();
                    c.CommandText = "ALTER TABLE AttendanceSegments ADD COLUMN EnterDevice TEXT";
                    c.ExecuteNonQuery();
                    Console.WriteLine("[DB] SQLite: ستون EnterDevice به AttendanceSegments اضافه شد.");
                }
            }

            // ---- AuditLogs: جدول لاگ عملیات ----
            if (TableCount("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='AuditLogs'") == 0)
            {
                using var c = raw.CreateCommand();
                c.CommandText = @"
                    CREATE TABLE AuditLogs (
                        Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        At TEXT NOT NULL,
                        UserId INTEGER NULL,
                        Username TEXT NULL,
                        Module TEXT NOT NULL,
                        Action TEXT NOT NULL,
                        HttpMethod TEXT NOT NULL,
                        Path TEXT NULL,
                        Summary TEXT NULL,
                        Payload TEXT NULL,
                        Ip TEXT NULL,
                        Device TEXT NULL,
                        StatusCode INTEGER NOT NULL DEFAULT 0,
                        DurationMs INTEGER NOT NULL DEFAULT 0
                    );
                    CREATE INDEX IF NOT EXISTS IX_AuditLogs_At ON AuditLogs (At);
                    CREATE INDEX IF NOT EXISTS IX_AuditLogs_UserId ON AuditLogs (UserId);";
                c.ExecuteNonQuery();
                Console.WriteLine("[DB] SQLite: جدول AuditLogs ساخته شد.");
            }

            // ---- CompanyHolidays: ستون IsOfficial ----
            if (TableCount("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CompanyHolidays'") > 0)
            {
                var holCols = new List<string>();
                using (var c = raw.CreateCommand())
                {
                    c.CommandText = "SELECT name FROM pragma_table_info('CompanyHolidays')";
                    using var rd = c.ExecuteReader();
                    while (rd.Read()) holCols.Add(rd.GetString(0));
                }
                if (!holCols.Contains("IsOfficial"))
                {
                    using var c = raw.CreateCommand();
                    c.CommandText = "ALTER TABLE CompanyHolidays ADD COLUMN IsOfficial INTEGER NOT NULL DEFAULT 0";
                    c.ExecuteNonQuery();
                    Console.WriteLine("[DB] SQLite: ستون IsOfficial به CompanyHolidays اضافه شد.");
                }
            }

            // ---- WorkCalendarSettings: جدول تنظیمات تقویم کاری ----
            if (TableCount("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='WorkCalendarSettings'") == 0)
            {
                using var c = raw.CreateCommand();
                c.CommandText = @"
                    CREATE TABLE WorkCalendarSettings (
                        Id INTEGER NOT NULL PRIMARY KEY,
                        DefaultStart TEXT NOT NULL,
                        DefaultEnd TEXT NOT NULL,
                        GraceMinutes INTEGER NOT NULL DEFAULT 10,
                        RestDayFlags INTEGER NOT NULL DEFAULT 32,
                        ApplyOfficialHolidays INTEGER NOT NULL DEFAULT 1,
                        UpdatedAt TEXT NOT NULL
                    );";
                c.ExecuteNonQuery();
                Console.WriteLine("[DB] SQLite: جدول WorkCalendarSettings ساخته شد.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] هشدار: خودتعمیرِ شمای SQLite انجام نشد: {ex.Message}");
        }
    }

    /// <summary>ذخیره‌ی اولیه‌ی تعطیلات رسمی یک سال شمسی از کاتالوگ — تعداد واردشده برمی‌گردد.</summary>
    public static int SeedOfficialHolidays(AppDbContext db, int jy)
    {
        var items = OfficialHolidayCatalog.GetForYear(jy);
        var added = 0;
        foreach (var (m, d, name) in items)
        {
            var g = Shared.PersianDate.ToGregorian(jy, m, d);
            if (g == DateTime.MinValue) continue;
            if (db.CompanyHolidays.Any(h => h.HolidayDate.Date == g.Date)) continue;
            db.CompanyHolidays.Add(new CompanyHoliday
            {
                HolidayDate = g,
                Name = name,
                IsOfficial = true,
                CreatedByName = "سیستم (کاتالوگ تعطیلات)",
                CreatedAt = DateTime.Now,
            });
            added++;
        }
        if (added > 0) db.SaveChanges();
        return added;
    }
}
