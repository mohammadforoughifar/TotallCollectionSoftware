<#
.SYNOPSIS
    تست Update-App.ps1 روی pwsh/لینوکس با فرمان‌های شبیه‌سازی‌شده.

.DESCRIPTION
    هر سناریو یک «دنیای» مستقل می‌سازد: پوشهٔ بستهٔ تازه، پوشهٔ نصب با فایل‌های قدیمی،
    و پوشهٔ داده با دیتابیس فایلی. فرمان‌های بیرونی (sqlcmd/appcmd/icacls/health) با
    فایل JSON شبیه‌سازی می‌شوند تا هیچ‌چیز واقعی تغییر نکند.

    اجرا:  pwsh -NoProfile -File tests/update/Test-UpdateApp.ps1
    خروج:  0 = همهٔ بررسی‌ها موفق، 1 = شکست
#>
[CmdletBinding()]
param(
    [string]$ScriptPath = "",
    [switch]$KeepTemp
)

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ScriptPath)) {
    $ScriptPath = Join-Path (Join-Path (Join-Path $root "..") "..") "tools/update/Update-App.ps1"
}
$ScriptPath = (Resolve-Path -LiteralPath $ScriptPath).Path

$script:Passes = 0
$script:Fails = 0
$script:Failures = New-Object System.Collections.ArrayList

function Check {
    param([bool]$Ok, [string]$Message)
    if ($Ok) { $script:Passes++; Write-Host ("PASS: " + $Message) -ForegroundColor Green; return }
    $script:Fails++
    [void]$script:Failures.Add($Message)
    Write-Host ("FAIL: " + $Message) -ForegroundColor Red
}

function Section { param([string]$Title) Write-Host ("`n=== " + $Title + " ===") -ForegroundColor Cyan }

function New-World {
    <#
        یک محیط آزمایش می‌سازد و مسیرها را برمی‌گرداند.
        نصب فعلی: Inventory.Api.exe + App_Data + License + logs + uploads + appsettings.json
        بسته:     فایل‌های تازه + package-info.json (+ appsettings تله برای بررسی جایگزینی نشدن)
    #>
    param([string]$Name)

    $base = Join-Path ([System.IO.Path]::GetTempPath()) ("totall-upd-" + $Name + "-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
    $app = Join-Path $base "app"
    $pkg = Join-Path $base "pkg"
    $data = Join-Path $base "data"

    foreach ($dir in @($app, $pkg, $data,
                      (Join-Path $app "App_Data"), (Join-Path $app "License"), (Join-Path $app "logs"),
                      (Join-Path $app "uploads"), (Join-Path $data "License"), (Join-Path $data "logs"),
                      (Join-Path $data "License/grants"))) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    Set-Content -LiteralPath (Join-Path $app "Inventory.Api.exe") -Value "old-binary" -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $app "appsettings.json") -Value '{"old":"customer-config"}' -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $app "App_Data/customer.db") -Value "customer-data" -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $app "License/installation.id") -Value "install-1" -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $app "uploads/doc.pdf") -Value "customer-upload" -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $app "logs/app-2026.log") -Value "old-log" -Encoding ASCII

    Set-Content -LiteralPath (Join-Path $pkg "Inventory.Api.exe") -Value "new-binary" -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $pkg "Vendor.Licensing.dll") -Value "new-dll" -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $pkg "appsettings.json") -Value '{"new":"vendor-config"}' -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $pkg "app_offline.htm") -Value "vendor-offline" -Encoding ASCII
    New-Item -ItemType Directory -Path (Join-Path $pkg "wwwroot") -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $pkg "wwwroot/app.css") -Value "new-css" -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $pkg "package-info.json") -Value (
        '{"version":"2026.11.14","builtAt":"2026-11-14T08:00:00Z","appPoolName":"TotallCollection"}') -Encoding UTF8

    # دیتابیس فایلی (حالت File) و لایسنس معتبر
    Set-Content -LiteralPath (Join-Path $data "totall.db") -Value "db-old" -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $data "License/grants/customer.lic") -Value "license" -Encoding ASCII

    return [pscustomobject]@{ Base = $base; App = $app; Pkg = $pkg; Data = $data }
}

