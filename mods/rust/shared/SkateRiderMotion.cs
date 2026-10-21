using System;

namespace Shortcut.RustMod
{
    public readonly struct SkateRiderMotionOptions
    {
        public readonly bool DisablePelvisSpring, PinLeftHand, PinRightHand;

        public SkateRiderMotionOptions(bool disablePelvisSpring, bool pinLeftHand, bool pinRightHand)
        { DisablePelvisSpring = disablePelvisSpring; PinLeftHand = pinLeftHand; PinRightHand = pinRightHand; }
    }

    public readonly struct SkateRiderMotionState
    {
        public readonly bool Initialized;
        public readonly SkateRiderRig Rig;
        public readonly SkateRiderPose Target, Shown;
        public readonly SkateVector PreviousPelvis, PreviousChest, PreviousLeftShoulder, PreviousRightShoulder;
        public readonly SkateVector PreviousLeftHand, PreviousRightHand;
        public readonly double PreviousDelta;

        internal SkateRiderMotionState(SkateRiderRig rig, SkateRiderPose target, SkateRiderPose shown,
                                       SkateVector previousPelvis, SkateVector previousChest,
                                       SkateVector previousLeftShoulder, SkateVector previousRightShoulder,
                                       SkateVector previousLeftHand, SkateVector previousRightHand, double previousDelta)
        {
            Initialized = true; Rig = rig; Target = target; Shown = shown;
            PreviousPelvis = previousPelvis; PreviousChest = previousChest;
            PreviousLeftShoulder = previousLeftShoulder; PreviousRightShoulder = previousRightShoulder;
            PreviousLeftHand = previousLeftHand; PreviousRightHand = previousRightHand;
            PreviousDelta = previousDelta;
        }
    }

    public readonly struct SkateRiderAirFrameState
    {
        public readonly bool Initialized;
        public readonly SkateVector Up, LandingUp;
        public readonly double LandingShare;

        internal SkateRiderAirFrameState(SkateVector up, SkateVector landingUp, double landingShare)
        { Initialized = true; Up = up; LandingUp = landingUp; LandingShare = landingShare; }
    }

    public static class SkateRiderMotion
    {
        public const double MinimumDelta = 0.0001;
        public const double MaximumDelta = 0.05;
        public const double MaximumOffset = 0.12;
        public const double MaximumSecondarySpeed = 6;
        public const double ResetDistance = 2;
        public const double ResetFrameCosine = -0.5;
        public const double LengthTolerance = 0.000001;
        private const double FrameTolerance = 0.000001;
        private const double Substep = 1.0 / 120;

