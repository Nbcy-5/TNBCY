using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TNBCY
{
    // MainRealTrack.cs - realistic track add-on for the v13 project (put it next to MainWorld.cs).
    //
    // To use it, change ONE line in Main.cs (StartGame):   BuildTrack();   ->   BuildRealTrack();
    // Nothing else is needed. The old BuildTrack code stays in MainWorld.cs unused, so nothing else breaks.
    //
    // What it does
    //  - Ballast: angular dark granite (like your photo) with a normal map, so the stones have real relief under the sun.
    //  - Sleepers: grey concrete with pits, dark aggregate and stains (like your photo), exactly 0.6 m apart, with rail pads and clips.
    //  - Stable distance LOD: the far sleeper strip sits below the real sleeper tops (no coplanar z-fighting).
    //    Coarser sleeper cadence preserves visible cross-ties at long range without forcing unstable high-frequency detail.
    //  - Points: the grey boxes are gone. The crossing now has a V-shaped cast frog with a polished top between the wing rails and check rails.
    //  - Grass tufts and small stones beside the track (not on platforms, not on the other track).
    public partial class Main
    {
        // ------------------------------------------------------------------ procedural textures
        sealed class RTTex { public byte[] Albedo, Normal; public int Size; }
        sealed class RTChunk { public List<Transform3D> T = new List<Transform3D>(); public List<Color> C = new List<Color>(); }
        static Task<RTTex> rtGraniteJob, rtConcreteJob;
        StandardMaterial3D rtFarRail; Material rtSleeperBedMat;   // v26: flat dark-iron rail and sleeper-bed texture for the distance
        readonly Dictionary<long, List<Vector2>> rtCenters = new Dictionary<long, List<Vector2>>();

        static float RTHash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }
        static int RTWrap(int v, int p) { v %= p; return v < 0 ? v + p : v; }

        static float RTNoise(float x, float y, int p, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi; fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            int x0 = RTWrap(xi, p), x1 = RTWrap(xi + 1, p), y0 = RTWrap(yi, p), y1 = RTWrap(yi + 1, p);
            float a = RTHash(x0, y0, seed), b = RTHash(x1, y0, seed), c = RTHash(x0, y1, seed), d = RTHash(x1, y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float RTFbm(float u, float v, int basePeriod, int oct, int seed)
        {
            float sum = 0f, amp = .5f, norm = 0f; int p = basePeriod;
            for (int o = 0; o < oct; o++) { sum += amp * RTNoise(u * p, v * p, p, seed + o * 17); norm += amp; amp *= .5f; p *= 2; }
            return sum / norm;
        }

        // normal map from a height field (tileable); strength s
        static byte[] RTNormals(float[] h, int size, float s)
        {
            var nrm = new byte[size * size * 3];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float hl = h[y * size + (x + size - 1) % size], hr = h[y * size + (x + 1) % size];
                    float hu = h[((y + size - 1) % size) * size + x], hd = h[((y + 1) % size) * size + x];
                    float nx = -(hr - hl) * s, ny = (hd - hu) * s, nz = 1f;
                    float len = Mathf.Sqrt(nx * nx + ny * ny + nz * nz); nx /= len; ny /= len; nz /= len;
                    int i = (y * size + x) * 3;
                    nrm[i] = (byte)Mathf.Clamp(Mathf.RoundToInt((nx * .5f + .5f) * 255f), 0, 255);
                    nrm[i + 1] = (byte)Mathf.Clamp(Mathf.RoundToInt((ny * .5f + .5f) * 255f), 0, 255);
                    nrm[i + 2] = (byte)Mathf.Clamp(Mathf.RoundToInt((nz * .5f + .5f) * 255f), 0, 255);
                }
            return nrm;
        }

        static byte RTByte(float v) { return (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255); }

        // Crushed granite ballast: irregular angular stones, dark gaps, bright mica flecks, a few brown stones.
        static RTTex RTMakeGranite(int size)
        {
            const int cells = 22;
            var alb = new byte[size * size * 3]; var hgt = new float[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float fu = x / (float)size * cells, fv = y / (float)size * cells;
                    float wu = fu + 1.1f * (RTNoise(fu * .5f, fv * .5f, 11, 91) - .5f), wv = fv + 1.1f * (RTNoise(fu * .5f + 3f, fv * .5f + 7f, 11, 92) - .5f);
                    int xi = Mathf.FloorToInt(wu), yi = Mathf.FloorToInt(wv);
                    float d1 = 99f, d2 = 99f, fx = 0f, fy = 0f; int bx = 0, by = 0;
                    for (int j = -1; j <= 1; j++)
                        for (int i = -1; i <= 1; i++)
                        {
                            int cx = xi + i, cy = yi + j, wx = RTWrap(cx, cells), wy = RTWrap(cy, cells);
                            float px = cx + .1f + .8f * RTHash(wx, wy, 1), py = cy + .1f + .8f * RTHash(wx, wy, 2), sz = .65f + .9f * RTHash(wx, wy, 8);
                            float dx = px - wu, dy = py - wv;
                            float ax = Mathf.Abs(dx), ay = Mathf.Abs(dy);
                            float d = (Mathf.Max(ax, ay) * .6f + (ax + ay) * .4f) / sz;       // angular, octagon-like stones of different sizes
                            if (d < d1) { d2 = d1; d1 = d; bx = wx; by = wy; fx = -dx / sz; fy = -dy / sz; } else if (d < d2) d2 = d;
                        }
                    float r1 = RTHash(bx, by, 3), r2 = RTHash(bx, by, 4), r3 = RTHash(bx, by, 5), r4 = RTHash(bx, by, 7);
                    float bs = .30f + .30f * r1 * r1;                                       // v23: lighter granite / cement grey
                    float cr = bs * 1.02f, cg = bs * 1.01f, cb = bs * .99f;
                    if (r2 > .94f) { cr = .28f + .1f * r1; cg = .19f + .05f * r1; cb = .14f; }                       // brown stone
                    else if (r2 < .16f) { cr = .40f + .15f * r1; cg = .41f + .15f * r1; cb = .42f + .15f * r1; }      // pale stone
                    else if (r2 > .80f) { cr = .50f + .14f * r1; cg = .37f + .09f * r1; cb = .37f + .09f * r1; }      // v26: rose granite chips like the reference photo
                    else if (r2 > .68f) { cr = .40f + .12f * r1; cg = .30f + .08f * r1; cb = .24f + .06f * r1; }      // v26: rust-orange stones
                    float spk = RTHash(x * 5 + bx * 7, y * 5 + by * 3, 6), spk2 = RTHash(x, y, 9);
                    float speck = spk > .90f ? .30f + .2f * spk2 : (spk < .10f ? -.09f : 0f);
                    float facet = Mathf.Clamp(.62f + 1.05f * (.5f + (fx * (r3 - .5f) + fy * (r4 - .5f)) * 1.1f), .25f, 1.7f);
                    float edge = Mathf.Clamp((d2 - d1) * 3.2f, 0f, 1f);
                    float k = facet * (.18f + .82f * Mathf.Pow(edge, .8f));
                    int o = (y * size + x) * 3;
                    float uu = x / (float)size, vv = y / (float)size;
                    float mud = Mathf.Clamp((RTFbm(uu, vv, 5, 3, 61) - .55f) * 4f, 0f, 1f) * .55f;            // v23: dried mud between the stones
                    float grs = Mathf.Clamp((RTFbm(uu, vv, 3, 4, 71) - .60f) * 5f, 0f, 1f) * .65f;            // v23: black diesel grease / oil patches
                    float fr = (cr + speck) * k, fg = (cg + speck) * k, fb = (cb + speck) * k;
                    fr = Mathf.Lerp(fr, .33f * k + .05f, mud); fg = Mathf.Lerp(fg, .26f * k + .04f, mud); fb = Mathf.Lerp(fb, .18f * k + .03f, mud);
                    fr *= 1f - grs * .85f; fg *= 1f - grs * .88f; fb *= 1f - grs * .9f;
                    alb[o] = RTByte(fr); alb[o + 1] = RTByte(fg); alb[o + 2] = RTByte(fb);
                    hgt[y * size + x] = edge * (.55f + .45f * r1) + .04f * spk2;
                }
            return new RTTex { Albedo = alb, Normal = RTNormals(hgt, size, 2.4f), Size = size };
        }

        // Concrete sleeper: warm grey, pitted, sparse dark aggregate and stains.
        static RTTex RTMakeConcrete(int size)
        {
            const int cells = 34;
            var alb = new byte[size * size * 3]; var hgt = new float[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float n = RTFbm(u, v, 4, 4, 11), m = RTFbm(u + .3f, v + .1f, 8, 3, 12);
                    float bs = .45f + .11f * (n - .5f) + .07f * (m - .5f);
                    float cr = bs, cg = bs * .975f, cb = bs * .92f;
                    float uu = u * cells + .8f * (RTFbm(u, v, 6, 3, 41) - .5f), vv = v * cells + .8f * (RTFbm(u + .5f, v + .2f, 6, 3, 42) - .5f);
                    int xi = Mathf.FloorToInt(uu), yi = Mathf.FloorToInt(vv);
                    float dmin = 9f; int sx = 0, sy = 0;
                    for (int j = -1; j <= 1; j++)
                        for (int i = -1; i <= 1; i++)
                        {
                            int cx = xi + i, cy = yi + j, wx = RTWrap(cx, cells), wy = RTWrap(cy, cells);
                            if (RTHash(wx, wy, 20) >= .42f) continue;
                            float px = cx + .15f + .7f * RTHash(wx, wy, 21), py = cy + .15f + .7f * RTHash(wx, wy, 22);
                            float rad = .07f + .30f * RTHash(wx, wy, 23) * RTHash(wx, wy, 23);
                            float d = Mathf.Sqrt((px - uu) * (px - uu) + (py - vv) * (py - vv)) / rad;
                            if (d < dmin) { dmin = d; sx = wx; sy = wy; }
                        }
                    bool spot = dmin < 1f;
                    if (spot)
                    {
                        float sh = .78f + .4f * RTHash(sx, sy, 25); bool dark = RTHash(sx, sy, 24) < .72f;
                        cr = (dark ? .27f : .60f) * sh; cg = (dark ? .28f : .57f) * sh; cb = (dark ? .31f : .52f) * sh;
                    }
                    float f1 = RTHash(x, y, 25), f2 = RTHash(x / 2, y / 2, 26);
                    bool pit = f1 > .955f || f2 > .985f;
                    if (pit) { cr *= .58f; cg *= .58f; cb *= .58f; }
                    float stain = Mathf.Clamp((n - .56f) * 5f, 0f, 1f) * .30f;
                    cr *= 1f - stain * .45f; cg *= 1f - stain * .55f; cb *= 1f - stain * .8f;
                    float kk = .93f + .14f * f1;
                    int o = (y * size + x) * 3;
                    alb[o] = RTByte(cr * kk); alb[o + 1] = RTByte(cg * kk); alb[o + 2] = RTByte(cb * kk);
                    hgt[y * size + x] = .35f * n + .25f * f1 - (pit ? .35f : 0f) + (spot ? .2f : 0f);
                }
            return new RTTex { Albedo = alb, Normal = RTNormals(hgt, size, 1.6f), Size = size };
        }

        // Optional: call once early (for example at the end of _Ready) so the textures are made while the menu is open.
        public static void RTPrefetch()
        {
            if (rtGraniteJob == null) rtGraniteJob = Task.Run(() => RTMakeGranite(512));
            if (rtConcreteJob == null) rtConcreteJob = Task.Run(() => RTMakeConcrete(256));
        }

        static ImageTexture RTTexture(byte[] data, int size, bool normal)
        {
            var img = Image.CreateFromData(size, size, false, Image.Format.Rgb8, data);
            img.GenerateMipmaps(normal);
            return ImageTexture.CreateFromImage(img);
        }

        void RTMaterials()
        {
            RTPrefetch();
            var g = rtGraniteJob.Result; var c = rtConcreteJob.Result;
            matGravel = new StandardMaterial3D
            {
                AlbedoTexture = RTTexture(g.Albedo, g.Size, false), VertexColorUseAsAlbedo = true, NormalEnabled = true, NormalTexture = RTTexture(g.Normal, g.Size, true), NormalScale = 1.6f,
                Roughness = .97f, CullMode = BaseMaterial3D.CullModeEnum.Disabled, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic
            };
            matConcrete = new StandardMaterial3D
            {
                AlbedoTexture = RTTexture(c.Albedo, c.Size, false), NormalEnabled = true, NormalTexture = RTTexture(c.Normal, c.Size, true), NormalScale = 1.1f,
                Roughness = .95f, VertexColorUseAsAlbedo = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic
            };
            rtFarRail = new StandardMaterial3D { AlbedoColor = new Color(.40f, .39f, .36f), Metallic = 0f, Roughness = 1f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };   // matte iron, never reflects the sky
            rtSleeperBedMat = TexMat(ProcTex.SleeperBed());
        }

        // ------------------------------------------------------------------ ground
        const string RTGroundShader = @"
shader_type spatial;
render_mode diffuse_lambert, specular_disabled;
uniform sampler2D grass_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D dirt_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D macro_tex : filter_linear_mipmap, repeat_enable;
varying vec3 wp;
void vertex() { wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
    vec2 uv = mod(wp.xz, 5000.0);
    float fd = smoothstep(60.0, 220.0, length(VERTEX));
    vec3 m1 = texture(macro_tex, uv * 0.0012).rgb;
    vec3 m2 = texture(macro_tex, uv * 0.011).rgb;
    float mud = smoothstep(0.52, 0.68, m1.r * 0.6 + m2.g * 0.4);
    vec3 g1 = mix(texture(grass_tex, uv * 0.2).rgb, vec3(0.30, 0.42, 0.16), fd * 0.6);
    vec3 g2 = texture(grass_tex, uv * 0.021).rgb;
    vec3 grass = g1 * (0.55 + 0.9 * g2) * mix(vec3(0.85, 0.95, 0.72), vec3(1.1, 1.05, 0.82), m2.b);
    vec3 dirt = mix(texture(dirt_tex, uv * 0.22).rgb, vec3(0.30, 0.22, 0.15), fd * 0.6) * (0.6 + 0.8 * m2.r);
    ALBEDO = mix(grass, dirt, mud);
    ROUGHNESS = 0.96;
}";

        // ------------------------------------------------------------------ ballast bed and rails
        // Same as BuildRibbons, but the bed has a buried skirt (no gap or z-fight with the ground) and tangents for the normal map.
        void RTRibbons(TEdge e)
        {
            var ss = new List<float> { 0f }; float s = 0f;
            while (s < e.Length - .01f)
            {
                float k = Curv(e, s); s = Mathf.Min(e.Length, s + Mathf.Clamp(.126f / Mathf.Sqrt(k + 1e-6f), 3f, 25f)); ss.Add(s);
            }
            float uvOff = RTUvBase(e);
            int sCnt = Mathf.Max(1, Mathf.RoundToInt(e.Length / RTSleeperSpacing)); float sStep = e.Length / sCnt;   // same spacing as the real sleepers
            int i0 = 0;
            while (i0 < ss.Count - 1)
            {
                int i1 = i0 + 1; while (i1 < ss.Count - 1 && ss[i1] - ss[i0] < 150f) i1++;      // v26: 150 m pieces, so the level of detail switches in small steps
                var bed = new SurfaceTool(); bed.Begin(Mesh.PrimitiveType.Triangles);
                var rail = new SurfaceTool(); rail.Begin(Mesh.PrimitiveType.Triangles);
                var far1 = new SurfaceTool(); far1.Begin(Mesh.PrimitiveType.Triangles);
                var far2 = new SurfaceTool(); far2.Begin(Mesh.PrimitiveType.Triangles);
                var far3 = new SurfaceTool(); far3.Begin(Mesh.PrimitiveType.Triangles);
                for (int i = i0; i < i1; i++)
                {
                    float sa = ss[i], sb = ss[i + 1];
                    Vector3 p0 = e.PosAt(sa), p1 = e.PosAt(sb), r0 = Right(e.DirAt(sa)), r1 = Right(e.DirAt(sb)), up = Vector3.Up;
                    const float tw = 1.72f, bw = 2.15f, h = .40f, dp = -.18f;
                    Vector3 nl = (-r0 * h + up * (bw - tw)).Normalized(), nr = (r0 * h + up * (bw - tw)).Normalized();
                    float ua = (sa + uvOff) * .5f, ub = (sb + uvOff) * .5f;       // v24: texture phase continues across piece joints
                    Color cToe = new Color(.50f, .44f, .37f), cShl = new Color(.80f, .75f, .68f), cOut = new Color(.95f, .93f, .90f), cGrs = new Color(.46f, .43f, .40f), cMid = new Color(.68f, .65f, .60f);
                    RTBand(bed, p0 - r0 * (bw + .25f) + up * dp, p0 - r0 * bw, p1 - r1 * (bw + .25f) + up * dp, p1 - r1 * bw, nl, -(bw + .25f) * .5f, -bw * .5f, ua, ub, cToe, cToe);
                    RTBand(bed, p0 - r0 * bw, p0 - r0 * tw + up * h, p1 - r1 * bw, p1 - r1 * tw + up * h, nl, -bw * .5f, -tw * .5f, ua, ub, cToe, cShl);
                    float[] la = { -tw, -1.30f, -RailG, -.45f, .45f, RailG, 1.30f, tw };
                    Color[] lc = { cShl, cOut, cGrs, cMid, cMid, cGrs, cOut, cShl };            // grease and dark stone right under the rails
                    for (int bi = 0; bi < la.Length - 1; bi++)
                        RTBand(bed, p0 + r0 * la[bi] + up * h, p0 + r0 * la[bi + 1] + up * h, p1 + r1 * la[bi] + up * h, p1 + r1 * la[bi + 1] + up * h, up, la[bi] * .5f, la[bi + 1] * .5f, ua, ub, lc[bi], lc[bi + 1]);
                    RTBand(bed, p0 + r0 * tw + up * h, p0 + r0 * bw, p1 + r1 * tw + up * h, p1 + r1 * bw, nr, tw * .5f, bw * .5f, ua, ub, cShl, cToe);
                    RTBand(bed, p0 + r0 * bw, p0 + r0 * (bw + .25f) + up * dp, p1 + r1 * bw, p1 + r1 * (bw + .25f) + up * dp, nr, bw * .5f, (bw + .25f) * .5f, ua, ub, cToe, cToe);
                    if (!(e.Turnout && (sb <= e.SkipStart + 20f || sa >= e.Length - e.SkipEnd - 20f)))      // v26: distant sleepers are a texture on a strip over the ballast
                    {
                        // Keep this distance-only sleeper surface below the real sleeper tops and rail feet.
                        // This removes the coplanar overlap that caused shimmer while stopped or moving.
                        float sy = h + .015f;
                        Vector3 a0 = p0 - r0 * 1.375f + up * sy, b0 = p0 + r0 * 1.375f + up * sy;
                        Vector3 a1 = p1 - r1 * 1.375f + up * sy, b1 = p1 + r1 * 1.375f + up * sy;
                    }
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (e.Turnout && side > 0 && ((e.SkipStart > 0f && sb <= TongueLen) || (e.SkipEnd > 0f && sa >= e.Length - TongueLen))) continue;
                        RailExtrude(rail, p0, p1, r0, r1, side * RailG, -side);
                        float fx = side * RailG;      // v26: flat dark-iron strips for the distance; wider the further away, so a rail never shrinks below a pixel
                        RTFarStrip(far1, p0, p1, r0, r1, fx, .10f); RTFarStrip(far2, p0, p1, r0, r1, fx, .30f); RTFarStrip(far3, p0, p1, r0, r1, fx, .60f);
                    }
                }
                bed.GenerateTangents();
                var mb = new MeshInstance3D { Mesh = bed.Commit(), MaterialOverride = matGravel }; AddChild(mb); Far(mb, 2600f);
                var mr = new MeshInstance3D { Mesh = rail.Commit(), MaterialOverride = matRail }; AddChild(mr); Lod(mr, 0f, 200f);
                var m1 = new MeshInstance3D { Mesh = far1.Commit(), MaterialOverride = rtFarRail }; AddChild(m1); Lod(m1, 140f, 520f);
                var m2 = new MeshInstance3D { Mesh = far2.Commit(), MaterialOverride = rtFarRail }; AddChild(m2); Lod(m2, 440f, 1150f);
                var m3 = new MeshInstance3D { Mesh = far3.Commit(), MaterialOverride = rtFarRail }; AddChild(m3); Lod(m3, 1000f, 2600f);
                i0 = i1;
            }
        }

        // Flat strip lying on the top of a rail (used only far away).
        static void RTFarStrip(SurfaceTool st, Vector3 p0, Vector3 p1, Vector3 r0, Vector3 r1, float xc, float hw)
        {
            Vector3 up = Vector3.Up * .60f;
            Strip(st, p0 + r0 * (xc - hw) + up, p0 + r0 * (xc + hw) + up, p1 + r1 * (xc - hw) + up, p1 + r1 * (xc + hw) + up, Vector3.Up, 0f, 1f, 0f, 1f);
        }

        // ------------------------------------------------------------------ points (switch and crossing)
        static void RTTri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 nn, Color col)
        {
            st.SetColor(col); st.SetNormal(nn); st.AddVertex(a);
            st.SetColor(col); st.SetNormal(nn); st.AddVertex(b);
            st.SetColor(col); st.SetNormal(nn); st.AddVertex(c);
        }

        // A cast frog: V-shaped body with a polished running surface and rusty sides, flush with the rail heads.
        void RTAddFrog(TEdge lp, float s, TEdge main, float sm)
        {
            Vector3 dl = lp.DirAt(s), dm = main.DirAt(sm);
            float side = Right(dm).Dot(lp.PosAt(s) - main.PosAt(sm)) > 0f ? 1f : -1f;      // +1: the loop lies on the right of the main line
            Vector3 x = lp.PosAt(s) - Right(dl) * (side * RailG);                           // where the two inner rails cross
            Vector3 b = (dm + dl).Normalized(), n = Right(b), up = Vector3.Up;
            float top = .44f + .172f + .004f, bot = .44f + .02f;
            var pts = new Vector3[]
            {
                x + up * top,
                x + b * .9f + n * .04f + up * top, x + b * 2.0f + n * .06f + up * top,
                x + b * 2.0f - n * .06f + up * top, x + b * .9f - n * .04f + up * top
            };
            Color polished = new Color(.38f, .36f, .35f), rust = new Color(.27f, .18f, .13f);
            var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
            for (int i = 1; i < pts.Length - 1; i++) RTTri(st, pts[0], pts[i], pts[i + 1], up, polished);
            Vector3 ctr = x + b * 1.0f + up * (top * .5f);
            for (int i = 0; i < pts.Length; i++)
            {
                Vector3 pa = pts[i], pb = pts[(i + 1) % pts.Length], pa0 = new Vector3(pa.X, bot, pa.Z), pb0 = new Vector3(pb.X, bot, pb.Z);
                Vector3 nn = (pb - pa).Cross(up).Normalized(); if (nn.Dot((pa + pb) * .5f - ctr) < 0f) nn = -nn;
                RTTri(st, pa, pb, pb0, nn, rust); RTTri(st, pa, pb0, pa0, nn, rust);
            }
            var frogMat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Metallic = .2f, Roughness = .55f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = frogMat });
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

        void RTTurnouts()
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
                if (lp.Turnout && lp.SkipStart > 0f) RTAddFrog(lp, lp.SkipStart, main, Mathf.Min(lp.SkipStart, main.Length));
            }
            foreach (var e in sc.Graph.Edges.Values)
                if (e.Turnout && e.SkipEnd > 0f) { var m = MainOf(e, false); if (m != null) RTAddFrog(e, e.Length - e.SkipEnd, m, Mathf.Max(0f, m.Length - e.SkipEnd)); }
        }

        float RTZoneStart(TEdge e, TEdge m)
        {
            for (float t = 0f; t < e.Length; t += 1f) if (e.PosAt(t).DistanceTo(m.PosAt(Mathf.Min(t, m.Length))) >= 3.2f) return t;
            return 0f;
        }
        float RTZoneEnd(TEdge e, TEdge m)
        {
            for (float t = 0f; t < e.Length; t += 1f) if (e.PosAt(e.Length - t).DistanceTo(m.PosAt(Mathf.Max(0f, m.Length - t))) >= 3.2f) return t;
            return 0f;
        }

        // ------------------------------------------------------------------ the track itself (use instead of BuildTrack)
        // A mast stands 3.7 m beside its track. Near the points a loop line runs close by, so the mast is slid along the track (up to 20 m)
        // until it is at least 2.7 m from every loop/main neighbour, and left out if there is no free spot. Loop masts that would stand
        // right beside the main line's own masts are left out too (the main line's masts already carry the wires there).
        const float MastOffset = 3.7f, MastClear = 2.7f;

        float MastPlace(TEdge e, float s)
        {
            if (e.Turnout && NearMainLine(e.PosAt(s), e, 5.5f)) return -1f;
            for (int k = 0; k <= 20; k++)
            {
                float t = s + (k == 0 ? 0f : ((k + 1) / 2) * 2f * (k % 2 == 1 ? 1f : -1f));
                if (t < 1f || t > e.Length - 1f) continue;
                if (MastClearOfOthers(e, t)) return t;
            }
            return -1f;
        }

        static float DistToPts(Vector3 q, System.Collections.Generic.List<Vector3> pts)
        {
            float best = float.MaxValue;
            for (int k = 0; k + 1 < pts.Count; k++)
            {
                Vector3 a = pts[k], b = pts[k + 1], ab = b - a; ab.Y = 0f; Vector3 aq = q - a; aq.Y = 0f;
                float l2 = ab.LengthSquared(), u = l2 > 1e-6f ? Mathf.Clamp(aq.Dot(ab) / l2, 0f, 1f) : 0f;
                best = Mathf.Min(best, (aq - ab * u).Length());
            }
            return best;
        }

        bool MastClearOfOthers(TEdge e, float t)
        {
            Vector3 q = e.PosAt(t) - Right(e.DirAt(t)) * MastOffset;
            foreach (var o in sc.Graph.Edges.Values)
            {
                if (o == e || !(o.Turnout || e.Turnout)) continue;      // only a loop line can come near a mast
                if (DistToPts(q, o.Pts) < MastClear) return false;
            }
            return true;
        }

        bool NearMainLine(Vector3 p, TEdge e, float within)
        {
            foreach (var o in sc.Graph.Edges.Values)
                if (o != e && !o.Turnout && DistToPts(p, o.Pts) < within && o.Id[0] == e.Id[0]) return true;   // a main line of the same direction
            return false;
        }

        void BuildRealTrack()
        {
            RTMaterials();
            MakeTrackMeshes();
            RTV24Init();                                            // v24: N26-style sleeper and rail fastening
            RTV28Init();                                            // v28 base track materials
            RTV29Init();                                            // v29: rounded rail profile, muddy ballast, gently bowed sleepers
            RTFixJoints();                                          // v23: both pieces share one direction at every joint
            var min = new Vector3(1e9f, 0, 1e9f); var max = new Vector3(-1e9f, 0, -1e9f);
            foreach (var e in sc.Graph.Edges.Values)
            {
                RTRibbons(e); RTFormation(e);
                // Each track edge gets its own sleeper across its own rail pair.
                // Do not create shared/fan sleepers between adjacent tracks at turnouts.
                int cnt = Mathf.Max(1, Mathf.RoundToInt(e.Length / RTSleeperSpacing)); float slStep = e.Length / cnt;   // v23: even spacing, no odd gap at piece joints
                for (int c0 = 0; c0 < cnt; c0 += 220)
                {
                    int cn = Mathf.Min(220, cnt - c0);
                    var tl = new List<Transform3D>(); var cl = new List<Color>(); var fl = new List<Transform3D>();
                var lodNear = new List<Transform3D>(); var lodMid = new List<Transform3D>(); var lodFar = new List<Transform3D>();
                    for (int k = 0; k < cn; k++)
                    {
                        float st = (c0 + k + .5f) * slStep; if (st > e.Length) break;
                        var p = e.PosAt(st); var q = Basis.LookingAt(e.DirAt(st), Vector3.Up);
                        min = min.Min(p); max = max.Max(p); if ((c0 + k) % 60 == 0) trackPts.Add(p);
                        float shade = .86f + .24f * RTHash(c0 + k, 11, 3);
                        Vector3 sp = p + Vector3.Up * .33f;
                        var sleeperXf = Xf(q, sp, new Vector3(2.75f, .21f, .26f));
                        tl.Add(sleeperXf); cl.Add(new Color(shade, shade * .99f, shade * .96f));
                        int sleeperIndex = c0 + k;
                        if (sleeperIndex % 2 == 0) lodNear.Add(sleeperXf);
                        if (sleeperIndex % 4 == 0) lodMid.Add(sleeperXf);
                        if (sleeperIndex % 8 == 0) lodFar.Add(sleeperXf);
                        fl.Add(Xf(q, sp + q.Y * .1f - q.X * RailG, Vector3.One)); fl.Add(Xf(q, sp + q.Y * .1f + q.X * RailG, Vector3.One));
                    }
                    if (tl.Count == 0) continue;
                    var slp = MM(rtSleeperN26, matConcrete, tl.Count, 112.5f, true); // Far() applies .8 scale: exact 90 m handover to sleeper LOD
                    var fit = MM(fitMesh, matFit, fl.Count, 55f);
                    for (int i = 0; i < tl.Count; i++) { slp.SetInstanceTransform(i, tl[i]); slp.SetInstanceColor(i, cl[i]); }
                    for (int i = 0; i < fl.Count; i++) fit.SetInstanceTransform(i, fl[i]);

                    // Replace the old coplanar distance texture with actual low-poly curved sleepers.
                    // This keeps cross-ties crisp and avoids a second textured plane shimmering over the ballast.
                    var farNear = MM(rtSleeperLod, matConcrete, lodNear.Count); Lod(farNear.GetParent() as GeometryInstance3D, 82f, 480f);
                    for (int i = 0; i < lodNear.Count; i++) farNear.SetInstanceTransform(i, lodNear[i]);
                    var farMid = MM(rtSleeperLod, matConcrete, lodMid.Count); Lod(farMid.GetParent() as GeometryInstance3D, 480f, 1250f);
                    for (int i = 0; i < lodMid.Count; i++) farMid.SetInstanceTransform(i, lodMid[i]);
                    var farFar = MM(rtSleeperLod, matConcrete, lodFar.Count); Lod(farFar.GetParent() as GeometryInstance3D, 1250f, 2600f);
                    for (int i = 0; i < lodFar.Count; i++) farFar.SetInstanceTransform(i, lodFar[i]);
                }
                RTBuildOHE(e);                                       // v23: realistic 25 kV overhead line
            }
            foreach (var e in sc.Graph.Edges.Values) if (e.Turnout) BuildJoints(e);
            RTTurnouts();
            var c = (min + max) * .5f;
            var gm = new ShaderMaterial { Shader = new Shader { Code = RTGroundShader } };
            gm.SetShaderParameter("grass_tex", ProcTex.Grass()); gm.SetShaderParameter("dirt_tex", ProcTex.Dirt()); gm.SetShaderParameter("macro_tex", ProcTex.Macro());
            AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(max.X - min.X + 8000f, max.Z - min.Z + 8000f) }, MaterialOverride = gm, Position = new Vector3(c.X, -.03f, c.Z), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            RTScatter();
        }

        // ------------------------------------------------------------------ grass tufts and small stones beside the track
        static long RTKey(int cx, int cz) { return ((long)cx << 32) ^ (uint)cz; }

        void RTAddCenter(Vector3 p)
        {
            long key = RTKey(Mathf.FloorToInt(p.X / 12f), Mathf.FloorToInt(p.Z / 12f)); List<Vector2> l;
            if (!rtCenters.TryGetValue(key, out l)) { l = new List<Vector2>(); rtCenters[key] = l; }
            l.Add(new Vector2(p.X, p.Z));
        }

        // true when no track centre line or platform lies within r metres of p
        bool RTFree(Vector3 p, float r)
        {
            int cx = Mathf.FloorToInt(p.X / 12f), cz = Mathf.FloorToInt(p.Z / 12f); float r2 = r * r;
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    List<Vector2> l; if (!rtCenters.TryGetValue(RTKey(cx + dx, cz + dz), out l)) continue;
                    foreach (var q in l) { float ex = q.X - p.X, ez = q.Y - p.Z; if (ex * ex + ez * ez < r2) return false; }
                }
            return true;
        }

        static void RTBlade(SurfaceTool st, Vector3 p, Color c) { st.SetColor(c); st.SetNormal(Vector3.Up); st.AddVertex(p); }

        static ArrayMesh RTTuftMesh()
        {
            return BuildMesh(st =>
            {
                Color dark = new Color(.10f, .17f, .06f), light = new Color(.55f, .65f, .25f);
                for (int b = 0; b < 9; b++)
                {
                    float a = b * Mathf.Tau / 9f + .35f * RTHash(b, 3, 5), lean = .25f + .35f * RTHash(b, 4, 5), h = .22f + .3f * RTHash(b, 5, 5);
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), sd = new Vector3(-dir.Z, 0f, dir.X) * .035f, root = dir * .04f;
                    Vector3 tip = root + dir * (lean * h) + Vector3.Up * h;
                    RTBlade(st, root - sd, dark); RTBlade(st, root + sd, dark); RTBlade(st, tip, light);
                }
            });
        }

        void RTScatter()
        {
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
            var tuft = RTTuftMesh();
            var grassMat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 1f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            var rockMesh = new SphereMesh { Radius = .5f, Height = 1f, RadialSegments = 6, Rings = 3 };
            var rockMat = Mat(Colors.White); rockMat.VertexColorUseAsAlbedo = true;
            var rng = new RandomNumberGenerator { Seed = 2024 };
            foreach (var e in sc.Graph.Edges.Values)
            {
                var grass = new Dictionary<int, RTChunk>(); var rocks = new Dictionary<int, RTChunk>();
                for (float s = 0f; s < e.Length; s += 1.3f)
                {
                    Vector3 c = e.PosAt(s), r = Right(e.DirAt(s)); int ck = (int)(s / 300f);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (rng.Randf() < .72f)
                        {
                            float lat = 2.75f + Mathf.Pow(rng.Randf(), 1.7f) * 5.5f;
                            Vector3 pos = c + r * (side * lat); pos.Y = -.01f;
                            if (RTFree(pos, 2.65f))
                            {
                                RTChunk ch; if (!grass.TryGetValue(ck, out ch)) { ch = new RTChunk(); grass[ck] = ch; }
                                float sz = rng.RandfRange(.7f, 1.6f);
                                var bs = new Basis(Vector3.Up, rng.RandfRange(0f, Mathf.Tau)) * Basis.FromScale(new Vector3(sz, sz * rng.RandfRange(.8f, 1.5f), sz));
                                ch.T.Add(new Transform3D(bs, pos));
                                float tt = rng.Randf(); ch.C.Add(new Color(.6f + .45f * tt, .85f + .25f * tt, .55f + .1f * tt));
                            }
                        }
                        if (rng.Randf() < .35f)
                        {
                            float lat = 2.3f + rng.Randf() * 1.7f;
                            Vector3 pos = c + r * (side * lat);
                            if (RTFree(pos, 2.2f))
                            {
                                RTChunk ch; if (!rocks.TryGetValue(ck, out ch)) { ch = new RTChunk(); rocks[ck] = ch; }
                                float sz = rng.RandfRange(.06f, .2f);
                                var bs = new Basis(Vector3.Up, rng.RandfRange(0f, Mathf.Tau)) * Basis.FromScale(new Vector3(sz * rng.RandfRange(.9f, 1.7f), sz * rng.RandfRange(.45f, .9f), sz * rng.RandfRange(.9f, 1.7f)));
                                pos.Y = sz * .2f; ch.T.Add(new Transform3D(bs, pos));
                                float g = rng.RandfRange(.28f, .55f); ch.C.Add(new Color(g, g * .97f, g * .92f));
                            }
                        }
                    }
                }
                foreach (var kv in grass)
                {
                    var ch = kv.Value; var mm = MM(tuft, grassMat, ch.T.Count, 190f, true);
                    for (int i = 0; i < ch.T.Count; i++) { mm.SetInstanceTransform(i, ch.T[i]); mm.SetInstanceColor(i, ch.C[i]); }
                }
                foreach (var kv in rocks)
                {
                    var ch = kv.Value; var mm = MM(rockMesh, rockMat, ch.T.Count, 120f, true);
                    for (int i = 0; i < ch.T.Count; i++) { mm.SetInstanceTransform(i, ch.T[i]); mm.SetInstanceColor(i, ch.C[i]); }
                }
            }
        }
    }
}
