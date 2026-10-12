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

    private static SkateTrickState RotationState(double flip, bool air = true, bool grinding = false,
                                                  SkateGrindKind kind = SkateGrindKind.None, bool frontside = false,
                                                  bool ridingSwitch = false, double spin = 0, double shove = 0,
                                                  double body = 0, double seconds = 0, long combo = 100,
                                                  int count = 2, long total = 500)
    {
        foreach (var ctor in typeof(SkateTrickState).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            if (ctor.GetParameters().Length == 22)
                return (SkateTrickState)ctor.Invoke(new object[] { spin, flip, shove, seconds, 0.0, air, grinding,
                    SkateGrabKind.None, ridingSwitch, combo, total, count, 0.0, false, false,
                    air ? SkatePopKind.Ollie : SkatePopKind.None, false, kind, frontside, air && ridingSwitch, false, body });
        throw new InvalidOperationException("existing body-aware state constructor unavailable");
    }

    private static SkateTrickInput RotatingInput(double flip = 0, bool air = true, bool grinding = false,
                                                 SkateGrindKind kind = SkateGrindKind.None, bool frontside = false,
                                                 double spin = 0, double shove = 0, double body = 0, double impact = 0)
    { return new SkateTrickInput(spin, flip, shove, SkateGrabKind.None, air, grinding, impact, false, false, 6,
        air ? SkatePopKind.Ollie : SkatePopKind.None, kind, frontside, false, body); }

    private static SkateTrickResult Catch(SkateTrickState air, bool frontside = false)
    {
        var caught = Step(air, Input(SkateGrindKind.Darkslide, frontside, true));
        Check(caught.Events == SkateTrickEvents.Landed && caught.TrickName == ExpectedName(18, frontside) && caught.Points == 0,
            "half-flip touchdown catches directional darkslide without an air-trick award");
        Check(object.ReferenceEquals(caught.TrickName, SkateTricks.GrindName(SkateGrindKind.Darkslide, frontside)), "catch reuses cached directional name");
        Check(caught.State.DarkslideCaught && !caught.State.Airborne && caught.State.Grinding &&
            caught.State.GrindKind == SkateGrindKind.Darkslide && caught.State.PopKind == SkatePopKind.None,
            "caught marker derives from supported darkslide half-turn state");
        Check(caught.State.Flip == (air.Flip < 0 ? -180 : 180) && caught.BoardFlip == caught.State.Flip,
            "catch snaps and retains signed half flip");
        Check(caught.State.ComboBasePoints == air.ComboBasePoints && caught.State.ComboCount == air.ComboCount &&
            caught.TotalPoints == air.TotalPoints && caught.BankedPoints == 0, "catch neither changes nor banks the existing combo");
        return caught;
    }

    private static void DarkslideCatchBoundariesAndHolding()
    {
        foreach (double flip in new[] { -205.0, -180, -155, 155, 180, 205 })
            foreach (bool frontside in new[] { false, true })
            {
                var caught = Catch(RotationState(flip), frontside);
                var state = caught.State;
                for (int i = 0; i < 32; i++)
                {
                    var held = Step(state, Input(SkateGrindKind.Darkslide, frontside, true));
                    Check(held.State.DarkslideCaught && held.State.Flip == caught.State.Flip && held.BoardFlip == caught.BoardFlip &&
                        held.Events == SkateTrickEvents.None && held.Points == 0, "holding retains half flip without duplicate landing or award");
                    Check(held.State.GrindSeconds == (i + 2) / 32.0 && held.State.ComboCount == 2 &&
                        held.State.ComboBasePoints == 100 && held.TotalPoints == 500, "caught darkslide accumulates ordinary grind duration only");
                    state = held.State;
                }
                var heldAxis = Step(state, RotatingInput(flip: flip < 0 ? -1 : 1, air: false, grinding: true,
                    kind: SkateGrindKind.Darkslide, frontside: frontside), 0.05);
                Check(heldAxis.State.DarkslideCaught && heldAxis.BoardFlip == caught.BoardFlip && heldAxis.Points == 0 &&
                    heldAxis.Events == SkateTrickEvents.None, "held half turn does not integrate an aerial flip axis on the surface");
            }
        foreach (double flip in new[] { -205.0001, -154.9999, 154.9999, 205.0001 })
        {
            var failed = Step(RotationState(flip), Input(SkateGrindKind.Darkslide, false, true));
            Check(failed.Events == SkateTrickEvents.Bailed && failed.Points == 0 && !failed.State.Grinding &&
                !failed.State.DarkslideCaught && failed.State.ComboCount == 0 && failed.TotalPoints == 500,
                "outside inclusive half-catch tolerance bails");
        }
        foreach (double flip in new[] { -385.0, -360, -335, -25, 0, 25, 335, 360, 385 })
        {
            var normal = Step(RotationState(flip), Input(SkateGrindKind.Darkslide, true, true));
            bool wholeFlip = Math.Abs(flip) > 25;
            string expected = !wholeFlip ? "Ollie" : flip > 0 ? "Kickflip" : "Heelflip";
            Check(normal.Events == SkateTrickEvents.Landed && normal.TrickName == expected &&
                normal.Points == (wholeFlip ? 150 : 50) && normal.State.Flip == 0 && !normal.State.DarkslideCaught &&
                normal.State.ComboCount == 3, "near-zero/full-turn touchdown retains ordinary landing into an uncaught grind");
        }
        foreach (var kind in new[] { SkateGrindKind.None, SkateGrindKind.FiftyFifty, SkateGrindKind.Boardslide })
            foreach (double flip in new[] { -180.0, 180 })
            {
                var failed = Step(RotationState(flip), Input(kind, false, kind != SkateGrindKind.None));
                Check(failed.Events == SkateTrickEvents.Bailed && failed.Points == 0 && !failed.State.DarkslideCaught,
                    "ordinary ground/other grind cannot catch a half-flipped board");
            }
    }

    private static void DarkslideExitCompletionAndUnwind()
    {
        foreach (int sign in new[] { -1, 1 })
            foreach (bool ridingSwitch in new[] { false, true })
            {
                var caught = Catch(RotationState(sign * 180, ridingSwitch: ridingSwitch));
                var state = caught.State;
                for (int i = 0; i < 31; i++) state = Step(state, Input(SkateGrindKind.Darkslide, false, true)).State;
                var popped = Step(state, RotatingInput());
                Check(popped.Events == SkateTrickEvents.GrindEnded && popped.TrickName == "Backside Darkslide" &&
                    popped.Points == 190 && popped.State.ComboBasePoints == 290 && popped.State.ComboCount == 3,
                    "one-second caught grind awards its duration once on popout");
                Check(popped.State.Airborne && !popped.State.Grinding && !popped.State.DarkslideCaught &&
                    popped.State.Flip == sign * 180 && popped.BoardFlip == sign * 180, "popout inherits half flip instead of a full upright turn");
                Check(object.ReferenceEquals(popped.TrickName, SkateTricks.GrindName(SkateGrindKind.Darkslide, false)),
                    "caught grind completion keeps cached directional name");
                state = popped.State;
                for (int i = 0; i < 5; i++)
                {
                    var rotated = Step(state, RotatingInput(sign), 0.05);
                    Check(rotated.Events == SkateTrickEvents.None && rotated.Points == 0 && rotated.State.ComboCount == 3,
                        "exit rotation never re-awards the grind");
                    Check(rotated.BoardFlip == sign * (180 + (i + 1) * 36), "actual exit axis drives the remaining half turn");
                    state = rotated.State;
                }
                var landed = Step(state, RotatingInput(air: false));
                string expected = sign > 0 ? "Kickflip" : "Heelflip";
                if (ridingSwitch) expected = "Switch " + expected;
                long airPoints = ridingSwitch ? 175 : 150;
                Check(landed.Events == SkateTrickEvents.Landed && landed.TrickName == expected && landed.Points == airPoints &&
                    landed.State.ComboBasePoints == 290 + airPoints && landed.State.ComboCount == 4 && landed.TotalPoints == 500,
                    "completed exit lands the actual signed flip with ordinary score");
                Check(landed.State.Flip == 0 && !landed.State.DarkslideCaught && landed.Switch == ridingSwitch,
                    "upright completion clears caught state and preserves stance");
                state = landed.State;
                for (int i = 0; i < 47; i++) state = Step(state, Input(SkateGrindKind.None, false, false)).State;
                var banked = Step(state, Input(SkateGrindKind.None, false, false));
                Check(banked.Events == SkateTrickEvents.ComboBanked && banked.BankedPoints == (290 + airPoints) * 4 &&
                    banked.TotalPoints == 500 + banked.BankedPoints, "exit combo banks existing total plus real grind/flip points");

                caught = Catch(RotationState(sign * 180, ridingSwitch: ridingSwitch));
                state = Step(caught.State, RotatingInput()).State;
                for (int i = 0; i < 5; i++) state = Step(state, RotatingInput(-sign), 0.05).State;
                var unwound = Step(state, RotatingInput(air: false));
                Check(unwound.Events == SkateTrickEvents.Landed && unwound.TrickName == (ridingSwitch ? "Switch Ollie" : "Ollie") &&
                    unwound.Points == (ridingSwitch ? 75 : 50) && unwound.BoardFlip == 0,
                    "reverse half unwind lands Ollie instead of inventing a complete flip");
                var missed = Step(Step(caught.State, RotatingInput()).State, RotatingInput(air: false));
                Check(missed.Events == SkateTrickEvents.Bailed && missed.Points == 0 && missed.State.ComboCount == 0 &&
                    missed.State.ComboBasePoints == 0 && missed.TotalPoints == 500 && !missed.State.DarkslideCaught,
                    "zero-axis popout cannot auto-catch the uncompleted half turn");
                var firstAxis = Step(caught.State, RotatingInput(sign), 0.05);
                Check(firstAxis.Events == SkateTrickEvents.GrindEnded && firstAxis.BoardFlip == sign * 216,
                    "popout frame integrates its actual axis after inheriting the caught half turn");
                state = firstAxis.State;
                for (int i = 0; i < 4; i++) state = Step(state, RotatingInput(sign), 0.05).State;
                var firstAxisLanded = Step(state, RotatingInput(air: false));
                Check(firstAxisLanded.Events == SkateTrickEvents.Landed && firstAxisLanded.TrickName == expected &&
                    firstAxisLanded.Points == airPoints, "flip begun on popout needs only the remaining actual rotation");
                var legacy = State(SkateGrindKind.Darkslide, ridingSwitch: ridingSwitch);
                Check(!legacy.DarkslideCaught && Step(Step(legacy, RotatingInput()).State, RotatingInput(air: false)).TrickName ==
                    (ridingSwitch ? "Switch Ollie" : "Ollie"), "uncaught zero-flip darkslide pop remains ordinary Ollie");
            }
    }

    private static void DarkslideDropBailAndGuards()
    {
        var caught = Catch(RotationState(180), true).State;
        foreach (var input in new[] { Input(SkateGrindKind.None, false, false), Input(SkateGrindKind.FiftyFifty, false, true),
                                     Input(SkateGrindKind.Bluntslide, false, true) })
        {
            var bailed = Step(caught, input);
            Check(bailed.Events == SkateTrickEvents.Bailed && bailed.TrickName == "Bail" && bailed.Points == 0 && bailed.BankedPoints == 0 &&
                bailed.State.ComboBasePoints == 0 && bailed.State.ComboCount == 0 && bailed.TotalPoints == 500 &&
                bailed.State.GrindKind == SkateGrindKind.None && !bailed.State.Grinding && !bailed.State.DarkslideCaught,
                "dropping/changing a caught darkslide bails before any grind completion award");
            Catch(RotationState(-180, total: bailed.TotalPoints, combo: 0, count: 0));
        }
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, -1.0001, 1.0001 })
        {
            Reject(caught, RotatingInput(flip: bad, air: false));
            Reject(caught, RotatingInput(spin: bad, air: false));
            Reject(caught, RotatingInput(shove: bad, air: false));
            Reject(caught, RotatingInput(body: bad, air: false));
        }
        foreach (double bad in new[] { 0, -1, double.NaN, double.PositiveInfinity, 0.0500001 })
            Reject(caught, Input(SkateGrindKind.None, false, false), bad);
        Reject(caught, RotatingInput(air: true, grinding: true, kind: SkateGrindKind.Darkslide));
        foreach (double bad in new[] { -0.0001, double.NaN, double.PositiveInfinity, 100000.0001 })
            Reject(caught, RotatingInput(air: false, impact: bad));
        foreach (var state in new[] { RotationState(180, air: false),
            RotationState(180, air: false, grinding: true, kind: SkateGrindKind.Boardslide),
            RotationState(179, air: false, grinding: true, kind: SkateGrindKind.Darkslide),
            RotationState(-181, air: false, grinding: true, kind: SkateGrindKind.Darkslide),
            RotationState(180, air: false, grinding: true, kind: SkateGrindKind.Darkslide, body: 1) })
            Reject(state, RotatingInput());
        Check(!RotationState(180).DarkslideCaught && !RotationState(180, air: false).DarkslideCaught &&
            !RotationState(180, air: false, grinding: true, kind: SkateGrindKind.Boardslide).DarkslideCaught,
            "computed marker is limited to non-airborne Darkslide grind state");
        foreach (double edge in new[] { -25.0, 25 })
        {
            var landed = Catch(RotationState(180, spin: edge, shove: edge, body: edge));
            Check(landed.BoardSpin == 0 && landed.BoardShove == 0 && landed.BodySpin == 0 && !landed.Switch,
                "inclusive spin/shove/body tolerance snaps to upright stance during catch");
        }
        foreach (var air in new[] { RotationState(180, spin: -25.0001), RotationState(180, spin: 25.0001),
            RotationState(180, shove: -25.0001), RotationState(180, shove: 25.0001),
            RotationState(180, body: -25.0001), RotationState(180, body: 25.0001), RotationState(180, body: 90) })
        {
            var failed = Step(air, Input(SkateGrindKind.Darkslide, false, true));
            Check(failed.Events == SkateTrickEvents.Bailed && failed.Points == 0 && !failed.State.DarkslideCaught,
                "darkslide catch still enforces spin/shove/body orientation");
        }
        var impact = Step(RotationState(180), RotatingInput(air: false, grinding: true, kind: SkateGrindKind.Darkslide,
            impact: SkateMotion.MaximumLandingFallSpeed + 1));
        Check(impact.Events == SkateTrickEvents.Bailed && impact.Points == 0 && !impact.State.DarkslideCaught, "darkslide catch keeps full impact bail");
        foreach (bool initialSwitch in new[] { false, true })
            foreach (double spin in new[] { 0.0, 180 })
                foreach (double body in new[] { 0.0, 180 })
                {
                    var landed = Catch(RotationState(-180, ridingSwitch: initialSwitch, spin: spin, shove: 180, body: body));
                    Check(landed.Switch == (initialSwitch ^ ((spin == 180) != (body == 180))) && landed.BoardSpin == spin &&
                        landed.BodySpin == body && landed.BoardShove == 180, "half catch retains ordinary board/body stance parity and snapped visuals");
                }
    }

    private static void DarkslideExplicitStanceFeed()
    {
        foreach (bool ridingSwitch in new[] { false, true })
            foreach (bool tailLeads in new[] { false, true })
                foreach (var popKind in new[] { SkatePopKind.Ollie, SkatePopKind.Nollie })
                    foreach (int sign in new[] { -1, 1 })
                    {
                        var state = new SkateTrickState(ridingSwitch);
                        var air = new SkateTrickInput(0, sign, 0, SkateGrabKind.None, true, false, 0,
                            false, false, 6, popKind, SkateGrindKind.None, false, false, 0, tailLeads);
                        for (int i = 0; i < 5; i++) state = Step(state, air, 0.05).State;
                        var contact = new SkateTrickInput(0, 0, 0, SkateGrabKind.None, false, true, 0,
                            false, false, 6, SkatePopKind.None, SkateGrindKind.Darkslide, true, false, 0, !tailLeads);
                        var caught = Step(state, contact);
                        Check(caught.State.DarkslideCaught && caught.TrickName == "Frontside Darkslide" &&
                            caught.Points == 0 && caught.State.ComboCount == 0 && !caught.State.PopTailLeads,
                            "explicit client feed catches half flip and clears airborne naming metadata");
                        var popped = Step(caught.State, air, 0.05);
                        Check(popped.State.Airborne && popped.State.Flip == sign * 216 &&
                            popped.State.PopTailLeads == tailLeads && popped.State.PopKind == popKind &&
                            popped.Events == SkateTrickEvents.GrindEnded,
                            "explicit exit recaptures pop end and physical tail direction before carrying half");
                        state = popped.State;
                        for (int i = 0; i < 4; i++) state = Step(state, air, 0.05).State;
                        var ground = new SkateTrickInput(0, 0, 0, SkateGrabKind.None, false, false, 0,
                            false, false, 6, SkatePopKind.None, SkateGrindKind.None, false, false, 0, !tailLeads);
                        var landed = Step(state, ground);
                        string prefix = ridingSwitch ? (tailLeads ? "Fakie " : "Switch ") : "";
                        if (popKind == SkatePopKind.Nollie) prefix += "Nollie ";
                        Check(landed.Events == SkateTrickEvents.Landed &&
                            landed.TrickName == prefix + (sign > 0 ? "Kickflip" : "Heelflip") &&
                            landed.Points == (ridingSwitch ? 175 : 150) && landed.Switch == ridingSwitch &&
                            landed.State.Flip == 0 && !landed.State.DarkslideCaught && !landed.State.PopTailLeads,
                            "completed explicit Darkslide exit keeps takeoff Fakie/Switch and literal pop name");
                    }
    }

    private static void DarkslideCatchAllocation()
    {
        foreach (int sign in new[] { -1, 1 })
            foreach (bool frontside in new[] { false, true })
            {
                var air = RotationState(sign * 180); var input = Input(SkateGrindKind.Darkslide, frontside, true);
                var caught = Catch(air, frontside); var expected = caught.TrickName;
                var pop = RotatingInput(sign); var ground = RotatingInput(air: false);
                var completed = RotationState(sign * 360);
                var flipName = Step(completed, ground).TrickName;
                Check(flipName == (sign > 0 ? "Kickflip" : "Heelflip"), "cached valid exit has actual signed flip name");
                SkateTrickResult result; string error;
                for (int i = 0; i < 200; i++)
                {
                    Check(SkateTricks.TryStep(air, input, 0.03125, out result, out error), "warm actual darkslide catch");
                    Check(SkateTricks.TryStep(caught.State, pop, 0.05, out result, out error), "warm caught grind popout");
                    Check(SkateTricks.TryStep(completed, ground, 0.03125, out result, out error), "warm valid exit landing");
                }
                long before = GC.GetAllocatedBytesForCurrentThread(); bool success = true;
                for (int i = 0; i < 20000; i++)
                {
                    success &= SkateTricks.TryStep(air, input, 0.03125, out result, out error) && result.State.DarkslideCaught &&
                        result.Points == 0 && object.ReferenceEquals(expected, result.TrickName);
                    success &= SkateTricks.TryStep(caught.State, pop, 0.05, out result, out error) &&
                        result.Events == SkateTrickEvents.GrindEnded && result.BoardFlip == sign * 216 &&
                        object.ReferenceEquals(expected, result.TrickName);
                    success &= SkateTricks.TryStep(completed, ground, 0.03125, out result, out error) &&
                        result.Events == SkateTrickEvents.Landed && result.Points == 150 && object.ReferenceEquals(flipName, result.TrickName);
                }
                Check(success && GC.GetAllocatedBytesForCurrentThread() - before == 0,
                    "cached signed catch, popout and valid exit landing allocate zero bytes after warmup");
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
            DarkslideCatchBoundariesAndHolding(); DarkslideExitCompletionAndUnwind(); DarkslideDropBailAndGuards(); DarkslideExplicitStanceFeed(); DarkslideCatchAllocation();
            Console.WriteLine("skate_trick_grind_names: " + checks + " checks passed");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