        // Display only: leave the support frame continuously, level near the flight apex,
        // then meet the sensed landing plane. Neither catch state nor stance snaps this frame.
        public const double AirReleaseMinimum = 0.18, AirLandingLead = 0.25, AirFrameRadiansPerSecond = Math.PI * 2;
        public static bool TryAirFrame(SkateRiderAirFrameState state, double dt,
                                       SkateVector takeoffUp, double sinceRelease, double riseAtRelease, double gravity,
                                       SkateVector landingUp, double gapToContact, double closingSpeed,
                                       out SkateVector up, out SkateRiderAirFrameState next, out string error)
        {
            up = takeoffUp; next = state; error = null;
            if (!Finite(dt) || dt < MinimumDelta || dt > MaximumDelta ||
                !Normal(takeoffUp) || takeoffUp.Y <= 0 || !Normal(landingUp) || landingUp.Y <= 0 ||
                !Finite(sinceRelease) || sinceRelease < 0 || !Finite(riseAtRelease) || riseAtRelease < 0 ||
                !Finite(gravity) || gravity <= 0 || !Finite(gapToContact) || !Finite(closingSpeed) || closingSpeed < 0 ||
                (state.Initialized && (!Normal(state.Up) || state.Up.Y <= 0 || !Normal(state.LandingUp) || state.LandingUp.Y <= 0 ||
                    !Finite(state.LandingShare) || state.LandingShare < 0 || state.LandingShare > 1)))
            { error = "Invalid rider air display frame."; return false; }
            var duration = Math.Max(AirReleaseMinimum, riseAtRelease / gravity);
            var release = Math.Min(1, sinceRelease / duration);
            release *= release * (3 - 2 * release);
            var released = Unit(takeoffUp * (1 - release) + new SkateVector(0, 1, 0) * release);
            var land = closingSpeed > 0.000001 ? Math.Max(0, Math.Min(1,
                1 - Math.Max(0, gapToContact) / (closingSpeed * AirLandingLead))) : 0;
            land *= land * (3 - 2 * land);
            // A missed probe must not undo an already observed landing approach.
            var held = state.Initialized ? state.LandingShare : 0;
            var plane = state.Initialized ? state.LandingUp : landingUp;
            if (land > 0) plane = held > 0 ? AirTurn(plane, landingUp, dt) : landingUp;
            land = Math.Max(held, land);
            var target = Unit(released * (1 - land) + plane * land);
            up = AirTurn(state.Initialized ? state.Up : takeoffUp, target, dt);
            if (!Normal(up) || up.Y <= 0) { up = takeoffUp; error = "Rider air display frame left the upper hemisphere."; return false; }
            next = new SkateRiderAirFrameState(up, plane, land);
            return true;
        }

        private static SkateVector AirTurn(SkateVector from, SkateVector to, double dt)
        {
            var cosine = Math.Max(-1, Math.Min(1, SkateVector.Dot(from, to)));
            var angle = Math.Acos(cosine); var step = AirFrameRadiansPerSecond * dt;
            if (angle <= step) return to;
            return Unit(from * Math.Cos(step) + Unit(to - from * cosine) * Math.Sin(step));
        }

        public static bool TryStep(SkateRiderMotionState state, SkateRiderRig rig, SkateRiderPose target, double dt,
                                   out SkateRiderPose shown, out SkateRiderMotionState next, out string error)
        { return TryStep(state, rig, target, dt, default(SkateRiderMotionOptions), out shown, out next, out error); }

