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
        // View: مشاهده ماژول | Create: ساخت دستور کار (حداقل برای خود) | AssignOthers: دستور کار به دیگران
        ["WorkOrders"] = new[] { "View", "Create", "AssignOthers" },
        ["Settings"] = CrudActions,
        ["Warehouses"] = CrudActions,
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
        ["InnerLetters"] = new[] { "Create", "Read", "Erja", "Delete" },
        // ================== اتوماسیون اداری — نامه صادره (فاز دوم + امضا) ==================
        // همان دسترسی‌های داخلی اما برای ماژول صادره + Sign (امضا کننده بودن)
        ["OutgoingLetters"] = new[] { "Create", "Read", "Erja", "Delete", "Sign" }
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

        // ================== دسترسی پیش‌فرض منابع انسانی برای نقش‌های موجود ==================
        // نقش‌های Operator / Accountant / Referrer هم باید بتوانند برای خودشان درخواست مرخصی و ورود/خروج ثبت کنند
        // (مگر اینکه مدیر بعداً این دسترسی را از صفحه‌ی نقش‌ها و دسترسی‌ها حذف کند).
        var defaultHrAccessRoleNames = new[] { "Operator", "Accountant", "Referrer" };
        var defaultHrRoles = await db.Roles.Where(r => defaultHrAccessRoleNames.Contains(r.Name)).ToListAsync();
        var defaultHrPerms = await db.Permissions
            .Where(p => (p.Module == "LeaveRequests" && p.Action == "Request") ||
                        (p.Module == "Attendance" && p.Action == "SelfCheckin")).ToListAsync();
        foreach (var perm in defaultHrPerms)
        {
            var existingRp = await db.RolePermissions
                .Where(rp => rp.PermissionId == perm.Id && defaultHrRoles.Select(r => r.Id).Contains(rp.RoleId))
                .Select(rp => rp.RoleId).ToListAsync();
            foreach (var r in defaultHrRoles.Where(r => !existingRp.Contains(r.Id)))
                db.RolePermissions.Add(new RolePermission { RoleId = r.Id, PermissionId = perm.Id });
        }
        await db.SaveChangesAsync();

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
            var operatorPerms = allPermissions
                .Where(p => (p.Action is "Read" or "Create" or "Update" && p.Module is not "SystemUsers" and not "Settings")
                            || p.Module is "Dashboards" or "ReportPages")
                .ToList();
            foreach (var perm in operatorPerms)
                db.RolePermissions.Add(new RolePermission { RoleId = operatorRole.Id, PermissionId = perm.Id });

            // Accountant: مالی و گزارشات + داشبورد مالی
            var accountantPerms = allPermissions
                .Where(p => p.Module is "Reports" or "Expenses" or "Parties" or "Orders" or "ReportPages"
                            || (p.Module == "Dashboards" && p.Action == "Financial"))
                .ToList();
            foreach (var perm in accountantPerms)
                db.RolePermissions.Add(new RolePermission { RoleId = accountantRole.Id, PermissionId = perm.Id });

            await db.SaveChangesAsync();
            Console.WriteLine("[RBAC] Roles and Permissions seeded successfully.");
        }
    }
}
