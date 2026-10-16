using System;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Security.Cryptography;
using Shortcut.RustMod;

// Owner-approved skate_park_build_items. This consumes an actual compiled shared/plugin
// DLL; it does not copy the helper or construct fake native Items, Planners or Unity objects.
internal static class SkateParkBuildItemScenarios
{
    private static int checks;
    private static readonly SkateParkKind[] Kinds = {
        SkateParkKind.Bank, SkateParkKind.Kicker, SkateParkKind.QuarterPipe,
        SkateParkKind.LedgeLow, SkateParkKind.LedgeHigh, SkateParkKind.LongLedge,
        SkateParkKind.Funbox, SkateParkKind.Halfpipe,
        SkateParkKind.MiniQuarterPipe, SkateParkKind.LargeQuarterPipe,
        SkateParkKind.HalfWallKicker, SkateParkKind.MegaHalfpipe
    };
    private static readonly string[] Names = {
        "Skate Bank", "Skate Kicker", "Skate Quarterpipe", "Skate Low Ledge",
        "Skate High Ledge", "Skate Long Ledge", "Skate Funbox", "Skate Halfpipe",
        "Skate Mini Quarterpipe", "Skate Large Quarterpipe", "Skate Half-wall Kicker", "Skate MEGA Halfpipe"
    };
    private static readonly int[] Markers = {
        0x534B5001, 0x534B5002, 0x534B5003, 0x534B5004,
        0x534B5005, 0x534B5006, 0x534B5007, 0x534B5008,
        0x534B5009, 0x534B500A, 0x534B500B, 0x534B500C
    };
    private static readonly int[] OriginalParts = { 1, 1, 2, 1, 1, 3, 3, 36, 2, 3, 1, 80 };
    private static readonly int[] KindIds = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
    private static readonly int[] InvalidKinds = { int.MinValue, -100, -1, 12, 255, int.MaxValue };
    private static readonly int[] FarMarkers = { int.MinValue, -1, 0, 1, int.MaxValue };
    private static readonly int[] Amounts = { int.MinValue, -2, -1, 0, 1, 2, 3, 8, int.MaxValue };

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Identity(bool expected, SkateParkKind expectedKind, string carrier,
                                 ulong skin, int marker, string name, int amount, string label)
    {
        var result = SkateParkKind.Halfpipe;
        bool accepted = SkateParkBuildItems.TryIdentity(carrier, skin, marker, name, amount, out result);
        Check(accepted == expected, label + " decision");
        Check(result == (expected ? expectedKind : default(SkateParkKind)), label + " output");
    }

    private static void CatalogAndTagBoundaries()
    {
        Check(SkateParkBuildItems.Carrier == "building.planner", "native plan carrier literal");
        Check(SkateParkBuildItems.MarkerBase == 0x534B5000, "reserved marker base literal");
        Check(SkateParkBuildItems.PieceCount == 12, "marker index bound includes reserved slots");
        Check(typeof(SkateParkBuildItems).Assembly == typeof(SkateParkLayout).Assembly,
              "item and layout contracts come from the same actual library");
        for (int i = 0; i < Kinds.Length; i++)
        {
            Check((int)Kinds[i] == KindIds[i], "stable kind identity " + i);
            Check(SkateParkBuildItems.Name(Kinds[i]) == Names[i], "English item name " + i);
            Check(SkateParkBuildItems.Marker(Kinds[i]) == Markers[i], "independent item marker " + i);
            Check(SkateParkLayout.PartCount(Kinds[i]) == OriginalParts[i], "original member count " + i);
            Check(SkateParkBuildItems.Tagged(Markers[i]), "known marker tagged " + i);
            var kind = SkateParkKind.Halfpipe;
            Check(SkateParkBuildItems.TryKind(Markers[i], out kind) && kind == Kinds[i],
                  "known marker resolves its literal kind " + i);
            Identity(true, Kinds[i], "building.planner", 0, Markers[i], Names[i], 1, "exact item " + i);
        }

        for (int marker = 0x534B4FFC; marker <= 0x534B5103; marker++)
        {
            bool tagged = marker >= 0x534B5000 && marker <= 0x534B50FF;
            Check(SkateParkBuildItems.Tagged(marker) == tagged, "tag boundary " + marker);
            var kind = SkateParkKind.Halfpipe;
            int known = Array.IndexOf(Markers, marker);
            bool valid = known >= 0;
            Check(SkateParkBuildItems.TryKind(marker, out kind) == valid, "marker gap decision " + marker);
            Check(kind == (valid ? Kinds[known] : default(SkateParkKind)),
                  "marker gap output " + marker);
            if (!valid)
                for (int i = 0; i < Kinds.Length; i++)
                    Identity(false, Kinds[i], "building.planner", 0, marker, Names[i], 1,
                             "unrecognized tagged/adjacent marker " + marker + " name " + i);
        }
        foreach (int marker in FarMarkers)
        {
            Check(!SkateParkBuildItems.Tagged(marker), "far marker not tagged " + marker);
            var kind = SkateParkKind.Halfpipe;
            Check(!SkateParkBuildItems.TryKind(marker, out kind) && kind == default(SkateParkKind),
                  "far marker refuses atomically " + marker);
            Identity(false, SkateParkKind.Bank, "building.planner", 0, marker, Names[0], 1,
                     "far identity marker " + marker);
        }
        foreach (int raw in InvalidKinds)
        {
            var kind = (SkateParkKind)raw;
            Check(SkateParkBuildItems.Name(kind) == null, "unknown kind has no item name " + raw);
            Check(SkateParkBuildItems.Marker(kind) == 0, "unknown kind has no item marker " + raw);
            Check(SkateParkLayout.PartCount(kind) == 0, "unknown kind has no recoverable layout " + raw);
        }
    }

