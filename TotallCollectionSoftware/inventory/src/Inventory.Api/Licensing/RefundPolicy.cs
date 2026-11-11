namespace Inventory.Api.Licensing;

/// <summary>
/// سیاست «بازگشت دستی سهمیه» — محدودیت‌هایی که جلوی سوءاستفاده و خطای انسانی را می‌گیرد.
///
/// <para>
/// <b>چرا محدودیت لازم است؟</b> «بازگشت دستی» تنها راهی است که یک انسان می‌تواند مصرف را
/// کم کند. بدون سقف، دو خطر واقعی وجود دارد:
/// </para>
/// <list type="number">
///   <item><b>سوءاستفاده:</b> کاربری که به صفحهٔ رزروها دسترسی دارد می‌تواند به‌تدریج
///   سهمیه را «آزاد» کند و سقف خریداری‌شده را دور بزند.</item>
///   <item><b>خطای انسانی:</b> یک کلیک اشتباه روی «Refund» سهمیهٔ مصرف‌شدهٔ واقعی را
///   برمی‌گرداند و گزارش مصرف با واقعیت نمی‌خواند.</item>
/// </list>
///
/// <para>
/// <b>پنج قید (همه با هم):</b>
/// </para>
/// <list type="number">
///   <item>سقف تعداد در هر دورهٔ سهمیه: «۲٪ سقف» یا «۵ مورد» — <b>هرکدام کمتر</b>
///   (و دست‌کم ۱، تا سقف‌های کوچک کاملاً بسته نشوند).</item>
///   <item>رزرو باید <b>قدیمی‌تر از X ساعت</b> باشد — جلوی «رزرو کن، فوراً بازگردان» را
///   می‌گیرد که عملاً یعنی ارسال مجانی.</item>
///   <item><b>یادداشت اجباری</b> — تصمیم انسانی باید دلیل داشته باشد و در ممیزی بماند.</item>
///   <item>رخداد با شدت <c>Warning</c> — در گزارش‌های پایش برجسته می‌شود.</item>
///   <item>تعدادها در <c>/api/license/status</c> نمایش داده می‌شوند تا مدیر بداند
///   چند مورد «در انتظار» و چند «بازگشت دستی» داشته است.</item>
/// </list>
///
/// <para>
/// ⚠️ <b>چرا این‌ها از appsettings خوانده می‌شوند ولی سقف سهمیه نه؟</b> این‌ها سقف
/// سهمیه <b>نیستند</b>؛ سیاست عملیاتی پشتیبانی‌اند. اما برای اینکه با تغییر یک فایل
/// نشود محدودیت را کامل برداشت، هر مقدار در بازهٔ مجاز <b>کلمپ</b> می‌شود:
/// درصد ۰..۵۰ و تعداد ۱..۵۰ و ساعت ۰..۳۳۶. مقدار صفر یعنی «همان پیش‌فرض».
/// </para>
/// </summary>
public sealed class RefundPolicy
{
    /// <summary>پیش‌فرض درصد سقف (از سقف دوره).</summary>
    public const int DefaultPercentOfLimit = 2;

    /// <summary>پیش‌فرض تعداد حداکثر در هر دوره.</summary>
    public const int DefaultMaxCount = 5;

    /// <summary>پیش‌فرض حداقل عمر رزرو (ساعت) برای اجازهٔ بازگشت دستی.</summary>
    public const int DefaultMinAgeHours = 24;

    /// <summary>
    /// بازهٔ بررسی «همین دوره» برای شمارش بازگشت‌ها.
    /// برای دورهٔ دائمی، «دوره» یعنی کل تاریخچه (کلید <c>-</c>).
    /// </summary>
    public RefundPolicy(int percentOfLimit, int maxCount, int minAgeHours)
    {
        PercentOfLimit = Math.Clamp(percentOfLimit, 0, 50);
        MaxCount = Math.Clamp(maxCount, 1, 50);

        // صفر و منفی یعنی «پیش‌فرض» — نه «بدون محدودیت سنی». اگر کسی بخواهد
        // محدودیت سنی را بردارد، باید صریحاً بگوید؛ سکوت نباید مسیر میان‌بر بسازد.
        MinAgeHours = minAgeHours <= 0
            ? DefaultMinAgeHours
            : Math.Clamp(minAgeHours, 1, 336);
    }

    /// <summary>درصد سقف دوره که حداکثر می‌تواند دستی بازگردانده شود (پیش‌فرض ۲).</summary>
    public int PercentOfLimit { get; }

    /// <summary>حداکثر تعداد بازگشت دستی در هر دوره (پیش‌فرض ۵).</summary>
    public int MaxCount { get; }

    /// <summary>حداقل عمر رزرو برای اجازهٔ بازگشت دستی (ساعت).</summary>
    public int MinAgeHours { get; }

    /// <summary>خواندن از <c>Licensing:ManualRefund:*</c> با کلمپ.</summary>
    public static RefundPolicy FromConfiguration(IConfiguration configuration)
        => new(
            configuration.GetValue("Licensing:ManualRefund:MaxPercentOfLimit", DefaultPercentOfLimit),
            configuration.GetValue("Licensing:ManualRefund:MaxCountPerPeriod", DefaultMaxCount),
            configuration.GetValue("Licensing:ManualRefund:MinReservationAgeHours", DefaultMinAgeHours));

    /// <summary>
    /// سقف تعداد مجاز برای یک دوره با سقف مشخص:
    /// <c>min(ceil(limit × percent / 100), maxCount)</c> و دست‌کم ۱.
    ///
    /// <para>
    /// <b>چرا <c>ceil</c> و نه <c>floor</c>؟</b> با سقف ۱۰۰ و ۲٪، هر دو یکی می‌شوند؛
    /// ولی با سقف ۳۰ و ۲٪، <c>floor</c> صفر می‌دهد یعنی «هیچ بازگشتی مجاز نیست» — که
    /// برای یک خطای فنی واقعی، مشتری را جریمه می‌کند. سقف ۱ حداقل انصاف است.
    /// </para>
    /// </summary>
    public int MaxRefundsFor(long periodLimit)
    {
        if (periodLimit <= 0) return 1;                     // سقف بی‌نهایت/نامعلوم ⇒ سخت‌گیرانه‌ترین
        var byPercent = (int)Math.Ceiling(periodLimit * (decimal)PercentOfLimit / 100m);
        return Math.Max(1, Math.Min(byPercent, MaxCount));
    }

    /// <summary>آیا این رزرو به‌قدر کافی قدیمی است؟</summary>
    public bool IsOldEnough(DateTime reservedAt, DateTime nowIran)
        => (nowIran - reservedAt) >= TimeSpan.FromHours(MinAgeHours);

    /// <summary>توصیف کوتاه سیاست — برای نمایش در <c>/api/license/status</c> و پیام خطا.</summary>
    public string Describe(long periodLimit)
        => $"حداکثر {MaxRefundsFor(periodLimit)} بازگشت دستی در هر دوره " +
           $"({PercentOfLimit}٪ سقف یا {MaxCount} مورد، هرکدام کمتر)، " +
           $"فقط برای رزروهای قدیمی‌تر از {MinAgeHours} ساعت.";
}
