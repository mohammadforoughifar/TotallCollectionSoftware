using Inventory.Shared.Dtos;

namespace Inventory.Api.Services.ReportStudio;

// =====================================================================
//  کاتالوگ اسکیما — «فهرست سفید» جدول‌ها، ستون‌ها و جوین‌های مجاز
//
//  این کلاس قلب امنیت گزارش‌ساز است: کلاینت هیچ‌وقت SQL یا نام واقعی
//  جدول/ستون نمی‌فرستد، فقط کلیدهای همین کاتالوگ را می‌فرستد. هر کلیدی
//  که اینجا نباشد، در اعتبارسنجی رد می‌شود.
//
//  افزودن جدول جدید = یک ورودی در Tables + (در صورت نیاز) چند Join
//  + یک case در RsQuerySource برای ساخت IQueryable آن.
// =====================================================================
public static class RsSchemaCatalog
{
    // ---------- کمکی‌های ساخت ستون ----------
    private static RsFieldDto F(string key, string title, RsFieldType type,
        bool group = true, bool agg = false, string? unit = null, string? hint = null,
        Dictionary<string, string>? enumMap = null) => new()
    {
        Key = key,
        Title = title,
        Type = type,
        Groupable = group,
        Aggregatable = agg,
        Unit = unit,
        Hint = hint,
        EnumMap = enumMap
    };

    private static RsFieldDto Text(string k, string t, string? hint = null) =>
        F(k, t, RsFieldType.Text, hint: hint);

    private static RsFieldDto Num(string k, string t, string? unit = null) =>
        F(k, t, RsFieldType.Number, group: false, agg: true, unit: unit);

    private static RsFieldDto Money(string k, string t) =>
        F(k, t, RsFieldType.Money, group: false, agg: true, unit: "ریال");

    private static RsFieldDto Date(string k, string t) =>
        F(k, t, RsFieldType.Date, agg: true);

    private static RsFieldDto Bool(string k, string t, string? hint = null) =>
        F(k, t, RsFieldType.Bool, hint: hint);

    private static RsFieldDto Enum(string k, string t, Dictionary<string, string> map) =>
        F(k, t, RsFieldType.Enum, enumMap: map);

    // ---------- نگاشت‌های Enum ----------
    private static Dictionary<string, string> LetterTypeMap => new()
    {
        ["1"] = "داخلی", ["2"] = "صادره", ["3"] = "وارده"
    };

    private static Dictionary<string, string> OutStatusMap => new()
    {
        ["0"] = "پیش‌نویس", ["1"] = "در گردش تایید", ["2"] = "تایید شده", ["3"] = "صادر شده"
    };

    private static Dictionary<string, string> LevelMap => new()
    {
        ["0"] = "عادی", ["1"] = "محرمانه", ["2"] = "خیلی محرمانه", ["3"] = "سرّی"
    };

    private static Dictionary<string, string> UrgencyMap => new()
    {
        ["0"] = "عادی", ["1"] = "فوری", ["2"] = "خیلی فوری", ["3"] = "آنی"
    };

    private static Dictionary<string, string> TaeedMap => new()
    {
        ["0"] = "بدون اقدام", ["1"] = "تایید", ["2"] = "رد"
    };

    private static Dictionary<string, string> InvoiceKindMap => new()
    {
        ["0"] = "فروش", ["1"] = "خرید", ["2"] = "برگشت از فروش", ["3"] = "برگشت از خرید"
    };

    private static Dictionary<string, string> InvoiceStatusMap => new()
    {
        ["0"] = "پیش‌نویس", ["1"] = "قطعی", ["2"] = "باطل"
    };

