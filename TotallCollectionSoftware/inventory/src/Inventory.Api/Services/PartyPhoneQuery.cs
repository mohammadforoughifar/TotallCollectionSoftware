using Inventory.Shared;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>
/// «جستجوی مشتری بر اساس شماره تلفن» بهصورت یک فیلتر EF قابلترجمه.
/// <para>
/// روی همان <see cref="IQueryable{T}"/> اعمال میشود، پس هم مستقیماً روی فهرست
/// مشتریان و هم داخل زیرپرسوجوهای <c>Any</c> در جستجوی اسناد (حواله، سفارش،
/// پذیرش تعمیرات، …) قابل استفاده است. فقط <c>Contains</c>، <c>Like</c> و
/// مقادیر ازپیشمحاسبشده بهکار رفته تا روی SQLite و SQL Server یکسان ترجمه شود.
/// </para>
/// </summary>
public static class PartyPhoneQuery
{
    /// <summary>تطبیق نام، موبایل یا تلفن ثابت؛ شامل ارقام فارسی/عربی و پیششمارهٔ کشور.</summary>
    public static IQueryable<Inventory.Api.Data.Party> MatchingPhone(
        this IQueryable<Inventory.Api.Data.Party> parties, string search)
    {
        var s = search.Trim();
        var (digits, noZero, withZero) = PhoneSearch.SqlKeys(s);

        // الگوی Like فقط ارقام کلید را بهترتیب میخواهد و جداکنندههای ذخیرهشده
        // (خط تیره، فاصله، پرانتز، +98) را نادیده میگیرد. در نبود رقم، الگوی
        // معادل همان متن خام است تا الگوی «%%» همهٔ ردیفها را برنگرداند.
        var key = PhoneSearch.Key(s);
        var like = key.Length >= 3
            ? "%" + string.Join("%", key.Select(c => c.ToString())) + "%"
            : "%" + s + "%";

        return parties.Where(p =>
            p.Name.Contains(s)
            || (p.Mobile != null && (p.Mobile.Contains(s) || p.Mobile.Contains(digits)
                                     || p.Mobile.Contains(noZero) || p.Mobile.Contains(withZero)
                                     || EF.Functions.Like(p.Mobile, like)))
            || (p.Phone != null && (p.Phone.Contains(s) || p.Phone.Contains(digits)
                                    || p.Phone.Contains(noZero) || p.Phone.Contains(withZero)
                                    || EF.Functions.Like(p.Phone, like))));
    }
}
