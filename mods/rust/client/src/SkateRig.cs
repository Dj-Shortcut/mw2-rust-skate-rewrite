// The client's game code is obfuscated: only Unity engine API, game component type names and
// Unity message names can be relied on. The local player is moved by a separate object (the walk
// component, a dynamic Rigidbody, a capsule) that is replaced on death and reconnect, so it is
// looked up again and gets a fresh late driver each time.
using System;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

public class SkateRig : MonoBehaviour
{
    public SkateRig(IntPtr p) : base(p) { }

    // Not lower: the engine stops mixing a source that is too quiet to be heard, and a scripted check
    // could no longer tell whether the board's sounds play. The rolling loop at walking pace is the
    // quietest of them.
    public const float MuteVolume = 0.004f;
    public static bool Mute;
    public static BasePlayer Local;
    public static Transform LocalT;
    public static Rigidbody Body;
    public static PlayerWalkMovement Walk;
    public static float AwakeAt = -1f;
    public static bool Ready { get { return Body != null && AwakeAt > 0f; } }
    public static bool CanMount { get { return Ready && late != null && Walk != null && Walk.enabled && !SkateRide.On && Time.realtimeSinceStartup - offAt > 0.7f; } }

    private static GameObject board;
    private static LateDriver late;
    private static float offAt = -99f, lastFrame;
    private static bool wasOn;
    private float nextTick, nextLog, lateRetryAt, lastJumpTap = -1f, crouchSince = -1f;
    private int standTicks, lateFailures;
    private bool tracking, lateAdded, hudFailed;

    private static void Say(string m) { Out.Say("SKATE " + m); }

    private void Update()
    {
        var now = Time.realtimeSinceStartup;
        SkateClock.Frame();
        var t0 = SkateClock.Now(); SkateKeys.Poll(); SkateConsole.Update(Time.realtimeSinceStartup); SkateClock.Add(SkateClock.Keys, t0);
        if (SkateRide.On)
        {
            // The game switches its walk component off when something else moves the player (a seat, a vehicle).
            if (Walk == null) SkateRide.Dismount("the movement object is gone");
            else if (!Walk.enabled) SkateRide.Dismount("the game took over the movement");
        }
        if ((!Scenarios.Active || Scenarios.Plays) && Ready)
        {
            if (SkateRide.On)
            {
                if (SkateKeys.CrouchTap && SkateRide.Mode == RideMode.Ground) crouchSince = now;
                if (!SkateKeys.Crouch || SkateRide.Mode != RideMode.Ground) crouchSince = -1f;
                if (SkateKeys.ToggleTap || (crouchSince > 0f && now - crouchSince > 0.6f)) { crouchSince = -1f; SkateRide.Dismount("stepped off"); }
                if (SkateKeys.CameraTap) SkateCamera.Chase = !SkateCamera.Chase;
            }
            else
            {
                var twice = false;
                if (SkateKeys.JumpTap) { twice = now - lastJumpTap < 0.4f; lastJumpTap = twice ? -1f : now; }
                if ((SkateKeys.ToggleTap || twice) && CanMount) MountNow(SkateCamera.LookYaw);
            }
        }
        if (SkateRide.On && now >= nextLog) { nextLog = now + 1f; Say(SkateRide.Status() + " | " + SkateClock.Report()); }
        if (wasOn && !SkateRide.On) { offAt = now; Say("OFF (" + SkateRide.OffReason + ") top=" + SkateRide.Top.ToString("F1") + " pull-backs=" + SkateRide.Resets + " score=" + SkateRide.Trick.TotalPoints); }
        wasOn = SkateRide.On;
        if (Scenarios.Active && Ready && late != null)
        {
            try { Scenarios.Update(now); }
            catch (Exception e) { Say("scenario threw " + e.GetType().Name + ": " + e.Message); Scenarios.Abort(); }
        }
        SkateSfx.Update(Time.deltaTime);
        if (now < nextTick) return;
        nextTick = now + 1f;
        try { Tick(now); }
        catch (Exception e) { Say("tick threw: " + e.GetType().Name + ": " + e.Message); nextTick = now + 5f; }
    }

