<#
.SYNOPSIS
    به‌روزرسانی امن نصب TotallCollection (گام ۶).

.DESCRIPTION
    این اسکریپت یک به‌روزرسانی را «یا کامل انجام می‌دهد یا هیچ‌چیز را عوض نمی‌کند».
    ترتیب کار عمداً این است: همهٔ پیش‌بررسی‌ها و دروازهٔ لایسنس **قبل** از هر تغییری
    انجام می‌شوند؛ اگر یکی رد شود، صفر تغییر روی دیسک و دیتابیس باقی می‌ماند.

    مراحل:
      ۱) پیش‌بررسی (مسیرها، بستهٔ نصب، فضای دیسک، قفل اجرای هم‌زمان)
      ۲) دروازهٔ لایسنس (رد ⇒ کد ۲۰، بدون هیچ تغییری)
      ۳) پشتیبان‌گیری: BACKUP DATABASE (بدون COMPRESSION) + کپی پوشهٔ برنامه
      ۴) app_offline.htm و توقف App Pool
      ۵) کپی فایل‌های تازه (هرگز /MIR؛ App_Data/License/logs/uploads و appsettings حفظ می‌شوند)
      ۶) --update-database با امکان بازگردانی دیتابیس در صورت شکست
      ۷) ACL پوشه‌های ProgramData (فقط ویندوز)
      ۸) برداشتن app_offline.htm، شروع App Pool، بررسی سلامت با تلاش مجدد
      ۹) در صورت شکست سلامت: بازگردانی پوشهٔ برنامه از پشتیبان

    لاگ: <ProgramData>\Totall\logs\update-<timestamp>.log

.PARAMETER MockCommands
    مسیر یک فایل JSON برای «شبیه‌سازی فرمان‌ها» (تست روی لینوکس). ساختار:
        { "sqlcmd": [0, 0], "update-database": [1] }
    هر کلید، صف نتیجهٔ فراخوانی‌های همان فرمان است (۰ = موفق).

.NOTES
    سازگار با Windows PowerShell 5.1: بدون `??`، `?:`، `&&`، `||` و بدون
    ForEach-Object -Parallel.
