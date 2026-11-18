# رفع ۹ باگ گزارششدهٔ سامانهٔ فروشگاهی

قراردادهای منبع (`test_shop_bug_fixes.py`) همراه با پروبهای C# موجود
(`ShopPricing/Program.cs` برای کاهش گروهی و `OrderVat/Program.cs` برای عکس
فوری موجودی) رفتارهای زیر را نگه میدارند. این تستها جایگزین بیلد Razor،
تست زندهٔ API و تست مرورگر نیستند؛ SDK داتنت در این محیط موجود نیست.

| # | باگ | محل رفع |
|---|-----|---------|
| ۱ | ارور اعتبارسنجی ولی ذخیره هم انجام میشد | `PartyFormModal.ValidateForm()` + نگهبان نهایی در `Save()`؛ `PartiesController.Save` و `ShopQuickCreate` و `InventoryService.SavePartyAsync` روی سرور همان قواعد را اعمال میکنند. |
| ۲ | فرم تأمینکننده validation نداشت | فیلدها و قواعد برای هر دو `PartyType` یکسان شد؛ دیگر فیلدهای شناسه/کد پستی مشروط به مشتری نیستند. |
| ۳ | طول کد پستی/تلفن در همهٔ فرمها | `ShopIdentifierRules.ValidatePostalCode` (۱۰ رقم)، `ValidatePhone`/`NormalizePhone` (۱۱ رقم) و `ValidateContact`؛ فراخوانی در فرم طرف حساب، انبار، معرف، تعمیرکار، مودیان، شرکت و نگهبان سرور. |
| ۴ | آستانههای اعشاری برای واحد صحیح | `Products.UnitAllowsFraction()` + `ShopQuantityRules.IsWhole` در فرم و `SaveProductAsync` (شامل ورود اکسل). |
| ۵ | «کاهش» فقط افزایش میداد | `select`بهجای بایند bool از رشته (`increase`/`decrease`) استفاده میکند؛ پروب `ShopPricing/Program.cs` کاهش ۱۰٪ را اجرا میکند. |
| ۶ | انتخاب گروه والد | گزینههای دارای فرزند در فرم کالا غیرفعالاند، `CategoryIsParent` جلوی ذخیره را میگیرد، `SaveProductAsync` و `SaveCategoryAsync` روی سرور همان قانون را اعمال میکنند. |
| ۷ | اکسل فقط کالا | `ProductImportKind` (Goods/Services)، دکمه و فایل نمونهٔ جداگانهٔ خدمات، پارامتر `kind` در API/کلاینت، مسیر `IsService` در `ImportProductsAsync`. |
| ۸ | ستونهای ارزش افزوده و وضعیت | ستونهای «ارزش افزوده»، «وضعیت کالا/خدمت» و «شناسه مالیاتی» به قالب اکسل اضافه و توسط `ReadOptionalVat`/`ReadImportActive` خوانده شدند؛ فایلهای قدیمی ۱۰ستونه همچنان پذیرفته میشوند. |
| ۹ | موجودی بدون تاریخ | `GetStockQuantitiesAsOfAsync` اسناد را تا تاریخ سند بازپخش میکند؛ `api/orders/warehouse-stock` پارامتر `asOf` میگیرد، عکس فوری کلاینت با تاریخ کلید میشود و با تغییر تاریخ دوباره بارگذاری میشود، و بررسی موجودی ایجاد/ویرایش/گارانتی فروش همان تاریخ را ملاک قرار میدهد. |

اجرا:

```
python3 -m unittest discover -s inventory/tests/ShopBugFixes -p 'test_*.py' -q
```

تستهای ازپیشموجود: تنها همان ۸ پوشهای که روی `HEAD` هم ناکام بودند
(عمدتاً بهدلیل نبود درخت آینهای `src/` یا فایلهای قالب چاپ) ناکام میمانند؛
هیچ شکست تازهای اضافه نشده است.
