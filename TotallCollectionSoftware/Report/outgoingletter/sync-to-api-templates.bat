@echo off
rem ============================================================================
rem  کپی قالب‌های این پوشه (Report\outgoingletter) به مسیری که سرور واقعاً چاپ می‌کند
rem
rem  چرا لازم است؟ API هیچ‌جای این پوشه را نمی‌خواند؛ قالب‌ها را از مسیر
rem    <درخت کد>\Inventory.Api\Reports\OutgoingLetters\Companies\{CompanyId}\Outgoing-{A4|A5}.mrt
rem  می‌خواند. تا وقتی این کپی انجام نشود، هر تغییری در این پوشه بی‌اثر است.
rem
rem  استفاده:  دوبار کلیک یا اجرا از خط فرمان (کنار همین فایل)
rem            sync-to-api-templates.bat            → کپی به هر دو درخت کد
rem            sync-to-api-templates.bat inventory    → فقط inventory\src
rem            sync-to-api-templates.bat src          → فقط src
rem ============================================================================
setlocal EnableExtensions EnableDelayedExpansion
chcp 65001 >nul

set "HERE=%~dp0"
set "ROOT=%HERE%..\.."
set "TARGET_ARG=%~1"

set "DEST1=%ROOT%inventory\src\Inventory.Api\Reports\OutgoingLetters\Companies"
set "DEST2=%ROOT%src\Inventory.Api\Reports\OutgoingLetters\Companies"

set "COPIES=0"
set "MISSING=0"

for %%C in (3 1003) do (
    for %%S in (A4 A5) do (
        set "SRC=%HERE%%%C\Report_%%S_OutgoingLetter.mrt"
        if not exist "!SRC!" (
            echo [MISSING] !SRC!
            set /a MISSING+=1
        ) else (
            if /I "%TARGET_ARG%"=="inventory" (
                call :copy "!SRC!" "%DEST1%\%%C\Outgoing-%%S.mrt"
            ) else if /I "%TARGET_ARG%"=="src" (
                call :copy "!SRC!" "%DEST2%\%%C\Outgoing-%%S.mrt"
            ) else (
                call :copy "!SRC!" "%DEST1%\%%C\Outgoing-%%S.mrt"
                call :copy "!SRC!" "%DEST2%\%%C\Outgoing-%%S.mrt"
            )
        )
    )
)

echo.
echo کپی‌شده: %COPIES% فایل   |   پیدا نشده: %MISSING% فایل
echo.
echo مرحلهٔ بعد: API را دوباره Build/Publish یا ری‌استارت کنید.
echo بررسی لاگ چاپ: TemplatePath / FinalPath / FromTemplate
echo.
echo هشدار: اگر فایل مقصد تازه‌تر از این پوشه باشد، همان تصویر جدیدتر با نسخهٔ
echo این پوشه جایگزین می‌شود. پیش از اجرا از مقصد نسخهٔ پشتیبان بگیرید.
exit /b 0

:copy
if not exist "%~dp2" mkdir "%~dp2" >nul 2>&1
copy /Y "%~1" "%~2" >nul
if errorlevel 1 (
    echo [FAILED] %~2
) else (
    echo [OK] %~2
    set /a COPIES+=1
)
exit /b 0
