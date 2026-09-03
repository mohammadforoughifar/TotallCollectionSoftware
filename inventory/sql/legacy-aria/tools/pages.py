"""خواندن صفحات ۸K دیتای SQL Server مستقیم از فایل بک‌آپ (MTF بدون فشرده‌سازی).
استفاده: ARIA_BAK=/path/aria.bak python3 dump.py   →   aria_dump.json
"""
import struct, collections
import os, sys
BAK=os.environ.get('ARIA_BAK') or (sys.argv[1] if len(sys.argv)>1 else '/tmp/aria/aria.bak')
d=open(BAK,'rb').read()
n=len(d)
pages={}  # (fileid,pageid)->offset
for off in range(512, n-8192+1, 512):
    hv=d[off]; typ=d[off+1]
    if hv==1 and typ in (1,2,3,4,8,9,10,11,13,15,16,17,18,19,20):
        pid,fid=struct.unpack_from('<IH',d,off+32)
        if fid in (1,) and pid<100000:
            pages[(fid,pid)]=off
def hdr(off):
    h={}
    h['type']=d[off+1]; h['level']=d[off+3]
    h['indexId']=struct.unpack_from('<H',d,off+6)[0]
    h['prevPage']=struct.unpack_from('<IH',d,off+8)
    h['pminlen']=struct.unpack_from('<H',d,off+14)[0]
    h['nextPage']=struct.unpack_from('<IH',d,off+16)
    h['slotCnt']=struct.unpack_from('<H',d,off+22)[0]
    h['objId']=struct.unpack_from('<I',d,off+24)[0]
    h['freeCnt'],h['freeData']=struct.unpack_from('<HH',d,off+28)
    h['pageId'],h['fileId']=struct.unpack_from('<IH',d,off+32)
    return h
if __name__=='__main__':
    print(len(pages))
    c=collections.Counter()
    for k,off in pages.items():
        h=hdr(off)
        if h['type']==1: c[(h['objId'],h['indexId'])]+=1
    for k,v in sorted(c.items()): print(k,v)
