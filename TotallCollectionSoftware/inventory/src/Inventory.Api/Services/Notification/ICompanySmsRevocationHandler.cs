namespace Inventory.Api.Services;

/// <summary>
/// A module with an SMS outbox registers a handler that terminally cancels only that
/// company's Pending events when its permission is revoked. Runs in the settings transaction.
/// In-flight requests must not be called unsent: they may already be at the provider.
/// </summary>
public interface ICompanySmsRevocationHandler
{
    string ModuleKey { get; }
    Task CancelPendingAsync(int companyId, DateTime nowUtc, CancellationToken ct);
}
