# Proyecto GTA IV

Mi GTA IV Complete Edition (Steam, **1.2.0.59**) modeado para jugar la historia con Niko de forma más
inmersiva: armas nuevas y balanceadas, ciudad viva, autos propios con nafta, todo en español.

Este repo guarda **lo que hicimos nosotros**: scripts propios, versiones modificadas de scripts de
otros autores, archivos de datos del juego mezclados a mano y herramientas. Los mods de terceros
**no** se suben: están listados con su link en [`docs/mods-terceros.md`](docs/mods-terceros.md).

## Estructura

```
juego/        espejo de la carpeta GTAIV: se copia encima de la instalación
  scripts/    scripts .cs (ScriptHookDotNet) y sus .ini
  plugins/    configuración de FusionFix y First Person
  update/     archivos de datos que carga FusionFix (armas, textos en español, sonido)
tools/        herramientas en Python (IMG, GXT, GAME.DAT16, desarmar .net.dll)
docs/         mods de terceros, controles, armas, ideas a futuro
instalar.ps1  copia juego/ a la carpeta del GTA IV (con backup)
```

## Instalación (sobre una base ya armada)

1. Instalar los mods de [`docs/mods-terceros.md`](docs/mods-terceros.md) en el orden indicado.
2. Armar `ArmasNuevas.img` con `tools/armar_armas_nuevas.py` (los modelos no están en el repo).
3. Ejecutar en PowerShell:
   ```powershell
   .\instalar.ps1 -Juego "D:\SteamLibrary\steamapps\common\Grand Theft Auto IV\GTAIV"
   ```

## Scripts propios

| Script | Qué hace | Teclas |
|---|---|---|
| `VidaNiko.cs` | Salud (desangrarse; gasa, analgésico, jeringa), necesidades (hambre, sed, vejiga, intestino), comida y bebida con el objeto en la mano, cigarrillos, almacenes, guantera del auto, sentarse | `U` usar / mantener `U` rueda, `E` comprar (hablando con el empleado), `Z` sentarse, `N` orinar, `Ctrl+F8` registrar lugar |
| `MisAutos.cs` | Garage personal estilo GTA V + nafta + baúl con armas | `L` guardar / lista, `E` baúl / estación / bidón |
| `Heridos.cs` | Los NPC caen heridos, sueltan el arma y se desangran | — (`Heridos.ini`) |
| `ArmasRapidas.cs` | Selector de armas para probar | `Shift+B` / `Shift+N` elegir, `Shift+M` agarrar |
| `NikoLook.cs` | Menú de aspecto de Niko (Ultimate Customizable Niko) | `F11` |
| `MotorAuto.cs` | Apagar/prender motor y luces | `I` |

Ver todos los controles en [`docs/controles.md`](docs/controles.md).

## Verificar que compilan

Los `.cs` se compilan solos al abrir el juego. Para chequearlos antes (Linux/Mono):

```bash
mcs -target:library -r:ScriptHookDotNet.dll -r:System.Windows.Forms.dll -r:System.Drawing.dll -out:/tmp/t.dll juego/scripts/MisAutos.cs
```
(`ScriptHookDotNet.dll` = copia de `ScriptHookDotNet.asi` del juego.)

## Estilo visual

Todas nuestras interfaces usan `tools/EstiloIV.cs` (colores de `common/data/hudColor.dat` del juego). ScriptHookDotNet compila cada script por separado, así que `tools/estilo_sync.py` copia esa clase dentro de cada `.cs`: editá `tools/EstiloIV.cs` y corré el script.
