using Inventory.Client.Extensions;
using Inventory.Shared.Dtos;
using System.Net;
using System.Text;

namespace Inventory.Client.Pages.Catalog;

/// <summary>Shop-only delivery note. Only persisted fields and derived quantities are printed.</summary>
public static class GoodsIssuesPrint
{
    public static string Build(GoodsIssueDto d, string company, string? paperSize = null, string? logoDataUrl = null)
    {
        var a5 = string.Equals(paperSize, "A5", StringComparison.OrdinalIgnoreCase);
        var companyName = string.IsNullOrWhiteSpace(company) ? "-" : company.Trim();
        var css = Css.Replace("__SIZE__", a5 ? "A5" : "A4")
            .Replace("__MARGIN__", a5 ? "7mm" : "10mm");
        var sb = new StringBuilder("<!doctype html><html lang=\"fa\" dir=\"rtl\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>بیجک تحویل کالا</title><style>");
        sb.Append(css).Append("</style></head><body><main class=\"sheet ").Append(a5 ? "a5" : "a4").Append("\">");
        sb.Append("<header class=\"head\">");
        if (logoDataUrl?.StartsWith("data:image/png;base64,", StringComparison.Ordinal) == true)
            sb.Append("<img class=\"logo\" src=\"").Append(Esc(logoDataUrl)).Append("\" alt=\"لوگوی شرکت\">");
        else sb.Append("<div class=\"mark\" aria-hidden=\"true\">ب</div>");
        sb.Append("<div class=\"title\"><h1>بیجک تحویل کالا</h1><h2>").Append(Esc(companyName)).Append("</h2></div><dl class=\"meta\">");
        sb.Append("<div><dt>شماره بیجک</dt><dd>").Append(Esc(d.Number)).Append("</dd></div><div><dt>تاریخ</dt><dd>").Append(d.Date.ToFa().FaDigits()).Append("</dd></div></dl></header>");
        sb.Append("<section class=\"party\"><h3>گیرندهٔ کالا</h3><div><span>نام / عنوان</span><strong>").Append(Esc(d.PartyName)).Append("</strong></div></section>");
        sb.Append("<div class=\"caption\"><strong>اقلام بیجک</strong><span>").Append(d.Lines.Count.Num()).Append(" ردیف · جمع مقدار: ").Append(d.TotalQuantity.Qty()).Append("</span></div>");
        sb.Append("<table><colgroup><col style=\"width:7%\"><col style=\"width:55%\"><col style=\"width:15%\"><col style=\"width:23%\"></colgroup><thead><tr><th>ردیف</th><th>شرح کالا</th><th>تعداد</th><th>ملاحظات</th></tr></thead><tbody>");
        for (var i = 0; i < d.Lines.Count; i++)
        {
            var line = d.Lines[i];
            sb.Append("<tr><td>").Append((i + 1).Num()).Append("</td><td class=\"name\">").Append(Esc(line.ProductName)).Append("</td><td>").Append(line.Quantity.Qty()).Append("</td><td>").Append(Esc(line.Note)).Append("</td></tr>");
        }
        sb.Append("</tbody></table><section class=\"signatures\"><div><strong>امضای تحویل‌دهنده</strong><span>نام و امضا</span></div><div><strong>امضای تحویل‌گیرنده</strong><span>نام و امضا</span></div></section>");
        sb.Append("<section class=\"notes\"><span class=\"brand\">").Append(Esc(companyName)).Append("</span><p>").Append(Esc(d.Description)).Append("</p></section></main></body></html>");
        return sb.ToString();
    }

    private static string Esc(string? value) => WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(value) ? "-" : value.Trim());

    public const string Css = @"@font-face{font-family:Vazirmatn;src:url('__FONT_BASE__fonts/Vazirmatn-Regular.woff2')}@font-face{font-family:Vazirmatn;src:url('__FONT_BASE__fonts/Vazirmatn-Bold.woff2');font-weight:700}
@page{size:__SIZE__ portrait;margin:__MARGIN__}*{box-sizing:border-box}body{margin:0;background:white;color:#253247;font:11px Vazirmatn,Tahoma,sans-serif}.sheet{max-width:190mm;margin:auto}.sheet.a5{max-width:134mm;font-size:9px}
.head{display:grid;grid-template-columns:1fr 1.3fr 1fr;gap:10px;align-items:center;border-bottom:2px solid #303b4b;padding-bottom:16px}.logo{width:96px;height:64px;object-fit:contain;justify-self:start}.a5 .logo{width:70px;height:50px}.mark{justify-self:start;border:1px solid #b5bdc8;padding:10px 16px;font-size:22px;font-weight:700}.title{text-align:center}.title h1{font-size:17px;margin:0 0 6px}.title h2{font-size:14px;margin:0;overflow-wrap:anywhere}.meta{margin:0;font-size:9px}.meta div{display:flex;justify-content:flex-end;gap:8px;margin:6px 0}.meta dt{white-space:nowrap;color:#64748b}.meta dd{margin:0;overflow-wrap:anywhere}
.party{margin-top:16px;border:1px solid #b5bdc8;border-radius:4px;overflow:hidden}.party h3{margin:0;padding:6px 10px;font-size:10px;background:#f0f2f5;border-bottom:1px solid #b5bdc8}.party>div{display:flex;gap:10px;padding:12px}.party span{color:#64748b}.party strong{font-weight:400;overflow-wrap:anywhere}.caption{display:flex;justify-content:space-between;gap:10px;margin:18px 0 8px;font-size:10px}.caption span{color:#64748b}
table{width:100%;border-collapse:collapse;table-layout:fixed}th,td{border:1px solid #a5afbc;text-align:center;padding:9px 7px;overflow-wrap:anywhere}thead{display:table-header-group}th{background:#f0f2f5;font-size:10px}th:first-child{white-space:nowrap}tbody tr:nth-child(even){background:#fafbfc}.name{text-align:right;font-weight:700}
.signatures{display:grid;grid-template-columns:1fr 1fr;gap:28px;margin:22px 0 16px;break-inside:avoid}.signatures>div{border:1px dashed #a5afbc;height:86px;display:flex;flex-direction:column;align-items:center;justify-content:space-between;padding:10px;font-size:10px}.signatures span{color:#94a3b8;font-size:9px}.notes{position:relative;border-top:1px solid #b5bdc8;margin-top:20px;padding-top:14px;break-inside:avoid}.brand{position:absolute;top:0;left:50%;transform:translate(-50%,-50%);background:white;padding:0 10px;font-size:8px;max-width:80%;text-align:center}.notes p{white-space:pre-wrap;overflow-wrap:anywhere;color:#64748b;line-height:1.9;margin:0}
.a5 .title h1{font-size:14px}.a5 .title h2{font-size:11px}.a5 .meta{font-size:7px}.a5 th,.a5 td{padding:6px 4px;font-size:8px}.a5 .signatures{gap:20px;margin:14px 0}.a5 .signatures>div{height:60px;font-size:8px}.a5 .signatures span{font-size:7px}
@media print{body{print-color-adjust:exact;-webkit-print-color-adjust:exact}tr,.head,.party{break-inside:avoid}.sheet{max-width:100%}}";
}
