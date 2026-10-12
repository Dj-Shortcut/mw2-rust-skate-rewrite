using System;

namespace Shortcut.RustMod
{
    [Flags]
    public enum SkateTrickEvents { None = 0, Landed = 1, Bailed = 2, GrindEnded = 4, ComboBanked = 8, ManualEnded = 16 }

    public enum SkateGrabKind
    {
        None = 0, Legacy = 1, Backside = 2, Frontside = 3, Double = 4, Tail = 5, Nose = 6,
        Stalefish = 7, Crail = 8, Seatbelt = 9, Melon = 10, Method = 11, Nosebone = 12, Tailbone = 13
    }

    public enum SkateGrindKind
    {
        None = 0, FiftyFifty = 1, FiveO = 2, Nosegrind = 3, Boardslide = 4, Lipslide = 5,
        Noseslide = 6, Tailslide = 7, Smith = 8, Feeble = 9, Crooked = 10,
        FiftyFiftyStall = 11, NoseStall = 12, TailStall = 13, BoardStall = 14, Handplant = 15,
        Bluntslide = 16, Nosebluntslide = 17, Darkslide = 18
    }

    public struct SkateTrickInput
    {
        public readonly double Spin, Flip, Shove, ImpactSpeed, RollingSpeed;
        public readonly bool Grab, Airborne, Grinding, Manual, NoseManual;
        public readonly SkateGrabKind GrabKind;
        public readonly SkatePopKind PopKind;
        public readonly SkateGrindKind GrindKind;
        public readonly bool GrindFrontside;
        public readonly bool Late;
        public readonly double BodySpin;
        public readonly bool TailLeads;
        internal readonly bool LegacyNaming, StanceNaming;
        public SkateTrickInput(double spin, double flip, bool grab, bool airborne, bool grinding, double impactSpeed)
            : this(spin, flip, grab, airborne, grinding, impactSpeed, false, 0) { }
        public SkateTrickInput(double spin, double flip, bool grab, bool airborne, bool grinding,
                               double impactSpeed, bool manual, double rollingSpeed)
            : this(spin, flip, 0, grab ? SkateGrabKind.Legacy : SkateGrabKind.None, airborne, grinding,
                impactSpeed, manual, false, rollingSpeed, SkatePopKind.Ollie, true) { }
        public SkateTrickInput(double spin, double flip, double shove, SkateGrabKind grabKind, bool airborne,
                               bool grinding, double impactSpeed, bool manual, bool noseManual,
                               double rollingSpeed, SkatePopKind popKind)
            : this(spin, flip, shove, grabKind, airborne, grinding, impactSpeed, manual, noseManual,
                rollingSpeed, popKind, false) { }
        private SkateTrickInput(double spin, double flip, double shove, SkateGrabKind grabKind, bool airborne,
                                bool grinding, double impactSpeed, bool manual, bool noseManual,
                                double rollingSpeed, SkatePopKind popKind, bool legacyNaming)
            : this(spin, flip, shove, grabKind, airborne, grinding, impactSpeed, manual, noseManual,
                rollingSpeed, popKind, legacyNaming, grinding ? SkateGrindKind.FiftyFifty : SkateGrindKind.None, false) { }
        public SkateTrickInput(double spin, double flip, double shove, SkateGrabKind grabKind, bool airborne,
                               bool grinding, double impactSpeed, bool manual, bool noseManual,
                               double rollingSpeed, SkatePopKind popKind, SkateGrindKind grindKind, bool grindFrontside)
            : this(spin, flip, shove, grabKind, airborne, grinding, impactSpeed, manual, noseManual,
                rollingSpeed, popKind, false, grindKind, grindFrontside) { }
        public SkateTrickInput(double spin, double flip, double shove, SkateGrabKind grabKind, bool airborne,
                               bool grinding, double impactSpeed, bool manual, bool noseManual,
                               double rollingSpeed, SkatePopKind popKind, SkateGrindKind grindKind, bool grindFrontside,
                               bool late, double bodySpin)
            : this(spin, flip, shove, grabKind, airborne, grinding, impactSpeed, manual, noseManual,
                rollingSpeed, popKind, false, grindKind, grindFrontside, late, bodySpin) { }
        public SkateTrickInput(double spin, double flip, double shove, SkateGrabKind grabKind, bool airborne,
                               bool grinding, double impactSpeed, bool manual, bool noseManual,
                               double rollingSpeed, SkatePopKind popKind, SkateGrindKind grindKind, bool grindFrontside,
                               bool late, double bodySpin, bool tailLeads)
            : this(spin, flip, shove, grabKind, airborne, grinding, impactSpeed, manual, noseManual,
                rollingSpeed, popKind, false, grindKind, grindFrontside, late, bodySpin, tailLeads, true) { }
        private SkateTrickInput(double spin, double flip, double shove, SkateGrabKind grabKind, bool airborne,
                                bool grinding, double impactSpeed, bool manual, bool noseManual,
                                double rollingSpeed, SkatePopKind popKind, bool legacyNaming,
                                SkateGrindKind grindKind, bool grindFrontside)
            : this(spin, flip, shove, grabKind, airborne, grinding, impactSpeed, manual, noseManual,
                rollingSpeed, popKind, legacyNaming, grindKind, grindFrontside, false, 0) { }
        private SkateTrickInput(double spin, double flip, double shove, SkateGrabKind grabKind, bool airborne,
                                bool grinding, double impactSpeed, bool manual, bool noseManual,
                                double rollingSpeed, SkatePopKind popKind, bool legacyNaming,
                                SkateGrindKind grindKind, bool grindFrontside, bool late, double bodySpin)
            : this(spin, flip, shove, grabKind, airborne, grinding, impactSpeed, manual, noseManual,
                rollingSpeed, popKind, legacyNaming, grindKind, grindFrontside, late, bodySpin, false, false) { }
        private SkateTrickInput(double spin, double flip, double shove, SkateGrabKind grabKind, bool airborne,
                                bool grinding, double impactSpeed, bool manual, bool noseManual,
                                double rollingSpeed, SkatePopKind popKind, bool legacyNaming,
                                SkateGrindKind grindKind, bool grindFrontside, bool late, double bodySpin,
                                bool tailLeads, bool stanceNaming)
        { Spin = spin; Flip = flip; Shove = shove; GrabKind = grabKind; Grab = grabKind != SkateGrabKind.None;
          Airborne = airborne; Grinding = grinding; ImpactSpeed = impactSpeed; Manual = manual;
          NoseManual = noseManual; RollingSpeed = rollingSpeed; PopKind = popKind; LegacyNaming = legacyNaming;
          GrindKind = grindKind; GrindFrontside = grindFrontside; Late = late; BodySpin = bodySpin;
          TailLeads = tailLeads; StanceNaming = stanceNaming; }
    }

