
'use strict';
// Static dispatch read model. No prices, payments, or invented delivery confirmation.
const $=id=>document.getElementById(id);
const number=value=>new Intl.NumberFormat('fa-IR').format(value);
const dispatch={number:'ب–۱۴۰۵–۰۰۳۲',date:'۱۴۰۵/۰۸/۲۰',
 recipient:{name:'عقاب افشان'},
 description:'تحویل کالا مطابق اقلام این بیجک.',
 lines:[{name:'کارت شبکه',quantity:6,note:''},{name:'اسپیکر کامپیوتر',quantity:5,note:''},{name:'دوربین مداربسته',quantity:5,note:''},{name:'دانگل HDMI',quantity:1,note:''},{name:'مانیتور سامسونگ',quantity:2,note:'شماره سریال مطابق سند'},{name:'کابل HDMI پنج متری',quantity:5,note:''}]};
function render(){
 $('invoice-number').textContent=dispatch.number;$('invoice-date').textContent=dispatch.date;
 $('printed-customer-name').textContent=dispatch.recipient?.name?.trim()||'-';
 $('line-count').textContent=number(dispatch.lines.length);$('quantity-total').textContent=number(dispatch.lines.reduce((sum,line)=>sum+line.quantity,0));
 $('item-rows').replaceChildren(...dispatch.lines.map((line,i)=>{const row=document.createElement('tr');[number(i+1),line.name,number(line.quantity),line.note||'-'].forEach(value=>{const cell=document.createElement('td');cell.textContent=value;row.append(cell);});return row;}));
 $('printed-notes').textContent=dispatch.description?.trim()||'-';
}
function fit(){const invoice=$('invoice'),viewport=document.querySelector('.paper-viewport');invoice.style.transform='';viewport.style.height='';if(innerWidth<=800){const scale=Math.min(1,viewport.clientWidth/invoice.offsetWidth);invoice.style.transform=`scale(${scale})`;invoice.style.transformOrigin='top right';viewport.style.height=`${Math.ceil(invoice.offsetHeight*scale)+20}px`;}}
function update(){const paper=$('paper').value;$('invoice').classList.toggle('a5',paper==='A5');$('paper-label').textContent=`${paper} · عمودی · بدون قیمت`;$('paper-rule').textContent=`@page{size:${paper} portrait;margin:${paper==='A5'?6:10}mm}`;requestAnimationFrame(fit);}
$('paper').addEventListener('change',update);$('print').addEventListener('click',()=>$('paper-dialog').showModal());$('reset').addEventListener('click',()=>location.reload());window.addEventListener('resize',fit);document.fonts.ready.then(fit);render();update();

$('cancel-paper').addEventListener('click',()=>$('paper-dialog').close());
$('confirm-paper').addEventListener('click',()=>{$('paper-dialog').close();window.print();});
