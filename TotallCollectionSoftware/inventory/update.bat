@echo off
setlocal EnableExtensions EnableDelayedExpansion
chcp 65001 >nul

rem ==================================================================
rem  به‌روزرسانی نسخهٔ نصب‌شده روی سرور مشتری — ترتیب، مهم است
rem
rem  چرا این فایل؟ شناسهٔ نصب (InstallationId) فقط در «اولین اجرای نسخهٔ
rem  جدید» ساخته می‌شود. مشتری‌ای که امروز روی نسخهٔ بدون لایسنس کار می‌کند،
rem  تا لحظهٔ آپدیت شناسه‌ای ندارد؛ پس نمی‌شود از قبل برایش لایسنس پل صادر
rem  کرد و روز آپدیت، سامانه تا رسیدن فایل لایسنس فقط‌خواندنی می‌ماند.
rem
rem  راه‌حل: پیش از جایگزینی فایل‌ها، یک بار
rem      LicenseCheck --prepare
rem  اجرا می‌شود. این دستور فقط جدول LicenseInstallations را (اگر نباشد)
rem  می‌سازد و یک شناسه درج می‌کند؛ هیچ چیز دیگری را لمس نمی‌کند.
rem  شناسه چاپ می‌شود و برای صدور لایسنس پل مصرف می‌گردد.
rem
rem  استفاده:
rem      update.bat                     فقط مرحلهٔ آماده‌سازی (شناسه را چاپ می‌کند)
rem      update.bat "D:\new-build"      آماده‌سازی + جایگزینی فایل‌ها از آن پوشه
rem
rem  رشتهٔ اتصال: از متغیر محیطی LICENSING_CONNECTION خوانده می‌شود؛ اگر
rem  تعریف نشده باشد، اتصال یکپارچهٔ ویندوز به سرور محلی و InventoryDb.
rem  (عمداً از خط فرمان خوانده نمی‌شود تا رمز در تاریخچهٔ شل نماند.)
rem ==================================================================

set "ROOT=%~dp0"
set "NEW_BUILD=%~1"
set "LICENSECHECK=%ROOT%tools\LicenseCheck\LicenseCheck.exe"
if not exist "%LICENSECHECK%" set "LICENSECHECK=%ROOT%tools\LicenseCheck\LicenseCheck.dll"

echo ==================================================================
echo  آماده‌سازی نصب موجود — پیش از جایگزینی فایل‌ها
echo ==================================================================

if exist "%ROOT%LicenseCheck.exe" set "LICENSECHECK=%ROOT%LicenseCheck.exe"

if not exist "%LICENSECHECK%" (
  echo [WARN] ابزار LicenseCheck در این پوشه نیست: %ROOT%tools\LicenseCheck\
  echo [WARN] مرحلهٔ آماده‌سازی رد شد. شناسهٔ نصب در اولین اجرای نسخهٔ جدید ساخته می‌شود
  echo [WARN] — یعنی تا بارگذاری فایل لایسنس، سامانه فقط‌خواندنی می‌ماند.
  goto :replace_files
)

echo.
echo [1/2] ساخت/خواندن شناسهٔ نصب روی دیتابیس موجود...
if "%LICENSECHECK:~-4%"==".dll" (
  dotnet "%LICENSECHECK%" --prepare
) else (
  "%LICENSECHECK%" --prepare
)

if errorlevel 1 (
  echo.
  echo [ERROR] آماده‌سازی ناموفق بود. فایل‌ها جایگزین نشدند.
  echo [ERROR] علت را برطرف کنید (معمولاً متغیر LICENSING_CONNECTION) و دوباره اجرا کنید.
  exit /b 1
)

echo.
echo [2/2] شناسهٔ بالا را برای صدور لایسنس نگه دارید:
echo       python tools\license-sign.py --private-key ^<key.pem^> --installation-id ^<ID^> --bridge --days 30 --out bridge.lic

:replace_files
if "%NEW_BUILD%"=="" (
  echo.
  echo [i] پوشهٔ نسخهٔ جدید داده نشد؛ فقط مرحلهٔ آماده‌سازی انجام شد.
  echo [i] برای جایگزینی فایل‌ها:  update.bat "D:\path\to\new-build"
  exit /b 0
)

echo.
echo [3/3] جایگزینی فایل‌ها از: %NEW_BUILD%
echo       مقصد: %ROOT%
echo.
echo  ⚠️ پیش از ادامه، سرویس/برنامهٔ Inventory.Api را متوقف کنید.
echo     (اجرای هم‌زمان، فایل‌های در حال استفاده را قفل می‌کند.)
pause

xcopy "%NEW_BUILD%\*" "%ROOT%\" /E /I /Y /EXCLUDE:%ROOT%update-exclude.txt 2>nul
if errorlevel 1 (
  rem اگر فایل exclude نبود، بدون آن کپی می‌کنیم
  xcopy "%NEW_BUILD%\*" "%ROOT%\" /E /I /Y
  if errorlevel 1 (
    echo [ERROR] کپی فایل‌ها ناموفق بود.
    exit /b 1
  )
)

echo.
echo [OK] فایل‌ها جایگزین شدند.
echo      گام بعد:
echo        1) سرویس/برنامه را اجرا کنید
echo        2) update-database.bat را اجرا کنید (اسکیمای لایسنس، افزایشی)
echo        3) فایل لایسنس را از صفحهٔ «لایسنس» بارگذاری کنید
echo.
echo  ℹ️ توجه: با این نسخه، کلید امضای JWT مخصوص همین نصب می‌شود (دیگر در کد نیست).
echo     در نتیجه همهٔ کاربران یک بار باید دوباره وارد شوند.
exit /b 0