    public struct SkateTrickState
    {
        public readonly double Spin, Flip, Shove, GrindSeconds, RollingSeconds, ManualSeconds;
        public readonly bool Airborne, Grinding, Grabbed, Switch, Manualing, NoseManualing;
        public readonly SkatePopKind PopKind;
        public readonly SkateGrabKind GrabKind;
        public readonly long ComboBasePoints, TotalPoints;
        public readonly int ComboCount;
        public readonly SkateGrindKind GrindKind;
        public readonly bool GrindFrontside, PopSwitch, PopTailLeads;
        public readonly bool Late;
        public readonly double BodySpin;
        internal readonly bool LegacyNaming, StanceNaming;
        public SkateTrickState(bool ridingSwitch) : this(0, 0, 0, 0, false, false, false, ridingSwitch, 0, 0, 0) { }
        public bool DarkslideCaught
        { get { return !Airborne && Grinding && GrindKind == SkateGrindKind.Darkslide && Math.Abs(Flip) == 180; } }
        internal SkateTrickState(double spin, double flip, double grind, double rolling, bool air, bool grinding,
                                 bool grab, bool ridingSwitch, long combo, long total, int count)
            : this(spin, flip, grind, rolling, air, grinding, grab, ridingSwitch, combo, total, count, 0, false) { }
        internal SkateTrickState(double spin, double flip, double grind, double rolling, bool air, bool grinding,
                                 bool grab, bool ridingSwitch, long combo, long total, int count,
                                 double manualSeconds, bool manualing)
            : this(spin, flip, 0, grind, rolling, air, grinding, grab ? SkateGrabKind.Legacy : SkateGrabKind.None,
                ridingSwitch, combo, total, count, manualSeconds, manualing, false,
                air ? SkatePopKind.Ollie : SkatePopKind.None, true) { }
        internal SkateTrickState(double spin, double flip, double shove, double grind, double rolling,
                                 bool air, bool grinding, SkateGrabKind grabKind, bool ridingSwitch,
                                 long combo, long total, int count, double manualSeconds, bool manualing,
                                 bool noseManualing, SkatePopKind popKind, bool legacyNaming)
            : this(spin, flip, shove, grind, rolling, air, grinding, grabKind, ridingSwitch, combo, total, count,
                manualSeconds, manualing, noseManualing, popKind, legacyNaming,
                grinding ? SkateGrindKind.FiftyFifty : SkateGrindKind.None, false, false) { }
        internal SkateTrickState(double spin, double flip, double shove, double grind, double rolling,
                                 bool air, bool grinding, SkateGrabKind grabKind, bool ridingSwitch,
                                 long combo, long total, int count, double manualSeconds, bool manualing,
                                 bool noseManualing, SkatePopKind popKind, bool legacyNaming,
                                 SkateGrindKind grindKind, bool grindFrontside, bool popSwitch)
            : this(spin, flip, shove, grind, rolling, air, grinding, grabKind, ridingSwitch, combo, total, count,
                manualSeconds, manualing, noseManualing, popKind, legacyNaming, grindKind, grindFrontside, popSwitch, false, 0) { }
        internal SkateTrickState(double spin, double flip, double shove, double grind, double rolling,
                                 bool air, bool grinding, SkateGrabKind grabKind, bool ridingSwitch,
                                 long combo, long total, int count, double manualSeconds, bool manualing,
                                 bool noseManualing, SkatePopKind popKind, bool legacyNaming,
                                 SkateGrindKind grindKind, bool grindFrontside, bool popSwitch, bool late, double bodySpin)
            : this(spin, flip, shove, grind, rolling, air, grinding, grabKind, ridingSwitch, combo, total, count,
                manualSeconds, manualing, noseManualing, popKind, legacyNaming, grindKind, grindFrontside,
                popSwitch, late, bodySpin, false, false) { }
        internal SkateTrickState(double spin, double flip, double shove, double grind, double rolling,
                                 bool air, bool grinding, SkateGrabKind grabKind, bool ridingSwitch,
                                 long combo, long total, int count, double manualSeconds, bool manualing,
                                 bool noseManualing, SkatePopKind popKind, bool legacyNaming,
                                 SkateGrindKind grindKind, bool grindFrontside, bool popSwitch, bool late, double bodySpin,
                                 bool popTailLeads, bool stanceNaming)
        { Spin = spin; Flip = flip; Shove = shove; GrindSeconds = grind; RollingSeconds = rolling;
          Airborne = air; Grinding = grinding; GrabKind = grabKind; Grabbed = grabKind != SkateGrabKind.None;
          Switch = ridingSwitch; ComboBasePoints = combo; TotalPoints = total; ComboCount = count;
          ManualSeconds = manualSeconds; Manualing = manualing; NoseManualing = noseManualing;
          PopKind = popKind; LegacyNaming = legacyNaming; GrindKind = grindKind; GrindFrontside = grindFrontside;
          PopSwitch = popSwitch; Late = late; BodySpin = bodySpin;
          PopTailLeads = popTailLeads; StanceNaming = stanceNaming; }
    }

