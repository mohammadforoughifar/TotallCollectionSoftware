namespace Inventory.Api.Licensing;

/// <summary>دلیل رد شدن تسویهٔ دستی — تا API کد خطای دقیق بدهد، نه «false».</summary>
public enum SettleFailure
{
    None = 0,
    NotFound,
    AlreadySettled,
    NoteRequired,
    TooRecent,
    RefundLimitReached
}

/// <summary>نتیجهٔ تسویهٔ دستی رزرو.</summary>
public sealed record SettleReservationResult(
    bool Success,
    long ReservationId,
    bool Confirmed,
    bool RefundApplied,
    SettleFailure Failure,
    string? Message)
{
    public static SettleReservationResult Ok(long id, bool confirmed, bool refunded)
        => new(true, id, confirmed, refunded, SettleFailure.None, null);

    public static SettleReservationResult Fail(SettleFailure failure, string message)
        => new(false, 0, false, false, failure, message);
}

/// <summary>خلاصهٔ رزروها برای <c>/api/license/status</c>.</summary>
public sealed record ReservationSummary(
    int Pending,
    int Reserved,
    int Confirmed,
    int Refunded,
    int ManualRefunds,
    object ManualRefundPolicy);
