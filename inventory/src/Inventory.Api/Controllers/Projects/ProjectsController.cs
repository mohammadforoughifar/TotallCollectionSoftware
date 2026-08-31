using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>ورود و خروج پروژه‌ها — ماژول دسترسی: Projects</summary>
[Route("api/projects")]
public class ProjectsController : RbacControllerBase
{
    private const string Module = "Projects";

    public ProjectsController(AppDbContext db) : base(db) { }

    /// <summary>نمایش نام کاربر: نام و نام خانوادگی، در غیر این صورت نام کاربری</summary>
    private static string DisplayOf(User u)
    {
        var full = $"{u.FirstName} {u.LastName}".Trim();
        return string.IsNullOrWhiteSpace(full) ? u.Username : full;
    }

    /// <summary>آیتم‌های کمبو: کاربران لاگین، کارفرماها، انواع فاکتور، پروژه‌ها.
    /// برای کاربران ماژول پروژه یا گزارش کار آزاد است.</summary>
    [HttpGet("lookups")]
    public async Task<IActionResult> Lookups()
    {
        var allowed = await HasAsync(Module, "Read") || await HasAsync("ReportWorks", "Read")
                      || await HasAsync("Karfarmas", "Read") || await HasAsync("TypeFactors", "Read");
        if (!allowed) return StatusCode(403, new { message = "شما به این بخش دسترسی ندارید." });

        var rawUsers = await Db.Users.AsNoTracking().Where(u => u.IsActive)
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
            .ToListAsync();
        var users = rawUsers.Select(u => new LookupItem { Id = u.Id, Name = DisplayOf(u) }).ToList();

        var karfarmas = await Db.KarFarmas.AsNoTracking().Where(k => !k.IsDelete)
            .OrderBy(k => k.Name)
            .Select(k => new LookupItem { Id = k.Id, Name = k.Name })
            .ToListAsync();

        var typeFactors = await Db.TypeFactors.AsNoTracking().Where(t => !t.IsDelete)
            .OrderBy(t => t.Name)
            .Select(t => new LookupItem { Id = t.Id, Name = t.Name })
            .ToListAsync();

        // کمبوی پروژه: نام پروژه + کد پروژه (نه سریال — سریال مستقل از کد است)
        var projects = await Db.ProjectEntryExits.AsNoTracking().Where(p => !p.IsDelete)
            .OrderByDescending(p => p.Id)
            .Select(p => new LookupItem { Id = p.Id, Name = p.ProjectName + " (کد " + p.CodeProject + ")" })
            .ToListAsync();

        // کمبوی «پروژه برگشتی» در فرم: فقط پروژه‌هایی که RE نیستند (برگشتی نمی‌تواند برگشتیِ برگشتی باشد)
        var baseProjects = await Db.ProjectEntryExits.AsNoTracking()
            .Where(p => !p.IsDelete && p.ReturnProjectId <= 0)
            .OrderByDescending(p => p.Id)
            .Select(p => new LookupItem { Id = p.Id, Name = p.ProjectName + " (کد " + p.CodeProject + ")" })
            .ToListAsync();

        return Ok(new ProjectLookups { Users = users, KarFarmas = karfarmas, TypeFactors = typeFactors, Projects = projects, BaseProjects = baseProjects });
    }

