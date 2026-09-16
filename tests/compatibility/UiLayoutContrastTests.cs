using System.Text.RegularExpressions;
using Inventory.Client.Extensions;
using Xunit;

namespace Inventory.Compatibility.Tests;

/// <summary>
/// قرارداد چیدمان و خوانایی رابط کاربری:
/// ۱) مودال بلند باید «بدنهٔ اسکرول‌شونده» داشته باشد و سربرگ/فوتر همیشه دیده شوند،
/// ۲) چک‌باکس/سوئیچ در حالت راست‌به‌چپ باید با flex چیده شود (float بوت‌استرپ در RTL جابه‌جا می‌شود)،
/// ۳) هیچ فرمی ستونِ بدون breakpoint (col-6 / col-4 …) نداشته باشد تا در موبایل نشکند،
/// ۴) کنتراست رنگ متن‌ها حداقل WCAG AA باشد (۴.۵ برای متن، ۳ برای آیکون).
/// اگر روزی این قواعد شکسته شود، همین‌جا تست قرمز می‌شود تا ایراد به کاربر نرسد.
/// </summary>
public sealed class UiLayoutContrastTests
{
    const double MinText = 4.5;
    const double MinIcon = 3.0;

    static string ClientRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..", "inventory/src/Inventory.Client"));

    static string Read(string relative) => File.ReadAllText(Path.Combine(ClientRoot, relative));

    static IEnumerable<string> DocArchivePages() =>
        Directory.GetFiles(Path.Combine(ClientRoot, "Pages", "DocArchive"), "*.razor");

    static IEnumerable<string> AllPages() =>
        Directory.GetFiles(Path.Combine(ClientRoot, "Pages"), "*.razor", SearchOption.AllDirectories);

    // ---------------------------------------------------------------- ۱) مودال

    [Fact]
    public void Tall_modal_body_scrolls_while_header_and_footer_stay_visible()
    {
        var css = Read("wwwroot/css/app.css");

        Assert.Matches(new Regex(@"\.modal-content\s*\{[^}]*max-height:\s*calc\(100vh"), css);
        Assert.Matches(new Regex(@"\.modal-body\s*\{[^}]*overflow-y:\s*auto"), css);
        Assert.Matches(new Regex(@"\.modal-header\s*,\s*\.modal-footer\s*\{[^}]*flex:\s*0 0 auto"), css);
    }

    [Fact]
    public void Mobile_modal_height_is_redeclared_inside_the_media_query()
    {
        var css = Read("wwwroot/css/app.css");
        var mobile = Regex.Match(css, @"@media\s*\(max-width:\s*575\.98px\)\s*\{(?<body>(?:[^{}]|\{[^{}]*\})*)\}");

        Assert.True(mobile.Success, "بلوک @media موبایل در app.css پیدا نشد");
        Assert.Matches(new Regex(@"\.modal-content\s*\{[^}]*max-height:\s*92vh"), mobile.Groups["body"].Value);
    }

    [Fact]
    public void Legacy_absolute_menus_inside_modals_keep_visible_overflow()
    {
        // منوهای قدیمی position:absolute (کمبوی چندانتخابی، ماشین‌های اداری، گفتگوی دستور کار)
        // اگر بدنهٔ مودال اسکرول بگیرد بریده می‌شوند؛ پس باید قاعدهٔ استثنا سر جایش بماند.
        var css = Read("wwwroot/css/app.css");

        foreach (var menu in new[] { "ms-combo-menu", "om-combo-menu", "wo-chat-menu" })
            Assert.Contains($":has(.{menu})", css);
    }

    // ---------------------------------------------------------------- ۲) چک‌باکس در RTL

    [Fact]
    public void Rtl_form_check_uses_flex_instead_of_bootstrap_float()
    {
        var css = Read("wwwroot/css/app.css");

        Assert.Matches(new Regex(@"\.form-check\s*\{[^}]*display:\s*flex"), css);
        // سلکتور به‌شکل گروهی نوشته شده: «.form-check .form-check-input, .form-check.form-switch …»
        Assert.Matches(new Regex(@"\.form-check[^{}]*\.form-check-input[^{}]*\{[^}]*float:\s*none", RegexOptions.Singleline), css);
        Assert.Matches(new Regex(@"\.form-check\.form-check-inline\s*\{[^}]*display:\s*inline-flex"), css);
    }

    [Fact]
    public void Permission_editor_lays_out_by_container_width_not_viewport()
    {
        // این کامپوننت هم در ستون کناریِ باریک و هم در مودال عریض استفاده می‌شود.
        var css = Read("wwwroot/css/app.css");
        Assert.Matches(new Regex(@"\.doc-perm-editor\s*>\s*\.row\s*>\s*\[class\*=""col-""\][^}]*flex:\s*1 1"), css);
    }

    // ---------------------------------------------------------------- ۳) گرید ریسپانسیو

