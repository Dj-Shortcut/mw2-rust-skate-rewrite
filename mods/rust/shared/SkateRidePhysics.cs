using System;

namespace Shortcut.RustMod
{
    public struct SkateSlopeInput
    {
        public readonly SkateVector SurfaceNormal, NoseForward;
        public readonly double SignedTangentSpeed, Gravity;
        public SkateSlopeInput(SkateVector surfaceNormal, SkateVector noseForward, double signedTangentSpeed)
            : this(surfaceNormal, noseForward, signedTangentSpeed, SkateRidePhysics.DefaultGravity) { }
        public SkateSlopeInput(SkateVector surfaceNormal, SkateVector noseForward, double signedTangentSpeed, double gravity)
        { SurfaceNormal = surfaceNormal; NoseForward = noseForward; SignedTangentSpeed = signedTangentSpeed; Gravity = gravity; }
    }

    public struct SkateSlopeResult
    {
        public readonly SkateVector Tangent, Velocity;
        public readonly double SignedTangentSpeed, HorizontalSpeed, SignedSlopeDegrees, GravityAcceleration;
        internal SkateSlopeResult(SkateVector tangent, SkateVector velocity, double speed, double horizontalSpeed,
                                  double slopeDegrees, double acceleration)
        { Tangent = tangent; Velocity = velocity; SignedTangentSpeed = speed; HorizontalSpeed = horizontalSpeed;
          SignedSlopeDegrees = slopeDegrees; GravityAcceleration = acceleration; }
    }

    public enum SkatePumpPhase { Idle = 0, Descending = 1, Bottom = 2, Spent = 3 }

    public struct SkatePumpInput
    {
        public readonly bool Supported, Crouching;
        public readonly double SignedSlopeDegrees, TangentSpeed;
        public readonly SkateVector TravelForward;
        public SkatePumpInput(bool supported, bool crouching, double signedSlopeDegrees, double tangentSpeed,
                              SkateVector travelForward)
        { Supported = supported; Crouching = crouching; SignedSlopeDegrees = signedSlopeDegrees;
          TangentSpeed = tangentSpeed; TravelForward = travelForward; }
    }

    public struct SkatePumpState
    {
        public readonly SkatePumpPhase Phase;
        public readonly bool PreviousCrouch, SawAscent;
        public readonly double ChargeSeconds, PeakDescentDegrees, BottomSeconds, AwardedEnergy;
        public readonly SkateVector PassForward;
        internal SkatePumpState(SkatePumpPhase phase, bool previousCrouch, bool sawAscent, double charge,
                                double peak, double bottom, double energy, SkateVector passForward)
        { Phase = phase; PreviousCrouch = previousCrouch; SawAscent = sawAscent; ChargeSeconds = charge;
          PeakDescentDegrees = peak; BottomSeconds = bottom; AwardedEnergy = energy; PassForward = passForward; }
    }

    public struct SkatePumpResult
    {
        public readonly SkatePumpState State;
        public readonly double EnergyGain, SpeedGain, TangentSpeed;
        public readonly bool Awarded, Reset;
        internal SkatePumpResult(SkatePumpState state, double energyGain, double speedGain, double speed,
                                 bool awarded, bool reset)
        { State = state; EnergyGain = energyGain; SpeedGain = speedGain; TangentSpeed = speed;
          Awarded = awarded; Reset = reset; }
    }

    public struct SkateLaunchInput
    {
        public readonly SkateVector SurfaceVelocity;
        public readonly double VerticalPop;
        public SkateLaunchInput(SkateVector surfaceVelocity, double verticalPop)
        { SurfaceVelocity = surfaceVelocity; VerticalPop = verticalPop; }
    }

    public struct SkateLaunchResult
    {
        public readonly SkateVector Velocity;
        internal SkateLaunchResult(SkateVector velocity) { Velocity = velocity; }
    }

