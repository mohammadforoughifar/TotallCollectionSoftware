#!/usr/bin/env python3
"""
ساخت اسکریپت T-SQL مهاجرت داده‌های دیتابیس قدیمی «aria» (ویندوز فرم) به InventoryDb (وب).

ورودی : aria-dump.json (خروجی dump.py از فایل aria.bak)
خروجی : ../Import-LegacyAria.sql

قواعد نگاشت (تأییدشده با کاربر):
- داده‌ها «همان‌طور که در جدول قدیمی هستند» منتقل می‌شوند؛ Idها حفظ می‌شوند (IDENTITY_INSERT).
- کاربران قدیمی (UserId / Operator) با همان Id در جدول Users ساخته می‌شوند اگر وجود نداشته باشند
  (نام کاربری legacy_<id>، غیرفعال، رمز نامعتبر → قابل ورود نیستند تا ادمین ویرایش کند).
- Operator گزارش کار = انجام‌دهندهٔ کار (ستون جدید ReportWorks.OperatorId) — با UserId ثبت‌کننده فرق دارد.
- کد برگشتی RE1/1139 → RE1-1139 ؛ ReturnProjectId = n
- FactorType = 0 → NULL
- گزارش کار: ProjectId از روی کد پروژه؛ اگر کد تکراری بود → پروژه با نزدیک‌ترین تاریخ ورودِ قبل از تاریخ گزارش
  (وگرنه جدیدترین)؛ اگر کدی پیدا نشد → پروژهٔ نگه‌دارنده «کد نامشخص» ساخته می‌شود تا هیچ گزارشی گم نشود.
"""
import json, re, os, collections, datetime

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, '..', 'aria-dump.json')
OUT = os.path.join(HERE, '..', 'Import-LegacyAria.sql')

t = json.load(open(SRC, encoding='utf-8'))
K = t['Karfarma_Tbl']; K1 = {r['KarfarmaId']: r for r in t['Karfarma_Tbl1']}
TF = t['TypeFactorTbl']; E = t['Entryandexitlist']; R = t['ReportWork']; AT = t['AttachAriya_Tbl']

# ---------------------------------------------------------------- helpers
def q(s):
    if s is None: return 'NULL'
    # خطوط جدید داخل متن → CHAR(13)+CHAR(10) نمی‌گذاریم؛ برای یک‌خطی ماندن هر INSERT، به فاصله تبدیل می‌شود
    s = str(s).replace("\r\n", "\n").replace("\r", "\n").replace("\n", " ").replace("'", "''")
    return "N'" + s + "'"

def qn(s, maxlen=None):
    """nvarchar با trim؛ رشته خالی → NULL نمی‌شود (همان‌طور که هست)"""
    if s is None: return 'NULL'
    s = str(s)
    if maxlen and len(s) > maxlen: s = s[:maxlen]
    return q(s)

def qi(v): return 'NULL' if v is None else str(int(v))
def qb(v): return 'NULL' if v is None else ('1' if v else '0')

def qdt(v):
    """'2018-10-18 00:00:00.000' یا '2018-10-18' → datetime2"""
    if not v: return 'NULL'
    v = str(v)
    if v.startswith('1900-01-01') or v.startswith('0001-'): return 'NULL'
    return "'" + v[:23] + "'"

def qtime(v):
    if not v: return "'00:00:00'"
    return "'" + str(v)[:16] + "'"

def secs(v):
    h, m, s = str(v).split(':'); return int(h) * 3600 + int(m) * 60 + float(s)

def ticks(v): return int(round(secs(v) * 10_000_000))

# ---------------------------------------------------------------- code normalisation
RE_PAT = re.compile(r'^\s*RE\s*(\d+)\s*[/\-]\s*(\S+)\s*$', re.I)
def norm_code(code):
    """→ (کد جدید, ReturnProjectId)"""
    c = (code or '').strip()
    m = RE_PAT.match(c)
    if m: return f"RE{int(m.group(1))}-{m.group(2)}", int(m.group(1))
    return c, 0

# ---------------------------------------------------------------- users
legacy_users = collections.OrderedDict()
def touch_user(uid, role_hint):
    if uid is None or int(uid) <= 0: return
    uid = int(uid)
    legacy_users.setdefault(uid, set()).add(role_hint)

for r in E: touch_user(r['UserId'], 'ثبت‌کننده پروژه')
for r in R:
    touch_user(r['UserId'], 'ثبت‌کننده گزارش')
    touch_user(r['Operator'], 'اپراتور')
for r in AT: touch_user(r['UserId'], 'ثبت‌کننده پیوست')

