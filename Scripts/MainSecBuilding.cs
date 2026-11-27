using Godot;

namespace TNBCY
{
    // v5.23: the Secunderabad main station building, standing directly behind Platform 1 (the side platform of the d0 line).
    // A generic Indian junction-station building built from boxes (not a copy of the real one): 84 m long, two storeys.
    //  - Two wings (12 m deep, 10 m to the cornice) and a centre block (16 m deep, 12.6 m to the cornice), cream walls with maroon cornices and pilasters.
    //  - Front (platform side): windows and sills, three doors in the centre, a BOOKING OFFICE door and an ENQUIRY door, a blue name board, a stepped pediment.
    //  - Clock tower in the centre (clock at 9:00, the time the game starts), four roof pavilions, a street-side porch with steps and a paved forecourt.
    // The floor is at platform level (SecTop). The platform railing leaves a gap in front of the building (see PfRailRun in MainPlatforms.cs).
    // Local frame of the mesh: x = away from the track (the platform back edge is at x = xb), y = up, z = along the track; the building is symmetric in z.
    public partial class Main
    {
        const float SbmHalf = 43f;                        // half the building length plus a little: the platform railing is left open this far each side of the centre
        string sbmEdgeId = ""; float sbmS0, sbmS1;       // stretch of an edge in front of the building (set in PfBuildStations before the platforms are built)

        bool SbmInZone(TEdge e, float u) { return e != null && e.Id == sbmEdgeId && u > sbmS0 && u < sbmS1; }

        // Distance along the station's edge where the building is centred.
        float SbmCentre(TStation stn) { return Mathf.Clamp(stn.Offset - 150f, SbmHalf, Mathf.Max(SbmHalf, stn.Edge.Length - SbmHalf)); }

        // Safety railing along the back of a platform; it stops in front of the station building so the doors open straight onto the platform.
        void SbmRailRun(TEdge e, float s0, float s1, float sm, float x)
        {
            if (e.Id != sbmEdgeId || s1 <= sbmS0 || s0 >= sbmS1) { SbmRailSpan(e, s0, s1, sm, x); return; }
            if (s0 < sbmS0) SbmRailSpan(e, s0, sbmS0, sm, x);
            if (s1 > sbmS1) SbmRailSpan(e, sbmS1, s1, sm, x);
        }

        void SbmRailSpan(TEdge e, float a, float b, float sm, float x)
        {
            if (b - a < .05f) return;
            float m = (a + b) * .5f; Vector3 pm = e.PosAt(m), dm = e.DirAt(m); Basis qm = Basis.LookingAt(dm, Vector3.Up); Vector3 rm = Right(dm) * sm; float lm = b - a + .2f;
            secRail.Add(Xf(qm, pm + rm * x + Vector3.Up * (SecTop + .95f), new Vector3(.05f, .05f, lm)));
            secRail.Add(Xf(qm, pm + rm * x + Vector3.Up * (SecTop + .50f), new Vector3(.04f, .04f, lm)));
        }

        // ---------------------------------------------------------------- mesh helpers (boxes given by their min / max extents)
        static void SbmBox(SurfaceTool t, float xa, float xz, float ya, float yz, float za, float zz, Color c)
        {
            AddBoxC(t, new Vector3((xa + xz) * .5f, (ya + yz) * .5f, (za + zz) * .5f), new Vector3(xz - xa, yz - ya, zz - za), c);
        }

        // Window on a wall face at x = xf. dir = -1: the window sticks out toward the track (front), +1: away from it (street side).
        static void SbmWin(SurfaceTool t, float xf, float dir, float zc, float y1, float y2, Color frame, Color glass, Color bar)
        {
            float a = xf, b = xf + dir * .12f, g0 = xf + dir * .10f, g1 = xf + dir * .15f, m1 = xf + dir * .17f, s1 = xf + dir * .30f;
            float ym = (y1 + y2) * .5f;
            SbmBox(t, Mathf.Min(a, b), Mathf.Max(a, b), y1 - .15f, y2 + .15f, zc - 1.2f, zc + 1.2f, frame);                  // frame
            SbmBox(t, Mathf.Min(g0, g1), Mathf.Max(g0, g1), y1, y2, zc - .95f, zc + .95f, glass);                              // glass
            SbmBox(t, Mathf.Min(g1, m1), Mathf.Max(g1, m1), y1, y2, zc - .04f, zc + .04f, bar);                                // vertical bar
            SbmBox(t, Mathf.Min(g1, m1), Mathf.Max(g1, m1), ym - .04f, ym + .04f, zc - .95f, zc + .95f, bar);                  // horizontal bar
            SbmBox(t, Mathf.Min(a, s1), Mathf.Max(a, s1), y1 - .23f, y1 - .15f, zc - 1.35f, zc + 1.35f, frame);                // sill
        }

