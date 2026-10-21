// The log also goes straight to plugins\skate.log: a native crash loses the loader's own log.
using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

[BepInPlugin("shortcut.skate.client", "Shortcut Skate Client", SkatePlugin.Version)]
public class SkatePlugin : BasePlugin
{
    public const string Version = "0.18.35";
    internal static SkatePlugin Instance;
    private SkateRig rig;

    public override void Load()
    {
        Instance = this;
        Out.Inner = Log;
        var steps = "skate";
        try
        {
            var path = System.IO.Path.Combine(Paths.PluginPath, "steps.txt");
            if (System.IO.File.Exists(path)) steps = System.IO.File.ReadAllText(path).Trim();
        }
        catch (Exception e) { Out.Err("steps.txt could not be read; using skate: " + e.GetType().Name + ": " + e.Message); }
        Out.Say("PLUGIN " + Version + " steps=" + steps);
        SkateInbox.Start();
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
            case "skate": return AddRig();
            case "physicsdiag": return SkatePhysicsDiagnostic.Enable();
            case "ride": case "pose": case "trick": case "pad": case "piece": case "status": case "park": case "send": case "place": case "tour": case "feet": case "mega": Scenarios.Name = s; return AddRig();
            case "nogrind": SkateGrind.Enabled = false; return "grinding off";
            case "yaw90": case "yaw180": case "yaw270": Scenarios.YawOffset = float.Parse(s.Substring(3)); return "scenario yaw offset " + Scenarios.YawOffset;
            case "mute": SkateRig.Mute = true; AudioListener.volume = SkateRig.MuteVolume; return "volume=" + AudioListener.volume;
            default:
                if (s.StartsWith("to:"))
                {
                    var xz = s.Substring(3).Split('/');
                    Scenarios.Target = new Vector3(float.Parse(xz[0], System.Globalization.CultureInfo.InvariantCulture), 0f, float.Parse(xz[1], System.Globalization.CultureInfo.InvariantCulture));
                    Scenarios.HasTarget = true;
                    return "tour target " + Out.V1(Scenarios.Target);
                }
                return "unknown step";
        }
    }

    private string AddRig()
    {
        if (rig != null) return "already added";
        SkateRide.LoadStance();
        rig = AddComponent<SkateRig>();
        return "added=" + (rig != null) + " stance=" + SkateRide.StanceName;
    }
}

public static class Out
{
    public static ManualLogSource Inner;
    private static string fileFailure;
    private static bool fileWarningSent;

    public static void Say(string m) { Write(m, false); }
    public static void Err(string m) { Write(m, true); }

    private static void Write(string m, bool error)
    {
        if (fileFailure == null)
        {
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(Paths.PluginPath, "skate.log"), (error ? "ERROR " : "") + m + "\r\n"); }
            catch (Exception e) { fileFailure = e.GetType().Name; }
        }
        var logger = Inner;
        if (logger == null) return;
        if (fileFailure != null && !fileWarningSent)
        {
            fileWarningSent = true;
            try { logger.LogWarning("skate.log file output disabled: " + fileFailure); }
            catch (Exception) { }
        }
        try { if (error) logger.LogError(m); else logger.LogMessage(m); }
        catch (Exception) { }
    }
    // Textures, clips, meshes and materials made here are held only from managed code, which the
    // engine does not see: the game's periodic unloading of unused assets frees them under us
    // (the HUD then passes null textures and the board's one-shot sounds null clips).
    public static T Keep<T>(T o) where T : UnityEngine.Object
    {
        try { if (o != null) o.hideFlags |= HideFlags.DontUnloadUnusedAsset; }
        catch (Exception) { }
        return o;
    }
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
