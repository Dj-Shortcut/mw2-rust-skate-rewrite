// While mounted the plugin owns the whole velocity of the movement body.
//   - The game's fixed step cancels any velocity written before it, so Step() must run from a
//     component created after the game's walk component (the late driver).
//   - The walk component must stay enabled: with it off the server pulls the player back.
//   - Engine methods the game itself never calls can be missing from the build, hence plain
//     arithmetic instead of UnityEngine.Mathf and a rotation built from its fields.
using System;
using Shortcut.RustMod;
using UnityEngine;

public enum RideMode { Off, Ground, Air, Grind, Bail }

public static class SkateRide
{
    // Ground speed remains tangent speed across changing slopes; server feedback and caps are X/Z.
    public const float PushAccel = 7f, MaxPush = 11f, BrakeDecel = 12f, RollDecel = 0.45f, MaxSpeed = 13f, MaxReverse = 6f, TurnRate = 150f, SlopeGain = 1.6f;
    public const float AirGravity = 16f, OlliePop = 6f, MaxFall = 30f, BailImpact = 12f, BailSeconds = 1.3f, BailDecel = 10f, PushPeriod = 0.8f, SeaLevel = -1.1f, LowestCap = 2f, LagStep = 0.1f, RaiseWait = 3f, CapForget = 30f, ManualPace = 1f, ManualHold = 0.3f, HopGrace = 0.3f;
    // Pumping: the shared SkateRidePhysics judges it (a crouch held down a transition, let go
    // through its bottom); PumpCharge only shapes the rider's crouch. Landing: the fall becomes
    // roll on a slope by LandRoll (the shared landing rule); a revert within RevertWindow of a
    // landed trick turns the board round at RevertRate and keeps the combo.
    public const float PumpCharge = 0.4f, LandRoll = 0.8f, RevertWindow = 0.35f, RevertRate = 600f;
    public const long RevertPoints = 50;
    public const float RevertStick = 0.85f;
    private static float spinAtLand;
    private static Vector3 landRoll; private static bool hasLandRoll;
    private const float RayLift = 0.6f, FeetProbe = 0.3f, AirProbe = 3f;
    // A lip ahead up to StepUp high (the seam between two foundations, a kerb) is stepped over:
    // the board is lifted onto it and the loss of way it costs is not read as an obstacle.
    public const float StepUp = 0.25f, StepLook = 0.35f, StepGrace = 0.25f;
    public static int StepUps;
    private static float steppedAt = -99f;
    public static readonly int GroundMask = ~((1 << 12) | (1 << 17) | (1 << 18) | (1 << 4) | (1 << 10) | (1 << 9) | (1 << 2));

    public static RideMode Mode = RideMode.Off;
    public static bool On { get { return Mode != RideMode.Off; } }
    public static bool Grounded, Braking, Jumped, TrickAir, Manual, NoseManual;
    public static SkateGrabKind Grab;
    public static bool Grabbing { get { return Grab != SkateGrabKind.None; } }
    public static bool Frozen;
    public static float Speed, Yaw, Lean, VSpeed, AirTime, AirPeak, Clearance = 99f, Actual, ActualV, Top, Cap = MaxSpeed, ModeAt, LandAt = -99f, LandImpact, PushPhase, CapLoweredAt = -99f;
    public static Vector3 Normal = Vector3.up, Tangent = Vector3.forward, ViewNormal = Vector3.up, Position;
    // In the shared trick module's signs: positive spin and shove are frontside, positive flip is a
    // kickflip. Spin turns rider and board, shove the board alone under his feet.
    public static double SpinDeg, FlipDeg, ShoveDeg;
    // A half shove that was landed leaves the board the other way round under the same rider.
    public static bool DeckReversed;
    public static SkateTrickState Trick;
    public static string TrickName = "", LastAir = "", OffReason = "";
    public static long TrickPoints, Banked, ComboPoints;
    public static float TrickAt = -99f, BankedAt = -99f;
    public static int Steps, Resets, Pops, Lands, Bails, Pushes, GrindStarts, GrindEnds, Refused, Pumps, Reverts;
    public static float PumpHeld, RevertLeft;
    // The shared ride physics: the slope along the actual travel, the pump's pass, and the normal of
    // the ground last hit under the board, which is what a landing meets.
    public static float SlopeDegrees;
    public static SkatePumpState Pump = default(SkatePumpState);
    public static Vector3 ContactNormal = Vector3.up;
    public static string PhysicsError = "";
    public static SkateGrindKind GrindKind;
    public static bool GrindFrontside, GrindAlong;
    // A stall holds the board still on an edge (Mode is Grind, GrindKind a stall kind); a handplant
    // is the stall with a hand on the edge. StallMax: how long one is held before rolling off.
    public const float StallSpeed = 7f, StallMax = 2.5f, StallOut = 2.5f;
    // A handplant comes down before it leaves the edge: the body swings back onto the board over
    // HandDownSeconds, held still on the edge meanwhile.
    public const float HandDownSeconds = 0.4f;
    private static float handDownAt = -99f; private static bool handDownPop;
    public static bool HandplantDown { get { return Stalling && handDownAt > 0f; } }
    public static bool Stalling;
    public static int Stalls;
    private static float stallAt, stallSign = 1f;
    public static bool Handplant { get { return Stalling && GrindKind == SkateGrindKind.Handplant; } }
    // A no comply or a boneless: the front foot is down beside the board for a moment after the pop.
    public const float PlantSeconds = 0.25f;
    public static float PlantedUntil = -99f;
    public static bool FootPlanted { get { return Mode == RideMode.Air && now < PlantedUntil; } }
    // The push with the front foot (mongo), as the push began.
    public static bool PushMongo;
    // Which foot a push uses: the trailing foot as a rule (the back foot, or the front one in a
    // mongo push); rolling tail first in the same stance, the tail foot leads and pushes, as a
    // skater pushing fakie does: the trailing foot would have to cross the standing leg to reach
    // the toe side. After a 180 the stance itself is turned round (NoseYaw), and the trailing foot
    // pushes on its own toe side. PushLead: the foot that leads in the way of travel pushes.
    public static bool PushLead;
    // The deck's angle to the edge, taken when the grind begins and kept through it.
    public static float GrindDeck;
    private static float grindCross, grindNoseFar, grindNose, airNoseOffset, airDeckOffset;
    private static Vector3 revertDir;
    private static float revertSign = 1f;
    private static float revertEndAt = -99f;
    private static bool fastFakie;
    private static bool landedTrick;
    private static Rigidbody body;
    private static Vector3 lastPos, lastDir, velocity;
    private static float lastSpeed, lastPull = -999f, raisedAt = -999f, putBackAt = -999f, raiseWait = RaiseWait, now, mountAt, takeOffY, flipLeft, shoveLeft, jumpWanted = -99f;
    private static bool hasLast, refusedSinceRaise, spinning;
    private static int stuck;
    // Held back: for HeldSteps physics steps in a row the board got less than HeldShare of what it
    // rode. The speed follows what it got, plus HeldLeft.
    public static bool Held;
    public const int HeldSteps = 3;
    public const float HeldShare = 0.35f, HeldLeft = 0.3f;
    private static int heldSteps;
    // The pop that was asked for, kept with the press for the moment the press itself is kept; what
    // of it is still to be started in the air; and which way a frontside turn goes in this jump.
    private static SkatePopKind wantKind = SkatePopKind.Ollie, popKind;
    private static int wantFlip, pendFlip;
    // In the air: degrees of body varial still to turn (the rider only; the board keeps its yaw),
    // the rider's turn so far, and whether the flip or shove under way was asked for in the air.
    private static float bodyLeft;
    public static double BodyDeg;
    public static bool LateTrick;
    // A flip or shove asked for this long after take-off or later is late: part of the pop before that.
    public const float LateAfter = 0.25f;
    private static float wantShove, wantScale = 1f, pendShove, spinSide = 1f;
    private const float SpinSlack = 15f;
    // The chest ahead of a spin (degrees, positive frontside): turned into it while it goes on and
    // brought back as the half turn completes; on the ground, wound up against the one being asked for.
    public static float SpinLead;
    public const float SpinLeadMost = 32f, SpinWindUp = 18f;

