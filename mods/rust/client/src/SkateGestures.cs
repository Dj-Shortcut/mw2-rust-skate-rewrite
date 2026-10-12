// The game's gesture catalog as the client has it, written to the log once a session: the names the
// server plugin's park gestures (ParkNextGesture, ParkCancelGesture in ShortcutSkate.json) must be
// set to. The server reads the same catalog; its own list needs a privileged skate.status in F1,
// which cannot be typed from a distance. Read by reflection, as the type's members are not compiled
// against; nothing is changed.
using System;
using System.Collections;
using System.Reflection;

public static class SkateGestures
{
    public static bool Done;
    public static int Count = -1;
    public static string Names = "";

    public static void Report()
    {
        if (Done) return;
        Done = true;
        try
        {
            Type type = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types; } catch (Exception) { continue; }
                foreach (var t in types) if (t != null && t.Name == "GestureCollection") { type = t; break; }
                if (type != null) break;
            }
            if (type == null) { Out.Say("GESTURES no GestureCollection type in the loaded assemblies"); return; }
            var instance = Get(null, type, "Instance") ?? Get(null, type, "instance");
            if (instance == null) { Out.Say("GESTURES GestureCollection.Instance is not there yet"); Done = false; return; }
            var all = Get(instance, type, "AllGestures") as IEnumerable;
            if (all == null) { Out.Say("GESTURES AllGestures is not readable"); return; }
            var line = ""; Count = 0;
            foreach (var g in all)
            {
                if (g == null) continue;
                var gt = g.GetType();
                var phrase = Get(g, gt, "gestureName");
                var english = phrase == null ? null : Get(phrase, phrase.GetType(), "english") as string;
                var token = phrase == null ? null : Get(phrase, phrase.GetType(), "token") as string;
                var convar = Get(g, gt, "convarName") as string;
                var command = Get(g, gt, "gestureCommand") as string;
                line += (Count == 0 ? "" : " | ") + (english ?? "?") + " [" + (convar ?? "") + (command != null && command != convar ? ", " + command : "") + (token != null ? ", " + token : "") + "]";
                Count++;
            }
            Names = line;
            Out.Say("GESTURES " + Count + ": " + line);
        }
        catch (Exception e) { Out.Say("GESTURES threw: " + e.GetType().Name + ": " + e.Message); }
    }

    // A field or a property of that name, public or not, static when the object is null.
    private static object Get(object o, Type t, string name)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        for (var at = t; at != null; at = at.BaseType)
        {
            var p = at.GetProperty(name, flags);
            if (p != null && p.GetIndexParameters().Length == 0) { try { return p.GetValue(o, null); } catch (Exception) { } }
            var f = at.GetField(name, flags);
            if (f != null) { try { return f.GetValue(o); } catch (Exception) { } }
        }
        return null;
    }
}
