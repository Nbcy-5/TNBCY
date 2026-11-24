using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // v23 track add-on: coloured ballast bands, muddy-cement formation under/beside the ballast (no more green strip),
    // seamless joints between track pieces, and a realistic 25 kV AC overhead line (steel masts, cantilever, sagging
    // messenger wire, staggered contact wire, droppers, insulators).
    public partial class Main
    {
        public const float RTSleeperSpacing = .65f;            // concrete sleepers, centre to centre (was 0.60 m)
        StandardMaterial3D rtMatCu, rtMatMast;
        ArrayMesh rtMastA, rtMastB;
        ShaderMaterial rtMudMat;

        // ------------------------------------------------------------------ small mesh helpers
        static void RTV(SurfaceTool st, Vector3 p, Vector3 n, Vector2 uv, Color c)
        {
            st.SetColor(c); st.SetNormal(n); st.SetUV(uv); st.AddVertex(p);
        }

        // Quad a0-b0-b1-a1 with a colour at each side (a = ca, b = cb).
        static void RTBand(SurfaceTool st, Vector3 a0, Vector3 b0, Vector3 a1, Vector3 b1, Vector3 n, float ua, float ub, float v0, float v1, Color ca, Color cb)
        {
            Vector2 u00 = new Vector2(ua, v0), u10 = new Vector2(ub, v0), u11 = new Vector2(ub, v1), u01 = new Vector2(ua, v1);
            RTV(st, a0, n, u00, ca); RTV(st, b0, n, u10, cb); RTV(st, b1, n, u11, cb);
            RTV(st, a0, n, u00, ca); RTV(st, b1, n, u11, cb); RTV(st, a1, n, u01, ca);
        }

        // Square bar between two points (tubes, wires, arms, truss members).
        static void RTBar(SurfaceTool st, Vector3 a, Vector3 b, float t, Color col)
        {
            Vector3 d = b - a; float len = d.Length(); if (len < 1e-4f) return; d /= len;
            Vector3 up = Mathf.Abs(d.Y) < .95f ? Vector3.Up : Vector3.Right;
            Vector3 s = d.Cross(up).Normalized() * (t * .5f);
            Vector3 v = d.Cross(s).Normalized() * (t * .5f);
            Vector3[] c0 = { a - s - v, a + s - v, a + s + v, a - s + v };
            Vector3[] c1 = { b - s - v, b + s - v, b + s + v, b - s + v };
            Vector3 ctr = (a + b) * .5f; st.SetColor(col);
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                Vector3 n = (c0[j] - c0[i]).Cross(c1[i] - c0[i]).Normalized();
                if (n.Dot((c0[i] + c0[j] + c1[i] + c1[j]) * .25f - ctr) < 0f) n = -n;
                st.SetNormal(n); st.AddVertex(c0[i]); st.SetNormal(n); st.AddVertex(c0[j]); st.SetNormal(n); st.AddVertex(c1[j]);
                st.SetNormal(n); st.AddVertex(c0[i]); st.SetNormal(n); st.AddVertex(c1[j]); st.SetNormal(n); st.AddVertex(c1[i]);
            }
        }

        // ------------------------------------------------------------------ seamless joints
        // Neighbouring track pieces met with a 0.6 to 1.6 degree kink, which left small wedge gaps in ballast, rails and wires.
        // Both pieces now share one bisector direction at the joint (main line to main line).
        void RTFixJoints()
        {
            foreach (var n in sc.Graph.Nodes.Values)
            {
                TEdge mi = null, mo = null;
                foreach (var e in sc.Graph.Edges.Values) { if (e.To == n && !e.Turnout && mi == null) mi = e; if (e.From == n && !e.Turnout && mo == null) mo = e; }
                if (mi == null || mo == null || mi == mo || mi.Tan.Count == 0 || mo.Tan.Count == 0) continue;
                Vector3 t = mi.Tan[mi.Tan.Count - 1] + mo.Tan[0]; t.Y = 0f;
                if (t.LengthSquared() < 1e-6f) continue;
                t = t.Normalized(); mi.Tan[mi.Tan.Count - 1] = t; mo.Tan[0] = t;
            }
        }

        // ------------------------------------------------------------------ muddy cement formation under and beside the ballast
        static RTTex RTMakeMudCement(int size)
        {
            var alb = new byte[size * size * 3];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float n1 = RTFbm(u, v, 4, 5, 81), n2 = RTFbm(u + .37f, v + .11f, 9, 3, 82), n3 = RTFbm(u, v, 3, 4, 83);
                    float b = .40f + .22f * (n1 - .5f) + .10f * (n2 - .5f);                        // cement grey
                    float mud = Mathf.Clamp((n3 - .48f) * 3.2f, 0f, 1f);                            // mud patches
                    float cr = Mathf.Lerp(b, .34f, mud * .8f), cg = Mathf.Lerp(b * .98f, .27f, mud * .8f), cb = Mathf.Lerp(b * .94f, .19f, mud * .8f);
                    float grs = Mathf.Clamp((RTFbm(u + .6f, v + .3f, 5, 3, 84) - .66f) * 6f, 0f, 1f) * .55f;   // oil
                    cr *= 1f - grs; cg *= 1f - grs; cb *= 1f - grs * .95f;
                    float f = .92f + .16f * RTHash(x, y, 85);
                    int o = (y * size + x) * 3;
                    alb[o] = RTByte(cr * f); alb[o + 1] = RTByte(cg * f); alb[o + 2] = RTByte(cb * f);
                }
            return new RTTex { Albedo = alb, Normal = null, Size = size };
        }

        const string RTMudShader = @"
