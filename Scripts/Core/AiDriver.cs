using Godot;

namespace TNBCY
{
    // Computer driver for the opposing train: obeys speed limits, signals (red = stop, yellow = slow), stops at platforms
    // and never runs past the end of the track the dispatcher has reserved for it.
    public class AiDriver
    {
        public readonly Train T;
        readonly TrackGraph g;
        readonly float departAt, defaultLimit;
        readonly System.Collections.Generic.HashSet<TStation> served = new System.Collections.Generic.HashSet<TStation>();
        float dwell;
        public float Dwell { get { return dwell; } set { dwell = value; } }
        public string Status = "waiting";
        // Distance ahead that the dispatcher has cleared for this train (float.MaxValue = unrestricted).
        public System.Func<float> Authority;
        const float A = 0.30f;     // planning deceleration, m/s^2 (a bit gentler than the real brake)

        public AiDriver(Train t, TrackGraph graph, float departAfter, float defLimitKmh)
        { T = t; g = graph; departAt = departAfter; defaultLimit = defLimitKmh; }

        float Limit(TEdge e) { return (e.LimitKmh > 0f ? e.LimitKmh : defaultLimit) / 3.6f; }
        static float StopSpeed(float d) { return d <= 0f ? 0f : Mathf.Sqrt(2f * A * d); }
        static float LimitAt(float vLim, float d) { return Mathf.Sqrt(vLim * vLim + 2f * A * Mathf.Max(d, 0f)); }

        void Hold() { T.Phys.Throttle = 0f; T.Phys.BrakeHandle = 1f; }

        public void Update(float dt, float simTime)
        {
            var P = T.Phys;
            if (!T.HasPlannedRoute) { Hold(); Status = "route unavailable"; return; }
            if (T.Finished) { Hold(); Status = "finished"; return; }
            if (simTime < departAt) { Hold(); Status = "waiting to depart"; return; }
            if (dwell > 0f) { dwell -= dt; Hold(); Status = "at platform"; return; }

            float v = P.Speed;
            var e = T.FrontSeg.Edge;
            float acc = -T.FrontOffset;
            float target = Limit(e);
            TStation arriving = null; float arriveD = 0f;
            float lead = 15f + v * 3f;                       // brake early: the air brake builds up slowly

            for (int hop = 0; hop < 6 && e != null && acc < 3000f; hop++)
            {
                if (hop > 0) target = Mathf.Min(target, LimitAt(Limit(e), acc));
                foreach (var sg in g.Signals)
                {
                    if (sg.Edge != e) continue;
                    float d = acc + sg.Offset;
                    if (d < 0f) continue;
                    if (sg.Aspect == 2) target = Mathf.Min(target, StopSpeed(d - lead));
                    else if (sg.Aspect == 1) target = Mathf.Min(target, LimitAt(60f / 3.6f, d - 20f));
                }
                foreach (var st in g.Stations)
                {
                    if (st.Edge != e || !st.Stop || served.Contains(st)) continue;
                    float d = acc + st.Offset;
                    if (d < -8f) { served.Add(st); continue; }
                    target = Mathf.Min(target, StopSpeed(d - 4f - v * 1.5f));
                    if (arriving == null || d < arriveD) { arriving = st; arriveD = d; }
                }
                var n = e.To; acc += e.Length;
                if (n.Outs.Count == 0) { target = Mathf.Min(target, StopSpeed(acc - 8f - v * 1.5f)); break; }
                e = T.NextEdge(e);
            }

            // Movement authority: stop short of track that another train holds.
            if (Authority != null)
            {
                float authority = Authority();
                if (authority < 1e8f)
                {
                    float room = authority - 15f - v * 1.5f;
                    target = Mathf.Min(target, StopSpeed(room));
                    if (room <= 0f && v < 1f) { Hold(); Status = "waiting for clear track"; return; }
                }
            }

            if (arriving != null && arriveD < 25f && v < 0.4f)
            {
                served.Add(arriving); dwell = arriving.Dwell; Hold(); Status = "at " + arriving.Name; return;
            }

            float err = target - v;
            if (err > 0.5f) { P.Throttle = Mathf.Clamp(err / 4f, 0.2f, 1f); P.BrakeHandle = 0f; Status = "running"; }
            else if (err < -0.5f) { P.Throttle = 0f; P.BrakeHandle = Mathf.Clamp(-err / 3f, 0.2f, 1f); Status = "braking"; }
            else { P.Throttle = 0.15f; P.BrakeHandle = 0f; Status = "running"; }
        }
    }
}
