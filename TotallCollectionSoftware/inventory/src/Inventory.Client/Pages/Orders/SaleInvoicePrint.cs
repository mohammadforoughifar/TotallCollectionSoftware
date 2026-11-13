using System.Net;
using System.Text;
using Inventory.Client.Extensions;
using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Pages.Orders;

/// <summary>
/// ساخت فاکتور/رسید چاپی مستقل و خودبسنده برای اسناد خرید/فروش (Orders).
/// سند به‌صورت HTML کامل (doctype + CSS خودبسنده) تولید می‌شود تا در iframe جداگانه
/// چاپ شود؛ چون استایل‌های <c>@media print</c>ِ خودِ برنامه روی محتوای سند اثر می‌گذارد.
/// نام فروشگاه، شماره حساب، شماره تماس و تنظیمات چاپ از «تعریف شرکت» (CompanyProfile) می‌آید.
/// </summary>
public static class SaleInvoicePrint
{
    /// <summary>سند کامل HTML چاپ را از روی سند خرید/فروش + پروفایل شرکت می‌سازد.</summary>
    public static string Build(Order order, CompanyProfileDto? profile, string? paperSize = null, ShopPrintSettingsDto? shopSettings = null)
    {
        var company = string.IsNullOrWhiteSpace(profile?.Name) ? "فروغ آریا" : profile!.Name.Trim();
        var isSale = order.IsSale;
        var docTitle = isSale ? "فاکتور فروش" : "فاکتور خرید";
        var partyLabel = isSale ? "مشتری" : "فروشنده";
        var lines = order.Lines.OrderBy(l => l.Id).ToList();
        var showSignatures = profile?.PrintShowSignatures ?? true;
        // Preserve fractional totals and rounded cheque/installment schedules in print.
        var exactPayments = order.HasChequeCharge || order.Cheques.Count > 0 ||
            order.Installments.Count > 0 || order.TotalAmount % 1m != 0m ||
            lines.Any(l => l.Price % 1m != 0m || l.Gross % 1m != 0m || l.DiscountAmount % 1m != 0m ||
                l.AfterDiscount % 1m != 0m || l.VatAmount % 1m != 0m || l.Total % 1m != 0m);
        // An individual print choice overrides, but never changes, the company default.
        var isA5 = string.Equals(paperSize, "A5", StringComparison.OrdinalIgnoreCase) ? true
            : string.Equals(paperSize, "A4", StringComparison.OrdinalIgnoreCase) ? false
            : profile?.IsA5 == true;
        var css = CssFor(isA5);

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"fa\" dir=\"rtl\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<title>").Append(Esc(docTitle)).Append(' ').Append(Esc(order.Number)).Append("</title>");
        sb.Append("<style>").Append(css).Append("</style></head><body><main class=\"sheet ")
            .Append(isA5 ? "paper-a5" : "paper-a4").Append("\">");

        // ---------- متن‌های بالای فاکتور (از تنظیمات چاپ) ----------
        foreach (var hl in SplitLines(profile?.PrintHeaderLines))
            sb.Append("<div class=\"pre-head\">").Append(Esc(hl)).Append("</div>");

