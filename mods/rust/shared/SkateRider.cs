using System;

namespace Shortcut.RustMod
{
    public struct SkateRiderRig
    {
        public readonly double PelvisHeight, HipWidth, ThighLength, ShinLength, FootLength;
        public readonly double SpineLength, ShoulderWidth, UpperArmLength, ForearmLength, NeckToHeadLength;
        // ShoulderRise: how far the skeleton's arm joints sit above the chest point (a spine above the
        // hips) along the back, 0 when unknown. A hand that must land on something (a handplant) is
        // reached from the joint, so the arm's real length is not overrun.
        public readonly double ShoulderRise;
        public SkateRiderRig(double pelvisHeight, double hipWidth, double thighLength, double shinLength,
                             double footLength, double spineLength, double shoulderWidth, double upperArmLength,
                             double forearmLength, double neckToHeadLength)
            : this(pelvisHeight, hipWidth, thighLength, shinLength, footLength, spineLength, shoulderWidth,
                   upperArmLength, forearmLength, neckToHeadLength, 0) { }
        public SkateRiderRig(double pelvisHeight, double hipWidth, double thighLength, double shinLength,
                             double footLength, double spineLength, double shoulderWidth, double upperArmLength,
                             double forearmLength, double neckToHeadLength, double shoulderRise)
        { PelvisHeight = pelvisHeight; HipWidth = hipWidth; ThighLength = thighLength; ShinLength = shinLength;
          FootLength = footLength; SpineLength = spineLength; ShoulderWidth = shoulderWidth;
          UpperArmLength = upperArmLength; ForearmLength = forearmLength; NeckToHeadLength = neckToHeadLength;
          ShoulderRise = shoulderRise; }
    }

    // Presentation only: one rotation/release/catch history per flight, independent of forces.
    public enum SkateRiderContactPhase { Ground, Air, Rotating, Holding, Releasing, Catching, Caught, Landing }

    public struct SkateRiderContactState
    {
        public readonly bool Initialized, Airborne, Rotating;
        public readonly SkateRiderContactPhase Phase;
        public readonly double LastFlip, LastShove, FlipTravel, ShoveTravel, FlipLift, ShoveLift, Flick, Scoop, Catch;
        public readonly int FlipDirection, ShoveDirection;
        public readonly SkateGrabKind GrabKind;
        public readonly double GrabWeight, GrabSeconds, Landing;
        public SkateGrabKind VisibleGrab { get { return GrabWeight > 0 ? GrabKind : SkateGrabKind.None; } }
        public double BoardFold { get { return GrabWeight * (0.8 + 0.2 * Math.Min(1, GrabSeconds / 0.6)); } }
        internal SkateRiderContactState(bool air, bool rotating, SkateRiderContactPhase phase,
            double flip, double shove, double flipTravel, double shoveTravel, int flipDirection, int shoveDirection,
            double flipLift, double shoveLift, double flick, double scoop, double caught,
            SkateGrabKind grab, double grabWeight, double grabSeconds, double landing)
        {
            Initialized = true; Airborne = air; Rotating = rotating; Phase = phase;
            LastFlip = flip; LastShove = shove; FlipTravel = flipTravel; ShoveTravel = shoveTravel;
            FlipDirection = flipDirection; ShoveDirection = shoveDirection;
            FlipLift = flipLift; ShoveLift = shoveLift; Flick = flick; Scoop = scoop; Catch = caught;
            GrabKind = grab; GrabWeight = grabWeight; GrabSeconds = grabSeconds; Landing = landing;
        }
    }

    public struct SkateRiderInput
    {
        public readonly SkateVector BoardPosition, BoardForward, BoardUp;
        public readonly SkateStance Stance;
        public readonly bool Switch, Airborne, Pushing, Grab, Bailed;
        public readonly double Speed, LeanDegrees, Crouch, PushPhase, FlipDegrees, LookYawDegrees, DeckOffset;
        public readonly double GroundDrop, BailPhase, PushSeconds;
        public readonly SkateGrabKind GrabKind;
        // MongoPush: the front foot pushes and the back foot stays on the tail. FootPlant (0..1): the
        // front foot is off the deck beside the board, as in a no comply or a boneless. GrabSeconds:
        // how long the grab has been held; the body folds into it and tweaks as it is held.
        public readonly bool MongoPush;
        public readonly double FootPlant, GrabSeconds;
        // Handplant (0..1): the rider on one straight arm, the hand planted on the edge beside the
        // board. At 1 he is upside down over the hand, the legs piked and the board held against his
        // feet by the other hand. Values between are the way up (or down): first the side bend down
        // to the edge, then the body swinging up over the hand like a cartwheel, the feet leaving the
        // board's place on the ground as it comes round.
        public readonly double Handplant;
        // DeckPitch (radians): the deck pitched about its middle, the end that leads up; a manual, a
        // 5-0 or a tail stall. The body stays on BoardUp and the feet go with the deck.
        public readonly double DeckPitch;
        // NoseSlide (0..1): in an ollie the front foot slides up toward the nose as the board levels
        // out, and stays there to the landing.
        public readonly double NoseSlide;
        // ChestTurnDegrees: the chest turned ahead of a spin (positive frontside), or wound up
        // against the one that is coming; on top of the riding and pushing turn.
        public readonly double ChestTurnDegrees;
        // ShoveDegrees: how far the deck has turned under the rider in a shove-it (negative
        // backside), as FlipDegrees is for a flip. Landing (0..1): how much of a landing's impact
        // the body is still taking up; it falls to 0 as the rider settles.
        public readonly double ShoveDegrees, Landing;
        public readonly SkateRiderContactState Contact;
        public SkateRiderInput(SkateVector boardPosition, SkateVector boardForward, SkateVector boardUp,
                               SkateStance stance, bool ridingSwitch, double speed, double leanDegrees,
                               double crouch, bool airborne, bool pushing, double pushPhase,
                               double flipDegrees, bool grab, bool bailed, double lookYawDegrees)
            : this(boardPosition, boardForward, boardUp, stance, ridingSwitch, speed, leanDegrees,
                   crouch, airborne, pushing, pushPhase, flipDegrees, grab, bailed, lookYawDegrees, 0) { }
        public SkateRiderInput(SkateVector boardPosition, SkateVector boardForward, SkateVector boardUp,
                               SkateStance stance, bool ridingSwitch, double speed, double leanDegrees,
                               double crouch, bool airborne, bool pushing, double pushPhase,
                               double flipDegrees, bool grab, bool bailed, double lookYawDegrees, double deckOffset)
            : this(boardPosition, boardForward, boardUp, stance, ridingSwitch, speed, leanDegrees,
                   crouch, airborne, pushing, pushPhase, flipDegrees, grab, bailed, lookYawDegrees, deckOffset, 0, 0) { }
        public SkateRiderInput(SkateVector boardPosition, SkateVector boardForward, SkateVector boardUp,
                               SkateStance stance, bool ridingSwitch, double speed, double leanDegrees,
                               double crouch, bool airborne, bool pushing, double pushPhase,
                               double flipDegrees, bool grab, bool bailed, double lookYawDegrees, double deckOffset,
                               double groundDrop, double bailPhase)
            : this(boardPosition, boardForward, boardUp, stance, ridingSwitch, speed, leanDegrees,
                   crouch, airborne, pushing, pushPhase, flipDegrees, grab ? SkateGrabKind.Legacy : SkateGrabKind.None,
                   bailed, lookYawDegrees, deckOffset, groundDrop, bailPhase) { }
        public SkateRiderInput(SkateVector boardPosition, SkateVector boardForward, SkateVector boardUp,
                               SkateStance stance, bool ridingSwitch, double speed, double leanDegrees,
                               double crouch, bool airborne, bool pushing, double pushPhase,
                               double flipDegrees, SkateGrabKind grabKind, bool bailed, double lookYawDegrees, double deckOffset,
                               double groundDrop, double bailPhase)
            : this(boardPosition, boardForward, boardUp, stance, ridingSwitch, speed, leanDegrees,
                   crouch, airborne, pushing, pushPhase, flipDegrees, grabKind, bailed, lookYawDegrees, deckOffset,
                   groundDrop, bailPhase, false, 0, SkateRider.GrabIn, 0) { }
        public SkateRiderInput(SkateVector boardPosition, SkateVector boardForward, SkateVector boardUp,
                               SkateStance stance, bool ridingSwitch, double speed, double leanDegrees,
                               double crouch, bool airborne, bool pushing, double pushPhase,
                               double flipDegrees, SkateGrabKind grabKind, bool bailed, double lookYawDegrees, double deckOffset,
                               double groundDrop, double bailPhase, bool mongoPush, double footPlant, double grabSeconds)
            : this(boardPosition, boardForward, boardUp, stance, ridingSwitch, speed, leanDegrees,
                   crouch, airborne, pushing, pushPhase, flipDegrees, grabKind, bailed, lookYawDegrees, deckOffset,
                   groundDrop, bailPhase, mongoPush, footPlant, grabSeconds, 0) { }
        public SkateRiderInput(SkateVector boardPosition, SkateVector boardForward, SkateVector boardUp,
                               SkateStance stance, bool ridingSwitch, double speed, double leanDegrees,
                               double crouch, bool airborne, bool pushing, double pushPhase,
                               double flipDegrees, SkateGrabKind grabKind, bool bailed, double lookYawDegrees, double deckOffset,
                               double groundDrop, double bailPhase, bool mongoPush, double footPlant, double grabSeconds, double handplant)
            : this(boardPosition, boardForward, boardUp, stance, ridingSwitch, speed, leanDegrees,
                   crouch, airborne, pushing, pushPhase, flipDegrees, grabKind, bailed, lookYawDegrees, deckOffset,
                   groundDrop, bailPhase, mongoPush, footPlant, grabSeconds, handplant, 0) { }
        public SkateRiderInput(SkateVector boardPosition, SkateVector boardForward, SkateVector boardUp,
                               SkateStance stance, bool ridingSwitch, double speed, double leanDegrees,
                               double crouch, bool airborne, bool pushing, double pushPhase,
                               double flipDegrees, SkateGrabKind grabKind, bool bailed, double lookYawDegrees, double deckOffset,
                               double groundDrop, double bailPhase, bool mongoPush, double footPlant, double grabSeconds, double handplant,
                               double deckPitch)
            : this(boardPosition, boardForward, boardUp, stance, ridingSwitch, speed, leanDegrees,
                   crouch, airborne, pushing, pushPhase, flipDegrees, grabKind, bailed, lookYawDegrees, deckOffset,
                   groundDrop, bailPhase, mongoPush, footPlant, grabSeconds, handplant, deckPitch, 0) { }
        public SkateRiderInput(SkateVector boardPosition, SkateVector boardForward, SkateVector boardUp,
                               SkateStance stance, bool ridingSwitch, double speed, double leanDegrees,
                               double crouch, bool airborne, bool pushing, double pushPhase,
                               double flipDegrees, SkateGrabKind grabKind, bool bailed, double lookYawDegrees, double deckOffset,
                               double groundDrop, double bailPhase, bool mongoPush, double footPlant, double grabSeconds, double handplant,
                               double deckPitch, double noseSlide)
            : this(boardPosition, boardForward, boardUp, stance, ridingSwitch, speed, leanDegrees,
                   crouch, airborne, pushing, pushPhase, flipDegrees, grabKind, bailed, lookYawDegrees, deckOffset,
                   groundDrop, bailPhase, mongoPush, footPlant, grabSeconds, handplant, deckPitch, noseSlide, 0) { }
        public SkateRiderInput(SkateVector boardPosition, SkateVector boardForward, SkateVector boardUp,
                               SkateStance stance, bool ridingSwitch, double speed, double leanDegrees,
                               double crouch, bool airborne, bool pushing, double pushPhase,
                               double flipDegrees, SkateGrabKind grabKind, bool bailed, double lookYawDegrees, double deckOffset,
                               double groundDrop, double bailPhase, bool mongoPush, double footPlant, double grabSeconds, double handplant,
                               double deckPitch, double noseSlide, double chestTurnDegrees)
            : this(boardPosition, boardForward, boardUp, stance, ridingSwitch, speed, leanDegrees,
                   crouch, airborne, pushing, pushPhase, flipDegrees, grabKind, bailed, lookYawDegrees, deckOffset,
                   groundDrop, bailPhase, mongoPush, footPlant, grabSeconds, handplant, deckPitch, noseSlide, chestTurnDegrees, 0, 0) { }
        public SkateRiderInput(SkateVector boardPosition, SkateVector boardForward, SkateVector boardUp,
                               SkateStance stance, bool ridingSwitch, double speed, double leanDegrees,
                               double crouch, bool airborne, bool pushing, double pushPhase,
                               double flipDegrees, SkateGrabKind grabKind, bool bailed, double lookYawDegrees, double deckOffset,
                               double groundDrop, double bailPhase, bool mongoPush, double footPlant, double grabSeconds, double handplant,
                               double deckPitch, double noseSlide, double chestTurnDegrees, double shoveDegrees, double landing)
            : this(boardPosition, boardForward, boardUp, stance, ridingSwitch, speed, leanDegrees, crouch, airborne,
                   pushing, pushPhase, flipDegrees, grabKind, bailed, lookYawDegrees, deckOffset, groundDrop, bailPhase,
                   mongoPush, footPlant, grabSeconds, handplant, deckPitch, noseSlide, chestTurnDegrees, shoveDegrees,
                   landing, default(SkateRiderContactState)) { }
        public SkateRiderInput(SkateVector boardPosition, SkateVector boardForward, SkateVector boardUp,
                               SkateStance stance, bool ridingSwitch, double speed, double leanDegrees,
                               double crouch, bool airborne, bool pushing, double pushPhase,
                               double flipDegrees, SkateGrabKind grabKind, bool bailed, double lookYawDegrees, double deckOffset,
                               double groundDrop, double bailPhase, bool mongoPush, double footPlant, double grabSeconds, double handplant,
                               double deckPitch, double noseSlide, double chestTurnDegrees, double shoveDegrees, double landing,
                               SkateRiderContactState contact, double pushSeconds = 0.8)
        { PushSeconds = pushSeconds; Contact = contact; ShoveDegrees = shoveDegrees; Landing = landing; ChestTurnDegrees = chestTurnDegrees; NoseSlide = noseSlide; DeckPitch = deckPitch; MongoPush = mongoPush; FootPlant = footPlant; GrabSeconds = grabSeconds; Handplant = handplant;
          BoardPosition = boardPosition; BoardForward = boardForward; BoardUp = boardUp; Stance = stance;
          Switch = ridingSwitch; Speed = speed; LeanDegrees = leanDegrees; Crouch = crouch; Airborne = airborne;
          Pushing = pushing; PushPhase = pushPhase; FlipDegrees = flipDegrees; GrabKind = grabKind; Grab = grabKind != SkateGrabKind.None;
          Bailed = bailed; LookYawDegrees = lookYawDegrees; DeckOffset = deckOffset;
          GroundDrop = groundDrop; BailPhase = bailPhase; }
    }