# ---------------------------------------------------------------- karfarma
karfarma_ids = {r['KarfarmaId'] for r in K}
missing_emp = sorted({r['Employer'] for r in E if r['Employer'] is not None and r['Employer'] not in karfarma_ids})
extra_karfarma = []
for eid in missing_emp:
    name = (K1.get(eid) or {}).get('KarfarmaName') or f'کارفرمای قدیمی {eid}'
    extra_karfarma.append(dict(KarfarmaId=eid, KarfarmaName=name, Address=None, ModiramelPhone=None, Phone=None, Fax=None, Shomaresabt=None, _note=f'در Karfarma_Tbl نبود — از Karfarma_Tbl1 برداشته شد'))

# ---------------------------------------------------------------- projects
projects = []
for r in E:
    code, ret = norm_code(r['ProjectCode'])
    projects.append(dict(r, _code=code, _ret=ret))
by_code = collections.defaultdict(list)
for p in projects: by_code[p['_code']].append(p)

def pick_project(code, report_date):
    cands = by_code.get(code)
    if not cands: return None
    if len(cands) == 1: return cands[0]
    # کد تکراری: پروژه‌ای که تاریخ ورودش <= تاریخ گزارش و نزدیک‌ترین است؛ وگرنه جدیدترین
    def entry(p): return (p.get('DateVorod') or '0000')[:10]
    rd = (report_date or '9999')[:10]
    before = [p for p in cands if entry(p) <= rd]
    pool = before if before else cands
    return max(pool, key=lambda p: (entry(p), p['Id']))

ORPHAN_ID = 24999  # شناسهٔ پروژهٔ نگه‌دارندهٔ گزارش‌های بدون پروژه (بالاتر از بیشینهٔ Id قدیمی ۲۴۶۲۳)
orphans = collections.Counter()
reports = []
for r in R:
    code, _ = norm_code(r['IdList1'])
    p = pick_project(code, r['Date'])
    if p is None:
        orphans[code] += 1
        pid = ORPHAN_ID
    else:
        pid = p['Id']
    reports.append(dict(r, _code=code, _pid=pid))

# ---------------------------------------------------------------- write SQL
L = []
w = L.append
w("""/* =====================================================================================
   مهاجرت داده‌های سیستم قدیمی «aria» (ویندوز فرم — AriyaHamrahGhenerator) به InventoryDb
   تولیدشده توسط tools/build_import_sql.py — ویرایش دستی نکنید؛ اسکریپت پایتون را اجرا کنید.

   پیش‌نیاز: نسخهٔ جدید برنامه یک‌بار اجرا شده باشد (مایگریشن LegacyAriaImportPrep اعمال شده:
             ستون ReportWorks.OperatorId، KarshenasiAvalie(100)، TotalSpentTime bigint).
   اجرا:     در SSMS روی دیتابیس InventoryDb — کل اسکریپت در یک تراکنش است؛ در صورت خطا چیزی ذخیره نمی‌شود.
   شمارش:    KarFarma %d (+%d تکمیلی) | TypeFactor %d | ProjectEntryExit %d | ReportWork %d | کاربران قدیمی %d
   ===================================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
USE [InventoryDb];
GO
IF COL_LENGTH('dbo.ReportWorks', 'OperatorId') IS NULL
BEGIN
    RAISERROR(N'ستون ReportWorks.OperatorId وجود ندارد — ابتدا نسخهٔ جدید برنامه را اجرا کنید تا مایگریشن LegacyAriaImportPrep اعمال شود.', 16, 1);
    SET NOEXEC ON;
END
GO
IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = 1)
BEGIN
    RAISERROR(N'کاربر Id=1 (admin) وجود ندارد — پروژه‌های قدیمیِ بدون ثبت‌کننده به این کاربر وصل می‌شوند.', 16, 1);
    SET NOEXEC ON;
END
GO
IF EXISTS (SELECT 1 FROM dbo.ProjectEntryExits) OR EXISTS (SELECT 1 FROM dbo.ReportWorks)
BEGIN
    RAISERROR(N'جداول ProjectEntryExits/ReportWorks خالی نیستند — این اسکریپت برای دیتابیس خالیِ ماژول پروژه طراحی شده. ابتدا Clean-LegacyAria.sql را اجرا کنید.', 16, 1);
    SET NOEXEC ON;
END
GO
BEGIN TRANSACTION;
BEGIN TRY
""" % (len(K), len(extra_karfarma), len(TF), len(projects), len(reports), len(legacy_users)))

