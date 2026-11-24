"""Copia tools/EstiloIV.cs dentro de cada script nuestro (ScriptHookDotNet compila cada .cs por separado).

Uso: python3 tools/estilo_sync.py   (desde la raiz del repo)
"""
import re, os
raiz = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
bloque = open(os.path.join(raiz, 'tools', 'EstiloIV.cs')).read().rstrip('\n') + '\n'
SCRIPTS = ['VidaNiko.cs', 'MisAutos.cs', 'NikoLook.cs', 'ArmasRapidas.cs', 'MotorAuto.cs']
for n in SCRIPTS:
    p = os.path.join(raiz, 'juego', 'scripts', n)
    s = open(p).read()
    if '// <estilo-iv>' in s:
        s = re.sub(r'    // <estilo-iv>.*?// </estilo-iv>\n', lambda m: bloque, s, flags=re.S)
    else:
        i = s.rstrip().rfind('}')          # cierre del namespace
        s = s[:i] + bloque + s[i:]
    open(p, 'w').write(s)
    print('ok', n)
