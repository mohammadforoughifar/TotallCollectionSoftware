namespace Inventory.Api.Licensing;

/// <summary>
/// ساعت قابل‌تثبیت برای منطق صندلی.
///
/// <para>
/// چرا لازم است؟ قاعدهٔ «۱۰ دقیقه بی‌فعالیتی» و «پنجرهٔ ۱۰ دقیقه‌ای تخریب» را فقط با
/// گذشت زمان واقعی می‌شد آزمود — یعنی هر تست باید ۱۰ دقیقه می‌خوابید (که در CI عملی
/// نیست و در عمل هم کسی اجرا نمی‌کند). با این لنگر، تست زمان را جلو می‌برد.
/// </para>
///
/// <para>
/// ⚠️ در کد تولیدی همیشه <see cref="IsFixed"/> = false است و <see cref="Now"/> همان
/// <see cref="DateTime.Now"/> را برمی‌گرداند؛ هیچ مسیر تنظیماتی برای قفل‌کردن ساعت وجود
/// ندارد (وگرنه مشتری می‌توانست ساعت را عقب نگه دارد و صندلی را بی‌نهایت بیکار کند).
/// </para>
/// </summary>
public static class ClockAnchor
{
    private static long _fixedTicks;

    /// <summary>آیا ساعت تثبیت شده است؟ (فقط در تست)</summary>
    public static bool IsFixed => Volatile.Read(ref _fixedTicks) != 0;

    /// <summary>«اکنون» — همان <see cref="DateTime.Now"/> مگر در تست.</summary>
    public static DateTime Now()
    {
        var ticks = Volatile.Read(ref _fixedTicks);
        return ticks == 0 ? DateTime.Now : new DateTime(ticks);
    }

    /// <summary>
    /// تثبیت ساعت تا زمان <c>Dispose</c>. استفاده:
    /// <code>using (ClockAnchor.Fix(ClockAnchor.Now().AddMinutes(11))) { ... }</code>
    /// </summary>
    public static IDisposable Fix(DateTime value) => new Scope(value);

    private sealed class Scope : IDisposable
    {
        private readonly long _previous;
        private int _disposed;

        public Scope(DateTime value)
        {
            _previous = Volatile.Read(ref _fixedTicks);
            Volatile.Write(ref _fixedTicks, value.Ticks == 0 ? 1 : value.Ticks);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            Volatile.Write(ref _fixedTicks, _previous);
        }
    }
}