    public struct SkateTrickResult
    {
        public readonly SkateTrickState State;
        public readonly double BoardSpin, BoardFlip, BoardShove, GrindSeconds, ManualSeconds;
        public readonly double BodySpin;
        public double RiderSpin { get { return BoardSpin + BodySpin; } }
        public readonly SkateTrickEvents Events;
        public readonly string TrickName;
        public readonly long Points, ComboPoints, BankedPoints;
        public bool Switch { get { return State.Switch; } }
        public bool Manualing { get { return State.Manualing; } }
        public bool NoseManualing { get { return State.NoseManualing; } }
        public long TotalPoints { get { return State.TotalPoints; } }
        internal SkateTrickResult(SkateTrickState state, double spin, double flip, double grind, SkateTrickEvents events,
                                  string name, long points, long banked)
            : this(state, spin, flip, grind, state.ManualSeconds, events, name, points, banked) { }
        internal SkateTrickResult(SkateTrickState state, double spin, double flip, double grind, double manualSeconds,
                                  SkateTrickEvents events, string name, long points, long banked)
            : this(state, spin, flip, state.Shove, grind, manualSeconds, events, name, points, banked) { }
        internal SkateTrickResult(SkateTrickState state, double spin, double flip, double shove, double grind,
                                  double manualSeconds, SkateTrickEvents events, string name, long points, long banked)
            : this(state, spin, flip, shove, grind, manualSeconds, events, name, points, banked, state.BodySpin) { }
        internal SkateTrickResult(SkateTrickState state, double spin, double flip, double shove, double grind,
                                  double manualSeconds, SkateTrickEvents events, string name, long points, long banked,
                                  double bodySpin)
        { State = state; BoardSpin = spin; BoardFlip = flip; BoardShove = shove; GrindSeconds = grind;
          ManualSeconds = manualSeconds; Events = events; TrickName = name; Points = points;
          ComboPoints = SkateTricks.LiveComboValue(state); BankedPoints = banked; BodySpin = bodySpin; }
    }

