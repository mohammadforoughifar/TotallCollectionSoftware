'use strict';
// Isolated design prototype: no API, no real company settings, no persistence.
const $ = id => document.getElementById(id);
const money = value => new Intl.NumberFormat('fa-IR', {maximumFractionDigits: 0}).format(value);
// Mock of the invoice-linked customer read model; no customer settings inputs.
// Production integration must populate this from authorized invoice/customer data.
const sampleInvoice = {number:'۱۴۰۵–۰۰۲۱۸', date:'۱۴۰۵/۰۸/۲۰', paymentMethod:'نقدی',
 customer:{name:'عقاب افشان — نمونه',phone:'۰۹۱۲–۰۰۰۰۰۰۰',id:'',address:'نشانی نمونهٔ مشتریِ مرتبط با همین فاکتور'}};
const lines = [
 {name:'کارت شبکه', qty:6, price:2500000, discount:0, note:'مدل نمونه'},
 {name:'اسپیکر کامپیوتر', qty:5, price:6800000, discount:1000000, note:'گارانتی طبق سند'},
 {name:'دوربین مداربسته', qty:5, price:24500000, discount:4000000, note:'مدل نمونه'},
 {name:'دانگل HDMI', qty:1, price:3900000, discount:0, note:'—'},
 {name:'مانیتور سامسونگ', qty:2, price:85000000, discount:5000000, note:'شماره سریال در سند'},
 {name:'کابل HDMI پنج متری', qty:5, price:1800000, discount:0, note:'—'}
];
const totals = lines.reduce((t,l) => ({gross:t.gross+l.qty*l.price,discount:t.discount+l.discount,qty:t.qty+l.qty}), {gross:0,discount:0,qty:0});
const net = totals.gross-totals.discount;
const vat = net*0.1; // Explicitly hypothetical demo rate, NOT a production tax rule.
$('item-rows').replaceChildren(...lines.map((l,i)=>{
 const row=document.createElement('tr');
 [money(i+1),l.name,money(l.qty),money(l.price),money(l.qty*l.price),l.note].forEach((value,j)=>{
  const cell=document.createElement('td');cell.textContent=value;
  if(j===3||j===4)cell.className='money';
  if(j===5)cell.className='remarks';
  row.append(cell);
 });return row;
}));
for(const [id,value] of Object.entries({gross:totals.gross,discount:totals.discount,net,vat,payable:net+vat,'quantity-total':totals.qty}))$(id).textContent=money(value);
const invoice=$('invoice');
function fitPaper(){
 const viewport=document.querySelector('.paper-viewport');
 invoice.style.transform='';viewport.style.height='';
 if(window.innerWidth<=800){
  const scale=Math.min(1,viewport.clientWidth/invoice.offsetWidth);
  invoice.style.transform=`scale(${scale})`;invoice.style.transformOrigin='top right';
  viewport.style.height=`${Math.ceil(invoice.offsetHeight*scale)+20}px`;
 }
}
let bankRowNumber=0;
function addBankRow(){
 const id=++bankRowNumber;
 const row=document.createElement('div');row.className='bank-entry';
 row.innerHTML=`<div class="bank-entry-head"><strong>اطلاعات پرداخت</strong><button type="button" class="remove-bank" aria-label="حذف این کارت و شبا">حذف</button></div><label for="card-${id}">شماره کارت<input id="card-${id}" class="bank-card" dir="ltr" inputmode="numeric" placeholder="۱۶ رقم" aria-describedby="card-error-${id}"><small id="card-error-${id}" class="field-error card-error" aria-live="polite"></small></label><label for="iban-${id}">شماره شبا<input id="iban-${id}" class="bank-iban" dir="ltr" placeholder="IR…" aria-describedby="iban-error-${id}"><small id="iban-error-${id}" class="field-error iban-error" aria-live="polite"></small></label>`;
 row.querySelector('button').addEventListener('click',()=>{row.remove();update();});
 $('bank-rows').append(row);update();
}
function updateBanks(){
 const seenCards=new Set(),seenIbans=new Set();let invalid=false;const valid=[];
 for(const row of document.querySelectorAll('.bank-entry')){
  const cardInput=row.querySelector('.bank-card'),ibanInput=row.querySelector('.bank-iban');
  const card=ShopPrintBankValidation.card(cardInput.value),iban=ShopPrintBankValidation.iban(ibanInput.value);
  if(card.value&&!card.error&&seenCards.has(card.value))card.error='این شماره کارت تکراری است.';
  if(iban.value&&!iban.error&&seenIbans.has(iban.value))iban.error='این شماره شبا تکراری است.';
  if(card.value)seenCards.add(card.value);if(iban.value)seenIbans.add(iban.value);
  for(const [input,result,selector] of [[cardInput,card,'.card-error'],[ibanInput,iban,'.iban-error']]){
   input.setAttribute('aria-invalid',String(Boolean(result.error)));row.querySelector(selector).textContent=result.error;
  }
  invalid ||= Boolean(card.error||iban.error);
  if(!card.error&&!iban.error&&(card.value||iban.value))valid.push({card:card.value,iban:iban.value});
 }
 $('printed-banks').replaceChildren(...valid.map((entry,i)=>{
  const block=document.createElement('div');block.className='printed-bank-entry';
  const heading=document.createElement('strong');heading.textContent=`روش واریز ${money(i+1)}`;block.append(heading);
  for(const [label,value] of [['شماره کارت',entry.card],['شماره شبا',entry.iban]])if(value){
   const line=document.createElement('div'),name=document.createElement('span'),number=document.createElement('bdi');
   name.textContent=label;number.textContent=value.match(/.{1,4}/g).join(' ');line.append(name,number);block.append(line);
  }return block;
 }));
 if(!valid.length){const msg=document.createElement('p');msg.className='hint';msg.textContent='اطلاعات بانکی معتبری برای نمایش وارد نشده است.';$('printed-banks').append(msg);}
 $('bank-status').textContent=invalid?'خطاهای شماره کارت/شبا را اصلاح کنید؛ چاپ تا رفع خطا غیرفعال است.':`${money(valid.length)} ردیف اطلاعات بانکی معتبر آمادهٔ نمایش است.`;
 $('print').disabled=invalid;return !invalid;
}
function update(){
 invoice.classList.toggle('a5',$('paper').value==='A5');
 for(const key of ['name','phone','id','address'])$('printed-customer-'+key).textContent=sampleInvoice.customer[key]?.trim()||'—';
 updateBanks();
 // Facts of this sample only; no invented receipt, delivery or warranty assertions.
 const buyer=sampleInvoice.customer.name?.trim()||'مشتری ثبت‌شده';
 $('invoice-number').textContent=sampleInvoice.number;
 $('invoice-date').textContent=sampleInvoice.date;
 $('printed-notes').textContent=`این فاکتور به شمارهٔ ${sampleInvoice.number} مورخ ${sampleInvoice.date} برای «${buyer}» شامل ${money(lines.length)} ردیف و مجموع ${money(totals.qty)} عدد کالا است.\nجمع کالاها ${money(totals.gross)} ریال است؛ با کسر ${money(totals.discount)} ریال تخفیف و افزودن ${money(vat)} ریال مالیات و عوارض نمونه، مبلغ نهایی ${money(net+vat)} ریال می‌شود.\nروش پرداخت این نمونه ${sampleInvoice.paymentMethod} است. درج مبلغ و شماره حساب به معنای تأیید دریافت وجه یا تحویل کالا نیست؛ وضعیت پرداخت باید از سوابق همان سند بررسی شود.`;
 $('paper-label').textContent=`${$('paper').value} · عمودی · ریال`;
 $('paper-rule').textContent=`@page { size: ${$('paper').value} portrait; margin: ${$('paper').value==='A5'?6:10}mm; }`;
 requestAnimationFrame(fitPaper);
}
document.querySelector('.settings').addEventListener('input',update);
$('paper').addEventListener('change',update);
$('add-bank').addEventListener('click',addBankRow);
$('reset').addEventListener('click',()=>location.reload());
$('print').addEventListener('click',()=>{if(updateBanks())window.print();});
window.addEventListener('resize',fitPaper);
document.fonts.ready.then(fitPaper);
window.addEventListener('load',fitPaper);
addBankRow();
