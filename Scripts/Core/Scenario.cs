using Godot;

namespace TNBCY
{
    // Game rules on top of the track graph: switches, held signals, station stops, speeding, end of run.
    public class Scenario
    {
        public readonly NetworkSpec Net; public readonly TrackGraph Graph; public readonly Train Train;
        public float Score; public bool Ended; public string EndMessage = "";
        public System.Action<string, string> OnEvent;   // (message, "ok" | "bad")

        public Scenario(NetworkSpec net, ConsistSpec consist)
        {
            Net = net; Graph = TrackGraph.Build(net);
            Train = new Train(consist, Graph.Edges[net.start.edge], net.start.offsetM);
        }
        public float LimitKmh(TEdge e) { return e.LimitKmh > 0f ? e.LimitKmh : Net.defaultLimitKmh; }
        void Emit(string m, bool good) { if (OnEvent != null) OnEvent(m, good ? "ok" : "bad"); }
        void Lose(float pts) { Score = Mathf.Max(0f, Score - pts); }

        public void Update(float dt)
        {
            if (Ended) return;
            Train.Update(dt);
            if (Train.Speed > LimitKmh(Train.FrontSeg.Edge) / 3.6f + 1.5f) Lose(20f * dt);
            UpdateSignals(dt); UpdateStations();
            if (!Ended && Train.Finished) End();
        }

        void UpdateSignals(float dt)
        {
            float off = Train.FrontOffset;
            foreach (var s in Graph.Signals)
            {
                bool onEdge = Train.FrontSeg.Edge == s.Edge; float ahead = s.Offset - off;
                if (s.Held && s.Red && !s.Started && onEdge && ahead < 400f && ahead > 0f) s.Started = true;
                if (s.Started && s.Red)
                {
                    s.Timer += dt;
                    if (s.Timer >= s.HoldSeconds) { s.Red = false; Emit("Signal " + s.Id + " cleared", true); }
                }
                if (!s.Passed && onEdge && off >= s.Offset)
                {
                    s.Passed = true;
                    if (s.Red) { Lose(100f); Emit("Signal passed at danger (" + s.Id + ")", false); }
                }
            }
        }

        void UpdateStations()
        {
            float off = Train.FrontOffset;
            foreach (var st in Graph.Stations)
            {
                if (st.Done || Train.FrontSeg.Edge != st.Edge) continue;
                float d = st.Offset - off;
                if (d <= 80f && d >= -40f && Train.Speed < 0.3f)
                {
                    st.Done = true; float ad = Mathf.Abs(d); int pts = (int)Mathf.Max(0f, 500f - ad * 8f); Score += pts;
                    Emit((ad <= 6f ? "Perfect stop at " : "Stopped at ") + st.Name + "  +" + pts, true);
                    if (st.Edge.To.Outs.Count == 0 && st.Edge.To.Type != "buffer") { Ended = true; EndMessage = "Route complete"; return; }
                }
                else if (d < -40f) { st.Done = true; Lose(30f); Emit("Missed " + st.Name, false); }
            }
        }

        void End()
        {
            Ended = true;
            if (Train.EndKind == 2) { Lose(100f); EndMessage = "Ran into the siding buffer stop!"; }
            else if (Train.EndSpeed > 3f) { Lose(100f); EndMessage = "Overran the end of the line"; }
            else EndMessage = "Route complete";
        }

        // ---- switches ----
        public TNode NextSwitch(out float dist)
        {
            dist = 0f; var e = Train.FrontSeg.Edge; float acc = -Train.FrontOffset;
            for (int hop = 0; hop < 8 && e != null; hop++)
            {
                acc += e.Length; var n = e.To;
                if (n.IsSwitch) { dist = acc; return n; }
                e = n.Outs.Count > 0 ? n.Outs[n.Sel] : null;
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
            Emit(n.Name + " set " + (n.Sel == 0 ? "STRAIGHT" : "DIVERGING"), true);
        }
    }
}
