# Clean stale MSBuild outputs, then build Shared first so the real error is visible.
# Close Visual Studio/IIS Express before running. Never touches databases/uploads/keys.
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)
$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw ".NET 8 SDK is required (install the SDK, not only the runtime)."
    }
    $sdks = dotnet --list-sdks
    if ($LASTEXITCODE -ne 0 -or -not ($sdks -match '^8\.0\.')) {
        throw ".NET 8 SDK was not found. Install it and use Visual Studio 2022 17.8 or newer."
    }

    Write-Host "Cleaning generated bin/obj folders..." -ForegroundColor Yellow
    # Enumerate first and remove deepest paths first. Do not silently ignore locked outputs.
    $outputs = Get-ChildItem -Path src, tests -Directory -Recurse -Force |
        Where-Object { $_.Name -in @("bin", "obj") } |
        Sort-Object { $_.FullName.Length } -Descending
    foreach ($output in $outputs) {
        if (Test-Path -LiteralPath $output.FullName) {
            Remove-Item -LiteralPath $output.FullName -Recurse -Force
        }
    }

    dotnet restore Inventory.sln
    if ($LASTEXITCODE -ne 0) { throw "Restore failed. Fix the first NuGet error above before rebuilding." }

    dotnet build src/Inventory.Shared/Inventory.Shared.csproj -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Inventory.Shared failed. Fix the first compiler error above; CS0006 is a downstream symptom." }

    dotnet build Inventory.sln -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Solution build failed. See the first error above." }

    Write-Host "Build completed successfully ($Configuration)." -ForegroundColor Green
}
finally { Pop-Location }
