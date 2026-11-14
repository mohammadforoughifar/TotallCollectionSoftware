# گام ۴ — گزارش بسته‌شدن (بخشی)

شاخهٔ `lic/step-4` · تاریخ 2026-11-14 · شاهدها: `docs/reports/evidence/stage4-licensingdb.log`

## وضعیت زیرمرحله‌ها

| زیرمرحله | وضعیت | شاهد / توضیح |
|---|---|---|
| ۴.۱ ابزار صدور `tools/LicenseTool` | ✅ | `tools/LicenseTool/Program.cs`, `presets.json`, `LicenseTool.csproj` |
| ↳ `keygen` (کلید رمزنگاری‌شده، اثر انگشت، ردیف manifest) | ✅ | اجرا شد؛ کلید خصوصی ۰۶۰۰ |
| ↳ `issue` با profile/presets/request/installation-id/--existing | ✅ | پیش‌نمایش ادغام + `--yes` اجباری |
| ↳ `preview` (Entitlement مؤثر، وضعیت ۳۳ ماژول، تعارض‌ها، هشدارها) | ✅ | خروجی CLI |
| ↳ `bridge --days N` (۳۳ ماژول، Subscription، users: None) | ✅ | اجرا شد؛ ۳۳ ماژول فعال |
| ↳ `verify` / `inspect-request` | ✅ | اجرا شد |
| ↳ دفتر صدور `issued-licenses.jsonl` خارج از مخزن | ✅ | پیش‌فرض `~/.licensetool/…` یا `LICENSETOOL_JOURNAL` |
| ↳ `license-sign.py` به ابزار توسعه تبدیل شد | ✅ | سرصفحهٔ سند + `LICENSING-DEPLOYMENT-FA.md` |
| ۴.۲ غنی‌سازی فایل درخواست (هش مؤلفه‌های ثانویه) | ❌ انجام نشد | `GET /api/license/request-file` فعلی فقط installationId/مشتری/ماشین را دارد |
| ۴.۳ بایند نصب (GUID اصلی + دو مؤلفهٔ ثانویه + rehostRecommended) | ❌ انجام نشد | ساختار فعلی `InstallationIdentity` پایه است، منطق آستانه ندارد |
| ۴.۴ ضد دستکاری ساعت (سه محل + «اکنونِ مؤثر») | ❌ انجام نشد | `ClockAnchor` فعلاً فقط قلاب تست است |
| ۴.۵ نسخه‌های ساخت (SaaS/OnPrem) | ✅ | در گام صفر: `.pubxml` + `LicenseCheck --deployment` + کد خروج ۷ |
| ۴.۶ رفت‌وبرگشت امضا | ✅ | `tests/LicensingDb` بخش ۳۰ + تأیید مستقل با `openssl dgst -sha256 -verify … → Verified OK` |

## فرض‌ها

1. کلید خصوصی همیشه از `--private-key` یا `LICENSETOOL_PRIVATE_KEY` خوانده می‌شود؛ رمز آن از `LICENSETOOL_KEY_PASSWORD`. هیچ کلید خصوصی در مخزن ساخته/ذخیره نشد.
2. الگوریتم امضا `RS256` (همان `LicenseVerifier`)؛ پوشش `{version,keyId,algorithm,payload,signature}` و امضا روی `LicenseJson.ToPayloadBytes`.
3. `presets.json` کنار ابزار منتشر می‌شود (چهار پیش‌تنظیم: مالی، منابع انسانی، انبار و فروش، اتوماسیون اداری).
4. ابزار عمداً در `publish-server.bat` منتشر نمی‌شود؛ تست بخش ۳۱ همین را اجبار می‌کند.
5. برای `bridge` تاریخ پایان صریح (`until`) نوشته می‌شود تا «۳۰ روز» دقیق باشد، نه ۳۰×۱۲/۳۶۵ ماه.

## فایل‌های تغییرکرده

`tools/LicenseTool/{Program.cs, LicenseTool.csproj, presets.json}` (نو) · `tools/license-sign.py` (سرصفحه) · `.gitignore` · `tests/LicensingDb/Program.cs`

## باقی‌ماندهٔ گام ۴

۴.۲ فایل درخواست غنی · ۴.۳ بایند نصب · ۴.۴ ضد دستکاری ساعت (سه محل ذخیره، آستانه‌های ۲۴ ساعت، ReadOnly خودبازگشت). سپس گام ۶ (Update-App.ps1)، گام ۵ (رابط Blazor) و E2E.
