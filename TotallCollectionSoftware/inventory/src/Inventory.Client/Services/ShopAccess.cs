using Inventory.Shared;

namespace Inventory.Client.Services;

/// <summary>
/// ==================== دسترسی بخش‌های سامانه فروشگاهی ====================
/// یک نقطهٔ تصمیم برای «این کاربر کدام بخش سامانه فروشگاهی را می‌بیند/می‌تواند تغییر دهد؟»
/// منوی کناری و نگهبان مسیر صفحات هر دو از همین کلاس استفاده می‌کنند تا هرگز از هم جدا نشوند.
/// <para>
/// ترتیب بررسی:
/// </para>
/// <list type="number">
///   <item>مجوز خودِ بخش: <c>Shop.{Key}.View</c> یا (برای بخش‌های عملیاتی) <c>Shop.{Key}.Manage</c></item>
///   <item>
///     سازگاری با نصب‌های موجود: مجوز «خام» ماژول قدیمی همان بخش (مثلاً <c>Orders.Read</c>)؛
///     عمداً از <see cref="IAuthState.HasDirect"/> استفاده می‌شود نه <c>Has</c>، وگرنه مجوز
///     «فروش» به‌خاطر ماژول مشترک <c>Orders</c> منوی «خرید» را هم باز می‌کرد.
///   </item>
/// </list>
/// </summary>
public static class ShopAccess
{
    /// <summary>آیا کاربر این بخش را می‌بیند؟ (مجوز بخش یا مجوز قدیمیِ همان بخش)</summary>
    public static bool CanSee(IAuthState auth, ShopSection section)
    {
        if (auth.IsAdmin) return true;
        if (auth.Has(section.ViewPermission)) return true;
        if (section.Writable && auth.Has(section.ManagePermission)) return true;

        foreach (var legacy in section.LegacyModules)
            if (ModuleAccess.CanSeeDirect(auth, legacy)) return true;

        return false;
    }

    public static bool CanSee(IAuthState auth, string key)
    {
        var section = ShopSections.ByKey(key);
        return section is not null && CanSee(auth, section);
    }

    /// <summary>آیا کاربر می‌تواند در این بخش ثبت/ویرایش/حذف کند؟</summary>
    public static bool CanManage(IAuthState auth, ShopSection section)
    {
        if (!section.Writable) return false;
        if (auth.Has(section.ManagePermission)) return true;

        // مجوز قدیمی: «ثبت» یا «ویرایش» یا «حذف» هر یک یعنی اجازهٔ عملیات در همین بخش
        foreach (var legacy in section.LegacyModules)
            if (auth.HasDirect($"{legacy}.Create") || auth.HasDirect($"{legacy}.Update") || auth.HasDirect($"{legacy}.Delete"))
                return true;

        return false;
    }

    public static bool CanManage(IAuthState auth, string key)
    {
        var section = ShopSections.ByKey(key);
        return section is not null && CanManage(auth, section);
    }

    /// <summary>آیا کاربر حداقل یکی از این بخش‌ها را می‌بیند؟ (برای نمایش/پنهان‌کردن گروه منو)</summary>
    public static bool CanSeeAny(IAuthState auth, params string[] keys) => keys.Any(k => CanSee(auth, k));

    /// <summary>همهٔ بخش‌هایی که کاربر می‌بیند — برای فهرست‌های راهنما و آزمون.</summary>
    public static IEnumerable<ShopSection> Visible(IAuthState auth) => ShopSections.All.Where(s => CanSee(auth, s));
}
