using System.Globalization;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

// ============================================================
//  سرویس ساختار شماره اندیکاتور — پورت StructureLetterService کارفرما
//  خروجی نمونه با ترتیب واحد/شماره/سال:  MQ/1/1405
//
//  جزء «واحد» در طرح مرجع از چارت سازمانی گرفته می‌شود:
//      GetOrganizationNameUniqAsync(sematId)  ←  Organization.NameUniq
//  اینجا همان منطق با IOrganizationServices پیاده شده است:
//    • اگر سمت صادرکننده (CreatorSematId) موجود باشد → NameUniq سازمانِ آن سمت
//    • وگرنه → سازمانِ پیش‌فرض (تنظیم‌شده در صفحه‌ی «تنظیمات ← ساختار شماره نامه»)
//  مقدار ثابت appsettings (Letters:UnitCode) فقط برای «سید سازمان اولیه» استفاده می‌شود.
// ============================================================

public interface ILetterStratureService
{
    /// <summary>
    /// ساخت شماره اندیکاتور کامل بر اساس ساختار ذخیره‌شده (معادل TotalNumberAsync مرجع).
    /// date: تاریخ مبنای جزء «سال» (پیش‌فرض: اکنون — برای بازسازی شماره نامه‌های قدیمی، تاریخ ثبت نامه پاس داده می‌شود)
    /// sematId: سمت صادرکننده — مبنای «واحد» (سازمانِ سمت)؛ null یعنی سازمان پیش‌فرض
    /// </summary>
    Task<string> TotalNumberAsync(int number, int typeForm, DateTime? date = null, int? sematId = null);

    /// <summary>اجزای ساختار برای فرم مشخص (به ترتیب)</summary>
    Task<List<string>> GetStructureAsync(int typeForm);

    /// <summary>ثبت/جایگزینی ساختار فرم (معادل Add/EditStratureLetter مرجع)</summary>
    Task SetStructureAsync(int typeForm, List<string> parts);

    /// <summary>
    /// اجزای معتبر ساختار — «واحد» | «شماره» | «سال»
    /// </summary>
    Task<List<string>> GetValidPartsAsync();
}

public class LetterStratureService : ILetterStratureService
{
    /// <summary>اجزای مجاز ساختار (فارسی — همان کلیدهای ذخیره‌شده در LetterStratures.TypeStrature)</summary>
    public static readonly string[] ValidParts = { "واحد", "شماره", "سال" };

    private readonly AppDbContext _db;
    private readonly IOrganizationServices _org;

    public LetterStratureService(AppDbContext db, IOrganizationServices org)
    {
        _db = db;
        _org = org;
    }

    public async Task<string> TotalNumberAsync(int number, int typeForm, DateTime? date = null, int? sematId = null)
    {
        if (number == -1) return string.Empty;

        var structure = await _db.LetterStratures.AsNoTracking()
            .Where(s => s.TypeForm == typeForm)
            .OrderBy(s => s.StratureId)
            .Select(s => s.TypeStrature)
            .ToListAsync();

        if (structure.Count == 0) return string.Empty;

        // «واحد» — از سازمانِ سمتِ صادرکننده، وگرنه سازمانِ پیش‌فرض (Organization.NameUniq)
        var unit = await _org.GetOrganizationNameUniqAsync(sematId);
        return FormatNumber(structure, number, date ?? DateTime.Now, unit);
    }

    /// <summary>
    /// تولید قطعی شماره از اجزای مرتب‌شده. این تابع یک نقطه‌ی مشترک برای نامه‌های جدید و
    /// بازاعمال تنظیمات روی نامه‌های موجود است تا ترتیب UI دقیقاً همان ترتیب خروجی باشد.
    /// </summary>
    public static string FormatNumber(IEnumerable<string> structure, int number, DateTime date, string? unit)
    {
        var year = new PersianCalendar().GetYear(date);
        var result = new List<string>();
        foreach (var item in structure)
        {
            switch (item)
            {
                case "سال":
                    result.Add(year.ToString(CultureInfo.InvariantCulture));
                    break;
                case "واحد":
                    if (!string.IsNullOrWhiteSpace(unit)) result.Add(unit.Trim());
                    break;
                case "شماره":
                    result.Add(number.ToString(CultureInfo.InvariantCulture));
                    break;
            }
        }
        return string.Join("/", result);
    }

    public async Task<List<string>> GetStructureAsync(int typeForm) =>
        await _db.LetterStratures
            .Where(s => s.TypeForm == typeForm)
            .OrderBy(s => s.StratureId)
            .Select(s => s.TypeStrature)
            .ToListAsync();

