# Single Inventory + HR host, using SQLite for local/demo use without SQL Server.
# Requires .NET 8 SDK. Default initial Inventory login: admin / admin (change it).
$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    & (Join-Path $PSScriptRoot "deploy-single.ps1")

    $env:Database__Provider = "Sqlite"
    $env:ConnectionStrings__Default = "Data Source=inventory.db"
    $env:ASPNETCORE_ENVIRONMENT = "Production"

    Write-Host "Open http://localhost:5100 - HR is in the main menu (/hr)." -ForegroundColor Green
    Write-Host "Press Ctrl+C to stop." -ForegroundColor Yellow
    Set-Location (Join-Path $PSScriptRoot "src/Inventory.Api")
    dotnet run --no-launch-profile
    if ($LASTEXITCODE -ne 0) { throw "Inventory API stopped with an error. See the log above." }
}
finally { Pop-Location }
