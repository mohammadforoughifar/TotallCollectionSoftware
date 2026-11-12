const test=require('node:test');const assert=require('node:assert/strict');const v=require('./bank-validation.js');
const prefix='603799000000000';
const card=Array.from({length:10},(_,n)=>prefix+n).find(n=>!v.card(n).error);
const bban='0620000000000000000001';
const digits=bban+'182700';const check=String(98-Number(BigInt(digits)%97n)).padStart(2,'0');const iban='IR'+check+bban;
module.exports={card,iban};
test('card format, checksum and Persian/Arabic normalization',()=>{
 assert.equal(v.card(card).error,'');assert.equal(v.card(card.replace(/\d/g,d=>'۰۱۲۳۴۵۶۷۸۹'[d])).value,card);
 for(const n of ['0000000000000000',card.slice(0,15),card+'0','x'+card,card.slice(0,15)+(Number(card[15])+1)%10])assert(v.card(n).error,n);
 assert.equal(v.card('۶۰۳۷-۹۹۰۰ ۰۰۰۰ ۰۰۰'+card[15]).value,card);
});
test('Iranian IBAN checksum, optional IR prefix, invalid characters',()=>{
 assert.equal(v.iban(iban).error,'');assert.equal(v.iban(iban.slice(2)).value,iban);
 assert.equal(v.iban(iban.toLowerCase()).error,'');
 for(const n of ['IR'+'0'.repeat(24),'DE'+iban.slice(2),iban+'0','IR00'+bban,iban+'x'])assert(v.iban(n).error,n);
});
test('blank optional fields do not become zeros or invented accounts',()=>{
 assert.deepEqual(v.card(''),{value:'',error:''});assert.deepEqual(v.iban(''),{value:'',error:''});
});
