using System;
using System.Globalization;
using Shortcut.RustMod;
using UnityEngine;
using UnityEngine.InputSystem;

public static class SkatePieces
{
    public static readonly string[] Names = { "bank", "kicker", "quarterpipe", "ledge-low", "ledge-high", "long-ledge", "funbox", "halfpipe", "mini-quarterpipe", "large-quarterpipe", "half-wall-kicker", "mega-halfpipe" };
    public const int MenuButton = SkatePad.Back;
    public const float ShownSeconds = 6f, PlaceSeconds = 120f, AimReach = 25f, TurnStep = 15f, ClearGround = 0.04f;

    public static bool Open;
    private static bool rustBuilding, rustWheelOpen, switchingToRust, switchingFromClosed;
    private static object modePlayer, modeBody;
    private static float modeAwakeAt;
    private static Rect modeButtonArea;
    private static object modeButtonPlayer, modeButtonBody;
    private static float modeButtonAwakeAt, modeButtonAt = -99f;
    private static bool modeButtonToRust, modeButtonQueued, modeButtonHeld, modeButtonPaused, modeButtonConsumes;
    public static bool RustBuilding { get { return rustBuilding; } }
    public static bool RustWheelOpen { get { return rustWheelOpen; } }
    public static bool RustModeHint { get; private set; }
    public static bool CanDrawWheel { get { return Open && !SkateRide.On && SkateRig.Ready &&
        ReferenceEquals(menuPlayer, SkateRig.Local) && ReferenceEquals(menuBody, SkateRig.Body) && menuAwakeAt == SkateRig.AwakeAt &&
        SkateHeld.RustWheelHiddenForSkate(SkateRig.Local); } }
    private static float stanceSince = -1f;
    public static int Chosen;
    public static float Turn;
    public static string Asked = "", Said = "", Status = "";
    public static float SaidAt = -99f, AskedAt = -99f;
    public static int Sent, Placed;
    // Where the preview stands: the point the player aims at on the ground, and the piece's yaw.
    public static bool AimOk;
    public static Vector3 Aim;
    public static float Yaw;
    private enum Entry { None, Keyboard, MouseHold, ControllerHold }
    private static Entry entry;
    private static bool openingSkate;
    private static Entry openingEntry;
    private static object openingPlayer, openingBody;
    private static float openingAwakeAt, openingUntil;
    private static bool wasP, wasBack, wasRight, inputPrimed, cancelledHold, stickChosen;
    private static object menuPlayer, menuBody;
    private static float menuAwakeAt;
    private static Vector2 pointer;
    public static int Hover = -1;
    public static bool BlocksControls;
    public static bool OwnsCursor;
    public static int BuilderKind = -1;
    private static int requestedKind = -1;
    private static float replyUntil;
    private static Application.LogCallback replyListener;
    private static bool replyListenerFailed;
    private static bool builderRequested;
    private static string selectionToken = "";
    private static object selectionPlayer, selectionBody;
    private static float selectionAwakeAt;
    private static object selectionCursorPlan;
    private static string selectionCursorToken = "";
    private static float selectionCursorUntil;
    private static int selectionCursorPolls;
    private static float builderNoticeUntil;
    private static int builderTurn;
    private static bool wasPlaceTrigger, builderPad;
    // One explicit placement at a time. A timeout is uncertainty, never permission to spend again.
    private static string placementToken = "";
    private static float placementUntil;
    private static object placementPlayer, placementBody;
    private static float placementAwakeAt;
    private static int placementKind = -1;
    private static bool placementUnknown;
    private static string BuilderControls { get { return builderPad
        ? "RT places, RB / LB rotate. Back, then X: Rust building."
        : "Left click places, R rotates. Open the wheel, then Shift + P: Rust building."; } }
    private static bool offPending;
    private static float offUntil;
    private static int previewKind = -1;
    // The kind whose ghost is drawn now; -1 for none.
    public static int GhostKind { get { return previewKind; } }
    private static float previewYaw = 9999f; private static Vector3 previewAt;

