using System;

namespace Shortcut.RustMod
{
    public enum SkatePopKind { None = 0, Ollie = 1, Nollie = 2, NoComply = 3, Boneless = 4, Fastplant = 5, Beanplant = 6 }

    public struct SkatePop
    {
        public readonly SkatePopKind Kind;
        public readonly double FlipTurns;
        public readonly double ShoveDegrees;
        public readonly double Strength;
        public bool Late { get { return Kind == SkatePopKind.None && (FlipTurns != 0 || ShoveDegrees != 0); } }
        public SkatePop(SkatePopKind kind, double flipTurns, double shoveDegrees, double strength)
        { Kind = kind; FlipTurns = flipTurns; ShoveDegrees = shoveDegrees; Strength = strength; }
    }

    [Flags]
    public enum SkateFlickEvents
    {
        None = 0,
        Ollie = 1,
        Nollie = 2,
        Kickflip = 4,
        Heelflip = 8,
        ShoveFrontside = 16,
        ShoveBackside = 32,
        BodyVarial = 64
    }

    public struct SkateFlickInput
    {
        public readonly double X;
        public readonly double Y;
        public readonly bool Airborne;
        public readonly bool Mirrored;
        public readonly double LeftX, LeftY;
        public readonly bool PoppedInAir;

        public SkateFlickInput(double x, double y, bool airborne, bool mirrored)
            : this(x, y, airborne, mirrored, 0, 0, false) { }
        public SkateFlickInput(double x, double y, bool airborne, bool mirrored,
                               double leftX, double leftY, bool poppedInAir)
        { X = x; Y = y; Airborne = airborne; Mirrored = mirrored;
          LeftX = leftX; LeftY = leftY; PoppedInAir = poppedInAir; }
    }

    public struct SkateFlickState
    {
        internal int Phase;
        internal bool Forward;
        internal double SetSeconds;
        internal double HeldSeconds;
        internal double FlickSeconds;
        internal double RimSeconds;
        internal double BestX;
        internal double BestY;
        internal double BandSeconds;
        internal double OutSeconds;
        internal bool Manual;
        internal bool NoseManual;
        internal int SetupSide;
        internal int SideLead;
        internal int ShallowSide;
        internal bool RimValid;
        internal double RimAngle;
        internal double SweepDegrees;
        internal double PreviousX;
        internal double PreviousY;
        internal double PreviousSeconds;
        internal bool BandForward;
        internal bool BodyRimValid, BodySpent;
        internal double BodyRimAngle, BodySweepDegrees, BodySeconds;
    }

    public struct SkateFlickResult
    {
        public readonly SkateFlickState State;
        public readonly SkateFlickEvents Events;
        public readonly double Pop;
        public readonly double Charge;
        public readonly bool Charging;
        public readonly bool Manual;
        public readonly bool NoseManual;
        public readonly SkatePop Composition;
        public readonly double BodyVarialDegrees;

        public SkateFlickResult(SkateFlickState state, SkateFlickEvents events, double pop, double charge, bool charging, bool manual, bool noseManual)
            : this(state, events, pop, charge, charging, manual, noseManual, Legacy(events, pop)) { }
        public SkateFlickResult(SkateFlickState state, SkateFlickEvents events, double pop, double charge,
                                bool charging, bool manual, bool noseManual, SkatePop composition)
            : this(state, events, pop, charge, charging, manual, noseManual, composition, 0) { }
        public SkateFlickResult(SkateFlickState state, SkateFlickEvents events, double pop, double charge,
                                bool charging, bool manual, bool noseManual, SkatePop composition, double bodyVarialDegrees)
        { State = state; Events = events; Pop = pop; Charge = charge; Charging = charging;
          Manual = manual; NoseManual = noseManual; Composition = composition; BodyVarialDegrees = bodyVarialDegrees; }

        private static SkatePop Legacy(SkateFlickEvents events, double strength)
        {
            var kind = (events & SkateFlickEvents.Nollie) != 0 ? SkatePopKind.Nollie :
                       (events & SkateFlickEvents.Ollie) != 0 ? SkatePopKind.Ollie : SkatePopKind.None;
            double flip = (events & SkateFlickEvents.Kickflip) != 0 ? 1 : (events & SkateFlickEvents.Heelflip) != 0 ? -1 : 0;
            double shove = (events & SkateFlickEvents.ShoveFrontside) != 0 ? 180 : (events & SkateFlickEvents.ShoveBackside) != 0 ? -180 : 0;
            return new SkatePop(kind, flip, shove, strength);
        }
    }

