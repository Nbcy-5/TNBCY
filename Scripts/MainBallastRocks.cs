using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // v49: real crushed-stone ballast, light grey like the reference photo, no green tint.
    //  - The green cast came from the lighting: the sky ambient light had green ground colours and a strong blue top, which mixed with the warm sun.
    //    The ambient light and the fog are now neutral grey (done in RTRocksBuild, so no other file changes).
    //  - The ballast bed keeps its plain straight slope and sharp edge (the heap of poured stone), but it is now covered with thousands of SMALL
    //    sharp stones (3 - 6 cm, like the photo). Some sit high, some low, some on top of others, and some have rolled down past the toe
    //    of the slope onto the ground, so the edge is broken up and irregular.
    //  - Between the sleepers the gaps are filled with stones, partly stacked.
    //  - Stones on the sleepers: only at some places, chosen at random (smooth noise along the track); everywhere else the sleepers are clean.
    // Same streaming as before: a 24 m block only gets its stones while it is near the camera, and the cheap 20-face stone shapes are used for
    // nearly all of them, so the frame rate stays smooth.
    public partial class Main
    {
        // ---- tuning knobs ----
        const float RTChipDensity = 1f;           // 1 = normal, .5 = half as many stones (faster), 2 = twice as many
        const float RTChipBuildRange = 30f;       // metres: track blocks closer than this to the camera get their stones
        const float RTChipDropRange = 48f;        // metres: blocks further away than this are freed
        const float RTChipBlock = 24f;            // metres of track per block
        const int RTChipVariants = 8;             // 0..5 cheap 20-face stones, 6..7 detailed 80-face stones
        const int RTChipMaxLive = 16;             // never more than this many blocks at once (busy station yards)
        const int RTPhotoBallastCells = 40;       // stones per 2 m tile of the ballast texture (higher = smaller stones)
        const float RTSleeperRockPatch = .60f;    // 0..1  noise level above which a stretch of sleepers may carry stones (higher = fewer places)
        const float RTSleeperRockChance = .45f;   // chance that a sleeper inside such a place carries stones

        // kept from v45 so nothing else breaks (no longer used)
        const float RTRockDensity = 1f;
        const float RTRockVisRange = 150f;
        const float RTRockStep = .5f;
        const int RTRockVariants = 6;

        sealed class RTChipBlockData { public TEdge E; public int EdgeIdx, Index; public float S0, S1, X, Z, D2; public Node3D Node; }
        readonly List<RTChipBlockData> rtChipBlocks = new List<RTChipBlockData>();
        readonly List<RTChipBlockData> rtChipWant = new List<RTChipBlockData>();
        readonly List<Vector3> rtChipSwitches = new List<Vector3>();
        ArrayMesh[] rtChipMeshes; StandardMaterial3D rtChipMat; Node3D rtChipRoot;
        bool rtChipFirst = true; int rtChipLive;

        static Color RTRockVertexColor(float shade, float y)
        {
            float ao = Mathf.Clamp(.78f + .45f * (y + .25f), .62f, 1f);     // a little darker toward the bottom of the stone
            float k = Mathf.Min(1.15f, shade * ao);
            return new Color(k, k * .995f, k * .985f);
        }

        // Crushed-stone polyhedron: a (subdivided) icosphere whose every vertex is pushed in until it meets the nearest cutting plane.
        static ArrayMesh RTMakeRockMesh(int seed, int subdiv)
        {
            return BuildMesh(st =>
            {
                float g = (1f + Mathf.Sqrt(5f)) * .5f;
                Vector3[] v =
                {
                    new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0),
                    new Vector3(0, -1, g), new Vector3(0, 1, g), new Vector3(0, -1, -g), new Vector3(0, 1, -g),
                    new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1)
                };
                for (int vi = 0; vi < 12; vi++) v[vi] = v[vi].Normalized();
                int[] f = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
                var tris = new List<Vector3[]>();
                for (int fi0 = 0; fi0 < f.Length; fi0 += 3) tris.Add(new[] { v[f[fi0]], v[f[fi0 + 1]], v[f[fi0 + 2]] });
                for (int lvl = 0; lvl < subdiv; lvl++)
                {
                    var next = new List<Vector3[]>();
                    foreach (var tr in tris)
                    {
                        Vector3 a0 = tr[0], b0 = tr[1], c0 = tr[2];
                        Vector3 ab = ((a0 + b0) * .5f).Normalized(), bc = ((b0 + c0) * .5f).Normalized(), ca = ((c0 + a0) * .5f).Normalized();
                        next.Add(new[] { a0, ab, ca }); next.Add(new[] { b0, bc, ab }); next.Add(new[] { c0, ca, bc }); next.Add(new[] { ab, bc, ca });
                    }
                    tris = next;
                }

                // random cutting planes (all at a similar distance from the centre, so the stone stays chunky)
                int np = 7 + (int)(RTHash(seed, 1, 5110) * 5f);
                var pn = new Vector3[np]; var pd = new float[np];
                for (int pi = 0; pi < np; pi++)
                {
                    float u = RTHash(pi, seed, 5111) * 2f - 1f, ang = RTHash(pi, seed, 5112) * Mathf.Tau;
                    float rr = Mathf.Sqrt(Mathf.Max(0f, 1f - u * u));
                    pn[pi] = new Vector3(rr * Mathf.Cos(ang), u, rr * Mathf.Sin(ang));
                    pd[pi] = .50f + .32f * RTHash(pi, seed, 5113);
                }
                float sx = 1.05f + .45f * RTHash(seed, 2, 5114), sy = .60f + .30f * RTHash(seed, 3, 5114), sz = .85f + .35f * RTHash(seed, 4, 5114);
                System.Func<Vector3, Vector3> proj = dir =>
                {
                    float rad = 1.1f;
                    for (int pj = 0; pj < np; pj++)
                    {
                        float dn = pn[pj].Dot(dir);
                        if (dn > .02f) rad = Mathf.Min(rad, pd[pj] / dn);
                    }
                    return new Vector3(dir.X * rad * sx, dir.Y * rad * sy, dir.Z * rad * sz);
                };

                foreach (var tr in tris)
                {
                    Vector3 a = proj(tr[0]), b = proj(tr[1]), c = proj(tr[2]);
                    Vector3 n = (b - a).Cross(c - a);
                    if (n.LengthSquared() < 1e-8f) continue;
                    n = n.Normalized();
                    if (n.Dot(a + b + c) < 0f) n = -n;
                    int qx = Mathf.RoundToInt(n.X * 4f), qy = Mathf.RoundToInt(n.Y * 4f), qz = Mathf.RoundToInt(n.Z * 4f);
                    float shade = .86f + .30f * RTHash((qx + 5) * 11 + (qy + 5), (qz + 5) * 17 + seed, 5101);   // one shade per flat plane (sunlit and shaded faces)
                    st.SetColor(RTRockVertexColor(shade, a.Y)); st.SetNormal(n); st.AddVertex(a);
                    st.SetColor(RTRockVertexColor(shade, b.Y)); st.SetNormal(n); st.AddVertex(b);
                    st.SetColor(RTRockVertexColor(shade, c.Y)); st.SetNormal(n); st.AddVertex(c);
                }
            });
        }

        // Salt-and-pepper granite speckle, mapped on the stones in object space: many small dark flecks, a few white ones, pale patches.
        static RTTex RTMakeSpeckle(int size)
        {
            var alb = new byte[size * size * 3];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float h = RTHash(x, y, 5200), n = RTFbm(x / (float)size, y / (float)size, 6, 3, 5201);
                    float v = .90f + .10f * n;
                    if (h < .13f) v -= .40f; else if (h < .20f) v -= .20f; else if (h > .93f) v += .10f;
                    int o = (y * size + x) * 3;
                    alb[o] = RTByte(v); alb[o + 1] = RTByte(v * .995f); alb[o + 2] = RTByte(v * .985f);
                }
            return new RTTex { Albedo = alb, Normal = null, Size = size };
        }

        // Granite colours like the photo: light grey, pale grey, white-ish quartz, a few mid grey and a few warm beige-white stones.
        // All of them are neutral or very slightly warm (red >= green >= blue), never green.
        static Color RTRockColor(float t, float j)
        {
            if (t < .40f) { float v = .80f + .12f * j; return new Color(v, v * .99f, v * .975f); }
            if (t < .72f) { float v = .88f + .10f * j; return new Color(v, v * .99f, v * .975f); }
            if (t < .88f) { float v = .68f + .10f * j; return new Color(v, v * .995f, v * .985f); }
            float q = .90f + .08f * j; return new Color(q, q * .96f, q * .90f);
        }

        // Ballast bed texture: light grey crushed granite chips, angular, with shadowed gaps and dark flecks (red >= green >= blue, no green cast).
        static RTTex RTMakePhotoBallast(int size)
        {
            const int cells = RTPhotoBallastCells;
            var alb = new byte[size * size * 3];
            var hgt = new float[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float fu = x / (float)size * cells, fv = y / (float)size * cells;
                    float wu = fu + .9f * (RTNoise(fu * .5f, fv * .5f, cells / 2, 1901) - .5f);
                    float wv = fv + .9f * (RTNoise(fu * .5f + 3f, fv * .5f + 7f, cells / 2, 1902) - .5f);
                    int xi = Mathf.FloorToInt(wu), yi = Mathf.FloorToInt(wv);
                    float d1 = 99f, d2 = 99f, fx = 0f, fy = 0f;
                    int bx = 0, by = 0;
                    for (int j = -1; j <= 1; j++)
                        for (int i = -1; i <= 1; i++)
                        {
                            int cx = xi + i, cy = yi + j, wx = RTWrap(cx, cells), wy = RTWrap(cy, cells);
                            float px = cx + .1f + .8f * RTHash(wx, wy, 1911);
                            float py = cy + .1f + .8f * RTHash(wx, wy, 1912);
                            float sz = .60f + .95f * RTHash(wx, wy, 1913);
                            float dx = px - wu, dy = py - wv;
                            float ax = Mathf.Abs(dx), ay = Mathf.Abs(dy);
                            float d = (Mathf.Max(ax, ay) * .6f + (ax + ay) * .4f) / sz;     // angular, octagon-like stones of different sizes
                            if (d < d1) { d2 = d1; d1 = d; bx = wx; by = wy; fx = -dx / sz; fy = -dy / sz; }
                            else if (d < d2) d2 = d;
                        }

                    float r1 = RTHash(bx, by, 1921), r2 = RTHash(bx, by, 1922), r3 = RTHash(bx, by, 1923), r4 = RTHash(bx, by, 1924);
                    float cr, cg, cb;
                    if (r2 < .38f) { float v = .66f + .14f * r1; cr = v; cg = v * .99f; cb = v * .975f; }                       // light grey
                    else if (r2 < .70f) { float v = .76f + .12f * r1; cr = v; cg = v * .99f; cb = v * .975f; }                  // pale grey
                    else if (r2 < .82f) { float v = .88f + .08f * r1; cr = v; cg = v * .985f; cb = v * .95f; }                  // white-ish quartz
                    else if (r2 < .93f) { float v = .54f + .12f * r1; cr = v; cg = v * .99f; cb = v * .97f; }                   // mid grey
                    else { cr = .82f + .06f * r1; cg = .76f + .06f * r1; cb = .67f + .05f * r1; }                               // warm beige-white

                    float facet = Mathf.Clamp(.72f + .70f * (.5f + (fx * (r3 - .5f) + fy * (r4 - .5f)) * 1.1f), .60f, 1.30f);
                    float edge = Mathf.Clamp((d2 - d1) * 3.2f, 0f, 1f);
                    float k = facet * (.34f + .66f * Mathf.Pow(edge, .7f)) * 1.05f;      // shadow in the gaps between the stones

                    float spk = RTHash(x * 5 + bx * 7, y * 5 + by * 3, 1906), spk2 = RTHash(x, y, 1909);
                    float speck = spk > .90f ? .10f + .08f * spk2 : (spk < .16f ? -.26f - .08f * spk2 : 0f);   // dark flecks (many) and white flecks (few)

                    float fr = (cr + speck) * k, fg = (cg + speck) * k, fb = (cb + speck) * k;

                    float gap = (1f - edge) * .40f;                                       // neutral dark gaps
                    fr = Mathf.Lerp(fr, .23f, gap); fg = Mathf.Lerp(fg, .225f, gap); fb = Mathf.Lerp(fb, .22f, gap);

                    int o = (y * size + x) * 3;
                    alb[o] = RTByte(fr); alb[o + 1] = RTByte(fg); alb[o + 2] = RTByte(fb);
                    hgt[y * size + x] = edge * (.55f + .45f * r1) + .04f * spk2;
                }
            return new RTTex { Albedo = alb, Normal = RTNormals(hgt, size, 3.0f), Size = size };
        }

        static float RTBallastSurface(float lat)      // height of the ballast surface at a distance from the track centre (matches RTRibbons)
        {
            const float tw = 1.72f, bw = 2.15f, h = .34f;
            if (lat <= tw) return h;
            if (lat <= bw) return h * (bw - lat) / (bw - tw);
            return 0f;
        }

        bool RTChipNearSwitch(Vector3 c)
        {
            foreach (var q in rtChipSwitches) { float dx = q.X - c.X, dz = q.Z - c.Z; if (dx * dx + dz * dz < 3600f) return true; }
            return false;
        }

        // Called at the end of RTV29Init. Makes the light neutral, replaces the ballast bed texture, prepares the stone meshes, the material
        // and the list of track blocks; the stones come later, near the camera.
        void RTRocksBuild()
        {
            RTPerfInit();                // v46: sleeper levels of detail (MainPerf.cs)
            if (sc == null) return;

            // v49: neutral ambient light and fog (the sky ambient was green and blue, which tinted the grey stones)
            foreach (var node in GetChildren())
            {
                var we = node as WorldEnvironment;
                if (we == null || we.Environment == null) continue;
                var en = we.Environment;
                en.AmbientLightSource = Godot.Environment.AmbientSource.Color;
                en.AmbientLightColor = new Color(.66f, .67f, .68f);
                en.AmbientLightEnergy = 1f;
                en.FogLightColor = new Color(.80f, .81f, .83f);
            }

            // the ballast bed texture is the light grey granite chips of the photo
            if (matGravel != null)
            {
                var pb = RTMakePhotoBallast(512);
                matGravel.AlbedoTexture = RTTexture(pb.Albedo, pb.Size, false);
                matGravel.NormalEnabled = true;
                matGravel.NormalTexture = RTTexture(pb.Normal, pb.Size, true);
                matGravel.NormalScale = 1.5f;
                matGravel.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
                matGravel.VertexColorUseAsAlbedo = false;
                matGravel.AlbedoColor = Colors.White;
                matGravel.Roughness = .98f;
            }

            rtChipMeshes = new ArrayMesh[RTChipVariants];
            for (int i = 0; i < RTChipVariants; i++) rtChipMeshes[i] = RTMakeRockMesh(i * 7 + 3, i < 6 ? 0 : 1);
            var spk = RTMakeSpeckle(128);
            rtChipMat = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true, AlbedoTexture = RTTexture(spk.Albedo, spk.Size, false), Roughness = .95f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                Uv1Triplanar = true, Uv1TriplanarSharpness = 4f, Uv1Scale = new Vector3(.9f, .9f, .9f)
            };

            rtChipSwitches.Clear();
            foreach (var n in sc.Graph.Switches) rtChipSwitches.Add(n.Pos);

            rtChipBlocks.Clear(); rtChipLive = 0; rtChipFirst = true;
            int edgeIdx = 0;
            foreach (var e in sc.Graph.Edges.Values)
            {
                int nb = Mathf.Max(1, Mathf.CeilToInt(e.Length / RTChipBlock));
                float len = e.Length / nb;
                for (int bi = 0; bi < nb; bi++)
                {
                    float s0 = bi * len, s1 = s0 + len;
                    Vector3 mid = e.PosAt((s0 + s1) * .5f);
                    rtChipBlocks.Add(new RTChipBlockData { E = e, EdgeIdx = edgeIdx, Index = bi, S0 = s0, S1 = s1, X = mid.X, Z = mid.Z });
                }
                edgeIdx++;
            }

            rtChipRoot = new Node3D(); AddChild(rtChipRoot);
            var timer = new Godot.Timer { WaitTime = .12, Autostart = true };
            timer.Timeout += RTChipTick;
            AddChild(timer);
        }

        // A few times a second: build the blocks that came into range (nearest first) and free the ones left behind.
        void RTChipTick()
        {
            if (cam == null || sc == null || rtChipRoot == null) return;
            RTApplyAA();
            Vector3 cp = cam.GlobalPosition;
            float br2 = RTChipBuildRange * RTChipBuildRange, dr2 = RTChipDropRange * RTChipDropRange;
            rtChipWant.Clear();
            foreach (var b in rtChipBlocks)
            {
                float dx = b.X - cp.X, dz = b.Z - cp.Z; b.D2 = dx * dx + dz * dz;
                if (b.Node == null) { if (b.D2 < br2) rtChipWant.Add(b); }
                else if (b.D2 > dr2) { b.Node.QueueFree(); b.Node = null; rtChipLive--; }
            }
            if (rtChipWant.Count == 0) return;
            rtChipWant.Sort((x, y) => x.D2.CompareTo(y.D2));
            int budget = rtChipFirst ? 10 : 2;       // the first call fills the nearest blocks at once, later calls add 2 blocks each
            for (int i = 0; i < rtChipWant.Count && budget > 0 && rtChipLive < RTChipMaxLive; i++)
            {
                RTChipBuild(rtChipWant[i]); rtChipLive++; budget--;
            }
            rtChipFirst = false;
        }

        // The stones of one 24 m block. The random numbers are seeded from the block, so it always comes out the same.
        void RTChipBuild(RTChipBlockData b)
        {
            var e = b.E;
            var rng = new RandomNumberGenerator();
            rng.Seed = (ulong)(b.EdgeIdx + 1) * 1000003UL + (ulong)(b.Index + 1) * 7919UL + 4949UL;
            var chunks = new RTChunk[RTChipVariants];
            System.Action<Vector3, float, float, float, float, float, Color> put = (lp, lsx, lsy, lsz, ltilt, lyaw, lcol) =>
            {
                // small stones use the cheap 20-face shapes (0..5), the biggest ones the detailed 80-face shapes (6..7)
                int variant = (lsx + lsz) * .5f < .042f ? (int)(rng.Randi() % 6u) : 6 + (int)(rng.Randi() % 2u);
                var lch = chunks[variant]; if (lch == null) { lch = new RTChunk(); chunks[variant] = lch; }
                var bs = new Basis(Vector3.Up, lyaw) * new Basis(Vector3.Right, rng.RandfRange(-ltilt, ltilt)) * new Basis(Vector3.Forward, rng.RandfRange(-ltilt, ltilt)) * Basis.FromScale(new Vector3(lsx, lsy, lsz));
                lch.T.Add(new Transform3D(bs, lp)); lch.C.Add(lcol);
            };

            // ---- the ballast heap: small sharp stones over the shoulder and the slope, some high, some low, some stacked, some rolled down onto the ground ----
            float step = .2f / RTChipDensity;
            for (float s = b.S0; s < b.S1; s += step)
            {
                float ss = Mathf.Clamp(s + rng.RandfRange(-.5f * step, .5f * step), 0f, e.Length);
                Vector3 c = e.PosAt(ss), dir = e.DirAt(ss), r = Right(dir);
                bool nearSw = RTChipNearSwitch(c);
                for (int side = -1; side <= 1; side += 2)
                {
                    if (!nearSw)
                    {
                        for (int rep = 0; rep < 8; rep++)
                        {
                            float lat = rng.RandfRange(1.40f, 2.45f);
                            float size = .013f + .017f * Mathf.Pow(rng.Randf(), 1.5f);          // stones about 3 - 6 cm across, like the photo
                            Vector3 pos = c + dir * rng.RandfRange(-.5f * step, .5f * step) + r * (side * lat);
                            pos.Y = c.Y + RTBallastSurface(lat) + size * rng.RandfRange(-.30f, .45f);   // some sit low in the bed, some stand proud
                            put(pos, size * rng.RandfRange(1.0f, 1.7f), size * rng.RandfRange(.55f, 1.0f), size * rng.RandfRange(.9f, 1.5f), .7f, rng.RandfRange(0f, Mathf.Tau), RTRockColor(rng.Randf(), rng.Randf()));
                            if (rng.Randf() < .30f)
                            {
                                float sTop = size * rng.RandfRange(.6f, .9f);
                                Vector3 pTop = pos + dir * (size * rng.RandfRange(-.8f, .8f)) + r * (side * size * rng.RandfRange(-.8f, .8f));
                                pTop.Y = pos.Y + size * rng.RandfRange(.45f, .85f);
                                put(pTop, sTop * rng.RandfRange(1.0f, 1.6f), sTop * rng.RandfRange(.55f, 1.0f), sTop * rng.RandfRange(.9f, 1.4f), .8f, rng.RandfRange(0f, Mathf.Tau), RTRockColor(rng.Randf(), rng.Randf()));
                            }
                        }

                        for (int sj = 0; sj < 2; sj++)          // stones that rolled down past the toe of the slope onto the ground
                        {
                            float lat = rng.RandfRange(2.10f, 2.90f);
                            float size = .010f + .014f * rng.Randf();
                            Vector3 pos = c + dir * rng.RandfRange(-.5f * step, .5f * step) + r * (side * lat);
                            pos.Y = c.Y + size * rng.RandfRange(-.10f, .35f);
                            put(pos, size * rng.RandfRange(1.0f, 1.7f), size * rng.RandfRange(.55f, 1.0f), size * rng.RandfRange(.9f, 1.5f), .7f, rng.RandfRange(0f, Mathf.Tau), RTRockColor(rng.Randf(), rng.Randf()));
                        }
                    }

                    if (rng.Randf() < .30f)          // a few single stones and little groups further out on the ground
                    {
                        float lat = 2.60f + Mathf.Pow(rng.Randf(), 1.8f) * 3.4f;
                        Vector3 pos = c + dir * rng.RandfRange(-.5f * step, .5f * step) + r * (side * lat);
                        if (RTFree(pos, 2.2f))
                        {
                            float size = .010f + .022f * Mathf.Pow(rng.Randf(), 1.6f);
                            pos.Y = c.Y + size * rng.RandfRange(-.05f, .22f);
                            put(pos, size * rng.RandfRange(.9f, 1.7f), size * rng.RandfRange(.5f, .95f), size * rng.RandfRange(.9f, 1.5f), .55f, rng.RandfRange(0f, Mathf.Tau), RTRockColor(rng.Randf(), rng.Randf()));
                            if (rng.Randf() < .30f)
                            {
                                for (int g2 = 0; g2 < 2; g2++)
                                {
                                    float sg = size * rng.RandfRange(.5f, .9f);
                                    Vector3 pg = pos + dir * (size * rng.RandfRange(-1.6f, 1.6f)) + r * (size * rng.RandfRange(-1.6f, 1.6f));
                                    pg.Y = c.Y + sg * rng.RandfRange(-.02f, .20f);
                                    put(pg, sg * rng.RandfRange(.9f, 1.6f), sg * rng.RandfRange(.5f, .9f), sg * rng.RandfRange(.9f, 1.4f), .55f, rng.RandfRange(0f, Mathf.Tau), RTRockColor(rng.Randf(), rng.Randf()));
                                }
                            }
                        }
                    }
                }
            }

            // ---- the gaps between the sleepers are filled with stones, some stacked (not near points, where the switch rails run) ----
            int cnt = Mathf.Max(1, Mathf.RoundToInt(e.Length / RTSleeperSpacing)); float slStep = e.Length / cnt;
            if (!e.Turnout)
            {
                int j0 = Mathf.Max(1, Mathf.CeilToInt(b.S0 / slStep)), j1 = Mathf.Min(cnt - 1, Mathf.CeilToInt(b.S1 / slStep) - 1);
                for (int j = j0; j <= j1; j++)
                {
                    Vector3 cGap = e.PosAt(j * slStep);
                    if (RTChipNearSwitch(cGap)) continue;
                    int nCrib = Mathf.Max(1, Mathf.RoundToInt(14f * RTChipDensity * (.6f + .8f * rng.Randf())));
                    for (int q = 0; q < nCrib; q++)
                    {
                        float sGap = j * slStep + rng.RandfRange(-.13f, .13f);
                        Vector3 cc = e.PosAt(sGap), rr = Right(e.DirAt(sGap));
                        float lat = rng.RandfRange(-1.30f, 1.30f);
                        float size = .012f + .014f * rng.Randf();
                        Vector3 pos = cc + rr * lat; pos.Y = cc.Y + .34f + size * rng.RandfRange(-.20f, .60f);
                        put(pos, size * rng.RandfRange(1.0f, 1.6f), size * rng.RandfRange(.6f, 1.0f), size * rng.RandfRange(.9f, 1.4f), .7f, rng.RandfRange(0f, Mathf.Tau), RTRockColor(rng.Randf(), rng.Randf()));
                        if (rng.Randf() < .30f)
                        {
                            float s2 = size * .75f;
                            Vector3 p2 = pos + rr * (size * rng.RandfRange(-.7f, .7f));
                            p2.Y = pos.Y + size * rng.RandfRange(.5f, .9f);
                            put(p2, s2 * rng.RandfRange(1.0f, 1.5f), s2 * rng.RandfRange(.6f, 1.0f), s2 * rng.RandfRange(.9f, 1.4f), .8f, rng.RandfRange(0f, Mathf.Tau), RTRockColor(rng.Randf(), rng.Randf()));
                        }
                    }
                }
            }

            // ---- stones lying on top of the concrete sleepers: ONLY AT SOME PLACES, chosen at random ----
            // A smooth noise along the track (same for the same sleeper every time) marks the places. Outside them the sleepers stay clean.
            // Inside a place only some sleepers carry a stone, sometimes two.
            int k0 = Mathf.Max(0, Mathf.CeilToInt(b.S0 / slStep - .5f));
            for (int k = k0; k < cnt; k++)
            {
                float st = (k + .5f) * slStep; if (st >= b.S1) break;
                float place = RTNoise(k * .09f, b.EdgeIdx * 3.1f + .5f, 61, 7001);
                if (place < RTSleeperRockPatch) continue;
                if (rng.Randf() > RTSleeperRockChance) continue;
                int nChip = 1 + (rng.Randf() < .35f ? 1 : 0);
                Vector3 p = e.PosAt(st); var qb = Basis.LookingAt(e.DirAt(st), Vector3.Up); Vector3 sp = p + Vector3.Up * .345f;
                for (int ci = 0; ci < nChip; ci++)
                {
                    float lx = rng.RandfRange(-1.30f, 1.30f);
                    if (Mathf.Abs(Mathf.Abs(lx) - RailG) < .12f) lx += lx < 0f ? -.20f : .20f;      // keep clear of the rail seats
                    lx = Mathf.Clamp(lx, -1.30f, 1.30f);
                    float lz = (rng.Randf() - .5f) * .16f;
                    float size = .011f + .010f * rng.Randf();
                    float nx = lx / 1.375f, top = .5f - .22f * (1f - nx * nx);                     // the sleeper top is slightly curved
                    Vector3 pos = sp + qb.X * lx + qb.Z * lz; pos.Y = sp.Y + .21f * top + size * .16f;
                    put(pos, size * rng.RandfRange(1.15f, 1.8f), size * rng.RandfRange(.55f, .85f), size * rng.RandfRange(1.0f, 1.55f), .5f, rng.RandfRange(0f, Mathf.Tau), RTRockColor(rng.Randf(), rng.Randf()));
                }
            }

            // ---- one MultiMesh per kind of stone for this block ----
            var holder = new Node3D();
            for (int v = 0; v < RTChipVariants; v++)
            {
                var ck = chunks[v]; if (ck == null || ck.T.Count == 0) continue;
                var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = rtChipMeshes[v], InstanceCount = ck.T.Count };
                for (int i = 0; i < ck.T.Count; i++) { mm.SetInstanceTransform(i, ck.T[i]); mm.SetInstanceColor(i, ck.C[i]); }
                holder.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = rtChipMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            }
            rtChipRoot.AddChild(holder); b.Node = holder;
        }
    }
}
