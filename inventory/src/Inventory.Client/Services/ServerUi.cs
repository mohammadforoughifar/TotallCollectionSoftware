using System.Globalization;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

public static class ServerUi
{
    public static string StateTitle(string? state) => state switch
    {
        "Healthy" => "سالم و تازه", "Warning" => "هشدار", "Critical" => "بحرانی",
        "Disconnected" => "عدم دسترسی", "Stale" => "اطلاعات قدیمی", _ => "نامشخص / بدون بررسی"
    };
    public static string StateClass(string? state) => state switch
    {
        "Healthy" => "bg-success", "Warning" => "bg-warning text-dark", "Critical" => "bg-danger",
        "Disconnected" => "bg-dark", "Stale" => "bg-warning-subtle text-dark border", _ => "bg-secondary"
    };
    public static string HealthTitle(string? health) => health?.ToUpperInvariant() switch
    { "OK" => "سالم", "WARNING" => "هشدار", "CRITICAL" => "بحرانی", _ => "نامشخص" };
    public static string HealthClass(string? health) => health?.ToUpperInvariant() switch
    { "OK" => "bg-success", "WARNING" => "bg-warning text-dark", "CRITICAL" => "bg-danger", _ => "bg-secondary" };
    public static string ConnectionTitle(string? value) => value switch
    { "Reachable" => "آخرین بررسی: متصل", "Disconnected" => "عدم دسترسی / خطای اتصال", _ => "اتصال فعلی نامشخص" };
    public static string FreshnessTitle(string? value) => value switch
    { "Fresh" => "اطلاعات تازه", "Stale" => "نیازمند بررسی مجدد", _ => "بدون نمونهٔ ثبت‌شده" };
    public static string ComponentTitle(string? type) => type switch
    {
        "PowerSupply" => "پاور", "Fan" => "فن", "Temperature" => "دما", "Memory" => "حافظه",
        "Processor" => "پردازنده", "Storage" => "کنترلر ذخیره‌سازی", "Disk" => "دیسک", "Chassis" => "شاسی", _ => type ?? "قطعه"
    };
    public static string CategoryTitle(string? category) => category switch
    { "Connection" => "ارتباط iLO", "HardwareEvent" => "رخداد iLO", _ => "سلامت سخت‌افزار" };
    public static string LifecycleTitle(string? value) => value switch
    { "Maintenance" => "در تعمیرات", "Standby" => "رزرو", "Retired" => "بازنشسته", _ => "فعال" };
    public static string CriticalityTitle(string? value) => value switch
    { "Low" => "کم", "High" => "زیاد", "Critical" => "بحرانی", _ => "عادی" };
    public static string PowerTitle(string? value) => value?.ToUpperInvariant() switch
    { "ON" => "روشن", "OFF" => "خاموش", _ => Text(value) };
    public static string ActionTitle(string? value) => value switch
    {
        "Create" => "ثبت سرور", "Update" => "ویرایش شناسنامه", "AutoCollect" => "پایش خودکار",
        "TestConnection" => "تست اتصال", "CollectInventory" => "دریافت دستی", "AlertAcknowledge" => "تأیید هشدار",
        "AlertResolve" => "رفع هشدار", "AlertWorkOrder" => "ساخت دستورکار", _ => Text(value)
    };
    public static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
    // Dates in the existing module are API-local wall times, not UTC. Do not shift them
    // using the browser's timezone. Display an explicit local-time note in the page.
    public static string Date(DateTime? date)
    {
        if (!date.HasValue || date.Value.Year < 622) return "—";
        var d = date.Value;
        var p = new PersianCalendar();
        return $"{p.GetYear(d):0000}/{p.GetMonth(d):00}/{p.GetDayOfMonth(d):00} · {d.Hour:00}:{d.Minute:00}";
    }
}
