using System.Globalization;
using System.Data;
using Inventory.Api.Services.Invoicing;
using Db = Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Inventory.Api.Services;

/// <summary>
/// پیاده‌سازی منطق اصلی برنامه: کالا، انبار، طرف حساب، خرید/فروش، کاردکس، نقطه سفارش و داشبورد.
/// </summary>
public class InventoryService : IInventoryService
{
    private readonly Db.AppDbContext _db;
    private readonly ILogger<InventoryService> _log;

    public InventoryService(Db.AppDbContext db, ILogger<InventoryService> log, IConfiguration? configuration = null)
    {
        _db = db;
        _log = log;
    }

    // =============================== تنظیمات ===============================

    public async Task<AppSettings> GetSettingsAsync()
    {
        var s = await _db.AppSettings.FirstOrDefaultAsync();
        return s is null
            ? new AppSettings()
            : new AppSettings { CostingMethod = s.CostingMethod, AllowNegativeStock = s.AllowNegativeStock, ItServerUrl = s.ItServerUrl, ItCompanyName = s.ItCompanyName, BaleBotToken = s.BaleBotToken, BaleSafirApiAccessKey = s.BaleSafirApiAccessKey, BaleSafirBotId = s.BaleSafirBotId, EitaaToken = s.EitaaToken, MessengerSenderNumber = s.MessengerSenderNumber, NotifyBannerMs = s.NotifyBannerMs, NotifyToastMs = s.NotifyToastMs };
    }

    public async Task<AppSettings> SaveSettingsAsync(AppSettings dto)
    {
        var method = (dto.CostingMethod ?? "Average").Trim();
        if (method is not ("Average" or "FIFO" or "LIFO"))
            throw new InvalidOperationException("روش قیمت‌گذاری باید یکی از مقادیر Average، FIFO یا LIFO باشد.");

        var s = await _db.AppSettings.FirstOrDefaultAsync();
        if (s is null)
        {
            s = new Db.AppSetting();
            _db.AppSettings.Add(s);
        }
        s.CostingMethod = method;
        s.AllowNegativeStock = dto.AllowNegativeStock;
        s.ItServerUrl = string.IsNullOrWhiteSpace(dto.ItServerUrl) ? null : dto.ItServerUrl.Trim().TrimEnd('/');
        s.ItCompanyName = string.IsNullOrWhiteSpace(dto.ItCompanyName) ? null : dto.ItCompanyName.Trim();
        s.BaleBotToken = string.IsNullOrWhiteSpace(dto.BaleBotToken) ? null : dto.BaleBotToken.Trim();
        s.BaleSafirApiAccessKey = string.IsNullOrWhiteSpace(dto.BaleSafirApiAccessKey) ? null : dto.BaleSafirApiAccessKey.Trim();
        // شناسهٔ بازو: اگر مدیر آن را وارد نکرده باشد، از عدد ابتدای توکن ربات استخراج می‌شود
        // (قالب توکن بله: 123456789:abc…) — چون سرویس سفیر همین شناسه را می‌خواهد.
        s.BaleSafirBotId = dto.BaleSafirBotId is > 0
            ? dto.BaleSafirBotId
            : ExtractBotId(s.BaleBotToken);
        s.EitaaToken = string.IsNullOrWhiteSpace(dto.EitaaToken) ? null : dto.EitaaToken.Trim();
        s.MessengerSenderNumber = string.IsNullOrWhiteSpace(dto.MessengerSenderNumber) ? "09111189771" : dto.MessengerSenderNumber.Trim();
        // مدت نمایش اعلان‌ها و پیام‌ها (سراسری): ۰ = تا بستن دستی، در غیر این‌صورت بین ۵ ثانیه تا ۱ ساعت؛ مقدار نامعتبر → پیش‌فرض
        s.NotifyBannerMs = SanitizeNotifyMs(dto.NotifyBannerMs, 120_000);
        s.NotifyToastMs = SanitizeNotifyMs(dto.NotifyToastMs, 20_000);
        await _db.SaveChangesAsync();
        return await GetSettingsAsync();
    }

    /// <summary>اعتبارسنجی مدت نمایش (میلی‌ثانیه): ۰ = تا بستن دستی؛ در غیر این‌صورت بین ۵۰۰۰ تا ۳۶۰۰۰۰۰؛ خارج از بازه → مقدار پیش‌فرض.</summary>
    private static int SanitizeNotifyMs(int ms, int fallback)
        => ms == 0 ? 0 : (ms >= 5_000 && ms <= 3_600_000 ? ms : fallback);

    /// <summary>
    /// استخراج شناسهٔ عددی بازو از توکن ربات بله (قالب «123456789:abc…»).
    /// سرویس سفیر همین عدد را به‌عنوان <c>bot_id</c> می‌خواهد.
    /// </summary>
    internal static int? ExtractBotId(string? botToken)
    {
        if (string.IsNullOrWhiteSpace(botToken)) return null;
        var token = botToken.Trim();
        var colon = token.IndexOf(':');
        var head = colon > 0 ? token[..colon] : token;
        return int.TryParse(head, out var id) && id > 0 ? id : null;
    }

    // =============================== معرف (بازاریاب) ===============================

