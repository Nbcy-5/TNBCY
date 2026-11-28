using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // Platforms: every stopping place of every station, and who has it right now.
    public class PlatformModule : ControllerModule
    {
        public override string Id { get { return "platforms"; } }
        public override string Title { get { return "PLATFORMS"; } }
        public override string Hint { get { return "FREE = you can have it.  RESERVED = a train has been given it.  OCCUPIED = a train is there now. Loop lines are platforms too."; } }

        static string Direction(PlatformSlot s)
        {
            char c = s.Edge.Id.Length > 0 ? char.ToLowerInvariant(s.Edge.Id[0]) : ' ';
            return c == 'd' ? "DOWN" : (c == 'u' ? "UP" : "YARD");
        }

        public override void Build(VBoxContainer host, ControllerContext ctx)
        {
            var sc = ctx.Sc; var d = ctx.Dispatch;
            if (sc == null || d == null) { host.AddChild(CcUi.Notice("Platforms are shown here during a drive.")); return; }

            var bases = new List<string>();
            foreach (var s in d.Slots) if (!bases.Contains(s.Base)) bases.Add(s.Base);
            foreach (var b in bases)
            {
                VBoxContainer v; var card = CcUi.Card(out v); host.AddChild(card);
                v.AddChild(CcUi.Text(b.ToUpperInvariant(), 22, Colors.White, true));
                foreach (var s in d.Slots)
                {
                    if (s.Base != b) continue;
                    string who; string state = d.SlotState(s, out who);
                    var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
                    row.AddThemeConstantOverride("separation", 12); v.AddChild(row);
                    row.AddChild(CcUi.Text(Direction(s) + "  ·  " + s.Label.ToUpperInvariant(), 18, Ui.Ink));
                    var st = Ui.MakeLabel(state + (who != "" ? "  " + who : ""), 17, state == "FREE" ? CcUi.Good : (state == "RESERVED" ? CcUi.Warn : CcUi.Bad), true, HorizontalAlignment.Right);
                    st.CustomMinimumSize = new Vector2(300, 0); row.AddChild(st);
                }
            }
        }
    }
}
