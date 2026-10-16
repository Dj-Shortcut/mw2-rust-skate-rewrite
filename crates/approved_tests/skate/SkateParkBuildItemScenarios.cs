using System;
using System.IO;
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
        SkateParkKind.MiniQuarterPipe, SkateParkKind.LargeQuarterPipe
    };
    private static readonly string[] Names = {
        "Skate Bank", "Skate Kicker", "Skate Quarterpipe", "Skate Low Ledge",
        "Skate High Ledge", "Skate Long Ledge", "Skate Funbox", "Skate Halfpipe",
        "Skate Mini Quarterpipe", "Skate Large Quarterpipe"
    };
    private static readonly int[] Markers = {
        0x534B5001, 0x534B5002, 0x534B5003, 0x534B5004,
        0x534B5005, 0x534B5006, 0x534B5007, 0x534B5008,
        0x534B5009, 0x534B500A
    };
    private static readonly int[] OriginalParts = { 1, 1, 2, 1, 1, 3, 3, 36, 2, 3 };
    private static readonly int[] InvalidKinds = { int.MinValue, -100, -1, 10, 11, 255, int.MaxValue };
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
        Check(SkateParkBuildItems.PieceCount == 10, "eight original and two appended named build items");
        Check(typeof(SkateParkBuildItems).Assembly == typeof(SkateParkLayout).Assembly,
              "item and layout contracts come from the same actual library");
        for (int i = 0; i < Kinds.Length; i++)
        {
            Check((int)Kinds[i] == i, "stable kind identity " + i);
            Check(SkateParkBuildItems.Name(Kinds[i]) == Names[i], "English item name " + i);
            Check(SkateParkBuildItems.Marker(Kinds[i]) == Markers[i], "independent item marker " + i);
            Check(SkateParkLayout.PartCount(Kinds[i]) == OriginalParts[i], "original member count " + i);
            Check(SkateParkBuildItems.Tagged(Markers[i]), "known marker tagged " + i);
            var kind = SkateParkKind.Halfpipe;
            Check(SkateParkBuildItems.TryKind(Markers[i], out kind) && kind == Kinds[i],
                  "known marker resolves its literal kind " + i);
            Identity(true, Kinds[i], "building.planner", 0, Markers[i], Names[i], 1, "exact item " + i);
        }

        // The whole tag range is reserved, but only markers 1..10 identify pieces.
        // Tagged-but-unrecognized plans must not silently become ordinary vanilla plans.
        for (int marker = 0x534B4FFC; marker <= 0x534B5103; marker++)
        {
            bool tagged = marker >= 0x534B5000 && marker <= 0x534B50FF;
            Check(SkateParkBuildItems.Tagged(marker) == tagged, "tag boundary " + marker);
            var kind = SkateParkKind.Halfpipe;
            bool valid = marker >= 0x534B5001 && marker <= 0x534B500A;
            Check(SkateParkBuildItems.TryKind(marker, out kind) == valid, "marker gap decision " + marker);
            Check(kind == (valid ? Kinds[marker - 0x534B5001] : default(SkateParkKind)),
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
                for (int members = -2; members <= 40; members++)
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
            DeterminismAndAllocation();
            Console.WriteLine("skate_park_build_items: " + checks + " checks passed");
            Console.WriteLine("Native item/UID deduction, registration, save/claim persistence, refund delivery and building-plan UI were not exercised.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
