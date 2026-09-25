using System.Text.Json;
using Inventory.Api.Data;
using Inventory.Api.Services.FaAtt;
using Inventory.Api.Services.FaPay;
using Inventory.Api.Services.Invoicing;
using Inventory.Api.Services.Treasury;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.Services.Ai;

// =====================================================================
// ابزارهای دستیار فروغ آریا — دست‌های اجرایی مدل زبانی.
//
// اصل امنیتی: هر ابزار فقط از سرویس‌های موجود سامانه با شناسه همان کاربر
// استفاده می‌کند؛ یعنی سطح دسترسی ابزار دقیقاً همان چیزی است که کاربر در
// خود سامانه دارد (مثلاً letter_detail برای نامه‌ای که کاربر به آن دسترسی
// ندارد null می‌دهد و ابزار خطای دسترسی برمی‌گرداند).
// =====================================================================

public class AiToolContext
{
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public bool IsAdmin { get; set; }
    public IServiceProvider Services { get; set; } = null!;
    public CancellationToken CancellationToken { get; set; }
}

public interface IAiTool
{
    string Name { get; }
    string Description { get; }
    object ParametersSchema { get; }
    Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx);
}

public class AiToolRegistry
{
    private readonly Dictionary<string, IAiTool> _tools;

    public AiToolRegistry(IEnumerable<IAiTool> tools) =>
        _tools = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

    public List<AiToolSchema> Schemas() => _tools.Values
        .Select(t => new AiToolSchema { Name = t.Name, Description = t.Description, Parameters = t.ParametersSchema })
        .ToList();

    public async Task<string> ExecuteAsync(string name, string argumentsJson, AiToolContext ctx)
    {
        if (!_tools.TryGetValue(name, out var tool))
            return JsonSerializer.Serialize(new { error = "unknown_tool", tool = name });
        try
        {
            using var doc = string.IsNullOrWhiteSpace(argumentsJson)
                ? JsonDocument.Parse("{}")
                : JsonDocument.Parse(argumentsJson);
            return await tool.ExecuteAsync(doc.RootElement, ctx);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = "tool_failed", tool = name, message = ex.Message });
        }
    }
}

internal static class AiToolArgs
{
    public static string? GetString(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object) return null;
        return args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    public static int? GetInt(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object) return null;
        if (!args.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(v.GetString(), out var s) => s,
            _ => null,
        };
    }

    public static bool GetBool(JsonElement args, string name, bool defaultValue = false)
    {
        if (args.ValueKind != JsonValueKind.Object) return defaultValue;
        if (!args.TryGetProperty(name, out var v)) return defaultValue;
        if (v.ValueKind == JsonValueKind.True) return true;
        if (v.ValueKind == JsonValueKind.False) return false;
        if (v.ValueKind == JsonValueKind.String)
            return v.GetString()?.Trim().ToLowerInvariant() is "true" or "1" or "yes";
        return defaultValue;
    }

    public static string StatusFa(int s) => s switch { 0 => "در انتظار", 1 => "تأیید شده", 2 => "رد شده", _ => "نامشخص" };
}

// ---------------- راهنمای هوشمند (قابلیت ۵) ----------------

public class GuideSearchTool : IAiTool
{
    public string Name => "guide_search";
    public string Description => "جستجو در راهنمای قدم‌به‌قدم سامانه. برای سؤال‌های «چطور ...؟» و «کجا ...؟» اول از این ابزار استفاده کن. لینک صفحه مرتبط هم برمی‌گرداند.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            query = new { type = "string", description = "عبارت جستجو به فارسی (مثل: ثبت مرخصی، فاکتور فروش)" },
        },
        required = new[] { "query" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var kb = ctx.Services.GetRequiredService<AiKnowledgeService>();
        var query = AiToolArgs.GetString(args, "query") ?? "";
        var matches = await kb.SearchAsync(query, 4, ctx.CancellationToken);
        return JsonSerializer.Serialize(matches.Select(m => new
        {
            m.Title,
            content = AiTextUtil.Truncate(m.Content, 700),
            m.Category,
            link = m.Link,
        }));
    }
}

// ---------------- منشی کارمندی (قابلیت ۲۲) ----------------

public class MyLeaveBalanceTool : IAiTool
{
    public string Name => "my_leave_balance";
    public string Description => "مانده مرخصی کاربر جاری به تفکیک نوع مرخصی (استحقاقی، استعلاجی و...). برای «چند روز مرخصی دارم؟» از این استفاده کن.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            year = new { type = "integer", description = "سال شمسی (اختیاری؛ خالی = سال جاری)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var fa = ctx.Services.GetRequiredService<IFaAttService>();
        var list = await fa.MyBalancesAsync(ctx.UserId, AiToolArgs.GetInt(args, "year"));
        return JsonSerializer.Serialize(list.Select(b => new
        {
            نوع = b.LeaveTypeName,
            سال = b.Year,
            استحقاق = b.EntitledDays,
            استفاده_شده = b.UsedDays,
            مانده = b.Remaining,
        }));
    }
}

public class MyLeavesTool : IAiTool
{
    public string Name => "my_leaves";
    public string Description => "فهرست درخواست‌های مرخصی کاربر جاری با وضعیت (در انتظار/تأیید/رد).";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            limit = new { type = "integer", description = "تعداد (پیش‌فرض ۵، حداکثر ۲۰)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var fa = ctx.Services.GetRequiredService<IFaAttService>();
        var limit = Math.Clamp(AiToolArgs.GetInt(args, "limit") ?? 5, 1, 20);
        var list = (await fa.MyLeavesAsync(ctx.UserId)).Take(limit).ToList();
        return JsonSerializer.Serialize(list.Select(l => new
        {
            نوع = l.LeaveTypeName,
            از = AiDateUtil.ToFaShort(l.FromDate),
            تا = AiDateUtil.ToFaShort(l.ToDate),
            وضعیت = AiToolArgs.StatusFa(l.Status),
        }));
    }
}

public class MyAttendanceTool : IAiTool
{
    public string Name => "my_attendance_today";
    public string Description => "وضعیت حضور امروز کاربر جاری: شیفت، ساعت ورود/خروج، تأخیر، کارکرد.";
    public object ParametersSchema => new { type = "object", properties = new { } };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var fa = ctx.Services.GetRequiredService<IFaAttService>();
        var t = await fa.MyTodayAsync(ctx.UserId);
        return JsonSerializer.Serialize(new
        {
            تاریخ = AiDateUtil.ToFaShort(t.Date),
            شیفت = t.ShiftName,
            ورود = t.Daily?.FirstIn?.ToString("HH:mm"),
            خروج = t.Daily?.LastOut?.ToString("HH:mm"),
            کارکرد_دقیقه = t.Daily?.WorkMinutes,
            تأخیر_دقیقه = t.Daily?.LateMinutes,
            اضافه_کار_دقیقه = t.Daily?.OvertimeMinutes,
            ناقص = t.Daily?.IsIncomplete,
        });
    }
}

