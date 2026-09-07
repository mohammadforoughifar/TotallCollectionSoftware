# 🚀 راهنمای استقرار — Totall Collection Software v3

## 📋 پیش‌نیازها
- **.NET 8 SDK** یا **.NET 8 Runtime**
- **SQL Server** (اختیاری — می‌توان از SQLite استفاده کرد)

## 📦 روش اول: استقرار سریع (فایل پابلیش شده)

فایل `TotallCollectionSoftware_v3_Published.zip` را از حالت فشرده خارج کنید:

```bash
unzip TotallCollectionSoftware_v3_Published.zip -d deploy
cd deploy/api
chmod +x run.sh
./run.sh
```

### تنظیم دیتابیس
فایل `appsettings.json` را ویرایش کنید:

**برای SQLite (پیش‌فرض):**
```json
{
  "Database": { "Provider": "Sqlite" },
  "ConnectionStrings": {
    "Default": "Data Source=inventory.db"
  }
}
```

**برای SQL Server:**
```json
{
  "Database": { "Provider": "SqlServer" },
  "ConnectionStrings": {
    "Default": "Server=.;Database=InventoryDb;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False"
  }
}
```

## 💬 ذخیره‌سازی فایل‌های پیام‌رسان (بعد از اولین استقرار حتماً بخوانید)

فایل‌های پیوست چت به‌صورت پیش‌فرض در پوشهٔ `App_Data/chat` کنار API ذخیره می‌شوند. این پوشه زیر پوشهٔ ریلیز است؛ اگر نسخهٔ بعدی را در پوشهٔ تازه مستقر کنید، دانلود پیوست‌های قبلی با خطای «فایل پیوست روی سرور یافت نشد.» شکست می‌خورد. راه‌حل: یک مسیر پایدار/اشتراکی تعریف و روی همهٔ نمونه‌ها تنظیم کنید:

```json
{
  "Files": {
    "ChatRoot": "D:\\ChatFiles",          // ذخیرهٔ جدید؛ برای لینوکس مثلاً /var/lib/totall/chat
    "ChatLegacyRoot": ""                    // (اختیاری) محل فایل‌های قدیمی wwwroot/uploads/chat
  }
}
```

یا بدون تغییر فایل، با متغیر محیط (مقدار خالی یعنی رفتار پیش‌فرض):
```bash
export Files__ChatRoot=/var/lib/totall/chat
export Files__ChatLegacyRoot=/var/backups/totall/oldchat/uploads/chat   # در صورت نیاز
```

جزئیات علت، fallback مسیرها و روش انتقال امن: بخش ۵ گزارش `BUGFIX-CHAT-REALTIME-FILES.md`.

## 🔧 روش دوم: استقرار از سورس

```bash
# 1. کلون ریپازیتوری
git clone https://github.com/mohammadforoughifar/TotallCollectionSoftware.git
cd TotallCollectionSoftware

# 2. بیلد کلاینت (Blazor WASM)
dotnet publish inventory/src/Inventory.Client/Inventory.Client.csproj -c Release -o publish/client

# 2-الف. بیلد کلاینت ماژول RADIS-HR (SPA مستقل زیر /radis-hr)
dotnet publish inventory/modules/RadisHr/src/RadisHr.Client/RadisHr.Client.csproj -c Release -o publish/radis-hr

# 3. کپی به wwwroot API
cp -rf publish/client/wwwroot/* inventory/src/Inventory.Api/wwwroot/
mkdir -p inventory/src/Inventory.Api/wwwroot/radis-hr
cp -rf publish/radis-hr/wwwroot/* inventory/src/Inventory.Api/wwwroot/radis-hr/

# 4. پابلیش API
dotnet publish inventory/src/Inventory.Api/Inventory.Api.csproj -c Release -o publish/api

# 5. اجرا
cd publish/api
dotnet Inventory.Api.dll
```

> **نکته:** به‌جای مراحل ۲ تا ۳ می‌توانید از اسکریپت‌های آمادهٔ `inventory/RUN.ps1` یا
> `inventory/deploy-single.ps1` / `inventory/deploy-single.sh` استفاده کنید که پابلیش
> کلاینت اصلی و RADIS-HR و کپی در `wwwroot` را خودکار انجام می‌دهند.

## 🌐 دسترسی
- **API + UI یکجا:** http://localhost:5100
- **مستندات Swagger:** http://localhost:5100/swagger
- **ورود پیش‌فرض:** admin / admin

## 📁 ساختار پابلیش
```
api/
├── Inventory.Api.dll          # هسته برنامه
├── appsettings.json            # تنظیمات
├── run.sh                      # اسکریپت اجرا (لینوکس)
├── web.config                  # تنظیمات IIS (ویندوز)
└── wwwroot/                    # کلاینت Blazor WASM + فایل‌های استاتیک
    ├── index.html
    ├── _framework/             # فایل‌های WASM
    ├── css/                    # استایل‌ها
    ├── fonts/                  # فونت‌های فارسی
    └── ...
```

## 🧹 نکات امنیتی
1. رمز پیش‌فرض `admin/admin` را تغییر دهید
2. در محیط Production حتماً `Database:SeedDemoData` را `false` بگذارید
3. برای SQL Server از `Trusted_Connection=False` و رمز عبور استفاده کنید
4. کلید `EncryptionKey` را برای رمزنگاری پیوست‌ها تنظیم کنید