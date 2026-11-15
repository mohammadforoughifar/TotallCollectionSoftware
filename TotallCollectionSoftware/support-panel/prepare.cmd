@echo off
rem آماده‌سازی یک‌بارهٔ مینی‌پروژه (LicenseTool + presets.json + پنل)
setlocal
cd /d "%~dp0"
set INV=%~dp0..\inventory
echo == 1/3 ساخت LicenseTool ==
dotnet build "%INV%\tools\LicenseTool\LicenseTool.csproj" -c Release -v:q || goto :err
echo == 2/3 کپی presets.json ==
copy /Y "%INV%\tools\LicenseTool\presets.json" "%INV%\tools\LicenseTool\bin\Release\net8.0\presets.json" >nul
echo == 3/3 ساخت پنل ==
dotnet build SupportPanel.csproj -c Release -v:q || goto :err
echo.
echo آماده است. جفت‌کلید بسازید و مسیرها را در appsettings.json بگذارید؛ سپس run.cmd
goto :eof
:err
echo خطا در آماده‌سازی.
exit /b 1
