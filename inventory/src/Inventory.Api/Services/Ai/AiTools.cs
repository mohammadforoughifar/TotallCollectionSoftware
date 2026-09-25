using System.Text.Json;
using Inventory.Api.Data;
using Inventory.Api.Services.FaAtt;
using Inventory.Api.Services.FaPay;
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
            اقدام = a.Action == "request_leave" ? "درخواست مرخصی" : a.Action == "clock" ? "ساعت‌زنی" : a.Action,
            خلاصه = a.Summary,
        }));
    }
}
