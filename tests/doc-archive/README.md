# تست یکپارچه آرشیو اسناد

اسکریپت‌های تست end-to-end که مستقیماً روی API در حال اجرا کار می‌کنند.

| فایل | محتوا |
|---|---|
| `lib.py` | توابع کمکی (فراخوانی HTTP، لاگین، گزارش نتیجه) |
| `run.py` | جریان کاری اصلی — ۷۲ تست |
| `edge.py` | سناریوهای مرزی و تداخل فازها — ۳۲ تست |
| `compare.py` | مقایسه دو ورژن — ۲۸ تست |

**جمع: ۱۳۲ تست**

## اجرا

```bash
# ۱) سرور را با یک دیتابیس تست بالا بیاورید (نه دیتابیس اصلی!)
cd inventory/src/Inventory.Api
Database__Provider=Sqlite ConnectionStrings__Default="Data Source=/tmp/e2e.db" \
  dotnet run --urls http://0.0.0.0:5100

# ۲) در ترمینال دیگر
cd tests/doc-archive
python3 run.py && python3 edge.py && python3 compare.py
```

خروجی: `0` یعنی همه موفق، `1` یعنی حداقل یک تست ناموفق.

آدرس API از متغیر محیطی `API_BASE` خوانده می‌شود (پیش‌فرض `http://localhost:5100`).

> ⚠️ این اسکریپت‌ها کاربر و نقش تست می‌سازند (`qa_*`, `cmp_*`).
> **روی دیتابیس عملیاتی اجرا نکنید.**
>
> هر اسکریپت روی دیتابیس **تازه** اجرا شود؛ `run.py` و `edge.py` به‌ترتیب
> وابسته‌اند و `compare.py` مستقل است.

گزارش کامل آخرین اجرا: [`TEST-DOC-ARCHIVE.md`](../../TEST-DOC-ARCHIVE.md)