        if (!isSale)
        {
            // Purchase-only header: equal outer columns keep the company centered.
            // The current Order DTO has no supplier invoice number; never use a demo value.
            sb.Append("<header class=\"purchase-header\"><div class=\"purchase-mark\">").Append(CompanyMark(profile, "خ")).Append("</div>");
            sb.Append("<div class=\"purchase-title\"><div class=\"caption\">فاکتور خرید</div><div class=\"company\">")
                .Append(Esc(company)).Append("</div></div><div class=\"purchase-meta\">");
            Meta(sb, "شماره سند خرید", order.Number);
            Meta(sb, "شماره فاکتور تأمین‌کننده", "-");
            Meta(sb, "تاریخ", order.Date.ToFa().FaDigits());
            sb.Append("</div>");
            if (!string.IsNullOrWhiteSpace(profile?.Phone) || !string.IsNullOrWhiteSpace(profile?.Address))
                sb.Append("<div class=\"purchase-contact\">").Append(Esc(profile?.Phone)).Append(" ")
                    .Append(Esc(profile?.Address)).Append("</div>");
            sb.Append("</header>");
        }
        else
        {
        // ---------- سربرگ — وسط‌چین (نام فروشگاه + عنوان سند) ----------
        sb.Append("<header class=\"header\">");
        sb.Append("<div class=\"brand-center\">").Append(CompanyMark(profile, isSale ? "ف" : "ر"))
            .Append("<div class=\"company\">")
            .Append(Esc(company)).Append("</div><div class=\"caption\">").Append(docTitle).Append("</div></div>");

        // ---------- اطلاعات تماس فروشگاه — وسط‌چین (تلفن / آدرس / شناسه‌ها) ----------
        var contactBits = new List<string>();
        if (!string.IsNullOrWhiteSpace(profile?.Phone)) contactBits.Add("تلفن: " + profile!.Phone!.Trim());
        if (!string.IsNullOrWhiteSpace(profile?.EconomicCode)) contactBits.Add("کد اقتصادی: " + profile!.EconomicCode!.Trim());
        if (!string.IsNullOrWhiteSpace(profile?.TaxId)) contactBits.Add("شناسه ملی: " + profile!.TaxId!.Trim());
        if (contactBits.Count > 0)
            sb.Append("<div class=\"contact-strip\">").Append(Esc(string.Join("  •  ", contactBits))).Append("</div>");
        if (!string.IsNullOrWhiteSpace(profile?.Address))
            sb.Append("<div class=\"contact-strip address\">آدرس: ").Append(Esc(profile!.Address!.Trim())).Append("</div>");

        sb.Append("<div class=\"invoice-stamp\"><span>").Append(docTitle).Append("</span><strong>")
            .Append(Esc(order.Number)).Append("</strong></div></header>");
        }
        if (order.IsCancelled)
            sb.Append("<div class=\"cancelled\">این فاکتور ابطال شده است</div>");

        // ---------- متادیتای سند ----------
        sb.Append("<section class=\"meta\">");
        if (isSale)
        {
            Meta(sb, "شماره سند", order.Number);
            Meta(sb, "تاریخ", order.Date.ToFa().FaDigits());
        }
        Meta(sb, "انبار", string.IsNullOrWhiteSpace(order.WarehouseName) ? "—" : order.WarehouseName!);
        if (isSale)
            Meta(sb, "روش پرداخت", PayLabel(order.PaymentMethod));
        sb.Append("</section>");

        // ---------- طرف حساب ----------
        sb.Append("<section class=\"party\"><div class=\"party-icon\">م</div><div class=\"party-info\"><div class=\"eyebrow\">")
            .Append(partyLabel)
            .Append("</div><strong>").Append(Esc(order.PartyName ?? "—")).Append("</strong></div>");
        if (!string.IsNullOrWhiteSpace(order.ReferrerName))
            sb.Append("<div class=\"party-note\">معرف: ").Append(Esc(order.ReferrerName)).Append("</div>");
        sb.Append("</section>");

        // ---------- جدول اقلام ----------
        sb.Append("<section class=\"section\"><div class=\"section-head\"><span class=\"step\">۱</span><div><h2>اقلام سند</h2><small>")
            .Append(lines.Count.Num()).Append(" قلم</small></div></div>");
        var hasLineDiscount = lines.Any(l => l.DiscountAmount > 0);
        var totalGross = lines.Sum(l => l.Gross);
        var totalDiscount = lines.Sum(l => l.DiscountAmount);
        var hasVat = lines.Any(l => l.VatValue > 0 || l.VatAmount > 0);
        var totalVat = lines.Sum(l => l.VatAmount);

