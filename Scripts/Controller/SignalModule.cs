using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // Signal management: every signal of the running route, with AUTO / STOP / CLEAR for each and ALL STOP / ALL AUTO.
    //   AUTO  = as the route was designed (timed holds work as before)
    //   STOP  = held at red until the controller clears it (a timed hold no longer clears it by itself)
    //   CLEAR = forced open (the aspect then follows the next signal: green, or yellow if the next one is red)
    public class SignalModule : ControllerModule
    {
        enum Mode { Auto, Stop, Clear }

        readonly Dictionary<string, Mode> modes = new Dictionary<string, Mode>();
        readonly Dictionary<string, bool> origHeld = new Dictionary<string, bool>();   // the route's own "held" flag, kept so AUTO can restore it
        Scenario seen;

        public override string Id { get { return "signals"; } }
        public override string Title { get { return "SIGNALS"; } }
        public override string Hint { get { return "AUTO = as designed.  STOP = hold at red until you clear it.  CLEAR = force the signal open."; } }

        static string AspectText(int a) { return a == 2 ? "RED" : (a == 1 ? "YELLOW" : "GREEN"); }
        static Color AspectColor(int a) { return a == 2 ? CcUi.Bad : (a == 1 ? CcUi.Warn : CcUi.Good); }
        static string LineName(TSignal s)
        {
            char c = s.Id.Length > 0 ? s.Id[0] : ' ';
            return c == 'D' ? "Down line" : (c == 'U' ? "Up line" : "Line");
        }

        Mode ModeOf(TSignal s) { Mode m; return modes.TryGetValue(s.Id, out m) ? m : Mode.Auto; }

        void Apply(TSignal s, Mode m)
        {
            if (!origHeld.ContainsKey(s.Id)) origHeld[s.Id] = s.Held;
            s.Started = false; s.Timer = 0f;
            if (m == Mode.Stop) { s.Held = false; s.Red = true; }
            else if (m == Mode.Clear) { s.Held = false; s.Red = false; }
            else { bool h = origHeld[s.Id]; s.Held = h; s.Red = h; }
            modes[s.Id] = m;
        }

        public override void Build(VBoxContainer host, ControllerContext ctx)
        {
            var sc = ctx.Sc;
            if (sc != seen) { seen = sc; modes.Clear(); origHeld.Clear(); }
            if (sc == null)
            {
                host.AddChild(CcUi.Notice("Signals are live. Start a drive, open the pause screen, then tap CONTROLLER."));
                return;
            }
            sc.Graph.UpdateAspects();

            int red = 0, yellow = 0, green = 0;
            foreach (var sig in sc.Graph.Signals) { if (sig.Aspect == 2) red++; else if (sig.Aspect == 1) yellow++; else green++; }
            VBoxContainer sv; var summary = CcUi.Card(out sv); host.AddChild(summary);
            sv.AddChild(CcUi.Text(sc.Graph.Signals.Count + " signals   ·   " + red + " red   ·   " + yellow + " yellow   ·   " + green + " green", 20, Colors.White, true));
            var flow = CcUi.Flow(); sv.AddChild(flow);
            var allStop = CcUi.Btn("ALL STOP", false, 170); flow.AddChild(allStop);
            allStop.Pressed += () =>
            {
                foreach (var sig in sc.Graph.Signals) Apply(sig, Mode.Stop);
                sc.Graph.UpdateAspects(); ctx.Say("All signals held at red", true); ctx.Rebuild();
            };
            var allAuto = CcUi.Btn("ALL AUTO", false, 170); flow.AddChild(allAuto);
            allAuto.Pressed += () =>
            {
                foreach (var sig in sc.Graph.Signals) Apply(sig, Mode.Auto);
                sc.Graph.UpdateAspects(); ctx.Say("All signals back to automatic", true); ctx.Rebuild();
            };

            foreach (var signal in sc.Graph.Signals) BuildRow(host, ctx, sc, signal);
        }

        void BuildRow(VBoxContainer host, ControllerContext ctx, Scenario sc, TSignal sg)
        {
            HBoxContainer h; var row = CcUi.Row(out h); host.AddChild(row);
            var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Pass };
            h.AddChild(info);
            info.AddChild(Ui.MakeLabel("SIGNAL " + sg.Id, 24, Colors.White, true));
            bool timed = origHeld.ContainsKey(sg.Id) ? origHeld[sg.Id] : sg.Held;
            string where = LineName(sg) + "  ·  " + sg.Edge.Id + " at " + Mathf.RoundToInt(sg.Offset) + " m"
                + (timed ? "  ·  timed hold " + Mathf.RoundToInt(sg.HoldSeconds) + " s" : "") + (sg.Passed ? "  ·  passed" : "");
            info.AddChild(CcUi.Text(where, 15, Ui.Dim));

            var aspect = Ui.MakeLabel(AspectText(sg.Aspect), 22, AspectColor(sg.Aspect), true, HorizontalAlignment.Center);
            aspect.CustomMinimumSize = new Vector2(110, 0); aspect.VerticalAlignment = VerticalAlignment.Center; h.AddChild(aspect);

            Mode cur = ModeOf(sg);
            foreach (Mode m in new[] { Mode.Auto, Mode.Stop, Mode.Clear })
            {
                Mode target = m; string label = target.ToString().ToUpperInvariant();
                var b = Ui.MakeButton(label, 18, cur == target, new Vector2(104, 52)); h.AddChild(b);
                b.Pressed += () =>
                {
                    Apply(sg, target); sc.Graph.UpdateAspects();
                    ctx.Say("Signal " + sg.Id + " set to " + label, true); ctx.Rebuild();
                };
            }
        }
    }
}