    public static class SkateTricks
    {
        public const double FlipTolerance = 25;
        public const double SpinTolerance = 25;
        public const double ShoveTolerance = 25;
        public const double ComboRollingTimeout = 1.5;
        public const int MaximumMultiplier = 16;
        public const double MinimumManualSpeed = 0.01;
        public const int ManualPointsPerSecond = 100;
        private const double MaximumManualSeconds = 86400;
        private const long PointsLimit = 1000000000000000;
        private static readonly string[] Names = {
            "Ollie", "Kickflip", "Heelflip", "Double Kickflip", "Double Heelflip",
            "Frontside 180", "Frontside 180 Kickflip", "Frontside 180 Heelflip", "Frontside 180 Double Kickflip", "Frontside 180 Double Heelflip",
            "Backside 180", "Backside 180 Kickflip", "Backside 180 Heelflip", "Backside 180 Double Kickflip", "Backside 180 Double Heelflip",
            "Frontside 360", "Frontside 360 Kickflip", "Frontside 360 Heelflip", "Frontside 360 Double Kickflip", "Frontside 360 Double Heelflip",
            "Backside 360", "Backside 360 Kickflip", "Backside 360 Heelflip", "Backside 360 Double Kickflip", "Backside 360 Double Heelflip",
            "Ollie Grab", "Kickflip Grab", "Heelflip Grab", "Double Kickflip Grab", "Double Heelflip Grab",
            "Frontside 180 Grab", "Frontside 180 Kickflip Grab", "Frontside 180 Heelflip Grab", "Frontside 180 Double Kickflip Grab", "Frontside 180 Double Heelflip Grab",
            "Backside 180 Grab", "Backside 180 Kickflip Grab", "Backside 180 Heelflip Grab", "Backside 180 Double Kickflip Grab", "Backside 180 Double Heelflip Grab",
            "Frontside 360 Grab", "Frontside 360 Kickflip Grab", "Frontside 360 Heelflip Grab", "Frontside 360 Double Kickflip Grab", "Frontside 360 Double Heelflip Grab",
            "Backside 360 Grab", "Backside 360 Kickflip Grab", "Backside 360 Heelflip Grab", "Backside 360 Double Kickflip Grab", "Backside 360 Double Heelflip Grab"
        };
        private static readonly string[] GrindNames = {
            "Grind", "50-50", "5-0", "Nosegrind", "Boardslide", "Lipslide", "Noseslide", "Tailslide", "Smith Grind", "Feeble Grind", "Crooked Grind",
            "50-50 Stall", "Nose Stall", "Tail Stall", "Board Stall", "Handplant",
            "Bluntslide", "Nosebluntslide", "Darkslide"
        };
        private static readonly string[] DirectionalGrindNames = BuildGrindNames();
        private static readonly int[] GrindRates = { 100, 100, 130, 130, 120, 140, 150, 150, 160, 160, 160, 150, 160, 160, 150, 250, 170, 170, 190 };
        private static readonly string[] GrabSuffixes = {
            "", " Grab", " Backside Grab", " Frontside Grab", " Double Grab", " Tail Grab", " Nose Grab",
            " Stalefish", " Crail", " Seatbelt", " Melon", " Method", " Nosebone", " Tailbone"
        };
        private static readonly string[] NamePrefixes = {
            "", "Nollie", "No Comply", "Boneless", "Fastplant", "Beanplant",
            "Switch", "Fakie", "Switch No Comply", "Switch Boneless", "Switch Fastplant", "Switch Beanplant",
            "Switch Nollie", "Fakie Nollie", "Fakie No Comply", "Fakie Boneless", "Fakie Fastplant", "Fakie Beanplant"
        };
        private static readonly string[] ComposedNames = BuildNames();
        private static readonly string[] FallbackNames = BuildFallbackNames();
        private static readonly string[] AirNames = BuildAirNames();
        private static readonly string[] AirFallbackNames = BuildAirFallbackNames();

        public static bool TryStep(SkateTrickState state, SkateTrickInput input, double dt,
                                   out SkateTrickResult result, out string error)
        { return TryStep(state, input, dt, SkateMotion.MaximumLandingFallSpeed, out result, out error); }