        var hasAdjustments = hasLineDiscount || hasVat;
        if (isA5)
        {
            // A5 has a narrower physical page: combine unit with quantity and
            // discount/VAT into one optional column instead of overflowing it.
            sb.Append("<table class=\"items-table compact-items\"><thead><tr><th class=\"num-col\">ردیف</th><th>شرح کالا / خدمت</th><th class=\"qty-col\">مقدار</th><th class=\"money-col\">فی (ریال)</th>");
            if (hasAdjustments) sb.Append("<th class=\"adjust-col\">تخفیف / ارزش افزوده</th>");
            sb.Append("<th class=\"money-col\">مبلغ (ریال)</th></tr></thead><tbody>");
        }
        else
        {
            sb.Append("<table class=\"items-table\"><thead><tr><th class=\"num-col\">ردیف</th><th>شرح کالا / خدمت</th><th class=\"unit-col\">واحد</th><th class=\"qty-col\">تعداد</th><th class=\"money-col\">فی (ریال)</th>");
            if (hasLineDiscount) sb.Append("<th class=\"money-col\">تخفیف</th>");
            if (hasVat) sb.Append("<th class=\"money-col\">پس از تخفیف</th><th class=\"money-col\">ارزش افزوده</th>");
            sb.Append("<th class=\"money-col\">مبلغ (ریال)</th></tr></thead><tbody>");
        }
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            sb.Append("<tr><td class=\"num-cell\">").Append((i + 1).Num()).Append("</td><td><strong>")
                .Append(Esc(line.ProductName));
            if (line.IsService)
                sb.Append(" <span class=\"badge\">خدمت</span>");
            if (!string.IsNullOrWhiteSpace(line.Description) && !string.Equals(line.Description, line.ProductName, StringComparison.Ordinal))
                sb.Append("<small class=\"subline\">").Append(Esc(line.Description)).Append("</small>");
            if (isA5)
            {
                sb.Append("</strong></td><td class=\"qty-cell\">").Append(line.Quantity.Qty())
                    .Append("<small class=\"subline\">").Append(Esc(line.Unit)).Append("</small></td><td class=\"money-cell\">")
                    .Append(exactPayments ? line.Price.MoneyExact() : line.Price.Money()).Append("</td>");
                if (hasAdjustments)
                {
                    sb.Append("<td class=\"adjust-cell\">");
                    if (line.DiscountAmount > 0)
                        sb.Append("<span>تخفیف: <b class=\"num\">").Append(line.DiscountAmount.MoneyExact()).Append("</b></span>");
                    if (line.VatAmount > 0 || line.VatValue > 0)
                        sb.Append("<span>ارزش افزوده: <b class=\"num\">").Append(line.VatAmount.MoneyExact()).Append("</b></span>");
                    if (line.DiscountAmount <= 0 && line.VatAmount <= 0 && line.VatValue <= 0) sb.Append("—");
                    sb.Append("</td>");
                }
                sb.Append("<td class=\"money-cell total-cell\">")
                    .Append(exactPayments ? line.Total.MoneyExact() : line.Total.Money()).Append("</td></tr>");
                continue;
            }
            sb.Append("</strong></td><td class=\"qty-cell\">").Append(Esc(line.Unit))
                .Append("</td><td class=\"qty-cell\">").Append(line.Quantity.Qty())
                .Append("</td><td class=\"money-cell\">").Append(exactPayments ? line.Price.MoneyExact() : line.Price.Money());
            if (hasLineDiscount)
            {
                sb.Append("</td><td class=\"money-cell\">");
                if (line.DiscountAmount > 0)
                {
                    sb.Append(exactPayments ? line.DiscountAmount.MoneyExact() : line.DiscountAmount.Money());
                    if (line.DiscountIsAmount && line.DiscountPercent > 0)
                        sb.Append(" <small class=\"text-muted\">(").Append(line.DiscountPercent.ToString("0.##")).Append("٪)</small>");
                }
                else
                {
                    sb.Append("—");
                }
            }
            if (hasVat)
            {
                sb.Append("</td><td class=\"money-cell\">").Append(exactPayments ? line.AfterDiscount.MoneyExact() : line.AfterDiscount.Money());
                sb.Append("</td><td class=\"money-cell\">").Append(exactPayments ? line.VatAmount.MoneyExact() : line.VatAmount.Money());
                if (line.VatValue > 0 && line.VatValue <= 100)
                    sb.Append(" <small class=\"text-muted\">(").Append(line.VatValue.ToString("0.##")).Append("٪)</small>");
            }
            sb.Append("</td><td class=\"money-cell total-cell\">").Append(exactPayments ? line.Total.MoneyExact() : line.Total.Money()).Append("</td></tr>");
        }
        if (lines.Count == 0)
        {
            var columns = isA5 ? 5 + (hasAdjustments ? 1 : 0) : (hasLineDiscount ? 7 : 6) + (hasVat ? 2 : 0);
            sb.Append("<tr><td colspan=\"").Append(columns).Append("\" class=\"empty\">برای این سند ردیفی ثبت نشده است.</td></tr>");
        }
        sb.Append("</tbody></table></section>");

        // ---------- توضیحات ----------
        if (!string.IsNullOrWhiteSpace(order.Description))
            sb.Append("<div class=\"desc\"><span>توضیحات: </span>").Append(Esc(order.Description)).Append("</div>");

        // ---------- جمع کل ----------
        if ((hasLineDiscount && totalDiscount > 0) || hasVat || order.ChequeIncomeAmount > 0 || order.ReturnedGoodsAmount > 0)
        {
            sb.Append("<div style=\"margin-top:10px;padding:8px 12px;border:1px solid #e2e8f0;border-radius:10px;background:#f8fafc;font-size:10.5px;\">");
            sb.Append("<div style=\"display:flex;justify-content:space-between;padding:2px 0;\"><span>جمع اقلام (ناخالص):</span><b class=\"num\">").Append(exactPayments ? totalGross.MoneyExact() : totalGross.Money()).Append(" ریال</b></div>");
            sb.Append("<div style=\"display:flex;justify-content:space-between;padding:2px 0;color:#dc2626;\"><span>تخفیف کل اقلام:</span><b class=\"num\">- ").Append(exactPayments ? totalDiscount.MoneyExact() : totalDiscount.Money()).Append(" ریال</b></div>");
            if (hasVat)
                sb.Append("<div style=\"display:flex;justify-content:space-between;padding:2px 0;\"><span>ارزش افزوده:</span><b class=\"num\">").Append(exactPayments ? totalVat.MoneyExact() : totalVat.Money()).Append(" ریال</b></div>");
            if (order.ReturnedGoodsAmount > 0)
                sb.Append("<div style=\"display:flex;justify-content:space-between;padding:2px 0;color:#b91c1c;\"><span>برگشت کالا و ارزش افزوده:</span><b class=\"num\">- ").Append(order.ReturnedGoodsAmount.MoneyExact()).Append(" ریال</b></div>");
            if (order.ChequeIncomeAmount > 0)
            {
                sb.Append("<div style=\"display:flex;justify-content:space-between;padding:2px 0;\"><span>کالا و ارزش افزوده:</span><b class=\"num\">").Append(exactPayments ? order.GoodsAmount.MoneyExact() : order.GoodsAmount.Money()).Append(" ریال</b></div>");
                sb.Append("<div style=\"display:flex;justify-content:space-between;padding:2px 0;\"><span>تاخیر تادیه چک (بدون ارزش افزوده):</span><b class=\"num\">").Append(exactPayments ? order.ChequeIncomeAmount.MoneyExact() : order.ChequeIncomeAmount.Money()).Append(" ریال</b></div>");
            }
            sb.Append("</div>");
        }

        sb.Append("<section class=\"grand-total\"><div><span>جمع نهایی سند</span><small>")
            .Append(docTitle).Append(" — مانده حساب در صورت‌حساب مشتری مشخص است</small></div><strong>")
            .Append(exactPayments ? order.TotalAmount.MoneyExact() : order.TotalAmount.Money()).Append(" <small>ریال</small></strong></section>");

        if (order.Cheques.Count > 0 || order.Installments.Count > 0)
            AppendPaymentSchedule(sb, order);

        // ---------- امضاها ----------
        if (showSignatures)
        {
            sb.Append("<section class=\"signatures\"><div><span>امضای ").Append(partyLabel)
                .Append("</span></div><div><span>مهر و امضای ").Append(isSale ? "فروشنده" : "خریدار")
                .Append("</span></div></section>");
        }

        // ---------- footer — شامل شماره حساب فروشگاه ----------
        sb.Append("<footer>");
        if (isSale && shopSettings is not null)
        {
            foreach (var bank in ShopPrintBankRules.Validate(shopSettings).Banks)
            {
                if (bank.Card.Length > 0) sb.Append("<div class=\"account-line\">شماره کارت: <bdi>").Append(Esc(bank.Card)).Append("</bdi></div>");
                if (bank.Iban.Length > 0) sb.Append("<div class=\"account-line\">شماره شبا: <bdi>").Append(Esc(bank.Iban)).Append("</bdi></div>");
            }
        }
        else if (!string.IsNullOrWhiteSpace(profile?.AccountNumber))
            sb.Append("<div class=\"account-line\"><b>شماره حساب:</b> ").Append(Esc(profile!.AccountNumber!.Trim())).Append("</div>");
        var footerLines = SplitLines(profile?.PrintFooterLines);
        if (footerLines.Count > 0)
        {
            foreach (var fl in footerLines)
                sb.Append("<div>").Append(Esc(fl)).Append("</div>");
        }
        else
        {
            sb.Append("<div>از اعتماد شما سپاسگزاریم <span>•</span> ").Append(Esc(company)).Append(" <span>•</span> چاپ‌شده در ")
                .Append(DateTime.Now.ToFaDateTime().FaDigits()).Append("</div>");
        }
        sb.Append("</footer>");
        sb.Append("</main></body></html>");
        return sb.ToString();
    }

    private static void AppendPaymentSchedule(StringBuilder sb, Order order)
    {
        sb.Append("<section class=\"section payment-schedule\"><div class=\"section-head\"><span class=\"step\">۲</span><div><h2>چک‌ها و اقساط ثبت‌شده</h2><small>")
            .Append(order.IsSale ? "تحویل چک به معنی وصول آن نیست." : "ثبت چک به معنی پرداخت آن نیست.")
            .Append("</small></div></div><table><thead><tr><th>شرح</th><th>سررسید</th><th>وضعیت</th><th class=\"money-col\">مبلغ (ریال)</th></tr></thead><tbody>");
        foreach (var cheque in order.Cheques.OrderBy(c => c.DueDate))
        {
            var status = cheque.Status switch
            {
                ChequeStatus.Cleared => order.IsSale ? "وصول‌شده" : "پرداخت‌شده",
                ChequeStatus.Bounced => "برگشت‌خورده",
                ChequeStatus.InCollection => "در جریان وصول",
                ChequeStatus.Endorsed => "واگذارشده",
                ChequeStatus.Cancelled => "ابطال‌شده",
                _ => cheque.IsCleared ? (order.IsSale ? "وصول‌شده" : "پرداخت‌شده") : "در انتظار"
            };
            sb.Append("<tr><td>چک ").Append(Esc(cheque.Number));
            if (!string.IsNullOrWhiteSpace(cheque.BankName)) sb.Append(" — ").Append(Esc(cheque.BankName));
            sb.Append("</td><td>").Append(cheque.DueDate == default ? "—" : cheque.DueDate.ToFa().FaDigits())
                .Append("</td><td>").Append(status).Append("</td><td class=\"money-cell\">")
                .Append(cheque.Amount.MoneyExact()).Append("</td></tr>");
        }
        foreach (var installment in order.Installments.OrderBy(i => i.No))
        {
            sb.Append("<tr><td>قسط ").Append(installment.No.Num()).Append("</td><td>")
                .Append(installment.DueDate == default ? "—" : installment.DueDate.ToFa().FaDigits())
                .Append("</td><td>").Append(installment.IsPaid ? "پرداخت‌شده" : "در انتظار")
                .Append("</td><td class=\"money-cell\">").Append(installment.Amount.MoneyExact()).Append("</td></tr>");
        }
        sb.Append("</tbody></table></section>");
    }

    private static void Meta(StringBuilder sb, string label, string value)
        => sb.Append("<div class=\"meta-item\"><span>").Append(Esc(label)).Append("</span><strong>").Append(Esc(value)).Append("</strong></div>");

    private static string PayLabel(PaymentMethod m) => m switch
    {
        PaymentMethod.Cash => "نقدی",
        PaymentMethod.Credit => "نسیه",
        PaymentMethod.Cheque => "چک",
        PaymentMethod.Mixed => "ترکیبی",
        _ => "اقساطی"
    };

    /// <summary>متن چندخطی را به فهرست خطوط تبدیل می‌کند (\n یا \r\n).</summary>
    private static List<string> SplitLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        return text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static string CompanyMark(CompanyProfileDto? profile, string fallback)
        => profile?.LogoDataUrl?.StartsWith("data:image/png;base64,", StringComparison.Ordinal) == true
            ? "<img class=\"mark company-logo\" src=\"" + Esc(profile.LogoDataUrl) + "\" alt=\"لوگوی شرکت\">"
            : "<span class=\"mark\">" + Esc(fallback) + "</span>";

    private static string Esc(string? value) => WebUtility.HtmlEncode(value ?? "");

    /// <summary>CSS سند با اندازهٔ کاغذ انتخاب‌شده (A4 یا A5).</summary>
    private static string CssFor(bool a5) => Css
        .Replace("__PAGE_SIZE__", a5 ? "A5 portrait" : "A4 portrait")
        .Replace("__PAGE_MARGIN__", a5 ? "7mm" : "10mm")
        .Replace("__SHEET_W__", a5 ? "134mm" : "190mm");

    public const string Css = @"@font-face { font-family:'Vazirmatn'; src:url('__FONT_BASE__fonts/Vazirmatn-Regular.woff2') format('woff2'); font-weight:400; font-display:swap; }