    public async Task SetStructureAsync(int typeForm, List<string> parts)
    {
        if (typeForm is < 1 or > 3)
            throw new Exception("نوع نامه نامعتبر است.");

        parts = (parts ?? new List<string>())
            .Select(p => p?.Trim() ?? "")
            .ToList();

        // اعتبارسنجی: اجزای مجاز، بدون تکرار، «شماره» الزامی، حداقل «شماره + یک جزء دیگر»
        if (parts.Count == 0)
            throw new Exception("ساختار نمی‌تواند خالی باشد.");
        if (parts.Count > ValidParts.Length)
            throw new Exception($"ساختار نمی‌تواند بیشتر از {ValidParts.Length} جزء داشته باشد.");
        if (parts.Any(p => !ValidParts.Contains(p)))
            throw new Exception($"جزء نامعتبر است — اجزای مجاز: {string.Join("، ", ValidParts)}.");
        if (parts.Distinct(StringComparer.Ordinal).Count() != parts.Count)
            throw new Exception("اجزای ساختار نباید تکراری باشند.");
        if (!parts.Contains("شماره"))
            throw new Exception("جزء «شماره» در ساختار الزامی است.");
        if (parts.Count < 2)
            throw new Exception("ساختار باید حداقل «شماره + یک جزء دیگر» داشته باشد.");

        await using var tx = await _db.Database.BeginTransactionAsync();

        // حذف و درج در دو SaveChanges جدا انجام می‌شود؛ بنابراین StratureIdهای identity دقیقاً
        // مطابق ترتیب parts ساخته می‌شوند و OrderBy(StratureId) در تولید شماره قطعی است.
        var old = await _db.LetterStratures.Where(s => s.TypeForm == typeForm).ToListAsync();
        _db.LetterStratures.RemoveRange(old);
        await _db.SaveChangesAsync();

        foreach (var part in parts)
        {
            _db.LetterStratures.Add(new LetterStrature
            {
                TypeForm = typeForm,
                TypeStrature = part
            });
            // ذخیره‌ی ترتیبی جلوی بازچینی INSERTهای batch توسط providerهای مختلف را می‌گیرد.
            await _db.SaveChangesAsync();
        }

        // تنظیمات بلافاصله روی نامه‌های موجود نیز اعمال می‌شود؛ نامه‌های بعدی هم در زمان ایجاد
        // همین سرویس را صدا می‌زنند. برای صادره‌ی امضاشده فقط شماره رسمی‌ای تغییر می‌کند که
        // دقیقاً از LetterNumber قبلی کپی شده باشد، نه شماره‌ای که دبیرخانه دستی وارد کرده است.
        await ApplyToExistingLettersAsync(typeForm, parts);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private async Task ApplyToExistingLettersAsync(int typeForm, IReadOnlyList<string> structure)
    {
        if (typeForm == 1)
        {
            var letters = await _db.InnerLetters
                .Where(l => !l.IsDelete && !l.Source.IsDelete)
                .ToListAsync();
            var units = await ResolveUnitsAsync(letters.Select(l => l.CreatorSematId));
            foreach (var letter in letters)
            {
                var unit = units[letter.CreatorSematId ?? 0];
                letter.LetterNumber = FormatNumber(structure, letter.Number, letter.DateSabt, unit);
            }
            return;
        }

        if (typeForm == 2)
        {
            var letters = await _db.OutgoingLetters
                .Where(l => !l.IsDelete && !l.Source.IsDelete)
                .ToListAsync();
            var units = await ResolveUnitsAsync(letters.Select(l => l.CreatorSematId));
            foreach (var letter in letters)
            {
                var previous = letter.LetterNumber;
                var updated = FormatNumber(structure, letter.Number, letter.DateSabt, units[letter.CreatorSematId ?? 0]);
                letter.LetterNumber = updated;
                if (!string.IsNullOrWhiteSpace(previous)
                    && string.Equals(letter.SadereNumber, previous, StringComparison.Ordinal))
                    letter.SadereNumber = updated;
            }
        }
        // TypeForm=3 برای نامه وارده رزرو است و با اضافه‌شدن موجودیت آن، همین‌جا اعمال می‌شود.
    }

    private async Task<Dictionary<int, string>> ResolveUnitsAsync(IEnumerable<int?> sematIds)
    {
        var result = new Dictionary<int, string>();
        foreach (var sematId in sematIds.Distinct())
            result[sematId ?? 0] = await _org.GetOrganizationNameUniqAsync(sematId);
        return result;
    }

    public Task<List<string>> GetValidPartsAsync() => Task.FromResult(ValidParts.ToList());
}