    public struct SkateRiderPose
    {
        public readonly SkateVector Pelvis, LeftHip, RightHip, LeftKnee, RightKnee;
        public readonly SkateVector LeftAnkle, RightAnkle, LeftToe, RightToe, Chest, Neck, Head;
        public readonly SkateVector LeftShoulder, RightShoulder, LeftElbow, RightElbow, LeftHand, RightHand;
        public readonly SkateVector PelvisForward, PelvisUp, ChestForward, ChestUp, HeadForward;
        internal SkateRiderPose(SkateVector pelvis, SkateVector leftHip, SkateVector rightHip,
                                SkateVector leftKnee, SkateVector rightKnee, SkateVector leftAnkle,
                                SkateVector rightAnkle, SkateVector leftToe, SkateVector rightToe,
                                SkateVector chest, SkateVector neck, SkateVector head, SkateVector leftShoulder,
                                SkateVector rightShoulder, SkateVector leftElbow, SkateVector rightElbow,
                                SkateVector leftHand, SkateVector rightHand, SkateVector pelvisForward,
                                SkateVector pelvisUp, SkateVector chestForward, SkateVector chestUp,
                                SkateVector headForward)
        { Pelvis = pelvis; LeftHip = leftHip; RightHip = rightHip; LeftKnee = leftKnee; RightKnee = rightKnee;
          LeftAnkle = leftAnkle; RightAnkle = rightAnkle; LeftToe = leftToe; RightToe = rightToe;
          Chest = chest; Neck = neck; Head = head; LeftShoulder = leftShoulder; RightShoulder = rightShoulder;
          LeftElbow = leftElbow; RightElbow = rightElbow; LeftHand = leftHand; RightHand = rightHand;
          PelvisForward = pelvisForward; PelvisUp = pelvisUp; ChestForward = chestForward;
          ChestUp = chestUp; HeadForward = headForward; }
    }

    public static class SkateRider
    {
        public const double MinimumMeasurement = 0.01;
        public const double MaximumMeasurement = 5;
        private const double PoseSpeed = 14;
        private const double ReachMargin = 0.000001;
        // A leg is never asked to be quite straight: a skeleton whose hips sit a little off the
        // module's would then fall short of the ankle, and a foot on the ground would drag.
        private const double LegReachMargin = 0.015;

        // A new rotation burst (including a genuine late trick after a catch) releases once.
        // Extra turns requested during that burst retain clearance without another flick at 360/180.
        public static bool TryStepContact(SkateRiderContactState state, bool airborne, bool rotating, bool descending,
            bool footPlanted, double flip, double shove, SkateGrabKind grab, double grabSeconds, double landing,
            double dt, out SkateRiderContactState next, out string error)
        {
            next = state; error = null;
            if (!Valid(state) || !Finite(dt) || dt <= 0 || dt > 0.1 || !Finite(flip) || Math.Abs(flip) > 3600000 ||
                !Finite(shove) || Math.Abs(shove) > 3600000 || !Finite(grabSeconds) || grabSeconds < 0 || grabSeconds > 86400 ||
                !Fraction(landing) || grab < SkateGrabKind.None || grab > SkateGrabKind.Tailbone)
            { error = "Invalid rider contact phase request."; return false; }
            if (!airborne)
            {
                next = new SkateRiderContactState(false, false, landing > 0 ? SkateRiderContactPhase.Landing : SkateRiderContactPhase.Ground,
                    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, SkateGrabKind.None, 0, 0, landing);
                return true;
            }
            bool fresh = !state.Initialized || !state.Airborne;
            double df = flip - (fresh ? 0 : state.LastFlip), ds = shove - (fresh ? 0 : state.LastShove);
            bool changed = Math.Abs(df) > 0.000001 || Math.Abs(ds) > 0.000001;
            bool burst = fresh || (rotating && !state.Rotating) || (changed && state.Catch > 0);
            double ft = (burst ? 0 : state.FlipTravel) + Math.Abs(df), st = (burst ? 0 : state.ShoveTravel) + Math.Abs(ds);
            ft = Math.Min(3600000, ft); st = Math.Min(3600000, st);
            int fd = burst ? 0 : state.FlipDirection, sd = burst ? 0 : state.ShoveDirection;
            if (fd == 0 && Math.Abs(df) > 0.000001) fd = Math.Sign(df);
            if (sd == 0 && Math.Abs(ds) > 0.000001) sd = Math.Sign(ds);
            double weight = fresh ? 0 : state.GrabWeight, seconds = fresh ? 0 : state.GrabSeconds;
            var visible = fresh ? SkateGrabKind.None : state.GrabKind;
            // Rotating also includes body/yaw spins, during which gameplay permits a grab.
            // Only a moving flip/shove displaces its contact or interrupts a fading grip.
            bool boardTurning = rotating && changed;
            if (boardTurning) { weight = 0; visible = SkateGrabKind.None; seconds = 0; }
            else if (grab != SkateGrabKind.None)
            { visible = grab; seconds = grabSeconds; weight = Smooth(Math.Min(1, grabSeconds / GrabIn)); }
            else
            { weight = Math.Max(0, weight - dt / GrabIn); if (weight == 0) { visible = SkateGrabKind.None; seconds = 0; } }
            bool aligned = Math.Abs(flip - Math.Round(flip / 360) * 360) < 0.01 && Math.Abs(shove - Math.Round(shove / 180) * 180) < 0.01;
            bool catching = (!rotating || grab != SkateGrabKind.None) && !footPlanted && aligned && (grab != SkateGrabKind.None || descending || ft > 0 || st > 0);
            double caught = burst ? 0 : state.Catch;
            if (boardTurning) caught = 0; else if (catching) caught = Math.Min(1, caught + dt / ContactCatchSeconds);
            double fl = Smooth(Math.Min(1, ft / 90)) * (1 - caught), sl = Smooth(Math.Min(1, st / 45)) * (1 - caught);
            var phase = boardTurning ? SkateRiderContactPhase.Rotating : grab != SkateGrabKind.None ? SkateRiderContactPhase.Holding
                : weight > 0 ? SkateRiderContactPhase.Releasing : catching ? caught < 1 ? SkateRiderContactPhase.Catching : SkateRiderContactPhase.Caught
                : rotating ? SkateRiderContactPhase.Rotating : SkateRiderContactPhase.Air;
            next = new SkateRiderContactState(true, rotating, phase, flip, shove, ft, st, fd, sd, fl, sl,
                Kick(Math.Min(1, ft / 360)), Kick(Math.Min(1, st / 180)), caught, visible, weight, seconds, 0);
            return true;
        }
        public const double ContactCatchSeconds = 0.12;

        public static bool TryCreate(SkateRiderRig rig, SkateRiderInput input,
                                     out SkateRiderPose pose, out string error)
        {
            pose = default(SkateRiderPose);
            error = null;
            if (!Valid(rig) || !Valid(input))
            { error = "Invalid skate rider request."; return false; }
            if (input.Handplant > 0.001 && !input.Bailed)
            {
                SkateRiderPose planted;
                if (!Handplant(rig, input, input.Handplant, out planted, out error)) return false;
                if (input.Handplant >= 0.999) { pose = planted; return true; }
                SkateRiderPose riding;
                if (!Riding(rig, input, out riding, out error)) return false;
                pose = Blend(riding, planted, Smooth(Math.Min(1, input.Handplant / PlantBlend)));
                if (!Finite(pose)) { pose = default(SkateRiderPose); error = "Skate rider pose exceeds the allowed bounds."; return false; }
                return true;
            }
            if (input.Contact.Initialized && input.Contact.GrabWeight > 0 && input.Contact.GrabWeight < 1 && input.Airborne && !input.Bailed)
            {
                SkateRiderPose free, held;
                if (!Riding(rig, WithGrab(input, SkateGrabKind.None, 0), out free, out error) ||
                    !Riding(rig, WithGrab(input, input.Contact.GrabKind, Math.Max(GrabIn, input.GrabSeconds)), out held, out error)) return false;
                return BlendContact(rig, input, free, held, input.Contact.GrabWeight, out pose, out error);
            }
            return Riding(rig, input, out pose, out error);
        }