public class MyPayslipTool : IAiTool
{
    public string Name => "my_payslip";
    public string Description => "فیش حقوقی کاربر جاری: ناخالص، کسورات، مالیات، بیمه و خالص پرداختی به‌همراه اقلام. بدون ورودی = آخرین فیش.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            year = new { type = "integer", description = "سال (اختیاری)" },
            month = new { type = "integer", description = "ماه ۱ تا ۱۲ (اختیاری)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var pay = ctx.Services.GetRequiredService<IFaPayService>();
        var slips = await pay.MySlipsAsync(ctx.UserId);
        var year = AiToolArgs.GetInt(args, "year");
        var month = AiToolArgs.GetInt(args, "month");
        var pick = slips
            .Where(s => (year == null || s.RunYear == year) && (month == null || s.RunMonth == month))
            .OrderByDescending(s => s.RunYear).ThenByDescending(s => s.RunMonth)
            .FirstOrDefault();
        if (pick == null)
            return JsonSerializer.Serialize(new { error = "not_found", message = "فیشی برای این دوره یافت نشد." });
        var full = await pay.GetMySlipAsync(pick.Id, ctx.UserId) ?? pick;
        return JsonSerializer.Serialize(new
        {
            دوره = $"{full.RunYear}/{full.RunMonth:00}",
            حقوق_پایه = full.BaseSalary,
            ناخالص = full.GrossEarnings,
            کسورات = full.TotalDeductions,
            مالیات = full.TaxAmount,
            بیمه = full.InsuranceAmount,
            خالص_پرداختی = full.NetPay,
            پرداخت_شده = full.IsPaid,
            اقلام = full.Items.Select(i => new { عنوان = i.Title, مبلغ = i.Amount }),
        });
    }
}

public class MyLoansTool : IAiTool
{
    public string Name => "my_loans";
    public string Description => "وام‌ها و مساعده‌های کاربر جاری: مبلغ، اقساط، پرداخت‌شده و مانده.";
    public object ParametersSchema => new { type = "object", properties = new { } };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<AppDbContext>();
        var extra = ctx.Services.GetRequiredService<IFaPayExtraService>();
        var empId = await db.HrEmployees.AsNoTracking()
            .Where(e => e.SystemUserId == ctx.UserId).Select(e => e.Id).FirstOrDefaultAsync(ctx.CancellationToken);
        if (empId == 0)
            return JsonSerializer.Serialize(new { error = "no_employee", message = "پرونده پرسنلی برای این کاربر یافت نشد." });
        var loans = await extra.ListLoansAsync(empId, null);
        return JsonSerializer.Serialize(loans.Select(l => new
        {
            عنوان = l.Title,
            مبلغ_کل = l.TotalAmount,
            تعداد_اقساط = l.InstallmentCount,
            مبلغ_قسط = l.InstallmentAmount,
            پرداخت_شده = l.PaidAmount,
            مانده = l.RemainingAmount,
        }));
    }
}

public class MyMissionsTool : IAiTool
{
    public string Name => "my_missions";
    public string Description => "مأموریت‌های کاربر جاری با مقصد و وضعیت.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            limit = new { type = "integer", description = "تعداد (پیش‌فرض ۵، حداکثر ۲۰)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var fa = ctx.Services.GetRequiredService<IFaAttService>();
        var limit = Math.Clamp(AiToolArgs.GetInt(args, "limit") ?? 5, 1, 20);
        var list = (await fa.MyMissionsAsync(ctx.UserId)).Take(limit).ToList();
        return JsonSerializer.Serialize(list.Select(m => new
        {
            مقصد = m.Destination,
            از = AiDateUtil.ToFaShort(m.FromDate),
            تا = AiDateUtil.ToFaShort(m.ToDate),
            وضعیت = AiToolArgs.StatusFa(m.Status),
        }));
    }
}

// ---------------- نامه‌ها در گفتگو ----------------

public class MyLettersStatsTool : IAiTool
{
    public string Name => "my_letters_stats";
    public string Description => "آمار کارتابل نامه کاربر جاری: خوانده‌نشده‌ها، ارسالی‌ها، پیش‌نویس‌ها، مهلت‌های نزدیک.";
    public object ParametersSchema => new { type = "object", properties = new { } };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var letters = ctx.Services.GetRequiredService<IInnerLetterService>();
        var s = await letters.GetStatsAsync(ctx.UserId);
        return JsonSerializer.Serialize(new
        {
            خوانده_نشده = s.InboxUnread,
            کل_وارده = s.InboxTotal,
            ارسالی = s.SentTotal,
            پیش_نویس = s.PishnevisTotal,
            مهلت_نزدیک = s.DeadlineSoon,
        });
    }
}

public class MyLettersInboxTool : IAiTool
{
    public string Name => "my_letters_inbox";
    public string Description => "فهرست نامه‌های وارده کاربر جاری (تازه‌ترین‌ها). برای «نامه‌ای دارم؟» و «نامه‌های خوانده‌نشده» استفاده کن.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            unread_only = new { type = "boolean", description = "فقط خوانده‌نشده‌ها (پیش‌فرض false)" },
            limit = new { type = "integer", description = "تعداد (پیش‌فرض ۵، حداکثر ۱۵)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var letters = ctx.Services.GetRequiredService<IInnerLetterService>();
        var limit = Math.Clamp(AiToolArgs.GetInt(args, "limit") ?? 5, 1, 15);
        var page = await letters.GetInboxAsync(ctx.UserId, null, AiToolArgs.GetBool(args, "unread_only"), 1, limit);
        return JsonSerializer.Serialize(new
        {
            تعداد_کل = page.TotalCount,
            نامه_ها = page.Items.Select(l => new
            {
                شناسه = l.LetterId,
                شماره = l.LetterNumber,
                عنوان = l.Title,
                فرستنده = l.Sender,
                تاریخ = AiDateUtil.ToFaShort(l.Date),
                خوانده_شده = l.IsRead,
                فوریت = l.Foriat,
            }),
        });
    }
}

public class LetterDetailTool : IAiTool
{
    public string Name => "letter_detail";
    public string Description => "متن کامل یک نامه + زنجیره ارجاع‌های آن (گردش). فقط اگر کاربر به نامه دسترسی داشته باشد داده برمی‌گردد.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            letter_id = new { type = "integer", description = "شناسه نامه (از خروجی my_letters_inbox)" },
        },
        required = new[] { "letter_id" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var letters = ctx.Services.GetRequiredService<IInnerLetterService>();
        var erja = ctx.Services.GetRequiredService<IErjaService>();
        var id = AiToolArgs.GetInt(args, "letter_id") ?? 0;
        var d = await letters.GetDetailAsync(id, ctx.UserId, ctx.IsAdmin);
        if (d == null)
            return JsonSerializer.Serialize(new { error = "access_denied", message = "نامه یافت نشد یا دسترسی ندارید." });
        List<object> chain = new();
        try
        {
            var tree = await erja.GetGardeshTreeAsync(id, ctx.UserId, ctx.IsAdmin);
            chain = tree.Take(20).Select(n => (object)new
            {
                از = n.Sender,
                به = n.Reciver,
                دستور = AiTextUtil.Truncate(AiTextUtil.StripHtml(n.MatnErja), 250),
                تاریخ = AiDateUtil.ToFaShort(n.Date),
                خوانده = n.IsRead,
                پاسخ = AiTextUtil.Truncate(AiTextUtil.StripHtml(n.Answer), 200),
            }).ToList();
        }
        catch { /* گردش اختیاری است */ }
        return JsonSerializer.Serialize(new
        {
            شماره = d.LetterNumber,
            عنوان = d.Title,
            فرستنده = d.SenderName,
            تاریخ = AiDateUtil.ToFaShort(d.DateSabt),
            متن = AiTextUtil.Truncate(AiTextUtil.StripHtml(d.Text), 3000),
            گیرندگان = d.ReciversGirande.Select(r => r.FullName).Where(x => x != "").Take(10),
            گردش_ارجاع = chain,
        });
    }
}

