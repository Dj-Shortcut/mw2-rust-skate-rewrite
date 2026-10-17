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
    private float holsterSaidAt = -99f; private int restoreSlot = -1;
    private string lastHolster = "";
    private float nextTick, nextLog, lateRetryAt, lastJumpTap = -1f, crouchSince = -1f;
    private int standTicks, lyingTicks, lateFailures;
    public const float LyingEye = 0.6f, LyingReach = 0.8f, DownSeconds = 0.5f;
    private static float downSince = -1f;
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
        if (wasOn && !SkateRide.On) { offAt = now; Say("OFF (" + SkateRide.OffReason + ") top=" + SkateRide.Top.ToString("F1") + " pull-backs=" + SkateRide.Resets + " score=" + SkateRide.Trick.TotalPoints); LevelView(); Unholster(); }
        // Nothing in the hand while riding, as on a vehicle: no building or swinging from the board,
        // and nothing held to be seen. Every frame, so a slot picked mid-ride never shows.
        if (SkateRide.On && Local != null)
        {
            string how;
            var held = SkateHeld.Holster(Local, out how);
            // The slot held when the ride began, to hand back on stepping off.
            if (held && restoreSlot < 0 && SkateHeld.Slot >= 0 && SkateHeld.Slot < 6) restoreSlot = SkateHeld.Slot;
            if (held || how != "") { if (how != lastHolster || now - holsterSaidAt > 10f) { lastHolster = how; holsterSaidAt = now; Say("HOLSTER " + how); } }
        }
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

    // The game's own first-person camera at the player and below a crouch: lying asleep, wounded
    // or dead. The rider's chase and fixed cameras are always well away from the player.
    [HideFromIl2Cpp]
    public static bool Lying(Vector3 cp)
    {
        if (LocalT == null) return false;
        var d = cp - LocalT.position;
        return d.y < LyingEye && d.x * d.x + d.z * d.z < LyingReach * LyingReach;
    }

    [HideFromIl2Cpp]
    private void LetGo(string why, string off, bool forgetServer)
    {
        Say(why + "; looking again");
        SkateRide.Dismount(off); RiderRig.Unbind();
        if (forgetServer) SkateRide.ForgetServer();
        // The old driver is queued behind the old walk component, not the next one.
        if (late != null) { UnityEngine.Object.Destroy(late); late = null; }
        Local = null; LocalT = null; Walk = null; Body = null; AwakeAt = -1f; standTicks = lyingTicks = 0; tracking = lateAdded = false; lateFailures = 0;
        downSince = -1f;
    }

    // Getting off hands the view back to the game looking level, the way the board was going: the
    // mouse may have taken the game's own pitch far up or down while the chase camera clamped it.
    // The player's input is told the new view (SetViewVars); the route is logged once.
    private static bool viewLooked; private static System.Reflection.MethodInfo setView; private static System.Reflection.MemberInfo inputMember;
    // Off the board the item put away at the start of the ride is taken in hand again.
    private void Unholster()
    {
        var slot = restoreSlot; restoreSlot = -1;
        if (slot < 0 || Local == null) return;
        string how;
        SkateHeld.SelectSlot(Local, slot, out how);
        Say("UNHOLSTER " + how);
    }

    public static void LevelView()
    {
        SetView(SkateRide.Yaw + (SkateRide.Speed < -0.5f ? 180f : 0f), "level");
    }

    // With a controller the game's own view is not the steering: Steam's layout sends the right
    // stick to the game as the mouse, so every flick turns the view (the owner's log: up to 145
    // degrees in a second on the board). The chase camera hides that, but the game's compass
    // reads the view and whips round at every trick, and the pitch is left far up or down for
    // the step-off. So while the controller rides the view is put back every frame, level and
    // the way the camera looks: behind the rider, the chase camera's own yaw; otherwise the way
    // the board goes, kept through a stop by the same hysteresis the chase camera uses. The
    // keyboard is left alone: there the mouse is the steering. Only the flick's own frame can
    // show before it is put back. Whether the compass follows is for the game to show.
    // A route that is missing or threw stops it for the rest of the ride; the next mount tries
    // again, since the player's input object is replaced on a respawn or reconnect.
    private static bool viewAhead = true, steadyFailed;
    private static void SteadyView()
    {
        if (steadyFailed || !SkateRide.On || !SkateKeys.Pad || SkateKeys.Scripted) return;
        if (SkateRide.Speed > 1f) viewAhead = true; else if (SkateRide.Speed < -1f) viewAhead = false;
        var yaw = SkateCamera.Chase && !SkateCamera.Fixed ? SkateCamera.FollowYaw : SkateRide.Yaw + (viewAhead ? 0f : 180f);
        if (!SetView(yaw, "steady")) steadyFailed = true;
    }

    // The game's view, yaw only and level, told to the player's input (SetViewVars). False once
    // the route is known not to be there, or when it threw: the caller then gives up on it.
    // The input object is read from the player each time rather than bound once: the game hands
    // out a fresh wrapper for it, so a bound call could not be told from a stale one. One array
    // is kept; the quaternion's box is the one allocation a frame this costs.
    private static readonly object[] viewArgs = new object[1];
    private static bool SetView(float yaw, string what)
    {
        if (Local == null) return true;
        try
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            if (!viewLooked)
            {
                viewLooked = true;
                // The game's fields come through as properties in this build; both are looked at.
                for (var at = Local.GetType(); at != null && setView == null; at = at.BaseType)
                {
                    foreach (var f in at.GetFields(flags | System.Reflection.BindingFlags.DeclaredOnly))
                    {
                        var m = f.FieldType.GetMethod("SetViewVars", flags, null, new[] { typeof(Quaternion) }, null);
                        if (m != null) { setView = m; inputMember = f; break; }
                    }
                    if (setView != null) break;
                    foreach (var p in at.GetProperties(flags | System.Reflection.BindingFlags.DeclaredOnly))
                    {
                        if (p.GetIndexParameters().Length != 0) continue;
                        var m = p.PropertyType.GetMethod("SetViewVars", flags, null, new[] { typeof(Quaternion) }, null);
                        if (m != null) { setView = m; inputMember = p; break; }
                    }
                }
                Out.Say("SKATE view route: " + (setView == null ? "none (no member with SetViewVars(Quaternion))" : inputMember.Name + ".SetViewVars"));
            }
            if (setView == null) return false;
            var field = inputMember as System.Reflection.FieldInfo;
            var input = field != null ? field.GetValue(Local) : ((System.Reflection.PropertyInfo)inputMember).GetValue(Local, null);
            if (input != null) { viewArgs[0] = Quaternion.Euler(0f, yaw, 0f); setView.Invoke(input, viewArgs); }
            return true;
        }
        catch (Exception e) { Out.Say("SKATE view " + what + " threw " + e.GetType().Name + ": " + e.Message); return false; }
    }

    public static void MountNow(float yaw)
    {
        if (Body == null) return;
        if (!RiderRig.Bound) { try { RiderRig.Bind(LocalT); } catch (Exception e) { Out.Say("RIDER bind threw " + e.GetType().Name + ": " + e.Message); } }
        SkateSfx.Start();
        SkateRide.Mount(Body, yaw); viewAhead = true; steadyFailed = false;
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
            LetGo("the player or its movement object is gone", "the movement object is gone", Local == null);
        // Wounded, dead or just respawned and asleep, the player keeps its object while the game
        // rebuilds what hangs off it: the game's own camera lies at the player, low. Everything this
        // life was bound to is let go and found again when the player stands up.
        if (tracking && AwakeAt > 0f && !SkateRide.On)
        {
            lyingTicks = Lying(cp) ? lyingTicks + 1 : 0;
            if (lyingTicks >= 2) LetGo("the player is lying down", "the player is down", true);
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
        // Riding, the camera is the rider's own until the game puts its first-person view of a
        // player going down there, low at the player: off the board then, and let go on the next tick.
        if (SkateRide.On && Camera.main != null && Lying(Camera.main.transform.position))
        {
            if (downSince < 0f) downSince = now;
            else if (now - downSince > DownSeconds) { downSince = -1f; SkateRide.Dismount("the player went down"); }
        }
        else downSince = -1f;
        SkateParks.Frame(now);
        try { SkateParks.Isolate(SkateRide.On, Body, now); } catch (Exception e) { Say("isolation threw " + e.GetType().Name + ": " + e.Message); }
        SkatePieces.Frame(now);
        SkateInbox.Frame(now);
        // With a controller the rider looks where the board goes, a little into the turn; the view
        // itself is turned by the right stick's flicks and says nothing.
        if (!SkateKeys.Scripted) SkateKeys.LookYaw = SkateKeys.Pad && SkateRide.On ? SkateRide.Yaw + SkateKeys.Steer * 35f : SkateCamera.LookYaw;
        var grinding = SkateRide.On && SkateRide.Mode == RideMode.Grind;
        SkateBoard.Tip += ((!SkateRide.On || grinding ? 0f : SkateRide.Manual ? 1f : SkateRide.NoseManual ? -1f : 0f) - SkateBoard.Tip) * SkateRide.Clamp(frame * 9f, 0f, 1f);
        double gp = 0, gr = 0, gc;
        if (grinding) Shortcut.RustMod.SkateRider.GrindDeck(SkateRide.GrindKind, out gp, out gr, out gc);
        SkateBoard.GrindPitch += ((float)gp - SkateBoard.GrindPitch) * SkateRide.Clamp(frame * 12f, 0f, 1f);
        SkateBoard.GrindRoll += ((float)gr - SkateBoard.GrindRoll) * SkateRide.Clamp(frame * 12f, 0f, 1f);
        // An ollie pitches the board: nose up as it climbs, level at the top, nose a little down as
        // it falls; not held in a grab or a handplant, where the hands have the board.
        var ollie = SkateRide.On && SkateRide.Mode == RideMode.Air && SkateRide.Jumped && !SkateRide.Grabbing && !SkateRide.Handplant;
        var climb = SkateRide.Clamp(SkateRide.VSpeed / SkateRide.OlliePop, -1f, 1f);
        var airPitch = ollie ? (climb > 0f ? SkateBoard.OllieNoseUp : SkateBoard.OllieNoseDown) * climb : 0f;
        SkateBoard.AirPitch += (airPitch - SkateBoard.AirPitch) * SkateRide.Clamp(frame * 14f, 0f, 1f);
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
        // The rider stands on the deck's middle where the pitch puts it, but the body keeps the
        // unpitched up; the module puts the feet on the pitched deck.
        var feet = at; var up = SkateRide.ViewNormal; var tilted = up;
        SkateBoard.Tilt(ref feet, ref tilted);
        var t2 = SkateClock.Now(); RiderRig.Frame(feet, up, !third, frame); SkateClock.Add(SkateClock.Rider, t2);
        var t3 = SkateClock.Now(); SkateCamera.Apply(at - Vector3.up * SkateBoard.Pop); SteadyView(); SkateClock.Add(SkateClock.Camera, t3);
        try { SkateWatch.Frame(LocalT, board, now); }
        catch (Exception e) { if (!watchFailed) { watchFailed = true; Say("watch threw " + e.GetType().Name + ": " + e.Message); } }
    }
    private static bool watchFailed;

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
        try { SkateParks.Clear(); }
        catch (Exception e) { Out.Err("PARK clear threw " + e.GetType().Name + ": " + e.Message); }
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
