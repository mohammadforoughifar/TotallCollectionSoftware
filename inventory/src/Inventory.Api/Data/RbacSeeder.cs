using Inventory.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

public static class RbacSeeder
{
    // اکشن‌های استاندارد CRUD
    private static readonly string[] CrudActions = { "Create", "Read", "Update", "Delete", "Export" };

    // ماژول‌ها و اکشن‌هایشان — منبع واحد حقیقت برای پرمیشن‌ها
    // ماژول‌های جدید (داشبوردها/گزارش‌ها) به‌صورت خودکار به دیتابیس‌های موجود هم اضافه می‌شوند.
    private static readonly Dictionary<string, string[]> ModuleActions = new()
    {
        ["Products"] = CrudActions,
        ["Stock"] = CrudActions,
        // فروش: + دسترسی «مشاهده سود» — قابل مدیریت به ازای هر نقش
        ["Orders"] = new[] { "Create", "Read", "Update", "Delete", "Export", "ViewProfit" },
        ["Repairs"] = CrudActions,
        // ================== مدیریت برنامه‌نویسان ==================
        // Read: دیدن بورد/اعضا/ماژول‌ها و تاریخچهٔ کار (مبنای نمایش در منو)
        // Create/Update/Delete: ساخت و ویرایش آیتم کاری و عضو
        // Manage: تعیین مالک ماژول و هم‌زمان‌سازی فهرست ماژول‌ها با مخزن
        ["DevTeam"] = new[] { "Read", "Create", "Update", "Delete", "Manage" },
        ["CctvCameras"] = CrudActions,
        ["CctvNvrs"] = CrudActions,
        ["Parties"] = CrudActions,
        ["Expenses"] = CrudActions,
        ["Reports"] = CrudActions,
        ["SystemUsers"] = CrudActions,
        ["SystemCompanies"] = CrudActions,   // کمپانی‌ها
        ["SystemDepartments"] = CrudActions, // واحدها (دپارتمان‌ها)
        // ================== درخواست خدمت آی‌تی ==================
        // Create: ثبت درخواست | ViewCompany: دیدن سیستم‌های کل شرکت | ViewDepartment: دیدن سیستم‌های واحد خود
        // Expert: کارشناس آی‌تی (دریافت ارجاع) | Manage: مدیر آی‌تی (ارجاع و تایید)
        ["ItRequests"] = new[] { "Create", "ViewCompany", "ViewDepartment", "Expert", "Manage" },
        // ================== دستور کار ==================
        // View: مشاهده ماژول (مبنای نمایش در منو) | Create: ساخت دستور کار (حداقل برای خود)
        // AssignOthers: دستور کار به دیگران | Delete: حذف — فقط با مجوز مستقل، به نقش‌های عادی خودکار اعطا نمی‌شود
        ["WorkOrders"] = new[] { "View", "Create", "AssignOthers", "Delete" },
        ["Settings"] = CrudActions,
        ["Warehouses"] = CrudActions,
        // ================== ماژول انبارداری ==================
        // InvDocs: اسناد رسید و حواله — Confirm: قطعی‌سازی | Cancel: ابطال
        ["InvDocs"] = new[] { "Create", "Read", "Update", "Delete", "Export", "Confirm", "Cancel" },
        // InvDocTypes: مدیریت انواع رسید/حواله و ماهیت آن‌ها
        ["InvDocTypes"] = CrudActions,
        // ================== ماژول حسابداری ==================
        // AccAccounts: کدینگ حساب‌ها، سال مالی و قواعد سند خودکار
        ["AccAccounts"] = CrudActions,
        // AccVouchers: اسناد حسابداری — Confirm: قطعی‌سازی | Cancel: ابطال
        ["AccVouchers"] = new[] { "Create", "Read", "Update", "Delete", "Export", "Confirm", "Cancel" },
        // AccDimensions: ابعاد تحلیلی (مراکز هزینه / شعبه) و مقادیر آن‌ها
        ["AccDimensions"] = CrudActions,
        // FixedAssets: دارایی ثابت — Confirm: ارسال استهلاک به حسابداری
        ["FixedAssets"] = new[] { "Create", "Read", "Update", "Delete", "Export", "Confirm" },
        // Budgets: بودجه و کنترل بودجه
        ["Budgets"] = CrudActions,
        // Moadian: سامانه مودیان — Send: ارسال/ابطال/برگشت فاکتورها
        ["Moadian"] = new[] { "Create", "Read", "Update", "Delete", "Export", "Send" },
        // FiscalPrinter: چاپگر مالی — Print: چاپ رسید
        ["FiscalPrinter"] = new[] { "Create", "Read", "Update", "Delete", "Print" },
        // ================== ماژول فاکتور ==================
        // FacInvoices: فاکتور خرید/فروش — Confirm: قطعی‌سازی | Cancel: ابطال
        ["FacInvoices"] = new[] { "Create", "Read", "Update", "Delete", "Export", "Confirm", "Cancel" },
        // ================== ماژول خزانه‌داری ==================
        // TrsAccounts: صندوق، بانک، کارتخوان، تنخواه و تنظیمات خزانه
        ["TrsAccounts"] = CrudActions,
        // TrsVouchers: اسناد دریافت/پرداخت/انتقال — Confirm: قطعی‌سازی | Cancel: ابطال
        ["TrsVouchers"] = new[] { "Create", "Read", "Update", "Delete", "Export", "Confirm", "Cancel" },
        // TrsCheques: چک‌ها — Confirm: اجرای عملیات چک (واگذاری، وصول، برگشت)
        ["TrsCheques"] = new[] { "Create", "Read", "Update", "Delete", "Export", "Confirm" },
        // ================== ماژول انبارگردانی و بارکد ==================
        // StkSessions: دوره‌های انبارگردانی — Apply: صدور اسناد اصلاح | Cancel: لغو دوره
        ["StkSessions"] = new[] { "Create", "Read", "Update", "Delete", "Export", "Apply", "Cancel" },
        // StkBarcodes: بارکدهای کالا، تولید گروهی و چاپ برچسب
        ["StkBarcodes"] = CrudActions,
        // ================== بخش معرف ==================
        ["Referrers"] = CrudActions,                                     // مدیریت معرف‌ها
        ["ReferrerWallets"] = new[] { "Read", "Update", "Export" },      // کیف پول معرف‌ها
        ["ReferrerPanel"] = new[] { "MyDashboard", "MyProducts", "MyWallet", "MyCard" }, // پنل معرف (هر بخش جدا)
        // ================== منابع انسانی: مرخصی و ماموریت ==================
        // Request: ثبت درخواست شخصی | Approve: تایید/رد درخواست دیگران | Report: گزارش ماهانه
        ["LeaveRequests"] = new[] { "Request", "Approve", "Report" },
        // ================== حضور و غیاب ==================
        // SelfCheckin: زدن ورود/خروج برای خود
        // ViewAll: مشاهده لاگ همه پرسنل | ManageShifts: مدیریت شیفت‌ها و اصلاح رکوردها | Report: گزارش ماهانه
        ["Attendance"] = new[] { "SelfCheckin", "ViewAll", "ManageShifts", "Report" },
        // ================== سامانه کامل RADIS-HR V019 ==================
        // Access: ورود به کل ماژول؛ مجوز روی تمام کنترلرهای واردشده نیز در Host اعمال می‌شود.
        ["RadisHr"] = new[] { "Access", "Dashboard", "Employees", "EmployeeEntry", "Attendance", "Payroll", "StatutoryRules", "OrgStructure", "OrgSettings", "Hse", "Finance", "Accounting", "ProductionDaily", "Notices" /* دسترسی تفکیکی هر لینک بن‌سازه */ },
        // ================== دسترسی به ازای هر داشبورد ==================
        ["Dashboards"] = new[] { "Financial", "Management", "Hardware" },
        // ================== دسترسی به ازای هر گزارش ==================
        ["ReportPages"] = new[] { "Kardex", "Reorder" },
        // ================== مدیریت پروژه‌ها ==================
        // ViewFactor: رویت ستون‌های شماره/نوع فاکتور در لیست و جزئیات و اکسل (حساس مالی)
        ["Projects"] = new[] { "Create", "Read", "Update", "Delete", "Export", "ViewFactor" }, // ورود و خروج پروژه‌ها
        ["ReportWorks"] = CrudActions,                         // گزارش‌های کار
        ["Karfarmas"] = CrudActions,                           // کارفرماها
        ["TypeFactors"] = CrudActions,                         // انواع فاکتور
        ["ProjectAttach"] = new[] { "Create", "Read", "Delete" }, // پیوست‌های پروژه
        ["ProjectCartable"] = new[] { "Read", "Manager", "Expert" }, // کارتابل پروژه — Read=مشاهده، Manager=تایید/رد مدیر، Expert=اتمام کارشناسی
        // ================== اتوماسیون اداری — نامه داخلی ==================
        // Create: ثبت و ارسال نامه (و مدیریت پیش‌نویس‌ها/گروه‌های گیرندگان)
        // Read: کارتابل، مشاهده نامه و پیوست‌ها | Erja: ارجاع نامه به دیگران
        // Delete: حذف نامه/گروه (مدیرانه)
        ["InnerLetters"] = new[] { "Create", "Read", "Erja", "Delete", "ViewAll" },
        // ================== آرشیو اسناد و مدارک ==================
        ["DocArchive"] = new[] { "Read", "Create", "Delete", "Manage", "Export" },
        // ================== هسته پرسنلی (کارگزینی) ==================
        // Read: مشاهده پرونده/چارت/قرارداد/احکام | Create: ثبت جدید | Update: ویرایش + اجرای حکم
        // Delete: حذف/غیرفعال‌سازی | Manage: داشبورد مدیریتی و گزارش‌ها
        ["HrCore"] = new[] { "Read", "Create", "Update", "Delete", "Manage", "Dashboard", "Contracts", "ContractTemplates", "Decrees", "Recruitment" /* دسترسی تفکیکی هر لینک */ },
        // حقوق و دستمزد: Read=مشاهده فیش و گزارش‌ها | Manage=تعریف آیتم، محاسبه، قفل دوره
        ["HrPay"] = new[] { "Read", "Manage" },
        // ================== منابع انسانی اصلی — مدیریت پایه سازمانی ==================
        // Read/Create/Update/Delete: دسترسی عمومی به کل ماژول (پیش‌فرض نقش‌های قدیمی)
        // Manage: داشبورد مدیریتی و گزارش‌ها
        // دسترسی‌های تفکیکی زیر در HrMainController واقعاً چک می‌شوند (نه فقط تزئینی) و
        // اجازه می‌دهند بدون مجوز عمومی، فقط بخش مشخصی مدیریت شود:
        //   Overview  → GET overview
        //   Company   → پروفایل شرکت + لوگو
        //   Org       → ساختار سازمانی (گره‌ها)
        //   Positions → پست‌های سازمانی
        //   Branches  → شعب و دفاتر
        //   Calendar  → زبان/تقویم + تعطیلات رسمی
        //   Rules     → قوانین پیش‌فرض مرخصی/تأخیر/اضافه‌کاری (عمداً از Update عمومی جداست
        //               چون این پارامترها محاسبات همه پرسنل را تحت‌تأثیر قرار می‌دهند)
        ["HrMain"] = new[] { "Read", "Create", "Update", "Delete", "Manage", "Overview", "Company", "Org", "Positions", "Branches", "Calendar", "Rules" },
        // ================== حضور و غیاب فروغ آریا (ماژول جدید و مستقل) ==================
        // Read: مشاهده شیفت/وضعیت روزانه/گزارش‌ها | Create: وب‌کلاک و ثبت تردد
        // Update: ویرایش + محاسبه مجدد + تأیید ماموریت/مرخصی | Delete: حذف | Manage: داشبورد مدیریتی
        ["FaAtt"] = new[] { "Read", "Create", "Update", "Delete", "Manage", "Clock", "Daily", "Shifts", "LeaveTypes", "Missions", "Approvals", "Leaves", "MyLeaves", "Balances", "Devices", "Reports" /* دسترسی تفکیکی هر لینک */ },
        // ================== حقوق و دستمزد فروغ آریا (ماژول جدید و مستقل) ==================
        // Read: مشاهده فیش خود و گزارش‌ها | Manage: تنظیمات، محاسبه، نهایی‌سازی و پرداخت
        ["FaPay"] = new[] { "Read", "Create", "Update", "Delete", "Manage", "Dashboard", "Runs", "My", "Adjustments", "Items", "Settings", "Reports" /* دسترسی تفکیکی هر لینک */ },
        // ================== آموزش و توسعه فروغ آریا (ماژول جدید و مستقل) ==================
        // Read: مشاهده دوره‌ها | Create: ثبت‌نام و ثبت نیاز | Manage: مدیریت کامل LMS
        ["FaLms"] = new[] { "Read", "Create", "Update", "Delete", "Manage", "Dashboard", "Courses", "Calendar", "Enrollments", "Needs", "Budgets", "Certificates", "My", "Reports" /* دسترسی تفکیکی هر لینک */ },
        // ================== ارتباطات داخلی فروغ آریا (ماژول جدید و مستقل) ==================
        // Read: مشاهده اطلاعیه‌ها و تیکت خود | Create: ثبت تیکت | Manage: مدیریت اطلاعیه‌ها و پاسخ تیکت‌ها
        ["FaCom"] = new[] { "Read", "Create", "Update", "Delete", "Manage", "Dashboard", "Announcements", "Tickets", "My" /* دسترسی تفکیکی هر لینک */ },
        // ================== صفحات شخصی هر کاربر ==================
        // View: نمایش آیتم در منوی «داشبوردها» و ورود به صفحهٔ کارتابل من / بایگانی شخصی
        ["MyCartable"] = new[] { "View" },
        ["MyArchive"] = new[] { "View" },
        // ================== داشبورد شخصی (طراحی‌شده توسط خود کاربر) ==================
        // View: دیدن «داشبورد من» و ویجت‌ها | Design: ساخت/ویرایش/حذف داشبورد و چیدمان ویجت‌ها
        // دادهٔ هر ویجت جداگانه با مجوز مشاهدهٔ ماژول خودش کنترل می‌شود.
        ["MyDashboards"] = new[] { "View", "Design" },
        // ================== گزارش‌ساز حرفه‌ای ==================
        // View: دیدن و اجرای گزارش‌های خود/اشتراکی | Design: ساخت و ویرایش گزارش
        // Share: اشتراک‌گذاری گزارش با کاربران و نقش‌ها
        // نکته: دسترسی به «دادهٔ» هر جدول جداگانه با RBAC همان ماژول کنترل می‌شود،
        // پس اشتراک‌گذاری هرگز مجوز داده را دور نمی‌زند.
        ["ReportStudio"] = new[] { "View", "Design", "Share" },
        // ================== پیام‌رسان سازمانی ==================
        // View: مشاهده گفتگوها و پیام‌ها | Send: ارسال پیام | Manage: مدیریت (حذف پیام/گروه)
        ["Chat"] = new[] { "View", "Send", "Manage" },
        // ================== اتوماسیون اداری — نامه صادره (فاز دوم + امضا + دبیرخانه) ==================
        // همان دسترسی‌های داخلی اما برای ماژول صادره + Sign (امضا کننده بودن)
        // Dabirkhane: دبیرخانه نامه صادره — ثبت شماره مقصد و روش ارسال نامه‌های امضا شده
        ["OutgoingLetters"] = new[] { "Create", "Read", "Erja", "Delete", "Sign", "Dabirkhane", "ViewAll" },
        // ================== اتوماسیون اداری — نامه وارده ==================
        // ViewAll: استثنای «دیدن نامه‌های دیگران» (پیش‌فرض هر کس فقط نامه‌های خودش را می‌بیند)
        ["IncomingLetters"] = new[] { "Create", "Read", "Erja", "Delete", "Dabirkhane", "ViewAll" },
        // ================== ایمیل سازمانی (پست الکترونیک) ==================
        // Read: صندوق ایمیل و حساب‌های من | Create: ارسال ایمیل و افزودن حساب
        // Update: ویرایش حساب/نشان/خوانده‌شده | Delete: حذف حساب/پیام
        ["Email"] = new[] { "Create", "Read", "Update", "Delete" },
        // ================== اتوماسیون اداری — ساختار شماره نامه (تنظیمات مدیر سیستم) ==================
        // Read: مشاهده ساختار شماره سه نوع نامه (داخلی/صادره/وارده) در تنظیمات
        // Update: تغییر ترتیب/اجزای ساختار شماره اندیکاتور
        ["LetterStructures"] = new[] { "Read", "Update" }
    };

