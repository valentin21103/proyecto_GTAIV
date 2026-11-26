// Culatazo.cs - pegar con la culata del arma.
//
// Con un arma de fuego en la mano y alguien pegado adelante tuyo, J hace que Niko le de un culatazo
// con la animacion del propio juego (melee_gun): le saca vida y lo tira al piso un momento.
// Sin nadie cerca, Niko tira el golpe al aire. (La V ya es "cambiar camara" en GTA IV.)
// Agachado usa el golpe bajo (melee_crouch).

using System;
using System.Windows.Forms;
using GTA;
using GTA.Native;

namespace CulatazoMod
{
    public class Culatazo : Script
    {
        const Keys Tecla = Keys.J;
        const float Alcance = 1.7f;      // metros
        const int Dano = 18;             // vida que saca (los peatones tienen ~100-200)
        const int Espera = 900;          // ms entre culatazos

        bool antes;
        int proximo, golpeEn;
        Ped objetivo;

        public Culatazo()
        {
            this.Interval = 0;
            this.Tick += new EventHandler(OnTick);
        }

        bool ArmaDeFuego(Ped me)
        {
            try
            {
                GTA.Native.Pointer w = new GTA.Native.Pointer(typeof(int));
                Function.Call("GET_CURRENT_CHAR_WEAPON", me, w);
                int a = (int)w;
                // 7 pistola, 9..20 armas de fuego, 21+ armas de los episodios y de mods (8 no existe)
                return a == 7 || (a >= 9 && a <= 63);
            }
            catch { return false; }
        }

        Ped Adelante(Ped me)
        {
            Ped mejor = null;
            float md = Alcance;
            try
            {
                Vector3 mp = me.Position;
                Vector3 f = me.Direction;
                foreach (Ped p in World.GetPeds(mp, Alcance + 0.5f))
                {
                    if (p == null || !Game.Exists(p) || p.Equals(me) || !p.isAlive || p.isInVehicle()) continue;
                    Vector3 d = p.Position - mp;
                    float dist = d.Length();
                    if (dist > md || Math.Abs(d.Z) > 1.2f) continue;
                    float dot = (d.X * f.X + d.Y * f.Y) / Math.Max(0.01f, (float)Math.Sqrt(d.X * d.X + d.Y * d.Y));
                    if (dot < 0.4f) continue;   // tiene que estar adelante
                    mejor = p; md = dist;
                }
            }
            catch { }
            return mejor;
        }

        void OnTick(object sender, EventArgs e)
        {
            int now = Game.GameTime;
            bool aprieta = false;
            try { aprieta = Game.isKeyPressed(Tecla); } catch { }
            bool pulso = aprieta && !antes;
            antes = aprieta;

            // el golpe "conecta" un instante despues de empezar la animacion
            if (golpeEn != 0 && now >= golpeEn)
            {
                golpeEn = 0;
                Pegar(objetivo);
                objetivo = null;
            }

            if (!pulso || now < proximo) return;
            Ped me;
            try { me = Player.Character; } catch { return; }
            if (me == null || !Game.Exists(me) || !me.isAliveAndWell || me.isInVehicle()) return;
            try
            {
                int pid = Function.Call<int>("GET_PLAYER_ID");
                if (!Function.Call<bool>("IS_PLAYER_CONTROL_ON", pid)) return;
                if (Function.Call<bool>("IS_PAUSE_MENU_ACTIVE")) return;
            }
            catch { return; }
            if (!ArmaDeFuego(me)) return;

            proximo = now + Espera;
            Ped p = Adelante(me);
            if (p != null)
            {
                try
                {
                    Vector3 d = p.Position - me.Position;
                    me.Heading = (float)(Math.Atan2(-d.X, d.Y) * 180.0 / Math.PI);   // mirarlo de frente
                }
                catch { }
            }
            bool agachado = false;
            try { agachado = Function.Call<bool>("IS_CHAR_DUCKING", me); } catch { }
            try
            {
                Function.Call("REQUEST_ANIMS", "melee_gun");
                me.Task.PlayAnimation(new AnimationSet("melee_gun"), agachado ? "melee_crouch" : "melee", 8f, AnimationFlags.None);
            }
            catch { }
            objetivo = p;
            golpeEn = now + 350;
        }

        void Pegar(Ped p)
        {
            if (p == null) return;
            try
            {
                if (!Game.Exists(p) || !p.isAlive) return;
                Ped me = Player.Character;
                if (p.Position.DistanceTo(me.Position) > Alcance + 0.6f) return;   // se alejo
                p.Health = Math.Max(1, p.Health - Dano);
                p.ForceRagdoll(1800, false);
                Function.Call("SAY_AMBIENT_SPEECH", p, "GENERIC_SHOCKED_HIGH", 1, 1, 0);
            }
            catch { }
        }
    }
}
