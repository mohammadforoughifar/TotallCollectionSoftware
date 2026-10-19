#Requires -Version 5.1
<#
.SYNOPSIS
    Diagnoses and fixes Windows MAX_PATH (260 characters) build failures for this repository.

.DESCRIPTION
    Long repository paths break the .NET build on Windows with errors such as:

        MSB4018  GenerateStaticWebAssetsPropsFile task failed unexpectedly
        DirectoryNotFoundException: ...\obj\Debug\net8.0\staticwebassets\msbuild.Inventory.Api.*.props

        Failed to convert '...Microsoft.Extensions.DependencyInjection.Abstractions.dll'
        to webcil: Could not find a part of the path '...\obj\Debug\net8.0\tmp-webcil\*.wasm'

    Both are the same root cause: the generated path exceeds MAX_PATH (260 chars).
    This script reports the offending paths and offers two supported fixes:

      1) -BuildRoot D:\tcs-build
         Redirects obj/bin to a short path (sets TCS_BUILD_ROOT). No admin needed.
         Use -Persist to store it for the current user (restart Visual Studio after).

      2) -EnableLongPaths
         Enables the Windows long-path policy and 'git config core.longpaths'.
         Requires an elevated PowerShell; restart the machine/VS afterwards.

.PARAMETER BuildRoot
    Short path used for obj/bin, e.g. D:\tcs-build. Created if missing.

.PARAMETER Persist
    With -BuildRoot, stores TCS_BUILD_ROOT as a user environment variable (setx).

.PARAMETER EnableLongPaths
    Enables HKLM LongPathsEnabled=1 and git core.longpaths=true. Requires admin.

.PARAMETER Clean
    Deletes every bin/obj folder of the solution so the next build starts fresh.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File inventory\tools\fix-windows-long-path.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File inventory\tools\fix-windows-long-path.ps1 -BuildRoot D:\tcs-build -Persist -Clean
#>
[CmdletBinding()]
param(
    [string] $BuildRoot,
    [switch] $Persist,
    [switch] $EnableLongPaths,
    [switch] $Clean
)

$ErrorActionPreference = 'Stop'

# inventory/tools -> repository root
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

Write-Host ''
Write-Host '=== MAX_PATH diagnosis ===' -ForegroundColor Cyan
Write-Host ("Repository root : {0}  ({1} chars)" -f $repoRoot, $repoRoot.Length)

# Real files the SDK generates inside obj, per project.
$probes = @(
    @{ Name = 'Inventory.Api  staticwebassets props'
       Path = Join-Path $repoRoot 'inventory\src\Inventory.Api\obj\Debug\net8.0\staticwebassets\msbuild.Inventory.Api.Microsoft.AspNetCore.StaticWebAssets.props' },
    @{ Name = 'Inventory.Client tmp-webcil wasm'
       Path = Join-Path $repoRoot 'inventory\src\Inventory.Client\obj\Debug\net8.0\tmp-webcil\Microsoft.Extensions.DependencyInjection.Abstractions.wasm' },
    @{ Name = 'Inventory.Client webcil (longest sample)'
       Path = Join-Path $repoRoot 'inventory\src\Inventory.Client\obj\Debug\net8.0\tmp-webcil\Microsoft.Extensions.DependencyInjection.Abstractions.wasm.bak' }
)

$over = @()
foreach ($p in $probes) {
    $len = $p.Path.Length
    $bad = $len -ge 260
    if ($bad) { $over += $p }
    $status = if ($bad) { 'OVER 260' } else { 'ok' }
    $color = if ($bad) { 'Red' } else { 'DarkGray' }
    Write-Host ("  {0,4}  {1,-9} {2}" -f $len, $status, $p.Name) -ForegroundColor $color
}

if ($over.Count -gt 0) {
    Write-Host ''
    Write-Host 'Result: the build WILL fail on Windows for the paths marked OVER 260.' -ForegroundColor Red
} else {
    Write-Host ''
    Write-Host 'Result: no MAX_PATH problem detected for these paths.' -ForegroundColor Green
}

