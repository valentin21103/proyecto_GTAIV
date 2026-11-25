# Cambios hechos en la carpeta del juego (fuera de scripts/)

Estado al 25/11/2026.

| Archivo | Cambio | Estado |
|---|---|---|
| `IV_Weapon_Wheel.ini` | `DROP_WEAPON_KEY` E -> G (chocaba con la E de interactuar) | activo, copia en `.bak` |
| `plugins/ProperCoverandDrive-By.ini` | `[ShoulderSwap] Key` 69 (E) -> 72 (H) | activo, copia en `.bak` |
| `plugins/FirstPersonCutscene.ini` | `SaveKey` F8 -> F5 (Ctrl+F8 abria su menu y el de VidaNiko) | activo, copia en `.bak` |
| `plugins/SISCO.asi` | desactivado para pruebas | reactivado |
| `IV_Weapon_Wheel.asi`, `FirstPersonCutscene.asi`, `ProperCoverandDrive-By.asi` | desactivados para pruebas | reactivados |
| `update/common/data/*` (autos agregados) | desactivados para pruebas | reactivados |
| `update/2a, 2b, 2c, 10` (mods de mapa) | sacados para pruebas | devueltos |
| `update/pc/data/maps/props/lev_des/minigame.ide` | apartado para probar la terminal de la policia; no era la causa | devuelto |
| `plugins/ProperCoverandDrive-By.asi`, `plugins/FirstPersonCutscene.asi` | apagados para la misma prueba; no eran la causa | reactivados |
| `IV_Weapon_Wheel.asi` | apagado para la misma prueba y vuelto a prender el 25/11 | activo |
| `Savefiles/Rockstar Games/GTA IV/ControlMap.dat` | renombrado a `.bak` para que el juego cree los controles de fabrica | el juego crea uno nuevo |
| `plugins/GTAIV.EFLC.FusionFix.cfg` | rendimiento: ShadowFilter 2->1, ExtraNightShadows 1->0, DepthOfField 2->1, DisplayFpsCounter 0->1 | activo, copia en `.bak_20261125` |

## Partida guardada y objetos interactivos
La partida guarda "este modelo usa este script" por numero de modelo. Al agregar/sacar modelos con mods
(armas, peds, autos, props) esos numeros se corren y los objetos quedan sin script (paso con las
computadoras del TW@ en Logging On). VidaNiko lo resuelve para las computadoras: con E frente a un monitor
abre `computerMain` pasandole el monitor, igual que el script del juego. Si otros objetos (cajeros, puestos
de comida, gaseosas, pool, bowling, dardos) no responden, se puede re-registrar su script igual.

## Funciones del juego revisadas
Todas las llamadas `Function.Call` de nuestros scripts se compararon con el uso real en los scripts de
Rockstar (script.img). Corregido: `GET_CHAR_SPEED` (escribe en un puntero; causaba los
"AccessViolationException" del log) y `DOES_EXTRA_EXIST` (no existe en 1.2.0.59).

## Computadora del patrullero (mision de Brucie)
El juego abre la computadora (`policetest`) desde `main.sco` ("R3 triggers") solo en 6 modelos fijos:
police, police2, noose, polpatriot, pstockade y fbi. El mod de autos (`update/common/data/vehicles.ide`)
agrega patrulleros de TBoGT (police3, police4, policew) que el juego reconoce como policia (la mision muestra
"Pulsa E...") pero que no tienen computadora: la E no hacia nada. VidaNiko la abre igual que el juego
(`START_NEW_SCRIPT("policetest", 1024)`, auto parado, derecho y en 4 ruedas); en los 6 modelos originales
espera 2,5 s por si la abre el juego.
