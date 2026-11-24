using Godot;

namespace TNBCY
{
    // The game's UI fonts and the shared premium look (navy glass panels with a gold edge).
    public static class UiFont
    {
        static Font bold, med;
        public static Font Bold { get { if (bold == null) bold = TryLoad("res://fonts/Poppins-Bold.ttf"); return bold; } }
        public static Font Med { get { if (med == null) med = TryLoad("res://fonts/Poppins-Medium.ttf"); return med; } }
        static Font TryLoad(string path)
        {
            try { if (ResourceLoader.Exists(path)) { var f = GD.Load<Font>(path); if (f != null) return f; } } catch { }
            return ThemeDB.FallbackFont;
        }
    }

    public static class Ui
    {
        public static readonly Color Gold = new Color(.93f, .70f, .25f);
        public static readonly Color GoldHi = new Color(1f, .87f, .52f);
        public static readonly Color Navy = new Color(.04f, .09f, .15f);
        public static readonly Color Glass = new Color(.06f, .11f, .18f, .84f);
        public static readonly Color Ink = new Color(.94f, .96f, .98f);
        public static readonly Color Dim = new Color(.62f, .70f, .78f);

        public static StyleBoxFlat Panel(Color bg, Color border, int radius, int borderW = 0, int shadow = 0)
        {
            var s = new StyleBoxFlat { BgColor = bg, BorderColor = border };
            s.SetCornerRadiusAll(radius); s.SetBorderWidthAll(borderW);
            if (shadow > 0) { s.ShadowColor = new Color(0f, 0f, 0f, .45f); s.ShadowSize = shadow; s.ShadowOffset = new Vector2(0f, shadow * .35f); }
            return s;
        }

        public static void Place(Control c, float al, float at, float ar, float ab, float ol, float ot, float orr, float ob)
        { c.AnchorLeft = al; c.AnchorTop = at; c.AnchorRight = ar; c.AnchorBottom = ab; c.OffsetLeft = ol; c.OffsetTop = ot; c.OffsetRight = orr; c.OffsetBottom = ob; }

        public static Label MakeLabel(string text, int size, Color col, bool bold = false, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            var l = new Label { Text = text, HorizontalAlignment = align, MouseFilter = Control.MouseFilterEnum.Ignore };
            l.AddThemeFontOverride("font", bold ? UiFont.Bold : UiFont.Med);
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", col);
            return l;
        }

        // primary = solid gold (the main action), otherwise dark glass with a gold edge
        public static Button MakeButton(string text, int size, bool primary, Vector2 min)
        {
            var b = new Button { Text = text, CustomMinimumSize = min, FocusMode = Control.FocusModeEnum.None };
            b.AddThemeFontOverride("font", UiFont.Bold); b.AddThemeFontSizeOverride("font_size", size);
            Color edge = new Color(Gold.R, Gold.G, Gold.B, .85f);
            Color n = primary ? Gold : new Color(.07f, .13f, .21f, .86f);
            Color h = primary ? GoldHi : new Color(.11f, .20f, .31f, .92f);
            Color p = primary ? new Color(.80f, .58f, .18f) : new Color(.93f, .70f, .25f, .95f);
            b.AddThemeStyleboxOverride("normal", Panel(n, edge, 18, 2, 12));
            b.AddThemeStyleboxOverride("hover", Panel(h, GoldHi, 18, 2, 12));
            b.AddThemeStyleboxOverride("pressed", Panel(p, GoldHi, 18, 2, 4));
            b.AddThemeStyleboxOverride("disabled", Panel(new Color(.07f, .1f, .15f, .6f), new Color(.4f, .45f, .5f, .5f), 18, 2, 0));
            b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            Color txt = primary ? new Color(.10f, .08f, .04f) : Ink;
            b.AddThemeColorOverride("font_color", txt); b.AddThemeColorOverride("font_hover_color", txt);
            b.AddThemeColorOverride("font_pressed_color", new Color(.10f, .08f, .04f)); b.AddThemeColorOverride("font_disabled_color", new Color(.5f, .55f, .6f));
            return b;
        }

        public static ImageTexture Dot(int size, Color c)
        {
            var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
            float r = size * .5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + .5f - r) * (x + .5f - r) + (y + .5f - r) * (y + .5f - r));
                    float a = Mathf.Clamp(r - d, 0f, 1f);
                    img.SetPixel(x, y, new Color(c.R, c.G, c.B, c.A * a));
                }
            return ImageTexture.CreateFromImage(img);
        }
    }
}
