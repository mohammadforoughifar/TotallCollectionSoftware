namespace Inventory.Shared;

/// <summary>
/// قاعدهٔ مشترک «بازهٔ تاریخ» در همهٔ فیلترها: تاریخ شروع نباید پس از تاریخ پایان باشد.
/// <para>
/// هر صفحه‌ای که دو فیلد «از تاریخ / تا تاریخ» دارد باید پیش از اعمال فیلتر این بررسی را
/// انجام دهد؛ هم در کلاینت (پیام به کاربر) و هم در سرور (رد درخواست نامعتبر).
/// </para>
/// </summary>
public static class DateRangeFilter
{
    /// <summary>پیام یکسان خطا برای همهٔ فیلترها.</summary>
    public const string InvalidMessage = "تاریخ شروع نمی‌تواند پس از تاریخ پایان باشد؛ بازهٔ تاریخ را اصلاح کنید.";

    /// <summary>آیا بازه نامعتبر است (شروع پس از پایان)؟</summary>
    public static bool IsInvalid(DateTime? from, DateTime? to)
        => from.HasValue && to.HasValue && from.Value.Date > to.Value.Date;

    /// <summary>اعتبارسنجی با پرتاب استثنا — برای سرویس‌های سرور.</summary>
    public static void EnsureValid(DateTime? from, DateTime? to)
    {
        if (IsInvalid(from, to)) throw new InvalidOperationException(InvalidMessage);
    }
}
