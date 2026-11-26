using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.SalesVisit;

/// <summary>
/// پیوست‌های سفارش ویزیتور — عکس چک و مدارک دیگر.
/// <para>
/// فایل روی دیسک سرور در <c>wwwroot/uploads/SalesVisit/{orderId}/</c> ذخیره می‌شود و
/// رکورد در جدول مشترک <see cref="AppAttachment"/> با <c>Module = "SalesVisit"</c> و
/// <c>RefId = orderId</c> ثبت می‌گردد. تصمیم مالی (تأیید/رد) در همان رکورد نگه‌داری
/// می‌شود تا کارتابل مالی بتواند وضعیت هر مدرک را جدا نشان دهد.
/// </para>
/// <para>
/// پیش از ذخیرهٔ سفارش، فرم با <c>RefId = VisAttachmentCategories.DraftRefId</c> آپلود
/// می‌کند؛ هنگام ذخیره، همان رکوردها به شناسهٔ واقعی سفارش منتقل می‌شوند.
/// </para>
/// </summary>
public interface IVisAttachmentService
{
    /// <summary>فهرست پیوست‌های یک سفارش (با تصمیم مالی هر مدرک).</summary>
    Task<List<VisOrderAttachmentDto>> ListAsync(int orderId);

    /// <summary>
    /// فهرست پیوست‌های چند سفارش — برای کارتابل مالی (یک کوئری، بدون N+1).
    /// </summary>
    Task<Dictionary<int, List<VisOrderAttachmentDto>>> ListForOrdersAsync(IReadOnlyCollection<int> orderIds);

    /// <summary>آپلود عکس چک/مدرک — پیش‌نویس (orderId ≤ 0) یا سفارش موجود.</summary>
    Task<VisOrderAttachmentDto> UploadAsync(int orderId, int userId, string? userName, IFormFile file,
        string? category, CancellationToken ct = default);

    /// <summary>حذف مدرک — پیش از تصمیم مالی (برای اصلاح اشتباه ویزیتور).</summary>
    Task DeleteAsync(int attachmentId, int orderId, bool allowAfterDecision);

    /// <summary>ثبت تصمیم مالی روی یک مدرک.</summary>
    Task<VisOrderAttachmentDto?> SetDecisionAsync(AppAttachment attachment, bool approved, string? note,
        string? userName, DateTime at);

    /// <summary>
    /// انتقال پیوست‌های پیش‌نویس به شناسهٔ واقعی سفارش (پس از ذخیره).
    /// شناسه‌ها از فهرست ورودی کلاینت امن‌سازی می‌شوند: فقط رکوردهای پیش‌نویسِ همان کاربر.
    /// </summary>
    Task<int> MoveDraftAttachmentsAsync(int orderId, int userId, int? companyId,
        IReadOnlyCollection<int>? attachmentIds, IReadOnlyCollection<int>? chequeAttachmentIds);

    /// <summary>جابه‌جایی/هم‌گام‌سازی دستهٔ چک برای فایل‌های آپلودشده در همان فرم.</summary>
    Task<int> MarkChequeAttachmentsAsync(int orderId, IReadOnlyCollection<int> attachmentIds);

    /// <summary>
    /// پیوست‌های معتبر این سفارش که باید بررسی مالی شوند (عکس چک‌ها، در صورت وجود).
    /// </summary>
    Task<List<AppAttachment>> PendingChequeAttachmentsAsync(int orderId);

    string Module { get; }
}

public class VisAttachmentService : IVisAttachmentService
{
    public const string ModuleName = "SalesVisit";

    private readonly AppDbContext _db;
    private readonly FileStore _store;

    public VisAttachmentService(AppDbContext db, FileStore store)
    {
        _db = db;
        _store = store;
    }

    public string Module => ModuleName;

    public async Task<List<VisOrderAttachmentDto>> ListAsync(int orderId)
    {
        if (orderId == 0) return new List<VisOrderAttachmentDto>();
        var map = await ListForOrdersAsync(new[] { orderId });
        return map.TryGetValue(orderId, out var list) ? list : new List<VisOrderAttachmentDto>();
    }