    private static void IdentityRefusals()
    {
        string[] carriers = { null, "", "Building.planner", "building.Planner", "building.planner ",
                              " building.planner", "building.planner\0", "building.planner\n", "hammer", "wood" };
        ulong[] skins = { 1, 2, 0x8000000000000000UL, ulong.MaxValue };
        for (int i = 0; i < Kinds.Length; i++)
        {
            foreach (string carrier in carriers)
                Identity(false, Kinds[i], carrier, 0, Markers[i], Names[i], 1, "wrong carrier " + i);
            foreach (ulong skin in skins)
                Identity(false, Kinds[i], "building.planner", skin, Markers[i], Names[i], 1,
                         "nondefault skin " + i + "/" + skin);
            foreach (int amount in Amounts)
                Identity(amount == 1, Kinds[i], "building.planner", 0, Markers[i], Names[i], amount,
                         "single-item amount " + i + "/" + amount);

            string[] names = { null, "", Names[i].ToUpperInvariant(), Names[i].ToLowerInvariant(),
                               " " + Names[i], Names[i] + " ", Names[i] + "\0", Names[i] + "\n",
                               Names[i] + "\u2028", Names[i] + "\u2029", "Skate Ban\u212A" };
            foreach (string name in names)
                Identity(false, Kinds[i], "building.planner", 0, Markers[i], name, 1,
                         "ordinal English name " + i);
            for (int other = 0; other < Kinds.Length; other++)
                Identity(other == i, Kinds[i], "building.planner", 0, Markers[i], Names[other], 1,
                         "crossed item name " + i + "/" + other);

            // Reject several simultaneous faults without leaking a partially parsed kind.
            Identity(false, Kinds[i], "hammer", ulong.MaxValue, Markers[i], Names[i], int.MaxValue,
                     "combined invalid fields " + i);
            Identity(false, Kinds[i], "building.planner", 0, 0x534B50FF, Names[i], 1,
                     "reserved future marker with legitimate name " + i);
        }
    }

