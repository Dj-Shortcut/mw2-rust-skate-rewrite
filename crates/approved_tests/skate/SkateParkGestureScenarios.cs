using System;
using System.IO;
using System.Security.Cryptography;
using Shortcut.RustMod;

internal static class SkateParkGestureScenarios
{
    private static int checks;
    private static readonly SkateParkKind[] DefaultOrder = {
        SkateParkKind.Halfpipe, SkateParkKind.Bank, SkateParkKind.Kicker,
        SkateParkKind.QuarterPipe, SkateParkKind.LedgeLow, SkateParkKind.LedgeHigh,
        SkateParkKind.LongLedge, SkateParkKind.Funbox
    };

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal(SkateParkSelectionState actual, SkateParkSelectionState expected, string message)
    {
        Check(actual.Selected == expected.Selected && actual.Kind == expected.Kind &&
              actual.Expires.Equals(expected.Expires), message);
    }

    private static SkateParkSelectionState Apply(SkateParkSelectionState state, SkateParkSelectionAction action,
                                                 double now, SkateParkKind[] order = null, bool ready = true)
    {
        SkateParkSelectionState next;
        Check(SkateParkSelections.TryApply(state, action, true, ready, now, order, out next), action + " accepted");
        return next;
    }

    private static void Refuse(SkateParkSelectionState state, SkateParkSelectionAction action, bool admin,
                               bool ready, double now, SkateParkKind[] order, string label)
    {
        SkateParkSelectionState next;
        Check(!SkateParkSelections.TryApply(state, action, admin, ready, now, order, out next), label + " refused");
        Equal(next, state, label + " atomic");
    }

    private static void Sequence()
    {
        Check(SkateParkSelections.LifetimeSeconds == 120, "two-minute lifetime");
        Check(SkateParkSelections.ValidOrder(DefaultOrder), "default order valid");
        var state = default(SkateParkSelectionState);
        for (int cycle = 0; cycle < 3; cycle++)
            for (int index = 0; index < DefaultOrder.Length; index++)
            {
                double now = 10 + cycle * DefaultOrder.Length + index;
                state = Apply(state, SkateParkSelectionAction.Next, now, DefaultOrder);
                Equal(state, new SkateParkSelectionState(true, DefaultOrder[index], now + 120), "halfpipe-first cycling");
            }

        var custom = new[] { SkateParkKind.LedgeHigh, SkateParkKind.Halfpipe, SkateParkKind.Kicker };
        state = Apply(state, SkateParkSelectionAction.Next, 40, custom);
        Equal(state, new SkateParkSelectionState(true, SkateParkKind.LedgeHigh, 160), "unlisted current starts configured order");
        state = Apply(state, SkateParkSelectionAction.Next, 41, custom);
        Equal(state, new SkateParkSelectionState(true, SkateParkKind.Halfpipe, 161), "custom second");
        state = Apply(state, SkateParkSelectionAction.Next, 42, custom);
        Equal(state, new SkateParkSelectionState(true, SkateParkKind.Kicker, 162), "custom third");
        state = Apply(state, SkateParkSelectionAction.Next, 43, custom);
        Equal(state, new SkateParkSelectionState(true, SkateParkKind.LedgeHigh, 163), "custom wraps");
        state = Apply(state, SkateParkSelectionAction.Cancel, 44, null, false);
        Check(!state.Selected && !SkateParkSelections.Active(state, 44), "cancel while park unavailable");
        state = Apply(state, SkateParkSelectionAction.Next, 45, DefaultOrder);
        Check(state.Kind == SkateParkKind.Halfpipe, "cancel restarts halfpipe");

        var single = new[] { SkateParkKind.QuarterPipe };
        for (int i = 0; i < 10; i++)
        {
            state = Apply(state, SkateParkSelectionAction.Next, 50 + i, single);
            Equal(state, new SkateParkSelectionState(true, SkateParkKind.QuarterPipe, 170 + i), "single-kind renewal");
        }
    }

