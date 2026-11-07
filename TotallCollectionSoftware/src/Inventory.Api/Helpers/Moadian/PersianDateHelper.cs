using System.Globalization;

namespace Inventory.Api.Helpers.Moadian;

/// <summary>تبدیل تاریخ میلادی به شمسی (جاری‌شدهٔ .NET PersianCalendar).</summary>
public static class PersianDateHelper
{
    private static readonly PersianCalendar Persian = new();

    /// <summary>بازگردانی سال/ماه/روز شمسی مطابق format (مثلاً "yyyy" یا "yyyy/MM/dd").</summary>
    public static string ToShamsi(DateTime date, string format = "yyyy")
    {
        var year = Persian.GetYear(date);
        var month = Persian.GetMonth(date);
        var day = Persian.GetDayOfMonth(date);
        return format
            .Replace("yyyy", year.ToString("D4"))
            .Replace("MM", month.ToString("D2"))
            .Replace("dd", day.ToString("D2"));
    }
}