    public static async Task SeedAsync(AppDbContext db)
    {
        var firstSeed = !await db.Roles.AnyAsync();

        if (firstSeed)
        {
            // Create Roles
            var adminRole = new Role { Name = "Admin", Description = "دسترسی کامل به تمام بخش‌ها", IsActive = true };
            var operatorRole = new Role { Name = "Operator", Description = "دسترسی به عملیات روزمره", IsActive = true };
            var accountantRole = new Role { Name = "Accountant", Description = "دسترسی به مالی و گزارشات", IsActive = true };

            await db.Roles.AddRangeAsync(adminRole, operatorRole, accountantRole);
            await db.SaveChangesAsync();
        }

        // ================== همگام‌سازی پرمیشن‌ها (حتی برای دیتابیس‌های موجود) ==================
        // هر پرمیشنی که در ModuleActions هست ولی در دیتابیس نیست، اضافه می‌شود
        var existing = await db.Permissions
            .Select(p => new { p.Module, p.Action })
            .ToListAsync();
        var existingSet = existing.Select(p => $"{p.Module}:{p.Action}").ToHashSet();

        var newPermissions = new List<Permission>();
        foreach (var (module, actions) in ModuleActions)
        {
            foreach (var action in actions)
            {
                if (!existingSet.Contains($"{module}:{action}"))
                {
                    newPermissions.Add(new Permission
                    {
                        Module = module,
                        Action = action,
                        Description = $"{action} {module}"
                    });
                }
            }
        }

        if (newPermissions.Count > 0)
        {
            await db.Permissions.AddRangeAsync(newPermissions);
            await db.SaveChangesAsync();

            // پرمیشن‌های جدید به‌صورت خودکار به نقش Admin داده می‌شوند
            var admin = await db.Roles.FirstOrDefaultAsync(r => r.Name == "Admin");
            if (admin != null)
            {
                foreach (var perm in newPermissions)
                    db.RolePermissions.Add(new RolePermission { RoleId = admin.Id, PermissionId = perm.Id });
                await db.SaveChangesAsync();
            }

            Console.WriteLine($"[RBAC] {newPermissions.Count} پرمیشن جدید اضافه شد (داشبوردها/گزارش‌ها/معرف).");
        }

        // ================== نقش «معرف» (Referrer) — برای کاربران لاگین معرف ==================
        var referrerRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "Referrer");
        if (referrerRole == null)
        {
            referrerRole = new Role { Name = "Referrer", Description = "معرف — دسترسی به پنل شخصی (کیف پول، محصولات، کارت)", IsActive = true };
            db.Roles.Add(referrerRole);
            await db.SaveChangesAsync();

            // پیش‌فرض: همه‌ی بخش‌های پنل معرف
            var panelPerms = await db.Permissions.Where(p => p.Module == "ReferrerPanel").ToListAsync();
            foreach (var perm in panelPerms)
                db.RolePermissions.Add(new RolePermission { RoleId = referrerRole.Id, PermissionId = perm.Id });
            await db.SaveChangesAsync();
            Console.WriteLine("[RBAC] نقش «Referrer» با دسترسی‌های پنل معرف ساخته شد.");
        }

