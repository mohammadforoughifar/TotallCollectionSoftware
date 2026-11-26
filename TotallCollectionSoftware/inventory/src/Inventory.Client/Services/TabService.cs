using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Inventory.Client.Services;

// ============================================================
//  سرویسِ تب‌های کاری
//  • هر بخش/صفحه‌ای که کاربر باز می‌کند یک تب می‌گیرد (به‌جز پیام‌رسان و صفحات عمومی)
//  • صفحاتِ تب‌های غیرفعال در DOM می‌مانند و فقط مخفی می‌شوند،
//    بنابراین فیلترها، شماره صفحه، اسکرول و فرم‌های نیمه‌پر هنگام جابه‌جایی حفظ می‌شوند
//  • فهرست تب‌ها در localStorage ذخیره می‌شود تا بعد از رفرش مرورگر هم بماند
//    (محتوای هر تب در اولین نمایش دوباره ساخته می‌شود)
// ============================================================

public sealed class TabItem
{
    /// <summary>کلید یکتا — مسیر نسبیِ نرمال‌شده (شامل پارامترهای query)</summary>
    public string Key { get; set; } = "";

    /// <summary>مسیر کامل برای پیمایش</summary>
    public string Url { get; set; } = "";

    public string Title { get; set; } = "";
    public string Icon { get; set; } = "bi-circle";

    /// <summary>سنجاق‌شده — قابل بستن نیست (مثل تبِ داشبورد)</summary>
    public bool Pinned { get; set; }

    public bool Closable => !Pinned;

    public bool IsActive { get; set; }

    /// <summary>با افزایش این شماره، صفحه‌ی تب از نو ساخته می‌شود (دکمهٔ بازسازی)</summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// داده‌های مسیرِ این تب. فقط برای تب‌هایی که در همین نشست باز شده‌اند مقدار دارد؛
    /// تب‌های بازیابی‌شده از localStorage تا زمانی که کاربر رویشان کلیک نکند خالی‌اند.
    /// </summary>
    [JsonIgnore]
    public RouteData? RouteData { get; set; }
}

public sealed class TabService
{
    private readonly IJSRuntime _js;
    private readonly NavigationManager _nav;
    private readonly LayoutState _layout;

    private const string StoreKey = "app.openTabs.v1";
    private const int MaxTabs = 25;

    private readonly List<TabItem> _tabs = new();
    private bool _loaded;
    private TabItem? _active;

    /// <summary>تاریخچهٔ بازدیدِ صفحه‌ها (کلید تب) برای دکمه‌های «صفحهٔ قبلی/بعدی».</summary>
    private readonly List<string> _history = new();
    private int _historyIndex = -1;

    public IReadOnlyList<TabItem> Tabs => _tabs;
    public TabItem? Active => _active;

    /// <summary>تبِ صفحهٔ قبلی در تاریخچهٔ بازدید؛ null یعنی دکمه باید غیرفعال باشد.</summary>
    public TabItem? Previous => HistoryAt(_historyIndex - 1);

    /// <summary>تبِ صفحهٔ بعدی در تاریخچهٔ بازدید؛ null یعنی دکمه باید غیرفعال باشد.</summary>
    public TabItem? Next => HistoryAt(_historyIndex + 1);

    private TabItem? HistoryAt(int index)
    {
        if (index < 0 || index >= _history.Count) return null;
        var key = _history[index];
        var tab = _tabs.FirstOrDefault(x => x.Key == key);
        return tab is null || ReferenceEquals(tab, _active) ? null : tab;
    }

    /// <summary>
    /// رفتن به صفحهٔ قبلی/بعدی. مثل مرورگر: اگر تاریخچه عقب رفته باشیم، «بعدی»
    /// دوباره جلو می‌رود؛ بازدید تازه شاخهٔ بعدی را پاک می‌کند.
    /// </summary>
    public void GoBack()
    {
        if (Previous is { } tab) MoveHistoryTo(_historyIndex - 1, tab);
    }

    public void GoForward()
    {
        if (Next is { } tab) MoveHistoryTo(_historyIndex + 1, tab);
    }

