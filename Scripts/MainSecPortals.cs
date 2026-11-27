using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // v5.22: Secunderabad station electrification with portal gantries.
    //  - Every 60 m through the station a steel portal (two masts + lattice cross girder) stands over each group of tracks.
    //    Each track gets its own hanger, insulators, drop tube and registration arm, so all tracks (main lines, yard lines, junction lines) are covered.
    //  - The masts stand 3.0 m outside the outermost track of a group, so no pole ever stands on or between the tracks of a pair.
    //    A mast is only built when it is clear of every track in the graph.
    //  - The old one-mast-per-track cantilevers are left out inside the station area (see RTInPortalZone), because on the 4.5 m yard spacing
    //    they stood in the neighbouring track.
    //  - Portals are left out near the foot-over-bridge and the signals.
    public partial class Main
    {
        const float PortalZ0 = -2235f, PortalZ1 = 150f, PortalStep = 60f, PortalMastOff = 3.0f, PortalMinX = -15f, PortalMaxX = 60f;
        bool portalsDone;

        sealed class PortalTrack { public TEdge E; public float S, X; }

        // true inside the Secunderabad station area (the ordinary single masts are not built there)
        static bool RTInPortalZone(Vector3 p)
        {
            return p.Z > PortalZ0 && p.Z < PortalZ1 && p.X > PortalMinX && p.X < PortalMaxX;
        }

        // Distance along the edge where it crosses the world line z = z0.
        static bool RTPortalCross(TEdge e, float z0, out float s)
        {
            s = 0f;
            for (int k = 0; k + 1 < e.Pts.Count; k++)
            {
                float za = e.Pts[k].Z, zb = e.Pts[k + 1].Z;
                if (Mathf.Abs(zb - za) < 1e-4f) continue;
                if ((za - z0) * (zb - z0) > 0f) continue;
                float u = (z0 - za) / (zb - za);
                s = e.Cum[k] + (e.Cum[k + 1] - e.Cum[k]) * u;
                return true;
            }
            return false;
        }

        float RTPortalClear(float x, float z)
        {
            float best = float.MaxValue; var q = new Vector3(x, 0f, z);
            foreach (var o in sc.Graph.Edges.Values)
                if (o.Pts.Count >= 2) best = Mathf.Min(best, DistToPts(q, o.Pts));
            return best;
        }

        void RTSecPortals()
        {
            if (portalsDone) return;
            portalsDone = true;
            try
            {
                RTOheInit();
                float fobLo = 1e9f, fobHi = -1e9f;
                foreach (var stn in sc.Graph.Stations)
                {
                    if (!RTIsSec(stn) || stn.Edge.Id != "d0") continue;
                    var d0 = stn.Edge;
                    float fs = Mathf.Clamp(MastSpacing * Mathf.Round((stn.Offset - 280f) / MastSpacing), 60f, d0.Length - 60f);
                    float fz = d0.PosAt(fs).Z;
                    fobLo = fz - 26f; fobHi = fz + 26f;      // foot-over-bridge: deck and trusses are at 8 - 10.3 m, so no portal here
                    break;
                }
                var sigZ = new List<float>();
                foreach (var sg in sc.Graph.Signals)
                {
                    var sp = sg.Edge.PosAt(sg.Offset);
                    if (RTInPortalZone(sp)) sigZ.Add(sp.Z);
                }
                int made = 0;
                for (float zc = PortalZ0 + 25f; zc < PortalZ1 - 10f; zc += PortalStep)
                {
                    bool ok = false; float z0 = zc;
                    for (int tryI = 0; tryI < 7 && !ok; tryI++)
                    {
                        z0 = zc + ((tryI + 1) / 2) * 6f * (tryI % 2 == 1 ? 1f : -1f);
                        ok = z0 > PortalZ0 + 5f && z0 < PortalZ1 - 5f && !(z0 > fobLo && z0 < fobHi);
                        if (ok) foreach (var sz in sigZ) if (Mathf.Abs(sz - z0) < 7f) { ok = false; break; }
                    }
                    if (!ok) continue;
                    made += RTBuildPortal(z0);
                }
                GD.Print("Secunderabad portals built: " + made);
            }
            catch (System.Exception ex) { GD.PrintErr("Secunderabad portals failed: " + ex.Message); }
        }

        int RTBuildPortal(float z0)
        {
            var tr = new List<PortalTrack>();
            foreach (var e in sc.Graph.Edges.Values)
            {
                if (e.Pts.Count < 2 || e.Cum.Count != e.Pts.Count) continue;
                float s;
                if (!RTPortalCross(e, z0, out s)) continue;
                Vector3 p = e.PosAt(s);
                if (p.X < PortalMinX || p.X > PortalMaxX) continue;
                if (Mathf.Abs(e.DirAt(s).Z) < .8f) continue;
                tr.Add(new PortalTrack { E = e, S = s, X = p.X });
            }
            if (tr.Count == 0) return 0;
            tr.Sort((a, b) => a.X.CompareTo(b.X));
            int made = 0, i0 = 0;
            while (i0 < tr.Count)
            {
                int i1 = i0;
                while (i1 + 1 < tr.Count && tr[i1 + 1].X - tr[i1].X < 8f) i1++;      // tracks less than 8 m apart share one portal
                made += RTPortalSpan(tr, i0, i1, z0);
                i0 = i1 + 1;
            }
            return made;
        }

        // One portal over tracks i0..i1 of the sorted list.
        int RTPortalSpan(List<PortalTrack> tr, int i0, int i1, float z0)
        {
            float xl = tr[i0].X - PortalMastOff, xr = tr[i1].X + PortalMastOff;
            if (RTPortalClear(xl, z0) < 2.2f || RTPortalClear(xr, z0) < 2.2f) return 0;      // never a pole in a track
            float cx = (xl + xr) * .5f, hl = (xr - xl) * .5f;
            var mesh = BuildMesh(st =>
            {
                Color steel = new Color(.58f, .60f, .62f), gal = new Color(.70f, .72f, .74f), conc = new Color(.60f, .59f, .56f), ins = new Color(.42f, .27f, .18f), dark = new Color(.12f, .12f, .13f);
                const float yb = 8.1f, yt = 8.9f, zp = .28f;
                for (int m = 0; m < 2; m++)
                {
                    float mx = m == 0 ? -hl : hl;
                    AddBoxC(st, new Vector3(mx, .2f, 0f), new Vector3(.8f, .5f, .8f), conc);                       // foundation
                    AddBoxC(st, new Vector3(mx, 4.8f, -.14f), new Vector3(.3f, 9.6f, .026f), steel);                // I-section flanges
                    AddBoxC(st, new Vector3(mx, 4.8f, .14f), new Vector3(.3f, 9.6f, .026f), steel);
                    AddBoxC(st, new Vector3(mx, 4.8f, 0f), new Vector3(.026f, 9.6f, .3f), steel);                   // web
                    AddBoxC(st, new Vector3(mx, 9.65f, 0f), new Vector3(.36f, .08f, .36f), dark);                   // top cap
                    for (int b = 0; b < 4; b++) AddBoxC(st, new Vector3(mx, 1.6f + b * 2f, 0f), new Vector3(.34f, .03f, .34f), dark);
                    AddBoxC(st, new Vector3(mx, (yb + yt) * .5f, 0f), new Vector3(.5f, 1.1f, .8f), dark);           // girder seat
                }
                int nb = Mathf.Max(2, Mathf.CeilToInt(2f * hl / 1.5f)); float dx = 2f * hl / nb;
                for (int pl = -1; pl <= 1; pl += 2)
                {
                    float z = pl * zp;
                    RTBar(st, new Vector3(-hl, yb, z), new Vector3(hl, yb, z), .12f, gal);                        // bottom chord
                    RTBar(st, new Vector3(-hl, yt, z), new Vector3(hl, yt, z), .12f, gal);                        // top chord
                    for (int k = 0; k <= nb; k++)
                    {
                        float x = -hl + k * dx;
                        RTBar(st, new Vector3(x, yb, z), new Vector3(x, yt, z), .07f, steel);
                        if (k < nb)
                        {
                            if ((k & 1) == 0) RTBar(st, new Vector3(x, yb, z), new Vector3(x + dx, yt, z), .06f, steel);
                            else RTBar(st, new Vector3(x, yt, z), new Vector3(x + dx, yb, z), .06f, steel);
                        }
                    }
                }
                for (int k = 0; k <= nb; k++)
                {
                    float x = -hl + k * dx;
                    RTBar(st, new Vector3(x, yt, -zp), new Vector3(x, yt, zp), .05f, steel);
                    RTBar(st, new Vector3(x, yb, -zp), new Vector3(x, yb, zp), .05f, steel);
                }
                for (int t = i0; t <= i1; t++)
                {
                    var pt = tr[t];
                    float hx = pt.X - cx;
                    float mh = RTMessH(pt.E, pt.S);
                    float stg = RTStagger(pt.E, pt.S);
                    float kx = hx + Right(pt.E.DirAt(pt.S)).X * stg;
                    RTBar(st, new Vector3(hx, yb, 0f), new Vector3(hx, 7.95f, 0f), .05f, steel);                    // hanger from the girder
                    for (int q = 0; q < 4; q++) AddBoxC(st, new Vector3(hx, 7.9f - q * .06f, 0f), new Vector3(.15f, .035f, .15f), ins);
                    RTBar(st, new Vector3(hx, 7.62f, 0f), new Vector3(hx, mh, 0f), .03f, steel);                   // down to the messenger wire
                    AddBoxC(st, new Vector3(hx, mh, 0f), new Vector3(.08f, .06f, .08f), dark);
                    RTBar(st, new Vector3(hx, mh, 0f), new Vector3(hx, 6.4f, 0f), .035f, steel);                   // drop tube
                    RTBar(st, new Vector3(hx - .8f, 6.4f, 0f), new Vector3(kx, OheContact + .04f, 0f), .045f, gal);   // registration arm
                    AddBoxC(st, new Vector3(kx, OheContact, 0f), new Vector3(.07f, .07f, .07f), dark);
                }
            });
            var inst = new MeshInstance3D { Mesh = mesh, MaterialOverride = rtMatMast, Position = new Vector3(cx, 0f, z0) };
            AddChild(inst); Far(inst, 1100f);
            return 1;
        }
    }
}
