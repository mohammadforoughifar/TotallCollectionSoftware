using System.Globalization;
using QuestPDF.Elements;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Inventory.Api.Services.Office;

/// <summary>
/// قالب مشترک چاپ نامه‌های اداری (نامهٔ داخلی و نامهٔ صادره).
///
/// چرا مشترک؟ نامه صادره باید «شبیه چاپ نامه داخلی» باشد: همان فونت، همان اندازه، همان
/// خط جداکنندهٔ نازک خاکستری و همان بلوک «برچسب: مقدار». اگر این اعداد در دو فایل تکرار
/// شوند، دیر یا زود از هم جدا می‌افتند؛ پس یک‌جا نگه داشته شده‌اند و هر دو مسیر چاپ از
/// همین فایل استفاده می‌کنند.
///
/// نکته: این فایل رفتار چاپ نامه داخلی را تغییر نمی‌دهد؛ فقط اقلامی که از قبل هم در
/// نامه داخلی همین مقادیر را داشتند (خط جداکننده با ضخامت ۰٫۷ و رنگ #999999 و فاصلهٔ ۵pt،
/// و تبدیل ارقام فارسی) به‌اشتراک گذاشته شده‌اند.
/// </summary>
internal static class LetterPrintTemplate
{
    // ── استایل مشترک نامه‌های اداری ──
    /// <summary>ضخامت خط جداکنندهٔ بین بلوک اطلاعات و متن (۰٫۷pt ≈ ۱px).</summary>
    internal const float SeparatorThickness = 0.7f;

    /// <summary>رنگ خاکستری روشن خط جداکننده.</summary>
    internal const string SeparatorColor = "#999999";

    /// <summary>فاصلهٔ عمودی یکسان بالا و پایین خط جداکننده.</summary>
    internal const float SeparatorSpacing = 5f;

    /// <summary>فاصله خطوط متن اصلی نامه.</summary>
    internal const float BodyLineHeight = 1.6f;

    /// <summary>حداقل فاصله بین پاراگراف‌ها (point).</summary>
    internal const float ParagraphSpacing = 6f;

    /// <summary>حداقل حاشیهٔ چپ و راست (میلی‌متر) — برابر در دو طرف.</summary>
    internal const float SideMarginMillimeters = 15f;

    /// <summary>
    /// رنگ مقدارهای سربرگ (شماره/تاریخ/پیوست). مثل قبل و مثل خودِ طرح، **سفید** است تا با
    /// برچسب‌های سفیدِ چاپ‌شده روی نوار آبی یکدست باشد (درخواست کارفرما).
    /// </summary>
    internal const string MetaValueColor = "#FFFFFF";

    /// <summary>مقادیر کم‌رنگ سربرگ (پیوست/شماره صفحه).</summary>
    internal const string MutedColor = "#475569";

    internal static float MmToPoints(float millimeters) => millimeters * 72f / 25.4f;

    /// <summary>علامت راست‌به‌چپ (Right-to-Left Mark) — یک کاراکتر قویِ RTL و بی‌عرض.</summary>
    internal const string Rlm = "\u200F";

    /// <summary>
    /// برچسب فارسی با دونقطه در پایانِ منطقی، محصور در RLM (U+200F) از دو طرف.
    ///
    /// چرا؟ دونقطه (:) کاراکتر «خنثی» (neutral) است. وقتی آخرین کاراکترِ یک متن باشد، جهت
    /// نمایشش را از جهت پایهٔ پاراگراف می‌گیرد؛ نتیجه این بود که برچسب «موضوع:» به‌صورت
    /// «:موضوع» چاپ می‌شد (دونقطه سمت راست کلمه، یعنی قبل از آن). با گذاشتن RLM بعد از
    /// دونقطه، دونقطه بین دو کاراکتر قویِ راست‌به‌چپ می‌نشیند و همیشه سمت چپ کلمه — یعنی
    /// همان «پس از کلمه» — چاپ می‌شود. RLM ابتدای متن هم جهت پایهٔ پاراگراف را قطعی
    /// می‌کند؛ همان روشی که <c>RichLetterPdfRenderer</c> برای متن نامه به کار می‌برد.
    /// RLM عرض ندارد، پس چیدمان و عرض برچسب تغییری نمی‌کند.
    ///
    /// برچسب‌هایی که دونقطه را ابتدا دارند (مثل «:حاضرین» در فایل‌های MRT) هم همین‌جا
    /// اصلاح می‌شوند تا دوبار دونقطه نشود.
    /// </summary>
    /// <param name="label">متن برچسب.</param>
    /// <param name="ensureTrailingColon">
    /// اگر true باشد (پیش‌فرض) همیشه یک دونقطه در پایان گذاشته می‌شود؛ برای برچسب‌هایی که
    /// در متن خودشان دونقطه ندارند ولی باید داشته باشند («موضوع»، «رونوشت»).
    /// اگر false باشد فقط دونقطه‌های موجود جابه‌جا/محصور می‌شوند (برچسب‌های آمادهٔ قالب MRT).
    /// </param>
    internal static string RtlLabel(string? label, bool ensureTrailingColon = true)
    {
        var value = (label ?? string.Empty).Trim();
        if (value.Length == 0) return string.Empty;
        if (value.StartsWith(':') || value.StartsWith('\uFF1A')) value = value[1..].Trim();
        var hadColon = value.EndsWith(':') || value.EndsWith('\uFF1A');
        value = value.TrimEnd(':', '\uFF1A').TrimEnd();
        if (value.Length == 0) return string.Empty;
        return hadColon || ensureTrailingColon
            ? Rlm + value + ":" + Rlm
            : Rlm + value + Rlm;
    }

