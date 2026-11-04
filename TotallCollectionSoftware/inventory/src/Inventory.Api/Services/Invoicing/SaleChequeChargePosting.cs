using System.Data;
using Inventory.Api.Data;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Api.Services.Invoicing;

/// <summary>
/// صدور سند حسابداری برای فاکتور فروش دارای ردیف مالی چک.
/// صرفاً داخل تراکنش Serializable متعلق به فراخواننده قابل اجرا است؛ این متد commit نمی‌کند.
/// مسیر ثبت فاکتور فقط با فعال‌سازی صریح حالت آزمایشی آن را فراخوانی می‌کند.
/// </summary>
public static class SaleChequeChargePosting
{
    public static async Task<AccVoucher> PostAsync(AppDbContext db, int saleId, string? user)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("صدور سند چک باید در تراکنش فاکتور انجام شود.");
        if (transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("تراکنش سند چک باید سطح Serializable داشته باشد.");

        var sale = await db.Transactions.Include(t => t.Lines).Include(t => t.Cheques).Include(t => t.Installments)
            .FirstOrDefaultAsync(t => t.Id == saleId)
            ?? throw new InvalidOperationException("فاکتور فروش یافت نشد.");
        if (sale.Type != TransactionType.Sale || sale.PartyId is not > 0)
            throw new InvalidOperationException("سند باید فاکتور فروش دارای مشتری باشد.");
        var charge = await db.SaleChequeCharges.FirstOrDefaultAsync(x => x.TransactionId == sale.Id)
            ?? throw new InvalidOperationException("ردیف تاخیر تادیه چک یافت نشد.");
        if (charge.Amount <= 0 || charge.CustomerCredit != 0)
            throw new InvalidOperationException("این مسیر فقط فاکتور دارای درآمد مثبت و بدون بستانکاری اصلاحی را پشتیبانی می‌کند.");
        if (await db.AccVouchers.AnyAsync(v => v.Source == VoucherSource.StoreSale && v.SourceId == sale.Id))
            throw new InvalidOperationException("برای این فاکتور قبلاً سند حسابداری صادر شده است؛ اصلاح آن باید از مسیر اصلاح سند انجام شود.");

        var rule = await db.FacRules.AsNoTracking().FirstOrDefaultAsync(r => r.Kind == InvoiceKind.Sale);
        if (rule is not { IsActive: true, AutoVoucher: true } ||
            rule.PartyAccountId is not > 0 || rule.MainAccountId is not > 0 || rule.ChequeIncomeAccountId is not > 0)
            throw new InvalidOperationException("قاعده فعال فروش و حساب‌های مشتری، فروش و درآمد تاخیر تادیه چک را تنظیم کنید.");

        var goods = sale.Lines.Sum(l => Math.Max(0m, (l.GrossAmount ?? OrderLineAmounts.Gross(l.Quantity, l.Price)) - l.DiscountAmount));
        var vat = sale.Lines.Sum(l => l.VatAmount);
        var amounts = SaleChequeVoucherAmounts.Create(goods, vat, charge.Amount);
        if (charge.BaseAmount != goods + vat || charge.InitialAmount != charge.Amount || charge.RatePercent < 0 ||
            charge.RatePercent > 999999999999.999999m || decimal.Round(charge.RatePercent, 6) != charge.RatePercent || charge.PeriodDays <= 0)
            throw new InvalidOperationException("مبنا، نرخ، بازه یا مبلغ اولیه تاخیر تادیه چک معتبر نیست.");
        if (!charge.CalculatorBaseAmount.HasValue || !charge.CalculatorRatePercent.HasValue || !charge.CalculatorMonths.HasValue)
            throw new InvalidOperationException("ورودی‌های ماشین‌حساب اولیه تاخیر تادیه چک کامل نیست.");
        decimal calculatedInitial;
        try
        {
            calculatedInitial = SaleChequeInitialCalculation.Calculate(charge.CalculatorBaseAmount.Value,
                charge.CalculatorRatePercent.Value, charge.CalculatorMonths.Value);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new InvalidOperationException("ورودی‌های ماشین‌حساب اولیه تاخیر تادیه چک معتبر نیست.");
        }
        if (charge.CalculatorBaseAmount.Value > charge.BaseAmount || calculatedInitial != charge.InitialAmount)
            throw new InvalidOperationException("مبلغ اولیه با نتیجه ماشین‌حساب مطابقت ندارد.");
        if (amounts.CustomerDebit != sale.Amount || amounts.CustomerDebit <= 0)
            throw new InvalidOperationException("مبلغ فاکتور با اقلام، مالیات و تاخیر تادیه چک مطابقت ندارد.");
        if (vat > 0 && rule.VatAccountId is not > 0)
            throw new InvalidOperationException("حساب مالیات بر ارزش افزوده کالا تنظیم نشده است.");
        var cash = sale.PaymentMethod == PaymentMethod.Cash ? sale.Amount : sale.CashAmount;
        if (cash < 0 || cash > sale.Amount || decimal.Round(cash, 2) != cash)
            throw new InvalidOperationException("مبلغ جزء نقدی معتبر نیست.");
        int? cashAccountId = rule.CashAccountId;
        if (cash > 0 && sale.TrsAccountId is > 0)
        {
            var treasuryAccount = await db.TrsAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == sale.TrsAccountId && a.IsActive)
                ?? throw new InvalidOperationException("صندوق یا بانک انتخاب‌شده فعال نیست یا یافت نشد.");
            cashAccountId = treasuryAccount.AccountId;
        }
        if (cash > 0 && cashAccountId is not > 0)
            throw new InvalidOperationException("برای دریافت نقدی، حساب حسابداری صندوق یا بانک انتخاب‌شده را تنظیم کنید.");
        if (sale.Cheques.Any(c => c.IsCleared || c.Status != ChequeStatus.InHand) ||
            sale.Installments.Any(i => i.IsPaid) || sale.SettledAmount != cash)
            throw new InvalidOperationException("صدور اولیه برای چک یا اقساط تسویه‌شده پشتیبانی نمی‌شود؛ ابتدا وضعیت وصول را تعیین‌تکلیف کنید.");
        if (sale.Cheques.Count == 0 || sale.Cheques.Any(c => c.Amount <= 0 || decimal.Round(c.Amount, 2) != c.Amount ||
                !c.OriginalDueDate.HasValue || c.OriginalDueDate.Value.Date != c.DueDate.Date))
            throw new InvalidOperationException("چک معتبر با سررسید اولیه مشخص و مبلغ قابل ثبت لازم است.");
        if (sale.Installments.Any(i => i.Amount <= 0 || decimal.Round(i.Amount, 2) != i.Amount))
            throw new InvalidOperationException("مبلغ هر قسط باید مثبت و حداکثر دو رقم اعشار داشته باشد.");
        var paid = cash + sale.Cheques.Sum(c => c.Amount) + sale.Installments.Sum(i => i.Amount);
        // Unlike the legacy flow, never silently accept even a one-rial mismatch.
        // A shortfall is allowed only as an explicit credit component with a due date.
        if (paid > sale.Amount || (paid < sale.Amount && !sale.DueDate.HasValue))
            throw new InvalidOperationException("جمع اجزای پرداخت با مبلغ فاکتور مطابقت ندارد؛ اختلاف را تعیین‌تکلیف کنید.");

