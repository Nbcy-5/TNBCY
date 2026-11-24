using Godot;

namespace TNBCY
{
    // Player settings, kept in user://settings.cfg so they survive restarts.
    public static class Settings
    {
        public static float CamSens = 1f;     // camera drag / zoom sensitivity, 0.3 .. 2.5
        public static int Shake = 2;          // ride shake on the camera: 0 off, 1 low, 2 normal, 3 high
        public static int Haptics = 1;        // phone vibration on rail joints and points: 0 off, 1 light, 2 normal
        public static int Gfx = 0;            // 0 auto, 1 fast, 2 balanced, 3 high, 4 ultra
        public static bool ShowFps;

        public static readonly string[] ShakeNames = { "Off", "Low", "Normal", "High" };
        public static readonly string[] HapticNames = { "Off", "Light", "Normal" };
        public static readonly string[] GfxNames = { "Auto", "Fast", "Balanced", "High", "Ultra" };

        const string FilePath = "user://settings.cfg";

        public static float ShakeK { get { return Shake == 0 ? 0f : (Shake == 1 ? .5f : (Shake == 2 ? 1f : 1.6f)); } }
        public static float HapticK { get { return Haptics == 0 ? 0f : (Haptics == 1 ? .5f : 1f); } }

        public static void Load()
        {
            var cf = new ConfigFile();
            if (cf.Load(FilePath) != Error.Ok) return;
            CamSens = Mathf.Clamp((float)cf.GetValue("camera", "sens", 1.0).AsDouble(), .3f, 2.5f);
            Shake = Mathf.Clamp(cf.GetValue("ride", "shake", 2).AsInt32(), 0, 3);
            Haptics = Mathf.Clamp(cf.GetValue("ride", "haptics", 1).AsInt32(), 0, 2);
            Gfx = Mathf.Clamp(cf.GetValue("graphics", "level", 0).AsInt32(), 0, 4);
            ShowFps = cf.GetValue("graphics", "fps", false).AsBool();
        }

        public static void Save()
        {
            var cf = new ConfigFile();
            cf.SetValue("camera", "sens", (double)CamSens);
            cf.SetValue("ride", "shake", Shake);
            cf.SetValue("ride", "haptics", Haptics);
            cf.SetValue("graphics", "level", Gfx);
            cf.SetValue("graphics", "fps", ShowFps);
            cf.Save(FilePath);
        }
    }
}
