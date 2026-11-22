using Godot;
using System.Collections.Generic;
using System.Text.Json;

namespace TNBCY
{
    // Game screen: train menu, loading, driver controls, HUD and the frame loop. World / train / camera code is in the other Main*.cs files.
    public partial class Main : Node3D
    {
        public const string Version = "v17";
        public class TrainOption { public string name = ""; public string file = ""; public string desc = ""; }
        public class TrainList { public List<TrainOption> trains = new List<TrainOption>(); }
        static readonly string[] QualityNames = { "Fast", "Balanced", "High", "Ultra" };
        static readonly string[] GfxNames = { "Auto", "Fast", "Balanced", "High", "Ultra" };

        Control menuPanel; Label loadL, fpsL; Button gfxBtn; int gfxChoice;
        Scenario sc; Camera3D cam; TrainVis playerVis, aiVis;
        readonly Dictionary<TSignal, StandardMaterial3D[]> sigMats = new Dictionary<TSignal, StandardMaterial3D[]>();
        readonly Dictionary<TNode, Label3D> swTags = new Dictionary<TNode, Label3D>();
        readonly List<Vector3> trackPts = new List<Vector3>();
        AudioStreamPlayer horn;
        TrainSounds sounds;   // speed-driven train sounds, set up in Data/train_sounds.json
        Label speedL, nextL, infoL, toastL, endL, updL, clockL, aiL;
        TouchControls touch; Control endPanel, updPanel; float toastT, noteT, startT, frameAcc, fpsT; int frameN, stableUp, lastRev; string apkUrl = "";
        bool electric; float pantK;

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
        MultiMesh MM(Mesh mesh, Material m, int n, float visRange = 0f, bool colors = false)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = colors, Mesh = mesh, InstanceCount = n };
            var inst = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = m };
            AddChild(inst); if (visRange > 0f) Far(inst, visRange); return mm;
        }
        MultiMesh MMBox(Material m, int n, float visRange = 0f) => MM(new BoxMesh { Size = Vector3.One }, m, n, visRange);
        Label3D Tag(string text, Vector3 pos, Color c)
        {
            var l = new Label3D { Text = text, FontSize = 64, PixelSize = 0.02f, OutlineSize = 12, Modulate = c, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true };
            AddChild(l); l.Position = pos; return l;
        }
        void Toast(string m, bool good, float secs = 3f) { toastL.Text = m; toastL.Modulate = good ? new Color(.5f, 1f, .6f) : new Color(1f, .5f, .4f); toastT = secs; }
        void Note(string m) { if (noteT <= 0f) { Toast(m, false); noteT = 2f; } }
        static string AspectName(int a) => a == 2 ? " RED" : (a == 1 ? " YELLOW" : " GREEN");

        public override void _Ready()
        {
            Updater.LoadSavedPack();                       // content downloaded by the updater overrides built-in data
            DisplayServer.ScreenSetKeepOn(true);
            DisplayServer.ScreenSetOrientation(DisplayServer.ScreenOrientation.SensorLandscape);    // flip the phone either way
            ProcTex.Prefetch();                            // textures are made on worker threads while the menu is open
            GetViewport().Msaa3D = Viewport.Msaa.Disabled;
            BuildHud();
            AddChild(new Updater { Message = m => Toast(m, true), ApkAvailable = OnApk });
        }

        async void StartGame(string consistFile)
        {
            try
            {
                menuPanel.Visible = false; loadL.Text = "Loading..."; loadL.Visible = true;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var net = LoadJson<NetworkSpec>("res://Data/network_demo.json");
                var consist = LoadJson<ConsistSpec>("res://Data/" + consistFile + ".json");
                ConsistSpec aiConsist = null;
                try { aiConsist = LoadJson<ConsistSpec>("res://Data/consist_ai.json"); } catch { }
                sc = new Scenario(net, consist, aiConsist) { OnEvent = (m, k) => { Toast(m, k == "ok"); RefreshSwitchTags(); } };

                BuildSky();
                cam = new Camera3D { Near = .12f, Far = 3500f, Fov = 72f }; AddChild(cam); cam.MakeCurrent();
                matGravel = TexMat(ProcTex.Gravel()); matConcrete = TexMat(ProcTex.Concrete());
                matConcrete.VertexColorUseAsAlbedo = true;   // sleepers get a slightly different shade each
                matSteel = Solid(new Color(.34f, .27f, .23f), .55f, .45f);
                matRail = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Metallic = .45f, Roughness = .38f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
                matFit = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Metallic = .3f, Roughness = .6f, CullMode = BaseMaterial3D.CullModeEnum.Disabled }; matWire = Solid(new Color(.18f, .18f, .2f), .4f, .5f); matMast = Solid(new Color(.55f, .57f, .6f), .3f, .6f);
                BuildTrack(); BuildScenery(); BuildStations(); BuildSignals(); BuildIndianRailwayObjects(); BuildWaterPipes(); BuildSwitchTags();
                playerVis = BuildTrain(sc.Train, null, null);
                if (sc.AiTrain != null) aiVis = BuildTrain(sc.AiTrain, new Color(.12f, .32f, .78f), new Color(.82f, .85f, .9f));
                BuildCab(); BuildHorn();
                if (sounds == null) { sounds = new TrainSounds(); AddChild(sounds); }

                electric = false;
                foreach (var c in sc.Train.Cars) { string nm = (c.Spec.name ?? "").ToLowerInvariant(); if (c.Spec.powered && !nm.Contains("demu") && !nm.Contains("diesel")) electric = true; }
                touch.Diesel = !electric; touch.Notch = 0; touch.BrakeStep = 1; touch.Reverser = 0; touch.PantoUp = false; touch.Breaker = false; lastRev = 0; pantK = 0f;
                ApplyQuality(gfxChoice == 0 ? 1 : gfxChoice - 1); startT = 0f;
                loadL.Visible = false; touch.Enabled = true;
                Toast(electric ? "Raise PANTO, close VCB, set reverser FWD, release the brake, then power up" : "Start ENGINE, set reverser FWD, release the brake, then power up", true, 7f);
            }
            catch (System.Exception ex) { GD.PrintErr(ex.ToString()); if (loadL != null) { loadL.Text = "Error: " + ex.Message; loadL.Visible = true; } }
        }

        void OnApk(string notes, string url) { apkUrl = url; updL.Text = "New version available\n" + notes; updPanel.Visible = true; touch.Enabled = false; }

        // ------------------------------------------------------------------ HUD
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

            fadeRect = new ColorRect { Color = new Color(0, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false }; fadeRect.SetAnchorsPreset(Control.LayoutPreset.FullRect); root.AddChild(fadeRect);
            speedL = Lbl("0 km/h", 40, HorizontalAlignment.Center); Place(speedL, .5f, 0, .5f, 0, -300, 6, 300, 60); root.AddChild(speedL);
            infoL = Lbl("", 22, HorizontalAlignment.Center); Place(infoL, .5f, 0, .5f, 0, -460, 62, 460, 110); root.AddChild(infoL);
            nextL = Lbl("", 22); Place(nextL, 0, 0, 0, 0, 20, 8, 470, 110); root.AddChild(nextL);
            clockL = Lbl("", 22, HorizontalAlignment.Right); Place(clockL, 1, 0, 1, 0, -420, 58, -20, 90); root.AddChild(clockL);
            aiL = Lbl("", 22, HorizontalAlignment.Right); Place(aiL, 1, 0, 1, 0, -420, 88, -20, 120); root.AddChild(aiL);
            fpsL = Lbl("", 18, HorizontalAlignment.Right); Place(fpsL, 1, 0, 1, 0, -420, 8, -20, 36); root.AddChild(fpsL);
            toastL = Lbl("", 32, HorizontalAlignment.Center); Place(toastL, .5f, .3f, .5f, .3f, -620, 0, 620, 60); root.AddChild(toastL);
            loadL = Lbl("", 40, HorizontalAlignment.Center); Place(loadL, 0, .5f, 1, .5f, 0, -30, 0, 30); loadL.Visible = false; root.AddChild(loadL);

            touch = new TouchControls(); touch.SetAnchorsPreset(Control.LayoutPreset.FullRect); root.AddChild(touch);
            touch.OnSwitch = () => sc.ToggleSwitch();
            touch.OnPantoToggle = TogglePanto; touch.OnBreakerToggle = ToggleBreaker;
            touch.OnPrevCoach = () => wantFocus = Mathf.Max(0, focusCar - 1);
            touch.OnNextCoach = () => wantFocus = Mathf.Min(sc.Train.Cars.Count - 1, focusCar + 1);

            BuildMenu(root);
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

        void BuildMenu(Control root)
        {
            var bg = new ColorRect { Color = new Color(.06f, .10f, .14f, 1f) }; bg.SetAnchorsPreset(Control.LayoutPreset.FullRect); root.AddChild(bg); menuPanel = bg;
            var cc = new CenterContainer(); cc.SetAnchorsPreset(Control.LayoutPreset.FullRect); bg.AddChild(cc);
            var vb = new VBoxContainer(); vb.AddThemeConstantOverride("separation", 8); cc.AddChild(vb);
            vb.AddChild(Lbl("TNBCY  " + Version, 46, HorizontalAlignment.Center));
            vb.AddChild(Lbl("Secunderabad Jn to Ghatkesar (20 km) - choose your train", 24, HorizontalAlignment.Center));
            TrainList list; try { list = LoadJson<TrainList>("res://Data/trains.json"); } catch { list = new TrainList(); }
            if (list.trains.Count == 0) list.trains.Add(new TrainOption { name = "Demo train", file = "consist_demo", desc = "" });
            foreach (var opt in list.trains)
            {
                var o = opt; var b = Btn(o.name + (o.desc.Length > 0 ? "   -   " + o.desc : ""), new Color(.24f, .36f, .44f));
                b.CustomMinimumSize = new Vector2(760, 50); b.AddThemeFontSizeOverride("font_size", 24); vb.AddChild(b);
                b.Pressed += () => StartGame(o.file);
            }
            gfxBtn = Btn("Graphics: Auto (adjusts itself to keep the game smooth)", new Color(.16f, .26f, .2f));
            gfxBtn.CustomMinimumSize = new Vector2(760, 46); gfxBtn.AddThemeFontSizeOverride("font_size", 22); vb.AddChild(gfxBtn);
            gfxBtn.Pressed += () => { gfxChoice = (gfxChoice + 1) % 5; gfxBtn.Text = "Graphics: " + GfxNames[gfxChoice] + (gfxChoice == 0 ? " (adjusts itself to keep the game smooth)" : ""); };
            touch.Enabled = false;
        }

        string NextText()
        {
            var T = sc.Train; var e = T.FrontSeg.Edge; float acc = -T.FrontOffset, best = 1e9f; string txt = "";
            for (int hop = 0; hop < 5 && e != null; hop++)
            {
                if (sc.Graph.SigByEdge.TryGetValue(e, out var list))
                    foreach (var sg in list)
                    {
                        if (sg.Passed) continue;
                        float d = acc + sg.Offset;
                        if (d >= 0f && d < best) { best = d; txt = "Signal " + sg.Id + AspectName(sg.Aspect); }
                    }
                foreach (var stn in sc.Graph.Stations)
                {
                    if (stn.Edge != e || stn.Done || !stn.Stop) continue;
                    float d = acc + stn.Offset;
                    if (d >= -40f && d < best) { best = Mathf.Max(d, 0f); txt = "Station " + stn.Name; }
                }
                var n = e.To; float dn = acc + e.Length;
                if (n.IsSwitch && dn < best) { best = dn; txt = "Switch " + n.Name + " set " + (n.Sel == 0 ? "main" : "loop"); }
                acc += e.Length; e = n.Outs.Count > 0 ? n.Outs[n.Sel] : null;
            }
            return best > 1e8f ? "Next: end of line" : "Next: " + txt + " in " + Mathf.RoundToInt(best) + " m";
        }

        // -------------------------------------------------------------- cameras
        // ------------------------------------------------------------ driver controls
        void TogglePanto()
        {
            if (touch.Diesel) { touch.PantoUp = !touch.PantoUp; if (!touch.PantoUp) touch.Notch = 0; Toast(touch.PantoUp ? "Engine running" : "Engine stopped", touch.PantoUp); return; }
            if (!touch.PantoUp) { touch.PantoUp = true; Toast("Pantograph raised", true); }
            else { touch.PantoUp = false; touch.Breaker = false; touch.Notch = 0; Toast("Pantograph lowered - breaker opened", false); }
        }

        void ToggleBreaker()
        {
            if (!touch.PantoUp) { Toast("Raise the pantograph first", false); return; }
            if (touch.Breaker) { touch.Breaker = false; touch.Notch = 0; Toast("Breaker (VCB) open", false); }
            else if (touch.Notch > 0) Toast("Set power to 0 before closing the breaker", false);
            else { touch.Breaker = true; Toast("Breaker (VCB) closed - 25 kV on", true); }
        }

        void ApplyControls(float dt)
        {
            var T = sc.Train; var P = T.Phys;
            bool power = electric ? (touch.PantoUp && touch.Breaker) : touch.PantoUp;
            if (touch.Reverser != lastRev)
            {
                if (touch.Reverser == -1) { touch.Reverser = lastRev; Note("Reverse running is not available yet"); }
                else if (T.Speed > .5f || touch.Notch > 0) { touch.Reverser = lastRev; Note("Stop and set power to 0 to change direction"); }
                else lastRev = touch.Reverser;
            }
            if (touch.Notch > 0)
            {
                string why = null;
                if (!power) why = electric ? "No power: raise the pantograph and close the VCB" : "Start the engine first";
                else if (touch.Reverser != 1) why = "Set the reverser to FWD";
                else if (touch.BrakeStep >= 2) why = "Release the train brake before applying power";
                if (why != null) { touch.Notch = 0; Note(why); }
            }
            if (touch.Emergency && touch.Notch > 0) touch.Notch = 0;
            if (!touch.Emergency && P.Emergency && P.Speed > .1f) { touch.ForceEmergency(); Note("Stop before releasing the emergency brake"); }
            P.Emergency = touch.Emergency;
            P.BrakeHandle = Mathf.MoveToward(P.BrakeHandle, touch.BrakeValue, 1.8f * dt);
            float tgt = (power && touch.Reverser == 1) ? touch.Throttle : 0f;
            P.Throttle = Mathf.MoveToward(P.Throttle, tgt, (tgt > P.Throttle ? .4f : 1.2f) * dt);      // traction builds up and drops off gradually
            pantK = Mathf.MoveToward(pantK, touch.PantoUp ? 1f : 0f, dt / 2f);
        }

        // keeps the frame rate smooth: lowers resolution, shadows and draw distance when the phone struggles
        void AutoQuality()
        {
            float avg = frameAcc / Mathf.Max(1, frameN); fpsL.Text = Mathf.RoundToInt(1f / Mathf.Max(avg, .001f)) + " fps  " + QualityNames[quality];
            if (sc != null && startT > 8f && gfxChoice == 0)
            {
                if (avg > .030f && quality > 0) { ApplyQuality(quality - 1); stableUp = 0; Toast("Graphics set to " + QualityNames[quality] + " to keep it smooth", true); }
                else if (avg < .0165f && quality < 2) { if (++stableUp >= 4) { ApplyQuality(quality + 1); stableUp = 0; } }
                else stableUp = 0;
            }
            frameAcc = 0f; frameN = 0; fpsT = 0f;
        }

        // ----------------------------------------------------------- simulation
        public override void _Process(double delta)
        {
            float rd = (float)delta; frameAcc += rd; frameN++; fpsT += rd; if (fpsT >= 2f) AutoQuality();
            if (sc == null || cam == null) return;
            float dt = Mathf.Min(rd, .05f); startT += dt; noteT -= dt;
            var T = sc.Train; var P = T.Phys;
            ApplyControls(dt);
            sc.Update(dt); toastT -= dt; if (toastT <= 0f) toastL.Text = "";

            UpdateTrain(playerVis, activeMode == 0, pantK);
            if (aiVis != null) UpdateTrain(aiVis, false, 1f);
            var cols = new[] { Colors.Red, new Color(1f, .8f, .1f), new Color(.2f, 1f, .3f) };
            foreach (var kv in sigMats)
            {
                int active = kv.Key.Aspect == 2 ? 0 : (kv.Key.Aspect == 1 ? 1 : 2);
                for (int i = 0; i < 3; i++)
                {
                    bool on = i == active;
                    kv.Value[i].AlbedoColor = on ? cols[i] : cols[i].Darkened(.8f);
                    kv.Value[i].EmissionEnabled = on; kv.Value[i].Emission = cols[i];
                }
            }
            UpdateCamera(dt);

            float lim = sc.LimitKmh(T.FrontSeg.Edge), spd = T.Speed * 3.6f;
            sounds?.Tick(spd, dt, sc.Ended);
            speedL.Text = Mathf.RoundToInt(spd) + " km/h  (limit " + Mathf.RoundToInt(lim) + ")";
            speedL.AddThemeColorOverride("font_color", spd > lim + 3f ? new Color(1f, .4f, .35f) : Colors.White);
            clockL.Text = sc.ClockText() + "   25 kV OHE";
            nextL.Text = NextText();
            float km = T.Odo / 1000f, tot = sc.Net.routeLengthM / 1000f;
            string rv = touch.Reverser == 1 ? "FWD" : (touch.Reverser == 0 ? "NEU" : "REV");
            infoL.Text = rv + "  N" + touch.Notch + "  Brake " + Mathf.RoundToInt(P.BrakeLevel * 100f) + "%  Stop " + Mathf.RoundToInt(P.BrakingDistance(0f)) + " m  "
                + km.ToString("0.0") + (tot > 0f ? " / " + tot.ToString("0.0") : "") + " km" + (touch.ShowCoachButtons ? "  Coach " + (focusCar + 1) + "/" + T.Cars.Count : "");
            if (sc.AiTrain != null)
            {
                T.PointAt(T.Odo, out var pp, out _); sc.AiTrain.PointAt(sc.AiTrain.Odo, out var pa, out _);
                aiL.Text = "Opposing train: " + sc.Ai.Status + "  " + (pp.DistanceTo(pa) / 1000f).ToString("0.0") + " km";
            }
            var sw = sc.NextSwitch(out float sd);
            touch.SwitchLabel = sw == null ? "none ahead" : sw.Name + (sw.Sel == 0 ? " main" : " loop") + " " + Mathf.RoundToInt(sd) + "m";
            if (sc.Ended && !endPanel.Visible) { endL.Text = sc.EndMessage; endPanel.Visible = true; touch.Enabled = false; }
        }
    }
}
