using System.Data;
using Inventory.Api.Data;
using Inventory.Api.Services.Invoicing;
using Inventory.Api.Services.Treasury;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Repairs;

/// <summary>پذیرش تعویض و مصرف اعتبار واقعی مشتری، بدون وابستگی به حسابداری ERP.</summary>
public class ShopTradeInService
{
    private readonly AppDbContext _db;
    private const decimal MaxMoney = 9999999999999999.99m;
    public ShopTradeInService(AppDbContext db) => _db = db;

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void CheckMoney(decimal amount)
    {
        if (amount <= 0 || amount > MaxMoney || decimal.Truncate(amount) != amount)
            throw new InvalidOperationException("مبلغ باید عدد صحیح ریالی و بزرگ‌تر از صفر باشد.");
    }

    public async Task<List<ShopTradeInDto>> ListAsync()
    {
        var records = await _db.ShopTradeIns.AsNoTracking().Include(t => t.Party)
            .OrderByDescending(t => t.ReceivedAt).ThenByDescending(t => t.Id).ToListAsync();
        var ids = records.Select(r => r.Id).ToList();
        var uses = ids.Count == 0 ? new List<ShopTradeInUse>() : await _db.ShopTradeInUses.AsNoTracking()
            .Include(u => u.TargetSale).Include(u => u.TrsAccount).Where(u => ids.Contains(u.TradeInId)).ToListAsync();
        return records.Select(r => ToDto(r, uses.Where(u => u.TradeInId == r.Id).ToList())).ToList();
    }

    public async Task<List<LookupItem>> CustomersAsync() => await _db.Parties.AsNoTracking()
        .Where(p => p.Type == PartyType.Customer && p.IsActive).OrderBy(p => p.Name)
        .Select(p => new LookupItem { Id = p.Id, Name = p.Name }).ToListAsync();

    public async Task<List<ShopTradeInTargetSaleDto>> TargetSalesAsync(int partyId)
    {
        if (!await _db.Parties.AnyAsync(p => p.Id == partyId && p.Type == PartyType.Customer))
            throw new InvalidOperationException("مشتری در شرکت جاری یافت نشد.");
        return await _db.Transactions.AsNoTracking().Where(t => t.Type == TransactionType.Sale && t.PartyId == partyId &&
                !_db.SaleChequeCancellations.Any(c => c.SaleTransactionId == t.Id))
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .Select(t => new ShopTradeInTargetSaleDto { Id = t.Id, Number = t.Number, Date = t.Date, TotalAmount = t.Amount }).ToListAsync();
    }

    private static ShopTradeInDto ToDto(ShopTradeIn r, List<ShopTradeInUse> uses)
    {
        if (r.OperationId == Guid.Empty || r.Party.CompanyId != r.CompanyId || r.Value <= 0 ||
            r.ReceivedAt == default || (r.FinalizedAt.HasValue && r.FinalizedAt.Value.Date < r.ReceivedAt.Date) ||
            (!r.FinalizedAt.HasValue && uses.Count > 0) || uses.Any(u => u.CompanyId != r.CompanyId || u.OperationId == Guid.Empty ||
                u.Amount <= 0 || u.Date == default || !r.FinalizedAt.HasValue || u.Date.Date < r.FinalizedAt.Value.Date) ||
            uses.Sum(u => u.Amount) > r.Value)
            throw new InvalidOperationException("سوابق اعتبار تعویض با پذیرش مطابقت ندارد؛ ابتدا تاریخچه را بررسی کنید.");
        return new ShopTradeInDto
        {
            Id = r.Id, OperationId = r.OperationId, Number = r.Number, PartyId = r.PartyId, PartyName = r.Party.Name,
            DeviceType = r.DeviceType, DeviceModel = r.DeviceModel, SerialNumber = r.SerialNumber, Note = r.Note,
            Value = r.Value, ReceivedAt = r.ReceivedAt, FinalizedAt = r.FinalizedAt, UsedAmount = uses.Sum(u => u.Amount),
            Uses = uses.OrderByDescending(u => u.Date).ThenByDescending(u => u.Id).Select(u => new ShopTradeInUseDto
            {
                Id = u.Id, Kind = u.Kind, TargetSaleId = u.TargetSaleId, TargetSaleNumber = u.TargetSale?.Number,
                TrsAccountName = u.TrsAccount?.Name, Amount = u.Amount, Date = u.Date, CreatedBy = u.CreatedBy
            }).ToList()
        };
    }