        public static bool TryStep(SkateTrickState state, SkateTrickInput input, double dt, double maximumLandingImpact,
                                   out SkateTrickResult result, out string error)
        {
            result = new SkateTrickResult(state, state.Spin, state.Flip, state.GrindSeconds, SkateTrickEvents.None, null, 0, 0);
            error = null;
            if (!Valid(state) || !Axis(input.Spin) || !Axis(input.Flip) || !Axis(input.Shove) || !Axis(input.BodySpin) ||
                !GrabKindValid(input.GrabKind) || !PopKindValid(input.PopKind) || input.Grab != (input.GrabKind != SkateGrabKind.None) ||
                !Finite(input.ImpactSpeed) || input.ImpactSpeed < 0 || input.ImpactSpeed > 100000 ||
                !Finite(input.RollingSpeed) || input.RollingSpeed < 0 || input.RollingSpeed > 100000 ||
                (input.Airborne && input.Grinding) || (input.Manual && input.NoseManual) ||
                !Finite(dt) || dt <= 0 || dt > SkateMotion.MaximumStep ||
                !Finite(maximumLandingImpact) || maximumLandingImpact <= 0 || maximumLandingImpact > 100000)
            { error = "Invalid skate trick step."; return false; }
            if (state.DarkslideCaught && !input.Airborne &&
                (!input.Grinding || input.GrindKind != SkateGrindKind.Darkslide))
                return TryBail(state, out result, out error);
            double spin = state.Spin, flip = state.Flip, shove = state.Shove;
            double bodySpin = state.BodySpin;
            bool late = state.Late;
            double grind = state.GrindSeconds, rolling = state.RollingSeconds, manual = state.ManualSeconds;
            long combo = state.ComboBasePoints, total = state.TotalPoints, points = 0, banked = 0;
            int count = state.ComboCount;
            bool ridingSwitch = state.Switch, legacyNaming = state.LegacyNaming;
            SkateGrabKind grabKind = state.GrabKind;
            SkatePopKind popKind = state.PopKind;
            SkateGrindKind grindKind = state.GrindKind; bool grindFrontside = state.GrindFrontside, popSwitch = state.PopSwitch;
            bool popTailLeads = state.PopTailLeads, stanceNaming = state.StanceNaming;
            var events = SkateTrickEvents.None;
            string name = null;
            if (input.Airborne)
            {
                if (!state.Airborne)
                {
                    spin = 0; flip = state.DarkslideCaught ? state.Flip : 0; shove = 0; grabKind = SkateGrabKind.None;
                    popKind = input.PopKind == SkatePopKind.None ? SkatePopKind.Ollie : input.PopKind;
                    legacyNaming = input.LegacyNaming; popSwitch = ridingSwitch;
                    popTailLeads = input.TailLeads; stanceNaming = input.StanceNaming;
                    bodySpin = 0; late = false;
                }
                else legacyNaming &= input.LegacyNaming;
                spin = BoundedAngle(spin + input.Spin * SkateMotion.RotationRate * dt);
                flip = BoundedAngle(flip + input.Flip * SkateMotion.FlipRate * dt);
                shove = BoundedAngle(shove + input.Shove * SkateMotion.ShoveRate * dt);
                bodySpin = Math.Max(-180, Math.Min(180, bodySpin + input.BodySpin * SkateMotion.RotationRate * dt));
                late |= input.Late && (input.Flip != 0 || input.Shove != 0);
                if (input.GrabKind != SkateGrabKind.None) grabKind = input.GrabKind;
            }
            double visualSpin = spin, visualFlip = flip, visualShove = shove;
            double visualBodySpin = bodySpin;
            double completedGrind = grind, completedManual = manual;
            if (state.Airborne && !input.Airborne)
            {
                int turns = (int)Math.Round(flip / 360, MidpointRounding.AwayFromZero);
                int halves = (int)Math.Round(spin / 180, MidpointRounding.AwayFromZero);
                int shoves = (int)Math.Round(shove / 180, MidpointRounding.AwayFromZero);
                int bodyHalves = (int)Math.Round(bodySpin / 180, MidpointRounding.AwayFromZero);
                bool darkCatch = input.Grinding && input.GrindKind == SkateGrindKind.Darkslide &&
                    Math.Abs(Math.Abs(flip) - 180) <= FlipTolerance + 1e-8;
                if ((!darkCatch && Math.Abs(flip - turns * 360) > FlipTolerance + 1e-8) ||
                    Math.Abs(spin - halves * 180) > SpinTolerance + 1e-8 ||
                    Math.Abs(shove - shoves * 180) > ShoveTolerance + 1e-8 ||
                    Math.Abs(bodySpin - bodyHalves * 180) > SpinTolerance + 1e-8 || input.ImpactSpeed > maximumLandingImpact)
                {
                    events |= SkateTrickEvents.Bailed;
                    name = "Bail";
                    combo = 0; count = 0; rolling = 0;
                    darkCatch = false;
                }
                else
                {
                    events |= SkateTrickEvents.Landed;
                    visualSpin = halves * 180; visualFlip = darkCatch ? Math.Sign(flip) * 180 : turns * 360; visualShove = shoves * 180;
                    visualBodySpin = bodyHalves * 180;
                    ridingSwitch ^= ((Math.Abs(halves) + Math.Abs(bodyHalves)) % 2) != 0;
                    if (darkCatch) name = GrindName(SkateGrindKind.Darkslide, input.GrindFrontside);
                    else
                    {
                        bool legacy = legacyNaming && shoves == 0 && popKind == SkatePopKind.Ollie &&
                            (grabKind == SkateGrabKind.None || grabKind == SkateGrabKind.Legacy) && !late && bodyHalves == 0;
                        name = LandingName(halves, turns, shoves, grabKind, popKind, legacy, popSwitch && !legacy,
                            late, bodyHalves != 0, popTailLeads, stanceNaming);
                        points = 50 + Math.Min(2, Math.Abs(turns)) * 100 +
                            Math.Min(legacy ? 2 : 4, Math.Abs(halves)) * 100 + Math.Min(2, Math.Abs(shoves)) * 100 +
                            (grabKind == SkateGrabKind.None ? 0 : 50) + (popSwitch && !legacy ? 25 : 0) +
                            (IsPlant(popKind) ? 50 : 0) + Math.Abs(bodyHalves) * 100;
                        combo = Add(combo, points); count = Math.Min(MaximumMultiplier, count + 1);
                    }
                }
                spin = 0; flip = darkCatch ? visualFlip : 0; shove = 0; grabKind = SkateGrabKind.None; popKind = SkatePopKind.None; popSwitch = false;
                bodySpin = 0; late = false;
                popTailLeads = false; stanceNaming = false;
            }
            if (state.Grinding && !input.Grinding)
            {
                int kind = GrindKindValid(state.GrindKind) ? (int)state.GrindKind : 0;
                points = Math.Max(IsStall(state.GrindKind) ? 100 : 1, (long)Math.Round(grind * GrindRates[kind]));
                name = GrindName(state.GrindKind, state.GrindFrontside);
                events |= SkateTrickEvents.GrindEnded;
                combo = Add(combo, points); count = Math.Min(MaximumMultiplier, count + 1);
                grind = 0; grindKind = SkateGrindKind.None; grindFrontside = false;
            }
            bool grinding = input.Grinding && (events & SkateTrickEvents.Bailed) == 0;
            if (grinding)
            {
                grind = Math.Min(86400, (state.Grinding ? grind : 0) + dt);
                completedGrind = grind;
                grindKind = GrindKindValid(input.GrindKind) ? input.GrindKind : SkateGrindKind.None;
                grindFrontside = input.GrindFrontside;
            }
            else if (!input.Grinding) { grindKind = SkateGrindKind.None; grindFrontside = false; }
            bool manualing = (input.Manual || input.NoseManual) && !input.Airborne && !input.Grinding &&
                input.RollingSpeed > MinimumManualSpeed && (events & SkateTrickEvents.Bailed) == 0;
            bool noseManualing = manualing && input.NoseManual;
            bool sameManual = state.Manualing && manualing && state.NoseManualing == noseManualing;
            if (state.Manualing && !sameManual)
            {
                completedManual = manual;
                if ((events & SkateTrickEvents.Bailed) == 0)
                {
                    points = DurationPoints(manual);
                    name = state.NoseManualing ? "Nose Manual" : "Manual";
                    events |= SkateTrickEvents.ManualEnded;
                    if (points > 0)
                    { combo = Add(combo, points); count = Math.Min(MaximumMultiplier, count + 1); }
                }
                manual = 0;
            }
            if (manualing)
            {
                manual = Math.Min(MaximumManualSeconds, (sameManual ? manual : 0) + dt);
                if ((events & SkateTrickEvents.ManualEnded) == 0) completedManual = manual;
                if (events == SkateTrickEvents.None)
                { name = noseManualing ? "Nose Manual" : "Manual"; points = DurationPoints(manual); }
            }
            if (input.Airborne || grinding || manualing || state.Airborne || state.Grinding || state.Manualing) rolling = 0;
            else if (count > 0)
            {
                rolling = Math.Min(ComboRollingTimeout, rolling + dt);
                if (rolling >= ComboRollingTimeout - 1e-9)
                {
                    banked = ComboValue(combo, count); total = Add(total, banked);
                    events |= SkateTrickEvents.ComboBanked;
                    name = "Combo"; points = banked;
                    combo = 0; count = 0; rolling = 0;
                }
            }
            var next = new SkateTrickState(spin, flip, shove, grind, rolling, input.Airborne, grinding,
                grabKind, ridingSwitch, combo, total, count, manual, manualing, noseManualing, popKind, legacyNaming,
                grindKind, grindFrontside, popSwitch, late, bodySpin, popTailLeads, stanceNaming);
            result = new SkateTrickResult(next, visualSpin, visualFlip, visualShove, completedGrind,
                completedManual, events, name, points, banked, visualBodySpin);
            return true;
        }

