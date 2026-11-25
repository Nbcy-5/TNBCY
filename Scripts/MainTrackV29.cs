using Godot;

namespace TNBCY
{
    // v40: sleepers and ballast match the "Track Update Preview" reference photo.
    //  - Sleepers: warm weathered concrete. Fine grain, pits, dark aggregate and dirt blotches come from a
    //    WORLD-SPACE texture, so every sleeper looks different. Rust and oil at the rail seats, dust, dirty long edges.
    //    The bowed (curved) sleeper shape is unchanged.
    //  - Ballast: natural crushed stone: dark charcoal, grey, pale, rust-brown and orange-tan stones,
    //    deep dark gaps with warm dust, strong relief. No cement look, no green/mud tint.
    // Tuning knobs are the constants just below.
    public partial class Main
    {
        Mesh rtSleeperLod;

        // ---- tuning knobs ----
        static readonly Color RTSleeperBase = new Color(.64f, .605f, .545f);   // warm concrete; lower = darker sleepers
        const float RTSleeperTexTilesPerMeter = .85f;                           // grain size on sleepers (higher = finer)
        const int RTBallastCells = 24;                                          // stones per 2 m tile (higher = smaller stones)
        const float RTBallastNormalScale = 1.5f;                                // stone relief strength

        // Weathering is applied to sleeper top faces: rust at rail seats, grease near pads, dust, dirty long edges.
        static Color RTSleeperTopColor(float x, float z)
        {
            float broad = RTNoise((x + .5f) * 5f, (z + .5f) * 5f, 16, 1601);
            float fine = RTNoise((x + .5f) * 23f, (z + .5f) * 19f, 32, 1602);
            Color c = RTSleeperBase * (.90f + .12f * broad + .06f * fine);

            // Dry cement dust and pale mineral deposits, irregular rather than uniform.
            float dust = Mathf.Clamp((broad - .42f) * .42f, 0f, .20f);
            c = c.Lerp(new Color(.76f, .72f, .62f), dust);

            // Dirt collects along the two long edges of the sleeper where ballast touches it.
            float edgeDirt = Mathf.SmoothStep(.36f, .50f, Mathf.Abs(z)) * .28f;
            c = c.Lerp(new Color(.30f, .27f, .23f), edgeDirt);

            for (int side = -1; side <= 1; side += 2)
            {
                float dx = (x - side * .3175f) / .085f;
                float railSeat = Mathf.Exp(-dx * dx);
                float rustNoise = RTNoise((z + .5f) * 8f + side * 3f, (x + .5f) * 7f, 16, 1603 + side);
                float rust = railSeat * (.52f + .30f * rustNoise);
                c = c.Lerp(new Color(.48f, .20f, .085f), rust);

                // Dark oily residue sits inside the rusted rail-seat band.
                float oilNoise = RTNoise((z + .5f) * 13f + side, (x + .5f) * 11f, 16, 1610 + side);
                float oil = railSeat * (.28f + .24f * oilNoise);
                c = c.Lerp(new Color(.105f, .115f, .115f), oil);

                // A few short rust streaks trail away from each fastening.
                float streakX = (x - side * .3175f) / .12f;
                float streakZ = (z + .5f) * 3.1f + .22f * RTNoise((x + .5f) * 8f, (z + .5f) * 5f, 16, 1624 + side);
                float streak = Mathf.Exp(-streakX * streakX) * Mathf.Clamp(1f - Mathf.Abs(streakZ - Mathf.Floor(streakZ) - .5f) * 3.5f, 0f, 1f) * .20f;
                c = c.Lerp(new Color(.55f, .27f, .13f), streak);
            }

            // Sparse embedded grime spots away from the rail seats.
            float grimeNoise = RTNoise((x + .5f) * 31f, (z + .5f) * 17f, 32, 1621);
            if (grimeNoise > .84f)
                c = c.Lerp(new Color(.29f, .30f, .29f), (grimeNoise - .84f) * 1.5f);
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

        // Sides and ends: dirtier and darker toward the bottom (buried in ballast dust).
        static void RTSleeperVertex(SurfaceTool st, Vector3 p, Vector3 normal)
        {
            float t = Mathf.Clamp(p.Y + .5f, 0f, 1f);
            st.SetColor(new Color(.52f, .49f, .44f) * (.55f + .45f * t)); st.SetNormal(normal); st.AddVertex(p);
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
                    // Vertex positions retain the exact curved sleeper profile.
                    const int zSegments = 24;
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
            });
        }

        static ArrayMesh RTMakeSleeperCurved() => RTMakeSleeperMesh(31, .22f, true);
        static ArrayMesh RTMakeSleeperLod() => RTMakeSleeperMesh(7, .22f, false);

