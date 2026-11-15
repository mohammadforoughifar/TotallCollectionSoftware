// نسخهٔ سبک: فقط ثابت‌های موردنیاز کنترلر آزمون.
// پیاده‌سازی اصلی (ActiveCompanyMiddleware) به لایسنس، RBAC و فیلتر شرکت وابسته است
// و در آزمون «اتصال انبوهٔ بله» نقشی ندارد.
namespace Inventory.Api.Infrastructure;

public static class ActiveCompanyMiddleware
{
    public const string ItemKey = "ActiveCompanyId";
    public const string NameItemKey = "ActiveCompanyName";
}
