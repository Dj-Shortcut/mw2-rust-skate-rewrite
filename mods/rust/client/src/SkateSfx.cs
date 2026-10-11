using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Shortcut.RustMod;
using UnityEngine;

public static class SkateSfx
{
    public const int Rate = 44100;
    public static bool Ready;
    public static string State = "not started";
    private static AudioSource roll, grind, shots;
    private static AudioClip push, ollie, landing, bail;
    private static int pops, lands, bails, pushes;
    private static float rollVolume, grindVolume;
    private static bool failed, deaf, ranked = true;
    private static Il2CppStructArray<float> block;
    public static float RollHeard, GrindHeard, ShotsHeard;

    private static AudioClip Clip(SkateSound sound, uint seed)
    {
        float[] samples; string error;
        if (!SkateAudio.TryCreate(sound, Rate, seed, out samples, out error)) throw new InvalidOperationException(sound + ": " + error);
        var clip = AudioClip.Create("skate_" + sound, samples.Length, 1, Rate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static AudioSource Source(GameObject go, AudioClip clip, bool loop)
    {
        var s = go.AddComponent<AudioSource>();
        s.spatialBlend = 0f; s.loop = loop; s.volume = loop ? 0f : 1f;
        if (ranked)
        {
            // When more sources play than the engine mixes, it drops the least important ones first.
            try { s.priority = 16; }
            catch (Exception e) { ranked = false; Out.Say("AUDIO source priority unavailable: " + e.GetType().Name + ": " + e.Message); }
        }
        if (clip != null) { s.clip = clip; s.Play(); }
        return s;
    }

    public static void Start()
    {
        if (Ready || failed) return;
        try
        {
            var go = new GameObject("skate_audio");
            UnityEngine.Object.DontDestroyOnLoad(go);
            roll = Source(go, Clip(SkateSound.Rolling, 11u), true);
            grind = Source(go, Clip(SkateSound.Grind, 12u), true);
            shots = Source(go, null, false);
            push = Clip(SkateSound.Push, 13u); ollie = Clip(SkateSound.Ollie, 14u); landing = Clip(SkateSound.Landing, 15u); bail = Clip(SkateSound.Bail, 16u);
            Sync();
            Ready = true; State = "ready";
            Out.Say("AUDIO ready: two loops and four one-shots at " + Rate + " Hz");
            Survey();
        }
        catch (Exception e) { failed = true; State = "failed: " + e.GetType().Name + ": " + e.Message; Out.Say("AUDIO " + State); Silence(); }
    }

    // Which of the game's own clips are in memory: skateboard-like ones could replace the synthesis.
    // Names only; nothing here names a type of the game.
    private static void Survey()
    {
        try
        {
            var all = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<AudioClip>());
            var like = new System.Text.StringBuilder(); var n = 0; var liked = 0;
            var words = new[] { "skate", "wheel", "scrape", "metal", "wood", "impact", "slide", "roll", "concrete", "footstep", "land", "thud", "grind", "drag", "stone" };
            for (var i = 0; i < all.Length; i++)
            {
                var c = all[i] == null ? null : all[i].TryCast<AudioClip>();
                if (c == null) continue;
                n++;
                var name = (c.name ?? "").ToLowerInvariant(); var hit = false;
                for (var w = 0; w < words.Length && !hit; w++) hit = name.Contains(words[w]);
                if (hit && like.Length < 1500) { liked++; like.Append(liked > 1 ? ", " : "").Append(c.name).Append('(').Append(c.length.ToString("F1")).Append("s)"); }
            }
            Out.Say("AUDIO clips in memory: " + n + ", of a skateboard's kind " + liked + ": " + like);
        }
        catch (Exception e) { Out.Say("AUDIO clips could not be listed: " + e.GetType().Name + ": " + e.Message); }
    }

    private static void Silence()
    {
        foreach (var source in new[] { roll, grind, shots })
        {
            try { if (source != null) { source.volume = 0f; source.Stop(); } }
            catch (Exception) { }
        }
    }

    private static void Sync() { pops = SkateRide.Pops; lands = SkateRide.Lands; bails = SkateRide.Bails; pushes = SkateRide.Pushes; }

    // The loudest sample each source has put out so far, as the engine reports it: what a scripted
    // check can know about sound without ears.
    public static void Listen()
    {
        if (!Ready || failed || deaf) return;
        try
        {
            if (block == null) block = new Il2CppStructArray<float>(256);
            RollHeard = Math.Max(RollHeard, Peak(roll)); GrindHeard = Math.Max(GrindHeard, Peak(grind)); ShotsHeard = Math.Max(ShotsHeard, Peak(shots));
        }
        catch (Exception e) { deaf = true; Out.Say("AUDIO output cannot be read back: " + e.GetType().Name + ": " + e.Message); }
    }

    private static float Peak(AudioSource source)
    {
        source.GetOutputData(block, 0);
        var peak = 0f;
        for (var i = 0; i < block.Length; i++) { var v = Math.Abs(block[i]); if (v > peak) peak = v; }
        return peak;
    }

    // The engine reports the output after the listener's volume; the figures are scaled back to full volume.
    public static string Heard
    {
        get
        {
            if (deaf) return "unreadable";
            var listener = AudioListener.volume;
            if (listener < 0.0001f) return "nothing, the listener is at zero";
            return "roll " + (RollHeard / listener).ToString("F3") + " grind " + (GrindHeard / listener).ToString("F3") + " one-shots " + (ShotsHeard / listener).ToString("F3") + " of full scale (listener at " + listener.ToString("F3") + ")";
        }
    }

    public static void Update(float frameSeconds)
    {
        if (!Ready || failed) return;
        try
        {
            var speed = Math.Abs(SkateRide.Speed);
            var wantRoll = SkateRide.Mode == RideMode.Ground ? SkateRide.Clamp(speed / 9f, 0f, 1f) * 0.55f : 0f;
            var wantGrind = SkateRide.Mode == RideMode.Grind ? 0.6f : 0f;
            var k = SkateRide.Clamp(frameSeconds * 12f, 0f, 1f);
            rollVolume += (wantRoll - rollVolume) * k; grindVolume += (wantGrind - grindVolume) * k;
            roll.volume = rollVolume; roll.pitch = 0.75f + speed / 13f * 0.7f;
            grind.volume = grindVolume; grind.pitch = 0.85f + speed / 13f * 0.4f;
            if (!SkateRide.On) { Sync(); return; }
            if (SkateRide.Pushes != pushes) shots.PlayOneShot(push, 0.5f);
            if (SkateRide.Pops != pops) shots.PlayOneShot(ollie, 0.9f);
            if (SkateRide.Lands != lands) shots.PlayOneShot(landing, SkateRide.Clamp(0.35f + SkateRide.LandImpact / 10f, 0.35f, 1f));
            if (SkateRide.Bails != bails) shots.PlayOneShot(bail, 1f);
            Sync();
        }
        catch (Exception e) { failed = true; State = "failed while playing: " + e.GetType().Name + ": " + e.Message; Out.Say("AUDIO " + State); Silence(); }
    }
}
