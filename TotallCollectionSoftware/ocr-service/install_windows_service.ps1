# ============================================================
# نصب سرویس OCR به‌صورت خودکار با شروع ویندوز (Task Scheduler)
# اجرا با PowerShell به‌صورت Administrator:
#     powershell -ExecutionPolicy Bypass -File .\install_windows_service.ps1
# حذف:
#     schtasks /Delete /TN "TotallOcrService" /F
# ============================================================
$TaskName = "TotallOcrService"
$Dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Bat = Join-Path $Dir "run_windows.bat"
$Log = Join-Path $Dir "ocr-service.log"

if (-not (Test-Path $Bat)) { Write-Error "run_windows.bat پیدا نشد کنار این اسکریپت."; exit 1 }

# با ورود کاربر اجرا می‌شود (بدون نیاز به ادمین) و خروجی در ocr-service.log می‌رود
$Action = New-ScheduledTaskAction -Execute "cmd.exe" -Argument "/c `"$Bat`" >> `"$Log`" 2>&1"
$Trigger = New-ScheduledTaskTrigger -AtLogOn
$Principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited
$Settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable

Register-ScheduledTask -TaskName $TaskName -Action $Action -Trigger $Trigger `
    -Principal $Principal -Settings $Settings -Description "Totall OCR Service (استخراج متن فارسی/انگلیسی)" -Force

Start-ScheduledTask -TaskName $TaskName
Write-Host "سرویس TotallOcrService ساخته و اجرا شد. لاگ: $Log"
Write-Host "وضعیت:"
Get-ScheduledTask -TaskName $TaskName | Select-Object TaskName, State
