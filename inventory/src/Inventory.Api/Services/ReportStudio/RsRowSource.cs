using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.ReportStudio;

// =====================================================================
//  منبع سطرها — ساخت IQueryable از روی جدول‌ها و جوین‌های انتخابی
//
//  رویکرد: هر ترکیبِ مجازِ جدول‌ها به یک پروجکشن تخت (Dictionary مانند)
//  تبدیل می‌شود. به‌جای Expression-building پیچیده و شکننده، از
//  پروجکشن صریح به RsRow استفاده می‌کنیم که:
//   • کاملاً توسط EF Core ترجمه می‌شود (بدون client-evaluation)
//   • تایپ‌سیف است و در زمان کامپایل بررسی می‌شود
//   • خطاهای زمان اجرا را عملاً حذف می‌کند
//
//  درس گرفته از نسخهٔ قبل: هرگز Select().Concat() — همیشه ریشه از
//  جدول هاب و استفاده از نویگیشن‌ها.
// =====================================================================

/// <summary>یک سطر تخت‌شده — کلید = شناسهٔ ستون در کاتالوگ.</summary>
public sealed class RsRow
{
    public Dictionary<string, object?> V { get; set; } = new();
}

public interface IRsRowSource
{
    /// <summary>سطرهای خام را بر اساس ترکیب جدول‌های انتخابی می‌سازد.</summary>
    Task<List<RsRow>> FetchAsync(RsQueryDto q, int hardLimit, CancellationToken ct = default);
}

public sealed class RsRowSource : IRsRowSource
{
    private readonly AppDbContext _db;
    public RsRowSource(AppDbContext db) => _db = db;

    private static string Key(IEnumerable<string> tables) =>
        string.Join("+", tables.Select(t => t.ToLowerInvariant()).OrderBy(t => t));

    public async Task<List<RsRow>> FetchAsync(RsQueryDto q, int hardLimit, CancellationToken ct = default)
    {
        var tables = q.Tables.Select(t => t.TableKey).ToList();
        var shape = Key(tables);
        var leftJoin = q.Tables.Skip(1).All(t => t.JoinKind == RsJoinKind.Left);

        return shape switch
        {
            "letter" => await LettersAsync(hardLimit, ct),
            "erja" => await ErjasAsync(hardLimit, ct),
            "erja+letter" => await LetterErjaAsync(leftJoin, hardLimit, ct),
            "invoice" => await InvoicesAsync(hardLimit, ct),
            "invoice+invoice_line" => await InvoiceLinesAsync(leftJoin, hardLimit, ct),
            "invoice+party" => await InvoicePartyAsync(leftJoin, hardLimit, ct),
            "invoice+invoice_line+party" => await InvoiceLinePartyAsync(leftJoin, hardLimit, ct),
            "invoice_line+invoice+product" => await InvoiceLineProductAsync(leftJoin, hardLimit, ct),
            "invoice+invoice_line+party+product" => await InvoiceFullAsync(leftJoin, hardLimit, ct),
            "party" => await PartiesAsync(hardLimit, ct),
            "product" => await ProductsAsync(hardLimit, ct),
            _ => throw new RsUnsupportedShapeException(tables)
        };
    }

    // =====================================================================
    //  اتوماسیون اداری
    // =====================================================================