public class UsersLookupTool : IAiTool
{
    public string Name => "users_lookup";
    public string Description => "جستجوی همکاران (نام و واحد سازمانی) برای پیشنهاد ارجاع نامه یا معرفی مسئول. شناسه کاربر هم برمی‌گرداند.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            search = new { type = "string", description = "نام/نام‌خانوادگی یا واحد (اختیاری؛ خالی = همه، حداکثر ۶۰ نفر)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<AppDbContext>();
        var search = AiTextUtil.NormalizeFa(AiToolArgs.GetString(args, "search"));
        var users = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Username != Data.AiSeeder.AiUsername)
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .Select(u => new { u.Id, u.Username, u.FirstName, u.LastName })
            .Take(200)
            .ToListAsync(ctx.CancellationToken);

        // واحد سازمانی از پروفایل پرسنلی (اتصال با نام کاربری — مثل سرویس چت)
        var deptRows = await (from profile in db.SystemUsers.AsNoTracking()
                              join department in db.SystemDepartments.AsNoTracking()
                                  on profile.DepartmentId equals (int?)department.Id
                              where profile.IsActive && department.IsActive && profile.Username != ""
                              select new { profile.Username, department.Name }).ToListAsync(ctx.CancellationToken);
        var deptMap = deptRows.GroupBy(p => p.Username.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

        var filtered = users
            .Select(u => new
            {
                u.Id,
                نام = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim(),
                واحد = deptMap.TryGetValue((u.Username ?? "").Trim(), out var dpt) ? dpt : null,
            })
            .Where(u => u.نام != "")
            .Where(u => search == "" || AiTextUtil.NormalizeFa(u.نام + " " + (u.واحد ?? "")).Contains(search))
            .Take(60)
            .ToList();
        return JsonSerializer.Serialize(filtered.Select(u => new { شناسه = u.Id, u.نام, u.واحد }));
    }
}

// ---------------- اقدام با تأیید ----------------

public class RequestLeaveTool : IAiTool
{
    public string Name => "request_leave";
    public string Description => "ثبت پیش‌فاکتور درخواست مرخصی (اجرا فقط بعد از تأیید کاربر). تاریخ‌ها را می‌توانی فارسی بدهی: امروز، فردا، پس‌فردا، نام روز هفته، یا ۱۴۰۴/۰۷/۰۵.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            leave_type = new { type = "string", description = "نوع مرخصی (مثلاً استحقاقی، استعلاجی) — خالی = استحقاقی" },
            from_date = new { type = "string", description = "از تاریخ (اجباری): امروز، فردا، پس‌فردا، نام روز، یا ۱۴۰۴/۰۷/۰۵" },
            to_date = new { type = "string", description = "تا تاریخ (اختیاری؛ خالی = همان روز شروع)" },
            reason = new { type = "string", description = "دلیل (اختیاری)" },
        },
        required = new[] { "from_date" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var actions = ctx.Services.GetRequiredService<AiActionService>();
        var fa = ctx.Services.GetRequiredService<IFaAttService>();
        var today = DateTime.Today;

        var fromParsed = AiLeaveHelper.ParseFaDate(AiToolArgs.GetString(args, "from_date"), today);
        if (fromParsed == null)
            return JsonSerializer.Serialize(new { error = "bad_date", message = "تاریخ شروع را نفهمیدم؛ مثلاً بگو «فردا» یا «۱۴۰۴/۰۷/۰۵»." });
        var from = fromParsed.Value;
        var to = AiLeaveHelper.ParseFaDate(AiToolArgs.GetString(args, "to_date"), today) ?? from;
        if (to < from) (from, to) = (to, from);

        var type = await AiLeaveHelper.MatchLeaveTypeAsync(fa, AiToolArgs.GetString(args, "leave_type"));
        if (type == null)
            return JsonSerializer.Serialize(new { error = "no_leave_type", message = "نوع مرخصی فعالی تعریف نشده." });

        var reason = AiToolArgs.GetString(args, "reason");
        var summary = $"مرخصی {type.Value.name} از {AiDateUtil.ToFaShort(from)} تا {AiDateUtil.ToFaShort(to)}" +
                      (string.IsNullOrWhiteSpace(reason) ? "" : $" (دلیل: {reason.Trim()})");
        var pending = await actions.CreateAsync(ctx.UserId, "request_leave",
            JsonSerializer.Serialize(new
            {
                leaveTypeId = type.Value.id,
                from = from.ToString("yyyy-MM-dd"),
                to = to.ToString("yyyy-MM-dd"),
                reason,
            }), summary, ctx.CancellationToken);

        return JsonSerializer.Serialize(new
        {
            action_id = pending.Id,
            summary,
            hint = "برای اجرا، کاربر باید بنویسد: تأیید",
        });
    }
}

public class ClockTool : IAiTool
{
    public string Name => "clock";
    public string Description => "ثبت پیش‌فاکتور ساعت‌زنی ورود/خروج (اجرا فقط بعد از تأیید کاربر).";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            type = new { type = "string", description = "ورود یا خروج (یا in/out)" },
        },
        required = new[] { "type" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var actions = ctx.Services.GetRequiredService<AiActionService>();
        var t = AiTextUtil.NormalizeFa(AiToolArgs.GetString(args, "type"));
        var kind = t switch
        {
            var x when x.Contains("ورود") || x == "in" || x == "0" => 0,
            var x when x.Contains("خروج") || x == "out" || x == "1" => 1,
            _ => -1,
        };
        if (kind < 0)
            return JsonSerializer.Serialize(new { error = "bad_type", message = "مشخص کن: ورود یا خروج؟" });

        var summary = kind == 0 ? "ثبت ساعت ورود (الان)" : "ثبت ساعت خروج (الان)";
        var pending = await actions.CreateAsync(ctx.UserId, "clock",
            JsonSerializer.Serialize(new { type = kind }), summary, ctx.CancellationToken);
        return JsonSerializer.Serialize(new
        {
            action_id = pending.Id,
            summary,
            hint = "برای اجرا، کاربر باید بنویسد: تأیید",
        });
    }
}

public class ConfirmActionTool : IAiTool
{
    public string Name => "confirm_action";
    public string Description => "تأیید و اجرای پیش‌فاکتور باز کاربر. فقط وقتی صدا بزن که کاربر صراحتاً تأیید کرد (مثلاً گفت «تأیید»، «باشه»، «اوکی»). بدون شناسه = تازه‌ترین پیش‌فاکتور.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            action_id = new { type = "integer", description = "شناسه پیش‌فاکتور (اختیاری؛ خالی = تازه‌ترین)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var actions = ctx.Services.GetRequiredService<AiActionService>();
        var (ok, message) = await actions.ConfirmAsync(ctx.UserId, ctx.UserName,
            AiToolArgs.GetInt(args, "action_id"), ctx.CancellationToken);
        return JsonSerializer.Serialize(new { ok, message });
    }
}

public class CancelActionTool : IAiTool
{
    public string Name => "cancel_action";
    public string Description => "لغو پیش‌فاکتور باز کاربر. وقتی کاربر گفت «لغو»، «کنسل» یا پشیمان شد صدا بزن. بدون شناسه = تازه‌ترین.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            action_id = new { type = "integer", description = "شناسه پیش‌فاکتور (اختیاری؛ خالی = تازه‌ترین)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var actions = ctx.Services.GetRequiredService<AiActionService>();
        var (ok, message) = await actions.CancelAsync(ctx.UserId,
            AiToolArgs.GetInt(args, "action_id"), ctx.CancellationToken);
        return JsonSerializer.Serialize(new { ok, message });
    }
}