    public async Task<List<Referrer>> GetReferrersAsync(bool activeOnly = false, Paging.Request? pagination = null, string? search = null)
    {
        var q = _db.Referrers.AsQueryable();
        if (activeOnly) q = q.Where(r => r.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(r => r.Name.Contains(term));
        }
        var list = await q.OrderBy(r => r.Name).ThenBy(x => x.Id).ToPageListAsync(pagination);

        var counts = await _db.Transactions
            .Where(t => t.ReferrerId != null && list.Select(r => r.Id).Contains(t.ReferrerId.Value))
            .GroupBy(t => t.ReferrerId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToListAsync();

        return list.Select(r => new Referrer
        {
            Id = r.Id,
            Name = r.Name,
            CompanyName = r.CompanyName,
            Phone = r.Phone,
            GoodsCommissionPercent = r.GoodsCommissionPercent,
            ServiceCommissionPercent = r.ServiceCommissionPercent,
            CardNumber = r.CardNumber,
            Iban = r.Iban,
            CanViewProducts = r.CanViewProducts,
                MaxDiscountPercent = r.MaxDiscountPercent,
            IsActive = r.IsActive,
            CreatedAt = r.CreatedAt,
            OrderCount = counts.FirstOrDefault(x => x.Id == r.Id)?.Count ?? 0
        }).ToList();
    }

    public async Task<Referrer> SaveReferrerAsync(Referrer dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("نام معرف را وارد کنید.");
        if (dto.GoodsCommissionPercent is < 0 or > 100 || dto.ServiceCommissionPercent is < 0 or > 100)
            throw new InvalidOperationException("درصد پورسانت باید بین ۰ و ۱۰۰ باشد.");

        var card = NullIfEmpty(dto.CardNumber)?.Replace(" ", "").Replace("-", "");
        if (card is not null && (card.Length != 16 || !card.All(char.IsDigit)))
            throw new InvalidOperationException("شماره کارت باید ۱۶ رقم باشد.");
        dto.CardNumber = card;

        var iban = NullIfEmpty(dto.Iban)?.Replace(" ", "").ToUpperInvariant();
        if (iban is not null)
        {
            if (iban.StartsWith("IR")) iban = iban[2..];
            if (iban.Length != 24 || !iban.All(char.IsDigit))
                throw new InvalidOperationException("شماره شبا باید ۲۴ رقم (بدون IR) باشد.");
        }
        dto.Iban = iban;

        var name = dto.Name.Trim();
        var dup = await _db.Referrers.AnyAsync(r => r.Name == name && r.Id != dto.Id);
        if (dup) throw new InvalidOperationException("معرفی با این نام از قبل وجود دارد.");

        Db.Referrer entity;
        if (dto.Id == 0)
        {
            entity = new Db.Referrer { CreatedAt = DateTime.Now };
            _db.Referrers.Add(entity);
        }
        else
        {
            entity = await _db.Referrers.FindAsync(dto.Id)
                ?? throw new InvalidOperationException("معرف یافت نشد.");
        }

        entity.Name = name;
        entity.CompanyName = NullIfEmpty(dto.CompanyName);
        entity.Phone = NullIfEmpty(dto.Phone);
        entity.GoodsCommissionPercent = dto.GoodsCommissionPercent;
        entity.ServiceCommissionPercent = dto.ServiceCommissionPercent;
        entity.CardNumber = NullIfEmpty(dto.CardNumber);
        entity.Iban = NullIfEmpty(dto.Iban);
        entity.CanViewProducts = dto.CanViewProducts;
        // سقف تخفیف مجاز ویزیتور در سفارش میدانی (۰ = بدون سقف)
        entity.MaxDiscountPercent = Math.Clamp(dto.MaxDiscountPercent, 0, 100);
        entity.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();

        dto.Id = entity.Id;
        dto.CreatedAt = entity.CreatedAt;
        return dto;
    }

    public async Task DeleteReferrerAsync(int id)
    {
        var r = await _db.Referrers.FindAsync(id);
        if (r is null) return;
        var used = await _db.Transactions.AnyAsync(t => t.ReferrerId == id);
        if (used) throw new InvalidOperationException("این معرف دارای اسناد فروش است و قابل حذف نیست؛ می‌توانید آن را غیرفعال کنید.");
        var hasPayments = await _db.ReferrerPayments.AnyAsync(p => p.ReferrerId == id);
        if (hasPayments) throw new InvalidOperationException("این معرف دارای سند پرداخت است و قابل حذف نیست؛ می‌توانید آن را غیرفعال کنید.");
        _db.Referrers.Remove(r);
        await _db.SaveChangesAsync();
    }

    // =============================== کیف پول و پرداخت معرف ===============================

    /// <summary>جمع پورسانت یک معرف روی همه اسناد فروش او.</summary>
    private async Task<decimal> ComputeReferrerCommissionAsync(int referrerId)
    {
        var txns = await _db.Transactions.Include(t => t.Lines)
            .Where(t => t.ReferrerId == referrerId && t.Type == TransactionType.Sale)
            .ToListAsync();

        var saleIds = txns.Select(t => t.Id).ToList();
        var charges = await _db.SaleChequeCharges.AsNoTracking().Where(c => saleIds.Contains(c.TransactionId))
            .ToDictionaryAsync(c => c.TransactionId, c => c.Amount);
        decimal total = 0;
        foreach (var t in txns)
        {
            var order = await ToOrderDtoAsync(t, charges.GetValueOrDefault(t.Id)); // شامل محاسبه سود و پورسانت
            // Never turn unknown rates into zero or treat a newly recorded sale's
            // estimated goods cost as a definite, payable wallet liability. Old
            // documents retain their prior estimated wallet behavior.
            if (!order.CommissionAmount.HasValue || (order.CommissionRatesAreHistorical && order.HasEstimatedSaleCost))
                throw new InvalidOperationException("پورسانت قطعی یکی از فروش‌های معرف قابل محاسبه نیست؛ نرخ معرف و بهای ثبت‌شده کالا را بررسی کنید.");
            total += order.CommissionAmount.Value;
        }
        return total;
    }

    /// <summary>نگاشت سبک معرف به DTO کیف پول (بدون مبالغ).</summary>
    private static Referrer MapWallet(Db.Referrer r, int orderCount = 0) => new()
    {
        Id = r.Id,
        Name = r.Name,
        CompanyName = r.CompanyName,
        Phone = r.Phone,
        GoodsCommissionPercent = r.GoodsCommissionPercent,
        ServiceCommissionPercent = r.ServiceCommissionPercent,
        CardNumber = r.CardNumber,
        Iban = r.Iban,
        CanViewProducts = r.CanViewProducts,
                MaxDiscountPercent = r.MaxDiscountPercent,
        IsActive = r.IsActive,
        CreatedAt = r.CreatedAt,
        OrderCount = orderCount
    };

    public async Task<Referrer?> GetReferrerWalletAsync(int referrerId)
    {
        var referrer = await _db.Referrers.AsNoTracking().FirstOrDefaultAsync(r => r.Id == referrerId);
        if (referrer is null) return null;
        var count = await _db.Transactions.CountAsync(t => t.ReferrerId == referrerId);
        // Sum in memory, as in the list implementation, for SQLite decimal support.
        var payments = await _db.ReferrerPayments.AsNoTracking().Where(p => p.ReferrerId == referrerId).ToListAsync();
        var dto = MapWallet(referrer, count);
        dto.TotalCommission = await ComputeReferrerCommissionAsync(referrerId);
        dto.TotalPaid = payments.Sum(p => p.Amount);
        dto.WalletBalance = dto.TotalCommission - dto.TotalPaid;
        return dto;
    }

    public async Task<List<Referrer>> GetReferrerWalletsAsync(string? search, string? sortBy, bool desc,
        Paging.Request? pagination = null)
    {
        // فیلتر جست‌وجو در SQL.
        var q = _db.Referrers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(r => r.Name.Contains(s) || (r.CompanyName != null && r.CompanyName.Contains(s)));
        }

        var mode = sortBy?.ToLowerInvariant();
        if (mode != "commission" && mode != "paid" && mode != "balance") mode = "name";

        if (mode is "commission" or "balance")
        {
            // کلید این دو مرتب‌سازی (پورسانت/مانده) به محاسبهٔ پورسانت روی زنجیرهٔ بهای تمام‌شدهٔ
            // FIFO/LIFO/میانگین وابسته است و در SQL قابل بیان نیست؛ بنابراین فقط برای این دو حالت،
            // مبالغ همهٔ معرف‌ها محاسبه و در حافظه مرتب می‌شود و سپس صفحهٔ درخواستی برش می‌خورد.
            // راه‌حل بلندمدتِ Database-level: ذخیرهٔ پورسانت روی سند فروش هنگام ثبت است.
            var all = await q.OrderBy(r => r.Name).ThenBy(r => r.Id).ToListAsync();
            var allIds = all.Select(r => r.Id).ToList();
            var orderCounts = allIds.Count == 0
                ? new Dictionary<int, int>()
                : await _db.Transactions
                    .Where(t => t.ReferrerId != null && allIds.Contains(t.ReferrerId.Value))
                    .GroupBy(t => t.ReferrerId!.Value)
                    .Select(g => new { Id = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.Id, x => x.Count);
            var paidMap = (await _db.ReferrerPayments.AsNoTracking().ToListAsync())
                .GroupBy(p => p.ReferrerId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

            var walletList = new List<Referrer>();
            foreach (var r in all)
            {
                var dto = MapWallet(r, orderCounts.TryGetValue(r.Id, out var oc) ? oc : 0);
                dto.TotalCommission = await ComputeReferrerCommissionAsync(r.Id);
                dto.TotalPaid = paidMap.TryGetValue(r.Id, out var paid) ? paid : 0;
                dto.WalletBalance = dto.TotalCommission - dto.TotalPaid;
                walletList.Add(dto);
            }

            var sorted = mode == "commission"
                ? (desc ? walletList.OrderByDescending(r => r.TotalCommission) : walletList.OrderBy(r => r.TotalCommission))
                : (desc ? walletList.OrderByDescending(r => r.WalletBalance) : walletList.OrderBy(r => r.WalletBalance));
            var list = sorted.ThenBy(r => r.Name).ThenBy(r => r.Id).ToList();
            if (pagination is { IsPaged: true })
            {
                pagination.SetTotal(list.Count);
                if (pagination.Skip > 0) list = list.Skip(pagination.Skip).ToList();
                if (pagination.Take is > 0) list = list.Take(pagination.Take.Value).ToList();
            }
            return list;
        }

        // name/paid: فیلتر → ترتیب → شمارش → صفحه در SQL؛ مبالغ فقط برای رکوردهای صفحه محاسبه می‌شود.
        // مرتب‌سازی paid روی SUM پرداختی‌ها در SQL (به‌خاطر نبود Sum دسیمال در SQLite با معادل double).
        IOrderedQueryable<Db.Referrer> ordered;
        if (mode == "paid")
        {
            ordered = _db.Database.IsSqlite()
                ? (desc
                    ? q.OrderByDescending(r => _db.ReferrerPayments.Where(p => p.ReferrerId == r.Id).Sum(p => (double?)p.Amount) ?? 0)
                    : q.OrderBy(r => _db.ReferrerPayments.Where(p => p.ReferrerId == r.Id).Sum(p => (double?)p.Amount) ?? 0))
                : (desc
                    ? q.OrderByDescending(r => _db.ReferrerPayments.Where(p => p.ReferrerId == r.Id).Sum(p => (decimal?)p.Amount) ?? 0m)
                    : q.OrderBy(r => _db.ReferrerPayments.Where(p => p.ReferrerId == r.Id).Sum(p => (decimal?)p.Amount) ?? 0m));
        }
        else
        {
            ordered = desc ? q.OrderByDescending(r => r.Name) : q.OrderBy(r => r.Name);
        }

        var page = await ordered.ThenBy(r => r.Name).ThenBy(r => r.Id).ToPageListAsync(pagination);
        var pageIds = page.Select(r => r.Id).ToList();
        var pageOrderCounts = pageIds.Count == 0
            ? new Dictionary<int, int>()
            : await _db.Transactions
                .Where(t => t.ReferrerId != null && pageIds.Contains(t.ReferrerId.Value))
                .GroupBy(t => t.ReferrerId!.Value)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.Count);
        var pagePaid = pageIds.Count == 0
            ? new List<Db.ReferrerPayment>()
            : await _db.ReferrerPayments.AsNoTracking().Where(p => pageIds.Contains(p.ReferrerId)).ToListAsync();
        var pagePaidMap = pagePaid.GroupBy(p => p.ReferrerId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        var result = new List<Referrer>();
        foreach (var r in page)
        {
            var dto = MapWallet(r, pageOrderCounts.TryGetValue(r.Id, out var pc) ? pc : 0);
            dto.TotalCommission = await ComputeReferrerCommissionAsync(r.Id);
            dto.TotalPaid = pagePaidMap.TryGetValue(r.Id, out var paid) ? paid : 0;
            dto.WalletBalance = dto.TotalCommission - dto.TotalPaid;
            result.Add(dto);
        }
        return result;
    }

    public async Task<List<ReferrerPayment>> GetReferrerPaymentsAsync(int? referrerId, Paging.Request? pagination = null)
    {
        var q = _db.ReferrerPayments.AsQueryable();
        if (referrerId is > 0) q = q.Where(p => p.ReferrerId == referrerId);
        var list = await q.OrderByDescending(p => p.Date).ThenByDescending(p => p.Id).ToPageListAsync(pagination);

        var names = await _db.Referrers.ToDictionaryAsync(r => r.Id, r => r.Name);
        return list.Select(p => new ReferrerPayment
        {
            Id = p.Id,
            ReferrerId = p.ReferrerId,
            ReferrerName = names.TryGetValue(p.ReferrerId, out var n) ? n : "",
            Number = p.Number,
            Amount = p.Amount,
            Date = p.Date,
            Description = p.Description,
            CreatedAt = p.CreatedAt
        }).ToList();
    }

    public async Task<ReferrerPayment> AddReferrerPaymentAsync(ReferrerPayment dto)
    {
        if (dto.ReferrerId <= 0) throw new InvalidOperationException("معرف را انتخاب کنید.");
        if (dto.Amount <= 0) throw new InvalidOperationException("مبلغ پرداخت باید بزرگ‌تر از صفر باشد.");
        _ = await _db.Referrers.FindAsync(dto.ReferrerId)
            ?? throw new InvalidOperationException("معرف یافت نشد.");

        // شماره سند پرداخت: PY-0001
        var count = await _db.ReferrerPayments.CountAsync();
        string number;
        do { number = $"PY-{++count:0000}"; }
        while (await _db.ReferrerPayments.AnyAsync(p => p.Number == number));

        var entity = new Db.ReferrerPayment
        {
            ReferrerId = dto.ReferrerId,
            Number = number,
            Amount = dto.Amount,
            Date = dto.Date == default ? DateTime.Now : dto.Date,
            Description = NullIfEmpty(dto.Description),
            CreatedAt = DateTime.Now
        };
        _db.ReferrerPayments.Add(entity);
        await _db.SaveChangesAsync();

        dto.Id = entity.Id;
        dto.Number = entity.Number;
        dto.Date = entity.Date;
        dto.CreatedAt = entity.CreatedAt;
        return dto;
    }

    public async Task DeleteReferrerPaymentAsync(int id)
    {
        var p = await _db.ReferrerPayments.FindAsync(id);
        if (p is null) return;
        _db.ReferrerPayments.Remove(p);
        await _db.SaveChangesAsync();
    }

    // =============================== محاسبه بهای تمام‌شده (Average / FIFO / LIFO) ===============================

    /// <summary>
    /// محاسبه بهای تمام‌شده واحد برای هر سطر فروش یک کالا در یک انبار، طبق روش قیمت‌گذاری.
    /// خروجی: نگاشت شناسه سطر فروش ← بهای تمام‌شده واحد.
    /// </summary>
    private static Dictionary<int, decimal> ComputeSaleCosts(
        List<Db.Transaction> orderedTxns, int productId, string method, decimal defaultCost)
    {
        var result = new Dictionary<int, decimal>();

        // لایه‌های موجودی: (مقدار باقی‌مانده، قیمت واحد)
        var layers = new List<(decimal Qty, decimal Cost)>();
        decimal avgQty = 0, avgCost = 0;

        foreach (var t in orderedTxns)
        {
            foreach (var l in t.Lines.Where(l => l.ProductId == productId).OrderBy(l => l.Id))
            {
                switch (t.Type)
                {
                    case TransactionType.Purchase:
                        layers.Add((l.Quantity, l.Price));
                        var nq = avgQty + l.Quantity;
                        if (nq != 0) avgCost = (avgQty * avgCost + l.Quantity * l.Price) / nq;
                        avgQty = nq;
                        break;

                    case TransactionType.SaleReturn:
                        if (!l.UnitCostSnapshot.HasValue || l.UnitCostSnapshot.Value < 0)
                            throw new InvalidOperationException("بهای ثبت‌شده کالای برگشتی معتبر نیست.");
                        var returnCost = l.UnitCostSnapshot.Value;
                        layers.Add((l.Quantity, returnCost));
                        var returnQty = avgQty + l.Quantity;
                        if (returnQty != 0) avgCost = (avgQty * avgCost + l.Quantity * returnCost) / returnQty;
                        avgQty = returnQty;
                        break;

                    case TransactionType.Sale:
                    case TransactionType.WarrantyOut:
                        decimal unitCost;
                        if (method == "FIFO" || method == "LIFO")
                        {
                            decimal remaining = l.Quantity, costSum = 0, taken = 0;
                            while (remaining > 0 && layers.Count > 0)
                            {
                                var idx = method == "FIFO" ? 0 : layers.Count - 1;
                                var layer = layers[idx];
                                var take = Math.Min(layer.Qty, remaining);
                                costSum += take * layer.Cost;
                                taken += take;
                                remaining -= take;
                                if (layer.Qty - take <= 0) layers.RemoveAt(idx);
                                else layers[idx] = (layer.Qty - take, layer.Cost);
                            }
                            // اگر لایه کافی نبود، باقی‌مانده با آخرین قیمت معلوم/پیش‌فرض
                            if (remaining > 0)
                            {
                                var fallback = taken > 0 ? costSum / taken : (avgCost > 0 ? avgCost : defaultCost);
                                costSum += remaining * fallback;
                                taken += remaining;
                            }
                            unitCost = taken > 0 ? costSum / taken : defaultCost;
                        }
                        else // Average
                        {
                            unitCost = avgQty > 0 || avgCost > 0 ? avgCost : defaultCost;
                        }

                        avgQty -= l.Quantity;
                        result[l.Id] = unitCost;
                        break;

                    default: // Adjustment / Initial — پایه جدید موجودی
                        layers.Clear();
                        var baseCost = l.Price > 0 ? l.Price : (avgCost > 0 ? avgCost : defaultCost);
                        if (l.Quantity > 0) layers.Add((l.Quantity, baseCost));
                        avgQty = l.Quantity;
                        if (avgCost == 0) avgCost = baseCost;
                        break;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// بهای فروش دارای ردیف مالی را پیش از ثبت سند و در همان تراکنش نگه می‌دارد.
    /// برای برگشت آتی نباید قیمت فروش را به جای بهای کالای خارج‌شده وارد انبار کرد.
    /// </summary>
    private async Task FreezeChargedSaleCostsAsync(Db.Transaction sale, Dictionary<int, Db.Product> products, string costingMethod)
    {
        if (_db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("ثبت بهای فروش باید داخل تراکنش فاکتور انجام شود.");
        foreach (var group in sale.Lines.GroupBy(l => l.ProductId))
        {
            if (!products.TryGetValue(group.Key, out var product))
                throw new InvalidOperationException("کالای فاکتور برای تعیین بهای تمام‌شده یافت نشد.");
            // Services do not create stock. Internal repair/warranty rows already carry
            // their actual cost and must not be overwritten by the stock estimate.
            if (product.IsService) continue;
            var unpricedLines = group.Where(l => !l.UnitCostSnapshot.HasValue).ToList();
            if (unpricedLines.Count == 0) continue;
            // The legacy sales cost calculator does not merge the separate warehouse ledger.
            // Never freeze a guessed cost when the product uses that ledger; an eventual return
            // must reject these rows until a reliable unified valuation is available.
            if (await _db.InvStocks.AnyAsync(s => s.ProductId == group.Key && s.WarehouseId == sale.WarehouseId) ||
                await _db.InvLedger.AnyAsync(e => e.ProductId == group.Key && e.WarehouseId == sale.WarehouseId))
            {
                foreach (var line in unpricedLines) line.UnitCostSnapshot = null;
                continue;
            }
            var txns = await _db.Transactions.Include(t => t.Lines)
                .Where(t => t.WarehouseId == sale.WarehouseId && t.Lines.Any(l => l.ProductId == group.Key))
                .OrderBy(t => t.Date).ThenBy(t => t.Id).ToListAsync();
            var saleIndex = txns.FindIndex(t => t.Id == sale.Id);
            if (saleIndex < 0) throw new InvalidOperationException("فاکتور برای تعیین بهای تمام‌شده یافت نشد.");
            var throughSale = txns.Take(saleIndex + 1).ToList();
            var reliableLines = ReliableSaleCostLines(throughSale, sale.Id, group.Key);
            var costs = reliableLines.Count == 0 ? new Dictionary<int, decimal>()
                : ComputeSaleCosts(throughSale, group.Key, costingMethod, product.PurchasePrice);
            foreach (var line in unpricedLines)
            {
                // When prior quantities or adjustments cannot substantiate the
                // cost, retain an explicitly estimated result rather than freezing
                // a fallback purchase price as historical fact.
                if (!reliableLines.Contains(line.Id)) { line.UnitCostSnapshot = null; continue; }
                if (!costs.TryGetValue(line.Id, out var cost) || cost < 0 || cost > 9999999999999999.99m)
                    throw new InvalidOperationException("بهای تمام‌شده کالا قابل تعیین نیست؛ ثبت متوقف شد.");
                line.UnitCostSnapshot = decimal.Round(cost, 2);
            }
        }
        await _db.SaveChangesAsync();
    }

    // Conservative confidence check for the same stock chronology consumed by
    // ComputeSaleCosts. An unexplained opening/adjustment or a historical
    // shortfall makes later values estimates, even if a fallback price exists.
    private static HashSet<int> ReliableSaleCostLines(List<Db.Transaction> ordered, int saleId, int productId)
    {
        var reliable = true;
        var available = 0m;
        var result = new HashSet<int>();
        foreach (var txn in ordered)
        {
            foreach (var line in txn.Lines.Where(l => l.ProductId == productId).OrderBy(l => l.Id))
            {
                if (line.Quantity <= 0) reliable = false;
                switch (txn.Type)
                {
                    case TransactionType.Purchase:
                        if (line.Price < 0) reliable = false;
                        available += line.Quantity;
                        break;
                    case TransactionType.SaleReturn:
                        if (!line.UnitCostSnapshot.HasValue || line.UnitCostSnapshot.Value < 0) reliable = false;
                        available += line.Quantity;
                        break;
                    case TransactionType.Sale:
                    case TransactionType.WarrantyOut:
                        if (line.Quantity > available) reliable = false;
                        if (txn.Id == saleId && reliable) result.Add(line.Id);
                        available -= line.Quantity;
                        break;
                    default:
                        reliable = false;
                        available = line.Quantity;
                        break;
                }
            }
        }
        return result;
    }

    private async Task SnapshotReferrerRatesAsync(Db.Transaction sale)
    {
        sale.ReferrerGoodsPercentSnapshot = null;
        sale.ReferrerServicePercentSnapshot = null;
        if (sale.Type != TransactionType.Sale || sale.ReferrerId is not > 0) return;
        // Query filters enforce the active company. Never infer an old rate from a
        // different company or fill missing historical snapshots automatically.
        var referrer = await _db.Referrers.AsNoTracking().FirstOrDefaultAsync(r => r.Id == sale.ReferrerId.Value)
            ?? throw new InvalidOperationException("معرف مشتری در شرکت جاری یافت نشد؛ مشخصات مشتری را اصلاح کنید.");
        if (referrer.GoodsCommissionPercent is < 0 or > 100 || referrer.ServiceCommissionPercent is < 0 or > 100 ||
            decimal.Round(referrer.GoodsCommissionPercent, 6) != referrer.GoodsCommissionPercent ||
            decimal.Round(referrer.ServiceCommissionPercent, 6) != referrer.ServiceCommissionPercent)
            throw new InvalidOperationException("درصد پورسانت معرف معتبر نیست؛ نرخ کالا و خدمات باید بین صفر و صد و حداکثر شش رقم اعشار باشد.");
        sale.ReferrerGoodsPercentSnapshot = referrer.GoodsCommissionPercent;
        sale.ReferrerServicePercentSnapshot = referrer.ServiceCommissionPercent;
    }

    /// <summary>محاسبه سود سطرهای فروش یک سند + پورسانت معرف.</summary>
    private async Task EnrichSaleProfitAsync(Order order)
    {
        if (order.Type != TransactionType.Sale) return;

        var settings = await GetSettingsAsync();
        var productIds = order.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        // واکشی فاکتور در دیتابیس برای دسترسی به Snapshotهای ثبت‌شده روی سطرهای TransactionLine
        var dbTxn = await _db.Transactions.Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == order.Id);
        var snapshotMap = dbTxn?.Lines.ToDictionary(l => l.Id, l => l.UnitCostSnapshot)
                          ?? new Dictionary<int, decimal?>();
        order.HasEstimatedSaleCost = order.Lines.Any(l => !l.IsService &&
            (!snapshotMap.TryGetValue(l.Id, out var cost) || !cost.HasValue));
        if (await _db.SaleChequeCancellations.AsNoTracking().AnyAsync(c => c.SaleTransactionId == order.Id))
        {
            foreach (var l in order.Lines)
            {
                l.ReturnedQuantity = l.Quantity;
                l.Profit = 0;
                l.Commission = 0;
            }
            order.TotalProfit = 0;
            order.CommissionAmount = 0;
            order.NetProfit = 0;
            order.HasEstimatedSaleCost = false;
            return;
        }
        // A missing product may have been a service or goods. Do not turn an
        // unresolvable cost/category into a false zero profit or commission.
        if (order.Lines.Any(l => !products.ContainsKey(l.ProductId)))
        {
            order.HasEstimatedSaleCost = true;
            order.TotalProfit = null;
            order.CommissionAmount = null;
            order.NetProfit = null;
            return;
        }
        var returnedLines = await _db.SaleChequeReturnLines.AsNoTracking()
            .Where(r => r.SaleChequeReturn.SaleTransactionId == order.Id)
            .Select(r => new { r.OriginalLineId, r.Quantity, r.GoodsAmount })
            .ToListAsync();
        var retQtyMap = returnedLines.GroupBy(r => r.OriginalLineId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        var retGoodsMap = returnedLines.GroupBy(r => r.OriginalLineId).ToDictionary(g => g.Key, g => g.Sum(x => x.GoodsAmount));

        foreach (var pid in productIds)
        {
            if (!products.TryGetValue(pid, out var product)) continue;
            var productLines = order.Lines.Where(x => x.ProductId == pid).ToList();

            if (product.IsService)
            {
                // خدمات / اجرت تعمیرات:
                // سود خالص = مبلغ کل سطر (پس از تخفیف) − (هزینه/دستمزد تعمیرکار × تعداد)
                foreach (var l in productLines)
                {
                    l.IsService = true;
                    l.ReturnedQuantity = retQtyMap.GetValueOrDefault(l.Id);
                    var netQty = Math.Max(0m, l.Quantity - l.ReturnedQuantity);
                    // بهای تمام‌شده/دستمزد اگر روی سطر نباشد از دیتابیس (UnitCostSnapshot) خوانده می‌شود
                    var unitCost = l.UnitCost ?? (snapshotMap.TryGetValue(l.Id, out var sn) && sn.HasValue ? sn.Value : 0m);
                    l.UnitCost = decimal.Round(unitCost, 2);
                    // سود دقیق با کسر تخفیف و کسر دستمزد تعمیرکار:
                    l.Profit = decimal.Round(l.AfterDiscount - retGoodsMap.GetValueOrDefault(l.Id) - (unitCost * netQty), 2);
                }
                continue;
            }

            foreach (var l in productLines)
            {
                l.ReturnedQuantity = retQtyMap.GetValueOrDefault(l.Id);
                if (!l.UnitCost.HasValue && snapshotMap.TryGetValue(l.Id, out var sn) && sn.HasValue)
                {
                    l.UnitCost = sn.Value;
                }
            }

            foreach (var l in productLines.Where(x => x.UnitCost.HasValue))
            {
                // بهای ذخیره‌شده روی خود سطر (مثل بهای قطعه تعمیری) بر روش عمومی اولویت دارد.
                // سود دقیق = مبلغ کل سطر (پس از تخفیف) − (بهای تمام‌شده × تعداد)
                var unitCost = l.UnitCost!.Value;
                var netQty = Math.Max(0m, l.Quantity - l.ReturnedQuantity);
                var netGoods = Math.Max(0m, l.AfterDiscount - retGoodsMap.GetValueOrDefault(l.Id));
                l.UnitCost = decimal.Round(unitCost, 2);
                l.Profit = decimal.Round(netGoods - (unitCost * netQty), 2);
            }

            var linesNeedingCost = productLines.Where(x => !x.UnitCost.HasValue).ToList();
            if (linesNeedingCost.Count == 0) continue;

            var txns = await _db.Transactions.Include(t => t.Lines)
                .Where(t => t.WarehouseId == order.WarehouseId && t.Lines.Any(l => l.ProductId == pid))
                .OrderBy(t => t.Date).ThenBy(t => t.Id)
                .ToListAsync();

            var costs = ComputeSaleCosts(txns, pid, settings.CostingMethod, product.PurchasePrice);

            foreach (var l in linesNeedingCost)
            {
                var unitCost = costs.TryGetValue(l.Id, out var c) ? c : product.PurchasePrice;
                var netQty = Math.Max(0m, l.Quantity - l.ReturnedQuantity);
                var netGoods = Math.Max(0m, l.AfterDiscount - retGoodsMap.GetValueOrDefault(l.Id));
                l.UnitCost = decimal.Round(unitCost, 2);
                l.Profit = decimal.Round(netGoods - (unitCost * netQty), 2);
            }
        }

        order.TotalProfit = order.Lines.Sum(l => l.Profit ?? 0);

        // پورسانت فقط از سود فروشگاه کم می‌شود، نه از مبلغ یا گردش مشتری.
        // برای فاکتورهای جدید نرخ در زمان ثبت نگهداری شده و پس از برگشت،
        // خدمات از مبلغ پس از تخفیفِ باقیمانده و کالا از سود مثبت محاسبه می‌شود.
        if (order.ReferrerId is > 0 && order.ReferrerGoodsCommissionPercent is decimal goodsRate &&
            order.ReferrerServiceCommissionPercent is decimal serviceRate)
        {
            foreach (var l in order.Lines)
            {
                var profit = l.Profit ?? 0m;
                var remainingAfterDiscount = Math.Max(0m, l.AfterDiscount - retGoodsMap.GetValueOrDefault(l.Id));
                l.Commission = l.IsService && !order.UsesLegacyCommissionPolicy
                    ? SaleReferrerCommission.Service(remainingAfterDiscount, serviceRate)
                    : SaleReferrerCommission.Goods(profit, l.IsService ? serviceRate : goodsRate);
            }
            order.CommissionAmount = order.Lines.Sum(l => l.Commission ?? 0m);
        }

        // نرخ تاریخی اسناد قدیمی بازسازی نمی‌شود؛ سیاست قبلی آن‌ها تغییر نمی‌کند.
        // اگر معرف موجود نباشد، مبلغ نامشخص را صفر قطعی وانمود نمی‌کنیم.
        order.NetProfit = order.ReferrerId is > 0 && !order.CommissionAmount.HasValue
            ? null : order.TotalProfit - (order.CommissionAmount ?? 0m);
    }

    /// <summary>بررسی منفی نشدن موجودی جفت‌های (انبار، کالا) — با توجه به تنظیمات.</summary>
    private async Task EnsureNoNegativeStockAsync(IEnumerable<(int Wh, int Pid)> pairs)
    {
        var settings = await GetSettingsAsync();
        if (settings.AllowNegativeStock) return;

        foreach (var (wh, pid) in pairs.Distinct())
        {
            var product = await _db.Products.FindAsync(pid);
            if (product is null || product.IsService) continue;
            var st = await _db.Stocks.FirstOrDefaultAsync(s => s.WarehouseId == wh && s.ProductId == pid);
            if (st is not null && st.Quantity < 0)
                throw new InvalidOperationException(
                    $"با این عملیات، موجودی «{product.Name}» منفی می‌شود ({st.Quantity:0.##})؛ عملیات انجام نشد. " +
                    "در صورت نیاز می‌توانید در تنظیمات، موجودی منفی را مجاز کنید.");
        }
    }

    // =============================== گروه کالا ===============================

    public async Task<List<ProductCategory>> GetCategoriesAsync(bool activeOnly = false, Paging.Request? pagination = null)
    {
        var scope = _db.ProductCategories.AsNoTracking().AsQueryable();
        if (activeOnly) scope = scope.Where(c => c.IsActive);
        IOrderedQueryable<Db.ProductCategory> Order(IQueryable<Db.ProductCategory> q) => q
            .OrderBy(c => c.Name).ThenBy(c => c.Id);

        // الگوی DatabaseTreePaging: شمارش و صفحه‌بندی ریشه‌ها در SQL و واکشی کامل
        // زیرشاخه‌های ریشه‌های منتخب؛ total تعداد ریشه‌هاست و ساختار درختی حفظ می‌شود.
        var cats = await Order(scope).ReadTreePageAsync(
            Order(scope.Where(c => c.ParentId == null || c.ParentId == 0 || !scope.Any(p => p.Id == c.ParentId))),
            ids => Order(scope.Where(c => c.ParentId != null && ids.Contains(c.ParentId.Value))),
            c => c.Id, pagination);

        // شمارش کالاهای هر گروه (بر اساس نام گروه ذخیره‌شده روی کالا) — فقط برای گره‌های صفحهٔ جاری
        var catNames = cats.Select(c => c.Name).Distinct().ToList();
        var counts = catNames.Count == 0
            ? new Dictionary<string, int>()
            : await _db.Products
                .Where(p => p.Category != null && catNames.Contains(p.Category))
                .GroupBy(p => p.Category!)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Name, x => x.Count);

        // ---------- چینش درختی: والدها اول، فرزندان زیر والد با محاسبه عمق ----------
        var byParent = cats.GroupBy(c => c.ParentId).ToDictionary(g => g.Key ?? 0, g => g.OrderBy(c => c.Name).ToList());
        var result = new List<ProductCategory>();

        void Walk(int parentId, int depth, string parentPath)
        {
            if (!byParent.TryGetValue(parentId, out var children)) return;
            foreach (var c in children)
            {
                var path = string.IsNullOrEmpty(parentPath) ? c.Name : $"{parentPath} ← {c.Name}";
                result.Add(new ProductCategory
                {
                    Id = c.Id,
                    Name = c.Name,
                    ParentId = c.ParentId,
                    Description = c.Description,
                    IsActive = c.IsActive,
                    CreatedAt = c.CreatedAt,
                    ProductCount = counts.TryGetValue(c.Name, out var pc) ? pc : 0,
                    Depth = depth,
                    FullPath = path
                });
                Walk(c.Id, depth + 1, path);
            }
        }

        Walk(0, 0, "");

        // گروه‌هایی که والدشان (به هر دلیل) در فهرست نیست، به انتها اضافه می‌شوند
        var seen = result.Select(r => r.Id).ToHashSet();
        foreach (var c in cats.Where(c => !seen.Contains(c.Id)))
            result.Add(new ProductCategory
            {
                Id = c.Id, Name = c.Name, ParentId = c.ParentId, Description = c.Description,
                IsActive = c.IsActive, CreatedAt = c.CreatedAt,
                ProductCount = counts.TryGetValue(c.Name, out var pc2) ? pc2 : 0,
                Depth = 0, FullPath = c.Name
            });

        return result;
    }

    public async Task<ProductCategory> SaveCategoryAsync(ProductCategory dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("نام گروه کالا را وارد کنید.");

        var name = dto.Name.Trim();
        var dup = await _db.ProductCategories.AnyAsync(c => c.Name == name && c.Id != dto.Id);
        if (dup) throw new InvalidOperationException("گروه کالایی با این نام از قبل وجود دارد.");

        // اعتبارسنجی والد: وجود داشته باشد و حلقه در درخت ایجاد نشود
        if (dto.ParentId is > 0)
        {
            if (dto.ParentId == dto.Id)
                throw new InvalidOperationException("یک گروه نمی‌تواند والد خودش باشد.");
            var parent = await _db.ProductCategories.FindAsync(dto.ParentId)
                ?? throw new InvalidOperationException("گروه والد یافت نشد.");

            // بررسی حلقه: والد انتخابی نباید از نوادگان همین گروه باشد
            var cursor = parent;
            while (cursor?.ParentId != null)
            {
                if (cursor.ParentId == dto.Id)
                    throw new InvalidOperationException("گروه والد نمی‌تواند زیرگروهِ خود این گروه باشد.");
                cursor = await _db.ProductCategories.FindAsync(cursor.ParentId);
            }
        }

        Db.ProductCategory entity;
        string? oldName = null;
        if (dto.Id == 0)
        {
            entity = new Db.ProductCategory { CreatedAt = DateTime.Now };
            _db.ProductCategories.Add(entity);
        }
        else
        {
            entity = await _db.ProductCategories.FindAsync(dto.Id)
                ?? throw new InvalidOperationException("گروه کالا یافت نشد.");
            oldName = entity.Name;
        }

        entity.Name = name;
        entity.ParentId = dto.ParentId is > 0 ? dto.ParentId : null;
        entity.Description = NullIfEmpty(dto.Description);
        entity.IsActive = dto.IsActive;

        // در صورت تغییر نام گروه، نام روی کالاهای مرتبط هم به‌روزرسانی می‌شود
        if (oldName != null && oldName != name)
        {
            var related = await _db.Products.Where(p => p.Category == oldName).ToListAsync();
            foreach (var p in related) p.Category = name;
        }

        await _db.SaveChangesAsync();

        var count = await _db.Products.CountAsync(p => p.Category == entity.Name);
        return new ProductCategory
        {
            Id = entity.Id,
            Name = entity.Name,
            ParentId = entity.ParentId,
            Description = entity.Description,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt,
            ProductCount = count
        };
    }

    public async Task DeleteCategoryAsync(int id)
    {
        var c = await _db.ProductCategories.FindAsync(id);
        if (c is null) return;
        var used = await _db.Products.AnyAsync(p => p.Category == c.Name);
        if (used) throw new InvalidOperationException("این گروه دارای کالا است و قابل حذف نیست؛ می‌توانید آن را غیرفعال کنید.");
        var hasChildren = await _db.ProductCategories.AnyAsync(x => x.ParentId == id);
        if (hasChildren) throw new InvalidOperationException("این گروه دارای زیرگروه است؛ ابتدا زیرگروه‌ها را حذف یا جابه‌جا کنید.");
        _db.ProductCategories.Remove(c);
        await _db.SaveChangesAsync();
    }

    // =============================== واحد شمارش ===============================

    public async Task<List<MeasureUnit>> GetUnitsAsync(bool activeOnly = false, Paging.Request? pagination = null, string? search = null)
    {
        var q = _db.MeasureUnits.AsQueryable();
        if (activeOnly) q = q.Where(u => u.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(u => u.Name.Contains(term));
        }
        var units = await q.OrderBy(u => u.Name).ThenBy(x => x.Id).ToPageListAsync(pagination);

        var counts = await _db.Products
            .GroupBy(p => new { p.CompanyId, p.Unit })
            .Select(g => new { g.Key.CompanyId, Name = g.Key.Unit, Count = g.Count() })
            .ToListAsync();

        return units.Select(u => new MeasureUnit
        {
            Id = u.Id,
            Name = u.Name,
            IsActive = u.IsActive,
            AllowsFractionalQuantity = u.AllowsFractionalQuantity,
            CreatedAt = u.CreatedAt,
            ProductCount = counts.FirstOrDefault(x => x.CompanyId == u.CompanyId && x.Name == u.Name)?.Count ?? 0
        }).ToList();
    }

    public async Task<MeasureUnit> SaveUnitAsync(MeasureUnit dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("نام واحد شمارش را وارد کنید.");

        var name = dto.Name.Trim();
        Db.MeasureUnit entity;
        string? oldName = null;
        if (dto.Id == 0)
        {
            var owner = _db.ActiveCompanyId is > 0 ? _db.ActiveCompanyId : _db.ResolveDefaultCompanyId();
            entity = new Db.MeasureUnit { CompanyId = owner, CreatedAt = DateTime.Now };
        }
        else
        {
            entity = await _db.MeasureUnits.FirstOrDefaultAsync(u => u.Id == dto.Id)
                ?? throw new InvalidOperationException("واحد شمارش یافت نشد.");
            oldName = entity.Name;
        }
        var dup = await _db.MeasureUnits.AnyAsync(u => u.CompanyId == entity.CompanyId && u.Name == name && u.Id != dto.Id);
        if (dup) throw new InvalidOperationException("واحد شمارشی با این نام از قبل وجود دارد.");
        if (dto.Id == 0) _db.MeasureUnits.Add(entity);

        entity.Name = name;
        entity.IsActive = dto.IsActive;
        // Old clients may omit the new field; never erase a saved decision.
        entity.AllowsFractionalQuantity = dto.AllowsFractionalQuantity ?? entity.AllowsFractionalQuantity
            ?? ShopQuantityRules.DefaultAllowsFraction(name);

        // در صورت تغییر نام واحد، روی کالاهای مرتبط هم اعمال می‌شود
        if (oldName != null && oldName != name)
        {
            var related = await _db.Products.Where(p => p.CompanyId == entity.CompanyId && p.Unit == oldName).ToListAsync();
            foreach (var p in related) p.Unit = name;
        }

        await _db.SaveChangesAsync();

        var count = await _db.Products.CountAsync(p => p.CompanyId == entity.CompanyId && p.Unit == entity.Name);
        return new MeasureUnit
        {
            Id = entity.Id, Name = entity.Name, IsActive = entity.IsActive,
            AllowsFractionalQuantity = entity.AllowsFractionalQuantity,
            CreatedAt = entity.CreatedAt, ProductCount = count
        };
    }

    public async Task DeleteUnitAsync(int id)
    {
        var u = await _db.MeasureUnits.FirstOrDefaultAsync(unit => unit.Id == id);
        if (u is null) return;
        var used = await _db.Products.AnyAsync(p => p.CompanyId == u.CompanyId && p.Unit == u.Name);
        if (used) throw new InvalidOperationException("این واحد روی کالاها استفاده شده و قابل حذف نیست؛ می‌توانید آن را غیرفعال کنید.");
        _db.MeasureUnits.Remove(u);
        await _db.SaveChangesAsync();
    }

    // =============================== ورود اکسل کالا ===============================

    /// <summary>ستون‌های فایل اکسل: کد کالا | نام کالا | گروه | واحد | قیمت خرید | قیمت فروش | نقطه سفارش | حداکثر موجودی | بارکد | توضیحات</summary>
    public async Task<ExcelImportResult> ImportProductsAsync(Stream excelStream, int? warehouseId = null)
    {
        var result = new ExcelImportResult();

        using var wb = new ClosedXML.Excel.XLWorkbook(excelStream);
        var ws = wb.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("فایل اکسل فاقد کاربرگ (Sheet) است.");

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        if (lastRow < 2)
            throw new InvalidOperationException("فایل اکسل خالی است؛ داده‌ها باید از سطر ۲ (بعد از عنوان) شروع شوند.");

        for (int r = 2; r <= lastRow; r++)
        {
            var row = ws.Row(r);
            if (row.IsEmpty()) continue;

            try
            {
                var code = row.Cell(1).GetString().Trim();
                var name = row.Cell(2).GetString().Trim();
                var category = row.Cell(3).GetString().Trim();
                var unit = row.Cell(4).GetString().Trim();
                var purchase = ReadWholePrice(row.Cell(5));
                var sale = ReadWholePrice(row.Cell(6));
                var reorder = ReadDecimal(row.Cell(7));
                var maxStock = ReadDecimal(row.Cell(8));
                var barcode = row.Cell(9).GetString().Trim();
                var desc = row.Cell(10).GetString().Trim();

                if (string.IsNullOrWhiteSpace(name))
                    throw new InvalidOperationException("نام کالا خالی است.");
                if (purchase < 0 || sale < 0 || reorder < 0 || maxStock < 0)
                    throw new InvalidOperationException("مقادیر عددی نمی‌توانند منفی باشند.");

                await SaveProductAsync(new Product
                {
                    Code = code,
                    Name = name,
                    Category = string.IsNullOrWhiteSpace(category) ? null : category,
                    Unit = string.IsNullOrWhiteSpace(unit) ? "عدد" : unit,
                    PurchasePrice = purchase,
                    SalePrice = sale,
                    ReorderPoint = reorder,
                    MaxStock = maxStock,
                    Barcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode,
                    Description = string.IsNullOrWhiteSpace(desc) ? null : desc,
                    WarehouseId = warehouseId is > 0 ? warehouseId : null,
                    IsActive = true
                });

                result.Imported++;
            }
            catch (Exception ex)
            {
                // A failed row may already have attached an Added product/unit; do
                // not let a later successful row accidentally persist it.
                _db.ChangeTracker.Clear();
                result.Failed++;
                result.Errors.Add($"سطر {r}: {ex.Message}");
            }
        }

        // SaveProductAsync creates each missing unit for that product's own company.
        return result;
    }

    private static decimal ReadWholePrice(ClosedXML.Excel.IXLCell cell)
    {
        if (cell.IsEmpty()) return 0m;
        decimal value;
        if (cell.DataType == ClosedXML.Excel.XLDataType.Number)
        {
            if (!cell.TryGetValue<decimal>(out value))
                throw new InvalidOperationException("قیمت عدد معتبری نیست.");
        }
        else
        {
            var raw = Fa.ToEn(cell.GetString()).Trim().Replace('\u066C', ',');
            // Text cells must be digits or properly grouped thousands. A comma
            // used as a decimal separator must never turn 100,50 into 10050.
            if (!System.Text.RegularExpressions.Regex.IsMatch(raw, @"^(?:[0-9]+|[0-9]{1,3}(?:,[0-9]{3})+)$") ||
                !decimal.TryParse(raw.Replace(",", ""), NumberStyles.None, CultureInfo.InvariantCulture, out value))
                throw new InvalidOperationException($"قیمت «{cell.GetString()}» باید به ریال صحیح و بدون اعشار باشد.");
        }
        ShopPricingService.ValidatePrice(value);
        return value;
    }

    private static decimal ReadDecimal(ClosedXML.Excel.IXLCell cell)
    {
        if (cell.IsEmpty()) return 0;
        if (cell.TryGetValue<decimal>(out var v)) return v;
        var s = Fa.ToEn(cell.GetString()).Replace(",", "").Trim();
        return decimal.TryParse(s, NumberStyles.Number,
            CultureInfo.InvariantCulture, out var d)
            ? d
            : throw new InvalidOperationException($"مقدار «{cell.GetString()}» عدد معتبری نیست.");
    }

    /// <summary>ساخت فایل اکسل نمونه برای ورود گروهی کالا (با دو سطر مثال).</summary>
    public byte[] BuildProductTemplate()
    {
        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("کالاها");
        ws.RightToLeft = true;

        string[] headers = { "کد کالا", "نام کالا", "گروه", "واحد", "قیمت خرید", "قیمت فروش", "نقطه سفارش", "حداکثر موجودی", "بارکد", "توضیحات" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#DDEBF7");
            c.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
        }

        // دو سطر نمونه
        ws.Cell(2, 1).Value = "";              ws.Cell(3, 1).Value = "P-9001";
        ws.Cell(2, 2).Value = "روغن ترمز DOT4"; ws.Cell(3, 2).Value = "کمک فنر جلو";
        ws.Cell(2, 3).Value = "روغن";           ws.Cell(3, 3).Value = "جلوبندی";
        ws.Cell(2, 4).Value = "لیتر";           ws.Cell(3, 4).Value = "عدد";
        ws.Cell(2, 5).Value = 450000;           ws.Cell(3, 5).Value = 3200000;
        ws.Cell(2, 6).Value = 590000;           ws.Cell(3, 6).Value = 4100000;
        ws.Cell(2, 7).Value = 10;               ws.Cell(3, 7).Value = 4;
        ws.Cell(2, 8).Value = 60;               ws.Cell(3, 8).Value = 20;
        ws.Cell(2, 9).Value = "6260001112223";  ws.Cell(3, 9).Value = "";
        ws.Cell(2, 10).Value = "سطر نمونه — قابل حذف"; ws.Cell(3, 10).Value = "سطر نمونه — قابل حذف";

        ws.Columns(1, 10).AdjustToContents();

        // راهنما در کاربرگ دوم
        var help = wb.Worksheets.Add("راهنما");
        help.RightToLeft = true;
        help.Cell(1, 1).Value = "راهنمای ورود کالا از اکسل";
        help.Cell(1, 1).Style.Font.Bold = true;
        help.Cell(3, 1).Value = "• داده‌ها را از سطر ۲ کاربرگ «کالاها» وارد کنید (سطر ۱ عنوان است).";
        help.Cell(4, 1).Value = "• «نام کالا» اجباری است؛ بقیه ستون‌ها اختیاری‌اند.";
        help.Cell(5, 1).Value = "• «کد کالا» خالی باشد، خودکار ساخته می‌شود؛ کد تکراری خطا می‌دهد.";
        help.Cell(6, 1).Value = "• گروه یا واحد جدید باشد، خودکار در اطلاعات پایه ساخته می‌شود.";
        help.Cell(7, 1).Value = "• دو سطر نمونه در کاربرگ «کالاها» را قبل از ورود نهایی حذف کنید.";
        help.Columns(1, 1).AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // =============================== کالا ===============================

    private async Task<Dictionary<(int? CompanyId, string Name), bool?>> ResolveUnitRulesAsync(IEnumerable<Db.Product> source)
    {
        var products = source.Where(p => !p.IsService).ToList();
        var names = products.Select(p => (p.Unit ?? "").Trim()).Where(n => n.Length > 0).Distinct().ToList();
        if (names.Count == 0) return new();
        var rows = await _db.MeasureUnits.AsNoTracking().Where(u => names.Contains(u.Name))
            .Select(u => new { u.CompanyId, u.Name, u.AllowsFractionalQuantity }).ToListAsync();
        var result = rows.GroupBy(u => (u.CompanyId, u.Name))
            .ToDictionary(g => g.Key, g => g.Count() == 1 ? g.First().AllowsFractionalQuantity : null);
        foreach (var product in products)
        {
            var key = (product.CompanyId, (product.Unit ?? "").Trim());
            // Missing built-in units have the same default as the startup seeder;
            // unknown/custom names remain unresolved. Never use another company's row.
            if (!result.ContainsKey(key)) result[key] = ShopQuantityRules.DefaultAllowsFraction(key.Item2);
        }
        return result;
    }

    private static bool? UnitRule(Db.Product product, Dictionary<(int? CompanyId, string Name), bool?> rules)
        => product.IsService ? true : rules.GetValueOrDefault((product.CompanyId, (product.Unit ?? "").Trim()));

    public async Task<PagedResult<Product>> GetProductsAsync(string? search, bool belowReorderOnly, int page, int pageSize, int? warehouseId = null)
    {
        var q = _db.Products.AsNoTracking().AsQueryable();

        // فیلتر انبار: کالاهای همان انبار + کالاهای بدون انبار اختصاصی + خدمات
        if (warehouseId is > 0)
            q = q.Where(p => p.IsService || p.WarehouseId == null || p.WarehouseId == warehouseId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(p => p.Name.Contains(s) || p.Code.Contains(s) || (p.Category != null && p.Category.Contains(s)) || (p.Barcode != null && p.Barcode.Contains(s)) || (p.TaxCode != null && p.TaxCode.Contains(s)));
        }

        // Filtering belongs to the same SQL query as COUNT and OFFSET/LIMIT.
        if (belowReorderOnly)
        {
            if (_db.Database.IsSqlite())
                q = q.Where(p => p.ReorderPoint > 0 &&
                    Math.Round(_db.Stocks.Where(s => s.ProductId == p.Id)
                        .Sum(s => (double?)s.Quantity) ?? 0, 3) <= (double)p.ReorderPoint);
            else
                q = q.Where(p => p.ReorderPoint > 0 &&
                    (_db.Stocks.Where(s => s.ProductId == p.Id).Sum(s => (decimal?)s.Quantity) ?? 0) <= p.ReorderPoint);
        }

        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? 15 : pageSize;
        var total = await q.CountAsync();
        var products = await q.OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Skip(Paging.Offset(page, pageSize)).Take(pageSize).ToListAsync();
        var ids = products.Select(p => p.Id).ToList();
        var stocks = await _db.Stocks.AsNoTracking().Where(s => ids.Contains(s.ProductId)).ToListAsync();
        var stocksByProduct = stocks.ToLookup(s => s.ProductId);
        var warehouseIds = products.Where(p => p.WarehouseId != null).Select(p => p.WarehouseId!.Value).ToList();
        var whNames = await _db.Warehouses.Where(w => warehouseIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Name);
        var effectivePrices = await ShopPricingService.ResolveAsync(_db, ids, DateTime.Today);
        var unitRules = await ResolveUnitRulesAsync(products);
        var mapped = products.Select(p =>
        {
            var dto = ToProductDto(p, stocksByProduct[p.Id], whNames);
            dto.AllowsFractionalQuantity = UnitRule(p, unitRules);
            if (effectivePrices.TryGetValue(p.Id, out var price)) dto.SalePrice = price.Price;
            return dto;
        }).ToList();
        return new PagedResult<Product>
        {
            Items = mapped,
            TotalCount = total, Page = page, PageSize = pageSize
        };
    }

    public Task<List<LookupItem>> GetProductLookupsAsync(Paging.Request? pagination = null)
        => _db.Products.AsNoTracking().OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Select(p => new LookupItem { Id = p.Id, Name = p.Code + " — " + p.Name })
            .ToPageListAsync(pagination, defaultCap: 100000);

    private static Product ToProductDto(Db.Product p, IEnumerable<Db.Stock> stocks, Dictionary<int, string>? whNames = null)
    {
        var ps = stocks.ToList();
        var qty = ps.Sum(s => s.Quantity);
        var totalCost = ps.Sum(s => s.Quantity * s.AvgCost);
        var avg = qty > 0 ? totalCost / qty : p.PurchasePrice;
        return new Product
        {
            Id = p.Id,
            Code = p.Code,
            Name = p.Name,
            Unit = p.Unit,
            Category = p.Category,
            Barcode = p.Barcode,
            TaxCode = p.TaxCode,
            SalePrice = p.SalePrice,
            PurchasePrice = p.PurchasePrice,
            InvoiceVatValue = p.InvoiceVatValue ?? (p.IsVatIncluded && p.VatRate >= 0 && p.VatRate <= 100 ? p.VatRate : 0),
            ReorderPoint = p.ReorderPoint,
            MaxStock = p.MaxStock,
            Description = p.Description,
            IsActive = p.IsActive,
            IsService = p.IsService,
            WarehouseId = p.WarehouseId,
            WarehouseName = p.WarehouseId.HasValue && whNames != null && whNames.TryGetValue(p.WarehouseId.Value, out var wn) ? wn : null,
            CreatedAt = p.CreatedAt,
            TotalStock = qty,
            AvgCost = avg
        };
    }

    public async Task<Product?> GetProductAsync(int id)
    {
        var p = await _db.Products.FindAsync(id);
        if (p is null) return null;
        var stocks = await _db.Stocks.Where(s => s.ProductId == id).ToListAsync();
        var whNames = await _db.Warehouses.ToDictionaryAsync(w => w.Id, w => w.Name);
        var dto = ToProductDto(p, stocks, whNames);
        dto.AllowsFractionalQuantity = UnitRule(p, await ResolveUnitRulesAsync(new[] { p }));
        var prices = await ShopPricingService.ResolveAsync(_db, new[] { id }, DateTime.Today);
        if (prices.TryGetValue(id, out var price)) dto.SalePrice = price.Price;
        return dto;
    }

    public async Task<Product> SaveProductAsync(Product dto)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            Db.Product entity;
            if (dto.Id == 0)
            {
                entity = new Db.Product { CreatedAt = DateTime.Now };
                _db.Products.Add(entity);
            }
            else
            {
                entity = await _db.Products.FirstOrDefaultAsync(p => p.Id == dto.Id) ?? throw new InvalidOperationException("کالا یافت نشد.");
            }

            var currentPrices = entity.Id == 0 ? new Dictionary<int, Db.ShopSalePrice>()
                : await ShopPricingService.ResolveAsync(_db, new[] { entity.Id }, DateTime.Today);
            decimal? previousSalePrice = entity.Id == 0 ? null : currentPrices.TryGetValue(entity.Id, out var currentPrice)
                ? currentPrice.Price : entity.SalePrice;
            // Preserve unchanged legacy fractional catalog prices; new/changed prices must be whole rials.
            if (entity.Id == 0 || previousSalePrice != dto.SalePrice) ShopPricingService.ValidatePrice(dto.SalePrice);
            if (!dto.IsService && (entity.Id == 0 || entity.PurchasePrice != dto.PurchasePrice))
                ShopPricingService.ValidatePrice(dto.PurchasePrice);

            if (dto.InvoiceVatValue.HasValue && !OrderLineAmounts.IsValidVatValue(dto.InvoiceVatValue.Value))
                throw new InvalidOperationException("ارزش افزوده باید عددی غیرمنفی با حداکثر دو رقم اعشار باشد.");
            await ShopCommissionProductKindGuard.EnsureChangeAllowedAsync(_db, entity, dto.IsService);
            if (dto.InvoiceVatValue.HasValue) entity.InvoiceVatValue = dto.InvoiceVatValue.Value;

            entity.Name = dto.Name.Trim();
            entity.IsService = dto.IsService;

            if (dto.IsService)
            {
                // خدمات: بدون گروه، بارکد، واحد، قیمت خرید، نقطه سفارش، حداکثر موجودی و انبار
                entity.Unit = "خدمت";
                entity.Category = null;
                entity.CategoryId = null;
                entity.Barcode = null;
                entity.SalePrice = dto.SalePrice;
                entity.PurchasePrice = 0;
                entity.ReorderPoint = 0;
                entity.MaxStock = 0;
                entity.WarehouseId = null;
            }
            else
            {
                entity.Unit = string.IsNullOrWhiteSpace(dto.Unit) ? "عدد" : dto.Unit.Trim();
                var unitOwner = entity.Id == 0
                    ? (_db.ActiveCompanyId is > 0 ? _db.ActiveCompanyId : _db.ResolveDefaultCompanyId())
                    : entity.CompanyId;
                // Do not borrow a same-named unit from another company, even when
                // this service is invoked without an HTTP company filter.
                if ((unitOwner.HasValue || !_db.ResolveDefaultCompanyId().HasValue) &&
                    !await _db.MeasureUnits.AnyAsync(u => u.CompanyId == unitOwner && u.Name == entity.Unit))
                    _db.MeasureUnits.Add(new Db.MeasureUnit { CompanyId = unitOwner, Name = entity.Unit,
                        AllowsFractionalQuantity = ShopQuantityRules.DefaultAllowsFraction(entity.Unit), CreatedAt = DateTime.Now });
                entity.Category = NullIfEmpty(dto.Category);

                // Keep the keyed and legacy text representation consistent when the shop form changes group.
                if (entity.Category == null) entity.CategoryId = null;
                else
                {
                    var matching = await _db.ProductCategories.Where(c => c.Name == entity.Category).Select(c => c.Id).ToListAsync();
                    if (matching.Count == 0)
                    {
                        _db.ProductCategories.Add(new Db.ProductCategory { Name = entity.Category, CreatedAt = DateTime.Now });
                        entity.CategoryId = null; // text fallback until that new group has an ID
                    }
                    else if (entity.CategoryId is not > 0 || !matching.Contains(entity.CategoryId.Value))
                        entity.CategoryId = matching.Count == 1 ? matching[0] : null;
                }
                entity.Barcode = NullIfEmpty(dto.Barcode);
                entity.SalePrice = dto.SalePrice;
                entity.PurchasePrice = dto.PurchasePrice;
                entity.ReorderPoint = dto.ReorderPoint;
                entity.MaxStock = dto.MaxStock;
                entity.WarehouseId = dto.WarehouseId is > 0 ? dto.WarehouseId : null;
            }

            entity.Description = NullIfEmpty(dto.Description);
            var taxCode = NullIfEmpty(dto.TaxCode)?.Trim();
            if (taxCode is { Length: < 5 or > 20 })
                throw new InvalidOperationException("شناسه مالیاتی کالا باید بین ۵ تا ۲۰ نویسه باشد.");
            if (taxCode is not null && await _db.Products.AnyAsync(p => p.TaxCode == taxCode && p.Id != entity.Id))
                throw new InvalidOperationException($"شناسه مالیاتی «{taxCode}» برای کالای دیگری ثبت شده است.");
            entity.TaxCode = taxCode;
            entity.IsActive = dto.IsActive;

            if (string.IsNullOrWhiteSpace(dto.Code))
            {
                entity.Code = await GenerateProductCodeAsync(entity.Id, dto.IsService);
            }
            else
            {
                var code = dto.Code.Trim();
                var dup = await _db.Products.AnyAsync(p => p.Code == code && p.Id != entity.Id);
                if (dup) throw new InvalidOperationException(dto.IsService ? "کد خدمت تکراری است." : "کد کالا تکراری است.");
                entity.Code = code;
            }

            if (entity.Id == 0 || previousSalePrice != dto.SalePrice ||
                (!currentPrices.ContainsKey(entity.Id) && ShopInvoiceAmounts.IsWholePrice(dto.SalePrice)))
                ShopPricingService.RecordCatalogPrice(_db, entity, previousSalePrice, _db.CurrentUserName);
            await _db.SaveChangesAsync();
            var result = await GetProductAsync(entity.Id) ?? throw new InvalidOperationException();
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            try { if (_db.Database.CurrentTransaction is not null) await transaction.RollbackAsync(); } catch { }
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<string> GenerateProductCodeAsync(int id, bool isService = false)
    {
        var prefix = isService ? "S" : "P";
        string code;
        int n = id == 0 ? await _db.Products.CountAsync() + 1 : id;
        do
        {
            code = $"{prefix}-{n:0000}";
            n++;
        } while (await _db.Products.AnyAsync(p => p.Code == code));
        return code;
    }

    public async Task DeleteProductAsync(int id)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            // A product with substantive price changes is retained for audit.
            if (await _db.ShopSalePrices.CountAsync(h => h.ProductId == id) > 1 ||
                await _db.ShopSalePrices.AnyAsync(h => h.ProductId == id && (h.Source == "group" || h.Source == "purchase")))
                throw new InvalidOperationException("کالا دارای سابقه تغییر قیمت است؛ برای حفظ سابقه می‌توانید آن را غیرفعال کنید.");
            var used = await _db.TransactionLines.AnyAsync(l => l.ProductId == id) ||
                await _db.ShopWarrantyItems.AnyAsync(i => i.ProductId == id) ||
                await _db.RepairItems.AnyAsync(i => i.ProductId == id && i.WarrantyDecision != WarrantyDecision.Unspecified);
            if (used) throw new InvalidOperationException("این کالا دارای سوابق انبار است و قابل حذف نیست؛ می‌توانید آن را غیرفعال کنید.");
            var p = await _db.Products.FirstOrDefaultAsync(p => p.Id == id);
            if (p is null) { await transaction.CommitAsync(); return; }
            var stocks = await _db.Stocks.Where(s => s.ProductId == id).ToListAsync();
            _db.Stocks.RemoveRange(stocks);
            _db.Products.Remove(p);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            try { if (_db.Database.CurrentTransaction is not null) await transaction.RollbackAsync(); } catch { }
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    // =============================== انبار ===============================

    public async Task<List<Warehouse>> GetWarehousesAsync(Paging.Request? pagination = null)
    {
        var list = await _db.Warehouses.OrderBy(w => w.Name).ThenBy(x => x.Id).ToPageListAsync(pagination);
        var ids = list.Select(w => w.Id).ToList();
        var stocks = await _db.Stocks.Where(s => ids.Contains(s.WarehouseId)).ToListAsync();
        return list.Select(w => new Warehouse
        {
            Id = w.Id,
            Name = w.Name,
            Address = w.Address,
            Phone = w.Phone,
            Note = w.Note,
            IsActive = w.IsActive,
            ItemCount = stocks.Count(s => s.WarehouseId == w.Id && s.Quantity > 0)
        }).ToList();
    }

    public async Task<Warehouse> SaveWarehouseAsync(Warehouse dto)
    {
        Db.Warehouse entity;
        if (dto.Id == 0) { entity = new Db.Warehouse(); _db.Warehouses.Add(entity); }
        else entity = await _db.Warehouses.FindAsync(dto.Id) ?? throw new InvalidOperationException("انبار یافت نشد.");

        entity.Name = dto.Name.Trim();
        entity.Address = NullIfEmpty(dto.Address);
        entity.Phone = NullIfEmpty(dto.Phone);
        entity.Note = NullIfEmpty(dto.Note);
        entity.IsActive = dto.IsActive;

        await _db.SaveChangesAsync();
        return (await GetWarehousesAsync()).First(w => w.Id == entity.Id);
    }

    public async Task DeleteWarehouseAsync(int id)
    {
        var used = await _db.Transactions.AnyAsync(t => t.WarehouseId == id);
        if (used) throw new InvalidOperationException("این انبار دارای سوابق است و قابل حذف نیست؛ می‌توانید آن را غیرفعال کنید.");
        var w = await _db.Warehouses.FindAsync(id);
        if (w is null) return;
        _db.Warehouses.Remove(w);
        await _db.SaveChangesAsync();
    }

    // =============================== طرف حساب ===============================

    /// <param name="referrerScope">
    /// اگر مقدار داشته باشد، فقط طرف حساب‌های همین پروندهٔ معرف برگردانده می‌شوند
    /// (ویزیتوری که مجوز دیدن کل مشتریان را ندارد).
    /// </param>
    public async Task<List<Party>> GetInvoicePartiesAsync(PartyType type)
    {
        var parties = await _db.Parties.AsNoTracking().Where(p => p.Type == type)
            .OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Select(p => new Party { Id = p.Id, Type = p.Type, Name = p.Name, Phone = p.Phone, Mobile = p.Mobile, IsActive = p.IsActive, ReferrerId = p.ReferrerId })
            .ToListAsync();
        var referrerIds = parties.Where(p => p.ReferrerId.HasValue).Select(p => p.ReferrerId!.Value).Distinct().ToList();
        var names = await _db.Referrers.AsNoTracking().Where(r => referrerIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name);
        foreach (var party in parties)
            if (party.ReferrerId is int id && names.TryGetValue(id, out var name)) party.ReferrerName = name;
        return parties;
    }

    public async Task<List<Party>> GetPartiesAsync(PartyType type, Paging.Request? pagination = null,
        int? referrerScope = null)
    {
        var q = _db.Parties.Where(p => p.Type == type);
        if (referrerScope is > 0)
            q = q.Where(p => p.ReferrerId == referrerScope || p.RegisteredByReferrerId == referrerScope);

        var list = await q.OrderBy(p => p.Name).ThenBy(x => x.Id).ToPageListAsync(pagination);
        var ids = list.Select(p => p.Id).ToList();
        var txns = await _db.Transactions.Include(t => t.Cheques).Include(t => t.Installments)
            .Where(t => t.PartyId != null && ids.Contains(t.PartyId.Value)).ToListAsync();
        // Use the same verified history as the detailed statement; a gross invoice total
        // is not a customer balance, particularly after receipts or a negative adjustment.
        var chequeHistory = await LoadChequeStatementHistoryAsync(ids);
        var (_, activeShopCredits) = await LoadShopCreditStatementEventsAsync(ids);
        var (tradeInBalanceEvents, tradeInAllocations) = await LoadTradeInStatementEventsAsync(ids);
        var tradeInByParty = tradeInBalanceEvents.ToLookup(e => e.PartyId);
        var cancelledTxnIds = (await _db.SaleChequeCancellations.AsNoTracking()
            .Where(c => c.SaleTransaction.PartyId != null && ids.Contains(c.SaleTransaction.PartyId.Value))
            .Select(c => c.SaleTransactionId).ToListAsync()).ToHashSet();
        var refNames = await _db.Referrers.ToDictionaryAsync(r => r.Id, r => r.Name);
        var today = DateTime.Today;

        return list.Select(p =>
        {
            var pTxns = txns.Where(t => t.PartyId == p.Id).ToList();
            var bounced = pTxns.Where(t => !cancelledTxnIds.Contains(t.Id)).SelectMany(t => t.Cheques).Where(c => c.Status == ChequeStatus.Bounced).ToList();
            var overdueInst = pTxns.Where(t => !cancelledTxnIds.Contains(t.Id)).SelectMany(t => t.Installments).Where(i => !i.IsPaid && i.DueDate.Date < today).ToList();

            return new Party
            {
                Id = p.Id,
                Type = p.Type,
                Name = p.Name,
                Phone = p.Phone,
                Mobile = p.Mobile,
                Address = p.Address,
                TaxId = p.TaxId,
                PostalCode = p.PostalCode,
                Note = p.Note,
                IsActive = p.IsActive,
                ReferrerId = p.ReferrerId,
                ReferrerName = p.ReferrerId.HasValue && refNames.TryGetValue(p.ReferrerId.Value, out var rn) ? rn : null,
                RegisteredByUserId = p.RegisteredByUserId,
                RegisteredByName = p.RegisteredByName,
                RegisteredByReferrerId = p.RegisteredByReferrerId,
                RegisteredByReferrerName = p.RegisteredByReferrerId.HasValue
                                           && refNames.TryGetValue(p.RegisteredByReferrerId.Value, out var rrn) ? rrn : null,
                RegisteredAt = p.RegisteredAt,
                Balance = pTxns.Sum(t => t.Type switch
                {
                    TransactionType.Sale when chequeHistory.TryGetValue(t.Id, out var history)
                        => t.Amount - history.CurrentIncome + history.InitialIncome
                           + history.Adjustments.Sum(a => a.Amount)
                           - history.Returns.Sum(r => r.GoodsWithVat + r.IncomeAmount)
                           - history.Cancellations.Sum(c => c.ReversedGoodsWithVat - c.ReversedIncomeDelta - c.RefundedCashAmount)
                           + history.CreditSettlements.Sum(s => s.Amount)
                           - history.InboundAllocations.Sum(a => a.Amount)
                           - history.InstallmentReceipts.Sum(r => r.Amount)
                           - t.CashAmount - history.Receipts.Sum(r => r.Amount) - activeShopCredits.GetValueOrDefault(t.Id),
                    TransactionType.Sale => t.Amount - Math.Max(0, t.SettledAmount) + tradeInAllocations.GetValueOrDefault(t.Id),
                    TransactionType.Purchase => -t.Amount + Math.Max(0, t.SettledAmount),
                    _ => 0m
                }) + tradeInByParty[p.Id].Sum(e => e.Debit - e.Credit),
                BouncedChequesCount = bounced.Count,
                BouncedChequesTotal = bounced.Sum(c => c.Amount),
                OverdueInstallmentsCount = overdueInst.Count,
                OverdueInstallmentsTotal = overdueInst.Sum(i => i.Amount)
            };
        }).ToList();
    }

    public async Task<Party> SavePartyAsync(Party dto)
    {
        var identifierError = ShopIdentifierRules.ValidateParty(dto);
        if (identifierError is not null) throw new InvalidOperationException(identifierError);
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            Db.Party entity;
            if (dto.Id == 0) { entity = new Db.Party(); _db.Parties.Add(entity); }
            else entity = await _db.Parties.FirstOrDefaultAsync(p => p.Id == dto.Id) ?? throw new InvalidOperationException("طرف حساب یافت نشد.");
            if (entity.Id > 0 && entity.Type != dto.Type &&
                (await _db.ShopWarranties.AnyAsync(w => w.PartyId == entity.Id) ||
                 await _db.RepairOrders.AnyAsync(r => r.PartyId == entity.Id && r.Items.Any(i => i.WarrantyDecision != WarrantyDecision.Unspecified))))
                throw new InvalidOperationException("نوع مشتری دارای پذیرش گارانتی برای حفظ سابقه قابل تغییر نیست.");

            entity.Type = dto.Type;
            entity.Name = dto.Name.Trim();
            entity.Phone = NullIfEmpty(dto.Phone);
            entity.Mobile = NullIfEmpty(dto.Mobile);
            entity.Address = NullIfEmpty(dto.Address);
            entity.TaxId = NullIfEmpty(dto.TaxId)?.Trim();
            entity.PostalCode = NullIfEmpty(dto.PostalCode)?.Trim();
            if (entity.TaxId is not null && (entity.TaxId.Length is not (10 or 11 or 12 or 14) || entity.TaxId.Any(c => !char.IsAsciiDigit(c))))
                throw new InvalidOperationException("شناسه مالیاتی خریدار باید ۱۰، ۱۱، ۱۲ یا ۱۴ رقم لاتین باشد.");
            if (entity.PostalCode is not null && (entity.PostalCode.Length != 10 || entity.PostalCode.Any(c => !char.IsAsciiDigit(c))))
                throw new InvalidOperationException("کد پستی خریدار باید دقیقاً ۱۰ رقم لاتین باشد.");
            entity.Note = NullIfEmpty(dto.Note);
            entity.IsActive = dto.IsActive;
            // معرف فقط برای مشتری معنا دارد؛ مقدار 0/null = بدون معرف
            entity.ReferrerId = dto.Type == PartyType.Customer && dto.ReferrerId is > 0 ? dto.ReferrerId : null;

            // «چه کسی این طرف حساب را ثبت کرد» فقط در ایجاد نوشته می‌شود؛ ویرایش آن را عوض نمی‌کند.
            if (dto.Id == 0)
            {
                entity.RegisteredByUserId = dto.RegisteredByUserId is > 0 ? dto.RegisteredByUserId : null;
                entity.RegisteredByName = NullIfEmpty(dto.RegisteredByName);
                entity.RegisteredByReferrerId = dto.RegisteredByReferrerId is > 0 ? dto.RegisteredByReferrerId : null;
                entity.RegisteredAt = dto.RegisteredAt ?? DateTime.Now;
            }

            await _db.SaveChangesAsync();
            var result = (await GetPartiesAsync(dto.Type)).First(p => p.Id == entity.Id);
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            try { if (_db.Database.CurrentTransaction is not null) await transaction.RollbackAsync(); } catch { }
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task DeletePartyAsync(int id)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var used = await _db.Transactions.AnyAsync(t => t.PartyId == id) ||
                await _db.ShopWarranties.AnyAsync(w => w.PartyId == id) ||
                await _db.RepairOrders.AnyAsync(r => r.PartyId == id && r.Items.Any(i => i.WarrantyDecision != WarrantyDecision.Unspecified));
            if (used) throw new InvalidOperationException("این طرف حساب دارای سوابق است و قابل حذف نیست؛ می‌توانید آن را غیرفعال کنید.");
            var p = await _db.Parties.FirstOrDefaultAsync(p => p.Id == id);
            if (p is null) { await transaction.CommitAsync(); return; }
            _db.Parties.Remove(p);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            try { if (_db.Database.CurrentTransaction is not null) await transaction.RollbackAsync(); } catch { }
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    // =============================== موجودی ===============================

    public async Task<PagedResult<StockItem>> GetStockAsync(int? warehouseId, string? search, bool belowOnly, int page, int pageSize)
    {
        var query = from stock in _db.Stocks.AsNoTracking()
                    join product in _db.Products on stock.ProductId equals product.Id
                    join warehouse in _db.Warehouses on stock.WarehouseId equals warehouse.Id into warehouses
                    from warehouse in warehouses.DefaultIfEmpty()
                    select new StockItem
                    {
                        WarehouseId = stock.WarehouseId, WarehouseName = warehouse == null ? null : warehouse.Name,
                        ProductId = product.Id, ProductName = product.Name, ProductCode = product.Code,
                        Unit = product.Unit, Category = product.Category,
                        Quantity = stock.Quantity, AvgCost = stock.AvgCost, LastSalePrice = product.SalePrice,
                        ReorderPoint = product.ReorderPoint, MaxStock = product.MaxStock
                    };
        if (warehouseId.HasValue) query = query.Where(r => r.WarehouseId == warehouseId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(r => r.ProductName.Contains(term) || r.ProductCode.Contains(term));
        }
        if (belowOnly) query = query.Where(r => r.ReorderPoint > 0 && r.Quantity <= r.ReorderPoint);
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? 15 : pageSize;
        var total = await query.CountAsync();
        var items = await query.OrderBy(r => r.ProductName).ThenBy(r => r.WarehouseName)
            .ThenBy(r => r.ProductId).ThenBy(r => r.WarehouseId)
            .Skip(Paging.Offset(page, pageSize)).Take(pageSize).ToListAsync();
        var prices = await ShopPricingService.ResolveAsync(_db, items.Select(i => i.ProductId), DateTime.Today);
        foreach (var item in items) if (prices.TryGetValue(item.ProductId, out var price)) item.LastSalePrice = price.Price;
        return new PagedResult<StockItem> { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
    }

    public async Task AdjustStockAsync(AdjustmentCommand cmd)
    {
        if (cmd.Quantity < 0) throw new InvalidOperationException("مقدار موجودی نمی‌تواند منفی باشد.");
        var p = await _db.Products.FindAsync(cmd.ProductId) ?? throw new InvalidOperationException("کالا یافت نشد.");

        var date = cmd.Date == default ? DateTime.Now : cmd.Date;

        var txn = new Db.Transaction
        {
            Number = await GenerateNumberAsync(TransactionType.Adjustment),
            Type = TransactionType.Adjustment,
            Date = date,
            Description = cmd.Description,
            WarehouseId = cmd.WarehouseId,
            CreatedAt = DateTime.Now
        };
        txn.Lines.Add(new Db.TransactionLine { ProductId = cmd.ProductId, Quantity = cmd.Quantity, Price = p.PurchasePrice, Description = "اصلاح موجودی" });

        await using var trx = await _db.Database.BeginTransactionAsync();
        _db.Transactions.Add(txn);
        await _db.SaveChangesAsync();  // ابتدا سند ذخیره می‌شود تا در بازمحاسبه دیده شود
        await RecalcStockAsync(cmd.WarehouseId, cmd.ProductId);
        await _db.SaveChangesAsync();
        await trx.CommitAsync();
    }

    // =============================== خرید و فروش ===============================

    public async Task<Order> CreateOrderAsync(OrderCommand cmd, string? user = null, bool trackShopCash = true)
    {
        // A repair/warranty owner may already hold the serializable transaction. Do not
        // commit, roll back or clear its tracked state here; propagate failures to it.
        var current = _db.Database.CurrentTransaction;
        if (current is not null && current.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("ثبت فاکتور مرتبط نیازمند تراکنش سریال‌پذیر است.");
        var ownsTransaction = current is null;
        await using var ownedTransaction = ownsTransaction
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;
        try
        {
            ShopPricingService.ValidatePurchasePriceInputs(cmd);
            if (cmd.Type == TransactionType.SaleReturn)
                throw new InvalidOperationException("برگشت فروش فقط از گردش ویژه فاکتور قابل ثبت است.");
            if (cmd.Type == TransactionType.WarrantyOut)
                throw new InvalidOperationException("خروج گارانتی فقط از پذیرش گارانتی قابل ثبت است.");
            if (cmd.ChequeCharge is not null && cmd.Type != TransactionType.Sale)
                throw new InvalidOperationException("تاخیر تادیه چک فقط برای فاکتور فروش مجاز است.");
            if ((cmd.ChequeCharge is not null || trackShopCash) && cmd.Installments.Any(i => i.IsPaid))
                throw new InvalidOperationException("تسویه قسط هنگام ثبت اولیه فاکتور سوددار مجاز نیست؛ دریافت قسط جداگانه ثبت می‌شود.");
            if ((cmd.ChequeCharge is not null || trackShopCash) && cmd.Cheques.Any(c => c.IsCleared))
                throw new InvalidOperationException("وصول چک هنگام ثبت اولیه فاکتور مجاز نیست؛ وصول باید جداگانه ثبت شود.");
            if (cmd.Lines.Count == 0) throw new InvalidOperationException("حداقل یک سطر کالا وارد کنید.");
            if (cmd.Lines.Any(l => l.VatValue.HasValue && !OrderLineAmounts.IsValidVatValue(l.VatValue.Value)))
                throw new InvalidOperationException("ارزش افزوده هر سطر باید عددی غیرمنفی با حداکثر دو رقم اعشار باشد.");
            if (cmd.Lines.Any(l => l.Quantity <= 0)) throw new InvalidOperationException("مقدار هر سطر باید بزرگ‌تر از صفر باشد.");
            if (cmd.Lines.Any(l => l.Quantity != decimal.Round(l.Quantity, 2)))
                throw new InvalidOperationException("مقدار هر سطر حداکثر تا دو رقم اعشار قابل ثبت است.");
            if (cmd.WarehouseId == 0) throw new InvalidOperationException("انبار را انتخاب کنید.");
            if (cmd.PartyId == 0) throw new InvalidOperationException("طرف حساب را انتخاب کنید.");

            // Older clients do not send VAT on creation; a missing value means zero.
            foreach (var line in cmd.Lines) line.VatValue ??= 0;
            ValidateTaxedLinePrecision(cmd.Lines);
            var productIds = cmd.Lines.Select(l => l.ProductId).Distinct().ToList();
            var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
            await ValidateOrderReferencesAsync(cmd, productIds, products.Count);

            var date = cmd.Date == default ? DateTime.Now : cmd.Date;
            var settings = await GetSettingsAsync();

            // خدمات فقط در فروش مجاز است
            if (cmd.Type == TransactionType.Purchase)
            {
                var svc = cmd.Lines.FirstOrDefault(l => products.TryGetValue(l.ProductId, out var p) && p.IsService);
                if (svc is not null)
                {
                    var name = products.TryGetValue(svc.ProductId, out var pp) ? pp.Name : "";
                    throw new InvalidOperationException($"«{name}» از نوع خدمات است و در سند خرید قابل انتخاب نیست.");
                }
            }

            // کالای دارای انبار اختصاصی فقط در همان انبار قابل خرید/فروش است
            foreach (var l in cmd.Lines)
            {
                if (products.TryGetValue(l.ProductId, out var prod) &&
                    !prod.IsService && prod.WarehouseId is > 0 && prod.WarehouseId != cmd.WarehouseId)
                {
                    throw new InvalidOperationException($"کالای «{prod.Name}» مخصوص انبار دیگری است و در این انبار قابل ثبت نیست.");
                }
            }

            // بررسی موجودی کافی برای فروش (خدمات موجودی ندارد — طبق تنظیمات)
            if (cmd.Type == TransactionType.Sale && !settings.AllowNegativeStock)
            {
                var stocks = await _db.Stocks.Where(s => s.WarehouseId == cmd.WarehouseId && productIds.Contains(s.ProductId)).ToListAsync();
                if (cmd.IsWarrantyInvoice)
                {
                    var physicalIds = productIds.Where(id => !products[id].IsService).ToList();
                    // Current Stock includes dated movements. A future receipt must not
                    // make a part appear available at today's warranty delivery.
                    if (await _db.Transactions.AnyAsync(t => t.WarehouseId == cmd.WarehouseId && t.Date > date &&
                        t.Lines.Any(l => physicalIds.Contains(l.ProductId))))
                        throw new InvalidOperationException("برای کالای گارانتی گردش آینده ثبت شده است؛ پیش از خروج، ترتیب موجودی را بررسی کنید.");
                    foreach (var group in cmd.Lines.Where(l => !products[l.ProductId].IsService).GroupBy(l => l.ProductId))
                    {
                        var available = stocks.FirstOrDefault(s => s.ProductId == group.Key)?.Quantity ?? 0m;
                        if (available < group.Sum(l => l.Quantity))
                            throw new InvalidOperationException($"موجودی «{products[group.Key].Name}» برای خروج گارانتی کافی نیست.");
                    }
                }
                foreach (var l in cmd.Lines)
                {
                    if (products.TryGetValue(l.ProductId, out var prod) && prod.IsService) continue;
                    var stock = stocks.FirstOrDefault(s => s.ProductId == l.ProductId);
                    var available = stock?.Quantity ?? 0;
                    if (available < l.Quantity)
                    {
                        var name = products.TryGetValue(l.ProductId, out var pp) ? pp.Name : "";
                        throw new InvalidOperationException($"موجودی «{name}» کافی نیست (موجود: {available:0.##}).");
                    }
                }
            }

            // معرف سند فروش: اگر از کلاینت نیامده باشد، خودکار از مشتری خوانده می‌شود (فاز ۹)
            int? saleReferrerId = null;
            if (cmd.Type == TransactionType.Sale)
            {
                if (cmd.ReferrerId is > 0) saleReferrerId = cmd.ReferrerId;
                else if (cmd.PartyId > 0)
                    saleReferrerId = await _db.Parties.Where(p => p.Id == cmd.PartyId)
                        .Select(p => p.ReferrerId).FirstOrDefaultAsync();
            }

            var wholeRials = (trackShopCash || cmd.ChequeCharge is not null) && (cmd.Type is TransactionType.Sale or TransactionType.Purchase);
            if (wholeRials) await ValidateShopLineRulesAsync(cmd, products);
            var txn = new Db.Transaction
            {
                ShopCashLedgerEnabled = trackShopCash && cmd.ChequeCharge is null && (cmd.Type is TransactionType.Sale or TransactionType.Purchase),
                Number = await GenerateNumberAsync(cmd.Type),
                Type = cmd.Type,
                Date = date,
                Description = cmd.Description,
                WarehouseId = cmd.WarehouseId,
                PartyId = cmd.PartyId,
                ReferrerId = saleReferrerId,
                CreatedAt = DateTime.Now
            };
            await SnapshotReferrerRatesAsync(txn);

            // در این مسیر، کل قابل پرداخت شامل سود محاسبه‌شده در سرور است؛
            // مبلغ چک‌ها را تغییر نمی‌دهیم و ثبت نهایی دوباره کنترل می‌شود.
            decimal? paymentTotal = null;
            if (cmd.ChequeCharge is { } chargeInput)
            {
                decimal income;
                try
                {
                    income = SaleChequeInitialCalculation.Calculate(chargeInput.CalculatorBaseAmount,
                        chargeInput.CalculatorRatePercent, chargeInput.CalculatorMonths);
                }
                catch (ArgumentOutOfRangeException)
                {
                    throw new InvalidOperationException("ورودی‌های ماشین‌حساب اولیه تاخیر تادیه چک معتبر نیست.");
                }
                var goodsTotal = ShopInvoiceLineMath.Total(cmd.Lines, wholeRials);
                if (income <= 0 || chargeInput.CalculatorBaseAmount > goodsTotal)
                    throw new InvalidOperationException("مبنای ماشین‌حساب یا سود اولیه معتبر نیست.");
                if (chargeInput.ConfirmedCreditDueDate?.Date != cmd.DueDate?.Date)
                    throw new InvalidOperationException("تأیید سررسید نسیه با اجزای پرداخت مطابقت ندارد.");
                paymentTotal = SaleChequeVoucherAmounts.Create(goodsTotal, 0, income).CustomerDebit;
            }

            // پرداخت سند (خرید و فروش): نقدی / نسیه / چک / اقساط
            if (cmd.Type is TransactionType.Sale or TransactionType.Purchase)
                ApplyPayment(txn, cmd, paymentTotal ?? ShopInvoiceLineMath.Total(cmd.Lines, wholeRials));

            foreach (var l in cmd.Lines)
            {
                var amounts = ShopInvoiceLineMath.Calculate(l, wholeRials);
                txn.Lines.Add(new Db.TransactionLine
                {
                    ProductId = l.ProductId, Quantity = l.Quantity, Price = l.Price,
                    DiscountPercent = amounts.DiscountPercent, DiscountAmount = amounts.DiscountAmount,
                    DiscountIsAmount = amounts.DiscountIsAmount, GrossAmount = amounts.Gross,
                    VatValue = amounts.VatValue, VatAmount = amounts.VatAmount, UnitCostSnapshot = l.UnitCostSnapshot,
                    Description = l.SourceDescription
                });
                txn.Amount += amounts.Total;
            }

            _db.Transactions.Add(txn);
            await _db.SaveChangesAsync();  // ابتدا سند ذخیره می‌شود تا در بازمحاسبه دیده شود

            foreach (var pid in productIds)
            {
                // Warranty labor is a service, not inventory consumption. Preserve
                // the established ordinary-invoice path while avoiding synthetic
                // negative stock for these new warranty-priced service rows.
                if (cmd.IsWarrantyInvoice && products[pid].IsService) continue;
                await RecalcStockAsync(cmd.WarehouseId, pid);
            }

            await _db.SaveChangesAsync();

            // کنترل نهایی: با احتساب همه اسناد (حتی اسناد آینده) موجودی منفی نشود
            await EnsureNoNegativeStockAsync(productIds.Select(pid => (cmd.WarehouseId, pid)));

            if (txn.ShopCashLedgerEnabled)
                await ShopOrderCashLedger.RecordInitialAsync(_db, txn, user);
            if (cmd.ChequeCharge is { } registration)
            {
                await FreezeChargedSaleCostsAsync(txn, products, settings.CostingMethod);
                await SaleChequeChargeRegistration.RegisterAsync(_db, txn.Id,
                    new SaleChequeChargeRegistrationInput(registration.CalculatorBaseAmount, registration.CalculatorRatePercent,
                        registration.CalculatorMonths, registration.AdjustmentRatePercent, registration.AdjustmentPeriodDays,
                        registration.ConfirmedCreditDueDate), user);
            }
            else if (txn.Type == TransactionType.Sale && txn.ReferrerId is > 0 &&
                (trackShopCash || (txn.Lines.Any(l => !products[l.ProductId].IsService && !l.UnitCostSnapshot.HasValue) &&
                    await _db.ShopPricingPolicies.AnyAsync(p => p.CompanyId == txn.CompanyId && p.MinimumProfitEnabled))))
            {
                // Freeze reliable store-ledger costs for new commission-bearing sales.
                // The existing method leaves mixed-ledger costs unknown rather than inventing them.
                await FreezeChargedSaleCostsAsync(txn, products, settings.CostingMethod);
            }
            if (txn.Type == TransactionType.Sale)
                await ShopMinimumProfitGuard.ValidateAsync(_db, txn, products);
            if (trackShopCash && txn.Type == TransactionType.Purchase)
                await ShopPricingService.RecordPurchasePricesAsync(_db, txn, cmd, user);
            var savedOrder = await GetOrderAsync(txn.Id) ?? throw new InvalidOperationException();
            if (ownsTransaction) await ownedTransaction!.CommitAsync();
            return savedOrder;
        }
        catch
        {
            if (ownsTransaction)
            {
                try { if (_db.Database.CurrentTransaction is not null) await ownedTransaction!.RollbackAsync(); } catch { }
                _db.ChangeTracker.Clear();
            }
            throw;
        }
    }

    public async Task<Order?> GetOrderAsync(int id)
    {
        var t = await _db.Transactions.Include(x => x.Lines)
            .Include(x => x.Cheques).Include(x => x.Installments)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (t is null) return null;
        return await ToOrderDtoAsync(t);
    }

    public async Task<PagedResult<Order>> GetOrdersAsync(TransactionType type, DateTime? from, DateTime? to, int? partyId, int? warehouseId, int page, int pageSize, bool? hasChequeIncome = null, bool openingOnly = false)
    {
        var q = _db.Transactions.Include(x => x.Lines)
            .Include(x => x.Cheques).Include(x => x.Installments)
            .Where(t => t.Type == type);
        if (from.HasValue) q = q.Where(t => t.Date >= from.Value.Date);
        if (to.HasValue) q = q.Where(t => t.Date < to.Value.Date.AddDays(1));
        if (partyId.HasValue) q = q.Where(t => t.PartyId == partyId.Value);
        if (warehouseId.HasValue) q = q.Where(t => t.WarehouseId == warehouseId.Value);
        if (hasChequeIncome.HasValue)
            q = q.Where(t => _db.SaleChequeCharges.Any(c => c.TransactionId == t.Id && c.Amount > 0) == hasChequeIncome.Value);

        // Filter before paging: ordinary invoices must not hide older opening entries.
        if (openingOnly)
            q = q.Where(t => (t.Description != null &&
                    (t.Description.StartsWith("[OPENING:") || t.Description.Contains("افتتاح") ||
                     t.Description == "مانده اقساط بدهی باز اول دوره" ||
                     t.Description == "موجودی اولیه اول دوره انبار")) ||
                t.Lines.Any(l => _db.Products.Any(p => p.Id == l.ProductId &&
                    (p.Code == "OPN-SVC" || p.Code == "OPN-GDS"))));

        var total = await q.CountAsync();
        var pageItems = await q.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
                               .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var saleIds = pageItems.Where(t => t.Type == TransactionType.Sale).Select(t => t.Id).ToList();
        var chargeBySale = await _db.SaleChequeCharges.AsNoTracking().Where(c => saleIds.Contains(c.TransactionId))
            .ToDictionaryAsync(c => c.TransactionId, c => c.Amount);
        var items = new List<Order>();
        foreach (var t in pageItems) items.Add(await ToOrderDtoAsync(t, chargeBySale.GetValueOrDefault(t.Id)));

        return new PagedResult<Order> { Items = items, TotalCount = total };
    }

    private async Task<Order> ToOrderDtoAsync(Db.Transaction t, decimal? knownChequeIncome = null)
    {
        var chargeRow = t.Type == TransactionType.Sale
            ? await _db.SaleChequeCharges.AsNoTracking().FirstOrDefaultAsync(c => c.TransactionId == t.Id)
            : null;
        var chequeIncome = knownChequeIncome ?? chargeRow?.Amount ?? 0m;
        if (chequeIncome < 0 || chequeIncome > t.Amount)
            throw new InvalidOperationException("مبلغ تاخیر تادیه چک با مبلغ فاکتور مطابقت ندارد.");
        var returnRecords = t.Type == TransactionType.Sale
            ? await _db.SaleChequeReturns.AsNoTracking().Include(r => r.ReturnTransaction)
                .Where(r => r.SaleTransactionId == t.Id).OrderBy(r => r.Id).ToListAsync()
            : new List<Db.SaleChequeReturn>();
        var cancelRecord = t.Type == TransactionType.Sale
            ? await _db.SaleChequeCancellations.AsNoTracking().FirstOrDefaultAsync(c => c.SaleTransactionId == t.Id)
            : null;
        var returnedGoods = returnRecords.Sum(r => r.GoodsAmount + r.VatAmount)
                          + (cancelRecord is not null ? cancelRecord.ReversedGoodsAmount + cancelRecord.ReversedVatAmount : 0m);
        var returnedIncome = returnRecords.Sum(r => r.IncomeAmount)
                           + (cancelRecord is not null && cancelRecord.ReversedIncomeDelta < 0 ? -cancelRecord.ReversedIncomeDelta : 0m);
        decimal availableCustomerCredit = 0m;
        if (chargeRow is not null && cancelRecord is null)
        {
            var signed = await SaleChequeSettlementLedger.SignedChargedSaleBalanceAsync(_db, t, chargeRow);
            availableCustomerCredit = Math.Max(0m, -signed);
        }
        var products = await _db.Products.Where(p => t.Lines.Select(l => l.ProductId).Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var invoiceParty = t.PartyId.HasValue
            ? await _db.Parties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == t.PartyId.Value)
            : null;
        var partyName = invoiceParty?.Name;
        string? whName = await _db.Warehouses.Where(w => w.Id == t.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync();
        var referrer = t.ReferrerId.HasValue
            ? await _db.Referrers.AsNoTracking().FirstOrDefaultAsync(r => r.Id == t.ReferrerId.Value)
            : null;
        var hasAnyCommissionRateSnapshot = t.ReferrerGoodsPercentSnapshot.HasValue || t.ReferrerServicePercentSnapshot.HasValue;
        var hasCommissionRateSnapshot = t.ReferrerGoodsPercentSnapshot.HasValue && t.ReferrerServicePercentSnapshot.HasValue;
        // A partial snapshot is corrupt, not a legacy document. Never silently
        // replace its missing half with today's rate or the old commission rule.
        decimal? goodsRate = hasCommissionRateSnapshot ? t.ReferrerGoodsPercentSnapshot
            : !hasAnyCommissionRateSnapshot ? referrer?.GoodsCommissionPercent : null;
        decimal? serviceRate = hasCommissionRateSnapshot ? t.ReferrerServicePercentSnapshot
            : !hasAnyCommissionRateSnapshot ? referrer?.ServiceCommissionPercent : null;
        if (goodsRate is < 0 or > 100 || serviceRate is < 0 or > 100)
            goodsRate = serviceRate = null;

        string? trsAccountName = null;
        if (t.TrsAccountId.HasValue)
            trsAccountName = await _db.TrsAccounts.Where(a => a.Id == t.TrsAccountId.Value).Select(a => a.Name).FirstOrDefaultAsync();

        var order = new Order
        {
            Id = t.Id,
            ChequeIncomeAmount = chequeIncome,
            HasChequeCharge = chargeRow is not null,
            IsCancelled = cancelRecord is not null,
            ReturnedGoodsAmount = returnedGoods,
            ReturnedChequeIncomeAmount = returnedIncome,
            CustomerCreditAmount = chargeRow?.CustomerCredit ?? 0m,
            AvailableCustomerCredit = availableCustomerCredit,
            ChequeChargeSnapshot = chargeRow is { CalculatorBaseAmount: not null, CalculatorRatePercent: not null, CalculatorMonths: not null }
                ? new SaleChequeChargeInput
                {
                    CalculatorBaseAmount = chargeRow.CalculatorBaseAmount.Value,
                    CalculatorRatePercent = chargeRow.CalculatorRatePercent.Value,
                    CalculatorMonths = chargeRow.CalculatorMonths.Value,
                    AdjustmentRatePercent = chargeRow.RatePercent,
                    AdjustmentPeriodDays = chargeRow.PeriodDays,
                    ConfirmedCreditDueDate = t.DueDate
                }
                : null,
            ChequeReturns = returnRecords.Select(r => new SaleChequeReturnSummaryDto
            {
                Id = r.Id,
                ReturnNumber = r.ReturnTransaction.Number,
                Date = r.Date,
                GoodsAmount = r.GoodsAmount,
                VatAmount = r.VatAmount,
                IncomeAmount = r.IncomeAmount,
                BalanceAfter = r.BalanceAfter,
                UncollectedChequeExcess = r.UncollectedChequeExcess,
                Decision = r.Decision
            }).ToList(),
            WarehouseId = t.WarehouseId,
            WarehouseName = whName,
            PartyId = t.PartyId ?? 0,
            PartyName = partyName,
            PartyPhone = string.IsNullOrWhiteSpace(invoiceParty?.Mobile) ? invoiceParty?.Phone : invoiceParty?.Mobile,
            PartyAddress = invoiceParty?.Address,
            PartyTaxId = invoiceParty?.TaxId,
            ReferrerId = t.ReferrerId,
            ReferrerName = referrer?.Name,
            ReferrerGoodsCommissionPercent = goodsRate,
            ReferrerServiceCommissionPercent = serviceRate,
            CommissionRatesAreHistorical = hasCommissionRateSnapshot && goodsRate.HasValue && serviceRate.HasValue,
            UsesLegacyCommissionPolicy = t.ReferrerId is > 0 && !hasAnyCommissionRateSnapshot,
            Type = t.Type,
            Number = t.Number,
            Date = t.Date,
            Description = t.Description ?? "",
            TotalAmount = t.Amount - returnedGoods,
            PaymentMethod = t.PaymentMethod,
            CashType = t.CashType,
            CashTrackingNumber = t.CashTrackingNumber,
            CashAmount = t.CashAmount,
            ShopCashLedgerEnabled = t.ShopCashLedgerEnabled,
            TrsAccountId = t.TrsAccountId,
            TrsAccountName = trsAccountName,
            DueDate = t.DueDate,
            SettledAmount = t.SettledAmount,
            Cheques = t.Cheques.OrderBy(c => c.DueDate).Select(c => new ChequeDto
            {
                Id = c.Id, Number = c.Number, TrsAccountId = c.TrsAccountId, BankName = c.BankName, AccountInfo = c.AccountInfo,
                OwnerName = c.OwnerName, Amount = c.Amount, DueDate = c.DueDate,
                IsCleared = c.IsCleared, ClearedAt = c.ClearedAt, Status = c.Status,
                BouncedAt = c.BouncedAt, BounceReason = c.BounceReason, Note = c.Note
            }).ToList(),
            Installments = t.Installments.OrderBy(i => i.No).Select(i => new InstallmentDto
            {
                Id = i.Id, No = i.No, Amount = i.Amount, DueDate = i.DueDate, IsPaid = i.IsPaid, PaidAt = i.PaidAt
            }).ToList(),
            Lines = t.Lines.OrderBy(l => l.Id).Select(l => new OrderLine
            {
                Id = l.Id,
                ProductId = l.ProductId,
                ProductName = products.TryGetValue(l.ProductId, out var p) ? p.Name : "",
                Description = l.Description,
                Unit = products.TryGetValue(l.ProductId, out var p2) ? p2.Unit : "",
                IsService = products.TryGetValue(l.ProductId, out var p3) && p3.IsService,
                Quantity = l.Quantity,
                Price = l.Price,
                DiscountPercent = l.DiscountPercent,
                DiscountAmount = l.DiscountAmount,
                DiscountIsAmount = l.DiscountIsAmount,
                GrossAmount = l.GrossAmount,
                VatValue = l.VatValue,
                VatAmount = l.VatAmount,
                UnitCost = l.UnitCostSnapshot
            }).ToList()
        };

        // محاسبه سود هر سطر و پورسانت معرف (فقط برای فروش)
        await EnrichSaleProfitAsync(order);

        return order;
    }

    public async Task ReconcileLegacyOrderCashAsync(int id, ShopCashReconciliationCommand cmd, string? user)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var order = await _db.Transactions.FirstOrDefaultAsync(t => t.Id == id)
                ?? throw new InvalidOperationException("فاکتور در شرکت جاری یافت نشد.");
            await ShopOrderCashLedger.ReconcileLegacyAsync(_db, order, cmd, user);
            await tx.CommitAsync();
        }
        catch
        {
            try { if (_db.Database.CurrentTransaction is not null) await tx.RollbackAsync(); } catch { }
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<ShopOrderCreditState> GetOrderCreditStateAsync(int id)
    {
        // Read a coherent invoice, payment and reserved-obligation snapshot; the cash verifier
        // also deliberately requires the same serializable transaction as mutations.
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var state = await ShopOrderCreditPosting.GetStateAsync(_db, id);
            await tx.CommitAsync();
            return state;
        }
        catch
        {
            try { if (_db.Database.CurrentTransaction is not null) await tx.RollbackAsync(); } catch { }
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task ReceiveOrderCreditAsync(int id, ShopOrderCreditReceiptCommand cmd, string? user)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await ShopOrderCreditPosting.ReceiveAsync(_db, id, cmd, user);
            await tx.CommitAsync();
        }
        catch
        {
            try { if (_db.Database.CurrentTransaction is not null) await tx.RollbackAsync(); } catch { }
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task ReverseOrderCreditAsync(int id, int receiptId, ShopOrderCreditReversalCommand cmd, string? user)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await ShopOrderCreditPosting.ReverseAsync(_db, id, receiptId, cmd, user);
            await tx.CommitAsync();
        }
        catch
        {
            try { if (_db.Database.CurrentTransaction is not null) await tx.RollbackAsync(); } catch { }
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    /// <summary>ویرایش سند خرید/فروش: جایگزینی سرصفحه و اقلام + بازمحاسبه موجودی انبارهای قدیم و جدید.</summary>
    public async Task<Order> UpdateOrderAsync(int id, OrderCommand cmd, string? user = null)
    {
        ShopPricingService.ValidatePurchasePriceInputs(cmd);
        if (cmd.ChequeCharge is not null)
            throw new InvalidOperationException("افزودن یا ویرایش تاخیر تادیه چک از مسیر قدیمی ویرایش پشتیبانی نمی‌شود.");
        if (cmd.Lines.Count == 0) throw new InvalidOperationException("حداقل یک سطر کالا وارد کنید.");
        if (cmd.Lines.Any(l => l.VatValue.HasValue && !OrderLineAmounts.IsValidVatValue(l.VatValue.Value)))
            throw new InvalidOperationException("ارزش افزوده هر سطر باید عددی غیرمنفی با حداکثر دو رقم اعشار باشد.");
        if (cmd.Lines.Any(l => l.Quantity <= 0)) throw new InvalidOperationException("مقدار هر سطر باید بزرگ‌تر از صفر باشد.");
        if (cmd.WarehouseId == 0) throw new InvalidOperationException("انبار را انتخاب کنید.");
        if (cmd.PartyId == 0) throw new InvalidOperationException("طرف حساب را انتخاب کنید.");

        await using var trx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var txn = await _db.Transactions.Include(t => t.Lines)
                .Include(t => t.Cheques).Include(t => t.Installments)
                .FirstOrDefaultAsync(t => t.Id == id)
                ?? throw new InvalidOperationException("سند یافت نشد.");
            if (txn.Type is not (TransactionType.Purchase or TransactionType.Sale))
                throw new InvalidOperationException("فقط اسناد خرید و فروش قابل ویرایش هستند.");
            await RejectLinkedWarrantyInvoiceMutationAsync(txn.Id);
            await RejectUnimplementedChequeChargeMutationAsync(txn.Id);
            if (await _db.ShopExpenseCheques.AnyAsync(c => c.SourceTransactionId == txn.Id))
                throw new InvalidOperationException("فاکتور دارای سابقه چک خرج‌شده است و تغییر مستقیم آن مجاز نیست.");
            if (await _db.ShopTradeInUses.AnyAsync(u => u.TargetSaleId == txn.Id))
                throw new InvalidOperationException("فاکتور دارای مصرف اعتبار تعویض است و ویرایش مستقیم آن مجاز نیست.");
            if (cmd.Type != txn.Type) throw new InvalidOperationException("نوع فاکتور قابل تغییر نیست.");
            if (await _db.SaleChequeReceipts.AnyAsync(r => r.TransactionId == txn.Id))
                throw new InvalidOperationException("فاکتور دارای تاریخچه وصول چک است؛ ویرایش مستقیم آن برای حفظ سابقه مجاز نیست.");
            if (txn.Cheques.Any(c => c.IsCleared || c.Status != ChequeStatus.InHand) ||
                txn.Installments.Any(i => i.IsPaid) || txn.SettledAmount != txn.CashAmount ||
                cmd.Cheques.Any(c => c.IsCleared) || cmd.Installments.Any(i => i.IsPaid))
                throw new InvalidOperationException("فاکتور دارای وصول یا تغییر وضعیت است؛ ابتدا گردش پرداخت آن را بررسی و اصلاح کنید.");
            var previousCash = await ShopOrderCashLedger.VerifyCurrentAsync(_db, txn);
            var hasCashHistory = await _db.ShopOrderCashEntries.IgnoreQueryFilters().AnyAsync(e => e.TransactionId == txn.Id);
            var originalAmount = txn.Amount;
            if (hasCashHistory && (cmd.PartyId != txn.PartyId || (cmd.Date != default && cmd.Date.Date != txn.Date.Date) ||
                ((cmd.Description?.Contains("افتتاح") == true) != (txn.Description?.Contains("افتتاح") == true))))
                throw new InvalidOperationException("تغییر طرف حساب، تاریخ یا نوع افتتاحِ فاکتور دارای گردش نقدی مجاز نیست؛ تاریخچه باید حفظ شود.");
            var oldCash = (txn.CashAmount, txn.CashType, txn.TrsAccountId, txn.CashTrackingNumber, txn.PartyId, txn.Date);

            var originalLines = txn.Lines.ToDictionary(l => l.Id);
            var existingLineQuantities = txn.Lines.ToDictionary(l => l.Id, l => (l.ProductId, l.Quantity));
            var existingLineCosts = txn.Lines.ToDictionary(l => l.Id, l => (l.ProductId, l.UnitCostSnapshot));
            var existingLineVat = txn.Lines.ToDictionary(l => l.Id, l => (l.ProductId, l.VatValue));
            foreach (var line in cmd.Lines)
                line.VatValue ??= existingLineVat.TryGetValue(line.Id, out var oldVat) && oldVat.ProductId == line.ProductId
                    ? oldVat.VatValue : 0m;
            ValidateTaxedLinePrecision(cmd.Lines);
            if (cmd.Lines.Any(l => l.Quantity != decimal.Round(l.Quantity, 2)
                                   && (!existingLineQuantities.TryGetValue(l.Id, out var original)
                                       || original.ProductId != l.ProductId || original.Quantity != l.Quantity)))
                throw new InvalidOperationException("مقدار هر سطر حداکثر تا دو رقم اعشار قابل ثبت است؛ فقط مقدار قدیمیِ بدون تغییر قابل حفظ است.");

            if (txn.Type is TransactionType.Sale or TransactionType.Purchase)
                ApplyPayment(txn, cmd, ShopInvoiceLineMath.Total(cmd.Lines, true, originalLines));
            if (!txn.ShopCashLedgerEnabled &&
                (txn.CashAmount != oldCash.CashAmount || ((oldCash.CashAmount > 0 || txn.CashAmount > 0) &&
                    (txn.CashType != oldCash.CashType || txn.TrsAccountId != oldCash.TrsAccountId ||
                     txn.CashTrackingNumber != oldCash.CashTrackingNumber || cmd.PartyId != oldCash.PartyId ||
                     (cmd.Date != default && cmd.Date.Date != oldCash.Date.Date)))))
                throw new InvalidOperationException("پرداخت نقدی فاکتور قدیمی تطبیق نشده است؛ برای جلوگیری از ثبت تکراری، ابتدا گردش آن را بررسی و تطبیق کنید.");

            var productIds = cmd.Lines.Select(l => l.ProductId).Distinct().ToList();
            var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
            await ValidateOrderReferencesAsync(cmd, productIds, products.Count);
            await ValidateShopLineRulesAsync(cmd, products, originalLines);

            // A frozen goods cost is only reusable while the stock chronology and
            // ordered goods quantities stay unchanged. A changed quantity can use
            // a different FIFO/LIFO layer even when the product ID is the same.
            var goodsCostBasisChanged = false;
            if (txn.Type == TransactionType.Sale)
            {
                var oldProductIds = txn.Lines.Select(l => l.ProductId).Distinct().ToList();
                var oldServiceIds = (await _db.Products.Where(p => oldProductIds.Contains(p.Id) && p.IsService)
                    .Select(p => p.Id).ToListAsync()).ToHashSet();
                var oldBasis = txn.Lines.Where(l => !oldServiceIds.Contains(l.ProductId)).OrderBy(l => l.Id)
                    .Select(l => (l.Id, l.ProductId, l.Quantity)).ToList();
                var newBasis = cmd.Lines.Where(l => !products.TryGetValue(l.ProductId, out var p) || !p.IsService)
                    .Select(l => (l.Id, l.ProductId, l.Quantity)).ToList();
                goodsCostBasisChanged = txn.WarehouseId != cmd.WarehouseId ||
                    (cmd.Date != default && cmd.Date != txn.Date) || !oldBasis.SequenceEqual(newBasis);
            }

            // کالای دارای انبار اختصاصی فقط در همان انبار قابل ثبت است
            foreach (var l in cmd.Lines)
            {
                if (products.TryGetValue(l.ProductId, out var prod) &&
                    !prod.IsService && prod.WarehouseId is > 0 && prod.WarehouseId != cmd.WarehouseId)
                {
                    throw new InvalidOperationException($"کالای «{prod.Name}» مخصوص انبار دیگری است و در این انبار قابل ثبت نیست.");
                }
            }

            if (txn.Type == TransactionType.Purchase)
            {
                var pricedProductIds = await _db.ShopSalePrices.AsNoTracking().Where(h => h.PurchaseTransactionId == txn.Id)
                    .Select(h => h.ProductId).Distinct().ToListAsync();
                if (pricedProductIds.Count > 0)
                {
                    if ((cmd.Date != default && cmd.Date.Date != txn.Date.Date) || cmd.WarehouseId != txn.WarehouseId || cmd.PartyId != txn.PartyId)
                        throw new InvalidOperationException("تاریخ، انبار و طرف حساب خریدی که منبع سابقه قیمت است قابل تغییر مستقیم نیست.");
                    foreach (var productId in pricedProductIds)
                    {
                        var oldLines = txn.Lines.Where(l => l.ProductId == productId).ToList();
                        var newLines = cmd.Lines.Where(l => l.ProductId == productId).ToList();
                        if (oldLines.Count != newLines.Count || oldLines.Any(old => !newLines.Any(line => line.Id == old.Id &&
                            line.Quantity == old.Quantity && line.Price == old.Price &&
                            ShopInvoiceLineMath.Calculate(line, true, old).DiscountAmount == old.DiscountAmount)))
                            throw new InvalidOperationException("سطر خریدی که منبع سابقه قیمت فروش است قابل تغییر مستقیم نیست؛ سابقه باید حفظ شود.");
                    }
                }
            }
            int? floorReferrer = txn.Type == TransactionType.Sale
                ? (cmd.ReferrerId is > 0 ? cmd.ReferrerId : await _db.Parties.Where(p => p.Id == cmd.PartyId).Select(p => p.ReferrerId).FirstOrDefaultAsync())
                : null;
            var unchangedFloorLines = txn.Type == TransactionType.Sale
                ? ShopMinimumProfitGuard.UnchangedLineIndexes(txn, cmd, floorReferrer) : new HashSet<int>();

            // جفت‌های (انبار، کالا) متأثر: هم اقلام قدیم و هم جدید
            var affected = txn.Lines.Select(l => (Wh: txn.WarehouseId, Pid: l.ProductId))
                .Concat(cmd.Lines.Select(l => (Wh: cmd.WarehouseId, Pid: l.ProductId)))
                .Distinct().ToList();

            // جایگزینی اقلام و سرصفحه
            _db.TransactionLines.RemoveRange(txn.Lines);
            txn.Lines.Clear();
            txn.WarehouseId = cmd.WarehouseId;
            txn.PartyId = cmd.PartyId;
            // معرف سند فروش: اگر از کلاینت نیامده باشد، خودکار از مشتری خوانده می‌شود (فاز ۹)
            int? updReferrerId = null;
            if (txn.Type == TransactionType.Sale)
            {
                if (cmd.ReferrerId is > 0) updReferrerId = cmd.ReferrerId;
                else if (cmd.PartyId > 0)
                    updReferrerId = await _db.Parties.Where(p => p.Id == cmd.PartyId)
                        .Select(p => p.ReferrerId).FirstOrDefaultAsync();
            }
            if (txn.ReferrerId != updReferrerId)
            {
                txn.ReferrerId = updReferrerId;
                await SnapshotReferrerRatesAsync(txn);
            }
            // An unchanged old referrer retains its original snapshot, including
            // null for a legacy document whose historical rates are unknown.
            txn.Date = cmd.Date == default ? txn.Date : cmd.Date;
            txn.Description = cmd.Description;
            txn.Amount = 0;
            foreach (var l in cmd.Lines)
            {
                var unitCostSnapshot = existingLineCosts.TryGetValue(l.Id, out var oldCost) && oldCost.ProductId == l.ProductId
                    ? oldCost.UnitCostSnapshot
                    : l.UnitCostSnapshot;
                if (txn.Type == TransactionType.Sale && goodsCostBasisChanged &&
                    (!products.TryGetValue(l.ProductId, out var costProduct) || !costProduct.IsService))
                    unitCostSnapshot = null;

                var oldLine = originalLines.TryGetValue(l.Id, out var existingLine) ? existingLine : null;
                var amounts = ShopInvoiceLineMath.Calculate(l, true, oldLine);
                txn.Lines.Add(new Db.TransactionLine
                {
                    ProductId = l.ProductId, Quantity = l.Quantity, Price = l.Price,
                    DiscountPercent = amounts.DiscountPercent, DiscountAmount = amounts.DiscountAmount,
                    DiscountIsAmount = amounts.DiscountIsAmount, GrossAmount = amounts.Gross,
                    VatValue = amounts.VatValue, VatAmount = amounts.VatAmount, UnitCostSnapshot = unitCostSnapshot
                });
                txn.Amount += amounts.Total;
            }
            if (hasCashHistory && txn.Amount != originalAmount)
                throw new InvalidOperationException("تغییر مبلغ کالای فاکتور دارای گردش نقدی مستقیم مجاز نیست؛ تاریخچه باید حفظ شود.");
            await _db.SaveChangesAsync();

            // بازمحاسبه موجودی همه جفت‌های متأثر
            foreach (var (wh, pid) in affected)
                await RecalcStockAsync(wh, pid);
            await _db.SaveChangesAsync();

            // اعتبارسنجی: هیچ موجودی‌ای نباید منفی شود (با احتساب اسناد بعدی — طبق تنظیمات)
            try
            {
                await EnsureNoNegativeStockAsync(affected);
            }
            catch
            {
                await trx.RollbackAsync();
                throw;
            }

            if (goodsCostBasisChanged && txn.Type == TransactionType.Sale && txn.ReferrerId is > 0 &&
                txn.ReferrerGoodsPercentSnapshot.HasValue && txn.ReferrerServicePercentSnapshot.HasValue)
            {
                var settings = await GetSettingsAsync();
                await FreezeChargedSaleCostsAsync(txn, products, settings.CostingMethod);
            }
            if (txn.Type == TransactionType.Sale)
                await ShopMinimumProfitGuard.ValidateAsync(_db, txn, products, unchangedFloorLines);
            if (txn.Type == TransactionType.Purchase)
                await ShopPricingService.RecordPurchasePricesAsync(_db, txn, cmd, user);
            if (txn.ShopCashLedgerEnabled)
                await ShopOrderCashLedger.SyncAfterEditAsync(_db, txn, previousCash, user);
            var savedOrder = await GetOrderAsync(txn.Id) ?? throw new InvalidOperationException();
            await trx.CommitAsync();
            return savedOrder;
        }
        catch
        {
            try { if (_db.Database.CurrentTransaction is not null) await trx.RollbackAsync(); } catch { }
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task DeleteOrderAsync(int id, string? user = null, bool allowUntrackedRollback = false)
    {
        await using var trx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var t = await _db.Transactions.Include(x => x.Lines).Include(x => x.Cheques).Include(x => x.Installments).FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new InvalidOperationException("سند یافت نشد.");

            if (t.Type == TransactionType.WarrantyOut)
                throw new InvalidOperationException("خروج انبار گارانتی از مسیر فاکتور قابل حذف نیست.");
            await RejectLinkedWarrantyInvoiceMutationAsync(t.Id);
            await RejectUnimplementedChequeChargeMutationAsync(t.Id);
            if (await _db.ShopExpenseCheques.AnyAsync(c => c.SourceTransactionId == t.Id))
                throw new InvalidOperationException("فاکتور دارای سابقه چک خرج‌شده است و تغییر مستقیم آن مجاز نیست.");
            if (await _db.ShopTradeInUses.AnyAsync(u => u.TargetSaleId == t.Id))
                throw new InvalidOperationException("فاکتور دارای مصرف اعتبار تعویض است و حذف مستقیم آن مجاز نیست.");
            if (t.Type == TransactionType.Purchase && await _db.ShopSalePrices.AnyAsync(h => h.PurchaseTransactionId == t.Id))
                throw new InvalidOperationException("این خرید منبع سابقه قیمت فروش است و حذف مستقیم آن مجاز نیست.");
            if (await _db.SaleChequeReceipts.AnyAsync(r => r.TransactionId == t.Id))
                throw new InvalidOperationException("فاکتور دارای تاریخچه وصول چک است و حذف مستقیم آن مجاز نیست.");
            if (t.Cheques.Any(c => c.IsCleared || c.Status != ChequeStatus.InHand) || t.Installments.Any(i => i.IsPaid) || t.SettledAmount != t.CashAmount)
                throw new InvalidOperationException("فاکتور دارای گردش پرداخت یا تغییر وضعیت است و حذف مستقیم آن مجاز نیست.");
            var previousCash = await ShopOrderCashLedger.VerifyCurrentAsync(_db, t);
            if (await _db.ShopOrderCashEntries.IgnoreQueryFilters().AnyAsync(e => e.TransactionId == t.Id))
                throw new InvalidOperationException("فاکتور دارای تاریخچه نقدی است؛ حذف مستقیم آن برای حفظ صورت‌حساب دوره‌های قبل مجاز نیست.");
            if (!t.ShopCashLedgerEnabled && t.CashAmount > 0 && !allowUntrackedRollback)
                throw new InvalidOperationException("نقد فاکتور قدیمی تطبیق نشده است؛ حذف آن پیش از بررسی سوابق مالی مجاز نیست.");
            var productIds = t.Lines.Select(l => l.ProductId).Distinct().ToList();
            var wh = t.WarehouseId;

            await ShopOrderCashLedger.ReverseForDeleteAsync(_db, t, previousCash, user);
            _db.Transactions.Remove(t);
            await _db.SaveChangesAsync();  // ابتدا حذف اعمال می‌شود تا از بازمحاسبه خارج شود

            foreach (var pid in productIds)
                await RecalcStockAsync(wh, pid);

            await _db.SaveChangesAsync();

            // کنترل: حذف سند خرید نباید موجودی را منفی کند (مثلاً وقتی کالای آن قبلاً فروخته شده)
            try
            {
                await EnsureNoNegativeStockAsync(productIds.Select(pid => (wh, pid)));
            }
            catch
            {
                await trx.RollbackAsync();
                throw;
            }

            await trx.CommitAsync();
        }
        catch
        {
            try { if (_db.Database.CurrentTransaction is not null) await trx.RollbackAsync(); } catch { }
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    /// <summary>آخرین سند خرید شامل کالای موردنظر.</summary>
    public async Task<Order?> GetLastPurchaseAsync(int productId)
    {
        var t = await _db.Transactions.Include(x => x.Lines)
            .Where(x => x.Type == TransactionType.Purchase && x.Lines.Any(l => l.ProductId == productId))
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync();
        if (t is null) return null;
        return await ToOrderDtoAsync(t);
    }

    public async Task<decimal> SuggestPriceAsync(int productId, TransactionType type)
    {
        var p = await _db.Products.FindAsync(productId);
        if (p is null) return 0;
        if (type == TransactionType.Sale)
        {
            var prices = await ShopPricingService.ResolveAsync(_db, new[] { productId }, DateTime.Today);
            return prices.TryGetValue(productId, out var price) ? price.Price : 0m;
        }
        return p.PurchasePrice;
    }

    // =============================== کاردکس ===============================

    public async Task<List<KardexRow>> GetKardexAsync(int productId, int? warehouseId, DateTime? from, DateTime? to, Paging.Request? pagination = null)
    {
        var source = _db.Transactions.AsNoTracking()
            .Where(t => !warehouseId.HasValue || t.WarehouseId == warehouseId.Value)
            .SelectMany(t => t.Lines.Where(l => l.ProductId == productId), (t, l) => new
            {
                TransactionId = t.Id, LineId = l.Id, t.Date, t.Type, t.Number, t.PartyId, t.Description,
                l.Quantity, l.Price, l.UnitCostSnapshot
            });
        var filtered = source;
        if (from.HasValue) { var start = from.Value.Date; filtered = filtered.Where(r => r.Date >= start); }
        if (to.HasValue && to.Value.Date < DateTime.MaxValue.Date)
        {
            var end = to.Value.Date.AddDays(1);
            filtered = filtered.Where(r => r.Date < end);
        }
        var selected = await filtered.OrderBy(r => r.Date).ThenBy(r => r.TransactionId).ThenBy(r => r.LineId)
            .ToPageListAsync(pagination);
        if (selected.Count == 0) return new();

        // The first row's opening balance includes history before both the date
        // filter and offset. Adjustment/Initial replace (rather than add to) stock.
        var first = selected[0];
        var preceding = source.Where(r => r.Date < first.Date || (r.Date == first.Date &&
            (r.TransactionId < first.TransactionId || (r.TransactionId == first.TransactionId && r.LineId < first.LineId))));
        var reset = await preceding.Where(r => r.Type != TransactionType.Sale && r.Type != TransactionType.WarrantyOut && r.Type != TransactionType.Purchase && r.Type != TransactionType.SaleReturn)
            .OrderByDescending(r => r.Date).ThenByDescending(r => r.TransactionId).ThenByDescending(r => r.LineId)
            .FirstOrDefaultAsync();
        decimal balance = reset?.Quantity ?? 0;
        if (reset != null)
            preceding = preceding.Where(r => r.Date > reset.Date || (r.Date == reset.Date &&
                (r.TransactionId > reset.TransactionId || (r.TransactionId == reset.TransactionId && r.LineId > reset.LineId))));
        var deltas = preceding.Where(r => r.Type == TransactionType.Sale || r.Type == TransactionType.WarrantyOut || r.Type == TransactionType.Purchase || r.Type == TransactionType.SaleReturn)
            .Select(r => r.Type == TransactionType.Sale || r.Type == TransactionType.WarrantyOut ? -r.Quantity : r.Quantity);
        balance += _db.Database.IsSqlite()
            ? (decimal)Math.Round(await deltas.SumAsync(q => (double?)q) ?? 0, 3)
            : await deltas.SumAsync(q => (decimal?)q) ?? 0m;

        var partyIds = selected.Where(r => r.PartyId.HasValue).Select(r => r.PartyId!.Value).Distinct().ToList();
        var parties = await _db.Parties.AsNoTracking().Where(p => partyIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name);
        var rows = new List<KardexRow>();
        foreach (var r in selected)
        {
            decimal inQty = 0, outQty = 0;
            if (r.Type is TransactionType.Sale or TransactionType.WarrantyOut) { outQty = r.Quantity; balance -= r.Quantity; }
            else if (r.Type == TransactionType.Purchase || r.Type == TransactionType.SaleReturn) { inQty = r.Quantity; balance += r.Quantity; }
            else
            {
                var delta = r.Quantity - balance;
                if (delta >= 0) inQty = delta; else outQty = -delta;
                balance = r.Quantity;
            }
            rows.Add(new KardexRow
            {
                Date = r.Date, Type = r.Type, DocumentNo = r.Number,
                PartyName = r.PartyId.HasValue && parties.TryGetValue(r.PartyId.Value, out var name) ? name : "",
                Description = r.Description ?? "", InQty = inQty, OutQty = outQty,
                Balance = balance, UnitPrice = r.Type == TransactionType.WarrantyOut ? (r.UnitCostSnapshot ?? 0m) : r.Price
            });
        }
        return rows;
    }

    // =============================== نقطه سفارش ===============================

    public async Task<List<ReorderItem>> GetReorderAsync(int? warehouseId, Paging.Request? pagination = null)
    {
        var products = _db.Products.AsNoTracking().Where(p => p.IsActive && p.ReorderPoint > 0);
        var stocks = _db.Stocks.AsNoTracking().Where(s => !warehouseId.HasValue || s.WarehouseId == warehouseId.Value);
        if (_db.Database.IsSqlite())
        {
            // SQLite has no decimal SUM. Quantities are stored at three decimal places.
            var quantities = products.Select(p => new
            {
                Product = p,
                Quantity = Math.Round(stocks.Where(s => s.ProductId == p.Id).Sum(s => (double?)s.Quantity) ?? 0, 3)
            });
            return await quantities.Where(x => x.Quantity <= (double)x.Product.ReorderPoint)
                .OrderByDescending(x => (double)x.Product.ReorderPoint - x.Quantity).ThenBy(x => x.Product.Id)
                .Select(x => new ReorderItem
                {
                    ProductId = x.Product.Id, ProductCode = x.Product.Code, ProductName = x.Product.Name,
                    Unit = x.Product.Unit, Category = x.Product.Category, ReorderPoint = x.Product.ReorderPoint,
                    MaxStock = x.Product.MaxStock, TotalStock = (decimal)x.Quantity
                }).ToPageListAsync(pagination);
        }
        var query = products.Select(p => new
        {
            Product = p,
            Quantity = stocks.Where(s => s.ProductId == p.Id).Sum(s => (decimal?)s.Quantity) ?? 0m
        });
        return await query.Where(x => x.Quantity <= x.Product.ReorderPoint)
            .OrderByDescending(x => x.Product.ReorderPoint - x.Quantity).ThenBy(x => x.Product.Id)
            .Select(x => new ReorderItem
            {
                ProductId = x.Product.Id, ProductCode = x.Product.Code, ProductName = x.Product.Name,
                Unit = x.Product.Unit, Category = x.Product.Category, ReorderPoint = x.Product.ReorderPoint,
                MaxStock = x.Product.MaxStock, TotalStock = x.Quantity
            }).ToPageListAsync(pagination);
    }

    // =============================== داشبورد ===============================

    private async Task<Dictionary<int, decimal>> LoadSaleChequeIncomesAsync(DateTime? from = null)
    {
        var query = _db.SaleChequeCharges.AsNoTracking()
            .Where(c => c.Transaction.Type == TransactionType.Sale);
        if (from.HasValue) query = query.Where(c => c.Transaction.Date >= from.Value);
        return await query.ToDictionaryAsync(c => c.TransactionId, c => c.Amount);
    }

    private static decimal GoodsAmount(Db.Transaction sale, IReadOnlyDictionary<int, decimal> incomes)
        => SaleChequeChargeMath.GoodsTotal(sale.Amount, incomes.GetValueOrDefault(sale.Id));

    public async Task<DashboardSummary> GetDashboardAsync()
    {
        var now = DateTime.Now;
        var products = await _db.Products.CountAsync();
        var warehouses = await _db.Warehouses.CountAsync();
        var customers = await _db.Parties.CountAsync(p => p.Type == PartyType.Customer);
        var suppliers = await _db.Parties.CountAsync(p => p.Type == PartyType.Supplier);

        var stocks = await _db.Stocks.ToListAsync();
        var productList = await _db.Products.Where(p => p.IsActive).ToListAsync();
        int below = 0;
        foreach (var p in productList.Where(p => p.ReorderPoint > 0))
        {
            var qty = stocks.Where(s => s.ProductId == p.Id).Sum(s => s.Quantity);
            if (qty <= p.ReorderPoint) below++;
        }

        var invValue = stocks.Sum(s => s.Quantity * s.AvgCost);

        var txns = await _db.Transactions.ToListAsync();
        var chequeIncomes = await LoadSaleChequeIncomesAsync();
        var todaySales = txns.Where(t => t.Type == TransactionType.Sale && t.Date.Date == now.Date).Sum(t => GoodsAmount(t, chequeIncomes))
                       - txns.Where(t => t.Type == TransactionType.SaleReturn && t.Date.Date == now.Date).Sum(t => t.Amount);
        var todayPurchases = txns.Where(t => t.Type == TransactionType.Purchase && t.Date.Date == now.Date).Sum(t => t.Amount);

        var (jy, jm, _) = PersianDate.FromGregorian(now);
        var monthStart = PersianDate.ToGregorian(jy, jm, 1);
        var monthEnd = PersianDate.ToGregorian(jy, jm == 12 ? 1 : jm + 1, 1).AddDays(-1);

        var monthSales = txns.Where(t => t.Type == TransactionType.Sale && t.Date >= monthStart && t.Date <= monthEnd).Sum(t => GoodsAmount(t, chequeIncomes))
                       - txns.Where(t => t.Type == TransactionType.SaleReturn && t.Date >= monthStart && t.Date <= monthEnd).Sum(t => t.Amount);
        var monthPurchases = txns.Where(t => t.Type == TransactionType.Purchase && t.Date >= monthStart && t.Date <= monthEnd).Sum(t => t.Amount);

        return new DashboardSummary
        {
            ProductCount = products,
            WarehouseCount = warehouses,
            CustomerCount = customers,
            SupplierCount = suppliers,
            BelowReorderCount = below,
            InventoryValue = invValue,
            TodaySales = todaySales,
            TodayPurchases = todayPurchases,
            MonthSales = monthSales,
            MonthPurchases = monthPurchases,
            LowStockThreshold = 0
        };
    }

    public async Task<List<RecentActivity>> GetRecentAsync(int count)
    {
        var txns = await _db.Transactions.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).Take(count).ToListAsync();
        var parties = await _db.Parties.ToDictionaryAsync(p => p.Id);

        return txns.Select(t => new RecentActivity
        {
            Date = t.Date,
            Type = t.Type.ToString(),
            Number = t.Number,
            PartyName = t.PartyId.HasValue && parties.TryGetValue(t.PartyId.Value, out var p) ? p.Name : "",
            Amount = t.Amount
        }).ToList();
    }

    /// <summary>خروج قطعات تحویل‌شده گارانتی بدون فروش، وجه، پورسانت یا درآمد.</summary>
    public async Task<int?> ConsumeWarrantyStockAsync(int warehouseId, int partyId, string warrantyNumber, DateTime at,
        IReadOnlyCollection<ShopWarrantyItemDto> supplied)
    {
        var tx = _db.Database.CurrentTransaction;
        if (tx is null || tx.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("خروج گارانتی باید در تراکنش سریال‌پذیر پذیرش انجام شود.");
        if (supplied.Count == 0) return null;
        if (warehouseId <= 0 || partyId <= 0 || at == default || string.IsNullOrWhiteSpace(warrantyNumber) ||
            supplied.Any(i => i.ProductId is not > 0 || !i.Supplied || i.Quantity <= 0 ||
                i.Quantity != decimal.Round(i.Quantity, 2) || i.UnitCost < 0 ||
                i.UnitCost > 9999999999999999.99m || decimal.Round(i.UnitCost, 2) != i.UnitCost))
            throw new InvalidOperationException("مشخصات کالای مصرف‌شده گارانتی معتبر نیست.");
        var ids = supplied.Select(i => i.ProductId!.Value).Distinct().ToList();
        var products = await _db.Products.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var cmd = new OrderCommand { Type = TransactionType.WarrantyOut, WarehouseId = warehouseId, PartyId = partyId,
            Lines = supplied.Select(i => new OrderLineInput { ProductId = i.ProductId!.Value, Quantity = i.Quantity, Price = 0 }).ToList() };
        await ValidateOrderReferencesAsync(cmd, ids, products.Count);
        if (products.Values.Any(p => p.IsService || !p.IsActive || (p.WarehouseId is > 0 && p.WarehouseId != warehouseId)))
            throw new InvalidOperationException("کالای مصرف‌شده باید کالای فعال و متعلق به انبار انتخاب‌شده باشد.");
        await ValidateShopLineRulesAsync(cmd, products);
        if (await _db.Transactions.AnyAsync(t => t.WarehouseId == warehouseId && t.Date > at && t.Lines.Any(l => ids.Contains(l.ProductId))))
            throw new InvalidOperationException("برای کالای گارانتی گردش آینده ثبت شده است؛ پیش از خروج، ترتیب موجودی را بررسی کنید.");
        var stocks = await _db.Stocks.Where(s => s.WarehouseId == warehouseId && ids.Contains(s.ProductId)).ToListAsync();
        foreach (var group in supplied.GroupBy(i => i.ProductId!.Value))
        {
            var available = stocks.FirstOrDefault(s => s.ProductId == group.Key)?.Quantity ?? 0m;
            if (available < group.Sum(i => i.Quantity))
                throw new InvalidOperationException($"موجودی «{products[group.Key].Name}» برای خروج گارانتی کافی نیست.");
        }
        var movement = new Db.Transaction
        {
            Type = TransactionType.WarrantyOut, Number = $"WG-{warrantyNumber}", Date = at,
            WarehouseId = warehouseId, PartyId = partyId, Description = $"خروج گارانتی — پذیرش {warrantyNumber}",
            Amount = 0m, CashAmount = 0m, SettledAmount = 0m, ShopCashLedgerEnabled = false,
            Lines = supplied.Select(i => new Db.TransactionLine { ProductId = i.ProductId!.Value,
                Quantity = i.Quantity, Price = 0m, UnitCostSnapshot = i.UnitCost, Description = i.Description }).ToList()
        };
        _db.Transactions.Add(movement);
        await _db.SaveChangesAsync();
        foreach (var id in ids) await RecalcStockAsync(warehouseId, id);
        await _db.SaveChangesAsync();
        await EnsureNoNegativeStockAsync(ids.Select(id => (warehouseId, id)));
        return movement.Id;
    }

    // =============================== ابزار ===============================

    /// <summary>
    /// اعتبارسنجی و اعمال اطلاعات پرداخت روی سند فروش (نقدی/نسیه/چک/اقساط).
    /// پرداخت ترکیبی: در نسیه/چک/اقساط می‌توان «پیش‌دریافت نقدی» هم داشت؛
    /// در این حالت جمع چک‌ها/اقساط باید برابر «مبلغ فاکتور منهای نقد» باشد.
    /// </summary>
    /// <summary>
    /// ثبت پرداخت سند (خرید/فروش) به‌صورت «ترکیبی»:
    /// هر سند می‌تواند هم‌زمان چند جزء داشته باشد — نقد (پیش‌دریافت) + چند چک + چند قسط.
    /// اگر جمع اجزا کمتر از مبلغ سند باشد، مابقی «نسیه» با تاریخ سررسید ثبت می‌شود؛
    /// اگر بیشتر باشد، مازاد همان سود مدت‌دار (چک/اقساط) است و پذیرفته می‌شود.
    /// روش پرداخت مؤثر خودکار تعیین می‌شود: تک‌جزء ⇒ همان جزء، چندجزء ⇒ Mixed.
    /// </summary>
    private async Task ValidateShopLineRulesAsync(OrderCommand cmd, Dictionary<int, Db.Product> products,
        IReadOnlyDictionary<int, Db.TransactionLine>? originals = null)
    {
        var existingIds = cmd.Lines.Where(l => l.Id > 0).Select(l => l.Id).ToList();
        if (existingIds.Count != existingIds.Distinct().Count())
            throw new InvalidOperationException("شناسه سطر فاکتور تکراری است.");
        var rules = await ResolveUnitRulesAsync(products.Values);
        foreach (var line in cmd.Lines)
        {
            var old = originals is not null && originals.TryGetValue(line.Id, out var saved) ? saved : null;
            if (line.Quantity > ShopInvoiceAmounts.MaximumQuantity &&
                !(old is not null && old.ProductId == line.ProductId && old.Quantity == line.Quantity))
                throw new InvalidOperationException("مقدار سطر خارج از محدوده قابل ثبت است.");
            if (!ShopInvoiceLineMath.SameBasis(line, old) && !ShopInvoiceAmounts.IsWholePrice(line.Price))
                throw new InvalidOperationException("قیمت هر سطر باید عددی غیرمنفی و بدون اعشار، به ریال صحیح باشد.");
            if (!products.TryGetValue(line.ProductId, out var product) || product.IsService) continue;
            // Preserve a historical quantity only when that same product and quantity remain unchanged.
            if (old is not null && old.ProductId == line.ProductId && old.Quantity == line.Quantity) continue;
            var allows = UnitRule(product, rules);
            if (!allows.HasValue)
                throw new InvalidOperationException($"نوع مقدار واحد «{product.Unit}» برای «{product.Name}» مشخص نیست؛ آن را در واحدهای شمارش تعیین کنید.");
            if (!allows.Value && !ShopQuantityRules.IsWhole(line.Quantity))
                throw new InvalidOperationException($"مقدار «{product.Name}» با واحد «{product.Unit}» باید عدد صحیح باشد.");
        }
    }

    private static void ValidateTaxedLinePrecision(IEnumerable<OrderLineInput> lines)
    {
        // SQL Server stores price and discount percent with two decimals. Reject values
        // which would otherwise make the persisted line disagree with its VAT snapshot.
        if (lines.Any(l => l.VatValue.GetValueOrDefault() > 0 &&
            (l.Price != decimal.Round(l.Price, 2) || (!l.DiscountIsAmount && l.DiscountPercent != decimal.Round(l.DiscountPercent, 2)))))
            throw new InvalidOperationException("در سطر مشمول ارزش افزوده، قیمت و درصد تخفیف حداکثر دو رقم اعشار دارند.");
    }

    private async Task ValidateOrderReferencesAsync(OrderCommand cmd, IReadOnlyCollection<int> productIds, int visibleProductCount)
    {
        if (visibleProductCount != productIds.Count || productIds.Any(id => id <= 0))
            throw new InvalidOperationException("یکی از کالاها متعلق به شرکت جاری نیست یا یافت نشد.");
        if (!await _db.Warehouses.AnyAsync(w => w.Id == cmd.WarehouseId) || !await _db.Parties.AnyAsync(p => p.Id == cmd.PartyId))
            throw new InvalidOperationException("انبار یا طرف حساب متعلق به شرکت جاری نیست یا یافت نشد.");
        if (cmd.ReferrerId is > 0 && !await _db.Referrers.AnyAsync(r => r.Id == cmd.ReferrerId))
            throw new InvalidOperationException("معرف متعلق به شرکت جاری نیست یا یافت نشد.");
        var accounts = cmd.Cheques.Where(c => c.TrsAccountId is > 0).Select(c => c.TrsAccountId!.Value).Distinct().ToList();
        foreach (var id in accounts)
        {
            var account = await _db.TrsAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id && a.IsActive)
                ?? throw new InvalidOperationException("حساب بانکی چک فعال نیست یا یافت نشد.");
            await SaleChequeTreasuryGuard.VerifyCompanyOwnershipAsync(_db, account,
                _db.ActiveCompanyId is > 0 ? _db.ActiveCompanyId : _db.ResolveDefaultCompanyId());
        }
    }

    private static void ApplyPayment(Db.Transaction txn, OrderCommand cmd, decimal? paymentTotal = null)
    {
        var total = paymentTotal ?? cmd.Lines.Sum(l => OrderLineAmounts.Total(l.Quantity, l.Price, l.DiscountPercent, l.DiscountAmount, l.DiscountIsAmount, l.VatValue ?? 0));

        txn.TrsAccountId = cmd.TrsAccountId > 0 ? cmd.TrsAccountId : null;
        txn.CashType = null;
        txn.CashAmount = 0;
        txn.DueDate = null;
        txn.SettledAmount = 0;
        // Preserve a known original date only for the same persisted cheque ID.
        // For legacy clients that omit IDs on edits, do not invent historical dates.
        var originalDueDates = txn.Cheques.Where(c => c.Id > 0).ToDictionary(c => c.Id, c => c.OriginalDueDate);
        txn.Cheques.Clear();
        txn.Installments.Clear();

        // ---------- ۱) جزء نقدی ----------
        // انتخاب صریح «نقدی» یعنی تمام مبلغ نقد است؛ در بقیهٔ روش‌ها نقد یک جزء اختیاری است.
        var cash = cmd.PaymentMethod == PaymentMethod.Cash ? total : Math.Max(0, cmd.CashAmount);
        if (cmd.PaymentMethod != PaymentMethod.Cash && cash > 0 && cash >= total && total > 0)
            throw new InvalidOperationException("پیش‌دریافت نقدی نمی‌تواند برابر یا بیشتر از مبلغ فاکتور باشد؛ در این صورت روش پرداخت را «نقدی» انتخاب کنید.");

        // ---------- ۲) جزء چک ----------
        var chequeSum = 0m;
        foreach (var c in cmd.Cheques)
        {
            if (string.IsNullOrWhiteSpace(c.Number))
                throw new InvalidOperationException("شماره چک را وارد کنید.");
            if (c.Amount <= 0)
                throw new InvalidOperationException("مبلغ چک باید بزرگ‌تر از صفر باشد.");
            if (c.DueDate == default)
                throw new InvalidOperationException("تاریخ سررسید چک را وارد کنید.");
            chequeSum += c.Amount;
            txn.Cheques.Add(new Db.Cheque
            {
                Number = c.Number.Trim(),
                TrsAccountId = c.TrsAccountId > 0 ? c.TrsAccountId : null,
                BankName = string.IsNullOrWhiteSpace(c.BankName) ? null : c.BankName.Trim(),
                AccountInfo = string.IsNullOrWhiteSpace(c.AccountInfo) ? null : c.AccountInfo.Trim(),
                OwnerName = string.IsNullOrWhiteSpace(c.OwnerName) ? null : c.OwnerName.Trim(),
                Amount = c.Amount,
                DueDate = c.DueDate.Date,
                OriginalDueDate = txn.Id == 0 ? c.DueDate.Date :
                    c.Id > 0 && originalDueDates.TryGetValue(c.Id, out var originalDate) ? originalDate : null,
                IsCleared = c.IsCleared,
                ClearedAt = c.IsCleared ? (c.ClearedAt ?? DateTime.Now) : null,
                Note = string.IsNullOrWhiteSpace(c.Note) ? null : c.Note.Trim(),
                CreatedAt = DateTime.Now
            });
        }
        if (cmd.PaymentMethod == PaymentMethod.Cheque && txn.Cheques.Count == 0)
            throw new InvalidOperationException("حداقل یک چک ثبت کنید.");

        // ---------- ۳) جزء اقساط ----------
        var instSum = 0m;
        var no = 1;
        foreach (var i in cmd.Installments.OrderBy(x => x.DueDate))
        {
            if (i.Amount <= 0)
                throw new InvalidOperationException("مبلغ هر قسط باید بزرگ‌تر از صفر باشد.");
            if (i.DueDate == default)
                throw new InvalidOperationException("تاریخ سررسید هر قسط را وارد کنید.");
            instSum += i.Amount;
            txn.Installments.Add(new Db.InstallmentLine
            {
                No = no++,
                Amount = i.Amount,
                DueDate = i.DueDate.Date,
                IsPaid = i.IsPaid,
                PaidAt = i.IsPaid ? (i.PaidAt ?? DateTime.Now) : null,
                CreatedAt = DateTime.Now
            });
        }
        if (cmd.PaymentMethod == PaymentMethod.Installment && txn.Installments.Count == 0)
            throw new InvalidOperationException("حداقل یک قسط تعریف کنید.");

        // ---------- ۴) باقی‌مانده = نسیه ----------
        var paid = cash + chequeSum + instSum;
        var remainder = total - paid;
        var creditRemainder = remainder > (cmd.IsWarrantyInvoice ? 0m : PaymentMix.Tolerance) ? remainder : 0;

        if (creditRemainder > 0)
        {
            if (!cmd.DueDate.HasValue)
                throw new InvalidOperationException(
                    $"جمع پرداخت‌ها ({paid:#,0}) کمتر از مبلغ سند ({total:#,0}) است؛ " +
                    "برای ثبت مابقی به‌صورت نسیه، تاریخ سررسید نسیه را وارد کنید.");
            txn.DueDate = cmd.DueDate.Value.Date;
        }

        // ---------- ۵) ثبت اجزای نقدی و روش مؤثر ----------
        if (cash > 0)
        {
            txn.CashAmount = cash;
            txn.CashType = cmd.CashType ?? Inventory.Shared.CashType.Cash;
            txn.SettledAmount = cash; // بخش نقدی همان ابتدا تسویه شده است
        }
        else if (cmd.PaymentMethod == PaymentMethod.Cash)
        {
            txn.CashType = cmd.CashType ?? Inventory.Shared.CashType.Cash;
        }

        // Missing values from older clients preserve an existing reference on edits.
        var reference = cmd.CashTrackingNumber?.Trim();
        if (reference?.Length > 100) throw new InvalidOperationException("شماره پیگیری حداکثر ۱۰۰ نویسه است.");
        txn.CashTrackingNumber = txn.Type == TransactionType.Sale && cash > 0 && txn.CashType == CashType.CardTransfer
            ? reference ?? txn.CashTrackingNumber : null;
        txn.PaymentMethod = PaymentMix.Derive(cash, chequeSum, instSum, creditRemainder);
    }

    public async Task<AdminDashboard> GetAdminDashboardAsync()
    {
        var now = DateTime.Now;
        var today = now.Date;
        var tomorrow = today.AddDays(1);

        // بازه‌های شمسی: هفته از شنبه، ماه از ۱ ماه شمسی، فصل از ماه ۱/۴/۷/۱۰
        var dow = (int)today.DayOfWeek; // Saturday=6
        var weekStart = today.AddDays(-((dow + 1) % 7));
        var (jy, jm, _) = PersianDate.FromGregorian(today);
        var monthStart = PersianDate.ToGregorian(jy, jm, 1);
        var quarterFirstMonth = jm <= 3 ? 1 : jm <= 6 ? 4 : jm <= 9 ? 7 : 10;
        var quarterStart = PersianDate.ToGregorian(jy, quarterFirstMonth, 1);
        var m6y = jy; var m6m = jm;
        for (var i = 0; i < 5; i++) { m6m--; if (m6m == 0) { m6m = 12; m6y--; } }
        var sixMonthStart = PersianDate.ToGregorian(m6y, m6m, 1);
        var chequeIncomes = await LoadSaleChequeIncomesAsync(sixMonthStart < quarterStart ? sixMonthStart : quarterStart);

        // A week or the 30-day window may cross the current Persian quarter boundary.
        var salesStart = new[] { quarterStart, weekStart, today.AddDays(-29) }.Min();
        var sales = await _db.Transactions
            .Include(t => t.Cheques)
            .Include(t => t.Installments)
            .Where(t => t.Type == TransactionType.Sale && t.Date >= salesStart && t.Date < tomorrow)
            .ToListAsync();
        var returnsQuarter = await _db.Transactions.AsNoTracking()
            .Where(t => t.Type == TransactionType.SaleReturn && t.Date >= salesStart && t.Date < tomorrow)
            .ToListAsync();

        var dash = new AdminDashboard
        {
            SalesToday = sales.Where(t => t.Date.Date == today).Sum(t => GoodsAmount(t, chequeIncomes)) - returnsQuarter.Where(r => r.Date.Date == today).Sum(r => r.Amount),
            SalesWeek = sales.Where(t => t.Date >= weekStart).Sum(t => GoodsAmount(t, chequeIncomes)) - returnsQuarter.Where(r => r.Date >= weekStart).Sum(r => r.Amount),
            SalesMonth = sales.Where(t => t.Date >= monthStart).Sum(t => GoodsAmount(t, chequeIncomes)) - returnsQuarter.Where(r => r.Date >= monthStart).Sum(r => r.Amount),
            SalesQuarter = sales.Where(t => t.Date >= quarterStart).Sum(t => GoodsAmount(t, chequeIncomes))
        };
        dash.SalesQuarter -= returnsQuarter.Where(r => r.Date >= quarterStart).Sum(r => r.Amount);

        // سود دوره‌ای بعد از محاسبه profitById (پایین‌تر) انجام می‌شود

        // ---------- داده نمودارها ----------
        // روند ۳۰ روز اخیر (فروش + سود) — سود از GetOrderAsync قبلاً برای فصل محاسبه شده؛
        // بازهٔ خواندن از ابتدای فصل یا ۲۹ روز قبل (هرکدام قدیمی‌تر است) شروع می‌شود.
        var profitById = new Dictionary<int, decimal>();
        foreach (var t in sales)
        {
            var o = await GetOrderAsync(t.Id);
            profitById[t.Id] = o?.TotalProfit ?? 0;
        }

        // سود دوره‌ای (با روش قیمت‌گذاری تنظیمات)
        foreach (var t in sales)
        {
            var profit = profitById.TryGetValue(t.Id, out var pv0) ? pv0 : 0;
            if (t.Date.Date == today) dash.ProfitToday += profit;
            if (t.Date >= weekStart) dash.ProfitWeek += profit;
            if (t.Date >= monthStart) dash.ProfitMonth += profit;
            if (t.Date >= quarterStart) dash.ProfitQuarter += profit;
        }

        for (var d = 29; d >= 0; d--)
        {
            var day = today.AddDays(-d);
            var dayTxns = sales.Where(t => t.Date.Date == day).ToList();
            var (djy, djm, djd) = PersianDate.FromGregorian(day);
            dash.DailyTrend.Add(new TrendPoint
            {
                Label = $"{djm}/{djd}",
                Sales = dayTxns.Sum(t => GoodsAmount(t, chequeIncomes)) - returnsQuarter.Where(r => r.Date.Date == day).Sum(r => r.Amount),
                Profit = dayTxns.Sum(t => profitById.TryGetValue(t.Id, out var pv) ? pv : 0)
            });
        }

        // ۶ ماه شمسی اخیر — نیاز به همه اسناد ۶ ماه (فراتر از فصل)
        var sales6m = await _db.Transactions
            .Where(t => t.Type == TransactionType.Sale && t.Date >= sixMonthStart && t.Date < tomorrow)
            .ToListAsync();
        var returns6m = await _db.Transactions.AsNoTracking()
            .Where(t => t.Type == TransactionType.SaleReturn && t.Date >= sixMonthStart && t.Date < tomorrow)
            .ToListAsync();
        var profit6m = new Dictionary<int, decimal>(profitById);
        foreach (var t in sales6m.Where(t => !profit6m.ContainsKey(t.Id)))
        {
            var o = await GetOrderAsync(t.Id);
            profit6m[t.Id] = o?.TotalProfit ?? 0;
        }
        var my = m6y; var mm = m6m;
        for (var i = 0; i < 6; i++)
        {
            var mStart = PersianDate.ToGregorian(my, mm, 1);
            var (ny, nm) = PersianDate.AddMonths(my, mm, 1);
            var mEnd = PersianDate.ToGregorian(ny, nm, 1);
            var mTxns = sales6m.Where(t => t.Date >= mStart && t.Date < mEnd).ToList();
            dash.MonthlyTrend.Add(new TrendPoint
            {
                Label = PersianDate.MonthName(mm),
                Sales = mTxns.Sum(t => GoodsAmount(t, chequeIncomes)) - returns6m.Where(r => r.Date >= mStart && r.Date < mEnd).Sum(r => r.Amount),
                Profit = mTxns.Sum(t => profit6m.TryGetValue(t.Id, out var pv) ? pv : 0)
            });
            my = ny; mm = nm;
        }

        // تفکیک روش پرداخت (فصل جاری) — بر اساس «اجزای واقعی» پرداخت تا اسناد ترکیبی هم درست شمرده شوند
        static decimal CashOf(Db.Transaction t) => t.PaymentMethod == PaymentMethod.Cash ? t.Amount : t.CashAmount;
        static decimal ChequeOf(Db.Transaction t) => t.Cheques.Where(c => c.Status != ChequeStatus.Cancelled).Sum(c => c.Amount);
        static decimal InstOf(Db.Transaction t) => t.Installments.Sum(i => i.Amount);
        static decimal CreditOf(Db.Transaction t, decimal customerCredit, decimal returnedGoods = 0m) =>
            Math.Max(0, t.Amount - customerCredit - CashOf(t) - ChequeOf(t) - InstOf(t) - returnedGoods);
        var quarterIds = sales.Where(t => t.Date >= quarterStart).Select(t => t.Id).ToList();
        var quarterCredits = quarterIds.Count == 0 ? new Dictionary<int, decimal>() :
            (await _db.SaleChequeCharges.AsNoTracking().Where(c => quarterIds.Contains(c.TransactionId))
                .Select(c => new { c.TransactionId, c.CustomerCredit }).ToListAsync())
                .ToDictionary(c => c.TransactionId, c => c.CustomerCredit);
        var quarterReturnedGoods = quarterIds.Count == 0 ? new Dictionary<int, decimal>() :
            (await _db.SaleChequeReturns.AsNoTracking().Where(r => quarterIds.Contains(r.SaleTransactionId))
                .Select(r => new { r.SaleTransactionId, Total = r.GoodsAmount + r.VatAmount }).ToListAsync())
                .GroupBy(r => r.SaleTransactionId).ToDictionary(g => g.Key, g => g.Sum(x => x.Total));

        var cancelledAll = (await _db.SaleChequeCancellations.AsNoTracking()
            .Select(c => c.SaleTransactionId).ToListAsync()).ToHashSet();
        var activeQuarterSales = sales.Where(t => t.Date >= quarterStart && !cancelledAll.Contains(t.Id)).ToList();
        dash.PayCash = activeQuarterSales.Sum(CashOf);
        dash.PayCheque = activeQuarterSales.Sum(ChequeOf);
        dash.PayInstallment = activeQuarterSales.Sum(InstOf);
        dash.PayCredit = activeQuarterSales.Sum(t => CreditOf(t, quarterCredits.GetValueOrDefault(t.Id), quarterReturnedGoods.GetValueOrDefault(t.Id)));

        var partyNames = await _db.Parties.ToDictionaryAsync(p => p.Id, p => p);

        // بدهکاران سررسیدشده: نسیه‌های پرداخت‌نشده + اقساط سررسیدشده
        // نسیهٔ سررسیدشده — شامل ماندهٔ نسیهٔ اسناد ترکیبی (بعد از کسر نقد/چک/اقساط)
        var creditTxns = await _db.Transactions
            .Include(t => t.Cheques)
            .Include(t => t.Installments)
            .Where(t => t.Type == TransactionType.Sale &&
                        t.DueDate != null && t.DueDate < tomorrow)
            .ToListAsync();
        creditTxns = creditTxns.Where(t => !cancelledAll.Contains(t.Id)).ToList();
        var overdueIds = creditTxns.Select(t => t.Id).ToList();
        var overdueCharges = overdueIds.Count == 0 ? new Dictionary<int, decimal>() :
            (await _db.SaleChequeCharges.AsNoTracking().Where(c => overdueIds.Contains(c.TransactionId))
                .Select(c => new { c.TransactionId, c.CustomerCredit }).ToListAsync())
                .ToDictionary(c => c.TransactionId, c => c.CustomerCredit);
        var overdueReturnedGoods = overdueIds.Count == 0 ? new Dictionary<int, decimal>() :
            (await _db.SaleChequeReturns.AsNoTracking().Where(r => overdueIds.Contains(r.SaleTransactionId))
                .Select(r => new { r.SaleTransactionId, Total = r.GoodsAmount + r.VatAmount }).ToListAsync())
                .GroupBy(r => r.SaleTransactionId).ToDictionary(g => g.Key, g => g.Sum(x => x.Total));
        var collectedCheques = new Dictionary<int, decimal>();
        if (overdueIds.Count > 0)
        {
            var overdueReceipts = await _db.SaleChequeReceipts.AsNoTracking()
                .Where(r => overdueIds.Contains(r.TransactionId) && r.ReversalDate == null)
                .Select(r => new { r.TransactionId, r.Amount }).ToListAsync();
            collectedCheques = overdueReceipts.GroupBy(r => r.TransactionId).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
        }
        foreach (var t in creditTxns)
        {
            var credit = CreditOf(t, overdueCharges.GetValueOrDefault(t.Id), overdueReturnedGoods.GetValueOrDefault(t.Id));
            // SettledAmount already contains initial cash and, on charged sales, collected cheques.
            // Deduct them once, not a second time from the overdue credit component.
            var paidInstallments = t.Installments.Where(i => i.IsPaid).Sum(i => i.Amount);
            var paidCredit = Math.Max(0, t.SettledAmount - CashOf(t) - paidInstallments - collectedCheques.GetValueOrDefault(t.Id));
            var outstandingCredit = Math.Max(0, credit - paidCredit);
            if (outstandingCredit == 0) continue;
            var party = t.PartyId.HasValue && partyNames.TryGetValue(t.PartyId.Value, out var pp) ? pp : null;
            dash.OverdueDebtors.Add(new DebtorItem
            {
                PartyId = t.PartyId ?? 0,
                PartyName = party?.Name ?? "—",
                Mobile = party?.Mobile,
                Number = t.Number,
                TransactionId = t.Id,
                Kind = "نسیه",
                Amount = outstandingCredit,
                DueDate = t.DueDate!.Value,
                DaysOverdue = (int)(today - t.DueDate.Value.Date).TotalDays
            });
        }

        var dueInstallments = await _db.Installments
            .Where(i => !i.IsPaid && i.DueDate < tomorrow)
            .ToListAsync();
        if (dueInstallments.Count > 0)
        {
            var instTxnIds = dueInstallments.Select(i => i.TransactionId).Distinct().ToList();
            var instTxns = await _db.Transactions.Where(t => instTxnIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
            foreach (var i in dueInstallments)
            {
                if (cancelledAll.Contains(i.TransactionId)) continue;
                if (!instTxns.TryGetValue(i.TransactionId, out var t) || t.Type != TransactionType.Sale) continue;
                var party = t.PartyId.HasValue && partyNames.TryGetValue(t.PartyId.Value, out var pp) ? pp : null;
                dash.OverdueDebtors.Add(new DebtorItem
                {
                    PartyId = t.PartyId ?? 0,
                    PartyName = party?.Name ?? "—",
                    Mobile = party?.Mobile,
                    Number = t.Number,
                    TransactionId = t.Id,
                    Kind = $"قسط {i.No}",
                    Amount = i.Amount,
                    DueDate = i.DueDate,
                    DaysOverdue = (int)(today - i.DueDate.Date).TotalDays
                });
            }
        }
        dash.OverdueDebtors = dash.OverdueDebtors.OrderByDescending(d => d.DaysOverdue).ThenByDescending(d => d.Amount).ToList();

        // چک‌های دریافتی از مشتری و چک‌های پرداختی ما (شامل سررسید امروز/گذشته یا برگشتی‌ها)
        // Use the same persisted shop cheque source as credit management. Do not mix
        // treasury IDs into these alerts: their settlement endpoints are different.
        var openCheques = await _db.Cheques.AsNoTracking()
            .Where(c => !c.IsCleared && c.Status != ChequeStatus.Cleared &&
                        c.Status != ChequeStatus.Cancelled && c.Status != ChequeStatus.Endorsed &&
                        !cancelledAll.Contains(c.TransactionId))
            .OrderBy(c => c.DueDate).ToListAsync();
        var openTxnIds = openCheques.Select(c => c.TransactionId).Distinct().ToList();
        var openTxnTypes = await _db.Transactions.AsNoTracking()
            .Where(t => openTxnIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Type);
        dash.PendingReceivedChequesCount = openCheques.Count(c => openTxnTypes.TryGetValue(c.TransactionId, out var type) && type == TransactionType.Sale);
        dash.PendingIssuedChequesCount = openCheques.Count(c => openTxnTypes.TryGetValue(c.TransactionId, out var type) && type == TransactionType.Purchase);
        var uncleanedCheques = openCheques.Where(c => c.DueDate < tomorrow || c.Status == ChequeStatus.Bounced).ToList();

        if (uncleanedCheques.Count > 0)
        {
            var chTxnIds = uncleanedCheques.Select(c => c.TransactionId).Distinct().ToList();
            var chTxns = await _db.Transactions.Where(t => chTxnIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
            foreach (var c in uncleanedCheques)
            {
                if (!chTxns.TryGetValue(c.TransactionId, out var t) ||
                    t.Type is not (TransactionType.Sale or TransactionType.Purchase)) continue;
                var party = t?.PartyId != null && partyNames.TryGetValue(t.PartyId.Value, out var pp) ? pp : null;
                var alertItem = new ChequeAlertItem
                {
                    ChequeId = c.Id,
                    Number = c.Number,
                    BankName = c.BankName,
                    OwnerName = c.OwnerName,
                    PartyName = party?.Name ?? "—",
                    OrderNumber = t?.Number ?? "",
                    Amount = c.Amount,
                    DueDate = c.DueDate,
                    Status = c.Status,
                    BounceReason = c.BounceReason
                };

                if (t != null && t.Type == TransactionType.Purchase)
                {
                    // چک پرداختی ما به فروشنده/تأمین‌کننده
                    dash.IssuedCheques.Add(alertItem);
                }
                else if (t?.Type == TransactionType.Sale)
                {
                    // چک دریافتی از مشتری (فروش)
                    dash.TodayCheques.Add(alertItem);
                }
            }
        }

        // Opening-balance/manual cheques are stored in treasury, not invoice Cheques.
        // Keep company query filters intact. Only read here; actions remain in their owning screen.
        var treasuryCheques = await _db.TrsCheques.AsNoTracking()
            .Where(c => (c.Status == ChequeStatus.InHand || c.Status == ChequeStatus.InCollection || c.Status == ChequeStatus.Bounced) &&
                        (c.DueDate < tomorrow || c.Status == ChequeStatus.Bounced))
            .OrderBy(c => c.DueDate).ToListAsync();
        foreach (var c in treasuryCheques)
        {
            var alert = new ChequeAlertItem
            {
                Source = "Treasury", ChequeId = c.Id, Number = c.Number, BankName = c.BankName,
                OwnerName = c.OwnerName, Amount = c.Amount, DueDate = c.DueDate, Status = c.Status,
                PartyName = c.PartyId.HasValue && partyNames.TryGetValue(c.PartyId.Value, out var party) ? party.Name : "—",
                OrderNumber = "ثبت مستقل / افتتاح حساب"
            };
            if (c.Kind == ChequeKind.Issued) dash.IssuedCheques.Add(alert);
            else dash.TodayCheques.Add(alert);
        }

        // Own issued expense cheques are a third source. Endorsed customer cheques
        // are not our issued cheques and must not be counted twice.
        var expenseCheques = await _db.ShopExpenseCheques.AsNoTracking()
            .Where(c => c.Kind == ExpenseChequeKind.Issued && c.State == ExpenseChequeState.Outstanding && c.DueDate < tomorrow)
            .OrderBy(c => c.DueDate).ToListAsync();
        foreach (var c in expenseCheques)
            dash.IssuedCheques.Add(new ChequeAlertItem
            {
                Source = "Expense", ChequeId = c.Id, Number = c.Number, BankName = c.BankName,
                PartyName = c.Recipient, Amount = c.Amount, DueDate = c.DueDate,
                Status = ChequeStatus.InHand, OrderNumber = "چک هزینه"
            });
        dash.TodayCheques = dash.TodayCheques.OrderBy(c => c.DueDate).ThenBy(c => c.Number).ToList();
        dash.IssuedCheques = dash.IssuedCheques.OrderBy(c => c.DueDate).ThenBy(c => c.Number).ToList();

        return dash;
    }

    public async Task<List<ReferrerProductItem>> GetReferrerProductsAsync(int referrerId, string? search, bool bypassFlag = false, Paging.Request? pagination = null)
    {
        var referrer = await _db.Referrers.FindAsync(referrerId)
            ?? throw new InvalidOperationException("معرف یافت نشد.");
        // bypassFlag: کاربر از طریق نقش‌های RBAC مجوز «محصولات من» را دارد
        if (!bypassFlag && !referrer.CanViewProducts)
            throw new InvalidOperationException("دسترسی مشاهده کالاها برای شما فعال نشده است.");

        var q = _db.Products.AsNoTracking().Where(p => p.IsActive && !p.IsService);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(p => p.Name.Contains(s) || p.Code.Contains(s) || (p.Category != null && p.Category.Contains(s)));
        }
        if (_db.Database.IsSqlite())
            q = q.Where(p => Math.Round(_db.Stocks.Where(s => s.ProductId == p.Id)
                .Sum(s => (double?)s.Quantity) ?? 0, 3) > 0);
        else
            q = q.Where(p => (_db.Stocks.Where(s => s.ProductId == p.Id)
                .Sum(s => (decimal?)s.Quantity) ?? 0) > 0);
        var products = await q.OrderBy(p => p.Name).ThenBy(p => p.Id).ToPageListAsync(pagination);
        return products
            .Select(p => new ReferrerProductItem
            {
                Name = p.Name,
                Code = p.Code,
                Unit = p.Unit,
                Category = p.Category,
                // قیمت فروش عمداً ارسال نمی‌شود — معرف فقط موجود بودن کالا را می‌بیند
                InStock = true
            }).ToList();
    }

    /// <summary>پیش‌نمایش تعدیل؛ سرور مبلغ را از شرایط ذخیره‌شده محاسبه می‌کند.</summary>
    public async Task<SaleChequeReschedulePreview> PreviewSaleChequeRescheduleAsync(int chequeId, DateTime expectedDueDate, DateTime newDueDate)
    {
        if (expectedDueDate == default || newDueDate == default || expectedDueDate.Date == newDueDate.Date)
            throw new InvalidOperationException("سررسید جدید معتبر و متفاوت از سررسید قبلی باشد.");
        var cheque = await _db.Cheques.AsNoTracking().FirstOrDefaultAsync(c => c.Id == chequeId)
            ?? throw new InvalidOperationException("چک یافت نشد.");
        if (cheque.DueDate.Date != expectedDueDate.Date)
            throw new InvalidOperationException("سررسید چک تغییر کرده است؛ اطلاعات را دوباره بارگذاری کنید.");
        if (cheque.IsCleared || cheque.Status is not (ChequeStatus.InHand or ChequeStatus.Bounced))
            throw new InvalidOperationException("تغییر سررسید فقط برای چک نزد صندوق یا برگشتی مجاز است.");
        var charge = await _db.SaleChequeCharges.AsNoTracking().FirstOrDefaultAsync(c => c.TransactionId == cheque.TransactionId)
            ?? throw new InvalidOperationException("ردیف تاخیر تادیه چک یافت نشد.");
        decimal effectiveBase, delta;
        try { (effectiveBase, delta) = await SaleChequeRescheduling.CalculatePlanAsync(_db, cheque, charge, newDueDate); }
        catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException)
        { throw new InvalidOperationException("شرایط یا مبلغ تعدیل سررسید معتبر نیست.", ex); }
        return new SaleChequeReschedulePreview { BaseAmount = effectiveBase, RatePercent = charge.RatePercent,
            PeriodDays = charge.PeriodDays, IncomeDelta = delta };
    }

    public async Task<SaleChequeReschedulePreview> RescheduleSaleChequeAsync(int chequeId, SaleChequeRescheduleCommand cmd, string? user)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var result = await SaleChequeRescheduling.ApplyAsync(_db, chequeId, cmd.OperationId,
            cmd.ExpectedDueDate, cmd.NewDueDate, cmd.OperationDate, user, cmd.ConfirmedIncomeDelta);
        await transaction.CommitAsync();
        return new SaleChequeReschedulePreview { BaseAmount = result.BaseAmount, RatePercent = result.RatePercent,
            PeriodDays = result.PeriodDays, IncomeDelta = result.IncomeDelta };
    }

    public async Task ReceiveSaleChequeAsync(int chequeId, SaleChequeReceiptCommand cmd, string? user)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await SaleChequeReceiptPosting.ReceiveAsync(_db, chequeId, cmd.OperationId, cmd.TrsAccountId, cmd.Date, user);
        await transaction.CommitAsync();
    }

    public async Task ReverseSaleChequeReceiptAsync(int chequeId, SaleChequeReceiptReversalCommand cmd, string? user)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await SaleChequeReceiptReversal.ReverseAsync(_db, chequeId, cmd.OperationId, cmd.Date, user);
        await transaction.CommitAsync();
    }

    public async Task<List<SaleChequeStatusHistoryItem>> GetSaleChequeStatusHistoryAsync(int chequeId)
    {
        var cheque = await _db.Cheques.AsNoTracking().FirstOrDefaultAsync(c => c.Id == chequeId)
            ?? throw new InvalidOperationException("چک یافت نشد.");
        if (!await _db.SaleChequeCharges.AnyAsync(c => c.TransactionId == cheque.TransactionId))
            throw new InvalidOperationException("ردیف تاخیر تادیه چک یافت نشد.");
        return await _db.SaleChequeStatusEvents.AsNoTracking().Where(e => e.ChequeId == chequeId)
            .OrderByDescending(e => e.Id).Select(e => new SaleChequeStatusHistoryItem
            {
                CreatedAt = e.CreatedAt, CreatedBy = e.CreatedBy, PreviousStatus = e.PreviousStatus,
                NewStatus = e.NewStatus, Reason = e.Reason
            }).ToListAsync();
    }

    public async Task ChangeSaleChequeStatusAsync(int chequeId, SaleChequeStatusCommand cmd, string? user)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        if (cmd.OperationId == Guid.Empty) throw new InvalidOperationException("شناسه عملیات الزامی است.");
        var reason = cmd.Bounced ? (string.IsNullOrWhiteSpace(cmd.Reason) ? "عدم موجودی کافی" : cmd.Reason.Trim()) : null;
        if (reason?.Length > 200 || user?.Length > 120)
            throw new InvalidOperationException("علت برگشت یا نام کاربر بیش از حد طولانی است.");
        var target = cmd.Bounced ? ChequeStatus.Bounced : ChequeStatus.InHand;
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var prior = await _db.SaleChequeStatusEvents.FirstOrDefaultAsync(e => e.OperationId == cmd.OperationId);
        if (prior is not null)
        {
            if (prior.ChequeId != chequeId || prior.NewStatus != target || prior.Reason != reason)
                throw new InvalidOperationException("شناسه عملیات قبلاً برای تغییر دیگری استفاده شده است.");
            await transaction.CommitAsync();
            return;
        }
        var cheque = await _db.Cheques.FirstOrDefaultAsync(c => c.Id == chequeId)
            ?? throw new InvalidOperationException("چک یافت نشد.");
        if (!await _db.SaleChequeCharges.AnyAsync(c => c.TransactionId == cheque.TransactionId))
            throw new InvalidOperationException("ردیف تاخیر تادیه چک یافت نشد.");
        var sale = await _db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == cheque.TransactionId)
            ?? throw new InvalidOperationException("فاکتور فروش یافت نشد.");
        await SaleChequeSettlementLedger.EnsureSettledAmountConsistentAsync(_db, sale);
        if (cheque.IsCleared || await _db.SaleChequeReceipts.AnyAsync(r => r.ChequeId == cheque.Id && r.ReversalDate == null))
            throw new InvalidOperationException("ابتدا وصول چک را لغو کنید.");
        if (cheque.Status is not (ChequeStatus.InHand or ChequeStatus.Bounced))
            throw new InvalidOperationException("وضعیت چک قابل تغییر نیست.");
        if (cheque.Status == target)
            throw new InvalidOperationException("چک هم‌اکنون در همین وضعیت است.");
        var oldStatus = cheque.Status;
        if (target == ChequeStatus.Bounced && oldStatus != target)
        {
            cheque.BouncedAt = DateTime.Now;
            cheque.BounceReason = reason;
        }
        cheque.Status = target;
        _db.SaleChequeStatusEvents.Add(new Db.SaleChequeStatusEvent
        {
            CompanyId = cheque.CompanyId, OperationId = cmd.OperationId, ChequeId = cheque.Id,
            PreviousStatus = oldStatus, NewStatus = target, CreatedAt = DateTime.Now, CreatedBy = user, Reason = reason
        });
        await _db.SaveChangesAsync();
        await transaction.CommitAsync(); // صرف برگشت چک هیچ تغییر سودی ایجاد نمی‌کند.
    }

    public async Task<SaleChequeReturnPreviewDto> PreviewSaleChequeReturnAsync(int saleId, SaleChequeReturnPreviewRequest req)
    {
        ArgumentNullException.ThrowIfNull(req);
        await RejectLinkedWarrantyInvoiceMutationAsync(saleId);
        var inputs = req.Lines.Select(l => new SaleChequeReturnLineInput(l.OriginalLineId, l.Quantity)).ToList();
        var computed = await SaleChequeReturnService.PreviewAsync(_db, saleId, req.Date, inputs);
        var p = computed.Plan;
        return new SaleChequeReturnPreviewDto
        {
            GoodsBeforeVat = p.GoodsBeforeVat,
            Vat = p.Vat,
            Income = p.Income,
            TotalCredit = p.TotalCredit,
            BalanceBefore = p.BalanceBefore,
            BalanceAfter = p.BalanceAfter,
            UncollectedChequeExcess = p.UncollectedChequeExcess
        };
    }

    public async Task<SaleChequeReturnSummaryDto> ReturnSaleChequeOrderAsync(int saleId, SaleChequeReturnCommand cmd, string? user)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        var inputs = cmd.Lines.Select(l => new SaleChequeReturnLineInput(l.OriginalLineId, l.Quantity)).ToList();
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await RejectLinkedWarrantyInvoiceMutationAsync(saleId);
        var record = await SaleChequeReturnService.ApplyAsync(
            _db, saleId, cmd.OperationId, cmd.Date, inputs, cmd.Decision, cmd.ConfirmedTotalCredit, user);
        var returnNumber = await _db.Transactions.Where(t => t.Id == record.ReturnTransactionId)
            .Select(t => t.Number).FirstOrDefaultAsync() ?? "";
        await transaction.CommitAsync();
        return new SaleChequeReturnSummaryDto
        {
            Id = record.Id,
            ReturnNumber = returnNumber,
            Date = record.Date,
            GoodsAmount = record.GoodsAmount,
            VatAmount = record.VatAmount,
            IncomeAmount = record.IncomeAmount,
            BalanceAfter = record.BalanceAfter,
            UncollectedChequeExcess = record.UncollectedChequeExcess,
            Decision = record.Decision
        };
    }

    public async Task ReplaceSaleChequeAsync(int chequeId, SaleChequeReplaceCommand cmd, string? user)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await SaleChequeReplacementService.ReplaceAsync(_db, chequeId, new SaleChequeReplacementInput(
            cmd.OperationId, cmd.OperationDate, cmd.Number, cmd.Amount, cmd.DueDate,
            cmd.BankName, cmd.AccountInfo, cmd.OwnerName, cmd.Note, cmd.Reason, cmd.ConfirmedCreditDueDate), user);
        await transaction.CommitAsync();
    }

    public async Task RefundSaleChequeCreditAsync(int saleId, SaleChequeCreditRefundCommand cmd, string? user)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await SaleChequeCreditSettlementService.RefundAsync(
            _db, saleId, cmd.OperationId, cmd.TrsAccountId, cmd.Amount, cmd.Date, cmd.Note, user);
        await transaction.CommitAsync();
    }

    public async Task ApplySaleChequeCreditAsync(int saleId, SaleChequeCreditAllocationCommand cmd, string? user)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await SaleChequeCreditSettlementService.ApplyToSaleAsync(
            _db, saleId, cmd.TargetSaleId, cmd.OperationId, cmd.Amount, cmd.Date, cmd.Note, user);
        await transaction.CommitAsync();
    }

    public async Task ReceiveSaleChequeInstallmentAsync(int installmentId, SaleChequeInstallmentReceiptCommand cmd, string? user)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await SaleChequeInstallmentPosting.ReceiveAsync(_db, installmentId, cmd.OperationId, cmd.TrsAccountId, cmd.Date, user);
        await transaction.CommitAsync();
    }

    public async Task ReverseSaleChequeInstallmentReceiptAsync(int installmentId, SaleChequeInstallmentReversalCommand cmd, string? user)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await SaleChequeInstallmentPosting.ReverseAsync(_db, installmentId, cmd.OperationId, cmd.Date, user);
        await transaction.CommitAsync();
    }

    public async Task CancelSaleChequeOrderAsync(int saleId, SaleChequeCancelCommand cmd, string? user)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await RejectLinkedWarrantyInvoiceMutationAsync(saleId);
        await SaleChequeCancellationService.VoidAsync(_db, saleId, cmd.OperationId, cmd.Date, cmd.Reason, user);
        await transaction.CommitAsync();
    }

    public async Task<Order> EditSaleChequeOrderAsync(int saleId, OrderCommand cmd, string? user)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ShopPricingService.ValidatePurchasePriceInputs(cmd);
        if (cmd.ChequeCharge is null || cmd.Type != TransactionType.Sale)
            throw new InvalidOperationException("ویرایش فاکتور سوددار نیازمند اطلاعات تاخیر تادیه چک است.");
        if (cmd.Lines.Count == 0 || cmd.Lines.Any(l => l.Quantity <= 0) ||
            cmd.Lines.Any(l => l.VatValue.HasValue && !OrderLineAmounts.IsValidVatValue(l.VatValue.Value)) ||
            cmd.WarehouseId <= 0 || cmd.PartyId <= 0)
            throw new InvalidOperationException("مشخصات اقلام، انبار یا مشتری برای ویرایش فاکتور معتبر نیست.");
        if (cmd.Cheques.Any(c => c.IsCleared) || cmd.Installments.Any(i => i.IsPaid))
            throw new InvalidOperationException("در ویرایش فاکتور سوددار، چک‌ها و اقساط باید در وضعیت تسویه‌نشده ثبت شوند.");

        await using var trx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await RejectLinkedWarrantyInvoiceMutationAsync(saleId);
            var oldSale = await _db.Transactions.AsNoTracking().Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == saleId)
                ?? throw new InvalidOperationException("فاکتور مورد ویرایش یافت نشد.");
            if (await _db.SaleChequeReturns.AnyAsync(r => r.SaleTransactionId == saleId) ||
                await _db.SaleChequeReschedules.AnyAsync(r => r.SaleChequeCharge.TransactionId == saleId) ||
                await _db.SaleChequeReceipts.AnyAsync(r => r.TransactionId == saleId) ||
                await _db.SaleChequeInstallmentReceipts.AnyAsync(r => r.TransactionId == saleId) ||
                await _db.ShopOrderCreditReceipts.IgnoreQueryFilters().AnyAsync(r => r.TransactionId == saleId) ||
                await _db.SaleChequeCreditSettlements.AnyAsync(s => s.SaleTransactionId == saleId || s.TargetSaleTransactionId == saleId))
                throw new InvalidOperationException("این فاکتور دارای گردش بعدی است؛ از گزینه‌های برگشت کالا، تغییر سررسید یا تعویض چک استفاده کنید.");

