from pathlib import Path
import re,shutil
import os
root=Path(__file__).resolve().parents[2] / 'src/Inventory.Client'
out=Path(os.environ.get('FIXTURE_DIR', '/home/user/history-button-preview'));out.mkdir(parents=True, exist_ok=True)
for name in ['bootstrap.min.css','app.css']:shutil.copy(root/'wwwroot/css'/name,out/name)
shutil.copy(root/'wwwroot/fonts/Vazirmatn-Regular.woff2',out/'font.woff2')
css=(root/'Pages/Orders/OrderForm.razor.css').read_text().replace('::deep','')
base='''<!doctype html><html dir="rtl" lang="fa"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><link rel="stylesheet" href="bootstrap.min.css"><link rel="stylesheet" href="app.css"><style>@font-face{font-family:preview;src:url(font.woff2)}body{font-family:preview;padding:12px;background:#f1f5f9}.purchase-items{background:white}small{display:block;color:#64748b;font-size:11px;margin-top:4px}input{width:100%}h2{font-size:14px;margin:24px 0 12px}.demo-footer{background:white;margin-top:16px;border-radius:10px;padding:12px}.demo-footer .modal-footer{padding:0;border:0}.demo-footer .btn{font-family:inherit}'''+css+'</style><body>'
cols='<col style="width:calc(21.73913% - 2.391304rem)"><col style="width:calc(8.695652% - .956522rem)"><col style="width:calc(11.956522% - 1.315217rem)"><col style="width:calc(10.869565% - 1.195652rem)"><col style="width:calc(11.956522% - 1.315217rem)"><col style="width:calc(10.869565% - 1.195652rem)"><col style="width:calc(11.956522% - 1.315217rem)"><col style="width:calc(11.956522% - 1.315217rem)"><col style="width:11rem">'
# All numeric columns plus product and reserved action column, with normalized weights.
weights=[20,8,11,10,11,10,11,14,11];total=sum(weights)
cols=''.join(f'<col style="width:calc({100*w/total}% - {11*w/total}rem)">' for w in weights)+'<col style="width:11rem">'
labels=['کالا','مقدار','قیمت واحد','مبلغ کل','تخفیف','پس از تخفیف','ارزش افزوده','کل پس از ارزش افزوده','سود','']
row='<td data-label="کالا"><input class="order-line-control" value="P-0021 — a16l maofan" readonly><small>واحد: عدد · موجودی: ۲٫۵</small></td>'
for label in labels[1:-1]:row+=f'<td data-label="{label}"><input class="order-line-control" value="۰" readonly></td>'
row+='''<td><div class="order-line-actions"><button class="last-purchase-action" aria-label="آخرین فاکتور خرید این کالا، میانبر F8"><span>آخرین خرید</span><kbd dir="ltr">F8</kbd></button><button class="btn btn-sm btn-light" aria-label="حذف سطر">×</button></div></td>'''
row='<div class="purchase-items sale-items"><table><colgroup>'+cols+'</colgroup><thead><tr>'+''.join(f'<th>{l}</th>' for l in labels)+'</tr></thead><tbody><tr>'+row+'</tr></tbody></table></div>'
footer1='''<div class="demo-footer"><div class="modal-footer"><div class="invoice-footer-actions"><button class="btn btn-light">انصراف (Esc)</button><button class="btn btn-primary">ادامه پرداخت (F1) ←</button></div></div></div>'''
footer2='''<div class="demo-footer"><div class="modal-footer"><div class="invoice-footer-actions"><button class="btn btn-light">انصراف (Esc)</button><button class="btn btn-outline-secondary">بازگشت (F3)</button><label class="d-flex align-items-center gap-1 fs-8">کاغذ چاپ <select class="form-select form-select-sm w-auto"><option>تنظیم شرکت</option></select></label><button class="btn btn-outline-primary">ثبت و چاپ (F4)</button><button class="btn btn-primary">ثبت فاکتور فروش (F1)</button></div></div></div>'''
(out/'row.html').write_text(base+row+'<h2>مرحلهٔ اطلاعات فاکتور</h2>'+footer1+'<h2>مرحلهٔ پرداخت</h2>'+footer2+'</body></html>')
(out/'index.html').write_text(base+'''<h1 style="font-size:20px">دکمه‌های جمع‌وجور فاکتور</h1><p>پیش‌نمایش ظاهری با دادهٔ نمونه؛ نه اجرای زندهٔ Blazor.</p><h2>دسکتاپ</h2><div style="overflow:auto"><iframe src="row.html" style="border:0;width:100%;min-width:1100px;height:450px" title="دسکتاپ"></iframe></div><h2>موبایل</h2><iframe src="row.html" style="border:0;width:390px;max-width:100%;height:1100px" title="موبایل"></iframe></body></html>''')
