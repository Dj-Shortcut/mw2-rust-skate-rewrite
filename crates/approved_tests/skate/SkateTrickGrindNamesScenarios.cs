using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Shortcut.RustMod;

internal static class SkateTrickGrindNamesScenarios
{
    private static readonly string[] Kinds = { "None", "FiftyFifty", "FiveO", "Nosegrind", "Boardslide", "Lipslide",
        "Noseslide", "Tailslide", "Smith", "Feeble", "Crooked", "FiftyFiftyStall", "NoseStall", "TailStall",
        "BoardStall", "Handplant", "Bluntslide", "Nosebluntslide", "Darkslide" };
    private static readonly string[] Names = { "Grind", "50-50", "5-0", "Nosegrind", "Boardslide", "Lipslide",
        "Noseslide", "Tailslide", "Smith Grind", "Feeble Grind", "Crooked Grind", "50-50 Stall", "Nose Stall",
        "Tail Stall", "Board Stall", "Handplant", "Bluntslide", "Nosebluntslide", "Darkslide" };
    private static readonly int[] Rates = { 100, 100, 130, 130, 120, 140, 150, 150, 160, 160, 160, 150, 160, 160, 150,
        250, 170, 170, 190 };
    private static readonly ConstructorInfo StateConstructor = typeof(SkateTrickState).GetConstructor(
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(double), typeof(double), typeof(double), typeof(double), typeof(double), typeof(bool),
            typeof(bool), typeof(SkateGrabKind), typeof(bool), typeof(long), typeof(long), typeof(int), typeof(double),
            typeof(bool), typeof(bool), typeof(SkatePopKind), typeof(bool), typeof(SkateGrindKind), typeof(bool), typeof(bool) }, null);
    private const long ScoreLimit = 1000000000000000;
    private static int checks;

    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new InvalidOperationException(message); }

    private static string ExpectedName(int kind, bool frontside)
    { return kind == 0 ? Names[0] : (frontside ? "Frontside " : "Backside ") + Names[kind]; }

    private static SkateTrickInput Input(SkateGrindKind kind, bool frontside, bool grinding,
                                         bool airborne = false, SkatePopKind pop = SkatePopKind.None, double impact = 0)
    { return new SkateTrickInput(0, 0, 0, SkateGrabKind.None, airborne, grinding, impact,
        false, false, 6, pop, kind, frontside); }

    private static SkateTrickState State(SkateGrindKind kind, bool frontside = false, bool ridingSwitch = false,
                                         double seconds = 1, long combo = 0, long total = 0, int count = 0,
                                         bool grinding = true)
    { return (SkateTrickState)StateConstructor.Invoke(new object[] { 0.0, 0.0, 0.0, seconds, 0.0, false, grinding,
        SkateGrabKind.None, ridingSwitch, combo, total, count, 0.0, false, false, SkatePopKind.None, false,
        kind, frontside, false }); }

    private static SkateTrickResult Step(SkateTrickState state, SkateTrickInput input, double dt = 0.03125)
    {
        SkateTrickResult result; string error;
        Check(SkateTricks.TryStep(state, input, dt, out result, out error) && error == null, "valid trick step");
        return result;
    }

    private static void VocabularyAndCompatibility()
    {
        var values = Enum.GetValues(typeof(SkateGrindKind));
        Check(values.Length == 19, "nineteen append-only grind kinds");
        for (int kind = 0; kind < Kinds.Length; kind++)
        {
            var value = (SkateGrindKind)values.GetValue(kind);
            Check((int)value == kind && Enum.GetName(typeof(SkateGrindKind), value) == Kinds[kind],
                "grind enum identity and value " + Kinds[kind]);
            Check(SkateTricks.GrindRate(value) == Rates[kind], "literal grind rate " + Kinds[kind]);
            Check(SkateTricks.IsStall(value) == (kind >= 11 && kind <= 15), "stall classification " + Kinds[kind]);
            foreach (bool frontside in new[] { false, true })
            {
                string actual = SkateTricks.GrindName(value, frontside);
                Check(actual == ExpectedName(kind, frontside), "literal directional name " + Kinds[kind]);
                Check(object.ReferenceEquals(actual, SkateTricks.GrindName(value, frontside)), "cached directional name identity");
            }
        }
        var legacy = new SkateTrickInput(0, 0, false, false, true, 0);
        Check(legacy.GrindKind == SkateGrindKind.FiftyFifty && !legacy.GrindFrontside, "legacy constructor keeps backside 50-50");
        var state = Step(new SkateTrickState(true), legacy).State;
        var ended = Step(state, new SkateTrickInput(0, 0, false, false, false, 0));
        Check(ended.TrickName == "Backside 50-50" && ended.Switch, "legacy grind preserves switch without renaming");
        Check(ended.Points == 3, "legacy short 50-50 keeps existing rounded score");
    }

    private static void EnterUpdateEndAndScoring()
    {
        for (int kind = 0; kind < Kinds.Length; kind++)
            foreach (bool frontside in new[] { false, true })
                foreach (bool ridingSwitch in new[] { false, true })
                {
                    var value = (SkateGrindKind)kind; var state = new SkateTrickState(ridingSwitch);
                    for (int i = 0; i < 32; i++)
                    {
                        var result = Step(state, Input(value, frontside, true));
                        Check(result.Events == SkateTrickEvents.None && result.TrickName == null && result.Points == 0,
                            "ongoing grind does not finish or bank");
                        Check(result.State.Grinding && !result.State.Airborne && result.State.GrindKind == value &&
                            result.State.GrindFrontside == frontside && result.Switch == ridingSwitch, "grind context retained");
                        Check(result.GrindSeconds == (i + 1) / 32.0 && result.State.GrindSeconds == result.GrindSeconds,
                            "exact binary-duration grind accumulation");
                        state = result.State;
                    }
                    var ended = Step(state, Input(SkateGrindKind.None, !frontside, false));
                    Check(ended.Events == SkateTrickEvents.GrindEnded && ended.TrickName == ExpectedName(kind, frontside),
                        "ending uses stored kind and side");
                    Check(object.ReferenceEquals(ended.TrickName, SkateTricks.GrindName(value, frontside)), "completion reuses cached name");
                    Check(ended.Points == Rates[kind] && ended.State.ComboBasePoints == Rates[kind] &&
                        ended.State.ComboCount == 1 && ended.ComboPoints == Rates[kind] && ended.TotalPoints == 0,
                        "one-second grind scores literal rate into open combo");
                    Check(ended.GrindSeconds == 1 && ended.State.GrindSeconds == 0 && !ended.State.Grinding &&
                        ended.State.GrindKind == SkateGrindKind.None && !ended.State.GrindFrontside,
                        "completion reports duration and clears grind state");
                    Check(ended.Switch == ridingSwitch, "grind completion preserves stance");
                }
        for (int kind = 16; kind <= 18; kind++)
        {
            var entered = Step(new SkateTrickState(false), Input(SkateGrindKind.FiftyFifty, false, true));
            var changed = Step(entered.State, Input((SkateGrindKind)kind, true, true));
            var ended = Step(changed.State, Input(SkateGrindKind.None, false, false));
            Check(ended.TrickName == ExpectedName(kind, true) && ended.GrindSeconds == 0.0625,
                "latest grind kind and side replace earlier contact");
            Check(ended.Points == (kind == 18 ? 12 : 11), "updated contact rate applies to accumulated duration");
        }
    }

    private static void SwitchFakieCombosAndBail()
    {
        for (int kind = 16; kind <= 18; kind++)
            foreach (bool frontside in new[] { false, true })
                foreach (bool ridingSwitch in new[] { false, true })
                    foreach (var pop in new[] { SkatePopKind.Ollie, SkatePopKind.Nollie })
                    {
                        var start = State((SkateGrindKind)kind, frontside, ridingSwitch);
                        var popped = Step(start, Input(SkateGrindKind.None, false, false, true, pop));
                        Check(popped.Events == SkateTrickEvents.GrindEnded && popped.TrickName == ExpectedName(kind, frontside) &&
                            popped.State.Airborne && popped.State.PopSwitch == ridingSwitch, "grind pop preserves aerial stance context");
                        var landed = Step(popped.State, Input(SkateGrindKind.None, false, false));
                        string name = pop == SkatePopKind.Nollie ? (ridingSwitch ? "Fakie Ollie" : "Nollie") :
                            ridingSwitch ? "Switch Ollie" : "Ollie";
                        long landing = ridingSwitch ? 75 : 50;
                        Check(landed.Events == SkateTrickEvents.Landed && landed.TrickName == name && landed.Points == landing,
                            "new grind does not change switch/fakie landing vocabulary");
                        Check(landed.State.ComboBasePoints == Rates[kind] + landing && landed.State.ComboCount == 2 &&
                            landed.ComboPoints == 2 * (Rates[kind] + landing), "landing chains onto grind combo");
                        var state = landed.State;
                        for (int i = 0; i < 47; i++)
                        {
                            var rolling = Step(state, Input(SkateGrindKind.None, false, false));
                            Check(rolling.Events == SkateTrickEvents.None && rolling.TotalPoints == 0, "combo waits for rolling timeout");
                            state = rolling.State;
                        }
                        var banked = Step(state, Input(SkateGrindKind.None, false, false));
                        Check(banked.Events == SkateTrickEvents.ComboBanked && banked.BankedPoints == 2 * (Rates[kind] + landing) &&
                            banked.TotalPoints == banked.BankedPoints && banked.State.ComboCount == 0 && banked.State.ComboBasePoints == 0,
                            "rolling timeout banks and clears combo");

                        SkateTrickResult bailed; string error;
                        Check(SkateTricks.TryBail(popped.State, out bailed, out error) && error == null, "explicit bail accepts new grind history");
                        Check(bailed.Events == SkateTrickEvents.Bailed && bailed.TrickName == "Bail" && bailed.TotalPoints == 0 &&
                            bailed.State.ComboCount == 0 && bailed.State.ComboBasePoints == 0 && !bailed.State.Airborne &&
                            !bailed.State.Grinding && bailed.Switch == ridingSwitch, "bail drops open combo and retains stance");
                        var impact = Step(popped.State, Input(SkateGrindKind.None, false, false, impact: SkateMotion.MaximumLandingFallSpeed + 1));
                        Check(impact.Events == SkateTrickEvents.Bailed && impact.Points == 0 && impact.State.ComboCount == 0 &&
                            impact.State.ComboBasePoints == 0, "hard touchdown loses grind combo");
                    }
    }

    private static void DurationAndScoreBoundaries()
    {
        for (int kind = 0; kind < Kinds.Length; kind++)
        {
            var value = (SkateGrindKind)kind;
            var shortEnd = Step(State(value, seconds: 0), Input(SkateGrindKind.None, false, false));
            Check(shortEnd.Points == (kind >= 11 && kind <= 15 ? 100 : 1), "zero-duration minimum follows unchanged stall classification");
            var longStep = Step(State(value, seconds: 86400), Input(value, true, true));
            Check(longStep.State.GrindSeconds == 86400, "grind duration saturates at one day");
            var longEnd = Step(longStep.State, Input(SkateGrindKind.None, false, false));
            Check(longEnd.Points == 86400L * Rates[kind], "maximum duration scores without overflow");
            var saturated = Step(State(value, combo: ScoreLimit, total: ScoreLimit, count: 16), Input(SkateGrindKind.None, false, false));
            Check(saturated.State.ComboBasePoints == ScoreLimit && saturated.State.ComboCount == 16 &&
                saturated.ComboPoints == ScoreLimit && saturated.TotalPoints == ScoreLimit, "points and multiplier saturate independently");
        }
        foreach (double dt in new[] { 0.0001, 0.05 })
        {
            var result = Step(new SkateTrickState(false), Input(SkateGrindKind.Darkslide, true, true), dt);
            Check(result.State.GrindSeconds == dt, "new kind accepts valid timestep endpoints");
        }
        foreach (var kind in new[] { SkateGrindKind.Bluntslide, SkateGrindKind.Nosebluntslide, SkateGrindKind.Darkslide })
        {
            var low = Step(State(kind, seconds: 0.25), Input(SkateGrindKind.None, false, false));
            var high = Step(State(kind, seconds: 0.75), Input(SkateGrindKind.None, false, false));
            Check(low.Points == (kind == SkateGrindKind.Darkslide ? 48 : 42) &&
                high.Points == (kind == SkateGrindKind.Darkslide ? 142 : 128), "half-point ties round toward even");
        }
    }

    private static void Reject(SkateTrickState state, SkateTrickInput input, double dt = 0.03125)
    {
        SkateTrickResult result; string error;
        Check(!SkateTricks.TryStep(state, input, dt, out result, out error) && error != null, "invalid state/request refused");
        Check(result.State.Equals(state) && result.Events == SkateTrickEvents.None && result.TrickName == null &&
            result.Points == 0 && result.BankedPoints == 0, "refusal preserves state and emits no event or award");
    }

    private static void InvalidKindsAndStates()
    {
        foreach (int invalid in new[] { int.MinValue, -1, 19, 255, int.MaxValue })
            foreach (bool frontside in new[] { false, true })
            {
                var kind = (SkateGrindKind)invalid;
                Check(SkateTricks.GrindRate(kind) == 100 && SkateTricks.GrindName(kind, frontside) == "Grind" &&
                    !SkateTricks.IsStall(kind), "invalid lookup kind retains generic fallback");
                var sanitized = Step(new SkateTrickState(false), Input(kind, frontside, true));
                Check(sanitized.State.Grinding && sanitized.State.GrindKind == SkateGrindKind.None,
                    "invalid input kind sanitizes instead of rejecting otherwise valid motion");
                Check(Step(sanitized.State, Input(SkateGrindKind.None, false, false)).TrickName == "Grind",
                    "sanitized kind completes with generic name");
                var badState = State(kind, frontside);
                Reject(badState, Input(SkateGrindKind.Darkslide, false, true));
                SkateTrickResult result; string error;
                Check(!SkateTricks.TryBail(badState, out result, out error) && error != null && result.State.Equals(badState),
                    "invalid stored grind cannot be silently repaired by bail");
                var invalidGroundState = State(kind, frontside, grinding: false);
                Check(!SkateTricks.TryAdd(invalidGroundState, "Revert", 50, false, out result, out error) &&
                    error != null && result.State.Equals(invalidGroundState) && result.Events == SkateTrickEvents.None && result.Points == 0,
                    "invalid stored grind refuses ground trick atomically");
            }
        for (int kind = 16; kind <= 18; kind++)
        {
            var value = (SkateGrindKind)kind; var state = State(value);
            Reject(State(value, grinding: false), Input(value, false, true));
            foreach (double seconds in new[] { -1, 86400.0001, double.NaN, double.PositiveInfinity })
                Reject(State(value, seconds: seconds), Input(value, false, true));
            foreach (double dt in new[] { 0, -1, 0.05000001, double.NaN, double.PositiveInfinity })
                Reject(state, Input(value, false, true), dt);
            Reject(state, Input(value, false, true, true, SkatePopKind.Ollie));
            Reject(state, Input(value, false, false, impact: double.NaN));
            Reject(State(value, combo: 1, count: 0), Input(value, false, true));
            Reject(State(value, combo: 0, count: 1), Input(value, false, true));
            Reject(State(value, combo: ScoreLimit + 1, count: 1), Input(value, false, true));
            Reject(State(value, total: ScoreLimit + 1), Input(value, false, true));
        }
    }

    private static void CachedNamesAndAllocation()
    {
        for (int kind = 0; kind < Kinds.Length; kind++)
            foreach (bool frontside in new[] { false, true })
            {
                var value = (SkateGrindKind)kind; string expected = SkateTricks.GrindName(value, frontside);
                var state = State(value, frontside); var input = Input(SkateGrindKind.None, false, false);
                SkateTrickResult result; string error;
                for (int i = 0; i < 200; i++)
                    Check(SkateTricks.TryStep(state, input, 0.03125, out result, out error), "warm cached grind completion");
                long before = GC.GetAllocatedBytesForCurrentThread(); bool success = true;
                for (int i = 0; i < 20000; i++)
                {
                    success &= SkateTricks.TryStep(state, input, 0.03125, out result, out error);
                    success &= object.ReferenceEquals(result.TrickName, expected) &&
                        object.ReferenceEquals(SkateTricks.GrindName(value, frontside), expected) && SkateTricks.GrindRate(value) == Rates[kind];
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Check(success && allocated == 0, "warmed names/lookups/completion allocate zero bytes");
            }
    }

    public static int Main(string[] args)
    {
        try
        {
            Check(args.Length <= 1, "optional argument is the expected actual-DLL SHA-256");
            Check(StateConstructor != null, "existing full state constructor remains available");
            string library = typeof(SkateTricks).Assembly.Location, digest;
            using (var hash = SHA256.Create())
                digest = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(library))).Replace("-", "").ToLowerInvariant();
            Console.WriteLine("Actual library: " + library + "\nSHA-256: " + digest);
            if (args.Length == 1) Check(string.Equals(args[0], digest, StringComparison.OrdinalIgnoreCase), "actual library hash pinned");
            VocabularyAndCompatibility(); EnterUpdateEndAndScoring(); SwitchFakieCombosAndBail();
            DurationAndScoreBoundaries(); InvalidKindsAndStates(); CachedNamesAndAllocation();
            Console.WriteLine("skate_trick_grind_names: " + checks + " checks passed");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
