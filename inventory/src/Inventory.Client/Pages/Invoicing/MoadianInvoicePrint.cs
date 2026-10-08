using System.Net;
using System.Text;
using Inventory.Client.Extensions;
using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Pages.Invoicing;

/// <summary>
/// ساخت «صورتحساب الکترونیکی» چاپی A4 مطابق نمونهٔ رسمی — سند HTML کامل و خودبسنده
/// (doctype + CSS خودبسنده + فونت وزیرمتن) برای نمایش/چاپ در iframe.
/// اقلام بیش از ۱۸ ردیف به صفحات بعدی منتقل می‌شوند (سربرگ جدول در هر صفحه تکرار می‌شود)
/// و جمع‌ها/امضا فقط در صفحهٔ آخر چاپ می‌شود.
/// </summary>
public static class MoadianInvoicePrint
{
    /// <summary>حداکثر ردیف اقلام در هر صفحهٔ A4.</summary>
    public const int RowsPerPage = 18;

    public static string Build(MoadianInvoice inv, CompanyProfileDto? profile, string fontBase = "/")
    {
        var company = string.IsNullOrWhiteSpace(profile?.Name)
            ? (string.IsNullOrWhiteSpace(inv.SellerName) ? "فروغ آریا" : inv.SellerName)
            : profile!.Name.Trim();
        var lines = inv.Lines.OrderBy(l => l.RowNo).ToList();
        var totalPages = Math.Max(1, (lines.Count + RowsPerPage - 1) / RowsPerPage);

        var sb = new StringBuilder(128 * 1024);
        sb.Append("<!doctype html><html lang=\"fa\" dir=\"rtl\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<title>صورتحساب الکترونیکی ").Append(Esc(InvoiceNumber(inv))).Append("</title>");
        sb.Append("<style>").Append(Css.Replace("__FONT_BASE__", fontBase)).Append("</style></head><body>");

        // ---------- متن‌های بالای برگه (از تنظیمات چاپ شرکت) ----------
        foreach (var hl in SplitLines(profile?.PrintHeaderLines))
            sb.Append("<div class=\"pre-head\">").Append(Esc(hl)).Append("</div>");

        // ---------- صفحه‌های اقلام ----------
        for (var page = 0; page < totalPages; page++)
        {
            var chunk = lines.Skip(page * RowsPerPage).Take(RowsPerPage).ToList();
            var firstOnPage = page * RowsPerPage + 1;

            sb.Append("<section class=\"page\">");

            if (page == 0)
            {
                // ---------- سربرگ ----------
                sb.Append("<header class=\"head\">");
                sb.Append("<div class=\"head-grid\">");
                HeadCell(sb, "شماره فاکتور:", InvoiceNumber(inv), bold: true);
                HeadCell(sb, "شماره ثبت در سامانه مالیاتی:", OrDash(inv.TaxId22), ltr: true);
                HeadCell(sb, "شماره سریال ثبت:", OrDash(inv.ReferenceId), ltr: true);
                HeadCell(sb, "شماره ثبت در سامانه دولتی:", OrDash(inv.TrackingId), ltr: true);
                sb.Append("</div>");
                sb.Append("<div class=\"title-row\"><span class=\"issue-date\">تاریخ: ")
                    .Append(Esc(PersianDate.ToShort(inv.Date).FaDigits())).Append("</span>");
                sb.Append("<h1>صورتحساب الکترونیکی</h1></div>");
                sb.Append("</header>");

                // ---------- مشخصات فروشنده ----------
                Party(sb, "مشخصات فروشنده",
                    Name: OrDash(inv.SellerName),
                    Phone: OrDash(profile?.Phone),
                    NationalId: OrDash(inv.EconomicCode ?? inv.TaxId),
                    Postal: "");

                // ---------- مشخصات خریدار ----------
                Party(sb, "مشخصات خریدار",
                    Name: OrDash(inv.BuyerName),
                    Phone: OrDash(inv.BuyerPhone),
                    NationalId: OrDash(inv.BuyerTaxId),
                    Postal: OrDash(inv.BuyerPostalCode));
            }

            // ---------- عنوان جدول ----------
            sb.Append("<div class=\"table-title\">مشخصات کالا یا خدمات درج شده</div>");

            // ---------- جدول اقلام ----------
            sb.Append("<table><thead><tr>");
            sb.Append("<th>ردیف</th><th>شناسه کالا/خدمات</th><th>شرح کالا یا خدمات</th><th>واحد</th>");
            sb.Append("<th>تعداد</th><th>قیمت واحد</th><th>مبلغ واحد</th><th>مبلغ تخفیف</th>");
            sb.Append("<th>مبلغ بعد از تخفیف</th><th>درصد مالیات بر ارزش افزوده</th>");
            sb.Append("<th>ریال مالیات بر ارزش افزوده</th><th>جمع کل کالا یا خدمات</th>");
            sb.Append("</tr></thead><tbody>");
            if (chunk.Count == 0)
            {
                sb.Append("<tr><td colspan=\"12\" class=\"empty\">ردیفی ثبت نشده است.</td></tr>");
            }
            for (var i = 0; i < chunk.Count; i++)
            {
                var l = chunk[i];
                var gross = l.Quantity * l.UnitPrice;
                var taxable = gross - l.Discount;
                sb.Append("<tr>")
                  .Append("<td class=\"c\">").Append(Fa.Digits(firstOnPage + i)).Append("</td>")
                  .Append("<td class=\"ltr\">").Append(Esc(l.SstId)).Append("</td>")
                  .Append("<td>").Append(Esc(l.SstTitle)).Append("</td>")
                  .Append("<td class=\"c\">").Append(Esc(l.UnitCode ?? "")).Append("</td>")
                  .Append("<td class=\"c\">").Append(Esc(Fa.Number(l.Quantity))).Append("</td>")
                  .Append("<td class=\"n\">").Append(Esc(Fa.Money(l.UnitPrice))).Append("</td>")
                  .Append("<td class=\"n\">").Append(Esc(Fa.Money(gross))).Append("</td>")
                  .Append("<td class=\"n\">").Append(Esc(Fa.Money(l.Discount))).Append("</td>")
                  .Append("<td class=\"n\">").Append(Esc(Fa.Money(taxable))).Append("</td>")
                  .Append("<td class=\"c\">").Append(l.VatRate > 0 ? Esc(Fa.Digits(l.VatRate) + "٪") : "—").Append("</td>")
                  .Append("<td class=\"n\">").Append(Esc(Fa.Money(l.VatAmount))).Append("</td>")
                  .Append("<td class=\"n strong\">").Append(Esc(Fa.Money(taxable + l.VatAmount))).Append("</td>")
                  .Append("</tr>");
            }
            sb.Append("</tbody></table>");

            // ---------- پاورقی صفحه (شماره صفحه) ----------
            if (totalPages > 1)
                sb.Append("<div class=\"page-foot\">صفحه ").Append(Fa.Digits(page + 1))
                  .Append(" از ").Append(Fa.Digits(totalPages)).Append("</div>");

            if (page == totalPages - 1)
            {
                // ---------- جمع‌ها (فقط صفحهٔ آخر) ----------
                sb.Append("<section class=\"totals\">");
                sb.Append("<div class=\"totals-row\"><span>نوع پرداخت:</span><b>")
                    .Append(Esc(string.IsNullOrWhiteSpace(inv.PayTypeTitle) ? "—" : inv.PayTypeTitle!)).Append("</b></div>");
                sb.Append("<div class=\"totals-row\"><span>مجموع تخفیف:</span><b>")
                    .Append(Esc(Fa.Money(inv.TotalDiscount))).Append(" ریال</b></div>");
                sb.Append("<div class=\"totals-row grand\"><span>جمع کل:</span><b>")
                    .Append(Esc(Fa.Money(inv.TotalNet))).Append(" ریال</b></div>");
                sb.Append("<div class=\"totals-row words\"><span>مجموع به حروف:</span><b>")
                    .Append(Esc(Fa.Words(inv.TotalNet))).Append(" ریال</b></div>");
                sb.Append("</section>");

                // ---------- پاورقی رسمی ----------
                sb.Append("<footer class=\"foot\">");
                var footerLines = SplitLines(profile?.PrintFooterLines);
                if (footerLines.Count > 0)
                {
                    foreach (var fl in footerLines)
                        sb.Append("<div>").Append(Esc(fl)).Append("</div>");
                }
                else
                {
                    sb.Append("<div>این صورتحساب توسط شرکت <b>").Append(Esc(company))
                      .Append("</b> صادر شده است.</div>");
                }
                sb.Append("</footer>");
            }

            sb.Append("</section>");
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string InvoiceNumber(MoadianInvoice inv)
        => string.IsNullOrWhiteSpace(inv.DocumentNumber) ? inv.Number.ToString() : inv.DocumentNumber!;

    private static void HeadCell(StringBuilder sb, string label, string value, bool bold = false, bool ltr = false)
    {
        var open = bold ? "<strong>" : "<span" + (ltr ? " class=\"num\" dir=\"ltr\"" : "") + ">";
        var close = bold ? "</strong>" : "</span>";
        sb.Append("<div class=\"head-cell\"><span>").Append(Esc(label))
          .Append("</span>").Append(open).Append(Esc(value)).Append(close).Append("</div>");
    }

    private static void Party(StringBuilder sb, string title, string Name, string Phone, string NationalId, string Postal)
    {
        sb.Append("<section class=\"party\"><div class=\"party-title\">").Append(Esc(title)).Append("</div>");
        sb.Append("<div class=\"party-row\"><span>نام:</span><b>").Append(Esc(Name)).Append("</b></div>");
        sb.Append("<div class=\"party-row split\">");
        sb.Append("<span>تلفن (تماس):</span><span>").Append(Esc(Phone)).Append("</span>");
        sb.Append("<span class=\"ms\">شناسه ملی (حقوقی: اقتصادی):</span><span class=\"num\" dir=\"ltr\">").Append(Esc(NationalId)).Append("</span>");
        sb.Append("</div>");
        sb.Append("<div class=\"party-row split\">");
        sb.Append("<span>کد پستی:</span><span class=\"num\" dir=\"ltr\">").Append(Esc(Postal)).Append("</span>");
        sb.Append("<span class=\"ms\">نوع شخص:</span><span>—</span>");
        sb.Append("</div>");
        sb.Append("</section>");
    }

    private static string OrDash(string? v) => string.IsNullOrWhiteSpace(v) ? "" : v!.Trim();

    private static List<string> SplitLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        return text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static string Esc(string? value) => WebUtility.HtmlEncode(value ?? "");

    public const string Css = @"@font-face { font-family:'Vazirmatn'; src:url('__FONT_BASE__fonts/Vazirmatn-Regular.woff2') format('woff2'); font-weight:400; font-display:swap; }
@font-face { font-family:'Vazirmatn'; src:url('__FONT_BASE__fonts/Vazirmatn-Medium.woff2') format('woff2'); font-weight:500; font-display:swap; }
@font-face { font-family:'Vazirmatn'; src:url('__FONT_BASE__fonts/Vazirmatn-Bold.woff2') format('woff2'); font-weight:700; font-display:swap; }
@page { size:A4 portrait; margin:10mm; }
* { box-sizing:border-box; }
body { margin:0; background:#fff; color:#111; font-family:'Vazirmatn',Tahoma,'Segoe UI',sans-serif; font-size:10px; }
.page { page-break-after:always; }
.page:last-child { page-break-after:auto; }
.pre-head { text-align:center; color:#333; font-size:9px; padding:1px 0; }
.head { border-bottom:2px solid #000; padding-bottom:8px; margin-bottom:8px; }
.head-grid { display:grid; grid-template-columns:1fr 1fr; gap:4px 18px; }
.head-cell { display:flex; justify-content:space-between; align-items:baseline; gap:8px; }
.head-cell span { color:#333; font-size:10px; }
.head-cell strong { font-size:12px; font-weight:700; }
.head-cell span.num { font-size:10px; letter-spacing:.2px; }
.title-row { text-align:center; margin-top:6px; }
.title-row h1 { margin:0; font-size:20px; font-weight:800; }
.title-row .issue-date { display:block; font-size:10px; color:#333; margin-bottom:2px; }
.party { border:1px solid #000; margin-bottom:8px; }
.party-title { background:#f2f2f2; font-weight:700; font-size:11px; text-align:center; padding:4px; }
.party-row { display:flex; gap:6px; padding:4px 8px; border-top:1px solid #ddd; font-size:10px; }
.party-row.split { flex-wrap:wrap; }
.party-row.split > span:nth-child(odd) { color:#333; min-width:150px; }
.party-row .ms { margin-right:24px; }
.party-row b { font-weight:600; }
.table-title { text-align:center; font-weight:700; font-size:11px; margin:8px 0 5px; }
table { width:100%; border-collapse:collapse; table-layout:fixed; }
thead th { border:1px solid #000; background:#f2f2f2; padding:4px 3px; font-size:8.5px; font-weight:700; text-align:center; line-height:1.4; }
tbody td { border:1px solid #000; padding:4px 4px; font-size:9px; text-align:center; word-wrap:break-word; line-height:1.5; }
tbody tr:nth-child(even) td { background:#fafafa; }
td.c { text-align:center; }
td.n { text-align:left; direction:rtl; white-space:nowrap; }
td.ltr { direction:ltr; text-align:center; font-size:8.5px; }
td.strong { font-weight:700; }
td.empty { color:#666; padding:10px; }
.page-foot { text-align:center; color:#333; font-size:9px; margin-top:4px; }
.totals { margin-top:10px; border:1px solid #000; }
.totals-row { display:flex; justify-content:space-between; align-items:center; padding:5px 10px; border-top:1px solid #ddd; font-size:10px; }
.totals-row:first-child { border-top:none; }
.totals-row.grand { font-size:12px; }
.totals-row.grand b { font-size:13px; }
.totals-row.words b { font-size:10px; }
.foot { margin-top:12px; text-align:center; font-size:10px; }
.foot b { font-weight:700; }
@media screen { .page { max-width:190mm; margin:0 auto; } }";
}
