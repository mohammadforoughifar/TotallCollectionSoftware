using System.Net;
using System.Text;
using Inventory.Client.Extensions;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Pages.Operations;

/// <summary>صورت‌حساب مستقل و قابل چاپ مشتری، بر مبنای همان گردش تاریخ‌دار دفتر فروشگاه.</summary>
public static class CustomerStatementPrint
{
    public static string Build(PartyStatementDetailDto statement, CompanyProfileDto? profile, DateTime? from, DateTime? to)
    {
        var company = string.IsNullOrWhiteSpace(profile?.Name) ? "فروشگاه" : profile!.Name.Trim();
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"fa\" dir=\"rtl\"><head><meta charset=\"utf-8\">")
            .Append("<title>صورت‌حساب مشتری</title><style>").Append(Css).Append("</style></head><body><main>")
            .Append("<header><div><div class=\"company\">").Append(Esc(company))
            .Append("</div><h1>صورت‌حساب مشتری</h1></div><div class=\"period\">از ")
            .Append(from.HasValue ? from.Value.ToFa().FaDigits() : "ابتدا")
            .Append(" تا ").Append(to.HasValue ? to.Value.ToFa().FaDigits() : "آخرین گردش ثبت‌شده")
            .Append("</div></header>");

        sb.Append("<section class=\"party\"><div><span>مشتری</span><strong>").Append(Esc(statement.PartyName))
            .Append("</strong></div>");
        if (!string.IsNullOrWhiteSpace(statement.Mobile))
            sb.Append("<div><span>شماره تماس</span><strong dir=\"ltr\">").Append(Esc(statement.Mobile)).Append("</strong></div>");
        sb.Append("<div><span>مانده اول دوره</span><strong>").Append(Balance(statement.OpeningBalance)).Append("</strong></div>")
            .Append("<div><span>مانده پایان دوره</span><strong>").Append(Balance(statement.ClosingBalance)).Append("</strong></div></section>");

        sb.Append("<section class=\"summary\">");
        Summary(sb, "کالا و خدمات پس از تخفیف و با ارزش افزوده (خالص برگشت دوره)", statement.SaleGoodsTotal);
        Summary(sb, "تخفیف فاکتورهای صادرشده دوره (پیش از برگشت)", statement.SaleIssuedDiscountTotal);
        Summary(sb, "تاخیر تادیه چک (خالص تغییرات و برگشت دوره)", statement.SaleChequeIncomeTotal);
        Summary(sb, "جمع بدهکار گردش دوره", statement.TotalDebit);
        Summary(sb, "جمع بستانکار گردش دوره", statement.TotalCredit);
        sb.Append("</section>");
        sb.Append("<p class=\"note\">تخفیف در مبلغ فاکتور لحاظ شده و دوباره از مانده کسر نمی‌شود. برگشت کالا و تعدیل سود در تاریخ واقعی خود آمده‌اند. تحویل چک، تا زمان وصول، پرداخت محسوب نمی‌شود.</p>");

