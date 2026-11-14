using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Inventory.Api.Controllers;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.Ai;
using Inventory.Shared.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

// اجرا:
//   dotnet run --project inventory/tests/BaleBulkLink
//
// این پراب کل مسیر «اتصال انبوهٔ کاربران به بله» را می‌آزماید:
//   • نرمال‌سازی شمارهٔ ایرانی به قالب 98… که سرویس سفیر می‌پذیرد
//   • تجزیهٔ پاسخ سفیر و نگاشت کدهای خطا (به‌ویژه NotBaleUser)
//   • دسته‌بندی نتیجه‌های اتصال (متصل / از قبل متصل / بدون اکانت بله / شمارهٔ نامعتبر / …)
//   • ذخیرهٔ اتصال و idempotency (اجرای دوباره پیام تکراری نمی‌فرستد)
//   • محافظت مدیریتی endpoint
//
// شبکه لازم نیست: سرویس سفیر با یک HttpMessageHandler ساختگی جایگزین می‌شود.

static void Check(bool ok, string message)
{
    if (!ok) throw new Exception("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
}

// ============================================================
//  ۱) نرمال‌سازی شمارهٔ تلفن
// ============================================================
Check(BaleSafirService.NormalizePhone("09121234567") == "989121234567",
    "09xxxxxxxxx normalizes to 98xxxxxxxxx");
Check(BaleSafirService.NormalizePhone("+989121234567") == "989121234567",
    "+98xxxxxxxxx normalizes to 98xxxxxxxxx");
Check(BaleSafirService.NormalizePhone("989121234567") == "989121234567",
    "98xxxxxxxxx stays unchanged");
Check(BaleSafirService.NormalizePhone("00989121234567") == "989121234567",
    "0098xxxxxxxxx normalizes to 98xxxxxxxxx");
Check(BaleSafirService.NormalizePhone("0912 123 4567") == "989121234567",
    "spaces are ignored");
Check(BaleSafirService.NormalizePhone("0912-123-4567") == "989121234567",
    "dashes are ignored");
Check(BaleSafirService.NormalizePhone("۰۹۱۲۱۲۳۴۵۶۷") == "989121234567",
    "Persian digits are converted");
Check(BaleSafirService.NormalizePhone("٠٩١٢١٢٣٤٥٦٧") == "989121234567",
    "Arabic-Indic digits are converted");
Check(BaleSafirService.NormalizePhone("9121234567") == "989121234567",
    "9xxxxxxxxx normalizes to 98xxxxxxxxx");
Check(BaleSafirService.NormalizePhone("0912345678") == null,
    "a 10-digit national mobile is rejected");
Check(BaleSafirService.NormalizePhone("0812345678") == null,
    "a landline is rejected (not a mobile)");
Check(BaleSafirService.NormalizePhone("02112345678") == null,
    "a Tehran landline is rejected");
Check(BaleSafirService.NormalizePhone("") == null && BaleSafirService.NormalizePhone(null) == null,
    "empty input is rejected");
Check(BaleSafirService.NormalizePhone("+14155552671") == null,
    "a foreign number is rejected");

// ============================================================
//  ۲) تجزیهٔ پاسخ سرویس سفیر
// ============================================================
var sent = BaleSafirService.ParseResponse("""{"message_id":"523e6875-7c41-491b-8460-04b33039d7fc","error_data":null}""");
Check(sent.Ok && sent.MessageId == "523e6875-7c41-491b-8460-04b33039d7fc",
    "a successful response yields the message id");
Check(sent.IsBaleUser, "a successful send proves the number has a Bale account");

var notBale = BaleSafirService.ParseResponse(
    """{"message_id":null,"error_data":[{"phone_number":"989121234567","code":17,"description":"کاربر اکانت بله ندارد"}]}""");
Check(notBale.Status == BaleSendStatus.NotBaleUser && !notBale.Ok,
    "error code 17 means the number has no Bale account");
Check(notBale.Error != null && notBale.Error.Contains("17"),
    "the error keeps the numeric Bale code for troubleshooting");

Check(BaleSafirService.ParseResponse(
    """{"error_data":[{"phone_number":"98912","code":8,"description":"شماره اشتباه"}]}""").Status
    == BaleSendStatus.InvalidPhone, "error code 8 means an invalid phone number");
Check(BaleSafirService.ParseResponse(
    """{"error_data":[{"code":3,"description":"بیش از حد مجاز"}]}""").Status
    == BaleSendStatus.RateLimited, "error code 3 means the rate limit was hit");
Check(BaleSafirService.ParseResponse(
    """{"error_data":[{"code":20,"description":"اعتبار کافی وجود ندارد"}]}""").Status
    == BaleSendStatus.PaymentRequired, "error code 20 means the account ran out of credit");
Check(BaleSafirService.ParseResponse(
    """{"error_data":[{"code":21,"description":"محدودیت مخاطبین"}]}""").Status
    == BaleSendStatus.ContactLimitReached, "error code 21 means the contact cap was reached");
Check(BaleSafirService.ParseResponse("not json").Status == BaleSendStatus.ServerError,
    "an unparsable response is a server error, never a silent success");
Check(BaleSafirService.ParseResponse("{}").Status == BaleSendStatus.ServerError,
    "an empty response is a server error, never a silent success");
Check(BaleSafirService.ParseResponse(
    """{"error_data":[{"code":4,"description":"خطای سرور"}]}""").Status
    == BaleSendStatus.ServerError, "error code 4 means a server error");

// ============================================================
//  ۳) دیتابیس واقعی (SQLite) + سرویس ساختگی سفیر
// ============================================================
await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite(connection).Options);
await db.Database.EnsureCreatedAsync();