        public static bool TryStep(SkateRiderMotionState state, SkateRiderRig rig, SkateRiderPose target, double dt,
                                   SkateRiderMotionOptions options, out SkateRiderPose shown,
                                   out SkateRiderMotionState next, out string error)
        {
            shown = default(SkateRiderPose); next = state; error = null;
            if (!Finite(dt) || dt < MinimumDelta || dt > MaximumDelta || !Valid(rig) ||
                !Valid(target, rig) || !Valid(state))
            { error = "Invalid skate rider motion request."; return false; }
            if (!state.Initialized || !Same(state.Rig, rig) ||
                (Anchor(target) - Anchor(state.Target)).Length > ResetDistance ||
                SkateVector.Dot(target.PelvisForward, state.Target.PelvisForward) < ResetFrameCosine ||
                SkateVector.Dot(target.PelvisUp, state.Target.PelvisUp) < ResetFrameCosine)
            {
                shown = target;
                next = new SkateRiderMotionState(rig, target, target, target.Pelvis, target.Chest,
                    target.LeftShoulder, target.RightShoulder, target.LeftHand, target.RightHand, dt);
                return true;
            }

            var up = Unit(target.PelvisUp);
            double pelvis = SkateVector.Dot(Transport(state.Shown.Pelvis, state.Target, target) - target.Pelvis, up);
            double previousPelvis = SkateVector.Dot(Transport(state.PreviousPelvis, state.Target, target) - target.Pelvis, up);
            var chest = Transport(state.Shown.Chest, state.Target, target);
            var previousChest = Transport(state.PreviousChest, state.Target, target);
            var leftShoulder = Transport(state.Shown.LeftShoulder, state.Target, target);
            var previousLeftShoulder = Transport(state.PreviousLeftShoulder, state.Target, target);
            var rightShoulder = Transport(state.Shown.RightShoulder, state.Target, target);
            var previousRightShoulder = Transport(state.PreviousRightShoulder, state.Target, target);
            var left = Transport(state.Shown.LeftHand, state.Target, target);
            var previousLeft = Transport(state.PreviousLeftHand, state.Target, target);
            var right = Transport(state.Shown.RightHand, state.Target, target);
            var previousRight = Transport(state.PreviousRightHand, state.Target, target);
            if (options.DisablePelvisSpring) { pelvis = 0; previousPelvis = 0; }
            if (options.PinLeftHand) { left = target.LeftHand; previousLeft = left; }
            if (options.PinRightHand) { right = target.RightHand; previousRight = right; }
            int count = (int)Math.Ceiling(dt / Substep);
            double step = dt / count, oldStep = state.PreviousDelta;
            for (int i = 0; i < count; i++)
            {
                if (!options.DisablePelvisSpring) Advance(ref pelvis, ref previousPelvis, step, oldStep);
                Advance(ref chest, ref previousChest, target.Chest, 144, 9, step, oldStep);
                Advance(ref leftShoulder, ref previousLeftShoulder, target.LeftShoulder, 144, 9, step, oldStep);
                Advance(ref rightShoulder, ref previousRightShoulder, target.RightShoulder, 144, 9, step, oldStep);
                if (!options.PinLeftHand) Advance(ref left, ref previousLeft, target.LeftHand, 100, 7, step, oldStep);
                if (!options.PinRightHand) Advance(ref right, ref previousRight, target.RightHand, 100, 7, step, oldStep);
                oldStep = step;
            }

            double factor = 1;
            bool found = false;
            for (int i = 0; i < 12; i++)
            {
                if (Build(rig, target, pelvis * factor, chest, leftShoulder, rightShoulder, left, right, factor, options, out shown) &&
                    Bounded(shown, target) && Valid(shown, rig))
                { found = true; break; }
                factor *= 0.5;
            }
            if (!found)
            { shown = target; factor = 0; }
            if (factor < 1)
            {
                next = new SkateRiderMotionState(rig, target, shown, shown.Pelvis, shown.Chest,
                    shown.LeftShoulder, shown.RightShoulder, shown.LeftHand, shown.RightHand, step);
            }
            else
            {
                var rawPelvis = target.Pelvis + up * pelvis;
                next = new SkateRiderMotionState(rig, target, shown,
                    History(shown.Pelvis, rawPelvis - (target.Pelvis + up * previousPelvis), step),
                    History(shown.Chest, chest - previousChest, step),
                    History(shown.LeftShoulder, leftShoulder - previousLeftShoulder, step),
                    History(shown.RightShoulder, rightShoulder - previousRightShoulder, step),
                    History(shown.LeftHand, left - previousLeft, step),
                    History(shown.RightHand, right - previousRight, step), step);
            }
            return true;
        }

        private static void Advance(ref double value, ref double previous, double dt, double oldDt)
        {
            double movement = (value - previous) * (dt / oldDt) * Math.Exp(-12 * dt) - value * 196 * dt * dt;
            movement = Math.Max(-MaximumSecondarySpeed * dt, Math.Min(MaximumSecondarySpeed * dt, movement));
            previous = value; value += movement;
        }

        private static void Advance(ref SkateVector value, ref SkateVector previous, SkateVector target,
                                    double spring, double damping, double dt, double oldDt)
        {
            var movement = (value - previous) * ((dt / oldDt) * Math.Exp(-damping * dt)) +
                (target - value) * (spring * dt * dt);
            previous = value; value += Limit(movement, MaximumSecondarySpeed * dt);
        }

        private static SkateVector History(SkateVector shown, SkateVector movement, double dt)
        {
            var previous = shown - Limit(movement, MaximumSecondarySpeed * dt);
            return Coordinate(previous) ? previous : shown;
        }