function Invoke-Update {
    param(
        [pscustomobject]$World,
        [hashtable]$Mock = @{},
        [string[]]$ExtraArgs = @()
    )

    $mockPath = Join-Path $World.Base ("mock-" + [Guid]::NewGuid().ToString("N").Substring(0, 6) + ".json")
    $Mock | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $mockPath -Encoding UTF8
    foreach ($key in $Mock.Keys) {
        $value = $Mock[$key]
        # ConvertTo-Json ممکن است آرایهٔ تک‌عضوی را اسکالر کند؛ یکدست می‌کنیم.
    }

    $argsList = @(
        "-NoProfile", "-File", $ScriptPath,
        "-PackagePath", $World.Pkg,
        "-AppPath", $World.App,
        "-DataRoot", $World.Data,
        "-DbProvider", "File",
        "-LicenseGate", "File",
        "-MockCommands", $mockPath,
        "-HealthRetries", "2",
        "-HealthRetrySeconds", "1"
    )
    $argsList = $argsList + $ExtraArgs

    $output = & (Get-Process -Id $PID).Path @argsList 2>&1 | Out-String
    $code = $LASTEXITCODE
    if ($null -eq $code) { $code = 0 }
    return [pscustomobject]@{ Code = $code; Output = $output }
}

function Read-Text {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return "" }
    return (Get-Content -LiteralPath $Path -Raw)
}

# ============================================================
Section "۱) به‌روزرسانی موفق: کپی درست، داده‌های مشتری دست‌نخورده، پشتیبان ساخته شد"
# ============================================================
$world = New-World "ok"
$result = Invoke-Update -World $world -Mock @{ "update-database" = @(0); "health" = @(0) }

Check ($result.Code -eq 0) ("به‌روزرسانی موفق با کد 0 (واقعی: " + $result.Code + ")")
Check ((Read-Text (Join-Path $world.App "Inventory.Api.exe")).Trim() -eq "new-binary") "فایل اجرایی تازه کپی شد"
Check (Test-Path -LiteralPath (Join-Path $world.App "Vendor.Licensing.dll")) "فایل‌های تازهٔ دیگر هم کپی شدند"
Check (Test-Path -LiteralPath (Join-Path $world.App "wwwroot/app.css")) "زیرپوشه‌ها هم کپی می‌شوند"
Check ((Read-Text (Join-Path $world.App "appsettings.json")) -like '*customer-config*') "appsettings مشتری جایگزین نشد"
Check ((Read-Text (Join-Path $world.App "App_Data/customer.db")).Trim() -eq "customer-data") "App_Data دست‌نخورده ماند"
Check ((Read-Text (Join-Path $world.App "uploads/doc.pdf")).Trim() -eq "customer-upload") "uploads دست‌نخورده ماند"
Check ((Read-Text (Join-Path $world.App "logs/app-2026.log")).Trim() -eq "old-log") "logs دست‌نخورده ماند"
Check ((Read-Text (Join-Path $world.App "License/installation.id")).Trim() -eq "install-1") "پوشهٔ License دست‌نخورده ماند"
Check (-not (Test-Path -LiteralPath (Join-Path $world.App "app_offline.htm"))) "app_offline.htm پس از موفقیت برداشته شد"
Check (-not (Test-Path -LiteralPath (Join-Path $world.Data "update.lock"))) "فایل قفل پس از پایان پاک شد"
$appBackups = @(Get-ChildItem -LiteralPath (Join-Path $world.Data "backups/app") -Directory -ErrorAction SilentlyContinue)
Check ($appBackups.Count -eq 1) ("یک پشتیبان برنامه ساخته شد (واقعی: " + $appBackups.Count + ")")
Check (Test-Path -LiteralPath (Join-Path $world.Data "backups/db")) "پوشهٔ پشتیبان دیتابیس ساخته شد"
$logs = @(Get-ChildItem -LiteralPath (Join-Path $world.Data "logs") -Filter "update-*.log" -ErrorAction SilentlyContinue)
Check ($logs.Count -ge 1) "لاگ به‌روزرسانی در ProgramData\logs نوشته شد"
if (-not $KeepTemp) { Remove-Item -LiteralPath $world.Base -Recurse -Force -ErrorAction SilentlyContinue }

# ============================================================
Section "۲) دروازهٔ لایسنس: رد ⇒ صفر تغییر"
# ============================================================
$world = New-World "lic"
$result = Invoke-Update -World $world -Mock @{ "license-gate" = @("invalid"); "update-database" = @(0); "health" = @(0) }

Check ($result.Code -eq 20) ("لایسنس نامعتبر ⇒ کد 20 (واقعی: " + $result.Code + ")")
Check ((Read-Text (Join-Path $world.App "Inventory.Api.exe")).Trim() -eq "old-binary") "برنامه کپی نشد (صفر تغییر)"
Check (-not (Test-Path -LiteralPath (Join-Path $world.App "Vendor.Licensing.dll"))) "هیچ فایل تازه‌ای اضافه نشد"
Check (-not (Test-Path -LiteralPath (Join-Path $world.Data "backups"))) "حتی پشتیبان هم ساخته نشد (قبل از هر تغییری)"
if (-not $KeepTemp) { Remove-Item -LiteralPath $world.Base -Recurse -Force -ErrorAction SilentlyContinue }

