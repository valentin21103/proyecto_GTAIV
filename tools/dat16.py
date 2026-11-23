import struct
def parse(p):
    d=open(p,'rb').read()
    ver,ds=struct.unpack('<II',d[:8])
    data=d[8:8+ds]; o=8+ds
    rest=d[o:]
    return ver,data,o,d
