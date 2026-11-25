    // <estilo-iv> (copiado de tools/EstiloIV.cs con tools/estilo_sync.py; no editar aca)
    // Estilo visual comun de nuestros mods, con los colores de common/data/hudColor.dat de GTA IV.
    // - Ayudas: caja de ayuda REAL del juego (textos en update/common/text/spanishVN.gxt, ver tools/gxt_vn.py).
    // - Avisos: subtitulos REALES del juego (no pisan el texto de una mision).
    // - Nada se dibuja con el menu de pausa / mapa, el celular, la computadora, fundidos o el HUD apagado.
    // - Barras: una sola columna arriba del radar (abajo a la derecha el juego pone calle y auto).
    static class IV
    {
        // paleta del juego
        public static readonly int[] Blanco = { 225, 225, 225 }, BlancoPuro = { 255, 255, 255 }, Gris = { 140, 140, 140 },
            Atenuado = { 90, 90, 90 }, Celeste = { 74, 148, 160 }, CelesteClaro = { 151, 201, 206 }, CelesteOscuro = { 51, 105, 114 },
            Verde = { 87, 124, 88 }, Naranja = { 188, 130, 74 }, VerdeClaro = { 159, 201, 166 }, Rojo = { 153, 69, 69 }, RojoClaro = { 206, 155, 155 },
            Amarillo = { 206, 169, 13 }, AmarilloClaro = { 215, 197, 121 };

        public const float Fila = 0.034f;

        // ---- columna de barras (arriba del radar). Lugar 0 = nafta (MisAutos), 1.. = VidaNiko ----
        public const float BarraX = 0.095f, BarraW = 0.085f, BarraY0 = 0.705f, BarraPaso = 0.021f;
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

        // alin: 0 izquierda, 1 centro
        public static void Texto(string s, float x, float y, float esc, int[] c, int a, int alin)
        {
            if (string.IsNullOrEmpty(s) || !Visible()) return;
            GTA.Native.Function.Call("SET_TEXT_FONT", 0);
            GTA.Native.Function.Call("SET_TEXT_SCALE", esc, esc);
            GTA.Native.Function.Call("SET_TEXT_COLOUR", c[0], c[1], c[2], a);
            GTA.Native.Function.Call("SET_TEXT_DROPSHADOW", 1, 0, 0, 0, 200);
            if (alin == 1) GTA.Native.Function.Call("SET_TEXT_CENTRE", 1);
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
            if (der != null) Texto(der, x + colDer, y + 0.006f, 0.29f, sel ? BlancoPuro : (apagado ? Atenuado : cDer), 255, 0);
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
            try { GTA.Game.DisplayText(s, 4500); avisoTxt = s; avisoT = now; } catch { }
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
            Texto(nombre, x - 0.004f - 0.0058f * nombre.Length, y - 0.007f, 0.21f, alerta ? RojoClaro : Blanco, 255, 0);
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
