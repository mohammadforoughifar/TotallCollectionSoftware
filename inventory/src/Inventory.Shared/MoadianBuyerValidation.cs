namespace Inventory.Shared;

/// <summary>
/// مقادیر مجاز «توکن نوع خریدار» (Tob) در payload سامانه مودیان — تعریف واحد و قابل تنظیم.
///
/// ⚠ تأیید رسمی مالک پروژه (۱۴۰۵/۰۷/۱۷):
///   Tob = 1 → حقیقی (Individual)
///   Tob = 2 → حقوقی (Legal)
///
/// نکته: enum درونی SDK (PersonType) صفر‌شمار است (LEGAL=0, REAL=1, FOREIGNERS=2,
/// FOREIGN_TRAVELERS=3) و فرمت سیم Tob نیست — فقط برای ارجاع مستندات ذکر شده است.
/// همه نقاط کد باید از این enum استفاده کنند (شماره‌ی جادو در کد ممنوع).
/// </summary>
public enum MoadianBuyerType
{
    /// <summary>حقیقی — Tob=1: Bid = شمارهٔ ملی ۱۰ رقمی با رقم کنترلی معتبر؛ Tinb باید خالی باشد</summary>
    Individual = 1,

    /// <summary>حقوقی — Tob=2: Bid = شناسهٔ ملی ۱۱ رقمی با رقم کنترلی معتبر و/یا Tinb = کد اقتصادی ۱۴ رقمی</summary>
    Legal = 2
}

/// <summary>
/// خطای اعتبارسنجی محلی فیلدهای خریدار — controller آن را به HTTP 422 با نام فیلد
/// تبدیل می‌کند و هیچ رکوردی به SDK/سامانه ارسال نمی‌شود.
/// </summary>
public sealed class MoadianBuyerValidationException : Exception
{
    public string Field { get; }

    public MoadianBuyerValidationException(string field, string message)
        : base(message)
    {
        Field = field;
    }
}

/// <summary>
/// نگاشت و اعتبارسنجی محلی فیلدهای خریدار (Tob/Bid/Tinb) مطابق قواعد تأییدشدهٔ سامانه مودیان:
///
///   • Tob  : نوع خریدار — ۱ = حقیقی، ۲ = حقوقی (enum قابل تنظیم MoadianBuyerType)
///   • Bid  : «شماره/شناسه ملی خریدار» — حقیقی: ۱۰ رقم + رقم کنترل کد ملی معتبر؛
///            حقوقی: ۱۱ رقم + رقم کنترل شناسهٔ ملی معتبر
///   • Tinb : «شماره اقتصادی خریدار» — حقوقی: کد اقتصادی ۱۴ رقمی یا شناسه⹀ ملی ۱۱ رقمی (متحد از bid) — حقیقی: باید خالی باشد
///            برای حقیقی باید خالی باشد
///
/// ورودی ERP فقط یک مقدار «شناسهٔ ملی/اقتصادی خریدار» بدون تفکیک نوع می‌فرستد؛
/// نوع از روی طول شناسه تشخیص داده می‌شود:
///   ۱۰ رقم = حقیقی (شمارهٔ ملی) → Tob=1
///   ۱۱ رقم = حقوقی (شناسهٔ ملی) → Tob=2
///   ۱۴ رقم = حقوقی (کد اقتصادی) → Tob=2
/// </summary>
public static class MoadianBuyerValidator
{
    /// <summary>
    /// نگاشت یک شناسهٔ واحد ERP به فیلدهای رسمی سامانه:
    ///   ۱۰ رقم → (Tob=1 حقیقی,  Bid=شمارهٔ ملی ۱۰ رقمی,  Tinb=null)
    ///   ۱۱ رقم → (Tob=2 حقوقی,  Bid=شناسه⹀ ملی ۱۱ رقمی,   Tinb=همان شناسه)
    ///   ۱۴ رقم → (Tob=2 حقوقی,  Bid=null,                Tinb=کد اقتصادی ۱۴ رقمی)
    ///   سایر   → (null, null, null) → اعتبارسنج رد می‌کند
    /// </summary>
    public static (int? Tob, string? Bid, string? Tinb) ResolveBuyer(string? buyerTaxId)
    {
        var id = buyerTaxId?.Trim();
        if (string.IsNullOrWhiteSpace(id)) return (null, null, null);
        if (!id.All(c => c >= '0' && c <= '9')) return (null, null, null);

        if (id.Length == 10) return ((int)MoadianBuyerType.Individual, id, null);
        if (id.Length == 11) return ((int)MoadianBuyerType.Legal, id, id); // ۱۱ رقم → Bid و Tinb همان شناسه ملی (نمونه تست‌شده در سامانه)
        if (id.Length == 14) return ((int)MoadianBuyerType.Legal, null, id);
        return (null, null, null);
    }

