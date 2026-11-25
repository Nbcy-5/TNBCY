using Godot;

namespace TNBCY
{
    // v46 performance helpers. Nothing here lowers texture quality: the textures, materials and the close-up sleeper are unchanged.
    //
    // Why the game lagged: every sleeper was a 1,600-triangle mesh and a whole 130 m piece of them was drawn up to 90 m away
    // (hundreds of thousands of triangles per track), and the far sleepers used a 370-triangle mesh each as well.
    // Now a sleeper has four levels of detail, picked by distance, so the part you can actually see stays exactly as sharp:
    //     close   (0 - 18 m)   : the full curved sleeper with the rail clips (same look as before, a little less vertex noise on the grain)
    //     middle  (18 - 150 m) : the same curved sleeper shape with fewer segments
    //     far     (90 - 480 m) : a 5-segment sleeper, every second sleeper
    //     distant (480 - 2600 m): a 3-segment sleeper, every fourth sleeper
    // The far levels sit 1.5 cm lower, so where they overlap the middle level they never flicker against it.
    public partial class Main
    {
        Mesh rtSleeperMid, rtSleeperFar1, rtSleeperFar2;
        const int RTSleeperPiece = 40;            // sleepers per close/middle piece (about 24 m of track), so the detail level switches in small steps
        const float RTSleeperNearRange = 18f;     // metres (measured to the centre of a piece) where the full sleeper hands over to the middle one

        // Curved concrete sleeper in the unit box space used by the track loop, with a choice of detail.
        //  segments   : slices across the sleeper (the curved top)
        //  zSegments  : slices along the sleeper's width (only used to carry the weathering colours)
        //  includeClips: rail clips on the top
        //  drop       : moves the whole mesh down (unit box space), used by the far levels
        static ArrayMesh RTMakeSleeperTier(int segments, int zSegments, float sag, bool includeClips, float drop)
        {
            return BuildMesh(st =>
            {
                const float width = 1f, z0 = -.5f, z1 = .5f;
                float bottom = -.5f - drop;
                for (int i = 0; i < segments; i++)
                {
                    float x0 = -width * .5f + width * i / segments;
                    float x1 = -width * .5f + width * (i + 1) / segments;
                    float nx0 = x0 / (width * .5f), nx1 = x1 / (width * .5f);
                    float y0 = .5f - sag * (1f - nx0 * nx0) - drop;
                    float y1 = .5f - sag * (1f - nx1 * nx1) - drop;

                    for (int j = 0; j < zSegments; j++)
                    {
                        float za = Mathf.Lerp(z0, z1, j / (float)zSegments);
                        float zb = Mathf.Lerp(z0, z1, (j + 1) / (float)zSegments);
                        RTSleeperTopQuad(st, new Vector3(x0, y0, za), new Vector3(x1, y1, za),
                            new Vector3(x1, y1, zb), new Vector3(x0, y0, zb), Vector3.Up);
                    }
                    RTSleeperQuad(st, new Vector3(x0, y0, z0), new Vector3(x1, y1, z0),
                        new Vector3(x1, bottom, z0), new Vector3(x0, bottom, z0), Vector3.Forward);
                    RTSleeperQuad(st, new Vector3(x1, y1, z1), new Vector3(x0, y0, z1),
                        new Vector3(x0, bottom, z1), new Vector3(x1, bottom, z1), Vector3.Back);

                    if (i == 0)
                        RTSleeperQuad(st, new Vector3(x0, y0, z1), new Vector3(x0, y0, z0),
                            new Vector3(x0, bottom, z0), new Vector3(x0, bottom, z1), Vector3.Left);
                    if (i == segments - 1)
                        RTSleeperQuad(st, new Vector3(x1, y1, z0), new Vector3(x1, y1, z1),
                            new Vector3(x1, bottom, z1), new Vector3(x1, bottom, z0), Vector3.Right);
                }
                RTSleeperQuad(st, new Vector3(-.5f, bottom, z0), new Vector3(.5f, bottom, z0),
                    new Vector3(.5f, bottom, z1), new Vector3(-.5f, bottom, z1), Vector3.Down);

                if (includeClips)
                {
                    Color steel = new Color(.24f, .24f, .26f);
                    for (int side = -1; side <= 1; side += 2)
                        for (int k = -1; k <= 1; k += 2)
                            AddBoxC(st, new Vector3(side * .3175f + k * .0382f, .5f - drop, 0f),
                                new Vector3(.00655f, .14f, .38f), steel);
                }
            });
        }

        // Called at the start of the rock set-up (end of RTV29Init), before the sleepers are made.
        void RTPerfInit()
        {
            rtSleeperN26 = RTMakeSleeperTier(31, 14, .22f, true, 0f);
            rtSleeperMid = RTMakeSleeperTier(15, 4, .22f, false, 0f);
            rtSleeperFar1 = RTMakeSleeperTier(5, 1, .22f, false, .07f);
            rtSleeperFar2 = RTMakeSleeperTier(3, 1, .22f, false, .07f);
        }

        // Like RTMultiMeshLod, but every instance carries its own colour (the per-sleeper shade).
        MultiMesh RTMultiMeshLodColor(Mesh mesh, Material material, int count, float begin, float end)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = count };
            var instance = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = material };
            AddChild(instance); Lod(instance, begin, end);
            return mm;
        }

        // Anti-aliasing per graphics level. This replaces the MSAA line in ApplyQuality (MainWorld.cs):
        // Fast no longer uses MSAA (FXAA still smooths the rails), Balanced and High keep 2x, Ultra keeps 4x.
        // It is checked a few times a second, so it also follows the automatic quality changes.
        void RTApplyAA()
        {
            var vp = GetViewport(); if (vp == null) return;
            var want = quality >= 2 ? Viewport.Msaa.Msaa4X : (quality == 1 ? Viewport.Msaa.Msaa2X : Viewport.Msaa.Disabled);
            if (vp.Msaa3D != want) vp.Msaa3D = want;
        }
    }
}