    public static Vector3 Dir(float yaw) { return Quaternion.Euler(0f, yaw, 0f) * Vector3.forward; }
    public static float Toward(float a, float b, float step) { return Math.Abs(b - a) <= step ? b : a + Math.Sign(b - a) * step; }
    public static float Delta(float from, float to) { var d = (to - from) % 360f; if (d > 180f) d -= 360f; if (d < -180f) d += 360f; return d; }
    public static float Clamp(float v, float lo, float hi) { return v < lo ? lo : v > hi ? hi : v; }
    public static Quaternion Turn(float degrees, Vector3 axis)
    {
        var n = axis.normalized; var half = degrees * (float)Math.PI / 360f; var s = (float)Math.Sin(half);
        var q = default(Quaternion); q.x = n.x * s; q.y = n.y * s; q.z = n.z * s; q.w = (float)Math.Cos(half);
        return q;
    }

    // Frontside is the turn that brings the rider's chest round to the way he travels: to the left
    // for a regular rider going nose first, to the right when the tail leads, and the other way
    // round for a goofy rider. NoseYaw is where the
    // rider's stance points (spin included), DeckYaw where the board's own nose points.
    public static float SpinYaw { get { return -(float)SpinDeg * spinSide; } }
    // A body varial turns the rider's stance and nothing else: the deck keeps its own yaw.
    public static float BodyYaw { get { return -(float)BodyDeg * spinSide; } }
    public static float NoseYaw { get { return Mode == RideMode.Grind ? SkateGrind.Heading + grindNose : Yaw + (Trick.Switch ? 180f : 0f) + SpinYaw + BodyYaw + airNoseOffset; } }
    public static float DeckYaw { get { return Mode == RideMode.Grind ? SkateGrind.Heading + GrindDeck : NoseYaw - BodyYaw - (float)ShoveDeg * spinSide + (DeckReversed ? 180f : 0f) + airDeckOffset; } }
    private static float HorizontalFactor { get { return Mode == RideMode.Ground ? Flat(Tangent).magnitude : 1f; } }
    public static float HorizontalSpeed { get { return Speed * HorizontalFactor; } }
    private static Vector3 Flat(Vector3 v) { return new Vector3(v.x, 0f, v.z); }
    private static Vector3 SurfaceTangent(Vector3 normal, Vector3 heading)
    {
        if (normal.y <= 0.3f) return heading;
        var v = heading; v.y = -(v.x * normal.x + v.z * normal.z) / normal.y;
        return v.normalized;
    }
    private static void LimitSpeed(float forward, float reverse)
    {
        var h = HorizontalFactor;
        if (h <= 0.0001f) { Speed = 0f; return; }
        Speed = Clamp(Speed, -reverse / h, forward / h);
        if (Mode == RideMode.Grind) SkateGrind.Speed = Speed;
    }
    public static bool TailFirst { get { return Trick.Switch != (Speed < -0.3f); } }
    // The rider's stance: regular (left foot forward, chest to the right of the nose) or goofy, the
    // mirror image. Read from the stance file at start, switched on foot with J or Back+Y, and kept.
    public static bool Goofy;
    public static float StanceSign { get { return Goofy ? -1f : 1f; } }
    public const string StanceFile = "skate-stance.txt";
    public static string StanceName { get { return Goofy ? "goofy" : "regular"; } }
    public static string StancePath()
    {
        try { return System.IO.Path.Combine(Application.persistentDataPath, StanceFile); } catch (Exception) { return null; }
    }
    public static void LoadStance()
    {
        try { var path = StancePath(); Goofy = path != null && System.IO.File.Exists(path) && System.IO.File.ReadAllText(path).Trim().ToLowerInvariant() == "goofy"; }
        catch (Exception e) { Goofy = false; Out.Say("STANCE could not be read: " + e.GetType().Name + ": " + e.Message); }
    }
    // Only off the board: the feet are placed on getting on.
    public static bool ToggleStance()
    {
        if (On) return false;
        Goofy = !Goofy;
        try { var path = StancePath(); if (path != null) System.IO.File.WriteAllText(path, StanceName); }
        catch (Exception e) { Out.Say("STANCE could not be kept: " + e.GetType().Name + ": " + e.Message); }
        Out.Say("STANCE " + StanceName);
        SkatePieces.Said = "Stance: " + StanceName + " (" + (Goofy ? "right" : "left") + " foot forward)"; SkatePieces.SaidAt = Time.realtimeSinceStartup;
        return true;
    }
    // Which way round a frontside turn goes from here: the sign a left-stick turn gets in the trick module.
    public static float SpinSide { get { return (TailFirst ? -1f : 1f) * StanceSign; } }
    public static bool Rotating { get { return spinning || Math.Abs(flipLeft) > 0.001f || Math.Abs(shoveLeft) > 0.001f || Math.Abs(bodyLeft) > 0.001f; } }

    public static void ForgetServer() { Cap = MaxSpeed; raiseWait = RaiseWait; refusedSinceRaise = false; raisedAt = putBackAt = -999f; }

    public static void Mount(Rigidbody b, float yaw)
    {
        body = b; Yaw = yaw;
        var v = b.linearVelocity; velocity = v; var rise = v.y; v.y = 0f;
        Speed = Vector3.Dot(v, Dir(yaw));
        SkateKeys.EndStep();
        hasLast = false; Steps = 0; Resets = 0; Top = 0f; Lean = 0f; lastPull = mountAt = Time.realtimeSinceStartup;
        // A cap from a ride a while ago says nothing about this one.
        if (mountAt - putBackAt > CapForget) ForgetServer();
        VSpeed = 0f; AirTime = 0f; flipLeft = shoveLeft = pendShove = bodyLeft = 0f; pendFlip = 0; SpinDeg = FlipDeg = ShoveDeg = BodyDeg = 0; LateTrick = false; Jumped = TrickAir = Braking = Manual = NoseManual = spinning = DeckReversed = false; PushPhase = 0f; stuck = 0; heldSteps = 0; Held = false;
        Grab = SkateGrabKind.None; spinSide = 1f; wantKind = SkatePopKind.Ollie; wantFlip = 0; wantShove = 0f; wantScale = 1f;
        PumpHeld = RevertLeft = 0f; landedTrick = fastFakie = false; steppedAt = -99f; StepUps = 0; Pump = default(SkatePumpState); SlopeDegrees = 0f; ContactNormal = Vector3.up; GrindKind = SkateGrindKind.None; GrindFrontside = GrindAlong = false; GrindDeck = 0f; Stalling = false; Stalls = 0; PlantedUntil = -99f; PushMongo = PushLead = false; handDownAt = -99f;
        Trick = new SkateTrickState(false); TrickName = ""; ComboPoints = 0; TrickAt = BankedAt = LandAt = jumpWanted = -99f; OffReason = "";
        SkateHud.Clear();
        Normal = ViewNormal = Vector3.up; Position = b.position; Tangent = Dir(yaw);
        airNoseOffset = airDeckOffset = 0f;
        SkateGrind.Reset();
        // Getting on while going up (the second press of a double jump) finishes that jump on the
        // board; the ground is still within reach of the feet then, and ground movement would cut it.
        if (rise > 1f) TakeOff(false, rise);
        else
        {
            Enter(RideMode.Ground); Sense(Position); Tangent = SurfaceTangent(Normal, Dir(Yaw));
            Speed = Vector3.Dot(velocity, Tangent);
            LimitSpeed(Cap, Math.Min(MaxReverse, Cap));
        }
    }

