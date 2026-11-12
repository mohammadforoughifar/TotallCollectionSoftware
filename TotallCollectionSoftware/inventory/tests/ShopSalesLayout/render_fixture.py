"""Browser fixture from the actual embedded production template/CSS.
This does NOT execute the C# builder or simulate a live database invoice.
"""
from pathlib import Path
import base64
import html
import re
ROOT=Path(__file__).resolve().parents[3]
SOURCE=ROOT/'inventory/src/Inventory.Client/Pages/Orders/ShopSaleInvoicePrint.cs'
# ROOT is the outer TotallCollectionSoftware application directory.
def fa(value):return f'{value:,}'.translate(str.maketrans('0123456789','۰۱۲۳۴۵۶۷۸۹'))
def build(paper='A4',many=False):
    source=SOURCE.read_text()
    template=re.search(r'public const string Template = """\n(.*?)\n""";',source,re.S)[1]
    css=re.search(r'public const string Style = """\n(.*?)\n""";',source,re.S)[1]
    assets=ROOT.parent/'docs/prototypes/shop-invoice-print/assets'
    for file in ['Vazirmatn-Regular.woff2','Vazirmatn-Bold.woff2']:
        data=base64.b64encode((assets/file).read_bytes()).decode()
        css=css.replace('__FONT_BASE__fonts/'+file,'data:font/woff2;base64,'+data)
    css=css.replace('__PAGE_SIZE__',paper).replace('__PAGE_MARGIN__','6mm' if paper=='A5' else '10mm')
    lines=[('کارت شبکه',5,100000,5000),('مانیتور',52100,221000,0)]
    if many:lines=lines*24
    gross=sum(q*p for _,q,p,_ in lines);discount=sum(d for *_,d in lines);net=gross-discount
    items=''.join('<tr><td>'+fa(i+1)+'</td><td>'+html.escape(name)+'</td><td>'+fa(q)+'</td>'+''.join('<td class="money">'+fa(v)+'</td>' for v in [price,q*price,d,q*price-d])+'</tr>' for i,(name,q,price,d) in enumerate(lines))
    totalRows=''.join('<div'+(' class="payable"' if id=='payable' else '')+'><dt>'+label+'</dt><dd id="'+id+'">'+fa(v)+'</dd></div>' for label,v,id in [('جمع بهای کالاها',gross,'gross'),('جمع تخفیف',discount,'discount'),('پس از تخفیف',net,'net'),('مالیات و عوارض',0,'vat'),('مبلغ نهایی فاکتور · ریال',net,'payable')])
    logo='data:image/png;base64,'+base64.b64encode((assets/'company-logo.png').read_bytes()).decode()
    tokens={'CSS':css,'PAPER_CLASS':'a5' if paper=='A5' else '', 'LOGO':'<img id="invoice-logo" alt="لوگوی شرکت" src="'+logo+'">', 'COMPANY':'فروغ آریا','NUMBER':'SL-0040','DATE':'۱۴۰۵/۰۸/۱۸','PARTY':'آقا موسی','PHONE':'-','ADDRESS':'-','TAX_ID':'-','COUNT':fa(len(lines)),'QUANTITY':fa(sum(q for _,q,*_ in lines)),'ITEMS':items,'TOTALS':totalRows,'BANKS':'<div class="printed-bank-entry"><div><span>شماره کارت</span><bdi>1234 5678 9012 3456</bdi></div></div>','COMPANY_PHONE':'۰۲۱–۰۰۰۰۰۰۰۰','COMPANY_ADDRESS':'-','NOTES':'فاکتور SL-0040 برای «آقا موسی»؛ مبلغ نهایی '+fa(net)+' ریال.','PAYMENTS':'<section class="settlement-details"><h3 id="payment-method">تسویهٔ نقدی</h3><div id="settlement-breakdown" style="--parts:1"><div><span>نقدی</span><strong>'+fa(net)+' ریال</strong></div></div></section>','SCHEDULE':'','CANCELLED':'','SIGNATURES':'<section class="signatures" id="signatures"><div><strong>امضای خریدار</strong><span>نام و امضا</span></div><div><strong>امضا و مهر فروشنده</strong><span>نام، مهر و امضا</span></div></section>'}
    return re.sub(r'__([A-Z_]+)__',lambda m:tokens.get(m[1],m[0]),template)
if __name__=='__main__':
    output=Path('/home/user/outputs');output.mkdir(exist_ok=True)
    for paper in ['A4','A5']:(output/f'shop-sales-layout-{paper}.html').write_text(build(paper))
    (output/'shop-sales-layout-long.html').write_text(build('A4',True))