        private static SkateRiderInput WithGrab(SkateRiderInput i, SkateGrabKind grab, double seconds)
        {
            return new SkateRiderInput(i.BoardPosition, i.BoardForward, i.BoardUp, i.Stance, i.Switch, i.Speed,
                i.LeanDegrees, i.Crouch, i.Airborne, i.Pushing, i.PushPhase, i.FlipDegrees, grab, i.Bailed,
                i.LookYawDegrees, i.DeckOffset, i.GroundDrop, i.BailPhase, i.MongoPush, i.FootPlant, seconds,
                i.Handplant, i.DeckPitch, i.NoseSlide, i.ChestTurnDegrees, i.ShoveDegrees, i.Landing, i.Contact, i.PushSeconds);
        }

        // Mix the complete free/held contact targets, then solve lengths again. Mixing independently
        // recomputed grab angles can switch an IK solution abruptly; mixing joints alone shortens limbs.
        private static bool BlendContact(SkateRiderRig r, SkateRiderInput input, SkateRiderPose free,
                                         SkateRiderPose held, double weight, out SkateRiderPose pose, out string error)
        {
            pose = default(SkateRiderPose); error = null;
            var p = Blend(free, held, weight);
            var up = Unit(input.BoardUp);
            var pu = p.PelvisUp; var pf = Unit(Project(p.PelvisForward, pu)); var pr = Cross(pu, pf);
            var cu = p.ChestUp; var cf = Unit(Project(p.ChestForward, cu)); var cr = Cross(cu, cf);
            var lh = p.Pelvis - pr * (r.HipWidth * 0.5); var rh = p.Pelvis + pr * (r.HipWidth * 0.5);
            var chest = p.Pelvis + cu * r.SpineLength; var neck = chest + cu * (r.SpineLength * 0.12);
            var ls = chest - cr * (r.ShoulderWidth * 0.5); var rs = chest + cr * (r.ShoulderWidth * 0.5);
            var floor = input.BoardPosition + up * (Math.Min(0, input.DeckOffset) - input.FootPlant * (input.GroundDrop + PlantDrop)
                - Math.Abs(SkatePose.FootOffset * Math.Sin(input.DeckPitch)));
            SkateVector lk, rk, le, re, lhand, rhand;
            if (!Limb(lh, p.LeftAnkle, r.ThighLength, r.ShinLength, p.LeftKnee - lh, floor, up, out lk) ||
                !Limb(rh, p.RightAnkle, r.ThighLength, r.ShinLength, p.RightKnee - rh, floor, up, out rk) ||
                !Arm(ls, p.LeftHand, r, -1, cr, cf, floor, up, out le, out lhand) ||
                !Arm(rs, p.RightHand, r, 1, cr, cf, floor, up, out re, out rhand))
            { error = "Rider release contacts cannot be reached."; return false; }
            pose = new SkateRiderPose(p.Pelvis, lh, rh, lk, rk, p.LeftAnkle, p.RightAnkle,
                p.LeftAnkle + Unit(p.LeftToe - p.LeftAnkle) * r.FootLength,
                p.RightAnkle + Unit(p.RightToe - p.RightAnkle) * r.FootLength, chest, neck,
                neck + cu * r.NeckToHeadLength, ls, rs, le, re, lhand, rhand, pf, pu, cf, cu, p.HeadForward);
            if (!Finite(pose)) { error = "Rider release pose exceeds its bounds."; return false; }
            if (!AboveGround(pose, floor, up)) { error = "Rider release pose is below the ground plane."; return false; }
            return true;
        }