# ============================================================
Section "۳) بستهٔ ناقص ⇒ کد 10، بدون تغییر"
# ============================================================
$world = New-World "pkg"
Remove-Item -LiteralPath (Join-Path $world.Pkg "package-info.json") -Force
$result = Invoke-Update -World $world -Mock @{ "update-database" = @(0); "health" = @(0) }
Check ($result.Code -eq 10) ("نبود package-info.json ⇒ کد 10 (واقعی: " + $result.Code + ")")
Check ((Read-Text (Join-Path $world.App "Inventory.Api.exe")).Trim() -eq "old-binary") "برنامه دست‌نخورده ماند"
if (-not $KeepTemp) { Remove-Item -LiteralPath $world.Base -Recurse -Force -ErrorAction SilentlyContinue }

# ============================================================
Section "۴) شکست به‌روزرسانی دیتابیس: بازگردانی برنامه، بدون بازگردانی دیتابیس (پیش‌فرض)"
# ============================================================
$world = New-World "dbfail"
$result = Invoke-Update -World $world -Mock @{ "update-database" = @(1); "health" = @(0) }
Check ($result.Code -eq 70) ("شکست دیتابیس ⇒ کد 70 (واقعی: " + $result.Code + ")")
Check ((Read-Text (Join-Path $world.App "Inventory.Api.exe")).Trim() -eq "old-binary") "پوشهٔ برنامه از پشتیبان بازگردانده شد"
Check (-not (Test-Path -LiteralPath (Join-Path $world.App "app_offline.htm"))) "app_offline.htm برداشته شد تا سرویس بالا بیاید"
Check ((Read-Text (Join-Path $world.Data "totall.db")).Trim() -eq "db-old") "دیتابیس دست‌نخورده ماند (بدون RestoreDbOnFailure)"
if (-not $KeepTemp) { Remove-Item -LiteralPath $world.Base -Recurse -Force -ErrorAction SilentlyContinue }

# ============================================================
Section "۵) شکست سلامت: کد 80 و بازگردانی برنامه"
# ============================================================
$world = New-World "health"
$result = Invoke-Update -World $world -Mock @{ "update-database" = @(0); "health" = @(1, 1, 1) }
Check ($result.Code -eq 80) ("سلامت ناموفق ⇒ کد 80 (واقعی: " + $result.Code + ")")
Check ((Read-Text (Join-Path $world.App "Inventory.Api.exe")).Trim() -eq "old-binary") "برنامه از پشتیبان بازگردانده شد"
if (-not $KeepTemp) { Remove-Item -LiteralPath $world.Base -Recurse -Force -ErrorAction SilentlyContinue }

# ============================================================
Section "۶) قفل اجرای هم‌زمان ⇒ کد 91"
# ============================================================
$world = New-World "lock"
Set-Content -LiteralPath (Join-Path $world.Data "update.lock") -Value "pid=9999" -Encoding UTF8
$result = Invoke-Update -World $world -Mock @{ "update-database" = @(0); "health" = @(0) }
Check ($result.Code -eq 91) ("قفل موجود ⇒ کد 91 (واقعی: " + $result.Code + ")")
Check ((Read-Text (Join-Path $world.App "Inventory.Api.exe")).Trim() -eq "old-binary") "هیچ تغییری انجام نشد"
if (-not $KeepTemp) { Remove-Item -LiteralPath $world.Base -Recurse -Force -ErrorAction SilentlyContinue }

# ============================================================
Section "۷) نگه‌داری پشتیبان: حداکثر ۳ نسخهٔ برنامه، پشتیبان‌های دیتابیس هرگز پاک نمی‌شوند"
# ============================================================
$world = New-World "retention"
$appBackupRoot = Join-Path $world.Data "backups/app"
$dbBackupRoot = Join-Path $world.Data "backups/db"
New-Item -ItemType Directory -Path $appBackupRoot -Force | Out-Null
New-Item -ItemType Directory -Path $dbBackupRoot -Force | Out-Null
foreach ($name in @("20200101-000000", "20200102-000000", "20200103-000000", "20200104-000000")) {
    New-Item -ItemType Directory -Path (Join-Path $appBackupRoot $name) -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $dbBackupRoot "db-$name.bak") -Value "old-db-backup" -Encoding ASCII
}
$result = Invoke-Update -World $world -Mock @{ "update-database" = @(0); "health" = @(0) }
Check ($result.Code -eq 0) ("به‌روزرسانی موفق با پشتیبان‌های قدیمی (واقعی: " + $result.Code + ")")
$appBackups = @(Get-ChildItem -LiteralPath $appBackupRoot -Directory)
Check ($appBackups.Count -eq 3) ("پشتیبان برنامه به ۳ نسخه محدود شد (واقعی: " + $appBackups.Count + ")")
$dbBackups = @(Get-ChildItem -LiteralPath $dbBackupRoot -File)
$oldDbKept = 0
foreach ($name in @("20200101-000000", "20200102-000000", "20200103-000000", "20200104-000000")) {
    if (Test-Path -LiteralPath (Join-Path $dbBackupRoot ("db-" + $name + ".bak"))) { $oldDbKept++ }
}
Check ($oldDbKept -eq 4) ("هر ۴ پشتیبان قدیمی دیتابیس باقی ماندند (واقعی: " + $oldDbKept + ")")
Check ($dbBackups.Count -ge 5) ("پشتیبان تازهٔ دیتابیس هم اضافه شد (کل: " + $dbBackups.Count + ")")
if (-not $KeepTemp) { Remove-Item -LiteralPath $world.Base -Recurse -Force -ErrorAction SilentlyContinue }

