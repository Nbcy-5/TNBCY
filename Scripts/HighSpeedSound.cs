using Godot;

namespace TNBCY
{
    // Looping running-train sound that is only heard while the train is faster than 95 km/h.
    // It fades in once the speed goes above StartKmh, gets louder the faster the train goes, and fades out when the speed drops below StopKmh.
    public partial class HighSpeedSound : Node
    {
        public const string SoundPath = "res://Assets/Audio/high_speed_loop.ogg";

        public float StartKmh = 95f;          // the sound starts when the speed goes above this
        public float StopKmh = 92f;           // and stops only when it drops below this (so it does not flicker on and off around 95)
        public float FadeInS = 1.5f, FadeOutS = 2.0f;
        public float MinVolume = .55f, MaxVolume = 1f, FullVolumeKmh = 140f;   // loudness at the start speed and at full speed (0..1)
        public bool PitchWithSpeed = false;   // true: slightly higher pitch when faster

        AudioStreamPlayer player;
        bool on;
        float gain;

        public override void _Ready()
        {
            var stream = GD.Load<AudioStream>(SoundPath);
            if (stream == null) { GD.PushWarning("HighSpeedSound: missing " + SoundPath); return; }
            if (stream is AudioStreamOggVorbis ogg) ogg.Loop = true;
            else if (stream is AudioStreamMP3 mp3) mp3.Loop = true;
            player = new AudioStreamPlayer { Stream = stream, VolumeDb = -80f };
            AddChild(player);
            player.Finished += () => { if (on) player.Play(); };   // safety net in case looping is not supported
        }

        // Call once per frame. silent = true forces the sound off (for example when the run has ended).
        public void Tick(float speedKmh, float dt, bool silent = false)
        {
            if (player == null) return;
            if (!silent && speedKmh > StartKmh) on = true;
            else if (silent || speedKmh < StopKmh) on = false;

            float k = Mathf.Clamp((speedKmh - StartKmh) / Mathf.Max(1f, FullVolumeKmh - StartKmh), 0f, 1f);
            float target = on ? Mathf.Lerp(MinVolume, MaxVolume, k) : 0f;
            float rate = target > gain ? 1f / Mathf.Max(.05f, FadeInS) : 1f / Mathf.Max(.05f, FadeOutS);
            gain = Mathf.MoveToward(gain, target, rate * dt);

            if (gain > .002f)
            {
                if (!player.Playing) player.Play();
                player.VolumeDb = Mathf.LinearToDb(gain);
                player.PitchScale = PitchWithSpeed ? .92f + .18f * k : 1f;
            }
            else if (player.Playing) player.Stop();
        }
    }
}
