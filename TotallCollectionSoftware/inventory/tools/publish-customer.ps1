<#
.SYNOPSIS
    پابلیش نسخهٔ تازه برای یک مشتری — با توقف در هر مرحله‌ای که تأیید نشود.

.DESCRIPTION
    چهار گام را پشت‌سرهم و بدون دخالت دستی انجام می‌دهد:

      ۱) ساخت/بازبینی کلید ریشه (RootKeyTool) و هم‌خوانی manifest + appsettings
      ۲) بازبینی تنظیمات حساسِ appsettings محصول (DevBypass، SeedDemo، رمز در رشتهٔ اتصال)
      ۳) پابلیش با publish-server.bat (همان مسیر رسمی پروژه؛ فقط و فقط حاوی کلیدهای Release)
      ۴) اثبات اینکه کلید تازه واقعاً داخل DLL منتشرشده جاسازی شده (LicenseTool keys --dir)

    هیچ فایلی روی سرور مشتری کپی نمی‌کند و هیچ کلید خصوصی‌ای را جابه‌جا نمی‌کند.

.EXAMPLE
    # اولین بار برای «فروغ آریا» — کلید ریشه ساخته می‌شود و بیلد گرفته می‌شود
    .\publish-customer.ps1 -CustomerName "فروغ آریا پارسیان"

.EXAMPLE
    # بار بعد: کلید هست، فقط پابلیش و تأیید
    .\publish-customer.ps1 -KeyId sup-202612 -SkipKeyGen

.EXAMPLE
    # فقط بررسی (بدون پابلیش) — پیش از هر انتشار واقعی
    .\publish-customer.ps1 -SkipPublish
#>
[CmdletBinding()]
param(
    [string] $CustomerName = "",
    [string] $KeyId = "sup-202612",
    [string] $KeysOut = "C:\LicensingKeys",
    [ValidateSet("onprem", "saas")]
    [string] $Mode = "onprem",
    [string] $Password = "",
    [switch] $SkipKeyGen,
    [switch] $SkipPublish,
    [switch] $Force
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# ─────────────────────────── کمکی‌ها ───────────────────────────

$script:StepNo = 0
function Step([string] $text) {
    $script:StepNo++
    Write-Host ""
    Write-Host ("[{0}] {1}" -f $script:StepNo, $text) -ForegroundColor Cyan
}
function Ok  ([string] $text) { Write-Host "    OK  $text" -ForegroundColor Green }
function Note([string] $text) { Write-Host "    ..  $text" -ForegroundColor DarkGray }
function Warn([string] $text) { Write-Host "    !   $text" -ForegroundColor Yellow }
function Die ([string] $text) {
    Write-Host ""
    Write-Host "  X  $text" -ForegroundColor Red
    Write-Host "  انتشار متوقف شد. هیچ فایلی برای مشتری آماده نشد." -ForegroundColor Red
    exit 1
}

function Get-FingerprintFromPem([string] $pemPath) {
    $text = Get-Content -Raw -LiteralPath $pemPath
    $body = [regex]::Match($text, "BEGIN PUBLIC KEY-----(?<b>[\s\S]+?)-----END PUBLIC KEY").Groups["b"].Value
    if ([string]::IsNullOrWhiteSpace($body)) { return $null }
    $clean = $body -replace "\s", ""
    $der = [Convert]::FromBase64String($clean)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash($der)
    } finally { $sha.Dispose() }
    return (($hash | ForEach-Object { $_.ToString("x2") }) -join "")
}

function Invoke-Tool([string] $exe, [string[]] $arguments, [string] $what) {
    Note "$exe $($arguments -join ' ')"
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { Die "$what ناموفق بود (کد خروج $LASTEXITCODE)." }
}

# ─────────────────────────── مسیرها ───────────────────────────

