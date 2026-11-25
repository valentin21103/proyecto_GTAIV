#!/usr/bin/env python3
"""Genera update/common/text/spanishVN.gxt y americanVN.gxt con los textos de ayuda de nuestros scripts.

FusionFix carga estos archivos extra de texto (igual que spanishDS.gxt de DynamicScenarios).
Cada texto se muestra en la caja de ayuda REAL de GTA IV con PRINT_HELP_FOREVER.

La etiqueta de cada texto es "VN" + joaat(texto ASCII del codigo) en hexa, igual que IV.Etiqueta() en EstiloIV.cs,
asi el codigo llama IV.Ayuda("texto") y no hace falta mantener una lista de claves a mano.

Formato (GTA IV, version 4, UTF-16): u16 4, u16 16, "TKEY" u32 tam, [u32 offset, u32 hash]*, "TDAT" u32 tam, textos.
Las entradas van ordenadas por hash (el juego las busca por busqueda binaria).
"""
import struct, sys, os

# texto tal cual esta en el codigo (ASCII)  ->  texto que se ve en el juego
TEXTOS = {
    "Apreta E para hablar con el empleado.": "Apretá E para hablar con el empleado.",
    "Apreta E para usar la computadora.": "Apretá E para usar la computadora.",
    "Podes caminar fumando. Apreta U para apagar el cigarrillo.": "Podés caminar fumando. Apretá U para apagar el cigarrillo.",
    "Te estas desangrando. Apreta U para ponerte una gasa.": "Te estás desangrando. Apretá U para ponerte una gasa.",
}


def joaat(s):
    h = 0
    for c in s.lower().encode('latin1'):
        h = (h + c) & 0xffffffff
        h = (h + (h << 10)) & 0xffffffff
        h ^= h >> 6
    h = (h + (h << 3)) & 0xffffffff
    h ^= h >> 11
    h = (h + (h << 15)) & 0xffffffff
    return h


def etiqueta(texto):
    return "VN%08X" % joaat(texto)


def armar(textos):
    entradas = sorted(((joaat(etiqueta(k)), v) for k, v in textos.items()), key=lambda e: e[0])
    datos = b""
    claves = b""
    for h, v in entradas:
        claves += struct.pack("<II", len(datos), h)
        datos += v.encode("utf-16-le") + b"\0\0"
    return struct.pack("<HH", 4, 16) + b"TKEY" + struct.pack("<I", len(claves)) + claves + b"TDAT" + struct.pack("<I", len(datos)) + datos


if __name__ == "__main__":
    salida = sys.argv[1] if len(sys.argv) > 1 else "juego/update/common/text"
    os.makedirs(salida, exist_ok=True)
    b = armar(TEXTOS)
    for idioma in ("spanish", "american"):
        with open(os.path.join(salida, idioma + "VN.gxt"), "wb") as f:
            f.write(b)
    for k in TEXTOS:
        print(etiqueta(k), k)
