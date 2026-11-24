using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // Settings rows (used on the home screen and in the in-game pause menu).
    public partial class SettingsPanel : VBoxContainer
    {
        public System.Action Changed;
        Label sensVal; HSlider sens; Button shakeB, hapticB, gfxB, fpsB;

        Control Row(string title, Control right)
        {
            var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 58) };
            row.AddThemeConstantOverride("separation", 16);
            var l = Ui.MakeLabel(title, 22, Ui.Ink, false); l.CustomMinimumSize = new Vector2(250, 0); l.VerticalAlignment = VerticalAlignment.Center; row.AddChild(l);
            right.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(right); AddChild(row); return row;
        }
        Button Opt() { var b = Ui.MakeButton("", 20, false, new Vector2(240, 50)); return b; }
        void Fire() { Settings.Save(); if (Changed != null) Changed(); }

        public void Build()
        {
            AddThemeConstantOverride("separation", 6);
            var box = new HBoxContainer(); box.AddThemeConstantOverride("separation", 12);
            sens = new HSlider { MinValue = .3, MaxValue = 2.5, Step = .05, Value = Settings.CamSens, CustomMinimumSize = new Vector2(260, 44), SizeFlagsVertical = SizeFlags.ShrinkCenter, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var track = Ui.Panel(new Color(1f, 1f, 1f, .16f), Colors.Transparent, 6); track.ContentMarginTop = 5; track.ContentMarginBottom = 5;
            var fill = Ui.Panel(Ui.Gold, Colors.Transparent, 6); fill.ContentMarginTop = 5; fill.ContentMarginBottom = 5;
            sens.AddThemeStyleboxOverride("slider", track); sens.AddThemeStyleboxOverride("grabber_area", fill); sens.AddThemeStyleboxOverride("grabber_area_highlight", fill);
            var dot = Ui.Dot(40, Ui.GoldHi); sens.AddThemeIconOverride("grabber", dot); sens.AddThemeIconOverride("grabber_highlight", dot);
            sensVal = Ui.MakeLabel("", 22, Ui.GoldHi, true, HorizontalAlignment.Right); sensVal.CustomMinimumSize = new Vector2(80, 0); sensVal.VerticalAlignment = VerticalAlignment.Center;
            box.AddChild(sens); box.AddChild(sensVal);
            Row("Camera sensitivity", box);
            sens.ValueChanged += v => { Settings.CamSens = (float)v; sensVal.Text = Settings.CamSens.ToString("0.00") + "x"; Fire(); };
            sensVal.Text = Settings.CamSens.ToString("0.00") + "x";

            shakeB = Opt(); Row("Ride shake (camera)", shakeB);
            shakeB.Pressed += () => { Settings.Shake = (Settings.Shake + 1) % 4; Refresh(); Fire(); };
            hapticB = Opt(); Row("Phone vibration", hapticB);
            hapticB.Pressed += () => { Settings.Haptics = (Settings.Haptics + 1) % 3; Refresh(); Fire(); if (Settings.Haptics > 0) Input.VibrateHandheld(40, Settings.HapticK); };
            gfxB = Opt(); Row("Graphics", gfxB);
            gfxB.Pressed += () => { Settings.Gfx = (Settings.Gfx + 1) % 5; Refresh(); Fire(); };
            fpsB = Opt(); Row("Show FPS", fpsB);
            fpsB.Pressed += () => { Settings.ShowFps = !Settings.ShowFps; Refresh(); Fire(); };
            Refresh();
        }

        public void Refresh()
        {
            sens.SetValueNoSignal(Settings.CamSens); sensVal.Text = Settings.CamSens.ToString("0.00") + "x";
            shakeB.Text = Settings.ShakeNames[Settings.Shake]; hapticB.Text = Settings.HapticNames[Settings.Haptics];
            gfxB.Text = Settings.GfxNames[Settings.Gfx]; fpsB.Text = Settings.ShowFps ? "On" : "Off";
        }
    }
}
