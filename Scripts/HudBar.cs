using Godot;

namespace TNBCY
{
    // Top information bar: speed, speed limit, next stop and clock, drawn as glass chips.
    public partial class HudBar : Control
    {
        public float SpeedKmh, LimitKmh;
        public string NextName = "", NextDist = "", Clock = "";
        static StyleBoxFlat chipStyle;

        float U { get { return Mathf.Clamp(Mathf.Min(Size.X / 1000f, Size.Y / 600f), .72f, 1.45f); } }

        public override void _Ready() { MouseFilter = MouseFilterEnum.Ignore; }
        public override void _Process(double delta) { if (Visible) QueueRedraw(); }

        void Txt(string s, Vector2 pos, float size, Color c, bool bold = true, HorizontalAlignment al = HorizontalAlignment.Left, float width = -1f)
        {
            DrawString(bold ? UiFont.Bold : UiFont.Med, pos, s, al, width, Mathf.RoundToInt(size), c);
        }

        void Chip(Rect2 r)
        {
            if (chipStyle == null) chipStyle = Ui.Panel(new Color(.05f, .09f, .15f, .80f), new Color(.93f, .70f, .25f, .40f), 14, 1, 0);
            DrawStyleBox(chipStyle, r);
        }

        static string Trunc(string s, int max) { return s != null && s.Length > max ? s.Substring(0, max - 1) + "." : (s ?? ""); }

        public override void _Draw()
        {
            float u = U, w = Size.X, y = 10f * u, h = 58f * u, gap = 8f * u, x = 14f * u;
            float wSpeed = 214f * u, wLimit = 172f * u, wNext = 306f * u, wTime = 178f * u;
            float total = wSpeed + wLimit + wNext + wTime + gap * 3f, avail = w - x - 150f * u;
            float k = Mathf.Min(1f, avail / total);
            wSpeed *= k; wLimit *= k; wNext *= k; wTime *= k; gap *= k;
            float fs = 15f * u * Mathf.Max(.82f, k);

            // ---- speed
            var r = new Rect2(x, y, wSpeed, h); Chip(r);
            bool over = SpeedKmh > LimitKmh + 3f;
            var c = new Vector2(x + 34f * u, y + h * .56f); float rad = 21f * u;
            DrawArc(c, rad, Mathf.DegToRad(135f), Mathf.DegToRad(405f), 40, new Color(1f, 1f, 1f, .18f), 4f * u, true);
            float frac = Mathf.Clamp(SpeedKmh / 140f, 0f, 1f);
            Color arc = over ? new Color(1f, .35f, .30f) : (frac > .75f ? new Color(1f, .75f, .2f) : new Color(.35f, .9f, .5f));
            if (frac > .01f) DrawArc(c, rad, Mathf.DegToRad(135f), Mathf.DegToRad(135f + 270f * frac), 40, arc, 4f * u, true);
            float na = Mathf.DegToRad(135f + 270f * frac);
            DrawLine(c, c + new Vector2(Mathf.Cos(na), Mathf.Sin(na)) * (rad - 3f * u), Colors.White, 2.2f * u, true);
            DrawCircle(c, 3.2f * u, Ui.Gold);
            Txt(Mathf.RoundToInt(SpeedKmh).ToString(), new Vector2(x + 64f * u, y + h * .62f), 31f * u * Mathf.Max(.85f, k), over ? new Color(1f, .45f, .4f) : Colors.White);
            Txt("km/h", new Vector2(x + 64f * u, y + h * .88f), 12f * u, Ui.Dim, false);
            x += wSpeed + gap;

            // ---- limit sign
            r = new Rect2(x, y, wLimit, h); Chip(r);
            var sc = new Vector2(x + 33f * u, y + h * .5f);
            DrawCircle(sc, 22f * u, new Color(.85f, .10f, .10f)); DrawCircle(sc, 16.5f * u, Colors.White);
            Txt(Mathf.RoundToInt(LimitKmh).ToString(), new Vector2(sc.X - 22f * u, sc.Y + 6f * u), 16f * u, new Color(.08f, .08f, .08f), true, HorizontalAlignment.Center, 44f * u);
            Txt("SPEED LIMIT", new Vector2(x + 64f * u, y + h * .42f), 10.5f * u, Ui.Dim, false);
            Txt(Mathf.RoundToInt(LimitKmh) + " km/h", new Vector2(x + 64f * u, y + h * .78f), fs, Colors.White);
            x += wLimit + gap;

            // ---- next stop
            r = new Rect2(x, y, wNext, h); Chip(r);
            var hc = new Vector2(x + 30f * u, y + h * .55f);
            DrawColoredPolygon(new[] { hc + new Vector2(-18f, -4f) * u, hc + new Vector2(0f, -17f) * u, hc + new Vector2(18f, -4f) * u }, Ui.Gold);
            DrawRect(new Rect2(hc.X - 14f * u, hc.Y - 4f * u, 28f * u, 18f * u), new Color(.96f, .93f, .86f));
            DrawRect(new Rect2(hc.X - 4f * u, hc.Y + 3f * u, 8f * u, 11f * u), new Color(.35f, .22f, .15f));
            Txt("NEXT STOP", new Vector2(x + 60f * u, y + h * .36f), 10.5f * u, Ui.Dim, false);
            Txt(Trunc(NextName, 22), new Vector2(x + 60f * u, y + h * .65f), fs, Ui.GoldHi);
            Txt(NextDist, new Vector2(x + 60f * u, y + h * .92f), 12.5f * u, Colors.White, false);
            x += wNext + gap;

            // ---- clock
            r = new Rect2(x, y, wTime, h); Chip(r);
            var cc = new Vector2(x + 30f * u, y + h * .5f);
            DrawArc(cc, 15f * u, 0f, Mathf.Tau, 28, Ui.Gold, 2.4f * u, true);
            DrawLine(cc, cc + new Vector2(0f, -10f * u), Colors.White, 2f * u, true); DrawLine(cc, cc + new Vector2(7f * u, 3f * u), Colors.White, 2f * u, true);
            Txt("TIME", new Vector2(x + 58f * u, y + h * .40f), 10.5f * u, Ui.Dim, false);
            Txt(Clock, new Vector2(x + 58f * u, y + h * .80f), fs * 1.12f, Colors.White);
        }
    }
}