    public static void Dismount(string reason)
    {
        if (Mode == RideMode.Off) return;
        Mode = RideMode.Off; Pump = default(SkatePumpState); PumpHeld = RevertLeft = 0f; Stalling = false; OffReason = reason; Grab = SkateGrabKind.None; Manual = NoseManual = false;
        SkateKeys.EndStep();
    }

    private static void Enter(RideMode m)
    {
        // A pump's pass lives on the ground: leaving it (air, grind, bail) forgets the charge, so a
        // crouch let go in the air cannot be paid out on the next ground step.
        if (m != RideMode.Ground && Mode == RideMode.Ground) { Pump = default(SkatePumpState); PumpHeld = 0f; RevertLeft = 0f; }
        Mode = m; ModeAt = Time.realtimeSinceStartup;
    }

    public static string Status()
    {
        return Mode + " speed=" + HorizontalSpeed.ToString("F1") + " actual=" + Actual.ToString("F1") + " top=" + Top.ToString("F1") + " cap=" + Cap.ToString("F1") + " yaw=" + Yaw.ToString("F0")
            + " grounded=" + Grounded + " pull-backs=" + Resets + " total=" + Trick.TotalPoints + (body != null ? " pos=" + Out.V1(body.position) : "");
    }

    public static void Step()
    {
        if (Mode == RideMode.Off) return;
        try
        {
            if (body == null) { Dismount("the movement body is gone"); return; }
            if (Frozen) { hasLast = false; return; }
            var dt = Time.fixedDeltaTime;
            if (!(dt > 0.001f && dt <= 0.05f)) dt = 0.03125f;
            now = Time.realtimeSinceStartup;
            var pos = body.position; var game = body.linearVelocity;
            Position = pos;
            if (pos.y < SeaLevel && OpenSky(pos)) { Dismount("in the water"); return; }
            Measure(pos, dt);
            Sense(pos);
            if (Mode != RideMode.Grind) SkateGrind.Tick(dt);
            if (SkateKeys.JumpPressed) { jumpWanted = now; wantKind = SkateKeys.PopKind; wantFlip = SkateKeys.PopFlip; wantShove = SkateKeys.PopShove; wantScale = SkateKeys.PopScale; }
            if (Mode == RideMode.Ground) GroundStep(dt);
            else if (Mode == RideMode.Air) AirStep(pos, dt);
            else if (Mode == RideMode.Grind) GrindStep(pos, dt);
            else BailStep(game, dt);
            if (Mode == RideMode.Off) return;
            if (Cap < MaxSpeed && now - lastPull > raiseWait)
            {
                Cap = Math.Min(MaxSpeed, Cap + (refusedSinceRaise ? 0.5f : 1f));
                if (!refusedSinceRaise) raiseWait = RaiseWait;
                refusedSinceRaise = false; lastPull = raisedAt = now;
            }
            var flat = Flat(velocity); var pace = flat.magnitude;
            if (pace > Cap && pace > 0f)
            {
                var scale = Cap / pace; velocity.x *= scale; velocity.z *= scale;
                if (Mode == RideMode.Ground) velocity.y += Tangent.y * Speed * (scale - 1f);
                Speed *= scale; if (Mode == RideMode.Grind) SkateGrind.Speed = Speed;
                flat = Flat(velocity); pace = flat.magnitude;
            }
            if (pace > Top) Top = pace;
            body.linearVelocity = velocity;
            var sign = Speed < 0f ? -1f : 1f;
            lastPos = pos; lastDir = pace > 0.0001f ? flat / pace * sign : Dir(Yaw); lastSpeed = pace * sign; hasLast = true; Steps++;
            var target = Mode == RideMode.Ground ? Normal : Vector3.up;
            ViewNormal = (ViewNormal + (target - ViewNormal) * 0.3f).normalized;
            SkateKeys.EndStep();
        }
        catch (Exception e) { Dismount("the step threw"); Out.Say("SKATE step threw " + e.GetType().Name + ": " + e.Message); }
    }

    private static void Measure(Vector3 pos, float dt)
    {
        if (!hasLast) return;
        var moved = pos - lastPos;
        ActualV = moved.y / dt; moved.y = 0f;
        Actual = Vector3.Dot(moved, lastDir) / dt;
        // Through a revert the board's heading and the way of travel part: no pull-back can be read.
        if (RevertLeft != 0f || now - revertEndAt < 0.15f) return;
        if ((lastSpeed > 1f && Actual < -3f) || (lastSpeed < -1f && Actual > 3f))
        {
            // The server put the player back. That is logged and nothing more: the board never
            // slows itself down for the server. A server that refuses skate speed has to allow it
            // (the ShortcutSkate plugin, or its own speed checks turned off by the admin).
            Resets++;
            Out.Say("SKATE put back #" + Resets + ", " + (now - mountAt).ToString("F2") + " s after getting on: moved " + Out.V3(pos - lastPos) + " in a step at speed " + lastSpeed.ToString("F1"));
            return;
        }
        if (now - steppedAt < StepGrace) { heldSteps = 0; return; }
        // Held back by anything on the ground or along an edge, a wall, a post, a fence: the board
        // did not get as far as it rode for a few steps in a row. Its speed then comes down to what
        // it actually does, so the meter and the tricks do not run on against the wall; a little is
        // left so that a push away from it starts again. A single short step is not that: a lip
        // stepped over, a slope, a frame of the server's.
        var rode = Math.Abs(Speed); var got = Math.Abs(Actual);
        if ((Mode == RideMode.Ground || Mode == RideMode.Grind) && rode > 1f && got < rode * HeldShare) heldSteps++; else heldSteps = 0;
        Held = heldSteps >= HeldSteps;
        if (Held)
        {
            if (heldSteps == HeldSteps) Out.Say("SKATE held back: rode " + rode.ToString("F1") + " m/s, got " + got.ToString("F1") + (Mode == RideMode.Grind ? " on the grind" : ""));
            if (Mode != RideMode.Grind) { var sign = Speed < 0f ? -1f : 1f; Speed = sign * Math.Min(rode, got + HeldLeft); }
            return;
        }
        // A wall right in front stops the board at once: it did not get as far as it rode and
        // something steep stands right in front of it.
        if (rode < 0.5f || got > rode * 0.5f) return;
        RaycastHit wall;
        var ahead = new Vector3(lastDir.x, 0f, lastDir.z) * Math.Sign(Speed);
        if (ahead.sqrMagnitude < 0.0001f || !Physics.Raycast(pos + Vector3.up * 0.3f, ahead.normalized, out wall, 0.6f, GroundMask, QueryTriggerInteraction.Ignore) || wall.m_Normal.y > 0.5f) return;
        if (Speed > 0f) LimitSpeed(Math.Max(0f, Actual) + 2f, Cap);
        else LimitSpeed(Cap, Math.Max(0f, -Actual) + 2f);
    }