        // A trick done on the ground in one go (a revert, a powerslide): it joins the open combo as
        // a landed trick and keeps the combo rolling.
        public static bool TryAdd(SkateTrickState state, string name, long points, bool turnsRound,
                                  out SkateTrickResult result, out string error)
        {
            result = new SkateTrickResult(state, state.Spin, state.Flip, state.GrindSeconds, SkateTrickEvents.None, null, 0, 0);
            error = null;
            if (!Valid(state) || string.IsNullOrEmpty(name) || points < 0 || points > PointsLimit)
            { error = "Invalid skate ground trick."; return false; }
            if (state.Airborne || state.Grinding) { error = "A ground trick needs the board on the ground."; return false; }
            long combo = Add(state.ComboBasePoints, points);
            int count = Math.Min(MaximumMultiplier, state.ComboCount + 1);
            var next = new SkateTrickState(0, 0, 0, 0, 0, false, false, SkateGrabKind.None, state.Switch ^ turnsRound,
                combo, state.TotalPoints, count, state.ManualSeconds, state.Manualing, state.NoseManualing,
                SkatePopKind.None, state.LegacyNaming, SkateGrindKind.None, false, false);
            result = new SkateTrickResult(next, 0, 0, 0, 0, state.ManualSeconds, SkateTrickEvents.Landed, name, points, 0);
            return true;
        }

        public static int GrindRate(SkateGrindKind kind) { return GrindRates[GrindKindValid(kind) ? (int)kind : 0]; }

        public static string GrindName(SkateGrindKind kind, bool frontside)
        {
            int k = GrindKindValid(kind) ? (int)kind : 0;
            return DirectionalGrindNames[(frontside ? GrindNames.Length : 0) + k];
        }

        private static string[] BuildGrindNames()
        {
            var names = new string[2 * GrindNames.Length];
            for (int side = 0; side < 2; side++)
                for (int kind = 0; kind < GrindNames.Length; kind++)
                    names[side * GrindNames.Length + kind] = kind == 0 ? GrindNames[0] :
                        (side == 0 ? "Backside " : "Frontside ") + GrindNames[kind];
            return names;
        }

        public static bool IsStall(SkateGrindKind kind) { return (int)kind >= 11 && (int)kind <= 15; }

        public static bool TryBail(SkateTrickState state, out SkateTrickResult result, out string error)
        {
            result = new SkateTrickResult(state, state.Spin, state.Flip, state.GrindSeconds, SkateTrickEvents.None, null, 0, 0);
            error = null;
            if (!Valid(state)) { error = "Invalid skate trick state."; return false; }
            var next = new SkateTrickState(0, 0, 0, 0, false, false, false, state.Switch, 0, state.TotalPoints, 0);
            result = new SkateTrickResult(next, 0, 0, 0, state.GrindSeconds, state.ManualSeconds,
                SkateTrickEvents.Bailed, "Bail", 0, 0);
            return true;
        }

