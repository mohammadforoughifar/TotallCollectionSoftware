using System.Text.Json;
using Inventory.Api.Data;
using Inventory.Api.Services.FaAtt;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Services.Ai;

// =====================================================================
// «اقدام با تأیید»: دستیار اقدام را به‌صورت پیش‌فاکتور ثبت می‌کند و فقط
// بعد از تأیید صریح همان کاربر (مثلاً با گفتن «تأیید») اجرا می‌شود.
// پیش‌فاکتورها ۱۵ دقیقه اعتبار دارند و فقط سازنده می‌تواند تأییدشان کند.
// =====================================================================

public class AiPendingActionInfo
{
    public int Id { get; set; }
    public string Action { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
}

public class AiActionService
{
    private readonly AppDbContext _db;
    private readonly AiOptions _options;
    private readonly IFaAttService _faAtt;
    private readonly ILogger<AiActionService> _log;

    public AiActionService(AppDbContext db, IOptions<AiOptions> options, IFaAttService faAtt, ILogger<AiActionService> log)
    {
        _db = db;
        _options = options.Value;
        _faAtt = faAtt;
        _log = log;
    }

    /// <summary>ثبت پیش‌فاکتور جدید و برگرداندن آن (برای نمایش به کاربر).</summary>
    public async Task<AiPendingAction> CreateAsync(int userId, string action, string argsJson, string summary, CancellationToken ct)
    {
        await SweepExpiredAsync(userId, ct);
        var minutes = _options.PendingActionExpiryMinutes > 0 ? _options.PendingActionExpiryMinutes : 15;
        var entity = new AiPendingAction
        {
            UserId = userId,
            Action = action,
            ArgsJson = string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson,
            Summary = summary,
            Status = 0,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(minutes),
        };
        _db.AiPendingActions.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    /// <summary>پیش‌فاکتورهای باز کاربر (منقضی‌ها خودکار باطل می‌شوند).</summary>
    public async Task<List<AiPendingActionInfo>> ListPendingAsync(int userId, CancellationToken ct)
    {
        await SweepExpiredAsync(userId, ct);
        return await _db.AiPendingActions.AsNoTracking()
            .Where(a => a.UserId == userId && a.Status == 0)
            .OrderByDescending(a => a.Id)
            .Select(a => new AiPendingActionInfo
            {
                Id = a.Id,
                Action = a.Action,
                Summary = a.Summary,
                ExpiresAtUtc = a.ExpiresAtUtc,
            })
            .ToListAsync(ct);
    }

    /// <summary>تأیید و اجرای اقدام (بدون شناسه = تازه‌ترین پیش‌فاکتور باز).</summary>
    public async Task<(bool ok, string message)> ConfirmAsync(int userId, string userName, int? actionId, CancellationToken ct)
    {
        await SweepExpiredAsync(userId, ct);
        var action = await FindPendingAsync(userId, actionId, ct);
        if (action == null)
            return (false, "پیش‌فاکتور بازی نداری. اول بگو چه کاری انجام بدهم (مثلاً «فردا مرخصی می‌خوام»).");

        // کنترل دسترسی ماژول مربوطه — مثل خود سامانه
        const string module = "FaAtt";
        var role = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.Role).FirstOrDefaultAsync(ct);
        if (!await AiAccessHelper.UserHasAsync(_db, userId, module, "Create", role, ct))
            return (false, "به این اقدام دسترسی نداری. ⛔");

        try
        {
            var result = action.Action switch
            {
                "request_leave" => await ExecuteLeaveAsync(action, userId, userName, ct),
                "clock" => await ExecuteClockAsync(action, userId, userName, ct),
                _ => throw new InvalidOperationException("نوع اقدام ناشناخته است."),
            };
            action.Status = 1;
            action.DecidedAtUtc = DateTime.UtcNow;
            action.ResultText = result;
            await _db.SaveChangesAsync(ct);
            return (true, "✅ انجام شد! " + result);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "اجرای اقدام {ActionId} ناموفق بود.", action.Id);
            action.Status = 2;
            action.DecidedAtUtc = DateTime.UtcNow;
            action.ResultText = "خطا: " + ex.Message;
            await _db.SaveChangesAsync(ct);
            return (false, "❌ اجرا نشد: " + ex.Message);
        }
    }

