// MisAutos.cs - garage personal estilo GTA V + nafta (parte de "Vida de Niko")
//
// AUTOS
//   Manejando:  L = guardar este auto en "Mis autos" (y queda como tu auto activo)
//   A pie:      L = abrir la lista   (flechas elegir, Enter pedirlo, Supr borrarlo, L cerrar)
//   El auto activo no desaparece, tiene icono en el mapa y se acuerda de su nafta.
//
// NAFTA
//   Todos los autos gastan nafta al andar (no durante misiones).
//   En una estacion (icono en el mapa): bajate al lado del auto y apreta G para cargar
//   (G de nuevo para cortar). H compra un bidon. Lejos de una estacion, G usa un bidon.
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
        const int VK_G = 0x47, VK_H = 0x48, VK_W = 0x57;
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
        Blip blip;
        Vector3 blipPos;
        string nombreActivo;
        int proximoBlip, proximoGuardado, ultimoTick;
        bool sucio;

        bool menu;
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
                o.Add("# MisAutos: modelo|color1|color2|color3|color4|accesorios|nombre|nafta(litros)");
                foreach (Auto a in autos)
                    o.Add(a.Modelo + "|" + a.C1 + "|" + a.C2 + "|" + a.C3 + "|" + a.C4 + "|" + a.Extras + "|" + a.Nombre.Replace("|", " ") + "|" + a.Nafta.ToString("0.0", CultureInfo.InvariantCulture));
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
            if (idx >= 0) { autos[idx] = a; Aviso("Actualizado en Mis autos: " + a.Nombre); }
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

            if (naftaActiva) TickNafta(me, now, dt);

            // menu de autos
            bool l = Pressed(VK_L);
            if (!menu)
            {
                if (l && me.isAliveAndWell && carga == 0)
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
                    if (!t.AvisoVacio) { t.AvisoVacio = true; Aviso(bidones > 0 ? "Sin nafta. Bajate y apreta G para usar un bidon" : "Sin nafta. Empuja o busca otro auto"); }
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
            bool g = Pressed(VK_G), h = Pressed(VK_H);

            if (carga == 0)
            {
                Estacion est = EstacionCerca(me.Position);
                Vehicle v = AutoCerca(me);
                Tanque t = v != null ? TanqueDe(v) : null;
                if (est != null)
                {
                    if (h)
                    {
                        if (bidones >= maxBidones) Aviso("Ya tenes " + bidones + " bidones (maximo " + maxBidones + ")");
                        else if (Player.Money < (int)precioBidon) Aviso("No te alcanza para un bidon ($" + (int)precioBidon + ")");
                        else { Player.Money = Player.Money - (int)precioBidon; bidones++; sucio = true; Aviso("Compraste un bidon (" + bidones + "/" + maxBidones + ")"); }
                    }
                    string ayuda = t != null
                        ? "G: cargar nafta  ($" + est.Precio.ToString("0.00", CultureInfo.InvariantCulture) + "/L, tanque " + (int)(t.L * 100f / t.Cap) + "%)    H: comprar bidon ($" + (int)precioBidon + ")"
                        : "Acercate a tu auto para cargar nafta    H: comprar bidon ($" + (int)precioBidon + ")";
                    Ayuda(ayuda);
                    if (g && t != null)
                    {
                        if (t.L >= t.Cap - 0.2f) Aviso("El tanque ya esta lleno");
                        else EmpezarCarga(me, t, est, false, now);
                    }
                }
                else if (t != null && bidones > 0 && t.L < t.Cap - 0.5f)
                {
                    Ayuda("G: usar bidon (+" + (int)litrosBidon + " L)   bidones: " + bidones + "   tanque " + (int)(t.L * 100f / t.Cap) + "%");
                    if (g) EmpezarCarga(me, t, null, true, now);
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
                if (g) { TerminarCarga(me, null); return; }
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
            Function.Call("DRAW_RECT", 0.5f, 0.905f, 0.62f, 0.04f, 0, 0, 0, 150);
            Texto(s, 0.5f, 0.89f, 0.27f, 255, 255, 255, true);
        }

        void DibujarNafta(Tanque t)
        {
            float pct = t.Cap > 0 ? t.L / t.Cap : 0f;
            float x = 0.885f, y = 0.955f, w = 0.09f, hh = 0.012f;
            Function.Call("DRAW_RECT", x, y, w + 0.006f, hh + 0.008f, 0, 0, 0, 150);
            Function.Call("DRAW_RECT", x, y, w, hh, 60, 60, 60, 200);
            int r = 230, g = 200, b = 60;
            if (t.L <= t.Res) { r = 230; g = 60; b = 50; }
            float fw = w * pct;
            if (fw > 0.001f) Function.Call("DRAW_RECT", x - w / 2f + fw / 2f, y, fw, hh, r, g, b, 230);
            Texto("NAFTA " + (int)(pct * 100f) + "%", x - w / 2f, y - 0.034f, 0.24f, 230, 230, 230, false);
        }

        void DibujarCarga()
        {
            if (cargaT == null) return;
            float pct = cargaT.Cap > 0 ? cargaT.L / cargaT.Cap : 0f;
            Function.Call("DRAW_RECT", 0.5f, 0.86f, 0.3f, 0.075f, 0, 0, 0, 160);
            string t1 = cargaBidon ? "Cargando bidon..." : "Cargando nafta...  $" + (cargaPagado + (int)cargaDeuda);
            Texto(t1, 0.5f, 0.828f, 0.3f, 255, 255, 255, true);
            Function.Call("DRAW_RECT", 0.5f, 0.872f, 0.26f, 0.014f, 60, 60, 60, 220);
            float fw = 0.26f * pct;
            if (fw > 0.001f) Function.Call("DRAW_RECT", 0.5f - 0.13f + fw / 2f, 0.872f, fw, 0.014f, 230, 200, 60, 230);
            Texto((int)(pct * 100f) + "%   (G: cortar)", 0.5f, 0.882f, 0.24f, 210, 210, 210, true);
        }

        void DibujarMenu()
        {
            float x = 0.035f, y = 0.18f, step = 0.032f;
            int n = autos.Count;
            Function.Call("DRAW_RECT", 0.17f, y + (n + 3) * step * 0.5f - 0.01f, 0.29f, (n + 3) * step + 0.02f, 0, 0, 0, 170);
            Texto("MIS AUTOS", x, y - 0.005f, 0.42f, 120, 200, 255, false);
            for (int i = 0; i < n; i++)
            {
                float yy = y + (i + 1) * step;
                bool s = i == sel;
                if (s) Function.Call("DRAW_RECT", 0.17f, yy + 0.014f, 0.285f, step * 0.95f, 255, 255, 255, 40);
                string nafta = autos[i].Nafta < 0 ? "lleno" : autos[i].Nafta.ToString("0", CultureInfo.InvariantCulture) + " L";
                Texto((s ? "> " : "  ") + autos[i].Nombre + (i == activoIdx ? "  (activo)" : ""), x, yy, 0.30f, s ? 255 : 190, s ? 255 : 190, s ? 255 : 190, false);
                Texto(nafta, x + 0.2f, yy, 0.28f, 200, 190, 120, false);
            }
            float fy = y + (n + 1) * step + 0.005f;
            Texto("Enter: pedirlo   Supr: borrar   L: cerrar   Bidones: " + bidones, x, fy, 0.24f, 190, 190, 190, false);
        }

        void DibujarAviso()
        {
            Function.Call("DRAW_RECT", 0.5f, 0.12f, 0.5f, 0.045f, 0, 0, 0, 160);
            Texto(aviso, 0.5f, 0.103f, 0.30f, 255, 255, 255, true);
        }
    }
}