            var editDate = cmd.Date == default ? oldSale.Date : cmd.Date;
            var voidDate = editDate.Date < oldSale.Date.Date ? oldSale.Date.Date : editDate.Date;
            await SaleChequeCancellationService.VoidInTransactionAsync(
                _db, saleId, Guid.NewGuid(), voidDate, $"ابطال سیستمی جهت ویرایش فاکتور {oldSale.Number}", user);

            var originalLines = oldSale.Lines.ToDictionary(l => l.Id);
            if (cmd.Lines.Any(l => l.Quantity != decimal.Round(l.Quantity, 2) &&
                (!originalLines.TryGetValue(l.Id, out var oldQuantity) || oldQuantity.ProductId != l.ProductId || oldQuantity.Quantity != l.Quantity)))
                throw new InvalidOperationException("مقدار هر سطر حداکثر تا دو رقم اعشار قابل ثبت است؛ فقط مقدار قدیمیِ بدون تغییر قابل حفظ است.");
            foreach (var line in cmd.Lines)
                line.VatValue ??= originalLines.TryGetValue(line.Id, out var oldVat) && oldVat.ProductId == line.ProductId
                    ? oldVat.VatValue : 0m;
            ValidateTaxedLinePrecision(cmd.Lines);
            var productIds = cmd.Lines.Select(l => l.ProductId).Distinct().ToList();
            var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
            await ValidateOrderReferencesAsync(cmd, productIds, products.Count);
            await ValidateShopLineRulesAsync(cmd, products, originalLines);
            var settings = await GetSettingsAsync();

