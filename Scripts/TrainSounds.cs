using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TNBCY
{
    // ---- Settings read from Data/train_sounds.json (add or change sounds there; no code change needed) ----

    public class SoundPitch
    {
        public float RefKmh = 0f;     // speed at which the recording plays at normal pitch
        public float Power = .6f;     // 0 = pitch never changes, 1 = pitch follows speed exactly
        public float Min = .8f, Max = 1.3f;
    }

    public class SoundLayer           // a looping sound that is heard only inside a speed range
    {
        public string Id = "";
        public string File = "";
        public bool Enabled = true;
        public float[] Fade = new float[0];            // [from, fullFrom, fullTo, to] in km/h: silent below "from", full between the middle two, silent above "to"
        public float VolumeDb = 0f;
        public float[][] VolumeCurve = null;           // optional [[km/h, dB], ...]: volume changes with speed (replaces VolumeDb)
        public SoundPitch Pitch = null;                // optional: pitch follows speed
    }

    public class OneShotSpec          // a sound played on request: sounds.Play("id")
    {
        public string Id = "";
        public string File = "";
        public bool Enabled = true;
        public float VolumeDb = 0f;
        public float CooldownS = 0f;                   // minimum time between two plays
        public float PitchMin = 1f, PitchMax = 1f;     // random pitch for variety
    }

    public class SoundConfig
    {
        public float MasterDb = 0f;
        public float SmoothS = .2f;                    // how quickly volume follows speed changes
        public List<SoundLayer> Layers = new List<SoundLayer>();
        public List<OneShotSpec> OneShots = new List<OneShotSpec>();
    }

    // Speed-driven train sound. Layers crossfade with equal power, so neighbouring bands blend without a dip in loudness.
    //   var sounds = new TrainSounds(); AddChild(sounds);                // once
    //   sounds.Tick(speedKmh, deltaSeconds, runEnded);                   // every frame
    //   sounds.Play("horn");                                             // one-shot sounds from the file
    public partial class TrainSounds : Node
    {
        public const string ConfigPath = "res://Data/train_sounds.json";

        class Live { public SoundLayer Spec; public AudioStreamPlayer Player; public float Gain; }

        readonly List<Live> layers = new List<Live>();
        readonly Dictionary<string, OneShotSpec> shots = new Dictionary<string, OneShotSpec>();
        readonly Dictionary<string, AudioStream> shotStreams = new Dictionary<string, AudioStream>();
        readonly Dictionary<string, double> lastShot = new Dictionary<string, double>();
        SoundConfig cfg = new SoundConfig();
        readonly Random rng = new Random();

        public override void _Ready()
        {
            try
            {
                using var f = Godot.FileAccess.Open(ConfigPath, Godot.FileAccess.ModeFlags.Read);
                cfg = JsonSerializer.Deserialize<SoundConfig>(f.GetAsText(), new JsonSerializerOptions { IncludeFields = true, PropertyNameCaseInsensitive = true }) ?? cfg;
            }
            catch (Exception ex) { GD.PushWarning("TrainSounds: cannot read " + ConfigPath + " (" + ex.Message + ")"); return; }

            foreach (var spec in cfg.Layers)
            {
                if (!spec.Enabled || spec.Fade == null || spec.Fade.Length != 4) continue;
                var stream = LoadStream(spec.File, true);
                if (stream == null) continue;
                var p = new AudioStreamPlayer { Stream = stream, VolumeDb = -80f };
                AddChild(p);
                layers.Add(new Live { Spec = spec, Player = p });
            }
            foreach (var s in cfg.OneShots)
            {
                if (!s.Enabled || string.IsNullOrEmpty(s.Id)) continue;
                var stream = LoadStream(s.File, false);
                if (stream == null) continue;
                shots[s.Id] = s; shotStreams[s.Id] = stream;
            }
        }

        static AudioStream LoadStream(string path, bool loop)
        {
            if (string.IsNullOrEmpty(path) || !ResourceLoader.Exists(path)) { GD.PushWarning("TrainSounds: missing sound " + path); return null; }
            var stream = GD.Load<AudioStream>(path);
            if (stream == null) return null;
            if (loop)
            {
                if (stream is AudioStreamOggVorbis ogg) ogg.Loop = true;
                else if (stream is AudioStreamMP3 mp3) mp3.Loop = true;
            }
            return stream;
        }

        // 0..1 for a speed, using sine/cosine ramps so two crossfading layers keep the same total power
        public static float FadeGain(float[] f, float v)
        {
            if (v <= f[0] || v >= f[3]) return 0f;
            if (v < f[1]) return Mathf.Sin(Mathf.Clamp((v - f[0]) / Mathf.Max(.001f, f[1] - f[0]), 0f, 1f) * Mathf.Pi * .5f);
            if (v <= f[2]) return 1f;
            return Mathf.Cos(Mathf.Clamp((v - f[2]) / Mathf.Max(.001f, f[3] - f[2]), 0f, 1f) * Mathf.Pi * .5f);
        }

        static float CurveDb(float[][] pts, float v)
        {
            if (pts.Length == 0) return 0f;
            if (v <= pts[0][0]) return pts[0][1];
            for (int i = 1; i < pts.Length; i++)
                if (v <= pts[i][0]) return Mathf.Lerp(pts[i - 1][1], pts[i][1], (v - pts[i - 1][0]) / Mathf.Max(.001f, pts[i][0] - pts[i - 1][0]));
            return pts[pts.Length - 1][1];
        }

        // Call once per frame. silent = true fades everything out (for example when the run has ended).
        public void Tick(float speedKmh, float dt, bool silent = false)
        {
            float step = dt / Mathf.Max(.02f, cfg.SmoothS);
            foreach (var l in layers)
            {
                var s = l.Spec;
                float target = silent ? 0f : FadeGain(s.Fade, speedKmh);
                l.Gain = Mathf.MoveToward(l.Gain, target, step);
                if (l.Gain > .003f)
                {
                    float db = s.VolumeCurve != null && s.VolumeCurve.Length > 0 ? CurveDb(s.VolumeCurve, speedKmh) : s.VolumeDb;
                    if (!l.Player.Playing) l.Player.Play();
                    l.Player.VolumeDb = Mathf.LinearToDb(l.Gain) + db + cfg.MasterDb;
                    if (s.Pitch != null && s.Pitch.RefKmh > 0f)
                        l.Player.PitchScale = Mathf.Clamp(Mathf.Pow(Mathf.Max(speedKmh, 1f) / s.Pitch.RefKmh, s.Pitch.Power), s.Pitch.Min, s.Pitch.Max);
                }
                else if (l.Player.Playing) l.Player.Stop();
            }
        }

        // Play a one-shot sound defined in the config. Returns false if it is unknown or still cooling down.
        public bool Play(string id, float extraDb = 0f)
        {
            if (!shots.TryGetValue(id, out var s)) return false;
            double now = Time.GetTicksMsec() / 1000.0;
            if (lastShot.TryGetValue(id, out var last) && now - last < s.CooldownS) return false;
            lastShot[id] = now;
            var p = new AudioStreamPlayer { Stream = shotStreams[id], VolumeDb = s.VolumeDb + extraDb + cfg.MasterDb };
            if (s.PitchMax > s.PitchMin) p.PitchScale = s.PitchMin + (float)rng.NextDouble() * (s.PitchMax - s.PitchMin);
            AddChild(p);
            p.Finished += () => p.QueueFree();
            p.Play();
            return true;
        }
    }
}
