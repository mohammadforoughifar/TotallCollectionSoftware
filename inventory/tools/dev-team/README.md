# DevTeam — وب‌هوک گیت و CI (مسیر A)

## پیش‌نیاز سرور

```bash
export DEVTEAM_GIT_WEBHOOK_SECRET='یک-رمز-قوی'
# یا در appsettings: DevTeam:GitWebhookSecret
```

`DevTeam:GitWebhookEnabled` پیش‌فرض `true` است.

## URLها (جایگزین `https://YOUR-HOST`)

| کاربرد | متد | مسیر |
|--------|-----|------|
| GitHub push | POST | `/api/dev-team/hooks/github` |
| GitLab push | POST | `/api/dev-team/hooks/gitlab` |
| CI عمومی | POST | `/api/dev-team/hooks/ci` |
| GitHub Actions | POST | `/api/dev-team/hooks/github-ci` |
| GitLab Pipeline | POST | `/api/dev-team/hooks/gitlab-ci` |
| سلامت | GET | `/api/dev-team/hooks/health` |

احراز: هدر `X-DevTeam-Token: <secret>` یا امضای مخصوص هر provider.

## کامیت

```
fix(office): pagination DT-1405-0003 mod:Office
# یا: task:12
```

## فایل‌ها

- `github-actions-ci.yml` — نمونه workflow که در failure به `hooks/ci` می‌زند
- `gitlab-ci-snippet.yml` — نمونه job گیت‌لب
- `smoke-hooks.sh` — تست health + CI dry failure

## RBAC

- پرمیشن‌های ماژول `DevTeam`: View, Create, Update, Delete, Manage, Assign
- نقش **DevDeveloper**: View/Create/Update/Assign
- **Admin**: همهٔ پرمیشن‌های DevTeam

در UI: میز کار توسعه ← تنظیمات ← کارت «یکپارچه‌سازی».
