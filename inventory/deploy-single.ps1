# deploy-single.ps1
# استقرار تک‌سروره در ویندوز: پابلیش کلاینت Blazor و کپی آن در wwwroot مربوط به API
# بعد از اجرای این اسکریپت، فقط کافی است API را اجرا کنید و مرورگر را روی http://localhost:5100 باز کنید.
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host "==> Publishing Blazor client..." -ForegroundColor Cyan
dotnet publish src/Inventory.Client -c Release -o publish/client
if ($LASTEXITCODE -ne 0) { throw "Inventory client publish failed" }

Write-Host "==> Publishing RADIS-HR V019 client..." -ForegroundColor Cyan
dotnet publish modules/RadisHr/src/RadisHr.Client -c Release -o publish/radis-hr
if ($LASTEXITCODE -ne 0) { throw "RADIS-HR client publish failed" }

Write-Host "==> Copying static files into src/Inventory.Api/wwwroot ..." -ForegroundColor Cyan
$dest = Join-Path $PSScriptRoot "src/Inventory.Api/wwwroot"
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
New-Item -ItemType Directory -Path $dest | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot "publish/client/wwwroot/*") -Destination $dest -Recurse -Force
$radisDest = Join-Path $dest "radis-hr"
New-Item -ItemType Directory -Path $radisDest | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot "publish/radis-hr/wwwroot/*") -Destination $radisDest -Recurse -Force

Write-Host ""
Write-Host "Done!" -ForegroundColor Green
Write-Host "Now run:" -ForegroundColor White
Write-Host "    cd src\Inventory.Api" -ForegroundColor Yellow
Write-Host "    dotnet run" -ForegroundColor Yellow
Write-Host "Then open in browser:" -ForegroundColor White
Write-Host "    http://localhost:5100" -ForegroundColor Cyan