    public struct SkateLandingInput
    {
        public readonly SkateVector IncomingVelocity, SurfaceNormal, BoardNoseForward;
        public readonly double MaximumImpact, FallToRoll;
        public SkateLandingInput(SkateVector incomingVelocity, SkateVector surfaceNormal, SkateVector boardNoseForward)
            : this(incomingVelocity, surfaceNormal, boardNoseForward,
                   SkateRidePhysics.DefaultMaximumImpact, SkateRidePhysics.DefaultFallToRoll) { }
        public SkateLandingInput(SkateVector incomingVelocity, SkateVector surfaceNormal, SkateVector boardNoseForward,
                                 double maximumImpact, double fallToRoll)
        { IncomingVelocity = incomingVelocity; SurfaceNormal = surfaceNormal; BoardNoseForward = boardNoseForward;
          MaximumImpact = maximumImpact; FallToRoll = fallToRoll; }
    }

    public struct SkateLandingResult
    {
        public readonly SkateVector RollVelocity;
        public readonly double SignedTangentSpeed, HorizontalSpeed, NormalImpact, LateralSlipSpeed;
        public readonly bool Bailed;
        internal SkateLandingResult(SkateVector velocity, double speed, double horizontalSpeed,
                                    double impact, double lateralSlipSpeed, bool bailed)
        { RollVelocity = velocity; SignedTangentSpeed = speed; HorizontalSpeed = horizontalSpeed;
          NormalImpact = impact; LateralSlipSpeed = lateralSlipSpeed; Bailed = bailed; }
    }

    public static class SkateRidePhysics
    {
        public const double MaximumStep = 0.05;
        public const double MaximumValue = 100000;
        public const double NormalLengthSquaredTolerance = 0.0001;
        public const double DefaultGravity = 16;
        public const double DefaultVerticalPop = 6;
        public const double DefaultMaximumImpact = 12;
        public const double DefaultFallToRoll = 0.8;
        public const double PumpChargeSeconds = 0.4;
        public const double PumpStartDegrees = 8;
        public const double PumpReleaseDegrees = 4;
        public const double PumpFullDegrees = 30;
        public const double MinimumPumpSpeed = 0.5;
        public const double MaximumPumpEnergy = 12;
        public const double PumpBottomSeconds = 1.5;
        private const double HorizontalTolerance = 1e-8;
        private const double DerivedTolerance = 1e-12;

        public static bool TryBoundHorizontal(float x, float z, float velocityX, float velocityZ,
                                              float dt, double maximumSpeed, out float boundedX, out float boundedZ)
        {
            boundedX = velocityX; boundedZ = velocityZ;
            if (!Scalar(x) || !Scalar(z) || !Finite(velocityX) || !Finite(velocityZ) ||
                !Finite(dt) || dt <= 0 || dt > (float)MaximumStep ||
                !Finite(maximumSpeed) || maximumSpeed <= 0 || maximumSpeed > SkateMotion.MaximumGroundSpeed) return false;
            double speed = Math.Sqrt((double)velocityX * velocityX + (double)velocityZ * velocityZ);
            if (HorizontalFits(x, z, velocityX, velocityZ, dt, maximumSpeed)) return true;
            double low = 0, high = Math.Min(1, maximumSpeed / speed);
            boundedX = boundedZ = 0;
            for (int i = 0; i < 32; i++)
            {
                double scale = (low + high) * 0.5;
                float candidateX = (float)(velocityX * scale), candidateZ = (float)(velocityZ * scale);
                if (HorizontalFits(x, z, candidateX, candidateZ, dt, maximumSpeed))
                { low = scale; boundedX = candidateX; boundedZ = candidateZ; }
                else high = scale;
            }
            return true;
        }

        private static bool HorizontalFits(float x, float z, float velocityX, float velocityZ, float dt, double maximumSpeed)
        {
            if (Math.Sqrt((double)velocityX * velocityX + (double)velocityZ * velocityZ) > maximumSpeed) return false;
            // Keep the two float32 roundings: the server measures rounded positions in double.
            float stepX = (float)(velocityX * dt), stepZ = (float)(velocityZ * dt);
            float nextX = (float)(x + stepX), nextZ = (float)(z + stepZ);
            if (!Scalar(nextX) || !Scalar(nextZ)) return false;
            double deltaX = (double)nextX - x, deltaZ = (double)nextZ - z;
            return Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ) <= maximumSpeed * dt;
        }