        var ids = new int?[] { rule.PartyAccountId, rule.MainAccountId, rule.ChequeIncomeAccountId,
            vat > 0 ? rule.VatAccountId : null, cash > 0 ? cashAccountId : null }
            .Where(x => x.HasValue).Select(x => x!.Value).ToList();
        if (ids.Count != ids.Distinct().Count())
            throw new InvalidOperationException("حساب‌های مشتری، فروش، مالیات، نقد و درآمد چک باید مستقل باشند.");
        var accounts = await db.AccAccounts.AsNoTracking().Where(a => ids.Contains(a.Id)).ToListAsync();
        if (accounts.Count != ids.Count || accounts.Any(a => !a.IsActive || !a.IsPostable))
            throw new InvalidOperationException("تمام حساب‌های سند باید فعال و قابل ثبت باشند.");
        var incomeAccount = accounts.Single(a => a.Id == rule.ChequeIncomeAccountId);
        if (incomeAccount.Type != AccountType.Income || incomeAccount.IsPermanent)
            throw new InvalidOperationException("حساب تاخیر تادیه چک باید درآمد موقت باشد.");
        var salesAccount = accounts.Single(a => a.Id == rule.MainAccountId);
        var partyAccount = accounts.Single(a => a.Id == rule.PartyAccountId);
        if (salesAccount.Type != AccountType.Income || salesAccount.IsPermanent || partyAccount.Type != AccountType.Asset)
            throw new InvalidOperationException("حساب فروش باید درآمد موقت و حساب مشتری باید دریافتنی از نوع دارایی باشد.");
        if (vat > 0 && accounts.Single(a => a.Id == rule.VatAccountId).Type != AccountType.Liability)
            throw new InvalidOperationException("حساب ارزش افزوده فروش باید از نوع بدهی باشد.");
        if (cash > 0 && accounts.Single(a => a.Id == cashAccountId).Type != AccountType.Asset)
            throw new InvalidOperationException("حساب صندوق یا بانک باید از نوع دارایی باشد.");