    private void MoveHistoryTo(int index, TabItem tab)
    {
        _historyIndex = index;
        _nav.NavigateTo(tab.Url);
    }

    /// <summary>ثبت بازدید در تاریخچه؛ تب‌های تکراری پشت‌سرهم جمع می‌شوند.</summary>
    private void PushHistory(string key)
    {
        if (_historyIndex >= 0 && _historyIndex < _history.Count && _history[_historyIndex] == key) return;
        if (_historyIndex + 1 < _history.Count)
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        _history.Add(key);
        _historyIndex = _history.Count - 1;
        // تاریخچه بی‌نهایت لازم نیست؛ ۵۰ بازدید آخر کافی است.
        if (_history.Count > 50)
        {
            _history.RemoveRange(0, _history.Count - 50);
            _historyIndex = _history.Count - 1;
        }
    }

    /// <summary>
    /// تب از فهرست حذف شد — کلیدش نباید در تاریخچه بماند. چون ورودی‌ها ممکن است
    /// جلوتر یا عقب‌تر از نقطهٔ جاری حذف شوند، جای فعلی از روی «کلیدِ صفحهٔ جاری»
    /// دوباره پیدا می‌شود تا دکمه‌های قبلی/بعدی به صفحهٔ اشتباه نروند.
    /// </summary>
    private void ForgetHistory(string key)
    {
        if (!_history.Contains(key)) return;
        var current = _historyIndex >= 0 && _historyIndex < _history.Count ? _history[_historyIndex] : null;
        _history.RemoveAll(x => x == key);
        if (_history.Count == 0) { _historyIndex = -1; return; }
        if (current is not null && current != key)
        {
            var at = _history.IndexOf(current);
            // همان صفحه هنوز در تاریخچه هست یا خودش صفحهٔ حذف‌شده بود.
            _historyIndex = at >= 0 ? at : Math.Min(_historyIndex, _history.Count - 1);
            return;
        }
        _historyIndex = Math.Min(_historyIndex, _history.Count - 1);
    }

    /// <summary>هر تغییری در فهرست تب‌ها — نوار تب و نگه‌دارنده‌ی صفحات مشترک‌اند</summary>
    public event Action? Changed;

    /// <summary>
    /// تب تازه فعال شد (کلیک روی تب یا پیمایش به آن). چون صفحه‌های تب‌ها در DOM
    /// می‌مانند و از نو ساخته نمی‌شوند، صفحه‌ای که فقط در OnInitializedAsync داده
    /// می‌خواند کهنه می‌ماند؛ با این رویداد خودش را تازه می‌کند (بدون رفرش دستی).
    /// </summary>
    public event Func<TabItem, Task>? ActivatedAsync;

    public TabService(IJSRuntime js, NavigationManager nav, LayoutState layout)
    {
        _js = js;
        _nav = nav;
        _layout = layout;
        _layout.Changed += OnLayoutTitleChanged;
    }

    // ==================== عنوان و آیکون ====================

