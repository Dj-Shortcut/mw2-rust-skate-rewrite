// Park pieces from the controller. The server plugin places a piece where the next square floor
// goes, after `skate.piece <name>` in the console. Typing that in the console with a controller in
// the hands is a chore, so the client offers a small menu on foot: hold the menu button (Back/View
// by default), step through the pieces with the D-pad, A puts `skate.piece <name>` on the
// clipboard for the F1 console (Ctrl+V, Enter), B puts the cancel there. The game's own console
// class is renamed in this client build and could not be found by its shape, so the plugin does not
// run the command itself. Whoever rides from the keyboard binds a key instead
// (bind f7 "skate.piece bank").
using System;
using Shortcut.RustMod;
using UnityEngine;

public static class SkatePieces
{
    public static readonly string[] Names = { "bank", "kicker", "quarterpipe", "ledge-low", "ledge-high", "long-ledge", "funbox", "halfpipe" };
    public const int MenuButton = SkatePad.Back;
    public const float ShownSeconds = 6f, PlaceSeconds = 120f;

    public static bool Open;
    public static int Chosen;
    public static string Asked = "", Said = "";
    public static float SaidAt = -99f, AskedAt = -99f;
    public static int Sent;
    public static Func<string, bool> Run;

    // Takes the controller's presses while the menu is open; returns the ones the ride may use.
    public static int Step(int padDown, float now)
    {
        var want = SkatePad.Shared && !SkateRide.On && (SkatePad.Buttons & MenuButton) != 0;
        if (want != Open) { Open = want; if (Open) Out.Say("PIECES menu open"); }
        if (!Open) return padDown;
        if ((padDown & SkatePad.DpadDown) != 0 || (padDown & SkatePad.DpadRight) != 0) Chosen = (Chosen + 1) % Names.Length;
        if ((padDown & SkatePad.DpadUp) != 0 || (padDown & SkatePad.DpadLeft) != 0) Chosen = (Chosen + Names.Length - 1) % Names.Length;
        if ((padDown & SkatePad.A) != 0) Ask(Names[Chosen], now);
        if ((padDown & SkatePad.B) != 0) Ask("cancel", now);
        return padDown & ~(SkatePad.DpadUp | SkatePad.DpadDown | SkatePad.DpadLeft | SkatePad.DpadRight | SkatePad.A | SkatePad.B | SkatePad.X | SkatePad.Y);
    }

    public static void Ask(string name, float now)
    {
        var line = "skate.piece " + name;
        var ok = Send(line);
        SaidAt = now;
        if (!ok) { Said = "The clipboard could not be written; type " + line + " in the F1 console"; return; }
        if (name == "cancel") { Asked = ""; Said = "On the clipboard: " + line + "  -  F1, Ctrl+V, Enter"; return; }
        Asked = name; AskedAt = now;
        Said = "On the clipboard: " + line + "  -  F1, Ctrl+V, Enter, then place one square floor with the building plan within " + (int)PlaceSeconds + " s (admin only)";
    }

    // The line goes on the system clipboard; the F1 console takes it with Ctrl+V.
    private static bool Send(string line)
    {
        try
        {
            if (Run != null) { Sent++; return Run(line); }
            GUIUtility.systemCopyBuffer = line;
            Sent++; Out.Say("PIECES on the clipboard: " + line);
            return true;
        }
        catch (Exception e) { Out.Say("PIECES could not put '" + line + "' on the clipboard: " + e.GetType().Name + ": " + e.Message); return false; }
    }

    // What the screen shows: the menu while open, the last answer for a few seconds, the pending piece.
    public static string Line(float now)
    {
        if (Open)
        {
            var s = "PARK PIECE   ";
            for (var i = 0; i < Names.Length; i++) s += (i == Chosen ? "[" + Names[i].ToUpper() + "]" : Names[i]) + (i + 1 < Names.Length ? "   " : "");
            return s + "      D-pad: choose   A: copy the command   B: copy cancel";
        }
        if (now - SaidAt < ShownSeconds) return Said;
        if (Asked != "" && now - AskedAt < PlaceSeconds) return "Park piece " + Asked + ": paste in F1, then place a square floor (" + (int)(PlaceSeconds - (now - AskedAt)) + " s after the paste)";
        return "";
    }

    public static void Reset() { Open = false; Asked = Said = ""; SaidAt = AskedAt = -99f; Chosen = 0; Sent = 0; }
}