    // A step describes the position held during dt, including the position that just ended when
    // an adapter receives only changes. Longer holds must be subdivided with that same position.
    public static class SkateFlick
    {
        public const double MaximumStep = 0.1;
        public const double DeadZone = 0.25;
        public const double HeldZone = 0.7;
        public const double HeldSpreadDegrees = 40;
        public const double SetSeconds = 0.03;
        public const double FlickSeconds = 0.3;
        public const double FlickReach = 0.45;
        public const double FireReach = 0.8;
        public const double FlickSide = 0.15;
        public const double ShoveSide = 0.2;
        public const double SettleSeconds = 0.025;
        public const double PeakDrop = 0.15;
        public const double StraightSpreadDegrees = 22.5;
        public const double FlipSpreadDegrees = 67.5;
        public const double AirFlickSeconds = 0.18;
        public const double AirSpreadDegrees = 112.5;
        public const double ManualFrom = 0.25;
        public const double ManualTo = 0.62;
        public const double ManualSide = 0.35;
        public const double ManualSeconds = 0.2;
        public const double ManualGrace = 0.06;
        public const double FullCharge = 0.3;
        public const double MinimumPop = 0.6;
        public const double BodyCircleSeconds = 1;

        private const int Rest = 0, Held = 1, Flicking = 2, Spent = 3, AirFlick = 4;
        private const double MaximumTimer = 86400;
        private const double HalfSweepDegrees = 157.5;
        private const double QuarterSweepDegrees = 67.5;
        private const double SettlingDegreesPerSecond = 120;

