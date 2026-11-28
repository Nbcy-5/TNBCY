using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // One place where a train can stop at a station: the game's own stop mark, or the loop line beside it.
    public class PlatformSlot
    {
        public string Base = "", Name = "", Label = "";    // Base = station name without " Platform n"
        public TEdge Edge; public float Offset;
        public TStation Real;                                // null for a loop platform
        public bool IsLoop, Done;                            // Done: loop platforms only (already stopped at or passed)
        public string Key { get { return Edge.Id; } }
    }

    // A slot that can be reached from a train's position, with the switch settings that lead to it.
    public class SlotRoute
    {
        public PlatformSlot Slot; public float Dist;         // Dist = metres from the train's front to the stop mark
        public List<TNode> Nodes = new List<TNode>();        // the switches on the way ...
        public List<int> Sels = new List<int>();             // ... and the position each must be in
        public List<float> NodeDist = new List<float>();     // ... and how far ahead each one is
        public int Mismatch()
        {
            int m = 0;
            for (int i = 0; i < Nodes.Count; i++) if (Nodes[i].Sel != Sels[i]) m++;
            return m;
        }
    }

    // Read-only questions about the track and the trains on it. Nothing here changes the game.
    public static class RailTools
    {
        public static string BaseName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            int i = name.IndexOf(" Platform", System.StringComparison.Ordinal);
            return i > 0 ? name.Substring(0, i) : name;
        }

        static string LabelFor(PlatformSlot s, Dictionary<string, string> labels)
        {
            string l;
            if (labels != null && labels.TryGetValue(s.Edge.Id, out l) && !string.IsNullOrEmpty(l)) return l;
            if (s.Name.Length > s.Base.Length) return s.Name.Substring(s.Base.Length).Trim();
            return s.IsLoop ? "Loop line" : "Main line";
        }

        // offset along edge e of the point nearest to p
        static float Nearest(TEdge e, Vector3 p)
        {
            float best = 1e18f, at = 0f;
            for (float s = 0f; s <= e.Length; s += 1f)
            {
                float d = e.PosAt(s).DistanceSquaredTo(p);
                if (d < best) { best = d; at = s; }
            }
            return at;
        }

        // Every stopping place of the route: the game's stop marks, plus a loop platform beside every stop mark that has a loop.
        public static List<PlatformSlot> BuildSlots(TrackGraph g, Dictionary<string, string> labels)
        {
            var list = new List<PlatformSlot>();
            foreach (var st in g.Stations)
            {
                if (!st.Stop) continue;
                var s = new PlatformSlot { Base = BaseName(st.Name), Name = st.Name, Edge = st.Edge, Offset = st.Offset, Real = st };
                s.Label = LabelFor(s, labels);
                list.Add(s);
            }
            var loops = new List<PlatformSlot>();
            foreach (var s in list)
            {
                var from = s.Edge.From;
                if (from.Outs.Count < 2) continue;
                foreach (var alt in from.Outs)
                {
                    if (alt == s.Edge || !alt.Turnout || alt.To != s.Edge.To) continue;     // a loop leaves the main line and rejoins it
                    var l = new PlatformSlot { Base = s.Base, Name = s.Name, Edge = alt, IsLoop = true };
                    l.Offset = Nearest(alt, s.Edge.PosAt(s.Offset));
                    l.Label = LabelFor(l, labels);
                    loops.Add(l);
                }
            }
            list.AddRange(loops);
            return list;
        }

        // Every slot ahead of the train within maxDist (along any route its switches could be set to), nearest first.
        public static List<SlotRoute> Ahead(List<PlatformSlot> slots, Train t, float maxDist)
        {
            var found = new List<SlotRoute>();
            Walk(slots, t.FrontSeg.Edge, -t.FrontOffset, maxDist, new List<TNode>(), new List<int>(), new List<float>(), found, 0);
            var best = new Dictionary<string, SlotRoute>();      // one route per slot: the one that needs the fewest switch changes
            foreach (var r in found)
            {
                SlotRoute old;
                if (!best.TryGetValue(r.Slot.Key, out old) || r.Mismatch() < old.Mismatch()) best[r.Slot.Key] = r;
            }
            var list = new List<SlotRoute>(best.Values);
            list.Sort((a, b) => a.Dist.CompareTo(b.Dist));
            return list;
        }

        static void Walk(List<PlatformSlot> slots, TEdge e, float acc, float maxDist, List<TNode> nodes, List<int> sels, List<float> nd, List<SlotRoute> found, int depth)
        {
            if (e == null || depth > 18 || acc > maxDist) return;
            foreach (var s in slots)
            {
                if (s.Edge != e) continue;
                float d = acc + s.Offset;
                if (d < -15f || d > maxDist) continue;
                var r = new SlotRoute { Slot = s, Dist = d };
                r.Nodes.AddRange(nodes); r.Sels.AddRange(sels); r.NodeDist.AddRange(nd);
                found.Add(r);
            }
            var n = e.To; float next = acc + e.Length;
            if (n.Outs.Count == 0) return;
            // v5.29: crossovers between the Down and Up mains are only ever set by hand. The controller's route search ignores them,
            // and a switch whose only other way out is a crossover is never touched (so a crossover thrown by the player stays thrown).
            int plain = 0; TEdge only = null;
            foreach (var o in n.Outs) if (!o.Crossover) { plain++; only = o; }
            if (plain == 1) { Walk(slots, only, next, maxDist, nodes, sels, nd, found, depth + 1); return; }
            for (int i = 0; i < n.Outs.Count; i++)
            {
                if (n.Outs[i].Crossover) continue;
                nodes.Add(n); sels.Add(i); nd.Add(next);
                Walk(slots, n.Outs[i], next, maxDist, nodes, sels, nd, found, depth + 1);
                nodes.RemoveAt(nodes.Count - 1); sels.RemoveAt(sels.Count - 1); nd.RemoveAt(nd.Count - 1);
            }
        }

        // true when any part of train t lies on edge e between offsets a and b
        public static bool Occupies(Train t, TEdge e, float a, float b)
        {
            if (t == null) return false;
            float tail = t.Odo - t.Length, head = t.Odo;
            foreach (var seg in t.Trail)
            {
                if (seg.Edge != e) continue;
                float s0 = seg.StartOdo, s1 = seg.StartOdo + e.Length;
                float lo = System.Math.Max(tail, s0), hi = System.Math.Min(head, s1);
                if (hi < lo) continue;
                if (hi - s0 >= a && lo - s0 <= b) return true;
            }
            return false;
        }

        // true when train other stands in the block that begins at signal s (up to the next signal, at most 2500 m)
        public static bool BlockBusy(TrackGraph g, TSignal s, Train other)
        {
            if (other == null) return false;
            var e = s.Edge; float from = s.Offset + 0.5f, travelled = 0f;
            for (int hop = 0; hop < 10 && e != null; hop++)
            {
                float to = e.Length; bool endsHere = false;
                List<TSignal> sigs;
                if (g.SigByEdge.TryGetValue(e, out sigs))
                    foreach (var sg in sigs) if (sg.Offset > from + 0.5f) { to = sg.Offset; endsHere = true; break; }
                if (Occupies(other, e, from, to)) return true;
                if (endsHere) return false;
                travelled += to - from;
                if (travelled > 2500f) return false;
                var n = e.To;
                if (n.Outs.Count == 0) return false;
                e = n.Outs[n.Sel]; from = 0f;
            }
            return false;
        }

        // the signals ahead of the train on the route its switches are set for, nearest first, with their distance
        public static List<KeyValuePair<TSignal, float>> SignalsAhead(TrackGraph g, Train t, float maxDist)
        {
            var res = new List<KeyValuePair<TSignal, float>>();
            var e = t.FrontSeg.Edge; float acc = -t.FrontOffset;
            for (int hop = 0; hop < 14 && e != null && acc <= maxDist; hop++)
            {
                List<TSignal> sigs;
                if (g.SigByEdge.TryGetValue(e, out sigs))
                    foreach (var sg in sigs)
                    {
                        float d = acc + sg.Offset;
                        if (d > 5f && d <= maxDist) res.Add(new KeyValuePair<TSignal, float>(sg, d));
                    }
                acc += e.Length;
                var n = e.To;
                e = n.Outs.Count > 0 ? n.Outs[n.Sel] : null;
            }
            return res;
        }
    }
}
