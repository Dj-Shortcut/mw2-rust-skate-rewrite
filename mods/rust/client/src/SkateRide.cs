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
    public const float PushAccel = 7f, MaxPush = 11f, BrakeDecel = 12f, RollDecel = 0.45f, MaxSpeed = 13f, MaxReverse = 6f, TurnRate = 150f, SlopeGain = 1.6f;
    public const float AirGravity = 16f, OlliePop = 6f, MaxFall = 30f, BailImpact = 12f, BailSeconds = 1.3f, BailDecel = 10f, PushPeriod = 0.8f, SeaLevel = -0.6f, LowestCap = 1.5f, LagStep = 0.1f, RaiseWait = 3f, CapForget = 30f, ManualPace = 1f, ManualHold = 0.3f, HopGrace = 0.3f;
    private const float RayLift = 0.6f, FeetProbe = 0.3f, AirProbe = 3f;
    public static readonly int GroundMask = ~((1 << 12) | (1 << 17) | (1 << 18) | (1 << 4) | (1 << 10) | (1 << 9) | (1 << 2));

    public static RideMode Mode = RideMode.Off;
    public static bool On { get { return Mode != RideMode.Off; } }
    public static bool Grounded, Braking, Jumped, TrickAir, Manual, NoseManual;
    public static SkateGrabKind Grab;
    public static bool Grabbing { get { return Grab != SkateGrabKind.None; } }
    public static bool Frozen;
    public static float Speed, Yaw, Lean, VSpeed, AirTime, AirPeak, Clearance = 99f, Actual, ActualV, Top, Cap = MaxSpeed, ModeAt, LandAt = -99f, LandImpact, PushPhase;
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
    public static int Steps, Resets, Pops, Lands, Bails, Pushes, GrindStarts, GrindEnds, Refused;
    private static Rigidbody body;
    private static Vector3 lastPos, lastDir, velocity;
    private static float lastSpeed, lastPull = -999f, raisedAt = -999f, putBackAt = -999f, raiseWait = RaiseWait, now, mountAt, takeOffY, flipLeft, shoveLeft, jumpWanted = -99f;
    private static bool hasLast, refusedSinceRaise, spinning;
    private static int stuck;
    // The pop that was asked for, kept with the press for the moment the press itself is kept; what
    // of it is still to be started in the air; and which way a frontside turn goes in this jump.
    private static SkatePopKind wantKind = SkatePopKind.Ollie, popKind;
    private static int wantFlip, pendFlip;
    private static float wantShove, wantScale = 1f, pendShove, spinSide = 1f;
    private const float SpinSlack = 15f;

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
    // for a regular rider going nose first, to the right when the tail leads. NoseYaw is where the
    // rider's stance points (spin included), DeckYaw where the board's own nose points.
    public static float SpinYaw { get { return -(float)SpinDeg * spinSide; } }
    public static float NoseYaw { get { return Yaw + (Trick.Switch ? 180f : 0f) + SpinYaw; } }
    public static float DeckYaw { get { return NoseYaw - (float)ShoveDeg * spinSide + (DeckReversed ? 180f : 0f); } }
    public static bool TailFirst { get { return Trick.Switch != (Speed < -0.3f); } }
    public static bool Rotating { get { return spinning || Math.Abs(flipLeft) > 0.001f || Math.Abs(shoveLeft) > 0.001f; } }

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
        VSpeed = 0f; AirTime = 0f; flipLeft = shoveLeft = pendShove = 0f; pendFlip = 0; SpinDeg = FlipDeg = ShoveDeg = 0; Jumped = TrickAir = Braking = Manual = NoseManual = spinning = DeckReversed = false; PushPhase = 0f; stuck = 0;
        Grab = SkateGrabKind.None; spinSide = 1f; wantKind = SkatePopKind.Ollie; wantFlip = 0; wantShove = 0f; wantScale = 1f;
        Trick = new SkateTrickState(false); TrickName = ""; ComboPoints = 0; TrickAt = BankedAt = LandAt = jumpWanted = -99f; OffReason = "";
        SkateHud.Clear();
        Normal = ViewNormal = Vector3.up; Position = b.position;
        SkateGrind.Reset();
        // Getting on while going up (the second press of a double jump) finishes that jump on the
        // board; the ground is still within reach of the feet then, and ground movement would cut it.
        if (rise > 1f) TakeOff(false, rise); else Enter(RideMode.Ground);
    }

    public static void Dismount(string reason)
    {
        if (Mode == RideMode.Off) return;
        Mode = RideMode.Off; OffReason = reason; Grab = SkateGrabKind.None; Manual = NoseManual = false;
        SkateKeys.EndStep();
    }

    private static void Enter(RideMode m) { Mode = m; ModeAt = Time.realtimeSinceStartup; }

    public static string Status()
    {
        return Mode + " speed=" + Speed.ToString("F1") + " actual=" + Actual.ToString("F1") + " top=" + Top.ToString("F1") + " cap=" + Cap.ToString("F1") + " yaw=" + Yaw.ToString("F0")
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
            if (Speed > Top) Top = Speed;
            body.linearVelocity = velocity;
            lastPos = pos; lastDir = Dir(Yaw); lastSpeed = Speed; hasLast = true; Steps++;
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
        if ((lastSpeed > 1f && Actual < -3f) || (lastSpeed < -1f && Actual > 3f))
        {
            // The server put the player back. Without its skate plugin it allows walking pace and
            // refuses more, tick after tick, until the rider is slower: two in a row lower the cap,
            // and a raise that is refused again is tried less and less often. One on its own is
            // left alone, and so is a ride's start: after waking the first seconds are put back at
            // any speed. A long frame is left alone too: the move it sent was too big for the server
            // because of the frame, not the speed.
            Resets++;
            Out.Say("SKATE put back #" + Resets + ", " + (now - mountAt).ToString("F2") + " s after getting on: moved " + Out.V3(pos - lastPos) + " in a step at speed " + lastSpeed.ToString("F1"));
            var again = now - putBackAt < 1f; putBackAt = now;
            if (again && Time.unscaledDeltaTime <= LagStep && now - mountAt > 3f && Math.Abs(lastSpeed) > LowestCap)
            {
                if (!refusedSinceRaise && now - raisedAt < 45f) raiseWait = Math.Min(raiseWait * 2f, 480f);
                refusedSinceRaise = true; lastPull = now;
                Cap = Math.Max(LowestCap, Math.Min(Cap, Math.Abs(lastSpeed)) * 0.85f);
            }
            // Being put back is not an obstacle: the board keeps what speed the cap allows.
            Speed = Clamp(Speed, -Cap, Cap);
            return;
        }
        if (Speed > 0f) { var limit = (Actual > 0f ? Actual : 0f) + 2f; if (Speed > limit) Speed = limit; }
        else if (Speed < 0f) { var limit = (Actual < 0f ? Actual : 0f) - 2f; if (Speed < limit) Speed = limit; }
    }

    // Land below sea level is under water unless something is overhead (tunnels, basements).
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
    }

    private static void GroundStep(float dt)
    {
        if (!Grounded) { TakeOff(false, velocity.y + AirGravity * dt); Sense(Position); AirStep(Position, dt); return; }
        var before = Yaw;
        var maxTurn = TurnRate / (1f + Math.Abs(Speed) / 8f) * dt;
        // A stick steers by how far it is pushed; keys and the mouse turn the board toward where the rider looks.
        if (SkateKeys.Analog) Yaw += Clamp(SkateKeys.Steer, -1f, 1f) * maxTurn;
        else Yaw += Clamp(Delta(Yaw, SkateKeys.LookYaw + ((SkateKeys.Left ? -1f : 0f) + (SkateKeys.Right ? 1f : 0f)) * 40f), -maxTurn, maxTurn);
        var dir = Dir(Yaw);
        var tangent = dir - Normal * Vector3.Dot(dir, Normal);
        tangent = tangent.sqrMagnitude > 0.0001f ? tangent.normalized : dir;
        Tangent = tangent; Braking = SkateKeys.Brake;
        // A press up to a moment before touch-down still counts, so tricks can be chained.
        if (now - jumpWanted < 0.12f)
        {
            Pops++; jumpWanted = -99f;
            TakeOff(true, tangent.y * Speed + OlliePop * wantScale * Normal.y + AirGravity * dt);
            AirStep(Position, dt);
            return;
        }
        Speed += Vector3.Dot(Physics.gravity, tangent) * SlopeGain * dt;
        // A manual is held on two wheels: it needs some speed and leaves no foot free to push. One
        // that has begun lasts down to a crawl: ending it at the pace it needs to begin would let a
        // held push in and out every few tenths of a second, each time ending and scoring it.
        var pace = Math.Abs(Speed);
        Manual = SkateKeys.Manual && !SkateKeys.Brake && pace > (Manual ? ManualHold : ManualPace);
        NoseManual = !Manual && SkateKeys.NoseManual && !SkateKeys.Brake && pace > (NoseManual ? ManualHold : ManualPace);
        var pushing = SkateKeys.Push && !SkateKeys.Brake && !Manual && !NoseManual && Speed < MaxPush;
        if (pushing) Speed = Math.Min(MaxPush, Speed + PushAccel * dt);
        if (SkateKeys.Brake) Speed = Toward(Speed, 0f, BrakeDecel * dt);
        Speed = Toward(Speed, 0f, RollDecel * dt);
        Speed = Clamp(Speed, -Math.Min(MaxReverse, Cap), Cap);
        if (pushing || PushPhase > 0f)
        {
            if (PushPhase <= 0f) Pushes++;
            PushPhase += dt / PushPeriod;
            if (PushPhase >= 1f) PushPhase = 0f;
        }
        velocity = dir * Speed;
        velocity.y = tangent.y * Speed - Clamp(Clearance * 8f, 0f, 3f);
        Lean += (Clamp(Delta(before, Yaw) / dt * 0.12f, -25f, 25f) - Lean) * 0.2f;
        TrickTick(false, false, 0, 0, 0, 0, dt);
    }

    private static void TakeOff(bool jumped, float vertical)
    {
        // Read while the board still rolls: which end leads decides which way is frontside.
        spinSide = TailFirst ? -1f : 1f;
        Enter(RideMode.Air);
        // A manual survives a hop over a bump; a jump ends it, and so does a longer fall below.
        Jumped = jumped; TrickAir = jumped; if (jumped) Manual = NoseManual = false; VSpeed = vertical; AirTime = 0f; AirPeak = 0f; takeOffY = Position.y;
        flipLeft = shoveLeft = 0f; spinning = false; PushPhase = 0f; Braking = false; stuck = 0;
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
        var flips = pendFlip + SkateKeys.LateFlip; pendFlip = 0;
        if (flips != 0 && (Math.Abs(flipLeft) < 0.001f || Math.Sign(flipLeft) == Math.Sign(flips)))
        {
            if (left >= (Math.Abs(flipLeft) + 360f * Math.Abs(flips)) / (float)SkateMotion.FlipRate) { flipLeft += 360f * flips; TrickAir = true; }
            else Refused++;
        }
        if (pendShove != 0f)
        {
            if (left >= Math.Abs(pendShove) / (float)SkateMotion.ShoveRate) { shoveLeft = pendShove; TrickAir = true; }
            else Refused++;
            pendShove = 0f;
        }
        var spinAxis = SpinAxis(left, dt);
        Grab = TrickAir ? SkateKeys.Grab : SkateGrabKind.None;

        var flipStep = (float)SkateMotion.FlipRate * dt; var shoveStep = (float)SkateMotion.ShoveRate * dt;
        var flipAxis = Clamp(flipLeft / flipStep, -1f, 1f); var shoveAxis = Clamp(shoveLeft / shoveStep, -1f, 1f);

        stuck = VSpeed < -2f && Math.Abs(ActualV) < 0.3f && AirTime > 0.2f ? stuck + 1 : 0;
        if ((VSpeed <= 0f && Clearance <= Math.Max(0.04f, -VSpeed * dt + 0.02f)) || stuck >= 3)
        {
            var impact = stuck >= 3 ? 0f : -VSpeed;
            Land(impact, dt);
            if (Mode == RideMode.Ground)
            {
                velocity = dir * Speed;
                velocity.y = -Clamp(Clearance / dt, 0f, impact);
            }
            else BailStep(velocity, dt);
            return;
        }
        velocity = dir * Speed + Vector3.up * VSpeed;
        if (VSpeed < 0f && !Rotating && SkateGrind.TryCapture(pos, velocity, NoseYaw, dt))
        {
            LastAir = AirLine("grind");
            var impact = -VSpeed;
            Enter(RideMode.Grind); GrindStarts++;
            Yaw = SkateGrind.Heading; Speed = SkateGrind.Speed; VSpeed = 0f;
            TrickTick(false, true, 0, 0, 0, impact, dt);
            TrickAir = Jumped = false; AirTime = 0f; Grab = SkateGrabKind.None;
            if (Mode == RideMode.Grind) GrindVelocity(pos);
            return;
        }
        // A fall without a jump is no trick, and it is not rolling either: the trick module is not
        // stepped, so an open combo neither banks nor is lost until the rider is down.
        if (TrickAir)
        {
            TrickTick(true, false, spinAxis, flipAxis, shoveAxis, 0, dt);
            flipLeft -= flipAxis * flipStep; shoveLeft -= shoveAxis * shoveStep;
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
        if (!TrickAir && !(want != 0f && SkateKeys.SpinPressed)) return 0f;
        var at = TrickAir ? (float)Trick.Spin : 0f;
        if (want != 0f)
        {
            var next = at + want * step;
            var ahead = want > 0f ? (float)Math.Ceiling(next / 180.0 - 0.0001) * 180f - next : next - (float)Math.Floor(next / 180.0 + 0.0001) * 180f;
            if (ahead <= (left - dt) * rate + SpinSlack) { TrickAir = spinning = true; return want; }
            if (SkateKeys.SpinPressed) Refused++;
        }
        if (!TrickAir) return 0f;
        var rest = (float)Math.Round(at / 180.0, MidpointRounding.AwayFromZero) * 180f - at;
        spinning = Math.Abs(rest) > 1f;
        return Clamp(rest / step, -1f, 1f);
    }

    private static string AirLine(string end)
    {
        return "air " + end + " time=" + AirTime.ToString("F2") + " peak=" + AirPeak.ToString("F2") + " impact=" + (-VSpeed).ToString("F1") + " spin=" + SpinDeg.ToString("F0") + " flip=" + FlipDeg.ToString("F0")
            + (Math.Abs(ShoveDeg) > 0.5 ? " shove=" + ShoveDeg.ToString("F0") : "") + " jumped=" + Jumped + " speed=" + Speed.ToString("F1");
    }

    private static void Land(float impact, float dt)
    {
        LastAir = AirLine("land");
        if (AirTime > 0.12f || impact > 1.5f) { Lands++; LandAt = now; LandImpact = impact; }
        Enter(RideMode.Ground);
        if (TrickAir) TrickTick(false, false, 0, 0, 0, impact, dt);
        else if (impact > BailImpact) Bail();
        else TrickTick(false, false, 0, 0, 0, 0, dt);
        flipLeft = shoveLeft = 0f; spinning = false; TrickAir = Jumped = false; Grab = SkateGrabKind.None; AirTime = 0f;
        if (Mode == RideMode.Ground) { SpinDeg = 0; FlipDeg = 0; ShoveDeg = 0; }
    }

    private static void TrickTick(bool airborne, bool grinding, float spinAxis, float flipAxis, float shoveAxis, float impact, float dt)
    {
        SkateTrickResult r; string error;
        var rolling = !airborne && !grinding;
        var input = new SkateTrickInput(spinAxis, flipAxis, shoveAxis, airborne ? Grab : SkateGrabKind.None, airborne, grinding, impact, Manual && rolling, NoseManual && !Manual && rolling,
            Math.Abs(Speed), airborne ? popKind : SkatePopKind.None);
        if (!SkateTricks.TryStep(Trick, input, dt, BailImpact, out r, out error))
        {
            Out.Say("SKATE trick step refused: " + error);
            return;
        }
        Trick = r.State; ComboPoints = r.ComboPoints;
        SpinDeg = airborne ? r.BoardSpin : 0; FlipDeg = airborne ? r.BoardFlip : 0; ShoveDeg = airborne ? r.BoardShove : 0;
        if ((r.Events & SkateTrickEvents.Bailed) != 0) { Bail(); return; }
        // The landing hands the turns back rounded: an odd number of half shoves turned the board round.
        if ((r.Events & SkateTrickEvents.Landed) != 0 && ((long)Math.Round(r.BoardShove / 180.0) & 1) != 0) DeckReversed = !DeckReversed;
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
        SkateTrickResult r; string error;
        SkateHud.Bailed(ComboPoints);
        if (SkateTricks.TryBail(Trick, out r, out error)) Trick = r.State;
        ComboPoints = 0; Manual = NoseManual = false;
        if (Mode == RideMode.Grind) GrindEnds++;
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

    private static void GrindStep(Vector3 pos, float dt)
    {
        bool ended;
        if (now - jumpWanted < 0.12f)
        {
            Pops++; GrindEnds++; jumpWanted = -99f; SkateGrind.Release(dt);
            TakeOff(true, OlliePop * wantScale + AirGravity * dt);
            AirStep(pos, dt);
            return;
        }
        if (!SkateGrind.TryContinue(pos, dt, out ended) || ended)
        {
            GrindEnds++; SkateGrind.Release(dt);
            Speed = SkateGrind.Speed;
            TakeOff(false, 0f);
            velocity = Dir(Yaw) * Speed;
            TrickTick(false, false, 0, 0, 0, 0, dt);
            return;
        }
        Speed = SkateGrind.Speed; Yaw = SkateGrind.Heading;
        Tangent = Dir(Yaw);
        GrindVelocity(pos);
        TrickTick(false, true, 0, 0, 0, 0, dt);
    }

    private static void GrindVelocity(Vector3 pos)
    {
        var to = SkateGrind.Target - pos;
        var side = to; side.y = 0f;
        var dir = Dir(Yaw);
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