            foreach (var l in cmd.Lines)
            {
                if (products.TryGetValue(l.ProductId, out var prod) &&
                    !prod.IsService && prod.WarehouseId is > 0 && prod.WarehouseId != cmd.WarehouseId)
                    throw new InvalidOperationException($"کالای «{prod.Name}» مخصوص انبار دیگری است.");
            }
            if (!settings.AllowNegativeStock)
            {
                var stocks = await _db.Stocks.Where(s => s.WarehouseId == cmd.WarehouseId && productIds.Contains(s.ProductId)).ToListAsync();
                foreach (var l in cmd.Lines)
                {
                    if (products.TryGetValue(l.ProductId, out var prod) && prod.IsService) continue;
                    var available = stocks.FirstOrDefault(s => s.ProductId == l.ProductId)?.Quantity ?? 0m;
                    if (available < l.Quantity)
                    {
                        var name = products.TryGetValue(l.ProductId, out var pp) ? pp.Name : "";
                        throw new InvalidOperationException($"موجودی «{name}» کافی نیست (موجود: {available:0.##}).");
                    }
                }
            }

            int? saleReferrerId = cmd.ReferrerId is > 0 ? cmd.ReferrerId
                : await _db.Parties.Where(p => p.Id == cmd.PartyId).Select(p => p.ReferrerId).FirstOrDefaultAsync();

