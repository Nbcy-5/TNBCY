using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // v5.23: the Secunderabad main station building (MainSecBuilding.cs) stands behind Platform 1; the platform railing leaves a gap in front of it.
    // v62: a platform is built only on the side of the track its station stop is on.
    //  - Secunderabad layout: Platform 1 | d0 + u17 | Platform 2 | A + B | Platform 3 | C + D | Platform 4,
    //    so exactly two tracks lie between every pair of platforms (no platform between A/B or C/D).
    // v61: platforms are built to fit the tracks.
    //  - A platform that has a track on both sides is an island: it fills the exact space between the two tracks,
    //    1.70 m from each track centre (Indian BG standard), with a finished edge (coping, red line, tactile band) on BOTH faces and no fence on the track side.
    //  - A platform with no track on its far side is a normal side platform, 7 m wide, with a safety railing at the back.
    //  - Width is measured from the real track geometry, so there are no gaps and no overlaps whatever the track spacing is.
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

        // Platform number of the track's face: P1 d0 | d0 u17 | P2 u17 A | A B | P3 B C | C D | P4 D
        static int PfNo(string id)
        {
            switch (id)
            {
                case "d0": return 1;
                case "u17": return 2;
                case "sc_yard_a": return 2;
                case "sc_yard_b": case "sc_yard_c": return 3;
                case "sc_yard_d": return 4;
                default: return 0;
            }
        }

        static string PfLabel(TEdge a, TEdge b)
        {
            int x = PfNo(a.Id), y = b == null ? 0 : PfNo(b.Id);
            if (x == 0) x = y;
            if (y == 0) y = x;
            if (x == 0) return null;
            return x == y ? "PLATFORM " + x : "PLATFORM " + System.Math.Min(x, y) + " / " + System.Math.Max(x, y);
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
            secRed.Add(Xf(qq, pp + rr * (xe + .12f) + up * 1.495f, new Vector3(.20f, .014f, L)));
            secTac.Add(Xf(qq, pp + rr * (xe + .75f) + up * 1.468f, new Vector3(.50f, .016f, L)));
            if (far)
            {
                secCop.Add(Xf(qq, pp + rr * (xf - .2f) + up * 1.455f, new Vector3(.40f, .07f, L)));
                secRed.Add(Xf(qq, pp + rr * (xf - .12f) + up * 1.495f, new Vector3(.20f, .014f, L)));
                secTac.Add(Xf(qq, pp + rr * (xf - .75f) + up * 1.468f, new Vector3(.50f, .016f, L)));
            }
            else
            {
                SbmRailRun(e, s0, e1, sm, xf - .15f);      // v5.23: the railing stops in front of the station building (doors open onto the platform)
                for (float u = s0 + 1.5f; u < e1; u += 3f)
                {
                    if (SbmInZone(e, u)) continue;
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
            if (far) { float rw = Mathf.Clamp(w - 1.2f, 1.6f, 6.0f); rA = xc - rw * .5f; rB = xc + rw * .5f; }
            else { rA = xe + .8f; rB = xf - .35f; }
            float rW = rB - rA, rC = (rA + rB) * .5f;

            for (float u = s0 + 3f; u < e1; u += 6f)
            {
                Vector3 pu = e.PosAt(u), du = e.DirAt(u); Basis qu = Basis.LookingAt(du, up); Vector3 ru = Right(du) * sm;
                if (PfFobZone(pu + ru * xc)) continue;
                secLight.Add(Xf(qu, pu + ru * rC + up * 5.02f, new Vector3(.14f, .07f, 1.3f)));
            }
            if (PfFobZone(pp + rr * xc)) return;

            // Indian railway-style gabled canopy: two shallow pitched sheets meet at a raised ridge.
            // The roof stays within the platform footprint, with deep corrugated fascia and a dark underside.
            float halfSpan = rW * .5f;
            float pitch = Mathf.Atan2(.42f, Mathf.Max(1f, halfSpan));
            Basis qL = qq * new Basis(Vector3.Back, pitch), qR = qq * new Basis(Vector3.Back, -pitch);
            Vector3 leftRoof = pp + rr * (rA + halfSpan * .5f) + up * 5.41f;
            Vector3 rightRoof = pp + rr * (rC + halfSpan * .5f) + up * 5.41f;
            secRoof.Add(Xf(qL, leftRoof, new Vector3(halfSpan + .12f, .12f, L)));
            secRoof.Add(Xf(qR, rightRoof, new Vector3(halfSpan + .12f, .12f, L)));
            // Deep blue folded fascia gives the canopy the substantial edge profile seen at Indian stations.
            secRoof.Add(Xf(qq, pp + rr * rA + up * 4.88f, new Vector3(.16f, .64f, L)));
            secRoof.Add(Xf(qq, pp + rr * rB + up * 4.88f, new Vector3(.16f, .64f, L)));
            // Fine raised seams follow each roof slope instead of running flat across the canopy.
            for (float rx = rA + .18f; rx < rC - .08f; rx += .42f)
            {
                float t = (rx - rA) / halfSpan;
                secRib.Add(Xf(qL, pp + rr * rx + up * (5.20f + .42f * t + .075f), new Vector3(.045f, .035f, L)));
            }
            for (float rx = rC + .18f; rx < rB - .08f; rx += .42f)
            {
                float t = (rx - rC) / halfSpan;
                secRib.Add(Xf(qR, pp + rr * rx + up * (5.62f - .42f * t + .075f), new Vector3(.045f, .035f, L)));
            }
            for (float u = s0 + 5f; u < e1; u += 10f)
            {
                Vector3 pu = e.PosAt(u), du = e.DirAt(u); Basis qu = Basis.LookingAt(du, up); Vector3 ru = Right(du) * sm;
                secBeam.Add(Xf(qu, pu + ru * rC + up * 5.0f, new Vector3(rW, .24f, .22f)));
                secCol.Add(Xf(qu, pu + ru * (rB - .2f) + up * ((SecTop + 5.18f) * .5f), new Vector3(.34f, 5.18f - SecTop, .34f)));
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

        // Smooth, tapered concrete ramp at each platform end. A continuous mesh replaces the old three visible steps.
        static void PfRampTri(List<Vector3> v, List<Vector3> n, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 normal = (b - a).Cross(c - a).Normalized();
            v.Add(a); v.Add(b); v.Add(c); n.Add(normal); n.Add(normal); n.Add(normal);
        }

        void PfRamp(TEdge e, float s, float dirSign, float sm, float w)
        {
            const int segments = 18;
            const float length = 12f, top = 1.40f;
            Vector3 p = e.PosAt(s), d = e.DirAt(s) * dirSign;
            Basis q = Basis.LookingAt(d, Vector3.Up);
            Vector3 rr = Right(e.DirAt(s)) * sm;
            var v = new List<Vector3>(segments * 36);
            var n = new List<Vector3>(segments * 36);
            Vector3[] tl = new Vector3[segments + 1], tr = new Vector3[segments + 1];
            Vector3[] bl = new Vector3[segments + 1], br = new Vector3[segments + 1];

            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float smooth = t * t * (3f - 2f * t);
                float half = w * .5f * (1f - .06f * smooth);
                float z = -length * t;
                float y = top * (1f - smooth);
                tl[i] = new Vector3(-half, y, z); tr[i] = new Vector3(half, y, z);
                bl[i] = new Vector3(-half, 0f, z); br[i] = new Vector3(half, 0f, z);
            }

            for (int i = 0; i < segments; i++)
            {
                // Tiled top, underside and both side faces of the continuous sloping platform nose.
                PfRampTri(v, n, tl[i], tr[i], tr[i + 1]); PfRampTri(v, n, tl[i], tr[i + 1], tl[i + 1]);
                PfRampTri(v, n, bl[i], br[i + 1], br[i]); PfRampTri(v, n, bl[i], bl[i + 1], br[i + 1]);
                PfRampTri(v, n, bl[i], tl[i + 1], tl[i]); PfRampTri(v, n, bl[i], bl[i + 1], tl[i + 1]);
                PfRampTri(v, n, br[i], tr[i], tr[i + 1]); PfRampTri(v, n, br[i], tr[i + 1], br[i + 1]);
            }
            // Close the ground-level nose and the joint hidden inside the platform slab.
            PfRampTri(v, n, bl[segments], br[segments], tr[segments]);
            PfRampTri(v, n, bl[segments], tr[segments], tl[segments]);
            PfRampTri(v, n, tl[0], tr[0], br[0]);
            PfRampTri(v, n, tl[0], br[0], bl[0]);

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = v.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = n.ToArray();
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.48f, .49f, .47f), Roughness = .94f, CullMode = BaseMaterial3D.CullModeEnum.Disabled } };
            AddChild(mi);
            mi.Transform = new Transform3D(q, p + rr * (SecEdge + w * .5f));
        }

        // Replaces BuildStations (MainWorld.cs). Platforms are 620 m long and follow the track, even across edge joins.
        void PfBuildStations()
        {
            const float PlatLen = 620f, Piece = 40f;
            fxPlat.Clear();
            var built = new HashSet<string>();
            foreach (var st in sc.Graph.Stations)
            {
                try
                {
                    bool sec = RTIsSec(st); float w0 = SecW;
                    if (sec && st.Edge.Id == "d0") { float bcz = SbmCentre(st); sbmEdgeId = "d0"; sbmS0 = bcz - SbmHalf; sbmS1 = bcz + SbmHalf; }     // v5.23: where the station building stands
                    for (int pass = 0; pass < 1; pass++)       // v62: only the station's own side (a platform between two tracks is built once, from either track)
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
                        string label = sec ? PfLabel(st.Edge, far ? nb : null) : null;

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
                    if (sec && st.Edge.Id == "d0")
                    {
                        // The main station building stands directly behind Platform 1 (MainSecBuilding.cs).
                        float cs = SbmCentre(st), sm0 = st.Side == "L" ? -1f : 1f;
                        SbmBuild(st.Edge, cs, sm0, SecEdge + w0);
                        Tag(st.Name, st.Edge.PosAt(cs) + Right(st.Edge.DirAt(cs)) * sm0 * 3.5f + Vector3.Up * 8f, Colors.Yellow);
                    }
                }
                catch (System.Exception ex) { GD.PrintErr("Platform build failed at " + st.Name + ": " + ex.Message); }
            }
            RTSecFinish();
        }
    }
}
