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

## 🔧 روش دوم: استقرار از سورس

```bash
# 1. کلون ریپازیتوری
git clone https://github.com/mohammadforoughifar/TotallCollectionSoftware.git
cd TotallCollectionSoftware

# 2. بیلد کلاینت (Blazor WASM)
dotnet publish inventory/src/Inventory.Client/Inventory.Client.csproj -c Release -o publish/client

# 3. کپی به wwwroot API
cp -rf publish/client/wwwroot/* inventory/src/Inventory.Api/wwwroot/

# 4. پابلیش API
dotnet publish inventory/src/Inventory.Api/Inventory.Api.csproj -c Release -o publish/api

# 5. اجرا
cd publish/api
dotnet Inventory.Api.dll
```

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