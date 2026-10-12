using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Rust;
using Shortcut.RustMod;
using Unity.Collections;
using UnityEngine;

namespace Shortcut.RustMod
{
    public enum SkateSessionReason
    {
        None, Inactive, InvalidClock, BackwardsClock, BatchGap, InvalidDelta,
        InvalidHorizontalPath, InvalidVerticalPath, InvalidMaximumSpeed, InvalidMaximumAirtime,
        InvalidRearmSeconds, HorizontalSpeed, VerticalLimit, Duration, Airtime, Unsupported,
        ClockBeforeDelta, NonIncreasingClock, InvalidTickCount, InvalidCoordinate,
        PositionDiscontinuity, CacheIndexMismatch, PermissionDenied, OptedOut, AutomaticDisabled,
        Unavailable, Disconnected, Npc, Dead, Sleeping, Wounded, Mounted, Swimming, Parented,
        IdentityChanged, ManualOff, ManualOn, AutomaticRearm, ManualRestart, Unload,
        BatchException, MovePatchException, SpeedPatchException, FlyPatchException,
        BatchExhausted, BeginRejected, Count
    }

    public struct SkateSessionObservation
    {
        public SkateSessionReason Reason;
        public double Time, DeltaSeconds, GapSeconds, HorizontalPath, VerticalPath;
        public double MaximumSpeed, MaximumAirtime, AirSeconds, DurationSeconds, ElapsedSeconds;
        public double RearmSeconds, RequiredRearmSeconds, PositionGap;
        public bool FullySupported, SupportKnown, WasActive;
        public long Batch;
        public int SegmentCount, BufferSize;

        public static SkateSessionObservation Empty(double now, SkateSessionReason reason)
        {
            return new SkateSessionObservation { Reason = reason, Time = now,
                DeltaSeconds = double.NaN, GapSeconds = double.NaN,
                HorizontalPath = double.NaN, VerticalPath = double.NaN,
                MaximumSpeed = double.NaN, MaximumAirtime = double.NaN,
                AirSeconds = double.NaN, DurationSeconds = double.NaN, ElapsedSeconds = double.NaN,
                RearmSeconds = double.NaN, RequiredRearmSeconds = double.NaN, PositionGap = double.NaN,
                SegmentCount = -1, BufferSize = -1 };
        }
    }

    public sealed class SkateSessionHistory
    {
        private readonly long[] disarms = new long[(int)SkateSessionReason.Count];
        private readonly long[] activations = new long[(int)SkateSessionReason.Count];
        private readonly long[] resets = new long[(int)SkateSessionReason.Count];
        public bool HasDisarm { get; private set; }
        public bool HasActivation { get; private set; }
        public bool HasWarmupReset { get; private set; }
        public bool HasSample { get; private set; }
        public SkateSessionObservation LastDisarm { get; private set; }
        public SkateSessionObservation LastActivation { get; private set; }
        public SkateSessionObservation LastWarmupReset { get; private set; }
        public SkateSessionObservation LastSample { get; private set; }

        private static bool Valid(SkateSessionReason reason)
        { return reason > SkateSessionReason.None && reason < SkateSessionReason.Count; }
        private static void Increment(long[] counts, SkateSessionReason reason)
        { if (Valid(reason) && counts[(int)reason] < long.MaxValue) counts[(int)reason]++; }
        public long DisarmCount(SkateSessionReason reason)
        { return Valid(reason) ? disarms[(int)reason] : 0; }
        public long ActivationCount(SkateSessionReason reason)
        { return Valid(reason) ? activations[(int)reason] : 0; }
        public long WarmupResetCount(SkateSessionReason reason)
        { return Valid(reason) ? resets[(int)reason] : 0; }

        public void Sample(SkateSessionObservation observation)
        { LastSample = observation; HasSample = true; }
        public void Disarm(SkateSessionObservation observation)
        {
            Sample(observation);
            if (!observation.WasActive || !Valid(observation.Reason)) return;
            LastDisarm = observation; HasDisarm = true;
            Increment(disarms, observation.Reason);
        }
        public void WarmupReset(SkateSessionObservation observation)
        {
            Sample(observation);
            if (observation.WasActive || !Valid(observation.Reason) ||
                !SkateSessionBounds.Finite(observation.RearmSeconds) || observation.RearmSeconds <= 0) return;
            LastWarmupReset = observation; HasWarmupReset = true;
            Increment(resets, observation.Reason);
        }
        public void Activate(SkateSessionObservation observation)
        {
            if (observation.Reason != SkateSessionReason.ManualOn &&
                observation.Reason != SkateSessionReason.AutomaticRearm) return;
            LastActivation = observation; HasActivation = true;
            Increment(activations, observation.Reason);
        }
    }

    public sealed class SkateDiagnosticsStore
    {
        public const int Capacity = 512;
        private sealed class Entry
        {
            public object Identity;
            public SkateSessionHistory History;
        }
        private readonly Dictionary<ulong, Entry> entries = new Dictionary<ulong, Entry>();
        private readonly ulong[] removals = new ulong[Capacity];
        public int Count { get { return entries.Count; } }

        public bool TryGet(ulong id, object identity, out SkateSessionHistory history)
        {
            Entry entry;
            history = null;
            if (identity == null || !entries.TryGetValue(id, out entry) ||
                !ReferenceEquals(identity, entry.Identity)) return false;
            history = entry.History; return true;
        }
        public SkateSessionHistory GetOrCreate(ulong id, object identity)
        {
            if (id == 0 || identity == null) return null;
            Entry entry;
            if (entries.TryGetValue(id, out entry))
            {
                if (ReferenceEquals(identity, entry.Identity)) return entry.History;
                entries.Remove(id);
            }
            if (Count >= Capacity) return null;
            entry = new Entry { Identity = identity, History = new SkateSessionHistory() };
            entries.Add(id, entry); return entry.History;
        }
        public void Remove(ulong id, object identity)
        {
            Entry entry;
            if (entries.TryGetValue(id, out entry) && ReferenceEquals(identity, entry.Identity)) entries.Remove(id);
        }
        public void RemoveInactive(Func<object, bool> connected)
        {
            int count = 0;
            foreach (var pair in entries)
                if (!connected(pair.Value.Identity)) removals[count++] = pair.Key;
            for (int i = 0; i < count; i++) entries.Remove(removals[i]);
        }
        public void Clear() { entries.Clear(); }
    }

    public enum SkateStatusError { None, TargetRequired, InvalidTarget, Forbidden }

    public static class SkateStatusAccess
    {
        public static bool TrySelectTarget(ulong actorId, bool hasPlayer, bool privileged, string explicitTarget,
                                          out ulong targetId, out SkateStatusError error)
        {
            targetId = 0; error = SkateStatusError.None;
            if (explicitTarget == null)
            {
                if (!hasPlayer || actorId == 0) { error = SkateStatusError.TargetRequired; return false; }
                targetId = actorId; return true;
            }
            if (!ulong.TryParse(explicitTarget, NumberStyles.None, CultureInfo.InvariantCulture, out targetId) || targetId == 0)
            { targetId = 0; error = SkateStatusError.InvalidTarget; return false; }
            if (!privileged && (!hasPlayer || targetId != actorId))
            { targetId = 0; error = SkateStatusError.Forbidden; return false; }
            return true;
        }
    }

    public static class SkateStatusReport
    {
        public static string Number(double value)
        { return SkateSessionBounds.Finite(value) ? value.ToString("G9", CultureInfo.InvariantCulture) : "unknown"; }
        private static double Age(double now, double time)
        {
            double age = now - time;
            return SkateSessionBounds.Finite(now) && SkateSessionBounds.Finite(time) && SkateSessionBounds.Finite(age) ?
                Math.Max(0, age) : double.NaN;
        }
        private static void Observation(StringBuilder text, string label, SkateSessionObservation value, double now)
        {
            text.Append(label).Append(": ").Append(value.Reason).Append("; age=")
                .Append(Number(Age(now, value.Time))).Append("s; dt=").Append(Number(value.DeltaSeconds))
                .Append("s; gap=").Append(Number(value.GapSeconds)).Append("s/0.5s; horizontal=")
                .Append(Number(value.HorizontalPath)).Append("m/").Append(Number(value.MaximumSpeed * value.DeltaSeconds))
                .Append("m; speed=").Append(Number(SkateSessionBounds.Finite(value.DeltaSeconds) && value.DeltaSeconds > 0 ? value.HorizontalPath / value.DeltaSeconds : double.NaN))
                .Append("m/s/").Append(Number(value.MaximumSpeed)).Append("m/s; vertical=").Append(Number(value.VerticalPath)).Append("m/")
                .Append(Number(40 * value.DeltaSeconds)).Append("m; supported=").Append(value.SupportKnown ? (value.FullySupported ? "true" : "false") : "unknown")
                .Append("; air=").Append(Number(value.AirSeconds)).Append("s/").Append(Number(value.MaximumAirtime))
                .Append("s; duration=").Append(Number(value.DurationSeconds)).Append("s/")
                .Append(Number(value.ElapsedSeconds + 0.125)).Append("s; warmup=").Append(Number(value.RearmSeconds))
                .Append("s/").Append(Number(value.RequiredRearmSeconds)).Append("s; position-gap=")
                .Append(Number(value.PositionGap)).Append("m/0.01m; batch=").Append(value.Batch.ToString(CultureInfo.InvariantCulture))
                .Append("; segments=").Append(value.SegmentCount.ToString(CultureInfo.InvariantCulture)).Append('/').Append(value.BufferSize.ToString(CultureInfo.InvariantCulture)).AppendLine();
        }
        public static string Format(SkateSessionHistory history, double now)
        {
            if (history == null) return "Diagnostics unavailable: the bounded connection store is full.";
            var text = new StringBuilder();
            if (history.HasSample) Observation(text, "Latest observation", history.LastSample, now);
            if (history.HasDisarm) Observation(text, "Last active disarm", history.LastDisarm, now);
            else text.AppendLine("Last active disarm: none recorded for this connection.");
            if (history.HasActivation) Observation(text, "Last successful activation", history.LastActivation, now);
            else text.AppendLine("Last successful activation: none recorded for this connection.");
            if (history.HasWarmupReset) Observation(text, "Last lost warmup", history.LastWarmupReset, now);
            text.Append("Counters (active disarms / successful activations / lost warmups):");
            bool any = false;
            for (SkateSessionReason reason = SkateSessionReason.None + 1; reason < SkateSessionReason.Count; reason++)
            {
                long d = history.DisarmCount(reason), a = history.ActivationCount(reason), r = history.WarmupResetCount(reason);
                if (d == 0 && a == 0 && r == 0) continue;
                any = true;
                text.Append(' ').Append(reason).Append('=').Append(d.ToString(CultureInfo.InvariantCulture)).Append('/').Append(a.ToString(CultureInfo.InvariantCulture)).Append('/').Append(r.ToString(CultureInfo.InvariantCulture)).Append(';');
            }
            if (!any) text.Append(" none.");
            return text.ToString();
        }
    }

    public sealed class SkateSessionBounds
    {
        public const double MaximumBatchSeconds = 0.5;
        public bool Active { get; private set; }
        public double AirSeconds { get; private set; }
        public SkateSessionObservation LastObservation { get; private set; }
        private double started;
        private double lastSample;
        private double lastSupported;
        private double duration;

        public static bool Eligible(bool connected, bool npc, bool alive, bool sleeping,
                                    bool wounded, bool mounted, bool swimming)
        { return connected && !npc && alive && !sleeping && !wounded && !mounted && !swimming; }

        public bool Begin(double now, bool supported)
        {
            SkateSessionObservation value = SkateSessionObservation.Empty(now, SkateSessionReason.None);
            value.FullySupported = supported; value.SupportKnown = true; value.WasActive = Active;
            value.AirSeconds = AirSeconds; value.DurationSeconds = duration; value.ElapsedSeconds = now - started;
            if (!Finite(now) || now < 0) value.Reason = SkateSessionReason.InvalidClock;
            else if (!supported) value.Reason = SkateSessionReason.Unsupported;
            else if (Active) value.Reason = SkateSessionReason.BeginRejected;
            LastObservation = value;
            if (value.Reason != SkateSessionReason.None) return false;
            Active = true;
            started = lastSample = lastSupported = now;
            duration = AirSeconds = 0;
            value.AirSeconds = value.DurationSeconds = value.ElapsedSeconds = 0;
            LastObservation = value;
            return true;
        }

        public void End() { Active = false; AirSeconds = 0; }

        public bool Observe(double now, double dt, double horizontalPath, double verticalPath,
                            bool fullySupported, double maxSpeed, double maxAir)
        {
            SkateSessionObservation value = SkateSessionObservation.Empty(now, SkateSessionReason.None);
            value.DeltaSeconds = dt; value.GapSeconds = now - lastSample;
            value.HorizontalPath = horizontalPath; value.VerticalPath = verticalPath;
            value.MaximumSpeed = maxSpeed; value.MaximumAirtime = maxAir;
            value.FullySupported = fullySupported; value.SupportKnown = true; value.WasActive = Active;
            value.AirSeconds = AirSeconds; value.DurationSeconds = duration + dt; value.ElapsedSeconds = now - started;
            if (!Active) value.Reason = SkateSessionReason.Inactive;
            else if (!Finite(now)) value.Reason = SkateSessionReason.InvalidClock;
            else if (now < lastSample) value.Reason = SkateSessionReason.BackwardsClock;
            else if (now - lastSample > MaximumBatchSeconds) value.Reason = SkateSessionReason.BatchGap;
            else if (!Finite(dt) || dt <= 0 || dt > MaximumBatchSeconds) value.Reason = SkateSessionReason.InvalidDelta;
            else if (!Finite(horizontalPath) || horizontalPath < 0) value.Reason = SkateSessionReason.InvalidHorizontalPath;
            else if (!Finite(verticalPath) || verticalPath < 0) value.Reason = SkateSessionReason.InvalidVerticalPath;
            else if (!Finite(maxSpeed) || maxSpeed <= 0) value.Reason = SkateSessionReason.InvalidMaximumSpeed;
            else if (!Finite(maxAir) || maxAir <= 0) value.Reason = SkateSessionReason.InvalidMaximumAirtime;
            else if (horizontalPath > maxSpeed * dt) value.Reason = SkateSessionReason.HorizontalSpeed;
            else if (verticalPath > 40 * dt) value.Reason = SkateSessionReason.VerticalLimit;
            else if (duration + dt > now - started + 0.125) value.Reason = SkateSessionReason.Duration;
            if (value.Reason != SkateSessionReason.None)
            {
                LastObservation = value;
                if (Active) End();
                return false;
            }
            double air = AirSeconds;
            if (!fullySupported || air > 0) air = Math.Max(air + dt, now - lastSupported);
            value.AirSeconds = air;
            if (air > maxAir)
            { value.Reason = SkateSessionReason.Airtime; LastObservation = value; End(); return false; }
            duration += dt;
            lastSample = now;
            AirSeconds = fullySupported ? 0 : air;
            if (fullySupported) lastSupported = now;
            LastObservation = value;
            return true;
        }

        public static bool Finite(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }

    public sealed class SkateSessionRearm
    {
        public double Seconds { get; private set; }
        public SkateSessionObservation LastObservation { get; private set; }
        private double started;
        private double last;
        private bool observing;

        public void Reset() { Seconds = 0; observing = false; }

        public bool Observe(double now, double dt, double horizontalPath, double verticalPath,
                            bool supported, double maxSpeed, double requiredSeconds)
        {
            SkateSessionObservation value = SkateSessionObservation.Empty(now, SkateSessionReason.None);
            value.DeltaSeconds = dt; value.GapSeconds = observing ? now - last : double.NaN;
            value.HorizontalPath = horizontalPath; value.VerticalPath = verticalPath;
            value.MaximumSpeed = maxSpeed; value.FullySupported = supported; value.SupportKnown = true;
            value.RearmSeconds = Seconds; value.RequiredRearmSeconds = requiredSeconds;
            value.DurationSeconds = Seconds + dt; value.ElapsedSeconds = observing ? now - started : dt;
            if (!SkateSessionBounds.Finite(now) || now < 0) value.Reason = SkateSessionReason.InvalidClock;
            else if (!SkateSessionBounds.Finite(dt) || dt <= 0 || dt > SkateSessionBounds.MaximumBatchSeconds) value.Reason = SkateSessionReason.InvalidDelta;
            else if (!SkateSessionBounds.Finite(horizontalPath) || horizontalPath < 0) value.Reason = SkateSessionReason.InvalidHorizontalPath;
            else if (!SkateSessionBounds.Finite(verticalPath) || verticalPath < 0) value.Reason = SkateSessionReason.InvalidVerticalPath;
            else if (!supported) value.Reason = SkateSessionReason.Unsupported;
            else if (!SkateSessionBounds.Finite(maxSpeed) || maxSpeed <= 0) value.Reason = SkateSessionReason.InvalidMaximumSpeed;
            else if (!SkateSessionBounds.Finite(requiredSeconds) || requiredSeconds < 0.5 || requiredSeconds > 10) value.Reason = SkateSessionReason.InvalidRearmSeconds;
            else if (horizontalPath > maxSpeed * dt) value.Reason = SkateSessionReason.HorizontalSpeed;
            else if (verticalPath > 40 * dt) value.Reason = SkateSessionReason.VerticalLimit;
            else if (now < dt) value.Reason = SkateSessionReason.ClockBeforeDelta;
            if (value.Reason != SkateSessionReason.None)
            { LastObservation = value; Reset(); return false; }
            if (!observing)
            { started = now - dt; last = now; observing = true; }
            else if (now <= last) value.Reason = SkateSessionReason.NonIncreasingClock;
            else if (now - last > SkateSessionBounds.MaximumBatchSeconds) value.Reason = SkateSessionReason.BatchGap;
            else if (Seconds + dt > now - started + 0.125) value.Reason = SkateSessionReason.Duration;
            if (value.Reason != SkateSessionReason.None)
            { LastObservation = value; Reset(); return false; }
            Seconds += dt;
            last = now;
            value.RearmSeconds = Seconds;
            LastObservation = value;
            return Seconds + 0.000000001 >= requiredSeconds;
        }
    }

    public readonly struct SkateMovementPoint
    {
        public readonly double X, Y, Z;
        public SkateMovementPoint(double x, double y, double z) { X = x; Y = y; Z = z; }
    }

    public static class SkateMovementPolicy
    {
        public const double MaximumFlyVerticalSpeed = 200;

        public static bool ValidTickCount(int count, int bufferSize) { return count >= 1 && count < bufferSize; }

        private static bool Coordinate(SkateMovementPoint point)
        { return SkateSessionBounds.Finite(point.X) && SkateSessionBounds.Finite(point.Y) &&
            SkateSessionBounds.Finite(point.Z) && Math.Abs(point.X) <= 100000 &&
            Math.Abs(point.Y) <= 100000 && Math.Abs(point.Z) <= 100000; }

        public static bool TryAccumulate(SkateMovementPoint from, SkateMovementPoint to, double horizontal,
                                         double vertical, out double nextHorizontal, out double nextVertical)
        {
            nextHorizontal = horizontal; nextVertical = vertical;
            if (!Coordinate(from) || !Coordinate(to) || !SkateSessionBounds.Finite(horizontal) || horizontal < 0 ||
                !SkateSessionBounds.Finite(vertical) || vertical < 0) return false;
            double x = to.X - from.X, z = to.Z - from.Z;
            double nextH = horizontal + Math.Sqrt(x * x + z * z), nextV = vertical + Math.Abs(to.Y - from.Y);
            if (!SkateSessionBounds.Finite(nextH) || !SkateSessionBounds.Finite(nextV)) return false;
            nextHorizontal = nextH; nextVertical = nextV;
            return true;
        }

        public static bool TryAllow(double dt, double horizontal, double maximumSpeed,
                                    out bool allowed, out SkateSessionReason reason)
        {
            allowed = false; reason = SkateSessionReason.None;
            if (!SkateSessionBounds.Finite(dt) || dt <= 0 || dt > SkateSessionBounds.MaximumBatchSeconds)
                reason = SkateSessionReason.InvalidDelta;
            else if (!SkateSessionBounds.Finite(horizontal) || horizontal < 0)
                reason = SkateSessionReason.InvalidHorizontalPath;
            else if (!SkateSessionBounds.Finite(maximumSpeed) || maximumSpeed < 1 || maximumSpeed > 50)
                reason = SkateSessionReason.InvalidMaximumSpeed;
            if (reason != SkateSessionReason.None) return false;
            allowed = horizontal <= maximumSpeed * dt;
            reason = allowed ? SkateSessionReason.None : SkateSessionReason.HorizontalSpeed;
            return true;
        }

        public static bool CanFilter(bool ready, bool validating, bool matchingBatch, bool matchingIdentity,
                                      bool movementAllowed, bool eligible, bool permitted, bool optedOut)
        { return ready && validating && matchingBatch && matchingIdentity && movementAllowed && eligible && permitted && !optedOut; }

        public static bool CanExemptFly(bool trusted, double dt, double vertical)
        {
            return trusted && SkateSessionBounds.Finite(dt) && dt > 0 && dt <= SkateSessionBounds.MaximumBatchSeconds &&
                SkateSessionBounds.Finite(vertical) && vertical >= 0 && vertical <= MaximumFlyVerticalSpeed * dt;
        }
    }

    public enum SkateParkSelectionAction { Next, Cancel, PlacementFailed, PlacementSucceeded }

    public struct SkateParkSelectionState
    {
        public readonly bool Selected;
        public readonly SkateParkKind Kind;
        public readonly double Expires;
        public SkateParkSelectionState(bool selected, SkateParkKind kind, double expires)
        { Selected = selected; Kind = kind; Expires = expires; }
    }

