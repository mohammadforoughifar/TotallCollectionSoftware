namespace Inventory.Shared;

/// <summary>
/// قواعد پایه برای سه انتخاب مستقل صورتحساب. این قواعد فقط ناسازگاری‌های روشن
/// میان نوع، inp و ins را کنترل می‌کنند. فهرست الگوها با منابع آموزشی ثالث تطبیق اولیه شده
/// و پیش از هرگونه فعال‌سازی ارسال باید با آخرین فایل رسمی سازمان تأیید شود.
/// </summary>
public static class MoadianInvoiceRules
{
    private static readonly MoadianInvoicePattern[] Type1Patterns =
    {
        MoadianInvoicePattern.Sale,
        MoadianInvoicePattern.CurrencySale,
        MoadianInvoicePattern.GoldJewelryPlatinum,
        MoadianInvoicePattern.Contracting,
        MoadianInvoicePattern.UtilityBills,
        MoadianInvoicePattern.AirlineTicket,
        MoadianInvoicePattern.Export,
        MoadianInvoicePattern.Waybill,
        MoadianInvoicePattern.PetroleumProducts,
        MoadianInvoicePattern.CommodityExchange,
        MoadianInvoicePattern.InsuranceServices,
        MoadianInvoicePattern.ChainSales
    };

    private static readonly MoadianInvoicePattern[] Type2Patterns =
    {
        MoadianInvoicePattern.Sale,
        MoadianInvoicePattern.GoldJewelryPlatinum,
        MoadianInvoicePattern.PetroleumProducts,
        MoadianInvoicePattern.InsuranceServices
    };

    private static readonly MoadianInvoiceSubject[] StandardSubjects =
    {
        MoadianInvoiceSubject.Original,
        MoadianInvoiceSubject.Corrective,
        MoadianInvoiceSubject.Void,
        MoadianInvoiceSubject.SaleReturn
    };

    private static readonly MoadianInvoiceSubject[] ExportSubjects =
    {
        MoadianInvoiceSubject.Original,
        MoadianInvoiceSubject.Corrective,
        MoadianInvoiceSubject.Void
    };

    public static IReadOnlyList<MoadianInvoicePattern> AvailablePatterns(MoadianTaxInvoiceType type) => type switch
    {
        MoadianTaxInvoiceType.Type1 => Type1Patterns,
        MoadianTaxInvoiceType.Type2 => Type2Patterns,
        _ => Array.Empty<MoadianInvoicePattern>()
    };

    public static IReadOnlyList<MoadianInvoiceSubject> AvailableSubjects(MoadianInvoicePattern pattern)
        => pattern == MoadianInvoicePattern.Export ? ExportSubjects : StandardSubjects;

    public static bool IsPatternAllowed(MoadianTaxInvoiceType type, MoadianInvoicePattern pattern)
        => pattern != MoadianInvoicePattern.Unselected && AvailablePatterns(type).Contains(pattern);

    public static bool IsSubjectAllowed(MoadianInvoicePattern pattern, MoadianInvoiceSubject subject)
        => subject != MoadianInvoiceSubject.Unselected && AvailableSubjects(pattern).Contains(subject);

    public static bool RequiresReference(MoadianInvoiceSubject subject)
        => subject is MoadianInvoiceSubject.Corrective or MoadianInvoiceSubject.Void or MoadianInvoiceSubject.SaleReturn;