        public static bool TryStep(SkateFlickState state, SkateFlickInput input, double dt, out SkateFlickResult result, out string error)
        {
            result = new SkateFlickResult(state, SkateFlickEvents.None, 0, 0, false, state.Manual, state.NoseManual);
            error = null;
            if (!Finite(input.X) || !Finite(input.Y) || Math.Abs(input.X) > 1.0001 || Math.Abs(input.Y) > 1.0001 ||
                !Finite(input.LeftX) || !Finite(input.LeftY) || Math.Abs(input.LeftX) > 1.0001 || Math.Abs(input.LeftY) > 1.0001 ||
                !Finite(dt) || !(dt > 0) || dt > MaximumStep || !Valid(state))
            { error = "Invalid skate flick request."; return false; }

            double x = input.X, y = input.Y;
            double far = Math.Sqrt(x * x + y * y);
            bool heldBack = far >= HeldZone && Degrees(Math.Abs(x), -y) <= FlipSpreadDegrees;
            bool heldForward = far >= HeldZone && Degrees(Math.Abs(x), y) <= FlipSpreadDegrees;
            var events = SkateFlickEvents.None;
            double pop = 0;
            var composition = default(SkatePop);

            if (state.Phase == Rest)
            {
                bool late = input.Airborne && far > DeadZone && Degrees(Math.Abs(x), y) > StraightSpreadDegrees &&
                            Degrees(Math.Abs(x), y) <= AirSpreadDegrees;
                if (late)
                { state.Phase = AirFlick; state.FlickSeconds = 0; }
                else if (heldBack || heldForward)
                {
                    if (state.SetSeconds == 0 || state.Forward != heldForward)
                    {
                        state.SetSeconds = 0; state.HeldSeconds = 0; state.SweepDegrees = 0; state.ShallowSide = 0;
                        state.RimValid = false;
                        state.SetupSide = Degrees(Math.Abs(x), heldForward ? y : -y) > HeldSpreadDegrees ? Math.Sign(x) : 0;
                    }
                    state.Forward = heldForward;
                    state.SetSeconds = Time(state.SetSeconds, dt);
                    Track(ref state, x, state.Forward ? -y : y, far);
                    if (state.SetSeconds >= SetSeconds) { state.Phase = Held; state.HeldSeconds = state.SetSeconds; }
                }
                else
                {
                    state.SetSeconds = 0;
                    if (!input.Airborne && far >= FireReach && Math.Abs(y) <= ShoveSide * far) state.SideLead = Math.Sign(x);
                    else if (far <= DeadZone) state.SideLead = 0;
                }
            }
            else if (state.Phase == Held)
            {
                Track(ref state, x, state.Forward ? -y : y, far);
                if (state.Forward ? heldForward : heldBack) state.HeldSeconds = Time(state.HeldSeconds, dt);
                else { state.Phase = Flicking; state.FlickSeconds = 0; state.RimSeconds = 0; state.BestX = 0; state.BestY = 0; }
            }

            if (state.Phase == Flicking)
            {
                double toward = state.Forward ? -y : y;
                if (state.Forward ? heldForward : heldBack) { state.Phase = Held; state.RimSeconds = 0; }
                else
                {
                    Track(ref state, x, toward, far);
                    state.FlickSeconds = Time(state.FlickSeconds, dt);
                    double best = Math.Sqrt(state.BestX * state.BestX + state.BestY * state.BestY);
                    if (toward > FlickSide && far > best) { state.BestX = x; state.BestY = y; best = far; }
                    if (far >= FireReach && toward > -ShoveSide)
                    {
                        if (state.PreviousSeconds > 0 && AngularChange(state.PreviousX, state.PreviousY, x, y) >
                            SettlingDegreesPerSecond * state.PreviousSeconds) state.RimSeconds = 0;
                        state.RimSeconds = Time(state.RimSeconds, dt);
                    }
                    else state.RimSeconds = 0;
                    bool settled = state.RimSeconds >= SettleSeconds;
                    bool turned = best >= FlickReach && ((far < FireReach && far < best - PeakDrop) || state.FlickSeconds > FlickSeconds);
                    if (settled || turned)
                    {
                        pop = MinimumPop + (1 - MinimumPop) * Math.Min(1, state.HeldSeconds / FullCharge);
                        composition = Compose(state, settled ? x : state.BestX,
                            settled ? toward : state.Forward ? -state.BestY : state.BestY, input.Mirrored, pop);
                        if (input.Airborne && input.PoppedInAir)
                        {
                            pop = 0;
                            composition = new SkatePop(SkatePopKind.None, composition.FlipTurns, composition.ShoveDegrees, 0);
                        }
                        events = Flags(composition);
                        state.Phase = Spent;
                    }
                    else if (state.FlickSeconds > FlickSeconds)
                    { state.Phase = Rest; state.SetSeconds = 0; state.SideLead = 0; state.SetupSide = 0; state.SweepDegrees = 0; state.ShallowSide = 0; state.RimValid = false; }
                }
            }
            else if (state.Phase == AirFlick)
            {
                state.FlickSeconds = Time(state.FlickSeconds, dt);
                if (!input.Airborne || far <= DeadZone) state.Phase = Rest;
                else if (state.FlickSeconds > AirFlickSeconds) state.Phase = Degrees(Math.Abs(x), y) <= AirSpreadDegrees ? Spent : Rest;
                else if (far >= FireReach)
                {
                    double angle = Degrees(Math.Abs(x), y);
                    double flip = angle > StraightSpreadDegrees && angle <= AirSpreadDegrees ? ((x < 0) != input.Mirrored ? 1 : -1) : 0;
                    composition = new SkatePop(SkatePopKind.None, flip, 0, 0);
                    events = Flags(composition);
                    state.Phase = events == SkateFlickEvents.None ? Rest : Spent;
                }
                if (state.Phase == Rest) state.SetSeconds = 0;
            }
            else if (state.Phase == Spent && far <= DeadZone)
            { state.Phase = Rest; state.SetSeconds = 0; state.SideLead = 0; state.SetupSide = 0; state.SweepDegrees = 0; state.ShallowSide = 0; state.RimValid = false; }

            double bodyVarial = StepBody(ref state, input, dt);
            if (bodyVarial != 0) events |= SkateFlickEvents.BodyVarial;
            bool charging = state.Phase == Held;
            bool backBand = -y >= ManualFrom && -y <= ManualTo && Math.Abs(x) <= ManualSide;
            bool forwardBand = y >= ManualFrom && y <= ManualTo && Math.Abs(x) <= ManualSide;
            bool keeps = (state.Manual && (backBand || (charging && !state.Forward))) || (state.NoseManual && (forwardBand || (charging && state.Forward)));
            if ((backBand || forwardBand) && state.Phase != Held && state.Phase != Flicking)
            {
                if (state.BandSeconds > 0 && state.BandForward != forwardBand) state.BandSeconds = 0;
                state.BandForward = forwardBand;
                state.BandSeconds = Time(state.BandSeconds, dt); state.OutSeconds = 0;
                if (state.BandSeconds >= ManualSeconds) { state.Manual = backBand; state.NoseManual = forwardBand; }
            }
            else if (keeps) state.OutSeconds = 0;
            else
            {
                state.BandSeconds = 0;
                state.OutSeconds = Time(state.OutSeconds, dt);
                if (state.OutSeconds > ManualGrace || events != SkateFlickEvents.None) { state.Manual = false; state.NoseManual = false; }
            }
            if (events != SkateFlickEvents.None) { state.Manual = state.NoseManual = false; state.BandSeconds = 0; }

            double charge = charging ? Math.Min(1, state.HeldSeconds / FullCharge) : 0;
            state.PreviousX = x; state.PreviousY = y; state.PreviousSeconds = dt;
            result = new SkateFlickResult(state, events, pop, charge, charging, state.Manual, state.NoseManual, composition, bodyVarial);
            return true;
        }

