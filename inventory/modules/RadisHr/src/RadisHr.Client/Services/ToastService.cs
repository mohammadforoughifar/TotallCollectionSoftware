namespace RadisHr.Client.Services;

public record Toast(Guid Id, string Message, string Kind);

/// <summary>اعلان‌های شناور — جایگزین alert() نسخهٔ اصلی</summary>
public class ToastService
{
    private readonly List<Toast> _items = new();
    public IReadOnlyList<Toast> Items => _items;
    public event Action? Changed;

    public void Success(string message) => Add(message, "success");
    public void Error(string message) => Add(message, "error");
    public void Info(string message) => Add(message, "info");

    private void Add(string message, string kind)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var toast = new Toast(Guid.NewGuid(), message, kind);
        _items.Add(toast);
        Changed?.Invoke();
        _ = RemoveLaterAsync(toast);
    }

    private async Task RemoveLaterAsync(Toast toast)
    {
        await Task.Delay(5000);
        _items.Remove(toast);
        Changed?.Invoke();
    }

    public void Dismiss(Toast toast)
    {
        _items.Remove(toast);
        Changed?.Invoke();
    }
}
