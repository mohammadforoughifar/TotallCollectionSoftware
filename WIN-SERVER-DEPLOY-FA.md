# استقرار روی ویندوز سرور — راهنمای سریع

## روش ۱: پابلیش آماده (اگر از Agent گرفته شده)
1. پوشهٔ پابلیش را روی سرور باز کنید (مثلاً `D:\TotallPublish`).
2. روی **`run-server.bat`** دابل‌کلیک کنید.
3. سرویس روی **http://192.168.30.104:8181** بالا می‌آید و مرورگر هم باز می‌شود.

## روش ۲: ساخت پابلیش روی سیستم خودتان
1. `run-server.bat` و `publish-win-server.bat` (همین مخزن ریشه) (کنار پوشهٔ `inventory`) بگذارید.
2. روی `publish-win-server.bat` دابل‌کلیک کنید → پوشهٔ `publish-win` ساخته می‌شود.
3. پوشهٔ `publish-win` را روی سرور کپی کنید و `run-server.bat` را اجرا کنید.

## تنظیمات داخل run-server.bat
- **آدرس**: `ASPNETCORE_URLS=http://192.168.30.104:8181`
- **دیتابیس**: `Server=.;Database=InventoryDb;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False`
- متغیرهای محیطی این bat بر appsettings.json تقدّم دارند؛ برای تغییر کارخانهٔ دیتابیس/پورت فقط همین فایل را ویرایش کنید.

## نکات اجرایی
- **فایروال**: پورت TCP 8181 را روی ویندوز سرور باز کنید
  (`netsh advfirewall firewall add rule name="Totall 8181" dir=in action=allow protocol=TCP localport=8181`).
- **دیتابیس**: برنامه در اولین اجرا جداول را خود می‌سازد/مهاجرت می‌دهد (EF Migrations خودکار؛ کاربر `sa` باید مجوز ساخت دیتابیس `InventoryDb` را داشته باشد).
- **HTTPS داخلی**: برنامه طبق تنظیمات، یک شنوندهٔ HTTPS خود-امضا هم روی پورت 5443 می‌سازد (اختیاری/اخبار سیستمی) — هنگام نصب با تانل به‌روزرسانی، اگر نمی‌خواهید در appsettings `"Https": {"Enabled": false}` کنید.
- **اجرا به‌عنوان سرویس ویندوز**: می‌توانید با `nssm` یا `sc.exe` همین run-server.bat را سرویس کنید، یا مستقیم `Inventory.Api.exe` را با متغیرهای بالا اجرا کنید.
- نسخهٔ پابلیش **self-contained** است؛ نیازی به نصب .NET روی سرور نیست.
