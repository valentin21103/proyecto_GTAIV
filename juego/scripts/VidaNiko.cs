// VidaNiko.cs - salud, necesidades, comida y sentarse (parte de "Vida de Niko")
//
// SALUD
//   Con poca vida Niko empieza a desangrarse hasta que se pone una gasa; con poca vida
//   camina herido y no puede correr.
//
// OBJETOS (se llevan encima, se usan desde la rueda)
//   Gasa (frena la sangre, cura todo) - Analgesico (cura la mitad) - Jeringa (cura de a poco)
//   Hamburguesa, Pancho, Chocolate (hambre) - Agua, Gaseosa, Cerveza (sed) - Cigarrillos
//   Niko tiene el objeto del juego en la mano (posiciones en scripts\Props.ini).
//   Cigarrillos: lo saca, lo prende y fuma (animaciones del Smoke Mod); U para apagarlo. Calma el hambre y cura un poco.
//   U rapido = usa la curacion que mas sirve. Mantener U = rueda de objetos:
//   mouse (o flechas) hacia el objeto y solta U. Sentado, comes y tomas sentado.
//
// TIENDAS (E para comprar; en farmacias, restaurantes y almacenes se le habla al empleado)
//   Farmacias: curaciones + agua.   Estaciones de servicio: de todo.
//   Restaurantes y bares: comida y bebida.   Almacenes (24/7, deli): bebidas, snacks y cigarrillos.
//   Ctrl+F8 abre un menu para registrar el lugar donde estes (scripts\Lugares.ini).
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

        const int VK_E = 0x45, VK_U = 0x55, VK_N = 0x4E, VK_Z = 0x5A, VK_W = 0x57, VK_SHIFT = 0x10, VK_CTRL = 0x11;
        const int VK_F9 = 0x78, VK_F8 = 0x77, VK_F6 = 0x75;
        const int VK_UP = 0x26, VK_DOWN = 0x28, VK_LEFT = 0x25, VK_RIGHT = 0x27, VK_ENTER = 0x0D, VK_BACK = 0x08;
        const string ArchivoIni = "scripts\\VidaNiko.ini";
        const string ArchivoEstado = "scripts\\VidaNikoEstado.txt";
        const string ArchivoNafta = "scripts\\Nafta.ini";
        const string ArchivoLugares = "scripts\\Lugares.ini";
        const string ArchivoFarmaciasViejo = "scripts\\Farmacias.ini";

        // ================= objetos =================
        // tipo: 0 curacion, 1 comida, 2 bebida, 3 cigarrillos
        class Obj
        {
            public string Nombre, Desc, Set, Clip, SetSentado, ClipSentado;
            public int Tipo, Precio, Max, Ms, Tengo, Pack = 1;
            public string Prop;
            public float Hambre, Sed, Vejiga;
            public Obj(string n, string d, int tipo, int precio, int max, int ms, float hambre, float sed, float vejiga, string set, string clip, string setS, string clipS)
            { Nombre = n; Desc = d; Tipo = tipo; Precio = precio; Max = max; Ms = ms; Hambre = hambre; Sed = sed; Vejiga = vejiga; Set = set; Clip = clip; SetSentado = setS; ClipSentado = clipS; }
        }
        const int GASA = 0, ANALGESICO = 1, JERINGA = 2;
        Obj[] objs = {
            new Obj("Gasa", "frena la sangre, cura todo", 0, 80, 3, 4500, 0, 0, 0, "gestures@male", "time", null, null),
            new Obj("Analgesico", "cura la mitad", 0, 40, 3, 1200, 0, 0, 0, "amb@beg_standing", "take_obj", null, null),
            new Obj("Jeringa", "cura de a poco", 0, 150, 2, 1200, 0, 0, 0, "amb@beg_standing", "take_obj", null, null),
            new Obj("Hamburguesa", "hambre +50", 1, 8, 3, 6000, 50, 0, 0, "amb@burger_idle", "eat_stand", "amb@cafe_eat_idles", "sit_eat"),
            new Obj("Pancho", "hambre +30", 1, 4, 3, 5000, 30, 0, 0, "amb@hotdog_idle", "eat_stand", "amb@cafe_eat_idles", "sit_eat"),
            new Obj("Chocolate", "hambre +15", 1, 3, 5, 4000, 15, 0, 0, "amb@eat_chocolate", "choc_stand", "amb@cafe_eat_idles", "sit_eat"),
            new Obj("Agua", "sed +50", 2, 2, 3, 4000, 0, 50, 12, "amb@drink_bottle", "bottle_stand", "amb@bnch_eat_idl", "sit_idle_a"),
            new Obj("Gaseosa", "sed +40", 2, 3, 3, 4000, 0, 40, 15, "amb@drink_can", "can_stand", "amb@bnch_eat_idl", "sit_idle_a"),
            new Obj("Cerveza", "sed +25", 2, 5, 3, 4000, 0, 25, 22, "amb@drink_bottle", "bottle_stand", "amb@bnch_eat_idl", "sit_idle_a"),
            new Obj("Cigarrillos", "atado de 20, calma el hambre", 3, 6, 40, 0, 0, 0, 0, "amb@smoking_spliff", "create_spliff", null, null),
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
        int accion, accionFin, accionDur, accionObj = -1;      // 1 usar objeto, 2 orinar, 3 de cuerpo, 4 gesto
        int jeringaHasta, proximaJeringa;
        bool[] prev = new bool[256];
        string aviso; int avisoHasta;

        // sentado: 0 no, 1 sentandose, 2 sentado, 3 parandose
        int sentado, sentadoFase;
        // tienda
        bool tienda; int tiendaSel; Lugar tiendaLugar; List<int> tiendaObjs = new List<int>();
        // menu para registrar lugares
        bool regMenu; int regSel;
        static readonly string[] TiposLugar = { "FARMACIA", "RESTAURANTE", "ALMACEN", "BANO" };
        static readonly string[] DescLugar = { "curaciones y agua", "comida y bebida", "bebidas, snacks y cigarrillos", "tu casa: ahi podes ir al bano" };
        // objeto en la mano y cigarrillo
        GTA.Object prop; int ptfx = -1; int fumarFase, proximaPitada;
        bool humo = true;
        // hablar con el empleado antes de comprar: 1 Niko saluda, 2 el empleado responde
        Ped empleado; int saludoFase, saludoHasta;
        // guantera de tu auto (MisAutos avisa en cual estas)
        const int MaxGuantera = 3;
        string miAuto = "";
        Dictionary<string, int[]> guanteras = new Dictionary<string, int[]>();
        bool guantera; int guanteraLado, guanteraSel;
        // rueda
        bool rueda; int ruedaSel = -1; int uDesde; float ruedaX, ruedaY;

        class Lugar { public string Tipo, Nombre; public Vector3 Pos; public float Radio; public Blip B; }
        List<Lugar> lugares = new List<Lugar>();
        Dictionary<string, AnimationSet> sets = new Dictionary<string, AnimationSet>();

        public VidaNiko()
        {
            this.Interval = 0;
            this.GUID = new Guid("3f6c2b8e-7a41-4d2e-9b51-0c4e8a1d7f22");
            BindScriptCommand("miauto", new ScriptCommandDelegate(CmdMiAuto));
            objs[GASA].Tengo = 1; objs[ANALGESICO].Tengo = 1;
            foreach (Obj o in objs)
            {
                if (o.Nombre == "Hamburguesa") o.Prop = "AMB_BURG";
                else if (o.Nombre == "Pancho") o.Prop = "AMB_HOTDOG";
                else if (o.Nombre == "Chocolate") o.Prop = "AMB_CHOCBAR";
                else if (o.Nombre == "Agua") o.Prop = "AMB_JUICE_BOT";
                else if (o.Nombre == "Gaseosa") o.Prop = "AMB_CAN_DRINK_1";
                else if (o.Nombre == "Cerveza") o.Prop = "AMB_WINE";
                else if (o.Nombre == "Cigarrillos") { o.Prop = "BM_CHAR_FAG_F"; o.Pack = 20; }
            }
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
                        "; humo del cigarrillo (0 si el juego se cierra al fumar)",
                        "Humo = 1",
                        "; tambalear al recibir 2 disparos seguidos y caer con 3 (1 si, 0 no)",
                        "Tambaleo = 1",
                        "TambaleoEnMisiones = 1",
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
                    else if (k == "humo") humo = v != 0;
                    else if (k == "tambaleo") tambaleo = v != 0;
                    else if (k == "tambaleoenmisiones") tambaleoMisiones = v != 0;
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
                    string k = l.Substring(0, i);
                    if (k.StartsWith("Guantera|")) { LeerGuantera(k.Substring(9), l.Substring(i + 1)); continue; }
                    float v = F(l.Substring(i + 1), -1f);
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
                foreach (KeyValuePair<string, int[]> g in guanteras)
                {
                    List<string> c = new List<string>();
                    for (int i = 0; i < objs.Length; i++) if (g.Value[i] > 0) c.Add(objs[i].Nombre + ":" + g.Value[i]);
                    if (c.Count > 0) l.Add("Guantera|" + g.Key + "=" + string.Join(",", c.ToArray()));
                }
                File.WriteAllLines(ArchivoEstado, l.ToArray());
            }
            catch { }
        }

        void LeerGuantera(string auto, string datos)
        {
            int[] g = Guantera(auto);
            foreach (string par in datos.Split(','))
            {
                string[] kv = par.Split(':');
                if (kv.Length != 2) continue;
                for (int i = 0; i < objs.Length; i++) if (objs[i].Nombre == kv[0].Trim()) g[i] = (int)F(kv[1], 0);
            }
        }

        int[] Guantera(string auto)
        {
            int[] g;
            if (!guanteras.TryGetValue(auto, out g)) { g = new int[objs.Length]; guanteras[auto] = g; }
            return g;
        }

        void CmdMiAuto(Script sender, ObjectCollection p)
        {
            try { miAuto = p.Count > 0 && p[0] != null ? p[0].ToString() : ""; } catch { miAuto = ""; }
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
                    l.Add("; ALMACEN = nombre | x, y, z | radio       (24/7, deli, licoreria: bebidas, snacks, cigarrillos)");
                    l.Add("; En el juego: Ctrl+F8 abre un menu para registrar el lugar donde estes parado.");
                    l.Add("FARMACIA = Pill-Pharm East Island City | 1299.0, 690.0, 36.0 | 45");
                    // pasar las farmacias registradas con la version anterior (Farmacias.ini)
                    if (File.Exists(ArchivoFarmaciasViejo))
                        foreach (string raw in File.ReadAllLines(ArchivoFarmaciasViejo))
                            if (raw.Trim().StartsWith("FARMACIA") && raw.IndexOf("East Island City") < 0) l.Add(raw.Trim());
                    File.WriteAllLines(ArchivoLugares, l.ToArray());
                }
                LeerLugares(ArchivoLugares, "FARMACIA", true);
                LeerLugares(ArchivoLugares, "RESTAURANTE", true);
                LeerLugares(ArchivoLugares, "ALMACEN", true);
                LeerLugares(ArchivoLugares, "BANO", false);
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
            if (g.Tipo == "BANO") return;   // tu casa no necesita icono
            // iconos del juego, chicos y solo cerca (como las tiendas normales, no como misiones)
            try
            {
                g.B = Blip.AddBlip(g.Pos);
                g.B.Icon = g.Tipo == "FARMACIA" ? BlipIcon.Pickup_Health : g.Tipo == "ALMACEN" ? BlipIcon.Misc_Deal : BlipIcon.Activity_Restaurant;
                g.B.Color = BlipColor.White;
                g.B.Name = NombreTipo(g.Tipo);
                g.B.ShowOnlyWhenNear = true;
                g.B.Scale = 0.8f;
            }
            catch { }
        }

        static string NombreTipo(string tipo)
        {
            return tipo == "FARMACIA" ? "Farmacia" : tipo == "ALMACEN" ? "Almacen" : tipo == "BANO" ? "Bano" : "Restaurante";
        }

        void Registrar(Ped me, string tipo)
        {
            try
            {
                Vector3 p = me.Position;
                Lugar g = new Lugar();
                g.Tipo = tipo;
                g.Nombre = NombreTipo(tipo) + " " + (lugares.Count + 1);
                g.Pos = p; g.Radio = 15f;
                PonerBlip(g);
                lugares.Add(g);
                File.AppendAllText(ArchivoLugares, tipo + " = " + g.Nombre + " | " +
                    p.X.ToString("0.00", CultureInfo.InvariantCulture) + ", " +
                    p.Y.ToString("0.00", CultureInfo.InvariantCulture) + ", " +
                    p.Z.ToString("0.00", CultureInfo.InvariantCulture) + " | 15\r\n");
                Aviso(tipo == "BANO" ? "Bano registrado: aca podes ir de cuerpo (mantener N)" : NombreTipo(tipo) + " registrado aca: habla con el empleado (E) para comprar");
            }
            catch { Aviso("No se pudo registrar el lugar"); }
        }

        Lugar TiendaCerca(Vector3 p)
        {
            foreach (Lugar g in lugares) if (g.Tipo != "BANO" && g.Pos.DistanceTo(p) <= g.Radio) return g;
            return null;
        }

        // inodoros y banos quimicos del mapa
        static readonly string[] ModelosBano = { "GB_toiletA", "CJ_MED_TOILET_2", "Lev1_Toilet", "Lev5_H3toilet", "toilet_MH2", "Toilet_trough",
            "GB_portaloo01", "CJ_PORTA_CABIN_1", "CJ_PORTA_CABIN_2", "CJ_PORTA_CABIN_3", "Portacabin", "Park_Toilet", "PkToilets_MH06" };

        bool EnBano(Ped me)
        {
            Vector3 p = me.Position;
            foreach (Lugar g in lugares) if (g.Pos.DistanceTo(p) <= (g.Tipo == "BANO" ? 12f : g.Radio)) return true;
            foreach (string m in ModelosBano)
            {
                try { if (Function.Call<bool>("DOES_OBJECT_OF_TYPE_EXIST_AT_COORDS", p.X, p.Y, p.Z, 4.0f, new Model(m).Hash)) return true; }
                catch { }
            }
            return false;
        }

        List<int> ObjsDeTienda(Lugar g)
        {
            List<int> r = new List<int>();
            for (int i = 0; i < objs.Length; i++)
            {
                Obj o = objs[i];
                if (g.Tipo == "ESTACION") { if (o.Tipo != 0) r.Add(i); }
                else if (g.Tipo == "FARMACIA" && (o.Tipo == 0 || o.Nombre == "Agua")) r.Add(i);
                else if (g.Tipo == "RESTAURANTE" && (o.Tipo == 1 || o.Tipo == 2)) r.Add(i);
                else if (g.Tipo == "ALMACEN" && (o.Tipo != 0 || i == ANALGESICO) && o.Nombre != "Hamburguesa") r.Add(i);
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
        // ---- diagnostico de la computadora del cibercafe (Ctrl+F6) ----
        // El script del juego (computerStreamed) solo muestra "usar computadora" si TODO esto da bien:
        // jugador jugando, pantalla sin fundido, control activo, libre para tareas ambientales, sin objeto en la mano,
        // y sin otro mensaje de ayuda en pantalla. Esto muestra cada condicion para ver cual falla.
        static readonly string[] ModelosPc = { "BM_monitor4", "BM_monitor6", "BM_laptop" };
        int diagHasta;
        string[] diag;

        bool B(string native, params Parameter[] a) { try { return Function.Call<bool>(native, a); } catch { return false; } }

        void Diagnostico(Ped me, bool arreglar)
        {
            Player pl = Game.LocalPlayer;
            if (arreglar)
            {
                try { Function.Call("SET_MINIGAME_IN_PROGRESS", false); } catch { }
                try { Function.Call("DISPLAY_NON_MINIGAME_HELP_MESSAGES", true); } catch { }
                try { Function.Call("CLEAR_HELP"); } catch { }
                try { Function.Call("SET_PLAYER_CONTROL", pl, true); } catch { }
                try { me.Task.ClearAll(); } catch { }
                try { Function.Call("DISABLE_PAUSE_MENU", false); } catch { }
                QuitarProp();
                ArreglarComputadoras("Ctrl+Shift+F6");
                Aviso("Destrabado: minijuego apagado, ayuda limpia, control activo. Proba la computadora y el ESC");
            }
            Vector3 p = me.Position;
            string pc = "ninguna a 15 m";
            foreach (string m in ModelosPc)
            {
                if (B("DOES_OBJECT_OF_TYPE_EXIST_AT_COORDS", p.X, p.Y, p.Z, 2.5f, new Model(m).Hash)) { pc = m + " a menos de 2,5 m"; break; }
                if (B("DOES_OBJECT_OF_TYPE_EXIST_AT_COORDS", p.X, p.Y, p.Z, 15f, new Model(m).Hash)) pc = m + " a menos de 15 m";
            }
            int cs = -1;
            try { cs = Function.Call<int>("GET_NUMBER_OF_INSTANCES_OF_STREAMED_SCRIPT", "computerStreamed"); } catch { }
            bool mano = false;
            try { GTA.Object o = Function.Call<GTA.Object>("GET_OBJECT_PED_IS_HOLDING", me); mano = o != null && Game.Exists(o); } catch { }
            diag = new string[] {
                "Computadora cerca: " + pc,
                "Script de la computadora: " + (cs > 0 ? "ACTIVO" : cs == 0 ? "no arranco" : "?"),
                "Interior: " + Si(B("IS_INTERIOR_SCENE")),
                "Jugando: " + Si(B("IS_PLAYER_PLAYING", pl)),
                "Pantalla sin fundido: " + Si(B("IS_SCREEN_FADED_IN")),
                "Control del jugador: " + Si(B("IS_PLAYER_CONTROL_ON", pl)),
                "Libre para tareas (clave): " + Si(B("IS_PLAYER_FREE_FOR_AMBIENT_TASK", pl)),
                "Objeto en la mano: " + (mano ? "SI (bloquea)" : "no"),
                "Otro mensaje de ayuda: " + (B("IS_HELP_MESSAGE_BEING_DISPLAYED") ? "SI (bloquea)" : "no"),
                "Minijuego en curso: " + (B("IS_MINIGAME_IN_PROGRESS") ? "SI (bloquea ESC)" : "no"),
                "Controles estandar: " + Si(B("USING_STANDARD_CONTROLS")),
                "Ctrl+Shift+F6: destrabar"
            };
            diagHasta = Game.GameTime + 20000;
        }

        static string Si(bool b) { return b ? "si" : "NO"; }

        // ---- Arreglo de las computadoras ----
        // La partida guardada trae registrado "este objeto usa este script" por NUMERO de modelo. Cuando los mods
        // agregan o sacan modelos (armas, peds, autos, props) esos numeros se corren y el monitor queda sin script:
        // no aparece "usar computadora". Aca se vuelve a registrar con el nombre real del modelo (igual que startup.sco).
        bool brainsArreglados, pausaArreglada;
        int inicioSesion, pcCercaDesde;

        void ArreglarComputadoras(string motivo)
        {
            if (brainsArreglados) return;
            brainsArreglados = true;
            try
            {
                Function.Call("ALLOCATE_SCRIPT_TO_OBJECT", "computerStreamed", unchecked((int)0x9AB9F5CC), 100, 3.0f, -1); // BM_monitor4
                Function.Call("ALLOCATE_SCRIPT_TO_OBJECT", "computerStreamed", unchecked((int)0xB94FB347), 100, 3.0f, -1); // BM_monitor6
                Function.Call("ALLOCATE_SCRIPT_TO_OBJECT", "computerStreamed", unchecked((int)0xCFFE2EF7), 100, 5.0f, -1); // BM_laptop
                try { File.AppendAllText("scripts\\DiagComputadora.txt", DateTime.Now.ToString("HH:mm:ss") + " >>> computadoras re-registradas (" + motivo + ")" + Environment.NewLine); } catch { }
                Aviso("Computadoras arregladas: sali del cibercafe, alejate una cuadra y volve a entrar");
            }
            catch { Aviso("No se pudieron registrar las computadoras"); }
        }

        // ---- Usar la computadora sin depender del registro de la partida ----
        // Hace exactamente lo que hace el script del juego (computerStreamed) cuando apretas la tecla:
        // control apagado, "minijuego en curso", fundido, y arranca computerMain pasandole EL MONITOR
        // (computerMain lee la sala y el tipo de computadora desde ese objeto). La mision ve todo igual.
        bool eTick;
        GTA.Object pcObj;
        int pcBusca, pcEstado, pcTid, pcDesde;
        IntPtr pcMem = IntPtr.Zero;

        int Inst(string sc) { try { return Function.Call<int>("GET_NUMBER_OF_INSTANCES_OF_STREAMED_SCRIPT", sc); } catch { return -1; } }

        static int HandleDe(object o)
        {
            for (Type t = o.GetType(); t != null; t = t.BaseType)
            {
                System.Reflection.PropertyInfo p = t.GetProperty("Handle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly);
                if (p != null) { try { return (int)p.GetValue(o, null); } catch { return 0; } }
            }
            return 0;
        }

        void LogPc(string l) { try { File.AppendAllText("scripts\\DiagComputadora.txt", DateTime.Now.ToString("HH:mm:ss") + " >>> " + l + Environment.NewLine); } catch { } }

        void TickComputadora()
        {
            int now = Game.GameTime;
            Ped me = null;
            try { me = Game.LocalPlayer.Character; } catch { }
            if (me == null || !Game.Exists(me)) return;
            if (pcEstado == 0)
            {
                if (now >= pcBusca)
                {
                    pcBusca = now + 300;
                    pcObj = null;
                    if (!me.isInVehicle() && me.isAliveAndWell && B("IS_INTERIOR_SCENE"))
                    {
                        float mejor = 1.7f;
                        foreach (string m in ModelosPc)
                        {
                            GTA.Object[] os = null;
                            try { os = World.GetAllObjects(new Model(m)); } catch { }
                            if (os == null) continue;
                            foreach (GTA.Object o in os)
                            {
                                try
                                {
                                    if (o == null || !Game.Exists(o)) continue;
                                    float d = o.Position.DistanceTo(me.Position);
                                    if (d < mejor) { mejor = d; pcObj = o; }
                                }
                                catch { }
                            }
                        }
                    }
                }
                if (pcObj == null) return;
                if (tienda || rueda || regMenu || guantera || accion != 0 || sentado != 0 || saludoFase != 0) return;
                if (Inst("computerStreamed") > 0 || Inst("computerMain") > 0) return;   // el juego ya la maneja
                Ayuda("Apreta E para usar la computadora.");
                if (eTick) EmpezarComputadora(me, now);
                return;
            }
            if (pcEstado == 1)
            {
                if (B("HAS_SCRIPT_LOADED", "computerMain") && !B("IS_SCREEN_FADING"))
                {
                    int h = 0;
                    try { if (pcObj != null && Game.Exists(pcObj)) h = HandleDe(pcObj); } catch { }
                    if (h == 0) { TerminarComputadora("el monitor desaparecio"); return; }
                    pcMem = Marshal.AllocHGlobal(16);
                    Marshal.WriteInt32(pcMem, h);
                    try { Function.Call("CLEAR_HELP"); } catch { }
                    try { pcTid = Function.Call<int>("START_NEW_SCRIPT_WITH_ARGS", "computerMain", pcMem.ToInt32(), 1, 1024); } catch { pcTid = 0; }
                    try { Function.Call("MARK_SCRIPT_AS_NO_LONGER_NEEDED", "computerMain"); } catch { }
                    LogPc("computerMain arrancado: monitor " + h + " hilo " + pcTid);
                    pcEstado = 2; pcDesde = now;
                }
                else if (now - pcDesde > 8000) TerminarComputadora("computerMain no cargo");
                return;
            }
            if (pcEstado == 2)
            {
                bool activo = pcTid != 0 && B("IS_THREAD_ACTIVE", pcTid);
                if (!activo && now - pcDesde > 1500) TerminarComputadora(pcTid == 0 ? "no se pudo arrancar" : "cerrada");
            }
        }

        void EmpezarComputadora(Ped me, int now)
        {
            if (pcObj == null || !Game.Exists(pcObj)) return;
            try { Function.Call("SET_PLAYER_CONTROL", PlayerId(), false); } catch { }
            try { Function.Call("SET_MINIGAME_IN_PROGRESS", true); } catch { }
            try { Function.Call("DISPLAY_NON_MINIGAME_HELP_MESSAGES", true); } catch { }
            try { Function.Call("DO_SCREEN_FADE_OUT", 500); } catch { }
            try { Function.Call("REQUEST_SCRIPT", "computerMain"); } catch { }
            pcEstado = 1; pcDesde = now;
            LogPc("usar computadora: " + pcObj.Model.ToString());
        }

        void TerminarComputadora(string motivo)
        {
            try { Function.Call("DISPLAY_NON_MINIGAME_HELP_MESSAGES", false); } catch { }
            try { Function.Call("SET_MINIGAME_IN_PROGRESS", false); } catch { }
            try { Function.Call("SET_PLAYER_CONTROL", PlayerId(), true); } catch { }
            try { if (B("IS_SCREEN_FADED_OUT") || B("IS_SCREEN_FADING_OUT")) Function.Call("DO_SCREEN_FADE_IN", 500); } catch { }
            try { Function.Call("DISABLE_PAUSE_MENU", false); } catch { }
            if (pcMem != IntPtr.Zero) { try { Marshal.FreeHGlobal(pcMem); } catch { } pcMem = IntPtr.Zero; }
            LogPc("computadora terminada: " + motivo);
            if (motivo != "cerrada") Aviso("No se pudo abrir la computadora (" + motivo + ")");
            pcEstado = 0; pcTid = 0; pcBusca = Game.GameTime + 2000;
        }

        // ---- computadora del patrullero (policetest) ----
        // El juego (main.sco, "R3 triggers") solo la ofrece en 6 modelos fijos: police, police2, noose,
        // polpatriot, pstockade y fbi. El mod de autos agrega patrulleros nuevos (police3, police4, policew)
        // que el juego reconoce como policia (la mision de Brucie pide usar la computadora) pero sin
        // computadora, asi que la E no hacia nada. Se abre igual que el juego: START_NEW_SCRIPT("policetest",
        // 1024) con el auto parado, derecho y apoyado en las 4 ruedas. En los 6 modelos originales se deja
        // que la abra el juego y solo se abre desde aca si a los 2,5 s el juego no lo hizo.
        static readonly string[] ModelosR3 = { "POLICE", "POLICE2", "NOOSE", "POLPATRIOT", "PSTOCKADE", "FBI" };
        int patEstado, patDesde, patAuto;
        bool patOriginal;

        bool PatrulleroListo(Ped me, out Vehicle v)
        {
            v = null;
            try
            {
                if (!me.isInVehicle() || !me.isAliveAndWell) return false;
                v = me.CurrentVehicle;
                if (v == null || !Game.Exists(v)) return false;
                if (!B("IS_CHAR_IN_ANY_POLICE_VEHICLE", me) || !B("IS_CHAR_SITTING_IN_ANY_CAR", me)) return false;
                if (!B("IS_THIS_MODEL_A_CAR", v.Model.Hash)) return false;          // ni motos ni helicopteros
                if (!B("IS_VEH_DRIVEABLE", v) || !B("IS_VEHICLE_ON_ALL_WHEELS", v)) return false;
                if (v.Speed > 0.4f) return false;                                      // igual que el juego
                if (!B("IS_PLAYER_CONTROL_ON", PlayerId())) return false;
                if (B("IS_SCREEN_FADED_OUT") || B("IS_SCREEN_FADING_OUT") || B("IS_MINIGAME_IN_PROGRESS")) return false;
                return true;
            }
            catch { return false; }
        }

        bool EsModeloR3(Vehicle v)
        {
            try { int hv = v.Model.Hash; foreach (string m in ModelosR3) if (hv == new Model(m).Hash) return true; } catch { }
            return false;
        }

        void TickPatrullero()
        {
            int now = Game.GameTime;
            Ped me = null;
            try { me = Game.LocalPlayer.Character; } catch { }
            if (me == null || !Game.Exists(me)) return;
            Vehicle v;
            if (patEstado == 0)
            {
                if (tienda || rueda || regMenu || guantera || (accion != 0 && accion != 5) || pcEstado != 0) return;
                bool listo = PatrulleroListo(me, out v);
                if (!listo) { if (!me.isInVehicle()) patAuto = 0; return; }
                if (Inst("policetest") > 0) return;                                   // ya esta abierta
                bool original = EsModeloR3(v);
                // patrullero agregado por mods: el juego no avisa, se muestra su mismo mensaje una vez por auto
                int h = 0; try { h = HandleDe(v); } catch { }
                if (!original && h != patAuto && !B("IS_HELP_MESSAGE_BEING_DISPLAYED"))
                {
                    patAuto = h;
                    try { if (B("DOES_TEXT_LABEL_EXIST", "R3H_COP")) Function.Call("PRINT_HELP", "R3H_COP"); } catch { }
                }
                if (!eTick) return;
                if (accion == 5) TerminarFumar(me, false);                          // se guarda el cigarrillo
                patOriginal = original; patEstado = 1; patDesde = now;
                try { Function.Call("REQUEST_SCRIPT", "policetest"); } catch { }
                return;
            }
            if (patEstado == 1)
            {
                if (Inst("policetest") > 0) { patEstado = 0; return; }                 // la abrio el juego
                if (!PatrulleroListo(me, out v)) { patEstado = 0; return; }
                if (!B("HAS_SCRIPT_LOADED", "policetest"))
                {
                    try { Function.Call("REQUEST_SCRIPT", "policetest"); } catch { }
                    if (now - patDesde > 8000) { patEstado = 0; Aviso("No se pudo cargar la computadora del patrullero"); }
                    return;
                }
                if (patOriginal && now - patDesde < 2500) return;                     // darle tiempo al juego
                try { Function.Call("CLEAR_HELP"); } catch { }
                int tid = 0;
                try { tid = Function.Call<int>("START_NEW_SCRIPT", "policetest", 1024); } catch { tid = 0; }
                try { Function.Call("MARK_SCRIPT_AS_NO_LONGER_NEEDED", "policetest"); } catch { }
                if (tid == 0) Aviso("No se pudo abrir la computadora del patrullero");
                patEstado = 2; patDesde = now;
                return;
            }
            if (patEstado == 2 && now - patDesde > 2000 && Inst("policetest") <= 0) patEstado = 0;
        }

        void ArreglosInicio()
        {
            int now = Game.GameTime;
            if (inicioSesion == 0) { inicioSesion = now; return; }
            if (!pausaArreglada && now - inicioSesion > 10000)
            {
                pausaArreglada = true;
                // si quedo el menu de pausa bloqueado (lo bloquea la computadora mientras se usa), se libera
                int cm = 0; try { cm = Function.Call<int>("GET_NUMBER_OF_INSTANCES_OF_STREAMED_SCRIPT", "computerMain"); } catch { }
                if (cm == 0) { try { Function.Call("DISABLE_PAUSE_MENU", false); } catch { } }
            }
        }

        // Registro automatico (scripts\DiagComputadora.txt) cuando estas en un interior con una computadora cerca
        // o durante la mision del cibercafe (roman9): cada segundo anota cada condicion que pide el juego.
        const bool RegistroActivo = false;
        int diagProx, diagLineas;
        const string NombresScripts = "advanced_car_actions,ambairpotarea,ambatmq,ambbar,ambbarrier,ambbeggar,ambblkhawk,ambbouncer,ambbridgepatrol,ambbusker,ambcabaret,ambcargoholddoors,ambchurchdoors,ambclublights,ambcomedyclub,ambcontrolloader,ambcontrolmain,ambdealer,ambdebug,ambfloater,ambgerry3doorlock,ambhelicopter,ambhomelandcordon,ambhomelandfed,ambhomelandsirens,ambhotel,ambinternetcafe,ambjerseydocksgates,ambjimslocks,ambliftdoors,ambnightclubext,ambpimpnpros,ambpoledancer,ambpolrdblk,ambpreacher,ambsavebed,ambshowroom,ambstripclub,ambtaxdpt,ambtaxihail,ambtoiletdoors,ambtunnelcops,ambtv,ambunarea,ambwardrobe,ambwindowlift,arrestescapehelp,atmobj,badman_1,bell2,bell3m,bell3p,bell5,bell6,bell7,bernie1,bernie2,bernie3,berniecar,binco_brook_s,boating,bowl_trigger,bowling_lane,brian_1,brian_2,brian_3,brucie1,brucie2,brucie3_meetguy,brucie3a,brucie3b,brucie4,brucie5m,brucie5p,brucie_heli,brucieboat,bruciecopter,bruciedrink,brucieeat,brucielift,brucieshow,bruciestrip,brucietenpin,burgervendor,cablecars,car_theft_texts,cardivehelp,carfoodrunjack,carwash,cherise,cia1,cia2,cia3,cia4,ciawanted,clarence,cleanupray5trains,computermain,computernews,computerstreamed,copbootsearch,darts,darts_launcher,darts_trigger,date_activity,date_food,dating_alex,dating_carmen,dating_kate,dating_kiki,dating_michelle,deatharresthelp,derrick1,derrick2,derrick3,dwayne1,dwayne3,dwayne_backup,dwaynecherise,dwaynedrink,dwayneeat,dwayneemail,dwayneshow,dwaynestrip,dwaynetenpin,eddie1,eddie2,elizabeta1,elizabeta2,elizabeta3,elizabeta4,emergencycall,empiredown,empiretelescope,endcreditroll,endgamenews,faustin1,faustin2,faustin3,faustin4,faustin5,faustin5_fire,faustin6,faustin7,finale1a,finale1b,finale1c,finale1d,finale2,finaleaorb,finaleintrocut,finalewedtxts,foodserver,francis1,francis2_appointment,francis2a,francis2b,francis3,francis4,francis5,francis6,gambetti1,gambetti2,gambetti3,garbage_trucks,gerry1,gerry1_base,gerry2,gerry3_phonegirl,gerry3a,gerry3b,gerry3c,gerry4,gerry5,go_on_date,gracie,gunlockup,gunlockupct,handbrakehelp,happytshirt,helicopter,hospitalpickup,hossan_1,hotwirehelp,ilyena,ilyena_calls,independencefm,initial,internet_dating,ivan_1,jacob1,jacob2,jacob3m,jacob3p,jacob_gun_car,jacobdarts,jacobdrink,jacobeat,jacoblift,jacobpool,jacobshow,jacobstrip,jeff_1,jeff_1_mission,jeff_2,jeff_3,jimmy1,jimmy2,jimmy3,jimmy4,jimmy4a,jimmysuit,laterweapons,magvendor,main,manhat_heli_tours,manny1,manny2,manny3,marnie1,marnie2,mel,michelle1,michelle1_queue,michelle_introcalls,modo_manhat_5,mostwanted,mptutoriallauncher,multiplayertutorial,multitutorial,network_session,nutvendor,organiserhelp,packie1,packie2,packie3,packie_bomb,packiedarts,packiedrink,packiepool,packieshow,packiestrip,packiesuit,packietenpin,pathos1,pathos2,pdbhangoutmgr,pdbhomemgr,pdbmostwanted,perseus_manhat_8,phonepadhelp,phoneprofilehelp,physicstestbed,placeholder,playboy2,playboy3,playboy4,policebackup,policetest,pool_game,pool_table,puzzle,puzzle_launcher,pxdfcut,ray1,ray2,ray3,ray4,ray5,ray6,replayhelp,replaytutorial,roman1,roman10m,roman10p,roman11,roman12,roman13,roman14,roman2,roman3,roman3txtmsgrepeat,roman4,roman5,roman6,roman7,roman8m,roman8p,roman9,roman_bleedoutintro,roman_fakeblip,roman_taxi,romanblips,romandarts,romandrink,romaneat,romanmarker,romanpool,romanshow,romanstrip,romantenpin,sara2,sara_1,sixaxistutlauncher,sixaxistutorial,spcellphone,spcellphonecalling,spcellphonedebug,spcellphoneendcall,spcellphonemain,spcellphonenetwork,spcellphonetutorial,speechcontrol,sprunk,startup,stat_bridge,stattracker,storytimer,stunt,taxi,taxi_trigger,telescope,tenpinbowl,text_link_mission,till,tollbooth,trainhelper,txtmsghelp,vendor,vigilante,vlad1,vlad2,vlad3,vlad4,wantedhelp,waypointhelp,webalexblog,webautoeroticar,webcraplist,webdating,webdatingboys,webdatingfullprofile,webdatinggirls,webeddieblog,webemail,weberror,webeyefind,webeyefindsearch,weblawyerscareer,weblawyerscv,weblcpd,weblcpdprofile,weblibertytree,weblibertytreenews,webourownreality,webpublicliberty,webrealestate,webringtones,webringtonesmono,webringtonespoly,webringtonespurchase,webringtonestheme,webweazel,webweazelnews,webwhatthey,window_lift_launcher";
        void RegistrarComputadora()
        {
            int now = Game.GameTime;
            if (!RegistroActivo) return;   // ya no hace falta (la computadora anda); se deja para diagnosticos
            if (now < diagProx || diagLineas > 3000) return;
            diagProx = now + 1000;
            try
            {
                Ped me = Game.LocalPlayer.Character;
                if (me == null || !Game.Exists(me)) return;
                int r9 = -1; try { r9 = Function.Call<int>("GET_NUMBER_OF_INSTANCES_OF_STREAMED_SCRIPT", "roman9"); } catch { }
                bool interior = B("IS_INTERIOR_SCENE");
                if (!interior && r9 <= 0) return;
                Vector3 p = me.Position;
                string pc = "-"; float mejor = 99f;
                foreach (string m in ModelosPc)
                {
                    GTA.Object[] os = null;
                    try { os = World.GetAllObjects(new Model(m)); } catch { }
                    if (os == null) continue;
                    foreach (GTA.Object o in os)
                    {
                        try
                        {
                            if (o == null || !Game.Exists(o)) continue;
                            float d = o.Position.DistanceTo(p);
                            if (d >= mejor) continue;
                            mejor = d;
                            pc = m + " " + d.ToString("0.0", CultureInfo.InvariantCulture) + "m";
                        }
                        catch { }
                    }
                }
                int csAhora = -1; try { csAhora = Function.Call<int>("GET_NUMBER_OF_INSTANCES_OF_STREAMED_SCRIPT", "computerStreamed"); } catch { }
                if (!brainsArreglados && mejor <= 2.0f && csAhora == 0 && !me.isInVehicle())
                {
                    if (pcCercaDesde == 0) pcCercaDesde = now;
                    else if (now - pcCercaDesde > 4000) ArreglarComputadoras("auto: monitor a " + mejor.ToString("0.0", CultureInfo.InvariantCulture) + " m sin script");
                }
                else if (mejor > 2.5f) pcCercaDesde = 0;
                if (r9 <= 0 && mejor > 12f) return;
                int objs = 0; try { objs = World.GetAllObjects().Length; } catch { }
                Player pl = Game.LocalPlayer;
                bool mano = false;
                try { GTA.Object o = Function.Call<GTA.Object>("GET_OBJECT_PED_IS_HOLDING", me); mano = o != null && Game.Exists(o); } catch { }
                int cs = -1, caf = -1, cm = -1;
                try { cs = Function.Call<int>("GET_NUMBER_OF_INSTANCES_OF_STREAMED_SCRIPT", "computerStreamed"); } catch { }
                try { caf = Function.Call<int>("GET_NUMBER_OF_INSTANCES_OF_STREAMED_SCRIPT", "ambInternetCafe"); } catch { }
                try { cm = Function.Call<int>("GET_NUMBER_OF_INSTANCES_OF_STREAMED_SCRIPT", "computerMain"); } catch { }
                string l = DateTime.Now.ToString("HH:mm:ss") + " pos=" + p.X.ToString("0.0", CultureInfo.InvariantCulture) + "," + p.Y.ToString("0.0", CultureInfo.InvariantCulture) + "," + p.Z.ToString("0.0", CultureInfo.InvariantCulture)
                    + " interior=" + Si(interior) + " mision=" + r9 + " pc=[" + pc + "] objetos=" + objs
                    + " scripts(pc=" + cs + " cafe=" + caf + " main=" + cm + ")"
                    + " jugando=" + Si(B("IS_PLAYER_PLAYING", pl)) + " fundido=" + Si(B("IS_SCREEN_FADED_IN")) + " control=" + Si(B("IS_PLAYER_CONTROL_ON", pl))
                    + " libre=" + Si(B("IS_PLAYER_FREE_FOR_AMBIENT_TASK", pl)) + " mano=" + Si(mano) + " ayuda=" + Si(B("IS_HELP_MESSAGE_BEING_DISPLAYED"))
                    + " minijuego=" + Si(B("IS_MINIGAME_IN_PROGRESS")) + " estandar=" + Si(B("USING_STANDARD_CONTROLS"))
                    + " teclaE=" + Si((GetAsyncKeyState(0x45) & 0x8000) != 0) + " accion=" + Si(B("IS_CONTROL_PRESSED", 2, 23))
                    + " auto=" + Si(me.isInVehicle()) + " pcEstado=" + pcEstado;
                // cada 5 segundos: que scripts del juego estan corriendo (para ver si se lleno el cupo de hilos)
                if (false)
                {
                    int total = 0;
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    foreach (string sn in NombresScripts.Split(','))
                    {
                        int k = 0;
                        try { k = Function.Call<int>("GET_NUMBER_OF_INSTANCES_OF_STREAMED_SCRIPT", sn); } catch { }
                        if (k > 0) { total += k; sb.Append(sn); if (k > 1) sb.Append("x" + k); sb.Append(' '); }
                    }
                    l += Environment.NewLine + "   SCRIPTS CORRIENDO (" + total + "): " + sb.ToString();
                }
                File.AppendAllText("scripts\\DiagComputadora.txt", l + Environment.NewLine);
                diagLineas++;
            }
            catch { }
        }

        void DibujarDiag()
        {
            if (diag == null || Game.GameTime > diagHasta) return;
            float x = 0.62f, y = 0.16f, w = 0.35f;
            IV.Titulo("Diagnostico computadora", x, y, w);
            y += 0.045f;
            foreach (string l in diag) { IV.Item(l, null, x, y, w, false, false, 0f); y += IV.Fila; }
        }

        bool EnCinematica()
        {
            try
            {
                // no usar los natives de cutscene: llamarlos mientras una mision carga una escena puede romper la memoria
                if (accion != 3 && (Function.Call<bool>("IS_SCREEN_FADED_OUT") || Function.Call<bool>("IS_SCREEN_FADING_OUT"))) return true;
                bool nuestro = tienda || rueda || regMenu || guantera || sentado != 0 || saludoFase != 0;
                if (!nuestro && !Function.Call<bool>("IS_PLAYER_CONTROL_ON", PlayerId())) return true;
            }
            catch { }
            return false;
        }

        void Control(bool on) { try { Function.Call("SET_PLAYER_CONTROL", PlayerId(), on); } catch { } }
        void Lento(bool on) { try { Function.Call("SET_TIME_SCALE", on ? 0.25f : 1.0f); } catch { } }

        // animacion de cuerpo entero (curarse con gasa, ir de cuerpo, tambalear)
        void Anim(Ped p, string set, string nombre, bool loop)
        {
            try
            {
                if (!AnimLista(set)) Function.Call("REQUEST_ANIMS", set);
                AnimationSet s;
                if (!sets.TryGetValue(set, out s)) { s = new AnimationSet(set); sets[set] = s; }
                p.Task.PlayAnimation(s, nombre, 8f, loop ? AnimationFlags.Unknown05 : AnimationFlags.None);
            }
            catch { }
        }

        // animacion solo de la cintura para arriba: Niko puede caminar mientras come, toma o fuma
        string arribaSet, arribaClip;
        void Arriba(Ped p, string set, string clip, bool loop, int ms)
        {
            try
            {
                if (!AnimLista(set)) Function.Call("REQUEST_ANIMS", set);
                Function.Call("TASK_PLAY_ANIM_SECONDARY_UPPER_BODY", p, clip, set, 4.0f, loop ? 1 : 0, 0, 0, 0, loop ? -2 : ms);
                arribaSet = set; arribaClip = clip;
            }
            catch { }
        }

        void CortarArriba(Ped p)
        {
            if (arribaSet == null) return;
            try { Function.Call("TASK_PLAY_ANIM_SECONDARY_UPPER_BODY", p, arribaClip, arribaSet, 8.0f, 0, 0, 0, 0, 1); } catch { }
            arribaSet = null; arribaClip = null;
        }

        static bool AnimLista(string set)
        {
            try { return Function.Call<bool>("HAVE_ANIMS_LOADED", set); } catch { return true; }
        }

        // se piden al juego con tiempo para que esten cargadas cuando se usan
        static readonly string[] SetsUsados = {
            "amb@nightclub_ext", "amb@smk_scn_idles", "amb@burger_idle", "amb@hotdog_idle", "amb@eat_chocolate",
            "amb@drink_bottle", "amb@drink_can", "amb@cafe_eat_idles", "amb@bnch_eat_idl", "amb@beg_standing",
            "cop_search_idles", "amb@hotdog_hold", "move_m@cower", "amb@yawn", "playidles_tired_1h",
            "dam_rec_player", "amb@burgercart", "amb@ffood_server",
            "missttkill", "amb@smoking_spliff", "veh@drivebyvan", "gestures@male" };
        int proximaCarga;
        void PrecargarAnims(int now)
        {
            if (now < proximaCarga) return;
            proximaCarga = now + 2000;
            foreach (string set in SetsUsados) { try { if (!AnimLista(set)) Function.Call("REQUEST_ANIMS", set); } catch { } }
        }

        bool Moviendose()
        {
            return Down(VK_W) || Down(0x41) || Down(0x53) || Down(0x44) || Down(0x20);
        }

        // ---- objetos en la mano (modelos del propio juego, posicion en scripts\Props.ini) ----
        // valores probados del Smoking Mod (hueso 1232 = mano derecha, 1205 = cabeza)
        const string ArchivoProps = "scripts\\Props.ini";
        static readonly string[] PropsDefecto = {
            "; v4 - Posicion de cada objeto en la mano derecha de Niko: MODELO = x, y, z | rotX, rotY, rotZ",
            "; Se relee cada vez que usas un objeto: podes ajustar con el juego abierto.",
            "BM_CHAR_FAG_F = 0.015, -0.005, -0.021 | 0, 0, 0",
            "AMB_BURG = -0.05, 0.0, 0.035 | 0, 0, 0",
            "AMB_HOTDOG = -0.05, 0.0, 0.035 | 0, 0, 0",
            "AMB_CHOCBAR = 0, 0, 0 | 0, 0, 0",
            "AMB_JUICE_BOT = 0, 0, 0 | 0, 0, 0",
            "AMB_CAN_DRINK_1 = 0, 0, 0 | 0, 0, 0",
            "AMB_WINE = 0, 0, 0 | 0, 0, 0",
            "; chorro al orinar (hueso de la pelvis): x, y, z | rotX, rotY, rotZ | tamano",
            "CHORRO = 0.0, 0.15, 0.0 | 0, 0, 0 | 1.0",
            "; bidon de nafta en la mano (MisAutos)",
            "CJ_OIL_TIN_1 = 0.06, 0.0, -0.18 | 0, 0, 0",
        };

        bool LeerProp(string modelo, out Vector3 pos, out Vector3 rot, out float escala)
        {
            pos = new Vector3(0f, 0f, 0f); rot = new Vector3(0f, 0f, 0f); escala = 1f;
            bool hay = false;
            try
            {
                if (!File.Exists(ArchivoProps) || File.ReadAllText(ArchivoProps).IndexOf("; v4") < 0) File.WriteAllLines(ArchivoProps, PropsDefecto);
                foreach (string raw in File.ReadAllLines(ArchivoProps))
                {
                    string l = raw.Trim();
                    if (l.StartsWith(";") || l.IndexOf('=') < 0) continue;
                    if (l.Substring(0, l.IndexOf('=')).Trim().ToUpperInvariant() != modelo) continue;
                    string[] p = l.Substring(l.IndexOf('=') + 1).Split('|');
                    string[] a = p[0].Split(',');
                    if (a.Length >= 3) pos = new Vector3(F(a[0], 0), F(a[1], 0), F(a[2], 0));
                    if (p.Length > 1) { string[] b = p[1].Split(','); if (b.Length >= 3) rot = new Vector3(F(b[0], 0), F(b[1], 0), F(b[2], 0)); }
                    if (p.Length > 2) escala = F(p[2], 1f);
                    hay = true;
                }
            }
            catch { }
            return hay;
        }

        // el modelo se pide al juego y se crea recien cuando esta cargado (sino no aparece)
        string propPendiente; int propLimite;
        void PonerProp(Ped me, string modelo)
        {
            QuitarProp();
            if (modelo == null) return;
            propPendiente = modelo; propLimite = Game.GameTime + 3000;
            TickProp(me);
        }

        void TickProp(Ped me)
        {
            if (propPendiente == null) return;
            try
            {
                Model m = new Model(propPendiente);
                if (!Function.Call<bool>("HAS_MODEL_LOADED", m.Hash))
                {
                    Function.Call("REQUEST_MODEL", m.Hash);
                    if (Game.GameTime > propLimite) propPendiente = null;
                    return;
                }
                Vector3 pos, rot; float esc;
                LeerProp(propPendiente, out pos, out rot, out esc);
                prop = World.CreateObject(m, me.Position + new Vector3(0f, 0f, -5f));
                if (prop != null && Game.Exists(prop))
                {
                    Function.Call("ATTACH_OBJECT_TO_PED", prop, me, 1232, pos.X, pos.Y, pos.Z, rot.X, rot.Y, rot.Z, 0);
                    if (accion == 5 && fumarFase >= 2) EncenderHumo();
                }
            }
            catch { prop = null; }
            propPendiente = null;
        }

        // suelta el objeto de la mano (la colilla cae al piso)
        void TirarProp()
        {
            propPendiente = null;
            if (ptfx >= 0) { try { Function.Call("STOP_PTFX", ptfx); } catch { } ptfx = -1; }
            if (prop == null) return;
            try { if (Game.Exists(prop)) { Function.Call("DETACH_OBJECT", prop, true); prop.NoLongerNeeded(); } } catch { }
            prop = null;
        }

        void QuitarProp()
        {
            propPendiente = null;
            if (ptfx >= 0) { try { Function.Call("STOP_PTFX", ptfx); } catch { } ptfx = -1; }
            if (prop == null) return;
            try { if (Game.Exists(prop)) { Function.Call("DETACH_OBJECT", prop, false); prop.Delete(); } } catch { }
            prop = null;
        }

        // chorro: el mismo efecto que usan los NPC que orinan en las misiones (ped_pissing)
        int chorro = -1;
        void PonerChorro(Ped me)
        {
            QuitarChorro();
            try
            {
                Vector3 pos, rot; float esc;
                LeerProp("CHORRO", out pos, out rot, out esc);
                chorro = Function.Call<int>("START_PTFX_ON_PED_BONE", "ped_pissing", me, pos.X, pos.Y, pos.Z, rot.X, rot.Y, rot.Z, 417, esc);
            }
            catch { chorro = -1; }
        }

        void QuitarChorro()
        {
            if (chorro >= 0) { try { Function.Call("STOP_PTFX", chorro); } catch { } chorro = -1; }
        }

        // la persona mas cerca adelante de Niko (el que atiende detras del mostrador)
        Ped BuscarEmpleado(Ped me)
        {
            Ped mejor = null; float mejorD = 99f;
            try
            {
                double h = me.Heading * Math.PI / 180.0;
                float fx = -(float)Math.Sin(h), fy = (float)Math.Cos(h);
                foreach (Ped p in World.GetPeds(me.Position, 4.5f))
                {
                    if (!Game.Exists(p) || p.Equals(me) || !p.isAliveAndWell || p.isInVehicle()) continue;
                    Vector3 d = p.Position - me.Position;
                    float dist = (float)Math.Sqrt(d.X * d.X + d.Y * d.Y);
                    if (dist < 0.3f || dist > 4.5f) continue;
                    if ((d.X * fx + d.Y * fy) / dist < 0.55f) continue;     // tiene que estar adelante (unos 55 grados)
                    if (dist < mejorD) { mejorD = dist; mejor = p; }
                }
            }
            catch { }
            return mejor;
        }

        void Hablar(Ped p, string frase)
        {
            try { Function.Call("SAY_AMBIENT_SPEECH", p, frase, 1, 1, 0); } catch { }
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

        // 2 golpes seguidos: tambalea. 3 o un golpe muy fuerte: se va al piso un momento y se levanta.
        bool tambaleo = true, tambaleoMisiones = true;
        int blindajeAnterior = -1, golpe1, golpe2, proximoTambaleo;
        void Golpe(Ped me, int now, int dano)
        {
            bool enAuto = false;
            try { enAuto = me.isInVehicle(); } catch { }
            if (enAuto || now < proximoTambaleo) { golpe2 = golpe1; golpe1 = now; return; }
            int seguidos = 1;
            if (now - golpe1 < 1500) seguidos++;
            if (now - golpe2 < 2000) seguidos++;
            golpe2 = golpe1; golpe1 = now;
            if (seguidos >= 3 || dano >= 40)
            {
                proximoTambaleo = now + 4000;
                if (accion != 0) TerminarAccion(me, true);
                if (sentado != 0) Pararse(me);
                try { me.ForceRagdoll(1400, false); } catch { }
            }
            else if (seguidos == 2)
            {
                proximoTambaleo = now + 1500;
                string[] r = { "chest", "head_front", "back_upper" };
                Anim(me, "dam_rec_player", r[now % 3], false);
            }
        }

        void Reiniciar()
        {
            bool habiaMenu = tienda || rueda || sentado != 0 || regMenu || guantera || saludoFase != 0;
            if (rueda) Lento(false);
            if (habiaMenu) Control(true);
            if (accion == 3) Fundido(false);
            QuitarProp(); QuitarChorro(); fumarFase = 0; regMenu = false;
            guantera = false; saludoFase = 0; empleado = null;
            rueda = false; tienda = false; sentado = 0; accion = 0; jeringaHasta = 0;
        }

        // ================= loop =================
        void OnTick(object sender, EventArgs ev)
        {
            IV.Tick();
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
            // ---- tambalear con los disparos ----
            int blindaje = 0;
            try { blindaje = me.Armor; } catch { }
            if (tambaleo && vidaAnterior >= 0 && blindajeAnterior >= 0)
            {
                int dano = (vidaAnterior - vida) + (blindajeAnterior - blindaje);
                if (dano >= 5 && !(mision && !tambaleoMisiones)) Golpe(me, now, dano);
            }
            blindajeAnterior = blindaje;
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

            PrecargarAnims(now);
            TickAcciones(me, now, vida);

            DibujarHud(pct, critico);
            if (tienda) DibujarTienda();
            if (regMenu) DibujarRegistro();
            if (guantera) DibujarGuantera();
            if (rueda) DibujarRueda();
            if (now < avisoHasta && aviso != null && !EnCinematica()) DibujarAviso();

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
            if (i < 0) { Aviso("No tenes nada para curarte (compra en una farmacia)"); return; }
            Obj o = objs[i];
            if (o.Tengo <= 0) { Aviso("No tenes " + o.Nombre.ToLowerInvariant()); return; }
            if (o.Tipo == 0 && vida >= vidaMax && !(i == GASA && sangrando)) { Aviso("No estas herido"); return; }
            if (o.Tipo == 3)
            {
                if (sentado != 0) { Aviso("Parate para fumar"); return; }
                o.Tengo--;
                // animaciones del Smoke Mod (Lilmcnessy): lo saca y lo prende una sola vez
                accion = 5; accionObj = i; fumarFase = 1; accionFin = now + 4100; accionDur = 4100;
                fumarPropEn = now + 1600; fumarTirarEn = 0; pitadas = 0; exhalarEn = 0;
                AnimF(me, "amb@smoking_spliff", "create_spliff", FlagsFumar);
                return;
            }
            o.Tengo--;
            accion = 1; accionObj = i; accionFin = now + o.Ms; accionDur = o.Ms;
            if (i == GASA)
            {
                // se mira y envuelve el antebrazo (no hay animacion de vendarse en el juego: la mas parecida)
                Arriba(me, o.Set, o.Clip, true, 0);
                return;
            }
            if (sentado == 2 && o.SetSentado != null) Arriba(me, o.SetSentado, o.ClipSentado, true, 0);
            else Arriba(me, o.Set, o.Clip, o.Tipo != 0, o.Ms);
            PonerProp(me, o.Prop);
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

        // ================= guantera =================
        // columna 0 = guantera (Enter tomar, Retroceso sacar), columna 1 = encima (Enter guardar)
        void TickGuantera(Ped me, bool uPress)
        {
            if (!guantera)
            {
                if (!uPress) return;
                float vel = 99f;
                try { vel = me.CurrentVehicle.Speed; } catch { }
                if (vel > 1f) { Aviso("Para el auto para abrir la guantera"); return; }
                guantera = true; guanteraLado = 0; guanteraSel = 0; Control(false);
                return;
            }
            List<int> lista = ListaGuantera(guanteraLado);
            if (Pressed(VK_LEFT) || Pressed(VK_RIGHT)) { guanteraLado = 1 - guanteraLado; guanteraSel = 0; lista = ListaGuantera(guanteraLado); }
            if (lista.Count > 0)
            {
                if (Pressed(VK_UP)) guanteraSel = (guanteraSel + lista.Count - 1) % lista.Count;
                if (Pressed(VK_DOWN)) guanteraSel = (guanteraSel + 1) % lista.Count;
                guanteraSel = Math.Min(guanteraSel, lista.Count - 1);
            }
            int[] g = Guantera(miAuto);
            bool enter = Pressed(VK_ENTER), back = Pressed(VK_BACK);
            if (lista.Count > 0 && (enter || back))
            {
                int i = lista[guanteraSel];
                Obj o = objs[i];
                if (guanteraLado == 0 && enter)
                {
                    g[i]--;
                    float m = Bonus();
                    sed = Math.Min(100f, sed + o.Sed * m); vejiga = Math.Min(100f, vejiga + o.Vejiga);
                    Aviso("Tomaste " + o.Nombre.ToLowerInvariant() + " de la guantera (sed " + (int)sed + "%)");
                }
                else if (guanteraLado == 0 && back)
                {
                    if (o.Tengo >= o.Max) Aviso("Ya llevas el maximo de " + o.Nombre.ToLowerInvariant());
                    else { g[i]--; o.Tengo++; Aviso("Sacaste " + o.Nombre.ToLowerInvariant() + " de la guantera"); }
                }
                else if (guanteraLado == 1 && enter)
                {
                    int total = 0; foreach (int c in g) total += c;
                    if (total >= MaxGuantera) Aviso("La guantera esta llena (" + MaxGuantera + ")");
                    else { o.Tengo--; g[i]++; Aviso("Guardaste " + o.Nombre.ToLowerInvariant() + " en la guantera"); }
                }
            }
            if (uPress) { guantera = false; Control(true); }
        }

        // bebidas en la guantera (lado 0) o encima (lado 1)
        List<int> ListaGuantera(int lado)
        {
            List<int> r = new List<int>();
            int[] g = Guantera(miAuto);
            for (int i = 0; i < objs.Length; i++)
                if (objs[i].Tipo == 2 && (lado == 0 ? g[i] > 0 : objs[i].Tengo > 0)) r.Add(i);
            return r;
        }

        // ================= fumar (animaciones del Smoke Mod de Lilmcnessy) =================
        // fase 1 lo saca y lo prende (una sola vez), 2 fuma: una pitada cada tanto (solo la parte
        // de arriba del cuerpo, se puede caminar y seguir en el auto), 3 lo apaga: a pie lo pisa,
        // en el auto lo tira por la ventana. U lo apaga antes.
        const int PitadasCigarrillo = 6, CadaPitada = 7000;
        const AnimationFlags FlagsFumar = (AnimationFlags)3328;   // los mismos flags del Smoke Mod
        int fumarPropEn, fumarTirarEn, pitadas, exhalarEn, fumarAyudaHasta;

        void AnimF(Ped p, string set, string clip, AnimationFlags flags)
        {
            try
            {
                if (!AnimLista(set)) Function.Call("REQUEST_ANIMS", set);
                AnimationSet s;
                if (!sets.TryGetValue(set, out s)) { s = new AnimationSet(set); sets[set] = s; }
                p.Task.PlayAnimation(s, clip, 8f, flags);
            }
            catch { }
        }

        void TickFumar(Ped me, int now, int vida, bool aPie, bool uPress)
        {
            TickProp(me);
            bool vivo = true, enAuto = false;
            try { vivo = me.isAliveAndWell; enAuto = me.isInVehicle(); } catch { }
            if (!vivo) { TerminarFumar(me, false); return; }
            // pantalla en negro (escena de mision, carga): se guarda el cigarrillo sin animar nada
            bool negro = false;
            try { negro = Function.Call<bool>("IS_SCREEN_FADED_OUT"); } catch { }
            if (negro) { TerminarFumar(me, false); return; }

            if (fumarFase == 1)
            {
                if (fumarPropEn > 0 && now >= fumarPropEn) { fumarPropEn = 0; PonerProp(me, objs[accionObj].Prop); }
                if (now >= accionFin)
                {
                    fumarFase = 2; pitadas = 0; proximaPitada = now + 2500;
                    accionDur = 2500 + PitadasCigarrillo * CadaPitada; accionFin = now + accionDur;
                    fumarAyudaHasta = now + 6000;
                    EncenderHumo();
                }
                else if (now > accionFin + 4000) TerminarFumar(me, false);   // por si algo se traba
                return;
            }

            if (fumarFase == 2)
            {
                if (exhalarEn > 0 && now >= exhalarEn)
                {
                    exhalarEn = 0;
                    if (humo) { try { Function.Call("TRIGGER_PTFX_ON_PED_BONE", "ped_smoke_exhale", me, 0f, 0f, 0f, 0f, 0f, 0f, 1205, 1.1f); } catch { } }
                    if (vida < vidaMax) SetVida(me, Math.Min(vidaMax, vida + 2));
                }
                if (uPress || (pitadas >= PitadasCigarrillo && now >= proximaPitada - 3000))
                {
                    fumarFase = 3; exhalarEn = 0;
                    if (enAuto) { AnimF(me, "veh@drivebyvan", "throw_grenade", AnimationFlags.None); fumarTirarEn = now + 1000; accionDur = 1400; }
                    else { AnimF(me, "amb@nightclub_ext", "smoke_stub_out", AnimationFlags.None); fumarTirarEn = now + 1700; accionDur = 1900; }
                    accionFin = now + accionDur;
                    return;
                }
                if (now >= proximaPitada && pitadas < PitadasCigarrillo)
                {
                    pitadas++; proximaPitada = now + CadaPitada; exhalarEn = now + 2200;
                    AnimF(me, "amb@smoking_spliff", enAuto ? "partial_smoke_car" : "partial_smoke", FlagsFumar);
                }
                return;
            }

            if (fumarFase == 3)
            {
                if (fumarTirarEn > 0 && now >= fumarTirarEn)
                {
                    fumarTirarEn = 0;
                    if (enAuto) QuitarProp(); else TirarProp();     // a pie la colilla queda en el piso
                }
                if (now >= accionFin) TerminarFumar(me, true);
                return;
            }
            TerminarFumar(me, false);
        }

        void EncenderHumo()
        {
            if (!humo || ptfx >= 0 || prop == null) return;
            try { if (Game.Exists(prop)) ptfx = Function.Call<int>("START_PTFX_ON_OBJ", "ambient_cig_smoke", prop, 0.125f, -0.02f, 0.01f, 0f, 0f, 0f, 1.1f); } catch { ptfx = -1; }
        }

        void TerminarFumar(Ped me, bool fumo)
        {
            QuitarProp();
            if (fumo) { hambre = Math.Min(100f, hambre + 10f); Aviso("Fumaste un cigarrillo. Te quedan " + objs[accionObj].Tengo); }
            fumarFase = 0; accion = 0; fumarPropEn = 0; fumarTirarEn = 0; exhalarEn = 0;
        }

        void TerminarAccion(Ped me, bool cuerpoEntero)
        {
            if (accion == 3) Fundido(false);
            QuitarChorro();
            QuitarProp();
            CortarArriba(me);
            if (cuerpoEntero) { try { me.Task.ClearAll(); } catch { } }
            accion = 0;
        }

        // ================= sentarse =================
        // Usa los asientos del propio juego (bancos, sillas, escalones), como el Sitting Mod de Aru.
        bool SentadoAhora(Ped me)
        {
            try { return Function.Call<bool>("IS_CHAR_SITTING_IDLE", me); } catch { return false; }
        }

        void Sentarse(Ped me, int now)
        {
            Vector3 p = me.Position;
            bool hay = false;
            try { hay = Function.Call<bool>("IS_SITTING_OBJECT_NEAR", p.X, p.Y, p.Z, 0); } catch { }
            if (!hay) { Aviso("No hay donde sentarse aca (acercate a un banco o una silla)"); return; }
            try
            {
                Function.Call("TASK_SIT_DOWN_ON_NEAREST_OBJECT", me, 2, 0, p.X, p.Y, p.Z, 0, 0f, -2, 0);
                sentado = 1; sentadoFase = now + 8000;
            }
            catch { }
        }

        void Pararse(Ped me)
        {
            try { Function.Call("CLEAR_CHAR_TASKS", me); } catch { }
            sentado = 0;
        }

        void TickSentado(Ped me, int now, bool z)
        {
            bool sentadoJuego = SentadoAhora(me);
            if (sentado == 1)
            {
                if (sentadoJuego) sentado = 2;
                else if (now > sentadoFase || z || Moviendose()) Pararse(me);   // no llego al asiento
            }
            else if (sentado == 2)
            {
                if (!sentadoJuego && accion == 0) sentado = 0;                  // se paro solo
                else if (z || Down(VK_W)) { if (accion == 1) { CortarArriba(me); QuitarProp(); accion = 0; } Pararse(me); }
            }
        }

        // ================= acciones =================
        void TickAcciones(Ped me, int now, int vida)
        {
            bool shift = Down(VK_SHIFT), ctrl = Down(VK_CTRL), mision = EnMision();
            bool aPie = !me.isInVehicle() && me.isAliveAndWell;
            bool e = Pressed(VK_E) && !shift, z = Pressed(VK_Z) && !shift;
            eTick = e;
            if (pcEstado != 0) return;   // en la computadora no se hace nada mas
            bool nDown = Down(VK_N) && !shift, nRel = !nDown && prev[VK_N];
            if (nDown && !prev[VK_N]) nDesde = now;
            prev[VK_N] = nDown;
            int nTipo = 0;                                     // 1 tocar, 2 mantener
            if (nDown && nDesde > 0 && now - nDesde > 450) { nTipo = 2; nDesde = 0; }
            else if (nRel && nDesde > 0) { nTipo = 1; nDesde = 0; }
            bool f9 = Pressed(VK_F9), f8 = Pressed(VK_F8), f6 = Pressed(VK_F6);
            bool uDown = Down(VK_U);
            bool uPress = uDown && !prev[VK_U], uRelease = !uDown && prev[VK_U];
            prev[VK_U] = uDown;

            if (!aPie && (sentado != 0 || tienda)) { Reiniciar(); return; }

            // ---- guantera (adentro de tu auto) ----
            bool enMiAuto = false;
            try { enMiAuto = miAuto.Length > 0 && me.isInVehicle(); } catch { }
            if (guantera && !enMiAuto) { guantera = false; Control(true); }
            if (enMiAuto && accion == 0) { TickGuantera(me, uPress); return; }

            // accion en curso (comer, curarse, orinar)
            if (sentado != 0) TickSentado(me, now, z && accion != 5);
            if (accion == 5) { TickFumar(me, now, vida, aPie, uPress); return; }
            if (accion != 0)
            {
                TickProp(me);
                bool cuerpoEntero = accion == 3;
                bool cortar = !aPie || now > accionFin + 4000 || ((cuerpoEntero || accion == 2) && Moviendose());
                if (cortar)
                {
                    // te moviste o algo se trabo: se corta sin efecto (el objeto se devuelve)
                    if (accion == 1 && accionObj >= 0 && now < accionFin) objs[accionObj].Tengo++;
                    TerminarAccion(me, cuerpoEntero);
                    return;
                }
                if (now >= accionFin)
                {
                    if (accion == 1) TerminarUso(me, vida);
                    else if (accion == 2) { vejiga = 0f; DarBonus(1.25f, 10); Aviso("Que alivio. Hiciste lugar: la comida te llena mas por 10 min"); }
                    else if (accion == 3) { intestino = 0f; vejiga = Math.Max(0f, vejiga - 40f); DarBonus(1.5f, 15); Aviso("Liberado. Hiciste lugar: la comida te llena mucho mas por 15 min"); }
                    TerminarAccion(me, cuerpoEntero);
                }
                return;
            }

            if (ctrl && f9 && aPie) { Registrar(me, "FARMACIA"); return; }
            if (ctrl && f6) { Diagnostico(me, shift); return; }
            if (ctrl && f8 && aPie && !regMenu) { regMenu = true; regSel = 0; Control(false); return; }
            if (regMenu)
            {
                if (Pressed(VK_UP)) regSel = (regSel + TiposLugar.Length - 1) % TiposLugar.Length;
                if (Pressed(VK_DOWN)) regSel = (regSel + 1) % TiposLugar.Length;
                if (Pressed(VK_ENTER)) { Registrar(me, TiposLugar[regSel]); regMenu = false; Control(true); }
                else if (Pressed(VK_BACK) || (ctrl && f8)) { regMenu = false; Control(true); }
                return;
            }

            // ---- saludo con el empleado ----
            if (saludoFase != 0)
            {
                bool sigue = false;
                try { sigue = empleado != null && Game.Exists(empleado) && empleado.isAliveAndWell && empleado.Position.DistanceTo(me.Position) < 6f; } catch { }
                if (!sigue) { saludoFase = 0; empleado = null; Control(true); Aviso("Se fue el empleado"); return; }
                if (now >= saludoHasta)
                {
                    if (saludoFase == 1)
                    {
                        saludoFase = 2; saludoHasta = now + 1300;
                        Hablar(empleado, "GENERIC_HI");
                    }
                    else
                    {
                        saludoFase = 0;
                        tienda = true; tiendaSel = 0;          // el control sigue bloqueado por la tienda
                    }
                }
                return;
            }

            // ---- tienda ----
            if (tienda)
            {
                int cnt = tiendaObjs.Count;
                if (Pressed(VK_UP)) tiendaSel = (tiendaSel + cnt - 1) % cnt;
                if (Pressed(VK_DOWN)) tiendaSel = (tiendaSel + 1) % cnt;
                if (Pressed(VK_ENTER))
                {
                    Obj o = objs[tiendaObjs[tiendaSel]];
                    if (o.Tengo + o.Pack > o.Max) Aviso("Ya tenes " + o.Tengo + " (maximo " + o.Max + ")");
                    else if (Player.Money < o.Precio) Aviso("No te alcanza ($" + o.Precio + ")");
                    else { Player.Money = Player.Money - o.Precio; o.Tengo += o.Pack; Aviso("Compraste: " + o.Nombre + " (" + o.Tengo + "/" + o.Max + ")"); }
                }
                if (e || Pressed(VK_BACK)) { tienda = false; Control(true); }
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
                    Control(true);
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
            if (sentado != 0) return;
            if (!aPie) return;
            if (z) { Sentarse(me, now); return; }

            // ---- tienda cercana ----
            // en todos los lugares (tambien estaciones) se compra hablando con el empleado (E).
            // En las estaciones, si no hay empleado, la E queda para el menu de nafta de MisAutos.
            Lugar t = TiendaCerca(me.Position);
            Ped emp = null;
            if (t != null) emp = BuscarEmpleado(me);
            if (t != null && e)
            {
                tiendaObjs = ObjsDeTienda(t);
                if (tiendaObjs.Count > 0)
                {
                    if (emp == null) { if (t.Tipo != "ESTACION") Aviso("No hay nadie atendiendo"); return; }
                    empleado = emp; tiendaLugar = t; saludoFase = 1; saludoHasta = now + 1300;
                    Control(false);
                    try { Function.Call("TASK_TURN_CHAR_TO_FACE_CHAR", me, emp); } catch { }
                    try { Function.Call("TASK_TURN_CHAR_TO_FACE_CHAR", emp, me); } catch { }
                    Hablar(me, "GENERIC_HI");
                    return;
                }
            }

            // ---- bano (bonus) ----
            if (nTipo == 1 && necesidades)
            {
                if (vejiga < 15f) Aviso("No tenes ganas de orinar");
                else
                {
                    accion = 2; accionFin = now + 7000; accionDur = 7000;
                    Anim(me, "missttkill", "piss_loop", true);
                    PonerChorro(me);
                }
            }
            else if (nTipo == 2 && necesidades)
            {
                if (intestino < 25f) Aviso("No tenes ganas de ir de cuerpo");
                else if (!EnBano(me)) Aviso("Necesitas un bano: un bar, restaurante, tienda, un bano quimico o tu casa (registrala con Ctrl+F8)");
                else
                {
                    accion = 3; accionFin = now + 9000; accionDur = 9000;
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
                try { Pointer pv = new Pointer(typeof(float)); Function.Call("GET_CHAR_SPEED", me, pv); vel = (float)pv.Value; } catch { }   // escribe en un puntero (2 datos), como lo usa Rockstar
                if (vel < 0.5f && !mision && Player.WantedLevel == 0 && !Down(0x02))
                {
                    // gesto de la cintura para arriba: no frena a Niko
                    if (sed < hambre) { Arriba(me, "amb@yawn", "yawn", false, 3000); Aviso("Niko tiene la boca seca..."); }
                    else { Arriba(me, "playidles_tired_1h", "heavy_breathing", false, 3000); Aviso("A Niko le ruge la panza..."); }
                }
            }

            if (t != null && !sangrando)
            {
                if (emp != null) Ayuda("Apreta E para hablar con el empleado.");
            }
        }

        // ================= dibujo (estilo GTA IV, ver clase IV) =================
        void Ayuda(string s) { IV.Ayuda(s, 1); }

        void DibujarHud(int pct, bool critico)
        {
            int now = Game.GameTime;
            // el estado de Niko solo se ve con la rueda (mantener U); nada en cinematicas
            DibujarDiag();
            ArreglosInicio();
            RegistrarComputadora();
            TickComputadora();
            TickPatrullero();
            if (pcEstado != 0) return;   // usando la computadora: nada nuestro en pantalla
            bool cine = EnCinematica();
            if (rueda && !cine)
            {
                float x = IV.BarraX, w = IV.BarraW, y = IV.BarraY(1);   // el lugar 0 es la nafta de MisAutos
                if (necesidades)
                {
                    IV.Barra("INTESTINO", intestino, x, y, w, IV.Naranja, intestino > 85f); y -= IV.BarraPaso;
                    IV.Barra("VEJIGA", vejiga, x, y, w, IV.Amarillo, vejiga > 80f); y -= IV.BarraPaso;
                    IV.Barra("SED", sed, x, y, w, IV.Celeste, sed < 25f); y -= IV.BarraPaso;
                    IV.Barra("HAMBRE", hambre, x, y, w, IV.AmarilloClaro, hambre < 25f); y -= IV.BarraPaso;
                }
                if (salud) { IV.Barra(sangrando ? "SANGRANDO" : "SALUD", pct, x, y, w, IV.VerdeClaro, critico || sangrando); y -= IV.BarraPaso; }
                if (now < bonusHasta) IV.Texto("COMIDA RINDE x" + bonusComida.ToString("0.00", CultureInfo.InvariantCulture), x, y - 0.006f, 0.21f, IV.VerdeClaro, 255, 0);
            }
            if (cine) return;
            if (sangrando && !rueda && !tienda && accion == 0) Ayuda("Te estas desangrando. Apreta U para ponerte una gasa.");
            if (accion == 1 && accionObj >= 0)
            {
                string t = accionObj == GASA ? "Poniendote una gasa" : accionObj == ANALGESICO ? "Tomando un analgesico" :
                           accionObj == JERINGA ? "Inyectandote" : (objs[accionObj].Tipo == 1 ? "Comiendo" : "Tomando");
                IV.Progreso(t, Avance(now));
            }
            if (accion == 2) IV.Progreso("Orinando", Avance(now));
            if (accion == 5 && fumarFase == 2 && now < fumarAyudaHasta) Ayuda("Podes caminar fumando. Apreta U para apagar el cigarrillo.");
            if (accion == 3) IV.Texto("Niko esta ocupado...", 0.5f, 0.47f, 0.42f, IV.BlancoPuro, 255, 1);
        }

        float Avance(int now) { return accionDur > 0 ? 1f - (accionFin - now) / (float)accionDur : 0f; }

        void DibujarTienda()
        {
            int cnt = tiendaObjs.Count;
            float x = 0.03f, y = 0.16f, w = 0.34f;
            IV.Titulo(tiendaLugar != null ? tiendaLugar.Nombre : "Tienda", x, y, w);
            y += 0.045f;
            for (int k = 0; k < cnt; k++)
            {
                Obj o = objs[tiendaObjs[k]];
                bool lleno = o.Tengo >= o.Max;
                IV.Item(o.Nombre + "   " + o.Tengo + "/" + o.Max, "$" + o.Precio, x, y, w, k == tiendaSel, lleno, 0.29f, IV.VerdeClaro);
                y += IV.Fila;
            }
            Obj sel = objs[tiendaObjs[tiendaSel]];
            IV.Pie(sel.Desc + "     Enter: comprar   E: salir", x, y, w);
        }

        void DibujarRegistro()
        {
            float x = 0.03f, y = 0.16f, w = 0.34f;
            IV.Titulo("Registrar este lugar", x, y, w);
            y += 0.045f;
            for (int k = 0; k < TiposLugar.Length; k++) { IV.Item(NombreTipo(TiposLugar[k]), DescLugar[k], x, y, w, k == regSel, false, 0.12f); y += IV.Fila; }
            IV.Pie("Enter: registrar   Retroceso: cancelar", x, y, w);
        }

        void DibujarGuantera()
        {
            int[] g = Guantera(miAuto);
            int total = 0; foreach (int c in g) total += c;
            float w = 0.24f, y0 = 0.16f;
            int filas = 1;
            for (int lado = 0; lado < 2; lado++)
            {
                List<int> l = ListaGuantera(lado);
                float x = 0.03f + lado * (w + 0.01f);
                IV.Titulo(lado == 0 ? "Guantera " + total + "/" + MaxGuantera : "Encima", x, y0, w);
                if (lado == guanteraLado) IV.Rect(x, y0 + 0.042f, w, 0.003f, IV.BlancoPuro, 255);
                float y = y0 + 0.045f;
                if (l.Count == 0) { IV.Item(lado == 0 ? "(vacia)" : "(sin bebidas)", null, x, y, w, false, true, 0f); y += IV.Fila; }
                for (int k = 0; k < l.Count; k++)
                {
                    int i = l[k];
                    IV.Item(objs[i].Nombre, "x" + (lado == 0 ? g[i] : objs[i].Tengo), x, y, w, lado == guanteraLado && k == guanteraSel, false, 0.19f);
                    y += IV.Fila;
                }
                filas = Math.Max(filas, Math.Max(1, l.Count));
            }
            IV.Pie(guanteraLado == 0 ? "Enter: tomar   Retroceso: sacar   Izq/Der: lado   U: cerrar" : "Enter: guardar   Izq/Der: lado   U: cerrar",
                   0.03f, y0 + 0.045f + filas * IV.Fila, w * 2 + 0.01f);
        }

        void DibujarRueda()
        {
            float cx = 0.5f, cy = 0.48f, rx = 0.2f, ry = 0.3f;
            int n = objs.Length;
            for (int i = 0; i < n; i++)
            {
                double ang = i * 2 * Math.PI / n;
                float px = cx + (float)Math.Sin(ang) * rx, py = cy - (float)Math.Cos(ang) * ry;
                Obj o = objs[i];
                bool s = i == ruedaSel, hay = o.Tengo > 0;
                IV.Rect(px - 0.047f, py - 0.032f, 0.094f, 0.064f, new int[] { 0, 0, 0 }, s ? 220 : 175);
                if (s) IV.Rect(px - 0.047f, py - 0.032f, 0.094f, 0.064f, IV.Celeste, 220);
                IV.Rect(px - 0.047f, py + 0.029f, 0.094f, 0.003f, hay ? IV.Celeste : IV.Atenuado, 255);
                int[] c = s ? IV.BlancoPuro : (hay ? IV.Blanco : IV.Atenuado);
                IV.Texto(o.Nombre.ToUpperInvariant(), px, py - 0.026f, 0.23f, c, 255, 1);
                IV.Texto("x" + o.Tengo, px, py + 0.0f, 0.27f, s ? IV.BlancoPuro : (hay ? IV.CelesteClaro : IV.Atenuado), 255, 1);
            }
            IV.Rect(cx - 0.085f, cy - 0.03f, 0.17f, 0.06f, new int[] { 0, 0, 0 }, 185);
            if (ruedaSel >= 0)
            {
                IV.Texto(objs[ruedaSel].Nombre.ToUpperInvariant(), cx, cy - 0.025f, 0.3f, IV.BlancoPuro, 255, 1);
                IV.Texto(objs[ruedaSel].Desc, cx, cy + 0.002f, 0.24f, IV.CelesteClaro, 255, 1);
            }
            else IV.Texto("Mueve el mouse y solta U", cx, cy - 0.012f, 0.25f, IV.Gris, 255, 1);
        }

        void DibujarAviso() { IV.Aviso(aviso, 0); }
    }
    // <estilo-iv> (copiado de tools/EstiloIV.cs con tools/estilo_sync.py; no editar aca)
    // Estilo visual comun de nuestros mods, con los colores de common/data/hudColor.dat de GTA IV.
    // - Ayudas: caja de ayuda REAL del juego (textos en update/common/text/spanishVN.gxt, ver tools/gxt_vn.py).
    // - Avisos: subtitulos REALES del juego (no pisan el texto de una mision).
    // - Nada se dibuja con el menu de pausa / mapa, el celular, la computadora, fundidos o el HUD apagado.
    // - Barras: una sola columna a la derecha, por encima de donde el juego pone el nombre de la calle y del auto.
    static class IV
    {
        // paleta del juego
        public static readonly int[] Blanco = { 225, 225, 225 }, BlancoPuro = { 255, 255, 255 }, Gris = { 140, 140, 140 },
            Atenuado = { 90, 90, 90 }, Celeste = { 74, 148, 160 }, CelesteClaro = { 151, 201, 206 }, CelesteOscuro = { 51, 105, 114 },
            Verde = { 87, 124, 88 }, Naranja = { 188, 130, 74 }, VerdeClaro = { 159, 201, 166 }, Rojo = { 153, 69, 69 }, RojoClaro = { 206, 155, 155 },
            Amarillo = { 206, 169, 13 }, AmarilloClaro = { 215, 197, 121 };

        public const float Fila = 0.034f;

        // ---- columna de barras (derecha, arriba del nombre de calle/auto). Lugar 0 = nafta (MisAutos), 1.. = VidaNiko ----
        public const float BarraX = 0.875f, BarraW = 0.085f, BarraY0 = 0.80f, BarraPaso = 0.021f;
        public static float BarraY(int lugar) { return BarraY0 - lugar * BarraPaso; }

        static bool N(string n, params GTA.Native.Parameter[] a) { try { return GTA.Native.Function.Call<bool>(n, a); } catch { return false; } }

        // ---- se puede dibujar? (una vez por cuadro) ----
        static int visT = -1; static bool vis = true; static float desp;
        public static bool Visible()
        {
            int t = GTA.Game.GameTime;
            if (t == visT) return vis;
            visT = t; vis = true; desp = 0f;
            try
            {
                if (N("IS_PAUSE_MENU_ACTIVE") || N("IS_SCREEN_FADED_OUT") || N("IS_SCREEN_FADING_OUT") || !N("IS_HUD_PREFERENCE_SWITCHED_ON")) { vis = false; return vis; }
                int cm = 0; try { cm = GTA.Native.Function.Call<int>("GET_NUMBER_OF_INSTANCES_OF_STREAMED_SCRIPT", "computerMain"); } catch { }
                if (cm > 0) { vis = false; return vis; }
                int pt = 0; try { pt = GTA.Native.Function.Call<int>("GET_NUMBER_OF_INSTANCES_OF_STREAMED_SCRIPT", "policetest"); } catch { }
                if (pt > 0) { vis = false; return vis; }                     // computadora del patrullero
                GTA.Ped p = GTA.Game.LocalPlayer.Character;
                if (p != null && GTA.Game.Exists(p))
                {
                    GTA.Native.Pointer tarea = new GTA.Native.Pointer(typeof(int));
                    if (N("GET_MOBILE_PHONE_TASK_SUB_TASK", p, tarea)) { vis = false; return vis; }
                }
                // si el juego muestra su caja de ayuda (y no es nuestra), los menus bajan para no taparla
                if (N("IS_HELP_MESSAGE_BEING_DISPLAYED") && (ayudaLab == null || !N("IS_THIS_HELP_MESSAGE_BEING_DISPLAYED", ayudaLab)))
                {
                    GTA.Native.Pointer w = new GTA.Native.Pointer(typeof(float)), h = new GTA.Native.Pointer(typeof(float));
                    try { GTA.Native.Function.Call("GET_HELP_MESSAGE_BOX_SIZE", w, h); desp = Math.Min(0.3f, Convert.ToSingle(h.Value) + 0.02f); } catch { desp = 0.12f; }
                }
            }
            catch { }
            return vis;
        }

        // llamar al principio de cada tick: saca nuestra ayuda si ya no se pide y muestra avisos pendientes
        public static void Tick()
        {
            if (ayudaLab != null && Environment.TickCount - ayudaPedida > 300)
            {
                if (N("IS_THIS_HELP_MESSAGE_BEING_DISPLAYED", ayudaLab)) { try { GTA.Native.Function.Call("CLEAR_HELP"); } catch { } }
                ayudaLab = null;
            }
        }

        // rectangulo con esquina arriba-izquierda en (x, y)
        public static void Rect(float x, float y, float w, float h, int[] c, int a)
        {
            if (!Visible()) return;
            GTA.Native.Function.Call("DRAW_RECT", x + w / 2f, y + h / 2f, w, h, c[0], c[1], c[2], a);
        }

        // alin: 0 izquierda, 1 centro, 2 derecha (x = borde derecho)
        public static void Texto(string s, float x, float y, float esc, int[] c, int a, int alin)
        {
            if (string.IsNullOrEmpty(s) || !Visible()) return;
            GTA.Native.Function.Call("SET_TEXT_FONT", 0);
            GTA.Native.Function.Call("SET_TEXT_SCALE", esc, esc);
            GTA.Native.Function.Call("SET_TEXT_COLOUR", c[0], c[1], c[2], a);
            GTA.Native.Function.Call("SET_TEXT_DROPSHADOW", 1, 0, 0, 0, 200);
            if (alin == 1) GTA.Native.Function.Call("SET_TEXT_CENTRE", 1);
            if (alin == 2) { GTA.Native.Function.Call("SET_TEXT_RIGHT_JUSTIFY", 1); GTA.Native.Function.Call("SET_TEXT_WRAP", 0.0f, x); x = 0f; }
            GTA.Native.Function.Call("DISPLAY_TEXT_WITH_LITERAL_STRING", x, y, "STRING", s);
        }

        // ---- menus (bajan solos si el juego tiene su ayuda en pantalla) ----
        // titulo de panel: texto blanco en mayusculas con una linea celeste abajo
        public static void Titulo(string s, float x, float y, float w)
        {
            if (!Visible()) return;
            y += desp;
            Rect(x, y, w, 0.042f, new int[] { 0, 0, 0 }, 200);
            Texto(s.ToUpperInvariant(), x + 0.008f, y + 0.007f, 0.40f, BlancoPuro, 255, 0);
            Rect(x, y + 0.042f, w, 0.003f, Celeste, 255);
        }

        // fila de menu; sel = resaltada en celeste
        public static void Item(string izq, string der, float x, float y, float w, bool sel, bool apagado, float colDer)
        {
            Item(izq, der, x, y, w, sel, apagado, colDer, CelesteClaro);
        }
        public static void Item(string izq, string der, float x, float y, float w, bool sel, bool apagado, float colDer, int[] cDer)
        {
            if (!Visible()) return;
            y += desp;
            Rect(x, y, w, Fila, new int[] { 0, 0, 0 }, 165);
            if (sel) Rect(x, y, w, Fila, Celeste, 210);
            int[] c = apagado ? Atenuado : (sel ? BlancoPuro : Blanco);
            Texto(izq, x + 0.008f, y + 0.006f, 0.29f, c, 255, 0);
            // la fuente del juego es ancha: la columna derecha arranca despues del texto izquierdo
            float col = Math.Max(colDer, 0.0105f * (izq == null ? 0 : izq.Length) + 0.02f);
            if (der != null) Texto(der, x + col, y + 0.006f, 0.29f, sel ? BlancoPuro : (apagado ? Atenuado : cDer), 255, 0);
        }

        // pie con las teclas, debajo del menu
        public static void Pie(string s, float x, float y, float w)
        {
            if (!Visible()) return;
            y += desp;
            Rect(x, y, w, 0.003f, Celeste, 255);
            Rect(x, y + 0.003f, w, 0.03f, new int[] { 0, 0, 0 }, 185);
            Texto(s, x + 0.008f, y + 0.009f, 0.235f, Gris, 255, 0);
        }

        // ---- ayuda: caja REAL del juego ----
        static string ayudaLab; static int ayudaPedida;
        static readonly System.Collections.Generic.Dictionary<string, bool> existe = new System.Collections.Generic.Dictionary<string, bool>();

        public static uint Joaat(string s)
        {
            uint h = 0;
            foreach (char ch in s.ToLowerInvariant()) { h += (byte)ch; h += h << 10; h ^= h >> 6; }
            h += h << 3; h ^= h >> 11; h += h << 15;
            return h;
        }

        // etiqueta del texto en spanishVN.gxt (la genera tools/gxt_vn.py con el mismo calculo)
        public static string Etiqueta(string s) { return "VN" + Joaat(s).ToString("X8"); }

        public static void Ayuda(string s, int fila)
        {
            if (string.IsNullOrEmpty(s) || !Visible()) return;
            string lab = Etiqueta(s);
            bool hay;
            if (!existe.TryGetValue(lab, out hay)) { hay = N("DOES_TEXT_LABEL_EXIST", lab); existe[lab] = hay; }
            if (hay)
            {
                ayudaPedida = Environment.TickCount;
                if (ayudaLab != lab || !N("IS_THIS_HELP_MESSAGE_BEING_DISPLAYED", lab))
                {
                    // no tapar la ayuda de una mision: solo si no hay otra en pantalla (o es nuestra)
                    if (ayudaLab == lab || !N("IS_HELP_MESSAGE_BEING_DISPLAYED"))
                    {
                        try { GTA.Native.Function.Call("PRINT_HELP_FOREVER", lab); ayudaLab = lab; } catch { }
                    }
                }
                return;
            }
            // texto sin etiqueta: caja dibujada, debajo de la ayuda del juego
            float w = Math.Max(0.16f, 0.0058f * s.Length + 0.02f), y = 0.055f + fila * 0.05f + desp;
            Rect(0.03f, y, w, 0.045f, new int[] { 0, 0, 0 }, 185);
            Texto(s, 0.04f, y + 0.012f, 0.28f, BlancoPuro, 255, 0);
        }

        // ---- aviso: subtitulo REAL del juego ----
        static string avisoTxt; static int avisoT;
        public static void Aviso(string s, int fila)
        {
            if (string.IsNullOrEmpty(s) || !Visible()) return;
            int now = Environment.TickCount;
            bool nuestro = s == avisoTxt && now - avisoT < 4500;
            if (nuestro) return;                                   // ya esta en pantalla
            bool ocupado = N("IS_SCRIPTED_CONVERSATION_ONGOING") || (N("IS_MESSAGE_BEING_DISPLAYED") && now - avisoT > 4500);
            if (ocupado)
            {
                // hay un dialogo o texto de mision: no se pisa, se dibuja mas arriba
                Texto(s, 0.5f, 0.78f - fila * 0.032f, 0.30f, BlancoPuro, 255, 1);
                return;
            }
            try { GTA.Native.Function.Call("PRINT_STRING_WITH_LITERAL_STRING_NOW", "STRING", s, 4500, 1); avisoTxt = s; avisoT = now; } catch { }
        }

        // barra fina (necesidades, nafta); alerta = parpadea en rojo como la vida del juego
        public static void Barra(string nombre, float valor, float x, float y, float w, int[] c, bool alerta)
        {
            if (!Visible()) return;
            float h = 0.009f;
            int a = 230;
            if (alerta && (Environment.TickCount / 400) % 2 == 0) a = 110;
            Rect(x - 0.002f, y - 0.003f, w + 0.004f, h + 0.006f, new int[] { 0, 0, 0 }, 170);
            Rect(x, y, w, h, alerta ? RojoOscuroDe(c) : CelesteOscuroDe(c), 190);
            float fw = w * Math.Max(0f, Math.Min(1f, valor / 100f));
            if (fw > 0.001f) Rect(x, y, fw, h, alerta ? Rojo : c, a);
            Texto(nombre, x - 0.006f, y - 0.007f, 0.21f, alerta ? RojoClaro : Blanco, 255, 2);
        }

        static int[] CelesteOscuroDe(int[] c) { return new int[] { c[0] / 3, c[1] / 3, c[2] / 3 }; }
        static int[] RojoOscuroDe(int[] c) { return new int[] { 60, 25, 25 }; }

        // barra de progreso al centro (cargar nafta, curarse, comer)
        public static void Progreso(string s, float valor)
        {
            if (!Visible()) return;
            Rect(0.4f, 0.80f, 0.2f, 0.05f, new int[] { 0, 0, 0 }, 170);
            Texto(s, 0.5f, 0.805f, 0.27f, BlancoPuro, 255, 1);
            Rect(0.41f, 0.833f, 0.18f, 0.008f, CelesteOscuro, 200);
            float fw = 0.18f * Math.Max(0f, Math.Min(1f, valor));
            if (fw > 0.001f) Rect(0.41f, 0.833f, fw, 0.008f, Celeste, 255);
        }
    }
    // </estilo-iv>
}