    /// <summary>نگاشتِ مسیر به (عنوان، آیکون) — برگرفته از آیتم‌های منوی برنامه</summary>
    private static readonly (string Prefix, string Title, string Icon)[] Known =
    {
        // مودیان تیسا — ترتیب مهم است: خاص قبل از عمومی
        ("moadian/basic-info",          "مودیان تیسا — اطلاعات پایه", "bi-archive"),
        ("moadian/invoices/new",        "مودیان تیسا — صورتحساب جدید", "bi-journal-plus"),
        ("moadian/invoices",            "مودیان تیسا — صورتحساب‌ها",   "bi-journal-text"),
        ("moadian/guide",               "مودیان تیسا — راهنما",        "bi-journal-check"),
        ("moadian",                     "مودیان تیسا — نمای کلی",     "bi-speedometer2"),
        ("reports/kardex",              "کاردکس کالا",          "bi-bar-chart-line"),
        ("reports/reorder",             "نقطه سفارش",           "bi-exclamation-triangle"),
        ("outgoing-letters/dabirkhane", "دبیرخانه نامه صادره",  "bi-mailbox"),
        ("outgoing-letters",            "نامه صادره",           "bi-send"),
        ("incoming-letters",            "نامه وارده",           "bi-inbox-fill"),
        ("office/personal-email",       "ایمیل شخصی",           "bi-person-badge"),
        ("office/email",                "ایمیل سازمانی",        "bi-envelope-at"),
        ("doc-archive/cartable",        "کارتابل آرشیو",        "bi-inbox"),
        ("export-center",               "مرکز خروجی",           "bi-file-earmark-arrow-down"),
        ("doc-archive",                 "پوشه‌ها و مدارک",      "bi-folder2-open"),
        ("settings/letter-structure",   "ساختار شماره نامه",    "bi-sort-numeric-down"),
        ("settings/audit-log",          "لاگ حسابرسی",          "bi-clock-history"),
        ("settings",                    "تنظیمات",              "bi-gear-fill"),
        ("work-orders",                 "دستورکار رایکا",            "bi-card-checklist"),
        ("dashboard-cctv",              "داشبورد مدیریت",       "bi-graph-up-arrow"),
        ("dashboard-hardware",          "داشبورد سخت‌افزار",    "bi-motherboard"),
        ("my-dashboards",               "داشبورد من",           "bi-grid-1x2-fill"),
        ("report-studio",               "گزارش‌ساز",             "bi-clipboard-data-fill"),
        ("my-archive",                  "بایگانی شخصی",         "bi-archive-fill"),
        ("cartable",                    "کارتابل من",           "bi-inbox-fill"),
        ("dashboard",                   "داشبورد",              "bi-speedometer2"),
        ("project-cartable",            "کارتابل پروژه",        "bi-inboxes"),
        ("report-works",                "گزارش‌های کار",        "bi-clock-history"),
        ("type-factors",                "انواع فاکتور",         "bi-card-list"),
        ("shop-guide",                  "راهنمای سامانه",        "bi-journal-check"),
        ("orders/purchase",             "خرید",                 "bi-bag-plus"),
        ("orders/sale",                 "فروش",                 "bi-cart3"),
        ("referrer-wallets",            "کیف پول معرف‌ها",      "bi-wallet2"),
        ("system-department",           "واحدها",               "bi-diagram-3"),
        ("system-company",              "کمپانی‌ها",            "bi-building"),
        ("system-users",                "کاربران سیستم",        "bi-people-fill"),
        ("system-info",                 "سیستم‌های ثبت‌شده",    "bi-list-check"),
        ("system-id/manual",            "ثبت دستی سیستم",       "bi-pc-display-horizontal"),
        ("system-id",                   "شناسنامه سیستم",       "bi-cpu-fill"),
        ("network-scan",                "اسکن شبکه",            "bi-broadcast-pin"),
        ("office-machines",             "ماشین‌های اداری",      "bi-printer-fill"),
        ("cctv-cameras",                "دوربین‌ها",            "bi-camera-video-fill"),
        ("cctv-nvrs",                   "دستگاه‌های NVR",       "bi-hdd-stack-fill"),
        ("it-vault",                    "گاوصندوق رمزها",       "bi-shield-lock-fill"),
        ("ip-management",               "مدیریت آی‌پی",         "bi-diagram-2-fill"),
        ("it-requests",                 "درخواست خدمت IT",      "bi-headset"),
        ("warehouses",                  "انبارها",              "bi-buildings"),
        ("categories",                  "گروه‌های کالا",        "bi-tags"),
        ("products",                    "کالاها",               "bi-box-seam"),
        ("parties",                     "طرف حساب‌ها",          "bi-people"),
        ("referrers",                   "معرف‌ها",              "bi-person-badge"),
        // پروژه‌های کاری و نگار ذهنی — ترتیب مهم است: خاص قبل از عمومی
        ("work-projects/mindflow",      "نگار ذهنی پروژه",      "bi-diagram-3-fill"),
        ("work-projects",               "پروژه‌های کاری",       "bi-kanban-fill"),
        ("mindflows",                   "نگار ذهنی",            "bi-diagram-3-fill"),
        ("projects",                    "پروژه‌ها",             "bi-arrow-left-right"),
        ("karfarmas",                   "کارفرماها",            "bi-briefcase"),
        ("letters",                     "نامه داخلی",           "bi-envelope-paper"),
        ("misc/minutes",                "صورتجلسه",             "bi-journal-text"),
        // گزارش‌های عملیاتی سامانه فروشگاهی — بدون این نگاشت، تبِ صفحه عنوانِ عمومی
        // «صفحه» می‌گرفت (مثلاً گزارش گارانتی که عنوان درستش «گزارش گارانتی» است)
        ("operations/reports/warranty",          "گزارش گارانتی",            "bi-shield-check"),
        ("operations/reports/dashboard",         "داشبورد عملیاتی",          "bi-bar-chart-line"),
        ("operations/reports/expenses",          "گزارش هزینه‌ها",           "bi-cash-coin"),
        ("operations/reports/party-statements",  "گزارش طلب و بدهی اشخاص",   "bi-journal-check"),
        ("operations/reports/cheque-income",     "گزارش چک‌های دریافتی",     "bi-journal-check"),
        ("operations/reports/orders",            "گزارش خرید و فروش",        "bi-graph-up-arrow"),
        ("expenses",                    "هزینه‌ها",             "bi-cash-coin"),
        ("repairs",                     "تعمیرات",              "bi-tools"),
        ("stock",                       "موجودی انبار",         "bi-boxes"),
        ("units",                       "واحدهای شمارش",        "bi-rulers"),
        ("users",                       "کاربران",              "bi-person-gear"),
        ("my-wallet",                   "کیف پول من",           "bi-wallet2"),
        ("my-products",                 "کالاهای موجود",        "bi-box-seam"),
        ("my-card",                     "کارت ویزیت من",        "bi-person-vcard"),
    };

