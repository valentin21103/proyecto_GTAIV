import struct,sys
def read(p):
    d=open(p,'rb').read()
    assert d[:4]==b'\x04\x00\x10\x00',d[:4]
    assert d[4:8]==b'TKEY'
    n=struct.unpack('<I',d[8:12])[0]
    ents=[struct.unpack('<II',d[12+i*8:20+i*8]) for i in range(n//8)]
    o=12+n
    assert d[o:o+4]==b'TDAT'
    m=struct.unpack('<I',d[o+4:o+8])[0]
    dat=d[o+8:o+8+m]
    out=[]
    for off,h in ents:
        e=off
        while dat[e:e+2]!=b'\0\0': e+=2
        out.append((h,dat[off:e].decode('utf-16le')))
    return out
if __name__=='__main__':
    for h,s in read(sys.argv[1]): print(hex(h),repr(s))
