using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // Game rules on top of the track graph: time scale, switches, signal aspects, station stops, the opposing AI train.
    public class Scenario
    {
        public readonly NetworkSpec Net; public readonly TrackGraph Graph; public readonly Train Train;
        public readonly AiDriver Ai;
        public float SimTime, TimeScale = 1f; public bool Ended; public string EndMessage = "";
        public System.Action<string, string> OnEvent;   // (message, "ok" | "bad")
        readonly HashSet<string> served = new HashSet<string>();

        public Scenario(NetworkSpec net, ConsistSpec consist, ConsistSpec aiConsist = null)
        {
            Net = net;
            // v5.29: expand the route's "limits" (stations, loops, halts) into reverse-direction track, crossovers and signals. A fault here must never stop the game.
            try { LimitBuilder.Expand(net); } catch (System.Exception ex) { GD.PrintErr("Limits not applied: " + ex.Message); }
            Graph = TrackGraph.Build(net);
            // v64: a train always starts ON its platform. If the route's start point is not at the platform of the non-stopping start station
            // on that edge (e.g. the platform was moved), the start is snapped to that platform's stop mark.
            float startOff = PlatformStart(net.start.edge, net.start.offsetM);
            float aiOff = net.ai != null ? PlatformStart(net.ai.edge, net.ai.offsetM) : 0f;
            Train = new Train(consist, Graph.Edges[net.start.edge], startOff);
            TimeScale = Mathf.Max(0.1f, net.timeScale);
            if (net.ai != null && net.ai.enabled && Graph.Edges.ContainsKey(net.ai.edge))
                Ai = new AiDriver(new Train(aiConsist ?? consist, Graph.Edges[net.ai.edge], aiOff), Graph, net.ai.departAfterS, net.defaultLimitKmh);
            if (Ai != null) Ai.T.NoCrossover = true;
            // Long trains start further along the platform: move the start platforms to where the fronts really are.
            foreach (var st in Graph.Stations)
            {
                if (st.Stop) continue;
                if (st.Edge == Train.FrontSeg.Edge && Mathf.Abs(st.Offset - startOff) < 20f) st.Offset = Train.Odo;
                if (Ai != null && st.Edge == Ai.T.FrontSeg.Edge && Mathf.Abs(st.Offset - aiOff) < 20f) st.Offset = Ai.T.Odo;
            }
        }

        // Stop mark of the non-stopping (start) station on this edge that lies within one platform length of the given offset; the offset itself when there is none.
        float PlatformStart(string edgeId, float off)
        {
            float best = off, bd = 700f;
            foreach (var st in Graph.Stations)
            {
                if (st.Stop || st.Edge.Id != edgeId) continue;
                float d = Mathf.Abs(st.Offset - off);
                if (d < bd) { bd = d; best = st.Offset; }
            }
            return best;
        }
        public List<string> ServedNames() { return new List<string>(served); }
        public void SetServed(IEnumerable<string> names) { served.Clear(); foreach (var n in names) served.Add(n); }
        public Train AiTrain { get { return Ai == null ? null : Ai.T; } }
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
            Train.Update(h);
            if (Ai != null) Ai.Update(h, SimTime);
            if (Ai != null) Ai.T.Update(h);
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
                if (s.Guard) { try { s.Blocked = GuardBlocked(s); } catch (System.Exception) { s.Blocked = false; } }
                if (s.Held && s.Red && !s.Started && (Near(Train, s) || Near(AiTrain, s))) s.Started = true;
                if (s.Started && s.Red)
                {
                    s.Timer += dt;
                    if (s.Timer >= s.HoldSeconds) { s.Red = false; if (Near(Train, s) || Train.FrontSeg.Edge == s.Edge) Emit("Signal " + s.Id + " cleared", true); }
                }
                if (!s.Passed && Train.FrontSeg.Edge == s.Edge && off >= s.Offset)
                {
                    s.Passed = true;
                    if (s.Stop) Emit("SIGNAL PASSED AT DANGER (" + s.Id + ")", false);
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
                e = n.Outs.Count > 0 ? n.Outs[n.Sel] : null;
            }
            return null;
        }
        // ---- track occupancy (v5.29): an edge and its hidden twin are the same rails, so a train on either one occupies both ----
        IEnumerable<Train> AllTrains() { yield return Train; if (Ai != null) yield return Ai.T; }

        public bool EdgeOccupied(TEdge e)
        {
            foreach (var t in AllTrains()) if (t.OnEdge(e) || (e.Twin != null && t.OnEdge(e.Twin))) return true;
            return false;
        }

        // True when a train is on the rails of e running the OTHER way (on its twin).
        bool OpposingOn(TEdge e)
        {
            if (e.Twin == null) return false;
            foreach (var t in AllTrains()) if (t.OnEdge(e.Twin)) return true;
            return false;
        }

        // Any train on the track from o onwards (about 900 m, along the way the switches are set)?
        bool TrackAheadBusy(TEdge o)
        {
            var e = o; float acc = 0f;
            for (int hop = 0; hop < 6 && e != null && acc < 900f; hop++)
            {
                if (EdgeOccupied(e)) return true;
                acc += e.Length; var n = e.To; e = n.Outs.Count > 0 ? n.Outs[n.Sel] : null;
            }
            return false;
        }

        // A guard (limit / crossover) signal is held red while a train runs towards it on the rails ahead: opposing trains never meet head-on inside a limit.
        bool GuardBlocked(TSignal s)
        {
            var e = s.Edge; float acc = -s.Offset;
            for (int hop = 0; hop < 5 && e != null && acc < 700f; hop++)
            {
                if (OpposingOn(e)) return true;
                acc += e.Length; var n = e.To; e = n.Outs.Count > 0 ? n.Outs[n.Sel] : null;
            }
            return false;
        }

        // Why switch n cannot be set to position 'to' right now (null = it can). Covers both trains, and the head-on rule for crossovers.
        public string SwitchBlockReason(TNode n, int to)
        {
            try
            {
                foreach (var t in AllTrains())
                {
                    string who = t == Train ? "your train" : "the opposing train";
                    foreach (var o in n.Outs) if (t.OnEdge(o)) return who + " is on it";
                    var f = t.FrontSeg; float lead = t == Train ? 40f : 60f;
                    if (f.Edge.To == n && f.Edge.Length - t.FrontOffset < lead) return who + " is too close";
                    foreach (var seg in t.Trail) if (seg.Edge.To == n && t.OnEdge(seg.Edge)) return who + " is still passing it";
                }
                if (to >= 0 && to < n.Outs.Count && n.Outs[to].Crossover && TrackAheadBusy(n.Outs[to])) return "the track ahead is occupied";
            }
            catch (System.Exception) { }
            return null;
        }
        public bool SwitchLocked(TNode n) { return SwitchBlockReason(n, -1) != null; }

        public void ToggleSwitch()
        {
            var n = NextSwitch(out float d);
            if (n == null) { Emit("No switch ahead", false); return; }
            int to = (n.Sel + 1) % n.Outs.Count;
            string why = SwitchBlockReason(n, to);
            if (why != null) { Emit(n.Name + " is locked (" + why + ")", false); return; }
            n.Sel = to;
            Graph.UpdateAspects();
            Emit(n.Name + " set " + n.SelLabel, true);
        }
    }
}
