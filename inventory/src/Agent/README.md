# ایجنت شناسنامه سخت‌افزار (Agent)

این پوشه ایجنت شناسنامه سیستم را در دو پروژه‌ی مجزا نگه می‌دارد تا هم به‌صورت **کلاس لایبرری** قابل استفاده در برنامه‌های دیگر باشد و هم به‌صورت **مستقل** رانش شود:

| پروژه | نوع | توضیح |
|---|---|---|
| `Agent.Core` | **Class Library** (net8.0) | همه‌ی منطق: جمع‌آوری مشخصات سخت‌افزار با WMI (مادربرد، CPU، رم، هارد + S.M.A.R.T، گرافیک، مانیتور، شبکه، درایوها، سیستم‌عامل) + کلاینت ارتباط با سرور + اجرای دستورهای از راه دور |
| `Agent.Runner` | Console App (net8.0) | پوسته‌ی نازک اجرایی — پیکربندی را می‌خواند، `Agent.Core` را صدا می‌زند و خروجی را چاپ می‌کند |

## استفاده از کلاس لایبرری در برنامه‌های دیگر

```csharp
using Agent.Core;

// ۱) جمع‌آوری مشخصات کامل سخت‌افزار
SystemInfoData info = HardwareCollector.Collect();          // با AgentId پیش‌فرض: AGENT-<ComputerName>
HardwareDetails details = HardwareCollector.CollectDetails(); // فقط جزئیات ساختاریافته

// ۲) ارسال به سرور شناسنامه سیستم
using var client = new AgentApiClient("http://192.168.1.10:5100");
SendResult result = await client.SendInfoAsync(info);

// ۳) دستورهای از راه دور (اختیاری — حالت Watch)
var cmds = await client.GetPendingCommandsAsync(info.AgentId);
foreach (var c in cmds)
{
    var (ok, msg) = RemoteCommandExecutor.Execute(c.Action); // Reboot | Shutdown | Lock
    await client.ReportCommandResultAsync(c.Id, ok, msg);
}
```

## رانش مستقل (روی سیستم کاربرها)

```bash
cd inventory/src/Agent/Agent.Runner
dotnet publish -c Release -o publish
```

پوشه‌ی `publish` را روی هر سیستم کپی کنید، آدرس سرور را در `agent.config.json` تنظیم کنید:

```json
{ "api": "http://192.168.1.10:5100", "watch": true }
```

و `run-agent.bat` را دابل‌کلیک کنید (یا `dotnet Agent.Runner.dll`).

- **بدون `watch`**: یک‌بار اطلاعات را می‌فرستد و خارج می‌شود.
- **با `watch: true`**: ماندگار می‌ماند و هر ۱۵ ثانیه دستورهای از راه دور (ری‌استارت/خاموش/قفل) را از سرور می‌گیرد.

## اندپوینت‌های سمت سرور (SystemInfoController)

- `POST api/SystemInfo` — ثبت/به‌روزرسانی مشخصات (upsert + تشخیص تغییر سخت‌افزار)
- `GET api/SystemInfo/agent-commands?agentId=...` — دستورهای در انتظار این سیستم
- `POST api/SystemInfo/commands/{id}/result` — گزارش نتیجه‌ی اجرای دستور

> نکته: جمع‌آوری WMI فقط روی ویندوز فعال است؛ روی سیستم‌عامل‌های دیگر فقط اطلاعات شبکه برگردانده می‌شود.
