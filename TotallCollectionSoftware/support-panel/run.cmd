@echo off
rem اجرای پنل پشتیبانی روی ویندوز (فقط localhost)
setlocal
cd /d "%~dp0"
if "%ASPNETCORE_ENVIRONMENT%"=="" set ASPNETCORE_ENVIRONMENT=Production
if "%ASPNETCORE_URLS%"=="" set ASPNETCORE_URLS=http://127.0.0.1:5399
if "%Signer__ToolPath%"=="" set Signer__ToolPath=%~dp0..\inventory\tools\LicenseTool\bin\Release\net8.0\LicenseTool.dll
if "%Signer__PresetsPath%"=="" set Signer__PresetsPath=%~dp0..\inventory\tools\LicenseTool\presets.json
echo پنل پشتیبانی روی %ASPNETCORE_URLS% اجرا می‌شود ...
dotnet run --project SupportPanel.csproj -c Release --no-launch-profile
endlocal