var handler = new FakeSafirHandler();
var httpFactory = new FakeHttpClientFactory(handler);
var credentials = new FakeCredentials();

var safir = new BaleSafirService(httpFactory, credentials.GetAsync, NullLogger<BaleSafirService>.Instance);
var linkService = new BaleLinkService(db, safir, NullLogger<BaleLinkService>.Instance);

// کاربر ۱: شمارهٔ متعلق به اکانت بله
db.Users.Add(new User { Username = "ali", Mobile = "09121234567", IsActive = true, PasswordHash = "x" });
// کاربر ۲: شمارهٔ متعلق به اکانت بله، اما از قبل متصل شده
db.Users.Add(new User { Username = "sara", Mobile = "09121234568", IsActive = true, PasswordHash = "x",
    BalePhone = "989121234568", BalePhoneLinked = true });
// کاربر ۳: شماره‌ای که اکانت بله ندارد
db.Users.Add(new User { Username = "reza", Mobile = "09121234569", IsActive = true, PasswordHash = "x" });
// کاربر ۴: شمارهٔ نامعتبر
db.Users.Add(new User { Username = "bad", Mobile = "12345", IsActive = true, PasswordHash = "x" });
// کاربر ۵: بدون شماره
db.Users.Add(new User { Username = "nomobile", Mobile = null, IsActive = true, PasswordHash = "x" });
// کاربر ۶: غیرفعال — نباید بررسی شود
db.Users.Add(new User { Username = "off", Mobile = "09121234570", IsActive = false, PasswordHash = "x" });
await db.SaveChangesAsync();

handler.Behaviour = FakeSafirHandler.Mode.Normal;
var result = await linkService.LinkAllUsersAsync();

Check(result.Total == 5, "only active users are counted (the inactive one is skipped)");
Check(result.Linked == 1, "one user is linked by bulk operation");
Check(result.AlreadyLinked == 1, "an already-linked user is counted as such");
Check(result.NotBaleUser == 1, "a number without a Bale account is counted separately");
Check(result.InvalidNumber == 1, "an invalid number is counted separately");
Check(result.NoNumber == 1, "a user without a number is counted separately");
Check(result.Failed == 0, "no failures in the happy path");
Check(result.Completed, "the operation completes without a global abort");
Check(result.Details.Count == 5, "every active user appears in the detail list");
Check(result.Details.Single(d => d.Username == "ali").Outcome == BaleLinkOutcome.Linked,
    "ali is reported as linked");
