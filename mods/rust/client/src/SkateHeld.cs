// What the player holds, for the park items: a building plan marked with instanceData.dataInt
// 0x534B5001..08 and named "Skate Bank" and so on. The client's types are renamed, so the item is
// looked for by its shape; when it cannot be read, nothing is assumed about it (no ghost). The HELD
// log lines name the members that could lead to it.
using System;
using System.Reflection;

public static class SkateHeld
{
    public static bool Reported;
    public static string Shortname = "", Name = "";
    public static int Marker;
    // The belt slot of the held item (its position), or -1.
    public static int Slot = -1;
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
                // Without a type named Item, the item type is one that holds an ItemDefinition (an
                // asset type, whose name is kept); those types and the player's members are logged.
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
        Shortname = ""; Name = ""; Marker = 0; Slot = -1;
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
            var pos = Get(it, "position");
            Slot = pos is int ? (int)pos : -1;
            return true;
        }
        catch (Exception) { return false; }
    }

    // The park items (Codex's catalogue): building plans marked 0x534B5001..0C in this order.
    public const int FirstMarker = 0x534B5001;
    public static readonly string[] ItemNames = { "Skate Bank", "Skate Kicker", "Skate Quarterpipe", "Skate Low Ledge", "Skate High Ledge", "Skate Long Ledge", "Skate Funbox", "Skate Halfpipe", "Skate Mini Quarterpipe", "Skate Large Quarterpipe", "Skate Half-wall Kicker", "Skate MEGA Halfpipe" };
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
            var inv = Get(player, "inventory") ?? OfType(player, "PlayerInventory");
            var belt = inv == null ? null : Get(inv, "containerBelt") ?? BeltByShape(inv);
            var list = belt == null ? null : Get(belt, "itemList") ?? OfType(belt, "List");
            if (list == null)
            {
                Belt = "belt not readable";
                if (!beltSaid)
                {
                    beltSaid = true;
                    Out.Say("HELD belt not readable: inventory " + (inv == null ? "missing; player " + Members(player) : "found, belt " + (belt == null ? "missing; inventory " + Members(inv) : "found, list missing; belt " + Members(belt))));
                }
                return -1;
            }
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

    private static void LookForBelt(object player)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        selectLooked = true;
        for (var at = player.GetType(); at != null && selectSlot == null; at = at.BaseType)
        {
            foreach (var f in at.GetFields(flags | BindingFlags.DeclaredOnly))
            {
                var m = f.FieldType.GetMethod("SelectSlot", flags, null, new[] { typeof(int) }, null);
                if (m != null) { selectSlot = m; selectMember = f; selectOwner = "field " + f.Name; break; }
            }
            if (selectSlot != null) break;
            foreach (var p in at.GetProperties(flags | BindingFlags.DeclaredOnly))
            {
                if (p.GetIndexParameters().Length != 0) continue;
                var m = p.PropertyType.GetMethod("SelectSlot", flags, null, new[] { typeof(int) }, null);
                if (m != null) { selectSlot = m; selectMember = p; selectOwner = "property " + p.Name; break; }
            }
        }
        Out.Say("HELD belt select route: " + (selectSlot == null ? "none found (no member with SelectSlot(int))" : selectOwner + "." + selectSlot.Name));
    }

    // Makes that belt slot the active one through the game's own belt, the way a number key does,
    // so the game itself tells the server. Looked for by shape: a member of the player whose type has
    // a SelectSlot(int); the route taken is logged once.
    private static bool selectLooked; private static MethodInfo selectSlot; private static MemberInfo selectMember; private static string selectOwner = "";
    public static bool SelectSlot(object player, int slot, out string how)
    {
        how = "";
        try
        {
            object owner = null;
            if (!selectLooked) LookForBelt(player);
            if (selectSlot == null) { how = "no belt select in this build"; return false; }
            // The member found, as declared: an inherited private one is not found again by name.
            var field = selectMember as FieldInfo;
            owner = field != null ? field.GetValue(player) : ((PropertyInfo)selectMember).GetValue(player, null);
            if (owner == null) { how = selectOwner + " is empty"; return false; }
            selectSlot.Invoke(owner, new object[] { slot });
            how = selectOwner + ".SelectSlot(" + slot + ")";
            return true;
        }
        catch (Exception e) { how = "select threw " + e.GetType().Name + ": " + e.Message; return false; }
    }

    // Puts away what the hand holds, as on a vehicle: nothing is built, swung or fired while riding.
    // The game's belt is asked to clear its selection (ClearSelection, else SelectSlot(-1)); the
    // route is logged once, and what still sits in the hand is logged each time it fails.
    private static bool clearLooked; private static MethodInfo clearSelection; private static string holsterRoute = "";
    public static bool Holster(object player, out string how)
    {
        how = "";
        if (player == null || !Read(player) || (Shortname == "" && Name == "")) return false;
        try
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            if (!selectLooked) LookForBelt(player);
            if (selectSlot == null) { how = "no belt in this build"; return false; }
            var field = selectMember as FieldInfo;
            var owner = field != null ? field.GetValue(player) : ((PropertyInfo)selectMember).GetValue(player, null);
            if (owner == null) { how = selectOwner + " is empty"; return false; }
            if (!clearLooked)
            {
                clearLooked = true;
                clearSelection = owner.GetType().GetMethod("ClearSelection", flags, null, Type.EmptyTypes, null);
                holsterRoute = clearSelection != null ? selectOwner + ".ClearSelection()" : selectOwner + ".SelectSlot(-1)";
                Out.Say("HELD holster route: " + holsterRoute);
            }
            if (clearSelection != null) clearSelection.Invoke(owner, null);
            else selectSlot.Invoke(owner, new object[] { -1 });
            how = holsterRoute + " put away " + (Name != "" ? Name : Shortname);
            return true;
        }
        catch (Exception e) { how = "holster threw " + e.GetType().Name + ": " + e.Message; return false; }
    }

    // The building plan's own turn (R), in degrees about up: the held planner's rotationOffset, as
    // the native target carries it to the server; 0 when it cannot be read.
    public static float PlannerTurn(object player)
    {
        try
        {
            if (player == null) return 0f;
            var held = player.GetType().GetMethod("GetHeldEntity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            var planner = held == null ? null : held.Invoke(player, null);
            var offset = planner == null ? null : Get(planner, "rotationOffset");
            return offset is UnityEngine.Vector3 ? ((UnityEngine.Vector3)offset).y : 0f;
        }
        catch (Exception) { return 0f; }
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

    private static bool beltSaid;

    // The first member of o whose type name starts with that name: the way in when a build renames it.
    private static object OfType(object o, string typeName)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        for (var t = o.GetType(); t != null; t = t.BaseType)
        {
            foreach (var p in t.GetProperties(flags | BindingFlags.DeclaredOnly))
                if (p.GetIndexParameters().Length == 0 && p.PropertyType.Name.StartsWith(typeName)) { try { var v = p.GetValue(o, null); if (v != null) return v; } catch (Exception) { } }
            foreach (var f in t.GetFields(flags | BindingFlags.DeclaredOnly))
                if (f.FieldType.Name.StartsWith(typeName)) { var v = f.GetValue(o); if (v != null) return v; }
        }
        return null;
    }

    // The belt among the inventory's item containers: the one six items wide.
    private static object BeltByShape(object inv)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        foreach (var p in inv.GetType().GetProperties(flags))
        {
            if (p.GetIndexParameters().Length != 0 || p.PropertyType.Name != "ItemContainer") continue;
            object c; try { c = p.GetValue(inv, null); } catch (Exception) { continue; }
            if (c != null && Get(c, "capacity") is int && (int)Get(c, "capacity") == 6) return c;
        }
        return null;
    }

    // What o holds, for the log: member name and type, at most forty.
    private static string Members(object o)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var s = o.GetType().Name + ":"; var n = 0;
        foreach (var p in o.GetType().GetProperties(flags)) { if (n++ >= 40) break; s += " " + p.Name + ":" + p.PropertyType.Name; }
        return s;
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
