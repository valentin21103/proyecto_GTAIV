// ArmasRapidas.cs - elegir armas facil para probarlas
// Shift+B = arma siguiente   Shift+N = arma anterior   Shift+M = darte el arma elegida
// (reemplaza al spawner de TACE, que recorria IDs a ciegas)

using System;
using System.Runtime.InteropServices;
using GTA;
using GTA.Native;

namespace ArmasRapidasMod
{
    public class ArmasRapidas : Script
    {
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);

        // id del juego, nombre, municion
        static readonly int[] Id =      { 7, 27, 43, 9, 29, 8, 12, 13, 32, 33, 10, 11, 26, 22, 31, 30, 14, 15, 34, 16, 17, 35, 21, 18, 4, 5, 28, 36, 1, 42, 23 };
        static readonly int[] Muni =    { 150, 170, 170, 200, 70, 64, 300, 300, 300, 300, 40, 50, 30, 40, 100, 60, 300, 300, 600, 40, 40, 40, 20, 10, 10, 10, 10, 10, 1, 1, 1 };
        static readonly string[] Nombre = {
            "Glock 19", "Beretta M9A3", "Pistola con silenciador", "FN Five-seveN", "Desert Eagle", "Revolver 8 Bull",
            "Micro-SMG", "MP5SD", "P90 (subfusil de asalto)", "Subfusil dorado",
            "Escopeta de combate", "Escopeta de corredera", "Escopeta recortada", "Escopeta de asalto", "Escopeta automatica (AA12)", "AA12 explosiva",
            "AK-47", "Carabina M4", "Ametralladora M249", "Rifle de francotirador", "Francotirador de combate", "Francotirador avanzado (DSR1)",
            "Lanzagranadas", "RPG", "Granadas", "Molotov", "Bombas caseras", "Bombas adhesivas",
            "Bate de beisbol", "Porra", "Taco de pool" };

        int sel;
        int mostrarHasta;
        string aviso;
        bool pb, pn, pm;

        public ArmasRapidas()
        {
            this.Interval = 0;
            this.Tick += new EventHandler(OnTick);
        }

        static bool Down(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }

        void OnTick(object sender, EventArgs e)
        {
            int now = Game.GameTime;
            bool shift = Down(0x10);
            bool b = shift && Down(0x42), n = shift && Down(0x4E), m = shift && Down(0x4D);

            if (b && !pb) { sel = (sel + 1) % Id.Length; Mostrar(now, null); }
            if (n && !pn) { sel = (sel + Id.Length - 1) % Id.Length; Mostrar(now, null); }
            if (m && !pm)
            {
                try
                {
                    Ped me = Player.Character;
                    if (Game.Exists(me) && me.isAliveAndWell)
                    {
                        Function.Call("GIVE_WEAPON_TO_CHAR", me, Id[sel], Muni[sel], false);
                        Function.Call("SET_CURRENT_CHAR_WEAPON", me, Id[sel], true);
                        Mostrar(now, "Te diste: " + Nombre[sel]);
                    }
                }
                catch { Mostrar(now, "No se pudo dar el arma"); }
            }
            pb = b; pn = n; pm = m;

            if (now < mostrarHasta) Dibujar();
        }

        void Mostrar(int now, string msg)
        {
            aviso = msg;
            mostrarHasta = now + 3500;
        }

        void Texto(string s, float x, float y, float scale, int r, int g, int bl)
        {
            Function.Call("SET_TEXT_FONT", 0);
            Function.Call("SET_TEXT_SCALE", scale, scale);
            Function.Call("SET_TEXT_COLOUR", r, g, bl, 255);
            Function.Call("SET_TEXT_DROPSHADOW", 1, 0, 0, 0, 255);
            Function.Call("SET_TEXT_CENTRE", 1);
            Function.Call("DISPLAY_TEXT_WITH_LITERAL_STRING", x, y, "STRING", s);
        }

        void Dibujar()
        {
            Function.Call("DRAW_RECT", 0.5f, 0.82f, 0.36f, 0.095f, 0, 0, 0, 160);
            Texto("(" + (sel + 1) + "/" + Id.Length + ")  " + Nombre[sel], 0.5f, 0.785f, 0.42f, 255, 255, 255);
            Texto(aviso != null ? aviso : "Shift+B / Shift+N: cambiar    Shift+M: agarrar", 0.5f, 0.825f, 0.28f, aviso != null ? 120 : 200, aviso != null ? 220 : 200, aviso != null ? 120 : 200);
        }
    }
}