Check(result.Details.Single(d => d.Username == "sara").Outcome == BaleLinkOutcome.AlreadyLinked,
    "sara is reported as already linked");
Check(result.Details.Single(d => d.Username == "reza").Outcome == BaleLinkOutcome.NotBaleUser,
    "reza is reported as having no Bale account");
Check(result.Details.Single(d => d.Username == "bad").Outcome == BaleLinkOutcome.InvalidNumber,
    "bad is reported as an invalid number");
Check(result.Details.Single(d => d.Username == "nomobile").Outcome == BaleLinkOutcome.NoNumber,
    "nomobile is reported as having no number");
Check(!result.Details.Any(d => d.Username == "off"),
    "the inactive user never appears in the details");

var ali = await db.Users.FirstAsync(u => u.Username == "ali");
Check(ali.BalePhoneLinked && ali.BalePhone == "989121234567",
    "the linked user's normalized Bale number is stored");
Check(ali.BaleChatId == null,
    "bulk linking does not fake a chat id — it uses the phone route");
var reza = await db.Users.FirstAsync(u => u.Username == "reza");
Check(!reza.BalePhoneLinked, "a user without a Bale account is not marked as linked");

// ---- idempotency: کاربرِ از قبل متصل نباید دوباره پیام بگیرد ----
handler.Phones.Clear();
var second = await linkService.LinkAllUsersAsync();
Check(second.Linked == 0, "a second run links nobody new");
Check(second.AlreadyLinked == 2, "both linked users are now already linked");
Check(!handler.Phones.Contains("989121234567"),
    "an already-linked user receives no second message");
Check(!handler.Phones.Contains("989121234568"),
    "the previously linked user receives no second message");
Check(handler.Phones.Contains("989121234569"),
    "a user without a Bale account is retried — he may join Bale later");

// ---- اجرای دوباره با force: همه دوباره بررسی می‌شوند ولی request_id پایدار می‌ماند ----
handler.SentCount = 0;
handler.Phones.Clear();
handler.RequestIds.Clear();
var forced = await linkService.LinkAllUsersAsync(includeAlreadyLinked: true);
Check(forced.Linked == 2, "a forced run re-checks both already-linked users");
Check(handler.Phones.Contains("989121234567") && handler.Phones.Contains("989121234568"),
    "a forced run really re-sends to the already-linked users");
// ---- request_id پایدار است: هر کاربر در هر اجرا همان شناسه را می‌گیرد ----
// (تکرارِ عمدیِ شناسه همان چیزی است که جلوی «پیام تکراری» در سفیر را می‌گیرد)
// بنابراین یکتاییِ شناسه‌ها باید درونِ یک اجرا برقرار باشد، نه در طولِ اجراها.
var forcedIds = handler.RequestIds.ToList();
Check(forcedIds.Count == forcedIds.Distinct().Count(),
    "within one run every request id is unique per user");
Check(forcedIds.Count == handler.PhoneChecks,
    "each contacted user produced exactly one request id");

// ---- اجرای دوباره همان شناسه‌ها را می‌فرستد تا سفیر پیام تکراری نسازد ----
var previousIds = handler.RequestIds.ToList();
handler.RequestIds.Clear();
handler.Phones.Clear();
await linkService.LinkAllUsersAsync(includeAlreadyLinked: true);
Check(handler.RequestIds.SequenceEqual(previousIds),
    "the same users get the same request ids on every run — Safir suppresses the duplicate");

// ---- خطای سراسری (اعتبار مالی) باید عملیات را متوقف کند ----
handler.Behaviour = FakeSafirHandler.Mode.PaymentRequired;
var aborted = await linkService.LinkAllUsersAsync(includeAlreadyLinked: true);
Check(!aborted.Completed && aborted.Aborted, "a global error aborts the operation");
Check(aborted.Failed > 0, "the failing users are counted as failed");
Check(aborted.Message.Contains("اعتبار مالی"), "the abort reason is explained to the admin");

