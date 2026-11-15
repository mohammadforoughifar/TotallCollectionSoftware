# InventoryRemoteAgent — ایجنت ریموت دسکتاپ (فاز ۱)

این ایجنت روی سیستم مقصد اجرا می‌شود و به سرور مرکزی متصل می‌گردد. فاز ۱ فقط **ویندوز** و جلسه **با رضایت کاربر** را پشتیبانی می‌کند.

## نصب سریع

1. در پنل مرکزی (`/settings/remote`) یک دستگاه بسازید و کلید `rmd_...` را کپی کنید.
2. روی سیستم مقصد:

```powershell
InventoryRemoteAgent.exe init
notepad remote-agent.json
```

فایل `remote-agent.json`:

```json
{
  "serverUrl": "https://central.example.com",
  "deviceKey": "rmd_...",
  "deviceName": "PC-ANBAR",
  "insecure": false
}
```

3. اجرا:

```powershell
InventoryRemoteAgent.exe run
```

یا یک‌باره:

```powershell
InventoryRemoteAgent.exe run --server https://central.example.com --key rmd_...
```

## رفتار امنیتی فاز ۱
- بدون کلید دستگاه اتصال پذیرفته نمی‌شود.
- هر جلسه نیاز به رضایت صریح کاربر روی سیستم مقصد دارد.
- کنترل موس/کیبورد فقط در جلسه‌های دارای مجوز Control فعال است.
- با قطع ایجنت یا پایان جلسه، کپچر متوقف می‌شود.

## محدودیت‌های فاز ۱
- فقط ویندوز؛ فقط session تعاملی کاربر.
- کپچر GDI/JPEG و relay از سرور مرکزی (نه WebRTC).
- Unattended/UAC/صفحه لاگین، Linux/macOS، ضبط جلسه و انتقال فایل در فازهای بعدی.