    /// <summary>اعتبارسنجی کامل سه فیلد؛ فهرست نقض‌ها (خالی = معتبر). هر نقض به‌صورت (فیلد, پیام دوزبانه).</summary>
    public static List<(string Field, string Message)> Validate(int? tob, string? bid, string? tinb)
    {
        var issues = new List<(string Field, string Message)>();
        bid = NullIfEmpty(bid);
        tinb = NullIfEmpty(tinb);

        // ---------- Tob ----------
        var tobAllowed = tob is (int)MoadianBuyerType.Individual or (int)MoadianBuyerType.Legal;
        if (!tobAllowed)
        {
            issues.Add(("tob",
                "توکن نوع خریدار باید ۱ (حقیقی) یا ۲ (حقوقی) باشد — Tob must be 1 (individual) or 2 (legal)."));
            return issues; // بدون Tob معتبر، بقیه قابل ارزیابی نیست
        }

        // ---------- Tinb (legal entity): 14-digit economic code or 11-digit national ID (sample verified in the system) ----------
        if (tob == (int)MoadianBuyerType.Legal && tinb is not null)
        {
            var tinbOk = (tinb.Length == 14 && tinb.All(c => c >= '0' && c <= '9')) || (tinb.Length == 11 && IsValidNationalId(tinb));
            if (!tinbOk)
                issues.Add(("tinb",
                    "«شماره اقتصادی خریدار» (tinb) باید خالی باشد یا شناسه⹀ ملی ۱۱ رقمی با رقم کنترلی معتبر یا کد اقتصادی ۱۴ رقمی — Tinb must be empty, an 11-digit national ID with a valid control digit, or a 14-digit economic code."));
        }

        // ---------- Bid ----------
        if (tob == (int)MoadianBuyerType.Individual)
        {
            if (bid is null)
            {
                issues.Add(("bid",
                    "برای خریدار «حقیقی»، «شمارهٔ ملی ۱۰ رقمی» (bid) الزامی است — For individual buyers, bid (10-digit national ID) is required."));
            }
            else if (bid.Length != 10 || !bid.All(c => c >= '0' && c <= '9') || !IsValidNationalId(bid))
            {
                issues.Add(("bid",
                    "برای خریدار «حقیقی»، bid باید دقیقاً ۱۰ رقم باشد و رقم کنترلی کد ملی معتبر باشد — bid must be exactly 10 digits with a valid control digit."));
            }

            // حقیقی کد اقتصادی ندارد
            if (tinb is not null)
                issues.Add(("tinb",
                    "برای خریدار «حقیقی»، فیلد «شماره اقتصادی» (tinb) باید خالی باشد — For individual buyers, tinb must be empty."));
        }
        else // Legal (Tob = 2)
        {
            if (bid is not null && (bid.Length != 11 || !bid.All(c => c >= '0' && c <= '9') || !IsValidNationalId(bid)))
                issues.Add(("bid",
                    "برای خریدار «حقوقی»، bid (در صورت وجود) باید دقیقاً ۱۱ رقم باشد و رقم کنترلی شناسهٔ ملی معتبر باشد — bid must be exactly 11 digits with a valid control digit."));

            // حقوقی: حداقل یکی از شناسهٔ ملی ۱۱ رقمی (bid) یا کد اقتصادی ۱۴ رقمی (tinb) الزامی است
            var hasBid = bid is not null && bid.Length == 11 && IsValidNationalId(bid);
            var hasTinb = tinb is not null && ((tinb.Length == 14 && tinb.All(c => c >= '0' && c <= '9')) || IsValidNationalId(tinb));
            if (!hasBid && !hasTinb)
                issues.Add(("bid",
                    "برای خریدار «حقوقی»، «شناسهٔ ملی ۱۱ رقمی» (bid) یا «کد اقتصادی ۱۴ رقمی» (tinb) الزامی است — For legal buyers, an 11-digit national ID (bid) or 14-digit economic code (tinb) is required."));
        }

        return issues;
    }

    /// <summary>
    /// اعتبارسنجی یک شناسهٔ واحد ERP (resolve + validate)؛ در صورت خطا
    /// <see cref="MoadianBuyerValidationException"/> پرتاب می‌شود تا controller آن را
    /// به 422 با نام فیلد تبدیل کند.
    /// </summary>
    public static void EnsureValid(string? buyerTaxId)
    {
        var (tob, bid, tinb) = ResolveBuyer(buyerTaxId);
        var issues = Validate(tob, bid, tinb);
        if (issues.Count == 0) return;

        var fields = string.Join("/", issues.Select(i => i.Field).Distinct());
        var message =
            "اطلاعات خریدار نامعتبر است: " + string.Join(" | ", issues.Select(i => i.Message)) +
            " — Buyer identification is invalid. Fix «شناسه ملی/اقتصادی خریدار» and try again.";
        throw new MoadianBuyerValidationException(fields, message);
    }

    /// <summary>
    /// اعتبارسنجی رقم کنترلی کد/شناسه ملی ایرانی (الگوریتم استاندارد):
    /// ضرب هر رقم از n رقم اول در ضرایب [29,27,23,19,17,29,27,23,19,17] (تا طول لازم)،
    /// جمع، باقی‌مانده بر ۱۱؛ معتبر است اگر باقی‌مانده == رقم کنترلی یا
    /// (باقی‌مانده == ۱۰ و رقم کنترلی == ۰).
    /// با نمونهٔ معتبر 14014038299 (۱۱ رقم) و 1234567898 (۱۰ رقم) سنجش شده است.
    /// </summary>
    public static bool IsValidNationalId(string id)
    {
        if (id is null) return false;
        if (id.Length is not (10 or 11)) return false;
        if (!id.All(c => c >= '0' && c <= '9')) return false;

        int[] weights = { 29, 27, 23, 19, 17, 29, 27, 23, 19, 17 };
        var sum = 0;
        var n = id.Length - 1; // n رقم اصلی + ۱ رقم کنترل
        for (var i = 0; i < n; i++)
            sum += (id[i] - '0') * weights[i];

        var r = sum % 11;
        var check = id[n] - '0';
        return r == check || (r == 10 && check == 0);
    }

    private static string? NullIfEmpty(string? s)
    {
        s = s?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }
}
