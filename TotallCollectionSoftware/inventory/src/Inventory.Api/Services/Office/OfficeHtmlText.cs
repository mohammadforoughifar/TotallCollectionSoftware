using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Inventory.Shared.Security;

namespace Inventory.Api.Services.Office;

/// <summary>
/// تبدیل بدنهٔ HTML نامه به متنی که روی کاغذ چاپ می‌شود — با حذف کامل «سرِ» سند.
///
/// چرا این کلاس هست: بدنهٔ بعضی نامه‌ها (مخصوصاً نامه‌های ساخته‌شده از ایمیل یا متن
/// کپی‌شده از Word/Outlook) یک **سند کامل HTML** است: <c>&lt;head&gt;</c> با
/// <c>&lt;meta&gt;</c>، بلوک‌های <c>&lt;style&gt;</c> با تعریف <c>@font-face</c> و
/// <c>mso-*</c>، و کامنت‌های شرطی <c>&lt;!--[if gte mso 9]&gt;…&lt;![endif]--&gt;</c>.
/// متنی که قبلاً استفاده می‌شد فقط تگ‌ها را حذف می‌کرد، بنابراین:
///   • متنِ داخل head/style/xml روی کاغذ می‌آمد («Normal»، «false»، «EN-US»،
///     «{mso-style-name:…}»)،
///   • و شکست‌خط‌های خودِ فایل HTML (هر بلوک CSS یک خط) هر کدام یک خط چاپی می‌شد،
///     یعنی ده‌ها خط نامرئی/خالی و چند صفحهٔ خالی پشت‌سرهم.
///
/// ترتیب کار (مهم است):
///   ۱) کامنت‌ها با محتوایشان (شامل کامنت‌های شرطی Word)
///   ۲) بلوک‌های غیرمتنی با محتوایشان: head / style / script / title / xml / svg / …
///   ۳) شکست‌های ساختاری (br، پایان p/div/li/…) به نشانهٔ موقت
///   ۴) حذف بقیهٔ تگ‌ها، سپس رمزگشایی موجودیت‌ها (دو بار: متن دوبار-encode هم درست شود)
///   ۵) **همهٔ** فضای سفیدِ سورس (شامل \r\n و تب و فاصلهٔ نشکن) به یک فاصله
///      ← همان جایی که نسخهٔ قبلی نشتی داشت و خطوط خالی می‌ساخت
///   ۶) نشانهٔ موقت به خط جدید، خط‌ها trim، و حداکثر یک خط خالی پشت‌سرهم
/// </summary>
internal static class OfficeHtmlText
{
    /// <summary>بلوکی که باید با محتوایش دور ریخته شود (نه فقط تگش).</summary>
    private static readonly Regex DroppedBlocks = new(
        @"<\s*(head|style|script|title|xml|svg|math|object|iframe|form|select|button|textarea|video|audio)\b[^>]*>.*?<\s*/\s*\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>همان بلوک‌های بالا اگر تگ پایانی نداشته باشند (سند نیمه‌خراب).</summary>
    private static readonly Regex DroppedOpenTags = new(
        @"<\s*/?\s*(head|style|script|title|xml|svg|math|object|iframe|form|select|button|textarea|video|audio)\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex Comments = new(@"<!--.*?-->",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex LineBreaks = new(@"<\s*br\s*/?\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BlockEnds = new(
        @"</\s*(p|div|li|ul|ol|h[1-6]|tr|table|tbody|thead|blockquote|section|article|pre|hr)\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>پایان سلول جدول شکست خط نیست؛ فقط فاصله می‌شود (ردیف‌ها از هم جدا می‌شوند).</summary>
    private static readonly Regex CellEnds = new(@"</\s*(td|th)\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>شکست‌خط متن ساده (ورودی بدون تگ) — باید حفظ شود.</summary>
    private static readonly Regex NewLines = new(@"\r\n|\r|\n", RegexOptions.Compiled);

    private static readonly Regex AnyTag = new(@"<[^>]*>", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>رشتهٔ چسبیدهٔ خیلی بلند (base64/دیتا-URI) — هیچ‌وقت بخشی از نامه نیست.</summary>
    private static readonly Regex MachineBlob = new(@"[A-Za-z0-9+/=_\-]{180,}", RegexOptions.Compiled);

    /// <summary>نشانهٔ موقت شکست خط؛ چون فضای سفیدِ سورس یک‌جا جمع می‌شود.</summary>
    private const char Break = '\u0001';

    /// <summary>
    /// HTML بدنهٔ نامه → متن چاپی. هرگز استثنا نمی‌دهد و برای ورودی خالی رشتهٔ خالی
    /// برمی‌گرداند.
    /// </summary>
    internal static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        try
        {
            // ۰) هرچیزی که سند نیست (script/style/iframe/…) یک‌بار هم با sanitizer حذف می‌شود؛
            //    این کار متنِ escape‌شده (‎&lt;p&gt;) را هم به HTML واقعی برمی‌گرداند.
            var s = RichLetterHtml.Sanitize(html);

            // متنی که هیچ تگی ندارد (مثل متن ارجاع/هامش) متن ساده است: شکست‌خط‌های خودش
            // ساختاری‌اند و باید بمانند. در سند HTML برعکس، شکست‌خط سورس فقط قالب‌بندی است.
            var plainTextInput = !AnyTag.IsMatch(html);

            // ۱) کامنت‌ها با محتوایشان — شامل <!--[if gte mso 9]> … <![endif]-->
            s = Comments.Replace(s, " ");

            // ۲) بلوک‌های غیرمتنی با محتوایشان (دو بار: ممکن است تودرتو باشند)
            for (var pass = 0; pass < 2; pass++)
            {
                var before = s;
                s = DroppedBlocks.Replace(s, " ");
                if (s == before) break;
            }
            s = DroppedOpenTags.Replace(s, " ");

            // ۳) شکست‌های ساختاری به نشانهٔ موقت
            if (plainTextInput) s = NewLines.Replace(s, Break.ToString());
            s = LineBreaks.Replace(s, Break.ToString());
            s = CellEnds.Replace(s, " ");
            s = BlockEnds.Replace(s, Break.ToString());

            // ۴) حذف بقیهٔ تگ‌ها + رمزگشایی (بار دوم: متن دوبار-encode)
            s = AnyTag.Replace(s, " ");
            s = WebUtility.HtmlDecode(s);
            if (s.IndexOf('<') >= 0) s = AnyTag.Replace(s, " ");
            s = WebUtility.HtmlDecode(s);

            // ۵) همهٔ فضای سفیدِ سورس جمع می‌شود؛ نشانهٔ شکست ساختاری دست‌نخورده می‌ماند.
            s = MachineBlob.Replace(s, " ");
            s = Regex.Replace(s, @"\s+", " ");
            s = Regex.Replace(s, @" ?\u0001[ \u0001]*", "\n");

            // ۶) خط‌ها تمیز و خط خالی‌های پشت‌سرهم جمع می‌شوند (حداکثر یکی)
            var builder = new StringBuilder(s.Length);
            var blanks = 0;
            foreach (var raw in s.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                {
                    blanks++;
                    if (blanks > 1) continue;
                }
                else
                {
                    blanks = 0;
                }

                builder.Append(line).Append('\n');
            }

            return builder.ToString().Trim();
        }
        catch
        {
            // چاپ نامه نباید به‌خاطر یک بدنهٔ عجیب متوقف شود: در بدترین حالت متن خام.
            return html!.Trim();
        }
    }
}
