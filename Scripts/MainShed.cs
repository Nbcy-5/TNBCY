using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // v5.24: new platform sheds for Secunderabad and Charlapalli, after the photos: blue corrugated sheet roofs with a green valance.
    //  - Island platforms: a butterfly roof (the outer edges are higher) on a centre column, like the V-shaped shed in the photo.
    //  - Side platforms: a lean-to roof, higher at the back. Platform 1 at Secunderabad is joined to the station building / wall behind it.
    //  - The sheet is drawn by a small shader (ribs along the slope), so no image files are needed.
    //  - A bay of roof is left out where the foot-over-bridge stairs come down or an overhead-line mast stands (Charlapalli).
    // The roof is built in 10 m bays from PfPiece (MainPlatforms.cs); ShdFinish turns the collected boxes into two MultiMeshes.
    public partial class Main
    {
        bool shdOn, shdSec;                                                          // set for every station in PfBuildStations
        readonly List<Transform3D> shdSlab = new List<Transform3D>(), shdVal = new List<Transform3D>();
        readonly List<Vector2> shdMasts = new List<Vector2>(); bool shdMastsDone;   // x, z of every ordinary overhead-line mast of the route

        const string ShdShader = @"
shader_type spatial;
render_mode diffuse_lambert, specular_disabled;
varying vec3 wp;
varying vec3 ax;
varying vec3 wn;
void vertex() {
    wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    ax = normalize(MODEL_MATRIX[2].xyz);
    wn = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}
void fragment() {
    float s = dot(wp, ax) / 0.115;
    float rib = 0.5 + 0.5 * sin(s * 6.28318);
    vec3 top = vec3(0.12, 0.36, 0.66) * (0.78 + 0.30 * rib);
    vec3 under = vec3(0.30, 0.33, 0.37);
    vec3 side = vec3(0.10, 0.28, 0.50);
    vec3 c = side;
    if (wn.y > 0.5) { c = top; } else if (wn.y < -0.5) { c = under; }
    ALBEDO = c;
    ROUGHNESS = 0.6;
}
";

        void ShdCollectMasts()
        {
            if (shdMastsDone) return;
            shdMastsDone = true;
            foreach (var e2 in sc.Graph.Edges.Values)
                for (int m = 0; (m + .5f) * MastSpacing < e2.Length; m++)
                {
                    float sp = MastPlace(e2, (m + .5f) * MastSpacing); if (sp < 0f) continue;
                    Vector3 pm = e2.PosAt(sp);
                    if (RTInPortalZone(pm)) continue;                                  // inside the station area the portals replace the single masts
                    Vector3 mp = pm - Right(e2.DirAt(sp)) * MastOffset;
                    shdMasts.Add(new Vector2(mp.X, mp.Z));
                }
        }

        bool ShdMastNear(Vector3 p, float r)
        {
            float r2 = r * r;
            foreach (var mm in shdMasts) { float dx = mm.X - p.X, dz = mm.Y - p.Z; if (dx * dx + dz * dz < r2) return true; }
            return false;
        }

        // Roof, columns and lights of one 40 m platform piece (s0..s1) in 10 m bays. w = platform width, far = island platform.
        void ShdRoof(TEdge e, float s0, float s1, float sm, float w, bool far)
        {
            Vector3 up = Vector3.Up;
            float xe = SecEdge, xc = xe + w * .5f, xf = xe + w;
            float rA, rB;                                              // roof extents, measured from the track centre
            if (far) { float rw = shdSec ? Mathf.Clamp(w - 3.6f, 2.4f, 5.2f) : Mathf.Clamp(w - 1.8f, 2.0f, 5.0f); rA = xc - rw * .5f; rB = xc + rw * .5f; }
            else { rA = xe + 1.9f; rB = xf; }
            float rW = rB - rA, rC = (rA + rB) * .5f, rise = .5f;
            bool wall = e.Id == sbmEdgeId;                             // Platform 1 has the station building or the wall behind it
            for (float u0 = s0; u0 < s1 - .5f; u0 += 10f)
            {
                float u1 = Mathf.Min(u0 + 10f, s1), um = (u0 + u1) * .5f, bl = u1 - u0 + .1f;
                Vector3 pm = e.PosAt(um), dm = e.DirAt(um); Basis qm = Basis.LookingAt(dm, up); Vector3 rm = Right(dm) * sm;
                if (PfFobZone(pm + rm * xc)) continue;                 // the foot-over-bridge stairs come down here
                if (ShdMastNear(pm + rm * rC, 5.5f)) continue;         // an overhead-line mast stands here: a gap in the roof
                int bi = Mathf.RoundToInt((u0 - s0) / 10f);
                if (far)
                {
                    float half = rW * .5f, th = Mathf.Atan2(rise, half), slope = Mathf.Sqrt(half * half + rise * rise);
                    for (int t = -1; t <= 1; t += 2)
                    {
                        Basis tb = qm * new Basis(Vector3.Back, th * (sm * t));        // butterfly roof: the outer edges are higher
                        shdSlab.Add(Xf(tb, pm + rm * (xc + t * half * .5f) + up * (4.95f + rise * .5f), new Vector3(slope, .08f, bl)));
                        shdVal.Add(Xf(qm, pm + rm * (xc + t * half) + up * (4.95f + rise - .22f), new Vector3(.06f, .46f, bl)));
                    }
                    secCol.Add(Xf(qm, pm + rm * xc + up * ((SecTop + 4.91f) * .5f), new Vector3(.28f, 4.91f - SecTop, .28f)));
                    float lx = xc + ((bi & 1) == 0 ? 1f : -1f) * half * .55f, ly = 4.91f + rise * .55f - .10f;
                    secLight.Add(Xf(qm, pm - dm * 2.5f + rm * lx + up * ly, new Vector3(.14f, .07f, 1.3f)));
                    secLight.Add(Xf(qm, pm + dm * 2.5f + rm * lx + up * ly, new Vector3(.14f, .07f, 1.3f)));
                }
                else
                {
                    float yF = 4.80f, yB = yF + rise, th = Mathf.Atan2(rise, rW), slope = Mathf.Sqrt(rW * rW + rise * rise);
                    Basis tb = qm * new Basis(Vector3.Back, th * sm);                  // lean-to: higher at the back, where the wall or building is
                    shdSlab.Add(Xf(tb, pm + rm * rC + up * ((yF + yB) * .5f), new Vector3(slope, .08f, bl)));
                    shdVal.Add(Xf(qm, pm + rm * rA + up * (yF - .22f), new Vector3(.06f, .46f, bl)));
                    secCol.Add(Xf(qm, pm + rm * (rA + .2f) + up * ((SecTop + yF - .04f) * .5f), new Vector3(.28f, yF - .04f - SecTop, .28f)));
                    if (!wall) secCol.Add(Xf(qm, pm + rm * (rB - .2f) + up * ((SecTop + yB - .04f) * .5f), new Vector3(.28f, yB - .04f - SecTop, .28f)));
                    float ly = yF - .04f + rise * ((rC - rA) / rW) - .10f;
                    secLight.Add(Xf(qm, pm - dm * 2.5f + rm * rC + up * ly, new Vector3(.14f, .07f, 1.3f)));
                    secLight.Add(Xf(qm, pm + dm * 2.5f + rm * rC + up * ly, new Vector3(.14f, .07f, 1.3f)));
                }
            }
        }

        void ShdFinish()
        {
            if (shdSlab.Count == 0) return;
            var sheet = new ShaderMaterial { Shader = new Shader { Code = ShdShader } };
            FillMM(shdSlab, sheet); FillMM(shdVal, Mat(new Color(.10f, .34f, .20f)));
        }
    }
}