    /// <summary>
    /// یک ردیف از بلوک اطلاعات نامه («برچسب» + «مقدار»).
    /// <paramref name="HideLabel"/> یعنی مقدار بدون برچسب چاپ شود (مثل ردیف بالای گیرنده).
    /// </summary>
    internal sealed record InfoRow(
        string Label,
        string Value,
        bool BoldValue = false,
        bool HideLabel = false);

    /// <summary>
    /// بلوک اطلاعات نامه (عنوان، گیرنده، ایجادکننده، رونوشت، موضوع) در دو ستون:
    /// برچسب سمت راست و مقدار کنارش. مقدار بلند در ستون خودش می‌شکند و خط دوم زیر
    /// برچسب نمی‌رود (تورفتگی آویزان). ردیف‌های خالی اصلاً چاپ نمی‌شوند.
    /// </summary>
    internal static void InfoBlock(
        ColumnDescriptor column,
        IReadOnlyList<InfoRow> rows,
        float fontSize,
        float rowSpacing,
        float labelGap)
    {
        var visible = rows.Where(row => !string.IsNullOrWhiteSpace(row.Value)).ToList();
        if (visible.Count == 0) return;

        // عرض ستون برچسب از بلندترین برچسب تخمین زده می‌شود (QuestPDF عرض متن را از پیش
        // نمی‌دهد) و یک حاشیهٔ اطمینان می‌گیرد تا برچسب هرگز نشکند.
        // ردیف‌های بدون برچسب (مثل گیرنده) در عرض ستون برچسب شریک نیستند تا مقدارشان
        // تمام عرض ستون متن را بگیرد.
        var labelled = visible.Where(row => !row.HideLabel).ToList();
        var labelWidth = labelled.Count == 0
            ? 0f
            : labelled.Max(row => row.Label.Length) * fontSize * 0.58f + fontSize;

        var first = true;
        foreach (var row in visible)
        {
            var item = first ? column.Item() : column.Item().PaddingTop(rowSpacing);
            first = false;
            var bold = row.BoldValue;
            item.Row(line =>
            {
                // مقدار: ستون اختصاصی خودش؛ خط دوم هرگز زیر برچسب نمی‌رود (تورفتگی آویزان).
                line.RelativeItem().AlignTop().AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(style => style
                        .FontFamily(bold ? "Vazirmatn-Bold" : "Vazirmatn")
                        .FontSize(fontSize)
                        .LineHeight(1.55f)
                        .DirectionFromRightToLeft());
                    text.AlignRight();
                    var span = text.Span(row.Value);
                    if (bold) span.Bold();
                });

                // ردیف بدون برچسب: همین‌جا تمام می‌شود (گیرنده تمام‌عرض، بدون «گیرنده نامه:»).
                if (row.HideLabel || labelWidth <= 0f) return;

                // فاصلهٔ ثابت بین مقدار و برچسب (همان «یک فاصله» بعد از دونقطه).
                line.ConstantItem(labelGap);

                // برچسب: هم‌اندازه و هم‌خط با مقدار، چسبیده به لبهٔ راستِ متن (مثل نامهٔ داخلی)
                // و دونقطه در انتهای خودش (سمت مقدار).
                line.ConstantItem(labelWidth).AlignTop().AlignRight()
                    .Text(RtlLabel(row.Label))
                    .FontFamily("Vazirmatn-Bold")
                    .FontSize(fontSize)
                    .LineHeight(1.55f)
                    .AlignRight()
                    .DirectionFromRightToLeft();
            });
        }
    }

    /// <summary>
    /// بلوک «رونوشت:» — زیر متن نامه و بالای بلوک امضا (درخواست کارفرما، برای همهٔ شرکت‌ها).
    /// هر رونوشت در خط خودش می‌آید؛ اگر تعدادشان زیاد باشد خودکار به خطوط بعد می‌رود
    /// (بدون بریدگی و بدون فشرده‌کردن فونت) و خط دومِ هر رونوشت زیر خودش (تورفتگی آویزان)
    /// می‌نشیند، نه زیر برچسب.
    /// </summary>
    internal static void CopyToBlock(
        ColumnDescriptor column,
        IReadOnlyList<string> copies,
        float fontSize,
        float labelGap,
        float topPadding)
    {
        var visible = copies
            .Where(copy => !string.IsNullOrWhiteSpace(copy))
            .Select(copy => copy.Trim())
            .ToList();
        if (visible.Count == 0) return;

        // دونقطهٔ پایان برچسب با RLM محصور می‌شود تا «رونوشت:» و نه «:رونوشت» چاپ شود.
        var label = RtlLabel("رونوشت");
        var labelWidth = "رونوشت:".Length * fontSize * 0.58f + fontSize;
        column.Item().PaddingTop(topPadding).Row(line =>
        {
            // هر رونوشت یک آیتم مستقل: با زیاد شدن تعداد، به خطوط بعد می‌رود.
            line.RelativeItem().AlignTop().AlignRight().Column(values =>
            {
                for (var index = 0; index < visible.Count; index++)
                {
                    values.Item().Text(text =>
                    {
                        text.DefaultTextStyle(style => style
                            .FontFamily("Vazirmatn")
                            .FontSize(fontSize)
                            .LineHeight(1.55f)
                            .DirectionFromRightToLeft());
                        text.AlignRight();
                        text.Span(visible[index]);
                    });
                }
            });

            line.ConstantItem(labelGap);

            line.ConstantItem(labelWidth).AlignTop().AlignRight()
                .Text(label)
                .FontFamily("Vazirmatn-Bold")
                .FontSize(fontSize)
                .LineHeight(1.55f)
                .AlignRight()
                .DirectionFromRightToLeft();
        });
    }

    /// <summary>
    /// خط افقی نازک و خاکستری بین بلوک اطلاعات و متن نامه. عرض آن خودکار برابر عرض
    /// ناحیهٔ متن است (چون داخل همان ستون/حاشیه رسم می‌شود) و فاصلهٔ بالا/پایینش یکسان است.
    /// </summary>
    internal static void Separator(ColumnDescriptor column, float spacing = SeparatorSpacing)
        => column.Item()
            .PaddingVertical(spacing)
            .LineHorizontal(SeparatorThickness)
            .LineColor(SeparatorColor);

    /// <summary>
    /// فاصلهٔ خالی داخل ستون (بدون هیچ خطی). نامهٔ صادرهٔ ۱۰۰۳ به‌درخواست کارفرما خط جداکننده
    /// ندارد؛ فقط همان فاصلهٔ بالا/پایینِ خط به‌صورت خالی نگه داشته می‌شود تا متن به بلوک
    /// اطلاعات نچسبد.
    /// </summary>
    internal static void Spacer(ColumnDescriptor column, float height)
        => column.Item().Height(Math.Max(0f, height));

    /// <summary>ارقام لاتین و عربی → ارقام فارسی (۴ ۵ ۶ نه ٤ ٥ ٦ و نه 456).</summary>
    internal static string PersianDigits(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;

        var chars = value.ToCharArray();
        for (var index = 0; index < chars.Length; index++)
        {
            if (chars[index] is >= '0' and <= '9')
                chars[index] = (char)('\u06F0' + (chars[index] - '0'));
            else if (chars[index] is >= '\u0660' and <= '\u0669')
                chars[index] = (char)('\u06F0' + (chars[index] - '\u0660'));
        }

        return new string(chars);
    }

    /// <summary>تاریخ شمسی با ارقام فارسی: ۱۴۰۵/۰۸/۰۶.</summary>
    internal static string PersianDate(DateTime? date)
    {
        if (date is null) return "—";
        var calendar = new PersianCalendar();
        var value = date.Value;
        return PersianDigits(
            $"{calendar.GetYear(value)}/{calendar.GetMonth(value):00}/{calendar.GetDayOfMonth(value):00}");
    }

    /// <summary>
    /// مقادیر ترکیبی فارسی/لاتین (مثل «۲۲۱/۱۴۰۵/IT») با جداسازی جهت (LRI/PDI) چاپ می‌شوند
    /// تا ترتیب اجزا در پاراگراف راست‌به‌چپ جابه‌جا نشود — همان کاری که &lt;bdi&gt; می‌کند.
    /// </summary>
    internal static string IsolateMixedText(string value)
    {
        var hasLatin = value.Any(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
        return hasLatin ? "\u2066" + value + "\u2069" : value;
    }
}
