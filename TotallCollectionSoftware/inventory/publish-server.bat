@echo off
setlocal EnableExtensions
chcp 65001 >nul

rem ================================================================
rem Inventory - انتشار روی Windows Server
rem آدرس اجرا: http://192.168.30.104:8181
rem SQL Server: 192.168.30.104 / InventoryDb
rem ================================================================

set "ROOT=%~dp0"
set "CLIENT=%ROOT%src\Inventory.Client"
set "API=%ROOT%src\Inventory.Api"
set "CLIENT_PUBLISH=%ROOT%artifacts\client-publish"
set "API_PUBLISH=%ROOT%artifacts\inventory-server"
set "DEPLOY=%ROOT%deploy\inventory-server"
set "DB_CONNECTION=Server=.;Database=InventoryDb;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False"
set "VERIFY=%ROOT%tools\verify-licensing.ps1"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [ERROR] .NET SDK 8 روی این سیستم نصب نیست.
  exit /b 1
)

echo [1/7] پاک‌سازی خروجی قبلی...
if exist "%CLIENT_PUBLISH%" rmdir /s /q "%CLIENT_PUBLISH%"
if exist "%API_PUBLISH%" rmdir /s /q "%API_PUBLISH%"
if exist "%DEPLOY%" rmdir /s /q "%DEPLOY%"
mkdir "%CLIENT_PUBLISH%" "%API_PUBLISH%" "%DEPLOY%"

pushd "%ROOT%"

echo [2/7] انتشار کلاینت Blazor...
dotnet publish "%CLIENT%\Inventory.Client.csproj" -c Release --no-restore -o "%CLIENT_PUBLISH%"
if errorlevel 1 goto :failed

rem فایل‌های static کلاینت باید در wwwroot API قرار بگیرند.
echo [3/7] انتقال فایل‌های رابط کاربری به API wwwroot...
if not exist "%API%\wwwroot" mkdir "%API%\wwwroot"
xcopy "%CLIENT_PUBLISH%\wwwroot\*" "%API%\wwwroot\" /E /I /Y >nul
if errorlevel 1 goto :failed

echo [4/7] انتشار API برای Windows x64...
dotnet publish "%API%\Inventory.Api.csproj" -c Release -r win-x64 --self-contained false -o "%API_PUBLISH%"
if errorlevel 1 goto :failed

echo [5/7] آماده‌سازی پوشه استقرار...
xcopy "%API_PUBLISH%\*" "%DEPLOY%\" /E /I /Y >nul
if errorlevel 1 goto :failed

rem ================================================================
rem [6/7] انتشار ابزار LicenseCheck در همان پوشهٔ استقرار
rem   چرا؟ دو کاربرد دارد و هر دو روی سرور مشتری/لحظهٔ انتشار لازم است:
rem     ۱) verify-licensing.ps1 با «--keys» می‌فهمد بیلد Release کلید عمومی دارد یا نه
rem     ۲) مدیر مشتری با «--prepare» پیش از جایگزینی فایل‌ها شناسهٔ نصب را می‌سازد
rem   خروجی framework-dependent است (روی سرور، .NET 8 Runtime لازم است — همان پیش‌نیاز API).
echo [6/7] انتشار ابزار LicenseCheck...
dotnet publish "%ROOT%tools\LicenseCheck\LicenseCheck.csproj" -c Release --self-contained false -o "%DEPLOY%\tools\LicenseCheck"
if errorlevel 1 goto :failed

rem ================================================================
rem [7/7] بررسی موتور لایسنس — شرط پایان موفق انتشار
rem   آفلاین: Inventory.Licensing.dll در خروجی + فیلد licensing در /api/health
rem   آنلاین: فقط اگر INVENTORY_HEALTH_URL تنظیم شده باشد
rem ================================================================
echo [7/7] بررسی موتور لایسنس در خروجی...
if not exist "%VERIFY%" (
  echo [ERROR] اسکریپت بررسی پیدا نشد: %VERIFY%
  goto :failed
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%VERIFY%" -AppPath "%DEPLOY%"
if errorlevel 1 (
  echo.
  echo [ERROR] خروجی منتشرشده موتور لایسنس ندارد یا health فیلد licensing را نمی‌سازد.
  echo [ERROR] انتشار متوقف شد.
  goto :failed
)

rem تنظیمات اجرایی را جداگانه می‌سازیم تا اجرای سرور بدون ویرایش دستی انجام شود.
>"%DEPLOY%\run-server.bat" echo @echo off
>>"%DEPLOY%\run-server.bat" echo set "ASPNETCORE_ENVIRONMENT=Production"
>>"%DEPLOY%\run-server.bat" echo set "ASPNETCORE_URLS=http://192.168.30.104:8181"
>>"%DEPLOY%\run-server.bat" echo set "ConnectionStrings__Default=%DB_CONNECTION%"
>>"%DEPLOY%\run-server.bat" echo cd /d "%%~dp0"
>>"%DEPLOY%\run-server.bat" echo Inventory.Api.exe

>"%DEPLOY%\README-DEPLOY.txt" echo آدرس سامانه: http://192.168.30.104:8181
>>"%DEPLOY%\README-DEPLOY.txt" echo اجرا: فایل run-server.bat را با دسترسی مناسب اجرا کنید.
>>"%DEPLOY%\README-DEPLOY.txt" echo در Windows Firewall پورت TCP 8181 را باز کنید.
>>"%DEPLOY%\README-DEPLOY.txt" echo SQL Server: 192.168.30.104 / InventoryDb

popd
echo.
echo [OK] انتشار با موفقیت انجام شد.
echo پوشه آماده انتقال به سرور:
echo %DEPLOY%
echo.
echo روی سرور فایل run-server.bat را اجرا کنید.
exit /b 0

:failed
popd
echo.
echo [ERROR] انتشار ناموفق بود. خروجی خطا را بررسی کنید.
exit /b 1