$here = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
$repo = $null
$probe = Get-Item $here
while ($probe -ne $null) {
    $candidate = Join-Path $probe.FullName "inventory/src/Inventory.Licensing/Keys/manifest.json"
    if (Test-Path $candidate) { $repo = $probe.FullName; break }
    $probe = $probe.Parent
}
if (-not $repo) { Die "ریشهٔ مخزن پیدا نشد (inventory/src/Inventory.Licensing/Keys/manifest.json). اسکریپت را از داخل مخزن اجرا کنید." }

$inventory   = Join-Path $repo "inventory"
$manifest    = Join-Path $inventory "src/Inventory.Licensing/Keys/manifest.json"
$keysFolder  = Join-Path $inventory "src/Inventory.Licensing/Keys"
$appsettings = Join-Path $inventory "src/Inventory.Api/appsettings.json"
$deploy      = Join-Path $inventory "deploy/inventory-server"
$artifacts   = Join-Path $inventory "artifacts/inventory-server"
$rootKeyTool = Join-Path $inventory "tools/RootKeyTool/RootKeyTool.csproj"
$licenseTool = Join-Path $inventory "tools/LicenseTool/LicenseTool.csproj"
$publisher   = Join-Path $inventory "publish-server.bat"

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$logDir = Join-Path $inventory "artifacts"
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
$logFile = Join-Path $logDir "publish-customer-$stamp.log"
Start-Transcript -Path $logFile -Force | Out-Null

Write-Host ""
Write-Host "════════ پابلیش نسخهٔ مشتری ════════" -ForegroundColor White
Write-Host "  مشتری        : $CustomerName"
Write-Host "  شناسهٔ کلید   : $KeyId"
Write-Host "  حالت استقرار : $Mode"
Write-Host "  مخزن         : $repo"
Write-Host "  گاوصندوق     : $KeysOut"
Write-Host "  گزارش        : $logFile"

try {

# ─────────────────────── گام ۱: کلید ریشه ───────────────────────
Step "کلید ریشه"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Die ".NET SDK 8 پیدا نشد (dotnet در PATH نیست)."
}

$privateKeyPath = Join-Path $KeysOut "$KeyId.private.pem"
$publicKeyPath  = Join-Path $KeysOut "$KeyId.pem"