    /// <summary>کد پروژه پیشنهادی بعدی = بزرگ‌ترین کد عددی + ۱ (خودکار — فقط نمایشی)</summary>
    [HttpGet("next-serial")]
    public async Task<IActionResult> NextSerial()
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        return Ok(new { next = await NextCodeAsync() });
    }

    /// <summary>بزرگ‌ترین کد عددی (کدهای REn نادیده گرفته می‌شوند) + ۱ — حذف‌شده‌ها هم لحاظ می‌شوند تا کد تکراری صادر نشود</summary>
    private async Task<string> NextCodeAsync()
    {
        var codes = await Db.ProjectEntryExits.AsNoTracking()
            .Where(p => !p.CodeProject.StartsWith("RE"))
            .Select(p => p.CodeProject)
            .ToListAsync();
        var maxCode = codes.Select(c => int.TryParse(c, out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return (maxCode + 1).ToString();
    }

    /// <summary>خروجی اکسل یک پروژه: شیت «اطلاعات پروژه» + شیت «گزارش‌های کار»</summary>
    [HttpGet("{id:int}/export")]
    public async Task<IActionResult> Export(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Export") is { } forbid) return forbid;
        var showFactor = await HasAsync(Module, "ViewFactor");

        var p = await Db.ProjectEntryExits.AsNoTracking()
            .Include(x => x.KarFarma)
            .Include(x => x.TypeFactor)
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDelete);
        if (p is null) return NotFound(new { message = "پروژه پیدا نشد." });

        var reports = await Db.ReportWorks.AsNoTracking()
            .Include(r => r.User)
            .Where(r => r.ProjectId == id && !r.IsDelete)
            .OrderBy(r => r.ReportDate)
            .ToListAsync();

        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("اطلاعات پروژه");
        ws.RightToLeft = true;
        var row = 1;
        void Field(string title, string? value)
        {
            ws.Cell(row, 1).Value = title;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 2).Value = value ?? "—";
            row++;
        }

        Field("کد پروژه", p.CodeProject);
        Field("شماره سریال", p.SerialNumber);
        Field("نام پروژه", p.ProjectName);
        Field("کارفرما", p.KarFarma?.Name);
        if (showFactor) Field("نوع فاکتور", p.TypeFactor?.Name);
        Field("کاربر", p.User is null ? null : DisplayOf(p.User));
        Field("تحویل گیرنده", p.ProjectReceiver);
        if (showFactor) Field("شماره فاکتور", p.FactorNumber);
        Field("قبض خروج", p.GhabzExit);
        Field("کارشناسی اولیه", p.KarshenasiAvalie);
        Field("عدد برگشتی", p.ReturnProjectId > 0 ? $"RE{p.ReturnProjectId}" : "—");
        Field("تاریخ ورود", Shamsi(p.EntryDate));
        Field("تاریخ خروج", Shamsi(p.ExitDate));
        Field("تاریخ خروج موقت", Shamsi(p.TemporaryExitDate));
        Field("تاریخ پرونده (تحویل پرونده)", Shamsi(p.FileDate));
        Field("تاریخ تحویل", Shamsi(p.DeliveryDate));
        Field("تاریخ ثبت پروژه", Shamsi(p.ProjectRegistrationDate));
        Field("تاریخ موردنیاز مشتری", Shamsi(p.CustomerRequiredDate));
        Field("پوشه", p.IsFolder == true ? "دارد" : "ندارد");
        Field("کل زمان مصرفی", $"{(int)p.TotalSpentTime.TotalHours}:{p.TotalSpentTime.Minutes:00} ساعت");
        Field("شرح", p.Description);
        ws.Columns(1, 2).AdjustToContents();

        var wr = wb.Worksheets.Add("گزارش‌های کار");
        wr.RightToLeft = true;
        wr.Cell(1, 1).Value = "تاریخ";
        wr.Cell(1, 2).Value = "کاربر";
        wr.Cell(1, 3).Value = "شروع";
        wr.Cell(1, 4).Value = "پایان";
        wr.Cell(1, 5).Value = "صبحانه";
        wr.Cell(1, 6).Value = "ناهار";
        wr.Cell(1, 7).Value = "ساعت خالص";
        wr.Cell(1, 8).Value = "شرح کار";
        wr.Range(1, 1, 1, 8).Style.Font.Bold = true;
        var r = 2;
        foreach (var x in reports)
        {
            wr.Cell(r, 1).Value = Inventory.Shared.PersianDate.ToShort(x.ReportDate);
            wr.Cell(r, 2).Value = x.User is null ? "" : DisplayOf(x.User);
            wr.Cell(r, 3).Value = x.StartTime.ToString("HH:mm");
            wr.Cell(r, 4).Value = x.EndTime.ToString("HH:mm");
            wr.Cell(r, 5).Value = x.BreakfastTime.ToString("HH:mm");
            wr.Cell(r, 6).Value = x.LunchTime.ToString("HH:mm");
            wr.Cell(r, 7).Value = x.SpentTime.ToString(@"hh\:mm");
            wr.Cell(r, 8).Value = x.WorkDescription;
            r++;
        }
        wr.Cell(r, 6).Value = "جمع:";
        wr.Cell(r, 6).Style.Font.Bold = true;
        wr.Cell(r, 7).Value = $"{(int)p.TotalSpentTime.TotalHours}:{p.TotalSpentTime.Minutes:00}";
        wr.Cell(r, 7).Style.Font.Bold = true;
        wr.Columns(1, 8).AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var fileName = $"project_{p.Id}_{p.SerialNumber}.xlsx";
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    /// <summary>خروجی اکسل از کل پروژه‌ها — هر پروژه یک سطر + سطر جمع ساعات (ستون‌های فاکتور فقط با مجوز رویت)</summary>
    [HttpGet("export")]
    public async Task<IActionResult> ExportAll()
    {
        if (await ForbiddenUnlessAsync(Module, "Export") is { } forbid) return forbid;
        var showFactor = await HasAsync(Module, "ViewFactor");

        var projects = await Db.ProjectEntryExits.AsNoTracking()
            .Include(p => p.KarFarma)
            .Include(p => p.TypeFactor)
            .Include(p => p.User)
            .Include(p => p.ReportWorks.Where(r => !r.IsDelete))
            .AsSplitQuery()
            .Where(p => !p.IsDelete)
            .OrderByDescending(p => p.Id)
            .ToListAsync();

        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("کل پروژه‌ها");
        ws.RightToLeft = true;

        // ---------- سرستون‌ها ----------
        var headers = new List<string>
        {
            "کد پروژه", "نام پروژه", "شماره سریال", "کارفرما", "تحویل گیرنده"
        };
        if (showFactor) { headers.Add("نوع فاکتور"); headers.Add("شماره فاکتور"); }
        headers.AddRange(new[]
        {
            "تاریخ ورود", "تاریخ خروج", "تاریخ خروج موقت", "تاریخ ثبت پروژه", "تاریخ نیاز مشتری",
            "تاریخ تحویل", "تاریخ پرونده", "کارشناسی اولیه", "قبض خروج", "برگشتی",
            "تعداد گزارش کار", "کل زمان مصرفی (ساعت)", "پوشه", "ثبت‌کننده", "شرح"
        });

        for (var i = 0; i < headers.Count; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#EEF2FF");
            c.Style.Border.BottomBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
        }
        ws.SheetView.FreezeRows(1);

        // ---------- ردیف‌ها ----------
        var row = 2;
        var totalTicks = 0L;
        foreach (var p in projects)
        {
            var col = 1;
            ws.Cell(row, col++).Value = p.CodeProject;
            ws.Cell(row, col++).Value = p.ProjectName;
            ws.Cell(row, col++).Value = p.SerialNumber;
            ws.Cell(row, col++).Value = p.KarFarma?.Name ?? "";
            ws.Cell(row, col++).Value = p.ProjectReceiver;
            if (showFactor)
            {
                ws.Cell(row, col++).Value = p.TypeFactor?.Name ?? "";
                ws.Cell(row, col++).Value = p.FactorNumber ?? "";
            }
            ws.Cell(row, col++).Value = Shamsi(p.EntryDate) ?? "";
            ws.Cell(row, col++).Value = Shamsi(p.ExitDate) ?? "";
            ws.Cell(row, col++).Value = Shamsi(p.TemporaryExitDate) ?? "";
            ws.Cell(row, col++).Value = Shamsi(p.ProjectRegistrationDate) ?? "";
            ws.Cell(row, col++).Value = Shamsi(p.CustomerRequiredDate) ?? "";
            ws.Cell(row, col++).Value = Shamsi(p.DeliveryDate) ?? "";
            ws.Cell(row, col++).Value = Shamsi(p.FileDate) ?? "";
            ws.Cell(row, col++).Value = p.KarshenasiAvalie ?? "";
            ws.Cell(row, col++).Value = p.GhabzExit ?? "";
            ws.Cell(row, col++).Value = p.ReturnProjectId > 0 ? $"RE{p.ReturnProjectId}" : "—";
            ws.Cell(row, col++).Value = p.ReportWorks.Count;
            ws.Cell(row, col++).Value = $"{(int)p.TotalSpentTime.TotalHours}:{p.TotalSpentTime.Minutes:00}";
            ws.Cell(row, col++).Value = p.IsFolder == true ? "دارد" : "ندارد";
            ws.Cell(row, col++).Value = p.User is null ? "" : DisplayOf(p.User);
            ws.Cell(row, col++).Value = p.Description ?? "";
            totalTicks += p.TotalSpentTime.Ticks;
            row++;
        }

        // ---------- سطر جمع ----------
        var total = TimeSpan.FromTicks(totalTicks);
        var totalCol = headers.IndexOf("کل زمان مصرفی (ساعت)") + 1;
        ws.Cell(row, 1).Value = $"جمع کل — {projects.Count} پروژه";
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, totalCol).Value = $"{(int)total.TotalHours}:{total.Minutes:00}";
        ws.Cell(row, totalCol).Style.Font.Bold = true;
        ws.Range(row, 1, row, headers.Count).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#FEF3C7");

        ws.Columns(1, headers.Count).AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var fileName = $"projects_all_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    private static string? Shamsi(DateTime? dt) =>
        dt is null ? null : Inventory.Shared.PersianDate.ToShort(dt.Value);

    /// <summary>لیست ورود/خروج پروژه‌ها با فیلتر</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] int? karfarmaId,
        [FromQuery] int? typeFactorId,
        [FromQuery] int? userId,
        [FromQuery] bool? returned)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;

        // رویت ستون‌های فاکتور فقط با مجوز Projects.ViewFactor
        var showFactor = await HasAsync(Module, "ViewFactor");

        var query = Db.ProjectEntryExits.AsNoTracking()
            .Include(p => p.KarFarma)
            .Include(p => p.TypeFactor)
            .Include(p => p.User)
            .Include(p => p.Attaches.Where(a => !a.IsDelete))
            .Include(p => p.ReportWorks.Where(r => !r.IsDelete))
            .AsSplitQuery()
            .Where(p => !p.IsDelete);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p =>
                p.ProjectName.Contains(search) ||
                p.CodeProject.Contains(search) ||
                p.SerialNumber.Contains(search) ||
                p.ProjectReceiver.Contains(search) ||
                // تطابق با شماره فاکتور فقط برای دارندگان مجوز رویت فاکتور
                (showFactor && p.FactorNumber != null && p.FactorNumber.Contains(search)) ||
                (p.Description != null && p.Description.Contains(search)));
        if (karfarmaId is > 0) query = query.Where(p => p.KarFarmaId == karfarmaId);
        if (typeFactorId is > 0) query = query.Where(p => p.FactorTypeId == typeFactorId);
        if (userId is > 0) query = query.Where(p => p.UserId == userId);
        if (returned == true) query = query.Where(p => p.ReturnProjectId > 0);

        var list = await query.OrderByDescending(p => p.Id).ToListAsync();
        return Ok(list.Select(p => ToDto(p, showFactor)).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var p = await Db.ProjectEntryExits.AsNoTracking()
            .Include(x => x.KarFarma)
            .Include(x => x.TypeFactor)
            .Include(x => x.User)
            .Include(x => x.Attaches.Where(a => !a.IsDelete))
            .Include(x => x.ReportWorks.Where(r => !r.IsDelete))
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDelete);
        if (p is null) return NotFound(new { message = "پروژه پیدا نشد." });
        var showFactor = await HasAsync(Module, "ViewFactor");
        return Ok(ToDto(p, showFactor));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ProjectEntryExitDto dto, [FromServices] Hubs.INotifyService notify)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        var err = await ValidateAsync(dto);
        if (err is not null) return err;

        var entity = new ProjectEntryExit();
        Map(dto, entity);

        // ==================== کد پروژه (خودکار) ====================
        // برگشتی: عدد برگشتی (دستی کاربر) + کد پروژه مبدأ → «REn-کد مبدأ»
        if (dto.ReturnProjectId > 0 || dto.ReturnOfProjectId > 0)
        {
            if (dto.ReturnProjectId <= 0)
                return BadRequest(new { message = "عدد برگشتی را وارد کنید (حداقل ۱)." });
            if (dto.ReturnProjectId > 9999)
                return BadRequest(new { message = "عدد برگشتی معتبر نیست (حداکثر ۹۹۹۹)." });
            if (dto.ReturnOfProjectId <= 0)
                return BadRequest(new { message = "پروژه مبدأ (برگشتی) را انتخاب کنید." });

            var parent = await Db.ProjectEntryExits.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == dto.ReturnOfProjectId && !x.IsDelete);
            if (parent is null)
                return BadRequest(new { message = "پروژه مبدأ (برگشتی) پیدا نشد." });

            var code = $"RE{dto.ReturnProjectId}-{parent.CodeProject}";
            if (await Db.ProjectEntryExits.AnyAsync(x => !x.IsDelete && x.CodeProject == code))
                return BadRequest(new { message = $"کد پروژه «{code}» قبلاً ثبت شده است. عدد برگشتی دیگری وارد کنید." });

            entity.CodeProject = code;
            entity.ReturnProjectId = dto.ReturnProjectId;
        }
        else
        {
            entity.CodeProject = await NextCodeAsync();
            entity.ReturnProjectId = 0;
        }

        // اطلاعات فاکتور فقط از فرم مجزای فاکتور ثبت می‌شود (بعداً تکمیل می‌گردد)
        entity.FactorTypeId = null;
        entity.FactorNumber = null;
        // کاربر ثبت‌کننده همیشه کاربر لاگین فعلی است (تو بک‌اند — اعتماد به کلاینت نمی‌کنیم)
        entity.UserId = MyUserId;
        entity.CreatedAt = DateTime.Now;
        entity.TotalSpentTime = TimeSpan.Zero;
        // گردش‌کار: هر پروژهٔ تازه‌ثبت‌شده اول به کارتابل مدیر می‌رود (درخواست کاربر)
        entity.FlowStatus = 0;

        Db.ProjectEntryExits.Add(entity);
        await Db.SaveChangesAsync();

        // زنگ اعلان برای مدیران کارتابل + اعلام زنده (SignalR) برای رفرش لیست‌ها
        await NotifyProjectEventAsync(notify, "Manager",
            "پروژه جدید ثبت شد",
            $"«{entity.ProjectName}» (کد {entity.CodeProject}) ثبت شد و در کارتابل مدیر قرار گرفت.",
            $"/project-cartable?queue=manager");

        return Ok(new { id = entity.Id, codeProject = entity.CodeProject });
    }

    /// <summary>اعلان زنگ به کاربران دارای مجوزِ اکشنِ کارتابل + پخش بلادرنگ «projects» برای رفرش لیست‌ها (SignalR)</summary>
    private async Task NotifyProjectEventAsync(Hubs.INotifyService? notify, string cartableAction, string title, string body, string link)
    {
        try
        {
            if (notify is not null)
            {
                var targets = await UsersWithPermissionAsync("ProjectCartable", cartableAction, excludeSelf: true);
                await notify.SendManyAsync(targets, title, body, DisplayNameOf(MyUserId), "مدیریت پروژه‌ها", link);
                await notify.BroadcastChangedAsync("projects");
            }
        }
        catch { /* اعلان نباید مانع عملیات اصلی شود */ }
    }

    /// <summary>نام نمایشی ثبت‌کننده (از دیتابیس)</summary>
    private string DisplayNameOf(int userId)
    {
        var u = Db.Users.Find(userId);
        if (u is null) return MyUsername;
        var full = $"{u.FirstName} {u.LastName}".Trim();
        return string.IsNullOrWhiteSpace(full) ? u.Username : full;
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ProjectEntryExitDto dto, [FromServices] Hubs.INotifyService notify)
    {
        if (await ForbiddenUnlessAsync(Module, "Update") is { } forbid) return forbid;
        var err = await ValidateAsync(dto, id);
        if (err is not null) return err;

        var entity = await Db.ProjectEntryExits.FirstOrDefaultAsync(p => p.Id == id && !p.IsDelete);
        if (entity is null) return NotFound(new { message = "پروژه پیدا نشد." });

        var originUserId = entity.UserId; // ثبت‌کننده اصلی حفظ می‌شود
        Map(dto, entity); // Map اطلاعات فاکتور و کد پروژه/عدد برگشتی را دست نمی‌زند
        entity.UserId = originUserId;
        await Db.SaveChangesAsync();
        try { await notify.BroadcastChangedAsync("projects"); } catch { }
        return Ok(new { id = entity.Id });
    }

    /// <summary>ثبت/ویرایش اطلاعات فاکتور پروژه (شماره + نوع فاکتور) — فرم مجزا از منوی سطر</summary>
    [HttpPut("{id:int}/factor")]
    public async Task<IActionResult> UpdateFactor(int id, [FromBody] ProjectFactorDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Update") is { } forbid) return forbid;

        var entity = await Db.ProjectEntryExits.FirstOrDefaultAsync(p => p.Id == id && !p.IsDelete);
        if (entity is null) return NotFound(new { message = "پروژه پیدا نشد." });

        if (dto.FactorTypeId <= 0 || !await Db.TypeFactors.AnyAsync(t => t.Id == dto.FactorTypeId && !t.IsDelete))
            return BadRequest(new { message = "نوع فاکتور انتخاب نشده یا معتبر نیست." });

        entity.FactorTypeId = dto.FactorTypeId;
        var fn = dto.FactorNumber?.Trim();
        entity.FactorNumber = string.IsNullOrWhiteSpace(fn) ? null : fn;
        await Db.SaveChangesAsync();
        return Ok(new { id = entity.Id });
    }

    /// <summary>حذف نرم پروژه + گزارش‌های کار و ثبت حذف پیوست‌هایش (فایل رمزنگاری‌شده هم حذف می‌شود)</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromServices] Services.IProjectFileProtection protect, [FromServices] Hubs.INotifyService notify)
    {
        if (await ForbiddenUnlessAsync(Module, "Delete") is { } forbid) return forbid;

        var entity = await Db.ProjectEntryExits
            .Include(p => p.Attaches.Where(a => !a.IsDelete))
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDelete);
        if (entity is null) return NotFound(new { message = "پروژه پیدا نشد." });

        entity.IsDelete = true;

        var reports = await Db.ReportWorks.Where(r => r.ProjectId == id && !r.IsDelete).ToListAsync();
        foreach (var r in reports) r.IsDelete = true;

        foreach (var a in entity.Attaches)
        {
            a.IsDelete = true;
            try { protect.Delete(a.StoredFileName); } catch { /* فایل شاید از قبل نباشد */ }
        }

        await Db.SaveChangesAsync();
        try { await notify.BroadcastChangedAsync("projects"); } catch { }
        return Ok(new { ok = true });
    }

    // ==================== کمکی ====================

    private async Task<IActionResult?> ValidateAsync(ProjectEntryExitDto dto, int excludeId = 0)
    {
        if (string.IsNullOrWhiteSpace(dto.ProjectName))
            return BadRequest(new { message = "نام پروژه الزامی است." });
        if (string.IsNullOrWhiteSpace(dto.SerialNumber))
            return BadRequest(new { message = "شماره سریال پروژه الزامی است (دستی وارد شود — مستقل از کد پروژه)." });
        var serial = dto.SerialNumber.Trim();
        var dup = await Db.ProjectEntryExits.AnyAsync(p => p.Id != excludeId && !p.IsDelete && p.SerialNumber == serial);
        if (dup)
            return BadRequest(new { message = "این شماره سریال قبلاً برای پروژه دیگری ثبت شده است." });
        if (string.IsNullOrWhiteSpace(dto.SerialNumber))
            return BadRequest(new { message = "شماره سریال پروژه الزامی است (دستی وارد شود — مستقل از کد پروژه)." });
        if (string.IsNullOrWhiteSpace(dto.ProjectReceiver))
            return BadRequest(new { message = "تحویل گیرنده پروژه الزامی است." });
        if (!await Db.KarFarmas.AnyAsync(k => k.Id == dto.KarFarmaId && !k.IsDelete))
            return BadRequest(new { message = "کارفرما انتخاب نشده یا معتبر نیست." });
        return null;
    }

    private static void Map(ProjectEntryExitDto dto, ProjectEntryExit e)
    {
        e.SerialNumber = dto.SerialNumber.Trim();
        e.ProjectName = dto.ProjectName.Trim();
        // نکته: CodeProject و ReturnProjectId اینجا نیستند — کد فقط موقع ایجاد صادر می‌شود و بعداً تغییر نمی‌کند
        e.GhabzExit = dto.GhabzExit?.Trim();
        // نکته: FactorNumber و FactorTypeId عمداً اینجا نیستند — فقط از UpdateFactor تغییر می‌کنند
        e.KarshenasiAvalie = dto.KarshenasiAvalie?.Trim();
        e.ProjectReceiver = dto.ProjectReceiver.Trim();
        e.Description = dto.Description?.Trim();
        e.KarFarmaId = dto.KarFarmaId;
        e.UserId = dto.UserId;
        e.ExitDate = dto.ExitDate;
        e.EntryDate = dto.EntryDate;
        e.FileDate = dto.FileDate;
        e.DeliveryDate = dto.DeliveryDate;
        e.TemporaryExitDate = dto.TemporaryExitDate;
        e.ProjectRegistrationDate = dto.ProjectRegistrationDate;
        e.CustomerRequiredDate = dto.CustomerRequiredDate;
        e.IsFolder = dto.IsFolder;
    }

    /// <param name="showFactor">رویت شماره/نوع فاکتور (مجوز Projects.ViewFactor)</param>
    private static ProjectEntryExitDto ToDto(ProjectEntryExit p, bool showFactor) => new()
    {
        Id = p.Id,
        CodeProject = p.CodeProject,
        ReturnProjectId = p.ReturnProjectId,
        SerialNumber = p.SerialNumber,
        ProjectName = p.ProjectName,
        GhabzExit = p.GhabzExit,
        KarshenasiAvalie = p.KarshenasiAvalie,
        ProjectReceiver = p.ProjectReceiver,
        Description = p.Description,
        KarFarmaId = p.KarFarmaId,
        UserId = p.UserId,
        ExitDate = p.ExitDate,
        EntryDate = p.EntryDate,
        FileDate = p.FileDate,
        DeliveryDate = p.DeliveryDate,
        TemporaryExitDate = p.TemporaryExitDate,
        ProjectRegistrationDate = p.ProjectRegistrationDate,
        CustomerRequiredDate = p.CustomerRequiredDate,
        IsFolder = p.IsFolder,
        TotalSpentTime = p.TotalSpentTime,
        KarFarmaName = p.KarFarma?.Name,
        UserName = p.User is null ? null : DisplayOf(p.User),
        AttachCount = p.Attaches.Count,
        ReportWorkCount = p.ReportWorks.Count,
        // فیلدهای فاکتور فقط با مجوز رویت فاکتور برمی‌گردند
        FactorNumber = showFactor ? p.FactorNumber : null,
        FactorTypeId = showFactor ? p.FactorTypeId : null,
        FactorTypeName = showFactor ? p.TypeFactor?.Name : null,
        // گردش‌کار کارتابل
        FlowStatus = p.FlowStatus,
        ManagerNote = p.ManagerNote,
        ExpertNote = p.ExpertNote,
        ManagerActionAt = p.ManagerActionAt,
        ExpertActionAt = p.ExpertActionAt
    };
}
