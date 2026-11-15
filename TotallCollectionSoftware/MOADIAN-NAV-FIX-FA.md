# رفع باز شدن هم‌زمان «سامانه فروشگاهی» با زیرمنوهای «سامانه مودیان»

## علت
در `inventory/src/Inventory.Client/Layout/NavMenu.razor` گروه «سامانه مودیان» به‌اشتباه با گروه «سامانه فروشگاهی» گره خورده بود:

```csharp
// OnInitialized
gOps = ... || p.StartsWith("moadian") || ...;
// OnLocationChanged
if (path.StartsWith("moadian")) gOps = true;
if (path.StartsWith("moadian/settings")) gOpsBase = true;   // مسیری با این نام وجود ندارد
```

یعنی هر بار که کاربـر روی یکی از زیرمنوهای مودیان (`moadian`, `moadian/invoices`, `moadian/invoices/new`, `moadian/basic-info`, `moadian/guide`) کلیک می‌کرد، پرچم `gOps` روشن و گروه «سامانه فروشگاهی» هم باز می‌شد.

نقص دوم: پرچم `gMoadian` هیچ‌جاک از مسیر جاری مقدار نمی‌گرفت؛ پس با ورود مستقیم به آدرس مودیان (رفرش صفحه یا لینک مستقیم) گروه مودیان بسته می‌ماند و فقط فروشگاهی باز می‌شد.

## اصلاح
- حذف `moadian` از شرط‌های `gOps` در `OnInitialized` و `OnLocationChanged`.
- حذف `moadian/settings` از شرط‌های `gOpsBase` (این مسیر در پروژه وجود ندارد؛ زیرمنوی «اطلاعات پایه» مودیان آدرسش `moadian/basic-info` است).
- افزودن مقدار‌دهی مستقل: `gMoadian = p.StartsWith("moadian")` در `OnInitialized` و `if (path.StartsWith("moadian")) gMoadian = true;` در `OnLocationChanged`.

نتیجه: هر گروه فقط با مسیرهای خودش باز می‌شود:

| گروه | مسیرهای بازکننده |
|---|---|
| سامانه مودیان | `moadian`, `moadian/invoices`, `moadian/invoices/new`, `moadian/invoices/{id}`, `moadian/basic-info`, `moadian/guide`, `moadian/connection`, `moadian/customers`, `moadian/goods`, `moadian/units` |
| سامانه فروشگاهی | `orders/*`, `goods-issues`, `stock`, `repairs`, `expenses`, `trs/vouchers`, `operations/reports/*`, اطلاعات پایه (`products`, `categories`, `units`, `warehouses`, `parties`, `referrers`) |

## تست
1. نوار کناری را ببندید (همه گروه‌ها)، سپس از گروه «سامانه مودیان» روی «صورتحساب‌ها» کلیک کنید → فقط گروه مودیان باز/فعال می‌شود و «سامانه فروشگاهی» بسته می‌ماند.
2. همین تست برای «نمای کلی»، «صورتحساب جدید»، «اطلاعات پایه»، «راهنما».
3. رفرش مستقیم روی `/moadian/invoices` → گروه مودیان خودکار باز است.
4. رفتن به «سامانه فروشگاهی ← بیجک» → فقط گروه فروشگاهی باز می‌شود و مودیان بسته می‌ماند.
5. بازگشت از مودیان به یک صفحهٔ فروشگاهی و برعکس، بدون باز شدن هم‌زمان دو گروه.

## فایل‌های تغییر‌یافته
- `inventory/src/Inventory.Client/Layout/NavMenu.razor`
