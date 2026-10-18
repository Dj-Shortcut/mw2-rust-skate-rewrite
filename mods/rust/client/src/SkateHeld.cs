using System;
using System.Reflection;
using Shortcut.RustMod;

public static class SkateHeld
{
    public static bool Reported;
    public static string Shortname = "", Name = "";
    public static int Marker;
    // The belt slot of the held item (its position), or -1.
    public static int Slot = -1;
    private static ulong skin;
    private static int amount;
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
        Shortname = ""; Name = ""; Marker = 0; Slot = -1; skin = ulong.MaxValue; amount = 0;
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
            var itemSkin = Get(it, "skin");
            var itemAmount = Get(it, "amount");
            skin = itemSkin is ulong ? (ulong)itemSkin : ulong.MaxValue;
            amount = itemAmount is int ? (int)itemAmount : 0;
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
        SkateParkKind kind;
        return SkateParkBuildItems.TryIdentity(Shortname, skin, Marker, Name, amount, out kind) ? (int)kind : -1;
    }

    // The belt slot (0..5) holding the park item of that kind, or -1; Belt names what was seen.
    public static string Belt = "";
    public static int FindInBelt(object player, int kind)
    {
        Belt = "";
        if (player == null || kind < 0 || kind >= SkateParkBuildItems.PieceCount) return -1;
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
                int slot; string seen;
                var match = IsKind(it, kind, out slot, out seen);
                Belt += " " + seen;
                if (found < 0 && match && slot >= 0 && slot < 6) found = slot;
            }
            return found;
        }
        catch (Exception e) { Belt = "belt threw " + e.GetType().Name; return -1; }
    }

    // One inventory item read as a park build item of this kind (by the shared identity rules).
    private static bool IsKind(object it, int kind, out int slot, out string seen)
    {
        var name = Get(it, "name") as string ?? "";
        var data = Get(it, "instanceData");
        var m = data == null ? null : Get(data, "dataInt");
        var marker = m is int ? (int)m : 0;
        var pos = Get(it, "position");
        slot = pos is int ? (int)pos : -1;
        seen = slot + ":" + (name != "" ? name : "?") + (marker != 0 ? "#" + marker.ToString("X") : "");
        var info = Get(it, "info");
        var carrier = info == null ? null : Get(info, "shortname") as string;
        var itemSkin = Get(it, "skin");
        var itemAmount = Get(it, "amount");
        SkateParkKind observed;
        return itemSkin is ulong && itemAmount is int &&
            SkateParkBuildItems.TryIdentity(carrier, (ulong)itemSkin, marker, name, (int)itemAmount, out observed) &&
            (int)observed == kind;
    }

    // Whether the main inventory (outside the belt) holds this park item: 1 yes, 0 no, -1 not readable.
    public static int InMain(object player, int kind)
    {
        if (player == null || kind < 0 || kind >= SkateParkBuildItems.PieceCount) return -1;
        try
        {
            var inv = Get(player, "inventory") ?? OfType(player, "PlayerInventory");
            var main = inv == null ? null : Get(inv, "containerMain");
            var list = main == null ? null : Get(main, "itemList");
            if (list == null) return -1;
            foreach (var it in Walk(list))
            {
                int slot; string seen;
                if (it != null && IsKind(it, kind, out slot, out seen)) return 1;
            }
            return 0;
        }
        catch (Exception) { return -1; }
    }

    private static void LookForBelt(object player)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        selectLooked = true;
        var seen = "";
        for (var at = player.GetType(); at != null && selectSlot == null; at = at.BaseType)
        {
            foreach (var f in at.GetFields(flags | BindingFlags.DeclaredOnly))
            {
                var m = SlotSelector(f.FieldType);
                if (BeltLike(f.Name, f.FieldType)) seen += " field " + f.Name + ":" + f.FieldType.Name + Slotty(f.FieldType);
                if (m != null) { selectSlot = m; selectMember = f; selectOwner = "field " + f.Name; break; }
            }
            if (selectSlot != null) break;
            foreach (var p in at.GetProperties(flags | BindingFlags.DeclaredOnly))
            {
                if (p.GetIndexParameters().Length != 0) continue;
                if (BeltLike(p.Name, p.PropertyType)) seen += " property " + p.Name + ":" + p.PropertyType.Name + Slotty(p.PropertyType);
                var m = SlotSelector(p.PropertyType);
                if (m != null) { selectSlot = m; selectMember = p; selectOwner = "property " + p.Name; break; }
            }
        }
        Out.Say("HELD belt select route: " + (selectSlot == null ? "none found (no member with SelectSlot(int, ...)); belt-like members:" + (seen == "" ? " (none)" : seen) : selectOwner + "." + selectSlot.Name + "(" + selectSlot.GetParameters().Length + " args)"));
    }

    // A member worth naming in the log: belt by name, or a type with slot methods.
    private static bool BeltLike(string name, Type t)
    {
        if (name.IndexOf("Belt", StringComparison.OrdinalIgnoreCase) >= 0 || t.Name.IndexOf("Belt", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (t.IsPrimitive || t == typeof(string)) return false;
        return Slotty(t).IndexOf("Slot", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // A SelectSlot whose first argument is the slot and whose others can be left at their defaults.
    private static MethodInfo SlotSelector(Type t)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        foreach (var m in t.GetMethods(flags))
        {
            if (m.Name != "SelectSlot") continue;
            var ps = m.GetParameters();
            if (ps.Length == 0 || ps[0].ParameterType != typeof(int)) continue;
            var rest = true;
            for (var i = 1; i < ps.Length; i++) if (!ps[i].IsOptional && !ps[i].ParameterType.IsValueType) rest = false;
            if (rest) return m;
        }
        return null;
    }

    // The slot and selection methods of a type, for the log.
    private static string Slotty(Type t)
    {
        var s = "";
        try
        {
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                if (m.Name.IndexOf("Slot", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("Select", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("Active", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var args = "";
                    foreach (var pa in m.GetParameters()) args += (args == "" ? "" : ",") + pa.ParameterType.Name;
                    s += " " + m.Name + "(" + args + ")";
                }
        }
        catch (Exception) { }
        return s == "" ? "" : " [" + s.Trim() + "]";
    }

    // The arguments for the belt's SelectSlot: the slot, then each other argument's default.
    private static object[] SlotArgs(int slot)
    {
        var ps = selectSlot.GetParameters();
        var args = new object[ps.Length];
        args[0] = slot;
        for (var i = 1; i < ps.Length; i++) args[i] = ps[i].IsOptional ? ps[i].DefaultValue : Activator.CreateInstance(ps[i].ParameterType);
        return args;
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
            selectSlot.Invoke(owner, SlotArgs(slot));
            how = selectOwner + ".SelectSlot(" + slot + ")";
            return true;
        }
        catch (Exception e) { how = "select threw " + e.GetType().Name + ": " + e.Message; return false; }
    }

    // Puts away what the hand holds, as on a vehicle: nothing is built, swung or fired while riding.
    // The game's belt is asked to clear its selection (ClearSelection, else SelectSlot(-1)); the
    // route is logged once, and what still sits in the hand is logged each time it fails.
    private static bool clearLooked; private static MethodInfo clearSelection; private static string holsterRoute = "";
    // This client build has no GetActiveItem (the log: "HELD methods returning Item: (none)"), so
    // the held item cannot be read by name; then the belt's own selected slot says what is held.
    public static bool Holster(object player, out string how)
    {
        how = "";
        if (player == null) return false;
        var named = Read(player);
        if (named && Shortname == "" && Name == "") return false;
        try
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            if (!selectLooked) LookForBelt(player);
            if (selectSlot == null) { how = "no belt in this build"; return false; }
            var field = selectMember as FieldInfo;
            var owner = field != null ? field.GetValue(player) : ((PropertyInfo)selectMember).GetValue(player, null);
            if (owner == null) { how = selectOwner + " is empty"; return false; }
            if (!named)
            {
                var selected = SelectedSlot(owner);
                if (selected == NotReadable) { how = "neither the held item nor the belt's selected slot can be read"; return false; }
                if (selected < 0 || selected >= 6) return false;
                Slot = selected; Name = "belt slot " + (selected + 1);
            }
            if (!clearLooked)
            {
                clearLooked = true;
                clearSelection = owner.GetType().GetMethod("ClearSelection", flags, null, Type.EmptyTypes, null);
                holsterRoute = clearSelection != null ? selectOwner + ".ClearSelection()" : selectOwner + ".SelectSlot(-1)";
                Out.Say("HELD holster route: " + holsterRoute);
            }
            if (clearSelection != null) clearSelection.Invoke(owner, null);
            else selectSlot.Invoke(owner, SlotArgs(-1));
            how = holsterRoute + " put away " + (Name != "" ? Name : Shortname);
            return true;
        }
        catch (Exception e) { how = "holster threw " + e.GetType().Name + ": " + e.Message; return false; }
    }

    // The belt's selected slot (0..5, -1 for none) as the belt keeps it: a member named like
    // SelectedSlot, static or not; NotReadable when the belt has none. The route is logged once.
    public const int NotReadable = int.MinValue;
    private static bool slotLooked; private static MemberInfo slotMember;
    private static int SelectedSlot(object belt)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        if (!slotLooked)
        {
            slotLooked = true;
            var t = belt.GetType();
            foreach (var name in new[] { "SelectedSlot", "selectedSlot", "_selectedSlot", "currentSlot" })
            {
                var p = t.GetProperty(name, all);
                if (p != null && p.PropertyType == typeof(int) && p.GetIndexParameters().Length == 0) { slotMember = p; break; }
                var f = t.GetField(name, all);
                if (f != null && f.FieldType == typeof(int)) { slotMember = f; break; }
            }
            var ints = "";
            if (slotMember == null) foreach (var m in t.GetMembers(all))
                {
                    var pi = m as PropertyInfo; var fi = m as FieldInfo;
                    if ((pi != null && pi.PropertyType == typeof(int) && pi.GetIndexParameters().Length == 0) || (fi != null && fi.FieldType == typeof(int))) ints += " " + m.Name;
                }
            Out.Say("HELD belt selected slot: " + (slotMember != null ? t.Name + "." + slotMember.Name : "none found; int members of " + t.Name + ":" + (ints == "" ? " (none)" : ints)));
        }
        if (slotMember == null) return NotReadable;
        var target = belt;
        var sp = slotMember as PropertyInfo; var sf = slotMember as FieldInfo;
        if ((sp != null && sp.GetGetMethod(true).IsStatic) || (sf != null && sf.IsStatic)) target = null;
        var v = sp != null ? sp.GetValue(target, null) : sf.GetValue(target);
        return v is int ? (int)v : NotReadable;
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
