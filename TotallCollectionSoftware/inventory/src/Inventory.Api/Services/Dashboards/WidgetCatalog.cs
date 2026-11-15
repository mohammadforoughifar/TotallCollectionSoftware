using Inventory.Shared.Dtos;

namespace Inventory.Api.Services.Dashboards;

// =====================================================================
//  کاتالوگ ویجت‌های داشبورد شخصی
//
//  هر ویجت یک «ماژول RBAC» دارد؛ کاربر فقط ویجت‌هایی را در پنل طراحی
//  می‌بیند که مجوز مشاهدهٔ آن ماژول را داشته باشد (و گرفتن داده هم در
//  WidgetDataService دوباره چک می‌شود).
//
//  افزودن ویجت جدید = یک ردیف اینجا + یک case در WidgetDataService.
// =====================================================================
public static class WidgetCatalog
{
    private const string Rial = "ریال";

    private static WidgetDefDto W(string key, string title, string cat, string icon, string module,
        DashWidgetKind kind, int w = 3, int h = 2, bool range = false, string unit = "",
        string drill = "", string desc = "", params DashChartType[] charts) => new()
    {
        Key = key,
        Title = title,
        Category = cat,
        Icon = icon,
        Module = module,
        Kind = kind,
        DefaultW = w,
        DefaultH = h,
        HasRange = range,
        Unit = unit,
        Drilldown = drill,
        Description = desc,
        ChartTypes = charts.ToList()
    };

    private static WidgetOptionDefDto Limit(int def = 5) => new()
    {
        Key = "limit", Label = "تعداد ردیف", Kind = "select",
        Choices = new List<string> { "3", "5", "8", "10", "15" }, Default = def.ToString()
    };

    /// <summary>دامنهٔ دید کاربر روی دستور کار — مقادیر همان برچسب‌اند چون UI مقدار را نمایش می‌دهد.</summary>
    private static WidgetOptionDefDto WoScopeOption() => new()
    {
        Key = "scope", Label = "دامنه", Kind = "select",
        Choices = new List<string> { "هر دو", "محول‌شده به من", "صادرشده توسط من" },
        Default = "هر دو"
    };

    public static readonly WidgetDefDto[] All = Build();

