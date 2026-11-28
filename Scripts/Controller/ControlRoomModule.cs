using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // The control room: the autopilot switch, what the controller is doing with the other train and with yours, and its log.
    public class ControlRoomModule : ControllerModule
    {
        public override string Id { get { return "room"; } }
        public override string Title { get { return "CONTROL ROOM"; } }
        public override string Hint
        {
            get { return "The controller runs the other train by itself: it starts it on time, makes it stop at or skip stations, picks its track and platform and holds it at red signals. It also finds you a free platform before every station."; }
        }

        public override void Build(VBoxContainer host, ControllerContext ctx)
        {
            var d = ctx.Dispatch; var store = ctx.Schedules; var sc = ctx.Sc;
            if (d == null || store == null) { host.AddChild(CcUi.Notice("The controller is not available.")); return; }

            VBoxContainer v; var card = CcUi.Card(out v); host.AddChild(card);
            var top = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
            top.AddThemeConstantOverride("separation", 14); v.AddChild(top);
            top.AddChild(CcUi.Text("AUTOPILOT", 24, Colors.White, true));
            bool on = d.Autopilot;
            var state = Ui.MakeLabel(on ? "ON" : "OFF", 24, on ? CcUi.Good : CcUi.Bad, true, HorizontalAlignment.Right);
            state.CustomMinimumSize = new Vector2(100, 0); top.AddChild(state);
            v.AddChild(CcUi.Text(on ? "The controller is guiding the trains." : "Off: the other train drives itself as before, and no platform is chosen for you.", 17, Ui.Dim));
            var toggle = CcUi.Btn(on ? "TURN OFF" : "TURN ON", !on, 190); v.AddChild(toggle);
            toggle.Pressed += () =>
            {
                d.Autopilot = !d.Autopilot; store.SetAutopilot(d.Autopilot);
                ctx.Say(d.Autopilot ? "Autopilot on" : "Autopilot off", true); ctx.Rebuild();
            };

            if (sc == null)
            {
                host.AddChild(CcUi.Notice("Start a drive to see the controller at work: the other train, platforms and signals."));
                return;
            }

            // ---- the other train
            VBoxContainer av; var aiCard = CcUi.Card(out av); host.AddChild(aiCard);
            av.AddChild(CcUi.Text("OTHER TRAIN", 20, Ui.Gold, true));
            if (sc.Ai == null) av.AddChild(CcUi.Text("There is no other train on this route.", 18, Ui.Dim));
            else
            {
                var e = d.AiEntry;
                string num = e != null && e.trainNumber > 0 ? e.trainNumber.ToString("D5") + "   " : "";
                av.AddChild(CcUi.Text(num + d.AiName.ToUpperInvariant(), 22, Colors.White, true));
                if (e != null) av.AddChild(CcUi.Text(e.origin + "  →  " + e.destination, 18, Ui.GoldHi, true));
                av.AddChild(CcUi.Text("Status: " + (d.AiNote != null ? d.AiNote : sc.Ai.Status), 18, d.AiNote != null ? CcUi.Warn : Ui.Ink));
                av.AddChild(CcUi.Text("Next: " + d.AiPlanText(), 18, Ui.Ink));
                if (e != null && store.HasTimetable(e))
                    av.AddChild(CcUi.Text("Timetable: departs " + store.DepartureText(e) + (e.stops != null && e.stops.Count > 0 ? "   ·   stops at " + StopNames(e) : ""), 16, Ui.Dim));
                else av.AddChild(CcUi.Text("No timetable: it leaves a minute after you and stops at every station.", 16, Ui.Dim));
            }

            // ---- your train
            VBoxContainer pv; var plCard = CcUi.Card(out pv); host.AddChild(plCard);
            pv.AddChild(CcUi.Text("YOUR TRAIN", 20, Ui.Gold, true));
            pv.AddChild(CcUi.Text(d.PlayerName.ToUpperInvariant(), 22, Colors.White, true));
            pv.AddChild(CcUi.Text("Next: " + d.PlayerPlanText(), 18, Ui.Ink));

            // ---- log
            VBoxContainer lv; var logCard = CcUi.Card(out lv); host.AddChild(logCard);
            lv.AddChild(CcUi.Text("CONTROLLER LOG", 20, Ui.Gold, true));
            if (d.Log.Count == 0) lv.AddChild(CcUi.Text("Nothing yet.", 16, Ui.Dim));
            else
            {
                var lines = new List<string>();
                for (int i = d.Log.Count - 1; i >= 0 && lines.Count < 14; i--) lines.Add(d.Log[i]);
                lv.AddChild(CcUi.Text(string.Join("\n", lines), 15, Ui.Ink));
            }
        }

        static string StopNames(ScheduleEntry e)
        {
            var names = new List<string>();
            foreach (var s in e.stops) names.Add(RailTools.BaseName(s.station));
            return string.Join(", ", names);
        }
    }
}
