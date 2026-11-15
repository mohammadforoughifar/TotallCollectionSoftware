namespace Inventory.Shared;

/// <summary>
/// Only integrations actually wired to the company SMS policy belong here. Adding an
/// entry alone does not connect a module; its sender and pending-queue cancellation must
/// also enforce the matching key. The default for every company and module is disabled.
/// </summary>
public static class SmsModuleCatalog
{
    public const string ShopRepairs = "Shop.Repairs";

    public sealed record Entry(string Key, string Group, string Title);

    public static readonly IReadOnlyList<Entry> All = new[]
    {
        new Entry(ShopRepairs, "سامانه فروشگاهی", "تعمیرات")
    };

    public static bool Contains(string? key) => key is not null &&
        All.Any(entry => string.Equals(entry.Key, key, StringComparison.Ordinal));
}
