'use strict';
// Exact-format/length validation only; no checksum, ownership or bank verification.
(function(root){
 const normalize=value=>String(value??'').replace(/[۰-۹]/g,c=>String(c.charCodeAt(0)-1776)).replace(/[٠-٩]/g,c=>String(c.charCodeAt(0)-1632)).replace(/[\s-]/g,'').toUpperCase();
 function card(value){
  const number=normalize(value);
  if(!number)return {value:'',error:''};
  if(!/^\d{16}$/.test(number))return {value:number,error:'شماره کارت باید دقیقاً ۱۶ رقم باشد.'};
  return {value:number,error:''};
 }
 function iban(value){
  let number=normalize(value);
  if(!number)return {value:'',error:''};
  if(/^\d{24}$/.test(number))number='IR'+number;
  if(!/^IR\d{24}$/.test(number))return {value:number,error:'شبا باید IR به‌همراه دقیقاً ۲۴ رقم باشد.'};
  return {value:number,error:''};
 }
 const api={normalize,card,iban};
 if(typeof module!=='undefined'&&module.exports)module.exports=api;else root.ShopPrintBankValidation=api;
})(globalThis);
