using System;
using System.Globalization;
using Shortcut.RustMod;
using UnityEngine;
using UnityEngine.InputSystem;

public static class SkatePieces
{
    public static readonly string[] Names = { "bank", "kicker", "quarterpipe", "ledge-low", "ledge-high", "long-ledge", "funbox", "halfpipe", "mini-quarterpipe", "large-quarterpipe", "half-wall-kicker", "mega-halfpipe" };
    public const int MenuButton = SkatePad.Back;
    public const float ShownSeconds = 6f, PlaceSeconds = 120f, AimReach = 40f, TurnStep = 15f, ClearGround = 0.04f;

    public static bool Open;
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
    private static bool wasP, wasBack, wasRight, inputPrimed, cancelledHold, stickChosen;
    private static object menuPlayer;
    private static Vector2 pointer;
    public static int Hover = -1;
    public static bool BlocksControls;
    public static bool OwnsCursor;
    private static CursorLockMode cursorBefore;
    private static bool cursorVisibleBefore;
    public static int BuilderKind = -1;
    private static int requestedKind = -1;
    private static float replyUntil;
    private static Application.LogCallback replyListener;
    private static bool replyListenerFailed;
    private static bool builderRequested;
    private static float builderNoticeUntil;
    private static int builderTurn;
    private static bool wasPlaceTrigger;
    private static bool offPending;
    private static float offUntil;
    private static int previewKind = -1;
    // The kind whose ghost is drawn now; -1 for none.
    public static int GhostKind { get { return previewKind; } }
    private static float previewYaw = 9999f; private static Vector3 previewAt;

