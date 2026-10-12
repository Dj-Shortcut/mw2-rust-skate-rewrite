using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Shortcut.RustMod;

internal static class SkateLateFlipsBodyVarialsScenarios
{
    private static int checks;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new InvalidOperationException(message); }
    private static void Near(double actual, double expected, string message)
    { Check(!double.IsNaN(actual) && !double.IsInfinity(actual) && Math.Abs(actual - expected) <= 1e-8, message); }

    private static SkateFlickResult Flick(ref SkateFlickState state, double x, double y, bool air = true,
                                          bool mirrored = false, double leftX = 0, double leftY = 0,
                                          bool popped = true, double dt = 0.05)
    {
        SkateFlickResult result; string error;
        Check(SkateFlick.TryStep(state, new SkateFlickInput(x, y, air, mirrored, leftX, leftY, popped), dt,
            out result, out error) && error == null, "valid additive flick step");
        Check(Math.Abs(result.BodyVarialDegrees) <= 180 && result.Pop >= 0 && result.Pop <= 1,
            "bounded gesture result");
        state = result.State;
        return result;
    }

    private static SkateFlickResult LateFlick(bool shove, bool mirrored)
    {
        var state = default(SkateFlickState);
        Flick(ref state, 0, -1, air: false, popped: false);
        var pop = Flick(ref state, 0, 1, air: false, popped: false);
        Check(pop.Events == SkateFlickEvents.Ollie && pop.Pop > 0 && pop.Composition.Kind == SkatePopKind.Ollie,
            "first held flick pops the board");
        Flick(ref state, 0, 0, dt: 0.01);
        Flick(ref state, 0, -1, mirrored: mirrored);
        var late = Flick(ref state, shove ? 1 : -0.7071067811865476, shove ? 0 : 0.7071067811865476, mirrored: mirrored);
        Check(late.Composition.Late && late.Composition.Kind == SkatePopKind.None && late.Pop == 0 && late.Composition.Strength == 0,
            "second held flick is late with no pop boost");
        Check((late.Events & (SkateFlickEvents.Ollie | SkateFlickEvents.Nollie)) == 0, "late flick emits no new pop event");
        if (shove)
        {
            Near(late.Composition.FlipTurns, 0, "late shove does not flip");
            Near(late.Composition.ShoveDegrees, mirrored ? -180 : 180, "mirrored late shove direction");
            Check(late.Events == (mirrored ? SkateFlickEvents.ShoveBackside : SkateFlickEvents.ShoveFrontside), "late shove event");
        }
        else
        {
            Near(late.Composition.FlipTurns, mirrored ? -1 : 1, "mirrored late flip direction");
            Near(late.Composition.ShoveDegrees, 0, "late diagonal flick has no shove");
            Check(late.Events == (mirrored ? SkateFlickEvents.Heelflip : SkateFlickEvents.Kickflip), "late flip event");
        }
        for (int i = 0; i < 6; i++)
            Check(Flick(ref state, shove ? 1 : -0.7071067811865476, shove ? 0 : 0.7071067811865476,
                mirrored: mirrored).Events == SkateFlickEvents.None, "held late flick is not duplicated");
        return late;
    }

    private static void LateRecognitionAndCompatibility()
    {
        Check((int)SkateFlickEvents.BodyVarial == 64 && (int)SkateFlickEvents.ShoveBackside == 32,
            "body event appends without renumbering old events");
        Check(!default(SkatePop).Late && !new SkatePop(SkatePopKind.Ollie, 1, 0, 1).Late &&
            new SkatePop(SkatePopKind.None, 1, 0, 0).Late && new SkatePop(SkatePopKind.None, 0, 180, 0).Late,
            "late composition requires a board rotation without a pop kind");
        foreach (bool mirrored in new[] { false, true })
        { LateFlick(false, mirrored); LateFlick(true, mirrored); }
        var state = default(SkateFlickState);
        Flick(ref state, 0, -1);
        var straight = Flick(ref state, 0, 1);
        Check(!straight.Composition.Late && straight.Composition.Kind == SkatePopKind.None && straight.Pop == 0 &&
            straight.Events == SkateFlickEvents.None, "straight post-pop flick cannot create Late Ollie or boost");

        var old = default(SkateFlickState); var explicitDefaults = old;
        foreach (var point in new[] { new[] { 0.0, -1.0 }, new[] { 0.0, -1.0 }, new[] { -0.7, 0.7 },
                                     new[] { -0.7, 0.7 }, new[] { 0.0, 0.0 } })
        {
            SkateFlickResult result; string error;
            Check(SkateFlick.TryStep(old, new SkateFlickInput(point[0], point[1], true, false), 0.05, out result, out error),
                "old airborne pullback API succeeds");
            var added = Flick(ref explicitDefaults, point[0], point[1], popped: false);
            Check(result.State.Equals(added.State) && result.Events == added.Events && result.Pop == added.Pop &&
                result.Composition.Kind == added.Composition.Kind && result.Composition.FlipTurns == added.Composition.FlipTurns &&
                result.BodyVarialDegrees == 0, "old input constructor equals explicit default new inputs");
            if ((result.Events & SkateFlickEvents.Ollie) != 0)
                Check(result.Composition.Kind == SkatePopKind.Ollie && !result.Composition.Late, "initial airborne pullback remains Ollie");
            old = result.State;
        }
        state = default(SkateFlickState);
        var side = Flick(ref state, -1, 0, popped: false);
        Check(side.Events == SkateFlickEvents.Kickflip && side.Composition.Late && side.Pop == 0,
            "existing immediate air-side flip stays available");
    }

    private static SkateFlickResult Circle(ref SkateFlickState state, int sign = 1, bool mirrored = false,
                                           int changes = 8, bool air = true, double radius = 0.8, double dt = 0.05)
    {
        int count = 0; var emitted = default(SkateFlickResult);
        for (int i = 0; i <= changes; i++)
        {
            double angle = sign * i * 45 * Math.PI / 180;
            var result = Flick(ref state, 0, 0, air, mirrored, radius * Math.Sin(angle), radius * Math.Cos(angle), dt: dt);
            Check(result.Pop == 0 && result.Composition.Strength == 0 && !result.Composition.Late, "left circle does not move/pop board");
            if ((result.Events & SkateFlickEvents.BodyVarial) != 0) { count++; emitted = result; }
            else Check(result.BodyVarialDegrees == 0, "no body target without circle event");
        }
        Check(count <= 1, "at most one body varial per circle");
        return emitted;
    }

    private static void BodyCircleRecognitionAndResets()
    {
        foreach (int sign in new[] { -1, 1 })
            foreach (bool mirrored in new[] { false, true })
            {
                var state = default(SkateFlickState);
                var result = Circle(ref state, sign, mirrored);
                Check(result.Events == SkateFlickEvents.BodyVarial && result.BodyVarialDegrees == sign * (mirrored ? -180 : 180),
                    "eight signed45-degree changes recognize mirrored rider half-turn");
                Flick(ref state, 0, 0, mirrored: mirrored);
                Check(Circle(ref state, sign, mirrored).Events == SkateFlickEvents.None, "center does not unlock a completed flight latch");
                Flick(ref state, 0, 0, air: false, mirrored: mirrored, popped: false);
                Check(Circle(ref state, sign, mirrored).Events == SkateFlickEvents.BodyVarial, "landing unlocks next body circle");
            }
        var partial = default(SkateFlickState);
        Check(Circle(ref partial, changes: 7).Events == SkateFlickEvents.None, "seven45-degree changes are not a full circle");
        Flick(ref partial, 0, 0);
        Check(Flick(ref partial, 0, 0, leftX: 0, leftY: 0.8).Events == SkateFlickEvents.None, "centering clears partial angular travel");
        var belowRadius = default(SkateFlickState);
        Check(Circle(ref belowRadius, radius: 0.69).Events == SkateFlickEvents.None, "small left-stick circle is rejected");
        var grounded = default(SkateFlickState);
        Check(Circle(ref grounded, air: false).Events == SkateFlickEvents.None, "ground circle cannot body varial");
        foreach (var angles in new[] { new[] { 0.0, 45, 90, 135, 90, 135, 180, 225, 270, 315, 360 },
                                      new[] { 0.0, 45, 90, 135, 300, 345, 390, 435, 480, 525 } })
        {
            var state = default(SkateFlickState);
            foreach (double degrees in angles)
            {
                double angle = degrees * Math.PI / 180;
                Check(Flick(ref state, 0, 0, leftX: 0.8 * Math.Sin(angle), leftY: 0.8 * Math.Cos(angle)).Events ==
                    SkateFlickEvents.None, "reversal/angular jump resets a partial circle");
            }
        }
        var slow = default(SkateFlickState);
        for (int i = 0; i <= 12; i++)
        {
            double angle = i * 30 * Math.PI / 180;
            Check(Flick(ref slow, 0, 0, leftX: 0.8 * Math.Sin(angle), leftY: 0.8 * Math.Cos(angle), dt: 0.1).Events ==
                SkateFlickEvents.None, "circle exceeding one second does not fire");
        }
        foreach (double dt in new[] { 0.0001, 0.1 })
        {
            var state = default(SkateFlickState);
            Check(Circle(ref state, dt: dt).Events == SkateFlickEvents.BodyVarial, "valid reader timestep endpoints recognize full circle");
        }
        var interrupted = default(SkateFlickState);
        Circle(ref interrupted, changes: 7);
        Flick(ref interrupted, 0, 0, leftX: 0, leftY: 0.69);
        Check(Flick(ref interrupted, 0, 0, leftX: 0, leftY: 0.8).Events == SkateFlickEvents.None,
            "radius interruption cannot finish the preceding partial circle");
        Check(Circle(ref interrupted).Events == SkateFlickEvents.BodyVarial, "radius interruption rearms a full fresh circle");
        var simultaneous = default(SkateFlickState);
        for (int i = 0; i < 8; i++)
        {
            double angle = i * 45 * Math.PI / 180;
            Flick(ref simultaneous, 0, i == 7 ? -1 : 0,
                leftX: 0.8 * Math.Sin(angle), leftY: 0.8 * Math.Cos(angle));
        }
        var together = Flick(ref simultaneous, -0.7071067811865476, 0.7071067811865476, leftX: 0, leftY: 0.8);
        Check(together.Events == (SkateFlickEvents.BodyVarial | SkateFlickEvents.Kickflip) &&
            together.BodyVarialDegrees == 180 && together.Composition.Late && together.Composition.FlipTurns == 1 &&
            together.Pop == 0 && together.Composition.Strength == 0,
            "one frame retains independent late flip and completed body circle without a boost");
    }

    private static SkateTrickInput AirInput(double flip = 0, double shove = 0, double body = 0, bool late = false,
                                            double spin = 0, bool air = true, double impact = 0,
                                            SkatePopKind pop = SkatePopKind.Ollie)
    { return new SkateTrickInput(spin, flip, shove, SkateGrabKind.None, air, false, impact, false, false, 6,
        pop, SkateGrindKind.None, false, late, body); }

    private static SkateTrickResult Trick(SkateTrickState state, SkateTrickInput input, double dt = 0.05)
    {
        SkateTrickResult result; string error;
        Check(SkateTricks.TryStep(state, input, dt, out result, out error) && error == null, "valid new scoring step");
        Near(result.RiderSpin, result.BoardSpin + result.BodySpin, "rider visual includes independent body spin");
        return result;
    }

    private static SkateTrickResult Flight(double flip = 0, double shove = 0, double body = 0, bool late = false,
                                           double spin = 0, bool ridingSwitch = false, SkatePopKind pop = SkatePopKind.Ollie)
    {
        var state = new SkateTrickState(ridingSwitch);
        for (int i = 0; i < 10; i++)
        {
            var result = Trick(state, AirInput(flip, shove, body, late, spin, pop: pop));
            Near(result.BoardSpin, spin * 18 * (i + 1), "body turn leaves board yaw accumulation independent");
            Near(result.BoardShove, shove * 36 * (i + 1), "body turn leaves shove accumulation independent");
            Near(result.BodySpin, body * 18 * (i + 1), "body target integrates rider-only rotation");
            state = result.State;
        }
        return Trick(state, AirInput(air: false));
    }

    private static void ExistingConstructors()
    {
        var seven = new SkateFlickResult(default(SkateFlickState), SkateFlickEvents.Kickflip, 0, 0, false, false, false);
        var eight = new SkateFlickResult(default(SkateFlickState), SkateFlickEvents.Kickflip, 0, 0, false, false, false,
            new SkatePop(SkatePopKind.None, 1, 0, 0));
        Check(seven.BodyVarialDegrees == 0 && eight.BodyVarialDegrees == 0 && seven.Composition.FlipTurns == 1 &&
            eight.Composition.FlipTurns == 1, "existing result constructors keep zero body target");
        var old = new SkateTrickInput(0, 1, 0, SkateGrabKind.None, true, false, 0, false, false, 6,
            SkatePopKind.Ollie, SkateGrindKind.None, false);
        Check(!old.Late && old.BodySpin == 0, "existing scorer input constructor defaults new metadata");
        var oldState = new SkateTrickState(false); var defaultState = oldState;
        for (int i = 0; i < 10; i++)
        {
            var a = Trick(oldState, old); var b = Trick(defaultState, AirInput(flip: 1));
            Check(a.State.Equals(b.State) && a.Events == b.Events && a.BoardFlip == b.BoardFlip && a.BodySpin == 0,
                "existing/new-default scoring inputs retain identical trajectory");
            oldState = a.State; defaultState = b.State;
        }
        var landed = Trick(oldState, new SkateTrickInput(0, 0, false, false, false, 0));
        Check(landed.TrickName == "Kickflip" && landed.Points == 150 && landed.BodySpin == 0, "old landing input preserves ordinary kickflip");
        bool found = false;
        foreach (var ctor in typeof(SkateTrickState).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            if (ctor.GetParameters().Length == 20)
            {
                var state = (SkateTrickState)ctor.Invoke(new object[] { 0.0, 360.0, 0.0, 0.0, 0.0, true, false,
                    SkateGrabKind.None, false, 0L, 0L, 0, 0.0, false, false, SkatePopKind.Ollie, false,
                    SkateGrindKind.None, false, false });
                Check(state.BodySpin == 0 && !state.Late && Trick(state, AirInput(air: false)).TrickName == "Kickflip",
                    "existing20-parameter state constructor retains default metadata and scoring");
                found = true;
            }
        Check(found, "existing full state constructor remains available");
    }

    private static void ReaderToScoringAndStance()
    {
        foreach (bool mirrored in new[] { false, true })
        {
            var late = LateFlick(false, mirrored).Composition;
            var circleState = default(SkateFlickState);
            var circle = Circle(ref circleState, mirrored: mirrored);
            var landed = Flight(late.FlipTurns, body: circle.BodyVarialDegrees / 180, late: late.Late);
            Check(landed.Events == SkateTrickEvents.Landed && landed.TrickName ==
                (mirrored ? "Body Varial Late Heelflip" : "Body Varial Late Kickflip") && landed.Points == 250,
                "recognized late flip and body target drive actual scorer");
            Check(landed.Switch && landed.BoardSpin == 0 && landed.BoardShove == 0 && Math.Abs(landed.BodySpin) == 180,
                "body half-turn changes stance without changing board yaw/shove");
            Check(!landed.State.Late && landed.State.BodySpin == 0 && !landed.State.Airborne, "landing clears flight metadata");
            var shove = LateFlick(true, mirrored).Composition;
            landed = Flight(shove: shove.ShoveDegrees / 360, late: shove.Late);
            Check(landed.TrickName == (mirrored ? "Late Pop Shove-it" : "Late Frontside Pop Shove-it") &&
                landed.Points == 150 && !landed.Switch, "late shove names/scores without stance or bonus drift");
        }
        foreach (double body in new[] { -1.0, 1.0 })
            foreach (double spin in new[] { -1.0, 0.0, 1.0 })
                foreach (bool ridingSwitch in new[] { false, true })
                {
                    var landed = Flight(body: body, spin: spin, ridingSwitch: ridingSwitch);
                    Check(landed.Events == SkateTrickEvents.Landed && landed.Switch == (ridingSwitch ^ (spin == 0)),
                        "board/body odd half-turn parity composes stance");
                    Check(landed.Points == 150 + (spin == 0 ? 0 : 100) + (ridingSwitch ? 25 : 0), "body half contributes exactly100 points");
                }
        Check(Flight(body: 1).TrickName == "Body Varial", "body-only landing name");
        Check(Flight(flip: 1, body: 1).TrickName == "Body Varial Kickflip", "body flip landing name");
        Check(Flight(flip: 1, late: true).TrickName == "Late Kickflip", "late flip landing name");
        Check(Flight(late: true).TrickName == "Ollie", "zero board rotations never name Late Ollie");
        Check(Flight(flip: 1, body: 1, ridingSwitch: true, pop: SkatePopKind.Nollie).TrickName == "Fakie Body Varial Kickflip",
            "new names preserve fakie pop context");
    }

    private static SkateTrickState AirState(double body, double flip = 0, bool late = false, bool air = true)
    {
        foreach (var ctor in typeof(SkateTrickState).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            if (ctor.GetParameters().Length == 22)
                return (SkateTrickState)ctor.Invoke(new object[] { 0.0, flip, 0.0, 0.0, 0.0, air, false, SkateGrabKind.None,
                    false, 0L, 0L, 0, 0.0, false, false, air ? SkatePopKind.Ollie : SkatePopKind.None, false,
                    SkateGrindKind.None, false, false, late, body });
        throw new InvalidOperationException("full body-spin state constructor unavailable");
    }

    private static void CatchBailAndClamp()
    {
        foreach (double body in new[] { -180.0, -155, -25, 0, 25, 155, 180 })
        {
            var landed = Trick(AirState(body), AirInput(air: false));
            Check(landed.Events == SkateTrickEvents.Landed && landed.State.BodySpin == 0 && !landed.State.Late,
                "body half-turn catch tolerance inclusive");
        }
        foreach (double body in new[] { -154.999, -90, -25.001, 25.001, 90, 154.999 })
        {
            var bailed = Trick(AirState(body), AirInput(air: false));
            Check(bailed.Events == SkateTrickEvents.Bailed && bailed.Points == 0 && bailed.State.ComboCount == 0 &&
                bailed.State.BodySpin == 0 && !bailed.State.Late, "unfinished body turn bails and resets");
        }
        foreach (double direction in new[] { -1.0, 1.0 })
        {
            var state = new SkateTrickState(false);
            for (int i = 0; i < 40; i++) state = Trick(state, AirInput(body: direction)).State;
            Near(state.BodySpin, direction * 180, "body turn caps at one half-turn per flight");
            var reset = Trick(state, AirInput(air: false));
            Near(Trick(reset.State, AirInput(body: direction)).BodySpin, direction * 18, "next flight starts a fresh body target");
            SkateTrickResult result; string error;
            Check(SkateTricks.TryBail(state, out result, out error) && error == null && result.BodySpin == 0 &&
                result.State.BodySpin == 0 && !result.State.Late, "explicit bail clears body visual/state metadata");
        }
        var cancelled = AirState(0, 0, late: true);
        Check(Trick(cancelled, AirInput(air: false)).TrickName == "Ollie", "cancelled late rotations do not decorate landing");
        var impact = Trick(AirState(180, 360, true), AirInput(air: false, impact: SkateMotion.MaximumLandingFallSpeed + 1));
        Check(impact.Events == SkateTrickEvents.Bailed && impact.Points == 0 && impact.State.ComboCount == 0,
            "full normal impact still bails completed new trick");
    }

    private static void RejectTrick(SkateTrickState state, SkateTrickInput input)
    {
        SkateTrickResult result; string error;
        Check(!SkateTricks.TryStep(state, input, 0.05, out result, out error) && error != null && result.State.Equals(state) &&
            result.Events == SkateTrickEvents.None && result.Points == 0, "invalid additive scorer request/state refuses atomically");
    }

    private static void InvalidAndCachedAllocation()
    {
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1.000001, 1.000001 })
            RejectTrick(new SkateTrickState(false), AirInput(body: bad));
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, -180.0001, 180.0001 })
            RejectTrick(AirState(bad), AirInput());
        RejectTrick(AirState(1, air: false), AirInput());
        RejectTrick(AirState(0, late: true, air: false), AirInput());
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, -1.0002, 1.0002 })
            foreach (bool leftX in new[] { false, true })
            {
                var state = default(SkateFlickState); SkateFlickResult result; string error;
                var input = new SkateFlickInput(0, 0, true, false, leftX ? bad : 0, leftX ? 0 : bad, true);
                Check(!SkateFlick.TryStep(state, input, 0.05, out result, out error) && error != null && result.State.Equals(state) &&
                    result.Events == SkateFlickEvents.None && result.BodyVarialDegrees == 0, "invalid left axis is atomically refused");
            }
        var partial = default(SkateFlickState);
        Circle(ref partial, changes: 3);
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, 0, -1, 0.10000001 })
        {
            SkateFlickResult result; string error;
            Check(!SkateFlick.TryStep(partial, new SkateFlickInput(0, 0, true, false, 0, -0.8, true), bad,
                out result, out error) && error != null && result.State.Equals(partial) &&
                result.Events == SkateFlickEvents.None && result.BodyVarialDegrees == 0,
                "invalid timestep preserves warmed partial circle atomically");
        }
        foreach (var field in typeof(SkateFlickState).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            if (field.FieldType == typeof(double))
            {
                object malformed = default(SkateFlickState); field.SetValue(malformed, double.NaN);
                var state = (SkateFlickState)malformed; SkateFlickResult result; string error;
                Check(!SkateFlick.TryStep(state, new SkateFlickInput(0, 0, true, false, 0, 0.8, true), 0.05, out result, out error) &&
                    error != null && result.State.Equals(state) && result.Events == SkateFlickEvents.None,
                    "malformed reader history refuses atomically " + field.Name);
            }
        foreach (var state in new[] { AirState(0, 360, true), AirState(180), AirState(180, 360), AirState(180, 360, true) })
        {
            var input = AirInput(air: false); var expected = Trick(state, input).TrickName;
            SkateTrickResult result; string error;
            for (int i = 0; i < 200; i++) Check(SkateTricks.TryStep(state, input, 0.05, out result, out error), "warm modified-name landing");
            long before = GC.GetAllocatedBytesForCurrentThread(); bool success = true;
            for (int i = 0; i < 20000; i++)
                success &= SkateTricks.TryStep(state, input, 0.05, out result, out error) && object.ReferenceEquals(expected, result.TrickName);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(success && allocated == 0, "modified landing names and scoring allocate zero bytes after warmup");
        }
        var reader = default(SkateFlickState); var readerInput = new SkateFlickInput(0, 0, true, false, 0, 0.8, true);
        SkateFlickResult read; string readError;
        for (int i = 0; i < 200; i++)
        { Check(SkateFlick.TryStep(reader, readerInput, 0.01, out read, out readError), "warm additive reader"); reader = read.State; }
        long start = GC.GetAllocatedBytesForCurrentThread(); bool readSuccess = true;
        for (int i = 0; i < 20000; i++)
        { readSuccess &= SkateFlick.TryStep(reader, readerInput, 0.01, out read, out readError); reader = read.State; }
        Check(readSuccess && GC.GetAllocatedBytesForCurrentThread() - start == 0, "warmed left-stick reader allocates zero bytes");
    }

    private readonly struct StanceCase
    {
        public readonly bool Switch, TailLeads, Late;
        public readonly SkatePopKind Pop;
        public readonly SkateGrabKind Grab;
        public readonly double Spin, Flip, Shove, Body;
        public readonly string Name;
        public readonly long Points;
        public readonly int Steps;
        public StanceCase(bool ridingSwitch, bool tailLeads, SkatePopKind pop, string name, long points,
                          double spin = 0, double flip = 0, double shove = 0, double body = 0,
                          bool late = false, SkateGrabKind grab = SkateGrabKind.None, int steps = 20)
        { Switch = ridingSwitch; TailLeads = tailLeads; Pop = pop; Name = name; Points = points;
          Spin = spin; Flip = flip; Shove = shove; Body = body; Late = late; Grab = grab; Steps = steps; }
    }

    private static SkateTrickInput StanceInput(StanceCase fixture, bool air = true, bool opposite = false,
                                              double impact = 0)
    {
        double seconds = fixture.Steps * 0.05;
        return new SkateTrickInput(fixture.Spin / (360 * seconds), fixture.Flip / (2 * seconds),
            fixture.Shove / (720 * seconds), fixture.Grab, air, false, impact, false, false, 6,
            fixture.Pop, SkateGrindKind.None, false, fixture.Late, fixture.Body / (360 * seconds),
            opposite ? !fixture.TailLeads : fixture.TailLeads);
    }

    private static bool StanceNaming(SkateTrickState state)
    {
        var field = typeof(SkateTrickState).GetField("StanceNaming", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Check(field != null, "captured stance-name mode is present");
        return (bool)field.GetValue(state);
    }

    private static SkateTrickState StanceAir(StanceCase fixture, SkateTrickState start, bool oldAfterTakeoff = false)
    {
        var state = start;
        for (int i = 0; i < fixture.Steps; i++)
        {
            var input = StanceInput(fixture, opposite: i > 0);
            if (i > 0 && oldAfterTakeoff)
            {
                double seconds = fixture.Steps * 0.05;
                input = AirInput(fixture.Flip / (2 * seconds), fixture.Shove / (720 * seconds),
                    fixture.Body / (360 * seconds), fixture.Late, fixture.Spin / (360 * seconds),
                    pop: fixture.Pop == SkatePopKind.Ollie ? SkatePopKind.Nollie : SkatePopKind.Ollie);
            }
            var result = Trick(state, input);
            Check(result.State.PopTailLeads == fixture.TailLeads && result.State.PopSwitch == start.Switch &&
                result.State.PopKind == fixture.Pop && StanceNaming(result.State), "takeoff mode, stance, direction and pop stay latched");
            Near(result.BoardSpin, fixture.Spin * (i + 1) / fixture.Steps, "explicit naming preserves board yaw");
            Near(result.BoardFlip, fixture.Flip * 360 * (i + 1) / fixture.Steps, "explicit naming preserves flip angle");
            Near(result.BoardShove, fixture.Shove * (i + 1) / fixture.Steps, "explicit naming preserves shove angle");
            Near(result.BodySpin, fixture.Body * (i + 1) / fixture.Steps, "explicit naming preserves body angle");
            state = result.State;
        }
        return state;
    }

    private static SkateTrickResult StanceLand(StanceCase fixture, SkateTrickState airborne)
    {
        var result = Trick(airborne, StanceInput(fixture, air: false, opposite: true));
        Check(result.Events == SkateTrickEvents.Landed && result.TrickName == fixture.Name && result.Points == fixture.Points,
            "literal explicit stance/pop/modifier name and score: " + fixture.Name);
        Check(!result.State.PopTailLeads && !result.State.PopSwitch && !StanceNaming(result.State), "accepted landing clears captured naming metadata");
        return result;
    }

    private static void ExplicitStanceVocabulary()
    {
        var cases = new[] {
            new StanceCase(false, false, SkatePopKind.Ollie, "Ollie", 50),
            new StanceCase(false, true, SkatePopKind.Ollie, "Ollie", 50),
            new StanceCase(false, true, SkatePopKind.Nollie, "Nollie", 50),
            new StanceCase(false, false, SkatePopKind.Ollie, "Kickflip", 150, flip: 1),
            new StanceCase(false, true, SkatePopKind.Nollie, "Nollie Kickflip", 150, flip: 1),
            new StanceCase(false, true, SkatePopKind.NoComply, "No Comply", 100),
            new StanceCase(false, true, SkatePopKind.Boneless, "Boneless", 100),
            new StanceCase(false, true, SkatePopKind.Fastplant, "Fastplant", 100),
            new StanceCase(false, true, SkatePopKind.Beanplant, "Beanplant", 100),
            new StanceCase(true, false, SkatePopKind.Ollie, "Switch Ollie", 75),
            new StanceCase(true, false, SkatePopKind.Nollie, "Switch Nollie", 75),
            new StanceCase(true, true, SkatePopKind.Ollie, "Fakie Ollie", 75),
            new StanceCase(true, true, SkatePopKind.Nollie, "Fakie Nollie", 75),
            new StanceCase(true, false, SkatePopKind.Ollie, "Switch Kickflip", 175, flip: 1),
            new StanceCase(true, false, SkatePopKind.Nollie, "Switch Nollie Kickflip", 175, flip: 1),
            new StanceCase(true, true, SkatePopKind.Ollie, "Fakie Kickflip", 175, flip: 1),
            new StanceCase(true, true, SkatePopKind.Nollie, "Fakie Nollie Kickflip", 175, flip: 1),
            new StanceCase(true, false, SkatePopKind.Ollie, "Switch Heelflip", 175, flip: -1),
            new StanceCase(true, true, SkatePopKind.Nollie, "Fakie Nollie Heelflip", 175, flip: -1),
            new StanceCase(true, false, SkatePopKind.Ollie, "Switch Frontside 180", 175, spin: 180),
            new StanceCase(true, true, SkatePopKind.Ollie, "Fakie Frontside 180", 175, spin: 180),
            new StanceCase(true, false, SkatePopKind.Ollie, "Switch Backside 180", 175, spin: -180),
            new StanceCase(true, true, SkatePopKind.Ollie, "Fakie Backside 180", 175, spin: -180),
            new StanceCase(true, false, SkatePopKind.Ollie, "Switch Frontside Pop Shove-it", 175, shove: 180),
            new StanceCase(true, true, SkatePopKind.Ollie, "Fakie Frontside Pop Shove-it", 175, shove: 180),
            new StanceCase(true, false, SkatePopKind.Ollie, "Switch Pop Shove-it", 175, shove: -180),
            new StanceCase(true, true, SkatePopKind.Ollie, "Fakie Pop Shove-it", 175, shove: -180),
            new StanceCase(true, false, SkatePopKind.NoComply, "Switch No Comply", 125),
            new StanceCase(true, true, SkatePopKind.NoComply, "Fakie No Comply", 125),
            new StanceCase(true, false, SkatePopKind.Boneless, "Switch Boneless", 125),
            new StanceCase(true, true, SkatePopKind.Boneless, "Fakie Boneless", 125),
            new StanceCase(true, false, SkatePopKind.Fastplant, "Switch Fastplant", 125),
            new StanceCase(true, true, SkatePopKind.Fastplant, "Fakie Fastplant", 125),
            new StanceCase(true, false, SkatePopKind.Beanplant, "Switch Beanplant", 125),
            new StanceCase(true, true, SkatePopKind.Beanplant, "Fakie Beanplant", 125),
            new StanceCase(true, false, SkatePopKind.Ollie, "Switch Ollie Melon", 125, grab: SkateGrabKind.Melon),
            new StanceCase(true, true, SkatePopKind.Nollie, "Fakie Nollie Melon", 125, grab: SkateGrabKind.Melon),
            new StanceCase(true, true, SkatePopKind.Ollie, "Fakie Late Kickflip", 175, flip: 1, late: true),
            new StanceCase(true, true, SkatePopKind.Nollie, "Fakie Nollie Late Kickflip", 175, flip: 1, late: true),
            new StanceCase(true, false, SkatePopKind.Ollie, "Switch Body Varial", 175, body: 180),
            new StanceCase(true, true, SkatePopKind.Nollie, "Fakie Nollie Body Varial Kickflip", 275, flip: 1, body: 180),
            new StanceCase(true, true, SkatePopKind.Ollie, "Fakie Body Varial Late Kickflip Melon", 325,
                flip: 1, body: 180, late: true, grab: SkateGrabKind.Melon)
        };
        foreach (var fixture in cases)
        {
            var airborne = StanceAir(fixture, new SkateTrickState(fixture.Switch));
            var landed = StanceLand(fixture, airborne);
            var oldInputFlight = StanceAir(fixture, new SkateTrickState(fixture.Switch), oldAfterTakeoff: true);
            var second = StanceLand(fixture, oldInputFlight);
            Check(object.ReferenceEquals(landed.TrickName, second.TrickName), "captured explicit names reuse the same cache across caller modes");
            SkateTrickResult bailed; string error;
            Check(SkateTricks.TryBail(airborne, out bailed, out error) && error == null && !bailed.State.PopTailLeads &&
                !bailed.State.PopSwitch && !StanceNaming(bailed.State), "explicit bail clears captured stance metadata");
            RejectTrick(airborne, new SkateTrickInput(0, 0, 0, SkateGrabKind.None, true, false, 0, false, false, 6,
                SkatePopKind.Ollie, SkateGrindKind.None, false, false, double.NaN, !fixture.TailLeads));
        }
    }

    private static void SequentialStanceSignals()
    {
        var turn = new StanceCase(false, false, SkatePopKind.Ollie, "Frontside 180", 150, spin: 180);
        var turned = StanceLand(turn, StanceAir(turn, new SkateTrickState(false)));
        Check(turned.Switch && turned.BoardSpin == 180 && turned.BoardShove == 0, "board/rider180 exposes a tail-leading next-pop signal");
        var fakie = new StanceCase(true, true, SkatePopKind.Ollie, "Fakie Kickflip", 175, flip: 1);
        Check(StanceLand(fakie, StanceAir(fakie, turned.State)).Switch, "next tail-leading switch pop is Fakie");
        var bigspin = new StanceCase(false, false, SkatePopKind.Ollie, "Frontside Bigspin", 250, spin: 180, shove: 180);
        var spun = StanceLand(bigspin, StanceAir(bigspin, new SkateTrickState(false)));
        Check(spun.Switch && spun.BoardSpin + spun.BoardShove == 360, "180+shove restores deck nose while rider remains switch");
        var opposite = new StanceCase(true, false, SkatePopKind.Ollie, "Switch Kickflip", 175, flip: 1);
        Check(StanceLand(opposite, StanceAir(opposite, spun.State)).Switch, "next nose-leading switch pop remains Switch");
        var varial = new StanceCase(false, false, SkatePopKind.Ollie, "Body Varial", 150, body: 180);
        var body = StanceLand(varial, StanceAir(varial, new SkateTrickState(false)));
        Check(body.Switch && body.BoardSpin == 0 && body.BoardShove == 0 && body.BodySpin == 180,
            "body-only varial changes stance without a physical deck reversal");
        StanceLand(opposite, StanceAir(opposite, body.State));
        var bodyShove = new StanceCase(false, false, SkatePopKind.Ollie, "Body Varial Frontside Pop Shove-it", 250,
            shove: 180, body: 180);
        var reversed = StanceLand(bodyShove, StanceAir(bodyShove, new SkateTrickState(false)));
        Check(reversed.Switch && reversed.BoardShove == 180 && reversed.BoardSpin == 0, "body+shove exposes reversed-deck tail signal");
        StanceLand(fakie, StanceAir(fakie, reversed.State));
    }

    private static void ExplicitSnapAndFallbackCaches()
    {
        foreach (var fixture in new[] {
            new StanceCase(true, true, SkatePopKind.Ollie, "Fakie Frontside 180", 175, spin: 155, steps: 40),
            new StanceCase(true, false, SkatePopKind.Ollie, "Switch Frontside 180", 175, spin: 205, steps: 40),
            new StanceCase(true, true, SkatePopKind.Ollie, "Fakie Frontside 360", 275, spin: 335, steps: 40),
            new StanceCase(true, false, SkatePopKind.Ollie, "Switch Frontside 360", 275, spin: 385, steps: 40),
            new StanceCase(true, true, SkatePopKind.Nollie, "Fakie Nollie Kickflip", 175, flip: 335.0 / 360, steps: 40),
            new StanceCase(true, false, SkatePopKind.Nollie, "Switch Nollie Kickflip", 175, flip: 385.0 / 360, steps: 40),
            new StanceCase(true, true, SkatePopKind.Ollie, "Fakie Frontside Pop Shove-it", 175, shove: 155, steps: 40),
            new StanceCase(true, false, SkatePopKind.Ollie, "Switch Frontside Pop Shove-it", 175, shove: 205, steps: 40) })
            StanceLand(fixture, StanceAir(fixture, new SkateTrickState(fixture.Switch)));
        foreach (var fixture in new[] {
            new StanceCase(true, true, SkatePopKind.Ollie, "", 0, spin: 154.9999, steps: 40),
            new StanceCase(true, false, SkatePopKind.Ollie, "", 0, spin: 205.0001, steps: 40),
            new StanceCase(true, true, SkatePopKind.Nollie, "", 0, flip: 334.9999 / 360, steps: 40),
            new StanceCase(true, false, SkatePopKind.Nollie, "", 0, shove: 205.0001, steps: 40) })
        {
            var result = Trick(StanceAir(fixture, new SkateTrickState(fixture.Switch)), StanceInput(fixture, air: false));
            Check(result.Events == SkateTrickEvents.Bailed && result.Points == 0 && !result.State.PopTailLeads &&
                !StanceNaming(result.State), "outside snap tolerance bails and clears naming metadata");
        }
        foreach (var fixture in new[] {
            new StanceCase(true, true, SkatePopKind.Nollie, "Fakie Nollie Multiple Rotation Trick", 475, spin: 900, steps: 60),
            new StanceCase(true, false, SkatePopKind.Nollie, "Switch Nollie Multiple Rotation Trick", 275, flip: 3, steps: 60),
            new StanceCase(true, true, SkatePopKind.Nollie, "Fakie Nollie Body Varial Late Multiple Rotation Trick", 375,
                flip: 3, body: 180, late: true, steps: 60),
            new StanceCase(true, false, SkatePopKind.Ollie, "Switch Late Kickflip", 175, flip: 1, late: true),
            new StanceCase(true, true, SkatePopKind.Ollie, "Fakie Body Varial", 175, body: 180) })
        {
            var airborne = StanceAir(fixture, new SkateTrickState(fixture.Switch));
            var expected = StanceLand(fixture, airborne).TrickName; var input = StanceInput(fixture, air: false);
            SkateTrickResult result; string error;
            for (int i = 0; i < 200; i++) Check(SkateTricks.TryStep(airborne, input, 0.05, out result, out error), "warm explicit cached landing");
            long before = GC.GetAllocatedBytesForCurrentThread(); bool success = true;
            for (int i = 0; i < 20000; i++)
                success &= SkateTricks.TryStep(airborne, input, 0.05, out result, out error) && object.ReferenceEquals(expected, result.TrickName);
            Check(success && GC.GetAllocatedBytesForCurrentThread() - before == 0, "explicit normal/fallback cached names allocate zero bytes");
        }
    }

    private static void StanceRotationTableAndInvalidHistory()
    {
        var rotations = new[] { -360.0, -180, 0, 180, 360 };
        var names = new[,] {
            { "Backside 360", "Backside 180", "Ollie", "Frontside 180", "Frontside 360" },
            { "Nollie Backside 360", "Nollie Backside 180", "Nollie", "Nollie Frontside 180", "Nollie Frontside 360" },
            { "Switch Backside 360", "Switch Backside 180", "Switch Ollie", "Switch Frontside 180", "Switch Frontside 360" },
            { "Switch Nollie Backside 360", "Switch Nollie Backside 180", "Switch Nollie", "Switch Nollie Frontside 180", "Switch Nollie Frontside 360" },
            { "Fakie Backside 360", "Fakie Backside 180", "Fakie Ollie", "Fakie Frontside 180", "Fakie Frontside 360" },
            { "Fakie Nollie Backside 360", "Fakie Nollie Backside 180", "Fakie Nollie", "Fakie Nollie Frontside 180", "Fakie Nollie Frontside 360" }
        };
        for (int row = 0; row < 6; row++)
            for (int column = 0; column < rotations.Length; column++)
            {
                bool ridingSwitch = row >= 2, tailLeads = row >= 4;
                var fixture = new StanceCase(ridingSwitch, tailLeads,
                    row % 2 == 0 ? SkatePopKind.Ollie : SkatePopKind.Nollie, names[row, column],
                    50 + (long)(Math.Abs(rotations[column]) / 180) * 100 + (ridingSwitch ? 25 : 0),
                    spin: rotations[column], steps: 40);
                StanceLand(fixture, StanceAir(fixture, new SkateTrickState(ridingSwitch)));
            }
        var captured = new StanceCase(true, true, SkatePopKind.Ollie, "Fakie Ollie", 75);
        var airborne = StanceAir(captured, new SkateTrickState(true));
        var ground = StanceLand(captured, airborne).State;
        foreach (var fieldName in new[] { "PopTailLeads", "StanceNaming" })
        {
            object malformed = ground;
            typeof(SkateTrickState).GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SetValue(malformed, true);
            RejectTrick((SkateTrickState)malformed, AirInput());
        }
        object inconsistent = airborne;
        typeof(SkateTrickState).GetField("StanceNaming", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(inconsistent, false);
        RejectTrick((SkateTrickState)inconsistent, AirInput());
    }

    public static int Main(string[] args)
    {
        try
        {
            Check(args.Length <= 1, "optional argument is expected actual-DLL SHA-256");
            string library = typeof(SkateTricks).Assembly.Location, digest;
            using (var hash = SHA256.Create())
                digest = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(library))).Replace("-", "").ToLowerInvariant();
            Console.WriteLine("Actual library: " + library + "\nSHA-256: " + digest);
            if (args.Length == 1) Check(string.Equals(args[0], digest, StringComparison.OrdinalIgnoreCase), "actual DLL hash pinned");
            LateRecognitionAndCompatibility(); BodyCircleRecognitionAndResets(); ExistingConstructors(); ReaderToScoringAndStance();
            CatchBailAndClamp(); InvalidAndCachedAllocation();
            ExplicitStanceVocabulary(); SequentialStanceSignals(); ExplicitSnapAndFallbackCaches();
            StanceRotationTableAndInvalidHistory();
            Console.WriteLine("skate_late_flips_and_body_varials: " + checks + " checks passed");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
