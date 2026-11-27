using Godot;

namespace TNBCY
{
    // Everything a Controller Centre module can use. Add a new field here when a future module needs something new.
    public class ControllerContext
    {
        public System.Func<Scenario> GetScenario = () => null;      // the running drive, or null on the home screen
        public System.Action SwitchesChanged = () => { };           // refresh the 3D switch tags
        public System.Action<string, bool> Say = (m, ok) => { };    // one-line status at the bottom of the centre
        public System.Action Rebuild = () => { };                   // rebuild the open screen (keeps the scroll position)
        public ScheduleStore Schedules;
        public Scenario Sc { get { return GetScenario(); } }
    }

    // One tab of the Controller Centre. To add a new one: write a class like SignalModule and register it
    // with ControllerCentre.AddModule(...) in Main.InstallControllerCentre (Scripts/MainController.cs).
    public abstract class ControllerModule
    {
        public abstract string Id { get; }
        public abstract string Title { get; }
        public virtual string Hint { get { return ""; } }
        public abstract void Build(VBoxContainer host, ControllerContext ctx);   // fill the right-hand area
    }

    // Small building blocks that give every module the same look (navy glass cards with a gold edge).
    public static class CcUi
    {
        public static readonly Color Good = new Color(.40f, .92f, .50f);
        public static readonly Color Warn = new Color(1f, .80f, .25f);
        public static readonly Color Bad = new Color(1f, .42f, .36f);

        public static PanelContainer Card(out VBoxContainer inner)
        {
            var p = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Pass };
            p.AddThemeStyleboxOverride("panel", Ui.Panel(new Color(.05f, .09f, .15f, .90f), new Color(Ui.Gold.R, Ui.Gold.G, Ui.Gold.B, .55f), 18, 2, 8));
            var m = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Pass };
            m.AddThemeConstantOverride("margin_left", 20); m.AddThemeConstantOverride("margin_right", 20);
            m.AddThemeConstantOverride("margin_top", 14); m.AddThemeConstantOverride("margin_bottom", 16);
            p.AddChild(m);
            inner = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
            inner.AddThemeConstantOverride("separation", 8);
            m.AddChild(inner);
            return p;
        }

        // A compact horizontal card for list rows (one signal, one switch).
        public static PanelContainer Row(out HBoxContainer row)
        {
            var p = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Pass };
            p.AddThemeStyleboxOverride("panel", Ui.Panel(new Color(.05f, .09f, .15f, .90f), new Color(Ui.Gold.R, Ui.Gold.G, Ui.Gold.B, .40f), 16, 2, 6));
            var m = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Pass };
            m.AddThemeConstantOverride("margin_left", 18); m.AddThemeConstantOverride("margin_right", 18);
            m.AddThemeConstantOverride("margin_top", 10); m.AddThemeConstantOverride("margin_bottom", 10);
            p.AddChild(m);
            row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
            row.AddThemeConstantOverride("separation", 12);
            m.AddChild(row);
            return p;
        }

        // A label that wraps and fills the width it is given.
        public static Label Text(string text, int size, Color color, bool bold = false)
        {
            var l = Ui.MakeLabel(text, size, color, bold);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            l.CustomMinimumSize = new Vector2(120, 0);
            return l;
        }

        public static Button Btn(string text, bool primary, int width = 150, int size = 18)
        {
            return Ui.MakeButton(text, size, primary, new Vector2(width, 52));
        }

        public static HFlowContainer Flow()
        {
            var f = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Pass };
            f.AddThemeConstantOverride("h_separation", 10); f.AddThemeConstantOverride("v_separation", 10);
            return f;
        }

        public static Control Notice(string text)
        {
            VBoxContainer v; var c = Card(out v);
            v.AddChild(Text(text, 20, Ui.Dim));
            return c;
        }
    }
}
