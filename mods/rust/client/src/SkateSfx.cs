using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Shortcut.RustMod;
using UnityEngine;

public static class SkateSfx
{
    public const int Rate = 44100;
    public static bool Ready;
    public static string State = "not started";
    private static GameObject audio;
    private static readonly AudioClip[] ownedClips = new AudioClip[6];
    private static int ownedCount;
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
        if (ownedCount == ownedClips.Length) throw new InvalidOperationException("audio clip capacity exceeded");
        var clip = AudioClip.Create("skate_" + sound, samples.Length, 1, Rate, false);
        // Own the clip before upload so a failed setup can release it.
        ownedClips[ownedCount++] = clip;
        if (clip == null || !clip.SetData(samples, 0)) throw new InvalidOperationException(sound + ": clip upload failed");
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
            audio = new GameObject("skate_audio");
            UnityEngine.Object.DontDestroyOnLoad(audio);
            roll = Source(audio, Clip(SkateSound.Rolling, 11u), true);
            grind = Source(audio, Clip(SkateSound.Grind, 12u), true);
            shots = Source(audio, null, false);
            push = Clip(SkateSound.Push, 13u); ollie = Clip(SkateSound.Ollie, 14u); landing = Clip(SkateSound.Landing, 15u); bail = Clip(SkateSound.Bail, 16u);
            Sync();
            Ready = true; State = "ready";
            Out.Say("AUDIO ready: two loops and four one-shots at " + Rate + " Hz");
        }
        catch (Exception e) { Fail("failed", e); }
    }

    public static void Shutdown()
    {
        failed = !Cleanup();
        deaf = false; ranked = true;
        State = failed ? "cleanup incomplete" : "stopped";
        if (failed) Out.Err("AUDIO " + State);
    }

    private static void Fail(string phase, Exception e)
    {
        failed = true;
        State = phase + ": " + e.GetType().Name + ": " + e.Message;
        if (!Cleanup()) State += "; cleanup incomplete";
        Out.Say("AUDIO " + State);
    }

    private static bool Cleanup()
    {
        Ready = false;
        Silence(roll); Silence(grind); Silence(shots);
        var clean = DestroyOwned(audio);
        if (clean) { audio = null; roll = grind = shots = null; }
        for (var i = 0; i < ownedCount; i++)
        {
            if (DestroyOwned(ownedClips[i])) ownedClips[i] = null;
            else clean = false;
        }
        if (clean) ownedCount = 0;
        push = ollie = landing = bail = null;
        block = null;
        rollVolume = grindVolume = RollHeard = GrindHeard = ShotsHeard = 0f;
        pops = lands = bails = pushes = 0;
        return clean;
    }

    private static bool DestroyOwned(UnityEngine.Object value)
    {
        try { if (value != null) UnityEngine.Object.Destroy(value); return true; }
        catch (Exception) { return false; }
    }

    private static void Silence(AudioSource source)
    {
        try { if (source != null) source.volume = 0f; }
        catch (Exception) { }
        try { if (source != null) source.Stop(); }
        catch (Exception) { }
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
        catch (Exception e) { Fail("failed while playing", e); }
    }
}