    public async Task<Dictionary<int, List<VisOrderAttachmentDto>>> ListForOrdersAsync(
        IReadOnlyCollection<int> orderIds)
    {
        var result = new Dictionary<int, List<VisOrderAttachmentDto>>();
        var ids = orderIds.Where(id => id != 0).Distinct().ToList();
        if (ids.Count == 0) return result;

        var rows = await _db.AppAttachments.AsNoTracking()
            .Where(a => a.Module == ModuleName && ids.Contains(a.RefId))
            .OrderBy(a => a.Id)
            .ToListAsync();

        foreach (var group in rows.GroupBy(a => a.RefId))
            result[group.Key] = group.Select(ToDto).ToList();
        return result;
    }

    public async Task<VisOrderAttachmentDto> UploadAsync(int orderId, int userId, string? userName, IFormFile file,
        string? category, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            throw new InvalidOperationException("فایلی انتخاب نشده است.");

        // فقط تصویر یا PDF — عکس چک/مدرک اسکن‌شده؛ هر نوع فایل اجرایی پذیرفته نمی‌شود.
        var name = Path.GetFileName(file.FileName ?? "");
        var ext = Path.GetExtension(name).ToLowerInvariant();
        var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tif", ".tiff", ".heic", ".heif", ".pdf" };
        if (!allowed.Contains(ext))
            throw new InvalidOperationException("فقط تصویر (JPG/PNG/WebP) یا PDF برای عکس چک و پیوست سفارش پذیرفته می‌شود.");
        if (file.Length > 25L * 1024 * 1024)
            throw new InvalidOperationException("حداکثر حجم هر پیوست ۲۵ مگابایت است؛ عکس را فشرده یا PDF کنید.");

        string relPath;
        await using (var stream = file.OpenReadStream())
        {
            relPath = await _store.SaveAsync(ModuleName, orderId, stream, name, subFolder: null);
        }

        var cleanCategory = string.Equals(category, VisAttachmentCategories.Cheque, StringComparison.OrdinalIgnoreCase)
            ? VisAttachmentCategories.Cheque
            : VisAttachmentCategories.General;

        var att = new AppAttachment
        {
            Module = ModuleName,
            RefId = orderId,
            FileName = string.IsNullOrWhiteSpace(name) ? "file" : name,
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType!,
            FilePath = relPath,
            Data = Array.Empty<byte>(),
            UploaderName = userName ?? "",
            UploaderUserId = userId,
            Category = cleanCategory,
            FinancialStatus = VisAttachmentFinancialStatus.Pending
        };
        _db.AppAttachments.Add(att);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            // رکورد ثبت نشد → فایل بی‌صاحب روی دیسک نماند
            _store.Delete(relPath);
            throw;
        }
        return ToDto(att);
    }

    public async Task DeleteAsync(int attachmentId, int orderId, bool allowAfterDecision)
    {
        var att = await _db.AppAttachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.Module == ModuleName);
        if (att is null) return;
        if (orderId != 0 && att.RefId != orderId && att.RefId != VisAttachmentCategories.DraftRefId)
            throw new InvalidOperationException("این پیوست متعلق به این سفارش نیست.");
        if (!allowAfterDecision && att.FinancialStatus != VisAttachmentFinancialStatus.Pending)
            throw new InvalidOperationException("این مدرک بررسی مالی شده است و حذف نمی‌شود؛ برای اصلاح، سفارش را ویرایش و مدرک تازه بارگذاری کنید.");

        _db.AppAttachments.Remove(att);
        await _db.SaveChangesAsync();
        _store.Delete(att.FilePath);
    }

    public Task<VisOrderAttachmentDto?> SetDecisionAsync(AppAttachment attachment, bool approved, string? note,
        string? userName, DateTime at)
    {
        attachment.FinancialStatus = approved ? VisAttachmentFinancialStatus.Approved : VisAttachmentFinancialStatus.Rejected;
        attachment.FinancialDecidedAt = at;
        attachment.FinancialDecidedBy = userName;
        attachment.FinancialNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        return Task.FromResult<VisOrderAttachmentDto?>(ToDto(attachment));
    }

    public async Task<int> MoveDraftAttachmentsAsync(int orderId, int userId, int? companyId,
        IReadOnlyCollection<int>? attachmentIds, IReadOnlyCollection<int>? chequeAttachmentIds)
    {
        if (orderId <= 0) return 0;

        var requested = (attachmentIds ?? Array.Empty<int>())
            .Where(id => id > 0)
            .Distinct()
            .ToList();
        var chequeIds = (chequeAttachmentIds ?? Array.Empty<int>())
            .Where(id => id > 0)
            .Distinct()
            .ToHashSet();

        // پیوست‌های پیش‌نویس همین کاربر (حتی اگر فهرست شناسه‌ها ناقص بماند، همه منتقل می‌شوند).
        // مالکیت پیش‌نویس فقط با UploaderUserId سنجیده می‌شود؛ ستون جداگانه‌ای برای
        // «شناسهٔ کاربر بارگذار» وجود ندارد و UploaderUserId همان است.
        var drafts = await _db.AppAttachments
            .Where(a => a.Module == ModuleName && a.RefId == VisAttachmentCategories.DraftRefId &&
                        a.UploaderUserId == userId)
            .ToListAsync();

        var targets = drafts
            .Where(d => requested.Count == 0 || requested.Contains(d.Id))
            .ToList();
        foreach (var d in targets)
        {
            d.RefId = orderId;
            d.CompanyId = companyId;
            d.FinancialStatus = VisAttachmentFinancialStatus.Pending;
            d.FinancialDecidedAt = null;
            d.FinancialDecidedBy = null;
            d.FinancialNote = null;
            if (chequeIds.Contains(d.Id)) d.Category = VisAttachmentCategories.Cheque;
        }

        // فایل‌هایی که قبلاً مستقیماً با شناسهٔ سفارش آپلود شده‌اند و در فهرست «عکس چک» آمده‌اند
        if (chequeIds.Count > 0)
        {
            var existing = await _db.AppAttachments
                .Where(a => a.Module == ModuleName && a.RefId == orderId && chequeIds.Contains(a.Id))
                .ToListAsync();
            foreach (var e in existing) e.Category = VisAttachmentCategories.Cheque;
        }

        if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync();
        return targets.Count;
    }

    public async Task<int> MarkChequeAttachmentsAsync(int orderId, IReadOnlyCollection<int> attachmentIds)
    {
        if (orderId <= 0 || attachmentIds.Count == 0) return 0;
        var ids = attachmentIds.Where(id => id > 0).Distinct().ToList();
        var rows = await _db.AppAttachments
            .Where(a => a.Module == ModuleName && a.RefId == orderId && ids.Contains(a.Id))
            .ToListAsync();
        foreach (var r in rows) r.Category = VisAttachmentCategories.Cheque;
        if (rows.Count > 0) await _db.SaveChangesAsync();
        return rows.Count;
    }

    public async Task<List<AppAttachment>> PendingChequeAttachmentsAsync(int orderId) =>
        await _db.AppAttachments.AsNoTracking()
            .Where(a => a.Module == ModuleName && a.RefId == orderId &&
                        a.Category == VisAttachmentCategories.Cheque)
            .OrderBy(a => a.Id)
            .ToListAsync();

    internal static VisOrderAttachmentDto ToDto(AppAttachment a) => new()
    {
        Id = a.Id,
        OrderId = a.RefId,
        FileName = a.FileName,
        ContentType = a.ContentType,
        Size = a.Size is > 0 ? a.Size.Value : (a.Data?.Length ?? 0),
        UploaderName = a.UploaderName,
        UploadedAt = a.UploadedAt,
        Category = string.IsNullOrWhiteSpace(a.Category) ? VisAttachmentCategories.General : a.Category,
        FinanceApproved = a.FinancialStatus switch
        {
            VisAttachmentFinancialStatus.Approved => true,
            VisAttachmentFinancialStatus.Rejected => false,
            _ => null
        },
        FinanceDecidedAt = a.FinancialDecidedAt,
        FinanceDecidedBy = a.FinancialDecidedBy,
        FinanceNote = a.FinancialNote
    };
}