        private static bool Build(SkateRiderRig rig, SkateRiderPose target, double height,
                                  SkateVector chestTarget, SkateVector leftShoulderTarget, SkateVector rightShoulderTarget,
                                  SkateVector leftTarget, SkateVector rightTarget,
                                  double factor, SkateRiderMotionOptions options, out SkateRiderPose pose)
        {
            pose = default(SkateRiderPose);
            var pelvis = target.Pelvis + Unit(target.PelvisUp) * height;
            var leftHip = target.LeftHip + (pelvis - target.Pelvis);
            var rightHip = target.RightHip + (pelvis - target.Pelvis);
            var leftShoulderGoal = target.LeftShoulder + (leftShoulderTarget - target.LeftShoulder) * factor;
            var rightShoulderGoal = target.RightShoulder + (rightShoulderTarget - target.RightShoulder) * factor;
            var centre = (chestTarget + (leftShoulderTarget + rightShoulderTarget) * 0.5) * 0.5;
            var chestUp = Unit(target.Chest + (centre - target.Chest) * factor - pelvis);
            var chestRight = Unit(Project(rightShoulderGoal - leftShoulderGoal, chestUp));
            if (chestUp.LengthSquared < 0.5 || chestRight.LengthSquared < 0.5) return false;
            var chestForward = Cross(chestRight, chestUp);
            var chest = pelvis + chestUp * rig.SpineLength;
            var leftShoulder = chest - chestRight * (rig.ShoulderWidth * 0.5);
            var rightShoulder = chest + chestRight * (rig.ShoulderWidth * 0.5);
            var leftHand = options.PinLeftHand ? target.LeftHand : Reach(leftShoulder, target.LeftHand + (leftTarget - target.LeftHand) * factor,
                target.LeftHand - target.LeftShoulder, rig.UpperArmLength, rig.ForearmLength);
            var rightHand = options.PinRightHand ? target.RightHand : Reach(rightShoulder, target.RightHand + (rightTarget - target.RightHand) * factor,
                target.RightHand - target.RightShoulder, rig.UpperArmLength, rig.ForearmLength);
            SkateVector leftKnee, rightKnee, leftElbow, rightElbow;
            if (!Limb(leftHip, target.LeftAnkle, rig.ThighLength, rig.ShinLength,
                      target.LeftKnee - target.LeftHip, target.PelvisForward, out leftKnee) ||
                !Limb(rightHip, target.RightAnkle, rig.ThighLength, rig.ShinLength,
                      target.RightKnee - target.RightHip, target.PelvisForward, out rightKnee) ||
                !Limb(leftShoulder, leftHand, rig.UpperArmLength, rig.ForearmLength,
                      target.LeftElbow - target.LeftShoulder, target.ChestForward * -1, out leftElbow) ||
                !Limb(rightShoulder, rightHand, rig.UpperArmLength, rig.ForearmLength,
                      target.RightElbow - target.RightShoulder, target.ChestForward * -1, out rightElbow)) return false;
            var neck = chest + chestUp * (rig.SpineLength * 0.12);
            pose = new SkateRiderPose(pelvis, leftHip, rightHip, leftKnee, rightKnee,
                target.LeftAnkle, target.RightAnkle, target.LeftToe, target.RightToe, chest, neck,
                neck + chestUp * rig.NeckToHeadLength, leftShoulder, rightShoulder, leftElbow, rightElbow,
                leftHand, rightHand, target.PelvisForward, target.PelvisUp, chestForward, chestUp, target.HeadForward);
            return true;
        }

        private static SkateVector Reach(SkateVector root, SkateVector target, SkateVector fallback, double first, double second)
        {
            var offset = target - root;
            double distance = offset.Length;
            double reach = Math.Max(Math.Abs(first - second), Math.Min(first + second, distance));
            var direction = distance > 1e-12 ? offset * (1 / distance) : Unit(fallback);
            if (direction.LengthSquared < 0.5) direction = new SkateVector(1, 0, 0);
            return root + direction * reach;
        }

