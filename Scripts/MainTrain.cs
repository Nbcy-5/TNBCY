using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // Trains: each car is ONE mesh with baked colours (few draw calls), pantographs that raise and lower, and the driver's cab.
    public partial class Main
    {
        class TrainVis { public Train T; public List<Node3D> Cars = new List<Node3D>(); public List<Node3D> Pants = new List<Node3D>(); public List<WheelSet> Wheels = new List<WheelSet>(); }
        readonly Dictionary<string, ArrayMesh> carMeshes = new Dictionary<string, ArrayMesh>();
        StandardMaterial3D carMat;
        Node3D cabRig, leverPower, leverBrake, leverRev;
        SubViewport gaugeVp; GaugeCluster gauge; StandardMaterial3D matBtn1, matBtn2;

        static void VC(SurfaceTool st, Vector3 p, Vector3 n, Color c) { st.SetColor(c); st.SetNormal(n); st.AddVertex(p); }
        static void QC(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color col)
        { VC(st, a, n, col); VC(st, b, n, col); VC(st, c, n, col); VC(st, a, n, col); VC(st, c, n, col); VC(st, d, n, col); }
        static void BoxC(SurfaceTool st, Vector3 c, Vector3 s, Color col)
        {
            Vector3 h = s * .5f; float x0 = c.X - h.X, x1 = c.X + h.X, y0 = c.Y - h.Y, y1 = c.Y + h.Y, z0 = c.Z - h.Z, z1 = c.Z + h.Z;
            QC(st, new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.Up, col);
            QC(st, new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), Vector3.Down, col);
            QC(st, new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), Vector3.Right, col);
            QC(st, new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x0, y0, z1), Vector3.Left, col);
            QC(st, new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.Back, col);
            QC(st, new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), Vector3.Forward, col);
        }
        static void OBoxC(SurfaceTool st, Vector3 a, Vector3 b, float t, Color col)       // box between two points (pantograph arms)
        {
            Vector3 d = b - a; float len = d.Length(); if (len < .001f) return; d /= len;
            Vector3 side = d.Cross(Vector3.Up); side = side.LengthSquared() < .01f ? Vector3.Right : side.Normalized();
            Vector3 up2 = side.Cross(d).Normalized(); Vector3 hs = side * (t * .5f), hu = up2 * (t * .5f);
            Vector3 c0 = a - hs - hu, c1 = a + hs - hu, c2 = a + hs + hu, c3 = a - hs + hu, e0 = b - hs - hu, e1 = b + hs - hu, e2 = b + hs + hu, e3 = b - hs + hu;
            QC(st, c3, c2, e2, e3, up2, col); QC(st, c0, c1, e1, e0, -up2, col); QC(st, c1, c2, e2, e1, side, col); QC(st, c0, c3, e3, e0, -side, col);
        }
        static void CylC(SurfaceTool st, Vector3 c, float r, float half, int axis, Color col, int seg = 10)
        {
            Vector3 ax = axis == 0 ? Vector3.Right : (axis == 1 ? Vector3.Up : Vector3.Back);
            Vector3 u1 = axis == 0 ? Vector3.Up : Vector3.Right, u2 = ax.Cross(u1);
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.Tau / seg, a1 = (i + 1) * Mathf.Tau / seg;
                Vector3 d0 = u1 * Mathf.Cos(a0) + u2 * Mathf.Sin(a0), d1 = u1 * Mathf.Cos(a1) + u2 * Mathf.Sin(a1);
                QC(st, c + d0 * r - ax * half, c + d0 * r + ax * half, c + d1 * r + ax * half, c + d1 * r - ax * half, (d0 + d1).Normalized(), col);
            }
        }

        ArrayMesh CarMesh(VehicleSpec v, float L, Color body, bool loco)
        {
            string key = v.name + "|" + v.colorRgb + "|" + L.ToString("0.0") + "|" + body.ToHtml() + "|" + loco;
            ArrayMesh m; if (carMeshes.TryGetValue(key, out m)) return m;
            m = BuildMesh(st =>
            {
                var dark = new Color(.10f, .10f, .12f); var glass = new Color(.05f, .08f, .11f); var cream = new Color(.92f, .90f, .82f);
                var roofC = new Color(.50f, .50f, .52f); var steel = new Color(.36f, .37f, .41f); var black = new Color(.06f, .06f, .07f);
                float bh = loco ? 3.4f : 3.0f, cy = 1.05f + bh * .5f, top = 1.05f + bh, bl = L - .6f;
                BoxC(st, new Vector3(0f, cy, 0f), new Vector3(3.2f, bh, bl), body);
                BoxC(st, new Vector3(0f, top + .1f, 0f), new Vector3(3.0f, .22f, bl - .6f), loco ? body.Darkened(.3f) : roofC);
                float eqHalf = Mathf.Max(1f, L * .37f - (loco ? 3.1f : 2.4f));   // equipment box between the bogies; the wheels stay visible
                BoxC(st, new Vector3(0f, .98f, 0f), new Vector3(2.4f, .42f, eqHalf * 2f), dark);
                if (loco)
                {
                    foreach (float sd in new[] { -1f, 1f })
                    {
                        BoxC(st, new Vector3(0f, 3.5f, sd * (bl * .5f + .02f)), new Vector3(2.5f, 1.1f, .06f), glass);
                        BoxC(st, new Vector3(0f, 2.1f, sd * (bl * .5f + .03f)), new Vector3(3.3f, .5f, .05f), new Color(.9f, .78f, .15f));
                        BoxC(st, new Vector3(-.9f, 3.0f, sd * (bl * .5f + .04f)), new Vector3(.3f, .25f, .05f), new Color(1f, 1f, .85f));
                        BoxC(st, new Vector3(.9f, 3.0f, sd * (bl * .5f + .04f)), new Vector3(.3f, .25f, .05f), new Color(1f, 1f, .85f));
                    }
                    for (int k = 0; k < 6; k++) { float zz = -bl * .32f + k * bl * .13f; BoxC(st, new Vector3(-1.62f, 2.3f, zz), new Vector3(.06f, 1.0f, 1.0f), dark); BoxC(st, new Vector3(1.62f, 2.3f, zz), new Vector3(.06f, 1.0f, 1.0f), dark); }
                    BoxC(st, new Vector3(0f, top + .35f, 3.5f), new Vector3(1.2f, .5f, 2.2f), steel);
                    BoxC(st, new Vector3(0f, top + .3f, -.5f), new Vector3(1.0f, .4f, 1.6f), steel);
                    foreach (float bz in new[] { -L * .37f, L * .37f }) BogieFrame(st, bz, new[] { -2.1f, 0f, 2.1f }, RailTop + .546f, .97f, .14f);   // wheels are separate, they turn
                }
                else
                {
                    BoxC(st, new Vector3(0f, cy + .35f, 0f), new Vector3(3.24f, .8f, bl * .9f), glass);
                    BoxC(st, new Vector3(0f, cy - .3f, 0f), new Vector3(3.24f, .12f, bl * .95f), cream);
                    foreach (float dz in new[] { -bl * .4f, bl * .4f }) { BoxC(st, new Vector3(-1.62f, 1.05f + 1.0f, dz), new Vector3(.05f, 2.0f, 1.1f), body.Darkened(.35f)); BoxC(st, new Vector3(1.62f, 1.05f + 1.0f, dz), new Vector3(.05f, 2.0f, 1.1f), body.Darkened(.35f)); }
                    for (int k = 0; k < 4; k++) BoxC(st, new Vector3(0f, top + .3f, -bl * .3f + k * bl * .2f), new Vector3(.4f, .15f, .6f), steel);
                    BoxC(st, new Vector3(0f, cy, bl * .5f + .02f), new Vector3(3.1f, 2.8f, .12f), body.Darkened(.4f));
                    BoxC(st, new Vector3(0f, cy, -bl * .5f - .02f), new Vector3(3.1f, 2.8f, .12f), body.Darkened(.4f));
                    CylC(st, new Vector3(-.7f, .8f, 0f), .3f, 1.6f, 2, dark); CylC(st, new Vector3(.7f, .8f, 0f), .3f, 1.6f, 2, dark);
                    foreach (float bz in new[] { -L * .37f, L * .37f }) BogieFrame(st, bz, new[] { -1.25f, 1.25f }, RailTop + .4575f, .97f, .14f);   // wheels are separate, they turn
                }
                foreach (float sd in new[] { -1f, 1f })        // buffers and coupler
                {
                    BoxC(st, new Vector3(0f, 1.0f, sd * (L * .5f - .05f)), new Vector3(2.6f, .3f, .12f), new Color(.5f, .1f, .08f));
                    CylC(st, new Vector3(-.9f, 1.0f, sd * (L * .5f + .1f)), .17f, .2f, 2, steel); CylC(st, new Vector3(.9f, 1.0f, sd * (L * .5f + .1f)), .17f, .2f, 2, steel);
                    BoxC(st, new Vector3(0f, .85f, sd * (L * .5f + .1f)), new Vector3(.18f, .18f, .5f), black);
                }
            });
            carMeshes[key] = m; return m;
        }

        ArrayMesh PantMesh(float rise)
        {
            string key = "pant" + rise.ToString("0.00"); ArrayMesh m; if (carMeshes.TryGetValue(key, out m)) return m;
            m = BuildMesh(st =>
            {
                var dark = new Color(.1f, .1f, .12f); var steel = new Color(.45f, .46f, .5f);
                BoxC(st, new Vector3(0f, .05f, 0f), new Vector3(1.6f, .15f, 1.2f), dark);
                foreach (float sx in new[] { -.35f, .35f })
                {
                    OBoxC(st, new Vector3(sx, .1f, 0f), new Vector3(sx, rise * .6f, 1.2f), .09f, steel);
                    OBoxC(st, new Vector3(sx, rise * .6f, 1.2f), new Vector3(sx, rise, 0f), .08f, steel);
                    BoxC(st, new Vector3(sx * 2f, .2f, 0f), new Vector3(.12f, .3f, .12f), dark);
                }
                BoxC(st, new Vector3(0f, rise + .02f, 0f), new Vector3(1.7f, .06f, .5f), dark);
            });
            carMeshes[key] = m; return m;
        }

        void Strut(Node parent, Vector3 a, Vector3 b, float t, Material m)
        {
            Vector3 d = b - a; float len = d.Length(); if (len < .001f) return;
            Box(parent, (a + b) * .5f, Basis.LookingAt(d / len, Vector3.Up), new Vector3(t, t, len), m);
        }

        TrainVis BuildTrain(Train T, Color? locoCol, Color? coachCol)
        {
            var vis = new TrainVis { T = T };
            if (carMat == null) carMat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = .55f, Metallic = .12f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            LoadLocoCatalog();
            for (int i = 0; i < T.Cars.Count; i++)
            {
                var c = T.Cars[i]; var v = c.Spec; float L = c.Length; string nm = (v.name ?? "").ToLowerInvariant();
                bool loco = v.powered && nm.Contains("loco"), pant = v.powered && !nm.Contains("demu") && !nm.Contains("diesel");
                var col = new Color(((v.colorRgb >> 16) & 255) / 255f, ((v.colorRgb >> 8) & 255) / 255f, (v.colorRgb & 255) / 255f);
                if (loco && locoCol.HasValue) col = locoCol.Value; else if (!loco && coachCol.HasValue) col = coachCol.Value;
                LocoSpec ls = null; Node3D car = null;
                if (loco && !string.IsNullOrEmpty(v.model) && locoCat.TryGetValue(v.model, out ls)) car = BuildLocoNode(ls);   // 3D model from Assets/Locos
                if (car == null) { ls = null; car = new MeshInstance3D { Mesh = CarMesh(v, L, col, loco), MaterialOverride = carMat }; }
                AddChild(car); vis.Cars.Add(car);
                if (ls != null) AddWheels(vis, car, i, ls.wheelR, ls.axles.ToArray());
                else if (loco) AddWheels(vis, car, i, .546f, new[] { -L * .37f - 2.1f, -L * .37f, -L * .37f + 2.1f, L * .37f - 2.1f, L * .37f, L * .37f + 2.1f });
                else AddWheels(vis, car, i, .4575f, new[] { -L * .37f - 1.25f, -L * .37f + 1.25f, L * .37f - 1.25f, L * .37f + 1.25f });
                if (pant)
                {
                    float baseY = ls != null ? RailTop + ls.roofY : 1.05f + (loco ? 3.4f : 3.0f) + .1f;
                    var pn = new MeshInstance3D { Mesh = PantMesh(6.32f - baseY), MaterialOverride = carMat, Position = new Vector3(0f, baseY, ls != null ? ls.pantZ : -3.6f) };
                    car.AddChild(pn); vis.Pants.Add(pn);
                }
            }
            return vis;
        }

        // Cars sit on their bogies: each car stands between two points on the track 13% in from its ends.
        void UpdateTrain(TrainVis vis, bool hideLoco, float pantK)
        {
            var T = vis.T;
            for (int i = 0; i < vis.Cars.Count; i++)
            {
                var c = T.Cars[i]; float ins = c.Spec.bogieCentreM > .5f ? Mathf.Max(.1f, c.Length * .5f - c.Spec.bogieCentreM) : c.Length * .13f;
                T.PointAt(T.Odo - c.Offset - ins, out var pf, out var df); T.PointAt(T.Odo - c.Offset - c.Length + ins, out var pr, out _);
                var fwd = pf - pr; fwd.Y = 0f; fwd = fwd.LengthSquared() < .0001f ? df : fwd.Normalized();
                vis.Cars[i].Transform = new Transform3D(new Basis(Vector3.Up, Mathf.Atan2(fwd.X, fwd.Z)), (pf + pr) * .5f);
            }
            if (vis.Cars.Count > 0) vis.Cars[0].Visible = !hideLoco;
            foreach (var pn in vis.Pants) pn.Scale = new Vector3(1f, Mathf.Lerp(.12f, 1f, pantK), 1f);
            float odo = T.Odo;      // the wheels turn with the distance run: angle = distance / radius
            foreach (var w in vis.Wheels)
            {
                float ang = Mathf.PosMod(odo / w.R, Mathf.Tau);
                for (int k = 0; k < w.Pos.Length; k++) w.MM.SetInstanceTransform(k, new Transform3D(new Basis(Vector3.Right, ang + w.Phase + k * .9f), w.Pos[k]));
            }
        }

        // ------------------------------------------------------------------ driver's cab
        void BuildCab()
        {
            cabRig = new Node3D(); cam.AddChild(cabRig); cabRig.Visible = false;
            var cream = Mat(new Color(.80f, .76f, .64f)); var dark = Mat(new Color(.09f, .10f, .12f)); var panelC = Mat(new Color(.16f, .18f, .21f));
            var ceil = Mat(new Color(.86f, .85f, .80f)); var rubber = Mat(new Color(.06f, .06f, .07f)); var metal = Solid(new Color(.65f, .67f, .72f), .8f, .35f);
            var lamp = Solid(Colors.White); lamp.EmissionEnabled = true; lamp.Emission = new Color(.9f, .9f, .8f);
            Box(cabRig, new Vector3(0f, -1.6f, -.3f), Basis.Identity, new Vector3(2.9f, .1f, 3.4f), rubber);
            foreach (float sx in new[] { -1f, 1f })
            {
                Box(cabRig, new Vector3(sx * 1.45f, -.98f, -.2f), Basis.Identity, new Vector3(.1f, 1.25f, 2.6f), cream);
                Box(cabRig, new Vector3(sx * 1.45f, .78f, -.2f), Basis.Identity, new Vector3(.1f, .45f, 2.6f), cream);
                Box(cabRig, new Vector3(sx * 1.45f, -.3f, .8f), Basis.Identity, new Vector3(.1f, 2.6f, .8f), cream);
                Box(cabRig, new Vector3(sx * 1.45f, .1f, -1.5f), Basis.Identity, new Vector3(.12f, 1.2f, .14f), dark);
                Box(cabRig, new Vector3(sx * 1.26f, .28f, -1.6f), new Basis(Vector3.Back, -sx * .22f), new Vector3(.30f, 2.2f, .22f), dark);      // heavy raked windscreen pillars
                Box(cabRig, new Vector3(sx * .81f, -.083f, -1.55f), new Basis(Vector3.Back, -sx * .95f), new Vector3(.03f, 1.3f, .02f), dark);    // long wiper arms
                Box(cabRig, new Vector3(sx * .30f, -.45f, -1.55f), Basis.Identity, new Vector3(.07f, .05f, .05f), metal);                       // wiper pivots
            }
            Box(cabRig, new Vector3(0f, .3f, -1.6f), Basis.Identity, new Vector3(.12f, 2.0f, .18f), dark);       // centre pillar
            Box(cabRig, new Vector3(0f, .98f, -1.6f), Basis.Identity, new Vector3(2.9f, .2f, .28f), dark);       // header
            Box(cabRig, new Vector3(0f, .80f, -1.38f), new Basis(Vector3.Right, .10f), new Vector3(2.7f, .05f, .6f), Mat(new Color(.52f, .53f, .52f)));    // grey sun blind
            Box(cabRig, new Vector3(0f, 1.0f, -.2f), Basis.Identity, new Vector3(2.9f, .08f, 2.8f), ceil);
            Box(cabRig, new Vector3(0f, .95f, -.3f), Basis.Identity, new Vector3(.5f, .04f, .2f), lamp);
            Box(cabRig, new Vector3(0f, -1.05f, -1.15f), Basis.Identity, new Vector3(2.9f, 1.1f, 1.0f), dark);   // console body (top at -0.5)
            Box(cabRig, new Vector3(0f, -.48f, -1.2f), Basis.Identity, new Vector3(2.9f, .06f, .9f), panelC);
            Box(cabRig, new Vector3(0f, -.22f, -1.28f), Basis.Identity, new Vector3(1.45f, .5f, .3f), dark);     // instrument hood
            foreach (float sx in new[] { -1f, 1f }) Box(cabRig, new Vector3(sx * 1.0f, -.50f, -.88f), new Basis(Vector3.Right, .3f), new Vector3(.9f, .08f, .7f), panelC);

            gaugeVp = new SubViewport { Size = new Vector2I(1536, 448), RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled, TransparentBg = false, Disable3D = true };
            AddChild(gaugeVp);
            gauge = new GaugeCluster { Position = Vector2.Zero, Size = new Vector2(1536f, 448f) }; gaugeVp.AddChild(gauge);
            var gm = new StandardMaterial3D { AlbedoTexture = gaugeVp.GetTexture(), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear };
            cabRig.AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(1.9f, .5542f) }, MaterialOverride = gm, Position = new Vector3(0f, -.30f, -1.0f), Rotation = new Vector3(-.55f, 0f, 0f) });

            matBtn1 = Solid(new Color(.1f, .3f, .12f)); matBtn1.EmissionEnabled = true; matBtn2 = Solid(new Color(.1f, .3f, .12f)); matBtn2.EmissionEnabled = true;
            Box(cabRig, new Vector3(.82f, -.47f, -1.05f), Basis.Identity, new Vector3(.09f, .04f, .09f), matBtn1);
            Box(cabRig, new Vector3(.96f, -.47f, -1.05f), Basis.Identity, new Vector3(.09f, .04f, .09f), matBtn2);
            leverPower = new Node3D { Position = new Vector3(.98f, -.5f, -.85f) }; cabRig.AddChild(leverPower);
            leverBrake = new Node3D { Position = new Vector3(-.98f, -.5f, -.85f) }; cabRig.AddChild(leverBrake);
            leverRev = new Node3D { Position = new Vector3(.62f, -.5f, -.9f) }; cabRig.AddChild(leverRev);
            foreach (var lv in new[] { leverPower, leverBrake, leverRev })
            {
                Box(lv, new Vector3(0f, .11f, 0f), Basis.Identity, new Vector3(.03f, .22f, .03f), metal);
                Box(lv, new Vector3(0f, .25f, 0f), Basis.Identity, new Vector3(.09f, .08f, .09f), Mat(lv == leverPower ? new Color(.94f, .63f, .19f) : (lv == leverBrake ? new Color(.85f, .3f, .22f) : new Color(.3f, .5f, .8f))));
            }
            AddCabDetail(dark, panelC, metal);
        }

        Label3D cabDigi;

        // Extra cab detail from the reference photos: peg-board wall, key panel, emergency button, red digital display.
        void AddCabDetail(StandardMaterial3D dark, StandardMaterial3D panelC, StandardMaterial3D metal)
        {
            Box(cabRig, new Vector3(1.385f, -.02f, -1.28f), Basis.Identity, new Vector3(.02f, 1.5f, .75f), TexMat(ProcTex.Perforated()));   // beige perforated board
            Box(cabRig, new Vector3(0f, -.306f, -1.018f), new Basis(Vector3.Right, -.55f), new Vector3(2.0f, .60f, .04f), dark);             // bezel behind the instrument screen
            var palette = new[] { new Color(.9f, .2f, .15f), new Color(.2f, .8f, .3f), new Color(.95f, .8f, .2f), new Color(.25f, .55f, .95f), new Color(.92f, .92f, .92f) };
            var keyMats = new StandardMaterial3D[palette.Length];
            for (int i = 0; i < palette.Length; i++) { keyMats[i] = Solid(palette[i] * .75f); keyMats[i].EmissionEnabled = true; keyMats[i].Emission = palette[i] * .25f; }
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 5; c++)
                    Box(cabRig, new Vector3(-1.38f + c * .07f, -.435f, -1.12f + r * .07f), Basis.Identity, new Vector3(.05f, .025f, .05f), keyMats[(r * 5 + c) % palette.Length]);
            var redMat = Solid(new Color(.85f, .08f, .06f), .1f, .35f); redMat.EmissionEnabled = true; redMat.Emission = new Color(.25f, .02f, .02f);
            cabRig.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .034f, BottomRadius = .04f, Height = .045f, RadialSegments = 14 }, MaterialOverride = redMat, Position = new Vector3(-.60f, -.425f, -.98f) });   // emergency mushroom button
            cabRig.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .055f, BottomRadius = .055f, Height = .02f, RadialSegments = 16 }, MaterialOverride = metal, Position = new Vector3(-.60f, -.44f, -.98f) });
            Box(cabRig, new Vector3(-.62f, -.343f, -1.046f), new Basis(Vector3.Right, -.55f), new Vector3(.26f, .12f, .01f), Mat(new Color(.02f, .02f, .02f)));   // display body
            cabDigi = new Label3D { Text = "00", FontSize = 64, PixelSize = .0013f, Modulate = new Color(1f, .22f, .14f), Font = UiFont.Bold, Position = new Vector3(-.62f, -.34f, -1.038f), Rotation = new Vector3(-.55f, 0f, 0f) };
            cabRig.AddChild(cabDigi);
        }

        void UpdateCab(bool active)
        {
            gaugeVp.RenderTargetUpdateMode = active ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
            if (!active) return;
            var T = sc.Train; var P = T.Phys; float L = P.BrakeLevel;
            float spd = T.Speed * 3.6f, lim = sc.LimitKmh(T.FrontSeg.Edge);
            if (cabDigi != null) cabDigi.Text = Mathf.RoundToInt(spd).ToString("00");
            gauge.Speed = spd; gauge.Limit = lim; gauge.Over = spd > lim + 3f; gauge.Emergency = P.Emergency;
            gauge.Bp = L <= .6f ? 5f - 2.5f * L : 3.5f - (L - .6f) / .4f * 3.5f; gauge.Bc = L <= .6f ? L / .6f * 3.8f : 3.8f + (L - .6f) / .4f * 1.2f;
            gauge.Notch = touch.Notch; gauge.Amps = P.Throttle * 900f * Mathf.Clamp(1.2f - spd / 140f, .15f, 1f); gauge.Volts = (touch.PantoUp && touch.Breaker) ? 25000f : 0f;
            gauge.Panto = touch.PantoUp; gauge.Breaker = touch.Breaker; gauge.Diesel = touch.Diesel; gauge.Reverser = touch.Reverser == 1 ? "FWD" : (touch.Reverser == 0 ? "NEU" : "REV");
            leverPower.Rotation = new Vector3(-(.15f + touch.Notch / 8f * .9f), 0f, 0f);
            leverBrake.Rotation = new Vector3(-(.15f + touch.BrakeStep / 6f * .9f), 0f, 0f);
            leverRev.Rotation = new Vector3(-touch.Reverser * .5f, 0f, 0f);
            matBtn1.Emission = touch.PantoUp ? new Color(.2f, 1f, .3f) : new Color(.05f, .12f, .05f); matBtn1.AlbedoColor = matBtn1.Emission;
            bool b = touch.Breaker && !touch.Diesel; matBtn2.Emission = b ? new Color(.2f, 1f, .3f) : new Color(.05f, .12f, .05f); matBtn2.AlbedoColor = matBtn2.Emission;
        }
    }
}