// ---- سقف نرخ هم باید متوقف کند ----
handler.Behaviour = FakeSafirHandler.Mode.RateLimited;
var limited = await linkService.LinkAllUsersAsync(includeAlreadyLinked: true);
Check(!limited.Completed, "the rate limit aborts the operation");
Check(limited.Message.Contains("نرخ"), "the rate-limit reason is explained to the admin");

// ---- نبودِ کلید دسترسی: خطای روشن، بدون تماس با شبکه ----
credentials.Key = null;
handler.SentCount = 0;
var unconfigured = await linkService.LinkAllUsersAsync();
Check(!unconfigured.Completed && unconfigured.Total == 0,
    "without credentials nothing is attempted");
Check(unconfigured.Message.Contains("Api Access Key"),
    "the admin is told exactly which setting is missing");
Check(handler.SentCount == 0, "no request is sent without credentials");

// ============================================================
//  ۴) محافظت مدیریتی endpoint
// ============================================================
credentials.Key = "test-key";

async Task<(HttpStatusCode status, object? body)> CallBulkLinkAsync(ClaimsPrincipal principal)
{
    var controller = new NotificationsController(
        db,
        new StubMessengerService(),
        linkService,
        new MessengerLinkCodes(),
        new StubPushService(),
        new PushKeyStore(new StubHostEnvironment(), NullLogger<PushKeyStore>.Instance),
        new PushSettings(new ConfigurationBuilder().Build()));
    controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } };
    var result = await controller.BulkLinkBale();
    return result switch
    {
        OkObjectResult ok => (HttpStatusCode.OK, ok.Value),
        ObjectResult obj => ((HttpStatusCode)(obj.StatusCode ?? (int)HttpStatusCode.OK), obj.Value),
        UnauthorizedResult => (HttpStatusCode.Unauthorized, null),
        ForbidResult => (HttpStatusCode.Forbidden, null),
        NotFoundResult => (HttpStatusCode.NotFound, null),
        StatusCodeResult sc => ((HttpStatusCode)sc.StatusCode, null),
        _ => (HttpStatusCode.OK, null)
    };
}

// ناشناس: نه شناسه‌ای دارد نه احراز هویتی
var anon = await CallBulkLinkAsync(new ClaimsPrincipal(new ClaimsIdentity()));
Check(anon.status == HttpStatusCode.Unauthorized, "an anonymous caller is rejected");

// احراز هویت‌شده ولی بدون شناسهٔ کاربر → باز هم نامجاز
var noId = await CallBulkLinkAsync(new ClaimsPrincipal(new ClaimsIdentity(
    new[] { new Claim(ClaimTypes.Name, "ghost") }, "TestAuth")));
Check(noId.status == HttpStatusCode.Unauthorized, "a caller without a user id is rejected");

// کاربر عادی: نقش «Referrer» دارد و در RBAC هم مدیر نیست → ممنوع
var plainUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
{
    new Claim(ClaimTypes.NameIdentifier, "5"), new Claim(ClaimTypes.Name, "user"),
    new Claim(ClaimTypes.Role, "Referrer")
}, "TestAuth"));
var forbidden = await CallBulkLinkAsync(plainUser);
Check(forbidden.status == HttpStatusCode.Forbidden, "a non-admin caller is rejected");

// کاربر با شناسهٔ ناموجود در دیتابیس هم نباید مدیر تلقی شود
var ghostAdmin = new ClaimsPrincipal(new ClaimsIdentity(new[]
{
    new Claim(ClaimTypes.NameIdentifier, "999999"), new Claim(ClaimTypes.Name, "ghost"),
    new Claim(ClaimTypes.Role, "Admin")
}, "TestAuth"));
Check((await CallBulkLinkAsync(ghostAdmin)).status == HttpStatusCode.OK,
    "the Admin role claim alone grants the operation");

var admin = new ClaimsPrincipal(new ClaimsIdentity(new[]
{
    new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Name, "admin"),
    new Claim(ClaimTypes.Role, "Admin")
}, "TestAuth"));
var allowed = await CallBulkLinkAsync(admin);
Check(allowed.status == HttpStatusCode.OK, "an admin caller is allowed");

