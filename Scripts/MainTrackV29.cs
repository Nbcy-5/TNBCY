using Godot;

namespace TNBCY
{
    // v37: preserve the curved sleeper design and add realistic weathering to sleeper tops only.
    public partial class Main
    {
        Mesh rtSleeperLod;
        // Weathering is applied to sleeper top faces only: rust at rail seats, grease near pads, and light dust.
        static Color RTSleeperTopColor(float x, float z)
        {
            float broad = RTNoise((x + .5f) * 5f, (z + .5f) * 4f, 16, 1601);
            float fine = RTNoise((x + .5f) * 17f, (z + .5f) * 13f, 32, 1602);
            Color c = new Color(.91f, .92f, .91f) * (.93f + .10f * broad + .035f * fine);
            float dust = Mathf.Clamp((broad - .48f) * .42f, 0f, .18f);
            c = c.Lerp(new Color(.98f, .95f, .87f), dust);
            for (int side = -1; side <= 1; side += 2)
            {
                float dx = (x - side * .3175f) / .052f;
                float railSeat = Mathf.Exp(-dx * dx);
                float rustNoise = RTNoise((z + .5f) * 10f + side * 3f, (x + .5f) * 6f, 16, 1603 + side);
                c = c.Lerp(new Color(.58f, .30f, .16f), railSeat * (.22f + .18f * rustNoise));
                float oilNoise = RTNoise((z + .5f) * 14f + side, (x + .5f) * 10f, 16, 1610 + side);
                c = c.Lerp(new Color(.24f, .25f, .24f), railSeat * (.08f + .12f * oilNoise));
            }
            if (RTNoise((x + .5f) * 29f, (z + .5f) * 23f, 32, 1621) > .88f)
                c = c.Lerp(new Color(.98f, .96f, .89f), .10f);
            return c;
        }

        static void RTSleeperTopVertex(SurfaceTool st, Vector3 p, Vector3 normal)
        {
            st.SetColor(RTSleeperTopColor(p.X, p.Z)); st.SetNormal(normal); st.AddVertex(p);
        }

        static void RTSleeperTopQuad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            RTSleeperTopVertex(st, a, normal); RTSleeperTopVertex(st, b, normal); RTSleeperTopVertex(st, c, normal);
            RTSleeperTopVertex(st, a, normal); RTSleeperTopVertex(st, c, normal); RTSleeperTopVertex(st, d, normal);
        }

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

                    // Subdivide only the existing top surface for weathering colour interpolation.
                    // Vertex positions retain the exact curved sleeper profile from v36.
                    const int zSegments = 12;
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
                    // Muted mid-grey concrete chips; only a restrained earthy tint in a few stones.
                    Color c = stone < .56f ? new Color(.48f, .50f, .51f)
                        : stone < .78f ? new Color(.55f, .56f, .56f)
                        : stone < .91f ? new Color(.43f, .45f, .46f)
                        : new Color(.48f, .45f, .41f);
                    float facet = Mathf.Clamp(.88f + .20f * (fx * (tint - .5f) + fy * (RTHash(bx, by, 923) - .5f)), .76f, 1.08f);
                    c *= facet;
                    float seam = 1f - Mathf.SmoothStep(.018f, .075f, d2 - d1);
                    c = c.Lerp(new Color(.27f, .28f, .28f), seam * .52f);

                    float u = x / (float)size, v = y / (float)size;
                    float mud = Mathf.Clamp((RTFbm(u, v, 4, 4, 931) - .50f) * 3.8f, 0f, .58f);
                    c = c.Lerp(new Color(.39f, .37f, .34f), mud * .62f);
                    float grease = Mathf.Clamp((RTFbm(u + .37f, v + .12f, 3, 4, 941) - .73f) * 1.8f, 0f, .10f);
                    c = c.Lerp(new Color(.28f, .28f, .27f), grease);
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