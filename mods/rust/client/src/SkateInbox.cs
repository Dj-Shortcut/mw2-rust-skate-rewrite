// Lines the server sends to this client, read without any hook: a server console command that
// prints on the client (echo) reaches Unity's log, which BepInEx writes to LogOutput.log when
// UnityLogListening is on. Every line carrying Marker is logged as an INBOX line with the time it
// was seen, so one live probe shows whether this route carries a server message at all. Nothing is
// acted on: a line here is not an identity, only evidence of the route.
using System;
using System.IO;
using System.Text;

public static class SkateInbox
{
    public const string Marker = "SKATE-PROBE";
    public const float EverySeconds = 1f;
    public const int MostLines = 50;
    public static int Seen;
    public static string Last = "";
    public static string State = "";
    private static long readTo = -1;
    private static float nextAt;
    private static string partial = "";

    // The log file this route would show up in; set by the tests.
    public static string LogPath;

    private static string Log() { return LogPath ?? Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log"); }

    // Called when the plugin loads, so a marker sent while connecting or waking up is kept: only
    // what was written before the plugin loaded is skipped.
    public static void Start()
    {
        try
        {
            var path = Log();
            if (!File.Exists(path)) { readTo = 0; Report("waiting for " + Path.GetFileName(path)); return; }
            using (var f = Open(path)) readTo = f.Length;
            Report("watching " + Path.GetFileName(path) + " from byte " + readTo);
        }
        catch (Exception e) { Report("start threw " + e.GetType().Name + ": " + e.Message); }
    }

    public static void Frame(float now)
    {
        if (now < nextAt) return;
        nextAt = now + EverySeconds;
        try
        {
            var path = Log();
            if (!File.Exists(path)) { Report("no " + Path.GetFileName(path)); return; }
            using (var f = Open(path))
            {
                // Without Start, only what is written from the first frame on counts.
                if (readTo < 0) { readTo = f.Length; Report("watching " + Path.GetFileName(path) + " from byte " + readTo); return; }
                // A log started over holds only new lines.
                if (readTo > f.Length) { readTo = 0; partial = ""; Report("log started over"); }
                if (f.Length == readTo) return;
                Report("reading");
                f.Seek(readTo, SeekOrigin.Begin);
                var buffer = new byte[Math.Min(f.Length - readTo, 1 << 20)];
                var n = f.Read(buffer, 0, buffer.Length);
                readTo += n;
                var text = partial + Encoding.UTF8.GetString(buffer, 0, n);
                var cut = text.LastIndexOf('\n');
                partial = cut < 0 ? text : text.Substring(cut + 1);
                if (cut < 0) return;
                foreach (var raw in text.Substring(0, cut).Split('\n'))
                {
                    var line = raw.TrimEnd('\r');
                    var at = line.IndexOf(Marker, StringComparison.Ordinal);
                    // Our own INBOX lines are not in this file, but a quoted marker could be.
                    if (at < 0 || line.IndexOf("INBOX", StringComparison.Ordinal) >= 0) continue;
                    Seen++; Last = line.Substring(at);
                    if (Seen <= MostLines) Out.Say("INBOX #" + Seen + " at " + now.ToString("F1") + " s: " + Last);
                }
            }
        }
        catch (Exception e) { Report("reading threw " + e.GetType().Name + ": " + e.Message); }
    }

    private static FileStream Open(string path) { return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }

    // Each change of state is logged once, so a broken reader is told apart from a quiet route.
    private static void Report(string state)
    {
        if (state == State) return;
        State = state;
        Out.Say("INBOX state: " + state);
    }

    public static void Reset() { Seen = 0; Last = State = partial = ""; readTo = -1; nextAt = 0f; }
}
