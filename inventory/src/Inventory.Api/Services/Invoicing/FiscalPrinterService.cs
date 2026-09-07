using System.Globalization;
using System.Net.Sockets;
using System.Text;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Db = Inventory.Api.Data;

namespace Inventory.Api.Services.Invoicing;

/// <summary>
/// چاپگر مالی/حرارتی:
///   • تنظیمات چاپگر (نام، پورت فایل/دستگاه/شبکه، عرض کاغذ، تکراری‌ها، سربرگ/پابرگ)
///   • ساخت رسید چاپی استاندارد فاکتور الکترونیکی (متن + پیش‌نمایش HTML + QR)
///   • خروجی ESC/POS به دستگاه یا فایل؛ بدون پورت = حالت شبیه‌سازی (فقط پیش‌نمایش)
/// </summary>
public interface IFiscalPrinterService
{
    Task<FiscalPrinterSetting> GetSettingAsync();
    Task<FiscalPrinterSetting> SaveSettingAsync(FiscalPrinterSetting dto);
    Task<FiscalPrintResult> RenderAsync(int invoiceId);
    Task<FiscalPrintResult> PrintAsync(int invoiceId);
}

public class FiscalPrinterService : IFiscalPrinterService
{
    private readonly Db.AppDbContext _db;
    private readonly ILogger<FiscalPrinterService> _log;

    public FiscalPrinterService(Db.AppDbContext db, ILogger<FiscalPrinterService> log)
    {
        _db = db; _log = log;
    }

    public async Task<FiscalPrinterSetting> GetSettingAsync()
    {
        var s = await _db.FiscalPrinterSettings.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync()
                ?? new Db.FiscalPrinterSetting();
        return new FiscalPrinterSetting
        {
            Id = s.Id, PrinterName = s.PrinterName, PortName = s.PortName, PaperWidthMm = s.PaperWidthMm,
            Copies = s.Copies, HeaderLines = s.HeaderLines, FooterLines = s.FooterLines,
            CutPaper = s.CutPaper, Enabled = s.Enabled, Notes = s.Notes
        };
    }

    public async Task<FiscalPrinterSetting> SaveSettingAsync(FiscalPrinterSetting dto)
    {
        var entity = await _db.FiscalPrinterSettings.OrderBy(x => x.Id).FirstOrDefaultAsync();
        if (entity is null)
        {
            entity = new Db.FiscalPrinterSetting();
            _db.FiscalPrinterSettings.Add(entity);
        }
        entity.PrinterName = string.IsNullOrWhiteSpace(dto.PrinterName) ? "چاپگر حرارتی" : dto.PrinterName;
        entity.PortName = string.IsNullOrWhiteSpace(dto.PortName) ? null : dto.PortName.Trim();
        entity.PaperWidthMm = dto.PaperWidthMm is 58 or 80 ? dto.PaperWidthMm : 80;
        entity.Copies = Math.Clamp(dto.Copies, 1, 5);
        entity.HeaderLines = dto.HeaderLines;
        entity.FooterLines = dto.FooterLines;
        entity.CutPaper = dto.CutPaper;
        entity.Enabled = dto.Enabled;
        entity.Notes = dto.Notes;
        await _db.SaveChangesAsync();
        dto.Id = entity.Id;
        return dto;
    }

    /// <summary>ساخت رسید (بدون ارسال به دستگاه).</summary>
    public async Task<FiscalPrintResult> RenderAsync(int invoiceId) => await BuildTicketAsync(invoiceId);