#>
[CmdletBinding()]
param(
    # پوشهٔ بستهٔ تازه (خروجی publish-server.bat)
    [Parameter(Mandatory = $true)][string]$PackagePath,

    # پوشهٔ نصب فعلی برنامه (جایی که Inventory.Api.exe هست)
    [Parameter(Mandatory = $true)][string]$AppPath,

    # ریشهٔ داده‌ها: پیش‌فرض %ProgramData%\Totall
    [string]$DataRoot = "",

    # آدرس بررسی سلامت؛ پیش‌فرض از package-info.json یا http://localhost/api/health
    [string]$HealthUrl = "",

    [ValidateRange(1, 60)][int]$HealthRetries = 10,
    [ValidateRange(1, 120)][int]$HealthRetrySeconds = 3,

    # نگه‌داری چند نسخهٔ پشتیبان برنامه (پشتیبان دیتابیس هرگز پاک نمی‌شود)
    [ValidateRange(1, 50)][int]$KeepAppBackups = 3,

    # در صورت شکست به‌هنگام به‌روزرسانی دیتابیس، دیتابیس از پشتیبان برگردد
    [switch]$RestoreDbOnFailure,

    # پشتیبان‌گیری را رد کن (فقط برای محیط آزمایش — در تولید استفاده نکنید)
    [switch]$SkipBackup,

    # دروازهٔ لایسنس: Health | Sql | File | Off
    [ValidateSet('Health', 'Sql', 'File', 'Off')][string]$LicenseGate = "Health",

    # مسیر فایل لایسنس/پوشهٔ grants برای دروازهٔ File (پیش‌فرض: <DataRoot>\License\grants)
    [string]$LicenseGrantsPath = "",

    # دیتابیس: SqlServer (پیش‌فرض) یا File (برای تست؛ SQLite)
    [ValidateSet('SqlServer', 'File')][string]$DbProvider = "SqlServer",

    # نام دیتابیس برای BACKUP/RESTORE
    [string]$DatabaseName = "TotallCollection",

    # رشتهٔ اتصال فقط از متغیر محیطی خوانده می‌شود (رمز در تاریخچهٔ شل نمی‌ماند)
    [string]$ConnectionStringEnvVar = "TOTALL_DB_CONN",

    # در تست: فایل JSON شبیه‌سازی فرمان‌ها
    [string]$MockCommands = "",

    # فقط گزارش کن چه می‌شد (هیچ تغییری نمی‌دهد)
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$startedAt = Get-Date

# ============================================================
#  کدهای خروج (قرارداد با update.bat و مستندات)
# ============================================================
$ExitCodes = @{
    Success          = 0
    ParamError       = 90
    PackageInvalid   = 10
    LicenseRejected  = 20
    PrecheckFailed   = 30
    BackupFailed     = 40
    StopAppFailed    = 50
    CopyFailed       = 60
    DbUpdateFailed   = 70
    HealthFailed     = 80
    AlreadyRunning   = 91
    Unexpected       = 95
}

# ============================================================
#  لاگ
# ============================================================
if ([string]::IsNullOrWhiteSpace($DataRoot)) {
    if ([string]::IsNullOrWhiteSpace($env:TOTALL_DATA_ROOT)) {
        $DataRoot = Join-Path $env:ProgramData "Totall"
    }
    else {
        $DataRoot = $env:TOTALL_DATA_ROOT
    }
}
$logDir = Join-Path $DataRoot "logs"
$script:LogFile = Join-Path $logDir ("update-" + $startedAt.ToString("yyyyMMdd-HHmmss") + ".log")
$script:LogLines = New-Object System.Collections.ArrayList

function Write-Log {
    param([string]$Message, [ValidateSet('INFO', 'WARN', 'ERROR', 'STEP')][string]$Level = 'INFO')
    $line = "{0} [{1}] {2}" -f (Get-Date).ToString("HH:mm:ss"), $Level, $Message
    [void]$script:LogLines.Add($line)
    $color = "Gray"
    if ($Level -eq 'WARN') { $color = "DarkYellow" }
    if ($Level -eq 'ERROR') { $color = "Red" }
    if ($Level -eq 'STEP') { $color = "Cyan" }
    Write-Host $line -ForegroundColor $color
}

function Save-Log {
    try {
        if (-not (Test-Path -LiteralPath $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
        $text = ($script:LogLines -join [Environment]::NewLine) + [Environment]::NewLine
        Set-Content -LiteralPath $script:LogFile -Value $text -Encoding UTF8
    }
    catch {
        Write-Host ("[WARN] نوشتن فایل لاگ ممکن نشد: " + $_.Exception.Message) -ForegroundColor DarkYellow
    }
}

function Exit-With {
    param([int]$Code, [string]$Message)
    if (-not [string]::IsNullOrWhiteSpace($Message)) { Write-Log $Message 'ERROR' }
    Write-Log ("پایان با کد " + $Code + " (کارکرد: " + [math]::Round(((Get-Date) - $startedAt).TotalSeconds, 1) + " ثانیه)") 'STEP'
    Save-Log
    exit $Code
}

# ============================================================
#  اجرای فرمان‌ها (قابل شبیه‌سازی)
# ============================================================
$script:MockTable = $null
$script:MockCursor = @{}

if (-not [string]::IsNullOrWhiteSpace($MockCommands)) {
    if (-not (Test-Path -LiteralPath $MockCommands)) {
        Write-Host ("فایل شبیه‌سازی فرمان‌ها پیدا نشد: " + $MockCommands) -ForegroundColor Red
        exit $ExitCodes.ParamError
    }
    $raw = Get-Content -LiteralPath $MockCommands -Raw
    $script:MockTable = $raw | ConvertFrom-Json
    Write-Host ("[شبیه‌سازی] فرمان‌ها از " + $MockCommands + " خوانده می‌شوند") -ForegroundColor DarkYellow
}

function Invoke-Step {
    <#
        یک فرمان بیرونی را اجرا می‌کند و کد خروجش را برمی‌گرداند.
        در حالت شبیه‌سازی، هیچ فرمانی اجرا نمی‌شود و نتیجه از فایل JSON می‌آید.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @(),
        [switch]$AllowFailure
    )

    $shown = $FilePath
    if ($Arguments.Count -gt 0) { $shown = $shown + " " + ($Arguments -join " ") }
    Write-Log ("اجرا: " + $shown)

    if ($null -ne $script:MockTable) {
        $queue = $script:MockTable.$Name
        if ($null -eq $queue) {
            Write-Log ("[شبیه‌سازی] فرمان «" + $Name + "» تعریف نشده؛ موفق فرض می‌شود.") 'WARN'
            return 0
        }
        $index = 0
        if ($script:MockCursor.ContainsKey($Name)) { $index = $script:MockCursor[$Name] }
        $code = 0
        if ($index -lt $queue.Count) { $code = [int]$queue[$index] }
        $script:MockCursor[$Name] = $index + 1
        Write-Log ("[شبیه‌سازی] " + $Name + " ⇒ کد " + $code) 'WARN'
        if ($code -ne 0 -and -not $AllowFailure) { Write-Log ("فرمان «" + $Name + "» با کد " + $code + " شکست خورد.") 'ERROR' }
        return $code
    }

    if ($DryRun) {
        Write-Log "[DryRun] فرمان اجرا نشد." 'WARN'
        return 0
    }

    try {
        & $FilePath @Arguments 2>&1 | ForEach-Object { Write-Log ("   " + $_) }
        $code = $LASTEXITCODE
        if ($null -eq $code) { $code = 0 }
        if ($code -ne 0 -and -not $AllowFailure) {
            Write-Log ("فرمان «" + $Name + "» با کد " + $code + " شکست خورد.") 'ERROR'
        }
        return $code
    }
    catch {
        Write-Log ("اجرای «" + $Name + "» ممکن نشد: " + $_.Exception.Message) 'ERROR'
        return 1
    }
}

# ============================================================
#  ۰) اعتبار پارامترها
# ============================================================
Write-Log ("به‌روزرسانی آغاز شد — بسته: " + $PackagePath + " | نصب: " + $AppPath) 'STEP'
Write-Log ("ریشهٔ داده: " + $DataRoot + " | دیتابیس: " + $DbProvider + "/" + $DatabaseName)

if (-not (Test-Path -LiteralPath $PackagePath)) {
    Exit-With $ExitCodes.PackageInvalid ("پوشهٔ بستهٔ نصب پیدا نشد: " + $PackagePath)
}
if (-not (Test-Path -LiteralPath $AppPath)) {
    Exit-With $ExitCodes.ParamError ("پوشهٔ نصب پیدا نشد: " + $AppPath)
}
$PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
$AppPath = (Resolve-Path -LiteralPath $AppPath).Path

if ($PackagePath.TrimEnd('\', '/') -eq $AppPath.TrimEnd('\', '/')) {
    Exit-With $ExitCodes.ParamError "پوشهٔ بسته و پوشهٔ نصب یکی هستند؛ بسته را در مسیر جداگانه باز کنید."
}

# ============================================================
#  ۱) پیش‌بررسی‌ها — هیچ تغییری نمی‌دهند
# ============================================================
Write-Log "مرحلهٔ ۱: پیش‌بررسی" 'STEP'

$lockFile = Join-Path $DataRoot "update.lock"
if (Test-Path -LiteralPath $lockFile) {
    $lockText = ""
    try { $lockText = (Get-Content -LiteralPath $lockFile -Raw).Trim() } catch { }
    Exit-With $ExitCodes.AlreadyRunning ("یک به‌روزرسانی دیگر در جریان است (قفل: " + $lockFile + " | " + $lockText + "). " +
        "اگر مطمئنید هیچ به‌روزرسانی‌ای در حال اجرا نیست، این فایل را پاک کنید.")
}

$packageInfoPath = Join-Path $PackagePath "package-info.json"
if (-not (Test-Path -LiteralPath $packageInfoPath)) {
    Exit-With $ExitCodes.PackageInvalid ("package-info.json در بسته نیست: " + $packageInfoPath)
}

$packageInfo = $null
try {
    $packageInfo = (Get-Content -LiteralPath $packageInfoPath -Raw) | ConvertFrom-Json
}
catch {
    Exit-With $ExitCodes.PackageInvalid ("package-info.json خوانده نشد: " + $_.Exception.Message)
}

# فایل اصلی برنامه باید در بسته باشد (وگرنه کپی، نصب را ناقص می‌کند)
$mainExeName = "Inventory.Api.exe"
if (-not (Test-Path -LiteralPath (Join-Path $PackagePath $mainExeName))) {
    if (-not (Test-Path -LiteralPath (Join-Path $PackagePath "Inventory.Api.dll"))) {
        Exit-With $ExitCodes.PackageInvalid ("در بسته نه Inventory.Api.exe هست نه Inventory.Api.dll — بستهٔ ناقص.")
    }
    $mainExeName = "Inventory.Api.dll"
}
Write-Log ("package-info: نسخه " + $packageInfo.version + " | ساخته‌شده " + $packageInfo.builtAt)

# فضای دیسک: دست‌کم ۵۰۰ مگابایت (پشتیبان دیتابیس + کپی برنامه)
try {
    $drive = New-Object System.IO.DriveInfo([System.IO.Path]::GetPathRoot($AppPath))
    $freeMb = [math]::Round($drive.AvailableFreeSpace / 1MB, 0)
    if ($freeMb -lt 500) {
        Exit-With $ExitCodes.PrecheckFailed ("فضای آزاد کم است: " + $freeMb + " مگابایت (حداقل ۵۰۰).")
    }
    Write-Log ("فضای آزاد: " + $freeMb + " مگابایت")
}
catch {
    Write-Log ("فضای دیسک بررسی نشد: " + $_.Exception.Message) 'WARN'
}

# پوشهٔ نصب باید قابل‌نوشتن باشد (وگرنه وسط کپی، نصب نیمه‌کاره می‌ماند)
$probe = Join-Path $AppPath (".update-probe-" + [Guid]::NewGuid().ToString("N") + ".tmp")
try {
    Set-Content -LiteralPath $probe -Value "probe" -Encoding ASCII
    Remove-Item -LiteralPath $probe -Force
}
catch {
    Exit-With $ExitCodes.PrecheckFailed ("پوشهٔ نصب قابل‌نوشتن نیست: " + $AppPath + " — " + $_.Exception.Message)
}

$healthTarget = $HealthUrl
if ([string]::IsNullOrWhiteSpace($healthTarget)) {
    $healthTarget = "http://localhost/api/health"
}

# ============================================================
#  ۲) دروازهٔ لایسنس — رد ⇒ صفر تغییر
# ============================================================
Write-Log ("مرحلهٔ ۲: دروازهٔ لایسنس (حالت: " + $LicenseGate + ")") 'STEP'

function Test-LicenseHealth {
    # از سرور در حال اجرا می‌پرسیم وضعیت لایسنس چیست. اگر پاسخ ندهد ⇒ «نامعلوم».
    try {
        $response = Invoke-WebRequest -Uri $healthTarget -TimeoutSec 5 -UseBasicParsing
        $payload = $response.Content | ConvertFrom-Json
        $licensing = $payload.PSObject.Properties | Where-Object { $_.Name -eq "licensing" }
        if ($null -eq $licensing) { return "unknown" }
        $status = $licensing.Value.status
        if ([string]::IsNullOrWhiteSpace($status)) { return "unknown" }
        return [string]$status
    }
    catch {
        return "unknown"
    }
}

function Test-LicenseSql {
    $conn = [Environment]::GetEnvironmentVariable($ConnectionStringEnvVar)
    if ([string]::IsNullOrWhiteSpace($conn)) {
        Write-Log ("متغیر محیطی " + $ConnectionStringEnvVar + " تنظیم نشده؛ دروازهٔ SQL ممکن نیست.") 'WARN'
        return "unknown"
    }
    $query = "SET NOCOUNT ON; SELECT TOP 1 Status FROM dbo.Licenses ORDER BY Id DESC"
    $out = Join-Path ([System.IO.Path]::GetTempPath()) ("licgate-" + [Guid]::NewGuid().ToString("N") + ".txt")
    $code = Invoke-Step -Name "sqlcmd-gate" -FilePath "sqlcmd" -AllowFailure -Arguments @(
        "-S", "tcp:localhost", "-d", $DatabaseName, "-Q", $query, "-h", "-1", "-W", "-o", $out, "-b")
    if ($code -ne 0) { return "unknown" }
    $value = ""
    try { $value = (Get-Content -LiteralPath $out -Raw).Trim() } catch { }
    Remove-Item -LiteralPath $out -Force -ErrorAction SilentlyContinue
    if ([string]::IsNullOrWhiteSpace($value)) { return "none" }
    return $value
}

function Test-LicenseFile {
    $folder = $LicenseGrantsPath
    if ([string]::IsNullOrWhiteSpace($folder)) { $folder = Join-Path $DataRoot "License\grants" }
    if (-not (Test-Path -LiteralPath $folder)) { return "none" }
    $files = @(Get-ChildItem -LiteralPath $folder -Filter "*.lic" -File -ErrorAction SilentlyContinue)
    if ($files.Count -eq 0) { return "none" }
    return "present"
}

$licenseState = "skipped"
if ($LicenseGate -ne "Off") {
    if ($LicenseGate -eq "Health") { $licenseState = Test-LicenseHealth }
    if ($LicenseGate -eq "Sql") { $licenseState = Test-LicenseSql }
    if ($LicenseGate -eq "File") { $licenseState = Test-LicenseFile }

    if ($null -ne $script:MockTable) {
        $mocked = $script:MockTable."license-gate"
        if ($null -ne $mocked) { $licenseState = [string]$mocked[0] }
    }

    $blocked = @("missing", "invalid", "none", "expired")
    if ($blocked -contains $licenseState) {
        Exit-With $ExitCodes.LicenseRejected ("به‌روزرسانی رد شد: وضعیت لایسنس «" + $licenseState + "» است. " +
            "هیچ تغییری انجام نشد. ابتدا لایسنس معتبر روی این نصب اعمال شود.")
    }
    if ($licenseState -eq "unknown") {
        Write-Log "وضعیت لایسنس نامعلوم است (سرور پاسخ نداد یا مقدار خوانده نشد) — ادامه می‌دهیم." 'WARN'
    }
    else {
        Write-Log ("وضعیت لایسنس: " + $licenseState)
    }
}
else {
    Write-Log "دروازهٔ لایسنس رد شد (Off) — فقط برای محیط آزمایش." 'WARN'
}

# ============================================================
#  قفل اجرا
# ============================================================
if (-not $DryRun) {
    try {
        if (-not (Test-Path -LiteralPath $DataRoot)) { New-Item -ItemType Directory -Path $DataRoot -Force | Out-Null }
        Set-Content -LiteralPath $lockFile -Value ("pid=" + $PID + " started=" + $startedAt.ToString("o")) -Encoding UTF8
    }
    catch {
        Exit-With $ExitCodes.PrecheckFailed ("ساخت فایل قفل ممکن نشد: " + $lockFile + " — " + $_.Exception.Message)
    }
}

function Remove-Lock {
    if ($DryRun) { return }
    if (Test-Path -LiteralPath $lockFile) { Remove-Item -LiteralPath $lockFile -Force -ErrorAction SilentlyContinue }
}

# ============================================================
#  ۳) پشتیبان‌گیری
# ============================================================
Write-Log "مرحلهٔ ۳: پشتیبان‌گیری" 'STEP'

$stamp = $startedAt.ToString("yyyyMMdd-HHmmss")
$backupRoot = Join-Path $DataRoot "backups"
$appBackupRoot = Join-Path $backupRoot "app"
$dbBackupRoot = Join-Path $backupRoot "db"
$appBackup = Join-Path $appBackupRoot $stamp
$dbBackupFile = Join-Path $dbBackupRoot ("db-" + $stamp + ".bak")

if ($SkipBackup) {
    Write-Log "پشتیبان‌گیری رد شد (SkipBackup) — در تولید استفاده نکنید." 'WARN'
}
else {
    if (-not $DryRun) {
        try {
            if (-not (Test-Path -LiteralPath $appBackupRoot)) { New-Item -ItemType Directory -Path $appBackupRoot -Force | Out-Null }
            if (-not (Test-Path -LiteralPath $dbBackupRoot)) { New-Item -ItemType Directory -Path $dbBackupRoot -Force | Out-Null }
        }
        catch {
            Remove-Lock
            Exit-With $ExitCodes.BackupFailed ("ساخت پوشهٔ پشتیبان ممکن نشد: " + $_.Exception.Message)
        }
    }

    # ---- ۳-الف) دیتابیس ----
    if ($DbProvider -eq "SqlServer") {
        # BACKUP بدون COMPRESSION: فایل پشتیبان باید با نسخه‌های قدیمی SQL Server هم باز شود.
        $query = "BACKUP DATABASE [" + $DatabaseName + "] TO DISK = N'" + $dbBackupFile + "' WITH INIT, NOFORMAT, CHECKSUM"
        $code = Invoke-Step -Name "sqlcmd" -FilePath "sqlcmd" -Arguments @(
            "-S", "tcp:localhost", "-d", "master", "-Q", $query, "-b")
        if ($code -ne 0) {
            Remove-Lock
            Exit-With $ExitCodes.BackupFailed ("پشتیبان‌گیری دیتابیس شکست خورد (کد " + $code + "). هیچ تغییری در برنامه انجام نشد.")
        }
    }
    else {
        # حالت File (تست/SQLite): کپی فایل دیتابیس
        $dbFiles = @(Get-ChildItem -LiteralPath $DataRoot -Filter "*.db" -File -Recurse -ErrorAction SilentlyContinue)
        if ($dbFiles.Count -eq 0) {
            Write-Log "هیچ فایل دیتابیسی برای پشتیبان‌گیری پیدا نشد (حالت File)." 'WARN'
        }
        foreach ($file in $dbFiles) {
            if ($DryRun) { continue }
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $dbBackupRoot $file.Name) -Force
        }
        if (-not $DryRun) {
            Set-Content -LiteralPath $dbBackupFile -Value ("file-backup " + $stamp) -Encoding UTF8
        }
    }

    # ---- ۳-ب) پوشهٔ برنامه ----
    if (-not $DryRun) {
        try {
            New-Item -ItemType Directory -Path $appBackup -Force | Out-Null
            $skippedDirs = @("App_Data", "License", "logs", "uploads")
            $items = @(Get-ChildItem -LiteralPath $AppPath -Force)
            foreach ($item in $items) {
                if ($item.PSIsContainer) {
                    if ($skippedDirs -contains $item.Name) { continue }
                    Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $appBackup $item.Name) -Recurse -Force
                }
                else {
                    Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $appBackup $item.Name) -Force
                }
            }
            Write-Log ("پشتیبان برنامه: " + $appBackup)
        }
        catch {
            Remove-Lock
            Exit-With $ExitCodes.BackupFailed ("پشتیبان‌گیری پوشهٔ برنامه شکست خورد: " + $_.Exception.Message)
        }
    }

    # ---- ۳-ج) نگه‌داری ۳ نسخهٔ پشتیبان برنامه (دیتابیس هرگز پاک نمی‌شود) ----
    if (-not $DryRun -and (Test-Path -LiteralPath $appBackupRoot)) {
        $existing = @(Get-ChildItem -LiteralPath $appBackupRoot -Directory | Sort-Object Name -Descending)
        if ($existing.Count -gt $KeepAppBackups) {
            for ($i = $KeepAppBackups; $i -lt $existing.Count; $i++) {
                try {
                    Remove-Item -LiteralPath $existing[$i].FullName -Recurse -Force
                    Write-Log ("پشتیبان قدیمی برنامه پاک شد: " + $existing[$i].Name)
                }
                catch {
                    Write-Log ("پاک‌کردن پشتیبان قدیمی ممکن نشد: " + $existing[$i].Name) 'WARN'
                }
            }
        }
    }
}

