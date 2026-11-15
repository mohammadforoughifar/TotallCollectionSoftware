# تست‌های بک‌آپ خودکار

تست‌های Node بدون نیاز به دیتابیس:

```bash
node --test inventory/tests/AutoBackup/*.test.cjs
```

پوشش فعلی:
- heartbeat/close مرورگر
- ثبت سرویس‌ها و worker در API
- idempotent بودن schema
- COPY_ONLY / CHECKSUM / VERIFYONLY
- idempotency و SHA-256 سمت مرکز
- اتصال صفحات تنظیمات و اسکریپت heartbeat

تست‌های واقعی SQL Server/IIS/شبکه باید روی سرور آزمایشی انجام شوند.
