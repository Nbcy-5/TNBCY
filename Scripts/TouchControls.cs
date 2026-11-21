using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // Replacement touch HUD: large, separated controls with a compact centre panel.
    // Public members intentionally match the previous TouchControls API.
    public partial class TouchControls : Control
    {
        public bool Enabled = true, Emergency, Diesel, ShowCoachButtons, PantoUp, Breaker;
        public int Notch, BrakeStep = 1, Reverser, CamMode;
        public string SwitchLabel = "No switch";
        public System.Action OnHorn, OnSwitch, OnPrevCoach, OnNextCoach, OnPantoToggle, OnBreakerToggle;

        public static readonly string[] CamNames = { "Cab", "Chase", "Nose", "Side", "Top", "Free" };
        static readonly string[] BrakeNames = { "REL", "RUN", "MIN", "50%", "75%", "FULL", "EMG" };
        public float Throttle { get { return Notch / 8f; } }
        public float BrakeValue
        {
            get
            {
                if (BrakeStep < 2) return 0f;
                if (BrakeStep > 5) return 1f;
                return BrakeStep == 2 ? .2f : (BrakeStep == 3 ? .45f : (BrakeStep == 4 ? .7f : 1f));
            }
        }

        float u, topY, bottomY, brakeX, powerX;
        Rect2 brakeZone, powerZone, reverserZone, coachLeft, coachRight;
        readonly Rect2[] reverserButtons = new Rect2[3];
        readonly Rect2[] actionButtons = new Rect2[6];
        readonly Dictionary<int, int> pointerOwners = new Dictionary<int, int>();
        static readonly StyleBoxFlat style = new StyleBoxFlat();

        void LayoutControls()
        {
            float w = Size.X, h = Size.Y;
            u = Mathf.Clamp(Mathf.Min(w / 1000f, h / 600f), .72f, 1.45f);

            // Keep the driving levers at the lower corners, clear of the centre buttons.
            topY = h - 292f * u;
            bottomY = h - 56f * u;
            brakeX = 62f * u;
            powerX = w - 62f * u;
            brakeZone = new Rect2(8f * u, topY - 42f * u, 142f * u, 282f * u);
            powerZone = new Rect2(w - 150f * u, topY - 42f * u, 142f * u, 282f * u);

            float centre = w * .5f;
            float bw = 104f * u, bh = 46f * u, gap = 8f * u;
            float total = bw * 3f + gap * 2f;
            float x0 = centre - total * .5f;
            float row1 = h - 166f * u;
            float row2 = h - 110f * u;
            for (int i = 0; i < 6; i++)
            {
                int row = i / 3, col = i % 3;
                actionButtons[i] = new Rect2(x0 + col * (bw + gap), row == 0 ? row1 : row2, bw, bh);
            }

            float rw = 76f * u, rh = 40f * u;
            float rx = centre - (rw * 3f + gap * 2f) * .5f;
            float ry = h - 222f * u;
            reverserZone = new Rect2(rx - 5f * u, ry - 5f * u, rw * 3f + gap * 2f + 10f * u, rh + 10f * u);
            for (int i = 0; i < 3; i++)
                reverserButtons[i] = new Rect2(rx + i * (rw + gap), ry, rw, rh);

            // below the speed / info readout (it ends about y = 110), not on top of it
            coachLeft = new Rect2(centre - 190f * u, 122f * u, 88f * u, 42f * u);
            coachRight = new Rect2(centre + 102f * u, 122f * u, 88f * u, 42f * u);
        }

        float BrakeY(int step) { return bottomY - step * (bottomY - topY) / 6f; }
        float PowerY(int notch) { return bottomY - notch * (bottomY - topY) / 8f; }

        public bool Blocks(Vector2 p)
        {
            if (!Enabled) return false;
            LayoutControls();
            if (brakeZone.HasPoint(p) || powerZone.HasPoint(p) || reverserZone.HasPoint(p)) return true;
            foreach (Rect2 r in actionButtons) if (r.Grow(5f * u).HasPoint(p)) return true;
            return ShowCoachButtons && (coachLeft.HasPoint(p) || coachRight.HasPoint(p));
        }

        public void ForceEmergency()
        {
            Emergency = true;
            BrakeStep = 6;
            Notch = 0;
        }

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            SetProcess(true);
        }

        public override void _Process(double delta)
        {
            if (Enabled) QueueRedraw();
        }

        public override void _Input(InputEvent ev)
        {
            if (!Enabled) return;

            if (ev is InputEventScreenTouch t)
            {
                if (t.Pressed) PointerDown(t.Index, t.Position);
                else pointerOwners.Remove(t.Index);
            }
            else if (ev is InputEventScreenDrag d) PointerMove(d.Index, d.Position);
            else if (!DisplayServer.IsTouchscreenAvailable() && !Input.IsEmulatingTouchFromMouse())   // PC mouse; the touch emulation already covers it, so a click was counted twice
            {
                if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
                {
                    if (mb.Pressed) PointerDown(100, mb.Position);
                    else pointerOwners.Remove(100);
                }
                else if (ev is InputEventMouseMotion mm && pointerOwners.ContainsKey(100))
                    PointerMove(100, mm.Position);
            }
        }

        void PointerDown(int id, Vector2 p)
        {
            LayoutControls();
            pointerOwners[id] = 0;

            if (brakeZone.HasPoint(p))
            {
                pointerOwners[id] = 1;
                SetBrake(p.Y);
                return;
            }
            if (powerZone.HasPoint(p))
            {
                pointerOwners[id] = 2;
                SetPower(p.Y);
                return;
            }

            for (int i = 0; i < 3; i++)
                if (reverserButtons[i].Grow(4f * u).HasPoint(p))
                {
                    Reverser = i == 0 ? 1 : (i == 1 ? 0 : -1);
                    Buzz();
                    return;
                }

            for (int i = 0; i < 6; i++)
                if (actionButtons[i].Grow(5f * u).HasPoint(p))
                {
                    Press(i);
                    return;
                }

            if (ShowCoachButtons)
            {
                if (coachLeft.HasPoint(p)) { OnPrevCoach?.Invoke(); Buzz(); }
                else if (coachRight.HasPoint(p)) { OnNextCoach?.Invoke(); Buzz(); }
            }
        }

        void PointerMove(int id, Vector2 p)
        {
            int owner;
            if (!pointerOwners.TryGetValue(id, out owner)) return;
            if (owner == 1) SetBrake(p.Y);
            else if (owner == 2) SetPower(p.Y);
        }

        static void Buzz() { Input.VibrateHandheld(8); }

        void SetBrake(float y)
        {
            int step = Mathf.Clamp(Mathf.RoundToInt((bottomY - y) / (bottomY - topY) * 6f), 0, 6);
            if (step == BrakeStep) return;
            BrakeStep = step;
            Emergency = step == 6;
            if (step == 6) Notch = 0;
            Buzz();
        }

        void SetPower(float y)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt((bottomY - y) / (bottomY - topY) * 8f), 0, 8);
            if (n != Notch) { Notch = n; Buzz(); }
        }

        void Press(int i)
        {
            Buzz();
            if (i == 0) OnPantoToggle?.Invoke();
            else if (i == 1) { if (!Diesel) OnBreakerToggle?.Invoke(); }
            else if (i == 2) OnHorn?.Invoke();
            else if (i == 3) OnSwitch?.Invoke();
            else if (i == 4) CamMode = (CamMode + 1) % CamNames.Length;
            else
            {
                if (Emergency) { Emergency = false; BrakeStep = 5; }
                else ForceEmergency();
            }
        }

        void Panel(Rect2 r, Color c, int radius = 9)
        {
            style.BgColor = c;
            style.SetCornerRadiusAll(Mathf.RoundToInt(radius * u));
            DrawStyleBox(style, r);
        }

        void Label(string value, Vector2 pos, int size, Color color,
            HorizontalAlignment align = HorizontalAlignment.Left, float width = -1f)
        {
            DrawString(ThemeDB.FallbackFont, pos, value, align, width, size, color);
        }

        void Button(Rect2 r, string title, string subtitle, Color bg, bool active)
        {
            Panel(r, bg);
            if (active)
                Panel(new Rect2(r.Position.X + 5f * u, r.Position.Y + 4f * u,
                    r.Size.X - 10f * u, 4f * u), new Color(.35f, .95f, .52f), 3);

            int fs = Mathf.Max(11, Mathf.RoundToInt(14f * u));
            float y = r.Position.Y + r.Size.Y * .5f + (subtitle.Length > 0 ? -2f * u : fs * .35f);
            Label(title, new Vector2(r.Position.X, y), fs, Colors.White, HorizontalAlignment.Center, r.Size.X);
            if (subtitle.Length > 0)
                Label(subtitle, new Vector2(r.Position.X, y + 16f * u),
                    Mathf.Max(9, Mathf.RoundToInt(10f * u)), new Color(.84f, .88f, .92f),
                    HorizontalAlignment.Center, r.Size.X);
        }

        public override void _Draw()
        {
            if (!Enabled) return;
            LayoutControls();

            int fs = Mathf.Max(10, Mathf.RoundToInt(12f * u));
            Color glass = new Color(.025f, .035f, .045f, .72f);
            Color white = new Color(.94f, .96f, .98f);
            Color green = new Color(.25f, .72f, .42f);
            Color red = new Color(.83f, .22f, .17f);

            // Brake lever: larger handle, labelled detents and a clear active state.
            Panel(brakeZone, glass, 12);
            Label("BRAKE", new Vector2(brakeZone.Position.X, brakeZone.Position.Y + 22f * u),
                fs + 2, white, HorizontalAlignment.Center, brakeZone.Size.X);
            Panel(new Rect2(brakeX - 7f * u, topY - 5f * u, 14f * u,
                bottomY - topY + 10f * u), new Color(.12f, .14f, .16f), 6);
            for (int i = 0; i <= 6; i++)
            {
                float y = BrakeY(i);
                Color c = i == 6 ? red : (i <= 1 ? green : white);
                DrawLine(new Vector2(brakeX + 11f * u, y),
                    new Vector2(brakeX + 27f * u, y), c, 2f * u);
                Label(BrakeNames[i], new Vector2(brakeX + 32f * u, y + fs * .35f), fs, c);
            }
            float by = BrakeY(BrakeStep);
            Panel(new Rect2(brakeX - 24f * u, by - 15f * u, 48f * u, 30f * u),
                Emergency ? red : new Color(.88f, .43f, .19f), 7);
            Label(BrakeNames[BrakeStep], new Vector2(brakeX - 24f * u, by + fs * .35f),
                fs, Colors.White, HorizontalAlignment.Center, 48f * u);

            // Power lever.
            Panel(powerZone, glass, 12);
            Label("POWER", new Vector2(powerZone.Position.X, powerZone.Position.Y + 22f * u),
                fs + 2, white, HorizontalAlignment.Center, powerZone.Size.X);
            Panel(new Rect2(powerX - 7f * u, topY - 5f * u, 14f * u,
                bottomY - topY + 10f * u), new Color(.12f, .14f, .16f), 6);
            for (int i = 0; i <= 8; i++)
            {
                float y = PowerY(i);
                DrawLine(new Vector2(powerX - 27f * u, y),
                    new Vector2(powerX - 11f * u, y), white, 2f * u);
                Label(i.ToString(), new Vector2(powerX - 48f * u, y + fs * .35f),
                    fs, white, HorizontalAlignment.Right, 16f * u);
            }
            float py = PowerY(Notch);
            Panel(new Rect2(powerX - 24f * u, py - 15f * u, 48f * u, 30f * u),
                new Color(.95f, .66f, .19f), 7);
            Label("N" + Notch, new Vector2(powerX - 24f * u, py + fs * .35f),
                fs, Colors.Black, HorizontalAlignment.Center, 48f * u);

            // Reverser is a proper three-position row, not a narrow vertical stack.
            Panel(reverserZone, glass, 8);
            string[] reverseText = { "FWD", "NEUTRAL", "REV" };
            int selected = Reverser == 1 ? 0 : (Reverser == 0 ? 1 : 2);
            for (int i = 0; i < 3; i++)
                Button(reverserButtons[i], reverseText[i], "",
                    i == selected ? new Color(.17f, .43f, .29f) : new Color(.16f, .20f, .24f), i == selected);

            bool running = PantoUp;
            Button(actionButtons[0], Diesel ? "ENGINE" : "PANTO",
                Diesel ? (running ? "running" : "stopped") : (running ? "raised" : "lowered"),
                new Color(.16f, .27f, .36f), running);
            Button(actionButtons[1], "VCB", Diesel ? "not fitted" : (Breaker ? "closed" : "open"),
                new Color(.16f, .27f, .36f), Breaker && !Diesel);
            Button(actionButtons[2], "HORN", "sound horn", new Color(.16f, .27f, .36f), false);
            Button(actionButtons[3], "POINTS", SwitchLabel, new Color(.16f, .27f, .36f), false);
            Button(actionButtons[4], "CAMERA", CamNames[CamMode], new Color(.16f, .27f, .36f), false);
            Button(actionButtons[5], Emergency ? "RELEASE" : "EMERGENCY",
                Emergency ? "release brake" : "apply brake", Emergency ? new Color(.48f, .16f, .13f) : new Color(.65f, .16f, .13f), Emergency);

            if (ShowCoachButtons)
            {
                Button(coachLeft, "< COACH", "", new Color(.15f, .23f, .31f), false);
                Button(coachRight, "COACH >", "", new Color(.15f, .23f, .31f), false);
            }
        }
    }
}
