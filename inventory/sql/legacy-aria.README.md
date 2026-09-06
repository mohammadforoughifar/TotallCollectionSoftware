# داده‌های مهاجرت سیستم قدیمی «آریا»

فایل‌های خام (`aria-dump.json`، `Import-LegacyAria.sql`، `ReportWork.csv`، `Entryandexitlist*.csv`)
برای کاهش حجم مخزن در آرشیو `legacy-aria.tar.gz` فشرده شده‌اند (۱۱ مگابایت ← ۰٫۹ مگابایت).

برای استخراج:

```bash
tar -xzf sql/legacy-aria.tar.gz -C sql
```

نسخه‌ی خام همچنان در تاریخچه‌ی git موجود است:
`git checkout <commit> -- inventory/sql/legacy-aria`