        private static string LandingName(int spin, int flip, int shove, SkateGrabKind grab,
                                          SkatePopKind pop, bool legacy, bool popSwitch, bool late, bool bodyVarial,
                                          bool popTailLeads, bool stanceNaming)
        {
            if (legacy && !IsPlant(pop))
            {
                if (Math.Abs(flip) > 2 || Math.Abs(spin) > 2)
                    return grab == SkateGrabKind.None ? "Multiple Rotation Trick" : "Multiple Rotation Trick Grab";
                int flipIndex = flip == 0 ? 0 : Math.Abs(flip) == 1 ? (flip > 0 ? 1 : 2) : (flip > 0 ? 3 : 4);
                int spinIndex = spin == 0 ? 0 : Math.Abs(spin) % 2 != 0 ? (spin > 0 ? 1 : 2) : (spin > 0 ? 3 : 4);
                return Names[spinIndex * 5 + flipIndex + (grab == SkateGrabKind.None ? 0 : 25)];
            }
            int style = (pop == SkatePopKind.None ? 0 : (int)pop - 1) + (popSwitch ? 6 : 0);
            if (stanceNaming && popSwitch)
            {
                if (popTailLeads)
                    style = pop == SkatePopKind.Ollie ? 7 : pop == SkatePopKind.Nollie ? 13 : (int)pop + 11;
                else if (pop == SkatePopKind.Nollie) style = 12;
            }
            int modifier = (late && (flip != 0 || shove != 0) ? 1 : 0) + (bodyVarial ? 2 : 0);
            if (Math.Abs(spin) > 4 || Math.Abs(flip) > 2 || Math.Abs(shove) > 2)
                return modifier == 0 ? FallbackNames[style * 14 + (int)grab] :
                    AirFallbackNames[(modifier - 1) * FallbackNames.Length + style * 14 + (int)grab];
            int index = NameIndex(spin, flip, shove, (int)grab, style);
            return modifier == 0 ? ComposedNames[index] : AirNames[(modifier - 1) * ComposedNames.Length + index];
        }

        private static string[] BuildAirNames()
        {
            var names = new string[3 * ComposedNames.Length];
            for (int modifier = 1; modifier <= 3; modifier++)
                for (int style = 0; style < NamePrefixes.Length; style++)
                    for (int spin = -4; spin <= 4; spin++)
                        for (int flip = -2; flip <= 2; flip++)
                            for (int shove = -2; shove <= 2; shove++)
                            {
                                string core = Composition(spin, flip, shove);
                                if ((modifier & 1) != 0 && (flip != 0 || shove != 0)) core = "Late " + core;
                                if ((modifier & 2) != 0) core = core == "Ollie" ? "Body Varial" : "Body Varial " + core;
                                core = PrefixName(core, style);
                                for (int grab = 0; grab < 14; grab++)
                                    names[(modifier - 1) * ComposedNames.Length + NameIndex(spin, flip, shove, grab, style)] = core + GrabSuffixes[grab];
                            }
            return names;
        }

        private static string[] BuildAirFallbackNames()
        {
            var names = new string[3 * FallbackNames.Length];
            for (int modifier = 1; modifier <= 3; modifier++)
                for (int style = 0; style < NamePrefixes.Length; style++)
                    for (int grab = 0; grab < 14; grab++)
                    {
                        string core = ((modifier & 1) != 0 ? "Late " : "") + "Multiple Rotation Trick";
                        if ((modifier & 2) != 0) core = "Body Varial " + core;
                        names[(modifier - 1) * FallbackNames.Length + style * 14 + grab] = PrefixName(core, style) + GrabSuffixes[grab];
                    }
            return names;
        }

        private static int NameIndex(int spin, int flip, int shove, int grab, int style)
        { return (((style * 9 + spin + 4) * 5 + flip + 2) * 5 + shove + 2) * 14 + grab; }

        private static string[] BuildNames()
        {
            var names = new string[NamePrefixes.Length * 9 * 5 * 5 * 14];
            for (int style = 0; style < NamePrefixes.Length; style++)
                for (int spin = -4; spin <= 4; spin++)
                    for (int flip = -2; flip <= 2; flip++)
                        for (int shove = -2; shove <= 2; shove++)
                        {
                            string core = PrefixName(Composition(spin, flip, shove), style);
                            for (int grab = 0; grab < 14; grab++)
                                names[NameIndex(spin, flip, shove, grab, style)] = core + GrabSuffixes[grab];
                        }
            return names;
        }

        private static string[] BuildFallbackNames()
        {
            var names = new string[NamePrefixes.Length * 14];
            for (int style = 0; style < NamePrefixes.Length; style++)
                for (int grab = 0; grab < 14; grab++)
                    names[style * 14 + grab] = PrefixName("Multiple Rotation Trick", style) + GrabSuffixes[grab];
            return names;
        }

        private static string PrefixName(string core, int style)
        {
            if (style == 0) return core;
            if (core == "Ollie" && style != 6 && style != 7) return NamePrefixes[style];
            return NamePrefixes[style] + " " + core;
        }

