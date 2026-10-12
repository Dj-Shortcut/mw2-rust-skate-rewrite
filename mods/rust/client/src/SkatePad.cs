// Inside the game a controller is keys and a mouse: Steam Input stands between it and whoever asks
// for it, and there is no stick to read. A reader outside the game (tools/sticks.ps1) shares the
// controller's own state in a named block of memory: a header and a ring of the last state
// changes, each with the reader's clock. The right stick goes through the flick module change by
// change, with the time it spent where it was, so a flick between two frames is not lost.
// While a trigger is pulled in the air the right stick belongs to the grab: the flick module is
// told that it rests, or picking a grab would also flip the board.
using System;
using System.IO.MemoryMappedFiles;
using Shortcut.RustMod;
using UnityEngine;

public static class SkatePad
{
    public const string MapName = "ShortcutSkatePad";
    public const uint Magic = 0x44504B53;
    public const int Ring = 128, SampleBytes = 24, Header = 32, Size = Header + Ring * SampleBytes;
    public const int DpadUp = 0x1, DpadDown = 0x2, DpadLeft = 0x4, DpadRight = 0x8, Start = 0x10, Back = 0x20, LeftStickClick = 0x40, RightStickClick = 0x80,
        LeftBumper = 0x100, RightBumper = 0x200, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;
    public const float Touch = 0.35f, Pulled = 0.3f, SilentSeconds = 1.5f, MostCatchUp = 0.5f;

    // What the reader last said, and when a hand was last on the controller.
    public static bool Shared;
    public static int Buttons;
    public static float LeftX, LeftY, RightX, RightY, LeftTrigger, RightTrigger, TouchedAt = -99f;
    // What the right stick has asked for since the ride last took it: a pop with what the board does
    // in it, a flip added in the air.
    private static readonly SkatePop[] popped = new SkatePop[4];
    private static int poppedCount;
    public static float Charge;
    public static bool Charging, Manual, NoseManual;
    public static int Slot = -1, Flicks;

    private static MemoryMappedFile map;
    private static MemoryMappedViewAccessor view;
    private static readonly byte[] block = new byte[Size];
    private static SkateFlickState flick;
    private static uint count;
    private static int pressed;
    private static long clock, beat;
    private static float nextOpen, beatAt;
    private static bool primed, failed, said, fed;

    private static void Say(string m) { Out.Say("PAD " + m); }

