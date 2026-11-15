# هاب‌های SignalR و لایسنس

> **سند مرجع بازبینی چهارم — مورد ۲.**
> هر هاب، هر متد، ماژول مربوط، و رفتارش در `Missing` / `Invalid` / `Valid`.

---

## ۱) چرا این سند لازم شد

دروازهٔ لایسنس (`LicenseGateMiddleware`) فقط مسیرهای `/api/*` را می‌بیند. هاب‌ها روی
`/hubs/*` سرو می‌شوند و از فیلترهای اکشن MVC عبور نمی‌کنند. نتیجه: **تا پیش از Stage ۲،
هیچ‌یک از پنج هاب حتی یک بررسی لایسنس نداشت** — نه وضعیت کلی، نه ماژول. یعنی کسی می‌توانست
بدون لایسنس، از طریق WebSocket، به همان قابلیت‌هایی برسد که از HTTP بسته بود.

راه‌حل: `HubLicenseFilter` (`Licensing/Infrastructure/HubLicenseFilter.cs`)، ثبت‌شده در
`Program.cs` داخل `AddSignalR`:

```csharp
builder.Services.AddSignalR(o =>
{
    // همان قاعدهٔ HTTP، روی کانال بلادرنگ
    o.AddFilter<Inventory.Api.Licensing.Infrastructure.HubLicenseFilter>();
});
builder.Services.AddScoped<Inventory.Api.Licensing.Infrastructure.HubLicenseFilter>();
```

---

## ۲) فهرست کامل هاب‌ها و متدها

| هاب | مسیر | ماژول | متدها | نوع |
|---|---|---|---|---|
| `DashboardHub` | `/hubs/dashboard` | `dashboard` (همیشه‌فعال) | `OnConnectedAsync`، `RequestHardwareCheck` | خواندنی |
| `NotifyHub` | `/hubs/notify` | `core` (همیشه‌فعال) | `OnConnectedAsync`، `SendAsync`، `SendManyAsync`، `SendToRoleAsync`، `BroadcastChangedAsync` | ارسال سمت‌سرور |
| `ChatHub` | `/hubs/chat` | `chat` | `OnConnectedAsync`، `JoinConversation`، `LeaveConversation`، `SendTyping`، `StopTyping` + ۶ متد `Notify*` سمت‌سرور | حضور/عضویت |
| `RemoteHub` | `/hubs/remote` | `it_assets` | `OnConnectedAsync`، `JoinSession`، `LeaveSession`، `SendInput`، `EndSession` | **کنشی** |
| `RemoteAgentHub` | `/hubs/remote-agent` | `it_assets` | `OnConnectedAsync`، `ConsentResult`، `Frame` | **کنشی** |

> ⚠️ `NotifyHub.SendAsync/SendManyAsync/SendToRoleAsync` و `ChatHub.Notify*` **متد هابِ
> قابل‌فراخوانی از کلاینت نیستند** — یعنی کلاینت نمی‌تواند با `invoke` صدایشان بزند، چون
> امضایشان با `Clients` کار می‌کند و از سمت سرور صدا زده می‌شوند؛ این‌ها از فیلتر
> `InvokeMethodAsync` عبور نمی‌کنند چون فراخوانی کلاینت به آن‌ها راه ندارد... **مگر** از طریق
> `HubConnection.InvokeAsync("SendAsync", …)` که SignalR آن را مجاز می‌داند. به همین دلیل
> `SendAsync` و هم‌خانواده‌هایش عمداً در فهرست «مجاز در فقط‌خواندنی» **نیستند** ⇒ در
> `Missing`/`Invalid` رد می‌شوند.

---

## ۳) رفتار در هر حالت

| وضعیت | اتصال `DashboardHub`/`NotifyHub`/`ChatHub` | اتصال `RemoteHub`/`RemoteAgentHub` | متد نوشتاری |
|---|---|---|---|
| `Valid` | ✅ | ✅ | ✅ |
| `Missing` | ✅ (باز) | ❌ `HubException` | ❌ `HubException` |
| `Invalid` | ✅ (باز) | ❌ `HubException` | ❌ `HubException` |
| ماژول `Disabled` | ❌ | ❌ | ❌ |

**چرا اتصال داشبورد/اعلان/چت در حالت بدون لایسنس باز می‌ماند؟** چون پوستهٔ برنامه برای
نمایش بنر «لایسنس فعال نیست» و پیام فارسی راهنما به همین اتصال‌ها وابسته است. اگر اتصال
رد شود، کلاینت در حلقهٔ اتصال مجدد می‌افتد و کاربر فکر می‌کند برنامه خراب است — بدترین
نتیجهٔ ممکن برای یک قفل.

**چرا اتصال ریموت بسته می‌شود؟** چون خودِ قابلیت، کنش است. دسترسی از راه دور به دستگاه
مشتری بدون لایسنس معتبر، همان چیزی است که باید بسته بماند.