    public async Task<ShopTradeInDto> SaveDraftAsync(ShopTradeInDto dto, string? user)
    {
        ArgumentNullException.ThrowIfNull(dto);
        CheckMoney(dto.Value);
        if (dto.OperationId == Guid.Empty || dto.PartyId <= 0 || dto.ReceivedAt == default ||
            string.IsNullOrWhiteSpace(dto.DeviceType) || dto.DeviceType.Trim().Length > 100 ||
            dto.DeviceModel?.Trim().Length > 200 || dto.SerialNumber?.Trim().Length > 100 ||
            dto.Note?.Trim().Length > 500 || user?.Length > 120)
            throw new InvalidOperationException("مشخصات پذیرش تعویض معتبر نیست.");
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var party = await _db.Parties.FirstOrDefaultAsync(p => p.Id == dto.PartyId && p.Type == PartyType.Customer)
            ?? throw new InvalidOperationException("مشتری انتخاب‌شده یافت نشد.");
        ShopTradeIn record;
        if (dto.Id == 0)
        {
            var prior = await _db.ShopTradeIns.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.OperationId == dto.OperationId);
            if (prior is not null)
            {
                if (prior.CompanyId != party.CompanyId || prior.PartyId != party.Id || prior.Value != dto.Value ||
                    prior.ReceivedAt.Date != dto.ReceivedAt.Date || prior.DeviceType != dto.DeviceType.Trim() ||
                    prior.DeviceModel != Optional(dto.DeviceModel) || prior.SerialNumber != Optional(dto.SerialNumber) || prior.Note != Optional(dto.Note))
                    throw new InvalidOperationException("شناسه عملیات قبلاً با اطلاعات دیگری استفاده شده است.");
                await transaction.CommitAsync();
                return (await ListAsync()).First(t => t.Id == prior.Id);
            }
            record = new ShopTradeIn { OperationId = dto.OperationId, PartyId = party.Id, Party = party, CreatedBy = user };
            _db.ShopTradeIns.Add(record);
        }
        else
        {
            record = await _db.ShopTradeIns.Include(t => t.Party).FirstOrDefaultAsync(t => t.Id == dto.Id)
                ?? throw new InvalidOperationException("پذیرش تعویض یافت نشد.");
            if (record.OperationId != dto.OperationId || record.FinalizedAt.HasValue)
                throw new InvalidOperationException("پذیرش نهایی‌شده قابل ویرایش نیست.");
            record.PartyId = party.Id;
            record.Party = party;
        }
        record.DeviceType = dto.DeviceType.Trim();
        record.DeviceModel = Optional(dto.DeviceModel);
        record.SerialNumber = Optional(dto.SerialNumber);
        record.Note = Optional(dto.Note);
        record.Value = dto.Value;
        record.ReceivedAt = dto.ReceivedAt.Date;
        await _db.SaveChangesAsync();
        if (string.IsNullOrEmpty(record.Number))
        {
            record.Number = $"TI-{record.Id:0000}";
            await _db.SaveChangesAsync();
        }
        await transaction.CommitAsync();
        return ToDto(record, new());
    }

    public async Task<ShopTradeInDto> FinalizeAsync(int id, DateTime date, string? user)
    {
        if (date == default || user?.Length > 120) throw new InvalidOperationException("تاریخ نهایی‌سازی معتبر نیست.");
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var record = await _db.ShopTradeIns.Include(t => t.Party).FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new InvalidOperationException("پذیرش تعویض یافت نشد.");
        if (record.FinalizedAt.HasValue)
        {
            if (record.FinalizedAt.Value.Date != date.Date)
                throw new InvalidOperationException("این پذیرش قبلاً در تاریخ دیگری نهایی شده است.");
            await transaction.CommitAsync();
            return (await ListAsync()).First(t => t.Id == id);
        }
        if (date.Date < record.ReceivedAt.Date) throw new InvalidOperationException("تاریخ نهایی‌سازی نمی‌تواند پیش از پذیرش باشد.");
        CheckMoney(record.Value);
        record.FinalizedAt = date.Date;
        record.FinalizedBy = user;
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        return ToDto(record, new());
    }

    public async Task UseAsync(int id, ShopTradeInUseCommand cmd, string? user)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        CheckMoney(cmd.Amount);
        if (cmd.OperationId == Guid.Empty || cmd.Date == default || cmd.Note?.Length > 200 || user?.Length > 120 ||
            cmd.Kind is not (TradeInUseKind.ApplyToSale or TradeInUseKind.Refund))
            throw new InvalidOperationException("مشخصات مصرف اعتبار معتبر نیست.");
        if ((cmd.Kind == TradeInUseKind.ApplyToSale && (cmd.TargetSaleId is not > 0 || cmd.TrsAccountId.HasValue)) ||
            (cmd.Kind == TradeInUseKind.Refund && (cmd.TrsAccountId is not > 0 || cmd.TargetSaleId.HasValue)))
            throw new InvalidOperationException("فاکتور یا حساب پرداخت را مطابق نوع عملیات انتخاب کنید.");
        var date = cmd.Date.Date;
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var prior = await _db.ShopTradeInUses.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.OperationId == cmd.OperationId);
        if (prior is not null)
        {
            if (prior.CompanyId != _db.ActiveCompanyId || prior.TradeInId != id || prior.Kind != cmd.Kind ||
                prior.TargetSaleId != cmd.TargetSaleId || prior.TrsAccountId != cmd.TrsAccountId ||
                prior.Amount != cmd.Amount || prior.Date.Date != date || prior.Note != Optional(cmd.Note))
                throw new InvalidOperationException("شناسه عملیات قبلاً با مشخصات دیگری استفاده شده است.");
            await transaction.CommitAsync();
            return;
        }
        var source = await _db.ShopTradeIns.Include(t => t.Party).FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new InvalidOperationException("پذیرش تعویض یافت نشد.");
        var uses = await _db.ShopTradeInUses.Where(u => u.TradeInId == id).ToListAsync();
        _ = ToDto(source, uses);
        if (!source.FinalizedAt.HasValue || date < source.FinalizedAt.Value.Date || uses.Any(u => u.Date.Date > date))
            throw new InvalidOperationException("تاریخ عملیات باید پس از نهایی‌سازی و آخرین گردش اعتبار باشد.");
        if (cmd.Amount > source.Value - uses.Sum(u => u.Amount))
            throw new InvalidOperationException("مبلغ از اعتبار باقی‌مانده مشتری بیشتر است.");
        var use = new ShopTradeInUse
        {
            CompanyId = source.CompanyId, TradeInId = id, OperationId = cmd.OperationId, Kind = cmd.Kind,
            Amount = cmd.Amount, Date = date, Note = Optional(cmd.Note), CreatedBy = user
        };
        if (cmd.Kind == TradeInUseKind.ApplyToSale)
        {
            var sale = await _db.Transactions.FirstOrDefaultAsync(t => t.Id == cmd.TargetSaleId)
                ?? throw new InvalidOperationException("فاکتور فروش یافت نشد.");
            if (sale.Type != TransactionType.Sale || sale.CompanyId != source.CompanyId || sale.PartyId != source.PartyId || date < sale.Date.Date)
                throw new InvalidOperationException("فاکتور مقصد باید متعلق به همین مشتری و شرکت باشد و تاریخ آن پیش از مصرف اعتبار باشد.");
            if (await ShopOrderCreditPosting.HasLaterEventsAsync(_db, sale.Id, date))
                throw new InvalidOperationException("تاریخ مصرف نمی‌تواند پیش از آخرین گردش فاکتور باشد.");
            var state = await ShopOrderCreditPosting.GetStateAsync(_db, sale.Id);
            if (state.IsCancelled || cmd.Amount > state.AvailableCredit || sale.SettledAmount + cmd.Amount > MaxMoney)
                throw new InvalidOperationException("مبلغ از مانده نسیه آزاد فاکتور بیشتر است یا فاکتور ابطال شده است.");
            sale.SettledAmount += cmd.Amount;
            use.TargetSaleId = sale.Id;
        }
        else
        {
            var account = await _db.TrsAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == cmd.TrsAccountId && a.IsActive)
                ?? throw new InvalidOperationException("حساب صندوق/بانک فعال یافت نشد.");
            await SaleChequeTreasuryGuard.VerifyCompanyOwnershipAsync(_db, account, source.CompanyId);
            await TreasuryBalanceCalculator.EnsureAvailableOnAndAfterAsync(_db, account.Id, date, cmd.Amount);
            var number = (await _db.TrsVouchers.IgnoreQueryFilters().Where(v => v.Kind == TreasuryKind.Payment)
                .Select(v => (int?)v.Number).MaxAsync() ?? 0) + 1;
            var title = $"پرداخت اعتبار تعویض {source.Number}";
            var voucher = new TrsVoucher
            {
                CompanyId = source.CompanyId, Number = number, Kind = TreasuryKind.Payment, Date = date,
                PartyId = source.PartyId, Description = title, RefNumber = source.Number,
                Status = TreasuryStatus.Confirmed, TotalAmount = cmd.Amount, CashAmount = cmd.Amount,
                CreatedBy = user, ConfirmedBy = user, CreatedAt = DateTime.Now, ConfirmedAt = DateTime.Now,
                Lines = new() { new TrsVoucherLine { RowNo = 1, TrsAccountId = account.Id, Amount = cmd.Amount,
                    Method = account.Kind is TreasuryAccountKind.Cash or TreasuryAccountKind.PettyCash ? PayMethod.Cash : PayMethod.Card,
                    RefNumber = source.Number, Description = title } }
            };
            _db.TrsVouchers.Add(voucher);
            use.TrsAccountId = account.Id;
            use.TrsVoucher = voucher;
        }
        _db.ShopTradeInUses.Add(use);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