    // Land a metre below the water's surface is for swimming, unless something is overhead (tunnels,
    // basements); the shallows along the shore are rideable.
    private static bool OpenSky(Vector3 pos)
    {
        RaycastHit hit;
        return !Physics.Raycast(pos + Vector3.up * 2f, Vector3.up, out hit, 60f, GroundMask, QueryTriggerInteraction.Ignore);
    }

    private static void Sense(Vector3 pos)
    {
        RaycastHit hit;
        var reach = Mode == RideMode.Air ? AirProbe : FeetProbe;
        var got = Physics.Raycast(pos + Vector3.up * RayLift, Vector3.down, out hit, RayLift + reach, GroundMask, QueryTriggerInteraction.Ignore);
        Clearance = got ? hit.m_Distance - RayLift : 99f;
        Grounded = got && Clearance <= FeetProbe;
        Normal = Grounded && hit.m_Normal.y > 0.3f ? hit.m_Normal : Vector3.up;
        if (got && hit.m_Normal.y > 0.3f) ContactNormal = hit.m_Normal;
    }

    private static void GroundStep(float dt)
    {
        if (!Grounded) { Launch(Tangent, 0f, dt); Sense(Position); AirStep(Position, dt); return; }
        var before = Yaw;
        var maxTurn = TurnRate / (1f + Math.Abs(Speed) / 8f) * dt;
        // A stick steers by how far it is pushed; keys and the mouse turn the board toward where the rider looks.
        if (RevertLeft != 0f) { }
        else if (SkateKeys.Analog) Yaw += Clamp(SkateKeys.Steer, -1f, 1f) * maxTurn;
        else Yaw += Clamp(Delta(Yaw, SkateKeys.LookYaw + ((SkateKeys.Left ? -1f : 0f) + (SkateKeys.Right ? 1f : 0f)) * 40f), -maxTurn, maxTurn);
        if (now - jumpWanted >= 0.12f) Revert(dt);
        var dir = RevertLeft != 0f ? revertDir * revertSign : Dir(Yaw);
        var tangent = SurfaceTangent(Normal, dir);
        // The shared physics: gravity along the tangent of the ground, the slope signed by the way of travel.
        SkateSlopeResult slope; string error;
        if (SkateRidePhysics.TrySlope(new SkateSlopeInput(V(Normal), V(dir), Speed, AirGravity), dt, out slope, out error))
        {
            tangent = V(slope.Tangent); Speed = (float)slope.SignedTangentSpeed; SlopeDegrees = (float)slope.SignedSlopeDegrees;
        }
        else { Speed += Vector3.Dot(Physics.gravity, tangent) * SlopeGain * dt; Physics_(error); }
        Tangent = tangent; Braking = SkateKeys.Brake;
        // A press up to a moment before touch-down still counts, so tricks can be chained.
        if (now - jumpWanted < 0.12f)
        {
            Pops++; jumpWanted = -99f;
            if (SkateTricks.IsPlant(wantKind)) PlantedUntil = now + PlantSeconds;
            Launch(tangent, OlliePop * wantScale * PopHeight(wantKind), dt);
            AirStep(Position, dt);
            return;
        }
        PumpStep(dt);
        // A manual is held on two wheels: it needs some speed and leaves no foot free to push. One
        // that has begun lasts down to a crawl: ending it at the pace it needs to begin would let a
        // held push in and out every few tenths of a second, each time ending and scoring it.
        var pace = Math.Abs(Speed);
        Manual = SkateKeys.Manual && !SkateKeys.Brake && pace > (Manual ? ManualHold : ManualPace);
        NoseManual = !Manual && SkateKeys.NoseManual && !SkateKeys.Brake && pace > (NoseManual ? ManualHold : ManualPace);
        var h = HorizontalFactor;
        var pushLimit = h > 0.0001f ? MaxPush / h : 0f;
        // Rolling tail first, a push drives the board on the way it rolls (a fakie push), not to a stop.
        var fakiePush = Speed < -0.3f;
        var pushing = SkateKeys.Push && !SkateKeys.Brake && !Manual && !NoseManual && (fakiePush ? Speed > -pushLimit : Speed < pushLimit);
        if (pushing && fakiePush) { Speed = Math.Max(-pushLimit, Speed - PushAccel * dt); fastFakie = true; }
        else if (pushing) Speed = Math.Min(pushLimit, Speed + PushAccel * dt);
        if (SkateKeys.Brake) Speed = Toward(Speed, 0f, BrakeDecel * dt);
        Speed = Toward(Speed, 0f, RollDecel * dt);
        // Tail first is slow going, except out of a revert: that keeps the pace it had, until it drops.
        if (HorizontalSpeed > -MaxReverse) fastFakie = false;
        LimitSpeed(Cap, fastFakie ? Cap : Math.Min(MaxReverse, Cap));
        if (pushing || PushPhase > 0f)
        {
            if (PushPhase <= 0f) { Pushes++; PushMongo = SkateKeys.Mongo; PushLead = PushMongo != fakiePush; }
            // While the foot is on the ground it stays where it came down and the board rolls on
            // past it: the stroke runs by the distance the board moves, not by the clock (a board
            // that cannot move gets the slow clock), so the foot never slides along the ground.
            var stroke = (float)(SkateRider.PushPlant + SkateRider.PushSweep);
            var span = (float)(SkateRider.PushUp - SkateRider.PushDown);
            var down = PushPhase > (float)SkateRider.PushDown && PushPhase < (float)SkateRider.PushUp;
            PushPhase += down ? Math.Max(dt / (PushPeriod * 3f), Math.Abs(Speed) * dt / stroke * span) : dt / PushPeriod;
            if (PushPhase >= 1f) PushPhase = 0f;
        }
        // The velocity runs along the ground's tangent; a gap under the wheels is closed on top of it.
        velocity = tangent * Speed;
        velocity.y -= Clamp(Clearance * 8f, 0f, 3f);
        StepOver(dir, dt);
        Lean += (Clamp(Delta(before, Yaw) / dt * 0.12f, -25f, 25f) - Lean) * 0.2f;
        TrickTick(false, false, 0, 0, 0, 0, dt);
    }

    // The ground a little ahead, in the way of travel: higher by a lip's worth, the board is lifted
    // over it within the step, which the game's own fixed step then carries.
    private static void StepOver(Vector3 dir, float dt)
    {
        if (Math.Abs(Speed) < 0.5f) return;
        var ahead = Position + dir * (Math.Sign(Speed) * StepLook);
        RaycastHit hit;
        if (!Physics.Raycast(ahead + Vector3.up * (StepUp + 0.3f), Vector3.down, out hit, StepUp + 0.3f + FeetProbe, GroundMask, QueryTriggerInteraction.Ignore)) return;
        var rise = hit.m_Point.y - Position.y;
        if (rise < 0.03f || rise > StepUp || hit.m_Normal.y < 0.7f) return;
        // A lip has a face: a ray along the ground from the wheels' height must meet something steep
        // before the spot ahead. A smooth bank rises as much but has no face, and is ridden up.
        RaycastHit face;
        var flat = new Vector3(dir.x, 0f, dir.z); if (flat.sqrMagnitude < 0.0001f) return; flat = flat.normalized;
        if (!Physics.Raycast(Position + Vector3.up * 0.02f, flat * Math.Sign(Speed), out face, StepLook + 0.05f, GroundMask, QueryTriggerInteraction.Ignore) || face.m_Normal.y > 0.5f) return;
        var lift = rise / Math.Max(dt, 0.001f) * 0.6f;
        if (velocity.y < lift) velocity.y = lift;
        if (now - steppedAt > StepGrace) StepUps++;
        steppedAt = now;
    }

