#!/usr/bin/env bash
# استقرار تک‌سروره: پابلیش کلاینت Blazor و کپی آن در wwwroot مربوط به API
set -e
cd "$(dirname "$0")"

echo "► پابلیش کلاینت Blazor WebAssembly…"
dotnet publish src/Inventory.Client -c Release -o publish/client

echo "► پابلیش کلاینت RADIS-HR V019…"
dotnet publish modules/RadisHr/src/RadisHr.Client -c Release -o publish/radis-hr

echo "► کپی فایل‌های استاتیک در wwwroot مربوط به API…"
rm -rf src/Inventory.Api/wwwroot
mkdir -p src/Inventory.Api/wwwroot
cp -r publish/client/wwwroot/. src/Inventory.Api/wwwroot/
mkdir -p src/Inventory.Api/wwwroot/radis-hr
cp -r publish/radis-hr/wwwroot/. src/Inventory.Api/wwwroot/radis-hr/

echo "✔ تمام شد (برنامه اصلی + RADIS-HR با ورود و دیتابیس مشترک)."
echo "حالا اجرا کنید:  cd src/Inventory.Api && dotnet run"
echo "سپس در مرورگر باز کنید:  http://localhost:5100"