        sb.Append("<table><thead><tr><th class=\"date\">تاریخ</th><th class=\"number\">شماره سند</th><th>شرح گردش</th><th class=\"amount\">بدهکار</th><th class=\"amount\">بستانکار</th><th class=\"balance\">مانده</th></tr></thead><tbody>");
        foreach (var row in statement.Rows)
        {
            sb.Append("<tr><td class=\"date-cell\">").Append(row.Date.ToFa().FaDigits())
                .Append("</td><td>").Append(Esc(string.IsNullOrWhiteSpace(row.DocumentNumber) ? "—" : row.DocumentNumber))
                .Append("</td><td><strong>").Append(Esc(row.Title)).Append("</strong>");
            if (!string.IsNullOrWhiteSpace(row.Description)) sb.Append("<small>").Append(Esc(row.Description)).Append("</small>");
            if (row.SaleInvoiceDiscount is decimal discount)
                sb.Append("<small>تخفیف فاکتور: ").Append(discount.MoneyExact()).Append(" ریال (در مبلغ سند لحاظ شده)</small>");
            sb.Append("</td><td class=\"amount-cell\">").Append(row.Debit == 0 ? "—" : row.Debit.MoneyExact())
                .Append("</td><td class=\"amount-cell\">").Append(row.Credit == 0 ? "—" : row.Credit.MoneyExact())
                .Append("</td><td class=\"balance-cell\">").Append(Balance(row.Balance)).Append("</td></tr>");
        }
        if (statement.Rows.Count == 0)
            sb.Append("<tr><td colspan=\"6\" class=\"empty\">در این بازه گردشی ثبت نشده است.</td></tr>");
        sb.Append("</tbody></table><footer>همه مبالغ به ریال است. جمع بستانکار می‌تواند شامل دریافت، برگشت کالا و سایر گردش‌های کاهنده بدهی باشد.</footer></main></body></html>");
        return sb.ToString();
    }

    private static void Summary(StringBuilder sb, string label, decimal amount)
        => sb.Append("<div><span>").Append(Esc(label)).Append("</span><strong dir=\"ltr\">")
            .Append(amount.MoneyExact()).Append("</strong></div>");

    private static string Balance(decimal value) => value == 0m ? "۰ — تسویه"
        : Math.Abs(value).MoneyExact() + (value > 0m ? " بدهکار" : " بستانکار");

    private static string Esc(string? value) => WebUtility.HtmlEncode(value ?? "");

    private const string Css = @"@font-face { font-family:'Vazirmatn'; src:url('__FONT_BASE__fonts/Vazirmatn-Regular.woff2') format('woff2'); font-weight:400; }
@font-face { font-family:'Vazirmatn'; src:url('__FONT_BASE__fonts/Vazirmatn-Bold.woff2') format('woff2'); font-weight:700; }
@page { size:A4 portrait; margin:10mm; }
* { box-sizing:border-box; } body { margin:0; color:#1e293b; font:10px 'Vazirmatn',Tahoma,sans-serif; }
main { max-width:190mm; margin:auto; } header { display:flex; justify-content:space-between; align-items:center; gap:15px; padding-bottom:9px; border-bottom:2px solid #4f46a5; }
.company { color:#312e81; font-size:15px; font-weight:700; overflow-wrap:anywhere; } h1 { margin:3px 0 0; font-size:12px; } .period { color:#475569; text-align:left; }
.party { display:grid; grid-template-columns:repeat(4,minmax(0,1fr)); gap:6px; margin:10px 0; } .party div { display:grid; gap:4px; padding:7px; border:1px solid #dfe5f0; border-radius:7px; overflow-wrap:anywhere; } .party span,.summary span { color:#64748b; font-size:9px; }
.summary { display:grid; grid-template-columns:repeat(3,minmax(0,1fr)); gap:6px; } .summary div { display:flex; flex-direction:column; gap:4px; padding:7px; border:1px solid #e2e8f0; border-radius:7px; } .summary strong { font-variant-numeric:tabular-nums; text-align:right; overflow-wrap:anywhere; }
.note { color:#475569; font-size:9px; line-height:1.8; margin:9px 0; }
table { width:100%; table-layout:fixed; border-collapse:collapse; } thead { display:table-header-group; } th { background:#303d68; color:#fff; padding:6px 5px; text-align:right; } td { padding:6px 5px; border-bottom:1px solid #e2e8f0; vertical-align:top; overflow-wrap:anywhere; } tbody tr:nth-child(even) { background:#f8fafc; } td small { display:block; color:#64748b; margin-top:3px; white-space:pre-wrap; } .date { width:12%; } .number { width:13%; } .amount { width:13%; } .balance { width:16%; } .date-cell { direction:ltr; } .amount-cell { direction:ltr; text-align:left; font-variant-numeric:tabular-nums; } .balance-cell { text-align:left; font-variant-numeric:tabular-nums; } .empty { text-align:center; padding:18px; color:#64748b; } footer { margin-top:10px; padding-top:7px; border-top:1px solid #e2e8f0; color:#64748b; font-size:9px; }
@media print { body { -webkit-print-color-adjust:exact; print-color-adjust:exact; } tr,.party,.summary { break-inside:avoid; page-break-inside:avoid; } }
";
}