# ---- users
w("""
/* ---------- ۱) کاربران قدیمی (UserId / Operator) — با همان Id ---------- */
/* اگر کاربری با این Id از قبل هست دست نمی‌خوریم. در غیر این صورت کاربر غیرفعال «legacy_<id>» ساخته می‌شود
   (رمز نامعتبر → قابل ورود نیست). بعداً از صفحهٔ «کاربران» نام/نام‌خانوادگی را اصلاح و در صورت نیاز فعال کنید. */
SET IDENTITY_INSERT dbo.Users ON;
""")
for uid, roles in sorted(legacy_users.items()):
    roles_txt = '، '.join(sorted(roles))
    w(f"IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = {uid}) "
      f"INSERT INTO dbo.Users (Id, Username, PasswordHash, Role, FirstName, LastName, IsActive, CreatedAt) "
      f"VALUES ({uid}, N'legacy_{uid}', N'!legacy', N'Operator', N'کاربر قدیمی', N'{uid}', 0, SYSDATETIME()); -- {roles_txt}")
w("SET IDENTITY_INSERT dbo.Users OFF;\n")

# ---- typefactor
w("/* ---------- ۲) نوع فاکتور ---------- */\nSET IDENTITY_INSERT dbo.TypeFactors ON;")
for r in TF:
    w(f"IF NOT EXISTS (SELECT 1 FROM dbo.TypeFactors WHERE Id = {r['Id']}) "
      f"INSERT INTO dbo.TypeFactors (Id, Name, IsDelete, CreatedAt) VALUES ({r['Id']}, {qn(r['TypeFactorName'],150)}, 0, SYSDATETIME());")
w("SET IDENTITY_INSERT dbo.TypeFactors OFF;\n")

# ---- karfarma
w("/* ---------- ۳) کارفرما ---------- */\nSET IDENTITY_INSERT dbo.KarFarmas ON;")
def karfarma_row(r, note=''):
    kname = (r['KarfarmaName'] or '').strip() or ('کارفرما %d' % r['KarfarmaId'])
    return (f"IF NOT EXISTS (SELECT 1 FROM dbo.KarFarmas WHERE Id = {r['KarfarmaId']}) "
            f"INSERT INTO dbo.KarFarmas (Id, Name, Address, ModirAmelPhone, Telephone, Fax, ShomareSabt, IsDelete, CreatedAt) VALUES ("
            f"{r['KarfarmaId']}, {qn(kname,200)}, {qn(r['Address'],500)}, {qn(r['ModiramelPhone'],20)}, "
            f"{qn(r['Phone'],20)}, {qn(r['Fax'],20)}, {qn(r['Shomaresabt'],50)}, 0, SYSDATETIME());" + (f" -- {note}" if note else ''))
for r in sorted(K, key=lambda x: x['KarfarmaId']): w(karfarma_row(r))
for r in extra_karfarma: w(karfarma_row(r, r['_note']))
w("SET IDENTITY_INSERT dbo.KarFarmas OFF;\n")

# ---- projects
w("""/* ---------- ۴) پروژه‌ها (Entryandexitlist → ProjectEntryExits) ----------
   FlowStatus = 3 (نهایی) تا کارتابل‌ها خالی بمانند؛ UserId خالی → کاربر Id=1 (admin).
   TotalSpentTime بعداً از جمع گزارش‌ها محاسبه می‌شود. */
SET IDENTITY_INSERT dbo.ProjectEntryExits ON;""")
cols = ("Id, CodeProject, ReturnProjectId, SerialNumber, ProjectName, GhabzExit, FactorNumber, KarshenasiAvalie, ProjectReceiver, Description, "
        "KarFarmaId, FactorTypeId, UserId, ExitDate, EntryDate, FileDate, DeliveryDate, TemporaryExitDate, ProjectRegistrationDate, CustomerRequiredDate, "
        "IsFolder, IsDelete, FlowStatus, TotalSpentTime, CreatedAt")
def project_row(p):
    ft = p['FactorType']
    ft = None if not ft else int(ft)
    uid = int(p['UserId']) if p['UserId'] else 1
    created = p['DateSabtPoroje'] or p['DateVorod']
    vals = [
        str(p['Id']), qn(p['_code'], 60), str(p['_ret']),
        qn((p['SerialNumber'] or ''), 50), qn((p['NameProject'] or '').strip() or f"پروژه {p['_code']}", 250),
        qn(p['GhabzExit'], 50), qn(p['FactorNumber'], 50), qn(p['KarshenasiAvalie'], 100),
        qn(p['ReceiverProject'] or '', 200), qn(p['Description'], 1000),
        qi(p['Employer']), qi(ft), str(uid),
        qdt(p['DateKhoroj']), qdt(p['DateVorod']), qdt(p['FileDate']), qdt(p['DeliveryDate']), qdt(p['DateMovaqatExit']),
        qdt(p['DateSabtPoroje']), qdt(p['DateNiazMoshtari']),
        qb(p['IsPoshe']), '0', '3', '0', qdt(created) if created else 'SYSDATETIME()',
    ]
    return f"INSERT INTO dbo.ProjectEntryExits ({cols}) VALUES ({', '.join(vals)});"