    // Right after a landed trick a fresh turn of the stick turns the board round on the ground: the
    // rider rolls on the same way, the other end leading, and the combo stays open.
    private static void Revert(float dt)
    {
        if (RevertLeft == 0f)
        {
            // A stick that was already steering through the landing is a carve, not a revert: the turn
            // must be asked for afresh, from the middle, and hard.
            if (!landedTrick || now - LandAt > RevertWindow || !SkateKeys.SpinPressed || SkateKeys.Spin == 0f || Math.Abs(Speed) < 1f) return;
            if (spinAtLand != 0f || (SkateKeys.Analog && Math.Abs(SkateKeys.Steer) < RevertStick)) return;
            landedTrick = false; RevertLeft = 180f * (SkateKeys.Spin > 0f ? 1f : -1f); Reverts++; revertSign = Math.Sign(Speed); revertDir = Dir(Yaw) * revertSign;
            Out.Say("SKATE revert begins");
        }
        var step = Math.Min(Math.Abs(RevertLeft), RevertRate * dt) * Math.Sign(RevertLeft);
        Yaw += step; RevertLeft -= step;
        if (Math.Abs(RevertLeft) > 0.001f) return;
        // Round: the board points the other way and the rider rolls on as before, the other end leading.
        RevertLeft = 0f; Speed = -Speed; revertEndAt = now; fastFakie = HorizontalSpeed < -MaxReverse;
        SkateTrickResult r; string error;
        if (SkateTricks.TryAdd(Trick, "Revert", RevertPoints, true, out r, out error)) Take(r);
        else Out.Say("SKATE revert refused: " + error);
    }

    // The crouch is held with a trigger; the shared pump judges the pass: charged going down a
    // transition, awarded when let go through its bottom, once per pass, by an energy budget.
    private static void PumpStep(float dt)
    {
        PumpHeld = SkateKeys.Crouch ? Math.Min(PumpCharge, PumpHeld + dt) : 0f;
        var travel = Flat(Tangent) * (Speed >= 0f ? 1f : -1f);
        SkatePumpResult r; string error;
        var input = new SkatePumpInput(Mode == RideMode.Ground, SkateKeys.Crouch, SlopeDegrees, Math.Abs(Speed), new SkateVector(travel.x, 0, travel.z));
        if (!SkateRidePhysics.TryPump(Pump, input, dt, out r, out error)) { Physics_(error); return; }
        Pump = r.State;
        if (!r.Awarded) return;
        Speed += Math.Sign(Speed) * (float)r.SpeedGain; Pumps++;
        Out.Say("SKATE pump +" + r.SpeedGain.ToString("F1") + " m/s (" + r.EnergyGain.ToString("F1") + " J/kg) through the bottom at " + SlopeDegrees.ToString("F0") + " degrees");
    }

    // Leaving the ground: the velocity along it is kept whole, the pop added straight up; the air
    // step takes its gravity off at once, which the launch gives back for that one step.
    private static void Launch(Vector3 tangent, float pop, float dt)
    {
        SkateLaunchResult r; string error;
        var surface = tangent * Speed;
        Vector3 outgoing;
        if (SkateRidePhysics.TryLaunch(new SkateLaunchInput(V(surface), pop), out r, out error)) outgoing = V(r.Velocity);
        else { Physics_(error); outgoing = surface + Vector3.up * pop; }
        var nose = NoseYaw; var deck = DeckYaw;
        var flat = Flat(outgoing); var sign = Speed < 0f ? -1f : 1f;
        if (flat.sqrMagnitude > 0.0001f) Yaw = (float)(Math.Atan2(flat.x * sign, flat.z * sign) * 180.0 / Math.PI);
        Speed = flat.magnitude * sign;
        TakeOff(pop > 0f, outgoing.y + AirGravity * dt);
        KeepAirPose(nose, deck);
    }

    private static void KeepAirPose(float nose, float deck)
    {
        airNoseOffset = Delta(Yaw + (Trick.Switch ? 180f : 0f), nose);
        airDeckOffset = Delta(nose + (DeckReversed ? 180f : 0f), deck);
    }

    private static int physicsSaid;
    private static void Physics_(string error) { PhysicsError = error; if (physicsSaid++ < 3) Out.Say("SKATE physics refused: " + error); }
    private static SkateVector V(Vector3 v) { return new SkateVector(v.x, v.y, v.z); }
    private static Vector3 V(SkateVector v) { return new Vector3((float)v.X, (float)v.Y, (float)v.Z); }

    // A no comply pops lower than an ollie (the front foot is off the board), a boneless higher
    // (the rider jumps off his planted foot).
    public static float PopHeight(SkatePopKind kind)
    {
        return kind == SkatePopKind.NoComply ? 0.85f : kind == SkatePopKind.Boneless ? 1.12f : kind == SkatePopKind.Fastplant ? 1.1f : kind == SkatePopKind.Beanplant ? 1.05f : 1f;
    }

    private static void TakeOff(bool jumped, float vertical)
    {
        // Read while the board still rolls: which end leads decides which way is frontside.
        spinSide = (TailFirst ? -1f : 1f) * StanceSign;
        Enter(RideMode.Air);
        // A manual survives a hop over a bump; a jump ends it, and so does a longer fall below.
        Jumped = jumped; TrickAir = jumped; if (jumped) Manual = NoseManual = false; VSpeed = vertical; AirTime = 0f; AirPeak = 0f; takeOffY = Position.y;
        flipLeft = shoveLeft = bodyLeft = 0f; spinning = false; PushPhase = 0f; Braking = false; stuck = 0; heldSteps = 0; Held = false; LateTrick = false;
        popKind = jumped ? wantKind : SkatePopKind.Ollie; pendFlip = jumped ? wantFlip : 0; pendShove = jumped ? wantShove : 0f;
        wantFlip = 0; wantShove = 0f; wantKind = SkatePopKind.Ollie;
    }

    private static float TimeToGround()
    {
        var h = Clearance > AirProbe ? AirProbe : Clearance < 0f ? 0f : Clearance;
        return (VSpeed + (float)Math.Sqrt(VSpeed * VSpeed + 2f * AirGravity * h)) / AirGravity;
    }

