using Godot;
using System.Collections.Generic;
using System.Text.Json;

namespace TNBCY
{
    public class SessTrain
    {
        public List<string> edges = new List<string>(); public List<float> starts = new List<float>();
        public float odo, speed, throttle, brakeHandle, brakeLevel; public bool emergency;
    }
    public class SessSignal { public string id = ""; public bool red, started, passed; public float timer; }

    public class SessionData
    {
        public int version = 2;
        public string route = "", consist = "", serviceId = "", title = "", sub = "", clock = "", saved = "";
        public float simTime, aiDwell;
        public SessTrain player = new SessTrain(), ai;
        public List<SessTrain> aiTrains = new List<SessTrain>();
        public List<float> aiDwells = new List<float>();
        public List<string> aiServiceIds = new List<string>();
        public Dictionary<string, int> switches = new Dictionary<string, int>();
        public List<SessSignal> signals = new List<SessSignal>();
        public List<bool> stDone = new List<bool>(), stServed = new List<bool>();
        public List<string> served = new List<string>();
        public int notch, brakeStep, reverser; public bool panto, breaker;
    }

    // Saves the running game to user://session.json so "Resume session" can carry on exactly where you stopped.
    public static class Session
    {
        const string FilePath = "user://session.json";
        static readonly JsonSerializerOptions Opt = new JsonSerializerOptions { IncludeFields = true, PropertyNameCaseInsensitive = true };

        public static bool Exists() { return Godot.FileAccess.FileExists(FilePath); }

        public static void Delete()
        {
            if (!Exists()) return;
            using (var d = DirAccess.Open("user://")) { if (d != null) d.Remove("session.json"); }
        }

        public static void Save(SessionData d)
        {
            try
            {
                using var f = Godot.FileAccess.Open(FilePath, Godot.FileAccess.ModeFlags.Write);
                if (f == null) return;
                f.StoreString(JsonSerializer.Serialize(d, Opt));
            }
            catch (System.Exception ex) { GD.PrintErr("Session save failed: " + ex.Message); }
        }

        public static SessionData Load()
        {
            try
            {
                if (!Exists()) return null;
                using var f = Godot.FileAccess.Open(FilePath, Godot.FileAccess.ModeFlags.Read);
                if (f == null) return null;
                var d = JsonSerializer.Deserialize<SessionData>(f.GetAsText(), Opt);
                return (d != null && (d.version == 1 || d.version == 2) && d.player != null && d.player.edges.Count > 0) ? d : null;
            }
            catch (System.Exception ex) { GD.PrintErr("Session load failed: " + ex.Message); return null; }
        }

        static SessTrain CaptureTrain(Train t)
        {
            var s = new SessTrain { odo = t.Odo, speed = t.Phys.Speed, throttle = t.Phys.Throttle, brakeHandle = t.Phys.BrakeHandle, brakeLevel = t.Phys.BrakeLevel, emergency = t.Phys.Emergency };
            foreach (var seg in t.Trail) { s.edges.Add(seg.Edge.Id); s.starts.Add(seg.StartOdo); }
            return s;
        }

        public static SessionData Capture(Scenario sc, TouchControls t, string route, string consist, string consistName, string routeName, string serviceId = "")
        {
            var d = new SessionData { route = route, consist = consist, serviceId = serviceId, simTime = sc.SimTime, title = consistName, sub = routeName, clock = sc.ClockText(), saved = Time.GetDatetimeStringFromSystem(false, true) };
            d.player = CaptureTrain(sc.Train);
            for (int i = 0; i < sc.AiTrains.Count; i++)
            {
                d.aiTrains.Add(CaptureTrain(sc.AiTrains[i]));
                d.aiDwells.Add(sc.AiDrivers[i].Dwell);
                d.aiServiceIds.Add(sc.AiServiceIds[i]);
            }
            if (sc.AiTrains.Count > 0) { d.ai = d.aiTrains[0]; d.aiDwell = d.aiDwells[0]; }
            foreach (var n in sc.Graph.Switches) d.switches[n.Id] = n.Sel;
            foreach (var s in sc.Graph.Signals) d.signals.Add(new SessSignal { id = s.Id, red = s.Red, started = s.Started, passed = s.Passed, timer = s.Timer });
            foreach (var st in sc.Graph.Stations) { d.stDone.Add(st.Done); d.stServed.Add(st.Served); }
            d.served = sc.ServedNames();
            d.notch = t.Notch; d.brakeStep = t.BrakeStep; d.reverser = t.Reverser; d.panto = t.PantoUp; d.breaker = t.Breaker;
            return d;
        }