        private static bool Riding(SkateRiderRig rig, SkateRiderInput input, out SkateRiderPose pose, out string error)
        {
            pose = default(SkateRiderPose); error = null;
            var up = Unit(input.BoardUp);
            var forward = Unit(Project(input.BoardForward, up));
            var right = Cross(up, forward);
            int side = input.Stance == SkateStance.Regular ? 1 : -1;
            var facing = right * side;
            var travel = forward * (input.Switch ? -1 : 1);
            bool pushing = input.Pushing && !input.Airborne && !input.Bailed;
            bool grabbing = input.Grab && input.Airborne && !input.Bailed;
            double plantDown = input.Airborne && !input.Bailed ? input.FootPlant * (input.GroundDrop + PlantDrop) : 0;
            var floor = input.BoardPosition + up * (Math.Min(0, input.DeckOffset) -
                (input.Bailed || pushing ? input.GroundDrop : 0) - plantDown - Math.Abs(SkatePose.FootOffset * Math.Sin(input.DeckPitch)));
            bool backIsLeft = (input.Stance == SkateStance.Goofy) != input.Switch;
            bool namedGrab = grabbing && input.GrabKind != SkateGrabKind.Legacy;
            var grip = namedGrab ? Grip(input.GrabKind, backIsLeft, input.Stance == SkateStance.Regular) : default(GrabGrip);
            bool grabLeft = namedGrab ? grip.Left : backIsLeft;
            double armLength = rig.UpperArmLength + rig.ForearmLength;
            double speed = Math.Min(1, input.Speed / PoseSpeed);
            // The drive advances contact by a slow clock even when the board cannot roll.
            // Match that same floor at the noncontact endpoints as well as the distance branch.
            double cycleDistance = Math.Max(input.Speed * input.PushSeconds,
                (PushPlant + PushSweep) / (3 * (PushUp - PushDown)));
            double lean = input.LeanDegrees * Math.PI / 180;
            double turn = pushing ? PushTurn(input.PushPhase) : 0;
            double swing = 0;
            double bail = input.Bailed ? input.BailPhase : 0;

            // In a flip the feet come up off the deck and apart, knees tucked, so the board turns over
            // under them; they come back down onto it as it comes round.
            bool tricking = input.Airborne && !input.Bailed;
            double flipping = tricking ? input.Contact.Initialized ? input.Contact.FlipLift : Math.Abs(Math.Sin((input.FlipDegrees % 360) * Math.PI / 360)) : 0;
            // In a shove-it the deck turns flat under the feet: they come up off it, less than for a
            // flip, the board being level, and back down on it each half turn as its ends line up.
            double shoving = tricking ? input.Contact.Initialized ? input.Contact.ShoveLift : Math.Abs(Math.Sin((input.ShoveDegrees % 180) * Math.PI / 180)) : 0;
            double clear = Math.Max(flipping, ShoveClear * shoving);
            double lift = SkatePose.FootLift + (SkatePose.FlipFootLift + FlipTuck) * clear;
            var deck = input.BoardPosition + up * lift;
            var leftAnkle = deck + forward * (side * SkatePose.FootOffset);
            var rightAnkle = deck - forward * (side * SkatePose.FootOffset);
            double pitchDrop = 0;
            if (input.DeckPitch != 0)
            {
                // The deck pitched about its middle: the leading foot up, the trailing one down, and both
                // a little in toward the middle.
                double ps = Math.Sin(input.DeckPitch), pc = Math.Cos(input.DeckPitch);
                double leftAlong = SkateVector.Dot(leftAnkle - input.BoardPosition, travel);
                double rightAlong = SkateVector.Dot(rightAnkle - input.BoardPosition, travel);
                leftAnkle = leftAnkle + up * (leftAlong * ps) - travel * (leftAlong * (1 - pc));
                rightAnkle = rightAnkle + up * (rightAlong * ps) - travel * (rightAlong * (1 - pc));
                pitchDrop = Math.Abs(SkatePose.FootOffset * ps);
            }
            if (flipping > 0)
            {
                var spread = forward * (side * FlipSpread * flipping);
                leftAnkle = leftAnkle + spread; rightAnkle = rightAnkle - spread;
            }
            if (input.NoseSlide > 0 && input.Airborne && !input.Bailed)
            {
                // The ollie's front foot, slid up toward the nose along the deck.
                var slide = travel * (OllieSlide * input.NoseSlide);
                if (backIsLeft) rightAnkle = rightAnkle + slide; else leftAnkle = leftAnkle + slide;
            }
            if (tricking && (input.FlipDegrees != 0 || input.ShoveDegrees != 0))
            {
                // What sets the board turning, at the start of each turn: the leading foot (the one
                // that slides toward the leading end) flicks off the heel-side edge for a kickflip and
                // the toe-side edge for a heelflip; the trailing foot scoops the tail toward the heels
                // in a backside shove-it, toward the toes in a frontside one. Each foot comes back
                // over the deck as the turn goes on, to be over it for the catch.
                var flickAmount = input.Contact.Initialized ? input.Contact.Flick : Kick(Math.Abs(input.FlipDegrees) % 360 / 360);
                var scoopAmount = input.Contact.Initialized ? input.Contact.Scoop : Kick(Math.Abs(input.ShoveDegrees) % 180 / 180);
                var flipDirection = input.Contact.Initialized ? input.Contact.FlipDirection : Math.Sign(input.FlipDegrees);
                var shoveDirection = input.Contact.Initialized ? input.Contact.ShoveDirection : Math.Sign(input.ShoveDegrees);
                var flick = facing * (-flipDirection * FlickOut * flickAmount) + travel * (FlickAhead * flickAmount);
                var scoop = facing * (shoveDirection * ScoopOut * scoopAmount);
                if (backIsLeft) { rightAnkle = rightAnkle + flick; leftAnkle = leftAnkle + scoop; }
                else { leftAnkle = leftAnkle + flick; rightAnkle = rightAnkle + scoop; }
            }
            var leadDeck = backIsLeft ? rightAnkle : leftAnkle;
            var backDeck = backIsLeft ? leftAnkle : rightAnkle;
            double leadToe = (RidingLeadToe + (PushLeadToe - RidingLeadToe) * turn) * Math.PI / 180;
            double backToe = RidingBackToe * Math.PI / 180;
            double grounded = 0, heel = 0;
            var leadAnkle = leadDeck; var backAnkle = backDeck;
            bool mongo = pushing && input.MongoPush;
            if (mongo)
            {
                // The leading foot pushes (a mongo push, or a push rolling tail first, the back foot
                // at the leading end); the other foot stays on its end and its toes stay forward. The
                // foot comes down a little ahead of its own end (LeadPlant ahead of the middle) and
                // sweeps back past the standing foot, the same stroke as the other push, and rests
                // on its end between strokes. Planted a whole foot's length further ahead, as it
                // was, it was out of the legs' reach over the standing foot: the pelvis dived to
                // half its height and the frame was refused at the foot-down phase.
                // The body turned toward the way of travel has the leading foot's hip on the heel
                // side, nose first or tail first alike: that foot goes down there, on its own side.
                // On the toes' side it crossed in front of the standing leg, a goofy rider's right
                // foot mongo pushing on the left of the board with the legs and the foot twisted
                // (owner, 15 October).
                var pushSide = facing * -1;
                var rest = input.BoardPosition + travel * SkatePose.FootOffset;
                var stroke = input.BoardPosition + travel * (LeadPlant - PushPlant);
                leadAnkle = PushFoot(rest, stroke, travel, pushSide, up, input.PushPhase, input.GroundDrop, cycleDistance, rig.FootLength, out grounded, out heel);
                leadToe = RidingLeadToe * Math.PI / 180 + (Math.PI / 2 - RidingLeadToe * Math.PI / 180) * grounded;
                swing = StrokeSwing(leadAnkle, rest, stroke, travel);
            }
            else if (pushing)
            {
                backAnkle = PushFoot(input.BoardPosition - travel * SkatePose.FootOffset, input.BoardPosition, travel, facing, up, input.PushPhase, input.GroundDrop, cycleDistance, rig.FootLength, out grounded, out heel);
                backToe = backToe + (Math.PI / 2 - backToe) * grounded;
                swing = StrokeSwing(backAnkle, input.BoardPosition - travel * SkatePose.FootOffset, input.BoardPosition, travel);
            }
            if (input.FootPlant > 0 && input.Airborne && !input.Bailed)
            {
                // The front foot off the deck and down toward the ground beside the board, ahead of it.
                var planted = input.BoardPosition + facing * 0.2 + travel * 0.22 + up * (SkatePose.FootLift - plantDown);
                leadAnkle = leadAnkle + (planted - leadAnkle) * input.FootPlant;
            }
            // Leaving the support does not ask the torso to untwist. Spins and grabs have their
            // own explicit chest/hip targets; a plain ollie retains the rider's stance.
            double leanScale = 1, yawScale = 1;
            var pelvisAt = default(SkateVector); double pelvisDown = 0, chestPitch = 0, bailYaw = 0;
            SkateVector leftReach = default(SkateVector), rightReach = default(SkateVector); bool reaching = false;
            if (input.Bailed)
            {
                Bail(input.BoardPosition, travel, facing, up, rig, input.GroundDrop, SkatePose.FootLift - Math.Min(0, input.DeckOffset),
                     input.Crouch, bail, out leadAnkle, out backAnkle, out pelvisAt, out pelvisDown, out chestPitch, out bailYaw,
                     out leftReach, out rightReach);
                leadToe = backToe = bailYaw;
                reaching = true; leanScale = 0; yawScale = 0;
            }
            if (backIsLeft) { leftAnkle = backAnkle; rightAnkle = leadAnkle; } else { leftAnkle = leadAnkle; rightAnkle = backAnkle; }
            var leadToeDir = facing * Math.Cos(leadToe) + travel * Math.Sin(leadToe);
            var backToeDir = facing * Math.Cos(backToe) + travel * Math.Sin(backToe);
            var leftToeDir = backIsLeft ? backToeDir : leadToeDir;
            var rightToeDir = backIsLeft ? leadToeDir : backToeDir;
            // A foot still on the deck follows its pitch, toes as well as ankle. The pushing
            // foot meets the support plane instead, then returns to the deck through recovery.
            var pushingLeft = mongo ? !backIsLeft : backIsLeft;
            var leftPitch = input.Bailed ? 0 : input.DeckPitch * (pushing && pushingLeft ? 1 - grounded : 1);
            var rightPitch = input.Bailed ? 0 : input.DeckPitch * (pushing && !pushingLeft ? 1 - grounded : 1);
            leftToeDir = PitchFoot(leftToeDir, travel, up, leftPitch);
            rightToeDir = PitchFoot(rightToeDir, travel, up, rightPitch);
            var leftFootUp = up * Math.Cos(leftPitch) - travel * Math.Sin(leftPitch);
            var rightFootUp = up * Math.Cos(rightPitch) - travel * Math.Sin(rightPitch);
            if (SkateVector.Dot(leftAnkle - floor, up) < -1e-8 || SkateVector.Dot(rightAnkle - floor, up) < -1e-8)
            { error = "Skate rider feet are below the ground plane."; return false; }

            double balance = input.Bailed ? 0 : 0.04 + 0.03 * input.Crouch;
            double pelvisLean = (lean * side - balance) * leanScale;
            // The hips open as the pushing foot goes back and close as it comes forward, so the
            // stroke runs through the pelvis and not only through the leg.
            double pelvisYaw = input.Bailed ? bailYaw : (RidingPelvisYaw + (PushPelvisYaw - RidingPelvisYaw) * turn + PushHipTwist * swing * turn) * yawScale * Math.PI / 180;
            if (grabbing) pelvisYaw = 0;
            var pelvisFacing = facing * Math.Cos(pelvisYaw) + travel * Math.Sin(pelvisYaw);
            var pelvisUp = up * Math.Cos(pelvisLean) + facing * Math.Sin(pelvisLean);
            var pelvisForward = Unit(Project(pelvisFacing, pelvisUp));
            var pelvisRight = Cross(pelvisUp, pelvisForward);
            double crouch = input.Crouch;
            SkateVector centre;
            if (input.Bailed) centre = pelvisAt;
            else
            {
                // The weight goes over the standing foot: most of the way for the usual push, where
                // the pushing foot comes down beside it; less for the leading foot's push, whose
                // foot comes down further out and would be beyond the legs' reach from there.
                double onLead = 0.5 + (mongo ? -LeadPushWeight : PushWeight) * turn;
                centre = Project(leadDeck * onLead + backDeck * (1 - onLead) - input.BoardPosition, up) + input.BoardPosition;
                centre += right * (rig.PelvisHeight * Math.Sin(lean) * (grabbing ? 0.1 : 0.45)) + travel * (0.035 * speed);
                centre -= facing * (rig.SpineLength * balance * (grabbing ? 3 : 1));
                centre += facing * (PushSide * turn);
                // The hips ride a little toward the way of travel as the foot sweeps back.
                centre += travel * (PushHipDrive * swing * turn);
                if (namedGrab) centre += forward * grip.LegShift - facing * grip.BodyShift;
                // On a pitched deck the weight goes over the end that is down.
                centre -= travel * (PitchWeight * Math.Sin(input.DeckPitch));
            }
            var leftHipBase = centre - pelvisRight * (rig.HipWidth * 0.5);
            var rightHipBase = centre + pelvisRight * (rig.HipWidth * 0.5);
            double minimum = SkatePose.FootLift, maximum = MaximumMeasurement * 2;
            if (!HeightRange(leftHipBase, leftAnkle, up, rig.ThighLength, rig.ShinLength, ref minimum, ref maximum) ||
                !HeightRange(rightHipBase, rightAnkle, up, rig.ThighLength, rig.ShinLength, ref minimum, ref maximum))
            { error = "Skate rider legs cannot reach the foot positions."; return false; }
            // The body dips a little for the push, settled before the foot is down: a pelvis that
            // moved while the foot stands on the ground would drag it, since the secondary motion's
            // pelvis lags the pose and the leg then falls short.
            double requested = rig.PelvisHeight - Math.Min(SkatePose.CrouchDrop, rig.PelvisHeight * 0.65) * crouch - PushDip * turn;
            double folded = grabbing ? Math.Min(1, input.GrabSeconds / GrabIn) : 0;
            folded = folded * folded * (3 - 2 * folded);
            double riding = requested;
            if (grabbing) requested = minimum + GrabTuckMargin;
            if (namedGrab) requested = grip.Extension > 0 ? minimum + (maximum - minimum) * grip.Extension : minimum + grip.Tuck;
            if (grabbing) requested = riding + (requested - riding) * folded;
            if (input.Bailed) requested = rig.PelvisHeight - pelvisDown;
            double height = Math.Max(minimum, Math.Min(maximum, requested));
            var pelvis = centre + up * height;
            var leftHip = leftHipBase + up * height;
            var rightHip = rightHipBase + up * height;
            SkateVector leftKnee, rightKnee;
            if (!Limb(leftHip, leftAnkle, rig.ThighLength, rig.ShinLength,
                      leftToeDir - pelvisRight * 0.15 + (input.Bailed ? travel * 2 : default(SkateVector)), floor, up, out leftKnee) ||
                !Limb(rightHip, rightAnkle, rig.ThighLength, rig.ShinLength,
                      rightToeDir + pelvisRight * 0.15 + (input.Bailed ? travel * 2 : default(SkateVector)), floor, up, out rightKnee))
            { error = "Skate rider legs cannot reach above the ground plane."; return false; }

            double chestYaw = input.Bailed ? bailYaw : (RidingChestYaw + (PushChestYaw - RidingChestYaw) * turn) * yawScale * Math.PI / 180;
            if (grabbing) chestYaw = 0;
            if (!input.Bailed) chestYaw += input.ChestTurnDegrees * Math.PI / 180;
            var chestFacing = facing * Math.Cos(chestYaw) + travel * Math.Sin(chestYaw);
            if (namedGrab && grip.ChestTurn != 0)
                chestFacing = facing * Math.Cos(grip.ChestTurn) + (grip.End ? forward : travel) * Math.Sin(grip.ChestTurn);
            // A heel-side grab: the chest turns round toward the heels and folds forward over them,
            // as a rider reaches behind; it does not lie back.
            bool heelReach = grabbing && namedGrab && grip.Across < 0 && !grip.Both && grip.ChestTurn == 0;
            if (heelReach) chestFacing = facing * Math.Cos(grip.HeelTurn) + travel * (Math.Sin(grip.HeelTurn) * HeelTurnSign);
            double carve = lean * 0.65 * side * leanScale;
            double landing = input.Bailed || input.Airborne ? 0 : input.Landing;
            double pitch = input.Bailed ? chestPitch : 0.1 + 0.12 * crouch + PushLean * turn + LandLean * landing;
            var carved = up * Math.Cos(carve) + facing * Math.Sin(carve);
            var ahead = Unit(Project(chestFacing, carved));
            var grab = input.BoardPosition + up * input.DeckOffset + facing * 0.1 - travel * 0.12;
            var secondGrab = default(SkateVector);
            if (namedGrab)
            {
                grab = input.BoardPosition + up * input.DeckOffset + facing * grip.Across +
                    (grip.End ? forward : travel) * grip.Along;
                if (grip.Both) secondGrab = input.BoardPosition + up * input.DeckOffset + facing * 0.1 - travel * 0.06;
            }
            double roll = 0;
            if (grabbing)
            {
                // The roll toward the grabbing hand comes in with the fold and grows a little as it is held: the tweak.
                roll = namedGrab && grip.Both ? 0 : GrabRoll * (grabLeft ? -1 : 1) * (0.4 + 0.6 * folded) * (1 + 0.25 * Math.Min(1, Math.Max(0, input.GrabSeconds - GrabIn) / 0.6));
                pitch = namedGrab && grip.Across < 0 && !grip.Both && !heelReach
                    ? -GrabPitch(pelvis, carved, ahead * -1, grab, rig, -roll, !grabLeft, pitch)
                    : GrabPitch(pelvis, carved, ahead, grab, rig, roll, grabLeft, pitch);
                // A heel-side grab is reached by bending down beside the board, not by lying back:
                // the backward lean is held to GrabBackLimit and the side bend takes the rest.
                if (pitch < -GrabBackLimit) { roll += (grabLeft ? -1 : 1) * Math.Min(0.3, -pitch - GrabBackLimit); pitch = -GrabBackLimit; }
                if (namedGrab && grip.Both) pitch = Math.Max(pitch,
                    GrabPitch(pelvis, carved, ahead, secondGrab, rig, roll, !grabLeft, 0.1 + 0.12 * crouch));
            }
            var chestUp = carved * Math.Cos(pitch) + ahead * Math.Sin(pitch);
            var chestForward = ahead * Math.Cos(pitch) - carved * Math.Sin(pitch);
            var chestRight = Cross(chestUp, chestForward);
            if (roll != 0)
            {
                var bentUp = chestUp * Math.Cos(roll) + chestRight * Math.Sin(roll);
                chestRight = chestRight * Math.Cos(roll) - chestUp * Math.Sin(roll);
                chestUp = bentUp;
            }
            var chest = pelvis + chestUp * rig.SpineLength;
            var neck = chest + chestUp * (rig.SpineLength * 0.12);
            var head = neck + chestUp * rig.NeckToHeadLength;
            var leftShoulder = chest - chestRight * (rig.ShoulderWidth * 0.5);
            var rightShoulder = chest + chestRight * (rig.ShoulderWidth * 0.5);

            double carving = Math.Min(1, Math.Abs(input.LeanDegrees) / SkatePose.MaximumLean);
            double air = input.Airborne ? 1 : 0;
            // At rest the arms hang loosely a little out and ahead, the elbows bent, not straight
            // down at the sides; they come up and out with a crouch, a carve and in the air.
            // A board turning under the feet takes the arms a little further out, for the balance.
            double armOut = armLength * (0.26 + 0.10 * crouch + 0.15 * carving + 0.30 * air + 0.12 * clear + LandArms * landing);
            double armDown = armLength * (0.84 - 0.22 * crouch - 0.12 * carving - 0.34 * air);
            double armAhead = armLength * (0.14 + 0.12 * crouch + 0.05 * speed + LandArms * landing);
            var along = travel * (armLength * (backIsLeft ? -1 : 1));
            // The arm opposite the pushing foot goes forward as that foot goes back; a mongo push
            // is the other foot, so the other arm.
            var counterSwing = chestForward * (armLength * PushSwing * swing * ((mongo ? !backIsLeft : backIsLeft) ? -1 : 1));
            var leftTarget = leftShoulder - chestRight * armOut + chestForward * armAhead - up * armDown
                + along * (backIsLeft ? 0.05 : 0.10) + counterSwing;
            var rightTarget = rightShoulder + chestRight * armOut + chestForward * armAhead - up * armDown
                - along * (backIsLeft ? 0.10 : 0.05) - counterSwing;
            if (grabbing)
            {
                // The free arm goes out to the side for balance, level with the shoulder or a little
                // above, not overhead; it lifts a little as the grab is held, and sways.
                double held = Math.Min(1, Math.Max(0, input.GrabSeconds - GrabIn) / (GrabIn * 2));
                double sway = Math.Sin(input.GrabSeconds * 7) * 0.06 * held;
                var outward = Unit(Project(chestRight, up));
                if (outward.LengthSquared < 0.5) outward = chestRight;
                double armLift = (namedGrab ? grip.FreeUp * 0.4 : 0.06) + 0.12 * held + sway;
                var freeRight = rightShoulder + outward * (armLength * 0.85) + up * (armLength * armLift) - travel * (armLength * 0.1);
                var freeLeft = leftShoulder - outward * (armLength * 0.85) + up * (armLength * armLift) - travel * (armLength * 0.1);
                if (namedGrab)
                {
                    if (grabLeft) { leftTarget = grab; rightTarget = grip.Both ? secondGrab : freeRight; }
                    else { rightTarget = grab; leftTarget = grip.Both ? secondGrab : freeLeft; }
                }
                else if (backIsLeft) { leftTarget = grab; rightTarget = freeRight; }
                else { rightTarget = grab; leftTarget = freeLeft; }
            }
            if (reaching) { leftTarget = leftReach; rightTarget = rightReach; }
            SkateVector leftHand, rightHand, leftElbow, rightElbow;
            if (!Arm(leftShoulder, leftTarget, rig, -1, chestRight, chestForward, floor, up,
                     out leftElbow, out leftHand) ||
                !Arm(rightShoulder, rightTarget, rig, 1, chestRight, chestForward, floor, up,
                     out rightElbow, out rightHand))
            { error = "Skate rider arms cannot reach above the ground plane."; return false; }
            var look = Forward(input.LookYawDegrees % 360);
            var headForward = Unit(Project(look, up));
            if (headForward.LengthSquared < 0.5) headForward = forward;
            // The heel lifts on the foot that pushes: the leading one in a mongo push, else the back one.
            var heelLeft = mongo ? !backIsLeft : backIsLeft;
            var leftToe = Toe(leftAnkle, leftToeDir, leftFootUp, rig.FootLength, heelLeft ? heel : 0);
            var rightToe = Toe(rightAnkle, rightToeDir, rightFootUp, rig.FootLength, heelLeft ? 0 : heel);
            pose = new SkateRiderPose(pelvis, leftHip, rightHip, leftKnee, rightKnee, leftAnkle, rightAnkle,
                leftToe, rightToe,
                chest, neck, head, leftShoulder, rightShoulder, leftElbow, rightElbow, leftHand, rightHand,
                pelvisForward, pelvisUp, chestForward, chestUp, headForward);
            if (!Finite(pose))
            { pose = default(SkateRiderPose); error = "Skate rider pose exceeds the allowed bounds."; return false; }
            if (!AboveGround(pose, floor, up))
            { pose = default(SkateRiderPose); error = "Skate rider pose is below the ground plane."; return false; }
            return true;
        }