        private static double StepBody(ref SkateFlickState state, SkateFlickInput input, double dt)
        {
            if (!input.Airborne) { ResetBody(ref state); state.BodySpent = false; return 0; }
            if (state.BodySpent) return 0;
            double far = Math.Sqrt(input.LeftX * input.LeftX + input.LeftY * input.LeftY);
            if (far < HeldZone) { ResetBody(ref state); return 0; }
            double angle = Math.Atan2(input.LeftX, input.LeftY) * 180 / Math.PI;
            if (!state.BodyRimValid) { StartBody(ref state, angle, dt); return 0; }
            double seconds = state.BodySeconds + dt;
            double change = Delta(state.BodyRimAngle, angle);
            if (seconds > BodyCircleSeconds + 1e-10 || Math.Abs(change) > 90.000001)
            { StartBody(ref state, angle, dt); return 0; }
            if (change * state.BodySweepDegrees < 0)
            { state.BodySweepDegrees = 0; seconds = dt; }
            state.BodyRimAngle = angle;
            state.BodySeconds = Math.Min(BodyCircleSeconds, seconds);
            state.BodySweepDegrees = Math.Max(-360, Math.Min(360, state.BodySweepDegrees + change));
            if (Math.Abs(state.BodySweepDegrees) < 360 - 1e-6) return 0;
            double degrees = Math.Sign(state.BodySweepDegrees) * (input.Mirrored ? -180 : 180);
            ResetBody(ref state); state.BodySpent = true;
            return degrees;
        }

        private static void StartBody(ref SkateFlickState state, double angle, double dt)
        { state.BodyRimValid = true; state.BodyRimAngle = angle; state.BodySweepDegrees = 0; state.BodySeconds = dt; }

        private static void ResetBody(ref SkateFlickState state)
        { state.BodyRimValid = false; state.BodyRimAngle = state.BodySweepDegrees = state.BodySeconds = 0; }

        private static SkatePop Compose(SkateFlickState state, double x, double toward, bool mirrored, double strength)
        {
            double angle = Degrees(Math.Abs(x), toward);
            double flip = angle > StraightSpreadDegrees && angle <= FlipSpreadDegrees ? ((x < 0) != mirrored ? 1 : -1) : 0;
            double shove = 0;
            if (flip != 0 && state.SideLead == -Math.Sign(x)) shove = -flip * 360;
            else if (flip != 0 && state.SetupSide == -Math.Sign(x)) shove = -flip * 180;
            else if (Math.Abs(state.SweepDegrees) >= HalfSweepDegrees)
                shove = -Math.Sign(state.SweepDegrees) * (mirrored ? -360 : 360);
            else if (Math.Abs(state.SweepDegrees) >= QuarterSweepDegrees || angle > FlipSpreadDegrees)
                shove = -(angle > FlipSpreadDegrees ? -Math.Sign(x) : Math.Sign(state.SweepDegrees)) * (mirrored ? -180 : 180);
            else if (angle <= StraightSpreadDegrees && state.ShallowSide != 0)
            { flip = (state.ShallowSide < 0) != mirrored ? 1 : -1; shove = flip * 180; }
            return new SkatePop(state.Forward ? SkatePopKind.Nollie : SkatePopKind.Ollie, flip, shove, strength);
        }