    [Fact]
    public void No_form_uses_a_fixed_column_without_a_breakpoint()
    {
        // col-6 / col-4 و… در موبایل همان نسبت را نگه می‌دارند و فیلدها آن‌قدر باریک
        // می‌شوند که برچسب و ورودی روی هم می‌ریزند. باید col-12 col-sm-N نوشته شود.
        var attr = new Regex("class=\"([^\"]*)\"");
        var fixedCol = new Regex(@"^col-(\d+)$");
        var responsive = new Regex(@"^col-(sm|md|lg|xl|xxl)(-\d+)?$");
        var offenders = new List<string>();

        foreach (var file in AllPages())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match m in attr.Matches(lines[i]))
                {
                    var tokens = m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var hasFixed = tokens.Any(t => fixedCol.IsMatch(t) && t != "col-12");
                    var hasResponsive = tokens.Any(t => responsive.IsMatch(t));
                    if (hasFixed && !hasResponsive)
                        offenders.Add($"{Path.GetFileName(file)}:{i + 1} → {m.Groups[1].Value}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "ستون‌های بدون breakpoint پیدا شد (در موبایل می‌شکنند):" + Environment.NewLine + string.Join(Environment.NewLine, offenders.Take(20)));
    }

    // ---------------------------------------------------------------- ۴) کنتراست رنگ

    [Fact]
    public void Tag_badges_never_use_fixed_white_text_on_a_user_chosen_color()
    {
        var offenders = new List<string>();

        foreach (var file in DocArchivePages())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!line.Contains("@t.Color")) continue;
                if (line.Contains("ReadableBadgeStyle")) continue;
                if (line.Contains("text-white") || Regex.IsMatch(line, @"color:\s*#fff"))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
            }
        }

        Assert.True(offenders.Count == 0,
            "بج/چیپ برچسب با متن سفیدِ ثابت روی رنگ انتخابی کاربر (باید ReadableBadgeStyle باشد): " +
            string.Join(", ", offenders));
    }

    [Theory]
    [InlineData("#0284c7")]  // آبی میانه — با هیچ متنی به ۴.۵ نمی‌رسید
    [InlineData("#059669")]
    [InlineData("#d97706")]
    [InlineData("#0891b2")]
    [InlineData("#65a30d")]
    public void Every_dynamic_tag_color_gets_a_readable_pair(string tagColor)
    {
        var pair = tagColor.ReadablePairOn();
        Assert.True(ColorContrast.ContrastRatio(pair.Background, pair.Text) >= MinText);
    }

    [Fact]
    public void Archive_css_text_colors_meet_WCAG_AA()
    {
        var offenders = new List<string>();
        var rule = new Regex(@"([^{}]+)\{([^{}]*)\}", RegexOptions.Singleline);
        var color = new Regex(@"(?<![-\w])color\s*:\s*(#[0-9a-fA-F]{3,8})");
        var background = new Regex(@"background(?:-color)?\s*:\s*([^;}]+)");
        var hex = new Regex("#[0-9a-fA-F]{3,8}");
        var iconish = new Regex(@"\.bi\b|::marker|btn-close|-ico\b|clear\b");

        // فقط قواعدی که خودشان هم رنگ متن و هم پس‌زمینه دارند (بقیه از والد ارث می‌برند)
        var targets = new (string Path, string? Only)[]
        {
            ("Pages/DocArchive/DocArchiveExplorer.razor.css", null),
            ("Pages/DocArchive/DocumentFormModal.razor.css", null),
            ("wwwroot/css/app.css", @"doc-archive|\.da-|\.m-card|\.badge-soft|\.wo-archive"),
        };

        foreach (var (relative, only) in targets)
        {
            var css = Read(relative);
            foreach (Match m in rule.Matches(css))
            {
                var selector = Regex.Replace(m.Groups[1].Value, @"\s+", " ").Trim();
                if (only is not null && !Regex.IsMatch(selector, only)) continue;

                var fg = color.Match(m.Groups[2].Value);
                if (!fg.Success || !ColorContrast.TryParseColor(fg.Groups[1].Value, out var fgRgb)) continue;

                var stops = background.Matches(m.Groups[2].Value)
                    .SelectMany(b => hex.Matches(b.Groups[1].Value).Select(h => h.Value))
                    .Where(s => ColorContrast.TryParseColor(s, out _))
                    .ToList();

                // گرادیان: بدترین رنگِ توقف مبنا است؛ اگر قاعده پس‌زمینه نداشت، سفید فرض می‌شود
                var worst = "#ffffff";
                var worstRatio = ColorContrast.ContrastRatio(fg.Groups[1].Value, worst);
                if (stops.Count > 0)
                {
                    worstRatio = double.MaxValue;
                    foreach (var stop in stops)
                    {
                        var r = ColorContrast.ContrastRatio(fg.Groups[1].Value, stop);
                        if (r < worstRatio) { worstRatio = r; worst = stop; }
                    }
                }

                var min = iconish.IsMatch(selector) ? MinIcon : MinText;
                if (worstRatio < min)
                    offenders.Add($"{relative} → {selector}: {fg.Groups[1].Value} روی {worst} = {worstRatio:F2} (نیاز {min})");
            }
        }

        Assert.True(offenders.Count == 0,
            "کنتراست کمتر از WCAG AA:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void Gradient_headers_with_white_text_meet_WCAG_AA()
    {
        var offenders = new List<string>();
        var style = new Regex("style=\"([^\"]*)\"");
        var hex = new Regex("#[0-9a-fA-F]{6}");

        foreach (var file in DocArchivePages())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match m in style.Matches(lines[i]))
                {
                    var value = m.Groups[1].Value.Replace(" ", "");
                    if (!value.Contains("color:#fff")) continue;

                    foreach (var stop in hex.Matches(m.Groups[1].Value).Select(h => h.Value))
                    {
                        var r = ColorContrast.ContrastRatio("#ffffff", stop);
                        if (r < MinText)
                            offenders.Add($"{Path.GetFileName(file)}:{i + 1} → متن سفید روی {stop} = {r:F2}");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "هدر گرادیانی با متن سفیدِ ناخوانا:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }
}
