@echo off
REM =====================================================================
REM  اجرای سرور نرم‌افزار یکپارچه (API + وب‌اپ) — ویندوز سرور (حالت کنسول)
REM  آدرس سرویس: http://192.168.30.104:8181
REM  برای اجرای پس‌زمینه + بستن با stop.bat، فایل start.bat را اجرا کنید.
REM  این فایل باید کنار Inventory.Api.exe در پوشهٔ پابلیش باشد.
REM =====================================================================
setlocal
cd /d "%~dp0"

REM --- آدرس و پورت سرویس‌دهی ---
set "ASPNETCORE_URLS=http://192.168.30.104:8181"

REM --- رشتهٔ اتصال به SQL Server ---
set "ConnectionStrings__Default=Server=192.168.30.104;Database=InventoryDb;User Id=sa;Password=Mohammad011700;Persist Security Info=True;TrustServerCertificate=True;Encrypt=False;MultipleActiveResultSets=True"

REM --- محیط عملیاتی ---
set "ASPNETCORE_ENVIRONMENT=Production"

echo.
echo  Server is starting on http://192.168.30.104:8181 ...
echo  Database: InventoryDb @ 192.168.30.104
echo  Health  : http://192.168.30.104:8181/api/health
echo  (Ctrl+C to stop this window)
echo.
start "" "http://192.168.30.104:8181"

Inventory.Api.exe

echo.
echo  Server stopped.
pause
endlocal
