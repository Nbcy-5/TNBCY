using Godot;

namespace TNBCY
{
    // v29: gently crowned concrete sleepers and selectively weathered cement-grey ballast.
    public partial class Main
    {
        static ArrayMesh RTMakeSleeperCurved()
        {
            return BuildMesh(st =>
            {
                // Eleven adjoining sections create a subtle downward bow at both ends.
                const int segments = 11;
                const float width = 2.75f;
                for (int i = 0; i < segments; i++)
                {
                    float x = -width * .5f + width * (i + .5f) / segments;
                    float nx = x / (width * .5f);
                    float drop = -.025f * nx * nx;
                    float sx = width / segments + .002f;
                    AddBoxC(st, new Vector3(x, drop, 0f),
                        new Vector3(sx, 1f, 1f), Colors.White);
                }

                Color steel = new Color(.22f, .22f, .24f);
                for (int side = -1; side <= 1; side += 2)
                    for (int k = -1; k <= 1; k += 2)
                        AddBoxC(st, new Vector3(side * .3175f + k * .0382f, .5f, 0f),
                            new Vector3(.018f, .14f, .38f), steel);
            });
        }

        static byte[] RTMakeMuddyGrey(byte[] src, int size)
        {
            var dst = new byte[src.Length];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int i = (y * size + x) * 3;
                    float l = (src[i] * .30f + src[i + 1] * .59f + src[i + 2] * .11f) / 255f;
                    float g = Mathf.Clamp(.52f + (l - .30f) * 1.15f, .416f, .95f);

                    float u = x / (float)size, v = y / (float)size;
                    float broad = RTFbm(u, v, 4, 4, 521);
                    float detail = RTFbm(u + .17f, v + .31f, 11, 3, 522);
                    float mud = Mathf.Clamp((broad * .72f + detail * .28f - .59f) * 3.8f, 0f, .42f);
                    float oilField = RTFbm(u + .37f, v + .12f, 3, 4, 531);
                    float oil = Mathf.Clamp((oilField - .66f) * 3.5f, 0f, .30f);

                    float r = g * 1.015f, gg = g, b = g * .985f;
                    r = Mathf.Lerp(r, .285f, mud);
                    gg = Mathf.Lerp(gg, .270f, mud);
                    b = Mathf.Lerp(b, .245f, mud);
                    r = Mathf.Lerp(r, .145f, oil);
                    gg = Mathf.Lerp(gg, .155f, oil);
                    b = Mathf.Lerp(b, .160f, oil);
                    dst[i] = RTByte(r);
                    dst[i + 1] = RTByte(gg);
                    dst[i + 2] = RTByte(b);
                }
            return dst;
        }

        void RTV29Init()
        {
            // Keep the V28 full sleeper profile, adding only a very slight downward bow at its ends.
            rtSleeperN26 = RTMakeSleeperCurved();

            // Neutral cement-grey ballast with restrained muddy and occasional dark greasy patches.
            if (matGravel != null && rtGraniteJob != null)
            {
                var granite = rtGraniteJob.Result;
                matGravel.AlbedoTexture = RTTexture(
                    RTMakeMuddyGrey(granite.Albedo, granite.Size), granite.Size, false);
                matGravel.VertexColorUseAsAlbedo = false;
                matGravel.AlbedoColor = Colors.White;
            }
        }
    }
}
