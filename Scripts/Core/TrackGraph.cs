using System.Collections.Generic;
using Godot;

namespace TNBCY
{
    public class TNode
    {
        public string Id = "", Name = "", Type = "plain";
        public Vector3 Pos;
        public List<TEdge> Outs = new List<TEdge>();
        public int Sel;                       // which outgoing edge the switch is set to
        public float PassedOdo = -1e9f;
        public bool IsSwitch { get { return Outs.Count > 1; } }
    }

    public class TEdge
    {
        public string Id = "";
        public TNode From, To;
        public List<Vector3> Pts = new List<Vector3>();
        public List<float> Cum = new List<float>();
        public List<Vector3> Tan = new List<Vector3>();
        public float Length, Gradient, CurveR, LimitKmh;   // Gradient in permil
        public bool Turnout;                                 // loop line that diverges from / merges into a main line
        public float SkipStart, SkipEnd;                     // metres where the loop's inner rail would cross the main line (frog)

        public void Finish()
        {
            Cum.Clear(); float c = 0f; Cum.Add(0f);
            for (int i = 1; i < Pts.Count; i++) { c += Pts[i].DistanceTo(Pts[i - 1]); Cum.Add(c); }
            Length = c;
            Tan.Clear();                       // smooth tangent at every point, so heading changes continuously along curves
            for (int i = 0; i < Pts.Count; i++)
            {
                Vector3 a = Pts[System.Math.Max(i - 1, 0)], b = Pts[System.Math.Min(i + 1, Pts.Count - 1)];
                Vector3 t = b - a; t.Y = 0f; Tan.Add(t.LengthSquared() < 1e-6f ? Vector3.Forward : t.Normalized());
            }
        }
        int Seg(float s)
        {
            int lo = 0, hi = Pts.Count - 2;
            while (lo < hi) { int mid = (lo + hi) / 2; if (s <= Cum[mid + 1]) hi = mid; else lo = mid + 1; }
            return lo;
        }
        public Vector3 PosAt(float s)
        {
            s = Mathf.Clamp(s, 0f, Length); int i = Seg(s);
            float l = Cum[i + 1] - Cum[i]; float u = l > 0.0001f ? (s - Cum[i]) / l : 0f;
            return Pts[i].Lerp(Pts[i + 1], u);
        }
        public Vector3 DirAt(float s)
        {
            s = Mathf.Clamp(s, 0f, Length); int i = Seg(s);
            float l = Cum[i + 1] - Cum[i]; float u = l > 0.0001f ? (s - Cum[i]) / l : 0f;
            Vector3 d = Tan[i].Lerp(Tan[i + 1], u); d.Y = 0f; return d.LengthSquared() < 1e-6f ? Tan[i] : d.Normalized();
        }
    }

    public class TSignal
    {
        public string Id = ""; public TEdge Edge; public float Offset, HoldSeconds, Timer;
        public bool Held, Red, Started, Passed;
        public int Aspect;                    // 0 green, 1 yellow, 2 red
    }
    public class TStation { public string Name = ""; public TEdge Edge; public float Offset, Dwell = 15f; public bool Done, Served, Stop = true; public string Side = "R"; }

    public class TrackGraph
    {
        public Dictionary<string, TNode> Nodes = new Dictionary<string, TNode>();
        public Dictionary<string, TEdge> Edges = new Dictionary<string, TEdge>();
        public List<TNode> Switches = new List<TNode>();
        public List<TSignal> Signals = new List<TSignal>();
        public List<TStation> Stations = new List<TStation>();
        public Dictionary<TEdge, List<TSignal>> SigByEdge = new Dictionary<TEdge, List<TSignal>>();

        // First signal after (edge, offset) along the route the switches are currently set for.
        public TSignal NextSignal(TEdge e, float offset)
        {
            for (int hop = 0; hop < 10 && e != null; hop++)
            {
                List<TSignal> l;
                if (SigByEdge.TryGetValue(e, out l))
                    foreach (var s in l) if (s.Offset > offset + 0.5f) return s;
                e = e.To.Outs.Count > 0 ? e.To.Outs[e.To.Sel] : null; offset = -1f;
            }
            return null;
        }

        // 3-aspect colour light signalling: RED = stop, YELLOW = next signal is red (be ready to stop), GREEN = clear.
        public void UpdateAspects()
        {
            foreach (var s in Signals)
            {
                if (s.Red) { s.Aspect = 2; continue; }
                var nx = NextSignal(s.Edge, s.Offset);
                s.Aspect = (nx != null && nx.Red) ? 1 : 0;
            }
        }