    /// <summary>لغو پیش‌فاکتور (بدون شناسه = تازه‌ترین).</summary>
    public async Task<(bool ok, string message)> CancelAsync(int userId, int? actionId, CancellationToken ct)
    {
        await SweepExpiredAsync(userId, ct);
        var action = await FindPendingAsync(userId, actionId, ct);
        if (action == null)
            return (false, "پیش‌فاکتور بازی نداری.");
        action.Status = 2;
        action.DecidedAtUtc = DateTime.UtcNow;
        action.ResultText = "لغو توسط کاربر";
        await _db.SaveChangesAsync(ct);
        return (true, $"پیش‌فاکتور «{action.Summary}» لغو شد. 🗑️");
    }

    // ---------------- اجرا ----------------

    private async Task<string> ExecuteLeaveAsync(AiPendingAction action, int userId, string userName, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(action.ArgsJson) ? "{}" : action.ArgsJson);
        var root = doc.RootElement;
        var typeId = root.TryGetProperty("leaveTypeId", out var t) && t.TryGetInt32(out var n) ? n : 0;
        var from = root.TryGetProperty("from", out var f) ? f.GetString() : null;
        var to = root.TryGetProperty("to", out var e) ? e.GetString() : null;
        var reason = root.TryGetProperty("reason", out var r) ? r.GetString() : null;
        if (typeId <= 0 || !DateTime.TryParse(from, out var fromDate) || !DateTime.TryParse(to, out var toDate))
            throw new InvalidOperationException("اطلاعات مرخصی ناقص است؛ دوباره بگو.");
        if (toDate < fromDate) (fromDate, toDate) = (toDate, fromDate);

        var empId = await _db.HrEmployees.AsNoTracking()
            .Where(x => x.SystemUserId == userId).Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (empId == 0) throw new InvalidOperationException("پرونده پرسنلی برایت یافت نشد.");

        var saved = await _faAtt.RequestMyLeaveAsync(userId, userName, new FaAttLeaveSaveDto
        {
            EmployeeId = empId,
            LeaveTypeId = typeId,
            FromDate = fromDate.Date,
            ToDate = toDate.Date,
            Reason = string.IsNullOrWhiteSpace(reason) ? "ثبت با دستیار فروغ آریا" : reason.Trim(),
        });
        return $"مرخصی {saved.LeaveTypeName ?? ""} از {AiDateUtil.ToFaShort(saved.FromDate)} تا {AiDateUtil.ToFaShort(saved.ToDate)} ثبت شد و در انتظار تأیید است.";
    }

    private async Task<string> ExecuteClockAsync(AiPendingAction action, int userId, string userName, CancellationToken ct)
    {
        _ = ct;
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(action.ArgsJson) ? "{}" : action.ArgsJson);
        var type = doc.RootElement.TryGetProperty("type", out var t) && t.TryGetInt32(out var n) ? n : -1;
        if (type is not (0 or 1)) throw new InvalidOperationException("نوع ساعت‌زنی مشخص نیست.");
        var log = await _faAtt.ClockAsync(userId, userName, new FaAttClockSaveDto { Type = type });
        var when = log.Timestamp.ToString("HH:mm");
        return type == 0 ? $"ساعت ورود ({when}) ثبت شد. 👋" : $"ساعت خروج ({when}) ثبت شد. 👋";
    }

    // ---------------- کمکی ----------------

    private async Task<AiPendingAction?> FindPendingAsync(int userId, int? actionId, CancellationToken ct)
    {
        var q = _db.AiPendingActions.Where(a => a.UserId == userId && a.Status == 0);
        if (actionId is > 0) return await q.FirstOrDefaultAsync(a => a.Id == actionId, ct);
        return await q.OrderByDescending(a => a.Id).FirstOrDefaultAsync(ct);
    }

    private async Task SweepExpiredAsync(int userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await _db.AiPendingActions
            .Where(a => a.UserId == userId && a.Status == 0 && a.ExpiresAtUtc <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, 2)
                .SetProperty(a => a.DecidedAtUtc, now)
                .SetProperty(a => a.ResultText, "منقضی شد"), ct);
    }
}

