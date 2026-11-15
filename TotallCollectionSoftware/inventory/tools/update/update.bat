@echo off
REM ============================================================
REM  پوستهٔ نازک به‌روزرسانی — همهٔ منطق در Update-App.ps1 است.
REM  کدهای خروج: 0 موفق | 10 بستهٔ نامعتبر | 20 لایسنس رد | 30 پیش‌بررسی
REM              40 پشتیبان | 50 توقف سرویس | 60 کپی | 70 دیتابیس
REM              80 سلامت | 90 پارامتر | 91 اجرای هم‌زمان | 95 غیرمنتظره
REM ============================================================
setlocal
set "HERE=%~dp0"
if "%~1"=="" goto usage
if "%~2"=="" goto usage

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%HERE%Update-App.ps1" -PackagePath "%~1" -AppPath "%~2" %3 %4 %5 %6 %7 %8 %9
exit /b %ERRORLEVEL%

:usage
echo Usage: update.bat ^<package-folder^> ^<app-folder^> [additional switches]
echo   example: update.bat D:\pkg\2026-11-14 C:\inetpub\Totall -HealthUrl http://localhost/api/health
exit /b 90