        private const double RidingPelvisYaw = 12, RidingChestYaw = 28, RidingLeadToe = 25, RidingBackToe = 5;
        private const double PushPelvisYaw = 55, PushChestYaw = 70, PushLeadToe = 60;
        private const double PushWeight = 0.28, PushSide = 0.03, PushDip = 0.06, PushLean = 0.24, PushSwing = 0.28;
        // The pushing foot comes down PushPlant ahead of the board's middle and leaves it PushSweep
        // behind: the stroke, over which the foot is on the ground (push phase PushDown..PushUp).
        public const double PushPlant = 0.18, PushSweep = 0.44, PushDown = 0.25, PushUp = 0.6;
        // The leading foot's push comes down LeadPlant ahead of the board's middle, a little ahead
        // of its own end, and the weight shifts LeadPushWeight of the way to the standing foot.
        public const double LeadPlant = 0.25, LeadPushWeight = 0.12;
        private const double PushHeel = 0.07, PushHipTwist = 8, PushHipDrive = 0.03;
        private const double GrabRoll = 0.4, GrabFoldLimit = 1.0, GrabTuckMargin = 0.05, PlantDrop = 0.3, GrabBackLimit = 0.55, HeelTurn = 1.2;
        private const double HeelTurnSign = 1;
        // A board turning under the rider: how much of a flip's clearance a shove-it takes, and how
        // far the flicking and scooping feet go out past the edge and ahead (metres, at the peak).
        private const double ShoveClear = 0.6, FlickOut = 0.13, FlickAhead = 0.05, ScoopOut = 0.1;
        // Landing: the chest folds forward over the knees and the arms come forward and out as the
        // impact is taken up.
        private const double LandLean = 0.22, LandArms = 0.16;
        private const double FlipTuck = 0.14, FlipSpread = 0.05, PitchWeight = 0.18, OllieSlide = 0.13;
        // The handplant: the side bend at the start (radians from upright), where the feet are folded
        // in by, how long the mix from the riding pose runs (of Handplant), the hand beside the board,
        // the pike (of the spine toward the chest, of the legs toward the chest's front) and the deck
        // under the back foot where the other hand holds it.
        private const double PlantLean = 0.75, PlantFoldBy = 1.6, PlantBlend = 0.3, PlantHandOut = 0.22, PlantHandAhead = 0.1,
            PlantPikeUp = 0.12, PlantPikeOut = 0.4, PlantDeck = 0.05, PlantOver = 0.2;
        public const double MaximumDeckPitch = 0.8, MaximumChestTurn = 60;

        // How a grind or a stall holds the deck, degrees: the end that leads up (a 5-0 and a tail
        // stall sit on the back truck or the tail, the nose up), the toe edge up (a smith dips the
        // front truck down past the edge on the heel side, a feeble over it on the toe side, a
        // crooked rides the nose with the deck on its edge). And how deep the rider crouches in it.
        public static void GrindDeck(SkateGrindKind kind, out double pitchDegrees, out double rollDegrees, out double crouch)
        {
            pitchDegrees = 0; rollDegrees = 0; crouch = 0.38;
            switch (kind)
            {
                case SkateGrindKind.FiveO: pitchDegrees = 14; crouch = 0.35; break;
                case SkateGrindKind.Nosegrind: pitchDegrees = -14; crouch = 0.42; break;
                case SkateGrindKind.Crooked: pitchDegrees = -12; rollDegrees = 14; crouch = 0.5; break;
                case SkateGrindKind.Smith: pitchDegrees = -10; rollDegrees = -18; crouch = 0.52; break;
                case SkateGrindKind.Feeble: pitchDegrees = -8; rollDegrees = 16; crouch = 0.45; break;
                case SkateGrindKind.Tailslide: pitchDegrees = 8; crouch = 0.4; break;
                case SkateGrindKind.Noseslide: pitchDegrees = -8; crouch = 0.42; break;
                // A bluntslide stands on the tail with the deck pitched up steeply, the rider low
                // over the back foot; a nosebluntslide the other way round. A darkslide rides the
                // deck upside down, griptape on the edge: the roll turns the board over, the feet
                // stand on what is now the top.
                case SkateGrindKind.Bluntslide: pitchDegrees = 40; crouch = 0.6; break;
                case SkateGrindKind.Nosebluntslide: pitchDegrees = -40; crouch = 0.6; break;
                case SkateGrindKind.Darkslide: rollDegrees = 180; crouch = 0.5; break;
                case SkateGrindKind.Boardslide: case SkateGrindKind.Lipslide: crouch = 0.45; break;
                case SkateGrindKind.TailStall: pitchDegrees = 27; crouch = 0.32; break;
                case SkateGrindKind.NoseStall: pitchDegrees = -27; crouch = 0.32; break;
                case SkateGrindKind.BoardStall: crouch = 0.55; break;
                case SkateGrindKind.FiftyFiftyStall: crouch = 0.45; break;
                case SkateGrindKind.Handplant: crouch = 0.38; break;
            }
        }
        // A grab folds in over GrabIn seconds; held longer it tweaks. The older inputs pose a grab just folded in.
        public const double GrabIn = 0.18;

        // Keep the same authored gripping hand while its target blends toward or away from the deck.
        public static void GrabHands(SkateStance stance, bool ridingSwitch, SkateGrabKind kind, out bool left, out bool right)
        {
            left = right = false;
            if (kind <= SkateGrabKind.None || kind > SkateGrabKind.Tailbone) return;
            bool backIsLeft = (stance == SkateStance.Goofy) != ridingSwitch;
            var grip = Grip(kind, backIsLeft, stance == SkateStance.Regular);
            bool hand = kind == SkateGrabKind.Legacy ? backIsLeft : grip.Left;
            left = grip.Both || hand; right = grip.Both || !hand;
        }

        private struct GrabGrip
        {
            public readonly bool Left, Both, End;
            public readonly double Across, Along, FreeUp, Tuck, Extension, LegShift, BodyShift, ChestTurn, HeelTurn;
            public GrabGrip(bool left, double across, double along, double freeUp = 0.25,
                            bool end = false, bool both = false, double tuck = GrabTuckMargin,
                            double extension = 0, double legShift = 0, double bodyShift = 0, double chestTurn = 0,
                            double heelTurn = SkateRider.HeelTurn)
            { Left = left; Across = across; Along = along; FreeUp = freeUp; End = end; Both = both;
              Tuck = tuck; Extension = extension; LegShift = legShift; BodyShift = bodyShift; ChestTurn = chestTurn; HeelTurn = heelTurn; }
        }

