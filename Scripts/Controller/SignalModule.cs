using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // Signals: a live view of every signal and of the ones the controller is holding at red (and why).
    public class SignalModule : ControllerModule
    {
        public override string Id { get { return "signals"; } }
        public override string Title { get { return "SIGNALS"; } }
        public override string Hint { get { return "The controller holds a signal at red when no platform is free or the track ahead is occupied, and lets it go as soon as that is over."; } }

        static string AspectText(int a) { return a == 2 ? "RED" : (a == 1 ? "YELLOW" : "GREEN"); }
        static Color AspectColor(int a) { return a == 2 ? CcUi.Bad : (a == 1 ? CcUi.Warn : CcUi.Good); }
        static string LineName(TSignal s)
        {
            char c = s.Id.Length > 0 ? s.Id[0] : ' ';
            return c == 'D' ? "Down line" : (c == 'U' ? "Up line" : "Line");
        }

        public override void Build(VBoxContainer host, ControllerContext ctx)
        {
            var sc = ctx.Sc; var d = ctx.Dispatch;
            if (sc == null) { host.AddChild(CcUi.Notice("Signals are shown here during a drive.")); return; }

            foreach (var sg in sc.Graph.Signals)
            {
                HBoxContainer h; var row = CcUi.Row(out h); host.AddChild(row);
                var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Pass };
                h.AddChild(info);
                info.AddChild(Ui.MakeLabel("SIGNAL " + sg.Id, 24, Colors.White, true));
                string hold = d != null ? d.HoldReason(sg) : "";
                string where = LineName(sg) + "  ·  " + sg.Edge.Id + " at " + Mathf.RoundToInt(sg.Offset) + " m" + (hold != "" ? "  ·  held by the controller: " + hold : "");
                info.AddChild(CcUi.Text(where, 15, hold != "" ? CcUi.Warn : Ui.Dim));
                var aspect = Ui.MakeLabel(AspectText(sg.Aspect), 22, AspectColor(sg.Aspect), true, HorizontalAlignment.Right);
                aspect.CustomMinimumSize = new Vector2(120, 0); aspect.VerticalAlignment = VerticalAlignment.Center; h.AddChild(aspect);
            }
        }
    }
}