for p in sorted(projects, key=lambda x: x['Id']): w(project_row(p))
if orphans:
    w(f"""-- پروژهٔ نگه‌دارنده برای گزارش‌هایی که کد پروژه‌شان در جدول قدیمی وجود نداشت ({sum(orphans.values())} گزارش: {', '.join(f'{c}×{n}' for c,n in orphans.items())})
INSERT INTO dbo.ProjectEntryExits ({cols}) VALUES ({ORPHAN_ID}, N'LEGACY-UNKNOWN', 0, N'', N'گزارش‌های قدیمی بدون پروژه (کد نامشخص)', NULL, NULL, NULL, N'', N'گزارش‌های کاری که در سیستم قدیمی به کد پروژه‌ای اشاره می‌کردند که وجود نداشت. کد اصلی هر گزارش در ستون CodeProject همان گزارش حفظ شده است.', {sorted(K, key=lambda x: x['KarfarmaId'])[0]['KarfarmaId']}, NULL, 1, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 0, 0, 3, 0, SYSDATETIME());""")
w("SET IDENTITY_INSERT dbo.ProjectEntryExits OFF;\n")

# ---- reports
w("""/* ---------- ۵) گزارش‌های کار (ReportWork → ReportWorks) ----------
   CodeProject = کد قدیمی (IdList1) نرمال‌شده | ProjectId از روی کد | OperatorId = ستون Operator قدیمی (۰ → NULL)
   SpentTime همان مقدار ذخیره‌شدهٔ قدیمی است (بازمحاسبه نمی‌شود). تاریخ ۱۹۰۰-۰۱-۰۱ (بدون تاریخ) → 0001-01-01 */
SET IDENTITY_INSERT dbo.ReportWorks ON;""")
rcols = "Id, CodeProject, ReportDate, UserId, OperatorId, WorkDescription, ProjectId, StartTime, EndTime, BreakfastTime, LunchTime, SpentTime, IsDelete, CreatedAt"
def report_row(r):
    op = int(r['Operator']) if r['Operator'] else None
    uid = int(r['UserId']) if r['UserId'] else 1
    d = r['Date'] or '1900-01-01'
    rd = "'0001-01-01'" if d.startswith('1900') else f"'{d}'"
    desc = (r['DescriptionWork'] or '').strip() or '—'
    vals = [str(r['Id']), qn(r['_code'], 60), rd, str(uid), qi(op), qn(desc, 1000), str(r['_pid']),
            qtime(r['DateStart']), qtime(r['DateEnd']), qtime(r['BreakFastTime']), qtime(r['LunchTime']), qtime(r['SpentTime']),
            '0', rd if not d.startswith('1900') else 'SYSDATETIME()']
    return f"INSERT INTO dbo.ReportWorks ({rcols}) VALUES ({', '.join(vals)});"
# batch: هر ۵۰۰ سطر یک PRINT برای پیگیری
for i, r in enumerate(sorted(reports, key=lambda x: x['Id'])):
    w(report_row(r))
w("SET IDENTITY_INSERT dbo.ReportWorks OFF;\n")

# ---- attachments (record only)
if AT:
    w("/* ---------- ۶) پیوست‌ها ---------- */")
    for a in AT:
        w(f"-- AttachAriya_Tbl Id={a['AttachIdAriya']} ProjectId={a['ProjectId']} File={a['PathDocument']} — فایل فیزیکی در دسترس نیست؛ طبق تصمیم کاربر منتقل نشد.")
    w("")

