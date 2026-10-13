using System;

namespace Shortcut.RustMod
{
    public struct SkateVector
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public SkateVector(double x, double y, double z) { X = x; Y = y; Z = z; }
        public double LengthSquared { get { return Dot(this, this); } }
        public double Length { get { return Math.Sqrt(LengthSquared); } }
        public static SkateVector operator +(SkateVector a, SkateVector b) { return new SkateVector(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static SkateVector operator -(SkateVector a, SkateVector b) { return new SkateVector(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static SkateVector operator *(SkateVector a, double b) { return new SkateVector(a.X * b, a.Y * b, a.Z * b); }
        public static double Dot(SkateVector a, SkateVector b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
    }

    public struct SkateHull
    {
        public readonly SkateVector HalfExtents;
        public readonly SkateVector CentreOffset;
        internal SkateHull(SkateVector extents, SkateVector offset) { HalfExtents = extents; CentreOffset = offset; }
    }

    public struct SkateHit
    {
        public readonly bool HasHit;
        public readonly bool StartedSolid;
        public readonly double Fraction;
        public readonly SkateVector Normal;
        public readonly long RailId;
        public SkateHit(bool hasHit, bool startedSolid, double fraction, SkateVector normal)
            : this(hasHit, startedSolid, fraction, normal, 0) { }
        public SkateHit(bool hasHit, bool startedSolid, double fraction, SkateVector normal, long railId)
        { HasHit = hasHit; StartedSolid = startedSolid; Fraction = fraction; Normal = normal; RailId = railId; }
        public static SkateHit Miss { get { return new SkateHit(false, false, 1, default(SkateVector)); } }
    }

    public interface ISkateWorld
    {
        bool TrySweep(SkateVector start, SkateVector end, SkateHull hull, out SkateHit hit);
        bool TryClear(SkateVector position, SkateHull hull, out bool clear);
    }

    public struct SkateInput
    {
        public readonly bool Push;
        public readonly bool Brake;
        public readonly bool Jump;
        public readonly double Steer;
        public readonly double Spin;
        public readonly double Flip;
        public SkateInput(bool push, bool brake, bool jump, double steer, double spin, double flip)
        { Push = push; Brake = brake; Jump = jump; Steer = steer; Spin = spin; Flip = flip; }
    }

    public enum SkateMode { Grounded, Airborne, Bailed, Grinding }
    [Flags]
    public enum SkateEvents { None = 0, Jumped = 1, Landed = 2, Bailed = 4, Blocked = 8, RailCaptured = 16, RailReleased = 32 }

    public sealed class SkateState
    {
        public SkateVector Position { get; private set; }
        public SkateVector Velocity { get; private set; }
        public SkateVector GroundNormal { get; private set; }
        public double Yaw { get; private set; }
        public double Spin { get; private set; }
        public double Flip { get; private set; }
        public int TravelSign { get; private set; }
        public bool PreviousJump { get; private set; }
        public SkateMode Mode { get; private set; }
        public SkateRail ActiveRail { get; private set; }
        public double RailProgress { get; private set; }
        public double RailSpeed { get; private set; }
        public long CooldownRailId { get; private set; }
        public double CooldownSeconds { get; private set; }

        internal SkateState(SkateVector position, SkateVector velocity, SkateVector normal, double yaw,
                            double spin, double flip, int travelSign, bool previousJump, SkateMode mode)
        { Position = position; Velocity = velocity; GroundNormal = normal; Yaw = yaw;
          Spin = spin; Flip = flip; TravelSign = travelSign; PreviousJump = previousJump; Mode = mode; }

        internal SkateState(SkateVector position, SkateVector velocity, SkateVector normal, double yaw,
                            double spin, double flip, int travelSign, bool previousJump, SkateMode mode,
                            SkateRail rail, double progress, double speed, long cooldownRailId, double cooldownSeconds)
            : this(position, velocity, normal, yaw, spin, flip, travelSign, previousJump, mode)
        { ActiveRail = rail; RailProgress = progress; RailSpeed = speed;
          CooldownRailId = cooldownRailId; CooldownSeconds = cooldownSeconds; }
    }

    public static class SkateMotion
    {
        public const double MaximumStep = 0.05;
        public const double MaximumGroundSpeed = 25;
        public const double PushAcceleration = 7;
        public const double BrakeDeceleration = 18;
        public const double RollingDeceleration = 0.4;
        public const double JumpSpeed = 5.2;
        public const double Gravity = 18;
        public const double MaximumFallSpeed = 40;
        public const double MaximumLandingFallSpeed = 8;
        public const double RotationRate = 360;
        public const double FlipRate = 720;
        public const double ShoveRate = 720;
        public const double Skin = 0.002;
        public const double WalkableNormalY = 0.7071067811865476;
        public static readonly SkateHull MountedHull = new SkateHull(new SkateVector(0.55, 0.95, 0.55), new SkateVector(0, 0.8, 0));
        public static readonly SkateHull StandingHull = new SkateHull(new SkateVector(0.4, 0.9, 0.4), new SkateVector(0, 0.75, 0));
        public const double RailDrag = 0.6;
        public const double MinimumCaptureSpeed = 1.5;
        public const double MinimumGrindingSpeed = 0.8;
        public const double RailRecaptureDelay = 0.25;
        public const double RailAlignment = 0.9063077870366499;
        private static readonly SkateVector Up = new SkateVector(0, 1, 0);

        public static bool TryMount(SkateVector position, double yaw, ISkateWorld world, out SkateState state, out string error)
        {
            state = null;
            error = null;
            if (!Coordinate(position) || !Finite(yaw) || Math.Abs(yaw) > 360000 || world == null)
                return Fail("Invalid mount request.", out error);
            bool clear;
            if (!Clear(world, position, MountedHull, out clear, out error)) return false;
            if (!clear) return Fail("The rider space is blocked.", out error);
            SkateVector supported, normal;
            bool found;
            if (!Support(world, position, MountedHull, 0.3, out found, out supported, out normal, out error)) return false;
            if (!found) return Fail("No nearby walkable support.", out error);
            if (!Clear(world, supported, MountedHull, out clear, out error)) return false;
            if (!clear) return Fail("The supported rider space is blocked.", out error);
            state = new SkateState(supported, default(SkateVector), normal, Yaw(yaw), 0, 0, 1, false, SkateMode.Grounded);
            return true;
        }

        public static bool TryStep(SkateState state, SkateInput input, double dt, ISkateWorld world,
                                   out SkateState next, out SkateEvents events, out string error)
        { return TryStep(state, input, dt, world, SkateRailSet.Empty, out next, out events, out error); }

        public static bool TryStep(SkateState state, SkateInput input, double dt, ISkateWorld world, SkateRailSet rails,
                                   out SkateState next, out SkateEvents events, out string error)
        {
            next = state;
            events = SkateEvents.None;
            error = null;
            if (!Valid(state) || !Axis(input.Steer) || !Axis(input.Spin) || !Axis(input.Flip) ||
                !Finite(dt) || dt <= 0 || dt > MaximumStep || world == null || rails == null)
                return Fail("Invalid skate step.", out error);
            if (state.Mode == SkateMode.Bailed) return Fail("A bailed rider must dismount or remount.", out error);
            bool clear;
            if (!Clear(world, state.Position, MountedHull, out clear, out error)) return false;
            if (!clear) return Fail("The rider space is blocked.", out error);
            SkateState candidate;
            SkateEvents pending;
            bool success = state.Mode == SkateMode.Grinding
                ? TryGrinding(state, input, dt, world, rails, out candidate, out pending, out error)
                : TryOrdinary(state, input, dt, world, rails, true, out candidate, out pending, out error);
            if (!success) return false;
            if (!Valid(candidate)) return Fail("Skate movement exceeds the allowed bounds.", out error);
            if (!Clear(world, candidate.Position, MountedHull, out clear, out error)) return false;
            if (!clear) return Fail("The final rider space is blocked.", out error);
            next = candidate;
            events = pending;
            return true;
        }

        private static bool TryOrdinary(SkateState state, SkateInput input, double dt, ISkateWorld world, SkateRailSet rails,
                                        bool allowCapture, out SkateState next, out SkateEvents events, out string error)
        {
            next = state;
            events = SkateEvents.None;
            error = null;
            var position = state.Position;
            var velocity = state.Velocity;
            var normal = state.GroundNormal;
            var mode = state.Mode;
            var yaw = state.Yaw;
            var spin = state.Spin;
            var flip = state.Flip;
            var sign = state.TravelSign;
            var pending = SkateEvents.None;

            if (mode == SkateMode.Grounded)
            {
                bool found;
                if (!Support(world, position, MountedHull, 0.12, out found, out position, out normal, out error)) return false;
                if (!found) mode = SkateMode.Airborne;
                else
                {
                    double speed = Math.Min(MaximumGroundSpeed, velocity.Length);
                    if (input.Push && !input.Brake) speed += PushAcceleration * dt;
                    var downhill = Project(Forward(yaw) * sign, normal);
                    speed += SkateVector.Dot(Up * -Gravity, Unit(downhill)) * dt;
                    bool reverse = speed < 0;
                    speed = Math.Abs(speed);
                    double deceleration = input.Brake ? BrakeDeceleration : RollingDeceleration;
                    speed = Math.Max(0, speed - deceleration * dt);
                    speed = Math.Min(MaximumGroundSpeed, speed);
                    if (reverse && speed > 0) sign = -sign;
                    yaw = Yaw(yaw + input.Steer * 90 * Math.Min(1, speed / 4) * dt * sign);
                    var tangent = Project(Forward(yaw) * sign, normal);
                    velocity = Unit(tangent) * speed;
                    if (input.Jump && !state.PreviousJump)
                    {
                        velocity = new SkateVector(velocity.X, JumpSpeed, velocity.Z);
                        mode = SkateMode.Airborne;
                        pending |= SkateEvents.Jumped;
                    }
                }
            }
            if (mode == SkateMode.Airborne)
            {
                velocity = new SkateVector(velocity.X, Math.Max(-MaximumFallSpeed, velocity.Y - Gravity * dt), velocity.Z);
                spin = Angle(spin + input.Spin * RotationRate * dt);
                flip = Angle(flip + input.Flip * FlipRate * dt);
            }

            var remaining = velocity * dt;
            var remainingTime = dt;
            for (int iteration = 0; iteration < 3 && remaining.LengthSquared > 1e-12; iteration++)
            {
                SkateHit hit;
                var end = position + remaining;
                if (!Sweep(world, position, end, MountedHull, out hit, out error)) return false;
                if (!hit.HasHit) { position = end; remaining = default(SkateVector); break; }
                position = position + remaining * hit.Fraction + hit.Normal * Skin;
                if (hit.Normal.Y >= WalkableNormalY && SkateVector.Dot(remaining, hit.Normal) < 0)
                {
                    if (mode == SkateMode.Airborne)
                    {
                        SkateState captured = null;
                        var contact = new SkateState(position, velocity, normal, yaw, spin, flip, sign, input.Jump, mode,
                            default(SkateRail), 0, 0, state.CooldownRailId, state.CooldownSeconds);
                        contact = AdvanceCooldown(contact, dt - remainingTime * (1 - hit.Fraction));
                        if (allowCapture && !TryCapture(contact, hit, world, rails, out captured, out error)) return false;
                        if (captured != null)
                        {
                            pending |= SkateEvents.RailCaptured;
                            double unused = remainingTime * (1 - hit.Fraction);
                            if (unused > 0)
                            {
                                SkateEvents travelEvents;
                                if (!TryGrinding(captured, input, unused, world, rails, out captured, out travelEvents, out error)) return false;
                                pending |= travelEvents;
                            }
                            next = captured;
                            events = pending;
                            return true;
                        }
                        if (-SkateVector.Dot(velocity, hit.Normal) > MaximumLandingFallSpeed || Math.Abs(flip) > 25)
                        { mode = SkateMode.Bailed; pending |= SkateEvents.Bailed; break; }
                        var flat = new SkateVector(velocity.X, 0, velocity.Z);
                        var facing = Forward(Yaw(yaw + spin));
                        double alignment = flat.Length < 0.1 ? 1 : SkateVector.Dot(Unit(flat), facing);
                        if (Math.Abs(alignment) < WalkableNormalY)
                        { mode = SkateMode.Bailed; pending |= SkateEvents.Bailed; break; }
                        yaw = Yaw(yaw + spin);
                        sign = alignment < 0 ? -1 : 1;
                        spin = 0;
                        flip = 0;
                        mode = SkateMode.Grounded;
                        pending |= SkateEvents.Landed;
                    }
                    normal = hit.Normal;
                    velocity = Project(velocity, normal);
                    if (velocity.Length > MaximumGroundSpeed) velocity = Unit(velocity) * MaximumGroundSpeed;
                    remaining = Project(remaining * (1 - hit.Fraction), normal);
                    remainingTime *= 1 - hit.Fraction;
                    if (hit.Fraction <= 1e-8) { remaining = default(SkateVector); break; }
                }
                else
                {
                    pending |= SkateEvents.Blocked;
                    if (mode == SkateMode.Airborne || velocity.Length > 4)
                    { mode = SkateMode.Bailed; pending |= SkateEvents.Bailed; }
                    velocity = default(SkateVector);
                    remaining = default(SkateVector);
                }
            }
            if (mode == SkateMode.Grounded)
            {
                bool found;
                if (!Support(world, position, MountedHull, 0.12, out found, out position, out normal, out error)) return false;
                if (!found) mode = SkateMode.Airborne;
                else velocity = Project(velocity, normal);
            }
            if (mode == SkateMode.Bailed)
            { velocity = default(SkateVector); spin = 0; flip = 0; }
            var candidate = new SkateState(position, velocity, normal, yaw, spin, flip, sign, input.Jump, mode,
                default(SkateRail), 0, 0, state.CooldownRailId, state.CooldownSeconds);
            next = AdvanceCooldown(candidate, dt);
            events = pending;
            return true;
        }

        private static bool TryCapture(SkateState contact, SkateHit hit, ISkateWorld world, SkateRailSet rails,
                                       out SkateState captured, out string error)
        {
            captured = null;
            error = null;
            SkateRail rail;
            if (hit.RailId <= 0 || !rails.TryGet(hit.RailId, out rail) ||
                (contact.CooldownRailId == rail.Id && contact.CooldownSeconds > 0) || contact.Velocity.Y >= 0 || hit.Normal.Y < 0.99 ||
                -SkateVector.Dot(contact.Velocity, hit.Normal) > MaximumLandingFallSpeed || Math.Abs(contact.Flip) > 25)
                return true;
            var direction = rail.Direction;
            double progress = SkateVector.Dot(contact.Position - rail.Start, direction);
            if (progress < 0 || progress > rail.Length) return true;
            var seated = Seat(rail, progress);
            var delta = contact.Position - seated;
            if (delta.X * delta.X + delta.Z * delta.Z > 0.12 * 0.12 || Math.Abs(delta.Y) > 0.01) return true;
            var flat = new SkateVector(contact.Velocity.X, 0, contact.Velocity.Z);
            double speed = SkateVector.Dot(flat, direction);
            double yaw = Yaw(contact.Yaw + contact.Spin);
            double facing = SkateVector.Dot(Forward(yaw), direction);
            if (Math.Abs(speed) < MinimumCaptureSpeed || Math.Abs(SkateVector.Dot(Unit(flat), direction)) < RailAlignment ||
                Math.Abs(facing) < RailAlignment) return true;
            SkateHit snap;
            if (!Sweep(world, contact.Position, seated, MountedHull, out snap, out error)) return false;
            if (snap.HasHit) return true;
            bool clear;
            if (!Clear(world, seated, MountedHull, out clear, out error)) return false;
            if (!clear) return true;
            speed = Math.Sign(speed) * Math.Min(MaximumGroundSpeed, Math.Abs(speed));
            captured = new SkateState(seated, direction * speed, Up, yaw, 0, 0, speed * facing < 0 ? -1 : 1,
                contact.PreviousJump, SkateMode.Grinding, rail, progress, speed, contact.CooldownRailId, contact.CooldownSeconds);
            return true;
        }

        private static bool TryGrinding(SkateState state, SkateInput input, double dt, ISkateWorld world, SkateRailSet rails,
                                        out SkateState next, out SkateEvents events, out string error)
        {
            next = state;
            events = SkateEvents.None;
            error = null;
            SkateRail current;
            bool supported = false;
            if (rails.TryGet(state.ActiveRail.Id, out current) && SkateRailSet.Same(current, state.ActiveRail))
                if (!RailSupport(world, state.Position, state.ActiveRail.Id, out supported, out error)) return false;
            if (!supported)
                return ContinueReleased(state, input, dt, 0, world, rails, out next, out events, out error);
            if (input.Jump && !state.PreviousJump)
            {
                if (!ContinueReleased(state, input, dt, JumpSpeed, world, rails, out next, out events, out error)) return false;
                events |= SkateEvents.Jumped;
                return true;
            }
            double magnitude = Math.Abs(state.RailSpeed);
            double deceleration = input.Brake ? BrakeDeceleration : RailDrag;
            double slowTime = (magnitude - MinimumGrindingSpeed) / deceleration;
            double duration = Math.Min(dt, slowTime);
            double distance = magnitude * duration - 0.5 * deceleration * duration * duration;
            double toEndpoint = state.RailSpeed > 0 ? state.ActiveRail.Length - state.RailProgress : state.RailProgress;
            bool endpoint = toEndpoint <= distance;
            double travel = Math.Min(toEndpoint, distance);
            double consumed = RailTravelTime(magnitude, deceleration, travel);
            double speed = Math.Sign(state.RailSpeed) * Math.Max(MinimumGrindingSpeed, magnitude - deceleration * consumed);
            double progress = endpoint ? (state.RailSpeed > 0 ? state.ActiveRail.Length : 0)
                : state.RailProgress + Math.Sign(state.RailSpeed) * travel;
            var end = Seat(state.ActiveRail, progress);
            SkateHit hit;
            if (!Sweep(world, state.Position, end, MountedHull, out hit, out error)) return false;
            if (hit.HasHit)
            {
                double impactTime = RailTravelTime(magnitude, deceleration, travel * hit.Fraction);
                double impactSpeed = magnitude - deceleration * impactTime;
                var position = state.Position + (end - state.Position) * hit.Fraction + hit.Normal * Skin;
                var stopped = new SkateState(position, default(SkateVector), Up, state.Yaw, 0, 0,
                    state.TravelSign, input.Jump, impactSpeed > 4 ? SkateMode.Bailed : SkateMode.Airborne,
                    default(SkateRail), 0, 0, state.ActiveRail.Id, RailRecaptureDelay);
                events = SkateEvents.RailReleased | SkateEvents.Blocked;
                double unused = Math.Max(0, dt - impactTime);
                if (stopped.Mode == SkateMode.Bailed)
                {
                    events |= SkateEvents.Bailed;
                    stopped = AdvanceCooldown(stopped, unused);
                }
                else
                {
                    if (unused > 0)
                    {
                        SkateEvents freeEvents;
                        if (!TryOrdinary(stopped, input, unused, world, rails, false, out stopped, out freeEvents, out error)) return false;
                        events |= freeEvents;
                    }
                }
                next = stopped;
                return true;
            }
            var moving = new SkateState(end, state.ActiveRail.Direction * speed, Up, state.Yaw, 0, 0,
                state.TravelSign, input.Jump, SkateMode.Grinding, state.ActiveRail, progress, speed,
                state.CooldownRailId, state.CooldownSeconds);
            if (endpoint || slowTime <= dt)
                return ContinueReleased(moving, input, Math.Max(0, dt - consumed), 0,
                    world, rails, out next, out events, out error);
            if (!RailSupport(world, end, state.ActiveRail.Id, out supported, out error)) return false;
            if (!supported)
                return ContinueReleased(moving, input, 0, 0, world, rails, out next, out events, out error);
            next = AdvanceCooldown(moving, dt);
            return true;
        }

        private static SkateState AdvanceCooldown(SkateState state, double elapsed)
        {
            double cooldown = Math.Max(0, state.CooldownSeconds - elapsed);
            if (cooldown <= 1e-12) cooldown = 0;
            return new SkateState(state.Position, state.Velocity, state.GroundNormal, state.Yaw,
                state.Spin, state.Flip, state.TravelSign, state.PreviousJump, state.Mode,
                state.ActiveRail, state.RailProgress, state.RailSpeed,
                cooldown > 0 ? state.CooldownRailId : 0, cooldown);
        }

        private static double RailTravelTime(double speed, double deceleration, double distance)
        { return distance <= 0 ? 0 : 2 * distance / (speed + Math.Sqrt(Math.Max(0, speed * speed - 2 * deceleration * distance))); }

        private static bool ContinueReleased(SkateState state, SkateInput input, double dt, double verticalSpeed,
                                             ISkateWorld world, SkateRailSet rails, out SkateState next,
                                             out SkateEvents events, out string error)
        {
            var released = new SkateState(state.Position, state.Velocity + Up * verticalSpeed, Up, state.Yaw,
                0, 0, state.TravelSign, input.Jump, SkateMode.Airborne, default(SkateRail), 0, 0,
                state.ActiveRail.Id, RailRecaptureDelay);
            next = released;
            events = SkateEvents.RailReleased;
            error = null;
            if (dt <= 0) return true;
            SkateEvents freeEvents;
            if (!TryOrdinary(released, input, dt, world, rails, false, out next, out freeEvents, out error)) return false;
            events |= freeEvents;
            return true;
        }

        private static bool RailSupport(ISkateWorld world, SkateVector position, long railId,
                                        out bool supported, out string error)
        {
            supported = false;
            SkateHit hit;
            if (!Sweep(world, position, position - Up * 0.03, MountedHull, out hit, out error)) return false;
            supported = hit.HasHit && hit.RailId == railId && hit.Normal.Y >= 0.99 &&
                hit.Fraction * 0.03 <= Skin + 0.01;
            return true;
        }

        private static SkateVector Seat(SkateRail rail, double progress)
        { return rail.Start + rail.Direction * progress + Up * (0.15 + Skin); }

        public static bool TryDismount(SkateState state, ISkateWorld world, out SkateVector feet, out string error)
        {
            feet = default(SkateVector);
            error = null;
            if (!Valid(state) || world == null) return Fail("Invalid dismount request.", out error);
            var right = Forward(state.Yaw + 90);
            for (int side = 1; side >= -1; side -= 2)
            {
                var candidate = state.Position + right * (side * 1.2);
                SkateHit hit;
                if (!Sweep(world, state.Position, candidate, StandingHull, out hit, out error)) return false;
                if (hit.HasHit) continue;
                bool found, clear;
                SkateVector supported, normal;
                if (!Support(world, candidate, StandingHull, 0.3, out found, out supported, out normal, out error)) return false;
                if (!found) continue;
                if (!Clear(world, supported, StandingHull, out clear, out error)) return false;
                if (!clear) continue;
                var standingFeet = supported - Up * 0.15;
                if (!Coordinate(standingFeet)) return Fail("Dismount exceeds the allowed bounds.", out error);
                feet = standingFeet;
                return true;
            }
            return Fail("No clear supported dismount space.", out error);
        }

        private static bool Support(ISkateWorld world, SkateVector position, SkateHull hull, double reach,
                                    out bool found, out SkateVector supported, out SkateVector normal, out string error)
        {
            found = false;
            supported = position;
            normal = Up;
            SkateHit hit;
            var start = position;
            var end = position - Up * reach;
            if (!Sweep(world, start, end, hull, out hit, out error)) return false;
            if (!hit.HasHit || hit.Normal.Y < WalkableNormalY) return true;
            supported = start + (end - start) * hit.Fraction + Up * (Skin / hit.Normal.Y);
            normal = hit.Normal;
            found = true;
            return true;
        }

        private static bool Sweep(ISkateWorld world, SkateVector start, SkateVector end, SkateHull hull,
                                  out SkateHit hit, out string error)
        {
            hit = SkateHit.Miss;
            error = null;
            if (!Coordinate(start) || !Coordinate(end)) return Fail("World query exceeds the allowed bounds.", out error);
            try
            {
                if (!world.TrySweep(start, end, hull, out hit)) return Fail("World collision query failed.", out error);
            }
            catch (Exception) { return Fail("World collision query failed.", out error); }
            if (hit.RailId < 0 || (!hit.HasHit && hit.RailId != 0)) return Fail("Invalid world rail identity.", out error);
            if (hit.StartedSolid) return Fail("World collision query starts inside solid space.", out error);
            if (!Finite(hit.Fraction) || hit.Fraction < 0 || hit.Fraction > 1)
                return Fail("Invalid world collision fraction.", out error);
            if (!hit.HasHit)
            {
                if (hit.Fraction != 1) return Fail("Invalid empty world collision query.", out error);
                return true;
            }
            if (!Vector(hit.Normal) || Math.Abs(hit.Normal.LengthSquared - 1) > 0.0001 ||
                SkateVector.Dot(end - start, hit.Normal) > 0.000001)
                return Fail("Invalid world collision normal.", out error);
            return true;
        }

        private static bool Clear(ISkateWorld world, SkateVector position, SkateHull hull, out bool clear, out string error)
        {
            clear = false;
            error = null;
            if (!Coordinate(position)) return Fail("World query exceeds the allowed bounds.", out error);
            try
            {
                if (!world.TryClear(position, hull, out clear)) return Fail("World clearance query failed.", out error);
            }
            catch (Exception) { return Fail("World clearance query failed.", out error); }
            return true;
        }

        internal static bool Valid(SkateState state)
        {
            return state != null && Coordinate(state.Position) && Vector(state.Velocity) &&
                Math.Abs(state.Velocity.Y) <= MaximumFallSpeed &&
                state.Velocity.X * state.Velocity.X + state.Velocity.Z * state.Velocity.Z <= MaximumGroundSpeed * MaximumGroundSpeed + 0.000001 &&
                Vector(state.GroundNormal) && Math.Abs(state.GroundNormal.LengthSquared - 1) < 0.0001 &&
                Finite(state.Yaw) && state.Yaw >= 0 && state.Yaw < 360 && Axis(state.Spin / 180) && Axis(state.Flip / 180) &&
                (state.TravelSign == -1 || state.TravelSign == 1) &&
                (state.Mode == SkateMode.Grounded || state.Mode == SkateMode.Airborne || state.Mode == SkateMode.Bailed || state.Mode == SkateMode.Grinding) &&
                Finite(state.CooldownSeconds) && state.CooldownSeconds >= 0 && state.CooldownSeconds <= RailRecaptureDelay &&
                (state.CooldownSeconds > 0 ? state.CooldownRailId > 0 : state.CooldownRailId == 0) && ValidRailState(state);
        }
        private static bool ValidRailState(SkateState state)
        {
            if (state.Mode != SkateMode.Grinding)
                return state.ActiveRail.Id == 0 && state.RailProgress == 0 && state.RailSpeed == 0;
            return SkateRailSet.ValidRail(state.ActiveRail) && Finite(state.RailProgress) &&
                state.RailProgress >= 0 && state.RailProgress <= state.ActiveRail.Length && Finite(state.RailSpeed) &&
                Math.Abs(state.RailSpeed) >= MinimumGrindingSpeed && Math.Abs(state.RailSpeed) <= MaximumGroundSpeed &&
                state.Spin == 0 && state.Flip == 0 && (state.Position - Seat(state.ActiveRail, state.RailProgress)).LengthSquared <= 1e-8 &&
                (state.Velocity - state.ActiveRail.Direction * state.RailSpeed).LengthSquared <= 1e-8;
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static bool Vector(SkateVector value) { return Finite(value.X) && Finite(value.Y) && Finite(value.Z); }
        private static bool Coordinate(SkateVector value) { return Vector(value) && Math.Abs(value.X) <= 100000 && Math.Abs(value.Y) <= 100000 && Math.Abs(value.Z) <= 100000; }
        private static bool Axis(double value) { return Finite(value) && Math.Abs(value) <= 1; }
        private static double Yaw(double value) { var result = value % 360; return result < 0 ? result + 360 : result; }
        private static double Angle(double value) { var result = Yaw(value); return result > 180 ? result - 360 : result; }
        private static SkateVector Forward(double yaw) { var radians = yaw * Math.PI / 180; return new SkateVector(Math.Sin(radians), 0, Math.Cos(radians)); }
        private static SkateVector Unit(SkateVector value) { var length = value.Length; return length > 1e-12 ? value * (1 / length) : default(SkateVector); }
        private static SkateVector Project(SkateVector value, SkateVector normal) { return value - normal * SkateVector.Dot(value, normal); }
        private static bool Fail(string message, out string error) { error = message; return false; }
    }
}
