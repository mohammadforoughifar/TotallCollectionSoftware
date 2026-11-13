@echo off
setlocal
pushd "%~dp0" >nul

set "VERIFY=%~dp0tools\verify-licensing.ps1"

echo [DB] Starting the Inventory and RADIS-HR database update...

if exist "%~dp0Inventory.Api.exe" (
    call :verify "%~dp0"
    if errorlevel 1 goto :finish
    "%~dp0Inventory.Api.exe" --update-database
) else if exist "%~dp0Inventory.Api.dll" (
    call :verify "%~dp0"
    if errorlevel 1 goto :finish
    dotnet "%~dp0Inventory.Api.dll" --update-database
) else if exist "%~dp0src\Inventory.Api\Inventory.Api.csproj" (
    rem اجرا از سورس: بررسی ارجاع پروژه به موتور لایسنس
    findstr /i /c:"Inventory.Licensing" "%~dp0src\Inventory.Api\Inventory.Api.csproj" >nul
    if errorlevel 1 (
        echo [ERROR] پروژهٔ Inventory.Api به Inventory.Licensing ارجاع نمی‌دهد.
        set "EXIT_CODE=3"
        goto :finish
    )
    dotnet run --project "%~dp0src\Inventory.Api\Inventory.Api.csproj" -- --update-database
) else (
    echo [ERROR] Inventory.Api.exe/.dll or the source project was not found beside this script.
    set "EXIT_CODE=2"
    goto :finish
)

set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" echo [ERROR] Database update failed with exit code %EXIT_CODE%.

rem اگر سروری در حال اجراست، همان لحظه health را هم بررسی می‌کنیم.
if defined INVENTORY_HEALTH_URL if "%EXIT_CODE%"=="0" (
    echo [DB] Checking live /api/health for the licensing field...
    powershell -NoProfile -ExecutionPolicy Bypass -File "%VERIFY%" -AppPath "%~dp0." -HealthUrl "%INVENTORY_HEALTH_URL%"
    if errorlevel 1 (
        echo [ERROR] سرور در حال اجرا فیلد licensing در /api/health ندارد.
        set "EXIT_CODE=4"
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
powershell -NoProfile -ExecutionPolicy Bypass -File "%VERIFY%" -AppPath "%~1"
exit /b %ERRORLEVEL%
