// Isolated browser prototype test, not production Blazor or database integration.
const assert=require('node:assert/strict');const puppeteer=require('puppeteer-core');const validation=require('./bank-validation.js');
(async()=>{
 const browser=await puppeteer.launch({executablePath:process.env.CHROME_BIN,args:['--no-sandbox','--disable-dev-shm-usage'],headless:true});
 try{
 const page=await browser.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.setViewport({width:1500,height:1450});await page.goto(process.env.PREVIEW_URL||'http://localhost:8082');await page.evaluate(()=>document.fonts.ready);
 const set=async(sel,value)=>{await page.$eval(sel,(el,val)=>{el.value=val;el.dispatchEvent(new Event('input',{bubbles:true}));},value);};
 assert.equal(await page.$$('input[type=checkbox], input[type=radio]').then(a=>a.length),0);
 assert.equal(await page.$eval('#payable',el=>el.textContent),new Intl.NumberFormat('fa-IR').format(378840000));
 assert.equal(await page.$$('.title-cell p').then(a=>a.length),0);
 assert.equal(await page.$$('.invoice-info h3, #bank-details .section-tag').then(a=>a.length),0);
 assert.equal(await page.$eval('.row-heading',e=>getComputedStyle(e).whiteSpace),'nowrap');
 assert.equal(await page.$eval('#printed-notes',e=>e.textContent.split('\n').length),2);
 assert.equal(await page.$$('#invoice > .store-contact').then(a=>a.length),0);
 assert.equal(await page.$$('#bank-details .store-contact').then(a=>a.length),1);
 assert.match(await page.$eval('#bank-details .store-contact',e=>e.textContent),/تلفن.*نشانی/);
 assert(await page.$('#printed-customer-phone'));
 assert(await page.$('#printed-customer-address'));
 assert.equal(await page.$$('#invoice-notes h3').then(a=>a.length),0);
 assert(await page.$eval('#printed-notes',e=>e.textContent.trim().length>0));
 assert.equal(await page.$$('.branded-footer').then(a=>a.length),0);
 const checkBrand=async()=>assert(await page.evaluate(()=>{
  const panel=document.querySelector('#invoice-notes').getBoundingClientRect();
  const brand=document.querySelector('.bank-frame-brand').getBoundingClientRect();
  return brand.top<panel.top && brand.bottom>panel.top && Math.abs((brand.left+brand.right)/2-(panel.left+panel.right)/2)<2;
 }),'Brand must straddle the notes section top border at its center');
 await checkBrand();
 await set('.bank-card','123');assert.equal(await page.$eval('#print',el=>el.disabled),true);
 assert.equal(await page.$eval('.bank-card',el=>el.getAttribute('aria-invalid')),'true');
 assert.match(await page.$eval('#printed-banks',el=>el.textContent),/123/);
 assert.match(await page.$eval('#printed-banks',el=>el.textContent),/ناقص/);
 await set('.bank-card','1234567890123456');
 assert.equal(await page.$eval('.bank-card',e=>e.getAttribute('aria-invalid')),'false');
 assert.equal(await page.$eval('.card-error',e=>e.textContent),'');
 assert.match(await page.$eval('#printed-banks',e=>e.textContent),/1234 5678 9012 3456/);
 assert(!/روش واریز/.test(await page.$eval('#printed-banks',e=>e.textContent)));
 assert.equal(await page.$eval('.printed-bank-entry>div',e=>getComputedStyle(e).justifyContent),'flex-start');
 await set('.bank-iban','123456789012345678901234');
 assert.equal(await page.$eval('.bank-iban',e=>e.getAttribute('aria-invalid')),'false');
 assert.match(await page.$eval('#printed-banks',e=>e.textContent),/IR12/);
 await set('.bank-iban','');
 const prefix='603799000000000';const card=Array.from({length:10},(_,i)=>prefix+i).find(n=>!validation.card(n).error);
 await set('.bank-card',card);assert.equal(await page.$eval('#print',el=>el.disabled),false);
 await page.click('#add-bank');await set('.bank-entry:last-child .bank-card',card);
 assert.equal(await page.$eval('#print',el=>el.disabled),true);assert.match(await page.$eval('.bank-entry:last-child .card-error',e=>e.textContent),/تکراری/);
 await page.click('.bank-entry:last-child .remove-bank');assert.equal(await page.$eval('#print',el=>el.disabled),false);
 const secondPrefix='589210000000000';const secondCard=Array.from({length:10},(_,i)=>secondPrefix+i).find(n=>!validation.card(n).error);
 await page.click('#add-bank');await set('.bank-entry:last-child .bank-card',secondCard);
 assert.equal(await page.$$('.printed-bank-entry').then(a=>a.length),2);
 assert.equal(await page.$eval('#print',el=>el.disabled),false);
 await page.click('.bank-card');await page.keyboard.press('End');await page.keyboard.type('999');
 assert.equal(await page.$eval('.bank-card',e=>e.value),card);
 assert.equal(await page.$eval('.bank-card',e=>e.getAttribute('aria-invalid')),'false');
 assert.equal(await page.$eval('.card-error',e=>e.textContent),'');
 await page.$eval('.bank-card',e=>{const data=new DataTransfer();data.setData('text','12345678901234567');e.dispatchEvent(new ClipboardEvent('paste',{clipboardData:data,bubbles:true,cancelable:true}));});
 assert.equal(await page.$eval('.bank-card',e=>e.value),card);
 assert.equal(await page.$eval('.bank-card',e=>e.getAttribute('aria-invalid')),'false');
 assert.equal(await page.$eval('.card-error',e=>e.textContent),'');
 await page.click('.bank-iban');await page.keyboard.type('1'.repeat(25));
 assert.equal(await page.$eval('.bank-iban',e=>e.value.length),24);
 assert.equal(await page.$eval('.bank-iban',e=>e.getAttribute('aria-invalid')),'false'); // Exact length restores normal state.
 await set('.bank-iban','123');assert.equal(await page.$eval('#print',e=>e.disabled),true);
 const bban='0620000000000000000001';const check=String(98-Number(BigInt(bban+'182700')%97n)).padStart(2,'0');const fullIban='IR'+check+bban;
 await set('.bank-iban','');await page.$eval('.bank-iban',(e,text)=>{const data=new DataTransfer();data.setData('text',text);e.dispatchEvent(new ClipboardEvent('paste',{clipboardData:data,bubbles:true,cancelable:true}));},fullIban);
 assert.equal(await page.$eval('.bank-iban',e=>e.value),fullIban.slice(2));
 assert.equal(await page.$eval('.bank-iban',e=>e.getAttribute('aria-invalid')),'false');
 assert.equal(await page.$eval('.iban-error',e=>e.textContent),'');
 assert.equal(await page.$eval('#print',e=>e.disabled),false);
 await page.$eval('.bank-iban',e=>{e.select();const data=new DataTransfer();data.setData('text','IR'+'1'.repeat(25));e.dispatchEvent(new ClipboardEvent('paste',{clipboardData:data,bubbles:true,cancelable:true}));});
 assert.equal(await page.$eval('.bank-iban',e=>e.value),fullIban.slice(2));
 assert.equal(await page.$eval('.bank-iban',e=>e.getAttribute('aria-invalid')),'false');
 assert.equal(await page.$eval('.iban-error',e=>e.textContent),'');
 await page.click('.bank-iban');await page.keyboard.press('End');await page.keyboard.type('999');
 assert.equal(await page.$eval('.bank-iban',e=>e.value),fullIban.slice(2));
 assert.equal(await page.$eval('.bank-iban',e=>e.getAttribute('aria-invalid')),'false');
 assert.equal(await page.$eval('#print',e=>e.disabled),false);
 await set('.bank-iban','');await set('.bank-card',card);

 assert.equal(await page.$$('input[id^=customer-],textarea[id^=customer-]').then(a=>a.length),0);
 assert.match(await page.$eval('#printed-customer-name',e=>e.textContent),/عقاب افشان/);
 assert.match(await page.$eval('#printed-notes',e=>e.textContent),/عقاب افشان/);
 assert.equal(await page.$eval('#printed-customer-id',e=>e.textContent),'-');
 assert.equal(await page.$eval('#printed-notes',e=>e.children.length),0);
 await page.evaluate(()=>{
  sampleInvoice.customer={name:'  ',phone:null,id:undefined,address:''};update();
 });
 for(const key of ['name','phone','id','address'])assert.equal(await page.$eval('#printed-customer-'+key,e=>e.textContent),'-');
 await page.evaluate(()=>{sampleInvoice.customer=null;update();});
 for(const key of ['name','phone','id','address'])assert.equal(await page.$eval('#printed-customer-'+key,e=>e.textContent),'-');
 await page.reload();await page.evaluate(()=>document.fonts.ready);
 for(const paper of ['A4','A5']){
  await page.select('#paper',paper);await page.emulateMediaType('print');
  await checkBrand();
  await page.pdf({path:`/home/user/outputs/shop-fixed-${paper}.pdf`,preferCSSPageSize:true,printBackground:true});
  assert.equal(await page.$eval('#invoice-notes',e=>getComputedStyle(e).display==='none'),false);
  assert.equal(await page.$eval('#invoice-logo',e=>getComputedStyle(e).display==='none'),false);
  await page.emulateMediaType('screen');console.log('PASS print '+paper);
 }
 await page.select('#paper','A4');await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));
 await page.$('#invoice').then(el=>el.screenshot({path:'/home/user/outputs/shop-invoice-fixed.png'}));
 for(const width of [320,390,768,1024,1500]){
  await page.setViewport({width,height:1000});await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));
  assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),'overflow '+width);
  await page.click('#add-bank');
  await page.waitForFunction(()=>document.activeElement===document.querySelector('.bank-entry:last-child .bank-card'));
  await page.keyboard.type(card);
  assert.equal(await page.$eval('.bank-entry:last-child .bank-card',e=>e.value),card);
  await page.click('.bank-entry:last-child .bank-iban');await page.keyboard.type('12');
  assert.equal(await page.$eval('.bank-entry:last-child .bank-iban',e=>e.value),'12');
  await page.click('.bank-entry:last-child .remove-bank');
  console.log('PASS responsive add/click/type card and IBAN '+width);
 }
 assert.deepEqual(errors,[]);console.log('PASS fixed layout, dynamic notes, validation and duplicate/removal behavior');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