    /// <summary>مسیرهایی که تب نمی‌گیرند — صفحات تمام‌صفحه یا عمومی</summary>
    private static readonly string[] Excluded =
    {
        "chat",             // پیام‌رسان تمام‌صفحه
        "public-request",   // فرم عمومی ثبت درخواست (بدون ورود)
    };

    public static bool IsExcluded(string relativeUrl)
    {
        var p = (relativeUrl ?? "").Trim('/').ToLowerInvariant();
        if (p.Length == 0) return false;
        return Excluded.Any(x => p == x || p.StartsWith(x + "/", StringComparison.Ordinal)
                                       || p.StartsWith(x + "?", StringComparison.Ordinal));
    }

    /// <summary>کلید یکتای تب برای یک مسیر نسبی</summary>
    public static string KeyOf(string relativeUrl)
        => (relativeUrl ?? "").Trim().TrimStart('/').TrimEnd('/').ToLowerInvariant();

    private static (string Title, string Icon) Resolve(string key)
    {
        // ریشهٔ سایت = «کارتابل من» (داشبورد به /dashboard منتقل شده است)
        if (key.Length == 0) return ("کارتابل من", "bi-inbox-fill");

        foreach (var k in Known)
            if (key == k.Prefix
                || key.StartsWith(k.Prefix + "/", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith(k.Prefix + "?", StringComparison.OrdinalIgnoreCase))
                return (k.Title, k.Icon);

        // مسیرهای «منابع انسانی فروغ آریا» (برنامهٔ پیشین) — پیش از بررسی منوی اصلی،
        // چون دو نمای پیشین (فهرست پرسنل / چارت سازمانی) درون hr-core جا دارند.
        if (ForoughAryaHrNavigation.IsWorkspace(key))
        {
            var fa = ForoughAryaHrNavigation.Title(key);
            if (!string.IsNullOrWhiteSpace(fa)) return (fa, "bi-person-workspace");
        }

        // فقط مسیرهای خودِ منابع انسانی — وگرنه هر صفحهٔ ناشناخته «منابع انسانی» می‌شد
        if (HrNavigation.IsWorkspace(key))
        {
            var hr = HrNavigation.Title(key);
            if (!string.IsNullOrWhiteSpace(hr)) return (hr, "bi-people");
        }

        // آخرین چاره: مسیرِ خام لاتین نباید در تب مرورگر دیده شود.
        // آخرین بخش مسیر را به یک عنوانِ خوانا تبدیل می‌کنیم و اگر ناشناخته بود، «صفحه» می‌گذاریم.
        return (Humanize(key), "bi-circle");
    }

    /// <summary>
    /// آیا این عنوان شبیه «اسلاگِ مسیر» است (لاتین/خط‌تیره/بدون حرف فارسی)؟
    /// چنین عنوانی از نسخه‌های قدیمی ذخیره شده و باید با عنوانِ فارسی جایگزین شود.
    /// </summary>
    private static bool IsSlugLike(string? title)
    {
        var t = (title ?? "").Trim();
        if (t.Length == 0) return false;
        // هر حرف فارسی/عربی ⇒ عنوان واقعیِ فارسی است، دست نزن
        if (t.Any(ch => ch >= '\u0600' && ch <= '\u06FF')) return false;
        // لاتین و بدون فاصله/با خط‌تیره ⇒ اسلاگ مسیر
        return t.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '/' or '.');
    }

