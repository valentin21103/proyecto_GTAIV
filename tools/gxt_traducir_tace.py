"""Genera spanish2.gxt (textos de armerias y paracaidas de TACE) traduciendo american2.gxt.
Uso: python gxt_traducir_tace.py american2.gxt spanish2.gxt"""
import struct,re,sys
from gxt import read
W={'Pistol':'Pistola','Combat Pistol':'Pistola de combate','Silenced Pistol':'Pistola con silenciador','Automatic 9mm':'9 mm automática','44':'.44','8 Bull':'8 Bull',
'SMG':'Subfusil','Micro-SMG':'Micro-SMG','Assault SMG':'Subfusil de asalto','Gold SMG':'Subfusil dorado','Pump Shotgun':'Escopeta de corredera','Combat Shotgun':'Escopeta de combate',
'Sawn-Off Shotgun':'Escopeta recortada','Assault Shotgun':'Escopeta de asalto','Auto Shotgun Standard':'Escopeta automática estándar','Auto Shotgun Explosive':'Escopeta automática explosiva',
'Assault Rifle':'Rifle de asalto','Carbine Rifle':'Carabina','Advanced MG':'Ametralladora avanzada','Minigun':'Minigun','Sniper Rifle':'Rifle de francotirador','Combat Sniper':'Francotirador de combate',
'Advanced Sniper':'Francotirador avanzado','RPG':'RPG','Grenade Launcher':'Lanzagranadas','Flamethrower':'Lanzallamas','Grenade':'Granada','Molotov':'Molotov','Pipe Bomb':'Bomba casera',
'Sticky Bomb':'Bomba adhesiva','Baseball Bat':'Bate de béisbol','Nightstick':'Porra','Revolver':'Revólver','Automatic 9mm ':'9 mm automática'}
AMMO={'Launching Grenades':'granadas para lanzagranadas','RP Grenade':'cohete RPG','Flamethrower fuel':'combustible para lanzallamas','Molotov':'Molotov','Grenade':'granada',
'Pipe Bomb':'bomba casera','Sticky Bomb':'bomba adhesiva','Automatic 9mm':'balas de 9 mm automática','Auto Shotgun Standard':'cartuchos de escopeta automática estándar'}
BUY='~s~Pulsa ~INPUT_FRONTEND_ACCEPT~ para comprar. ~n~~s~Pulsa ~INPUT_PICKUP~ para cancelar.'
M={
'Lose your wanted level.':'Pierde tu nivel de búsqueda.',
'Use ~PAD_LSTICK_ALL~ to move in the air. ~n~Press ~INPUT_SPRINT~ to activate parachute. ~n~Hold ~INPUT_MELEE_ATTACK1~ to view different cameras.':'Usa ~PAD_LSTICK_ALL~ para moverte en el aire. ~n~Pulsa ~INPUT_SPRINT~ para abrir el paracaídas. ~n~Mantén ~INPUT_MELEE_ATTACK1~ para cambiar de cámara.',
"~s~New stock is always being added, so if you don't see something you need now, come back later.":'~s~Siempre entra mercadería nueva: si ahora no ves lo que necesitas, vuelve más tarde.',
'Display Model Only: Check back soon!':'Solo exhibición: ¡vuelve pronto!',
'~s~Walk up to each weapon to see which are currently available.':'~s~Acércate a cada arma para ver cuáles están disponibles.',
'This will replace your current weapon in this slot. ~n~~s~Press ~INPUT_FRONTEND_ACCEPT~ to confirm. ~s~Press ~INPUT_PICKUP~ to cancel.':'Esto reemplaza el arma que tienes en este espacio. ~n~~s~Pulsa ~INPUT_FRONTEND_ACCEPT~ para confirmar. ~s~Pulsa ~INPUT_PICKUP~ para cancelar.',
'You already have a bat.':'Ya tienes un bate.',
'Use ~PAD_LSTICK_ALL~ to shift body weight. ~n~Use ~PAD_LT~ and ~PAD_RT~ to apply left and right air brakes. ~n~Press ~INPUT_ENTER~ to detach the parachute. ~n~Hold ~INPUT_MELEE_ATTACK1~ to view different cameras.':'Usa ~PAD_LSTICK_ALL~ para inclinar el cuerpo. ~n~Usa ~PAD_LT~ y ~PAD_RT~ para frenar a la izquierda y a la derecha. ~n~Pulsa ~INPUT_ENTER~ para soltar el paracaídas. ~n~Mantén ~INPUT_MELEE_ATTACK1~ para cambiar de cámara.',
'Use ~PAD_LSTICK_ALL~ to shift body weight. ~n~Use ~PAD_LB~ and ~PAD_RB~ to apply left and right air brakes. ~n~Press ~INPUT_ENTER~ to detach the parachute. ~n~Hold ~INPUT_MELEE_ATTACK1~ to view different cameras.':'Usa ~PAD_LSTICK_ALL~ para inclinar el cuerpo. ~n~Usa ~PAD_LB~ y ~PAD_RB~ para frenar a la izquierda y a la derecha. ~n~Pulsa ~INPUT_ENTER~ para soltar el paracaídas. ~n~Mantén ~INPUT_MELEE_ATTACK1~ para cambiar de cámara.',
'~s~You can buy weapons from various gun stores throughout Liberty City.':'~s~Puedes comprar armas en varias armerías de Liberty City.',
'You already have a Nightstick.':'Ya tienes una porra.',
'Purchase a ~g~Micro-SMG.':'Compra un ~g~Micro-SMG.',
'~s~Press ~INPUT_PICKUP~ to examine a weapon.':'~s~Pulsa ~INPUT_PICKUP~ para examinar un arma.',
'Press ~INPUT_ATTACK~ to put on the parachute.':'Pulsa ~INPUT_ATTACK~ para ponerte el paracaídas.',
'~s~Press ~INPUT_FRONTEND_ACCEPT~ to buy the Parachute for $600.':'~s~Pulsa ~INPUT_FRONTEND_ACCEPT~ para comprar el paracaídas por $600.',
'~s~Press ~INPUT_FRONTEND_ACCEPT~ to buy the body armor.':'~s~Pulsa ~INPUT_FRONTEND_ACCEPT~ para comprar el chaleco antibalas.',
'~s~Press ~INPUT_FRONTEND_ACCEPT~ to buy armor. ~n~~g~Body Armor $500':'~s~Pulsa ~INPUT_FRONTEND_ACCEPT~ para comprar blindaje. ~n~~g~Chaleco antibalas $500',
'~s~Press ~INPUT_FRONTEND_ACCEPT~ to buy. ~n~~s~Press ~INPUT_PICKUP~ to cancel.':BUY,
'Baseball Bat ~g~$5':'Bate de béisbol ~g~$5','Nightstick ~g~$25':'Porra ~g~$25',
"~s~Press ~INPUT_FRONTEND_ACCEPT~ to buy. ~n~~s~Press ~INPUT_PICKUP~ to cancel. ~n~~g~You don't have enough cash for that weapon.":BUY+' ~n~~g~No tienes suficiente dinero para esa arma.',
}
def tr(s):
    if s in M: return M[s]
    p='~s~Press ~INPUT_FRONTEND_ACCEPT~ to buy. ~n~~s~Press ~INPUT_PICKUP~ to cancel. ~n~~g~'
    if s.startswith(p):
        r=s[len(p):]
        m=re.fullmatch(r'(.+) purchased\.',r)
        if m: return BUY+' ~n~~g~Compraste: '+W[m.group(1)]+'.'
        m=re.fullmatch(r'(.+) (\$\d+)',r)
        if m: return BUY+' ~n~~g~'+W[m.group(1)]+' '+m.group(2)
    m=re.fullmatch(r'~s~Press ~INPUT_FRONTEND_ACCEPT~ to buy (ammo|fuel)\. ~n~~g~(\d+) ?x (.+) (\$\d+)',s)
    if m:
        n,name,price=m.group(2),m.group(3),m.group(4)
        what='combustible' if m.group(1)=='fuel' else 'munición'
        if name.endswith(' Rounds'):
            base=name[:-7]; desc='balas de '+W.get(base,base)
        else: desc=AMMO[name]
        if n=='1' and name in('Molotov','Grenade','Pipe Bomb','Sticky Bomb','RP Grenade'): desc=AMMO[name]
        return f'~s~Pulsa ~INPUT_FRONTEND_ACCEPT~ para comprar {what}. ~n~~g~{n}x {desc} {price}'
    raise Exception('no:'+s)
src=read(sys.argv[1]); out=[(h,tr(s)) for h,s in src]
# write
out.sort(key=lambda x:x[0])
dat=b'';ents=b''
for h,s in out:
    ents+=struct.pack('<II',len(dat),h); dat+=s.encode('utf-16le')+b'\0\0'
open(sys.argv[2],'wb').write(b'\x04\x00\x10\x00'+b'TKEY'+struct.pack('<I',len(ents))+ents+b'TDAT'+struct.pack('<I',len(dat))+dat)
for h,s in out: print(s)