    private static void AirStep(Vector3 pos, float dt)
    {
        AirTime += dt;
        if (AirTime > HopGrace) Manual = NoseManual = false;
        VSpeed -= AirGravity * dt; if (VSpeed < -MaxFall) VSpeed = -MaxFall;
        if (pos.y - takeOffY > AirPeak) AirPeak = pos.y - takeOffY;
        var dir = Dir(Yaw);
        Tangent = dir; Lean += (0f - Lean) * 0.2f;

        // A rotation is only started when the arc lasts long enough to finish it: an unfinished one is a bail.
        var left = (float)(TimeToGround() - 0.03);
        var flips = pendFlip + SkateKeys.LateFlip; var lateFlips = SkateKeys.LateFlip != 0 && Jumped && AirTime > LateAfter; pendFlip = 0;
        if (flips != 0 && (Math.Abs(flipLeft) < 0.001f || Math.Sign(flipLeft) == Math.Sign(flips)))
        {
            if (left >= (Math.Abs(flipLeft) + 360f * Math.Abs(flips)) / (float)SkateMotion.FlipRate) { flipLeft += 360f * flips; TrickAir = true; if (lateFlips) LateTrick = true; }
            else Refused++;
        }
        // A shove asked for in the air goes on top of what is under way, the same way round.
        var lateShove = Jumped ? SkateKeys.LateShove : 0f;
        if (lateShove != 0f && (Math.Abs(shoveLeft) < 0.001f || Math.Sign(shoveLeft) == Math.Sign(lateShove))) pendShove += lateShove;
        if (pendShove != 0f)
        {
            if (left >= (Math.Abs(shoveLeft) + Math.Abs(pendShove)) / (float)SkateMotion.ShoveRate) { shoveLeft += pendShove; TrickAir = true; if (lateShove != 0f && AirTime > LateAfter) LateTrick = true; }
            else Refused++;
            pendShove = 0f;
        }
        // A body varial: the rider turns a half round, the board keeps its yaw. Only when the arc
        // lasts long enough for it; one per flight.
        var body = Jumped ? SkateKeys.BodyVarial : 0f;
        if (body != 0f && Math.Abs(bodyLeft) < 0.001f && Math.Abs(BodyDeg) < 0.001)
        {
            if (left >= Math.Abs(body) / (float)SkateMotion.RotationRate) { bodyLeft = body; TrickAir = true; }
            else Refused++;
        }
        var spinAxis = SpinAxis(left, dt);
        Grab = TrickAir ? SkateKeys.Grab : SkateGrabKind.None;

        var flipStep = (float)SkateMotion.FlipRate * dt; var shoveStep = (float)SkateMotion.ShoveRate * dt; var bodyStep = (float)SkateMotion.RotationRate * dt;
        var flipAxis = Clamp(flipLeft / flipStep, -1f, 1f); var shoveAxis = Clamp(shoveLeft / shoveStep, -1f, 1f); var bodyAxis = Clamp(bodyLeft / bodyStep, -1f, 1f);

        stuck = VSpeed < -2f && Math.Abs(ActualV) < 0.3f && AirTime > 0.2f ? stuck + 1 : 0;
        if ((VSpeed <= 0f && Clearance <= Math.Max(0.04f, -VSpeed * dt + 0.02f)) || stuck >= 3)
        {
            var recovery = stuck >= 3;
            var incoming = dir * Speed + Vector3.up * VSpeed;
            var impact = recovery ? 0f : Math.Max(0f, -VSpeed);
            Land(incoming, recovery, dt);
            if (Mode == RideMode.Ground)
            {
                velocity = hasLandRoll ? landRoll : dir * Speed;
                velocity.y = (hasLandRoll ? landRoll.y : 0f) - Clamp(Clearance / dt, 0f, impact);
            }
            else BailStep(incoming, dt);
            return;
        }
        velocity = dir * Speed + Vector3.up * VSpeed;
        if (VSpeed < 0f && Jumped && !Rotating && SkateGrind.TryCapture(pos, velocity, NoseYaw, dt))
        {
            LastAir = AirLine("grind");
            var impact = -VSpeed;
            ClassifyGrind(pos);
            Enter(RideMode.Grind); GrindStarts++;
            Yaw = SkateGrind.Heading; Speed = SkateGrind.Speed; VSpeed = 0f;
            LimitSpeed(Cap, Cap);
            TrickTick(false, true, 0, 0, 0, impact, dt);
            TrickAir = Jumped = false; AirTime = 0f; Grab = SkateGrabKind.None;
            if (Mode == RideMode.Grind) GrindVelocity(pos);
            return;
        }
        if (VSpeed < 0f && Jumped && !Rotating && SkateGrind.StallCandidate && Math.Abs(Speed) < StallSpeed)
        {
            BeginStall(pos, dt);
            return;
        }
        // A fall without a jump is no trick, and it is not rolling either: the trick module is not
        // stepped, so an open combo neither banks nor is lost until the rider is down.
        if (TrickAir)
        {
            TrickTick(true, false, spinAxis, flipAxis, shoveAxis, 0, dt, bodyAxis);
            flipLeft -= flipAxis * flipStep; shoveLeft -= shoveAxis * shoveStep; bodyLeft -= bodyAxis * bodyStep;
        }
    }

    // The rider turns for as long as the turn is held and the next half turn can still be reached
    // before the ground; from there on, and when the turn is let go, he is brought round to the
    // nearest half turn. So a turn never ends between two half turns, which would be a bail: the
    // landing itself takes a few degrees either way.
    private static float SpinAxis(float left, float dt)
    {
        var rate = (float)SkateMotion.RotationRate; var step = rate * dt;
        var want = -SkateKeys.Spin * spinSide;
        // A fall without a jump becomes a trick when a turn is asked for afresh, not by a stick that
        // was steering when the ground ended.
        if (!TrickAir && !(want != 0f && SkateKeys.SpinPressed)) { SpinLead = 0f; return 0f; }
        var at = TrickAir ? (float)Trick.Spin : 0f;
        if (want != 0f)
        {
            var next = at + want * step;
            var ahead = want > 0f ? (float)Math.Ceiling(next / 180.0 - 0.0001) * 180f - next : next - (float)Math.Floor(next / 180.0 + 0.0001) * 180f;
            if (ahead <= (left - dt) * rate + SpinSlack) { TrickAir = spinning = true; SpinLead = want * SpinLeadMost; return want; }
            if (SkateKeys.SpinPressed) Refused++;
        }
        if (!TrickAir) { SpinLead = 0f; return 0f; }
        var rest = (float)Math.Round(at / 180.0, MidpointRounding.AwayFromZero) * 180f - at;
        spinning = Math.Abs(rest) > 1f;
        SpinLead = Clamp(rest / 90f, -1f, 1f) * SpinLeadMost;
        return Clamp(rest / step, -1f, 1f);
    }

    private static string AirLine(string end)
    {
        return "air " + end + " time=" + AirTime.ToString("F2") + " peak=" + AirPeak.ToString("F2") + " impact=" + (-VSpeed).ToString("F1") + " spin=" + SpinDeg.ToString("F0") + " flip=" + FlipDeg.ToString("F0")
            + (Math.Abs(ShoveDeg) > 0.5 ? " shove=" + ShoveDeg.ToString("F0") : "") + " jumped=" + Jumped + " speed=" + Speed.ToString("F1");
    }

    private static void Land(Vector3 incoming, bool recovery, float dt)
    {
        LastAir = AirLine("land");
        landRoll = Vector3.zero; hasLandRoll = false;
        var normal = Grounded ? Normal : ContactNormal;
        if (recovery) incoming.y = 0f;
        var impact = 0f; var r = default(SkateLandingResult); string error = null;
        var valid = !recovery && SkateRidePhysics.TryLand(new SkateLandingInput(V(incoming), V(normal), V(Dir(Yaw)), BailImpact, LandRoll), out r, out error);
        if (valid) impact = r.Bailed ? Math.Max((float)r.NormalImpact, BailImpact + 0.01f) : (float)r.NormalImpact;
        else if (!recovery) { Physics_(error); Bail(); return; }
        if (AirTime > 0.12f || impact > 1.5f) { Lands++; LandAt = now; LandImpact = impact; }
        landedTrick = TrickAir; spinAtLand = SkateKeys.Spin;
        Enter(RideMode.Ground);
        if (TrickAir) TrickTick(false, false, 0, 0, 0, impact, dt);
        else if (impact > BailImpact) Bail();
        else TrickTick(false, false, 0, 0, 0, 0, dt);
        if (Mode == RideMode.Ground)
        {
            Yaw += airNoseOffset + airDeckOffset; airNoseOffset = airDeckOffset = 0f;
            SpinDeg = FlipDeg = ShoveDeg = 0;
            var axis = Dir(Yaw + (Trick.Switch ? 180f : 0f) + (DeckReversed ? 180f : 0f));
            if (!SkateRidePhysics.TryLand(new SkateLandingInput(V(incoming), V(normal), V(axis), BailImpact, recovery ? 0f : LandRoll), out r, out error))
            { Physics_(error); Speed = Flat(incoming).magnitude * (Speed < 0f ? -1f : 1f); Bail(); return; }
            Tangent = SurfaceTangent(normal, Dir(Yaw)); Normal = normal;
            Speed = Vector3.Dot(V(r.RollVelocity), Tangent);
            LimitSpeed(Cap, fastFakie ? Cap : Math.Min(MaxReverse, Cap));
            landRoll = Tangent * Speed; hasLandRoll = true;
        }
        flipLeft = shoveLeft = 0f; spinning = false; TrickAir = Jumped = false; Grab = SkateGrabKind.None; AirTime = 0f;
    }