    public static class SkateParkSelections
    {
        public const double LifetimeSeconds = 120;
        public static bool ValidOrder(SkateParkKind[] order)
        {
            if (order == null || order.Length < 1 || order.Length > 8) return false;
            for (int i = 0; i < order.Length; i++)
            {
                if (SkateParkLayout.Name(order[i]) == null) return false;
                for (int j = 0; j < i; j++) if (order[i] == order[j]) return false;
            }
            return true;
        }
        private static bool Clock(double now) { return SkateSessionBounds.Finite(now) && now >= 0; }
        private static bool Valid(SkateParkSelectionState state)
        { return SkateParkLayout.Name(state.Kind) != null && SkateSessionBounds.Finite(state.Expires) &&
            state.Expires >= 0 && (!state.Selected || state.Expires > 0); }
        public static bool Active(SkateParkSelectionState state, double now)
        { return Valid(state) && Clock(now) && state.Selected && now <= state.Expires; }
        public static bool TrySelect(SkateParkKind kind, bool admin, bool ready, double now, out SkateParkSelectionState state)
        {
            state = new SkateParkSelectionState();
            double deadline = now + LifetimeSeconds;
            if (!admin || !ready || !Clock(now) || !SkateSessionBounds.Finite(deadline) || deadline <= now ||
                SkateParkLayout.Name(kind) == null) return false;
            state = new SkateParkSelectionState(true, kind, deadline);
            return true;
        }
        public static bool TryApply(SkateParkSelectionState state, SkateParkSelectionAction action, bool admin,
            bool ready, double now, SkateParkKind[] order, out SkateParkSelectionState result)
        {
            result = state;
            if (!admin || !Clock(now) || !Valid(state)) return false;
            switch (action)
            {
                case SkateParkSelectionAction.Next:
                    if (!ready || !ValidOrder(order)) return false;
                    int next = 0;
                    if (Active(state, now))
                        for (int i = 0; i < order.Length; i++)
                            if (order[i] == state.Kind) { next = (i + 1) % order.Length; break; }
                    SkateParkSelectionState selected;
                    if (!TrySelect(order[next], admin, ready, now, out selected)) return false;
                    result = selected; return true;
                case SkateParkSelectionAction.Cancel:
                    result = new SkateParkSelectionState(false, state.Kind, 0); return true;
                case SkateParkSelectionAction.PlacementFailed: return true;
                case SkateParkSelectionAction.PlacementSucceeded:
                    if (!ready || !Active(state, now)) return false;
                    result = new SkateParkSelectionState(false, state.Kind, 0); return true;
                default: return false;
            }
        }
        public static bool ValidGestureName(string name)
        { return !string.IsNullOrWhiteSpace(name) && name.Length <= 128 && name == name.Trim() && !name.Any(char.IsControl); }
        public static bool TryGesture(string name, string[][] aliases, out int index, out string error)
        {
            bool missing;
            return TryGesture(name, aliases, out index, out error, out missing);
        }
        public static bool TryGesture(string name, string[][] aliases, out int index, out string error, out bool missing)
        {
            index = -1; error = null; missing = false;
            if (!ValidGestureName(name)) { error = "Configured gesture name is invalid."; return false; }
            if (aliases == null) { error = "Gesture catalog is unavailable."; return false; }
            for (int i = 0; i < aliases.Length; i++)
            {
                if (aliases[i] == null || !aliases[i].Any(alias => string.Equals(alias, name, StringComparison.OrdinalIgnoreCase))) continue;
                if (index >= 0) { index = -1; error = "Gesture '" + name + "' is ambiguous."; return false; }
                index = i;
            }
            if (index >= 0) return true;
            missing = true;
            error = "Gesture '" + name + "' was not found in the native catalog.";
            return false;
        }
        public static string GestureCatalogNames(string[][] aliases)
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (aliases == null) return "";
            foreach (string[] row in aliases)
            {
                if (row == null) continue;
                string name = row.Length > 0 ? row[0] : null;
                if (!DisplayGestureName(name)) name = row.Length > 2 ? row[2] : null;
                if (DisplayGestureName(name) && seen.Add(name)) names.Add(name);
            }
            return string.Join(", ", names);
        }
        private static bool DisplayGestureName(string name)
        { return ValidGestureName(name) && name.IndexOf(',') < 0 && name.IndexOf('\u2028') < 0 && name.IndexOf('\u2029') < 0; }
    }

    // BEGIN SHARED SKATE PARK
    public enum SkateParkKind { Bank, Kicker, QuarterPipe, LedgeLow, LedgeHigh, LongLedge, Funbox, Halfpipe }
    public enum SkateParkMaterial { Stone, Metal, Wood }

    public struct SkateParkPart
    {
        public double Right, Forward, Height, Pitch, Yaw;
        public string Prefab;
        public SkateParkMaterial Material;
    }

    public struct SkateParkTransform
    {
        public double X, Y, Z, Yaw, Pitch;
    }

    public struct SkateParkBlock
    {
        public readonly string Prefab;
        public readonly double X, Y, Z, Yaw, Pitch;
        public SkateParkBlock(string prefab, double x, double y, double z, double yaw, double pitch)
        { Prefab = prefab; X = x; Y = y; Z = z; Yaw = yaw; Pitch = pitch; }
    }

    public static class SkateParkLayout
    {
        public const string FloorPrefab = "assets/prefabs/building core/floor/floor.prefab";
        public const string LowWallPrefab = "assets/prefabs/building core/wall.low/wall.low.prefab";
        public const string HalfWallPrefab = "assets/prefabs/building core/wall.half/wall.half.prefab";
        private const double HalfLength = 1.5;
        private const double Degrees = Math.PI / 180;
        public static string Name(SkateParkKind kind)
        {
            switch (kind)
            {
                case SkateParkKind.Bank: return "bank";
                case SkateParkKind.Kicker: return "kicker";
                case SkateParkKind.QuarterPipe: return "quarterpipe";
                case SkateParkKind.LedgeLow: return "ledge-low";
                case SkateParkKind.LedgeHigh: return "ledge-high";
                case SkateParkKind.LongLedge: return "long-ledge";
                case SkateParkKind.Funbox: return "funbox";
                case SkateParkKind.Halfpipe: return "halfpipe";
                default: return null;
            }
        }
        public static bool TryKind(string name, out SkateParkKind kind)
        {
            kind = SkateParkKind.Bank;
            if (name == null) return false;
            for (int i = 0; i <= (int)SkateParkKind.Halfpipe; i++)
                if (string.Equals(name, Name((SkateParkKind)i), StringComparison.OrdinalIgnoreCase))
                { kind = (SkateParkKind)i; return true; }
            return false;
        }
        public static int PartCount(SkateParkKind kind)
        {
            switch (kind)
            {
                case SkateParkKind.Bank: case SkateParkKind.Kicker:
                case SkateParkKind.LedgeLow: case SkateParkKind.LedgeHigh: return 1;
                case SkateParkKind.QuarterPipe: return 2;
                case SkateParkKind.LongLedge: case SkateParkKind.Funbox: return 3;
                case SkateParkKind.Halfpipe: return 36;
                default: return 0;
            }
        }
        public static bool TryPart(SkateParkKind kind, int index, out SkateParkPart part)
        {
            part = new SkateParkPart();
            if (index < 0 || index >= PartCount(kind)) return false;
            double low = HalfLength * Math.Sin(15 * Degrees);
            switch (kind)
            {
                case SkateParkKind.Bank: part.Height = HalfLength * Math.Sin(12 * Degrees); part.Pitch = -12; break;
                case SkateParkKind.Kicker: part.Height = HalfLength * Math.Sin(25 * Degrees); part.Pitch = -25; break;
                case SkateParkKind.QuarterPipe:
                    part.Height = index == 0 ? low : 2 * low + HalfLength * Math.Sin(35 * Degrees);
                    part.Forward = index == 0 ? 0 : HalfLength * (Math.Cos(15 * Degrees) + Math.Cos(35 * Degrees));
                    part.Pitch = index == 0 ? -15 : -35; break;
                case SkateParkKind.LedgeLow: part.Height = 0.4; break;
                case SkateParkKind.LedgeHigh: part.Height = 0.8; break;
                case SkateParkKind.LongLedge: part.Height = 0.4; part.Right = index == 0 ? 0 : index == 1 ? -3 : 3; break;
                case SkateParkKind.Funbox:
                    part.Height = index == 0 ? 2 * low : low;
                    part.Forward = index == 0 ? 0 : (index == 1 ? -1 : 1) * HalfLength * (1 + Math.Cos(15 * Degrees));
                    part.Pitch = index == 0 ? 0 : index == 1 ? -15 : 15; break;
                case SkateParkKind.Halfpipe: HalfpipePart(index, out part); break;
                default: return false;
            }
            if (part.Prefab == null) part.Prefab = FloorPrefab;
            return true;
        }
        private static void HalfpipePart(int index, out SkateParkPart part)
        {
            part = new SkateParkPart { Prefab = FloorPrefab };
            if (index < 4)
            { part.Right = (index % 2) * 3; part.Forward = (index / 2) * 3; return; }
            double lower = HalfLength * Math.Sin(15 * Degrees);
            double upper = 2 * lower + HalfLength * Math.Sin(35 * Degrees);
            double rise = 2 * lower + 2 * HalfLength * Math.Sin(35 * Degrees);
            double lowerRun = HalfLength * Math.Cos(15 * Degrees);
            double upperRun = HalfLength * Math.Cos(35 * Degrees);
            double backLower = -HalfLength - lowerRun;
            double backUpper = -HalfLength - 2 * lowerRun - upperRun;
            double frontLower = 3 + HalfLength + lowerRun;
            double frontUpper = 3 + HalfLength + 2 * lowerRun + upperRun;
            double backDeck = -3 - 2 * lowerRun - 2 * upperRun;
            double frontDeck = 6 + 2 * lowerRun + 2 * upperRun;
            part.Material = SkateParkMaterial.Metal;
            if (index < 16)
            {
                part.Right = (index % 2) * 3;
                switch ((index - 4) / 2)
                {
                    case 0: part.Forward = backLower; part.Height = lower; part.Pitch = 15; break;
                    case 1: part.Forward = backUpper; part.Height = upper; part.Pitch = 35; break;
                    case 2: part.Forward = frontLower; part.Height = lower; part.Pitch = -15; break;
                    case 3: part.Forward = frontUpper; part.Height = upper; part.Pitch = -35; break;
                    case 4: part.Forward = backDeck; part.Height = rise; break;
                    case 5: part.Forward = frontDeck; part.Height = rise; break;
                }
                return;
            }
            if (index < 20)
            {
                part.Prefab = LowWallPrefab; part.Material = SkateParkMaterial.Wood;
                part.Right = (index % 2) * 3; part.Height = rise;
                part.Forward = index < 18 ? backDeck - HalfLength : frontDeck + HalfLength;
                part.Yaw = index < 18 ? 180 : 0;
                return;
            }
            part.Prefab = HalfWallPrefab;
            part.Right = index % 2 == 0 ? -1.85 : 4.85;
            part.Yaw = index % 2 == 0 ? 90 : 270;
            switch ((index - 20) / 2)
            {
                case 0: part.Forward = 0; break;
                case 1: part.Forward = 3; break;
                case 2: part.Forward = backLower; part.Height = lower; break;
                case 3: part.Forward = backUpper; part.Height = upper; break;
                case 4: part.Forward = frontLower; part.Height = lower; break;
                case 5: part.Forward = frontUpper; part.Height = upper; break;
                case 6: part.Forward = backDeck; part.Height = rise; break;
                case 7: part.Forward = frontDeck; part.Height = rise; break;
            }
        }
        public static bool TryTransform(SkateParkKind kind, int index, double anchorX, double anchorY,
                                        double anchorZ, double anchorYaw, out SkateParkTransform transform)
        {
            transform = new SkateParkTransform();
            SkateParkPart part;
            if (!TryPart(kind, index, out part) || !Coordinate(anchorX) || !Coordinate(anchorY) || !Coordinate(anchorZ) ||
                !Finite(anchorYaw) || Math.Abs(anchorYaw) > 1000000) return false;
            double yaw = anchorYaw % 360;
            if (yaw < 0) yaw += 360;
            double sin = Math.Sin(yaw * Degrees), cos = Math.Cos(yaw * Degrees);
            double x = anchorX + part.Right * cos + part.Forward * sin;
            double y = anchorY + part.Height;
            double z = anchorZ - part.Right * sin + part.Forward * cos;
            if (!Coordinate(x) || !Coordinate(y) || !Coordinate(z)) return false;
            double partYaw = (yaw + part.Yaw) % 360;
            transform = new SkateParkTransform { X = x, Y = y, Z = z, Yaw = partYaw, Pitch = part.Pitch };
            return true;
        }
        public static bool TrySupportedAnchor(SkateParkKind kind, double supportTop, double floorCenterY,
            double floorCenterZ, double floorSizeY, double floorSizeZ, out double anchorY)
        {
            anchorY = 0;
            if (PartCount(kind) == 0 || !Coordinate(supportTop) || !Coordinate(floorCenterY) ||
                !Coordinate(floorCenterZ) || !Coordinate(floorSizeY) || !Coordinate(floorSizeZ) ||
                floorSizeY <= 0 || floorSizeZ <= 0) return false;
            double baseline = supportTop - (floorCenterY - floorSizeY / 2) + 0.025;
            for (int i = 0; i < PartCount(kind); i++)
            {
                SkateParkPart part;
                if (!TryPart(kind, i, out part)) return false;
                if (part.Prefab != FloorPrefab) continue;
                double cos = Math.Cos(part.Pitch * Degrees), sin = Math.Sin(part.Pitch * Degrees);
                double bottom = part.Height + floorCenterY * cos - floorCenterZ * sin -
                    floorSizeY / 2 * Math.Abs(cos) - floorSizeZ / 2 * Math.Abs(sin);
                baseline = Math.Max(baseline, supportTop + 0.025 - bottom);
            }
            if (!Coordinate(baseline)) return false;
            anchorY = baseline;
            return true;
        }
        public static bool TryAnchor(SkateParkKind kind, int index, double partX, double partY,
            double partZ, double partYaw, out double anchorX, out double anchorY,
            out double anchorZ, out double anchorYaw)
        {
            anchorX = anchorY = anchorZ = anchorYaw = 0;
            SkateParkPart part;
            if (!TryPart(kind, index, out part) || !Coordinate(partX) || !Coordinate(partY) ||
                !Coordinate(partZ) || !Angle(partYaw)) return false;
            double yaw = Normalize(partYaw - part.Yaw);
            double sin = Math.Sin(yaw * Degrees), cos = Math.Cos(yaw * Degrees);
            double x = partX - part.Right * cos - part.Forward * sin;
            double y = partY - part.Height;
            double z = partZ + part.Right * sin - part.Forward * cos;
            if (!Coordinate(x) || !Coordinate(y) || !Coordinate(z)) return false;
            anchorX = x; anchorY = y; anchorZ = z; anchorYaw = yaw;
            return true;
        }

        public static bool TryMatch(SkateParkKind kind, IReadOnlyList<SkateParkBlock> blocks,
            double tolerance, out double anchorX, out double anchorY, out double anchorZ, out double anchorYaw)
        {
            anchorX = anchorY = anchorZ = anchorYaw = 0;
            int count = PartCount(kind);
            if (count == 0 || count > 36 || blocks == null || blocks.Count != count || !Finite(tolerance) ||
                tolerance < 0 || tolerance > 0.1) return false;
            SkateParkBlock first = blocks[0];
            for (int i = 0; i < count; i++)
            {
                SkateParkBlock block = blocks[i];
                if (!Valid(block)) return false;
                if (Before(block, first)) first = block;
            }
            bool found = false;
            double foundX = 0, foundY = 0, foundZ = 0, foundYaw = 0;
            for (int index = 0; index < count; index++)
            {
                SkateParkPart part;
                if (!TryPart(kind, index, out part) || !string.Equals(first.Prefab, part.Prefab, StringComparison.Ordinal) ||
                    !CloseAngle(first.Pitch, part.Pitch, tolerance)) continue;
                double x, y, z, yaw;
                if (!TryAnchor(kind, index, first.X, first.Y, first.Z, first.Yaw, out x, out y, out z, out yaw) ||
                    !Match(kind, blocks, tolerance, x, y, z, yaw)) continue;
                if (found && (!ClosePosition(x, y, z, foundX, foundY, foundZ, tolerance) ||
                    !CloseAngle(yaw, foundYaw, tolerance))) return false;
                if (!found) { found = true; foundX = x; foundY = y; foundZ = z; foundYaw = yaw; }
            }
            if (!found) return false;
            anchorX = foundX; anchorY = foundY; anchorZ = foundZ; anchorYaw = foundYaw;
            return true;
        }

        public static bool VisualOrigin(SkateParkKind kind, out double right, out double forward,
            out double height, out double yaw)
        {
            right = forward = height = yaw = 0;
            SkateParkPart first;
            if (!TryPart(kind, 0, out first)) return false;
            switch (kind)
            {
                case SkateParkKind.Bank: case SkateParkKind.Kicker: case SkateParkKind.QuarterPipe:
                    forward = -HalfLength * Math.Cos(first.Pitch * Degrees); break;
                case SkateParkKind.Halfpipe: right = forward = HalfLength; break;
            }
            return true;
        }

        private static bool Match(SkateParkKind kind, IReadOnlyList<SkateParkBlock> blocks,
            double tolerance, double anchorX, double anchorY, double anchorZ, double anchorYaw)
        {
            int count = PartCount(kind);
            ulong used = 0;
            for (int index = 0; index < count; index++)
            {
                SkateParkPart part;
                SkateParkTransform expected;
                if (!TryPart(kind, index, out part) ||
                    !TryTransform(kind, index, anchorX, anchorY, anchorZ, anchorYaw, out expected)) return false;
                int match = -1;
                for (int observed = 0; observed < count; observed++)
                {
                    SkateParkBlock block = blocks[observed];
                    if (!string.Equals(part.Prefab, block.Prefab, StringComparison.Ordinal) ||
                        !ClosePosition(block.X, block.Y, block.Z, expected.X, expected.Y, expected.Z, tolerance) ||
                        !CloseAngle(block.Yaw, expected.Yaw, tolerance) ||
                        !CloseAngle(block.Pitch, expected.Pitch, tolerance)) continue;
                    if (match >= 0) return false;
                    match = observed;
                }
                if (match < 0 || (used & (1UL << match)) != 0) return false;
                used |= 1UL << match;
            }
            return used == (1UL << count) - 1;
        }

        private static bool Valid(SkateParkBlock block)
        { return block.Prefab != null && Coordinate(block.X) && Coordinate(block.Y) && Coordinate(block.Z) &&
            Angle(block.Yaw) && Angle(block.Pitch); }

        private static bool Before(SkateParkBlock a, SkateParkBlock b)
        {
            int prefab = string.Compare(a.Prefab, b.Prefab, StringComparison.Ordinal);
            if (prefab != 0) return prefab < 0;
            if (a.X != b.X) return a.X < b.X;
            if (a.Y != b.Y) return a.Y < b.Y;
            if (a.Z != b.Z) return a.Z < b.Z;
            double yawA = Normalize(a.Yaw), yawB = Normalize(b.Yaw);
            if (yawA != yawB) return yawA < yawB;
            return Normalize(a.Pitch) < Normalize(b.Pitch);
        }

        private static bool ClosePosition(double x, double y, double z, double otherX, double otherY,
            double otherZ, double tolerance)
        {
            double dx = x - otherX, dy = y - otherY, dz = z - otherZ;
            return dx * dx + dy * dy + dz * dz <= tolerance * tolerance;
        }

        private static bool CloseAngle(double a, double b, double tolerance)
        {
            double delta = (a - b) % 360;
            if (delta < -180) delta += 360;
            else if (delta > 180) delta -= 360;
            return Math.Abs(delta) <= tolerance;
        }

        private static double Normalize(double angle)
        {
            double value = angle % 360;
            if (value < 0) value += 360;
            return value >= 360 || value == 0 ? 0 : value;
        }

        private static bool Angle(double value)
        { return Finite(value) && Math.Abs(value) <= 1000000; }

        private static bool Finite(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }

        private static bool Coordinate(double value)
        { return Finite(value) && Math.Abs(value) <= 100000; }
    }

    // END SHARED SKATE PARK

    public struct SkatePracticePart
    {
        public float Right, Forward, Height, Pitch;
    }

    public static class SkatePracticeLayout
    {
        public const string FloorPrefab = "assets/prefabs/building core/floor/floor.prefab";
        public const int PartCount = 8;

        public static bool TryPart(int index, out SkatePracticePart part)
        {
            part = new SkatePracticePart();
            switch (index)
            {
                case 0: part = new SkatePracticePart { Right = -4.5f, Forward = -3, Height = 0.39f, Pitch = -15 }; break;
                case 1: part = new SkatePracticePart { Right = 0, Forward = -3, Height = 0.75f, Pitch = -30 }; break;
                case 2: part = new SkatePracticePart { Right = 4.5f, Forward = -3, Height = 0.312f, Pitch = -12 }; break;
                case 3: part = new SkatePracticePart { Right = -4.5f, Forward = 1.5f, Height = 0.4f }; break;
                case 4: part = new SkatePracticePart { Right = 0, Forward = 1.5f, Height = 0.8f }; break;
                case 5: part = new SkatePracticePart { Right = -3, Forward = 6, Height = 0.4f }; break;
                case 6: part = new SkatePracticePart { Right = 0, Forward = 6, Height = 0.4f }; break;
                case 7: part = new SkatePracticePart { Right = 3, Forward = 6, Height = 0.4f }; break;
                default: return false;
            }
            return true;
        }
    }
}

namespace Shortcut.RustMod
{
    public static class SkateParkPlacement
    {
        public static bool TryRequest(string[] args, out SkateParkKind kind, out bool explicitAnchor,
            out double x, out double y, out double z, out double yaw, out string failure)
        {
            kind = default(SkateParkKind); explicitAnchor = false;
            x = y = z = yaw = 0;
            failure = "Usage: skate.place <bank|kicker|quarterpipe|ledge-low|ledge-high|long-ledge|funbox|halfpipe> [x y z yaw].";
            if (args == null || (args.Length != 1 && args.Length != 5)) return false;
            SkateParkKind parsed;
            if (!SkateParkLayout.TryKind(args[0], out parsed))
            { failure = "Unknown park piece. Use bank, kicker, quarterpipe, ledge-low, ledge-high, long-ledge, funbox or halfpipe."; return false; }
            if (args.Length == 1) { kind = parsed; failure = null; return true; }
            var values = new double[4];
            string[] names = { "x", "y", "z", "yaw" };
            for (int i = 0; i < values.Length; i++)
            {
                if (!double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) ||
                    !SkateSessionBounds.Finite(values[i]) || Math.Abs(values[i]) > (i == 3 ? 1000000 : 100000))
                { failure = "Invalid " + names[i] + ": use a finite invariant number within " + (i == 3 ? "1000000 degrees." : "100000 metres."); return false; }
            }
            kind = parsed; explicitAnchor = true;
            x = values[0]; y = values[1]; z = values[2]; yaw = (values[3] % 360 + 360) % 360;
            failure = null; return true;
        }
    }
}

namespace Oxide.Plugins
{
    [Info("ShortcutSkate", "Dj-Shortcut", "0.3.8")]
    [Description("Player-scoped skateboard movement bounds and saved park assemblies.")]
    public sealed class ShortcutSkate : RustPlugin
    {
        private const string UsePermission = "shortcutskate.use";
        private const string FlyPermission = "shortcutskate.fly";
        private static ShortcutSkate instance;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Dictionary<ulong, Session> sessions = new Dictionary<ulong, Session>();
        private readonly Dictionary<ulong, BasePlayer> optedOut = new Dictionary<ulong, BasePlayer>();
        private readonly List<BaseEntity> practice = new List<BaseEntity>();
        private readonly SkateDiagnosticsStore diagnostics = new SkateDiagnosticsStore();
        private static readonly Func<object, bool> ConnectedIdentity = identity =>
        {
            BasePlayer player = identity as BasePlayer;
            return player != null && player.IsConnected;
        };
        private SkateSessionReason unavailableReason = SkateSessionReason.Unavailable;
        private Settings settings;
        private int groundMask;
        private long batch;
        private bool validating;
        private bool ready;