        public static bool TrySlope(SkateSlopeInput input, double dt, out SkateSlopeResult result, out string error)
        {
            result = default(SkateSlopeResult); error = null;
            SkateVector normal, tangent;
            if (!Step(dt) || !Scalar(input.SignedTangentSpeed) || !Positive(input.Gravity) ||
                !Frame(input.SurfaceNormal, input.NoseForward, out normal, out tangent))
            { error = "Invalid skate slope input."; return false; }
            double acceleration = -input.Gravity * tangent.Y;
            double speed = input.SignedTangentSpeed + acceleration * dt;
            if (!Scalar(speed)) { error = "Skate slope result is out of bounds."; return false; }
            SkateVector velocity = tangent * speed;
            double horizontal = HorizontalLength(tangent);
            double angle = speed == 0 ? 0 : Math.Atan2(tangent.Y, horizontal) * 180 / Math.PI * Math.Sign(speed);
            if (!DerivedVector(velocity) || !Finite(angle) || !Finite(acceleration))
            { error = "Skate slope result is out of bounds."; return false; }
            result = new SkateSlopeResult(tangent, velocity, speed, Math.Abs(speed) * horizontal, angle, acceleration);
            return true;
        }

        public static bool TryPump(SkatePumpState state, SkatePumpInput input, double dt,
                                   out SkatePumpResult result, out string error)
        {
            result = new SkatePumpResult(state, 0, 0, 0, false, false); error = null;
            if (!PumpState(state) || !Step(dt) || !Finite(input.SignedSlopeDegrees) ||
                Math.Abs(input.SignedSlopeDegrees) >= 90 || !Nonnegative(input.TangentSpeed) ||
                !Vector(input.TravelForward) || Math.Abs(input.TravelForward.Y) > HorizontalTolerance)
            { error = "Invalid skate pump input."; return false; }
            if (!input.Supported || input.TangentSpeed < MinimumPumpSpeed)
            {
                result = new SkatePumpResult(Idle(input.Crouching), 0, 0, input.TangentSpeed, false,
                                             state.Phase != SkatePumpPhase.Idle);
                return true;
            }
            SkateVector travel;
            if (!Heading(input.TravelForward, out travel)) { error = "Invalid skate pump input."; return false; }
            bool reset = state.Phase != SkatePumpPhase.Idle && SkateVector.Dot(state.PassForward, travel) <= DerivedTolerance;
            SkatePumpState working = reset ? Idle(input.Crouching) : state;
            SkatePumpPhase phase = working.Phase;
            bool sawAscent = working.SawAscent;
            double charge = working.ChargeSeconds, peak = working.PeakDescentDegrees;
            double bottom = working.BottomSeconds, awardedEnergy = working.AwardedEnergy;
            SkateVector pass = working.PassForward;
            if (phase == SkatePumpPhase.Spent && input.SignedSlopeDegrees >= PumpStartDegrees) sawAscent = true;
            if ((phase == SkatePumpPhase.Idle || (phase == SkatePumpPhase.Spent && sawAscent)) &&
                input.SignedSlopeDegrees <= -PumpStartDegrees)
            {
                phase = SkatePumpPhase.Descending; sawAscent = false; charge = bottom = awardedEnergy = 0;
                peak = -input.SignedSlopeDegrees; pass = travel;
            }
            bool release = working.PreviousCrouch && !input.Crouching;
            if (phase == SkatePumpPhase.Descending)
            {
                if (input.SignedSlopeDegrees <= -PumpStartDegrees)
                {
                    peak = Math.Max(peak, -input.SignedSlopeDegrees);
                    if (input.Crouching) charge = Math.Min(PumpChargeSeconds, charge + dt);
                }
                if (input.SignedSlopeDegrees >= -PumpReleaseDegrees) { phase = SkatePumpPhase.Bottom; bottom = 0; }
                else if (release) charge = 0;
            }
            double energyGain = 0, speedGain = 0, speed = input.TangentSpeed;
            if (phase == SkatePumpPhase.Bottom)
            {
                bottom += dt;
                if (input.SignedSlopeDegrees > PumpStartDegrees || bottom >= PumpBottomSeconds)
                {
                    phase = SkatePumpPhase.Spent; charge = bottom = 0;
                    sawAscent = input.SignedSlopeDegrees >= PumpStartDegrees;
                }
                else if (release)
                {
                    double slopeWeight = Math.Max(0, Math.Min(1, (peak - PumpStartDegrees) / (PumpFullDegrees - PumpStartDegrees)));
                    energyGain = Math.Min(MaximumPumpEnergy - awardedEnergy,
                        MaximumPumpEnergy * (charge / PumpChargeSeconds) * slopeWeight);
                    if (energyGain > 0)
                    {
                        speed = Math.Sqrt(speed * speed + 2 * energyGain);
                        if (!Nonnegative(speed)) { error = "Skate pump result is out of bounds."; return false; }
                        speedGain = speed - input.TangentSpeed; awardedEnergy += energyGain;
                        phase = SkatePumpPhase.Spent; bottom = 0;
                        sawAscent = input.SignedSlopeDegrees >= PumpStartDegrees;
                    }
                    charge = 0;
                }
            }
            var next = new SkatePumpState(phase, input.Crouching, sawAscent, charge, peak, bottom, awardedEnergy, pass);
            if (!PumpState(next) || !Finite(energyGain) || !Finite(speedGain))
            { error = "Skate pump result is out of bounds."; return false; }
            result = new SkatePumpResult(next, energyGain, speedGain, speed, energyGain > 0, reset);
            return true;
        }