    public static bool IsValidReferenceTaxId(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Trim().Length == 22;

    /// <summary>پیام اعتبارسنجی انتخاب‌ها؛ null یعنی سه انتخاب و ارجاعِ پایه سازگارند.</summary>
    public static string? ValidateSelection(
        MoadianTaxInvoiceType type,
        MoadianInvoicePattern pattern,
        MoadianInvoiceSubject subject,
        string? referenceTaxId)
    {
        if (type is not (MoadianTaxInvoiceType.Type1 or MoadianTaxInvoiceType.Type2))
            return "نوع صورتحساب را به‌صورت جداگانه (نوع ۱ یا ۲) انتخاب کنید.";

        if (pattern == MoadianInvoicePattern.Unselected)
            return "الگوی صورتحساب (inp) را انتخاب کنید.";
        if (!Enum.IsDefined(pattern) || !IsPatternAllowed(type, pattern))
            return "الگوی انتخابی با نوع صورتحساب سازگار نیست؛ گزینهٔ مجاز را انتخاب کنید.";

        if (subject == MoadianInvoiceSubject.Unselected)
            return "موضوع صورتحساب (ins) را انتخاب کنید.";
        if (!Enum.IsDefined(subject))
            return "کد موضوع انتخاب‌شده در فهرست شناخته‌شده نیست.";
        if (!IsSubjectAllowed(pattern, subject))
            return pattern == MoadianInvoicePattern.Export && subject == MoadianInvoiceSubject.SaleReturn
                ? "موضوع برگشت از فروش برای الگوی صادرات مجاز نیست؛ موضوع دیگری انتخاب کنید."
                : "موضوع انتخاب‌شده با الگوی صورتحساب سازگار نیست.";

        if (RequiresReference(subject) && !IsValidReferenceTaxId(referenceTaxId))
            return "برای موضوع اصلاحی، ابطالی یا برگشت از فروش، شناسهٔ ۲۲ نویسه‌ای صورتحساب مرجع را وارد کنید.";
        if (!RequiresReference(subject) && !string.IsNullOrWhiteSpace(referenceTaxId))
            return "برای موضوع اصلی نباید شناسهٔ صورتحساب مرجع وارد شود.";

        return null;
    }

    /// <summary>
    /// کنترل محلی زمان صدور. قالب دقیق wire برای indatim باید در سازندهٔ payload
    /// با نسخهٔ جاری مستند رسمی اعمال شود؛ این بررسی فقط مقدار خالی/آینده را رد می‌کند.
    /// </summary>
    public static string? ValidateIssueDate(DateTime issueDate, DateTime? now = null)
    {
        if (issueDate == default)
            return "تاریخ و زمان صدور (indatim) وارد نشده یا معتبر نیست.";
        if (issueDate > (now ?? DateTime.Now))
            return "تاریخ و زمان صدور نمی‌تواند در آینده باشد.";
        return null;
    }

    public static string PatternTitle(MoadianInvoicePattern pattern) => pattern switch
    {
        MoadianInvoicePattern.Sale => "فروش کالا و خدمات",
        MoadianInvoicePattern.CurrencySale => "فروش ارز",
        MoadianInvoicePattern.GoldJewelryPlatinum => "طلا، جواهر و پلاتین",
        MoadianInvoicePattern.Contracting => "قرارداد پیمانکاری",
        MoadianInvoicePattern.UtilityBills => "قبوض خدماتی",
        MoadianInvoicePattern.AirlineTicket => "بلیت هواپیما",
        MoadianInvoicePattern.Export => "صادرات",
        MoadianInvoicePattern.Waybill => "بارنامه",
        MoadianInvoicePattern.PetroleumProducts => "فرآورده‌های نفتی",
        MoadianInvoicePattern.CommodityExchange => "بورس کالا",
        MoadianInvoicePattern.InsuranceServices => "خدمات بیمه‌ای",
        MoadianInvoicePattern.ChainSales => "فروش زنجیره‌ای",
        _ => "انتخاب نشده"
    };

    public static string SubjectTitle(MoadianInvoiceSubject subject) => subject switch
    {
        MoadianInvoiceSubject.Original => "اصلی",
        MoadianInvoiceSubject.Corrective => "اصلاحی",
        MoadianInvoiceSubject.Void => "ابطالی",
        MoadianInvoiceSubject.SaleReturn => "برگشت از فروش",
        _ => "انتخاب نشده"
    };
}
