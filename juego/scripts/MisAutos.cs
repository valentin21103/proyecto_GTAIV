// MisAutos.cs - garage personal estilo GTA V + nafta (parte de "Vida de Niko")
//
// AUTOS
//   Manejando:  L = guardar este auto en "Mis autos" (y queda como tu auto activo)
//   A pie:      L = abrir la lista   (flechas elegir, Enter pedirlo, Supr borrarlo, L cerrar)
//   El auto activo no desaparece, tiene icono en el mapa y se acuerda de su nafta.
//
// BAUL (solo tu auto activo)
//   Apreta E cerca de tu auto, elegi 'Abrir baul' (parado atras del auto) y podes guardar o sacar armas
//   (flechas izq/der para cambiar de lado, arriba/abajo para elegir, Enter para mover, E para cerrar).
//
// NAFTA
//   Todos los autos gastan nafta al andar (no durante misiones).
//   En una estacion (icono en el mapa): bajate al lado del auto y apreta E: menu para cargar
//   nafta o comprar un bidon (E de nuevo para cortar la carga). Lejos de una estacion,
//   E junto al auto usa un bidon. No aparece ningun cartel solo: todo es con E.
//   Datos de estaciones y tanques en scripts\Nafta.ini (basados en Realistic Fuel Mod).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using GTA;
using GTA.Native;

namespace MisAutosMod
{
    public class MisAutos : Script
    {
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);

        const int VK_L = 0x4C, VK_UP = 0x26, VK_DOWN = 0x28, VK_ENTER = 0x0D, VK_DEL = 0x2E;
        const int VK_E = 0x45, VK_BACK = 0x08, VK_W = 0x57, VK_LEFT = 0x25, VK_RIGHT = 0x27;
        const int MaxBaul = 8;
        const int MaxAutos = 20;
        const string Archivo = "scripts\\MisAutos.txt";
        const string ArchivoNafta = "scripts\\Nafta.ini";
        const string ArchivoEstado = "scripts\\NaftaEstado.txt";

        // ---------- datos ----------
        class Auto
        {
            public int Modelo, C1, C2, C3, C4, Extras;
            public string Nombre;
            public float Nafta = -1f;      // litros; -1 = tanque lleno al pedirlo
            public List<int[]> Baul = new List<int[]>();   // {arma, municion}
        }

        class Tanque
        {
            public Vehicle V;
            public float L, Cap, Res, Drain;
            public bool AvisoReserva, AvisoVacio;
            public int Visto;
        }

        class Estacion
        {
            public string Nombre;
            public Vector3 Pos;
            public float Radio, Precio;
            public Blip B;
        }

        class Datos { public float Cap, Res, Drain; }

        List<Auto> autos = new List<Auto>();
        List<Tanque> tanques = new List<Tanque>();
        List<Estacion> estaciones = new List<Estacion>();
        Dictionary<string, Datos> datosModelo = new Dictionary<string, Datos>();

        bool naftaActiva = true;
        float multConsumo = 1f, precioBidon = 60f, litrosBidon = 10f;
        int maxBidones = 3, bidones = 1;

        Vehicle activo;
        int activoIdx = -1;
        // VidaNiko usa esto para la guantera
        static readonly Guid VidaNikoGuid = new Guid("3f6c2b8e-7a41-4d2e-9b51-0c4e8a1d7f22");
        string miAutoEnviado = "";
        Blip blip;
        Vector3 blipPos;
        string nombreActivo;
        int proximoBlip, proximoGuardado, ultimoTick;
        bool sucio;

        bool menu;
        bool teclaE;                // E apretada en este tick (la tecla de interaccion)
        bool menuNafta; int selNafta;   // menu chico de la estacion: 0 cargar, 1 bidon
        bool baul; int baulLado, baulSel, baulCierre;   // lado 0 = encima, 1 = baul
        int sel;
        bool[] prev = new bool[256];
        string aviso;
        int avisoHasta;
        Random rnd = new Random();

        // carga de nafta
        int carga;               // 0 nada, 1 abriendo, 2 cargando, 3 cerrando
        bool cargaBidon;
        Tanque cargaT;
        Estacion cargaE;
        float cargaLitros, cargaDeuda;
        int cargaPagado, cargaFase, cargaUltimo;
        Dictionary<string, AnimationSet> sets = new Dictionary<string, AnimationSet>();

        public MisAutos()
        {
            this.Interval = 0;
            Cargar();
            CargarNafta();
            this.Tick += new EventHandler(OnTick);
        }

