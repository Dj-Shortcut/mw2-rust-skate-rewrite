// How long the plugin's own work takes each frame, by part, for the once-a-second status line:
// the owner saw the game drop from 50 to 5 frames a second while riding, and the log must say
// which part eats the time. The clock only adds; the status line reads and clears it.
using System.Diagnostics;

public static class SkateClock
{
    public const int Keys = 0, Ride = 1, Board = 2, Look = 3, Rider = 4, Camera = 5, Hud = 6, Held = 7, Parts = 8;
    // "look" includes "held": the held item is handled inside the look.
    private static readonly string[] names = { "keys", "ride", "board", "look", "rider", "camera", "hud", "held" };
    private static readonly long[] ticks = new long[Parts];
    private static readonly int[] calls = new int[Parts];
    private static int frames;
    private static long frameTicks, lastFrame;

    public static long Now() { return Stopwatch.GetTimestamp(); }

    public static void Add(int part, long since) { ticks[part] += Stopwatch.GetTimestamp() - since; calls[part]++; }

    // Called once per rendered frame: the whole frame's length comes from the gaps between calls.
    public static void Frame()
    {
        var now = Stopwatch.GetTimestamp();
        if (lastFrame != 0) { frameTicks += now - lastFrame; frames++; }
        lastFrame = now;
    }

    // Milliseconds per frame for each part and for the whole frame, then cleared.
    public static string Report()
    {
        var perMs = Stopwatch.Frequency / 1000.0;
        var n = frames > 0 ? frames : 1;
        var s = "ms/frame: all " + (frameTicks / perMs / n).ToString("F1") + " (" + frames + " frames)";
        for (var i = 0; i < Parts; i++)
        {
            if (calls[i] > 0) s += " " + names[i] + " " + (ticks[i] / perMs / n).ToString("F2");
            ticks[i] = 0; calls[i] = 0;
        }
        frames = 0; frameTicks = 0;
        return s;
    }
}
