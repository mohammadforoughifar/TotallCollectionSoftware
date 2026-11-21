@echo off
rem آماده‌سازی یک‌بارهٔ پنل پشتیبانی (بدون موتور لایسنس/ابزار امضا)
setlocal
cd /d "%~dp0"
echo == ساخت پنل ==
dotnet build SupportPanel.csproj -c Release -v:q || goto :err
echo.
echo آماده است. اجرا: run.cmd
goto :eof
:err
echo خطا در آماده‌سازی.
exit /b 1
