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

    public struct SkateRiderInput
    {
        public readonly SkateVector BoardPosition, BoardForward, BoardUp;
        public readonly SkateStance Stance;
        public readonly bool Switch, Airborne, Pushing, Grab, Bailed;
        public readonly double Speed, LeanDegrees, Crouch, PushPhase, FlipDegrees, LookYawDegrees, DeckOffset;
        public readonly double GroundDrop, BailPhase;
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
        { ChestTurnDegrees = chestTurnDegrees; NoseSlide = noseSlide; DeckPitch = deckPitch; MongoPush = mongoPush; FootPlant = footPlant; GrabSeconds = grabSeconds; Handplant = handplant;
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
        private const double ReachMargin = 0.000001;

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
            return Riding(rig, input, out pose, out error);
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
            double speed = input.Speed / SkateMotion.MaximumGroundSpeed;
            double lean = input.LeanDegrees * Math.PI / 180;
            double turn = pushing ? PushTurn(input.PushPhase) : 0;
            double swing = pushing ? Math.Sin(2 * Math.PI * input.PushPhase) : 0;
            double bail = input.Bailed ? input.BailPhase : 0;

            // In a flip the feet come up off the deck and apart, knees tucked, so the board turns over
            // under them; they come back down onto it as it comes round.
            double flipping = input.Airborne && !input.Bailed ? Math.Abs(Math.Sin((input.FlipDegrees % 360) * Math.PI / 360)) : 0;
            double lift = SkatePose.FootLift + (SkatePose.FlipFootLift + FlipTuck) * flipping;
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
            var leadDeck = backIsLeft ? rightAnkle : leftAnkle;
            var backDeck = backIsLeft ? leftAnkle : rightAnkle;
            double leadToe = (RidingLeadToe + (PushLeadToe - RidingLeadToe) * turn) * Math.PI / 180;
            double backToe = RidingBackToe * Math.PI / 180;
            double grounded = 0, heel = 0;
            var leadAnkle = leadDeck; var backAnkle = backDeck;
            bool mongo = pushing && input.MongoPush;
            if (mongo)
            {
                // The front foot pushes; the back foot stays on the tail and the toes stay forward.
                // The stroke runs a foot's length further forward: the pushing foot starts from the front of the deck.
                leadAnkle = PushFoot(input.BoardPosition + travel * (SkatePose.FootOffset * 2), travel, facing, up, input.PushPhase, input.GroundDrop, out grounded, out heel);
                leadToe = RidingLeadToe * Math.PI / 180 + (Math.PI / 2 - RidingLeadToe * Math.PI / 180) * grounded;
            }
            else if (pushing)
            {
                backAnkle = PushFoot(input.BoardPosition, travel, facing, up, input.PushPhase, input.GroundDrop, out grounded, out heel);
                backToe = backToe + (Math.PI / 2 - backToe) * grounded;
            }
            if (input.FootPlant > 0 && input.Airborne && !input.Bailed)
            {
                // The front foot off the deck and down toward the ground beside the board, ahead of it.
                var planted = input.BoardPosition + facing * 0.2 + travel * 0.22 + up * (SkatePose.FootLift - plantDown);
                leadAnkle = leadAnkle + (planted - leadAnkle) * input.FootPlant;
            }
            double leanScale = 1, yawScale = input.Airborne ? AirYaw : 1;
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
            if (SkateVector.Dot(leftAnkle - floor, up) < -1e-8 || SkateVector.Dot(rightAnkle - floor, up) < -1e-8)
            { error = "Skate rider feet are below the ground plane."; return false; }

            double balance = input.Bailed ? 0 : 0.04 + 0.03 * input.Crouch;
            double pelvisLean = (lean * side - balance) * leanScale;
            double pelvisYaw = input.Bailed ? bailYaw : (RidingPelvisYaw + (PushPelvisYaw - RidingPelvisYaw) * turn) * yawScale * Math.PI / 180;
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
                double onLead = 0.5 + PushWeight * turn * (mongo ? -1 : 1);
                centre = Project(leadDeck * onLead + backDeck * (1 - onLead) - input.BoardPosition, up) + input.BoardPosition;
                centre += right * (rig.PelvisHeight * Math.Sin(lean) * (grabbing ? 0.1 : 0.45)) + travel * (0.035 * speed);
                centre -= facing * (rig.SpineLength * balance * (grabbing ? 3 : 1));
                centre += facing * (PushSide * turn);
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
            double pitch = input.Bailed ? chestPitch : 0.1 + 0.12 * crouch + PushLean * turn;
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
            double armOut = armLength * (0.17 + 0.10 * crouch + 0.15 * carving + 0.35 * air);
            double armDown = armLength * (0.94 - 0.25 * crouch - 0.12 * carving - 0.40 * air);
            double armAhead = armLength * (0.10 + 0.12 * crouch + 0.05 * speed);
            var along = travel * (armLength * (backIsLeft ? -1 : 1));
            var counterSwing = chestForward * (armLength * PushSwing * swing * (backIsLeft ? -1 : 1));
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
            var leftToe = Toe(leftAnkle, leftToeDir, up, rig.FootLength, backIsLeft ? heel : 0);
            var rightToe = Toe(rightAnkle, rightToeDir, up, rig.FootLength, backIsLeft ? 0 : heel);
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
        private const double AirYaw = 0.35, PushWeight = 0.28, PushSide = 0.03, PushDip = 0.06, PushLean = 0.34, PushSwing = 0.28;
        // The pushing foot comes down PushPlant ahead of the board's middle and leaves it PushSweep
        // behind: the stroke, over which the foot is on the ground (push phase PushDown..PushUp).
        public const double PushPlant = 0.18, PushSweep = 0.44, PushDown = 0.25, PushUp = 0.6;
        private const double PushHeel = 0.07;
        private const double GrabRoll = 0.4, GrabFoldLimit = 1.0, GrabTuckMargin = 0.05, PlantDrop = 0.3, GrabBackLimit = 0.55, HeelTurn = 1.2;
        private const double HeelTurnSign = 1;
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
                case SkateGrabKind.Nosebone: return new GrabGrip(!noseIsLeft, 0, -0.36, 0.3, end: true,
                    extension: 0.12, legShift: -0.15, chestTurn: -0.35);
                case SkateGrabKind.Tailbone: return new GrabGrip(noseIsLeft, 0, 0.36, 0.3, end: true,
                    extension: 0.12, legShift: 0.15, chestTurn: 0.35);
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

        private static SkateVector PushFoot(SkateVector origin, SkateVector travel, SkateVector side,
                                           SkateVector up, double phase, double groundDrop, out double grounded, out double heel)
        {
            var deck = origin - travel * SkatePose.FootOffset + up * SkatePose.FootLift;
            var front = origin + travel * PushPlant + side * SkatePose.PushFootSide + up * (SkatePose.FootLift - groundDrop);
            var back = origin - travel * PushSweep + side * SkatePose.PushFootSide + up * (SkatePose.FootLift - groundDrop + PushHeel);
            var lifted = back + up * (0.1 + groundDrop * 0.5) + travel * 0.06;
            heel = 0;
            if (phase <= PushDown) { grounded = Smooth(phase / PushDown); return Blend(deck, front, grounded); }
            if (phase <= PushUp) { grounded = 1; heel = PushHeel * (phase - PushDown) / (PushUp - PushDown); return Blend(front, back, (phase - PushDown) / (PushUp - PushDown)); }
            if (phase <= 0.85) { grounded = 1 - Smooth((phase - PushUp) / 0.25); heel = PushHeel * grounded; return Blend(back, lifted, 1 - grounded); }
            grounded = 0; return Blend(lifted, deck, Smooth((phase - 0.85) / 0.15));
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
            double far = first + second - ReachMargin;
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
        private static bool Valid(SkateRiderInput input)
        { return Vector(input.BoardPosition) && Math.Abs(input.BoardPosition.X) <= 100000 &&
            Math.Abs(input.BoardPosition.Y) <= 100000 && Math.Abs(input.BoardPosition.Z) <= 100000 &&
            Vector(input.BoardForward) && Vector(input.BoardUp) &&
            Math.Abs(input.BoardForward.LengthSquared - 1) <= 0.0001 && Math.Abs(input.BoardUp.LengthSquared - 1) <= 0.0001 &&
            Math.Abs(SkateVector.Dot(input.BoardForward, input.BoardUp)) <= 0.0001 && input.BoardUp.Y > 0 &&
            (input.Stance == SkateStance.Regular || input.Stance == SkateStance.Goofy) &&
            Finite(input.Speed) && input.Speed >= 0 && input.Speed <= SkateMotion.MaximumGroundSpeed &&
            Finite(input.LeanDegrees) && Math.Abs(input.LeanDegrees) <= SkatePose.MaximumLean &&
            Fraction(input.Crouch) && Fraction(input.PushPhase) && Finite(input.FlipDegrees) &&
            Math.Abs(input.FlipDegrees) <= 3600000 && Finite(input.LookYawDegrees) && Math.Abs(input.LookYawDegrees) <= 3600000 &&
            Finite(input.DeckOffset) && Math.Abs(input.DeckOffset) <= MaximumMeasurement &&
            Finite(input.GroundDrop) && input.GroundDrop >= 0 && input.GroundDrop <= MaximumMeasurement &&
            Fraction(input.FootPlant) && Fraction(input.Handplant) && Finite(input.GrabSeconds) && input.GrabSeconds >= 0 && input.GrabSeconds <= 86400 &&
            Finite(input.DeckPitch) && Math.Abs(input.DeckPitch) <= MaximumDeckPitch && Fraction(input.NoseSlide) &&
            Finite(input.ChestTurnDegrees) && Math.Abs(input.ChestTurnDegrees) <= MaximumChestTurn &&
            Fraction(input.BailPhase) && input.GrabKind >= SkateGrabKind.None && input.GrabKind <= SkateGrabKind.Tailbone; }
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
