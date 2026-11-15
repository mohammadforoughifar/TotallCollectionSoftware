'use strict';
// Isolated design prototype: no API, no real company settings, no persistence.
const $ = id => document.getElementById(id);
const money = value => new Intl.NumberFormat('fa-IR', {maximumFractionDigits: 0}).format(value);
// Mock of the invoice-linked customer read model; no customer settings inputs.
// Production integration must populate this from authorized invoice/customer data.
const sampleInvoice = {number:'خ–۱۴۰۵–۰۰۷۳', date:'۱۴۰۵/۰۸/۲۰', paymentMethod:'ترکیبی — نقدی، چکی و نسیه',
 settlement:{cash:100000000,credit:78840000,creditDue:'۱۴۰۵/۱۱/۲۰'},
 cheques:[{bank:'ملت',sayad:'',due:'۱۴۰۵/۰۹/۲۰',amount:120000000},{bank:'ملی',sayad:'',due:'۱۴۰۵/۱۰/۲۰',amount:80000000}],
 supplierInvoice:'۱۴۸۲',supplier:{name:'پخش تجهیزات رایانه سپهر',phone:'۰۹۱۲–۰۰۰۰۰۰۰',id:'',address:''}};
const lines = [
 {name:'کارت شبکه', qty:6, price:2500000, discount:0, note:'—'},
 {name:'اسپیکر کامپیوتر', qty:5, price:6800000, discount:1000000, note:'گارانتی طبق سند'},
 {name:'دوربین مداربسته', qty:5, price:24500000, discount:4000000, note:'—'},
 {name:'دانگل HDMI', qty:1, price:3900000, discount:0, note:'—'},
 {name:'مانیتور سامسونگ', qty:2, price:85000000, discount:5000000, note:'شماره سریال در سند'},
 {name:'کابل HDMI پنج متری', qty:5, price:1800000, discount:0, note:'—'}
];
const totals = lines.reduce((t,l) => ({gross:t.gross+l.qty*l.price,discount:t.discount+l.discount,qty:t.qty+l.qty}), {gross:0,discount:0,qty:0});
const net = totals.gross-totals.discount;
const vat = net*0.1; // Explicitly hypothetical demo rate, NOT a production tax rule.
$('item-rows').replaceChildren(...lines.map((line,i)=>{
 const row=document.createElement('tr');
 [money(i+1),line.name,money(line.qty),money(line.price),money(line.qty*line.price),line.discount?money(line.discount):'-',money(line.qty*line.price-line.discount)].forEach((value,j)=>{
  const cell=document.createElement('td');cell.textContent=value;
  if(j>=3)cell.className='money';
  if(j===1&&line.note&&line.note!=='—'){const note=document.createElement('small');note.className='item-note';note.textContent=line.note;cell.append(note);}
  row.append(cell);
 });return row;
}));
for(const [id,value] of Object.entries({gross:totals.gross,discount:totals.discount,net,vat,payable:net+vat,'quantity-total':totals.qty}))$(id).textContent=money(value);
$('payment-method').textContent='تسویهٔ '+sampleInvoice.paymentMethod;
$('line-count').textContent=money(lines.length);
const chequeTotal=sampleInvoice.cheques.reduce((sum,cheque)=>sum+cheque.amount,0);
$('settlement-breakdown').replaceChildren(...[
 ['نقدی',sampleInvoice.settlement.cash,''],
 ['چکی',chequeTotal,'چک پرداختی'],
 ['مانده بدهی',sampleInvoice.settlement.credit,sampleInvoice.settlement.creditDue?`موعد: ${sampleInvoice.settlement.creditDue}`:'']
].filter(([,amount])=>amount>0).map(([label,amount,detail])=>{
 const block=document.createElement('div'),title=document.createElement('span'),value=document.createElement('strong');
 title.textContent=label;value.textContent=`${money(amount)} ریال`;block.append(title,value);
 if(detail){const note=document.createElement('small');note.textContent=detail;block.append(note);}
 return block;
}));

$('cheque-rows').replaceChildren(...sampleInvoice.cheques.map(cheque=>{
 const row=document.createElement('tr');
 [cheque.bank,cheque.sayad||'-',cheque.due,money(cheque.amount)].forEach((value,i)=>{
  const cell=document.createElement('td');cell.textContent=value;
  if(i>0){cell.dir='ltr';cell.className='cheque-number';}
  row.append(cell);
 });return row;
}));

const invoice=$('invoice');
$('supplier-invoice').textContent=sampleInvoice.supplierInvoice||'-';
for(const key of ['name','phone','id','address'])$('printed-customer-'+key).textContent=sampleInvoice.supplier?.[key]?.trim()||'-';
$('invoice-number').textContent=sampleInvoice.number;
$('invoice-date').textContent=sampleInvoice.date;
$('printed-notes').textContent=`خرید از «${sampleInvoice.supplier.name}» طبق فاکتور تأمین‌کننده ${sampleInvoice.supplierInvoice}؛ ${money(lines.length)} ردیف و ${money(totals.qty)} عدد کالا، به مبلغ ${money(net+vat)} ریال.`;
function fitPaper(){
 const viewport=document.querySelector('.paper-viewport');invoice.style.transform='';viewport.style.height='';
 if(window.innerWidth<=800){const scale=Math.min(1,viewport.clientWidth/invoice.offsetWidth);invoice.style.transform=`scale(${scale})`;invoice.style.transformOrigin='top right';viewport.style.height=`${Math.ceil(invoice.offsetHeight*scale)+20}px`;}
}
function update(){invoice.classList.toggle('a5',$('paper').value==='A5');$('paper-label').textContent=`${$('paper').value} · عمودی · ریال`;$('paper-rule').textContent=`@page {size:${$('paper').value} portrait;margin:${$('paper').value==='A5'?6:10}mm}`;requestAnimationFrame(fitPaper);}
$('paper').addEventListener('change',update);$('print').addEventListener('click',()=>$('paper-dialog').showModal());$('reset').addEventListener('click',()=>location.reload());window.addEventListener('resize',fitPaper);document.fonts.ready.then(fitPaper);update();

$('cancel-paper').addEventListener('click',()=>$('paper-dialog').close());
$('confirm-paper').addEventListener('click',()=>{$('paper-dialog').close();window.print();});