    private static void VerifiedSingleDebit()
    {
        int successes = 0;
        foreach (int before in Amounts)
            foreach (int after in Amounts)
                foreach (bool sameItem in new[] { false, true })
                {
                    bool expected = sameItem && before == 1 && after == 0;
                    bool actual = SkateParkBuildItems.VerifiedDebit(before, after, sameItem);
                    Check(actual == expected, "exact identity-bound debit " + before + "/" + after + "/" + sameItem);
                    if (actual) successes++;
                }
        Check(successes == 1, "only one exact debit tuple is refundable evidence");
        Check(!SkateParkBuildItems.VerifiedDebit(0, 0, true), "no item before cannot justify a refund");
        Check(!SkateParkBuildItems.VerifiedDebit(1, 1, true), "unchanged amount cannot justify a refund");
        Check(!SkateParkBuildItems.VerifiedDebit(2, 1, true), "pooled decrement cannot justify a single-item refund");
        Check(!SkateParkBuildItems.VerifiedDebit(1, 0, false), "different pooled identity cannot justify a refund");
        Check(!SkateParkBuildItems.VerifiedDebit(1, 2, true), "amount growth cannot justify a refund");
        // sameItem is supplied by the native adapter. This proves its pure decision contract,
        // not that a runtime Item UID, Remove/UseItem call or delivery actually succeeded.
    }

    private static void RecoveryStatesAndCounts()
    {
        Check((int)SkateParkBuildItemState.None == 0 && (int)SkateParkBuildItemState.Preparing == 1 &&
              (int)SkateParkBuildItemState.Paid == 2 && (int)SkateParkBuildItemState.RecoveryClaimed == 3,
              "stable persisted payment-state values");
        for (int raw = -4; raw <= 8; raw++)
        {
            var state = (SkateParkBuildItemState)raw;
            Check(SkateParkBuildItems.ValidState(state) == (raw >= 0 && raw <= 3), "state boundary " + raw);
            for (int i = 0; i < Kinds.Length; i++)
                for (int members = -2; members <= 82; members++)
                    Check(SkateParkBuildItems.Recoverable(state, members, Kinds[i]) ==
                          (raw == 2 && members == OriginalParts[i]),
                          "payment/full original count " + raw + "/" + i + "/" + members);
        }
        foreach (int raw in new[] { int.MinValue, int.MaxValue })
        {
            var state = (SkateParkBuildItemState)raw;
            Check(!SkateParkBuildItems.ValidState(state), "extreme unknown payment state " + raw);
            for (int i = 0; i < Kinds.Length; i++)
                Check(!SkateParkBuildItems.Recoverable(state, OriginalParts[i], Kinds[i]),
                      "unknown paid-looking state cannot recover " + raw + "/" + i);
        }
        foreach (int raw in InvalidKinds)
            for (int state = 0; state <= 3; state++)
                foreach (int members in new[] { int.MinValue, -1, 0, 1, 2, 3, 36, int.MaxValue })
                    Check(!SkateParkBuildItems.Recoverable((SkateParkBuildItemState)state, members, (SkateParkKind)raw),
                          "unknown kind cannot recover " + state + "/" + members + "/" + raw);
        for (int i = 0; i < Kinds.Length; i++)
        {
            foreach (int members in new[] { int.MinValue, int.MaxValue })
                Check(!SkateParkBuildItems.Recoverable(SkateParkBuildItemState.Paid, members, Kinds[i]),
                      "extreme member count refuses " + i + "/" + members);
            Check(!SkateParkBuildItems.Recoverable(SkateParkBuildItemState.Paid, OriginalParts[i] - 1, Kinds[i]),
                  "partial group cannot recreate item " + i);
            Check(!SkateParkBuildItems.Recoverable(SkateParkBuildItemState.None, OriginalParts[i], Kinds[i]),
                  "legacy/free group cannot create item " + i);
            Check(!SkateParkBuildItems.Recoverable(SkateParkBuildItemState.Preparing, OriginalParts[i], Kinds[i]),
                  "unconfirmed payment cannot create item " + i);

            // Observe the actual eligibility predicate before and after the persisted claim state.
            // The adapter must persist RecoveryClaimed before delivery. No native transition is
            // fabricated here: save atomicity and exactly-once delivery are separate checks.
            var state = SkateParkBuildItemState.Paid;
            Check(SkateParkBuildItems.Recoverable(state, OriginalParts[i], Kinds[i]), "paid full group eligible " + i);
            state = SkateParkBuildItemState.RecoveryClaimed;
            Check(SkateParkBuildItems.ValidState(state), "claim is a persistable state " + i);
            for (int retry = 0; retry < 3; retry++)
                Check(!SkateParkBuildItems.Recoverable(state, OriginalParts[i], Kinds[i]),
                      "claimed group cannot recover again " + i + "/" + retry);
            Check(!SkateParkBuildItems.Recoverable(state, 0, Kinds[i]), "claimed demolished group remains ineligible " + i);
        }
    }