    public static void MountNow(float yaw)
    {
        if (Body == null) return;
        if (!RiderRig.Bound) { try { RiderRig.Bind(LocalT); } catch (Exception e) { Out.Say("RIDER bind threw " + e.GetType().Name + ": " + e.Message); } }
        SkateSfx.Start();
        SkateRide.Mount(Body, yaw);
        SkateHud.MountedAt = Time.realtimeSinceStartup;
        Say("MOUNT yaw=" + SkateRide.Yaw.ToString("F0") + " speed=" + SkateRide.Speed.ToString("F1") + " rider=" + RiderRig.Bound + " audio=" + SkateSfx.State);
    }

    [HideFromIl2Cpp]
    private void Tick(float now)
    {
        if (Mute) AudioListener.volume = MuteVolume;
        var cam = Camera.main;
        if (cam == null) return;
        var cp = cam.transform.position;
        if (tracking && (Local == null || Body == null || Walk == null))
        {
            Say("the player or its movement object is gone; looking again");
            SkateRide.Dismount("the movement object is gone"); RiderRig.Unbind();
            if (Local == null) SkateRide.ForgetServer();
            // The old driver is queued behind the old walk component, not the next one.
            if (late != null) { UnityEngine.Object.Destroy(late); late = null; }
            Local = null; LocalT = null; Walk = null; Body = null; AwakeAt = -1f; standTicks = 0; tracking = lateAdded = false; lateFailures = 0;
        }
        if (Local == null)
        {
            var players = UnityEngine.Object.FindObjectsOfType<BasePlayer>();
            BasePlayer best = null; var bestD = 999f;
            for (var i = 0; i < players.Length; i++)
            {
                var d = Vector3.Distance(players[i].transform.position, cp);
                if (d < bestD) { bestD = d; best = players[i]; }
            }
            if (best == null || bestD > 4f) return;
            var walks = UnityEngine.Object.FindObjectsOfType<PlayerWalkMovement>();
            PlayerWalkMovement w = null; var wd = 999f;
            for (var i = 0; i < walks.Length; i++)
            {
                var d = Vector3.Distance(walks[i].transform.position, cp);
                if (d < wd) { wd = d; w = walks[i]; }
            }
            if (w == null || wd > 4f) return;
            var rb = w.gameObject.GetComponent<Rigidbody>();
            if (rb == null) return;
            Local = best; LocalT = best.transform; Walk = w; Body = rb; tracking = true;
            Say("LOCAL found player=" + Out.V1(LocalT.position));
            if (board == null) { try { board = SkateBoard.Build(); board.SetActive(false); } catch (Exception e) { Say("board threw " + e.GetType().Name + ": " + e.Message); } }
            return;
        }
        if (AwakeAt < 0f)
        {
            // The camera lies on the ground while the player sleeps.
            standTicks = cp.y - LocalT.position.y > 1.0f ? standTicks + 1 : 0;
            if (standTicks >= 2) { AwakeAt = now; Say("AWAKE player=" + Out.V1(LocalT.position) + (Scenarios.Active ? " | scenario " + Scenarios.Name : " | K or a double jump gets on")); }
            return;
        }
        if (!lateAdded && lateFailures < 5 && now - AwakeAt >= 2f && now >= lateRetryAt)
        {
            // A fresh driver for every movement object, so that it is always queued behind that object's walk component.
            try
            {
                late = SkatePlugin.Instance.AddComponent<LateDriver>();
                lateAdded = late != null;
                Say("late driver added=" + lateAdded);
            }
            catch (Exception e) { late = null; Say("late driver threw " + e.GetType().Name + ": " + e.Message); }
            if (!lateAdded) { lateFailures++; lateRetryAt = now + 3f; }
        }
        if (SkateRide.On && !RiderRig.Bound) { try { RiderRig.Bind(LocalT); } catch (Exception e) { Say("rider bind threw " + e.GetType().Name + ": " + e.Message); } }
    }

