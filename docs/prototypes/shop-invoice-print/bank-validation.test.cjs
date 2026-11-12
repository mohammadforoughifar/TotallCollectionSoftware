const test=require('node:test');const assert=require('node:assert/strict');const v=require('./bank-validation.js');
test('exactly 16 card digits accepted regardless of checksum',()=>{
 for(const n of ['1234567890123456','0000000000000000','6037990000000001'])assert.equal(v.card(n).error,'');
 for(const n of ['123','1'.repeat(15),'1'.repeat(17),'x'+'1'.repeat(15)])assert(v.card(n).error);
 assert.equal(v.card('۱۲۳۴-۵۶۷۸ ۹۰۱۲ ۳۴۵۶').value,'1234567890123456');
});
test('exactly 24 IBAN digits with fixed or supplied IR prefix',()=>{
 const digits='123456789012345678901234';
 for(const n of [digits,'IR'+digits,'ir'+digits])assert.equal(v.iban(n).error,'');
 for(const n of ['IR123','IR'+'1'.repeat(25),'DE'+digits,digits+'x'])assert(v.iban(n).error);
 assert.equal(v.iban('۱۲۳۴۵۶۷۸۹۰۱۲۳۴۵۶۷۸۹۰۱۲۳۴').value,'IR'+digits);
});
test('blank unused fields remain optional',()=>{
 assert.deepEqual(v.card(''),{value:'',error:''});assert.deepEqual(v.iban(''),{value:'',error:''});
});
