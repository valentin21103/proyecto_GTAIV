# Armas

En GTA IV **el sonido de un arma depende del nombre de su modelo** (objetos `W_<MODELO>` en
`update/pc/audio/config/GAME.DAT16`), no del tipo de arma. Por eso cambiamos modelos de lugar.

## Pistolas

| ID | Tipo en WeaponInfo | Arma | Modelo | Daño | Pausa (ms) | Balas | Extra |
|---|---|---|---|---|---|---|---|
| 7 | PISTOL | Glock 19 | w_glock | 30 | 200 | 15 | |
| 27 | EPISODIC_7 | Beretta M9A3 | w_e1_cz75 | 35 | 200 | 12 | semiautomática (anim gun@handgun) |
| 43 | EPISODIC_23 | Pistola con silenciador | w_e3_silenced | 30 | 200 | 17 | |
| 9 | DEAGLE | FN Five-seveN | w_e2_44amag | 25 | 80 | 20 | atraviesa chalecos, sonido P90 |
| 29 | EPISODIC_9 | Desert Eagle | w_eagle | 60 | 450 | 7 | sonido Desert Eagle original |
| 8 | UNUSED0 | Revólver 8 Bull | w_e2_eagle | 85 | 550 | 8 | |

`weapons.ide` también se cambió (anim de cada modelo, y se volvió a agregar `w_eagle`).

## Pistolas duales (Double Homicide)
- `fireMode=stagger`: derecha, izquierda, derecha... (antes disparaban las dos juntas).
- `model` y `weaponModel` actualizados a los modelos nuevos (en CE el mod busca el arma por modelo).
- `shootPower` igual al daño de cada pistola.

## Prioridad de carpetas en update/ (FusionFix)
`00.0ArmasNuevas` > `00.AnimatedWeapons` > `00.DoubleHomicide` > ... > `9X.TACE.Lite`
(menor orden alfabético = mayor prioridad; números antes que letras).
