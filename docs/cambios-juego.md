# Cambios hechos en la carpeta del juego (fuera de scripts/)

Estado al 24/11/2026. Todo lo que se desactivo para probar volvio a su lugar.

| Archivo | Cambio | Estado |
|---|---|---|
| `IV_Weapon_Wheel.ini` | `DROP_WEAPON_KEY` E -> G (chocaba con la E de interactuar) | activo, copia en `.bak` |
| `plugins/ProperCoverandDrive-By.ini` | `[ShoulderSwap] Key` 69 (E) -> 72 (H) | activo, copia en `.bak` |
| `plugins/FirstPersonCutscene.ini` | `SaveKey` F8 -> F5 (Ctrl+F8 abria su menu y el de VidaNiko) | activo, copia en `.bak` |
| `plugins/SISCO.asi` | desactivado para pruebas | reactivado |
| `IV_Weapon_Wheel.asi`, `FirstPersonCutscene.asi`, `ProperCoverandDrive-By.asi` | desactivados para pruebas | reactivados |
| `update/common/data/*` (autos agregados) | desactivados para pruebas | reactivados |
| `update/2a, 2b, 2c, 10` (mods de mapa) | sacados para pruebas | devueltos |

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
