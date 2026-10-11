// A console command typed for the player. The game's own console class is renamed in this client
// build and could not be found by its shape, so the plugin does what a hand does: it sends the
// game window the keys through Windows (SendInput) — F1 opens the console, the line is typed as
// characters, Enter runs it, F1 closes the console again. One step a frame or two apart, because
// the console takes a frame to open and to take focus. The game must be the window in front, which
// it is while someone plays. Where SendInput is not there (not Windows), the line goes on the
// clipboard instead and the screen says so.
using System;
using System.Runtime.InteropServices;
using UnityEngine;

public static class SkateConsole
{
    public const float StepSeconds = 0.12f;
    // The game reads keys by their scan code (raw input), so a key is sent as a scan code: a virtual
    // key alone arrives with scan code 0 and the game sees no key at all.
    private const ushort VkF1 = 0x70, VkReturn = 0x0D, ScanF1 = 0x3B, ScanReturn = 0x1C;
    private const uint InputKeyboard = 1, KeyUp = 0x0002, Unicode = 0x0004, ScanCode = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { public ushort Vk, Scan; public uint Flags, Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public KeyboardInput Key; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);

    public static Func<string, bool> Typist;
    // A check leaves the console open, so the answer is on the screen.
    public static bool KeepOpen;
    // Left open by the last line (KeepOpen): the next line must not press F1, which would close it.
    public static bool LeftOpen;
    public static int Typed, Failed;
    public static string Last = "";
    private static string pending = "";
    private static int step = -1;
    private static float nextAt;
    private static bool said;

    // Takes the line for typing; false when it is already busy with one or typing is not available.
    public static bool Queue(string line, float now)
    {
        if (Typist != null) { var ok = Typist(line); if (ok) { Typed++; Last = line; } else Failed++; return ok; }
        if (step >= 0) return false;
        pending = line; step = 0; nextAt = now; Last = line;
        return true;
    }

    public static bool Busy { get { return step >= 0; } }

    public static void Update(float now)
    {
        if (step < 0 || now < nextAt) return;
        try
        {
            if (step == 0) { if (!LeftOpen) Tap(VkF1, ScanF1); }
            else if (step == 1) Text(pending);
            else if (step == 2) Tap(VkReturn, ScanReturn);
            else if (step == 3) { if (!KeepOpen) Tap(VkF1, ScanF1); LeftOpen = KeepOpen; Typed++; Out.Say("CONSOLE typed: " + pending); }
            step = step < 3 ? step + 1 : -1; nextAt = now + StepSeconds;
        }
        catch (Exception e)
        {
            step = -1; Failed++;
            if (!said) { said = true; Out.Say("CONSOLE typing is not available here: " + e.GetType().Name + ": " + e.Message); }
            try { GUIUtility.systemCopyBuffer = pending; Out.Say("CONSOLE on the clipboard instead: " + pending); } catch (Exception) { }
        }
    }

    private static void Tap(ushort vk, ushort scan)
    {
        var inputs = new Input[2];
        inputs[0].Type = InputKeyboard; inputs[0].Key.Vk = vk; inputs[0].Key.Scan = scan; inputs[0].Key.Flags = ScanCode;
        inputs[1].Type = InputKeyboard; inputs[1].Key.Vk = vk; inputs[1].Key.Scan = scan; inputs[1].Key.Flags = ScanCode | KeyUp;
        Send(inputs);
    }

    private static void Text(string s)
    {
        var inputs = new Input[s.Length * 2];
        for (var i = 0; i < s.Length; i++)
        {
            inputs[2 * i].Type = InputKeyboard; inputs[2 * i].Key.Scan = s[i]; inputs[2 * i].Key.Flags = Unicode;
            inputs[2 * i + 1].Type = InputKeyboard; inputs[2 * i + 1].Key.Scan = s[i]; inputs[2 * i + 1].Key.Flags = Unicode | KeyUp;
        }
        Send(inputs);
    }

    private static void Send(Input[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input)));
        if (sent != inputs.Length) throw new InvalidOperationException("SendInput sent " + sent + " of " + inputs.Length + " (error " + Marshal.GetLastWin32Error() + ")");
    }

    public static void Reset() { pending = ""; step = -1; Typed = Failed = 0; Last = ""; LeftOpen = false; }
}