# ---- totals & reseed
w("""/* ---------- ۷) جمع ساعات هر پروژه (تیک) + بازنشانی شمارنده‌ها ---------- */
UPDATE p SET p.TotalSpentTime = ISNULL(s.T, 0)
FROM dbo.ProjectEntryExits p
LEFT JOIN (
    SELECT ProjectId, SUM(CAST(DATEDIFF(millisecond, CAST('00:00:00' AS time), SpentTime) AS bigint) * 10000) AS T
    FROM dbo.ReportWorks WHERE IsDelete = 0 GROUP BY ProjectId
) s ON s.ProjectId = p.Id;

DBCC CHECKIDENT ('dbo.KarFarmas', RESEED);
DBCC CHECKIDENT ('dbo.TypeFactors', RESEED);
DBCC CHECKIDENT ('dbo.ProjectEntryExits', RESEED);
DBCC CHECKIDENT ('dbo.ReportWorks', RESEED);
DBCC CHECKIDENT ('dbo.Users', RESEED);

/* ---------- ۸) گزارش نتیجه ---------- */
DECLARE @k int = (SELECT COUNT(*) FROM dbo.KarFarmas), @t int = (SELECT COUNT(*) FROM dbo.TypeFactors),
        @p int = (SELECT COUNT(*) FROM dbo.ProjectEntryExits), @r int = (SELECT COUNT(*) FROM dbo.ReportWorks),
        @o int = (SELECT COUNT(*) FROM dbo.ReportWorks WHERE OperatorId IS NOT NULL),
        @u int = (SELECT COUNT(*) FROM dbo.Users WHERE Username LIKE N'legacy[_]%');
PRINT N'کارفرما: ' + CAST(@k AS nvarchar) + N' | نوع فاکتور: ' + CAST(@t AS nvarchar) + N' | پروژه: ' + CAST(@p AS nvarchar)
    + N' | گزارش کار: ' + CAST(@r AS nvarchar) + N' (با اپراتور: ' + CAST(@o AS nvarchar) + N') | کاربران قدیمی ساخته‌شده: ' + CAST(@u AS nvarchar);
""")
w(f"""IF @p <> {len(projects) + (1 if orphans else 0)} OR @r <> {len(reports)}
BEGIN
    RAISERROR(N'تعداد رکوردهای درج‌شده با انتظار مطابقت ندارد — تراکنش برگردانده شد.', 16, 1);
END
COMMIT TRANSACTION;
PRINT N'مهاجرت با موفقیت انجام شد ✅';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    DECLARE @msg nvarchar(4000) = ERROR_MESSAGE(), @line int = ERROR_LINE();
    RAISERROR(N'خطا در خط %d: %s — هیچ تغییری ذخیره نشد.', 16, 1, @line, @msg);
END CATCH
GO
SET NOEXEC OFF;
GO
""")
open(OUT, 'w', encoding='utf-8-sig').write('\n'.join(L))

# ---- clean script
open(os.path.join(HERE, '..', 'Clean-LegacyAria.sql'), 'w', encoding='utf-8-sig').write("""/* پاک‌سازی کامل ماژول پروژه‌ها برای اجرای مجدد Import-LegacyAria.sql
   ⚠️ همهٔ پروژه‌ها، گزارش‌های کار، پیوست‌ها، کارفرماها و انواع فاکتور حذف می‌شوند. */
USE [InventoryDb];
GO
BEGIN TRANSACTION;
DELETE FROM dbo.ProjectAttaches;
DELETE FROM dbo.ReportWorks;
DELETE FROM dbo.ProjectEntryExits;
DELETE FROM dbo.KarFarmas;
DELETE FROM dbo.TypeFactors;
DELETE FROM dbo.Users WHERE Username LIKE N'legacy[_]%' AND IsActive = 0;
DBCC CHECKIDENT ('dbo.ProjectAttaches', RESEED, 0);
DBCC CHECKIDENT ('dbo.ReportWorks', RESEED, 0);
DBCC CHECKIDENT ('dbo.ProjectEntryExits', RESEED, 0);
DBCC CHECKIDENT ('dbo.KarFarmas', RESEED, 0);
DBCC CHECKIDENT ('dbo.TypeFactors', RESEED, 0);
COMMIT TRANSACTION;
PRINT N'ماژول پروژه‌ها پاک شد.';
GO
""")

# ---- summary
print(f"users: {len(legacy_users)} -> {sorted(legacy_users)}")
print(f"karfarma: {len(K)} + extra {[(r['KarfarmaId'], r['KarfarmaName']) for r in extra_karfarma]}")
print(f"projects: {len(projects)}  RE: {sum(1 for p in projects if p['_ret'])}")
dup = {c: len(v) for c, v in by_code.items() if len(v) > 1}
print(f"duplicate codes kept as-is: {dup}")
print(f"reports: {len(reports)}  orphans: {dict(orphans)}  with operator: {sum(1 for r in reports if r['Operator'])}")
amb = collections.Counter(r['_code'] for r in reports if len(by_code.get(r['_code'], [])) > 1)
print(f"reports on duplicate codes (resolved by date): {dict(amb)}")
print("written:", OUT)
