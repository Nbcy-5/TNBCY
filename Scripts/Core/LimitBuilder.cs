using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TNBCY
{
    // v5.28: two-way running only inside "limits".
    //
    // A limit (see LimitSpec) is a named stretch of the Down and Up mains - a main station, a passing loop or a halt. For every limit this class
    // rewrites the route data at start-up, before the track graph is built:
    //   1. The Down and Up main edges are cut where the crossovers attach (the longest piece keeps the original edge id, so stations, signals and
    //      the start points keep working; their offsets are re-mapped).
    //   2. Every track inside the limit gets a hidden reverse twin (same rails, opposite direction). A twin is a "virtual" edge: it is never drawn,
    //      has no masts and is not in TrackGraph.Edges. It is what lets a train run the "wrong" way on a main inside the limit.
    //   3. Two single crossovers at each end join the Down and Up mains - one for each running direction (4 crossover edges per limit):
    //        X1 Down main -> Up main (twin), near the north end     X2 Down main (twin) -> Up main, near the north end
    //        X3 Up main (twin) -> Down main, near the south end     X4 Up main -> Down main (twin), near the south end
    //      A crossover is always the SECOND way out of its switch, so every switch defaults to MAIN.
    //   4. Signals (flagged Guard) are added in both directions at the entry and exit of the limit and at both ends of every crossover.
    // Outside the limits nothing changes: Down stays Down and Up stays Up, with no crossovers.
    // If anything goes wrong in one limit the route is left exactly as it was for that limit.
    public static class LimitBuilder
    {
        static readonly JsonSerializerOptions Opt = new JsonSerializerOptions { IncludeFields = true, PropertyNameCaseInsensitive = true };

        sealed class Piece { public string Id, From, To; public float A, B; public EdgeSpec Spec; }

        static T Clone<T>(T x) { return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(x, Opt), Opt); }

        public static void Expand(NetworkSpec net)
        {
            if (net == null || net.limitsDone) return;
            net.limitsDone = true;
            if (net.limits == null || net.limits.Count == 0) return;
            var work = Clone(net);
            foreach (var e in work.edges) if (string.IsNullOrEmpty(e.baseId)) { e.baseId = e.id; e.baseStart = 0f; }
            int applied = 0;
            foreach (var lim in net.limits)
            {
                var trial = Clone(work);
                try { ExpandOne(trial, lim); work = trial; applied++; }
                catch (Exception ex) { GD.PrintErr("Limit " + lim.id + " skipped: " + ex.Message); }
            }
            if (applied == 0) return;
            net.nodes = work.nodes; net.edges = work.edges; net.signals = work.signals; net.stations = work.stations; net.start = work.start; net.ai = work.ai;
            GD.Print("Limits applied: " + applied + " of " + net.limits.Count);
        }

        // ------------------------------------------------------------------------------------------------ helpers
        static char Flip(char c) { return c == 'D' ? 'U' : (c == 'U' ? 'D' : c); }
        static string TwNode(string id) { return Flip(id.Length > 0 ? id[0] : 'N') + "~r~" + id; }
        static string TwEdge(string id) { return "r~" + id; }

        static NodeSpec NodeById(NetworkSpec w, string id)
        {
            foreach (var n in w.nodes) if (n.id == id) return n;
            throw new Exception("node " + id + " not found");
        }

        static void EnsureTwinNode(NetworkSpec w, string nodeId)
        {
            string tw = TwNode(nodeId);
            foreach (var n in w.nodes) if (n.id == tw) return;
            var o = NodeById(w, nodeId);
            w.nodes.Add(new NodeSpec { id = tw, x = o.x, z = o.z, type = "plain" });
        }

        // The piece (cut from route-file edge baseId) that holds [from, to] (to < 0 = to the end of the piece that holds 'from').
        static EdgeSpec FindPiece(NetworkSpec w, TrackGraph g, string baseId, float from, ref float to, out float local0)
        {
            foreach (var es in w.edges)
            {
                if (es.virt || es.crossover || es.baseId != baseId) continue;
                TEdge ge; if (!g.ById.TryGetValue(es.id, out ge)) continue;
                float bs = es.baseStart, be = bs + ge.Length;
                if (from < bs - 0.01f || from > be + 0.01f) continue;
                if (to < 0f) to = be;
                if (to > be + 0.01f) continue;
                local0 = bs; return es;
            }
            throw new Exception("limit does not fit on one piece of edge " + baseId);
        }

        static float NearestOffset(TEdge e, float lo, float hi, Vector3 p)
        {
            float best = lo, bd = float.MaxValue;
            for (float s = lo; s <= hi + 0.001f; s += 1f) { float d = e.PosAt(s).DistanceTo(p); if (d < bd) { bd = d; best = s; } }
            float c = best;
            for (float s = Mathf.Max(lo, c - 1f); s <= Mathf.Min(hi, c + 1f) + 0.001f; s += 0.1f) { float d = e.PosAt(s).DistanceTo(p); if (d < bd) { bd = d; best = s; } }
            return best;
        }

        static List<List<float>> ViaOf(TEdge ge, float a, float b, bool whole, EdgeSpec original)
        {
            if (whole) return original.via;
            var v = new List<List<float>>();
            for (float s = a + 8f; s < b - 4f; s += 8f) { var p = ge.PosAt(s); v.Add(new List<float> { p.X, p.Z }); }
            return v;
        }

        // Cut one edge at the given offsets. Returns the pieces in order; w.edges, stations, signals, start and ai are updated.
        static List<Piece> Cut(NetworkSpec w, TrackGraph g, EdgeSpec es, List<float> cuts)
        {
            TEdge ge = g.ById[es.id];
            var inner = new List<float>();
            cuts.Sort();
            foreach (var c in cuts)
            {
                if (c < 1f || c > ge.Length - 1f) continue;
                if (inner.Count > 0 && c - inner[inner.Count - 1] < 1f) continue;
                inner.Add(c);
            }
            var pieces = new List<Piece>();
            if (inner.Count == 0) { pieces.Add(new Piece { Id = es.id, From = es.fromNode, To = es.toNode, A = 0f, B = ge.Length, Spec = es }); return pieces; }
            if (!string.IsNullOrEmpty(es.turnout)) throw new Exception("edge " + es.id + " is a loop line and cannot carry a crossover");

            var bounds = new List<float> { 0f }; bounds.AddRange(inner); bounds.Add(ge.Length);
            string fc = es.fromNode.Length > 0 ? es.fromNode.Substring(0, 1) : "N";
            var nodeIds = new List<string> { es.fromNode };
            for (int i = 1; i < bounds.Count - 1; i++)
            {
                string nid = fc + "~" + es.id + "@" + Mathf.RoundToInt(bounds[i]);
                var p = ge.PosAt(bounds[i]);
                w.nodes.Add(new NodeSpec { id = nid, x = p.X, z = p.Z, type = "plain" });
                nodeIds.Add(nid);
            }
            nodeIds.Add(es.toNode);

            int longest = 0;
            for (int i = 1; i < bounds.Count - 1; i++) if (bounds[i + 1] - bounds[i] > bounds[longest + 1] - bounds[longest]) longest = i;
            for (int i = 0; i < bounds.Count - 1; i++)
            {
                float a = bounds[i], b = bounds[i + 1];
                string pid = i == longest ? es.id : es.id + "~" + i;
                var ps = new EdgeSpec
                {
                    id = pid, fromNode = nodeIds[i], toNode = nodeIds[i + 1], turnout = "", via = ViaOf(ge, a, b, false, es),
                    gradientPermil = es.gradientPermil, curveRadiusM = es.curveRadiusM, speedLimitKmh = es.speedLimitKmh,
                    baseId = es.baseId, baseStart = es.baseStart + a, dir = es.dir
                };
                pieces.Add(new Piece { Id = pid, From = ps.fromNode, To = ps.toNode, A = a, B = b, Spec = ps });
            }
            int ix = w.edges.IndexOf(es);
            w.edges.RemoveAt(ix);
            var specs = new List<EdgeSpec>(); foreach (var p in pieces) specs.Add(p.Spec);
            w.edges.InsertRange(ix, specs);

            foreach (var st in w.stations) if (st.edge == es.id) Remap(pieces, ref st.edge, ref st.offsetM);
            foreach (var sg in w.signals) if (sg.edge == es.id) Remap(pieces, ref sg.edge, ref sg.offsetM);
            if (w.start != null && w.start.edge == es.id) Remap(pieces, ref w.start.edge, ref w.start.offsetM);
            if (w.ai != null && w.ai.edge == es.id) Remap(pieces, ref w.ai.edge, ref w.ai.offsetM);
            return pieces;
        }

        static void Remap(List<Piece> pieces, ref string edge, ref float off)
        {
            Piece hit = pieces[pieces.Count - 1];
            foreach (var p in pieces) if (off < p.B) { hit = p; break; }
            edge = hit.Id; off -= hit.A;
        }

        static EdgeSpec Twin(NetworkSpec w, TrackGraph g, Piece p, char pieceDir)
        {
            var o = p.Spec; 
            EnsureTwinNode(w, p.From); EnsureTwinNode(w, p.To);
            var via = new List<List<float>>();
            for (int i = o.via.Count - 1; i >= 0; i--) via.Add(new List<float>(o.via[i]));
            return new EdgeSpec
            {
                id = TwEdge(p.Id), fromNode = TwNode(p.To), toNode = TwNode(p.From), turnout = "", via = via,
                gradientPermil = -o.gradientPermil, curveRadiusM = o.curveRadiusM, speedLimitKmh = o.speedLimitKmh,
                virt = true, twinOf = p.Id, dir = Flip(pieceDir).ToString(), baseId = TwEdge(o.baseId), baseStart = o.baseStart
            };
        }

        static string NodeAt(List<Piece> pieces, float local)
        {
            foreach (var p in pieces)
            {
                if (Mathf.Abs(local - p.A) < 0.6f) return p.From;
                if (Mathf.Abs(local - p.B) < 0.6f) return p.To;
            }
            throw new Exception("no node at " + local);
        }

        static void Place(List<Piece> pieces, float local, bool rev, out string edge, out float off)
        {
            Piece hit = pieces[pieces.Count - 1];
            local = Mathf.Clamp(local, pieces[0].A + 1f, pieces[pieces.Count - 1].B - 1f);
            foreach (var p in pieces) if (local <= p.B) { hit = p; break; }
            if (!rev) { edge = hit.Id; off = local - hit.A; }
            else { edge = TwEdge(hit.Id); off = hit.B - local; }
        }

        // ------------------------------------------------------------------------------------------------ one limit
        static void ExpandOne(NetworkSpec w, LimitSpec lim)
        {
            var g = TrackGraph.Build(w);
            string id = string.IsNullOrEmpty(lim.id) ? "L" : lim.id;

            float dTo = lim.downToM, uTo = lim.upToM, dBase, uBase;
            EdgeSpec dEs = FindPiece(w, g, lim.downEdge, lim.downFromM, ref dTo, out dBase);
            EdgeSpec uEs = FindPiece(w, g, lim.upEdge, lim.upFromM, ref uTo, out uBase);
            TEdge de = g.ById[dEs.id], ue = g.ById[uEs.id];
            float dF = lim.downFromM - dBase, dT = dTo - dBase, uF = lim.upFromM - uBase, uT = uTo - uBase;
            if (dT <= dF || uT <= uF) throw new Exception("empty limit");
            char dDir = de.Dir, uDir = ue.Dir;
            if (dDir == uDir) throw new Exception("down and up edges run the same way");

            float T = dT - dF, Lx = lim.crossoverLengthM, gap = lim.crossoverGapM, m = lim.endMarginM, set = lim.signalSetbackM;
            if (T < 2f * m + 4f * Lx + 2f * gap + 60f) throw new Exception("limit too short for its crossovers (" + Mathf.RoundToInt(T) + " m)");

            Func<float, float> clampT = t => Mathf.Clamp(t, 1f, T - 1f);
            Func<float, Vector3> Dp = t => de.PosAt(dF + clampT(t));
            var uoCache = new Dictionary<int, float>();
            Func<float, float> Uo = t =>
            {
                int key = Mathf.RoundToInt(t * 10f); float v;
                if (!uoCache.TryGetValue(key, out v)) { v = NearestOffset(ue, uF, uT, de.PosAt(dF + Mathf.Clamp(t, 0f, T))); uoCache[key] = v; }
                return v;
            };
            Func<float, Vector3> Up = t => ue.PosAt(Uo(t));

            // crossover attach points, as distance t along the limit measured on the Down line
            float tAn = m, tAnU = m + Lx;
            float tBnU = m + Lx + gap, tBn = m + 2f * Lx + gap;
            float tBsU = T - m - 2f * Lx - gap, tBs = T - m - Lx - gap;
            float tAsU = T - m, tAs = T - m - Lx;

            var dCuts = new List<float> { dF, dT, dF + tAn, dF + tBn, dF + tBs, dF + tAs };
            var uCuts = new List<float> { uF, uT, Uo(tAnU), Uo(tBnU), Uo(tBsU), Uo(tAsU) };

            var dPieces = Cut(w, g, dEs, dCuts);
            // the Up line may be cut after the Down line was: rebuild the geometry lookup for it (its own edge is untouched by the cuts above)
            var uPieces = Cut(w, g, uEs, uCuts);

            // ---- hidden twins inside the limit, main twins first (so MAIN stays the first way out of every twin switch)
            var twins = new List<EdgeSpec>();
            foreach (var p in dPieces) if (p.A >= dF - 0.6f && p.B <= dT + 0.6f) twins.Add(Twin(w, g, p, dDir));
            foreach (var p in uPieces) if (p.A >= uF - 0.6f && p.B <= uT + 0.6f) twins.Add(Twin(w, g, p, uDir));
            foreach (var xid in lim.extraEdges)
            {
                EdgeSpec xs = null; foreach (var e in w.edges) if (e.id == xid) { xs = e; break; }
                if (xs == null) throw new Exception("extra edge " + xid + " not found");
                TEdge xg = g.ById[xid];
                var xp = new Piece { Id = xid, From = xs.fromNode, To = xs.toNode, A = 0f, B = xg.Length, Spec = xs };
                twins.Add(Twin(w, g, xp, xg.Dir));
            }
            w.edges.AddRange(twins);

            // ---- the four crossovers (one for each direction at each end)
            string xo = "xo_" + id + "_";
            AddCrossover(w, de, ue, Dp, Up, xo + "an", NodeAt(dPieces, dF + tAn), TwNode(NodeAt(uPieces, Uo(tAnU))), tAn, tAnU, true, lim, dDir);
            AddCrossover(w, de, ue, Dp, Up, xo + "bn", TwNode(NodeAt(dPieces, dF + tBn)), NodeAt(uPieces, Uo(tBnU)), tBn, tBnU, true, lim, uDir);
            AddCrossover(w, de, ue, Dp, Up, xo + "bs", TwNode(NodeAt(uPieces, Uo(tBsU))), NodeAt(dPieces, dF + tBs), tBsU, tBs, false, lim, dDir);
            AddCrossover(w, de, ue, Dp, Up, xo + "as", NodeAt(uPieces, Uo(tAsU)), TwNode(NodeAt(dPieces, dF + tAs)), tAsU, tAs, false, lim, uDir);

            // ---- guard signals, both directions, at the limit entry / exit and at both ends of every crossover
            Action<string, List<Piece>, float, bool, char> Sig = (sid, pieces, local, rev, dir) =>
            {
                string se; float so; Place(pieces, local, rev, out se, out so);
                w.signals.Add(new NetSignalSpec { id = sid, edge = se, offsetM = so, held = false, holdSeconds = 20f, dir = dir.ToString(), guard = true });
            };
            Sig(id + "-DI", dPieces, Mathf.Max(dF - set, 1f), false, dDir);                 // Down line: entry
            Sig(id + "-DO", dPieces, dT - 20f, false, dDir);                                 // Down line: exit
            Sig(id + "-UI", uPieces, Mathf.Max(uF - set, 1f), false, uDir);                 // Up line: entry
            Sig(id + "-UO", uPieces, uT - 20f, false, uDir);                                 // Up line: exit
            Sig(id + "-X1A", dPieces, dF + tAn - set, false, dDir);                          // X1: Down main -> Up main (twin)
            Sig(id + "-X1B", uPieces, Uo(tAnU + 25f), true, dDir);
            Sig(id + "-X2A", dPieces, dF + tBn + set, true, uDir);                           // X2: Down main (twin) -> Up main
            Sig(id + "-X2B", uPieces, Uo(tBnU - 25f), false, uDir);
            Sig(id + "-X3A", uPieces, Uo(tBsU - set), true, dDir);                           // X3: Up main (twin) -> Down main
            Sig(id + "-X3B", dPieces, dF + tBs + 25f, false, dDir);
            Sig(id + "-X4A", uPieces, Uo(tAsU + set), false, uDir);                          // X4: Up main -> Down main (twin)
            Sig(id + "-X4B", dPieces, dF + tAs - 25f, true, uDir);
        }

        // One crossover edge between the two main lines. tFrom / tTo are distances along the limit (Down-line axis); fromIsDown says which line it leaves.
        static void AddCrossover(NetworkSpec w, TEdge de, TEdge ue, Func<float, Vector3> Dp, Func<float, Vector3> Up, string eid, string fromNode, string toNode,
                                 float tFrom, float tTo, bool fromIsDown, LimitSpec lim, char dir)
        {
            int n = Mathf.Max(6, Mathf.RoundToInt(Mathf.Abs(tTo - tFrom) / 8f));
            var via = new List<List<float>>();
            for (int k = 1; k < n; k++)
            {
                float u = k / (float)n, t = tFrom + u * (tTo - tFrom), wgt = u * u * (3f - 2f * u);       // smooth S-curve from one line to the other
                Vector3 a = fromIsDown ? Dp(t) : Up(t), b = fromIsDown ? Up(t) : Dp(t);
                Vector3 p = a * (1f - wgt) + b * wgt;
                via.Add(new List<float> { p.X, p.Z });
            }
            w.edges.Add(new EdgeSpec
            {
                id = eid, fromNode = fromNode, toNode = toNode, turnout = "R", via = via, gradientPermil = de.Gradient, curveRadiusM = 800f,
                speedLimitKmh = lim.crossoverLimitKmh, crossover = true, dir = dir.ToString(), baseId = eid, baseStart = 0f
            });
        }
    }
}