# ============================================================
#  ۴) offline + توقف App Pool
# ============================================================
Write-Log "مرحلهٔ ۴: بردن برنامه به حالت offline" 'STEP'

$offlineFile = Join-Path $AppPath "app_offline.htm"
$offlineWasThere = Test-Path -LiteralPath $offlineFile
$poolName = "TotallCollection"
if ($null -ne $packageInfo.appPoolName -and -not [string]::IsNullOrWhiteSpace($packageInfo.appPoolName)) {
    $poolName = [string]$packageInfo.appPoolName
}

if (-not $DryRun) {
    try {
        $offlineHtml = "<!DOCTYPE html><html lang=""fa"" dir=""rtl""><head><meta charset=""utf-8""/>" +
            "<title>به‌روزرسانی در جریان است</title></head><body style=""font-family:Tahoma;text-align:center;padding-top:80px"">" +
            "<h1>سامانه در حال به‌روزرسانی است</h1><p>این عملیات چند دقیقه طول می‌کشد. لطفاً صفحه را نبندید و بعداً تلاش کنید.</p>" +
            "</body></html>"
        Set-Content -LiteralPath $offlineFile -Value $offlineHtml -Encoding UTF8
        Write-Log "app_offline.htm نوشته شد (درخواست‌ها به صفحهٔ نگه‌داری می‌روند)."
    }
    catch {
        Remove-Lock
        Exit-With $ExitCodes.StopAppFailed ("نوشتن app_offline.htm ممکن نشد: " + $_.Exception.Message)
    }
}