    public static int Input(int padDown, float now, Keyboard keyboard, Mouse mouse, bool uiBlocked)
    {
        try { return InputFrame(padDown, now, keyboard, mouse, uiBlocked); }
        catch (Exception e)
        {
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
        BlocksControls = false;
        bool p = keyboard != null && keyboard.pKey.isPressed;
        bool back = SkatePad.Shared && (SkatePad.Buttons & MenuButton) != 0;
        bool right = mouse != null && mouse.rightButton.isPressed;
        bool trigger = SkatePad.Shared && SkatePad.RightTrigger > SkatePad.Pulled;
        bool triggerPressed = trigger && !wasPlaceTrigger;
        wasPlaceTrigger = trigger;
        if (!inputPrimed) { wasP = p; wasBack = back; wasRight = right; inputPrimed = true; }
        if (!right && !back) cancelledHold = false;
        bool opened = false;
        bool canOpen = !uiBlocked && !SkateRide.On && SkateRig.Ready && SkateRig.Local != null;
        if (!canOpen || (Open && !ReferenceEquals(menuPlayer, SkateRig.Local)))
        {
            bool ready = NativeMode(now);
            if (Open) Close(ready);
            wasP = p; wasBack = back; wasRight = right;
            return padDown;
        }
        if (!Open)
        {
            bool wantsOpen = (right && !wasRight) || (p && !wasP) || (back && !wasBack);
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
            bool native = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            if (right && !wasRight && native && planner)
            {
                if (NativeMode(now))
                { wasP = p; wasBack = back; wasRight = right; return padDown; }
                wasP = p; wasBack = back; wasRight = right;
                return padDown;
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
                keyboard.tabKey.wasPressedThisFrame || (!opened && p && !wasP)));
            if (cancel) { cancelledHold = true; bool ready = NativeMode(now); Close(ready); }
            else
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
                if ((padDown & (SkatePad.DpadRight | SkatePad.DpadDown)) != 0 || (keyboard != null && keyboard.eKey.wasPressedThisFrame))
                { Hover = (Chosen + 1) % Names.Length; pointer = SkateBuildWheel.PointerFor(Hover); stickChosen = false; }
                if ((padDown & (SkatePad.DpadLeft | SkatePad.DpadUp)) != 0 || (keyboard != null && keyboard.qKey.wasPressedThisFrame))
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
                    Said = SkateConsole.Send("skate.builder place " + builderTurn.ToString(CultureInfo.InvariantCulture))
                        ? "Placement requested. The server checks the site and 10 Wood."
                        : "Placement not sent. Check your connection, then click again.";
                    builderNoticeUntil = now + 2f;
                }
                padDown &= ~(SkatePad.LeftBumper | SkatePad.RightBumper);
            }
        }
        wasP = p; wasBack = back; wasRight = right;
        return BlocksControls ? 0 : padDown;
    }

    private static bool Begin(Entry from, float now)
    {
        if (!SkateBuildWheel.Available)
        { Said = "The skate wheel cannot be drawn here. Use F1: skate.builder <piece>."; SaidAt = now; return false; }
        string why;
        if (!SkateHeld.PausePlanner(SkateRig.Local, out why))
        {
            Said = "Hold your building plan to open the skate wheel. " + why;
            SaidAt = now;
            return false;
        }
        try
        {
            cursorBefore = Cursor.lockState; cursorVisibleBefore = Cursor.visible;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = false; OwnsCursor = true;
        }
        catch (Exception)
        {
            if (!builderRequested && requestedKind < 0 && !offPending) SkateHeld.ResumePlanner();
            Said = "The skate wheel cannot take cursor focus."; SaidAt = now; return false;
        }
        Open = true; entry = from; menuPlayer = SkateRig.Local;
        Hover = -1; pointer = Vector2.zero; Status = ""; stickChosen = false;
        ListenForReplies();
        HeldClear();
        return true;
    }

    private static bool NativeMode(float now)
    {
        if (offPending) return false;
        if (!builderRequested) return true;
        SaidAt = now;
        if (!SkateConsole.Send("skate.builder off"))
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
        if (reply == "off")
        {
            if (!offPending && requestedKind >= 0) return;
            BuilderKind = requestedKind = -1; builderRequested = offPending = false; Said = "Rust building"; if (!Open) SkateHeld.ResumePlanner();
        }
        else if (reply.StartsWith("selected ", StringComparison.Ordinal))
        {
            var name = reply.Substring(9).Trim();
            if (offPending || requestedKind < 0 || requestedKind >= Names.Length || name != Names[requestedKind]) return;
            BuilderKind = requestedKind; requestedKind = -1;
            Picked = BuilderKind;
            builderTurn = 0;
            Said = SkateBuildWheel.Labels[BuilderKind] + " selected. 10 Wood per successful piece; left click places, R rotates.";
        }
        else if (reply.StartsWith("refused ", StringComparison.Ordinal))
        {
            if (offPending) return;
            BuilderKind = requestedKind = -1;
            builderRequested = false; if (!Open) SkateHeld.ResumePlanner();
            Said = reply.Substring(8); if (Said.Length > 180) Said = Said.Substring(0, 180);
        }
        else if (reply.StartsWith("placed ", StringComparison.Ordinal) && BuilderKind >= 0 &&
            reply == "placed " + Names[BuilderKind] + " 10 Wood")
        { Said = "Placed " + SkateBuildWheel.Labels[BuilderKind] + " for 10 Wood."; }
        else if (reply.StartsWith("placement-refused ", StringComparison.Ordinal) && BuilderKind >= 0)
        { Said = reply.Substring(18); if (Said.Length > 180) Said = Said.Substring(0, 180); }
        else return;
        SaidAt = Time.realtimeSinceStartup;
        builderNoticeUntil = SaidAt + 2.5f;
    }

    private static void Close(bool resumePlanner = true)
    {
        Open = false; entry = Entry.None; menuPlayer = null; Hover = -1;
        stanceSince = -1f;
        if (OwnsCursor)
        {
            OwnsCursor = false;
            try
            {
                if (Cursor.lockState == CursorLockMode.None && !Cursor.visible)
                { Cursor.lockState = cursorBefore; Cursor.visible = cursorVisibleBefore; }
            }
            catch (Exception e) { Out.Say("PIECES cursor restore unavailable: " + e.GetType().Name); }
        }
        if (resumePlanner) SkateHeld.ResumePlanner();
        SkateParks.PreviewClear(); previewKind = -1; previewYaw = 9999f;
    }

    public static void Frame(float now)
    {
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
        }
        if (Open) { AimOk = false; HeldClear(); return; }
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
        else if (!SkateRide.On && BuilderKind >= 0) BuilderKind = -1;
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
        Vector3 origin; float yaw; bool onHost; string why;
        Valid = SkateParks.Ghost((SkateParks.Kind)HeldKind, aimHit, Yaw, BuilderKind >= 0 ? builderTurn * 90f : SkateHeld.PlannerTurn(SkateRig.Local), out origin, out yaw, out onHost, out why, BuilderKind >= 0);
        OnFoundation = onHost; Yaw = yaw; GhostWhy = why;
        if (HeldKind != previewKind) { SkateParks.PreviewAt((SkateParks.Kind)HeldKind, origin, Yaw, false); previewKind = HeldKind; previewAt = origin; previewYaw = Yaw; }
        else if (Vector3.Distance(previewAt, origin) > 0.01f || Math.Abs(previewYaw - Yaw) > 0.01f) { SkateParks.PreviewMove(origin, Yaw); previewAt = origin; previewYaw = Yaw; }
        if (now >= builderNoticeUntil)
        {
            Said = SkateHeld.ItemNames[HeldKind] + (Valid ? ": preview fits here; server verifies placement and 10 Wood" : ": preview does not fit: " + GhostWhy) + "  |  Shift + right click: Rust building"; SaidAt = now;
        }
        if (GhostWhy != lastWhy) { lastWhy = GhostWhy; Out.Say("PIECES ghost " + SkateHeld.ItemNames[HeldKind] + (Valid ? " fits" + (OnFoundation ? " on a foundation" : "") : ": " + GhostWhy)); }
    }

    // The half-wall kicker goes against a wall, not on the ground: its ghost stands where the
    // server would put it on the half wall aimed at, and nowhere else.
    private static void HeldWallFrame(float now)
    {
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
        Said = SkateHeld.ItemNames[HeldKind] + (BuilderKind >= 0 ? ": preview against this half wall; left click places for 10 Wood"
            : ": against this half wall (left click with the square foundation)");
    }

    private static void HeldClear() { if (previewKind >= 0) { SkateParks.PreviewClear(); previewKind = -1; } }

    // The piece last taken with the picker; -1 before any.
    public static int Picked = -1;

    public static void Place(float now, bool pad)
    {
        SaidAt = now;
        if (requestedKind >= 0 || offPending) { Said = Status = "Waiting for the server reply..."; return; }
        if (SkateRig.Local == null || !SkateHeld.HasPlanner(SkateRig.Local))
        { Said = Status = "Hold your ordinary building plan first."; return; }
        string command = "skate.builder " + Names[Chosen];
        if (!SkateConsole.Send(command))
        { Said = Status = "Not selected. In F1 type: " + command; return; }
        requestedKind = Chosen; replyUntil = now + 5f; BuilderKind = -1; Picked = -1; builderRequested = true;
        Close(false);
        Said = "Requested " + SkateBuildWheel.Labels[Chosen] + ". Server checks placement and 10 Wood; left click places, R rotates.";
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
        bool ready = NativeMode(Time.realtimeSinceStartup);
        Close(ready);
        inputPrimed = false;
    }
    public static void Reset()
    {
        if (builderRequested) NativeMode(Time.realtimeSinceStartup);
        // Player/rig teardown cannot retain a disabled component after its input driver is gone.
        Close(); inputPrimed = false; cancelledHold = BlocksControls = false; BuilderKind = requestedKind = -1;
        builderRequested = offPending = false;
        Picked = -1; HeldKind = -1; Asked = Said = Status = ""; SaidAt = AskedAt = -99f; Chosen = 0;
        Sent = Placed = 0; Turn = 0f; AimOk = false;
        builderTurn = 0; wasPlaceTrigger = false;
    }
    public static void Shutdown()
    {
        Reset();
        if (replyListener != null)
        {
            try { Application.remove_logMessageReceived(replyListener); } catch (Exception) { }
            replyListener = null;
        }
    }
}