        private const int MaximumParkGroups = 256;
        private const int MaximumParkSelections = 128;
        private const int MaximumParkDataLength = 1048576;
        private const float MaximumParkPlacementDistance = 25;
        private const string ParkFoundationPrefab = "assets/prefabs/building core/foundation/foundation.prefab";
        private readonly Dictionary<ulong, ParkSelection> parkSelections = new Dictionary<ulong, ParkSelection>();
        private object parkSelectionEpoch = new object();
        private readonly Dictionary<ulong, ParkGroup> parkMembers = new Dictionary<ulong, ParkGroup>();
        private readonly List<BaseEntity> parkRollback = new List<BaseEntity>();
        private ParkData parkData;
        private bool parkDataBroken;
        private bool parkStorageFailed;
        private string parkStorageError;
        private bool parkReady;
        private bool parkBusy;
        private bool parkRefreshing;
        private bool parkClosing;
        private const string GesturePatchId = "ShortcutSkate.ParkGestures";
        private Harmony gestureHarmony;
        private bool gestureAvailable;
        private bool gestureShape;
        private bool gestureNativeHook;
        private string gestureFailure = "Gesture selection has not initialized.";
        private bool gestureNameMissing;
        private string gestureCatalogNames;
        private GestureConfig parkNextGesture, parkCancelGesture;
        private SkateParkKind[] parkGestureOrder;
        private readonly HashSet<string> parkDemolitionPending = new HashSet<string>(StringComparer.Ordinal);
        private BuildingBlock parkDemolitionHookBlock;
        private BasePlayer parkDemolitionHookPlayer;
        private bool parkDemolitionHookImmediate;
        private string ParkFilename { get { return Path.Combine(Interface.Oxide.DataFileSystem.Directory, "ShortcutSkate.Parks.json"); } }
        private const int MaximumGroundedMembers = 4096;
        private readonly Dictionary<ulong, StabilityEntity> groundedEntities = new Dictionary<ulong, StabilityEntity>();
        private readonly Dictionary<ulong, GroundedMember> groundedMembers = new Dictionary<ulong, GroundedMember>();
        private readonly Dictionary<ulong, StabilityEntity> groundedPending = new Dictionary<ulong, StabilityEntity>();
        private GroundedData groundedData;
        private bool groundedDataBroken, groundedStorageFailed, groundedRefreshing, groundedBusy, groundedReady;
        private string GroundedFilename { get { return Path.Combine(Interface.Oxide.DataFileSystem.Directory, "ShortcutSkate.Grounded.json"); } }

        public sealed class GroundedData
        {
            [JsonProperty(Required = Required.Always)] public int Version;
            [JsonProperty(Required = Required.Always)] public string WipeId;
            [JsonProperty(Required = Required.Always)] public long SaveCreatedTicks;
            [JsonProperty(Required = Required.Always)] public uint Seed, Size;
            [JsonProperty(Required = Required.Always)] public string Checksum;
            [JsonProperty(Required = Required.Always)] public List<GroundedMember> Members;
        }

        public sealed class GroundedMember
        {
            [JsonProperty(Required = Required.Always), JsonConverter(typeof(ParkIdConverter))] public ulong Id, Owner;
            [JsonProperty(Required = Required.Always)] public uint Prefab;
            [JsonProperty(Required = Required.Always), JsonConverter(typeof(GroundedIdConverter))] public ulong ParentId, ParentOwner;
            [JsonProperty(Required = Required.Always)] public uint ParentPrefab, ParentBone;
            [JsonProperty(Required = Required.Always)] public double X, Y, Z, RotX, RotY, RotZ;
        }

