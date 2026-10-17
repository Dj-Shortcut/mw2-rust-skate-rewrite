using System;
using System.IO;
using System.Security.Cryptography;
using Shortcut.RustMod;

internal static class SkateRidePhysicsScenarios
{
    private const double Tolerance = 1e-10;
    private static readonly SkateVector Forward = new SkateVector(0, 0, 1);
    private static readonly SkateVector Up = new SkateVector(0, 1, 0);
    private static int checks;

    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new InvalidOperationException(message); }
    private static void Near(double actual, double expected, string message)
    { Check(!double.IsNaN(actual) && !double.IsInfinity(actual) && Math.Abs(actual - expected) <= Tolerance, message + ": " + actual + " != " + expected); }
    private static void Vector(SkateVector actual, SkateVector expected, string message)
    { Near(actual.X, expected.X, message + " X"); Near(actual.Y, expected.Y, message + " Y"); Near(actual.Z, expected.Z, message + " Z"); }
    private static SkateVector Normal(double degrees)
    { double angle = degrees * Math.PI / 180; return new SkateVector(0, Math.Cos(angle), -Math.Sin(angle)); }
    private static SkateVector Rotate(SkateVector vector, double degrees)
    {
        double angle = degrees * Math.PI / 180, c = Math.Cos(angle), s = Math.Sin(angle);
        return new SkateVector(vector.X * c + vector.Z * s, vector.Y, -vector.X * s + vector.Z * c);
    }

    private static SkatePumpResult Pump(ref SkatePumpState state, bool supported, bool crouch,
                                         double slope, double speed, SkateVector travel, double dt = 0.05)
    {
        SkatePumpResult result;
        string error;
        Check(SkateRidePhysics.TryPump(state, new SkatePumpInput(supported, crouch, slope, speed, travel), dt,
                                      out result, out error) && error == null, "valid pump observation");
        Check(result.EnergyGain >= 0 && result.EnergyGain <= 12 + Tolerance, "per-sample pump budget");
        state = result.State;
        return result;
    }
    private static SkatePumpState Charge(double slope = -35, int count = 8, double dt = 0.05)
    {
        var state = default(SkatePumpState);
        for (int i = 0; i < count; i++)
        {
            var result = Pump(ref state, true, true, slope, 6, Forward, dt);
            Near(result.EnergyGain, 0, "charging adds no speed");
        }
        return state;
    }

    private static void TransitionPumping()
    {
        int start = checks;
        Near(SkateRidePhysics.MaximumPumpEnergy, 12, "energy cap");
        var state = Charge();
        var full = Pump(ref state, true, false, 0, 6, Forward);
        Check(full.Awarded, "release at bottom awards");
        Near(full.EnergyGain, 12, "full descent charge energy");
        Near(full.TangentSpeed, 7.745966692414834, "full charge speed");
        Near(full.SpeedGain, 1.745966692414834, "full charge speed gain");
        double speed = full.TangentSpeed;
        for (int i = 0; i < 200; i++)
        {
            var result = Pump(ref state, true, i % 2 == 0, i % 3 - 1, speed, Forward);
            Near(result.EnergyGain, 0, "spent flat/threshold spam gives no energy");
            speed = result.TangentSpeed;
        }
        Near(speed, full.TangentSpeed, "spam preserves awarded speed");
        Pump(ref state, true, false, 15, speed, Forward);
        for (int i = 0; i < 8; i++) Pump(ref state, true, true, -35, speed, Forward);
        var second = Pump(ref state, true, false, 0, speed, Forward);
        Near(second.EnergyGain, 12, "genuine ascent/new descent rearms one pass");
        Near(second.TangentSpeed, 9.16515138991168, "second pass speed");
        Near(full.TangentSpeed * full.TangentSpeed / 32, 1.875, "one pump ideal height at gravity16");
        Near(second.TangentSpeed * second.TangentSpeed / 32, 2.625, "two pump ideal height");
        Check(1.875 < 2.4971864443607004 && 2.625 > 2.4971864443607004, "two-pass energy fixture brackets authored deck");

        state = Charge(-35, 16, 0.025);
        var split = Pump(ref state, true, false, 0, 6, Forward, 0.025);
        Near(split.EnergyGain, full.EnergyGain, "charge partition energy");
        Near(split.TangentSpeed, full.TangentSpeed, "charge partition speed");
        state = Charge(-35, 4);
        var half = Pump(ref state, true, false, 0, 6, Forward);
        Near(half.EnergyGain, 6, "half charge"); Near(half.TangentSpeed, 6.928203230275509, "half-charge speed");
        state = Charge(-15);
        var shallow = Pump(ref state, true, false, 0, 6, Forward);
        Near(shallow.EnergyGain, 3.8181818181818183, "shallow slope reduces pump energy");
        Near(shallow.TangentSpeed, 6.605782590758164, "shallow-pump speed");

        foreach (double slope in new[] { 0d, -7d, 15d })
        {
            state = default(SkatePumpState);
            for (int i = 0; i < 100; i++)
                Near(Pump(ref state, true, i % 2 == 0, slope, 6, Forward).EnergyGain, 0, "unqualified button spam");
        }
        state = default(SkatePumpState);
        for (int i = 0; i < 100; i++)
            Near(Pump(ref state, true, i % 2 == 0, -35, 6, Forward).EnergyGain, 0, "constant steep release never awards");
        Near(Pump(ref state, true, false, 0, 6, Forward).EnergyGain, 0, "early releases cannot bank a charge");
        state = Charge();
        Pump(ref state, true, true, 0, 6, Forward);
        for (int i = 0; i < 31; i++) Pump(ref state, true, true, 0, 6, Forward);
        Near(Pump(ref state, true, false, 0, 6, Forward).EnergyGain, 0, "bottom timeout cancels charge");
        for (int i = 0; i < 8; i++) Pump(ref state, true, true, -35, 6, Forward);
        Near(Pump(ref state, true, false, 0, 6, Forward).EnergyGain, 0, "timeout does not create a new geometric pass");

        state = Charge();
        for (int i = 0; i < 20; i++) Pump(ref state, true, true, 0, 6, Forward);
        Near(Pump(ref state, true, false, 0, 6, Forward).EnergyGain, 12, "charge survives six-metre flat at six m/s");
        foreach (var direction in new[] { new SkateVector(0, 0, -1), new SkateVector(1, 0, 0) })
        {
            state = Charge();
            var turn = Pump(ref state, true, true, 0, 6, direction);
            Check(turn.Reset && !turn.Awarded, "physical reversal or ninety-degree turn resets pass");
            Near(Pump(ref state, true, false, 0, 6, direction).EnergyGain, 0, "turn cannot spend old charge");
        }
        state = Charge();
        Pump(ref state, true, true, -35, 6, Rotate(Forward, 60));
        Check(Pump(ref state, true, true, -35, 6, Rotate(Forward, 120)).Reset, "cumulative travel turn resets");
        foreach (bool supported in new[] { false, true })
        {
            state = Charge();
            var reset = Pump(ref state, supported, true, -35, supported ? 0 : 6, new SkateVector());
            Check(reset.Reset && !reset.Awarded, "air/stop clears pass");
            Near(Pump(ref state, true, false, 0, 6, Forward).EnergyGain, 0, "reset loses prior charge");
        }
        state = Charge();
        SkatePumpResult refused;
        string error;
        Check(!SkateRidePhysics.TryPump(state, new SkatePumpInput(true, false, 0, 6, Forward), 0,
                                       out refused, out error) && !string.IsNullOrEmpty(error), "invalid step refused");
        Check(refused.State.Equals(state) && !refused.Awarded && !refused.Reset && refused.EnergyGain == 0, "invalid pump is atomic");
        Console.WriteLine("skate_transition_pumping: " + (checks - start) + " checks passed");
    }

    private static SkateSlopeResult Slope(double angle, double speed, double dt = 0.05, double gravity = 16)
    {
        SkateSlopeResult result;
        string error;
        Check(SkateRidePhysics.TrySlope(new SkateSlopeInput(Normal(angle), Forward, speed, gravity), dt,
                                       out result, out error) && error == null, "valid slope");
        return result;
    }
    private static void DropInAndLaunch()
    {
        int start = checks;
        var flat = Slope(0, 6);
        Vector(flat.Velocity, new SkateVector(0, 0, 6), "flat full velocity");
        var up = Slope(35, 6);
        Near(up.GravityAcceleration, -9.177222981616737, "uphill gravity along tangent");
        Near(up.SignedTangentSpeed, 5.541138850919163, "uphill slows");
        Vector(up.Velocity, new SkateVector(0, 3.178266675436544, 4.539035217419587), "uphill vector");
        var down = Slope(-35, 6);
        Near(down.SignedTangentSpeed, 6.458861149080837, "drop-in accelerates");
        Vector(down.Velocity, new SkateVector(0, -3.704650560776009, 5.290789314048315), "drop-in vector");
        var reverse = Slope(35, 0.1);
        Near(reverse.SignedTangentSpeed, -0.3588611490808369, "stall naturally reverses");
        Near(reverse.SignedSlopeDegrees, -35, "reversed travel is downhill");
        Vector(reverse.Velocity, new SkateVector(0, -0.2058342990346279, -0.2939618438854642), "reverse vector");
        var tailFirst = Slope(35, -6);
        Check(tailFirst.SignedTangentSpeed < -6 && tailFirst.SignedSlopeDegrees < 0, "tail-first downhill gains speed");

        foreach (double angle in new[] { -35d, 0d, 35d })
        {
            double speed = 6, height = 0;
            for (int i = 0; i < 20; i++)
            {
                var step = Slope(angle, speed, 0.025);
                height += (speed + step.SignedTangentSpeed) * 0.5 * 0.025 * step.Tangent.Y;
                speed = step.SignedTangentSpeed;
            }
            Near(speed, 6 - 16 * Math.Sin(angle * Math.PI / 180) * 0.5, "partitioned gravity speed");
            Near(speed * speed / 2 + 16 * height, 18, "unassisted slope conserves mechanical energy");
        }
        SkateSlopeResult cross;
        string error;
        double radians = 35 * Math.PI / 180;
        Check(SkateRidePhysics.TrySlope(new SkateSlopeInput(new SkateVector(Math.Sin(radians), Math.Cos(radians), 0),
                        Forward, 6), 0.05, out cross, out error), "cross-slope accepted");
        Near(cross.SignedSlopeDegrees, 0, "cross-slope is not ascent");
        Near(cross.GravityAcceleration, 0, "cross-slope has no along-travel gravity");
        Vector(cross.Velocity, new SkateVector(0, 0, 6), "heading retained on cross-slope");
        foreach (double yaw in new[] { 30d, 90d, 187d, 270d })
        {
            SkateSlopeResult rotated;
            Check(SkateRidePhysics.TrySlope(new SkateSlopeInput(Rotate(Normal(35), yaw), Rotate(Forward, yaw), 6),
                                           0.05, out rotated, out error), "rotated slope accepted");
            Vector(rotated.Velocity, Rotate(up.Velocity, yaw), "slope rotational covariance");
            Near(rotated.SignedTangentSpeed, up.SignedTangentSpeed, "rotated magnitude");
        }

        SkateVector uphillVelocity = new SkateVector(0, 3.441458618106276, 4.914912265733951);
        SkateLaunchResult launch;
        Check(SkateRidePhysics.TryLaunch(new SkateLaunchInput(uphillVelocity, 0), out launch, out error), "passive exit");
        Vector(launch.Velocity, uphillVelocity, "exit preserves entire tangent velocity");
        Check(SkateRidePhysics.TryLaunch(new SkateLaunchInput(uphillVelocity, 6), out launch, out error), "ramp pop");
        Vector(launch.Velocity, new SkateVector(0, 9.441458618106276, 4.914912265733951), "vertical pop retains ramp rise");
        Near(launch.Velocity.Y * launch.Velocity.Y / 32, 2.785660651169165, "ramp pop ideal apex");
        foreach (int sign in new[] { -1, 1 })
        foreach (double pop in new[] { 0d, 6 })
        {
            var steep = new SkateVector(0, 9.271838545667875, sign * 3.74606593415912);
            Check(SkateRidePhysics.TryLaunch(new SkateLaunchInput(steep, pop), out launch, out error), "68 degree transition exit");
            Near(launch.Velocity.Z, sign * 3.74606593415912, "steep exit retains forward/reverse tangent travel");
            Near(launch.Velocity.Y, 9.271838545667875 + pop, "steep exit retains tangent rise plus pop");
        }
        Check(SkateRidePhysics.TryLaunch(new SkateLaunchInput(new SkateVector(0, 0, 6), 6), out launch, out error), "flat pop");
        Vector(launch.Velocity, new SkateVector(0, 6, 6), "flat pop");
        Near(2 * launch.Velocity.Y / 16, 0.75, "flat ideal flight time");
        Check(SkateRidePhysics.TryLaunch(new SkateLaunchInput(new SkateVector(0, -3.441458618106276, 4.914912265733951), 6),
                                        out launch, out error), "downhill pop");
        Vector(launch.Velocity, new SkateVector(0, 2.558541381893724, 4.914912265733951), "pop retains inherited descent");
        Console.WriteLine("skate_drop_in_and_launch: " + (checks - start) + " checks passed");
    }

    private static SkateLandingResult Land(SkateVector velocity, SkateVector normal, SkateVector nose,
                                           double maximumImpact = 12, double fallToRoll = 0.8)
    {
        SkateLandingResult result;
        string error;
        Check(SkateRidePhysics.TryLand(new SkateLandingInput(velocity, normal, nose, maximumImpact, fallToRoll),
                                      out result, out error) && error == null, "valid landing");
        Check(result.RollVelocity.LengthSquared <= velocity.LengthSquared + Tolerance, "landing cannot create kinetic energy");
        return result;
    }
    private static void SurfaceLanding()
    {
        int start = checks;
        foreach (double angle in new[] { -60d, -35d, 0d, 35d, 60d })
        foreach (double yaw in new[] { 0d, 30d, 90d, 187d })
        foreach (double weight in new[] { 0d, 0.8d, 1d })
        {
            double radians = angle * Math.PI / 180;
            var normal = Rotate(Normal(angle), yaw);
            var nose = Rotate(Forward, yaw);
            var tangentVelocity = Rotate(new SkateVector(0, 6 * Math.Sin(radians), 6 * Math.Cos(radians)), yaw);
            var current = tangentVelocity;
            for (int seam = 0; seam < 20; seam++)
            {
                var landing = Land(current, normal, nose, 12, weight);
                Near(landing.NormalImpact, 0, "tangent recontact has no impact");
                Near(landing.SignedTangentSpeed, 6, "seam recontact preserves tangent speed at every fall weight");
                Vector(landing.RollVelocity, tangentVelocity, "seam recontact preserves tangent velocity");
                current = landing.RollVelocity;
            }
            var reversed = Land(tangentVelocity, normal, nose * -1, 12, weight);
            Near(reversed.SignedTangentSpeed, -6, "tangent nose reversal changes only signed speed");
            Vector(reversed.RollVelocity, tangentVelocity, "reversed deck preserves tangent travel");
            var normalOnly = Land(normal * -10, normal, nose, 12, weight);
            Near(normalOnly.NormalImpact, 10, "pure normal impact stays independent of damping");
            Vector(normalOnly.RollVelocity, new SkateVector(), "normal impact cannot invent surface roll");
        }
        var velocity = new SkateVector(0, -8, 6);
        var straight = Land(velocity, Up, Forward);
        Near(straight.NormalImpact, 8, "flat impact"); Near(straight.SignedTangentSpeed, 6, "aligned speed retained");
        Vector(straight.RollVelocity, new SkateVector(0, 0, 6), "aligned roll");
        var backwards = Land(velocity, Up, new SkateVector(0, 0, -1));
        Near(backwards.SignedTangentSpeed, -6, "backwards signed roll");
        Vector(backwards.RollVelocity, straight.RollVelocity, "switch-equivalent keeps physical travel");
        var sideways = Land(velocity, Up, new SkateVector(1, 0, 0));
        Near(sideways.SignedTangentSpeed, 0, "sideways scrubs roll"); Near(sideways.LateralSlipSpeed, 6, "sideways lateral loss");
        Check(!sideways.Bailed, "yaw alone has no extra bail threshold");
        var diagonal = Land(velocity, Up, new SkateVector(1, 0, 1));
        Near(diagonal.SignedTangentSpeed, 4.242640687119286, "diagonal retained component");
        Vector(diagonal.RollVelocity, new SkateVector(3, 0, 3), "diagonal roll");
        Near(diagonal.LateralSlipSpeed, 4.242640687119286, "diagonal slip");
        var uphill = Land(velocity, Normal(35), Forward);
        Near(uphill.NormalImpact, 9.99467497241821, "uphill whole-vector impact");
        Near(uphill.SignedTangentSpeed, 0.3263007749255822, "opposing fall damping cannot boost uphill roll");
        Vector(uphill.RollVelocity, new SkateVector(0, 0.1871584356604002, 0.2672899468333728), "uphill roll");
        var downhill = Land(velocity, Normal(-35), Forward);
        Near(downhill.NormalImpact, 3.111757736205658, "downhill whole-vector impact");
        Near(downhill.SignedTangentSpeed, 9.067748511747602, "only additional fall is damped in downhill roll");
        Vector(downhill.RollVelocity, new SkateVector(0, -5.201046877095691, 7.427864730496512), "downhill roll");
        Check(!uphill.Bailed && !downhill.Bailed, "ordinary slope landings below bail threshold");
        var slopeBackwards = Land(velocity, Normal(-35), new SkateVector(0, 0, -1));
        Vector(slopeBackwards.RollVelocity, downhill.RollVelocity, "slope tail-first keeps vector");
        Near(slopeBackwards.SignedTangentSpeed, -downhill.SignedTangentSpeed, "slope tail-first sign");
        Near(Land(velocity, Normal(35), Forward, 12, 1).SignedTangentSpeed, 0.3263007749255822, "undamped uphill vertical conversion");
        Near(Land(velocity, Normal(-35), Forward, 12, 1).SignedTangentSpeed, 9.503523756542318, "undamped downhill vertical conversion");
        Near(Land(velocity, Normal(-35), Forward, 12, 0).SignedTangentSpeed, 7.324647532568738, "zero fall conversion retains surface-following speed");
        foreach (double weight in new[] { 0d, 0.8d, 1d })
        {
            var fall = Land(new SkateVector(0, -8, 0), Normal(-35), Forward, 12, weight);
            Near(fall.SignedTangentSpeed, 4.588611490808368 * weight, "pure vertical downfall conversion follows weight");
            var separating = Land(Normal(-35) * 10 + new SkateVector(0, -3.441458618106276, 4.914912265733951),
                                  Normal(-35), Forward, 12, weight);
            Near(separating.NormalImpact, 0, "outward velocity is not a falling impact");
            Near(separating.SignedTangentSpeed, 6, "separating contact does not damp its tangent");
        }
        Near(Land(new SkateVector(0, -9, 6), Normal(35), Forward).SignedTangentSpeed, 0, "damping cannot reverse projected travel");
        var crossNormal = new SkateVector(0.4, 0.8, Math.Sqrt(0.2));
        var lateralImpact = new SkateVector(1.499090833947008, -10.094891746265528, -5.643216042537819);
        var crossLanding = Land(lateralImpact, crossNormal, Forward);
        Near(crossLanding.SignedTangentSpeed, 0, "fall damping cannot turn lateral motion into board roll");
        Near(crossLanding.LateralSlipSpeed, 5.127128439056031, "cross-slope keeps the retained lateral direction");
        Check(!Land(new SkateVector(0, -12, 0), Up, Forward).Bailed, "exact maximum impact accepted");
        Check(Land(new SkateVector(0, -12.00001, 0), Up, Forward).Bailed, "over maximum impact bails");
        Check(Land(velocity, Normal(35), Forward, 9).Bailed && !Land(velocity, Normal(-35), Forward, 9).Bailed,
              "impact against surface decides bail");
        foreach (double yaw in new[] { 30d, 90d, 187d, 270d })
        {
            var rotated = Land(Rotate(velocity, yaw), Rotate(Normal(-35), yaw), Rotate(Forward, yaw));
            Vector(rotated.RollVelocity, Rotate(downhill.RollVelocity, yaw), "landing rotational covariance");
            Near(rotated.NormalImpact, downhill.NormalImpact, "rotated impact");
            Near(rotated.SignedTangentSpeed, downhill.SignedTangentSpeed, "rotated roll speed");
        }
        Console.WriteLine("skate_surface_landing: " + (checks - start) + " checks passed");
    }

    private static int Main()
    {
        string path = typeof(SkateRidePhysics).Assembly.Location;
        using (var file = File.OpenRead(path))
        using (var sha = SHA256.Create())
            Console.WriteLine("Actual shared DLL: " + path + "\nSHA-256: " + Convert.ToHexString(sha.ComputeHash(file)).ToLowerInvariant());
        TransitionPumping(); DropInAndLaunch(); SurfaceLanding();
        Console.WriteLine("PASS: " + checks + " approved ride-physics checks");
        return 0;
    }
}
