// VidaNiko.cs - salud, necesidades, comida y sentarse (parte de "Vida de Niko")
//
// SALUD
//   Con poca vida Niko empieza a desangrarse hasta que se pone una gasa; con poca vida
//   camina herido y no puede correr.
//
// OBJETOS (se llevan encima, se usan desde la rueda)
//   Gasa (frena la sangre, cura todo) - Analgesico (cura la mitad) - Jeringa (cura de a poco)
//   Hamburguesa, Chocolate (hambre) - Agua, Gaseosa, Cerveza (sed)
//   U rapido = usa la curacion que mas sirve. Mantener U = rueda de objetos:
//   mouse (o flechas) hacia el objeto y solta U. Sentado, comes y tomas sentado.
//
// TIENDAS (B para comprar)
//   Farmacias: curaciones + agua.   Estaciones de servicio: de todo.
//   Restaurantes y bares: comida y bebida.
//   Ctrl+F9 registra una farmacia donde estes, Ctrl+F8 un restaurante (scripts\Lugares.ini).
//
// SENTARSE
//   Z = sentarse (ponete de espaldas al banco, silla o escalon). Z o W para pararte.
//
// NECESIDADES
//   Hambre y sed bajan con el tiempo; vejiga e intestino se llenan. Comer o tomar en los
//   puestos, maquinas y locales del juego tambien cuenta. Con hambre o sed Niko lo demuestra
//   (jadea, bosteza) cuando esta quieto.
//   BANO (bonus, sin castigo): N = orinar, mantener N = ir de cuerpo. Despues "hiciste lugar":
//   la comida y bebida te llenan mas por un rato (x1.25 orinando, x1.5 de cuerpo).
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

        const int VK_B = 0x42, VK_U = 0x55, VK_N = 0x4E, VK_Z = 0x5A, VK_W = 0x57, VK_SHIFT = 0x10, VK_CTRL = 0x11;
        const int VK_F9 = 0x78, VK_F8 = 0x77;
        const int VK_UP = 0x26, VK_DOWN = 0x28, VK_LEFT = 0x25, VK_RIGHT = 0x27, VK_ENTER = 0x0D, VK_BACK = 0x08;
        const string ArchivoIni = "scripts\\VidaNiko.ini";
        const string ArchivoEstado = "scripts\\VidaNikoEstado.txt";
        const string ArchivoNafta = "scripts\\Nafta.ini";
        const string ArchivoLugares = "scripts\\Lugares.ini";
        const string ArchivoFarmaciasViejo = "scripts\\Farmacias.ini";

        // ================= objetos =================
        // tipo: 0 curacion, 1 comida, 2 bebida
        class Obj
        {
            public string Nombre, Desc, Set, Clip, SetSentado, ClipSentado;
            public int Tipo, Precio, Max, Ms, Tengo;
            public float Hambre, Sed, Vejiga;
            public Obj(string n, string d, int tipo, int precio, int max, int ms, float hambre, float sed, float vejiga, string set, string clip, string setS, string clipS)
            { Nombre = n; Desc = d; Tipo = tipo; Precio = precio; Max = max; Ms = ms; Hambre = hambre; Sed = sed; Vejiga = vejiga; Set = set; Clip = clip; SetSentado = setS; ClipSentado = clipS; }
        }
        const int GASA = 0, ANALGESICO = 1, JERINGA = 2;
        Obj[] objs = {
            new Obj("Gasa", "frena la sangre, cura todo", 0, 80, 3, 3500, 0, 0, 0, "cop_search_idles", "idle_check_under_vehicle", null, null),
            new Obj("Analgesico", "cura la mitad", 0, 40, 3, 1200, 0, 0, 0, "amb@beg_standing", "take_obj", null, null),
            new Obj("Jeringa", "cura de a poco", 0, 150, 2, 1200, 0, 0, 0, "amb@beg_standing", "take_obj", null, null),
            new Obj("Hamburguesa", "hambre +50", 1, 8, 3, 6000, 50, 0, 0, "amb@burger_idle", "eat_stand", "amb@cafe_eat_idles", "sit_eat"),
            new Obj("Chocolate", "hambre +15", 1, 3, 5, 4000, 15, 0, 0, "amb@eat_chocolate", "choc_stand", "amb@cafe_eat_idles", "sit_eat"),
            new Obj("Agua", "sed +50", 2, 2, 3, 4000, 0, 50, 12, "amb@drink_bottle", "bottle_stand", "amb@bnch_eat_idl", "sit_idle_a"),
            new Obj("Gaseosa", "sed +40", 2, 3, 3, 4000, 0, 40, 15, "amb@drink_can", "can_stand", "amb@bnch_eat_idl", "sit_idle_a"),
            new Obj("Cerveza", "sed +25", 2, 5, 3, 4000, 0, 25, 22, "amb@drink_bottle", "bottle_stand", "amb@bnch_eat_idl", "sit_idle_a"),
        };

        // ---- opciones ----
        bool salud = true, necesidades = true;
        int vidaCritica = 30, segundosSangrado = 8;
        float minutosHambre = 90f, minutosSed = 60f, minutosVejiga = 75f;

        // ---- estado ----
        float hambre = 80f, sed = 80f, vejiga = 20f, intestino = 10f;
        float bonusComida = 1f; int bonusHasta, proximoGesto, nDesde;
        int vidaAnterior = -1, vidaMax = 100;
        bool sangrando, herido, estabaMuerto;
        int proximoSangrado, proximoDano, proximoGuardado, ultimoTick, ignorarSubidaHasta;
        bool avisoHambre, avisoSed, avisoVejiga, avisoIntestino;
        int accion, accionFin, accionObj = -1;      // 1 usar objeto, 2 orinar, 3 de cuerpo, 4 gesto
        int jeringaHasta, proximaJeringa;
        bool[] prev = new bool[256];
        string aviso; int avisoHasta;

        // sentado: 0 no, 1 sentandose, 2 sentado, 3 parandose
        int sentado, sentadoFase;
        // tienda
        bool tienda; int tiendaSel; Lugar tiendaLugar; List<int> tiendaObjs = new List<int>();
        // rueda
        bool rueda; int ruedaSel = -1; int uDesde; float ruedaX, ruedaY;

        class Lugar { public string Tipo, Nombre; public Vector3 Pos; public float Radio; public Blip B; }
        List<Lugar> lugares = new List<Lugar>();
        Dictionary<string, AnimationSet> sets = new Dictionary<string, AnimationSet>();

        public VidaNiko()
        {
            this.Interval = 0;
            objs[GASA].Tengo = 1; objs[ANALGESICO].Tengo = 1;
            CargarIni();
            CargarEstado();
            CargarLugares();
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
                if (!File.Exists(ArchivoIni) || File.ReadAllText(ArchivoIni).IndexOf("PrecioGasa") < 0)
                {
                    File.WriteAllLines(ArchivoIni, new string[] {
                        "; VidaNiko - salud, necesidades, comida y sentarse",
                        "; 1 = activado, 0 = desactivado",
                        "Salud = 1",
                        "Necesidades = 1",
                        "; vida (%) por debajo de la cual Niko empieza a desangrarse",
                        "VidaCritica = 30",
                        "; segundos entre cada punto de vida que perdes desangrandote",
                        "SegundosSangrado = 8",
                        "; precios",
                        "PrecioGasa = 80",
                        "PrecioAnalgesico = 40",
                        "PrecioJeringa = 150",
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
                    else if (k == "preciogasa") objs[GASA].Precio = (int)v;
                    else if (k == "precioanalgesico") objs[ANALGESICO].Precio = (int)v;
                    else if (k == "preciojeringa") objs[JERINGA].Precio = (int)v;
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
                    else if (k == "Intestino") intestino = v;
                    else foreach (Obj o in objs) if (k == o.Nombre) o.Tengo = (int)v;
                }
            }
            catch { }
        }

        void GuardarEstado()
        {
            try
            {
                List<string> l = new List<string>();
                l.Add("Hambre=" + hambre.ToString("0.0", CultureInfo.InvariantCulture));
                l.Add("Sed=" + sed.ToString("0.0", CultureInfo.InvariantCulture));
                l.Add("Vejiga=" + vejiga.ToString("0.0", CultureInfo.InvariantCulture));
                l.Add("Intestino=" + intestino.ToString("0.0", CultureInfo.InvariantCulture));
                foreach (Obj o in objs) l.Add(o.Nombre + "=" + o.Tengo);
                File.WriteAllLines(ArchivoEstado, l.ToArray());
            }
            catch { }
        }

        void CargarLugares()
        {
            try
            {
                if (!File.Exists(ArchivoLugares))
                {
                    List<string> l = new List<string>();
                    l.Add("; Lugares donde se compra con B.");
                    l.Add("; FARMACIA = nombre | x, y, z | radio      (curaciones y agua)");
                    l.Add("; RESTAURANTE = nombre | x, y, z | radio   (comida y bebida)");
                    l.Add("; En el juego: Ctrl+F9 agrega una farmacia y Ctrl+F8 un restaurante donde estes parado.");
                    l.Add("FARMACIA = Pill-Pharm East Island City | 1299.0, 690.0, 36.0 | 45");
                    // pasar las farmacias registradas con la version anterior (Farmacias.ini)
                    if (File.Exists(ArchivoFarmaciasViejo))
                        foreach (string raw in File.ReadAllLines(ArchivoFarmaciasViejo))
                            if (raw.Trim().StartsWith("FARMACIA") && raw.IndexOf("East Island City") < 0) l.Add(raw.Trim());
                    File.WriteAllLines(ArchivoLugares, l.ToArray());
                }
                LeerLugares(ArchivoLugares, "FARMACIA", true);
                LeerLugares(ArchivoLugares, "RESTAURANTE", true);
                LeerLugares(ArchivoNafta, "ESTACION", false);
            }
            catch { }
        }

        void LeerLugares(string archivo, string clave, bool conBlip)
        {
            if (!File.Exists(archivo)) return;
            foreach (string raw in File.ReadAllLines(archivo))
            {
                string l = raw.Trim();
                if (!l.ToUpperInvariant().StartsWith(clave) || l.IndexOf('=') < 0) continue;
                string[] p = l.Substring(l.IndexOf('=') + 1).Split('|');
                if (p.Length < 2) continue;
                string[] c = p[1].Split(',');
                if (c.Length < 3) continue;
                Lugar g = new Lugar();
                g.Tipo = clave;
                g.Nombre = p[0].Trim();
                g.Pos = new Vector3(F(c[0], 0), F(c[1], 0), F(c[2], 0));
                g.Radio = clave == "ESTACION" ? 22f : (p.Length >= 3 ? F(p[2], 15f) : 15f);
                if (conBlip) PonerBlip(g);
                lugares.Add(g);
            }
        }

        void PonerBlip(Lugar g)
        {
            try { g.B = Blip.AddBlip(g.Pos); g.B.Name = g.Tipo == "FARMACIA" ? "Farmacia" : "Restaurante"; g.B.ShowOnlyWhenNear = false; } catch { }
        }

        void Registrar(Ped me, string tipo)
        {
            try
            {
                Vector3 p = me.Position;
                Lugar g = new Lugar();
                g.Tipo = tipo;
                g.Nombre = (tipo == "FARMACIA" ? "Farmacia " : "Restaurante ") + (lugares.Count + 1);
                g.Pos = p; g.Radio = 15f;
                PonerBlip(g);
                lugares.Add(g);
                File.AppendAllText(ArchivoLugares, tipo + " = " + g.Nombre + " | " +
                    p.X.ToString("0.00", CultureInfo.InvariantCulture) + ", " +
                    p.Y.ToString("0.00", CultureInfo.InvariantCulture) + ", " +
                    p.Z.ToString("0.00", CultureInfo.InvariantCulture) + " | 15\r\n");
                Aviso((tipo == "FARMACIA" ? "Farmacia" : "Restaurante") + " registrado aca (ya podes comprar con B)");
            }
            catch { Aviso("No se pudo registrar el lugar"); }
        }

        Lugar TiendaCerca(Vector3 p)
        {
            foreach (Lugar g in lugares) if (g.Pos.DistanceTo(p) <= g.Radio) return g;
            return null;
        }

        List<int> ObjsDeTienda(Lugar g)
        {
            List<int> r = new List<int>();
            for (int i = 0; i < objs.Length; i++)
            {
                Obj o = objs[i];
                if (g.Tipo == "ESTACION") r.Add(i);
                else if (g.Tipo == "FARMACIA" && (o.Tipo == 0 || o.Nombre == "Agua")) r.Add(i);
                else if (g.Tipo == "RESTAURANTE" && o.Tipo != 0) r.Add(i);
            }
            return r;
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
        void Control(bool on) { try { Function.Call("SET_PLAYER_CONTROL", PlayerId(), on); } catch { } }
        void Lento(bool on) { try { Function.Call("SET_TIME_SCALE", on ? 0.25f : 1.0f); } catch { } }

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

        float Bonus() { return Game.GameTime < bonusHasta ? bonusComida : 1f; }
        void DarBonus(float m, int minutos)
        {
            if (Game.GameTime >= bonusHasta || m >= bonusComida) bonusComida = m;
            bonusHasta = Game.GameTime + minutos * 60000;
        }
        // pantalla a negro mientras Niko va de cuerpo (no hay modelo de Niko sin pantalon)
        void Fundido(bool afuera)
        {
            try { Function.Call(afuera ? "DO_SCREEN_FADE_OUT" : "DO_SCREEN_FADE_IN", afuera ? 1200 : 800); } catch { }
        }

        void SetVida(Ped me, int v)
        {
            ignorarSubidaHasta = Game.GameTime + 600;
            try { me.Health = v; } catch { }
            vidaAnterior = v;
        }

        void Reiniciar()
        {
            bool habiaMenu = tienda || rueda || sentado != 0;
            if (rueda) Lento(false);
            if (habiaMenu) Control(true);
            if (accion == 3) Fundido(false);
            rueda = false; tienda = false; sentado = 0; accion = 0; jeringaHasta = 0;
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

            bool muerto = false;
            try { muerto = me.isDead || !me.isAlive; } catch { }
            if (muerto) { estabaMuerto = true; Reiniciar(); return; }
            if (estabaMuerto)
            {
                estabaMuerto = false;
                hambre = Math.Max(hambre, 70f); sed = Math.Max(sed, 70f); vejiga = Math.Min(vejiga, 30f); intestino = Math.Min(intestino, 30f);
                vidaAnterior = -1; herido = false; sangrando = false;
                try { Function.Call("SET_ANIM_GROUP_FOR_CHAR", me, "move_player"); } catch { }
            }

            int vida = 0;
            try { vida = me.Health; } catch { return; }
            if (vida > vidaMax) vidaMax = vida;
            bool mision = EnMision();

            // detectar comer/tomar del juego (puestos, maquinas, locales): te sube la vida
            if (vidaAnterior >= 0 && vida > vidaAnterior && now > ignorarSubidaHasta && accion == 0 && now > jeringaHasta)
            {
                int sube = vida - vidaAnterior;
                if (necesidades && sube >= 3)
                {
                    float m = Bonus();
                    if (sube <= 20) { sed = Math.Min(100f, sed + 35f * m); vejiga = Math.Min(100f, vejiga + 12f); Aviso("Tomaste algo: sed " + (int)sed + "%"); }
                    else { hambre = Math.Min(100f, hambre + 40f * m); sed = Math.Min(100f, sed + 10f * m); intestino = Math.Min(100f, intestino + 15f); Aviso("Comiste: hambre " + (int)hambre + "%"); }
                }
            }
            vidaAnterior = vida;

            // ---- necesidades ----
            if (necesidades)
            {
                hambre = Math.Max(0f, hambre - dt * 100f / (minutosHambre * 60f));
                sed = Math.Max(0f, sed - dt * 100f / (minutosSed * 60f));
                vejiga = Math.Min(100f, vejiga + dt * 100f / (minutosVejiga * 60f));
                intestino = Math.Min(100f, intestino + dt * 100f / (minutosVejiga * 2f * 60f));
                if (hambre < 25f && !avisoHambre) { avisoHambre = true; Aviso("Niko tiene hambre: come algo (U o un puesto de comida)"); }
                if (hambre > 35f) avisoHambre = false;
                if (sed < 25f && !avisoSed) { avisoSed = true; Aviso("Niko tiene sed: toma algo (U o una maquina)"); }
                if (sed > 35f) avisoSed = false;
                if (vejiga > 80f && !avisoVejiga) { avisoVejiga = true; Aviso("Niko necesita orinar: apreta N"); }
                if (vejiga < 60f) avisoVejiga = false;
                if (intestino > 85f && !avisoIntestino) { avisoIntestino = true; Aviso("Niko necesita ir de cuerpo: mantene N"); }
                if (intestino < 60f) avisoIntestino = false;
                if ((hambre <= 0f || sed <= 0f) && !mision && now >= proximoDano)
                {
                    proximoDano = now + 6000;
                    if (vida > 15) SetVida(me, vida - 2);
                }
            }

            // ---- salud ----
            int pct = vidaMax > 0 ? vida * 100 / vidaMax : 100;
            bool critico = salud && pct <= vidaCritica;
            if (critico && !sangrando) { sangrando = true; Aviso("Te estas desangrando: ponete una gasa (U)"); }
            Caminar(me, critico);
            if (salud && sangrando && !mision && now >= proximoSangrado)
            {
                if (proximoSangrado != 0 && vida > 5) SetVida(me, vida - 1);
                proximoSangrado = now + segundosSangrado * 1000;
            }
            if (!sangrando) proximoSangrado = 0;

            if (now < jeringaHasta && now >= proximaJeringa)
            {
                proximaJeringa = now + 250;
                if (vida < vidaMax) SetVida(me, Math.Min(vidaMax, vida + 1));
            }

            bool cansado = necesidades && (hambre < 10f || sed < 10f);
            try { Function.Call("DISABLE_PLAYER_SPRINT", PlayerId(), critico || cansado); } catch { }

            TickAcciones(me, now, vida);

            DibujarHud(pct, critico);
            if (tienda) DibujarTienda();
            if (rueda) DibujarRueda();
            if (now < avisoHasta && aviso != null) DibujarAviso();

            if (now >= proximoGuardado) { proximoGuardado = now + 15000; GuardarEstado(); }
        }

        // ================= usar objetos =================
        int MejorCuracion(int vida)
        {
            if (sangrando && objs[GASA].Tengo > 0) return GASA;
            if (vida * 100 <= vidaMax * 50 && objs[ANALGESICO].Tengo > 0) return ANALGESICO;
            if (objs[JERINGA].Tengo > 0) return JERINGA;
            if (objs[ANALGESICO].Tengo > 0) return ANALGESICO;
            if (objs[GASA].Tengo > 0) return GASA;
            return -1;
        }

        void Usar(Ped me, int i, int vida, int now)
        {
            if (i < 0) { Aviso("No tenes nada para curarte (compra con B en farmacias o estaciones)"); return; }
            Obj o = objs[i];
            if (o.Tengo <= 0) { Aviso("No tenes " + o.Nombre.ToLowerInvariant()); return; }
            if (o.Tipo == 0 && vida >= vidaMax && !(i == GASA && sangrando)) { Aviso("No estas herido"); return; }
            o.Tengo--;
            accion = 1; accionObj = i; accionFin = now + o.Ms;
            if (sentado == 2)
            {
                if (o.SetSentado != null) Anim(me, o.SetSentado, o.ClipSentado, true);
            }
            else if (sentado == 0)
            {
                try { me.Task.ClearAllImmediately(); } catch { }
                Anim(me, o.Set, o.Clip, o.Tipo != 0);
            }
        }

        void TerminarUso(Ped me, int vida)
        {
            Obj o = objs[accionObj];
            if (accionObj == GASA)
            {
                sangrando = false;
                SetVida(me, vidaMax);
                Aviso("Te pusiste una gasa: la sangre paro. Gasas: " + o.Tengo);
            }
            else if (accionObj == ANALGESICO)
            {
                SetVida(me, Math.Min(vidaMax, vida + vidaMax / 2));
                Aviso("Tomaste un analgesico" + (sangrando ? " (seguis sangrando: ponete una gasa)" : "") + ". Te quedan " + o.Tengo);
            }
            else if (accionObj == JERINGA)
            {
                jeringaHasta = Game.GameTime + 20000;
                Aviso("Te inyectaste: te vas curando de a poco" + (sangrando ? " (seguis sangrando)" : ""));
            }
            else
            {
                float m = Bonus();
                hambre = Math.Min(100f, hambre + o.Hambre * m);
                sed = Math.Min(100f, sed + o.Sed * m);
                vejiga = Math.Min(100f, vejiga + o.Vejiga);
                if (o.Tipo == 1) intestino = Math.Min(100f, intestino + o.Hambre * 0.3f);
                Aviso((o.Tipo == 1 ? "Comiste: " : "Tomaste: ") + o.Nombre.ToLowerInvariant() + "   (hambre " + (int)hambre + "%, sed " + (int)sed + "%)");
            }
        }

        // ================= sentarse =================
        void TickSentado(Ped me, int now, bool z)
        {
            if (sentado == 1 && now >= sentadoFase) { sentado = 2; Anim(me, "amb_sit_chair_m", "sit_down_idle_01", true); }
            else if (sentado == 2 && (z || Down(VK_W)))
            {
                sentado = 3; sentadoFase = now + 1800;
                Anim(me, "amb_sit_chair_m", "get_up_front", false);
            }
            else if (sentado == 3 && now >= sentadoFase)
            {
                sentado = 0;
                try { me.Task.ClearAll(); } catch { }
                Control(true);
            }
        }

        // ================= acciones =================
        void TickAcciones(Ped me, int now, int vida)
        {
            bool shift = Down(VK_SHIFT), ctrl = Down(VK_CTRL), mision = EnMision();
            bool aPie = !me.isInVehicle() && me.isAliveAndWell;
            bool b = Pressed(VK_B) && !shift, z = Pressed(VK_Z) && !shift;
            bool nDown = Down(VK_N) && !shift, nRel = !nDown && prev[VK_N];
            if (nDown && !prev[VK_N]) nDesde = now;
            prev[VK_N] = nDown;
            int nTipo = 0;                                     // 1 tocar, 2 mantener
            if (nDown && nDesde > 0 && now - nDesde > 450) { nTipo = 2; nDesde = 0; }
            else if (nRel && nDesde > 0) { nTipo = 1; nDesde = 0; }
            bool f9 = Pressed(VK_F9), f8 = Pressed(VK_F8);
            bool uDown = Down(VK_U);
            bool uPress = uDown && !prev[VK_U], uRelease = !uDown && prev[VK_U];
            prev[VK_U] = uDown;

            if (!aPie && (sentado != 0 || tienda)) { Reiniciar(); return; }

            // accion en curso (comer, curarse, orinar)
            if (accion != 0)
            {
                if (!aPie) { if (accion == 3) Fundido(false); accion = 0; return; }
                if (now >= accionFin)
                {
                    if (accion == 1) TerminarUso(me, vida);
                    else if (accion == 2) { vejiga = 0f; DarBonus(1.25f, 10); Aviso("Que alivio. Hiciste lugar: la comida te llena mas por 10 min"); }
                    else if (accion == 3) { intestino = 0f; vejiga = Math.Max(0f, vejiga - 40f); DarBonus(1.5f, 15); Fundido(false); Aviso("Liberado. Hiciste lugar: la comida te llena mucho mas por 15 min"); }
                    bool gesto = accion == 4;
                    accion = 0;
                    if (sentado == 0 && !gesto) { try { me.Task.ClearAll(); } catch { } }
                    else if (sentado == 2) Anim(me, "amb_sit_chair_m", "sit_down_idle_01", true);
                }
                return;
            }

            if (ctrl && f9 && aPie) { Registrar(me, "FARMACIA"); return; }
            if (ctrl && f8 && aPie) { Registrar(me, "RESTAURANTE"); return; }

            // ---- tienda ----
            if (tienda)
            {
                int cnt = tiendaObjs.Count;
                if (Pressed(VK_UP)) tiendaSel = (tiendaSel + cnt - 1) % cnt;
                if (Pressed(VK_DOWN)) tiendaSel = (tiendaSel + 1) % cnt;
                if (Pressed(VK_ENTER))
                {
                    Obj o = objs[tiendaObjs[tiendaSel]];
                    if (o.Tengo >= o.Max) Aviso("Ya tenes " + o.Tengo + " (maximo " + o.Max + ")");
                    else if (Player.Money < o.Precio) Aviso("No te alcanza ($" + o.Precio + ")");
                    else { Player.Money = Player.Money - o.Precio; o.Tengo++; Aviso("Compraste: " + o.Nombre + " (" + o.Tengo + "/" + o.Max + ")"); }
                }
                if (b || Pressed(VK_BACK)) { tienda = false; Control(true); }
                return;
            }

            // ---- rueda (mantener U) ----
            if (uPress && aPie) uDesde = now;
            if (uDown && !rueda && uDesde > 0 && now - uDesde > 250 && aPie)
            {
                rueda = true; ruedaSel = -1; ruedaX = 0; ruedaY = 0;
                Lento(true); Control(false);
            }
            if (rueda)
            {
                try
                {
                    Pointer mx = new Pointer(typeof(int)), my = new Pointer(typeof(int));
                    Function.Call("GET_MOUSE_INPUT", mx, my);
                    ruedaX += Convert.ToInt32(mx.Value); ruedaY += Convert.ToInt32(my.Value);
                }
                catch { }
                float r = (float)Math.Sqrt(ruedaX * ruedaX + ruedaY * ruedaY);
                if (r > 40f)
                {
                    double ang = Math.Atan2(ruedaX, -ruedaY);            // 0 = arriba, sentido horario
                    if (ang < 0) ang += 2 * Math.PI;
                    ruedaSel = (int)Math.Round(ang / (2 * Math.PI / objs.Length)) % objs.Length;
                    if (r > 200f) { ruedaX *= 200f / r; ruedaY *= 200f / r; }
                }
                if (Pressed(VK_RIGHT) || Pressed(VK_DOWN)) ruedaSel = (ruedaSel + 1 + objs.Length) % objs.Length;
                if (Pressed(VK_LEFT) || Pressed(VK_UP)) ruedaSel = (Math.Max(ruedaSel, 0) - 1 + objs.Length) % objs.Length;
                if (uRelease || !aPie)
                {
                    rueda = false; Lento(false);
                    if (sentado == 0) Control(true);
                    if (ruedaSel >= 0 && aPie) Usar(me, ruedaSel, vida, now);
                }
                uDesde = 0;
                return;
            }
            if (uRelease && uDesde > 0)
            {
                uDesde = 0;
                if (aPie && salud) Usar(me, MejorCuracion(vida), vida, now);
                return;
            }

            // ---- sentado ----
            if (sentado != 0) { TickSentado(me, now, z); return; }
            if (!aPie) return;

            if (z)
            {
                sentado = 1; sentadoFase = now + 2200;
                try { me.Task.ClearAllImmediately(); } catch { }
                Control(false);
                Anim(me, "amb_sit_chair_m", "sit_down_front", false);
                return;
            }

            // ---- tienda cercana ----
            Lugar t = TiendaCerca(me.Position);
            if (t != null && b)
            {
                tiendaObjs = ObjsDeTienda(t);
                if (tiendaObjs.Count > 0) { tienda = true; tiendaSel = 0; tiendaLugar = t; Control(false); return; }
            }

            // ---- bano (bonus) ----
            if (nTipo == 1 && necesidades)
            {
                if (vejiga < 15f) Aviso("No tenes ganas de orinar");
                else
                {
                    accion = 2; accionFin = now + 6000;
                    try { me.Task.ClearAllImmediately(); } catch { }
                    Anim(me, "amb@bouncer_idles_b", "lookaround_a", true);
                }
            }
            else if (nTipo == 2 && necesidades)
            {
                if (intestino < 25f) Aviso("No tenes ganas de ir de cuerpo");
                else
                {
                    accion = 3; accionFin = now + 9000;
                    try { me.Task.ClearAllImmediately(); } catch { }
                    Anim(me, "move_m@cower", "idle", true);
                    Fundido(true);
                }
            }

            // ---- gestos de hambre / sed (solo quieto, sin mision ni policia) ----
            if (necesidades && now >= proximoGesto && (hambre < 25f || sed < 25f))
            {
                bool grave = hambre < 10f || sed < 10f;
                proximoGesto = now + (grave ? 25000 : 50000);
                float vel = 0f;
                try { vel = Function.Call<float>("GET_CHAR_SPEED", me); } catch { }
                if (vel < 0.5f && !mision && Player.WantedLevel == 0 && !Down(0x02))
                {
                    accion = 4; accionFin = now + 3500;
                    if (sed < hambre) { Anim(me, "amb@yawn", "yawn", false); Aviso("Niko tiene la boca seca..."); }
                    else { Anim(me, "playidles_tired_1h", "heavy_breathing", false); Aviso("A Niko le ruge la panza..."); }
                }
            }

            if (t != null) Ayuda("B: comprar en " + t.Nombre);
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

        void Ayuda(string s)
        {
            Function.Call("DRAW_RECT", 0.5f, 0.945f, 0.4f, 0.035f, 0, 0, 0, 150);
            Texto(s, 0.5f, 0.932f, 0.26f, 255, 255, 255, true);
        }

        void Barra(string nombre, float valor, float y, bool alerta)
        {
            float x = 0.885f, w = 0.09f, h = 0.010f;
            Function.Call("DRAW_RECT", x, y, w + 0.006f, h + 0.006f, 0, 0, 0, 140);
            Function.Call("DRAW_RECT", x, y, w, h, 60, 60, 60, 200);
            float fw = w * Math.Max(0f, Math.Min(1f, valor / 100f));
            int r = alerta ? 230 : 120, g = alerta ? 70 : 200, b = alerta ? 50 : 120;
            if (fw > 0.001f) Function.Call("DRAW_RECT", x - w / 2f + fw / 2f, y, fw, h, r, g, b, 230);
            Texto(nombre, x - w / 2f - 0.059f, y - 0.011f, 0.22f, 220, 220, 220, false);
        }

        void DibujarHud(int pct, bool critico)
        {
            bool mostrar = accion != 0 || critico || sangrando || rueda || Game.GameTime < jeringaHasta ||
                           (necesidades && (hambre < 50f || sed < 50f || vejiga > 60f || intestino > 60f));
            if (!mostrar) return;
            float y = 0.90f;
            if (necesidades)
            {
                Barra("INTESTINO", intestino, y, intestino > 85f); y -= 0.022f;
                Barra("VEJIGA", vejiga, y, vejiga > 80f); y -= 0.022f;
                Barra("SED", sed, y, sed < 25f); y -= 0.022f;
                Barra("HAMBRE", hambre, y, hambre < 25f); y -= 0.022f;
            }
            if (salud) { Barra(sangrando ? "SANGRANDO" : "SALUD", pct, y, critico || sangrando); y -= 0.022f; }
            if (sangrando && !rueda) Texto("U: gasa (" + objs[GASA].Tengo + ")   mantener U: rueda", 0.84f, y - 0.012f, 0.22f, 255, 120, 100, true);
            if (accion == 1 && accionObj >= 0)
            {
                string t = accionObj == GASA ? "Poniendote una gasa..." : accionObj == ANALGESICO ? "Tomando un analgesico..." :
                           accionObj == JERINGA ? "Inyectandote..." : (objs[accionObj].Tipo == 1 ? "Comiendo..." : "Tomando...");
                Texto(t, 0.5f, 0.85f, 0.32f, 255, 255, 255, true);
            }
            if (accion == 2) Texto("Orinando...", 0.5f, 0.85f, 0.32f, 255, 255, 255, true);
            if (accion == 3) Texto("(Niko esta ocupado...)", 0.5f, 0.48f, 0.4f, 255, 255, 255, true);
            if (Game.GameTime < bonusHasta) Texto("Lugar extra: comida x" + bonusComida.ToString("0.00", CultureInfo.InvariantCulture), 0.885f, y - 0.012f, 0.22f, 140, 220, 140, true);
        }

        void DibujarTienda()
        {
            int cnt = tiendaObjs.Count;
            float x = 0.5f, y = 0.26f, alto = 0.07f + cnt * 0.036f;
            Function.Call("DRAW_RECT", x, y + alto / 2f, 0.4f, alto + 0.03f, 0, 0, 0, 180);
            Texto(tiendaLugar != null ? tiendaLugar.Nombre.ToUpperInvariant() : "TIENDA", x, y - 0.005f, 0.36f, 120, 220, 140, true);
            for (int k = 0; k < cnt; k++)
            {
                Obj o = objs[tiendaObjs[k]];
                float yy = y + 0.035f + k * 0.036f;
                bool s = k == tiendaSel;
                if (s) Function.Call("DRAW_RECT", x, yy + 0.013f, 0.38f, 0.032f, 255, 255, 255, 40);
                Texto(o.Nombre + "  $" + o.Precio + "   (" + o.Desc + ")   tenes " + o.Tengo + "/" + o.Max, x, yy, 0.26f, s ? 255 : 190, s ? 255 : 190, s ? 255 : 190, true);
            }
            Texto("Flechas: elegir   Enter: comprar   B: salir", x, y + 0.04f + cnt * 0.036f, 0.22f, 190, 190, 190, true);
        }

        void DibujarRueda()
        {
            float cx = 0.5f, cy = 0.5f, rx = 0.17f, ry = 0.25f;
            int n = objs.Length;
            Function.Call("DRAW_RECT", cx, cy, 0.5f, 0.66f, 0, 0, 0, 110);
            for (int i = 0; i < n; i++)
            {
                double ang = i * 2 * Math.PI / n;
                float px = cx + (float)Math.Sin(ang) * rx, py = cy - (float)Math.Cos(ang) * ry;
                Obj o = objs[i];
                bool s = i == ruedaSel, hay = o.Tengo > 0;
                Function.Call("DRAW_RECT", px, py, 0.1f, 0.075f, s ? 230 : 35, s ? 200 : 35, s ? 60 : 35, s ? 230 : 190);
                int c = s ? 0 : (hay ? 255 : 110);
                Texto(o.Nombre, px, py - 0.026f, 0.27f, c, c, c, true);
                Texto("x" + o.Tengo, px, py + 0.002f, 0.27f, c, c, c, true);
            }
            Texto(ruedaSel >= 0 ? objs[ruedaSel].Nombre + ": " + objs[ruedaSel].Desc : "Move el mouse y solta U", cx, cy - 0.012f, 0.27f, 230, 230, 230, true);
        }

        void DibujarAviso()
        {
            Function.Call("DRAW_RECT", 0.5f, 0.165f, 0.5f, 0.045f, 0, 0, 0, 160);
            Texto(aviso, 0.5f, 0.148f, 0.30f, 255, 255, 255, true);
        }
    }
}
