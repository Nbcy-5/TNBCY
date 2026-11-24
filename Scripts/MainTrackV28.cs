using Godot;

namespace TNBCY
{
    // v28 track visual update. Adapted to the existing TNBCY track renderer:
    // complete concrete sleeper slabs, light cement-grey ballast, and mottled rust on the rails.
    public partial class Main
    {
        // One complete sleeper in the unit box space used by the track loop:
        // x across (2.75 m), y up (0.21 m), z along (0.26 m).
        static ArrayMesh RTMakeSleeperFull()
        {
            return BuildMesh(st =>
            {
                Color body = Colors.White;
                Color steel = new Color(.22f, .22f, .24f);
                AddBoxC(st, Vector3.Zero, new Vector3(1f, 1f, 1f), body);
                for (int side = -1; side <= 1; side += 2)
                    for (int k = -1; k <= 1; k += 2)
                        AddBoxC(st, new Vector3(side * .3175f + k * .0382f, .5f, 0f),
                            new Vector3(.018f, .14f, .38f), steel);
            });
        }

        // Convert the generated ballast to neutral cement grey without removing its relief.
        static byte[] RTLiftGrey(byte[] src, float lo, float gain, float pivot, float warm)
        {
            var dst = new byte[src.Length];
            for (int i = 0; i + 2 < src.Length; i += 3)
            {
                float l = (src[i] * .30f + src[i + 1] * .59f + src[i + 2] * .11f) / 255f;
                float g = Mathf.Clamp(lo + (l - pivot) * gain, lo * .8f, .95f);
                dst[i] = RTByte(g * (1f + warm));
                dst[i + 1] = RTByte(g);
                dst[i + 2] = RTByte(g * (1f - warm));
            }
            return dst;
        }

        // Tileable iron surface: broad rust patches with smaller mottled edges.
        static byte[] RTMakeRust(int size)
        {
            var data = new byte[size * size * 3];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float big = RTFbm(u, v, 3, 4, 301);
                    float mid = RTFbm(u + .21f, v + .43f, 8, 3, 302);
                    float fine = RTHash(x, y, 303);
                    float rust = Mathf.Clamp((big * .75f + mid * .25f - .50f) * 6.5f, 0f, 1f);
                    rust *= .55f + .45f * mid;
                    if (RTFbm(u, v, 14, 2, 304) > .70f) rust = Mathf.Max(rust, .4f);
                    float pit = .90f + .10f * fine;
                    float red = pit;
                    float green = Mathf.Lerp(1f, .60f - .10f * mid, rust) * pit;
                    float blue = Mathf.Lerp(1f, .36f - .08f * mid, rust) * pit;
                    int o = (y * size + x) * 3;
                    data[o] = RTByte(red);
                    data[o + 1] = RTByte(green);
                    data[o + 2] = RTByte(blue);
                }
            return data;
        }

        void RTV28Init()
        {
            // Replace v24's narrow-waist sleeper with one full, flat-topped slab.
            rtSleeperN26 = RTMakeSleeperFull();

            // Keep the granite texture's generated detail, but remove warm/dark vertex bands.
            if (matGravel != null && rtGraniteJob != null)
            {
                var granite = rtGraniteJob.Result;
                matGravel.AlbedoTexture = RTTexture(
                    RTLiftGrey(granite.Albedo, .52f, 1.15f, .30f, .015f), granite.Size, false);
                matGravel.VertexColorUseAsAlbedo = false;
                matGravel.AlbedoColor = Colors.White;
            }

            // Rust is applied in world-space triplanar coordinates so it does not repeat
            // identically on every short rail section.
            if (matRail != null)
            {
                matRail.AlbedoTexture = RTTexture(RTMakeRust(256), 256, false);
                matRail.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
                matRail.Uv1Triplanar = true;
                matRail.Uv1WorldTriplanar = true;
                matRail.Uv1Scale = new Vector3(.16f, .16f, .16f);
            }
        }
    }
}