    private static void LifetimeAndPlacement()
    {
        SkateParkSelectionState selected;
        Check(SkateParkSelections.TrySelect(SkateParkKind.Bank, true, true, 10, out selected), "manual selection shares policy");
        Equal(selected, new SkateParkSelectionState(true, SkateParkKind.Bank, 130), "manual deadline");
        Check(SkateParkSelections.Active(selected, 129.999), "before expiry active");
        Check(SkateParkSelections.Active(selected, 130), "deadline inclusive");
        Check(!SkateParkSelections.Active(selected, 130.001), "after expiry inactive");
        var atDeadline = Apply(selected, SkateParkSelectionAction.Next, 130, DefaultOrder);
        Equal(atDeadline, new SkateParkSelectionState(true, SkateParkKind.Kicker, 250), "inclusive deadline cycles");
        var expired = Apply(selected, SkateParkSelectionAction.Next, 130.001, DefaultOrder);
        Equal(expired, new SkateParkSelectionState(true, SkateParkKind.Halfpipe, 250.001), "expired starts first");

        var failed = Apply(selected, SkateParkSelectionAction.PlacementFailed, 11, null, false);
        Equal(failed, selected, "missing placement data keeps deadline");
        failed = Apply(failed, SkateParkSelectionAction.PlacementFailed, 129.5, null, false);
        Equal(failed, selected, "repeated failure does not renew selection");
        failed = Apply(failed, SkateParkSelectionAction.PlacementFailed, 131, null, false);
        Equal(failed, selected, "failure does not resurrect expired selection");
        Refuse(selected, SkateParkSelectionAction.PlacementSucceeded, true, false, 11, null, "unready success");
        Refuse(selected, SkateParkSelectionAction.PlacementSucceeded, true, true, 131, null, "expired success");
        var success = Apply(selected, SkateParkSelectionAction.PlacementSucceeded, 130);
        Check(!success.Selected, "successful placement consumes selection");
        success = Apply(success, SkateParkSelectionAction.Next, 131, DefaultOrder);
        Check(success.Kind == SkateParkKind.Halfpipe, "successful placement resets sequence");
    }

