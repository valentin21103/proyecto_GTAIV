"""Leer y armar archivos IMG v3 de GTA IV (con o sin cifrado).

Uso: import img; img.cargar_clave(r"...\\GTAIV.exe"); r = img.read("x.img"); data = img.build([("w_glock.wdr", bytes), ...])
"""
import struct
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
import hashlib, os
# La clave AES de los IMG es de Rockstar: no se guarda en el repo, se busca en tu GTAIV.exe
_KEY_SHA1 = bytes.fromhex('DEA375EF1E6EF2223A1221C2C575C47BF17EFA5E')
KEY = None
def cargar_clave(gtaiv_exe):
    global KEY
    d = open(gtaiv_exe, 'rb').read()
    for i in range(len(d) - 32):
        if hashlib.sha1(d[i:i+32]).digest() == _KEY_SHA1:
            KEY = d[i:i+32]; return KEY
    raise RuntimeError('No se encontro la clave en ' + gtaiv_exe)
def _ecb(data,enc):
    c=Cipher(algorithms.AES(KEY),modes.ECB())
    n=len(data)//16*16; head,tail=data[:n],data[n:]
    for _ in range(16):
        o=c.encryptor() if enc else c.decryptor()
        head=o.update(head)+o.finalize()
    return head+tail
def dec(b): return _ecb(b,False)
def enc(b): return _ecb(b,True)
def read(p):
    d=open(p,'rb').read()
    encd=False
    h=d[:20]
    if struct.unpack('<I',h[:4])[0]!=0xA94E2A52:
        h=dec(d[:16])+d[16:20]; encd=True
    magic,ver,n,tsize,isz,unk=struct.unpack('<IIIIHH',h)
    t=d[20:20+tsize]
    if encd: t=dec(t)
    items=[]
    for i in range(n):
        items.append(struct.unpack('<IIIHH',t[i*16:i*16+16]))
    names=t[n*16:].split(b'\0')[:n]
    return dict(enc=encd,ver=ver,n=n,tsize=tsize,isz=isz,unk=unk,items=items,names=[x.decode() for x in names],data=d)

def build(files, encrypt=True):
    # files: list of (name, bytes); all must be RSC resources
    n=len(files)
    names=b''.join(nm.encode()+b'\0' for nm,_ in files)
    tsize=n*16+len(names)
    off=-(-(20+tsize)//2048)
    items=b''; body=b''
    cur=off
    for nm,b in files:
        assert b[:4]==b'RSC\x05', nm
        rtype=struct.unpack('<I',b[4:8])[0]; flags=struct.unpack('<I',b[8:12])[0]
        ub=-(-len(b)//2048); pad=ub*2048-len(b)
        items+=struct.pack('<IIIHH',flags,rtype,cur,ub,0x2000|pad)
        body+=b+b'\0'*pad; cur+=ub
    table=items+names
    hdr=struct.pack('<IIIIHH',0xA94E2A52,3,n,tsize,16,233)
    if encrypt:
        hdr=enc(hdr[:16])+hdr[16:]; table=enc(table)
    head=hdr+table
    head+=b'\0'*(off*2048-len(head))
    return head+body
