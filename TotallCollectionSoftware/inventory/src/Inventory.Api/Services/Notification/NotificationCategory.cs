using System.Linq.Expressions;
using Inventory.Api.Data;

namespace Inventory.Api.Services;

/// <summary>
/// دسته‌بندی اعلان‌های کارتابل به دو گروه «اقدامات اصلی» و «اقدامات فرعی».
/// <para>
/// <b>اصلی</b> یعنی یک <i>موردِ تازه</i> به کارتابل کاربر رسیده و منتظر نخستین اقدام اوست:
/// نامهٔ وارده/داخلی تازه، نامهٔ صادره جهت امضا یا بررسی، دستور کار تازه، درخواست IT تازه،
/// تیکت تازهٔ منابع انسانی، درخواست مرخصی/اضافه‌کاری تازه، پروژهٔ تازه در کارتابل کارشناسی،
/// صورت‌جلسهٔ تازه، و سفارش تازهٔ ویزیتور در انتظار تأیید.
/// </para>
/// <para>
/// <b>فرعی</b> یعنی همهٔ رخدادهای بعدی روی یک موردِ موجود: تأیید/رد، ارجاع، پاسخ، کامنت،
/// پیوست تازه، تمدید مهلت، بسته‌شدن، بازگشایی، ویرایش، یادآور، تغییر وضعیت فاکتور و …
/// </para>
/// <para>
/// این دسته‌بندی از <c>Title</c> اعلان استخراج می‌شود (نه ستون دیتابیس) تا اعلان‌های
/// تاریخی هم بدون مایگریشن و بدون دست‌زدن به ۴۰ نقطهٔ ارسال، درست دسته‌بندی شوند.
/// عبارت‌ها عمداً «چندکلمه‌ای و مشخص» انتخاب شده‌اند تا با عنوان‌های فرعی اشتباه نشوند؛
/// مثلاً «پاسخ جدید در تیکت» با «تیکت جدید HR» قاطی نمی‌شود. هر عنوان ناشناخته به‌طور
/// پیش‌فرض «فرعی» است (گروه امن: هیچ اقدام اصلی از دست نمی‌رود).
/// </para>
/// </summary>
public static class NotificationCategory
{
    public const string Main = "main";
    public const string Sub = "sub";

    /// <summary>عبارت‌های «مورد تازه» — هر اعلانی که یکی از این‌ها در عنوانش باشد «اصلی» است.</summary>
    public static readonly string[] MainPhrases =
    {
        // ---------- اتوماسیون اداری: نامه‌ها ----------
        "نامهٔ جدید در کارتابل",
        "نامه جدید در کارتابل",
        "نامه وارده جدید",
        "نامه داخلی جدید",
        "نامه صادره جهت امضا",
        "نامه صادره جهت بررسی",
        "صورتجلسه جدید",

        // ---------- دستور کار و پروژه ----------
        "دستور کار جدید",
        "دستور کار فوری",
        "دستور کار از تسک توسعه",
        "پروژه در کارتابل کارشناسی",
        "پروژهٔ اصلاح‌شده در کارتابل مدیر",
        "پروژه اصلاح‌شده در کارتابل مدیر",

        // ---------- درخواست‌های IT ----------
        "درخواست IT جدید",
        "درخواست IT بیرونی",

        // ---------- منابع انسانی ----------
        "تیکت جدید HR",
        "درخواست منابع انسانی جدید",
        "درخواست اضافه‌کاری جدید",
        "درخواست نیازمند تایید مدیر",
        "درخواست نیازمند تایید کارگزینی",

        // ---------- آرشیو اسناد ----------
        "درخواست دسترسی به مدرک",

        // ---------- سامانهٔ فروشگاهی (ویزیتوری) ----------
        "سفارش جدید"
    };

    /// <summary>دستهٔ یک اعلان بر اساس عنوانش (برای نمایش در پاسخ API و پیام بلادرنگ).</summary>
    public static string Classify(string? title)
    {
        var text = title ?? "";
        foreach (var phrase in MainPhrases)
            if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                return Main;
        return Sub;
    }

    /// <summary>شرط «اصلی» به شکل قابل ترجمه به SQL (OR روی LIKE) — برای فیلتر و شمارش سمت دیتابیس.</summary>
    public static Expression<Func<AppNotification, bool>> MainPredicate() => Build(negate: false);

    /// <summary>شرط «فرعی» — مکمل دقیق <see cref="MainPredicate"/>؛ مجموع دو گروه برابر کل است.</summary>
    public static Expression<Func<AppNotification, bool>> SubPredicate() => Build(negate: true);

    private static Expression<Func<AppNotification, bool>> Build(bool negate)
    {
        var parameter = Expression.Parameter(typeof(AppNotification), "n");
        var title = Expression.Property(parameter, nameof(AppNotification.Title));
        var containsMethod = typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!;

        Expression? any = null;
        foreach (var phrase in MainPhrases)
        {
            var call = Expression.Call(title, containsMethod, Expression.Constant(phrase, typeof(string)));
            any = any is null ? call : Expression.OrElse(any, call);
        }

        // Title در دیتابیس اجباری است؛ ولی برای اطمینان، عنوان خالی «فرعی» حساب می‌شود.
        var body = any ?? Expression.Constant(false);
        return Expression.Lambda<Func<AppNotification, bool>>(negate ? Expression.Not(body) : body, parameter);
    }
}