    private static void Refusals()
    {
        var selected = new SkateParkSelectionState(true, SkateParkKind.LongLedge, 150);
        foreach (SkateParkSelectionAction action in Enum.GetValues(typeof(SkateParkSelectionAction)))
            Refuse(selected, action, false, true, 50, DefaultOrder, "non-admin " + action);
        Refuse(selected, SkateParkSelectionAction.Next, true, false, 50, DefaultOrder, "unready next");
        Refuse(selected, (SkateParkSelectionAction)99, true, true, 50, DefaultOrder, "unknown action");
        foreach (double now in new[] { -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Check(!SkateParkSelections.Active(selected, now), "invalid clock is not active");
            foreach (SkateParkSelectionAction action in Enum.GetValues(typeof(SkateParkSelectionAction)))
                Refuse(selected, action, true, true, now, DefaultOrder, "invalid clock " + action);
        }
        Refuse(selected, SkateParkSelectionAction.Next, true, true, double.MaxValue, DefaultOrder, "unrepresentable new deadline");

        SkateParkKind[][] invalidOrders = {
            null, new SkateParkKind[0], new[] { SkateParkKind.Bank, SkateParkKind.Bank },
            new[] { (SkateParkKind)99 },
            new[] { SkateParkKind.Bank, SkateParkKind.Kicker, SkateParkKind.QuarterPipe,
                    SkateParkKind.LedgeLow, SkateParkKind.LedgeHigh, SkateParkKind.LongLedge,
                    SkateParkKind.Funbox, SkateParkKind.Halfpipe, SkateParkKind.Bank }
        };
        foreach (var order in invalidOrders)
        {
            Check(!SkateParkSelections.ValidOrder(order), "invalid order rejected");
            Refuse(selected, SkateParkSelectionAction.Next, true, true, 50, order, "invalid order next");
        }
        foreach (var bad in new[] {
            new SkateParkSelectionState(true, (SkateParkKind)99, 150),
            new SkateParkSelectionState(true, SkateParkKind.Bank, 0),
            new SkateParkSelectionState(true, SkateParkKind.Bank, -1),
            new SkateParkSelectionState(true, SkateParkKind.Bank, double.NaN),
            new SkateParkSelectionState(true, SkateParkKind.Bank, double.PositiveInfinity)
        })
        {
            Check(!SkateParkSelections.Active(bad, 50), "corrupt selection inactive");
            foreach (SkateParkSelectionAction action in Enum.GetValues(typeof(SkateParkSelectionAction)))
                Refuse(bad, action, true, true, 50, DefaultOrder, "corrupt state " + action);
        }
        SkateParkSelectionState result;
        Check(!SkateParkSelections.TrySelect((SkateParkKind)99, true, true, 0, out result), "unknown manual kind refused");
        Check(!SkateParkSelections.TrySelect(SkateParkKind.Bank, false, true, 0, out result), "non-admin manual refused");
        Check(!SkateParkSelections.TrySelect(SkateParkKind.Bank, true, false, 0, out result), "unready manual refused");
        Check(!SkateParkSelections.TrySelect(SkateParkKind.Bank, true, true, double.MaxValue, out result), "manual deadline overflow refused");
    }

    private static void GestureMatch(string name, string[][] catalog, bool accepted, int expectedIndex,
                                     string expectedError, bool expectedMissing, string label)
    {
        int index = 42;
        string error = "stale error";
        bool missing = !expectedMissing;
        bool result = SkateParkSelections.TryGesture(name, catalog, out index, out error, out missing);
        Check(result == accepted, label + " result");
        Check(index == expectedIndex, label + " index");
        Check(error == expectedError, label + " error");
        Check(missing == expectedMissing, label + " missing flag");
        int oldIndex = 42;
        string oldError = "stale error";
        Check(SkateParkSelections.TryGesture(name, catalog, out oldIndex, out oldError) == result,
              label + " legacy overload result");
        Check(oldIndex == index && oldError == error, label + " legacy overload outputs");
    }

    private static void GestureNames()
    {
        var catalog = new[] {
            new[] { "I need wood", "gesture.needwood", "needwood", "needwood" },
            new[] { "Thumbs up", "gesture.thumbsup", "thumbsup", "thumbsup" },
            null, new string[] { null, "token.wave", "convar.wave", "command.wave" }
        };
        foreach (string name in new[] { "I need wood", "i NEED WOOD", "gesture.needwood", "needwood" })
            GestureMatch(name, catalog, true, 0, null, false, "select alias " + name);
        foreach (string name in new[] { "Thumbs up", "THUMBS UP", "gesture.thumbsup", "thumbsup" })
            GestureMatch(name, catalog, true, 1, null, false, "cancel alias " + name);
        GestureMatch("command.wave", catalog, true, 3, null, false, "command with missing English resolves");
        GestureMatch("token.wave", catalog, true, 3, null, false, "token with missing English resolves");
        GestureMatch("convar.wave", catalog, true, 3, null, false, "convar with missing English resolves");
        GestureMatch("missing", catalog, false, -1, "Gesture 'missing' was not found in the native catalog.",
                     true, "unknown gesture");
        GestureMatch("I need wood", null, false, -1, "Gesture catalog is unavailable.", false, "absent catalog");
        GestureMatch("missing", new string[0][], false, -1,
                     "Gesture 'missing' was not found in the native catalog.", true, "empty catalog");
        GestureMatch("missing", new[] { null, new string[0], new string[] { null, null, null, null } },
                     false, -1, "Gesture 'missing' was not found in the native catalog.", true, "present catalog without aliases");
        var ambiguous = new[] { new[] { "Thumbs up" }, new[] { "thumbs UP" } };
        GestureMatch("thumbs up", ambiguous, false, -1, "Gesture 'thumbs up' is ambiguous.", false, "distinct rows ambiguous");
        GestureMatch("repeat", new[] { new[] { "Repeat", "repeat", "REPEAT", "Repeat" } },
                     true, 0, null, false, "repeated aliases within one gesture");
        GestureMatch("Comma, English", new[] { new[] { "Comma, English", "token", "convar", "command" } },
                     true, 0, null, false, "display exclusion does not alter lookup aliases");
        foreach (string name in new[] { null, "", " ", " I need wood", "Thumbs up ", "I\nneed wood",
                                        "I\rneed wood", "I\tneed wood", "I\0need wood", new string('a', 129) })
        {
            Check(!SkateParkSelections.ValidGestureName(name), "invalid configured gesture name");
            GestureMatch(name, catalog, false, -1, "Configured gesture name is invalid.", false, "invalid name");
        }
        GestureMatch(null, null, false, -1, "Configured gesture name is invalid.", false, "invalid name before absent catalog");
        Check(SkateParkSelections.ValidGestureName(new string('a', 128)), "gesture-name maximum accepted");
        GestureMatch(new string('a', 128), new[] { new[] { new string('a', 128) } },
                     true, 0, null, false, "maximum-length alias");
    }

    private static void CatalogNames(string[][] catalog, string expected, string label)
    {
        string actual = SkateParkSelections.GestureCatalogNames(catalog);
        Check(actual == expected, label + " names and order");
        Check(actual != null && actual.IndexOf('\n') < 0 && actual.IndexOf('\r') < 0 &&
              actual.IndexOf('\u2028') < 0 && actual.IndexOf('\u2029') < 0, label + " one line");
        bool controls = false;
        foreach (char character in actual) controls |= char.IsControl(character);
        Check(!controls, label + " no controls");
    }

    private static void GestureCatalogDisplay()
    {
        CatalogNames(null, "", "absent display catalog");
        CatalogNames(new string[0][], "", "empty display catalog");
        CatalogNames(new[] { new[] { "English only" } }, "English only", "short English row");
        CatalogNames(new[] {
            new[] { "I need wood", "gesture.needwood", "needwood", "needwood" },
            new[] { "Thumbs up", "gesture.thumbsup", "thumbsup", "thumbsup" },
            null, new string[] { null, "token.wave", "convar.wave", "command.wave" }
        }, "I need wood, Thumbs up, convar.wave", "English preferred and convar fallback");
        CatalogNames(new[] {
            new[] { "Zulu" }, new[] { "Alpha" }, new[] { "zULU" },
            new[] { "Beta" }, new string[] { null, "token", "aLPHA", "command" },
            new string[] { null, "token", "Gamma", "command" },
            new string[] { null, "token", "bETA", "command" }
        }, "Zulu, Alpha, Beta, Gamma", "first display spelling and native order");
        CatalogNames(new[] {
            new string[] { null, "token", "null-english", "command" },
            new[] { "", "token", "empty-english", "command" },
            new[] { " ", "token", "blank-english", "command" },
            new[] { " Leading", "token", "leading-space", "command" },
            new[] { "Trailing ", "token", "trailing-space", "command" },
            new[] { "Control\nName", "token", "control-name", "command" },
            new[] { "Comma, Name", "token", "comma-name", "command" },
            new[] { "Line\u2028Name", "token", "line-separator", "command" },
            new[] { "Paragraph\u2029Name", "token", "paragraph-separator", "command" },
            new[] { new string('a', 129), "token", "long-english", "command" }
        }, "null-english, empty-english, blank-english, leading-space, trailing-space, control-name, comma-name, " +
           "line-separator, paragraph-separator, long-english", "unusable English falls back");
        CatalogNames(new[] {
            null, new string[0], new string[] { null }, new string[] { null, "Token only" },
            new string[] { null, "token", null, "Command only" },
            new[] { "Comma, English", "Token only" },
            new string[] { null, "token", "", "command" },
            new string[] { null, "token", " Convar", "command" },
            new string[] { null, "token", "Convar ", "command" },
            new string[] { null, "token", "Comma, convar", "command" },
            new string[] { null, "token", "Control\tconvar", "command" },
            new string[] { null, "token", "Line\u2028convar", "command" },
            new string[] { null, "token", "Paragraph\u2029convar", "command" },
            new string[] { null, "token", new string('a', 129), "command" }
        }, "", "unusable rows never display tokens or commands");
        CatalogNames(new[] {
            new[] { "Valid English", "token", "Invalid\nconvar", "command" },
            new string[] { null, "token", "Valid convar", "command" }
        }, "Valid English, Valid convar", "usable preferred English does not require usable fallback");
        CatalogNames(new[] { new[] { new string('a', 128) } }, new string('a', 128), "maximum-length English");
        CatalogNames(new[] { new string[] { null, null, new string('b', 128) } },
                     new string('b', 128), "maximum-length convar");
    }

    private static int Main()
    {
        string path = typeof(SkateParkSelections).Assembly.Location;
        using (var file = File.OpenRead(path))
        using (var sha = SHA256.Create())
            Console.WriteLine("Actual plugin: " + path + "\nSHA-256: " + Convert.ToHexString(sha.ComputeHash(file)).ToLowerInvariant());
        Sequence();
        LifetimeAndPlacement();
        Refusals();
        GestureNames();
        GestureCatalogDisplay();
        Console.WriteLine("skate_park_gesture_selection: " + checks + " checks passed");
        return 0;
    }
}
