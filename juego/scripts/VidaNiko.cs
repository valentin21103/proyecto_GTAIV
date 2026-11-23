// VidaNiko.cs - salud y necesidades de Niko (parte de "Vida de Niko")
//
// SALUD (idea de Bleed & Heal)
//   Con poca vida Niko se desangra de a poco, camina herido y no puede correr.
//   Botiquin: B en una estacion de servicio lo compra, U lo usa (en cualquier lado, a pie).
//
// NECESIDADES
//   Hambre y sed bajan con el tiempo; la vejiga se llena (mas rapido si tomas algo).
//   Comer o tomar en los puestos, maquinas y locales del juego las llena solo
//   (el script detecta cuando el juego te sube la vida al comer o tomar).
//   N = orinar (alivia la vejiga). Si te ve la policia, te gana una estrella.
//   Con hambre o sed muy bajas no podes correr y vas perdiendo vida.
//   Nada de esto te hace perder vida durante las misiones.
//
// Opciones en scripts\VidaNiko.ini (se crea solo).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using GTA;
using GTA.Native;

namespace VidaNikoMod
{
    public class VidaNiko : Script
    {
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);

        const int VK_B = 0x42, VK_U = 0x55, VK_N = 0x4E, VK_SHIFT = 0x10;
        const string ArchivoIni = "scripts\\VidaNiko.ini";
        const string ArchivoEstado = "scripts\\VidaNikoEstado.txt";
        const string ArchivoNafta = "scripts\\Nafta.ini";

        // ---- opciones ----
        bool salud = true, necesidades = true;
        int vidaCritica = 30;          // % de vida por debajo del cual te desangras
        int segundosSangrado = 8;      // cada cuanto perdes 1 punto desangrandote
        int curaBotiquin = 45;         // vida que da un botiquin
        int precioBotiquin = 100, maxBotiquines = 3;
        float minutosHambre = 90f;     // minutos reales de lleno a vacio
        float minutosSed = 60f;
        float minutosVejiga = 75f;     // minutos para llenarse sin tomar nada

        // ---- estado ----
        float hambre = 80f, sed = 80f, vejiga = 20f;   // 0-100 (hambre/sed: 100 = bien; vejiga: 100 = urgente)
        int botiquines = 1;
        int vidaAnterior = -1, vidaMax = 100;
        int proximoSangrado, proximoDano, proximoGuardado, ultimoTick, ignorarSubidaHasta;
        bool herido, sinCorrer, estabaMuerto;
        bool avisoHambre, avisoSed, avisoVejiga;
        int accion, accionFin;        // 1 botiquin, 2 orinar
        bool[] prev = new bool[256];
        string aviso; int avisoHasta;
        List<Vector3> estaciones = new List<Vector3>();
        Dictionary<string, AnimationSet> sets = new Dictionary<string, AnimationSet>();

        public VidaNiko()
        {
            this.Interval = 0;
            CargarIni();
            CargarEstado();
            CargarEstaciones();
            this.Tick += new EventHandler(OnTick);
        }

        // ================= archivos =================
        static float F(string s, float d)
        {
            float v;
            return float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : d;
        }

        void CargarIni()
        {
            try
            {
                if (!File.Exists(ArchivoIni))
                {
                    File.WriteAllLines(ArchivoIni, new string[] {
                        "; VidaNiko - salud y necesidades de Niko",
                        "; 1 = activado, 0 = desactivado",
                        "Salud = 1",
                        "Necesidades = 1",
                        "; vida (%) por debajo de la cual Niko se desangra",
                        "VidaCritica = 30",
                        "; segundos entre cada punto de vida que perdes desangrandote",
                        "SegundosSangrado = 8",
                        "CuraBotiquin = 45",
                        "PrecioBotiquin = 100",
                        "MaxBotiquines = 3",
                        "; minutos reales que tarda cada necesidad de lleno a vacio",
                        "MinutosHambre = 90",
                        "MinutosSed = 60",
                        "MinutosVejiga = 75",
                    });
                    return;
                }
                foreach (string raw in File.ReadAllLines(ArchivoIni))
                {
                    string l = raw.Trim();
                    if (l.Length == 0 || l.StartsWith(";") || l.IndexOf('=') < 0) continue;
                    string k = l.Substring(0, l.IndexOf('=')).Trim().ToLowerInvariant();
                    float v = F(l.Substring(l.IndexOf('=') + 1), -1f);
                    if (v < 0) continue;
                    if (k == "salud") salud = v != 0;
                    else if (k == "necesidades") necesidades = v != 0;
                    else if (k == "vidacritica") vidaCritica = (int)v;
                    else if (k == "segundossangrado") segundosSangrado = Math.Max(1, (int)v);
                    else if (k == "curabotiquin") curaBotiquin = (int)v;
                    else if (k == "preciobotiquin") precioBotiquin = (int)v;
                    else if (k == "maxbotiquines") maxBotiquines = Math.Max(1, (int)v);
                    else if (k == "minutoshambre") minutosHambre = Math.Max(5f, v);
                    else if (k == "minutossed") minutosSed = Math.Max(5f, v);
                    else if (k == "minutosvejiga") minutosVejiga = Math.Max(5f, v);
                }
            }
            catch { }
        }