@font-face { font-family:'Vazirmatn'; src:url('__FONT_BASE__fonts/Vazirmatn-Medium.woff2') format('woff2'); font-weight:500; font-display:swap; }
@font-face { font-family:'Vazirmatn'; src:url('__FONT_BASE__fonts/Vazirmatn-Bold.woff2') format('woff2'); font-weight:700; font-display:swap; }
@page { size:__PAGE_SIZE__; margin:__PAGE_MARGIN__; }
* { box-sizing:border-box; }
body { margin:0; background:#fff; color:#182337; font-family:'Vazirmatn',Tahoma,'Segoe UI',sans-serif; font-size:10.5px; }
.sheet { max-width:__SHEET_W__; margin:0 auto; }
.pre-head { text-align:center; color:#536278; font-size:9px; padding:1px 0; overflow-wrap:anywhere; }
.header { display:grid; grid-template-columns:minmax(0,1fr) auto; gap:4px 12px; align-items:center; padding:0 0 9px; border-bottom:2px solid #4f46a5; }
.brand-center { min-width:0; display:grid; grid-template-columns:34px minmax(0,1fr); grid-template-areas:'mark company' 'mark caption'; align-items:center; gap:1px 9px; text-align:right; }
.mark { grid-area:mark; width:34px; height:34px; display:grid; place-items:center; border:1px solid #c7d2fe; border-radius:10px; background:#eef2ff; color:#4338ca; font-size:18px; font-weight:700; }
.company { grid-area:company; color:#1e1b4b; font-size:16px; font-weight:700; overflow-wrap:anywhere; }
.caption { grid-area:caption; color:#64748b; font-size:9px; }
.invoice-stamp { grid-column:2; grid-row:1; display:grid; gap:2px; min-width:90px; padding:5px 9px; border:1px solid #c7d2fe; border-radius:8px; color:#3730a3; text-align:center; }
.invoice-stamp span { font-size:9px; }.invoice-stamp strong { font-size:12px; overflow-wrap:anywhere; }
.contact-strip { grid-column:1 / -1; color:#536278; font-size:9px; text-align:right; overflow-wrap:anywhere; }
.contact-strip.address { margin-top:0; }
.cancelled { margin-top:8px; padding:5px 8px; border:1px solid #dc2626; color:#991b1b; text-align:center; font-weight:700; }
.meta { display:grid; grid-template-columns:repeat(4,minmax(0,1fr)); gap:6px; margin:9px 0; }
.meta-item { min-width:0; display:grid; gap:2px; padding:6px 8px; border:1px solid #e2e8f0; border-radius:7px; background:#f8fafc; }
.meta-item span,.eyebrow { color:#64748b; font-size:9px; }.meta-item strong { overflow-wrap:anywhere; font-size:10px; }
.party { display:flex; align-items:center; gap:8px; margin-bottom:10px; padding:7px 9px; border:1px solid #dbe3f4; border-radius:8px; background:#f8faff; }
.party-icon { width:27px; height:27px; flex:none; display:grid; place-items:center; border-radius:8px; background:#eef2ff; color:#4f46e5; font-weight:700; }
.party-info { display:grid; gap:1px; min-width:0; }.party-info strong { font-size:11px; overflow-wrap:anywhere; }
.party-note { margin-inline-start:auto; color:#64748b; font-size:9px; overflow-wrap:anywhere; }
.section { margin-top:11px; }.section-head { display:flex; align-items:center; gap:7px; margin-bottom:6px; }
.section-head h2 { margin:0; color:#1e293b; font-size:11px; font-weight:700; }.section-head small { display:block; color:#64748b; font-size:8px; }
.step { width:21px; height:21px; flex:none; display:grid; place-items:center; border-radius:6px; background:#eef2ff; color:#4338ca; font-weight:700; }
table { width:100%; table-layout:fixed; border-collapse:collapse; }
thead { display:table-header-group; } thead th { padding:6px 5px; background:#303d68; color:#fff; text-align:right; font-size:9px; font-weight:700; overflow-wrap:anywhere; }
tbody td { padding:6px 5px; border-bottom:1px solid #e2e8f0; vertical-align:top; overflow-wrap:anywhere; }
tbody tr:nth-child(even) td { background:#f8fafc; }
.num-col { width:5%; text-align:center; }.unit-col { width:7%; }.qty-col { width:8%; text-align:center; }.money-col { width:12%; text-align:left; }
.num-cell,.qty-cell { text-align:center; }.num-cell { color:#64748b; }
.money-cell { direction:ltr; text-align:left; white-space:normal; overflow-wrap:anywhere; font-variant-numeric:tabular-nums; }
.total-cell { color:#1e1b4b; font-weight:700; }.subline { display:block; color:#64748b; font-size:8px; }
.compact-items .num-col { width:7%; }.compact-items .qty-col { width:12%; }.compact-items .money-col { width:18%; }.compact-items .adjust-col { width:20%; }
.adjust-cell { font-size:8px; }.adjust-cell span { display:block; }.adjust-cell b { direction:ltr; unicode-bidi:isolate; font-variant-numeric:tabular-nums; }
.payment-schedule th:first-child { width:38%; }.payment-schedule th:nth-child(2),.payment-schedule th:nth-child(3) { width:20%; }.payment-schedule .money-col { width:22%; }
.badge { display:inline-block; padding:0 4px; border-radius:4px; background:#eef2ff; color:#4338ca; font-size:8px; font-weight:700; }
.empty { padding:15px; color:#64748b; text-align:center; }
.desc { margin-top:8px; padding:7px 9px; border:1px dashed #cbd5e1; border-radius:7px; color:#475569; white-space:pre-wrap; overflow-wrap:anywhere; }.desc span { font-weight:700; }
.grand-total { display:flex; justify-content:space-between; align-items:center; gap:12px; margin-top:9px; padding:9px 11px; border:1px solid #c7d2fe; border-radius:9px; background:#f5f6ff; }
.grand-total div { display:grid; gap:2px; }.grand-total span { color:#312e81; font-size:11px; font-weight:700; }.grand-total div small { color:#64748b; font-size:8px; }
.grand-total strong { color:#3730a3; font-size:15px; font-weight:700; text-align:left; overflow-wrap:anywhere; }.grand-total strong small { font-size:9px; }
.signatures { display:flex; gap:30px; margin-top:26px; }.signatures div { flex:1; height:32px; border-top:1px dashed #94a3b8; text-align:center; }.signatures span { position:relative; top:4px; color:#64748b; font-size:9px; }
footer { margin-top:10px; padding-top:7px; border-top:1px solid #e2e8f0; color:#64748b; text-align:center; font-size:8px; overflow-wrap:anywhere; }
footer div { padding:1px 0; } footer span { padding:0 4px; color:#cbd5e1; } footer .account-line { font-size:9px; font-weight:700; color:#312e81; }
.paper-a5 .meta { grid-template-columns:repeat(2,minmax(0,1fr)); }.paper-a5 tbody td,.paper-a5 thead th { padding:5px 3px; }
.purchase-header { display:grid; grid-template-columns:minmax(0,1fr) minmax(0,1.1fr) minmax(0,1fr); gap:8px; align-items:center; padding-bottom:12px; border-bottom:2px solid #303b4b; }
.company-logo { object-fit:contain; background:none; border:0; border-radius:0; }
.purchase-mark .company-logo { width:96px; height:64px; }
.brand-center:has(.company-logo) { grid-template-columns:90px minmax(0,1fr); }
.brand-center .company-logo { width:90px; height:60px; }
.purchase-mark { justify-self:start; }.purchase-title { text-align:center; min-width:0; }
.purchase-title .caption { font-size:15px; color:#182337; font-weight:700; }
.purchase-title .company { font-size:13px; }
.purchase-meta { min-width:0; width:100%; }
.purchase-meta .meta-item { display:flex; justify-content:flex-end; align-items:center; gap:7px; padding:2px 0; border:0; border-radius:0; background:none; }
.purchase-meta .meta-item span { white-space:nowrap; font-size:8px; }
.purchase-meta .meta-item strong { font-size:9px; white-space:nowrap; }
.purchase-contact { grid-column:1/-1; font-size:9px; color:#536278; }
.paper-a5 .purchase-meta .meta-item { gap:4px; }.paper-a5 .purchase-meta .meta-item span { font-size:6px; }.paper-a5 .purchase-meta .meta-item strong { font-size:7px; }
.paper-a5 .purchase-title .company { font-size:10px; }.paper-a5 .purchase-title .caption { font-size:12px; }
@media print {
 body { -webkit-print-color-adjust:exact; print-color-adjust:exact; }
 .sheet { width:__SHEET_W__; max-width:100%; margin:0 auto; }
 thead { display:table-header-group; }
 tr,.grand-total,.signatures { break-inside:avoid; page-break-inside:avoid; }
 .section-head { break-after:avoid; page-break-after:avoid; }
}
@media screen and (max-width:700px) { .sheet { width:100%; }.meta { grid-template-columns:repeat(2,minmax(0,1fr)); } }
";
}
