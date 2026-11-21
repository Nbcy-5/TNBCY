using Godot;

namespace TNBCY
{
    // The driver's instrument panel. It is drawn into a SubViewport and shown as a texture on the cab console,
    // so the dials and numbers stay sharp and move with the real train data.
    public partial class GaugeCluster : Control
    {
        public float Speed, Limit = 100f, Bp = 5f, Bc, Amps, Volts;
        public bool Panto, Breaker, Diesel, Over, Emergency;
        public string Reverser = "NEU";
        public int Notch;

        const float StartA = 135f * Mathf.Pi / 180f, Sweep = 270f * Mathf.Pi / 180f;
        static readonly Color Face = new Color(.04f, .05f, .06f), Rim = new Color(.55f, .56f, .6f), Txt = new Color(.9f, .92f, .95f);

        float Ang(float v, float max) { return StartA + Sweep * Mathf.Clamp(v / max, 0f, 1f); }

        void Dial(Vector2 c, float r, float max, float major, float minor, int fs, string title, string unit)
        {
            DrawCircle(c, r + 6f, Rim); DrawCircle(c, r, Face);
            var f = ThemeDB.FallbackFont;
            for (float v = 0f; v <= max + .001f; v += minor)
            {
                float a = Ang(v, max); bool mj = Mathf.Abs(v % major) < .001f || Mathf.Abs(v % major - major) < .001f;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                DrawLine(c + d * (r - (mj ? 20f : 11f)), c + d * (r - 4f), Txt, mj ? 3f : 1.5f, true);
                if (mj) DrawString(f, c + d * (r - 38f) + new Vector2(-fs * .6f, fs * .35f), Mathf.RoundToInt(v).ToString(), HorizontalAlignment.Center, fs * 1.2f, fs, Txt);
            }
            DrawString(f, c + new Vector2(-r, r * .62f), title, HorizontalAlignment.Center, r * 2f, fs, new Color(.6f, .75f, .9f));
            DrawString(f, c + new Vector2(-r, r * .84f), unit, HorizontalAlignment.Center, r * 2f, (int)(fs * .85f), new Color(.6f, .6f, .65f));
        }

        void Needle(Vector2 c, float r, float ang, Color col, float w)
        {
            Vector2 d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            DrawLine(c - d * 14f, c + d * (r - 10f), col, w, true); DrawCircle(c, 9f, col);
        }

        void Lamp(Vector2 p, string name, bool on, Color col)
        {
            DrawRect(new Rect2(p, new Vector2(120f, 34f)), on ? col : new Color(.1f, .11f, .12f));
            DrawRect(new Rect2(p, new Vector2(120f, 34f)), Rim, false, 2f);
            DrawString(ThemeDB.FallbackFont, p + new Vector2(0f, 24f), name, HorizontalAlignment.Center, 120f, 20, on ? Colors.Black : new Color(.5f, .52f, .55f));
        }

        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(.02f, .025f, .03f));
            var f = ThemeDB.FallbackFont;

            // speedometer 0..140 km/h with the line limit as a coloured ring
            Vector2 c1 = new Vector2(230f, 190f); float r1 = 165f;
            Dial(c1, r1, 140f, 20f, 10f, 26, "SPEED", "km/h");
            DrawArc(c1, r1 - 8f, Ang(0f, 140f), Ang(Limit, 140f), 40, new Color(.2f, .8f, .35f), 6f, true);
            if (Limit < 140f) DrawArc(c1, r1 - 8f, Ang(Limit, 140f), Ang(140f, 140f), 24, new Color(.9f, .25f, .2f), 6f, true);
            Needle(c1, r1, Ang(Speed, 140f), Over ? new Color(1f, .35f, .3f) : new Color(1f, .85f, .3f), 5f);
            DrawString(f, c1 + new Vector2(-70f, 78f), Mathf.RoundToInt(Speed).ToString("000"), HorizontalAlignment.Center, 140f, 44, Over ? new Color(1f, .4f, .35f) : Colors.White);

            // brake pipe (white) and brake cylinder (red) pressures, kg/cm2
            Vector2 c2 = new Vector2(560f, 190f); float r2 = 130f;
            Dial(c2, r2, 10f, 2f, 1f, 22, "BP / BC", "kg/cm2");
            Needle(c2, r2, Ang(Bc, 10f), new Color(1f, .3f, .25f), 4f);
            Needle(c2, r2, Ang(Bp, 10f), Colors.White, 4f);

            // traction current
            Vector2 c3 = new Vector2(850f, 190f); float r3 = 110f;
            Dial(c3, r3, 1000f, 200f, 100f, 18, Diesel ? "ENGINE" : "TRACTION", "A");
            Needle(c3, r3, Ang(Amps, 1000f), new Color(.5f, .85f, 1f), 4f);
            if (!Diesel) DrawString(f, c3 + new Vector2(-r3, -r3 * .35f), Volts > 1f ? "25 kV" : "-- kV", HorizontalAlignment.Center, r3 * 2f, 20, Volts > 1f ? new Color(.4f, 1f, .5f) : new Color(.7f, .4f, .35f));

            // lamps
            float y = 352f;
            Lamp(new Vector2(30f, y), Diesel ? "ENGINE" : "PANTO", Panto, new Color(.3f, .9f, .45f));
            Lamp(new Vector2(160f, y), "VCB", Breaker && !Diesel, new Color(.3f, .9f, .45f));
            Lamp(new Vector2(290f, y), "REV " + Reverser, Reverser == "FWD", new Color(.4f, .75f, 1f));
            Lamp(new Vector2(420f, y), "N" + Notch, Notch > 0, new Color(1f, .75f, .25f));
            Lamp(new Vector2(550f, y), "BRAKE", Bc > .3f, new Color(1f, .4f, .3f));
            Lamp(new Vector2(680f, y), "OVERSPD", Over, new Color(1f, .3f, .25f));
            Lamp(new Vector2(810f, y), "EMERG", Emergency, new Color(1f, .2f, .2f));
        }

        public override void _Process(double delta) { QueueRedraw(); }
    }
}
