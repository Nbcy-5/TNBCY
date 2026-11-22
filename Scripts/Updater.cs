using Godot;
using System.Text.Json;

namespace TNBCY
{
    // In-game updater (C# only).
    //  * New content (routes, trains, numbers): downloads content.pck and loads it, no new APK needed.
    //  * New app version (C# code changes): shows a message and opens the APK download link.
    public partial class Updater : Node
    {
        public const int ApkVersion = 17;   // keep equal to version/code in the Android export preset
        const int ContentScale = 100;      // content version = app version * 100 + patch number (content_patch.txt)
        static int BuiltInContent { get { return ApkVersion * ContentScale; } }
        const string CfgPath = "user://update.cfg", PackFile = "content.pck", PackPath = "user://content.pck";

        public class Remote { public int apkVersion; public int contentVersion; public string apkUrl = ""; public string pckUrl = ""; public string notes = ""; }
        public class Config { public string versionUrl = ""; }

        public System.Action<string> Message;               // status text
        public System.Action<string, string> ApkAvailable;  // notes, url
        HttpRequest http; Remote remote;

        // Call before loading any game data so downloaded content overrides the built-in files.
        public static void LoadSavedPack()
        {
            if (!Godot.FileAccess.FileExists(PackPath)) return;
            if (SavedVersion() < BuiltInContent)   // the installed app is newer than the downloaded pack: the app's own data wins, drop the old pack
            {
                using (var d = DirAccess.Open("user://")) { if (d != null) d.Remove(PackFile); }
                return;
            }
            ProjectSettings.LoadResourcePack(PackPath);
        }
        static int SavedVersion()
        {
            var cf = new ConfigFile();
            return cf.Load(CfgPath) == Error.Ok ? cf.GetValue("content", "version", 0).AsInt32() : 0;
        }
        static int LocalVersion() { return System.Math.Max(SavedVersion(), BuiltInContent); }   // what is built into the app counts as already installed
        static void SaveVersion(int v)
        {
            var cf = new ConfigFile(); cf.Load(CfgPath); cf.SetValue("content", "version", v); cf.Save(CfgPath);
        }

        public override void _Ready()
        {
            Config cfg;
            try
            {
                using var f = Godot.FileAccess.Open("res://Data/update_config.json", Godot.FileAccess.ModeFlags.Read);
                cfg = JsonSerializer.Deserialize<Config>(f.GetAsText(), new JsonSerializerOptions { IncludeFields = true });
            }
            catch { return; }
            if (cfg == null || string.IsNullOrWhiteSpace(cfg.versionUrl)) return;   // updates switched off until a URL is set

            http = new HttpRequest { Timeout = 15 }; AddChild(http);
            http.RequestCompleted += OnVersion;
            http.Request(cfg.versionUrl + (cfg.versionUrl.Contains("?") ? "&" : "?") + "t=" + Time.GetTicksMsec());
        }

        void OnVersion(long result, long code, string[] headers, byte[] body)
        {
            http.RequestCompleted -= OnVersion;
            if (result != 0 || code != 200) return;      // offline or server problem: just keep playing
            try { remote = JsonSerializer.Deserialize<Remote>(System.Text.Encoding.UTF8.GetString(body), new JsonSerializerOptions { IncludeFields = true }); }
            catch { return; }
            if (remote == null) return;

            if (remote.apkVersion > ApkVersion && !string.IsNullOrEmpty(remote.apkUrl)) { ApkAvailable?.Invoke(remote.notes, remote.apkUrl); return; }
            if (remote.contentVersion > LocalVersion() && !string.IsNullOrEmpty(remote.pckUrl))
            {
                Message?.Invoke("Downloading update...");
                http.Timeout = 0; http.DownloadFile = PackPath + ".tmp";
                http.RequestCompleted += OnPack; http.Request(remote.pckUrl);
            }
        }

        void OnPack(long result, long code, string[] headers, byte[] body)
        {
            http.RequestCompleted -= OnPack;
            if (result != 0 || code != 200) { Message?.Invoke("Update download failed"); return; }
            using (var d = DirAccess.Open("user://"))
            {
                if (d.FileExists(PackFile)) d.Remove(PackFile);
                d.Rename(PackFile + ".tmp", PackFile);
            }
            SaveVersion(remote.contentVersion);
            ProjectSettings.LoadResourcePack(PackPath);
            Message?.Invoke("Update installed");
            GetTree().ReloadCurrentScene();
        }
    }
}