        private static GrabGrip Grip(SkateGrabKind kind, bool backIsLeft, bool noseIsLeft)
        {
            switch (kind)
            {
                case SkateGrabKind.Backside: return new GrabGrip(!backIsLeft, -0.1, 0.06, bodyShift: -0.15);
                case SkateGrabKind.Frontside: return new GrabGrip(backIsLeft, 0.1, -0.08);
                case SkateGrabKind.Double: return new GrabGrip(!backIsLeft, -0.1, 0.06, both: true, tuck: 0.035, bodyShift: 0.1);
                case SkateGrabKind.Tail: return new GrabGrip(!noseIsLeft, 0, -0.36, 0.3, end: true);
                case SkateGrabKind.Nose: return new GrabGrip(noseIsLeft, 0, 0.36, 0.3, end: true);
                case SkateGrabKind.Stalefish: return new GrabGrip(backIsLeft, -0.1, -0.15, 0.4);
                case SkateGrabKind.Crail: return new GrabGrip(backIsLeft, 0.1, 0.3, 0.2, end: true,
                    chestTurn: backIsLeft != noseIsLeft ? 1.2 : 0);
                case SkateGrabKind.Seatbelt: return new GrabGrip(!backIsLeft, 0.1, -0.3, 0.35, end: true,
                    chestTurn: backIsLeft != noseIsLeft ? -1.2 : 0);
                // A melon: the chest stays toward the toes and folds over the knees, the lead hand down between them.
                case SkateGrabKind.Melon: return new GrabGrip(!backIsLeft, -0.1, -0.06, 0.35, tuck: 0.035, bodyShift: -0.15, heelTurn: 0.3);
                case SkateGrabKind.Method: return new GrabGrip(!backIsLeft, -0.1, 0.04, 0.45, tuck: 0.015, bodyShift: -0.18);
                // A nosebone: the back hand on the tail, the front leg pushed out straight toward the
                // nose (the hips go back over the tail); a tailbone the other way round.
                // The hips sit high and well back from the boned foot, so that leg straightens out
                // along the board while the other knee stays bent.
                case SkateGrabKind.Nosebone: return new GrabGrip(!noseIsLeft, 0, -0.36, 0.3, end: true,
                    extension: 0.9, legShift: -0.2, chestTurn: -0.35);
                case SkateGrabKind.Tailbone: return new GrabGrip(noseIsLeft, 0, 0.36, 0.3, end: true,
                    extension: 0.9, legShift: 0.2, chestTurn: 0.35);
                default: return default(GrabGrip);
            }
        }

        // The handplant, by how far it has gone (0..1): the lead hand planted on the edge beside the
        // board, the arm straight; the body turns over the hand in its own frontal plane, as in a
        // cartwheel: first a side bend down to the hand (the feet still on the board on the ground),
        // then up and over until it stands on the hand upside down, the legs piked so the feet, with
        // the board against them, are at the chest's front where the other hand holds the board.
        private static bool Handplant(SkateRiderRig rig, SkateRiderInput input, double h, out SkateRiderPose pose, out string error)
        {
            pose = default(SkateRiderPose); error = null;
            var up = Unit(input.BoardUp);
            var forward = Unit(Project(input.BoardForward, up));
            var right = Cross(up, forward);
            int side = input.Stance == SkateStance.Regular ? 1 : -1;
            var facing = right * side;
            var travel = forward * (input.Switch ? -1 : 1);
            bool backIsLeft = (input.Stance == SkateStance.Goofy) != input.Switch;
            bool leadIsLeft = !backIsLeft;
            // Across the body at the top of the handplant, the lateral axis is travel (times this sign).
            double s = side * (input.Switch ? -1 : 1);
            double armLength = rig.UpperArmLength + rig.ForearmLength;
            double legLength = rig.ThighLength + rig.ShinLength;
            // The edge's top is the ground under the board: the deck's top is DeckOffset above the
            // ankles' plane that BoardPosition is, the wheels GroundDrop below that.
            var edge = input.BoardPosition + up * (Math.Min(0, input.DeckOffset) - input.GroundDrop);
            var ground = edge - up * 0.02;
            // The body's angle over the hand: the lean of the side bend at the start, upside down at the end.
            // At the top the body is not quite vertical: it leans back over the edge by PlantOver, the
            // hips over the ramp, the knees the highest point and the feet with the board in front.
            double theta = PlantLean + (Math.PI - PlantOver - PlantLean) * Smooth(Math.Max(0, Math.Min(1, (h - 0.08) / 0.92)));
            var chestUp = up * Math.Cos(theta) + travel * Math.Sin(theta);
            var chestForward = facing;
            var chestRight = Cross(chestUp, chestForward);
            var hand = edge + facing * PlantHandOut + travel * PlantHandAhead;
            // The planted arm's joint straight above the hand; the chest's shoulder point a rise below it.
            var plantedJoint = hand + up * (armLength * 0.97) - travel * 0.06;
            var plantedShoulder = plantedJoint - chestUp * rig.ShoulderRise;
            var shoulderCentre = plantedShoulder - chestRight * (rig.ShoulderWidth * 0.5 * (leadIsLeft ? -1 : 1));
            var leftShoulder = shoulderCentre - chestRight * (rig.ShoulderWidth * 0.5);
            var rightShoulder = shoulderCentre + chestRight * (rig.ShoulderWidth * 0.5);
            var chest = shoulderCentre;
            var neck = chest + chestUp * (rig.SpineLength * 0.12);
            var head = neck + chestUp * rig.NeckToHeadLength;
            var pelvis = chest - chestUp * rig.SpineLength;
            var pelvisUp = chestUp; var pelvisForward = chestForward; var pelvisRight = chestRight;
            var leftHip = pelvis - pelvisRight * (rig.HipWidth * 0.5);
            var rightHip = pelvis + pelvisRight * (rig.HipWidth * 0.5);
            // The feet: on the board on the ground until the body comes past the lean, then folded in
            // to the pike, a stance apart along the body's lateral axis. From the moment they leave
            // the ground they are carried in the body's own frame, so that they swing round over the
            // hand with the body, the way a cartwheel's legs do, and fold in on the way: a straight
            // line from the ground to the pike would leave a foot hanging below the body half way.
            double f = Smooth(Math.Max(0, Math.Min(1, (theta - PlantLean) / (PlantFoldBy - PlantLean))));
            var deckGround = input.BoardPosition + up * SkatePose.FootLift;
            // On the ground the lead foot is ahead along travel; folded in, the feet are apart across
            // the body, each on its own side, so the body turning over carries them round with it.
            double leadSign = leadIsLeft ? 1 : -1;
            var leftGround = deckGround + travel * (SkatePose.FootOffset * leadSign);
            var rightGround = deckGround - travel * (SkatePose.FootOffset * leadSign);
            // The body's frame at the moment the feet leave the ground, and the feet's places in it.
            var up0 = up * Math.Cos(PlantLean) + travel * Math.Sin(PlantLean);
            var right0 = Cross(up0, chestForward);
            var pelvis0 = plantedJoint - up0 * rig.ShoulderRise - right0 * (rig.ShoulderWidth * 0.5 * (leadIsLeft ? -1 : 1)) - up0 * rig.SpineLength;
            var leftLocal = Local(leftGround - pelvis0, up0, chestForward, right0);
            var rightLocal = Local(rightGround - pelvis0, up0, chestForward, right0);
            var pikeLocal = new SkateVector(rig.SpineLength * PlantPikeUp, legLength * PlantPikeOut, 0);
            var leftFold = Blend(leftLocal, pikeLocal - new SkateVector(0, 0, SkatePose.FootOffset), f);
            var rightFold = Blend(rightLocal, pikeLocal + new SkateVector(0, 0, SkatePose.FootOffset), f);
            var leftAnkle = pelvis + chestUp * leftFold.X + chestForward * leftFold.Y + chestRight * leftFold.Z;
            var rightAnkle = pelvis + chestUp * rightFold.X + chestForward * rightFold.Y + chestRight * rightFold.Z;
            // Never below the ground: a foot still on the ground before the body passes the lean stays exactly there.
            if (f <= 0) { leftAnkle = leftGround; rightAnkle = rightGround; }
            var leadAnkle = leadIsLeft ? leftAnkle : rightAnkle;
            var backAnkle = leadIsLeft ? rightAnkle : leftAnkle;
            var pole = facing - chestUp * f;
            SkateVector leftKnee, rightKnee;
            if (!Limb(leftHip, leftAnkle, rig.ThighLength, rig.ShinLength, pole, ground, up, out leftKnee) ||
                !Limb(rightHip, rightAnkle, rig.ThighLength, rig.ShinLength, pole, ground, up, out rightKnee))
            { error = "Skate rider legs cannot reach the board in a handplant."; return false; }
            // The feet lie flat on the deck, whose top faces the pelvis; the toes across it, the lead
            // foot's a little toward the nose.
            var mid = (leadAnkle + backAnkle) * 0.5;
            var grip = Unit(pelvis - mid);
            if (grip.LengthSquared < 0.5) grip = up;
            var deckNormal = Unit(Blend(up, grip, f));
            var along = Unit(leadAnkle - backAnkle);
            if (along.LengthSquared < 0.5) along = travel;
            var leadToeDir = Unit(Project(facing + along * 0.45, deckNormal));
            var backToeDir = Unit(Project(facing + along * 0.1, deckNormal));
            if (leadToeDir.LengthSquared < 0.5) leadToeDir = facing;
            if (backToeDir.LengthSquared < 0.5) backToeDir = facing;
            var leadToe = Toe(leadAnkle, leadToeDir, up, rig.FootLength, 0);
            var backToe = Toe(backAnkle, backToeDir, up, rig.FootLength, 0);
            var leftToe = backIsLeft ? backToe : leadToe;
            var rightToe = backIsLeft ? leadToe : backToe;
            // The planted arm straight to the hand; the other hand on the deck's top at the back foot,
            // a little toward the toe edge. Both from the arm joints.
            var freeJoint = (leadIsLeft ? rightShoulder : leftShoulder) + chestUp * rig.ShoulderRise;
            var freeTarget = backAnkle + grip * Math.Min(0, input.DeckOffset) + chestForward * PlantDeck;
            SkateVector plantedElbow, plantedHand, freeElbow, freeHand;
            if (!Arm(plantedJoint, hand, rig, leadIsLeft ? -1 : 1, chestRight, chestForward, ground, up, out plantedElbow, out plantedHand) ||
                !Arm(freeJoint, freeTarget, rig, leadIsLeft ? 1 : -1, chestRight, chestForward, ground, up, out freeElbow, out freeHand))
            { error = "Skate rider arms cannot reach in a handplant."; return false; }
            var leftElbow = leadIsLeft ? plantedElbow : freeElbow; var leftHand = leadIsLeft ? plantedHand : freeHand;
            var rightElbow = leadIsLeft ? freeElbow : plantedElbow; var rightHand = leadIsLeft ? freeHand : plantedHand;
            pose = new SkateRiderPose(pelvis, leftHip, rightHip, leftKnee, rightKnee, leftAnkle, rightAnkle,
                leftToe, rightToe, chest, neck, head, leftShoulder, rightShoulder, leftElbow, rightElbow, leftHand, rightHand,
                pelvisForward, pelvisUp, chestForward, chestUp, chestForward);
            if (!Finite(pose)) { pose = default(SkateRiderPose); error = "Skate rider pose exceeds the allowed bounds."; return false; }
            return true;
        }