# ---------------------------------------------------------------- fix 1
if ($BuildRoot) {
    Write-Host ''
    Write-Host '=== Fix 1: redirect obj/bin to a short path ===' -ForegroundColor Cyan

    $BuildRoot = $BuildRoot.TrimEnd('\', '/')
    if ($BuildRoot.Length -gt 60) {
        Write-Warning "BuildRoot is $($BuildRoot.Length) chars; keep it under 60 for safety."
    }
    if (-not (Test-Path -LiteralPath $BuildRoot)) {
        New-Item -ItemType Directory -Path $BuildRoot -Force | Out-Null
        Write-Host "Created $BuildRoot"
    }

    $env:TCS_BUILD_ROOT = $BuildRoot
    Write-Host "TCS_BUILD_ROOT = $BuildRoot (current session)"

    if ($Persist) {
        [Environment]::SetEnvironmentVariable('TCS_BUILD_ROOT', $BuildRoot, 'User')
        Write-Host 'Stored for the current user. Close and reopen Visual Studio / the terminal.' -ForegroundColor Yellow
    } else {
        Write-Host 'This PowerShell session only. Re-run with -Persist to make it permanent.' -ForegroundColor DarkGray
    }

    Write-Host ''
    Write-Host 'Then build with:' -ForegroundColor Green
    Write-Host '  dotnet build inventory\Inventory.sln -c Debug'
}

# ---------------------------------------------------------------- fix 2
if ($EnableLongPaths) {
    Write-Host ''
    Write-Host '=== Fix 2: enable Windows long paths ===' -ForegroundColor Cyan

    $isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) {
        Write-Warning 'Run PowerShell as Administrator to change HKLM, or use -BuildRoot instead.'
    } else {
        $key = 'HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem'
        $current = (Get-ItemProperty -Path $key -Name LongPathsEnabled -ErrorAction SilentlyContinue).LongPathsEnabled
        if ($current -ne 1) {
            Set-ItemProperty -Path $key -Name LongPathsEnabled -Value 1 -Type DWord
            Write-Host 'LongPathsEnabled = 1 (restart required).' -ForegroundColor Green
        } else {
            Write-Host 'LongPathsEnabled is already 1.'
        }
        Write-Host 'Restart Windows and Visual Studio, then rebuild.' -ForegroundColor Yellow
    }

    try {
        git config --system core.longpaths true
        Write-Host 'git core.longpaths = true' -ForegroundColor Green
    } catch {
        Write-Warning "Could not set git core.longpaths: $($_.Exception.Message)"
    }
}

# ---------------------------------------------------------------- clean
if ($Clean) {
    Write-Host ''
    Write-Host '=== Cleaning bin/obj ===' -ForegroundColor Cyan
    $targets = @(Get-ChildItem -Path (Join-Path $repoRoot 'inventory\src'), (Join-Path $repoRoot 'inventory\tests') `
        -Directory -Recurse -Include 'bin', 'obj' -ErrorAction SilentlyContinue)
    foreach ($d in $targets) {
        try {
            Remove-Item -LiteralPath $d.FullName -Recurse -Force
            Write-Host "  removed $($d.FullName)"
        } catch {
            Write-Warning "  could not remove $($d.FullName): $($_.Exception.Message)"
        }
    }
    if ($targets.Count -eq 0) { Write-Host '  nothing to clean.' }
}

# ---------------------------------------------------------------- advice
Write-Host ''
Write-Host '=== Recommended ===' -ForegroundColor Cyan
if ($over.Count -gt 0 -and -not $BuildRoot -and -not $EnableLongPaths) {
    Write-Host '  Shortest path is to move/rename the repository, e.g.:' -ForegroundColor Yellow
    Write-Host '    D:\src\tcs'
    Write-Host '  or redirect the build output (no admin):' -ForegroundColor Yellow
    Write-Host '    .\fix-windows-long-path.ps1 -BuildRoot D:\tcs-build -Persist -Clean'
    Write-Host '  or enable long paths (admin):' -ForegroundColor Yellow
    Write-Host '    .\fix-windows-long-path.ps1 -EnableLongPaths'
    Write-Host ''
    Write-Host '  Full guide: WINDOWS-LONG-PATH-FIX-FA.md' -ForegroundColor DarkGray
} else {
    Write-Host '  Nothing else to do. If errors persist, run with -Clean and rebuild.' -ForegroundColor Green
}
