// Heridos.cs - la gente reacciona a los tiros segun que tan herida esta.
//
//   Leve:  sigue peleando como siempre (lo maneja el juego).
//   Medio: (le queda poca vida o varios impactos) cae, se levanta, suelta el arma y se va
//          rengueando, caminando, sin correr ni disparar.
//   Grave: (casi sin vida o muchos impactos) queda en el piso medio vivo, quejandose y sin arma.
//          Algunos en vez de quedarse tirados se rinden: de rodillas suplicando o con las manos arriba.
//          Despues de un rato se desangran (SegundosHastaMorir, 0 = nunca).
//   Un tiro en la cabeza sigue matando de una (eso lo hace el juego).
//   Durante las misiones no toca a nadie: los scripts de mision cambian la vida de sus personajes.
// Opciones en scripts\Heridos.ini (se crea solo; las claves nuevas se agregan solas).

using System;
using System.Collections.Generic;
using System.IO;
using GTA;
using GTA.Native;

namespace HeridosMod
{
    public class Heridos : Script
    {
        // --- opciones (se pueden cambiar en Heridos.ini) ---
        bool activado = true;
        int impactosMedio = 3;           // impactos que lo dejan herido medio (se va rengueando)
        int impactosGrave = 5;           // impactos que lo dejan grave (en el piso)
        int vidaMedio = 30;              // % de vida o menos: herido medio
        int vidaGrave = 20;              // % de vida o menos: grave
        int segundosHastaMorir = 120;    // grave: segundos hasta desangrarse (0 = no muere solo)
        int chanceRendirse = 35;         // % de los graves que se rinden en vez de quedarse tirados
        float radio = 70f;

        const int LEVE = 0, MEDIO = 1, GRAVE = 2;
        const int G_TIRADO = 0, G_SUPLICA = 1, G_MANOS = 2;

        class Estado
        {
            public Ped Ped;
            public int VidaInicial, VidaAnterior, Impactos, Nivel, Forma;
            public bool Ignorar;
            public int Desde, Proximo, VistoUltima, Paso;
        }

        List<Estado> estados = new List<Estado>();
        int proximoEscaneo;
        string iniPath;
        Random rnd = new Random();

        static readonly string[] SetsAnims = { "move_injured_generic", "amb@injured_front", "amb@injured_side", "injured", "plead", "get_up" };

        public Heridos()
        {
            this.Interval = 100;
            iniPath = "scripts\\Heridos.ini";
            CargarIni();
            this.Tick += new EventHandler(OnTick);
        }

        // ---------------------------------------------------------------- ini
        void CargarIni()
        {
            try
            {
                List<string> lineas = File.Exists(iniPath) ? new List<string>(File.ReadAllLines(iniPath)) : new List<string>();
                Dictionary<string, int> vals = new Dictionary<string, int>();
                foreach (string raw in lineas)
                {
                    string l = raw.Trim();
                    if (l.Length == 0 || l.StartsWith(";") || l.IndexOf('=') < 0) continue;
                    string k = l.Substring(0, l.IndexOf('=')).Trim().ToLowerInvariant();
                    int n;
                    if (int.TryParse(l.Substring(l.IndexOf('=') + 1).Trim(), out n)) vals[k] = n;
                }
                int v;
                if (vals.TryGetValue("activado", out v)) activado = v != 0;
                if (vals.TryGetValue("impactosmedio", out v)) impactosMedio = Math.Max(1, v);
                if (vals.TryGetValue("impactosgrave", out v)) impactosGrave = Math.Max(impactosMedio, v);
                if (vals.TryGetValue("vidamedio", out v)) vidaMedio = Math.Max(0, Math.Min(90, v));
                if (vals.TryGetValue("vidagrave", out v)) vidaGrave = Math.Max(0, Math.Min(vidaMedio, v));
                if (vals.TryGetValue("segundoshastamorir", out v)) segundosHastaMorir = Math.Max(0, v);
                if (vals.TryGetValue("chancerendirse", out v)) chanceRendirse = Math.Max(0, Math.Min(100, v));

                // archivo nuevo o de la version anterior: se escribe completo con los valores actuales
                if (!vals.ContainsKey("impactosgrave"))
                {
                    File.WriteAllLines(iniPath, new string[] {
                        "; Heridos - la gente reacciona a los tiros segun que tan herida esta",
                        "; 1 = activado, 0 = desactivado",
                        "Activado = " + (activado ? 1 : 0),
                        "; herido medio: cae, se levanta, suelta el arma y se va rengueando",
                        "ImpactosMedio = " + impactosMedio,
                        "VidaMedio = " + vidaMedio,
                        "; grave: queda en el piso medio vivo, o se rinde",
                        "ImpactosGrave = " + impactosGrave,
                        "VidaGrave = " + vidaGrave,
                        "; % de los graves que se rinden (de rodillas o con las manos arriba)",
                        "ChanceRendirse = " + chanceRendirse,
                        "; segundos que tarda un grave en desangrarse (0 = no muere solo)",
                        "SegundosHastaMorir = " + segundosHastaMorir,
                    });
                }
            }
            catch { }
        }