        private static string Composition(int spin, int flip, int shove)
        {
            if (Math.Abs(spin) == 1 && spin == shove && flip == 0)
                return spin > 0 ? "Frontside Bigspin" : "Backside Bigspin";
            if (Math.Abs(spin) == 1 && shove == 0 && flip == 1)
                return spin > 0 ? "Frontside Flip" : "Backside Flip";
            string board;
            if (flip == 1 && shove == -1) board = "Varial Kickflip";
            else if (flip == -1 && shove == 1) board = "Varial Heelflip";
            else if (flip == 1 && shove == -2) board = "360 Flip";
            else if (flip == -1 && shove == 2) board = "Laser Flip";
            else if (flip == 1 && shove == 1) board = "Hardflip";
            else if (flip == -1 && shove == -1) board = "Inward Heelflip";
            else if (flip == 0 && shove == -2) board = "Impossible";
            else
            {
                string flips = flip == 0 ? "" : flip == 1 ? "Kickflip" : flip == -1 ? "Heelflip" :
                    flip > 0 ? "Double Kickflip" : "Double Heelflip";
                string shoves = shove == 0 ? "" : shove == -1 ? "Pop Shove-it" : shove == 1 ? "Frontside Pop Shove-it" :
                    shove < 0 ? "360 Shove-it" : "Frontside 360 Shove-it";
                board = shoves.Length == 0 ? flips : flips.Length == 0 ? shoves : shoves + " " + flips;
            }
            if (spin == 0) return board.Length == 0 ? "Ollie" : board;
            string rotation = spin > 0 ? "Frontside " : "Backside ";
            switch (Math.Abs(spin))
            {
                case 1: rotation += "180"; break;
                case 2: rotation += "360"; break;
                case 3: rotation += "540"; break;
                default: rotation += "720"; break;
            }
            return board.Length == 0 ? rotation : rotation + " " + board;
        }

        internal static long ComboValue(long points, int count) { return Math.Min(PointsLimit, points * (long)count); }
        internal static long LiveComboValue(SkateTrickState state)
        {
            if (state.ComboBasePoints < 0 || state.ComboBasePoints > PointsLimit ||
                state.ComboCount < 0 || state.ComboCount > MaximumMultiplier) return 0;
            long points = state.ComboBasePoints;
            int count = state.ComboCount;
            if (state.Manualing && Finite(state.ManualSeconds) && state.ManualSeconds > 0 && state.ManualSeconds <= MaximumManualSeconds)
            { points = Add(points, DurationPoints(state.ManualSeconds)); count = Math.Min(MaximumMultiplier, count + 1); }
            return ComboValue(points, count);
        }
        private static long DurationPoints(double seconds)
        {
            if (!Finite(seconds) || seconds <= 0) return 0;
            return Math.Max(1, (long)Math.Round(Math.Min(MaximumManualSeconds, seconds) * ManualPointsPerSecond));
        }
        private static double BoundedAngle(double value) { return Math.Max(-3600000, Math.Min(3600000, value)); }
        private static long Add(long a, long b) { return Math.Min(PointsLimit, a + b); }
        private static bool Valid(SkateTrickState s)
        { return Finite(s.Spin) && Finite(s.Flip) && Finite(s.Shove) && Math.Abs(s.Spin) <= 3600000 &&
            Math.Abs(s.Flip) <= 3600000 && Math.Abs(s.Shove) <= 3600000 &&
            GrabKindValid(s.GrabKind) && PopKindValid(s.PopKind) && s.Grabbed == (s.GrabKind != SkateGrabKind.None) &&
            (!s.Airborne || s.PopKind != SkatePopKind.None) &&
            Finite(s.GrindSeconds) && s.GrindSeconds >= 0 && s.GrindSeconds <= 86400 && Finite(s.RollingSeconds) &&
            s.RollingSeconds >= 0 && s.RollingSeconds <= ComboRollingTimeout && s.ComboCount >= 0 && s.ComboCount <= MaximumMultiplier &&
            s.ComboBasePoints >= 0 && s.ComboBasePoints <= PointsLimit && s.TotalPoints >= 0 && s.TotalPoints <= PointsLimit &&
            !(s.Airborne && s.Grinding) && Finite(s.ManualSeconds) && s.ManualSeconds >= 0 && s.ManualSeconds <= MaximumManualSeconds &&
            (s.Manualing ? s.ManualSeconds > 0 && !s.Airborne && !s.Grinding : s.ManualSeconds == 0) &&
            (!s.NoseManualing || s.Manualing) && (s.ComboCount == 0) == (s.ComboBasePoints == 0) &&
            GrindKindValid(s.GrindKind) && (s.Grinding || s.GrindKind == SkateGrindKind.None) && (s.Airborne || !s.PopSwitch) &&
            (s.Airborne || (!s.PopTailLeads && !s.StanceNaming)) && (s.StanceNaming || !s.PopTailLeads) &&
            (s.Airborne || s.Flip == 0 || s.DarkslideCaught) &&
            Finite(s.BodySpin) && Math.Abs(s.BodySpin) <= 180 && (s.Airborne || (s.BodySpin == 0 && !s.Late)); }
        private static bool GrabKindValid(SkateGrabKind kind) { return (int)kind >= 0 && (int)kind <= 13; }
        private static bool GrindKindValid(SkateGrindKind kind) { return (int)kind >= 0 && (int)kind <= 18; }
        private static bool PopKindValid(SkatePopKind kind) { return (int)kind >= 0 && (int)kind <= 6; }
        public static bool IsPlant(SkatePopKind kind) { return (int)kind >= 3 && (int)kind <= 6; }
        private static bool Axis(double n) { return Finite(n) && Math.Abs(n) <= 1; }
        private static bool Finite(double n) { return !double.IsNaN(n) && !double.IsInfinity(n); }
    }
}