public class PendingActionsTool : IAiTool
{
    public string Name => "my_pending_actions";
    public string Description => "فهرست پیش‌فاکتورهای باز کاربر (اقدام‌های در انتظار تأیید).";
    public object ParametersSchema => new { type = "object", properties = new { } };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var actions = ctx.Services.GetRequiredService<AiActionService>();
        var list = await actions.ListPendingAsync(ctx.UserId, ctx.CancellationToken);
        return JsonSerializer.Serialize(list.Select(a => new
        {
            شناسه = a.Id,
            اقدام = AiActionNames.Fa(a.Action),
            خلاصه = a.Summary,
        }));
    }
}

// ---------------- نام فارسی اقدام‌ها ----------------

internal static class AiActionNames
{
    public static string Fa(string action) => action switch
    {
        "request_leave" => "درخواست مرخصی",
        "clock" => "ساعت‌زنی",
        "request_mission" => "درخواست مأموریت",
        "decide_leave" => "تأیید/رد مرخصی",
        "answer_referral" => "پاسخ به ارجاع",
        "create_ticket" => "ثبت تیکت پشتیبانی",
        "report_work" => "گزارش‌کار",
        "create_letter_draft" => "پیش‌نویس نامه",
        _ => action,
    };
}

// ---------------- تأییدها و اقدام‌های اجرایی (فاز ۲) ----------------

public class PendingApprovalsTool : IAiTool
{
    public string Name => "pending_approvals";
    public string Description => "مرخصی‌های در انتظار تأییدِ کاربر جاری (برای مدیر/سرپرست: درخواست‌های نیروهای مستقیم). شناسه هر مورد را هم می‌دهد تا با decide_leave تأیید/رد شود. قبل از decide_leave حتماً این را صدا بزن تا شناسه درست را پیدا کنی.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            limit = new { type = "integer", description = "تعداد (پیش‌فرض ۱۰، حداکثر ۳۰)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var fa = ctx.Services.GetRequiredService<IFaAttService>();
        var limit = Math.Clamp(AiToolArgs.GetInt(args, "limit") ?? 10, 1, 30);
        var list = (await fa.TeamLeavesAsync(ctx.UserId)).Take(limit).ToList();
        return JsonSerializer.Serialize(list.Select(l => new
        {
            شناسه = l.Id,
            کارمند = l.EmployeeName,
            نوع = l.LeaveTypeName,
            از = AiDateUtil.ToFaShort(l.FromDate),
            تا = AiDateUtil.ToFaShort(l.ToDate),
            دلیل = l.Reason,
        }));
    }
}

public class MyReferralsPendingTool : IAiTool
{
    public string Name => "my_referrals_pending";
    public string Description => "ارجاع‌های بی‌پاسخ کاربر جاری از کارتابل (نامه‌هایی که باید جواب بدهد). شناسه ارجاع (erja_id) را می‌دهد تا با answer_referral پاسخ/تأیید/رد شود. قبل از answer_referral حتماً این را صدا بزن.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            limit = new { type = "integer", description = "تعداد (پیش‌فرض ۱۰، حداکثر ۲۰)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var letters = ctx.Services.GetRequiredService<IInnerLetterService>();
        var limit = Math.Clamp(AiToolArgs.GetInt(args, "limit") ?? 10, 1, 20);
        var page = await letters.GetInboxAsync(ctx.UserId, null, false, 1, 50);
        var items = page.Items
            .Where(l => l.ErjaId != null && !l.HasAnswer)
            .Take(limit).ToList();
        return JsonSerializer.Serialize(items.Select(l => new
        {
            erja_id = l.ErjaId,
            letter_id = l.LetterId,
            شماره = l.LetterNumber,
            عنوان = l.Title,
            فرستنده = l.Sender,
            پاراف = AiTextUtil.Truncate(AiTextUtil.StripHtml(l.MatnErja ?? ""), 200),
            تاریخ = AiDateUtil.ToFaShort(l.Date),
            مهلت = l.MohlatPasokh == null ? null : AiDateUtil.ToFaShort(l.MohlatPasokh.Value),
        }));
    }
}

public class RequestMissionTool : IAiTool
{
    public string Name => "request_mission";
    public string Description => "ثبت پیش‌فاکتور درخواست مأموریت (اجرا فقط بعد از تأیید کاربر). تاریخ‌ها را می‌توانی فارسی بدهی: امروز، فردا، پس‌فردا، نام روز هفته، یا ۱۴۰۴/۰۷/۰۵.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            destination = new { type = "string", description = "مقصد مأموریت (اجباری)" },
            from_date = new { type = "string", description = "از تاریخ (اجباری)" },
            to_date = new { type = "string", description = "تا تاریخ (اختیاری؛ خالی = همان روز شروع)" },
            reason = new { type = "string", description = "دلیل/موضوع مأموریت (اختیاری)" },
        },
        required = new[] { "destination", "from_date" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var actions = ctx.Services.GetRequiredService<AiActionService>();
        var dest = (AiToolArgs.GetString(args, "destination") ?? "").Trim();
        if (dest == "")
            return JsonSerializer.Serialize(new { error = "bad_destination", message = "مقصد مأموریت مشخص نیست." });
        var today = DateTime.Today;
        var fromParsed = AiLeaveHelper.ParseFaDate(AiToolArgs.GetString(args, "from_date"), today);
        if (fromParsed == null)
            return JsonSerializer.Serialize(new { error = "bad_date", message = "تاریخ شروع را نفهمیدم؛ مثلاً بگو «فردا» یا «۱۴۰۴/۰۷/۰۵»." });
        var from = fromParsed.Value;
        var to = AiLeaveHelper.ParseFaDate(AiToolArgs.GetString(args, "to_date"), today) ?? from;
        if (to < from) (from, to) = (to, from);

        var reason = AiToolArgs.GetString(args, "reason");
        var summary = $"مأموریت {dest} از {AiDateUtil.ToFaShort(from)} تا {AiDateUtil.ToFaShort(to)}" +
                      (string.IsNullOrWhiteSpace(reason) ? "" : $" (دلیل: {reason.Trim()})");
        var pending = await actions.CreateAsync(ctx.UserId, "request_mission",
            JsonSerializer.Serialize(new
            {
                from = from.ToString("yyyy-MM-dd"),
                to = to.ToString("yyyy-MM-dd"),
                destination = dest,
                reason,
            }), summary, ctx.CancellationToken);
        return JsonSerializer.Serialize(new { action_id = pending.Id, summary, hint = "برای اجرا، کاربر باید بنویسد: تأیید" });
    }
}

public class DecideLeaveTool : IAiTool
{
    public string Name => "decide_leave";
    public string Description => "ثبت پیش‌فاکتور تأیید/رد یک مرخصی (اجرا فقط بعد از تأیید کاربر). شناسه را از pending_approvals بگیر. فقط برای مرخصی نیروهای مستقیم کاربر (یا کارشناس منابع انسانی) جواب می‌دهد.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            leave_id = new { type = "integer", description = "شناسه مرخصی از خروجی pending_approvals (اجباری)" },
            approve = new { type = "boolean", description = "true=تأیید، false=رد (اجباری)" },
        },
        required = new[] { "leave_id", "approve" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var actions = ctx.Services.GetRequiredService<AiActionService>();
        var fa = ctx.Services.GetRequiredService<IFaAttService>();
        var leaveId = AiToolArgs.GetInt(args, "leave_id") ?? 0;
        if (leaveId <= 0)
            return JsonSerializer.Serialize(new { error = "bad_id", message = "شناسه مرخصی معتبر نیست؛ اول pending_approvals را ببین." });
        var approve = AiToolArgs.GetBool(args, "approve");

        var leave = (await fa.TeamLeavesAsync(ctx.UserId)).FirstOrDefault(l => l.Id == leaveId);
        var what = approve ? "تأیید" : "رد";
        var summary = leave == null
            ? $"{what} مرخصی شماره {leaveId}"
            : $"{what} مرخصی {leave.EmployeeName} ({leave.LeaveTypeName}، {AiDateUtil.ToFaShort(leave.FromDate)} تا {AiDateUtil.ToFaShort(leave.ToDate)})";
        var pending = await actions.CreateAsync(ctx.UserId, "decide_leave",
            JsonSerializer.Serialize(new { leave_id = leaveId, approve }), summary, ctx.CancellationToken);
        return JsonSerializer.Serialize(new { action_id = pending.Id, summary, hint = "برای اجرا، کاربر باید بنویسد: تأیید" });
    }
}

