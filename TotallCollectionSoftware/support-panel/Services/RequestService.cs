using System.Text.Json;
using LicenseSigner.Data;
using Microsoft.EntityFrameworkCore;

namespace LicenseSigner.Services;

/// <summary>نتیجهٔ ثبت درخواست.</summary>
public sealed record IngestResult(bool Ok, string? Error, int RequestId, string? RequestNo, bool Duplicate);

/// <summary>نتیجهٔ گذار وضعیت.</summary>
public sealed record TransitionResult(bool Ok, string? Error);

/// <summary>
/// ثبت و تغییر وضعیت درخواست. همهٔ نوشتن‌ها یک ردیف <see cref="RequestEvent"/> و یک ردیف
/// ممیزی می‌سازند تا خط زمانی و زنجیرهٔ هش هیچ‌وقت از هم جدا نشوند.
/// </summary>
public sealed class RequestService(SupportDbContext db, AuditChainService audit, SignerOptions options,
    ILogger<RequestService> log)
{
    /// <summary>ثبت درخواست تازه از ورودی تجزیه‌شده.</summary>
    public async Task<IngestResult> IngestAsync(ParsedRequest parsed, RequestChannel channel, SupportUser? actor,
        IReadOnlyList<(string FileName, byte[] Bytes)>? attachments = null, CancellationToken ct = default)
    {
        if (!parsed.Ok || string.IsNullOrWhiteSpace(parsed.InstallationId))
            return new IngestResult(false, parsed.Warnings.FirstOrDefault(w => w.StartsWith("⛔", StringComparison.Ordinal))
                                            ?? "درخواست معتبر نیست.", 0, null, false);

        // تشخیص تکراری: همان نصب + همان هش محتوا
        var existing = await db.Requests.AsNoTracking()
            .Where(r => r.InstallationId == parsed.InstallationId && r.PayloadSha256 == parsed.Sha256)
            .OrderBy(r => r.Id)
            .FirstOrDefaultAsync(ct);

        var installation = await db.Installations.FirstOrDefaultAsync(i => i.InstallationId == parsed.InstallationId, ct);
        if (installation is null)
        {
            installation = new Installation
            {
                InstallationId = parsed.InstallationId,
                MachineName = parsed.MachineName,
                EngineVersion = parsed.EngineVersion,
                EngineSchema = parsed.EngineSchema,
                BuildDate = parsed.BuildDate,
                CreatedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow
            };
            db.Installations.Add(installation);
        }
        else
        {
            installation.LastSeenAt = DateTime.UtcNow;
            installation.MachineName ??= parsed.MachineName;
            installation.EngineVersion ??= parsed.EngineVersion;
        }

        var request = new LicenseRequest
        {
            RequestNo = await NextRequestNoAsync(ct),
            InstallationRef = installation,
            InstallationId = parsed.InstallationId,
            Type = parsed.Type,
            Status = existing is null ? RequestStatus.New : RequestStatus.Duplicate,
            Channel = channel,
            RequestVersion = parsed.RequestVersion,
            PayloadJson = parsed.RawJson,
            PayloadSha256 = parsed.Sha256,
            ContactName = parsed.ContactName,
            ContactPhone = parsed.ContactPhone,
            ContactEmail = parsed.ContactEmail,
            CustomerNote = parsed.CustomerNote,
            InterestedModulesCsv = parsed.InterestedModules.Count == 0 ? null : string.Join(",", parsed.InterestedModules),
            Deployment = parsed.Deployment,
            MachineName = parsed.MachineName,
            EngineVersion = parsed.EngineVersion,
            EngineSchema = parsed.EngineSchema,
            BuildDate = parsed.BuildDate,
            RequestedAtUtc = parsed.RequestedAtUtc ?? DateTime.UtcNow,
            RequestedAtTehran = JalaliDate.Full(parsed.RequestedAtUtc ?? DateTime.UtcNow),
            WarningsJson = parsed.Warnings.Count == 0 ? null : JsonSerializer.Serialize(parsed.Warnings),
            DuplicateOfRequestId = existing?.Id,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = actor?.Id
        };

        db.Requests.Add(request);
        await db.SaveChangesAsync(ct);

        foreach (var (fileName, bytes) in attachments ?? Array.Empty<(string, byte[])>())
        {
            var safe = SafeFileName(fileName);
            var relative = Path.Combine("attachments", request.RequestNo, safe);
            var full = Path.Combine(options.EffectiveWorkFolder(), relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllBytesAsync(full, bytes, ct);

            db.Attachments.Add(new RequestAttachment
            {
                RequestId = request.Id,
                FileName = safe,
                RelativePath = relative,
                SizeBytes = bytes.LongLength,
                Sha256 = RequestParser.Sha256(bytes),
                CreatedAt = DateTime.UtcNow
            });
        }

        await AddEventAsync(request.Id, "CREATE", null, null,
            existing is null
                ? $"درخواست از کانال «{Ui.Channel(channel)}» ثبت شد."
                : $"فایل تکراری تشخیص داده شد و به درخواست {existing.RequestNo} وصل شد.", actor, ct);

        if (!string.IsNullOrWhiteSpace(parsed.CustomerName))
            await AddEventAsync(request.Id, "NOTE", null, null,
                $"نام مشتری در فایل: «{parsed.CustomerName}» — مشتری باید ساخته/وصل شود.", actor, ct);

        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(actor?.UserName ?? "system", existing is null ? "REQUEST_CREATE" : "REQUEST_DUPLICATE",
            "LicenseRequest", request.Id.ToString(),
            JsonSerializer.Serialize(new { request.RequestNo, request.InstallationId, duplicate = existing?.RequestNo }),
            actor?.Id, ct);

        log.LogInformation("درخواست {No} ثبت شد (نصب {Installation}, کانال {Channel}, تکراری={Dup})",
            request.RequestNo, request.InstallationId, channel, existing is not null);

        return new IngestResult(true, null, request.Id, request.RequestNo, existing is not null);
    }

    /// <summary>شمارهٔ درخواست: REQ-{سال شمسی}-{چهار رقم}.</summary>
    public async Task<string> NextRequestNoAsync(CancellationToken ct = default)
    {
        var year = JalaliDate.ToJalali(JalaliDate.TehranNow()).Year;
        var prefix = $"REQ-{year}-";
        var count = await db.Requests.CountAsync(r => r.RequestNo.StartsWith(prefix), ct);
        for (var i = count + 1; i < count + 500; i++)
        {
            var candidate = $"{prefix}{i:0000}";
            if (!await db.Requests.AnyAsync(r => r.RequestNo == candidate, ct)) return candidate;
        }

        return $"{prefix}{DateTime.UtcNow.Ticks % 10000:0000}";
    }

    public async Task<TransitionResult> TransitionAsync(int requestId, RequestStatus to, string? note, SupportUser actor,
        CancellationToken ct = default)
    {
        var request = await db.Requests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null) return new TransitionResult(false, "درخواست پیدا نشد.");

        var hasIdentity = await db.IdentityChecks.AnyAsync(i => i.RequestId == requestId, ct);
        var check = Workflow.CanTransition(request.Status, to, actor.Role, hasIdentity);
        if (!check.Ok) return new TransitionResult(false, check.Error);

        var from = request.Status;
        request.Status = to;
        request.UpdatedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(note))
            request.InternalNote = note!.Trim().Length <= 1000 ? note.Trim() : note.Trim()[..1000];

        await AddEventAsync(requestId, "STATUS", from.ToString(), to.ToString(),
            string.IsNullOrWhiteSpace(note) ? $"وضعیت به «{Ui.Status(to)}» تغییر کرد." : note.Trim(), actor, ct);
        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(actor.UserName, "STATUS", "LicenseRequest", requestId.ToString(),
            JsonSerializer.Serialize(new { from = from.ToString(), to = to.ToString() }), actor.Id, ct);
        return new TransitionResult(true, null);
    }

    public async Task<(bool Ok, string? Error)> SaveIdentityAsync(int requestId, IdentityMethod method,
        string verifiedWith, string verifiedAtText, string? note, SupportUser actor, CancellationToken ct = default)
    {
        var request = await db.Requests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null) return (false, "درخواست پیدا نشد.");
        if (Workflow.IsClosed(request.Status) && request.Status != RequestStatus.Issued)
            return (false, "درخواست بسته است و تأیید هویت نمی‌پذیرد.");
        if (string.IsNullOrWhiteSpace(verifiedWith))
            return (false, "نام تأییدکننده لازم است (چه کسی تأیید کرد).");
        if (!JalaliDate.TryParse(verifiedAtText, out var verifiedAt))
            return (false, "تاریخ تأیید باید شمسی و به شکل 1405/08/23 باشد.");

        db.IdentityChecks.Add(new IdentityCheck
        {
            RequestId = requestId,
            Method = method,
            VerifiedWith = verifiedWith.Trim().Length <= 150 ? verifiedWith.Trim() : verifiedWith.Trim()[..150],
            VerifiedAt = verifiedAt,
            Note = string.IsNullOrWhiteSpace(note) ? null : (note!.Trim().Length <= 500 ? note.Trim() : note.Trim()[..500]),
            ByName = actor.DisplayName,
            CreatedAt = DateTime.UtcNow
        });
        await AddEventAsync(requestId, "IDENTITY", null, null,
            $"تأیید هویت از راه «{Ui.IdentityMethodText(method)}» با «{verifiedWith.Trim()}».", actor, ct);
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(actor.UserName, "IDENTITY", "LicenseRequest", requestId.ToString(),
            JsonSerializer.Serialize(new { method = method.ToString() }), actor.Id, ct);
        return (true, null);
    }

    public async Task AddNoteAsync(int requestId, string text, SupportUser actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        await AddEventAsync(requestId, "NOTE", null, null, text.Trim(), actor, ct);
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(actor.UserName, "NOTE", "LicenseRequest", requestId.ToString(), null, actor.Id, ct);
    }

    /// <summary>وصل کردن درخواست به مشتری موجود (و نصبش).</summary>
    public async Task<(bool Ok, string? Error)> LinkCustomerAsync(int requestId, int customerId, SupportUser actor,
        CancellationToken ct = default)
    {
        var request = await db.Requests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null) return (false, "درخواست پیدا نشد.");
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId && c.MergedIntoId == null, ct);
        if (customer is null) return (false, "مشتری پیدا نشد یا ادغام شده است.");

        request.CustomerId = customer.Id;
        request.UpdatedAt = DateTime.UtcNow;
        if (request.InstallationRefId is { } installationId)
        {
            var installation = await db.Installations.FindAsync(new object?[] { installationId }, ct);
            if (installation is not null && installation.CustomerId is null) installation.CustomerId = customer.Id;
        }

        await AddEventAsync(requestId, "CUSTOMER", null, null, $"مشتری «{customer.Name}» به درخواست وصل شد.", actor, ct);
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(actor.UserName, "CUSTOMER_LINK", "LicenseRequest", requestId.ToString(),
            JsonSerializer.Serialize(new { customerId = customer.Id }), actor.Id, ct);
        return (true, null);
    }

    public async Task AddEventAsync(int requestId, string kind, string? fromStatus, string? toStatus, string text,
        SupportUser? actor, CancellationToken ct = default)
    {
        db.RequestEvents.Add(new RequestEvent
        {
            RequestId = requestId,
            Kind = kind,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Text = text.Length <= 2000 ? text : text[..2000],
            ByName = actor?.DisplayName ?? "system",
            At = DateTime.UtcNow
        });
        await Task.CompletedTask;
    }

    public static string SafeFileName(string? name)
    {
        var fileName = Path.GetFileName(name ?? "");
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "attachment";
        var cleaned = new string(fileName.Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_').ToArray());
        return cleaned.Length == 0 ? "attachment" : cleaned;
    }
}
