using Inventory.Client.Extensions;
using Xunit;

namespace Inventory.Compatibility.Tests;

/// <summary>
/// تست ابزار کنتراست رنگ (WCAG) — مطمئن می‌شود متن برچسب‌ها/بج‌ها روی هر رنگی که
/// کاربر انتخاب می‌کند خوانا می‌ماند (حداقل نسبت کنتراست ۴.۵ برای متن معمولی).
/// </summary>
public sealed class ColorContrastTests
{
    /// <summary>رنگ‌های پیش‌فرض برچسب در DocTagsModal.</summary>
    static readonly string[] TagPresetColors =
    {
        "#4f46e5", "#059669", "#dc2626", "#d97706", "#0284c7",
        "#7c3aed", "#db2777", "#475569", "#0891b2", "#65a30d"
    };

    /// <summary>رنگ‌های روشن/میانه‌ای که متن سفید روی آن‌ها خوانا نیست.</summary>
    static readonly string[] UnreadableWithWhite = { "#059669", "#d97706", "#0284c7", "#0891b2", "#65a30d" };

    [Fact]
    public void Black_and_white_are_21_to_1()
    {
        Assert.Equal(21, ColorContrast.ContrastRatio("#ffffff", "#000000"), 0);
    }

    [Theory]
    // رنگ‌های تیره → متن سفید
    [InlineData("#4f46e5", ColorContrast.OnDark)]   // نیلی
    [InlineData("#475569", ColorContrast.OnDark)]   // طوسی تیره
    [InlineData("#dc2626", ColorContrast.OnDark)]   // قرمز
    [InlineData("#7c3aed", ColorContrast.OnDark)]   // بنفش
    [InlineData("#db2777", ColorContrast.OnDark)]   // سرخابی
    // رنگ‌های روشن/میانه → متن جوهری تیره (سفید روی این‌ها خوانا نبود)
    [InlineData("#65a30d", ColorContrast.OnLight)]  // سبز زیتونی
    [InlineData("#d97706", ColorContrast.OnLight)]  // کهربایی
    [InlineData("#0891b2", ColorContrast.OnLight)]  // فیروزه‌ای
    [InlineData("#059669", ColorContrast.OnLight)]  // سبز
    [InlineData("#0284c7", ColorContrast.OnLight)]  // آبی میانه
    [InlineData("#fde047", ColorContrast.OnLight)]  // زرد روشن
    [InlineData("#ffffff", ColorContrast.OnLight)]  // سفید
    public void Readable_text_is_chosen_per_background(string background, string expected)
    {
        Assert.Equal(expected, background.ReadableTextOn());
    }

    [Theory]
    [InlineData("#4f46e5")]
    [InlineData("#059669")]
    [InlineData("#dc2626")]
    [InlineData("#d97706")]
    [InlineData("#0284c7")]
    [InlineData("#7c3aed")]
    [InlineData("#db2777")]
    [InlineData("#475569")]
    [InlineData("#0891b2")]
    [InlineData("#65a30d")]
    [InlineData("#fde047")]   // زرد روشن (انتخاب کاربر با input type=color)
    [InlineData("#f8fafc")]   // تقریباً سفید
    [InlineData("#000000")]
    public void Every_tag_color_gets_a_WCAG_AA_pair(string background)
    {
        var pair = background.ReadablePairOn();
        var ratio = ColorContrast.ContrastRatio(pair.Background, pair.Text);

        Assert.True(ratio >= ColorContrast.MinRatio,
            $"{background} → پس‌زمینه {pair.Background} با متن {pair.Text} کنتراست {ratio:F2} دارد (باید ≥ ۴.۵ باشد)");
        Assert.Equal(pair.Ratio, ratio, 1);
    }

