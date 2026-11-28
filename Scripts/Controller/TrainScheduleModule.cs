using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // Train schedules: shows every service with its timetable, and lets the controller retime, delay or cancel it.
    public class TrainScheduleModule : ControllerModule
    {
        public override string Id { get { return "trains"; } }
        public override string Title { get { return "TRAIN SCHEDULES"; } }
        public override string Hint
        {
            get { return "Retime, delay or cancel a service. Changes are saved on this phone. Cancelled trains are hidden from SELECT TRAIN. The game clock starts at the departure time shown here, and the controller sends the other train off at its own time and stops it only at the stations of its timetable."; }
        }

        public override void Build(VBoxContainer host, ControllerContext ctx)
        {
            var store = ctx.Schedules;
            if (store == null || store.Entries.Count == 0) { host.AddChild(CcUi.Notice("No trains found in Data/trains.json.")); return; }
            foreach (var entry in store.Entries) BuildCard(host, ctx, store, entry);
            host.AddChild(CcUi.Text("New trains are added in Data/trains.json (see CONTROLLER_CENTRE.txt).", 15, Ui.Dim));
        }

        static void BuildCard(VBoxContainer host, ControllerContext ctx, ScheduleStore store, ScheduleEntry en)
        {
            var o = store.Peek(en);
            VBoxContainer v; var card = CcUi.Card(out v); host.AddChild(card);

            var top = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
            top.AddThemeConstantOverride("separation", 14); v.AddChild(top);
            string number = en.trainNumber > 0 ? en.trainNumber.ToString("D5") + "   " : "";
            top.AddChild(CcUi.Text(number + en.name.ToUpperInvariant(), 24, Colors.White, true));
            int kind = store.StatusKind(en);
            var status = Ui.MakeLabel(store.StatusText(en), 20, kind == 2 ? CcUi.Bad : (kind == 1 ? CcUi.Warn : CcUi.Good), true, HorizontalAlignment.Right);
            status.CustomMinimumSize = new Vector2(240, 0); top.AddChild(status);

            v.AddChild(CcUi.Text(en.origin + "  →  " + en.destination, 20, Ui.GoldHi, true));
            string days = string.IsNullOrEmpty(en.days) ? "Daily" : en.days;
            string dist = en.distanceKm > 0f ? "  ·  " + en.distanceKm.ToString("0.#") + " km" : "";
            v.AddChild(CcUi.Text((string.IsNullOrEmpty(en.kind) ? "Passenger" : en.kind) + "  ·  " + en.coaches + " coaches  ·  Platform " + en.platform + "  ·  " + days + dist, 17, Ui.Dim));
            v.AddChild(CcUi.Text("DEPARTS " + store.DepartureText(en) + (store.HasTimetable(en) ? "" : " (default)") + "      ARRIVES " + store.ArrivalText(en), 22, Colors.White, true));

            if (en.stops != null && en.stops.Count > 0)
            {
                var lines = new List<string>();
                foreach (var s in en.stops)
                    lines.Add(s.station + "      ARR " + store.StopTimeText(s.arr, en) + "      DEP " + store.StopTimeText(s.dep, en));
                v.AddChild(CcUi.Text(string.Join("\n", lines), 18, Ui.Ink));
            }

            var flow = CcUi.Flow(); v.AddChild(flow);
            AddButton(flow, "DELAY +5", false, false, () =>
            {
                store.AddDelay(en, 5); ctx.Say(en.name + ": delayed " + store.Peek(en).delayMin + " min", true); ctx.Rebuild();
            });
            AddButton(flow, "DELAY -5", false, o.delayMin <= 0, () =>
            {
                store.AddDelay(en, -5); ctx.Say(en.name + ": delay " + store.Peek(en).delayMin + " min", true); ctx.Rebuild();
            });
            AddButton(flow, "RETIME +5", false, false, () =>
            {
                store.Retime(en, 5); ctx.Say(en.name + ": now departs " + store.DepartureText(en), true); ctx.Rebuild();
            });
            AddButton(flow, "RETIME -5", false, false, () =>
            {
                store.Retime(en, -5); ctx.Say(en.name + ": now departs " + store.DepartureText(en), true); ctx.Rebuild();
            });
            AddButton(flow, o.cancelled ? "RESTORE" : "CANCEL", o.cancelled, false, () =>
            {
                bool cancel = !store.Peek(en).cancelled;
                if (!store.SetCancelled(en, cancel)) { ctx.Say("At least one service has to keep running", false); return; }
                ctx.Say(en.name + (cancel ? ": cancelled" : ": restored"), true); ctx.Rebuild();
            });
            AddButton(flow, "RESET", false, o.IsDefault(), () =>
            {
                store.Reset(en); ctx.Say(en.name + ": back to the timetable", true); ctx.Rebuild();
            });
        }

        static void AddButton(HFlowContainer flow, string text, bool primary, bool disabled, System.Action act)
        {
            var b = CcUi.Btn(text, primary, 150);
            b.Disabled = disabled; flow.AddChild(b); b.Pressed += act;
        }
    }
}