        // Tileable concrete grain used as a multiplier on the sleeper's vertex colour (values are 0..1, mean about .85).
        // Dirt blotches, dark aggregate, a few pale pebbles and small pits. World-space mapped, so no two sleepers match.
        static RTTex RTMakeSleeperConcrete(int size)
        {
            const int cells = 40;
            var alb = new byte[size * size * 3];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float broad = RTFbm(u, v, 3, 4, 1701);
                    float mid = RTFbm(u + .31f, v + .17f, 8, 3, 1702);
                    float m = .90f + .12f * (mid - .5f);
                    float dirt = Mathf.Clamp((broad - .50f) * 3.4f, 0f, .60f);
                    m *= 1f - dirt * .40f;

                    float uu = u * cells + .8f * (RTFbm(u, v, 6, 3, 1711) - .5f);
                    float vv = v * cells + .8f * (RTFbm(u + .5f, v + .2f, 6, 3, 1712) - .5f);
                    int xi = Mathf.FloorToInt(uu), yi = Mathf.FloorToInt(vv);
                    float dmin = 9f; int sx = 0, sy = 0;
                    for (int j = -1; j <= 1; j++)
                        for (int i = -1; i <= 1; i++)
                        {
                            int cx = xi + i, cy = yi + j, wx = RTWrap(cx, cells), wy = RTWrap(cy, cells);
                            if (RTHash(wx, wy, 1720) >= .45f) continue;
                            float px = cx + .15f + .7f * RTHash(wx, wy, 1721), py = cy + .15f + .7f * RTHash(wx, wy, 1722);
                            float rad = .08f + .30f * RTHash(wx, wy, 1723) * RTHash(wx, wy, 1723);
                            float d = Mathf.Sqrt((px - uu) * (px - uu) + (py - vv) * (py - vv)) / rad;
                            if (d < dmin) { dmin = d; sx = wx; sy = wy; }
                        }
                    if (dmin < 1f)
                    {
                        bool dark = RTHash(sx, sy, 1724) < .72f;
                        float sh = .75f + .30f * RTHash(sx, sy, 1725);
                        m *= dark ? .52f + .25f * sh : 1.06f;
                    }

                    float f1 = RTHash(x, y, 1703);
                    if (f1 > .965f || RTHash(x / 2, y / 2, 1704) > .985f) m *= .55f;     // pits
                    m *= .95f + .07f * f1;
                    m = Mathf.Clamp(m, 0f, 1f);

                    int o = (y * size + x) * 3;
                    alb[o] = RTByte(m);
                    alb[o + 1] = RTByte(m * (.985f - dirt * .06f));
                    alb[o + 2] = RTByte(m * (.95f - dirt * .20f));
                }
            return new RTTex { Albedo = alb, Normal = null, Size = size };
        }

        // Natural crushed-stone ballast like the reference photo: mostly dark charcoal and grey angular stones,
        // some pale ones, a few rust-brown and orange-tan ones, very dark gaps with warm dust, sharp facets.
        static RTTex RTMakeNaturalBallast(int size)
        {
            const int cells = RTBallastCells;
            var alb = new byte[size * size * 3];
            var hgt = new float[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float fu = x / (float)size * cells, fv = y / (float)size * cells;
                    float wu = fu + .9f * (RTNoise(fu * .5f, fv * .5f, cells / 2, 901) - .5f);
                    float wv = fv + .9f * (RTNoise(fu * .5f + 3f, fv * .5f + 7f, cells / 2, 902) - .5f);
                    int xi = Mathf.FloorToInt(wu), yi = Mathf.FloorToInt(wv);
                    float d1 = 99f, d2 = 99f, fx = 0f, fy = 0f;
                    int bx = 0, by = 0;
                    for (int j = -1; j <= 1; j++)
                        for (int i = -1; i <= 1; i++)
                        {
                            int cx = xi + i, cy = yi + j, wx = RTWrap(cx, cells), wy = RTWrap(cy, cells);
                            float px = cx + .1f + .8f * RTHash(wx, wy, 911);
                            float py = cy + .1f + .8f * RTHash(wx, wy, 912);
                            float sz = .60f + .95f * RTHash(wx, wy, 913);
                            float dx = px - wu, dy = py - wv;
                            float ax = Mathf.Abs(dx), ay = Mathf.Abs(dy);
                            float d = (Mathf.Max(ax, ay) * .6f + (ax + ay) * .4f) / sz;     // angular, octagon-like stones of different sizes
                            if (d < d1) { d2 = d1; d1 = d; bx = wx; by = wy; fx = -dx / sz; fy = -dy / sz; }
                            else if (d < d2) d2 = d;
                        }

                    float r1 = RTHash(bx, by, 921), r2 = RTHash(bx, by, 922), r3 = RTHash(bx, by, 923), r4 = RTHash(bx, by, 924);
                    float cr, cg, cb;
                    if (r2 < .42f) { float v = .15f + .10f * r1; cr = v; cg = v; cb = v * 1.03f; }                          // dark charcoal
                    else if (r2 < .70f) { float v = .27f + .12f * r1; cr = v; cg = v * .99f; cb = v * .97f; }               // mid grey
                    else if (r2 < .84f) { float v = .42f + .16f * r1; cr = v; cg = v * .99f; cb = v * .96f; }               // pale grey
                    else if (r2 < .93f) { cr = .34f + .12f * r1; cg = .20f + .07f * r1; cb = .13f + .04f * r1; }            // rust-brown
                    else { cr = .46f + .12f * r1; cg = .31f + .08f * r1; cb = .20f + .05f * r1; }                           // orange-tan

                    float facet = Mathf.Clamp(.66f + .90f * (.5f + (fx * (r3 - .5f) + fy * (r4 - .5f)) * 1.1f), .35f, 1.45f);
                    float edge = Mathf.Clamp((d2 - d1) * 3.2f, 0f, 1f);
                    float k = facet * (.22f + .78f * Mathf.Pow(edge, .8f));            // dark gaps between stones

                    float uu = x / (float)size, vv = y / (float)size;
                    float dust = Mathf.Clamp((RTFbm(uu, vv, 4, 3, 931) - .48f) * 2.2f, 0f, 1f) * .22f;
                    float spk = RTHash(x * 5 + bx * 7, y * 5 + by * 3, 6), spk2 = RTHash(x, y, 9);
                    float speck = spk > .93f ? .18f + .15f * spk2 : (spk < .06f ? -.05f : 0f);     // bright mineral flecks

                    float fr = (cr + speck) * k, fg = (cg + speck) * k, fb = (cb + speck) * k;
                    // Dry pale-tan dust film on stone faces.
                    fr = Mathf.Lerp(fr, .52f * k, dust * edge); fg = Mathf.Lerp(fg, .45f * k, dust * edge); fb = Mathf.Lerp(fb, .36f * k, dust * edge);
                    // Warm brown dust packed into the gaps instead of neutral black.
                    float gap = (1f - edge) * .35f;
                    fr = Mathf.Lerp(fr, .20f, gap); fg = Mathf.Lerp(fg, .14f, gap); fb = Mathf.Lerp(fb, .09f, gap);

                    int o = (y * size + x) * 3;
                    alb[o] = RTByte(fr); alb[o + 1] = RTByte(fg); alb[o + 2] = RTByte(fb);
                    hgt[y * size + x] = edge * (.55f + .45f * r1) + .04f * spk2;
                }
            return new RTTex { Albedo = alb, Normal = RTNormals(hgt, size, 3.0f), Size = size };
        }

        void RTV29Init()
        {
            rtSleeperN26 = RTMakeSleeperCurved();
            rtSleeperLod = RTMakeSleeperLod();

            if (matGravel != null)
            {
                var ballast = RTMakeNaturalBallast(512);
                matGravel.AlbedoTexture = RTTexture(ballast.Albedo, ballast.Size, false);
                matGravel.NormalEnabled = true;
                matGravel.NormalTexture = RTTexture(ballast.Normal, ballast.Size, true);
                matGravel.NormalScale = RTBallastNormalScale;
                matGravel.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
                matGravel.VertexColorUseAsAlbedo = false;
                matGravel.AlbedoColor = Colors.White;
                matGravel.Roughness = .98f;
            }

            // Sleeper colour = mesh vertex colour (rail-seat rust, oil, dust, dirty edges) multiplied by a world-space
            // concrete grain texture (pits, aggregate, dirt blotches) so every sleeper differs.
            var concrete = RTMakeSleeperConcrete(256);
            var sleeperMat = new StandardMaterial3D
            {
                AlbedoTexture = RTTexture(concrete.Albedo, concrete.Size, false),
                AlbedoColor = Colors.White,
                VertexColorUseAsAlbedo = true,
                Roughness = .96f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                Uv1Triplanar = true,
                Uv1WorldTriplanar = true,
                Uv1TriplanarSharpness = 3f,
                Uv1Scale = new Vector3(RTSleeperTexTilesPerMeter, RTSleeperTexTilesPerMeter, RTSleeperTexTilesPerMeter)
            };
            matConcrete = sleeperMat;
        }
    }
}
