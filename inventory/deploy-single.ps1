# Single-host deployment: HR is now part of Inventory.Client.
$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    Write-Host "==> Publishing Inventory client (including HR)..." -ForegroundColor Cyan
    dotnet publish src/Inventory.Client -c Release -o publish/client
    if ($LASTEXITCODE -ne 0) { throw "Inventory client publish failed. See the first build error above." }

    $dest = Join-Path $PSScriptRoot "src/Inventory.Api/wwwroot"
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    # Never delete wwwroot: it also contains runtime uploads and encryption keys.
    foreach ($generated in @("_framework", "radis-hr")) {
        $path = Join-Path $dest $generated
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
    Copy-Item -Path (Join-Path $PSScriptRoot "publish/client/wwwroot/*") -Destination $dest -Recurse -Force

    Write-Host "Done. Existing uploads and SecureFiles were preserved." -ForegroundColor Green
    Write-Host "Run: cd src\Inventory.Api; dotnet run" -ForegroundColor Yellow
    Write-Host "Open: http://localhost:5100 (HR: /hr)" -ForegroundColor Cyan
}
finally { Pop-Location }