        void CargarEstado()
        {
            try
            {
                if (!File.Exists(ArchivoEstado)) return;
                foreach (string l in File.ReadAllLines(ArchivoEstado))
                {
                    int i = l.IndexOf('=');
                    if (i < 0) continue;
                    string k = l.Substring(0, i); float v = F(l.Substring(i + 1), -1f);
                    if (v < 0) continue;
                    if (k == "Hambre") hambre = v;
                    else if (k == "Sed") sed = v;
                    else if (k == "Vejiga") vejiga = v;
                    else if (k == "Botiquines") botiquines = (int)v;
                }
            }
            catch { }
        }

        void GuardarEstado()
        {
            try
            {
                File.WriteAllLines(ArchivoEstado, new string[] {
                    "Hambre=" + hambre.ToString("0.0", CultureInfo.InvariantCulture),
                    "Sed=" + sed.ToString("0.0", CultureInfo.InvariantCulture),
                    "Vejiga=" + vejiga.ToString("0.0", CultureInfo.InvariantCulture),
                    "Botiquines=" + botiquines });
            }
            catch { }
        }

        // las estaciones de servicio de Nafta.ini tambien venden botiquines
        void CargarEstaciones()
        {
            try
            {
                if (!File.Exists(ArchivoNafta)) return;
                foreach (string raw in File.ReadAllLines(ArchivoNafta))
                {
                    string l = raw.Trim();
                    if (!l.ToUpperInvariant().StartsWith("ESTACION")) continue;
                    string[] p = l.Substring(l.IndexOf('=') + 1).Split('|');
                    if (p.Length < 2) continue;
                    string[] c = p[1].Split(',');
                    if (c.Length < 3) continue;
                    estaciones.Add(new Vector3(F(c[0], 0), F(c[1], 0), F(c[2], 0)));
                }
            }
            catch { }
        }

        // ================= utilidades =================
        bool Pressed(int vk)
        {
            bool d = (GetAsyncKeyState(vk) & 0x8000) != 0;
            bool r = d && !prev[vk];
            prev[vk] = d;
            return r;
        }

        static bool Down(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }
        void Aviso(string s) { aviso = s; avisoHasta = Game.GameTime + 4500; }
        int PlayerId() { try { return Function.Call<int>("GET_PLAYER_ID"); } catch { return 0; } }
        bool EnMision() { try { return Function.Call<bool>("GET_MISSION_FLAG"); } catch { return false; } }

        void Anim(Ped p, string set, string nombre, bool loop)
        {
            try
            {
                AnimationSet s;
                if (!sets.TryGetValue(set, out s)) { s = new AnimationSet(set); sets[set] = s; }
                p.Task.PlayAnimation(s, nombre, 8f, loop ? AnimationFlags.Unknown05 : AnimationFlags.None);
            }
            catch { }
        }

        void Caminar(Ped me, bool heridoAhora)
        {
            if (heridoAhora == herido) return;
            herido = heridoAhora;
            try { Function.Call("SET_ANIM_GROUP_FOR_CHAR", me, heridoAhora ? "move_injured_generic" : "move_player"); } catch { }
        }

        void SetVida(Ped me, int v)
        {
            ignorarSubidaHasta = Game.GameTime + 600;
            try { me.Health = v; } catch { }
            vidaAnterior = v;
        }

        bool EnEstacion(Vector3 p)
        {
            foreach (Vector3 e in estaciones) if (e.DistanceTo(p) < 22f) return true;
            return false;
        }

