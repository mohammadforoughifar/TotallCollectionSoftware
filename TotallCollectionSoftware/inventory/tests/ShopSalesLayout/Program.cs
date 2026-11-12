using Inventory.Client.Pages.Orders;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Inventory.Client.Extensions;
var order = new Order { Type=TransactionType.Sale, Number="SL-0040", Date=new DateTime(2026,11,9), PartyName="<script>alert(1)</script>", PartyPhone="09123456789", PartyAddress="address", PartyTaxId="123", TotalAmount=11514595000m, CashAmount=11514595000m, PaymentMethod=PaymentMethod.Cash,
 Lines=new() { new() { Id=1, ProductName="Item A",Quantity=5, Price=100000,DiscountAmount=5000 }, new() { Id=2,ProductName="Item B",Quantity=52100, Price=221000 } } };
var profile=new CompanyProfileDto { Name="Company",Phone="02112345678",Address="Company address" };
var settings=new ShopPrintSettingsDto { Banks=new() { new() { Card="1234567890123456",Iban="123456789012345678901234" } } };
foreach(var paper in new[]{"A4","A5"}) {
 var html=ShopSaleInvoicePrint.Build(order,profile,paper,settings);
 if(!html.Contains(order.TotalAmount.MoneyExact()) || !html.Contains("1234 5678 9012 3456"))throw new Exception("Invoice/bank amounts lost");
 if(!html.Contains("&lt;script&gt;")||html.Contains("<script>alert(1)</script>"))throw new Exception("HTML injection");
 if(html.Contains("invoice-stamp")||html.Contains("grand-total")||html.Contains("اقلام سند"))throw new Exception("Legacy layout used");
 File.WriteAllText(Path.Combine(AppContext.BaseDirectory,$"sales-{paper}.html"),html);
}
order.CashAmount=100;order.TotalAmount=1000;order.PaymentMethod=PaymentMethod.Mixed;order.DueDate=new DateTime(2026,12,1);order.Cheques.Add(new(){Number="C1",Amount=200,Status=ChequeStatus.Bounced,DueDate=new DateTime(2026,12,1)});order.Installments.Add(new(){No=1,Amount=300,IsPaid=false});
var mixed=ShopSaleInvoicePrint.Build(order,profile,"A4",settings);
if(!mixed.Contains("برگشت‌خورده")||!mixed.Contains("اقساط ثبت‌شده")||!mixed.Contains("نسیه هنگام ثبت"))throw new Exception("Payment data missing");
order.IsCancelled=true;order.ReturnedGoodsAmount=50;order.ChequeIncomeAmount=25;
var corrected=ShopSaleInvoicePrint.Build(order,profile);
if(!corrected.Contains("این فاکتور ابطال شده است")||!corrected.Contains("برگشت کالا و مالیات")||!corrected.Contains("تاخیر تادیه چک"))throw new Exception("Financial adjustments hidden");
Console.WriteLine("PASS shop sales layout, encoding, exact amounts, bank data and payment/return states");

order.Type=TransactionType.Purchase;
foreach(var paper in new[]{"A4","A5"}) {
 var html=ShopPurchaseInvoicePrint.Build(order,profile,paper);
 if(!html.Contains("فاکتور خرید")||!html.Contains("بهای واحد خرید")||!html.Contains("امضای مسئول خرید")||!html.Contains("شماره فاکتور تأمین‌کننده"))throw new Exception("Incomplete purchase template");
 if(html.Contains("<section class=\"bank-details\"")||html.Contains("در انتظار وصول"))throw new Exception("Sales-only data in purchase");
 File.WriteAllText(Path.Combine(AppContext.BaseDirectory,$"purchase-{paper}.html"),html);
 var dispatch=new GoodsIssueDto { Number="HI-0040",Date=order.Date,PartyName="<recipient>",Description="saved note",Lines=new(){new(){ProductName="Item",Quantity=1.25m,Note="remark"}}};
 var note=Inventory.Client.Pages.Catalog.GoodsIssuesPrint.Build(dispatch,profile.Name,paper);
 if(!note.Contains("&lt;recipient&gt;")||!note.Contains("saved note")||!note.Contains("remark")||note.Contains("<section class=\"settlement\""))throw new Exception("Incorrect dispatch template");
 File.WriteAllText(Path.Combine(AppContext.BaseDirectory,$"dispatch-{paper}.html"),note);
}
Console.WriteLine("PASS complete purchase and dispatch templates, headers, saved values and escaping");
