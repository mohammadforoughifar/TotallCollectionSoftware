using Inventory.Client;
using Inventory.Client.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// HttpClient با آدرس داینامیک — همان آدرسی که ApiOptions در زمان اجرا تعیین می‌کند
// (قبلاً هاردکد به http://localhost:5100 بود و صفحات سیستم روی پورت/دامنه دیگر کار نمی‌کردند)
builder.Services.AddScoped(sp =>
{
    var opts = sp.GetRequiredService<ApiOptions>();
    var baseUrl = string.IsNullOrWhiteSpace(opts.BaseUrl) ? "http://localhost:5100" : opts.BaseUrl;
    return new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
});
builder.Services.AddSingleton<ApiOptions>();
builder.Services.AddSingleton<IAuthState, AuthState>();
builder.Services.AddScoped<IAuthApi, AuthApi>();

// ---------- ثبت سرویس‌ها با اینترفیس (اصل وارونگی وابستگی — DIP) ----------
builder.Services.AddScoped<IApiClient, ApiClient>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IReferrerService, ReferrerService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IUnitService, UnitService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IWarehouseService, WarehouseService>();
builder.Services.AddScoped<IPartyService, PartyService>();
builder.Services.AddScoped<IStockService, StockService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IReportService, ReportService>();
// ---------- ماژول انبارداری ----------
builder.Services.AddScoped<IInvCategoryService, InvCategoryService>();
builder.Services.AddScoped<IInvAttributeService, InvAttributeService>();
builder.Services.AddScoped<IInvProductService, InvProductService>();
builder.Services.AddScoped<IInvWarehouseService, InvWarehouseService>();
builder.Services.AddScoped<IInvDocTypeService, InvDocTypeService>();
builder.Services.AddScoped<IInvDocService, InvDocService>();
builder.Services.AddScoped<IInvReportService, InvReportService>();

// ---------- ماژول حسابداری ----------
builder.Services.AddScoped<IAccFiscalYearService, AccFiscalYearService>();
builder.Services.AddScoped<IAccAccountService, AccAccountService>();
builder.Services.AddScoped<IAccVoucherService, AccVoucherService>();
builder.Services.AddScoped<IAccReportService, AccReportService>();
builder.Services.AddScoped<IAccInvRuleService, AccInvRuleService>();
builder.Services.AddScoped<IAccDimensionService, AccDimensionService>();
builder.Services.AddScoped<IFixedAssetCategoryService, FixedAssetCategoryService>();
builder.Services.AddScoped<IFixedAssetService, FixedAssetService>();
builder.Services.AddScoped<IFixedAssetRunService, FixedAssetRunService>();
builder.Services.AddScoped<IBudgetService, BudgetService>();

// ---------- ماژول فاکتور ----------
builder.Services.AddScoped<IFacInvoiceService, FacInvoiceService>();
builder.Services.AddScoped<IFacRuleService, FacRuleService>();
builder.Services.AddScoped<IFacReportService, FacReportService>();

// ---------- سامانه مودیان + چاپگر مالی ----------
builder.Services.AddScoped<IMoadianClientService, MoadianClientService>();
builder.Services.AddScoped<IFiscalPrinterClientService, FiscalPrinterClientService>();

// ماژول خزانه‌داری
builder.Services.AddScoped<ITrsAccountService, TrsAccountService>();
builder.Services.AddScoped<ITrsVoucherService, TrsVoucherService>();
builder.Services.AddScoped<ITrsChequeService, TrsChequeService>();
builder.Services.AddScoped<ITrsRuleService, TrsRuleService>();
builder.Services.AddScoped<ITrsReportService, TrsReportService>();

// ماژول انبارگردانی و بارکد
builder.Services.AddScoped<IBcdBarcodeService, BcdBarcodeService>();
builder.Services.AddScoped<IStkSessionService, StkSessionService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IRepairService, RepairService>();
builder.Services.AddScoped<IExpenseService, ExpenseService>();
// ---------- ماژول مدیریت پروژه‌ها ----------
builder.Services.AddScoped<IKarfarmaService, KarfarmaService>();
builder.Services.AddScoped<ITypeFactorService, TypeFactorService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IProjectCartableService, ProjectCartableService>();
builder.Services.AddScoped<IReportWorkService, ReportWorkService>();
// ---------- ماژول اتوماسیون اداری (نامه داخلی + صادره) ----------
builder.Services.AddScoped<ILetterService, LetterService>();
builder.Services.AddScoped<IOutgoingLetterService, OutgoingLetterService>();
builder.Services.AddSingleton<IToastService, ToastService>();
builder.Services.AddSingleton<LayoutState>();
builder.Services.AddSingleton<RealtimeService>(); // هسته‌ی بلادرنگ سراسری (SignalR)

var host = builder.Build();

// تعیین آدرس API در زمان اجرا (پشتیبانی از پیش‌نمایش ابری و localhost)
var js = host.Services.GetRequiredService<Microsoft.JSInterop.IJSRuntime>();
var options = host.Services.GetRequiredService<ApiOptions>();
options.BaseUrl = await js.InvokeAsync<string>("inventoryApiBase", new object[] { "http://localhost:5100" });

// بازیابی نشست ورود از localStorage
await host.Services.GetRequiredService<IAuthState>().InitializeAsync();

await host.RunAsync();
