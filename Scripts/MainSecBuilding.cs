using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // v5.24: the Secunderabad main station building and wall behind Platform 1 (the side platform of the d0 line).
    // A generic Indian junction-station building built from boxes (not a copy of the real one).
    //  - 460 m long = 3/4 of the 620 m platform, at the stop-mark end: a 28 m centre block with the clock tower and 2 wings of 54 bays (4 m each).
    //  - The other 160 m of the platform have a plastered boundary wall with pilasters and a painted dado.
    //  - Front (platform side): windows, doors with signs (BOOKING OFFICE, ENQUIRY ...), a blue name board, pilasters, roof pavilions.
    //    Back (street side): windows, and in the centre a porch with steps and a paved forecourt.
    //  - Clock tower in the centre (clock at 9:00, the time the game starts).
    // The floor is at platform level (SecTop). The platform railing is left out along the whole of Platform 1, and the new shed (MainShed.cs) leans on the building.
    // Local frame of the meshes: x = away from the track (the platform back edge is at x = xb), y = up, z = along the track; the building is symmetric in z.
    public partial class Main
    {
        const int SbmWingBays = 54;
        const float SbmBay = 4f, SbmCen = 14f, SbmWH = 10.0f, SbmCH = 12.6f;      // bay width, half width of the centre block, cornice heights of wings / centre block
        const float SbmLen = 2f * (SbmCen + SbmWingBays * SbmBay);                // 460 m
        string sbmEdgeId = ""; float sbmS0, sbmS1;                                // stretch of an edge where the platform railing is left out (the whole of Platform 1)

        static readonly Color SbmCream = new Color(.89f, .82f, .66f), SbmMaroon = new Color(.50f, .19f, .15f), SbmStone = new Color(.58f, .54f, .47f), SbmRoofC = new Color(.33f, .34f, .37f),
            SbmWhite = new Color(.94f, .93f, .89f), SbmGlass = new Color(.13f, .21f, .31f), SbmLeaf = new Color(.27f, .16f, .09f), SbmBlue = new Color(.05f, .17f, .42f),
            SbmGold = new Color(.86f, .66f, .16f), SbmBlack = new Color(.04f, .04f, .05f), SbmPaving = new Color(.58f, .56f, .52f);

        bool SbmInZone(TEdge e, float u) { return e != null && e.Id == sbmEdgeId && u > sbmS0 && u < sbmS1; }

        // Where things stand along the station's edge: lo = start of the platform (the wall starts here), b0..b1 = the building, b1 = end of the platform.
        void SbmPlan(TStation stn, out float lo, out float b0, out float b1)
        {
            float hi = Mathf.Min(stn.Edge.Length, stn.Offset + 10f);
            lo = Mathf.Max(0f, hi - 620f); b1 = hi; b0 = Mathf.Max(lo, hi - SbmLen);
        }

        // Safety railing along the back of a platform; it stops along the station building and wall so the doors open straight onto the platform.
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

        // Plastered boundary wall with pilasters, a painted dado and coping: the last quarter of Platform 1 (lo..hi = local z range).
        static void SbmWall(SurfaceTool sf, float x0, float lo, float hi)
        {
            Color plaster = new Color(.90f, .80f, .56f);
            SbmBox(sf, x0, x0 + .45f, 0f, 5.3f, lo, hi, plaster);
            SbmBox(sf, x0 - .03f, x0, SecTop, 2.1f, lo, hi, SbmMaroon);                // painted dado, platform side
            SbmBox(sf, x0 + .45f, x0 + .48f, 0f, 2.1f, lo, hi, SbmMaroon);              // street side
            SbmBox(sf, x0 - .12f, x0 + .57f, 5.3f, 5.5f, lo, hi, SbmWhite);             // coping
            for (float z = lo + 3f; z < hi - 1f; z += 6f)
            {
                SbmBox(sf, x0 - .15f, x0 + .60f, 0f, 5.3f, z - .275f, z + .275f, SbmCream);
                SbmBox(sf, x0 - .20f, x0 + .65f, 5.3f, 5.62f, z - .35f, z + .35f, SbmWhite);
                SbmBox(sf, x0 - .08f, x0 + .53f, 5.62f, 5.8f, z - .2f, z + .2f, SbmMaroon);
            }
        }

        // One chunk of a wing: bays i0..i1-1 counted from the centre block; sg = -1 / +1 = which side (local z). outer = the last chunk (ends the building).
        static void SbmWingChunk(SurfaceTool sf, float x0, int sg, int i0, int i1, bool outer, List<float> doorZ)
        {
            float a = sg * (SbmCen + SbmBay * i0), b = sg * (SbmCen + SbmBay * i1);
            float lo = Mathf.Min(a, b), hi = Mathf.Max(a, b), rlo = lo, rhi = hi;
            if (outer) { if (sg > 0) rhi += .3f; else rlo -= .3f; }                 // roof overhangs at the outer end
            SbmBox(sf, x0, x0 + 12f, 0f, SbmWH, lo, hi, SbmCream);                                                  // walls
            SbmBox(sf, x0 - .30f, x0, 9.5f, SbmWH, lo, hi, SbmMaroon);                                              // cornice, front
            SbmBox(sf, x0 + 12f, x0 + 12.30f, 9.5f, SbmWH, lo, hi, SbmMaroon);                                      // cornice, back
            SbmBox(sf, x0 - .30f, x0 + 12.30f, SbmWH, SbmWH + .30f, rlo, rhi, SbmRoofC);                            // roof slab
            SbmBox(sf, x0 - .30f, x0 - .05f, SbmWH + .30f, SbmWH + .90f, rlo, rhi, SbmCream);                       // parapet front
            SbmBox(sf, x0 + 12.05f, x0 + 12.30f, SbmWH + .30f, SbmWH + .90f, rlo, rhi, SbmCream);                   // parapet back
            if (outer)
            {
                float ea = sg > 0 ? hi + .05f : lo - .3f, eb = sg > 0 ? hi + .3f : lo - .05f;
                SbmBox(sf, x0 - .30f, x0 + 12.30f, SbmWH + .30f, SbmWH + .90f, ea, eb, SbmCream);                   // parapet at the end
            }
            SbmBox(sf, x0 + 12f, x0 + 12.15f, 0f, SecTop, lo, hi, SbmStone);                                        // stone plinth on the street side
            for (int i = i0; i < i1; i++)
            {
                float zc = sg * (SbmCen + SbmBay * i + SbmBay * .5f);
                bool door = i % 12 == 6;                                                                              // a door every 12 bays on the platform side
                if (!door) SbmWin(sf, x0, -1f, zc, 2.7f, 5.0f, SbmWhite, SbmGlass, SbmWhite);
                SbmWin(sf, x0, -1f, zc, 6.9f, 9.0f, SbmWhite, SbmGlass, SbmWhite);
                SbmWin(sf, x0 + 12f, 1f, zc, 2.7f, 5.0f, SbmWhite, SbmGlass, SbmWhite);
                SbmWin(sf, x0 + 12f, 1f, zc, 6.9f, 9.0f, SbmWhite, SbmGlass, SbmWhite);
                if (door)
                {
                    SbmDoor(sf, x0, -1f, zc, SecTop, SbmWhite, SbmLeaf, SbmGlass);
                    SbmBox(sf, x0 - .28f, x0 - .10f, 5.45f, 6.35f, zc - 1.8f, zc + 1.8f, SbmWhite);                 // board over the door
                    SbmBox(sf, x0 - .36f, x0 - .28f, 5.55f, 6.25f, zc - 1.7f, zc + 1.7f, SbmBlue);
                    doorZ.Add(zc);
                }
                if (i > 0) { float zp = sg * (SbmCen + SbmBay * i); SbmBox(sf, x0 - .18f, x0, SecTop, 9.5f, zp - .25f, zp + .25f, SbmMaroon); }   // pilaster between two bays
                if (i % 9 == 4) SbmChhatri(sf, x0 + 6f, zc, SbmWH + .30f, SbmCream, SbmMaroon, SbmGold);               // roof pavilion every 36 m
            }
        }

        // The 28 m centre block: cornices, roof, stepped pediment, windows, three doors each side, name boards, clock tower, street porch and forecourt.
        static void SbmCentreBlock(SurfaceTool sf, float x0)
        {
            const float CH = SbmCH;
            float[] cw = { -10.5f, 10.5f };                                   // ground floor windows
            float[] cf = { -10.5f, -5.5f, 0f, 5.5f, 10.5f };                  // first floor windows
            float[] cd = { -5.5f, 0f, 5.5f };                                 // doors
            float[] pcz = { -12.5f, -7.5f, -2.5f, 2.5f, 7.5f, 12.5f };       // porch columns

            SbmBox(sf, x0, x0 + 16f, 0f, CH, -14f, 14f, SbmCream);                                                  // walls
            SbmBox(sf, x0 - .40f, x0, 12.1f, CH, -14.3f, 14.3f, SbmMaroon);                                         // cornices
            SbmBox(sf, x0 + 16f, x0 + 16.40f, 12.1f, CH, -14.3f, 14.3f, SbmMaroon);
            SbmBox(sf, x0 - .40f, x0 + 16.40f, CH, CH + .30f, -14.4f, 14.4f, SbmRoofC);                             // roof, parapet
            SbmBox(sf, x0 - .40f, x0 - .10f, CH + .30f, CH + 1.0f, -14.4f, 14.4f, SbmCream);
            SbmBox(sf, x0 + 16.10f, x0 + 16.40f, CH + .30f, CH + 1.0f, -14.4f, 14.4f, SbmCream);
            SbmBox(sf, x0 - .40f, x0 + 16.40f, CH + .30f, CH + 1.0f, -14.4f, -14.1f, SbmCream);
            SbmBox(sf, x0 - .40f, x0 + 16.40f, CH + .30f, CH + 1.0f, 14.1f, 14.4f, SbmCream);
            SbmBox(sf, x0 - .40f, x0 + .60f, CH + 1.0f, CH + 2.4f, -8f, 8f, SbmCream);                              // stepped pediment on the front
            SbmBox(sf, x0 - .40f, x0 + .60f, CH + 2.4f, CH + 3.4f, -5f, 5f, SbmCream);
            SbmBox(sf, x0 - .40f, x0 + .60f, CH + 3.4f, CH + 4.1f, -2.2f, 2.2f, SbmMaroon);

            foreach (float qz in cw)
            {
                SbmWin(sf, x0, -1f, qz, 2.7f, 5.0f, SbmWhite, SbmGlass, SbmWhite);
                SbmWin(sf, x0 + 16f, 1f, qz, 2.7f, 5.0f, SbmWhite, SbmGlass, SbmWhite);
            }
            foreach (float qz in cf)
            {
                SbmWin(sf, x0, -1f, qz, 6.9f, 9.0f, SbmWhite, SbmGlass, SbmWhite);
                SbmWin(sf, x0 + 16f, 1f, qz, 6.9f, 9.0f, SbmWhite, SbmGlass, SbmWhite);
            }
            foreach (float qz in cd)
            {
                SbmDoor(sf, x0, -1f, qz, SecTop, SbmWhite, SbmLeaf, SbmGlass);
                SbmDoor(sf, x0 + 16f, 1f, qz, SecTop, SbmWhite, SbmLeaf, SbmGlass);
            }

            // name boards (the lettering is added as text in SbmBuild)
            SbmBox(sf, x0 - .28f, x0 - .10f, 9.9f, 11.9f, -10.2f, 10.2f, SbmWhite);
            SbmBox(sf, x0 - .36f, x0 - .28f, 10.0f, 11.8f, -10.0f, 10.0f, SbmBlue);
            SbmBox(sf, x0 + 16.10f, x0 + 16.28f, 9.9f, 11.9f, -10.2f, 10.2f, SbmWhite);
            SbmBox(sf, x0 + 16.28f, x0 + 16.36f, 10.0f, 11.8f, -10.0f, 10.0f, SbmBlue);

            // clock tower (centre, set back 4 m from the front wall)
            SbmBox(sf, x0 + 4f, x0 + 12f, CH, 21.0f, -4f, 4f, SbmCream);                                           // shaft
            SbmBox(sf, x0 + 3.8f, x0 + 12.2f, 15.8f, 16.2f, -4.2f, 4.2f, SbmMaroon);                               // bands
            SbmBox(sf, x0 + 3.8f, x0 + 12.2f, 20.6f, 21.0f, -4.2f, 4.2f, SbmMaroon);
            SbmBox(sf, x0 + 5f, x0 + 11f, 21.0f, 24.0f, -3f, 3f, SbmCream);                                        // belfry
            SbmBox(sf, x0 + 4.94f, x0 + 5.0f, 21.6f, 23.4f, -1.2f, 1.2f, SbmGlass);                                // belfry openings
            SbmBox(sf, x0 + 11.0f, x0 + 11.06f, 21.6f, 23.4f, -1.2f, 1.2f, SbmGlass);
            SbmBox(sf, x0 + 7.3f, x0 + 8.7f, 21.6f, 23.4f, -3.06f, -3.0f, SbmGlass);
            SbmBox(sf, x0 + 7.3f, x0 + 8.7f, 21.6f, 23.4f, 3.0f, 3.06f, SbmGlass);
            SbmBox(sf, x0 + 4.6f, x0 + 11.4f, 24.0f, 24.6f, -3.4f, 3.4f, SbmMaroon);                               // stepped cap
            SbmBox(sf, x0 + 5.4f, x0 + 10.6f, 24.6f, 25.6f, -2.6f, 2.6f, SbmMaroon);
            SbmBox(sf, x0 + 6.4f, x0 + 9.6f, 25.6f, 26.6f, -1.6f, 1.6f, SbmMaroon);
            SbmBox(sf, x0 + 7.2f, x0 + 8.8f, 26.6f, 27.4f, -.8f, .8f, SbmMaroon);
            SbmBox(sf, x0 + 7.85f, x0 + 8.15f, 27.4f, 29.2f, -.15f, .15f, SbmGold);                                // finial
            // clock on the platform side: facing -x; the viewer's left is -z, so the hour hand points to -z (9 o'clock), the minute hand up
            SbmBox(sf, x0 + 3.92f, x0 + 4.0f, 16.3f, 20.1f, -1.9f, 1.9f, SbmMaroon);
            SbmBox(sf, x0 + 3.86f, x0 + 3.94f, 16.5f, 19.9f, -1.7f, 1.7f, SbmWhite);
            SbmBox(sf, x0 + 3.80f, x0 + 3.86f, 18.2f, 19.5f, -.06f, .06f, SbmBlack);
            SbmBox(sf, x0 + 3.80f, x0 + 3.86f, 18.14f, 18.26f, -1.0f, 0f, SbmBlack);
            SbmBox(sf, x0 + 3.78f, x0 + 3.86f, 18.08f, 18.32f, -.12f, .12f, SbmBlack);
            SbmBox(sf, x0 + 3.82f, x0 + 3.86f, 19.6f, 19.8f, -.07f, .07f, SbmBlack);
            SbmBox(sf, x0 + 3.82f, x0 + 3.86f, 16.6f, 16.8f, -.07f, .07f, SbmBlack);
            SbmBox(sf, x0 + 3.82f, x0 + 3.86f, 18.13f, 18.27f, -1.6f, -1.4f, SbmBlack);
            SbmBox(sf, x0 + 3.82f, x0 + 3.86f, 18.13f, 18.27f, 1.4f, 1.6f, SbmBlack);
            // clock on the street side: facing +x; the viewer's left is +z, so the hour hand points to +z
            SbmBox(sf, x0 + 12.0f, x0 + 12.08f, 16.3f, 20.1f, -1.9f, 1.9f, SbmMaroon);
            SbmBox(sf, x0 + 12.06f, x0 + 12.14f, 16.5f, 19.9f, -1.7f, 1.7f, SbmWhite);
            SbmBox(sf, x0 + 12.14f, x0 + 12.20f, 18.2f, 19.5f, -.06f, .06f, SbmBlack);
            SbmBox(sf, x0 + 12.14f, x0 + 12.20f, 18.14f, 18.26f, 0f, 1.0f, SbmBlack);
            SbmBox(sf, x0 + 12.14f, x0 + 12.22f, 18.08f, 18.32f, -.12f, .12f, SbmBlack);
            SbmBox(sf, x0 + 12.14f, x0 + 12.18f, 19.6f, 19.8f, -.07f, .07f, SbmBlack);
            SbmBox(sf, x0 + 12.14f, x0 + 12.18f, 16.6f, 16.8f, -.07f, .07f, SbmBlack);
            SbmBox(sf, x0 + 12.14f, x0 + 12.18f, 18.13f, 18.27f, -1.6f, -1.4f, SbmBlack);
            SbmBox(sf, x0 + 12.14f, x0 + 12.18f, 18.13f, 18.27f, 1.4f, 1.6f, SbmBlack);

            // street-side porch: terrace at floor level, roof on columns, two steps down to the forecourt
            SbmBox(sf, x0 + 16f, x0 + 22.8f, 0f, SecTop, -13.5f, 13.5f, SbmStone);
            SbmBox(sf, x0 + 22.8f, x0 + 23.5f, 0f, .97f, -13.5f, 13.5f, SbmStone);
            SbmBox(sf, x0 + 23.5f, x0 + 24.2f, 0f, .49f, -13.5f, 13.5f, SbmStone);
            SbmBox(sf, x0 + 16f, x0 + 23.0f, 6.2f, 6.7f, -13.5f, 13.5f, SbmCream);
            SbmBox(sf, x0 + 22.8f, x0 + 23.05f, 5.9f, 6.75f, -13.6f, 13.6f, SbmMaroon);
            foreach (float cc in pcz) SbmBox(sf, x0 + 22.0f, x0 + 22.6f, SecTop, 6.2f, cc - .3f, cc + .3f, SbmCream);

            // paved forecourt in front of the porch
            SbmBox(sf, x0 + 16f, x0 + 34f, 0f, .06f, -45f, 45f, SbmPaving);
        }

        void SbmAdd(Node3D parent, ArrayMesh mesh, float range)
        {
            var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = fxVert };
            parent.AddChild(mi); Far(mi, range);
        }

        // White lettering on a board. yawDeg = -90: the text faces the track (local -x), +90: faces the street (local +x).
        void SbmText(Node3D parent, string text, Vector3 pos, float em, float yawDeg, float range)
        {
            var lb = FxText(parent, text, pos, em, Colors.White);
            lb.RotationDegrees = new Vector3(0f, yawDeg, 0f);
            Far(lb, range);
        }

        // e = the edge the platform runs along, cs = distance along it at the centre of the building, sm0 = platform side (-1 left, +1 right),
        // xb = platform back edge from the track centre, wallS0 = where the platform (and the wall) starts, bldS0 = where the building starts.
        void SbmBuild(TEdge e, float cs, float sm0, float xb, float wallS0, float bldS0)
        {
            FxInit();
            Vector3 p = e.PosAt(cs), d = e.DirAt(cs); p.Y = 0f;
            Basis rot = Basis.LookingAt(sm0 > 0f ? d : -d, Vector3.Up);      // local +x points away from the track on the platform side
            float x0 = xb, zs = sm0 < 0f ? 1f : -1f;                          // local z = zs * (distance along the edge - cs)
            var root = FxRoot(rot, p);
            var doorZ = new List<float>();

            SbmAdd(root, BuildMesh(sf => SbmCentreBlock(sf, x0)), 1500f);
            for (int sg = -1; sg <= 1; sg += 2)
                for (int c0 = 0; c0 < SbmWingBays; c0 += 12)
                {
                    int c1 = Mathf.Min(c0 + 12, SbmWingBays);
                    SbmAdd(root, BuildMesh(sf => SbmWingChunk(sf, x0, sg, c0, c1, c1 == SbmWingBays, doorZ)), 1500f);
                }
            float wa = zs * (wallS0 - cs), wb = zs * (bldS0 - cs);            // local z of the two ends of the wall
            if (Mathf.Abs(wb - wa) > 1f) SbmAdd(root, BuildMesh(sf => SbmWall(sf, x0, Mathf.Min(wa, wb), Mathf.Max(wa, wb))), 1500f);

            SbmText(root, "SECUNDERABAD JUNCTION", new Vector3(x0 - .39f, 10.9f, 0f), 1.0f, -90f, 600f);
            SbmText(root, "SECUNDERABAD JUNCTION", new Vector3(x0 + 16.39f, 10.9f, 0f), 1.0f, 90f, 600f);
            string[] names = { "BOOKING OFFICE", "ENQUIRY", "WAITING HALL", "RESERVATION", "TICKET COUNTER", "INFORMATION", "LUGGAGE", "RAILWAY POLICE" };
            for (int k = 0; k < doorZ.Count; k++) SbmText(root, names[k % names.Length], new Vector3(x0 - .39f, 5.9f, doorZ[k]), .30f, -90f, 160f);
        }
    }
}