    private static WidgetDefDto[] Build()
    {
        var list = new List<WidgetDefDto>
        {
            // ==================== فروش و فاکتور ====================
            W("fac-sales-today", "فروش امروز", "فروش و فاکتور", "bi-cart-check", "FacInvoices",
              DashWidgetKind.Kpi, 3, 2, false, Rial, "fac/invoices", "جمع فاکتورهای فروش قطعی امروز"),
            W("fac-sales-period", "جمع فروش", "فروش و فاکتور", "bi-graph-up-arrow", "FacInvoices",
              DashWidgetKind.Kpi, 3, 2, true, Rial, "fac/invoices", "جمع فاکتورهای فروش قطعی در بازهٔ انتخابی + مقایسه با بازهٔ قبل"),
            W("fac-purchase-period", "جمع خرید", "فروش و فاکتور", "bi-bag-plus", "FacInvoices",
              DashWidgetKind.Kpi, 3, 2, true, Rial, "fac/invoices", "جمع فاکتورهای خرید قطعی در بازهٔ انتخابی"),
            W("fac-invoice-count", "تعداد فاکتور", "فروش و فاکتور", "bi-receipt", "FacInvoices",
              DashWidgetKind.Kpi, 3, 2, true, "عدد", "fac/invoices", "تعداد فاکتورهای قطعی بازه"),
            W("fac-sales-trend", "روند فروش", "فروش و فاکتور", "bi-activity", "FacInvoices",
              DashWidgetKind.Chart, 6, 3, true, Rial, "fac/reports/summary", "جمع فروش به تفکیک ماه (۱۲ ماه اخیر)",
              DashChartType.Line, DashChartType.Bar),
            W("fac-sales-vs-purchase", "فروش در برابر خرید", "فروش و فاکتور", "bi-bar-chart", "FacInvoices",
              DashWidgetKind.Chart, 6, 3, true, Rial, "fac/reports/summary", "مقایسهٔ ماهانهٔ فروش و خرید",
              DashChartType.Bar, DashChartType.Line),
            W("fac-top-parties", "برترین طرف حساب‌ها", "فروش و فاکتور", "bi-people", "FacInvoices",
              DashWidgetKind.Table, 6, 3, true, Rial, "fac/invoices", "پرفروش‌ترین مشتریان در بازه"),
            W("fac-top-products", "کالاهای پرفروش", "فروش و فاکتور", "bi-box-seam", "FacInvoices",
              DashWidgetKind.Table, 6, 3, true, "", "fac/invoices", "پرفروش‌ترین کالاها بر اساس مبلغ در بازه"),

            // ==================== انبارداری ====================
            W("inv-stock-value", "ارزش موجودی", "انبارداری", "bi-clipboard-check", "InvDocs",
              DashWidgetKind.Kpi, 3, 2, false, Rial, "inv/stock", "ارزش کل موجودی انبارها (میانگین موزون)"),
            W("inv-doc-count", "اسناد انبار", "انبارداری", "bi-journal-text", "InvDocs",
              DashWidgetKind.Kpi, 3, 2, true, "عدد", "inv/docs", "تعداد رسید و حوالهٔ قطعی بازه"),
            W("inv-doc-trend", "روند اسناد انبار", "انبارداری", "bi-graph-up", "InvDocs",
              DashWidgetKind.Chart, 6, 3, true, "عدد", "inv/docs", "تعداد اسناد انبار به تفکیک ماه",
              DashChartType.Bar, DashChartType.Line),

            // ==================== کالا و موجودی ====================
            W("prod-reorder-count", "اقلام زیر نقطه سفارش", "کالا و موجودی", "bi-exclamation-triangle", "Products",
              DashWidgetKind.Kpi, 3, 2, false, "قلم", "reports/reorder", "تعداد کالاهایی که موجودی‌شان زیر نقطه سفارش است"),
            W("prod-reorder-list", "فهرست اقلام رو به اتمام", "کالا و موجودی", "bi-list-ul", "Products",
              DashWidgetKind.Table, 6, 3, false, "", "reports/reorder", "کالاهای زیر نقطه سفارش به ترتیب کمبود"),
            W("prod-stock-by-warehouse", "موجودی به تفکیک انبار", "کالا و موجودی", "bi-building", "Products",
              DashWidgetKind.Chart, 4, 3, false, "", "inv/stock", "سهم هر انبار از تعداد اقلام موجود",
              DashChartType.Doughnut, DashChartType.Pie, DashChartType.Bar),

            // ==================== حسابداری ====================
            W("acc-voucher-count", "اسناد حسابداری", "حسابداری", "bi-journal-bookmark", "AccVouchers",
              DashWidgetKind.Kpi, 3, 2, true, "عدد", "acc/vouchers", "تعداد اسناد حسابداری بازه"),
            W("acc-voucher-trend", "روند صدور سند", "حسابداری", "bi-journals", "AccVouchers",
              DashWidgetKind.Chart, 6, 3, true, "عدد", "acc/vouchers", "تعداد سند به تفکیک ماه",
              DashChartType.Line, DashChartType.Bar),

            // ==================== خزانه‌داری ====================
            W("trs-receipt-period", "دریافتی خزانه", "خزانه‌داری", "bi-cash-coin", "TrsVouchers",
              DashWidgetKind.Kpi, 3, 2, true, Rial, "trs/vouchers", "جمع اسناد دریافت قطعی بازه"),
            W("trs-payment-period", "پرداختی خزانه", "خزانه‌داری", "bi-wallet2", "TrsVouchers",
              DashWidgetKind.Kpi, 3, 2, true, Rial, "trs/vouchers", "جمع اسناد پرداخت قطعی بازه"),
            W("trs-flow-trend", "روند دریافت و پرداخت", "خزانه‌داری", "bi-list-columns", "TrsVouchers",
              DashWidgetKind.Chart, 6, 3, true, Rial, "trs/reports/flow", "مقایسهٔ ماهانهٔ دریافت و پرداخت",
              DashChartType.Bar, DashChartType.Line),
            W("trs-cheques-amount", "چک‌های نزد ما", "خزانه‌داری", "bi-card-checklist", "TrsCheques",
              DashWidgetKind.Kpi, 3, 2, false, Rial, "trs/cheques", "جمع مبلغ چک‌های دریافتیِ وصول‌نشده"),
            W("trs-cheques-due", "چک‌های سررسیدنزدیک", "خزانه‌داری", "bi-calendar-event", "TrsCheques",
              DashWidgetKind.Table, 6, 3, false, Rial, "trs/cheques", "چک‌های در دست و در جریان وصول با سررسید نزدیک"),

            // ==================== هزینه ====================
            W("exp-period", "هزینه‌ها", "مالی", "bi-cash-stack", "Expenses",
              DashWidgetKind.Kpi, 3, 2, true, Rial, "expenses", "جمع هزینه‌های ثبت‌شده در بازه"),
            W("exp-by-category", "هزینه به تفکیک دسته", "مالی", "bi-pie-chart", "Expenses",
              DashWidgetKind.Chart, 4, 3, true, Rial, "expenses", "سهم هر دسته هزینه در بازه",
              DashChartType.Pie, DashChartType.Doughnut, DashChartType.Bar),

            // ==================== دستور کار ====================
            // همهٔ ویجت‌های این بخش توسط WorkOrderWidgets پاسخ داده می‌شوند (نه switch اصلی).
            // دامنه پیش‌فرض «هر دو» = دستورهای صادرشده توسط من یا محول‌شده به من، در شرکت فعال.
            W("wo-open", "دستور کارهای باز", "دستور کار", "bi-card-checklist", "WorkOrders",
              DashWidgetKind.Kpi, 3, 2, false, "دستور", "work-orders", "دستورهای کار بازِ محول‌شده به من یا از طرف من"),
            W("wo-overdue", "دستور کارهای معوق", "دستور کار", "bi-alarm", "WorkOrders",
              DashWidgetKind.Kpi, 3, 2, false, "دستور", "work-orders?tab=mine", "دستورهای بازی که از مهلتشان گذشته"),
            W("wo-by-status", "وضعیت دستور کارها", "دستور کار", "bi-kanban", "WorkOrders",
              DashWidgetKind.Chart, 4, 3, false, "دستور", "work-orders?tab=stats", "توزیع باز/بسته در دستورهای من",
              DashChartType.Doughnut, DashChartType.Pie, DashChartType.Bar),

            // ---------- خانوادهٔ A: میز کار من ----------
            W("wo-my-inbox", "محول‌شده به من", "دستور کار", "bi-inbox-fill", "WorkOrders",
              DashWidgetKind.Kpi, 3, 2, false, "دستور", "work-orders?tab=assigned",
              "دستورهای بازی که دیگران به من واگذار کرده‌اند"),
            W("wo-my-unseen", "دستورهای دیده‌نشده", "دستور کار", "bi-eye-slash", "WorkOrders",
              DashWidgetKind.Kpi, 3, 2, false, "دستور", "work-orders?tab=assigned",
              "دستورهای بازِ محول‌شده به من که هنوز باز نکرده‌ام"),
            W("wo-due-today", "مهلت امروز", "دستور کار", "bi-calendar-day", "WorkOrders",
              DashWidgetKind.Kpi, 3, 2, false, "دستور", "work-orders?tab=calendar",
              "دستورهای بازی که مهلتشان امروز است"),
            W("wo-due-week", "مهلت ۷ روز آینده", "دستور کار", "bi-calendar-week", "WorkOrders",
              DashWidgetKind.Kpi, 3, 2, false, "دستور", "work-orders?tab=calendar",
              "دستورهای بازی که مهلتشان از فردا تا هفت روز آینده است"),
            W("wo-my-next", "نزدیک‌ترین مهلت‌ها", "دستور کار", "bi-calendar-event", "WorkOrders",
              DashWidgetKind.Table, 6, 3, false, "", "work-orders?tab=calendar",
              "دستورهای بازِ من به ترتیب نزدیکی مهلت — با کلیک روی هر سطر، خود دستور باز می‌شود"),

            // ---------- خانوادهٔ B: سلامت جریان ----------
            W("wo-ontime-rate", "نرخ انجام به‌موقع", "دستور کار", "bi-bullseye", "WorkOrders",
              DashWidgetKind.Kpi, 3, 2, true, "٪", "work-orders?tab=stats",
              "سهم انجام به‌موقع از دستورهای بسته‌شدهٔ بازه (مخرج: به‌موقع + با تأخیر + بسته بدون انجام)"),
            W("wo-outcome-mix", "ترکیب نتیجهٔ دستورها", "دستور کار", "bi-pie-chart-fill", "WorkOrders",
              DashWidgetKind.Chart, 4, 3, true, "دستور", "work-orders?tab=stats",
              "پنج حالت: باز، گذشته از مهلت، انجام به‌موقع، انجام با تأخیر، بسته بدون انجام",
              DashChartType.Doughnut, DashChartType.Pie, DashChartType.Bar),
            W("wo-closed-nodone", "بسته بدون انجام", "دستور کار", "bi-x-octagon", "WorkOrders",
              DashWidgetKind.Kpi, 3, 2, true, "دستور", "work-orders?tab=archive",
              "دستورهایی که بسته شده‌اند ولی همهٔ گیرندگان «انجام شد» نزده‌اند"),
            W("wo-open-age", "سن معوق‌ها", "دستور کار", "bi-hourglass-bottom", "WorkOrders",
              DashWidgetKind.Chart, 4, 3, false, "دستور", "work-orders?tab=mine",
              "توزیع دستورهای معوق در سبدهای ۱ روز، ۲ تا ۷، ۸ تا ۳۰ و بیش از ۳۰ روز",
              DashChartType.Bar, DashChartType.Doughnut),
            W("wo-created-vs-closed", "ایجاد در برابر بستن", "دستور کار", "bi-arrow-left-right", "WorkOrders",
              DashWidgetKind.Chart, 6, 3, false, "دستور", "work-orders?tab=stats",
              "دوازده ماه شمسی اخیر: چند دستور ایجاد و چند دستور بسته شده است",
              DashChartType.Bar, DashChartType.Line),
            W("wo-first-response", "زمان تا اولین پاسخ", "دستور کار", "bi-stopwatch", "WorkOrders",
              DashWidgetKind.Kpi, 3, 2, true, "ساعت", "work-orders?tab=perf",
              "میانگین فاصلهٔ ثبت دستور تا اولین پاسخ گیرنده — هرچه کمتر، بهتر"),

            // ---------- خانوادهٔ C: تیم ----------
            W("wo-pending-approval", "در انتظار تأیید من", "دستور کار", "bi-hourglass-split", "WorkOrders",
              DashWidgetKind.Kpi, 3, 2, false, "پاسخ", "work-orders?tab=mine",
              "پاسخ‌هایی که گیرنده داده ولی من هنوز تأیید یا رد نکرده‌ام"),
            W("wo-pending-approval-list", "فهرست در انتظار تأیید", "دستور کار", "bi-list-check", "WorkOrders",
              DashWidgetKind.Table, 6, 3, false, "", "work-orders?tab=mine",
              "پاسخ‌های تأییدنشده به ترتیب نزدیکی مهلت"),
            W("wo-team-load", "بار کاری هر نفر", "دستور کار", "bi-people-fill", "WorkOrders",
              DashWidgetKind.Chart, 6, 3, false, "دستور", "work-orders?tab=kanban",
              "تعداد دستور بازِ واگذارشده به هر گیرنده، در دستورهای صادرشده توسط من",
              DashChartType.Bar, DashChartType.Doughnut),
            W("wo-team-scoreboard", "عملکرد گیرندگان", "دستور کار", "bi-trophy", "WorkOrders",
              DashWidgetKind.Table, 9, 4, true, "", "work-orders?tab=perf",
              "کل دریافتی، انجام‌شده، به‌موقع، بی‌پاسخ و درصد به‌موقع برای گیرندگانِ دستورهای من"),
            W("wo-unanswered", "بی‌پاسخ‌ترین گیرندگان", "دستور کار", "bi-chat-left-dots", "WorkOrders",
              DashWidgetKind.Table, 6, 3, false, "", "work-orders?tab=perf",
              "گیرندگانی که بیشترین دستور بازِ بی‌پاسخ را دارند"),

            // ---------- خانوادهٔ D/E: کیفیت فرآیند ----------
            W("wo-extension-hot", "تمدیدهای مکرر", "دستور کار", "bi-arrow-repeat", "WorkOrders",
              DashWidgetKind.Table, 6, 3, false, "", "work-orders?tab=mine",
              "دستورهایی که سه بار یا بیشتر تمدید شده‌اند (سقف مجاز ۵ بار)"),
            W("wo-checklist-rate", "تکمیل چک‌لیست", "دستور کار", "bi-check2-square", "WorkOrders",
              DashWidgetKind.Kpi, 3, 2, false, "٪", "work-orders?tab=mine",
              "درصد آیتم‌های تیک‌خوردهٔ چک‌لیست روی دستورهای باز"),
            W("wo-source-mix", "مبدأ دستورها", "دستور کار", "bi-diagram-3", "WorkOrders",
              DashWidgetKind.Chart, 4, 3, true, "دستور", "work-orders",
              "دستورها از کجا آمده‌اند: ثبت دستی، نامه، مدرک، صورتجلسه یا هشدار سرور",
              DashChartType.Doughnut, DashChartType.Pie, DashChartType.Bar),

            // ---------- پروژه کاری ----------
            W("wp-my-progress", "پیشرفت پروژه‌های من", "پروژه کاری", "bi-briefcase", "WorkProjects",
              DashWidgetKind.Table, 9, 4, false, "", "work-projects",
              "پروژه‌های بازِ من با پیشرفت وزنی، تعداد دستور باز و معوق"),

            // ==================== اتوماسیون اداری ====================
            W("off-letters-count", "نامه‌های ثبت‌شده", "اتوماسیون اداری", "bi-envelope-paper", "InnerLetters",
              DashWidgetKind.Kpi, 3, 2, true, "نامه", "letters", "تعداد کل نامه‌های داخلی، صادره و واردهٔ ثبت‌شده در بازه"),
            W("off-letters-by-type", "نامه‌ها به تفکیک نوع", "اتوماسیون اداری", "bi-pie-chart-fill", "InnerLetters",
              DashWidgetKind.Chart, 4, 3, true, "نامه", "letters", "سهم نامه‌های داخلی، صادره و وارده از کل نامه‌های بازه",
              DashChartType.Pie, DashChartType.Doughnut, DashChartType.Bar),
            W("off-letters-trend", "روند نامه‌ها", "اتوماسیون اداری", "bi-graph-up-arrow", "InnerLetters",
              DashWidgetKind.Chart, 6, 3, true, "نامه", "letters", "تعداد نامه به تفکیک ماه و نوع (داخلی/صادره/وارده) در ۱۲ ماه اخیر",
              DashChartType.Line, DashChartType.Bar),
            W("off-letters-by-status", "وضعیت نامه‌ها", "اتوماسیون اداری", "bi-kanban", "InnerLetters",
              DashWidgetKind.Chart, 4, 3, true, "نامه", "letters", "توزیع وضعیت نامه‌ها (پیش‌نویس، در گردش، صادر شده، در جریان، بایگانی‌شده)",
              DashChartType.Doughnut, DashChartType.Pie, DashChartType.Bar),
            W("off-letters-by-urgency", "فوریت نامه‌ها", "اتوماسیون اداری", "bi-alarm", "InnerLetters",
              DashWidgetKind.Chart, 4, 3, true, "نامه", "letters", "توزیع فوریت نامه‌ها به تفکیک عادی/فوری/خیلی فوری/آنی",
              DashChartType.Doughnut, DashChartType.Pie, DashChartType.Bar),
            W("off-erja-unread", "ارجاع‌های خوانده‌نشده", "اتوماسیون اداری", "bi-envelope-exclamation", "InnerLetters",
              DashWidgetKind.Kpi, 3, 2, false, "مورد", "letters/inbox", "ارجاع‌هایی که هنوز خوانده نشده‌اند"),

            // ---------- نامه صادره ----------
            W("off-out-count", "نامه‌های صادره", "اتوماسیون اداری", "bi-envelope-arrow-up", "InnerLetters",
              DashWidgetKind.Kpi, 3, 2, true, "نامه", "letters/outgoing", "تعداد نامه‌های صادرهٔ ثبت‌شده در بازه"),
            W("off-out-by-status", "وضعیت نامه‌های صادره", "اتوماسیون اداری", "bi-send-check", "InnerLetters",
              DashWidgetKind.Chart, 4, 3, true, "نامه", "letters/outgoing",
              "توزیع وضعیت نامه‌های صادره: پیش‌نویس، در گردش تایید، تایید شده، صادر شده",
              DashChartType.Doughnut, DashChartType.Pie, DashChartType.Bar),
            W("off-out-by-method", "روش ارسال صادره", "اتوماسیون اداری", "bi-truck", "InnerLetters",
              DashWidgetKind.Chart, 4, 3, true, "نامه", "letters/outgoing",
              "سهم هر روش ارسال (پست، ایمیل، پیک، نمابر و ...) در نامه‌های صادره",
              DashChartType.Pie, DashChartType.Doughnut, DashChartType.Bar),
            W("off-out-trend", "روند نامه‌های صادره", "اتوماسیون اداری", "bi-graph-up", "InnerLetters",
              DashWidgetKind.Chart, 6, 3, true, "نامه", "letters/outgoing",
              "مقایسهٔ ماهانهٔ نامه‌های ثبت‌شده و نامه‌های واقعاً صادرشده (۱۲ ماه اخیر)",
              DashChartType.Line, DashChartType.Bar),
            W("off-out-top-receivers", "گیرندگان پرمکاتبه", "اتوماسیون اداری", "bi-building-up", "InnerLetters",
              DashWidgetKind.Table, 6, 3, true, "", "letters/outgoing",
              "سازمان‌هایی که بیشترین نامهٔ صادره برایشان ارسال شده"),
            W("off-out-pending", "صادره در انتظار صدور", "اتوماسیون اداری", "bi-hourglass-split", "InnerLetters",
              DashWidgetKind.Kpi, 3, 2, false, "نامه", "letters/outgoing",
              "نامه‌های صادره‌ای که هنوز صادر نشده‌اند (پیش‌نویس یا در گردش تایید)"),

            // ---------- نامه داخلی ----------
            W("off-inner-count", "نامه‌های داخلی", "اتوماسیون اداری", "bi-envelope-paper-heart", "InnerLetters",
              DashWidgetKind.Kpi, 3, 2, true, "نامه", "letters", "تعداد نامه‌های داخلی ثبت‌شده در بازه"),
            W("off-inner-by-urgency", "فوریت نامه‌های داخلی", "اتوماسیون اداری", "bi-lightning-charge", "InnerLetters",
              DashWidgetKind.Chart, 4, 3, true, "نامه", "letters",
              "توزیع فوریت نامه‌های داخلی (عادی، فوری، آنی)",
              DashChartType.Doughnut, DashChartType.Pie, DashChartType.Bar),
            W("off-inner-by-conf", "محرمانگی نامه‌های داخلی", "اتوماسیون اداری", "bi-shield-lock", "InnerLetters",
              DashWidgetKind.Chart, 4, 3, true, "نامه", "letters",
              "توزیع سطح محرمانگی نامه‌های داخلی (عادی، محرمانه، سری)",
              DashChartType.Doughnut, DashChartType.Pie, DashChartType.Bar),
            W("off-inner-trend", "روند نامه‌های داخلی", "اتوماسیون اداری", "bi-activity", "InnerLetters",
              DashWidgetKind.Chart, 6, 3, true, "نامه", "letters",
              "تعداد نامه‌های داخلی به تفکیک ماه (۱۲ ماه اخیر)",
              DashChartType.Line, DashChartType.Bar),
            W("off-inner-top-senders", "پرمکاتبه‌ترین ثبت‌کنندگان", "اتوماسیون اداری", "bi-person-lines-fill", "InnerLetters",
              DashWidgetKind.Table, 6, 3, true, "", "letters",
              "کاربرانی که بیشترین نامهٔ داخلی را ثبت کرده‌اند"),
            W("off-inner-overdue", "نامه‌های داخلی معوق", "اتوماسیون اداری", "bi-clock-history", "InnerLetters",
              DashWidgetKind.Kpi, 3, 2, false, "نامه", "letters/inbox",
              "نامه‌های داخلی که مهلت پاسخ ارجاعشان گذشته و هنوز بی‌پاسخ‌اند"),

            // ==================== سایر ماژول‌ها ====================
            W("it-open", "درخواست‌های IT باز", "مدیریت سیستم", "bi-headset", "ItRequests",
              DashWidgetKind.Kpi, 3, 2, false, "عدد", "it-requests", "درخواست‌های خدمت IT که هنوز بسته نشده‌اند"),
            W("hr-employees", "پرسنل فعال", "منابع انسانی", "bi-people", "HrCore",
              DashWidgetKind.Kpi, 3, 2, false, "نفر", "hr-main/employees", "تعداد پرسنل فعال در سامانه کارگزینی"),
            W("fa-att-today", "ترددهای امروز", "منابع انسانی", "bi-fingerprint", "FaAtt",
              DashWidgetKind.Kpi, 3, 2, false, "رکورد", "fa-att/daily", "تعداد رکوردهای ورود و خروج ثبت‌شدهٔ امروز"),
            W("doc-expiring", "مدارک رو به انقضا", "آرشیو اسناد", "bi-folder2-open", "DocArchive",
              DashWidgetKind.Table, 6, 3, false, "", "doc-archive", "مدارکی که تاریخ انقضایشان نزدیک است")
        };

        // گزینه‌های اختصاصی
        foreach (var k in new[] { "fac-top-parties", "fac-top-products", "prod-reorder-list", "trs-cheques-due", "doc-expiring",
                                  "off-out-top-receivers", "off-inner-top-senders" })
            list.First(x => x.Key == k).Options.Add(Limit(k == "trs-cheques-due" || k == "doc-expiring" ? 8 : 5));

        // ---------- گزینه‌های دستور کار ----------
        // «دامنه» فقط شخصی است. نمای کل شرکت عمداً اضافه نشده تا بدون مجوز تازه، دادهٔ
        // دیگران قابل دیدن نباشد.
        foreach (var k in new[] { "wo-open", "wo-overdue", "wo-by-status", "wo-due-today", "wo-due-week",
                                  "wo-ontime-rate", "wo-outcome-mix", "wo-closed-nodone", "wo-open-age",
                                  "wo-created-vs-closed", "wo-first-response", "wo-extension-hot",
                                  "wo-checklist-rate", "wo-source-mix" })
            list.First(x => x.Key == k).Options.Add(WoScopeOption());

        foreach (var k in new[] { "wo-my-next", "wo-pending-approval-list", "wo-team-load",
                                  "wo-team-scoreboard", "wo-unanswered", "wo-extension-hot", "wp-my-progress" })
            list.First(x => x.Key == k).Options.Add(Limit(k == "wo-team-scoreboard" ? 15 : 10));

        return list.ToArray();
    }

    public static WidgetDefDto? Find(string key) =>
        All.FirstOrDefault(w => w.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>فقط ویجت‌هایی که کاربر مجوز مشاهدهٔ ماژولشان را دارد.</summary>
    public static IReadOnlyList<WidgetDefDto> Visible(Func<string, string, bool> hasPermission) =>
        All.Where(w => hasPermission(w.Module, "Read")
                    || hasPermission(w.Module, "View")
                    || hasPermission(w.Module, "Access")).ToList();
}