            var unchangedFloorLines = ShopMinimumProfitGuard.UnchangedLineIndexes(oldSale, cmd, saleReferrerId);
            var chargeInput = cmd.ChequeCharge;
            decimal income = SaleChequeInitialCalculation.Calculate(
                chargeInput.CalculatorBaseAmount, chargeInput.CalculatorRatePercent, chargeInput.CalculatorMonths);
            var goodsTotal = ShopInvoiceLineMath.Total(cmd.Lines, true, originalLines);
            if (income <= 0 || chargeInput.CalculatorBaseAmount > goodsTotal)
                throw new InvalidOperationException("مبنای ماشین‌حساب یا سود اولیه معتبر نیست.");
            if (chargeInput.ConfirmedCreditDueDate?.Date != cmd.DueDate?.Date)
                throw new InvalidOperationException("تأیید سررسید نسیه با اجزای پرداخت مطابقت ندارد.");
            var paymentTotal = SaleChequeVoucherAmounts.Create(goodsTotal, 0, income).CustomerDebit;

            var newTxn = new Db.Transaction
            {
                Number = await GenerateNumberAsync(TransactionType.Sale),
                Type = TransactionType.Sale,
                Date = editDate,
                Description = string.IsNullOrWhiteSpace(cmd.Description) ? $"ویرایش‌شده از {oldSale.Number}" : cmd.Description,
                WarehouseId = cmd.WarehouseId,
                PartyId = cmd.PartyId,
                ReferrerId = saleReferrerId,
                CreatedAt = DateTime.Now
            };
            if (oldSale.ReferrerId == saleReferrerId)
            {
                newTxn.ReferrerGoodsPercentSnapshot = oldSale.ReferrerGoodsPercentSnapshot;
                newTxn.ReferrerServicePercentSnapshot = oldSale.ReferrerServicePercentSnapshot;
            }
            else await SnapshotReferrerRatesAsync(newTxn);
            foreach (var c in cmd.Cheques) c.Id = 0;
            foreach (var i in cmd.Installments) i.Id = 0;
            ApplyPayment(newTxn, cmd, paymentTotal);

