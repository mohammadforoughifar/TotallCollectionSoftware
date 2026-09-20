# گزارش ممیزی برنچ‌ها — ۲۰۲۶/۰۹/۲۰

هدف: پاسخ قطعی به این پرسش که «آیا کامیتی از برنچ `Hemmati-Otomation` روی `main` پوش‌نشده باقی مانده است؟»
و تعیین تکلیف PR باز شمارهٔ ۳۳.

تمام اعدادِ این سند خروجیِ مستقیمِ دستوراتِ git هستند و در تاریخِ بالا روی مخزن اندازه‌گیری شده‌اند.

---

## ۱) نتیجهٔ خلاصه

| پرسش | پاسخ |
|---|---|
| کامیتِ پوش‌نشده از `Hemmati-Otomation` روی `main`؟ | **خیر — صفر کامیت.** کل کارِ آن برنچ در `main` ادغام شده است. |
| تاریخچهٔ `main` سالم است؟ | **بله.** ۱۴۲ کامیت، ۱ ریشه، کلون shallow نیست. |
| آیا PR شمارهٔ ۳۳ باید ادغام شود؟ | **خیر.** محتوایش ۱۰۰٪ در `main` است؛ ادغامِ اجباری‌اش ۲۱٬۴۵۴ خط از `main` حذف می‌کند. |

---

## ۲) برنچ `Hemmati-Otomation` کاملاً ادغام شده است

```console
$ git rev-list --count origin/main..origin/Hemmati-Otomation
0
$ git rev-list --count 8e4fa02..origin/Hemmati-Otomation
0
$ git merge-base --is-ancestor origin/Hemmati-Otomation origin/main && echo YES
YES
```

کامیتِ ادغام‌کننده در `main`:

```console
$ git log --oneline --ancestry-path 51e70c8..origin/main --merges | tail -1
aa2291d Merge remote-tracking branch 'origin/main'
```

`51e70c8` همان HEAD برنچ `Hemmati-Otomation` است
(`feat(incoming-letters): ثبت و نهایی‌سازی زیرسیستم و کارتابل نامه وارده در اتوماسیون اداری`).

### تفاوتِ محتوایی به نفع `main` است، نه به نفع برنچ

```console
$ git diff --shortstat origin/main origin/Hemmati-Otomation
 54 files changed, 112 insertions(+), 3893 deletions(-)

$ comm -13 <(git ls-tree -r --name-only origin/main | sort) \
           <(git ls-tree -r --name-only origin/Hemmati-Otomation | sort) | wc -l
0          # هیچ فایل منحصربه‌فردی در برنچ نیست

$ comm -23 <(git ls-tree -r --name-only origin/main | sort) \
           <(git ls-tree -r --name-only origin/Hemmati-Otomation | sort) | wc -l
15         # ۱۵ فایل که فقط در main هستند
```

از آن ۱۵ فایل، ۵ تای آن‌ها کلِ «سیستم تب‌های کاری» هستند:

```text
inventory/src/Inventory.Client/Layout/TabBar.razor
inventory/src/Inventory.Client/Layout/TabPages.razor
inventory/src/Inventory.Client/Layout/TabRouteView.razor
inventory/src/Inventory.Client/Services/TabService.cs
inventory/src/Inventory.Client/wwwroot/js/tabs.js
```

### هشدار: کامیتِ پاکسازیِ آن برنچ حذفِ گسترده دارد

```console
$ git show --shortstat --format='%h %s' 159d699
159d699 پاکسازی: حذف فایل‌های بی‌اثر در اجرای برنامه
 92 files changed, 15854 deletions(-)
```

**نتیجهٔ عملی:** ادغامِ `Hemmati-Otomation` روی `main` هیچ کدِ تازه‌ای نمی‌آورد و در عوض
نسخه‌های قدیمی‌تر را برمی‌گرداند و سیستم تب‌ها را حذف می‌کند. انجام نشد.

---

## ۳) سلامتِ تاریخچهٔ `main`

```console
$ git rev-list --count origin/main
142
$ git rev-list --max-parents=0 origin/main | wc -l
1
$ git rev-parse --is-shallow-repository
false
```

### دامِ کلونِ shallow (درسِ این ممیزی)

در یک کلونِ shallow، دستورِ `--max-parents=0` مرزِ برش را «کامیتِ ریشه» نشان می‌دهد و
`git rev-list --count` هم تعدادِ کامیت‌ها را کمتر از واقع گزارش می‌کند. در همین مخزن پیش از
`git fetch --unshallow origin main` دو نتیجهٔ **غلط** به دست آمد:

| ادعای غلط | واقعیت |
|---|---|
| «`main` با یک کامیتِ ریشه‌ای بازنویسی (force-push) شده و تاریخچه پاک شده است» | تاریخچه سالم است؛ ۱۴۲ کامیت و ۱ ریشه |
| «`Hemmati-Otomation` ۱۳۷ کامیت جلوتر از `8e4fa02` است» | `git rev-list --count 8e4fa02..origin/Hemmati-Otomation` → **۰** |

**قاعده:** پیش از هر تحلیلِ تاریخچه، `git rev-parse --is-shallow-repository` را چک کنید و در صورت
نیاز `git fetch --unshallow` بزنید.

---

## ۴) PR شمارهٔ ۳۳ (`arena/dabirkhane-improvements` → `main`)

```console
$ gh pr view 33 --json state,mergeable,mergeStateStatus,changedFiles,additions,deletions
state=OPEN  mergeable=CONFLICTING  mergeState=DIRTY  files=27  +3179/-116

$ git rev-list --count origin/main..91ddd6f
1
```

آزمایشِ ادغام روی یک برنچِ موقت (بدون پوش) نشان داد هر ۳ کانفلیکت **فقط کامنتِ مستندسازی** هستند
(سمتِ `main` یک `/// <summary>` یا `@* … *@` اضافه دارد):

```text
inventory/src/Inventory.Api/Controllers/Office/IncomingLettersController.cs
inventory/src/Inventory.Api/Services/Office/IncomingLetterService.cs
inventory/src/Inventory.Client/Pages/Office/Letters/Incoming/IncomingLetterCartable.razor
```

پس از حلِ هر سه به نفع `main`:

```console
$ git diff --name-only origin/main | wc -l
0          # محتوای PR کاملاً در main است؛ ادغامش صفر تغییر کد می‌داد
```

اما خودِ PR از `8e4fa02` منشعب شده و اگر به زور ادغام شود، `main` عقب می‌رود:

```console
$ comm -13 <(git ls-tree -r --name-only origin/main | sort) \
           <(git ls-tree -r --name-only 91ddd6f | sort) | wc -l
102        # فایل منحصربه‌فردِ PR

$ git diff --shortstat 91ddd6f origin/main
 184 files changed, 9678 insertions(+), 21455 deletions(-)
```

**پیشنهاد:** بستنِ PR شمارهٔ ۳۳ (نه ادغام). تصمیم نهایی با نگهدارندهٔ مخزن است؛ در این ممیزی
بسته نشد.

---

## ۵) راستی‌آزماییِ نشده

هیچ بیلدی در محیطِ ممیزی اجرا نشد: `dotnet` نصب نبود و دسترسی به `api.nuget.org` مسدود بود
(`curl: (35) OpenSSL SSL_connect: SSL_ERROR_SYSCALL`). بنابراین **سلامتِ کامپایل تأیید نشده است**.
هیچ تغییرِ کدی هم در این ممیزی داده نشد که نیاز به بیلد داشته باشد.
