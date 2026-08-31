using System.Text;
using System.Text.Json;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>
/// ================== سرویس پیام‌رسان‌های ایرانی (بله + ایتا) ==================
///  • بله:  ارسال با ربات از طریق tapi.bale.ai — کاربر باید یک‌بار ربات را /start کند
///  • ایتا: ارسال از طریق سرویس ایتایار (eitaayar.ir) با توکن پنل
///  • تشخیص عضویت: با «همگام‌سازی بله» — مخاطبینی که با ربات شماره‌شان را به اشتراک
///    گذاشته‌اند، با موبایل کاربران تطبیق داده می‌شوند و شناسه چتشان ذخیره می‌شود.
///  • شماره معرف سامانه (پیش‌فرض 09111189771) در امضای هر پیام می‌آید.
/// </summary>
public interface IMessengerService
{
    /// <summary>ارسال پیام به کاربر در بله و ایتا (هر کدام که دارد). خطاها بی‌صدا ثبت می‌شوند.</summary>
    Task SendToUserAsync(int userId, string title, string? body);

    /// <summary>همگام‌سازی بله: تطبیق مخاطبین ربات با موبایل کاربران — خروجی: تعداد تطبیق‌های جدید.</summary>
    Task<(int matched, string message)> SyncBaleAsync();
}

public class MessengerService : IMessengerService
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpFactory;

    public MessengerService(AppDbContext db, IHttpClientFactory httpFactory)
    {
        _db = db;
        _httpFactory = httpFactory;
    }

    private async Task<(string? bale, string? eitaa, string sender)> TokensAsync()
    {
        var st = await _db.AppSettings.AsNoTracking().FirstOrDefaultAsync();
        return (st?.BaleBotToken, st?.EitaaToken, st?.MessengerSenderNumber ?? "09111189771");
    }

    public async Task SendToUserAsync(int userId, string title, string? body)
    {
        try
        {
            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return;
            if (string.IsNullOrWhiteSpace(user.BaleChatId) && string.IsNullOrWhiteSpace(user.EitaaChatId)) return;

            var (baleToken, eitaaToken, sender) = await TokensAsync();
            var text = $"🔔 {title}\n{body}\n———\n📱 سامانه انبار و فروش — {sender}";

            var http = _httpFactory.CreateClient("messenger");

            // ---------- بله ----------
            if (!string.IsNullOrWhiteSpace(user.BaleChatId) && !string.IsNullOrWhiteSpace(baleToken))
            {
                try
                {
                    var payload = JsonSerializer.Serialize(new { chat_id = user.BaleChatId, text });
                    await http.PostAsync($"https://tapi.bale.ai/bot{baleToken}/sendMessage",
                        new StringContent(payload, Encoding.UTF8, "application/json"));
                }
                catch (Exception ex) { Console.WriteLine($"[Messenger] Bale send failed: {ex.Message}"); }
            }

            // ---------- ایتا (ایتایار) ----------
            if (!string.IsNullOrWhiteSpace(user.EitaaChatId) && !string.IsNullOrWhiteSpace(eitaaToken))
            {
                try
                {
                    var form = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["chat_id"] = user.EitaaChatId,
                        ["text"] = text
                    });
                    await http.PostAsync($"https://eitaayar.ir/api/{eitaaToken}/sendMessage", form);
                }
                catch (Exception ex) { Console.WriteLine($"[Messenger] Eitaa send failed: {ex.Message}"); }
            }
        }
        catch (Exception ex) { Console.WriteLine($"[Messenger] send error: {ex.Message}"); }
    }

    public async Task<(int matched, string message)> SyncBaleAsync()
    {
        var (baleToken, _, _) = await TokensAsync();
        if (string.IsNullOrWhiteSpace(baleToken))
            return (0, "توکن ربات بله در تنظیمات وارد نشده است.");

        try
        {
            var http = _httpFactory.CreateClient("messenger");
            var resp = await http.GetAsync($"https://tapi.bale.ai/bot{baleToken}/getUpdates");
            if (!resp.IsSuccessStatusCode)
                return (0, $"ارتباط با سرور بله ناموفق بود ({(int)resp.StatusCode}).");

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("result", out var results))
                return (0, "پاسخ نامعتبر از سرور بله.");

            // phone → chatId از مخاطبین به‌اشتراک‌گذاشته‌شده با ربات
            var phoneToChat = new Dictionary<string, string>();
            foreach (var upd in results.EnumerateArray())
            {
                if (!upd.TryGetProperty("message", out var msg)) continue;
                if (!msg.TryGetProperty("contact", out var contact)) continue;
                var phone = contact.TryGetProperty("phone_number", out var ph) ? ph.GetString() : null;
                var chatId = msg.TryGetProperty("chat", out var chat) && chat.TryGetProperty("id", out var cid)
                    ? cid.GetRawText() : null;
                if (!string.IsNullOrWhiteSpace(phone) && !string.IsNullOrWhiteSpace(chatId))
                    phoneToChat[Normalize(phone)] = chatId!;
            }

            if (phoneToChat.Count == 0)
                return (0, "مخاطبی پیدا نشد — کاربران باید ربات را استارت کرده و شماره خود را به اشتراک بگذارند.");

            var users = await _db.Users.Where(u => u.Mobile != null && u.Mobile != "").ToListAsync();
            var matched = 0;
            foreach (var u in users)
            {
                if (phoneToChat.TryGetValue(Normalize(u.Mobile!), out var chatId) && u.BaleChatId != chatId)
                {
                    u.BaleChatId = chatId;
                    matched++;
                }
            }
            if (matched > 0) await _db.SaveChangesAsync();
            return (matched, $"{matched} کاربر با بله تطبیق داده شد.");
        }
        catch (Exception ex)
        {
            return (0, "خطا در همگام‌سازی بله: " + ex.Message);
        }
    }

    /// <summary>نرمال‌سازی شماره: 09xx و +989xx و 989xx یکسان می‌شوند.</summary>
    private static string Normalize(string phone)
    {
        var p = new string(phone.Where(char.IsDigit).ToArray());
        if (p.StartsWith("98")) p = "0" + p[2..];
        if (!p.StartsWith("0")) p = "0" + p;
        return p;
    }
}
