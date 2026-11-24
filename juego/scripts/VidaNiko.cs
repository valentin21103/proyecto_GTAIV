// VidaNiko.cs - salud y necesidades de Niko (parte de "Vida de Niko")
//
// SALUD
//   Con poca vida Niko empieza a desangrarse: pierde vida de a poco hasta que se pone una
//   gasa. Mientras tiene poca vida camina herido y no puede correr.
//   Objetos (se compran en farmacias, hospitales y estaciones de servicio con B):
//     Gasa        -> frena la sangre y cura toda la vida
//     Analgesico  -> cura la mitad de la vida, rapido (no frena la sangre)
//     Jeringa     -> te va curando de a poco durante unos segundos (no frena la sangre)
//   U rapido = usa lo que mas te sirve (gasa si sangras). Mantener U = rueda de objetos:
//   move el mouse (o flechas / 1-2-3) hacia el objeto y solta U.
//   Ctrl+F9 = registrar el lugar donde estas como farmacia (Farmacias.ini).
//
// NECESIDADES
//   Hambre y sed bajan con el tiempo; la vejiga se llena (mas rapido si tomas algo).
//   Comer o tomar en los puestos, maquinas y locales del juego las llena solo.
//   N = orinar (si te ve la policia, una estrella).
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

        const int VK_B = 0x42, VK_U = 0x55, VK_N = 0x4E, VK_SHIFT = 0x10, VK_CTRL = 0x11, VK_F9 = 0x78;
        const int VK_UP = 0x26, VK_DOWN = 0x28, VK_LEFT = 0x25, VK_RIGHT = 0x27, VK_ENTER = 0x0D, VK_BACK = 0x08;
        const int VK_1 = 0x31, VK_2 = 0x32, VK_3 = 0x33;
        const string ArchivoIni = "scripts\\VidaNiko.ini";
        const string ArchivoEstado = "scripts\\VidaNikoEstado.txt";
        const string ArchivoNafta = "scripts\\Nafta.ini";
        const string ArchivoFarmacias = "scripts\\Farmacias.ini";

        // ---- objetos ----
        const int GASA = 0, ANALGESICO = 1, JERINGA = 2;
        static readonly string[] NombreObj = { "Gasa", "Analgesico", "Jeringa" };
        static readonly string[] DescObj = { "frena la sangre, cura todo", "cura la mitad", "cura de a poco" };
        int[] precio = { 80, 40, 150 };
        int[] maxObj = { 3, 3, 2 };
        int[] tengo = { 1, 1, 0 };

        // ---- opciones ----
        bool salud = true, necesidades = true;
        int vidaCritica = 30;
        int segundosSangrado = 8;
        float minutosHambre = 90f, minutosSed = 60f, minutosVejiga = 75f;

        // ---- estado ----
        float hambre = 80f, sed = 80f, vejiga = 20f;
        int vidaAnterior = -1, vidaMax = 100;
        bool sangrando, herido, estabaMuerto;
        int proximoSangrado, proximoDano, proximoGuardado, ultimoTick, ignorarSubidaHasta;
        bool avisoHambre, avisoSed, avisoVejiga;
        int accion, accionFin, accionObj = -1;      // 1 usar objeto, 2 orinar
        int jeringaHasta, proximaJeringa;           // curacion gradual
        bool[] prev = new bool[256];
        string aviso; int avisoHasta;

        // tienda
        bool tienda; int tiendaSel; string tiendaNombre;
        // rueda
        bool rueda; int ruedaSel = -1; int uDesde; float ruedaX, ruedaY;

        class Lugar { public string Nombre; public Vector3 Pos; public float Radio; public Blip B; }
        List<Lugar> lugares = new List<Lugar>();
        Dictionary<string, AnimationSet> sets = new Dictionary<string, AnimationSet>();

        public VidaNiko()
        {
            this.Interval = 0;
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
                if (!File.Exists(ArchivoIni))
                {
                    File.WriteAllLines(ArchivoIni, new string[] {
                        "; VidaNiko - salud y necesidades de Niko",
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
                    else if (k == "preciogasa") precio[GASA] = (int)v;
                    else if (k == "precioanalgesico") precio[ANALGESICO] = (int)v;
                    else if (k == "preciojeringa") precio[JERINGA] = (int)v;
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
                    else if (k == "Gasas") tengo[GASA] = (int)v;
                    else if (k == "Analgesicos") tengo[ANALGESICO] = (int)v;
                    else if (k == "Jeringas") tengo[JERINGA] = (int)v;
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
                    "Gasas=" + tengo[GASA], "Analgesicos=" + tengo[ANALGESICO], "Jeringas=" + tengo[JERINGA] });
            }
            catch { }
        }

        // farmacias (Farmacias.ini) + estaciones de servicio (Nafta.ini)
        void CargarLugares()
        {
            try
            {
                if (!File.Exists(ArchivoFarmacias))
                {
                    File.WriteAllLines(ArchivoFarmacias, new string[] {
                        "; Farmacias y hospitales donde se compran gasas, analgesicos y jeringas.",
                        "; FARMACIA = nombre | x, y, z | radio",
                        "; Para agregar una: parate en la puerta en el juego y apreta Ctrl+F9.",
                        "; Pill-Pharm de East Island City (aproximada, frente al complejo amurallado de Yorktown Ave)",
                        "FARMACIA = Pill-Pharm East Island City | 1299.0, 690.0, 36.0 | 45",
                    });
                }
                LeerLugares(ArchivoFarmacias, "FARMACIA", true);
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
                g.Nombre = p[0].Trim();
                g.Pos = new Vector3(F(c[0], 0), F(c[1], 0), F(c[2], 0));
                g.Radio = clave == "ESTACION" ? 22f : (p.Length >= 3 ? F(p[2], 15f) : 15f);
                if (conBlip) PonerBlip(g);
                lugares.Add(g);
            }
        }

        void PonerBlip(Lugar g)
        {
            try { g.B = Blip.AddBlip(g.Pos); g.B.Name = "Farmacia"; g.B.ShowOnlyWhenNear = false; } catch { }
        }

        void RegistrarFarmacia(Ped me)
        {
            try
            {
                Vector3 p = me.Position;
                Lugar g = new Lugar();
                g.Nombre = "Farmacia " + (lugares.Count + 1);
                g.Pos = p; g.Radio = 15f;
                PonerBlip(g);
                lugares.Add(g);
                File.AppendAllText(ArchivoFarmacias, "FARMACIA = " + g.Nombre + " | " +
                    p.X.ToString("0.00", CultureInfo.InvariantCulture) + ", " +
                    p.Y.ToString("0.00", CultureInfo.InvariantCulture) + ", " +
                    p.Z.ToString("0.00", CultureInfo.InvariantCulture) + " | 15\r\n");
                Aviso("Farmacia registrada aca (ya podes comprar con B)");
            }
            catch { Aviso("No se pudo registrar la farmacia"); }
        }

        Lugar TiendaCerca(Vector3 p)
        {
            foreach (Lugar g in lugares) if (g.Pos.DistanceTo(p) <= g.Radio) return g;
            return null;
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

        void SetVida(Ped me, int v)
        {
            ignorarSubidaHasta = Game.GameTime + 600;
            try { me.Health = v; } catch { }
            vidaAnterior = v;
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
            if (muerto)
            {
                estabaMuerto = true; accion = 0; jeringaHasta = 0;
                if (rueda) { rueda = false; Lento(false); Control(true); }
                if (tienda) { tienda = false; Control(true); }
                return;
            }
            if (estabaMuerto)
            {
                estabaMuerto = false;
                hambre = Math.Max(hambre, 70f); sed = Math.Max(sed, 70f); vejiga = Math.Min(vejiga, 30f);
                vidaAnterior = -1; herido = false; sangrando = false;
                try { Function.Call("SET_ANIM_GROUP_FOR_CHAR", me, "move_player"); } catch { }
            }

            int vida = 0;
            try { vida = me.Health; } catch { return; }
            if (vida > vidaMax) vidaMax = vida;
            bool mision = EnMision();

            // detectar comer/tomar: el juego te sube la vida cuando comes o tomas algo
            if (vidaAnterior >= 0 && vida > vidaAnterior && now > ignorarSubidaHasta && accion == 0 && now > jeringaHasta)
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
                if (vejiga >= 100f && accion == 0) { vejiga = 0f; Aviso("No aguantaste mas... Niko se hizo encima"); }
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
                if (proximoSangrado != 0 && vida > 5) SetVida(me, vida - 1);   // no te mata solo: te deja en las ultimas
                proximoSangrado = now + segundosSangrado * 1000;
            }
            if (!sangrando) proximoSangrado = 0;

            // jeringa: cura de a poco
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

        // ================= objetos =================
        int MejorObjeto(int vida)
        {
            if (sangrando && tengo[GASA] > 0) return GASA;
            if (vida * 100 <= vidaMax * 50 && tengo[ANALGESICO] > 0) return ANALGESICO;
            if (tengo[JERINGA] > 0) return JERINGA;
            if (tengo[ANALGESICO] > 0) return ANALGESICO;
            if (tengo[GASA] > 0) return GASA;
            return -1;
        }

        void Usar(Ped me, int obj, int vida, int now)
        {
            if (obj < 0) { Aviso("No tenes nada para curarte (compra con B en farmacias o estaciones)"); return; }
            if (tengo[obj] <= 0) { Aviso("No tenes " + NombreObj[obj].ToLowerInvariant()); return; }
            if (vida >= vidaMax && !(obj == GASA && sangrando)) { Aviso("No estas herido"); return; }
            tengo[obj]--;
            accion = 1; accionObj = obj;
            try { me.Task.ClearAllImmediately(); } catch { }
            if (obj == GASA) { accionFin = now + 3500; Anim(me, "cop_search_idles", "idle_check_under_vehicle", false); }
            else { accionFin = now + 1200; Anim(me, "amb@beg_standing", "take_obj", false); }
        }

        void TerminarUso(Ped me, int vida)
        {
            if (accionObj == GASA)
            {
                sangrando = false;
                SetVida(me, vidaMax);
                Aviso("Te pusiste una gasa: la sangre paro. Gasas: " + tengo[GASA]);
            }
            else if (accionObj == ANALGESICO)
            {
                SetVida(me, Math.Min(vidaMax, vida + vidaMax / 2));
                Aviso("Tomaste un analgesico" + (sangrando ? " (seguis sangrando: ponete una gasa)" : "") + ". Te quedan " + tengo[ANALGESICO]);
            }
            else if (accionObj == JERINGA)
            {
                jeringaHasta = Game.GameTime + 20000;   // ~80 de vida en 20 s
                Aviso("Te inyectaste: te vas curando de a poco" + (sangrando ? " (seguis sangrando)" : ""));
            }
        }

        void TickAcciones(Ped me, int now, int vida)
        {
            bool shift = Down(VK_SHIFT), ctrl = Down(VK_CTRL);
            bool aPie = !me.isInVehicle() && me.isAliveAndWell;
            bool b = Pressed(VK_B) && !shift, n = Pressed(VK_N) && !shift, f9 = Pressed(VK_F9);
            bool uDown = Down(VK_U);
            bool uPress = uDown && !prev[VK_U], uRelease = !uDown && prev[VK_U];
            prev[VK_U] = uDown;

            if (accion != 0)
            {
                if (!aPie) { accion = 0; return; }
                if (now >= accionFin)
                {
                    if (accion == 1) TerminarUso(me, vida);
                    else if (accion == 2) { vejiga = 0f; Aviso("Que alivio"); }
                    accion = 0;
                    try { me.Task.ClearAll(); } catch { }
                }
                return;
            }

            if (ctrl && f9 && aPie) { RegistrarFarmacia(me); return; }

            // ---- tienda ----
            if (tienda)
            {
                if (Pressed(VK_UP)) tiendaSel = (tiendaSel + 2) % 3;
                if (Pressed(VK_DOWN)) tiendaSel = (tiendaSel + 1) % 3;
                if (Pressed(VK_ENTER))
                {
                    int o = tiendaSel;
                    if (tengo[o] >= maxObj[o]) Aviso("Ya tenes " + tengo[o] + " (maximo " + maxObj[o] + ")");
                    else if (Player.Money < precio[o]) Aviso("No te alcanza ($" + precio[o] + ")");
                    else { Player.Money = Player.Money - precio[o]; tengo[o]++; Aviso("Compraste: " + NombreObj[o] + " (" + tengo[o] + "/" + maxObj[o] + ")"); }
                }
                if (b || Pressed(VK_BACK) || !aPie) { tienda = false; Control(true); }
                return;
            }

            // ---- rueda de objetos (mantener U) ----
            if (salud)
            {
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
                    if (ruedaY < -40 && Math.Abs(ruedaX) < -ruedaY) ruedaSel = GASA;
                    else if (ruedaX < -40) ruedaSel = ANALGESICO;
                    else if (ruedaX > 40) ruedaSel = JERINGA;
                    if (Pressed(VK_UP) || Pressed(VK_1)) ruedaSel = GASA;
                    if (Pressed(VK_LEFT) || Pressed(VK_2)) ruedaSel = ANALGESICO;
                    if (Pressed(VK_RIGHT) || Pressed(VK_3)) ruedaSel = JERINGA;
                    if (uRelease || !aPie)
                    {
                        rueda = false; Lento(false); Control(true);
                        if (ruedaSel >= 0 && aPie) Usar(me, ruedaSel, vida, now);
                    }
                    uDesde = 0;
                    return;
                }
                if (uRelease && uDesde > 0)
                {
                    uDesde = 0;
                    if (aPie) Usar(me, MejorObjeto(vida), vida, now);
                    return;
                }
            }
            if (!aPie) return;

            // ---- abrir tienda ----
            Lugar t = TiendaCerca(me.Position);
            if (t != null && salud)
            {
                if (b) { tienda = true; tiendaSel = sangrando ? GASA : 0; tiendaNombre = t.Nombre; Control(false); return; }
            }

            // ---- orinar ----
            if (n && necesidades)
            {
                if (vejiga < 15f) Aviso("No tenes ganas");
                else
                {
                    accion = 2; accionFin = now + 6000;
                    try { me.Task.ClearAllImmediately(); } catch { }
                    Anim(me, "amb@bouncer_idles_b", "lookaround_a", true);
                    try
                    {
                        foreach (Ped p in World.GetPeds(me.Position, 20f))
                        {
                            if (!Game.Exists(p) || p.Equals(me)) continue;
                            if (Function.Call<int>("GET_PED_TYPE", p) == 6 && Player.WantedLevel < 1) { Player.WantedLevel = 1; Aviso("Un policia te vio... falta a la moral publica"); break; }
                        }
                    }
                    catch { }
                }
            }

            if (t != null && salud && !tienda) Ayuda("B: comprar en " + t.Nombre);
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
            bool mostrar = accion != 0 || critico || sangrando || Game.GameTime < jeringaHasta ||
                           (necesidades && (hambre < 50f || sed < 50f || vejiga > 60f));
            if (!mostrar) return;
            float y = 0.90f;
            if (necesidades)
            {
                Barra("VEJIGA", vejiga, y, vejiga > 80f); y -= 0.022f;
                Barra("SED", sed, y, sed < 25f); y -= 0.022f;
                Barra("HAMBRE", hambre, y, hambre < 25f); y -= 0.022f;
            }
            if (salud) { Barra(sangrando ? "SANGRANDO" : "SALUD", pct, y, critico || sangrando); y -= 0.022f; }
            if (sangrando) Texto("U: gasa (" + tengo[GASA] + ")   mantener U: rueda", 0.84f, y - 0.012f, 0.22f, 255, 120, 100, true);
            if (accion == 1 && accionObj >= 0) Texto(accionObj == GASA ? "Poniendote una gasa..." : (accionObj == ANALGESICO ? "Tomando un analgesico..." : "Inyectandote..."), 0.5f, 0.85f, 0.32f, 255, 255, 255, true);
            if (accion == 2) Texto("Orinando...", 0.5f, 0.85f, 0.32f, 255, 255, 255, true);
        }

        void DibujarTienda()
        {
            float x = 0.5f, y = 0.32f;
            Function.Call("DRAW_RECT", x, y + 0.075f, 0.36f, 0.22f, 0, 0, 0, 180);
            Texto(tiendaNombre != null ? tiendaNombre.ToUpperInvariant() : "FARMACIA", x, y - 0.02f, 0.38f, 120, 220, 140, true);
            for (int i = 0; i < 3; i++)
            {
                float yy = y + 0.025f + i * 0.04f;
                bool s = i == tiendaSel;
                if (s) Function.Call("DRAW_RECT", x, yy + 0.014f, 0.34f, 0.036f, 255, 255, 255, 40);
                Texto(NombreObj[i] + "  $" + precio[i] + "   (" + DescObj[i] + ")   tenes " + tengo[i] + "/" + maxObj[i], x, yy, 0.27f, s ? 255 : 190, s ? 255 : 190, s ? 255 : 190, true);
            }
            Texto("Flechas: elegir   Enter: comprar   B: salir", x, y + 0.155f, 0.22f, 190, 190, 190, true);
        }

        void DibujarRueda()
        {
            float cx = 0.5f, cy = 0.5f;
            Function.Call("DRAW_RECT", cx, cy, 0.34f, 0.40f, 0, 0, 0, 120);
            float[] px = { cx, cx - 0.11f, cx + 0.11f };
            float[] py = { cy - 0.12f, cy + 0.08f, cy + 0.08f };
            for (int i = 0; i < 3; i++)
            {
                bool s = i == ruedaSel;
                bool hay = tengo[i] > 0;
                Function.Call("DRAW_RECT", px[i], py[i], 0.1f, 0.09f, s ? 230 : 40, s ? 200 : 40, s ? 60 : 40, s ? 220 : 180);
                int c = hay ? 255 : 120;
                Texto(NombreObj[i], px[i], py[i] - 0.03f, 0.3f, s ? 0 : c, s ? 0 : c, s ? 0 : c, true);
                Texto("x" + tengo[i], px[i], py[i] + 0.0f, 0.3f, s ? 0 : c, s ? 0 : c, s ? 0 : c, true);
            }
            Texto(ruedaSel >= 0 ? DescObj[ruedaSel] : "Move el mouse (o flechas) y solta U", cx, cy + 0.15f, 0.26f, 230, 230, 230, true);
        }

        void DibujarAviso()
        {
            Function.Call("DRAW_RECT", 0.5f, 0.165f, 0.5f, 0.045f, 0, 0, 0, 160);
            Texto(aviso, 0.5f, 0.148f, 0.30f, 255, 255, 255, true);
        }
    }
}
