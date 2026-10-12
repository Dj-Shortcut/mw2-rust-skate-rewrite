// What the player holds, for the park items (Codex's ShortcutSkate 0.3.11: a building plan marked
// with instanceData.dataInt 0x534B5001..08 and named "Skate Bank" and so on). The client's own code
// is renamed in this build, so the item is looked for by its shape: a member of the local player
// that gives an object with an `info` (the item definition, `shortname`) and an `instanceData` with
// a `dataInt`. Until that is seen live, the members that might lead there are written to the log
// once (HELD lines), so the next build can go straight to them.
using System;
using System.Reflection;

public static class SkateHeld
{
    public static bool Reported;
    public static string Shortname = "", Name = "";
    public static int Marker;
    private static MethodInfo activeItem;
    private static bool looked;

    public static void Report(object player)
    {
        if (Reported || player == null) return;
        Reported = true;
        try
        {
            var line = "";
            var t = player.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            for (var at = t; at != null && at != typeof(object); at = at.BaseType)
                foreach (var m in at.GetMethods(flags | BindingFlags.DeclaredOnly))
                    if (m.GetParameters().Length == 0 && m.ReturnType != null && m.ReturnType.Name == "Item")
                        line += " " + at.Name + "." + m.Name + "()";
            Out.Say("HELD methods returning Item:" + (line == "" ? " (none)" : line));
            Type item = null;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = a.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types; } catch (Exception) { continue; }
                foreach (var ty in types) if (ty != null && ty.Name == "Item" && ty.GetProperty("info") != null) { item = ty; break; }
                if (item != null) break;
            }
            if (item == null) { Out.Say("HELD no Item type with an info member"); return; }
            var members = "";
            foreach (var p in item.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) members += " " + p.Name + ":" + p.PropertyType.Name;
            Out.Say("HELD Item members:" + members);
        }
        catch (Exception e) { Out.Say("HELD threw: " + e.GetType().Name + ": " + e.Message); }
    }

    // The held item's shortname, name and marker, by the named route when the game has it
    // (GetActiveItem); otherwise nothing, and the log says what there is.
    public static bool Read(object player)
    {
        Shortname = ""; Name = ""; Marker = 0;
        if (player == null) return false;
        try
        {
            if (!looked) { looked = true; activeItem = player.GetType().GetMethod("GetActiveItem", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null); }
            if (activeItem == null) return false;
            var it = activeItem.Invoke(player, null);
            if (it == null) return false;
            var info = Get(it, "info");
            Shortname = (info == null ? null : Get(info, "shortname") as string) ?? "";
            Name = Get(it, "name") as string ?? "";
            var data = Get(it, "instanceData");
            var marker = data == null ? null : Get(data, "dataInt");
            Marker = marker is int ? (int)marker : 0;
            return true;
        }
        catch (Exception) { return false; }
    }

    private static object Get(object o, string name)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var t = o.GetType();
        var p = t.GetProperty(name, flags);
        if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(o, null);
        var f = t.GetField(name, flags);
        return f == null ? null : f.GetValue(o);
    }
}