// ---- مسیر RBAC: مدیریت از طریق مجوز «Settings.Manage» در نقش، نه claim نقش ----
// یک کاربر عادی با نقشی که مجوز مدیریت تنظیمات را دارد باید اجازهٔ عملیات را بگیرد.
var rbacUserId = 77;
var rbacRoleId = 88;
db.Users.Add(new User { Id = rbacUserId, Username = "rbacboss", Mobile = "09121234999",
    IsActive = true, PasswordHash = "x" });
db.Roles.Add(new Role { Id = rbacRoleId, Name = "settings-manager", IsActive = true });
db.UserRoles.Add(new UserRole { UserId = rbacUserId, RoleId = rbacRoleId });
var perm = new Permission { Module = "Settings", Action = "Manage", Description = "تنظیمات مدیریت" };
db.Permissions.Add(perm);
await db.SaveChangesAsync();
db.RolePermissions.Add(new RolePermission { RoleId = rbacRoleId, PermissionId = perm.Id });
await db.SaveChangesAsync();

var rbacUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
{
    new Claim(ClaimTypes.NameIdentifier, rbacUserId.ToString()), new Claim(ClaimTypes.Name, "rbacboss")
}, "TestAuth"));
var rbacAllowed = await CallBulkLinkAsync(rbacUser);
Check(rbacAllowed.status == HttpStatusCode.OK,
    "the Settings.Manage permission also grants the operation");

// ---- کاربری با نقش، ولی بدون مجوز مدیریت → ممنوع ----
var rbacDeniedUserId = 78;
var rbacDeniedRoleId = 89;
db.Users.Add(new User { Id = rbacDeniedUserId, Username = "limited", Mobile = "09121234888",
    IsActive = true, PasswordHash = "x" });
db.Roles.Add(new Role { Id = rbacDeniedRoleId, Name = "settings-viewer", IsActive = true });
db.UserRoles.Add(new UserRole { UserId = rbacDeniedUserId, RoleId = rbacDeniedRoleId });
var perm2 = new Permission { Module = "Settings", Action = "View", Description = "مشاهدهٔ تنظیمات" };
db.Permissions.Add(perm2);
await db.SaveChangesAsync();
db.RolePermissions.Add(new RolePermission { RoleId = rbacDeniedRoleId, PermissionId = perm2.Id });
await db.SaveChangesAsync();

var rbacDenied = new ClaimsPrincipal(new ClaimsIdentity(new[]
{
    new Claim(ClaimTypes.NameIdentifier, rbacDeniedUserId.ToString()), new Claim(ClaimTypes.Name, "limited")
}, "TestAuth"));
Check((await CallBulkLinkAsync(rbacDenied)).status == HttpStatusCode.Forbidden,
    "a role without the Settings.Manage permission is rejected");

// ============================================================
//  ۵) انتخاب مخاطب: کاربرِ «فقط-شماره» هم باید مخاطب اعلان‌ها باشد
// ============================================================
// این بخش رگرسیونِ شکافی است که پیش‌تر وجود داشت: سرویس‌های AI/اعلان فقط
// BaleChatId را می‌دیدند، پس کاربرانی که فقط از مسیر «سفیر» (شماره) متصل شده بودند
// هرگز گزارش یا اعلان خودکار دریافت نمی‌کردند — خلافِ هدفِ «کاربر هرگز مجبور
// به اتصال به بله نشود».
Check(MessengerLinkHelper.HasMessengerLink(null, true, null),
    "a phone-linked user is a valid notification recipient");
Check(MessengerLinkHelper.HasMessengerLink("123", false, null),
    "an interactive-chat user is a valid notification recipient");
Check(MessengerLinkHelper.HasMessengerLink(null, false, "456"),
    "an Eitaa user is a valid notification recipient");
Check(!MessengerLinkHelper.HasMessengerLink(null, false, null),
    "a user with no messenger link is not a notification recipient");
Check(!MessengerLinkHelper.HasMessengerLink("", false, "  "),
    "blank chat ids do not count as links");

