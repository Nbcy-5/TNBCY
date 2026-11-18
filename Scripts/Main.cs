using Godot;
using System.Collections.Generic;
using System.Text.Json;

namespace TNBCY
{
    // The whole game screen: builds the world from the track graph, the train, and the touch HUD.
    public partial class Main : Node3D
    {
        Scenario sc; Camera3D cam; bool camReady;
        readonly List<Node3D> cars = new List<Node3D>();
        readonly Dictionary<TSignal, StandardMaterial3D> sigMats = new Dictionary<TSignal, StandardMaterial3D>();
        readonly Dictionary<TNode, Label3D> swTags = new Dictionary<TNode, Label3D>();
        readonly List<Vector3> trackPts = new List<Vector3>();
        AudioStreamPlayer horn;
        Label speedL, nextL, scoreL, infoL, toastL, endL, updL;
        TouchControls touch; Control endPanel, updPanel; float toastT; string apkUrl = "";

        static T LoadJson<T>(string path)
        {
            using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            return JsonSerializer.Deserialize<T>(f.GetAsText(), new JsonSerializerOptions { IncludeFields = true, PropertyNameCaseInsensitive = true });
        }
        static StandardMaterial3D Mat(Color c) => new StandardMaterial3D { AlbedoColor = c, Roughness = 0.9f };
        static Transform3D Xf(Basis b, Vector3 p, Vector3 s) => new Transform3D(new Basis(b.X * s.X, b.Y * s.Y, b.Z * s.Z), p);
        static Vector3 Right(Vector3 d) => new Vector3(-d.Z, 0f, d.X);