        private static bool Limb(SkateVector root, SkateVector end, double first, double second,
                                 SkateVector bend, SkateVector fallback, out SkateVector joint)
        {
            joint = default(SkateVector);
            var offset = end - root;
            double distance = offset.Length;
            if (distance <= 1e-12)
            {
                if (first != second) return false;
                var folded = Unit(bend);
                if (folded.LengthSquared < 0.5) folded = Unit(fallback);
                if (folded.LengthSquared < 0.5) return false;
                joint = root + folded * first;
                return true;
            }
            if (distance > first + second + 1e-10 || distance < Math.Abs(first - second) - 1e-10) return false;
            var direction = offset * (1 / distance);
            double along = (first * first - second * second + distance * distance) / (2 * distance);
            double radius = Math.Sqrt(Math.Max(0, first * first - along * along));
            var pole = Unit(Project(bend, direction));
            if (pole.LengthSquared < 0.5) pole = Unit(Project(fallback, direction));
            if (pole.LengthSquared < 0.5) pole = Unit(Cross(direction,
                Math.Abs(direction.X) < 0.75 ? new SkateVector(1, 0, 0) : new SkateVector(0, 1, 0)));
            joint = root + direction * along + pole * radius;
            return true;
        }

        private static SkateVector Transport(SkateVector point, SkateRiderPose from, SkateRiderPose to)
        {
            var offset = point - Anchor(from);
            var fromUp = Unit(from.PelvisUp); var fromForward = Unit(Project(from.PelvisForward, fromUp));
            var toUp = Unit(to.PelvisUp); var toForward = Unit(Project(to.PelvisForward, toUp));
            return Anchor(to) + Cross(toUp, toForward) * SkateVector.Dot(offset, Cross(fromUp, fromForward)) +
                toUp * SkateVector.Dot(offset, fromUp) + toForward * SkateVector.Dot(offset, fromForward);
        }

        private static bool Valid(SkateRiderMotionState state)
        {
            if (!state.Initialized)
                return Zero(state.Rig) && Zero(state.Target) && Zero(state.Shown) &&
                    Zero(state.PreviousPelvis) && Zero(state.PreviousChest) && Zero(state.PreviousLeftHand) &&
                    Zero(state.PreviousRightHand) && Zero(state.PreviousLeftShoulder) && Zero(state.PreviousRightShoulder) &&
                    state.PreviousDelta == 0;
            return Valid(state.Rig) && Valid(state.Target, state.Rig) && Valid(state.Shown, state.Rig) &&
                Bounded(state.Shown, state.Target) && Finite(state.PreviousDelta) &&
                state.PreviousDelta >= MinimumDelta && state.PreviousDelta <= MaximumDelta &&
                ValidHistory(state.PreviousPelvis, state.Shown.Pelvis, state.PreviousDelta) &&
                ValidHistory(state.PreviousChest, state.Shown.Chest, state.PreviousDelta) &&
                ValidHistory(state.PreviousLeftShoulder, state.Shown.LeftShoulder, state.PreviousDelta) &&
                ValidHistory(state.PreviousRightShoulder, state.Shown.RightShoulder, state.PreviousDelta) &&
                ValidHistory(state.PreviousLeftHand, state.Shown.LeftHand, state.PreviousDelta) &&
                ValidHistory(state.PreviousRightHand, state.Shown.RightHand, state.PreviousDelta);
        }

        private static bool ValidHistory(SkateVector previous, SkateVector current, double dt)
        { return Coordinate(previous) && (previous - current).Length <= MaximumSecondarySpeed * dt + LengthTolerance; }

