using System.Globalization;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

// ============================================================
//  سرویس ساختار شماره اندیکاتور — پورت StructureLetterService کارفرما
//  خروجی نمونه با ترتیب واحد/شماره/سال:  MQ/1/1405
//
//  نکته «واحد»: در طرح مرجع، واحد از چارت سازمانی
//  (GetOrganizationNameUniqAsync بر اساس SematId فرستنده) گرفته می‌شود.
//  چون چارت سازمانی هنوز پورت نشده، فعلاً از تنظیمات (Letters:UnitCode)
//  خوانده می‌شود؛ بعد از دریافت سرویس‌های سازمان از کارفرما، همین‌جا
//  جایگزین می‌شود (امضای متد از الان sematId را می‌پذیرد).
// ============================================================

public interface ILetterStratureService
{
    /// <summary>
    /// ساخت شماره اندیکاتور کامل بر اساس ساختار ذخیره‌شده (معادل TotalNumberAsync مرجع).
    /// date: تاریخ مبنای جزء «سال» (پیش‌فرض: اکنون — برای بازسازی شماره نامه‌های قدیمی، تاریخ ثبت نامه پاس داده می‌شود)
    /// </summary>
    Task<string> TotalNumberAsync(int number, int typeForm, DateTime? date = null, int? sematId = null);

    /// <summary>اجزای ساختار برای فرم مشخص (به ترتیب)</summary>
    Task<List<string>> GetStructureAsync(int typeForm);

    /// <summary>ثبت/جایگزینی ساختار فرم (معادل Add/EditStratureLetter مرجع)</summary>
    Task SetStructureAsync(int typeForm, List<string> parts);
}

public class LetterStratureService : ILetterStratureService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public LetterStratureService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<string> TotalNumberAsync(int number, int typeForm, DateTime? date = null, int? sematId = null)
    {
        if (number == -1) return string.Empty;

        var structure = await _db.LetterStratures
            .Where(s => s.TypeForm == typeForm)
            .OrderBy(s => s.StratureId)
            .ToListAsync();

        if (structure.Count == 0) return string.Empty;

        var pc = new PersianCalendar();
        var year = pc.GetYear(date ?? DateTime.Now);

        // «واحد» — تا پورت چارت سازمانی از تنظیمات؛ بعداً: GetOrganizationNameUniqAsync(sematId)
        var unit = _config["Letters:UnitCode"] ?? "MQ";

        var parts = new List<string>();
        foreach (var item in structure)
        {
            switch (item.TypeStrature)
            {
                case "سال":
                    parts.Add(year.ToString());
                    break;
                case "واحد":
                    if (!string.IsNullOrEmpty(unit)) parts.Add(unit);
                    break;
                case "شماره":
                    parts.Add(number.ToString());
                    break;
            }
        }
        return string.Join("/", parts);
    }

    public async Task<List<string>> GetStructureAsync(int typeForm) =>
        await _db.LetterStratures
            .Where(s => s.TypeForm == typeForm)
            .OrderBy(s => s.StratureId)
            .Select(s => s.TypeStrature)
            .ToListAsync();

    public async Task SetStructureAsync(int typeForm, List<string> parts)
    {
        var valid = new[] { "واحد", "شماره", "سال" };
        if (parts.Count == 0 || parts.Any(p => !valid.Contains(p)) || !parts.Contains("شماره"))
            throw new Exception("ساختار نامعتبر است — اجزای مجاز: واحد، شماره، سال (وجود «شماره» الزامی است).");

        var old = _db.LetterStratures.Where(s => s.TypeForm == typeForm);
        _db.LetterStratures.RemoveRange(old);
        foreach (var p in parts)
            _db.LetterStratures.Add(new LetterStrature { TypeForm = typeForm, TypeStrature = p });
        await _db.SaveChangesAsync();
    }
}