        // Catmull-Rom smoothing so bends through "via" points are curves, not sharp corners.
        static List<Vector3> Smooth(List<Vector3> p)
        {
            if (p.Count < 3) return p;
            var o = new List<Vector3>();
            for (int i = 0; i < p.Count - 1; i++)
            {
                Vector3 p0 = p[System.Math.Max(i - 1, 0)], p1 = p[i], p2 = p[i + 1], p3 = p[System.Math.Min(i + 2, p.Count - 1)];
                int n = System.Math.Max(2, (int)(p1.DistanceTo(p2) / 5f));
                for (int k = 0; k < n; k++)
                {
                    float t = k / (float)n, t2 = t * t, t3 = t2 * t;
                    o.Add(0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3));
                }
            }
            o.Add(p[p.Count - 1]); return o;
        }

        public static TrackGraph Build(NetworkSpec n)
        {
            var g = new TrackGraph();
            foreach (var ns in n.nodes)
                g.Nodes[ns.id] = new TNode { Id = ns.id, Type = ns.type ?? "plain", Pos = new Vector3(ns.x, 0f, ns.z) };
            foreach (var es in n.edges)
            {
                if (!g.Nodes.ContainsKey(es.fromNode) || !g.Nodes.ContainsKey(es.toNode)) { GD.PrintErr("Edge " + es.id + " uses unknown node"); continue; }
                var e = new TEdge { Turnout = !string.IsNullOrEmpty(es.turnout), Id = es.id, From = g.Nodes[es.fromNode], To = g.Nodes[es.toNode], Gradient = es.gradientPermil, CurveR = es.curveRadiusM, LimitKmh = es.speedLimitKmh };
                var pts = new List<Vector3> { e.From.Pos };
                if (es.via != null) foreach (var v in es.via) pts.Add(new Vector3(v[0], 0f, v[1]));
                pts.Add(e.To.Pos);
                e.Pts = Smooth(pts); e.Finish();
                e.From.Outs.Add(e); g.Edges[es.id] = e;
            }
            foreach (var e in g.Edges.Values)
            {
                if (!e.Turnout) continue;
                if (e.From.Outs.Count > 1)                       // diverging end: find where it clears the main line by one gauge
                {
                    var m = e.From.Outs[0];
                    for (float t = 2f; t < System.Math.Min(80f, System.Math.Min(e.Length, m.Length)); t += .5f)
                        if (e.PosAt(t).DistanceTo(m.PosAt(t)) >= 1.75f) { e.SkipStart = t; break; }
                }
                foreach (var m in g.Edges.Values)                // merging end
                    if (!m.Turnout && m.To == e.To && m != e)
                        for (float t = 2f; t < System.Math.Min(80f, System.Math.Min(e.Length, m.Length)); t += .5f)
                            if (e.PosAt(e.Length - t).DistanceTo(m.PosAt(m.Length - t)) >= 1.75f) { e.SkipEnd = t; break; }
            }
            int k = 1;
            foreach (var ns in n.nodes)
            {
                var nd = g.Nodes[ns.id];
                if (nd.IsSwitch) { nd.Name = "SW" + k; k++; g.Switches.Add(nd); }
            }
            foreach (var s in n.signals)
            {
                if (!g.Edges.ContainsKey(s.edge)) continue;
                g.Signals.Add(new TSignal { Id = s.id, Edge = g.Edges[s.edge], Offset = s.offsetM, Held = s.held, HoldSeconds = s.holdSeconds, Red = s.held });
            }
            foreach (var s in n.stations)
            {
                if (!g.Edges.ContainsKey(s.edge)) continue;
                g.Stations.Add(new TStation { Name = s.name, Edge = g.Edges[s.edge], Offset = s.offsetM, Stop = s.stop, Side = s.side ?? "R", Dwell = s.dwellS });
            }
            foreach (var sg in g.Signals)
            {
                List<TSignal> l;
                if (!g.SigByEdge.TryGetValue(sg.Edge, out l)) { l = new List<TSignal>(); g.SigByEdge[sg.Edge] = l; }
                l.Add(sg);
            }
            foreach (var l in g.SigByEdge.Values) l.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            g.UpdateAspects();
            return g;
        }
    }
}
