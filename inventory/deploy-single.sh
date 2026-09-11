#!/usr/bin/env bash
# استقرار تک‌سروره: پابلیش کلاینت Blazor + پابلیش سبک API
# مصرف: ./deploy-single.sh [RID]   مثل: ./deploy-single.sh linux-x64  (پیش‌فرض: linux-x64)
# خروجی نهایی: publish/app  (حدود ۹۰ مگ؛ نیازمند ران‌تایم ‎.NET 8‎ روی سرور)
set -e
cd "$(dirname "$0")"
RID="${1:-linux-x64}"

echo "► [1/3] پابلیش کلاینت Blazor WebAssembly…"
dotnet publish src/Inventory.Client -c Release -o publish/client

echo "► [2/3] کپی فایل‌های استاتیک در wwwroot مربوط به API…"
mkdir -p src/Inventory.Api/wwwroot
find src/Inventory.Api/wwwroot -mindepth 1 -maxdepth 1 ! -name 'README.md' -exec rm -rf {} +
cp -r publish/client/wwwroot/. src/Inventory.Api/wwwroot/

echo "► [3/3] پابلیش سبک API (فقط $RID، بدون ران‌تایم اضافه)…"
dotnet publish src/Inventory.Api/Inventory.Api.csproj -c Release -r "$RID" --self-contained false -o publish/app
rm -rf publish/app/wwwroot/SecureFiles publish/app/wwwroot/uploads

echo ""
echo "✔ تمام شد. حجم خروجی:"
du -sh publish/app
echo ""
echo "اجرا روی سرور (نیازمند ران‌تایم .NET 8):"
echo "    cd publish/app && ./Inventory.Api  (یا: dotnet Inventory.Api.dll)"
echo "سپس در مرورگر باز کنید:  http://localhost:5100"