    /// <summary>پایهٔ نامه‌ها — ریشه از LetterSources تا حذف منطقی رعایت شود.</summary>
    private IQueryable<LetterProj> LetterBase()
    {
        var now = DateTime.Now;
        return _db.LetterSources
            .Where(x => !x.IsDelete
                && (x.InnerLetter != null || x.OutgoingLetter != null || x.IncomingLetter != null))
            .Select(x => new LetterProj
            {
                Id = x.Id,
                Type = x.SourceType,
                No = x.InnerLetter != null ? x.InnerLetter.LetterNumber
                   : x.OutgoingLetter != null ? (x.OutgoingLetter.SadereNumber ?? x.OutgoingLetter.LetterNumber)
                   : x.IncomingLetter!.LetterNumber,
                Title = x.InnerLetter != null ? x.InnerLetter.Title
                      : x.OutgoingLetter != null ? x.OutgoingLetter.Title
                      : x.IncomingLetter!.Title,
                Date = x.InnerLetter != null ? x.InnerLetter.DateSabt
                     : x.OutgoingLetter != null ? x.OutgoingLetter.DateSabt
                     : x.IncomingLetter!.DateErsal,
                DateClosed = x.OutgoingLetter != null ? x.OutgoingLetter.DateSadere
                           : x.IncomingLetter != null ? (DateTime?)x.IncomingLetter.Date : null,
                CreatorId = x.InnerLetter != null ? x.InnerLetter.CreatorUserId
                          : x.OutgoingLetter != null ? x.OutgoingLetter.CreatorUserId
                          : x.IncomingLetter!.CreateUserId,
                ConfText = x.InnerLetter != null ? x.InnerLetter.Mahramanegi
                         : x.OutgoingLetter != null ? x.OutgoingLetter.Mahramanegi : null,
                ConfNum = x.IncomingLetter != null ? (int?)x.IncomingLetter.Mahramanegi : null,
                UrgText = x.InnerLetter != null ? x.InnerLetter.Foriat
                        : x.OutgoingLetter != null ? x.OutgoingLetter.Foriat : null,
                UrgNum = x.IncomingLetter != null ? (int?)x.IncomingLetter.Foriat : null,
                Party = x.OutgoingLetter != null ? x.OutgoingLetter.ReceiverOrganization
                      : x.IncomingLetter != null ? x.IncomingLetter.Ferestande : null,
                OutStatus = x.OutgoingLetter != null ? (int?)x.OutgoingLetter.Status : null,
                InBayegani = x.IncomingLetter != null && x.IncomingLetter.IsBayegani,
                Method = x.InnerLetter != null ? "اتوماسیون"
                       : x.OutgoingLetter != null ? x.OutgoingLetter.SendMethod
                       : x.IncomingLetter!.TypeErsal,
                Starred = x.InnerLetter != null ? x.InnerLetter.IsNeshan
                        : x.OutgoingLetter != null ? x.OutgoingLetter.IsNeshan
                        : x.IncomingLetter!.IsNeshan,
                ErjaCount = x.Erjas.Count(e => !e.IsDelete),
                DaysOpen = x.OutgoingLetter != null && x.OutgoingLetter.DateSadere != null
                    ? (x.OutgoingLetter.DateSadere.Value - x.OutgoingLetter.DateSabt).TotalDays
                    : x.InnerLetter != null ? (now - x.InnerLetter.DateSabt).TotalDays
                    : x.OutgoingLetter != null ? (now - x.OutgoingLetter.DateSabt).TotalDays
                    : (now - x.IncomingLetter!.DateErsal).TotalDays
            });
    }

    private sealed class LetterProj
    {
        public int Id; public int Type; public string? No; public string? Title;
        public DateTime Date; public DateTime? DateClosed; public int CreatorId;
        public string? ConfText; public int? ConfNum; public string? UrgText; public int? UrgNum;
        public string? Party; public int? OutStatus; public bool InBayegani;
        public string? Method; public bool Starred; public int ErjaCount; public double DaysOpen;
    }

