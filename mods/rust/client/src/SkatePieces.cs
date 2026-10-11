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
        var ok = SkateConsole.Queue(line, now);
        SaidAt = now;
        if (!ok) { Said = "The console is still busy; press again in a moment"; return; }
        Sent++;
        if (name == "cancel") { Asked = ""; Said = "Typed in the console: " + line; return; }
        Asked = name; AskedAt = now;
        Said = "Typed in the console: " + line + "  -  now place one square foundation with the building plan within " + (int)PlaceSeconds + " s (admin only; the server answers in F1)";
    }

    // What the screen shows: the menu while open, the last answer for a few seconds, the pending piece.
    public static string Line(float now)
    {
        if (Open)
        {
            var s = "PARK PIECE   ";
            for (var i = 0; i < Names.Length; i++) s += (i == Chosen ? "[" + Names[i].ToUpper() + "]" : Names[i]) + (i + 1 < Names.Length ? "   " : "");
            return s + "      D-pad: choose   A: ask the server   B: cancel";
        }
        if (now - SaidAt < ShownSeconds) return Said;
        if (Asked != "" && now - AskedAt < PlaceSeconds) return "Park piece " + Asked + ": place a square foundation (" + (int)(PlaceSeconds - (now - AskedAt)) + " s left)";
        return "";
    }

    public static void Reset() { Open = false; Asked = Said = ""; SaidAt = AskedAt = -99f; Chosen = 0; Sent = 0; }
}