$isWindowsHost = $true
if ($PSVersionTable.PSEdition -eq "Core" -and -not $IsWindows) { $isWindowsHost = $false }

$stopCode = 0
if ($isWindowsHost) {
    $stopCode = Invoke-Step -Name "appcmd-stop" -FilePath "appcmd.exe" -AllowFailure -Arguments @(
        "stop", "apppool", ("/apppool.name:" + $poolName))
    if ($stopCode -ne 0) {
        Write-Log "appcmd در دسترس نبود؛ تلاش با Stop-WebAppPool." 'WARN'
        try {
            Import-Module WebAdministration -ErrorAction Stop
            Stop-WebAppPool -Name $poolName -ErrorAction Stop
            $stopCode = 0
        }
        catch {
            Write-Log ("توقف App Pool ممکن نشد: " + $_.Exception.Message) 'WARN'
            $stopCode = 1
        }
    }
    if ($stopCode -ne 0) {
        Write-Log "توقف App Pool ناموفق بود — فایل‌ها ممکن است قفل باشند." 'WARN'
    }
}
else {
    Write-Log "میزبان ویندوز نیست: IIS/App Pool رد شد (خارج از دامنهٔ تست لینوکس)." 'WARN'
}

# ============================================================
#  ۵) کپی فایل‌های تازه
# ============================================================
Write-Log "مرحلهٔ ۵: کپی فایل‌های بسته" 'STEP'

