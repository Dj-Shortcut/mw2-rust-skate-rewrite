using System;
using System.IO;
using System.Security.Cryptography;
using Shortcut.RustMod;

internal static class SkateServerMovementPolicyScenarios
{
    private static int checks;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new InvalidOperationException(message); }
    private static void Near(double actual, double expected, string message)
    { Check(!double.IsNaN(actual) && !double.IsInfinity(actual) && Math.Abs(actual - expected) <= 1e-9, message); }
    private static bool Bit(double actual, double expected)
    { return BitConverter.DoubleToInt64Bits(actual) == BitConverter.DoubleToInt64Bits(expected); }

    private static bool Allow(double dt, double path, double maximumSpeed, SkateSessionReason expected)
    {
        bool allowed; SkateSessionReason reason;
        Check(SkateMovementPolicy.TryAllow(dt, path, maximumSpeed, out allowed, out reason), "finite movement request accepted");
        Check(reason == expected && allowed == (expected == SkateSessionReason.None), "movement decision and reason agree");
        return allowed;
    }

    private static void Refuse(double dt, double path, double maximumSpeed, SkateSessionReason expected)
    {
        bool allowed; SkateSessionReason reason;
        Check(!SkateMovementPolicy.TryAllow(dt, path, maximumSpeed, out allowed, out reason) && !allowed && reason == expected,
            "malformed movement request denied with exact reason");
    }

    private static void Accumulate(SkateMovementPoint from, SkateMovementPoint to, ref double horizontal, ref double vertical)
    {
        double nextHorizontal, nextVertical;
        Check(SkateMovementPolicy.TryAccumulate(from, to, horizontal, vertical, out nextHorizontal, out nextVertical),
            "finite full segment accumulated");
        horizontal = nextHorizontal; vertical = nextVertical;
    }

    private static void RejectSegment(SkateMovementPoint from, SkateMovementPoint to, double horizontal, double vertical)
    {
        double nextHorizontal, nextVertical;
        Check(!SkateMovementPolicy.TryAccumulate(from, to, horizontal, vertical, out nextHorizontal, out nextVertical),
            "invalid segment/path is refused");
        Check(Bit(nextHorizontal, horizontal) && Bit(nextVertical, vertical), "invalid accumulation preserves both prior path bits");
    }

    private static void TickCountsAndCoordinates()
    {
        int[][] fixtures = { new[] { int.MinValue, 32, 0 }, new[] { -1, 32, 0 }, new[] { 0, 32, 0 },
            new[] { 1, 32, 1 }, new[] { 31, 32, 1 }, new[] { 32, 32, 0 }, new[] { 33, 32, 0 },
            new[] { 1, 1, 0 }, new[] { 1, 2, 1 }, new[] { 2, 2, 0 }, new[] { int.MaxValue - 1, int.MaxValue, 1 },
            new[] { int.MaxValue, int.MaxValue, 0 }, new[] { 1, int.MaxValue, 1 }, new[] { 0, 0, 0 },
            new[] { 1, 0, 0 }, new[] { 1, -1, 0 }, new[] { 1, int.MinValue, 0 }, new[] { int.MinValue, int.MinValue, 0 } };
        foreach (var fixture in fixtures)
            Check(SkateMovementPolicy.ValidTickCount(fixture[0], fixture[1]) == (fixture[2] == 1),
                "literal tick-count endpoint/buffer boundary fixture");
        var zero = default(SkateMovementPoint);
        var upper = new SkateMovementPoint(100000, 100000, 100000);
        var lower = new SkateMovementPoint(-100000, -100000, -100000);
        double horizontal = 0, vertical = 0;
        Accumulate(upper, lower, ref horizontal, ref vertical);
        Near(horizontal, Math.Sqrt(80000000000), "inclusive world-coordinate extrema stay finite");
        Near(vertical, 200000, "full vertical delta at coordinate extrema");
        foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 100000.0001, -100000.0001 })
            for (int axis = 0; axis < 3; axis++)
            {
                var bad = new SkateMovementPoint(axis == 0 ? value : 0, axis == 1 ? value : 0, axis == 2 ? value : 0);
                RejectSegment(zero, bad, 2, 3);
                RejectSegment(bad, zero, 2, 3);
            }
        foreach (double path in new[] { -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            RejectSegment(zero, zero, path, 3);
            RejectSegment(zero, zero, 2, path);
        }
        horizontal = vertical = double.MaxValue;
        Accumulate(zero, new SkateMovementPoint(1, 1, 1), ref horizontal, ref vertical);
        Check(horizontal == double.MaxValue && vertical == double.MaxValue, "huge finite prior paths remain finite under rounded small increments");
    }

    private static void FullPathAndHorizontalPolicy()
    {
        double horizontal = 2.5, vertical = 1;
        var from = new SkateMovementPoint(10, -3, 20); var to = new SkateMovementPoint(13, 9, 24);
        Accumulate(from, to, ref horizontal, ref vertical);
        Near(horizontal, 7.5, "3-4-5 horizontal length adds to prior path");
        Near(vertical, 13, "vertical length adds absolute height change");
        horizontal = vertical = 0;
        Accumulate(from, to, ref horizontal, ref vertical);
        Accumulate(to, from, ref horizontal, ref vertical);
        Near(horizontal, 10, "out-and-back batch sums travel rather than endpoint displacement");
        Near(vertical, 24, "vertical path preserves both directions");
        Check(Allow(0.5, horizontal, 25, SkateSessionReason.None), "complete path inside horizontal cap accepted");
        Check(!Allow(0.25, horizontal, 25, SkateSessionReason.HorizontalSpeed), "zero-net-displacement overspeed batch denied");
        horizontal = vertical = 0;
        var bottom = new SkateMovementPoint(0, -100000, 0); var top = new SkateMovementPoint(0, 100000, 0);
        Accumulate(bottom, top, ref horizontal, ref vertical);
        Check(horizontal == 0 && vertical == 200000 && Allow(0.03125, horizontal, 25, SkateSessionReason.None),
            "finite vertical motion does not become a horizontal speed gate");
        Check(!SkateMovementPolicy.CanExemptFly(true, 0.03125, vertical),
            "world-spanning vertical motion never exempts native fly checks even for a trusted player");
        horizontal = vertical = 0;
        var zero = default(SkateMovementPoint); var fast = new SkateMovementPoint(0.8, 0, 0);
        Accumulate(zero, fast, ref horizontal, ref vertical);
        Check(!Allow(0.01, horizontal, 25, SkateSessionReason.HorizontalSpeed), "early segment already exceeds cap");
        RejectSegment(fast, new SkateMovementPoint(0, double.NaN, 0), horizontal, vertical);
        Check(horizontal == 0.8 && vertical == 0, "invalid later coordinate is checked after an overspeed prefix");
        horizontal = vertical = 0;
        foreach (var segment in new[] { new SkateMovementPoint(0.3, 50, 0.4), new SkateMovementPoint(0.6, -50, 0.8),
                                       new SkateMovementPoint(0.9, 25, 1.2) })
        {
            Accumulate(zero, segment, ref horizontal, ref vertical);
            zero = segment;
        }
        Near(horizontal, 1.5, "all segments contribute horizontal travel despite large vertical prefix");
        Near(vertical, 225, "complete batch keeps diagnostic vertical path");
    }

    private static void NumberBoundaries()
    {
        foreach (double dt in new[] { double.Epsilon, 0.0001, 0.03125, 0.5 })
            foreach (double maximum in new[] { 1.0, 25, 50 })
            {
                Check(Allow(dt, 0, maximum, SkateSessionReason.None), "stationary batch accepted at valid scalar boundaries");
                Check(Allow(dt, maximum * dt, maximum, SkateSessionReason.None), "exact horizontal cap inclusive");
                Check(!Allow(dt, maximum * dt + 0.000001, maximum, SkateSessionReason.HorizontalSpeed),
                    "positive overspeed margin denied");
            }
        foreach (double dt in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1, 0, 0.500000001 })
            Refuse(dt, 0, 25, SkateSessionReason.InvalidDelta);
        foreach (double path in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -0.000001 })
            Refuse(0.1, path, 25, SkateSessionReason.InvalidHorizontalPath);
        foreach (double maximum in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1, 0, 0.999999, 50.000001 })
            Refuse(0.1, 0, maximum, SkateSessionReason.InvalidMaximumSpeed);
        Check(!Allow(0.5, double.MaxValue, 50, SkateSessionReason.HorizontalSpeed), "huge finite travel is denied without arithmetic overflow");
    }

    private static void AccessAndFreshness()
    {
        for (int mask = 0; mask < 256; mask++)
        {
            bool actual = SkateMovementPolicy.CanFilter((mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0,
                (mask & 8) != 0, (mask & 16) != 0, (mask & 32) != 0, (mask & 64) != 0, (mask & 128) != 0);
            Check(actual == (mask == 127), "all fresh guards required and explicit opt-out wins");
        }
        for (int mask = 0; mask < 128; mask++)
        {
            bool eligible = SkateSessionBounds.Eligible((mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0,
                (mask & 8) != 0, (mask & 16) != 0, (mask & 32) != 0, (mask & 64) != 0);
            Check(eligible == (mask == 5), "connected living human must be awake unmounted unwounded and dry");
        }
        Check(SkateMovementPolicy.CanFilter(true, true, true, true, true, true, true, false), "fully eligible fresh batch grants exemption");
        Check(!SkateMovementPolicy.CanFilter(true, true, false, true, true, true, true, false), "stale movement result cannot exempt a new batch");
        Check(!SkateMovementPolicy.CanFilter(true, true, true, false, true, true, true, false), "reused identity cannot inherit old connection result");
        Check(!SkateMovementPolicy.CanFilter(true, true, true, true, true, true, false, false), "revoked permission is checked at filtering time");
        Check(!SkateMovementPolicy.CanFilter(true, true, true, true, true, true, true, true), "fresh opt-out disables filtering");
        Check(!SkateMovementPolicy.CanFilter(false, true, true, true, true, true, true, false), "unavailable configuration cannot grant exemption");
        Check(!SkateMovementPolicy.CanFilter(true, true, true, true, true, false, true, false), "newly ineligible player cannot inherit earlier acceptance");
    }

    private static void LegacyHistoryCannotStarveMovement()
    {
        var bounds = new SkateSessionBounds(); var rearm = new SkateSessionRearm();
        Check(bounds.Begin(10, true), "legacy supported session starts");
        int firstAirtimeRefusal = 0;
        for (int tick = 1; tick <= 96; tick++)
        {
            double now = 10 + tick / 32.0;
            bool legacy = bounds.Observe(now, 1.0 / 32, 5.0 / 32, 0, false, 25, 2);
            if (!legacy && firstAirtimeRefusal == 0)
            {
                firstAirtimeRefusal = tick;
                Check(bounds.LastObservation.Reason == SkateSessionReason.Airtime, "old air timer disarms the unsupported trajectory");
            }
            bool fresh = Allow(1.0 / 32, 5.0 / 32, 25, SkateSessionReason.None);
            Check(SkateMovementPolicy.CanFilter(true, true, true, true, fresh, true, true, false),
                "fresh bounded movement remains filterable regardless of old air history");
            if (!bounds.Active)
            {
                Check(!rearm.Observe(now, 1.0 / 32, 5.0 / 32, 0, false, 25, 1.5) && rearm.Seconds == 0 &&
                    rearm.LastObservation.Reason == SkateSessionReason.Unsupported, "legacy unsupported rearm remains starved");
            }
        }
        Check(firstAirtimeRefusal == 65 && !bounds.Active && rearm.Seconds == 0, "actual old helpers reproduce the two-second starvation boundary");
        rearm.Reset();
        for (int tick = 1; tick <= 64; tick++)
        {
            Check(!rearm.Observe(20 + tick / 32.0, 1.0 / 32, 5.0 / 32, 0, true, 25, 1.5),
                "repeated diagnostic resets prevent legacy warmup completion");
            if (tick % 2 == 0) rearm.Reset();
            bool fresh = Allow(1.0 / 32, 5.0 / 32, 25, SkateSessionReason.None);
            Check(SkateMovementPolicy.CanFilter(true, true, true, true, fresh, true, true, false),
                "warmup/discontinuity diagnostic resets cannot become authorization gates");
        }
        Check(rearm.Seconds == 0, "legacy reset fixture ends without accumulated warmup");
        bounds = new SkateSessionBounds();
        Check(bounds.Begin(30, true) && !bounds.Observe(31, 0.1, 0.5, 0, true, 25, 2) &&
            bounds.LastObservation.Reason == SkateSessionReason.BatchGap, "legacy inter-batch clock gap disarms");
        Check(Allow(0.1, 0.5, 25, SkateSessionReason.None), "fresh scalar batch ignores previous clock history");
    }

    private static void TrustedFlyPolicy()
    {
        Check(SkateMovementPolicy.MaximumFlyVerticalSpeed == 200, "explicit full vertical batch rate budget");
        foreach (double dt in new[] { double.Epsilon, 0.0001, 0.03125, 0.5 })
        {
            foreach (double rate in new[] { 0.0, 6, 19, 30, 45.869597533611945, 50, 150, 200 })
            {
                double vertical = rate * dt;
                Check(SkateMovementPolicy.CanExemptFly(true, dt, vertical), "trusted stationary, pop, steep transition, lip assist and capped fall rates accepted");
                Check(!SkateMovementPolicy.CanExemptFly(false, dt, vertical), "ordinary permitted movement cannot authorize flight");
            }
            Check(!SkateMovementPolicy.CanExemptFly(true, dt, 200 * dt + 0.000001), "vertical rate excess keeps native fly result");
        }
        foreach (double dt in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1, 0, 0.500000001 })
            Check(!SkateMovementPolicy.CanExemptFly(true, dt, 0), "malformed delta cannot exempt flight");
        foreach (double path in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -0.000001, double.MaxValue })
            Check(!SkateMovementPolicy.CanExemptFly(true, 0.1, path), "malformed or enormous vertical path cannot exempt flight");
        double horizontal = 0, verticalPath = 0;
        var bottom = default(SkateMovementPoint); var top = new SkateMovementPoint(0, 4, 0);
        Accumulate(bottom, top, ref horizontal, ref verticalPath);
        Accumulate(top, bottom, ref horizontal, ref verticalPath);
        Near(verticalPath, 8, "fly budget uses full out-and-back vertical travel");
        Check(!SkateMovementPolicy.CanExemptFly(true, 0.03125, verticalPath), "zero net vertical displacement cannot hide an excessive path");
        double steepPop = 13 * Math.Sqrt(1 - 0.31 * 0.31) / 0.31 + 6;
        Near(steepPop, 45.869597533611945, "horizontal pace and steep surface preserve launch vertical component");
        Check(SkateMovementPolicy.CanExemptFly(true, 0.03125, steepPop / 32), "source-valid steep pop remains fly-exempt for a trusted player");
        Check(SkateMovementPolicy.CanExemptFly(true, 0.003, 0.25 * 0.6), "source-valid short-step lip lift remains fly-exempt");
        Check(SkateMovementPolicy.CanExemptFly(true, 0.001, 0.25 * 0.6), "lip lift at its minimum denominator remains fly-exempt");
        for (int mask = 0; mask < 256; mask++)
        {
            bool movement = SkateMovementPolicy.CanFilter((mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0,
                (mask & 8) != 0, (mask & 16) != 0, (mask & 32) != 0, (mask & 64) != 0, (mask & 128) != 0);
            Check((movement && SkateMovementPolicy.CanExemptFly(true, 0.1, 3)) == (mask == 127),
                "fly exemption retains every fresh movement guard");
            Check(!(movement && SkateMovementPolicy.CanExemptFly(false, 0.1, 3)), "general permission never bypasses separate fly authority");
        }
        for (int tick = 1; tick <= 320; tick++)
            Check(SkateMovementPolicy.CanExemptFly(true, 1.0 / 32, 30.0 / 32),
                "ten-second trusted fall has no inherited airtime or warmup cutoff");
        Check(!SkateMovementPolicy.CanExemptFly(false, 0.1, 1) && SkateMovementPolicy.CanExemptFly(true, 0.1, 1) &&
            !SkateMovementPolicy.CanExemptFly(false, 0.1, 1), "fresh trusted grant and revocation take effect without activation history");
        for (int i = 0; i < 200; i++)
            Check(SkateMovementPolicy.CanExemptFly(true, 0.1, 3), "warm fly decision");
        long before = GC.GetAllocatedBytesForCurrentThread(); bool correct = true;
        for (int i = 0; i < 20000; i++)
            correct &= SkateMovementPolicy.CanExemptFly(true, 0.1, 3) && !SkateMovementPolicy.CanExemptFly(false, 0.1, 3) &&
                !SkateMovementPolicy.CanExemptFly(true, 0.1, 21);
        Check(correct && GC.GetAllocatedBytesForCurrentThread() - before == 0, "warm fly grant and denial decisions allocate zero bytes");
    }

    private static void DeterminismAndAllocation()
    {
        var from = new SkateMovementPoint(1, 2, 3); var to = new SkateMovementPoint(1.3, 5, 3.4);
        for (int i = 0; i < 128; i++)
        {
            double firstHorizontal, firstVertical, secondHorizontal, secondVertical;
            Check(SkateMovementPolicy.TryAccumulate(from, to, 0, 0, out firstHorizontal, out firstVertical) &&
                SkateMovementPolicy.TryAccumulate(from, to, 0, 0, out secondHorizontal, out secondVertical) &&
                Bit(firstHorizontal, secondHorizontal) && Bit(firstVertical, secondVertical), "identical segment has deterministic path bits");
        }
        double horizontal, vertical; bool allowed; SkateSessionReason reason;
        for (int i = 0; i < 200; i++)
            Check(SkateMovementPolicy.TryAccumulate(from, to, 0, 0, out horizontal, out vertical) &&
                SkateMovementPolicy.TryAllow(0.1, horizontal, 25, out allowed, out reason), "warm pure movement helpers");
        long before = GC.GetAllocatedBytesForCurrentThread(); bool success = true;
        for (int i = 0; i < 20000; i++)
            success &= SkateMovementPolicy.TryAccumulate(from, to, 0, 0, out horizontal, out vertical) &&
                SkateMovementPolicy.TryAllow(0.1, horizontal, 25, out allowed, out reason) && allowed &&
                SkateMovementPolicy.CanFilter(true, true, true, true, allowed, true, true, false);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(success && allocated == 0, "warmed successful pure movement decisions allocate zero bytes");
        before = GC.GetAllocatedBytesForCurrentThread(); bool rejected = true;
        for (int i = 0; i < 20000; i++)
            rejected &= !SkateMovementPolicy.TryAllow(double.NaN, 0, 25, out allowed, out reason) && !allowed &&
                !SkateMovementPolicy.CanFilter(true, true, false, true, true, true, true, false);
        Check(rejected && GC.GetAllocatedBytesForCurrentThread() - before == 0, "warmed malformed/stale requests allocate zero bytes");
    }

    public static int Main(string[] args)
    {
        try
        {
            Check(args.Length <= 1, "optional argument is expected actual-plugin SHA-256");
            string library = typeof(SkateMovementPolicy).Assembly.Location, digest;
            using (var hash = SHA256.Create())
                digest = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(library))).Replace("-", "").ToLowerInvariant();
            Console.WriteLine("Actual plugin: " + library + "\nSHA-256: " + digest);
            if (args.Length == 1) Check(string.Equals(args[0], digest, StringComparison.OrdinalIgnoreCase), "actual plugin hash pinned");
            TickCountsAndCoordinates(); FullPathAndHorizontalPolicy(); NumberBoundaries(); AccessAndFreshness();
            LegacyHistoryCannotStarveMovement(); TrustedFlyPolicy(); DeterminismAndAllocation();
            Console.WriteLine("skate_server_movement_policy: " + checks + " checks passed");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
