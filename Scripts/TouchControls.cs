using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // Multi-touch driver controls drawn by hand: power lever, brake lever and three buttons.
    // Each finger is tracked separately, so you can move both levers at once.
    public partial class TouchControls : Control
    {
        public float Throttle, Brake;      // 0..1 lever positions
        public bool Emergency, Cab, Enabled = true;
        public System.Action OnHorn, OnSwitch;
        public string SwitchLabel = "Switch";
        readonly Dictionary<int, int> owner = new Dictionary<int, int>();
        readonly float[] flash = new float[4];
        const float KnobR = 34f;

        Rect2 Track(bool left) { float h = Size.Y; float x = left ? 100f : Size.X - 100f; return new Rect2(x - 9f, h * .30f, 18f, h * .50f); }
        Rect2 Zone(bool left) => Track(left).GrowIndividual(75f, 40f, 75f, 50f);
        Rect2 Btn(int i) { float w = 165f, g = 12f, h = 64f; float x0 = Size.X / 2f - (4f * w + 3f * g) / 2f; return new Rect2(x0 + i * (w + g), Size.Y - h - 18f, w, h); }

        public override void _Ready() { MouseFilter = MouseFilterEnum.Ignore; }
        public override void _Process(double delta)
        {
            for (int i = 0; i < 4; i++) flash[i] = Mathf.Max(0f, flash[i] - (float)delta);
            QueueRedraw();
        }

        public override void _Input(InputEvent e)
        {
            if (!Enabled) { owner.Clear(); return; }
            if (e is InputEventScreenTouch t)
            {
                if (t.Pressed)
                {
                    for (int i = 0; i < 4; i++) if (Btn(i).HasPoint(t.Position)) { Press(i); return; }
                    if (Zone(true).HasPoint(t.Position)) { owner[t.Index] = 0; SetLever(0, t.Position.Y); }
                    else if (Zone(false).HasPoint(t.Position)) { owner[t.Index] = 1; SetLever(1, t.Position.Y); }
                }
                else owner.Remove(t.Index);
            }
            else if (e is InputEventScreenDrag d)
            {
                if (owner.TryGetValue(d.Index, out int z)) SetLever(z, d.Position.Y);
            }
        }

        void SetLever(int z, float y)
        {
            var tr = Track(z == 0);
            float v = Mathf.Clamp((tr.End.Y - y) / tr.Size.Y, 0f, 1f);
            if (v < .03f) v = 0f; else if (v > .97f) v = 1f;
            if (z == 0) Throttle = v; else Brake = v;
        }
        void Press(int i)
        {
            if (i == 0) OnHorn?.Invoke(); else if (i == 1) Emergency = !Emergency; else if (i == 2) OnSwitch?.Invoke(); else Cab = !Cab;
            flash[i] = .15f; Input.VibrateHandheld(25);
        }

        void Txt(string s, Vector2 pos, float width, int size, Color c)
        {
            var f = ThemeDB.FallbackFont;
            DrawStringOutline(f, pos, s, HorizontalAlignment.Center, width, size, 8, new Color(0, 0, 0, .8f));
            DrawString(f, pos, s, HorizontalAlignment.Center, width, size, c);
        }

        void Lever(bool left, float value, string label, Color color)
        {
            var tr = Track(left); float cx = tr.GetCenter().X; var slot = new Color(.06f, .09f, .12f, .92f);
            DrawRect(tr, slot); DrawCircle(new Vector2(cx, tr.Position.Y), 9f, slot); DrawCircle(new Vector2(cx, tr.End.Y), 9f, slot);
            for (int k = 0; k <= 10; k++)
            {
                float y = tr.End.Y - tr.Size.Y * k / 10f; float len = k % 5 == 0 ? 34f : 24f;
                DrawLine(new Vector2(cx + 16f, y), new Vector2(cx + len, y), new Color(1, 1, 1, .55f), 2f);
            }
            float ky = tr.End.Y - tr.Size.Y * value; var kp = new Vector2(cx, ky);
            DrawRect(new Rect2(cx - 5f, ky, 10f, tr.End.Y - ky), color);
            DrawCircle(kp + new Vector2(4f, 8f), KnobR, new Color(0, 0, 0, .35f));
            DrawCircle(kp, KnobR, color.Darkened(.5f));
            DrawCircle(kp - new Vector2(2f, 3f), KnobR - 5f, color);
            DrawCircle(kp - new Vector2(8f, 11f), KnobR - 19f, color.Lightened(.45f));
            DrawCircle(kp - new Vector2(11f, 15f), 6f, new Color(1, 1, 1, .85f));
            Txt(label, new Vector2(cx - 80f, tr.Position.Y - 44f), 160f, 26, Colors.White);
            Txt(Mathf.RoundToInt(value * 100f) + "%", new Vector2(cx - 80f, tr.Position.Y - 14f), 160f, 24, color.Lightened(.35f));
        }

        public override void _Draw()
        {
            Lever(true, Throttle, "Power", new Color(.94f, .63f, .19f));
            Lever(false, Brake, "Brake", new Color(.9f, .33f, .24f));
            string[] names = { "Horn", Emergency ? "Release" : "Emergency", SwitchLabel, Cab ? "Chase" : "Cab" };
            Color[] cols = { new Color(.24f, .36f, .44f), Emergency ? new Color(.9f, .2f, .15f) : new Color(.66f, .23f, .17f), new Color(.7f, .5f, .12f), new Color(.24f, .36f, .44f) };
            for (int i = 0; i < 4; i++)
            {
                var r = Btn(i); var c = flash[i] > 0 ? cols[i].Lightened(.3f) : cols[i]; float push = flash[i] > 0 ? 3f : 0f;
                DrawRect(new Rect2(r.Position + new Vector2(0, 6f - push), r.Size), c.Darkened(.55f));
                DrawRect(new Rect2(r.Position + new Vector2(0, push), r.Size), c);
                DrawRect(new Rect2(r.Position + new Vector2(0, push), new Vector2(r.Size.X, 4f)), c.Lightened(.35f));
                Txt(names[i], new Vector2(r.Position.X, r.Position.Y + r.Size.Y / 2f + 9f + push), r.Size.X, 21, Colors.White);
            }
        }
    }
}