    [Fact]
    public void Preset_tag_colors_that_already_work_keep_their_exact_color()
    {
        foreach (var color in TagPresetColors.Except(UnreadableWithWhite))
        {
            var pair = color.ReadablePairOn();
            Assert.Equal(color, pair.Background);   // رنگ کاربر دست‌نخورده می‌ماند
            Assert.Equal(ColorContrast.OnDark, pair.Text);
        }
    }

    [Fact]
    public void Midtone_colors_are_only_nudged_enough_to_pass()
    {
        // آبی میانه: با سفید ۴.۱۰ و با جوهری ~۴.۴ است → باید کمی تنظیم شود
        var pair = "#0284c7".ReadablePairOn();

        Assert.True(pair.Ratio >= ColorContrast.MinRatio);
        Assert.True(ColorContrast.TryParseColor(pair.Background, out var adjusted));
        Assert.True(ColorContrast.TryParseColor("#0284c7", out var original));

        // تغییر حداقلی: هر کانال حداکثر ۶۰ واحد جابه‌جا شود (رنگ همان آبی می‌ماند)
        Assert.True(Math.Abs(adjusted.R - original.R) <= 60, $"R: {original.R} → {adjusted.R}");
        Assert.True(Math.Abs(adjusted.G - original.G) <= 60, $"G: {original.G} → {adjusted.G}");
        Assert.True(Math.Abs(adjusted.B - original.B) <= 60, $"B: {original.B} → {adjusted.B}");
    }

    [Fact]
    public void Preset_tag_colors_never_need_plain_white_text()
    {
        // اگر روزی کسی متن برچسب را دوباره «سفید ثابت» کند، این تست نشان می‌دهد کدام رنگ‌ها ناخوانا می‌شوند.
        Assert.Equal(UnreadableWithWhite, TagPresetColors.Where(c => !c.WhiteIsReadableOn()).ToArray());
    }

    [Fact]
    public void Badge_style_is_ready_to_drop_into_an_inline_style_attribute()
    {
        Assert.Equal("background-color:#4f46e5;color:#ffffff", "#4f46e5".ReadableBadgeStyle());
        Assert.StartsWith("background-color:#", "#fde047".ReadableBadgeStyle());
        Assert.Contains("color:" + ColorContrast.OnLight, "#fde047".ReadableBadgeStyle());
    }

    [Theory]
    [InlineData("#abc", true)]
    [InlineData("#aabbcc", true)]
    [InlineData("#aabbccdd", true)]
    [InlineData("rgb(12, 34, 56)", true)]
    [InlineData("rgba(12, 34, 56, .5)", true)]
    [InlineData("not-a-color", false)]
    [InlineData("#gggggg", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Color_parsing(string? value, bool expected)
    {
        Assert.Equal(expected, ColorContrast.TryParseColor(value, out _));
    }

    [Fact]
    public void Unknown_background_falls_back_to_dark_text()
    {
        Assert.Equal(ColorContrast.OnLight, "transparent".ReadableTextOn());
        Assert.Equal(ColorContrast.OnLight, ((string?)null).ReadableTextOn());
        Assert.Equal(ColorContrast.OnLight, "transparent".ReadablePairOn().Text);
    }

    [Theory]
    // رنگ‌هایی که در CSS آرشیو اسناد اصلاح شدند — همه باید روی سفید ≥ ۴.۵ باشند
    [InlineData("#64748b")]   // متن فرعی / آیکون‌ها
    [InlineData("#5f6c80")]   // شمارش نتایج و سطح دسترسی
    [InlineData("#5b6a80")]   // سرستون جدول
    [InlineData("#55637a")]   // badge-soft-muted
    [InlineData("#0369a1")]   // دکمه/آیکون آبی
    [InlineData("#b45309")]   // گرادیان هدر کارتابل
    [InlineData("#4338ca")]   // گرادیان هدر نمای مدرک
    public void Fixed_archive_colors_pass_AA(string color)
    {
        Assert.True(ColorContrast.ContrastRatio(color, "#ffffff") >= 4.5,
            $"{color} روی سفید کنتراست کافی ندارد");
    }
}
