using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Inventory.Client.Extensions;

/// <summary>
/// ابزار تبدیل محتوای HTML (نامه، ایمیل، مدرک) به متن خوانا.
/// <para>
/// چرا لازم است: شرح «دستور کار» با ویرایشگر متن غنی پر می‌شود و به‌صورت HTML رندر
/// می‌شود؛ ولی متن نامه‌ها — مخصوصاً نامهٔ ساخته‌شده از ایمیل دریافتی — خودش HTML است.
/// اگر آن را با <c>HtmlEncode</c> داخل شرح بریزیم، همهٔ تگ‌ها به شکل
/// <c>&amp;lt;p&amp;gt;</c> روی صفحه ظاهر می‌شوند (همان باگ «متن نامه در دستور کار
/// به‌هم‌ریخته است»). راه درست: اول HTML را به متن ساده و خوانا تبدیل کنیم، سپس
/// متن را encode و با <c>&lt;br /&gt;</c> به HTML امن برگردانیم.
/// </para>
/// </summary>
public static class HtmlText
{
    /// <summary>تبدیل HTML به متن ساده (بدون تگ، با شکست خط طبیعی و رمزگشایی موجودیت‌ها).</summary>
    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";

        var s = html;

        // ۱) بلوک‌های بی‌مصرف در متن نهایی
        s = Regex.Replace(s, @"<(script|style|head)\b[^>]*>.*?</\1\s*>", " ",
                          RegexOptions.IgnoreCase | RegexOptions.Singleline);
        s = Regex.Replace(s, @"<!--.*?-->", " ", RegexOptions.Singleline);

        // ۲) شکست خط در محل تگ‌های بلوکی (تا پاراگراف‌ها و ردیف‌های جدول به هم نچسبند)
        s = Regex.Replace(s, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"</\s*(p|div|tr|li|ul|ol|table|tbody|thead|blockquote|h[1-6]|section|article)\s*>", "\n",
                          RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<\s*li\b[^>]*>", "• ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"</\s*(td|th)\s*>", " ", RegexOptions.IgnoreCase);

        // ۳) حذف بقیهٔ تگ‌ها
        s = Regex.Replace(s, @"<[^>]+>", " ");

        // ۴) رمزگشایی موجودیت‌ها — دوبار، چون دادهٔ قدیمی ممکن است دوبار encode شده باشد
        s = WebUtility.HtmlDecode(s);
        s = WebUtility.HtmlDecode(s);

        // ۵) تمیزکاری فاصله‌ها و خطوط خالی تکراری
        s = s.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = s.Split('\n')
                     .Select(l => Regex.Replace(l, @"[ \t\u00A0]+", " ").Trim())
                     .ToList();

        var sb = new StringBuilder();
        var emptyRun = 0;
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                emptyRun++;
                if (emptyRun > 1) continue;   // بیش از یک خط خالی پشت‌سرهم لازم نیست
            }
            else emptyRun = 0;

            sb.AppendLine(line);
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// متن خوانای یک محتوای HTML، به‌صورت HTML امن (پاراگراف + شکست خط) — مناسب ریختن
    /// در ویرایشگر شرح دستور کار یا هر فیلد غنی دیگر.
    /// </summary>
    public static string ToSafeHtml(string? html)
    {
        var text = ToPlainText(html);
        if (text.Length == 0) return "";
        return "<p>" + WebUtility.HtmlEncode(text).Replace("\n", "<br />") + "</p>";
    }
}
