import struct, json, datetime, collections
from rec import *

objs={126623494:'Karfarma_Tbl1',971150505:'ReportWorklast',1163151189:'ReportWork',1195151303:'Karfarma_Tbl',1237579447:'TypeFactorTbl',1243151474:'Entryandexitlist',1298103665:'AttachAriya_Tbl',1330103779:'Pic_Tbl',1646628909:'Entryandexitlist1'}
# column names
names={}
for off,so in rows_for(41):
    r=parse_record(off,so,None); f=r['fixed']
    oid=struct.unpack_from('<i',f,0)[0]
    if oid not in objs: continue
    colid=struct.unpack_from('<i',f,6)[0]
    names[(oid,colid)]=s16(r['var'][0][0])
# rscols
rs={}
for off,so in rows_for(3,0):
    r=parse_record(off,so,None); f=r['fixed']
    rsid=struct.unpack_from('<q',f,0)[0]
    rscolid,hbcolid=struct.unpack_from('<ii',f,8)
    ti=struct.unpack_from('<i',f,24)[0]
    offset=struct.unpack_from('<h',f,40)[0]; nullbit=struct.unpack_from('<i',f,44)[0]
    rs.setdefault(rsid,[]).append(dict(colid=rscolid,hb=hbcolid,xtype=ti&0xff,length=ti>>8,offset=offset,nullbit=nullbit&0xffff))
# rowset -> obj, hobt id -> pages
tables={}
for off,so in rows_for(5,0):
    r=parse_record(off,so,None); f=r['fixed']
    rid=struct.unpack_from('<q',f,0)[0]; idmajor,idminor=struct.unpack_from('<ii',f,9); rcrows=struct.unpack_from('<q',f,27)[0]
    if idmajor in objs: tables[rid]=dict(name=objs[idmajor],oid=idmajor,rows=rcrows)
au={}
for off,so in rows_for(7,0):
    r=parse_record(off,so,None); f=r['fixed']
    auid=struct.unpack_from('<q',f,0)[0]; typ=f[8]; owner=struct.unpack_from('<q',f,9)[0]
    if owner in tables:
        tables[owner].setdefault('au',{})[typ]=(auid>>16)&0xffffffff

def dec_date(b):
    days=int.from_bytes(b,'little'); return (datetime.date(1,1,1)+datetime.timedelta(days=days)).isoformat()
