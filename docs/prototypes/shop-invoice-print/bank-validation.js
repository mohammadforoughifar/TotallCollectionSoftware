'use strict';
// Format/checksum validation only, not account ownership or bank verification.
(function(root){
 const normalize=value=>String(value??'').replace(/[۰-۹]/g,c=>String(c.charCodeAt(0)-1776)).replace(/[٠-٩]/g,c=>String(c.charCodeAt(0)-1632)).replace(/[\s-]/g,'').toUpperCase();
 function card(value){
  const number=normalize(value);
  if(!number)return {value:'',error:''};
  if(!/^\d{16}$/.test(number))return {value:number,error:'شماره کارت باید دقیقاً ۱۶ رقم باشد.'};
  if(/^(\d)\1+$/.test(number))return {value:number,error:'شماره کارت معتبر نیست.'};
  const sum=[...number].reduce((sum,d,i)=>{let n=Number(d)*(i%2===0?2:1);return sum+(n>9?n-9:n);},0);
  return {value:number,error:sum%10?'رقم کنترل شماره کارت صحیح نیست.':''};
 }
 function iban(value){
  let number=normalize(value);
  if(!number)return {value:'',error:''};
  if(/^\d{24}$/.test(number))number='IR'+number;
  if(!/^IR\d{24}$/.test(number))return {value:number,error:'شبا باید IR به‌همراه دقیقاً ۲۴ رقم باشد.'};
  const rearranged=number.slice(4)+'1827'+number.slice(2,4);
  let mod=0;for(const d of rearranged)mod=(mod*10+Number(d))%97;
  return {value:number,error:mod!==1||/^0+$/.test(number.slice(4))?'رقم کنترل شماره شبا صحیح نیست.':''};
 }
 const api={normalize,card,iban};
 if(typeof module!=='undefined'&&module.exports)module.exports=api;else root.ShopPrintBankValidation=api;
})(globalThis);
