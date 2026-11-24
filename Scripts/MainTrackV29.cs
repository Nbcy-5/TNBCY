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
                // Unit-width local mesh: BuildRealTrack scales it to 2.75 m exactly once.
                // The earlier 2.75 m local width was multiplied by the placement scale again,
                // making sleepers about 7.56 m wide and allowing them to reach the adjacent track.
                const int segments = 31;
                const float width = 1f;
                for (int i = 0; i < segments; i++)
                {
                    float x = -width * .5f + width * (i + .5f) / segments;
                    float nx = x / (width * .5f);
                    // About 2 cm of smooth centre dip after the .21 m placement scale.
                    float drop = -.10f * (1f - nx * nx);
                    float sx = width / segments + .001f;
                    AddBoxC(st, new Vector3(x, drop, 0f),
                        new Vector3(sx, 1f, 1f), Colors.White);
                }

                Color steel = new Color(.22f, .22f, .24f);
                for (int side = -1; side <= 1; side += 2)
                    for (int k = -1; k <= 1; k += 2)
                        AddBoxC(st, new Vector3(side * .3175f + k * .0382f, .5f, 0f),
                            new Vector3(.00655f, .14f, .38f), steel);
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
                    float g = Mathf.Clamp(.58f + (l - .30f) * .82f, .48f, .86f);

                    float u = x / (float)size, v = y / (float)size;
                    float broad = RTFbm(u, v, 4, 4, 521);
                    float detail = RTFbm(u + .17f, v + .31f, 11, 3, 522);
                    float mud = Mathf.Clamp((broad * .72f + detail * .28f - .60f) * 3.0f, 0f, .34f);
                    float oilField = RTFbm(u + .37f, v + .12f, 3, 4, 531);
                    float oil = Mathf.Clamp((oilField - .72f) * 2.4f, 0f, .10f);

                    // Light neutral granite with warm earth in the joints; no near-black oil wash.
                    float r = g * 1.025f, gg = g * 1.005f, b = g * .975f;
                    r = Mathf.Lerp(r, .43f, mud);
                    gg = Mathf.Lerp(gg, .36f, mud);
                    b = Mathf.Lerp(b, .28f, mud);
                    r = Mathf.Lerp(r, .29f, oil);
                    gg = Mathf.Lerp(gg, .27f, oil);
                    b = Mathf.Lerp(b, .24f, oil);
                    dst[i] = RTByte(r);
                    dst[i + 1] = RTByte(gg);
                    dst[i + 2] = RTByte(b);
                }
            return dst;
        }

        void RTV29Init()
        {
            // Keep the V28 full sleeper profile, adding only a very slight downward dip at its centre.
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