    /// <summary>چاپ رسید روی دستگاه/فایل؛ بدون پورت = شبیه‌سازی.</summary>
    public async Task<FiscalPrintResult> PrintAsync(int invoiceId)
    {
        var setting = await _db.FiscalPrinterSettings.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync()
                      ?? new Db.FiscalPrinterSetting();
        if (!setting.Enabled)
            throw new InvalidOperationException("چاپگر غیرفعال است؛ از تنظیمات چاپگر مالی فعال کنید.");

        var ticket = await BuildTicketAsync(invoiceId);
        var port = setting.PortName;

        if (string.IsNullOrWhiteSpace(port))
        {
            ticket.Success = true;
            ticket.Simulated = true;
            ticket.Message = "پورت چاپگر تنظیم نشده — پیش‌نمایش رسید (حالت شبیه‌سازی)";
            return ticket;
        }

        try
        {
            var data = BuildPosBytes(ticket.TicketText, setting.Copies, setting.CutPaper);

            if (port.StartsWith("FILE:", StringComparison.OrdinalIgnoreCase))
            {
                var path = port[5..].Trim();
                File.WriteAllBytes(path, data);
                ticket.Success = true;
                ticket.Message = $"رسید در {path} ذخیره شد ({data.Length} بایت).";
                return ticket;
            }

            if (port.Contains(':'))
            {
                var parts = port.Split(':');
                using var client = new TcpClient();
                await client.ConnectAsync(parts[0], int.Parse(parts[1]));
                var stream = client.GetStream();
                await stream.WriteAsync(data);
                await stream.FlushAsync();
                ticket.Success = true;
                ticket.Message = $"به چاپگر {port} ارسال شد ({data.Length} بایت).";
                return ticket;
            }

            // مسیر دستگاه/فایل (Linux: /dev/usb/lp0 — ویندوز: از FILE: استفاده کنید)
            File.WriteAllBytes(port, data);
            ticket.Success = true;
            ticket.Message = $"به دستگاه {port} ارسال شد ({data.Length} بایت).";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "چاپ مالی ناموفق برای فاکتور {Id} روی {Port}", invoiceId, port);
            ticket.Success = false;
            ticket.Simulated = true;
            ticket.Message = $"خطا در ارسال به چاپگر ({port}): {ex.Message} — پیش‌نمایش آماده است.";
        }
        return ticket;
    }

    // =====================================================================
    private async Task<FiscalPrintResult> BuildTicketAsync(int invoiceId)
    {
        var inv = await _db.MoadianInvoices.AsNoTracking()
            .Include(i => i.Lines).Include(i => i.FiscalPeriod)
            .FirstOrDefaultAsync(i => i.Id == invoiceId)
            ?? throw new InvalidOperationException("فاکتور الکترونیکی یافت نشد.");
        var setting = await _db.FiscalPrinterSettings.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync()
                      ?? new Db.FiscalPrinterSetting();

        var (y, m, d) = PersianDate.FromGregorian(inv.Date);
        var faDate = $"{Fa.Digits(y)}/{Fa.Digits(m.ToString("00"))}/{Fa.Digits(d.ToString("00"))}";
        var kindFa = inv.Kind switch
        {
            MoadianInvoiceKind.Sale => "فاکتور فروش",
            MoadianInvoiceKind.SaleReturn => "فاکتور برگشت از فروش",
            MoadianInvoiceKind.Purchase => "فاکتور خرید",
            MoadianInvoiceKind.PurchaseReturn => "فاکتور برگشت از خرید",
            _ => "پیش‌فاکتور"
        };

        var sb = new StringBuilder();
        // ----- سربرگ -----
        if (!string.IsNullOrWhiteSpace(setting.HeaderLines)) sb.AppendLine(setting.HeaderLines);
        sb.AppendLine(Center($"* {inv.SellerName} *"));
        sb.AppendLine(Center($"شناسه مالیاتی: {inv.TaxId}"));
        if (!string.IsNullOrWhiteSpace(inv.EconomicCode)) sb.AppendLine(Center($"کد اقتصادی: {inv.EconomicCode}"));
        sb.AppendLine(Divider());
        sb.AppendLine(Center($"** {kindFa} **"));
        sb.AppendLine(Center($"شماره: {Fa.Digits(inv.Number.ToString("D6"))}   تاریخ: {faDate}"));
        if (inv.TrackingId is not null) sb.AppendLine(Center($"پیگیری: {inv.TrackingId}"));
        if (inv.ReferenceId is not null) sb.AppendLine(Center($"REF: {inv.ReferenceId}"));
        sb.AppendLine(Center($"وضعیت: {StatusFa(inv.Status)}"));
        sb.AppendLine(Divider());

        // ----- خریدار -----
        sb.AppendLine($"خریدار: {inv.BuyerName ?? "—"}");
        if (!string.IsNullOrWhiteSpace(inv.BuyerTaxId)) sb.AppendLine($"شناسه/کد ملی: {inv.BuyerTaxId}");
        if (!string.IsNullOrWhiteSpace(inv.BuyerAddress)) sb.AppendLine($"آدرس: {inv.BuyerAddress}");
        sb.AppendLine(Divider());

        // ----- اقلام -----
        foreach (var l in inv.Lines.OrderBy(x => x.RowNo))
        {
            sb.AppendLine(l.SstTitle);
            sb.AppendLine($"   {Fa.Money(l.UnitPrice)} × {Qty(l.Quantity)}  =  {Fa.Money(l.Total)}");
        }
        sb.AppendLine(Divider());

        sb.AppendLine(Row("جمع ناخالص", inv.TotalGross));
        if (inv.TotalDiscount > 0) sb.AppendLine(Row("تخفیف", -inv.TotalDiscount));
        sb.AppendLine(Row("مبلغ مشمول مالیات", inv.TotalTaxable));
        sb.AppendLine(Row("مالیات بر ارزش افزوده", inv.TotalVat));
        sb.AppendLine(Row("*** مبلغ قابل پرداخت ***", inv.TotalNet));
        sb.AppendLine(Divider());

        // ----- QR -----
        var qr = QrContent(inv);

        // ----- پابرگ -----
        if (!string.IsNullOrWhiteSpace(setting.FooterLines))
        {
            sb.AppendLine();
            sb.AppendLine(setting.FooterLines);
        }
        sb.AppendLine(Center("*** سپاس از خرید شما ***"));

        var html = BuildPreviewHtml(inv, kindFa, faDate, qr);
        return new FiscalPrintResult
        {
            Success = true,
            Message = "رسید آماده چاپ است.",
            TicketText = sb.ToString(),
            PreviewHtml = html,
            QrContent = qr
        };
    }

    private static string QrContent(Db.MoadianInvoice inv)
    {
        // JSON استاندارد QR فاکتور الکترونیکی (برای استعلام در سامانه)
        // {"m":{"i":<taxid>,"n":<number>,"d":<yyyyMMdd>,"f":<pattern>},"s":<uid>}
        var inner = $"{{\"i\":\"{inv.TaxId}\",\"n\":\"{inv.Number}\",\"d\":\"{inv.Date:yyyyMMdd}\",\"f\":{(int)inv.Kind}}}";
        return $"{{\"m\":{inner},\"s\":\"{inv.ReferenceId ?? string.Empty}\"}}";
    }

    private static byte[] BuildPosBytes(string ticket, int copies, bool cut)
    {
        var list = new List<byte>();
        for (var c = 0; c < Math.Max(1, copies); c++)
        {
            list.AddRange(new byte[] { 0x1B, 0x40 });                     // init
            list.AddRange(Encoding.UTF8.GetBytes(ticket));
            list.AddRange(new byte[] { 0x0A, 0x0A, 0x0A });                // feed 3
            if (cut) list.AddRange(new byte[] { 0x1D, 0x56, 0x42, 0x00 }); // cut (partial)
        }
        return list.ToArray();
    }

    // ------------------------------ helpers ------------------------------
    private static string Divider() => new('=', 42);

    private static string Center(string s) => s;

    private static string Row(string label, decimal v) =>
        $"{label} ............ {Fa.Money(v)}";

    private static string Qty(decimal q) =>
        Fa.Digits(q.ToString(q == decimal.Truncate(q) ? "0" : "0.###", CultureInfo.InvariantCulture));

    private static string StatusFa(MoadianInvoiceStatus s) => s switch
    {
        MoadianInvoiceStatus.Draft => "پیش‌نویس",
        MoadianInvoiceStatus.Queued => "در صف ارسال",
        MoadianInvoiceStatus.Sending => "در حال ارسال",
        MoadianInvoiceStatus.Sent => "ارسال‌شده",
        MoadianInvoiceStatus.Returned => "برگشتی از سامانه",
        MoadianInvoiceStatus.Failed => "ناموفق",
        MoadianInvoiceStatus.Voided => "ابطال‌شده",
        _ => ""
    };

    private static string BuildPreviewHtml(Db.MoadianInvoice inv, string kindFa, string faDate, string qr)
    {
        var rows = new StringBuilder();
        foreach (var l in inv.Lines.OrderBy(x => x.RowNo))
        {
            rows.Append("<tr>")
                .Append("<td style='text-align:right;padding:1px 2px'>").Append(ServerH(l.SstTitle)).Append("</td>")
                .Append("<td style='text-align:left;padding:1px 2px' dir='ltr'>").Append(Qty(l.Quantity)).Append("</td>")
                .Append("<td style='text-align:left;padding:1px 2px' dir='ltr'>").Append(Fa.Digits(l.UnitPrice.ToString("N2", CultureInfo.InvariantCulture))).Append("</td>")
                .Append("<td style='text-align:left;padding:1px 2px' dir='ltr'>").Append(Fa.Digits(l.Total.ToString("N2", CultureInfo.InvariantCulture))).Append("</td>")
                .Append("</tr>");
        }

        return "<div style='direction:rtl;font-family:Tahoma,Arial,sans-serif;font-size:11px;color:#111;max-width:300px;margin:0 auto;border:1px dashed #999;padding:10px;background:#fff'>"
            + "<div style='text-align:center;font-weight:bold;font-size:13px'>" + ServerH(inv.SellerName) + "</div>"
            + "<div style='text-align:center'>شماره مالیاتی: " + ServerH(inv.TaxId) + "</div>"
            + "<div style='text-align:center'>کد اقتصادی: " + ServerH(inv.EconomicCode ?? "—") + "</div>"
            + "<div style='border-top:1px dashed #999;margin:6px 0'></div>"
            + "<div style='text-align:center;font-weight:bold'>" + kindFa + " — " + Fa.Digits(inv.Number.ToString("D6")) + "</div>"
            + "<div style='text-align:center'>تاریخ: " + faDate + "</div>"
            + "<div style='text-align:center'>وضعیت: " + StatusFa(inv.Status) + "</div>"
            + "<div style='border-top:1px dashed #999;margin:6px 0'></div>"
            + "<div>خریدار: " + ServerH(inv.BuyerName ?? "—") + "</div>"
            + "<div>شناسه: " + ServerH(inv.BuyerTaxId ?? "—") + "</div>"
            + "<div style='border-top:1px dashed #999;margin:6px 0'></div>"
            + "<table style='width:100%;border-collapse:collapse'><tr><th style='text-align:right'>شرح</th><th>تعداد</th><th>فی</th><th>جمع</th></tr>"
            + rows + "</table>"
            + "<div style='border-top:1px dashed #999;margin:6px 0'></div>"
            + "<div>جمع ناخالص: <b>" + Fa.Money(inv.TotalGross) + "</b></div>"
            + "<div>مالیات: <b>" + Fa.Money(inv.TotalVat) + "</b></div>"
            + "<div style='font-size:14px;font-weight:bold;text-align:left;margin-top:4px'>قابل پرداخت: " + Fa.Money(inv.TotalNet) + " ریال</div>"
            + "<div style='border-top:1px dashed #999;margin:8px 0'></div>"
            + "<div style='text-align:center;font-size:10px'>*** سپاس از خرید شما ***</div>"
            + "</div>";
    }

    private static string ServerH(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");
}