        // Validate a saved train's complete path before mutating any live simulation state.
        static bool TrainFits(Scenario sc, SessTrain s)
        {
            if (s == null || s.edges == null || s.starts == null || s.edges.Count == 0 || s.edges.Count != s.starts.Count) return false;
            foreach (var id in s.edges) if (!sc.Graph.Edges.ContainsKey(id)) return false;
            return true;
        }

        static bool RestoreTrain(Scenario sc, Train T, SessTrain s)
        {
            if (s == null || s.edges.Count == 0 || s.edges.Count != s.starts.Count) return false;
            var trail = new List<PathSeg>();
            for (int i = 0; i < s.edges.Count; i++)
            {
                TEdge e; if (!sc.Graph.Edges.TryGetValue(s.edges[i], out e)) return false;
                trail.Add(new PathSeg { Edge = e, StartOdo = s.starts[i] });
            }
            for (int i = 0; i + 1 < trail.Count; i++) trail[i].Edge.To.PassedOdo = trail[i + 1].StartOdo;
            T.Restore(trail, s.odo);
            var P = T.Phys; P.Speed = s.speed; P.Throttle = s.throttle; P.BrakeHandle = s.brakeHandle; P.BrakeLevel = s.brakeLevel; P.Emergency = s.emergency;
            return true;
        }

        // false = the saved data does not fit this route any more (the caller then starts fresh)
        public static bool Restore(Scenario sc, SessionData d, TouchControls t)
        {
            try
            {
                if (d.signals == null || d.stDone == null || d.stServed == null ||
                    d.signals.Count != sc.Graph.Signals.Count || d.stDone.Count != sc.Graph.Stations.Count ||
                    d.stServed.Count != sc.Graph.Stations.Count) return false;

                // Preflight every train and signal before changing the player, AI, points or signals.
                if (!TrainFits(sc, d.player)) return false;
                if (d.version >= 2)
                {
                    if (d.aiTrains == null || d.aiDwells == null || d.aiServiceIds == null ||
                        d.aiTrains.Count != sc.AiTrains.Count || d.aiDwells.Count != sc.AiDrivers.Count ||
                        d.aiServiceIds.Count != sc.AiServiceIds.Count) return false;
                    for (int i = 0; i < d.aiServiceIds.Count; i++)
                        if (d.aiServiceIds[i] != sc.AiServiceIds[i] || !TrainFits(sc, d.aiTrains[i])) return false;
                }
                else if (sc.AiTrains.Count > 0)
                {
                    if (sc.AiTrains.Count > 1 || d.ai == null || !TrainFits(sc, d.ai)) return false;
                }
                for (int i = 0; i < d.signals.Count; i++)
                    if (sc.Graph.Signals[i].Id != d.signals[i].id) return false;

                if (!RestoreTrain(sc, sc.Train, d.player)) return false;
                if (d.version >= 2)
                {
                    for (int i = 0; i < sc.AiTrains.Count; i++)
                    {
                        if (!RestoreTrain(sc, sc.AiTrains[i], d.aiTrains[i])) return false;
                        sc.AiDrivers[i].Dwell = d.aiDwells[i];
                    }
                }
                else if (sc.AiTrains.Count > 0)
                {
                    if (!RestoreTrain(sc, sc.AiTrains[0], d.ai)) return false;
                    sc.AiDrivers[0].Dwell = d.aiDwell;
                }
                foreach (var n in sc.Graph.Switches) { int v; if (d.switches.TryGetValue(n.Id, out v) && v >= 0 && v < n.Outs.Count) n.Sel = v; }
                for (int i = 0; i < d.signals.Count; i++)
                {
                    var s = sc.Graph.Signals[i]; var x = d.signals[i]; if (s.Id != x.id) return false;
                    s.Red = x.red; s.Started = x.started; s.Passed = x.passed; s.Timer = x.timer;
                }
                for (int i = 0; i < d.stDone.Count; i++) { sc.Graph.Stations[i].Done = d.stDone[i]; sc.Graph.Stations[i].Served = d.stServed[i]; }
                sc.SimTime = d.simTime; sc.SetServed(d.served);
                sc.Graph.UpdateAspects();
                t.Notch = d.notch; t.BrakeStep = d.brakeStep; t.Reverser = d.reverser; t.PantoUp = d.panto; t.Breaker = d.breaker;
                t.Emergency = sc.Train.Phys.Emergency;
                return true;
            }
            catch (System.Exception ex) { GD.PrintErr("Session restore failed: " + ex.Message); return false; }
        }
    }
}
