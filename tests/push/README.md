# Android PWA Push regression

From repository root:

```bash
dotnet test tests/push/Inventory.Push.Tests.csproj -m:1 -nr:false -p:UseSharedCompilation=false
node tests/push/worker_test.mjs
dotnet build inventory/src/Inventory.Client/Inventory.Client.csproj -m:1 -nr:false -p:UseSharedCompilation=false
python3 -m playwright install chromium
python3 tests/push/ui_test.py
```

Backend: 20 passing cases, in-memory SQLite, generated ephemeral test keys, and a fake HTTP handler (no FCM/network). Includes a real WebPush encryption/request generation test and a **Push-table-only** snapshot check. The pre-existing global chat migration test still reports drift in other modules; it has not been disabled or rewritten.

Worker checks use a modeled service-worker environment. Browser checks run real compiled Blazor + production JavaScript with mock browser PushManager/Notification permission and API responses. They are not an Android delivery or production deployment test. Screenshots are under `~/.cache/push-ui`.

See [deployment, configuration and limitations](../../inventory/docs/ANDROID-PUSH.md). Never put VAPID private keys or subscription authentication keys in test logs, fixtures or Git.
