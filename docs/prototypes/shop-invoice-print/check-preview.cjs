// Requires external puppeteer-core and CHROME_BIN. Tests this prototype, not Blazor.
const assert=require('node:assert/strict');
const puppeteer=require('puppeteer-core');
(async()=>{
 const browser=await puppeteer.launch({executablePath:process.env.CHROME_BIN,args:['--no-sandbox','--disable-dev-shm-usage'],headless:true});
 try{
 const page=await browser.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.setViewport({width:1500,height:1400});await page.goto(process.env.PREVIEW_URL||'http://localhost:8082');await page.evaluate(()=>document.fonts.ready);
 assert.equal(await page.$eval('#item-rows',el=>el.children.length),6);
 assert.equal(await page.$eval('#payable',el=>el.textContent),new Intl.NumberFormat('fa-IR').format(378840000));
 for(const theme of ['classic','modern']){
  await page.click(`input[name=template][value=${theme}]`);
  for(const paper of ['A4','A5']){
   await page.select('#paper',paper);await page.emulateMediaType('print');
   const info=await page.$eval('#invoice',el=>({width:el.scrollWidth,client:el.clientWidth,height:el.offsetHeight}));
   assert(info.width<=info.client+2,`${theme}/${paper} overflow`);
   await page.pdf({path:`/home/user/outputs/shop-${theme}-${paper}.pdf`,preferCSSPageSize:true,printBackground:true});
   await page.emulateMediaType('screen');console.log(`PASS ${theme}/${paper}`);
  }
 }
 await page.select('#paper','A4');await page.click('input[name=template][value=classic]');
 await page.$eval('#notes',el=>{el.value='<script>not executable</script>';el.dispatchEvent(new Event('input',{bubbles:true}));});
 assert.equal(await page.$eval('#printed-notes',el=>el.children.length),0);
 await page.reload();await page.evaluate(()=>document.fonts.ready);
 await page.screenshot({path:'/home/user/outputs/shop-print-settings-preview.png',fullPage:true});
 await page.$('#invoice').then(el=>el.screenshot({path:'/home/user/outputs/shop-invoice-classic.png'}));
 for(const width of [320,390,768,1024,1500]){
  await page.setViewport({width,height:1000});await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))));
  assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),`Page overflow at ${width}`);
  console.log(`PASS responsive ${width}`);
 }
 assert.deepEqual(errors,[]);
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
