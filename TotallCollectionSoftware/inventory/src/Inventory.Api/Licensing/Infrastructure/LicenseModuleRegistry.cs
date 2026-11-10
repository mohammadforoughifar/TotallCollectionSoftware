using Inventory.Licensing.Modules;

namespace Inventory.Api.Licensing.Infrastructure;

/// <summary>
/// نگاشت کنترلرها به کد ماژول لایسنس — <b>ثابت در کد</b>.
///
/// <para>
/// چرا لازم است؟ چون فقط ۱۵ کنترلر از ۱۲۶ کنترلر پروژه صفت <c>[ModulePermission]</c>
/// دارند؛ تکیه بر صفت‌های موجود یعنی ۱۱۱ کنترلر بدون هیچ بررسی لایسنسی می‌ماند.
/// این جدول همان شکاف را می‌بندد و مستقل از RBAC است.
/// </para>
///
/// <para>
/// برای کنترلری که این‌جا نیست (کنترلر تازه)، سیاست «باز ولی لاگ‌شده» است تا انتشار
/// تازه باعث قطع کار مشتری نشود؛ ولی در لاگ API یک خط هشدار ثبت می‌شود تا تیم متوجه شود
/// و ردیف آن را به همین فایل اضافه کند. (fail-open آگاهانه برای پوشش، با ردیابی)
/// </para>
/// </summary>
public static class LicenseModuleRegistry
{
    private static readonly Dictionary<string, string> ByController = new(StringComparer.OrdinalIgnoreCase)
    {
        // ---------- پایه ----------
        ["Auth"] = ModuleCodes.Core,
        ["UserProfile"] = ModuleCodes.Core,
        ["AuditLogs"] = ModuleCodes.Core,
        ["CompanyContext"] = ModuleCodes.Core,
        ["CompanyProfile"] = ModuleCodes.Core,
        ["Files"] = ModuleCodes.Core,
        ["Https"] = ModuleCodes.Core,
        ["Permissions"] = ModuleCodes.Core,
        ["Roles"] = ModuleCodes.Core,
        ["Settings"] = ModuleCodes.Core,
        ["SystemCompanies"] = ModuleCodes.Core,
        ["SystemDepartments"] = ModuleCodes.Core,
        ["SystemOrganizations"] = ModuleCodes.Core,
        ["SystemUsers"] = ModuleCodes.Core,
        ["LetterStructures"] = ModuleCodes.Core,

        ["Dashboard"] = ModuleCodes.Dashboard,
        ["Dashboards"] = ModuleCodes.Dashboard,

        ["Archive"] = ModuleCodes.Cartable,
        ["Cartable"] = ModuleCodes.Cartable,
        ["Notifications"] = ModuleCodes.Cartable,
        ["NotifyTiming"] = ModuleCodes.Cartable,

        ["Backup"] = ModuleCodes.Backup,
        ["BackupSession"] = ModuleCodes.Backup,
        ["CentralBackup"] = ModuleCodes.Backup,

        ["License"] = ModuleCodes.Licensing,

        // ---------- کالا و فروش ----------
        ["Categories"] = ModuleCodes.Catalog,
        ["Parties"] = ModuleCodes.Catalog,
        ["Products"] = ModuleCodes.Catalog,
        ["ShopPricing"] = ModuleCodes.Catalog,
        ["Stock"] = ModuleCodes.Catalog,
        ["Units"] = ModuleCodes.Catalog,
        ["Warehouses"] = ModuleCodes.Catalog,

        ["GoodsIssues"] = ModuleCodes.Sales,
        ["Orders"] = ModuleCodes.Sales,

        ["SalesVisit"] = ModuleCodes.SalesVisit,

        ["InvBaseData"] = ModuleCodes.Warehousing,
        ["InvDocs"] = ModuleCodes.Warehousing,
        ["InvProducts"] = ModuleCodes.Warehousing,

        ["Stocktaking"] = ModuleCodes.Stocktaking,

        ["Invoicing"] = ModuleCodes.Invoicing,

        ["Moadian"] = ModuleCodes.Moadian,

        // ---------- مالی ----------
        ["AccDimensions"] = ModuleCodes.Accounting,
        ["Accounting"] = ModuleCodes.Accounting,
        ["Budgets"] = ModuleCodes.Accounting,
        ["FixedAssets"] = ModuleCodes.Accounting,
        ["Treasury"] = ModuleCodes.Treasury,
        ["Expenses"] = ModuleCodes.Expenses,

        // ---------- عملیات ----------
        ["Repairs"] = ModuleCodes.Repairs,
        ["ShopTradeIn"] = ModuleCodes.Repairs,
        ["ShopWarranty"] = ModuleCodes.Repairs,

        ["Karfarmas"] = ModuleCodes.Projects,
        ["ProjectAttach"] = ModuleCodes.Projects,
        ["ProjectCartable"] = ModuleCodes.Projects,
        ["Projects"] = ModuleCodes.Projects,
        ["ReportWorks"] = ModuleCodes.Projects,
        ["TypeFactors"] = ModuleCodes.Projects,

        ["WorkOrders"] = ModuleCodes.WorkOrders,
        ["StandaloneMindFlows"] = ModuleCodes.WorkOrders,
        ["WorkProjects"] = ModuleCodes.WorkOrders,

        // ---------- اتوماسیون اداری ----------
        ["Email"] = ModuleCodes.Letters,
        ["IncomingLetters"] = ModuleCodes.Letters,
        ["InnerLetters"] = ModuleCodes.Letters,
        ["MeetingMinutes"] = ModuleCodes.Letters,
        ["OutgoingLetters"] = ModuleCodes.Letters,

        ["CctvCameras"] = ModuleCodes.OfficeAssets,
        ["CctvNvrs"] = ModuleCodes.OfficeAssets,
        ["CctvScan"] = ModuleCodes.OfficeAssets,
        ["NetworkScan"] = ModuleCodes.OfficeAssets,
        ["OfficeMachineAgent"] = ModuleCodes.OfficeAssets,
        ["OfficeMachines"] = ModuleCodes.OfficeAssets,

        // ---------- آرشیو و IT ----------
        ["DocAccessRequests"] = ModuleCodes.DocArchive,
        ["DocBulk"] = ModuleCodes.DocArchive,
        ["DocChat"] = ModuleCodes.DocArchive,
        ["DocCollab"] = ModuleCodes.DocArchive,
        ["DocContentCompare"] = ModuleCodes.DocArchive,
        ["DocEntityLinks"] = ModuleCodes.DocArchive,
        ["DocEvolution"] = ModuleCodes.DocArchive,
        ["DocFavorites"] = ModuleCodes.DocArchive,
        ["DocFolders"] = ModuleCodes.DocArchive,
        ["DocImport"] = ModuleCodes.DocArchive,
        ["DocPrint"] = ModuleCodes.DocArchive,
        ["DocSearch"] = ModuleCodes.DocArchive,
        ["DocSemantic"] = ModuleCodes.DocArchive,
        ["DocSettings"] = ModuleCodes.DocArchive,
        ["DocTags"] = ModuleCodes.DocArchive,
        ["Documents"] = ModuleCodes.DocArchive,

        ["Ipam"] = ModuleCodes.ItAssets,
        ["ItRemote"] = ModuleCodes.ItAssets,
        ["ItRequests"] = ModuleCodes.ItAssets,
        ["ItVault"] = ModuleCodes.ItAssets,
        ["Servers"] = ModuleCodes.ItAssets,
        ["SystemInfo"] = ModuleCodes.ItAssets,

        ["Chat"] = ModuleCodes.Chat,

        // ---------- منابع انسانی ----------
        ["Attendance"] = ModuleCodes.HrFa,
        ["FaHrPersonnel"] = ModuleCodes.HrFa,
        ["HrPay"] = ModuleCodes.HrFa,
        ["HrPerf"] = ModuleCodes.HrFa,
        ["HrTime"] = ModuleCodes.HrFa,
        ["LeaveRequests"] = ModuleCodes.HrFa,

        ["HrCore"] = ModuleCodes.HrCore,
        ["Recruitment"] = ModuleCodes.HrCore,
        ["HrMain"] = ModuleCodes.HrCore,
        ["HrTalent"] = ModuleCodes.HrCore,

        ["FaAtt"] = ModuleCodes.FahamAtt,
        ["FaPay"] = ModuleCodes.FahamPay,
        ["FaPayExtra"] = ModuleCodes.FahamPay,
        ["FaLms"] = ModuleCodes.FahamLms,
        ["FaCom"] = ModuleCodes.FahamCom,

        // ---------- گزارش و ابزار ----------
        ["Export"] = ModuleCodes.Reports,
        ["ReportStudio"] = ModuleCodes.Reports,
        ["OperationsReports"] = ModuleCodes.Reports,
        ["Reports"] = ModuleCodes.Reports,

        ["Ai"] = ModuleCodes.Ai,

        ["DevTeam"] = ModuleCodes.DevTeam,
        ["DevTeamGitWebhook"] = ModuleCodes.DevTeam,

        ["Remote"] = ModuleCodes.ItAssets
    };

