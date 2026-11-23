# Mods de terceros (base de la instalación)

Juego: **GTA IV Complete Edition 1.2.0.59 (Steam)**. Nada de Vortex en la raíz del juego: los
symlinks de Vortex en la raíz hacían crashear al iniciar (MTLX.dll). Todo va como archivos reales.

## 1. Base
| Mod | Notas |
|---|---|
| Gillian "Liberty Drag-and-Drop" 1.2 | Ultimate ASI Loader (dinput8.dll), DXVK, base |
| FusionFix 5.1.1 | `plugins/`; config propia en `juego/plugins` |
| DLSS-IV, SISCO, ConsoleSelectMenuIV, FusionRainDroplets, LibertyCityPlates, ProperCoverandDrive-By | `plugins/` |
| ScriptHookDotNet 1.7.2.0 (Priler) + aCompleteEditionHook.asi (2021) | raíz del juego |

## 2. Gráficos, sangre y sonido
| Mod | Notas |
|---|---|
| Natural Timecycle | `update/` |
| Rivers of Blood | `update/Rivers of Blood` + efectos |
| Dark & Gritty Weapon Sounds V3 | mezclado dentro de `resident.rpf`, `EP1_SFX.rpf`, `EP2_SFX.rpf` |

## 3. Armas
| Mod | Notas |
|---|---|
| TACE.lite 1.2 (CE 1.2.0.59) | `update/9X.TACE.Lite`, `9Y.GTAIV.EFLC.FusionFix`; requiere partida nueva |
| Animated Weapons 3.0 | solo los modelos/animaciones en `update/00.AnimatedWeapons` (el .asi crashea) |
| Double Homicide 1.3 (pistolas duales) | `scripts/DoubleHomicide.*` + `update/00.DoubleHomicide`; config propia |
| IV Weapon Wheel | raíz; `Tab` rueda, `Q` radio |
| Modelos (LibertyCity.net): Glock 19, Beretta M9A3, FN Five-seveN, (H&K USP sin usar) | se arman con `tools/armar_armas_nuevas.py` |

## 4. Gameplay (scripts)
| Mod | Notas |
|---|---|
| ActivitiesPlus 1.4.2.1 (Nexus 1571) | 10 módulos `.net.dll` |
| Greet-Taunt-Rob (1530), Stop Right There (1538), Pusher, Dynamic Scenarios (1601) | versiones propias en español y con teclado |
| First Person Overhaul IV 1.2 | `plugins/FirstPersonCutscene.*` |
| Steering Animations for First Person | `update/00.FirstPersonSteering` |
| VRegen | `scripts/VRegen.cs` |

## 5. Personaje
| Mod | Notas |
|---|---|
| Ultimate Customizable Niko 0.8 (Nexus 1676) | `playerped.rpf`, `pedVariations.dat`, props; menú propio `NikoLook.cs` |

## Revisados y descartados (no andan en CE o pisan archivos)
Liberty Rush, Potential Grim (solo se podría aprovechar la parte de datos), Brake Lights Mod,
Simple Traffic Loader (IV-SDK .NET), Liberty Tweaks / Project Thunder (IV-SDK .NET), WeaponAnimations.asi.
