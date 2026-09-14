using System.Globalization;
using Inventory.Shared;
using Xunit;

namespace Inventory.WorkOrders.Tests;

public class ScheduleTests
{
    private static readonly PersianCalendar Pc = new();
    public static DateTime Fa(int y, int m, int d) => Pc.ToDateTime(y, m, d, 9, 37, 21, 123);

    [Theory]
    [InlineData(1405, 1, 28, 4)]
    [InlineData(1405, 7, 28, 3)]
    [InlineData(1405, 12, 28, 2)]
    [InlineData(1403, 12, 28, 3)]
    public void Daily_ends_at_Jalali_month_end(int year, int month, int day, int count)
    {
        var first = Fa(year, month, day);
        var dates = WorkOrderSchedule.Dates(first, 1);
        Assert.Equal(count, dates.Count);
        Assert.Equal(first, dates[0]);
        Assert.All(dates, d => { Assert.Equal(month, Pc.GetMonth(d)); Assert.Equal(first.TimeOfDay, d.TimeOfDay); });
        Assert.Equal(Pc.GetDaysInMonth(year, month), Pc.GetDayOfMonth(dates.Last()));
    }

    [Theory]
    [InlineData(1405, 1, 1)]
    [InlineData(1405, 12, 28)]
    [InlineData(1403, 12, 1)]
    public void Weekly_preserves_weekday_and_stops_at_Jalali_year_end(int y, int m, int day)
    {
        var first = Fa(y, m, day);
        var dates = WorkOrderSchedule.Dates(first, 2);
        Assert.All(dates, d => { Assert.Equal(y, Pc.GetYear(d)); Assert.Equal(first.DayOfWeek, d.DayOfWeek); Assert.Equal(first.TimeOfDay, d.TimeOfDay); });
        for (var i = 1; i < dates.Count; i++) Assert.Equal(TimeSpan.FromDays(7), dates[i] - dates[i - 1]);
        Assert.Equal(y + 1, Pc.GetYear(dates.Last().AddDays(7)));
    }

    [Theory]
    [InlineData(1405, 6, 31, 29)]
    [InlineData(1403, 6, 31, 30)]
    [InlineData(1405, 7, 15, 15)]
    public void Monthly_uses_original_Jalali_day_with_short_month_clamp(int y, int m, int day, int finalDay)
    {
        var first = Fa(y, m, day);
        var dates = WorkOrderSchedule.Dates(first, 3);
        Assert.Equal(13 - m, dates.Count);
        Assert.Equal(finalDay, Pc.GetDayOfMonth(dates.Last()));
        for (var i = 0; i < dates.Count; i++)
        {
            Assert.Equal(m + i, Pc.GetMonth(dates[i]));
            Assert.Equal(Math.Min(day, Pc.GetDaysInMonth(y, m + i)), Pc.GetDayOfMonth(dates[i]));
            Assert.Equal(first.TimeOfDay, dates[i].TimeOfDay);
        }
    }

    [Fact]
    public void None_creates_only_the_original() => Assert.Single(WorkOrderSchedule.Dates(Fa(1405, 2, 1), 0));

    [Fact]
    public void Invalid_pattern_is_rejected() => Assert.Throws<ArgumentOutOfRangeException>(() => WorkOrderSchedule.Dates(Fa(1405, 1, 1), 4));
}
