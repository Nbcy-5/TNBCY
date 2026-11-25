using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // Game rules on top of the track graph: time scale, switches, signal aspects, station stops, the opposing AI train.
    public class Scenario
    {
        public readonly NetworkSpec Net; public readonly TrackGraph Graph; public readonly Train Train;
        public readonly List<AiDriver> AiDrivers = new List<AiDriver>();
        public readonly List<Train> AiTrains = new List<Train>();
        public readonly List<string> AiServiceIds = new List<string>();
        public AiDriver Ai { get { return AiDrivers.Count == 0 ? null : AiDrivers[0]; } }
        public Train AiTrain { get { return AiTrains.Count == 0 ? null : AiTrains[0]; } }
        public readonly RailwayDispatcher Dispatcher;
        public float SimTime, TimeScale = 1f; public bool Ended; public string EndMessage = "";
        public System.Action<string, string> OnEvent;   // (message, "ok" | "bad")
        readonly HashSet<string> served = new HashSet<string>();

        public Scenario(NetworkSpec net, ConsistSpec consist, ConsistSpec aiConsist = null)
            : this(net, consist, "", null, null, aiConsist) { }

        public Scenario(NetworkSpec net, ConsistSpec consist, string playerDestinationEdge,
            IList<string> playerViaEdges, List<AiServiceRuntime> aiServices, ConsistSpec fallbackAiConsist = null)
        {
            Net = net; Graph = TrackGraph.Build(net);
            Train = new Train(consist, Graph.Edges[net.start.edge], net.start.offsetM);
            TimeScale = Mathf.Max(0.1f, net.timeScale);
            Dispatcher = new RailwayDispatcher(Graph);
            string dispatchReason;

            if (!string.IsNullOrEmpty(playerDestinationEdge))
            {
                if (!Dispatcher.TryPlanRoute("PLAYER", Train, playerDestinationEdge, playerViaEdges, true, out dispatchReason))
                    GD.PrintErr("Player route planning: " + dispatchReason);
            }
            else if (!Dispatcher.TryPlanToFarthestStop("PLAYER", Train, true, out dispatchReason))
                GD.PrintErr("Player route planning: " + dispatchReason);

            if (aiServices != null && aiServices.Count > 0)
            {
                foreach (var service in aiServices)
                {
                    if (service == null || service.consist == null || !Graph.Edges.ContainsKey(service.startEdge))
                    { GD.PrintErr("Skipping invalid AI service " + (service == null ? "(null)" : service.id)); continue; }
                    var aiTrain = new Train(service.consist, Graph.Edges[service.startEdge], service.startOffsetM);
                    var driver = new AiDriver(aiTrain, Graph, service.departureAfterS, net.defaultLimitKmh);
                    AiTrains.Add(aiTrain); AiDrivers.Add(driver); AiServiceIds.Add("AI-1"); AiServiceIds.Add(service.id);
                    if (!Dispatcher.TryPlanRoute(service.id, aiTrain, service.destinationEdge, service.viaEdges, false, out dispatchReason))
                        GD.PrintErr("AI route planning (" + service.id + "): " + dispatchReason);
                }
            }
            else if (net.ai != null && net.ai.enabled && Graph.Edges.ContainsKey(net.ai.edge))
            {
                var aiTrain = new Train(fallbackAiConsist ?? consist, Graph.Edges[net.ai.edge], net.ai.offsetM);
                var driver = new AiDriver(aiTrain, Graph, net.ai.departAfterS, net.defaultLimitKmh);
                AiTrains.Add(aiTrain); AiDrivers.Add(driver);
                if (!Dispatcher.TryPlanToFarthestStop("AI-1", aiTrain, false, out dispatchReason))
                    GD.PrintErr("AI route planning: " + dispatchReason);
            }

            // Long trains start further along the platform: move the start markers to where the fronts really are.
            foreach (var st in Graph.Stations)
            {
                if (st.Stop) continue;
                if (st.Edge == Train.FrontSeg.Edge && Mathf.Abs(st.Offset - net.start.offsetM) < 20f) st.Offset = Train.Odo;
                foreach (var aiTrain in AiTrains)
                    if (st.Edge == aiTrain.FrontSeg.Edge && Mathf.Abs(st.Offset - aiTrain.FrontOffset) < 20f)
                        st.Offset = aiTrain.FrontOffset;
            }
        }
        public List<string> ServedNames() { return new List<string>(served); }
        public void SetServed(IEnumerable<string> names) { served.Clear(); foreach (var n in names) served.Add(n); }
        public float LimitKmh(TEdge e) { return e.LimitKmh > 0f ? e.LimitKmh : Net.defaultLimitKmh; }
        void Emit(string m, bool good) { if (OnEvent != null) OnEvent(m, good ? "ok" : "bad"); }
        public float ElapsedReal { get { return SimTime / TimeScale; } }

        public string ClockText()
        {
            int h = 9, m = 0;
            if (!string.IsNullOrEmpty(Net.clockStart) && Net.clockStart.Length >= 5)
            { int.TryParse(Net.clockStart.Substring(0, 2), out h); int.TryParse(Net.clockStart.Substring(3, 2), out m); }
            int t = (int)SimTime + h * 3600 + m * 60;
            return (t / 3600 % 24).ToString("00") + ":" + (t / 60 % 60).ToString("00") + ":" + (t % 60).ToString("00");
        }

        public void Update(float dt)
        {
            if (Ended) return;
            float remain = dt * TimeScale;
            while (remain > 0f && !Ended) { float h = Mathf.Min(0.05f, remain); remain -= h; Step(h); }
        }

        void Step(float h)
        {
            SimTime += h;
            Dispatcher.Update();
            Train.Update(h);
            for (int i = 0; i < AiDrivers.Count; i++)
            {
                AiDrivers[i].Update(h, SimTime);
                AiTrains[i].Update(h);
            }
            UpdateSignals(h); UpdateStations();
            if (!Ended && Train.Finished) End();
        }

        bool Near(Train t, TSignal s)
        {
            if (t == null || t.FrontSeg.Edge != s.Edge) return false;
            float ahead = s.Offset - t.FrontOffset; return ahead < 400f && ahead > 0f;
        }

        void UpdateSignals(float dt)
        {
            float off = Train.FrontOffset;
            foreach (var s in Graph.Signals)
            {
                bool trainNear = Near(Train, s);
                for (int i = 0; i < AiTrains.Count && !trainNear; i++) trainNear = Near(AiTrains[i], s);
                if (s.Held && s.Red && !s.Started && trainNear) s.Started = true;
                if (s.Started && s.Red)
                {
                    s.Timer += dt;
                    if (s.Timer >= s.HoldSeconds) { s.Red = false; if (trainNear || Train.FrontSeg.Edge == s.Edge) Emit("Signal " + s.Id + " cleared", true); }
                }
                if (!s.Passed && Train.FrontSeg.Edge == s.Edge && off >= s.Offset)
                {
                    s.Passed = true;
                    if (s.Red) Emit("SIGNAL PASSED AT DANGER (" + s.Id + ")", false);
                }
            }
            Graph.UpdateAspects();
        }

        void MarkServed(string name)
        {
            served.Add(name);
            foreach (var st in Graph.Stations) if (st.Name == name && st.Edge.From.Id[0] == Train.FrontSeg.Edge.From.Id[0]) st.Done = true;
        }

        void UpdateStations()
        {
            float off = Train.FrontOffset;
            foreach (var st in Graph.Stations)
            {
                if (st.Done || Train.FrontSeg.Edge != st.Edge) continue;
                float d = st.Offset - off;
                if (!st.Stop) { if (d < -40f) st.Done = true; continue; }
                if (d <= 80f && d >= -40f && Train.Speed < 0.3f)
                {
                    float ad = Mathf.Abs(d);
                    MarkServed(st.Name); st.Done = true;
                    Emit("Stopped at " + st.Name + (ad <= 6f ? "" : " (" + Mathf.RoundToInt(ad) + " m from the mark)"), true);
                    if (st.Edge.To.Outs.Count == 0 && st.Edge.To.Type != "buffer") { Complete("Route complete"); return; }
                }
                else if (d < -40f)
                {
                    st.Done = true;
                    if (!served.Contains(st.Name)) Emit("Passed " + st.Name + " without stopping", false);
                }
            }
        }

        void Complete(string msg)
        {
            Ended = true; int el = (int)SimTime;
            EndMessage = msg + "\nTrip time " + (el / 60) + ":" + (el % 60).ToString("00");
        }

        void End()
        {
            if (Train.EndKind == 2) { Ended = true; EndMessage = "Ran into the buffer stop!"; }
            else if (Train.EndSpeed > 3f) { Ended = true; EndMessage = "Overran the end of the line"; }
            else Complete("Route complete");
        }

        // ---- switches ----
        public TNode NextSwitch(out float dist)
        {
            dist = 0f; var e = Train.FrontSeg.Edge; float acc = -Train.FrontOffset;
            for (int hop = 0; hop < 8 && e != null; hop++)
            {
                acc += e.Length; var n = e.To;
                if (n.IsSwitch) { dist = acc; return n; }
                e = Train.NextEdge(e);
            }
            return null;
        }
        public bool SwitchLocked(TNode n)
        {
            if (Train.Odo - Train.Length < n.PassedOdo) return true;      // train is still on it
            var cur = Train.FrontSeg;
            return cur.Edge.To == n && cur.Edge.Length - Train.FrontOffset < 40f;   // too close
        }
        public void ToggleSwitch()
        {
            var n = NextSwitch(out float d);
            if (n == null) { Emit("No switch ahead", false); return; }
            if (SwitchLocked(n)) { Emit(n.Name + " is locked (too close)", false); return; }
            n.Sel = (n.Sel + 1) % n.Outs.Count;
            Graph.UpdateAspects();
            Emit(n.Name + " set " + (n.Sel == 0 ? "MAIN" : "LOOP"), true);
        }
    }
}
