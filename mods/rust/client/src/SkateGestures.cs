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
    public const int MostTries = 20;
    private static int tries;

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
            // Instance can stay null in a world; then a loaded object of the type is used, and failing
            // that its static members are named once.
            if (instance == null) instance = Loaded(type);
            if (instance == null)
            {
                tries++;
                if (tries < MostTries) { Done = false; return; }
                Out.Say("GESTURES no GestureCollection instance after " + tries + " tries; its static members: " + Statics(type));
                return;
            }
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

    // An object of the type among those Unity has loaded (a ScriptableObject asset).
    private static object Loaded(Type type)
    {
        try
        {
            foreach (var m in typeof(UnityEngine.Resources).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "FindObjectsOfTypeAll" || !m.IsGenericMethodDefinition) continue;
                // An interop array is not a System.Array but can be walked.
                var found = m.MakeGenericMethod(type).Invoke(null, null) as IEnumerable;
                if (found != null) foreach (var o in found) if (o != null) return o;
            }
        }
        catch (Exception e) { Out.Say("GESTURES FindObjectsOfTypeAll threw: " + e.GetType().Name + ": " + e.Message); }
        return null;
    }

    private static string Statics(Type type)
    {
        var line = "";
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var p in type.GetProperties(flags)) line += " P:" + p.Name + ":" + p.PropertyType.Name;
        foreach (var f in type.GetFields(flags)) line += " F:" + f.Name + ":" + f.FieldType.Name;
        foreach (var m in type.GetMethods(flags)) if (!m.IsSpecialName) line += " M:" + m.Name;
        return line == "" ? "(none)" : line;
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