    // =====================================================================
    //  جدول‌ها
    // =====================================================================
    public static readonly IReadOnlyList<RsTableDto> Tables = new List<RsTableDto>
    {
        // ---------------- اتوماسیون اداری ----------------
        new()
        {
            Key = "letter",
            Title = "نامه‌ها (هر سه نوع)",
            Category = "اتوماسیون اداری",
            Icon = "bi-envelope-paper",
            Module = "InnerLetters",
            Description = "هر سطر یک نامه است — داخلی، صادره یا وارده. نقطهٔ شروع بیشتر گزارش‌های مکاتبات.",
            Fields = new()
            {
                Num("letter.id", "شناسه"),
                Enum("letter.type", "نوع نامه", LetterTypeMap),
                Text("letter.no", "شماره نامه"),
                Text("letter.title", "موضوع"),
                Date("letter.date", "تاریخ ثبت"),
                Date("letter.date_closed", "تاریخ صدور / تاریخ نامه"),
                Text("letter.creator", "ثبت‌کننده"),
                Text("letter.status", "وضعیت"),
                Enum("letter.conf", "محرمانگی", LevelMap),
                Enum("letter.urg", "فوریت", UrgencyMap),
                Text("letter.party", "طرف مقابل", "گیرندهٔ صادره یا فرستندهٔ وارده"),
                Text("letter.method", "روش ارسال"),
                Bool("letter.starred", "نشان‌دار"),
                Bool("letter.archived", "بایگانی‌شده"),
                Num("letter.erja_count", "تعداد ارجاع", "مورد"),
                Num("letter.days_open", "روزهای باز", "روز"),
                F("letter.count", "تعداد نامه", RsFieldType.Number,
                  group: false, agg: true, unit: "نامه", hint: "برای شمارش نامه‌ها از این سنجه استفاده کنید")
            }
        },
        new()
        {
            Key = "erja",
            Title = "ارجاع‌ها",
            Category = "اتوماسیون اداری",
            Icon = "bi-arrow-left-right",
            Module = "InnerLetters",
            Description = "هر سطر یک ارجاع نامه به یک کاربر است. برای تحلیل گردش کار و پاسخ‌گویی.",
            Fields = new()
            {
                Num("erja.id", "شناسه"),
                Text("erja.sender", "فرستنده"),
                Text("erja.receiver", "گیرنده"),
                Date("erja.date", "تاریخ ارجاع"),
                Text("erja.type", "نوع ارجاع", "گیرنده / ارجاع / هامش"),
                Enum("erja.taeed", "وضعیت تایید", TaeedMap),
                Bool("erja.is_read", "خوانده‌شده"),
                Bool("erja.answered", "پاسخ داده‌شده"),
                Bool("erja.overdue", "معوق", "مهلت گذشته و بی‌پاسخ"),
                Date("erja.deadline", "مهلت پاسخ"),
                Text("erja.note", "متن ارجاع"),
                F("erja.count", "تعداد ارجاع", RsFieldType.Number,
                  group: false, agg: true, unit: "مورد")
            }
        },

        new()
        {
            Key = "outgoing",
            Title = "نامه‌های صادره",
            Category = "اتوماسیون اداری",
            Icon = "bi-envelope-arrow-up",
            Module = "OutgoingLetters",
            Description = "جزئیات کامل نامه‌های صادره: گیرنده، دبیرخانه، روش ارسال و رهگیری.",
            Fields = new()
            {
                Num("outgoing.id", "شناسه"),
                Text("outgoing.no", "شماره نامه"),
                Text("outgoing.sadere_no", "شماره صادره"),
                Num("outgoing.number", "شماره ترتیبی"),
                Text("outgoing.title", "موضوع"),
                Date("outgoing.date_sabt", "تاریخ ثبت"),
                Date("outgoing.date_sadere", "تاریخ صدور"),
                Text("outgoing.creator", "ثبت‌کننده"),
                Enum("outgoing.status", "وضعیت", OutStatusMap),
                Text("outgoing.receiver_org", "سازمان گیرنده"),
                Text("outgoing.receiver_name", "نام گیرنده"),
                Text("outgoing.receiver_title", "سمت گیرنده"),
                Text("outgoing.method", "روش ارسال"),
                Text("outgoing.tracking", "کد رهگیری"),
                Text("outgoing.deliverer", "تحویل‌گیرنده"),
                Text("outgoing.dest_reg_no", "شماره ثبت مقصد"),
                Text("outgoing.dest_email", "ایمیل مقصد"),
                Text("outgoing.ext_ref", "شماره عطف خارجی"),
                Text("outgoing.copy_to", "رونوشت"),
                Text("outgoing.conf", "محرمانگی"),
                Text("outgoing.urg", "فوریت"),
                Bool("outgoing.dabirkhane", "ثبت دبیرخانه"),
                Date("outgoing.date_dabirkhane", "تاریخ دبیرخانه"),
                Bool("outgoing.starred", "نشان‌دار"),
                Num("outgoing.days_to_issue", "روز تا صدور", "روز"),
                F("outgoing.count", "تعداد نامه صادره", RsFieldType.Number,
                  group: false, agg: true, unit: "نامه")
            }
        },
        new()
        {
            Key = "incoming",
            Title = "نامه‌های وارده",
            Category = "اتوماسیون اداری",
            Icon = "bi-envelope-arrow-down",
            Module = "IncomingLetters",
            Description = "جزئیات کامل نامه‌های وارده: فرستنده، شماره ثبت و وضعیت بایگانی.",
            Fields = new()
            {
                Num("incoming.id", "شناسه"),
                Text("incoming.no", "شماره نامه"),
                Num("incoming.number_sabt", "شماره ثبت"),
                Text("incoming.number_varede", "شماره نامهٔ فرستنده"),
                Text("incoming.title", "موضوع"),
                Date("incoming.date", "تاریخ نامه"),
                Date("incoming.date_ersal", "تاریخ دریافت"),
                Text("incoming.creator", "ثبت‌کننده"),
                Text("incoming.sender", "فرستنده"),
                Text("incoming.method", "نحوهٔ ارسال"),
                Text("incoming.delivery_name", "تحویل‌دهنده"),
                Enum("incoming.conf", "محرمانگی", LevelMap),
                Enum("incoming.urg", "فوریت", UrgencyMap),
                Bool("incoming.archived", "بایگانی‌شده"),
                Bool("incoming.starred", "نشان‌دار"),
                Text("incoming.description", "توضیحات"),
                Num("incoming.days_open", "روزهای باز", "روز"),
                F("incoming.count", "تعداد نامه وارده", RsFieldType.Number,
                  group: false, agg: true, unit: "نامه")
            }
        },

        // ---------------- فروش و خرید ----------------
        new()
        {
            Key = "invoice",
            Title = "فاکتورها",
            Category = "فروش و خرید",
            Icon = "bi-receipt",
            Module = "FacInvoices",
            Description = "سرجمع هر فاکتور فروش/خرید.",
            Fields = new()
            {
                Num("invoice.id", "شناسه"),
                Num("invoice.number", "شماره فاکتور"),
                Enum("invoice.kind", "نوع", InvoiceKindMap),
                Enum("invoice.status", "وضعیت", InvoiceStatusMap),
                Date("invoice.date", "تاریخ"),
                Date("invoice.due", "سررسید"),
                Text("invoice.party", "طرف حساب"),
                Text("invoice.warehouse", "انبار"),
                Money("invoice.gross", "مبلغ ناخالص"),
                Money("invoice.discount", "تخفیف"),
                Money("invoice.vat", "مالیات"),
                Money("invoice.net", "مبلغ خالص"),
                F("invoice.count", "تعداد فاکتور", RsFieldType.Number,
                  group: false, agg: true, unit: "فاکتور")
            }
        },
        new()
        {
            Key = "invoice_line",
            Title = "اقلام فاکتور",
            Category = "فروش و خرید",
            Icon = "bi-list-ul",
            Module = "FacInvoices",
            Description = "هر سطر یک قلم کالا در فاکتور. برای تحلیل فروش بر اساس کالا.",
            Fields = new()
            {
                Text("invoice_line.product", "کالا"),
                Text("invoice_line.product_code", "کد کالا"),
                Text("invoice_line.category", "دستهٔ کالا"),
                Text("invoice_line.unit", "واحد"),
                Num("invoice_line.qty", "مقدار"),
                Money("invoice_line.price", "فی"),
                Money("invoice_line.discount", "تخفیف"),
                Money("invoice_line.total", "مبلغ کل"),
                F("invoice_line.count", "تعداد قلم", RsFieldType.Number,
                  group: false, agg: true, unit: "قلم")
            }
        },

        // ---------------- پایه ----------------
        new()
        {
            Key = "party",
            Title = "طرف حساب‌ها",
            Category = "اطلاعات پایه",
            Icon = "bi-people",
            Module = "Parties",
            Description = "مشتریان و تأمین‌کنندگان.",
            Fields = new()
            {
                Num("party.id", "شناسه"),
                Text("party.name", "نام"),
                Text("party.mobile", "موبایل"),
                Text("party.phone", "تلفن"),
                Bool("party.is_active", "فعال"),
                F("party.count", "تعداد طرف حساب", RsFieldType.Number,
                  group: false, agg: true, unit: "نفر")
            }
        },
        new()
        {
            Key = "product",
            Title = "کالاها",
            Category = "اطلاعات پایه",
            Icon = "bi-box-seam",
            Module = "Products",
            Description = "فهرست کالاها و خدمات.",
            Fields = new()
            {
                Num("product.id", "شناسه"),
                Text("product.name", "نام کالا"),
                Text("product.code", "کد"),
                Text("product.category", "دسته"),
                Text("product.unit", "واحد"),
                Money("product.sale_price", "قیمت فروش"),
                Money("product.purchase_price", "قیمت خرید"),
                Bool("product.is_active", "فعال"),
                F("product.count", "تعداد کالا", RsFieldType.Number,
                  group: false, agg: true, unit: "قلم")
            }
        }
    };