    /// <summary>
    /// تبدیلِ مسیرِ ناشناخته به عنوانِ فارسیِ خوانا.
    /// چون عنوانِ واقعیِ صفحه با PageTitle بعد از رندر مشخص می‌شود و TabService آن را نمی‌بیند،
    /// اینجا فقط از دیده‌شدنِ اسلاگ/خط‌تیره در تب جلوگیری می‌کنیم.
    /// </summary>
    private static string Humanize(string key)
    {
        var slug = key.Split('?')[0].Trim('/').Split('/').LastOrDefault() ?? "";
        if (slug.Length == 0) return "صفحه";
        // اگر مسیر کاملاً عددی یا GUID بود، شماره‌اش را نگه دار: «صفحه ۱۲»
        if (slug.All(char.IsDigit)) return $"صفحه {slug}";
        if (Guid.TryParse(slug, out _)) return "صفحه";
        // عنوان فارسیِ ذخیره‌شده در Layout (SetTitle) بعداً روی این مقدار بازنویسی می‌شود؛
        // این فقط یک حداقلِ قابل‌قبول است.
        return "صفحه";
    }

    /// <summary>
    /// عنوانِ جایگزینِ عمومیِ تب («صفحه») عنوانِ واقعی نیست و باید از نگاشت مسیر
    /// دوباره ساخته شود؛ پیش‌تر این تازه‌سازی فقط برای «shop-guide» انجام می‌شد و
    /// تبِ گزارش گارانتی عنوانِ «صفحه» را برای همیشه نگه می‌داشت.
    /// </summary>
    private static bool IsPlaceholderTitle(string? title)
    {
        var t = (title ?? "").Trim();
        return t is "صفحه" or "(صفحه)" || t.StartsWith("صفحه ", StringComparison.Ordinal);
    }

    // ==================== بارگیری / ذخیره ====================

    private sealed class Snapshot
    {
        public string Url { get; set; } = "";
        public string Title { get; set; } = "";
        public string Icon { get; set; } = "";
        public bool Pinned { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>بازیابیِ فهرست تب‌ها از localStorage — یک‌بار در آغاز برنامه</summary>
    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            var json = await _js.InvokeAsync<string?>("localStorage.getItem", StoreKey);
            if (string.IsNullOrWhiteSpace(json)) return;

            var items = JsonSerializer.Deserialize<List<Snapshot>>(json, JsonOpts);
            if (items is null) return;

            foreach (var s in items)
            {
                if (string.IsNullOrWhiteSpace(s.Url)) continue;
                if (IsExcluded(s.Url)) continue;

                var key = KeyOf(s.Url);
                if (_tabs.Any(x => x.Key == key)) continue;

                var (title, icon) = Resolve(key);
                var savedTitle = s.Title ?? "";
                var staleHr = !HrNavigation.IsWorkspace(key)
                    && (savedTitle == "منابع انسانی" || savedTitle == "خانه منابع انسانی");
                // تب‌های ذخیره‌شدهٔ نسخه‌های قبل ممکن است عنوانِ لاتین (اسلاگ مسیر) داشته باشند.
                // عنوانِ کاملِ فارسی همیشه برنده است تا تب‌ها فارسی بمانند.
                var staleSlug = IsSlugLike(savedTitle);
                _tabs.Add(new TabItem
                {
                    Key = key,
                    Url = s.Url,
                    // تبِ خانه (کلید "") عنوانش را از مسیرِ ریشه می‌گیرد، نه از نسخهٔ ذخیره‌شده
                    Title = string.IsNullOrWhiteSpace(savedTitle) || staleHr || staleSlug || IsPlaceholderTitle(savedTitle) || key.Length == 0 ? title : savedTitle,
                    Icon = string.IsNullOrWhiteSpace(s.Icon) || staleHr ? icon : s.Icon,
                    // تبِ خانه همیشه سنجاق است
                    Pinned = s.Pinned || key.Length == 0
                });
            }
            Changed?.Invoke();
        }
        catch { /* localStorage در دسترس نبود — تب‌ها فقط در همین نشست می‌مانند */ }
    }