public class AnswerReferralTool : IAiTool
{
    public string Name => "answer_referral";
    public string Description => "ثبت پیش‌فاکتور پاسخ به یک ارجاع نامه (اجرا فقط بعد از تأیید کاربر). شناسه ارجاع را از my_referrals_pending بگیر. تصمیم: «تأیید» یا «رد» یا «پاسخ» (متن خالی).";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            erja_id = new { type = "integer", description = "شناسه ارجاع از خروجی my_referrals_pending (اجباری)" },
            decision = new { type = "string", description = "تأیید / رد / پاسخ (پیش‌فرض: پاسخ)" },
            text = new { type = "string", description = "متن پاسخ (برای تصمیم «پاسخ» اجباری است)" },
        },
        required = new[] { "erja_id" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var actions = ctx.Services.GetRequiredService<AiActionService>();
        var letters = ctx.Services.GetRequiredService<IInnerLetterService>();
        var erjaId = AiToolArgs.GetInt(args, "erja_id") ?? 0;
        if (erjaId <= 0)
            return JsonSerializer.Serialize(new { error = "bad_id", message = "شناسه ارجاع معتبر نیست؛ اول my_referrals_pending را ببین." });
        var d = AiTextUtil.NormalizeFa(AiToolArgs.GetString(args, "decision"));
        var decision = d.Contains("تایید") || d.Contains("تأیید") || d is "approve" or "1" ? 1
            : d is "رد" or "reject" or "2" || d.Contains("مخالف") ? 2 : 0;
        var text = (AiToolArgs.GetString(args, "text") ?? "").Trim();
        if (decision == 0 && text == "")
            return JsonSerializer.Serialize(new { error = "no_text", message = "متن پاسخ را بگو، یا تصمیم را «تأیید»/«رد» بگذار." });

        // عنوان نامه برای خلاصه خواناتر (اختیاری)
        string title = "";
        try
        {
            var page = await letters.GetInboxAsync(ctx.UserId, null, false, 1, 50);
            title = page.Items.FirstOrDefault(l => l.ErjaId == erjaId)?.Title ?? "";
        }
        catch { /* خلاصه بدون عنوان */ }
        var what = decision == 1 ? "تأیید" : decision == 2 ? "رد" : "پاسخ به";
        var summary = $"{what} ارجاع" + (title == "" ? $" شماره {erjaId}" : $" «{title}»") +
                      (decision == 0 ? $": {AiTextUtil.Truncate(text, 80)}" : "");
        var pending = await actions.CreateAsync(ctx.UserId, "answer_referral",
            JsonSerializer.Serialize(new { erja_id = erjaId, decision, text }), summary, ctx.CancellationToken);
        return JsonSerializer.Serialize(new { action_id = pending.Id, summary, hint = "برای اجرا، کاربر باید بنویسد: تأیید" });
    }
}

public class CreateTicketTool : IAiTool
{
    public string Name => "create_ticket";
    public string Description => "ثبت پیش‌فاکتور تیکت پشتیبانی منابع انسانی (اجرا فقط بعد از تأیید کاربر). دسته: مرخصی، حقوق، بیمه، قرارداد، آموزش یا سایر.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            subject = new { type = "string", description = "موضوع تیکت (اجباری)" },
            body = new { type = "string", description = "شرح مشکل/درخواست (اجباری)" },
            category = new { type = "string", description = "دسته: مرخصی، حقوق، بیمه، قرارداد، آموزش، سایر (پیش‌فرض: سایر)" },
        },
        required = new[] { "subject", "body" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var actions = ctx.Services.GetRequiredService<AiActionService>();
        var subject = (AiToolArgs.GetString(args, "subject") ?? "").Trim();
        var body = (AiToolArgs.GetString(args, "body") ?? "").Trim();
        if (subject == "" || body == "")
            return JsonSerializer.Serialize(new { error = "bad_input", message = "موضوع و شرح تیکت هر دو لازم است." });
        var c = AiTextUtil.NormalizeFa(AiToolArgs.GetString(args, "category"));
        var (category, catName) = c.Contains("مرخص") ? (1, "مرخصی")
            : c.Contains("حقوق") || c.Contains("فیش") || c.Contains("دستمزد") ? (2, "حقوق")
            : c.Contains("بیمه") ? (3, "بیمه")
            : c.Contains("قرارداد") ? (4, "قرارداد")
            : c.Contains("آموزش") ? (5, "آموزش") : (0, "سایر");
        var summary = $"تیکت «{subject}» (دسته: {catName})";
        var pending = await actions.CreateAsync(ctx.UserId, "create_ticket",
            JsonSerializer.Serialize(new { subject, body, category }), summary, ctx.CancellationToken);
        return JsonSerializer.Serialize(new { action_id = pending.Id, summary, hint = "برای اجرا، کاربر باید بنویسد: تأیید" });
    }
}

