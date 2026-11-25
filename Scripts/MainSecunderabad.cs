using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // v23: Secunderabad Jn rebuilt from the P2 station pack, with its mistakes corrected.
    //  - Platforms: 7 m wide, top 0.85 m above rail head, edge 1.70 m from the track centre (Indian BG standard).
    //    P2 had the edge 3.06 m away and the top only 0.18 m above the rail, so the yellow strip floated in the air.
    //  - World-space tiled paving (shader, no image), cream coping, red edge line, yellow studded tactile band, railing, end ramps.
    //  - Blue-roof shelters with steel columns and beams, benches, tube lights, hanging platform number boards.
    //  - Foot-over-bridge across both platforms and both lines with truss girders, blue roof and two 28-degree stair flights
    //    (P2 stairs were 55 degrees). Deck underside 8.0 m, well above the 25 kV wires (5.75 m contact, 7.15 m messenger).
    // Everything is built from the same track graph, so it stays attached to the line.
    public partial class Main
    {
        const float SecEdge = 1.70f, SecW = 7.0f, SecTop = 1.46f;
        readonly List<Transform3D> secBody = new List<Transform3D>(), secTopL = new List<Transform3D>(), secCop = new List<Transform3D>(), secRed = new List<Transform3D>(),
            secTac = new List<Transform3D>(), secRail = new List<Transform3D>(), secPost = new List<Transform3D>(), secRoof = new List<Transform3D>(),
            secCol = new List<Transform3D>(), secBeam = new List<Transform3D>(), secSeat = new List<Transform3D>(), secBack = new List<Transform3D>(),
            secLeg = new List<Transform3D>(), secLight = new List<Transform3D>(), secSign = new List<Transform3D>();
        bool secFobOk, secDone; Vector3 secFobC, secFobD; int secIdx;

        static bool RTIsSec(TStation st) { return st.Name.StartsWith("Sec"); }

        const string SecTileShader = @"
shader_type spatial;
render_mode diffuse_lambert, specular_disabled;
varying vec3 wp;
void vertex() { wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
float h(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
void fragment() {
    vec2 uv = wp.xz / 0.6; vec2 id = floor(uv); vec2 f = fract(uv);
    float line = min(step(f.x, 0.03) + step(f.y, 0.03), 1.0);
    vec3 base = vec3(0.66, 0.65, 0.61) * (0.90 + 0.16 * h(id));
    base *= 0.92 + 0.08 * h(floor(wp.xz * 3.0));
    ALBEDO = mix(base, vec3(0.36, 0.36, 0.34), line * 0.75);
    ROUGHNESS = 0.9;
}";
        const string SecTactileShader = @"
shader_type spatial;
render_mode diffuse_lambert, specular_disabled;
varying vec3 wp;
void vertex() { wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
    vec2 f = fract(wp.xz / 0.12) - 0.5;
    float dome = 1.0 - smoothstep(0.26, 0.36, length(f));
    vec3 y = vec3(0.93, 0.72, 0.08);
    ALBEDO = mix(y * 0.55, y * 1.05, dome);
    ROUGHNESS = 0.85;
}";

        void RTSecInitFob(TStation st)
        {
            if (secFobOk || secDone) return;
            if (!sc.Graph.Edges.TryGetValue("d0", out var d0)) return;
            float s = Mathf.Clamp(MastSpacing * Mathf.Round((st.Offset - 280f) / MastSpacing), 60f, d0.Length - 60f);   // midway between two masts
            Vector3 p = d0.PosAt(s), d = d0.DirAt(s);
            secFobC = p + Right(d) * 2.25f; secFobD = d; secFobOk = true;          // 2.25 m = half the 4.5 m track spacing of this route
        }

        bool RTSecFobZone(Vector3 p)
        {
            if (!secFobOk) return false;
            float along = (p - secFobC).Dot(secFobD); return along > -4.5f && along < 18f;
        }

        // One platform piece (a chord of the track between s0 and e1).
        void RTSecPiece(TEdge e, float s0, float e1, float sm, bool endPiece, TStation st)
        {
            RTSecInitFob(st);
            float len = e1 - s0, mid = (s0 + e1) * .5f; secIdx++;
            Vector3 pp = e.PosAt(mid), dd = e.DirAt(mid), up = Vector3.Up; Basis qq = Basis.LookingAt(dd, up); Vector3 rr = Right(dd) * sm;
            float L = len + .2f;
            secBody.Add(Xf(qq, pp + rr * (SecEdge + SecW * .5f) + up * .70f, new Vector3(SecW, 1.40f, L)));
            secTopL.Add(Xf(qq, pp + rr * 5.4f + up * 1.44f, new Vector3(6.6f, .04f, L)));
            secCop.Add(Xf(qq, pp + rr * 1.9f + up * 1.455f, new Vector3(.40f, .07f, L)));
            secRed.Add(Xf(qq, pp + rr * 1.76f + up * 1.495f, new Vector3(.10f, .012f, L)));
            secTac.Add(Xf(qq, pp + rr * 2.45f + up * 1.468f, new Vector3(.50f, .016f, L)));
            secRail.Add(Xf(qq, pp + rr * 8.55f + up * (SecTop + .95f), new Vector3(.05f, .05f, L)));
            secRail.Add(Xf(qq, pp + rr * 8.55f + up * (SecTop + .50f), new Vector3(.04f, .04f, L)));
            for (float u = s0 + 5f; u < e1; u += 10f) fxPlat.Add(e.PosAt(u) + Right(e.DirAt(u)) * sm * 5.2f);     // keeps signs off the platform
            for (float u = s0 + 1.5f; u < e1; u += 3f)
            {
                Vector3 pu = e.PosAt(u), du = e.DirAt(u); Basis qu = Basis.LookingAt(du, up); Vector3 ru = Right(du) * sm;
                secPost.Add(Xf(qu, pu + ru * 8.55f + up * (SecTop + .5f), new Vector3(.06f, 1.0f, .06f)));
            }
            if (endPiece) return;
            for (float u = s0 + 3f; u < e1; u += 6f)
            {
                Vector3 pu = e.PosAt(u) , du = e.DirAt(u); Basis qu = Basis.LookingAt(du, up); Vector3 ru = Right(du) * sm;
                if (RTSecFobZone(pu)) continue;
                secLight.Add(Xf(qu, pu + ru * 6.0f + up * 5.02f, new Vector3(.14f, .07f, 1.3f)));
            }
            if (RTSecFobZone(pp)) return;                                                         // the stairs of the bridge come down here
            secRoof.Add(Xf(qq, pp + rr * 6.225f + up * 5.2f, new Vector3(4.15f, .12f, L)));        // blue roof, edge 4.15 m from the track (clear of masts)
            secBeam.Add(Xf(qq, pp + rr * 6.225f + up * 5.0f, new Vector3(4.15f, .20f, .18f)));
            secCol.Add(Xf(qq, pp + rr * 8.0f + up * ((SecTop + 5.14f) * .5f), new Vector3(.28f, 5.14f - SecTop, .28f)));
            if (len >= 25f)
            {
                Vector3 pb = e.PosAt(s0 + len * .5f + 6f), db = e.DirAt(s0 + len * .5f + 6f); Basis qb = Basis.LookingAt(db, up); Vector3 rb = Right(db) * sm;
                if (!RTSecFobZone(pb))
                    for (int k = 0; k < 2; k++)
                    {
                        Vector3 bc = pb + qb.Z * (k == 0 ? -4.4f : 4.4f) + rb * 4.6f;
                        secSeat.Add(Xf(qb, bc + up * (SecTop + .46f), new Vector3(.50f, .06f, 2.2f)));
                        secBack.Add(Xf(qb, bc + rb * .27f + up * (SecTop + .80f), new Vector3(.06f, .50f, 2.2f)));
                        secLeg.Add(Xf(qb, bc + qb.Z * .9f + up * (SecTop + .23f), new Vector3(.40f, .46f, .08f)));
                        secLeg.Add(Xf(qb, bc - qb.Z * .9f + up * (SecTop + .23f), new Vector3(.40f, .46f, .08f)));
                    }
            }
            if (secIdx % 2 == 0)                                                                    // hanging platform number board
            {
                string num = e.Id == "sc_yard_a" || e.Id == "sc_yard_b" ? "3" : (e.Id == "sc_yard_c" || e.Id == "sc_yard_d" ? "4" : (e.Id.Length > 0 && e.Id[0] == 'd' ? "1" : "2"));
                Vector3 bp = pp + rr * 5.2f + up * 4.2f;
                secSign.Add(Xf(qq, bp, new Vector3(2.6f, 1.0f, .08f)));
                FxInit();
                var r1 = FxRoot(Basis.LookingAt(dd, up), bp - dd * .05f); var r2 = FxRoot(Basis.LookingAt(-dd, up), bp + dd * .05f);
                Far(FxText(r1, "PLATFORM " + num, new Vector3(0f, 0f, 0f), .55f, Colors.White), 160f);
                Far(FxText(r2, "PLATFORM " + num, new Vector3(0f, 0f, 0f), .55f, Colors.White), 160f);
            }
        }

        // Three-step ramp at a platform end.
        void RTSecRamp(TEdge e, float s, float dirSign, float sm)
        {
            Vector3 p = e.PosAt(s), d = e.DirAt(s) * dirSign; Basis q = Basis.LookingAt(e.DirAt(s), Vector3.Up); Vector3 rr = Right(e.DirAt(s)) * sm;
            for (int k = 0; k < 3; k++)
            {
                float ht = 1.10f - .35f * k;
                secBody.Add(Xf(q, p + d * (2f + 4f * k) + rr * (SecEdge + SecW * .5f) + Vector3.Up * (ht * .5f), new Vector3(SecW, ht, 4f)));
            }
        }

        void RTSecFinish()
        {
            if (secDone) return;
            secDone = true;
            var tile = new ShaderMaterial { Shader = new Shader { Code = SecTileShader } };
            var tac = new ShaderMaterial { Shader = new Shader { Code = SecTactileShader } };
            var lamp = new StandardMaterial3D { AlbedoColor = new Color(.96f, .96f, .92f), EmissionEnabled = true, Emission = new Color(1f, .96f, .82f), EmissionEnergyMultiplier = 1.2f };
            FillMM(secBody, Mat(new Color(.56f, .56f, .54f))); FillMM(secTopL, tile); FillMM(secCop, Mat(new Color(.88f, .88f, .84f)));
            FillMM(secRed, Mat(new Color(.63f, .17f, .14f))); FillMM(secTac, tac); FillMM(secRail, Solid(new Color(.30f, .32f, .35f), .3f, .5f));
            FillMM(secPost, Solid(new Color(.30f, .32f, .35f), .3f, .5f)); FillMM(secRoof, Solid(new Color(.16f, .42f, .66f), .2f, .6f));
            FillMM(secCol, Mat(new Color(.25f, .27f, .30f))); FillMM(secBeam, Mat(new Color(.25f, .27f, .30f)));
            FillMM(secSeat, Mat(new Color(.62f, .20f, .16f))); FillMM(secBack, Mat(new Color(.62f, .20f, .16f))); FillMM(secLeg, Mat(new Color(.22f, .24f, .27f)));
            FillMM(secLight, lamp); FillMM(secSign, Mat(new Color(.13f, .36f, .59f)));
            if (secFobOk) RTSecBuildFob();
        }

        // Foot-over-bridge. Local frame: x = right of the down line's direction, y up, +z = backwards. Origin on the ground between the two lines.
        void RTSecBuildFob()
        {
            FxInit();
            var mesh = BuildMesh(st =>
            {
                Color deck = new Color(.66f, .65f, .61f), blue = new Color(.13f, .36f, .59f), steel = new Color(.25f, .27f, .30f), roof = new Color(.19f, .49f, .72f),
                      white = new Color(.92f, .92f, .88f), tread = new Color(.60f, .60f, .57f);
                const float yD = 8.0f, yT = 8.2f, hx = 10.65f, gz = 1.45f, hgt = 1.1f;
                AddBoxC(st, new Vector3(0f, yD + .1f, 0f), new Vector3(2f * hx, .2f, 3.0f), deck);                     // walkway slab
                for (float x = -hx + .5f; x < hx; x += 3f) AddBoxC(st, new Vector3(x, yD - .12f, 0f), new Vector3(.15f, .24f, 3.0f), steel);   // cross beams
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    float z = sd * gz;
                    RTBar(st, new Vector3(-hx, yT + hgt, z), new Vector3(hx, yT + hgt, z), .12f, blue);               // top chord
                    RTBar(st, new Vector3(-hx, yT, z), new Vector3(hx, yT, z), .14f, blue);                            // bottom chord
                    int i = 0;
                    for (float x = -hx; x < hx - .1f; x += 1.5f, i++)
                    {
                        RTBar(st, new Vector3(x, yT, z), new Vector3(x, yT + hgt, z), .08f, steel);                    // vertical
                        if ((i & 1) == 0) RTBar(st, new Vector3(x, yT, z), new Vector3(x + 1.5f, yT + hgt, z), .07f, steel);        // diagonals
                        else RTBar(st, new Vector3(x, yT + hgt, z), new Vector3(x + 1.5f, yT, z), .07f, steel);
                        if (i % 2 == 0) RTBar(st, new Vector3(x, yT + hgt, z), new Vector3(x, yT + 1.95f, sd * 1.75f), .06f, steel);   // roof support
                    }
                    for (int cx = -1; cx <= 1; cx += 2)                                                                // columns on the platforms
                        AddBoxC(st, new Vector3(cx * 6.0f, (SecTop + yD) * .5f, z * .9f), new Vector3(.4f, yD - SecTop, .4f), steel);
                }
                AddBoxC(st, new Vector3(0f, yT + 2.0f, 0f), new Vector3(2f * hx + .6f, .06f, 3.9f), roof);              // blue roof
                for (float x = -hx; x <= hx; x += 3f) RTBar(st, new Vector3(x, yT + 1.95f, -1.75f), new Vector3(x, yT + 2.1f, 0f), .05f, steel);
                for (float x = -hx; x <= hx; x += 3f) RTBar(st, new Vector3(x, yT + 2.1f, 0f), new Vector3(x, yT + 1.95f, 1.75f), .05f, steel);
                RTBar(st, new Vector3(-hx, yT + .95f, -1.5f), new Vector3(hx, yT + .95f, -1.5f), .05f, white);           // inner handrail look
                const int N = 42; float rise = (yT - SecTop) / N, run = .30f, z0 = -2.0f;
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    float xc = sd * 9.65f;
                    AddBoxC(st, new Vector3(xc, yT - .1f, z0 + .25f), new Vector3(2.0f, .2f, .5f), deck);               // landing
                    for (int k = 0; k < N; k++)
                        AddBoxC(st, new Vector3(xc, yT - (k + 1) * rise - .03f, z0 - (k + .5f) * run), new Vector3(2.0f, .06f, run), tread);
                    for (int sg = -1; sg <= 1; sg += 2)
                    {
                        float x = xc + sg * .98f; float zE = z0 - N * run;
                        RTBar(st, new Vector3(x, yT - .12f, z0), new Vector3(x, SecTop + .04f, zE), .16f, steel);         // stringer
                        RTBar(st, new Vector3(x, yT + .95f, z0), new Vector3(x, SecTop + .95f, zE), .06f, white);         // handrail
                        RTBar(st, new Vector3(x, yT + .50f, z0), new Vector3(x, SecTop + .50f, zE), .04f, white);         // mid rail
                        for (int k = 0; k <= N; k += 5)
                        {
                            float zz = z0 - k * run, yy = yT - k * rise;
                            RTBar(st, new Vector3(x, yy, zz), new Vector3(x, yy + .95f, zz), .035f, steel);              // baluster
                        }
                    }
                    for (float k = 0f; k <= N; k += 14f)                                                                   // stair support legs
                    {
                        float zz = z0 - k * run, yy = yT - k * rise - .15f;
                        if (yy > SecTop + .3f) AddBoxC(st, new Vector3(xc, (SecTop + yy) * .5f, zz), new Vector3(1.9f, yy - SecTop, .15f), steel);
                    }
                }
            });
            var root = FxRoot(Basis.LookingAt(secFobD, Vector3.Up), secFobC);
            var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = fxVert }; root.AddChild(mi); Far(mi, 900f);
            Far(FxText(root, "FOOT OVER BRIDGE", new Vector3(0f, 8.2f + 1.1f + .0f, 1.52f), .5f, Colors.White), 120f);
        }
    }
}
