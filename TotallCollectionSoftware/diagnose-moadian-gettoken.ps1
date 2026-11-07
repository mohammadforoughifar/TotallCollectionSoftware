#Requires -Version 7.0
<#
===============================================================================
diagnose-moadian-gettoken.ps1
تشخیص خطای احراز هویت سامانه مودیان — بدون نیاز به بیلد/استارتاپ ERP
===============================================================================
این اسکریپت دقیقاً همان درخواستی را که SDK (TaxCollectData.Library 0.0.34)
می‌فرستد بازتولید می‌کند:
  1) Normalization: مقادیر فلترشده (نیم‌علائم '#' جداکننده، مقادیر خالی = '#')
     به ترتیب الفبایی کلیدها
  2) امضای RSA/SHA256/PKCS1 روی متن نرمال‌شده
  3) ارسال POST به {Base}/self-tsp/sync/GET_TOKEN با هدرهای requestTraceId/timestamp
و پاسخ خام سامانه (کد خطای واقعی) را نمایش می‌دهد.

استفاده:
  # ۰) مقایسهٔ دو کلید بدون فاش‌شدن آن‌ها (اثر انگشت):
  pwsh .\diagnose-moadian-gettoken.ps1 -Fingerprint -KeyFile .\Private.key
  pwsh .\diagnose-moadian-gettoken.ps1 -Fingerprint -KeyFile .\کلید_آپلودشده.key
  # یکسان = همان کلید | متفاوت = کلیدهای متفاوت

  # 1) با شناسه/کلید فعلیِ سیستم (همان‌ها که ERP می‌فرستد):
  pwsh .\diagnose-moadian-gettoken.ps1 -FiscalId A2ZRHM -KeyFile "مسیر\کلید.key"

  # 2) با MemoryId و کلید کاری (از جدول SAZMAN دیتابیس Taxation):
  -- SELECT MEMORYID, PRIVIATEKEY FROM dbo.SAZMAN
  -- ستون PRIVIATEKEY را در فایلی مثل work.key ذخیره کنید (PEM یا base64 خالی از سربرگ، هر دو می‌خورد)
  pwsh .\diagnose-moadian-gettoken.ps1 -FiscalId <MEMORYID> -KeyFile .\work.key

  # آدرس پیش‌فرض: https://tp.tax.gov.ir/req/api/  (تولید)
  # برای تست: -Base https://sandboxrc.tax.gov.ir/req/api/
===============================================================================
#>
param(
    [string]$FiscalId = "",
    [Parameter(Mandatory)][string]$KeyFile,
    [string]$Base = "https://tp.tax.gov.ir/req/api/",
    [string]$ClientTypeSegment = "self-tsp",
    [switch]$Fingerprint
)

$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSEdition -ne "Core") {
    Write-Error "این اسکریپت نیاز به PowerShell 7 (pwsh) دارد. با 'pwsh' اجرا کنید، نه 'powershell' قدیمی."
}
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13

# ---------- بارگذاری کلید (PEM کامل یا base64 بدون سربرگ) ----------
if (-not (Test-Path $KeyFile)) { throw "فایل کلید یافت نشد: $KeyFile" }
$raw = (Get-Content $KeyFile -Raw) -replace '-----[A-Z0-9 ]+-----', ''
$b64 = ($raw -replace '\s', '')
try {
    $der = [Convert]::FromBase64String($b64)
} catch {
    throw "محتوای فایل کلید base64 معتبر نیست. (کلید باید PKCS#8 باشد؛ PEM کامل یا base64 بدون سربرگ)"
}
$rsa = [System.Security.Cryptography.RSA]::Create()
try { $rsa.ImportPkcs8PrivateKey($der, [ref]$null) }
catch {
    # اگر PKCS#1 (RSAPrivateKey) باشد
    try {
        $rsa.ImportRSAPrivateKey($der, [ref]$null)
    } catch {
        throw "کلید قابل بارگذاری نبود (نه PKCS#8 بود نه PKCS#1): $($_.Exception.Message)"
    }
}
Write-Host "✓ کلید بارگذاری شد: $(Split-Path $KeyFile -Leaf) (مدول: $($rsa.Key.KeySize) بیت)"

