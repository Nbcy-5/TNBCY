using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // Locomotive models (.glb from Assets/Locos) and the round iron wheelsets that turn with the train's speed.
    public partial class Main
    {
        const float RailTop = .612f;     // rail head height above the track point (rail foot .44 + head .172)
        const int WSeg = 32;             // segments around a wheel (round, not a polygon)

        class WheelSet { public MultiMesh MM; public float R; public Vector3[] Pos; public float Phase; }

        readonly List<LocoSpec> locoList = new List<LocoSpec>();
        readonly Dictionary<string, LocoSpec> locoCat = new Dictionary<string, LocoSpec>();
        StandardMaterial3D wheelMat; ShaderMaterial hideMat;

        // ------------------------------------------------------------------ catalogue (Data/locos.json)
        void LoadLocoCatalog()
        {
            if (locoList.Count > 0) return;
            try
            {
                var lst = LoadJson<LocoList>("res://Data/locos.json");
                if (lst != null && lst.locos != null)
                    foreach (var s in lst.locos)
                        if (s != null && !string.IsNullOrEmpty(s.id) && s.axles != null && s.axles.Count >= 2 && !locoCat.ContainsKey(s.id)) { locoList.Add(s); locoCat[s.id] = s; }
            }
            catch (System.Exception ex) { GD.PrintErr("locos.json: " + ex.Message); }
        }

        static VehicleSpec LocoVehicle(LocoSpec l)
        {
            return new VehicleSpec
            {
                name = l.name + " electric loco", count = 1, massTonnes = l.massTonnes, lengthM = l.lengthM,
                brakeForceKn = l.brakeForceKn, powered = true, maxPowerKw = l.maxPowerKw, maxTractiveEffortKn = l.maxTractiveEffortKn,
                maxSpeedKmh = l.maxSpeedKmh, colorRgb = 0x1f6f5c, model = l.id, bogieCentreM = l.bogieCentreM
            };
        }

        // ------------------------------------------------------------------ the 3D loco
        // The .glb body comes from Assets/Locos (front toward -Z, Y up, origin on the rail). The game's front is +Z, so it is turned round.
        // The wheels inside the file (if any) are hidden: round iron wheelsets that really turn are used instead.
        Node3D BuildLocoNode(LocoSpec ls)
        {
            PackedScene ps = null;
            try { ps = GD.Load<PackedScene>("res://Assets/Locos/" + ls.model + ".glb"); }
            catch (System.Exception ex) { GD.PrintErr("loco model: " + ex.Message); }
            if (ps == null) return null;
            var inst = ps.Instantiate() as Node3D;
            if (inst == null) return null;
            HideWheelSurfaces(inst);
            var holder = new Node3D();
            var yaw = new Node3D { Position = new Vector3(0f, RailTop, 0f), Rotation = new Vector3(0f, Mathf.Pi, 0f) };
            holder.AddChild(yaw);
            yaw.AddChild(inst);
            inst.Position = new Vector3(0f, 0f, -ls.zc);
            if (ls.frames) holder.AddChild(new MeshInstance3D { Mesh = LocoFrameMesh(ls), MaterialOverride = carMat });
            return holder;
        }

        ShaderMaterial HideMat()
        {
            if (hideMat == null) hideMat = new ShaderMaterial { Shader = new Shader { Code = "shader_type spatial;\nvoid fragment() { discard; }\n" } };
            return hideMat;
        }

        void HideWheelSurfaces(Node n)
        {
            if (n is MeshInstance3D mi && mi.Mesh != null)
            {
                int cnt = mi.Mesh.GetSurfaceCount();
                for (int s = 0; s < cnt; s++)
                {
                    var mat = mi.Mesh.SurfaceGetMaterial(s);
                    string nm = mat != null ? (mat.ResourceName ?? "").ToLowerInvariant() : "";
                    if (nm.Contains("wheel")) mi.SetSurfaceOverrideMaterial(s, HideMat());
                }
            }
            foreach (Node ch in n.GetChildren()) HideWheelSurfaces(ch);
        }

        // ------------------------------------------------------------------ bogie frames (for models that have none)
        // Two side beams under the floor, axle boxes at the hubs and two cross members. bz = bogie centre along the car, offs = axle offsets from it.
        static void BogieFrame(SurfaceTool st, float bz, float[] offs, float yc, float beamY, float beamH)
        {
            var iron = new Color(.13f, .12f, .12f); var rust = new Color(.26f, .16f, .11f); var black = new Color(.05f, .05f, .06f);
            float a0 = offs[0], a1 = offs[offs.Length - 1], mid = (a0 + a1) * .5f, span = a1 - a0;
            foreach (float sx in new[] { -1f, 1f })
            {
                BoxC(st, new Vector3(sx * 1.04f, beamY, bz + mid), new Vector3(.12f, beamH, span + .9f), sx > 0f ? iron : rust);
                foreach (float o in offs) BoxC(st, new Vector3(sx * 1.03f, yc, bz + o), new Vector3(.16f, .26f, .30f), black);
            }
            foreach (float k in new[] { -1f, 1f }) BoxC(st, new Vector3(0f, beamY, bz + mid + k * span * .25f), new Vector3(1.4f, .14f, .30f), rust);
        }

        ArrayMesh LocoFrameMesh(LocoSpec ls)
        {
            string key = "locoframes|" + ls.id; ArrayMesh m; if (carMeshes.TryGetValue(key, out m)) return m;
            int n = ls.axles.Count, half = n / 2;
            m = BuildMesh(st =>
            {
                for (int bi = 0; bi < 2; bi++)
                {
                    int i0 = bi * half, i1 = bi == 0 ? half : n; float mean = 0f;
                    for (int k = i0; k < i1; k++) mean += ls.axles[k];
                    mean /= (i1 - i0);
                    var offs = new float[i1 - i0];
                    for (int k = i0; k < i1; k++) offs[k - i0] = ls.axles[k] - mean;
                    BogieFrame(st, mean, offs, RailTop + ls.wheelR, RailTop + .88f, .16f);
                }
            });
            carMeshes[key] = m; return m;
        }

        // ------------------------------------------------------------------ wheels
        static float Hash01(int n)
        {
            unchecked
            {
                uint h = (uint)(n * 374761393 + 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFu) / 65535f;
            }
        }

        // Iron: mostly grey steel with a little brown rust and some black. rust / dark 0..1, lift = lighter (worn running surface).
        static Color IronCol(int seed, float rust, float dark, float lift)
        {
            Color c = new Color(.34f, .34f, .36f).Lerp(new Color(.24f, .24f, .26f), Hash01(seed));
            float rr = Mathf.Clamp(rust * (.35f + .65f * Hash01(seed + 977)), 0f, 1f);
            c = c.Lerp(new Color(.40f, .23f, .13f), rr);
            c = c.Lerp(new Color(.05f, .05f, .06f), Mathf.Clamp(dark, 0f, 1f));
            return lift > 0f ? c.Lightened(lift) : c;
        }

        // Flat disc or ring in the plane x = const (r0 = 0 gives a full disc).
        static void DiscX(SurfaceTool st, float x, float r0, float r1, float nx, int seed, float rust, float dark, float lift)
        {
            Vector3 n = new Vector3(nx, 0f, 0f), c0 = new Vector3(x, 0f, 0f);
            for (int i = 0; i < WSeg; i++)
            {
                float a0 = i * Mathf.Tau / WSeg, a1 = (i + 1) * Mathf.Tau / WSeg;
                Vector3 d0 = new Vector3(0f, Mathf.Cos(a0), Mathf.Sin(a0)), d1 = new Vector3(0f, Mathf.Cos(a1), Mathf.Sin(a1));
                Color col = IronCol(seed + i * 3, rust, dark, lift);
                if (r0 <= .001f) { VC(st, c0, n, col); VC(st, c0 + d0 * r1, n, col); VC(st, c0 + d1 * r1, n, col); }
                else QC(st, c0 + d0 * r0, c0 + d0 * r1, c0 + d1 * r1, c0 + d1 * r0, n, col);
            }
        }

        // Cylinder wall around the X axis with smooth normals.
        static void BandX(SurfaceTool st, float x0, float x1, float r, int seed, float rust, float dark, float lift)
        {
            for (int i = 0; i < WSeg; i++)
            {
                float a0 = i * Mathf.Tau / WSeg, a1 = (i + 1) * Mathf.Tau / WSeg;
                Vector3 n0 = new Vector3(0f, Mathf.Cos(a0), Mathf.Sin(a0)), n1 = new Vector3(0f, Mathf.Cos(a1), Mathf.Sin(a1));
                Color col = IronCol(seed + i * 3, rust, dark, lift);
                Vector3 p00 = new Vector3(x0, 0f, 0f) + n0 * r, p10 = new Vector3(x1, 0f, 0f) + n0 * r, p11 = new Vector3(x1, 0f, 0f) + n1 * r, p01 = new Vector3(x0, 0f, 0f) + n1 * r;
                VC(st, p00, n0, col); VC(st, p10, n0, col); VC(st, p11, n1, col);
                VC(st, p00, n0, col); VC(st, p11, n1, col); VC(st, p01, n1, col);
            }
        }

        // One wheel: tread, flange, rim, recessed rusty web with spoke ribs, black hub. s = +1 right wheel, -1 left wheel. Back-to-back 1.600 m, rail centre at 0.873 m.
        static void WheelSide(SurfaceTool st, float s, float r, int seed)
        {
            float xi = s * .80f, xo = s * .935f, xw = s * .90f, xf = s * .826f;
            float rh = .17f, rr = r * .80f;
            BandX(st, xi, xo, r, seed, .06f, 0f, .16f);                       // running surface: worn, lighter iron
            BandX(st, xi, xf, r + .028f, seed + 11, .10f, .55f, 0f);          // flange: dark
            DiscX(st, xi, 0f, r + .028f, -s, seed + 17, .35f, .30f, 0f);      // inner face
            DiscX(st, xf, r, r + .028f, s, seed + 19, .10f, .55f, 0f);        // flange shoulder
            DiscX(st, xo, rr, r, s, seed + 23, .22f, .08f, .04f);            // rim face
            BandX(st, xw, xo, rr, seed + 31, .60f, .30f, 0f);                 // wall of the recess
            DiscX(st, xw, rh, rr, s, seed + 41, .70f, .22f, 0f);              // web: rusty
            BandX(st, xw, xo + s * .07f, rh, seed + 53, .12f, .62f, 0f);      // hub: black
            DiscX(st, xo + s * .07f, 0f, rh, s, seed + 59, .08f, .66f, 0f);   // hub cap
            for (int k = 0; k < 8; k++)                                       // spoke ribs on the web: they make the turning easy to see
            {
                float a = k * Mathf.Tau / 8f + .2f;
                Vector3 d = new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a)), cx = new Vector3(xw + s * .0175f, 0f, 0f);
                Color rc = (k & 1) == 0 ? IronCol(seed + 70 + k, .05f, .75f, 0f) : IronCol(seed + 90 + k, .75f, .15f, 0f);
                OBoxC(st, cx + d * (rh - .01f), cx + d * (rr + .01f), .035f, rc);
            }
        }

        // A wheelset (axle + two wheels) centred on its own axle, axis along X; it is turned about X every frame.
        ArrayMesh WheelsetMesh(float r)
        {
            string key = "wheelset|" + r.ToString("0.000"); ArrayMesh m; if (carMeshes.TryGetValue(key, out m)) return m;
            m = BuildMesh(st =>
            {
                WheelSide(st, 1f, r, 101); WheelSide(st, -1f, r, 211);
                BandX(st, -.80f, .80f, .085f, 307, .25f, .55f, 0f);          // axle
            });
            carMeshes[key] = m; return m;
        }

        StandardMaterial3D WheelMat()
        {
            if (wheelMat == null) wheelMat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Metallic = .5f, Roughness = .5f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            return wheelMat;
        }

        // All wheelsets of one car in a single MultiMesh (one draw call per car). zs = axle positions along the car (+Z = front).
        void AddWheels(TrainVis vis, Node3D car, int carIdx, float r, float[] zs)
        {
            var pos = new Vector3[zs.Length];
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = WheelsetMesh(r), InstanceCount = zs.Length };
            for (int k = 0; k < zs.Length; k++) { pos[k] = new Vector3(0f, RailTop + r, zs[k]); mm.SetInstanceTransform(k, new Transform3D(Basis.Identity, pos[k])); }
            var inst = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = WheelMat(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityRangeEnd = 170f };
            car.AddChild(inst);
            vis.Wheels.Add(new WheelSet { MM = mm, R = r, Pos = pos, Phase = carIdx * .37f });
        }
    }
}