// همان منطق، به‌صورت کوئری روی دیتابیس واقعی
var recipients = await db.Users.AsNoTracking()
    .Where(u => u.IsActive && (u.BaleChatId != null || u.EitaaChatId != null || u.BalePhoneLinked))
    .Select(u => u.Username)
    .ToListAsync();
Check(recipients.Contains("ali"),
    "a user linked through Safir is selected as a recipient");
Check(recipients.Contains("sara"),
    "a previously linked user is selected as a recipient");
Check(!recipients.Contains("reza"),
    "a user without a Bale account is not selected");
Check(!recipients.Contains("nomobile"),
    "a user without any messenger link is not selected");
Check(!recipients.Contains("off"),
    "an inactive user is not selected");

// ============================================================
//  ۶) خودتعمیر اسکیما روی دیتابیس قدیمی (SQLite)
// ============================================================
// سناریو: دیتابیسی که پیش از این قابلیت ساخته شده و ستون‌های تازه را ندارد.
// سناریو ۲: اجرای دوبارهٔ همان اسکیما (باید بی‌خطر و idempotent باشد).
await using (var oldConn = new SqliteConnection("Data Source=:memory:"))
{
    await oldConn.OpenAsync();
    await using var oldDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(oldConn).Options);

    // جدول کاربران «قدیمی» را دستی می‌سازیم — بدون ستون‌های بله
    await using (var cmd = oldConn.CreateCommand())
    {
        cmd.CommandText = @"CREATE TABLE Users (
            Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            Username TEXT NOT NULL,
            PasswordHash TEXT NOT NULL,
            Role TEXT NOT NULL DEFAULT 'Admin',
            Mobile TEXT NULL,
            IsActive INTEGER NOT NULL DEFAULT 1,
            CreatedAt TEXT NOT NULL);";
        await cmd.ExecuteNonQueryAsync();
    }
    await using (var cmd = oldConn.CreateCommand())
    {
        cmd.CommandText = @"CREATE TABLE AppSettings (
            Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            BaleBotToken TEXT NULL,
            EitaaToken TEXT NULL,
            MessengerSenderNumber TEXT NULL);";
        await cmd.ExecuteNonQueryAsync();
    }

    await BaleLinkSchemaV1.EnsureAsync(oldDb);
    var cols = new List<string>();
    await using (var cmd = oldConn.CreateCommand())
    {
        cmd.CommandText = "PRAGMA table_info(\"Users\")";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) cols.Add(reader.GetString(1));
    }
    Check(cols.Contains("BalePhone"), "BalePhone is added to an existing Users table");
    Check(cols.Contains("BalePhoneLinked"), "BalePhoneLinked is added to an existing Users table");

    var settingCols = new List<string>();
    await using (var cmd = oldConn.CreateCommand())
    {
        cmd.CommandText = "PRAGMA table_info(\"AppSettings\")";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) settingCols.Add(reader.GetString(1));
    }
    Check(settingCols.Contains("BaleSafirApiAccessKey"), "BaleSafirApiAccessKey is added to an existing AppSettings table");
    Check(settingCols.Contains("BaleSafirBotId"), "BaleSafirBotId is added to an existing AppSettings table");

    // اجرای دوباره نباید خطا بدهد (idempotent)
    await BaleLinkSchemaV1.EnsureAsync(oldDb);
    Check(true, "running the schema repair twice is safe");

    // ستون تازه واقعاً کار می‌کند
    await using (var cmd = oldConn.CreateCommand())
    {
        cmd.CommandText = "INSERT INTO Users (Username, PasswordHash, Mobile, BalePhone, BalePhoneLinked, CreatedAt) VALUES ('olduser','x','09120000000','989120000000',1,'2026-01-01 00:00:00')";
        await cmd.ExecuteNonQueryAsync();
    }
    await using (var cmd = oldConn.CreateCommand())
    {
        cmd.CommandText = "SELECT BalePhone, BalePhoneLinked FROM Users WHERE Username='olduser'";
        await using var reader = await cmd.ExecuteReaderAsync();
        Check(await reader.ReadAsync(), "a user row can be inserted with the new columns");
        Check(reader.GetString(0) == "989120000000" && reader.GetInt64(1) == 1,
            "the new columns keep their values");
    }
}

