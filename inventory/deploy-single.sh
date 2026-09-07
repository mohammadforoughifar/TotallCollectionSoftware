#!/usr/bin/env bash
# A single Inventory client, including HR. Runtime uploads/keys are never deleted.
set -euo pipefail
cd "$(dirname "$0")"

echo "► Publishing Inventory (including HR)…"
dotnet publish src/Inventory.Client -c Release -o publish/client

dest="src/Inventory.Api/wwwroot"
mkdir -p "$dest"
# Remove only regenerated Blazor assets and the retired standalone HR client.
rm -rf "$dest/_framework" "$dest/radis-hr"
cp -R publish/client/wwwroot/. "$dest/"

echo "✔ Inventory + HR published together. Existing uploads and SecureFiles are preserved."
echo "Run: cd src/Inventory.Api && dotnet run"
echo "Open: http://localhost:5100 (HR: /hr)"
