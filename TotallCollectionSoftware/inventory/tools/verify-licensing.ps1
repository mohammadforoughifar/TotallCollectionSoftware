# ============================================================
#  بررسی «موتور لایسنس» در خروجی انتشار / نصب
#
#  چرا این فایل وجود دارد؟
#  مخزن دو درخت سورس دارد:
#     inventory\src\...   ← سورس مرجع، موتور لایسنس دارد
#     src\...             ← سورس قدیمی، لایسنس ندارد (فعلاً برای مقایسه نگه داشته شده)
#  اگر کسی اشتباهاً از درخت قدیمی منتشر کند، هیچ خطایی نمی‌بیند و فقط بعداً
#  می‌فهمد سامانه بدون لایسنس توزیع شده است. این اسکریپت همان لحظه جلویش را می‌گیرد.
#
#  دو بررسی انجام می‌دهد و هر دو باید موفق باشند:
#    ۱) آفلاین  — اسمبلی Inventory.Licensing.dll کنار اجرایی هست و
#                 Inventory.Api.dll واقعاً فیلد licensing را در /api/health می‌سازد
#                 (نشانگر <licensing>i__Field در متادیتا).
#    ۲) آنلاین  — اگر سروری روی HealthUrl پاسخ بدهد، پاسخ باید فیلد licensing داشته باشد.
#                 اگر سروری بالا نباشد، این مرحله «رد شده» ثبت می‌شود، نه شکست.
#
#  کدهای خروج:
#     0 = سالم
#     2 = Inventory.Licensing.dll در خروجی نیست
#     3 = Inventory.Api.dll فیلد licensing را نمی‌سازد (نسخهٔ قدیمی/بی‌لایسنس)
#     4 = سرور پاسخ داد ولی فیلد licensing نداشت
#     5 = RequireOnline داده شده ولی سروری پاسخ نداد
#
#  نمونه:
#     powershell -NoProfile -ExecutionPolicy Bypass -File verify-licensing.ps1 -AppPath .\deploy\inventory-server
#     powershell ... -File verify-licensing.ps1 -AppPath . -HealthUrl http://192.168.30.104:8181/api/health -RequireOnline
# ============================================================
param(
    [Parameter(Mandatory = $true)][string]$AppPath,
    [string]$HealthUrl = "",
    [int]$TimeoutSec = 6,
    [switch]$RequireOnline,
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"

function Say([string]$text, [string]$color = "Gray") {
    if (-not $Quiet) { Write-Host $text -ForegroundColor $color }
}

function Fail([string]$code, [string]$message) {
    Write-Host ""
    Write-Host "[ERROR] $message" -ForegroundColor Red
    Write-Host "[ERROR] licensing verification failed (code $code)." -ForegroundColor Red
    exit $code
}

if (-not (Test-Path -LiteralPath $AppPath)) {
    Fail 2 "مسیر خروجی پیدا نشد: $AppPath"
}
$dir = (Resolve-Path -LiteralPath $AppPath).Path

# ---------- ۱) اسمبلی موتور ----------
$engine = Join-Path $dir "Inventory.Licensing.dll"
if (-not (Test-Path -LiteralPath $engine)) {
    Fail 2 ("Inventory.Licensing.dll در خروجی نیست: $dir" + [Environment]::NewLine +
            "   یعنی این خروجی از سورس بدون لایسنس (احتمالاً پوشهٔ قدیمی src) ساخته شده است.")
}
Say ("  OK   Inventory.Licensing.dll  ({0:N0} bytes)" -f (Get-Item -LiteralPath $engine).Length) "Green"

# ---------- ۲) فیلد licensing در /api/health ----------
# نشانگر در متادیتای Inventory.Api.dll: نام خاصیت ناشناسِ health به‌صورت
# <licensing>i__Field ذخیره می‌شود. اگر این رشته نباشد، آن بیلد health بدون
# فیلد licensing است — همان چیزی که باید منتشر نشود.
$apiDll = Join-Path $dir "Inventory.Api.dll"
if (-not (Test-Path -LiteralPath $apiDll)) {
    Fail 3 "Inventory.Api.dll در خروجی نیست: $dir"
}

$marker = [System.Text.Encoding]::UTF8.GetBytes("<licensing>i__Field")
$bytes = [System.IO.File]::ReadAllBytes($apiDll)

function Find-Bytes([byte[]]$haystack, [byte[]]$needle) {
    if ($needle.Length -eq 0 -or $haystack.Length -lt $needle.Length) { return $false }
    $last = $haystack.Length - $needle.Length
    for ($i = 0; $i -le $last; $i++) {
        if ($haystack[$i] -ne $needle[0]) { continue }
        $ok = $true
        for ($j = 1; $j -lt $needle.Length; $j++) {
            if ($haystack[$i + $j] -ne $needle[$j]) { $ok = $false; break }
        }
        if ($ok) { return $true }
    }
    return $false
}

if (-not (Find-Bytes $bytes $marker)) {
    Fail 3 ("Inventory.Api.dll فیلد licensing را در /api/health نمی‌سازد: $apiDll" + [Environment]::NewLine +
            "   یعنی این خروجی از سورس قدیمی (بدون موتور لایسنس) ساخته شده است.")
}
Say "  OK   /api/health فیلد licensing دارد (متادیتای اسمبلی)" "Green"

# ---------- ۳) بررسی زندهٔ health (اختیاری) ----------
if ([string]::IsNullOrWhiteSpace($HealthUrl)) {
    $HealthUrl = $env:INVENTORY_HEALTH_URL
}
if ([string]::IsNullOrWhiteSpace($HealthUrl)) {
    if ($RequireOnline) { Fail 5 "HealthUrl داده نشده ولی RequireOnline فعال است." }
    Say "  SKIP بررسی زندهٔ health (آدرسی داده نشد)" "DarkYellow"
    Say "licensing verification passed (offline)." "Green"
    exit 0
}

try {
    $response = Invoke-WebRequest -Uri $HealthUrl -TimeoutSec $TimeoutSec -UseBasicParsing -ErrorAction Stop
    $json = $response.Content | ConvertFrom-Json
    $prop = $json.PSObject.Properties | Where-Object { $_.Name -eq "licensing" }
    if ($null -eq $prop) {
        Fail 4 ("سرور روی $HealthUrl پاسخ داد ولی فیلد licensing نداشت." + [Environment]::NewLine +
                "   یعنی نسخهٔ در حال اجرا موتور لایسنس ندارد.")
    }
    $engineVersion = $prop.Value.engine
    Say "  OK   $HealthUrl → licensing.engine = $engineVersion" "Green"
}
catch {
    if ($RequireOnline) {
        Fail 5 "سرور روی $HealthUrl پاسخ نداد: $($_.Exception.Message)"
    }
    Say "  SKIP بررسی زندهٔ health (سرور بالا نیست: $($_.Exception.Message))" "DarkYellow"
}

Say "licensing verification passed." "Green"
exit 0
