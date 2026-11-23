using Godot;
using System.Collections.Generic;

namespace TNBCY
{
    // v21: photo-textured pine trees (crossed quads) from the JVC pine set by Jan K., mixed in with the round-crowned trees of BuildScenery.
    // If a texture cannot be loaded the variant is skipped and the old tree is used, so a missing file can never break the scene.
    public partial class Main
    {
        const float PineShare = .4f;             // share of all trees that are pines (0 = none, 1 = all)
        ArrayMesh[] natMesh; StandardMaterial3D[] natMat;

        void NatureInit()
        {
            if (natMesh != null) return;
            var data = NatureData.Pines;
            natMesh = new ArrayMesh[data.Length]; natMat = new StandardMaterial3D[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                Texture2D tex = null;
                try { tex = GD.Load<Texture2D>("res://Assets/Nature/" + data[i].Tex + ".png"); } catch { }
                if (tex == null) { GD.PrintErr("Nature texture missing: " + data[i].Tex); continue; }
                var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
                var d = data[i];
                for (int k = 0; k < d.I.Length; k++)
                {
                    int vi = d.I[k] * 5;
                    st.SetNormal(Vector3.Up); st.SetUV(new Vector2(d.V[vi + 3], d.V[vi + 4]));
                    st.AddVertex(new Vector3(d.V[vi], d.V[vi + 1], d.V[vi + 2]));
                }
                natMesh[i] = st.Commit();
                // Unshaded: the photo already has its light painted in, and a lit two-sided quad would go dark seen from behind.
                natMat[i] = new StandardMaterial3D
                {
                    AlbedoTexture = tex, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor, AlphaScissorThreshold = .5f,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled, VertexColorUseAsAlbedo = true,
                    TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic
                };
            }
        }

        bool PineOk(int v) { return natMesh != null && v >= 0 && v < natMesh.Length && natMesh[v] != null && natMat[v] != null; }

        void BuildPines(Dictionary<long, List<Prop>> pines)
        {
            foreach (var kv in pines)
                for (int vv = 0; vv < natMesh.Length; vv++)
                {
                    var l = kv.Value.FindAll(q => q.v == vv);
                    if (l.Count == 0 || !PineOk(vv)) continue;
                    var pm = MM(natMesh[vv], natMat[vv], l.Count, 1800f, true);
                    for (int i = 0; i < l.Count; i++)
                    {
                        float ps = .55f + (l[i].s - .8f) / .8f * .35f;               // 55 % .. 90 % of the original size: about 8 to 22 m tall
                        var rot = new Basis(Vector3.Up, l[i].rot) * Basis.FromScale(Vector3.One * ps);
                        pm.SetInstanceTransform(i, new Transform3D(rot, l[i].p));
                        float br = .78f + (l[i].c.G - .30f) / .16f * .22f;           // slight brightness change from tree to tree
                        pm.SetInstanceColor(i, new Color(br, br, br));
                    }
                }
        }
    }
}