        // Two poses mixed: the joints between the two, the frame directions too and made unit.
        private static SkateRiderPose Blend(SkateRiderPose a, SkateRiderPose b, double t)
        {
            return new SkateRiderPose(Blend(a.Pelvis, b.Pelvis, t), Blend(a.LeftHip, b.LeftHip, t), Blend(a.RightHip, b.RightHip, t),
                Blend(a.LeftKnee, b.LeftKnee, t), Blend(a.RightKnee, b.RightKnee, t), Blend(a.LeftAnkle, b.LeftAnkle, t),
                Blend(a.RightAnkle, b.RightAnkle, t), Blend(a.LeftToe, b.LeftToe, t), Blend(a.RightToe, b.RightToe, t),
                Blend(a.Chest, b.Chest, t), Blend(a.Neck, b.Neck, t), Blend(a.Head, b.Head, t),
                Blend(a.LeftShoulder, b.LeftShoulder, t), Blend(a.RightShoulder, b.RightShoulder, t),
                Blend(a.LeftElbow, b.LeftElbow, t), Blend(a.RightElbow, b.RightElbow, t),
                Blend(a.LeftHand, b.LeftHand, t), Blend(a.RightHand, b.RightHand, t),
                Turn(a.PelvisForward, b.PelvisForward, t), Turn(a.PelvisUp, b.PelvisUp, t),
                Turn(a.ChestForward, b.ChestForward, t), Turn(a.ChestUp, b.ChestUp, t), Turn(a.HeadForward, b.HeadForward, t));
        }

        private static SkateVector Turn(SkateVector a, SkateVector b, double t)
        {
            var mixed = Unit(Blend(a, b, t));
            return mixed.LengthSquared < 0.5 ? (t < 0.5 ? a : b) : mixed;
        }

        private static double PushTurn(double phase)
        { return phase < 0.2 ? Smooth(phase / 0.2) : phase < 0.75 ? 1 : Smooth((1 - phase) / 0.25); }

        public static double PushContact(double phase)
        {
            if (!Finite(phase) || phase < 0 || phase > 1) return 0;
            return phase <= PushDown ? Smooth(phase / PushDown) : phase <= PushUp ? 1 :
                phase <= 0.85 ? 1 - Smooth((phase - PushUp) / 0.25) : 0;
        }

        private static SkateVector PitchFoot(SkateVector direction, SkateVector travel, SkateVector up, double pitch)
        { return direction + (travel * (Math.Cos(pitch) - 1) + up * Math.Sin(pitch)) * SkateVector.Dot(direction, travel); }

        private static double GrabPitch(SkateVector pelvis, SkateVector carved, SkateVector ahead, SkateVector target,
                                        SkateRiderRig rig, double roll, bool backIsLeft, double initial)
        {
            double reach = rig.UpperArmLength + rig.ForearmLength - ReachMargin * 2;
            var across = Cross(carved, ahead);
            double shoulderSide = (backIsLeft ? -1 : 1) * rig.ShoulderWidth * 0.5;
            double radius = rig.SpineLength * Math.Cos(roll) - shoulderSide * Math.Sin(roll);
            var centre = pelvis + across * (rig.SpineLength * Math.Sin(roll) + shoulderSide * Math.Cos(roll));
            var offset = target - centre;
            double vertical = radius * SkateVector.Dot(offset, carved), forward = radius * SkateVector.Dot(offset, ahead);
            double required = (offset.LengthSquared + radius * radius - reach * reach) * 0.5;
            double best = initial, bestValue = vertical * Math.Cos(initial) + forward * Math.Sin(initial);
            if (bestValue >= required) return initial;
            double magnitude = Math.Sqrt(vertical * vertical + forward * forward);
            if (magnitude <= 1e-12) return initial;
            double endValue = vertical * Math.Cos(GrabFoldLimit) + forward * Math.Sin(GrabFoldLimit);
            if (endValue > bestValue) { best = GrabFoldLimit; bestValue = endValue; }
            double peak = Math.Atan2(forward, vertical);
            double crossing = double.PositiveInfinity;
            double angle = Math.Acos(Math.Max(-1, Math.Min(1, required / magnitude)));
            for (int turn = -1; turn <= 1; turn++)
            {
                double maximum = peak + turn * 2 * Math.PI;
                if (maximum >= initial && maximum <= GrabFoldLimit && magnitude > bestValue)
                { best = maximum; bestValue = magnitude; }
                if (required > magnitude) continue;
                double first = maximum - angle, second = maximum + angle;
                if (first >= initial && first <= GrabFoldLimit) crossing = Math.Min(crossing, first);
                if (second >= initial && second <= GrabFoldLimit) crossing = Math.Min(crossing, second);
            }
            return double.IsInfinity(crossing) ? best : crossing;
        }

        // The kick at the start of a turn (fraction 0..1 of it): out over the first quarter, back
        // over the deck by the half, not at all after it.
        private static double Kick(double turned)
        { return turned < 0.5 ? Math.Sin(turned * 2 * Math.PI) : 0; }

        // Where the pushing foot is in its stroke, from -1 (planted ahead) to 1 (swept back), 0 on
        // its place on the deck: read from the foot itself, so the arms and hips follow the contact
        // and not a clock that has no relation to it. Each side is measured against that stroke's
        // own end (origin as PushFoot has it): the leading foot's push plants only a little ahead
        // of its place and sweeps far behind it, the trailing foot's the other way round.
        private static double StrokeSwing(SkateVector foot, SkateVector rest, SkateVector origin, SkateVector travel)
        {
            double back = SkateVector.Dot(rest - foot, travel);
            double reach = back >= 0 ? SkateVector.Dot(rest - origin, travel) + PushSweep : SkateVector.Dot(origin - rest, travel) + PushPlant;
            if (reach < 1e-6) return back >= 0 ? 1 : -1;
            return Math.Max(-1, Math.Min(1, back / reach));
        }

        // The pushing foot through a stroke: from its resting spot on the deck (rest) down to the
        // ground PushPlant ahead of origin, back along the ground to PushSweep behind it, up and
        // back to its spot.
        private static SkateVector PushFoot(SkateVector rest, SkateVector origin, SkateVector travel, SkateVector side,
                                           SkateVector up, double phase, double groundDrop, double cycleDistance, double footLength, out double grounded, out double heel)
        {
            var deck = rest + up * SkatePose.FootLift;
            var front = origin + travel * PushPlant + side * SkatePose.PushFootSide + up * (SkatePose.FootLift - groundDrop);
            var back = origin - travel * PushSweep + side * SkatePose.PushFootSide + up * (SkatePose.FootLift - groundDrop + PushHeel);
            var lifted = back + up * (0.1 + groundDrop * 0.5) + travel * 0.06;
            heel = 0;
            grounded = PushContact(phase);
            if (phase <= PushDown) return SwingFoot(deck, front, travel, phase / PushDown, cycleDistance * PushDown, footLength, true);
            // Contact runs linearly by actual board distance in the client drive. Remapping this
            // part would slide a planted foot through the world, even with unchanged endpoints.
            if (phase <= PushUp) { heel = PushHeel * (phase - PushDown) / (PushUp - PushDown); return Blend(front, back, (phase - PushDown) / (PushUp - PushDown)); }
            if (phase <= 0.85) { heel = PushHeel * grounded; return SwingFoot(back, lifted, travel, (phase - PushUp) / 0.25, cycleDistance * 0.25, footLength, false); }
            grounded = 0; return Blend(lifted, deck, Smooth((phase - 0.85) / 0.15));
        }

        private static SkateVector SwingFoot(SkateVector from, SkateVector to, SkateVector travel, double t, double distance, double footLength, bool entering)
        {
            // Match the ground contact's world velocity only near its endpoint. A full-segment
            // tangent grows with speed and can swing beyond the legs' reach; its influence is
            // instead limited by the measured foot length, with no change to the contact stroke.
            var width = Math.Min(1, footLength / Math.Max(0.000001, distance));
            var q = entering ? (t - (1 - width)) / width : t / width;
            if (q <= 0 || q >= 1) return Blend(from, to, Smooth(t));
            var q2 = q * q; var q3 = q2 * q;
            var tangent = -distance * width;
            return Blend(from, to, Smooth(t)) + travel * (tangent * (entering ? q3 - q2 : q3 - 2 * q2 + q));
        }

        private static SkateVector Toe(SkateVector ankle, SkateVector direction, SkateVector up, double length, double heel)
        {
            double down = Math.Min(heel, length * 0.8);
            return ankle + direction * Math.Sqrt(length * length - down * down) - up * down;
        }

        private static void Bail(SkateVector origin, SkateVector travel, SkateVector facing, SkateVector up,
                                 SkateRiderRig rig, double groundDrop, double ankleHeight, double crouch, double phase,
                                 out SkateVector leadAnkle, out SkateVector backAnkle, out SkateVector pelvis,
                                 out double pelvisDown, out double chestPitch, out double yaw,
                                 out SkateVector leftHand, out SkateVector rightHand)
        {
            var ground = origin + up * (SkatePose.FootLift - groundDrop);
            double h = rig.PelvisHeight, half = rig.ShoulderWidth * 0.5;
            double riding = Math.Min(SkatePose.CrouchDrop, h * 0.65) * crouch - groundDrop;
            double onHands = 0.04 - ankleHeight;
            int k = phase < 0.3 ? 0 : phase < 0.62 ? 1 : 2;
            double t = Smooth(k == 0 ? phase / 0.3 : k == 1 ? (phase - 0.3) / 0.32 : (phase - 0.62) / 0.38);
            leadAnkle = ground + travel * Mix(SkatePose.FootOffset, 0.55, 0.12, 0.30, k, t) +
                facing * Mix(0, -0.10, -0.10, -0.10, k, t) + up * Mix(groundDrop, 0, 0.03, 0, k, t);
            backAnkle = ground + travel * Mix(-SkatePose.FootOffset, 0.05, 0.10, 0.05, k, t) +
                facing * Mix(0, 0.08, 0.10, 0.10, k, t) + up * Mix(groundDrop, 0.05, 0.03, 0, k, t);
            pelvis = ground + travel * Mix(0, 0.38, 0.52, 0.22, k, t);
            pelvisDown = Mix(riding, 0.30 * h, h - 0.36, 0.40 * h, k, t);
            chestPitch = Mix(0.1 + 0.12 * crouch, 0.85, 1.45, 0.45, k, t);
            yaw = Mix(0.35, 1.35, 1.5, 1.2, k, t);
            var faced = facing * Math.Cos(yaw) + travel * Math.Sin(yaw);
            var across = Cross(up, faced);
            var between = pelvis + faced * (Mix(0.15, 0.50, 0.28, 0.25, k, t) + 0.25) +
                up * Mix(0.60 * h, 0.42 * h, onHands, 0.38 * h, k, t);
            leftHand = between - across * half;
            rightHand = between + across * half;
        }

        private static double Mix(double first, double second, double third, double fourth, int k, double t)
        { return k == 0 ? first + (second - first) * t : k == 1 ? second + (third - second) * t : third + (fourth - third) * t; }

        private static bool HeightRange(SkateVector hip, SkateVector ankle, SkateVector up,
                                        double first, double second, ref double minimum, ref double maximum)
        {
            var offset = ankle - hip;
            double vertical = SkateVector.Dot(offset, up);
            double horizontal = Math.Max(0, offset.LengthSquared - vertical * vertical);
            double far = first + second - LegReachMargin;
            double near = Math.Sqrt(Math.Abs(first * first - second * second)) + ReachMargin;
            if (horizontal > far * far) return false;
            minimum = Math.Max(minimum, vertical + Math.Sqrt(Math.Max(0, near * near - horizontal)));
            maximum = Math.Min(maximum, vertical + Math.Sqrt(Math.Max(0, far * far - horizontal)));
            return minimum <= maximum;
        }