// =====================================================================
// کمک‌متن مرخصی: تطبیق نام نوع مرخصی + فهم تاریخ‌های فارسی
// (امروز، فردا، پس‌فردا، نام روز هفته، ۱۴۰۴/۰۷/۰۵ یا 2026-09-27)
// =====================================================================

public static class AiLeaveHelper
{
    /// <summary>پیدا کردن نوع مرخصی از روی نام (تقریبی)؛ پیش‌فرض: استحقاقی.</summary>
    public static async Task<(int id, string name)?> MatchLeaveTypeAsync(IFaAttService fa, string? wanted)
    {
        var types = (await fa.ListLeaveTypesAsync()).Where(t => t.IsActive).ToList();
        if (types.Count == 0) return null;
        var w = AiTextUtil.NormalizeFa(wanted);
        if (w != "")
        {
            var hit = types.FirstOrDefault(t => AiTextUtil.NormalizeFa(t.Name) == w)
                ?? types.FirstOrDefault(t => AiTextUtil.NormalizeFa(t.Name).Contains(w))
                ?? types.FirstOrDefault(t => w.Contains(AiTextUtil.NormalizeFa(t.Name)));
            if (hit != null) return (hit.Id, hit.Name);
        }
        var def = types.FirstOrDefault(t => AiTextUtil.NormalizeFa(t.Name).Contains("استحقاقی")) ?? types[0];
        return (def.Id, def.Name);
    }

    /// <summary>فهم تاریخ فارسی/میلادی به تاریخ میلادی (ساعت ۰۰:۰۰). ناموفق = null.</summary>
    public static DateTime? ParseFaDate(string? text, DateTime today)
    {
        var t = AiTextUtil.NormalizeFa(text).Replace("پس فردا", "پس‌فردا");
        if (t == "") return null;
        today = today.Date;

        if (t is "امروز") return today;
        if (t is "فردا") return today.AddDays(1);
        if (t is "پس‌فردا" or "پس فردا") return today.AddDays(2);

        // نام روز هفته → نزدیک‌ترین آینده (اگر امروز همان روز است، امروز)
        var dow = t switch
        {
            "شنبه" => (DayOfWeek?)DayOfWeek.Saturday,
            "یکشنبه" or "یک شنبه" => DayOfWeek.Sunday,
            "دوشنبه" or "دو شنبه" => DayOfWeek.Monday,
            "سه‌شنبه" or "سه شنبه" => DayOfWeek.Tuesday,
            "چهارشنبه" or "چهار شنبه" => DayOfWeek.Wednesday,
            "پنجشنبه" or "پنج شنبه" => DayOfWeek.Thursday,
            "جمعه" => DayOfWeek.Friday,
            _ => null,
        };
        if (dow != null)
        {
            var delta = ((int)dow.Value - (int)today.DayOfWeek + 7) % 7;
            return today.AddDays(delta);
        }

        // عددی: 1404/07/05 یا 2026-09-27 (ارقام فارسی هم قبول)
        var digits = ToEnDigits(t);
        var m = System.Text.RegularExpressions.Regex.Match(digits, @"(\d{4})\s*[/\-]\s*(\d{1,2})\s*[/\-]\s*(\d{1,2})");
        if (!m.Success) return null;
        var y = int.Parse(m.Groups[1].Value);
        var mo = int.Parse(m.Groups[2].Value);
        var d = int.Parse(m.Groups[3].Value);
        try
        {
            if (y is >= 1300 and <= 1500)
                return new System.Globalization.PersianCalendar().ToDateTime(y, mo, d, 0, 0, 0, 0);
            return new DateTime(y, mo, d);
        }
        catch { return null; }
    }

    private static string ToEnDigits(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(c switch
            {
                >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
                _ => c,
            });
        return sb.ToString();
    }
}
