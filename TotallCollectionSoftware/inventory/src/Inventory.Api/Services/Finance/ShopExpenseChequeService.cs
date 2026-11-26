using System.Data;
using Inventory.Api.Data;
using Inventory.Api.Services.Invoicing;
using Inventory.Api.Services.Treasury;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using TrsVoucher = Inventory.Api.Data.TrsVoucher;
using TrsVoucherLine = Inventory.Api.Data.TrsVoucherLine;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Finance;

/// <summary>پرداخت هزینه با چک صادره یا چک دریافتی، بدون جریان صوری بانک.</summary>
public class ShopExpenseChequeService
{
    private readonly AppDbContext _db;
    private const decimal MaxMoney = 9999999999999999.99m;
    public ShopExpenseChequeService(AppDbContext db) => _db = db;
    private static string? Optional(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    public async Task<List<ShopChequeItem>> EligibleReceivedAsync()
    {
        var q = from c in _db.Cheques.AsNoTracking()
                join t in _db.Transactions.AsNoTracking() on c.TransactionId equals t.Id
                join p in _db.Parties.AsNoTracking() on t.PartyId equals p.Id
                where t.Type == TransactionType.Sale && c.CompanyId == t.CompanyId && p.CompanyId == t.CompanyId &&
                      !c.IsCleared && c.Status == ChequeStatus.InHand &&
                      !_db.ShopExpenseCheques.Any(e => e.SourceChequeId == c.Id && e.State != ExpenseChequeState.Returned)
                orderby c.DueDate, c.Id
                select new ShopChequeItem { Id = c.Id, TransactionId = t.Id, OrderNumber = t.Number,
                    TransactionType = t.Type, PartyId = p.Id, PartyName = p.Name, Number = c.Number,
                    BankName = c.BankName, Amount = c.Amount, DueDate = c.DueDate, Status = c.Status };
        return await q.ToListAsync();
    }

    public Task<List<Inventory.Api.Data.TrsAccount>> ReplacementAccountsAsync() => SaleChequeTreasuryGuard.ListCompanyAccountsAsync(_db);

    public async Task<List<Inventory.Api.Data.TrsAccount>> BanksAsync() => await SaleChequeTreasuryGuard.ListCompanyAccountsAsync(_db, TreasuryAccountKind.Bank);

    public async Task SettleReturnedAsync(int expenseId, ExpenseReplacementPaymentCommand cmd, string? user)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        if (cmd.OperationId == Guid.Empty || cmd.Date == default || cmd.TrsAccountId <= 0 || user?.Length > 120 ||
            cmd.PayType is not (CashType.Cash or CashType.CardReader or CashType.CardTransfer))
            throw new InvalidOperationException("روش، حساب یا تاریخ پرداخت معتبر نیست.");

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var prior = await _db.Expenses.IgnoreQueryFilters().Include(e => e.ReplacementVoucher)
            .FirstOrDefaultAsync(e => e.ReplacementOperationId == cmd.OperationId);
        if (prior is not null)
        {
            if (prior.Id != expenseId || prior.CompanyId != _db.ActiveCompanyId || prior.PayType != cmd.PayType ||
                prior.TrsAccountId != cmd.TrsAccountId || prior.ReplacementVoucher?.Date.Date != cmd.Date.Date)
                throw new InvalidOperationException("شناسه عملیات قبلاً با اطلاعات دیگری استفاده شده است.");
            await transaction.CommitAsync();
            return;
        }

        var expense = await _db.Expenses.FirstOrDefaultAsync(e => e.Id == expenseId)
            ?? throw new InvalidOperationException("سند هزینه یافت نشد.");
        var cheques = await _db.ShopExpenseCheques.AsNoTracking().Include(c => c.Expense)
            .Include(c => c.SettlementVoucher).ThenInclude(v => v!.Lines)
            .Where(c => c.ExpenseId == expense.Id).ToListAsync();
        foreach (var cheque in cheques) ShopExpenseChequeIntegrity.Validate(cheque);
        if (expense.ReplacementVoucherId.HasValue || cheques.Count == 0 ||
            cheques.Any(c => c.CompanyId != expense.CompanyId || c.State != ExpenseChequeState.Returned || !c.SettledAt.HasValue) ||
            cheques.Any(c => c.SettledAt!.Value.Date > cmd.Date.Date))
            throw new InvalidOperationException("پرداخت جایگزین فقط پس از برگشت همه چک‌ها و از تاریخ آخرین برگشت ممکن است.");
        var account = await _db.TrsAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == cmd.TrsAccountId && a.IsActive)
            ?? throw new InvalidOperationException("حساب پرداخت فعال نیست یا یافت نشد.");
        var validKind = cmd.PayType == CashType.Cash
            ? account.Kind is TreasuryAccountKind.Cash or TreasuryAccountKind.PettyCash
            : account.Kind is TreasuryAccountKind.Bank or TreasuryAccountKind.Pos;
        if (!validKind) throw new InvalidOperationException("نوع حساب با روش پرداخت مطابقت ندارد.");
        await SaleChequeTreasuryGuard.VerifyCompanyOwnershipAsync(_db, account, expense.CompanyId);
        await TreasuryBalanceCalculator.EnsureAvailableOnAndAfterAsync(_db, account.Id, cmd.Date.Date, expense.Amount);
        var no = (await _db.TrsVouchers.IgnoreQueryFilters().Where(v => v.Kind == TreasuryKind.Payment).Select(v => (int?)v.Number).MaxAsync() ?? 0) + 1;
        var title = $"پرداخت جایگزین هزینه {expense.Number} پس از برگشت چک";
        var method = cmd.PayType == CashType.Cash ? PayMethod.Cash : PayMethod.Card;
        var voucher = new TrsVoucher { CompanyId = expense.CompanyId, Number = no, Kind = TreasuryKind.Payment,
            Date = cmd.Date.Date, Description = title, Status = TreasuryStatus.Confirmed, TotalAmount = expense.Amount,
            CashAmount = expense.Amount, CreatedBy = user, ConfirmedBy = user, CreatedAt = DateTime.Now, ConfirmedAt = DateTime.Now,
            Lines = new() { new TrsVoucherLine { RowNo = 1, TrsAccountId = account.Id, Amount = expense.Amount, Method = method, Description = title } } };
        expense.PayType = cmd.PayType;
        expense.TrsAccountId = account.Id;
        expense.TrsAccountName = account.Name;
        expense.ReplacementOperationId = cmd.OperationId;
        expense.ReplacementVoucher = voucher;
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
    }


    public async Task<int> CreateAsync(ExpenseChequeCreateCommand cmd, ExpenseChequeKind routeKind, string? user)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        var dto = cmd.Expense ?? throw new InvalidOperationException("اطلاعات هزینه لازم است.");
        var recipient = Optional(cmd.Recipient);
        // هر شرط پیام خودش را دارد؛ پیامِ عمومی قبلی نمی‌گفت کدام فیلد مشکل دارد و
        // کاربر فقط «HTTP 400» می‌دید.
        if (cmd.OperationId == Guid.Empty || cmd.Kind != routeKind)
            throw new InvalidOperationException("درخواست ثبت چک هزینه معتبر نیست؛ فرم را ببندید و دوباره باز کنید.");
        if (dto.CategoryId <= 0) throw new InvalidOperationException("نوع هزینه را انتخاب کنید.");
        if (dto.Date == default) throw new InvalidOperationException("تاریخ سند هزینه را وارد کنید.");
        if (cmd.HandedAt == default) throw new InvalidOperationException("تاریخ تحویل چک را وارد کنید.");
        if (dto.Amount <= 0) throw new InvalidOperationException("مبلغ هزینه را وارد کنید.");
        if (dto.Amount > MaxMoney || decimal.Round(dto.Amount, 2) != dto.Amount)
            throw new InvalidOperationException("مبلغ هزینه معتبر نیست؛ حداکثر دو رقم اعشار و در محدودهٔ مجاز وارد کنید.");
        if (recipient is null) throw new InvalidOperationException("دریافت‌کننده / طرف هزینه را وارد کنید.");
        if (recipient.Length > 150) throw new InvalidOperationException("نام دریافت‌کننده حداکثر ۱۵۰ نویسه است.");
        if (dto.Description?.Length > 500) throw new InvalidOperationException("توضیحات حداکثر ۵۰۰ نویسه است.");
        if (user?.Length > 120) throw new InvalidOperationException("شناسهٔ کاربر معتبر نیست؛ دوباره وارد شوید.");
        // قاعدهٔ ترتیب: پرداخت نمی‌تواند پیش از خودِ سند هزینه باشد. متن پیام می‌گوید
        // کدام تاریخ را اصلاح کند (قاعدهٔ مشترک با فرم؛ Inventory.Shared.ExpenseChequeDates).
        if (ExpenseChequeDates.HandoverIsBeforeExpense(cmd.HandedAt, dto.Date))
            throw new InvalidOperationException(ExpenseChequeDates.HandoverBeforeExpenseMessage);
        if (routeKind == ExpenseChequeKind.Issued && (cmd.IssuingAccountId is not > 0 || cmd.SourceChequeId.HasValue ||
                string.IsNullOrWhiteSpace(cmd.Number) || cmd.Number.Trim().Length > 50 || cmd.DueDate == default))
            throw new InvalidOperationException("شماره، بانک و سررسید چک صادره را وارد کنید.");
        // سررسید چک صادره پیش از تحویل چک معنا ندارد (چک با مهلت گذشته تحویل داده می‌شود).
        // همان قاعدهٔ کلاینت (Inventory.Shared.ExpenseChequeDates) اینجا مرجع نهایی است.
        if (routeKind == ExpenseChequeKind.Issued && ExpenseChequeDates.DueIsBeforeHandover(cmd.DueDate, cmd.HandedAt))
            throw new InvalidOperationException(ExpenseChequeDates.DueBeforeHandoverMessage);
        if (routeKind == ExpenseChequeKind.Received && (cmd.SourceChequeId is not > 0 || cmd.IssuingAccountId.HasValue))
            throw new InvalidOperationException("چک دریافتی موجود را انتخاب کنید.");

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var prior = await _db.ShopExpenseCheques.IgnoreQueryFilters().Include(c => c.Expense).FirstOrDefaultAsync(c => c.OperationId == cmd.OperationId);
        if (prior is not null)
        {
            if (prior.CompanyId != _db.ActiveCompanyId || prior.Kind != routeKind || (dto.Id > 0 && prior.ExpenseId != dto.Id) ||
                prior.Expense.CategoryId != dto.CategoryId || prior.Amount != dto.Amount || prior.Expense.Date.Date != dto.Date.Date ||
                prior.HandedAt.Date != cmd.HandedAt.Date || prior.Recipient != recipient || prior.Expense.Description != Optional(dto.Description) ||
                prior.SourceChequeId != cmd.SourceChequeId || prior.IssuingAccountId != cmd.IssuingAccountId ||
                (routeKind == ExpenseChequeKind.Issued && (prior.Number != cmd.Number.Trim() || prior.DueDate.Date != cmd.DueDate.Date)))
                throw new InvalidOperationException("شناسه عملیات قبلاً با اطلاعات دیگری استفاده شده است.");
            await transaction.CommitAsync(); return prior.ExpenseId;
        }
        var category = await _db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == dto.CategoryId)
            ?? throw new InvalidOperationException("نوع هزینه یافت نشد.");
        if (dto.Id == 0 && !category.IsActive) throw new InvalidOperationException("نوع هزینه غیرفعال است.");
        Expense expense;
        if (dto.Id == 0)
        {
            expense = new Expense { CompanyId = category.CompanyId, CategoryId = category.Id, Amount = dto.Amount,
                Date = dto.Date.Date, PayType = CashType.Cash, Payee = recipient, Description = Optional(dto.Description) };
            _db.Expenses.Add(expense);
            await _db.SaveChangesAsync();
            expense.Number = $"EX-{expense.Id:0000}";
        }
        else
        {
            expense = await _db.Expenses.FirstOrDefaultAsync(e => e.Id == dto.Id)
                ?? throw new InvalidOperationException("سند هزینه یافت نشد.");
            if (expense.ReplacementVoucherId.HasValue || expense.CompanyId != category.CompanyId || expense.CategoryId != dto.CategoryId || expense.Amount != dto.Amount ||
                expense.Date.Date != dto.Date.Date || expense.Description != Optional(dto.Description) ||
                !await _db.ShopExpenseCheques.AnyAsync(c => c.ExpenseId == expense.Id && c.State == ExpenseChequeState.Returned) ||
                await _db.ShopExpenseCheques.AnyAsync(c => c.ExpenseId == expense.Id && c.State != ExpenseChequeState.Returned))
                throw new InvalidOperationException("فقط برای هزینه دارای چک برگشتی و بدون چک فعال می‌توان چک جایگزین ثبت کرد.");
        }
        var link = new ShopExpenseCheque { CompanyId = expense.CompanyId, OperationId = cmd.OperationId, ExpenseId = expense.Id,
            Expense = expense, Kind = routeKind, State = ExpenseChequeState.Outstanding, Recipient = recipient,
            Amount = expense.Amount, HandedAt = cmd.HandedAt.Date, CreatedBy = user };
        if (routeKind == ExpenseChequeKind.Issued)
        {
            var bank = await _db.TrsAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == cmd.IssuingAccountId && a.IsActive && a.Kind == TreasuryAccountKind.Bank)
                ?? throw new InvalidOperationException("حساب بانکی صادرکننده فعال نیست یا یافت نشد.");
            await SaleChequeTreasuryGuard.VerifyCompanyOwnershipAsync(_db, bank, expense.CompanyId);
            if (await _db.ShopExpenseCheques.AnyAsync(c => c.IssuingAccountId == bank.Id && c.Number == cmd.Number.Trim()))
                throw new InvalidOperationException("این شماره چک قبلاً برای این بانک ثبت شده است.");
            link.IssuingAccountId = bank.Id; link.Number = cmd.Number.Trim(); link.BankName = bank.Name; link.DueDate = cmd.DueDate.Date;
        }
        else
        {
            var cheque = await _db.Cheques.FirstOrDefaultAsync(c => c.Id == cmd.SourceChequeId)
                ?? throw new InvalidOperationException("چک دریافتی یافت نشد.");
            var sale = await _db.Transactions.FirstOrDefaultAsync(t => t.Id == cheque.TransactionId)
                ?? throw new InvalidOperationException("فاکتور مبدأ چک یافت نشد.");
            // تاریخ، پیام خودش را دارد؛ پیامِ «چک در دسترس نیست» برای این حالت گمراه‌کننده بود.
            if (cmd.HandedAt.Date < sale.Date.Date)
                throw new InvalidOperationException("تاریخ تحویل چک نمی‌تواند پیش از تاریخ فاکتور مبدأ باشد.");
            if (sale.Type != TransactionType.Sale || sale.PartyId is not > 0 || sale.CompanyId != expense.CompanyId || cheque.CompanyId != sale.CompanyId ||
                cheque.IsCleared || cheque.Status != ChequeStatus.InHand || cheque.Amount != expense.Amount ||
                string.IsNullOrWhiteSpace(cheque.Number) || cheque.DueDate == default ||
                await _db.ShopExpenseCheques.AnyAsync(c => c.SourceChequeId == cheque.Id && c.State != ExpenseChequeState.Returned) ||
                await _db.SaleChequeCancellations.AnyAsync(c => c.SaleTransactionId == sale.Id))
                throw new InvalidOperationException("چک دریافتی در دسترس نیست، مبلغ آن یکسان نیست یا متعلق به شرکت جاری نیست.");
            if (await ShopOrderCreditPosting.HasLaterEventsAsync(_db, sale.Id, cmd.HandedAt.Date))
                throw new InvalidOperationException("تاریخ خرج چک نمی‌تواند پیش از آخرین گردش فاکتور باشد.");
            if (sale.ShopCashLedgerEnabled) await ShopOrderCashLedger.VerifyCurrentAsync(_db, sale);
            if (!sale.ShopCashLedgerEnabled && sale.CashAmount > 0 && !await _db.SaleChequeCharges.AnyAsync(c => c.TransactionId == sale.Id))
                throw new InvalidOperationException("نقد فاکتور قدیمی ابتدا باید تطبیق شود.");
            await SaleChequeSettlementLedger.EnsureSettledAmountConsistentAsync(_db, sale);
            link.SourceChequeId = cheque.Id; link.SourceTransactionId = sale.Id; link.Number = cheque.Number;
            link.BankName = cheque.BankName; link.DueDate = cheque.DueDate.Date;
            _db.SaleChequeStatusEvents.Add(new SaleChequeStatusEvent { CompanyId = cheque.CompanyId, OperationId = cmd.OperationId,
                ChequeId = cheque.Id, PreviousStatus = cheque.Status, NewStatus = ChequeStatus.Endorsed, CreatedBy = user,
                Reason = $"خرج بابت هزینه {expense.Number}" });
            cheque.Status = ChequeStatus.Endorsed;
        }
        _db.ShopExpenseCheques.Add(link);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        return expense.Id;
    }

    public async Task ChangeStatusAsync(int id, ExpenseChequeStatusCommand cmd, ExpenseChequeKind routeKind, string? user)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        if (cmd.OperationId == Guid.Empty || cmd.Date == default || cmd.State is not (ExpenseChequeState.Cleared or ExpenseChequeState.Returned) ||
            cmd.Reason?.Length > 200 || user?.Length > 120)
            throw new InvalidOperationException("وضعیت، تاریخ یا شناسه عملیات چک معتبر نیست.");
        var date = cmd.Date.Date;
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var prior = await _db.ShopExpenseCheques.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.StatusOperationId == cmd.OperationId);
        if (prior is not null)
        {
            if (prior.CompanyId != _db.ActiveCompanyId || prior.Id != id || prior.Kind != routeKind || prior.State != cmd.State ||
                prior.SettledAt?.Date != date || prior.Reason != Optional(cmd.Reason))
                throw new InvalidOperationException("شناسه عملیات قبلاً با اطلاعات دیگری استفاده شده است.");
            await transaction.CommitAsync(); return;
        }
        var link = await _db.ShopExpenseCheques.Include(c => c.Expense).FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new InvalidOperationException("چک هزینه یافت نشد.");
        if (link.Kind != routeKind || link.CompanyId != link.Expense.CompanyId || link.State != ExpenseChequeState.Outstanding || date < link.HandedAt.Date)
            throw new InvalidOperationException("چک در وضعیت قابل تغییر نیست یا تاریخ پیش از تحویل است.");
        if (routeKind == ExpenseChequeKind.Issued && cmd.State == ExpenseChequeState.Cleared)
        {
            var bank = await _db.TrsAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == link.IssuingAccountId && a.IsActive && a.Kind == TreasuryAccountKind.Bank)
                ?? throw new InvalidOperationException("حساب بانکی صادرکننده یافت نشد.");
            await SaleChequeTreasuryGuard.VerifyCompanyOwnershipAsync(_db, bank, link.CompanyId);
            await TreasuryBalanceCalculator.EnsureAvailableOnAndAfterAsync(_db, bank.Id, date, link.Amount);
            var no = (await _db.TrsVouchers.IgnoreQueryFilters().Where(v => v.Kind == TreasuryKind.Payment).Select(v => (int?)v.Number).MaxAsync() ?? 0) + 1;
            var title = $"پاس چک هزینه {link.Expense.Number}";
            var voucher = new TrsVoucher { CompanyId = link.CompanyId, Number = no, Kind = TreasuryKind.Payment, Date = date,
                Description = title, RefNumber = link.Number, Status = TreasuryStatus.Confirmed, TotalAmount = link.Amount, ChequeAmount = link.Amount,
                CreatedBy = user, ConfirmedBy = user, CreatedAt = DateTime.Now, ConfirmedAt = DateTime.Now,
                Lines = new() { new TrsVoucherLine { RowNo = 1, TrsAccountId = bank.Id, Amount = link.Amount, Method = PayMethod.Cheque, RefNumber = link.Number, Description = title } } };
            _db.TrsVouchers.Add(voucher); link.SettlementVoucher = voucher;
        }
        else if (routeKind == ExpenseChequeKind.Received)
        {
            var cheque = await _db.Cheques.FirstOrDefaultAsync(c => c.Id == link.SourceChequeId)
                ?? throw new InvalidOperationException("چک مبدأ یافت نشد.");
            var sale = await _db.Transactions.FirstOrDefaultAsync(t => t.Id == link.SourceTransactionId)
                ?? throw new InvalidOperationException("فاکتور مبدأ یافت نشد.");
            if (cheque.TransactionId != sale.Id || cheque.CompanyId != link.CompanyId || sale.CompanyId != link.CompanyId ||
                sale.Type != TransactionType.Sale || cheque.Status != ChequeStatus.Endorsed || cheque.IsCleared ||
                cheque.Amount != link.Amount || cheque.Number != link.Number || cheque.DueDate.Date != link.DueDate.Date)
                throw new InvalidOperationException("چک خرج‌شده با فاکتور یا سند هزینه مطابقت ندارد.");
            if (await ShopOrderCreditPosting.HasLaterEventsAsync(_db, sale.Id, date))
                throw new InvalidOperationException("تاریخ تعیین وضعیت قبل از آخرین گردش فاکتور است.");
            await SaleChequeSettlementLedger.EnsureSettledAmountConsistentAsync(_db, sale);
            if (cmd.State == ExpenseChequeState.Cleared)
            {
                if (sale.SettledAmount + link.Amount > MaxMoney) throw new InvalidOperationException("جمع تسویه از محدوده مجاز بیشتر است.");
                sale.SettledAmount += link.Amount; // وصول در بانک گیرنده، بدون ثبت ورود به بانک فروشگاه
            }
            else
            {
                _db.SaleChequeStatusEvents.Add(new SaleChequeStatusEvent { CompanyId = cheque.CompanyId, OperationId = cmd.OperationId,
                    ChequeId = cheque.Id, PreviousStatus = ChequeStatus.Endorsed, NewStatus = ChequeStatus.Bounced,
                    Reason = Optional(cmd.Reason) ?? "چک خرج‌شده به فروشگاه برگشت", CreatedBy = user });
                cheque.Status = ChequeStatus.Bounced; cheque.BouncedAt = date; cheque.BounceReason = Optional(cmd.Reason) ?? "چک خرج‌شده به فروشگاه برگشت";
            }
        }
        link.State = cmd.State; link.StatusOperationId = cmd.OperationId; link.SettledAt = date; link.Reason = Optional(cmd.Reason);
        link.StatusBy = user; link.StatusAt = DateTime.Now;
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

}
