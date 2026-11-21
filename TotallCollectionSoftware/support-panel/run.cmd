@echo off
rem اجرای پنل پشتیبانی روی ویندوز (فقط localhost)
setlocal
cd /d "%~dp0"
if "%ASPNETCORE_ENVIRONMENT%"=="" set ASPNETCORE_ENVIRONMENT=Production
if "%ASPNETCORE_URLS%"=="" set ASPNETCORE_URLS=http://127.0.0.1:5399
echo پنل پشتیبانی روی %ASPNETCORE_URLS% اجرا می‌شود ...
dotnet run --project SupportPanel.csproj -c Release --no-launch-profile
endlocal
