@echo off
REM =====================================================================
REM  ساخت کامل پابلیش (کلاینت + API - یک‌فایلی win-x64) برای ویندوز سرور
REM  اجرا: این فایل را در ریشهٔ مخزن (کنار پوشهٔ inventory) بگذارید و دابل‌کلیک کنید.
REM  پیش‌نیاز: .NET SDK 8.0 روی سیستم (dotnet --version باید ۸.x بدهد)
REM  خروجی: پوشهٔ publish-win (کنار همین فایل) شامل Inventory.Api.exe و run-server.bat
REM =====================================================================
setlocal
set "ROOT=%~dp0"
set "API=%ROOT%inventory\src\Inventory.Api"
set "CLIENT=%ROOT%inventory\src\Inventory.Client"
set "OUT=%ROOT%publish-win"
set "TMP=%ROOT%_client-tmp"

echo [1/5] بازیابی بسته‌های کلاینت...
dotnet restore "%CLIENT%\Inventory.Client.csproj" || goto err

echo [2/5] پابلیش کلاینت (Blazor WASM)...
if exist "%TMP%" rmdir /s /q "%TMP%"
dotnet publish "%CLIENT%\Inventory.Client.csproj" -c Release -o "%TMP%" --no-restore || goto err

echo [3/5] کپی خروجی کلاینت به wwwroot سرور...
xcopy /s /y /q "%TMP%\wwwroot\*" "%API%\wwwroot\" >nul || goto err

echo [4/5] بازیابی بسته‌های API (win-x64)...
dotnet restore "%API%\Inventory.Api.csproj" -r win-x64 || goto err

echo [5/5] پابلیش API (self-contained، تک‌فایل)...
if exist "%OUT%" rmdir /s /q "%OUT%"
dotnet publish "%API%\Inventory.Api.csproj" -c Release -r win-x64 --self-contained -o "%OUT%" --no-restore || goto err

copy /y "%ROOT%update-database.bat" "%OUT%\update-database.bat" >nul
copy /y "%ROOT%run-server.bat" "%OUT%\run-server.bat" >nul
copy /y "%ROOT%start.bat"      "%OUT%\start.bat"      >nul
copy /y "%ROOT%stop.bat"       "%OUT%\stop.bat"       >nul
if exist "%TMP%" rmdir /s /q "%TMP%"

echo.
echo ======================================================
echo  DONE! پوشهٔ خروجی: %OUT%
echo  آن را به سرور منتقل کنید و start.bat را اجرا کنید (stop.bat = توقف سرور).
echo  آدرس: http://192.168.30.104:8181
echo ======================================================
endlocal
pause
exit /b 0

:err
echo.
echo *** خطا در ساخت پابلیش — پیام بالا را بخوانید ***
endlocal
pause
exit /b 1
