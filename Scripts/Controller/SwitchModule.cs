using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // Switches: a live view of every set of points. The controller sets them for the trains' platforms and routes.
    public class SwitchModule : ControllerModule
    {
        public override string Id { get { return "switches"; } }
        public override string Title { get { return "SWITCHES"; } }
        public override string Hint { get { return "The controller sets the points ahead of each train for its platform, and never moves a switch that a train is on or about to reach."; } }

        static string PosName(TNode n, int i) { return n.PosLabel(i); }      // v5.29: MAIN, LOOP, CROSS (crossover) or ROUTE n

        public override void Build(VBoxContainer host, ControllerContext ctx)
        {
            var sc = ctx.Sc; var d = ctx.Dispatch;
            if (sc == null) { host.AddChild(CcUi.Notice("Switches are shown here during a drive.")); return; }

            foreach (var node in sc.Graph.Switches)
            {
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

                bool busy = d != null && d.SwitchInUse(node);
                var state = Ui.MakeLabel(busy ? "IN USE" : "FREE", 20, busy ? CcUi.Warn : CcUi.Good, true, HorizontalAlignment.Right);
                state.CustomMinimumSize = new Vector2(110, 0); state.VerticalAlignment = VerticalAlignment.Center; h.AddChild(state);
            }
        }
    }
}
