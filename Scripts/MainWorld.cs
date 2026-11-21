using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // World building: ground, continuous rails and ballast, sleepers, overhead line, turnouts, scenery, stations, signals.
    public partial class Main
    {
        StandardMaterial3D matGravel, matConcrete, matSteel, matWire, matMast;
        ArrayMesh wireMesh, mastMesh; BoxMesh sleeperMesh;
        const float MastSpacing = 54f;
        readonly List<GeometryInstance3D> farNodes = new List<GeometryInstance3D>();
        readonly List<float> farBase = new List<float>();
        DirectionalLight3D sun;
        int quality = 1;                    // 0 fast, 1 balanced, 2 high, 3 ultra

        // ---------------------------------------------------------------- helpers
        static StandardMaterial3D Solid(Color c, float metal = 0f, float rough = .9f)
            => new StandardMaterial3D { AlbedoColor = c, Metallic = metal, Roughness = rough, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        static StandardMaterial3D TexMat(Texture2D t)
            => new StandardMaterial3D { AlbedoTexture = t, Roughness = .95f, CullMode = BaseMaterial3D.CullModeEnum.Disabled, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic };

        static void V(SurfaceTool st, Vector3 p, Vector3 n, Vector2 uv) { st.SetNormal(n); st.SetUV(uv); st.AddVertex(p); }
        static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            V(st, a, n, ua); V(st, b, n, ub); V(st, c, n, uc); V(st, a, n, ua); V(st, c, n, uc); V(st, d, n, ud);
        }
        static void AddBox(SurfaceTool st, Vector3 c, Vector3 s)
        {
            Vector3 h = s * .5f; float x0 = c.X - h.X, x1 = c.X + h.X, y0 = c.Y - h.Y, y1 = c.Y + h.Y, z0 = c.Z - h.Z, z1 = c.Z + h.Z;
            Vector2 a = new Vector2(0, 0), b = new Vector2(1, 0), cc = new Vector2(1, 1), d = new Vector2(0, 1);
            Quad(st, new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.Up, a, b, cc, d);
            Quad(st, new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), Vector3.Down, a, b, cc, d);
            Quad(st, new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), Vector3.Right, a, b, cc, d);
            Quad(st, new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x0, y0, z1), Vector3.Left, a, b, cc, d);
            Quad(st, new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.Back, a, b, cc, d);
            Quad(st, new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), Vector3.Forward, a, b, cc, d);
        }
        static ArrayMesh BuildMesh(System.Action<SurfaceTool> f)
        {
            var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles); f(st); return st.Commit();
        }

        void Far(GeometryInstance3D n, float range)
        {
            n.VisibilityRangeEnd = range * visScale; n.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            farNodes.Add(n); farBase.Add(range);
        }
        float visScale = .8f;

        void MakeTrackMeshes()
        {
            const float hl = 3f;
            wireMesh = BuildMesh(st =>
            {
                AddBox(st, new Vector3(0f, 6.25f, 0f), new Vector3(.03f, .03f, 2f * hl));    // contact wire
                AddBox(st, new Vector3(0f, 7.05f, 0f), new Vector3(.025f, .025f, 2f * hl));  // catenary (messenger) wire
                AddBox(st, new Vector3(0f, 6.65f, 0f), new Vector3(.015f, .8f, .015f));      // dropper
            });
            mastMesh = BuildMesh(st =>
            {
                AddBox(st, new Vector3(-3.7f, 4.15f, 0f), new Vector3(.3f, 8.3f, .3f));      // mast
                AddBox(st, new Vector3(-3.7f, .2f, 0f), new Vector3(.7f, .4f, .7f));         // foundation
                AddBox(st, new Vector3(-1.85f, 7.5f, 0f), new Vector3(3.7f, .12f, .12f));    // cantilever boom
                AddBox(st, new Vector3(-1.85f, 6.4f, 0f), new Vector3(3.7f, .07f, .07f));    // registration arm
                AddBox(st, new Vector3(0f, 7.28f, 0f), new Vector3(.1f, .46f, .1f));         // insulator
            });
            sleeperMesh = new BoxMesh { Size = Vector3.One };
        }

        // ---------------------------------------------------------------- ground
        const string GroundShader = @"