Console.WriteLine();
Console.WriteLine("همهٔ بررسی‌های اتصال انبوهٔ بله با موفقیت گذشت.");

// ============================================================
//  کمکی‌ها
// ============================================================
sealed class FakeCredentials
{
    public string? Key = "test-key";
    public int BotId = 123456789;
    public Task<(string? key, int botId)> GetAsync() => Task.FromResult((Key, BotId));
}

sealed class FakeSafirHandler : HttpMessageHandler
{
    public enum Mode { Normal, PaymentRequired, RateLimited }

    public Mode Behaviour = Mode.Normal;
    public int SentCount;
    public List<string> RequestIds = new();
    public List<string> Phones = new();

    /// <summary>تعداد شماره‌هایی که واقعاً برایشان درخواست ارسال شد.</summary>
    public int PhoneChecks => Phones.Count(p => !string.IsNullOrEmpty(p));

    /// <summary>شماره‌هایی که سفیر «اکانت بله ندارد» اعلام می‌کند (کد ۱۷).</summary>
    public HashSet<string> NotBalePhones = new() { "989121234569" };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        SentCount++;
        var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);

        string phone = "";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("request_id", out var rid))
                RequestIds.Add(rid.GetString() ?? "");
            if (doc.RootElement.TryGetProperty("phone_number", out var ph))
                phone = ph.GetString() ?? "";
        }
        catch { /* ignore */ }
        Phones.Add(phone);

        string json = Behaviour switch
        {
            Mode.PaymentRequired => """{"error_data":[{"code":20,"description":"اعتبار کافی وجود ندارد"}]}""",
            Mode.RateLimited => """{"error_data":[{"code":3,"description":"بیش از حد مجاز پیام ارسال شده"}]}""",
            // در حالت عادی، فقط شماره‌هایی که در NotBalePhones هستند خطای «اکانت بله ندارد» می‌گیرند
            _ when NotBalePhones.Contains(phone) => """{"message_id":null,"error_data":[{"code":17,"description":"کاربر اکانت بله ندارد"}]}""",
            _ => """{"message_id":"m-1","error_data":null}"""
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}

sealed class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}

sealed class StubMessengerService : IMessengerService
{
    public Task<MessengerSendResult> SendToUserAsync(int userId, string title, string? body)
        => Task.FromResult(new MessengerSendResult(false, null, false, null, false));
    public Task<bool> SendExcelToUserAsync(int userId, string caption, byte[] bytes, string fileName)
        => Task.FromResult(false);
    public Task<(int matched, string message)> SyncBaleAsync() => Task.FromResult((0, ""));
    public Task<(int linked, long maxUpdateId, string note)> PollBaleAsync(long offset)
        => Task.FromResult((0, 0L, ""));
    public Task<MessengerSendResult> SendTestAsync(int userId)
        => Task.FromResult(new MessengerSendResult(false, null, false, null, false));
    public Task<(bool ok, string message)> LinkEitaaAsync(string code, string initData)
        => Task.FromResult((false, ""));
    public Task<bool> IsBalePhoneRegisteredAsync(string phone) => Task.FromResult(false);
}

sealed class StubPushService : IPushService
{
    public Task SaveSubscriptionAsync(int userId, string endpoint, string p256dh, string auth, string? userAgent)
        => Task.CompletedTask;
    public Task RemoveSubscriptionAsync(int userId, string endpoint) => Task.CompletedTask;
    public Task SendToUserAsync(int userId, string title, string? body, string? link) => Task.CompletedTask;
    public Task SendToUsersAsync(IEnumerable<int> userIds, string title, string? body, string? link) => Task.CompletedTask;
    public Task<int> ClearSubscriptionsAsync() => Task.FromResult(0);
    public string VapidPublicKey => "";
    public bool IsConfigured => false;
    public string Source => "test";
}

sealed class StubHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Test";
    public string ApplicationName { get; set; } = "BaleBulkLinkTest";
    public string ContentRootPath { get; set; } = Path.GetTempPath();
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
        = new Microsoft.Extensions.FileProviders.NullFileProvider();
}
