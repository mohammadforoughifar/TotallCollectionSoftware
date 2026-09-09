@echo off
chcp 65001 >nul
cd /d "%~dp0"
title ایجنت شناسنامه سیستم — فروغ آریا

echo ==============================================
echo    ایجنت شناسنامه سیستم — فروغ آریا
echo ==============================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [!] .NET روی این سیستم نصب نیست.
    echo     برای اجرا، ابتدا .NET 8 را از این آدرس نصب کنید:
    echo     https://dotnet.microsoft.com/download/dotnet/8.0
    echo     (آیتم Runtime یا Desktop Runtime را دانلود و نصب کنید)
    echo.
    pause
    exit /b 1
)

echo [i] آدرس سرور از فایل agent.config.json خوانده می‌شود:
if exist agent.config.json (
    type agent.config.json
    echo.
    echo     اگر آدرس درست نیست، این فایل را با notepad باز و
    echo     آدرس واقعی سرور را جایگزین کنید. مثال:
    echo         { "api": "http://192.168.1.10:5100", "watch": true }
) else (
    echo [!] فایل agent.config.json پیدا نشد — از پیش‌فرض http://localhost:5100 استفاده می‌شود.
)
echo.
echo [i] ایجنت شروع می‌شود... اگر پنجره ماندگار ماند، یعنی در حالت Watch است.
echo     برای خروج Ctrl+C بزنید.
echo.

dotnet Agent.Console.dll
echo.
echo ─────────────────────────────────────────────
pause