        public static bool TryLaunch(SkateLaunchInput input, out SkateLaunchResult result, out string error)
        {
            result = default(SkateLaunchResult); error = null;
            if (!Vector(input.SurfaceVelocity) || !Nonnegative(input.VerticalPop))
            { error = "Invalid skate launch input."; return false; }
            var velocity = new SkateVector(input.SurfaceVelocity.X, input.SurfaceVelocity.Y + input.VerticalPop, input.SurfaceVelocity.Z);
            if (!Vector(velocity)) { error = "Skate launch result is out of bounds."; return false; }
            result = new SkateLaunchResult(velocity); return true;
        }

        public static bool TryLand(SkateLandingInput input, out SkateLandingResult result, out string error)
        {
            result = default(SkateLandingResult); error = null;
            SkateVector normal, tangent;
            if (!Vector(input.IncomingVelocity) || !Positive(input.MaximumImpact) || !Finite(input.FallToRoll) ||
                input.FallToRoll < 0 || input.FallToRoll > 1 ||
                !Frame(input.SurfaceNormal, input.BoardNoseForward, out normal, out tangent))
            { error = "Invalid skate landing input."; return false; }
            double normalSpeed = SkateVector.Dot(input.IncomingVelocity, normal);
            double impact = Math.Max(0, -normalSpeed);
            SkateVector projected = input.IncomingVelocity - normal * normalSpeed;
            double planeSquared = projected.LengthSquared;
            if (impact > 0 && input.FallToRoll < 1 && planeSquared > 0)
            {
                double withheldFall = (1 - input.FallToRoll) * impact / normal.Y;
                double retained = Math.Max(0, Math.Min(1, 1 + withheldFall * projected.Y / planeSquared));
                projected = projected * retained;
            }
            double speed = SkateVector.Dot(projected, tangent);
            SkateVector roll = tangent * speed;
            double lateral = (projected - roll).Length;
            double horizontal = HorizontalLength(roll);
            if (!DerivedVector(roll) || !Finite(speed) || !Finite(impact) || !Finite(lateral) || !Finite(horizontal))
            { error = "Skate landing result is out of bounds."; return false; }
            result = new SkateLandingResult(roll, speed, horizontal, impact, lateral, impact > input.MaximumImpact);
            return true;
        }