def dec_time(b):
    v=int.from_bytes(b,'little'); s=v/10**7
    h=int(s//3600); m=int(s%3600//60); sec=s%60
    return f"{h:02d}:{m:02d}:{sec:09.6f}"
def dec_dt(b):
    t,days=struct.unpack('<Ii',b)
    return (datetime.datetime(1900,1,1)+datetime.timedelta(days=days,seconds=t/300)).isoformat(sep=' ',timespec='milliseconds')

def read_lob(ptr, kind):
    """Resolve text/row-overflow pointers. Returns bytes."""
    return None

def find_page(fid,pid):
    return pages.get((fid,pid))

def parse_lob_root(pid,fid,slot):
    off=find_page(fid,pid)
    if off is None: return None
    so=slots(off)[slot]
    base=off+so
    # blob fragment: status(1) ? ; blob record: [status 2][blobid 8][type 2]...
    typ=struct.unpack_from('<H',d,base+10)[0]  # blob type: 0=small root?,2=internal,3=data,4=large root yukon, 5=small root..
    return base,typ

def read_blob(pid,fid,slot,depth=0):
    off=find_page(fid,pid)
    if off is None: return b'<<MISSING PAGE %d>>'%pid
    sl=slots(off)
    if slot>=len(sl): return b'<<BAD SLOT>>'
    base=off+sl[slot]
    # blob structure: 2 bytes status, 8 bytes blobid, 2 bytes type
    btype=struct.unpack_from('<H',d,base+10)[0]
    if btype==3: # DATA
        length=struct.unpack_from('<H',d,base+12)[0]
        return d[base+14:base+14+length-14] if False else d[base+14: base+length]
    if btype==2: # INTERNAL
        maxlinks,curlinks,level=struct.unpack_from('<HHH',d,base+12)
        out=b''
        p=base+20
        for i in range(curlinks):
            offs,=struct.unpack_from('<I',d,p); cpid,cfid,cslot=struct.unpack_from('<IHH',d,p+4+0) if False else (struct.unpack_from('<I',d,p+4)[0],struct.unpack_from('<H',d,p+8)[0],struct.unpack_from('<H',d,p+10)[0])
            out+=read_blob(cpid,cfid,cslot,depth+1); p+=12
        return out
    if btype==5: # SMALL_ROOT
        length=struct.unpack_from('<H',d,base+12)[0]
        return d[base+16:base+16+length]
    if btype==4 or btype==0: # LARGE_ROOT_YUKON
        maxlinks,curlinks,level=struct.unpack_from('<HHH',d,base+12)
        out=b''
        p=base+20
        for i in range(curlinks):
            size,=struct.unpack_from('<I',d,p); cpid=struct.unpack_from('<I',d,p+4)[0]; cfid=struct.unpack_from('<H',d,p+8)[0]; cslot=struct.unpack_from('<H',d,p+10)[0]
            out+=read_blob(cpid,cfid,cslot,depth+1); p+=12
        return out
    return b'<<BLOB TYPE %d>>'%btype

def resolve_var(vb, big, xtype, length):
    if not big: return vb
    # complex column: 2 bytes type. 0x0002 -> text pointer/ row-overflow pointer (24 bytes), 0x0004 -> forwarded/back pointer, 0x0001 -> ... (sparse)
    ctype=struct.unpack_from('<H',vb,0)[0]
    if ctype==2 and len(vb)==24:
        # row-overflow/ LOB root pointer: 2 type, 1 level, 1 unused, 4 seq, 8 timestamp, 4 length? layout: [type2][level1][unused1][seq4][timestamp8][length4][pageid4][fileid2][slot2]? Actually 24: 2+1+1+4+8 ... 
        level=vb[2]; length_=struct.unpack_from('<I',vb,12)[0]; pid=struct.unpack_from('<I',vb,16)[0]; fid=struct.unpack_from('<H',vb,20)[0]; slot=struct.unpack_from('<H',vb,22)[0]
        return read_blob(pid,fid,slot)[:length_]
    if ctype==2 and len(vb)==16:
        # text pointer (16 bytes): timestamp 8, pageid 4, fileid 2, slot 2
        pid=struct.unpack_from('<I',vb,8)[0]; fid=struct.unpack_from('<H',vb,12)[0]; slot=struct.unpack_from('<H',vb,14)[0]
        return read_blob(pid,fid,slot)
    if ctype==2 and len(vb)>24:
        # inline root with multiple links: [type2][level1][unused1][seq4][timestamp8] then repeated (length4,pid4,fid2,slot2)
        out=b''; p=16
        while p+12<=len(vb):
            ln=struct.unpack_from('<I',vb,p)[0]; pid=struct.unpack_from('<I',vb,p+4)[0]; fid=struct.unpack_from('<H',vb,p+8)[0]; slot=struct.unpack_from('<H',vb,p+10)[0]
            out+=read_blob(pid,fid,slot); p+=12
        return out
    return b'<<COMPLEX %d len %d>>'%(ctype,len(vb))

def decode_value(xtype,length,b):
    if b is None: return None
    if xtype==56: return struct.unpack('<i',b)[0]
    if xtype==127: return struct.unpack('<q',b)[0]
    if xtype==52: return struct.unpack('<h',b)[0]
    if xtype==48: return b[0]
    if xtype==104: return b
    if xtype in (231,239): return b.decode('utf-16le',errors='replace')
    if xtype in (167,175): return b.decode('cp1256',errors='replace')
    if xtype==61: return dec_dt(b)
    if xtype==40: return dec_date(b)
    if xtype==41: return dec_time(b)
    return b.hex()

def dump_table(t):
    hobt=t['au'].get(1)
    cols=sorted(rs[[k for k,v in tables.items() if v is t][0]],key=lambda c:c['colid'])
    out=[]
    stats=collections.Counter()
    for (fid,pid),off in sorted(pages.items()):
        h=hdr(off)
        if h['type']!=1 or h['objId']!=hobt: continue
        for si,so in enumerate(slots(off)):
            if so==0 or so>=8192: continue
            r=parse_record(off,so,None)
            stats[r['rtype']]+=1
            if r['rtype'] not in (0,1): continue
            f=r['fixed']; row={}
            for c in cols:
                nm=names.get((t['oid'],c['colid']),f'col{c["colid"]}')
                nb=c['nullbit']-1
                if nb>=0 and c['nullbit']<=r['ncols'] and r['isnull'](nb): row[nm]=None; continue
                if c['offset']>0:
                    o=c['offset']-4
                    if c['xtype']==104:
                        row[nm]=bool(f[o]>>0 &1) if o<len(f) else None
                    else:
                        sz={56:4,127:8,52:2,48:1,61:8,40:3,41:5}.get(c['xtype'],c['length'])
                        row[nm]=decode_value(c['xtype'],c['length'],f[o:o+sz]) if o+sz<=len(f) else None
                else:
                    vi=-c['offset']-1
                    if vi<len(r['var']):
                        vb,big=r['var'][vi]
                        vb=resolve_var(vb,big,c['xtype'],c['length'])
                        row[nm]=decode_value(c['xtype'],c['length'],vb)
                    else: row[nm]=None
            row['_page']=pid; row['_slot']=si
            out.append(row)
    return out,stats

if __name__=='__main__':
    import sys
    allt={}
    for rid,t in tables.items():
        rows,stats=dump_table(t)
        print(t['name'],'expected',t['rows'],'got',len(rows),dict(stats))
        allt[t['name']]=rows
    json.dump(allt,open('aria_dump.json','w'),ensure_ascii=False,indent=1,default=str)
