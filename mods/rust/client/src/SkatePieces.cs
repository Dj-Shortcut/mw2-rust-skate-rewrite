// The park picker: a piece chosen on the controller or the keyboard, its authored model drawn
// where the player aims as a ghost preview (no colliders), turned in steps, and placed with one
// press: the client sends `skate.place <piece> x y z yaw` to the server as a console packet
// (SkateConsole.Send), and the server builds the piece there. Where sending is not available
// nothing reaches the server from here; the screen then shows the line to type in F1 instead.
using System;
using System.Globalization;
using Shortcut.RustMod;
using UnityEngine;

public static class SkatePieces
{
    public static readonly string[] Names = { "bank", "kicker", "quarterpipe", "ledge-low", "ledge-high", "long-ledge", "funbox", "halfpipe" };
    public const int MenuButton = SkatePad.Back;
    public const float ShownSeconds = 6f, PlaceSeconds = 120f, AimReach = 40f, TurnStep = 15f, ClearGround = 0.04f;

    public static bool Open;
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
    private static float previewYaw = 9999f; private static Vector3 previewAt;

    // Takes the controller's presses while the menu is open; returns the ones the ride may use.
    public static int Step(int padDown, float now)
    {
        var want = SkatePad.Shared && !SkateRide.On && (SkatePad.Buttons & MenuButton) != 0;
        var padOpen = Open && !keyboardOpen;
        if (want != padOpen) { if (want) { Open = true; keyboardOpen = false; Out.Say("PIECES menu open"); } else if (!keyboardOpen) Close(); }
        if (!Open || keyboardOpen) return padDown;
        if ((padDown & SkatePad.DpadDown) != 0 || (padDown & SkatePad.DpadRight) != 0) Chosen = (Chosen + 1) % Names.Length;
        if ((padDown & SkatePad.DpadUp) != 0 || (padDown & SkatePad.DpadLeft) != 0) Chosen = (Chosen + Names.Length - 1) % Names.Length;
        if ((padDown & SkatePad.LeftBumper) != 0) Turn -= TurnStep;
        if ((padDown & SkatePad.RightBumper) != 0) Turn += TurnStep;
        if ((padDown & SkatePad.A) != 0) Place(now);
        if ((padDown & SkatePad.B) != 0) Ask("cancel", now);
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
            if (o && !wasO) Place(Time.realtimeSinceStartup);
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
        if (!Open) { if (previewKind >= 0) { SkateParks.PreviewClear(); previewKind = -1; } return; }
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

    // The chosen piece where the preview stands: sent to the server to build it there. Without a
    // way to send, nothing reaches the server from here, and the line to type is shown instead.
    // The console typist (SendInput) is not tried: it does not reach this client's console.
    public static void Place(float now)
    {
        var name = Names[Chosen];
        SaidAt = now;
        if (!SkateConsole.CanSend) { Said = Manual(name); return; }
        if (!AimOk) { Said = "Aim at the ground first"; return; }
        var ci = CultureInfo.InvariantCulture;
        // Untouched, the server places the piece where the player looks and lifts it clear of the
        // ground itself; turned, the preview's own anchor goes along, lifted by a floor tile's
        // thickness and a little slack, which the server takes exactly as given.
        var lift = -SkateParks.FloorBottom + ClearGround;
        var line = Turn == 0f ? "skate.place " + name
            : "skate.place " + name + " " + Aim.x.ToString("F2", ci) + " " + (Aim.y + lift).ToString("F2", ci) + " " + Aim.z.ToString("F2", ci) + " " + Yaw.ToString("F0", ci);
        if (SkateConsole.Send(line))
        {
            Placed++; Asked = "";
            Said = "Asked the server for a " + name + " at " + Out.V1(Aim) + " (yaw " + Yaw.ToString("F0") + "); the server answers in F1 (skate.place needs ShortcutSkate 0.3.6 or later)";
            return;
        }
        Said = "Not sent: " + SkateConsole.SendState;
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
            s += keyboardOpen ? "      P: next   Q/E: turn   O: place where you aim   Backspace: close" : "      D-pad: choose   LB/RB: turn   A: place where you aim   B: cancel";
            if (!AimOk) s += "   (aim at the ground)";
            if (Status.Length > 0) s += "   " + Status;
            return s;
        }
        if (now - SaidAt < ShownSeconds) return Said;
        if (Asked != "" && now - AskedAt < PlaceSeconds) return "Park piece " + Asked + ": place a square foundation (" + (int)(PlaceSeconds - (now - AskedAt)) + " s left)";
        return "";
    }

    public static void Reset() { Close(); Asked = Said = Status = ""; SaidAt = AskedAt = -99f; Chosen = 0; Sent = Placed = 0; Turn = 0f; AimOk = false; }
}