            foreach (var l in cmd.Lines)
            {
                var oldLine = originalLines.TryGetValue(l.Id, out var existingLine) ? existingLine : null;
                var amounts = ShopInvoiceLineMath.Calculate(l, true, oldLine);
                newTxn.Lines.Add(new Db.TransactionLine
                {
                    ProductId = l.ProductId, Quantity = l.Quantity, Price = l.Price,
                    DiscountPercent = amounts.DiscountPercent, DiscountAmount = amounts.DiscountAmount,
                    DiscountIsAmount = amounts.DiscountIsAmount, GrossAmount = amounts.Gross,
                    VatValue = amounts.VatValue, VatAmount = amounts.VatAmount
                });
                newTxn.Amount += amounts.Total;
            }

            _db.Transactions.Add(newTxn);
            await _db.SaveChangesAsync();
            foreach (var pid in productIds)
                await RecalcStockAsync(cmd.WarehouseId, pid);
            await _db.SaveChangesAsync();
            await EnsureNoNegativeStockAsync(productIds.Select(pid => (cmd.WarehouseId, pid)));

            await FreezeChargedSaleCostsAsync(newTxn, products, settings.CostingMethod);
            await ShopMinimumProfitGuard.ValidateAsync(_db, newTxn, products, unchangedFloorLines,
                editDate.Date == oldSale.Date.Date ? oldSale.Id : null);
            await SaleChequeChargeRegistration.RegisterAsync(_db, newTxn.Id,
                new SaleChequeChargeRegistrationInput(chargeInput.CalculatorBaseAmount, chargeInput.CalculatorRatePercent,
                    chargeInput.CalculatorMonths, chargeInput.AdjustmentRatePercent, chargeInput.AdjustmentPeriodDays,
                    chargeInput.ConfirmedCreditDueDate), user);
            var savedOrder = await GetOrderAsync(newTxn.Id) ?? throw new InvalidOperationException();
            await trx.CommitAsync();
            return savedOrder;
        }
        catch
        {
            if (_db.Database.CurrentTransaction is not null)
            {
                try { await trx.RollbackAsync(); } catch { }
            }
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<List<TrsAccount>> GetActiveCompanyTreasuryAccountsAsync(TreasuryAccountKind? kind = null)
    {
        var entities = await SaleChequeTreasuryGuard.ListCompanyAccountsAsync(_db, kind);
        // Invoice selection needs identity and account kind, not ERP mappings or balances.
        return entities.Select(e => new TrsAccount
        {
            Id = e.Id, CompanyId = e.CompanyId, Code = e.Code, Name = e.Name, Kind = e.Kind,
            BankName = e.BankName, IsDefault = e.IsDefault, IsActive = e.IsActive
        }).ToList();
    }

    // The finalized source and its customer-facing invoice must stay in agreement.
    // Settlement is still allowed through the separate receipt ledger.
    private async Task RejectLinkedWarrantyInvoiceMutationAsync(int transactionId)
    {
        if (await _db.ShopWarranties.IgnoreQueryFilters().AnyAsync(w => w.ChargeTransactionId == transactionId) ||
            await _db.RepairOrders.IgnoreQueryFilters().AnyAsync(r => r.WarrantyProcessed && r.InvoiceTransactionId == transactionId))
            throw new InvalidOperationException("فاکتور مرتبط با پذیرش گارانتی نهایی‌شده قابل ویرایش یا حذف مستقیم نیست.");
    }

    // مسیرهای قدیمی رکورد مالی و سند مرتبط را اصلاح نمی‌کنند. تا تکمیل گردش اصلاحی
    // نباید داده‌ای را تغییر دهند که منجر به دوگانگی حساب و صورت‌حساب می‌شود.
    private async Task RejectUnimplementedChequeChargeMutationAsync(int transactionId)
    {
        if (await _db.ShopOrderCreditReceipts.IgnoreQueryFilters().AnyAsync(r => r.TransactionId == transactionId))
            throw new InvalidOperationException("فاکتور دارای تاریخچه تسویه نسیه است؛ ویرایش یا حذف مستقیم آن برای حفظ سابقه مجاز نیست.");
        if (await _db.SaleChequeReceipts.AnyAsync(r => r.TransactionId == transactionId) ||
            await _db.SaleChequeInstallmentReceipts.AnyAsync(r => r.TransactionId == transactionId) ||
            await _db.SaleChequeCharges.AnyAsync(c => c.TransactionId == transactionId) ||
            await _db.SaleChequeReturns.AnyAsync(r => r.SaleTransactionId == transactionId || r.ReturnTransactionId == transactionId) ||
            await _db.SaleChequeCancellations.AnyAsync(c => c.SaleTransactionId == transactionId || c.ReturnTransactionId == transactionId) ||
            await _db.SaleChequeCreditSettlements.AnyAsync(s => s.SaleTransactionId == transactionId || s.TargetSaleTransactionId == transactionId))
            throw new InvalidOperationException("عملیات روی فاکتور دارای تاخیر تادیه چک فقط از گردش اختصاصی فاکتور فروشگاهی مجاز است.");
    }

    /// <summary>پاس کردن چک.</summary>
    public Task ClearChequeAsync(int chequeId)
    {
        // The old status-only endpoint did not record a bank movement.
        throw new InvalidOperationException("برای وصول یا پرداخت چک، حساب بانکی و تاریخ را در مدیریت چک‌های فروشگاه انتخاب کنید.");
    }

    /// <summary>برگشت زدن چک.</summary>
    public async Task BounceChequeAsync(int chequeId, string? reason)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var c = await _db.Cheques.FindAsync(chequeId)
            ?? throw new InvalidOperationException("چک یافت نشد.");
        if (reason?.Trim().Length > 200) throw new InvalidOperationException("علت برگشت حداکثر ۲۰۰ نویسه است.");
        if (c.Status == ChequeStatus.Cancelled) throw new InvalidOperationException("چک ابطال‌شده قابل برگشت نیست.");
        if (await _db.SaleChequeCharges.AnyAsync(x => x.TransactionId == c.TransactionId))
            throw new InvalidOperationException("وضعیت چک این فاکتور از گردش اختصاصی تغییر می‌کند.");
        if (c.IsCleared || c.Status == ChequeStatus.Cleared || await _db.SaleChequeReceipts.AnyAsync(r => r.ChequeId == c.Id && r.ReversalDate == null))
            throw new InvalidOperationException("ابتدا وصول چک را از مسیر لغو وصول با حفظ گردش بانکی لغو کنید.");
        c.IsCleared = false;
        c.ClearedAt = null;
        c.Status = ChequeStatus.Bounced;
        c.BouncedAt = DateTime.Now;
        c.BounceReason = string.IsNullOrWhiteSpace(reason) ? "عدم موجودی کافی" : reason.Trim();
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>بازگردانی وضعیت چک به در جریان (InHand).</summary>
    public async Task ResetChequeAsync(int chequeId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var c = await _db.Cheques.FindAsync(chequeId)
            ?? throw new InvalidOperationException("چک یافت نشد.");
        if (c.Status == ChequeStatus.Cancelled) throw new InvalidOperationException("چک ابطال‌شده قابل بازگردانی نیست.");
        if (await _db.SaleChequeCharges.AnyAsync(x => x.TransactionId == c.TransactionId))
            throw new InvalidOperationException("وضعیت چک این فاکتور از گردش اختصاصی تغییر می‌کند.");
        if (c.IsCleared || c.Status == ChequeStatus.Cleared || await _db.SaleChequeReceipts.AnyAsync(r => r.ChequeId == c.Id && r.ReversalDate == null))
            throw new InvalidOperationException("ابتدا وصول چک را از مسیر لغو وصول با حفظ گردش بانکی لغو کنید.");
        c.IsCleared = false;
        c.ClearedAt = null;
        c.Status = ChequeStatus.InHand;
        c.BouncedAt = null;
        c.BounceReason = null;
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>ثبت پرداخت قسط.</summary>
    public Task PayInstallmentAsync(int installmentId)
    {
        // A status-only update has no account/date history and makes the bank
        // balance incorrect. New clients must use the explicit receipt route.
        throw new InvalidOperationException("برای تسویه قسط، حساب صندوق/بانک و تاریخ را در مدیریت اقساط فروشگاه انتخاب کنید.");
    }

    /// <summary>لغو پرداخت قسط (برگشت به حالت پرداخت‌نشده / بدهکار).</summary>
    public Task UnpayInstallmentAsync(int installmentId)
    {
        // Status-only legacy payments have no account history to reverse safely.
        throw new InvalidOperationException("تسویه قدیمی قسط فاقد گردش حساب قابل احراز است؛ پیش از لغو، سوابق آن باید بررسی شود.");
    }

    /// <summary>تسویه نسیه با مسیر وضعیت‌محور قدیمی قابل ثبت مالی قابل اتکا نیست.</summary>
    public Task SettleCreditAsync(int transactionId, decimal amount)
    {
        throw new InvalidOperationException("تسویه نسیه از مسیر قدیمی حساب و تاریخ گردش ندارد؛ از گزینه نسیه و سوابق در فهرست فاکتورها استفاده کنید.");
    }

    private async Task<string> GenerateNumberAsync(TransactionType type)
    {
        var prefix = type switch
        {
            TransactionType.Purchase => "PR",
            TransactionType.Sale => "SL",
            TransactionType.SaleReturn => "SR",
            _ => "ADJ"
        };
        var count = await _db.Transactions.CountAsync(t => t.Type == type);
        return $"{prefix}-{count + 1:0000}";
    }

    /// <summary>بازسازی موجودی و قیمت میانگین یک کالا در یک انبار بر اساس کل اسناد (فقط در حافظه؛ ذخیره توسط فراخواننده).</summary>
    private async Task RecalcStockAsync(int warehouseId, int productId)
    {
        var txns = await _db.Transactions.Include(t => t.Lines)
            .Where(t => t.WarehouseId == warehouseId && t.Lines.Any(l => l.ProductId == productId))
            .OrderBy(t => t.Date).ThenBy(t => t.Id)
            .ToListAsync();

        decimal qty = 0, cost = 0;
        var product = await _db.Products.FindAsync(productId);
        var defaultCost = product?.PurchasePrice ?? 0;

        foreach (var t in txns)
        {
            foreach (var l in t.Lines.Where(l => l.ProductId == productId))
            {
                switch (t.Type)
                {
                    case TransactionType.Purchase:
                        var newQty = qty + l.Quantity;
                        if (newQty != 0)
                            cost = (qty * cost + l.Quantity * l.Price) / newQty;
                        qty = newQty;
                        break;
                    case TransactionType.Sale:
                    case TransactionType.WarrantyOut:
                        qty -= l.Quantity;
                        break;
                    case TransactionType.SaleReturn:
                        if (!l.UnitCostSnapshot.HasValue || l.UnitCostSnapshot.Value < 0)
                            throw new InvalidOperationException("بهای ثبت‌شده کالای برگشتی معتبر نیست.");
                        var returnedQty = qty + l.Quantity;
                        if (returnedQty != 0)
                            cost = (qty * cost + l.Quantity * l.UnitCostSnapshot.Value) / returnedQty;
                        qty = returnedQty;
                        break;
                    default: // Adjustment / Initial
                        qty = l.Quantity;
                        if (cost == 0 && qty > 0) cost = l.Price > 0 ? l.Price : defaultCost;
                        break;
                }
            }
        }

        // ---------- ماژول انبارداری ----------
        // گردش اسناد رسید/حواله انبار هم به مانده اضافه می‌شود تا مانده «یکپارچه» بماند.
        var invStock = await _db.InvStocks.AsNoTracking()
            .FirstOrDefaultAsync(s => s.WarehouseId == warehouseId && s.ProductId == productId);
        if (invStock is not null)
        {
            qty += invStock.Quantity;
            if (invStock.AvgCost > 0) cost = invStock.AvgCost;
        }

        var stock = await _db.Stocks.FirstOrDefaultAsync(s => s.WarehouseId == warehouseId && s.ProductId == productId);
        if (qty == 0 && stock is null) return;

        if (stock is null)
        {
            stock = new Db.Stock { WarehouseId = warehouseId, ProductId = productId };
            _db.Stocks.Add(stock);
        }
        stock.Quantity = qty;
        stock.AvgCost = qty > 0 ? cost : defaultCost;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ================== مدیریت اقساط و چک‌های سامانه فروشگاهی ==================

    public async Task<ShopCreditSummary> GetShopCreditSummaryAsync()
    {
        var today = DateTime.Today;
        var cheques = await _db.Cheques.AsNoTracking().ToListAsync();
        var installments = await _db.Installments.AsNoTracking().ToListAsync();

        var cancelledIds = (await _db.SaleChequeCancellations.AsNoTracking()
            .Select(c => c.SaleTransactionId).ToListAsync()).ToHashSet();

        var inHand = cheques.Where(c => !c.IsCleared && c.Status != ChequeStatus.Cleared && c.Status != ChequeStatus.Bounced && c.Status != ChequeStatus.Cancelled && c.Status != ChequeStatus.Endorsed && !cancelledIds.Contains(c.TransactionId)).ToList();
        var cleared = cheques.Where(c => c.IsCleared || c.Status == ChequeStatus.Cleared).ToList();
        var bounced = cheques.Where(c => c.Status == ChequeStatus.Bounced && !cancelledIds.Contains(c.TransactionId)).ToList();

        var pendingInst = installments.Where(i => !i.IsPaid && !cancelledIds.Contains(i.TransactionId)).ToList();
        var paidInst = installments.Where(i => i.IsPaid).ToList();
        var overdueInst = installments.Where(i => !i.IsPaid && i.DueDate.Date < today && !cancelledIds.Contains(i.TransactionId)).ToList();

        return new ShopCreditSummary
        {
            InHandChequesCount = inHand.Count,
            InHandChequesTotal = inHand.Sum(x => x.Amount),
            ClearedChequesCount = cleared.Count,
            ClearedChequesTotal = cleared.Sum(x => x.Amount),
            BouncedChequesCount = bounced.Count,
            BouncedChequesTotal = bounced.Sum(x => x.Amount),

            PendingInstallmentsCount = pendingInst.Count,
            PendingInstallmentsTotal = pendingInst.Sum(x => x.Amount),
            PaidInstallmentsCount = paidInst.Count,
            PaidInstallmentsTotal = paidInst.Sum(x => x.Amount),
            OverdueInstallmentsCount = overdueInst.Count,
            OverdueInstallmentsTotal = overdueInst.Sum(x => x.Amount)
        };
    }

    public async Task<List<ShopChequeItem>> GetShopChequesAsync(ChequeStatus? status, TransactionType? txnType, string? search)
    {
        var today = DateTime.Today;
        var q = _db.Cheques.AsNoTracking().AsQueryable();

        if (status.HasValue)
        {
            if (status.Value == ChequeStatus.Cleared)
                q = q.Where(c => c.IsCleared || c.Status == ChequeStatus.Cleared);
            else if (status.Value == ChequeStatus.Bounced)
                q = q.Where(c => c.Status == ChequeStatus.Bounced);
            else if (status.Value == ChequeStatus.Cancelled)
                q = q.Where(c => c.Status == ChequeStatus.Cancelled);
            else
                q = q.Where(c => !c.IsCleared && c.Status != ChequeStatus.Cleared && c.Status != ChequeStatus.Bounced && c.Status != ChequeStatus.Cancelled && c.Status != ChequeStatus.Endorsed);
        }

        var list = await q.OrderBy(c => c.DueDate).ToListAsync();
        if (list.Count == 0) return new List<ShopChequeItem>();

        var txnIds = list.Select(c => c.TransactionId).Distinct().ToList();
        var txns = await _db.Transactions.AsNoTracking()
            .Where(t => txnIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id);

        var partyIds = txns.Values.Where(t => t.PartyId.HasValue).Select(t => t.PartyId!.Value).Distinct().ToList();
        var parties = await _db.Parties.AsNoTracking()
            .Where(p => partyIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);
        var chargedTxnIds = (await _db.SaleChequeCharges.AsNoTracking()
            .Where(c => txnIds.Contains(c.TransactionId)).Select(c => c.TransactionId).ToListAsync()).ToHashSet();

        var cancelledTxnIds = (await _db.SaleChequeCancellations.AsNoTracking()
            .Where(c => txnIds.Contains(c.SaleTransactionId)).Select(c => c.SaleTransactionId).ToListAsync()).ToHashSet();
        var result = new List<ShopChequeItem>();
        foreach (var c in list)
        {
            if (cancelledTxnIds.Contains(c.TransactionId) && !c.IsCleared && c.Status != ChequeStatus.Cleared && c.Status != ChequeStatus.Cancelled) continue;
            txns.TryGetValue(c.TransactionId, out var t);
            if (t is null || t.CompanyId != c.CompanyId)
                throw new InvalidOperationException("شرکت چک با فاکتور آن مطابقت ندارد؛ سابقه نیازمند بررسی است.");
            if (txnType.HasValue && t.Type != txnType.Value) continue;

            Db.Party? party = null;
            if (t?.PartyId.HasValue == true) parties.TryGetValue(t.PartyId.Value, out party);

            var item = new ShopChequeItem
            {
                Id = c.Id,
                TransactionId = c.TransactionId,
                HasChequeCharge = chargedTxnIds.Contains(c.TransactionId),
                OrderNumber = t?.Number ?? "—",
                TransactionType = t?.Type ?? TransactionType.Sale,
                PartyId = t?.PartyId ?? 0,
                PartyName = party?.Name ?? (c.OwnerName ?? "—"),
                PartyMobile = party?.Mobile,
                PartyPhone = party?.Phone,
                Number = c.Number,
                TrsAccountId = c.TrsAccountId,
                BankName = c.BankName,
                AccountInfo = c.AccountInfo,
                OwnerName = c.OwnerName,
                Amount = c.Amount,
                DueDate = c.DueDate,
                IsCleared = c.IsCleared || c.Status == ChequeStatus.Cleared,
                ClearedAt = c.ClearedAt,
                Status = c.Status,
                BouncedAt = c.BouncedAt,
                BounceReason = c.BounceReason,
                Note = c.Note,
                DaysOverdue = c.DueDate.Date < today ? (int)(today - c.DueDate.Date).TotalDays : 0
            };

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                bool match = item.Number.ToLower().Contains(s) ||
                             item.PartyName.ToLower().Contains(s) ||
                             (item.BankName != null && item.BankName.ToLower().Contains(s)) ||
                             item.OrderNumber.ToLower().Contains(s) ||
                             PhoneSearch.Matches(s, item.PartyMobile, item.PartyPhone);
                if (!match) continue;
            }

            result.Add(item);
        }

        return result;
    }

    public async Task<List<ShopInstallmentItem>> GetShopInstallmentsAsync(bool? isPaid, string? search)
    {
        var today = DateTime.Today;
        var q = _db.Installments.AsNoTracking().AsQueryable();

        if (isPaid.HasValue)
            q = q.Where(i => i.IsPaid == isPaid.Value);

        var list = await q.OrderBy(i => i.DueDate).ToListAsync();
        if (list.Count == 0) return new List<ShopInstallmentItem>();

        var txnIds = list.Select(i => i.TransactionId).Distinct().ToList();
        var txns = await _db.Transactions.AsNoTracking()
            .Where(t => txnIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id);
        var chargedTxnIds = (await _db.SaleChequeCharges.AsNoTracking()
            .Where(c => txnIds.Contains(c.TransactionId)).Select(c => c.TransactionId).ToListAsync()).ToHashSet();
        var cancelledTxnIds = (await _db.SaleChequeCancellations.AsNoTracking()
            .Where(c => txnIds.Contains(c.SaleTransactionId)).Select(c => c.SaleTransactionId).ToListAsync()).ToHashSet();

        var installmentIds = list.Select(i => i.Id).ToList();
        var trackedReceiptIds = (await _db.SaleChequeInstallmentReceipts.AsNoTracking()
            .Where(r => installmentIds.Contains(r.InstallmentId) && r.ReversalDate == null)
            .Select(r => r.InstallmentId).ToListAsync()).ToHashSet();

        var partyIds = txns.Values.Where(t => t.PartyId.HasValue).Select(t => t.PartyId!.Value).Distinct().ToList();
        var parties = await _db.Parties.AsNoTracking()
            .Where(p => partyIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        var result = new List<ShopInstallmentItem>();
        foreach (var i in list)
        {
            if (cancelledTxnIds.Contains(i.TransactionId)) continue;
            txns.TryGetValue(i.TransactionId, out var t);
            if (t is null || t.CompanyId != i.CompanyId)
                throw new InvalidOperationException("شرکت قسط با فاکتور آن مطابقت ندارد؛ سابقه نیازمند بررسی است.");
            Db.Party? party = null;
            if (t?.PartyId.HasValue == true) parties.TryGetValue(t.PartyId.Value, out party);

            var item = new ShopInstallmentItem
            {
                Id = i.Id,
                TransactionId = i.TransactionId,
                HasChequeCharge = chargedTxnIds.Contains(i.TransactionId),
                TransactionType = t?.Type ?? TransactionType.Sale,
                HasTrackedReceipt = trackedReceiptIds.Contains(i.Id),
                OrderNumber = t?.Number ?? "—",
                PartyId = t?.PartyId ?? 0,
                PartyName = party?.Name ?? "—",
                PartyMobile = party?.Mobile,
                PartyPhone = party?.Phone,
                No = i.No,
                Amount = i.Amount,
                DueDate = i.DueDate,
                IsPaid = i.IsPaid,
                PaidAt = i.PaidAt,
                DaysOverdue = !i.IsPaid && i.DueDate.Date < today ? (int)(today - i.DueDate.Date).TotalDays : 0
            };

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                bool match = item.PartyName.ToLower().Contains(s) ||
                             item.OrderNumber.ToLower().Contains(s) ||
                             PhoneSearch.Matches(s, item.PartyMobile, item.PartyPhone);
                if (!match) continue;
            }

            result.Add(item);
        }

        return result;
    }


    // ================== گزارش گردش حساب و مانده اشخاص (طلب و بدهی) ==================

    private sealed record ChequeStatementAdjustment(DateTime Date, decimal Amount);
    private sealed record ChequeStatementReceipt(DateTime Date, decimal Amount, bool IsEndorsement = false);
    private sealed record ChequeStatementReturn(DateTime Date, string ReturnNumber, decimal GoodsWithVat, decimal IncomeAmount);
    private sealed record ChequeStatementCancellation(DateTime Date, decimal ReversedGoodsWithVat, decimal ReversedIncomeDelta, decimal RefundedCashAmount);
    private sealed record ChequeStatementCreditSettlement(DateTime Date, SaleChequeCreditSettlementKind Kind, string? TargetNumber, decimal Amount);
    private sealed record ChequeStatementInboundAllocation(DateTime Date, string SourceNumber, decimal Amount);
    private sealed record ChequeStatementHistory(decimal CurrentIncome, decimal InitialIncome,
        decimal? CalculatorBaseAmount, decimal? CalculatorRatePercent, int? CalculatorMonths,
        List<ChequeStatementAdjustment> Adjustments,
        List<ChequeStatementReceipt> Receipts,
        List<ChequeStatementReturn> Returns,
        List<ChequeStatementReceipt> InstallmentReceipts,
        List<ChequeStatementCancellation> Cancellations,
        List<ChequeStatementCreditSettlement> CreditSettlements,
        List<ChequeStatementInboundAllocation> InboundAllocations);

    private async Task<Dictionary<int, ChequeStatementHistory>> LoadChequeStatementHistoryAsync(IReadOnlyCollection<int> partyIds)
    {
        if (partyIds.Count == 0) return new();
        var charges = await _db.SaleChequeCharges.AsNoTracking().Include(c => c.Transaction)
            .Where(c => c.Transaction.PartyId.HasValue && partyIds.Contains(c.Transaction.PartyId.Value) && c.Transaction.Type == TransactionType.Sale)
            .ToListAsync();
        var saleIds = charges.Select(c => c.TransactionId).ToList();
        var chargeIds = charges.Select(c => c.Id).ToList();
        var events = (await _db.SaleChequeChargeEvents.AsNoTracking().Where(e => chargeIds.Contains(e.SaleChequeChargeId)).ToListAsync())
            .ToLookup(e => e.SaleChequeChargeId);
        var receipts = (await _db.SaleChequeReceipts.AsNoTracking().Where(r => saleIds.Contains(r.TransactionId)).ToListAsync())
            .ToLookup(r => r.TransactionId);
        var endorsedReceipts = (await _db.ShopExpenseCheques.AsNoTracking()
            .Where(c => c.SourceTransactionId.HasValue && saleIds.Contains(c.SourceTransactionId.Value) && c.State == ExpenseChequeState.Cleared)
            .ToListAsync()).ToLookup(c => c.SourceTransactionId!.Value);
        var instReceipts = (await _db.SaleChequeInstallmentReceipts.AsNoTracking().Where(r => saleIds.Contains(r.TransactionId)).ToListAsync())
            .ToLookup(r => r.TransactionId);
        var returns = (await _db.SaleChequeReturns.AsNoTracking().Include(r => r.ReturnTransaction)
            .Where(r => saleIds.Contains(r.SaleTransactionId)).ToListAsync()).ToLookup(r => r.SaleTransactionId);
        var cancellations = (await _db.SaleChequeCancellations.AsNoTracking()
            .Where(c => saleIds.Contains(c.SaleTransactionId)).ToListAsync()).ToLookup(c => c.SaleTransactionId);
        var settlements = await _db.SaleChequeCreditSettlements.AsNoTracking().Include(s => s.SaleTransaction)
            .Include(s => s.TargetSaleTransaction)
            .Where(s => saleIds.Contains(s.SaleTransactionId) || (s.TargetSaleTransactionId.HasValue && saleIds.Contains(s.TargetSaleTransactionId.Value)))
            .ToListAsync();
        var outbound = settlements.ToLookup(s => s.SaleTransactionId);
        var inbound = settlements.Where(s => s.Kind == SaleChequeCreditSettlementKind.ApplyToSale && s.TargetSaleTransactionId.HasValue)
            .ToLookup(s => s.TargetSaleTransactionId!.Value);
        var result = new Dictionary<int, ChequeStatementHistory>();
        foreach (var charge in charges)
        {
            var sale = charge.Transaction;
            await SaleChequeSettlementLedger.EnsureSettledAmountConsistentAsync(_db, sale, allowUnpostedLegacy: true);
            var history = events[charge.Id];
            var saleReceipts = receipts[sale.Id];
            var saleInstReceipts = instReceipts[sale.Id];
            var receiptRows = saleReceipts.Select(r => new ChequeStatementReceipt(r.Date, r.Amount)).ToList();
            receiptRows.AddRange(saleReceipts.Where(r => r.ReversalDate.HasValue)
                .Select(r => new ChequeStatementReceipt(r.ReversalDate!.Value, -r.Amount)));
            receiptRows.AddRange(endorsedReceipts[sale.Id].Select(c => new ChequeStatementReceipt(c.SettledAt!.Value, c.Amount, true)));
            var instRows = saleInstReceipts.Select(r => new ChequeStatementReceipt(r.Date, r.Amount)).ToList();
            instRows.AddRange(saleInstReceipts.Where(r => r.ReversalDate.HasValue)
                .Select(r => new ChequeStatementReceipt(r.ReversalDate!.Value, -r.Amount)));
            result.Add(sale.Id, new ChequeStatementHistory(charge.Amount, charge.InitialAmount,
                charge.CalculatorBaseAmount, charge.CalculatorRatePercent, charge.CalculatorMonths,
                history.Where(e => e.Kind == SaleChequeChargeEventKind.DueDateAdjustment)
                    .Select(e => new ChequeStatementAdjustment(e.Date, e.IncomeDelta)).OrderBy(e => e.Date).ToList(),
                receiptRows.OrderBy(r => r.Date).ToList(),
                returns[sale.Id].OrderBy(r => r.Date).ThenBy(r => r.Id)
                    .Select(r => new ChequeStatementReturn(r.Date, r.ReturnTransaction.Number, r.GoodsAmount + r.VatAmount, r.IncomeAmount)).ToList(),
                instRows.OrderBy(r => r.Date).ToList(),
                cancellations[sale.Id].OrderBy(c => c.Date).ThenBy(c => c.Id)
                    .Select(c => new ChequeStatementCancellation(c.Date, c.ReversedGoodsAmount + c.ReversedVatAmount, c.ReversedIncomeDelta, c.RefundedCashAmount)).ToList(),
                outbound[sale.Id].OrderBy(c => c.Date).ThenBy(c => c.Id)
                    .Select(c => new ChequeStatementCreditSettlement(c.Date, c.Kind, c.TargetSaleTransaction?.Number, c.Amount)).ToList(),
                inbound[sale.Id].OrderBy(c => c.Date).ThenBy(c => c.Id)
                    .Select(c => new ChequeStatementInboundAllocation(c.Date, c.SaleTransaction.Number, c.Amount)).ToList()));
        }
        return result;
    }

    private static string DescribeChequeIncome(ChequeStatementHistory? history, string saleNumber)
    {
        var text = $"سود توافق‌شده بابت پرداخت چکی فاکتور {saleNumber}؛ جدا از کالا و بدون ارزش افزوده";
        // Old records may not contain calculator snapshots. Never invent their terms.
        if (history?.CalculatorBaseAmount is decimal amount &&
            history.CalculatorRatePercent is decimal rate && history.CalculatorMonths is int months)
        {
            var basis = Fa.Digits(amount.ToString("#,##0.##", CultureInfo.InvariantCulture));
            var percent = Fa.Digits(rate.ToString("0.######", CultureInfo.InvariantCulture));
            text += $" (مبنا {basis} ریال، {percent}٪ ماهانه، {Fa.Digits(months)} ماه)";
        }
        return text;
    }

    private sealed record ShopCashStatementEvent(int TransactionId, int PartyId, string OrderNumber,
        TransactionType Type, DateTime Date, decimal Amount, bool IsReversal, bool IsOpening);

    private async Task<List<ShopCashStatementEvent>> LoadShopCashStatementEventsAsync(IReadOnlyCollection<int> partyIds)
    {
        if (partyIds.Count == 0) return new();
        var entries = await _db.ShopOrderCashEntries.AsNoTracking()
            .Where(e => e.PartyId.HasValue && partyIds.Contains(e.PartyId.Value)).ToListAsync();
        await ShopOrderCashLedger.EnsureMatchedVouchersValidAsync(_db, entries);
        var trackedOrders = await _db.Transactions.AsNoTracking().Where(t => t.ShopCashLedgerEnabled && t.CashAmount > 0 &&
            t.PartyId.HasValue && partyIds.Contains(t.PartyId.Value)).ToListAsync();
        if (trackedOrders.Any(t => entries.Count(e => e.TransactionId == t.Id && !e.ReversalDate.HasValue &&
                e.CompanyId == t.CompanyId && e.PartyId == t.PartyId && e.Amount == t.CashAmount &&
                e.TrsAccountId == t.TrsAccountId && e.OrderType == t.Type) != 1 || t.CashAmount > t.SettledAmount))
            throw new InvalidOperationException("گردش نقدی فعال فاکتور در صورت‌حساب یافت نشد یا با فاکتور مطابقت ندارد.");
        var activeIds = entries.Where(e => !e.ReversalDate.HasValue).Select(e => e.TransactionId).Distinct().ToList();
        var activeOrders = activeIds.Count == 0 ? new List<Db.Transaction>() : await _db.Transactions.AsNoTracking()
            .Where(t => activeIds.Contains(t.Id)).ToListAsync();
        var byId = activeOrders.ToDictionary(t => t.Id);
        var result = new List<ShopCashStatementEvent>();
        foreach (var e in entries)
        {
            if (e.Amount <= 0 || e.OperationId == Guid.Empty || e.OrderType is not (TransactionType.Sale or TransactionType.Purchase) ||
                e.ReversalDate.HasValue != e.ReversalOperationId.HasValue || e.ReversalOperationId == Guid.Empty ||
                (e.ReversalDate.HasValue && e.ReversalDate.Value.Date < e.Date.Date))
                throw new InvalidOperationException("گردش نقدی صورت‌حساب ناقص است.");
            if (!e.ReversalDate.HasValue && (!byId.TryGetValue(e.TransactionId, out var order) ||
                !order.ShopCashLedgerEnabled || order.CompanyId != e.CompanyId || order.PartyId != e.PartyId ||
                order.Type != e.OrderType || order.CashAmount != e.Amount || order.TrsAccountId != e.TrsAccountId))
                throw new InvalidOperationException("گردش نقدی صورت‌حساب با فاکتور مطابقت ندارد.");
            result.Add(new ShopCashStatementEvent(e.TransactionId, e.PartyId!.Value, e.OrderNumber, e.OrderType, e.Date, e.Amount, false, e.IsOpening));
            if (e.ReversalDate.HasValue)
                result.Add(new ShopCashStatementEvent(e.TransactionId, e.PartyId.Value, e.OrderNumber, e.OrderType, e.ReversalDate.Value, e.Amount, true, false));
        }
        return result;
    }

    private sealed record OrdinaryChequeStatementEvent(int TransactionId, int PartyId, string OrderNumber,
        TransactionType Type, DateTime Date, decimal Amount, bool IsReversal, bool IsEndorsement = false);

    private async Task<(List<OrdinaryChequeStatementEvent> Events, Dictionary<int, decimal> Active)> LoadOrdinaryChequeStatementEventsAsync(IReadOnlyCollection<int> partyIds)
    {
        if (partyIds.Count == 0) return (new(), new());
        var receipts = await _db.SaleChequeReceipts.AsNoTracking().Include(r => r.Transaction).Include(r => r.Cheque)
            .Where(r => r.Transaction.PartyId.HasValue && partyIds.Contains(r.Transaction.PartyId.Value) &&
                !_db.SaleChequeCharges.IgnoreQueryFilters().Any(c => c.TransactionId == r.TransactionId)).ToListAsync();
        var events = new List<OrdinaryChequeStatementEvent>();
        var active = new Dictionary<int, decimal>();
        foreach (var r in receipts)
        {
            var t = r.Transaction;
            if (r.CompanyId != t.CompanyId || r.Cheque.CompanyId != t.CompanyId || r.Cheque.TransactionId != t.Id ||
                t.Type is not (TransactionType.Sale or TransactionType.Purchase) || r.OperationId == Guid.Empty || r.Amount <= 0 || r.Date.Date < t.Date.Date ||
                r.ReversalDate.HasValue != r.ReversalOperationId.HasValue || r.ReversalOperationId == Guid.Empty ||
                (r.ReversalDate.HasValue && r.ReversalDate.Value.Date < r.Date.Date) ||
                (!r.ReversalDate.HasValue && (!r.Cheque.IsCleared || r.Cheque.Status != ChequeStatus.Cleared ||
                    r.Cheque.TrsAccountId != r.TrsAccountId || r.Cheque.Amount != r.Amount)))
                throw new InvalidOperationException("تاریخچه وصول چک با صورت‌حساب مطابقت ندارد.");
            events.Add(new OrdinaryChequeStatementEvent(t.Id, t.PartyId!.Value, t.Number, t.Type, r.Date, r.Amount, false));
            if (r.ReversalDate.HasValue)
                events.Add(new OrdinaryChequeStatementEvent(t.Id, t.PartyId.Value, t.Number, t.Type, r.ReversalDate.Value, r.Amount, true));
            else active[t.Id] = active.GetValueOrDefault(t.Id) + r.Amount;
        }
        var endorsements = await _db.ShopExpenseCheques.AsNoTracking().Include(c => c.SourceTransaction)
            .Include(c => c.SourceCheque).Include(c => c.Expense)
            .Where(c => c.State == ExpenseChequeState.Cleared && c.SourceTransaction != null &&
                c.SourceTransaction.PartyId.HasValue && partyIds.Contains(c.SourceTransaction.PartyId.Value) &&
                !_db.SaleChequeCharges.IgnoreQueryFilters().Any(x => x.TransactionId == c.SourceTransactionId)).ToListAsync();
        foreach (var c in endorsements)
        {
            var t = c.SourceTransaction!;
            if (c.Kind != ExpenseChequeKind.Received || c.CompanyId != t.CompanyId || c.Expense.CompanyId != t.CompanyId ||
                c.SourceCheque is null || c.SourceCheque.CompanyId != t.CompanyId || c.SourceCheque.TransactionId != t.Id ||
                c.SourceCheque.Status != ChequeStatus.Endorsed || c.SourceCheque.IsCleared || c.SourceCheque.Amount != c.Amount ||
                c.Amount != c.Expense.Amount || c.Amount <= 0 || c.OperationId == Guid.Empty || c.StatusOperationId is null ||
                !c.SettledAt.HasValue || c.SettledAt.Value.Date < c.HandedAt.Date || c.HandedAt.Date < t.Date.Date ||
                t.Type != TransactionType.Sale)
                throw new InvalidOperationException("تاریخچه چک خرج‌شده با صورت‌حساب مطابقت ندارد.");
            events.Add(new OrdinaryChequeStatementEvent(t.Id, t.PartyId!.Value, t.Number, t.Type, c.SettledAt.Value, c.Amount, false, true));
            active[t.Id] = active.GetValueOrDefault(t.Id) + c.Amount;
        }
        return (events, active);
    }

    private sealed record OrdinaryInstallmentStatementEvent(int TransactionId, int PartyId, string OrderNumber,
        TransactionType Type, DateTime Date, decimal Amount, bool IsReversal);

    private async Task<(List<OrdinaryInstallmentStatementEvent> Events, Dictionary<int, decimal> Active)> LoadOrdinaryInstallmentStatementEventsAsync(IReadOnlyCollection<int> partyIds)
    {
        if (partyIds.Count == 0) return (new(), new());
        var receipts = await _db.SaleChequeInstallmentReceipts.AsNoTracking().Include(r => r.Transaction).Include(r => r.Installment)
            .Where(r => r.Transaction.PartyId.HasValue && partyIds.Contains(r.Transaction.PartyId.Value) &&
                !_db.SaleChequeCharges.IgnoreQueryFilters().Any(c => c.TransactionId == r.TransactionId)).ToListAsync();
        var events = new List<OrdinaryInstallmentStatementEvent>();
        var active = new Dictionary<int, decimal>();
        foreach (var r in receipts)
        {
            var t = r.Transaction;
            if (r.CompanyId != t.CompanyId || r.Installment.CompanyId != t.CompanyId || r.Installment.TransactionId != t.Id ||
                t.Type is not (TransactionType.Sale or TransactionType.Purchase) || r.OperationId == Guid.Empty || r.Amount <= 0 || r.Date.Date < t.Date.Date ||
                r.ReversalDate.HasValue != r.ReversalOperationId.HasValue || r.ReversalOperationId == Guid.Empty ||
                (r.ReversalDate.HasValue && r.ReversalDate.Value.Date < r.Date.Date) ||
                (!r.ReversalDate.HasValue && (!r.Installment.IsPaid || r.Installment.Amount != r.Amount)))
                throw new InvalidOperationException("تاریخچه دریافت یا پرداخت قسط با صورت‌حساب مطابقت ندارد.");
            events.Add(new OrdinaryInstallmentStatementEvent(t.Id, t.PartyId!.Value, t.Number, t.Type, r.Date, r.Amount, false));
            if (r.ReversalDate.HasValue)
                events.Add(new OrdinaryInstallmentStatementEvent(t.Id, t.PartyId.Value, t.Number, t.Type, r.ReversalDate.Value, r.Amount, true));
            else active[t.Id] = active.GetValueOrDefault(t.Id) + r.Amount;
        }
        return (events, active);
    }

    private sealed record ShopCreditStatementEvent(int TransactionId, int PartyId, string OrderNumber,
        TransactionType Type, DateTime Date, decimal Amount, bool IsReversal);

    private async Task<(List<ShopCreditStatementEvent> Events, Dictionary<int, decimal> Active)> LoadShopCreditStatementEventsAsync(IReadOnlyCollection<int> partyIds)
    {
        if (partyIds.Count == 0) return (new(), new());
        var companyId = _db.ActiveCompanyId;
        // Include mismatched snapshots and parents so corruption cannot disappear behind company or party filters.
        var receipts = await _db.ShopOrderCreditReceipts.IgnoreQueryFilters().AsNoTracking().Include(r => r.Transaction).Include(r => r.TrsAccount)
            .Where(r => (!companyId.HasValue || r.CompanyId == companyId || r.Transaction.CompanyId == companyId) &&
                (partyIds.Contains(r.PartyId) || (r.Transaction.PartyId.HasValue && partyIds.Contains(r.Transaction.PartyId.Value))))
            .ToListAsync();
        var events = new List<ShopCreditStatementEvent>();
        var active = new Dictionary<int, decimal>();
        var multiCompany = receipts.Count > 0 && await _db.SystemCompanies.CountAsync() > 1;
        foreach (var r in receipts)
        {
            var t = r.Transaction;
            if (r.CompanyId != t.CompanyId || r.OrderType != t.Type || r.OrderNumber != t.Number || r.PartyId != t.PartyId ||
                (r.TrsAccount.CompanyId.HasValue && r.TrsAccount.CompanyId != t.CompanyId) ||
                (multiCompany && (t.CompanyId is not > 0 || r.TrsAccount.CompanyId != t.CompanyId)) ||
                t.Type is not (TransactionType.Sale or TransactionType.Purchase) || r.TrsAccountId <= 0 ||
                r.OperationId == Guid.Empty || r.Amount <= 0 || decimal.Round(r.Amount, 2) != r.Amount || r.Date == default || r.Date.Date < t.Date.Date ||
                r.ReversalDate.HasValue != r.ReversalOperationId.HasValue || r.ReversalDate.HasValue != r.ReversedAt.HasValue ||
                r.ReversalOperationId == Guid.Empty || (r.ReversalDate.HasValue && r.ReversalDate.Value.Date < r.Date.Date))
                throw new InvalidOperationException("تاریخچه نسیه با فاکتور و صورت‌حساب مطابقت ندارد.");
            events.Add(new ShopCreditStatementEvent(t.Id, r.PartyId, r.OrderNumber, r.OrderType, r.Date, r.Amount, false));
            if (r.ReversalDate.HasValue)
                events.Add(new ShopCreditStatementEvent(t.Id, r.PartyId, r.OrderNumber, r.OrderType, r.ReversalDate.Value, r.Amount, true));
            else active[t.Id] = active.GetValueOrDefault(t.Id) + r.Amount;
        }
        return (events, active);
    }

    private sealed record TradeInStatementEvent(int PartyId, DateTime Date, string Number, int? TransactionId,
        string Title, string Description, decimal Debit, decimal Credit);

    private async Task<(List<TradeInStatementEvent> Events, Dictionary<int, decimal> Allocations)> LoadTradeInStatementEventsAsync(IReadOnlyCollection<int> partyIds)
    {
        if (partyIds.Count == 0) return (new(), new());
        var company = _db.ActiveCompanyId;
        var sources = await _db.ShopTradeIns.IgnoreQueryFilters().AsNoTracking().Include(t => t.Party)
            .Where(t => partyIds.Contains(t.PartyId) && (!company.HasValue || t.CompanyId == company || t.Party.CompanyId == company))
            .ToListAsync();
        var sourceIds = sources.Select(t => t.Id).ToList();
        var uses = sourceIds.Count == 0 ? new List<Db.ShopTradeInUse>() : await _db.ShopTradeInUses.IgnoreQueryFilters()
            .AsNoTracking().Include(u => u.TradeIn).Include(u => u.TargetSale).Include(u => u.TrsAccount)
            .Include(u => u.TrsVoucher).ThenInclude(v => v!.Lines)
            .Where(u => sourceIds.Contains(u.TradeInId)).ToListAsync();
        var events = new List<TradeInStatementEvent>();
        var allocations = new Dictionary<int, decimal>();
        foreach (var source in sources)
        {
            var own = uses.Where(u => u.TradeInId == source.Id).ToList();
            if (source.CompanyId != source.Party.CompanyId || source.OperationId == Guid.Empty || source.Value <= 0 ||
                source.ReceivedAt == default || (source.FinalizedAt.HasValue && source.FinalizedAt.Value.Date < source.ReceivedAt.Date) ||
                (!source.FinalizedAt.HasValue && own.Count != 0) || own.Sum(u => u.Amount) > source.Value)
                throw new InvalidOperationException("پذیرش یا اعتبار تعویض با صورت‌حساب مطابقت ندارد.");
            if (!source.FinalizedAt.HasValue) continue;
            events.Add(new TradeInStatementEvent(source.PartyId, source.FinalizedAt.Value, source.Number, null,
                "اعتبار طرح تعویض", $"ارزش دستگاه {source.DeviceType} در پذیرش {source.Number}", 0m, source.Value));
            foreach (var use in own)
            {
                if (use.CompanyId != source.CompanyId || use.OperationId == Guid.Empty || use.Amount <= 0 ||
                    use.Date == default || use.Date.Date < source.FinalizedAt.Value.Date)
                    throw new InvalidOperationException("گردش اعتبار تعویض با پذیرش مطابقت ندارد.");
                if (use.Kind == TradeInUseKind.ApplyToSale)
                {
                    var sale = use.TargetSale;
                    if (sale is null || sale.Type != TransactionType.Sale || sale.CompanyId != source.CompanyId ||
                        sale.PartyId != source.PartyId || use.Date.Date < sale.Date.Date ||
                        use.TrsAccountId.HasValue || use.TrsVoucherId.HasValue)
                        throw new InvalidOperationException("فاکتور مقصد اعتبار تعویض معتبر نیست.");
                    allocations[sale.Id] = allocations.GetValueOrDefault(sale.Id) + use.Amount;
                    events.Add(new TradeInStatementEvent(source.PartyId, use.Date, source.Number, sale.Id,
                        "مصرف اعتبار تعویض", $"انتقال از پذیرش {source.Number} به فاکتور {sale.Number}", use.Amount, 0m));
                    events.Add(new TradeInStatementEvent(source.PartyId, use.Date, sale.Number, sale.Id,
                        "تسویه از اعتبار تعویض", $"تسویه فاکتور {sale.Number} از اعتبار پذیرش {source.Number}", 0m, use.Amount));
                }
                else if (use.Kind == TradeInUseKind.Refund)
                {
                    var v = use.TrsVoucher;
                    if (use.TargetSaleId.HasValue || use.TrsAccountId is not > 0 || use.TrsAccount is null || v is null ||
                        v.CompanyId != source.CompanyId || v.Kind != TreasuryKind.Payment || v.Status != TreasuryStatus.Confirmed ||
                        v.PartyId != source.PartyId || v.Date.Date != use.Date.Date || v.TotalAmount != use.Amount ||
                        v.Lines.Count != 1 || v.Lines[0].TrsAccountId != use.TrsAccountId || v.Lines[0].Amount != use.Amount ||
                        v.Lines[0].Method is PayMethod.Cheque or PayMethod.Discount ||
                        (use.TrsAccount.CompanyId.HasValue && use.TrsAccount.CompanyId != source.CompanyId))
                        throw new InvalidOperationException("سند پرداخت اعتبار تعویض با گردش مشتری مطابقت ندارد.");
                    events.Add(new TradeInStatementEvent(source.PartyId, use.Date, source.Number, null,
                        "پرداخت اعتبار تعویض", $"پرداخت اعتبار پذیرش {source.Number}", use.Amount, 0m));
                }
                else throw new InvalidOperationException("نوع گردش اعتبار تعویض معتبر نیست.");
            }
        }
        return (events, allocations);
    }

    public async Task<PartyStatementReportDto> GetPartyStatementSummaryReportAsync(PartyType? type = null, DateTime? from = null, DateTime? to = null, string? search = null)
    {
        if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
            throw new InvalidOperationException("تاریخ شروع صورت‌حساب نمی‌تواند پس از تاریخ پایان باشد.");
        var partiesQ = _db.Parties.AsNoTracking().AsQueryable();
        if (type.HasValue) partiesQ = partiesQ.Where(p => p.Type == type.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            // نام، موبایل و تلفن ثابت — با همسانسازی شماره (ارقام فارسی، +98، صفر آغازین)
            partiesQ = partiesQ.MatchingPhone(search.Trim());
        }

        var parties = await partiesQ.OrderBy(p => p.Name).ToListAsync();
        var partyIds = parties.Select(p => p.Id).ToList();
        var chequeHistory = await LoadChequeStatementHistoryAsync(partyIds);
        var shopCashEvents = (await LoadShopCashStatementEventsAsync(partyIds)).ToLookup(e => e.PartyId);
        var (ordinaryChequeEvents, activeOrdinaryCheques) = await LoadOrdinaryChequeStatementEventsAsync(partyIds);
        var ordinaryChequesByParty = ordinaryChequeEvents.ToLookup(e => e.PartyId);
        var (ordinaryInstallmentEvents, activeOrdinaryInstallments) = await LoadOrdinaryInstallmentStatementEventsAsync(partyIds);
        var ordinaryInstallmentsByParty = ordinaryInstallmentEvents.ToLookup(e => e.PartyId);
        var (shopCreditEvents, activeShopCredits) = await LoadShopCreditStatementEventsAsync(partyIds);
        var shopCreditsByParty = shopCreditEvents.ToLookup(e => e.PartyId);
        var (tradeInEvents, activeTradeInAllocations) = await LoadTradeInStatementEventsAsync(partyIds);
        var tradeInsByParty = tradeInEvents.ToLookup(e => e.PartyId);
        var legacyInboundAllocations = (await _db.SaleChequeCreditSettlements.AsNoTracking()
            .Include(s => s.SaleTransaction)
            .Where(s => s.Kind == SaleChequeCreditSettlementKind.ApplyToSale &&
                        s.TargetSaleTransactionId.HasValue &&
                        s.TargetSaleTransaction!.PartyId.HasValue &&
                        partyIds.Contains(s.TargetSaleTransaction.PartyId.Value))
            .ToListAsync())
            .Where(s => !chequeHistory.ContainsKey(s.TargetSaleTransactionId!.Value))
            .ToLookup(s => s.TargetSaleTransactionId!.Value);

        // اسناد فروشگاه
        var txns = await _db.Transactions.AsNoTracking()
            .Where(t => t.PartyId != null && partyIds.Contains(t.PartyId.Value))
            .OrderBy(t => t.Date).ThenBy(t => t.Id)
            .ToListAsync();

        var report = new PartyStatementReportDto();

        foreach (var p in parties)
        {
            var pTxns = txns.Where(t => t.PartyId == p.Id).ToList();

            decimal openDebit = 0;
            decimal openCredit = 0;
            decimal periodDebit = 0;
            decimal periodCredit = 0;

            foreach (var t in pTxns)
            {
                if (!chequeHistory.ContainsKey(t.Id) && t.SettledAmount < (t.ShopCashLedgerEnabled ? t.CashAmount : 0m) +
                    activeOrdinaryCheques.GetValueOrDefault(t.Id) + activeOrdinaryInstallments.GetValueOrDefault(t.Id) + activeShopCredits.GetValueOrDefault(t.Id) + activeTradeInAllocations.GetValueOrDefault(t.Id) + legacyInboundAllocations[t.Id].Sum(a => a.Amount))
                    throw new InvalidOperationException("تسویه فاکتور با گردش‌های ثبت‌شده صورت‌حساب مطابقت ندارد.");
                var isOpening = t.Description != null && t.Description.Contains("افتتاح");
                var isBefore = from.HasValue && t.Date.Date < from.Value.Date;

                // در منطق فروشگاهی:
                // فروش = افزایش طلب ما از شخص (بدهکار شدن طرف حساب)
                // خرید = افزایش بدهی ما به شخص (بستانکار شدن طرف حساب)
                var initialSaleTotal = t.Type == TransactionType.Sale && chequeHistory.TryGetValue(t.Id, out var saleHistory)
                    ? t.Amount - saleHistory.CurrentIncome + saleHistory.InitialIncome : t.Amount;
                decimal debit = t.Type == TransactionType.Sale ? initialSaleTotal : 0;
                decimal credit = t.Type == TransactionType.Purchase ? t.Amount : 0;

                // برای فروش دارای سود، فقط نقد اولیه در تاریخ فاکتور قرار می‌گیرد؛ وصول‌ها تاریخ مستقل دارند.
                if (t.Type == TransactionType.Sale && (t.SettledAmount > 0 ||
                    (chequeHistory.ContainsKey(t.Id) && t.CashAmount > 0)))
                {
                    var legacyAllocSum = legacyInboundAllocations[t.Id].Sum(a => a.Amount);
                    credit += chequeHistory.ContainsKey(t.Id) ? t.CashAmount :
                        Math.Max(0m, t.SettledAmount - legacyAllocSum - (t.ShopCashLedgerEnabled ? t.CashAmount : 0m) - activeOrdinaryCheques.GetValueOrDefault(t.Id) - activeOrdinaryInstallments.GetValueOrDefault(t.Id) - activeShopCredits.GetValueOrDefault(t.Id) - activeTradeInAllocations.GetValueOrDefault(t.Id));
                }
                // اگر بخشی از خرید پرداخت شده باشد، بدهکار می‌شود (کاهش بدهی)
                if (t.Type == TransactionType.Purchase && t.SettledAmount > 0)
                {
                    debit += Math.Max(0m, t.SettledAmount - (t.ShopCashLedgerEnabled ? t.CashAmount : 0m) - activeOrdinaryCheques.GetValueOrDefault(t.Id) - activeOrdinaryInstallments.GetValueOrDefault(t.Id) - activeShopCredits.GetValueOrDefault(t.Id));
                }

                if (isBefore || (isOpening && (!to.HasValue || t.Date.Date <= to.Value.Date)))
                {
                    openDebit += debit;
                    openCredit += credit;
                }
                else if (!isOpening)
                {
                    if (!to.HasValue || t.Date.Date <= to.Value.Date)
                    {
                        periodDebit += debit;
                        periodCredit += credit;
                    }
                }
                foreach (var alloc in legacyInboundAllocations[t.Id])
                {
                    if (from.HasValue && alloc.Date.Date < from.Value.Date) openCredit += alloc.Amount;
                    else if (!to.HasValue || alloc.Date.Date <= to.Value.Date) periodCredit += alloc.Amount;
                }
                if (t.Type == TransactionType.Sale && chequeHistory.TryGetValue(t.Id, out var adjustments))
                {
                    foreach (var adjustment in adjustments.Adjustments)
                    {
                        var adjustmentDebit = Math.Max(0, adjustment.Amount);
                        var adjustmentCredit = Math.Max(0, -adjustment.Amount);
                        if (from.HasValue && adjustment.Date.Date < from.Value.Date)
                        {
                            openDebit += adjustmentDebit;
                            openCredit += adjustmentCredit;
                        }
                        else if (!to.HasValue || adjustment.Date.Date <= to.Value.Date)
                        {
                            periodDebit += adjustmentDebit;
                            periodCredit += adjustmentCredit;
                        }
                    }
                    foreach (var receipt in adjustments.Receipts.Concat(adjustments.InstallmentReceipts))
                    {
                        var receiptDebit = Math.Max(0, -receipt.Amount);
                        var receiptCredit = Math.Max(0, receipt.Amount);
                        if (from.HasValue && receipt.Date.Date < from.Value.Date)
                        { openDebit += receiptDebit; openCredit += receiptCredit; }
                        else if (!to.HasValue || receipt.Date.Date <= to.Value.Date)
                        { periodDebit += receiptDebit; periodCredit += receiptCredit; }
                    }
                    foreach (var ret in adjustments.Returns)
                    {
                        var returnCredit = ret.GoodsWithVat + ret.IncomeAmount;
                        if (from.HasValue && ret.Date.Date < from.Value.Date)
                            openCredit += returnCredit;
                        else if (!to.HasValue || ret.Date.Date <= to.Value.Date)
                            periodCredit += returnCredit;
                    }
                    foreach (var cn in adjustments.Cancellations)
                    {
                        var cnCredit = cn.ReversedGoodsWithVat + Math.Max(0m, -cn.ReversedIncomeDelta);
                        var cnDebit = Math.Max(0m, cn.ReversedIncomeDelta) + cn.RefundedCashAmount;
                        if (from.HasValue && cn.Date.Date < from.Value.Date)
                        { openDebit += cnDebit; openCredit += cnCredit; }
                        else if (!to.HasValue || cn.Date.Date <= to.Value.Date)
                        { periodDebit += cnDebit; periodCredit += cnCredit; }
                    }
                    foreach (var cs in adjustments.CreditSettlements)
                    {
                        if (from.HasValue && cs.Date.Date < from.Value.Date) openDebit += cs.Amount;
                        else if (!to.HasValue || cs.Date.Date <= to.Value.Date) periodDebit += cs.Amount;
                    }
                    foreach (var ia in adjustments.InboundAllocations)
                    {
                        if (from.HasValue && ia.Date.Date < from.Value.Date) openCredit += ia.Amount;
                        else if (!to.HasValue || ia.Date.Date <= to.Value.Date) periodCredit += ia.Amount;
                    }
                }
            }

            foreach (var e in shopCashEvents[p.Id])
            {
                var isDebit = (e.Type == TransactionType.Purchase) != e.IsReversal;
                if ((e.IsOpening && (!to.HasValue || e.Date.Date <= to.Value.Date)) || (from.HasValue && e.Date.Date < from.Value.Date))
                { if (isDebit) openDebit += e.Amount; else openCredit += e.Amount; }
                else if (!to.HasValue || e.Date.Date <= to.Value.Date)
                { if (isDebit) periodDebit += e.Amount; else periodCredit += e.Amount; }
            }

            foreach (var e in ordinaryChequesByParty[p.Id])
            {
                var isDebit = (e.Type == TransactionType.Purchase) != e.IsReversal;
                if (from.HasValue && e.Date.Date < from.Value.Date)
                { if (isDebit) openDebit += e.Amount; else openCredit += e.Amount; }
                else if (!to.HasValue || e.Date.Date <= to.Value.Date)
                { if (isDebit) periodDebit += e.Amount; else periodCredit += e.Amount; }
            }

            foreach (var e in ordinaryInstallmentsByParty[p.Id])
            {
                var isDebit = (e.Type == TransactionType.Purchase) != e.IsReversal;
                if (from.HasValue && e.Date.Date < from.Value.Date)
                { if (isDebit) openDebit += e.Amount; else openCredit += e.Amount; }
                else if (!to.HasValue || e.Date.Date <= to.Value.Date)
                { if (isDebit) periodDebit += e.Amount; else periodCredit += e.Amount; }
            }

            foreach (var e in shopCreditsByParty[p.Id])
            {
                var isDebit = (e.Type == TransactionType.Purchase) != e.IsReversal;
                if (from.HasValue && e.Date.Date < from.Value.Date)
                { if (isDebit) openDebit += e.Amount; else openCredit += e.Amount; }
                else if (!to.HasValue || e.Date.Date <= to.Value.Date)
                { if (isDebit) periodDebit += e.Amount; else periodCredit += e.Amount; }
            }

            foreach (var e in tradeInsByParty[p.Id])
            {
                if (from.HasValue && e.Date.Date < from.Value.Date) { openDebit += e.Debit; openCredit += e.Credit; }
                else if (!to.HasValue || e.Date.Date <= to.Value.Date) { periodDebit += e.Debit; periodCredit += e.Credit; }
            }

            var item = new PartyBalanceSummaryDto
            {
                PartyId = p.Id,
                PartyName = p.Name,
                Mobile = p.Mobile,
                PartyType = p.Type,
                OpeningDebit = openDebit,
                OpeningCredit = openCredit,
                PeriodDebit = periodDebit,
                PeriodCredit = periodCredit
            };

            report.Items.Add(item);

            if (item.RemainingBalance > 0)
            {
                report.TotalReceivable += item.RemainingBalance;
                report.DebtorCount++;
            }
            else if (item.RemainingBalance < 0)
            {
                report.TotalPayable += Math.Abs(item.RemainingBalance);
                report.CreditorCount++;
            }
        }

        return report;
    }

    public async Task<PartyStatementDetailDto> GetPartyStatementDetailAsync(int partyId, DateTime? from = null, DateTime? to = null)
    {
        if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
            throw new InvalidOperationException("تاریخ شروع صورت‌حساب نمی‌تواند پس از تاریخ پایان باشد.");
        var party = await _db.Parties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == partyId)
            ?? throw new InvalidOperationException("طرف حساب یافت نشد.");

        var txns = await _db.Transactions.AsNoTracking()
            .Include(t => t.Lines)
            .Where(t => t.PartyId == partyId)
            .OrderBy(t => t.Date).ThenBy(t => t.Id)
            .ToListAsync();

        // اصل سود و تعدیلات در تاریخ واقعی خودشان نمایش داده می‌شوند.
        var chequeHistory = await LoadChequeStatementHistoryAsync(new[] { partyId });
        var shopCashEvents = await LoadShopCashStatementEventsAsync(new[] { partyId });
        var (ordinaryChequeEvents, activeOrdinaryCheques) = await LoadOrdinaryChequeStatementEventsAsync(new[] { partyId });
        var (ordinaryInstallmentEvents, activeOrdinaryInstallments) = await LoadOrdinaryInstallmentStatementEventsAsync(new[] { partyId });
        var (shopCreditEvents, activeShopCredits) = await LoadShopCreditStatementEventsAsync(new[] { partyId });
        var (tradeInEvents, activeTradeInAllocations) = await LoadTradeInStatementEventsAsync(new[] { partyId });
        var legacyInboundAllocations = (await _db.SaleChequeCreditSettlements.AsNoTracking()
            .Include(s => s.SaleTransaction)
            .Where(s => s.Kind == SaleChequeCreditSettlementKind.ApplyToSale &&
                        s.TargetSaleTransactionId.HasValue &&
                        s.TargetSaleTransaction!.PartyId == partyId)
            .ToListAsync())
            .Where(s => !chequeHistory.ContainsKey(s.TargetSaleTransactionId!.Value))
            .ToLookup(s => s.TargetSaleTransactionId!.Value);

        var result = new PartyStatementDetailDto
        {
            PartyId = party.Id,
            PartyName = party.Name,
            Mobile = party.Mobile,
            PartyType = party.Type
        };

        decimal runningBalance = 0; // مثبت = طلب ما (بدهکار)، منفی = بدهی ما (بستانکار)
        decimal openDebit = 0;
        decimal openCredit = 0;

        foreach (var t in txns)
        {
            if (!chequeHistory.ContainsKey(t.Id) && t.SettledAmount < (t.ShopCashLedgerEnabled ? t.CashAmount : 0m) +
                activeOrdinaryCheques.GetValueOrDefault(t.Id) + activeOrdinaryInstallments.GetValueOrDefault(t.Id) + activeShopCredits.GetValueOrDefault(t.Id) + activeTradeInAllocations.GetValueOrDefault(t.Id) + legacyInboundAllocations[t.Id].Sum(a => a.Amount))
                throw new InvalidOperationException("تسویه فاکتور با گردش‌های ثبت‌شده صورت‌حساب مطابقت ندارد.");
            var isOpening = t.Description != null && t.Description.Contains("افتتاح");
            var isBefore = from.HasValue && t.Date.Date < from.Value.Date;

            var initialSaleTotal = t.Type == TransactionType.Sale && chequeHistory.TryGetValue(t.Id, out var saleHistory)
                ? t.Amount - saleHistory.CurrentIncome + saleHistory.InitialIncome : t.Amount;
            decimal debit = t.Type == TransactionType.Sale ? initialSaleTotal : 0;
            decimal credit = t.Type == TransactionType.Purchase ? t.Amount : 0;
            // Initial cash still occurred even when cancellation later brought SettledAmount to zero.
            if (t.SettledAmount > 0 || (t.Type == TransactionType.Sale && chequeHistory.ContainsKey(t.Id) && t.CashAmount > 0))
            {
                if (t.Type == TransactionType.Sale)
                {
                    var baseSettled = chequeHistory.ContainsKey(t.Id) ? t.CashAmount :
                        t.SettledAmount - (t.ShopCashLedgerEnabled ? t.CashAmount : 0m) - activeOrdinaryCheques.GetValueOrDefault(t.Id) - activeOrdinaryInstallments.GetValueOrDefault(t.Id) - activeShopCredits.GetValueOrDefault(t.Id);
                    credit += Math.Max(0m, baseSettled - legacyInboundAllocations[t.Id].Sum(a => a.Amount) - (chequeHistory.ContainsKey(t.Id) ? 0m : activeTradeInAllocations.GetValueOrDefault(t.Id)));
                }
                if (t.Type == TransactionType.Purchase) debit += Math.Max(0m, t.SettledAmount - (t.ShopCashLedgerEnabled ? t.CashAmount : 0m) - activeOrdinaryCheques.GetValueOrDefault(t.Id) - activeOrdinaryInstallments.GetValueOrDefault(t.Id) - activeShopCredits.GetValueOrDefault(t.Id));
            }

            if (isBefore || (isOpening && (!to.HasValue || t.Date.Date <= to.Value.Date)))
            {
                openDebit += debit;
                openCredit += credit;
                runningBalance += (debit - credit);
            }
        }

        if (from.HasValue)
        {
            foreach (var adjustment in chequeHistory.Values.SelectMany(x => x.Adjustments).Where(x => x.Date.Date < from.Value.Date))
            {
                openDebit += Math.Max(0, adjustment.Amount);
                openCredit += Math.Max(0, -adjustment.Amount);
                runningBalance += adjustment.Amount;
            }
            foreach (var receipt in chequeHistory.Values.SelectMany(x => x.Receipts.Concat(x.InstallmentReceipts)).Where(x => x.Date.Date < from.Value.Date))
            {
                openDebit += Math.Max(0, -receipt.Amount);
                openCredit += Math.Max(0, receipt.Amount);
                runningBalance -= receipt.Amount;
            }
            foreach (var ret in chequeHistory.Values.SelectMany(x => x.Returns).Where(x => x.Date.Date < from.Value.Date))
            {
                var returnCredit = ret.GoodsWithVat + ret.IncomeAmount;
                openCredit += returnCredit;
                runningBalance -= returnCredit;
            }
            foreach (var cn in chequeHistory.Values.SelectMany(x => x.Cancellations).Where(x => x.Date.Date < from.Value.Date))
            {
                var cnCredit = cn.ReversedGoodsWithVat + Math.Max(0m, -cn.ReversedIncomeDelta);
                var cnDebit = Math.Max(0m, cn.ReversedIncomeDelta) + cn.RefundedCashAmount;
                openDebit += cnDebit;
                openCredit += cnCredit;
                runningBalance += cnDebit - cnCredit;
            }
            foreach (var cs in chequeHistory.Values.SelectMany(x => x.CreditSettlements).Where(x => x.Date.Date < from.Value.Date))
            {
                openDebit += cs.Amount;
                runningBalance += cs.Amount;
            }
            foreach (var ia in chequeHistory.Values.SelectMany(x => x.InboundAllocations).Where(x => x.Date.Date < from.Value.Date))
            {
                openCredit += ia.Amount;
                runningBalance -= ia.Amount;
            }
            foreach (var alloc in legacyInboundAllocations.SelectMany(g => g).Where(x => x.Date.Date < from.Value.Date))
            {
                openCredit += alloc.Amount;
                runningBalance -= alloc.Amount;
            }
        }
        foreach (var e in shopCashEvents.Where(e => (e.IsOpening && (!to.HasValue || e.Date.Date <= to.Value.Date)) ||
                     (from.HasValue && e.Date.Date < from.Value.Date)))
        {
            var isDebit = (e.Type == TransactionType.Purchase) != e.IsReversal;
            if (isDebit) { openDebit += e.Amount; runningBalance += e.Amount; }
            else { openCredit += e.Amount; runningBalance -= e.Amount; }
        }
        foreach (var e in ordinaryChequeEvents.Where(e => from.HasValue && e.Date.Date < from.Value.Date))
        {
            var isDebit = (e.Type == TransactionType.Purchase) != e.IsReversal;
            if (isDebit) { openDebit += e.Amount; runningBalance += e.Amount; }
            else { openCredit += e.Amount; runningBalance -= e.Amount; }
        }
        foreach (var e in ordinaryInstallmentEvents.Where(e => from.HasValue && e.Date.Date < from.Value.Date))
        {
            var isDebit = (e.Type == TransactionType.Purchase) != e.IsReversal;
            if (isDebit) { openDebit += e.Amount; runningBalance += e.Amount; }
            else { openCredit += e.Amount; runningBalance -= e.Amount; }
        }
        foreach (var e in shopCreditEvents.Where(e => from.HasValue && e.Date.Date < from.Value.Date))
        {
            var isDebit = (e.Type == TransactionType.Purchase) != e.IsReversal;
            if (isDebit) { openDebit += e.Amount; runningBalance += e.Amount; }
            else { openCredit += e.Amount; runningBalance -= e.Amount; }
        }
        foreach (var e in tradeInEvents.Where(e => from.HasValue && e.Date.Date < from.Value.Date))
        {
            openDebit += e.Debit; openCredit += e.Credit; runningBalance += e.Debit - e.Credit;
        }
        result.OpeningBalance = runningBalance;

        // سطر ابتدایی: مانده اول دوره / افتتاحیه
        if (runningBalance != 0 || openDebit > 0 || openCredit > 0)
        {
            result.Rows.Add(new PartyStatementRowDto
            {
                Date = from ?? (txns.FirstOrDefault()?.Date ?? DateTime.Today),
                Title = "مانده افتتاح حساب (اول دوره)",
                Description = "مانده منتقل‌شده از دوره‌های قبل و اسناد افتتاحیه",
                IsOpening = true,
                Debit = runningBalance > 0 ? runningBalance : 0,
                Credit = runningBalance < 0 ? Math.Abs(runningBalance) : 0,
                Balance = runningBalance
            });
        }

        // سطرهای گردش طی دوره
        var periodTxns = txns.Where(t =>
            (t.Type == TransactionType.Sale || t.Type == TransactionType.Purchase) &&
            !(t.Description != null && t.Description.Contains("افتتاح")) &&
            (!from.HasValue || t.Date.Date >= from.Value.Date) &&
            (!to.HasValue || t.Date.Date <= to.Value.Date)
        ).ToList();

        foreach (var t in periodTxns)
        {
            ChequeStatementHistory? saleHistory = null;
            if (t.Type == TransactionType.Sale) chequeHistory.TryGetValue(t.Id, out saleHistory);
            var income = saleHistory?.InitialIncome ?? 0m;
            decimal debit = t.Type == TransactionType.Sale ? t.Amount - (saleHistory?.CurrentIncome ?? 0m) : 0;
            decimal credit = t.Type == TransactionType.Purchase ? t.Amount : 0;
            decimal? saleDiscount = t.Type == TransactionType.Sale ? t.Lines.Sum(l => l.DiscountAmount) : null;
            if (t.Type == TransactionType.Sale)
            {
                result.SaleGoodsTotal += debit;
                result.SaleIssuedDiscountTotal += saleDiscount ?? 0m;
                result.SaleChequeIncomeTotal += income;
            }

            runningBalance += (debit - credit);
            result.TotalDebit += debit;
            result.TotalCredit += credit;

            result.Rows.Add(new PartyStatementRowDto
            {
                Date = t.Date,
                TransactionId = t.Id,
                DocumentNumber = t.Number.ToString(),
                Title = t.Type == TransactionType.Sale ? "فاکتور فروش" : "فاکتور خرید",
                Description = t.Description ?? "",
                SaleInvoiceDiscount = saleDiscount,
                Debit = debit,
                Credit = credit,
                Balance = runningBalance
            });

            if (income > 0)
            {
                runningBalance += income;
                result.TotalDebit += income;
                result.Rows.Add(new PartyStatementRowDto
                {
                    Date = t.Date,
                    TransactionId = t.Id,
                    DocumentNumber = t.Number,
                    Title = "تاخیر تادیه چک",
                    Description = DescribeChequeIncome(saleHistory, t.Number),
                    Debit = income,
                    Balance = runningBalance
                });
            }

            // در فاکتور دارای سود فقط دریافت نقدی اولیه در تاریخ فاکتور قرار می‌گیرد.
            var baseInitialSettled = saleHistory is not null ? t.CashAmount :
                t.SettledAmount - (t.ShopCashLedgerEnabled ? t.CashAmount : 0m) - activeOrdinaryCheques.GetValueOrDefault(t.Id) - activeOrdinaryInstallments.GetValueOrDefault(t.Id) - activeShopCredits.GetValueOrDefault(t.Id);
            var initialSettled = t.Type == TransactionType.Sale
                ? Math.Max(0m, baseInitialSettled - legacyInboundAllocations[t.Id].Sum(a => a.Amount) - (saleHistory is null ? activeTradeInAllocations.GetValueOrDefault(t.Id) : 0m))
                : baseInitialSettled;
            if (initialSettled > 0)
            {
                var settleDebit = t.Type == TransactionType.Purchase ? initialSettled : 0;
                var settleCredit = t.Type == TransactionType.Sale ? initialSettled : 0;
                runningBalance += (settleDebit - settleCredit);
                result.TotalDebit += settleDebit;
                result.TotalCredit += settleCredit;

                result.Rows.Add(new PartyStatementRowDto
                {
                    Date = t.Date,
                    TransactionId = t.Id,
                    DocumentNumber = t.Number.ToString(),
                    Title = t.Type == TransactionType.Sale ? "تسویه / دریافتی فاکتور" : "پرداختی فاکتور خرید",
                    Description = $"تسویه بابت فاکتور شماره {t.Number}",
                    Debit = settleDebit,
                    Credit = settleCredit,
                    Balance = runningBalance
                });
            }
        }

        foreach (var t in txns.Where(t => t.Type == TransactionType.Sale))
        {
            if (!chequeHistory.TryGetValue(t.Id, out var history)) continue;
            foreach (var adjustment in history.Adjustments.Where(x =>
                         (!from.HasValue || x.Date.Date >= from.Value.Date) &&
                         (!to.HasValue || x.Date.Date <= to.Value.Date)))
            {
                var debit = Math.Max(0, adjustment.Amount);
                var credit = Math.Max(0, -adjustment.Amount);
                result.TotalDebit += debit;
                result.TotalCredit += credit;
                result.SaleChequeIncomeTotal += adjustment.Amount;
                result.Rows.Add(new PartyStatementRowDto
                {
                    Date = adjustment.Date, TransactionId = t.Id, DocumentNumber = t.Number,
                    Title = "تعدیل تاخیر تادیه چک", Description = $"تغییر سررسید فاکتور شماره {t.Number}",
                    Debit = debit, Credit = credit
                });
            }
            foreach (var receipt in history.Receipts.Where(x =>
                         (!from.HasValue || x.Date.Date >= from.Value.Date) &&
                         (!to.HasValue || x.Date.Date <= to.Value.Date)))
            {
                var receiptDebit = Math.Max(0, -receipt.Amount);
                var receiptCredit = Math.Max(0, receipt.Amount);
                result.TotalDebit += receiptDebit;
                result.TotalCredit += receiptCredit;
                result.Rows.Add(new PartyStatementRowDto
                {
                    Date = receipt.Date, TransactionId = t.Id, DocumentNumber = t.Number,
                    Title = receipt.IsEndorsement ? "پاس چک خرج‌شده بابت هزینه" : receipt.Amount < 0 ? "لغو وصول چک" : "وصول چک",
                    Description = $"گردش چک فاکتور شماره {t.Number}", Debit = receiptDebit, Credit = receiptCredit
                });
            }
            foreach (var ret in history.Returns.Where(x =>
                         (!from.HasValue || x.Date.Date >= from.Value.Date) &&
                         (!to.HasValue || x.Date.Date <= to.Value.Date)))
            {
                result.TotalCredit += ret.GoodsWithVat;
                result.SaleGoodsTotal -= ret.GoodsWithVat;
                result.Rows.Add(new PartyStatementRowDto
                {
                    Date = ret.Date, TransactionId = t.Id, DocumentNumber = ret.ReturnNumber,
                    Title = "برگشت از فروش",
                    Description = $"برگشت کالا از فاکتور شماره {t.Number}",
                    Credit = ret.GoodsWithVat
                });
                if (ret.IncomeAmount > 0)
                {
                    result.TotalCredit += ret.IncomeAmount;
                    result.SaleChequeIncomeTotal -= ret.IncomeAmount;
                    result.Rows.Add(new PartyStatementRowDto
                    {
                        Date = ret.Date, TransactionId = t.Id, DocumentNumber = ret.ReturnNumber,
                        Title = "برگشت تاخیر تادیه چک",
                        Description = $"کسر نسبی سود فاکتور شماره {t.Number}",
                        Credit = ret.IncomeAmount
                    });
                }
            }
            foreach (var instRec in history.InstallmentReceipts.Where(x =>
                         (!from.HasValue || x.Date.Date >= from.Value.Date) &&
                         (!to.HasValue || x.Date.Date <= to.Value.Date)))
            {
                var instDebit = Math.Max(0, -instRec.Amount);
                var instCredit = Math.Max(0, instRec.Amount);
                result.TotalDebit += instDebit;
                result.TotalCredit += instCredit;
                result.Rows.Add(new PartyStatementRowDto
                {
                    Date = instRec.Date, TransactionId = t.Id, DocumentNumber = t.Number,
                    Title = instRec.Amount < 0 ? "لغو دریافت قسط" : "دریافت قسط",
                    Description = $"گردش قسط فاکتور شماره {t.Number}",
                    Debit = instDebit, Credit = instCredit
                });
            }
            foreach (var cn in history.Cancellations.Where(x =>
                         (!from.HasValue || x.Date.Date >= from.Value.Date) &&
                         (!to.HasValue || x.Date.Date <= to.Value.Date)))
            {
                if (cn.ReversedGoodsWithVat > 0)
                {
                    result.TotalCredit += cn.ReversedGoodsWithVat;
                    result.SaleGoodsTotal -= cn.ReversedGoodsWithVat;
                    result.Rows.Add(new PartyStatementRowDto
                    {
                        Date = cn.Date, TransactionId = t.Id, DocumentNumber = t.Number,
                        Title = "ابطال فاکتور فروش",
                        Description = $"برگشت اقلام باقی‌مانده بابت ابطال فاکتور {t.Number}",
                        Credit = cn.ReversedGoodsWithVat
                    });
                }
                if (cn.ReversedIncomeDelta != 0)
                {
                    var incDebit = Math.Max(0m, cn.ReversedIncomeDelta);
                    var incCredit = Math.Max(0m, -cn.ReversedIncomeDelta);
                    result.TotalDebit += incDebit;
                    result.TotalCredit += incCredit;
                    result.SaleChequeIncomeTotal += cn.ReversedIncomeDelta;
                    result.Rows.Add(new PartyStatementRowDto
                    {
                        Date = cn.Date, TransactionId = t.Id, DocumentNumber = t.Number,
                        Title = "معکوس تاخیر تادیه چک (ابطال)",
                        Description = $"تسویه ردیف مالی بابت ابطال فاکتور {t.Number}",
                        Debit = incDebit, Credit = incCredit
                    });
                }
                if (cn.RefundedCashAmount > 0)
                {
                    result.TotalDebit += cn.RefundedCashAmount;
                    result.Rows.Add(new PartyStatementRowDto
                    {
                        Date = cn.Date, TransactionId = t.Id, DocumentNumber = t.Number,
                        Title = "استرداد پیش‌دریافت نقدی",
                        Description = $"استرداد نقد اولیه بابت ابطال فاکتور {t.Number}",
                        Debit = cn.RefundedCashAmount
                    });
                }
            }
            foreach (var cs in history.CreditSettlements.Where(x =>
                         (!from.HasValue || x.Date.Date >= from.Value.Date) &&
                         (!to.HasValue || x.Date.Date <= to.Value.Date)))
            {
                result.TotalDebit += cs.Amount;
                result.Rows.Add(new PartyStatementRowDto
                {
                    Date = cs.Date, TransactionId = t.Id, DocumentNumber = t.Number,
                    Title = cs.Kind == SaleChequeCreditSettlementKind.RefundPayment ? "پرداخت بستانکاری به مشتری" : "انتقال بستانکاری به فاکتور دیگر",
                    Description = cs.Kind == SaleChequeCreditSettlementKind.RefundPayment
                        ? $"پرداخت وجه بستانکاری فاکتور {t.Number}"
                        : $"انتقال بستانکاری فاکتور {t.Number} به فاکتور {cs.TargetNumber}",
                    Debit = cs.Amount
                });
            }
            foreach (var ia in history.InboundAllocations.Where(x =>
                         (!from.HasValue || x.Date.Date >= from.Value.Date) &&
                         (!to.HasValue || x.Date.Date <= to.Value.Date)))
            {
                result.TotalCredit += ia.Amount;
                result.Rows.Add(new PartyStatementRowDto
                {
                    Date = ia.Date, TransactionId = t.Id, DocumentNumber = t.Number,
                    Title = "تسویه از محل بستانکاری",
                    Description = $"تسویه فاکتور {t.Number} از محل بستانکاری فاکتور {ia.SourceNumber}",
                    Credit = ia.Amount
                });
            }
        }
        foreach (var t in txns.Where(t => t.Type == TransactionType.Sale && !chequeHistory.ContainsKey(t.Id)))
        {
            foreach (var ia in legacyInboundAllocations[t.Id].Where(x =>
                         (!from.HasValue || x.Date.Date >= from.Value.Date) &&
                         (!to.HasValue || x.Date.Date <= to.Value.Date)))
            {
                result.TotalCredit += ia.Amount;
                result.Rows.Add(new PartyStatementRowDto
                {
                    Date = ia.Date, TransactionId = t.Id, DocumentNumber = t.Number,
                    Title = "تسویه از محل بستانکاری",
                    Description = $"تسویه فاکتور {t.Number} از محل بستانکاری فاکتور {ia.SaleTransaction.Number}",
                    Credit = ia.Amount
                });
            }
        }
        foreach (var e in shopCashEvents.Where(e => !e.IsOpening &&
                     (!from.HasValue || e.Date.Date >= from.Value.Date) && (!to.HasValue || e.Date.Date <= to.Value.Date)))
        {
            var isDebit = (e.Type == TransactionType.Purchase) != e.IsReversal;
            var debit = isDebit ? e.Amount : 0m;
            var credit = isDebit ? 0m : e.Amount;
            result.TotalDebit += debit;
            result.TotalCredit += credit;
            result.Rows.Add(new PartyStatementRowDto
            {
                Date = e.Date, TransactionId = e.TransactionId, DocumentNumber = e.OrderNumber,
                Title = e.IsReversal ? "اصلاح نقد فاکتور" : e.Type == TransactionType.Sale ? "دریافت نقدی فاکتور" : "پرداخت نقدی فاکتور",
                Description = $"گردش نقدی فاکتور شماره {e.OrderNumber}", Debit = debit, Credit = credit
            });
        }
        foreach (var e in ordinaryChequeEvents.Where(e =>
                     (!from.HasValue || e.Date.Date >= from.Value.Date) && (!to.HasValue || e.Date.Date <= to.Value.Date)))
        {
            var isDebit = (e.Type == TransactionType.Purchase) != e.IsReversal;
            var debit = isDebit ? e.Amount : 0m;
            var credit = isDebit ? 0m : e.Amount;
            result.TotalDebit += debit;
            result.TotalCredit += credit;
            result.Rows.Add(new PartyStatementRowDto
            {
                Date = e.Date, TransactionId = e.TransactionId, DocumentNumber = e.OrderNumber,
                Title = e.IsEndorsement ? "پاس چک خرج‌شده بابت هزینه" : e.IsReversal ? (e.Type == TransactionType.Sale ? "لغو وصول چک" : "لغو پرداخت چک") :
                    (e.Type == TransactionType.Sale ? "وصول چک فروش" : "پرداخت چک خرید"),
                Description = $"گردش چک فاکتور شماره {e.OrderNumber}", Debit = debit, Credit = credit
            });
        }
        foreach (var e in ordinaryInstallmentEvents.Where(e =>
                     (!from.HasValue || e.Date.Date >= from.Value.Date) && (!to.HasValue || e.Date.Date <= to.Value.Date)))
        {
            var isDebit = (e.Type == TransactionType.Purchase) != e.IsReversal;
            var debit = isDebit ? e.Amount : 0m;
            var credit = isDebit ? 0m : e.Amount;
            result.TotalDebit += debit;
            result.TotalCredit += credit;
            result.Rows.Add(new PartyStatementRowDto
            {
                Date = e.Date, TransactionId = e.TransactionId, DocumentNumber = e.OrderNumber,
                Title = e.IsReversal ? (e.Type == TransactionType.Sale ? "لغو دریافت قسط" : "لغو پرداخت قسط") :
                    (e.Type == TransactionType.Sale ? "دریافت قسط فروش" : "پرداخت قسط خرید"),
                Description = $"گردش قسط فاکتور شماره {e.OrderNumber}", Debit = debit, Credit = credit
            });
        }
        foreach (var e in shopCreditEvents.Where(e =>
                     (!from.HasValue || e.Date.Date >= from.Value.Date) && (!to.HasValue || e.Date.Date <= to.Value.Date)))
        {
            var isDebit = (e.Type == TransactionType.Purchase) != e.IsReversal;
            var debit = isDebit ? e.Amount : 0m;
            var credit = isDebit ? 0m : e.Amount;
            result.TotalDebit += debit;
            result.TotalCredit += credit;
            result.Rows.Add(new PartyStatementRowDto
            {
                Date = e.Date, TransactionId = e.TransactionId, DocumentNumber = e.OrderNumber,
                Title = e.IsReversal ? (e.Type == TransactionType.Sale ? "لغو دریافت نسیه" : "لغو پرداخت نسیه") :
                    (e.Type == TransactionType.Sale ? "دریافت نسیه فروش" : "پرداخت نسیه خرید"),
                Description = $"گردش نسیه فاکتور شماره {e.OrderNumber}", Debit = debit, Credit = credit
            });
        }
        foreach (var e in tradeInEvents.Where(e => (!from.HasValue || e.Date.Date >= from.Value.Date) &&
                     (!to.HasValue || e.Date.Date <= to.Value.Date)))
        {
            result.TotalDebit += e.Debit; result.TotalCredit += e.Credit;
            result.Rows.Add(new PartyStatementRowDto
            {
                Date = e.Date, TransactionId = e.TransactionId, DocumentNumber = e.Number,
                Title = e.Title, Description = e.Description, Debit = e.Debit, Credit = e.Credit
            });
        }
        var openingRows = result.Rows.Where(r => r.IsOpening).ToList();
        var datedRows = result.Rows.Where(r => !r.IsOpening).OrderBy(r => r.Date.Date).ThenBy(r => r.TransactionId).ToList();
        runningBalance = result.OpeningBalance;
        foreach (var row in datedRows)
        {
            runningBalance += row.Debit - row.Credit;
            row.Balance = runningBalance;
        }
        result.Rows = openingRows.Concat(datedRows).ToList();
        result.ClosingBalance = runningBalance;
        return result;
    }

}