        // ================= loop =================
        void OnTick(object sender, EventArgs ev)
        {
            Ped me;
            try { me = Player.Character; } catch { return; }
            if (!Game.Exists(me)) return;
            int now = Game.GameTime;
            float dt = ultimoTick == 0 ? 0f : Math.Min(0.5f, (now - ultimoTick) / 1000f);
            ultimoTick = now;

            // muerte / reaparicion: arrancas comido y sin sangrar
            bool muerto = false;
            try { muerto = me.isDead || !me.isAlive; } catch { }
            if (muerto) { estabaMuerto = true; accion = 0; return; }
            if (estabaMuerto)
            {
                estabaMuerto = false;
                hambre = Math.Max(hambre, 70f); sed = Math.Max(sed, 70f); vejiga = Math.Min(vejiga, 30f);
                vidaAnterior = -1; herido = false;
                try { Function.Call("SET_ANIM_GROUP_FOR_CHAR", me, "move_player"); } catch { }
            }

            int vida = 0;
            try { vida = me.Health; } catch { return; }
            if (vida > vidaMax) vidaMax = vida;
            bool mision = EnMision();

            // detectar comer/tomar: el juego te sube la vida cuando comes o tomas algo
            if (vidaAnterior >= 0 && vida > vidaAnterior && now > ignorarSubidaHasta && accion == 0)
            {
                int sube = vida - vidaAnterior;
                if (necesidades && sube >= 3)
                {
                    if (sube <= 20) { sed = Math.Min(100f, sed + 35f); vejiga = Math.Min(100f, vejiga + 12f); Aviso("Tomaste algo: sed " + (int)sed + "%"); }
                    else { hambre = Math.Min(100f, hambre + 40f); sed = Math.Min(100f, sed + 10f); Aviso("Comiste: hambre " + (int)hambre + "%"); }
                }
            }
            vidaAnterior = vida;

            // ---- necesidades ----
            if (necesidades)
            {
                hambre = Math.Max(0f, hambre - dt * 100f / (minutosHambre * 60f));
                sed = Math.Max(0f, sed - dt * 100f / (minutosSed * 60f));
                vejiga = Math.Min(100f, vejiga + dt * 100f / (minutosVejiga * 60f));

                if (hambre < 25f && !avisoHambre) { avisoHambre = true; Aviso("Niko tiene hambre: come algo en un puesto o local"); }
                if (hambre > 35f) avisoHambre = false;
                if (sed < 25f && !avisoSed) { avisoSed = true; Aviso("Niko tiene sed: toma algo en una maquina o un bar"); }
                if (sed > 35f) avisoSed = false;
                if (vejiga > 80f && !avisoVejiga) { avisoVejiga = true; Aviso("Niko necesita orinar: apreta N"); }
                if (vejiga < 60f) avisoVejiga = false;
                if (vejiga >= 100f && accion == 0)
                {
                    vejiga = 0f;
                    Aviso("No aguantaste mas... Niko se hizo encima");
                }

                // hambre o sed en cero: perdes vida de a poco (no en misiones)
                if ((hambre <= 0f || sed <= 0f) && !mision && now >= proximoDano)
                {
                    proximoDano = now + 6000;
                    if (vida > 15) SetVida(me, vida - 2);
                }
            }

            // ---- salud ----
            int pct = vidaMax > 0 ? vida * 100 / vidaMax : 100;
            bool critico = salud && pct <= vidaCritica;
            Caminar(me, critico);
            if (critico && !mision && accion == 0 && now >= proximoSangrado)
            {
                if (proximoSangrado != 0 && vida > 5) SetVida(me, vida - 1);   // no te mata: te deja en las ultimas
                proximoSangrado = now + segundosSangrado * 1000;
            }
            if (!critico) proximoSangrado = 0;

            bool cansado = necesidades && (hambre < 10f || sed < 10f);
            sinCorrer = critico || cansado;
            if (sinCorrer) { try { Function.Call("DISABLE_PLAYER_SPRINT", PlayerId(), true); } catch { } }
            else { try { Function.Call("DISABLE_PLAYER_SPRINT", PlayerId(), false); } catch { } }

            // ---- acciones ----
            TickAcciones(me, now, vida);

            // ---- dibujo ----
            DibujarHud(pct, critico);
            if (now < avisoHasta && aviso != null) DibujarAviso();

            if (now >= proximoGuardado) { proximoGuardado = now + 15000; GuardarEstado(); }
        }