    private static void TrickTick(bool airborne, bool grinding, float spinAxis, float flipAxis, float shoveAxis, float impact, float dt, float bodyAxis = 0f)
    {
        SkateTrickResult r; string error;
        var rolling = !airborne && !grinding;
        var input = new SkateTrickInput(spinAxis, flipAxis, shoveAxis, airborne ? Grab : SkateGrabKind.None, airborne, grinding, impact, Manual && rolling, NoseManual && !Manual && rolling,
            Math.Abs(Speed), airborne ? popKind : SkatePopKind.None, grinding ? GrindKind : SkateGrindKind.None, grinding && GrindFrontside,
            airborne && LateTrick && (flipAxis != 0f || shoveAxis != 0f), airborne ? bodyAxis : 0f);
        if (!SkateTricks.TryStep(Trick, input, dt, BailImpact, out r, out error))
        {
            Out.Say("SKATE trick step refused: " + error);
            return;
        }
        SpinDeg = airborne ? r.BoardSpin : 0; FlipDeg = airborne ? r.BoardFlip : 0; ShoveDeg = airborne ? r.BoardShove : 0; BodyDeg = airborne ? r.BodySpin : 0;
        Take(r);
    }

    private static void Take(SkateTrickResult r)
    {
        Trick = r.State; ComboPoints = r.ComboPoints;
        if ((r.Events & SkateTrickEvents.Bailed) != 0) { Bail(); return; }
        // The landing hands the turns back rounded: an odd number of half shoves turned the board round.
        if ((r.Events & SkateTrickEvents.Landed) != 0 && ((long)Math.Round(r.BoardShove / 180.0) & 1) != 0) DeckReversed = !DeckReversed;
        // A body varial of an odd half round leaves the deck the other way round under the feet as well.
        if ((r.Events & SkateTrickEvents.Landed) != 0 && ((long)Math.Round(r.BodySpin / 180.0) & 1) != 0) DeckReversed = !DeckReversed;
        if ((r.Events & (SkateTrickEvents.Landed | SkateTrickEvents.GrindEnded | SkateTrickEvents.ManualEnded)) != 0)
        {
            TrickName = r.TrickName;
            TrickPoints = r.Points; TrickAt = now;
            if (r.Points > 0) SkateHud.Landed(r.TrickName, r.Points);
            Out.Say("SKATE trick " + r.TrickName + " +" + r.Points + " combo=" + r.ComboPoints + " x" + Trick.ComboCount + " switch=" + Trick.Switch);
        }
        if ((r.Events & SkateTrickEvents.ComboBanked) != 0)
        {
            Banked = r.BankedPoints; BankedAt = now;
            SkateHud.Banked(r.BankedPoints, Trick.TotalPoints);
            Out.Say("SKATE combo banked +" + r.BankedPoints + " total=" + Trick.TotalPoints);
        }
    }

    public static void Bail()
    {
        if (Mode == RideMode.Bail || Mode == RideMode.Off) return;
        if (Stalling) { Stalling = false; Manual = NoseManual = false; SkateGrind.Release(0.02f); }
        SkateTrickResult r; string error;
        SkateHud.Bailed(ComboPoints);
        if (SkateTricks.TryBail(Trick, out r, out error)) Trick = r.State;
        ComboPoints = 0; Manual = NoseManual = false;
        if (Mode == RideMode.Grind) { GrindEnds++; Yaw = SkateGrind.Heading; SkateGrind.Release(Time.fixedDeltaTime); }
        Speed = HorizontalSpeed;
        Enter(RideMode.Bail); Bails++;
        TrickName = "Bail"; TrickPoints = 0; TrickAt = now;
        flipLeft = shoveLeft = 0f; spinning = false; TrickAir = Jumped = false; Grab = SkateGrabKind.None;
        Out.Say("SKATE bail | " + LastAir);
    }

    private static void BailStep(Vector3 game, float dt)
    {
        Speed = Toward(Speed, 0f, BailDecel * dt);
        velocity = Dir(Yaw) * Speed; velocity.y = game.y < 0f ? game.y : 0f;
        if (now - ModeAt > BailSeconds) Dismount("bail");
    }

    // How the board meets the edge it stops on: square with a grab held is a handplant, with the
    // stick back or forward a tail or nose stall, otherwise a board stall; slowly along it, a 50-50 stall.
    private static void BeginStall(Vector3 pos, float dt)
    {
        LastAir = AirLine("stall");
        var impact = -VSpeed;
        stallSign = Speed < 0f ? -1f : 1f;
        // A handplant is asked for with a bumper (LB frontside, RB backside) or, on the keyboard, the grab key.
        var plant = SkateKeys.HandplantFront || SkateKeys.HandplantBack || (!SkateKeys.Pad && (Grab != SkateGrabKind.None || SkateKeys.Grab != SkateGrabKind.None));
        GrindKind = plant ? SkateGrindKind.Handplant : !SkateGrind.StallSquare ? SkateGrindKind.FiftyFiftyStall
            : SkateKeys.Manual ? SkateGrindKind.TailStall : SkateKeys.NoseManual ? SkateGrindKind.NoseStall : SkateGrindKind.BoardStall;
        SkateGrind.BeginStall(pos);
        var to = SkateGrind.Target - pos; to.y = 0f;
        GrindFrontside = Vector3.Dot(to, Dir(NoseYaw + 90f * StanceSign)) > 0f; GrindAlong = false;
        if (GrindKind == SkateGrindKind.Handplant && (SkateKeys.HandplantFront || SkateKeys.HandplantBack)) GrindFrontside = SkateKeys.HandplantFront;
        Enter(RideMode.Grind); GrindStarts++; Stalls++; Stalling = true; stallAt = now; handDownAt = -99f;
        Speed = 0f; VSpeed = 0f;
        TrickTick(false, true, 0, 0, 0, impact, dt);
        TrickAir = Jumped = false; AirTime = 0f; Grab = SkateGrabKind.None;
        Manual = GrindKind == SkateGrindKind.TailStall; NoseManual = GrindKind == SkateGrindKind.NoseStall;
        StallVelocity(pos);
        Out.Say("GRIND " + SkateTricks.GrindName(GrindKind, GrindFrontside) + " begins");
    }