        private static SkateFlickEvents Flags(SkatePop pop)
        {
            var flags = pop.Kind == SkatePopKind.Ollie ? SkateFlickEvents.Ollie : pop.Kind == SkatePopKind.Nollie ? SkateFlickEvents.Nollie : SkateFlickEvents.None;
            if (pop.FlipTurns > 0) flags |= SkateFlickEvents.Kickflip; else if (pop.FlipTurns < 0) flags |= SkateFlickEvents.Heelflip;
            if (pop.ShoveDegrees > 0) flags |= SkateFlickEvents.ShoveFrontside; else if (pop.ShoveDegrees < 0) flags |= SkateFlickEvents.ShoveBackside;
            return flags;
        }

        private static void Track(ref SkateFlickState state, double x, double toward, double far)
        {
            if (far < FireReach) { state.RimValid = false; return; }
            double angle = Math.Atan2(-x, -toward) * 180 / Math.PI;
            if (state.RimValid)
            {
                double change = Delta(state.RimAngle, angle);
                if (Math.Abs(change) <= 90.000001) state.SweepDegrees = Math.Max(-360, Math.Min(360, state.SweepDegrees + change));
            }
            state.RimValid = true; state.RimAngle = angle;
            if (toward < 0 && Math.Abs(angle) <= StraightSpreadDegrees)
            { state.SetupSide = 0; state.SweepDegrees = 0; state.ShallowSide = 0; }
            else if (toward < 0 && Math.Abs(angle) > HeldSpreadDegrees && Math.Abs(angle) <= FlipSpreadDegrees &&
                     Math.Abs(state.SweepDegrees) > StraightSpreadDegrees) state.ShallowSide = Math.Sign(x);
        }

        private static double AngularChange(double ax, double ay, double bx, double by)
        {
            if (ax * ax + ay * ay <= DeadZone * DeadZone) return 180;
            return Math.Abs(Delta(Math.Atan2(ax, ay) * 180 / Math.PI, Math.Atan2(bx, by) * 180 / Math.PI));
        }
        private static double Delta(double from, double to)
        { double delta = to - from; if (delta > 180) delta -= 360; if (delta < -180) delta += 360; return delta; }
        private static double Time(double current, double dt) { return Math.Min(MaximumTimer, current + dt); }
        private static bool Timer(double value) { return Finite(value) && value >= 0 && value <= MaximumTimer; }
        private static bool Side(int value) { return value >= -1 && value <= 1; }
        private static bool Valid(SkateFlickState state)
        {
            return state.Phase >= Rest && state.Phase <= AirFlick && Timer(state.SetSeconds) && Timer(state.HeldSeconds) &&
                Timer(state.FlickSeconds) && Timer(state.RimSeconds) && Timer(state.BandSeconds) && Timer(state.OutSeconds) &&
                Finite(state.BestX) && Math.Abs(state.BestX) <= 1.0001 && Finite(state.BestY) && Math.Abs(state.BestY) <= 1.0001 &&
                Finite(state.RimAngle) && Math.Abs(state.RimAngle) <= 180 && Finite(state.SweepDegrees) && Math.Abs(state.SweepDegrees) <= 360 &&
                Finite(state.PreviousX) && Math.Abs(state.PreviousX) <= 1.0001 && Finite(state.PreviousY) && Math.Abs(state.PreviousY) <= 1.0001 &&
                Finite(state.PreviousSeconds) && state.PreviousSeconds >= 0 && state.PreviousSeconds <= MaximumStep &&
                Side(state.SetupSide) && Side(state.SideLead) && Side(state.ShallowSide) && !(state.Manual && state.NoseManual) &&
                Finite(state.BodyRimAngle) && Math.Abs(state.BodyRimAngle) <= 180 &&
                Finite(state.BodySweepDegrees) && Math.Abs(state.BodySweepDegrees) < 360 - 1e-6 &&
                Finite(state.BodySeconds) && state.BodySeconds >= 0 && state.BodySeconds <= BodyCircleSeconds &&
                (state.BodyRimValid ? state.BodySeconds > 0 :
                    state.BodyRimAngle == 0 && state.BodySweepDegrees == 0 && state.BodySeconds == 0) &&
                !(state.BodySpent && state.BodyRimValid);
        }

        private static double Degrees(double across, double along) { return Math.Atan2(across, along) * 180 / Math.PI; }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
