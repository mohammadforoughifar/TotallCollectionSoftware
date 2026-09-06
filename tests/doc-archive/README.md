# تست یکپارچه آرشیو اسناد

اسکریپت‌های تست end-to-end که مستقیماً روی API در حال اجرا کار می‌کنند.

| فایل | محتوا |
|---|---|
| `lib.py` | توابع کمکی (فراخوانی HTTP، لاگین، گزارش نتیجه) |
| `run.py` | جریان کاری اصلی — ۷۲ تست |
| `edge.py` | سناریوهای مرزی و تداخل فازها — ۳۲ تست |

## اجرا

```bash
# ۱) سرور را با یک دیتابیس تست بالا بیاورید (نه دیتابیس اصلی!)
cd inventory/src/Inventory.Api
Database__Provider=Sqlite ConnectionStrings__Default="Data Source=/tmp/e2e.db" \
  dotnet run --urls http://0.0.0.0:5100

# ۲) در ترمینال دیگر
cd tests/doc-archive
python3 run.py && python3 edge.py
```

خروجی: `0` یعنی همه موفق، `1` یعنی حداقل یک تست ناموفق.

> ⚠️ این اسکریپت‌ها کاربر و نقش تست می‌سازند (`qa_manager`, `qa_author`, `qa_review`,
> `qa_guest`). **روی دیتابیس عملیاتی اجرا نکنید.**

گزارش کامل آخرین اجرا: [`TEST-DOC-ARCHIVE.md`](../../TEST-DOC-ARCHIVE.md)