        var date = sale.Date.Date;
        var years = await db.AccFiscalYears.Where(y => y.StartDate <= date && y.EndDate >= date).ToListAsync();
        if (years.Count != 1 || years[0].IsClosed)
            throw new InvalidOperationException("برای تاریخ فاکتور باید دقیقاً یک سال مالی باز وجود داشته باشد.");
        var year = years[0];
        var number = (await db.AccVouchers.Where(v => v.FiscalYearId == year.Id).Select(v => (int?)v.Number).MaxAsync() ?? 0) + 1;
        var title = $"فاکتور فروش {sale.Number}";
        var voucher = new AccVoucher
        {
            FiscalYearId = year.Id, Number = number, Date = date, Status = VoucherStatus.Confirmed,
            Source = VoucherSource.StoreSale, SourceId = sale.Id, SourceTitle = title,
            Description = $"سند خودکار — {title}", RefNumber = sale.Number,
            CreatedBy = user, ConfirmedBy = user, CreatedAt = DateTime.Now, ConfirmedAt = DateTime.Now
        };
        void Add(int account, decimal debit, decimal credit, string description)
        {
            if (debit == 0 && credit == 0) return;
            voucher.Lines.Add(new AccVoucherLine
            {
                RowNo = voucher.Lines.Count + 1, AccountId = account, PartyId = sale.PartyId,
                Debit = debit, Credit = credit, Description = description, RefNumber = sale.Number
            });
        }
        Add(rule.PartyAccountId!.Value, amounts.CustomerDebit, 0, title);
        Add(rule.MainAccountId!.Value, 0, amounts.GoodsCredit, $"فروش کالا — {title}");
        if (vat > 0) Add(rule.VatAccountId!.Value, 0, vat, $"ارزش افزوده کالا — {title}");
        Add(rule.ChequeIncomeAccountId!.Value, 0, amounts.ChequeIncomeCredit, $"تاخیر تادیه چک — {title}");
        if (cash > 0)
        {
            Add(cashAccountId!.Value, cash, 0, $"دریافت نقدی — {title}");
            Add(rule.PartyAccountId.Value, 0, cash, $"تسویه نقدی مشتری — {title}");
        }
        voucher.TotalDebit = voucher.Lines.Sum(l => l.Debit);
        voucher.TotalCredit = voucher.Lines.Sum(l => l.Credit);
        if (voucher.Lines.Count < 2 || voucher.TotalDebit != voucher.TotalCredit || voucher.TotalDebit == 0)
            throw new InvalidOperationException("سند حسابداری فاکتور تراز نیست.");
        // Snapshot account IDs with the voucher; a later rule change must not rewrite history.
        if ((charge.IncomeAccountId.HasValue && charge.IncomeAccountId != rule.ChequeIncomeAccountId) ||
            (charge.PartyAccountId.HasValue && charge.PartyAccountId != rule.PartyAccountId))
            throw new InvalidOperationException("حساب‌های ذخیره‌شدهٔ فاکتور با حساب‌های صدور مطابقت ندارند.");
        charge.IncomeAccountId = rule.ChequeIncomeAccountId;
        charge.PartyAccountId = rule.PartyAccountId;
        db.AccVouchers.Add(voucher);
        db.SaleChequeChargeEvents.Add(new SaleChequeChargeEvent
        {
            CompanyId = charge.CompanyId, OperationId = Guid.NewGuid(), SaleChequeChargeId = charge.Id,
            AccVoucher = voucher, Kind = SaleChequeChargeEventKind.Initial, Date = voucher.Date, IncomeDelta = charge.Amount
        });
        await db.SaveChangesAsync();
        return voucher;
    }
}