    private static bool ExerciseWarmContracts(int iterations)
    {
        bool correct = true;
        for (int i = 0; i < iterations; i++)
        {
            int index = i % Kinds.Length;
            var kind = Kinds[index];
            SkateParkKind resolved;
            correct &= SkateParkBuildItems.Name(kind) == Names[index] &&
                SkateParkBuildItems.Marker(kind) == Markers[index] && SkateParkBuildItems.Tagged(Markers[index]);
            correct &= SkateParkBuildItems.TryKind(Markers[index], out resolved) && resolved == kind;
            correct &= SkateParkBuildItems.TryIdentity("building.planner", 0, Markers[index], Names[index], 1,
                                                       out resolved) && resolved == kind;
            correct &= !SkateParkBuildItems.TryKind(0x534B50FF, out resolved) && resolved == default(SkateParkKind);
            correct &= !SkateParkBuildItems.TryIdentity("building.planner", 0, Markers[index], Names[index], 2,
                                                        out resolved) && resolved == default(SkateParkKind);
            correct &= !SkateParkBuildItems.TryIdentity("hammer", 0, Markers[index], Names[index], 1,
                                                        out resolved) && resolved == default(SkateParkKind);
            correct &= SkateParkBuildItems.VerifiedDebit(1, 0, true) &&
                !SkateParkBuildItems.VerifiedDebit(1, 0, false) && !SkateParkBuildItems.VerifiedDebit(2, 1, true);
            correct &= SkateParkBuildItems.Recoverable(SkateParkBuildItemState.Paid, OriginalParts[index], kind) &&
                !SkateParkBuildItems.Recoverable(SkateParkBuildItemState.Paid, OriginalParts[index] - 1, kind) &&
                !SkateParkBuildItems.Recoverable(SkateParkBuildItemState.RecoveryClaimed, OriginalParts[index], kind);
            correct &= SkateParkBuildItems.ValidState(SkateParkBuildItemState.RecoveryClaimed) &&
                !SkateParkBuildItems.ValidState((SkateParkBuildItemState)int.MaxValue);
        }
        return correct;
    }

    private static void DeterminismAndAllocation()
    {
        Check(ExerciseWarmContracts(20000), "warm known/invalid identities and payment states");
        for (int pass = 0; pass < 3; pass++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool correct = ExerciseWarmContracts(100000);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(correct, "steady known/invalid contracts deterministic " + pass);
            Check(allocated == 0, "warmed actual helper allocates zero bytes " + pass + ": " + allocated);
        }
    }


