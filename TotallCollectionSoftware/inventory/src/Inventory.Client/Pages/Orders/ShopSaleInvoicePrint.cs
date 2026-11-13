using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Inventory.Client.Extensions;
using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Pages.Orders;

/// <summary>Approved shop sales layout. Read-only invoice data; not used by ERP/office printing.</summary>
public static class ShopSaleInvoicePrint
{
    public static string Build(Order order, CompanyProfileDto? profile, string? paperSize = null, ShopPrintSettingsDto? settings = null)
    {
        return BuildDocument(order, profile, paperSize, settings, false);
    }
    internal static string BuildPurchase(Order order, CompanyProfileDto? profile, string? paperSize)
        => BuildDocument(order, profile, paperSize, null, true);

    private static string BuildDocument(Order order, CompanyProfileDto? profile, string? paperSize, ShopPrintSettingsDto? settings, bool purchase)
    {
        if (purchase ? !order.IsPurchase : !order.IsSale) throw new ArgumentException("نوع سند با قالب چاپ مطابقت ندارد.", nameof(order));
        var a5 = string.Equals(paperSize, "A5", StringComparison.OrdinalIgnoreCase);
        var lines = order.Lines.OrderBy(l => l.Id).ToList();
        var company = Text(profile?.Name);
        var items = new StringBuilder();
        foreach (var (line, index) in lines.Select((line, index) => (line, index)))
        {
            items.Append("<tr><td>").Append((index + 1).Num()).Append("</td><td>").Append(Text(line.ProductName));
            if (line.IsService) items.Append("<small class=\"item-note\">خدمت</small>");
            if (line.ReturnedQuantity > 0) items.Append("<small class=\"item-note\">مقدار برگشتی: ").Append(line.ReturnedQuantity.Qty()).Append("</small>");
            items.Append("</td><td>").Append(line.Quantity.Qty()).Append("</td>");
            foreach (var (amount, isDiscount) in new[] { (line.Price, false), (line.Gross, false), (line.DiscountAmount, true), (line.AfterDiscount, false) })
            {
                var formatted = isDiscount && amount == 0 ? "-" : amount.MoneyExact();
                items.Append("<td class=\"money").Append(formatted.Length > 16 ? " long-number" : "").Append("\">").Append(formatted).Append("</td>");
            }
            items.Append("</tr>");
        }
        var totals = new StringBuilder();
        Total(totals, purchase ? "جمع بهای خرید" : "جمع بهای کالاها", lines.Sum(l => l.Gross), "gross");
        Total(totals, "جمع تخفیف", lines.Sum(l => l.DiscountAmount), "discount");
        Total(totals, "پس از تخفیف", lines.Sum(l => l.AfterDiscount), "net");
        Total(totals, "مالیات و عوارض", lines.Sum(l => l.VatAmount), "vat");
        if (order.ReturnedGoodsAmount > 0) Total(totals, "برگشت کالا و مالیات", -order.ReturnedGoodsAmount, "returned");
        if (order.ChequeIncomeAmount != 0) Total(totals, "تاخیر تادیه چک (بدون مالیات)", order.ChequeIncomeAmount, "cheque-charge");
        Total(totals, purchase ? "مبلغ نهایی خرید · ریال" : "مبلغ نهایی فاکتور · ریال", order.TotalAmount, "payable", true);
        var banks = new StringBuilder();
        foreach (var bank in ShopPrintBankRules.Validate(settings ?? new()).Banks)
        {
            banks.Append("<div class=\"printed-bank-entry\">");
            Bank(banks, "شماره کارت", bank.Card); Bank(banks, "شماره شبا", bank.Iban);
            banks.Append("</div>");
        }
        var notes = $"{(purchase ? "فاکتور خرید" : "فاکتور فروش")} {order.Number} مورخ {order.Date.ToFa()} {(purchase ? "از تأمین‌کننده" : "برای")} «{Plain(order.PartyName)}»؛ {lines.Count.Num()} ردیف، مجموع مقدار {lines.Sum(l => l.Quantity).Qty()} و مبلغ نهایی {order.TotalAmount.MoneyExact()} ریال.";
        if (!string.IsNullOrWhiteSpace(order.Description)) notes += "\n" + order.Description.Trim();
        var values = new Dictionary<string, string>
        {
            ["__CSS__"] = Style.Replace("__PAGE_SIZE__", a5 ? "A5" : "A4").Replace("__PAGE_MARGIN__", a5 ? "6mm" : "10mm"),
            ["__PAPER_CLASS__"] = (a5 ? "a5" : "") + (purchase ? " purchase-document" : ""),
            ["__TITLE__"] = purchase ? "فاکتور خرید" : "فاکتور فروش",
            ["__NUMBER_LABEL__"] = purchase ? "شماره سند خرید" : "شماره فاکتور",
            // No supplier invoice number exists in Order DTO. Do not invent a value.
            ["__SUPPLIER_META__"] = purchase ? "<div><dt>شماره فاکتور تأمین‌کننده</dt><dd>-</dd></div>" : "",
            ["__PARTY_LABEL__"] = purchase ? "فروشنده / تأمین‌کننده" : "اطلاعات خریدار",
            ["__ITEM_CAPTION__"] = purchase ? "اقلام خرید از تأمین‌کننده" : "شرح کالاها و خدمات",
            ["__UNIT_LABEL__"] = purchase ? "بهای واحد خرید" : "فی واحد",
            ["__SELLER_SECTION__"] = purchase ? "" : SellerSection(profile, banks.ToString()),
            ["__LOGO__"] = profile?.LogoDataUrl?.StartsWith("data:image/png;base64,", StringComparison.Ordinal) == true
                ? "<img id=\"invoice-logo\" src=\"" + Text(profile.LogoDataUrl) + "\" alt=\"لوگوی شرکت\">"
                : "<span class=\"letter-logo\" aria-label=\"بدون لوگو\">" + (purchase ? "خ" : "ف") + "</span>",
            ["__COMPANY__"] = company, ["__NUMBER__"] = Text(order.Number), ["__DATE__"] = order.Date.ToFa().FaDigits(),
            ["__PARTY__"] = Text(order.PartyName), ["__PHONE__"] = Text(order.PartyPhone),
            ["__TAX_ID__"] = Text(order.PartyTaxId), ["__ADDRESS__"] = Text(order.PartyAddress),
            ["__COUNT__"] = lines.Count.Num(), ["__QUANTITY__"] = lines.Sum(l => l.Quantity).Qty(),
            ["__ITEMS__"] = items.ToString(), ["__TOTALS__"] = totals.ToString(), ["__BANKS__"] = banks.ToString(),
            ["__COMPANY_PHONE__"] = Text(profile?.Phone), ["__COMPANY_ADDRESS__"] = Text(profile?.Address),
            ["__NOTES__"] = Text(notes), ["__PAYMENTS__"] = Payments(order, purchase), ["__SCHEDULE__"] = Schedule(order, purchase),
            ["__CANCELLED__"] = order.IsCancelled ? "<div class=\"cancelled\">این فاکتور ابطال شده است</div>" : "",
            ["__SIGNATURES__"] = purchase
                ? "<section class=\"signatures\" id=\"signatures\"><div><strong>امضای مسئول خرید</strong><span>نام و امضا</span></div><div><strong>امضا و مهر تأمین‌کننده</strong><span>نام، مهر و امضا</span></div></section>"
                : "<section class=\"signatures\" id=\"signatures\"><div><strong>امضای خریدار</strong><span>نام و امضا</span></div><div><strong>امضا و مهر فروشنده</strong><span>نام، مهر و امضا</span></div></section>"
        };
        // Single-pass substitution: invoice text that resembles a token stays literal.
        return Regex.Replace(Template, @"__[A-Z_]+__", match => values.TryGetValue(match.Value, out var value) ? value : match.Value);
    }
    private static string SellerSection(CompanyProfileDto? profile, string banks)
        => "<section class=\"bank-details\" id=\"bank-details\"><div class=\"store-contact\"><span>تلفن: <bdi>" + Text(profile?.Phone) + "</bdi></span><span>نشانی: " + Text(profile?.Address) + "</span></div><div id=\"printed-banks\" class=\"bank-list\">" + banks + "</div></section>";