    // OnGUI only records intent. Input owns the actual switch and its server/cursor gates.
    public static void ModeButton(Rect area, bool clicked)
    {
        if (!Application.isFocused || SkateRide.On || !SkateRig.Ready || SkateRig.Local == null || openingSkate || switchingToRust ||
            !(Open && OwnsCursor && CanDrawWheel) && !RustModeHint) return;
        modeButtonArea = area; modeButtonAt = Time.realtimeSinceStartup;
        modeButtonPlayer = SkateRig.Local; modeButtonBody = SkateRig.Body; modeButtonAwakeAt = SkateRig.AwakeAt;
        modeButtonToRust = Open;
        if (clicked)
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasReleasedThisFrame)
            {
                var p = mouse.position.ReadValue();
                if (area.Contains(new Vector2(p.x, Screen.height - p.y))) modeButtonQueued = true;
            }
        }
    }

    private static bool ModeButtonInput(float now, Mouse mouse)
    {
        bool current = now >= modeButtonAt && now - modeButtonAt <= 0.25f && Application.isFocused && !SkateRide.On && SkateRig.Ready &&
            ReferenceEquals(modeButtonPlayer, SkateRig.Local) && ReferenceEquals(modeButtonBody, SkateRig.Body) && modeButtonAwakeAt == SkateRig.AwakeAt &&
            !openingSkate && !switchingToRust && (modeButtonToRust ? Open && OwnsCursor && CanDrawWheel : rustBuilding && RustModeHint);
        bool click = current && modeButtonQueued;
        modeButtonQueued = false;
        bool held = mouse != null && mouse.leftButton.isPressed;
        var position = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
        bool over = current && mouse != null && modeButtonArea.Contains(new Vector2(position.x, Screen.height - position.y));
        bool consume = click || modeButtonHeld || (over && held);
        modeButtonConsumes = consume;
        modeButtonHeld = current && held && (over || modeButtonHeld);
        if (consume) BlocksControls = true;
        // Native placement must not also receive a footer-button press or release. Borrow only
        // the same local Planner while this control owns the mouse; normal Rust clicks elsewhere pass.
        if (current && rustBuilding && (over || consume))
        {
            string why;
            if (!SkateHeld.PausePlanner(SkateRig.Local, out why)) return false;
            modeButtonPaused = true;
        }
        else if (modeButtonPaused)
        {
            modeButtonPaused = false;
            if (!Open && !openingSkate && !builderRequested && !offPending) SkateHeld.ResumePlanner();
        }
        return click;
    }

    private static void ClearModeButton()
    {
        modeButtonQueued = modeButtonHeld = modeButtonConsumes = false;
        modeButtonPlayer = modeButtonBody = null; modeButtonAt = -99f;
        if (modeButtonPaused && !Open && !openingSkate && !builderRequested && !offPending) SkateHeld.ResumePlanner();
        modeButtonPaused = false;
    }

    private static void ClearSelectionCursor()
    { selectionCursorPlan = null; selectionCursorToken = ""; selectionCursorUntil = 0f; selectionCursorPolls = 0; }

    private static bool SelectionCursorSettling(float now, bool uiBlocked, bool cancel)
    {
        if (selectionCursorPlan == null) return false;
        // Rust's cursor manager retains the previous positive request. Only this just-closed
        // selection may wait for its next two input frames; no build input is allowed meanwhile.
        if (!uiBlocked || cancel || Open || openingSkate || !Application.isFocused || SkateRide.On || !SkateRig.Ready ||
            SkateRig.Local == null || SkateRig.Local.lifestate != BaseCombatEntity.LifeState.Alive ||
            !ReferenceEquals(selectionPlayer, SkateRig.Local) || !ReferenceEquals(selectionBody, SkateRig.Body) || selectionAwakeAt != SkateRig.AwakeAt ||
            !builderRequested || offPending || selectionToken != selectionCursorToken ||
            (requestedKind < 0 && BuilderKind < 0) || (requestedKind >= 0 && now >= replyUntil) ||
            !ReferenceEquals(selectionCursorPlan, SkateHeld.PausedPlanner(SkateRig.Local)) ||
            now >= selectionCursorUntil || selectionCursorPolls >= 2)
        { ClearSelectionCursor(); return false; }
        selectionCursorPolls++;
        BlocksControls = true;
        return true;
    }

    public static int Input(int padDown, float now, Keyboard keyboard, Mouse mouse, bool uiBlocked)
    {
        try { return InputFrame(padDown, now, keyboard, mouse, uiBlocked); }
        catch (Exception e)
        {
            switchingToRust = false;
            ForgetRustWheel();
            bool ready = NativeMode(now);
            Close(ready);
            Said = "Skate wheel input unavailable. Use F1: skate.builder <piece>, or skate.builder off.";
            SaidAt = now;
            Out.Say("PIECES wheel input unavailable: " + e.GetType().Name);
            return padDown;
        }
    }

    private static int InputFrame(int padDown, float now, Keyboard keyboard, Mouse mouse, bool uiBlocked)
    {
        BlocksControls = false; RustModeHint = false;
        bool p = keyboard != null && keyboard.pKey.isPressed;
        bool back = SkatePad.Shared && (SkatePad.Buttons & MenuButton) != 0;
        bool right = mouse != null && mouse.rightButton.isPressed;
        bool trigger = SkatePad.Shared && SkatePad.RightTrigger > SkatePad.Pulled;
        bool triggerPressed = trigger && !wasPlaceTrigger;
        wasPlaceTrigger = trigger;
        if (!inputPrimed) { wasP = p; wasBack = back; wasRight = right; inputPrimed = true; }
        if (!right && !back) cancelledHold = false;
        bool opened = false;
        bool shift = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        bool switchPressed = (padDown & SkatePad.X) != 0 || (shift && p && !wasP);
        if (rustBuilding && (!ReferenceEquals(modePlayer, SkateRig.Local) || !ReferenceEquals(modeBody, SkateRig.Body) || modeAwakeAt != SkateRig.AwakeAt))
            ForgetRustWheel();
        bool offboard = !SkateRide.On && SkateRig.Ready && SkateRig.Local != null && Application.isFocused;
        if (rustBuilding && offboard && !SkateHeld.HasPlanner(SkateRig.Local)) ForgetRustWheel();
        rustWheelOpen = rustBuilding && offboard && SkateHeld.RustWheelOpen(SkateRig.Local);
        RustModeHint = rustBuilding && offboard && (!uiBlocked || rustWheelOpen);
        switchPressed |= ModeButtonInput(now, mouse);
        bool cancelKey = (padDown & SkatePad.B) != 0 || (keyboard != null && (keyboard.escapeKey.wasPressedThisFrame || keyboard.f1Key.wasPressedThisFrame || keyboard.tabKey.wasPressedThisFrame));
        bool selectionSettling = SelectionCursorSettling(now, uiBlocked, cancelKey);
        if (openingSkate)
        {
            BlocksControls = true;
            bool cancelOpening = !Application.isFocused || SkateRide.On || !SkateRig.Ready ||
                !ReferenceEquals(openingPlayer, SkateRig.Local) || !ReferenceEquals(openingBody, SkateRig.Body) || openingAwakeAt != SkateRig.AwakeAt ||
                cancelKey || (openingEntry == Entry.MouseHold && !right) || (openingEntry == Entry.ControllerHold && !back);
            string why;
            bool closed = !cancelOpening && SkateHeld.RustWheelClosed(SkateRig.Local, out why);
            if (cancelOpening || now >= openingUntil)
            {
                bool ready = NativeMode(now); Close(ready);
                Said = cancelOpening ? "Build-mode switch cancelled." : "Rust's wheel did not release focus. Close other screens, then try again."; SaidAt = now;
            }
            else if (closed) { var from = openingEntry; openingSkate = false; OpenSkateWheel(from, now); }
            wasP = p; wasBack = back; wasRight = right;
            return 0;
        }
        // Only a proven own native radial may surrender an otherwise blocked cursor to us.
        if (rustBuilding && switchPressed && !cancelKey && offboard)
        {
            BlocksControls = true; cancelledHold = true;
            Begin(Entry.Keyboard, now, uiBlocked);
            wasP = p; wasBack = back; wasRight = right;
            return 0;
        }
        bool canOpen = !uiBlocked && !SkateRide.On && SkateRig.Ready && SkateRig.Local != null && Application.isFocused;
        if (!canOpen || (Open && (!ReferenceEquals(menuPlayer, SkateRig.Local) || !ReferenceEquals(menuBody, SkateRig.Body) || menuAwakeAt != SkateRig.AwakeAt)))
        {
            if (!selectionSettling)
            {
                bool ready = NativeMode(now);
                if (Open) Close(ready);
            }
            wasP = p; wasBack = back; wasRight = right;
            return selectionSettling ? 0 : padDown;
        }
        bool wantsOpen = (right && !wasRight) || (p && !wasP) || (back && !wasBack);
        if (rustBuilding)
        {
            // The ordinary planner receives its usual hold/release input. No synthetic native opener.
            stanceSince = -1f;
            wasP = p; wasBack = back; wasRight = right;
            return padDown;
        }
        if (!Open)
        {
            bool planner = wantsOpen && SkateHeld.HasPlanner(SkateRig.Local);
            if (requestedKind >= 0 || offPending)
            {
                if (!SkateHeld.HasPlanner(SkateRig.Local))
                {
                    NativeMode(now);
                    wasP = p; wasBack = back; wasRight = right;
                    return padDown;
                }
                bool cancelPending = (padDown & SkatePad.B) != 0 || (keyboard != null && keyboard.escapeKey.wasPressedThisFrame);
                if (cancelPending) NativeMode(now);
                if (wantsOpen) { Said = "Waiting for the server reply..."; SaidAt = now; }
                wasP = p; wasBack = back; wasRight = right;
                return padDown;
            }
            if (right && !wasRight && shift && planner)
            {
                SwitchToRust(now, true);
                wasP = p; wasBack = back; wasRight = right;
                return 0;
            }
            if (!opened)
            {
                if (p && !wasP) opened = Begin(Entry.Keyboard, now);
                else if (back && !wasBack && !cancelledHold) opened = Begin(Entry.ControllerHold, now);
                else if (right && !wasRight && !cancelledHold && planner) opened = Begin(Entry.MouseHold, now);
            }
        }
        if (Open)
        {
            BlocksControls = true;
            bool cancel = (padDown & SkatePad.B) != 0 || (keyboard != null &&
                (keyboard.escapeKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame || keyboard.f1Key.wasPressedThisFrame ||
                keyboard.tabKey.wasPressedThisFrame || (!opened && p && !wasP && !shift)));
            if (cancel) { switchingToRust = false; cancelledHold = true; bool ready = NativeMode(now); Close(ready); }
            else if (switchPressed)
            {
                cancelledHold = true;
                SwitchToRust(now);
                wasP = p; wasBack = back; wasRight = right;
                return 0;
            }
            else if (!switchingToRust && !modeButtonConsumes)
            {
                SkateRide.StanceHold(ref stanceSince, (SkatePad.Buttons & SkatePad.Y) != 0, now);
                if (SkatePad.Shared && (SkatePad.RightX * SkatePad.RightX + SkatePad.RightY * SkatePad.RightY > 0.04f || stickChosen))
                {
                    stickChosen = true;
                    Hover = SkateWheelChoice.Sector(SkatePad.RightX, SkatePad.RightY, 0.35);
                    pointer = new Vector2(SkatePad.RightX, -SkatePad.RightY) * SkateBuildWheel.PointerRadius;
                }
                else if (mouse != null)
                {
                    var delta = mouse.delta.ReadValue();
                    pointer += new Vector2(delta.x, -delta.y);
                    pointer = Vector2.ClampMagnitude(pointer, SkateBuildWheel.PointerRadius);
                    Hover = SkateWheelChoice.Sector(pointer.x, -pointer.y, SkateBuildWheel.PointerRadius * 0.24);
                }
                if ((padDown & (SkatePad.DpadRight | SkatePad.DpadDown)) != 0 || (keyboard != null && keyboard.rightArrowKey.wasPressedThisFrame))
                { Hover = (Chosen + 1) % Names.Length; pointer = SkateBuildWheel.PointerFor(Hover); stickChosen = false; }
                if ((padDown & (SkatePad.DpadLeft | SkatePad.DpadUp)) != 0 || (keyboard != null && keyboard.leftArrowKey.wasPressedThisFrame))
                { Hover = (Chosen + Names.Length - 1) % Names.Length; pointer = SkateBuildWheel.PointerFor(Hover); stickChosen = false; }
                if (Hover >= 0) Chosen = Hover;
                bool released = (entry == Entry.MouseHold && wasRight && !right) || (entry == Entry.ControllerHold && wasBack && !back);
                bool confirm = (padDown & SkatePad.A) != 0 || (keyboard != null &&
                    (keyboard.enterKey.wasPressedThisFrame || keyboard.oKey.wasPressedThisFrame));
                if (released || confirm)
                {
                    bool select = Hover >= 0;
                    bool pad = entry == Entry.ControllerHold;
                    if (select) Place(now, pad);
                    else { bool ready = NativeMode(now); Close(ready); }
                }
            }
        }
        if (!Open && BuilderKind >= 0)
        {
            if (!SkateHeld.HasPlanner(SkateRig.Local)) NativeMode(now);
            else
            {
                if ((keyboard != null && keyboard.rKey.wasPressedThisFrame) || (padDown & SkatePad.RightBumper) != 0)
                    builderTurn = (builderTurn + 1) % 4;
                if ((padDown & SkatePad.LeftBumper) != 0) builderTurn = (builderTurn + 3) % 4;
                bool click = mouse != null && mouse.leftButton.wasPressedThisFrame;
                if (click || triggerPressed)
                {
                    BlocksControls = true;
                    SaidAt = now;
                    RequestPlacement(now);
                    builderNoticeUntil = placementUnknown ? float.PositiveInfinity : now + 2.5f;
                }
                padDown &= ~(SkatePad.LeftBumper | SkatePad.RightBumper);
            }
        }
        wasP = p; wasBack = back; wasRight = right;
        return BlocksControls ? 0 : padDown;
    }

    private static void RequestPlacement(float now)
    {
        if (placementToken.Length != 0)
            Said = placementUnknown ? "Placement unconfirmed. Reopen the wheel, then choose Rust building before retrying."
                : "Waiting for placement confirmation...";
        else
        {
            placementToken = Guid.NewGuid().ToString("N");
            placementUntil = now + 5f; placementUnknown = false;
            placementPlayer = SkateRig.Local; placementBody = SkateRig.Body; placementAwakeAt = SkateRig.AwakeAt; placementKind = BuilderKind;
            if (SkateConsole.Send("skate.builder place " + builderTurn.ToString(CultureInfo.InvariantCulture) + " " + placementToken))
                Said = "Placement requested. The server checks the site and 10 Wood.";
            else
            {
                placementUnknown = true; placementUntil = now;
                Said = "Placement could not be confirmed. Reopen the wheel, then choose Rust building before retrying.";
            }
        }
    }

    private static bool Begin(Entry from, float now, bool nativeUiExpected = false)
    {
        if (!SkateBuildWheel.Available)
        { Said = "The skate wheel cannot be drawn here. Use F1: skate.builder <piece>."; SaidAt = now; return false; }
        string why; bool waiting;
        // Close only the ordinary plan's own radial, before taking our cursor snapshot.
        if (!SkateHeld.CloseRustWheel(SkateRig.Local, nativeUiExpected, out waiting, out why))
        {
            if (waiting && SkateHeld.PausePlanner(SkateRig.Local, out why))
            {
                openingSkate = true; openingEntry = from; openingPlayer = SkateRig.Local; openingBody = SkateRig.Body; openingAwakeAt = SkateRig.AwakeAt;
                openingUntil = now + 3f; BlocksControls = true;
                Said = "Closing the Rust wheel before Skate building..."; SaidAt = now;
            }
            else { Said = "Could not close the Rust build wheel. " + why; SaidAt = now; }
            return false;
        }
        return OpenSkateWheel(from, now);
    }

    private static bool OpenSkateWheel(Entry from, float now)
    {
        string why;
        if (!SkateHeld.PausePlanner(SkateRig.Local, out why))
        {
            Said = "Hold your building plan to open the skate wheel. " + why;
            SaidAt = now;
            return false;
        }
        if (!SkateHeld.AcquireSkateCursor(SkateRig.Local, out why))
        {
            SkateHeld.ReleaseSkateCursor();
            if (!builderRequested && requestedKind < 0 && !offPending) SkateHeld.ResumePlanner();
            Said = "The skate wheel cannot take cursor focus. " + why; SaidAt = now; return false;
        }
        OwnsCursor = true;
        openingSkate = false; openingPlayer = openingBody = null; openingAwakeAt = openingUntil = 0;
        Open = true; entry = from; menuPlayer = SkateRig.Local; menuBody = SkateRig.Body; menuAwakeAt = SkateRig.AwakeAt;
        rustBuilding = rustWheelOpen = switchingToRust = RustModeHint = false; modePlayer = modeBody = null; modeAwakeAt = 0;
        Hover = -1; pointer = Vector2.zero; Status = ""; stickChosen = false;
        ListenForReplies();
        HeldClear();
        return true;
    }

    private static void SwitchToRust(float now, bool fromClosed = false)
    {
        switchingToRust = true; switchingFromClosed = fromClosed;
        if (fromClosed) { modePlayer = SkateRig.Local; modeBody = SkateRig.Body; modeAwakeAt = SkateRig.AwakeAt; }
        if (NativeMode(now)) FinishRustSwitch(now);
        else Status = offPending ? "Switching to Rust building. Waiting for the server..." : Said;
    }

    private static void FinishRustSwitch(float now)
    {
        // A reply arriving after focus/life/plan loss cannot open UI over another menu.
        bool sameLife = switchingFromClosed
            ? ReferenceEquals(modePlayer, SkateRig.Local) && ReferenceEquals(modeBody, SkateRig.Body) && modeAwakeAt == SkateRig.AwakeAt
            : Open && OwnsCursor && ReferenceEquals(menuPlayer, SkateRig.Local) && ReferenceEquals(menuBody, SkateRig.Body) && menuAwakeAt == SkateRig.AwakeAt;
        if (!switchingToRust || !sameLife || !Application.isFocused || !SkateRig.Ready || !SkateHeld.HasPlanner(SkateRig.Local))
        {
            Close(!builderRequested && requestedKind < 0 && !offPending);
            Said = "Build-mode switch cancelled."; SaidAt = now; return;
        }
        switchingToRust = false;
        Close(false);
        SkateHeld.ResumePlanner();
        rustBuilding = true; rustWheelOpen = false;
        modePlayer = SkateRig.Local; modeBody = SkateRig.Body; modeAwakeAt = SkateRig.AwakeAt;
        Said = "Rust building. Release, then hold Back / right click for the Rust wheel. X / Shift + P: Skate building."; SaidAt = now;
    }

    private static void ForgetRustWheel()
    {
        // Rust mode yielded ownership to the native planner; it is not our UI lease to close.
        rustBuilding = rustWheelOpen = RustModeHint = false; modePlayer = modeBody = null; modeAwakeAt = 0;
        ClearModeButton();
    }

    private static bool NativeMode(float now)
    {
        ClearSelectionCursor();
        if (offPending) return false;
        if (!builderRequested) return true;
        SaidAt = now;
        if (!SkateConsole.Send("skate.builder off" + (selectionToken.Length == 0 ? "" : " " + selectionToken)))
        { Said = Status = "Could not request Rust building. In F1 type: skate.builder off"; return false; }
        BuilderKind = requestedKind = -1; offPending = true; offUntil = now + 5f;
        Said = "Requested Rust building. Waiting for the server reply...";
        return false;
    }

    private static void ListenForReplies()
    {
        if (replyListener != null || replyListenerFailed) return;
        try
        {
            replyListener = (Action<string, string, LogType>)BuilderReply;
            Application.add_logMessageReceived(replyListener);
        }
        catch (Exception e)
        { replyListener = null; replyListenerFailed = true; Out.Say("PIECES server reply listener unavailable: " + e.GetType().Name); }
    }

    private static void BuilderReply(string message, string trace, LogType type)
    {
        const string prefix = "[SKATE-BUILDER] ";
        if (message == null || !message.StartsWith(prefix, StringComparison.Ordinal)) return;
        string reply = message.Substring(prefix.Length).Trim();
        bool placementReply = reply.StartsWith("placed ", StringComparison.Ordinal) ||
            reply.StartsWith("placement-refused ", StringComparison.Ordinal) || reply.StartsWith("payment-review ", StringComparison.Ordinal);
        if (placementReply)
        {
            string suffix = " [request=" + placementToken + "]";
            if (placementToken.Length == 0 || !ReferenceEquals(placementPlayer, SkateRig.Local) ||
                !ReferenceEquals(placementBody, SkateRig.Body) || placementAwakeAt != SkateRig.AwakeAt ||
                placementKind != BuilderKind ||
                !reply.EndsWith(suffix, StringComparison.Ordinal)) return;
            reply = reply.Substring(0, reply.Length - suffix.Length);
        }
        bool selectionReply = reply == "off" || reply.StartsWith("off [selection=", StringComparison.Ordinal) ||
            reply.StartsWith("selected ", StringComparison.Ordinal) || reply.StartsWith("refused ", StringComparison.Ordinal);
        if (selectionReply)
        {
            string suffix = " [selection=" + selectionToken + "]";
            if (selectionToken.Length == 0 || !ReferenceEquals(selectionPlayer, SkateRig.Local) ||
                !ReferenceEquals(selectionBody, SkateRig.Body) || selectionAwakeAt != SkateRig.AwakeAt ||
                !reply.EndsWith(suffix, StringComparison.Ordinal)) return;
            reply = reply.Substring(0, reply.Length - suffix.Length);
        }
        if (reply == "off")
        {
            BuilderKind = requestedKind = -1; builderRequested = offPending = false; ClearPlacement(); ClearSelectionReceipt(); Said = "Rust building";
            if (switchingToRust) FinishRustSwitch(Time.realtimeSinceStartup);
            else if (!Open) SkateHeld.ResumePlanner();
        }
        else if (reply.StartsWith("selected ", StringComparison.Ordinal))
        {
            var name = reply.Substring(9).Trim();
            if (offPending || requestedKind < 0 || requestedKind >= Names.Length || name != Names[requestedKind]) return;
            BuilderKind = requestedKind; requestedKind = -1;
            Picked = BuilderKind;
            builderTurn = 0; ClearPlacement();
            Said = SkateBuildWheel.Labels[BuilderKind] + " selected. 10 Wood per successful piece. " + BuilderControls;
        }
        else if (reply.StartsWith("refused ", StringComparison.Ordinal))
        {
            if (offPending) return;
            BuilderKind = requestedKind = -1;
            builderRequested = false; ClearPlacement(); ClearSelectionReceipt(); if (!Open) SkateHeld.ResumePlanner();
            Said = reply.Substring(8); if (Said.Length > 180) Said = Said.Substring(0, 180);
        }
        else if (reply.StartsWith("placed ", StringComparison.Ordinal) && BuilderKind >= 0 &&
            reply == "placed " + Names[BuilderKind] + " 10 Wood")
        { ClearPlacement(); Placed++; Said = "Placed " + SkateBuildWheel.Labels[BuilderKind] + " for 10 Wood. " + BuilderControls; }
        else if (reply.StartsWith("placement-refused ", StringComparison.Ordinal) && BuilderKind >= 0)
        { ClearPlacement(); Said = reply.Substring(18); if (Said.Length > 180) Said = Said.Substring(0, 180); }
        else if (reply.StartsWith("payment-review ", StringComparison.Ordinal) && BuilderKind >= 0)
        { placementUnknown = true; Said = "Park saved; payment needs operator review. Do not retry. " + reply.Substring(15); }
        else return;
        SaidAt = Time.realtimeSinceStartup;
        builderNoticeUntil = placementUnknown ? float.PositiveInfinity : SaidAt + 2.5f;
    }

    private static void ClearSelectionReceipt()
    { ClearSelectionCursor(); selectionToken = ""; selectionPlayer = selectionBody = null; selectionAwakeAt = 0; }

    private static void ClearPlacement()
    { placementToken = ""; placementUntil = 0; placementUnknown = false; placementPlayer = placementBody = null; placementAwakeAt = 0; placementKind = -1; }

    private static void Close(bool resumePlanner = true)
    {
        ClearSelectionCursor();
        // The native cursor manager settles the request after our component stops updating.
        // Do not restore global cursor flags over another menu's current request.
        SkateHeld.ReleaseSkateCursor();
        OwnsCursor = false;
        Open = false; entry = Entry.None; menuPlayer = menuBody = null; menuAwakeAt = 0; Hover = -1; switchingToRust = switchingFromClosed = RustModeHint = false;
        openingSkate = false; openingEntry = Entry.None; openingPlayer = openingBody = null; openingAwakeAt = openingUntil = 0;
        ClearModeButton();
        stanceSince = -1f;
        if (resumePlanner) SkateHeld.ResumePlanner();
        SkateSupportFootprint.Hide();
        SkateParks.PreviewClear(); previewKind = -1; previewYaw = 9999f;
    }

    private static void CheckPlacement(float now)
    {
        if (placementToken.Length != 0 && !placementUnknown && now >= placementUntil)
        {
            placementUnknown = true;
            Said = "Placement unconfirmed. No automatic retry. Reopen the wheel, then choose Rust building before retrying.";
            SaidAt = now; builderNoticeUntil = float.PositiveInfinity;
        }
    }

    public static void Frame(float now)
    {
        CheckPlacement(now);
        if (requestedKind >= 0 && now >= replyUntil)
        {
            NativeMode(now);
            Said = offPending ? "Server reply not received. Requested Rust building instead."
                : "Server reply not received. In F1 type: skate.builder off";
            SaidAt = now; replyUntil = float.PositiveInfinity;
        }
        if (offPending && now >= offUntil)
        {
            Said = "Rust-building reply not received. In F1 type: skate.builder off, or re-equip your building plan.";
            SaidAt = now; offUntil = float.PositiveInfinity;
            if (Open && switchingToRust) Status = Said;
        }
        if (Open)
        {
            // A native input callback can open its wheel later in the frame that began ours.
            // Never paint over it; close only that same owned menu before taking focus again.
            if (!CanDrawWheel)
            {
                bool sameMenu = !SkateRide.On && SkateRig.Ready && Application.isFocused && ReferenceEquals(menuPlayer, SkateRig.Local) &&
                    ReferenceEquals(menuBody, SkateRig.Body) && menuAwakeAt == SkateRig.AwakeAt;
                if (sameMenu && SkateHeld.RustWheelOpen(SkateRig.Local)) { var from = entry; Close(false); Begin(from, now, true); }
                else
                {
                    Out.Say("PIECES build wheel closed: " + (sameMenu ? SkateHeld.RustWheelGate : "player-or-focus-changed"));
                    bool ready = NativeMode(now); Close(ready);
                    Said = "The Rust build screen changed. Close other screens, then reopen the wheel."; SaidAt = now;
                }
            }
            AimOk = false; HeldClear(); return;
        }
        HeldFrame(now);
    }

    public static int HeldKind = -1;
    // Whether the ghost now sits snapped on a standing foundation's top.
    public static bool OnFoundation;
    // Whether the whole support was found, and if not, why not.
    public static bool Valid; public static string GhostWhy = "";
    private static string lastWhy = ""; private static RaycastHit aimHit;
    private static void HeldFrame(float now)
    {
        var was = HeldKind;
        var read = !SkateRide.On && SkateRig.Local != null && SkateHeld.Read(SkateRig.Local);
        HeldKind = read ? SkateHeld.HeldKind() : -1;
        if (!SkateRide.On && BuilderKind >= 0 && SkateHeld.HasPlanner(SkateRig.Local)) HeldKind = BuilderKind;
        else if (!SkateRide.On && BuilderKind >= 0)
        {
            // Request off before forgetting the selection; clearing it here bypassed Input's cleanup.
            bool ready = NativeMode(now);
            Close(ready);
        }
        if (HeldKind != was) Out.Say("PIECES held: " + (HeldKind < 0 ? "no park item" : SkateHeld.ItemNames[HeldKind]) + " (item " + SkateHeld.Shortname + " '" + SkateHeld.Name + "' marker " + SkateHeld.Marker.ToString("X") + ")");
        AimOk = false;
        if (HeldKind < 0) { HeldClear(); return; }
        if (HeldKind == (int)SkateParks.Kind.HalfWallKicker) { HeldWallFrame(now); return; }
        try
        {
            var cam = Camera.main;
            RaycastHit hit;
            // No ground in reach (sky, a steep wall, too far): no ghost left at an old spot.
            if (cam == null || !Physics.Raycast(cam.transform.position, cam.transform.forward, out hit, AimReach, SkateRide.GroundMask, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.5f) { HeldClear(); return; }
            AimOk = true; Aim = hit.point; Yaw = cam.transform.eulerAngles.y;
            aimHit = hit;
        }
        catch (Exception) { HeldClear(); return; }
        // Snapped and seated by the server's own rules (#476): onto a standing foundation, or beside a
        // park floor, turned by the planner's R; Valid only when every support cell is found.
        Vector3 origin; float yaw; bool onHost; string why; SkateParkTransform footprint;
        Valid = SkateParks.Ghost((SkateParks.Kind)HeldKind, aimHit, Yaw, BuilderKind >= 0 ? builderTurn * 90f : SkateHeld.PlannerTurn(SkateRig.Local), out origin, out yaw, out onHost, out why, out footprint, BuilderKind >= 0);
        OnFoundation = onHost; Yaw = yaw; GhostWhy = why;
        SkateSupportFootprint.Show((SkateParkKind)HeldKind, footprint, Valid);
        if (HeldKind != previewKind) { SkateParks.PreviewAt((SkateParks.Kind)HeldKind, origin, Yaw, false); previewKind = HeldKind; previewAt = origin; previewYaw = Yaw; }
        else if (Vector3.Distance(previewAt, origin) > 0.01f || Math.Abs(previewYaw - Yaw) > 0.01f) { SkateParks.PreviewMove(origin, Yaw); previewAt = origin; previewYaw = Yaw; }
        if (now >= builderNoticeUntil)
        {
            Said = SkateHeld.ItemNames[HeldKind] + (Valid ? ": client support fits; server still verifies placement and 10 Wood" : ": support unverified: " + GhostWhy) + "  |  " + (BuilderKind >= 0 ? BuilderControls : "Shift + right click: Rust building"); SaidAt = now;
        }
        if (GhostWhy != lastWhy) { lastWhy = GhostWhy; Out.Say("PIECES ghost " + SkateHeld.ItemNames[HeldKind] + (Valid ? " fits" + (OnFoundation ? " on a foundation" : "") : ": " + GhostWhy)); }
    }

    // The half-wall kicker goes against a wall, not on the ground: its ghost stands where the
    // server would put it on the half wall aimed at, and nowhere else.
    private static void HeldWallFrame(float now)
    {
        SkateSupportFootprint.Hide(); // A half-wall attachment has no ground-cell mask.
        var origin = Vector3.zero; var yaw = 0f; var why = "no camera"; var ok = false;
        try
        {
            var cam = Camera.main;
            if (cam != null && SkateRig.LocalT != null) ok = SkateParks.HalfWallAim(cam.transform.position, cam.transform.forward, SkateRig.LocalT.position, out origin, out yaw, out why);
        }
        catch (Exception e) { ok = false; why = e.GetType().Name; }
        SaidAt = now;
        if (!ok) { HeldClear(); Said = SkateHeld.ItemNames[HeldKind] + ": " + why; return; }
        AimOk = true; Aim = origin; Yaw = yaw;
        if (HeldKind != previewKind) { SkateParks.PreviewAt((SkateParks.Kind)HeldKind, origin, yaw, false); previewKind = HeldKind; previewAt = origin; previewYaw = yaw; }
        else if (Vector3.Distance(previewAt, origin) > 0.01f || Math.Abs(previewYaw - yaw) > 0.01f) { SkateParks.PreviewMove(origin, yaw); previewAt = origin; previewYaw = yaw; }
        Said = SkateHeld.ItemNames[HeldKind] + (BuilderKind >= 0 ? ": preview against this half wall; 10 Wood. " + BuilderControls
            : ": against this half wall (left click with the square foundation)");
    }

    private static void HeldClear() { SkateSupportFootprint.Hide(); if (previewKind >= 0) { SkateParks.PreviewClear(); previewKind = -1; } }

    // The piece last taken with the picker; -1 before any.
    public static int Picked = -1;

    public static void Place(float now, bool pad)
    {
        SaidAt = now;
        if (requestedKind >= 0 || offPending) { Said = Status = "Waiting for the server reply..."; return; }
        if (SkateRig.Local == null || !SkateHeld.HasPlanner(SkateRig.Local))
        { Said = Status = "Hold your ordinary building plan first."; return; }
        selectionToken = Guid.NewGuid().ToString("N");
        selectionPlayer = SkateRig.Local; selectionBody = SkateRig.Body; selectionAwakeAt = SkateRig.AwakeAt;
        var plan = SkateHeld.PausedPlanner(SkateRig.Local);
        requestedKind = Chosen; replyUntil = now + 5f; BuilderKind = -1; Picked = -1; builderRequested = true; builderPad = pad;
        string command = "skate.builder " + Names[Chosen] + " " + selectionToken;
        bool sent = SkateConsole.Send(command);
        Close(false);
        if (sent && plan != null && builderRequested && !offPending && selectionToken.Length != 0)
        {
            // A selection ACK does not extend this independent, already-running cursor bound.
            selectionCursorPlan = plan; selectionCursorToken = selectionToken;
            selectionCursorUntil = Time.realtimeSinceStartup + 0.5f;
        }
        Said = sent ? "Requested " + SkateBuildWheel.Labels[Chosen] + ". Server checks placement and 10 Wood. " + BuilderControls
            : "Selection unconfirmed. Waiting for a server reply before another request.";
    }


    // The piece asked for by name alone, the way a server with ShortcutSkate 0.3.5 takes it: the
    // next square foundation becomes the piece. Sent when sending works; shown to type otherwise.
    public static void Ask(string name, float now)
    {
        var line = "skate.piece " + name;
        SaidAt = now;
        if (!SkateConsole.CanSend) { Said = Manual(name); return; }
        if (!SkateConsole.Send(line)) { Said = "Not sent: " + SkateConsole.SendState; return; }
        Sent++;
        if (name == "cancel") { Asked = ""; Said = "Sent to the server: " + line; return; }
        Asked = name; AskedAt = now;
        Said = "Sent to the server: " + line + "  -  now place one square foundation with the building plan within " + (int)PlaceSeconds + " s (admin only; the server answers in F1)";
    }

    // What to type in F1 when the plugin cannot send: the 0.3.5 route, and the 0.3.6 one.
    public static string Manual(string name)
    {
        if (name == "cancel") return "Cannot send to the server (" + SkateConsole.SendState + "). In F1 type: skate.piece cancel";
        return "Cannot send to the server (" + SkateConsole.SendState + "). In F1 type: skate.piece " + name + "  then place one square foundation within " + (int)PlaceSeconds + " s (ShortcutSkate 0.3.5); or skate.place " + name + " (0.3.6 or later)";
    }

    // What the screen shows: the menu while open, the last answer for a few seconds, the pending piece.
    public static string Line(float now)
    {
        if (Open)
        {
            return "Skate park wheel";
        }
        if (now - SaidAt < ShownSeconds) return Said;
        if (Asked != "" && now - AskedAt < PlaceSeconds) return "Park piece " + Asked + ": place a square foundation (" + (int)(PlaceSeconds - (now - AskedAt)) + " s left)";
        return "";
    }

    public static Vector2 Pointer { get { return pointer; } }
    public static void CancelFocus()
    {
        switchingToRust = false;
        bool ready = NativeMode(Time.realtimeSinceStartup);
        Close(ready);
        inputPrimed = false;
    }
    public static void Reset()
    {
        switchingToRust = false;
        ForgetRustWheel();
        if (builderRequested) NativeMode(Time.realtimeSinceStartup);
        // Player/rig teardown cannot retain a disabled component after its input driver is gone.
        Close(); inputPrimed = false; cancelledHold = BlocksControls = false; BuilderKind = requestedKind = -1;
        SkateSupportFootprint.Reset();
        builderRequested = offPending = false;
        Picked = -1; HeldKind = -1; Asked = Said = Status = ""; SaidAt = AskedAt = -99f; Chosen = 0;
        Sent = Placed = 0; Turn = 0f; AimOk = false;
        builderTurn = 0; wasPlaceTrigger = builderPad = false; ClearPlacement(); ClearSelectionReceipt(); builderNoticeUntil = 0;
    }
    public static void Shutdown()
    {
        Reset();
        SkateHeld.ReleaseSkateCursor(true);
        if (replyListener != null)
        {
            try { Application.remove_logMessageReceived(replyListener); } catch (Exception) { }
            replyListener = null;
        }
    }
}
