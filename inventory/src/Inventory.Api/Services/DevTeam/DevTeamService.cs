using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.DevTeam;

/// <summary>
/// پیاده‌سازی سرویس مدیریت برنامه‌نویسان.
/// <para>
/// نکتهٔ پیاده‌سازی: حجم دادهٔ این ماژول کوچک است (چند نفر، چند ده ماژول، چند صد آیتم)،
/// پس جمع‌بندی‌ها عمداً در حافظه انجام می‌شوند نه در SQL. این کار هم ترجمه‌پذیری
/// LINQ را به خطر نمی‌اندازد و هم خواندن کد را ساده نگه می‌دارد.
/// </para>
/// </summary>
public class DevTeamService : IDevTeamService
{
    private readonly AppDbContext _db;

    public DevTeamService(AppDbContext db) => _db = db;

    // ==================================================================
    //  اعضای تیم
    // ==================================================================

    public async Task<List<DevMemberDto>> GetMembersAsync(bool activeOnly = false)
    {
        var query = _db.DevMembers.AsNoTracking();
        if (activeOnly) query = query.Where(m => m.IsActive);
        var members = await query.OrderBy(m => m.FullName).ToListAsync();

        // شمارش مالکیت ماژول‌ها و آیتم‌های باز — با یک پرس‌وجو هرکدام، نه N پرس‌وجو
        var owned = await _db.DevModules.AsNoTracking()
            .Where(m => m.OwnerId != null)
            .GroupBy(m => m.OwnerId!.Value)
            .Select(g => new { MemberId = g.Key, Count = g.Count() })
            .ToListAsync();
        var openTasks = await _db.DevTasks.AsNoTracking()
            .Where(t => t.AssigneeId != null && t.Status != DevTaskStatus.Done)
            .GroupBy(t => t.AssigneeId!.Value)
            .Select(g => new { MemberId = g.Key, Count = g.Count() })
            .ToListAsync();

        return members.Select(m => ToDto(m,
            owned.FirstOrDefault(o => o.MemberId == m.Id)?.Count ?? 0,
            openTasks.FirstOrDefault(o => o.MemberId == m.Id)?.Count ?? 0)).ToList();
    }

