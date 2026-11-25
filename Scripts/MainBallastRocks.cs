using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // v44: real 3D ballast rocks. The ballast bed is still a textured mesh, but on top of it (and beside it on the ground)
    // there are now hundreds of thousands of individual sharp-edged granite rocks, pebbles and flat mud clumps
    // (instanced, so they are cheap). Each rock is a faceted low-poly stone with random size, tilt and colour,
    // so the ballast reads as stones lying one on top of another instead of a flat painted surface.
    // Rocks are only drawn near the camera (RTRockVisRange metres); far away the textured bed is used.
    public partial class Main
    {
        // ---- tuning knobs ----
        const float RTRockDensity = 1f;        // 1 = normal, .5 = half as many rocks (faster), 2 = twice as many
        const float RTRockVisRange = 150f;     // metres from the camera (scaled by the graphics level)
        const float RTRockStep = .5f;          // metres along the track between rock groups at density 1

        // Faceted rock: a jagged icosahedron with flat shading, so every face catches the sun differently.
        static ArrayMesh RTMakeRockMesh(int seed)
        {
            return BuildMesh(st =>
            {
                float t = (1f + Mathf.Sqrt(5f)) * .5f;
                Vector3[] v =
                {
                    new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                    new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                    new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
                };
                for (int i = 0; i < 12; i++)
                {
                    Vector3 d = v[i].Normalized(); float r = .60f + .55f * RTHash(i, seed, 5100);      // jagged: every corner at a different distance
                    v[i] = new Vector3(d.X * r * 1.15f, d.Y * r * .85f, d.Z * r);
                }
                int[] f = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
                for (int i = 0; i < f.Length; i += 3)
                {
                    Vector3 a = v[f[i]], b = v[f[i + 1]], c = v[f[i + 2]];
                    Vector3 n = (b - a).Cross(c - a).Normalized();
                    if (n.Dot(a + b + c) < 0f) n = -n;
                    float s = .76f + .36f * RTHash(i, seed, 5101);                                    // each flat face a slightly different grey
                    var col = new Color(s, s * .99f, s * 1.01f);
                    st.SetColor(col); st.SetNormal(n); st.AddVertex(a);
                    st.SetColor(col); st.SetNormal(n); st.AddVertex(b);
                    st.SetColor(col); st.SetNormal(n); st.AddVertex(c);
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
            if (t < .26f) { float v = .50f + .10f * j; return new Color(v, v, v * 1.02f); }
            if (t < .50f) { float v = .66f + .12f * j; return new Color(v, v * .99f, v * .98f); }
            if (t < .64f) { float v = .34f + .10f * j; return new Color(v, v * 1.01f, v * 1.05f); }
            if (t < .78f) return new Color(.62f + .08f * j, .48f + .06f * j, .45f + .05f * j);
            if (t < .90f) return new Color(.52f + .08f * j, .46f + .07f * j, .40f + .05f * j);
            float q = .80f + .10f * j; return new Color(q, q * .99f, q * .96f);
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
            var meshes = new ArrayMesh[4];
            for (int i = 0; i < 4; i++) meshes[i] = RTMakeRockMesh(i * 7 + 3);
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

            var rng = new RandomNumberGenerator { Seed = 4444 };
            float step = RTRockStep / Mathf.Max(.1f, RTRockDensity);
            foreach (var e in sc.Graph.Edges.Values)
            {
                var chunks = new Dictionary<int, RTChunk>();
                System.Action<Vector3, float, float, float, float, float, Color> put = (pos, sx, sy, sz, tilt, yaw, col) =>
                {
                    int variant = (int)(rng.Randi() % 4u);
                    int key = Mathf.FloorToInt((pos.X + pos.Z) * .0083f) * 4 + variant;       // coarse spatial chunk (about 120 m), so the distance culling works per chunk
                    RTChunk ch; if (!chunks.TryGetValue(key, out ch)) { ch = new RTChunk(); chunks[key] = ch; }
                    var bs = new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, rng.RandfRange(-tilt, tilt)) * new Basis(Vector3.Forward, rng.RandfRange(-tilt, tilt)) * Basis.FromScale(new Vector3(sx, sy, sz));
                    ch.T.Add(new Transform3D(bs, pos)); ch.C.Add(col);
                };

                // ---- rocks along the track: shoulder and slope of the ballast, ground pebbles, mud clumps ----
                for (float s = 0f; s < e.Length; s += step)
                {
                    float ss = Mathf.Clamp(s + rng.RandfRange(-.2f, .2f), 0f, e.Length);
                    Vector3 c = e.PosAt(ss), r = Right(e.DirAt(ss));
                    bool nearSwitch = false;
                    foreach (var q in swPos) { float dx = q.X - c.X, dz = q.Z - c.Z; if (dx * dx + dz * dz < 3600f) { nearSwitch = true; break; } }

                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (!nearSwitch)
                        {
                            // shoulder and slope of the ballast: the main 3D stones, large, tilted every way
                            for (int k = 0; k < 2; k++)
                            {
                                if (k == 1 && rng.Randf() > .75f) continue;
                                float lat = rng.RandfRange(1.40f, 2.30f);
                                float size = .10f + .15f * Mathf.Pow(rng.Randf(), 1.4f);
                                Vector3 pos = c + r * (side * lat); pos.Y = c.Y + RTBallastSurface(lat) + size * .10f;
                                float tt = rng.Randf();
                                put(pos, size * rng.RandfRange(1.0f, 1.6f), size * rng.RandfRange(.55f, .95f), size * rng.RandfRange(.9f, 1.5f), .55f, rng.RandfRange(0f, Mathf.Tau), RTRockColor(tt, rng.Randf()));
                            }
                        }
                        // ground beside the ballast: pebbles, small stones
                        if (rng.Randf() < .55f)
                        {
                            float lat = 2.25f + Mathf.Pow(rng.Randf(), 1.6f) * 2.8f;
                            Vector3 pos = c + r * (side * lat);
                            if (RTFree(pos, 2.2f))
                            {
                                float size = .035f + .11f * Mathf.Pow(rng.Randf(), 1.5f);
                                pos.Y = c.Y + size * .12f;
                                put(pos, size * rng.RandfRange(.9f, 1.7f), size * rng.RandfRange(.5f, .9f), size * rng.RandfRange(.9f, 1.5f), .5f, rng.RandfRange(0f, Mathf.Tau), RTRockColor(rng.Randf(), rng.Randf()));
                            }
                        }
                        // flat dark mud clumps on the ground
                        if (rng.Randf() < .14f)
                        {
                            float lat = 2.3f + rng.Randf() * 2.4f;
                            Vector3 pos = c + r * (side * lat);
                            if (RTFree(pos, 2.2f))
                            {
                                float size = .14f + .30f * rng.Randf();
                                pos.Y = c.Y + size * .04f;
                                float m = rng.Randf();
                                put(pos, size * rng.RandfRange(1.0f, 1.8f), size * .22f, size * rng.RandfRange(.8f, 1.5f), .12f, rng.RandfRange(0f, Mathf.Tau), new Color(.34f + .08f * m, .25f + .06f * m, .17f + .04f * m));
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
                        if (rng.Randf() > .5f * RTRockDensity) continue;
                        float sc0 = (k + 1) * slStep + rng.RandfRange(-.05f, .05f);
                        Vector3 c = e.PosAt(sc0), r = Right(e.DirAt(sc0));
                        bool nearSwitch = false;
                        foreach (var q in swPos) { float dx = q.X - c.X, dz = q.Z - c.Z; if (dx * dx + dz * dz < 3600f) { nearSwitch = true; break; } }
                        if (nearSwitch) continue;
                        float zone = rng.Randf(), lat;
                        if (zone < .34f) lat = rng.RandfRange(-.45f, .45f);
                        else if (zone < .67f) lat = rng.RandfRange(1.02f, 1.30f);
                        else lat = -rng.RandfRange(1.02f, 1.30f);
                        float size = .045f + .04f * rng.Randf();
                        Vector3 pos = c + r * lat; pos.Y = c.Y + .34f + size * .10f;
                        put(pos, size * rng.RandfRange(1.0f, 1.4f), size * rng.RandfRange(.6f, .9f), size * rng.RandfRange(.9f, 1.2f), .6f, rng.RandfRange(0f, Mathf.Tau), RTRockColor(rng.Randf(), rng.Randf()));
                    }
                }

                foreach (var kv in chunks)
                {
                    var ch = kv.Value; int variant = ((kv.Key % 4) + 4) % 4;
                    var mm = MM(meshes[variant], mat, ch.T.Count, RTRockVisRange, true);
                    for (int i = 0; i < ch.T.Count; i++) { mm.SetInstanceTransform(i, ch.T[i]); mm.SetInstanceColor(i, ch.C[i]); }
                }
            }
        }
    }
}
