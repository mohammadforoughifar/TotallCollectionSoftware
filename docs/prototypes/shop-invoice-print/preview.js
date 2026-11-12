'use strict';
// Isolated design prototype: no API, no real company settings, no persistence.
const $ = id => document.getElementById(id);
const money = value => new Intl.NumberFormat('fa-IR', {maximumFractionDigits: 0}).format(value);
// Mock of the invoice-linked customer read model; no customer settings inputs.
// Production integration must populate this from authorized invoice/customer data.
const sampleInvoice = {number:'۱۴۰۵–۰۰۲۱۸', date:'۱۴۰۵/۰۸/۲۰', paymentMethod:'نقدی',
 customer:{name:'عقاب افشان',phone:'۰۹۱۲–۰۰۰۰۰۰۰',id:'',address:''}};
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
// Review alternatives only: not a new persisted shop layout setting.
const tableLayouts={
 balanced:{description:'پیشنهاد اول: شرح خوانا، سه ستون مبلغ هم‌اندازه و تخفیف مشخص؛ تعادل بین سادگی و شفافیت.',
  columns:[['row','ردیف',5],['name','شرح کالا / خدمت',40],['qty','تعداد',7],['price','فی واحد',16],['discount','تخفیف',16],['net','پس از تخفیف',16]]},
 detailed:{description:'پیشنهاد دوم: چهار ستون مبلغ هم‌اندازه؛ فی واحد، مبلغ اولیه، تخفیف و مبلغ پس از تخفیف. مناسب کنترل ریز محاسبات.',
  columns:[['row','ردیف',5],['name','شرح کالا / خدمت',32],['qty','تعداد',7],['price','فی واحد',14],['gross','مبلغ اولیه',14],['discount','تخفیف',14],['net','پس از تخفیف',14]]},
 minimal:{description:'پیشنهاد سوم: پنج ستون و بیشترین فضا برای شرح کالا؛ تخفیف هر ردیف زیر شرح می‌آید و مبلغ پس از تخفیف در آخر است. مناسب چاپ خلوت به‌ویژه A5.',
  columns:[['row','ردیف',5],['name','شرح کالا / خدمت',51],['qty','تعداد',8],['price','فی واحد',18],['net','پس از تخفیف',18]]}
};
function renderTable(layout='balanced'){
 const definition=tableLayouts[layout];
 const table=document.querySelector('.proposal-items');table.dataset.layout=layout;
 $('item-columns').replaceChildren(...definition.columns.map(([, ,width])=>{const col=document.createElement('col');col.style.width=width+'%';return col;}));
 const head=document.createElement('tr');
 for(const [key,label] of definition.columns){const th=document.createElement('th');th.scope='col';th.textContent=label;th.className=key==='row'?'row-heading':key==='name'?'description-heading':'';head.append(th);}
 $('item-head').replaceChildren(head);
 $('item-rows').replaceChildren(...lines.map((line,i)=>{
  const row=document.createElement('tr');
  const values={row:i+1,qty:line.qty,price:line.price,gross:line.qty*line.price,discount:line.discount,net:line.qty*line.price-line.discount};
  for(const [key] of definition.columns){
   const cell=document.createElement('td');cell.dataset.column=key;
   if(key==='name'){
    cell.className='item-description';const name=document.createElement('strong');name.textContent=line.name;cell.append(name);
    const details=[];if(line.note&&line.note!=='—')details.push(line.note);
    if(layout==='minimal'&&line.discount)details.push(`تخفیف: ${money(line.discount)} ریال`);
    if(details.length){const note=document.createElement('small');note.textContent=details.join(' · ');cell.append(note);}
   }else{cell.textContent=key==='discount'&&!line.discount?'-':money(values[key]);if(!['row','qty'].includes(key))cell.className='money';}
   row.append(cell);
  }return row;
 }));
 $('table-proposal-description').textContent=definition.description;
 document.querySelectorAll('[data-table-layout]').forEach(button=>button.setAttribute('aria-pressed',String(button.dataset.tableLayout===layout)));
 if(typeof fitPaper==='function')requestAnimationFrame(fitPaper);
}
document.querySelectorAll('[data-table-layout]').forEach(button=>button.addEventListener('click',()=>renderTable(button.dataset.tableLayout)));
renderTable();
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
function addBankRow({focus=false}={}){
 const id=++bankRowNumber;
 const row=document.createElement('div');row.className='bank-entry';
 row.innerHTML=`<div class="bank-entry-head"><strong>کارت / شبای ${money(id)}</strong><button type="button" class="remove-bank" aria-label="حذف این کارت و شبا">حذف</button></div><label for="card-${id}">شماره کارت<input id="card-${id}" class="bank-card" dir="ltr" inputmode="numeric" minlength="16" maxlength="16" pattern="[0-9]{16}" placeholder="۱۶ رقم" aria-describedby="card-error-${id}"><small id="card-error-${id}" class="field-error card-error" aria-live="polite"></small></label><label for="iban-${id}">شماره شبا (۲۴ رقم)<span class="iban-input-wrap" dir="ltr"><span class="iban-prefix" aria-hidden="true">IR</span><input id="iban-${id}" class="bank-iban" dir="ltr" inputmode="numeric" minlength="24" maxlength="24" pattern="[0-9]{24}" placeholder="۲۴ رقم بدون IR" aria-describedby="iban-error-${id}"></span><small id="iban-error-${id}" class="field-error iban-error" aria-live="polite"></small></label>`;
 for(const [selector,max] of [['.bank-card',16],['.bank-iban',24]]){
  const input=row.querySelector(selector);
  function insert(text,event){
   event.preventDefault();
   let normalized=ShopPrintBankValidation.normalize(text);
   if(max===24)normalized=normalized.replace(/^IR/,'');
   const start=input.selectionStart??input.value.length,end=input.selectionEnd??start;
   if(!/^\d*$/.test(normalized)||input.value.length-(end-start)+normalized.length>max){
    // Rejected keystrokes/pastes do not change the stored value or its validity.
    updateBanks();return;
   }
   input.setRangeText(normalized,start,end,'end');input.dispatchEvent(new Event('input',{bubbles:true}));
  }
  input.addEventListener('paste',event=>insert(event.clipboardData.getData('text'),event));
  input.addEventListener('beforeinput',event=>{
   if(event.inputType==='insertText'&&event.data)insert(event.data,event);
  });
  input.addEventListener('input',()=>{
   // Mobile keyboards may bypass beforeinput; normalize without truncating data.
   input.value=ShopPrintBankValidation.normalize(input.value);
  });
 }
 row.querySelector('.bank-card').addEventListener('keydown',event=>{
  if(event.key!=='Enter'||event.isComposing||event.altKey||event.ctrlKey||event.metaKey||event.shiftKey)return;
  event.preventDefault();
  row.querySelector('.bank-iban').focus();
 });
 row.querySelector('button').addEventListener('click',()=>{row.remove();update();});
 $('bank-rows').append(row);update();
 if(focus)requestAnimationFrame(()=>{
  // Bring the actual editable row into view, including below the mobile preview.
  row.scrollIntoView({block:'center',behavior:'instant'});
  row.querySelector('.bank-card').focus({preventScroll:true});
 });
}
function updateBanks(){
 const seenCards=new Set(),seenIbans=new Set();let invalid=false;const valid=[],entries=[];
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
  if(card.value||iban.value)entries.push({card:card.value,iban:iban.value,incomplete:Boolean(card.error||iban.error)});
 }
 $('printed-banks').replaceChildren(...entries.map((entry,i)=>{
  const block=document.createElement('div');block.className='printed-bank-entry';
  if(entry.incomplete){const state=document.createElement('small');state.className='preview-bank-state';state.textContent='ناقص یا تکراری — فقط پیش‌نمایش، غیرقابل چاپ';block.append(state);}
  for(const [label,value] of [['شماره کارت',entry.card],['شماره شبا',entry.iban]])if(value){
   const line=document.createElement('div'),name=document.createElement('span'),number=document.createElement('bdi');
   name.textContent=label;number.textContent=value.match(/.{1,4}/g).join(' ');line.append(name,number);block.append(line);
  }return block;
 }));
 if(!entries.length){const msg=document.createElement('p');msg.className='hint';msg.textContent='با ورود شماره کارت یا شبا، همین‌جا نمایش داده می‌شود.';$('printed-banks').append(msg);}
 $('bank-status').textContent=invalid?'خطاهای شماره کارت/شبا را اصلاح کنید؛ چاپ تا رفع خطا غیرفعال است.':`${money(valid.length)} ردیف اطلاعات بانکی معتبر آمادهٔ نمایش است.`;
 $('print').disabled=invalid;return !invalid;
}
function update(){
 invoice.classList.toggle('a5',$('paper').value==='A5');
 for(const key of ['name','phone','id','address'])$('printed-customer-'+key).textContent=sampleInvoice.customer?.[key]?.trim()||'-';
 updateBanks();
 // Facts of this sample only; no invented receipt, delivery or warranty assertions.
 const buyer=sampleInvoice.customer?.name?.trim()||'-';
 $('invoice-number').textContent=sampleInvoice.number;
 $('invoice-date').textContent=sampleInvoice.date;
 $('printed-notes').textContent=`فاکتور ${sampleInvoice.number} مورخ ${sampleInvoice.date} برای «${buyer}»؛ ${money(lines.length)} ردیف، ${money(totals.qty)} عدد کالا و مبلغ نهایی ${money(net+vat)} ریال.`;
 $('paper-label').textContent=`${$('paper').value} · عمودی · ریال`;
 $('paper-rule').textContent=`@page { size: ${$('paper').value} portrait; margin: ${$('paper').value==='A5'?6:10}mm; }`;
 requestAnimationFrame(fitPaper);
}
document.querySelector('.settings').addEventListener('input',update);
$('paper').addEventListener('change',update);
$('add-bank').addEventListener('click',()=>addBankRow({focus:true}));
$('reset').addEventListener('click',()=>location.reload());
$('print').addEventListener('click',()=>{if(updateBanks())window.print();});
window.addEventListener('resize',fitPaper);
document.fonts.ready.then(fitPaper);
window.addEventListener('load',fitPaper);
addBankRow();