    private async Task<Dictionary<int, string>> UserNamesAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i > 0).Distinct().ToList();
        if (list.Count == 0) return new();
        return await _db.Users.Where(u => list.Contains(u.Id))
            .Select(u => new { u.Id, N = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim(), u.Username })
            .ToDictionaryAsync(u => u.Id, u => string.IsNullOrWhiteSpace(u.N) ? u.Username : u.N, ct);
    }

    private static string ConfFa(string? text, int? num) =>
        num is not null
            ? num switch { 0 => "عادی", 1 => "محرمانه", 2 => "خیلی محرمانه", _ => "سرّی" }
            : string.IsNullOrWhiteSpace(text) ? "عادی" : text.Trim();

    private static string UrgFa(string? text, int? num) =>
        num is not null
            ? num switch { 0 => "عادی", 1 => "فوری", 2 => "خیلی فوری", _ => "آنی" }
            : string.IsNullOrWhiteSpace(text) ? "عادی" : text.Trim();

    private static string StatusFa(int type, int? outStatus, bool bayegani) => type switch
    {
        1 => "ثبت‌شده",
        2 => outStatus switch
        {
            0 => "پیش‌نویس", 1 => "در گردش تایید", 2 => "تایید شده", 3 => "صادر شده", _ => "نامشخص"
        },
        _ => bayegani ? "بایگانی‌شده" : "در جریان"
    };

    private static void FillLetter(RsRow r, LetterProj p, Dictionary<int, string> users)
    {
        r.V["letter.id"] = p.Id;
        r.V["letter.type"] = p.Type;
        r.V["letter.no"] = p.No;
        r.V["letter.title"] = p.Title;
        r.V["letter.date"] = p.Date;
        r.V["letter.date_closed"] = p.DateClosed;
        r.V["letter.creator"] = users.GetValueOrDefault(p.CreatorId);
        r.V["letter.status"] = StatusFa(p.Type, p.OutStatus, p.InBayegani);
        r.V["letter.conf"] = ConfFa(p.ConfText, p.ConfNum);
        r.V["letter.urg"] = UrgFa(p.UrgText, p.UrgNum);
        r.V["letter.party"] = p.Party;
        r.V["letter.method"] = p.Method;
        r.V["letter.starred"] = p.Starred;
        r.V["letter.archived"] = p.InBayegani;
        r.V["letter.erja_count"] = p.ErjaCount;
        r.V["letter.days_open"] = Math.Round(p.DaysOpen, 1);
        r.V["letter.count"] = 1;
    }

    private async Task<List<RsRow>> LettersAsync(int limit, CancellationToken ct)
    {
        var raw = await LetterBase().Take(limit).ToListAsync(ct);
        var users = await UserNamesAsync(raw.Select(x => x.CreatorId), ct);
        return raw.Select(p => { var r = new RsRow(); FillLetter(r, p, users); return r; }).ToList();
    }

    private sealed class ErjaProj
    {
        public int Id; public int SourceId; public int SenderId; public int ReceiverId;
        public DateTime Date; public string Type = ""; public int Taeed;
        public bool IsRead; public string Answer = ""; public DateTime? Deadline; public string Note = "";
    }

    private IQueryable<ErjaProj> ErjaBase() =>
        _db.Erjas.Where(e => !e.IsDelete).Select(e => new ErjaProj
        {
            Id = e.ErjaId,
            SourceId = e.SourceId,
            SenderId = e.SenderUserId,
            ReceiverId = e.ReciverUserId,
            Date = e.Date,
            Type = e.Type,
            Taeed = e.TypeTaeed,
            IsRead = e.IsRead,
            Answer = e.Answer,
            Deadline = e.MohlatPasokh,
            Note = e.MatnErja
        });

    private static void FillErja(RsRow r, ErjaProj e, Dictionary<int, string> users, DateTime now)
    {
        var answered = !string.IsNullOrEmpty(e.Answer);
        r.V["erja.id"] = e.Id;
        r.V["erja.sender"] = users.GetValueOrDefault(e.SenderId);
        r.V["erja.receiver"] = users.GetValueOrDefault(e.ReceiverId);
        r.V["erja.date"] = e.Date;
        r.V["erja.type"] = e.Type;
        r.V["erja.taeed"] = e.Taeed;
        r.V["erja.is_read"] = e.IsRead;
        r.V["erja.answered"] = answered;
        r.V["erja.overdue"] = e.Deadline != null && e.Deadline < now && !answered;
        r.V["erja.deadline"] = e.Deadline;
        r.V["erja.note"] = e.Note;
        r.V["erja.count"] = 1;
    }

    private async Task<List<RsRow>> ErjasAsync(int limit, CancellationToken ct)
    {
        var now = DateTime.Now;
        var raw = await ErjaBase().Take(limit).ToListAsync(ct);
        var users = await UserNamesAsync(raw.SelectMany(x => new[] { x.SenderId, x.ReceiverId }), ct);
        return raw.Select(e => { var r = new RsRow(); FillErja(r, e, users, now); return r; }).ToList();
    }

    private async Task<List<RsRow>> LetterErjaAsync(bool left, int limit, CancellationToken ct)
    {
        var now = DateTime.Now;
        var letters = await LetterBase().Take(limit).ToListAsync(ct);
        var ids = letters.Select(l => l.Id).ToList();
        var erjas = await ErjaBase().Where(e => ids.Contains(e.SourceId)).ToListAsync(ct);

        var users = await UserNamesAsync(
            letters.Select(l => l.CreatorId)
                   .Concat(erjas.SelectMany(e => new[] { e.SenderId, e.ReceiverId })), ct);

        var byLetter = erjas.GroupBy(e => e.SourceId).ToDictionary(g => g.Key, g => g.ToList());
        var rows = new List<RsRow>();
        foreach (var l in letters)
        {
            var mine = byLetter.GetValueOrDefault(l.Id);
            if (mine is null || mine.Count == 0)
            {
                if (!left) continue;                 // Inner join: نامهٔ بدون ارجاع حذف می‌شود
                var r = new RsRow();
                FillLetter(r, l, users);
                rows.Add(r);
                continue;
            }
            foreach (var e in mine)
            {
                var r = new RsRow();
                FillLetter(r, l, users);
                FillErja(r, e, users, now);
                rows.Add(r);
                if (rows.Count >= limit) return rows;
            }
        }
        return rows;
    }

    // =====================================================================
    //  فروش و خرید
    // =====================================================================

    private sealed class InvProj
    {
        public int Id; public int Number; public int Kind; public int Status;
        public DateTime Date; public DateTime? Due; public string? PartyName; public int? PartyId;
        public string? Warehouse; public decimal Gross; public decimal Discount;
        public decimal Vat; public decimal Net;
    }

    private IQueryable<InvProj> InvoiceBase() =>
        _db.FacInvoices.Select(i => new InvProj
        {
            Id = i.Id,
            Number = i.Number,
            Kind = (int)i.Kind,
            Status = (int)i.Status,
            Date = i.Date,
            Due = i.DueDate,
            PartyId = i.PartyId,
            PartyName = i.Party != null ? i.Party.Name : null,
            Warehouse = i.Warehouse != null ? i.Warehouse.Name : null,
            Gross = i.TotalGross,
            Discount = i.TotalLineDiscount + i.InvoiceDiscount,
            Vat = i.TotalVat,
            Net = i.TotalNet
        });

    private static void FillInvoice(RsRow r, InvProj i)
    {
        r.V["invoice.id"] = i.Id;
        r.V["invoice.number"] = i.Number;
        r.V["invoice.kind"] = i.Kind;
        r.V["invoice.status"] = i.Status;
        r.V["invoice.date"] = i.Date;
        r.V["invoice.due"] = i.Due;
        r.V["invoice.party"] = i.PartyName;
        r.V["invoice.warehouse"] = i.Warehouse;
        r.V["invoice.gross"] = i.Gross;
        r.V["invoice.discount"] = i.Discount;
        r.V["invoice.vat"] = i.Vat;
        r.V["invoice.net"] = i.Net;
        r.V["invoice.count"] = 1;
    }

    private async Task<List<RsRow>> InvoicesAsync(int limit, CancellationToken ct)
    {
        var raw = await InvoiceBase().Take(limit).ToListAsync(ct);
        return raw.Select(i => { var r = new RsRow(); FillInvoice(r, i); return r; }).ToList();
    }

    private sealed class LineProj
    {
        public int InvoiceId; public int ProductId; public string? Product; public string? Code;
        public string? Category; public string? Unit; public decimal Qty; public decimal Price;
        public decimal Discount; public decimal Total;
    }

    private IQueryable<LineProj> LineBase() =>
        _db.FacInvoiceLines.Select(l => new LineProj
        {
            InvoiceId = l.InvoiceId,
            ProductId = l.ProductId,
            Product = l.Product != null ? l.Product.Name : null,
            Code = l.Product != null ? l.Product.Code : null,
            Category = l.Product != null ? l.Product.Category : null,
            Unit = l.Product != null ? l.Product.Unit : null,
            Qty = l.Quantity,
            Price = l.UnitPrice,
            Discount = l.Discount,
            Total = l.Total
        });

    private static void FillLine(RsRow r, LineProj l)
    {
        r.V["invoice_line.product"] = l.Product;
        r.V["invoice_line.product_code"] = l.Code;
        r.V["invoice_line.category"] = l.Category;
        r.V["invoice_line.unit"] = l.Unit;
        r.V["invoice_line.qty"] = l.Qty;
        r.V["invoice_line.price"] = l.Price;
        r.V["invoice_line.discount"] = l.Discount;
        r.V["invoice_line.total"] = l.Total;
        r.V["invoice_line.count"] = 1;
    }

    private async Task<List<(InvProj Inv, LineProj? Line)>> InvLinePairsAsync(
        bool left, int limit, CancellationToken ct)
    {
        var invs = await InvoiceBase().Take(limit).ToListAsync(ct);
        var ids = invs.Select(i => i.Id).ToList();
        var lines = await LineBase().Where(l => ids.Contains(l.InvoiceId)).ToListAsync(ct);
        var byInv = lines.GroupBy(l => l.InvoiceId).ToDictionary(g => g.Key, g => g.ToList());

        var pairs = new List<(InvProj, LineProj?)>();
        foreach (var i in invs)
        {
            var mine = byInv.GetValueOrDefault(i.Id);
            if (mine is null || mine.Count == 0)
            {
                if (left) pairs.Add((i, null));
                continue;
            }
            foreach (var l in mine)
            {
                pairs.Add((i, l));
                if (pairs.Count >= limit) return pairs;
            }
        }
        return pairs;
    }

    private async Task<List<RsRow>> InvoiceLinesAsync(bool left, int limit, CancellationToken ct)
    {
        var pairs = await InvLinePairsAsync(left, limit, ct);
        return pairs.Select(p =>
        {
            var r = new RsRow();
            FillInvoice(r, p.Inv);
            if (p.Line is not null) FillLine(r, p.Line);
            return r;
        }).ToList();
    }

    private sealed class PartyProj
    {
        public int Id; public string Name = ""; public string? Mobile;
        public string? Phone; public bool IsActive;
    }

    private IQueryable<PartyProj> PartyBase() =>
        _db.Parties.Select(p => new PartyProj
        {
            Id = p.Id, Name = p.Name, Mobile = p.Mobile, Phone = p.Phone, IsActive = p.IsActive
        });

    private static void FillParty(RsRow r, PartyProj p)
    {
        r.V["party.id"] = p.Id;
        r.V["party.name"] = p.Name;
        r.V["party.mobile"] = p.Mobile;
        r.V["party.phone"] = p.Phone;
        r.V["party.is_active"] = p.IsActive;
        r.V["party.count"] = 1;
    }

    private async Task<List<RsRow>> PartiesAsync(int limit, CancellationToken ct)
    {
        var raw = await PartyBase().Take(limit).ToListAsync(ct);
        return raw.Select(p => { var r = new RsRow(); FillParty(r, p); return r; }).ToList();
    }

    private sealed class ProductProj
    {
        public int Id; public string Name = ""; public string Code = ""; public string? Category;
        public string Unit = ""; public decimal Sale; public decimal Purchase; public bool IsActive;
    }

    private IQueryable<ProductProj> ProductBase() =>
        _db.Products.Select(p => new ProductProj
        {
            Id = p.Id, Name = p.Name, Code = p.Code, Category = p.Category,
            Unit = p.Unit, Sale = p.SalePrice, Purchase = p.PurchasePrice, IsActive = p.IsActive
        });

    private static void FillProduct(RsRow r, ProductProj p)
    {
        r.V["product.id"] = p.Id;
        r.V["product.name"] = p.Name;
        r.V["product.code"] = p.Code;
        r.V["product.category"] = p.Category;
        r.V["product.unit"] = p.Unit;
        r.V["product.sale_price"] = p.Sale;
        r.V["product.purchase_price"] = p.Purchase;
        r.V["product.is_active"] = p.IsActive;
        r.V["product.count"] = 1;
    }

    private async Task<List<RsRow>> ProductsAsync(int limit, CancellationToken ct)
    {
        var raw = await ProductBase().Take(limit).ToListAsync(ct);
        return raw.Select(p => { var r = new RsRow(); FillProduct(r, p); return r; }).ToList();
    }

    private async Task<Dictionary<int, PartyProj>> PartyMapAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new();
        return await PartyBase().Where(p => list.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
    }

    private async Task<Dictionary<int, ProductProj>> ProductMapAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new();
        return await ProductBase().Where(p => list.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
    }

    private async Task<List<RsRow>> InvoicePartyAsync(bool left, int limit, CancellationToken ct)
    {
        var invs = await InvoiceBase().Take(limit).ToListAsync(ct);
        var parties = await PartyMapAsync(invs.Where(i => i.PartyId != null).Select(i => i.PartyId!.Value), ct);
        var rows = new List<RsRow>();
        foreach (var i in invs)
        {
            PartyProj? pp = i.PartyId != null ? parties.GetValueOrDefault(i.PartyId.Value) : null;
            if (pp is null && !left) continue;
            var r = new RsRow();
            FillInvoice(r, i);
            if (pp is not null) FillParty(r, pp);
            rows.Add(r);
        }
        return rows;
    }

    private async Task<List<RsRow>> InvoiceLinePartyAsync(bool left, int limit, CancellationToken ct)
    {
        var pairs = await InvLinePairsAsync(left, limit, ct);
        var parties = await PartyMapAsync(
            pairs.Where(p => p.Inv.PartyId != null).Select(p => p.Inv.PartyId!.Value), ct);

        var rows = new List<RsRow>();
        foreach (var (inv, line) in pairs)
        {
            PartyProj? pp = inv.PartyId != null ? parties.GetValueOrDefault(inv.PartyId.Value) : null;
            if (pp is null && !left) continue;
            var r = new RsRow();
            FillInvoice(r, inv);
            if (line is not null) FillLine(r, line);
            if (pp is not null) FillParty(r, pp);
            rows.Add(r);
        }
        return rows;
    }

    private async Task<List<RsRow>> InvoiceLineProductAsync(bool left, int limit, CancellationToken ct)
    {
        var pairs = await InvLinePairsAsync(left, limit, ct);
        var products = await ProductMapAsync(
            pairs.Where(p => p.Line != null).Select(p => p.Line!.ProductId), ct);

        var rows = new List<RsRow>();
        foreach (var (inv, line) in pairs)
        {
            ProductProj? pr = line != null ? products.GetValueOrDefault(line.ProductId) : null;
            if (pr is null && !left) continue;
            var r = new RsRow();
            FillInvoice(r, inv);
            if (line is not null) FillLine(r, line);
            if (pr is not null) FillProduct(r, pr);
            rows.Add(r);
        }
        return rows;
    }

    private async Task<List<RsRow>> InvoiceFullAsync(bool left, int limit, CancellationToken ct)
    {
        var pairs = await InvLinePairsAsync(left, limit, ct);
        var parties = await PartyMapAsync(
            pairs.Where(p => p.Inv.PartyId != null).Select(p => p.Inv.PartyId!.Value), ct);
        var products = await ProductMapAsync(
            pairs.Where(p => p.Line != null).Select(p => p.Line!.ProductId), ct);

        var rows = new List<RsRow>();
        foreach (var (inv, line) in pairs)
        {
            var r = new RsRow();
            FillInvoice(r, inv);
            if (line is not null) FillLine(r, line);
            if (inv.PartyId != null && parties.TryGetValue(inv.PartyId.Value, out var pp)) FillParty(r, pp);
            if (line != null && products.TryGetValue(line.ProductId, out var pr)) FillProduct(r, pr);
            rows.Add(r);
        }
        return rows;
    }
}

/// <summary>ترکیب جدول‌ها پشتیبانی نمی‌شود — با پیام راهنما به کاربر.</summary>
public sealed class RsUnsupportedShapeException : Exception
{
    public RsUnsupportedShapeException(IEnumerable<string> tables)
        : base("این ترکیب از جدول‌ها هنوز پشتیبانی نمی‌شود: " +
               string.Join(" + ", tables.Select(t => RsSchemaCatalog.FindTable(t)?.Title ?? t)) +
               ". لطفاً جدول‌ها را طبق مسیرهای جوین پیشنهادی انتخاب کنید.")
    { }
}