    // Optional extension of skate_park_build_items: consume only the actual plugin's
    // pure JSON parser. It neither constructs native objects nor touches World/SaveRestore.
    private static void OptionalParkMetadataParsing()
    {
        Assembly library = typeof(SkateParkBuildItems).Assembly;
        if (library.GetName().Name != "ShortcutSkate")
        {
            Console.WriteLine("Shared library has no plugin ParseParkData; metadata checks skipped.");
            return;
        }
        string references = Environment.GetEnvironmentVariable("SKATE_PARK_PARSER_REFERENCES");
        if (string.IsNullOrEmpty(references))
        {
            Console.WriteLine("Set SKATE_PARK_PARSER_REFERENCES to genuine managed DLLs for the optional parser checks.");
            return;
        }
        references = Path.GetFullPath(references);
        Check(File.Exists(Path.Combine(references, "Oxide.Rust.dll")), "parser uses genuine cached managed references");
        AppDomain.CurrentDomain.AssemblyResolve += (sender, request) =>
        {
            string path = Path.Combine(references, new AssemblyName(request.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        Type plugin = library.GetType("Oxide.Plugins.ShortcutSkate", true);
        MethodInfo parse = plugin.GetMethod("ParseParkData", BindingFlags.Public | BindingFlags.Static,
            null, new[] { typeof(string) }, null);
        Check(parse != null, "actual plugin exposes its pure park parser");
        Type json = null;
        foreach (CustomAttributeData attribute in plugin.GetNestedType("ParkGroup").GetField("Owner").GetCustomAttributesData())
            if (attribute.AttributeType.FullName == "Newtonsoft.Json.JsonConverterAttribute")
                json = attribute.AttributeType.Assembly.GetType("Newtonsoft.Json.JsonConvert", true);
        Check(json != null, "serializer matches the actual plugin's JSON attribute assembly");
        MethodInfo serialize = json.GetMethod("SerializeObject", BindingFlags.Public | BindingFlags.Static,
            null, new[] { typeof(object) }, null);
        Check(serialize != null, "actual Newtonsoft serializer provides object round-trip API");

        // These records deliberately use the old schema without Wall or BuildItemState.
        // All IDs below are tiny synthetic fixture identities, never operational/player IDs.
        var oldGroups = new StringBuilder();
        for (int i = 0; i < 8; i++)
        {
            if (i != 0) oldGroups.Append(',');
            oldGroups.Append(MetadataGroup(Kinds[i], i + 1, (ulong)(100 + i * 40), null,
                new SkateParkTransform { X = 17.375, Y = 2.625, Z = -8.0625, Yaw = 37.25 }, false));
        }
        object old = ParseAndRoundTrip(parse, serialize, MetadataWorld(oldGroups.ToString()), 8);
        var groups = (System.Collections.IList)old.GetType().GetField("Groups").GetValue(old);
        for (int i = 0; i < 8; i++)
        {
            object group = groups[i];
            Check((string)group.GetType().GetField("Kind").GetValue(group) == SkateParkLayout.Name(Kinds[i]),
                "old serialized kind survives parser round-trip " + i);
            Check(group.GetType().GetField("Wall").GetValue(group) == null,
                "old schema gains no external wall dependency " + i);
            Check(((System.Collections.IList)group.GetType().GetField("Members").GetValue(group)).Count == OriginalParts[i],
                "old serialized member count survives parser round-trip " + i);
        }

        SkateParkBox wallBox = new SkateParkBox { Y = 0.75, SizeX = 0.2, SizeY = 1.5, SizeZ = 3 };
        SkateParkBox floorBox = new SkateParkBox { SizeX = 3, SizeY = 0.2, SizeZ = 3 };
        foreach (int side in new[] { -1, 1 })
        {
            var target = new SkateParkBlock(SkateParkLayout.HalfWallPrefab, 13.25, 4.5, -7.125, 37.25, 0);
            SkateParkTransform anchor;
            Check(SkateParkLayout.TryHalfWallAnchor(target, wallBox, floorBox, side, out anchor),
                "metadata fixture uses valid measured half-wall join");
            string wall = MetadataWall(10, side, wallBox, floorBox);
            string group = MetadataGroup(SkateParkKind.HalfWallKicker, 10, 900, wall, anchor, true);
            object current = ParseAndRoundTrip(parse, serialize, MetadataWorld(group), 1);
            var currentGroups = (System.Collections.IList)current.GetType().GetField("Groups").GetValue(current);
            object attachment = currentGroups[0].GetType().GetField("Wall").GetValue(currentGroups[0]);
            Check(attachment != null && (ulong)attachment.GetType().GetField("Id").GetValue(attachment) == 10 &&
                (int)attachment.GetType().GetField("Side").GetValue(attachment) == side,
                "external wall identity/side survives parser round-trip");
            Check(((System.Collections.IList)currentGroups[0].GetType().GetField("Members").GetValue(currentGroups[0])).Count == 1,
                "serialized host remains outside the one paid/refunded member");
            MetadataRefused(parse, MetadataWorld(MetadataGroup(SkateParkKind.HalfWallKicker, 10, 900, null, anchor, true)),
                "half-wall record without attachment rejected");
            MetadataRefused(parse, MetadataWorld(MetadataGroup(SkateParkKind.HalfWallKicker, 10, 900, "null", anchor, true)),
                "half-wall record with null attachment rejected");
            foreach (string badWall in new[] {
                wall.Replace("\"Side\":" + side.ToString(CultureInfo.InvariantCulture) + ",", ""),
                wall.Replace("\"Side\":" + side.ToString(CultureInfo.InvariantCulture), "\"Side\":0"),
                wall.Replace("\"Side\":" + side.ToString(CultureInfo.InvariantCulture), "\"Side\":2"),
                wall.Replace("\"Id\":\"10\"", "\"Id\":\"0\""),
                wall.Replace("\"Owner\":\"1\"", "\"Owner\":\"0\""),
                wall.Replace("\"Building\":1", "\"Building\":0"),
                wall.Replace("\"Grade\":2", "\"Grade\":-1"),
                wall.Replace("\"X\":13.25", "\"X\":\"NaN\""),
                wall.Replace("\"Yaw\":37.25", "\"Yaw\":\"Infinity\""),
                wall.Replace("\"SizeX\":0.2", "\"SizeX\":0"),
                wall.Replace("\"SizeY\":0.2", "\"SizeY\":\"NaN\"") })
                MetadataRefused(parse, MetadataWorld(MetadataGroup(SkateParkKind.HalfWallKicker, 10, 900, badWall, anchor, true)),
                    "malformed external attachment rejected");
            SkateParkTransform shifted = anchor;
            shifted.X += 0.003;
            MetadataRefused(parse, MetadataWorld(MetadataGroup(SkateParkKind.HalfWallKicker, 10, 900, wall, shifted, true)),
                "internally consistent member with mismatched external wall join rejected");
            MetadataRefused(parse, MetadataWorld(MetadataGroup(SkateParkKind.HalfWallKicker, 10, 900,
                MetadataWall(900, side, wallBox, floorBox), anchor, true)), "external wall cannot also be a park member");
            MetadataRefused(parse, MetadataWorld(group + "," + MetadataGroup(SkateParkKind.HalfWallKicker, 11, 901, wall, anchor, true)),
                "two groups cannot occupy the same external wall side");
            MetadataRefused(parse, MetadataWorld(MetadataGroup(SkateParkKind.Bank, 10, 900, wall,
                new SkateParkTransform(), false)), "old kind cannot acquire unexpected wall dependency");
        }
        SkateParkTransform left, right;
        var host = new SkateParkBlock(SkateParkLayout.HalfWallPrefab, 13.25, 4.5, -7.125, 37.25, 0);
        Check(SkateParkLayout.TryHalfWallAnchor(host, wallBox, floorBox, -1, out left), "left-side metadata fixture valid");
        Check(SkateParkLayout.TryHalfWallAnchor(host, wallBox, floorBox, 1, out right), "right-side metadata fixture valid");
        ParseAndRoundTrip(parse, serialize, MetadataWorld(
            MetadataGroup(SkateParkKind.HalfWallKicker, 10, 900, MetadataWall(10, -1, wallBox, floorBox), left, true) + "," +
            MetadataGroup(SkateParkKind.HalfWallKicker, 11, 901, MetadataWall(10, 1, wallBox, floorBox), right, true)), 2);
        Console.WriteLine("Optional actual-plugin metadata parsing passed; native save/load and lifecycle remain unexercised.");
    }

    private static string Number(double value)
    { return value.ToString("R", CultureInfo.InvariantCulture); }

    private static string MetadataBox(SkateParkBox box)
    {
        return "{\"X\":" + Number(box.X) + ",\"Y\":" + Number(box.Y) + ",\"Z\":" + Number(box.Z) +
            ",\"SizeX\":" + Number(box.SizeX) + ",\"SizeY\":" + Number(box.SizeY) + ",\"SizeZ\":" + Number(box.SizeZ) + "}";
    }

    private static string MetadataWall(ulong id, int side, SkateParkBox wallBox, SkateParkBox floorBox)
    {
        return "{\"Id\":\"" + id.ToString(CultureInfo.InvariantCulture) + "\",\"Owner\":\"1\",\"Building\":1,\"Side\":" +
            side.ToString(CultureInfo.InvariantCulture) + ",\"Grade\":2,\"X\":13.25,\"Y\":4.5,\"Z\":-7.125,\"Yaw\":37.25," +
            "\"WallBounds\":" + MetadataBox(wallBox) + ",\"FloorBounds\":" + MetadataBox(floorBox) + "}";
    }

    private static string MetadataGroup(SkateParkKind kind, int groupId, ulong firstMember, string wall,
        SkateParkTransform anchor, bool paid)
    {
        var text = new StringBuilder("{\"Id\":\"");
        text.Append(groupId.ToString("x32", CultureInfo.InvariantCulture)).Append("\",\"Owner\":\"1\",\"Kind\":\"");
        text.Append(SkateParkLayout.Name(kind)).Append("\",\"AnchorX\":").Append(Number(anchor.X));
        text.Append(",\"AnchorY\":").Append(Number(anchor.Y)).Append(",\"AnchorZ\":").Append(Number(anchor.Z));
        text.Append(",\"AnchorYaw\":").Append(Number(anchor.Yaw)).Append(",\"Members\":[");
        for (int i = 0; i < SkateParkLayout.PartCount(kind); i++)
        {
            SkateParkTransform part;
            Check(SkateParkLayout.TryTransform(kind, i, anchor.X, anchor.Y, anchor.Z, anchor.Yaw, out part),
                "metadata fixture uses valid member transform");
            if (i != 0) text.Append(',');
            text.Append("{\"Index\":").Append(i.ToString(CultureInfo.InvariantCulture)).Append(",\"Id\":\"");
            text.Append((firstMember + (ulong)i).ToString(CultureInfo.InvariantCulture)).Append("\",\"Prefab\":1,\"X\":");
            text.Append(Number(part.X)).Append(",\"Y\":").Append(Number(part.Y)).Append(",\"Z\":").Append(Number(part.Z));
            text.Append(",\"Yaw\":").Append(Number(part.Yaw)).Append(",\"Pitch\":").Append(Number(part.Pitch)).Append('}');
        }
        text.Append(']');
        if (paid) text.Append(",\"BuildItemState\":2");
        if (wall != null) text.Append(",\"Wall\":").Append(wall);
        return text.Append('}').ToString();
    }

    private static string MetadataWorld(string groups)
    {
        return "{\"Version\":1,\"WipeId\":\"approved-half-wall-fixture\",\"SaveCreatedTicks\":" +
            TimeSpan.TicksPerSecond.ToString(CultureInfo.InvariantCulture) + ",\"Seed\":0,\"Size\":1000,\"Checksum\":\"\",\"Groups\":[" + groups + "]}";
    }

    private static object ParseAndRoundTrip(MethodInfo parse, MethodInfo serialize, string text, int groupCount)
    {
        object value = parse.Invoke(null, new object[] { text });
        Check(value != null && ((System.Collections.IList)value.GetType().GetField("Groups").GetValue(value)).Count == groupCount,
            "actual park parser accepts the fixture group count");
        string written = (string)serialize.Invoke(null, new[] { value });
        object restored = parse.Invoke(null, new object[] { written });
        Check(restored != null && (string)serialize.Invoke(null, new[] { restored }) == written,
            "actual park parser and serializer round-trip stably");
        return restored;
    }

    private static void MetadataRefused(MethodInfo parse, string text, string label)
    {
        bool refused = false;
        try { parse.Invoke(null, new object[] { text }); }
        catch (TargetInvocationException error)
        {
            Exception cause = error.InnerException;
            if (cause == null || (!(cause is InvalidDataException) &&
                (cause.GetType().Namespace == null || !cause.GetType().Namespace.StartsWith("Newtonsoft.Json", StringComparison.Ordinal)))) throw;
            refused = true;
        }
        Check(refused, label);
    }

    public static int Main(string[] args)
    {
        try
        {
            Check(args.Length <= 1, "optional argument is the expected actual-DLL SHA-256");
            string library = typeof(SkateParkBuildItems).Assembly.Location, digest;
            using (var file = File.OpenRead(library))
            using (var hash = SHA256.Create())
                digest = BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
            Console.WriteLine("Actual library: " + library + "\nSHA-256: " + digest);
            if (args.Length == 1)
                Check(string.Equals(args[0], digest, StringComparison.OrdinalIgnoreCase), "actual library hash pinned");
            CatalogAndTagBoundaries(); IdentityRefusals(); VerifiedSingleDebit(); RecoveryStatesAndCounts();
            DeterminismAndAllocation(); OptionalParkMetadataParsing();
            Console.WriteLine("skate_park_build_items: " + checks + " checks passed");
            Console.WriteLine("Native item/UID deduction, registration, save/claim persistence, refund delivery and building-plan UI were not exercised.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
