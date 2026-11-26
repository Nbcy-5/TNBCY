using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // Switch management: every set of points on the running route, thrown to MAIN or LOOP.
    // A switch is locked while a train is on it or about to reach it, so a train is never thrown off the rails.
    public class SwitchModule : ControllerModule
    {
        public override string Id { get { return "switches"; } }
        public override string Title { get { return "SWITCHES"; } }
        public override string Hint { get { return "Set the points to the main line or the loop. A switch is locked while a train is on it or very close to it."; } }

        static string PosName(TNode n, int i)
        {
            return i == 0 ? "MAIN" : (n.Outs.Count == 2 ? "LOOP" : "ROUTE " + (i + 1));
        }

        // true when the switch must not be thrown now; why says the reason
        static bool Locked(Scenario sc, TNode n, out string why)
        {
            why = "";
            if (sc.SwitchLocked(n)) { why = "your train is on it or too close"; return true; }
            var ai = sc.AiTrain;
            if (ai != null && ai.Trail.Count > 0)
            {
                var seg = ai.FrontSeg;
                if (seg.Edge.To == n && seg.Edge.Length - ai.FrontOffset < 60f) { why = "the opposing train is about to reach it"; return true; }
                foreach (var o in n.Outs)
                    if (seg.Edge == o && ai.FrontOffset < ai.Length + 5f) { why = "the opposing train is on it"; return true; }
            }
            return false;
        }

        static void Throw(Scenario sc, TNode n, int to, ControllerContext ctx)
        {
            if (n.Sel == to) { ctx.Say(n.Name + " is already set to " + PosName(n, to), true); return; }
            string why;
            if (Locked(sc, n, out why)) { ctx.Say(n.Name + " is locked: " + why, false); ctx.Rebuild(); return; }
            n.Sel = to; sc.Graph.UpdateAspects(); ctx.SwitchesChanged();
            ctx.Say(n.Name + " set to " + PosName(n, to), true); ctx.Rebuild();
        }

        public override void Build(VBoxContainer host, ControllerContext ctx)
        {
            var sc = ctx.Sc;
            if (sc == null)
            {
                host.AddChild(CcUi.Notice("Switches are live. Start a drive, open the pause screen, then tap CONTROLLER."));
                return;
            }
            VBoxContainer sv; var summary = CcUi.Card(out sv); host.AddChild(summary);
            sv.AddChild(CcUi.Text(sc.Graph.Switches.Count + " switches on this route", 20, Colors.White, true));
            float ahead; var next = sc.NextSwitch(out ahead);
            if (next != null)
                sv.AddChild(CcUi.Text("Next switch on your path: " + next.Name + " in " + (ahead < 1000f ? Mathf.RoundToInt(ahead) + " m" : (ahead / 1000f).ToString("0.0") + " km"), 17, Ui.GoldHi));

            foreach (var node in sc.Graph.Switches) BuildRow(host, ctx, sc, node);
        }

        static void BuildRow(VBoxContainer host, ControllerContext ctx, Scenario sc, TNode node)
        {
            string why; bool locked = Locked(sc, node, out why);
            HBoxContainer h; var row = CcUi.Row(out h); host.AddChild(row);
            var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Pass };
            h.AddChild(info);

            var head = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
            head.AddThemeConstantOverride("separation", 14); info.AddChild(head);
            head.AddChild(Ui.MakeLabel(node.Name, 26, Colors.White, true));
            head.AddChild(Ui.MakeLabel("set to " + PosName(node, node.Sel), 20, Ui.GoldHi, true));

            var routes = new List<string>();
            foreach (var o in node.Outs) routes.Add(o.Id + " " + Mathf.RoundToInt(sc.LimitKmh(o)) + " km/h");
            info.AddChild(CcUi.Text("Junction " + node.Id + "   ·   " + string.Join("  /  ", routes), 15, Ui.Dim));
            if (locked) info.AddChild(CcUi.Text("LOCKED: " + why, 16, CcUi.Warn, true));

            for (int i = 0; i < node.Outs.Count; i++)
            {
                int to = i;
                var b = Ui.MakeButton(PosName(node, i), 18, node.Sel == i, new Vector2(120, 52));
                b.Disabled = locked; h.AddChild(b);
                b.Pressed += () => Throw(sc, node, to, ctx);
            }
        }
    }
}