    /// <summary>کنترلرهای معاف از بررسی لایسنس (زیرساخت خود لایسنس و احراز هویت).</summary>
    private static readonly HashSet<string> ExemptControllers = new(StringComparer.OrdinalIgnoreCase)
    {
        "License", "Auth", "Health"
    };

    /// <summary>آیا این کنترلر از بررسی لایسنس معاف است؟</summary>
    public static bool IsExempt(string? controllerName)
        => !string.IsNullOrWhiteSpace(controllerName) && ExemptControllers.Contains(controllerName);

    /// <summary>کد ماژول یک کنترلر؛ null یعنی نگاشت نشده.</summary>
    public static string? Resolve(string? controllerName)
    {
        if (string.IsNullOrWhiteSpace(controllerName)) return null;
        return ByController.TryGetValue(controllerName, out var module) ? module : null;
    }

    /// <summary>کد ماژول از مسیر درخواست — برای کنترلرهای ماژول RadisHr (پیشوند /radis-hr).</summary>
    public static string? ResolveFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (path.StartsWith("/radis-hr", StringComparison.OrdinalIgnoreCase)) return ModuleCodes.BonHr;
        return null;
    }

    /// <summary>همهٔ کنترلرهای نگاشت‌نشده (برای تست و ابزار نگه‌داری).</summary>
    public static IReadOnlyCollection<string> MappedControllers => ByController.Keys.ToList();
}
