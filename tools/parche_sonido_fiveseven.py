"""Le da a la FN Five-seveN (modelo w_e2_44amag) el sonido de disparo y eco del P90 (misma bala 5,7 mm).

  python parche_sonido_fiveseven.py <ruta a update/pc/audio/config/GAME.DAT16>

GAME.DAT16: [u32 version][u32 tam_datos][datos][tabla de nombres]. Cada objeto W_<MODELO> es un
WeaponSettings de 75 bytes; en el byte 10 esta el hash del sonido de disparo y en el 14 el eco.
"""
import struct, re, sys

def objeto(d, nombre):
    ds = struct.unpack('<I', d[4:8])[0]; o = 8 + ds
    nb = nombre.encode()
    for m in re.finditer(re.escape(nb), bytes(d[o:])):
        s = o + m.start()
        if d[s-1] == len(nb):
            off, size = struct.unpack('<II', d[s+len(nb):s+len(nb)+8])
            return 8 + off, size
    raise KeyError(nombre)

if __name__ == '__main__':
    p = sys.argv[1]
    d = bytearray(open(p, 'rb').read())
    fn, _ = objeto(d, 'W_E2_44AMAG'); p90, _ = objeto(d, 'W_E2_P90')
    print('antes', d[fn+10:fn+18].hex())
    d[fn+10:fn+18] = d[p90+10:p90+18]
    open(p, 'wb').write(d)
    print('despues', d[fn+10:fn+18].hex())
