using Microsoft.Data.SqlClient;
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
            catch (Exception ex) when (ex is SqlException or InvalidOperationException)
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
}
