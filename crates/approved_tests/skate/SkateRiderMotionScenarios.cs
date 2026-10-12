using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Shortcut.RustMod;

internal static class SkateRiderMotionScenarios
{
    private static readonly SkateRiderRig Rig = new SkateRiderRig(0.76, 0.27, 0.43, 0.43, 0.18, 0.46, 0.36, 0.28, 0.27, 0.17);
    private static readonly SkateVector Up = new SkateVector(0, 1, 0), Forward = new SkateVector(0, 0, 1);
    private static readonly string[] PoseFields = { "Pelvis", "LeftHip", "RightHip", "LeftKnee", "RightKnee", "LeftAnkle",
        "RightAnkle", "LeftToe", "RightToe", "Chest", "Neck", "Head", "LeftShoulder", "RightShoulder", "LeftElbow",
        "RightElbow", "LeftHand", "RightHand", "PelvisForward", "PelvisUp", "ChestForward", "ChestUp", "HeadForward" };
    private static readonly ConstructorInfo PoseConstructor = typeof(SkateRiderPose).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)[0];
    private static readonly ConstructorInfo StateConstructor = typeof(SkateRiderMotionState).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)[0];
    private const double Tolerance = 0.000001;
    private static int checks;

    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new InvalidOperationException(message); }
    private static void Near(double actual, double expected, double tolerance, string message)
    { Check(!double.IsNaN(actual) && !double.IsInfinity(actual) && Math.Abs(actual - expected) <= tolerance, message); }
    private static void Near(SkateVector actual, SkateVector expected, double tolerance, string message)
    { Near((actual - expected).Length, 0, tolerance, message); }
    private static SkateVector Cross(SkateVector a, SkateVector b)
    { return new SkateVector(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X); }
    private static SkateVector Field(SkateRiderPose p, string name)
    { return (SkateVector)typeof(SkateRiderPose).GetField(name).GetValue(p); }

    private static SkateRiderPose Target(SkateRiderRig rig, double crouch = 0, double lean = 0,
                                         SkateStance stance = SkateStance.Regular, bool ridingSwitch = false,
                                         bool pushing = false, double phase = 0, bool airborne = false,
                                         SkateGrabKind grab = SkateGrabKind.None, double handplant = 0)
    {
        SkateRiderPose pose; string error;
        var input = new SkateRiderInput(default(SkateVector), Forward, Up, stance, ridingSwitch, 6, lean, crouch,
            airborne, pushing, phase, 0, grab, false, 25, 0, 0, 0, false, 0, SkateRider.GrabIn, handplant);
        Check(SkateRider.TryCreate(rig, input, out pose, out error) && error == null, "actual rider target is reachable");
        return pose;
    }

    private static SkateRiderPose Step(ref SkateRiderMotionState state, SkateRiderRig rig, SkateRiderPose target, double dt)
    {
        SkateRiderPose shown; SkateRiderMotionState next; string error;
        Check(SkateRiderMotion.TryStep(state, rig, target, dt, out shown, out next, out error) && error == null,
            "valid secondary motion step");
        Check(next.Initialized, "state initialized");
        Verify(shown, target, rig);
        state = next;
        return shown;
    }

    private static SkateRiderMotionOptions Options(int mask)
    { return new SkateRiderMotionOptions((mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0); }

    private static SkateRiderPose Step(ref SkateRiderMotionState state, SkateRiderRig rig, SkateRiderPose target,
                                      double dt, SkateRiderMotionOptions options)
    {
        SkateRiderPose shown; SkateRiderMotionState next; string error;
        Check(SkateRiderMotion.TryStep(state, rig, target, dt, options, out shown, out next, out error) && error == null,
            "valid per-part secondary motion step");
        Check(next.Initialized, "per-part state initialized");
        if (options.DisablePelvisSpring)
            Check(shown.Pelvis.Equals(target.Pelvis), "disabled pelvis stays exactly on target");
        if (options.PinLeftHand)
            Check(shown.LeftHand.Equals(target.LeftHand), "left hand stays exactly on target");
        if (options.PinRightHand)
            Check(shown.RightHand.Equals(target.RightHand), "right hand stays exactly on target");
        Verify(shown, target, rig);
        state = next;
        return shown;
    }

    private static bool SameBits(object a, object b)
    {
        var type = a.GetType();
        if (type != b.GetType()) return false;
        if (type == typeof(double))
            return BitConverter.DoubleToInt64Bits((double)a) == BitConverter.DoubleToInt64Bits((double)b);
        if (type == typeof(bool)) return (bool)a == (bool)b;
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            if (!SameBits(field.GetValue(a), field.GetValue(b))) return false;
        return true;
    }

    private static void Verify(SkateRiderPose p, SkateRiderPose target, SkateRiderRig rig)
    {
        Check(p.LeftAnkle.Equals(target.LeftAnkle) && p.RightAnkle.Equals(target.RightAnkle) &&
            p.LeftToe.Equals(target.LeftToe) && p.RightToe.Equals(target.RightToe), "ankles and toes copied exactly");
        foreach (var name in PoseFields)
        {
            var value = Field(p, name);
            Check(!double.IsNaN(value.X) && !double.IsInfinity(value.X) && Math.Abs(value.X) <= 100000 &&
                  !double.IsNaN(value.Y) && !double.IsInfinity(value.Y) && Math.Abs(value.Y) <= 100000 &&
                  !double.IsNaN(value.Z) && !double.IsInfinity(value.Z) && Math.Abs(value.Z) <= 100000, "finite coordinate " + name);
        }
        for (int i = 0; i < 18; i++)
            Check((Field(p, PoseFields[i]) - Field(target, PoseFields[i])).Length <= 0.12 + 1e-10, "post-IK bound " + PoseFields[i]);
        Near((p.LeftKnee - p.LeftHip).Length, rig.ThighLength, Tolerance, "left thigh length");
        Near((p.RightKnee - p.RightHip).Length, rig.ThighLength, Tolerance, "right thigh length");
        Near((p.LeftAnkle - p.LeftKnee).Length, rig.ShinLength, Tolerance, "left shin length");
        Near((p.RightAnkle - p.RightKnee).Length, rig.ShinLength, Tolerance, "right shin length");
        Near((p.LeftElbow - p.LeftShoulder).Length, rig.UpperArmLength, Tolerance, "left upper arm length");
        Near((p.RightElbow - p.RightShoulder).Length, rig.UpperArmLength, Tolerance, "right upper arm length");
        Near((p.LeftHand - p.LeftElbow).Length, rig.ForearmLength, Tolerance, "left forearm length");
        Near((p.RightHand - p.RightElbow).Length, rig.ForearmLength, Tolerance, "right forearm length");
        Near((p.LeftToe - p.LeftAnkle).Length, rig.FootLength, Tolerance, "left foot length");
        Near((p.RightToe - p.RightAnkle).Length, rig.FootLength, Tolerance, "right foot length");
        Near((p.RightHip - p.LeftHip).Length, rig.HipWidth, Tolerance, "hip span");
        Near((p.Chest - p.Pelvis).Length, rig.SpineLength, Tolerance, "spine length");
        Near((p.Neck - p.Chest).Length, rig.SpineLength * 0.12, Tolerance, "neck spacing");
        Near((p.Head - p.Neck).Length, rig.NeckToHeadLength, Tolerance, "head length");
        Near((p.RightShoulder - p.LeftShoulder).Length, rig.ShoulderWidth, Tolerance, "shoulder span");
        Near(p.LeftHip, p.Pelvis - Cross(p.PelvisUp, p.PelvisForward) * (rig.HipWidth * 0.5), Tolerance, "left hip frame");
        Near(p.RightHip, p.Pelvis + Cross(p.PelvisUp, p.PelvisForward) * (rig.HipWidth * 0.5), Tolerance, "right hip frame");
        foreach (var value in new[] { p.PelvisUp, p.PelvisForward, p.ChestUp, p.ChestForward, p.HeadForward })
            Near(value.LengthSquared, 1, Tolerance, "unit orientation");
        Near(SkateVector.Dot(p.PelvisUp, p.PelvisForward), 0, Tolerance, "orthogonal pelvis frame");
        Near(SkateVector.Dot(p.ChestUp, p.ChestForward), 0, Tolerance, "orthogonal chest frame");
    }

    private static SkateRiderPose Transform(SkateRiderPose p, double yaw, SkateVector translation)
    {
        var values = new object[PoseFields.Length];
        for (int i = 0; i < values.Length; i++)
        {
            var v = Rotate(Field(p, PoseFields[i]), yaw);
            values[i] = i < 18 ? v + translation : v;
        }
        return (SkateRiderPose)PoseConstructor.Invoke(values);
    }
    private static SkateVector Rotate(SkateVector v, double yaw)
    {
        double angle = yaw * Math.PI / 180, c = Math.Cos(angle), s = Math.Sin(angle);
        return new SkateVector(v.X * c + v.Z * s, v.Y, -v.X * s + v.Z * c);
    }
    private static SkateRiderPose Changed(SkateRiderPose p, string field, SkateVector value)
    {
        var values = new object[PoseFields.Length];
        for (int i = 0; i < values.Length; i++) values[i] = PoseFields[i] == field ? value : Field(p, PoseFields[i]);
        return (SkateRiderPose)PoseConstructor.Invoke(values);
    }

    private static SkateRiderPose ChestYaw(SkateRiderPose p, double degrees)
    {
        var values = new object[PoseFields.Length];
        for (int i = 0; i < values.Length; i++)
        {
            var value = Field(p, PoseFields[i]);
            if (i >= 12 && i <= 17) value = p.Chest + RotateAxis(value - p.Chest, p.ChestUp, degrees);
            if (PoseFields[i] == "ChestForward") value = RotateAxis(value, p.ChestUp, degrees);
            values[i] = value;
        }
        return (SkateRiderPose)PoseConstructor.Invoke(values);
    }
    private static SkateVector RotateAxis(SkateVector value, SkateVector axis, double degrees)
    {
        double angle = degrees * Math.PI / 180, c = Math.Cos(angle), s = Math.Sin(angle);
        return value * c + Cross(axis, value) * s + axis * (SkateVector.Dot(axis, value) * (1 - c));
    }

    private static void InitializationAndResponse()
    {
        var standing = Target(Rig); var bent = Target(Rig, 0.12);
        var state = default(SkateRiderMotionState);
        var first = Step(ref state, Rig, standing, 0.01);
        Check(first.Equals(standing), "default state shows exact target");
        for (int i = 0; i < 20; i++)
        {
            var stationary = Step(ref state, Rig, standing, 0.01);
            foreach (var name in PoseFields) Near(Field(stationary, name), Field(standing, name), 1e-8, "stationary target does not acquire artificial motion");
        }
        var lag = Step(ref state, Rig, bent, 0.01);
        Check((lag.Pelvis - bent.Pelvis).Length > 0.001, "pelvis height spring visibly lags target");
        Check((lag.LeftHand - bent.LeftHand).Length > 0.001 || (lag.RightHand - bent.RightHand).Length > 0.001,
            "hands visibly lag changed body pose");
        double minimum = SkateVector.Dot(lag.Pelvis - bent.Pelvis, bent.PelvisUp);
        for (int i = 0; i < 300; i++)
        {
            var p = Step(ref state, Rig, bent, 0.01);
            minimum = Math.Min(minimum, SkateVector.Dot(p.Pelvis - bent.Pelvis, bent.PelvisUp));
        }
        Check(minimum < -0.00001, "underdamped pelvis crosses the target before settling");
        Near(state.Shown.Pelvis, bent.Pelvis, 0.00002, "pelvis spring converges");
        Near(state.Shown.LeftHand, bent.LeftHand, 0.00005, "left hand converges");
        Near(state.Shown.RightHand, bent.RightHand, 0.00005, "right hand converges");

        state = default(SkateRiderMotionState);
        Step(ref state, Rig, standing, 0.01);
        var yawed = ChestYaw(standing, 30);
        var shoulderLag = Step(ref state, Rig, yawed, 0.01);
        Check((shoulderLag.LeftShoulder - yawed.LeftShoulder).Length > 0.001 &&
            (shoulderLag.RightShoulder - yawed.RightShoulder).Length > 0.001, "pure chest yaw produces two shoulder-particle lags");
        Near(shoulderLag.Pelvis, yawed.Pelvis, 1e-8, "isolated shoulder yaw preserves pelvis");
        double initial = SkateVector.Dot(Cross(yawed.ChestForward, shoulderLag.ChestForward), yawed.ChestUp);
        bool overshot = false;
        for (int i = 0; i < 300; i++)
        {
            var p = Step(ref state, Rig, yawed, 0.01);
            overshot |= initial * SkateVector.Dot(Cross(yawed.ChestForward, p.ChestForward), yawed.ChestUp) < -0.00001;
        }
        Check(overshot, "shoulder Verlet yaw overshoots before settling");
        Near(state.Shown.LeftShoulder, yawed.LeftShoulder, 0.00005, "left shoulder yaw converges");
        Near(state.Shown.RightShoulder, yawed.RightShoulder, 0.00005, "right shoulder yaw converges");
    }

    private static void TransportAndReset()
    {
        var target = Target(Rig, 0.12);
        var state = default(SkateRiderMotionState);
        Step(ref state, Rig, Target(Rig), 0.01);
        for (int i = 0; i < 6; i++) Step(ref state, Rig, target, 0.01);
        var still = state; var moving = state;
        var stillShown = Step(ref still, Rig, target, 0.05);
        var translation = new SkateVector(0.7, 0.08, 0);
        var moved = Transform(target, 20, translation);
        var movedShown = Step(ref moving, Rig, moved, 0.05);
        var expected = Transform(stillShown, 20, translation);
        foreach (var name in PoseFields) Near(Field(movedShown, name), Field(expected, name), 1e-8, "rigid high-speed frame transport " + name);
        Check((movedShown.Pelvis - moved.Pelvis).Length > 0.0001, "14m/s translation retains existing lag");
        var teleported = Transform(moved, 0, new SkateVector(3, 0, 0));
        Check(Step(ref moving, Rig, teleported, 0.05).Equals(teleported), "large translation resets exactly");
        var anchor = (teleported.LeftAnkle + teleported.RightAnkle) * 0.5;
        var turned = Transform(teleported, 180, anchor - Rotate(anchor, 180));
        Near((turned.LeftAnkle + turned.RightAnkle) * 0.5, anchor, 1e-8, "rotation reset has no anchor translation");
        Check(Step(ref moving, Rig, turned, 0.01).Equals(turned), "large frame rotation resets exactly");
        var changedRig = new SkateRiderRig(0.76, 0.27, 0.43, 0.43, 0.18, 0.46, 0.36, 0.29, 0.27, 0.17);
        var changedTarget = Target(changedRig, 0.12);
        Check(Step(ref moving, changedRig, changedTarget, 0.01).Equals(changedTarget), "rig change resets old lengths/history");
    }

    private static void TimestepsAndReach()
    {
        var target = Target(Rig, 0.12);
        var fine = default(SkateRiderMotionState); var coarse = default(SkateRiderMotionState);
        var start = Target(Rig);
        Step(ref fine, Rig, start, 1.0 / 120); Step(ref coarse, Rig, start, 1.0 / 120);
        for (int i = 0; i < 30; i++) Step(ref fine, Rig, target, 1.0 / 120);
        for (int i = 0; i < 10; i++) Step(ref coarse, Rig, target, 0.025);
        Near(fine.Shown.Pelvis, coarse.Shown.Pelvis, 1e-8, "pelvis scalar agrees across equal internal substep partitions");
        foreach (var name in PoseFields) Near(Field(fine.Shown, name), Field(coarse.Shown, name), 0.01, "bounded articulation projection difference across timestep partitions " + name);
        foreach (double dt in new[] { 0.0001, 0.001, 0.007, 0.013, 0.05 })
        {
            var state = default(SkateRiderMotionState);
            Step(ref state, Rig, start, dt);
            for (int i = 0; i < 40; i++) Step(ref state, Rig, target, dt);
        }
        var unequal = new SkateRiderRig(0.7, 0.24, 0.3, 0.54, 0.16, 0.44, 0.34, 0.35, 0.23, 0.16);
        var nearReach = new SkateRiderRig(0.86, 0.16, 0.43, 0.43, 0.18, 0.46, 0.36, 0.28, 0.27, 0.17);
        foreach (var rig in new[] { Rig, unequal, nearReach })
        {
            foreach (var stance in new[] { SkateStance.Regular, SkateStance.Goofy })
            {
                foreach (bool ridingSwitch in new[] { false, true })
                {
                    var state = default(SkateRiderMotionState);
                    for (int i = 0; i < 80; i++)
                    {
                        double crouch = i < 20 || i >= 60 ? 0 : 0.65;
                        var p = Target(rig, crouch, Math.Sin(i * 0.11) * 12, stance, ridingSwitch);
                        Step(ref state, rig, p, i % 2 == 0 ? 0.007 : 0.021);
                    }
                }
            }
        }
        var inverted = Target(Rig, airborne: true, handplant: 1);
        var plantState = default(SkateRiderMotionState);
        Check(Step(ref plantState, Rig, inverted, 0.01).Equals(inverted), "valid inverted handplant target accepted");
        Step(ref plantState, Rig, inverted, 0.05);
    }

    private static void Determinism()
    {
        var a = default(SkateRiderMotionState); var b = default(SkateRiderMotionState);
        for (int i = 0; i < 160; i++)
        {
            double phase = (i % 80) / 79.0;
            var target = Target(Rig, 0.2 + 0.1 * Math.Sin(i * 0.07), Math.Sin(i * 0.05) * 15,
                SkateStance.Goofy, true, true, phase);
            double dt = i % 3 == 0 ? 0.007 : i % 3 == 1 ? 0.013 : 0.025;
            var x = Step(ref a, Rig, target, dt); var y = Step(ref b, Rig, target, dt);
            Check(x.Equals(y) && a.Equals(b), "identical sequence reproduces exact pose and state");
        }
    }

    private static void GrabTransitions()
    {
        foreach (var stance in new[] { SkateStance.Regular, SkateStance.Goofy })
            foreach (bool ridingSwitch in new[] { false, true })
            {
                var state = default(SkateRiderMotionState);
                Step(ref state, Rig, Target(Rig, stance: stance, ridingSwitch: ridingSwitch), 0.01);
                foreach (SkateGrabKind grab in Enum.GetValues(typeof(SkateGrabKind)))
                {
                    var target = Target(Rig, crouch: 0.25, stance: stance, ridingSwitch: ridingSwitch,
                        airborne: true, grab: grab);
                    for (int i = 0; i < 8; i++) Step(ref state, Rig, target, i % 2 == 0 ? 0.007 : 0.021);
                }
            }
    }

    private static void DefaultOverloadCompatibility()
    {
        var oldState = default(SkateRiderMotionState); var explicitState = oldState;
        for (int i = 0; i < 160; i++)
        {
            var target = Target(Rig, 0.14 + Math.Sin(i * 0.09) * 0.12, Math.Sin(i * 0.13) * 8,
                SkateStance.Goofy, true, true, (i % 60) / 59.0);
            double dt = i % 3 == 0 ? 0.007 : i % 3 == 1 ? 0.013 : 0.025;
            var oldShown = Step(ref oldState, Rig, target, dt);
            var explicitShown = Step(ref explicitState, Rig, target, dt, default(SkateRiderMotionOptions));
            Check(SameBits(oldShown, explicitShown), "old/default overload poses agree bit for bit");
            Check(SameBits(oldState, explicitState), "old/default overload histories agree bit for bit");
        }
    }

    private static void PartControlsAndHistories()
    {
        var start = Target(Rig); var moved = ChestYaw(Target(Rig, 0.08, 4), 8);
        for (int mask = 0; mask < 8; mask++)
        {
            var options = Options(mask);
            Check(options.DisablePelvisSpring == ((mask & 1) != 0) && options.PinLeftHand == ((mask & 2) != 0) &&
                options.PinRightHand == ((mask & 4) != 0), "options constructor preserves independent controls");
            var state = default(SkateRiderMotionState);
            Check(Step(ref state, Rig, start, 0.01, options).Equals(start), "all options initialize at exact target");
            bool shouldersLag = false, leftLags = false, rightLags = false;
            for (int i = 0; i < 40; i++)
            {
                var shown = Step(ref state, Rig, moved, i % 2 == 0 ? 0.007 : 0.013, options);
                shouldersLag |= (shown.LeftShoulder - moved.LeftShoulder).Length > 0.00001 ||
                    (shown.RightShoulder - moved.RightShoulder).Length > 0.00001;
                leftLags |= (shown.LeftHand - moved.LeftHand).Length > 0.00001;
                rightLags |= (shown.RightHand - moved.RightHand).Length > 0.00001;
                if (options.DisablePelvisSpring)
                    Check(state.PreviousPelvis.Equals(state.Shown.Pelvis), "disabled pelvis has no hidden spring velocity");
                if (options.PinLeftHand)
                    Check(state.PreviousLeftHand.Equals(state.Shown.LeftHand), "pinned left hand has no hidden velocity");
                if (options.PinRightHand)
                    Check(state.PreviousRightHand.Equals(state.Shown.RightHand), "pinned right hand has no hidden velocity");
            }
            Check(shouldersLag, "admissible shoulder motion remains active with per-part controls");
            if (!options.PinLeftHand) Check(leftLags, "free left hand retains secondary motion");
            if (!options.PinRightHand) Check(rightLags, "free right hand retains secondary motion");

            Step(ref state, Rig, start, 0.01);
            Step(ref state, Rig, moved, 0.01);
            var pinned = Step(ref state, Rig, moved, 0.01, Options(7));
            Check(state.PreviousPelvis.Equals(pinned.Pelvis) && state.PreviousLeftHand.Equals(pinned.LeftHand) &&
                state.PreviousRightHand.Equals(pinned.RightHand), "enabling controls clears already warmed histories");
            var released = Step(ref state, Rig, moved, 0.01, Options(0));
            Near(released.Pelvis, moved.Pelvis, 1e-12, "releasing pelvis restarts without old spring kick");
            for (int i = 0; i < 48; i++)
            {
                options = Options(i % 8);
                var target = ChestYaw(Target(Rig, 0.08 + Math.Sin(i * 0.21) * 0.06, Math.Sin(i * 0.17) * 5),
                    Math.Sin(i * 0.19) * 8);
                Step(ref state, Rig, target, i % 2 == 0 ? 0.007 : 0.021, options);
            }
        }
    }

    private static SkateRiderPose StraightArm(SkateRiderPose target, bool left)
    {
        var root = left ? target.LeftShoulder : target.RightShoulder;
        var hand = left ? target.LeftHand : target.RightHand;
        var direction = (hand - root) * (1 / (hand - root).Length);
        target = Changed(target, left ? "LeftElbow" : "RightElbow", root + direction * Rig.UpperArmLength);
        return Changed(target, left ? "LeftHand" : "RightHand", root + direction * (Rig.UpperArmLength + Rig.ForearmLength));
    }

    private static void ControlledReachAndReset()
    {
        var target = Target(Rig); var extended = StraightArm(StraightArm(ChestYaw(target, 8), true), false);
        var changedRig = new SkateRiderRig(0.76, 0.27, 0.43, 0.43, 0.18, 0.46, 0.36, 0.29, 0.27, 0.17);
        for (int mask = 0; mask < 8; mask++)
        {
            var options = Options(mask); var state = default(SkateRiderMotionState);
            Step(ref state, Rig, target, 0.01, options);
            for (int i = 0; i < 12; i++) Step(ref state, Rig, extended, 0.01, options);
            var teleported = Transform(extended, 0, new SkateVector(3, 0, 0));
            Check(Step(ref state, Rig, teleported, 0.01, options).Equals(teleported), "controlled teleport resets at exact target");
            var anchor = (teleported.LeftAnkle + teleported.RightAnkle) * 0.5;
            var turned = Transform(teleported, 180, anchor - Rotate(anchor, 180));
            Check(Step(ref state, Rig, turned, 0.01, options).Equals(turned), "controlled frame reversal resets exactly");
            var changedTarget = Target(changedRig, 0.12);
            Check(Step(ref state, changedRig, changedTarget, 0.01, options).Equals(changedTarget),
                "controlled rig change resets old geometry/history");
            var inverted = Target(Rig, airborne: true, handplant: 1);
            Step(ref state, Rig, inverted, 0.01, options);
            for (int i = 0; i < 8; i++)
                Step(ref state, Rig, ChestYaw(inverted, Math.Sin(i * 0.31) * 4), 0.013, options);
        }
    }

    private static void ControlledGrabTransitions()
    {
        foreach (var stance in new[] { SkateStance.Regular, SkateStance.Goofy })
            foreach (bool ridingSwitch in new[] { false, true })
                for (int mask = 0; mask < 8; mask++)
                {
                    var options = Options(mask); var state = default(SkateRiderMotionState);
                    Step(ref state, Rig, Target(Rig, stance: stance, ridingSwitch: ridingSwitch), 0.01, options);
                    foreach (SkateGrabKind grab in Enum.GetValues(typeof(SkateGrabKind)))
                        for (int i = 0; i < 4; i++)
                        {
                            var target = Target(Rig, crouch: 0.22 + i * 0.01, lean: i * 2,
                                stance: stance, ridingSwitch: ridingSwitch, airborne: true, grab: grab);
                            Step(ref state, Rig, target, i % 2 == 0 ? 0.007 : 0.021, options);
                        }
                }
    }

    private static void ControlledDeterminismAndTransport()
    {
        for (int mask = 0; mask < 8; mask++)
        {
            var options = Options(mask); var a = default(SkateRiderMotionState); var b = a;
            for (int i = 0; i < 80; i++)
            {
                var target = Target(Rig, 0.12 + Math.Sin(i * 0.09) * 0.08, Math.Sin(i * 0.07) * 8,
                    SkateStance.Goofy, true, true, (i % 40) / 39.0);
                double dt = i % 3 == 0 ? 0.007 : i % 3 == 1 ? 0.013 : 0.025;
                var x = Step(ref a, Rig, target, dt, options); var y = Step(ref b, Rig, target, dt, options);
                Check(SameBits(x, y) && SameBits(a, b), "controlled sequence reproduces exact pose/history bits");
            }
            var finalTarget = a.Target; var still = a; var moving = a;
            var stillShown = Step(ref still, Rig, finalTarget, 0.025, options);
            var translation = new SkateVector(0.4, 0.05, -0.2);
            var movedTarget = Transform(finalTarget, 15, translation);
            var movingShown = Step(ref moving, Rig, movedTarget, 0.025, options);
            var expected = Transform(stillShown, 15, translation);
            foreach (var name in PoseFields)
                Near(Field(movingShown, name), Field(expected, name), 1e-8, "controlled rigid frame transport " + name);
        }
    }

    private static void Reject(SkateRiderMotionState state, SkateRiderRig rig, SkateRiderPose target, double dt)
    {
        SkateRiderPose shown; SkateRiderMotionState next; string error;
        Check(!SkateRiderMotion.TryStep(state, rig, target, dt, out shown, out next, out error) && error != null,
            "invalid motion request refused");
        Check(shown.Equals(default(SkateRiderPose)) && next.Equals(state), "invalid request preserves state atomically");
    }
    private static void Reject(SkateRiderMotionState state, SkateRiderRig rig, SkateRiderPose target,
                               double dt, SkateRiderMotionOptions options)
    {
        SkateRiderPose shown; SkateRiderMotionState next; string error;
        Check(!SkateRiderMotion.TryStep(state, rig, target, dt, options, out shown, out next, out error) && error != null,
            "invalid per-part motion request refused");
        Check(shown.Equals(default(SkateRiderPose)) && SameBits(next, state), "per-part refusal preserves state bits atomically");
    }
    private static SkateRiderMotionState Corrupt(SkateRiderMotionState state, int index, object value)
    {
        object[] values = { state.Rig, state.Target, state.Shown, state.PreviousPelvis, state.PreviousChest,
            state.PreviousLeftShoulder, state.PreviousRightShoulder, state.PreviousLeftHand, state.PreviousRightHand, state.PreviousDelta };
        values[index] = value;
        return (SkateRiderMotionState)StateConstructor.Invoke(values);
    }

    private static void InvalidInputsAndAllocation()
    {
        var target = Target(Rig); var state = default(SkateRiderMotionState);
        Step(ref state, Rig, target, 0.01);
        foreach (double dt in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity, -1, 0,
                                     double.Epsilon, 0.00009999, 0.05000001 }) Reject(state, Rig, target, dt);
        Reject(state, Rig, default(SkateRiderPose), 0.01);
        Reject(state, default(SkateRiderRig), target, 0.01);
        foreach (var name in PoseFields)
            foreach (double invalid in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
                for (int component = 0; component < 3; component++)
                {
                    var v = Field(target, name);
                    var bad = new SkateVector(component == 0 ? invalid : v.X, component == 1 ? invalid : v.Y, component == 2 ? invalid : v.Z);
                    Reject(state, Rig, Changed(target, name, bad), 0.01);
                }
        for (int i = 18; i < PoseFields.Length; i++) Reject(state, Rig, Changed(target, PoseFields[i], default(SkateVector)), 0.01);
        Reject(state, Rig, Changed(target, "LeftAnkle", target.LeftAnkle + new SkateVector(0.01, 0, 0)), 0.01);
        Reject(state, Rig, Changed(target, "Chest", target.Chest + Up * 0.01), 0.01);
        Reject(state, Rig, Changed(target, "PelvisForward", target.PelvisUp), 0.01);
        Reject(state, Rig, Transform(target, 0, new SkateVector(100001, 0, 0)), 0.01);
        double[] measures = { 0.76, 0.27, 0.43, 0.43, 0.18, 0.46, 0.36, 0.28, 0.27, 0.17 };
        for (int i = 0; i < measures.Length; i++)
        {
            var values = (double[])measures.Clone(); values[i] = double.NaN;
            var bad = new SkateRiderRig(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8], values[9]);
            Reject(state, bad, target, 0.01);
        }
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, 0, 0.00001, 0.06 })
            Reject(Corrupt(state, 9, invalid), Rig, target, 0.01);
        for (int i = 3; i <= 8; i++)
        {
            Reject(Corrupt(state, i, new SkateVector(double.NaN, 0, 0)), Rig, target, 0.01);
            Reject(Corrupt(state, i, new SkateVector(1000, 0, 0)), Rig, target, 0.01);
        }
        Reject(Corrupt(state, 0, default(SkateRiderRig)), Rig, target, 0.01);
        Reject(Corrupt(state, 1, default(SkateRiderPose)), Rig, target, 0.01);
        Reject(Corrupt(state, 2, default(SkateRiderPose)), Rig, target, 0.01);

        SkateRiderPose shown; SkateRiderMotionState next; string error;
        for (int i = 0; i < 200; i++)
            Check(SkateRiderMotion.TryStep(state, Rig, target, 0.01, out shown, out next, out error), "warm actual step");
        long before = GC.GetAllocatedBytesForCurrentThread(); bool success = true;
        for (int i = 0; i < 20000; i++)
        {
            success &= SkateRiderMotion.TryStep(state, Rig, target, 0.01, out shown, out next, out error);
            state = next;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(success && allocated == 0, "warmed successful steps allocate zero bytes");
        before = GC.GetAllocatedBytesForCurrentThread(); bool rejected = true;
        for (int i = 0; i < 20000; i++)
            rejected &= !SkateRiderMotion.TryStep(state, Rig, target, double.NaN, out shown, out next, out error);
        allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(rejected && allocated == 0, "warmed refusals allocate zero bytes");
    }

    private static void ControlledInvalidInputsAndAllocation()
    {
        var target = Target(Rig); var state = default(SkateRiderMotionState);
        Step(ref state, Rig, target, 0.01);
        for (int mask = 0; mask < 8; mask++)
        {
            var options = Options(mask);
            foreach (double dt in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity, -1, 0,
                                         double.Epsilon, 0.00009999, 0.05000001 })
                Reject(state, Rig, target, dt, options);
            Reject(state, default(SkateRiderRig), target, 0.01, options);
            Reject(state, Rig, default(SkateRiderPose), 0.01, options);
            foreach (var name in PoseFields)
                for (int component = 0; component < 3; component++)
                {
                    var value = Field(target, name);
                    var bad = new SkateVector(component == 0 ? double.NaN : value.X,
                        component == 1 ? double.PositiveInfinity : value.Y,
                        component == 2 ? double.NegativeInfinity : value.Z);
                    Reject(state, Rig, Changed(target, name, bad), 0.01, options);
                }
            Reject(state, Rig, Changed(target, "LeftAnkle", target.LeftAnkle + Up * 0.01), 0.01, options);
            Reject(state, Rig, Changed(target, "Chest", target.Chest + Up * 0.01), 0.01, options);
            for (int i = 3; i <= 8; i++)
            {
                Reject(Corrupt(state, i, new SkateVector(double.NaN, 0, 0)), Rig, target, 0.01, options);
                Reject(Corrupt(state, i, new SkateVector(1000, 0, 0)), Rig, target, 0.01, options);
            }
            Reject(Corrupt(state, 9, double.NaN), Rig, target, 0.01, options);
            Reject(Corrupt(state, 9, 0.06), Rig, target, 0.01, options);
            for (int i = 0; i < 3; i++)
                Reject(Corrupt(state, i, i == 0 ? (object)default(SkateRiderRig) : default(SkateRiderPose)),
                    Rig, target, 0.01, options);

            SkateRiderPose shown; SkateRiderMotionState next; string error;
            for (int i = 0; i < 200; i++)
            {
                Check(SkateRiderMotion.TryStep(state, Rig, target, 0.01, options, out shown, out next, out error),
                    "warm actual controlled step");
                state = next;
            }
            long before = GC.GetAllocatedBytesForCurrentThread(); bool success = true;
            for (int i = 0; i < 20000; i++)
            {
                success &= SkateRiderMotion.TryStep(state, Rig, target, 0.01, options, out shown, out next, out error);
                state = next;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(success && allocated == 0, "warmed controlled steps allocate zero bytes");
            before = GC.GetAllocatedBytesForCurrentThread(); bool rejected = true;
            for (int i = 0; i < 20000; i++)
                rejected &= !SkateRiderMotion.TryStep(state, Rig, target, double.NaN, options, out shown, out next, out error);
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(rejected && allocated == 0, "warmed controlled refusals allocate zero bytes");
        }
    }

    public static int Main(string[] args)
    {
        try
        {
            Check(args.Length <= 1, "optional argument is the expected actual-DLL SHA-256");
            string library = typeof(SkateRiderMotion).Assembly.Location, digest;
            using (var hash = SHA256.Create())
                digest = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(library))).Replace("-", "").ToLowerInvariant();
            Console.WriteLine("Actual library: " + library + "\nSHA-256: " + digest);
            if (args.Length == 1) Check(string.Equals(args[0], digest, StringComparison.OrdinalIgnoreCase), "actual library hash pinned");
            InitializationAndResponse(); TransportAndReset(); TimestepsAndReach(); Determinism(); GrabTransitions(); InvalidInputsAndAllocation();
            DefaultOverloadCompatibility(); PartControlsAndHistories(); ControlledReachAndReset(); ControlledGrabTransitions();
            ControlledDeterminismAndTransport(); ControlledInvalidInputsAndAllocation();
            Console.WriteLine("skate_rider_secondary_motion: " + checks + " checks passed");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