    public async Task<DevMemberDto> SaveMemberAsync(DevMemberDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.FullName))
            throw new InvalidOperationException("نام کامل عضو تیم نمی‌تواند خالی باشد.");
        if (!string.IsNullOrWhiteSpace(dto.ColorHex) && !IsHexColor(dto.ColorHex))
            throw new InvalidOperationException("قالب رنگ نامعتبر است. نمونهٔ درست: #2f6fed");

        DevMember entity;
        if (dto.Id > 0)
        {
            entity = await _db.DevMembers.FirstOrDefaultAsync(m => m.Id == dto.Id)
                     ?? throw new InvalidOperationException("عضو تیم یافت نشد.");
        }
        else
        {
            // جلوگیری از عضو تکراری — همان دردی که ۲۱ هویت نویسنده در git ساخت
            var dup = await _db.DevMembers.AnyAsync(m => m.FullName == dto.FullName.Trim());
            if (dup) throw new InvalidOperationException($"عضوی با نام «{dto.FullName.Trim()}» پیش‌تر ثبت شده است.");
            entity = new DevMember { CreatedAt = DateTime.Now };
            _db.DevMembers.Add(entity);
        }

        entity.FullName = dto.FullName.Trim();
        entity.GithubHandle = dto.GithubHandle?.Trim();
        entity.Email = dto.Email?.Trim();
        entity.Phone = dto.Phone?.Trim();
        entity.Role = dto.Role;
        entity.IsActive = dto.IsActive;
        entity.ColorHex = string.IsNullOrWhiteSpace(dto.ColorHex) ? "#6c757d" : dto.ColorHex.Trim();
        entity.Note = dto.Note?.Trim();

        await _db.SaveChangesAsync();
        return ToDto(entity);
    }

    public async Task DeleteMemberAsync(int id)
    {
        var m = await _db.DevMembers.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new InvalidOperationException("عضو تیم یافت نشد.");

        // به‌جای حذف سخت، اگر جایی ارجاع شده باشد غیرفعال می‌کنیم تا تاریخچهٔ کار از بین نرود.
        var referenced = await _db.DevTasks.AnyAsync(t => t.AssigneeId == id)
                         || await _db.DevTaskLogs.AnyAsync(l => l.MemberId == id)
                         || await _db.DevModules.AnyAsync(x => x.OwnerId == id);
        if (referenced)
        {
            m.IsActive = false;
            await _db.SaveChangesAsync();
            throw new InvalidOperationException(
                "این عضو در آیتم‌ها یا مالکیت ماژول‌ها ارجاع شده است، پس حذف نشد و فقط غیرفعال گردید. " +
                "ابتدا آیتم‌هایش را به فرد دیگری واگذار کنید.");
        }

        _db.DevMembers.Remove(m);
        await _db.SaveChangesAsync();
    }

    // ==================================================================
    //  ماژول‌ها و مالکیت
    // ==================================================================

    public async Task<List<DevModuleDto>> GetModulesAsync(bool activeOnly = false)
    {
        var query = _db.DevModules.AsNoTracking();
        if (activeOnly) query = query.Where(m => m.IsActive);
        var modules = await query.OrderBy(m => m.SortOrder).ThenBy(m => m.Key).ToListAsync();

        // نام و نقش مالک با هم — تا ستون «مالک» در صفحهٔ ماژول‌ها نقش را هم نشان دهد
        var members = await _db.DevMembers.AsNoTracking()
            .ToDictionaryAsync(m => m.Id, m => new { m.FullName, m.Role });

        // شمارش بر پایهٔ (ماژول، وضعیت) در یک پرس‌وجو؛ قبلاً فقط «باز» شمرده می‌شد و
        // صفحه نمی‌توانست «در حال انجام» و «مسدود» را جدا نشان دهد.
        var statusCounts = await _db.DevTasks.AsNoTracking()
            .GroupBy(t => new { t.ModuleId, t.Status })
            .Select(g => new { g.Key.ModuleId, g.Key.Status, Count = g.Count() })
            .ToListAsync();

        int Count(int moduleId, DevTaskStatus status) =>
            statusCounts.FirstOrDefault(c => c.ModuleId == moduleId && c.Status == status)?.Count ?? 0;

        return modules.Select(m =>
        {
            var dto = ToDto(m);
            if (m.OwnerId.HasValue && members.TryGetValue(m.OwnerId.Value, out var owner))
            {
                dto.OwnerName = owner.FullName;
                dto.OwnerRoleTitle = DevTeamLabels.Role(owner.Role);
            }
            dto.InProgress = Count(m.Id, DevTaskStatus.InProgress);
            dto.Blocked = Count(m.Id, DevTaskStatus.Blocked);
            dto.Backlog = Count(m.Id, DevTaskStatus.Backlog);
            dto.OpenTasks = dto.InProgress + dto.Blocked + dto.Backlog
                            + Count(m.Id, DevTaskStatus.InReview);
            return dto;
        }).ToList();
    }

    public async Task<DevModuleDto> SaveModuleAsync(DevModuleDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Key))
            throw new InvalidOperationException("کلید ماژول نمی‌تواند خالی باشد.");
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new InvalidOperationException("نام ماژول نمی‌تواند خالی باشد.");

        var key = dto.Key.Trim();
        DevModule entity;
        if (dto.Id > 0)
        {
            entity = await _db.DevModules.FirstOrDefaultAsync(m => m.Id == dto.Id)
                     ?? throw new InvalidOperationException("ماژول یافت نشد.");
            var dupKey = await _db.DevModules.AnyAsync(m => m.Key == key && m.Id != dto.Id);
            if (dupKey) throw new InvalidOperationException($"ماژول دیگری با کلید «{key}» وجود دارد.");
        }
        else
        {
            if (await _db.DevModules.AnyAsync(m => m.Key == key))
                throw new InvalidOperationException($"ماژول «{key}» پیش‌تر ثبت شده است.");
            entity = new DevModule { CreatedAt = DateTime.Now };
            _db.DevModules.Add(entity);
        }

        if (dto.OwnerId is > 0 && !await _db.DevMembers.AnyAsync(m => m.Id == dto.OwnerId))
            throw new InvalidOperationException("مالک انتخاب‌شده در فهرست اعضای تیم وجود ندارد.");

        entity.Key = key;
        entity.Title = dto.Title.Trim();
        entity.OwnerId = dto.OwnerId > 0 ? dto.OwnerId : null;
        entity.Icon = dto.Icon?.Trim();
        entity.ColorHex = string.IsNullOrWhiteSpace(dto.ColorHex) ? "#0d6efd" : dto.ColorHex.Trim();
        entity.SortOrder = dto.SortOrder;
        entity.IsActive = dto.IsActive;
        entity.RepoPaths = dto.RepoPaths?.Trim();
        entity.ServiceLines = dto.ServiceLines;
        entity.PageCount = dto.PageCount;
        entity.Note = dto.Note?.Trim();

        await _db.SaveChangesAsync();
        return await EnrichModuleAsync(entity);
    }

    public async Task DeleteModuleAsync(int id)
    {
        var m = await _db.DevModules.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new InvalidOperationException("ماژول یافت نشد.");

        if (await _db.DevTasks.AnyAsync(t => t.ModuleId == id))
            throw new InvalidOperationException(
                "این ماژول آیتم کاری دارد و حذف نمی‌شود. ابتدا آیتم‌ها را به ماژول دیگری منتقل کنید " +
                "یا ماژول را غیرفعال کنید.");

        _db.DevModules.Remove(m);
        await _db.SaveChangesAsync();
    }

    public async Task<DevModuleDto> SetModuleOwnerAsync(int moduleId, int? ownerId)
    {
        var m = await _db.DevModules.FirstOrDefaultAsync(x => x.Id == moduleId)
                ?? throw new InvalidOperationException("ماژول یافت نشد.");

        if (ownerId is > 0)
        {
            var owner = await _db.DevMembers.FirstOrDefaultAsync(x => x.Id == ownerId)
                        ?? throw new InvalidOperationException("فرد انتخاب‌شده در فهرست اعضای تیم وجود ندارد.");
            if (!owner.IsActive)
                throw new InvalidOperationException($"«{owner.FullName}» غیرفعال است و نمی‌تواند مالک ماژول باشد.");
            m.OwnerId = owner.Id;
        }
        else
        {
            m.OwnerId = null;
        }

        await _db.SaveChangesAsync();
        return await EnrichModuleAsync(m);
    }

    public async Task<int> SyncModulesFromRepoAsync(string ownershipCsvPath)
    {
        if (!File.Exists(ownershipCsvPath))
            throw new InvalidOperationException($"فایل {ownershipCsvPath} پیدا نشد.");

        var lines = await File.ReadAllLinesAsync(ownershipCsvPath);
        if (lines.Length < 2) return 0;

        var existing = await _db.DevModules.ToDictionaryAsync(m => m.Key, m => m);

        // نگاشت نام کاربری GitHub → شناسهٔ عضو، برای پر کردن خودکار مالک از csv
        var byGithub = (await _db.DevMembers.AsNoTracking()
                .Where(m => m.GithubHandle != null).ToListAsync())
            .GroupBy(m => m.GithubHandle!.Trim().TrimStart('@').ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().Id);

        var added = 0;
        var order = existing.Count == 0 ? 0 : existing.Values.Max(v => v.SortOrder);

        // ستون‌های csv: module,services_dir,controllers_dir,entities_dir,pages_dir,service_lines,pages,owner_github
        foreach (var raw in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var f = raw.Split(',');
            if (f.Length < 7) continue;
            var key = f[0].Trim();
            if (key.Length == 0) continue;

            // ماژول‌های موجود فقط از نظر حجم کد به‌روز می‌شوند؛ مالکِ تعیین‌شدهٔ دستی دست‌نخورده می‌ماند.
            if (existing.TryGetValue(key, out var m))
            {
                var paths = string.Join(";", new[] { f[1], f[2], f[3], f[4] }.Where(p => !string.IsNullOrWhiteSpace(p)));
                if (paths.Length > 1000) paths = paths[..1000];
                m.RepoPaths = paths;
                if (int.TryParse(f[5], out var sl)) m.ServiceLines = sl;
                if (int.TryParse(f[6], out var pc)) m.PageCount = pc;
                if (m.Title == m.Key) m.Title = DevTeamLabels.ModuleTitle(m.Key); // عنوان لاتینِ قدیمی را فارسی کن
                if (m.OwnerId is null && f.Length >= 8)
                {
                    var handle = f[7].Trim().TrimStart('@');
                    if (handle.Length > 0 && byGithub.TryGetValue(handle.ToLowerInvariant(), out var oid))
                        m.OwnerId = oid;
                }
                continue;
            }

            order += 10;
            var newPaths = string.Join(";", new[] { f[1], f[2], f[3], f[4] }.Where(p => !string.IsNullOrWhiteSpace(p)));
            if (newPaths.Length > 1000) newPaths = newPaths[..1000];

            // ستون هشتم (owner_github) در csv خالی است؛ اگر روزی پر شود، مالک به‌صورت
            // خودکار از روی نام کاربری GitHub به عضو تیم وصل می‌شود. یعنی همان فایلی
            // که CODEOWNERS از آن ساخته می‌شود، بورد را هم پر می‌کند.
            int? ownerId = null;
            if (f.Length >= 8)
            {
                var handle = f[7].Trim().TrimStart('@');
                if (handle.Length > 0)
                    ownerId = byGithub.GetValueOrDefault(handle.ToLowerInvariant());
            }

            _db.DevModules.Add(new DevModule
            {
                Key = key,
                Title = DevTeamLabels.ModuleTitle(key), // فارسیِ شناخته‌شده، وگرنه همان کلید
                OwnerId = ownerId,
                SortOrder = order,
                IsActive = true,
                RepoPaths = newPaths,
                ServiceLines = int.TryParse(f[5], out var s2) ? s2 : 0,
                PageCount = int.TryParse(f[6], out var p2) ? p2 : 0,
                CreatedAt = DateTime.Now
            });
            added++;
        }

        await _db.SaveChangesAsync();
        return added;
    }

    // ==================================================================
    //  آیتم‌های کاری
    // ==================================================================

    public async Task<PagedResult<DevTaskDto>> GetTasksAsync(
        string? search, DevTaskStatus? status, int? moduleId, int? assigneeId, int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 20;

        var q = _db.DevTasks.AsNoTracking();
        if (status.HasValue) q = q.Where(t => t.Status == status.Value);
        if (moduleId is > 0) q = q.Where(t => t.ModuleId == moduleId);
        if (assigneeId is > 0) q = q.Where(t => t.AssigneeId == assigneeId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(t => t.Number.Contains(s) || t.Title.Contains(s) || (t.BranchName != null && t.BranchName.Contains(s)));
        }

        var total = await q.CountAsync();
        var items = await q
            .OrderBy(t => t.Status == DevTaskStatus.Done)
            .ThenByDescending(t => t.Priority)
            .ThenByDescending(t => t.UpdatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        var enriched = await EnrichAsync(items);
        return new PagedResult<DevTaskDto>
        {
            Items = enriched,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<DevTaskDto?> GetTaskAsync(int id)
    {
        var t = await _db.DevTasks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (t is null) return null;
        return (await EnrichAsync([t])).FirstOrDefault();
    }

    public async Task<DevTaskDto> SaveTaskAsync(DevTaskDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new InvalidOperationException("عنوان آیتم نمی‌تواند خالی باشد.");
        if (dto.ModuleId <= 0 || !await _db.DevModules.AnyAsync(m => m.Id == dto.ModuleId))
            throw new InvalidOperationException("ماژول انتخاب‌شده معتبر نیست.");
        if (dto.AssigneeId is > 0 && !await _db.DevMembers.AnyAsync(m => m.Id == dto.AssigneeId))
            throw new InvalidOperationException("فرد مسئول در فهرست اعضای تیم وجود ندارد.");

        DevTask entity;
        bool isNew = dto.Id <= 0;
        DevTaskStatus? fromStatus = null;
        int? fromAssignee = null;

        if (isNew)
        {
            entity = new DevTask { CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now };
            if (string.IsNullOrWhiteSpace(dto.Number))
                entity.Number = await NextNumberAsync(dto.ModuleId);
            _db.DevTasks.Add(entity);
        }
        else
        {
            entity = await _db.DevTasks.FirstOrDefaultAsync(t => t.Id == dto.Id)
                     ?? throw new InvalidOperationException("آیتم یافت نشد.");
            fromStatus = entity.Status;
            fromAssignee = entity.AssigneeId;
        }

        if (!string.IsNullOrWhiteSpace(dto.Number)) entity.Number = dto.Number.Trim();
        entity.Title = dto.Title.Trim();
        entity.Description = dto.Description?.Trim();
        entity.ModuleId = dto.ModuleId;
        entity.AssigneeId = dto.AssigneeId > 0 ? dto.AssigneeId : null;
        entity.Status = dto.Status;
        entity.Priority = dto.Priority;
        entity.Size = dto.Size;
        entity.BranchName = dto.BranchName?.Trim();
        entity.PullRequestUrl = dto.PullRequestUrl?.Trim();
        entity.AgentAssisted = dto.AgentAssisted;
        entity.DueDate = dto.DueDate;
        entity.UpdatedAt = DateTime.Now;

        if (isNew)
        {
            entity.CreatedBy = dto.CreatedBy?.Trim();
            entity.StartedAt = entity.Status == DevTaskStatus.InProgress ? DateTime.Now : null;
            entity.CompletedAt = entity.Status == DevTaskStatus.Done ? DateTime.Now : null;
        }
        else
        {
            ApplyStatusDates(entity, fromStatus);
        }

        await _db.SaveChangesAsync();

        // تاریخچهٔ کار — بدون آن، «کی چه کرد» دوباره گم می‌شود
        if (isNew)
        {
            AddLog(entity.Id, entity.AssigneeId, DevLogAction.Created, "آیتم ایجاد شد.", null, entity.Status);
        }
        else
        {
            if (fromStatus.HasValue && fromStatus.Value != entity.Status)
                AddLog(entity.Id, dto.AssigneeId, DevLogAction.StatusChanged, null, fromStatus, entity.Status);
            if (fromAssignee != entity.AssigneeId)
                AddLog(entity.Id, entity.AssigneeId, DevLogAction.Assigned, "فرد مسئول تغییر کرد.", null, null, null);
        }
        await _db.SaveChangesAsync();

        return (await EnrichAsync([entity])).First();
    }

    public async Task DeleteTaskAsync(int id)
    {
        var t = await _db.DevTasks.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new InvalidOperationException("آیتم یافت نشد.");
        var logs = await _db.DevTaskLogs.Where(l => l.TaskId == id).ToListAsync();
        _db.DevTaskLogs.RemoveRange(logs);
        _db.DevTasks.Remove(t);
        await _db.SaveChangesAsync();
    }

    public async Task<DevTaskDto> SetStatusAsync(int id, DevTaskStatusRequest request)
    {
        var t = await _db.DevTasks.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new InvalidOperationException("آیتم یافت نشد.");

        var from = t.Status;
        if (from == request.Status)
            return (await EnrichAsync([t])).First();

        // یک آیتم بدون مسئول نباید «در حال انجام» باشد — وگرنه بورد دروغ می‌گوید
        if (request.Status == DevTaskStatus.InProgress && t.AssigneeId is null)
            throw new InvalidOperationException(
                "پیش از شروع کار باید فرد مسئول تعیین شود؛ وگرنه مشخص نیست چه کسی آن را انجام می‌دهد.");

        t.Status = request.Status;
        ApplyStatusDates(t, from);
        t.UpdatedAt = DateTime.Now;

        _db.DevTaskLogs.Add(new DevTaskLog
        {
            TaskId = t.Id,
            MemberId = t.AssigneeId,
            Action = request.Status == DevTaskStatus.Done ? DevLogAction.Completed : DevLogAction.StatusChanged,
            Note = string.IsNullOrWhiteSpace(request.Note)
                ? $"{DevTeamLabels.Status(from)} ← {DevTeamLabels.Status(request.Status)}"
                : request.Note.Trim(),
            FromStatus = from,
            ToStatus = request.Status,
            CommitSha = request.CommitSha?.Trim(),
            At = DateTime.Now
        });

        await _db.SaveChangesAsync();
        return (await EnrichAsync([t])).First();
    }

    public async Task<DevTaskDto> AssignAsync(int id, int? memberId, string? note = null)
    {
        var t = await _db.DevTasks.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new InvalidOperationException("آیتم یافت نشد.");

        if (memberId is > 0)
        {
            var m = await _db.DevMembers.FirstOrDefaultAsync(x => x.Id == memberId)
                    ?? throw new InvalidOperationException("فرد انتخاب‌شده در فهرست اعضای تیم وجود ندارد.");
            if (!m.IsActive) throw new InvalidOperationException($"«{m.FullName}» غیرفعال است.");
        }

        var previous = t.AssigneeId;
        t.AssigneeId = memberId > 0 ? memberId : null;
        t.UpdatedAt = DateTime.Now;

        string text;
        if (t.AssigneeId is int newId)
        {
            var name = (await _db.DevMembers.FindAsync(newId))?.FullName ?? "";
            text = string.IsNullOrWhiteSpace(note) ? $"واگذار شد به {name}" : note.Trim();
        }
        else
        {
            text = string.IsNullOrWhiteSpace(note) ? "واگذاری برداشته شد" : note.Trim();
        }

        _db.DevTaskLogs.Add(new DevTaskLog
        {
            TaskId = t.Id,
            MemberId = t.AssigneeId,
            Action = DevLogAction.Assigned,
            Note = text,
            At = DateTime.Now
        });

        await _db.SaveChangesAsync();
        _ = previous;
        return (await EnrichAsync([t])).First();
    }

    // ==================================================================
    //  تاریخچهٔ کار
    // ==================================================================

    public async Task<List<DevTaskLogDto>> GetTaskLogsAsync(int taskId)
    {
        var logs = await _db.DevTaskLogs.AsNoTracking()
            .Where(l => l.TaskId == taskId)
            .OrderByDescending(l => l.At).ToListAsync();
        return await EnrichLogsAsync(logs);
    }

    public async Task<DevTaskLogDto> AddLogAsync(int taskId, DevTaskLogRequest request)
    {
        var task = await _db.DevTasks.FirstOrDefaultAsync(t => t.Id == taskId)
                   ?? throw new InvalidOperationException("آیتم یافت نشد.");

        var memberId = request.MemberId > 0 ? request.MemberId : task.AssigneeId;
        if (memberId is > 0 && !await _db.DevMembers.AnyAsync(m => m.Id == memberId))
            throw new InvalidOperationException("فرد انتخاب‌شده در فهرست اعضای تیم وجود ندارد.");

        var log = new DevTaskLog
        {
            TaskId = taskId,
            MemberId = memberId > 0 ? memberId : null,
            Action = request.Action,
            Note = string.IsNullOrWhiteSpace(request.Note) ? DevTeamLabels.Action(request.Action) : request.Note.Trim(),
            CommitSha = request.CommitSha?.Trim(),
            At = DateTime.Now
        };
        _db.DevTaskLogs.Add(log);

        task.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        return (await EnrichLogsAsync([log])).First();
    }

    public async Task<List<DevTaskLogDto>> GetActivityAsync(int days = 14, int take = 200)
    {
        if (days < 1) days = 14;
        if (take < 1 || take > 1000) take = 200;
        var since = DateTime.Now.AddDays(-days);

        var logs = await _db.DevTaskLogs.AsNoTracking()
            .Where(l => l.At >= since)
            .OrderByDescending(l => l.At)
            .Take(take)
            .ToListAsync();
        return await EnrichLogsAsync(logs);
    }

    // ==================================================================
    //  بورد و شاخص‌ها
    // ==================================================================

    public async Task<DevBoardDto> GetBoardAsync(int? moduleId = null, int? assigneeId = null)
    {
        var q = _db.DevTasks.AsNoTracking();
        if (moduleId is > 0) q = q.Where(t => t.ModuleId == moduleId);
        if (assigneeId is > 0) q = q.Where(t => t.AssigneeId == assigneeId);
        var tasks = await q.OrderByDescending(t => t.Priority).ThenByDescending(t => t.UpdatedAt).ToListAsync();
        var enriched = await EnrichAsync(tasks);

        var columns = DevTeamLabels.BoardOrder
            .Select(s => new DevBoardColumnDto
            {
                Status = s,
                Title = DevTeamLabels.Status(s),
                Tasks = enriched.Where(t => t.Status == s).ToList()
            })
            .ToList();

        // ---------- بار کاری اعضا ----------
        var members = await _db.DevMembers.AsNoTracking().Where(m => m.IsActive).OrderBy(m => m.FullName).ToListAsync();
        var owned = await _db.DevModules.AsNoTracking()
            .Where(m => m.OwnerId != null)
            .GroupBy(m => m.OwnerId!.Value)
            .Select(g => new { MemberId = g.Key, Count = g.Count() })
            .ToListAsync();

        var workload = members.Select(m =>
        {
            var mine = enriched.Where(t => t.AssigneeId == m.Id).ToList();
            return new DevWorkloadDto
            {
                MemberId = m.Id,
                FullName = m.FullName,
                ColorHex = m.ColorHex,
                RoleTitle = DevTeamLabels.Role(m.Role),
                Backlog = mine.Count(t => t.Status == DevTaskStatus.Backlog),
                InProgress = mine.Count(t => t.Status == DevTaskStatus.InProgress),
                InReview = mine.Count(t => t.Status == DevTaskStatus.InReview),
                Blocked = mine.Count(t => t.Status == DevTaskStatus.Blocked),
                Done = mine.Count(t => t.Status == DevTaskStatus.Done),
                OwnedModules = owned.FirstOrDefault(o => o.MemberId == m.Id)?.Count ?? 0
            };
        }).OrderByDescending(w => w.InProgress + w.Blocked).ThenByDescending(w => w.Total).ToList();

        // ---------- بار ماژول‌ها ----------
        var modules = await _db.DevModules.AsNoTracking().OrderBy(m => m.SortOrder).ThenBy(m => m.Key).ToListAsync();
        var moduleLoad = modules.Select(m =>
        {
            var mine = enriched.Where(t => t.ModuleId == m.Id).ToList();
            var owner = m.OwnerId.HasValue ? members.FirstOrDefault(x => x.Id == m.OwnerId.Value) : null;
            return new DevModuleLoadDto
            {
                ModuleId = m.Id,
                Key = m.Key,
                Title = m.Title,
                ColorHex = m.ColorHex,
                OwnerName = owner?.FullName,
                OpenTasks = mine.Count(t => t.Status != DevTaskStatus.Done),
                InProgress = mine.Count(t => t.Status == DevTaskStatus.InProgress),
                Blocked = mine.Count(t => t.Status == DevTaskStatus.Blocked),
                DoneTasks = mine.Count(t => t.Status == DevTaskStatus.Done),
                ServiceLines = m.ServiceLines
            };
        }).ToList();

        // ---------- شاخص‌ها ----------
        var weekAgo = DateTime.Now.AddDays(-7);
        var done = enriched.Where(t => t.Status == DevTaskStatus.Done).ToList();
        var durations = done
            .Where(t => t.StartedAt.HasValue && t.CompletedAt.HasValue)
            .Select(t => (t.CompletedAt!.Value - t.StartedAt!.Value).TotalDays)
            .Where(d => d >= 0).OrderBy(d => d).ToList();

        var stats = new DevStatsDto
        {
            ActiveMembers = members.Count,
            TotalModules = modules.Count,
            ModulesWithOwner = modules.Count(m => m.OwnerId.HasValue),
            OpenTasks = enriched.Count(t => t.Status != DevTaskStatus.Done),
            InProgress = enriched.Count(t => t.Status == DevTaskStatus.InProgress),
            InReview = enriched.Count(t => t.Status == DevTaskStatus.InReview),
            Blocked = enriched.Count(t => t.Status == DevTaskStatus.Blocked),
            DoneThisWeek = done.Count(t => t.CompletedAt.HasValue && t.CompletedAt.Value >= weekAgo),
            Overdue = enriched.Count(t => t.Status != DevTaskStatus.Done && t.DueDate.HasValue && t.DueDate.Value < DateTime.Now),
            AgentAssistedShare = done.Count == 0 ? 0 : (int)Math.Round(100.0 * done.Count(t => t.AgentAssisted) / done.Count),
            MedianDaysToComplete = durations.Count == 0 ? 0 : Median(durations)
        };

        return new DevBoardDto { Columns = columns, Workload = workload, ModuleLoad = moduleLoad, Stats = stats };
    }

    // ==================================================================
    //  کمکی‌ها
    // ==================================================================

    /// <summary>پرکردن نام ماژول و نام فرد مسئول روی DTOها با دو پرس‌وجو، نه N پرس‌وجو.</summary>
    private async Task<List<DevTaskDto>> EnrichAsync(List<DevTask> tasks)
    {
        if (tasks.Count == 0) return [];

        var moduleIds = tasks.Select(t => t.ModuleId).Distinct().ToList();
        var memberIds = tasks.Where(t => t.AssigneeId.HasValue).Select(t => t.AssigneeId!.Value).Distinct().ToList();

        var modules = await _db.DevModules.AsNoTracking()
            .Where(m => moduleIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m);
        var members = await _db.DevMembers.AsNoTracking()
            .Where(m => memberIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m);

        var now = DateTime.Now;
        return tasks.Select(t =>
        {
            var dto = ToDto(t);
            if (modules.TryGetValue(t.ModuleId, out var mod))
            {
                dto.ModuleKey = mod.Key;
                dto.ModuleTitle = mod.Title;
                dto.ModuleColorHex = mod.ColorHex;
            }
            if (t.AssigneeId is int aid && members.TryGetValue(aid, out var mem))
            {
                dto.AssigneeName = mem.FullName;
                dto.AssigneeColorHex = mem.ColorHex;
            }
            dto.DaysToDue = t.DueDate.HasValue ? (int)Math.Ceiling((t.DueDate.Value - now).TotalDays) : null;
            return dto;
        }).ToList();
    }

    private async Task<List<DevTaskLogDto>> EnrichLogsAsync(List<DevTaskLog> logs)
    {
        if (logs.Count == 0) return [];
        var taskIds = logs.Select(l => l.TaskId).Distinct().ToList();
        var memberIds = logs.Where(l => l.MemberId.HasValue).Select(l => l.MemberId!.Value).Distinct().ToList();

        var tasks = await _db.DevTasks.AsNoTracking()
            .Where(t => taskIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t);
        var members = await _db.DevMembers.AsNoTracking()
            .Where(m => memberIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m);

        return logs.Select(l =>
        {
            var dto = new DevTaskLogDto
            {
                Id = l.Id,
                TaskId = l.TaskId,
                MemberId = l.MemberId,
                Action = l.Action,
                Note = l.Note,
                FromStatus = l.FromStatus,
                ToStatus = l.ToStatus,
                CommitSha = l.CommitSha,
                At = l.At
            };
            if (tasks.TryGetValue(l.TaskId, out var t)) { dto.TaskNumber = t.Number; dto.TaskTitle = t.Title; }
            if (l.MemberId is int mid && members.TryGetValue(mid, out var m)) dto.MemberName = m.FullName;
            return dto;
        }).ToList();
    }

    private void AddLog(int taskId, int? memberId, DevLogAction action, string? note,
                        DevTaskStatus? from, DevTaskStatus? to, string? commitSha = null)
        => _db.DevTaskLogs.Add(new DevTaskLog
        {
            TaskId = taskId,
            MemberId = memberId,
            Action = action,
            Note = note,
            FromStatus = from,
            ToStatus = to,
            CommitSha = commitSha,
            At = DateTime.Now
        });

    /// <summary>تاریخ شروع/پایان را با وضعیت هم‌راستا نگه می‌دارد تا شاخص‌ها درست بمانند.</summary>
    private static void ApplyStatusDates(DevTask t, DevTaskStatus? from)
    {
        if (t.Status == DevTaskStatus.InProgress && !t.StartedAt.HasValue) t.StartedAt = DateTime.Now;
        if (t.Status == DevTaskStatus.Done)
        {
            if (!t.StartedAt.HasValue) t.StartedAt = DateTime.Now;
            t.CompletedAt = DateTime.Now;
        }
        else if (from == DevTaskStatus.Done)
        {
            // از «انجام شد» برگشته — پس دیگر تمام‌شده نیست
            t.CompletedAt = null;
        }
    }

    /// <summary>ساخت کلید بعدی یک ماژول، مثل HR-7. شماره از شمار آیتم‌های موجود همان ماژول می‌آید.</summary>
    private async Task<string> NextNumberAsync(int moduleId)
    {
        var mod = await _db.DevModules.AsNoTracking().FirstOrDefaultAsync(m => m.Id == moduleId);
        var prefix = (mod?.Key ?? "TASK").ToUpperInvariant();
        var used = await _db.DevTasks.AsNoTracking()
            .Where(t => t.ModuleId == moduleId && t.Number.StartsWith(prefix + "-"))
            .Select(t => t.Number).ToListAsync();

        var max = 0;
        foreach (var n in used)
        {
            var tail = n[(prefix.Length + 1)..];
            if (int.TryParse(tail, out var v) && v > max) max = v;
        }
        return $"{prefix}-{max + 1}";
    }

    private static double Median(List<double> sorted)
    {
        if (sorted.Count == 0) return 0;
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    private static bool IsHexColor(string s)
        => s.Length is 7 or 9 && s[0] == '#' && s.Skip(1).All(Uri.IsHexDigit);

    // ---------------- نگاشت ----------------

    private static DevMemberDto ToDto(DevMember m, int ownedModules = 0, int openTasks = 0) => new()
    {
        Id = m.Id,
        FullName = m.FullName,
        GithubHandle = m.GithubHandle,
        Email = m.Email,
        Phone = m.Phone,
        Role = m.Role,
        IsActive = m.IsActive,
        ColorHex = m.ColorHex,
        Note = m.Note,
        CreatedAt = m.CreatedAt,
        OwnedModules = ownedModules,
        OpenTasks = openTasks
    };

    /// <summary>
    /// غنی‌سازی DTO یک ماژول — نام و نقش مالک + شمارش آیتم‌ها بر پایهٔ وضعیت.
    /// <para>
    /// پیش‌تر «تعیین مالک» و «ذخیرهٔ ماژول» فقط <c>ToDto</c> خام را برمی‌گرداندند، پس
    /// پاسخشان <c>ownerRoleTitle</c> نداشت و شمارش آیتم‌هایش صفرِ کهنه بود؛ در نتیجه
    /// صفحهٔ ماژول‌ها مجبور بود بعد از هر ذخیره کل فهرست را دوباره از سرور بگیرد.
    /// حالا همان منطقی که <see cref="GetModulesAsync"/> به‌صورت دسته‌ای اجرا می‌کند،
    /// برای یک ماژول هم اعمال می‌شود تا دو مسیر پاسخ یکسان بدهند.
    /// </para>
    /// </summary>
    private async Task<DevModuleDto> EnrichModuleAsync(DevModule m)
    {
        var dto = ToDto(m);

        if (m.OwnerId is int oid)
        {
            var owner = await _db.DevMembers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == oid);
            if (owner is not null)
            {
                dto.OwnerName = owner.FullName;
                dto.OwnerRoleTitle = DevTeamLabels.Role(owner.Role);
            }
        }

        var counts = await _db.DevTasks.AsNoTracking()
            .Where(t => t.ModuleId == m.Id)
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        int Count(DevTaskStatus st) => counts.FirstOrDefault(c => c.Status == st)?.Count ?? 0;
        dto.InProgress = Count(DevTaskStatus.InProgress);
        dto.Blocked = Count(DevTaskStatus.Blocked);
        dto.Backlog = Count(DevTaskStatus.Backlog);
        dto.OpenTasks = dto.InProgress + dto.Blocked + dto.Backlog + Count(DevTaskStatus.InReview);
        return dto;
    }

    private static DevModuleDto ToDto(DevModule m) => new()
    {
        Id = m.Id,
        Key = m.Key,
        Title = m.Title,
        OwnerId = m.OwnerId,
        Icon = m.Icon,
        ColorHex = m.ColorHex,
        SortOrder = m.SortOrder,
        IsActive = m.IsActive,
        RepoPaths = m.RepoPaths,
        ServiceLines = m.ServiceLines,
        PageCount = m.PageCount,
        Note = m.Note,
        CreatedAt = m.CreatedAt
    };

    private static DevTaskDto ToDto(DevTask t) => new()
    {
        Id = t.Id,
        Number = t.Number,
        Title = t.Title,
        Description = t.Description,
        ModuleId = t.ModuleId,
        AssigneeId = t.AssigneeId,
        Status = t.Status,
        Priority = t.Priority,
        Size = t.Size,
        BranchName = t.BranchName,
        PullRequestUrl = t.PullRequestUrl,
        AgentAssisted = t.AgentAssisted,
        StartedAt = t.StartedAt,
        DueDate = t.DueDate,
        CompletedAt = t.CompletedAt,
        CreatedBy = t.CreatedBy,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt
    };
}
