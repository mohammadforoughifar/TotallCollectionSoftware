using Microsoft.Data.SqlClient;

namespace Inventory.Api.Licensing.Services;

/// <summary>نوع خطای پایگاه داده در مسیر صندلی.</summary>
public enum SeatFailureClass
{
    /// <summary>گذرا: timeout، قطع اتصال، deadlock، قفل — دوباره تلاش می‌شود.</summary>
    Transient = 0,

    /// <summary>ساختاری: جدول/ستون نیست یا دسترسی ندارد — تا رفع مشکل، شمارش ممکن نیست.</summary>
    Structural = 1
}

/// <summary>
/// تشخیص «گذرا» از «ساختاری» — بند ۲ صورت‌جلسه.
///
/// <para>
/// تفکیک عمدی است: خطای گذرا را می‌شود با پنجرهٔ ۱۰ دقیقه‌ای گذراند (کار نصب نمی‌خوابد)،
/// ولی خطای ساختاری یعنی «نمی‌توانم بشمارم»؛ در آن حالت ورود جدید رد می‌شود تا صندلی
/// بی‌حساب پخش نشود.
/// </para>
/// </summary>
public static class SeatErrors
{
    /// <summary>شماره خطاهای SQL Server که «نبودِ ساختار/دسترسی» هستند.</summary>
    private static readonly HashSet<int> StructuralSqlNumbers = new() { 207, 208, 229, 262, 2812 };

    private static readonly string[] StructuralMarkers =
    {
        "no such table",
        "no such column",
        "Invalid object name",
        "Invalid column name",
        "permission was denied",
        "The SELECT permission was denied",
        "The object",
        "Cannot find the object",
        "does not exist"
    };

    public static SeatFailureClass Classify(Exception? exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is SqlException sql && StructuralSqlNumbers.Contains(sql.Number))
                return SeatFailureClass.Structural;

            var message = e.Message ?? "";
            foreach (var marker in StructuralMarkers)
                if (message.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    return SeatFailureClass.Structural;
        }

        return SeatFailureClass.Transient;
    }

    /// <summary>آیا این خطا «بیکاری قفل/رقابت» است و تلاش دوباره معنا دارد؟</summary>
    public static bool IsTransient(Exception? exception) => Classify(exception) == SeatFailureClass.Transient;
}