    private async Task SaveAsync()
    {
        if (!_loaded) return;
        try
        {
            var snap = _tabs.Select(t => new Snapshot
            {
                Url = t.Url,
                Title = t.Title,
                Icon = t.Icon,
                Pinned = t.Pinned
            }).ToList();
            await _js.InvokeVoidAsync("localStorage.setItem", StoreKey,
                JsonSerializer.Serialize(snap, JsonOpts));
        }
        catch { }
    }

    // ==================== همگام‌سازی با مسیر جاری ====================

    /// <summary>
    /// هر پیمایش صدا می‌زند: تبِ متناظر را می‌سازد یا به‌روز می‌کند و فعال می‌کند.
    /// </summary>
    public void Sync(string relativeUrl, RouteData routeData)
    {
        var key = KeyOf(relativeUrl);
        var t = _tabs.FirstOrDefault(x => x.Key == key);

        if (t is null)
        {
            var (title, icon) = Resolve(key);
            t = new TabItem
            {
                Key = key,
                Url = relativeUrl,
                Title = title,
                Icon = icon,
                Pinned = key.Length == 0 // تبِ خانه سنجاق است
            };
            _tabs.Add(t);
            TrimExcess();
        }
        else
        {
            t.Url = relativeUrl;
            if ((!HrNavigation.IsWorkspace(key)
                    && (t.Title == "منابع انسانی" || t.Title == "خانه منابع انسانی"))
                || IsSlugLike(t.Title) || IsPlaceholderTitle(t.Title))
            {
                var (title, icon) = Resolve(key);
                t.Title = title;
                t.Icon = icon;
            }
        }

        t.RouteData = routeData;

        var wasActiveKey = _active?.Key;
        foreach (var x in _tabs) x.IsActive = ReferenceEquals(x, t);
        _active = t;
        PushHistory(t.Key);

        Changed?.Invoke();
        if (!string.Equals(wasActiveKey, t.Key, StringComparison.Ordinal)) _ = RaiseActivatedAsync(t);
        _ = SaveAsync();
    }

    /// <summary>
    /// اعلام فعال‌شدن تب به شنونده‌ها. خارج از چرخه‌ی رندرِ همین پیمایش اجرا می‌شود
    /// و خطای یک صفحه بقیه را از تازه‌سازی باز نمی‌دارد.
    /// </summary>
    private async Task RaiseActivatedAsync(TabItem tab)
    {
        var handlers = ActivatedAsync;
        if (handlers is null) return;
        await Task.Yield();
        foreach (var handler in handlers.GetInvocationList().Cast<Func<TabItem, Task>>())
        {
            try { await handler(tab); }
            catch { /* تازه‌سازیِ یک تب نباید بقیه‌ی برنامه را بشکند. */ }
        }
    }

    /// <summary>
    /// هیچ تب فعالی نشان داده نشود — برای صفحاتی که تب نمی‌گیرند
    /// (مثل پیام‌رسان یا هنگامی که کاربر وارد نشده است).
    /// </summary>
    public void SetNoActive()
    {
        if (_active is null) return;
        foreach (var x in _tabs) x.IsActive = false;
        _active = null;
        Changed?.Invoke();
    }