---

## ۴) متدهای مجاز در حالت فقط‌خواندنی — با دلیل

فهرست کامل و بسته (`HubLicenseFilter.ReadOnlySafeMethods`):

| متد | هاب | چرا مجاز است |
|---|---|---|
| `RequestHardwareCheck` | Dashboard | یک دور پینگ دستگاه‌ها؛ دادهٔ کسب‌وکار نمی‌نویسد و فقط آمار سخت‌افزار را تازه می‌کند — همان چیزی که GETهای داشبورد نشان می‌دهند. |
| `JoinConversation` | Chat | عضویت در گروه SignalR، پس از بررسی عضویت واقعی در گفتگو. |
| `LeaveConversation` | Chat | خروج از گروه؛ هیچ نوشتنی ندارد. |
| `SendTyping` | Chat | وضعیت گذرا («در حال تایپ»)، نه پیام و نه رکورد دیتابیس. |
| `StopTyping` | Chat | خاموش‌کردن همان نشانگر. |

هر متد دیگری (از جمله متد تازه‌ای که فردا اضافه شود) در حالت فقط‌خواندنی **رد** می‌شود —
این فهرست «allowlist» است، نه «denylist».

---

## ۵) نگاشت هاب → ماژول (fail-closed)

`HubLicenseFilter.Hubs` تنها منبع حقیقت است. هابی که در آن نباشد، **بسته** می‌شود و در لاگ
هشدار ثبت می‌شود:

```
[License] هاب «X» در نگاشت لایسنس نیست؛ اتصال بسته شد. ردیف آن را به HubLicenseFilter اضافه کنید.
```

⚠️ این برخلاف نگاشت کنترلرهاست که fail-open است. تفاوت عمدی است: افتادن یک **هاب** در حالت
باز، یک کانال بلادرنگِ بدون هیچ بررسی می‌سازد؛ افتادن یک **کنترلر** در حالت بسته فقط یک خطای
قابل‌مشاهده در تست است.

---

## ۶) آنچه در Stage ۳ به این‌ها اضافه می‌شود

> ⚠️ **هنوز پیاده نشده — تعهد Stage ۳.**

- **صندلی (seat):** اتصال هر هاب باید یک صندلی کاربر را مصرف کند؛ قطع اتصال آن را آزاد کند.
- **`sid`:** هر اتصال باید به شناسهٔ نشست (`sid`) گره بخورد تا احراز هویت دوباره در همان
  اتصال، نشست قبلی را نسازد.
- `RemoteAgentHub` که فعلاً `.AllowAnonymous()` است (ایجنت روی دستگاه مشتری، بدون JWT)
  باید با توکن یک‌بارمصرفِ صادرشده از سرور تأیید شود، نه با ناشناس‌بودن.

---

## ۷) چگونه آزمایش می‌شود

`tests/LicensingDb` بخش ۱۸ (ماتریس دروازه) و بخش‌های هاب:

- `DashboardHub` در `Missing` ⇒ اتصال موفق (پوسته باید بنر بدهد)
- `RemoteHub` در `Missing` ⇒ `HubException`
- هاب ناشناخته ⇒ `HubException` + لاگ هشدار
- ماژول `Disabled` ⇒ اتصال هر پنج هاب رد
- متد غیرمجاز در `Invalid` ⇒ `HubException` با پیام فارسی

## ۶. تشخیص هاب: نام کامل نوع، نه نام کوتاه (بازبینی چهارم، بند ۵)

`HubLicenseFilter.ResolveHubKey(Type)` با این ترتیب تصمیم می‌گیرد:

1. **`Type.FullName`** — نگاشت دقیق، مثل `Inventory.Api.Hubs.Remote.RemoteAgentHub`
2. اگر نبود: **نام کوتاه** — فقط برای سازگاری با کلاس‌های مشتق و دوقلوهای تست
3. اگر هیچ‌کدام نبود: **بسته** (fail-closed)

نام‌های تولیدی کامپایلر (شامل `<` یا `$`، مثل انواع بی‌نام) هرگز پذیرفته نمی‌شوند.

| نام کامل | کلید |
|---|---|
| `Inventory.Api.Hubs.DashboardHub` | DashboardHub |
| `Inventory.Api.Hubs.NotifyHub` | NotifyHub |
| `Inventory.Api.Hubs.ChatHub` | ChatHub |
| `Inventory.Api.Hubs.Remote.RemoteHub` | RemoteHub |
| `Inventory.Api.Hubs.Remote.RemoteAgentHub` | RemoteAgentHub |

تست `tests/LicensingDb` §۲۳: هر پنج هاب واقعی با نام کامل شناخته می‌شوند؛ اگر هاب تازه‌ای
اضافه شود و به نگاشت نیاید، تست قرمز می‌شود.
