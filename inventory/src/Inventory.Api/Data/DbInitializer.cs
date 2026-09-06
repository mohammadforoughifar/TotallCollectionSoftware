using Inventory.Api.Services;
using Inventory.Shared;
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

                // ابعاد تحلیلی پیش‌فرض (حسابداری تحلیلی): مرکز هزینه و شعبه
                if (!db.AccDimensions.Any())
                {
                    db.AccDimensions.AddRange(
                        new AccDimension { Code = "CC", Name = "مرکز هزینه", IsSystem = true, SortOrder = 1 },
                        new AccDimension { Code = "BR", Name = "شعبه", IsSystem = true, SortOrder = 2 });
                    db.SaveChanges();
                    Console.WriteLine("[DB] ابعاد تحلیلی پیش‌فرض (مرکز هزینه/شعبه) ساخته شد.");
                }

                // ==================== سامانه مودیان (فاکتور الکترونیکی) ====================
                // تنظیمات پیش‌فرض بدون BaseUrl = حالت شبیه‌سازی ارسال (برای تست)
                if (!db.MoadianSettings.Any())
                {
                    db.MoadianSettings.Add(new MoadianSetting
                    {
                        TaxId = "14000000000",
                        SellerName = "شرکت نمونه بازرگانی",
                        DefaultVatRate = 9,
                        SendIntervalMinutes = 5
                    });
                    db.SaveChanges();
                    Console.WriteLine("[DB] تنظیمات پیش‌فرض مودیان (حالت آزمایشی) ساخته شد.");
                }

                if (!db.MoadianCpcList.Any())
                {
                    db.MoadianCpcList.AddRange(SeedCpc());
                    db.SaveChanges();
                    Console.WriteLine("[DB] فهرست شناسه‌های کالا/خدمت (CPC) بارگذاری شد.");
                }

                if (!db.MoadianFiscalPeriods.Any())
                {
                    var (fy, fm, _) = PersianDate.FromGregorian(DateTime.Now);
                    db.MoadianFiscalPeriods.Add(new MoadianFiscalPeriod { Year = fy, Month = fm });
                    db.SaveChanges();
                    Console.WriteLine("[DB] دوره مالیاتی جاری مودیان ساخته شد.");
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

                // ============ انبارداری: انواع پیش‌فرض رسید و حواله ============
                // ماهیت هر نوع، اثر سند روی موجودی را مشخص می‌کند:
                // افزایشی = رسید | کاهشی = حواله | خنثی = انتقال بین انبار یا سند یادداشتی
                if (!db.InvDocTypes.Any())
                {
                    db.InvDocTypes.AddRange(
                        // ---------- رسیدها (افزایشی) ----------
                        new InvDocType { Code = "RC-OPEN", Name = "رسید موجودی اول دوره", Nature = StockNature.Increase, RequiresPrice = true, NumberPrefix = "RCO", Color = "success", Icon = "bi-box-arrow-in-down", IsSystem = true, SortOrder = 1 },
                        new InvDocType { Code = "RC-BUY", Name = "رسید خرید", Nature = StockNature.Increase, RequiresParty = true, RequiresPrice = true, NumberPrefix = "RCB", Color = "success", Icon = "bi-bag-plus", IsSystem = true, SortOrder = 2 },
                        new InvDocType { Code = "RC-RET", Name = "رسید برگشت از فروش", Nature = StockNature.Increase, RequiresParty = true, RequiresPrice = true, NumberPrefix = "RCR", Color = "success", Icon = "bi-arrow-return-left", SortOrder = 3 },
                        new InvDocType { Code = "RC-PRD", Name = "رسید تولید", Nature = StockNature.Increase, NumberPrefix = "RCP", Color = "success", Icon = "bi-gear-wide-connected", SortOrder = 4 },
                        new InvDocType { Code = "RC-ADJ", Name = "رسید اضافی انبارگردانی", Nature = StockNature.Increase, NumberPrefix = "RCA", Color = "success", Icon = "bi-clipboard-plus", SortOrder = 5 },

                        // ---------- حواله‌ها (کاهشی) ----------
                        new InvDocType { Code = "IS-SELL", Name = "حواله فروش", Nature = StockNature.Decrease, RequiresParty = true, RequiresPrice = true, NumberPrefix = "ISS", Color = "danger", Icon = "bi-cart-dash", IsSystem = true, SortOrder = 6 },
                        new InvDocType { Code = "IS-USE", Name = "حواله مصرف", Nature = StockNature.Decrease, NumberPrefix = "ISU", Color = "danger", Icon = "bi-box-arrow-up", SortOrder = 7 },
                        new InvDocType { Code = "IS-RET", Name = "حواله برگشت از خرید", Nature = StockNature.Decrease, RequiresParty = true, RequiresPrice = true, NumberPrefix = "ISR", Color = "danger", Icon = "bi-arrow-return-right", SortOrder = 8 },
                        new InvDocType { Code = "IS-SCRP", Name = "حواله ضایعات", Nature = StockNature.Decrease, NumberPrefix = "ISC", Color = "danger", Icon = "bi-trash3", SortOrder = 9 },
                        new InvDocType { Code = "IS-ADJ", Name = "حواله کسری انبارگردانی", Nature = StockNature.Decrease, NumberPrefix = "ISA", Color = "danger", Icon = "bi-clipboard-minus", SortOrder = 10 },

                        // ---------- خنثی ----------
                        new InvDocType { Code = "TRN", Name = "انتقال بین انبار", Nature = StockNature.Neutral, IsTransfer = true, NumberPrefix = "TRN", Color = "primary", Icon = "bi-arrow-left-right", IsSystem = true, SortOrder = 11 },
                        new InvDocType { Code = "NOTE", Name = "سند یادداشتی (بدون اثر بر موجودی)", Nature = StockNature.Neutral, NumberPrefix = "NOT", Color = "muted", Icon = "bi-sticky", SortOrder = 12 });

                    db.SaveChanges();
                    Console.WriteLine("[DB] انواع پیش‌فرض رسید و حواله انبار ساخته شدند.");
                }

                // ============ حسابداری: سال مالی و کدینگ پیش‌فرض حساب‌ها ============
                SeedAccounting(db);

                // ============ فاکتور: پیکربندی پیش‌فرض انواع فاکتور ============
                SeedInvoicing(db);

                // ============ خزانه‌داری: صندوق/بانک و قواعد سند خودکار ============
                SeedTreasury(db);

                // انبار پیش‌فرض ماژول انبارداری
                var defaultWh = db.Warehouses.FirstOrDefault(w => w.IsDefault);
                if (defaultWh is null)
                {
                    var first = db.Warehouses.OrderBy(w => w.Id).FirstOrDefault();
                    if (first is not null) { first.IsDefault = true; db.SaveChanges(); }
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

                // ============ اتوماسیون اداری: ساختار پیش‌فرض شماره اندیکاتور ============
                // ترتیب پیش‌فرض: واحد/شماره/سال → مثل MQ/1/1405 (ساختار مرجع کارفرما)
                if (!db.LetterStratures.Any(s => s.TypeForm == 1))
                {
                    db.LetterStratures.AddRange(
                        new LetterStrature { TypeForm = 1, TypeStrature = "واحد" },
                        new LetterStrature { TypeForm = 1, TypeStrature = "شماره" },
                        new LetterStrature { TypeForm = 1, TypeStrature = "سال" });
                    db.SaveChanges();
                    Console.WriteLine("[DB] ساختار پیش‌فرض شماره اندیکاتور (واحد/شماره/سال) ساخته شد.");
                }

                // بازسازی شماره اندیکاتور نامه‌های قدیمی با ساختار جدید
                // (شماره ترتیبی Number ثابت می‌ماند؛ فقط رشته نمایشی بازتولید می‌شود)
                {
                    var structure = db.LetterStratures.Where(s => s.TypeForm == 1)
                        .OrderBy(s => s.StratureId).Select(s => s.TypeStrature).ToList();
                    if (structure.Count > 0)
                    {
                        var unit = config?["Letters:UnitCode"] ?? "MQ";
                        var pc = new System.Globalization.PersianCalendar();
                        var toFix = db.InnerLetters.Where(l => !l.IsDelete).ToList();
                        int fixedCount = 0;
                        foreach (var l in toFix)
                        {
                            var parts = new List<string>();
                            foreach (var p in structure)
                                switch (p)
                                {
                                    case "سال": parts.Add(pc.GetYear(l.DateSabt).ToString()); break;
                                    case "واحد": if (!string.IsNullOrEmpty(unit)) parts.Add(unit); break;
                                    case "شماره": parts.Add(l.Number.ToString()); break;
                                }
                            var newNum = string.Join("/", parts);
                            if (l.LetterNumber != newNum) { l.LetterNumber = newNum; fixedCount++; }
                        }
                        if (fixedCount > 0)
                        {
                            db.SaveChanges();
                            Console.WriteLine($"[DB] شماره اندیکاتور {fixedCount} نامه قدیمی با ساختار جدید بازسازی شد.");
                        }
                    }
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
    /// <summary>
    /// ایجاد سال مالی جاری و کدینگ استاندارد حساب‌ها (گروه ← کل ← معین) در اولین اجرا،
    /// به‌همراه قواعد پیش‌فرض صدور خودکار سند از روی اسناد انبار.
    /// </summary>
    private static void SeedAccounting(AppDbContext db)
    {
        // ---------- سال مالی جاری ----------
        if (!db.AccFiscalYears.Any())
        {
            var todayFa = PersianDate.FromGregorian(DateTime.Now);
            var start = PersianDate.ToGregorian(todayFa.Year, 1, 1);
            var end = PersianDate.ToGregorian(todayFa.Year, 12, PersianDate.DaysInMonth(todayFa.Year, 12));

            db.AccFiscalYears.Add(new AccFiscalYear
            {
                Title = $"سال مالی {todayFa.Year}",
                Code = todayFa.Year.ToString(),
                StartDate = start,
                EndDate = end,
                IsCurrent = true
            });
            db.SaveChanges();
            Console.WriteLine("[DB] سال مالی جاری ساخته شد.");
        }

        // ---------- کدینگ حساب‌ها ----------
        if (!db.AccAccounts.Any())
        {
            // (کد، نام، کد والد، نوع، ماهیت، قابل ثبت، سیستمی)
            var rows = new (string Code, string Name, string? Parent, AccountType Type, AccountNature Nature, bool Postable, bool System)[]
            {
                // ============ ۱) دارایی‌های جاری ============
                ("1",      "دارایی‌های جاری",            null,   AccountType.Asset,     AccountNature.Debit,  false, true),
                ("11",     "موجودی نقد و بانک",          "1",    AccountType.Asset,     AccountNature.Debit,  false, true),
                ("1101",   "صندوق",                      "11",   AccountType.Asset,     AccountNature.Debit,  true,  true),
                ("1102",   "بانک",                       "11",   AccountType.Asset,     AccountNature.Debit,  true,  true),
                ("1103",   "تنخواه‌گردان",                "11",   AccountType.Asset,     AccountNature.Debit,  true,  false),
                ("12",     "حساب‌های دریافتنی",           "1",    AccountType.Asset,     AccountNature.Debit,  false, true),
                ("1201",   "حساب‌های دریافتنی تجاری",     "12",   AccountType.Asset,     AccountNature.Debit,  true,  true),
                ("1202",   "اسناد دریافتنی (چک)",        "12",   AccountType.Asset,     AccountNature.Debit,  true,  false),
                ("13",     "موجودی کالا",                "1",    AccountType.Asset,     AccountNature.Debit,  false, true),
                ("1301",   "موجودی کالا — انبار",        "13",   AccountType.Asset,     AccountNature.Debit,  true,  true),
                ("1302",   "کالای در راه",               "13",   AccountType.Asset,     AccountNature.Debit,  true,  false),
                ("14",     "پیش‌پرداخت‌ها",               "1",    AccountType.Asset,     AccountNature.Debit,  false, false),
                ("1401",   "پیش‌پرداخت خرید",            "14",   AccountType.Asset,     AccountNature.Debit,  true,  false),

                // ============ ۲) دارایی‌های ثابت ============
                ("2",      "دارایی‌های ثابت",             null,   AccountType.Asset,     AccountNature.Debit,  false, false),
                ("21",     "اموال، ماشین‌آلات و تجهیزات",  "2",    AccountType.Asset,     AccountNature.Debit,  false, false),
                ("2101",   "اثاثیه و منصوبات",           "21",   AccountType.Asset,     AccountNature.Debit,  true,  false),
                ("2102",   "ماشین‌آلات",                  "21",   AccountType.Asset,     AccountNature.Debit,  true,  false),
                ("2103",   "استهلاک انباشته",            "21",   AccountType.Asset,     AccountNature.Credit, true,  false),

                // ============ ۳) بدهی‌ها ============
                ("3",      "بدهی‌های جاری",               null,   AccountType.Liability, AccountNature.Credit, false, true),
                ("31",     "حساب‌های پرداختنی",           "3",    AccountType.Liability, AccountNature.Credit, false, true),
                ("3101",   "حساب‌های پرداختنی تجاری",     "31",   AccountType.Liability, AccountNature.Credit, true,  true),
                ("3102",   "اسناد پرداختنی (چک)",        "31",   AccountType.Liability, AccountNature.Credit, true,  false),
                ("32",     "مالیات و عوارض",             "3",    AccountType.Liability, AccountNature.Credit, false, true),
                ("3201",   "مالیات بر ارزش افزوده",      "32",   AccountType.Liability, AccountNature.Both,   true,  true),
                ("3202",   "عوارض",                      "32",   AccountType.Liability, AccountNature.Both,   true,  false),
                ("33",     "پیش‌دریافت‌ها",               "3",    AccountType.Liability, AccountNature.Credit, false, false),
                ("3301",   "پیش‌دریافت فروش",            "33",   AccountType.Liability, AccountNature.Credit, true,  false),

                // ============ ۴) سرمایه ============
                ("4",      "حقوق صاحبان سهام",           null,   AccountType.Equity,    AccountNature.Credit, false, true),
                ("41",     "سرمایه",                     "4",    AccountType.Equity,    AccountNature.Credit, false, true),
                ("4101",   "سرمایه اولیه",               "41",   AccountType.Equity,    AccountNature.Credit, true,  true),
                ("4102",   "سود و زیان انباشته",         "41",   AccountType.Equity,    AccountNature.Both,   true,  true),

                // ============ ۵) درآمد ============
                ("5",      "درآمدها",                    null,   AccountType.Income,    AccountNature.Credit, false, true),
                ("51",     "درآمد عملیاتی",              "5",    AccountType.Income,    AccountNature.Credit, false, true),
                ("5101",   "فروش کالا",                  "51",   AccountType.Income,    AccountNature.Credit, true,  true),
                ("5102",   "برگشت از فروش و تخفیفات",    "51",   AccountType.Income,    AccountNature.Debit,  true,  true),
                ("52",     "درآمد غیرعملیاتی",           "5",    AccountType.Income,    AccountNature.Credit, false, false),
                ("5201",   "سایر درآمدها",               "52",   AccountType.Income,    AccountNature.Credit, true,  false),

                // ============ ۶) بهای تمام‌شده و هزینه‌ها ============
                ("6",      "بهای تمام‌شده و هزینه‌ها",     null,   AccountType.Expense,   AccountNature.Debit,  false, true),
                ("61",     "بهای تمام‌شده کالای فروش‌رفته", "6",   AccountType.Expense,   AccountNature.Debit,  false, true),
                ("6101",   "بهای تمام‌شده کالای فروش‌رفته", "61",  AccountType.Expense,   AccountNature.Debit,  true,  true),
                ("6102",   "کسری و ضایعات انبار",        "61",   AccountType.Expense,   AccountNature.Debit,  true,  true),
                ("62",     "خرید",                       "6",    AccountType.Expense,   AccountNature.Debit,  false, true),
                ("6201",   "خرید کالا",                  "62",   AccountType.Expense,   AccountNature.Debit,  true,  true),
                ("6202",   "برگشت از خرید",              "62",   AccountType.Expense,   AccountNature.Credit, true,  true),
                ("63",     "هزینه‌های عمومی و اداری",     "6",    AccountType.Expense,   AccountNature.Debit,  false, false),
                ("6301",   "حقوق و دستمزد",              "63",   AccountType.Expense,   AccountNature.Debit,  true,  false),
                ("6302",   "اجاره",                      "63",   AccountType.Expense,   AccountNature.Debit,  true,  false),
                ("6303",   "آب، برق، گاز و تلفن",        "63",   AccountType.Expense,   AccountNature.Debit,  true,  false),
                ("6304",   "حمل و نقل",                  "63",   AccountType.Expense,   AccountNature.Debit,  true,  false),
                ("6305",   "سایر هزینه‌ها",               "63",   AccountType.Expense,   AccountNature.Debit,  true,  false)
            };

            var map = new Dictionary<string, AccAccount>();
            var order = 1;
            foreach (var r in rows)
            {
                var level = r.Parent is null
                    ? AccountLevel.Group
                    : map[r.Parent].Level switch
                    {
                        AccountLevel.Group => AccountLevel.General,
                        AccountLevel.General => AccountLevel.Subsidiary,
                        _ => AccountLevel.Detail
                    };

                var acc = new AccAccount
                {
                    Code = r.Code,
                    Name = r.Name,
                    ParentId = r.Parent is null ? null : map[r.Parent].Id,
                    Level = level,
                    Type = r.Type,
                    Nature = r.Nature,
                    IsPermanent = r.Type is AccountType.Asset or AccountType.Liability or AccountType.Equity,
                    IsPostable = r.Postable,
                    IsSystem = r.System,
                    RequiresParty = r.Code is "1201" or "3101",
                    SortOrder = order++
                };
                db.AccAccounts.Add(acc);
                db.SaveChanges();   // برای گرفتن Id جهت گره‌های فرزند
                map[r.Code] = acc;
            }
            Console.WriteLine($"[DB] کدینگ پیش‌فرض حساب‌ها ({rows.Length} حساب) ساخته شد.");
        }

        // ---------- قواعد صدور خودکار سند از روی اسناد انبار ----------
        if (!db.AccInvRules.Any() && db.InvDocTypes.Any() && db.AccAccounts.Any())
        {
            int? Acc(string code) => db.AccAccounts.FirstOrDefault(a => a.Code == code)?.Id;

            var inventory = Acc("1301");     // موجودی کالا — انبار
            var purchase = Acc("6201");      // خرید کالا
            var cogs = Acc("6101");          // بهای تمام‌شده کالای فروش‌رفته
            var scrap = Acc("6102");         // کسری و ضایعات
            var salesReturn = Acc("5102");   // برگشت از فروش
            var purchaseReturn = Acc("6202");// برگشت از خرید
            var capital = Acc("4101");       // سرمایه اولیه (افتتاحیه انبار)

            // (کد نوع سند، حساب طرف مقابل)
            var pairs = new (string DocCode, int? Counter)[]
            {
                ("RC-OPEN", capital),
                ("RC-BUY",  purchase),
                ("RC-RET",  salesReturn),
                ("RC-ADJ",  scrap),
                ("IS-SELL", cogs),
                ("IS-USE",  cogs),
                ("IS-RET",  purchaseReturn),
                ("IS-SCRP", scrap),
                ("IS-ADJ",  scrap)
            };

            foreach (var (docCode, counter) in pairs)
            {
                var type = db.InvDocTypes.FirstOrDefault(t => t.Code == docCode);
                if (type is null || inventory is null || counter is null) continue;

                db.AccInvRules.Add(new AccInvRule
                {
                    DocTypeId = type.Id,
                    InventoryAccountId = inventory,
                    CounterAccountId = counter,
                    UseCostValue = true,
                    // پیش‌فرض غیرفعال: تا وقتی کاربر کدینگ را بررسی و تایید نکرده، سند خودکار صادر نمی‌شود
                    IsActive = false,
                    Description = "قاعده‌ی پیش‌فرض — پس از بررسی کدینگ، آن را فعال کنید."
                });
            }
            db.SaveChanges();
            Console.WriteLine("[DB] قواعد پیش‌فرض سند خودکار انبار ساخته شدند (غیرفعال).");
        }
    }

    /// <summary>
    /// پیکربندی پیش‌فرض انواع فاکتور: نوع سند انبار و حساب‌های سند خودکار.
    /// </summary>
    private static void SeedInvoicing(AppDbContext db)
    {
        if (db.FacRules.Any() || !db.AccAccounts.Any() || !db.InvDocTypes.Any()) return;

        int? Acc(string code) => db.AccAccounts.FirstOrDefault(a => a.Code == code)?.Id;
        int? Doc(string code) => db.InvDocTypes.FirstOrDefault(t => t.Code == code)?.Id;

        var receivable = Acc("1201");    // حساب‌های دریافتنی تجاری
        var payable = Acc("3101");       // حساب‌های پرداختنی تجاری
        var vat = Acc("3201");           // مالیات بر ارزش افزوده
        var cash = Acc("1101");          // صندوق
        var sales = Acc("5101");         // فروش کالا
        var salesReturn = Acc("5102");   // برگشت از فروش و تخفیفات
        var purchase = Acc("6201");      // خرید کالا
        var purchaseReturn = Acc("6202");// برگشت از خرید
        var freightIn = Acc("6304");     // حمل و نقل (هزینه)
        var otherIncome = Acc("5201");   // سایر درآمدها (حمل دریافتی از مشتری)

        db.FacRules.AddRange(
            // فاکتور خرید → رسید خرید | خرید بدهکار، پرداختنی بستانکار
            new FacRule
            {
                Kind = InvoiceKind.Purchase,
                DocTypeId = Doc("RC-BUY"),
                PartyAccountId = payable,
                MainAccountId = purchase,
                VatAccountId = vat,
                CashAccountId = cash,
                ShippingAccountId = freightIn,
                AutoInvDoc = true,
                AutoVoucher = true,
                IsActive = true,
                Description = "خرید کالا بدهکار / حساب‌های پرداختنی (یا صندوق) بستانکار"
            },
            // فاکتور فروش → حواله فروش | دریافتنی بدهکار، فروش بستانکار
            new FacRule
            {
                Kind = InvoiceKind.Sale,
                DocTypeId = Doc("IS-SELL"),
                PartyAccountId = receivable,
                MainAccountId = sales,
                VatAccountId = vat,
                CashAccountId = cash,
                ShippingAccountId = otherIncome,
                AutoInvDoc = true,
                AutoVoucher = true,
                IsActive = true,
                Description = "حساب‌های دریافتنی (یا صندوق) بدهکار / فروش کالا بستانکار"
            },
            // برگشت از خرید → حواله برگشت از خرید
            new FacRule
            {
                Kind = InvoiceKind.PurchaseReturn,
                DocTypeId = Doc("IS-RET"),
                PartyAccountId = payable,
                MainAccountId = purchaseReturn,
                VatAccountId = vat,
                CashAccountId = cash,
                ShippingAccountId = freightIn,
                AutoInvDoc = true,
                AutoVoucher = true,
                IsActive = true,
                Description = "حساب‌های پرداختنی بدهکار / برگشت از خرید بستانکار"
            },
            // برگشت از فروش → رسید برگشت از فروش
            new FacRule
            {
                Kind = InvoiceKind.SaleReturn,
                DocTypeId = Doc("RC-RET"),
                PartyAccountId = receivable,
                MainAccountId = salesReturn,
                VatAccountId = vat,
                CashAccountId = cash,
                ShippingAccountId = otherIncome,
                AutoInvDoc = true,
                AutoVoucher = true,
                IsActive = true,
                Description = "برگشت از فروش بدهکار / حساب‌های دریافتنی بستانکار"
            });

        db.SaveChanges();
        Console.WriteLine("[DB] پیکربندی پیش‌فرض انواع فاکتور ساخته شد.");
    }

    /// <summary>
    /// خزانه‌داری: حساب «اسناد در جریان وصول»، صندوق و بانک پیش‌فرض
    /// و قواعد سند خودکار دریافت/پرداخت.
    /// </summary>
    private static void SeedTreasury(AppDbContext db)
    {
        if (!db.AccAccounts.Any()) return;

        AccAccount? Acc(string code) => db.AccAccounts.FirstOrDefault(a => a.Code == code);

        // ---------- حساب «اسناد در جریان وصول» ----------
        // در کدینگ پیش‌فرض وجود ندارد چون فقط ماژول خزانه به آن نیاز دارد.
        var collection = Acc("1203");
        if (collection is null)
        {
            var parent = Acc("12");   // حساب‌های دریافتنی
            if (parent is not null)
            {
                collection = new AccAccount
                {
                    Code = "1203",
                    Name = "اسناد در جریان وصول",
                    ParentId = parent.Id,
                    Level = AccountLevel.Subsidiary,
                    Type = AccountType.Asset,
                    Nature = AccountNature.Debit,
                    IsPermanent = true,
                    IsPostable = true,
                    IsSystem = false,
                    SortOrder = (db.AccAccounts.Max(a => (int?)a.SortOrder) ?? 0) + 1
                };
                db.AccAccounts.Add(collection);
                db.SaveChanges();
                Console.WriteLine("[DB] حساب «اسناد در جریان وصول» (۱۲۰۳) ساخته شد.");
            }
        }

        // ---------- صندوق و بانک پیش‌فرض ----------
        if (!db.TrsAccounts.Any())
        {
            db.TrsAccounts.Add(new TrsAccount
            {
                Code = "CSH-01",
                Name = "صندوق مرکزی",
                Kind = TreasuryAccountKind.Cash,
                AccountId = Acc("1101")?.Id,
                IsDefault = true,
                IsActive = true,
                SortOrder = 1,
                Description = "صندوق نقدی پیش‌فرض سیستم"
            });

            db.TrsAccounts.Add(new TrsAccount
            {
                Code = "BNK-01",
                Name = "حساب جاری",
                Kind = TreasuryAccountKind.Bank,
                AccountId = Acc("1102")?.Id,
                BankName = "بانک ملت",
                IsActive = true,
                SortOrder = 2,
                Description = "حساب بانکی پیش‌فرض سیستم"
            });

            db.SaveChanges();
            Console.WriteLine("[DB] صندوق و حساب بانکی پیش‌فرض ساخته شدند.");
        }

        // ---------- قواعد سند خودکار خزانه ----------
        if (!db.TrsRules.Any())
        {
            var receivable = Acc("1201")?.Id;      // حساب‌های دریافتنی تجاری
            var payable = Acc("3101")?.Id;         // حساب‌های پرداختنی تجاری
            var chequeIn = Acc("1202")?.Id;        // اسناد دریافتنی (چک)
            var chequeOut = Acc("3102")?.Id;       // اسناد پرداختنی (چک)
            var inCollection = collection?.Id;     // اسناد در جریان وصول
            var salesDiscount = Acc("5102")?.Id;   // برگشت از فروش و تخفیفات
            var purchaseDiscount = Acc("6202")?.Id;// برگشت از خرید (تخفیف دریافتی)
            var bankFee = Acc("6305")?.Id;         // سایر هزینه‌ها (کارمزد بانکی)

            db.TrsRules.AddRange(
                new TrsRule
                {
                    Kind = TreasuryKind.Receipt,
                    PartyAccountId = receivable,
                    ChequeAccountId = chequeIn,
                    CollectionAccountId = inCollection,
                    DiscountAccountId = salesDiscount,
                    FeeAccountId = bankFee,
                    AutoVoucher = true,
                    IsActive = receivable is not null,
                    Description = "صندوق/بانک و اسناد دریافتنی بدهکار / حساب‌های دریافتنی بستانکار"
                },
                new TrsRule
                {
                    Kind = TreasuryKind.Payment,
                    PartyAccountId = payable,
                    ChequeAccountId = chequeOut,
                    CollectionAccountId = null,
                    DiscountAccountId = purchaseDiscount,
                    FeeAccountId = bankFee,
                    AutoVoucher = true,
                    IsActive = payable is not null,
                    Description = "حساب‌های پرداختنی بدهکار / صندوق‌ بانک و اسناد پرداختنی بستانکار"
                });

            db.SaveChanges();
            Console.WriteLine("[DB] قواعد پیش‌فرض سند خودکار خزانه ساخته شد.");
        }
    }

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

    /// <summary>
    /// فهرست اولیه شناسه‌های کالا/خدمت (CPC) برای سامانه مودیان.
    /// این کدها نمونه/آغازین هستند و از بخش «مودیان ← شناسه کالا/خدمت» قابل ویرایش‌اند؛
    /// در عملیات واقعی، کدهای رسمی از فهرست CPC سامانه دریافت می‌شود.
    /// </summary>
    private static List<MoadianCpc> SeedCpc() => new()
    {
        new MoadianCpc { Code = "GENERAL-000", Title = "کالا/خدمت عمومی (پیش‌فرض)", Unit = "عدد", IsActive = true },
        new MoadianCpc { Code = "620000000000", Title = "خدمات رایانه‌ای و نرم‌افزار", EnTitle = "IT & software services", Unit = "خدمت", IsActive = true },
        new MoadianCpc { Code = "820000000000", Title = "خدمات اداری و پشتیبانی", EnTitle = "Administrative services", Unit = "خدمت", IsActive = true },
        new MoadianCpc { Code = "650000000000", Title = "خدمات بیمه و مالی", EnTitle = "Insurance & financial services", Unit = "خدمت", IsActive = true },
        new MoadianCpc { Code = "440000000000", Title = "تجهیزات اداری و رایانه‌ای", EnTitle = "Office equipment", Unit = "عدد", IsActive = true },
        new MoadianCpc { Code = "460000000000", Title = "ملزومات اداری و مصرفی", EnTitle = "Office supplies", Unit = "عدد", IsActive = true },
        new MoadianCpc { Code = "380000000000", Title = "مواد اولیه و قطعات", EnTitle = "Raw materials & parts", Unit = "عدد", IsActive = true },
        new MoadianCpc { Code = "490000000000", Title = "خدمات حمل‌ونقل و لجستیک", EnTitle = "Transport & logistics", Unit = "خدمت", IsActive = true },
        new MoadianCpc { Code = "700000000000", Title = "خدمات تعمیر و نگهداری", EnTitle = "Repair & maintenance", Unit = "خدمت", IsActive = true },
        new MoadianCpc { Code = "810000000000", Title = "خدمات اجاره و لیزینگ", EnTitle = "Rental services", Unit = "خدمت", IsActive = true }
    };
}