    // =====================================================================
    //  جوین‌های مجاز
    //
    //  فقط این مسیرها قابل استفاده‌اند. کاربر نمی‌تواند دو جدول بی‌ربط را
    //  به هم بچسباند و ضرب دکارتی بسازد.
    // =====================================================================
    public static readonly IReadOnlyList<RsJoinPathDto> Joins = new List<RsJoinPathDto>
    {
        new()
        {
            Key = "letter->erja",
            FromTable = "letter",
            ToTable = "erja",
            Title = "نامه ← ارجاع‌های آن",
            Description = "هر نامه می‌تواند چند ارجاع داشته باشد؛ سطرهای نامه به تعداد ارجاع‌ها تکرار می‌شوند.",
            Multiplies = true
        },
        new()
        {
            Key = "outgoing->erja",
            FromTable = "outgoing",
            ToTable = "erja",
            Title = "نامه صادره ← ارجاع‌های آن",
            Description = "گردش تایید و ارجاع نامه‌های صادره.",
            Multiplies = true
        },
        new()
        {
            Key = "incoming->erja",
            FromTable = "incoming",
            ToTable = "erja",
            Title = "نامه وارده ← ارجاع‌های آن",
            Description = "ارجاع نامه‌های وارده به کاربران.",
            Multiplies = true
        },
        new()
        {
            Key = "invoice->invoice_line",
            FromTable = "invoice",
            ToTable = "invoice_line",
            Title = "فاکتور ← اقلام آن",
            Description = "هر فاکتور چند قلم دارد؛ سطرها تکرار می‌شوند.",
            Multiplies = true
        },
        new()
        {
            Key = "invoice->party",
            FromTable = "invoice",
            ToTable = "party",
            Title = "فاکتور ← طرف حساب",
            Description = "اطلاعات کامل مشتری/تأمین‌کنندهٔ فاکتور.",
            Multiplies = false
        },
        new()
        {
            Key = "invoice_line->product",
            FromTable = "invoice_line",
            ToTable = "product",
            Title = "قلم فاکتور ← کالا",
            Description = "مشخصات کامل کالای هر قلم.",
            Multiplies = false
        }
    };

