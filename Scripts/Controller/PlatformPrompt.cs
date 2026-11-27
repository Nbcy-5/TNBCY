using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    public class PromptOption
    {
        public string Key = "", Label = "", Note = "";
        public bool Free, Chosen, Recommended;
    }

    public class PromptModel
    {
        public string Title = "", Sub = "", Warn = "";
        public bool ChooseMode = true, CanChange;
        public List<PromptOption> Options = new List<PromptOption>();

        // everything except the distance line, so the buttons are only rebuilt when something real changes
        public string Signature()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Title).Append('|').Append(Warn).Append('|').Append(ChooseMode).Append('|').Append(CanChange);
            foreach (var o in Options)
                sb.Append('|').Append(o.Key).Append(':').Append(o.Label).Append(':').Append(o.Free).Append(':').Append(o.Note).Append(':').Append(o.Chosen).Append(':').Append(o.Recommended);
            return sb.ToString();
        }
    }

    // The "choose your platform" panel shown while driving. It does not pause the game and lets every touch outside the panel through.
    public partial class PlatformPrompt : Control
    {
        public System.Action<string> OnChoose;
        public System.Action OnChange;
        PanelContainer panel; Label titleL, subL; VBoxContainer list; string sig = "";

        public void Setup()
        {
            MouseFilter = MouseFilterEnum.Ignore; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            panel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, Visible = false };
            Ui.Place(panel, .5f, 0, .5f, 0, -300, 122, 300, 122); panel.GrowVertical = GrowDirection.End;
            panel.AddThemeStyleboxOverride("panel", Ui.Panel(new Color(.04f, .08f, .14f, .97f), new Color(Ui.Gold.R, Ui.Gold.G, Ui.Gold.B, .90f), 20, 2, 14));
            AddChild(panel);
            var m = new MarginContainer();
            m.AddThemeConstantOverride("margin_left", 20); m.AddThemeConstantOverride("margin_right", 20);
            m.AddThemeConstantOverride("margin_top", 14); m.AddThemeConstantOverride("margin_bottom", 16);
            panel.AddChild(m);
            var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 8); m.AddChild(box);
            titleL = Ui.MakeLabel("", 24, Ui.GoldHi, true, HorizontalAlignment.Center); box.AddChild(titleL);
            subL = Ui.MakeLabel("", 17, Ui.Dim, false, HorizontalAlignment.Center); box.AddChild(subL);
            list = new VBoxContainer(); list.AddThemeConstantOverride("separation", 8); box.AddChild(list);
        }

        public void ShowModel(PromptModel m)
        {
            if (panel == null) return;
            panel.Visible = true; titleL.Text = m.Title; subL.Text = m.Sub;
            string s = m.Signature();
            if (s == sig) return;
            sig = s;
            foreach (var c in list.GetChildren()) { list.RemoveChild(c); c.QueueFree(); }
            if (!string.IsNullOrEmpty(m.Warn))
            {
                var w = CcUi.Text(m.Warn, 19, CcUi.Bad, true); w.HorizontalAlignment = HorizontalAlignment.Center; list.AddChild(w);
            }
            foreach (var o in m.Options)
            {
                if (m.ChooseMode)
                {
                    var b = Ui.MakeButton(o.Label + (o.Free ? "" : "  -  " + o.Note), 20, o.Recommended, new Vector2(560, 54));
                    b.Disabled = !o.Free;
                    string key = o.Key;
                    b.Pressed += () => { if (OnChoose != null) OnChoose(key); };
                    list.AddChild(b);
                }
                else
                {
                    var l = Ui.MakeLabel(o.Label, 22, CcUi.Good, true, HorizontalAlignment.Center); list.AddChild(l);
                }
            }
            if (!m.ChooseMode && m.CanChange)
            {
                var ch = Ui.MakeButton("CHANGE PLATFORM", 18, false, new Vector2(560, 46));
                ch.Pressed += () => { if (OnChange != null) OnChange(); };
                list.AddChild(ch);
            }
        }

        public void HideModel()
        {
            if (panel != null && panel.Visible) { panel.Visible = false; sig = ""; }
        }
    }
}