    private static string Plain(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
    private static string Text(string? value) => WebUtility.HtmlEncode(Plain(value));
    private static void Total(StringBuilder sb, string label, decimal amount, string id, bool final = false)
        => sb.Append(final ? "<div class=\"payable\">" : "<div>").Append("<dt>").Append(Text(label)).Append("</dt><dd id=\"").Append(id).Append("\">").Append(amount.MoneyExact()).Append("</dd></div>");
    private static void Bank(StringBuilder sb, string label, string number)
    {
        if (number.Length == 0) return;
        var grouped = string.Join(" ", Enumerable.Range(0, (number.Length + 3) / 4).Select(i => number.Substring(i * 4, Math.Min(4, number.Length - i * 4))));
        sb.Append("<div><span>").Append(Text(label)).Append("</span><bdi>").Append(Text(grouped)).Append("</bdi></div>");
    }
    private static string Payments(Order order, bool purchase)
    {
        // These are issuance components, NOT current collected balances. SettledAmount includes
        // later receipts and must never be presented as the original cash part.
        var cash = order.CashAmount;
        var cheque = order.Cheques.Sum(c => c.Amount);
        var installments = order.Installments.Sum(i => i.Amount);
        var originalAmount = order.TotalAmount + order.ReturnedGoodsAmount;
        var credit = Math.Max(0, originalAmount - cash - cheque - installments);
        var parts = new List<(string Label, decimal Amount, string Detail)>();
        if (cash > 0) parts.Add(("نقدی", cash, ""));
        if (cheque > 0) parts.Add(("چکی", cheque, "وضعیت هر چک در جدول زیر"));
        if (installments > 0) parts.Add(("اقساطی", installments, ""));
        if (credit > 0) parts.Add((order.PaymentMethod == PaymentMethod.Cash && cash == 0 ? "مبلغ سند نقدی" : (purchase ? "مانده بدهی هنگام ثبت" : "نسیه هنگام ثبت"), credit,
            order.DueDate.HasValue ? "موعد: " + order.DueDate.Value.ToFa().FaDigits() : ""));
        if (parts.Count == 0) return "";
        var sb = new StringBuilder("<section class=\"settlement-details\" aria-label=\"جزئیات تسویه\"><h3 id=\"payment-method\">تسویهٔ ");
        sb.Append(Text(PaymentMix.Fa(order.PaymentMethod)));
        if (parts.Count > 1) sb.Append(" — ").Append(Text(string.Join("، ", parts.Select(p => p.Label is "نسیه هنگام ثبت" or "مانده بدهی هنگام ثبت" ? "نسیه" : p.Label))));
        sb.Append("</h3>");
        if (order.ReturnedGoodsAmount > 0) sb.Append("<p class=\"hint\">ترکیب تسویه هنگام ثبت؛ برگشت‌ها در جمع نهایی لحاظ شده‌اند.</p>");
        sb.Append("<div id=\"settlement-breakdown\" style=\"--parts:").Append(Math.Max(1, parts.Count)).Append("\">");
        foreach (var part in parts)
        {
            sb.Append("<div><span>").Append(Text(part.Label)).Append("</span><strong>").Append(part.Amount.MoneyExact()).Append(" ریال</strong>");
            if (part.Detail.Length > 0) sb.Append("<small>").Append(Text(part.Detail)).Append("</small>");
            sb.Append("</div>");
        }
        return sb.Append("</div></section>").ToString();
    }
    private static string Status(ChequeDto cheque, bool purchase) => cheque.Status switch
    {
        ChequeStatus.Cleared => purchase ? "پرداخت‌شده" : "وصول‌شده", ChequeStatus.Bounced => "برگشت‌خورده", ChequeStatus.Cancelled => "ابطال‌شده",
        ChequeStatus.Endorsed => "واگذارشده", ChequeStatus.InCollection => purchase ? "در جریان پرداخت" : "در جریان وصول",
        _ => cheque.IsCleared ? (purchase ? "پرداخت‌شده" : "وصول‌شده") : (purchase ? "در انتظار پرداخت" : "در انتظار وصول")
    };
    private static string Schedule(Order order, bool purchase)
    {
        var sb = new StringBuilder();
        if (order.Cheques.Count > 0)
        {
            sb.Append("<section class=\"cheque-details\" id=\"cheque-details\"><div class=\"cheque-caption\">").Append(purchase ? "چک‌های پرداختی به تأمین‌کننده" : "مشخصات چک‌های ثبت‌شده").Append("</div><table class=\"cheque-table\"><thead><tr><th>بانک</th><th>شماره چک</th><th>سررسید</th><th>مبلغ · ریال</th><th>وضعیت</th></tr></thead><tbody>");
            foreach (var cheque in order.Cheques.OrderBy(c => c.DueDate))
                sb.Append("<tr><td>").Append(Text(cheque.BankName)).Append("</td><td><bdi>").Append(Text(cheque.Number)).Append("</bdi></td><td>").Append(cheque.DueDate == default ? "-" : cheque.DueDate.ToFa().FaDigits()).Append("</td><td>").Append(cheque.Amount.MoneyExact()).Append("</td><td>").Append(Status(cheque, purchase)).Append("</td></tr>");
            sb.Append("</tbody></table></section>");
        }
        if (order.Installments.Count > 0)
        {
            sb.Append("<section class=\"installment-details\"><h3>اقساط ثبت‌شده</h3><table class=\"cheque-table\"><thead><tr><th>قسط</th><th>سررسید</th><th>مبلغ · ریال</th><th>وضعیت</th></tr></thead><tbody>");
            foreach (var installment in order.Installments.OrderBy(i => i.No))
                sb.Append("<tr><td>").Append(installment.No.Num()).Append("</td><td>").Append(installment.DueDate == default ? "-" : installment.DueDate.ToFa().FaDigits()).Append("</td><td>").Append(installment.Amount.MoneyExact()).Append("</td><td>").Append(installment.IsPaid ? "پرداخت‌شده" : "در انتظار").Append("</td></tr>");
            sb.Append("</tbody></table></section>");
        }
        return sb.ToString();
    }

    public const string Template = """
<!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>__TITLE__ __NUMBER__</title><style>__CSS__</style></head><body><article class="invoice classic __PAPER_CLASS__" id="invoice" aria-label="__TITLE__">
  <header class="invoice-header"><div class="logo-cell">__LOGO__</div><div class="title-cell"><span class="document-kind" id="document-title">__TITLE__</span><h2>__COMPANY__</h2></div><dl class="document-meta"><div><dt>__NUMBER_LABEL__</dt><dd id="invoice-number">__NUMBER__</dd></div>__SUPPLIER_META__<div><dt>تاریخ</dt><dd id="invoice-date">__DATE__</dd></div></dl></header>
  __CANCELLED__
  <section class="buyer"><div class="section-tag" id="party-label">__PARTY_LABEL__</div><div class="buyer-details"><div><span>نام / عنوان</span><strong id="printed-customer-name">__PARTY__</strong></div><div><span>تلفن</span><strong id="printed-customer-phone">__PHONE__</strong></div><div><span>شناسه / کد ملی</span><strong id="printed-customer-id">__TAX_ID__</strong></div><div class="buyer-address"><span>نشانی</span><strong id="printed-customer-address">__ADDRESS__</strong></div></div></section>
  <section class="items-section"><div class="table-caption"><strong>__ITEM_CAPTION__</strong><span><span id="line-count">__COUNT__</span> ردیف · مجموع مقدار: <span id="quantity-total">__QUANTITY__</span> · مبالغ به ریال</span></div><table class="items proposal-items detailed-items" aria-label="اقلام فاکتور با تفکیک قیمت"><colgroup><col style="width:5%"><col style="width:32%"><col style="width:7%"><col style="width:14%"><col style="width:14%"><col style="width:14%"><col style="width:14%"></colgroup><thead><tr><th class="row-heading" scope="col">ردیف</th><th scope="col">شرح کالا / خدمت</th><th scope="col">تعداد</th><th scope="col">__UNIT_LABEL__</th><th scope="col">مبلغ اولیه</th><th scope="col">تخفیف</th><th scope="col">پس از تخفیف</th></tr></thead><tbody id="item-rows">__ITEMS__</tbody></table></section>
  <section class="settlement"><dl class="totals">__TOTALS__</dl></section>
  __PAYMENTS__
  __SCHEDULE__
  __SIGNATURES__
  __SELLER_SECTION__
  <section class="invoice-notes" id="invoice-notes"><span class="bank-frame-brand">__COMPANY__</span><p id="printed-notes">__NOTES__</p></section>
 </article></body></html>
""";

    // Frozen approved preview stylesheet, embedded so print has no application CSS dependency.
    public const string Style = """
@font-face{font-family:Vazir;src:url(__FONT_BASE__fonts/Vazirmatn-Regular.woff2)}@font-face{font-family:Vazir;src:url(__FONT_BASE__fonts/Vazirmatn-Bold.woff2);font-weight:700}*{box-sizing:border-box}body{margin:0;background:#eef1f5;color:#253247;font-family:Vazir,sans-serif;font-size:13px}button,input,select,textarea{font:inherit}button{cursor:pointer}button:focus-visible,input:focus-visible,select:focus-visible,textarea:focus-visible{outline:2px solid #4f46e5;outline-offset:3px}button{border:1px solid #dbe1ea;background:white;color:#475569;border-radius:8px;padding:8px 14px}.primary{background:#4338ca;color:white;border-color:#4338ca}.app-header{height:82px;padding:16px 30px;display:flex;align-items:center;justify-content:space-between;background:white;border-bottom:1px solid #e2e8f0}.app-brand{display:flex;gap:12px;align-items:center}.app-brand strong{font-size:16px}.app-brand small{display:block;color:#94a3b8;margin-top:4px;font-size:11px}.app-mark{background:#eef2ff;border-radius:12px;padding:12px;color:#4f46e5;font-weight:700;direction:ltr}.scope-badge{background:#ecfdf5;color:#047857;border:1px solid #d1fae5;padding:6px 12px;border-radius:20px;font-size:11px}.preview-banner{margin:20px 28px 0;padding:12px 16px;background:#fffbeb;border:1px solid #fde9b4;border-radius:10px;color:#92400e;display:flex;gap:14px;flex-wrap:wrap;font-size:12px}.workspace{max-width:1600px;margin:auto;padding:24px 28px;display:grid;grid-template-columns:320px minmax(0,1fr);gap:28px;align-items:start}.settings{background:white;border:1px solid #e2e8f0;border-radius:14px;overflow:hidden}.panel-heading{padding:24px 20px;background:#fafbfe;border-bottom:1px solid #edf0f5}.eyebrow{font-size:10px;color:#6366f1}.panel-heading h1{font-size:20px;margin:8px 0}.panel-heading p{font-size:12px;color:#94a3b8;margin:0}fieldset{border:0;border-bottom:1px solid #edf0f5;margin:0;padding:20px}legend{float:right;width:100%;font-weight:700;margin-bottom:16px;font-size:12px}fieldset>label,.two-fields>label{display:block;font-size:11px;color:#64748b;margin-bottom:12px}input:not([type=radio]):not([type=checkbox]),textarea,select{display:block;width:100%;border:1px solid #dbe1ea;border-radius:7px;background:white;padding:8px 10px;color:#334155;margin-top:6px;font-size:12px}textarea{resize:vertical;line-height:1.9}.template-options{clear:both;display:grid;gap:8px;margin-bottom:16px}.template-options label{display:flex;gap:8px;padding:11px;border:1px solid #e2e8f0;border-radius:9px;cursor:pointer}.template-options label:has(input:checked){border-color:#818cf8;background:#f5f5ff}.template-options b{display:block;font-size:12px}.template-options small{font-size:10px;color:#94a3b8}.two-fields{display:grid;grid-template-columns:1fr 1.5fr;gap:12px}.source-note,.hint{font-size:10px;color:#94a3b8;line-height:1.9;clear:both}.source-note{background:#f8fafc;padding:10px;border-radius:7px}.identity{display:flex;gap:10px;align-items:center;margin:15px 0}.identity img{width:64px;height:44px;object-fit:contain}.identity strong{font-size:10px}.identity small{display:block;color:#94a3b8;font-size:9px}label.check{display:flex;gap:8px;align-items:center;color:#475569;font-size:12px;margin-bottom:10px;clear:both}input[type=checkbox],input[type=radio]{accent-color:#4f46e5}.scope-note{padding:20px;background:#f8fafc;font-size:11px}.scope-note p{font-size:10px;color:#94a3b8;line-height:1.9}.preview-panel{min-width:0}.preview-toolbar{display:flex;justify-content:space-between;align-items:center;gap:12px;margin-bottom:18px}.preview-toolbar strong{font-size:13px}.preview-toolbar small{display:block;font-size:11px;color:#94a3b8;direction:ltr;text-align:right;margin-top:4px}.toolbar-actions{display:flex;gap:8px}.paper-viewport{overflow-x:auto;padding:8px 8px 20px;scrollbar-width:thin}.invoice{--line:#b5bdc8;--ink:#252c36;--accent:#303b4b;width:794px;min-height:1123px;padding:36px 36px 24px;margin:auto;background:white;color:var(--ink);box-shadow:0 8px 30px #1e293b12;font-size:11px}.invoice-header{display:grid;grid-template-columns:110px 1fr 150px;align-items:center;gap:14px;padding-bottom:16px;border-bottom:2px solid var(--accent)}.logo-cell img{display:block;width:106px;height:74px;object-fit:contain}.title-cell{text-align:center}.document-kind{display:block;font-size:17px;font-weight:700;margin-bottom:5px}.title-cell h2{font-size:15px;margin:0 0 4px}.title-cell p{font-size:9px;color:#7b8490;margin:0}.document-meta{margin:0;font-size:10px}.document-meta div{display:flex;justify-content:space-between;gap:6px;margin:5px 0}.document-meta dt{font-weight:400;color:#7b8490}.document-meta dd{margin:0;font-weight:700;white-space:nowrap}.store-contact{display:flex;justify-content:space-between;gap:10px;font-size:9px;padding:9px 0;color:#66717f;border-bottom:1px solid #e1e5ea}.buyer{margin-top:16px;border:1px solid var(--line);border-radius:5px;overflow:hidden}.section-tag{font-size:10px;font-weight:700;color:var(--accent);padding:6px 10px;background:#f3f4f6;border-bottom:1px solid var(--line)}.buyer-details{padding:10px 12px;display:grid;grid-template-columns:1.5fr 1fr 1fr;gap:9px 18px}.buyer-details div{display:flex;gap:8px;align-items:baseline}.buyer-details span{font-size:9px;color:#7b8490;white-space:nowrap}.buyer-details strong{font-size:10px;font-weight:400}.buyer-details .buyer-address{grid-column:1/-1}.table-caption{display:flex;justify-content:space-between;margin:18px 0 8px;font-size:10px}.table-caption span{font-size:9px;color:#8d95a1}.items{width:100%;border-collapse:collapse;table-layout:fixed;font-size:10px}.items .row-col{width:30px}.items .qty-col{width:48px}.items .unit-price-col{width:94px}.items .total-col{width:107px}.items .remarks-col{width:95px}.items th,.items td{border:1px solid var(--line);padding:9px 7px;text-align:center;overflow-wrap:anywhere}.items th{background:#f0f2f5;font-weight:700}.items th small{font-size:8px;font-weight:400;color:#6b7280}.items td:nth-child(2){text-align:right}.items td:nth-child(6){color:#7b8490;font-size:9px}.items tbody tr:nth-child(even){background:#fafbfc}.items td strong{font-weight:400}.items .money{white-space:nowrap;font-variant-numeric:tabular-nums;overflow-wrap:normal}.settlement{display:grid;grid-template-columns:1fr 280px;border:1px solid var(--line);border-top:0;break-inside:avoid}.invoice-info{padding:14px;border-left:1px solid var(--line)}h3{font-size:10px;margin:0 0 8px}.invoice-info dl{font-size:9px;margin:0}.invoice-info dl div{display:flex;gap:8px;margin:7px 0}.invoice-info dt{color:#7b8490}.invoice-info dd{margin:0}.sample-warning{font-size:9px;color:#9ca3af;margin:16px 0 0}.totals{margin:0}.totals>div{padding:7px 12px;display:flex;align-items:center;justify-content:space-between;gap:6px;border-bottom:1px solid #e3e6eb}.totals>div:last-child{border-bottom:0}.totals dt{font-size:10px;font-weight:400}.totals dt small{font-size:8px}.totals dd{margin:0;white-space:nowrap;font-variant-numeric:tabular-nums;font-size:11px}.totals .payable{background:#f0f2f5;padding:11px 12px}.totals .payable dt,.totals .payable dd{font-weight:700}.totals .payable dd{font-size:14px}.signatures{display:grid;grid-template-columns:1fr 1fr;gap:28px;padding:22px 0 16px;break-inside:avoid}.signatures>div{border:1px dashed #c3cad4;height:86px;border-radius:4px;display:flex;flex-direction:column;align-items:center;justify-content:space-between;padding:10px;font-size:10px}.signatures span{font-size:9px;color:#a1a8b3}.bank-details{border:1px solid var(--line);border-radius:4px;overflow:hidden;margin-top:5px;break-inside:avoid}.bank-owner{display:flex;align-items:center;gap:6px;padding:9px 12px 3px;font-size:9px}.bank-owner strong{font-weight:400}.bank-owner small{font-size:8px;color:#9ca3af;margin-right:auto}.bank-numbers{display:grid;grid-template-columns:1.3fr 1fr;gap:16px;padding:5px 12px 12px}.bank-numbers div{display:flex;flex-wrap:wrap;align-items:center;gap:6px}.bank-numbers span{font-size:9px;color:#7b8490}.bank-numbers bdi{font-size:10px;letter-spacing:.3px;font-family:Vazir,sans-serif;white-space:nowrap}.invoice-notes{border-top:1px solid #dfe3e9;margin-top:18px;padding-top:12px;break-inside:avoid}.invoice-notes p{white-space:pre-line;font-size:9px;line-height:1.9;margin:0;color:#687384}.invoice-footer{display:flex;justify-content:space-between;gap:12px;margin-top:24px;padding-top:10px;border-top:1px solid #edf0f4;font-size:8px;color:#9ca3af}.preview-footnote{font-size:11px;color:#94a3b8;line-height:1.9;max-width:794px;margin:12px auto}.modern{--line:#d8e1ed;--accent:#334aa2}.modern .invoice-header{border-color:#4c63bc}.modern .document-kind{color:#334aa2}.modern .buyer,.modern .bank-details{border-radius:10px}.modern .section-tag{background:#f1f4fc}.modern .items th{background:#edf2fc;color:#334aa2}.modern .items td,.modern .items th{border-left:0;border-right:0}.modern .items tbody tr:nth-child(even){background:#f8faff}.modern .settlement{border:0;border-bottom:1px solid var(--line)}.modern .invoice-info{border-left:0}.modern .totals{border:1px solid var(--line);border-radius:0 0 9px 9px;overflow:hidden}.modern .payable{background:#edf2fc;color:#273d8f}.no-line-notes .remarks,.no-line-notes .remarks-col{display:none}.no-line-notes .items .remarks-col{width:0}.invoice.a5{width:560px;min-height:794px;padding:25px 24px 20px;font-size:9px}.a5 .invoice-header{grid-template-columns:72px 1fr 115px;gap:8px;padding-bottom:10px}.a5 .logo-cell img{width:70px;height:50px}.a5 .document-kind{font-size:14px}.a5 .title-cell h2{font-size:11px}.a5 .title-cell p{font-size:7px}.a5 .document-meta{font-size:8px}.a5 .store-contact{font-size:7px;gap:7px;padding:6px 0}.a5 .buyer{margin-top:10px}.a5 .buyer-details{gap:6px;padding:7px}.a5 .buyer-details strong,.a5 .buyer-details span{font-size:8px}.a5 .table-caption{margin:12px 0 6px}.a5 .items{font-size:8px}.a5 .items th,.a5 .items td{padding:6px 4px}.a5 .items th small{font-size:6px}.a5 .items .row-col{width:23px}.a5 .items .qty-col{width:30px}.a5 .items .unit-price-col{width:66px}.a5 .items .total-col{width:75px}.a5 .items .remarks-col{width:61px}.a5 .items td:nth-child(6){font-size:7px}.a5 .settlement{grid-template-columns:1fr 210px}.a5 .totals dt{font-size:8px}.a5 .totals dd{font-size:9px}.a5 .totals>div{padding:5px 8px}.a5 .totals .payable dd{font-size:11px}.a5 .invoice-info{padding:9px}.a5 .invoice-info dl{font-size:8px}.a5 .signatures{padding:12px 0 10px;gap:20px}.a5 .signatures>div{height:60px;font-size:9px;padding:7px}.a5 .bank-numbers{grid-template-columns:1fr;gap:4px;padding:4px 9px 8px}.a5 .bank-numbers bdi{font-size:9px}.a5 .bank-owner{flex-wrap:wrap;padding:6px 9px 2px}.a5 .bank-owner small{margin:0}.a5 .section-tag{font-size:9px;padding:4px 9px}.a5 .invoice-notes{margin-top:12px;padding-top:8px}.a5 .invoice-notes p{font-size:8px}.a5 .invoice-footer{margin-top:14px;font-size:7px}[hidden]{display:none!important}@media(max-width:1200px){.workspace{grid-template-columns:280px minmax(0,1fr);gap:16px;padding:20px 16px}.paper-viewport{padding:4px 0 16px}.toolbar-actions button{padding:7px 9px;font-size:11px}}@media(max-width:800px){.app-header{padding:14px 16px;height:auto;gap:12px}.app-brand strong{font-size:13px}.scope-badge{font-size:9px;white-space:nowrap}.preview-banner{margin:14px 12px 0;font-size:11px;gap:4px}.workspace{display:flex;flex-direction:column;padding:16px 12px}.settings,.preview-panel{width:100%}.settings{order:2}.preview-panel{order:1}.paper-viewport{overflow:hidden}.preview-toolbar{align-items:center}.preview-footnote{font-size:10px}}
@media print{html,body{background:white;margin:0;padding:0;color:black}.app-header,.preview-banner,.settings,.preview-toolbar,.preview-footnote{display:none!important}.workspace,.preview-panel,.paper-viewport{display:block;margin:0;padding:0;overflow:visible!important;width:auto!important;height:auto!important}.invoice,.invoice.a5{width:100%!important;min-height:0;margin:0;padding:0;transform:none!important;box-shadow:none;zoom:1!important}.invoice{print-color-adjust:exact;-webkit-print-color-adjust:exact}.items thead{display:table-header-group}.items tr,.invoice-header,.buyer,.settlement,.signatures,.bank-details,.invoice-notes{break-inside:avoid}.invoice-footer{position:static}.preview-panel{order:0}}
/* Explicit line-height avoids font-metric inflation in Persian print output. */
.invoice{line-height:1.65}
.a5{line-height:1.55}
@media print{
 .invoice .signatures{padding:16px 0 12px}
 .invoice .signatures>div{height:74px}
 .invoice.a5 .signatures{padding:10px 0 8px}
 .invoice.a5 .signatures>div{height:52px}
 .invoice.a5 .invoice-footer{margin-top:10px}
 .invoice.a5 .bank-owner{font-size:8px}
 .invoice.a5 .bank-owner small{font-size:7px}
}
@media print{
 .invoice .items td,.invoice .items th{padding:5px 6px}
 .invoice .logo-cell img{height:60px}
 .invoice .invoice-header{padding-bottom:10px}
 .invoice .buyer{margin-top:10px}
 .invoice .table-caption{margin-top:12px}
 .invoice .signatures>div{height:56px}
 .invoice .invoice-footer{margin-top:12px}
 .invoice.a5 .items td,.invoice.a5 .items th{padding:4px 3px}
 .invoice.a5 .logo-cell img{height:44px}
 .invoice.a5 .signatures>div{height:42px}
 .invoice.a5 .invoice-notes{margin-top:8px;padding-top:6px}
}
@media print{
 .invoice.a5 .totals>div{padding:3px 7px}
 .invoice.a5 .buyer-details{padding:5px 7px;gap:4px}
 .invoice.a5 .sample-warning{margin-top:5px}
 .invoice.a5 .invoice-notes p{line-height:1.6}
 .invoice.a5 .signatures{padding:7px 0}
 .invoice.a5 .bank-owner{line-height:1.4}
 .invoice.a5 .bank-numbers{padding-bottom:5px}
}
.fixed-layout-note{margin:16px 20px 0}.bank-entry{clear:both;border:1px solid #e2e8f0;border-radius:9px;padding:12px;margin-bottom:12px}.bank-entry-head{display:flex;align-items:center;justify-content:space-between;margin-bottom:10px;font-size:11px}.bank-entry-head button{padding:3px 8px;font-size:10px;color:#b91c1c}.bank-entry label{display:block;font-size:11px;color:#64748b;margin:8px 0}.field-error{display:block;color:#b91c1c;font-size:10px;margin-top:5px}input[aria-invalid=true]{border-color:#dc2626!important;background:#fff7f7!important}.primary:disabled{opacity:.45;cursor:not-allowed}.paper-choice{display:flex;align-items:center;gap:5px;font-size:10px}.paper-choice select{width:64px;margin:0;padding:7px}.bank-list{padding:8px 12px}.printed-bank-entry{padding:5px 0;border-bottom:1px solid #e2e8f0;break-inside:avoid}.printed-bank-entry:last-child{border:0}.printed-bank-entry>strong{font-size:9px}.printed-bank-entry>div{display:flex;align-items:center;justify-content:space-between;gap:8px;font-size:9px;padding:3px 0}.printed-bank-entry bdi{white-space:nowrap;font-size:10px}.branded-footer{position:relative;display:block;border:1px solid #aab4c0;border-top:0;border-radius:0 0 9px 9px;min-height:20px;margin-top:22px}.branded-footer .footer-brand{position:absolute;bottom:-8px;left:50%;transform:translateX(-50%);background:white;padding:0 10px;color:#475569;font-size:9px;white-space:nowrap}.footer-brand bdi{font-size:7px;margin-right:5px}.invoice-notes p{font-size:10px;color:#475569}.invoice.a5 .invoice-notes p{font-size:8px}.invoice.a5 .printed-bank-entry bdi{font-size:8px}.invoice.a5 .branded-footer .footer-brand{font-size:8px}.toolbar-actions{flex-wrap:wrap;justify-content:flex-end}
@media print{.branded-footer{margin-bottom:9px}.invoice-notes{break-inside:auto}.bank-details{break-inside:auto}.bank-details .section-tag{break-after:avoid}.printed-bank-entry{break-inside:avoid}}

/* Software brand sits on the separator immediately above invoice notes. */
.bank-details { position: relative; overflow: visible; margin-bottom: 14px; padding-bottom: 8px; }
.invoice-notes { position: relative; }
.bank-frame-brand { position: absolute; top: 0; left: 50%; transform: translate(-50%,-50%); background: #fff; padding: 0 10px; color: #475569; font-size: 9px; line-height: 1.6; white-space: nowrap; }
.bank-frame-brand bdi { font-size: 7px; margin-right: 5px; }
.invoice.a5 .bank-frame-brand { font-size: 8px; }
@media print { .bank-details { break-inside: avoid; } }

.iban-input-wrap { display:flex; align-items:stretch; margin-top:6px; border:1px solid #dbe1ea; border-radius:7px; overflow:hidden; background:white; }
.iban-prefix { display:flex; align-items:center; padding:0 9px; background:#f1f5f9; color:#475569; border-right:1px solid #dbe1ea; font-size:12px; }
.iban-input-wrap input.bank-iban { border:0; margin:0; min-width:0; border-radius:0; }
.iban-input-wrap:has(input[aria-invalid=true]) { border-color:#dc2626; }

/* Seller contact information belongs with payment details, not the header. */
.bank-details .store-contact { padding: 9px 12px; flex-wrap: wrap; justify-content: flex-start; gap: 7px 24px; }
.invoice.a5 .bank-details .store-contact { padding: 6px 9px; gap: 5px 14px; }

.preview-bank-state{display:block;color:#b45309;font-size:9px;margin:3px 0}.printed-bank-entry bdi{overflow-wrap:anywhere;white-space:normal}
@media print{.preview-bank-state{display:none}}

/* Compact, legible invoice facts; row heading must never split into letters. */
.items .row-col { width: 38px; }
.items .row-heading { white-space: nowrap; word-break: normal; overflow-wrap: normal; padding-inline: 3px; }
.invoice.a5 .items .row-col { width: 30px; }
.invoice-info dl { font-size: 11px; line-height: 1.8; }
.invoice-info dl div { margin: 7px 0; }
.invoice-info dt { color: #64748b; }
.invoice.a5 .invoice-info dl { font-size: 9px; }
.invoice-notes p { font-size: 11px; line-height: 1.9; }
.invoice.a5 .invoice-notes p { font-size: 9px; line-height: 1.8; }
.printed-bank-entry > div { justify-content: flex-start; gap: 10px; flex-wrap: wrap; }
.printed-bank-entry > div > span { white-space: nowrap; }
.printed-bank-entry bdi { font-size: 11px; }
.invoice.a5 .printed-bank-entry bdi { font-size: 9px; }

/* Subtle vertical separators keep all proposed table columns distinct. */
.invoice .proposal-items th:not(:last-child),
.invoice .proposal-items td:not(:last-child) {
 border-left: 1px solid #c7cfd9;
}
/* Full-width paired summary: no tall empty panel beside the totals. */
.invoice .settlement { display:block; }
.invoice .invoice-info { padding:8px 12px;border-left:0;border-bottom:1px solid var(--line); }
.invoice .invoice-info dl { display:grid;grid-template-columns:1fr 1fr;gap:0; }
.invoice .invoice-info dl div { margin:0;align-items:center;gap:10px; }
.invoice .invoice-info dl div+div { border-right:1px solid #dfe3e9;padding-right:12px; }
.invoice .totals { display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr); }
.invoice .totals>div { min-width:0;padding:9px 12px;border-bottom:1px solid #dfe3e9; }
.invoice .totals>div:nth-child(odd):not(.payable) { border-left:1px solid #dfe3e9; }
.invoice .totals .payable { grid-column:1/-1;border-bottom:0;padding:12px; }
.invoice.a5 .invoice-info { padding:6px 8px; }
.invoice.a5 .totals>div { padding:7px 8px; }
.invoice.a5 .totals .payable { padding:9px 8px; }

/* Invoice facts precede the item table; sums align with each numeric column. */
.invoice .items-section>.invoice-info { border:1px solid var(--line);border-bottom:0;background:#fff; }
/* Keep document labels and their values together. */
.invoice .document-meta div { justify-content:flex-start;gap:8px; }
.invoice .document-meta dt { flex:0 0 66px; }
.invoice .document-meta dd { text-align:right; }
.invoice.a5 .document-meta dt { flex-basis:53px; }
.invoice.a5 .document-meta div { gap:5px; }
/* Restore the original single-column monetary summary on the left. */
.invoice .settlement { display:flex;justify-content:flex-end;border:0; }
.invoice .settlement .totals { display:block;width:280px;max-width:100%;border:1px solid var(--line);border-top:0; }
.invoice .settlement .totals>div { border-left:0;border-right:0;border-bottom:1px solid #dfe3e9; }
.invoice .settlement .totals .payable { border-bottom:0; }
.invoice.a5 .settlement .totals { width:210px; }

/* Requested live review of the detailed price table; other sections unchanged. */
.invoice .detailed-items th,.invoice .detailed-items td { padding:9px 5px;font-size:10px; }
.invoice .detailed-items .money { color:var(--ink);font-size:10px; }
.invoice .detailed-items .item-note { display:block;font-size:8px;color:#7b8490;margin-top:4px; }
.invoice .detailed-items td:last-child { font-weight:700; }
.invoice.a5 .detailed-items th,.invoice.a5 .detailed-items td { padding:6px 3px;font-size:8px; }
.invoice.a5 .detailed-items .money { font-size:8px; }
.invoice.a5 .detailed-items .item-note { font-size:7px; }
/* Stronger divider between payment method and item quantity, including print. */
.invoice .invoice-info dl div+div { border-right:1.5px solid #475569; }
@media print {
 .invoice .invoice-info dl div+div { border-right:0.6mm solid #303b4b; }
}

.cheque-details{margin-top:14px;break-inside:avoid}
.cheque-caption{display:flex;justify-content:space-between;gap:10px;font-size:10px;font-weight:700;margin-bottom:7px}
.cheque-caption span{font-size:9px;color:#64748b;font-weight:400}
.cheque-table{width:100%;border-collapse:collapse;table-layout:fixed;font-size:10px}
.cheque-table th,.cheque-table td{border:1px solid #a5afbc;padding:8px;text-align:center}
.cheque-table th{background:#f0f2f5;font-weight:700}
.cheque-table th small{font-size:8px;font-weight:400}
.cheque-number{font-variant-numeric:tabular-nums}
.a5 .cheque-details{margin-top:10px}.a5 .cheque-caption{font-size:8px}.a5 .cheque-caption span{font-size:7px}
.a5 .cheque-table{font-size:8px}.a5 .cheque-table th,.a5 .cheque-table td{padding:5px}

.settlement-details{margin-top:14px;break-inside:avoid}
.settlement-details h3{margin-bottom:7px}
#settlement-breakdown{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));border:1px solid #a5afbc}
#settlement-breakdown>div{padding:10px 12px;display:flex;flex-direction:column;gap:5px;min-width:0}
#settlement-breakdown>div+div{border-right:1px solid #a5afbc}
#settlement-breakdown span{font-size:10px;color:#64748b}
#settlement-breakdown strong{font-size:11px;font-variant-numeric:tabular-nums}
#settlement-breakdown small{font-size:9px;color:#64748b}
.a5 #settlement-breakdown>div{padding:7px 8px}.a5 #settlement-breakdown span,.a5 #settlement-breakdown strong{font-size:8px}.a5 #settlement-breakdown small{font-size:7px}
/* Compact settlement columns: method header, rule, amount and optional detail. */
#settlement-breakdown>div { padding:0;gap:0; }
#settlement-breakdown span { display:block;padding:5px 10px;text-align:center;background:#f0f2f5;border-bottom:1px solid #a5afbc;color:#334155;font-weight:700; }
#settlement-breakdown strong { display:block;padding:7px 10px 4px;text-align:center; }
#settlement-breakdown small { display:block;padding:0 10px 6px;text-align:center; }
#settlement-breakdown>div:not(:has(small)) strong { padding-bottom:7px; }
.invoice.a5 #settlement-breakdown>div { padding:0; }
.invoice.a5 #settlement-breakdown span { padding:4px 7px; }
.invoice.a5 #settlement-breakdown strong { padding:5px 7px 3px; }
.invoice.a5 #settlement-breakdown small { padding:0 7px 4px; }

/* Production document uses exactly the approved invoice structure, not application UI. */
@page{size:__PAGE_SIZE__ portrait;margin:__PAGE_MARGIN__}
body{background:white}.invoice{box-shadow:none;margin:0 auto}
.invoice .invoice-header{grid-template-columns:180px minmax(0,1fr) 180px}
.invoice.a5 .invoice-header{grid-template-columns:125px minmax(0,1fr) 125px}
.invoice .document-meta div{justify-content:flex-end}.invoice .document-meta dt{flex:0 0 auto}
.invoice .logo-cell .letter-logo{width:106px;height:74px;display:grid;place-items:center;font-size:24px;border:1px solid #b5bdc8}
.invoice.a5 .logo-cell .letter-logo{width:70px;height:50px;font-size:18px}
.invoice .detailed-items .long-number{white-space:normal;overflow-wrap:anywhere;font-size:8px}
.invoice.a5 .detailed-items .long-number{font-size:7px}
.invoice .buyer-details strong{overflow-wrap:anywhere;min-width:0}
.invoice .title-cell h2{overflow-wrap:anywhere}
#settlement-breakdown{grid-template-columns:repeat(var(--parts,3),minmax(0,1fr))}
.cancelled{border:2px solid #b91c1c;color:#b91c1c;padding:8px;text-align:center;margin-top:12px;font-weight:700}
.installment-details{margin-top:12px;break-inside:avoid}.installment-details h3{margin-bottom:7px}
.printed-bank-entry bdi{font-variant-numeric:tabular-nums}.invoice .printed-bank-entry>div{justify-content:flex-start;gap:10px}
.invoice .totals dd{overflow-wrap:anywhere;white-space:normal}

.invoice.purchase-document .invoice-header{grid-template-columns:228px minmax(0,1fr) 228px}
.invoice.a5.purchase-document .invoice-header{grid-template-columns:180px minmax(0,1fr) 180px}
.invoice.purchase-document .document-meta dt{white-space:nowrap}

""";
}
