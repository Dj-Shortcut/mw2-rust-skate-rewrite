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

    private static void GestureNames()
    {
        var catalog = new[] {
            new[] { "I need wood", "gesture.needwood", "needwood", "needwood" },
            new[] { "Thumbs up", "gesture.thumbsup", "thumbsup", "thumbsup" },
            null, new string[] { null, "Wave", "gesture.wave", "wave" }
        };
        int index;
        string error;
        foreach (string name in new[] { "I need wood", "i NEED WOOD", "gesture.needwood", "needwood" })
        {
            Check(SkateParkSelections.TryGesture(name, catalog, out index, out error), "select alias resolves");
            Check(index == 0 && error == null, "select aliases identify one native row");
        }
        foreach (string name in new[] { "Thumbs up", "THUMBS UP", "gesture.thumbsup", "thumbsup" })
        {
            Check(SkateParkSelections.TryGesture(name, catalog, out index, out error), "cancel alias resolves");
            Check(index == 1 && error == null, "cancel aliases identify distinct native row");
        }
        Check(SkateParkSelections.TryGesture("wave", catalog, out index, out error) && index == 3, "null rows and aliases skipped");
        Check(!SkateParkSelections.TryGesture("missing", catalog, out index, out error) && index == -1 &&
              !string.IsNullOrEmpty(error), "unknown gesture fails closed");
        Check(!SkateParkSelections.TryGesture("I need wood", null, out index, out error) && index == -1 &&
              !string.IsNullOrEmpty(error), "missing catalog fails closed");
        var ambiguous = new[] { new[] { "Thumbs up" }, new[] { "thumbs UP" } };
        Check(!SkateParkSelections.TryGesture("thumbs up", ambiguous, out index, out error) && index == -1 &&
              !string.IsNullOrEmpty(error), "ambiguous rows fail closed");
        foreach (string name in new[] { null, "", " ", " I need wood", "Thumbs up ", "I\nneed wood", new string('a', 129) })
        {
            Check(!SkateParkSelections.ValidGestureName(name), "invalid configured gesture name");
            Check(!SkateParkSelections.TryGesture(name, catalog, out index, out error) && index == -1 &&
                  !string.IsNullOrEmpty(error), "invalid gesture leaves no match");
        }
        Check(SkateParkSelections.ValidGestureName(new string('a', 128)), "gesture-name maximum accepted");
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
        Console.WriteLine("skate_park_gesture_selection: " + checks + " checks passed");
        return 0;
    }
}
