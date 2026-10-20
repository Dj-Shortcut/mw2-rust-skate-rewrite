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

    // The walk Rigidbody is the physical player. Rust sends the separate player root's
    // position, so keep that proxy with the body while the board owns locomotion.
    // Its kinematic collider must not become another physical participant as it follows.
    private sealed class RootProxyLease
    {
        public BasePlayer Player;
        public Transform Root, Frame, Parent;
        public Rigidbody Proxy, Physical;
        public PlayerWalkMovement Movement;
        public float Life, MaximumOffset;
        public bool OriginalCollisions, Changed, RestoreReported, Releasing;
        public int Attempts, Writes, Fixed, Update, Late, Samples;
    }
    private static RootProxyLease rootProxy;

    private static bool ProxyFinite(Vector3 v)
    {
        return !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
    }
    private static bool ProxyFinite(Quaternion q) { return ProxyFinite(new Vector3(q.x, q.y, q.z)) && !float.IsNaN(q.w) && !float.IsInfinity(q.w); }
    private static bool ProxySame(Vector3 a, Vector3 b) { return a.x == b.x && a.y == b.y && a.z == b.z; }
    private static bool ProxySame(Quaternion a, Quaternion b) { return a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w; }

    private static bool ProxyCurrent(RootProxyLease p)
    {
        return p != null && Local == p.Player && LocalT == p.Root && Body == p.Physical && Walk == p.Movement && AwakeAt == p.Life &&
            Local != null && Local.lifestate == BaseCombatEntity.LifeState.Alive && LocalT != null && LocalT == Local.transform && LocalT.parent == null &&
            Body != null && !Body.isKinematic && Body.detectCollisions && Body.transform == p.Frame && p.Frame.parent == p.Parent && !p.Frame.IsChildOf(LocalT) &&
            Walk != null && Walk.enabled && Walk.transform == p.Frame && p.Proxy != null && p.Proxy.transform == LocalT && p.Proxy.isKinematic && !p.Proxy.detectCollisions;
    }

    [HideFromIl2Cpp]
    private static bool AcquireRootProxy()
    {
        ReleaseRootProxy();
        if (rootProxy != null) { Say("ROOTSYNC mount refused: collision restoration is pending"); return false; }
        try
        {
            if (!Ready || Local == null || LocalT == null || LocalT != Local.transform || LocalT.parent != null ||
                Local.lifestate != BaseCombatEntity.LifeState.Alive || Walk == null || !Walk.enabled || Body == null || Body.isKinematic || !Body.detectCollisions)
                throw new InvalidOperationException("player is not ready");
            var frame = Body.transform; var proxy = LocalT.GetComponent<Rigidbody>();
            if (frame != Walk.transform || frame == LocalT || frame.IsChildOf(LocalT) || LocalT.IsChildOf(frame) ||
                proxy == null || proxy == Body || proxy.transform != LocalT || !proxy.isKinematic)
                throw new InvalidOperationException("player proxy is not independent");
            // This lease owns only this proxy, never another Rigidbody or a joint-driven body.
            var bodies = LocalT.GetComponentsInChildren<Rigidbody>(true);
            if (bodies.Length != 1 || bodies[0] != proxy || LocalT.GetComponentsInChildren<Joint>(true).Length != 0 ||
                frame.GetComponentsInChildren<Joint>(true).Length != 0)
                throw new InvalidOperationException("player proxy has other physical owners");
            var position = Body.position; var velocity = Body.linearVelocity; var world = frame.position; var rotation = frame.rotation;
            if (!ProxyFinite(position) || !ProxyFinite(velocity) || !ProxyFinite(world) || !ProxyFinite(rotation) || !ProxyFinite(LocalT.position) || (LocalT.position - position).sqrMagnitude > 1f)
                throw new InvalidOperationException("player proxy is too far from the body");
            rootProxy = new RootProxyLease { Player = Local, Root = LocalT, Proxy = proxy, Physical = Body, Movement = Walk,
                Frame = frame, Parent = frame.parent, Life = AwakeAt, OriginalCollisions = proxy.detectCollisions };
            // Record ownership before the setter: even a later readback failure must restore it.
            if (rootProxy.OriginalCollisions) { rootProxy.Changed = true; proxy.detectCollisions = false; }
            if (!ProxyCurrent(rootProxy) || !ProxySame(Body.position, position) || !ProxySame(Body.linearVelocity, velocity) ||
                !ProxySame(frame.position, world) || !ProxySame(frame.rotation, rotation))
                throw new InvalidOperationException("player proxy collision lease did not verify");
            Say("ROOTSYNC acquired kinematic=True collisions=False physical-unchanged=True");
            return true;
        }
        catch (Exception e)
        {
            Say("ROOTSYNC mount refused: " + e.GetType().Name + ": " + e.Message);
            ReleaseRootProxy(); return false;
        }
    }

    [HideFromIl2Cpp]
    public static void SyncRootProxy(int phase)
    {
        if (!SkateRide.On) { ReleaseRootProxy(); return; }
        try
        {
            var p = rootProxy;
            if (!ProxyCurrent(p)) throw new InvalidOperationException("player proxy ownership changed");
            var position = p.Physical.position; var velocity = p.Physical.linearVelocity; var world = p.Frame.position; var rotation = p.Frame.rotation;
            var offset = p.Root.position - position;
            if (!ProxyFinite(position) || !ProxyFinite(velocity) || !ProxyFinite(world) || !ProxyFinite(rotation) || !ProxyFinite(offset) || offset.sqrMagnitude > 1f)
                throw new InvalidOperationException("player proxy offset is invalid");
            p.MaximumOffset = Math.Max(p.MaximumOffset, offset.magnitude);
            p.Attempts++; p.Root.position = position;
            if (!ProxyCurrent(p) || !ProxySame(p.Physical.position, position) || !ProxySame(p.Physical.linearVelocity, velocity) ||
                !ProxySame(p.Frame.position, world) || !ProxySame(p.Frame.rotation, rotation) ||
                !ProxyFinite(p.Root.position) || (p.Root.position - position).sqrMagnitude > 0.00000001f)
                throw new InvalidOperationException("player proxy write did not verify");
            p.Writes++;
            if (phase == 0) p.Fixed++; else if (phase == 1) p.Update++; else p.Late++;
            var bit = 1 << phase;
            if ((p.Samples & bit) == 0) { p.Samples |= bit; Say("ROOTSYNC sample phase=" + (phase == 0 ? "fixed" : phase == 1 ? "update" : "late") + " physical-unchanged=True readback=True"); }
        }
        catch (Exception e)
        {
            Say("ROOTSYNC stopped: " + e.GetType().Name + ": " + e.Message);
            SkateRide.Dismount("the player proxy changed");
        }
    }

    [HideFromIl2Cpp]
    public static void ReleaseRootProxy()
    {
        var p = rootProxy; if (p == null || p.Releasing) return;
        p.Releasing = true;
        try
        {
            if (p.Changed && p.Proxy != null)
            {
                if (p.Proxy.transform != p.Root) throw new InvalidOperationException("player proxy identity changed");
                // A different value is already the original one; do not overwrite another owner.
                if (!p.Proxy.detectCollisions) p.Proxy.detectCollisions = p.OriginalCollisions;
                if (p.Proxy.detectCollisions != p.OriginalCollisions) throw new InvalidOperationException("collision restoration did not verify");
            }
            var gone = p.Proxy == null;
            rootProxy = null;
            Say("ROOTSYNC released proxy-gone=" + gone + " restored=" + (gone ? "n/a" : "True") + " attempts=" + p.Attempts + " writes=" + p.Writes + " fixed=" + p.Fixed + " update=" + p.Update + " late=" + p.Late + " max-offset=" + p.MaximumOffset.ToString("F3"));
        }
        catch (Exception e)
        {
            // Keep the exact old reference for another release attempt; never transfer its lease.
            if (!p.RestoreReported) { p.RestoreReported = true; Say("ROOTSYNC restore pending: " + e.GetType().Name + ": " + e.Message); }
        }
        finally { p.Releasing = false; }
    }

    private static string RootProxyStatus()
    {
        var p = rootProxy;
        return p == null ? "root-sync=off" : "root-sync=" + p.Writes + "/" + p.Attempts + " fixed=" + p.Fixed + " update=" + p.Update + " late=" + p.Late + " max-offset=" + p.MaximumOffset.ToString("F3");
    }

    private void Update()
    {
        var now = Time.realtimeSinceStartup;
        SyncRootProxy(1);
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
        if (SkateRide.On && now >= nextLog) { nextLog = now + 1f; Say(SkateRide.Status() + " | " + RootProxyStatus() + " | " + SkateClock.Report()); }
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
        SkateRide.Dismount(off);
        // Release only this ride's held-state ownership while its player/model
        // references still exist; a later Update is not guaranteed on teardown.
        try { Unholster(); }
        catch (Exception e) { Out.Err("HELD restore during movement teardown threw " + e.GetType().Name + ": " + e.Message); }
        RiderRig.Unbind();
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
        var shown = SkateHeld.Show();
        if (shown > 0) Say("UNHOLSTER switched " + shown + " held entity back on");
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
        // A chase camera that threw no longer moves FollowYaw: the board's heading then.
        var yaw = SkateCamera.Chase && !SkateCamera.Fixed && !SkateCamera.Failed ? SkateCamera.FollowYaw : SkateRide.Yaw + (viewAhead ? 0f : 180f);
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
        if (Body == null || SkateRide.On || !AcquireRootProxy()) return;
        try
        {
            if (!RiderRig.Bound) { try { RiderRig.Bind(LocalT); } catch (Exception e) { Out.Say("RIDER bind threw " + e.GetType().Name + ": " + e.Message); } }
            SkateSfx.Start();
            SkateRide.Mount(Body, yaw); viewAhead = true; steadyFailed = false;
            SkateHud.MountedAt = Time.realtimeSinceStartup;
            Say("MOUNT yaw=" + SkateRide.Yaw.ToString("F0") + " speed=" + SkateRide.Speed.ToString("F1") + " rider=" + RiderRig.Bound + " audio=" + SkateSfx.State);
        }
        catch { SkateRide.Dismount("the skate mount failed"); ReleaseRootProxy(); throw; }
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
        SyncRootProxy(2);
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
        var third = SkateRide.On && LocalT != null && (SkateCamera.Chase || SkateCamera.Fixed);
        var t1 = SkateClock.Now(); RiderRig.Look(third); SkateClock.Add(SkateClock.Look, t1);
        RiderRig.Prepare(frame);
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
        var at = LocalT != null ? SkateBoard.Contact(LocalT) : Vector3.zero;
        if (SkateRide.On && LocalT != null)
        {
            var feet = at; var up = SkateRide.ViewNormal; var tilted = up;
            SkateBoard.Tilt(ref feet, ref tilted);
            var t2 = SkateClock.Now(); RiderRig.Frame(feet, up, !third, frame); SkateClock.Add(SkateClock.Rider, t2);
        }
        // Held board placement is produced by this frame's solved pose.
        if (board != null)
        {
            var t0 = SkateClock.Now();
            try { SkateBoard.Follow(board, LocalT); }
            catch (Exception e) { Say("board update threw " + e.GetType().Name + ": " + e.Message); board = null; }
            SkateClock.Add(SkateClock.Board, t0);
        }
        if (!SkateRide.On || LocalT == null) return;
        var t3 = SkateClock.Now(); SkateCamera.Apply(at - Vector3.up * SkateBoard.Pop); SteadyView(); SkateClock.Add(SkateClock.Camera, t3);
        try { SkateWatch.Frame(LocalT, board, now); }
        catch (Exception e) { if (!watchFailed) { watchFailed = true; Say("watch threw " + e.GetType().Name + ": " + e.Message); } }
    }
    private static bool watchFailed;

    private void OnDisable() { SkateRide.Dismount("the skate rig is disabled"); ReleaseRootProxy(); }

    private void OnDestroy()
    {
        try { SkateRide.Dismount("the skate rig is gone"); }
        catch (Exception e) { Out.Err("SKATE dismount threw " + e.GetType().Name + ": " + e.Message); }
        try { Unholster(); }
        catch (Exception e) { Out.Err("HELD restore during rig teardown threw " + e.GetType().Name + ": " + e.Message); }
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