    /// <summary>اگر تعداد تب‌ها از حد مجاز گذشت، قدیمی‌ترین تبِ غیرسنجاق حذف می‌شود</summary>
    private void TrimExcess()
    {
        while (_tabs.Count > MaxTabs)
        {
            var victim = _tabs.FirstOrDefault(x => x.Closable && !x.IsActive);
            if (victim is null) break;
            _tabs.Remove(victim);
            ForgetHistory(victim.Key);
        }
    }

    /// <summary>
    /// عنوانی که صفحه با &lt;PageTitle&gt; اعلام کرده را از &lt;head&gt; بخوان و روی تب فعال بنشان.
    /// <para>
    /// چرا از DOM؟ <c>&lt;PageTitle&gt;</c> فقط <c>document.title</c> را عوض می‌کند و
    /// TabService هیچ راهی برای دیدنش ندارد. بسیاری از صفحه‌ها (مثل نگار ذهنی) عنوانِ
    /// فارسی‌شان را فقط با PageTitle می‌دهند، پس تبشان اسلاگ/عنوانِ عمومی می‌ماند.
    /// </para>
    /// </summary>
    public async ValueTask SyncFromDocumentTitleAsync()
    {
        if (_active is null) return;
        string? raw;
        try { raw = await _js.InvokeAsync<string?>("document.title"); }
        catch { return; }

        var title = (raw ?? "").Trim();
        if (title.Length == 0 || IsSlugLike(title)) return;
        if (string.Equals(title, _active.Title, StringComparison.Ordinal)) return;

        _active.Title = title.Length > 46 ? title[..46] + "…" : title;
        Changed?.Invoke();
        await SaveAsync();
    }

    /// <summary>وقتی صفحه‌ای عنوانِ خود را با Layout.SetTitle معرفی کرد، عنوانِ تب هم به‌روز می‌شود</summary>
    private void OnLayoutTitleChanged()
    {
        if (_active is null) return;
        var title = (_layout.Title ?? "").Trim();
        if (title.Length == 0 || title == _active.Title) return;
        _active.Title = title.Length > 46 ? title[..46] + "…" : title;
        Changed?.Invoke();
        _ = SaveAsync();
    }

    // ==================== عملیات روی تب‌ها ====================

    public bool HasTab(string relativeUrl)
    {
        var key = KeyOf(relativeUrl);
        return _tabs.Any(x => x.Key == key);
    }

    /// <summary>رفتن به یک تب — اگر قبلاً باز بود فقط فعال می‌شود (و وضعیتش حفظ می‌ماند)</summary>
    public void Activate(TabItem t)
    {
        if (t.IsActive) return;
        _nav.NavigateTo(t.Url);
    }

    public async Task CloseAsync(TabItem t)
    {
        if (!t.Closable) return;
        var i = _tabs.IndexOf(t);
        if (i < 0) return;

        _tabs.RemoveAt(i);
        ForgetHistory(t.Key);

        if (t.IsActive)
        {
            // بعد از بستن، تبِ کناری فعال می‌شود؛ اگر تبِ بعدی نبود، قبلی
            var next = _tabs.ElementAtOrDefault(Math.Min(i, _tabs.Count - 1))
                       ?? _tabs.FirstOrDefault();
            _active = null;
            _nav.NavigateTo(next?.Url ?? "");
        }

        Changed?.Invoke();
        await SaveAsync();
    }

    public async Task CloseOthersAsync(TabItem keep)
    {
        var doomed = _tabs.Where(x => !ReferenceEquals(x, keep) && x.Closable).ToList();
        foreach (var d in doomed) { _tabs.Remove(d); ForgetHistory(d.Key); }

        if (!keep.IsActive) _nav.NavigateTo(keep.Url);
        Changed?.Invoke();
        await SaveAsync();
    }

    /// <summary>بستنِ همه — تب‌های سنجاق (مثل خانه) می‌مانند</summary>
    public async Task CloseAllAsync()
    {
        var doomed = _tabs.Where(x => x.Closable).ToList();
        foreach (var d in doomed) { _tabs.Remove(d); ForgetHistory(d.Key); }

        var home = _tabs.FirstOrDefault(x => x.Key.Length == 0);
        _nav.NavigateTo(home?.Url ?? "");
        Changed?.Invoke();
        await SaveAsync();
    }