        // ---------------------------------------------------------------- utilidades
        Estado Buscar(Ped p)
        {
            foreach (Estado e in estados) if (e.Ped.Equals(p)) return e;
            return null;
        }

        int Vida(Ped p) { try { return p.Health; } catch { return -1; } }

        void Pedir()
        {
            foreach (string s in SetsAnims) { try { Function.Call("REQUEST_ANIMS", s); } catch { } }
        }

        bool Listo(string set) { try { return Function.Call<bool>("HAVE_ANIMS_LOADED", set); } catch { return false; } }

        void Animar(Ped p, string set, string clip, bool loop)
        {
            try
            {
                if (!Listo(set)) { Function.Call("REQUEST_ANIMS", set); return; }
                p.Task.PlayAnimation(new AnimationSet(set), clip, 4f, loop ? AnimationFlags.Unknown05 : AnimationFlags.None);
            }
            catch { }
        }

        bool Animando(Ped p, string set, string clip)
        {
            try { return Function.Call<bool>("IS_CHAR_PLAYING_ANIM", p, set, clip); } catch { return false; }
        }

        bool EnElPiso(Ped p)
        {
            try { return p.isRagdoll; } catch { return false; }
        }

        bool EsDeMiGrupo(Ped p)
        {
            try
            {
                int pid = Function.Call<int>("GET_PLAYER_ID");
                GTA.Native.Pointer g = new GTA.Native.Pointer(typeof(int));
                Function.Call("GET_PLAYER_GROUP", pid, g);
                return Function.Call<bool>("IS_GROUP_MEMBER", p, (int)g);
            }
            catch { return false; }
        }

        // deja de tocar a todos: les devuelve sus reacciones normales
        void Soltar()
        {
            foreach (Estado e in estados)
            {
                if (e.Nivel == LEVE) continue;
                try { if (Game.Exists(e.Ped)) Function.Call("SET_BLOCKING_OF_NON_TEMPORARY_EVENTS", e.Ped, 0); } catch { }
            }
            estados.Clear();
        }

        // ---------------------------------------------------------------- tick
        void OnTick(object sender, EventArgs ev)
        {
            if (!activado) return;
            int now = Game.GameTime;
            Ped me;
            try { me = Player.Character; } catch { return; }
            if (!Game.Exists(me)) return;

            // En las misiones no se toca a nadie: los scripts de mision cambian la vida de sus personajes
            // (eso parecia un tiro y los tiraba al piso, y despues los mataba).
            bool mision = false;
            try { mision = Function.Call<bool>("GET_MISSION_FLAG"); } catch { }
            if (mision) { if (estados.Count > 0) Soltar(); return; }

            Ped[] peds;
            try { peds = World.GetPeds(me.Position, radio); } catch { return; }

            foreach (Ped p in peds)
            {
                try
                {
                    if (!Game.Exists(p) || p.Equals(me)) continue;
                    if (!p.isAlive) continue;
                    Estado e = Buscar(p);
                    int vida = Vida(p);
                    if (vida < 0) continue;
                    if (e == null)
                    {
                        e = new Estado(); e.Ped = p; e.VidaInicial = Math.Max(vida, 1); e.VidaAnterior = vida;
                        e.Ignorar = vida > 200 || EsDeMiGrupo(p);
                        estados.Add(e);
                    }
                    e.VistoUltima = now;
                    if (e.Ignorar) continue;   // companeros o personajes con vida extra

                    int dano = e.VidaAnterior - vida;
                    e.VidaAnterior = vida;
                    if (dano >= 8) e.Impactos++;   // golpes chicos (pinas, caidas) no cuentan

                    if (dano > 0 && !p.isInVehicle())
                    {
                        int pct = vida * 100 / e.VidaInicial;
                        int nivel = LEVE;
                        if (e.Impactos >= impactosGrave || pct <= vidaGrave) nivel = GRAVE;
                        else if (e.Impactos >= impactosMedio || pct <= vidaMedio) nivel = MEDIO;
                        if (nivel > e.Nivel) Empezar(e, nivel, me, now);
                    }

                    if (e.Nivel == MEDIO) TickMedio(e, me, now);
                    else if (e.Nivel == GRAVE) TickGrave(e, now);
                }
                catch { }
            }

            if (now >= proximoEscaneo)
            {
                proximoEscaneo = now + 3000;
                estados.RemoveAll(delegate (Estado x)
                {
                    try { return !Game.Exists(x.Ped) || !x.Ped.isAlive || now - x.VistoUltima > 10000; }
                    catch { return true; }
                });
            }
        }