if ($SkipKeyGen) {
    Note "با -SkipKeyGen اجرا شد؛ ساخت کلید رد شد."
    if (-not (Test-Path $privateKeyPath)) { Die "کلید خصوصی نیست: $privateKeyPath" }
}
elseif ((Test-Path $privateKeyPath) -and (-not $Force)) {
    Ok "کلید از قبل هست و بازنویسی نشد: $privateKeyPath"
    Note "برای ساخت کلید تازه (خطرناک: لایسنس‌های قبلی باطل می‌شوند) -Force بدهید."
}
else {
    if ($Force -and (Test-Path $privateKeyPath)) {
        Warn "بازنویسی کلید موجود! لایسنس‌های امضاشده با کلید قبلی باطل می‌شوند."
    }

    $plain = $Password
    if ([string]::IsNullOrEmpty($plain)) {
        $secure = Read-Host "گذرواژهٔ کلید خصوصی ریشه را وارد کنید" -AsSecureString
        $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        try   { $plain = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr) }
        finally { [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
    }
    if ([string]::IsNullOrWhiteSpace($plain)) { Die "گذرواژهٔ کلید خالی است (کلید بدون رمز ساخته نمی‌شود)." }

    # گذرواژه از راه متغیر محیطی به ابزار می‌رسد، نه آرگومان خط فرمان.
    $env:LICENSING_PUBLISH_KEY_PASSWORD = $plain

    $args = @(
        "run", "--project", $rootKeyTool, "-c", "Release", "--",
        "new", "--key-id", $KeyId,
        "--out", $KeysOut,
        "--password-env", "LICENSING_PUBLISH_KEY_PASSWORD",
        "--repo", $repo
    )
    Invoke-Tool "dotnet" $args "ساخت کلید ریشه"

    Remove-Item Env:\LICENSING_PUBLISH_KEY_PASSWORD -ErrorAction SilentlyContinue
    Ok "کلید ساخته شد."
}

if (-not (Test-Path $publicKeyPath)) { Die "کلید عمومی نیست: $publicKeyPath" }
$fingerprint = Get-FingerprintFromPem $publicKeyPath
if (-not $fingerprint -or $fingerprint.Length -ne 64) { Die "اثر انگشت کلید عمومی محاسبه نشد: $publicKeyPath" }
Ok "اثر انگشت $KeyId = $fingerprint"

# هم‌خوانی manifest
if (-not (Test-Path $manifest)) { Die "manifest.json پیدا نشد: $manifest" }
$manifestJson = Get-Content -Raw -LiteralPath $manifest | ConvertFrom-Json
$row = $manifestJson.keys | Where-Object {
    $prop = $_.PSObject.Properties["keyId"]
    $prop -and ($prop.Value -eq $KeyId)
} | Select-Object -First 1
if (-not $row) { Die "کلید «$KeyId» در manifest.json نیست؛ انتشار با آن بی‌فایده است." }

$rowFile = if ($row.PSObject.Properties["file"]) { [string] $row.file } else { "$KeyId.pem" }
$rowSha  = if ($row.PSObject.Properties["sha256"]) { [string] $row.sha256 } else { "" }
$rowDbg  = if ($row.PSObject.Properties["debugOnly"]) { [bool] $row.debugOnly } else { $false }

if ($rowSha -and (-not $fingerprint.StartsWith($rowSha, [System.StringComparison]::OrdinalIgnoreCase))) {
    Die "اثر انگشت کلید با manifest نمی‌خواند (manifest=$rowSha)."
}
if ($rowDbg) { Die "این کلید در manifest با debugOnly=true ثبت شده؛ در بیلد Release بارگذاری نمی‌شود." }
if (-not (Test-Path (Join-Path $keysFolder $rowFile))) { Die "فایل کلید عمومی «$rowFile» در Keys نیست." }
Ok "manifest هم‌خوان است (file=$rowFile)"

# ───────────────── گام ۲: بازبینی تنظیمات محصول ─────────────────
Step "بازبینی تنظیمات محصول"

if (-not (Test-Path $appsettings)) { Die "appsettings محصول پیدا نشد: $appsettings" }
$settingsText = Get-Content -Raw -LiteralPath $appsettings

$mId = [regex]::Match($settingsText, '"SigningKeyId"\s*:\s*"([^"]*)"')
if (-not $mId.Success) { Die "SigningKeyId در appsettings نیست؛ کلید ریشه را با RootKeyTool و -repo بسازید." }
if ($mId.Groups[1].Value -ne $KeyId) {
    Die "appsettings به کلید «$($mId.Groups[1].Value)» اشاره می‌کند، نه «$KeyId»."
}
Ok "Licensing:SigningKeyId = $KeyId"

$mFp = [regex]::Match($settingsText, '"SigningKeyFingerprintSha256"\s*:\s*"([^"]*)"')
if ($mFp.Success -and $mFp.Groups[1].Value) {
    if (-not $fingerprint.StartsWith($mFp.Groups[1].Value, [System.StringComparison]::OrdinalIgnoreCase)) {
        Die "اثر انگشت appsettings با کلید واقعی نمی‌خواند."
    }
    Ok "اثر انگشت appsettings هم‌خوان است"
} else {
    Warn "SigningKeyFingerprintSha256 در appsettings خالی است (بررسی خودکار هم‌خوانی انجام نمی‌شود)."
}

if ($settingsText -match '"DevelopmentBypass"\s*:\s*true') {
    Die "Licensing:DevelopmentBypass = true است؛ بیلد تولیدی با آن منتشر نشود."
}
Ok "DevelopmentBypass = false"

if ($settingsText -match '"SeedDemoData"\s*:\s*true') {
    Warn "Database:SeedDemoData = true است؛ روی نصب مشتری دادهٔ نمونه ساخته می‌شود."
}
if ($settingsText -match '(?i)Password\s*=') {
    Warn "رشتهٔ اتصال داخل appsettings رمز دارد؛ بهتر است خالی باشد و ConnectionStrings__Default از محیط بیاید."
}

# ─────────────────────── گام ۳: پابلیش ───────────────────────
Step "پابلیش ($Mode)"

if ($SkipPublish) {
    Note "با -SkipPublish اجرا شد؛ پابلیش رد شد."
    if (-not (Test-Path $deploy)) { Die "خروجی پابلیش قبلی هم نیست: $deploy" }
}
else {
    if (-not (Test-Path $publisher)) { Die "publish-server.bat پیدا نشد: $publisher" }
    Push-Location $inventory
    try {
        Invoke-Tool "cmd.exe" @("/c", $publisher, $Mode) "پابلیش"
    } finally {
        Pop-Location
    }
    Ok "پابلیش تمام شد: $deploy"
}

if (-not (Test-Path $deploy)) { Die "پوشهٔ خروجی پابلیش نیست: $deploy" }

# ─────────── گام ۴: اثبات کلید داخل DLL منتشرشده ───────────
Step "تأیید کلید داخل بیلد منتشرشده"

$licenseToolDll = Join-Path $inventory "tools/LicenseTool/bin/Release/net8.0/LicenseTool.dll"
if (-not (Test-Path $licenseToolDll)) {
    Note "LicenseTool ساخته نشده؛ یک‌بار بیلد می‌شود."
    Invoke-Tool "dotnet" @("build", $licenseTool, "-c", "Release", "-v", "q", "--nologo") "بیلد LicenseTool"
}
if (-not (Test-Path $licenseToolDll)) { Die "LicenseTool.dll ساخته نشد: $licenseToolDll" }

$keysArgs = @($licenseToolDll, "keys", "--dir", $deploy, "--expect", $KeyId)
Note "dotnet $($keysArgs -join ' ')"
$keysOutput = & dotnet @keysArgs 2>&1
$keysExit = $LASTEXITCODE
$keysOutput | ForEach-Object { Write-Host "      $_" }

if ($keysExit -ne 0) {
    Die "بیلد منتشرشده کلید «$KeyId» را ندارد یا کلید معتبری ندارد (کد $keysExit). فایل‌ها را به سرور نبرید."
}
if (-not (($keysOutput -join "`n") -like "*$fingerprint*")) {
    Warn "اثر انگشت در خروجی دیده نشد؛ خروجی بالا را دستی مقایسه کنید."
} else {
    Ok "کلید $KeyId با اثر انگشت درست داخل بیلد جاسازی شده است"
}

# ─────────────────────── خلاصه ───────────────────────
Write-Host ""
Write-Host "════════ آمادهٔ انتقال ════════" -ForegroundColor White
Write-Host "  پوشهٔ بیلد    : $deploy"
Write-Host "  شناسهٔ کلید   : $KeyId"
Write-Host "  اثر انگشت     : $fingerprint"
Write-Host "  گزارش کامل    : $logFile"
Write-Host ""
Write-Host "  قدم بعد:" -ForegroundColor Cyan
Write-Host "   ۱) روی سرور مشتری App Pool را Stop و از برنامه + C:\ProgramData\TotallCollection پشتیبان بگیرید."
Write-Host "   ۲) پوشهٔ بیلد را با robocopy منتقل کنید (License / data / logs را حذف نکنید)."
Write-Host "   ۳) ConnectionStrings__Default را از متغیر محیطی بدهید (نه داخل appsettings)."
Write-Host "   ۴) در پنل، Signer:RootKeyId=$KeyId و Signer:RootPrivateKeyPath=$privateKeyPath را ست کنید."
Write-Host "   ۵) فایل درخواست تازه از مشتری بگیرید، امضا کنید و license.lic را برگردانید."
Write-Host ""
Ok "پایان موفق."

}
finally {
    Remove-Item Env:\LICENSING_PUBLISH_KEY_PASSWORD -ErrorAction SilentlyContinue
    try { Stop-Transcript | Out-Null } catch { }
}
