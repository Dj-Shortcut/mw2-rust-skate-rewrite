// The legacy Input class is disabled in this client build; a controller arrives as keys through
// Steam Input, and its sticks only through the reader outside the game (SkatePad). Presses are
// latched until a physics step has seen them: the game steps physics at 32 Hz and draws frames
// more often. Nothing is read while the cursor is free (console, chat, inventory), so typing never
// rides.
// With a controller the layout is that of the skate. games: left stick steers and turns the rider
// in the air, right stick is the board (SkateFlick), A and X push, B brakes, Y gets on and off, a
// trigger grabs and the right stick picks the grab. Steam's layout keeps sending its keys for the
// same buttons, so while the controller rides the keys do not.
using System;
using Shortcut.RustMod;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public static class SkateKeys
{
    public const float PadRest = 0.5f, SteerDead = 0.18f, Straight = 0.35f, StickPush = 0.75f, SpinFrom = 0.5f, LeastPop = 0.7f, GrabStick = 0.6f;
    public static bool Scripted;
    public static bool Push, Brake, Left, Right, Crouch, Jump, Manual, NoseManual;
    // As in skate.: A pushes with the back foot, X with the front (mongo). A tapped just before the
    // pop's flick, while the right stick is held back for it, makes it a no comply. A trigger held and A or X pressed is a foot plant: LT+A a
    // boneless, LT+X or RT+X a fastplant, RT+A a beanplant. A bumper held when the board meets an
    // edge is a handplant: LB frontside, RB backside. On the keyboard Q (no comply) or Z (boneless)
    // held with Space.
    public const float NoComplyTap = 0.35f;
    // How long the left stick must stay on a side, and still, before it is a turn: a stick that
    // travels round the rim faster than RimMoving (degrees a second) is circling for a body varial,
    // not held to a side, however slowly it goes round.
    public const float SpinHold = 0.06f, RimMoving = 180f;
    private static int rawSide; private static float sideSince = -99f, rimAngle, rimAt = -99f, movedAt = -99f; private static bool onRim; private static int bodySeen;
    public static bool Mongo, HandplantFront, HandplantBack;
    private static float aTappedAt = -99f;
    // What a physics step has not seen yet. A pop comes with what the board does in it: whole flips
    // (a kickflip is +1) and degrees of shove under the feet (frontside is +). LateFlip is flips
    // asked for in the air. SpinPressed: the turn was asked for afresh, not held from before.
    public static bool JumpPressed, SpinPressed;
    public static SkatePopKind PopKind = SkatePopKind.Ollie;
    public static int PopFlip, LateFlip;
    // Asked for in the air: degrees of shove under the feet, and degrees of body varial (the rider
    // turns, the board does not).
    public static float LateShove, BodyVarial;
    public static float PopShove, PopScale = 1f;
    public static bool JumpTap, ToggleTap, CameraTap, CrouchTap;
    public static float LookYaw;
    // Pad: the controller rides, not the keyboard. Analog: Steer is a rate, not a direction to turn to.
    public static bool Pad, Analog, Charging;
    public static float Steer, Charge;
    // Held in the air: the rider's turn, +1 to his right as the stick or the keys say, and the grab.
    public static float Spin;
    public static SkateGrabKind Grab;
    private static bool wasPush, wasBrake, wasLeft, wasRight, wasJump, wasCrouch, wasToggle, wasCamera, wasStance, wasBody;
    private static float wasSpin;
    private static bool popKeys = true, crouchKeys = true, cameraKey = true, manualKey = true, taps = true, cursorKnown = true, failed;

    public static void Reset()
    {
        Push = Brake = Left = Right = Crouch = Jump = Manual = NoseManual = Analog = Charging = false; Steer = Charge = Spin = wasSpin = 0f; PopScale = 1f;
        Grab = SkateGrabKind.None; PopKind = SkatePopKind.Ollie; Mongo = HandplantFront = HandplantBack = false; aTappedAt = -99f;
        EndStep(); JumpTap = ToggleTap = CameraTap = CrouchTap = false;
    }

    public static void Poll()
    {
        JumpTap = ToggleTap = CameraTap = CrouchTap = false;
        if (Scripted || failed) return;
        var now = Time.realtimeSinceStartup;
        SkatePad.Poll(now);
        var padDown = SkatePieces.Step(SkatePad.Pressed(), now);
        SkatePadLog.Frame(now);
        try
        {
            var kb = Keyboard.current;
            if (kb == null || CursorFree())
            {
                Push = Brake = Left = Right = Crouch = Jump = Manual = NoseManual = Charging = false; Steer = Charge = Spin = wasSpin = 0f; Grab = SkateGrabKind.None;
                wasPush = wasBrake = wasLeft = wasRight = wasJump = wasCrouch = wasToggle = wasCamera = false; EndStep(); SkatePad.DropPops();
                // J and X held through chat or the console are not a press when the cursor locks
                // again: their latches follow the keys while input is suspended.
                if (kb != null) { try { wasStance = Down(kb.jKey); wasBody = Down(kb.xKey); } catch (Exception) { wasStance = wasBody = true; } }
                else wasStance = wasBody = true;
                return;
            }
            SkatePieces.Keys(Down(kb.pKey), Down(kb.qKey), Down(kb.eKey), Down(kb.oKey), Down(kb.backspaceKey));
            var stanceKey = Down(kb.jKey);
            if (stanceKey && !wasStance) SkateRide.ToggleStance();
            wasStance = stanceKey;
            bool push = Down(kb.wKey), brake = Down(kb.sKey), left = Down(kb.aKey), right = Down(kb.dKey);
            bool jump = Down(kb.spaceKey), toggle = Down(kb.kKey), crouch = false, camera = false, manual = false, noComply = false, boneless = false, body = false;
            if (popKeys)
            {
                try { noComply = Down(kb.qKey); boneless = Down(kb.zKey); body = Down(kb.xKey); }
                catch (Exception e) { popKeys = false; Out.Say("INPUT no comply, boneless and body varial keys unavailable: " + e.GetType().Name + ": " + e.Message); }
            }
            if (crouchKeys)
            {
                try { crouch = Down(kb.leftCtrlKey) || Down(kb.cKey); }
                catch (Exception e) { crouchKeys = false; Out.Say("INPUT crouch keys unavailable: " + e.GetType().Name + ": " + e.Message); }
            }
            if (cameraKey)
            {
                try { camera = Down(kb.lKey); }
                catch (Exception e) { cameraKey = false; Out.Say("INPUT camera key unavailable: " + e.GetType().Name + ": " + e.Message); }
            }
            if (manualKey)
            {
                try { manual = kb.leftShiftKey.isPressed; }
                catch (Exception e) { manualKey = false; Out.Say("INPUT manual key unavailable: " + e.GetType().Name + ": " + e.Message); }
            }
            // Whose hands are on the ride: the controller's from the moment it is touched; the
            // keyboard's again when a riding key goes down while the controller has been at rest,
            // which Steam's layout cannot have sent.
            var keyDown = (push && !wasPush) || (brake && !wasBrake) || (left && !wasLeft) || (right && !wasRight) || (jump && !wasJump) || (crouch && !wasCrouch);
            var had = Pad;
            if (!SkatePad.Shared) Pad = false;
            else if (now - SkatePad.TouchedAt < 0.05f) Pad = true;
            else if (keyDown && now - SkatePad.TouchedAt > PadRest) Pad = false;
            if (Pad != had) Out.Say("INPUT the " + (Pad ? "controller" : "keyboard") + " rides");

            if (toggle && !wasToggle) ToggleTap = true;
            if (camera && !wasCamera) CameraTap = true;
            // Two quick jumps get on the board, whoever rides: Steam sends the jump key for the controller too.
            if (jump && !wasJump) JumpTap = true;
            if (Pad)
            {
                if ((padDown & SkatePad.Y) != 0) ToggleTap = true;
                if ((padDown & SkatePad.RightStickClick) != 0) CameraTap = true;
                float x = SkatePad.LeftX, y = SkatePad.LeftY;
                var straight = Math.Abs(x) < Straight;
                Analog = true;
                Steer = Math.Abs(x) <= SteerDead ? 0f : (x - Math.Sign(x) * SteerDead) / (1f - SteerDead);
                bool lt0 = SkatePad.LeftTrigger > SkatePad.Pulled, rt0 = SkatePad.RightTrigger > SkatePad.Pulled;
                // With a trigger held A and X are foot plants, not pushes.
                Push = (!lt0 && !rt0 && (SkatePad.Buttons & (SkatePad.A | SkatePad.X)) != 0) || (straight && y > StickPush);
                Mongo = (SkatePad.Buttons & SkatePad.X) != 0 && (SkatePad.Buttons & SkatePad.A) == 0;
                HandplantFront = (SkatePad.Buttons & SkatePad.LeftBumper) != 0; HandplantBack = (SkatePad.Buttons & SkatePad.RightBumper) != 0;
                // Only while the right stick is held back for the pop: a push tapped on its own stays a push.
                if ((padDown & SkatePad.A) != 0 && SkatePad.Charging) aTappedAt = now;
                if ((lt0 || rt0) && (padDown & (SkatePad.A | SkatePad.X)) != 0 && SkateRide.Mode != RideMode.Air)
                {
                    JumpPressed = true; PopFlip = 0; PopShove = 0f; PopScale = 1f;
                    PopKind = (padDown & SkatePad.A) != 0 ? (lt0 ? SkatePopKind.Boneless : SkatePopKind.Beanplant) : SkatePopKind.Fastplant;
                    aTappedAt = -99f;
                }
                Brake = (SkatePad.Buttons & SkatePad.B) != 0 || (straight && y < -StickPush);
                // A side counts as held after a moment of standing still there: a circle of the
                // stick (a body varial) passes both sides without asking for a turn.
                var side = x < -SpinFrom ? -1 : x > SpinFrom ? 1 : 0;
                if (side != rawSide) { rawSide = side; sideSince = now; }
                // A circle completed between two frames (the reader replays every sample) is no
                // held side either: the body varial it made counts as the stick moving.
                if (SkatePad.BodyVarials != bodySeen) { bodySeen = SkatePad.BodyVarials; movedAt = now; }
                if (x * x + y * y > SpinFrom * SpinFrom)
                {
                    var angle = (float)(Math.Atan2(x, y) * 180.0 / Math.PI);
                    if (onRim && now - rimAt > 0.0005f && Math.Abs(SkateRide.Delta(rimAngle, angle)) / (now - rimAt) > RimMoving) movedAt = now;
                    rimAngle = angle; rimAt = now; onRim = true;
                }
                else onRim = false;
                var held = side != 0 && now - sideSince >= SpinHold && now - movedAt >= SpinHold;
                Left = held && side < 0; Right = held && side > 0;
                Spin = Left ? -1f : Right ? 1f : 0f;
                bool lt = SkatePad.LeftTrigger > SkatePad.Pulled, rt = SkatePad.RightTrigger > SkatePad.Pulled;
                Crouch = lt || rt;
                Grab = GrabOf(lt, rt, SkatePad.RightX, SkatePad.RightY, SkateRide.Goofy != (SkateRide.On && SkateRide.TailFirst));
                Manual = SkatePad.Manual; NoseManual = SkatePad.NoseManual; Jump = false;
                Charging = SkatePad.Charging; Charge = SkatePad.Charge;
                BodyVarial += SkatePad.TakeBodyVarial();
                SkatePop pop;
                while (SkatePad.TakePop(out pop))
                {
                    var flips = (int)Math.Round(pop.FlipTurns);
                    if (pop.Kind == SkatePopKind.None) { LateFlip += flips; LateShove += (float)pop.ShoveDegrees; }
                    else { JumpPressed = true; PopKind = PopWith(pop.Kind, now - aTappedAt < NoComplyTap && (SkatePad.Buttons & SkatePad.A) == 0, false); aTappedAt = -99f; PopFlip = flips; PopShove = (float)pop.ShoveDegrees; PopScale = LeastPop + (1f - LeastPop) * (float)pop.Strength; }
                }
            }
            else
            {
                SkatePad.DropPops();
                Analog = Charging = false; Charge = 0f; NoseManual = false;
                Steer = (left ? -1f : 0f) + (right ? 1f : 0f);
                Spin = Steer;
                if (push && !wasPush) LateFlip++;
                if (brake && !wasBrake) LateFlip--;
                // X tapped in the air: a body varial, the rider's half round without the board.
                if (body && !wasBody) BodyVarial += 180f;
                wasBody = body;
                Mongo = HandplantFront = HandplantBack = false;
                if (jump && !wasJump) { JumpPressed = true; PopKind = PopWith(SkatePopKind.Ollie, noComply, boneless); PopFlip = 0; PopShove = 0f; PopScale = 1f; }
                if (crouch && !wasCrouch) CrouchTap = true;
                Push = push; Brake = brake; Left = left; Right = right; Jump = jump; Crouch = crouch; Manual = manual;
                Grab = crouch ? SkateGrabKind.Legacy : SkateGrabKind.None;
            }
            if (Spin != 0f && Spin != wasSpin) SpinPressed = true;
            wasSpin = Spin;
            wasPush = push; wasBrake = brake; wasLeft = left; wasRight = right; wasJump = jump; wasCrouch = crouch; wasToggle = toggle; wasCamera = camera;
        }
        catch (Exception e) { failed = true; Reset(); Out.Say("INPUT key read threw " + e.GetType().Name + ": " + e.Message); }
    }

    // A no comply or a boneless takes the place of an ollie; a nollie stays a nollie.
    public static SkatePopKind PopWith(SkatePopKind kind, bool noComply, bool boneless)
    {
        if (kind != SkatePopKind.Ollie) return kind;
        return noComply ? SkatePopKind.NoComply : boneless ? SkatePopKind.Boneless : kind;
    }

    public static void EndStep() { JumpPressed = SpinPressed = false; PopFlip = LateFlip = 0; PopShove = LateShove = BodyVarial = 0f; }

    // Which grab the triggers and the right stick ask for. The triggers alone give the three plain
    // grabs; with the stick held up, down or to a side they give the others. The stick's sides are
    // the rider's own: mirrored when he rides tail first.
    public static SkateGrabKind GrabOf(bool lt, bool rt, float x, float y, bool mirrored)
    {
        if (!lt && !rt) return SkateGrabKind.None;
        if (mirrored) x = -x;
        float ax = Math.Abs(x), ay = Math.Abs(y);
        var up = y > GrabStick && ay >= ax; var down = y < -GrabStick && ay >= ax;
        var left = x < -GrabStick && ax > ay; var right = x > GrabStick && ax > ay;
        if (lt && rt) return up ? SkateGrabKind.Nosebone : down ? SkateGrabKind.Tailbone : SkateGrabKind.Double;
        if (rt) return up ? SkateGrabKind.Seatbelt : down ? SkateGrabKind.Nose : left ? SkateGrabKind.Melon : right ? SkateGrabKind.Method : SkateGrabKind.Backside;
        return up ? SkateGrabKind.Tail : down ? SkateGrabKind.Crail : right ? SkateGrabKind.Stalefish : SkateGrabKind.Frontside;
    }

    // A tap can begin and end between two frames; the key's state alone never shows such a tap.
    public static bool Down(KeyControl key)
    {
        if (key.isPressed) return true;
        if (!taps) return false;
        try { return key.wasPressedThisFrame; }
        catch (Exception e) { taps = false; Out.Say("INPUT taps between frames cannot be seen: " + e.GetType().Name + ": " + e.Message); return false; }
    }

    private static bool CursorFree()
    {
        if (!cursorKnown) return false;
        try { return Cursor.lockState != CursorLockMode.Locked; }
        catch (Exception e) { cursorKnown = false; Out.Say("INPUT cursor state unavailable: " + e.GetType().Name + ": " + e.Message); return false; }
    }
}
