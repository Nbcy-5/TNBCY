using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // The Controller Centre screen: a tab column on the left (one tab per module) and the open module on the right.
    // It is opened from the home screen and from the pause screen. Modules are registered with AddModule.
    public partial class ControllerCentre : Control
    {
        public System.Action OnClosed, Tick;      // Tick runs every frame, even while the centre is closed
        readonly List<ControllerModule> modules = new List<ControllerModule>();
        ControllerContext ctx; int current; bool built;
        VBoxContainer tabs, host; ScrollContainer scroll; Label statusL, modeL;

        public void AddModule(ControllerModule m) { modules.Add(m); }

        // Call once, after the node has been added to the tree and every module has been registered.
        public void Setup(ControllerContext c)
        {
            ctx = c; ctx.Rebuild = RebuildKeepScroll;
            MouseFilter = MouseFilterEnum.Stop; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

            var dim = new ColorRect { Color = new Color(.02f, .04f, .08f, .95f), MouseFilter = MouseFilterEnum.Ignore };
            dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(dim);

            var title = Ui.MakeLabel("CONTROLLER CENTRE", 36, Ui.GoldHi, true, HorizontalAlignment.Center);
            Ui.Place(title, .5f, 0, .5f, 0, -400, 14, 400, 68); AddChild(title);
            modeL = Ui.MakeLabel("", 18, Ui.Dim, false, HorizontalAlignment.Center);
            Ui.Place(modeL, .5f, 0, .5f, 0, -400, 70, 400, 100); AddChild(modeL);

            var close = Ui.MakeButton("<  CLOSE", 22, false, new Vector2(150, 52));
            Ui.Place(close, 0, 0, 0, 0, 30, 26, 180, 78); AddChild(close); close.Pressed += Close;

            tabs = new VBoxContainer(); tabs.AddThemeConstantOverride("separation", 12);
            Ui.Place(tabs, 0, 0, 0, 1, 30, 124, 300, -70); AddChild(tabs);

            scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Auto };
            Ui.Place(scroll, 0, 0, 1, 1, 320, 124, -30, -70); AddChild(scroll);
            host = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            host.AddThemeConstantOverride("separation", 12); scroll.AddChild(host);

            statusL = Ui.MakeLabel("", 20, Colors.White, true);
            Ui.Place(statusL, 0, 1, 1, 1, 30, -56, -30, -16); AddChild(statusL);

            Visible = false; built = true;
        }

        public void Open()
        {
            if (!built) return;
            Visible = true; statusL.Text = "";
            UpdateMode(); RebuildTabs(); BuildModule(); scroll.ScrollVertical = 0;
        }

        public void Close()
        {
            Visible = false;
            if (OnClosed != null) OnClosed();
        }

        public void Say(string message, bool ok)
        {
            if (statusL == null) return;
            statusL.Text = message;
            statusL.Modulate = ok ? new Color(.6f, 1f, .65f) : new Color(1f, .6f, .5f);
        }

        public override void _Process(double delta)
        {
            if (Tick != null) Tick();
        }

        void UpdateMode()
        {
            var sc = ctx.Sc;
            modeL.Text = sc == null
                ? "OFFLINE  -  start a drive to control its signals and switches"
                : "LIVE DRIVE  -  clock " + sc.ClockText() + (sc.Ended ? "  -  finished" : "");
        }

        void RebuildTabs()
        {
            foreach (var c in tabs.GetChildren()) { tabs.RemoveChild(c); c.QueueFree(); }
            for (int i = 0; i < modules.Count; i++)
            {
                int idx = i;
                var b = Ui.MakeButton(modules[i].Title, 20, i == current, new Vector2(270, 72));
                tabs.AddChild(b);
                b.Pressed += () => { current = idx; RebuildTabs(); BuildModule(); scroll.ScrollVertical = 0; };
            }
            var more = Ui.MakeButton("MORE COMING SOON", 16, false, new Vector2(270, 56));
            more.Disabled = true; tabs.AddChild(more);
        }

        void BuildModule()
        {
            foreach (var c in host.GetChildren()) { host.RemoveChild(c); c.QueueFree(); }
            if (modules.Count == 0) return;
            var m = modules[current];
            host.AddChild(Ui.MakeLabel(m.Title, 30, Ui.Gold, true));
            if (!string.IsNullOrEmpty(m.Hint)) host.AddChild(CcUi.Text(m.Hint, 17, Ui.Dim));
            try { m.Build(host, ctx); }
            catch (System.Exception ex)
            {
                GD.PrintErr("Controller Centre: " + m.Id + " failed: " + ex);
                host.AddChild(CcUi.Notice("This screen could not be built: " + ex.Message));
            }
        }

        // Rebuilds the open module after a change. The list is rebuilt from scratch, so the scroll position is put back one frame later.
        async void RebuildKeepScroll()
        {
            int keep = scroll.ScrollVertical;
            UpdateMode(); BuildModule();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            scroll.ScrollVertical = keep;
        }
    }
}
