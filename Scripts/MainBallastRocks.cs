using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // v46: small crushed-granite chips, built only around the camera.
    // v45 made every rock for the whole route at load time: hundreds of thousands of stones, and each one was big (up to 40 cm).
    // Now:
    //  - The stones are real ballast size: chips about 2 - 8 cm across, a few a bit bigger. Same sharp plane-cut granite shapes as v45.
    //  - The route is cut into 24 m blocks. A block only gets its stones while it is within RTChipBuildRange of the camera, and they are
    //    freed again when you drive away (RTChipDropRange). The stones of a block are always the same, so nothing changes when you come back.
    //    This keeps the triangle count, memory and loading time small, so the game runs smoothly.
    //  - Chips on the ballast shoulder and slope (some with a smaller chip on top), pebbles on the ground beside it,
    //    stones lying between the sleepers, and little granite chips lying on top of the sleepers.
    // The ballast texture, normal map and all other textures are untouched.
    public partial class Main
    {
        // ---- tuning knobs ----
        const float RTChipDensity = 1f;           // 1 = normal, .5 = half as many chips (faster), 2 = twice as many
        const float RTChipBuildRange = 30f;       // metres: track blocks closer than this to the camera get their stones
        const float RTChipDropRange = 48f;        // metres: blocks further away than this are freed
        const float RTChipBlock = 24f;            // metres of track per block
        const int RTChipVariants = 6;             // 0..3 detailed chips, 4..5 cheap pebbles
        const int RTChipMaxLive = 30;             // never more than this many blocks at once (busy station yards)

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
            float ao = Mathf.Clamp(.74f + .55f * (y + .25f), .55f, 1f);     // darker toward the bottom of the stone
            float k = shade * ao;
            return new Color(k, k * .99f, k * 1.01f);
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
                    float shade = .70f + .40f * RTHash((qx + 5) * 11 + (qy + 5), (qz + 5) * 17 + seed, 5101);   // one shade per flat plane
                    st.SetColor(RTRockVertexColor(shade, a.Y)); st.SetNormal(n); st.AddVertex(a);
                    st.SetColor(RTRockVertexColor(shade, b.Y)); st.SetNormal(n); st.AddVertex(b);
                    st.SetColor(RTRockVertexColor(shade, c.Y)); st.SetNormal(n); st.AddVertex(c);
                }
            });
        }

        // Fine salt-and-pepper mineral speckle, mapped on the rocks in object space.
        static RTTex RTMakeSpeckle(int size)
        {
            var alb = new byte[size * size * 3];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float h = RTHash(x, y, 5200), n = RTFbm(x / (float)size, y / (float)size, 6, 3, 5201);
                    float v = .80f + .20f * n;
                    if (h > .90f) v += .18f; else if (h < .08f) v -= .25f;
                    int o = (y * size + x) * 3;
                    alb[o] = RTByte(v); alb[o + 1] = RTByte(v * .99f); alb[o + 2] = RTByte(v * .98f);
                }
            return new RTTex { Albedo = alb, Normal = null, Size = size };
        }

        // Granite colours: grey, light grey, dark slate, pink granite, warm grey-brown, pale quartz.
        static Color RTRockColor(float t, float j)
        {
            if (t < .26f) { float v = .56f + .12f * j; return new Color(v, v, v * 1.02f); }
            if (t < .50f) { float v = .72f + .14f * j; return new Color(v, v * .99f, v * .98f); }
            if (t < .64f) { float v = .36f + .10f * j; return new Color(v, v * 1.01f, v * 1.05f); }
            if (t < .78f) return new Color(.68f + .08f * j, .52f + .06f * j, .48f + .05f * j);
            if (t < .90f) return new Color(.58f + .08f * j, .51f + .07f * j, .43f + .05f * j);
            float q = .84f + .10f * j; return new Color(q, q * .99f, q * .96f);
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

        // Called at the end of RTV29Init. Only prepares the meshes, the material and the list of track blocks; the stones come later, near the camera.
        void RTRocksBuild()
        {
            RTPerfInit();                // v46: sleeper levels of detail (MainPerf.cs)
            if (sc == null) return;
            rtChipMeshes = new ArrayMesh[RTChipVariants];
            for (int i = 0; i < RTChipVariants; i++) rtChipMeshes[i] = RTMakeRockMesh(i * 7 + 3, i < 4 ? 1 : 0);
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
            int budget = rtChipFirst ? 40 : 2;       // the first call fills the whole view at once, later calls add 2 blocks each
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
            rng.Seed = (ulong)(b.EdgeIdx + 1) * 1000003UL + (ulong)(b.Index + 1) * 7919UL + 4646UL;
            var chunks = new RTChunk[RTChipVariants];
            System.Action<Vector3, float, float, float, float, float, Color> put = (lp, lsx, lsy, lsz, ltilt, lyaw, lcol) =>
            {
                // tiny stones use the cheap 20-face meshes, bigger ones the detailed 80-face meshes
                int variant = (lsx + lsz) * .5f < .050f ? 4 + (int)(rng.Randi() % 2u) : (int)(rng.Randi() % 4u);
                var lch = chunks[variant]; if (lch == null) { lch = new RTChunk(); chunks[variant] = lch; }
                var bs = new Basis(Vector3.Up, lyaw) * new Basis(Vector3.Right, rng.RandfRange(-ltilt, ltilt)) * new Basis(Vector3.Forward, rng.RandfRange(-ltilt, ltilt)) * Basis.FromScale(new Vector3(lsx, lsy, lsz));
                lch.T.Add(new Transform3D(bs, lp)); lch.C.Add(lcol);
            };

            // ---- chips on the shoulder and slope of the ballast, and pebbles on the ground beside it ----
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
                        float lat = rng.RandfRange(1.42f, 2.35f);
                        float size = .016f + .032f * Mathf.Pow(rng.Randf(), 1.5f);          // radius 1.6 - 4.8 cm: small crushed granite
                        Vector3 pos = c + dir * rng.RandfRange(-.5f * step, .5f * step) + r * (side * lat);
                        pos.Y = c.Y + RTBallastSurface(lat) + size * rng.RandfRange(-.10f, .30f);
                        put(pos, size * rng.RandfRange(1.0f, 1.7f), size * rng.RandfRange(.55f, 1.0f), size * rng.RandfRange(.9f, 1.5f), .6f, rng.RandfRange(0f, Mathf.Tau), RTRockColor(rng.Randf(), rng.Randf()));
                        if (rng.Randf() < .35f)
                        {
                            float sTop = size * rng.RandfRange(.5f, .8f);
                            Vector3 pTop = pos + dir * (size * rng.RandfRange(-.6f, .6f)) + r * (side * size * rng.RandfRange(-.6f, .6f));
                            pTop.Y = pos.Y + size * rng.RandfRange(.30f, .55f);
                            put(pTop, sTop * rng.RandfRange(1.0f, 1.6f), sTop * rng.RandfRange(.55f, 1.0f), sTop * rng.RandfRange(.9f, 1.4f), .7f, rng.RandfRange(0f, Mathf.Tau), RTRockColor(rng.Randf(), rng.Randf()));
                        }
                    }

                    if (rng.Randf() < .30f)
                    {
                        float lat = 2.20f + Mathf.Pow(rng.Randf(), 1.8f) * 3.8f;
                        Vector3 pos = c + dir * rng.RandfRange(-.5f * step, .5f * step) + r * (side * lat);
                        if (RTFree(pos, 2.2f))
                        {
                            float size = .012f + .030f * Mathf.Pow(rng.Randf(), 1.6f);
                            pos.Y = c.Y + size * rng.RandfRange(-.05f, .22f);
                            put(pos, size * rng.RandfRange(.9f, 1.7f), size * rng.RandfRange(.5f, .95f), size * rng.RandfRange(.9f, 1.5f), .55f, rng.RandfRange(0f, Mathf.Tau), RTRockColor(rng.Randf(), rng.Randf()));
                            if (rng.Randf() < .25f)
                            {
                                for (int g2 = 0; g2 < 2; g2++)
                                {
                                    float sg = size * rng.RandfRange(.45f, .85f);
                                    Vector3 pg = pos + dir * (size * rng.RandfRange(-1.6f, 1.6f)) + r * (size * rng.RandfRange(-1.6f, 1.6f));
                                    pg.Y = c.Y + sg * rng.RandfRange(-.02f, .20f);
                                    put(pg, sg * rng.RandfRange(.9f, 1.6f), sg * rng.RandfRange(.5f, .9f), sg * rng.RandfRange(.9f, 1.4f), .55f, rng.RandfRange(0f, Mathf.Tau), RTRockColor(rng.Randf(), rng.Randf()));
                                }
                            }
                        }
                    }
                }
            }

            // ---- stones between the sleepers (not near points, where the switch rails run) and little chips lying on the sleepers ----
            int cnt = Mathf.Max(1, Mathf.RoundToInt(e.Length / RTSleeperSpacing)); float slStep = e.Length / cnt;
            if (!e.Turnout)
            {
                int j0 = Mathf.Max(1, Mathf.CeilToInt(b.S0 / slStep)), j1 = Mathf.Min(cnt - 1, Mathf.CeilToInt(b.S1 / slStep) - 1);
                for (int j = j0; j <= j1; j++)
                {
                    Vector3 cGap = e.PosAt(j * slStep);
                    if (RTChipNearSwitch(cGap)) continue;
                    int nCrib = Mathf.Max(1, Mathf.RoundToInt(3.6f * RTChipDensity * (.6f + .8f * rng.Randf())));
                    for (int q = 0; q < nCrib; q++)
                    {
                        float sGap = j * slStep + rng.RandfRange(-.07f, .07f);
                        Vector3 cc = e.PosAt(sGap), rr = Right(e.DirAt(sGap));
                        float zone = rng.Randf(), lat;
                        if (zone < .30f) lat = rng.RandfRange(-.55f, .55f);
                        else if (zone < .65f) lat = rng.RandfRange(1.00f, 1.30f);
                        else lat = -rng.RandfRange(1.00f, 1.30f);
                        float size = .014f + .016f * rng.Randf();
                        Vector3 pos = cc + rr * lat; pos.Y = cc.Y + .34f + size * rng.RandfRange(-.05f, .22f);
                        put(pos, size * rng.RandfRange(1.0f, 1.5f), size * rng.RandfRange(.6f, .95f), size * rng.RandfRange(.9f, 1.3f), .6f, rng.RandfRange(0f, Mathf.Tau), RTRockColor(rng.Randf(), rng.Randf()));
                    }
                }
            }

            // little granite chips lying on top of the concrete sleepers (about one per sleeper, sometimes two or three)
            int k0 = Mathf.Max(0, Mathf.CeilToInt(b.S0 / slStep - .5f));
            for (int k = k0; k < cnt; k++)
            {
                float st = (k + .5f) * slStep; if (st >= b.S1) break;
                int nChip = (rng.Randf() < .65f ? 1 : 0) + (rng.Randf() < .30f ? 1 : 0) + (rng.Randf() < .10f ? 1 : 0);
                if (nChip == 0) continue;
                Vector3 p = e.PosAt(st); var qb = Basis.LookingAt(e.DirAt(st), Vector3.Up); Vector3 sp = p + Vector3.Up * .345f;
                for (int ci = 0; ci < nChip; ci++)
                {
                    float lx = rng.RandfRange(-1.30f, 1.30f);
                    if (Mathf.Abs(Mathf.Abs(lx) - RailG) < .12f) lx += lx < 0f ? -.20f : .20f;      // keep clear of the rail seats
                    lx = Mathf.Clamp(lx, -1.30f, 1.30f);
                    float lz = (rng.Randf() - .5f) * .16f;
                    float size = .010f + .018f * rng.Randf();
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
