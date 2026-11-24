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
        public bool HudVisible = true;                      // false = only the small eye button is shown
        public System.Action OnMenu, OnHudToggle;
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
        Rect2 brakeZone, powerZone, reverserZone, coachLeft, coachRight, eyeBtn, menuBtn;
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

            // eye (hide / show all controls) and menu, top right
            eyeBtn = new Rect2(w - 66f * u, 12f * u, 54f * u, 54f * u);
            menuBtn = new Rect2(w - 128f * u, 12f * u, 54f * u, 54f * u);
        }

        float BrakeY(int step) { return bottomY - step * (bottomY - topY) / 6f; }
        float PowerY(int notch) { return bottomY - notch * (bottomY - topY) / 8f; }

        public bool Blocks(Vector2 p)
        {
            if (!Enabled) return false;
            LayoutControls();
            if (eyeBtn.Grow(6f * u).HasPoint(p)) return true;
            if (!HudVisible) return false;
            if (menuBtn.Grow(6f * u).HasPoint(p)) return true;
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
            if (eyeBtn.Grow(6f * u).HasPoint(p)) { HudVisible = !HudVisible; Buzz(); if (OnHudToggle != null) OnHudToggle(); return; }
            if (!HudVisible) return;
            if (menuBtn.Grow(6f * u).HasPoint(p)) { Buzz(); if (OnMenu != null) OnMenu(); return; }

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

        static void Buzz() { if (Settings.Haptics > 0) Input.VibrateHandheld(8, Settings.HapticK); }

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

        void Panel(Rect2 r, Color c, int radius = 9, bool border = false)
        {
            style.BgColor = c;
            style.SetCornerRadiusAll(Mathf.RoundToInt(radius * u));
            if (border) { style.BorderColor = new Color(Ui.Gold.R, Ui.Gold.G, Ui.Gold.B, .42f); style.SetBorderWidthAll(Mathf.Max(1, Mathf.RoundToInt(1.3f * u))); }
            else style.SetBorderWidthAll(0);
            DrawStyleBox(style, r);
        }

        void Label(string value, Vector2 pos, int size, Color color,
            HorizontalAlignment align = HorizontalAlignment.Left, float width = -1f)
        {
            DrawString(UiFont.Bold, pos, value, align, width, size, color);
        }

        void Button(Rect2 r, string title, string subtitle, Color bg, bool active)
        {
            Panel(r, bg, 10, true);
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

        void DrawEyeMenu()
        {
            float a = HudVisible ? 1f : .55f;
            Panel(eyeBtn, new Color(.03f, .06f, .10f, .80f * a), 12, true);
            Vector2 c = eyeBtn.GetCenter(); float r = 13f * u; Color col = new Color(.94f, .96f, .98f, a);
            DrawArc(c + new Vector2(0f, r * .62f), r * 1.2f, Mathf.DegToRad(215f), Mathf.DegToRad(325f), 14, col, 2.3f * u, true);
            DrawArc(c - new Vector2(0f, r * .62f), r * 1.2f, Mathf.DegToRad(35f), Mathf.DegToRad(145f), 14, col, 2.3f * u, true);
            DrawCircle(c, r * .36f, col);
            if (!HudVisible) DrawLine(c + new Vector2(-r, r), c + new Vector2(r, -r), new Color(1f, .35f, .3f, a), 2.6f * u, true);
            if (!HudVisible) return;
            Panel(menuBtn, new Color(.03f, .06f, .10f, .80f), 12, true);
            Vector2 m = menuBtn.GetCenter();
            for (int i = -1; i <= 1; i++) DrawLine(m + new Vector2(-11f * u, i * 8f * u), m + new Vector2(11f * u, i * 8f * u), Colors.White, 2.6f * u, true);
        }

        public override void _Draw()
        {
            if (!Enabled) return;
            LayoutControls();
            DrawEyeMenu();
            if (!HudVisible) return;

            int fs = Mathf.Max(10, Mathf.RoundToInt(12f * u));
            Color glass = new Color(.03f, .06f, .10f, .74f);
            Color white = new Color(.94f, .96f, .98f);
            Color green = new Color(.25f, .72f, .42f);
            Color red = new Color(.83f, .22f, .17f);
            Color track = new Color(.09f, .11f, .14f, .95f);

            // ---- Brake lever: labelled detents, red fill rising with the brake, clear active state.
            Panel(brakeZone, glass, 14, true);
            int bp = BrakeStep >= 5 ? 100 : (BrakeStep == 4 ? 75 : (BrakeStep == 3 ? 50 : (BrakeStep == 2 ? 20 : 0)));
            Label("BRAKE  " + (Emergency ? "EMG" : bp + "%"), new Vector2(brakeZone.Position.X, brakeZone.Position.Y + 22f * u),
                fs + 2, white, HorizontalAlignment.Center, brakeZone.Size.X);
            Panel(new Rect2(brakeX - 7f * u, topY - 5f * u, 14f * u, bottomY - topY + 10f * u), track, 6);
            for (int i = 1; i < BrakeStep; i++)
            {
                float y0 = BrakeY(i + 1) + 2f * u, y1 = BrakeY(i) - 2f * u;
                DrawRect(new Rect2(brakeX - 4f * u, y0, 8f * u, y1 - y0), new Color(.55f, .12f, .10f).Lerp(new Color(1f, .30f, .22f), i / 5f));
            }
            for (int i = 0; i <= 6; i++)
            {
                float y = BrakeY(i);
                Color c = i == 6 ? red : (i <= 1 ? green : white);
                DrawLine(new Vector2(brakeX + 11f * u, y), new Vector2(brakeX + 27f * u, y), c, 2f * u);
                Label(BrakeNames[i], new Vector2(brakeX + 32f * u, y + fs * .35f), fs, c);
            }
            float by = BrakeY(BrakeStep);
            Panel(new Rect2(brakeX - 24f * u, by - 15f * u, 48f * u, 30f * u), Emergency ? red : new Color(.90f, .45f, .20f), 8, true);
            Label(BrakeNames[BrakeStep], new Vector2(brakeX - 24f * u, by + fs * .35f), fs, Colors.White, HorizontalAlignment.Center, 48f * u);

            // ---- Power lever: green fill rising with the notch.
            Panel(powerZone, glass, 14, true);
            Label("POWER  " + Mathf.RoundToInt(Notch * 12.5f) + "%", new Vector2(powerZone.Position.X, powerZone.Position.Y + 22f * u),
                fs + 2, white, HorizontalAlignment.Center, powerZone.Size.X);
            Panel(new Rect2(powerX - 7f * u, topY - 5f * u, 14f * u, bottomY - topY + 10f * u), track, 6);
            for (int i = 0; i < Notch; i++)
            {
                float y0 = PowerY(i + 1) + 2f * u, y1 = PowerY(i) - 2f * u;
                DrawRect(new Rect2(powerX - 4f * u, y0, 8f * u, y1 - y0), new Color(.18f, .62f, .30f).Lerp(new Color(.55f, .95f, .35f), i / 7f));
            }
            for (int i = 0; i <= 8; i++)
            {
                float y = PowerY(i);
                DrawLine(new Vector2(powerX - 27f * u, y), new Vector2(powerX - 11f * u, y), white, 2f * u);
                Label(i.ToString(), new Vector2(powerX - 48f * u, y + fs * .35f), fs, white, HorizontalAlignment.Right, 16f * u);
            }
            float py = PowerY(Notch);
            Panel(new Rect2(powerX - 24f * u, py - 15f * u, 48f * u, 30f * u), new Color(.96f, .70f, .22f), 8, true);
            Label("N" + Notch, new Vector2(powerX - 24f * u, py + fs * .35f), fs, Colors.Black, HorizontalAlignment.Center, 48f * u);

            // ---- Reverser: a three-position row.
            Panel(reverserZone, glass, 10, true);
            string[] reverseText = { "FWD", "NEUTRAL", "REV" };
            int selected = Reverser == 1 ? 0 : (Reverser == 0 ? 1 : 2);
            for (int i = 0; i < 3; i++)
                Button(reverserButtons[i], reverseText[i], "", i == selected ? new Color(.17f, .43f, .29f) : new Color(.12f, .17f, .22f), i == selected);

            bool running = PantoUp;
            Color btn = new Color(.11f, .20f, .30f);
            Button(actionButtons[0], Diesel ? "ENGINE" : "PANTO", Diesel ? (running ? "running" : "stopped") : (running ? "raised" : "lowered"), btn, running);
            Button(actionButtons[1], "VCB", Diesel ? "not fitted" : (Breaker ? "closed" : "open"), btn, Breaker && !Diesel);
            Button(actionButtons[2], "HORN", "sound horn", btn, false);
            Button(actionButtons[3], "POINTS", SwitchLabel, btn, false);
            Button(actionButtons[4], "CAMERA", CamNames[CamMode], btn, false);
            Button(actionButtons[5], Emergency ? "RELEASE" : "EMERGENCY", Emergency ? "release brake" : "apply brake",
                Emergency ? new Color(.48f, .16f, .13f) : new Color(.65f, .16f, .13f), Emergency);

            if (ShowCoachButtons)
            {
                Button(coachLeft, "< COACH", "", new Color(.12f, .20f, .28f), false);
                Button(coachRight, "COACH >", "", new Color(.12f, .20f, .28f), false);
            }
        }
    }
}
