using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // v20: lineside equipment built from the Signal_signs_etc pack, corrected and fitted to this route.
    // Colour-light signals (hooded lenses that glow, ladder, number plate), kilometre posts, yellow trilingual station name boards,
    // speed boards, T/P boards and gradient posts. Everything faces the oncoming driver, sits on the driver's left and keeps clear of
    // overhead-line masts and platforms. Letters are flat text on the boards (not floating tags), so they hide behind things like real signs.
    public partial class Main
    {
        const bool ShowRegionalScripts = true;     // Telugu + Hindi lines on station boards (needs a phone font with those scripts; set false if you see boxes)

        readonly List<Vector3> fxPlat = new List<Vector3>();   // points along every platform centre line (filled in BuildStations)
        StandardMaterial3D fxVert;                              // vertex-coloured, slightly shiny
        ArrayMesh fxSigMesh, fxKmMesh, fxSignMesh, fxGradMesh, fxArmMesh, fxStationMesh;
        CylinderMesh fxLensMesh;

        void FxInit()
        {
            if (fxVert != null) return;
            fxVert = Solid(Colors.White, .15f, .55f); fxVert.VertexColorUseAsAlbedo = true;
            fxLensMesh = new CylinderMesh { TopRadius = .115f, BottomRadius = .115f, Height = .03f, RadialSegments = 20, Rings = 1 };
            Color conc = new Color(.62f, .61f, .58f), steel = new Color(.50f, .52f, .55f), dark = new Color(.09f, .09f, .10f), white = new Color(.92f, .92f, .88f);
            Color yellow = new Color(.96f, .78f, .08f), black = new Color(.04f, .04f, .045f);

            // ---- colour-light signal head on a tubular post (origin: foot of post, +Z faces the driver)
            fxSigMesh = BuildMesh(st =>
            {
                AddBoxC(st, new Vector3(0f, .2f, -.25f), new Vector3(.6f, .4f, .6f), conc);                  // concrete foundation
                AddBoxC(st, new Vector3(0f, 2.9f, -.25f), new Vector3(.14f, 5.0f, .14f), steel);             // post
                AddBoxC(st, new Vector3(0f, 4.25f, -.17f), new Vector3(.12f, .08f, .22f), steel);            // brackets
                AddBoxC(st, new Vector3(0f, 5.25f, -.17f), new Vector3(.12f, .08f, .22f), steel);
                AddBoxC(st, new Vector3(0f, 4.75f, 0f), new Vector3(.44f, 1.4f, .3f), dark);                 // lamp housing
                AddBoxC(st, new Vector3(0f, 4.75f, .152f), new Vector3(.78f, 1.62f, .02f), white);           // white-edged target plate
                AddBoxC(st, new Vector3(0f, 4.75f, .165f), new Vector3(.72f, 1.56f, .02f), black);
                for (int i = 0; i < 3; i++)
                {
                    float ly = 5.2f - i * .45f;
                    AddBoxC(st, new Vector3(0f, ly, .19f), new Vector3(.3f, .3f, .03f), new Color(.16f, .16f, .17f));   // lens holder
                    AddBoxC(st, new Vector3(0f, ly + .19f, .30f), new Vector3(.36f, .03f, .26f), black);                // hood: roof
                    AddBoxC(st, new Vector3(-.17f, ly + .09f, .31f), new Vector3(.025f, .2f, .24f), black);             // hood: sides
                    AddBoxC(st, new Vector3(.17f, ly + .09f, .31f), new Vector3(.025f, .2f, .24f), black);
                }
                AddBoxC(st, new Vector3(-.2f, 2.5f, -.42f), new Vector3(.04f, 4.2f, .04f), steel);           // maintenance ladder on the back
                AddBoxC(st, new Vector3(.2f, 2.5f, -.42f), new Vector3(.04f, 4.2f, .04f), steel);
                for (int r = 0; r < 11; r++) AddBoxC(st, new Vector3(0f, .6f + r * .36f, -.42f), new Vector3(.44f, .03f, .03f), steel);
                AddBoxC(st, new Vector3(0f, 3.45f, -.165f), new Vector3(.46f, .24f, .02f), white);           // number plate
            });

            // ---- kilometre post
            fxKmMesh = BuildMesh(st =>
            {
                AddBoxC(st, new Vector3(0f, .1f, 0f), new Vector3(.44f, .2f, .34f), conc);
                AddBoxC(st, new Vector3(0f, .7f, 0f), new Vector3(.3f, 1.2f, .2f), new Color(.88f, .87f, .82f));
                AddBoxC(st, new Vector3(0f, 1.27f, 0f), new Vector3(.3f, .16f, .205f), black);
            });

            // ---- yellow board on a post: speed board, T/P board
            fxSignMesh = BuildMesh(st =>
            {
                AddBoxC(st, new Vector3(0f, .12f, 0f), new Vector3(.34f, .24f, .34f), conc);
                AddBoxC(st, new Vector3(0f, 1.2f, 0f), new Vector3(.09f, 2.4f, .09f), steel);
                AddBoxC(st, new Vector3(0f, 2.0f, .06f), new Vector3(.9f, .7f, .04f), black);
                AddBoxC(st, new Vector3(0f, 2.0f, .085f), new Vector3(.84f, .64f, .03f), yellow);
            });

            // ---- gradient post: concrete post with a white plate for the ratio
            fxGradMesh = BuildMesh(st =>
            {
                AddBoxC(st, new Vector3(0f, .1f, 0f), new Vector3(.4f, .2f, .4f), conc);
                AddBoxC(st, new Vector3(0f, .75f, 0f), new Vector3(.16f, 1.3f, .16f), conc);
                AddBoxC(st, new Vector3(0f, .95f, .09f), new Vector3(.56f, .18f, .02f), white);
            });
            fxArmMesh = BuildMesh(st =>
            {
                AddBoxC(st, Vector3.Zero, new Vector3(.58f, .12f, .03f), black);
                AddBoxC(st, new Vector3(0f, 0f, .004f), new Vector3(.52f, .08f, .035f), white);
            });

            // ---- station name board with two posts (origin: centre of the board, +Z toward the track)
            fxStationMesh = BuildMesh(st =>
            {
                AddBoxC(st, new Vector3(0f, 0f, -.01f), new Vector3(6.5f, 1.72f, .10f), black);
                AddBoxC(st, Vector3.Zero, new Vector3(6.38f, 1.6f, .12f), yellow);
                AddBoxC(st, new Vector3(-2.6f, -1.3f, -.02f), new Vector3(.18f, 1.8f, .18f), steel);
                AddBoxC(st, new Vector3(2.6f, -1.3f, -.02f), new Vector3(.18f, 1.8f, .18f), steel);
            });
        }

        Label3D FxText(Node3D parent, string text, Vector3 pos, float em, Color c)
        {
            var l = new Label3D { Text = text, FontSize = 96, PixelSize = em / 96f, Modulate = c, OutlineSize = 0,
                                  Billboard = BaseMaterial3D.BillboardModeEnum.Disabled, DoubleSided = false };
            parent.AddChild(l); l.Position = pos; return l;
        }

        Node3D FxRoot(Basis b, Vector3 pos) { var n = new Node3D(); AddChild(n); n.Transform = new Transform3D(b, pos); return n; }

        bool FxOnPlatform(Vector3 pt)
        {
            pt.Y = 0f;
            foreach (var q in fxPlat) { var dq = q - pt; dq.Y = 0f; if (dq.LengthSquared() < 36f) return true; }
            return false;
        }

        // True when an overhead-line mast stands within 'within' metres (along the track) of this offset.
        bool FxMastNear(TEdge e, float off, float within)
        {
            int m0 = Mathf.RoundToInt(off / MastSpacing - .5f);
            for (int m = m0 - 1; m <= m0 + 1; m++)
            {
                if (m < 0) continue;
                float sm = (m + .5f) * MastSpacing; if (sm > e.Length) continue;
                float sp = MastPlace(e, sm);
                if (sp >= 0f && Mathf.Abs(sp - off) < within) return true;
            }
            return false;
        }

        // Side offset (metres from the track centre, positive = driver's left, negative = right) that avoids masts and platforms.
        float FxSide(TEdge e, float off, float baseSide)
        {
            float side = baseSide;
            if (FxMastNear(e, off, 2.2f)) side += 1.3f;
            var p = e.PosAt(off); var d = e.DirAt(off);
            if (FxOnPlatform(p - Right(d) * side)) side = -baseSide;   // platform on the left: stand on the right instead
            return side;
        }

        // ------------------------------------------------------------------ colour-light signals
        void BuildRealSignals()
        {
            FxInit();
            var cols = new[] { Colors.Red, new Color(1f, .8f, .1f), new Color(.2f, 1f, .3f) };
            foreach (var s in sc.Graph.Signals)
            {
                var p = s.Edge.PosAt(s.Offset); var d = s.Edge.DirAt(s.Offset); var q = Basis.LookingAt(d, Vector3.Up);
                float side = s.Edge.Virtual ? -3.4f : FxSide(s.Edge, s.Offset, 3.4f);    // v5.29: a signal for the wrong-road direction stands on the outer (driver's right) side, never between the two mains
                var root = FxRoot(q, p - Right(d) * side);
                root.AddChild(new MeshInstance3D { Mesh = fxSigMesh, MaterialOverride = fxVert });
                var mats = new StandardMaterial3D[3];
                for (int i = 0; i < 3; i++)
                {
                    mats[i] = Mat(cols[i].Darkened(.8f)); mats[i].EmissionEnabled = false; mats[i].Roughness = .25f; mats[i].EmissionEnergyMultiplier = 2.2f;
                    var lens = new MeshInstance3D { Mesh = fxLensMesh, MaterialOverride = mats[i] };
                    root.AddChild(lens);
                    lens.Transform = new Transform3D(new Basis(Vector3.Right, Mathf.Pi / 2f), new Vector3(0f, 5.2f - i * .45f, .215f));
                }
                sigMats[s] = mats;
                Far(FxText(root, s.Id, new Vector3(0f, 3.45f, -.14f), .15f, Colors.Black), 220f);
            }
        }

        // ------------------------------------------------------------------ kilometre post (called from BuildIndianRailwayObjects)
        void FxKmPost(TEdge e, float off, int km)
        {
            FxInit();
            var p = e.PosAt(off); var d = e.DirAt(off); var q = Basis.LookingAt(d, Vector3.Up);
            float side = FxSide(e, off, 2.9f);
            var root = FxRoot(q, p - Right(d) * side);
            var mi = new MeshInstance3D { Mesh = fxKmMesh, MaterialOverride = fxVert }; root.AddChild(mi); Far(mi, 700f);
            Far(FxText(root, "" + km, new Vector3(0f, .74f, .106f), .22f, Colors.Black), 350f);
        }

        // ------------------------------------------------------------------ station name board (called from BuildIndianRailwayObjects)
        void FxStationBoard(TStation st)
        {
            FxInit();
            var p = st.Edge.PosAt(st.Offset); var d = st.Edge.DirAt(st.Offset);
            var side = Right(d) * (st.Side == "L" ? -1f : 1f);
            var toTrack = -side;
            var root = FxRoot(new Basis(Vector3.Up.Cross(toTrack), Vector3.Up, toTrack), p + side * 4.8f + Vector3.Up * 3.25f);
            root.AddChild(new MeshInstance3D { Mesh = fxStationMesh, MaterialOverride = fxVert });

            string full = st.Name; int i = full.IndexOf(" (");
            string nm = i > 0 ? full.Substring(0, i) : full, code = i > 0 ? full.Substring(i + 2).TrimEnd(')') : "";
            string tel = "", hin = "";
            switch (nm)
            {
                case "Secunderabad Jn": tel = "సిక్ంద్రాబాద్ జంక్షన్"; hin = "सिकंदराबाद जंक्शन"; break;
                case "Moula Ali": tel = "మౌలాలి"; hin = "मौलाली"; break;
                case "Charlapalli": tel = "చర్లపల్లి"; hin = "चर्लापल्ली"; break;
                case "Ghatkesar": tel = "ఘట్కేసర్"; hin = "घाटकेसर"; break;
            }
            if (ShowRegionalScripts && tel != "")
            {
                FxText(root, nm.ToUpper(), new Vector3(0f, .42f, .075f), .5f, Colors.Black);
                FxText(root, tel, new Vector3(0f, -.02f, .075f), .36f, Colors.Black);
                FxText(root, hin, new Vector3(0f, -.46f, .075f), .36f, Colors.Black);
            }
            else
            {
                FxText(root, nm.ToUpper(), new Vector3(0f, .15f, .075f), .6f, Colors.Black);
                if (code != "") FxText(root, code, new Vector3(0f, -.4f, .075f), .4f, Colors.Black);
            }
        }

        // ------------------------------------------------------------------ speed boards, T/P boards, gradient posts
        void FxSpeedBoard(TEdge e, float off, string txt, bool tp)
        {
            var p = e.PosAt(off); var d = e.DirAt(off); var q = Basis.LookingAt(d, Vector3.Up);
            float side = FxSide(e, off, 3.2f);
            var root = FxRoot(q, p - Right(d) * side);
            var mi = new MeshInstance3D { Mesh = fxSignMesh, MaterialOverride = fxVert }; root.AddChild(mi); Far(mi, 800f);
            Far(FxText(root, txt, new Vector3(0f, 2.0f, .102f), tp ? .3f : .4f, Colors.Black), 400f);
        }

        void FxGradientPost(TEdge e, float off, float gPrev, float gNew)
        {
            var p = e.PosAt(off); var d = e.DirAt(off);
            float sideDist = FxSide(e, off, 3.0f);
            var side = Right(d) * (sideDist >= 0f ? -1f : 1f);          // unit vector from the track to the post
            var toTrack = -side;
            var root = FxRoot(new Basis(Vector3.Up.Cross(toTrack), Vector3.Up, toTrack), p + side * Mathf.Abs(sideDist));
            var mi = new MeshInstance3D { Mesh = fxGradMesh, MaterialOverride = fxVert }; root.AddChild(mi); Far(mi, 500f);
            for (int k = 0; k < 2; k++)
            {
                float g = k == 0 ? gPrev : gNew;                          // left arm: the grade before, right arm: the grade ahead
                float ang = Mathf.DegToRad(Mathf.Clamp(g * 3f, -25f, 25f));
                var arm = new MeshInstance3D { Mesh = fxArmMesh, MaterialOverride = fxVert }; root.AddChild(arm); Far(arm, 500f);
                arm.Transform = new Transform3D(new Basis(Vector3.Back, ang), new Vector3(k == 0 ? -.3f : .3f, 1.35f, .1f));
            }
            string t = Mathf.Abs(gNew) < .5f ? "LEVEL" : "1 IN " + Mathf.RoundToInt(1000f / Mathf.Abs(gNew));
            Far(FxText(root, t, new Vector3(0f, .95f, .106f), .1f, Colors.Black), 250f);
        }

        void BuildLineSigns()
        {
            FxInit();
            foreach (var e in sc.Graph.Edges.Values)
            {
                if (e.Turnout) continue;
                TEdge n = null;
                foreach (var o in e.To.Outs) if (!o.Turnout) { n = o; break; }
                if (n == null || n == e) continue;
                float off = Mathf.Min(8f, n.Length * .5f);
                if (n.LimitKmh > 0f && n.LimitKmh < e.LimitKmh - .5f) FxSpeedBoard(n, off, "" + Mathf.RoundToInt(n.LimitKmh), false);
                else if (n.LimitKmh >= 100f && n.LimitKmh > e.LimitKmh + .5f) FxSpeedBoard(n, off, "T/P", true);
                if (Mathf.Abs(n.Gradient - e.Gradient) > .5f) FxGradientPost(n, Mathf.Min(24f, n.Length * .5f), e.Gradient, n.Gradient);
            }
        }
    }
}
