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
            if (item == null)
            {
                // Seen live (Rust 25878151): no type is named Item. The item type is the one that holds
                // an ItemDefinition (a Unity asset type, whose name the build keeps); named here with
                // its members, and the player members that lead to it, for the next build.
                var shapes = 0;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try { types = a.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types; } catch (Exception) { continue; }
                    foreach (var ty in types)
                    {
                        if (ty == null || shapes >= 12) continue;
                        PropertyInfo def = null;
                        try { foreach (var pr in ty.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) if (pr.PropertyType.Name == "ItemDefinition") { def = pr; break; } } catch (Exception) { continue; }
                        if (def == null) continue;
                        shapes++;
                        var ms = "";
                        try { foreach (var pr in ty.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) ms += " " + pr.Name + ":" + pr.PropertyType.Name; } catch (Exception) { }
                        Out.Say("HELD holds an ItemDefinition: " + ty.FullName + " (" + a.GetName().Name + ") members:" + (ms.Length > 900 ? ms.Substring(0, 900) + "…" : ms));
                    }
                }
                if (shapes == 0) Out.Say("HELD no type holds an ItemDefinition");
                var pm = "";
                for (var at = t; at != null && at != typeof(object); at = at.BaseType)
                    foreach (var pr in at.GetProperties(flags | BindingFlags.DeclaredOnly))
                        if (pr.GetIndexParameters().Length == 0 && pr.PropertyType.Assembly == t.Assembly) pm += " " + pr.Name + ":" + pr.PropertyType.Name;
                Out.Say("HELD player properties of game types:" + (pm.Length > 1500 ? pm.Substring(0, 1500) + "…" : pm));
                return;
            }
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

    // The park items (Codex's catalogue): building plans marked 0x534B5001..08 in this order.
    public const int FirstMarker = 0x534B5001;
    public static readonly string[] ItemNames = { "Skate Bank", "Skate Kicker", "Skate Quarterpipe", "Skate Low Ledge", "Skate High Ledge", "Skate Long Ledge", "Skate Funbox", "Skate Halfpipe" };
    // The park kind of the held item, or -1.
    public static int HeldKind()
    {
        if (Marker >= FirstMarker && Marker < FirstMarker + ItemNames.Length) return Marker - FirstMarker;
        for (var i = 0; i < ItemNames.Length; i++) if (Name == ItemNames[i]) return i;
        return -1;
    }

    // The belt slot (0..5) holding the park item of that kind, or -1; Belt names what was seen.
    public static string Belt = "";
    public static int FindInBelt(object player, int kind)
    {
        Belt = "";
        try
        {
            var inv = Get(player, "inventory");
            var belt = inv == null ? null : Get(inv, "containerBelt");
            var list = belt == null ? null : Get(belt, "itemList");
            if (list == null) { Belt = "belt not readable"; return -1; }
            var found = -1;
            foreach (var it in Walk(list))
            {
                if (it == null) continue;
                var name = Get(it, "name") as string ?? "";
                var data = Get(it, "instanceData");
                var m = data == null ? null : Get(data, "dataInt");
                var marker = m is int ? (int)m : 0;
                var pos = Get(it, "position");
                var slot = pos is int ? (int)pos : -1;
                Belt += " " + slot + ":" + (name != "" ? name : "?") + (marker != 0 ? "#" + marker.ToString("X") : "");
                if (found < 0 && (marker == FirstMarker + kind || name == ItemNames[kind])) found = slot;
            }
            return found;
        }
        catch (Exception e) { Belt = "belt threw " + e.GetType().Name; return -1; }
    }

    // Makes that belt slot the active one through the game's own belt, the way a number key does,
    // so the game itself tells the server. Looked for by shape: a member of the player whose type has
    // a SelectSlot(int); the route taken is logged once.
    private static bool selectLooked; private static MethodInfo selectSlot; private static string selectOwner = "";
    public static bool SelectSlot(object player, int slot, out string how)
    {
        how = "";
        try
        {
            object owner = null;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            if (!selectLooked)
            {
                selectLooked = true;
                for (var at = player.GetType(); at != null && selectSlot == null; at = at.BaseType)
                {
                    foreach (var f in at.GetFields(flags | BindingFlags.DeclaredOnly))
                    {
                        var m = f.FieldType.GetMethod("SelectSlot", flags, null, new[] { typeof(int) }, null);
                        if (m != null) { selectSlot = m; selectOwner = "field " + f.Name; break; }
                    }
                    if (selectSlot != null) break;
                    foreach (var p in at.GetProperties(flags | BindingFlags.DeclaredOnly))
                    {
                        if (p.GetIndexParameters().Length != 0) continue;
                        var m = p.PropertyType.GetMethod("SelectSlot", flags, null, new[] { typeof(int) }, null);
                        if (m != null) { selectSlot = m; selectOwner = "property " + p.Name; break; }
                    }
                }
                Out.Say("HELD belt select route: " + (selectSlot == null ? "none found (no member with SelectSlot(int))" : selectOwner + "." + selectSlot.Name));
            }
            if (selectSlot == null) { how = "no belt select in this build"; return false; }
            var name = selectOwner.Substring(selectOwner.IndexOf(' ') + 1);
            owner = Get(player, name);
            if (owner == null) { how = selectOwner + " is empty"; return false; }
            selectSlot.Invoke(owner, new object[] { slot });
            how = selectOwner + ".SelectSlot(" + slot + ")";
            return true;
        }
        catch (Exception e) { how = "select threw " + e.GetType().Name + ": " + e.Message; return false; }
    }

    // A list the game keeps: walked as a list when it is one, else by Count and its indexer.
    private static System.Collections.Generic.IEnumerable<object> Walk(object list)
    {
        var e = list as System.Collections.IEnumerable;
        if (e != null) { foreach (var o in e) yield return o; yield break; }
        var t = list.GetType();
        var count = t.GetProperty("Count");
        var item = t.GetMethod("get_Item", new[] { typeof(int) });
        if (count == null || item == null) yield break;
        var n = (int)count.GetValue(list, null);
        for (var i = 0; i < n; i++) yield return item.Invoke(list, new object[] { i });
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