        // ================== نقش‌های منابع انسانی ==================
        // نقش «مدیر منابع انسانی» — تایید/رد همه‌ی درخواست‌ها + گزارش ماهانه‌ی همه‌ی نیروها (پنل مدیریت)
        var hrManagerRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "HrManager");
        if (hrManagerRole == null)
        {
            hrManagerRole = new Role { Name = "HrManager", Description = "مدیر منابع انسانی — تایید/رد درخواست‌های مرخصی و ماموریت همه‌ی نیروها + گزارش ماهانه", IsActive = true };
            db.Roles.Add(hrManagerRole);
            await db.SaveChangesAsync();
            Console.WriteLine("[RBAC] نقش «HrManager» ساخته شد.");
        }
        var hrPerms = await db.Permissions
            .Where(p => p.Module == "LeaveRequests" && (p.Action == "Request" || p.Action == "Approve" || p.Action == "Report"))
            .ToListAsync();
        var hrHas = await db.RolePermissions.Where(rp => rp.RoleId == hrManagerRole.Id)
            .Select(rp => rp.PermissionId).ToListAsync();
        foreach (var perm in hrPerms.Where(p => !hrHas.Contains(p.Id)))
            db.RolePermissions.Add(new RolePermission { RoleId = hrManagerRole.Id, PermissionId = perm.Id });
        await db.SaveChangesAsync();

        // نقش «کارمند» — فقط کارتابل شخصی (ثبت درخواست مرخصی/ماموریت؛ فقط اطلاعات خودش)
        var employeeRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "Employee");
        if (employeeRole == null)
        {
            employeeRole = new Role { Name = "Employee", Description = "کارمند — کارتابل شخصی منابع انسانی (ثبت و پیگیری درخواست‌های خود)", IsActive = true };
            db.Roles.Add(employeeRole);
            await db.SaveChangesAsync();
            Console.WriteLine("[RBAC] نقش «Employee» ساخته شد.");
        }
        var empHas = await db.RolePermissions.Where(rp => rp.RoleId == employeeRole.Id)
            .Select(rp => rp.PermissionId).ToListAsync();
        var empPerms = await db.Permissions
            .Where(p => p.Module == "LeaveRequests" && p.Action == "Request")
            .ToListAsync();
        foreach (var perm in empPerms.Where(p => !empHas.Contains(p.Id)))
            db.RolePermissions.Add(new RolePermission { RoleId = employeeRole.Id, PermissionId = perm.Id });
        await db.SaveChangesAsync();

        // ================== دسترسی‌های حضور و غیاب ==================
        // کارمند: ثبت ورود/خروج شخصی
        var selfCheckPerm = await db.Permissions.FirstOrDefaultAsync(p => p.Module == "Attendance" && p.Action == "SelfCheckin");
        if (selfCheckPerm != null)
        {
            var hasSelf = await db.RolePermissions.Where(rp => rp.PermissionId == selfCheckPerm.Id && rp.RoleId == employeeRole.Id).AnyAsync();
            if (!hasSelf) { db.RolePermissions.Add(new RolePermission { RoleId = employeeRole.Id, PermissionId = selfCheckPerm.Id }); await db.SaveChangesAsync(); }
        }
        // مدیر منابع انسانی: تمام دسترسی‌های حضور و غیاب
        var hrAttPerms = await db.Permissions.Where(p => p.Module == "Attendance").ToListAsync();
        var hrAttHas = await db.RolePermissions.Where(rp => rp.RoleId == hrManagerRole.Id).Select(rp => rp.PermissionId).ToListAsync();
        foreach (var perm in hrAttPerms.Where(p => !hrAttHas.Contains(p.Id)))
            db.RolePermissions.Add(new RolePermission { RoleId = hrManagerRole.Id, PermissionId = perm.Id });
        await db.SaveChangesAsync();

        // مدیر منابع انسانی: مدیریت پایه سازمانی (HrMain) + کارگزینی (HrCore) + حقوق (HrPay)
        var hrMainModules = new[] { "HrMain", "HrCore", "HrPay", "FaAtt", "FaPay", "FaLms", "FaCom" };
        var hrMainPerms = await db.Permissions.Where(p => hrMainModules.Contains(p.Module)).ToListAsync();
        var hrMainHas = await db.RolePermissions.Where(rp => rp.RoleId == hrManagerRole.Id).Select(rp => rp.PermissionId).ToListAsync();
        foreach (var perm in hrMainPerms.Where(p => !hrMainHas.Contains(p.Id)))
            db.RolePermissions.Add(new RolePermission { RoleId = hrManagerRole.Id, PermissionId = perm.Id });
        await db.SaveChangesAsync();

        // ================== دسترسی پیش‌فرض منابع انسانی برای نقش‌های موجود ==================
        // نقش‌های Operator / Accountant / Referrer هم باید بتوانند برای خودشان درخواست مرخصی و ورود/خروج ثبت کنند
        // (مگر اینکه مدیر بعداً این دسترسی را از صفحه‌ی نقش‌ها و دسترسی‌ها حذف کند).
        var defaultHrAccessRoleNames = new[] { "Operator", "Accountant", "Referrer" };
        var defaultHrRoles = await db.Roles.Where(r => defaultHrAccessRoleNames.Contains(r.Name)).ToListAsync();
        var defaultHrPerms = await db.Permissions
            .Where(p => (p.Module == "LeaveRequests" && p.Action == "Request") ||
                        (p.Module == "Attendance" && p.Action == "SelfCheckin") ||
                        (p.Module == "FaAtt" && (p.Action == "Read" || p.Action == "Create")) ||
                        (p.Module == "FaPay" && p.Action == "Read") ||
                        (p.Module == "FaLms" && (p.Action == "Read" || p.Action == "Create")) ||
                        (p.Module == "FaCom" && (p.Action == "Read" || p.Action == "Create"))).ToListAsync();
        foreach (var perm in defaultHrPerms)
        {
            var existingRp = await db.RolePermissions
                .Where(rp => rp.PermissionId == perm.Id && defaultHrRoles.Select(r => r.Id).Contains(rp.RoleId))
                .Select(rp => rp.RoleId).ToListAsync();
            foreach (var r in defaultHrRoles.Where(r => !existingRp.Contains(r.Id)))
                db.RolePermissions.Add(new RolePermission { RoleId = r.Id, PermissionId = perm.Id });
        }
        await db.SaveChangesAsync();

        // ================== پیام‌رسان سازمانی — دسترسی پیش‌فرض برای همهٔ نقش‌های فعال ==================
        // پرمیشن‌های جدید چت فقط به Admin داده می‌شوند؛ برای حفظ رفتار فعلی (همه بتوانند چت کنند)
        // به سایر نقش‌های فعال هم View و Send داده می‌شود. مدیر می‌تواند بعداً از صفحهٔ نقش‌ها محدود کند.
        var chatDefaultPerms = await db.Permissions
            .Where(p => p.Module == "Chat" && p.Action != "Manage")
            .ToListAsync();
        if (chatDefaultPerms.Count > 0)
        {
            var activeRoles = await db.Roles.Where(r => r.IsActive).ToListAsync();
            foreach (var role in activeRoles)
            {
                var roleHas = await db.RolePermissions.Where(rp => rp.RoleId == role.Id)
                    .Select(rp => rp.PermissionId).ToListAsync();
                foreach (var perm in chatDefaultPerms.Where(p => !roleHas.Contains(p.Id)))
                    db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = perm.Id });
            }
            await db.SaveChangesAsync();
            Console.WriteLine("[RBAC] دسترسی پیش‌فرض پیام‌رسان (View/Send) به نقش‌های فعال داده شد.");
        }

        // ============ کارتابل من / بایگانی شخصی / داشبورد من — دسترسی پیش‌فرض برای همهٔ نقش‌های فعال ============
        // این صفحه‌ها شخصی هر کاربر هستند؛ برای اینکه بعد از ارتقا از منوی کسی حذف نشوند،
        // مجوز مشاهده‌شان به همهٔ نقش‌های فعال داده می‌شود. مدیر می‌تواند بعداً از
        // «تنظیمات ← نقش‌ها و دسترسی‌ها» برای هر نقش بردارد تا آیتم از منو پنهان شود.
        var personalPerms = await db.Permissions
            .Where(p => (p.Module == "MyCartable" || p.Module == "MyArchive") && p.Action == "View")
            .Concat(db.Permissions.Where(p => p.Module == "MyDashboards"))
            .Concat(db.Permissions.Where(p => p.Module == "ReportStudio" && p.Action == "View"))
            .ToListAsync();
        if (personalPerms.Count > 0)
        {
            var activeRolesForPersonal = await db.Roles.Where(r => r.IsActive).ToListAsync();
            foreach (var role in activeRolesForPersonal)
            {
                var roleHas = await db.RolePermissions.Where(rp => rp.RoleId == role.Id)
                    .Select(rp => rp.PermissionId).ToListAsync();
                foreach (var perm in personalPerms.Where(p => !roleHas.Contains(p.Id)))
                    db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = perm.Id });
            }
            await db.SaveChangesAsync();
            Console.WriteLine("[RBAC] دسترسی «کارتابل من / بایگانی شخصی / داشبورد من» به نقش‌های فعال داده شد.");
        }

        if (firstSeed)
        {
            var allPermissions = await db.Permissions.ToListAsync();
            var adminRole = await db.Roles.FirstAsync(r => r.Name == "Admin");
            var operatorRole = await db.Roles.FirstAsync(r => r.Name == "Operator");
            var accountantRole = await db.Roles.FirstAsync(r => r.Name == "Accountant");

            // Admin: همه‌ی پرمیشن‌ها (پرمیشن‌های جدید بالاتر داده شده — تکراری نشود)
            var adminHas = await db.RolePermissions.Where(rp => rp.RoleId == adminRole.Id)
                .Select(rp => rp.PermissionId).ToListAsync();
            foreach (var perm in allPermissions.Where(p => !adminHas.Contains(p.Id)))
                db.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = perm.Id });

            // Operator: عملیات روزمره + داشبوردها و گزارش‌ها
            // نکته: بلوک‌های بالاتر (دسترسی پیش‌فرض منابع انسانی / پیام‌رسان / صفحات شخصی)
            // ممکن است قبلاً به این نقش پرمیشن داده باشند؛ بدون این چک، EF به دلیل
            // «کلید تکراری RolePermission» استثنا می‌دهد.
            var operatorHas = await db.RolePermissions.Where(rp => rp.RoleId == operatorRole.Id)
                .Select(rp => rp.PermissionId).ToListAsync();
            var operatorPerms = allPermissions
                .Where(p => !operatorHas.Contains(p.Id)
                            && ((p.Action is "Read" or "Create" or "Update" && p.Module is not "SystemUsers" and not "Settings")
                                || p.Module is "Dashboards" or "ReportPages"))
                .ToList();
            foreach (var perm in operatorPerms)
                db.RolePermissions.Add(new RolePermission { RoleId = operatorRole.Id, PermissionId = perm.Id });

            // Accountant: مالی و گزارشات + داشبورد مالی
            var accountantHas = await db.RolePermissions.Where(rp => rp.RoleId == accountantRole.Id)
                .Select(rp => rp.PermissionId).ToListAsync();
            var accountantPerms = allPermissions
                .Where(p => !accountantHas.Contains(p.Id)
                            && (p.Module is "Reports" or "Expenses" or "Parties" or "Orders" or "ReportPages"
                                || (p.Module == "Dashboards" && p.Action == "Financial")))
                .ToList();
            foreach (var perm in accountantPerms)
                db.RolePermissions.Add(new RolePermission { RoleId = accountantRole.Id, PermissionId = perm.Id });

            await db.SaveChangesAsync();
            Console.WriteLine("[RBAC] Roles and Permissions seeded successfully.");
        }
    }
}
