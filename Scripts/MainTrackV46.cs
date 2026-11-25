using Godot;

namespace TNBCY
{
    // v46 sleeper performance. Textures, colours, shape and weathering are unchanged; only the amount of geometry sent to the GPU is.
    //
    // Before: every sleeper within about 90 m used the 1,670-triangle weathered mesh (31 x 24 colour quads), sleepers were grouped in
    // chunks of 220 (143 m), so whole chunks were drawn in full detail when only one end was close, and even the distance meshes had
    // about 400 triangles each. A frame carried a few million triangles of sleepers alone.
    //
    // Now:
    //  - every sleeper mesh is indexed (shared vertices): the vertex shader runs about 5x fewer times for exactly the same picture
    //  - sleepers are grouped in chunks of 64 (41 m), so the switch between detail levels happens close to where it should
    //  - full weathering detail (31 x 24 colour quads, steel shoulders) within RT46NearRange (24 m) of the camera
    //  - 24 m .. 110 m: 15 x 6 colour quads (same bow, same colours, a colour grid smaller than a pixel at that distance)
    //  - beyond 110 m: the distance sleepers (every 2nd, then every 4th) use a 5 x 2 mesh
    // Tuning knobs: RT46SleeperChunk, RT46NearRange, RT46MidRange. Raise RT46NearRange to see full weathering detail further out.
    public partial class Main
    {
        const int RT46SleeperChunk = 64;     // sleepers per chunk
        const float RT46NearRange = 24f;     // full-detail sleepers up to here (distance to the chunk centre)
        const float RT46MidRange = 110f;     // medium-detail sleepers up to here, distance sleepers beyond
        Mesh rtSleeperMid;

        // Same geometry and colours as RTMakeSleeperMesh (MainTrackV29.cs), but indexed and with a choice of colour-grid detail.
        static ArrayMesh RT46MakeSleeperMesh(int segments, float sag, bool includeClips, int zSegments)
        {
            var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
            const float width = 1f, z0 = -.5f, z1 = .5f, bottom = -.5f;
            for (int i = 0; i < segments; i++)
            {
                float x0 = -width * .5f + width * i / segments;
                float x1 = -width * .5f + width * (i + 1) / segments;
                float nx0 = x0 / (width * .5f), nx1 = x1 / (width * .5f);
                float y0 = .5f - sag * (1f - nx0 * nx0);
                float y1 = .5f - sag * (1f - nx1 * nx1);

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
                        AddBoxC(st, new Vector3(side * .3175f + k * .0382f, .5f, 0f),
                            new Vector3(.00655f, .14f, .38f), steel);
            }
            st.Index();      // shares identical vertices between neighbouring quads
            return st.Commit();
        }

        // Called from BuildRealTrack right after RTV29Init: replaces the v29 sleeper meshes.
        void RT46SleeperMeshes()
        {
            rtSleeperN26 = RT46MakeSleeperMesh(31, .22f, true, 24);     // same as v29: full weathering detail, only close to the camera
            rtSleeperMid = RT46MakeSleeperMesh(15, .22f, false, 6);
            rtSleeperLod = RT46MakeSleeperMesh(5, .22f, false, 2);
        }

        // MultiMesh with a fixed visibility window (not scaled by the graphics level) and optional per-instance colours.
        MultiMesh RT46MM(Mesh mesh, Material material, int count, float begin, float end, bool colors)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = colors, Mesh = mesh, InstanceCount = count };
            var instance = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = material };
            AddChild(instance); Lod(instance, begin, end);
            return mm;
        }
    }
}