    // =====================================================================
    //  جست‌وجوها
    // =====================================================================

    public static RsTableDto? FindTable(string? key) =>
        key is null ? null : Tables.FirstOrDefault(t =>
            t.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public static RsFieldDto? FindField(string? fieldKey)
    {
        if (string.IsNullOrWhiteSpace(fieldKey)) return null;
        var dot = fieldKey.IndexOf('.');
        if (dot <= 0) return null;
        var table = FindTable(fieldKey[..dot]);
        return table?.Fields.FirstOrDefault(f =>
            f.Key.Equals(fieldKey, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>جدولی که این ستون به آن تعلق دارد.</summary>
    public static string? TableOfField(string? fieldKey)
    {
        if (string.IsNullOrWhiteSpace(fieldKey)) return null;
        var dot = fieldKey.IndexOf('.');
        return dot <= 0 ? null : fieldKey[..dot];
    }

    public static RsJoinPathDto? FindJoin(string? key) =>
        key is null ? null : Joins.FirstOrDefault(j =>
            j.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>جوین‌هایی که از یک جدولِ حاضر در گزارش به جدول تازه می‌روند.</summary>
    public static IEnumerable<RsJoinPathDto> JoinsFrom(IEnumerable<string> presentTables)
    {
        var set = new HashSet<string>(presentTables, StringComparer.OrdinalIgnoreCase);
        return Joins.Where(j => set.Contains(j.FromTable) && !set.Contains(j.ToTable));
    }
}