        MeshInstance3D Box(Node parent, Vector3 pos, Basis rot, Vector3 size, Material m)
        {
            var mi = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = m };
            parent.AddChild(mi); mi.Transform = new Transform3D(rot, pos); return mi;
        }
        MultiMesh MM(Mesh mesh, Material m, int n)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = n };
            AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = m }); return mm;
        }
        MultiMesh MMBox(Material m, int n) => MM(new BoxMesh { Size = Vector3.One }, m, n);
        Label3D Tag(string text, Vector3 pos, Color c)
        {
            var l = new Label3D { Text = text, FontSize = 64, PixelSize = 0.02f, OutlineSize = 12, Modulate = c, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true };
            AddChild(l); l.Position = pos; return l;
        }
        void Toast(string m, bool good) { toastL.Text = m; toastL.Modulate = good ? new Color(.5f, 1f, .6f) : new Color(1f, .5f, .4f); toastT = 3f; }

        public override void _Ready()
        {
            Updater.LoadSavedPack();                       // content downloaded by the updater overrides built-in data
            DisplayServer.ScreenSetKeepOn(true);
            GetViewport().Msaa3D = Viewport.Msaa.Msaa2X;
            var net = LoadJson<NetworkSpec>("res://Data/network_demo.json");
            var consist = LoadJson<ConsistSpec>("res://Data/consist_demo.json");
            BuildHud();
            sc = new Scenario(net, consist) { OnEvent = (m, k) => { Toast(m, k == "ok"); RefreshSwitchTags(); } };

            var sky = new Sky { SkyMaterial = new ProceduralSkyMaterial {
                SkyTopColor = new Color(.22f, .45f, .8f), SkyHorizonColor = new Color(.82f, .85f, .88f),
                GroundHorizonColor = new Color(.7f, .75f, .72f), GroundBottomColor = new Color(.3f, .4f, .3f) } };
            var env = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky, Sky = sky,
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(.62f, .64f, .7f),
                FogEnabled = true, FogLightColor = new Color(.78f, .84f, .9f), FogDensity = 0.0006f
            };
            AddChild(new WorldEnvironment { Environment = env });
            AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-48, -35, 0), ShadowEnabled = true, DirectionalShadowMaxDistance = 140f });
            cam = new Camera3D { Near = .3f, Far = 3000f, Fov = 65f }; AddChild(cam); cam.MakeCurrent();

            BuildTrack(); BuildTrees(); BuildStations(); BuildSignals(); BuildSwitchTags(); BuildTrain(consist); BuildHorn();
            AddChild(new Updater { Message = m => Toast(m, true), ApkAvailable = OnApk });
            Toast("Drive to Seacliff. Tap Switch to change the points ahead.", true);
        }

        void OnApk(string notes, string url) { apkUrl = url; updL.Text = "New version available\n" + notes; updPanel.Visible = true; touch.Enabled = false; }

        // ---------------------------------------------------------------- world
        void BuildTrack()
        {
            int total = 0; foreach (var e in sc.Graph.Edges.Values) total += Mathf.CeilToInt(e.Length / 5f);
            var bal = MMBox(Mat(new Color(.38f, .36f, .34f)), total);
            var slp = MMBox(Mat(new Color(.3f, .22f, .16f)), total * 2);
            var rail = MMBox(Mat(new Color(.7f, .7f, .72f)), total * 2);
            int idx = 0; var min = new Vector3(1e9f, 0, 1e9f); var max = new Vector3(-1e9f, 0, -1e9f);
            foreach (var e in sc.Graph.Edges.Values)
            {
                int np = Mathf.CeilToInt(e.Length / 5f); float step = e.Length / np;
                for (int k = 0; k < np; k++, idx++)
                {
                    float s = (k + .5f) * step; var p = e.PosAt(s); var q = Basis.LookingAt(e.DirAt(s), Vector3.Up);
                    min = min.Min(p); max = max.Max(p); if (k % 2 == 0) trackPts.Add(p);
                    bal.SetInstanceTransform(idx, Xf(q, p + q * new Vector3(0, .02f, 0), new Vector3(3.4f, .2f, step + .1f)));
                    slp.SetInstanceTransform(idx * 2, Xf(q, p + q * new Vector3(0, .16f, -step * .25f), new Vector3(2.7f, .12f, .4f)));
                    slp.SetInstanceTransform(idx * 2 + 1, Xf(q, p + q * new Vector3(0, .16f, step * .25f), new Vector3(2.7f, .12f, .4f)));
                    rail.SetInstanceTransform(idx * 2, Xf(q, p + q * new Vector3(-.72f, .27f, 0), new Vector3(.1f, .16f, step + .1f)));
                    rail.SetInstanceTransform(idx * 2 + 1, Xf(q, p + q * new Vector3(.72f, .27f, 0), new Vector3(.1f, .16f, step + .1f)));
                }
            }
            var c = (min + max) * .5f;
            Box(this, new Vector3(c.X, -.55f, c.Z), Basis.Identity, new Vector3(8000, 1, 8000), Mat(new Color(.28f, .5f, .25f)));
            treeBounds = new Rect2(min.X - 300f, min.Z - 200f, max.X - min.X + 600f, max.Z - min.Z + 400f);
        }
        Rect2 treeBounds;

        void BuildTrees()
        {
            const int n = 500; var rng = new RandomNumberGenerator { Seed = 7 };
            var trunk = MM(new CylinderMesh { TopRadius = .25f, BottomRadius = .35f, Height = 2f, RadialSegments = 6, Rings = 1 }, Mat(new Color(.35f, .24f, .15f)), n);
            var crown = MM(new CylinderMesh { TopRadius = 0f, BottomRadius = 1.8f, Height = 5f, RadialSegments = 7, Rings = 1 }, Mat(new Color(.16f, .4f, .2f)), n);
            for (int i = 0; i < n; i++)
            {
                float x = rng.RandfRange(treeBounds.Position.X, treeBounds.End.X), z = rng.RandfRange(treeBounds.Position.Y, treeBounds.End.Y);
                float s = rng.RandfRange(.8f, 1.7f);
                foreach (var tp in trackPts) { float dx = tp.X - x, dz = tp.Z - z; if (dx * dx + dz * dz < 18f * 18f) { s = .001f; break; } }
                var sc3 = Vector3.One * s;
                trunk.SetInstanceTransform(i, new Transform3D(Basis.FromScale(sc3), new Vector3(x, 1f * s, z)));
                crown.SetInstanceTransform(i, new Transform3D(Basis.FromScale(sc3), new Vector3(x, 4.5f * s, z)));
            }
        }

        void BuildStations()
        {
            var plat = Mat(new Color(.75f, .72f, .65f)); var pole = Mat(new Color(.2f, .2f, .2f)); var roof = Mat(new Color(.42f, .18f, .16f));
            foreach (var st in sc.Graph.Stations)
            {
                float s = Mathf.Max(0f, st.Offset - 10f); var p = st.Edge.PosAt(s); var d = st.Edge.DirAt(s); var q = Basis.LookingAt(d, Vector3.Up); var r = Right(d);
                Box(this, p + r * 3.4f + Vector3.Up * .3f, q, new Vector3(3.2f, .9f, 110f), plat);
                Box(this, p + r * 3.6f + Vector3.Up * 4.2f, q, new Vector3(3.8f, .25f, 70f), roof);
                foreach (float z in new[] { -30f, 0f, 30f }) Box(this, p + r * 4.8f + d * z + Vector3.Up * 2.3f, q, new Vector3(.2f, 3.6f, .2f), pole);
                Tag(st.Name, p + r * 3.4f + Vector3.Up * 7f, Colors.Yellow);
            }
        }

        void BuildSignals()
        {
            var pole = Mat(new Color(.2f, .2f, .2f));
            foreach (var s in sc.Graph.Signals)
            {
                var p = s.Edge.PosAt(s.Offset); var d = s.Edge.DirAt(s.Offset); var q = Basis.LookingAt(d, Vector3.Up); var bp = p + Right(d) * 3.2f;
                Box(this, bp + Vector3.Up * 2.5f, q, new Vector3(.25f, 5f, .25f), pole);
                var m = Mat(Colors.Green); m.EmissionEnabled = true; sigMats[s] = m;
                Box(this, bp + Vector3.Up * 5.3f, q, new Vector3(.9f, .9f, .5f), m);
                Tag(s.Id, bp + Vector3.Up * 7.2f, Colors.White);
            }
        }

        void BuildSwitchTags()
        {
            foreach (var n in sc.Graph.Switches) swTags[n] = Tag("", n.Pos + new Vector3(0f, 6f, 0f), Colors.Yellow);
            RefreshSwitchTags();
        }
        void RefreshSwitchTags()
        {
            foreach (var kv in swTags) kv.Value.Text = kv.Key.Name + ": " + (kv.Key.Sel == 0 ? "STRAIGHT" : "DIVERGE");
        }

        void BuildTrain(ConsistSpec consist)
        {
            foreach (var c in sc.Train.Cars)
            {
                var v = c.Spec; float L = c.Length;
                var col = new Color(((v.colorRgb >> 16) & 255) / 255f, ((v.colorRgb >> 8) & 255) / 255f, (v.colorRgb & 255) / 255f);
                var g = Box(this, Vector3.Zero, Basis.Identity, new Vector3(2.9f, 3.4f, L - .6f), Mat(col));
                Box(g, new Vector3(0, .3f, 0), Basis.Identity, new Vector3(2.95f, .5f, (L - .6f) * .8f), Mat(new Color(.95f, .9f, .6f)));
                Box(g, new Vector3(0, 1.75f, 0), Basis.Identity, new Vector3(2.7f, .25f, L - 1f), Mat(col.Darkened(.3f)));
                Box(g, new Vector3(0, -1.55f, 0), Basis.Identity, new Vector3(2.5f, .7f, L - 2f), Mat(new Color(.12f, .12f, .14f)));
                cars.Add(g);
            }
        }

        void BuildHorn()
        {
            int rate = 22050, n = rate * 7 / 10; var d = new byte[n * 2];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate, e = Mathf.Min(1f, t * 20f) * Mathf.Min(1f, (0.7f - t) * 10f);
                float s = e * .25f * (Mathf.Sign(Mathf.Sin(6.2832f * 220f * t)) + Mathf.Sign(Mathf.Sin(6.2832f * 277f * t))) * .5f;
                short v = (short)(s * 32767f); d[i * 2] = (byte)(v & 0xff); d[i * 2 + 1] = (byte)((v >> 8) & 0xff);
            }
            horn = new AudioStreamPlayer { Stream = new AudioStreamWav { Data = d, Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = false } };
            AddChild(horn); touch.OnHorn = () => horn.Play();
        }

        // ------------------------------------------------------------------ HUD
        static void Place(Control c, float al, float at, float ar, float ab, float ol, float ot, float orr, float ob)
        { c.AnchorLeft = al; c.AnchorTop = at; c.AnchorRight = ar; c.AnchorBottom = ab; c.OffsetLeft = ol; c.OffsetTop = ot; c.OffsetRight = orr; c.OffsetBottom = ob; }
        static StyleBoxFlat Flat(Color c, int r = 8) { var s = new StyleBoxFlat { BgColor = c }; s.SetCornerRadiusAll(r); return s; }

        Label Lbl(string t, int size, HorizontalAlignment a = HorizontalAlignment.Left)
        {
            var l = new Label { Text = t, HorizontalAlignment = a };
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", Colors.White);
            l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, .75f));
            l.AddThemeConstantOverride("outline_size", 8);
            return l;
        }
        Button Btn(string t, Color c)
        {
            var b = new Button { Text = t, CustomMinimumSize = new Vector2(180, 64), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            b.AddThemeFontSizeOverride("font_size", 26);
            b.AddThemeStyleboxOverride("normal", Flat(c)); b.AddThemeStyleboxOverride("hover", Flat(c.Lightened(.1f))); b.AddThemeStyleboxOverride("pressed", Flat(c.Lightened(.25f)));
            return b;
        }
        PanelContainer Panel(out VBoxContainer vb)
        {
            var p = new PanelContainer { Visible = false, CustomMinimumSize = new Vector2(480, 220) };
            p.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center); p.GrowHorizontal = Control.GrowDirection.Both; p.GrowVertical = Control.GrowDirection.Both;
            vb = new VBoxContainer(); p.AddChild(vb); return p;
        }

        void BuildHud()
        {
            var layer = new CanvasLayer(); AddChild(layer);
            var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore }; root.SetAnchorsPreset(Control.LayoutPreset.FullRect); layer.AddChild(root);

            speedL = Lbl("0 km/h", 40, HorizontalAlignment.Center); Place(speedL, .5f, 0, .5f, 0, -300, 6, 300, 60); root.AddChild(speedL);
            infoL = Lbl("", 22, HorizontalAlignment.Center); Place(infoL, .5f, 0, .5f, 0, -320, 62, 320, 110); root.AddChild(infoL);
            nextL = Lbl("", 22); Place(nextL, 0, 0, 0, 0, 20, 8, 420, 110); root.AddChild(nextL);
            scoreL = Lbl("Score 0", 34, HorizontalAlignment.Right); Place(scoreL, 1, 0, 1, 0, -300, 8, -20, 60); root.AddChild(scoreL);
            toastL = Lbl("", 32, HorizontalAlignment.Center); Place(toastL, .5f, .3f, .5f, .3f, -520, 0, 520, 60); root.AddChild(toastL);

            touch = new TouchControls(); touch.SetAnchorsPreset(Control.LayoutPreset.FullRect); root.AddChild(touch);
            touch.OnSwitch = () => sc.ToggleSwitch();

            endPanel = Panel(out var vb1);
            endL = Lbl("", 34, HorizontalAlignment.Center); vb1.AddChild(endL);
            var again = Btn("Drive again", new Color(.94f, .63f, .19f)); vb1.AddChild(again);
            again.Pressed += () => GetTree().ReloadCurrentScene();
            root.AddChild(endPanel);

            updPanel = Panel(out var vb2);
            updL = Lbl("", 28, HorizontalAlignment.Center); vb2.AddChild(updL);
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12); vb2.AddChild(row);
            var dl = Btn("Download", new Color(.94f, .63f, .19f)); var later = Btn("Later", new Color(.24f, .36f, .44f)); row.AddChild(dl); row.AddChild(later);
            dl.Pressed += () => OS.ShellOpen(apkUrl);
            later.Pressed += () => { updPanel.Visible = false; touch.Enabled = true; };
            root.AddChild(updPanel);
        }

        string NextText()
        {
            var T = sc.Train; var e = T.FrontSeg.Edge; float acc = -T.FrontOffset, best = 1e9f; string txt = "";
            for (int hop = 0; hop < 4 && e != null; hop++)
            {
                foreach (var sg in sc.Graph.Signals)
                {
                    if (sg.Edge != e || sg.Passed) continue;
                    float d = acc + sg.Offset;
                    if (d >= 0f && d < best) { best = d; txt = "Signal " + sg.Id + (sg.Red ? " RED" : " clear"); }
                }
                foreach (var stn in sc.Graph.Stations)
                {
                    if (stn.Edge != e || stn.Done) continue;
                    float d = acc + stn.Offset;
                    if (d >= -40f && d < best) { best = Mathf.Max(d, 0f); txt = "Station " + stn.Name; }
                }
                var n = e.To; float dn = acc + e.Length;
                if (n.IsSwitch && dn < best) { best = dn; txt = "Switch " + n.Name + " set " + (n.Sel == 0 ? "straight" : "diverging"); }
                acc += e.Length; e = n.Outs.Count > 0 ? n.Outs[n.Sel] : null;
            }
            return best > 1e8f ? "Next: end of line" : "Next: " + txt + " in " + Mathf.RoundToInt(best) + " m";
        }

        // ----------------------------------------------------------- simulation
        public override void _Process(double delta)
        {
            if (sc == null) return;
            float dt = Mathf.Min((float)delta, .05f);
            var T = sc.Train; var P = T.Phys;
            P.Throttle = Mathf.MoveToward(P.Throttle, touch.Throttle, .9f * dt);      // smooth lever response
            P.BrakeHandle = Mathf.MoveToward(P.BrakeHandle, touch.Brake, 1.8f * dt);
            if (!touch.Emergency && P.Emergency && P.Speed > .1f) { touch.Emergency = true; Toast("Stop before releasing the emergency brake", false); }
            P.Emergency = touch.Emergency;
            sc.Update(dt); toastT -= dt; if (toastT <= 0f) toastL.Text = "";
            bool cab = touch.Cab;

            for (int i = 0; i < cars.Count; i++)
            {
                var c = T.Cars[i]; T.PointAt(T.Odo - c.Offset, out var pf, out var df); T.PointAt(T.Odo - c.Offset - c.Length, out var pr, out _);
                var fwd = pf - pr; fwd.Y = 0f; fwd = fwd.LengthSquared() < .0001f ? df : fwd.Normalized();
                cars[i].Transform = new Transform3D(new Basis(Vector3.Up, Mathf.Atan2(fwd.X, fwd.Z)), (pf + pr) * .5f + Vector3.Up * 2f);
            }
            cars[0].Visible = !cab;
            foreach (var kv in sigMats) { var col = kv.Key.Red ? Colors.Red : new Color(.2f, 1f, .3f); kv.Value.AlbedoColor = col; kv.Value.Emission = col; }

            T.PointAt(T.Odo, out var p, out var d);
            if (cab) cam.LookAtFromPosition(p - d * 6f + Vector3.Up * 4.4f, p + d * 120f + Vector3.Up * 2f, Vector3.Up);
            else
            {
                Vector3 want = p - d * 45f + Right(d) * 2.5f + Vector3.Up * 9f;
                cam.GlobalPosition = camReady ? cam.GlobalPosition.Lerp(want, Mathf.Min(1f, dt * 6f)) : want; camReady = true;
                cam.LookAt(p + d * 25f + Vector3.Up * 2f, Vector3.Up);
            }

            float lim = sc.LimitKmh(T.FrontSeg.Edge), spd = T.Speed * 3.6f;
            speedL.Text = Mathf.RoundToInt(spd) + " km/h  (limit " + Mathf.RoundToInt(lim) + ")";
            speedL.AddThemeColorOverride("font_color", spd > lim + 3f ? new Color(1f, .4f, .35f) : Colors.White);
            scoreL.Text = "Score " + Mathf.RoundToInt(sc.Score);
            nextL.Text = NextText();
            infoL.Text = "Brake " + Mathf.RoundToInt(P.BrakeLevel * 100f) + "%   Stopping distance " + Mathf.RoundToInt(P.BrakingDistance(0f)) + " m";
            var sw = sc.NextSwitch(out float sd);
            touch.SwitchLabel = sw == null ? "No switch" : sw.Name + (sw.Sel == 0 ? " straight" : " diverge");
            if (sc.Ended && !endPanel.Visible) { endL.Text = sc.EndMessage + "\nFinal score: " + Mathf.RoundToInt(sc.Score); endPanel.Visible = true; touch.Enabled = false; }
        }
    }
}