public class ReportWorkTool : IAiTool
{
    public string Name => "report_work";
    public string Description => "ثبت پیش‌فاکتور گزارش‌کار روزانه روی یک پروژه (اجرا فقط بعد از تأیید کاربر). پروژه را می‌توانی با نام (حتی ناقص) یا شناسه بدهی؛ اگر چند پروژه شبیه هم بود، ابزار فهرست می‌دهد تا دقیق بپرسی.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            project = new { type = "string", description = "نام (کامل یا بخشی) یا شناسه عددی پروژه (اجباری)" },
            description = new { type = "string", description = "شرح کاری که انجام شد (اجباری)" },
            date = new { type = "string", description = "تاریخ گزارش: امروز، دیروز، یا ۱۴۰۴/۰۷/۰۵ (پیش‌فرض: امروز)" },
            start = new { type = "string", description = "ساعت شروع مثل 08:00 (پیش‌فرض 08:00)" },
            end = new { type = "string", description = "ساعت پایان مثل 17:00 (پیش‌فرض 17:00)" },
        },
        required = new[] { "project", "description" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var actions = ctx.Services.GetRequiredService<AiActionService>();
        var db = ctx.Services.GetRequiredService<AppDbContext>();
        var projArg = (AiToolArgs.GetString(args, "project") ?? "").Trim();
        var desc = (AiToolArgs.GetString(args, "description") ?? "").Trim();
        if (projArg == "" || desc == "")
            return JsonSerializer.Serialize(new { error = "bad_input", message = "پروژه و شرح کار هر دو لازم است." });

        var query = db.ProjectEntryExits.AsNoTracking().Where(x => !x.IsDelete);
        List<ProjectListRow> matches;
        if (int.TryParse(AiTextUtil.ToEnDigits(projArg), out var projId))
        {
            matches = await query.Where(x => x.Id == projId)
                .Select(x => new ProjectListRow(x.Id, x.ProjectName, x.CodeProject)).ToListAsync(ctx.CancellationToken);
        }
        else
        {
            var norm = AiTextUtil.NormalizeFa(projArg);
            var all = await query.Select(x => new ProjectListRow(x.Id, x.ProjectName, x.CodeProject))
                .ToListAsync(ctx.CancellationToken);
            matches = all.Where(x => AiTextUtil.NormalizeFa(x.Name + " " + x.Code).Contains(norm)).Take(6).ToList();
        }
        if (matches.Count == 0)
            return JsonSerializer.Serialize(new { error = "no_project", message = $"پروژه‌ای با نام «{projArg}» پیدا نکردم؛ نام دقیق‌تر یا شناسه را بگو." });
        if (matches.Count > 1)
            return JsonSerializer.Serialize(new
            {
                error = "ambiguous_project",
                message = "چند پروژه شبیه هم پیدا شد؛ کدام؟",
                candidates = matches.Select(m => new { شناسه = m.Id, نام = m.Name, کد = m.Code }),
            });
        var project = matches[0];

        var date = AiLeaveHelper.ParseFaDate(AiToolArgs.GetString(args, "date"), DateTime.Today) ?? DateTime.Today;
        var startRaw = AiTextUtil.ToEnDigits(AiToolArgs.GetString(args, "start") ?? "08:00");
        var endRaw = AiTextUtil.ToEnDigits(AiToolArgs.GetString(args, "end") ?? "17:00");
        if (!TimeOnly.TryParse(startRaw, out var start) || !TimeOnly.TryParse(endRaw, out var end))
            return JsonSerializer.Serialize(new { error = "bad_time", message = "ساعت شروع/پایان معتبر نیست؛ مثل 08:00 و 17:00." });
        if (start == end)
            return JsonSerializer.Serialize(new { error = "bad_time", message = "ساعت شروع و پایان نمی‌توانند یکسان باشند." });

        var summary = $"گزارش‌کار پروژه «{project.Name}» برای {AiDateUtil.ToFaShort(date)} ({start:HH:mm} تا {end:HH:mm})";
        var pending = await actions.CreateAsync(ctx.UserId, "report_work",
            JsonSerializer.Serialize(new
            {
                project_id = project.Id,
                description = desc,
                date = date.ToString("yyyy-MM-dd"),
                start = start.ToString("HH:mm"),
                end = end.ToString("HH:mm"),
            }), summary, ctx.CancellationToken);
        return JsonSerializer.Serialize(new { action_id = pending.Id, summary, hint = "برای اجرا، کاربر باید بنویسد: تأیید" });
    }

    private sealed record ProjectListRow(int Id, string Name, string Code);
}

public class CreateLetterDraftTool : IAiTool
{
    public string Name => "create_letter_draft";
    public string Description => "ثبت پیش‌فاکتور پیش‌نویس نامه داخلی جدید (اجرا فقط بعد از تأیید کاربر). پیش‌نویس در کارتابل ذخیره می‌شود تا بعداً گیرنده بگیرد و ارسال شود.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            title = new { type = "string", description = "عنوان نامه (اجباری)" },
            text = new { type = "string", description = "متن نامه (اختیاری؛ می‌تواند خالی بماند تا بعداً نوشته شود)" },
        },
        required = new[] { "title" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var actions = ctx.Services.GetRequiredService<AiActionService>();
        var title = (AiToolArgs.GetString(args, "title") ?? "").Trim();
        if (title == "")
            return JsonSerializer.Serialize(new { error = "bad_input", message = "عنوان نامه لازم است." });
        var text = AiToolArgs.GetString(args, "text") ?? "";
        var summary = $"پیش‌نویس نامه «{title}»";
        var pending = await actions.CreateAsync(ctx.UserId, "create_letter_draft",
            JsonSerializer.Serialize(new { title, text }), summary, ctx.CancellationToken);
        return JsonSerializer.Serialize(new { action_id = pending.Id, summary, hint = "برای اجرا، کاربر باید بنویسد: تأیید" });
    }
}

// ---------------- هوش مدیریتی (فقط خواندنی) ----------------

/// <summary>کنترل دسترسی گزارش‌های مدیریتی — هر گزارش مجوز Read همان ماژول سامانه را می‌خواهد.</summary>
internal static class AiBiAccess
{
    public static async Task<string?> DeniedJsonAsync(AiToolContext ctx, string module, string reportName)
    {
        var db = ctx.Services.GetRequiredService<AppDbContext>();
        var role = await db.Users.AsNoTracking()
            .Where(u => u.Id == ctx.UserId).Select(u => u.Role).FirstOrDefaultAsync(ctx.CancellationToken);
        if (await AiAccessHelper.UserHasAsync(db, ctx.UserId, module, "Read", role, ctx.CancellationToken))
            return null;
        return JsonSerializer.Serialize(new
        {
            error = "access_denied",
            message = $"به گزارش «{reportName}» دسترسی نداری؛ اگر لازمش داری از مدیر سیستم بخواه. (مجوز: {module})",
        });
    }
}

/// <summary>تبدیل دوره فارسی به بازه میلادی (امروز/این هفته/این ماه/ماه گذشته/امسال یا بازه دلخواه).</summary>
internal static class AiBiPeriod
{
    public static (DateTime from, DateTime to, string label) Resolve(string? period, string? fromRaw, string? toRaw)
    {
        var today = DateTime.Today;
        var f = AiLeaveHelper.ParseFaDate(fromRaw, today);
        var t = AiLeaveHelper.ParseFaDate(toRaw, today);
        if (f != null || t != null)
        {
            var from = (f ?? t)!.Value.Date;
            var to = (t ?? f)!.Value.Date;
            if (to < from) (from, to) = (to, from);
            return (from, to, $"{AiDateUtil.ToFaShort(from)} تا {AiDateUtil.ToFaShort(to)}");
        }
        var p = AiTextUtil.NormalizeFa(period);
        var pc = new System.Globalization.PersianCalendar();
        if (p.Contains("امسال") || p == "year")
        {
            var from = pc.ToDateTime(pc.GetYear(today), 1, 1, 0, 0, 0, 0);
            return (from, today, "امسال");
        }
        if (p.Contains("ماه گذشته") || p.Contains("ماه قبل") || p == "last_month")
        {
            var y = pc.GetYear(today); var m = pc.GetMonth(today);
            var pm = m == 1 ? 12 : m - 1; var py = m == 1 ? y - 1 : y;
            var from = pc.ToDateTime(py, pm, 1, 0, 0, 0, 0);
            var to = pc.ToDateTime(py, pm, pc.GetDaysInMonth(py, pm), 0, 0, 0, 0);
            return (from, to, "ماه گذشته");
        }
        if (p.Contains("ماه") || p == "month")
        {
            var from = pc.ToDateTime(pc.GetYear(today), pc.GetMonth(today), 1, 0, 0, 0, 0);
            return (from, today, "این ماه");
        }
        if (p.Contains("هفته") || p == "week")
        {
            var daysSinceSaturday = (((int)today.DayOfWeek) + 1) % 7; // هفته از شنبه شروع می‌شود
            return (today.AddDays(-daysSinceSaturday), today, "این هفته");
        }
        return (today, today, "امروز");
    }
}

