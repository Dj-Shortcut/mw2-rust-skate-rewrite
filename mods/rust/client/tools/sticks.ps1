# Reads the controller from outside the game and shares it with the plugin. Inside the game Steam
# Input stands between a controller and whoever asks for it: the game gets keys and a mouse, and
# no stick. A process that Steam did not start still sees the controller itself (XInput).
#   zz sticks              watch every slot for $pwatch seconds (default 10) and say what was seen
#   $pserve = 'RustClient' share the controller in memory until that process has come and gone;
#                          session.ps1 starts it this way, as a process of its own
# The shared block is named ShortcutSkatePad2: a header and a ring of the last state changes, each
# with the reader's own clock, so that a flick between two frames of the game is not lost. Its
# layout is repeated in the plugin (SkatePad.cs).
$code = @'
using System;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public static class SkatePadReader1
{
    [StructLayout(LayoutKind.Sequential)]
    public struct State { public uint Packet; public ushort Buttons; public byte LeftTrigger, RightTrigger; public short LeftX, LeftY, RightX, RightY; }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint Get(uint slot, out State state);
    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint ms);

    public const string MapName = "ShortcutSkatePad2";
    public const uint Magic = 0x44504B53, Version = 2, NoSlot = 0xFFFFFFFF;
    public const int Ring = 128, SampleBytes = 24, Header = 32, Size = Header + Ring * SampleBytes;
    private static readonly string[] Names = { "up", "down", "left", "right", "start", "back", "left stick", "right stick", "LB", "RB", "", "", "A", "B", "X", "Y" };

    public static string Buttons(int mask)
    {
        var b = new StringBuilder();
        for (var i = 0; i < 16; i++) if ((mask & (1 << i)) != 0 && Names[i] != "") b.Append(b.Length > 0 ? "," : "").Append(Names[i]);
        return b.Length > 0 ? b.ToString() : "none";
    }

    public static string Look(int seconds)
    {
        var shown = new bool[4]; var packets = new int[4]; var last = new uint[4]; var pressed = new int[4]; var most = new int[4, 6];
        var clock = Stopwatch.StartNew(); long nextAll = 0;
        while (clock.ElapsedMilliseconds < seconds * 1000L)
        {
            // A slot without a controller is slow to ask, so the empty ones are asked now and then.
            var all = clock.ElapsedMilliseconds >= nextAll; if (all) nextAll = clock.ElapsedMilliseconds + 1000;
            for (var i = 0; i < 4; i++)
            {
                if (!all && !shown[i]) continue;
                State s;
                if (Get((uint)i, out s) != 0) { shown[i] = false; continue; }
                if (!shown[i]) { shown[i] = true; last[i] = s.Packet; }
                if (s.Packet != last[i]) { packets[i]++; last[i] = s.Packet; }
                pressed[i] |= s.Buttons;
                int[] v = { Math.Abs((int)s.LeftX), Math.Abs((int)s.LeftY), Math.Abs((int)s.RightX), Math.Abs((int)s.RightY), s.LeftTrigger, s.RightTrigger };
                for (var k = 0; k < 6; k++) if (v[k] > most[i, k]) most[i, k] = v[k];
            }
            Thread.Sleep(10);
        }
        var o = new StringBuilder(); var any = false;
        for (var i = 0; i < 4; i++)
        {
            if (!shown[i] && packets[i] == 0 && pressed[i] == 0) continue;
            any = true;
            o.AppendLine("slot " + i + ": state changes=" + packets[i] + " buttons=" + Buttons(pressed[i]) + " left stick up to " + most[i, 0] * 100 / 32768 + "," + most[i, 1] * 100 / 32768
                + " percent, right stick up to " + most[i, 2] * 100 / 32768 + "," + most[i, 3] * 100 / 32768 + " percent, triggers up to " + most[i, 4] * 100 / 255 + "," + most[i, 5] * 100 / 255 + " percent");
        }
        if (!any) o.AppendLine("no controller in any slot: Windows shows none to a process outside the game");
        return o.ToString().TrimEnd();
    }

    // Returns when the process has been seen and is gone again, or never came within waitSeconds.
    public static string Serve(string process, int waitSeconds)
    {
        bool mine;
        using (var only = new Mutex(true, "ShortcutSkatePadReader", out mine))
        {
            if (!mine) return "another reader is already sharing the controller";
            using (var map = MemoryMappedFile.CreateOrOpen(MapName, Size))
            using (var view = map.CreateViewAccessor(0, Size))
            {
                uint publication = view.ReadUInt32(28) & ~1u;
                uint ownership = unchecked(view.ReadUInt32(24) + 1);
                view.Write(28, unchecked(++publication)); Thread.MemoryBarrier();
                view.Write(0, Magic); view.Write(4, Version); view.Write(8, (uint)0); view.Write(12, NoSlot);
                view.Write(16, (long)0); view.Write(24, ownership);
                Thread.MemoryBarrier(); view.Write(28, unchecked(++publication));
                var clock = Stopwatch.StartNew();
                var shown = new bool[4]; var last = new uint[4]; var changedAt = new long[4];
                uint count = 0; var slot = -1; long nextAll = 0, nextAlive = 0; var seen = false; var samples = 0L;
                timeBeginPeriod(1);
                try
                {
                    while (true)
                    {
                        // Publish the ring and slot together; odd generations are never consumable.
                        view.Write(28, unchecked(++publication)); Thread.MemoryBarrier();
                        var us = clock.ElapsedTicks * 1000000L / Stopwatch.Frequency;
                        var all = us >= nextAll; if (all) nextAll = us + 1000000;
                        for (var i = 0; i < 4; i++)
                        {
                            if (!all && !shown[i]) continue;
                            State s;
                            if (Get((uint)i, out s) != 0) { shown[i] = false; if (slot == i) { slot = -1; ownership = unchecked(ownership + 1); } continue; }
                            var fresh = !shown[i]; if (fresh) { shown[i] = true; last[i] = s.Packet; }
                            var changed = s.Packet != last[i]; last[i] = s.Packet;
                            if (changed) changedAt[i] = us;
                            // The slot that is shared is the one a hand is on: another slot takes over
                            // when it changes while the shared one has been still for a second.
                            if (slot < 0 || (changed && i != slot && us - changedAt[slot] > 1000000)) { slot = i; ownership = unchecked(ownership + 1); changed = true; }
                            if (i != slot || !(changed || fresh)) continue;
                            long at = Header + (count % Ring) * SampleBytes;
                            view.Write(at, us); view.Write(at + 8, s.Packet); view.Write(at + 12, s.Buttons); view.Write(at + 14, s.LeftTrigger); view.Write(at + 15, s.RightTrigger);
                            view.Write(at + 16, s.LeftX); view.Write(at + 18, s.LeftY); view.Write(at + 20, s.RightX); view.Write(at + 22, s.RightY);
                            count++; samples++; view.Write(8, count);
                        }
                        view.Write(12, slot < 0 ? NoSlot : (uint)slot); view.Write(24, ownership); view.Write(16, us);
                        Thread.MemoryBarrier(); view.Write(28, unchecked(++publication));
                        if (us >= nextAlive)
                        {
                            nextAlive = us + 1000000;
                            var running = Process.GetProcessesByName(process).Length > 0;
                            if (running) seen = true;
                            else if (seen) return "shared " + samples + " state changes; " + process + " has closed";
                            else if (us > waitSeconds * 1000000L) return process + " did not start within " + waitSeconds + " seconds";
                        }
                        Thread.Sleep(4);
                    }
                }
                finally
                {
                    if ((publication & 1) == 0) view.Write(28, unchecked(++publication));
                    Thread.MemoryBarrier(); view.Write(12, NoSlot); view.Write(0, (uint)0);
                    Thread.MemoryBarrier(); view.Write(28, unchecked(++publication)); timeEndPeriod(1);
                }
            }
        }
    }
}
'@
if (-not ('SkatePadReader1' -as [type])) { Add-Type -TypeDefinition $code -ReferencedAssemblies 'System.Core' }
# A reader started as a process of its own is told through the environment whom to serve.
$serve = "$pserve"; if (-not $serve) { $serve = "$env:SKATE_PAD_SERVE" }
if ($serve) {
  $said = [SkatePadReader1]::Serve($serve, 600)
  (Get-Date -Format 's') + ' ' + $said | Out-File "$HOME\Downloads\claude-loader-probe\sticks.log" -Append -Encoding utf8
  $said
}
else {
  $n = 10; if ($pwatch) { $n = [int]$pwatch }
  Write-Host "watching the controller for $n seconds: move both sticks, press buttons"
  [SkatePadReader1]::Look($n)
}
