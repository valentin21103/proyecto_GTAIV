// Heridos.cs - la gente cae herida en vez de aguantar tiro tras tiro.
//
// Cuando una persona recibe varios impactos (o le queda poca vida) cae al piso,
// suelta el arma y queda tirada agonizando. Despues de un rato se desangra.
// Un tiro en la cabeza sigue matando de una (eso lo hace el juego).
// Opciones en scripts\Heridos.ini (se crea solo la primera vez).

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
        int impactosParaCaer = 3;        // impactos que lo tiran al piso
        int vidaRestanteParaCaer = 25;   // o si le queda este % de vida o menos
        int segundosHastaMorir = 45;     // 0 = no se mueren solos
        bool soloArmasDeFuego = true;    // ignorar golpes chicos (pinas, caidas)
        float radio = 80f;

        class Estado
        {
            public Ped Ped;
            public int VidaInicial, VidaAnterior, Impactos;
            public bool Caido, Ignorar;
            public int CaidoDesde, ProximoRagdoll, VistoUltima;
        }

        List<Estado> estados = new List<Estado>();
        int proximoEscaneo;
        string iniPath;

        public Heridos()
        {
            this.Interval = 100;
            iniPath = "scripts\\Heridos.ini";
            CargarIni();
            this.Tick += new EventHandler(OnTick);
        }

        void CargarIni()
        {
            try
            {
                if (!File.Exists(iniPath))
                {
                    File.WriteAllLines(iniPath, new string[] {
                        "; Heridos - la gente cae herida en vez de aguantar muchos tiros",
                        "; 1 = activado, 0 = desactivado",
                        "Activado = 1",
                        "; impactos que hacen caer a una persona",
                        "ImpactosParaCaer = 3",
                        "; tambien cae si le queda este porcentaje de vida o menos",
                        "VidaRestanteParaCaer = 25",
                        "; segundos tirada hasta desangrarse (0 = no muere sola)",
                        "SegundosHastaMorir = 45",
                    });
                    return;
                }
                foreach (string raw in File.ReadAllLines(iniPath))
                {
                    string l = raw.Trim();
                    if (l.Length == 0 || l.StartsWith(";") || l.IndexOf('=') < 0) continue;
                    string k = l.Substring(0, l.IndexOf('=')).Trim().ToLowerInvariant();
                    string v = l.Substring(l.IndexOf('=') + 1).Trim();
                    int n;
                    if (!int.TryParse(v, out n)) continue;
                    if (k == "activado") activado = n != 0;
                    else if (k == "impactosparacaer") impactosParaCaer = Math.Max(1, n);
                    else if (k == "vidarestanteparacaer") vidaRestanteParaCaer = Math.Max(0, Math.Min(90, n));
                    else if (k == "segundoshastamorir") segundosHastaMorir = Math.Max(0, n);
                }
            }
            catch { }
        }

        Estado Buscar(Ped p)
        {
            foreach (Estado e in estados) if (e.Ped.Equals(p)) return e;
            return null;
        }

        int Vida(Ped p) { try { return p.Health; } catch { return -1; } }

        void OnTick(object sender, EventArgs ev)
        {
            if (!activado) return;
            int now = Game.GameTime;
            Ped me;
            try { me = Player.Character; } catch { return; }
            if (!Game.Exists(me)) return;

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
                    if (e.Ignorar) continue;   // companeros de mision o personajes con vida extra

                    if (!e.Caido)
                    {
                        int dano = e.VidaAnterior - vida;
                        if (dano > 0 && (!soloArmasDeFuego || dano >= 8)) e.Impactos++;
                        e.VidaAnterior = vida;
                        bool pocaVida = vida * 100 <= e.VidaInicial * vidaRestanteParaCaer;
                        if (dano > 0 && !p.isInVehicle() && (e.Impactos >= impactosParaCaer || pocaVida))
                            Derribar(e, now);
                    }
                    else
                    {
                        if (p.isInVehicle()) { e.Caido = false; continue; }
                        if (now >= e.ProximoRagdoll)
                        {
                            try { p.ForceRagdoll(6000, false); } catch { }
                            e.ProximoRagdoll = now + 5000;
                        }
                        if (segundosHastaMorir > 0 && now - e.CaidoDesde > segundosHastaMorir * 1000)
                        {
                            try { Function.Call("SET_CHAR_HEALTH", p, 0); } catch { }
                        }
                    }
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

        void Derribar(Estado e, int now)
        {
            Ped p = e.Ped;
            e.Caido = true;
            e.CaidoDesde = now;
            e.ProximoRagdoll = now + 5000;
            try { Function.Call("REMOVE_ALL_CHAR_WEAPONS", p); } catch { }
            try { Function.Call("SET_BLOCKING_OF_NON_TEMPORARY_EVENTS", p, 1); } catch { }
            try { p.Task.ClearAllImmediately(); } catch { }
            try { p.ForceRagdoll(6000, false); } catch { }
        }
    }
}