    public static void Poll(float now)
    {
        if (fed) { Held(now); return; }
        if (failed) return;
        try
        {
            if (view == null)
            {
                if (now < nextOpen) return;
                nextOpen = now + 2f;
                try { map = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read); }
                catch (System.IO.FileNotFoundException) { if (!said) { said = true; Say("no controller reader beside the game: keyboard only"); } return; }
                view = map.CreateViewAccessor(0, Size, MemoryMappedFileAccess.Read);
                primed = false; beatAt = now;
                Say("the controller reader is there");
            }
            view.ReadArray(0, block, 0, Size);
            var stamp = BitConverter.ToInt64(block, 16);
            if (stamp != beat) { beat = stamp; beatAt = now; }
            if (BitConverter.ToUInt32(block, 0) != Magic || now - beatAt > SilentSeconds)
            {
                Say("the controller reader has stopped");
                view.Dispose(); map.Dispose(); view = null; map = null; Lost();
                return;
            }
            var slot = (int)BitConverter.ToUInt32(block, 12);
            if (slot != Slot) { Slot = slot; Say(slot < 0 ? "no controller" : "controller in slot " + slot); }
            if (slot < 0) { Lost(); return; }
            var n = BitConverter.ToUInt32(block, 8);
            if (!primed)
            {
                // The state of the moment, without what was pressed before the plugin looked.
                primed = true; count = n; clock = stamp;
                if (n > 0) Take(Header + (int)((n - 1) % Ring) * SampleBytes, now, false);
            }
            if (n - count > Ring) count = n - Ring;
            for (; count != n; count++) Take(Header + (int)(count % Ring) * SampleBytes, now, true);
            Advance(stamp);
            Shared = true;
            Held(now);
        }
        catch (Exception e) { failed = true; Lost(); Say("the controller cannot be read: " + e.GetType().Name + ": " + e.Message); }
    }

    // A hand is on the controller for as long as anything on it is away from its rest.
    private static void Held(float now)
    {
        if (Shared && (Buttons != 0 || LeftTrigger > Pulled || RightTrigger > Pulled || LeftX * LeftX + LeftY * LeftY > Touch * Touch || RightX * RightX + RightY * RightY > Touch * Touch)) TouchedAt = now;
    }

    private static void Lost()
    {
        Shared = false; Buttons = 0; LeftX = LeftY = RightX = RightY = LeftTrigger = RightTrigger = 0f;
        poppedCount = 0; Charging = Manual = NoseManual = false; Charge = 0f; pressed = 0; flick = default(SkateFlickState);
    }

    private static void Take(int at, float now, bool edges)
    {
        var us = BitConverter.ToInt64(block, at);
        State(us, BitConverter.ToUInt16(block, at + 12), block[at + 14] / 255f, block[at + 15] / 255f, BitConverter.ToInt16(block, at + 16) / 32768f, BitConverter.ToInt16(block, at + 18) / 32768f,
            BitConverter.ToInt16(block, at + 20) / 32768f, BitConverter.ToInt16(block, at + 22) / 32768f, now, edges);
    }

    // The controller's state from the moment `us` of the reader's clock on.
    private static void State(long us, int buttons, float lt, float rt, float lx, float ly, float rx, float ry, float now, bool edges)
    {
        Advance(us);
        if (edges) pressed |= buttons & ~Buttons;
        SkatePadLog.Stick(us, rx, ry);
        SkatePadLog.Buttons(buttons, lt, rt, lx, ly, rx, ry, now, edges);
        Buttons = buttons; LeftTrigger = lt; RightTrigger = rt; LeftX = lx; LeftY = ly; RightX = rx; RightY = ry;
        // Also a press that is over again before the next frame.
        if (buttons != 0 || lt > Pulled || rt > Pulled || lx * lx + ly * ly > Touch * Touch || rx * rx + ry * ry > Touch * Touch) TouchedAt = now;
    }

    // The right stick has been where it is from the clock's last moment until `us`.
    private static void Advance(long us)
    {
        SkatePadLog.Clock(us);
        var seconds = (us - clock) / 1000000.0;
        clock = us;
        if (seconds <= 0) return;
        if (seconds > MostCatchUp) seconds = MostCatchUp;
        var air = SkateRide.Mode == RideMode.Air;
        var grabbing = air && (LeftTrigger > Pulled || RightTrigger > Pulled);
        var input = new SkateFlickInput(grabbing ? 0f : SkateRide.Clamp(RightX, -1f, 1f), grabbing ? 0f : SkateRide.Clamp(RightY, -1f, 1f), air, SkateRide.Goofy != (SkateRide.On && SkateRide.TailFirst));
        while (seconds > 0.000001)
        {
            var dt = seconds > 0.01 ? 0.01 : seconds; seconds -= dt;
            SkateFlickResult r; string error;
            if (!SkateFlick.TryStep(flick, input, dt, out r, out error)) { flick = default(SkateFlickState); return; }
            flick = r.State;
            if (r.Events != SkateFlickEvents.None)
            {
                if (poppedCount < popped.Length) popped[poppedCount++] = r.Composition;
                Flicks++; SkatePadLog.Fired(r.Events, r.Composition);
            }
            Charging = r.Charging; Charge = (float)r.Charge; Manual = r.Manual; NoseManual = r.NoseManual;
        }
    }

    // Buttons that went down since the last call.
    public static int Pressed() { var p = pressed; pressed = 0; return p; }

    // The oldest thing the right stick asked for that the ride has not taken yet.
    public static bool TakePop(out SkatePop pop)
    {
        pop = default(SkatePop);
        if (poppedCount == 0) return false;
        pop = popped[0]; poppedCount--;
        for (var i = 0; i < poppedCount; i++) popped[i] = popped[i + 1];
        return true;
    }

    public static void DropPops() { poppedCount = 0; }

    // A scripted check plays a controller without one: the same states, on a clock of its own.
    public static void Feed(long us, int buttons, float lt, float rt, float lx, float ly, float rx, float ry, float now)
    {
        if (!fed) { fed = true; clock = us; Lost(); }
        Shared = true; Slot = 9;
        State(us, buttons, lt, rt, lx, ly, rx, ry, now, true);
    }

    public static void FeedClock(long us) { if (fed) Advance(us); }

    public static void FeedEnd() { if (fed) { fed = false; Lost(); Slot = -1; } }
}
