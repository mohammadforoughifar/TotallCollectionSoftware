'use strict';
// Isolated design prototype: no API, no real company settings, no persistence.
const $ = id => document.getElementById(id);
const money = value => new Intl.NumberFormat('fa-IR', {maximumFractionDigits: 0}).format(value);
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
function update(){
 const modern=document.querySelector('input[name=template]:checked').value==='modern';
 invoice.classList.toggle('modern',modern);invoice.classList.toggle('classic',!modern);
 invoice.classList.toggle('a5',$('paper').value==='A5');
 invoice.classList.toggle('no-line-notes',!$('show-line-notes').checked);
 $('document-title').textContent=$('doc-type').value;
 $('party-label').textContent=$('doc-type').value==='رسید خرید'?'اطلاعات فروشنده':'اطلاعات خریدار';
 $('invoice-logo').hidden=!$('show-logo').checked;
 for(const [control,target] of [['show-signatures','signatures'],['show-bank','bank-details'],['show-notes','invoice-notes']])$(target).hidden=!$(control).checked;
 for(const [input,target] of [['owner','printed-owner'],['card','printed-card'],['iban','printed-iban'],['notes','printed-notes']])$(target).textContent=$(input).value;
 $('paper-label').textContent=`${$('paper').value} · عمودی · ریال`;
 $('paper-rule').textContent=`@page { size: ${$('paper').value} portrait; margin: ${$('paper').value==='A5'?6:10}mm; }`;
 requestAnimationFrame(fitPaper);
}
document.querySelector('.settings').addEventListener('input',update);
document.querySelector('.settings').addEventListener('change',update);
$('reset').addEventListener('click',()=>location.reload());
$('print').addEventListener('click',()=>window.print());
window.addEventListener('resize',fitPaper);
document.fonts.ready.then(fitPaper);
window.addEventListener('load',fitPaper);
update();