# پوشه‌ها/فایل‌هایی که هرگز از بسته کپی نمی‌شوند و هرگز پاک نمی‌شوند.
$excludedDirs = @("App_Data", "License", "logs", "uploads", "backups")
$excludedFiles = @("appsettings.json", "appsettings.Production.json", "appsettings.Development.json",
    "app_offline.htm", "update.lock")

function Copy-PackageFiles {
    param([string]$Source, [string]$Destination)

    $root = Get-Item -LiteralPath $Source
    $files = @(Get-ChildItem -LiteralPath $Source -Recurse -File -Force)

    foreach ($file in $files) {
        $relative = $file.FullName.Substring($root.FullName.Length).TrimStart('\', '/')
        $segments = $relative -split '[\\/]'

        $skip = $false
        foreach ($segment in $segments) {
            if ($excludedDirs -contains $segment) { $skip = $true }
        }
        if ($excludedFiles -contains $file.Name) { $skip = $true }
        if ($skip) { continue }

        $target = Join-Path $Destination $relative
        $targetDir = Split-Path -Parent $target
        if (-not (Test-Path -LiteralPath $targetDir)) {
            New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
        }
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }
}

if ($DryRun) {
    Write-Log "[DryRun] فایلی کپی نمی‌شود." 'WARN'
}
else {
    try {
        Copy-PackageFiles -Source $PackagePath -Destination $AppPath
        Write-Log "فایل‌های بسته کپی شدند (کپی افزایشی، بدون آینه‌سازی؛ App_Data، License، logs، uploads و appsettings دست‌نخورده)."
    }
    catch {
        Remove-Lock
        Exit-With $ExitCodes.CopyFailed ("کپی فایل‌ها شکست خورد: " + $_.Exception.Message +
            " — پوشهٔ برنامه از پشتیبان قابل بازگردانی است: " + $appBackup)
    }
}

# ============================================================
#  ۶) به‌روزرسانی دیتابیس
# ============================================================
Write-Log "مرحلهٔ ۶: به‌روزرسانی دیتابیس" 'STEP'

$updateDbRunner = Join-Path $AppPath $mainExeName
$dbCode = 0
if ($DryRun) {
    Write-Log "[DryRun] --update-database اجرا نمی‌شود." 'WARN'
}
elseif (-not (Test-Path -LiteralPath $updateDbRunner)) {
    Write-Log ("فایل اجرایی برنامه در پوشهٔ نصب نیست: " + $updateDbRunner) 'ERROR'
    $dbCode = 1
}
else {
    $dbCode = Invoke-Step -Name "update-database" -FilePath $updateDbRunner -Arguments @("--update-database")
}

if ($dbCode -ne 0) {
    Write-Log ("به‌روزرسانی دیتابیس شکست خورد (کد " + $dbCode + ").") 'ERROR'

    if ($RestoreDbOnFailure -and -not $SkipBackup) {
        Write-Log "بازگردانی دیتابیس از پشتیبان (RestoreDbOnFailure)." 'WARN'
        if ($DbProvider -eq "SqlServer") {
            $restore = "ALTER DATABASE [" + $DatabaseName + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                "RESTORE DATABASE [" + $DatabaseName + "] FROM DISK = N'" + $dbBackupFile + "' WITH REPLACE; " +
                "ALTER DATABASE [" + $DatabaseName + "] SET MULTI_USER"
            $restoreCode = Invoke-Step -Name "sqlcmd-restore" -FilePath "sqlcmd" -Arguments @(
                "-S", "tcp:localhost", "-d", "master", "-Q", $restore, "-b")
            if ($restoreCode -ne 0) {
                Write-Log "بازگردانی دیتابیس هم شکست خورد — پشتیبان دستی لازم است: " + $dbBackupFile 'ERROR'
            }
            else {
                Write-Log "دیتابیس از پشتیبان بازگردانده شد."
            }
        }
        else {
            Write-Log "حالت File: فایل پشتیبان‌گرفته‌شده دستی بازگردانده می‌شود (پوشه: " + $dbBackupRoot + ")." 'WARN'
        }
    }
    else {
        Write-Log ("دیتابیس بازگردانده نشد (RestoreDbOnFailure داده نشده). پشتیبان موجود: " + $dbBackupFile) 'WARN'
    }

    Write-Log "بازگردانی پوشهٔ برنامه از پشتیبان." 'WARN'
    if (-not $DryRun -and (Test-Path -LiteralPath $appBackup)) {
        try {
            Copy-Item -Path (Join-Path $appBackup "*") -Destination $AppPath -Recurse -Force
            Write-Log "پوشهٔ برنامه بازگردانده شد."
        }
        catch {
            Write-Log ("بازگردانی پوشهٔ برنامه شکست خورد: " + $_.Exception.Message) 'ERROR'
        }
    }
    if (-not $DryRun) { Remove-Item -LiteralPath $offlineFile -Force -ErrorAction SilentlyContinue }
    Remove-Lock
    Exit-With $ExitCodes.DbUpdateFailed "به‌روزرسانی دیتابیس شکست خورد؛ برنامه بازگردانده شد."
}

# ============================================================
#  ۷) ACL پوشه‌های ProgramData (فقط ویندوز)
# ============================================================
Write-Log "مرحلهٔ ۷: دسترسی‌های ProgramData" 'STEP'

$aclFolders = @(
    (Join-Path $DataRoot "security"),
    (Join-Path $DataRoot "License"),
    (Join-Path $DataRoot "projects"),
    (Join-Path $DataRoot "logs")
)

if (-not $isWindowsHost) {
    Write-Log "ACL ویندوزی اعمال نشد (میزبان لینوکس) — در چک‌لیست تست دستی ویندوز آمده است." 'WARN'
}
else {
    foreach ($folder in $aclFolders) {
        if (-not (Test-Path -LiteralPath $folder)) {
            if (-not $DryRun) { New-Item -ItemType Directory -Path $folder -Force | Out-Null }
        }
        # IIS AppPoolVirtualAccount: <poolName> — دسترسی Modify روی همان پوشه‌های داده.
        $identity = "IIS AppPool\" + $poolName
        $code = Invoke-Step -Name "icacls" -FilePath "icacls.exe" -AllowFailure -Arguments @(
            $folder, "/grant", ($identity + ":(OI)(CI)M"), "/T", "/C")
        if ($code -ne 0) {
            Write-Log ("اعمال ACL ممکن نشد روی " + $folder + " (کد " + $code + ") — دستی بررسی شود.") 'WARN'
        }
    }
}

# ============================================================
#  ۸) پایان offline، شروع App Pool و بررسی سلامت
# ============================================================
Write-Log "مرحلهٔ ۸: بازگرداندن سرویس و بررسی سلامت" 'STEP'

if (-not $DryRun) {
    Remove-Item -LiteralPath $offlineFile -Force -ErrorAction SilentlyContinue
    Write-Log "app_offline.htm برداشته شد."
}

if ($isWindowsHost) {
    $startCode = Invoke-Step -Name "appcmd-start" -FilePath "appcmd.exe" -AllowFailure -Arguments @(
        "start", "apppool", ("/apppool.name:" + $poolName))
    if ($startCode -ne 0) {
        try {
            Import-Module WebAdministration -ErrorAction Stop
            Start-WebAppPool -Name $poolName -ErrorAction Stop
        }
        catch {
            Write-Log ("شروع App Pool ممکن نشد: " + $_.Exception.Message) 'WARN'
        }
    }
}
else {
    Write-Log "شروع App Pool رد شد (میزبان لینوکس)." 'WARN'
}

$healthy = $false
for ($attempt = 1; $attempt -le $HealthRetries; $attempt++) {
    if ($null -ne $script:MockTable) {
        $queue = $script:MockTable."health"
        if ($null -ne $queue) {
            $index = 0
            if ($script:MockCursor.ContainsKey("health")) { $index = $script:MockCursor["health"] }
            $value = 0
            if ($index -lt $queue.Count) { $value = [int]$queue[$index] }
            $script:MockCursor["health"] = $index + 1
            if ($value -eq 0) { $healthy = $true; break }
            continue
        }
    }

    try {
        $response = Invoke-WebRequest -Uri $healthTarget -TimeoutSec 10 -UseBasicParsing
        if ($response.StatusCode -eq 200) { $healthy = $true; break }
        Write-Log ("تلاش " + $attempt + ": کد " + $response.StatusCode) 'WARN'
    }
    catch {
        Write-Log ("تلاش " + $attempt + " از " + $HealthRetries + " ناموفق: " + $_.Exception.Message) 'WARN'
    }
    if ($attempt -lt $HealthRetries) { Start-Sleep -Seconds $HealthRetrySeconds }
}

if (-not $healthy) {
    Write-Log "بررسی سلامت شکست خورد — بازگردانی پوشهٔ برنامه از پشتیبان." 'ERROR'
    if (-not $DryRun -and (Test-Path -LiteralPath $appBackup)) {
        try {
            Copy-Item -Path (Join-Path $appBackup "*") -Destination $AppPath -Recurse -Force
            Write-Log "پوشهٔ برنامه بازگردانده شد؛ سرویس را دستی راه‌اندازی کنید." 'WARN'
        }
        catch {
            Write-Log ("بازگردانی شکست خورد: " + $_.Exception.Message) 'ERROR'
        }
    }
    Remove-Lock
    Exit-With $ExitCodes.HealthFailed ("بررسی سلامت ناموفق بود. پشتیبان‌ها: " + $backupRoot + " — دیتابیس را دستی بررسی کنید.")
}

# ============================================================
#  ۹) پایان
# ============================================================
Remove-Lock
Write-Log ("به‌روزرسانی با موفقیت انجام شد. پشتیبان‌ها: " + $backupRoot) 'STEP'
Write-Log ("لاگ: " + $script:LogFile)
Exit-With $ExitCodes.Success ""
