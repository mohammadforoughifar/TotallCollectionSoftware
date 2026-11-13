namespace Inventory.Api.Licensing.Infrastructure;

/// <summary>
/// خطای «صندلی» با کد ماشین‌خوان. حساب‌های مازاد (Named) و سقف پر (Concurrent) با این
/// استثنا رد می‌شوند تا کنترلر یک‌جا به پاسخ ۴۰۳ با کد تبدیلش کند.
/// </summary>
public sealed class SeatException : Exception
{
    public SeatException(string code, string message) : base(message) => Code = code;

    /// <summary>کد ماشین‌خوان — <c>LICENSE_SEAT_FULL</c>، <c>LICENSE_SEAT_BUSY</c>، …</summary>
    public string Code { get; }
}
