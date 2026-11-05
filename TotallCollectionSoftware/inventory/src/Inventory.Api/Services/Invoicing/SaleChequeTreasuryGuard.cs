using Inventory.Api.Data;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Invoicing;

/// <summary>
/// اعتبارسنجی یکپارچه مالکیت شرکت حساب‌های بانکی و خزانه در عملیات فروش سوددار.
/// در محیط چندشرکتی حساب باید صریحاً به همان شرکت فاکتور تعلق داشته باشد.
/// </summary>
public static class SaleChequeTreasuryGuard
{
    public static async Task VerifyCompanyOwnershipAsync(AppDbContext db, TrsAccount account, int? saleCompanyId)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (db.ActiveCompanyId == 0 || (db.ActiveCompanyId is > 0 && saleCompanyId != db.ActiveCompanyId))
            throw new InvalidOperationException("شرکت فاکتور با شرکت فعال کاربر مطابقت ندارد.");
        if (await db.SystemCompanies.CountAsync() > 1)
        {
            if (saleCompanyId is not > 0 || account.CompanyId != saleCompanyId)
                throw new InvalidOperationException("در محیط چندشرکتی، حساب بانکی/خزانه باید صریحاً به شرکت فعال همین فاکتور اختصاص یافته باشد.");
        }
        else if (account.CompanyId.HasValue && account.CompanyId != saleCompanyId)
        {
            throw new InvalidOperationException("حساب بانکی/خزانه انتخاب‌شده متعلق به شرکت دیگری است.");
        }
    }

    public static async Task<List<TrsAccount>> ListCompanyAccountsAsync(AppDbContext db, TreasuryAccountKind? kind = null)
    {
        var activeCompanyId = db.ActiveCompanyId;
        if (activeCompanyId == 0) return new List<TrsAccount>();
        var multiCompany = await db.SystemCompanies.CountAsync() > 1;
        var q = db.TrsAccounts.AsNoTracking()
            .Where(a => a.IsActive);
        if (kind.HasValue)
            q = q.Where(a => a.Kind == kind.Value);
        if (multiCompany)
        {
            if (activeCompanyId is not > 0) return new List<TrsAccount>();
            q = q.Where(a => a.CompanyId == activeCompanyId);
        }
        else if (activeCompanyId is > 0)
        {
            q = q.Where(a => a.CompanyId == null || a.CompanyId == activeCompanyId);
        }
        return await q.OrderBy(a => a.SortOrder).ThenBy(a => a.Code).ThenBy(a => a.Id).ToListAsync();
    }
}
