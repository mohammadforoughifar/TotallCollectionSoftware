// Rendered CSS fixture, not a running Blazor application.
const assert = require('node:assert/strict');
const puppeteer = require('puppeteer-core');
(async()=>{
 const browser=await puppeteer.launch({executablePath:process.env.CHROME_BIN,args:['--no-sandbox','--disable-dev-shm-usage'],headless:true});
 try{
 const page=await browser.newPage();
 for(const width of [320,360,390,600,768,992,1280,1568]){
  await page.setViewport({width,height:1300});
  await page.goto(process.env.FIXTURE_URL || 'http://localhost:8081/row.html');
  await page.evaluate(()=>document.fonts.ready);
  const result=await page.evaluate(()=>{
   const rect=e=>{const r=e.getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height,right:r.right,bottom:r.bottom}};
   const history=document.querySelector('.last-purchase-action');
   return {overflow:document.documentElement.scrollWidth>innerWidth,history:rect(history),remove:rect(history.nextElementSibling),label:rect(history.querySelector('span')),badge:rect(history.querySelector('kbd')),footers:[...document.querySelectorAll('.invoice-footer-actions')].map(f=>({box:rect(f),buttons:[...f.querySelectorAll('button')].map(rect)}))};
  });
  assert.equal(result.overflow,false,`page overflow at ${width}`);
  assert(Math.abs(result.history.y-result.remove.y)<3,`F8/delete baseline at ${width}`);
  assert(Math.abs(result.label.y-result.badge.y)<8,`label/badge wrapping at ${width}`);
  assert(result.history.right<=width && result.history.x>=0,`history outside viewport ${width}`);
  for(const f of result.footers)for(const b of f.buttons){assert(b.width<250,`stretched button at ${width}`);assert(b.height<=44,`oversized button at ${width}`);assert(b.x>=f.box.x-1 && b.right<=f.box.right+1,`footer overflow at ${width}`);}
  console.log(`PASS ${width}px: F8/delete inline, no page overflow, compact footers`);
 }
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