    // Held still on the edge until a pop or StallMax; then back the way the board came, tail first
    // (a rock to fakie), or nose first out of a handplant, which comes round.
    private static void StallStep(Vector3 pos, float dt)
    {
        var popped = now - jumpWanted < 0.12f;
        var leaving = popped || now - stallAt > StallMax;
        if (GrindKind == SkateGrindKind.Handplant)
        {
            // The pop (or the time) starts the way down; the edge is left once the body is back on the board.
            if (leaving && handDownAt < 0f) { handDownAt = now; handDownPop = popped; jumpWanted = -99f; }
            leaving = handDownAt > 0f && now - handDownAt >= HandDownSeconds;
            popped = leaving && handDownPop;
        }
        if (leaving)
        {
            Stalling = false; Manual = NoseManual = false; GrindEnds++; SkateGrind.Release(dt);
            if (GrindKind == SkateGrindKind.Handplant) { Yaw += 180f; Speed = stallSign * StallOut; }
            else Speed = -stallSign * StallOut;
            if (popped)
            {
                Pops++; jumpWanted = -99f;
                TakeOff(true, OlliePop * wantScale * PopHeight(wantKind) + AirGravity * dt);
                AirStep(pos, dt);
            }
            else
            {
                TakeOff(false, 0f);
                velocity = Dir(Yaw) * Speed;
                TrickTick(false, false, 0, 0, 0, 0, dt);
            }
            return;
        }
        SkateGrind.HoldStall(pos);
        StallVelocity(pos);
        TrickTick(false, true, 0, 0, 0, 0, dt);
    }

    private static void StallVelocity(Vector3 pos)
    {
        var to = SkateGrind.Target - pos;
        var side = to; side.y = 0f;
        var pull = side * 8f;
        if (pull.sqrMagnitude > 6.25f) pull = pull.normalized * 2.5f;
        velocity = pull;
        velocity.y = Clamp(to.y * 10f, -3f, 3f);
    }

    private static void GrindStep(Vector3 pos, float dt)
    {
        if (Stalling) { StallStep(pos, dt); return; }
        bool ended;
        if (now - jumpWanted < 0.12f)
        {
            var nose = NoseYaw; var deck = DeckYaw;
            Pops++; GrindEnds++; jumpWanted = -99f; SkateGrind.Release(dt);
            Speed = SkateGrind.Speed; Yaw = SkateGrind.Heading;
            LimitSpeed(Cap, Cap);
            TakeOff(true, OlliePop * wantScale * PopHeight(wantKind) + AirGravity * dt);
            KeepAirPose(nose, deck);
            AirStep(pos, dt);
            return;
        }
        if (Held)
        {
            // Held back on the edge (a post, a wall at its end): the grind is over, and the board
            // comes down with the pace it actually had.
            GrindEnds++; SkateGrind.Release(dt);
            SkateGrind.Speed = Speed = (Speed < 0f ? -1f : 1f) * Math.Min(Math.Abs(Speed), Math.Abs(Actual) + HeldLeft);
            Yaw = SkateGrind.Heading; airNoseOffset = airDeckOffset = 0f;
            TakeOff(false, 0f);
            velocity = Dir(Yaw) * Speed;
            TrickTick(false, false, 0, 0, 0, 0, dt);
            return;
        }
        if (!SkateGrind.TryContinue(pos, dt, out ended) || ended)
        {
            GrindEnds++; SkateGrind.Release(dt);
            // Off the edge the board comes round to the way of travel, as it does in Skate.
            Speed = SkateGrind.Speed; Yaw = SkateGrind.Heading;
            LimitSpeed(Cap, Cap); airNoseOffset = airDeckOffset = 0f;
            TakeOff(false, 0f);
            velocity = Dir(Yaw) * Speed;
            TrickTick(false, false, 0, 0, 0, 0, dt);
            return;
        }
        Speed = SkateGrind.Speed; Yaw = SkateGrind.Heading;
        LimitSpeed(Cap, Cap);
        Tangent = Dir(SkateGrind.Heading);
        GrindVelocity(pos);
        // Along the edge the stick leans the board onto one truck: back for a 5-0, forward for a nosegrind.
        if (GrindAlong) GrindKind = SkateKeys.Manual ? SkateGrindKind.FiveO : SkateKeys.NoseManual ? SkateGrindKind.Nosegrind : SkateGrindKind.FiftyFifty;
        TrickTick(false, true, 0, 0, 0, 0, dt);
    }

    // How the board meets the edge: along it (both trucks), across it (a slide, by which part of the
    // board is on the edge), or at an angle (one truck on, the nose over or beside the edge). The
    // side is frontside when the edge is on the side the rider's chest faces.
    public static void ClassifyGrindForTest(Vector3 pos) { ClassifyGrind(pos); }
    private static void ClassifyGrind(Vector3 pos)
    {
        var rail = Dir(SkateGrind.Heading); var deck = Dir(DeckYaw);
        var rel = Delta(SkateGrind.Heading, DeckYaw); var across = Math.Abs(rel) > 90f ? 180f - Math.Abs(rel) : Math.Abs(rel);
        var to = SkateGrind.Target - pos; to.y = 0f;
        var chest = Dir(NoseYaw + 90f * StanceSign);
        GrindFrontside = Vector3.Dot(to, chest) > 0f;
        // Where the edge crosses the board's own axis, from its middle in metres; the nose is "far"
        // when it points away from the side the rider came from.
        var cross = deck.x * rail.z - deck.z * rail.x;
        grindCross = cross; var t = 0f;
        if (Math.Abs(cross) > 0.2f) t = (to.x * rail.z - to.z * rail.x) / cross;
        var near = -to.normalized; grindNoseFar = Vector3.Dot(deck, near) < 0f ? 1f : -1f;
        GrindAlong = across < 25f;
        // Along the edge the deck is squared to it; across or at an angle it keeps the angle it had.
        grindNose = Delta(SkateGrind.Heading, NoseYaw);
        GrindDeck = GrindAlong ? (Math.Abs(rel) > 90f ? 180f : 0f) : rel;
        if (GrindAlong) GrindKind = SkateGrindKind.FiftyFifty;
        else if (across > 65f) GrindKind = t > 0.25f ? SkateGrindKind.Noseslide : t < -0.25f ? SkateGrindKind.Tailslide : grindNoseFar > 0f ? SkateGrindKind.Boardslide : SkateGrindKind.Lipslide;
        else GrindKind = t > 0.1f ? SkateGrindKind.Crooked : grindNoseFar > 0f ? SkateGrindKind.Feeble : SkateGrindKind.Smith;
        Out.Say("GRIND kind " + SkateTricks.GrindName(GrindKind, GrindFrontside) + " (deck " + rel.ToString("F0") + " deg to the edge, crossing at " + t.ToString("F2") + " m)");
    }

    private static void GrindVelocity(Vector3 pos)
    {
        var to = SkateGrind.Target - pos;
        var side = to; side.y = 0f;
        var dir = Dir(SkateGrind.Heading);
        side = side - dir * Vector3.Dot(side, dir);
        var pull = side * 8f;
        if (pull.sqrMagnitude > 6.25f) pull = pull.normalized * 2.5f;
        velocity = dir * Speed + pull;
        velocity.y = Clamp(to.y * 10f, -3f, 3f);
    }
}

// Must be created after the game's walk component exists: its FixedUpdate and LateUpdate then run
// after the game's own.
public class LateDriver : MonoBehaviour
{
    public LateDriver(IntPtr p) : base(p) { }

    public static int Calls;

    private void FixedUpdate()
    {
        Calls++;
        var t0 = SkateClock.Now(); SkateRide.Step(); SkateClock.Add(SkateClock.Ride, t0);
    }

    private void LateUpdate()
    {
        SkateRig.LateFrame();
    }
}
