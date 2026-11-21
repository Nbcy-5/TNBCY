using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // World building: ground, continuous rails and ballast, sleepers, overhead line, turnouts, scenery, stations, signals.
    public partial class Main
    {
        StandardMaterial3D matGravel, matConcrete, matSteel, matWire, matMast, matRail, matFit;
        Mesh fitMesh, jointMesh;
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
            // One rail seat (origin: rail centre on top of the sleeper): rubber pad and a spring clip each side of the rail foot
            fitMesh = BuildMesh(st =>
            {
                AddBoxC(st, new Vector3(0f, .006f, 0f), new Vector3(.15f, .012f, .2f), new Color(.05f, .05f, .055f));       // rubber pad
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    AddBoxC(st, new Vector3(sd * .064f, .04f, 0f), new Vector3(.046f, .012f, .022f), new Color(.3f, .21f, .15f)); // clip toe on the rail foot
                    AddBoxC(st, new Vector3(sd * .09f, .052f, 0f), new Vector3(.014f, .05f, .022f), new Color(.3f, .21f, .15f));  // clip leg
                }
            });
            // One fishplated rail joint (origin: rail centre on top of the sleeper): two plates and four bolts, heads alternating sides
            jointMesh = BuildMesh(st =>
            {
                Color plate = new Color(.36f, .27f, .2f), bolt = new Color(.2f, .16f, .13f);
                for (int sd = -1; sd <= 1; sd += 2) AddBoxC(st, new Vector3(sd * .0205f, .095f, 0f), new Vector3(.02f, .07f, .61f), plate);
                for (int b = 0; b < 4; b++)
                {
                    float z = -.255f + b * .17f; int hs = b % 2 == 0 ? 1 : -1;
                    AddBoxC(st, new Vector3(hs * .043f, .095f, z), new Vector3(.028f, .032f, .032f), bolt);    // head
                    AddBoxC(st, new Vector3(-hs * .04f, .095f, z), new Vector3(.022f, .03f, .03f), bolt);      // nut
                }
            });
        }

        static void AddBoxC(SurfaceTool st, Vector3 c, Vector3 sz, Color col) { st.SetColor(col); AddBox(st, c, sz); }

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

        // 60 kg flat-bottom rail section (x across, y up from the underside of the foot), counter-clockwise.
        static readonly Vector2[] RailProfile =
        {
            new Vector2(-.075f, 0f), new Vector2(.075f, 0f), new Vector2(.075f, .012f), new Vector2(.030f, .030f), new Vector2(.0085f, .052f), new Vector2(.0085f, .100f),
            new Vector2(.0295f, .118f), new Vector2(.036f, .128f), new Vector2(.036f, .158f), new Vector2(.030f, .172f),
            new Vector2(-.030f, .172f), new Vector2(-.036f, .158f), new Vector2(-.036f, .128f), new Vector2(-.0295f, .118f), new Vector2(-.0085f, .100f), new Vector2(-.0085f, .052f), new Vector2(-.030f, .030f), new Vector2(-.075f, .012f)
        };

        // Extrude the rail section between two track positions. xc: sideways position of the rail centre (0 for loose pieces); gaugeSide: -1/+1 = which head side
        // faces the track centre (polished by the wheels), 0 = none. Polished top, rusty sides and web, dark foot.
        static void RailExtrude(SurfaceTool st, Vector3 p0, Vector3 p1, Vector3 r0, Vector3 r1, float xc, int gaugeSide)
        {
            const float y0 = .44f; int n = RailProfile.Length;
            for (int j = 0; j < n; j++)
            {
                Vector2 a = RailProfile[j], b = RailProfile[(j + 1) % n];
                Vector2 n2 = new Vector2(b.Y - a.Y, -(b.X - a.X)).Normalized();
                Vector3 nn = (r0 * n2.X + Vector3.Up * n2.Y).Normalized();
                Color c = new Color(.3f, .2f, .15f);                                                       // web and under-head: rust
                if (j == 9) c = new Color(.74f, .74f, .76f);                                               // running surface
                else if (j == 8 || j == 10) c = new Color(.5f, .46f, .43f);                                // rounded head corners
                else if (j == 7 || j == 11) c = (gaugeSide != 0 && ((j == 7) == (gaugeSide > 0))) ? new Color(.58f, .57f, .58f) : new Color(.36f, .26f, .2f);   // gauge face / outer face
                else if (j <= 3 || j >= 16) c = new Color(.2f, .15f, .12f);                                // foot
                st.SetColor(c);
                Strip(st, p0 + r0 * (xc + a.X) + Vector3.Up * (y0 + a.Y), p0 + r0 * (xc + b.X) + Vector3.Up * (y0 + b.Y),
                          p1 + r1 * (xc + a.X) + Vector3.Up * (y0 + a.Y), p1 + r1 * (xc + b.X) + Vector3.Up * (y0 + b.Y), nn, 0f, 1f, 0f, 1f);
            }
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
                    const float tw = 1.72f, bw = 2.15f, h = .40f;
                    Vector3 nl = (-r0 * h + up * (bw - tw)).Normalized(), nr = (r0 * h + up * (bw - tw)).Normalized();
                    Strip(bed, p0 - r0 * bw, p0 - r0 * tw + up * h, p1 - r1 * bw, p1 - r1 * tw + up * h, nl, -bw * .5f, -tw * .5f, sa * .5f, sb * .5f);
                    Strip(bed, p0 - r0 * tw + up * h, p0 + r0 * tw + up * h, p1 - r1 * tw + up * h, p1 + r1 * tw + up * h, up, -tw * .5f, tw * .5f, sa * .5f, sb * .5f);
                    Strip(bed, p0 + r0 * tw + up * h, p0 + r0 * bw, p1 + r1 * tw + up * h, p1 + r1 * bw, nr, tw * .5f, bw * .5f, sa * .5f, sb * .5f);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (e.Turnout && side > 0 && ((e.SkipStart > 0f && sb <= TongueLen) || (e.SkipEnd > 0f && sa >= e.Length - TongueLen))) continue;   // only the switch-rail length is drawn separately; the closure rail runs on to the frog
                        RailExtrude(rail, p0, p1, r0, r1, side * RailG, -side);
                    }
                }
                var mb = new MeshInstance3D { Mesh = bed.Commit(), MaterialOverride = matGravel }; AddChild(mb); Far(mb, 2600f);
                var mr = new MeshInstance3D { Mesh = rail.Commit(), MaterialOverride = matRail }; AddChild(mr); Far(mr, 2600f);
                i0 = i1;
            }
        }

        const float TongueLen = 7.5f, RailG = .873f;

        // A short length of rail (foot, web, head) between two ground points - used for wing, check and flare details.
        void RailPiece(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a; if (d.Length() < .02f) return; Vector3 r = Right(d.Normalized());
            var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
            RailExtrude(st, a, b, r, r, 0f, 0);
            AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = matRail });
        }

        // Check rail 65 mm inside a running rail (lat = signed offset of that running rail), flared at both ends.
        void CheckRail(TEdge e, float s, float lat)
        {
            float sg = lat > 0f ? 1f : -1f, c = lat - sg * .135f, f = lat - sg * .205f;
            Vector3 P(float t, float off) { t = Mathf.Clamp(t, 0f, e.Length); return e.PosAt(t) + Right(e.DirAt(t)) * off; }
            RailPiece(P(s - 2.6f, f), P(s - 1.8f, c));
            RailPiece(P(s - 1.8f, c), P(s + 1.8f, c));
            RailPiece(P(s + 1.8f, c), P(s + 2.6f, f));
        }

        // Frog where the loop's inner rail crosses the main line's inner rail: V nose, heavy body, wing rails and check rails.
        void AddFrog(TEdge lp, float s, TEdge main, float sm)
        {
            Vector3 dl = lp.DirAt(s), dm = main.DirAt(sm);
            float side = Right(dm).Dot(lp.PosAt(s) - main.PosAt(sm)) > 0f ? 1f : -1f;      // +1: loop lies on the right of the main line
            Vector3 x = lp.PosAt(s) - Right(dl) * (side * RailG);                           // crossing point of the two inner rails
            Vector3 b = (dm + dl).Normalized(), n = Right(b); var q = Basis.LookingAt(b, Vector3.Up);
            Box(this, x + b * .5f + Vector3.Up * .54f, q, new Vector3(.11f, .17f, 1.0f), matSteel);     // nose
            Box(this, x + b * 1.75f + Vector3.Up * .54f, q, new Vector3(.27f, .17f, 1.5f), matSteel);   // frog body
            for (int k = -1; k <= 1; k += 2)
            {
                Vector3 w = n * (k * .125f), fl = n * (k * .24f);                                       // wing rails with a flangeway beside the nose
                RailPiece(x + b * -2.2f + fl, x + b * -1.4f + w);
                RailPiece(x + b * -1.4f + w, x + b * 2.2f + w);
                RailPiece(x + b * 2.2f + w, x + b * 3.0f + fl);
            }
            CheckRail(main, sm, -side * RailG);       // opposite the frog on the main line
            CheckRail(lp, s, side * RailG);           // and on the loop line
        }

        TEdge MainOf(TEdge e, bool atStart)
        {
            if (atStart) return e.From.Outs.Count > 1 ? e.From.Outs[0] : null;
            foreach (var m in sc.Graph.Edges.Values) if (!m.Turnout && m.To == e.To && m != e) return m;
            return null;
        }

        // Loop lines in the yard have 13 m rails joined by fishplates; the main lines are welded.
        void BuildJoints(TEdge e)
        {
            var js = new List<float>();
            for (float s = 13f; s < e.Length - 13f; s += 13f)
            {
                if ((e.SkipStart > 0f && s < 60f) || (e.SkipEnd > 0f && s > e.Length - 60f)) continue;   // no joints through the points
                js.Add(s);
            }
            for (int c0 = 0; c0 < js.Count; c0 += 24)
            {
                int cn = Mathf.Min(24, js.Count - c0); var mm = MM(jointMesh, matFit, cn * 2, 170f);
                for (int k = 0; k < cn; k++)
                {
                    float s = js[c0 + k]; var q = Basis.LookingAt(e.DirAt(s), Vector3.Up); Vector3 p = e.PosAt(s) + Vector3.Up * .43f;
                    mm.SetInstanceTransform(k * 2, Xf(q, p - q.X * RailG, Vector3.One)); mm.SetInstanceTransform(k * 2 + 1, Xf(q, p + q.X * RailG, Vector3.One));
                }
            }
        }

        void BuildTurnouts()
        {
            var green = Mat(new Color(.15f, .35f, .22f)); var dark = Mat(new Color(.12f, .12f, .14f));
            foreach (var n in sc.Graph.Switches)
            {
                var main = n.Outs[0]; var lp = n.Outs[1]; Vector3 d = main.DirAt(0f), r = Right(d);
                Box(this, n.Pos + r * 3.0f + Vector3.Up * .45f, Basis.LookingAt(d, Vector3.Up), new Vector3(.5f, .9f, .7f), green);       // point machine
                Box(this, n.Pos + r * 2.2f + Vector3.Up * .3f, Basis.LookingAt(d, Vector3.Up), new Vector3(1.8f, .08f, .15f), dark);      // drive rod
                Vector3 ra = Right(lp.DirAt(0f));
                RailPiece(lp.PosAt(0f) + ra * RailG, lp.PosAt(TongueLen) + Right(lp.DirAt(TongueLen)) * RailG);   // switch rail of the loop line
                Box(this, lp.PosAt(2.5f) + Vector3.Up * .42f, Basis.LookingAt(lp.DirAt(2.5f), Vector3.Up), new Vector3(2.0f, .05f, .12f), dark);   // stretcher bar
                if (lp.Turnout && lp.SkipStart > 0f) AddFrog(lp, lp.SkipStart, main, Mathf.Min(lp.SkipStart, main.Length));
            }
            foreach (var e in sc.Graph.Edges.Values) if (e.Turnout && e.SkipEnd > 0f) { var m = MainOf(e, false); if (m != null) AddFrog(e, e.Length - e.SkipEnd, m, Mathf.Max(0f, m.Length - e.SkipEnd)); }
        }

        void BuildTrack()
        {
            MakeTrackMeshes();
            var min = new Vector3(1e9f, 0, 1e9f); var max = new Vector3(-1e9f, 0, -1e9f);
            const int Chunk = 60;
            var fan = new List<Transform3D>();
            foreach (var e in sc.Graph.Edges.Values)
            {
                BuildRibbons(e);
                // points area: where a loop line is closer than 3.2 m to its main line the sleepers are long and carry both tracks
                TEdge mS = null, mE = null; float zS = 0f, zE = 0f;
                if (e.Turnout)
                {
                    if (e.SkipStart > 0f) { mS = MainOf(e, true); if (mS != null) for (float t = 0f; t < e.Length; t += 1f) if (e.PosAt(t).DistanceTo(mS.PosAt(Mathf.Min(t, mS.Length))) >= 3.2f) { zS = t; break; } }
                    if (e.SkipEnd > 0f) { mE = MainOf(e, false); if (mE != null) for (float t = 0f; t < e.Length; t += 1f) if (e.PosAt(e.Length - t).DistanceTo(mE.PosAt(Mathf.Max(0f, mE.Length - t))) >= 3.2f) { zE = t; break; } }
                }
                int np = Mathf.Max(1, Mathf.CeilToInt(e.Length / 6f)); float step = e.Length / np;
                int sl = Mathf.Max(1, Mathf.RoundToInt(step / .6f));
                for (int c0 = 0; c0 < np; c0 += Chunk)
                {
                    int cn = Mathf.Min(Chunk, np - c0);
                    var slp = MM(sleeperMesh, matConcrete, cn * sl, 1700f, true); var wires = MM(wireMesh, matWire, cn, 2000f); var fit = MM(fitMesh, matFit, cn * sl * 2, 60f);
                    for (int k = 0; k < cn; k++)
                    {
                        float s = (c0 + k + .5f) * step; var p = e.PosAt(s); var q = Basis.LookingAt(e.DirAt(s), Vector3.Up);
                        min = min.Min(p); max = max.Max(p); if ((c0 + k) % 6 == 0) trackPts.Add(p);
                        wires.SetInstanceTransform(k, Xf(q, p, new Vector3(1f, 1f, step / 6f)));
                        for (int i = 0; i < sl; i++)
                        {
                            float zl = -step * .5f + (i + .5f) * step / sl, st = Mathf.Clamp(s - zl, 0f, e.Length);
                            TEdge mm = null; float tm = 0f;
                            if (mS != null && st < zS) { mm = mS; tm = Mathf.Min(st, mS.Length); }
                            else if (mE != null && st > e.Length - zE) { mm = mE; tm = Mathf.Max(0f, mE.Length - (e.Length - st)); }
                            if (mm != null)
                            {
                                var mq = Basis.LookingAt(mm.DirAt(tm), Vector3.Up); Vector3 pm = mm.PosAt(tm), pl = e.PosAt(st);
                                float off = (pl - pm).Dot(mq.X);
                                fan.Add(Xf(mq, pm + mq.X * (off * .5f) + Vector3.Up * .335f, new Vector3(Mathf.Abs(off) + 2.75f, .21f, .26f)));
                                var tiny = Xf(q, p, new Vector3(.0001f, .0001f, .0001f));   // this loop-line sleeper is replaced by the long one
                                slp.SetInstanceTransform(k * sl + i, tiny); slp.SetInstanceColor(k * sl + i, Colors.White);
                                fit.SetInstanceTransform((k * sl + i) * 2, tiny); fit.SetInstanceTransform((k * sl + i) * 2 + 1, tiny);
                                continue;
                            }
                            Vector3 sp = p + q * new Vector3(0f, .33f, zl);
                            slp.SetInstanceTransform(k * sl + i, Xf(q, sp, new Vector3(2.75f, .2f, .26f)));
                            float shade = .8f + .26f * (((c0 + k) * 7 + i * 13 + (c0 + k) * i) * .6180339f % 1f); slp.SetInstanceColor(k * sl + i, new Color(shade, shade * .99f, shade * .96f));
                            fit.SetInstanceTransform((k * sl + i) * 2, Xf(q, sp + q.Y * .1f - q.X * RailG, Vector3.One));
                            fit.SetInstanceTransform((k * sl + i) * 2 + 1, Xf(q, sp + q.Y * .1f + q.X * RailG, Vector3.One));
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
            FillMM(fan, matConcrete);
            foreach (var e in sc.Graph.Edges.Values) if (e.Turnout) BuildJoints(e);
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


        // ---------------------------------------------------------------- platform water pipe line
        const string WaterShader = @"
shader_type spatial;
render_mode unshaded, cull_disabled;
void fragment() {
    float f = fract(UV.y * 3.0 + TIME * 2.2);
    ALBEDO = vec3(0.78, 0.9, 1.0);
    ALPHA = 0.18 + 0.55 * step(0.4, f);
}";

        // Tube along a list of points (used for the pipe).
        static void Tube(SurfaceTool st, List<Vector3> c, float r, int sides)
        {
            for (int i = 0; i + 1 < c.Count; i++)
            {
                Vector3 d0 = (i > 0 ? c[i + 1] - c[i - 1] : c[i + 1] - c[i]).Normalized(), d1 = (i + 2 < c.Count ? c[i + 2] - c[i] : c[i + 1] - c[i]).Normalized();
                Vector3 s0 = d0.Cross(Vector3.Up).Normalized(), u0 = s0.Cross(d0).Normalized(), s1 = d1.Cross(Vector3.Up).Normalized(), u1 = s1.Cross(d1).Normalized();
                for (int k = 0; k < sides; k++)
                {
                    float a = k * Mathf.Tau / sides, b = (k + 1) * Mathf.Tau / sides;
                    Vector3 na0 = s0 * Mathf.Cos(a) + u0 * Mathf.Sin(a), nb0 = s0 * Mathf.Cos(b) + u0 * Mathf.Sin(b), na1 = s1 * Mathf.Cos(a) + u1 * Mathf.Sin(a), nb1 = s1 * Mathf.Cos(b) + u1 * Mathf.Sin(b);
                    Quad(st, c[i] + na0 * r, c[i] + nb0 * r, c[i + 1] + nb1 * r, c[i + 1] + na1 * r, (na0 + nb0).Normalized(), new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
                }
            }
        }

        // A water pipe on yellow and black posts runs along each platform in the gap between the two main lines
        // (Secunderabad, Charlapalli, Ghatkesar), with taps that drip.
        void BuildWaterPipes()
        {
            var black = Mat(new Color(.07f, .07f, .08f)); var yel = Mat(new Color(.95f, .8f, .08f)); var tapMat = Mat(new Color(.5f, .42f, .2f));
            var pipeMat = Solid(new Color(.08f, .09f, .12f), .5f, .45f);
            var water = new ShaderMaterial { Shader = new Shader { Code = WaterShader } };
            var lowers = new List<Transform3D>(); var uppers = new List<Transform3D>(); var brackets = new List<Transform3D>(); var spouts = new List<Transform3D>(); var drips = new List<Transform3D>();
            var tube = new SurfaceTool(); tube.Begin(Mesh.PrimitiveType.Triangles); bool any = false;
            foreach (var stn in sc.Graph.Stations)
            {
                if (stn.Edge.Id.Length == 0 || stn.Edge.Id[0] != 'd') continue;            // one pipe per platform pair, taken from the down line
                if (!(stn.Name.StartsWith("Sec") || stn.Name.StartsWith("Char") || stn.Name.StartsWith("Ghat"))) continue;
                var e = stn.Edge; float lo = Mathf.Max(0f, stn.Offset - 620f), hi = Mathf.Min(e.Length, stn.Offset + 10f);
                int cnt = Mathf.Max(2, Mathf.CeilToInt((hi - lo) / 2f)); var pts = new List<Vector3>();
                for (int i = 0; i <= cnt; i++)
                {
                    float t = lo + (hi - lo) * i / cnt; Vector3 d = e.DirAt(t), g = e.PosAt(t) + Right(d) * 2.25f; var q = Basis.LookingAt(d, Vector3.Up);
                    pts.Add(g + Vector3.Up * 1.1f);
                    if (i % 3 == 0)                                                      // a post every 6 m
                    {
                        lowers.Add(Xf(q, g + Vector3.Up * .225f, new Vector3(.12f, .45f, .12f)));
                        uppers.Add(Xf(q, g + Vector3.Up * .725f, new Vector3(.12f, .55f, .12f)));
                        brackets.Add(Xf(q, g + Vector3.Up * 1.01f, new Vector3(.2f, .05f, .2f)));
                    }
                    if (i % 12 == 4)                                                     // a tap every 24 m, water running to the ground
                    {
                        spouts.Add(Xf(q, g + Vector3.Up * .96f, new Vector3(.06f, .13f, .06f)));
                        drips.Add(Xf(q, g + Vector3.Up * .45f, new Vector3(.02f, .9f, .02f)));
                    }
                }
                Tube(tube, pts, .075f, 8); any = true;
            }
            if (!any) return;
            var mi = new MeshInstance3D { Mesh = tube.Commit(), MaterialOverride = pipeMat }; AddChild(mi); Far(mi, 1500f);
            FillMM(lowers, black); FillMM(uppers, yel); FillMM(brackets, black); FillMM(spouts, tapMat); FillMM(drips, water);
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