# ============================================================
Section "۸) سازگاری با Windows PowerShell 5.1 و پوستهٔ نازک"
# ============================================================
$scriptText = Get-Content -LiteralPath $ScriptPath -Raw
# فقط «کد» سنجیده می‌شود، نه توضیحات: خودِ مستندات این واژه‌ها را نام می‌برند.
$noBlock = [regex]::Replace($scriptText, '(?s)<#.*?#>', '')
$codeLines = @($noBlock -split "`r?`n" | Where-Object { -not $_.TrimStart().StartsWith("#") })
$codeText = $codeLines -join "`n"

Check (-not ($codeText -match '\?\?')) "بدون عملگر ?? (سازگار با PS 5.1)"
Check (-not ($codeText -match '&&|\|\|')) "بدون && و ||"
Check (-not ($codeText -match 'ForEach-Object\s+-Parallel')) "بدون ForEach-Object -Parallel"
Check (-not ($codeText -match '\?\s')) "بدون عملگر سه‌گانهٔ ?:"
Check ($codeText -match 'RestoreDbOnFailure') "سوئیچ بازگردانی دیتابیس وجود دارد"
Check ($codeText -match 'BACKUP DATABASE') "پشتیبان‌گیری با BACKUP DATABASE انجام می‌شود"
Check (-not ($codeText -match 'COMPRESSION')) "پشتیبان دیتابیس بدون COMPRESSION است"
Check (-not ($codeText -match '/MIR')) "کپی هرگز /MIR نیست"
Check ($codeText -match 'app_offline\.htm') "app_offline.htm نوشته/برداشته می‌شود"

$batPath = Join-Path (Split-Path -Parent $ScriptPath) "update.bat"
Check (Test-Path -LiteralPath $batPath) "update.bat کنار اسکریپت هست"
if (Test-Path -LiteralPath $batPath) {
    $batText = Get-Content -LiteralPath $batPath -Raw
    $batLines = @($batText -split "`n" | Where-Object { $_.Trim() -ne "" -and -not $_.Trim().StartsWith("REM") })
    Check ($batLines.Count -le 12) ("update.bat نازک است (" + $batLines.Count + " خط مؤثر)")
    Check ($batText -match 'Update-App\.ps1') "update.bat فقط Update-App.ps1 را صدا می‌زند"
    Check ($batText -match 'exit /b') "update.bat کد خروج اسکریپت را برمی‌گرداند"
}

# ============================================================
Section "۹) بستهٔ انتشار: Update-App.ps1، update.bat و package-info.json"
# ============================================================
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $root "../..")).Path
$publishBat = Join-Path $repoRoot "publish-server.bat"
Check (Test-Path -LiteralPath $publishBat) "publish-server.bat پیدا شد"
if (Test-Path -LiteralPath $publishBat) {
    $publishText = Get-Content -LiteralPath $publishBat -Raw
    Check ($publishText -match 'Update-App\.ps1') "بسته شامل Update-App.ps1 است"
    Check ($publishText -match 'update\.bat') "بسته شامل update.bat است"
    Check ($publishText -match 'package-info\.json') "بسته package-info.json را می‌سازد"
    Check ($publishText -match 'LicenseCheck') "بسته LicenseCheck را منتشر می‌کند"
    Check (-not ($publishText -match 'LicenseTool')) "ابزار صدور لایسنس در بسته نیست"
}

# ============================================================
Write-Host ""
Write-Host ("═" * 96)
if ($script:Fails -eq 0) {
    Write-Host ("✅ همهٔ " + $script:Passes + " بررسی تست به‌روزرسانی موفق بود.") -ForegroundColor Green
    exit 0
}
Write-Host ("⛔ " + $script:Fails + " بررسی شکست خورد از " + ($script:Passes + $script:Fails) + ":") -ForegroundColor Red
foreach ($item in $script:Failures) { Write-Host ("   • " + $item) -ForegroundColor Red }
exit 1
