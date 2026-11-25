using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // v46: small crushed-granite stones, made only around the camera.
    //
    // v45 problems:
    //  - the stones were far too big (a "small" one was 20-60 cm long; real ballast is 3-6 cm)
    //  - about 1.2 million of them were created while loading. They were grouped by (x+z), so on a diagonal line a single group could hold
    //    a whole kilometre of stones, and all of it was drawn (80 triangles each) whenever any part of it was in view
    //
    // Now:
    //  - stones are 3-7 cm (a few up to 15 cm), the same size as the stones in the ballast texture
    //  - the track is cut into 32 m pieces. A piece gets its stones only while it is within reach of the camera and loses them again once
    //    the train is well past (nothing is made at load time, memory stays small on any route length, the first stones appear a moment after the start)
    //  - a stone is a 20-face crushed-granite polyhedron with flat faces and sharp edges: at 5 cm it is only a few pixels wide, more faces
    //    would not be visible
    //  - pieces beyond the draw distance are hidden (the distance follows the graphics level: x0.6 Fast .. x1.3 Ultra)
    //  - little stones lie ON the sleepers (never over the rail seats), between the sleepers, on the ballast shoulder and beside the track
    //  - stones never cast shadows
    //
    // Tuning knobs are the constants just below.
    public partial class Main
    {
        // ---- tuning knobs ----
        const float RTRockDensity = 1f;        // 1 = normal, .5 = half as many stones (faster), 2 = twice as many
        const float RTRockVisRange = 55f;      // metres from the camera (scaled by the graphics level)
        const float RTRockStep = .5f;          // metres along the track between stone groups at density 1
        const int RTRockVariants = 4;          // four different stone shapes
        const float RT46PieceLen = 32f;        // length of one piece of track that gets its stones together

        sealed class RT46Piece
        {
            public TEdge E; public int EdgeIdx, Index; public float S0, S1; public Vector3 A, M, B; public Node3D Node;
        }
        readonly List<RT46Piece> rt46Pieces = new List<RT46Piece>();
        readonly List<RT46Piece> rt46Live = new List<RT46Piece>();
        readonly List<Vector3> rt46Switches = new List<Vector3>();
        ArrayMesh[] rt46Meshes; StandardMaterial3D rt46Mat; Godot.Timer rt46Timer; bool rt46Ready;

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

        // Called at the end of RTV29Init. Makes the stone shapes and material and starts the timer that creates / removes stones around the camera.
        void RTRocksBuild()
        {
            if (sc == null) return;
            foreach (var old in rt46Live) if (old.Node != null) old.Node.QueueFree();
            rt46Live.Clear(); rt46Pieces.Clear(); rt46Ready = false;

            rt46Meshes = new ArrayMesh[RTRockVariants];
            for (int i = 0; i < RTRockVariants; i++) rt46Meshes[i] = RTMakeRockMesh(i * 7 + 3, 0);
            var spk = RTMakeSpeckle(128);
            rt46Mat = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true, AlbedoTexture = RTTexture(spk.Albedo, spk.Size, false), Roughness = .95f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                Uv1Triplanar = true, Uv1TriplanarSharpness = 4f, Uv1Scale = new Vector3(.9f, .9f, .9f)
            };

            rt46Switches.Clear();
            foreach (var n in sc.Graph.Switches) rt46Switches.Add(n.Pos);

            if (rt46Timer == null)
            {
                rt46Timer = new Godot.Timer { WaitTime = .25, OneShot = false };
                rt46Timer.Timeout += RT46RockTick;
                AddChild(rt46Timer); rt46Timer.Start();
            }
        }

        // The track cut into pieces (made on the first tick, when the track geometry is final).
        void RT46Prepare()
        {
            rt46Ready = true; rt46Pieces.Clear();
            int ei = 0;
            foreach (var e in sc.Graph.Edges.Values)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(e.Length / RT46PieceLen));
                for (int i = 0; i < n; i++)
                {
                    float s0 = i * RT46PieceLen, s1 = Mathf.Min(e.Length, s0 + RT46PieceLen);
                    if (s1 <= s0) continue;
                    rt46Pieces.Add(new RT46Piece { E = e, EdgeIdx = ei, Index = i, S0 = s0, S1 = s1, A = e.PosAt(s0), M = e.PosAt((s0 + s1) * .5f), B = e.PosAt(s1) });
                }
                ei++;
            }
        }

        static float RT46SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a; float l2 = ab.LengthSquared();
            float t = l2 > 1e-6f ? Mathf.Clamp((p - a).Dot(ab) / l2, 0f, 1f) : 0f;
            return p.DistanceTo(a + ab * t);
        }

        static float RT46PieceDist(RT46Piece pc, Vector3 p)
        {
            return Mathf.Min(RT46SegDist(p, pc.A, pc.M), RT46SegDist(p, pc.M, pc.B));
        }

        bool RT46NearSwitch(Vector3 p)
        {
            for (int i = 0; i < rt46Switches.Count; i++)
            {
                float dx = rt46Switches[i].X - p.X, dz = rt46Switches[i].Z - p.Z;
                if (dx * dx + dz * dz < 3600f) return true;
            }
            return false;
        }

        // Four times a second: hide pieces beyond the draw distance, remove pieces the train has left behind, make the nearest missing pieces.
        void RT46RockTick()
        {
            if (sc == null || cam == null || rt46Meshes == null) return;
            if (!rt46Ready) RT46Prepare();
            if (rt46Pieces.Count == 0) return;

            Vector3 cp = cam.GlobalPosition;
            float show = RTRockVisRange * visScale, build = show + 70f, drop = show + 140f;

            for (int i = rt46Live.Count - 1; i >= 0; i--)
            {
                var pc = rt46Live[i]; float d = RT46PieceDist(pc, cp);
                if (d > drop) { pc.Node.QueueFree(); pc.Node = null; rt46Live.RemoveAt(i); continue; }
                bool vis = d < show;
                if (pc.Node.Visible != vis) pc.Node.Visible = vis;
            }

            ulong t0 = Godot.Time.GetTicksUsec(), budget = rt46Live.Count == 0 ? 90000UL : 6000UL;      // the first fill may take longer, later pieces only a few ms per tick
            float reach = build + RT46PieceLen;
            for (int n = 0; n < 8; n++)
            {
                RT46Piece best = null; float bd = build;
                for (int i = 0; i < rt46Pieces.Count; i++)
                {
                    var pc = rt46Pieces[i]; if (pc.Node != null) continue;
                    float dx = pc.M.X - cp.X, dz = pc.M.Z - cp.Z;
                    if (dx * dx + dz * dz > reach * reach) continue;
                    float d = RT46PieceDist(pc, cp);
                    if (d < bd) { bd = d; best = pc; }
                }
                if (best == null) break;
                RT46BuildPiece(best);
                best.Node.Visible = bd < show;
                if (Godot.Time.GetTicksUsec() - t0 > budget) break;
            }
        }

        // All stones of one 32 m piece of track. The random numbers are seeded per piece, so a piece always looks the same.
        void RT46BuildPiece(RT46Piece pc)
        {
            var e = pc.E; float s0 = pc.S0, s1 = pc.S1;
            var rnd = new System.Random(unchecked((pc.EdgeIdx + 1) * 73856093 ^ (pc.Index + 3) * 19349663 ^ 46046));
            var T = new List<Transform3D>[RTRockVariants]; var C = new List<Color>[RTRockVariants];
            for (int v = 0; v < RTRockVariants; v++) { T[v] = new List<Transform3D>(); C[v] = new List<Color>(); }
            System.Func<float> R = () => (float)rnd.NextDouble();
            System.Func<float, float, float> RR = (lo, hi) => lo + (hi - lo) * (float)rnd.NextDouble();
            System.Action<Vector3, float, float, float, float, float, Color> put = (pos, sx, sy, sz, tilt, yaw, col) =>
            {
                int pick = rnd.Next(RTRockVariants);
                var bs = new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, RR(-tilt, tilt)) * new Basis(Vector3.Forward, RR(-tilt, tilt)) * Basis.FromScale(new Vector3(sx, sy, sz));
                T[pick].Add(new Transform3D(bs, pos)); C[pick].Add(col);
            };

            // ---- along the track: shoulder and slope of the ballast, ground pebbles, a few bigger rocks, mud ----
            float step = RTRockStep / Mathf.Max(.1f, RTRockDensity);
            for (float s = s0; s < s1; s += step)
            {
                float ss = Mathf.Clamp(s + RR(-.2f, .2f), 0f, e.Length);
                Vector3 c = e.PosAt(ss), dir = e.DirAt(ss), r = Right(dir);
                bool nearSwitch = RT46NearSwitch(c);

                for (int side = -1; side <= 1; side += 2)
                {
                    if (!nearSwitch)
                    {
                        // shoulder and slope of the ballast: small crushed stones, tilted every way, some sunk in, some high,
                        // and about 1 in 3 carries a smaller stone resting on top
                        int nShoulder = 4 + (R() < .35f ? 1 : 0);
                        for (int k = 0; k < nShoulder; k++)
                        {
                            float lat = RR(1.40f, 2.35f);
                            float size = .012f + .013f * Mathf.Pow(R(), 1.4f);
                            Vector3 pos = c + dir * RR(-.25f, .25f) + r * (side * lat);
                            pos.Y = c.Y + RTBallastSurface(lat) + size * RR(-.10f, .28f);
                            put(pos, size * RR(1.0f, 1.7f), size * RR(.55f, 1.0f), size * RR(.9f, 1.5f), .6f, RR(0f, Mathf.Tau), RTRockColor(R(), R()));
                            if (R() < .35f)
                            {
                                float sTop = size * RR(.45f, .75f);
                                Vector3 pTop = pos + dir * (size * RR(-.6f, .6f)) + r * (side * size * RR(-.6f, .6f));
                                pTop.Y = pos.Y + size * RR(.28f, .50f);
                                put(pTop, sTop * RR(1.0f, 1.6f), sTop * RR(.55f, 1.0f), sTop * RR(.9f, 1.4f), .7f, RR(0f, Mathf.Tau), RTRockColor(R(), R()));
                            }
                        }
                    }

                    // ground beside the ballast: small stones, many near the ballast and fewer further out, sometimes in little groups
                    int nGround = R() < .30f ? 2 : 1;
                    for (int k = 0; k < nGround; k++)
                    {
                        float lat = 2.20f + Mathf.Pow(R(), 1.8f) * 3.8f;
                        Vector3 pos = c + dir * RR(-.25f, .25f) + r * (side * lat);
                        if (!RTFree(pos, 2.2f)) continue;
                        float size = .008f + .012f * Mathf.Pow(R(), 1.6f);
                        pos.Y = c.Y + size * RR(-.05f, .22f);
                        put(pos, size * RR(.9f, 1.7f), size * RR(.5f, .95f), size * RR(.9f, 1.5f), .55f, RR(0f, Mathf.Tau), RTRockColor(R(), R()));
                        if (R() < .12f)
                        {
                            for (int g2 = 0; g2 < 2; g2++)
                            {
                                float sg = size * RR(.40f, .80f);
                                Vector3 pg = pos + dir * (size * RR(-1.3f, 1.3f)) + r * (size * RR(-1.3f, 1.3f));
                                pg.Y = c.Y + sg * RR(-.02f, .20f);
                                put(pg, sg * RR(.9f, 1.6f), sg * RR(.5f, .9f), sg * RR(.9f, 1.4f), .55f, RR(0f, Mathf.Tau), RTRockColor(R(), R()));
                            }
                        }
                    }

                    // now and then a bigger rock (10-15 cm) with a few stones around it
                    if (R() < .02f)
                    {
                        float lat = RR(2.5f, 5.5f);
                        Vector3 pos = c + dir * RR(-.25f, .25f) + r * (side * lat);
                        if (RTFree(pos, 2.2f))
                        {
                            float size = .030f + .020f * R();
                            pos.Y = c.Y + size * RR(.05f, .22f);
                            put(pos, size * RR(1.0f, 1.5f), size * RR(.6f, .9f), size * RR(.9f, 1.3f), .35f, RR(0f, Mathf.Tau), RTRockColor(R(), R()));
                            for (int b2 = 0; b2 < 3; b2++)
                            {
                                float sb = size * RR(.25f, .50f);
                                Vector3 pb = pos + dir * (size * RR(-1.3f, 1.3f)) + r * (size * RR(-1.3f, 1.3f));
                                pb.Y = c.Y + sb * RR(0f, .25f);
                                put(pb, sb * RR(1.0f, 1.6f), sb * RR(.55f, 1.0f), sb * RR(.9f, 1.4f), .55f, RR(0f, Mathf.Tau), RTRockColor(R(), R()));
                            }
                        }
                    }

                    // flat dark mud patches on the ground (3 overlapping clumps)
                    if (R() < .10f)
                    {
                        float lat = 2.3f + R() * 2.6f;
                        Vector3 pos0 = c + dir * RR(-.3f, .3f) + r * (side * lat);
                        if (RTFree(pos0, 2.2f))
                        {
                            float mud = R();
                            for (int j = 0; j < 3; j++)
                            {
                                float size = .04f + .07f * R();
                                Vector3 pos = pos0 + dir * RR(-.15f, .15f) + r * RR(-.15f, .15f);
                                pos.Y = c.Y + size * .04f;
                                float wet = mud * RR(.7f, 1.0f);
                                put(pos, size * RR(1.0f, 1.8f), size * .22f, size * RR(.8f, 1.5f), .12f, RR(0f, Mathf.Tau), new Color(.30f + .10f * wet, .22f + .07f * wet, .15f + .05f * wet));
                            }
                        }
                    }
                }
            }

            // ---- between and on the sleepers (not near points, where the switch rails run) ----
            if (!e.Turnout)
            {
                int cnt = Mathf.Max(1, Mathf.RoundToInt(e.Length / RTSleeperSpacing)); float slStep = e.Length / cnt;

                // small stones lying in the gaps between the sleepers
                for (int j = Mathf.Max(1, Mathf.CeilToInt(s0 / slStep)); j < cnt; j++)
                {
                    float gap = j * slStep; if (gap >= s1) break;
                    if (RT46NearSwitch(e.PosAt(gap))) continue;
                    int nCrib = Mathf.Max(1, Mathf.RoundToInt(2.6f * RTRockDensity * (.6f + .8f * R())));
                    for (int j2 = 0; j2 < nCrib; j2++)
                    {
                        float sAt = Mathf.Clamp(gap + RR(-.07f, .07f), 0f, e.Length);
                        Vector3 cc = e.PosAt(sAt), rc = Right(e.DirAt(sAt));
                        float zone = R(), lat;
                        if (zone < .30f) lat = RR(-.55f, .55f);
                        else if (zone < .65f) lat = RR(1.00f, 1.30f);
                        else lat = -RR(1.00f, 1.30f);
                        float size = .011f + .010f * R();
                        Vector3 pos = cc + rc * lat; pos.Y = cc.Y + .34f + size * RR(-.05f, .22f);
                        put(pos, size * RR(1.0f, 1.5f), size * RR(.6f, .95f), size * RR(.9f, 1.3f), .6f, RR(0f, Mathf.Tau), RTRockColor(R(), R()));
                    }
                }

                // little crushed-granite chips lying ON the sleepers: on the middle part and on the ends, never over the rail seats (rails stand at +-0.873 m)
                for (int k = Mathf.Max(0, Mathf.CeilToInt(s0 / slStep - .5f)); k < cnt; k++)
                {
                    float st = (k + .5f) * slStep; if (st >= s1) break;
                    float h0 = RTHash(k, 77, 4601);
                    int nTop = h0 > .45f ? (h0 > .80f ? (h0 > .94f ? 3 : 2) : 1) : 0;
                    if (nTop == 0) continue;
                    Vector3 p = e.PosAt(st);
                    if (RT46NearSwitch(p)) continue;
                    var q = Basis.LookingAt(e.DirAt(st), Vector3.Up);
                    Vector3 sp = p + Vector3.Up * .345f;
                    for (int t = 0; t < nTop; t++)
                    {
                        float lx = R() < .35f ? RR(-.62f, .62f) : (R() < .5f ? -1f : 1f) * RR(1.06f, 1.30f);
                        float lz = RR(-.075f, .075f);
                        float nx = lx / 1.375f;
                        float top = .5f - .22f * (1f - nx * nx);                      // height of the bowed sleeper top at this spot
                        float size = .011f + .011f * R();
                        Vector3 pos = sp + q.X * lx + q.Z * lz + q.Y * (.21f * top + size * .10f);
                        put(pos, size * RR(1.0f, 1.6f), size * RR(.5f, .9f), size * RR(.9f, 1.4f), .5f, RR(0f, Mathf.Tau), RTRockColor(R(), R()));
                    }
                }
            }

            // ---- one MultiMesh per stone shape, all under one node so the whole piece can be hidden or removed at once ----
            var holder = new Node3D(); AddChild(holder);
            for (int vi = 0; vi < RTRockVariants; vi++)
            {
                int count = T[vi].Count; if (count == 0) continue;
                var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = rt46Meshes[vi], InstanceCount = count };
                for (int i = 0; i < count; i++) { mm.SetInstanceTransform(i, T[vi][i]); mm.SetInstanceColor(i, C[vi][i]); }
                holder.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = rt46Mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            }
            pc.Node = holder; rt46Live.Add(pc);
        }
    }
}