        private static bool Valid(SkateRiderPose p, SkateRiderRig r)
        {
            return Coordinate(p.Pelvis) && Coordinate(p.LeftHip) && Coordinate(p.RightHip) && Coordinate(p.LeftKnee) &&
                Coordinate(p.RightKnee) && Coordinate(p.LeftAnkle) && Coordinate(p.RightAnkle) && Coordinate(p.LeftToe) &&
                Coordinate(p.RightToe) && Coordinate(p.Chest) && Coordinate(p.Neck) && Coordinate(p.Head) &&
                Coordinate(p.LeftShoulder) && Coordinate(p.RightShoulder) && Coordinate(p.LeftElbow) && Coordinate(p.RightElbow) &&
                Coordinate(p.LeftHand) && Coordinate(p.RightHand) && Normal(p.PelvisUp) && Normal(p.PelvisForward) &&
                Normal(p.ChestUp) && Normal(p.ChestForward) && Normal(p.HeadForward) &&
                Math.Abs(SkateVector.Dot(p.PelvisUp, p.PelvisForward)) <= FrameTolerance &&
                Math.Abs(SkateVector.Dot(p.ChestUp, p.ChestForward)) <= FrameTolerance &&
                Near(p.LeftHip, p.Pelvis - Cross(p.PelvisUp, p.PelvisForward) * (r.HipWidth * 0.5)) &&
                Near(p.RightHip, p.Pelvis + Cross(p.PelvisUp, p.PelvisForward) * (r.HipWidth * 0.5)) &&
                Near(p.Chest, p.Pelvis + p.ChestUp * r.SpineLength) &&
                Near(p.Neck, p.Chest + p.ChestUp * (r.SpineLength * 0.12)) &&
                Near(p.Head, p.Neck + p.ChestUp * r.NeckToHeadLength) &&
                Near(p.LeftShoulder, p.Chest - Cross(p.ChestUp, p.ChestForward) * (r.ShoulderWidth * 0.5)) &&
                Near(p.RightShoulder, p.Chest + Cross(p.ChestUp, p.ChestForward) * (r.ShoulderWidth * 0.5)) &&
                Length(p.LeftHip, p.RightHip, r.HipWidth) && Length(p.Pelvis, p.Chest, r.SpineLength) &&
                Length(p.Chest, p.Neck, r.SpineLength * 0.12) && Length(p.Neck, p.Head, r.NeckToHeadLength) &&
                Length(p.LeftShoulder, p.RightShoulder, r.ShoulderWidth) &&
                Length(p.LeftHip, p.LeftKnee, r.ThighLength) && Length(p.RightHip, p.RightKnee, r.ThighLength) &&
                Length(p.LeftKnee, p.LeftAnkle, r.ShinLength) && Length(p.RightKnee, p.RightAnkle, r.ShinLength) &&
                Length(p.LeftShoulder, p.LeftElbow, r.UpperArmLength) && Length(p.RightShoulder, p.RightElbow, r.UpperArmLength) &&
                Length(p.LeftElbow, p.LeftHand, r.ForearmLength) && Length(p.RightElbow, p.RightHand, r.ForearmLength) &&
                Length(p.LeftAnkle, p.LeftToe, r.FootLength) && Length(p.RightAnkle, p.RightToe, r.FootLength);
        }

        private static bool Bounded(SkateRiderPose a, SkateRiderPose b)
        {
            return Offset(a.Pelvis, b.Pelvis) && Offset(a.LeftHip, b.LeftHip) && Offset(a.RightHip, b.RightHip) &&
                Offset(a.LeftKnee, b.LeftKnee) && Offset(a.RightKnee, b.RightKnee) && Offset(a.LeftAnkle, b.LeftAnkle) &&
                Offset(a.RightAnkle, b.RightAnkle) && Offset(a.LeftToe, b.LeftToe) && Offset(a.RightToe, b.RightToe) &&
                Offset(a.Chest, b.Chest) && Offset(a.Neck, b.Neck) && Offset(a.Head, b.Head) &&
                Offset(a.LeftShoulder, b.LeftShoulder) && Offset(a.RightShoulder, b.RightShoulder) &&
                Offset(a.LeftElbow, b.LeftElbow) && Offset(a.RightElbow, b.RightElbow) &&
                Offset(a.LeftHand, b.LeftHand) && Offset(a.RightHand, b.RightHand);
        }