        void TickAcciones(Ped me, int now, int vida)
        {
            bool shift = Down(VK_SHIFT);
            bool b = Pressed(VK_B) && !shift, u = Pressed(VK_U), n = Pressed(VK_N) && !shift;
            bool aPie = !me.isInVehicle() && me.isAliveAndWell;

            if (accion != 0)
            {
                if (!aPie) { accion = 0; return; }
                if (now >= accionFin)
                {
                    if (accion == 1)
                    {
                        SetVida(me, Math.Min(vidaMax, vida + curaBotiquin));
                        herido = true; Caminar(me, false);
                        Aviso("Te curaste. Botiquines: " + botiquines);
                    }
                    else if (accion == 2)
                    {
                        vejiga = 0f;
                        Aviso("Que alivio");
                    }
                    accion = 0;
                    try { me.Task.ClearAll(); } catch { }
                }
                return;
            }
            if (!aPie) return;

            // comprar botiquin en estaciones de servicio
            if (b && salud && EnEstacion(me.Position))
            {
                if (botiquines >= maxBotiquines) Aviso("Ya tenes " + botiquines + " botiquines (maximo " + maxBotiquines + ")");
                else if (Player.Money < precioBotiquin) Aviso("No te alcanza para un botiquin ($" + precioBotiquin + ")");
                else { Player.Money = Player.Money - precioBotiquin; botiquines++; Aviso("Compraste un botiquin (" + botiquines + "/" + maxBotiquines + ")"); }
            }

            // usar botiquin
            if (u && salud)
            {
                if (botiquines <= 0) Aviso("No tenes botiquines (se compran con B en las estaciones de servicio)");
                else if (vida >= vidaMax) Aviso("No estas herido");
                else
                {
                    botiquines--;
                    accion = 1; accionFin = now + 3500;
                    try { me.Task.ClearAllImmediately(); } catch { }
                    Anim(me, "cop_search_idles", "idle_check_under_vehicle", false);
                }
            }

            // orinar
            if (n && necesidades)
            {
                if (vejiga < 15f) Aviso("No tenes ganas");
                else
                {
                    accion = 2; accionFin = now + 6000;
                    try { me.Task.ClearAllImmediately(); } catch { }
                    Anim(me, "amb@bouncer_idles_b", "lookaround_a", true);
                    // si hay policia cerca y te ve: una estrella
                    try
                    {
                        foreach (Ped p in World.GetPeds(me.Position, 20f))
                        {
                            if (!Game.Exists(p) || p.Equals(me)) continue;
                            int tipo = Function.Call<int>("GET_PED_TYPE", p);
                            if (tipo == 6 && Player.WantedLevel < 1) { Player.WantedLevel = 1; Aviso("Un policia te vio... falta a la moral publica"); break; }
                        }
                    }
                    catch { }
                }
            }
        }

        // ================= dibujo =================
        void Texto(string s, float x, float y, float scale, int r, int g, int b, bool centro)
        {
            Function.Call("SET_TEXT_FONT", 0);
            Function.Call("SET_TEXT_SCALE", scale, scale);
            Function.Call("SET_TEXT_COLOUR", r, g, b, 255);
            Function.Call("SET_TEXT_DROPSHADOW", 1, 0, 0, 0, 255);
            if (centro) Function.Call("SET_TEXT_CENTRE", 1);
            Function.Call("DISPLAY_TEXT_WITH_LITERAL_STRING", x, y, "STRING", s);
        }

        void Barra(string nombre, float valor, float y, bool alerta)
        {
            float x = 0.885f, w = 0.09f, h = 0.010f;
            Function.Call("DRAW_RECT", x, y, w + 0.006f, h + 0.006f, 0, 0, 0, 140);
            Function.Call("DRAW_RECT", x, y, w, h, 60, 60, 60, 200);
            float fw = w * Math.Max(0f, Math.Min(1f, valor / 100f));
            int r = alerta ? 230 : 120, g = alerta ? 70 : 200, b = alerta ? 50 : 120;
            if (fw > 0.001f) Function.Call("DRAW_RECT", x - w / 2f + fw / 2f, y, fw, h, r, g, b, 230);
            Texto(nombre, x - w / 2f - 0.004f - 0.055f, y - 0.011f, 0.22f, 220, 220, 220, false);
        }

        void DibujarHud(int pct, bool critico)
        {
            // solo aparece cuando algo necesita atencion (o mientras haces una accion)
            bool mostrar = accion != 0 || critico || (necesidades && (hambre < 50f || sed < 50f || vejiga > 60f));
            if (!mostrar) return;
            float y = 0.90f;
            if (necesidades)
            {
                Barra("VEJIGA", vejiga, y, vejiga > 80f); y -= 0.022f;
                Barra("SED", sed, y, sed < 25f); y -= 0.022f;
                Barra("HAMBRE", hambre, y, hambre < 25f); y -= 0.022f;
            }
            if (salud) { Barra("SALUD", pct, y, critico); y -= 0.022f; }
            if (critico) Texto("Te estas desangrando  -  U: usar botiquin (" + botiquines + ")", 0.84f, y - 0.012f, 0.22f, 255, 120, 100, true);
            if (accion == 1) Texto("Curandote...", 0.5f, 0.85f, 0.32f, 255, 255, 255, true);
            if (accion == 2) Texto("Orinando...", 0.5f, 0.85f, 0.32f, 255, 255, 255, true);
        }

        void DibujarAviso()
        {
            Function.Call("DRAW_RECT", 0.5f, 0.165f, 0.5f, 0.045f, 0, 0, 0, 160);
            Texto(aviso, 0.5f, 0.148f, 0.30f, 255, 255, 255, true);
        }
    }
}