# ---------- حالت اثر انگشت: مقایسهٔ دو کلید بدون فاش‌شدن آن‌ها ----------
if ($Fingerprint) {
    $spki = $rsa.ExportSubjectPublicKeyInfo()
    $fp = [Convert]::ToBase64String([System.Security.Cryptography.SHA256]::Create().ComputeHash($spki))
    Write-Host ""
    Write-Host "اثر انگشت کلید (SHA-256 کلید عمومی): $fp"
    Write-Host "فایل: $KeyFile"
    Write-Host ""
    Write-Host "این مقدار را برای هر دو فایل (کلید کاری و کلید آپلودشده در ERP) بگیرید و مقایسه کنید."
    Write-Host "یکسان = همان کلید است | متفاوت = کلیدهای متفاوت‌اند."
    return
}

if ([string]::IsNullOrWhiteSpace($FiscalId)) {
    throw "برای ارسال GET_TOKEN، -FiscalId الزامی است (یا برای مقایسهٔ کلیدها -Fingerprint استفاده کنید)."
}

# ---------- ارسال یک GET_TOKEN ----------
function Send-GetToken([string]$fid, [System.Security.Cryptography.RSA]$key) {
    $uid     = [guid]::NewGuid().ToString()
    $traceId = [guid]::NewGuid().ToString()
    $ts      = [DateTimeOffset]::Now.ToUnixTimeMilliseconds().ToString()

    # مقادیر به ترتیب الفبایی کلیدهای نرمال‌شده:
    # Data.Username, EncryptionKeyId, FiscalId, Iv, PacketType, Retry, SymmetricKey, Uid, requestTraceId, timestamp
    $values = @($fid, "#", $fid, "#", "GET_TOKEN", "false", "#", $uid, $traceId, $ts)
    $normalized = ($values -join "#")

    $sigBytes = $key.SignData([Text.Encoding]::UTF8.GetBytes($normalized),
                              [System.Security.Cryptography.HashAlgorithmName]::SHA256,
                              [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $signature = [Convert]::ToBase64String($sigBytes)

    $bodyObj = [ordered]@{
        signature      = $signature
        signatureKeyId = ""
        packet         = [ordered]@{
            uid              = $uid
            packetType       = "GET_TOKEN"
            retry            = $false
            data             = [ordered]@{ username = $fid }
            encryptionKeyId  = $null
            symmetricKey     = $null
            iv               = $null
            fiscalId         = $fid
            dataSignature    = $null
            signatureKeyId   = $null
        }
    }
    $body = $bodyObj | ConvertTo-Json -Depth 6 -Compress

    $uri = $Base.TrimEnd('/') + "/" + $ClientTypeSegment + "/sync/GET_TOKEN"
    Write-Host ""
    Write-Host "── GET_TOKEN | fiscalId=$fid"
    Write-Host "   URL: $uri"
    try {
        $resp = Invoke-WebRequest -Uri $uri -Method POST -ContentType "application/json" `
                 -Body ([Text.Encoding]::UTF8.GetBytes($body)) `
                 -Headers @{ requestTraceId = $traceId; timestamp = $ts } `
                 -SkipHttpErrorCheck -TimeoutSec 60
        Write-Host "   HTTP $($resp.StatusCode)"
        Write-Host "   پاسخ خام سامانه:"
        Write-Host $resp.Content
    } catch {
        Write-Host "   خطای شبکه: $($_.Exception.Message)"
    }
}

# ---------- سلامت آدرس (بدون امضا) ----------
Write-Host "=============================================="
Write-Host "مرحله ۱: سلامت آدرس (GET_SERVER_INFORMATION)"
Write-Host "=============================================="
$uri = $Base.TrimEnd('/') + "/" + $ClientTypeSegment + "/sync/GET_SERVER_INFORMATION"
try {
    $resp = Invoke-WebRequest -Uri $uri -Method POST -ContentType "application/json" -Body "{}" -SkipHttpErrorCheck -TimeoutSec 30
    Write-Host "HTTP $($resp.StatusCode)"
    Write-Host $resp.Content
} catch {
    Write-Host "خطای شبکه: $($_.Exception.Message)"
}

Write-Host ""
Write-Host "=============================================="
Write-Host "مرحله ۲: احراز هویت (GET_TOKEN)"
Write-Host "=============================================="
Send-GetToken -fid $FiscalId -key $rsa
