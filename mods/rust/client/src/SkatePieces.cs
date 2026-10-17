// The park picker and the park items (owner's choice, 2026-10-13: "own build picker and hotbar
// items"). The server (Codex's ShortcutSkate 0.3.11) gives eight park items, building plans marked
// with their kind; one is placed with the game's own building controls as a square foundation
// (left click, R turns it) and the server builds the piece in its place. Nothing here sends a
// command: the client cannot (the console packet path is not found in this build and typed keys
// never reach the console). The picker only chooses a piece and puts that item in the player's
// hand through the game's own belt; while a park item is held, its piece is drawn as a ghost where
// the player aims.
using System;
using System.Globalization;
using Shortcut.RustMod;
using UnityEngine;

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
    private static bool keyboardOpen, wasP, wasQ, wasE, wasO, wasBack;
    private static int previewKind = -1;
    // The kind whose ghost is drawn now; -1 for none.
    public static int GhostKind { get { return previewKind; } }
    private static float previewYaw = 9999f; private static Vector3 previewAt;

    // Takes the controller's presses while the menu is open; returns the ones the ride may use.
    public static int Step(int padDown, float now)
    {
        var want = SkatePad.Shared && !SkateRide.On && (SkatePad.Buttons & MenuButton) != 0;
        var padOpen = Open && !keyboardOpen;
        if (want != padOpen) { if (want) { Open = true; keyboardOpen = false; Out.Say("PIECES menu open"); } else if (!keyboardOpen) Close(); }
        if (!Open || keyboardOpen) { stanceSince = -1f; return padDown; }
        if ((padDown & SkatePad.DpadDown) != 0 || (padDown & SkatePad.DpadRight) != 0) Chosen = (Chosen + 1) % Names.Length;
        if ((padDown & SkatePad.DpadUp) != 0 || (padDown & SkatePad.DpadLeft) != 0) Chosen = (Chosen + Names.Length - 1) % Names.Length;
        if ((padDown & SkatePad.LeftBumper) != 0) Turn -= TurnStep;
        if ((padDown & SkatePad.RightBumper) != 0) Turn += TurnStep;
        if ((padDown & SkatePad.A) != 0) Place(now, true);
        if ((padDown & SkatePad.B) != 0) Close();
        SkateRide.StanceHold(ref stanceSince, (SkatePad.Buttons & SkatePad.Y) != 0, now);
        return padDown & ~(SkatePad.DpadUp | SkatePad.DpadDown | SkatePad.DpadLeft | SkatePad.DpadRight | SkatePad.A | SkatePad.B | SkatePad.X | SkatePad.Y | SkatePad.LeftBumper | SkatePad.RightBumper);
    }

    // The keyboard: P opens the picker and steps to the next piece, Q and E turn it, O places it,
    // Backspace closes it. Only off the board.
    public static void Keys(bool p, bool q, bool e, bool o, bool back)
    {
        if (SkateRide.On) { if (keyboardOpen) Close(); wasP = p; wasQ = q; wasE = e; wasO = o; wasBack = back; return; }
        if (p && !wasP)
        {
            if (!Open) { Open = keyboardOpen = true; Out.Say("PIECES picker open (keyboard)"); }
            else if (keyboardOpen) Chosen = (Chosen + 1) % Names.Length;
        }
        if (Open && keyboardOpen)
        {
            if (q && !wasQ) Turn -= TurnStep;
            if (e && !wasE) Turn += TurnStep;
            if (o && !wasO) Place(Time.realtimeSinceStartup, false);
            if (back && !wasBack) Close();
        }
        wasP = p; wasQ = q; wasE = e; wasO = o; wasBack = back;
    }

    private static void Close()
    {
        Open = keyboardOpen = false;
        SkateParks.PreviewClear(); previewKind = -1; previewYaw = 9999f;
    }

    // Each frame while the picker is open: the aim point under the camera's view, the preview there.
    public static void Frame(float now)
    {
        if (!Open) { HeldFrame(now); return; }
        AimOk = false;
        try
        {
            var cam = Camera.main;
            if (cam == null) return;
            var from = cam.transform.position; var dir = cam.transform.forward;
            RaycastHit hit;
            if (!Physics.Raycast(from, dir, out hit, AimReach, SkateRide.GroundMask, QueryTriggerInteraction.Ignore)) return;
            if (hit.normal.y < 0.5f) return;
            AimOk = true; Aim = hit.point;
            var e = cam.transform.eulerAngles;
            Yaw = (float)Math.Round((e.y + Turn) / 5f) * 5f;
        }
        catch (Exception ex) { Status = "aim failed: " + ex.GetType().Name; return; }
        var kind = (SkateParks.Kind)Chosen;
        if ((int)kind != previewKind)
        {
            var line = SkateParks.PreviewAt(kind, Aim, Yaw, false);
            previewKind = (int)kind; previewAt = Aim; previewYaw = Yaw;
            Status = line.StartsWith("not drawn") ? line : "";
        }
        else if (Vector3.Distance(previewAt, Aim) > 0.01f || Math.Abs(previewYaw - Yaw) > 0.01f)
        {
            SkateParks.PreviewMove(Aim, Yaw); previewAt = Aim; previewYaw = Yaw;
        }
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
        Valid = SkateParks.Ghost((SkateParks.Kind)HeldKind, aimHit, Yaw, SkateHeld.PlannerTurn(SkateRig.Local), out origin, out yaw, out onHost, out why);
        OnFoundation = onHost; Yaw = yaw; GhostWhy = why;
        if (HeldKind != previewKind) { SkateParks.PreviewAt((SkateParks.Kind)HeldKind, origin, Yaw, false); previewKind = HeldKind; previewAt = origin; previewYaw = Yaw; }
        else if (Vector3.Distance(previewAt, origin) > 0.01f || Math.Abs(previewYaw - Yaw) > 0.01f) { SkateParks.PreviewMove(origin, Yaw); previewAt = origin; previewYaw = Yaw; }
        Said = SkateHeld.ItemNames[HeldKind] + (Valid ? (OnFoundation ? ": fits here (left click, R turns it)" : ": on the ground here (left click, R turns it)") : ": does not fit: " + GhostWhy); SaidAt = now;
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
        Said = SkateHeld.ItemNames[HeldKind] + ": against this half wall (left click with the square foundation)";
    }

    private static void HeldClear() { if (previewKind >= 0) { SkateParks.PreviewClear(); previewKind = -1; } }

    // The piece last taken with the picker; -1 before any.
    public static int Picked = -1;

    // The chosen piece's park item put in the player's hand, the way a number key would; the
    // player then places it with the building controls. Without one in the belt, the screen says so.
    public static void Place(float now, bool pad)
    {
        SaidAt = now;
        var player = SkateRig.Local;
        if (player == null) { Said = "No player yet"; return; }
        Picked = Chosen;
        var slot = SkateHeld.FindInBelt(player, Chosen);
        Out.Say("PIECES take " + SkateHeld.ItemNames[Chosen] + ": belt" + (SkateHeld.Belt == "" ? " empty" : SkateHeld.Belt) + " -> slot " + slot);
        // Shown in the open menu too, which the keyboard picker leaves open.
        // Where the belt cannot be read at all, the player picks the item's number key; the
        // server is not asked, since the item may well be there already.
        if (slot < 0 && SkateHeld.Belt == "belt not readable")
        {
            Close();
            Said = "Press the number key of your " + SkateHeld.ItemNames[Chosen] + ", then place it like a square foundation (left click, R turns it). None yet? An admin types skate.items " + Names[Chosen];
            return;
        }
        if (slot < 0)
        {
            // Only an item seen nowhere in the inventory is asked from the server, so none is issued twice.
            var held = SkateHeld.InMain(player, Chosen);
            if (held != 0)
            {
                Said = Status = "Move your " + SkateHeld.ItemNames[Chosen] + " from inventory to a belt slot, then select it" + (held < 0 ? " (an admin gives the park items: skate.items " + Names[Chosen] + ")" : "");
                return;
            }
            Said = Status = AskItem(Chosen, now) ? "Asked the server for a " + SkateHeld.ItemNames[Chosen] + "; take it again (" + (pad ? "A" : "O") + ") once it is in your belt"
                : "No " + SkateHeld.ItemNames[Chosen] + " in your inventory (an admin gives the park items: skate.items " + Names[Chosen] + ")";
            return;
        }
        string how;
        if (SkateHeld.SelectSlot(player, slot, out how))
        {
            Placed++; Close();
            Said = "Holding the " + SkateHeld.ItemNames[Chosen] + ": place it like a square foundation (left click, R turns it)";
            Out.Say("PIECES selected by " + how);
            return;
        }
        Out.Say("PIECES could not select: " + how);
        Close();
        Said = Status = "Press " + (slot + 1) + " to hold the " + SkateHeld.ItemNames[Chosen] + ", then place it like a square foundation";
    }

    // Without the item at hand the server is asked for one, at most once per piece every few
    // seconds: ShortcutSkate's skate.items gives it to an administrator and refuses anyone else.
    private static readonly float[] itemAskedAt = new float[Names.Length];
    public static int ItemsAsked;
    private static bool AskItem(int kind, float now)
    {
        if (itemAskedAt[kind] > 0f && now - itemAskedAt[kind] < 5f) return false;
        if (!SkateConsole.CanSend) { Out.Say("PIECES item ask cannot send: " + SkateConsole.SendState); return false; }
        var line = "skate.items " + Names[kind];
        if (!SkateConsole.Send(line)) { Out.Say("PIECES item ask not sent: " + SkateConsole.SendState); return false; }
        itemAskedAt[kind] = now; ItemsAsked++;
        Out.Say("PIECES item asked: " + line);
        return true;
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
            var s = "PARK PIECE   ";
            for (var i = 0; i < Names.Length; i++) s += (i == Chosen ? "[" + Names[i].ToUpper() + "]" : Names[i]) + (i + 1 < Names.Length ? "   " : "");
            s += keyboardOpen ? "      P: next   O: take it   Backspace: close" : "      D-pad: choose   A: take it   B: close   hold Y: stance (" + SkateRide.StanceName + ")";
            if (Status.Length > 0) s += "   " + Status;
            return s;
        }
        if (now - SaidAt < ShownSeconds) return Said;
        if (Asked != "" && now - AskedAt < PlaceSeconds) return "Park piece " + Asked + ": place a square foundation (" + (int)(PlaceSeconds - (now - AskedAt)) + " s left)";
        return "";
    }

    public static void Reset() { Close(); Picked = -1; HeldKind = -1; Asked = Said = Status = ""; SaidAt = AskedAt = -99f; Chosen = 0; Sent = Placed = 0; Turn = 0f; AimOk = false; }
}
