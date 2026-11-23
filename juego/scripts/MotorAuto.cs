// MotorAuto.cs - apagar y prender el auto con una tecla
// Manejando: I = apagar motor y luces / prenderlos de nuevo.
// Con el motor apagado el auto no arranca solo al acelerar: hay que volver a apretar I.

using System;
using System.Runtime.InteropServices;
using GTA;
using GTA.Native;

namespace MotorAutoMod
{
    public class MotorAuto : Script
    {
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);

        const int VK_TECLA = 0x49;   // I (de ignicion)

        Vehicle apagado;
        bool prev;
        string aviso;
        int avisoHasta;

        public MotorAuto()
        {
            this.Interval = 0;
            this.Tick += new EventHandler(OnTick);
        }

        void OnTick(object sender, EventArgs e)
        {
            Ped me;
            try { me = Player.Character; } catch { return; }
            if (!Game.Exists(me)) return;

            bool down = (GetAsyncKeyState(VK_TECLA) & 0x8000) != 0;
            bool press = down && !prev;
            prev = down;

            Vehicle v = null;
            bool conductor = false;
            try
            {
                if (me.isInVehicle())
                {
                    v = me.CurrentVehicle;
                    Ped drv = v.GetPedOnSeat(VehicleSeat.Driver);
                    conductor = drv != null && drv.Equals(me);
                }
            }
            catch { v = null; }

            if (apagado != null)
            {
                bool sigue = false;
                try { sigue = Game.Exists(apagado) && v != null && v.Equals(apagado) && conductor; } catch { }
                if (!sigue) apagado = null;   // te bajaste: el auto queda apagado, deja de forzarse
            }

            if (press && v != null && conductor)
            {
                if (apagado == null)
                {
                    apagado = v;
                    try { Function.Call("FORCE_CAR_LIGHTS", v, 1); } catch { }
                    Aviso("Motor apagado  (I para prender)");
                }
                else
                {
                    apagado = null;
                    try { Function.Call("SET_CAR_ENGINE_ON", v, true, false); } catch { }
                    try { Function.Call("FORCE_CAR_LIGHTS", v, 0); } catch { }
                    Aviso("Motor encendido");
                }
            }

            if (apagado != null)
            {
                try { Function.Call("SET_CAR_ENGINE_ON", apagado, false, true); } catch { }
            }

            if (Game.GameTime < avisoHasta) Dibujar();
        }

        void Aviso(string s) { aviso = s; avisoHasta = Game.GameTime + 2500; }

        void Dibujar()
        {
            Function.Call("SET_TEXT_FONT", 0);
            Function.Call("SET_TEXT_SCALE", 0.32f, 0.32f);
            Function.Call("SET_TEXT_COLOUR", 255, 255, 255, 255);
            Function.Call("SET_TEXT_DROPSHADOW", 1, 0, 0, 0, 255);
            Function.Call("SET_TEXT_CENTRE", 1);
            Function.Call("DISPLAY_TEXT_WITH_LITERAL_STRING", 0.5f, 0.86f, "STRING", aviso);
        }
    }
}
