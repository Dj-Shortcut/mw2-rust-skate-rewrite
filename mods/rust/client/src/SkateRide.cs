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
    // RollDecel: what plain rolling loses, m/s each second. At 0.45 a board pushed to 20 km/h stood
    // still on a flat floor within seconds (owner, 15 October: the skate. games coast far longer);
    // 0.2 is about a real board on smooth concrete, 20 km/h rolling on for some 28 s.
    public const float PushAccel = 7f, PushSteady = 0.4f, PushKick = 2.7f, MaxPush = 11f, BrakeDecel = 12f, RollDecel = 0.2f, MaxSpeed = 25f, TurnRate = 150f, SlopeGain = 1.6f;
    // A powerslide: the brake with a turn held while rolling faster than PowerslideFrom swings the
    // board across the way of travel (PowerslideDegrees) and scrubs speed at PowerslideDecel; the
    // board keeps its line while it slides and swings back when the brake or the turn is let go.
    public const float PowerslideFrom = 2.5f, PowerslideDegrees = 85f, PowerslideDecel = 9f, PowerslideIn = 12f, PowerslideOut = 7f;
    public static float Powerslide, PowerslideSide;
    // Steering with the stick has weight, as in the skate. games: the turn builds over a moment as the
    // rider leans into it and eases out when let go, rather than the heading jumping with the stick;
    // and a board barely rolling turns only a little (a stopped board is turned by a kickturn, not a carve).
    public const float SteerIn = 7f, SteerOut = 10f, SteerStill = 0.35f, SteerFull = 2f;
    public static float SteerRate;
    private static bool strokeBack;
    public const float AirGravity = 16f, OlliePop = 6f, MaxFall = 30f, BailImpact = 12f, BailSeconds = 1.3f, BailDecel = 10f, PushPeriod = 0.8f, SeaLevel = -1.1f, LowestCap = 2f, LagStep = 0.1f, RaiseWait = 3f, CapForget = 30f, ManualPace = 1f, ManualHold = 0.3f, HopGrace = 0.3f;
    // Pumping: the shared SkateRidePhysics judges it (a crouch held down a transition, let go
    // through its bottom); PumpCharge only shapes the rider's crouch. Landing: the fall becomes
    // roll on a slope by LandRoll (the shared landing rule); a revert within RevertWindow of a
    // landed trick turns the board round at RevertRate and keeps the combo.
    public const float PumpCharge = 0.4f, LandRoll = 0.8f, RevertWindow = 0.6f, RevertRate = 600f;
    public const long RevertPoints = 50;
    public const float RevertStick = 0.85f, RevertSweep = 180f;
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
    public static int ResetsAll;
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
    // How near a half flip the board must be for the edge to take it upside down (the scorer's tolerance).
    public const double DarkCatch = 25.0;
    private static bool spinLate;
    // How much higher than the take-off the ground under the board must be for that.
    public const float DarkOver = 0.15f;
    private static float Over() { return Clearance > 50f ? -99f : Position.y - Clearance - takeOffY; }
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
    public static int Powerslides;
    public static bool PushMongo;
    // Which foot a push uses: the rider's back foot (the right one for a regular rider), or the
    // front one in a mongo push, whichever end of the board leads. Rolling tail first, after a
    // rollback, a revert or a 180 alike, the back foot is the one at the leading end, and it pushes
    // from there, as a skater pushing fakie does; the front foot pushing there would be a mongo push
    // the rider never asked for. PushLead: the foot that leads in the way of travel pushes.
    public static bool PushLead;
    // The deck's angle to the edge, taken when the grind begins and kept through it.
    public static float GrindDeck;
    private static float grindCross, grindNoseFar, grindNose, airNoseOffset, airDeckOffset;
    // Off an edge the board comes round to the way of travel over a moment, not in a jump: the pose
    // it had on the edge is kept as an air offset that runs out at ComeRoundRate.
    private const float ComeRoundRate = 540f;
    private static bool comingRound;
    private static Vector3 revertDir;
    private static float revertSign = 1f;
    private static float revertEndAt = -99f;
    // The revert's turn so far: the rider and the board pivot together on the ground, the way of
    // travel unchanged, and at the end the half turn is the stance's (the scorer's Switch) instead.
    private static float revertTurned, revertStep;
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
    public static float SlideYaw { get { return Mode == RideMode.Ground ? PowerslideDegrees * Powerslide * PowerslideSide : 0f; } }
    public static float NoseYaw { get { return Mode == RideMode.Grind ? SkateGrind.Heading + grindNose : Yaw + (Trick.Switch ? 180f : 0f) + SpinYaw + BodyYaw + SlideYaw + revertTurned + airNoseOffset; } }
    public static float DeckYaw { get { return Mode == RideMode.Grind ? SkateGrind.Heading + GrindDeck : NoseYaw - BodyYaw - (float)ShoveDeg * spinSide + (DeckReversed ? 180f : 0f) + airDeckOffset; } }
    private static float HorizontalFactor { get { return Mode == RideMode.Ground ? Flat(Tangent).magnitude : 1f; } }
    public static float HorizontalSpeed { get { return Speed * HorizontalFactor; } }
    private static Vector3 Flat(Vector3 v) { return new Vector3(v.x, 0f, v.z); }
    // The way along the ground that goes the heading's way. The shared frame (its ground is at most
    // 72 degrees, normal y over 0.3) keeps the heading as the horizontal part and gives it the rise
    // the normal asks for: the heading's part into the slope, up the slope, by 1/y. Up a transition
    // the normal tilts on to level at the lip (SteepSlope), where that rise would be unbounded and
    // the heading's part along the face would vanish with it, so a diagonal line up the wall became
    // a straight vertical launch. Past the shared limit the factor is held at its value there: the
    // part into the face goes up it, the part along the face (along the coping) stays, and the two
    // agree with the shared frame at 72 degrees.
    private const float SharedNormalLeast = 0.3f;
    public static Vector3 SurfaceTangent(Vector3 normal, Vector3 heading)
    {
        var level = new Vector3(normal.x, 0f, normal.z); var lean = level.magnitude;
        if (lean < 0.0001f) return heading;
        level /= lean;
        var into = -(heading.x * level.x + heading.z * level.z);
        var along = heading + level * into;
        var up = new Vector3(-level.x * normal.y, lean, -level.z * normal.y);
        var v = along + up * (into / Math.Max(normal.y, SharedNormalLeast));
        return v.sqrMagnitude > 0.000001f ? v.normalized : heading;
    }
    private static void LimitSpeed(float forward, float reverse)
    {
        var h = HorizontalFactor;
        // Straight up a lip there is no horizontal pace to cap.
        if (h <= 0.0001f) return;
        Speed = Clamp(Speed, -reverse / h, forward / h);
        if (Mode == RideMode.Grind) SkateGrind.Speed = Speed;
    }
    // Which end leads: the stance's half turns, and whether the board rolls against its heading. The
    // latter is read with a band (from -0.5 m/s back, to -0.1 m/s forward again), so a board rocking
    // about a standstill does not turn the rider's chest back and forth.
    public static bool TailFirst { get { return Trick.Switch != rollingBack; } }
    private static bool rollingBack;
    // What stands in the way, for the log: a ray just over the ground and one at the knee, ahead.
    // A lip stops only the low one; a wall both.
    private static string HeldAhead(Vector3 pos)
    {
        var ahead = Dir(Yaw) * (Speed < 0f ? -1f : 1f);
        var line = "";
        foreach (var h in new[] { 0.08f, 0.5f })
        {
            RaycastHit hit;
            if (SkateParks.Ray(pos + Vector3.up * h, ahead, out hit, 1.5f, GroundMask))
                line += " | " + h.ToString("F2") + " m up: hit at " + hit.distance.ToString("F2") + " m, normal y " + hit.normal.y.ToString("F2");
            else line += " | " + h.ToString("F2") + " m up: clear";
        }
        return line;
    }

    public static void SenseRollingBack() { if (Speed < -0.5f) rollingBack = true; else if (Speed > -0.1f) rollingBack = false; }
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
    // A tap never changes the stance: F2 (J is the game's clan key, which frees the cursor) or Y in
    // the controller's menu has to be held this long. A stance changed by a stray press was the rider pushing with the wrong foot.
    public const float StanceHoldSeconds = 1f;
    // Called each frame with whether the key is held; switches once per hold, after StanceHoldSeconds.
    public static void StanceHold(ref float since, bool held, float now)
    {
        if (!held) { since = -1f; return; }
        if (since < 0f) { since = now; SkatePieces.Said = "Stance: " + StanceName + " - hold to switch"; SkatePieces.SaidAt = now; return; }
        if (since > 0f && now - since >= StanceHoldSeconds) { ToggleStance(); since = 0f; }
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
        try
        {
            var cap = b.gameObject.GetComponent<CapsuleCollider>();
            CapsuleReach = cap != null ? Math.Max(0f, cap.radius - WallProbe) : 0.2f;
            CapsuleRadius = cap != null ? Math.Max(0f, Math.Min(1f, cap.radius)) : 0.5f;
            if (!capsuleSaid) { capsuleSaid = true; Out.Say("SKATE capsule " + (cap != null ? "radius " + cap.radius.ToString("F2") + " height " + cap.height.ToString("F2") : "not found") + "; walls are swept " + CapsuleReach.ToString("F2") + " m past the probe"); }
        }
        catch (Exception e) { CapsuleReach = 0.2f; if (!capsuleSaid) { capsuleSaid = true; Out.Say("SKATE capsule unreadable: " + e.GetType().Name + ": " + e.Message); } }
        var v = b.linearVelocity; velocity = v; var rise = v.y; v.y = 0f;
        Speed = Vector3.Dot(v, Dir(yaw));
        SkateKeys.EndStep();
        hasLast = false; pastCount = 0; Steps = 0; Resets = 0; Top = 0f; Lean = 0f; SteerRate = 0f; Powerslide = 0f; PowerslideSide = 1f; lastPull = mountAt = Time.realtimeSinceStartup;
        // A cap from a ride a while ago says nothing about this one.
        if (mountAt - putBackAt > CapForget) ForgetServer();
        VSpeed = 0f; AirTime = 0f; flipLeft = shoveLeft = pendShove = bodyLeft = 0f; pendFlip = 0; SpinDeg = FlipDeg = ShoveDeg = BodyDeg = 0; LateTrick = false; Jumped = TrickAir = Braking = Manual = NoseManual = spinning = DeckReversed = false; PushPhase = 0f; stuck = 0; heldSteps = 0; Held = false;
        Grab = SkateGrabKind.None; spinSide = 1f; wantKind = SkatePopKind.Ollie; wantFlip = 0; wantShove = 0f; wantScale = 1f;
        PumpHeld = RevertLeft = revertTurned = 0f; landedTrick = false; steppedAt = -99f; StepUps = 0; Pump = default(SkatePumpState); SlopeDegrees = 0f; ContactNormal = Vector3.up; OnRamp = false; GrindKind = SkateGrindKind.None; GrindFrontside = GrindAlong = false; GrindDeck = 0f; Stalling = false; Stalls = 0; PlantedUntil = -99f; PushMongo = PushLead = false; handDownAt = -99f; rollingBack = false;
        Trick = new SkateTrickState(false); TrickName = ""; ComboPoints = 0; TrickAt = BankedAt = LandAt = jumpWanted = -99f; OffReason = "";
        SkateHud.Clear();
        Normal = ViewNormal = Vector3.up; Position = b.position; Tangent = Dir(yaw);
        airNoseOffset = airDeckOffset = 0f; comingRound = false;
        SkateWatch.Reset();
        SkateGrind.Reset();
        // Getting on while going up (the second press of a double jump) finishes that jump on the
        // board; the ground is still within reach of the feet then, and ground movement would cut it.
        if (rise > 1f) TakeOff(false, rise);
        else
        {
            Enter(RideMode.Ground); Sense(Position); Tangent = SurfaceTangent(Normal, Dir(Yaw));
            Speed = Vector3.Dot(velocity, Tangent);
            LimitSpeed(Cap, Cap);
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
        if (m != RideMode.Ground && Mode == RideMode.Ground) { Pump = default(SkatePumpState); PumpHeld = 0f; RevertLeft = revertTurned = 0f; }
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
            if (Frozen) { hasLast = false; pastCount = 0; return; }
            var dt = Time.fixedDeltaTime;
            if (!(dt > 0.001f && dt <= 0.05f)) dt = 0.03125f;
            now = Time.realtimeSinceStartup;
            var pos = body.position; var game = body.linearVelocity;
            Position = pos;
            if (pos.y < SeaLevel && OpenSky(pos)) { Dismount("in the water"); return; }
            SenseRollingBack();
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
            if (Mode == RideMode.Ground || Mode == RideMode.Air)
            {
                velocity = WallAhead(pos, velocity, dt);
                flat = Flat(velocity); pace = flat.magnitude;
            }
            float boundedX, boundedZ;
            if (!SkateRidePhysics.TryBoundHorizontal(pos.x, pos.z, velocity.x, velocity.z, dt, Cap, out boundedX, out boundedZ))
            { Dismount("invalid movement speed budget"); return; }
            if (boundedX != velocity.x || boundedZ != velocity.z)
            {
                var previousPace = Math.Sqrt((double)velocity.x * velocity.x + (double)velocity.z * velocity.z);
                var scale = previousPace > 0 ? (float)(Math.Sqrt((double)boundedX * boundedX + (double)boundedZ * boundedZ) / previousPace) : 0f;
                if (Mode == RideMode.Ground) velocity.y += Tangent.y * Speed * (scale - 1f);
                velocity.x = boundedX; velocity.z = boundedZ;
                Speed *= scale; if (Mode == RideMode.Grind) SkateGrind.Speed = Speed;
                flat = Flat(velocity); pace = flat.magnitude;
            }
            if (pace > Top) Top = pace;
            body.linearVelocity = velocity;
            var sign = Speed < 0f ? -1f : 1f;
            lastPos = pos; lastDir = pace > 0.0001f ? flat / pace * sign : Dir(Yaw); lastSpeed = pace * sign; hasLast = true; Steps++;
            pastPos[pastNext] = pos; pastAt[pastNext] = now; pastNext = (pastNext + 1) % PastSteps; if (pastCount < PastSteps) pastCount++;
            var target = Mode == RideMode.Ground ? Normal : Vector3.up;
            ViewNormal = (ViewNormal + (target - ViewNormal) * 0.3f).normalized;
            SkateKeys.EndStep();
        }
        catch (Exception e) { Dismount("the step threw"); Out.Say("SKATE step threw " + e.GetType().Name + ": " + e.Message); }
    }

    // A wall the next step would reach. The physics engine moves the player's capsule one step at
    // a time without sweeping it, and a step at riding speed (0.8 m at 25 m/s) carries the capsule
    // into a thin wall, where it stays wedged: the game's collision then holds it wherever the
    // board points. An owner's ride into a thatch wall at 26 m/s was held on that spot for more
    // than 500 steps, with nothing ahead of it. So the step is swept first: what would go into a
    // steep face is taken out (a glancing hit slides along it), and a hard hit is a bail.
    public const float WallProbe = 0.3f, WallSkin = 0.1f, WallSlam = 8f, WallFrom = 3f;
    private static readonly float[] wallHeights = { 0.6f, 1.3f };
    public static int WallStops, WallSlams;
    // A transition's riding face is ground to roll up or come down onto, including contact while
    // rising through the air. Its undersides, side edges and coping stay walls. A sphere sweep's
    // normal points at the sphere, not out of the face (round on an edge
    // or the coping), so the face is read again with a ray to the contact. On the face itself the
    // two agree; on a transition's open side edge (the drawn surface has no side face) the ray
    // still finds the upward face, but the sweep's normal turns out sideways, and that stays a wall.
    // The face is ridden as far as the vertical lip, so a face is one up to level.
    private static bool RampFaceAhead(Vector3 from, RaycastHit hit)
    {
        if (Mode != RideMode.Ground && Mode != RideMode.Air) return false;
        if (!SkateParks.IsRamp(hit) || SkateParks.IsCoping(hit)) return false;
        var to = hit.m_Point - from; var reach = to.magnitude;
        RaycastHit face;
        if (reach < 0.001f || !SkateParks.Ray(from, to / reach, out face, reach + WallProbe, GroundMask) || face.collider != hit.collider) return false;
        return face.m_Normal.y > -0.05f && Vector3.Dot(face.m_Normal, hit.m_Normal) > 0.9f;
    }
    // The whole measured pace of the last step, along the path the body took (for the checks' logs).
    public static float PathSpeed;
    private static Vector3 WallAhead(Vector3 pos, Vector3 v, float dt)
    {
        var pace = v.magnitude;
        if (pace < WallFrom) return v;
        var dir = v / pace; var reach = pace * dt + CapsuleReach + WallSkin;
        var found = false; var nearest = float.MaxValue; var normal = Vector3.zero;
        for (var i = 0; i < wallHeights.Length; i++)
        {
            RaycastHit hit;
            var from = pos + Vector3.up * wallHeights[i];
            if (SkateParks.Sphere(from, WallProbe, dir, out hit, reach, GroundMask)
                && hit.m_Normal.y < 0.5f && hit.distance < nearest && !RampFaceAhead(from, hit)) { found = true; nearest = hit.distance; normal = hit.m_Normal; }
        }
        if (!found) return v;
        var into = -Vector3.Dot(v, normal);
        if (into <= 0f) return v;
        var kept = v + normal * into;
        WallStops++;
        if (into > WallSlam && Mode == RideMode.Ground)
        {
            WallSlams++;
            Out.Say("SKATE wall: hit at " + into.ToString("F1") + " m/s into it, " + nearest.ToString("F2") + " m ahead at " + Out.V3(pos) + "; bail");
            Bail(); Speed = 0f;
            return new Vector3(0f, Math.Min(0f, v.y), 0f);
        }
        // Along the ground the speed follows what is left of the step, so the next step does not
        // ride on into the wall.
        if (Mode == RideMode.Ground)
        {
            var left = Flat(kept).magnitude; var had = Flat(v).magnitude;
            if (had > 0.0001f) Speed *= left / had;
        }
        if (WallStops <= 20 || WallStops % 50 == 0) Out.Say("SKATE wall: " + into.ToString("F1") + " m/s into it taken out (" + WallStops + ") at " + Out.V3(pos));
        return kept;
    }
    // How far the player's capsule reaches past the probe's sphere, read from its collider.
    private static float CapsuleReach = 0.2f;
    // The capsule's radius, for its rest on a slope (Sense); a slope over 78 degrees counts as 78.
    private static float CapsuleRadius = 0.5f;
    private const float CapsuleSlackMost = 2f;
    private static bool capsuleSaid;

    // Where the body was over the last steps (#487). A server that puts a player back sets it on a
    // position it had before, so a put-back landing on one of these, a few steps old, is the
    // server's; one landing nowhere the body has been is the client's own physics.
    private const int PastSteps = 64;
    private static readonly Vector3[] pastPos = new Vector3[PastSteps];
    private static readonly float[] pastAt = new float[PastSteps];
    private static int pastNext, pastCount;

    private static string NearestPast(Vector3 pos)
    {
        if (pastCount == 0) return "no earlier steps";
        // Along the path between two kept steps, so the spacing of the steps (0.3 m at 9 m/s) does
        // not count as a miss.
        var best = float.MaxValue; var age = 0f;
        var newest = (pastNext - 1 + PastSteps) % PastSteps;
        for (var n = 0; n < pastCount; n++)
        {
            var i = (newest - n + PastSteps) % PastSteps;
            var a = pastPos[i]; var d = (a - pos).magnitude; var t = pastAt[i];
            if (n + 1 < pastCount)
            {
                var j = (i - 1 + PastSteps) % PastSteps; var ab = pastPos[j] - a; var len = ab.sqrMagnitude;
                if (len > 0.000001f)
                {
                    var f = Clamp(Vector3.Dot(pos - a, ab) / len, 0f, 1f);
                    d = (a + ab * f - pos).magnitude; t = pastAt[i] + (pastAt[j] - pastAt[i]) * f;
                }
            }
            if (d < best) { best = d; age = now - t; }
        }
        return "nearest earlier path " + best.ToString("F2") + " m away, " + age.ToString("F2") + " s old" + (best < 0.1f ? " (back where it was)" : " (nowhere it was)");
    }

    private static void Measure(Vector3 pos, float dt)
    {
        if (!hasLast) return;
        var moved = pos - lastPos;
        // The whole path, up a transition as much as along the flat: on a steep bank the
        // horizontal part of a step is small while the board rides the full step along the slope.
        var path = moved.magnitude / dt;
        PathSpeed = path;
        ActualV = moved.y / dt; moved.y = 0f;
        Actual = Vector3.Dot(moved, lastDir) / dt;
        // Through a revert the board's heading and the way of travel part: no pull-back can be read.
        if (RevertLeft != 0f || now - revertEndAt < 0.15f) return;
        if ((lastSpeed > 1f && Actual < -3f) || (lastSpeed < -1f && Actual > 3f))
        {
            // The server put the player back. That is logged and nothing more: the board never
            // slows itself down for the server. A server that refuses skate speed has to allow it
            // (the ShortcutSkate plugin, or its own speed checks turned off by the admin).
            Resets++; ResetsAll++;
            Out.Say("SKATE put back #" + Resets + ", " + (now - mountAt).ToString("F2") + " s after getting on: moved " + Out.V3(pos - lastPos) + " in a step at speed " + lastSpeed.ToString("F1") + "; " + NearestPast(pos));
            return;
        }
        if (now - steppedAt < StepGrace) { heldSteps = 0; return; }
        // Held back by anything on the ground or along an edge, a wall, a post, a fence: the board
        // did not get as far as it rode for a few steps in a row. Its speed then comes down to what
        // it actually does, so the meter and the tricks do not run on against the wall; a little is
        // left so that a push away from it starts again. A single short step is not that: a lip
        // stepped over, a slope, a frame of the server's. Measured along the whole path, so that a
        // steep transition, where little of a step is horizontal, is not taken for a wall.
        var rode = Math.Abs(Speed); var got = path;
        if ((Mode == RideMode.Ground || Mode == RideMode.Grind) && rode > 1f && got < rode * HeldShare) heldSteps++; else heldSteps = 0;
        Held = heldSteps >= HeldSteps;
        if (Held)
        {
            if (heldSteps == HeldSteps) Out.Say("SKATE held back: rode " + rode.ToString("F1") + " m/s, got " + got.ToString("F1") + (Mode == RideMode.Grind ? " on the grind" : "") + " at " + Out.V3(pos) + HeldAhead(pos));
            if (Mode != RideMode.Grind) { var sign = Speed < 0f ? -1f : 1f; Speed = sign * Math.Min(rode, got + HeldLeft); }
            return;
        }
        // A wall right in front stops the board at once: it did not get as far as it rode and
        // something steep stands right in front of it.
        if (rode < 0.5f || got > rode * 0.5f) return;
        RaycastHit wall;
        var ahead = new Vector3(lastDir.x, 0f, lastDir.z) * Math.Sign(Speed);
        var from = pos + Vector3.up * 0.3f;
        if (ahead.sqrMagnitude < 0.0001f || !SkateParks.Ray(from, ahead.normalized, out wall, 0.6f, GroundMask) || wall.m_Normal.y > 0.5f || RampFaceAhead(from, wall)) return;
        if (Speed > 0f) LimitSpeed(Math.Max(0f, Actual) + 2f, Cap);
        else LimitSpeed(Cap, Math.Max(0f, -Actual) + 2f);
    }

    // Land a metre below the water's surface is for swimming, unless something is overhead (tunnels,
    // basements); the shallows along the shore are rideable.
    private static bool OpenSky(Vector3 pos)
    {
        RaycastHit hit;
        return !SkateParks.Ray(pos + Vector3.up * 2f, Vector3.up, out hit, 60f, GroundMask);
    }

    private static void Sense(Vector3 pos)
    {
        RaycastHit hit;
        // Keep the last verified face's normal probe in the air too: a rising capsule can
        // meet the curve again where the straight-down probe no longer reaches it.
        if ((Mode == RideMode.Ground || Mode == RideMode.Air) && OnRamp && RampContact(pos, out hit)) return;
        var reach = Mode == RideMode.Air ? AirProbe : FeetProbe;
        var got = SkateParks.Ray(pos + Vector3.up * RayLift, Vector3.down, out hit, RayLift + reach, GroundMask);
        // The player's capsule ends in a half sphere: on a slope it rests on its side of that sphere, so
        // its lowest point stands radius * (1/cos - 1) over the ground straight below (0.19 m at 43
        // degrees, 0.5 m at 60, measured on a MEGA transition). That is contact, not a gap: read as a
        // gap, the gap closer pressed the body into every transition and the ground was lost past 55
        // degrees.
        var slack = got && hit.m_Normal.y > 0.2f && hit.m_Normal.y < 0.999f ? Math.Min(CapsuleSlackMost, CapsuleRadius * (1f / hit.m_Normal.y - 1f)) : 0f;
        Clearance = got ? hit.m_Distance - RayLift - slack : 99f;
        OnRamp = got && SkateParks.IsRamp(hit) && !SkateParks.IsCoping(hit);
        // Over a transition's face (coming down onto it from the air, or the first step onto it)
        // the gap is the half sphere's distance to the face along the face's normal, exact rather
        // than the rest's estimate, and the face keeps its normal however steep: a fall back onto
        // the wall is landed on the wall (Land), not on level ground.
        if (OnRamp && hit.m_Normal.y > 0.001f) Clearance = Vector3.Dot(pos + Vector3.up * CapsuleRadius - hit.m_Point, hit.m_Normal) - CapsuleRadius;
        Grounded = got && Clearance <= FeetProbe;
        Normal = Grounded && (hit.m_Normal.y > SharedNormalLeast || (OnRamp && hit.m_Normal.y > 0.001f)) ? hit.m_Normal : Vector3.up;
        BelowNormal = got && hit.m_Normal.y > 0.001f ? hit.m_Normal : Vector3.up;
        if (got && hit.m_Normal.y > SharedNormalLeast) ContactNormal = hit.m_Normal;
    }
    // The normal of what Sense last found under the body, grounded or not (the air step's landing
    // reads the gap to a transition's face along it).
    public static Vector3 BelowNormal = Vector3.up;

    // On a transition's drawn surface (Sense): its faces are ground, not walls (WallAhead), and the
    // contact is read along the face's normal (RampContact).
    public static bool OnRamp;
    // Rides off the lip of a transition (the log's count): the board leaves the face with the whole
    // pace it had along it, up as much as the face stood up, and comes back down onto it.
    public static int Verts;
    public const float RampReach = 0.3f;

    // On a transition the body rests against the face, not on the ground straight below it: the
    // capsule's bottom half sphere touches the face along the face's normal. Read straight down,
    // the ground under the body is lost from about 53 degrees on (the gap below grows past the
    // probe, the capsule's rest on the slope counted in), and the ride flapped between ground and
    // air up every transition (#487). So on a face the ride is on, the contact is read along the
    // last normal from the half sphere's centre: the distance to the face less the radius is the
    // gap. The ground is kept as far as the lip, where the face stands vertical; a face that leans
    // over is none, and so is anything but the transition's own collider. The ride's frame on a
    // face past the shared physics' 72 degrees is the client's own (SteepSlope).
    private static bool RampContact(Vector3 pos, out RaycastHit hit)
    {
        hit = default(RaycastHit);
        var along = -Normal;
        if (along.sqrMagnitude < 0.5f) return false;
        along = along.normalized;
        if (!SkateParks.Ray(pos + Vector3.up * CapsuleRadius, along, out hit, CapsuleRadius + FeetProbe + RampReach, GroundMask)) return false;
        // The coping on the lip is not the face: met by this ray, the face has ended there.
        if (!SkateParks.IsRamp(hit) || SkateParks.IsCoping(hit) || hit.m_Normal.y < -0.05f) return false;
        Clearance = hit.m_Distance - CapsuleRadius;
        Grounded = Clearance <= FeetProbe;
        OnRamp = true;
        Normal = hit.m_Normal.y > 0.001f ? hit.m_Normal : new Vector3(hit.m_Normal.x, 0.001f, hit.m_Normal.z).normalized;
        BelowNormal = Normal;
        if (hit.m_Normal.y > SharedNormalLeast) ContactNormal = hit.m_Normal;
        return true;
    }

    // Past the shared physics' limit (its frame takes no ground over 72 degrees) the same rule is
    // kept by the client up to the lip: gravity along the tangent, the slope signed by the way of travel.
    private static void SteepSlope(Vector3 tangent, float dt)
    {
        Speed += -AirGravity * tangent.y * dt;
        var horizontal = Flat(tangent).magnitude;
        SlopeDegrees = Speed == 0f ? 0f : (float)(Math.Atan2(tangent.y, horizontal) * 180.0 / Math.PI) * Math.Sign(Speed);
    }

    private static void GroundStep(float dt)
    {
        if (!Grounded)
        {
            // Off the lip of a transition (or any edge): the pace along the ground goes on as it
            // was, and a pop asked for in the last moment on it is taken here, as it would have
            // been a step earlier on the ground (TakeOff forgets the request otherwise). The air
            // brings the board back down onto the face, rolling back fakie, or onto the deck when
            // the way led over it.
            var popped = now - jumpWanted < 0.12f;
            var pop = 0f;
            if (popped)
            {
                Pops++; jumpWanted = -99f;
                if (SkateTricks.IsPlant(wantKind)) PlantedUntil = now + PlantSeconds;
                pop = OlliePop * wantScale * PopHeight(wantKind);
            }
            var rise = Tangent.y * Speed;
            if (rise > 0.5f && Math.Abs(SlopeDegrees) >= 45f)
            {
                Verts++;
                Out.Say("SKATE vert: off the lip at " + Math.Abs(SlopeDegrees).ToString("F0") + " deg, " + rise.ToString("F1") + " m/s up, " + Flat(Tangent * Speed).magnitude.ToString("F1") + " along" + (popped ? ", popped" : "") + ", at " + Out.V3(Position));
            }
            Launch(Tangent, pop, dt); Sense(Position); AirStep(Position, dt); return;
        }
        var before = Yaw;
        var maxTurn = TurnRate / (1f + Math.Abs(Speed) / 8f) * dt;
        // The brake with a turn held at speed is a powerslide: the board swings across the line and
        // scrubs speed; let go, or slowed to a walk, it swings back. The side is the side the turn
        // was held to, read as the slide starts.
        var turnHeld = SkateKeys.Analog ? Math.Abs(SkateKeys.Steer) > 0.5f : (SkateKeys.Left || SkateKeys.Right);
        var wantSlide = SkateKeys.Brake && turnHeld && RevertLeft == 0f && (Powerslide > 0.01f ? Math.Abs(Speed) > 0.8f : Math.Abs(Speed) > PowerslideFrom);
        if (wantSlide && Powerslide < 0.01f)
        {
            var toRight = SkateKeys.Analog ? SkateKeys.Steer > 0f : SkateKeys.Right;
            PowerslideSide = (toRight ? 1f : -1f) * (Speed < 0f ? -1f : 1f);
            Powerslides++;
        }
        Powerslide += ((wantSlide ? 1f : 0f) - Powerslide) * Clamp(dt * (wantSlide ? PowerslideIn : PowerslideOut), 0f, 1f);
        if (Powerslide < 0.01f && !wantSlide) Powerslide = 0f;
        // A stick steers by how far it is pushed; keys and the mouse turn the board toward where the rider looks.
        if (RevertLeft != 0f || Powerslide > 0.3f) SteerRate = 0f;
        else if (SkateKeys.Analog)
        {
            var rolling = Clamp(SteerStill + (1f - SteerStill) * Math.Abs(Speed) / SteerFull, SteerStill, 1f);
            var want = Clamp(SkateKeys.Steer, -1f, 1f) * TurnRate / (1f + Math.Abs(Speed) / 8f) * rolling;
            var easing = Math.Abs(want) > Math.Abs(SteerRate) && want * SteerRate >= 0f ? SteerIn : SteerOut;
            SteerRate += (want - SteerRate) * (1f - (float)Math.Exp(-easing * dt));
            Yaw += SteerRate * dt;
        }
        else Yaw += Clamp(Delta(Yaw, SkateKeys.LookYaw + ((SkateKeys.Left ? -1f : 0f) + (SkateKeys.Right ? 1f : 0f)) * 40f), -maxTurn, maxTurn);
        if (now - jumpWanted >= 0.12f) Revert(dt);
        var dir = RevertLeft != 0f ? revertDir * revertSign : Dir(Yaw);
        var tangent = SurfaceTangent(Normal, dir);
        // The shared physics: gravity along the tangent of the ground, the slope signed by the way of
        // travel. Up a transition past its limit (a face over 72 degrees, only ever a ramp's), the
        // client keeps the same rule to the lip.
        SkateSlopeResult slope; string error;
        if (Normal.y <= SharedNormalLeast) SteepSlope(tangent, dt);
        else if (SkateRidePhysics.TrySlope(new SkateSlopeInput(V(Normal), V(dir), Speed, AirGravity), dt, out slope, out error))
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
        // Part of a push's speed comes as soon as it is asked for; the rest, the kick, comes while the
        // foot is on the ground driving the board on (below), so the surge is seen with the stroke.
        if (pushing && fakiePush) Speed = Math.Max(-pushLimit, Speed - PushAccel * PushSteady * dt);
        else if (pushing) Speed = Math.Min(pushLimit, Speed + PushAccel * PushSteady * dt);
        if (Powerslide > 0.01f) Speed = Toward(Speed, 0f, (BrakeDecel * (1f - Powerslide) + PowerslideDecel * Powerslide) * dt);
        else if (SkateKeys.Brake) Speed = Toward(Speed, 0f, BrakeDecel * dt);
        Speed = Toward(Speed, 0f, RollDecel * dt);
        // Rolling back down a transition keeps its pace both ways (#409): only friction, the brake,
        // a powerslide and the server's horizontal cap slow it, never which end leads.
        LimitSpeed(Cap, Cap);
        if (pushing || PushPhase > 0f)
        {
            if (PushPhase <= 0f) { Pushes++; PushMongo = SkateKeys.Mongo; PushLead = PushMongo != TailFirst; strokeBack = fakiePush; }
            // While the foot is on the ground it stays where it came down and the board rolls on
            // past it: the stroke runs by the distance the board moves, not by the clock (a board
            // that cannot move gets the slow clock), so the foot never slides along the ground.
            var stroke = (float)(SkateRider.PushPlant + SkateRider.PushSweep);
            var span = (float)(SkateRider.PushUp - SkateRider.PushDown);
            var down = PushPhase > (float)SkateRider.PushDown && PushPhase < (float)SkateRider.PushUp;
            // The kick: what the stroke covers of the foot's time on the ground this step drives the
            // board on; the stroke then runs by the board's new pace, so the foot stays put.
            var from = PushPhase;
            var guess = from + (down ? Math.Max(dt / (PushPeriod * 3f), Math.Abs(Speed) * dt / stroke * span) : dt / PushPeriod);
            var driven = Math.Min(guess, (float)SkateRider.PushUp) - Math.Max(from, (float)SkateRider.PushDown);
            if (driven > 0f && SkateKeys.Push && !SkateKeys.Brake && !Manual && !NoseManual)
            {
                var gain = PushKick * driven / span;
                if (strokeBack) { if (Speed > -pushLimit) Speed = Math.Max(-pushLimit, Speed - gain); }
                else if (Speed < pushLimit) Speed = Math.Min(pushLimit, Speed + gain);
            }
            PushPhase += down ? Math.Max(dt / (PushPeriod * 3f), Math.Abs(Speed) * dt / stroke * span) : dt / PushPeriod;
            if (PushPhase >= 1f) PushPhase = 0f;
        }
        // The velocity runs along the ground's tangent; a gap under the wheels is closed on top of it:
        // down, and on a transition's face toward the face, which is where the gap was read.
        velocity = tangent * Speed;
        var close = Clamp(Clearance * 8f, 0f, 3f);
        if (OnRamp) velocity -= Normal * close; else velocity.y -= close;
        StepOver(dir, dt);
        // In a slide the rider leans back from the way he goes, into the turn that started it.
        var leanWant = Powerslide > 0.01f ? -PowerslideSide * 18f * Powerslide : Clamp((Delta(before, Yaw) + revertStep) / dt * 0.12f, -25f, 25f);
        revertStep = 0f;
        Lean += (leanWant - Lean) * (1f - (float)Math.Exp(-12f * dt));
        TrickTick(false, false, 0, 0, 0, 0, dt);
    }

    // The ground a little ahead, in the way of travel: higher by a lip's worth, the board is lifted
    // over it within the step, which the game's own fixed step then carries.
    private static void StepOver(Vector3 dir, float dt)
    {
        if (Math.Abs(Speed) < 0.5f) return;
        var ahead = Position + dir * (Math.Sign(Speed) * StepLook);
        RaycastHit hit;
        if (!SkateParks.Ray(ahead + Vector3.up * (StepUp + 0.3f), Vector3.down, out hit, StepUp + 0.3f + FeetProbe, GroundMask)) return;
        var rise = hit.m_Point.y - Position.y;
        if (rise < 0.03f || rise > StepUp || hit.m_Normal.y < 0.7f) return;
        // A lip has a face: a ray along the ground from the wheels' height must meet something steep
        // before the spot ahead. A smooth bank rises as much but has no face, and is ridden up.
        RaycastHit face;
        var flat = new Vector3(dir.x, 0f, dir.z); if (flat.sqrMagnitude < 0.0001f) return; flat = flat.normalized;
        if (!SkateParks.Ray(Position + Vector3.up * 0.02f, flat * Math.Sign(Speed), out face, StepLook + 0.05f, GroundMask) || face.m_Normal.y > 0.5f) return;
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
            // With the controller a revert is the left stick swept half round its rim, as in the
            // skate. games: a hard steer after a landing is a carve (the owner's ride had a dozen
            // reverts he never asked for). On the keys the turn must be asked for afresh.
            if (!landedTrick || now - LandAt > RevertWindow || Math.Abs(Speed) < 1f) return;
            float way;
            if (SkateKeys.Analog) { if (Math.Abs(SkatePad.LeftSweep) < RevertSweep) return; way = Math.Sign(SkatePad.LeftSweep); SkatePad.LeftSweep = 0f; }
            else { if (!SkateKeys.SpinPressed || SkateKeys.Spin == 0f || spinAtLand != 0f) return; way = SkateKeys.Spin > 0f ? 1f : -1f; }
            landedTrick = false; RevertLeft = 180f * way; Reverts++; revertSign = Math.Sign(Speed); revertDir = Dir(Yaw) * revertSign;
            Out.Say("SKATE revert begins");
        }
        var step = Math.Min(Math.Abs(RevertLeft), RevertRate * dt) * Math.Sign(RevertLeft);
        revertTurned += step; RevertLeft -= step; revertStep = step;
        if (Math.Abs(RevertLeft) > 0.001f) return;
        // Round: the rider and the board point the other way and roll on as before, the other end
        // leading. The half turn goes to the stance from here on, so the pose does not move at the
        // handover: the turn made so far comes off as the scorer's Switch comes on. The heading and
        // the speed stay as they were: the way of travel never changed.
        RevertLeft = 0f; revertEndAt = now;
        SkateTrickResult r; string error;
        if (SkateTricks.TryAdd(Trick, "Revert", RevertPoints, true, out r, out error)) { Take(r); revertTurned = 0f; }
        else { Out.Say("SKATE revert refused: " + error); Yaw += revertTurned; Speed = -Speed; revertTurned = 0f; }
    }

    // The crouch is held with a trigger; the shared pump judges the pass: charged going down a
    // transition, awarded when let go through its bottom, once per pass, by an energy budget.
    private static void PumpStep(float dt)
    {
        PumpHeld = SkateKeys.Crouch ? Math.Min(PumpCharge, PumpHeld + dt) : 0f;
        var travel = Flat(Tangent); if (travel.sqrMagnitude < 0.0001f) travel = Dir(Yaw);
        travel = travel * (Speed >= 0f ? 1f : -1f);
        SkatePumpResult r; string error;
        // The shared pump takes a slope short of vertical; at the lip the face is as good as that.
        var input = new SkatePumpInput(Mode == RideMode.Ground, SkateKeys.Crouch, Clamp(SlopeDegrees, -89.9f, 89.9f), Math.Abs(Speed), new SkateVector(travel.x, 0, travel.z));
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

    // The shared edge finder takes speeds up to the shared ground limit: a faster board is taken
    // onto the edge at that speed.
    private static Vector3 EdgeVelocity(Vector3 v)
    {
        var flat = Flat(v); var most = (float)SkateMotion.MaximumGroundSpeed - 0.01f;
        if (flat.magnitude <= most) return v;
        flat = flat.normalized * most; return new Vector3(flat.x, v.y, flat.z);
    }

    private static void KeepAirPose(float nose, float deck)
    {
        airNoseOffset = Delta(Yaw + (Trick.Switch ? 180f : 0f), nose);
        airDeckOffset = Delta(nose + (DeckReversed ? 180f : 0f), deck);
    }

    // Off an edge (or out of a handplant, turned round): the pose is kept, and the air step
    // brings the board round to the heading at ComeRoundRate.
    private static void ComeRound(float nose, float deck)
    {
        KeepAirPose(nose, deck);
        comingRound = Math.Abs(airNoseOffset) > 0.5f || Math.Abs(airDeckOffset) > 0.5f;
        if (!comingRound) airNoseOffset = airDeckOffset = 0f;
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
        flipLeft = shoveLeft = bodyLeft = 0f; spinning = spinLate = false; PushPhase = 0f; Braking = false; stuck = 0; heldSteps = 0; Held = false; LateTrick = false;
        popKind = jumped ? wantKind : SkatePopKind.Ollie; pendFlip = jumped ? wantFlip : 0; pendShove = jumped ? wantShove : 0f;
        wantFlip = 0; wantShove = 0f; wantKind = SkatePopKind.Ollie;
    }

    private static float TimeToGround()
    {
        var h = Clearance;
        // Over a transition's face the gap is read along its normal: the fall to it is longer.
        if (OnRamp && h > 0f) h /= Math.Max(BelowNormal.y, 0.2f);
        h = h > AirProbe ? AirProbe : h < 0f ? 0f : h;
        return (VSpeed + (float)Math.Sqrt(VSpeed * VSpeed + 2f * AirGravity * h)) / AirGravity;
    }

    private static void AirStep(Vector3 pos, float dt)
    {
        AirTime += dt;
        if (comingRound)
        {
            airNoseOffset = Toward(airNoseOffset, 0f, ComeRoundRate * dt); airDeckOffset = Toward(airDeckOffset, 0f, ComeRoundRate * dt);
            comingRound = airNoseOffset != 0f || airDeckOffset != 0f;
        }
        if (AirTime > HopGrace) Manual = NoseManual = false;
        VSpeed -= AirGravity * dt; if (VSpeed < -MaxFall) VSpeed = -MaxFall;
        if (pos.y - takeOffY > AirPeak) AirPeak = pos.y - takeOffY;
        var dir = Dir(Yaw);
        Tangent = dir; SteerRate = 0f; Lean += (0f - Lean) * (1f - (float)Math.Exp(-12f * dt));

        // A rotation is only started when the arc lasts long enough to finish it: an unfinished one is a bail.
        var left = (float)(TimeToGround() - 0.03);
        var flips = pendFlip + SkateKeys.LateFlip; var lateFlips = SkateKeys.LateFlip != 0 && Jumped && AirTime > LateAfter; pendFlip = 0;
        // As in Skate, a flip and a grab never overlap: a flip or shove-it asked for during a grab,
        // or while the grab button is already held (also from before the pop), is not done; a grab
        // pressed during a flip or shove-it starts once the board has come round.
        var grabbing = Grab != SkateGrabKind.None || SkateKeys.Grab != SkateGrabKind.None;
        if (grabbing && Math.Abs(shoveLeft) < 0.001f) pendShove = 0f;
        if (flips != 0 && !grabbing && (Math.Abs(flipLeft) < 0.001f || Math.Sign(flipLeft) == Math.Sign(flips)))
        {
            // As in Skate: a flip is started whenever it is asked for. One asked too late does not
            // come round before the ground, and that is a bail, unless the board meets an edge half
            // way round (a darkslide).
            if (left < (Math.Abs(flipLeft) + 360f * Math.Abs(flips)) / (float)SkateMotion.FlipRate) Late("a flip", left);
            flipLeft += 360f * flips; TrickAir = true; if (lateFlips) LateTrick = true;
        }
        else if (flips != 0 && grabbing) SkateHud.Notice("No flip during a grab");
        // A shove asked for in the air goes on top of what is under way, the same way round.
        var lateShove = Jumped ? SkateKeys.LateShove : 0f;
        if (lateShove != 0f && !grabbing && (Math.Abs(shoveLeft) < 0.001f || Math.Sign(shoveLeft) == Math.Sign(lateShove))) pendShove += lateShove;
        if (pendShove != 0f)
        {
            if (left < (Math.Abs(shoveLeft) + Math.Abs(pendShove)) / (float)SkateMotion.ShoveRate) Late("a shove-it", left);
            shoveLeft += pendShove; TrickAir = true; if (lateShove != 0f && AirTime > LateAfter) LateTrick = true;
            pendShove = 0f;
        }
        // A body varial: the rider turns a half round, the board keeps its yaw. Only when the arc
        // lasts long enough for it; one per flight.
        var body = Jumped ? SkateKeys.BodyVarial : 0f;
        if (body != 0f && Math.Abs(bodyLeft) < 0.001f && Math.Abs(BodyDeg) < 0.001)
        {
            if (left < Math.Abs(body) / (float)SkateMotion.RotationRate) Late("a body varial", left);
            bodyLeft = body; TrickAir = true;
        }
        var spinAxis = SpinAxis(left, dt);
        Grab = TrickAir && Math.Abs(flipLeft) < 0.001f && Math.Abs(shoveLeft) < 0.001f ? SkateKeys.Grab : SkateGrabKind.None;

        var flipStep = (float)SkateMotion.FlipRate * dt; var shoveStep = (float)SkateMotion.ShoveRate * dt; var bodyStep = (float)SkateMotion.RotationRate * dt;
        var flipAxis = Clamp(flipLeft / flipStep, -1f, 1f); var shoveAxis = Clamp(shoveLeft / shoveStep, -1f, 1f); var bodyAxis = Clamp(bodyLeft / bodyStep, -1f, 1f);

        stuck = VSpeed < -2f && Math.Abs(ActualV) < 0.3f && AirTime > 0.2f ? stuck + 1 : 0;
        // Touching a curve is decided along its normal, not by world-up: the rider can
        // still be rising when the face catches up. A pop separating from it stays airborne.
        var incoming = dir * Speed + Vector3.up * VSpeed;
        var contactNormal = OnRamp ? BelowNormal : Vector3.up;
        var separation = Vector3.Dot(incoming, contactNormal);
        var closing = Math.Max(0f, -separation);
        if ((separation <= 0f && Clearance <= Math.Max(0.04f, closing * dt + 0.02f)) || stuck >= 3)
        {
            var recovery = stuck >= 3;
            var impact = recovery ? 0f : closing;
            Land(incoming, recovery, dt);
            if (Mode == RideMode.Ground)
            {
                velocity = hasLandRoll ? landRoll : dir * Speed;
                if (!hasLandRoll) velocity.y = 0f;
                velocity -= contactNormal * Clamp(Clearance / dt, 0f, impact);
            }
            else BailStep(incoming, dt);
            return;
        }
        velocity = dir * Speed + Vector3.up * VSpeed;
        // A board half way through a flip, upside down, may come down onto an edge: a darkslide.
        var halfFlipped = !spinning && Math.Abs(shoveLeft) < 0.001f && Math.Abs(bodyLeft) < 0.001f && Math.Abs(flipLeft) > 0.001f
            && Math.Abs(Math.Abs(FlipDeg) - 180.0) <= DarkCatch;
        if (VSpeed < 0f && Jumped && (!Rotating || halfFlipped) && SkateGrind.TryCapture(pos, EdgeVelocity(velocity), NoseYaw, dt))
        {
            LastAir = AirLine(halfFlipped ? "darkslide" : "grind");
            var impact = -VSpeed;
            ClassifyGrind(pos);
            if (halfFlipped)
            {
                // The rest of the flip is not done: the board stays over, the griptape on the edge.
                GrindKind = SkateGrindKind.Darkslide; flipLeft = 0f; SkateBoard.GrindRoll = 180f;
                Out.Say("GRIND kind " + SkateTricks.GrindName(GrindKind, GrindFrontside) + " (caught half way through the flip)");
            }
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
            if (ahead <= (left - dt) * rate + SpinSlack || spinLate) { TrickAir = spinning = true; SpinLead = want * SpinLeadMost; return want; }
            // A turn asked for afresh too late is started all the same, and turns for as long as it
            // is held: not round before the ground, it is a bail.
            // Only in a trick: a fall that was not popped (rolling off an edge) is steered, not spun.
            if (SkateKeys.SpinPressed && TrickAir) { Late(Math.Abs(at) > 45f ? "the next half spin" : "a spin", left); spinLate = true; TrickAir = spinning = true; SpinLead = want * SpinLeadMost; return want; }
        }
        else spinLate = false;
        if (!TrickAir) { SpinLead = 0f; return 0f; }
        var rest = (float)Math.Round(at / 180.0, MidpointRounding.AwayFromZero) * 180f - at;
        spinning = Math.Abs(rest) > 1f;
        SpinLead = Clamp(rest / 90f, -1f, 1f) * SpinLeadMost;
        return Clamp(rest / step, -1f, 1f);
    }

    // A press in the air that the arc cannot finish: started all the same (it will be a bail), and
    // logged for the ride summary, and said on screen for a moment.
    private static void Late(string what, float left)
    {
        Refused++; SkateHud.Notice("Too late for " + what);
        Out.Say("SKATE late " + what + ": " + (left < 0f ? 0f : left).ToString("F2") + " s of air left, " + AirTime.ToString("F2") + " s in");
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
        var normal = OnRamp ? BelowNormal : Grounded ? Normal : ContactNormal;
        if (recovery) incoming.y = 0f;
        // Back down onto a transition's face past the shared landing's 72 degrees (only ever a
        // ramp's, from Sense): the fall's part into the face is the impact, the rest rolls along it.
        var steep = normal.y <= SharedNormalLeast;
        var impact = 0f; var r = default(SkateLandingResult); string error = null;
        var valid = !recovery && (steep || SkateRidePhysics.TryLand(new SkateLandingInput(V(incoming), V(normal), V(Dir(Yaw)), BailImpact, LandRoll), out r, out error));
        if (valid && steep) impact = Math.Max(0f, -Vector3.Dot(incoming, normal));
        else if (valid) impact = r.Bailed ? Math.Max((float)r.NormalImpact, BailImpact + 0.01f) : (float)r.NormalImpact;
        else if (!recovery) { Physics_(error); Bail(); return; }
        if (AirTime > 0.12f || impact > 1.5f) { Lands++; LandAt = now; LandImpact = impact; }
        landedTrick = TrickAir; spinAtLand = SkateKeys.Spin;
        // A revert is a sweep made after the wheels are down: one from the air (a body varial's
        // circle, with the stick still on the rim) must not turn the board round on landing.
        SkatePad.LeftSweep = 0f;
        // As in Skate: a grab still held as the wheels touch down is a bail.
        var heldGrab = TrickAir && !recovery && (Grab != SkateGrabKind.None || SkateKeys.Grab != SkateGrabKind.None);
        Enter(RideMode.Ground);
        if (heldGrab) { Out.Say("SKATE bail: the grab was still held at the landing"); Bail(); return; }
        if (TrickAir) TrickTick(false, false, 0, 0, 0, impact, dt);
        else if (impact > BailImpact) Bail();
        else TrickTick(false, false, 0, 0, 0, 0, dt);
        if (Mode == RideMode.Ground)
        {
            Yaw += airNoseOffset + airDeckOffset; airNoseOffset = airDeckOffset = 0f; comingRound = false;
            SpinDeg = FlipDeg = ShoveDeg = 0;
            var axis = Dir(Yaw + (Trick.Switch ? 180f : 0f) + (DeckReversed ? 180f : 0f));
            Vector3 roll;
            if (steep) roll = incoming - normal * Vector3.Dot(incoming, normal);
            else if (SkateRidePhysics.TryLand(new SkateLandingInput(V(incoming), V(normal), V(axis), BailImpact, recovery ? 0f : LandRoll), out r, out error)) roll = V(r.RollVelocity);
            else { Physics_(error); Speed = Flat(incoming).magnitude * (Speed < 0f ? -1f : 1f); Bail(); return; }
            Tangent = SurfaceTangent(normal, Dir(Yaw)); Normal = normal;
            Speed = Vector3.Dot(roll, Tangent);
            LimitSpeed(Cap, Cap);
            landRoll = Tangent * Speed; hasLandRoll = true;
        }
        flipLeft = shoveLeft = 0f; spinning = false; TrickAir = Jumped = false; Grab = SkateGrabKind.None; AirTime = 0f;
    }

    private static void TrickTick(bool airborne, bool grinding, float spinAxis, float flipAxis, float shoveAxis, float impact, float dt, float bodyAxis = 0f)
    {
        SkateTrickResult r; string error;
        var rolling = !airborne && !grinding;
        // Which end of the deck leads, physically: the scorer latches it at the first airborne step
        // and names the flight Fakie (switch, the tail leading) or Switch (switch, the nose leading).
        // The pop end stays the rider's: an ollie pops the end under the back foot, whichever end
        // of the deck that is after a shove.
        var input = new SkateTrickInput(spinAxis, flipAxis, shoveAxis, airborne ? Grab : SkateGrabKind.None, airborne, grinding, impact, Manual && rolling, NoseManual && !Manual && rolling,
            Math.Abs(Speed), airborne ? popKind : SkatePopKind.None, grinding ? GrindKind : SkateGrindKind.None, grinding && GrindFrontside,
            airborne && LateTrick && (flipAxis != 0f || shoveAxis != 0f), airborne ? bodyAxis : 0f, TailFirst != DeckReversed);
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
        // The board stops on the edge as it came: square to it, or along it for a 50-50 stall.
        var stallRel = Delta(SkateGrind.Heading, DeckYaw);
        grindNose = Delta(SkateGrind.Heading, NoseYaw);
        GrindDeck = SkateGrind.StallSquare ? stallRel : Math.Abs(stallRel) > 90f ? 180f : 0f;
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
            var planted = GrindKind == SkateGrindKind.Handplant;
            var noseStall = NoseYaw; var deckStall = DeckYaw;
            if (planted) { Yaw += 180f; Speed = stallSign * StallOut; }
            else Speed = -stallSign * StallOut;
            if (popped)
            {
                Pops++; jumpWanted = -99f;
                TakeOff(true, OlliePop * wantScale * PopHeight(wantKind) + AirGravity * dt);
                if (planted) ComeRound(noseStall, deckStall);
                AirStep(pos, dt);
            }
            else
            {
                TakeOff(false, 0f);
                if (planted) ComeRound(noseStall, deckStall);
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
            // Out of a darkslide the board finishes the flip it was caught in: the remaining half,
            // the same way round, before the ground; the scorer carries the half it has.
            var dark = Trick.DarkslideCaught ? Math.Sign(Trick.Flip) : 0;
            Pops++; GrindEnds++; jumpWanted = -99f; SkateGrind.Release(dt);
            Speed = SkateGrind.Speed; Yaw = SkateGrind.Heading;
            LimitSpeed(Cap, Cap);
            if (dark != 0) { wantFlip = 0; wantShove = 0f; }
            TakeOff(true, OlliePop * wantScale * PopHeight(wantKind) + AirGravity * dt);
            if (dark != 0) { flipLeft = 180f * dark; SkateBoard.GrindRoll = 0f; FlipDeg = Trick.Flip; }
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
            var noseHeld = NoseYaw; var deckHeld = DeckYaw;
            Yaw = SkateGrind.Heading;
            TakeOff(false, 0f);
            ComeRound(noseHeld, deckHeld);
            velocity = Dir(Yaw) * Speed;
            TrickTick(false, false, 0, 0, 0, 0, dt);
            return;
        }
        if (!SkateGrind.TryContinue(pos, dt, out ended) || ended)
        {
            GrindEnds++; SkateGrind.Release(dt);
            // Off the edge the board comes round to the way of travel, as it does in Skate: from the
            // angle it had on the edge, over a moment.
            var noseOff = NoseYaw; var deckOff = DeckYaw;
            Speed = SkateGrind.Speed; Yaw = SkateGrind.Heading;
            LimitSpeed(Cap, Cap);
            TakeOff(false, 0f);
            ComeRound(noseOff, deckOff);
            velocity = Dir(Yaw) * Speed;
            TrickTick(false, false, 0, 0, 0, 0, dt);
            return;
        }
        Speed = SkateGrind.Speed; Yaw = SkateGrind.Heading;
        LimitSpeed(Cap, Cap);
        Tangent = Dir(SkateGrind.Heading);
        GrindVelocity(pos);
        // Along the edge the stick leans the board onto one truck: back for a 5-0, forward for a nosegrind.
        if (GrindKind == SkateGrindKind.Darkslide) { }
        else if (GrindAlong) GrindKind = SkateKeys.Manual ? SkateGrindKind.FiveO : SkateKeys.NoseManual ? SkateGrindKind.Nosegrind : SkateGrindKind.FiftyFifty;
        // Across the edge on the tail or the nose, the stick held back or forward stands the deck up: a bluntslide.
        else if (GrindKind == SkateGrindKind.Tailslide || GrindKind == SkateGrindKind.Bluntslide) GrindKind = SkateKeys.Manual ? SkateGrindKind.Bluntslide : SkateGrindKind.Tailslide;
        else if (GrindKind == SkateGrindKind.Noseslide || GrindKind == SkateGrindKind.Nosebluntslide) GrindKind = SkateKeys.NoseManual ? SkateGrindKind.Nosebluntslide : SkateGrindKind.Noseslide;
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
        else if (across > 65f) GrindKind = t > 0.25f ? (SkateKeys.NoseManual ? SkateGrindKind.Nosebluntslide : SkateGrindKind.Noseslide) : t < -0.25f ? (SkateKeys.Manual ? SkateGrindKind.Bluntslide : SkateGrindKind.Tailslide) : grindNoseFar > 0f ? SkateGrindKind.Boardslide : SkateGrindKind.Lipslide;
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
