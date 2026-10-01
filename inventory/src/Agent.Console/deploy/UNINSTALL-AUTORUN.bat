@echo off
schtasks /delete /tn "InventoryAgent-Boot" /f
schtasks /delete /tn "InventoryAgent-Hourly" /f
echo Removed.
pause >nul