        private static bool Valid(SkateRiderRig r)
        { return Measurement(r.PelvisHeight) && Measurement(r.HipWidth) && Measurement(r.ThighLength) && Measurement(r.ShinLength) &&
            Measurement(r.FootLength) && Measurement(r.SpineLength) && Measurement(r.ShoulderWidth) && Measurement(r.UpperArmLength) &&
            Measurement(r.ForearmLength) && Measurement(r.NeckToHeadLength); }
        private static bool Same(SkateRiderRig a, SkateRiderRig b)
        { return a.PelvisHeight == b.PelvisHeight && a.HipWidth == b.HipWidth && a.ThighLength == b.ThighLength &&
            a.ShinLength == b.ShinLength && a.FootLength == b.FootLength && a.SpineLength == b.SpineLength &&
            a.ShoulderWidth == b.ShoulderWidth && a.UpperArmLength == b.UpperArmLength &&
            a.ForearmLength == b.ForearmLength && a.NeckToHeadLength == b.NeckToHeadLength; }
        private static bool Zero(SkateRiderRig r)
        { return Same(r, default(SkateRiderRig)); }
        private static bool Zero(SkateRiderPose p)
        { return Zero(p.Pelvis) && Zero(p.LeftHip) && Zero(p.RightHip) && Zero(p.LeftKnee) && Zero(p.RightKnee) &&
            Zero(p.LeftAnkle) && Zero(p.RightAnkle) && Zero(p.LeftToe) && Zero(p.RightToe) && Zero(p.Chest) && Zero(p.Neck) &&
            Zero(p.Head) && Zero(p.LeftShoulder) && Zero(p.RightShoulder) && Zero(p.LeftElbow) && Zero(p.RightElbow) &&
            Zero(p.LeftHand) && Zero(p.RightHand) && Zero(p.PelvisUp) && Zero(p.PelvisForward) && Zero(p.ChestUp) &&
            Zero(p.ChestForward) && Zero(p.HeadForward); }
        private static bool Zero(SkateVector v) { return v.X == 0 && v.Y == 0 && v.Z == 0; }
        private static bool Finite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }
        private static bool Measurement(double v) { return Finite(v) && v >= SkateRider.MinimumMeasurement && v <= SkateRider.MaximumMeasurement; }
        private static bool Coordinate(SkateVector v)
        { return Finite(v.X) && Finite(v.Y) && Finite(v.Z) && Math.Abs(v.X) <= 100000 && Math.Abs(v.Y) <= 100000 && Math.Abs(v.Z) <= 100000; }
        private static bool Normal(SkateVector v) { return Coordinate(v) && Math.Abs(v.LengthSquared - 1) <= FrameTolerance; }
        private static bool Near(SkateVector a, SkateVector b) { return (a - b).Length <= LengthTolerance; }
        private static bool Length(SkateVector a, SkateVector b, double length) { return Math.Abs((a - b).Length - length) <= LengthTolerance; }
        private static bool Offset(SkateVector a, SkateVector b) { return (a - b).Length <= MaximumOffset + 1e-10; }
        private static SkateVector Anchor(SkateRiderPose p) { return (p.LeftAnkle + p.RightAnkle) * 0.5; }
        private static SkateVector Unit(SkateVector v) { double length = v.Length; return length > 1e-12 ? v * (1 / length) : default(SkateVector); }
        private static SkateVector Limit(SkateVector v, double maximum) { double length = v.Length; return length > maximum ? v * (maximum / length) : v; }
        private static SkateVector Project(SkateVector v, SkateVector normal) { return v - normal * SkateVector.Dot(v, normal); }
        private static SkateVector Cross(SkateVector a, SkateVector b)
        { return new SkateVector(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X); }
    }
}