        // Double door with a white surround and a fanlight; yf = floor level.
        static void SbmDoor(SurfaceTool t, float xf, float dir, float zc, float yf, Color trim, Color leaf, Color glass)
        {
            float a = xf, b = xf + dir * .16f, l0 = xf + dir * .10f, l1 = xf + dir * .20f, m1 = xf + dir * .22f;
            SbmBox(t, Mathf.Min(a, b), Mathf.Max(a, b), yf, yf + 3.9f, zc - 1.65f, zc + 1.65f, trim);                          // surround
            SbmBox(t, Mathf.Min(l0, l1), Mathf.Max(l0, l1), yf, yf + 3.1f, zc - 1.3f, zc + 1.3f, leaf);                        // door leaves
            SbmBox(t, Mathf.Min(l0, l1), Mathf.Max(l0, l1), yf + 3.2f, yf + 3.65f, zc - 1.3f, zc + 1.3f, glass);               // fanlight
            SbmBox(t, Mathf.Min(l1, m1), Mathf.Max(l1, m1), yf, yf + 3.65f, zc - .04f, zc + .04f, trim);                       // line between the leaves
        }

        // Small roof pavilion (chhatri): four pillars, a slab and a stepped dome.
        static void SbmChhatri(SurfaceTool t, float cx, float cz, float y0, Color col, Color dome, Color top)
        {
            for (int i = -1; i <= 1; i += 2)
                for (int k = -1; k <= 1; k += 2)
                    SbmBox(t, cx + i * 1.4f - .17f, cx + i * 1.4f + .17f, y0, y0 + 1.8f, cz + k * 1.4f - .17f, cz + k * 1.4f + .17f, col);
            SbmBox(t, cx - 1.7f, cx + 1.7f, y0 + 1.8f, y0 + 2.1f, cz - 1.7f, cz + 1.7f, col);
            SbmBox(t, cx - 1.1f, cx + 1.1f, y0 + 2.1f, y0 + 2.6f, cz - 1.1f, cz + 1.1f, dome);
            SbmBox(t, cx - .6f, cx + .6f, y0 + 2.6f, y0 + 3.1f, cz - .6f, cz + .6f, dome);
            SbmBox(t, cx - .1f, cx + .1f, y0 + 3.1f, y0 + 3.9f, cz - .1f, cz + .1f, top);
        }

        // White lettering on a board. yawDeg = -90: the text faces the track (local -x), +90: faces the street (local +x).
        void SbmText(Node3D parent, string text, Vector3 pos, float em, float yawDeg, float range)
        {
            var lb = FxText(parent, text, pos, em, Colors.White);
            lb.RotationDegrees = new Vector3(0f, yawDeg, 0f);
            Far(lb, range);
        }

