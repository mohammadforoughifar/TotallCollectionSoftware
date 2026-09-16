using System.Text.RegularExpressions;
using Xunit;

namespace Inventory.Compatibility.Tests;

/// <summary>
/// قرارداد رابط کاربری سامانه: مودال/فرم فقط با «دکمهٔ ×» یا دکمه‌های «بستن»/«انصراف» بسته می‌شود.
/// کلیک روی پس‌زمینهٔ مودال (backdrop / overlay / form-zir) نباید آن را ببندد، چون دادهٔ تایپ‌شدهٔ
/// کاربر بی‌خبر از بین می‌رود. این تست سورس همهٔ کامپوننت‌های Blazor را اسکن می‌کند و اگر جایی
/// لایهٔ پس‌زمینهٔ مودال هندلر @onclick داشت، بیلد/تست را قرمز می‌کند تا ایراد دوباره برنگردد.
/// </summary>
public sealed class ModalDismissTests
{
    static string ClientRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..", "inventory/src/Inventory.Client"));

    /// <summary>کلاس‌های لایهٔ پس‌زمینهٔ مودال/شیت (توکن کامل کلاس).</summary>
    static readonly string[] OverlayTokens =
    {
        "modal",             // <div class="modal fade show d-block"> — قالب بوت‌استرپ
        "modal-backdrop",
        "gov",               // مودال‌های ماژول نامه‌ها (letters.css)
        "ov",                // شیت‌های ماژول نامه‌ها و ایمیل (letters.css)
        "fpv-backdrop",      // پیش‌نمایش فایل
        "chat-modal-backdrop",
        "wo-dialog-backdrop",
        "da-backdrop",
    };

    /// <summary>
    /// لایه‌هایی که «مودال» نیستند و بستنشان با کلیک بیرون رفتار درست است:
    /// دراپ‌داون، تقویم، منوی راست‌کلیک، سایدبار موبایل و پاپ‌اور ابزارها.
    /// </summary>
    static readonly string[] NonModalTokens =
    {
        "pop-overlay", "dfi-overlay", "menu-overlay", "sidebar-backdrop",
        "da-tools-backdrop", "modal-dialog", "modal-content",
    };

    static readonly Regex TagRegex = new("<[A-Za-z][\\w.\\-]*(?:[^>\"']|\"[^\"]*\"|'[^']*')*>", RegexOptions.Singleline);
    static readonly Regex ClassRegex = new("class=\"([^\"]*)\"", RegexOptions.IgnoreCase);
    static readonly Regex StyleRegex = new("style=\"([^\"]*)\"", RegexOptions.IgnoreCase);
    static readonly Regex OnClickRegex = new("@onclick\\s*=", RegexOptions.IgnoreCase);

    [Fact]
    public void Modal_backdrop_never_closes_the_dialog_on_outside_click()
    {
        var root = ClientRoot;
        Assert.True(Directory.Exists(root), $"مسیر کلاینت پیدا نشد: {root}");

        var violations = new List<string>();
        var scanned = 0;

        foreach (var file in Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            scanned++;
            var text = File.ReadAllText(file);

            foreach (Match m in TagRegex.Matches(text))
            {
                var tag = m.Value;
                if (!OnClickRegex.IsMatch(tag)) continue;

                var classes = (ClassRegex.Match(tag).Groups[1].Value ?? "")
                    .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (classes.Any(c => NonModalTokens.Contains(c))) continue;

                var style = StyleRegex.Match(tag).Groups[1].Value ?? "";
                var inlineFullscreen =
                    Regex.IsMatch(style, "position\\s*:\\s*fixed", RegexOptions.IgnoreCase) &&
                    (Regex.IsMatch(style, "inset\\s*:\\s*0", RegexOptions.IgnoreCase) ||
                     (Regex.IsMatch(style, "top\\s*:\\s*0", RegexOptions.IgnoreCase) &&
                      Regex.IsMatch(style, "left\\s*:\\s*0", RegexOptions.IgnoreCase)));

                var isModalOverlay = classes.Any(c => OverlayTokens.Contains(c)) || inlineFullscreen;
                if (!isModalOverlay) continue;

                var line = text.Substring(0, m.Index).Count(c => c == '\n') + 1;
                violations.Add($"{Path.GetRelativePath(root, file)}:{line} → {Regex.Replace(tag, "\\s+", " ").Trim()}");
            }
        }

        Assert.True(scanned > 100, $"تعداد فایل‌های اسکن‌شده غیرمنتظره است: {scanned}");
        Assert.True(violations.Count == 0,
            "مودال‌ها نباید با کلیک روی پس‌زمینه بسته شوند (فقط دکمهٔ × / «بستن» / «انصراف»)." +
            Environment.NewLine + "موارد ناقض:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Shared_modal_component_has_no_backdrop_click_handler()
    {
        var path = Path.Combine(ClientRoot, "Shared", "Modal.razor");
        Assert.True(File.Exists(path), $"کامپوننت مشترک مودال پیدا نشد: {path}");

        var text = File.ReadAllText(path);
        var backdrop = Regex.Match(text, "<div[^>]*class=\"modal fade[^\"]*\"[^>]*>", RegexOptions.Singleline);

        Assert.True(backdrop.Success, "لایهٔ پس‌زمینهٔ مودال مشترک پیدا نشد.");
        Assert.DoesNotContain("@onclick", backdrop.Value);
        // خودِ مودال باید دکمهٔ بستن (×) داشته باشد تا کاربر راه خروج داشته باشد.
        Assert.Contains("btn-close", text);
    }
}