shader_type spatial;
render_mode diffuse_lambert, specular_disabled;
uniform sampler2D grass_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D dirt_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D macro_tex : filter_linear_mipmap, repeat_enable;
varying vec3 wp;
void vertex() { wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
    vec2 uv = wp.xz;
    vec3 m1 = texture(macro_tex, uv * 0.0012).rgb;
    vec3 m2 = texture(macro_tex, uv * 0.011).rgb;
    float mud = smoothstep(0.52, 0.68, m1.r * 0.6 + m2.g * 0.4);
    vec3 g1 = texture(grass_tex, uv * 0.2).rgb;
    vec3 g2 = texture(grass_tex, uv * 0.021).rgb;
    vec3 grass = g1 * (0.55 + 0.9 * g2) * mix(vec3(0.85, 0.95, 0.72), vec3(1.1, 1.05, 0.82), m2.b);
    vec3 dirt = texture(dirt_tex, uv * 0.22).rgb * (0.6 + 0.8 * m2.r);
    ALBEDO = mix(grass, dirt, mud);
    ROUGHNESS = 0.96;
}";

        // ------------------------------------------------------------ continuous rails
        static float Curv(TEdge e, float s)
        {
            Vector3 a = e.DirAt(s), b = e.DirAt(Mathf.Min(s + 3f, e.Length)); return a.AngleTo(b) / 3f;
        }

        static void Strip(SurfaceTool st, Vector3 a0, Vector3 b0, Vector3 a1, Vector3 b1, Vector3 n, float ua, float ub, float v0, float v1)
        {
            Quad(st, a0, b0, b1, a1, n, new Vector2(ua, v0), new Vector2(ub, v0), new Vector2(ub, v1), new Vector2(ua, v1));
        }

        // Ballast bed and rails are extruded along the smooth centre line of each edge, so curves are continuous.
        void BuildRibbons(TEdge e)
        {
            var ss = new List<float> { 0f }; float s = 0f;
            while (s < e.Length - .01f)
            {
                float k = Curv(e, s); s = Mathf.Min(e.Length, s + Mathf.Clamp(.126f / Mathf.Sqrt(k + 1e-6f), 3f, 25f)); ss.Add(s);
            }
            int i0 = 0;
            while (i0 < ss.Count - 1)
            {
                int i1 = i0 + 1; while (i1 < ss.Count - 1 && ss[i1] - ss[i0] < 300f) i1++;
                var bed = new SurfaceTool(); bed.Begin(Mesh.PrimitiveType.Triangles);
                var rail = new SurfaceTool(); rail.Begin(Mesh.PrimitiveType.Triangles);
                for (int i = i0; i < i1; i++)
                {
                    float sa = ss[i], sb = ss[i + 1];
                    Vector3 p0 = e.PosAt(sa), p1 = e.PosAt(sb), r0 = Right(e.DirAt(sa)), r1 = Right(e.DirAt(sb)), up = Vector3.Up;
                    const float tw = 1.72f, bw = 2.15f, h = .32f;
                    Vector3 nl = (-r0 * h + up * (bw - tw)).Normalized(), nr = (r0 * h + up * (bw - tw)).Normalized();
                    Strip(bed, p0 - r0 * bw, p0 - r0 * tw + up * h, p1 - r1 * bw, p1 - r1 * tw + up * h, nl, -bw * .5f, -tw * .5f, sa * .5f, sb * .5f);
                    Strip(bed, p0 - r0 * tw + up * h, p0 + r0 * tw + up * h, p1 - r1 * tw + up * h, p1 + r1 * tw + up * h, up, -tw * .5f, tw * .5f, sa * .5f, sb * .5f);
                    Strip(bed, p0 + r0 * tw + up * h, p0 + r0 * bw, p1 + r1 * tw + up * h, p1 + r1 * bw, nr, tw * .5f, bw * .5f, sa * .5f, sb * .5f);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (e.Turnout && side > 0 && (sb <= e.SkipStart || sa >= e.Length - e.SkipEnd)) continue;   // inner rail is replaced by the frog
                        for (int k = 0; k < 3; k++)
                        {
                            float yc = k == 0 ? .445f : (k == 1 ? .52f : .605f), w = k == 0 ? .15f : (k == 1 ? .03f : .07f), hh = k == 0 ? .03f : (k == 1 ? .12f : .05f);
                            float xc = side * .873f, xl = xc - w * .5f, xr = xc + w * .5f, yt = yc + hh * .5f, yb = yc - hh * .5f;
                            Strip(rail, p0 + r0 * xl + up * yt, p0 + r0 * xr + up * yt, p1 + r1 * xl + up * yt, p1 + r1 * xr + up * yt, up, 0f, 1f, 0f, 1f);
                            Strip(rail, p0 + r0 * xl + up * yt, p0 + r0 * xl + up * yb, p1 + r1 * xl + up * yt, p1 + r1 * xl + up * yb, -r0, 0f, 1f, 0f, 1f);
                            Strip(rail, p0 + r0 * xr + up * yb, p0 + r0 * xr + up * yt, p1 + r1 * xr + up * yb, p1 + r1 * xr + up * yt, r0, 0f, 1f, 0f, 1f);
                        }
                    }
                }
                var mb = new MeshInstance3D { Mesh = bed.Commit(), MaterialOverride = matGravel }; AddChild(mb); Far(mb, 2600f);
                var mr = new MeshInstance3D { Mesh = rail.Commit(), MaterialOverride = matSteel }; AddChild(mr); Far(mr, 2600f);
                i0 = i1;
            }
        }

        void AddFrog(TEdge e, float s)
        {
            Vector3 d = e.DirAt(s); Vector3 p = e.PosAt(s) + Right(d) * .873f + Vector3.Up * .56f;
            Box(this, p, Basis.LookingAt(d, Vector3.Up), new Vector3(.3f, .2f, 3.2f), matSteel);
            Box(this, p + Right(d) * -.14f - d * 1.6f, Basis.LookingAt(d, Vector3.Up), new Vector3(.12f, .14f, 1.4f), matSteel);
        }

        void BuildTurnouts()
        {
            var green = Mat(new Color(.15f, .35f, .22f)); var dark = Mat(new Color(.12f, .12f, .14f));
            foreach (var n in sc.Graph.Switches)
            {
                var main = n.Outs[0]; var lp = n.Outs[1]; Vector3 d = main.DirAt(0f), r = Right(d);
                Box(this, n.Pos + r * 3.0f + Vector3.Up * .45f, Basis.LookingAt(d, Vector3.Up), new Vector3(.5f, .9f, .7f), green);       // point machine
                Box(this, n.Pos + r * 2.2f + Vector3.Up * .3f, Basis.LookingAt(d, Vector3.Up), new Vector3(1.8f, .08f, .15f), dark);      // drive rod
                Vector3 ra = Right(lp.DirAt(0f)), rb = Right(lp.DirAt(7f));
                Strut(this, lp.PosAt(0f) + ra * .873f + Vector3.Up * .56f, lp.PosAt(7f) + rb * .873f + Vector3.Up * .56f, .07f, matSteel);   // switch blade
                Strut(this, main.PosAt(0f) - r * .873f + Vector3.Up * .56f, main.PosAt(7f) - Right(main.DirAt(7f)) * .873f + Vector3.Up * .56f, .07f, matSteel);
                Box(this, lp.PosAt(2.5f) + Vector3.Up * .42f, Basis.LookingAt(lp.DirAt(2.5f), Vector3.Up), new Vector3(2.0f, .05f, .12f), dark);   // stretcher bar
                if (lp.Turnout && lp.SkipStart > 0f) AddFrog(lp, lp.SkipStart);
            }
            foreach (var e in sc.Graph.Edges.Values) if (e.Turnout && e.SkipEnd > 0f) AddFrog(e, e.Length - e.SkipEnd);
        }

        void BuildTrack()
        {
            MakeTrackMeshes();
            var min = new Vector3(1e9f, 0, 1e9f); var max = new Vector3(-1e9f, 0, -1e9f);
            const int Chunk = 60;
            foreach (var e in sc.Graph.Edges.Values)
            {
                BuildRibbons(e);
                int np = Mathf.Max(1, Mathf.CeilToInt(e.Length / 6f)); float step = e.Length / np;
                int sl = Mathf.Max(1, Mathf.RoundToInt(step / .6f));
                for (int c0 = 0; c0 < np; c0 += Chunk)
                {
                    int cn = Mathf.Min(Chunk, np - c0);
                    var slp = MM(sleeperMesh, matConcrete, cn * sl, 1700f); var wires = MM(wireMesh, matWire, cn, 2000f);
                    for (int k = 0; k < cn; k++)
                    {
                        float s = (c0 + k + .5f) * step; var p = e.PosAt(s); var q = Basis.LookingAt(e.DirAt(s), Vector3.Up);
                        min = min.Min(p); max = max.Max(p); if ((c0 + k) % 6 == 0) trackPts.Add(p);
                        wires.SetInstanceTransform(k, Xf(q, p, new Vector3(1f, 1f, step / 6f)));
                        for (int i = 0; i < sl; i++)
                        {
                            float zl = -step * .5f + (i + .5f) * step / sl;
                            slp.SetInstanceTransform(k * sl + i, Xf(q, p + q * new Vector3(0f, .33f, zl), new Vector3(2.75f, .2f, .26f)));
                        }
                    }
                    float s0 = c0 * step, s1 = (c0 + cn) * step; var mastS = new List<float>();
                    for (int m = Mathf.CeilToInt(s0 / MastSpacing - .5f); (m + .5f) * MastSpacing < s1; m++) { float sm = (m + .5f) * MastSpacing; if (sm >= s0) mastS.Add(sm); }
                    if (mastS.Count > 0)
                    {
                        var masts = MM(mastMesh, matMast, mastS.Count, 2000f);
                        for (int i = 0; i < mastS.Count; i++)
                            masts.SetInstanceTransform(i, Xf(Basis.LookingAt(e.DirAt(mastS[i]), Vector3.Up), e.PosAt(mastS[i]), Vector3.One));
                    }
                }
            }
            BuildTurnouts();
            var c = (min + max) * .5f;
            var gm = new ShaderMaterial { Shader = new Shader { Code = GroundShader } };
            gm.SetShaderParameter("grass_tex", ProcTex.Grass()); gm.SetShaderParameter("dirt_tex", ProcTex.Dirt()); gm.SetShaderParameter("macro_tex", ProcTex.Macro());
            AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(max.X - min.X + 8000f, max.Z - min.Z + 8000f) }, MaterialOverride = gm, Position = new Vector3(c.X, -.02f, c.Z), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }

        // ---------------------------------------------------------------- scenery (grouped in cells so far cells are culled)
        struct Prop { public Vector3 p; public float s, rot; public Color c; }

        void BuildScenery()
        {
            var rng = new RandomNumberGenerator { Seed = 7 };
            var blocked = new HashSet<long>(); const float G = 24f;
            foreach (var t in trackPts)
                for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
                    blocked.Add(((long)(Mathf.FloorToInt(t.X / G) + dx) << 32) ^ (uint)(Mathf.FloorToInt(t.Z / G) + dz));
            var trees = new Dictionary<long, List<Prop>>(); var bushes = new Dictionary<long, List<Prop>>(); var rocks = new Dictionary<long, List<Prop>>();
            const float Cell = 450f; int nt = 1700, nb = 1300, nr = 450;
            for (int n = 0; n < nt + nb + nr; n++)
            {
                var tp = trackPts[rng.RandiRange(0, trackPts.Count - 1)];
                float x = tp.X + rng.RandfRange(-170f, 170f), z = tp.Z + rng.RandfRange(-170f, 170f);
                if (blocked.Contains(((long)Mathf.FloorToInt(x / G) << 32) ^ (uint)Mathf.FloorToInt(z / G))) continue;
                var pr = new Prop { p = new Vector3(x, 0f, z), s = rng.RandfRange(.8f, 1.6f), rot = rng.RandfRange(0f, 6.28f), c = new Color(rng.RandfRange(.10f, .24f), rng.RandfRange(.30f, .46f), rng.RandfRange(.10f, .2f)) };
                long key = ((long)Mathf.FloorToInt(x / Cell) << 32) ^ (uint)Mathf.FloorToInt(z / Cell);
                var dict = n < nt ? trees : (n < nt + nb ? bushes : rocks);
                List<Prop> l; if (!dict.TryGetValue(key, out l)) { l = new List<Prop>(); dict[key] = l; }
                l.Add(pr);
            }
            var trunkMesh = new CylinderMesh { TopRadius = .22f, BottomRadius = .34f, Height = 2.4f, RadialSegments = 6, Rings = 1 };
            var crownMesh = new SphereMesh { Radius = 2.1f, Height = 4.6f, RadialSegments = 8, Rings = 4 };
            var bushMesh = new SphereMesh { Radius = .9f, Height = 1.3f, RadialSegments = 7, Rings = 3 };
            var rockMesh = new SphereMesh { Radius = .6f, Height = .8f, RadialSegments = 6, Rings = 3 };
            var trunkMat = Mat(new Color(.33f, .23f, .15f)); var rockMat = Mat(new Color(.42f, .41f, .39f));
            var leafMat = Mat(Colors.White); leafMat.VertexColorUseAsAlbedo = true;
            foreach (var kv in trees)
            {
                var l = kv.Value; var tr = MM(trunkMesh, trunkMat, l.Count, 1500f); var cr = MM(crownMesh, leafMat, l.Count, 1500f, true);
                for (int i = 0; i < l.Count; i++)
                {
                    var rot = new Basis(Vector3.Up, l[i].rot) * Basis.FromScale(Vector3.One * l[i].s);
                    tr.SetInstanceTransform(i, new Transform3D(rot, l[i].p + new Vector3(0f, 1.2f * l[i].s, 0f)));
                    cr.SetInstanceTransform(i, new Transform3D(rot, l[i].p + new Vector3(0f, 4.4f * l[i].s, 0f))); cr.SetInstanceColor(i, l[i].c);
                }
            }
            foreach (var kv in bushes)
            {
                var l = kv.Value; var bu = MM(bushMesh, leafMat, l.Count, 800f, true);
                for (int i = 0; i < l.Count; i++) { bu.SetInstanceTransform(i, new Transform3D(Basis.FromScale(Vector3.One * l[i].s), l[i].p + new Vector3(0f, .45f * l[i].s, 0f))); bu.SetInstanceColor(i, l[i].c.Lightened(.1f)); }
            }
            foreach (var kv in rocks)
            {
                var l = kv.Value; var ro = MM(rockMesh, rockMat, l.Count, 600f);
                for (int i = 0; i < l.Count; i++) ro.SetInstanceTransform(i, new Transform3D(new Basis(Vector3.Up, l[i].rot) * Basis.FromScale(Vector3.One * l[i].s), l[i].p + new Vector3(0f, .15f * l[i].s, 0f)));
            }
        }

        // ------------------------------------------------------------- sky, light and graphics quality
        void BuildSky()
        {
            var sky = new Sky { SkyMaterial = new ProceduralSkyMaterial {
                SkyTopColor = new Color(.20f, .44f, .82f), SkyHorizonColor = new Color(.80f, .85f, .90f),
                GroundHorizonColor = new Color(.68f, .74f, .72f), GroundBottomColor = new Color(.3f, .4f, .3f), SunAngleMax = 40f } };
            var env = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky, Sky = sky,
                AmbientLightSource = Godot.Environment.AmbientSource.Sky, AmbientLightEnergy = 1.0f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic, TonemapExposure = 1.05f,
                FogEnabled = true, FogLightColor = new Color(.76f, .83f, .92f), FogDensity = 0.0006f
            };
            AddChild(new WorldEnvironment { Environment = env });
            sun = new DirectionalLight3D { RotationDegrees = new Vector3(-48, -35, 0), LightColor = new Color(1f, .96f, .9f), LightEnergy = 1.15f, ShadowEnabled = true, DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal };
            AddChild(sun);
        }

        public void ApplyQuality(int q)
        {
            quality = Mathf.Clamp(q, 0, 3);
            float[] scale = { .65f, .8f, 1f, 1f }, shadow = { 0f, 70f, 110f, 170f }, vis = { .5f, .75f, 1f, 1.3f };
            var vp = GetViewport();
            vp.Scaling3DScale = scale[quality];
            vp.Msaa3D = quality >= 3 ? Viewport.Msaa.Msaa2X : Viewport.Msaa.Disabled;
            vp.ScreenSpaceAA = quality == 0 ? Viewport.ScreenSpaceAAEnum.Disabled : Viewport.ScreenSpaceAAEnum.Fxaa;
            if (sun != null) { sun.ShadowEnabled = shadow[quality] > 0f; sun.DirectionalShadowMaxDistance = Mathf.Max(30f, shadow[quality]); }
            visScale = vis[quality];
            for (int i = 0; i < farNodes.Count; i++) farNodes[i].VisibilityRangeEnd = farBase[i] * visScale;
        }

        void FillMM(List<Transform3D> list, Material m)
        {
            if (list.Count == 0) return;
            var mm = MMBox(m, list.Count, 0f);
            for (int i = 0; i < list.Count; i++) mm.SetInstanceTransform(i, list[i]);
        }

        // The edge that leads into e (the main line is preferred over a loop line).
        TEdge PredOf(TEdge e)
        {
            TEdge best = null;
            foreach (var ed in sc.Graph.Edges.Values) if (ed.To == e.From && (best == null || best.Id.Contains("l_"))) best = ed;
            return best;
        }

        // Platforms are 620 m long (the longest train is 597 m) and follow the track, even across edge joins.
        void BuildStations()
        {
            const float PlatLen = 620f, Piece = 40f;
            var yellow = Mat(new Color(.9f, .75f, .1f)); var pole = Mat(new Color(.25f, .25f, .27f)); var roof = Mat(new Color(.55f, .55f, .58f));
            var brick = Mat(new Color(.55f, .3f, .24f)); var rf2 = Mat(new Color(.3f, .3f, .32f));
            var slabs = new List<Transform3D>(); var lines = new List<Transform3D>(); var roofs = new List<Transform3D>(); var poles = new List<Transform3D>();
            foreach (var st in sc.Graph.Stations)
            {
                float sm = st.Side == "L" ? -1f : 1f;
                var e = st.Edge; float hi = Mathf.Min(e.Length, st.Offset + 10f), remain = PlatLen;
                for (int guard = 0; remain > 1f && e != null && guard < 6; guard++)
                {
                    float lo = Mathf.Max(0f, hi - remain);
                    for (float s0 = lo; s0 < hi - 1f; s0 += Piece)
                    {
                        float e1 = Mathf.Min(s0 + Piece, hi), len = e1 - s0, mid = (s0 + e1) * .5f;
                        var pp = e.PosAt(mid); var dd = e.DirAt(mid); var qq = Basis.LookingAt(dd, Vector3.Up); var rr = Right(dd) * sm;
                        slabs.Add(Xf(qq, pp + rr * 3.5f + Vector3.Up * .72f, new Vector3(3.4f, 1.45f, len + .2f)));
                        lines.Add(Xf(qq, pp + rr * 1.9f + Vector3.Up * 1.46f, new Vector3(.35f, .02f, len + .2f)));
                        roofs.Add(Xf(qq, pp + rr * 4.6f + Vector3.Up * 5.4f, new Vector3(3.8f, .25f, len + .2f)));
                        poles.Add(Xf(qq, pp + rr * 5.4f + Vector3.Up * 3.4f, new Vector3(.25f, 3.9f, .25f)));
                    }
                    remain -= hi - lo;
                    if (lo > 0f) break;
                    e = PredOf(e); if (e != null) hi = e.Length;
                }
                float cs = Mathf.Max(0f, st.Offset - 150f);          // station building and name board sit behind the stop mark
                var p = st.Edge.PosAt(cs); var d = st.Edge.DirAt(cs); var q = Basis.LookingAt(d, Vector3.Up); var r = Right(d) * sm;
                Box(this, p + r * 17f + Vector3.Up * 3f, q, new Vector3(9f, 6f, 70f), brick);
                Box(this, p + r * 17f + Vector3.Up * 6.3f, q, new Vector3(10f, .6f, 72f), rf2);
                if (st.Stop || st.Name.StartsWith("Sec")) Tag(st.Name, p + r * 3.5f + Vector3.Up * 8f, Colors.Yellow);
            }
            FillMM(slabs, matConcrete); FillMM(lines, yellow); FillMM(roofs, roof); FillMM(poles, pole);
        }

        // Colour light signals: three lamps (red, yellow, green) on the driver's left side.
        void BuildSignals()
        {
            var pole = Mat(new Color(.2f, .2f, .2f)); var head = Mat(new Color(.05f, .05f, .05f));
            var cols = new[] { Colors.Red, new Color(1f, .8f, .1f), new Color(.2f, 1f, .3f) };
            foreach (var s in sc.Graph.Signals)
            {
                var p = s.Edge.PosAt(s.Offset); var d = s.Edge.DirAt(s.Offset); var q = Basis.LookingAt(d, Vector3.Up); var bp = p - Right(d) * 3.4f;
                Box(this, bp + Vector3.Up * 3f, q, new Vector3(.25f, 6f, .25f), pole);
                Box(this, bp + Vector3.Up * 6.2f, q, new Vector3(.9f, 2.7f, .4f), head);
                var mats = new StandardMaterial3D[3];
                for (int i = 0; i < 3; i++)
                {
                    mats[i] = Mat(cols[i].Darkened(.8f)); mats[i].EmissionEnabled = false;
                    Box(this, bp + Vector3.Up * (7.0f - i * .8f) - d * .22f, q, new Vector3(.6f, .6f, .1f), mats[i]);
                }
                sigMats[s] = mats;
                Tag(s.Id, bp + Vector3.Up * 8.6f, Colors.White);
            }
        }

        // True when point pt is at least minDist metres from every loop line (straight-segment distance on the 5 m track samples).
        bool ClearOfLoopLines(Vector3 pt, float minDist)
        {
            foreach (var ed in sc.Graph.Edges.Values)
            {
                if (!ed.Turnout) continue;
                for (int k = 0; k + 1 < ed.Pts.Count; k++)
                {
                    Vector3 a = ed.Pts[k], b = ed.Pts[k + 1], ab = b - a; ab.Y = 0f; Vector3 ap = pt - a; ap.Y = 0f;
                    float l2 = ab.LengthSquared(), t = l2 > 1e-6f ? Mathf.Clamp(ap.Dot(ab) / l2, 0f, 1f) : 0f;
                    if ((ap - ab * t).Length() < minDist) return false;
                }
            }
            return true;
        }

        // Indian railway route furniture: station name and code boards, kilometre posts and signal relay huts.
        // Geometry is generated from the same track graph, so objects remain attached to curves.
        void BuildIndianRailwayObjects()
        {
            var boardBlue = Mat(new Color(.025f, .12f, .42f));
            var boardWhite = Mat(new Color(.94f, .94f, .88f));
            var postMat = Mat(new Color(.78f, .78f, .72f));
            var hutMat = Mat(new Color(.78f, .72f, .58f));
            var roofMat = Mat(new Color(.25f, .25f, .27f));
            foreach (var st in sc.Graph.Stations)
            {
                var p = st.Edge.PosAt(st.Offset); var d = st.Edge.DirAt(st.Offset);
                var q = Basis.LookingAt(d, Vector3.Up); var side = Right(d) * (st.Side == "L" ? -1f : 1f);
                var center = p + side * 4.8f;   // on the platform, facing the track
                Box(this, center + Vector3.Up * 3.2f, q, new Vector3(.22f, 2.0f, 7.2f), boardBlue);
                Box(this, center + Vector3.Up * 2.0f, q, new Vector3(.35f, 2.4f, .35f), postMat);
                var label = new Label3D { Text = st.Name, FontSize = 48, PixelSize = .018f, Modulate = Colors.White,
                    OutlineSize = 8, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true };
                AddChild(label); label.Position = center + Vector3.Up * 3.0f;
            }
            // Signal relay/equipment huts, positioned outside the running clearance.
            foreach (var sg in sc.Graph.Signals)
            {
                var p = sg.Edge.PosAt(sg.Offset); var d = sg.Edge.DirAt(sg.Offset); var q = Basis.LookingAt(d, Vector3.Up);
                var side = -Right(d) * 15f;
                Box(this, p + side + Vector3.Up * 1.25f, q, new Vector3(2.4f, 2.5f, 2.0f), hutMat);
                Box(this, p + side + Vector3.Up * 2.58f, q, new Vector3(2.55f, .18f, 2.15f), roofMat);
                Box(this, p + side * .916f + Vector3.Up * 1.05f, q, new Vector3(.08f, .9f, .65f), boardWhite);
            }
            // Kilometre markers on the down line. Distances are from the Secunderabad departure datum.
            string[] ids = { "d0","d1","d2","d3","d4","d5","d6","d7","d8","d9","d10","d11","d12","d13","d14","d15","d16","d17" };
            // Start of each down-line piece measured from the real edge lengths (a typed table drifted by up to ~145 m)
            float[] starts = new float[ids.Length]; float acc = 0f;
            for (int ei = 0; ei < ids.Length; ei++) { starts[ei] = acc; if (sc.Graph.Edges.TryGetValue(ids[ei], out var ed)) acc += ed.Length; }
            for (int km = 1; km <= 19; km++)
            {
                float target = km * 1000f + 765f; // player start datum is 765 m into the scaled alignment
                for (int i = 0; i < ids.Length; i++)
                {
                    if (!sc.Graph.Edges.TryGetValue(ids[i], out var e)) continue;
                    if (target < starts[i] || target > starts[i] + e.Length) continue;
                    float off = target - starts[i];
                    // near the points a loop line runs close beside the main line: slide the post along the track until it clears every loop line
                    for (float shift = 0f; shift <= 400f && off + shift < e.Length - 1f; shift += 5f)
                    {
                        var pt = e.PosAt(off + shift) - Right(e.DirAt(off + shift)) * 2.9f;
                        if (ClearOfLoopLines(pt, 2.6f)) { off += shift; break; }
                    }
                    var p = e.PosAt(off); var d = e.DirAt(off); var q = Basis.LookingAt(d, Vector3.Up);
                    var side = -Right(d) * 2.9f;
                    Box(this, p + side + Vector3.Up * .9f, q, new Vector3(.75f, 1.8f, .18f), postMat);
                    Tag("" + km, p + side + Vector3.Up * 1.2f, Colors.Black);
                    break;
                }
            }
        }


        void BuildSwitchTags()
        {
            foreach (var n in sc.Graph.Switches) swTags[n] = Tag("", n.Pos + new Vector3(0f, 6f, 0f), Colors.Yellow);
            RefreshSwitchTags();
        }
        void RefreshSwitchTags()
        {
            foreach (var kv in swTags) kv.Value.Text = kv.Key.Name + ": " + (kv.Key.Sel == 0 ? "MAIN" : "LOOP");
        }

    }
}
