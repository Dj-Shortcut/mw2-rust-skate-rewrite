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
    private static readonly AudioClip[] ownedClips = new AudioClip[7 + SkateFoley.Count];
    private static int ownedCount;
    private static AudioSource roll, grind, slide, shots, hollow, grit, whirr;
    private static AudioClip push, ollie, landing, bail;
    private static readonly AudioClip[] foley = new AudioClip[SkateFoley.Count];
    private static int pops, lands, bails, pushes, grindStarts, grindEnds, slams, reverts, stepUps;
    private static bool wasOn, wasAir, fell;
    private static float rollVolume, grindVolume, slideVolume, hollowVolume, gritVolume, whirrVolume, whirrPitch = 1f;
    // What the wheels are on, looked at a few times a second: a wooden park piece booms, dirt and grass
    // crunch, a foundation clacks at its seams every Seam metres.
    public enum Ground { Hard, Wood, Dirt, Build }
    public static Ground Surface = Ground.Hard;
    public const float SurfaceEvery = 0.1f, Seam = 3f;
    private static float surfaceAt, seamRun;
    private static int terrainLayer = -2, buildLayer = -2;
    public static float SlideVolumeForTest { get { return slideVolume; } }
    private static bool failed, deaf, ranked = true;
    private static Il2CppStructArray<float> block;
    public static float RollHeard, GrindHeard, ShotsHeard;

    private static AudioClip Clip(SkateSound sound, uint seed)
    {
        float[] samples; string error;
        if (!SkateAudio.TryCreate(sound, Rate, seed, out samples, out error)) throw new InvalidOperationException(sound + ": " + error);
        if (ownedCount == ownedClips.Length) throw new InvalidOperationException("audio clip capacity exceeded");
        var clip = Out.Keep(AudioClip.Create("skate_" + sound, samples.Length, 1, Rate, false));
        // Own the clip before upload so a failed setup can release it.
        ownedClips[ownedCount++] = clip;
        if (clip == null || !clip.SetData(samples, 0)) throw new InvalidOperationException(sound + ": clip upload failed");
        return clip;
    }

    private static AudioClip Clip(SkateFoleySound sound, uint seed)
    {
        if (ownedCount == ownedClips.Length) throw new InvalidOperationException("audio clip capacity exceeded");
        var samples = SkateFoley.Create(sound, Rate, seed);
        var clip = Out.Keep(AudioClip.Create("skate_" + sound, samples.Length, 1, Rate, false));
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
            slide = Source(audio, Clip(SkateSound.DeckSlide, 17u), true);
            shots = Source(audio, null, false);
            push = Clip(SkateSound.Push, 13u); ollie = Clip(SkateSound.Ollie, 14u); landing = Clip(SkateSound.Landing, 15u); bail = Clip(SkateSound.Bail, 16u);
            for (var i = 0; i < SkateFoley.Count; i++) if (!SkateFoley.IsLoop((SkateFoleySound)i)) foley[i] = Clip((SkateFoleySound)i, 21u + (uint)i);
            hollow = Source(audio, Clip(SkateFoleySound.Hollow, 31u), true);
            grit = Source(audio, Clip(SkateFoleySound.Grit, 32u), true);
            whirr = Source(audio, Clip(SkateFoleySound.Whirr, 33u), true);
            Sync(); wasOn = false;
            Ready = true; State = "ready";
            Out.Say("AUDIO ready: six loops and eleven one-shots at " + Rate + " Hz");
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
        Silence(roll); Silence(grind); Silence(slide); Silence(shots); Silence(hollow); Silence(grit); Silence(whirr);
        var clean = DestroyOwned(audio);
        if (clean) { audio = null; roll = grind = slide = shots = hollow = grit = whirr = null; }
        for (var i = 0; i < ownedCount; i++)
        {
            if (DestroyOwned(ownedClips[i])) ownedClips[i] = null;
            else clean = false;
        }
        if (clean) ownedCount = 0;
        push = ollie = landing = bail = null;
        for (var i = 0; i < foley.Length; i++) foley[i] = null;
        block = null;
        wasAir = false;
        rollVolume = grindVolume = slideVolume = hollowVolume = gritVolume = whirrVolume = RollHeard = GrindHeard = ShotsHeard = 0f;
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

    private static void Sync()
    {
        pops = SkateRide.Pops; lands = SkateRide.Lands; bails = SkateRide.Bails; pushes = SkateRide.Pushes;
        grindStarts = SkateRide.GrindStarts; grindEnds = SkateRide.GrindEnds; slams = SkateRide.WallSlams; reverts = SkateRide.Reverts; stepUps = SkateRide.StepUps;
    }

    private static void Play(SkateFoleySound sound, float volume) { var c = foley[(int)sound]; if (c != null) shots.PlayOneShot(c, volume); }

    // The nearest of the terrain, a building block or a park piece (the default layer) under the
    // board; anything else, rock and monument concrete, is hard ground.
    private static void Feel(float now)
    {
        if (now - surfaceAt < SurfaceEvery) return;
        surfaceAt = now;
        if (terrainLayer == -2) { try { terrainLayer = LayerMask.NameToLayer("Terrain"); buildLayer = LayerMask.NameToLayer("Construction"); } catch (Exception) { terrainLayer = buildLayer = -1; } }
        var from = SkateRide.Position + Vector3.up * 0.5f;
        RaycastHit hit;
        if (!Physics.Raycast(from, Vector3.down, out hit, 1f, SkateRide.GroundMask, QueryTriggerInteraction.Ignore)) return;
        var d = hit.m_Distance + 0.02f;
        if (Physics.Raycast(from, Vector3.down, out hit, d, 1, QueryTriggerInteraction.Ignore)) Surface = Ground.Wood;
        else if (buildLayer >= 0 && Physics.Raycast(from, Vector3.down, out hit, d, 1 << buildLayer, QueryTriggerInteraction.Ignore)) Surface = Ground.Build;
        else if (terrainLayer >= 0 && Physics.Raycast(from, Vector3.down, out hit, d, 1 << terrainLayer, QueryTriggerInteraction.Ignore)) Surface = Ground.Dirt;
        else Surface = Ground.Hard;
        if (Surface != said) { said = Surface; if (surfaceSays++ < 40) Out.Say("AUDIO wheels on " + Surface); }
    }
    private static Ground said = Ground.Hard; private static int surfaceSays;

    // The loudest sample each source has put out so far, as the engine reports it: what a scripted
    // check can know about sound without ears.
    public static void Listen()
    {
        if (!Ready || failed || deaf) return;
        try
        {
            if (block == null) block = new Il2CppStructArray<float>(256);
            RollHeard = Math.Max(RollHeard, Peak(roll)); GrindHeard = Math.Max(GrindHeard, Math.Max(Peak(grind), Peak(slide))); ShotsHeard = Math.Max(ShotsHeard, Peak(shots));
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
            if (float.IsNaN(speed) || float.IsInfinity(speed)) speed = 0f;
            if (float.IsNaN(frameSeconds) || float.IsInfinity(frameSeconds) || frameSeconds < 0f) frameSeconds = 0f;
            var ratio = SkateRide.Clamp(speed / SkateRide.MaxSpeed, 0f, 1f);
            var on = SkateRide.On;
            var onEdge = on && SkateRide.Mode == RideMode.Grind && speed > 0.1f;
            var deck = SkateRide.GrindKind == SkateGrindKind.Boardslide || SkateRide.GrindKind == SkateGrindKind.Lipslide ||
                SkateRide.GrindKind == SkateGrindKind.Noseslide || SkateRide.GrindKind == SkateGrindKind.Tailslide ||
                SkateRide.GrindKind == SkateGrindKind.Bluntslide || SkateRide.GrindKind == SkateGrindKind.Nosebluntslide || SkateRide.GrindKind == SkateGrindKind.Darkslide;
            var rolling = on && SkateRide.Mode == RideMode.Ground;
            // A landing is heard on what it lands on, not on what the board left.
            if (SkateRide.Lands != lands) surfaceAt = -99f;
            if (rolling) Feel(Time.time);
            var rollShare = rolling ? SkateRide.Clamp(speed / 9f, 0f, 1f) : 0f;
            var wantRoll = rollShare * (Surface == Ground.Dirt ? 0.4f : Surface == Ground.Wood ? 0.45f : 0.55f);
            var wantHollow = Surface == Ground.Wood ? rollShare * 0.5f : 0f;
            var wantGrit = Surface == Ground.Dirt ? rollShare * 0.45f : 0f;
            // Off the ground the wheels spin on at the speed they rolled and wind down; back on, they
            // are the rolling sound again.
            var airborne = on && SkateRide.Mode == RideMode.Air;
            if (airborne && !wasAir) { whirrVolume = 0.08f + ratio * 0.22f; whirrPitch = 0.75f + ratio * 0.5f; }
            wasAir = airborne;
            var edgeVolume = 0.3f + ratio * 0.3f;
            var wantGrind = onEdge && !deck ? edgeVolume : 0f;
            // A powerslide on the ground scrubs with the deck's own slide sound, louder the faster.
            var sliding = on && SkateRide.Mode == RideMode.Ground && SkateRide.Powerslide > 0.3f && speed > 0.8f;
            var wantSlide = onEdge && deck ? edgeVolume : sliding ? (0.25f + ratio * 0.35f) * SkateRide.Powerslide : 0f;
            var k = 1f - (float)Math.Exp(-12f * SkateRide.Clamp(frameSeconds, 0f, 0.1f));
            rollVolume += (wantRoll - rollVolume) * k;
            grindVolume += (wantGrind - grindVolume) * k; slideVolume += (wantSlide - slideVolume) * k;
            hollowVolume += (wantHollow - hollowVolume) * k; gritVolume += (wantGrit - gritVolume) * k;
            if (airborne) { whirrVolume *= (float)Math.Exp(-SkateRide.Clamp(frameSeconds, 0f, 0.1f) / 1.4f); whirrPitch = Math.Max(0.6f, whirrPitch - frameSeconds * 0.15f); }
            else whirrVolume += (0f - whirrVolume) * k;
            var pitch = 0.75f + ratio * 0.55f;
            roll.volume = rollVolume; roll.pitch = Surface == Ground.Dirt ? pitch * 0.85f : Surface == Ground.Wood ? pitch * 0.9f : pitch;
            hollow.volume = hollowVolume; hollow.pitch = 0.85f + ratio * 0.3f;
            grit.volume = gritVolume; grit.pitch = 0.8f + ratio * 0.5f;
            whirr.volume = whirrVolume; whirr.pitch = whirrPitch;
            grind.volume = grindVolume; slide.volume = slideVolume;
            grind.pitch = slide.pitch = 0.9f + ratio * 0.35f;
            if (!on)
            {
                // Stepped off: the board drops back onto its wheels. A bail has had its own sound.
                if (wasOn && !fell && SkateRide.Bails == bails) Play(SkateFoleySound.Drop, 0.35f);
                wasOn = false; Sync(); return;
            }
            if (!wasOn) { Play(SkateFoleySound.Drop, 0.55f); wasOn = true; fell = false; seamRun = 0f; }
            var bailed = SkateRide.Bails != bails;
            if (bailed) fell = true;
            if (bailed) shots.PlayOneShot(bail, 0.8f);
            else
            {
                var popped = SkateRide.Pops != pops;
                if (SkateRide.Pushes != pushes) shots.PlayOneShot(push, 0.4f);
                if (popped) shots.PlayOneShot(ollie, 0.8f);
                if (SkateRide.Lands != lands)
                {
                    var hard = SkateRide.Clamp(SkateRide.LandImpact / 15f, 0f, 1f);
                    shots.PlayOneShot(landing, SkateRide.Clamp(0.3f + hard, 0.3f, 0.85f));
                    // A big drop onto a ramp also booms in its boards.
                    if (Surface == Ground.Wood && hard > 0.2f) Play(SkateFoleySound.SlideIn, 0.3f + hard * 0.4f);
                }
                if (SkateRide.GrindStarts != grindStarts && SkateRide.GrindKind != SkateGrindKind.Handplant)
                    Play(deck ? SkateFoleySound.SlideIn : SkateFoleySound.GrindIn, 0.45f + ratio * 0.4f);
                if (SkateRide.GrindEnds != grindEnds && !popped) Play(SkateFoleySound.GrindOut, 0.5f);
                if (SkateRide.WallSlams != slams) Play(SkateFoleySound.Slam, 0.8f);
                if (SkateRide.Reverts != reverts) Play(SkateFoleySound.Scrape, 0.3f + ratio * 0.3f);
                if (SkateRide.StepUps != stepUps) Play(SkateFoleySound.Seam, 0.6f);
                if (rolling && Surface == Ground.Build && speed > 1f)
                {
                    seamRun += speed * frameSeconds;
                    if (seamRun >= Seam) { seamRun -= Seam; Play(SkateFoleySound.Seam, 0.2f + ratio * 0.35f); }
                }
            }
            Sync();
        }
        catch (Exception e) { Fail("failed while playing", e); }
    }
}
