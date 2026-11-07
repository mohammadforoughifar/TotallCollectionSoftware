# ============================================================
#  تست دود (smoke test) ماژول مودیان — بررسی سلامت ثبت/ویرایش/خواندن
#  اجرا:  pwsh ./tools/moadian-smoke-test.ps1   (یا پاورشل ویندوز 5.1)
#  پیش‌نیاز: API روی http://localhost:5100 بالا باشد.
#  نتایج: PASS/FAIL هر سناریو چاپ می‌شود؛ خروجی غیر 0 = حداقل یک FAIL.
#  این اسکریپت فقط دادهٔ تست (شماره تصادفی در هر اجرا) می‌سازد و خودش پاک می‌کند.
# ============================================================
param(
    [string]$BaseUrl = "http://localhost:5100"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http 2>$null | Out-Null

$script:Fail = 0
$script:Pass = 0

function Check([string]$name, [bool]$ok, [string]$extra = "") {
    if ($ok) { $script:Pass++; Write-Host "  PASS  $name" -ForegroundColor Green }
    else     { $script:Fail++; Write-Host "  FAIL  $name  $extra" -ForegroundColor Red }
}

function Invoke-Moadian {
    param([string]$Method, [string]$Path, $Body = $null)
    $uri = "$BaseUrl/Moadian/$Path"
    $opts = @{ Method = $Method; Uri = $uri; ContentType = "application/json; charset=utf-8" }
    if ($null -ne $Body) {
        $json = ($Body | ConvertTo-Json -Depth 12 -Compress)
        $opts.Body = [System.Text.Encoding]::UTF8.GetBytes($json)
    }
    try { return Invoke-RestMethod @opts }
    catch {
        $msg = $_.Exception.Message
        if ($_.ErrorDetails) { $msg += " | " + $_.ErrorDetails.Message }
        throw $msg
    }
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  تست دود ماژول مودیان  ←  $BaseUrl" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$runId = Get-Random -Minimum 100000 -Maximum 999999
$nid   = "99$runId"               # شناسهٔ ملی تستی یکتا
$cpc   = "1000000000$($runId % 10)" -replace "(.{11}).*", '$1'   # CPC یازده‌رقمی
$cpid  = 0

# ---------- ۰) دسترس‌پذیری ----------
Write-Host "`n[۰] دسترس‌پذیری endpoint"
try {
    $r = Invoke-Moadian -Method GET -Path "GetAllUnitOfMeasurementAsync?searchTerm=&page=1&pageSize=1"
    Check "GET /Moadian/... (routing)" ($r.success -eq $true)
} catch { Check "GET /Moadian/... (routing)" $false "$_" }

# ---------- ۱) شرکت (CpInfo) ----------
Write-Host "`n[۱] ثبت/ویرایش/خواندن شرکت"
try {
    $r = Invoke-Moadian -Method POST -Path "SaveCpInfo" -Body @{
        cpinfoid = 0; persianName = "شرکت تست دود $runId"; nationalid = $nid; economicNumber = "12345678"; type = "حقوقی";
        englishName = "Smoke Test Co $runId"; postalCode = "1314747474"
    }
    Check "SaveCpInfo (ثبت جدید)" ($r.success -eq $true) ($r.message)
    $cpid = $r.data.cpinfoid
    Check "cpinfoid برگشتی > 0" ($cpid -gt 0)

    $r2 = Invoke-Moadian -Method POST -Path "SaveCpInfo" -Body @{
        cpinfoid = 0; persianName = "تکثیر"; nationalid = $nid; type = "حقوقی"
    }
    Check "رد شناسهٔ ملی تکراری (400)" ($r2.success -eq $false -and $r2.status -eq 400) ($r2.message)

    $r3 = Invoke-Moadian -Method POST -Path "SaveCpInfo" -Body @{
        cpinfoid = $cpid; persianName = "شرکت تست دود $runId (ویرایش)"; nationalid = $nid; economicNumber = "87654321"; type = "حقیقی";
        englishName = "Edited Co"; postalCode = "1511919191"
    }
    Check "SaveCpInfo (ویرایش)" ($r3.success -eq $true) ($r3.message)

    $r4 = Invoke-Moadian -Method GET -Path "GetCpInfos"
    $found = $r4.data | Where-Object { $_.cpinfoid -eq $cpid }
    Check "GetCpInfos شامل شرکت جدید" ($null -ne $found)
    Check "نوع حقیقی/حقوقی ذخیره می‌شود" ($found.type -eq "حقیقی") ("type=$($found.type)")
    Check "EnglishName در ویرایش ذخیره می‌شود" ($found.englishName -eq "Edited Co") ("englishName=$($found.englishName)")
    Check "PostalCode در ویرایش ذخیره می‌شود" ($found.postalCode -eq "1511919191") ("postalCode=$($found.postalCode)")

    $r5 = Invoke-Moadian -Method POST -Path "SaveCpInfo" -Body @{ cpinfoid = 0; persianName = ""; nationalid = "x" }
    Check "رد نام خالی (400)" ($r5.success -eq $false -and $r5.status -eq 400)
} catch { Check "بخش شرکت کلی" $false "$_" }

# ---------- ۲) سال مالی ----------
Write-Host "`n[۲] سال مالی"
try {
    $r = Invoke-Moadian -Method POST -Path "SaveFiscalYear" -Body @{
        cpInfoId = $cpid; title = "سال تست $runId"; startDate = "2026-01-01T00:00:00"; endDate = "2026-12-29T00:00:00"; isActive = $true
    }
    Check "SaveFiscalYear" ($r.success -eq $true) ($r.message)

    $r2 = Invoke-Moadian -Method GET -Path "GetFiscalYears/$cpid"
    Check "GetFiscalYears برگردان لیست" ($r2.success -eq $true -and $r2.data.Count -ge 1) ($r2.message)
    $fy = $r2.data[0]
    Check "عنوان سال مالی ذخیره می‌شود" ($fy.title -eq "سال تست $runId") ("title=$($fy.title)")
    Check "وضعیت فعال ذخیره می‌شود" ($fy.isActive -eq $true) ("isActive=$($fy.isActive)")

    $r3 = Invoke-Moadian -Method POST -Path "SaveFiscalYear" -Body @{
        cpInfoId = $cpid; startDate = "2026-12-29T00:00:00"; endDate = "2026-01-01T00:00:00"
    }
    Check "رد بازهٔ معکوس (400)" ($r3.success -eq $false -and $r3.status -eq 400)
} catch { Check "بخش سال مالی کلی" $false "$_" }

# ---------- ۳) واحد سنجش ----------
Write-Host "`n[۳] واحد سنجش"
$unitId = 0
try {
    $r = Invoke-Moadian -Method POST -Path "InsertUnitOfMeasurementAsync" -Body @{ unitOfMeasurementId = 0; name = "واحد تست$runId"; code = "T$($runId % 100000)" }
    Check "InsertUnit" ($r.success -eq $true -or $r.status -eq 201) ($r.message)
    $unitId = $r.data.id

    if ($unitId -gt 0) {
        $r2 = Invoke-Moadian -Method PUT -Path "UpdateUnitOfMeasurementAsync" -Body @{ unitOfMeasurementId = $unitId; name = "واحد تست$runId (و)"; code = "T$($runId % 100000)" }
        Check "UpdateUnit" ($r2.success -eq $true -or $r2.status -eq 201) ($r2.message)
    } else { Write-Host "  SKIP  UpdateUnit (id=0)" -ForegroundColor Yellow }

    $r3 = Invoke-Moadian -Method GET -Path "GetAllUnitOfMeasurementAsync?searchTerm=T$($runId % 100000)&page=1&pageSize=10"
    Check "GetAllUnitOfMeasurement (جستجو)" ($r3.success -eq $true) ($r3.message)
} catch { Check "بخش واحد کلی" $false "$_" }

# ---------- ۴) کالا/خدمت ----------
Write-Host "`n[۴] کالا/خدمت — بدون واحد و با واحد"
$goodId = 0
try {
    # سناریوی باگ قبلی: بدون واحد (UnitOfMeasurementId=0) نباید 500 شود
    $r = Invoke-Moadian -Method POST -Path "InsertGoodOrServiceAsync" -Body @{
        goodsOrServicesId = 0; cpInfoId = $cpid; name = "کالای تست بدون واحد $runId"; uniqueIdentifier = $cpc;
        unitOfMeasurementId = 0; unitOfMeasurement = $null; valueAddedPercentage = 9; price = 1000
    }
    Check "InsertGood بدون واحد (رفع NullRef)" ($r.success -eq $true -or $r.status -eq 201) ($r.message)
    $goodId = $r.data.id; if (-not $goodId) { $goodId = $r.data.Id }

    if ($goodId -gt 0 -and $unitId -gt 0) {
        $r2 = Invoke-Moadian -Method PUT -Path "UpdateGoodOrService" -Body @{
            goodsOrServicesId = $goodId; cpInfoId = $cpid; name = "کالای تست (و)"; uniqueIdentifier = $cpc;
            unitOfMeasurementId = $unitId; unitOfMeasurement = "واحد تست"; valueAddedPercentage = 10; price = 2000
        }
        Check "UpdateGood (با واحد)" ($r2.success -eq $true -or $r2.status -eq 201) ($r2.message)
    } else { Write-Host "  SKIP  UpdateGood" -ForegroundColor Yellow }

    $r3 = Invoke-Moadian -Method POST -Path "InsertGoodOrServiceAsync" -Body @{
        goodsOrServicesId = 0; cpInfoId = $cpid; name = ""; uniqueIdentifier = $cpc; price = 0
    }
    Check "رد کالای ناقص (400 + پیام فارسی)" ($r3.success -eq $false -and $r3.status -eq 400 -and $r3.message.Length -gt 10) ($r3.message)

    $r4 = Invoke-Moadian -Method GET -Path "GetGoodOrService?cpinfoId=$cpid&searchTerm=&page=1&pageSize=20"
    Check "GetGoodOrService لیست" ($r4.success -eq $true) ($r4.message)
} catch { Check "بخش کالا کلی" $false "$_" }

# ---------- ۵) مشتری ----------
Write-Host "`n[۵] مشتری"
$custId = 0
try {
    $r = Invoke-Moadian -Method POST -Path "InsertCustomerAsync" -Body @{
        customerId = 0; cpInfoId = $cpid; name = "مشتری تست $runId"; postalCode = "1111111111"; phone = "09120000001";
        nationalID = "1234567890"; type = "حقیقی"; economicNumber = "1"; address = "آدرس تست"
    }
    Check "InsertCustomer" ($r.success -eq $true -or $r.status -eq 201) ($r.message)
    $custId = $r.data.customerId; if (-not $custId) { $custId = $r.data.CustomerId }

    if ($custId -gt 0) {
        $r2 = Invoke-Moadian -Method PUT -Path "UpdateCustomer" -Body @{
            customerId = $custId; cpInfoId = $cpid; name = "مشتری تست (و)"; postalCode = "1111111111"; phone = "09120000001";
            nationalID = "1234567890"; type = "حقوقی"; economicNumber = "2"; address = "تست۲"
        }
        Check "UpdateCustomer" ($r2.success -eq $true -or $r2.status -eq 200) ($r2.message)
    }

    $r3 = Invoke-Moadian -Method GET -Path "GetAllCostomer?cpinfoid=$cpid"
    Check "GetAllCostamer لیست" ($r3.success -eq $true -and $r3.data.Count -ge 1) ($r3.message)
} catch { Check "بخش مشتری کلی" $false "$_" }

# ---------- ۶) صورتحساب ----------
Write-Host "`n[۶] صورتحساب"
try {
    $fy = (Invoke-Moadian -Method GET -Path "GetFiscalYears/$cpid").data[0]
    $invoice = @{
        id = 0; isNew = $true; customerId = $custId; numberInvoice = $null; fiscalYearId = "$($fy.id)";
        cPInfoId = $cpid; typePattern = "نوع اول"; dateOrder = $null; dateOrderPicker = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ss");
        paymentType = "نقدی"; description = "تست دود"; subject = "اصلی"; showButtons = $true;
        totallMoney = 1000; priceNaghd = 1000;
        invoiceItems = @(@{
            goodsOrServicesId = $goodId; uniqueIdentifier = $cpc; count = 1; priceUnit = 1000;
            valueAddedPercentage = 0; discount = 0; name = "کالای تست"
        })
    }
    $r = Invoke-Moadian -Method POST -Path "AddInvoice" -Body $invoice
    Check "AddInvoice (ثبت)" ($r.success -eq $true -or $r.status -eq 201) ($r.message)
    $invId = $r.data.id; if (-not $invId) { $invId = $r.data.Id }

    if ($invId -gt 0) {
        $r2 = Invoke-Moadian -Method GET -Path "GetInvoiceByIdAsync/$invId"
        Check "GetInvoiceById" ($r2.success -eq $true) ($r2.message)

        $r3 = Invoke-Moadian -Method GET -Path "GetAllInvoice?firstDate=&endDate=&operation=%D9%87%D9%85%D9%87&CpinfoId=$cpid&searchTerm=&page=1&pageSize=10"
        Check "GetAllInvoice" ($r3.success -eq $true) ($r3.message)

        $r4 = Invoke-Moadian -Method DELETE -Path "Invoice/$invId"
        Check "DeleteInvoice" ($r4.success -eq $true) ($r4.message)
    } else { Write-Host "  SKIP  سایر آزمون‌های صورتحساب (id=0)" -ForegroundColor Yellow }
} catch { Check "بخش صورتحساب کلی" $false "$_" }

# ---------- ۷) گارد تست اتصال ----------
Write-Host "`n[۷] گارد GetIsConnectToMoadian"
try {
    $r = Invoke-Moadian -Method POST -Path "GetIsConnectToMoadian" -Body @{ taxMemoryID = "TEST"; webServiceAddress = "https://x"; filePrivatekey = $null }
    Check "بدون فایل کلید → 400 پیام فارسی (نه 500)" ($r.success -eq $false -and $r.status -eq 400) ($r.message)

    # حالت دوم: با cpinfoid — سرور رکورد اتصال را از بانک و کلید را از uploads/{نام شرکت} می‌خواند
    $r2 = Invoke-Moadian -Method POST -Path "GetIsConnectToMoadian" -Body @{ cpinfoid = $cpid }
    Check "اتصال‌نثبت → 400 پیام فارسی (نه 500)" ($r2.success -eq $false -and $r2.status -eq 400) ($r2.message)
} catch { Check "گارد تست اتصال" $false "$_" }

# ---------- ۸) حذف شرکت و بازیابی (پاک‌سازی پایانی) ----------
Write-Host "`n[۸] حذف نرم و ثبت مجدد همان شناسهٔ ملی"
try {
    $r = Invoke-Moadian -Method DELETE -Path "DeleteCpInfo/$cpid"
    Check "DeleteCpInfo" ($r.success -eq $true) ($r.message)

    $r2 = Invoke-Moadian -Method GET -Path "GetCpInfos"
    $still = $r2.data | Where-Object { $_.cpinfoid -eq $cpid }
    Check "شرکت حذف‌شده از فهرست نمی‌آید" ($null -eq $still)

    $r3 = Invoke-Moadian -Method DELETE -Path "DeleteCpInfo/$cpid"
    Check "حذف شرکت ناموجود → 404" ($r3.success -eq $false -and $r3.status -eq 404) ($r3.message)

    # سناریو واقعی: ثبت شرکت بعد از حذف نرم با همان شناسهٔ ملی — باید بازیابی موفق باشد (نه خطای تکراری)
    $r4 = Invoke-Moadian -Method POST -Path "SaveCpInfo" -Body @{
        cpinfoid = 0; persianName = "شرکت تست دود $runId (بازیابی)"; nationalid = $nid; type = "حقوقی"
    }
    Check "ثبت مجدد بعد از حذف نرم (بدون خطای تکراری)" ($r4.success -eq $true) ($r4.message)
    Check "cpinfoid همان رکورد حذف‌شده بازیابی شد" ($r4.data.cpinfoid -eq $cpid) ("cpinfoid=$($r4.data.cpinfoid)")

    $r5 = Invoke-Moadian -Method GET -Path "GetCpInfos"
    $again = $r5.data | Where-Object { $_.cpinfoid -eq $cpid }
    Check "شرکت بازیابی‌شده دوباره در فهرست است" ($null -ne $again)

    # پاک‌سازی نهایی
    Invoke-Moadian -Method DELETE -Path "DeleteCpInfo/$cpid" | Out-Null
} catch { Check "بخش حذف/بازیابی شرکت" $false "$_" }

# ---------- جمع‌بندی ----------
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host ("  نتیجه:  PASS={0}   FAIL={1}" -f $script:Pass, $script:Fail) -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
exit $(if ($script:Fail -eq 0) { 0 } else { 1 })
