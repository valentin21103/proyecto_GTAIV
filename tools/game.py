"""Ver que sonido usa cada arma (objetos W_* de GAME.DAT16). Uso: python game.py <update/pc/audio/config>"""
import struct,re,sys
import os; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); import dat16
def joaat(s):
    h=0
    for c in s.lower().encode():
        h=(h+c)&0xffffffff; h=(h+(h<<10))&0xffffffff; h^=h>>6
    h=(h+(h<<3))&0xffffffff; h^=h>>11; h=(h+(h<<15))&0xffffffff; return h
A = (sys.argv[1] if len(sys.argv) > 1 else ".") + "/"   # carpeta update/pc/audio/config
names=set()
for f in ['SOUNDS.DAT15','EFLC_SOUNDS.DAT15','GAME.DAT16','eflc_GAME.DAT16','CURVES.DAT12','CATEGORIES.DAT15']:
    try: d=open(A+f,'rb').read()
    except: continue
    for m in re.finditer(rb'[A-Za-z0-9_\\]{3,80}',d): names.add(m.group().decode())
H={joaat(n):n for n in names}
def objs(path):
    ver,data,o,d=dat16.parse(path)
    r={}
    for m in re.finditer(rb'W_[A-Z0-9_]+',d[o:]):
        s=o+m.start(); L=d[s-1]
        if L!=len(m.group()): continue
        e=s+L; off,size=struct.unpack('<II',d[e:e+8]); r[m.group().decode()]=(off,size)
    return data,r,o,d
def show(data,r,k):
    off,size=r[k]; b=data[off:off+size]
    words=[struct.unpack('<I',b[i:i+4])[0] for i in range(10,size-3,4)]
    return [H.get(w, hex(w)) for w in words]

if __name__ == "__main__":
    data, r, o, d = objs(A + "GAME.DAT16")
    for k in sorted(r):
        s = show(data, r, k)
        if s and s[0] != "NULL_SOUND": print(k, s[0], "|", s[1])