public class SalesSummaryTool : IAiTool
{
    public string Name => "sales_summary";
    public string Description => "خلاصه فروش و خرید یک دوره: جمع و تعداد فروش/خرید، برگشتی، سود ناخالص تقریبی، دریافتنی/پرداختنی، پرفروش‌ترین کالاها و بزرگ‌ترین طرف‌ها. دوره را می‌توانی فارسی بدهی: امروز، این هفته، این ماه، ماه گذشته، امسال.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            period = new { type = "string", description = "دوره: امروز، این هفته، این ماه، ماه گذشته، امسال (پیش‌فرض: این ماه)" },
            from_date = new { type = "string", description = "شروع بازه دلخواه (مثل ۱۴۰۴/۰۷/۰۱) — جایگزین period" },
            to_date = new { type = "string", description = "پایان بازه دلخواه" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var denied = await AiBiAccess.DeniedJsonAsync(ctx, "FacInvoices", "فروش و خرید");
        if (denied != null) return denied;
        var period = AiToolArgs.GetString(args, "period");
        if (string.IsNullOrWhiteSpace(period) && AiToolArgs.GetString(args, "from_date") == null)
            period = "این ماه";
        var (from, to, label) = AiBiPeriod.Resolve(period,
            AiToolArgs.GetString(args, "from_date"), AiToolArgs.GetString(args, "to_date"));
        var inv = ctx.Services.GetRequiredService<IInvoicingService>();
        var d = await inv.GetDashboardAsync(from, to);
        return JsonSerializer.Serialize(new
        {
            دوره = label,
            فروش_جمع = d.SaleTotal,
            فروش_تعداد = d.SaleCount,
            خرید_جمع = d.PurchaseTotal,
            برگشتی_فروش = d.SaleReturnTotal,
            سود_ناخالص_تقریبی = d.GrossProfit,
            دریافتنی = d.Receivable,
            پرداختنی = d.Payable,
            پیش_نویس_باز = d.DraftCount,
            پرفروش_ترین_کالاها = d.TopProducts.Select(r => new { عنوان = r.Title, تعداد = r.Count, مبلغ = r.Net }),
            بزرگ_ترین_طرف_ها = d.TopParties.Select(r => new { عنوان = r.Title, تعداد = r.Count, مبلغ = r.Net }),
        });
    }
}

public class RecentInvoicesTool : IAiTool
{
    public string Name => "recent_invoices";
    public string Description => "آخرین فاکتورهای قطعی‌شده (فروش یا خرید): شماره، طرف حساب، تاریخ، مبلغ، نقد/نسیه و سررسید.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            kind = new { type = "string", description = "فروش، خرید یا همه (پیش‌فرض: فروش)" },
            limit = new { type = "integer", description = "تعداد (پیش‌فرض ۵، حداکثر ۱۵)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var denied = await AiBiAccess.DeniedJsonAsync(ctx, "FacInvoices", "فاکتورها");
        if (denied != null) return denied;
        var k = AiTextUtil.NormalizeFa(AiToolArgs.GetString(args, "kind"));
        InvoiceKind? kind = k.Contains("خرید") ? InvoiceKind.Purchase : k.Contains("همه") ? null : InvoiceKind.Sale;
        var limit = Math.Clamp(AiToolArgs.GetInt(args, "limit") ?? 5, 1, 15);
        var inv = ctx.Services.GetRequiredService<IInvoicingService>();
        var page = await inv.GetInvoicesAsync(kind, InvoiceStatus.Confirmed, null, null, null, null, null, 1, limit);
        return JsonSerializer.Serialize(page.Items.Select(i => new
        {
            شماره = i.Number,
            نوع = i.Kind == InvoiceKind.Sale ? "فروش" : i.Kind == InvoiceKind.Purchase ? "خرید" : "برگشتی",
            طرف = i.PartyName,
            تاریخ = AiDateUtil.ToFaShort(i.Date),
            مبلغ = i.TotalNet,
            تسویه = i.Settlement == SettlementType.Credit ? "نسیه" : "نقد",
            سررسید = i.DueDate == null ? null : AiDateUtil.ToFaShort(i.DueDate.Value),
        }));
    }
}

public class StockStatusTool : IAiTool
{
    public string Name => "stock_status";
    public string Description => "موجودی انبار. با search (نام یا کد کالا) = موجودی آن کالاها در همه انبارها؛ بدون search = فقط کالاهای زیر نقطه سفارش (کمبودها).";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            search = new { type = "string", description = "نام یا کد کالا (اختیاری؛ خالی = فقط کمبودها)" },
            limit = new { type = "integer", description = "تعداد (پیش‌فرض ۱۰، حداکثر ۲۰)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var denied = await AiBiAccess.DeniedJsonAsync(ctx, "Products", "موجودی انبار");
        if (denied != null) return denied;
        var search = (AiToolArgs.GetString(args, "search") ?? "").Trim();
        var limit = Math.Clamp(AiToolArgs.GetInt(args, "limit") ?? 10, 1, 20);
        var wh = ctx.Services.GetRequiredService<IWarehousingService>();
        var belowOnly = search == "";
        var page = await wh.GetStockAsync(null, null, belowOnly ? null : search, belowOnly, 1, limit);
        return JsonSerializer.Serialize(page.Items.Select(r => new
        {
            کد = r.ProductCode,
            کالا = r.ProductName,
            انبار = r.WarehouseName,
            موجودی = r.Quantity,
            واحد = r.Unit,
            نقطه_سفارش = r.ReorderPoint,
            وضعیت = r.BelowReorder ? "کمبود" : "موجود",
        }));
    }
}

public class ChequesDueTool : IAiTool
{
    public string Name => "cheques_due";
    public string Description => "چک‌های باز نزدیک سررسید (دریافتی و صادره، شامل معوق‌ها): شماره، نوع، طرف، مبلغ، بانک، سررسید + جمع دریافتی/صادره.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            days = new { type = "integer", description = "تا چند روز آینده (پیش‌فرض ۷، حداکثر ۹۰)" },
            kind = new { type = "string", description = "دریافتی، صادره یا همه (پیش‌فرض: همه)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var denied = await AiBiAccess.DeniedJsonAsync(ctx, "TrsCheques", "چک‌ها");
        if (denied != null) return denied;
        var k = AiTextUtil.NormalizeFa(AiToolArgs.GetString(args, "kind"));
        ChequeKind? kind = k.Contains("دریافت") ? ChequeKind.Received
            : k.Contains("صادر") || k.Contains("پرداخت") ? ChequeKind.Issued : null;
        var days = Math.Clamp(AiToolArgs.GetInt(args, "days") ?? 7, 0, 90);
        var today = DateTime.Today;
        var trs = ctx.Services.GetRequiredService<ITreasuryService>();
        var page = await trs.GetChequesAsync(kind, null, null, null, null,
            today.AddDays(-60), today.AddDays(days), true, 1, 50);
        var items = page.Items
            .Select(c => new
            {
                شماره = c.Number,
                نوع = c.Kind == ChequeKind.Received ? "دریافتی" : "صادره",
                طرف = c.PartyName ?? c.OwnerName,
                مبلغ = c.Amount,
                بانک = c.BankName,
                سررسید = AiDateUtil.ToFaShort(c.DueDate),
                وضعیت = c.DueDate.Date < today ? $"معوق ({(today - c.DueDate.Date).Days} روز)"
                    : c.DueDate.Date == today ? "امروز" : $"{(c.DueDate.Date - today).Days} روز مانده",
            })
            .Take(20).ToList();
        return JsonSerializer.Serialize(new
        {
            تعداد_کل = page.TotalCount,
            جمع_دریافتی = page.Items.Where(c => c.Kind == ChequeKind.Received).Sum(c => c.Amount),
            جمع_صادره = page.Items.Where(c => c.Kind == ChequeKind.Issued).Sum(c => c.Amount),
            چک_ها = items,
        });
    }
}

