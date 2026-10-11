// Park pieces from the controller. The server plugin places a piece where the next square floor
// goes, after `skate.piece <name>` in the console. Typing that in the console with a controller in
// the hands is a chore, so the client offers a small menu on foot: hold the menu button (Back/View
// by default), step through the pieces with the D-pad, A asks the server for the one shown, B
// cancels a pending choice. The server answers in the console; the client shows what it asked for.
// Whoever rides from the keyboard binds a key in the console instead (bind f7 "skate.piece bank").
using System;
using Shortcut.RustMod;
using UnityEngine;

public static class SkatePieces
{
    public static readonly string[] Names = { "bank", "kicker", "quarterpipe", "ledge-low", "ledge-high", "long-ledge", "funbox" };
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
        var ok = Send("skate.piece " + name);
        SaidAt = now;
        if (!ok) { Said = "The console command could not be sent; type skate.piece " + name + " in the F1 console"; return; }
        if (name == "cancel") { Asked = ""; Said = "Park piece cancelled"; return; }
        Asked = name; AskedAt = now;
        Said = "Asked the server for a " + name + ": place one square floor with the building plan within " + (int)PlaceSeconds + " s (you must be an admin)";
    }

    // The game's console, as a bound key would reach it; the server runs skate.piece.
    private static bool Send(string line)
    {
        try
        {
            if (Run != null) { Sent++; return Run(line); }
            ConsoleSystem.Run(ConsoleSystem.Option.Server, line);
            Sent++; Out.Say("PIECES sent: " + line);
            return true;
        }
        catch (Exception e) { Out.Say("PIECES could not send '" + line + "': " + e.GetType().Name + ": " + e.Message); return false; }
    }

    // What the screen shows: the menu while open, the last answer for a few seconds, the pending piece.
    public static string Line(float now)
    {
        if (Open)
        {
            var s = "PARK PIECE   ";
            for (var i = 0; i < Names.Length; i++) s += (i == Chosen ? "[" + Names[i].ToUpper() + "]" : Names[i]) + (i + 1 < Names.Length ? "   " : "");
            return s + "      D-pad: choose   A: ask   B: cancel";
        }
        if (now - SaidAt < ShownSeconds) return Said;
        if (Asked != "" && now - AskedAt < PlaceSeconds) return "Park piece " + Asked + ": place a square floor (" + (int)(PlaceSeconds - (now - AskedAt)) + " s left)";
        return "";
    }

    public static void Reset() { Open = false; Asked = Said = ""; SaidAt = AskedAt = -99f; Chosen = 0; Sent = 0; }
}
