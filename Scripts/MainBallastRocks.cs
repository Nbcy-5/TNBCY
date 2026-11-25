using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // v45: more realistic 3D ballast rocks.
    // v44 rocks were smooth 12-corner blobs, few in number, each lying alone on the surface.
    // Now:
    //  - Every rock is a crushed-stone polyhedron: a sphere cut by 7-11 random flat planes, so it has real flat faces and
    //    sharp edges (like broken granite). 4 detailed variants (80 faces) and 2 cheaper pebble variants (20 faces).
    //  - Faces are shaded per plane, and the lower part of each rock is darker, so stones show depth where they touch.
    //  - About 3x as many stones on the ballast shoulder, with smaller stones resting ON TOP of bigger ones,
    //    and some stones sunk into the surface while others sit high (one up, one down).
    //  - Pebble groups, a few boulder piles and patches of flat mud on the ground beside the ballast.
    //  - More stones lying between the sleepers.
    // Rocks are only drawn near the camera (RTRockVisRange metres); far away the textured bed is used.
    public partial class Main
    {
        // ---- tuning knobs ----
        const float RTRockDensity = 1f;        // 1 = normal, .5 = half as many rocks (faster), 2 = twice as many
        const float RTRockVisRange = 150f;     // metres from the camera (scaled by the graphics level)
        const float RTRockStep = .5f;          // metres along the track between rock groups at density 1
        const int RTRockVariants = 6;          // 0..3 detailed rocks, 4..5 small pebbles

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

        void RTRocksBuild()
        {
            if (sc == null) return;
            var meshes = new ArrayMesh[RTRockVariants];
            for (int i = 0; i < RTRockVariants; i++) meshes[i] = RTMakeRockMesh(i * 7 + 3, i < 4 ? 1 : 0);
            var spk = RTMakeSpeckle(128);
            var mat = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true, AlbedoTexture = RTTexture(spk.Albedo, spk.Size, false), Roughness = .95f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                Uv1Triplanar = true, Uv1TriplanarSharpness = 4f, Uv1Scale = new Vector3(.9f, .9f, .9f)
            };

            // where the track and the platforms are (so ground pebbles do not land on a platform or another track)
            rtCenters.Clear();
            foreach (var e in sc.Graph.Edges.Values) for (float s = 0f; s <= e.Length; s += 1f) RTAddCenter(e.PosAt(s));
            foreach (var stn in sc.Graph.Stations)
            {
                float sm = stn.Side == "L" ? -1f : 1f;
                float s0 = Mathf.Max(0f, stn.Offset - 625f), s1 = Mathf.Min(stn.Edge.Length, stn.Offset + 12f);
                for (float s = s0; s <= s1; s += 2f)
                {
                    Vector3 p = stn.Edge.PosAt(s), r = Right(stn.Edge.DirAt(s)) * sm;
                    RTAddCenter(p + r * 1.8f); RTAddCenter(p + r * 3.5f); RTAddCenter(p + r * 5.2f);
                }
            }
            var swPos = new List<Vector3>();
            foreach (var n in sc.Graph.Switches) swPos.Add(n.Pos);

            var rng = new RandomNumberGenerator { Seed = 4545 };
            float step = RTRockStep / Mathf.Max(.1f, RTRockDensity);
            foreach (var e in sc.Graph.Edges.Values)
            {
                var chunks = new Dictionary<int, RTChunk>();
                System.Action<Vector3, float, float, float, float, float, Color> put = (pos, sx, sy, sz, tilt, yaw, col) =>
                {
                    // small stones use the cheap pebble meshes, bigger ones the detailed meshes
                    int variant = (sx + sz) * .5f < .075f ? 4 + (int)(rng.Randi() % 2u) : (int)(rng.Randi() % 4u);
                    int key = Mathf.FloorToInt((pos.X + pos.Z) * .0083f) * RTRockVariants + variant;       // coarse spatial chunk (about 120 m), so the distance culling works per chunk
                    RTChunk ch; if (!chunks.TryGetValue(key, out ch)) { ch = new RTChunk(); chunks[key] = ch; }
                    var bs = new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, rng.RandfRange(-tilt, tilt)) * new Basis(Vector3.Forward, rng.RandfRange(-tilt, tilt)) * Basis.FromScale(new Vector3(sx, sy, sz));
                    ch.T.Add(new Transform3D(bs, pos)); ch.C.Add(col);
                };

                // ---- rocks along the track: shoulder and slope of the ballast, ground pebbles, boulders, mud ----
                for (float s = 0f; s < e.Length; s += step)
                {
                    float ss = Mathf.Clamp(s + rng.RandfRange(-.2f, .2f), 0f, e.Length);
                    Vector3 c = e.PosAt(ss), dir = e.DirAt(ss), r = Right(dir);
                    bool nearSwitch = false;
                    foreach (var q in swPos) { float dx = q.X - c.X, dz = q.Z - c.Z; if (dx * dx + dz * dz < 3600f) { nearSwitch = true; break; } }

                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (!nearSwitch)
                        {
                            // shoulder and slope of the ballast: the main 3D stones, tilted every way, some sunk in, some high,
                            // and about 4 in 10 carry a smaller stone resting on top
                            int nShoulder = 2 + (rng.Randf() < .60f ? 1 : 0);
                            for (int k = 0; k < nShoulder; k++)
                            {
                                float lat = rng.RandfRange(1.42f, 2.35f);
                                float size = .07f + .17f * Mathf.Pow(rng.Randf(), 1.5f);
                                Vector3 pos = c + dir * rng.RandfRange(-.25f, .25f) + r * (side * lat);
                                pos.Y = c.Y + RTBallastSurface(lat) + size * rng.RandfRange(-.10f, .28f);
                                put(pos, size * rng.RandfRange(1.0f, 1.7f), size * rng.RandfRange(.55f, 1.0f), size * rng.RandfRange(.9f, 1.5f), .6f, rng.RandfRange(0f, Mathf.Tau), RTRockColor(rng.Randf(), rng.Randf()));
                                if (rng.Randf() < .40f)
                                {
                                    float sTop = size * rng.RandfRange(.45f, .75f);
                                    Vector3 pTop = pos + dir * (size * rng.RandfRange(-.6f, .6f)) + r * (side * size * rng.RandfRange(-.6f, .6f));
                                    pTop.Y = pos.Y + size * rng.RandfRange(.28f, .50f);
                                    put(pTop, sTop * rng.RandfRange(1.0f, 1.6f), sTop * rng.RandfRange(.55f, 1.0f), sTop * rng.RandfRange(.9f, 1.4f), .7f, rng.RandfRange(0f, Mathf.Tau), RTRockColor(rng.Randf(), rng.Randf()));
                                }
                            }
                        }

                        // ground beside the ballast: pebbles and small stones, many near the ballast and fewer further out, sometimes in little groups
                        int nGround = rng.Randf() < .65f ? 2 : 1;
                        for (int k = 0; k < nGround; k++)
                        {
                            float lat = 2.20f + Mathf.Pow(rng.Randf(), 1.8f) * 3.8f;
                            Vector3 pos = c + dir * rng.RandfRange(-.25f, .25f) + r * (side * lat);
                            if (!RTFree(pos, 2.2f)) continue;
                            float size = .03f + .12f * Mathf.Pow(rng.Randf(), 1.6f);
                            pos.Y = c.Y + size * rng.RandfRange(-.05f, .22f);
                            put(pos, size * rng.RandfRange(.9f, 1.7f), size * rng.RandfRange(.5f, .95f), size * rng.RandfRange(.9f, 1.5f), .55f, rng.RandfRange(0f, Mathf.Tau), RTRockColor(rng.Randf(), rng.Randf()));
                            if (rng.Randf() < .25f)
                            {
                                for (int g2 = 0; g2 < 2; g2++)
                                {
                                    float sg = size * rng.RandfRange(.40f, .80f);
                                    Vector3 pg = pos + dir * (size * rng.RandfRange(-1.3f, 1.3f)) + r * (size * rng.RandfRange(-1.3f, 1.3f));
                                    pg.Y = c.Y + sg * rng.RandfRange(-.02f, .20f);
                                    put(pg, sg * rng.RandfRange(.9f, 1.6f), sg * rng.RandfRange(.5f, .9f), sg * rng.RandfRange(.9f, 1.4f), .55f, rng.RandfRange(0f, Mathf.Tau), RTRockColor(rng.Randf(), rng.Randf()));
                                }
                            }
                        }

                        // now and then a bigger rock with a few stones around it
                        if (rng.Randf() < .035f)
                        {
                            float lat = rng.RandfRange(2.5f, 5.5f);
                            Vector3 pos = c + dir * rng.RandfRange(-.25f, .25f) + r * (side * lat);
                            if (RTFree(pos, 2.2f))
                            {
                                float size = .22f + .16f * rng.Randf();
                                pos.Y = c.Y + size * rng.RandfRange(.05f, .22f);
                                put(pos, size * rng.RandfRange(1.0f, 1.5f), size * rng.RandfRange(.6f, .9f), size * rng.RandfRange(.9f, 1.3f), .35f, rng.RandfRange(0f, Mathf.Tau), RTRockColor(rng.Randf(), rng.Randf()));
                                for (int b2 = 0; b2 < 3; b2++)
                                {
                                    float sb = size * rng.RandfRange(.25f, .50f);
                                    Vector3 pb = pos + dir * (size * rng.RandfRange(-1.3f, 1.3f)) + r * (size * rng.RandfRange(-1.3f, 1.3f));
                                    pb.Y = c.Y + sb * rng.RandfRange(0f, .25f);
                                    put(pb, sb * rng.RandfRange(1.0f, 1.6f), sb * rng.RandfRange(.55f, 1.0f), sb * rng.RandfRange(.9f, 1.4f), .55f, rng.RandfRange(0f, Mathf.Tau), RTRockColor(rng.Randf(), rng.Randf()));
                                }
                            }
                        }

                        // flat dark mud patches on the ground (3 overlapping clumps)
                        if (rng.Randf() < .16f)
                        {
                            float lat = 2.3f + rng.Randf() * 2.6f;
                            Vector3 pos0 = c + dir * rng.RandfRange(-.3f, .3f) + r * (side * lat);
                            if (RTFree(pos0, 2.2f))
                            {
                                float mud = rng.Randf();
                                for (int j = 0; j < 3; j++)
                                {
                                    float size = .10f + .26f * rng.Randf();
                                    Vector3 pos = pos0 + dir * rng.RandfRange(-.3f, .3f) + r * rng.RandfRange(-.3f, .3f);
                                    pos.Y = c.Y + size * .04f;
                                    float wet = mud * rng.RandfRange(.7f, 1.0f);
                                    put(pos, size * rng.RandfRange(1.0f, 1.8f), size * .22f, size * rng.RandfRange(.8f, 1.5f), .12f, rng.RandfRange(0f, Mathf.Tau), new Color(.30f + .10f * wet, .22f + .07f * wet, .15f + .05f * wet));
                                }
                            }
                        }
                    }
                }

                // ---- stones lying between the sleepers (not near points, where the switch rails run) ----
                if (!e.Turnout)
                {
                    int cnt = Mathf.Max(1, Mathf.RoundToInt(e.Length / RTSleeperSpacing)); float slStep = e.Length / cnt;
                    for (int k = 0; k < cnt - 1; k++)
                    {
                        Vector3 cGap = e.PosAt((k + 1) * slStep);
                        bool nearSwitch = false;
                        foreach (var q in swPos) { float dx = q.X - cGap.X, dz = q.Z - cGap.Z; if (dx * dx + dz * dz < 3600f) { nearSwitch = true; break; } }
                        if (nearSwitch) continue;
                        int nCrib = Mathf.Max(1, Mathf.RoundToInt(3.2f * RTRockDensity * (.6f + .8f * rng.Randf())));
                        for (int j = 0; j < nCrib; j++)
                        {
                            float sc0 = (k + 1) * slStep + rng.RandfRange(-.07f, .07f);
                            Vector3 c = e.PosAt(sc0), r = Right(e.DirAt(sc0));
                            float zone = rng.Randf(), lat;
                            if (zone < .30f) lat = rng.RandfRange(-.55f, .55f);
                            else if (zone < .65f) lat = rng.RandfRange(1.00f, 1.30f);
                            else lat = -rng.RandfRange(1.00f, 1.30f);
                            float size = .04f + .04f * rng.Randf();
                            Vector3 pos = c + r * lat; pos.Y = c.Y + .34f + size * rng.RandfRange(-.05f, .22f);
                            put(pos, size * rng.RandfRange(1.0f, 1.5f), size * rng.RandfRange(.6f, .95f), size * rng.RandfRange(.9f, 1.3f), .6f, rng.RandfRange(0f, Mathf.Tau), RTRockColor(rng.Randf(), rng.Randf()));
                        }
                    }
                }

                foreach (var kv in chunks)
                {
                    var ch = kv.Value; int variant = ((kv.Key % RTRockVariants) + RTRockVariants) % RTRockVariants;
                    var mm = MM(meshes[variant], mat, ch.T.Count, RTRockVisRange, true);
                    for (int i = 0; i < ch.T.Count; i++) { mm.SetInstanceTransform(i, ch.T[i]); mm.SetInstanceColor(i, ch.C[i]); }
                }
            }
        }
    }
}
