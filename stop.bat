@echo off
REM =====================================================================
REM  توقف سرور نرم‌افزار یکپارچه فروغ آریا (Inventory.Api.exe)
REM  این فایل باید کنار Inventory.Api.exe در پوشهٔ پابلیش باشد.
REM =====================================================================
setlocal
cd /d "%~dp0"
set "KILLED="

if exist "totall.pid" (
  set /p APP_PID=<totall.pid
  if not "%APP_PID%"=="" (
    taskkill /PID %APP_PID% /T /F >nul 2>nul
    if not errorlevel 1 (echo Stopped process %APP_PID%. & set "KILLED=1")
  )
  del "totall.pid" >nul 2>nul
)

REM اگر با pid متوقف نشد، همهٔ نمونه‌های Inventory.Api.exe بسته می‌شوند
taskkill /IM Inventory.Api.exe /T /F >nul 2>nul
if errorlevel 1 (
  if not defined KILLED echo No running Inventory.Api.exe found.
) else (
  if not defined KILLED echo Stopped Inventory.Api.exe.
)
endlocal