        // ================= archivos =================
        static float F(string s, float def)
        {
            float v;
            if (float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return def;
        }

        void Cargar()
        {
            try
            {
                if (!File.Exists(Archivo)) return;
                foreach (string l in File.ReadAllLines(Archivo))
                {
                    if (l.Trim().Length == 0 || l.StartsWith("#")) continue;
                    string[] p = l.Split('|');
                    if (p.Length < 7) continue;
                    Auto a = new Auto();
                    a.Modelo = int.Parse(p[0]); a.C1 = int.Parse(p[1]); a.C2 = int.Parse(p[2]);
                    a.C3 = int.Parse(p[3]); a.C4 = int.Parse(p[4]); a.Extras = int.Parse(p[5]);
                    a.Nombre = p[6];
                    if (p.Length >= 8) a.Nafta = F(p[7], -1f);
                    if (p.Length >= 9 && p[8].Trim().Length > 0)
                        foreach (string w in p[8].Split(';'))
                        {
                            string[] q = w.Split(':');
                            if (q.Length == 2) a.Baul.Add(new int[] { int.Parse(q[0]), int.Parse(q[1]) });
                        }
                    autos.Add(a);
                }
            }
            catch { }
        }

        void Guardar()
        {
            try
            {
                List<string> o = new List<string>();
                o.Add("# MisAutos: modelo|color1|color2|color3|color4|accesorios|nombre|nafta(litros)|baul(arma:municion;...)");
                foreach (Auto a in autos)
                    o.Add(a.Modelo + "|" + a.C1 + "|" + a.C2 + "|" + a.C3 + "|" + a.C4 + "|" + a.Extras + "|" + a.Nombre.Replace("|", " ") + "|" + a.Nafta.ToString("0.0", CultureInfo.InvariantCulture) + "|" + BaulTexto(a));
                File.WriteAllLines(Archivo, o.ToArray());
                File.WriteAllLines(ArchivoEstado, new string[] { "Bidones=" + bidones });
            }
            catch { }
            sucio = false;
        }

        void CargarNafta()
        {
            try
            {
                if (File.Exists(ArchivoNafta))
                {
                    foreach (string raw in File.ReadAllLines(ArchivoNafta))
                    {
                        string l = raw.Trim();
                        if (l.Length == 0 || l.StartsWith(";") || l.StartsWith("[") || l.IndexOf('=') < 0) continue;
                        string k = l.Substring(0, l.IndexOf('=')).Trim().ToUpperInvariant();
                        string v = l.Substring(l.IndexOf('=') + 1).Trim();
                        if (k == "ACTIVADO") naftaActiva = v != "0";
                        else if (k == "CONSUMO") multConsumo = F(v, 1f);
                        else if (k == "PRECIOBIDON") precioBidon = F(v, 60f);
                        else if (k == "LITROSBIDON") litrosBidon = F(v, 10f);
                        else if (k == "MAXBIDONES") maxBidones = (int)F(v, 3f);
                        else if (k == "ESTACION")
                        {
                            string[] p = v.Split('|');
                            if (p.Length < 4) continue;
                            string[] c = p[1].Split(',');
                            Estacion e = new Estacion();
                            e.Nombre = p[0].Trim();
                            e.Pos = new Vector3(F(c[0], 0), F(c[1], 0), F(c[2], 0));
                            e.Radio = F(p[2], 10f) + 6f;
                            e.Precio = F(p[3], 5f);
                            estaciones.Add(e);
                        }
                        else if (k == "AUTO")
                        {
                            string[] p = v.Split('|');
                            if (p.Length < 4) continue;
                            Datos d = new Datos();
                            d.Cap = F(p[1], 60f); d.Res = F(p[2], 8f); d.Drain = F(p[3], 8f);
                            datosModelo[p[0].Trim().ToUpperInvariant()] = d;
                        }
                    }
                }
                if (File.Exists(ArchivoEstado))
                    foreach (string l in File.ReadAllLines(ArchivoEstado))
                        if (l.StartsWith("Bidones=")) bidones = (int)F(l.Substring(8), 1f);
            }
            catch { }
            foreach (Estacion e in estaciones)
            {
                try { e.B = Blip.AddBlip(e.Pos); e.B.Name = "Estacion de servicio"; e.B.ShowOnlyWhenNear = true; } catch { }
            }
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

        void Aviso(string s) { aviso = s; avisoHasta = Game.GameTime + 4000; }

        int PlayerId() { try { return Function.Call<int>("GET_PLAYER_ID"); } catch { return 0; } }

        bool EnMision() { try { return Function.Call<bool>("GET_MISSION_FLAG"); } catch { return false; } }

        string ClaveModelo(int modelo)
        {
            try { string k = Function.Call<string>("GET_DISPLAY_NAME_FROM_VEHICLE_MODEL", modelo); if (k != null) return k.ToUpperInvariant(); }
            catch { }
            return "";
        }

        string NombreDe(int modelo)
        {
            try
            {
                string key = Function.Call<string>("GET_DISPLAY_NAME_FROM_VEHICLE_MODEL", modelo);
                string txt = Function.Call<string>("GET_STRING_FROM_TEXT_FILE", key);
                if (txt != null && txt.Length > 0 && txt != "NULL") return txt;
                if (key != null && key.Length > 0) return key;
            }
            catch { }
            return "Auto " + modelo;
        }

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

        // ================= tanques =================
        Tanque BuscarTanque(Vehicle v)
        {
            foreach (Tanque t in tanques) { try { if (t.V.Equals(v)) return t; } catch { } }
            return null;
        }

        Tanque TanqueDe(Vehicle v)
        {
            Tanque t = BuscarTanque(v);
            if (t != null) return t;
            t = new Tanque();
            t.V = v;
            Datos d = null;
            try { datosModelo.TryGetValue(ClaveModelo(v.Model.Hash), out d); } catch { }
            t.Cap = d != null ? d.Cap : 60f;
            t.Res = d != null ? d.Res : 8f;
            t.Drain = d != null ? d.Drain : 8f;
            bool esActivo = false;
            try { esActivo = activo != null && Game.Exists(activo) && activo.Equals(v); } catch { }
            if (esActivo && activoIdx >= 0 && activoIdx < autos.Count)
                t.L = autos[activoIdx].Nafta < 0 ? t.Cap : Math.Min(autos[activoIdx].Nafta, t.Cap);
            else
                t.L = t.Cap * (0.15f + (float)rnd.NextDouble() * 0.55f);   // autos de la calle: 15% a 70%
            tanques.Add(t);
            return t;
        }

        bool EsActivo(Vehicle v)
        {
            try { return activo != null && Game.Exists(activo) && activo.Equals(v); } catch { return false; }
        }

        void SincronizarActivo(Tanque t)
        {
            if (activoIdx < 0 || activoIdx >= autos.Count || !EsActivo(t.V)) return;
            if (Math.Abs(autos[activoIdx].Nafta - t.L) > 0.05f) { autos[activoIdx].Nafta = t.L; sucio = true; }
        }

        // ================= autos =================
        Auto Leer(Vehicle v)
        {
            Auto a = new Auto();
            a.Modelo = v.Model.Hash;
            try
            {
                Pointer p1 = new Pointer(typeof(int)), p2 = new Pointer(typeof(int));
                Function.Call("GET_CAR_COLOURS", v, p1, p2);
                a.C1 = Convert.ToInt32(p1.Value); a.C2 = Convert.ToInt32(p2.Value);
                Pointer p3 = new Pointer(typeof(int)), p4 = new Pointer(typeof(int));
                Function.Call("GET_EXTRA_CAR_COLOURS", v, p3, p4);
                a.C3 = Convert.ToInt32(p3.Value); a.C4 = Convert.ToInt32(p4.Value);
            }
            catch { }
            for (int i = 1; i <= 9; i++)
            {
                try { if (Function.Call<bool>("DOES_EXTRA_EXIST", v, i) && Function.Call<bool>("IS_VEHICLE_EXTRA_TURNED_ON", v, i)) a.Extras |= 1 << i; }
                catch { }
            }
            a.Nombre = NombreDe(a.Modelo);
            return a;
        }

        void Aplicar(Vehicle v, Auto a)
        {
            try { Function.Call("CHANGE_CAR_COLOUR", v, a.C1, a.C2); } catch { }
            try { Function.Call("SET_EXTRA_CAR_COLOURS", v, a.C3, a.C4); } catch { }
            for (int i = 1; i <= 9; i++)
            {
                try { if (Function.Call<bool>("DOES_EXTRA_EXIST", v, i)) Function.Call("TURN_OFF_VEHICLE_EXTRA", v, i, (a.Extras & (1 << i)) == 0); }
                catch { }
            }
        }

        void SoltarActivo()
        {
            try { if (blip != null) blip.Delete(); } catch { }
            blip = null;
            try { if (activo != null && Game.Exists(activo)) activo.NoLongerNeeded(); } catch { }
            activo = null;
            activoIdx = -1;
        }

        void HacerActivo(Vehicle v, int idx, string nombre)
        {
            activo = v;
            activoIdx = idx;
            nombreActivo = nombre;
            try { Function.Call("SET_CAR_AS_MISSION_CAR", v); } catch { }
            PonerBlip();
        }

        void PonerBlip()
        {
            try { if (blip != null) blip.Delete(); } catch { }
            blip = null;
            try
            {
                if (activo == null || !Game.Exists(activo)) return;
                blipPos = activo.Position;
                blip = Blip.AddBlip(blipPos);
                blip.ShowOnlyWhenNear = false;
                blip.Name = nombreActivo != null ? nombreActivo : "Mi auto";
            }
            catch { blip = null; }
        }

        void GuardarAutoActual(Ped me)
        {
            Vehicle v = me.CurrentVehicle;
            if (v == null || !Game.Exists(v)) return;
            Auto a = Leer(v);
            Tanque t = TanqueDe(v);
            a.Nafta = t.L;
            int idx = -1;
            for (int i = 0; i < autos.Count; i++)
                if (autos[i].Modelo == a.Modelo && autos[i].C1 == a.C1 && autos[i].C2 == a.C2) { idx = i; break; }
            if (idx >= 0) { a.Baul = autos[idx].Baul; autos[idx] = a; Aviso("Actualizado en Mis autos: " + a.Nombre); }
            else
            {
                if (autos.Count >= MaxAutos) { Aviso("Mis autos esta lleno (" + MaxAutos + "). Borra uno con L > Supr."); return; }
                autos.Add(a); idx = autos.Count - 1;
                Aviso("Guardado en Mis autos: " + a.Nombre + "  (ahora es tu auto)");
            }
            if (!EsActivo(v)) { SoltarActivo(); HacerActivo(v, idx, a.Nombre); }
            else activoIdx = idx;
            Guardar();
        }

        void Pedir(Ped me, int idx)
        {
            Auto a = autos[idx];
            if (activo != null && Game.Exists(activo) && me.isInVehicle() && me.CurrentVehicle.Equals(activo))
            { Aviso("Ya estas en tu auto"); return; }
            SoltarActivo();
            try
            {
                double h = me.Heading * Math.PI / 180.0;
                Vector3 mp = me.Position;
                Vector3 cerca = new Vector3(mp.X - (float)Math.Sin(h) * 12f, mp.Y + (float)Math.Cos(h) * 12f, mp.Z);
                Vector3 pos = World.GetNextPositionOnStreet(cerca);
                if (pos.DistanceTo(me.Position) > 60f) pos = World.GetNextPositionOnStreet(me.Position);
                Vehicle v = World.CreateVehicle(new Model(a.Modelo), pos);
                if (v == null || !Game.Exists(v)) { Aviso("No se pudo traer el auto, proba en otro lugar"); return; }
                try { v.Heading = me.Heading; } catch { }
                Aplicar(v, a);
                HacerActivo(v, idx, a.Nombre);
                Tanque t = TanqueDe(v);
                Aviso("Te dejaron tu " + a.Nombre + " cerca  (nafta: " + (int)(t.L * 100f / t.Cap) + "%)");
            }
            catch { Aviso("No se pudo traer el auto"); }
        }

        // ================= loop =================
        void OnTick(object sender, EventArgs e)
        {
            Ped me;
            try { me = Player.Character; } catch { return; }
            if (!Game.Exists(me)) return;
            int now = Game.GameTime;
            float dt = ultimoTick == 0 ? 0f : Math.Min(0.25f, (now - ultimoTick) / 1000f);
            ultimoTick = now;
            teclaE = Pressed(VK_E);

            // auto activo destruido
            try
            {
                if (activo != null && (!Game.Exists(activo) || Function.Call<bool>("IS_CAR_DEAD", activo)))
                { SoltarActivo(); Aviso("Tu auto quedo destruido. Pedilo de nuevo con L"); }
            }
            catch { activo = null; }

            // icono del auto activo
            if (activo != null && now >= proximoBlip)
            {
                proximoBlip = now + 1000;
                try
                {
                    bool adentro = me.isInVehicle() && me.CurrentVehicle.Equals(activo);
                    if (adentro) { if (blip != null) { blip.Delete(); blip = null; } }
                    else if (blip == null || activo.Position.DistanceTo(blipPos) > 5f) PonerBlip();
                }
                catch { }
            }

            // avisar a VidaNiko si estas adentro de tu auto (guantera)
            string miAuto = "";
            try { if (activo != null && activoIdx >= 0 && activoIdx < autos.Count && me.isInVehicle() && me.CurrentVehicle.Equals(activo)) miAuto = autos[activoIdx].Nombre; } catch { }
            if (miAuto != miAutoEnviado)
            {
                try { SendScriptCommand(VidaNikoGuid, "miauto", miAuto); miAutoEnviado = miAuto; } catch { }
            }

            if (baul) { TickBaul(me, now); DibujarBaul(); }
            else if (naftaActiva) TickNafta(me, now, dt);

            // menu de autos
            bool l = Pressed(VK_L);
            if (!menu)
            {
                if (l && me.isAliveAndWell && carga == 0 && !baul && !menuNafta)
                {
                    if (me.isInVehicle()) GuardarAutoActual(me);
                    else if (autos.Count == 0) Aviso("Todavia no guardaste autos: subite a uno y apreta L");
                    else { menu = true; if (sel >= autos.Count) sel = 0; try { Function.Call("SET_PLAYER_CONTROL", PlayerId(), false); } catch { } }
                }
            }
            else
            {
                bool up = Pressed(VK_UP), down = Pressed(VK_DOWN), enter = Pressed(VK_ENTER), del = Pressed(VK_DEL);
                if (l || !me.isAliveAndWell || me.isInVehicle()) CerrarMenu();
                else
                {
                    if (up) sel = (sel + autos.Count - 1) % autos.Count;
                    if (down) sel = (sel + 1) % autos.Count;
                    if (enter) { CerrarMenu(); Pedir(me, sel); }
                    else if (del)
                    {
                        Aviso("Borrado de Mis autos: " + autos[sel].Nombre);
                        if (sel == activoIdx) SoltarActivo();
                        else if (sel < activoIdx) activoIdx--;
                        autos.RemoveAt(sel); Guardar();
                        if (autos.Count == 0) CerrarMenu(); else if (sel >= autos.Count) sel = autos.Count - 1;
                    }
                }
                if (menu) DibujarMenu();
            }

            if (sucio && now >= proximoGuardado) { proximoGuardado = now + 10000; Guardar(); }
            if (now < avisoHasta && aviso != null) DibujarAviso();
        }

        void CerrarMenu()
        {
            menu = false;
            try { Function.Call("SET_PLAYER_CONTROL", PlayerId(), true); } catch { }
        }

        // ================= nafta =================
        void TickNafta(Ped me, int now, float dt)
        {
            bool enAuto = false;
            Vehicle v = null;
            try
            {
                if (me.isInVehicle())
                {
                    v = me.CurrentVehicle;
                    Ped drv = v.GetPedOnSeat(VehicleSeat.Driver);
                    enAuto = drv != null && drv.Equals(me);
                }
            }
            catch { }

            if (enAuto && carga != 0) TerminarCarga(me, "");

            if (enAuto && v != null)
            {
                Tanque t = TanqueDe(v);
                t.Visto = now;
                float vel = 0f;
                try { vel = v.Speed; } catch { }
                bool acelera = Down(VK_W);
                if (!EnMision() && t.L > 0f && (vel > 0.3f || acelera))
                {
                    float litrosSeg = (t.Drain / 8f) * (0.005f + 0.0024f * vel) * multConsumo;
                    t.L = Math.Max(0f, t.L - litrosSeg * dt);
                }
                if (t.L <= t.Res && t.L > 0f && !t.AvisoReserva) { t.AvisoReserva = true; Aviso("Entraste en reserva: busca una estacion de servicio"); }
                if (t.L > t.Res + 1f) t.AvisoReserva = false;
                if (t.L <= 0f)
                {
                    try { Function.Call("SET_CAR_ENGINE_ON", v, false, true); } catch { }
                    if (!t.AvisoVacio) { t.AvisoVacio = true; Aviso(bidones > 0 ? "Sin nafta. Bajate y apreta E junto al auto para usar un bidon" :"Sin nafta. Empuja o busca otro auto"); }
                }
                else t.AvisoVacio = false;
                SincronizarActivo(t);
                DibujarNafta(t);
            }
            else if (me.isAliveAndWell && !menu)
            {
                TickCarga(me, now, dt);
            }

            // limpiar tanques de autos que ya no existen (el del auto activo se guarda en Mis autos)
            if (tanques.Count > 40)
                tanques.RemoveAll(delegate (Tanque x) { try { return !Game.Exists(x.V); } catch { return true; } });
        }

        Estacion EstacionCerca(Vector3 p)
        {
            foreach (Estacion e in estaciones) if (e.Pos.DistanceTo(p) <= e.Radio) return e;
            return null;
        }

        Vehicle AutoCerca(Ped me)
        {
            Vehicle mejor = null;
            float md = 4.5f;
            try
            {
                foreach (Vehicle v in World.GetVehicles(me.Position, 6f))
                {
                    if (!Game.Exists(v)) continue;
                    float d = v.Position.DistanceTo(me.Position);
                    if (d < md) { md = d; mejor = v; }
                }
            }
            catch { }
            return mejor;
        }

        void TickCarga(Ped me, int now, float dt)
        {
            // sin carteles automaticos: todo arranca al apretar E
            if (menuNafta) { TickMenuNafta(me, now); return; }

            if (carga == 0)
            {
                if (!teclaE) return;
                if (EstacionCerca(me.Position) != null || AutoCerca(me) != null || EnZonaBaul(me))
                {
                    menuNafta = true;
                    selNafta = 0;
                    try { Function.Call("SET_PLAYER_CONTROL", PlayerId(), false); } catch { }
                }
                return;
            }

            // cargando
            bool lejos = false;
            try { lejos = !Game.Exists(cargaT.V) || cargaT.V.Position.DistanceTo(me.Position) > 6f; } catch { lejos = true; }
            if (lejos) { TerminarCarga(me, "Te alejaste del auto"); return; }

            if (carga == 1 && now >= cargaFase)
            {
                carga = 2;
                Anim(me, "amb@bouncer_idles_b", "lookaround_a", true);
                cargaUltimo = now;
            }
            else if (carga == 2)
            {
                if (teclaE) { TerminarCarga(me, null); return; }
                float paso = 2.0f * dt;   // 2 litros por segundo
                float falta = cargaT.Cap - cargaT.L;
                if (cargaBidon) falta = Math.Min(falta, litrosBidon - cargaLitros);
                if (paso > falta) paso = falta;
                if (!cargaBidon)
                {
                    float costo = paso * cargaE.Precio;
                    int pagar = (int)(cargaDeuda + costo);
                    if (pagar > 0 && Player.Money < pagar) { TerminarCarga(me, "Te quedaste sin plata"); return; }
                    cargaDeuda += costo;
                    if (cargaDeuda >= 1f)
                    {
                        int p = (int)cargaDeuda;
                        Player.Money = Player.Money - p;
                        cargaPagado += p;
                        cargaDeuda -= p;
                    }
                }
                cargaT.L += paso;
                cargaLitros += paso;
                SincronizarActivo(cargaT);
                if (falta - paso <= 0.01f) { TerminarCarga(me, null); return; }
                DibujarCarga();
            }
            else if (carga == 3 && now >= cargaFase)
            {
                carga = 0;
                try { me.Task.ClearAll(); } catch { }
            }
            if (carga == 1 || carga == 3) DibujarCarga();
        }

        // menu chico de la estacion (se abre con E)
        void CerrarMenuNafta()
        {
            menuNafta = false;
            try { Function.Call("SET_PLAYER_CONTROL", PlayerId(), true); } catch { }
        }

        void TickMenuNafta(Ped me, int now)
        {
            Estacion est = EstacionCerca(me.Position);
            Vehicle v = AutoCerca(me);
            Tanque t = v != null ? TanqueDe(v) : null;
            bool zona = EnZonaBaul(me);
            bool up = Pressed(VK_UP), down = Pressed(VK_DOWN), enter = Pressed(VK_ENTER), back = Pressed(VK_BACK);
            if ((est == null && t == null && !zona) || !me.isAliveAndWell || me.isInVehicle() || back || teclaE) { CerrarMenuNafta(); return; }

            // opciones segun lo que tengas cerca: 0 cargar, 1 comprar bidon, 2 usar bidon, 3 baul
            List<int> ids = new List<int>();
            if (est != null) { ids.Add(0); ids.Add(1); }
            if (t != null) ids.Add(2);
            bool hayActivo = false;
            try { hayActivo = activo != null && activoIdx >= 0 && activoIdx < autos.Count && Game.Exists(activo) && activo.Position.DistanceTo(me.Position) < 7f; } catch { }
            if (hayActivo) ids.Add(3);
            if (ids.Count == 0) { CerrarMenuNafta(); return; }
            if (selNafta >= ids.Count) selNafta = ids.Count - 1;
            if (up) selNafta = (selNafta + ids.Count - 1) % ids.Count;
            if (down) selNafta = (selNafta + 1) % ids.Count;
            int id = ids[selNafta];

            if (enter)
            {
                if (id == 0)
                {
                    if (t == null) Aviso("Acercate a tu auto para cargar nafta");
                    else if (t.L >= t.Cap - 0.2f) Aviso("El tanque ya esta lleno");
                    else { CerrarMenuNafta(); EmpezarCarga(me, t, est, false, now); return; }
                }
                else if (id == 1)
                {
                    if (bidones >= maxBidones) Aviso("Ya tenes " + bidones + " bidones (maximo " + maxBidones + ")");
                    else if (Player.Money < (int)precioBidon) Aviso("No te alcanza para un bidon ($" + (int)precioBidon + ")");
                    else { Player.Money = Player.Money - (int)precioBidon; bidones++; sucio = true; Aviso("Compraste un bidon (" + bidones + "/" + maxBidones + ")"); }
                }
                else if (id == 2)
                {
                    if (bidones <= 0) Aviso("No tenes bidones");
                    else if (t.L >= t.Cap - 0.5f) Aviso("El tanque ya esta lleno");
                    else { CerrarMenuNafta(); EmpezarCarga(me, t, null, true, now); return; }
                }
                else
                {
                    if (!zona) Aviso("Ponete atras de tu auto para abrir el baul");
                    else { CerrarMenuNafta(); AbrirBaul(me); return; }
                }
            }

            float x = 0.03f, y = 0.16f, w = 0.3f;
            IV.Titulo(est != null ? "Estacion de servicio" : "Tu auto", x, y, w);
            y += 0.045f;
            for (int i = 0; i < ids.Count; i++)
            {
                string izq = "", der = null;
                bool apagado = false;
                int k = ids[i];
                if (k == 0)
                {
                    izq = "Cargar nafta";
                    if (t != null) der = "$" + est.Precio.ToString("0.00", CultureInfo.InvariantCulture) + "/L   tanque " + (int)(t.L * 100f / t.Cap) + "%";
                    else { der = "sin auto cerca"; apagado = true; }
                }
                else if (k == 1) { izq = "Comprar bidon"; der = "$" + (int)precioBidon + "   (" + bidones + "/" + maxBidones + ")"; }
                else if (k == 2) { izq = "Usar bidon"; der = "tenes " + bidones + "   tanque " + (int)(t.L * 100f / t.Cap) + "%"; apagado = bidones <= 0; }
                else
                {
                    izq = "Abrir baul";
                    der = zona ? autos[activoIdx].Baul.Count + "/" + MaxBaul + " armas" : "ponete atras del auto";
                    apagado = !zona;
                }
                IV.Item(izq, der, x, y, w, i == selNafta, apagado, 0.1f);
                y += IV.Fila;
            }
            IV.Pie("Arriba/Abajo: elegir   Enter: aceptar   E: cerrar", x, y, w);
        }

        void EmpezarCarga(Ped me, Tanque t, Estacion est, bool bidon, int now)
        {
            carga = 1;
            cargaT = t; cargaE = est; cargaBidon = bidon;
            cargaLitros = 0f; cargaDeuda = 0f; cargaPagado = 0;
            cargaFase = now + 1500;
            if (bidon) { bidones--; sucio = true; }
            try { Function.Call("SET_CAR_ENGINE_ON", t.V, false, true); } catch { }
            try { me.Task.ClearAllImmediately(); } catch { }
            try { me.Task.TurnTo(t.V.Position); } catch { }
            Anim(me, "amb@car_stash", "open_boot", false);
        }

        void TerminarCarga(Ped me, string motivo)
        {
            if (carga == 0) return;
            if (!cargaBidon && cargaDeuda > 0.01f)
            {
                int p = (int)Math.Ceiling(cargaDeuda);
                if (Player.Money >= p) { Player.Money = Player.Money - p; cargaPagado += p; }
                cargaDeuda = 0f;
            }
            string res = cargaBidon
                ? "Cargaste " + cargaLitros.ToString("0.0", CultureInfo.InvariantCulture) + " L del bidon"
                : "Cargaste " + cargaLitros.ToString("0.0", CultureInfo.InvariantCulture) + " L por $" + cargaPagado;
            Aviso(motivo != null && motivo.Length > 0 ? motivo + ". " + res : res);
            sucio = true;
            if (me.isInVehicle()) { carga = 0; return; }
            carga = 3;
            cargaFase = Game.GameTime + 1300;
            Anim(me, "car_boot", "close_boot", false);
        }


        // ================= baul =================
        static readonly int[] IdsArma = { 1, 23, 42, 4, 5, 28, 36, 7, 27, 43, 9, 29, 8, 10, 11, 26, 22, 31, 30, 12, 13, 32, 33, 14, 15, 34, 16, 17, 35, 21, 18 };
        static readonly string[] NombresArma = { "Bate", "Taco de pool", "Porra", "Granadas", "Molotov", "Bombas caseras", "Bombas adhesivas",
            "Glock 19", "Beretta M9A3", "Pistola con silenciador", "FN Five-seveN", "Desert Eagle", "Revolver 8 Bull",
            "Escopeta de corredera", "Escopeta de combate", "Escopeta recortada", "Escopeta de asalto", "AA12", "AA12 explosiva",
            "Micro-SMG", "MP5", "P90", "Subfusil dorado", "AK-47", "Carabina M4", "M249", "Francotirador PSG1", "Rifle M40", "DSR1",
            "Lanzagranadas", "RPG" };

        static string NombreArma(int id)
        {
            for (int i = 0; i < IdsArma.Length; i++) if (IdsArma[i] == id) return NombresArma[i];
            return "Arma " + id;
        }

        static string BaulTexto(Auto a)
        {
            List<string> l = new List<string>();
            foreach (int[] w in a.Baul) l.Add(w[0] + ":" + w[1]);
            return string.Join(";", l.ToArray());
        }

        // armas que Niko lleva encima: {arma, municion}
        List<int[]> ArmasEncima(Ped me)
        {
            List<int[]> r = new List<int[]>();
            for (int slot = 1; slot <= 10; slot++)
            {
                try
                {
                    Pointer w = new Pointer(typeof(int)), a1 = new Pointer(typeof(int)), a2 = new Pointer(typeof(int));
                    Function.Call("GET_CHAR_WEAPON_IN_SLOT", me, slot, w, a1, a2);
                    int id = Convert.ToInt32(w.Value);
                    if (id <= 0 || id == 41 || id == 45 || id == 46) continue;   // nada / paracaidas / camara
                    int muni = 0;
                    try { Pointer m = new Pointer(typeof(int)); Function.Call("GET_AMMO_IN_CHAR_WEAPON", me, id, m); muni = Convert.ToInt32(m.Value); } catch { }
                    r.Add(new int[] { id, Math.Max(muni, 1) });
                }
                catch { }
            }
            return r;
        }

        Vector3 Trasera(Vehicle v)
        {
            double h = v.Heading * Math.PI / 180.0;
            Vector3 p = v.Position;
            return new Vector3(p.X + (float)Math.Sin(h) * 2.4f, p.Y - (float)Math.Cos(h) * 2.4f, p.Z);
        }

        bool EnZonaBaul(Ped me)
        {
            try
            {
                return activo != null && activoIdx >= 0 && activoIdx < autos.Count && Game.Exists(activo)
                    && !me.isInVehicle() && me.isAliveAndWell && Trasera(activo).DistanceTo(me.Position) <= 2.6f;
            }
            catch { return false; }
        }

        void AbrirBaul(Ped me)
        {
            baul = true; baulLado = 0; baulSel = 0; baulCierre = 0;
            try { me.Task.ClearAllImmediately(); } catch { }
            try { me.Task.TurnTo(activo.Position); } catch { }
            Anim(me, "amb@car_stash", "open_boot", false);
            try { Function.Call("OPEN_CAR_DOOR", activo, 5); } catch { }
            try { Function.Call("SET_PLAYER_CONTROL", PlayerId(), false); } catch { }
        }

        void CerrarBaul(Ped me)
        {
            baul = false;
            try { Function.Call("SET_PLAYER_CONTROL", PlayerId(), true); } catch { }
            Anim(me, "car_boot", "close_boot", false);
            try { if (activo != null && Game.Exists(activo)) Function.Call("SHUT_CAR_DOOR", activo, 5); } catch { }
            Guardar();
        }

        void TickBaul(Ped me, int now)
        {
            bool t = teclaE || Pressed(VK_BACK), up = Pressed(VK_UP), down = Pressed(VK_DOWN), left = Pressed(VK_LEFT), right = Pressed(VK_RIGHT), enter = Pressed(VK_ENTER);
            bool valido = false;
            try { valido = activo != null && Game.Exists(activo) && activoIdx >= 0 && activoIdx < autos.Count && me.isAliveAndWell && !me.isInVehicle() && Trasera(activo).DistanceTo(me.Position) < 3.5f; } catch { }
            if (t || !valido) { CerrarBaul(me); return; }
            Auto a = autos[activoIdx];
            List<int[]> encima = ArmasEncima(me);
            if (left) { baulLado = 0; baulSel = 0; }
            if (right) { baulLado = 1; baulSel = 0; }
            int n = baulLado == 0 ? encima.Count : a.Baul.Count;
            if (n > 0)
            {
                if (up) baulSel = (baulSel + n - 1) % n;
                if (down) baulSel = (baulSel + 1) % n;
                if (baulSel >= n) baulSel = n - 1;
            }
            if (!enter || n == 0) return;
            if (baulLado == 0)
            {
                // guardar en el baul
                if (a.Baul.Count >= MaxBaul) { Aviso("El baul esta lleno (" + MaxBaul + " armas)"); return; }
                int[] w = encima[baulSel];
                try { Function.Call("REMOVE_WEAPON_FROM_CHAR", me, w[0]); } catch { return; }
                a.Baul.Add(w);
                Aviso("Guardaste: " + NombreArma(w[0]));
            }
            else
            {
                // sacar del baul (si ya llevas un arma de ese tipo, se cambian)
                int[] w = a.Baul[baulSel];
                int slotNuevo = -1;
                try { Pointer sp = new Pointer(typeof(int)); Function.Call("GET_WEAPONTYPE_SLOT", w[0], sp); slotNuevo = Convert.ToInt32(sp.Value); } catch { }
                if (slotNuevo > 0)
                {
                    try
                    {
                        Pointer ow = new Pointer(typeof(int)), a1 = new Pointer(typeof(int)), a2 = new Pointer(typeof(int));
                        Function.Call("GET_CHAR_WEAPON_IN_SLOT", me, slotNuevo, ow, a1, a2);
                        int vieja = Convert.ToInt32(ow.Value);
                        if (vieja > 0 && vieja != w[0])
                        {
                            Pointer m = new Pointer(typeof(int));
                            Function.Call("GET_AMMO_IN_CHAR_WEAPON", me, vieja, m);
                            Function.Call("REMOVE_WEAPON_FROM_CHAR", me, vieja);
                            a.Baul.Add(new int[] { vieja, Math.Max(1, Convert.ToInt32(m.Value)) });
                            Aviso("Cambiaste " + NombreArma(vieja) + " por " + NombreArma(w[0]));
                        }
                        else Aviso("Sacaste: " + NombreArma(w[0]));
                    }
                    catch { }
                }
                else Aviso("Sacaste: " + NombreArma(w[0]));
                try { Function.Call("GIVE_WEAPON_TO_CHAR", me, w[0], w[1], false); } catch { }
                a.Baul.Remove(w);
            }
            sucio = true;
        }

        // ================= dibujo (estilo GTA IV, ver clase IV) =================
        void DibujarBaul()
        {
            if (activoIdx < 0 || activoIdx >= autos.Count) return;
            Ped me = null;
            try { me = Player.Character; } catch { }
            if (me == null) return;
            List<int[]> encima = ArmasEncima(me);
            List<int[]> enBaul = autos[activoIdx].Baul;
            float w = 0.26f, y0 = 0.16f;
            for (int lado = 0; lado < 2; lado++)
            {
                List<int[]> l = lado == 0 ? encima : enBaul;
                float x = lado == 0 ? 0.03f : 0.03f + w + 0.01f;
                IV.Titulo(lado == 0 ? "Encima" : "Baul " + enBaul.Count + "/" + MaxBaul, x, y0, w);
                if (lado == baulLado) IV.Rect(x, y0 + 0.042f, w, 0.003f, IV.BlancoPuro, 255);
                float y = y0 + 0.045f;
                if (l.Count == 0) { IV.Item("(vacio)", null, x, y, w, false, true, 0f); y += IV.Fila; }
                for (int i = 0; i < l.Count; i++)
                {
                    IV.Item(NombreArma(l[i][0]), l[i][1].ToString(), x, y, w, lado == baulLado && i == baulSel, false, 0.2f);
                    y += IV.Fila;
                }
            }
            int filas = Math.Max(1, Math.Max(encima.Count, enBaul.Count));
            IV.Pie(autos[activoIdx].Nombre + "     Izq/Der: lado   Enter: " + (baulLado == 0 ? "guardar" : "sacar") + "   E: cerrar",
                   0.03f, y0 + 0.045f + filas * IV.Fila, w * 2 + 0.01f);
        }

        void Ayuda(string s) { IV.Ayuda(s, 0); }

        void DibujarNafta(Tanque t)
        {
            float pct = t.Cap > 0 ? t.L / t.Cap : 0f;
            IV.Barra("NAFTA", pct * 100f, 0.86f, 0.945f, 0.085f, IV.AmarilloClaro, t.L <= t.Res);
        }

        void DibujarCarga()
        {
            if (cargaT == null) return;
            float pct = cargaT.Cap > 0 ? cargaT.L / cargaT.Cap : 0f;
            string t1 = cargaBidon ? "Cargando el bidon" : "Cargando nafta   $" + (cargaPagado + (int)cargaDeuda);
            IV.Progreso(t1 + "   (E: cortar)", pct);
        }

        void DibujarMenu()
        {
            float x = 0.03f, y = 0.16f, w = 0.3f;
            IV.Titulo("Mis autos", x, y, w);
            y += 0.045f;
            int n = autos.Count;
            if (n == 0) { IV.Item("(no tenes autos guardados)", null, x, y, w, false, true, 0f); y += IV.Fila; }
            for (int i = 0; i < n; i++)
            {
                string nafta = autos[i].Nafta < 0 ? "lleno" : autos[i].Nafta.ToString("0", CultureInfo.InvariantCulture) + " L";
                IV.Item(autos[i].Nombre + (i == activoIdx ? "  *" : ""), nafta, x, y, w, i == sel, false, 0.235f);
                y += IV.Fila;
            }
            IV.Pie("Enter: pedirlo   Supr: borrar   L: cerrar   Bidones: " + bidones, x, y, w);
        }

        void DibujarAviso() { IV.Aviso(aviso, 1); }
    }
    // <estilo-iv> (copiado de tools/EstiloIV.cs con tools/estilo_sync.py; no editar aca)
    // Estilo visual comun de nuestros mods, con los colores de common/data/hudColor.dat de GTA IV.
    static class IV
    {
        // paleta del juego
        public static readonly int[] Blanco = { 225, 225, 225 }, BlancoPuro = { 255, 255, 255 }, Gris = { 140, 140, 140 },
            Atenuado = { 90, 90, 90 }, Celeste = { 74, 148, 160 }, CelesteClaro = { 151, 201, 206 }, CelesteOscuro = { 51, 105, 114 },
            Verde = { 87, 124, 88 }, Naranja = { 188, 130, 74 }, VerdeClaro = { 159, 201, 166 }, Rojo = { 153, 69, 69 }, RojoClaro = { 206, 155, 155 },
            Amarillo = { 206, 169, 13 }, AmarilloClaro = { 215, 197, 121 };

        public const float Fila = 0.034f;

        // rectangulo con esquina arriba-izquierda en (x, y)
        public static void Rect(float x, float y, float w, float h, int[] c, int a)
        {
            GTA.Native.Function.Call("DRAW_RECT", x + w / 2f, y + h / 2f, w, h, c[0], c[1], c[2], a);
        }

        // alin: 0 izquierda, 1 centro
        public static void Texto(string s, float x, float y, float esc, int[] c, int a, int alin)
        {
            if (string.IsNullOrEmpty(s)) return;
            GTA.Native.Function.Call("SET_TEXT_FONT", 0);
            GTA.Native.Function.Call("SET_TEXT_SCALE", esc, esc);
            GTA.Native.Function.Call("SET_TEXT_COLOUR", c[0], c[1], c[2], a);
            GTA.Native.Function.Call("SET_TEXT_DROPSHADOW", 1, 0, 0, 0, 200);
            if (alin == 1) GTA.Native.Function.Call("SET_TEXT_CENTRE", 1);
            GTA.Native.Function.Call("DISPLAY_TEXT_WITH_LITERAL_STRING", x, y, "STRING", s);
        }

        // titulo de panel: texto blanco en mayusculas con una linea celeste abajo
        public static void Titulo(string s, float x, float y, float w)
        {
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
            Rect(x, y, w, Fila, new int[] { 0, 0, 0 }, 165);
            if (sel) Rect(x, y, w, Fila, Celeste, 210);
            int[] c = apagado ? Atenuado : (sel ? BlancoPuro : Blanco);
            Texto(izq, x + 0.008f, y + 0.006f, 0.29f, c, 255, 0);
            if (der != null) Texto(der, x + colDer, y + 0.006f, 0.29f, sel ? BlancoPuro : (apagado ? Atenuado : cDer), 255, 0);
        }

        // pie con las teclas, debajo del menu
        public static void Pie(string s, float x, float y, float w)
        {
            Rect(x, y, w, 0.003f, Celeste, 255);
            Rect(x, y + 0.003f, w, 0.03f, new int[] { 0, 0, 0 }, 185);
            Texto(s, x + 0.008f, y + 0.009f, 0.235f, Gris, 255, 0);
        }

        // caja de ayuda como la del juego (arriba a la izquierda)
        // fila: 0 MisAutos, 1 VidaNiko (para que no se pisen)
        public static void Ayuda(string s, int fila)
        {
            float w = Math.Max(0.16f, 0.0058f * s.Length + 0.02f), y = 0.055f + fila * 0.05f;
            Rect(0.03f, y, w, 0.045f, new int[] { 0, 0, 0 }, 185);
            Texto(s, 0.04f, y + 0.012f, 0.28f, BlancoPuro, 255, 0);
        }

        // aviso tipo subtitulo del juego (abajo al centro, sin caja)
        public static void Aviso(string s, int fila)
        {
            Texto(s, 0.5f, 0.875f + fila * 0.032f, 0.33f, BlancoPuro, 255, 1);
        }

        // barra fina (necesidades, nafta); alerta = parpadea en rojo como la vida del juego
        public static void Barra(string nombre, float valor, float x, float y, float w, int[] c, bool alerta)
        {
            float h = 0.009f;
            int a = 230;
            if (alerta && (Environment.TickCount / 400) % 2 == 0) a = 110;
            Rect(x - 0.002f, y - 0.003f, w + 0.004f, h + 0.006f, new int[] { 0, 0, 0 }, 170);
            Rect(x, y, w, h, alerta ? RojoOscuroDe(c) : CelesteOscuroDe(c), 190);
            float fw = w * Math.Max(0f, Math.Min(1f, valor / 100f));
            if (fw > 0.001f) Rect(x, y, fw, h, alerta ? Rojo : c, a);
            Texto(nombre, x - 0.004f - 0.0058f * nombre.Length, y - 0.007f, 0.21f, alerta ? RojoClaro : Blanco, 255, 0);
        }

        static int[] CelesteOscuroDe(int[] c) { return new int[] { c[0] / 3, c[1] / 3, c[2] / 3 }; }
        static int[] RojoOscuroDe(int[] c) { return new int[] { 60, 25, 25 }; }

        // barra de progreso al centro (cargar nafta, curarse, comer)
        public static void Progreso(string s, float valor)
        {
            Rect(0.4f, 0.80f, 0.2f, 0.05f, new int[] { 0, 0, 0 }, 170);
            Texto(s, 0.5f, 0.805f, 0.27f, BlancoPuro, 255, 1);
            Rect(0.41f, 0.833f, 0.18f, 0.008f, CelesteOscuro, 200);
            float fw = 0.18f * Math.Max(0f, Math.Min(1f, valor));
            if (fw > 0.001f) Rect(0.41f, 0.833f, fw, 0.008f, Celeste, 255);
        }
    }
    // </estilo-iv>
}
