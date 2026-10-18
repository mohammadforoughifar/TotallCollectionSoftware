@echo off
REM =====================================================================
REM  اجرای سرور نرم‌افزار یکپارچه فروغ آریا (API + وب‌اپ) — ویندوز سرور
REM  آدرس سرویس: http://192.168.30.104:8181
REM  دیتابیس   : SQL Server روی 192.168.30.104 (کاربر sa)
REM  این فایل باید کنار Inventory.Api.exe در پوشهٔ پابلیش باشد.
REM =====================================================================
setlocal
cd /d "%~dp0"

if not exist "Inventory.Api.exe" (
  echo ERROR: Inventory.Api.exe در این پوشه پیدا نشد. این فایل را کنار آن اجرا کنید.
  pause
  exit /b 1
)

REM --- آدرس و پورت سرویس‌دهی (همین مقدار در سمت کلاینت/مرورگر استفاده می‌شود) ---
set "ASPNETCORE_URLS=http://192.168.30.104:8181"

REM --- رشتهٔ اتصال SQL Server ---
set "ConnectionStrings__Default=Server=192.168.30.104;Database=InventoryDb;User Id=sa;Password=Mohammad011700;Persist Security Info=True;TrustServerCertificate=True;Encrypt=False"

REM --- محیط اجرا ---
set "ASPNETCORE_ENVIRONMENT=Production"
set "DOTNET_PRINT_TELEMETRY_MESSAGE=false"

if not exist "logs" mkdir "logs"

if exist "totall.pid" (
  for /f %%P in (totall.pid) do tasklist /FI "PID eq %%P" 2>nul | find "%%P" >nul && (
echo Server is already running ^(PID %%P^). Use stop.bat to stop it.
    pause
    exit /b 0
  )
  del "totall.pid" >nul 2>nul
)

echo.
echo Starting server on http://192.168.30.104:8181 ...
echo Database: InventoryDb @ 192.168.30.104
echo.
powershell -NoProfile -ExecutionPolicy Bypass -Command "$p=Start-Process -FilePath '%~dp0Inventory.Api.exe' -WorkingDirectory '%~dp0' -PassThru -WindowStyle Hidden -RedirectStandardOutput '%~dp0logs\server.log' -RedirectStandardError '%~dp0logs\server-error.log'; Set-Content -Path '%~dp0totall.pid' -Value $p.Id"
if errorlevel 1 (echo ERROR: server could not start. See logs\server-error.log & pause & exit /b 1)

timeout /t 5 /nobreak >nul
start "" "http://192.168.30.104:8181"

echo Server started.  Log file: logs\server.log
echo Health check : http://192.168.30.104:8181/api/health
echo If the page does not open, make sure Windows Firewall allows inbound TCP 8181
echo   netsh advfirewall firewall add rule name="Totall 8181" dir=in action=allow protocol=TCP localport=8181
echo.
echo API in background started. Press any key to close this window.
pause >nul
endlocal