        private static bool Arm(SkateVector shoulder, SkateVector target, SkateRiderRig rig, int side,
                                SkateVector right, SkateVector forward, SkateVector ground, SkateVector up,
                                out SkateVector elbow, out SkateVector hand)
        {
            var offset = target - shoulder;
            double distance = offset.Length;
            var direction = distance > ReachMargin ? offset * (1 / distance) : forward;
            double reach = Math.Max(Math.Abs(rig.UpperArmLength - rig.ForearmLength) + ReachMargin,
                Math.Min(rig.UpperArmLength + rig.ForearmLength - ReachMargin, distance));
            double floor = -SkateVector.Dot(shoulder - ground, up) / reach;
            double vertical = SkateVector.Dot(direction, up);
            if (vertical < floor)
            {
                var horizontal = Unit(Project(direction, up));
                if (horizontal.LengthSquared < 0.5) horizontal = right * side;
                vertical = Math.Max(-1, Math.Min(1, floor));
                direction = horizontal * Math.Sqrt(Math.Max(0, 1 - vertical * vertical)) + up * vertical;
            }
            hand = shoulder + direction * reach;
            var bend = forward * -1 + right * (side * 0.12);
            if (Limb(shoulder, hand, rig.UpperArmLength, rig.ForearmLength, bend, ground, up, out elbow)) return true;
            hand = shoulder + right * (side * reach);
            return Limb(shoulder, hand, rig.UpperArmLength, rig.ForearmLength, bend, ground, up, out elbow);
        }

        private static bool Limb(SkateVector root, SkateVector end, double first, double second,
                                 SkateVector bend, SkateVector ground, SkateVector up, out SkateVector joint)
        {
            joint = default(SkateVector);
            var offset = end - root;
            double distance = offset.Length;
            if (distance <= 1e-12 || distance > first + second + 1e-8 ||
                distance < Math.Abs(first - second) - 1e-8) return false;
            var direction = offset * (1 / distance);
            double along = (first * first - second * second + distance * distance) / (2 * distance);
            double radius = Math.Sqrt(Math.Max(0, first * first - along * along));
            var centre = root + direction * along;
            var pole = Unit(Project(bend, direction));
            if (pole.LengthSquared < 0.5)
            {
                pole = Unit(Project(up, direction));
                if (pole.LengthSquared < 0.5) pole = Unit(Cross(direction,
                    Math.Abs(direction.X) < 0.75 ? new SkateVector(1, 0, 0) : new SkateVector(0, 1, 0)));
            }
            double centreHeight = SkateVector.Dot(centre - ground, up);
            if (centreHeight + radius * SkateVector.Dot(pole, up) < 0)
            {
                var high = Project(up, direction);
                double heightRange = high.Length;
                if (radius <= 1e-12 || heightRange <= 1e-12)
                { joint = centre + pole * radius; return SkateVector.Dot(joint - ground, up) >= -1e-8; }
                double required = -centreHeight / (radius * heightRange);
                if (required > 1 + 1e-8) return false;
                high *= 1 / heightRange;
                var across = Unit(Project(pole, high));
                if (across.LengthSquared < 0.5) across = Unit(Cross(direction, high));
                required = Math.Max(-1, Math.Min(1, required));
                pole = high * required + across * Math.Sqrt(Math.Max(0, 1 - required * required));
            }
            joint = centre + pole * radius;
            return Vector(joint) && SkateVector.Dot(joint - ground, up) >= -1e-7;
        }

        private static bool Valid(SkateRiderRig rig)
        { return Measurement(rig.PelvisHeight) && Measurement(rig.HipWidth) && Measurement(rig.ThighLength) &&
            Measurement(rig.ShinLength) && Measurement(rig.FootLength) && Measurement(rig.SpineLength) &&
            Measurement(rig.ShoulderWidth) && Measurement(rig.UpperArmLength) && Measurement(rig.ForearmLength) &&
            Measurement(rig.NeckToHeadLength) && Finite(rig.ShoulderRise) && Math.Abs(rig.ShoulderRise) <= MaximumMeasurement; }
        private static bool Valid(SkateRiderContactState s)
        {
            return !s.Initialized || (s.Phase >= SkateRiderContactPhase.Ground && s.Phase <= SkateRiderContactPhase.Landing &&
                Finite(s.LastFlip) && Math.Abs(s.LastFlip) <= 3600000 && Finite(s.LastShove) && Math.Abs(s.LastShove) <= 3600000 &&
                Finite(s.FlipTravel) && s.FlipTravel >= 0 && s.FlipTravel <= 3600000 && Finite(s.ShoveTravel) && s.ShoveTravel >= 0 && s.ShoveTravel <= 3600000 &&
                s.FlipDirection >= -1 && s.FlipDirection <= 1 && s.ShoveDirection >= -1 && s.ShoveDirection <= 1 &&
                Fraction(s.FlipLift) && Fraction(s.ShoveLift) && Fraction(s.Flick) && Fraction(s.Scoop) && Fraction(s.Catch) && Fraction(s.GrabWeight) && Fraction(s.Landing) &&
                Finite(s.GrabSeconds) && s.GrabSeconds >= 0 && s.GrabSeconds <= 86400 && s.GrabKind >= SkateGrabKind.None && s.GrabKind <= SkateGrabKind.Tailbone);
        }
        private static bool Valid(SkateRiderInput input)
        { return Vector(input.BoardPosition) && Math.Abs(input.BoardPosition.X) <= 100000 &&
            Math.Abs(input.BoardPosition.Y) <= 100000 && Math.Abs(input.BoardPosition.Z) <= 100000 &&
            Vector(input.BoardForward) && Vector(input.BoardUp) &&
            Math.Abs(input.BoardForward.LengthSquared - 1) <= 0.0001 && Math.Abs(input.BoardUp.LengthSquared - 1) <= 0.0001 &&
            Math.Abs(SkateVector.Dot(input.BoardForward, input.BoardUp)) <= 0.0001 && input.BoardUp.Y > 0 &&
            (input.Stance == SkateStance.Regular || input.Stance == SkateStance.Goofy) &&
            Finite(input.Speed) && input.Speed >= 0 && input.Speed <= SkateMotion.MaximumGroundSpeed &&
            Finite(input.LeanDegrees) && Math.Abs(input.LeanDegrees) <= SkatePose.MaximumLean &&
            Finite(input.PushSeconds) && input.PushSeconds > 0 && input.PushSeconds <= 10 &&
            Fraction(input.Crouch) && Fraction(input.PushPhase) && Finite(input.FlipDegrees) &&
            Math.Abs(input.FlipDegrees) <= 3600000 && Finite(input.LookYawDegrees) && Math.Abs(input.LookYawDegrees) <= 3600000 &&
            Finite(input.DeckOffset) && Math.Abs(input.DeckOffset) <= MaximumMeasurement &&
            Finite(input.GroundDrop) && input.GroundDrop >= 0 && input.GroundDrop <= MaximumMeasurement &&
            Fraction(input.FootPlant) && Fraction(input.Handplant) && Finite(input.GrabSeconds) && input.GrabSeconds >= 0 && input.GrabSeconds <= 86400 &&
            Finite(input.DeckPitch) && Math.Abs(input.DeckPitch) <= MaximumDeckPitch && Fraction(input.NoseSlide) &&
            Finite(input.ChestTurnDegrees) && Math.Abs(input.ChestTurnDegrees) <= MaximumChestTurn &&
            Finite(input.ShoveDegrees) && Math.Abs(input.ShoveDegrees) <= 3600000 && Fraction(input.Landing) &&
            Valid(input.Contact) && Fraction(input.BailPhase) && input.GrabKind >= SkateGrabKind.None && input.GrabKind <= SkateGrabKind.Tailbone; }
        private static bool Finite(SkateRiderPose p)
        { return Vector(p.Pelvis) && Vector(p.LeftHip) && Vector(p.RightHip) && Vector(p.LeftKnee) && Vector(p.RightKnee) &&
            Vector(p.LeftAnkle) && Vector(p.RightAnkle) && Vector(p.LeftToe) && Vector(p.RightToe) && Vector(p.Chest) &&
            Vector(p.Neck) && Vector(p.Head) && Vector(p.LeftShoulder) && Vector(p.RightShoulder) &&
            Vector(p.LeftElbow) && Vector(p.RightElbow) && Vector(p.LeftHand) && Vector(p.RightHand) &&
            UnitVector(p.PelvisForward) && UnitVector(p.PelvisUp) && UnitVector(p.ChestForward) &&
            UnitVector(p.ChestUp) && UnitVector(p.HeadForward); }
        private static bool AboveGround(SkateRiderPose p, SkateVector ground, SkateVector up)
        { return Above(p.Pelvis, ground, up) && Above(p.LeftHip, ground, up) && Above(p.RightHip, ground, up) &&
            Above(p.LeftKnee, ground, up) && Above(p.RightKnee, ground, up) && Above(p.LeftAnkle, ground, up) &&
            Above(p.RightAnkle, ground, up) && Above(p.LeftToe, ground, up) && Above(p.RightToe, ground, up) &&
            Above(p.Chest, ground, up) && Above(p.Neck, ground, up) && Above(p.Head, ground, up) &&
            Above(p.LeftShoulder, ground, up) && Above(p.RightShoulder, ground, up) && Above(p.LeftElbow, ground, up) &&
            Above(p.RightElbow, ground, up) && Above(p.LeftHand, ground, up) && Above(p.RightHand, ground, up); }
        private static bool Above(SkateVector joint, SkateVector ground, SkateVector up)
        { return SkateVector.Dot(joint - ground, up) >= -1e-7; }
        private static bool Measurement(double value)
        { return Finite(value) && value >= MinimumMeasurement && value <= MaximumMeasurement; }
        private static bool Fraction(double value) { return Finite(value) && value >= 0 && value <= 1; }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static bool Vector(SkateVector value) { return Finite(value.X) && Finite(value.Y) && Finite(value.Z); }
        private static bool UnitVector(SkateVector value) { return Vector(value) && Math.Abs(value.LengthSquared - 1) < 1e-6; }
        private static SkateVector Unit(SkateVector value)
        { double length = value.Length; return length > 1e-12 ? value * (1 / length) : default(SkateVector); }
        private static SkateVector Project(SkateVector value, SkateVector normal) { return value - normal * SkateVector.Dot(value, normal); }
        private static SkateVector Cross(SkateVector a, SkateVector b)
        { return new SkateVector(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X); }
        // A vector's coordinates along three frame axes, as (up, forward, right).
        private static SkateVector Local(SkateVector v, SkateVector up, SkateVector forward, SkateVector right)
        { return new SkateVector(SkateVector.Dot(v, up), SkateVector.Dot(v, forward), SkateVector.Dot(v, right)); }
        private static SkateVector Forward(double yaw)
        { double radians = yaw * Math.PI / 180; return new SkateVector(Math.Sin(radians), 0, Math.Cos(radians)); }
        private static SkateVector Blend(SkateVector a, SkateVector b, double t) { return a + (b - a) * t; }
        private static double Smooth(double t) { return t * t * (3 - 2 * t); }
    }
}
