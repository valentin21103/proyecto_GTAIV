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