        public sealed class GroundedIdConverter : JsonConverter
        {
            public override bool CanConvert(Type type) { return type == typeof(ulong); }
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            { writer.WriteValue(((ulong)value).ToString(CultureInfo.InvariantCulture)); }
            public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer)
            {
                ulong value;
                string text = reader.TokenType == JsonToken.String ? reader.Value as string : null;
                if (text == null || !ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) ||
                    text != value.ToString(CultureInfo.InvariantCulture))
                    throw new InvalidDataException("Grounded parent IDs must be canonical unsigned decimal strings.");
                return value;
            }
        }

        public static GroundedData ParseGroundedData(string json)
        {
            if (json == null || json.Length == 0 || json.Length > MaximumParkDataLength)
                throw new InvalidDataException("Grounded building data is empty or exceeds one MiB.");
            using (var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 16 })
            {
                var keys = new Stack<HashSet<string>>();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonToken.StartObject) keys.Push(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    else if (reader.TokenType == JsonToken.StartArray) keys.Push(null);
                    else if (reader.TokenType == JsonToken.EndObject || reader.TokenType == JsonToken.EndArray) keys.Pop();
                    else if (reader.TokenType == JsonToken.PropertyName && !keys.Peek().Add((string)reader.Value))
                        throw new InvalidDataException("Duplicate grounded building data property.");
                }
            }
            GroundedData value = JsonConvert.DeserializeObject<GroundedData>(json, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.None, MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Error, MaxDepth = 16
            });
            if (value == null || value.Version != 1 || string.IsNullOrEmpty(value.WipeId) || value.WipeId.Length > 128 ||
                value.SaveCreatedTicks <= 0 || value.SaveCreatedTicks > DateTime.MaxValue.Ticks ||
                value.SaveCreatedTicks % TimeSpan.TicksPerSecond != 0 || value.Checksum == null || value.Checksum.Length > 256 ||
                value.Members == null || value.Members.Count > MaximumGroundedMembers)
                throw new InvalidDataException("Invalid grounded building world identity or member count.");
            var ids = new HashSet<ulong>();
            foreach (GroundedMember member in value.Members)
                if (!ValidGroundedMember(member) || !ids.Add(member.Id))
                    throw new InvalidDataException("Invalid or duplicate grounded building identity or pose.");
            return value;
        }

        private static bool GroundedCoordinate(double value)
        { return SkateSessionBounds.Finite(value) && Math.Abs(value) <= 100000; }

        private static bool GroundedAngle(double value)
        { return SkateSessionBounds.Finite(value) && value >= 0 && value < 360; }

        private static bool ValidGroundedMember(GroundedMember member)
        {
            return member != null && member.Id != 0 && member.Owner != 0 && member.Prefab != 0 && member.ParentId != member.Id &&
                (member.ParentId == 0 ? member.ParentPrefab == 0 && member.ParentOwner == 0 && member.ParentBone == 0 : member.ParentPrefab != 0) &&
                GroundedCoordinate(member.X) && GroundedCoordinate(member.Y) && GroundedCoordinate(member.Z) &&
                GroundedAngle(member.RotX) && GroundedAngle(member.RotY) && GroundedAngle(member.RotZ);
        }

        internal static bool SameGroundedGeneration(GroundedData data, GroundedData current)
        {
            return data != null && current != null && !string.IsNullOrEmpty(current.WipeId) && current.WipeId.Length <= 128 &&
                current.SaveCreatedTicks > 0 && current.SaveCreatedTicks <= DateTime.MaxValue.Ticks &&
                current.SaveCreatedTicks % TimeSpan.TicksPerSecond == 0 && data.WipeId == current.WipeId &&
                data.SaveCreatedTicks == current.SaveCreatedTicks && data.Seed == current.Seed && data.Size == current.Size;
        }

        public sealed class ParkData
        {
            [JsonProperty(Required = Required.Always)] public int Version;
            [JsonProperty(Required = Required.Always)] public string WipeId;
            [JsonProperty(Required = Required.Always)] public long SaveCreatedTicks;
            [JsonProperty(Required = Required.Always)] public uint Seed;
            [JsonProperty(Required = Required.Always)] public uint Size;
            [JsonProperty(Required = Required.Always)] public string Checksum;
            [JsonProperty(Required = Required.Always)] public List<ParkGroup> Groups;
        }

        public sealed class ParkGroup
        {
            [JsonProperty(Required = Required.Always)] public string Id;
            [JsonProperty(Required = Required.Always), JsonConverter(typeof(ParkIdConverter))] public ulong Owner;
            [JsonProperty(Required = Required.Always)] public string Kind;
            [JsonProperty(Required = Required.Always)] public double AnchorX, AnchorY, AnchorZ, AnchorYaw;
            [JsonProperty(Required = Required.Always)] public List<ParkMember> Members;
        }

        public sealed class ParkMember
        {
            [JsonProperty(Required = Required.Always)] public int Index;
            [JsonProperty(Required = Required.Always), JsonConverter(typeof(ParkIdConverter))] public ulong Id;
            [JsonProperty(Required = Required.Always)] public uint Prefab;
            [JsonProperty(Required = Required.Always)] public double X, Y, Z, Yaw, Pitch;
        }

        public sealed class ParkIdConverter : JsonConverter
        {
            public override bool CanConvert(Type type) { return type == typeof(ulong); }
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            { writer.WriteValue(((ulong)value).ToString(CultureInfo.InvariantCulture)); }
            public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer)
            {
                ulong value;
                string text = reader.TokenType == JsonToken.String ? reader.Value as string : null;
                if (text == null || !ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) ||
                    value == 0 || text != value.ToString(CultureInfo.InvariantCulture))
                    throw new InvalidDataException("Park owner/member IDs must be canonical nonzero unsigned decimal strings.");
                return value;
            }
        }

        private sealed class ParkSelection
        {
            public BasePlayer Player;
            public SkateParkSelectionState State;
            public SkateParkKind Kind { get { return State.Kind; } }
            public double Expires { get { return State.Expires; } }
            public bool Pending;
        }

        private sealed class ParkAnchorSnapshot
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public ulong Owner;
            public uint Building;
        }

        private sealed class ParkNativePart
        {
            public string Prefab;
            public Construction Construction;
            public DeployVolume[] Volumes;
            public BuildingGrade.Enum Grade;
        }

        private sealed class ParkPlacementRequest
        {
            public BasePlayer Player;
            public Planner Planner;
            public Item PlannerItem;
            public ItemContainer Belt;
            public ParkData Data;
            public BuildingBlock Foundation;
            public ulong FoundationId;
            public ParkAnchorSnapshot Snapshot;
            public ParkSelection Selection;
            public uint Building;
        }

        public static ParkData ParseParkData(string json)
        {
            if (json == null || json.Length == 0 || json.Length > MaximumParkDataLength)
                throw new InvalidDataException("Park data is empty or exceeds one MiB.");
            using (var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 16 })
            {
                var keys = new Stack<HashSet<string>>();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonToken.StartObject) keys.Push(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    else if (reader.TokenType == JsonToken.StartArray) keys.Push(null);
                    else if (reader.TokenType == JsonToken.EndObject || reader.TokenType == JsonToken.EndArray) keys.Pop();
                    else if (reader.TokenType == JsonToken.PropertyName && !keys.Peek().Add((string)reader.Value))
                        throw new InvalidDataException("Duplicate park data property.");
                }
            }
            ParkData value = JsonConvert.DeserializeObject<ParkData>(json, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.None, MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Error, MaxDepth = 16
            });
            if (value == null || value.Version != 1 || string.IsNullOrEmpty(value.WipeId) || value.WipeId.Length > 128 ||
                value.SaveCreatedTicks <= 0 || value.SaveCreatedTicks > DateTime.MaxValue.Ticks ||
                value.SaveCreatedTicks % TimeSpan.TicksPerSecond != 0 ||
                value.Checksum == null || value.Checksum.Length > 256 || value.Groups == null || value.Groups.Count > MaximumParkGroups)
                throw new InvalidDataException("Invalid park world identity or group count.");
            var groups = new HashSet<string>(StringComparer.Ordinal);
            var members = new HashSet<ulong>();
            foreach (ParkGroup group in value.Groups)
            {
                Guid id;
                SkateParkKind kind;
                if (group == null || !Guid.TryParseExact(group.Id, "N", out id) || group.Id != id.ToString("N") ||
                    !groups.Add(group.Id) || group.Owner == 0 ||
                    !SkateParkLayout.TryKind(group.Kind, out kind) || group.Kind != SkateParkLayout.Name(kind) ||
                    group.Members == null || group.Members.Count < 1 || group.Members.Count > SkateParkLayout.PartCount(kind))
                    throw new InvalidDataException("Invalid park group identity or kind.");
                var indices = new HashSet<int>();
                foreach (ParkMember member in group.Members)
                {
                    SkateParkTransform expected;
                    if (member == null || member.Id == 0 || member.Prefab == 0 || !members.Add(member.Id) || !indices.Add(member.Index) ||
                        !SkateParkLayout.TryTransform(kind, member.Index, group.AnchorX, group.AnchorY, group.AnchorZ,
                            group.AnchorYaw, out expected) || !Near(member.X, expected.X) || !Near(member.Y, expected.Y) ||
                        !Near(member.Z, expected.Z) || !Near(member.Yaw, expected.Yaw) || !Near(member.Pitch, expected.Pitch))
                        throw new InvalidDataException("Invalid or duplicate park member identity or layout.");
                }
            }
            return value;
        }

        private static bool Near(double a, double b)
        { return SkateSessionBounds.Finite(a) && SkateSessionBounds.Finite(b) && Math.Abs(a - b) <= 0.002; }

        public sealed class Settings
        {
            [JsonProperty(Required = Required.Always)] public int Version;
            [JsonProperty(Required = Required.Always)] public bool AllowEveryone;
            [JsonProperty(Required = Required.Always)] public double MaximumHorizontalSpeed;
            [JsonProperty(Required = Required.Always)] public double MaximumAirtime;
            [JsonProperty(Required = Required.DisallowNull)] public bool AutomaticEligiblePlayers = true;
            [JsonProperty(Required = Required.DisallowNull)] public double AutomaticRearmSeconds = 1.5;
            [JsonProperty(Required = Required.DisallowNull)] public bool GroundedBuilding = false;
            [JsonProperty(Required = Required.DisallowNull)] public string ParkNextGesture = "I need wood";
            [JsonProperty(Required = Required.DisallowNull)] public string ParkCancelGesture = "Thumbs up";
            [JsonProperty(Required = Required.DisallowNull)] public string[] ParkPieceOrder =
                { "halfpipe", "bank", "kicker", "quarterpipe", "ledge-low", "ledge-high", "long-ledge", "funbox" };
        }

        private sealed class Session
        {
            public BasePlayer Player;
            public readonly SkateSessionBounds Bounds = new SkateSessionBounds();
            public readonly SkateSessionRearm Rearm = new SkateSessionRearm();
            public bool Manual;
            public bool HasEnd;
            public long Batch;
            public bool Allowed;
            public bool MovementAllowed;
            public double MovementSeconds = double.NaN, MovementVerticalPath = double.NaN;
            public SkateSessionReason MovementReason = SkateSessionReason.Inactive;
            public Vector3 LastEnd;
            public double LastObserved;
            public SkateSessionHistory History;
            public SkateSessionObservation Observation;
            public double SaidAt = -99;
        }

        protected override void LoadDefaultConfig()
        {
            Config.WriteObject(new Settings { Version = 1, AllowEveryone = true,
                MaximumHorizontalSpeed = 25, MaximumAirtime = 2 }, true);
        }

        protected override void LoadConfig()
        {
            ready = false;
            unavailableReason = SkateSessionReason.Unavailable;
            settings = null;
            gestureAvailable = false;
            gestureNameMissing = false; gestureCatalogNames = null;
            gestureFailure = "Valid gesture configuration is required.";
            try
            {
                base.LoadConfig();
                settings = ParseSettings(File.ReadAllText(Config.Filename));
            }
            catch (Exception error)
            { PrintError("Skating is disabled; configuration is preserved. " + error.Message); }
        }

        public static Settings ParseSettings(string json)
        {
            var value = JsonConvert.DeserializeObject<Settings>(json, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.None,
                MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Error
            });
            if (value == null || value.Version != 1 ||
                !SkateSessionBounds.Finite(value.MaximumHorizontalSpeed) ||
                value.MaximumHorizontalSpeed < 1 || value.MaximumHorizontalSpeed > 50 ||
                !SkateSessionBounds.Finite(value.MaximumAirtime) ||
                value.MaximumAirtime < 0.1 || value.MaximumAirtime > 2 ||
                !SkateSessionBounds.Finite(value.AutomaticRearmSeconds) ||
                value.AutomaticRearmSeconds < 0.5 || value.AutomaticRearmSeconds > 10)
                throw new InvalidDataException("Use Version 1, speed 1-50 m/s, airtime 0.1-2 seconds and rearm 0.5-10 seconds.");
            if (!SkateParkSelections.ValidGestureName(value.ParkNextGesture) || !SkateParkSelections.ValidGestureName(value.ParkCancelGesture) ||
                string.Equals(value.ParkNextGesture, value.ParkCancelGesture, StringComparison.OrdinalIgnoreCase) ||
                value.ParkPieceOrder == null || value.ParkPieceOrder.Length < 1 || value.ParkPieceOrder.Length > 8)
                throw new InvalidDataException("Use distinct valid gesture names and a unique park-piece order containing 1-8 pieces.");
            var order = new SkateParkKind[value.ParkPieceOrder.Length];
            for (int i = 0; i < order.Length; i++)
                if (!SkateParkLayout.TryKind(value.ParkPieceOrder[i], out order[i]))
                    throw new InvalidDataException("ParkPieceOrder contains an unknown park piece.");
            if (!SkateParkSelections.ValidOrder(order)) throw new InvalidDataException("ParkPieceOrder contains duplicate park pieces.");
            return value;
        }

        private void Init()
        {
            permission.RegisterPermission(UsePermission, this);
            permission.RegisterPermission(FlyPermission, this);
            instance = this;
            groundMask = LayerMask.GetMask("Terrain", "World", "Construction", "Deployed");
            ReadParkData();
            ReadGroundedData();
        }

        private void OnServerInitialized()
        {
            InitializeGroundedBuildings();
            InitializeParks();
            InitializeParkGestures();
            ready = settings != null && Patched("ValidateMoves", typeof(MovePatch), false) &&
                Patched("AreSpeeding", typeof(SpeedPatch), true) && Patched("AreFlying", typeof(FlyPatch), true);
            if (!ready) PrintError("Skating is disabled: matching movement patches and valid config are required.");
            else Puts("Skating ready: movement patches installed; permitted eligible players up to " + settings.MaximumHorizontalSpeed +
                " m/s; fly exemption requires a server admin or shortcutskate.fly and vertical travel up to 200 m/s; support, airtime and rearm are diagnostic only.");
            timer.Every(0.1f, Sweep);
        }

        private static bool Patched(string name, Type patch, bool postfix)
        {
            var method = AccessTools.Method(typeof(AntiHack), name);
            var info = method == null ? null : Harmony.GetPatchInfo(method);
            if (info == null) return false;
            return (postfix ? info.Postfixes : info.Prefixes).Any(p => p.PatchMethod.DeclaringType == patch) &&
                (postfix || info.Finalizers.Any(p => p.PatchMethod.DeclaringType == patch));
        }

        private void InitializeParkGestures()
        {
            gestureAvailable = false; gestureShape = false; gestureNativeHook = false;
            gestureNameMissing = false; gestureCatalogNames = null;
            parkNextGesture = parkCancelGesture = null; parkGestureOrder = null;
            try
            {
                if (settings == null) throw new InvalidDataException("Valid gesture configuration is required.");
                var collection = GestureCollection.Instance;
                if (collection == null || collection.AllGestures == null)
                    throw new InvalidDataException("GestureCollection.Instance.AllGestures is unavailable.");
                var configs = new List<GestureConfig>();
                foreach (GestureConfig gesture in collection.AllGestures)
                    if (gesture != null && !configs.Any(item => ReferenceEquals(item, gesture))) configs.Add(gesture);
                var aliases = new string[configs.Count][];
                for (int i = 0; i < configs.Count; i++)
                {
                    var gesture = configs[i];
                    aliases[i] = new[] { gesture.gestureName == null ? null : gesture.gestureName.english,
                        gesture.gestureName == null ? null : gesture.gestureName.token, gesture.convarName, gesture.gestureCommand };
                }
                int next, cancel;
                gestureCatalogNames = SkateParkSelections.GestureCatalogNames(aliases);
                string nextError, cancelError;
                bool nextMissing, cancelMissing;
                bool nextFound = SkateParkSelections.TryGesture(settings.ParkNextGesture, aliases, out next, out nextError, out nextMissing);
                bool cancelFound = SkateParkSelections.TryGesture(settings.ParkCancelGesture, aliases, out cancel, out cancelError, out cancelMissing);
                gestureNameMissing = nextMissing || cancelMissing;
                if (!nextFound) throw new InvalidDataException("Next: " + nextError);
                if (!cancelFound) throw new InvalidDataException("Cancel: " + cancelError);
                if (next == cancel) throw new InvalidDataException("Next and cancel resolve to the same native gesture.");
                parkNextGesture = configs[next]; parkCancelGesture = configs[cancel];
                parkGestureOrder = new SkateParkKind[settings.ParkPieceOrder.Length];
                for (int i = 0; i < parkGestureOrder.Length; i++)
                    if (!SkateParkLayout.TryKind(settings.ParkPieceOrder[i], out parkGestureOrder[i]))
                        throw new InvalidDataException("ParkPieceOrder contains an unknown park piece.");
                if (!SkateParkSelections.ValidOrder(parkGestureOrder)) throw new InvalidDataException("ParkPieceOrder is invalid.");
                MethodInfo method = typeof(BasePlayer).GetMethod("Server_StartGesture", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(GestureConfig), typeof(BasePlayer.GestureStartSource), typeof(bool) }, null);
                if (method == null || method.ReturnType != typeof(void))
                    throw new InvalidDataException("The native accepted-start method is unavailable.");
                gestureHarmony = new Harmony(GesturePatchId);
                gestureHarmony.UnpatchAll(GesturePatchId);
                gestureHarmony.Patch(method, transpiler: new HarmonyMethod(typeof(ShortcutSkate).GetMethod(
                    "GestureTranspiler", BindingFlags.Static | BindingFlags.NonPublic)));
                var patches = Harmony.GetPatchInfo(method);
                if (!gestureShape || patches == null || !patches.Transpilers.Any(p => p.owner == GesturePatchId))
                    throw new InvalidDataException(gestureFailure ?? "The accepted-start bridge was not registered.");
                gestureAvailable = true; gestureFailure = null;
            }
            catch (Exception error)
            { gestureFailure = error.Message; PrintWarning("Park gesture selection is unavailable: " + gestureFailure); }
        }

        internal static IList<CodeInstruction> BridgeInstructions(IList<CodeInstruction> instructions, FieldInfo field,
            MethodInfo dispatcher, out bool nativeHook, out string reason)
        {
            nativeHook = false; reason = null;
            if (instructions == null || instructions.Any(i => i == null || i.labels == null || i.blocks == null) || field == null || field.IsStatic ||
                dispatcher == null || !dispatcher.IsStatic || dispatcher.ReturnType != typeof(void))
            { reason = "Accepted-start bridge inputs are unavailable."; return instructions; }
            var parameters = dispatcher.GetParameters();
            if (parameters.Length != 2 || parameters[0].ParameterType != field.DeclaringType || parameters[1].ParameterType != field.FieldType)
            { reason = "Accepted-start dispatcher signature is incompatible."; return instructions; }
            int store = -1, stores = 0;
            for (int i = 0; i < instructions.Count; i++)
                if (instructions[i].opcode == OpCodes.Stfld && Equals(instructions[i].operand, field)) { store = i; stores++; }
            if (stores != 1 || store < 2 || store + 1 >= instructions.Count ||
                instructions[store - 2].opcode != OpCodes.Ldarg_0 || instructions[store - 1].opcode != OpCodes.Ldarg_1)
            { reason = "The accepted currentGesture assignment is unavailable or ambiguous."; return instructions; }
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.operand is Label && instructions[store + 1].labels.Contains((Label)instruction.operand))
                { reason = "A branch can bypass the accepted-start dispatch point."; return instructions; }
                var labels = instruction.operand as Label[];
                if (labels != null && labels.Any(label => instructions[store + 1].labels.Contains(label)))
                { reason = "A switch can bypass the accepted-start dispatch point."; return instructions; }
            }
            int hook = -1, hooks = 0;
            for (int i = 0; i < instructions.Count; i++)
                if (instructions[i].opcode == OpCodes.Ldstr && Equals(instructions[i].operand, "OnPlayerGestureStarted")) { hook = i; hooks++; }
            if (hooks != 0)
            {
                MethodInfo call = hook >= 0 && hook + 4 < instructions.Count ? instructions[hook + 3].operand as MethodInfo : null;
                var args = call == null ? null : call.GetParameters();
                if (hooks != 1 || hook != store + 1 || hook + 4 >= instructions.Count ||
                    instructions[hook + 1].opcode != OpCodes.Ldarg_0 || instructions[hook + 2].opcode != OpCodes.Ldarg_1 ||
                    instructions[hook + 3].opcode != OpCodes.Call || instructions[hook + 4].opcode != OpCodes.Pop ||
                    call == null || !call.IsStatic || call.DeclaringType != typeof(Interface) || call.Name != "CallHook" || call.ReturnType != typeof(object) ||
                    args.Length != 3 || args[0].ParameterType != typeof(string) || args[1].ParameterType != typeof(object) || args[2].ParameterType != typeof(object))
                { reason = "An unrecognized native started-hook dispatcher prevents safe bridging."; return instructions; }
                for (int i = hook + 1; i <= hook + 4; i++)
                    foreach (CodeInstruction instruction in instructions)
                    {
                        if (instruction.operand is Label && instructions[i].labels.Contains((Label)instruction.operand))
                        { reason = "A branch enters the native started-hook dispatcher."; return instructions; }
                        var labels = instruction.operand as Label[];
                        if (labels != null && labels.Any(label => instructions[i].labels.Contains(label)))
                        { reason = "A switch enters the native started-hook dispatcher."; return instructions; }
                    }
                nativeHook = true; return instructions;
            }
            var result = new List<CodeInstruction>(instructions.Count + 3);
            for (int i = 0; i < instructions.Count; i++)
            {
                result.Add(instructions[i]);
                if (i == store)
                {
                    result.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    result.Add(new CodeInstruction(OpCodes.Ldarg_1));
                    result.Add(new CodeInstruction(OpCodes.Call, dispatcher));
                }
            }
            return result;
        }

        private static IEnumerable<CodeInstruction> GestureTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = instructions.ToList();
            bool nativeHook;
            string reason;
            var result = BridgeInstructions(original, typeof(BasePlayer).GetField("currentGesture", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                typeof(ShortcutSkate).GetMethod("GestureDispatch", BindingFlags.Static | BindingFlags.NonPublic), out nativeHook, out reason);
            if (instance != null)
            { instance.gestureShape = reason == null; instance.gestureNativeHook = nativeHook; instance.gestureFailure = reason; }
            return result;
        }

        private static void GestureDispatch(BasePlayer player, GestureConfig gesture)
        {
            try
            {
                if (instance != null && instance.gestureAvailable && !instance.parkClosing)
                    instance.OnPlayerGestureStarted(player, gesture);
            }
            catch
            {
                try { if (instance != null) { instance.gestureAvailable = false; instance.gestureFailure = "The gesture handler failed; reload the plugin."; } }
                catch { }
            }
        }

        private void OnPlayerGestureStarted(BasePlayer player, GestureConfig gesture)
        {
            if (player == null || !player.IsAdmin || player.IsNpc || !player.IsConnected || !gestureAvailable || parkClosing) return;
            try { HandleParkGesture(player, gesture); }
            catch { gestureAvailable = false; gestureFailure = "The gesture handler failed; reload the plugin."; }
        }

        private void HandleParkGesture(BasePlayer player, GestureConfig gesture)
        {
            bool cancel = ReferenceEquals(gesture, parkCancelGesture);
            if (!cancel && !ReferenceEquals(gesture, parkNextGesture)) return;
            ParkSelection previous;
            bool existing = parkSelections.TryGetValue(player.userID, out previous) && ReferenceEquals(previous.Player, player);
            SkateParkSelectionState state = existing ? previous.State : new SkateParkSelectionState();
            SkateParkSelectionState selected;
            if (cancel)
            {
                if (SkateParkSelections.TryApply(state, SkateParkSelectionAction.Cancel, true, false,
                    clock.Elapsed.TotalSeconds, null, out selected))
                { parkSelectionEpoch = new object(); parkSelections.Remove(player.userID); player.ChatMessage("Park selection cancelled."); }
                return;
            }
            string failure;
            if (!Eligible(player)) { player.ChatMessage("Park placement is unavailable; be alive, awake, unmounted and on dry land."); return; }
            object epoch = parkSelectionEpoch = new object();
            if (!RefreshParkWorld(out failure)) { if (player.IsAdmin) player.ChatMessage(failure); return; }
            if (!player.IsAdmin) return;
            if (!ReferenceEquals(epoch, parkSelectionEpoch) || !ReferenceEquals(instance, this) || parkClosing ||
                !gestureAvailable || !Eligible(player))
            { player.ChatMessage("The selection request was superseded or the administrator became unavailable."); return; }
            PruneParkSelections();
            state = parkSelections.TryGetValue(player.userID, out previous) && ReferenceEquals(previous.Player, player) ?
                previous.State : new SkateParkSelectionState();
            if (parkData.Groups.Count >= MaximumParkGroups ||
                (!parkSelections.ContainsKey(player.userID) && parkSelections.Count >= MaximumParkSelections))
            { player.ChatMessage("The park group or pending-selection limit has been reached."); return; }
            if (!SkateParkSelections.TryApply(state, SkateParkSelectionAction.Next, true, true,
                clock.Elapsed.TotalSeconds, parkGestureOrder, out selected)) return;
            parkSelections[player.userID] = new ParkSelection { Player = player, State = selected };
            player.ChatMessage("Selected park " + SkateParkLayout.Name(selected.Kind) + ". Place a normal square foundation within 120 seconds. Use " +
                settings.ParkNextGesture + " for next or " + settings.ParkCancelGesture + " to cancel.");
        }

        private bool Permitted(BasePlayer player)
        { return settings != null && (settings.AllowEveryone || permission.UserHasPermission(player.UserIDString, UsePermission)); }

        private bool FlyPermitted(BasePlayer player)
        { return player.IsAdmin || permission.UserHasPermission(player.UserIDString, FlyPermission); }

        private static bool Eligible(BasePlayer player)
        {
            return player != null && SkateSessionBounds.Eligible(player.IsConnected, player.IsNpc,
                player.IsAlive(), player.IsSleeping(), player.IsWounded(), player.isMounted, player.IsSwimming()) &&
                player.GetParentEntity() == null;
        }

        private bool OptedOut(BasePlayer player)
        {
            BasePlayer previous;
            if (!optedOut.TryGetValue(player.userID, out previous)) return false;
            if (ReferenceEquals(previous, player)) return true;
            optedOut.Remove(player.userID);
            return false;
        }

        [ConsoleCommand("skate.toggle")]
        private void Toggle(ConsoleSystem.Arg arg) { Command(arg, 0); }
        [ConsoleCommand("skate.on")]
        private void On(ConsoleSystem.Arg arg) { Command(arg, 1); }
        [ConsoleCommand("skate.off")]
        private void Off(ConsoleSystem.Arg arg) { Command(arg, -1); }
        [ConsoleCommand("skate.status")]
        private void Status(ConsoleSystem.Arg arg)
        {
            int count = arg.Args == null ? 0 : arg.Args.Length;
            if (count > 1) { arg.ReplyWith("Usage: skate.status [steamID]."); return; }
            BasePlayer actor = arg.Player();
            bool privileged = (actor != null && actor.IsAdmin) || arg.IsAdmin ||
                (actor == null && arg.IsServerside && arg.Connection == null);
            ulong target;
            SkateStatusError error;
            if (!SkateStatusAccess.TrySelectTarget(actor == null ? 0 : actor.userID, actor != null,
                privileged, count == 0 ? null : arg.GetString(0), out target, out error))
            {
                arg.ReplyWith(error == SkateStatusError.Forbidden ? "You may only view your own skating status." :
                    error == SkateStatusError.TargetRequired ? "Usage: skate.status <steamID>." : "Use one nonzero numeric Steam ID.");
                return;
            }
            // Authorize the explicit ID before consulting the native player cache.
            BasePlayer player = actor != null && actor.userID == target ? actor : BasePlayer.FindByID(target);
            if (player == null || !player.IsConnected || player.IsNpc)
            { arg.ReplyWith("No connected player with that Steam ID."); return; }
            Session session;
            bool candidate = sessions.TryGetValue(player.userID, out session) && ReferenceEquals(player, session.Player);
            if (candidate && (!ready || !Eligible(player) || !Permitted(player) || OptedOut(player)))
            { Stop(player, PolicyReason(player)); candidate = false; }
            bool authorized = ready && Eligible(player) && Permitted(player) && !OptedOut(player);
            string state = authorized ? "Skating enabled; each complete movement batch must stay within the speed limit." :
                (OptedOut(player) ? "Skating off for this connection." : "Skating unavailable or not permitted.");
            SkateSessionHistory history;
            if (!diagnostics.TryGet(player.userID, player, out history))
                history = diagnostics.GetOrCreate(player.userID, player);
            if (candidate) session.History = history;
            ParkSelection selection;
            double now = clock.Elapsed.TotalSeconds;
            bool pending = parkSelections.TryGetValue(player.userID, out selection) && ReferenceEquals(selection.Player, player) &&
                SkateParkSelections.Active(selection.State, now);
            arg.ReplyWith(state + "\nSteam ID " + target.ToString(CultureInfo.InvariantCulture) +
                "; ready=" + ready + "; unavailable-reason=" + (ready ? SkateSessionReason.None : unavailableReason) +
                "; diagnostic-mode=" + (candidate ? (session.Manual ? "manual" : settings.AutomaticEligiblePlayers ? "automatic" : "disabled") : "inactive") +
                "; movement-policy=permitted-horizontal-cap; last-batch-allowed=" + (candidate && session.MovementAllowed) +
                "; fly-policy=trusted-vertical-cap; fly-authorized=" + (authorized && FlyPermitted(player)) +
                "; fly-vertical-limit=" + SkateStatusReport.Number(SkateMovementPolicy.MaximumFlyVerticalSpeed) + "m/s" +
                "; last-batch-vertical-path=" + SkateStatusReport.Number(candidate ? session.MovementVerticalPath : double.NaN) + "m" +
                "; movement-reason=" + (candidate ? session.MovementReason : SkateSessionReason.Inactive) +
                "; diagnostics-active=" + (candidate && session.Bounds.Active) +
                "; diagnostics-batch-allowed=" + (candidate && session.Allowed) +
                "; park-selection=" + (pending ? SkateParkLayout.Name(selection.Kind) : "none") +
                "; park-selection-remaining=" + (pending ? SkateStatusReport.Number(selection.Expires - now) : "0") + "s" +
                "; park-gestures=" + gestureAvailable + "; gesture-source=" + (gestureAvailable ? (gestureNativeHook ? "native" : "bridge") : "unavailable") +
                "; gesture-diagnostic=" + (gestureAvailable ? "none" : gestureFailure) +
                "; speed-limit=" + SkateStatusReport.Number(settings == null ? double.NaN : settings.MaximumHorizontalSpeed) +
                "m/s; diagnostic-air-limit=" + SkateStatusReport.Number(settings == null ? double.NaN : settings.MaximumAirtime) +
                "s; automatic-diagnostics=" + (settings != null && settings.AutomaticEligiblePlayers) +
                "; diagnostic-rearm-required=" + SkateStatusReport.Number(settings == null ? double.NaN : settings.AutomaticRearmSeconds) +
                "s; grounded-building=" + (settings != null && settings.GroundedBuilding) +
                "; grounded-ready=" + groundedReady + "; grounded-records=" + groundedMembers.Count.ToString(CultureInfo.InvariantCulture) +
                ".\n" + (privileged && gestureNameMissing ? "Native gestures: " +
                    (string.IsNullOrEmpty(gestureCatalogNames) ? "(no readable names)" : gestureCatalogNames) + "\n" : "") +
                "Session diagnostics only:\n" + SkateStatusReport.Format(history, clock.Elapsed.TotalSeconds));
        }

        private void Command(ConsoleSystem.Arg arg, int action)
        {
            BasePlayer player = arg.Player();
            if (player == null) { arg.ReplyWith("Use this command as a player."); return; }
            Session session;
            bool candidate = sessions.TryGetValue(player.userID, out session) && ReferenceEquals(player, session.Player);
            if (action < 0 || (action == 0 && candidate))
            {
                optedOut[player.userID] = player;
                Stop(player, SkateSessionReason.ManualOff);
                arg.ReplyWith("Skating off; automatic skating is disabled for this connection.");
                return;
            }
            if (!ready) { arg.ReplyWith("Skating is unavailable. Ask the server administrator."); return; }
            if (!Permitted(player)) { arg.ReplyWith("You do not have permission to skate."); return; }
            if (!Eligible(player))
            { arg.ReplyWith("Be alive, awake, unmounted and out of water to skate."); return; }
            optedOut.Remove(player.userID);
            if (candidate) { session.Manual = true; arg.ReplyWith("Skating enabled; movement stays within the speed limit."); return; }
            Stop(player, SkateSessionReason.ManualRestart);
            session = new Session { Player = player, Manual = true, HasEnd = true, LastEnd = player.transform.position,
                History = diagnostics.GetOrCreate(player.userID, player) };
            session.LastObserved = clock.Elapsed.TotalSeconds;
            bool diagnosticStarted = session.Bounds.Begin(session.LastObserved, Supported(player.transform.position));
            sessions[player.userID] = session;
            SkateSessionObservation activation = session.Bounds.LastObservation;
            activation.Reason = diagnosticStarted ? SkateSessionReason.ManualOn : SkateSessionReason.Unsupported;
            activation.MaximumSpeed = settings.MaximumHorizontalSpeed;
            activation.MaximumAirtime = settings.MaximumAirtime;
            activation.RearmSeconds = 0; activation.RequiredRearmSeconds = settings.AutomaticRearmSeconds;
            session.Observation = activation;
            if (session.History != null)
            { session.History.Sample(activation); if (diagnosticStarted) session.History.Activate(activation); }
            arg.ReplyWith("Skating enabled; movement stays within the speed limit.");
        }

        private SkateSessionReason PolicyReason(BasePlayer player)
        {
            if (!ready) return unavailableReason;
            if (!Eligible(player))
            {
                if (player == null || !player.IsConnected) return SkateSessionReason.Disconnected;
                if (player.IsNpc) return SkateSessionReason.Npc;
                if (!player.IsAlive()) return SkateSessionReason.Dead;
                if (player.IsSleeping()) return SkateSessionReason.Sleeping;
                if (player.IsWounded()) return SkateSessionReason.Wounded;
                if (player.isMounted) return SkateSessionReason.Mounted;
                if (player.IsSwimming()) return SkateSessionReason.Swimming;
                return SkateSessionReason.Parented;
            }
            if (!Permitted(player)) return SkateSessionReason.PermissionDenied;
            if (OptedOut(player)) return SkateSessionReason.OptedOut;
            return SkateSessionReason.None;
        }

        private SkateSessionObservation Snapshot(Session session, SkateSessionReason reason, double now)
        {
            SkateSessionObservation value = session.Observation;
            value.Reason = reason;
            value.ElapsedSeconds += now - value.Time;
            value.Time = now; value.GapSeconds = now - session.LastObserved;
            value.WasActive = session.Bounds.Active; value.AirSeconds = session.Bounds.AirSeconds;
            value.RearmSeconds = session.Rearm.Seconds;
            value.MaximumSpeed = settings == null ? double.NaN : settings.MaximumHorizontalSpeed;
            value.MaximumAirtime = settings == null ? double.NaN : settings.MaximumAirtime;
            value.RequiredRearmSeconds = settings == null ? double.NaN : settings.AutomaticRearmSeconds;
            return value;
        }

        private static SkateSessionObservation Context(SkateSessionObservation value, SkateSessionObservation batchValue)
        {
            value.Batch = batchValue.Batch; value.SegmentCount = batchValue.SegmentCount;
            value.BufferSize = batchValue.BufferSize; value.PositionGap = batchValue.PositionGap;
            value.RequiredRearmSeconds = batchValue.RequiredRearmSeconds;
            value.MaximumAirtime = batchValue.MaximumAirtime;
            return value;
        }

        private void Stop(BasePlayer player, SkateSessionReason reason)
        {
            if (ReferenceEquals(player, null)) return;
            Session session;
            if (!sessions.TryGetValue(player.userID, out session) || !ReferenceEquals(player, session.Player)) return;
            Disarm(session, Snapshot(session, reason, clock.Elapsed.TotalSeconds));
            sessions.Remove(player.userID);
        }

        private static void Disarm(Session session, SkateSessionObservation observation)
        {
            double said = instance == null ? double.NaN : instance.clock.Elapsed.TotalSeconds;
            if (observation.WasActive && instance != null && said - session.SaidAt > 2)
            {
                session.SaidAt = said;
                instance.Puts("Skate diagnostics reset for " + (session.Player == null ? "?" : session.Player.displayName) + ": " + observation.Reason);
            }
            session.Observation = observation;
            if (session.History != null)
            {
                session.History.Disarm(observation);
                session.History.WarmupReset(observation);
            }
            session.Bounds.End();
            session.Rearm.Reset();
            session.Allowed = false;
            session.HasEnd = false;
        }

        private void Sweep()
        {
            foreach (Session session in sessions.Values.ToArray())
            {
                if (!ready || !Eligible(session.Player) || !Permitted(session.Player) || OptedOut(session.Player))
                    Stop(session.Player, PolicyReason(session.Player));
                else if (session.Bounds.Active && clock.Elapsed.TotalSeconds - session.LastObserved > SkateSessionBounds.MaximumBatchSeconds)
                    Disarm(session, Snapshot(session, batch == long.MaxValue ? SkateSessionReason.BatchExhausted :
                        SkateSessionReason.BatchGap, clock.Elapsed.TotalSeconds));
            }
            foreach (var pair in optedOut.ToArray())
                if (pair.Value == null || !pair.Value.IsConnected) optedOut.Remove(pair.Key);
            diagnostics.RemoveInactive(ConnectedIdentity);
        }

        private void OnPlayerDeath(BasePlayer player, HitInfo info) { Stop(player, SkateSessionReason.Dead); }
        private void OnPlayerSleep(BasePlayer player) { Stop(player, SkateSessionReason.Sleeping); }
        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            Stop(player, SkateSessionReason.Disconnected);
            BasePlayer previous;
            if (player != null)
            {
                ParkSelection selection;
                if (parkSelections.TryGetValue(player.userID, out selection) && ReferenceEquals(selection.Player, player))
                    parkSelections.Remove(player.userID);
                diagnostics.Remove(player.userID, player);
                if (optedOut.TryGetValue(player.userID, out previous) && ReferenceEquals(player, previous))
                    optedOut.Remove(player.userID);
            }
        }
        private void OnEntityMounted(BaseMountable mountable, BasePlayer player) { Stop(player, SkateSessionReason.Mounted); }
        private void OnPlayerWound(BasePlayer player, HitInfo info) { Stop(player, SkateSessionReason.Wounded); }

        private void Unload()
        {
            InitializeGroundedBuildings();
            parkClosing = true;
            groundedReady = false;
            groundedPending.Clear();
            groundedMembers.Clear();
            groundedEntities.Clear();
            gestureAvailable = false;
            gestureNameMissing = false; gestureCatalogNames = null;
            try { if (gestureHarmony != null) gestureHarmony.UnpatchAll(GesturePatchId); }
            catch (Exception error) { PrintError("The park gesture patch could not be removed: " + error.Message); }
            parkReady = false;
            parkSelections.Clear();
            parkDemolitionPending.Clear();
            CleanupParkRollback();
            if (parkRollback.Count != 0) PrintError("Unload could not remove " + parkRollback.Count + " unsaved park rollback parts; cleanup is incomplete.");
            if (!parkDataBroken && parkData != null && SameParkWorld(parkData)) SaveParksSafely();
            ready = false;
            foreach (Session session in sessions.Values.ToArray()) Stop(session.Player, SkateSessionReason.Unload);
            optedOut.Clear();
            diagnostics.Clear();
            ClearPractice();
            if (practice.Count != 0) PrintError("Unload could not remove " + practice.Count + " practice parts; an entity kill hook or server error prevented cleanup.");
            if (ReferenceEquals(instance, this)) instance = null;
        }

        [ConsoleCommand("skate.practice.spawn")]
        private void SpawnPractice(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) { arg.ReplyWith("Use this command as an in-game administrator."); return; }
            if (!ready || !Eligible(player)) { arg.ReplyWith("Practice is unavailable; stand alive on dry ground with skating ready."); return; }
            if (practice.Count != 0) { arg.ReplyWith("Clear the existing practice area first with skate.practice.clear."); return; }
            try
            {
                string path = SkatePracticeLayout.FloorPrefab;
                var manifest = GameManifest.Current;
                var prefab = GameManager.server.FindPrefab(path);
                var construction = PrefabAttribute.server.Find<Construction>(StringPool.Get(path));
                var stone = construction == null ? null : construction.GetGrade(BuildingGrade.Enum.Stone, 0);
                if (manifest == null || manifest.entities == null ||
                    !manifest.entities.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) ||
                    prefab == null || prefab.GetComponent<BuildingBlock>() == null || construction == null ||
                    stone == null || stone.gradeBase == null || stone.gradeBase.type != BuildingGrade.Enum.Stone)
                { arg.ReplyWith("Practice floor prefab or stone construction data is unavailable in this server build."); return; }
                Vector3 forward = player.eyes.BodyForward();
                forward.y = 0;
                if (!Coordinate(forward) || forward.sqrMagnitude < 0.0001f)
                { arg.ReplyWith("Look horizontally toward an open practice area."); return; }
                forward.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                Vector3 origin = player.transform.position + forward * 12;
                RaycastHit ground;
                int terrainMask = LayerMask.GetMask("Terrain", "World");
                if (!Physics.Raycast(origin + Vector3.up * 3, Vector3.down, out ground, 6, terrainMask, QueryTriggerInteraction.Ignore))
                { arg.ReplyWith("Practice needs open, flat dry ground about twelve metres ahead."); return; }
                origin.y = ground.point.y;
                for (int x = -4; x <= 4; x++)
                    for (int z = -3; z <= 5; z++)
                    {
                        Vector3 point = origin + right * (x * 1.5f) + forward * (z * 1.5f);
                        if (!Physics.Raycast(point + Vector3.up * 3, Vector3.down, out ground, 6, terrainMask, QueryTriggerInteraction.Ignore) ||
                            ground.normal.y < 0.98f || Math.Abs(ground.point.y - origin.y) > 0.1f ||
                            WaterLevel.Test(ground.point + Vector3.up * 0.1f, true, false))
                        { arg.ReplyWith("Practice needs a clear, flat dry area about fourteen metres wide."); return; }
                    }
                Quaternion yaw = Quaternion.LookRotation(forward, Vector3.up);
                if (Physics.CheckBox(origin + forward * 1.5f + Vector3.up * 1.5f,
                    new Vector3(7, 1.4f, 7.5f), yaw,
                    LayerMask.GetMask("Construction", "Deployed", "Player (Server)"), QueryTriggerInteraction.Ignore))
                { arg.ReplyWith("Practice space is occupied by a player or existing building."); return; }
                for (int index = 0; index < SkatePracticeLayout.PartCount; index++)
                {
                    SkatePracticePart part;
                    if (!SkatePracticeLayout.TryPart(index, out part)) throw new InvalidDataException("Invalid practice layout.");
                    Vector3 position = origin + right * part.Right + forward * part.Forward + Vector3.up * part.Height;
                    BaseEntity entity = GameManager.server.CreateEntity(path, position, yaw * Quaternion.Euler(part.Pitch, 0, 0), true);
                    if (entity == null) throw new InvalidDataException("Practice floor creation failed.");
                    practice.Add(entity);
                    BuildingBlock block = entity as BuildingBlock;
                    if (block == null) throw new InvalidDataException("Practice floor is not a building part.");
                    block.enableSaving = false;
                    block.CullBushes = false;
                    block.grounded = true;
                    block.OwnerID = player.userID;
                    block.Spawn();
                    if (block.IsDestroyed || block.blockDefinition == null) throw new InvalidDataException("Practice floor initialization failed.");
                    block.SetGrade(BuildingGrade.Enum.Stone);
                    block.SetHealthToMax();
                    block.SendNetworkUpdateImmediate();
                }
                arg.ReplyWith("Practice area spawned: two ramps, a bank, 0.4 m and 0.8 m ledges, and a long low ledge. Clear with skate.practice.clear.");
            }
            catch (Exception error)
            {
                ClearPractice();
                PrintError("Practice creation failed. " + error.Message);
                arg.ReplyWith(practice.Count == 0 ? "Practice could not be created; created parts were removed." :
                    "Practice creation failed and cleanup is incomplete; use skate.practice.clear.");
            }
        }

        [ConsoleCommand("skate.practice.clear")]
        private void PracticeClear(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) { arg.ReplyWith("Use this command as an in-game administrator."); return; }
            ClearPractice();
            arg.ReplyWith(practice.Count == 0 ? "Practice area cleared." : "Practice cleanup is incomplete; check the server log and retry.");
        }

        private void ClearPractice()
        {
            for (int i = practice.Count - 1; i >= 0; i--)
            {
                BaseEntity entity = practice[i];
                try
                {
                    if (entity != null && !entity.IsDestroyed) entity.Kill();
                    if (entity != null && !entity.IsDestroyed)
                    { PrintError("Practice part removal was refused by the server; the part remains tracked."); continue; }
                    practice.RemoveAt(i);
                }
                catch (Exception error) { PrintError("Practice part cleanup failed. " + error.Message); }
            }
        }

        private void ReadGroundedData()
        {
            try
            {
                if (!File.Exists(GroundedFilename)) return;
                if (new FileInfo(GroundedFilename).Length > MaximumParkDataLength)
                    throw new InvalidDataException("Grounded building data exceeds one MiB.");
                groundedData = ParseGroundedData(File.ReadAllText(GroundedFilename));
            }
            catch (Exception error)
            {
                groundedDataBroken = true;
                PrintError("Grounded building restoration is unavailable; the original data file is preserved. " + error.Message);
            }
        }

        private static GroundedData NewGroundedData()
        {
            ParkData current = NewParkData();
            return new GroundedData { Version = 1, WipeId = current.WipeId, SaveCreatedTicks = current.SaveCreatedTicks,
                Seed = current.Seed, Size = current.Size, Checksum = current.Checksum, Members = new List<GroundedMember>() };
        }

        private static ParkData GroundedIdentity(GroundedData data)
        {
            return data == null ? null : new ParkData { WipeId = data.WipeId, SaveCreatedTicks = data.SaveCreatedTicks,
                Seed = data.Seed, Size = data.Size, Checksum = data.Checksum };
        }

        private static bool SameGroundedGeneration(GroundedData data)
        {
            return data != null && !string.IsNullOrEmpty(SaveRestore.WipeId) && data.WipeId == SaveRestore.WipeId &&
                data.SaveCreatedTicks == ParkSaveTicks() && data.Seed == World.Seed && data.Size == World.Size;
        }

        private static Vector3 GroundedPosition(GroundedMember member)
        { return new Vector3((float)member.X, (float)member.Y, (float)member.Z); }

        private static Quaternion GroundedRotation(GroundedMember member)
        { return Quaternion.Euler((float)member.RotX, (float)member.RotY, (float)member.RotZ); }

        private static bool GroundedPose(Vector3 position, Quaternion rotation, GroundedMember member)
        {
            return Coordinate(position) && (position - GroundedPosition(member)).sqrMagnitude <= 0.000625f &&
                Quaternion.Angle(rotation, GroundedRotation(member)) <= 0.1f;
        }

        private static Vector3 GroundedEuler(Quaternion rotation)
        {
            Vector3 value = rotation.eulerAngles;
            return new Vector3(GroundedAngle(value.x), GroundedAngle(value.y), GroundedAngle(value.z));
        }

        internal static float GroundedAngle(float value)
        {
            float angle = value % 360f;
            if (angle < 0) angle += 360f;
            return angle >= 360f ? 0 : angle;
        }

        private static bool GroundedIdentityMatches(StabilityEntity entity, GroundedMember member)
        {
            if (entity == null || entity.IsDestroyed || !entity.enableSaving || ParkId(entity) != member.Id ||
                entity.prefabID != member.Prefab || entity.OwnerID != member.Owner) return false;
            BaseEntity parent = entity.GetParentEntity();
            if (entity.parentEntity.uid.Value != member.ParentId || entity.parentBone != member.ParentBone ||
                ParkId(parent) != member.ParentId || (parent != null &&
                (parent.IsDestroyed || !parent.enableSaving || parent.prefabID != member.ParentPrefab || parent.OwnerID != member.ParentOwner))) return false;
            return true;
        }

        private static bool GroundedMatches(StabilityEntity entity, GroundedMember member)
        {
            return GroundedIdentityMatches(entity, member) &&
                GroundedPose(entity.transform.localPosition, entity.transform.localRotation, member);
        }

        private static GroundedMember CaptureGroundedMember(StabilityEntity entity)
        {
            BaseEntity parent = entity.GetParentEntity();
            Vector3 position = entity.transform.localPosition;
            Vector3 rotation = GroundedEuler(entity.transform.localRotation);
            return new GroundedMember { Id = ParkId(entity), Owner = entity.OwnerID, Prefab = entity.prefabID,
                ParentId = entity.parentEntity.uid.Value, ParentBone = entity.parentBone, ParentPrefab = parent == null ? 0 : parent.prefabID,
                ParentOwner = parent == null ? 0 : parent.OwnerID,
                X = position.x, Y = position.y, Z = position.z, RotX = rotation.x, RotY = rotation.y, RotZ = rotation.z };
        }

        private bool VerifiedGroundedMember(StabilityEntity entity)
        {
            GroundedMember member;
            StabilityEntity expected;
            ulong id = ParkId(entity);
            return settings != null && settings.GroundedBuilding && !groundedDataBroken && SameGroundedGeneration(groundedData) &&
                id != 0 && groundedMembers.TryGetValue(id, out member) && groundedEntities.TryGetValue(id, out expected) &&
                ReferenceEquals(entity, expected) && GroundedMatches(entity, member);
        }

        private void SaveGroundedData()
        {
            GroundedData current = NewGroundedData();
            if (groundedDataBroken || !SameGroundedGeneration(groundedData, current) || groundedData.Checksum != current.Checksum)
                throw new InvalidDataException("Grounded building metadata has no matching native save/world identity.");
            string json;
            try { json = JsonConvert.SerializeObject(groundedData, Formatting.Indented); }
            catch (Exception) { groundedStorageFailed = true; throw; }
            if (json.Length > MaximumParkDataLength || Encoding.UTF8.GetByteCount(json) > MaximumParkDataLength)
                throw new InvalidDataException("Grounded building metadata exceeds one MiB.");
            try { ParseGroundedData(json); }
            catch (Exception) { groundedStorageFailed = true; throw; }
            string temporary = GroundedFilename + ".writing-" + Guid.NewGuid().ToString("N");
            bool changedWorld = false;
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (!SameGroundedGeneration(groundedData) || groundedData.Checksum != (World.Checksum ?? ""))
                {
                    changedWorld = true;
                    throw new InvalidDataException("Save/world identity changed before grounded building metadata replacement.");
                }
                if (File.Exists(GroundedFilename)) File.Replace(temporary, GroundedFilename, null);
                else File.Move(temporary, GroundedFilename);
                if (File.ReadAllText(GroundedFilename) != json)
                    throw new IOException("Grounded building metadata readback differed from its written bytes.");
            }
            catch (Exception) { if (!changedWorld) groundedStorageFailed = true; groundedReady = false; throw; }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private void InitializeGroundedBuildings()
        {
            if (settings == null || !settings.GroundedBuilding || groundedDataBroken || groundedStorageFailed ||
                groundedRefreshing || groundedBusy || parkClosing) return;
            groundedReady = false;
            groundedRefreshing = true;
            try
            {
                GroundedData current = NewGroundedData();
                List<string> problems = ParkIdentityChanges(null, GroundedIdentity(current));
                if (problems.Count != 0) throw new InvalidDataException(string.Join("\n", problems));
                if (!SameGroundedGeneration(groundedData, current))
                {
                    if (groundedData != null) PrintWarning("Old grounded building records belong to another save/world generation; no entity IDs were adopted.");
                    groundedData = current;
                }
                foreach (GroundedMember member in groundedData.Members)
                {
                    StabilityEntity live;
                    if (!groundedEntities.TryGetValue(member.Id, out live) ||
                        !ReferenceEquals(live, BaseNetworkable.serverEntities.Find(new NetworkableId(member.Id))) ||
                        !GroundedIdentityMatches(live, member)) continue;
                    GroundedMember pose = CaptureGroundedMember(live);
                    if (!ValidGroundedMember(pose)) continue;
                    member.X = pose.X; member.Y = pose.Y; member.Z = pose.Z;
                    member.RotX = pose.RotX; member.RotY = pose.RotY; member.RotZ = pose.RotZ;
                }
                groundedMembers.Clear();
                groundedEntities.Clear();
                foreach (GroundedMember member in groundedData.Members.ToArray())
                {
                    StabilityEntity entity = BaseNetworkable.serverEntities.Find(new NetworkableId(member.Id)) as StabilityEntity;
                    if (!GroundedMatches(entity, member)) { groundedData.Members.Remove(member); continue; }
                    groundedMembers.Add(member.Id, member);
                    groundedEntities.Add(member.Id, entity);
                    entity.grounded = true;
                    entity.InitializeSupports();
                    entity.UpdateSurroundingEntities();
                }
                foreach (GroundedMember member in groundedData.Members.ToArray())
                {
                    StabilityEntity entity;
                    if (!groundedEntities.TryGetValue(member.Id, out entity) ||
                        !ReferenceEquals(entity, BaseNetworkable.serverEntities.Find(new NetworkableId(member.Id))) || !GroundedMatches(entity, member))
                    { groundedData.Members.Remove(member); groundedMembers.Remove(member.Id); groundedEntities.Remove(member.Id); }
                }
                current = NewGroundedData();
                if (!SameGroundedGeneration(groundedData, current))
                    throw new InvalidDataException("Save/world generation changed during grounded building verification.");
                groundedData.Checksum = current.Checksum;
                SaveGroundedData();
                groundedReady = true;
            }
            catch (Exception error)
            { PrintError("New grounded building support is unavailable; verified existing support is retained. " + error.Message); }
            finally { groundedRefreshing = false; }
        }

        private bool RefreshGroundedWorld()
        {
            if (groundedDataBroken || groundedStorageFailed || groundedRefreshing || groundedBusy || parkClosing) return false;
            if (!groundedReady || !SameGroundedGeneration(groundedData) || groundedData.Checksum != (World.Checksum ?? "")) InitializeGroundedBuildings();
            return groundedReady && !groundedDataBroken && !groundedStorageFailed && SameGroundedGeneration(groundedData) &&
                groundedData.Checksum == (World.Checksum ?? "");
        }

        private void GroundBuiltEntity(BasePlayer player, StabilityEntity entity)
        {
            if (settings == null || !settings.GroundedBuilding || !Eligible(player) || !Permitted(player) ||
                entity == null || entity.IsDestroyed || !entity.enableSaving || parkClosing) return;
            ulong id = ParkId(entity), owner = entity.OwnerID;
            uint prefab = entity.prefabID;
            if (id == 0 || owner == 0 || prefab == 0 || groundedPending.ContainsKey(id)) return;
            if (groundedPending.Count >= MaximumGroundedMembers)
            { PrintWarning("Grounded building has reached its pending-placement capacity."); return; }
            GroundedData generation = NewGroundedData();
            groundedPending.Add(id, entity);
            // Parenting follows the placement callback; fingerprint the final local pose on the next tick.
            NextTick(() =>
            {
                try
                {
                    StabilityEntity pending;
                    if (!ReferenceEquals(instance, this) || parkClosing || !groundedPending.TryGetValue(id, out pending) ||
                        !ReferenceEquals(entity, pending) || settings == null || !settings.GroundedBuilding ||
                        !Eligible(player) || !Permitted(player) || entity == null || entity.IsDestroyed ||
                        !ReferenceEquals(entity, BaseNetworkable.serverEntities.Find(new NetworkableId(id))) ||
                        ParkId(entity) != id || entity.prefabID != prefab || entity.OwnerID != owner ||
                        !SameGroundedGeneration(generation, NewGroundedData())) return;
                    RegisterGroundedBuilding(player, entity);
                }
                catch (Exception error) { PrintError("Grounded building registration failed; normal placement remains. " + error.Message); }
                finally
                {
                    StabilityEntity pending;
                    if (groundedPending.TryGetValue(id, out pending) && ReferenceEquals(entity, pending)) groundedPending.Remove(id);
                }
            });
        }

        private void RegisterGroundedBuilding(BasePlayer player, StabilityEntity entity)
        {
            if (settings == null || !settings.GroundedBuilding || !Eligible(player) || !Permitted(player) ||
                entity == null || entity.IsDestroyed || !entity.enableSaving) return;
            if (!RefreshGroundedWorld())
            { PrintWarning("Grounded building was not applied: its metadata or native save/world identity is unavailable."); return; }
            groundedBusy = true;
            GroundedMember member = null;
            bool added = false, persisted = false, applied = false;
            try
            {
                if (!Eligible(player) || !Permitted(player) || parkClosing || settings == null || !settings.GroundedBuilding) return;
                member = CaptureGroundedMember(entity);
                if (!ValidGroundedMember(member) || !GroundedMatches(entity, member))
                    throw new InvalidDataException("Placed stability entity has no verified saved identity/owner/parent/pose.");
                if (groundedMembers.ContainsKey(member.Id))
                {
                    if (VerifiedGroundedMember(entity)) entity.grounded = true;
                    return;
                }
                if (groundedData.Members.Count >= MaximumGroundedMembers)
                    throw new InvalidDataException("Grounded building metadata reached its 4096-member capacity.");
                groundedData.Members.Add(member);
                groundedMembers.Add(member.Id, member);
                groundedEntities.Add(member.Id, entity);
                added = true;
                SaveGroundedData();
                persisted = true;
                // Persist first: a support flag that is absent from the save must not outlive its trusted record.
                if (!Eligible(player) || !Permitted(player) || parkClosing || !SameGroundedGeneration(groundedData) ||
                    !ReferenceEquals(entity, BaseNetworkable.serverEntities.Find(new NetworkableId(member.Id))) || !GroundedMatches(entity, member))
                    throw new InvalidDataException("Builder or placed entity identity changed before grounding.");
                entity.grounded = true;
                applied = true;
                entity.InitializeSupports();
                entity.UpdateSurroundingEntities();
            }
            catch (Exception error)
            {
                if (added && (!applied || entity == null || entity.IsDestroyed || !GroundedMatches(entity, member)))
                {
                    groundedData.Members.Remove(member);
                    groundedMembers.Remove(member.Id);
                    groundedEntities.Remove(member.Id);
                    if (persisted && !groundedStorageFailed)
                        try { SaveGroundedData(); } catch (Exception cleanup) { PrintError("Grounded record cleanup failed. " + cleanup.Message); }
                }
                PrintError("Grounded building processing did not finish. " + error.Message);
            }
            finally { groundedBusy = false; }
        }

        private void ReadParkData()
        {
            try
            {
                if (!File.Exists(ParkFilename)) return;
                if (new FileInfo(ParkFilename).Length > MaximumParkDataLength)
                    throw new InvalidDataException("Park data exceeds one MiB.");
                parkData = ParseParkData(File.ReadAllText(ParkFilename));
            }
            catch (Exception error)
            {
                parkDataBroken = true;
                PrintError("Park grouping is unavailable; the original data file is preserved. " + error.Message);
            }
        }

        private static bool SameParkGeneration(ParkData data)
        {
            return data != null && !string.IsNullOrEmpty(SaveRestore.WipeId) &&
                data.WipeId == SaveRestore.WipeId && data.SaveCreatedTicks == ParkSaveTicks() &&
                data.Seed == World.Seed && data.Size == World.Size;
        }

        private static bool SameParkWorld(ParkData data)
        { return SameParkGeneration(data) && data.Checksum == (World.Checksum ?? ""); }

        internal static bool SameParkGeneration(ParkData data, ParkData current)
        {
            return data != null && current != null && !string.IsNullOrEmpty(current.WipeId) &&
                current.WipeId.Length <= 128 && current.SaveCreatedTicks > 0 && current.SaveCreatedTicks <= DateTime.MaxValue.Ticks &&
                current.SaveCreatedTicks % TimeSpan.TicksPerSecond == 0 && data.WipeId == current.WipeId &&
                data.SaveCreatedTicks == current.SaveCreatedTicks && data.Seed == current.Seed && data.Size == current.Size;
        }

        internal static List<string> ParkIdentityChanges(ParkData data, ParkData current)
        {
            var failures = new List<string>();
            if (current == null) { failures.Add("Current native save/world identity is unavailable."); return failures; }
            if (string.IsNullOrEmpty(current.WipeId)) failures.Add("SaveRestore.WipeId is unavailable.");
            else if (current.WipeId.Length > 128) failures.Add("SaveRestore.WipeId exceeds 128 characters.");
            if (current.SaveCreatedTicks <= 0) failures.Add("SaveRestore.SaveCreatedTime is unavailable: ticks=" +
                current.SaveCreatedTicks.ToString(CultureInfo.InvariantCulture) + ".");
            else if (current.SaveCreatedTicks > DateTime.MaxValue.Ticks || current.SaveCreatedTicks % TimeSpan.TicksPerSecond != 0)
                failures.Add("SaveRestore.SaveCreatedTime is not a valid whole-second identity: ticks=" +
                    current.SaveCreatedTicks.ToString(CultureInfo.InvariantCulture) + ".");
            if (current.Checksum == null) failures.Add("World.Checksum snapshot is unavailable.");
            else if (current.Checksum.Length > 256) failures.Add("World.Checksum exceeds 256 characters.");
            if (data == null) return failures;
            if (data.WipeId != current.WipeId) failures.Add("SaveRestore.WipeId changed: stored=" + data.WipeId + "; current=" + current.WipeId + ".");
            if (data.SaveCreatedTicks != current.SaveCreatedTicks) failures.Add("SaveRestore.SaveCreatedTime changed: stored=" +
                data.SaveCreatedTicks.ToString(CultureInfo.InvariantCulture) + "; current=" + current.SaveCreatedTicks.ToString(CultureInfo.InvariantCulture) + ".");
            if (data.Seed != current.Seed) failures.Add("World.Seed changed: stored=" + data.Seed.ToString(CultureInfo.InvariantCulture) +
                "; current=" + current.Seed.ToString(CultureInfo.InvariantCulture) + ".");
            if (data.Size != current.Size) failures.Add("World.Size changed: stored=" + data.Size.ToString(CultureInfo.InvariantCulture) +
                "; current=" + current.Size.ToString(CultureInfo.InvariantCulture) + ".");
            if (data.Checksum != current.Checksum) failures.Add("World.Checksum changed: stored=" + data.Checksum +
                "; current=" + current.Checksum + ".");
            return failures;
        }

        private static List<string> ParkWorldProblems(ParkData data, bool compare)
        { return ParkIdentityChanges(compare ? data : null, NewParkData()); }

        private bool RefreshParkWorld(out string failure)
        {
            failure = null;
            if (parkClosing || parkDataBroken)
            { failure = "Park metadata is unavailable; the original file is preserved. Check the server log."; return false; }
            if (parkStorageFailed)
            { failure = "Park metadata storage failed; reload the plugin after fixing the storage error: " + parkStorageError; return false; }
            if (parkRefreshing)
            { failure = "Park save/world verification is already in progress; try again after it finishes."; return false; }
            List<string> problems = ParkWorldProblems(null, false);
            if (problems.Count != 0) { failure = string.Join("\n", problems); return false; }
            if (parkBusy)
            {
                if (parkReady && SameParkWorld(parkData)) return true;
                failure = "Park placement is busy; its save/world identity cannot be refreshed during a transaction.";
                return false;
            }
            if (!parkReady || !SameParkWorld(parkData)) InitializeParks();
            if (parkReady && SameParkWorld(parkData)) return true;
            failure = parkStorageFailed ? "Park metadata storage failed; reload the plugin after fixing the storage error: " + parkStorageError :
                "Park metadata could not be refreshed for the current save/world. Check the server log.";
            return false;
        }

        private static ParkData NewParkData()
        {
            return new ParkData { Version = 1, WipeId = SaveRestore.WipeId,
                SaveCreatedTicks = ParkSaveTicks(), Seed = World.Seed, Size = World.Size,
                Checksum = World.Checksum ?? "", Groups = new List<ParkGroup>() };
        }

        private static long ParkSaveTicks()
        {
            // Native modern save headers persist integer epoch seconds, dropping fresh-wipe fractions.
            long ticks = SaveRestore.SaveCreatedTime.Ticks;
            return ticks - ticks % TimeSpan.TicksPerSecond;
        }

        private static ulong ParkId(BaseNetworkable entity)
        { return entity == null || entity.net == null ? 0 : entity.net.ID.Value; }

        private static BuildingBlock FindParkBlock(ulong id)
        { return BaseNetworkable.serverEntities.Find(new NetworkableId(id)) as BuildingBlock; }

        private static Quaternion ParkRotation(ParkMember member)
        { return Quaternion.Euler(0, (float)member.Yaw, 0) * Quaternion.Euler((float)member.Pitch, 0, 0); }

        private static Vector3 ParkPosition(ParkMember member)
        { return new Vector3((float)member.X, (float)member.Y, (float)member.Z); }

        private static bool ParkPose(Vector3 position, Quaternion rotation, ParkMember member)
        {
            return Coordinate(position) && (position - ParkPosition(member)).sqrMagnitude <= 0.000625f &&
                Quaternion.Angle(rotation, ParkRotation(member)) <= 0.1f;
        }

        private static bool ParkMatches(BuildingBlock block, ParkGroup group, ParkMember member)
        {
            SkateParkKind kind;
            SkateParkPart part;
            return block != null && !block.IsDestroyed && ParkId(block) == member.Id && block.prefabID == member.Prefab &&
                SkateParkLayout.TryKind(group.Kind, out kind) && SkateParkLayout.TryPart(kind, member.Index, out part) &&
                member.Prefab == StringPool.Get(part.Prefab) && block.PrefabName == part.Prefab && block.OwnerID == group.Owner &&
                block.GetParentEntity() == null && ParkPose(block.transform.position, block.transform.rotation, member);
        }

        private void RestoreGroundedBuildings(Dictionary<BaseEntity, ProtoBuf.Entity> entities)
        {
            if (settings == null || !settings.GroundedBuilding || groundedDataBroken || !SameGroundedGeneration(groundedData) || entities == null) return;
            try
            {
                var saved = new Dictionary<ulong, KeyValuePair<BaseEntity, ProtoBuf.Entity>>();
                var duplicates = new HashSet<ulong>();
                foreach (var pair in entities)
                {
                    ulong id = ParkId(pair.Key);
                    if (id == 0 || duplicates.Contains(id)) continue;
                    if (saved.ContainsKey(id)) { saved.Remove(id); duplicates.Add(id); }
                    else saved.Add(id, pair);
                }
                foreach (GroundedMember member in groundedData.Members)
                {
                    KeyValuePair<BaseEntity, ProtoBuf.Entity> pair;
                    if (!saved.TryGetValue(member.Id, out pair)) continue;
                    StabilityEntity entity = pair.Key as StabilityEntity;
                    ProtoBuf.Entity proto = pair.Value;
                    if (entity == null || entity.prefabID != member.Prefab || proto == null || proto.baseNetworkable == null ||
                        proto.baseEntity == null || proto.ownerInfo == null || proto.baseNetworkable.uid.Value != member.Id ||
                        proto.baseNetworkable.prefabID != member.Prefab || proto.ownerInfo.steamid != member.Owner ||
                        (proto.parent == null ? 0 : proto.parent.uid.Value) != member.ParentId ||
                        (proto.parent == null ? 0 : proto.parent.bone) != member.ParentBone ||
                        !GroundedPose(proto.baseEntity.pos, Quaternion.Euler(proto.baseEntity.rot), member)) continue;
                    if (member.ParentId != 0)
                    {
                        KeyValuePair<BaseEntity, ProtoBuf.Entity> parent;
                        if (!saved.TryGetValue(member.ParentId, out parent) || parent.Key == null || parent.Key.prefabID != member.ParentPrefab ||
                            parent.Value == null || parent.Value.baseNetworkable == null ||
                            parent.Value.baseNetworkable.uid.Value != member.ParentId || parent.Value.baseNetworkable.prefabID != member.ParentPrefab ||
                            (parent.Value.ownerInfo == null ? 0 : parent.Value.ownerInfo.steamid) != member.ParentOwner) continue;
                    }
                    // Live owner/parent/local transform arrive later; only the incoming saved fingerprint is authoritative here.
                    entity.grounded = true;
                }
            }
            catch (Exception error) { PrintError("Early grounded building restoration failed. " + error.Message); }
        }

        // Void is intentional: a bool from this hook would abort the game's entire normal save-load loop.
        // OwnerID has not loaded yet; validate the saved protobuf owner, ID, prefab and pose instead.
        private void OnSaveLoad(Dictionary<BaseEntity, ProtoBuf.Entity> entities)
        {
            RestoreGroundedBuildings(entities);
            if (parkDataBroken || parkData == null || !SameParkGeneration(parkData) || entities == null) return;
            try
            {
                var saved = new Dictionary<ulong, KeyValuePair<BaseEntity, ProtoBuf.Entity>>();
                foreach (var pair in entities)
                    if (pair.Key != null && ParkId(pair.Key) != 0) saved[ParkId(pair.Key)] = pair;
                foreach (ParkGroup group in parkData.Groups)
                    foreach (ParkMember member in group.Members)
                    {
                        SkateParkKind kind;
                        SkateParkPart part;
                        if (!SkateParkLayout.TryKind(group.Kind, out kind) ||
                            !SkateParkLayout.TryPart(kind, member.Index, out part) || member.Prefab != StringPool.Get(part.Prefab)) continue;
                        KeyValuePair<BaseEntity, ProtoBuf.Entity> pair;
                        if (!saved.TryGetValue(member.Id, out pair)) continue;
                        BuildingBlock block = pair.Key as BuildingBlock;
                        ProtoBuf.Entity proto = pair.Value;
                        if (block == null || block.PrefabName != part.Prefab || block.prefabID != member.Prefab ||
                            proto == null || proto.baseNetworkable == null || proto.baseEntity == null || proto.ownerInfo == null ||
                            proto.baseNetworkable.uid.Value != member.Id || proto.baseNetworkable.prefabID != member.Prefab ||
                            proto.ownerInfo.steamid != group.Owner ||
                            !ParkPose(proto.baseEntity.pos, Quaternion.Euler(proto.baseEntity.rot), member) ||
                            !ParkPose(block.transform.position, block.transform.rotation, member)) continue;
                        block.grounded = true;
                    }
            }
            catch (Exception error) { PrintError("Early park support restoration failed. " + error.Message); }
        }

        private void InitializeParks()
        {
            parkReady = false;
            if (parkDataBroken || parkClosing || parkStorageFailed || parkRefreshing) return;
            parkRefreshing = true;
            try
            {
                ParkData current = NewParkData();
                List<string> problems = ParkIdentityChanges(null, current);
                if (problems.Count != 0) throw new InvalidDataException(string.Join("\n", problems));
                if (parkData == null || !SameParkGeneration(parkData, current))
                {
                    // Never adopt a reused network ID from a different save/world or recreate old pieces.
                    if (parkData != null)
                    {
                        foreach (string problem in ParkIdentityChanges(parkData, current)) PrintWarning(problem);
                        PrintWarning("Old park grouping belongs to another save/world generation; no old entity IDs were adopted.");
                    }
                    parkData = current;
                }
                parkMembers.Clear();
                foreach (ParkGroup group in parkData.Groups.ToArray())
                {
                    foreach (ParkMember member in group.Members.ToArray())
                    {
                        BuildingBlock block = FindParkBlock(member.Id);
                        if (!ParkMatches(block, group, member)) { group.Members.Remove(member); continue; }
                        block.grounded = true;
                        block.InitializeSupports();
                        block.UpdateSurroundingEntities();
                        parkMembers.Add(member.Id, group);
                    }
                    if (group.Members.Count == 0) parkData.Groups.Remove(group);
                }
                foreach (ParkGroup group in parkData.Groups.ToArray())
                {
                    foreach (ParkMember member in group.Members.ToArray())
                        if (!ParkMatches(FindParkBlock(member.Id), group, member))
                        { group.Members.Remove(member); parkMembers.Remove(member.Id); }
                    if (group.Members.Count == 0) parkData.Groups.Remove(group);
                }
                current = NewParkData();
                if (!SameParkGeneration(parkData, current))
                    throw new InvalidDataException("Save/world generation changed during park verification.\n" +
                        string.Join("\n", ParkIdentityChanges(parkData, current)));
                if (parkData.Checksum != current.Checksum)
                {
                    foreach (string problem in ParkIdentityChanges(parkData, current)) PrintWarning(problem);
                    parkData.Checksum = current.Checksum;
                }
                SaveParkData();
                parkReady = true;
            }
            catch (Exception error)
            { PrintError("Park creation is unavailable; verified existing support is retained. " + error.Message); }
            finally { parkRefreshing = false; }
        }

        private void SaveParkData()
        {
            if (parkDataBroken || parkData == null || !SameParkWorld(parkData))
                throw new InvalidDataException("Park metadata has no matching native save/world identity.\n" +
                    string.Join("\n", ParkWorldProblems(parkData, true)));
            string json;
            try { json = JsonConvert.SerializeObject(parkData, Formatting.Indented); }
            catch (Exception error) { parkStorageFailed = true; parkStorageError = error.Message; throw; }
            if (json.Length > MaximumParkDataLength || Encoding.UTF8.GetByteCount(json) > MaximumParkDataLength)
                throw new InvalidDataException("Park metadata exceeds its one MiB capacity; no metadata file was written.");
            try { ParseParkData(json); }
            catch (Exception error) { parkStorageFailed = true; parkStorageError = error.Message; throw; }
            string filename = ParkFilename;
            string temporary = filename + ".writing";
            try
            {
                try
                {
                    using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }
                    if (File.Exists(filename)) File.Replace(temporary, filename, null);
                    else File.Move(temporary, filename);
                    if (File.ReadAllText(filename) != json) throw new IOException("Park metadata readback did not match.");
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            catch (IOException error) { parkStorageFailed = true; parkStorageError = error.Message; throw; }
            catch (UnauthorizedAccessException error) { parkStorageFailed = true; parkStorageError = error.Message; throw; }
        }

        private void SaveParksSafely()
        {
            try { SaveParkData(); }
            catch (Exception error)
            {
                parkReady = false;
                PrintError("Park metadata could not be saved; new park placement is disabled. " + error.Message);
            }
        }

        private void OnServerSave()
        {
            if (!parkBusy && !parkRefreshing && !parkDataBroken && parkData != null) SaveParksSafely();
            InitializeGroundedBuildings();
        }

        [ConsoleCommand("skate.piece")]
        private void SelectParkPiece(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin)
            { arg.ReplyWith("Use this command as an in-game administrator."); return; }
            if (arg.Args == null || arg.Args.Length != 1)
            { arg.ReplyWith("Usage: skate.piece <bank|kicker|quarterpipe|ledge-low|ledge-high|long-ledge|funbox|halfpipe|cancel>."); return; }
            if (string.Equals(arg.GetString(0), "cancel", StringComparison.OrdinalIgnoreCase))
            {
                ParkSelection previous;
                SkateParkSelectionState state = parkSelections.TryGetValue(player.userID, out previous) && ReferenceEquals(previous.Player, player) ?
                    previous.State : new SkateParkSelectionState();
                SkateParkSelectionState cancelled;
                if (SkateParkSelections.TryApply(state, SkateParkSelectionAction.Cancel, true, false,
                    clock.Elapsed.TotalSeconds, null, out cancelled))
                { parkSelectionEpoch = new object(); parkSelections.Remove(player.userID); arg.ReplyWith("Park selection cancelled."); }
                return;
            }
            SkateParkKind kind;
            if (!SkateParkLayout.TryKind(arg.GetString(0), out kind))
            { arg.ReplyWith("Unknown park piece. Use bank, kicker, quarterpipe, ledge-low, ledge-high, long-ledge, funbox or halfpipe."); return; }
            string failure;
            if (!Eligible(player))
            { arg.ReplyWith("Park placement is unavailable; be alive, awake, unmounted and on dry land."); return; }
            object epoch = parkSelectionEpoch = new object();
            if (!RefreshParkWorld(out failure)) { arg.ReplyWith(failure); return; }
            if (!ReferenceEquals(epoch, parkSelectionEpoch) || !ReferenceEquals(instance, this) || parkClosing ||
                !player.IsAdmin || !Eligible(player))
            { arg.ReplyWith("The selection request was superseded or the administrator became unavailable."); return; }
            PruneParkSelections();
            if (parkData.Groups.Count >= MaximumParkGroups ||
                (!parkSelections.ContainsKey(player.userID) && parkSelections.Count >= MaximumParkSelections))
            { arg.ReplyWith("The park group or pending-selection limit has been reached."); return; }
            SkateParkSelectionState selected;
            if (!SkateParkSelections.TrySelect(kind, true, true, clock.Elapsed.TotalSeconds, out selected))
            { arg.ReplyWith("Park selection time is unavailable."); return; }
            parkSelections[player.userID] = new ParkSelection { Player = player, State = selected };
            arg.ReplyWith("Selected " + SkateParkLayout.Name(kind) + ". Place one normal horizontal square foundation with a building plan within 120 seconds. Failed attempts retain this selection until its original expiry; " +
                (kind == SkateParkKind.Halfpipe ? "the halfpipe uses a Stone flat, Metal transitions/decks/sides and Wood back rails." : "the assembly will be Stone."));
        }

        private static bool ParkPlannerItem(BasePlayer player, ItemContainer belt, Item item, Planner planner)
        {
            return player != null && player.inventory != null && belt != null &&
                ReferenceEquals(player.inventory.containerBelt, belt) && belt.itemList != null &&
                belt.itemList.Count <= 32 && item != null && item.isServer &&
                ReferenceEquals(item.parent, belt) && belt.itemList.Contains(item) &&
                ReferenceEquals(item.GetOwnerPlayer(), player) && planner != null && !planner.IsDestroyed &&
                ReferenceEquals(item.GetHeldEntity(), planner) && ReferenceEquals(planner.GetOwnerPlayer(), player) &&
                ReferenceEquals(planner.GetParentEntity(), player);
        }

        private static bool FindParkPlanner(BasePlayer player, out ItemContainer belt, out Item item, out Planner planner)
        {
            belt = player.inventory == null ? null : player.inventory.containerBelt;
            item = null; planner = null;
            if (belt == null || belt.itemList == null || belt.itemList.Count > 32) return false;
            Planner active = player.GetHeldEntity() as Planner;
            foreach (Item candidate in belt.itemList)
            {
                if (candidate == null || !candidate.isServer) continue;
                Planner existing = candidate.GetHeldEntity() as Planner;
                if (!ParkPlannerItem(player, belt, candidate, existing)) continue;
                if (planner == null || ReferenceEquals(existing, active)) { item = candidate; planner = existing; }
                if (ReferenceEquals(existing, active)) break;
            }
            return planner != null;
        }

        private bool CurrentParkRequest(ParkPlacementRequest request)
        {
            if (request == null || !ReferenceEquals(instance, this) || parkClosing || !parkReady ||
                SaveRestore.IsSaving || !ReferenceEquals(parkData, request.Data) || !SameParkWorld(request.Data) ||
                request.Player == null || !request.Player.IsAdmin || !Eligible(request.Player) ||
                request.Planner == null || request.Planner.IsDestroyed ||
                !ReferenceEquals(request.Planner.GetOwnerPlayer(), request.Player)) return false;
            if (request.Selection != null)
                return CurrentParkSelection(request.Player, request.Selection) &&
                    ParkFoundationMatches(request.Foundation, request.FoundationId, request.Snapshot);
            return ParkPlannerItem(request.Player, request.Belt, request.PlannerItem, request.Planner);
        }

        [ConsoleCommand("skate.place")]
        private void PlaceParkPiece(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin)
            { arg.ReplyWith("Use this command as an in-game administrator."); return; }
            SkateParkKind kind;
            bool explicitAnchor;
            double x, y, z, yaw;
            string failure;
            string[] arguments = null;
            if (arg.Args != null && (arg.Args.Length == 1 || arg.Args.Length == 5))
            {
                arguments = new string[arg.Args.Length];
                for (int i = 0; i < arguments.Length; i++) arguments[i] = arg.GetString(i);
            }
            if (!SkateParkPlacement.TryRequest(arguments, out kind, out explicitAnchor, out x, out y, out z, out yaw, out failure))
            { arg.ReplyWith(failure); return; }
            try
            {
                if (!Eligible(player) || player.eyes == null)
                { arg.ReplyWith("Park placement is unavailable; be alive, awake, unmounted and on dry land."); return; }
                if (parkBusy || parkRefreshing || parkClosing || SaveRestore.IsSaving)
                { arg.ReplyWith("Park placement is busy or the world is being saved; try again shortly."); return; }
                if (!RefreshParkWorld(out failure)) { arg.ReplyWith(failure); return; }
                ItemContainer belt;
                Item item;
                Planner planner;
                if (!FindParkPlanner(player, out belt, out item, out planner))
                { arg.ReplyWith("Park placement needs an existing building plan in your belt for native building-hook checks. Hold the plan once if it has not initialized."); return; }
                Vector3 eye = player.eyes.position;
                if (!Coordinate(eye)) { arg.ReplyWith("The server eye position is unavailable."); return; }
                var failures = new List<string>();
                ParkNativePart[] definitions;
                ParkPrefabs(kind, failures, out definitions);
                if (failures.Count != 0) { arg.ReplyWith(string.Join("\n", failures)); return; }
                if (!explicitAnchor)
                {
                    Vector3 direction = player.eyes.HeadForward();
                    int mask = LayerMask.GetMask("Terrain", "Construction");
                    RaycastHit hit;
                    if (!Coordinate(direction) || direction.sqrMagnitude < 0.0001f || mask == 0 ||
                        LayerMask.NameToLayer("Terrain") < 0 || LayerMask.NameToLayer("Construction") < 0 ||
                        !Physics.Raycast(eye, direction.normalized, out hit, MaximumParkPlacementDistance, mask, QueryTriggerInteraction.Ignore) ||
                        !Coordinate(hit.point) || !Coordinate(hit.normal) || hit.normal.y <= 0)
                    { arg.ReplyWith("Aim at an upward-facing terrain or construction surface within 25 metres."); return; }
                    Vector3 viewAngles = player.eyes.rotation.eulerAngles;
                    if (!Coordinate(viewAngles))
                    { arg.ReplyWith("The server view heading is unavailable."); return; }
                    Bounds bounds = definitions[0].Construction.bounds;
                    if (!SkateParkLayout.TrySupportedAnchor(kind, hit.point.y, bounds.center.y, bounds.center.z,
                        bounds.size.y, bounds.size.z, out y))
                    { arg.ReplyWith("The aim surface or supported native layout height is outside world-coordinate bounds."); return; }
                    x = hit.point.x; z = hit.point.z;
                    yaw = (viewAngles.y % 360 + 360) % 360;
                }
                Vector3 anchor = new Vector3((float)x, (float)y, (float)z);
                if (!Coordinate(anchor) || Vector3.Distance(eye, anchor) > MaximumParkPlacementDistance)
                { arg.ReplyWith("The park anchor must be within 25 metres of your server eye position."); return; }
                var request = new ParkPlacementRequest { Player = player, Planner = planner, PlannerItem = item,
                    Belt = belt, Data = parkData };
                var group = new ParkGroup { Id = Guid.NewGuid().ToString("N"), Owner = player.userID,
                    Kind = SkateParkLayout.Name(kind), AnchorX = x, AnchorY = y, AnchorZ = z, AnchorYaw = yaw,
                    Members = new List<ParkMember>() };
                if (!BuildParkAssembly(request, kind, definitions, group, out failure))
                { arg.ReplyWith("Park " + group.Kind + " placement refused: " + failure); return; }
                arg.ReplyWith(ParkPlacementReply(group));
            }
            catch (Exception error) { arg.ReplyWith("Park placement refused: " + error.Message); }
        }

        private static string ParkPlacementReply(ParkGroup group)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "Park {0} placed at ({1:R}, {2:R}, {3:R}), yaw {4:R}; {5} saved native parts. Use a hammer to demolish the group.",
                group.Kind, group.AnchorX, group.AnchorY, group.AnchorZ, group.AnchorYaw, group.Members.Count);
        }

        private void PruneParkSelections()
        {
            double now = clock.Elapsed.TotalSeconds;
            foreach (var pair in parkSelections.ToArray())
                if (pair.Value.Player == null || !pair.Value.Player.IsConnected || now > pair.Value.Expires)
                    parkSelections.Remove(pair.Key);
        }

        [ConsoleCommand("skate.pieces")]
        private void ListParkPieces(ConsoleSystem.Arg arg)
        {
            BasePlayer actor = arg.Player();
            bool privileged = (actor != null && actor.IsAdmin) || arg.IsAdmin ||
                (actor == null && arg.IsServerside && arg.Connection == null);
            if (!privileged) { arg.ReplyWith("Only an administrator may list park groups."); return; }
            if (arg.Args != null && arg.Args.Length != 0) { arg.ReplyWith("Usage: skate.pieces."); return; }
            string failure;
            if (!RefreshParkWorld(out failure)) { arg.ReplyWith(failure); return; }
            var text = new StringBuilder("Park groups: ").Append(parkData.Groups.Count.ToString(CultureInfo.InvariantCulture))
                .Append("/256; new placement ready=").Append(parkReady).Append('.');
            foreach (SkateParkKind kind in Enum.GetValues(typeof(SkateParkKind)))
                text.Append("\n").Append(SkateParkLayout.Name(kind)).Append(": ")
                    .Append(parkData.Groups.Count(g => g.Kind == SkateParkLayout.Name(kind)).ToString(CultureInfo.InvariantCulture));
            foreach (ParkGroup group in parkData.Groups)
            {
                SkateParkKind kind;
                SkateParkLayout.TryKind(group.Kind, out kind);
                text.Append("\n").Append(group.Id).Append(' ').Append(group.Kind).Append(" owner=")
                    .Append(group.Owner.ToString(CultureInfo.InvariantCulture)).Append(" parts=")
                    .Append(group.Members.Count.ToString(CultureInfo.InvariantCulture)).Append('/')
                    .Append(SkateParkLayout.PartCount(kind).ToString(CultureInfo.InvariantCulture));
                if (group.Members.Count != SkateParkLayout.PartCount(kind)) text.Append(" (partial)");
            }
            arg.ReplyWith(text.ToString());
        }

        private bool CurrentParkSelection(BasePlayer player, ParkSelection selection)
        {
            ParkSelection current;
            return player != null && ReferenceEquals(selection.Player, player) &&
                parkSelections.TryGetValue(player.userID, out current) && ReferenceEquals(current, selection) &&
                SkateParkSelections.Active(selection.State, clock.Elapsed.TotalSeconds);
        }

        private void OnEntityBuilt(Planner planner, GameObject gameObject)
        {
            BasePlayer player = planner == null ? null : planner.GetOwnerPlayer();
            if (settings != null && settings.GroundedBuilding && gameObject != null)
            {
                try { GroundBuiltEntity(player, gameObject.GetComponent<StabilityEntity>()); }
                catch (Exception error) { PrintError("Grounded building registration was unavailable. " + error.Message); }
            }
            if (player == null) return;
            ParkSelection selection;
            if (!parkSelections.TryGetValue(player.userID, out selection)) return;
            if (!CurrentParkSelection(player, selection))
            { if (ReferenceEquals(selection.Player, player)) parkSelections.Remove(player.userID); return; }
            BuildingBlock anchor = gameObject == null ? null : gameObject.GetComponent<BuildingBlock>();
            if (anchor == null || anchor.PrefabName != ParkFoundationPrefab || selection.Pending) return;
            selection.Pending = true;
            ulong id = ParkId(anchor);
            // Planner still has normal item/material bookkeeping to do after this hook.
            NextTick(() =>
            {
                try { BuildParkPiece(player, planner, anchor, id, selection); }
                catch (Exception error) { ReportParkFailures(player, new[] { "Park placement failed: " + error.Message }); }
                finally { selection.Pending = false; }
            });
        }

        private static bool HorizontalParkFoundation(BuildingBlock anchor)
        {
            return anchor != null && !anchor.IsDestroyed && anchor.net != null && anchor.enableSaving &&
                anchor.PrefabName == ParkFoundationPrefab && anchor.GetParentEntity() == null &&
                Coordinate(anchor.transform.position) && Vector3.Dot(anchor.transform.up, Vector3.up) >= (1f - 1e-5f);
        }

        private static bool ParkFoundationMatches(BuildingBlock anchor, ulong id, ParkAnchorSnapshot snapshot)
        {
            return HorizontalParkFoundation(anchor) && ParkId(anchor) == id && anchor.OwnerID == snapshot.Owner &&
                anchor.buildingID == snapshot.Building &&
                (anchor.transform.position - snapshot.Position).sqrMagnitude <= 0.000001f &&
                Quaternion.Angle(anchor.transform.rotation, snapshot.Rotation) <= 0.01f;
        }

        private static double ParkFoundationTop(BuildingBlock anchor, Bounds bounds)
        {
            double top = double.NegativeInfinity;
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 point = anchor.transform.position + anchor.transform.rotation *
                            (bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z)));
                        if (!Coordinate(point)) return double.NaN;
                        top = Math.Max(top, point.y);
                    }
            return top;
        }

        private void ReportParkFailures(BasePlayer player, IEnumerable<string> failures)
        {
            foreach (string failure in failures)
            {
                PrintWarning(failure);
                if (player != null && player.IsConnected) player.ConsoleMessage(failure);
            }
        }

        private static bool ParkPrefab(SkateParkPart part, List<string> failures, out ParkNativePart definition)
        {
            string path = part.Prefab;
            string role = path + " material=" + part.Material;
            int before = failures.Count;
            definition = null;
            Construction construction;
            DeployVolume[] volumes;
            ParkNativeMetadata(path, role, failures, out construction, out volumes);
            BuildingGrade.Enum grade;
            switch (part.Material)
            {
                case SkateParkMaterial.Stone: grade = BuildingGrade.Enum.Stone; break;
                case SkateParkMaterial.Metal: grade = BuildingGrade.Enum.Metal; break;
                case SkateParkMaterial.Wood: grade = BuildingGrade.Enum.Wood; break;
                default: failures.Add(role + ": material is unsupported."); return false;
            }
            if (construction != null)
            {
                // Material lookup can silently fall back; require the exact requested grade and default skin.
                var selected = construction.GetGrade(grade, 0);
                if (selected == null) failures.Add(role + ": Construction.GetGrade is unavailable.");
                else if (selected.gradeBase == null) failures.Add(role + ": gradeBase is unavailable.");
                else
                {
                    if (selected.gradeBase.type != grade) failures.Add(role + ": gradeBase.type=" + selected.gradeBase.type + "; required=" + grade + ".");
                    if (selected.gradeBase.skin != 0) failures.Add(role + ": gradeBase.skin=" + selected.gradeBase.skin.ToString(CultureInfo.InvariantCulture) + "; required=0.");
                }
                Bounds bounds = construction.bounds;
                CheckParkBound(failures, role, "bounds.size.x", bounds.size.x, 2.9, 3.1);
                CheckParkBound(failures, role, "bounds.center.x", bounds.center.x, -0.05, 0.05);
                if (path == SkateParkLayout.FloorPrefab)
                {
                    CheckParkBound(failures, role, "bounds.size.z", bounds.size.z, 2.9, 3.1);
                    CheckParkBound(failures, role, "bounds.size.y", bounds.size.y, 0, 1, true);
                    CheckParkBound(failures, role, "bounds.center.z", bounds.center.z, -0.05, 0.05);
                    CheckParkBound(failures, role, "bounds.center.y", bounds.center.y, -0.5, 0.5);
                }
                else if (path == SkateParkLayout.LowWallPrefab || path == SkateParkLayout.HalfWallPrefab)
                {
                    double height = path == SkateParkLayout.LowWallPrefab ? 1 : 1.5;
                    CheckParkBound(failures, role, "bounds.size.y", bounds.size.y, height - 0.1, height + 0.1);
                    CheckParkBound(failures, role, "bounds.size.z", bounds.size.z, 0, 0.6, true);
                    CheckParkBound(failures, role, "bounds.center.z", bounds.center.z, -0.05, 0.05);
                    CheckParkBound(failures, role, "bounds.min.y", bounds.min.y, -0.05, 0.05);
                }
                else failures.Add(role + ": prefab role is unsupported.");
            }
            if (failures.Count != before) return false;
            definition = new ParkNativePart { Prefab = path, Construction = construction, Volumes = volumes, Grade = grade };
            return true;
        }

        private static void CheckParkBound(List<string> failures, string role, string field, double actual, double minimum, double maximum, bool exclusiveMinimum = false)
        {
            if (!SkateSessionBounds.Finite(actual) || actual < minimum || (exclusiveMinimum && actual == minimum) || actual > maximum)
                failures.Add(role + ": " + field + "=" + actual.ToString("R", CultureInfo.InvariantCulture) + "; required=" + (exclusiveMinimum ? "(" : "[") +
                    minimum.ToString("R", CultureInfo.InvariantCulture) + "," + maximum.ToString("R", CultureInfo.InvariantCulture) + "].");
        }

        private static void ParkNativeMetadata(string path, string role, List<string> failures,
            out Construction construction, out DeployVolume[] volumes)
        {
            construction = null; volumes = null;
            var manifest = GameManifest.Current;
            if (manifest == null) failures.Add(role + ": GameManifest.Current is unavailable.");
            else if (manifest.entities == null) failures.Add(role + ": GameManifest.Current.entities is unavailable.");
            else if (!manifest.entities.Any(p => string.Equals(p, path, StringComparison.Ordinal)))
                failures.Add(role + ": GameManifest.Current.entities does not contain the prefab.");
            if (GameManager.server == null) failures.Add(role + ": GameManager.server is unavailable.");
            else
            {
                var prefab = GameManager.server.FindPrefab(path);
                if (prefab == null) failures.Add(role + ": GameManager.FindPrefab is unavailable.");
                else if (prefab.GetComponent<BuildingBlock>() == null) failures.Add(role + ": native BuildingBlock component is unavailable.");
            }
            uint id = StringPool.Get(path);
            if (id == 0) failures.Add(role + ": StringPool prefab ID is unavailable.");
            else if (StringPool.Get(id) != path) failures.Add(role + ": StringPool prefab ID does not resolve to the required path.");
            if (PrefabAttribute.server == null) failures.Add(role + ": PrefabAttribute.server is unavailable.");
            else if (id != 0 && StringPool.Get(id) == path)
            {
                construction = PrefabAttribute.server.Find<Construction>(id);
                volumes = PrefabAttribute.server.FindAll<DeployVolume>(id);
                if (construction == null) failures.Add(role + ": Construction is unavailable.");
                if (volumes == null) failures.Add(role + ": DeployVolume array is unavailable.");
                else for (int i = 0; i < volumes.Length; i++)
                    if (volumes[i] == null) failures.Add(role + ": DeployVolume[" + i.ToString(CultureInfo.InvariantCulture) + "] is unavailable.");
            }
        }

        private static bool ParkPrefabs(SkateParkKind kind, List<string> failures, out ParkNativePart[] definitions)
        {
            definitions = new ParkNativePart[SkateParkLayout.PartCount(kind)];
            if (definitions.Length == 0) { failures.Add("Park layout kind is unavailable."); return false; }
            if (TerrainMeta.HeightMap == null) failures.Add("TerrainMeta.HeightMap is unavailable.");
            if (LayerMask.GetMask("World", "Construction", "Deployed", "Player (Server)") == 0)
                failures.Add("Park occupancy layer mask is unavailable.");
            var cache = new Dictionary<string, ParkNativePart>(StringComparer.Ordinal);
            for (int i = 0; i < definitions.Length; i++)
            {
                SkateParkPart part;
                if (!SkateParkLayout.TryPart(kind, i, out part))
                { failures.Add("Park layout part[" + i.ToString(CultureInfo.InvariantCulture) + "] is unavailable."); continue; }
                string key = part.Prefab + ":" + ((int)part.Material).ToString(CultureInfo.InvariantCulture);
                ParkNativePart definition;
                if (!cache.TryGetValue(key, out definition))
                {
                    ParkPrefab(part, failures, out definition);
                    cache.Add(key, definition);
                }
                definitions[i] = definition;
            }
            return failures.Count == 0;
        }

        private static bool ParkFoundation(BuildingBlock anchor, List<string> failures, out Construction construction)
        {
            int before = failures.Count;
            DeployVolume[] volumes;
            string role = ParkFoundationPrefab + " material=" + anchor.grade;
            ParkNativeMetadata(ParkFoundationPrefab, role, failures, out construction, out volumes);
            if (construction != null)
            {
                Bounds bounds = construction.bounds;
                CheckParkBound(failures, role, "bounds.size.x", bounds.size.x, 2.9, 3.1);
                CheckParkBound(failures, role, "bounds.size.z", bounds.size.z, 2.9, 3.1);
                CheckParkBound(failures, role, "bounds.size.y", bounds.size.y, 0, 6, true);
                CheckParkBound(failures, role, "bounds.center.x", bounds.center.x, -0.05, 0.05);
                CheckParkBound(failures, role, "bounds.center.z", bounds.center.z, -0.05, 0.05);
                CheckParkBound(failures, role, "bounds.center.y", bounds.center.y, -100000, 100000);
                if (!ReferenceEquals(anchor.blockDefinition, construction)) failures.Add(role + ": placed blockDefinition does not match native Construction.");
            }
            return failures.Count == before;
        }

        private static bool ParkProximity(BasePlayer player, Construction construction, Vector3 position,
            Quaternion rotation, BuildingBlock anchor, List<BuildingBlock> transactionParts = null)
        {
            // Exclude only the normal support and this transaction's parts; unrelated neighbours still apply.
            var obb = new OBB(position, rotation, construction.bounds);
            var neighbours = new List<BuildingBlock>();
            Vis.Entities(obb.position, obb.extents.magnitude + 2, neighbours, 2097152, QueryTriggerInteraction.Collide);
            uint buildingId = 0;
            foreach (BuildingBlock neighbour in neighbours)
            {
                if ((anchor != null && ReferenceEquals(neighbour, anchor)) || (transactionParts != null && transactionParts.Contains(neighbour))) continue;
                if (neighbour == null || neighbour.IsDestroyed || neighbour.blockDefinition == null) return false;
                var a = BuildingProximity.GetProximity(construction, position, rotation, neighbour.blockDefinition,
                    neighbour.transform.position, neighbour.transform.rotation);
                var b = BuildingProximity.GetProximity(neighbour.blockDefinition, neighbour.transform.position,
                    neighbour.transform.rotation, construction, position, rotation);
                if (a.connection || b.connection)
                {
                    var building = neighbour.GetBuilding();
                    var privilege = building == null ? null : building.GetDominatingBuildingPrivilege();
                    if (privilege != null)
                    {
                        if (!construction.canBypassBuildingPermission && !privilege.CanBuild(player)) return false;
                        if (buildingId != 0 && buildingId != building.ID) return false;
                        buildingId = building.ID;
                    }
                }
                if (a.hit || b.hit)
                {
                    var line = a.sqrDist <= b.sqrDist ? a.line : b.line;
                    Vector3 delta = line.point1 - line.point0;
                    if (Math.Abs(delta.y) <= 1.49f && Math.Sqrt(delta.x * delta.x + delta.z * delta.z) <= 1.49) return false;
                }
            }
            return true;
        }

        private static bool ParkPreflight(BasePlayer player, Planner planner, BuildingBlock anchor,
            ParkNativePart[] definitions, ParkMember[] parts, out string failure)
        {
            failure = "The complete park assembly needs dry, clear space and building permission.";
            int mask = LayerMask.GetMask("World", "Construction", "Deployed", "Player (Server)");
            if (mask == 0 || TerrainMeta.HeightMap == null) return false;
            foreach (ParkMember member in parts)
            {
                Construction construction = definitions[member.Index].Construction;
                DeployVolume[] volumes = definitions[member.Index].Volumes;
                Vector3 position = ParkPosition(member);
                Quaternion rotation = ParkRotation(member);
                Bounds bounds = construction.bounds;
                if (!player.CanBuild(position, rotation, bounds, false))
                { failure = "You do not have building permission across the complete park assembly."; return false; }
                var target = new Construction.Target { valid = true, player = player, position = position,
                    rotation = rotation.eulerAngles, normal = Vector3.up, onTerrain = true };
                if (Interface.CallHook("CanBuild", planner, construction, target) != null)
                { failure = "A building hook rejected part of the park assembly."; return false; }
                if (!ParkProximity(player, construction, position, rotation, anchor))
                { failure = "The park assembly conflicts with a neighbouring building or its permission."; return false; }
                if (DeployVolume.Check(position, rotation, volumes, null, DeployVolume.TypeFilterMode.Ignore, anchor, mask, false))
                { failure = "A native deploy volume is occupied across the park assembly."; return false; }
                if (!ParkFootprint(position, rotation, bounds, anchor, null, mask, out failure)) return false;
            }
            return true;
        }

        private static bool ParkFootprint(Vector3 position, Quaternion rotation, Bounds bounds, BuildingBlock anchor,
            List<BuildingBlock> transactionParts, int mask, out string failure)
        {
            failure = "The complete park assembly needs dry, clear space.";
            Vector3 extents = bounds.extents;
            extents.x = Math.Max(0.01f, extents.x - 0.015f);
            extents.z = Math.Max(0.01f, extents.z - 0.015f);
            foreach (Collider collider in Physics.OverlapBox(position + rotation * bounds.center, extents, rotation,
                mask, QueryTriggerInteraction.Ignore))
            {
                if (collider == null) continue;
                BaseEntity entity = collider.ToBaseEntity();
                if ((anchor != null && ReferenceEquals(entity, anchor)) || (transactionParts != null && transactionParts.Contains(entity as BuildingBlock))) continue;
                failure = "The park assembly overlaps another entity or world obstruction.";
                return false;
            }
            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                {
                    Vector3 point = position + rotation * new Vector3(bounds.center.x + x * bounds.extents.x,
                        bounds.min.y, bounds.center.z + z * bounds.extents.z);
                    float height = TerrainMeta.HeightMap.GetHeight(point);
                    if (!SkateSessionBounds.Finite(height) || height > point.y + 0.1f ||
                        WaterLevel.Test(point + Vector3.up * 0.1f, true, false))
                    { failure = "The complete park footprint must be above dry terrain."; return false; }
                }
            return true;
        }

        private static ParkAnchorSnapshot CaptureParkAnchor(BuildingBlock anchor)
        {
            return new ParkAnchorSnapshot { Position = anchor.transform.position, Rotation = anchor.transform.rotation,
                Owner = anchor.OwnerID, Building = anchor.buildingID };
        }

        private static void RefreshParkBlock(BuildingBlock block, Bounds oldBounds)
        {
            block.NetworkPositionTick();
            block.RefreshEntityLinks();
            block.InitializeSupports();
            block.UpdateSurroundingEntities();
            StabilityEntity.UpdateSurroundingsQueue.NotifyNeighbours(oldBounds);
            block.UpdateSkin(true);
            block.RefreshNeighbours(false);
            block.SendNetworkUpdateImmediate();
        }

        private static Bounds ParkWorldBounds(BuildingBlock block)
        {
            OBB obb = block.WorldSpaceBounds();
            return new Bounds(obb.position, Vector3.one * (obb.extents.magnitude * 2));
        }

        private void BuildParkPiece(BasePlayer player, Planner planner, BuildingBlock anchor, ulong anchorId, ParkSelection selection)
        {
            if (!CurrentParkSelection(player, selection) || parkClosing) return;
            string failure;
            if (!RefreshParkWorld(out failure))
            { ReportParkFailures(player, failure.Split('\n')); return; }
            if (parkBusy || SaveRestore.IsSaving || !player.IsAdmin || !Eligible(player) || planner == null ||
                !ReferenceEquals(planner.GetOwnerPlayer(), player) || !HorizontalParkFoundation(anchor) ||
                ParkId(anchor) != anchorId || anchor.OwnerID != (ulong)player.userID || parkMembers.ContainsKey(anchorId))
            { ReportParkFailures(player, new[] { "Park placement refused; use a new normal horizontal square foundation while alive, awake, unmounted and on dry land. The selection remains until its original expiry." }); return; }
            if (parkData.Groups.Count >= MaximumParkGroups)
            { ReportParkFailures(player, new[] { "Park group limit reached: 256. The selection remains until its original expiry." }); return; }
            var failures = new List<string>();
            ParkNativePart[] definitions;
            ParkPrefabs(selection.Kind, failures, out definitions);
            Construction foundation;
            ParkFoundation(anchor, failures, out foundation);
            if (failures.Count != 0) { ReportParkFailures(player, failures); return; }
            if (!CurrentParkSelection(player, selection)) return;
            ParkAnchorSnapshot snapshot = CaptureParkAnchor(anchor);
            Bounds floorBounds = definitions[0].Construction.bounds;
            double baseline;
            if (!SkateParkLayout.TrySupportedAnchor(selection.Kind, ParkFoundationTop(anchor, foundation.bounds),
                floorBounds.center.y, floorBounds.center.z, floorBounds.size.y, floorBounds.size.z, out baseline))
            { ReportParkFailures(player, new[] { "Park foundation top or supported layout height is invalid or outside world-coordinate bounds." }); return; }
            var group = new ParkGroup { Id = Guid.NewGuid().ToString("N"), Owner = player.userID,
                Kind = SkateParkLayout.Name(selection.Kind), AnchorX = snapshot.Position.x, AnchorY = baseline,
                AnchorZ = snapshot.Position.z, AnchorYaw = snapshot.Rotation.eulerAngles.y, Members = new List<ParkMember>() };
            var request = new ParkPlacementRequest { Player = player, Planner = planner, Data = parkData,
                Foundation = anchor, FoundationId = anchorId, Snapshot = snapshot, Selection = selection,
                Building = snapshot.Building };
            if (!BuildParkAssembly(request, selection.Kind, definitions, group, out failure))
            { ReportParkFailures(player, failure.Split('\n')); return; }
            player.ConsoleMessage(ParkPlacementReply(group) + " The original foundation remains a separate normal building piece.");
        }

        private bool BuildParkAssembly(ParkPlacementRequest request, SkateParkKind kind,
            ParkNativePart[] definitions, ParkGroup group, out string failure)
        {
            failure = "Park placement is busy, unavailable or the world is being saved.";
            if (parkBusy || parkRefreshing || !CurrentParkRequest(request)) return false;
            // Kill hooks may reenter placement while a previous rollback is being retried.
            if (parkRollback.Count != 0)
            {
                parkBusy = true;
                try { CleanupParkRollback(); }
                finally { parkBusy = false; }
            }
            if (parkRollback.Count != 0)
            { failure = "Previous unsaved park cleanup is incomplete; new placement is disabled until those parts are removed."; return false; }
            if (!CurrentParkRequest(request)) return false;
            if (request.Data.Groups.Count >= MaximumParkGroups)
            { failure = "Park group limit reached: 256."; return false; }
            BasePlayer player = request.Player;
            Planner planner = request.Planner;
            BuildingBlock anchor = request.Foundation;
            var parts = new ParkMember[SkateParkLayout.PartCount(kind)];
            for (int i = 0; i < parts.Length; i++)
            {
                SkateParkTransform transform;
                if (!SkateParkLayout.TryTransform(kind, i, group.AnchorX, group.AnchorY, group.AnchorZ, group.AnchorYaw, out transform))
                { failure = "Park layout part[" + i.ToString(CultureInfo.InvariantCulture) + "] exceeds world-coordinate bounds."; return false; }
                parts[i] = new ParkMember { Index = i, Prefab = StringPool.Get(definitions[i].Prefab), X = transform.X, Y = transform.Y,
                    Z = transform.Z, Yaw = transform.Yaw, Pitch = transform.Pitch };
            }
            var created = new List<BuildingBlock>();
            bool recorded = false, committed = false;
            parkBusy = true;
            try
            {
                if (!ParkPreflight(player, planner, anchor, definitions, parts, out failure))
                    throw new InvalidDataException(failure);
                // Native/plugin callbacks can change the actor, planner, world or foundation.
                if (!CurrentParkRequest(request))
                    throw new InvalidDataException("The administrator, building plan, save/world or placement request changed during preflight.");
                for (int i = 0; i < parts.Length; i++)
                {
                    if (!CurrentParkRequest(request))
                        throw new InvalidDataException("The administrator, building plan, save/world or placement request changed during creation.");
                    BaseEntity entity = GameManager.server.CreateEntity(definitions[i].Prefab,
                        ParkPosition(parts[i]), ParkRotation(parts[i]), true);
                    if (entity == null) throw new InvalidDataException("A park part could not be created: " + definitions[i].Prefab + ".");
                    parkRollback.Add(entity);
                    BuildingBlock block = entity as BuildingBlock;
                    if (block == null) throw new InvalidDataException("A park prefab is not a native building block: " + definitions[i].Prefab + ".");
                    created.Add(block);
                    block.enableSaving = false;
                    block.CullBushes = false;
                    block.grounded = true;
                    block.OwnerID = group.Owner;
                    block.skinID = 0;
                    block.Spawn();
                    if (block.IsDestroyed || block.net == null || block.blockDefinition == null)
                        throw new InvalidDataException("A park part failed to initialize: " + definitions[i].Prefab + ".");
                    if (!CurrentParkRequest(request))
                        throw new InvalidDataException("The administrator, building plan, save/world or placement request changed during part initialization.");
                    parts[i].Id = ParkId(block);
                    if (parts[i].Id == 0 || !ParkMatches(block, group, parts[i]) ||
                        !ReferenceEquals(block.blockDefinition, definitions[i].Construction))
                        throw new InvalidDataException("A new park part has an invalid native ID, prefab, owner or pose: " + definitions[i].Prefab + ".");
                    block.skinID = 0;
                    block.SetGrade(definitions[i].Grade);
                    block.SetHealthToMax();
                    if (request.Selection == null && i == 0)
                    {
                        // The DecayEntity overload allocates a real native building; uint zero does not.
                        block.AttachToBuilding((DecayEntity)null);
                        request.Building = block.buildingID;
                    }
                    else block.AttachToBuilding(request.Building);
                    if (request.Building == 0 || block.buildingID != request.Building)
                        throw new InvalidDataException("Native park building attachment failed.");
                    block.StartBeingDemolishable();
                    RefreshParkBlock(block, ParkWorldBounds(block));
                }
                foreach (ParkMember part in parts)
                {
                    if (!CurrentParkRequest(request)) throw new InvalidDataException("The placement request changed before final building-hook checks.");
                    Construction construction = definitions[part.Index].Construction;
                    var target = new Construction.Target { valid = true, player = player, onTerrain = true,
                        position = ParkPosition(part), rotation = ParkRotation(part).eulerAngles,
                        normal = Vector3.up };
                    if (Interface.CallHook("CanBuild", planner, construction, target) != null)
                        throw new InvalidDataException("A building hook rejected final park registration.");
                }
                foreach (ParkMember part in parts)
                {
                    BuildingBlock block = created[part.Index];
                    ParkNativePart definition = definitions[part.Index];
                    Construction construction = definition.Construction;
                    if (!CurrentParkRequest(request) || !ParkMatches(block, group, part) ||
                        block.grade != definition.Grade || block.skinID != 0 || !ReferenceEquals(block.blockDefinition, construction) ||
                        !block.grounded || block.enableSaving || block.buildingID != request.Building ||
                        !player.CanBuild(ParkPosition(part), ParkRotation(part), construction.bounds, false) ||
                        !ParkProximity(player, construction, ParkPosition(part), ParkRotation(part), anchor, created) ||
                        !ParkFootprint(ParkPosition(part), ParkRotation(part), construction.bounds, anchor, created,
                            LayerMask.GetMask("World", "Construction", "Deployed", "Player (Server)"), out failure))
                        throw new InvalidDataException("A placement request or park member changed during final permission verification.");
                }
                group.Members.AddRange(parts);
                request.Data.Groups.Add(group);
                recorded = true;
                // Persist metadata before registering the spawned parts in the native save list.
                SaveParkData();
                foreach (ParkMember part in parts)
                {
                    BuildingBlock block = created[part.Index];
                    block.EnableSaving(true);
                    if (!CurrentParkRequest(request) || !ParkMatches(block, group, part) ||
                        block.grade != definitions[part.Index].Grade || block.skinID != 0 ||
                        !ReferenceEquals(block.blockDefinition, definitions[part.Index].Construction) ||
                        !block.enableSaving || !block.grounded || block.buildingID != request.Building)
                        throw new InvalidDataException("A placement request or park member changed during save registration.");
                }
                SkateParkSelectionState consumed = new SkateParkSelectionState();
                if (!CurrentParkRequest(request) || (request.Selection != null &&
                    !SkateParkSelections.TryApply(request.Selection.State, SkateParkSelectionAction.PlacementSucceeded,
                        player.IsAdmin, parkReady, clock.Elapsed.TotalSeconds, null, out consumed)))
                    throw new InvalidDataException("The placement request expired or changed before completion.");
                foreach (ParkMember part in parts) parkMembers.Add(part.Id, group);
                foreach (BuildingBlock block in created) parkRollback.Remove(block);
                if (request.Selection != null)
                {
                    request.Selection.State = consumed;
                    parkSelectionEpoch = new object();
                    parkSelections.Remove(player.userID);
                }
                committed = true;
                failure = null;
                return true;
            }
            catch (Exception error)
            {
                SkateParkSelectionState retained;
                if (request.Selection != null && CurrentParkSelection(player, request.Selection) &&
                    SkateParkSelections.TryApply(request.Selection.State, SkateParkSelectionAction.PlacementFailed,
                        player != null && player.IsAdmin, parkReady, clock.Elapsed.TotalSeconds, null, out retained))
                    request.Selection.State = retained;
                if (recorded)
                {
                    request.Data.Groups.Remove(group);
                    foreach (ParkMember part in parts) parkMembers.Remove(part.Id);
                }
                CleanupParkRollback();
                if (recorded) SaveParksSafely();
                failure = error.Message + (parkRollback.Count == 0 ? "" : " Part cleanup is incomplete; new placement is disabled until cleanup succeeds.");
                return false;
            }
            finally
            {
                parkBusy = false;
                if (!committed && parkClosing) CleanupParkRollback();
            }
        }

        private void CleanupParkRollback()
        {
            for (int i = parkRollback.Count - 1; i >= 0; i--)
            {
                BaseEntity entity = parkRollback[i];
                try
                {
                    if (entity != null && !entity.IsDestroyed)
                    {
                        entity.EnableSaving(false);
                        entity.Kill();
                    }
                    if (entity != null && !entity.IsDestroyed)
                    { PrintError("Unsaved park rollback part removal was refused; it remains tracked."); continue; }
                    parkRollback.RemoveAt(i);
                }
                catch (Exception error) { PrintError("Unsaved park rollback cleanup failed. " + error.Message); }
            }
        }

        private ParkGroup VerifiedParkGroup(BuildingBlock block)
        {
            ParkGroup group;
            if (block == null || parkDataBroken || parkData == null || !SameParkGeneration(parkData) ||
                !parkMembers.TryGetValue(ParkId(block), out group)) return null;
            ParkMember member = group.Members.FirstOrDefault(m => m.Id == ParkId(block));
            return member != null && ParkMatches(block, group, member) ? group : null;
        }

        private object OnStructureRotate(BuildingBlock block, BasePlayer player)
        {
            if (VerifiedParkGroup(block) == null) return null;
            if (player != null) player.ConsoleMessage("Park assembly rotation is fixed. Demolish the group and place a new selected piece to turn it.");
            return false;
        }

        private object OnStructureDemolish(DecayEntity entity, BasePlayer player, bool immediate)
        {
            if (!ReferenceEquals(parkDemolitionHookBlock, null) && ReferenceEquals(entity, parkDemolitionHookBlock) && ReferenceEquals(player, parkDemolitionHookPlayer) &&
                immediate == parkDemolitionHookImmediate && parkBusy) return null;
            ParkGroup group = VerifiedParkGroup(entity as BuildingBlock);
            if (group == null) return null;
            if (parkBusy || parkClosing || player == null) return false;
            if (!parkDemolitionPending.Add(group.Id)) return false;
            BuildingBlock origin = entity as BuildingBlock;
            NextTick(() =>
            {
                parkDemolitionPending.Remove(group.Id);
                if (!parkClosing && ReferenceEquals(VerifiedParkGroup(origin), group))
                    DemolishParkGroup(group, player, immediate);
            });
            // Return before any destruction: the original dispatcher must finish its other hooks first.
            return false;
        }

        private void DemolishParkGroup(ParkGroup group, BasePlayer player, bool immediate)
        {
            if (parkBusy || parkClosing || player == null || !player.IsConnected || player.IsNpc || !player.CanInteract()) return;
            var blocks = new List<BuildingBlock>();
            bool removalStarted = false;
            parkBusy = true;
            try
            {
                foreach (ParkMember member in group.Members)
                {
                    BuildingBlock block = FindParkBlock(member.Id);
                    if (!ParkMatches(block, group, member))
                        throw new InvalidDataException("Park group identity changed; demolition refused.");
                    bool permitted;
                    if (immediate)
                    {
                        // Mirror the native admin immediate path while still honoring each CanDemolish veto.
                        object decision = Interface.CallHook("CanDemolish", player, block);
                        permitted = player.IsAdmin && (!(decision is bool) || (bool)decision);
                    }
                    else permitted = block.CanDemolish(player);
                    if (!permitted)
                        throw new InvalidDataException("Demolition is not permitted for every park member; no part was removed.");
                    blocks.Add(block);
                }
                // A later permission hook can mutate an earlier member; validate the whole set once again.
                for (int i = 0; i < blocks.Count; i++)
                {
                    parkDemolitionHookBlock = blocks[i];
                    parkDemolitionHookPlayer = player;
                    parkDemolitionHookImmediate = immediate;
                    try
                    {
                        if (Interface.CallHook("OnStructureDemolish", blocks[i], player, immediate) != null)
                            throw new InvalidDataException("A demolition hook rejected the park group; no part was removed.");
                    }
                    finally { parkDemolitionHookBlock = null; parkDemolitionHookPlayer = null; }
                }
                for (int i = 0; i < blocks.Count; i++)
                    if (parkClosing || !player.IsConnected || !player.CanInteract() || (immediate && !player.IsAdmin) ||
                        !ParkMatches(blocks[i], group, group.Members[i]))
                        throw new InvalidDataException("A park member changed during demolition permission checks.");
                for (int i = 0; i < blocks.Count; i++)
                {
                    if (parkClosing || !player.IsConnected || !player.CanInteract() || (immediate && !player.IsAdmin) ||
                        !ParkMatches(blocks[i], group, group.Members[i]))
                        throw new InvalidDataException("A remaining park member or actor changed during removal; remaining parts were retained.");
                    removalStarted = true;
                    blocks[i].Kill(BaseNetworkable.DestroyMode.Gib);
                }
            }
            catch (Exception error)
            {
                PrintWarning("Park group demolition refused or incomplete. " + error.Message);
                if (player != null && player.IsConnected) player.ConsoleMessage(error.Message);
            }
            finally
            {
                parkDemolitionHookBlock = null;
                parkDemolitionHookPlayer = null;
                parkBusy = false;
                PruneDestroyedParkMembers(group);
            }
            if (player != null && player.IsConnected)
                player.ConsoleMessage(group.Members.Count == 0 ? "Park group demolished." : removalStarted ?
                    "Park demolition is incomplete; surviving parts remain grouped. A kill hook or server error prevented removal." :
                    "Park group remains; check the preceding refusal. Surviving parts stay grouped.");
        }

        private void PruneDestroyedParkMembers(ParkGroup group)
        {
            bool changed = false;
            foreach (ParkMember member in group.Members.ToArray())
            {
                BuildingBlock block = FindParkBlock(member.Id);
                if (block != null && !block.IsDestroyed) continue;
                group.Members.Remove(member);
                parkMembers.Remove(member.Id);
                changed = true;
            }
            if (group.Members.Count == 0) parkData.Groups.Remove(group);
            if (changed) SaveParksSafely();
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            ParkGroup group;
            ulong id = ParkId(entity);
            QueueGroundedRemoval(entity, id);
            if (id == 0 || !parkMembers.TryGetValue(id, out group)) return;
            // Kill hooks can cancel destruction. Never delete metadata or cascade while the origin is alive.
            NextTick(() =>
            {
                if (!parkClosing && (entity == null || entity.IsDestroyed) && parkData != null && SameParkGeneration(parkData))
                    PruneDestroyedParkMembers(group);
            });
        }

        private void QueueGroundedRemoval(BaseNetworkable entity, ulong id)
        {
            GroundedMember member;
            StabilityEntity expected;
            if (id == 0 || !groundedMembers.TryGetValue(id, out member) ||
                !groundedEntities.TryGetValue(id, out expected) || !ReferenceEquals(entity, expected)) return;
            NextTick(() =>
            {
                GroundedMember current;
                if (!ReferenceEquals(instance, this) || parkClosing || (entity != null && !entity.IsDestroyed) ||
                    !SameGroundedGeneration(groundedData) || !groundedMembers.TryGetValue(id, out current) || !ReferenceEquals(member, current)) return;
                groundedMembers.Remove(id);
                groundedEntities.Remove(id);
                groundedData.Members.Remove(member);
                if (!groundedDataBroken && !groundedStorageFailed && !groundedRefreshing && !groundedBusy)
                    try { SaveGroundedData(); }
                    catch (Exception error) { PrintError("Destroyed grounded building record could not be saved. " + error.Message); }
            });
        }

        private void OnEntityStabilityCheck(StabilityEntity entity)
        {
            if (VerifiedGroundedMember(entity)) entity.grounded = true;
            BuildingBlock block = entity as BuildingBlock;
            if (VerifiedParkGroup(block) != null) block.grounded = true;
            // Do not return a non-null override: normal stability processing and other hooks still run.
        }

        private bool Supported(Vector3 point)
        {
            RaycastHit hit;
            return Physics.Raycast(point + Vector3.up * 0.15f, Vector3.down, out hit, 0.3f,
                groundMask, QueryTriggerInteraction.Ignore) && hit.normal.y >= 0.70710677f;
        }

        private void BeginBatch(ref BasePlayer.PlayerServerStates.ReadOnly states, NativeArray<int>.ReadOnly indices)
        {
            if (!ready || validating || batch == long.MaxValue) { validating = false; return; }
            validating = true;
            batch++;
            double now = clock.Elapsed.TotalSeconds;
            foreach (int index in indices)
            {
                BasePlayer player = states.PlayerCache.Objects[index];
                if (player == null) continue;
                Session session;
                if (sessions.TryGetValue(player.userID, out session) && !ReferenceEquals(player, session.Player))
                {
                    Disarm(session, Snapshot(session, SkateSessionReason.IdentityChanged, now));
                    diagnostics.Remove(player.userID, session.Player);
                    sessions.Remove(player.userID); session = null;
                }
                if (player.ActivePlayerInd != index || !Eligible(player) || !Permitted(player) ||
                    OptedOut(player) || states.CachedStates[index].IsSwimming)
                {
                    SkateSessionReason reason = player.ActivePlayerInd != index ? SkateSessionReason.CacheIndexMismatch : PolicyReason(player);
                    if (reason == SkateSessionReason.None) reason = SkateSessionReason.Swimming;
                    Stop(player, reason); continue;
                }
                if (session == null)
                {
                    session = new Session { Player = player, History = diagnostics.GetOrCreate(player.userID, player),
                        Observation = SkateSessionObservation.Empty(now, SkateSessionReason.None) };
                    sessions[player.userID] = session;
                }
                if (session.History == null) session.History = diagnostics.GetOrCreate(player.userID, player);
                session.Allowed = false;
                session.MovementAllowed = false;
                session.MovementSeconds = session.MovementVerticalPath = double.NaN;
                session.MovementReason = SkateSessionReason.Inactive;
                session.Batch = batch;
                SkateSessionObservation value = Snapshot(session, SkateSessionReason.None, now);
                value.Batch = batch; value.DeltaSeconds = value.HorizontalPath = value.VerticalPath = double.NaN;
                value.SegmentCount = value.BufferSize = -1; value.PositionGap = double.NaN; value.SupportKnown = false;
                try
                {
                    var cache = states.TickCache;
                    int count = cache.Infos[index].Count;
                    value.SegmentCount = count; value.BufferSize = cache.BufferSize;
                    if (!SkateMovementPolicy.ValidTickCount(count, cache.BufferSize))
                    { session.MovementReason = value.Reason = SkateSessionReason.InvalidTickCount; Disarm(session, value); continue; }
                    int offset = checked(index * cache.BufferSize);
                    double dt = states.TickDeltaTime[index];
                    value.DeltaSeconds = dt;
                    bool allowed;
                    SkateSessionReason movementReason;
                    if (!SkateMovementPolicy.TryAllow(dt, 0, settings.MaximumHorizontalSpeed, out allowed, out movementReason))
                    { session.MovementReason = value.Reason = movementReason; Disarm(session, value); continue; }
                    Vector3 previous = cache.Segments[offset].point;
                    if (!Coordinate(previous))
                    { session.MovementReason = value.Reason = SkateSessionReason.InvalidCoordinate; Disarm(session, value); continue; }
                    if (session.HasEnd) value.PositionGap = Math.Sqrt((previous - session.LastEnd).sqrMagnitude);
                    Vector3 start = previous;
                    double horizontal = 0, vertical = 0;
                    bool invalidCoordinate = false;
                    for (int s = 1; s <= count; s++)
                    {
                        Vector3 point = cache.Segments[offset + s].point;
                        double nextHorizontal, nextVertical;
                        if (!SkateMovementPolicy.TryAccumulate(new SkateMovementPoint(previous.x, previous.y, previous.z),
                            new SkateMovementPoint(point.x, point.y, point.z), horizontal, vertical,
                            out nextHorizontal, out nextVertical))
                        { invalidCoordinate = true; break; }
                        horizontal = nextHorizontal; vertical = nextVertical; previous = point;
                    }
                    value.HorizontalPath = horizontal; value.VerticalPath = vertical;
                    if (invalidCoordinate)
                    { session.MovementReason = value.Reason = SkateSessionReason.InvalidCoordinate; Disarm(session, value); continue; }
                    if (!SkateMovementPolicy.TryAllow(dt, horizontal, settings.MaximumHorizontalSpeed, out allowed, out movementReason))
                    { session.MovementReason = value.Reason = movementReason; Disarm(session, value); continue; }
                    session.MovementAllowed = allowed;
                    session.MovementSeconds = dt;
                    session.MovementVerticalPath = vertical;
                    session.MovementReason = movementReason;
                    if (!allowed) { value.Reason = movementReason; Disarm(session, value); continue; }
                    if (!session.Manual && !settings.AutomaticEligiblePlayers)
                    { value.Reason = SkateSessionReason.AutomaticDisabled; Disarm(session, value); continue; }
                    if (session.HasEnd && (start - session.LastEnd).sqrMagnitude > 0.0001f)
                    { value.Reason = SkateSessionReason.PositionDiscontinuity; Disarm(session, value); continue; }
                    previous = start;
                    bool supported = Supported(start);
                    value.FullySupported = supported; value.SupportKnown = true;
                    bool supportComplete = vertical <= 40 * dt;
                    for (int s = 1; supportComplete && s <= count; s++)
                    {
                        Vector3 point = cache.Segments[offset + s].point;
                        int samples = Math.Max(1, (int)Math.Ceiling((point - previous).magnitude / 0.1));
                        for (int sample = 1; sample <= samples; sample++)
                            supported = Supported(Vector3.Lerp(previous, point, (float)sample / samples)) && supported;
                        previous = point;
                    }
                    if (session.Bounds.Active)
                    {
                        session.Allowed = session.Bounds.Observe(now, dt, horizontal,
                            vertical, supported, settings.MaximumHorizontalSpeed, settings.MaximumAirtime);
                        double priorWarmup = value.RearmSeconds;
                        value = Context(session.Bounds.LastObservation, value);
                        value.SupportKnown = supportComplete;
                        value.RearmSeconds = priorWarmup;
                        if (!session.Allowed) { Disarm(session, value); continue; }
                    }
                    else
                    {
                        bool rearmed = session.Rearm.Observe(now, dt, horizontal, vertical,
                            supported, settings.MaximumHorizontalSpeed, settings.AutomaticRearmSeconds);
                        value = Context(session.Rearm.LastObservation, value);
                        value.SupportKnown = supportComplete;
                        value.AirSeconds = session.Bounds.AirSeconds;
                        if (session.Rearm.Seconds == 0) { Disarm(session, value); continue; }
                        session.Allowed = true;
                        if (rearmed)
                        {
                            double achievedWarmup = session.Rearm.Seconds;
                            session.Allowed = session.Bounds.Begin(now - dt, true) &&
                                session.Bounds.Observe(now, dt, horizontal, vertical, supported,
                                    settings.MaximumHorizontalSpeed, settings.MaximumAirtime);
                            value = Context(session.Bounds.LastObservation, value);
                            value.WasActive = false; value.RearmSeconds = achievedWarmup;
                            if (session.Allowed)
                            {
                                value.Reason = SkateSessionReason.AutomaticRearm;
                                if (session.History != null) session.History.Activate(value);
                                if (now - session.SaidAt > 2) { session.SaidAt = now; Puts("Skate diagnostics active for " + player.displayName + "."); }
                            }
                            session.Rearm.Reset();
                            if (!session.Allowed) { Disarm(session, value); continue; }
                        }
                    }
                    session.Observation = value;
                    if (session.History != null) session.History.Sample(value);
                    session.HasEnd = true;
                    session.LastEnd = cache.Segments[offset + count].point;
                    session.LastObserved = now;
                }
                catch (Exception)
                {
                    if (!session.MovementAllowed) session.MovementReason = SkateSessionReason.BatchException;
                    value.Reason = SkateSessionReason.BatchException;
                    value.SupportKnown = false; Disarm(session, value);
                }
            }
        }

        private static bool Coordinate(Vector3 value)
        {
            return SkateSessionBounds.Finite(value.x) && SkateSessionBounds.Finite(value.y) &&
                SkateSessionBounds.Finite(value.z) && Math.Abs(value.x) <= 100000 &&
                Math.Abs(value.y) <= 100000 && Math.Abs(value.z) <= 100000;
        }

        private void Filter(ref BasePlayer.PlayerServerStates.ReadOnly states,
                            NativeArray<int>.ReadOnly indices, NativeArray<bool> results, bool flying)
        {
            if (!ready || !validating) return;
            for (int n = 0; n < indices.Length; n++)
            {
                int index = indices[n];
                BasePlayer player = states.PlayerCache.Objects[index];
                Session session;
                if (player == null || !sessions.TryGetValue(player.userID, out session)) continue;
                if (!SkateMovementPolicy.CanFilter(ready, validating, session.Batch == batch,
                    player.ActivePlayerInd == index && ReferenceEquals(player, session.Player), session.MovementAllowed,
                    Eligible(player), Permitted(player), OptedOut(player))) continue;
                if (flying && !SkateMovementPolicy.CanExemptFly(FlyPermitted(player),
                    session.MovementSeconds, session.MovementVerticalPath)) continue;
                results[n] = false;
            }
        }

        // The result arrays are consumed before penalties and position rejection.
        // Patch the completed batch, preserving every other anti-hack result.
        [AutoPatch]
        [HarmonyPatch(typeof(AntiHack), "ValidateMoves")]
        private static class MovePatch
        {
            private static void Prefix(ref BasePlayer.PlayerServerStates.ReadOnly __0, NativeArray<int>.ReadOnly __1)
            {
                if (instance == null) return;
                try { instance.BeginBatch(ref __0, __1); }
                catch (Exception) { instance.validating = false; instance.ready = false; instance.unavailableReason = SkateSessionReason.MovePatchException; }
            }
            private static Exception Finalizer(Exception __exception)
            { if (instance != null) instance.validating = false; return __exception; }
        }

        [AutoPatch]
        [HarmonyPatch(typeof(AntiHack), "AreSpeeding")]
        private static class SpeedPatch
        {
            private static void Postfix(ref BasePlayer.PlayerServerStates.ReadOnly __0,
                                        NativeArray<int>.ReadOnly __2, NativeArray<bool> __3)
            {
                if (instance == null) return;
                try { instance.Filter(ref __0, __2, __3, false); }
                catch (Exception) { instance.validating = false; instance.ready = false; instance.unavailableReason = SkateSessionReason.SpeedPatchException; }
            }
        }

        [AutoPatch]
        [HarmonyPatch(typeof(AntiHack), "AreFlying")]
        private static class FlyPatch
        {
            private static void Postfix(ref BasePlayer.PlayerServerStates.ReadOnly __0,
                                        NativeArray<int>.ReadOnly __3, NativeArray<bool> __4)
            {
                if (instance == null) return;
                try { instance.Filter(ref __0, __3, __4, true); }
                catch (Exception) { instance.validating = false; instance.ready = false; instance.unavailableReason = SkateSessionReason.FlyPatchException; }
            }
        }
    }
}
