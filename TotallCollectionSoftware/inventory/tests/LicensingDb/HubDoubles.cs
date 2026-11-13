using Microsoft.AspNetCore.SignalR;

namespace HubTestDoubles;

// ⚠️ چرا این فایل جدا است و کلاس‌ها «مخفی» نیستند؟
//
// HubLicenseFilter تصمیمش را بر پایهٔ GetType().Name می‌گیرد. انواع file-local در C#
// نامِ زمان‌اجرای دست‌کاری‌شده دارند (پیشوند تولیدشده) و GetType().Name دیگر برابر
// «DashboardHub» نیست ⇒ همهٔ هاب‌ها «نگاشت‌نشده» دیده می‌شوند و تست معنایش را از دست
// می‌دهد. پس این دوقلوها در یک فضای‌نام جدا و با نامِ دقیقاً یکسان تعریف شده‌اند؛
// namespace جدا مانع تعارض با کلاس‌های واقعی Inventory.Api.Hubs.* می‌شود.

/// <summary>دوقلوی DashboardHub — فقط برای تست فیلتر لایسنس.</summary>
public sealed class DashboardHub : Hub
{
    public Task RequestHardwareCheck() => Task.CompletedTask;
}

/// <summary>دوقلوی NotifyHub.</summary>
public sealed class NotifyHub : Hub
{
    public Task SendAsync(int userId) => Task.CompletedTask;
}

/// <summary>دوقلوی ChatHub.</summary>
public sealed class ChatHub : Hub
{
    public Task SendTyping(int conversationId) => Task.CompletedTask;

    public Task NotifyMessage(int id) => Task.CompletedTask;
}

/// <summary>دوقلوی RemoteHub.</summary>
public sealed class RemoteHub : Hub
{
    public Task SendInput(string sessionId, string input) => Task.CompletedTask;
}

/// <summary>دوقلوی RemoteAgentHub.</summary>
public sealed class RemoteAgentHub : Hub
{
    public Task Frame(string sessionId, byte[] jpeg) => Task.CompletedTask;
}

/// <summary>
/// هابی که در نگاشت نیست — برای اثبات رفتار fail-closed.
/// نامش عمداً هیچ‌جا ثبت نشده است.
/// </summary>
public sealed class GhostHub : Hub
{
    public Task Any() => Task.CompletedTask;
}