    public static void LateFrame()
    {
        var now = Time.realtimeSinceStartup;
        var frame = SkateRide.Clamp(now - lastFrame, 0.001f, 0.1f); lastFrame = now;
        SkateCamera.Sample();
        // With a controller the rider looks where the board goes, a little into the turn; the view
        // itself is turned by the right stick's flicks and says nothing.
        if (!SkateKeys.Scripted) SkateKeys.LookYaw = SkateKeys.Pad && SkateRide.On ? SkateRide.Yaw + SkateKeys.Steer * 35f : SkateCamera.LookYaw;
        SkateBoard.Tip += ((!SkateRide.On ? 0f : SkateRide.Stalling && SkateRide.GrindKind == Shortcut.RustMod.SkateGrindKind.TailStall ? SkateBoard.StallTip
            : SkateRide.Stalling && SkateRide.GrindKind == Shortcut.RustMod.SkateGrindKind.NoseStall ? -SkateBoard.StallTip : SkateRide.Manual ? 1f : SkateRide.NoseManual ? -1f : 0f) - SkateBoard.Tip) * SkateRide.Clamp(frame * 9f, 0f, 1f);
        // In a jump the knees come up: the board rises toward the rider around the top and is back
        // at the legs' full reach when it moves at take-off speed, going up or coming down. The
        // player itself is where the game has it; only the board and the feet are drawn higher.
        var rise = 0f;
        if (SkateRide.On && SkateRide.Mode == RideMode.Air && SkateRide.Jumped && !SkateRide.Frozen)
        {
            var v = SkateRide.VSpeed / SkateRide.OlliePop;
            rise = SkateBoard.PopMost * SkateRide.Clamp(1f - v * v, 0f, 1f);
        }
        // A grab pulls the board up further, to the rider's hands, as the body folds into it.
        if (SkateRide.On && SkateRide.Mode == RideMode.Air) rise += SkateBoard.GrabRise * SkateBoard.GrabFold;
        SkateBoard.Pop += (rise - SkateBoard.Pop) * SkateRide.Clamp(frame * 18f, 0f, 1f);
        if (board != null)
        {
            var t0 = SkateClock.Now();
            try { SkateBoard.Follow(board, LocalT); }
            catch (Exception e) { Say("board update threw " + e.GetType().Name + ": " + e.Message); board = null; }
            SkateClock.Add(SkateClock.Board, t0);
        }
        var third = SkateRide.On && LocalT != null && (SkateCamera.Chase || SkateCamera.Fixed);
        var t1 = SkateClock.Now(); RiderRig.Look(third); SkateClock.Add(SkateClock.Look, t1);
        if (!SkateRide.On || LocalT == null) return;
        var at = SkateBoard.Contact(LocalT);
        var feet = at; var up = SkateRide.ViewNormal;
        SkateBoard.Tilt(ref feet, ref up);
        var t2 = SkateClock.Now(); RiderRig.Frame(feet, up, !third, frame); SkateClock.Add(SkateClock.Rider, t2);
        var t3 = SkateClock.Now(); SkateCamera.Apply(at - Vector3.up * SkateBoard.Pop); SkateClock.Add(SkateClock.Camera, t3);
    }

    private void OnDestroy()
    {
        try { SkateRide.Dismount("the skate rig is gone"); }
        catch (Exception e) { Out.Err("SKATE dismount threw " + e.GetType().Name + ": " + e.Message); }
        try { if (late != null) late.enabled = false; }
        catch (Exception e) { Out.Err("SKATE late driver disable threw " + e.GetType().Name + ": " + e.Message); }
        try { if (late != null) UnityEngine.Object.Destroy(late); late = null; }
        catch (Exception e) { Out.Err("SKATE late driver teardown threw " + e.GetType().Name + ": " + e.Message); }
        try { RiderRig.Unbind(); }
        catch (Exception e) { Out.Err("RIDER unbind threw " + e.GetType().Name + ": " + e.Message); }
        try { if (board != null) board.SetActive(false); }
        catch (Exception e) { Out.Err("SKATE board disable threw " + e.GetType().Name + ": " + e.Message); }
        try { if (board != null) UnityEngine.Object.Destroy(board); board = null; }
        catch (Exception e) { Out.Err("SKATE board teardown threw " + e.GetType().Name + ": " + e.Message); }
        try { SkateSfx.Shutdown(); }
        catch (Exception e) { Out.Err("AUDIO teardown threw " + e.GetType().Name + ": " + e.Message); }
        Local = null; LocalT = null; Body = null; Walk = null; AwakeAt = -1f; wasOn = false;
    }

    private void OnGUI()
    {
        if (hudFailed) return;
        var t0 = SkateClock.Now();
        try { SkateHud.Draw(); }
        catch (Exception e) { hudFailed = true; Say("text threw " + e.GetType().Name + ": " + e.Message); }
        SkateClock.Add(SkateClock.Hud, t0);
    }
}