shader_type spatial;
render_mode diffuse_lambert, specular_disabled;
uniform sampler2D mud_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D macro_tex : filter_linear_mipmap, repeat_enable;
varying vec3 wp;
void vertex() { wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
    vec2 uv = mod(wp.xz, 5000.0);
    vec3 t1 = texture(mud_tex, uv * 0.32).rgb;
    vec3 t2 = texture(mud_tex, uv * 0.045).rgb;
    vec3 m = texture(macro_tex, uv * 0.011).rgb;
    float wet = smoothstep(0.35, 0.72, m.g);
    ALBEDO = t1 * (0.65 + 0.7 * t2) * mix(vec3(1.0), vec3(0.62, 0.55, 0.46), wet);
    ROUGHNESS = mix(0.95, 0.65, wet * 0.5);
}";

        void RTFormation(TEdge e)
        {
            if (rtMudMat == null)
            {
                var t = RTMakeMudCement(256);
                rtMudMat = new ShaderMaterial { Shader = new Shader { Code = RTMudShader } };
                rtMudMat.SetShaderParameter("mud_tex", RTTexture(t.Albedo, t.Size, false));
                rtMudMat.SetShaderParameter("macro_tex", ProcTex.Macro());
            }
            const float hw = 2.95f, y = .05f;            // the strip hides the green ground for 2.95 m each side of the centre line
            int n = Mathf.Max(1, Mathf.CeilToInt(e.Length / 5f)); float ds = e.Length / n;
            for (int c0 = 0; c0 < n; c0 += 60)
            {
                int c1 = Mathf.Min(n, c0 + 60);
                var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
                for (int i = c0; i < c1; i++)
                {
                    float sa = i * ds, sb = (i + 1) * ds;
                    Vector3 p0 = e.PosAt(sa), p1 = e.PosAt(sb), r0 = Right(e.DirAt(sa)), r1 = Right(e.DirAt(sb)), up = Vector3.Up;
                    Vector3 a0 = p0 - r0 * hw, b0 = p0 + r0 * hw, a1 = p1 - r1 * hw, b1 = p1 + r1 * hw; a0.Y = y; b0.Y = y; a1.Y = y; b1.Y = y;
                    RTV(st, a0, up, Vector2.Zero, Colors.White); RTV(st, b0, up, Vector2.Zero, Colors.White); RTV(st, b1, up, Vector2.Zero, Colors.White);
                    RTV(st, a0, up, Vector2.Zero, Colors.White); RTV(st, b1, up, Vector2.Zero, Colors.White); RTV(st, a1, up, Vector2.Zero, Colors.White);
                }
                var mi = new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = rtMudMat }; AddChild(mi); Far(mi, 1200f);
            }
        }

        // ------------------------------------------------------------------ overhead line (25 kV AC, Indian practice)
        const float OheContact = 5.75f, OheMess = 7.15f, OheSag = .85f, OheStagger = .2f;

        ArrayMesh RTMakeMast(float stagger)
        {
            return BuildMesh(st =>
            {
                Color steel = new Color(.58f, .60f, .62f), gal = new Color(.70f, .72f, .74f), conc = new Color(.60f, .59f, .56f), ins = new Color(.42f, .27f, .18f), dark = new Color(.12f, .12f, .13f);
                const float mx = -3.7f;
                AddBoxC(st, new Vector3(mx, .12f, 0f), new Vector3(.95f, .56f, .95f), conc);                    // concrete foundation block
                AddBoxC(st, new Vector3(mx, .42f, 0f), new Vector3(.52f, .04f, .52f), dark);                    // base plate
                AddBoxC(st, new Vector3(mx, 4.55f, -.135f), new Vector3(.27f, 8.2f, .022f), steel);             // I-section: two flanges
                AddBoxC(st, new Vector3(mx, 4.55f, .135f), new Vector3(.27f, 8.2f, .022f), steel);
                AddBoxC(st, new Vector3(mx, 4.55f, 0f), new Vector3(.022f, 8.2f, .27f), steel);                 // and the web
                AddBoxC(st, new Vector3(mx, 8.7f, 0f), new Vector3(.3f, .06f, .3f), dark);                      // top cap
                for (int i = 0; i < 4; i++) AddBoxC(st, new Vector3(mx, 1.3f + i * 1.9f, 0f), new Vector3(.31f, .03f, .31f), dark);   // stiffener bands
                // cantilever: upper bracket tube, stay tube, registration arm, steady arm
                RTBar(st, new Vector3(mx, 7.55f, 0f), new Vector3(.0f, 7.55f, 0f), .085f, gal);
                RTBar(st, new Vector3(mx, 8.35f, 0f), new Vector3(-.1f, 7.6f, 0f), .06f, gal);
                RTBar(st, new Vector3(mx, 6.55f, 0f), new Vector3(stagger, 6.2f, 0f), .06f, gal);
                RTBar(st, new Vector3(stagger, 6.2f, 0f), new Vector3(stagger, OheContact + .02f, 0f), .03f, steel);
                for (int i = 0; i < 4; i++) AddBoxC(st, new Vector3(mx + .28f + i * .1f, 7.55f, 0f), new Vector3(.035f, .17f, .17f), ins);   // bracket insulator, ribbed porcelain
                for (int i = 0; i < 4; i++) AddBoxC(st, new Vector3(0f, 7.42f - i * .06f, 0f), new Vector3(.15f, .035f, .15f), ins);       // hanger insulator
                RTBar(st, new Vector3(0f, 7.25f, 0f), new Vector3(0f, OheMess, 0f), .03f, steel);                // messenger support clamp
                AddBoxC(st, new Vector3(0f, OheMess, 0f), new Vector3(.08f, .06f, .08f), dark);
            });
        }

        void RTOheInit()
        {
            if (rtMastA != null) return;
            rtMatCu = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Metallic = .3f, Roughness = .5f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            rtMatMast = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Metallic = .45f, Roughness = .55f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            rtMastA = RTMakeMast(OheStagger); rtMastB = RTMakeMast(-OheStagger);
        }

        static float RTStag(int k) { return (k & 1) == 0 ? OheStagger : -OheStagger; }

        float RTLastMast(TEdge e) { return Mathf.Floor(e.Length / MastSpacing - .5f); }

        float RTStagger(TEdge e, float s)
        {
            int lastM = (int)RTLastMast(e); if (lastM < 0) return 0f;
            float first = .5f * MastSpacing, last = (lastM + .5f) * MastSpacing;
            if (s < first) return Mathf.Lerp(0f, RTStag(0), s / first);                    // fades to zero at the piece ends so wires join exactly
            if (s > last) return Mathf.Lerp(RTStag(lastM), 0f, Mathf.Clamp((s - last) / Mathf.Max(.5f, e.Length - last), 0f, 1f));
            float g = s / MastSpacing - .5f; int m = Mathf.FloorToInt(g); float f = g - m;
            return Mathf.Lerp(RTStag(m), RTStag(m + 1), f);
        }

        float RTMessH(TEdge e, float s)
        {
            int lastM = (int)RTLastMast(e); if (lastM < 0) return OheMess;
            if (s < .5f * MastSpacing || s > (lastM + .5f) * MastSpacing) return OheMess;
            float g = s / MastSpacing - .5f; float f = g - Mathf.Floor(g);
            return OheMess - OheSag * 4f * f * (1f - f);                                    // parabolic sag of the messenger wire between masts
        }

        void RTBuildOHE(TEdge e)
        {
            RTOheInit();
            int n = Mathf.Max(1, Mathf.CeilToInt(e.Length / 4.5f)); float step = e.Length / n;
            Color cu = new Color(.55f, .33f, .2f), mess = new Color(.62f, .64f, .66f), drop = new Color(.55f, .57f, .6f);
            for (int c0 = 0; c0 < n; c0 += 40)
            {
                int c1 = Mathf.Min(n, c0 + 40);
                var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
                for (int i = c0; i < c1; i++)
                {
                    float sa = i * step, sb = (i + 1) * step;
                    Vector3 ma = e.PosAt(sa) + Vector3.Up * RTMessH(e, sa), mb = e.PosAt(sb) + Vector3.Up * RTMessH(e, sb);
                    Vector3 ca = e.PosAt(sa) + Right(e.DirAt(sa)) * RTStagger(e, sa) + Vector3.Up * OheContact;
                    Vector3 cb = e.PosAt(sb) + Right(e.DirAt(sb)) * RTStagger(e, sb) + Vector3.Up * OheContact;
                    RTBar(st, ma, mb, .03f, mess);
                    RTBar(st, ca, cb, .026f, cu);
                    if ((i & 1) == 1)
                    {
                        float g = sb / MastSpacing - .5f; float f = g - Mathf.Floor(g); float dm = Mathf.Min(f, 1f - f) * MastSpacing;
                        int lastM = (int)RTLastMast(e);
                        if (dm > 3f && lastM >= 0 && sb > .5f * MastSpacing && sb < (lastM + .5f) * MastSpacing)
                            RTBar(st, mb, cb, .012f, drop);   // dropper joins the messenger to the contact wire
                    }
                }
                var mi = new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = rtMatCu }; AddChild(mi); Far(mi, 520f);
            }
            // masts: cantilever to the track, standing on the driver's left
            var ta = new List<Transform3D>(); var tb = new List<Transform3D>();
            for (int m = 0; (m + .5f) * MastSpacing < e.Length; m++)
            {
                float sp = MastPlace(e, (m + .5f) * MastSpacing); if (sp < 0f) continue;
                var tr = Xf(Basis.LookingAt(e.DirAt(sp), Vector3.Up), e.PosAt(sp), Vector3.One);
                if ((m & 1) == 0) ta.Add(tr); else tb.Add(tr);
            }
            if (ta.Count > 0) { var mm = MM(rtMastA, rtMatMast, ta.Count, 1100f); for (int i = 0; i < ta.Count; i++) mm.SetInstanceTransform(i, ta[i]); }
            if (tb.Count > 0) { var mm = MM(rtMastB, rtMatMast, tb.Count, 1100f); for (int i = 0; i < tb.Count; i++) mm.SetInstanceTransform(i, tb[i]); }
        }
    }
}
