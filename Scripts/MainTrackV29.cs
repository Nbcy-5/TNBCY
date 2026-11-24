using Godot;

namespace TNBCY
{
    // v34: stable continuous sleeper geometry and a dedicated light cement-granite ballast texture.
    public partial class Main
    {
        Mesh rtSleeperLod;
        static void RTSleeperVertex(SurfaceTool st, Vector3 p, Vector3 normal)
        {
            st.SetColor(Colors.White); st.SetNormal(normal); st.AddVertex(p);
        }

        static void RTSleeperQuad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            RTSleeperVertex(st, a, normal); RTSleeperVertex(st, b, normal); RTSleeperVertex(st, c, normal);
            RTSleeperVertex(st, a, normal); RTSleeperVertex(st, c, normal); RTSleeperVertex(st, d, normal);
        }

        static ArrayMesh RTMakeSleeperMesh(int segments, float sag, bool includeClips)
        {
            return BuildMesh(st =>
            {
                const float width = 1f, z0 = -.5f, z1 = .5f, bottom = -.5f;
                for (int i = 0; i < segments; i++)
                {
                    float x0 = -width * .5f + width * i / segments;
                    float x1 = -width * .5f + width * (i + 1) / segments;
                    float nx0 = x0 / (width * .5f), nx1 = x1 / (width * .5f);
                    float y0 = .5f - sag * (1f - nx0 * nx0);
                    float y1 = .5f - sag * (1f - nx1 * nx1);

                    // Adjacent top strips share exact edges; no overlapping coplanar faces.
                    RTSleeperQuad(st, new Vector3(x0, y0, z0), new Vector3(x1, y1, z0),
                        new Vector3(x1, y1, z1), new Vector3(x0, y0, z1), Vector3.Up);
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
                    Color steel = new Color(.22f, .22f, .24f);
                    for (int side = -1; side <= 1; side += 2)
                        for (int k = -1; k <= 1; k += 2)
                            AddBoxC(st, new Vector3(side * .3175f + k * .0382f, .5f, 0f),
                                new Vector3(.00655f, .14f, .38f), steel);
                }
            });
        }

        static ArrayMesh RTMakeSleeperCurved() => RTMakeSleeperMesh(31, .22f, true);
        static ArrayMesh RTMakeSleeperLod() => RTMakeSleeperMesh(7, .22f, false);

        // New colour source, not a brightness remap of the old dark granite.
        // Neutral concrete-grey angular chips, pale mineral flecks and restrained warm mud.
        static RTTex RTMakeCementGranite(int size)
        {
            const int cells = 26;
            var alb = new byte[size * size * 3];
            var hgt = new float[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float fu = x / (float)size * cells, fv = y / (float)size * cells;
                    float wu = fu + .65f * (RTNoise(fu * .5f, fv * .5f, 13, 901) - .5f);
                    float wv = fv + .65f * (RTNoise(fu * .5f + 3f, fv * .5f + 7f, 13, 902) - .5f);
                    int xi = Mathf.FloorToInt(wu), yi = Mathf.FloorToInt(wv);
                    float d1 = 99f, d2 = 99f, fx = 0f, fy = 0f;
                    int bx = 0, by = 0;
                    for (int j = -1; j <= 1; j++)
                        for (int i = -1; i <= 1; i++)
                        {
                            int cx = xi + i, cy = yi + j, wx = RTWrap(cx, cells), wy = RTWrap(cy, cells);
                            float px = cx + .1f + .8f * RTHash(wx, wy, 911);
                            float py = cy + .1f + .8f * RTHash(wx, wy, 912);
                            float sz = .72f + .55f * RTHash(wx, wy, 913);
                            float dx = px - wu, dy = py - wv;
                            float d = (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) * .62f + (Mathf.Abs(dx) + Mathf.Abs(dy)) * .38f) / sz;
                            if (d < d1) { d2 = d1; d1 = d; bx = wx; by = wy; fx = dx / sz; fy = dy / sz; }
                            else if (d < d2) d2 = d;
                        }

                    float stone = RTHash(bx, by, 921), tint = RTHash(bx, by, 922);
                    Color c = stone < .56f ? new Color(.61f, .63f, .64f)
                        : stone < .78f ? new Color(.70f, .71f, .70f)
                        : stone < .91f ? new Color(.57f, .59f, .59f)
                        : new Color(.64f, .58f, .50f);
                    float facet = Mathf.Clamp(.88f + .20f * (fx * (tint - .5f) + fy * (RTHash(bx, by, 923) - .5f)), .76f, 1.08f);
                    c *= facet;
                    float seam = 1f - Mathf.SmoothStep(.018f, .075f, d2 - d1);
                    c = c.Lerp(new Color(.31f, .31f, .30f), seam * .48f);

                    float u = x / (float)size, v = y / (float)size;
                    float mud = Mathf.Clamp((RTFbm(u, v, 4, 4, 931) - .53f) * 3.2f, 0f, .48f);
                    c = c.Lerp(new Color(.43f, .36f, .28f), mud * .52f);
                    float grease = Mathf.Clamp((RTFbm(u + .37f, v + .12f, 3, 4, 941) - .73f) * 1.8f, 0f, .08f);
                    c = c.Lerp(new Color(.32f, .30f, .27f), grease);
                    float fleck = RTHash(x, y, 951);
                    if (fleck > .992f) c = c.Lerp(new Color(.82f, .82f, .78f), .45f);

                    int o = (y * size + x) * 3;
                    alb[o] = RTByte(c.R); alb[o + 1] = RTByte(c.G); alb[o + 2] = RTByte(c.B);
                    hgt[y * size + x] = Mathf.Clamp((d2 - d1) * 2.5f, 0f, 1f) * (.7f + .3f * tint) + .03f * fleck;
                }
            return new RTTex { Albedo = alb, Normal = RTNormals(hgt, size, 2.0f), Size = size };
        }

        void RTV29Init()
        {
            rtSleeperN26 = RTMakeSleeperCurved();
            rtSleeperLod = RTMakeSleeperLod();

            if (matGravel != null)
            {
                var granite = RTMakeCementGranite(512);
                matGravel.AlbedoTexture = RTTexture(granite.Albedo, granite.Size, false);
                matGravel.NormalEnabled = true;
                matGravel.NormalTexture = RTTexture(granite.Normal, granite.Size, true);
                matGravel.NormalScale = 1.15f;
                matGravel.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
                matGravel.VertexColorUseAsAlbedo = false;
                matGravel.AlbedoColor = Colors.White;
                matGravel.Roughness = .98f;
            }
        }
    }
}