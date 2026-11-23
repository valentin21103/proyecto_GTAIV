"""Arma update/00.0ArmasNuevas/IV/ArmasNuevas.img con los modelos de armas que reemplazamos.

Los modelos son de otros autores (LibertyCity.net) y no se suben al repo: descargalos
(ver docs/mods-terceros.md), descomprimilos en una carpeta y pasala como argumento.

  python armar_armas_nuevas.py <carpeta_modelos> <GTAIV.exe> <salida.img>

Mapeo (el nombre del modelo decide el SONIDO del arma, ver docs/armas.md):
  Glock 19        -> w_glock      (sonido pistola)
  Beretta M9A3    -> w_e1_cz75    (sonido CZ75)
  FN Five-seveN   -> w_e2_44amag  (sonido parcheado a P90 en GAME.DAT16)
  Revolver 8 Bull -> w_e2_eagle   (sonido Desert Eagle de TBoGT)
  Desert Eagle    -> w_eagle      (sonido Desert Eagle original de GTA IV)
"""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
import img

ARCHIVOS = [
    ('gun@deagle.wad',   'fn-5/gun@deagle.wad'),
    ('w_e1_cz75.wdr',    'beretta-m9a3/w_glock.wdr'),
    ('w_e1_cz75.wtd',    'beretta-m9a3/w_glock.wtd'),
    ('w_e2_44amag.wdr',  'fn-5/w_eagle.wdr'),
    ('w_e2_44amag.wtd',  'fn-5/Black/w_eagle.wtd'),
    ('w_e2_eagle.wdr',   'tace/w_e3_8bull.wdr'),       # extraido de 9X.TACE.Lite/B.Weapons.img
    ('w_e2_eagle.wtd',   'tace/w_e3_8bull.wtd'),
    ('w_eagle.wdr',      'animated_weapons/w_eagle.wdr'),  # de 00.AnimatedWeapons
    ('w_eagle.wtd',      'animated_weapons/w_eagle.wtd'),
    ('w_glock.wdr',      'glock-19/15 round mag/w_glock.wdr'),
    ('w_glock.wtd',      'glock-19/w_glock.wtd'),
]

if __name__ == '__main__':
    base, exe, salida = sys.argv[1:4]
    img.cargar_clave(exe)
    files = [(n, open(os.path.join(base, src), 'rb').read()) for n, src in ARCHIVOS]
    open(salida, 'wb').write(img.build(files))
    print('OK:', salida, [n for n, _ in files])