        private static bool PumpState(SkatePumpState state)
        {
            if (!Finite(state.ChargeSeconds) || state.ChargeSeconds < 0 || state.ChargeSeconds > PumpChargeSeconds ||
                !Finite(state.PeakDescentDegrees) || !Finite(state.BottomSeconds) || state.BottomSeconds < 0 ||
                !Finite(state.AwardedEnergy) || state.AwardedEnergy < 0 || state.AwardedEnergy > MaximumPumpEnergy ||
                !Vector(state.PassForward)) return false;
            if (state.Phase == SkatePumpPhase.Idle)
                return !state.SawAscent && state.ChargeSeconds == 0 && state.PeakDescentDegrees == 0 &&
                       state.BottomSeconds == 0 && state.AwardedEnergy == 0 && state.PassForward.LengthSquared == 0;
            if (state.PeakDescentDegrees < PumpStartDegrees || state.PeakDescentDegrees >= 90 ||
                Math.Abs(state.PassForward.Y) > HorizontalTolerance ||
                Math.Abs(state.PassForward.LengthSquared - 1) > NormalLengthSquaredTolerance) return false;
            if (state.Phase == SkatePumpPhase.Descending)
                return !state.SawAscent && state.BottomSeconds == 0 && state.AwardedEnergy == 0;
            if (state.Phase == SkatePumpPhase.Bottom)
                return !state.SawAscent && state.BottomSeconds < PumpBottomSeconds && state.AwardedEnergy == 0;
            return state.Phase == SkatePumpPhase.Spent && state.ChargeSeconds == 0 && state.BottomSeconds == 0;
        }

        private static SkatePumpState Idle(bool crouching)
        { return new SkatePumpState(SkatePumpPhase.Idle, crouching, false, 0, 0, 0, 0, default(SkateVector)); }

        private static bool Frame(SkateVector surfaceNormal, SkateVector nose, out SkateVector normal, out SkateVector tangent)
        {
            normal = tangent = default(SkateVector);
            if (!Vector(surfaceNormal) || surfaceNormal.Y <= 0.3 ||
                Math.Abs(surfaceNormal.LengthSquared - 1) > NormalLengthSquaredTolerance) return false;
            SkateVector heading;
            if (!Heading(nose, out heading)) return false;
            normal = surfaceNormal * (1 / surfaceNormal.Length);
            if (normal.Y <= 0.3) return false;
            tangent = new SkateVector(heading.X, -(heading.X * normal.X + heading.Z * normal.Z) / normal.Y, heading.Z);
            tangent = tangent * (1 / tangent.Length);
            return true;
        }

        private static bool Heading(SkateVector value, out SkateVector heading)
        {
            heading = default(SkateVector);
            if (!Vector(value) || Math.Abs(value.Y) > HorizontalTolerance) return false;
            double length = HorizontalLength(value);
            if (length <= 1e-12) return false;
            heading = new SkateVector(value.X / length, 0, value.Z / length); return true;
        }

        private static double HorizontalLength(SkateVector value) { return Math.Sqrt(value.X * value.X + value.Z * value.Z); }
        private static bool Step(double value) { return Finite(value) && value > 0 && value <= MaximumStep; }
        private static bool Scalar(double value) { return Finite(value) && Math.Abs(value) <= MaximumValue; }
        private static bool Positive(double value) { return Scalar(value) && value > 0; }
        private static bool Nonnegative(double value) { return Scalar(value) && value >= 0; }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static bool Vector(SkateVector value)
        { return Finite(value.X) && Finite(value.Y) && Finite(value.Z) && value.LengthSquared <= MaximumValue * MaximumValue; }
        private static bool DerivedVector(SkateVector value)
        { return Finite(value.X) && Finite(value.Y) && Finite(value.Z) &&
                 value.LengthSquared <= MaximumValue * MaximumValue * (1 + DerivedTolerance); }
    }
}
