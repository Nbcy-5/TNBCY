using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // v62: seven platforms (1 to 7) with one track between neighbours; the two main lines are the only pair of tracks between platforms.\n    // v61: platforms are built to fit the tracks.
    //  - A platform that has a track on both sides is an island: it fills the exact space between the two tracks,
    //    1.70 m from each track centre (Indian BG standard), with a finished edge (coping, red line, tactile band) on BOTH faces and no fence on the track side.
    //  - A platform with no track on its far side is a normal side platform, 7 m wide, with a safety railing at the back.
    //  - Width is measured from the real track geometry, so there are no gaps and no overlaps whatever the track spacing is.
    //  - Every station now uses the same realistic platform (paved top, coping, tactile band, shelter, benches, lights).
    public partial class Main
    {
        const float PfMaxGap = 16f;     // look for a neighbouring track up to this far from the track centre

        static Vector3 PfClosest(Vector3 q, List<Vector3> pts)
        {
            float best = float.MaxValue; Vector3 bp = new Vector3(pts[0].X, 0f, pts[0].Z);
            Vector3 qq = new Vector3(q.X, 0f, q.Z);
            for (int k = 0; k + 1 < pts.Count; k++)
            {
                Vector3 a = new Vector3(pts[k].X, 0f, pts[k].Z), b = new Vector3(pts[k + 1].X, 0f, pts[k + 1].Z), ab = b - a, aq = qq - a;
                float l2 = ab.LengthSquared(), u = l2 > 1e-6f ? Mathf.Clamp(aq.Dot(ab) / l2, 0f, 1f) : 0f;
                Vector3 c = a + ab * u; float d2 = (qq - c).LengthSquared();
                if (d2 < best) { best = d2; bp = c; }
            }
            return bp;
        }

        // Sideways distance from the track centre at (e, s) to the nearest other track running alongside on side sm; -1 when there is none.
        float PfNeighbour(TEdge e, float s, float sm, out TEdge nb)
        {
            nb = null; float best = -1f;
            Vector3 p = e.PosAt(s), d = e.DirAt(s), rr = Right(d) * sm; p.Y = 0f;
            foreach (var o in sc.Graph.Edges.Values)
            {
                if (o == e || o.Pts.Count < 2) continue;
                Vector3 v = PfClosest(p, o.Pts) - p; v.Y = 0f;
                float lat = v.Dot(rr), along = v.Dot(d);
                if (lat < 2.5f || lat > PfMaxGap || Mathf.Abs(along) > 4f) continue;
                if (best < 0f || lat < best) { best = lat; nb = o; }
            }
            return best;
        }

        static bool PfPair(string x, string y, string p, string q) { return (x == p && y == q) || (x == q && y == p); }

        // Platform numbers run from the station building side: 1 is the side platform beside the outer down-side loop track, then the island platforms
        // 2 and 3 (between the down-side loop tracks and the down main), 4 to 7 (between the up main and the up-side yard tracks).
        // Exactly one track runs between two neighbouring platforms, except where the two main lines run side by side (between 3 and 4).
        static string PfName(TEdge a, TEdge b)
        {
            string x = a.Id;
            if (b == null) return x == "sc_yard_x2" ? "PLATFORM 1" : null;
            string y = b.Id;
            if (PfPair(x, y, "sc_yard_x2", "sc_yard_x1")) return "PLATFORM 2";
            if (PfPair(x, y, "sc_yard_x1", "d0")) return "PLATFORM 3";
            if (PfPair(x, y, "u17", "sc_yard_a")) return "PLATFORM 4";
            if (PfPair(x, y, "sc_yard_a", "sc_yard_b")) return "PLATFORM 5";
            if (PfPair(x, y, "sc_yard_b", "sc_yard_c")) return "PLATFORM 6";
            if (PfPair(x, y, "sc_yard_c", "sc_yard_d")) return "PLATFORM 7";
            return null;
        }

        // The foot-over-bridge stairs come down here: no roof, lights or benches.
        bool PfFobZone(Vector3 p)
        {
            if (!secFobOk) return false;
            Vector3 v = p - secFobC; v.Y = 0f;
            float along = v.Dot(secFobD), lat = Mathf.Abs(v.Dot(Right(secFobD)));
            return along > -4.5f && along < 18f && lat < 11.5f;
        }

        // One 40 m piece of a platform. w = width, far = a track runs along the far edge too.
        void PfPiece(TEdge e, float s0, float e1, float sm, float w, bool far, bool endPiece, string label, TStation secSt)
        {
            if (secSt != null) RTSecInitFob(secSt);
            float len = e1 - s0, mid = (s0 + e1) * .5f; secIdx++;
            Vector3 pp = e.PosAt(mid), dd = e.DirAt(mid), up = Vector3.Up; Basis qq = Basis.LookingAt(dd, up); Vector3 rr = Right(dd) * sm;
            float L = len + .2f, xe = SecEdge, xc = xe + w * .5f, xf = xe + w;
            float topA = xe + .4f, topB = far ? xf - .4f : xf;
            secBody.Add(Xf(qq, pp + rr * xc + up * .70f, new Vector3(w, 1.40f, L)));
            secTopL.Add(Xf(qq, pp + rr * ((topA + topB) * .5f) + up * 1.44f, new Vector3(topB - topA, .04f, L)));
            secCop.Add(Xf(qq, pp + rr * (xe + .2f) + up * 1.455f, new Vector3(.40f, .07f, L)));
            secRed.Add(Xf(qq, pp + rr * (xe + .06f) + up * 1.495f, new Vector3(.10f, .012f, L)));
            secTac.Add(Xf(qq, pp + rr * (xe + .75f) + up * 1.468f, new Vector3(.50f, .016f, L)));
            if (far)
            {
                secCop.Add(Xf(qq, pp + rr * (xf - .2f) + up * 1.455f, new Vector3(.40f, .07f, L)));
                secRed.Add(Xf(qq, pp + rr * (xf - .06f) + up * 1.495f, new Vector3(.10f, .012f, L)));
                secTac.Add(Xf(qq, pp + rr * (xf - .75f) + up * 1.468f, new Vector3(.50f, .016f, L)));
            }
            else
            {
                secRail.Add(Xf(qq, pp + rr * (xf - .15f) + up * (SecTop + .95f), new Vector3(.05f, .05f, L)));
                secRail.Add(Xf(qq, pp + rr * (xf - .15f) + up * (SecTop + .50f), new Vector3(.04f, .04f, L)));
                for (float u = s0 + 1.5f; u < e1; u += 3f)
                {
                    Vector3 pu = e.PosAt(u), du = e.DirAt(u); Basis qu = Basis.LookingAt(du, up); Vector3 ru = Right(du) * sm;
                    secPost.Add(Xf(qu, pu + ru * (xf - .15f) + up * (SecTop + .5f), new Vector3(.06f, 1.0f, .06f)));
                }
            }
            for (float u = s0 + 5f; u < e1; u += 10f)
            {
                Vector3 pu = e.PosAt(u), ru = Right(e.DirAt(u)) * sm;
                fxPlat.Add(pu + ru * xc); fxPlat.Add(pu + ru * (xe + w * .25f)); fxPlat.Add(pu + ru * (xe + w * .75f));      // keeps signs off the platform
            }
            if (endPiece) return;

            float rA, rB;
            if (far) { float rw = Mathf.Clamp(w - 3.0f, 1.6f, 5.4f); rA = xc - rw * .5f; rB = xc + rw * .5f; }
            else { rA = xe + 2.45f; rB = xf - .4f; }
            float rW = rB - rA, rC = (rA + rB) * .5f;

            for (float u = s0 + 3f; u < e1; u += 6f)
            {
                Vector3 pu = e.PosAt(u), du = e.DirAt(u); Basis qu = Basis.LookingAt(du, up); Vector3 ru = Right(du) * sm;
                if (PfFobZone(pu + ru * xc)) continue;
                secLight.Add(Xf(qu, pu + ru * rC + up * 5.02f, new Vector3(.14f, .07f, 1.3f)));
            }
            if (PfFobZone(pp + rr * xc)) return;

            secRoof.Add(Xf(qq, pp + rr * rC + up * 5.2f, new Vector3(rW, .12f, L)));
            for (float u = s0 + 5f; u < e1; u += 10f)
            {
                Vector3 pu = e.PosAt(u), du = e.DirAt(u); Basis qu = Basis.LookingAt(du, up); Vector3 ru = Right(du) * sm;
                secBeam.Add(Xf(qu, pu + ru * rC + up * 5.0f, new Vector3(rW, .20f, .18f)));
                secCol.Add(Xf(qu, pu + ru * (rB - .2f) + up * ((SecTop + 5.14f) * .5f), new Vector3(.28f, 5.14f - SecTop, .28f)));
                if (far) secCol.Add(Xf(qu, pu + ru * (rA + .2f) + up * ((SecTop + 5.14f) * .5f), new Vector3(.28f, 5.14f - SecTop, .28f)));
            }
            if (len >= 25f)
            {
                float bs = s0 + len * .5f + 6f;
                Vector3 pb = e.PosAt(bs), db = e.DirAt(bs); Basis qb = Basis.LookingAt(db, up); Vector3 rb = Right(db) * sm;
                if (!PfFobZone(pb + rb * xc))
                {
                    float xs = far ? xc : xe + 2.9f;
                    for (int k = 0; k < 2; k++)
                    {
                        Vector3 bc = pb + qb.Z * (k == 0 ? -4.4f : 4.4f) + rb * xs;
                        secSeat.Add(Xf(qb, bc + up * (SecTop + .46f), new Vector3(.50f, .06f, 2.2f)));
                        secBack.Add(Xf(qb, bc + rb * .27f + up * (SecTop + .80f), new Vector3(.06f, .50f, 2.2f)));
                        secLeg.Add(Xf(qb, bc + qb.Z * .9f + up * (SecTop + .23f), new Vector3(.40f, .46f, .08f)));
                        secLeg.Add(Xf(qb, bc - qb.Z * .9f + up * (SecTop + .23f), new Vector3(.40f, .46f, .08f)));
                    }
                }
            }
            if (label != null && secIdx % 2 == 0)
            {
                Vector3 bp = pp + rr * xc + up * 4.2f;
                secSign.Add(Xf(qq, bp, new Vector3(Mathf.Min(2.6f, w - 1.0f), 1.0f, .08f)));
                FxInit();
                var r1 = FxRoot(Basis.LookingAt(dd, up), bp - dd * .05f); var r2 = FxRoot(Basis.LookingAt(-dd, up), bp + dd * .05f);
                float em = label.Length > 12 ? .34f : .5f;
                Far(FxText(r1, label, Vector3.Zero, em, Colors.White), 160f);
                Far(FxText(r2, label, Vector3.Zero, em, Colors.White), 160f);
            }
        }

        // Three-step ramp at a platform end.
        void PfRamp(TEdge e, float s, float dirSign, float sm, float w)
        {
            Vector3 p = e.PosAt(s), d = e.DirAt(s) * dirSign; Basis q = Basis.LookingAt(e.DirAt(s), Vector3.Up); Vector3 rr = Right(e.DirAt(s)) * sm;
            for (int k = 0; k < 3; k++)
            {
                float ht = 1.10f - .35f * k;
                secBody.Add(Xf(q, p + d * (2f + 4f * k) + rr * (SecEdge + w * .5f) + Vector3.Up * (ht * .5f), new Vector3(w, ht, 4f)));
            }
        }

        // Replaces BuildStations (MainWorld.cs). Platforms are 620 m long and follow the track, even across edge joins.
        void PfBuildStations()
        {
            const float PlatLen = 620f, Piece = 40f;
            var brick = Mat(new Color(.55f, .3f, .24f)); var rf2 = Mat(new Color(.3f, .3f, .32f));
            fxPlat.Clear();
            var built = new HashSet<string>();
            foreach (var st in sc.Graph.Stations)
            {
                try
                {
                    bool sec = RTIsSec(st); float w0 = SecW;
                    for (int pass = 0; pass < 2; pass++)       // pass 0: the station's own side, pass 1: the other side (only if a track runs there too)
                    {
                        float sm = (st.Side == "L" ? -1f : 1f) * (pass == 0 ? 1f : -1f);
                        float midS = Mathf.Clamp(st.Offset - PlatLen * .5f, 1f, Mathf.Max(1f, st.Edge.Length - 1f));
                        TEdge nb; float lat = PfNeighbour(st.Edge, midS, sm, out nb);
                        float w = SecW; bool far = false;
                        if (lat > 0f && nb != null && lat - 2f * SecEdge <= 12f)
                        {
                            w = lat - 2f * SecEdge;
                            if (w < 2.5f) continue;                   // tracks too close together for a platform (the two main lines)
                            string key = string.CompareOrdinal(st.Edge.Id, nb.Id) < 0 ? st.Edge.Id + "|" + nb.Id : nb.Id + "|" + st.Edge.Id;
                            if (!built.Add(key)) continue;            // already built from the track on the other side of it
                            far = true;
                        }
                        else if (pass == 1) continue;
                        if (pass == 0) w0 = w;
                        string label = sec ? PfName(st.Edge, far ? nb : null) : null;

                        var e = st.Edge; float hi = Mathf.Min(e.Length, st.Offset + 10f), remain = PlatLen;
                        for (int guard = 0; remain > 1f && e != null && guard < 6; guard++)
                        {
                            float lo = Mathf.Max(0f, hi - remain);
                            for (float s0 = lo; s0 < hi - 1f; s0 += Piece)
                            {
                                float e1 = Mathf.Min(s0 + Piece, hi);
                                PfPiece(e, s0, e1, sm, w, far, e1 >= hi - .5f || s0 <= lo + .5f, label, sec ? st : null);
                            }
                            if (guard == 0) PfRamp(e, hi, 1f, sm, w);
                            if (lo > 0f || remain - (hi - lo) <= 1f || PredOf(e) == null) PfRamp(e, lo, -1f, sm, w);
                            remain -= hi - lo;
                            if (lo > 0f) break;
                            e = PredOf(e); if (e != null) hi = e.Length;
                        }
                    }
                    if (sec && (st.Edge.Id == "sc_yard_x2" || (st.Edge.Id == "d0" && !sc.Graph.Edges.ContainsKey("sc_yard_x2"))))
                    {
                        // The main station building stands directly behind Platform 1 (the outermost platform).
                        float cs = Mathf.Max(0f, st.Offset - 150f), sm0 = st.Side == "L" ? -1f : 1f;
                        var p = st.Edge.PosAt(cs); var d = st.Edge.DirAt(cs); var q = Basis.LookingAt(d, Vector3.Up); var r = Right(d) * sm0;
                        float bx = SecEdge + w0 + 4.6f;
                        Box(this, p + r * bx + Vector3.Up * 3f, q, new Vector3(9f, 6f, 70f), brick);
                        Box(this, p + r * bx + Vector3.Up * 6.3f, q, new Vector3(10f, .6f, 72f), rf2);
                        Tag("Secunderabad Jn (SC)", p + r * 3.5f + Vector3.Up * 8f, Colors.Yellow);
                    }
                }
                catch (System.Exception ex) { GD.PrintErr("Platform build failed at " + st.Name + ": " + ex.Message); }
            }
            RTSecFinish();
        }
    }
}
