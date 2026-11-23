// NikoLook.cs - menu simple para cambiar el aspecto de Niko (pelo, cabeza/barba, ropa)
// Pensado para Ultimate Customizable Niko: recorre todas las variaciones del modelo del jugador.
//
// F11            abrir / cerrar el menu (Niko queda quieto mientras esta abierto)
// Flecha arriba/abajo    elegir parte
// Flecha izq/der         cambiar modelo
// Shift + izq/der        cambiar color / textura
// Retroceso (Backspace)  volver esa parte a como estaba al abrir el menu
//
// La ropa queda puesta y se guarda con la partida como cualquier ropa del juego.

using System;
using System.Runtime.InteropServices;
using GTA;
using GTA.Native;

namespace NikoLookMenu
{
    public class NikoLook : Script
    {
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);

        const int VK_TOGGLE = 0x7A;   // F11
        const int VK_UP = 0x26, VK_DOWN = 0x28, VK_LEFT = 0x25, VK_RIGHT = 0x27;
        const int VK_SHIFT = 0x10, VK_BACK = 0x08;

        static readonly int[] Comps = { 7, 0, 10, 1, 6, 2, 5, 4, 3, 8, 9 };
        static readonly string[] Names = { "Pelo", "Cabeza / barba", "Cara", "Torso", "Chaqueta", "Piernas", "Pies", "Manos", "Accesorio", "Accesorio 2", "Dientes" };

        bool open;
        int row;
        bool[] prev = new bool[256];
        int[] origDraw = new int[11], origTex = new int[11];
        int repeatKey = -1, repeatAt;

        public NikoLook()
        {
            this.Interval = 0;
            this.Tick += new EventHandler(OnTick);
        }

        bool Pressed(int vk, int now)
        {
            bool down = (GetAsyncKeyState(vk) & 0x8000) != 0;
            bool was = prev[vk];
            prev[vk] = down;
            if (down && !was) { repeatKey = vk; repeatAt = now + 350; return true; }
            if (down && repeatKey == vk && now >= repeatAt) { repeatAt = now + 90; return true; }
            if (!down && repeatKey == vk) repeatKey = -1;
            return false;
        }

        Ped Me { get { return Player.Character; } }

        int PlayerId()
        {
            try { return Function.Call<int>("GET_PLAYER_ID"); } catch { return 0; }
        }

        int DrawCount(int c) { try { return Function.Call<int>("GET_NUMBER_OF_CHAR_DRAWABLE_VARIATIONS", Me, c); } catch { return 0; } }
        int TexCount(int c, int d) { try { return Function.Call<int>("GET_NUMBER_OF_CHAR_TEXTURE_VARIATIONS", Me, c, d); } catch { return 0; } }
        int CurDraw(int c) { try { return Function.Call<int>("GET_CHAR_DRAWABLE_VARIATION", Me, c); } catch { return 0; } }
        int CurTex(int c) { try { return Function.Call<int>("GET_CHAR_TEXTURE_VARIATION", Me, c); } catch { return 0; } }
        void SetVar(int c, int d, int t) { try { Function.Call("SET_CHAR_COMPONENT_VARIATION", Me, c, d, t); } catch { } }

        void SetControl(bool on)
        {
            try { Function.Call("SET_PLAYER_CONTROL", PlayerId(), on); } catch { }
        }

        void Open()
        {
            if (!Game.Exists(Me) || !Me.isAliveAndWell || Me.isInVehicle()) return;
            for (int i = 0; i < Comps.Length; i++) { origDraw[i] = CurDraw(Comps[i]); origTex[i] = CurTex(Comps[i]); }
            open = true;
            SetControl(false);
        }

        void Close()
        {
            open = false;
            SetControl(true);
        }

        void OnTick(object sender, EventArgs e)
        {
            int now = Game.GameTime;
            if (Pressed(VK_TOGGLE, now)) { if (open) Close(); else Open(); }
            if (!open) return;
            if (!Game.Exists(Me) || !Me.isAliveAndWell || Me.isInVehicle()) { Close(); return; }

            bool shift = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
            if (Pressed(VK_UP, now)) row = (row + Comps.Length - 1) % Comps.Length;
            if (Pressed(VK_DOWN, now)) row = (row + 1) % Comps.Length;

            int c = Comps[row];
            int dc = DrawCount(c);
            int d = CurDraw(c), t = CurTex(c);
            int dir = 0;
            if (Pressed(VK_LEFT, now)) dir = -1;
            if (Pressed(VK_RIGHT, now)) dir = 1;
            if (dir != 0 && dc > 0)
            {
                if (shift)
                {
                    int tc = TexCount(c, d);
                    if (tc > 1) SetVar(c, d, (t + dir + tc) % tc);
                }
                else if (dc > 1)
                {
                    int nd = (d + dir + dc) % dc;
                    SetVar(c, nd, 0);
                }
            }
            if (Pressed(VK_BACK, now)) SetVar(c, origDraw[row], origTex[row]);

            Draw();
        }

        void Text(string s, float x, float y, float scale, int r, int g, int b)
        {
            Function.Call("SET_TEXT_FONT", 0);
            Function.Call("SET_TEXT_SCALE", scale, scale);
            Function.Call("SET_TEXT_COLOUR", r, g, b, 255);
            Function.Call("SET_TEXT_DROPSHADOW", 1, 0, 0, 0, 255);
            Function.Call("DISPLAY_TEXT_WITH_LITERAL_STRING", x, y, "STRING", s);
        }

        void Draw()
        {
            float x = 0.035f, y = 0.18f, step = 0.032f;
            int n = Comps.Length;
            Function.Call("DRAW_RECT", 0.17f, y + (n + 3) * step * 0.5f - 0.01f, 0.29f, (n + 3) * step + 0.02f, 0, 0, 0, 170);
            Text("ASPECTO DE NIKO", x, y - 0.005f, 0.42f, 255, 200, 60);
            for (int i = 0; i < n; i++)
            {
                int c = Comps[i];
                int dc = DrawCount(c);
                int d = CurDraw(c);
                int tc = dc > 0 ? TexCount(c, d) : 0;
                string val = dc > 0 ? ("Modelo " + (d + 1) + "/" + dc + "   Color " + (CurTex(c) + 1) + "/" + Math.Max(tc, 1)) : "-";
                bool sel = i == row;
                float yy = y + (i + 1) * step;
                if (sel) Function.Call("DRAW_RECT", 0.17f, yy + 0.014f, 0.285f, step * 0.95f, 255, 255, 255, 40);
                Text((sel ? "> " : "  ") + Names[i], x, yy, 0.30f, sel ? 255 : 200, sel ? 255 : 200, sel ? 255 : 200);
                Text(val, x + 0.115f, yy, 0.30f, sel ? 255 : 170, sel ? 255 : 170, sel ? 255 : 170);
            }
            float fy = y + (n + 1) * step + 0.005f;
            Text("Arriba/abajo: parte   Izq/der: modelo   Shift+izq/der: color", x, fy, 0.24f, 190, 190, 190);
            Text("Retroceso: deshacer   F11: cerrar", x, fy + 0.022f, 0.24f, 190, 190, 190);
        }
    }
}