    /// <summary>سنجاق / آزاد کردن یک تب</summary>
    public async Task TogglePinAsync(TabItem t)
    {
        t.Pinned = !t.Pinned;
        Changed?.Invoke();
        await SaveAsync();
    }

    /// <summary>بازسازیِ صفحه‌ی تب — برای وقتی که محتوا کهنه شده یا نمودار درست رسم نشده</summary>
    public void Refresh(TabItem t)
    {
        t.Version++;
        Changed?.Invoke();
    }

    /// <summary>
    /// جابه‌جاییِ تب با کشیدن و رها کردن — رها کردن روی تبِ مقصد.
    /// <para>
    /// <paramref name="after"/> مشخص می‌کند تبِ کشیده‌شده قبلِ مقصد می‌نشیند یا بعدِ آن؛
    /// پیش‌فرض <c>false</c> یعنی «قبلِ مقصد» (رها کردن روی تب = نشستن قبلِ آن).
    /// </para>
    /// </summary>
    public async Task MoveAsync(TabItem source, TabItem target, bool after = false)
    {
        var si = _tabs.IndexOf(source);
        var ti = _tabs.IndexOf(target);
        if (si < 0 || ti < 0 || si == ti) return;

        _tabs.RemoveAt(si);
        // بعد از حذفِ مبدأ، جای مقصد اگر مبدأ جلوتر بود یک خانه جابه‌جا می‌شود.
        var insertAt = ti + (after ? 1 : 0) - (si < ti ? 1 : 0);
        insertAt = Math.Clamp(insertAt, 0, _tabs.Count);
        _tabs.Insert(insertAt, source);
        Changed?.Invoke();
        await SaveAsync();
    }

    /// <summary>
    /// جابه‌جاییِ تب به یک ایندکسِ مشخص (۰ تا Count) — برای درگ لمسی/قلمی و صفحه‌کلید.
    /// ایندکس، جایگاهِ نهایی در فهرستِ پس از حذفِ مبدأ است؛ یعنی
    /// <c>MoveToAsync(t, 0)</c> تب را به ابتدای نوار و <c>MoveToAsync(t, Count-1)</c> به انتها می‌برد.
    /// </summary>
    public async Task MoveToAsync(TabItem source, int targetIndex)
    {
        var si = _tabs.IndexOf(source);
        if (si < 0) return;

        _tabs.RemoveAt(si);
        var insertAt = Math.Clamp(targetIndex, 0, _tabs.Count);
        if (insertAt == si) { _tabs.Insert(si, source); return; }
        _tabs.Insert(insertAt, source);
        Changed?.Invoke();
        await SaveAsync();
    }

    /// <summary>
    /// همان <see cref="MoveToAsync"/> ولی با کلیدِ تب — برای فراخوانی از JS (درگ لمسی).
    /// کلید پایدارتر از ارجاعِ شیء است، چون بعد از رندرِ مجددِ Blazor ارجاع‌ها عوض نمی‌شوند
    /// ولی کلید همیشه همان است.
    /// </summary>
    public async Task MoveByKeyAsync(string sourceKey, int targetIndex)
    {
        var source = _tabs.FirstOrDefault(x => x.Key == sourceKey);
        if (source is null) return;
        await MoveToAsync(source, targetIndex);
    }

    /// <summary>
    /// جابه‌جاییِ تب یک گام در فهرست — برای جابه‌جایی با صفحه‌کلید (Ctrl+←/→).
    /// <paramref name="offset"/> مثبت یعنی یک خانه به سمت انتهای نوار و منفی یعنی یک خانه به سمت ابتدا.
    /// </summary>
    public async Task MoveByOffsetAsync(TabItem source, int offset)
    {
        var si = _tabs.IndexOf(source);
        if (si < 0) return;
        await MoveToAsync(source, si + offset);
    }

    /// <summary>آیا این نوع صفحه «لایه‌ی اختصاصی» دارد و نباید تب بگیرد؟</summary>
    public static bool HasOwnLayout(Type pageType) =>
        pageType.GetCustomAttribute<Microsoft.AspNetCore.Components.LayoutAttribute>() != null;
}