        // e = the edge the platform runs along, cs = distance along it at the centre of the building, sm0 = platform side (-1 left, +1 right), xb = platform back edge from the track centre.
        void SbmBuild(TEdge e, float cs, float sm0, float xb)
        {
            FxInit();
            Vector3 p = e.PosAt(cs), d = e.DirAt(cs); p.Y = 0f;
            Basis rot = Basis.LookingAt(sm0 > 0f ? d : -d, Vector3.Up);      // local +x points away from the track on the platform side
            float x0 = xb;
            Color cream = new Color(.89f, .82f, .66f), maroon = new Color(.50f, .19f, .15f), stone = new Color(.58f, .54f, .47f), roofc = new Color(.33f, .34f, .37f),
                  white = new Color(.94f, .93f, .89f), glass = new Color(.13f, .21f, .31f), leaf = new Color(.27f, .16f, .09f), blue = new Color(.05f, .17f, .42f),
                  gold = new Color(.86f, .66f, .16f), black = new Color(.04f, .04f, .05f), paving = new Color(.58f, .56f, .52f);
            const float WH = 10.0f, CH = 12.6f;                               // cornice height of the wings and of the centre block
            float[] wz = { 18f, 22f, 26f, 30f, 34f, 38f };                    // window bays of a wing (distance from the centre)
            float[] pz = { 16f, 20f, 24f, 32f, 36f, 40f };                    // pilasters of a wing front
            float[] cw = { -10.5f, 10.5f };                                   // centre block: ground floor windows
            float[] cf = { -10.5f, -5.5f, 0f, 5.5f, 10.5f };                  // centre block: first floor windows
            float[] cd = { -5.5f, 0f, 5.5f };                                 // centre block: doors
            float[] pcz = { -12.5f, -7.5f, -2.5f, 2.5f, 7.5f, 12.5f };       // porch columns
            float[] kzs = { 17.5f, 38.5f };                                   // roof pavilions of a wing

            var mesh = BuildMesh(sf =>
            {
                // ---- walls
                SbmBox(sf, x0, x0 + 12f, 0f, WH, -42f, -14f, cream);
                SbmBox(sf, x0, x0 + 12f, 0f, WH, 14f, 42f, cream);
                SbmBox(sf, x0, x0 + 16f, 0f, CH, -14f, 14f, cream);

                // ---- the two wings
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    float za = sg > 0 ? 14f : -42f, zb = sg > 0 ? 42f : -14f;             // wall
                    float ra = sg > 0 ? 14f : -42.3f, rb = sg > 0 ? 42.3f : -14f;         // roof (overhangs at the outer end)
                    float ea = sg > 0 ? 42.05f : -42.3f, eb = sg > 0 ? 42.3f : -42.05f;   // parapet at the outer end
                    SbmBox(sf, x0 - .30f, x0, 9.5f, WH, za, zb, maroon);                  // cornice, front
                    SbmBox(sf, x0 + 12f, x0 + 12.30f, 9.5f, WH, za, zb, maroon);          // cornice, back
                    SbmBox(sf, x0 - .30f, x0 + 12.30f, WH, WH + .30f, ra, rb, roofc);     // roof slab
                    SbmBox(sf, x0 - .30f, x0 - .05f, WH + .30f, WH + .90f, ra, rb, cream);        // parapet front
                    SbmBox(sf, x0 + 12.05f, x0 + 12.30f, WH + .30f, WH + .90f, ra, rb, cream);    // parapet back
                    SbmBox(sf, x0 - .30f, x0 + 12.30f, WH + .30f, WH + .90f, ea, eb, cream);      // parapet end
                    SbmBox(sf, x0 + 12f, x0 + 12.15f, 0f, SecTop, za, zb, stone);         // stone plinth on the street side
                    foreach (float bz in wz)
                    {
                        float wc = sg * bz;
                        if (bz != 26f && bz != 30f) SbmWin(sf, x0, -1f, wc, 2.7f, 5.0f, white, glass, white);   // ground floor (the door stands between bays 22 and 34)
                        SbmWin(sf, x0, -1f, wc, 6.9f, 9.0f, white, glass, white);
                        SbmWin(sf, x0 + 12f, 1f, wc, 2.7f, 5.0f, white, glass, white);
                        SbmWin(sf, x0 + 12f, 1f, wc, 6.9f, 9.0f, white, glass, white);
                    }
                    foreach (float pp in pz) SbmBox(sf, x0 - .18f, x0, SecTop, 9.5f, sg * pp - .25f, sg * pp + .25f, maroon);
                    SbmDoor(sf, x0, -1f, sg * 28f, SecTop, white, leaf, glass);
                    SbmBox(sf, x0 - .28f, x0 - .10f, 5.45f, 6.35f, sg * 28f - 1.8f, sg * 28f + 1.8f, white);     // board over the door
                    SbmBox(sf, x0 - .36f, x0 - .28f, 5.55f, 6.25f, sg * 28f - 1.7f, sg * 28f + 1.7f, blue);
                    foreach (float kz in kzs) SbmChhatri(sf, x0 + 6f, sg * kz, WH + .30f, cream, maroon, gold);
                }

                // ---- centre block: cornices, roof, parapet, stepped pediment on the front
                SbmBox(sf, x0 - .40f, x0, 12.1f, CH, -14.3f, 14.3f, maroon);
                SbmBox(sf, x0 + 16f, x0 + 16.40f, 12.1f, CH, -14.3f, 14.3f, maroon);
                SbmBox(sf, x0 - .40f, x0 + 16.40f, CH, CH + .30f, -14.4f, 14.4f, roofc);
                SbmBox(sf, x0 - .40f, x0 - .10f, CH + .30f, CH + 1.0f, -14.4f, 14.4f, cream);
                SbmBox(sf, x0 + 16.10f, x0 + 16.40f, CH + .30f, CH + 1.0f, -14.4f, 14.4f, cream);
                SbmBox(sf, x0 - .40f, x0 + 16.40f, CH + .30f, CH + 1.0f, -14.4f, -14.1f, cream);
                SbmBox(sf, x0 - .40f, x0 + 16.40f, CH + .30f, CH + 1.0f, 14.1f, 14.4f, cream);
                SbmBox(sf, x0 - .40f, x0 + .60f, CH + 1.0f, CH + 2.4f, -8f, 8f, cream);
                SbmBox(sf, x0 - .40f, x0 + .60f, CH + 2.4f, CH + 3.4f, -5f, 5f, cream);
                SbmBox(sf, x0 - .40f, x0 + .60f, CH + 3.4f, CH + 4.1f, -2.2f, 2.2f, maroon);

                // ---- centre block: windows and doors, front (platform) and back (street)
                foreach (float qz in cw)
                {
                    SbmWin(sf, x0, -1f, qz, 2.7f, 5.0f, white, glass, white);
                    SbmWin(sf, x0 + 16f, 1f, qz, 2.7f, 5.0f, white, glass, white);
                }
                foreach (float qz in cf)
                {
                    SbmWin(sf, x0, -1f, qz, 6.9f, 9.0f, white, glass, white);
                    SbmWin(sf, x0 + 16f, 1f, qz, 6.9f, 9.0f, white, glass, white);
                }
                foreach (float qz in cd)
                {
                    SbmDoor(sf, x0, -1f, qz, SecTop, white, leaf, glass);
                    SbmDoor(sf, x0 + 16f, 1f, qz, SecTop, white, leaf, glass);
                }

                // ---- name boards (the lettering is added below as text)
                SbmBox(sf, x0 - .28f, x0 - .10f, 9.9f, 11.9f, -10.2f, 10.2f, white);
                SbmBox(sf, x0 - .36f, x0 - .28f, 10.0f, 11.8f, -10.0f, 10.0f, blue);
                SbmBox(sf, x0 + 16.10f, x0 + 16.28f, 9.9f, 11.9f, -10.2f, 10.2f, white);
                SbmBox(sf, x0 + 16.28f, x0 + 16.36f, 10.0f, 11.8f, -10.0f, 10.0f, blue);

                // ---- clock tower (centre, set back 4 m from the front wall)
                SbmBox(sf, x0 + 4f, x0 + 12f, CH, 21.0f, -4f, 4f, cream);                         // shaft
                SbmBox(sf, x0 + 3.8f, x0 + 12.2f, 15.8f, 16.2f, -4.2f, 4.2f, maroon);             // bands
                SbmBox(sf, x0 + 3.8f, x0 + 12.2f, 20.6f, 21.0f, -4.2f, 4.2f, maroon);
                SbmBox(sf, x0 + 5f, x0 + 11f, 21.0f, 24.0f, -3f, 3f, cream);                      // belfry
                SbmBox(sf, x0 + 4.94f, x0 + 5.0f, 21.6f, 23.4f, -1.2f, 1.2f, glass);              // belfry openings
                SbmBox(sf, x0 + 11.0f, x0 + 11.06f, 21.6f, 23.4f, -1.2f, 1.2f, glass);
                SbmBox(sf, x0 + 7.3f, x0 + 8.7f, 21.6f, 23.4f, -3.06f, -3.0f, glass);
                SbmBox(sf, x0 + 7.3f, x0 + 8.7f, 21.6f, 23.4f, 3.0f, 3.06f, glass);
                SbmBox(sf, x0 + 4.6f, x0 + 11.4f, 24.0f, 24.6f, -3.4f, 3.4f, maroon);             // stepped cap
                SbmBox(sf, x0 + 5.4f, x0 + 10.6f, 24.6f, 25.6f, -2.6f, 2.6f, maroon);
                SbmBox(sf, x0 + 6.4f, x0 + 9.6f, 25.6f, 26.6f, -1.6f, 1.6f, maroon);
                SbmBox(sf, x0 + 7.2f, x0 + 8.8f, 26.6f, 27.4f, -.8f, .8f, maroon);
                SbmBox(sf, x0 + 7.85f, x0 + 8.15f, 27.4f, 29.2f, -.15f, .15f, gold);              // finial
                // clock on the platform side: facing -x; the viewer's left is -z, so the hour hand points to -z (9 o'clock), the minute hand up
                SbmBox(sf, x0 + 3.92f, x0 + 4.0f, 16.3f, 20.1f, -1.9f, 1.9f, maroon);
                SbmBox(sf, x0 + 3.86f, x0 + 3.94f, 16.5f, 19.9f, -1.7f, 1.7f, white);
                SbmBox(sf, x0 + 3.80f, x0 + 3.86f, 18.2f, 19.5f, -.06f, .06f, black);             // minute hand (12)
                SbmBox(sf, x0 + 3.80f, x0 + 3.86f, 18.14f, 18.26f, -1.0f, 0f, black);             // hour hand (9)
                SbmBox(sf, x0 + 3.78f, x0 + 3.86f, 18.08f, 18.32f, -.12f, .12f, black);           // hub
                SbmBox(sf, x0 + 3.82f, x0 + 3.86f, 19.6f, 19.8f, -.07f, .07f, black);             // marks at 12, 6, 9 and 3
                SbmBox(sf, x0 + 3.82f, x0 + 3.86f, 16.6f, 16.8f, -.07f, .07f, black);
                SbmBox(sf, x0 + 3.82f, x0 + 3.86f, 18.13f, 18.27f, -1.6f, -1.4f, black);
                SbmBox(sf, x0 + 3.82f, x0 + 3.86f, 18.13f, 18.27f, 1.4f, 1.6f, black);
                // clock on the street side: facing +x; the viewer's left is +z, so the hour hand points to +z
                SbmBox(sf, x0 + 12.0f, x0 + 12.08f, 16.3f, 20.1f, -1.9f, 1.9f, maroon);
                SbmBox(sf, x0 + 12.06f, x0 + 12.14f, 16.5f, 19.9f, -1.7f, 1.7f, white);
                SbmBox(sf, x0 + 12.14f, x0 + 12.20f, 18.2f, 19.5f, -.06f, .06f, black);
                SbmBox(sf, x0 + 12.14f, x0 + 12.20f, 18.14f, 18.26f, 0f, 1.0f, black);
                SbmBox(sf, x0 + 12.14f, x0 + 12.22f, 18.08f, 18.32f, -.12f, .12f, black);
                SbmBox(sf, x0 + 12.14f, x0 + 12.18f, 19.6f, 19.8f, -.07f, .07f, black);
                SbmBox(sf, x0 + 12.14f, x0 + 12.18f, 16.6f, 16.8f, -.07f, .07f, black);
                SbmBox(sf, x0 + 12.14f, x0 + 12.18f, 18.13f, 18.27f, -1.6f, -1.4f, black);
                SbmBox(sf, x0 + 12.14f, x0 + 12.18f, 18.13f, 18.27f, 1.4f, 1.6f, black);

                // ---- street-side porch: terrace at floor level, roof on columns, two steps down to the forecourt
                SbmBox(sf, x0 + 16f, x0 + 22.8f, 0f, SecTop, -13.5f, 13.5f, stone);
                SbmBox(sf, x0 + 22.8f, x0 + 23.5f, 0f, .97f, -13.5f, 13.5f, stone);
                SbmBox(sf, x0 + 23.5f, x0 + 24.2f, 0f, .49f, -13.5f, 13.5f, stone);
                SbmBox(sf, x0 + 16f, x0 + 23.0f, 6.2f, 6.7f, -13.5f, 13.5f, cream);
                SbmBox(sf, x0 + 22.8f, x0 + 23.05f, 5.9f, 6.75f, -13.6f, 13.6f, maroon);
                foreach (float cc in pcz) SbmBox(sf, x0 + 22.0f, x0 + 22.6f, SecTop, 6.2f, cc - .3f, cc + .3f, cream);

                // ---- paved forecourt
                SbmBox(sf, x0 + 16f, x0 + 44f, 0f, .06f, -42f, 42f, paving);
            });

            var root = FxRoot(rot, p);
            var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = fxVert };
            root.AddChild(mi); Far(mi, 1500f);

            SbmText(root, "SECUNDERABAD JUNCTION", new Vector3(x0 - .39f, 10.9f, 0f), 1.0f, -90f, 600f);
            SbmText(root, "SECUNDERABAD JUNCTION", new Vector3(x0 + 16.39f, 10.9f, 0f), 1.0f, 90f, 600f);
            SbmText(root, "BOOKING OFFICE", new Vector3(x0 - .39f, 5.9f, -28f), .30f, -90f, 160f);
            SbmText(root, "ENQUIRY", new Vector3(x0 - .39f, 5.9f, 28f), .36f, -90f, 160f);
        }
    }
}
