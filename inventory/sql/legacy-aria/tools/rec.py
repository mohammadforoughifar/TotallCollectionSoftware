import struct
from pages import d, pages, hdr

def slots(off):
    h=hdr(off)
    n=h['slotCnt']
    res=[]
    for i in range(n):
        so=struct.unpack_from('<H',d,off+8192-2*(i+1))[0]
        res.append(so)
    return res

def parse_record(off, so, fixed_cols):
    """fixed_cols: list of (name,size) for fixed part in order. returns dict with fixed bytes, null bitmap, var cols list"""
    base=off+so
    status=d[base]
    rtype=(status>>1)&7
    hasnull=bool(status&0x10); hasvar=bool(status&0x20)
    fixlen=struct.unpack_from('<H',d,base+2)[0]
    fixed=d[base+4:base+fixlen]
    p=base+fixlen
    ncols=struct.unpack_from('<H',d,p)[0]; p+=2
    nb=(ncols+7)//8
    nullbits=d[p:p+nb]; p+=nb
    varcols=[]
    if hasvar:
        nvar=struct.unpack_from('<H',d,p)[0]; p+=2
        ends=[struct.unpack_from('<H',d,p+2*i)[0] for i in range(nvar)]
        p+=2*nvar
        start=p-base
        for e in ends:
            big=bool(e&0x8000); e&=0x7fff
            varcols.append((d[base+start:base+e], big))
            start=e
    out={'rtype':rtype,'fixed':fixed,'ncols':ncols,'nullbits':nullbits,'var':varcols,'raw_off':base}
    def isnull(i): return bool(nullbits[i//8]>>(i%8)&1) if i//8<len(nullbits) else True
    out['isnull']=isnull
    return out

def rows_for(objid, indexid=1):
    for (fid,pid),off in sorted(pages.items()):
        h=hdr(off)
        if h['type']==1 and h['objId']==objid and h['indexId']==indexid:
            for so in slots(off):
                if so==0 or so>=8192: continue
                yield off,so

def s16(b): return b.decode('utf-16le',errors='replace')

def datetime_from(b):
    days,ticks=struct.unpack('<iI',b[4:8]+b[0:4]) if False else (struct.unpack('<I',b[0:4])[0],struct.unpack('<i',b[4:8])[0])
    import datetime
    t,days=struct.unpack('<Ii',b)
    return datetime.datetime(1900,1,1)+datetime.timedelta(days=days,seconds=t/300)