public class TopDebtorsTool : IAiTool
{
    public string Name => "top_debtors";
    public string Description => "بدهکاران بزرگ: طرف‌های با بیشترین جمع فاکتور نسیه (منهای برگشتی) — دقیقاً با همان تعریف «دریافتنی» خود سامانه.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            limit = new { type = "integer", description = "تعداد (پیش‌فرض ۱۰، حداکثر ۲۰)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var denied = await AiBiAccess.DeniedJsonAsync(ctx, "FacInvoices", "مطالبات");
        if (denied != null) return denied;
        var limit = Math.Clamp(AiToolArgs.GetInt(args, "limit") ?? 10, 1, 20);
        var svc = ctx.Services.GetRequiredService<AiReportService>();
        var rows = await svc.GetDebtorRowsAsync(limit, ctx.CancellationToken);
        return JsonSerializer.Serialize(rows.Select(r => new
        {
            طرف = r.Name,
            جمع_نسیه = r.Total,
            تعداد_فاکتور = r.Count,
        }));
    }
}

public class CashStatusTool : IAiTool
{
    public string Name => "cash_status";
    public string Description => "وضعیت نقدینگی: جمع موجودی صندوق‌ها و بانک‌ها، مانده هر حساب، و گردش دوره (جمع دریافت/پرداخت).";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            period = new { type = "string", description = "دوره گردش: امروز، این هفته، این ماه، امسال (پیش‌فرض: این ماه)" },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var denied = await AiBiAccess.DeniedJsonAsync(ctx, "TrsAccounts", "صندوق و بانک");
        if (denied != null) return denied;
        var period = AiToolArgs.GetString(args, "period");
        if (string.IsNullOrWhiteSpace(period)) period = "این ماه";
        var (from, to, label) = AiBiPeriod.Resolve(period, null, null);
        var trs = ctx.Services.GetRequiredService<ITreasuryService>();
        var d = await trs.GetDashboardAsync(from, to);
        return JsonSerializer.Serialize(new
        {
            دوره = label,
            جمع_کل = d.TotalBalance,
            صندوق = d.TotalCashBalance,
            بانک = d.TotalBankBalance,
            حساب_ها = d.Accounts.Select(a => new
            {
                نام = a.Name,
                نوع = a.Kind switch
                {
                    TreasuryAccountKind.Cash => "صندوق",
                    TreasuryAccountKind.Bank => "بانک",
                    TreasuryAccountKind.Pos => "کارتخوان",
                    _ => "تنخواه",
                },
                مانده = a.Balance,
                بانک = a.BankName,
            }),
            دریافت_دوره = d.PeriodIn,
            پرداخت_دوره = d.PeriodOut,
            تعداد_دریافت = d.ReceiptCount,
            تعداد_پرداخت = d.PaymentCount,
        });
    }
}

public class BuildReportTool : IAiTool
{
    public string Name => "build_report";
    public string Description => "ساخت گزارش تحلیلی با خروجی اکسل. وقتی کاربر «گزارش» خواست، گفت «به تفکیک ...»، یا جدول کامل/فایل اکسل لازم داشت. datasets: فروش (sales)، موجودی (stock)، چک‌ها (cheques)، بدهکاران (debtors). خروجی: report_id + پیش‌نمایش ۱۵ سطر اول. شناسه گزارش را به کاربر نشان نده؛ فقط جدول را خلاصه کن و بگو فایل اکسل کامل با دکمه زیر پیام قابل دانلود است.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            dataset = new { type = "string", description = "نوع گزارش: فروش، موجودی، چک، بدهکاران (اجباری)" },
            period = new { type = "string", description = "دوره (فروش/چک): امروز، این هفته، این ماه، ماه گذشته، امسال (پیش‌فرض: این ماه)" },
            from_date = new { type = "string", description = "شروع بازه دلخواه" },
            to_date = new { type = "string", description = "پایان بازه دلخواه" },
            group_by = new { type = "string", description = "تفکیک گزارش فروش: طرف/مشتری، کالا، ماه، روز (پیش‌فرض: طرف)" },
            kind = new { type = "string", description = "فروش: فروش/خرید — چک: دریافتی/صادره (پیش‌فرض: فروش / همه)" },
            search = new { type = "string", description = "جستجوی کالا (موجودی؛ خالی = فقط کمبودها)" },
            days = new { type = "integer", description = "چک‌ها تا چند روز آینده (پیش‌فرض ۷)" },
        },
        required = new[] { "dataset" },
    };

    public async Task<string> ExecuteAsync(JsonElement args, AiToolContext ctx)
    {
        var ds = AiTextUtil.NormalizeFa(AiToolArgs.GetString(args, "dataset"));
        var svc = ctx.Services.GetRequiredService<AiReportService>();
        AiReportPreview preview;
        if (ds.Contains("موجودی") || ds.Contains("انبار") || ds.Contains("کالا") || ds == "stock")
        {
            var denied = await AiBiAccess.DeniedJsonAsync(ctx, "Products", "موجودی انبار");
            if (denied != null) return denied;
            preview = await svc.BuildStockAsync(ctx.UserId,
                AiToolArgs.GetString(args, "search"), ctx.CancellationToken);
        }
        else if (ds.Contains("چک") || ds == "cheques")
        {
            var denied = await AiBiAccess.DeniedJsonAsync(ctx, "TrsCheques", "چک‌ها");
            if (denied != null) return denied;
            var k = AiTextUtil.NormalizeFa(AiToolArgs.GetString(args, "kind"));
            ChequeKind? kind = k.Contains("دریافت") ? ChequeKind.Received
                : k.Contains("صادر") || k.Contains("پرداخت") ? ChequeKind.Issued : null;
            var days = Math.Clamp(AiToolArgs.GetInt(args, "days") ?? 7, 0, 90);
            preview = await svc.BuildChequesAsync(ctx.UserId, kind, days, ctx.CancellationToken);
        }
        else if (ds.Contains("بدهکار") || ds.Contains("مطالبات") || ds == "debtors")
        {
            var denied = await AiBiAccess.DeniedJsonAsync(ctx, "FacInvoices", "مطالبات");
            if (denied != null) return denied;
            preview = await svc.BuildDebtorsAsync(ctx.UserId, ctx.CancellationToken);
        }
        else if (ds.Contains("فروش") || ds.Contains("خرید") || ds.Contains("فاکتور")
            || ds is "sales" or "fac")
        {
            var denied = await AiBiAccess.DeniedJsonAsync(ctx, "FacInvoices", "فروش و خرید");
            if (denied != null) return denied;
            var period = AiToolArgs.GetString(args, "period");
            if (string.IsNullOrWhiteSpace(period) && AiToolArgs.GetString(args, "from_date") == null)
                period = "این ماه";
            var (from, to, label) = AiBiPeriod.Resolve(period,
                AiToolArgs.GetString(args, "from_date"), AiToolArgs.GetString(args, "to_date"));
            var kind = AiTextUtil.NormalizeFa(AiToolArgs.GetString(args, "kind")).Contains("خرید")
                ? InvoiceKind.Purchase : InvoiceKind.Sale;
            preview = await svc.BuildSalesAsync(ctx.UserId, kind,
                AiToolArgs.GetString(args, "group_by") ?? "طرف", from, to, label, ctx.CancellationToken);
        }
        else
        {
            return JsonSerializer.Serialize(new
            {
                error = "bad_dataset",
                message = "نوع گزارش مشخص نیست؛ یکی از: فروش، موجودی، چک، بدهکاران.",
            });
        }

        return JsonSerializer.Serialize(new
        {
            report_id = preview.ReportId,
            title = preview.Title,
            columns = preview.Columns,
            rows = preview.Rows,
            total_row = preview.TotalRow,
            total_rows = preview.TotalRows,
            hint = $"این جدول فقط {preview.Rows.Count} سطر اول از {preview.TotalRows} سطر است؛ فایل اکسل کامل با دکمه دانلود زیر پیام.",
        });
    }
}
