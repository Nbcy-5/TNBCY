using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TNBCY
{
    // Seamlessly tiling textures made in code (no image files). They are generated on worker threads
    // while the train menu is on screen, so starting the game does not stall.
    public static class ProcTex
    {
        static readonly Dictionary<string, Task<byte[]>> jobs = new Dictionary<string, Task<byte[]>>();

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }
        static int Wrap(int v, int p) { v %= p; return v < 0 ? v + p : v; }

        static float VNoise(float x, float y, int p, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi; fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            int x0 = Wrap(xi, p), x1 = Wrap(xi + 1, p), y0 = Wrap(yi, p), y1 = Wrap(yi + 1, p);
            float a = Hash(x0, y0, seed), b = Hash(x1, y0, seed), c = Hash(x0, y1, seed), d = Hash(x1, y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(float u, float v, int basePeriod, int oct, int seed)
        {
            float sum = 0f, amp = .5f, norm = 0f; int p = basePeriod;
            for (int o = 0; o < oct; o++) { sum += amp * VNoise(u * p, v * p, p, seed + o * 17); norm += amp; amp *= .5f; p *= 2; }
            return sum / norm;
        }

        static byte[] Bytes(int size, System.Func<float, float, Color> f)
        {
            var data = new byte[size * size * 3];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Color c = f(x / (float)size, y / (float)size); int i = (y * size + x) * 3;
                    data[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(c.R * 255f), 0, 255);
                    data[i + 1] = (byte)Mathf.Clamp(Mathf.RoundToInt(c.G * 255f), 0, 255);
                    data[i + 2] = (byte)Mathf.Clamp(Mathf.RoundToInt(c.B * 255f), 0, 255);
                }
            return data;
        }

        static void Job(string name, int size, System.Func<float, float, Color> f)
        {
            lock (jobs) { if (!jobs.ContainsKey(name)) jobs[name] = Task.Run(() => Bytes(size, f)); }
        }

        static ImageTexture Get(string name, int size, System.Func<float, float, Color> f)
        {
            Job(name, size, f);
            Task<byte[]> t; lock (jobs) { t = jobs[name]; }
            var img = Image.CreateFromData(size, size, false, Image.Format.Rgb8, t.Result);
            img.GenerateMipmaps();
            return ImageTexture.CreateFromImage(img);
        }

        public static void Prefetch()
        {
            Job("grass", 512, GrassFn); Job("dirt", 512, DirtFn); Job("gravel", 512, GravelFn);
            Job("concrete", 256, ConcreteFn); Job("macro", 256, MacroFn);
        }

        public static ImageTexture Grass() { return Get("grass", 512, GrassFn); }
        public static ImageTexture Dirt() { return Get("dirt", 512, DirtFn); }
        public static ImageTexture Gravel() { return Get("gravel", 512, GravelFn); }
        public static ImageTexture Concrete() { return Get("concrete", 256, ConcreteFn); }
        public static ImageTexture Macro() { return Get("macro", 256, MacroFn); }

        static Color GrassFn(float u, float v)
        {
            float n = Fbm(u, v, 4, 5, 3), fine = Hash((int)(u * 512), (int)(v * 512), 9), blade = Hash((int)(u * 512), (int)(v * 128), 19);
            var dark = new Color(.13f, .24f, .07f); var mid = new Color(.27f, .43f, .12f); var dry = new Color(.52f, .50f, .22f);
            Color c = n < .5f ? dark.Lerp(mid, n * 2f) : mid.Lerp(dry, (n - .5f) * 1.3f);
            float k = .74f + .3f * fine + .2f * blade; if (fine < .06f) k *= .6f;
            return new Color(c.R * k, c.G * k, c.B * k);
        }

        static Color DirtFn(float u, float v)
        {
            float n = Fbm(u, v, 3, 5, 21), wet = Fbm(u + .31f, v + .17f, 2, 4, 33), fine = Hash((int)(u * 512), (int)(v * 512), 5);
            Color c = new Color(.19f, .13f, .08f).Lerp(new Color(.43f, .32f, .21f), n);
            float k = (.82f + .3f * fine) * (wet > .58f ? .72f : 1f);
            if (fine > .993f) return new Color(.5f, .49f, .46f);
            return new Color(c.R * k, c.G * k, c.B * k);
        }

        // Indian broad-gauge ballast: angular granite chips with warm Telangana red-soil fines.
        // Replaces the old near-black grayscale gravel.
        static Color GravelFn(float u, float v)
        {
            const int cells = 46; const int seed = 117;
            float x = u * cells, y = v * cells;
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float d1 = 9f, d2 = 9f; int bx = 0, by = 0;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    int cx = xi + i, cy = yi + j;
                    int wx = Wrap(cx, cells), wy = Wrap(cy, cells);
                    float px = cx + Hash(wx, wy, seed) * .78f + .11f;
                    float py = cy + Hash(wx, wy, seed + 1) * .78f + .11f;
                    float dx = px - x, dy = py - y;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d < d1) { d2 = d1; d1 = d; bx = wx; by = wy; }
                    else if (d < d2) d2 = d;
                }

            float stone = Hash(bx, by, seed + 2);
            float tint = Hash(bx, by, seed + 3);
            float edge = Mathf.Clamp((d2 - d1) * 2.8f, .12f, 1f);
            float fine = Hash((int)(u * 512f), (int)(v * 512f), seed + 9);

            Color granite = new Color(.39f, .38f, .35f);
            Color pinkGranite = new Color(.48f, .34f, .29f);
            Color paleStone = new Color(.57f, .54f, .47f);
            Color redEarth = new Color(.34f, .20f, .13f);
            Color c = stone < .58f ? granite : (stone < .78f ? pinkGranite : paleStone);
            if (fine > .80f) c = c.Lerp(redEarth, .28f);
            float shade = (.72f + .42f * tint) * (.78f + .22f * edge);
            float micro = .94f + .12f * fine;
            return new Color(c.R * shade * micro, c.G * shade * micro, c.B * shade * micro);
        }

        static Color ConcreteFn(float u, float v)
        {
            float n = Fbm(u, v, 4, 4, 41), fine = Hash((int)(u * 256), (int)(v * 256), 7);
            float k = .58f + .14f * n + .06f * (fine - .5f);
            return new Color(k, k * .985f, k * .95f);
        }

        // large-scale variation used by the ground shader to place mud patches and colour drift
        static Color MacroFn(float u, float v)
        {
            return new Color(Fbm(u, v, 4, 5, 61), Fbm(u, v, 12, 4, 71), Fbm(u, v, 24, 3, 81));
        }
    }
}
