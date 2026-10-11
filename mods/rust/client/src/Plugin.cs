// The log also goes straight to plugins\skate.log: a native crash loses the loader's own log.
using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

[BepInPlugin("shortcut.skate.client", "Shortcut Skate Client", SkatePlugin.Version)]
public class SkatePlugin : BasePlugin
{
    public const string Version = "0.15.0";
    internal static SkatePlugin Instance;

    public override void Load()
    {
        Instance = this;
        Out.Inner = Log;
        var path = System.IO.Path.Combine(Paths.PluginPath, "steps.txt");
        var steps = System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path).Trim() : "skate";
        Out.Say("PLUGIN " + Version + " steps=" + steps);
        foreach (var s in steps.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            try { Out.Say("STEP " + s + ": " + Run(s.Trim())); }
            catch (Exception e) { Out.Err("STEP " + s + " threw: " + e); }
        }
        Out.Say("PLUGIN loaded");
    }

    private string Run(string s)
    {
        switch (s)
        {
            case "skate": return "added=" + (AddComponent<SkateRig>() != null);
            case "ride": case "pose": case "trick": case "pad": case "piece": Scenarios.Name = s; return "added=" + (AddComponent<SkateRig>() != null);
            case "nogrind": SkateGrind.Enabled = false; return "grinding off";
            case "mute": SkateRig.Mute = true; AudioListener.volume = SkateRig.MuteVolume; return "volume=" + AudioListener.volume;
            default: return "unknown step";
        }
    }
}

public static class Out
{
    public static ManualLogSource Inner;
    private static readonly string file = System.IO.Path.Combine(Paths.PluginPath, "skate.log");

    public static void Say(string m) { System.IO.File.AppendAllText(file, m + "\r\n"); if (Inner != null) Inner.LogMessage(m); }
    public static void Err(string m) { System.IO.File.AppendAllText(file, "ERROR " + m + "\r\n"); if (Inner != null) Inner.LogError(m); }
    public static string V(Vector3 v) { return v.x.ToString("F2") + "," + v.y.ToString("F2") + "," + v.z.ToString("F2"); }
    public static string V1(Vector3 v) { return v.x.ToString("F1") + "," + v.y.ToString("F1") + "," + v.z.ToString("F1"); }
    public static string V3(Vector3 v) { return v.x.ToString("F3") + "," + v.y.ToString("F3") + "," + v.z.ToString("F3"); }

    public static string Try(Func<string> f)
    {
        try { return f(); }
        catch (Exception e) { return "unavailable(" + (e.Message != null && e.Message.Contains("unstripping") ? "stripped" : e.GetType().Name + ": " + e.Message) + ")"; }
    }

    public static string Chain(Transform t)
    {
        var s = ""; var p = t.parent; var n = 0;
        while (p != null && n++ < 8) { s = p.gameObject.name + "/" + s; p = p.parent; }
        return s == "" ? "(root)" : s;
    }
}