        // ---------------------------------------------------------------- estados
        void Empezar(Estado e, int nivel, Ped me, int now)
        {
            Ped p = e.Ped;
            bool antes = e.Nivel != LEVE;
            e.Nivel = nivel; e.Desde = now; e.Paso = 0;
            Pedir();
            try { Function.Call("REMOVE_ALL_CHAR_WEAPONS", p); } catch { }   // suelta el arma: no pelea mas
            try { Function.Call("SET_BLOCKING_OF_NON_TEMPORARY_EVENTS", p, 1); } catch { }
            try { p.Task.ClearAllImmediately(); } catch { }
            if (nivel == GRAVE)
            {
                int r = rnd.Next(100);
                e.Forma = r < chanceRendirse ? (rnd.Next(2) == 0 ? G_SUPLICA : G_MANOS) : G_TIRADO;
                // el que se rinde se queda donde esta; el que queda tirado se cae
                if (e.Forma == G_TIRADO || !antes) { try { p.ForceRagdoll(3000, false); } catch { } }
                e.Proximo = now + 3200;
            }
            else
            {
                try { p.ForceRagdoll(2000, false); } catch { }   // cae y despues se levanta
                e.Proximo = now + 2600;
            }
        }

        // se levanta, suelta el arma y se va rengueando, caminando, lejos del jugador
        void TickMedio(Estado e, Ped me, int now)
        {
            Ped p = e.Ped;
            if (p.isInVehicle()) { e.Nivel = LEVE; return; }
            if (now < e.Proximo || EnElPiso(p)) return;
            e.Proximo = now + 4000;
            try
            {
                if (Listo("move_injured_generic")) Function.Call("SET_ANIM_GROUP_FOR_CHAR", p, "move_injured_generic");
                Vector3 pp = p.Position, mp = me.Position;
                float dx = pp.X - mp.X, dy = pp.Y - mp.Y, l = (float)Math.Sqrt(dx * dx + dy * dy);
                if (l < 0.5f) { dx = 1f; dy = 0f; l = 1f; }
                Vector3 destino = new Vector3(pp.X + dx / l * 40f, pp.Y + dy / l * 40f, pp.Z);
                Function.Call("TASK_GO_STRAIGHT_TO_COORD", p, destino.X, destino.Y, destino.Z, 2, -1);   // 2 = caminando
                Function.Call("SET_CHAR_KEEP_TASK", p, 1);
            }
            catch { }
            // ya se alejo: se lo deja tranquilo (sigue rengueando)
            try { if (p.Position.DistanceTo(me.Position) > 45f) { e.Ignorar = true; Function.Call("SET_BLOCKING_OF_NON_TEMPORARY_EVENTS", p, 0); } } catch { }
        }

        // en el piso medio vivo, o rendido; despues de un rato se desangra
        void TickGrave(Estado e, int now)
        {
            Ped p = e.Ped;
            if (p.isInVehicle()) { e.Nivel = LEVE; return; }
            if (segundosHastaMorir > 0 && now - e.Desde > segundosHastaMorir * 1000)
            {
                try { Function.Call("SET_CHAR_HEALTH", p, 0); } catch { }
                return;
            }
            if (now < e.Proximo || EnElPiso(p)) return;
            e.Proximo = now + (e.Forma == G_MANOS ? 7500 : 2500);
            try
            {
                if (e.Forma == G_SUPLICA)
                {
                    if (!Animando(p, "plead", "plead")) Animar(p, "plead", "plead", true);
                }
                else if (e.Forma == G_MANOS)
                {
                    Function.Call("TASK_HANDS_UP", p, 8000);
                }
                else
                {
                    // tirado: se queja de costado, boca abajo o boca arriba
                    string set = "amb@injured_side", clip = "idle01";
                    int v = (int)(e.Ped.GetHashCode() & 0x7fffffff) % 3;
                    if (v == 1) set = "amb@injured_front";
                    else if (v == 2) { set = "injured"; clip = "inj_back_idle"; }
                    if (!Animando(p, set, clip)) Animar(p, set, clip, true);
                }
            }
            catch { }
        }
    }
}
