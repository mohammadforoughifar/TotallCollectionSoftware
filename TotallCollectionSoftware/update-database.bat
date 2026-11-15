@echo off
setlocal EnableExtensions
pushd "%~dp0" >nul
chcp 65001 >nul

rem ================================================================
rem به‌روزرسانی دیتابیس Inventory و RADIS-HR
rem
rem ⚠️ سورس مرجع در inventory\src است، نه src قدیمی ریشه.
rem اگر از نسخهٔ بدون لایسنس اجرا شود، اسکیمای لایسنس ساخته نمی‌شود
rem و بعداً «مجوز در سیستم ثبت نشد» می‌دهد. این اسکریپت قبل از اجرا
rem وجود موتور لایسنس را بررسی می‌کند و در صورت نبود، شکست می‌دهد.
rem ================================================================

set "VERIFY=%~dp0inventory\tools\verify-licensing.ps1"
set "EXIT_CODE=0"

echo [DB] Starting the Inventory and RADIS-HR database update...

if exist "%~dp0Inventory.Api.exe" (
    call :verify "%~dp0"
    if errorlevel 1 goto :finish
    "%~dp0Inventory.Api.exe" --update-database
) else if exist "%~dp0Inventory.Api.dll" (
    call :verify "%~dp0"
    if errorlevel 1 goto :finish
    dotnet "%~dp0Inventory.Api.dll" --update-database
) else if exist "%~dp0inventory\src\Inventory.Api\Inventory.Api.csproj" (
    rem اجرا از سورس: خروجی ساخته‌شده هنوز مشخص نیست، پس خود پروژه بررسی می‌شود
    rem که واقعاً به پروژهٔ لایسنس ارجاع بدهد.
    findstr /i /c:"Inventory.Licensing" "%~dp0inventory\src\Inventory.Api\Inventory.Api.csproj" >nul
    if errorlevel 1 (
        echo [ERROR] پروژهٔ Inventory.Api به Inventory.Licensing ارجاع نمی‌دهد.
        echo [ERROR] یعنی سورس بدون موتور لایسنس است؛ به‌روزرسانی متوقف شد.
        set "EXIT_CODE=3"
        goto :finish
    )
    dotnet run --project "%~dp0inventory\src\Inventory.Api\Inventory.Api.csproj" -- --update-database
) else (
    echo [ERROR] نه Inventory.Api.exe/.dll کنار این اسکریپت هست و نه سورس در inventory\src.
    set "EXIT_CODE=2"
    goto :finish
)

set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" (
    echo [ERROR] Database update failed with exit code %EXIT_CODE%.
    goto :finish
)

rem اگر سروری در حال اجراست، همان لحظه health را هم بررسی می‌کنیم.
if defined INVENTORY_HEALTH_URL (
    echo [DB] Checking live /api/health for the licensing field...
    powershell -NoProfile -ExecutionPolicy Bypass -File "%VERIFY%" -AppPath "%~dp0." -HealthUrl "%INVENTORY_HEALTH_URL%"
    if errorlevel 1 (
        echo [ERROR] سرور در حال اجرا فیلد licensing در /api/health ندارد.
        set "EXIT_CODE=4"
        goto :finish
    )
)

:finish
popd >nul
exit /b %EXIT_CODE%

rem ---------- زیرروال بررسی خروجی آمادهٔ اجرا ----------
:verify
if not exist "%VERIFY%" (
    echo [ERROR] اسکریپت بررسی موتور لایسنس پیدا نشد: %VERIFY%
    exit /b 2
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%VERIFY%" -AppPath "%~1" %2
exit /b %ERRORLEVEL%
