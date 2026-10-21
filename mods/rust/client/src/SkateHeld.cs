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

    private static Planner pausedPlanner;
    private static bool plannerWasEnabled;
    private static object plannerPlayer;
    // Wheel/builder previews are a separate owner from the holstered riding models.
    private static readonly System.Collections.Generic.List<UnityEngine.GameObject> plannerGuides = new System.Collections.Generic.List<UnityEngine.GameObject>();
    public static bool PausePlanner(object player, out string why)
    {
        why = "";
        if (pausedPlanner != null && ReferenceEquals(plannerPlayer, player) && HasPlanner(player))
        { Guides(pausedPlanner, false, plannerGuides, true); return true; }
        if (pausedPlanner != null) ResumePlanner();
        var root = player as UnityEngine.Component;
        if (root == null) { why = "The local player is unavailable."; return false; }
        var model = RiderRig.Bound && RiderRig.Root != null ? (UnityEngine.Component)RiderRig.Root : root;
        var planners = model.GetComponentsInChildren<Planner>(false);
        Planner found = null;
        for (int i = 0; i < planners.Length; i++)
        {
            Planner candidate = planners[i];
            if (candidate == null || !candidate.enabled || !candidate.gameObject.activeInHierarchy) continue;
            var parent = candidate.transform.parent;
            if (parent == null || !parent.name.EndsWith("_prop", StringComparison.Ordinal)) continue;
            if (found != null) { why = "More than one active building plan was found."; return false; }
            found = candidate;
        }
        if (found == null) { why = "The active building plan is unavailable."; return false; }
        plannerWasEnabled = found.enabled;
        pausedPlanner = found;
        plannerPlayer = player;
        found.enabled = false;
        Guides(found, false, plannerGuides, true);
        Out.Say("PIECES ordinary building plan paused on local hand");
        return true;
    }

    public static bool HasPlanner(object player)
    {
        if (pausedPlanner != null)
        {
            var parent = pausedPlanner.transform.parent;
            return ReferenceEquals(plannerPlayer, player) && pausedPlanner.gameObject.activeInHierarchy && parent != null && parent.name.EndsWith("_prop", StringComparison.Ordinal);
        }
        var root = player as UnityEngine.Component;
        if (root == null) return false;
        var model = RiderRig.Bound && RiderRig.Root != null ? (UnityEngine.Component)RiderRig.Root : root;
        var planners = model.GetComponentsInChildren<Planner>(false);
        int found = 0;
        for (int i = 0; i < planners.Length; i++)
        {
            var p = planners[i];
            if (p == null || !p.enabled || !p.gameObject.activeInHierarchy) continue;
            var parent = p.transform.parent;
            if (parent != null && parent.name.EndsWith("_prop", StringComparison.Ordinal)) found++;
        }
        return found == 1;
    }

    public static void ResumePlanner()
    {
        var planner = pausedPlanner;
        pausedPlanner = null;
        plannerPlayer = null;
        for (var i = plannerGuides.Count - 1; i >= 0; i--)
        {
            try
            {
                var go = plannerGuides[i];
                if (go != null && !go.activeSelf) go.SetActive(true);
                plannerGuides.RemoveAt(i);
            }
            catch (Exception e) { Out.Say("PIECES building preview restore unavailable: " + e.GetType().Name); }
        }
        try { if (planner != null && plannerWasEnabled && !planner.enabled) planner.enabled = true; }
        catch (Exception e) { Out.Say("PIECES building plan restore unavailable: " + e.GetType().Name); }
        plannerWasEnabled = false;
    }

    // Resolve only the normal local routes while off the board. Do not select, hide or survey the scene.
    public static void Prepare(object player)
    {
        if (player == null) return;
        Read(player);
        if (!selectLooked) LookForBelt(player);
        var root = player as UnityEngine.Component;
        var model = RiderRig.Bound && RiderRig.Root != null ? (UnityEngine.Component)RiderRig.Root : root;
        if (model != null) model.GetComponentsInChildren<HeldEntity>(false);
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
            if (selectSlot == null) return Hide(player, out how);
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

    // Without a belt to clear (this build: no member of the player has a SelectSlot), the held
    // entity itself is switched off on this client while riding: its model, its view model and a
    // building plan's ghost go with it, and a switched-off plan takes no clicks. Each one is
    // switched on again on stepping off (Show). What the held entity offers is logged once a type.
    private static readonly System.Collections.Generic.List<UnityEngine.GameObject> hidden = new System.Collections.Generic.List<UnityEngine.GameObject>();
    private static readonly System.Collections.Generic.HashSet<string> probed = new System.Collections.Generic.HashSet<string>();
    private static bool Hide(object player, out string how)
    {
        how = "";
        var root = player as UnityEngine.Component;
        if (root == null) { how = "the player is not a component"; return false; }
        // This client's member names are obfuscated (its type names are kept), and nothing on the
        // player returns its held entity; so the held entity is found by its type among the
        // player's children. (The first-person view model is RiderRig's: it hides it from the
        // chase camera.)
        // The held entity sits on a hand bone (r_prop, l_prop) of the rider's model, which is not
        // under the player (the log: a building planner under r_prop in player_model.prefab);
        // what hangs on the back (a gun under spine3) is left alone.
        var model = RiderRig.Bound && RiderRig.Root != null ? (UnityEngine.Component)RiderRig.Root : root;
        var off = 0; var names = "";
        var heldOnes = model.GetComponentsInChildren<HeldEntity>(false);
        for (var i = 0; i < heldOnes.Length; i++)
        {
            var c = heldOnes[i];
            if (c == null) continue;
            var bone = c.transform.parent;
            if (bone == null || !bone.name.EndsWith("_prop", StringComparison.Ordinal)) continue;
            var go = c.gameObject;
            if (go == null || !go.activeSelf) continue;
            var kind = c.GetType().Name;
            var first = probed.Add(kind);
            if (first) Out.Say("HELD switching off a " + kind + " (" + go.name + ") while riding");
            go.SetActive(false);
            hidden.Add(go);
            names += " " + kind + " (" + go.name + ")" + Guides(c, first);
            off++;
        }
        // A scene-wide survey is diagnostic work, not part of every first boarding.
        if (off == 0) return false;
        how = "switched off" + names;
        return true;
    }

    private static bool Derives(Type t, string name)
    {
        for (; t != null && t != typeof(object); t = t.BaseType) if (t.Name == name) return true;
        return false;
    }

    // A building plan switched off on the hand bone still leaves its red preview standing in front
    // of the rider (the Shadow test of 18 October): that preview is not under the plan, or it would
    // have gone off with it. So what the plan refers to is read by shape, with its member names
    // obfuscated: every member whose type is a scene object (a GameObject, or a component) and
    // whose object is not under the plan. One named like a guide is switched off with the plan and
    // comes back with it (Show). The first plan of a type logs those bounded local references;
    // normal holstering does not survey every renderer in the scene.
    private static string Guides(UnityEngine.Component plan, bool log)
    { return Guides(plan, log, hidden, false); }

    private static string Guides(UnityEngine.Component plan, bool log, System.Collections.Generic.List<UnityEngine.GameObject> owned, bool plannerReferences)
    {
        var names = ""; var line = ""; var n = 0;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        try
        {
            for (var t = plan.GetType(); t != null && t != typeof(object) && n < 24; t = t.BaseType)
            {
                // Fields and properties of a drawn object's type only: a game property of another
                // type could do work in its getter, and nothing of the plan is to be set off here.
                var members = new System.Collections.Generic.List<MemberInfo>();
                foreach (var p in t.GetProperties(flags))
                    if (p.GetIndexParameters().Length == 0 && (Drawn(p.PropertyType) ||
                        (plannerReferences && t == typeof(Planner) && p.PropertyType == typeof(Il2CppSystem.Object) &&
                         t.GetField("NativeFieldInfoPtr_" + p.Name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static) != null))) members.Add(p);
                foreach (var f in t.GetFields(flags)) if (Drawn(f.FieldType)) members.Add(f);
                foreach (var p in members)
                {
                    if (n >= 24) break;
                    object v;
                    try { v = p is PropertyInfo ? ((PropertyInfo)p).GetValue(plan, null) : ((FieldInfo)p).GetValue(plan); } catch (Exception) { continue; }
                    var o = v as UnityEngine.Object;
                    // This build erases Planner's scene references to Object. Only its field-backed
                    // getters are admitted above; TryCast checks the live IL2CPP class, not a guessed ABI.
                    if (plannerReferences && ReferenceEquals(o, null))
                    {
                        var native = v as Il2CppSystem.Object;
                        if (!ReferenceEquals(native, null))
                        { try { o = native.TryCast<UnityEngine.GameObject>(); if (o == null) o = native.TryCast<UnityEngine.Component>(); } catch (Exception) { continue; } }
                    }
                    if (ReferenceEquals(o, null) || o == null) continue;
                    var go = o as UnityEngine.GameObject;
                    var part = o as UnityEngine.Component;
                    if (ReferenceEquals(go, null) && !ReferenceEquals(part, null) && part != null) go = part.gameObject;
                    n++;
                    var typeName = p is PropertyInfo ? ((PropertyInfo)p).PropertyType.Name : ((FieldInfo)p).FieldType.Name;
                    if (ReferenceEquals(go, null) || go == null) { line += " " + p.Name + ":" + typeName + "=" + o.name; continue; }
                    var outside = !go.transform.IsChildOf(plan.transform);
                    line += " " + p.Name + ":" + typeName + "=" + go.name + (go.activeInHierarchy ? "" : ",off") + (outside ? ",outside" : "");
                    if (outside && (!plannerReferences || !plan.transform.IsChildOf(go.transform)) && go.activeSelf && GuideLike(go.name))
                    {
                        if (plannerReferences)
                        {
                            // Record before changing it, so even a throwing setter can be restored.
                            if (!owned.Contains(go)) owned.Add(go);
                        }
                        go.SetActive(false);
                        if (!plannerReferences) owned.Add(go);
                        names += " and its " + go.name;
                    }
                }
            }
            if (log) Out.Say("HELD the " + plan.GetType().Name + " refers to" + (line == "" ? " no scene object" : ":" + Short(line, 1200)));
        }
        catch (Exception e) { if (log) Out.Say("HELD reading the " + plan.GetType().Name + " threw: " + e.GetType().Name + ": " + e.Message); }
        return names;
    }

    private static bool Drawn(Type t)
    {
        return t == typeof(UnityEngine.GameObject) || typeof(UnityEngine.Transform).IsAssignableFrom(t) || typeof(UnityEngine.Renderer).IsAssignableFrom(t);
    }

    private static bool GuideLike(string name)
    {
        return name.IndexOf("guide", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("preview", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("ghost", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("placeholder", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // What is drawn within 40 m of the rider under a guide-like name: once, for the log.
    private static void NearbyGuides(UnityEngine.Vector3 at)
    {
        try
        {
            var all = UnityEngine.Object.FindObjectsOfType<UnityEngine.Renderer>();
            var line = ""; var n = 0;
            var seen = new System.Collections.Generic.HashSet<string>();
            for (var i = 0; i < all.Length && n < 10; i++)
            {
                var r = all[i];
                if (ReferenceEquals(r, null) || r == null) continue;
                var go = r.gameObject;
                if (!GuideLike(go.name) && go.name.IndexOf("planner", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var d = (go.transform.position - at).magnitude;
                if (d > 40f) continue;
                var root = go.transform.root;
                var key = root.name + "/" + go.name;
                if (!seen.Add(key)) continue;
                var material = "";
                try { var m = r.sharedMaterial; if (!ReferenceEquals(m, null) && m != null) material = ", " + m.name; } catch (Exception) { }
                line += " " + go.name + (root == go.transform ? "" : " in " + root.name) + " " + d.ToString("F1") + " m" + (r.enabled ? "" : ", off") + material + ";";
                n++;
            }
            Out.Say("HELD drawn near the rider under a guide-like name (" + all.Length + " renderers looked at):" + (line == "" ? " (none)" : Short(line, 1200)));
        }
        catch (Exception e) { Out.Say("HELD looking for guides threw: " + e.GetType().Name + ": " + e.Message); }
    }

    // Where the belt could be when nothing on the player is belt-like (this build: member names
    // obfuscated, type names kept; the log said "belt-like members: (none)"): every loaded game
    // type named like a belt, with what it offers by shape, logged once. A static int can carry
    // the selected slot; a method taking one int can be the selection; a component's instances
    // in the scene are counted and the first one's ints read. Nothing is called.
    private static bool surveyed;
    public static void SurveyBelt()
    {
        if (surveyed) return;
        surveyed = true;
        try
        {
            var found = 0;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var an = a.GetName().Name;
                if (a == typeof(SkateHeld).Assembly || an.StartsWith("System", StringComparison.Ordinal) || an.StartsWith("mscorlib", StringComparison.Ordinal) || an.StartsWith("netstandard", StringComparison.Ordinal) ||
                    an.StartsWith("BepInEx", StringComparison.Ordinal) || an.StartsWith("Il2CppInterop", StringComparison.Ordinal) || an.StartsWith("Il2Cppmscorlib", StringComparison.Ordinal) ||
                    an.StartsWith("UnityEngine", StringComparison.Ordinal) || an.StartsWith("Microsoft", StringComparison.Ordinal) || an.StartsWith("Mono.", StringComparison.Ordinal)) continue;
                Type[] types;
                try { types = a.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types; } catch (Exception) { continue; }
                foreach (var t in types)
                {
                    if (t == null || found >= 8 || t.Name.IndexOf("Belt", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    found++;
                    Out.Say("HELD belt survey " + found + ": " + Short(Shape(t, an), 1500));
                }
            }
            if (found == 0) Out.Say("HELD belt survey: no loaded game type is named like a belt");
        }
        catch (Exception e) { Out.Say("HELD belt survey threw: " + e.GetType().Name + ": " + e.Message); }
    }

    // One type's shape for the log: its base, its static ints with their values, its instance ints
    // (with the first scene instance's values when it is a component), and its methods taking
    // one int.
    private static string Shape(Type t, string assembly)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var s = t.FullName + " (" + assembly + ")" + (t.BaseType == null ? "" : " : " + t.BaseType.Name) + (t.IsAbstract ? " abstract" : "");
        object instance = null; var instances = -1;
        if (typeof(UnityEngine.Object).IsAssignableFrom(t) && !t.IsAbstract && !t.ContainsGenericParameters)
        {
            try
            {
                foreach (var m in typeof(UnityEngine.Object).GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != "FindObjectsOfType" || !m.IsGenericMethodDefinition || m.GetParameters().Length != 0) continue;
                    var found = m.MakeGenericMethod(t).Invoke(null, null) as System.Collections.IEnumerable;
                    if (found == null) continue;
                    instances = 0;
                    foreach (var o in found) { if (o != null && instance == null) instance = o; instances++; }
                    break;
                }
            }
            catch (Exception e) { s += " [instances: " + e.GetType().Name + "]"; }
            if (instances >= 0) s += " [" + instances + " in the scene]";
        }
        var statics = ""; var ints = ""; var methods = "";
        // The game's own types only, not the engine's bases under them.
        for (var at = t; at != null && at.Assembly == t.Assembly; at = at.BaseType)
        {
            foreach (var p in at.GetProperties(all))
            {
                if (p.GetIndexParameters().Length != 0 || p.PropertyType != typeof(int)) continue;
                var getter = p.GetGetMethod(true);
                if (getter == null) continue;
                if (getter.IsStatic) statics += " " + p.Name + "=" + Value(p, null);
                else ints += " " + p.Name + (instance == null ? "" : "=" + Value(p, instance));
            }
            foreach (var f in at.GetFields(all))
            {
                if (f.FieldType != typeof(int)) continue;
                if (f.IsStatic) { try { statics += " " + f.Name + "=" + f.GetValue(null); } catch (Exception) { statics += " " + f.Name + "=?"; } }
                else ints += " " + f.Name + (instance == null ? "" : "=" + Value(f, instance));
            }
            foreach (var m in at.GetMethods(all))
            {
                if (m.IsSpecialName) continue;
                var ps = m.GetParameters();
                if (ps.Length != 1 || ps[0].ParameterType != typeof(int)) continue;
                methods += " " + (m.IsStatic ? "static " : "") + m.ReturnType.Name + " " + m.Name + "(int)";
            }
        }
        return s + "; static ints:" + (statics == "" ? " (none)" : statics) + "; ints:" + (ints == "" ? " (none)" : ints) + "; methods taking one int:" + (methods == "" ? " (none)" : methods);
    }

    private static string Value(MemberInfo m, object o)
    {
        try
        {
            var p = m as PropertyInfo;
            var v = p != null ? p.GetValue(o, null) : ((FieldInfo)m).GetValue(o);
            return v == null ? "null" : v.ToString();
        }
        catch (Exception e) { return "?" + e.GetType().Name; }
    }

    private static string Short(string s, int most) { return s.Length > most ? s.Substring(0, most) + "…" : s; }

    // Stepping off: every held entity switched off for the ride is switched on again.
    public static int Show()
    {
        var n = 0;
        foreach (var go in hidden) if (go != null && !go.activeSelf) { go.SetActive(true); n++; }
        hidden.Clear();
        return n;
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
