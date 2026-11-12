using Inventory.Client.Extensions;
using Inventory.Client.Pages.Orders;
using Inventory.Shared.Dtos;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Inventory.Client.Pages.Catalog;

/// <summary>Complete approved dispatch layout using persisted fields only.</summary>
public static class GoodsIssuesPrint
{
    public static string Build(GoodsIssueDto d, string company, string? paperSize = null, string? logoDataUrl = null)
    {
        var a5 = string.Equals(paperSize, "A5", StringComparison.OrdinalIgnoreCase);
        var items = new StringBuilder();
        for (var i = 0; i < d.Lines.Count; i++)
        {
            var line = d.Lines[i];
            items.Append("<tr><td>").Append((i + 1).Num()).Append("</td><td>").Append(Esc(line.ProductName))
                .Append("</td><td>").Append(line.Quantity.Qty()).Append("</td><td>").Append(Esc(line.Note)).Append("</td></tr>");
        }
        var values = new Dictionary<string, string>
        {
            ["__CSS__"] = Css.Replace("__PAGE_SIZE__", a5 ? "A5" : "A4").Replace("__PAGE_MARGIN__", a5 ? "6mm" : "10mm"),
            ["__PAPER_CLASS__"] = a5 ? "a5" : "", ["__COMPANY__"] = Esc(company), ["__NUMBER__"] = Esc(d.Number),
            ["__DATE__"] = d.Date.ToFa().FaDigits(), ["__PARTY__"] = Esc(d.PartyName),
            ["__COUNT__"] = d.Lines.Count.Num(), ["__QUANTITY__"] = d.TotalQuantity.Qty(),
            ["__ITEMS__"] = items.ToString(), ["__NOTES__"] = Esc(d.Description),
            ["__LOGO__"] = logoDataUrl?.StartsWith("data:image/png;base64,", StringComparison.Ordinal) == true
                ? "<img id=\"invoice-logo\" src=\"" + Esc(logoDataUrl) + "\" alt=\"لوگوی شرکت\">"
                : "<span class=\"letter-logo\" aria-label=\"بدون لوگو\">ب</span>"
        };
        return Regex.Replace(Template, @"__[A-Z_]+__", match => values.TryGetValue(match.Value, out var value) ? value : match.Value);
    }
    private static string Esc(string? value) => WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(value) ? "-" : value.Trim());

    public const string Css = ShopSaleInvoicePrint.Style + "\n.invoice .invoice-header{grid-template-columns:228px minmax(0,1fr) 228px}.invoice.a5 .invoice-header{grid-template-columns:180px minmax(0,1fr) 180px}.items td:last-child{color:#64748b;font-size:9px}";

    public const string Template = """
<!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>بیجک تحویل کالا __NUMBER__</title><style>__CSS__</style></head><body><article class="invoice classic __PAPER_CLASS__" id="invoice" aria-label="بیجک تحویل کالا">
  <header class="invoice-header"><div class="logo-cell">__LOGO__</div><div class="title-cell"><span class="document-kind" id="document-title">بیجک تحویل کالا</span><h2>__COMPANY__</h2></div><dl class="document-meta"><div><dt>شماره بیجک</dt><dd id="invoice-number">__NUMBER__</dd></div><div><dt>تاریخ</dt><dd id="invoice-date">__DATE__</dd></div></dl></header>
  <section class="buyer"><div class="section-tag" id="party-label">گیرندهٔ کالا</div><div class="buyer-details"><div><span>نام / عنوان</span><strong id="printed-customer-name">__PARTY__</strong></div></div></section>
  <section class="items-section"><div class="table-caption"><strong>اقلام بیجک</strong><span><span id="line-count">__COUNT__</span> ردیف · <span id="quantity-total">__QUANTITY__</span> جمع مقدار</span></div><table class="items" aria-label="اقلام بیجک بدون قیمت"><colgroup><col style="width:7%"><col style="width:55%"><col style="width:15%"><col style="width:23%"></colgroup><thead><tr><th class="row-heading" scope="col">ردیف</th><th scope="col">شرح کالا</th><th scope="col">تعداد</th><th scope="col">ملاحظات</th></tr></thead><tbody id="item-rows">__ITEMS__</tbody></table></section>

  <section class="signatures" id="signatures"><div><strong>امضای تحویل‌دهنده</strong><span>نام و امضا</span></div><div><strong>امضای تحویل‌گیرنده</strong><span>نام، مهر و امضا</span></div></section>

  <section class="invoice-notes" id="invoice-notes"><span class="bank-frame-brand">__COMPANY__</span><p id="printed-notes">__NOTES__</p></section>
 </article></body></html>
""";
}
