// Reference: Unity.Collections
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
        BatchExhausted, BeginRejected, StallPatchException, Count
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
        public long NativeStallCorrections { get; private set; }
        public long NativeStallExemptions { get; private set; }

        public void RecordNativeStall(bool exempted)
        {
            if (NativeStallCorrections < long.MaxValue) NativeStallCorrections++;
            if (exempted && NativeStallExemptions < long.MaxValue) NativeStallExemptions++;
        }

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
            text.Append("Native stall would-correct=").Append(history.NativeStallCorrections.ToString(CultureInfo.InvariantCulture))
                .Append("; exempted=").Append(history.NativeStallExemptions.ToString(CultureInfo.InvariantCulture)).AppendLine();
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
            if (order == null || order.Length < 1 || order.Length > SkateParkBuildItems.PieceCount) return false;
            for (int i = 0; i < order.Length; i++)
            {
                if (SkateParkLayout.Name(order[i]) == null || order[i] == SkateParkKind.HalfWallKicker) return false;
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
        { return ValidGestureName(name) && name.IndexOf(',') < 0 && name.IndexOf((char)0x2028) < 0 && name.IndexOf((char)0x2029) < 0; }
    }

    // BEGIN SHARED SKATE PARK
    public enum SkateParkKind { Bank, Kicker, QuarterPipe, LedgeLow, LedgeHigh, LongLedge, Funbox, Halfpipe,
        MiniQuarterPipe, LargeQuarterPipe, HalfWallKicker, MegaHalfpipe }
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

    public struct SkateParkQuarterProfile
    {
        public double Radius, Height, Entry, Deck, StartForward, HalfWidth, EndForward;
        public double Skin, Vert, CopingRadius, RailRadius, RailHeight;
    }

    public struct SkateParkBox
    {
        public double X, Y, Z, SizeX, SizeY, SizeZ;
    }

    public struct SkateParkSupportProbe
    {
        public SkateParkTransform Point, Cell;
        public int Right, Forward, Sample;
        public bool RequireCell;
    }

    public struct SkateParkSupportHit
    {
        public double Height;
        public ulong FoundationId;
    }

    public delegate bool SkateParkSupportReader(SkateParkSupportProbe probe,
        out SkateParkSupportHit hit, out string failure);

    public static class SkateParkLayout
    {
        public const string FloorPrefab = "assets/prefabs/building core/floor/floor.prefab";
        public const string LowWallPrefab = "assets/prefabs/building core/wall.low/wall.low.prefab";
        public const string HalfWallPrefab = "assets/prefabs/building core/wall.half/wall.half.prefab";
        private const double HalfLength = 1.5;
        private const double Degrees = Math.PI / 180;
        public const int KindCount = 12;
        public const double CellSize = 3;
        public const double NativeBoundsTolerance = 1e-3;
        public const string TwigFloorCollider = "assets/prefabs/building core/floor/floor.twig.prefab";
        public const double TwigFloorOverhang = 0.05486429;
        public const int QuarterArcSegments = 30;
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
                case SkateParkKind.MegaHalfpipe: return "mega-halfpipe";
                case SkateParkKind.MiniQuarterPipe: return "mini-quarterpipe";
                case SkateParkKind.LargeQuarterPipe: return "large-quarterpipe";
                case SkateParkKind.HalfWallKicker: return "half-wall-kicker";
                default: return null;
            }
        }
        public static bool TryKind(string name, out SkateParkKind kind)
        {
            kind = SkateParkKind.Bank;
            if (name == null) return false;
            for (int i = 0; i <= (int)SkateParkKind.MegaHalfpipe; i++)
                if (string.Equals(name, Name((SkateParkKind)i), StringComparison.OrdinalIgnoreCase))
                { kind = (SkateParkKind)i; return true; }
            return false;
        }
        public static int PartCount(SkateParkKind kind)
        {
            switch (kind)
            {
                case SkateParkKind.Bank: case SkateParkKind.Kicker: case SkateParkKind.HalfWallKicker:
                case SkateParkKind.LedgeLow: case SkateParkKind.LedgeHigh: return 1;
                case SkateParkKind.QuarterPipe: return 2;
                case SkateParkKind.LongLedge: case SkateParkKind.Funbox: return 3;
                case SkateParkKind.Halfpipe: return 36;
                case SkateParkKind.MegaHalfpipe: return SkateMegaHalfpipe.PartCount;
                case SkateParkKind.MiniQuarterPipe: return 2;
                case SkateParkKind.LargeQuarterPipe: return 3;
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
                case SkateParkKind.HalfWallKicker: part.Pitch = -30; break;
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
                case SkateParkKind.MegaHalfpipe: return SkateMegaHalfpipe.TryPart(index, out part);
                case SkateParkKind.MiniQuarterPipe:
                    if (index == 1)
                        part = new SkateParkPart { Prefab = LowWallPrefab, Forward = 1.15, Yaw = 90 };
                    break;
                case SkateParkKind.LargeQuarterPipe:
                    part.Right = index == 1 ? 3 : 0; part.Forward = index == 2 ? 3 : 0; break;
                default: return false;
            }
            if (part.Prefab == null) part.Prefab = FloorPrefab;
            return true;
        }

        public static int CellCount(SkateParkKind kind)
        { return kind == SkateParkKind.MiniQuarterPipe ? 1 : kind == SkateParkKind.QuarterPipe ? 2 :
            kind == SkateParkKind.LargeQuarterPipe ? 3 : 0; }

        public static bool TryCell(SkateParkKind kind, int index, out int right, out int forward)
        {
            right = forward = 0;
            if (index < 0 || index >= CellCount(kind)) return false;
            right = kind == SkateParkKind.LargeQuarterPipe && index == 1 ? 1 : 0;
            forward = (kind == SkateParkKind.QuarterPipe && index == 1) || index == 2 ? 1 : 0;
            return true;
        }

        public static bool TryQuarterProfile(SkateParkKind kind, out SkateParkQuarterProfile profile)
        {
            profile = new SkateParkQuarterProfile();
            if (CellCount(kind) == 0) return false;
            bool mini = kind == SkateParkKind.MiniQuarterPipe;
            profile = new SkateParkQuarterProfile { Radius = mini ? 1.2 : 3, Height = mini ? 1.2 : 3,
                Entry = 0.2, Deck = mini ? 1.4 : 2.6, StartForward = -1.4, HalfWidth = 1.4,
                EndForward = mini ? 1.4 : 4.4, Skin = 0.08, Vert = 0, CopingRadius = 0.05,
                RailRadius = 0.05, RailHeight = 0 };
            return true;
        }

        public static int QuarterPointCount(SkateParkKind kind)
        { return CellCount(kind) == 0 ? 0 : QuarterArcSegments + 3; }

        public static bool TryQuarterPoint(SkateParkKind kind, int index, out double forward, out double height)
        {
            forward = height = 0;
            SkateParkQuarterProfile profile;
            if (!TryQuarterProfile(kind, out profile) || index < 0 || index >= QuarterPointCount(kind)) return false;
            if (index == 0) { forward = profile.StartForward; return true; }
            double foot = profile.StartForward + profile.Entry;
            if (index <= QuarterArcSegments + 1)
            {
                double angle = (index - 1) * Math.PI / (2 * QuarterArcSegments);
                forward = foot + profile.Radius * Math.Sin(angle);
                height = profile.Radius * (1 - Math.Cos(angle));
                if (index == QuarterArcSegments + 1) { forward = foot + profile.Radius; height = profile.Radius; }
                return true;
            }
            forward = profile.EndForward;
            height = profile.Height;
            return true;
        }

        public static int QuarterShellPointCount(SkateParkKind kind)
        { int count = QuarterPointCount(kind); return count == 0 ? 0 : count + 2; }

        public static bool TryQuarterShellPoint(SkateParkKind kind, int index, out double forward, out double height)
        {
            forward = height = 0;
            int count = QuarterPointCount(kind);
            if (count == 0 || index < 0 || index >= count + 2) return false;
            if (index < count) return TryQuarterPoint(kind, index, out forward, out height);
            SkateParkQuarterProfile profile;
            TryQuarterProfile(kind, out profile);
            forward = index == count ? profile.EndForward : profile.StartForward;
            height = -profile.Skin;
            return true;
        }

        public static bool TryQuarterSurface(SkateParkKind kind, double right, double forward,
            out double height, out double normalUp, out double normalForward)
        {
            height = normalUp = normalForward = 0;
            SkateParkQuarterProfile profile;
            if (!TryQuarterProfile(kind, out profile) || !Finite(right) || !Finite(forward)) return false;
            if (kind == SkateParkKind.LargeQuarterPipe && right > profile.HalfWidth && right <= 4.4 &&
                forward >= -1.4 && forward <= 1.4)
            { normalUp = 1; return true; }
            if (Math.Abs(right) > profile.HalfWidth || forward < profile.StartForward || forward > profile.EndForward)
                return false;
            double foot = profile.StartForward + profile.Entry;
            double lip = foot + profile.Radius;
            double run = forward - foot;
            normalUp = 1;
            if (run <= 0) return true;
            if (forward > lip) { height = profile.Height; return true; }
            if (forward == lip) { height = profile.Radius; normalUp = 0; normalForward = -1; return true; }
            double vertical = Math.Sqrt(Math.Max(0, profile.Radius * profile.Radius - run * run));
            height = profile.Radius - vertical;
            normalUp = vertical / profile.Radius; normalForward = -run / profile.Radius;
            return true;
        }

        public static double Float32BoundsTolerance(double worldX, double worldY, double worldZ)
        {
            if (!Finite(worldX) || !Finite(worldY) || !Finite(worldZ)) return double.NaN;
            return Math.Max(NativeBoundsTolerance,
                Math.Max(Math.Abs(worldX), Math.Max(Math.Abs(worldY), Math.Abs(worldZ))) / 4194304);
        }

        public static bool MatchesMeasuredTwigBounds(string colliderName, double minRight, double minUp, double minForward,
            double maxRight, double maxUp, double maxForward)
        {
            return string.Equals(colliderName, TwigFloorCollider, StringComparison.Ordinal) &&
                Finite(minRight) && Finite(minUp) && Finite(minForward) && Finite(maxRight) && Finite(maxUp) && Finite(maxForward) &&
                Math.Abs(minRight - -1.541687) <= NativeBoundsTolerance &&
                Math.Abs(minUp - -0.00000024586916) <= NativeBoundsTolerance &&
                Math.Abs(minForward - -1.55486429) <= NativeBoundsTolerance &&
                Math.Abs(maxRight - 1.54439974) <= NativeBoundsTolerance &&
                Math.Abs(maxUp - 0.100000769) <= NativeBoundsTolerance &&
                Math.Abs(maxForward - 1.54002917) <= NativeBoundsTolerance;
        }

        public static bool MatchesMeasuredTwigCollider(string colliderName, SkateParkBox local, SkateParkBox block)
        {
            return MatchesMeasuredTwigBounds(colliderName,
                local.X - local.SizeX / 2, local.Y - local.SizeY / 2, local.Z - local.SizeZ / 2,
                local.X + local.SizeX / 2, local.Y + local.SizeY / 2, local.Z + local.SizeZ / 2) &&
                MatchesMeasuredTwigBounds(colliderName,
                block.X - block.SizeX / 2, block.Y - block.SizeY / 2, block.Z - block.SizeZ / 2,
                block.X + block.SizeX / 2, block.Y + block.SizeY / 2, block.Z + block.SizeZ / 2);
        }

        public static bool ContainsQuarterPoint(SkateParkKind kind, double right, double up, double forward,
            double minimumUp, double maximumUp, double tolerance = NativeBoundsTolerance)
        {
            return ContainsQuarterColliderPoint(kind, right, up, forward, minimumUp, maximumUp, tolerance, false);
        }

        public static bool ContainsQuarterColliderPoint(SkateParkKind kind, double right, double up, double forward,
            double minimumUp, double maximumUp, double tolerance, bool measuredTwig)
        {
            double horizontalTolerance = tolerance + (measuredTwig ? TwigFloorOverhang : 0);
            if (CellCount(kind) == 0 || !Finite(right) || !Finite(up) || !Finite(forward) ||
                !Finite(minimumUp) || !Finite(maximumUp) || minimumUp > maximumUp ||
                !Finite(tolerance) || tolerance < 0 || tolerance > 0.1 || horizontalTolerance > 0.1 ||
                up < minimumUp - tolerance || up > maximumUp + tolerance) return false;
            for (int i = 0; i < CellCount(kind); i++)
            {
                int cellRight, cellForward;
                TryCell(kind, i, out cellRight, out cellForward);
                if (Math.Abs(right - CellSize * cellRight) <= CellSize / 2 + horizontalTolerance &&
                    Math.Abs(forward - CellSize * cellForward) <= CellSize / 2 + horizontalTolerance) return true;
            }
            return false;
        }

        public static bool ContainsNativeBounds(SkateParkKind kind, int index, double centerRight,
            double centerForward, double sizeRight, double sizeForward)
        {
            SkateParkPart part;
            if (CellCount(kind) == 0 || !TryPart(kind, index, out part) || !Finite(centerRight) ||
                !Finite(centerForward) || !Finite(sizeRight) || !Finite(sizeForward) || sizeRight <= 0 || sizeForward <= 0)
                return false;
            int cellRight = kind == SkateParkKind.LargeQuarterPipe && index == 1 ? 1 : 0;
            int cellForward = kind == SkateParkKind.LargeQuarterPipe && index == 2 ? 1 : 0;
            double sin = Math.Sin(part.Yaw * Degrees), cos = Math.Cos(part.Yaw * Degrees);
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                double localX = centerRight + x * sizeRight / 2, localZ = centerForward + z * sizeForward / 2;
                double r = part.Right + localX * cos + localZ * sin - CellSize * cellRight;
                double f = part.Forward - localX * sin + localZ * cos - CellSize * cellForward;
                if (kind == SkateParkKind.QuarterPipe)
                {
                    if (!ContainsQuarterPoint(kind, r, 0, f, 0, 0)) return false;
                }
                else if (Math.Abs(r) > 1.5 + NativeBoundsTolerance || Math.Abs(f) > 1.5 + NativeBoundsTolerance) return false;
            }
            return true;
        }

        public static bool BlocksMegaSpace(object entity, object placingPlayer, object anchor, bool transactionPart)
        {
            return entity == null || (!ReferenceEquals(entity, placingPlayer) &&
                !ReferenceEquals(entity, anchor) && !transactionPart);
        }

        // Strict overlap of yaw-only boxes; touching cell edges are allowed. Inputs are world metres.
        public static bool OverlapsBox(double x, double z, double yaw, double halfRight, double halfForward,
            double otherX, double otherZ, double otherYaw, double otherHalfRight, double otherHalfForward)
        {
            return OverlapsBoxBeyondContact(x, z, yaw, halfRight, halfForward,
                otherX, otherZ, otherYaw, otherHalfRight, otherHalfForward, 0);
        }

        public static bool OverlapsBoxBeyondContact(double x, double z, double yaw, double halfRight, double halfForward,
            double otherX, double otherZ, double otherYaw, double otherHalfRight, double otherHalfForward, double contactTolerance)
        {
            if (!Finite(contactTolerance) || contactTolerance < 0 || contactTolerance > NativeBoundsTolerance ||
                !Coordinate(x) || !Coordinate(z) || !Angle(yaw) || !Coordinate(otherX) || !Coordinate(otherZ) ||
                !Angle(otherYaw) || !Finite(halfRight) || !Finite(halfForward) || !Finite(otherHalfRight) ||
                !Finite(otherHalfForward) || halfRight <= 0 || halfForward <= 0 || otherHalfRight <= 0 || otherHalfForward <= 0)
                return true;
            double a = yaw * Degrees, b = otherYaw * Degrees;
            double ax = Math.Cos(a), az = -Math.Sin(a), bx = Math.Cos(b), bz = -Math.Sin(b);
            double dx = otherX - x, dz = otherZ - z;
            double c = Math.Abs(ax * bx + az * bz), s = Math.Abs(ax * -bz + az * bx);
            return Math.Abs(dx * ax + dz * az) < halfRight + otherHalfRight * c + otherHalfForward * s - 1e-7 - contactTolerance &&
                Math.Abs(dx * -az + dz * ax) < halfForward + otherHalfRight * s + otherHalfForward * c - 1e-7 - contactTolerance &&
                Math.Abs(dx * bx + dz * bz) < otherHalfRight + halfRight * c + halfForward * s - 1e-7 - contactTolerance &&
                Math.Abs(dx * -bz + dz * bx) < otherHalfForward + halfRight * s + halfForward * c - 1e-7 - contactTolerance;
        }
        public static bool SharesNominalEdge(SkateParkTransform pose, SkateParkBox bounds,
            SkateParkTransform otherPose, SkateParkBox otherBounds)
        {
            double a = pose.Yaw * Degrees, b = otherPose.Yaw * Degrees;
            return SharesBoxEdge(pose.X + bounds.X * Math.Cos(a) + bounds.Z * Math.Sin(a),
                pose.Z - bounds.X * Math.Sin(a) + bounds.Z * Math.Cos(a), pose.Yaw, bounds.SizeX / 2, bounds.SizeZ / 2,
                otherPose.X + otherBounds.X * Math.Cos(b) + otherBounds.Z * Math.Sin(b),
                otherPose.Z - otherBounds.X * Math.Sin(b) + otherBounds.Z * Math.Cos(b), otherPose.Yaw,
                otherBounds.SizeX / 2, otherBounds.SizeZ / 2);
        }

        // A neighboring floor must share an edge, not merely have disjoint nominal bounds.
        public static bool SharesBoxEdge(double x, double z, double yaw, double halfRight, double halfForward,
            double otherX, double otherZ, double otherYaw, double otherHalfRight, double otherHalfForward)
        {
            if (!Coordinate(x) || !Coordinate(z) || !Angle(yaw) || !Coordinate(otherX) || !Coordinate(otherZ) ||
                !Angle(otherYaw) || !Finite(halfRight) || !Finite(halfForward) || !Finite(otherHalfRight) ||
                !Finite(otherHalfForward) || halfRight <= 0 || halfForward <= 0 || otherHalfRight <= 0 || otherHalfForward <= 0)
                return false;
            double a = yaw * Degrees, relative = (otherYaw - yaw) * Degrees;
            double c = Math.Abs(Math.Cos(relative)), s = Math.Abs(Math.Sin(relative));
            if (c > 1e-6 && s > 1e-6) return false;
            double dx = otherX - x, dz = otherZ - z;
            double rightGap = Math.Abs(dx * Math.Cos(a) - dz * Math.Sin(a)) - halfRight - otherHalfRight * c - otherHalfForward * s;
            double forwardGap = Math.Abs(dx * Math.Sin(a) + dz * Math.Cos(a)) - halfForward - otherHalfRight * s - otherHalfForward * c;
            return (Math.Abs(rightGap) <= NativeBoundsTolerance && forwardGap < -NativeBoundsTolerance) ||
                (Math.Abs(forwardGap) <= NativeBoundsTolerance && rightGap < -NativeBoundsTolerance);
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
                part.Yaw = index < 18 ? 270 : 90;
                return;
            }
            part.Prefab = HalfWallPrefab;
            part.Right = index % 2 == 0 ? -1.85 : 4.85;
            part.Yaw = index % 2 == 0 ? 180 : 0;
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
            if (kind == SkateParkKind.HalfWallKicker || PartCount(kind) == 0 || !Coordinate(supportTop) || !Coordinate(floorCenterY) ||
                !Coordinate(floorCenterZ) || !Coordinate(floorSizeY) || !Coordinate(floorSizeZ) ||
                floorSizeY <= 0 || floorSizeZ <= 0) return false;
            double baseline = double.NegativeInfinity;
            for (int i = 0; i < PartCount(kind); i++)
            {
                SkateParkPart part;
                if (!TryPart(kind, i, out part)) return false;
                if (part.Prefab != FloorPrefab) continue;
                double cos = Math.Cos(part.Pitch * Degrees), sin = Math.Sin(part.Pitch * Degrees);
                double bottom = part.Height + floorCenterY * cos - floorCenterZ * sin -
                    floorSizeY / 2 * Math.Abs(cos) - floorSizeZ / 2 * Math.Abs(sin);
                baseline = Math.Max(baseline, supportTop - bottom);
            }
            if (!Coordinate(baseline)) return false;
            anchorY = baseline;
            return true;
        }

        public const double SupportTolerance = 0.02;

        public static bool TrySnapYaw(double requestedYaw, double referenceYaw, double step, out double yaw)
        {
            yaw = 0;
            if (!Angle(requestedYaw) || !Angle(referenceYaw) || !Finite(step) || step <= 0 || step > 360 || !Finite(360 / step) ||
                Math.Abs(360 / step - Math.Round(360 / step)) > 1e-6) return false;
            double turns = (requestedYaw - referenceYaw) / step;
            if (!Finite(turns)) return false;
            double snapped = referenceYaw + Math.Round(turns, MidpointRounding.AwayFromZero) * step;
            if (!Finite(snapped)) return false;
            yaw = Normalize(snapped);
            return true;
        }

        public static bool TryAdjacentCell(double referenceX, double referenceZ, double referenceYaw,
            double hitX, double hitZ, out double x, out double z)
        {
            x = z = 0;
            if (!Coordinate(referenceX) || !Coordinate(referenceZ) || !Angle(referenceYaw) ||
                !Coordinate(hitX) || !Coordinate(hitZ)) return false;
            double sin = Math.Sin(referenceYaw * Degrees), cos = Math.Cos(referenceYaw * Degrees);
            double right = (hitX - referenceX) * cos - (hitZ - referenceZ) * sin;
            double forward = (hitX - referenceX) * sin + (hitZ - referenceZ) * cos;
            if (Math.Abs(right) > CellSize / 2 + SupportTolerance ||
                Math.Abs(forward) > CellSize / 2 + SupportTolerance) return false;
            double r = Math.Abs(right) >= Math.Abs(forward) ? (right < 0 ? -CellSize : CellSize) : 0;
            double f = r == 0 ? (forward < 0 ? -CellSize : CellSize) : 0;
            double nextX = referenceX + r * cos + f * sin;
            double nextZ = referenceZ - r * sin + f * cos;
            if (!Coordinate(nextX) || !Coordinate(nextZ)) return false;
            x = nextX; z = nextZ;
            return true;
        }

        public static int SupportCellCount(SkateParkKind kind)
        {
            switch (kind)
            {
                case SkateParkKind.Bank: case SkateParkKind.Kicker: case SkateParkKind.LedgeLow:
                case SkateParkKind.LedgeHigh: case SkateParkKind.MiniQuarterPipe: return 1;
                case SkateParkKind.QuarterPipe: return 2;
                case SkateParkKind.LongLedge: case SkateParkKind.Funbox: case SkateParkKind.LargeQuarterPipe: return 3;
                case SkateParkKind.Halfpipe: return 32;
                case SkateParkKind.MegaHalfpipe: return 84;
                default: return 0;
            }
        }

        public static bool TrySupportCell(SkateParkKind kind, int index, out int right, out int forward)
        {
            right = forward = 0;
            if (index < 0 || index >= SupportCellCount(kind)) return false;
            switch (kind)
            {
                case SkateParkKind.QuarterPipe: forward = index; break;
                case SkateParkKind.LongLedge: right = index - 1; break;
                case SkateParkKind.Funbox: forward = index - 1; break;
                case SkateParkKind.LargeQuarterPipe: right = index == 1 ? 1 : 0; forward = index == 2 ? 1 : 0; break;
                case SkateParkKind.Halfpipe: right = index % 4 - 1; forward = index / 4 - 3; break;
                case SkateParkKind.MegaHalfpipe: right = index % 6 - 1; forward = index / 6 - 5; break;
            }
            return true;
        }

        public static bool TrySupportGridAnchor(SkateParkKind kind, double gridX, double gridZ, double yaw,
            out double x, out double z)
        {
            x = z = 0;
            if (SupportCellCount(kind) == 0 || !Coordinate(gridX) || !Coordinate(gridZ) || !Angle(yaw)) return false;
            double phase = kind == SkateParkKind.MegaHalfpipe ? -4.5 : 0;
            double px = gridX - phase * Math.Sin(yaw * Degrees), pz = gridZ - phase * Math.Cos(yaw * Degrees);
            if (!Coordinate(px) || !Coordinate(pz)) return false;
            x = px; z = pz;
            return true;
        }

        public static bool TrySupportCellPose(SkateParkKind kind, int index, double x, double z, double yaw,
            out SkateParkTransform cell)
        {
            cell = new SkateParkTransform();
            int right, forward;
            if (!TrySupportCell(kind, index, out right, out forward) || !Coordinate(x) || !Coordinate(z) || !Angle(yaw)) return false;
            double r = right * CellSize, f = forward * CellSize + (kind == SkateParkKind.MegaHalfpipe ? -4.5 : 0);
            double px = x + r * Math.Cos(yaw * Degrees) + f * Math.Sin(yaw * Degrees);
            double pz = z - r * Math.Sin(yaw * Degrees) + f * Math.Cos(yaw * Degrees);
            if (!Coordinate(px) || !Coordinate(pz)) return false;
            cell = new SkateParkTransform { X = px, Z = pz, Yaw = Normalize(yaw) };
            return true;
        }

        public static bool FitsSupportCell(SkateParkTransform cell, double foundationX, double foundationZ, double foundationYaw)
        {
            double snapped;
            if (!Coordinate(cell.X) || !Coordinate(cell.Z) || !Angle(cell.Yaw) || !Coordinate(foundationX) ||
                !Coordinate(foundationZ) || !TrySnapYaw(foundationYaw, cell.Yaw, 90, out snapped)) return false;
            return Math.Abs(cell.X - foundationX) <= SupportTolerance && Math.Abs(cell.Z - foundationZ) <= SupportTolerance &&
                Math.Abs(Normalize(foundationYaw - snapped + 180) - 180) <= 0.01;
        }

        public static bool TrySupportPlane(IReadOnlyList<double> heights, out double top)
        {
            top = 0;
            if (heights == null || heights.Count == 0) return false;
            double low = double.PositiveInfinity, high = double.NegativeInfinity;
            foreach (double height in heights)
            {
                if (!Coordinate(height)) return false;
                low = Math.Min(low, height); high = Math.Max(high, height);
            }
            if (high - low > SupportTolerance) return false;
            top = high;
            return true;
        }

        // How a kind is turned when it is placed, on top of the player's own turn: the halfpipe's
        // 21 m run goes across the view instead of back over the player who places it.
        public static double PlacementYaw(SkateParkKind kind)
        { return kind == SkateParkKind.Halfpipe ? 90 : 0; }
        public static bool TryGhostFrame(SkateParkKind kind, bool adjacent, double hostX, double hostZ,
            double hostYaw, double hitX, double hitZ, double requestedYaw, bool relativeYaw, double step,
            out SkateParkTransform frame)
        {
            frame = new SkateParkTransform();
            double x = hostX, z = hostZ, yaw;
            if (adjacent && !TryAdjacentCell(hostX, hostZ, hostYaw, hitX, hitZ, out x, out z)) return false;
            if (!TrySnapYaw((relativeYaw ? hostYaw + requestedYaw : requestedYaw) + PlacementYaw(kind), hostYaw, step, out yaw) ||
                !TrySupportGridAnchor(kind, x, z, yaw, out x, out z)) return false;
            frame = new SkateParkTransform { X = x, Z = z, Yaw = yaw };
            return true;
        }

        public static bool TryGhostAnchor(SkateParkKind kind, SkateParkBox floor, SkateParkTransform hint,
            double? selectedTop, SkateParkSupportReader read, out SkateParkTransform anchor,
            out double supportTop, out string failure)
        {
            anchor = new SkateParkTransform(); supportTop = 0;
            failure = "No valid support: measured floor bounds and a complete support query are required.";
            if (!Box(floor) || SupportCellCount(kind) == 0 || !Coordinate(hint.X) || !Coordinate(hint.Y) ||
                !Coordinate(hint.Z) || !Angle(hint.Yaw) || !Coordinate(hint.Y + 2) || read == null ||
                (selectedTop.HasValue && !Coordinate(selectedTop.Value))) return false;
            var foundations = new List<ulong>();
            double low = double.PositiveInfinity, high = double.NegativeInfinity;
            for (int i = 0; i < SupportCellCount(kind); i++)
            {
                int right, forward;
                SkateParkTransform cell;
                if (!TrySupportCell(kind, i, out right, out forward) ||
                    !TrySupportCellPose(kind, i, hint.X, hint.Z, hint.Yaw, out cell))
                { failure = "A required support cell is outside world bounds."; return false; }
                ulong first = 0;
                for (int sample = 0; sample < 9; sample++)
                {
                    double r = (sample % 3 - 1) * 1.49, f = (sample / 3 - 1) * 1.49;
                    double angle = cell.Yaw * Degrees;
                    var probe = new SkateParkSupportProbe { RequireCell = true, Cell = cell,
                        Right = right, Forward = forward, Sample = sample,
                        Point = new SkateParkTransform { X = cell.X + r * Math.Cos(angle) + f * Math.Sin(angle),
                            Y = hint.Y + 2, Z = cell.Z - r * Math.Sin(angle) + f * Math.Cos(angle) } };
                    SkateParkSupportHit hit;
                    if (!ReadGhostSupport(probe, selectedTop, read, ref low, ref high, out hit, out failure)) return false;
                    if (sample != 0 && first != hit.FoundationId)
                    { failure = SupportLabel(probe) + ": the cell must have its own single foundation or continuous dry terrain."; return false; }
                    first = hit.FoundationId;
                    if (first != 0 && !foundations.Contains(first)) foundations.Add(first);
                }
            }
            for (int i = 0; i < PartCount(kind); i++)
            for (int sample = 0; sample < 9; sample++)
            {
                SkateParkTransform point, pose;
                if (!TrySupportSample(kind, i, sample, floor, hint.X, hint.Z, hint.Yaw, out point)) continue;
                if (!TryTransform(kind, i, hint.X, 0, hint.Z, hint.Yaw, out pose))
                { failure = "A native contact is outside world bounds."; return false; }
                double angle = pose.Yaw * Degrees, frame = hint.Yaw * Degrees;
                double lateral = (point.X - pose.X) * Math.Cos(angle) - (point.Z - pose.Z) * Math.Sin(angle);
                double longitudinal = (point.X - pose.X) * Math.Sin(angle) + (point.Z - pose.Z) * Math.Cos(angle);
                double insetRight = Math.Sign(lateral) * Math.Min(Math.Abs(lateral), 0.01);
                double insetForward = Math.Abs(pose.Pitch) < 0.001 ? Math.Sign(longitudinal) * Math.Min(Math.Abs(longitudinal), 0.01) : 0;
                var probe = new SkateParkSupportProbe { Sample = sample,
                    Right = (int)Math.Floor(((point.X - hint.X) * Math.Cos(frame) -
                        (point.Z - hint.Z) * Math.Sin(frame) + 1.5) / 3),
                    Forward = (int)Math.Floor(((point.X - hint.X) * Math.Sin(frame) +
                        (point.Z - hint.Z) * Math.Cos(frame) + (kind == SkateParkKind.MegaHalfpipe ? 4.5 : 0) + 1.5) / 3),
                    Point = new SkateParkTransform { X = point.X - insetRight * Math.Cos(angle) - insetForward * Math.Sin(angle),
                        Y = hint.Y + 2, Z = point.Z + insetRight * Math.Sin(angle) - insetForward * Math.Cos(angle) } };
                SkateParkSupportHit hit;
                if (!ReadGhostSupport(probe, selectedTop, read, ref low, ref high, out hit, out failure)) return false;
                if (hit.FoundationId != 0 && !foundations.Contains(hit.FoundationId))
                { failure = SupportLabel(probe) + ": native contact is outside its verified foundation cells."; return false; }
            }
            double y;
            if ((selectedTop.HasValue && Math.Abs(high - selectedTop.Value) > SupportTolerance) ||
                !TrySupportedAnchor(kind, high, floor.Y, floor.Z, floor.SizeY, floor.SizeZ, out y))
            { failure = "The supported anchor is outside world bounds."; return false; }
            anchor = new SkateParkTransform { X = hint.X, Y = y, Z = hint.Z, Yaw = Normalize(hint.Yaw) };
            supportTop = high; failure = null;
            return true;
        }

        private static bool ReadGhostSupport(SkateParkSupportProbe probe, double? selectedTop,
            SkateParkSupportReader read, ref double low, ref double high, out SkateParkSupportHit hit, out string failure)
        {
            hit = new SkateParkSupportHit();
            failure = SupportLabel(probe) + ": missing, blocked or unknown support.";
            if (!Coordinate(probe.Point.X) || !Coordinate(probe.Point.Y) || !Coordinate(probe.Point.Z)) return false;
            string reason;
            if (!read(probe, out hit, out reason))
            { if (!string.IsNullOrEmpty(reason)) failure = reason; return false; }
            if (!Coordinate(hit.Height)) return false;
            low = Math.Min(low, hit.Height); high = Math.Max(high, hit.Height);
            if (high - low > SupportTolerance || (probe.RequireCell && selectedTop.HasValue && Math.Abs(hit.Height - selectedTop.Value) > SupportTolerance))
            { failure = SupportLabel(probe) + ": uneven support; all cells must share the selected support height within 0.02 m."; return false; }
            failure = null;
            return true;
        }

        public static string SupportLabel(SkateParkSupportProbe probe)
        { return "Park cell (" + probe.Right + "," + probe.Forward + ")"; }

        public static bool TrySupportSample(SkateParkKind kind, int index, int sample, SkateParkBox floor,
            double x, double z, double yaw, out SkateParkTransform point)
        {
            point = new SkateParkTransform();
            double baseline;
            SkateParkPart part;
            SkateParkTransform pose;
            if (!Box(floor) || sample < 0 || sample >= 9 || !TryPart(kind, index, out part) ||
                part.Prefab != FloorPrefab || !TrySupportedAnchor(kind, 0, floor.Y, floor.Z,
                    floor.SizeY, floor.SizeZ, out baseline) ||
                !TryTransform(kind, index, x, 0, z, yaw, out pose)) return false;
            double pitch = part.Pitch * Degrees, heading = pose.Yaw * Degrees;
            double right = floor.X + (sample % 3 - 1) * floor.SizeX / 2;
            double forward = floor.Z + (sample / 3 - 1) * floor.SizeZ / 2;
            double bottom = floor.Y - floor.SizeY / 2;
            double height = part.Height + bottom * Math.Cos(pitch) - forward * Math.Sin(pitch);
            if (Math.Abs(height + baseline) > 1e-6) return false;
            double run = bottom * Math.Sin(pitch) + forward * Math.Cos(pitch);
            double px = pose.X + right * Math.Cos(heading) + run * Math.Sin(heading);
            double pz = pose.Z - right * Math.Sin(heading) + run * Math.Cos(heading);
            if (!Coordinate(px) || !Coordinate(pz)) return false;
            point = new SkateParkTransform { X = px, Y = height, Z = pz };
            return true;
        }

        public static bool TryHalfWallAnchor(SkateParkBlock wall, SkateParkBox wallBounds,
            SkateParkBox floorBounds, int side, out SkateParkTransform anchor)
        {
            anchor = new SkateParkTransform();
            if (wall.Prefab != HalfWallPrefab || !Valid(wall) || !CloseAngle(wall.Pitch, 0, 0.01) ||
                (side != -1 && side != 1) || !Box(wallBounds) || !Box(floorBounds) ||
                wallBounds.SizeX > 0.6 || wallBounds.SizeY < 1.4 || wallBounds.SizeY > 1.6 ||
                wallBounds.SizeZ < 2.9 || wallBounds.SizeZ > 3.1 || Math.Abs(wallBounds.X) > 0.05 ||
                Math.Abs(wallBounds.Z) > 0.05 || Math.Abs(wallBounds.Y - wallBounds.SizeY / 2) > 0.05 ||
                floorBounds.SizeX < 2.9 || floorBounds.SizeX > wallBounds.SizeZ + 0.002 ||
                floorBounds.SizeZ < 2.9 || floorBounds.SizeZ > 3.1 || floorBounds.SizeY > 1 ||
                Math.Abs(floorBounds.X) > 0.05 || Math.Abs(floorBounds.Z) > 0.05 || Math.Abs(floorBounds.Y) > 0.5)
                return false;
            double yaw = Normalize(wall.Yaw + (side == 1 ? 270 : 90));
            double sin = Math.Sin(wall.Yaw * Degrees), cos = Math.Cos(wall.Yaw * Degrees);
            double top = floorBounds.Y + floorBounds.SizeY / 2;
            double end = floorBounds.Z + floorBounds.SizeZ / 2;
            double rise = top * Math.Cos(30 * Degrees) + end * Math.Sin(30 * Degrees);
            double run = -top * Math.Sin(30 * Degrees) + end * Math.Cos(30 * Degrees);
            double wallRight = wallBounds.X + side * wallBounds.SizeX / 2;
            double x = wall.X + wallRight * cos + wallBounds.Z * sin - run * Math.Sin(yaw * Degrees) - floorBounds.X * Math.Cos(yaw * Degrees);
            double y = wall.Y + wallBounds.Y + wallBounds.SizeY / 2 - rise;
            double z = wall.Z - wallRight * sin + wallBounds.Z * cos - run * Math.Cos(yaw * Degrees) + floorBounds.X * Math.Sin(yaw * Degrees);
            if (!Coordinate(x) || !Coordinate(y) || !Coordinate(z)) return false;
            anchor = new SkateParkTransform { X = x, Y = y, Z = z, Yaw = yaw, Pitch = -30 };
            return true;
        }

        private static bool Box(SkateParkBox box)
        {
            return Coordinate(box.X) && Coordinate(box.Y) && Coordinate(box.Z) &&
                Finite(box.SizeX) && Finite(box.SizeY) && Finite(box.SizeZ) &&
                box.SizeX > 0 && box.SizeY > 0 && box.SizeZ > 0;
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
            if (count == 0 || count > SkateMegaHalfpipe.PartCount || blocks == null || blocks.Count != count || !Finite(tolerance) ||
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
                case SkateParkKind.Bank: case SkateParkKind.Kicker:
                    forward = -HalfLength * Math.Cos(first.Pitch * Degrees); break;
                case SkateParkKind.Halfpipe: right = forward = HalfLength; break;
                case SkateParkKind.MegaHalfpipe: right = SkateMegaHalfpipe.CenterRight; break;
            }
            return true;
        }

        private static bool Match(SkateParkKind kind, IReadOnlyList<SkateParkBlock> blocks,
            double tolerance, double anchorX, double anchorY, double anchorZ, double anchorYaw)
        {
            int count = PartCount(kind);
            var used = new bool[count];
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
                if (match < 0 || used[match]) return false;
                used[match] = true;
            }
            return true;
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

    public static class SkateHalfpipe
    {
        public const int ProfilePointCount = 40;
        public const double Lift = 0.02;
        private const double HalfLength = 1.5, Degrees = Math.PI / 180, Bend = 3;
        private const int BendSteps = 8;

        private struct ProfilePoint
        {
            public readonly float Forward, Height;
            public ProfilePoint(float forward, float height) { Forward = forward; Height = height; }
        }

        private static readonly ProfilePoint[] profile = BuildProfile();

        public static bool TryProfilePoint(int index, out double forward, out double height)
        {
            forward = height = 0;
            if (index < 0 || index >= ProfilePointCount) return false;
            forward = profile[index].Forward;
            height = profile[index].Height;
            return true;
        }

        private static ProfilePoint[] BuildProfile()
        {
            double run1 = 2 * HalfLength * Math.Cos(15 * Degrees), rise1 = 2 * HalfLength * Math.Sin(15 * Degrees);
            double run2 = 2 * HalfLength * Math.Cos(35 * Degrees), rise2 = 2 * HalfLength * Math.Sin(35 * Degrees);
            double half = 2 * HalfLength, deck = 2 * HalfLength, top = rise1 + rise2;
            var corners = new List<ProfilePoint>(8);
            var radii = new List<double>(8);
            foreach (int side in new[] { -1, 1 })
            {
                // Preserve the client's Vector2 float rounding before double bend arithmetic.
                var points = new[] {
                    new ProfilePoint((float)(side * (half + run1 + run2 + deck)), (float)top),
                    new ProfilePoint((float)(side * (half + run1 + run2)), (float)top),
                    new ProfilePoint((float)(side * (half + run1)), (float)rise1),
                    new ProfilePoint((float)(side * half), 0f) };
                var bends = new[] { 0.0, 0.0, Bend, Bend };
                if (side < 0)
                    for (int i = 0; i < 4; i++) { corners.Add(points[i]); radii.Add(bends[i]); }
                else
                    for (int i = 3; i >= 0; i--) { corners.Add(points[i]); radii.Add(bends[i]); }
            }
            var result = new List<ProfilePoint>(ProfilePointCount);
            for (int i = 0; i < corners.Count; i++)
            {
                if (i == 0 || i == corners.Count - 1 || radii[i] <= 0) { result.Add(corners[i]); continue; }
                double ax = corners[i - 1].Forward, ay = corners[i - 1].Height;
                double bx = corners[i].Forward, by = corners[i].Height;
                double cx = corners[i + 1].Forward, cy = corners[i + 1].Height;
                double l1 = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                double l2 = Math.Sqrt((cx - bx) * (cx - bx) + (cy - by) * (cy - by));
                double d1x = (bx - ax) / l1, d1y = (by - ay) / l1, d2x = (cx - bx) / l2, d2y = (cy - by) / l2;
                double turn = Math.Acos(Math.Max(-1.0, Math.Min(1.0, d1x * d2x + d1y * d2y)));
                if (turn < 0.001) { result.Add(corners[i]); continue; }
                double t = Math.Min(radii[i] * Math.Tan(turn / 2), Math.Min(l1, l2) * 0.45);
                double r = t / Math.Tan(turn / 2);
                double sx = bx - d1x * t, sy = by - d1y * t;
                double cross = d1x * d2y - d1y * d2x, sign = cross > 0 ? 1.0 : -1.0;
                double ox = sx - d1y * r * sign, oy = sy + d1x * r * sign;
                double fx = sx - ox, fy = sy - oy;
                int steps = Math.Max(BendSteps, (int)Math.Ceiling(turn * 180.0 / Math.PI / 4.0));
                for (int k = 0; k <= steps; k++)
                {
                    double angle = turn * k / steps * sign;
                    double cosine = Math.Cos(angle), sine = Math.Sin(angle);
                    result.Add(new ProfilePoint((float)(ox + fx * cosine - fy * sine), (float)(oy + fx * sine + fy * cosine)));
                }
            }
            if (result.Count != ProfilePointCount)
                throw new InvalidOperationException("Regular halfpipe profile point count changed.");
            return result.ToArray();
        }
    }

    public static class SkateMegaHalfpipe
    {
        public const int PartCount = 80, Lanes = 4, TransitionSegments = 5, CurveSteps = 60;
        public const int ProfilePointCount = 2 * CurveSteps + 4;
        public const double Tile = 3, Width = 12, FlatLength = 12, DeckLength = 3;
        public const double CenterRight = 4.5, RideLift = 0.05, CopingRadius = 0.05;
        public const double EnvelopeMargin = 0.65, AirClearance = 3;
        public static readonly double Radius = Tile / (2 * Math.Sin(Math.PI / 20));
        public static double LipForward { get { return FlatLength / 2 + Radius; } }
        public static double Length { get { return 2 * (LipForward + DeckLength); } }

        public static bool TryPart(int index, out SkateParkPart part)
        {
            part = new SkateParkPart();
            if (index < 0 || index >= PartCount) return false;
            part.Prefab = SkateParkLayout.FloorPrefab;
            part.Material = SkateParkMaterial.Metal;
            part.Right = index % Lanes * Tile;
            if (index < 16)
            {
                part.Forward = -4.5 + index / Lanes * Tile;
                part.Material = SkateParkMaterial.Stone;
            }
            else if (index < 56)
            {
                int segment = (index - 16) % 20 / Lanes;
                int side = index < 36 ? -1 : 1;
                double a = segment * Math.PI / 10, b = (segment + 1) * Math.PI / 10;
                part.Forward = side * (FlatLength / 2 + Radius * (Math.Sin(a) + Math.Sin(b)) / 2);
                part.Height = Radius * (1 - (Math.Cos(a) + Math.Cos(b)) / 2);
                part.Pitch = -side * (segment * 18 + 9);
            }
            else
            {
                int side = (index / Lanes) % 2 == 0 ? -1 : 1;
                part.Height = Radius;
                part.Forward = side * (LipForward + DeckLength / 2);
                if (index >= 64)
                {
                    part.Prefab = SkateParkLayout.LowWallPrefab;
                    part.Yaw = side < 0 ? 270 : 90;
                    part.Forward = side * (LipForward + (index < 72 ? 0 : DeckLength) + 0.3);
                    if (index < 72) part.Height -= 1;
                    else part.Material = SkateParkMaterial.Wood;
                }
            }
            return true;
        }

        public static bool TryProfilePoint(int index, out double forward, out double height)
        {
            forward = height = 0;
            if (index < 0 || index >= ProfilePointCount) return false;
            height = RideLift;
            if (index == 0 || index == ProfilePointCount - 1)
            {
                forward = (index == 0 ? -1 : 1) * (LipForward + DeckLength);
                height += Radius;
                return true;
            }
            bool back = index <= CurveSteps + 1;
            double angle = (back ? CurveSteps + 1 - index : index - CurveSteps - 2) * Math.PI / (2 * CurveSteps);
            forward = (back ? -1 : 1) * (FlatLength / 2 + Radius * Math.Sin(angle));
            height += Radius * (1 - Math.Cos(angle));
            return true;
        }
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
    // BEGIN SHARED SKATE PARK BUILD ITEMS
    public enum SkateParkBuildItemState { None, Preparing, Paid, RecoveryClaimed }

    public static class SkateParkBuildItems
    {
        public const string Carrier = "building.planner";
        public const int MarkerBase = 0x534B5000;
        public const int PieceCount = 12;

        public static string Name(SkateParkKind kind)
        {
            switch (kind)
            {
                case SkateParkKind.Bank: return "Skate Bank";
                case SkateParkKind.Kicker: return "Skate Kicker";
                case SkateParkKind.QuarterPipe: return "Skate Quarterpipe";
                case SkateParkKind.LedgeLow: return "Skate Low Ledge";
                case SkateParkKind.LedgeHigh: return "Skate High Ledge";
                case SkateParkKind.LongLedge: return "Skate Long Ledge";
                case SkateParkKind.Funbox: return "Skate Funbox";
                case SkateParkKind.Halfpipe: return "Skate Halfpipe";
                case SkateParkKind.MegaHalfpipe: return "Skate MEGA Halfpipe";
                case SkateParkKind.MiniQuarterPipe: return "Skate Mini Quarterpipe";
                case SkateParkKind.LargeQuarterPipe: return "Skate Large Quarterpipe";
                case SkateParkKind.HalfWallKicker: return "Skate Half-wall Kicker";
                default: return null;
            }
        }

        public static bool Tagged(int marker)
        { return marker >= MarkerBase && marker <= MarkerBase + 255; }

        public static int Marker(SkateParkKind kind)
        { return Name(kind) == null ? 0 : MarkerBase + (int)kind + 1; }

        public static bool TryKind(int marker, out SkateParkKind kind)
        {
            kind = default(SkateParkKind);
            if (!Tagged(marker)) return false;
            int index = marker - MarkerBase - 1;
            if (index < 0 || index >= PieceCount) return false;
            var parsed = (SkateParkKind)index;
            if (Name(parsed) == null) return false;
            kind = parsed;
            return true;
        }

        public static bool TryIdentity(string carrier, ulong skin, int marker, string name,
            int amount, out SkateParkKind kind)
        {
            kind = default(SkateParkKind);
            SkateParkKind parsed;
            if (carrier != Carrier || skin != 0 || amount != 1 || !TryKind(marker, out parsed) ||
                !string.Equals(name, Name(parsed), StringComparison.Ordinal)) return false;
            kind = parsed;
            return true;
        }

        public static bool VerifiedDebit(int before, int after, bool sameItem)
        { return sameItem && before == 1 && after == 0; }

        public static bool Recoverable(SkateParkBuildItemState state, int members, SkateParkKind kind)
        {
            return state == SkateParkBuildItemState.Paid && Name(kind) != null &&
                members == SkateParkLayout.PartCount(kind);
        }

        public static bool ValidState(SkateParkBuildItemState state)
        { return (int)state >= 0 && (int)state <= (int)SkateParkBuildItemState.RecoveryClaimed; }
    }
    // END SHARED SKATE PARK BUILD ITEMS

    public static class SkateParkPlacement
    {
        public static bool TryRequest(string[] args, out SkateParkKind kind, out bool explicitAnchor,
            out double x, out double y, out double z, out double yaw, out string failure)
        {
            kind = default(SkateParkKind); explicitAnchor = false;
            x = y = z = yaw = 0;
            failure = "Usage: skate.place <bank|kicker|quarterpipe|ledge-low|ledge-high|long-ledge|funbox|halfpipe|mega-halfpipe> [x y z yaw].";
            if (args == null || (args.Length != 1 && args.Length != 5)) return false;
            SkateParkKind parsed;
            if (!SkateParkLayout.TryKind(args[0], out parsed))
            { failure = "Unknown park piece. Use bank, kicker, quarterpipe, ledge-low, ledge-high, long-ledge, funbox, halfpipe, mini-quarterpipe, large-quarterpipe or mega-halfpipe."; return false; }
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
    [Info("ShortcutSkate", "Dj-Shortcut", "0.3.31")]
    [Description("Player-scoped skateboard movement bounds and saved park assemblies.")]
    public sealed class ShortcutSkate : RustPlugin
    {
        private const string UsePermission = "shortcutskate.use";
        private const string FlyPermission = "shortcutskate.fly";
        private static ShortcutSkate instance;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Dictionary<ulong, Session> sessions = new Dictionary<ulong, Session>();
        private readonly Dictionary<ulong, BasePlayer> optedOut = new Dictionary<ulong, BasePlayer>();
        private readonly List<PracticeReceipt> practice = new List<PracticeReceipt>();
        private bool practiceCleaning, practiceSpawning;
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
        private const double NativeStallStartupSeconds = 5;
        private readonly Dictionary<ulong, StallStartup> stallStartups = new Dictionary<ulong, StallStartup>();
        private long nativeStallCorrections, nativeStallExemptions;
        private double nativeStallSaidAt = -99;

        private sealed class StallStartup
        {
            public BasePlayer Player;
            public double Started;
        }

        private const int MaximumParkGroups = 256;
        private const int MaximumParkSelections = 128;
        private const int MaximumParkDataLength = 1048576;
        private const float MaximumParkPlacementDistance = 25;
        private const string ParkFoundationPrefab = "assets/prefabs/building core/foundation/foundation.prefab";
        private readonly Dictionary<ulong, ParkSelection> parkSelections = new Dictionary<ulong, ParkSelection>();
        private object parkSelectionEpoch = new object();
        private readonly Dictionary<ulong, ParkGroup> parkMembers = new Dictionary<ulong, ParkGroup>();
        private readonly Dictionary<ParkGroup, ParkProfileCollision> parkProfileCollisions = new Dictionary<ParkGroup, ParkProfileCollision>();
        private readonly Dictionary<ulong, string> parkProfileRestoreHolds = new Dictionary<ulong, string>();
        private readonly List<ParkRollbackReceipt> parkRollback = new List<ParkRollbackReceipt>();
        private bool parkRollbackCleaning;
        private readonly Dictionary<ulong, ParkNativeRequest> parkNativeRequests = new Dictionary<ulong, ParkNativeRequest>();
        private readonly List<ParkNativeRequest> parkNativeCleanup = new List<ParkNativeRequest>();
        private bool parkNativeCleaning;
        private ParkPlacementRequest parkBuildHookRequest;
        private Construction parkBuildHookConstruction;
        private Construction.Target parkBuildHookTarget;
        private readonly List<ParkItemDelivery> parkItemDeliveries = new List<ParkItemDelivery>();
        private ParkData parkData;
        private bool parkDataBroken;
        private bool parkStorageFailed;
        private string parkStorageError;
        private bool parkReady;
        private bool parkProfileRestoreRefreshNeeded;
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
            [JsonProperty(Required = Required.DisallowNull, DefaultValueHandling = DefaultValueHandling.Ignore)]
            public SkateParkBuildItemState BuildItemState;
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public ParkWallAttachment Wall;
        }

        public sealed class ParkWallAttachment
        {
            [JsonProperty(Required = Required.Always), JsonConverter(typeof(ParkIdConverter))] public ulong Id, Owner;
            [JsonProperty(Required = Required.Always)] public uint Building;
            [JsonProperty(Required = Required.Always)] public int Side, Grade;
            [JsonProperty(Required = Required.Always)] public double X, Y, Z, Yaw;
            [JsonProperty(Required = Required.Always)] public SkateParkBox WallBounds, FloorBounds;
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

        private sealed class PracticeReceipt
        {
            public readonly BaseEntity Entity;
            public readonly ParkData World;
            private PracticeLifetime lifetime;

            public PracticeReceipt(BaseEntity entity, ParkData world)
            {
                Entity = entity;
                World = new ParkData { WipeId = world.WipeId, SaveCreatedTicks = world.SaveCreatedTicks,
                    Seed = world.Seed, Size = world.Size, Checksum = world.Checksum };
            }

            public PracticeLifetime Lifetime { get { return lifetime; } }
            public bool Capture()
            {
                // Only validated post-Spawn creation may seal a lifetime; cleanup must never refresh it.
                if (lifetime != null) return false;
                lifetime = new PracticeLifetime(Entity);
                return true;
            }
        }

        private sealed class PracticeLifetime
        {
            public readonly object Network;
            public readonly ulong Id, Owner;
            public readonly uint Prefab;
            public readonly int Frame;

            public PracticeLifetime(BaseEntity entity)
            {
                Network = entity.net;
                Id = ParkId(entity);
                Owner = entity.OwnerID;
                Prefab = entity.prefabID;
                Frame = entity.creationFrame;
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

        private sealed class ParkSupport
        {
            public BuildingBlock Block;
            public ulong Id;
            public ParkAnchorSnapshot Snapshot;
            public BuildingGrade.Enum Grade;
            public Construction Construction;
            public Vector3 Point;
            public Collider Collider;
            public string Cell;
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
            public BuildingBlock WallBlock;
            public ParkWallAttachment Wall;
            public BuildingBlock Foundation;
            public BuildingBlock Support;
            public ulong SupportId;
            public object SupportNetwork;
            public uint SupportPrefab;
            public int SupportFrame;
            public ParkAnchorSnapshot SupportSnapshot;
            public ulong FoundationId;
            public ParkAnchorSnapshot Snapshot;
            public ParkSelection Selection;
            public List<ParkSupport> Supports;
            public double SupportTop;
            public string SupportFailure;
            public BuildingBlock SnapBlock;
            public ulong SnapId;
            public ParkAnchorSnapshot SnapSnapshot;
            public double SnapSupportTop;
            public uint Building;
            public bool BuildItem, ItemSpent, RefundClaimed;
            public SkateParkKind ItemKind;
            public ItemId ItemUid;
            public ItemDefinition ItemDefinition;
            public ProtoBuf.Item.InstanceData ItemData;
            public ulong Owner;
        }

        private sealed class ParkItemDelivery
        {
            public bool Delivering;
            public Item Item;
            public ItemId Uid;
            public SkateParkKind Kind;
            public BasePlayer Player;
            public Vector3 Position;
            public ulong Owner;
            public ParkData Data;
        }

        private sealed class ParkRollbackReceipt
        {
            public readonly BaseEntity Entity;
            public readonly ParkData Data, World;
            private ParkRollbackLifetime lifetime;

            public ParkRollbackReceipt(BaseEntity entity, ParkData data)
            {
                Entity = entity;
                Data = data;
                World = new ParkData { WipeId = data.WipeId, SaveCreatedTicks = data.SaveCreatedTicks,
                    Seed = data.Seed, Size = data.Size, Checksum = data.Checksum };
            }

            public ParkRollbackLifetime Lifetime { get { return lifetime; } }
            public bool Capture()
            {
                // Only the caller's validated post-Spawn boundary may seal this receipt.
                // A later cleanup attempt must never adopt a replacement lifetime.
                if (lifetime != null) return false;
                lifetime = new ParkRollbackLifetime(Entity);
                return true;
            }
        }

        private sealed class ParkRollbackLifetime
        {
            public readonly object Network;
            public readonly ulong Id, Owner;
            public readonly uint Prefab;
            public readonly int Frame;

            public ParkRollbackLifetime(BaseEntity entity)
            {
                Network = entity.net;
                Id = ParkId(entity);
                Owner = entity.OwnerID;
                Prefab = entity.prefabID;
                Frame = entity.creationFrame;
            }
        }

        private sealed class ParkNativeRequest
        {
            public ParkPlacementRequest Placement;
            public Construction Construction;
            public Construction CarrierConstruction;
            public BuildingBlock Carrier;
            public BaseEntity Parent;
            public GameObject UnverifiedObject;
            public BaseEntity UnverifiedEntity;
            public ulong UnverifiedId;
            public ParkNativeRequest ObservationOrigin;
            public List<ParkNativeRequest> AdditionalObservations;
            public bool Observed;
            public ulong CarrierId, CarrierOwner;
            public ulong ParentId, ParentOwner;
            public uint CarrierPrefab, ParentPrefab, ParentBone;
            public Vector3 Position;
            public Quaternion Rotation;
            public bool BuildChecked, Admitted, Captured, SavingDisabled, PaymentSuppressed, CleanupReady, Removed, Unverified;
            public string Failure;
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
                    else if (reader.TokenType == JsonToken.PropertyName)
                    {
                        string property = (string)reader.Value;
                        if (!keys.Peek().Add(property)) throw new InvalidDataException("Duplicate park data property.");
                        if (string.Equals(property, "BuildItemState", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!reader.Read() || reader.TokenType != JsonToken.Integer ||
                                Convert.ToInt64(reader.Value, CultureInfo.InvariantCulture) < 0 ||
                                Convert.ToInt64(reader.Value, CultureInfo.InvariantCulture) > (int)SkateParkBuildItemState.RecoveryClaimed)
                                throw new InvalidDataException("Park build-item state must be an integer from zero through three.");
                        }
                    }
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
                    !groups.Add(group.Id) || group.Owner == 0 || !SkateParkBuildItems.ValidState(group.BuildItemState) ||
                    !SkateParkLayout.TryKind(group.Kind, out kind) || group.Kind != SkateParkLayout.Name(kind) ||
                    group.Members == null || group.Members.Count < 1 || group.Members.Count > SkateParkLayout.PartCount(kind))
                    throw new InvalidDataException("Invalid park group identity or kind.");
                SkateParkTransform wallAnchor;
                if (kind == SkateParkKind.HalfWallKicker ? !ValidWallAttachment(group.Wall, out wallAnchor) : group.Wall != null)
                    throw new InvalidDataException("Invalid half-wall attachment metadata.");
                if (group.Wall != null && (!TryWallAnchor(group.Wall, out wallAnchor) ||
                    !Near(group.AnchorX, wallAnchor.X) || !Near(group.AnchorY, wallAnchor.Y) ||
                    !Near(group.AnchorZ, wallAnchor.Z) || !Near(group.AnchorYaw, wallAnchor.Yaw)))
                    throw new InvalidDataException("Half-wall attachment join does not match its anchor.");
                var indices = new HashSet<int>();
                foreach (ParkMember member in group.Members)
                {
                    SkateParkTransform expected;
                    if (member == null || member.Id == 0 || member.Prefab == 0 || !members.Add(member.Id) || !indices.Add(member.Index) ||
                        !SkateParkLayout.TryTransform(kind, member.Index, group.AnchorX, group.AnchorY, group.AnchorZ,
                            group.AnchorYaw, out expected) || !Near(member.X, expected.X) || !Near(member.Y, expected.Y) ||
                        !Near(member.Z, expected.Z) ||
                        (!Near(member.Yaw, expected.Yaw) && !(kind == SkateParkKind.Halfpipe && member.Index >= 16 &&
                            Near(member.Yaw, (expected.Yaw + 270) % 360))) || !Near(member.Pitch, expected.Pitch))
                        throw new InvalidDataException("Invalid or duplicate park member identity or layout.");
                }
            }
            var sockets = new HashSet<string>(StringComparer.Ordinal);
            foreach (ParkGroup group in value.Groups)
                if (group.Wall != null && (members.Contains(group.Wall.Id) ||
                    !sockets.Add(group.Wall.Id.ToString(CultureInfo.InvariantCulture) + ":" + group.Wall.Side)))
                    throw new InvalidDataException("A half-wall host is a park member or its side is already occupied.");
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
                { "halfpipe", "bank", "kicker", "quarterpipe", "ledge-low", "ledge-high", "long-ledge", "funbox",
                    "mini-quarterpipe", "large-quarterpipe" };
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
            public double RidingAt = -99;
        }

        private const string TracePatchId = "shortcut.skate.movementtrace";
        private Harmony traceHarmony;
        private bool traceFailed;
        private MovementTrace movementTrace;
        [ThreadStatic] private static TraceValidation traceValidation;
        [ThreadStatic] private static TraceRpc traceRpc;
        [ThreadStatic] private static string traceScope;

        private sealed class MovementTrace
        {
            public BasePlayer Player;
            public string Id;
            public double Ends;
            public long Validations, Rejections, Sends, Dropped, Written, Errors;
            public string LastDecision = "none";
            public long Decision, DecisionUtcTicks;
            public readonly Queue<string> Pending = new Queue<string>();
            public readonly object Sync = new object();

            public void Write(string value)
            {
                lock (Sync)
                {
                    if (Written >= 512) { Dropped++; return; }
                    Written++;
                    Pending.Enqueue("Movement trace=" + Id + "; event=" + Written.ToString(CultureInfo.InvariantCulture) +
                        "; utc=" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) + "; " + value);
                }
            }
        }

        private sealed class TraceValidation
        {
            public MovementTrace Run;
            public int Index;
            public double Seconds;
            public Vector3 Start, End;
            public bool Noclip, Speed, Fly;
            public string Blocker = "none";
            public string PreviousScope;
        }

        private sealed class TraceRpc
        {
            public MovementTrace Run;
            public string Function, Scope;
            public Vector3 Destination;
        }

        private MovementTrace TraceFor(BasePlayer player)
        {
            MovementTrace run = movementTrace;
            return run != null && ReferenceEquals(run.Player, player) &&
                clock.Elapsed.TotalSeconds < run.Ends ? run : null;
        }

        private static string TracePoint(Vector3 value)
        {
            return value.x.ToString("F4", CultureInfo.InvariantCulture) + "," +
                value.y.ToString("F4", CultureInfo.InvariantCulture) + "," + value.z.ToString("F4", CultureInfo.InvariantCulture);
        }

        private static string TraceName(string value)
        {
            if (string.IsNullOrEmpty(value)) return "none";
            return new string(value.Take(96).Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' ? c : '_').ToArray());
        }

        private static string TraceBlocker(Collider collider)
        {
            if (collider == null) return "none";
            BaseEntity entity = collider.GetComponentInParent<BaseEntity>();
            BuildingBlock block = entity as BuildingBlock;
            return "collider:" + TraceName(collider.name) + ",layer:" + collider.gameObject.layer.ToString(CultureInfo.InvariantCulture) +
                ",instance:" + collider.GetInstanceID().ToString(CultureInfo.InvariantCulture) +
                ",entity:" + TraceName(entity == null ? null : entity.ShortPrefabName) +
                ",grade:" + (block == null ? "none" : block.grade.ToString()) +
                ",bounds-center:" + TracePoint(collider.bounds.center) + ",bounds-size:" + TracePoint(collider.bounds.size);
        }

        private void DrainTrace(MovementTrace run)
        {
            if (run == null) return;
            var lines = new List<string>();
            lock (run.Sync) while (run.Pending.Count != 0) lines.Add(run.Pending.Dequeue());
            foreach (string line in lines) Puts(line);
        }

        private void FinishTrace(string reason)
        {
            MovementTrace run = movementTrace;
            movementTrace = null;
            if (run == null) return;
            DrainTrace(run);
            Puts("Movement trace=" + run.Id + "; stopped=" + reason + "; validations=" + run.Validations.ToString(CultureInfo.InvariantCulture) +
                "; rejected=" + run.Rejections.ToString(CultureInfo.InvariantCulture) + "; correction-sends=" + run.Sends.ToString(CultureInfo.InvariantCulture) +
                "; dropped=" + run.Dropped.ToString(CultureInfo.InvariantCulture) + "; errors=" + run.Errors.ToString(CultureInfo.InvariantCulture));
        }

        private void FlushTrace()
        {
            MovementTrace run = movementTrace;
            if (run == null) return;
            DrainTrace(run);
            if (clock.Elapsed.TotalSeconds >= run.Ends || run.Player == null || !run.Player.IsConnected)
                FinishTrace("expired-or-disconnected");
        }

        private void TracePatch(Type target, string name, Type[] arguments, Type hooks, bool prefix, bool postfix, bool finalizer)
        {
            MethodInfo method = AccessTools.Method(target, name, arguments);
            if (method == null) throw new InvalidOperationException("A movement trace target is unavailable.");
            var previous = Harmony.GetPatchInfo(method);
            if (name == "ClientRPCSend" && previous != null && previous.Transpilers.Any(p => p.owner != TracePatchId))
                throw new InvalidOperationException("The native send body has another transpiler.");
            Func<string, HarmonyMethod> hook = part => new HarmonyMethod(hooks.GetMethod(part, BindingFlags.Static | BindingFlags.NonPublic))
                { priority = part == "Postfix" ? Priority.Last : Priority.First };
            traceHarmony.Patch(method, prefix: prefix ? hook("Prefix") : null, postfix: postfix ? hook("Postfix") : null,
                finalizer: finalizer ? hook("Finalizer") : null);
            var patches = Harmony.GetPatchInfo(method);
            if (patches == null || (prefix && !patches.Prefixes.Any(p => p.owner == TracePatchId)) ||
                (postfix && !patches.Postfixes.Any(p => p.owner == TracePatchId)) || (finalizer && !patches.Finalizers.Any(p => p.owner == TracePatchId)))
                throw new InvalidOperationException("A movement trace observer was not registered.");
        }

        private bool InstallTrace()
        {
            if (traceFailed) return false;
            if (traceHarmony != null) return true;
            try
            {
                traceHarmony = new Harmony(TracePatchId);
                TracePatch(typeof(AntiHack), "ValidateMoves", new[] { typeof(BasePlayer.PlayerServerStates.ReadOnly).MakeByRefType(),
                    typeof(NativeArray<int>.ReadOnly), typeof(NativeArray<BasePlayer.PositionChange>) }, typeof(TraceMoves), true, true, true);
                TracePatch(typeof(AntiHack), "AreNoClipping", new[] { typeof(BasePlayer.PlayerServerStates.ReadOnly).MakeByRefType(),
                    typeof(NativeArray<AntiHack.PlayerNoclipState>), typeof(NativeArray<int>.ReadOnly), typeof(NativeList<int>), typeof(Span<Collider>) },
                    typeof(TraceNoclip), false, true, false);
                TracePatch(typeof(AntiHack), "AreSpeeding", null, typeof(TraceSpeed), false, true, false);
                TracePatch(typeof(AntiHack), "AreFlying", null, typeof(TraceFly), false, true, false);
                TracePatch(typeof(AntiHack), "Log", new[] { typeof(BasePlayer), typeof(AntiHackType), typeof(string), typeof(bool) }, typeof(TraceLog), true, false, false);
                TracePatch(typeof(BaseEntity), "ClientRPC", new[] { typeof(RpcTarget), typeof(Vector3) }, typeof(TraceRpcCall), true, false, true);
                TracePatch(typeof(BaseEntity), "ClientRPC", new[] { typeof(RpcTarget), typeof(Vector3), typeof(NetworkableId) }, typeof(TraceRpcCall), true, false, true);
                TracePatch(typeof(BaseEntity), "ClientRPCSend", new[] { typeof(Network.NetWrite), typeof(Network.SendInfo) }, typeof(TraceRpcSend), false, true, false);
                TracePatch(typeof(BasePlayer), "UpdatePositionFromTick", new[] { typeof(PlayerTick), typeof(bool) }, typeof(TraceTick), true, false, true);
                TracePatch(typeof(BasePlayer), "ServerFinalizePlayers", null, typeof(TraceFinalize), true, false, true);
                return true;
            }
            catch (Exception)
            {
                traceFailed = true;
                try { if (traceHarmony != null) traceHarmony.UnpatchAll(TracePatchId); } catch (Exception) { }
                traceHarmony = null;
                return false;
            }
        }

        [ConsoleCommand("skate.trace")]
        private void TraceCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer actor = arg.Player();
            bool privileged = (actor != null && actor.IsAdmin) || arg.IsAdmin ||
                (actor == null && arg.IsServerside && arg.Connection == null);
            if (!privileged) { arg.ReplyWith("Administrator access is required."); return; }
            int count = arg.Args == null ? 0 : arg.Args.Length;
            if (count == 1 && arg.GetString(0) == "stop") { FinishTrace("operator"); arg.ReplyWith("Movement trace stopped."); return; }
            ulong target;
            int seconds = 60;
            if (count < 1 || count > 2 || !ulong.TryParse(arg.GetString(0), NumberStyles.None, CultureInfo.InvariantCulture, out target) || target == 0 ||
                (count == 2 && (!int.TryParse(arg.GetString(1), NumberStyles.None, CultureInfo.InvariantCulture, out seconds) || seconds < 1 || seconds > 120)))
            { arg.ReplyWith("Usage: skate.trace <steamID> [1-120 seconds], or skate.trace stop."); return; }
            BasePlayer player = BasePlayer.FindByID(target);
            if (player == null || !player.IsConnected || player.IsNpc)
            { arg.ReplyWith("No connected player with that Steam ID."); return; }
            if (!InstallTrace()) { arg.ReplyWith("Movement trace is unavailable; riding and native checks are unchanged."); return; }
            FinishTrace("replaced");
            var run = new MovementTrace { Player = player, Id = Guid.NewGuid().ToString("N").Substring(0, 8), Ends = clock.Elapsed.TotalSeconds + seconds };
            movementTrace = run;
            run.Write("started; seconds=" + seconds.ToString(CultureInfo.InvariantCulture) + "; observers=installed");
            DrainTrace(run);
            arg.ReplyWith("Movement trace " + run.Id + " armed for " + seconds.ToString(CultureInfo.InvariantCulture) + " seconds; observers installed. Native checks are unchanged.");
        }

        private static class TraceMoves
        {
            private static void Prefix(ref BasePlayer.PlayerServerStates.ReadOnly __0, NativeArray<int>.ReadOnly __1, out TraceValidation __state)
            {
                __state = traceValidation;
                traceValidation = null;
                try
                {
                    MovementTrace run = instance == null ? null : instance.movementTrace;
                    if (run == null || instance.TraceFor(run.Player) == null) return;
                    foreach (int index in __1)
                    {
                        if (!ReferenceEquals(__0.PlayerCache.Objects[index], run.Player)) continue;
                        var cache = __0.TickCache;
                        int count = cache.Infos[index].Count, offset = checked(index * cache.BufferSize);
                        traceValidation = new TraceValidation { Run = run, Index = index, Seconds = __0.TickDeltaTime[index],
                            Start = cache.Segments[offset].point, End = cache.Segments[offset + count].point,
                            PreviousScope = traceScope };
                        traceScope = "validate";
                        return;
                    }
                }
                catch (Exception) { if (instance != null && instance.movementTrace != null) instance.movementTrace.Errors++; }
            }

            private static void Postfix(NativeArray<BasePlayer.PositionChange> __2, TraceValidation __state)
            {
                TraceValidation value = traceValidation;
                if (value == null || ReferenceEquals(value, __state)) return;
                try
                {
                    BasePlayer.PositionChange result = __2[value.Index];
                    MovementTrace run = value.Run;
                    run.Validations++; run.Decision++;
                    run.DecisionUtcTicks = DateTime.UtcNow.Ticks;
                    string reasons = (value.Noclip ? "noclip," : "") + (value.Speed ? "speed," : "") + (value.Fly ? "fly," : "") +
                        (value.Seconds > ConVar.AntiHack.maxdeltatime ? "max-delta," : "");
                    run.LastDecision = result + ":" + (reasons.Length == 0 ? "unobserved-reason" : reasons.TrimEnd(','));
                    if (result != BasePlayer.PositionChange.Invalid) return;
                    run.Rejections++;
                    run.Write("rejected; decision=" + run.Decision.ToString(CultureInfo.InvariantCulture) + "; reason=" + run.LastDecision +
                        "; dt=" + value.Seconds.ToString("G9", CultureInfo.InvariantCulture) + "; from=" + TracePoint(value.Start) +
                        "; to=" + TracePoint(value.End) + "; blocker=" + value.Blocker);
                }
                catch (Exception) { value.Run.Errors++; }
            }

            private static void Finalizer(TraceValidation __state)
            {
                if (traceValidation != null && !ReferenceEquals(traceValidation, __state)) traceScope = traceValidation.PreviousScope;
                traceValidation = __state;
            }
        }

        private static class TraceNoclip
        {
            private static void Postfix(NativeList<int> __3, Span<Collider> __4)
            {
                TraceValidation value = traceValidation;
                if (value == null) return;
                try
                {
                    for (int n = 0; n < __3.Length; n++) if (__3[n] == value.Index)
                    { value.Noclip = true; value.Blocker = TraceBlocker(__4[value.Index]); break; }
                }
                catch (Exception) { value.Run.Errors++; }
            }
        }

        private static void TraceBoolean(NativeArray<int>.ReadOnly indices, NativeArray<bool> results, bool flying)
        {
            TraceValidation value = traceValidation;
            if (value == null) return;
            try
            {
                for (int n = 0; n < indices.Length; n++) if (indices[n] == value.Index)
                { if (flying) value.Fly = results[n]; else value.Speed = results[n]; return; }
            }
            catch (Exception) { value.Run.Errors++; }
        }

        private static class TraceSpeed
        { private static void Postfix(NativeArray<int>.ReadOnly __2, NativeArray<bool> __3) { TraceBoolean(__2, __3, false); } }
        private static class TraceFly
        { private static void Postfix(NativeArray<int>.ReadOnly __3, NativeArray<bool> __4) { TraceBoolean(__3, __4, true); } }

        private static class TraceLog
        {
            private static void Prefix(BasePlayer __0, AntiHackType __1, string __2)
            {
                MovementTrace run = null;
                try
                {
                    run = instance == null ? null : instance.TraceFor(__0);
                    if (run == null) return;
                    string reason = __2 != null && __2.StartsWith("moved too far between ticks:", StringComparison.Ordinal) ? "tick-distance" :
                        __2 != null && __2.StartsWith("Player had too many ticks buffered", StringComparison.Ordinal) ? "tick-overflow" : "native-log";
                    run.Write("native-check; reason=" + reason + "; type=" + __1 + "; scope=" + (traceScope ?? "other"));
                }
                catch (Exception) { if (run != null) run.Errors++; }
            }
        }

        private static class TraceRpcCall
        {
            private static void Prefix(BaseEntity __instance, RpcTarget __0, Vector3 __1, out TraceRpc __state)
            {
                __state = traceRpc;
                traceRpc = null;
                MovementTrace run = null;
                try
                {
                    run = instance == null ? null : instance.TraceFor(__instance as BasePlayer);
                    if (run == null || (__0.Function != "ForcePositionTo" && __0.Function != "ForcePositionToParentOffset")) return;
                    traceRpc = new TraceRpc { Run = run, Function = __0.Function, Destination = __1, Scope = traceScope ?? "other" };
                }
                catch (Exception) { if (run != null) run.Errors++; }
            }
            private static void Finalizer(TraceRpc __state) { traceRpc = __state; }
        }

        private static class TraceRpcSend
        {
            private static void Postfix(BaseEntity __instance, Network.SendInfo __1, bool __runOriginal)
            {
                TraceRpc value = traceRpc;
                if (value == null || !ReferenceEquals(value.Run.Player, __instance)) return;
                try
                {
                    MovementTrace run = value.Run;
                    if (!__runOriginal) { run.Errors++; return; }
                    var connection = run.Player.net == null ? null : run.Player.net.connection;
                    if (connection == null || (!ReferenceEquals(__1.connection, connection) &&
                        (__1.connections == null || !__1.connections.Any(c => ReferenceEquals(c, connection))))) return;
                    run.Sends++;
                    run.Write("correction-send; rpc=" + value.Function + "; scope=" + value.Scope + "; destination=" + TracePoint(value.Destination) +
                        "; destination-space=" + (value.Function == "ForcePositionToParentOffset" ? "parent-offset" : "world") +
                        "; recipient=target; decision=" + run.Decision.ToString(CultureInfo.InvariantCulture) + "; last-validation=" + run.LastDecision +
                        "; last-validation-age-ms=" + (run.DecisionUtcTicks == 0 ? "unknown" :
                            ((DateTime.UtcNow.Ticks - run.DecisionUtcTicks) / 10000.0).ToString("F3", CultureInfo.InvariantCulture)));
                }
                catch (Exception) { value.Run.Errors++; }
            }
        }

        private static class TraceTick
        {
            private static void Prefix(BasePlayer __instance, out string __state)
            {
                __state = traceScope;
                if (instance != null && instance.TraceFor(__instance) != null) traceScope = "tick";
            }
            private static void Finalizer(string __state) { traceScope = __state; }
        }

        private static class TraceFinalize
        {
            private static void Prefix(out string __state) { __state = traceScope; traceScope = "finalize"; }
            private static void Finalizer(string __state) { traceScope = __state; }
        }

        public const double RidingSpeed = 7, RidingGraceSeconds = 3;

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
                value.ParkPieceOrder == null || value.ParkPieceOrder.Length < 1 || value.ParkPieceOrder.Length > SkateParkBuildItems.PieceCount)
                throw new InvalidDataException("Use distinct valid gesture names and a unique park-piece order containing 1-12 pieces.");
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
            CleanupWallAttachments();
            InitializeParkGestures();
            KeepDaylight();
            ready = settings != null && Patched("ValidateMoves", typeof(MovePatch), false) &&
                Patched("AreSpeeding", typeof(SpeedPatch), true) && Patched("AreFlying", typeof(FlyPatch), true) && StallPatched();
            if (!ready) PrintError("Skating is disabled: matching movement patches and valid config are required.");
            else Puts("Skating ready: movement patches installed; permitted eligible players up to " + settings.MaximumHorizontalSpeed +
                " m/s; fly exemption requires a server admin or shortcutskate.fly and vertical travel up to 200 m/s; support, airtime and rearm are diagnostic only.");
            timer.Every(0.1f, Sweep);
            timer.Every(0.1f, FlushTrace);
            timer.Every(1f, CleanupWallAttachments);
            timer.Every(HeldParkPreviewSeconds, DrawHeldParkPreviews);
        }

        // While an administrator on foot holds a Skate park build item, the server draws that
        // piece's footprint where they aim, with the same ray, mask and target rule a placement
        // uses: green where the target is accepted, red with a short reason where it is not, with
        // the piece name and an arrow to its forward edge. The client cannot read the held item in
        // this Rust build, so the server shows it.
        private const float HeldParkPreviewSeconds = 0.25f;
        private void DrawHeldParkPreviews()
        {
            if (!parkReady || parkClosing) return;
            foreach (BasePlayer player in BasePlayer.activePlayerList.ToArray())
            {
                try { DrawHeldParkPreview(player); }
                catch (Exception) { }
            }
        }

        private void DrawHeldParkPreview(BasePlayer player)
        {
            SkateParkKind kind;
            Session session;
            if (player == null || !player.IsAdmin || !Eligible(player) || player.eyes == null ||
                !(player.GetHeldEntity() is Planner) || !ParkItemIdentity(player.GetActiveItem(), out kind)) return;
            // Every permitted player has a movement session on foot too; only recent riding speed
            // (the rule OnPlayerLand uses) means the player is on the board.
            if (sessions.TryGetValue(player.userID, out session) && ReferenceEquals(player, session.Player) &&
                clock.Elapsed.TotalSeconds - session.RidingAt <= RidingGraceSeconds) return;
            // The placement ray's mask, players included: a player in the way blocks a placement, so
            // the preview must not turn green on the surface behind them.
            int mask = LayerMask.GetMask("World", "Terrain", "Construction", "Deployed", "Player (Server)");
            Ray ray = player.eyes.HeadRay();
            RaycastHit hit;
            if (mask == 0 || !Coordinate(ray.origin) || !Coordinate(ray.direction) || ray.direction.sqrMagnitude < 0.0001f ||
                !Physics.Raycast(ray.origin, ray.direction.normalized, out hit, MaximumParkPlacementDistance, mask, QueryTriggerInteraction.Ignore) ||
                hit.collider == null || !Coordinate(hit.point) || !Coordinate(hit.normal)) return;
            string name = SkateParkBuildItems.Name(kind);
            float life = HeldParkPreviewSeconds + 0.05f;
            var failures = new List<string>();
            ParkNativePart[] definitions;
            bool prefabs = ParkPrefabs(kind, failures, out definitions) && failures.Count == 0;
            var probe = new ParkPlacementRequest { Player = player, Owner = player.userID, ItemKind = kind, BuildItem = true };
            double x = hit.point.x, z = hit.point.z, yaw = player.eyes.rotation.eulerAngles.y, top = hit.point.y;
            if (kind == SkateParkKind.HalfWallKicker)
            {
                // A half-wall kicker is placed against an aimed half wall, through the same rule.
                ParkGroup wallGroup;
                string wallFailure;
                if (prefabs && TryWallPlacement(probe, ray, null, definitions, out wallGroup, out wallFailure))
                { DrawHalfWallKickerPreview(player, wallGroup, name, life); return; }
                DrawParkFootprint(player, kind, x, z, yaw, top, false, name + " - aim at the side of a half wall", life);
                return;
            }
            BuildingBlock host = hit.collider.ToBaseEntity() as BuildingBlock;
            bool valid = false;
            string why = "aim at a foundation top";
            if (host != null && hit.normal.y >= 0.98f)
            {
                string failure;
                if (prefabs && TryParkTargetFrame(probe, host, hit, 0, true, definitions, out x, out z, out yaw, out failure))
                {
                    valid = true;
                    why = "";
                    if (Coordinate(probe.SnapSupportTop)) top = probe.SnapSupportTop;
                }
                else why = "no compatible foundation here";
            }
            DrawParkFootprint(player, kind, x, z, yaw, top, valid, name + (valid ? "" : " - " + why), life);
        }

        // The outline of a piece's support cells in the piece frame (right, forward, metres): the
        // exposed cell edges, merged along each grid line, so an L-shaped piece such as the large
        // quarterpipe shows the cells it reserves and not a bounding rectangle.
        private sealed class ParkOutline
        {
            public double R0, R1, F0, F1;
            public readonly List<double[]> Edges = new List<double[]>();
        }
        private readonly Dictionary<SkateParkKind, ParkOutline> parkOutlines = new Dictionary<SkateParkKind, ParkOutline>();
        private static long OutlineKey(int right, int forward) { return ((long)right << 32) ^ (uint)forward; }
        private ParkOutline Outline(SkateParkKind kind)
        {
            ParkOutline print;
            if (parkOutlines.TryGetValue(kind, out print)) return print;
            var cells = new HashSet<long>();
            int rmin = 0, rmax = 0, fmin = 0, fmax = 0;
            for (int i = 0; i < SkateParkLayout.SupportCellCount(kind); i++)
            {
                int right, forward;
                if (!SkateParkLayout.TrySupportCell(kind, i, out right, out forward)) continue;
                cells.Add(OutlineKey(right, forward));
                rmin = Math.Min(rmin, right); rmax = Math.Max(rmax, right);
                fmin = Math.Min(fmin, forward); fmax = Math.Max(fmax, forward);
            }
            if (cells.Count == 0) cells.Add(OutlineKey(0, 0));
            double size = SkateParkLayout.CellSize, half = size / 2, phase = kind == SkateParkKind.MegaHalfpipe ? -4.5 : 0;
            print = new ParkOutline { R0 = rmin * size - half, R1 = rmax * size + half,
                F0 = fmin * size - half + phase, F1 = fmax * size + half + phase };
            // Edges across the forward axis: the line between row f - 1 and row f, where exactly one
            // of the two cells is reserved. A run of exposed edges along the line becomes one segment.
            for (int f = fmin; f <= fmax + 1; f++)
            {
                int start = int.MinValue;
                for (int r = rmin; r <= rmax + 1; r++)
                {
                    bool exposed = r <= rmax && cells.Contains(OutlineKey(r, f)) != cells.Contains(OutlineKey(r, f - 1));
                    if (exposed && start == int.MinValue) start = r;
                    if (exposed || start == int.MinValue) continue;
                    print.Edges.Add(new[] { start * size - half, f * size - half + phase, (r - 1) * size + half, f * size - half + phase });
                    start = int.MinValue;
                }
            }
            for (int r = rmin; r <= rmax + 1; r++)
            {
                int start = int.MinValue;
                for (int f = fmin; f <= fmax + 1; f++)
                {
                    bool exposed = f <= fmax && cells.Contains(OutlineKey(r, f)) != cells.Contains(OutlineKey(r - 1, f));
                    if (exposed && start == int.MinValue) start = f;
                    if (exposed || start == int.MinValue) continue;
                    print.Edges.Add(new[] { r * size - half, start * size - half + phase, r * size - half, (f - 1) * size + half + phase });
                    start = int.MinValue;
                }
            }
            parkOutlines[kind] = print;
            return print;
        }

        private void DrawParkFootprint(BasePlayer player, SkateParkKind kind, double x, double z, double yaw, double top,
            bool valid, string label, float life)
        {
            ParkOutline print = Outline(kind);
            float y = (float)top + 0.05f;
            double c = Math.Cos(yaw * Math.PI / 180), sn = Math.Sin(yaw * Math.PI / 180);
            Func<double, double, Vector3> at = (r, f) => new Vector3((float)(x + r * c + f * sn), y, (float)(z - r * sn + f * c));
            Color color = valid ? Color.green : Color.red;
            foreach (double[] edge in print.Edges)
                player.SendConsoleCommand("ddraw.line", life, color, at(edge[0], edge[1]), at(edge[2], edge[3]));
            Vector3 mid = at((print.R0 + print.R1) / 2, (print.F0 + print.F1) / 2), front = at((print.R0 + print.R1) / 2, print.F1);
            player.SendConsoleCommand("ddraw.arrow", life, color, mid, front, 0.5f);
            player.SendConsoleCommand("ddraw.text", life, color, mid + Vector3.up * 1.5f, label);
        }

        // The half-wall kicker is one floor pitched 30 degrees up against the wall, so its preview
        // is that tilted rectangle at the anchor the placement would use.
        private void DrawHalfWallKickerPreview(BasePlayer player, ParkGroup group, string name, float life)
        {
            SkateParkBox floor = group.Wall.FloorBounds;
            double yaw = group.AnchorYaw * Math.PI / 180, pitch = 30 * Math.PI / 180;
            Vector3 right = new Vector3((float)Math.Cos(yaw), 0, (float)-Math.Sin(yaw));
            Vector3 flat = new Vector3((float)Math.Sin(yaw), 0, (float)Math.Cos(yaw));
            Vector3 slope = flat * (float)Math.Cos(pitch) + Vector3.up * (float)Math.Sin(pitch);
            Vector3 centre = new Vector3((float)group.AnchorX, (float)group.AnchorY, (float)group.AnchorZ) + Vector3.up * 0.05f;
            Vector3 dr = right * (float)(floor.SizeX / 2), df = slope * (float)(floor.SizeZ / 2);
            Vector3 a = centre - dr - df, b = centre + dr - df, d = centre + dr + df, e = centre - dr + df;
            foreach (var edge in new[] { new[] { a, b }, new[] { b, d }, new[] { d, e }, new[] { e, a } })
                player.SendConsoleCommand("ddraw.line", life, Color.green, edge[0], edge[1]);
            player.SendConsoleCommand("ddraw.arrow", life, Color.green, centre, centre + df, 0.5f);
            player.SendConsoleCommand("ddraw.text", life, Color.green, centre + Vector3.up * 1.5f, name);
        }

        // The owner's server is a skate park, tested at all hours: the time stands at midday while
        // this plugin is loaded (env.time 12, env.progresstime false), and the day cycle runs again
        // when it unloads.
        private const float DaylightHour = 12;
        private void KeepDaylight()
        {
            try
            {
                ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), "env.progresstime", false);
                ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), "env.time", DaylightHour);
                Puts("Daylight: time held at " + DaylightHour + ":00.");
            }
            catch (Exception error) { PrintWarning("Daylight could not be held: " + error.Message); }
        }

        private static void ReleaseDaylight()
        {
            try { ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), "env.progresstime", true); }
            catch (Exception) { }
        }

        private static bool Patched(string name, Type patch, bool postfix)
        {
            var method = AccessTools.Method(typeof(AntiHack), name);
            var info = method == null ? null : Harmony.GetPatchInfo(method);
            if (info == null) return false;
            return (postfix ? info.Postfixes : info.Prefixes).Any(p => p.PatchMethod.DeclaringType == patch) &&
                (postfix || info.Finalizers.Any(p => p.PatchMethod.DeclaringType == patch));
        }

        private static bool StallPatched()
        {
            var method = AccessTools.Method(typeof(BasePlayer), "UpdatePositionFromTick", new[] { typeof(PlayerTick), typeof(bool) });
            var info = method == null ? null : Harmony.GetPatchInfo(method);
            return info != null && info.Prefixes.Any(p => p.PatchMethod.DeclaringType == typeof(StallPatch));
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
            double now = clock.Elapsed.TotalSeconds;
            foreach (var pair in stallStartups.ToArray())
                if (pair.Value.Player == null || !pair.Value.Player.IsConnected ||
                    now < pair.Value.Started || now - pair.Value.Started >= NativeStallStartupSeconds)
                    stallStartups.Remove(pair.Key);
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

        private void StartStallStartup(BasePlayer player)
        {
            if (player == null || !player.IsConnected || player.IsNpc) return;
            if (!stallStartups.ContainsKey(player.userID) && stallStartups.Count >= SkateDiagnosticsStore.Capacity) return;
            stallStartups[player.userID] = new StallStartup { Player = player, Started = clock.Elapsed.TotalSeconds };
        }

        private void EndStallStartup(BasePlayer player)
        {
            StallStartup startup;
            if (player != null && stallStartups.TryGetValue(player.userID, out startup) && ReferenceEquals(startup.Player, player))
                stallStartups.Remove(player.userID);
        }

        private void OnPlayerConnected(BasePlayer player) { StartStallStartup(player); }
        private void OnPlayerSleepEnded(BasePlayer player) { StartStallStartup(player); }
        private void OnPlayerRespawned(BasePlayer player) { StartStallStartup(player); }
        private void OnPlayerDeath(BasePlayer player, HitInfo info)
        { EndStallStartup(player); Stop(player, SkateSessionReason.Dead); }
        private void OnPlayerSleep(BasePlayer player)
        { EndStallStartup(player); Stop(player, SkateSessionReason.Sleeping); }
        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            EndStallStartup(player);
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
        private object OnPlayerLand(BasePlayer player, float amount)
        {
            Session session;
            if (!ready || player == null || !sessions.TryGetValue(player.userID, out session) || !ReferenceEquals(player, session.Player)) return null;
            if (clock.Elapsed.TotalSeconds - session.RidingAt > RidingGraceSeconds) return null;
            return true;
        }

        private void Unload()
        {
            ReleaseDaylight();
            FinishTrace("unload");
            try { if (traceHarmony != null) traceHarmony.UnpatchAll(TracePatchId); }
            catch (Exception) { PrintWarning("Movement trace observers could not be removed."); }
            traceHarmony = null;
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
            ready = false;
            stallStartups.Clear();
            foreach (Session session in sessions.Values.ToArray()) Stop(session.Player, SkateSessionReason.Unload);
            foreach (ParkNativeRequest pending in parkNativeCleanup)
                if (pending.PaymentSuppressed) pending.CleanupReady = true;
            CleanupNativeParkCarriers();
            parkNativeRequests.Clear();
            if (parkNativeCleanup.Count != 0)
                PrintError("Unload has " + parkNativeCleanup.Count + " native park carriers with incomplete cleanup; unverified identities were left untouched and no build items were spent for those requests.");
            foreach (ParkGroup group in parkProfileCollisions.Keys.ToArray())
            {
                try
                {
                    if (!RemoveParkProfileCollision(group))
                        PrintWarning("Occupied MEGA collision restoration remains owned independently until its player bodies are clear.");
                }
                catch (Exception error)
                { PrintError("Park collision restoration could not finish during unload (" + error.GetType().Name + "); no successful restoration is claimed."); }
            }
            CleanupParkRollback();
            if (parkRollback.Count != 0) PrintError("Unload could not remove " + parkRollback.Count + " unsaved park rollback parts; cleanup is incomplete.");
            if (parkItemDeliveries.Count != 0)
                PrintError("Unload has " + parkItemDeliveries.Count + " unverified build-item deliveries. No replacement items were minted; private operator recovery may be required.");
            if (!parkDataBroken && parkData != null && SameParkWorld(parkData)) SaveParksSafely();
            optedOut.Clear();
            diagnostics.Clear();
            ClearPractice();
            if (practice.Count != 0) PrintError("Unload has " + practice.Count + " unverified practice parts; private operator recovery may be required.");
            if (ReferenceEquals(instance, this)) instance = null;
        }

        [ConsoleCommand("skate.practice.spawn")]
        private void SpawnPractice(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) { arg.ReplyWith("Use this command as an in-game administrator."); return; }
            if (!ready || !Eligible(player)) { arg.ReplyWith("Practice is unavailable; stand alive on dry ground with skating ready."); return; }
            if (practiceSpawning || practiceCleaning || practice.Count != 0)
            { arg.ReplyWith("Practice is busy or still tracked; clear the existing area before another spawn."); return; }
            practiceSpawning = true;
            try
            {
                ParkData world = NewParkData();
                if (ParkIdentityChanges(null, world).Count != 0 || !SameParkWorld(world))
                { arg.ReplyWith("Practice needs a verified current save/world identity."); return; }
                ulong owner = player.userID;
                string path = SkatePracticeLayout.FloorPrefab;
                uint prefabId = StringPool.Get(path);
                var prefab = GameManager.server.FindPrefab(path);
                var construction = PrefabAttribute.server.Find<Construction>(StringPool.Get(path));
                var stone = construction == null ? null : construction.GetGrade(BuildingGrade.Enum.Stone, 0);
                if (prefabId == 0 || StringPool.Get(prefabId) != path || prefab == null ||
                    prefab.GetComponent<BuildingBlock>() == null || construction == null ||
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
                    if (!ReferenceEquals(instance, this) || !ready || parkClosing || !SameParkWorld(world))
                        throw new InvalidDataException("Practice creation lost its current plugin or save/world.");
                    SkatePracticePart part;
                    if (!SkatePracticeLayout.TryPart(index, out part)) throw new InvalidDataException("Invalid practice layout.");
                    Vector3 position = origin + right * part.Right + forward * part.Forward + Vector3.up * part.Height;
                    Quaternion rotation = yaw * Quaternion.Euler(part.Pitch, 0, 0);
                    BaseEntity entity = GameManager.server.CreateEntity(path, position, rotation, true);
                    if (entity == null) throw new InvalidDataException("Practice floor creation failed.");
                    var receipt = new PracticeReceipt(entity, world);
                    practice.Add(receipt);
                    BuildingBlock block = entity as BuildingBlock;
                    if (block == null) throw new InvalidDataException("Practice floor is not a building part.");
                    block.enableSaving = false;
                    block.CullBushes = false;
                    block.grounded = true;
                    block.OwnerID = owner;
                    block.Spawn();
                    if (block.IsDestroyed || block.net == null || ParkId(block) == 0 ||
                        block.prefabID != prefabId || block.PrefabName != path || block.OwnerID != owner ||
                        !ReferenceEquals(BaseNetworkable.serverEntities.Find(new NetworkableId(ParkId(block))), block) ||
                        !ReferenceEquals(block.blockDefinition, construction) || block.GetParentEntity() != null ||
                        !Coordinate(block.transform.position) || !((block.transform.position - position).sqrMagnitude <= 0.000625f) ||
                        !(Quaternion.Angle(block.transform.rotation, rotation) <= 0.1f) ||
                        !ReferenceEquals(instance, this) || !ready || parkClosing || !SameParkWorld(world))
                        throw new InvalidDataException("Practice floor initialization or identity is unverified.");
                    if (!receipt.Capture() || !PracticeMatches(receipt))
                        throw new InvalidDataException("Practice floor lifetime is unverified.");
                    block.EnableSaving(false);
                    if (!PracticeUnsaved(receipt)) throw new InvalidDataException("Practice saving removal is unverified.");
                    block.SetGrade(BuildingGrade.Enum.Stone);
                    block.SetHealthToMax();
                    block.SendNetworkUpdateImmediate();
                    if (!PracticeUnsaved(receipt) || !practice.Contains(receipt))
                        throw new InvalidDataException("Practice floor lifetime changed during initialization.");
                }
                if (!ReferenceEquals(instance, this) || !ready || parkClosing || practice.Count != SkatePracticeLayout.PartCount)
                    throw new InvalidDataException("Practice creation was interrupted.");
                foreach (PracticeReceipt receipt in practice)
                    if (!PracticeUnsaved(receipt)) throw new InvalidDataException("A practice part lost its validated lifetime before completion.");
                arg.ReplyWith("Practice area spawned: two ramps, a bank, 0.4 m and 0.8 m ledges, and a long low ledge. Clear with skate.practice.clear.");
            }
            catch (Exception error)
            {
                ClearPractice();
                PrintError("Practice creation failed. " + error.Message);
                arg.ReplyWith(practice.Count == 0 ? "Practice could not be created; created parts were removed." :
                    "Practice creation failed and cleanup is incomplete; use skate.practice.clear.");
            }
            finally { practiceSpawning = false; }
        }

        [ConsoleCommand("skate.practice.clear")]
        private void PracticeClear(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin) { arg.ReplyWith("Use this command as an in-game administrator."); return; }
            ClearPractice();
            arg.ReplyWith(practice.Count == 0 ? "Practice area cleared." : "Practice cleanup is incomplete; unverified parts remain untouched. Check the server log.");
        }

        private void ClearPractice()
        {
            if (practiceCleaning) return;
            practiceCleaning = true;
            try
            {
                for (int i = practice.Count - 1; i >= 0; i--)
                {
                    PracticeReceipt receipt = practice[i];
                    try
                    {
                        if (!PracticeRemoved(receipt))
                        {
                            if (!PracticeMatches(receipt) || BaseEntity.saveList == null)
                            { PrintError("Practice part identity is unverified; it remains tracked and untouched."); continue; }
                            receipt.Entity.EnableSaving(false);
                            if (!PracticeUnsaved(receipt))
                            { PrintError("Practice part saving removal is unverified; it remains tracked."); continue; }
                            receipt.Entity.Kill();
                        }
                        if (!PracticeRemoved(receipt))
                        { PrintError("Practice part removal is unverified; it remains tracked."); continue; }
                        practice.RemoveAt(i);
                    }
                    catch (Exception error) { PrintError("Practice part cleanup failed. " + error.Message); }
                }
            }
            finally { practiceCleaning = false; }
        }

        private static bool PracticeWorld(PracticeReceipt receipt)
        {
            return receipt != null && ParkIdentityChanges(null, receipt.World).Count == 0 && SameParkWorld(receipt.World);
        }

        private static bool PracticeMatches(PracticeReceipt receipt)
        {
            if (!PracticeWorld(receipt) || receipt.Lifetime == null) return false;
            BaseEntity entity = receipt.Entity;
            PracticeLifetime lifetime = receipt.Lifetime;
            return entity != null && !entity.IsDestroyed && lifetime.Id != 0 &&
                ReferenceEquals(entity.net, lifetime.Network) && ParkId(entity) == lifetime.Id &&
                entity.creationFrame == lifetime.Frame && entity.prefabID == lifetime.Prefab && entity.OwnerID == lifetime.Owner &&
                ReferenceEquals(BaseNetworkable.serverEntities.Find(new NetworkableId(lifetime.Id)), entity);
        }

        private static bool PracticeUnsaved(PracticeReceipt receipt)
        {
            return PracticeMatches(receipt) && !receipt.Entity.enableSaving &&
                BaseEntity.saveList != null && !BaseEntity.saveList.Contains(receipt.Entity);
        }

        private static bool PracticeRemoved(PracticeReceipt receipt)
        {
            if (!PracticeWorld(receipt) || receipt.Lifetime == null || receipt.Lifetime.Id == 0) return false;
            BaseEntity entity = receipt.Entity;
            return ReferenceEquals(BaseNetworkable.serverEntities.Find(new NetworkableId(receipt.Lifetime.Id)), null) &&
                (entity == null || entity.IsDestroyed) && (ParkId(entity) == 0 || ParkId(entity) == receipt.Lifetime.Id) &&
                BaseEntity.saveList != null && !BaseEntity.saveList.Contains(entity);
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
                {
                    if (group.Wall != null && !SavedWallMatches(group.Wall, saved)) continue;
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
            }
            catch (Exception error) { PrintError("Early park support restoration failed. " + error.Message); }
        }

        private void InitializeParks()
        {
            parkReady = false;
            if (parkDataBroken || parkClosing || parkStorageFailed || parkRefreshing) return;
            parkProfileRestoreRefreshNeeded = false;
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
                    foreach (ParkGroup group in parkProfileCollisions.Keys.ToArray())
                        if (!RemoveParkProfileCollision(group))
                            throw new InvalidDataException("Previous-world park collision restoration is still pending; no new generation was adopted.");
                    if (parkData != null && parkData.Groups.Any(ParkProfileRestorePending))
                        throw new InvalidDataException("Previous-world park collision restoration is still pending; no new generation was adopted.");
                    parkData = current;
                }
                parkMembers.Clear();
                foreach (ParkGroup group in parkData.Groups.ToArray())
                {
                    if (ParkProfileRestorePending(group))
                    {
                        parkProfileRestoreRefreshNeeded = true;
                        // Keep the old group's identity intact while an earlier plugin's
                        // independent owner still controls its colliders.
                        foreach (ParkMember member in group.Members) parkMembers.Add(member.Id, group);
                        continue;
                    }
                    foreach (ParkMember member in group.Members.ToArray())
                    {
                        BuildingBlock block = FindParkBlock(member.Id);
                        if (!ParkMatches(block, group, member)) { group.Members.Remove(member); continue; }
                        block.grounded = group.Wall == null || WallMatches(FindParkBlock(group.Wall.Id), group.Wall);
                        block.InitializeSupports();
                        block.UpdateSurroundingEntities();
                        parkMembers.Add(member.Id, group);
                    }
                    if (group.Members.Count == 0) parkData.Groups.Remove(group);
                }
                foreach (ParkGroup group in parkData.Groups.ToArray())
                {
                    if (ParkProfileRestorePending(group)) continue;
                    foreach (ParkMember member in group.Members.ToArray())
                        if (!ParkMatches(FindParkBlock(member.Id), group, member))
                        { group.Members.Remove(member); parkMembers.Remove(member.Id); }
                    if (group.Members.Count == 0) parkData.Groups.Remove(group);
                }
                foreach (ParkGroup group in parkProfileCollisions.Keys.ToArray())
                    if (!parkData.Groups.Contains(group) && !RemoveParkProfileCollision(group))
                        throw new InvalidDataException("An earlier park collision restoration is still pending.");
                current = NewParkData();
                if (!SameParkGeneration(parkData, current))
                    throw new InvalidDataException("Save/world generation changed during park verification.\n" +
                        string.Join("\n", ParkIdentityChanges(parkData, current)));
                if (parkData.Checksum != current.Checksum)
                {
                    foreach (string problem in ParkIdentityChanges(parkData, current)) PrintWarning(problem);
                    parkData.Checksum = current.Checksum;
                }
                foreach (ParkGroup group in parkData.Groups)
                {
                    if (ParkProfileRestorePending(group))
                    { parkProfileRestoreRefreshNeeded = true; continue; }
                    if (ParkRollbackOwnsGroup(group)) continue;
                    SkateParkKind kind;
                    SkateParkQuarterProfile profile;
                    if (!SkateParkLayout.TryKind(group.Kind, out kind) ||
                        (!ParkOwnedHalfpipe(kind) && !SkateParkLayout.TryQuarterProfile(kind, out profile))) continue;
                    if (ParkOwnedHalfpipe(kind))
                    {
                        // An altered saved kit keeps native collision without blocking other parks.
                        try
                        {
                            var profileFailures = new List<string>();
                            ParkNativePart[] profileDefinitions;
                            if (!ParkPrefabs(kind, profileFailures, out profileDefinitions))
                                throw new InvalidDataException("Halfpipe collision prefab verification failed.");
                            EnsureParkProfileCollision(kind, group, group.Members, profileDefinitions[0].Construction.bounds);
                        }
                        catch (ParkProfileActivationOccupiedException)
                        {
                            // Only this group's new fill is deferred; no body or native collider moved.
                            // The controlled cleanup pass retries quietly after the body leaves.
                            parkProfileRestoreRefreshNeeded = true;
                        }
                        catch (ParkProfileRestorePendingException)
                        {
                            parkProfileRestoreRefreshNeeded = true;
                            PrintWarning("Saved halfpipe collision restoration is independently owned; replacement verification will retry after body clearance.");
                        }
                        catch (ParkProfileChangedException)
                        {
                            if (!RemoveParkProfileCollision(group))
                            {
                                parkProfileRestoreRefreshNeeded = true;
                                PrintWarning("An altered saved halfpipe group is waiting for independently owned collision restoration.");
                            }
                            else PrintWarning("A saved halfpipe group has altered native grades, skins or scale; its original floor collision is retained.");
                        }
                        catch (Exception error)
                        {
                            bool restored = RemoveParkProfileCollision(group);
                            if (!restored) parkProfileRestoreRefreshNeeded = true;
                            PrintWarning("Saved halfpipe profile creation failed (" + error.GetType().Name + "); " +
                                (restored ? "native floor collision is retained for that group." : "independently owned collision restoration is pending."));
                        }
                        continue;
                    }
                    var failures = new List<string>();
                    ParkNativePart[] definitions;
                    if (!ParkPrefabs(kind, failures, out definitions))
                        throw new InvalidDataException(string.Join("\n", failures));
                    EnsureParkProfileCollision(kind, group, group.Members, definitions[0].Construction.bounds);
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
            { arg.ReplyWith("Usage: skate.piece <bank|kicker|quarterpipe|ledge-low|ledge-high|long-ledge|funbox|halfpipe|mini-quarterpipe|large-quarterpipe|mega-halfpipe|cancel>."); return; }
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
            { arg.ReplyWith("Unknown park piece. Use bank, kicker, quarterpipe, ledge-low, ledge-high, long-ledge, funbox, halfpipe, mini-quarterpipe, large-quarterpipe, half-wall-kicker or mega-halfpipe."); return; }
            if (kind == SkateParkKind.HalfWallKicker)
            { ReplyParkCommand(arg, kind, "Use the Half-wall Kicker build item and aim at an existing half wall."); return; }
            string failure;
            if (!Eligible(player))
            { ReplyParkCommand(arg, kind, "Park placement is unavailable; be alive, awake, unmounted and on dry land."); return; }
            object epoch = parkSelectionEpoch = new object();
            if (!RefreshParkWorld(out failure)) { ReplyParkCommand(arg, kind, failure); return; }
            if (!ReferenceEquals(epoch, parkSelectionEpoch) || !ReferenceEquals(instance, this) || parkClosing ||
                !player.IsAdmin || !Eligible(player))
            { ReplyParkCommand(arg, kind, "The selection request was superseded or the administrator became unavailable."); return; }
            PruneParkSelections();
            if (parkData.Groups.Count >= MaximumParkGroups ||
                (!parkSelections.ContainsKey(player.userID) && parkSelections.Count >= MaximumParkSelections))
            { ReplyParkCommand(arg, kind, "The park group or pending-selection limit has been reached."); return; }
            SkateParkSelectionState selected;
            if (!SkateParkSelections.TrySelect(kind, true, true, clock.Elapsed.TotalSeconds, out selected))
            { ReplyParkCommand(arg, kind, "Park selection time is unavailable."); return; }
            parkSelections[player.userID] = new ParkSelection { Player = player, State = selected };
            ReplyParkCommand(arg, kind, "Selected " + SkateParkLayout.Name(kind) + ". Place one normal horizontal square foundation with a building plan within 120 seconds. Failed attempts retain this selection until its original expiry; " +
                (kind == SkateParkKind.MegaHalfpipe ? "the MEGA kit requires its matching client profile before riding." :
                 kind == SkateParkKind.Halfpipe ? "the halfpipe uses a Stone flat, Metal transitions/decks/sides and Wood back rails." : "the assembly will be Stone."));
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
                if (candidate == null || !candidate.isServer || TaggedParkItem(candidate)) continue;
                Planner existing = candidate.GetHeldEntity() as Planner;
                if (!ParkPlannerItem(player, belt, candidate, existing)) continue;
                if (planner == null || ReferenceEquals(existing, active)) { item = candidate; planner = existing; }
                if (ReferenceEquals(existing, active)) break;
            }
            return planner != null;
        }

        private static Item ParkPlannerOwnerItem(Planner planner)
        {
            BasePlayer player = planner == null ? null : planner.GetOwnerPlayer();
            ItemContainer belt = player == null || player.inventory == null ? null : player.inventory.containerBelt;
            if (belt == null || belt.itemList == null || belt.itemList.Count > 32) return null;
            foreach (Item candidate in belt.itemList)
                if (ParkPlannerItem(player, belt, candidate, planner)) return candidate;
            return null;
        }

        private static bool TaggedParkItem(Item item)
        {
            return item != null && item.info != null && item.info.shortname == SkateParkBuildItems.Carrier &&
                item.instanceData != null && SkateParkBuildItems.Tagged(item.instanceData.dataInt);
        }

        private static bool ParkItemIdentity(Item item, out SkateParkKind kind)
        {
            kind = default(SkateParkKind);
            return item != null && item.isServer && !item.IsRemoved() && item.info != null && item.instanceData != null &&
                SkateParkBuildItems.TryIdentity(item.info.shortname, item.skin, item.instanceData.dataInt,
                item.name, item.amount, out kind) && !item.uid.Equals(default(ItemId));
        }

        private object CanStackItem(Item item, Item other)
        { return TaggedParkItem(item) || TaggedParkItem(other) ? (object)false : null; }

        private object OnMaxStackable(Item item)
        { return TaggedParkItem(item) ? (object)1 : null; }

        private object CanBuild(Planner planner, Construction construction, Construction.Target target)
        {
            ParkPlacementRequest request = parkBuildHookRequest;
            if (request != null && parkBusy && CurrentParkRequest(request) && ReferenceEquals(request.Planner, planner) &&
                ReferenceEquals(parkBuildHookConstruction, construction) && ReferenceEquals(target.player, request.Player) &&
                target.Equals(parkBuildHookTarget)) return null;
            BasePlayer player = planner == null ? null : planner.GetOwnerPlayer();
            ParkNativeRequest pending = PendingNativeParkRequest(player, planner);
            if (pending == null)
            {
                if (!TaggedNativeParkPlanner(player, planner)) return null;
                ReportParkFailures(player, new[] { "Park placement needs the original native affordability receipt; the build item was retained." });
                return false;
            }
            if (pending.BuildChecked || pending.Admitted) return false;
            pending.BuildChecked = true;
            request = pending.Placement;
            if (!ReferenceEquals(request.Player, player) || !ReferenceEquals(request.Planner, planner) ||
                !ReferenceEquals(pending.Construction, construction) || !NativeParkConstruction(construction) ||
                !CurrentParkRequest(request) || parkBusy || parkRefreshing || parkNativeCleaning)
            {
                pending.Failure = "The original administrator, building plan, build item, native construction or world changed after affordability; placement was refused.";
                ReportParkFailures(request.Player, new[] { pending.Failure });
                return false;
            }
            if (request.ItemKind == SkateParkKind.HalfWallKicker)
            {
                parkNativeRequests.Remove(request.Owner);
                PlaceNativeWallKicker(request, construction, target);
                return false;
            }
            if (!target.valid || !ReferenceEquals(target.player, player) || target.shouldParent ||
                (target.entity != null && target.entity.SupportsChildDeployables() && target.entity.ForceDeployableSetParent()) ||
                !Coordinate(target.position) || !Coordinate(target.rotation) ||
                !Coordinate(target.ray.origin) || !Coordinate(target.ray.direction))
            {
                pending.Failure = "Park placement needs a valid unparented native square-foundation target; the build item was retained.";
                ReportParkFailures(player, new[] { pending.Failure });
                return false;
            }
            CaptureParkSupport(request, target.entity as BuildingBlock);
            if (target.socket == null && target.entity is BuildingBlock &&
                TrySupportedNativeTarget(request, construction, target))
            {
                parkNativeRequests.Remove(request.Owner);
                return false;
            }
            pending.Admitted = true;
            return null;
        }

        private ParkNativeRequest PendingNativeParkRequest(BasePlayer player, Planner planner)
        {
            ParkNativeRequest pending;
            if (player != null && parkNativeRequests.TryGetValue(player.userID, out pending)) return pending;
            foreach (ParkNativeRequest candidate in parkNativeRequests.Values)
                if (ReferenceEquals(candidate.Placement.Planner, planner)) return candidate;
            return null;
        }

        private static bool TaggedNativeParkPlanner(BasePlayer player, Planner planner)
        {
            return (player != null && TaggedParkItem(player.GetActiveItem())) || TaggedParkItem(ParkPlannerOwnerItem(planner));
        }

        private static bool NativeParkConstruction(Construction construction)
        {
            uint id = StringPool.Get(ParkFoundationPrefab);
            return construction != null && id != 0 && StringPool.Get(id) == ParkFoundationPrefab &&
                PrefabAttribute.server != null && ReferenceEquals(PrefabAttribute.server.Find<Construction>(id), construction);
        }

        private bool TryNativeParkRequest(BasePlayer player, Planner planner, Construction construction,
            out ParkPlacementRequest request, out string failure)
        {
            request = null;
            failure = "Select a stock square foundation while holding the marked park build item; other native building pieces cannot place a park assembly.";
            if (!NativeParkConstruction(construction)) return false;
            failure = "Park placement is busy or unavailable; the build item was retained.";
            if (!ReferenceEquals(instance, this) || parkClosing || parkBusy || parkRefreshing || parkNativeCleaning ||
                parkNativeRequests.Count != 0 || SaveRestore.IsSaving || player == null || !player.IsAdmin ||
                !Eligible(player) || player.eyes == null || !Coordinate(player.eyes.position)) return false;
            CleanupNativeParkCarriers();
            if (parkNativeCleanup.Count != 0)
            { failure = "An earlier native carrier removal is unverified; park placement is disabled and the build item was retained."; return false; }
            if (!RefreshParkWorld(out failure)) return false;
            if (parkData.Groups.Count >= MaximumParkGroups)
            { failure = "Park group limit reached: 256; the build item was retained."; return false; }
            Item item = player.GetActiveItem();
            ItemContainer belt = player.inventory == null ? null : player.inventory.containerBelt;
            SkateParkKind kind;
            if (!ParkItemIdentity(item, out kind) || !ParkPlannerItem(player, belt, item, planner) ||
                !ReferenceEquals(player.GetHeldEntity(), planner))
            { failure = "Hold one unchanged park build item owned by this administrator; placement was refused."; return false; }
            request = new ParkPlacementRequest { Player = player, Planner = planner, PlannerItem = item, Belt = belt,
                Data = parkData, BuildItem = true, ItemKind = kind, ItemUid = item.uid, ItemDefinition = item.info,
                ItemData = item.instanceData, Owner = player.userID };
            if (!CurrentParkRequest(request) || parkNativeRequests.Count != 0 || parkBusy || parkNativeCleaning ||
                !NativeParkConstruction(construction))
            { request = null; failure = "The administrator, build item, native construction or world changed; the build item was retained."; return false; }
            failure = null;
            return true;
        }

        private object CanAffordToPlace(BasePlayer player, Planner planner, Construction construction)
        {
            if (PendingNativeParkRequest(player, planner) != null) return false;
            if (!TaggedNativeParkPlanner(player, planner)) return null;
            ParkPlacementRequest request;
            string failure;
            bool allowed = TryNativeParkRequest(player, planner, construction, out request, out failure);
            if (!allowed) { ReportParkFailures(player, new[] { failure }); return false; }
            var pending = new ParkNativeRequest { Placement = request, Construction = construction };
            parkNativeRequests.Add(request.Owner, pending);
            try { NextTick(() => FinishNativeParkRequest(pending)); }
            catch (Exception error)
            {
                parkNativeRequests.Remove(request.Owner);
                ReportParkFailures(player, new[] { "Native park affordability could not be scheduled; the build item was retained. " + error.Message });
                return false;
            }
            return true;
        }

        private object OnPayForPlacement(BasePlayer player, Planner planner, Construction construction)
        {
            ParkNativeRequest pending = PendingNativeParkRequest(player, planner);
            if (pending == null || !pending.Admitted || !ReferenceEquals(pending.Placement.Player, player) ||
                player == null || player.userID != pending.Placement.Owner ||
                !ReferenceEquals(pending.Placement.Planner, planner) || !ReferenceEquals(pending.Construction, construction)) return null;
            // This bill belongs to the admitted native request even if a later callback changes the active item or authority.
            pending.PaymentSuppressed = true;
            return true;
        }

        private bool ParkBuildingHook(ParkPlacementRequest request, Construction construction, Construction.Target target)
        {
            if (parkBuildHookRequest != null || !CurrentParkRequest(request)) return false;
            parkBuildHookRequest = request;
            parkBuildHookConstruction = construction;
            parkBuildHookTarget = target;
            try { return Interface.CallHook("CanBuild", request.Planner, construction, target) == null; }
            finally
            {
                parkBuildHookRequest = null;
                parkBuildHookConstruction = null;
                parkBuildHookTarget = new Construction.Target();
            }
        }

        private static Item CreateParkItem(SkateParkKind kind)
        {
            string name = SkateParkBuildItems.Name(kind);
            if (name == null) return null;
            Item item = ItemManager.CreateByName(SkateParkBuildItems.Carrier, 1, 0);
            if (item == null) return null;
            try
            {
                ProtoBuf.Item.InstanceData data = item.instanceData ?? Facepunch.Pool.Get<ProtoBuf.Item.InstanceData>();
                data.ShouldPool = false;
                data.dataInt = SkateParkBuildItems.Marker(kind);
                item.instanceData = data;
                item.name = name;
                item.MarkDirty();
                SkateParkKind observed;
                if (!ParkItemIdentity(item, out observed) || observed != kind)
                    throw new InvalidDataException("The native building-plan item identity is unavailable.");
                return item;
            }
            catch { item.Remove(); throw; }
        }

        private static bool SameParkDeliveryItem(ParkItemDelivery delivery)
        {
            SkateParkKind kind;
            return delivery != null && delivery.Item != null && delivery.Item.uid.Equals(delivery.Uid) &&
                ParkItemIdentity(delivery.Item, out kind) && kind == delivery.Kind;
        }

        private static bool ParkDeliveryComplete(ParkItemDelivery delivery)
        {
            if (!SameParkDeliveryItem(delivery)) return false;
            Item item = delivery.Item;
            if (item.parent != null && delivery.Player != null && delivery.Player.userID == delivery.Owner &&
                ReferenceEquals(item.GetOwnerPlayer(), delivery.Player)) return true;
            WorldItem dropped = item.GetWorldEntity() as WorldItem;
            return dropped != null && !dropped.IsDestroyed && dropped.net != null &&
                ReferenceEquals(dropped.item, item) && Coordinate(dropped.transform.position);
        }

        private bool DeliverParkItem(ParkItemDelivery delivery)
        {
            if (delivery == null || delivery.Delivering) return false;
            delivery.Delivering = true;
            try
            {
                if (ParkDeliveryComplete(delivery)) return true;
                if (!ReferenceEquals(instance, this) || parkClosing || !SameParkDeliveryItem(delivery) ||
                    delivery.Player == null || !delivery.Player.IsConnected || !delivery.Player.IsAdmin ||
                    delivery.Player.userID != delivery.Owner || delivery.Player.inventory == null ||
                    (delivery.Data != null && (!ReferenceEquals(delivery.Data, parkData) || !SameParkWorld(delivery.Data)))) return false;
                Item item = delivery.Item;
                if (item.parent != null || !ReferenceEquals(item.GetWorldEntity(), null)) return false;
                // Check actual ownership/world placement after callbacks, rather than trusting GiveItem's bool alone.
                delivery.Player.inventory.GiveItem(item);
                if (ParkDeliveryComplete(delivery)) return true;
                if (!ReferenceEquals(instance, this) || parkClosing || !SameParkDeliveryItem(delivery) ||
                    delivery.Player == null || !delivery.Player.IsConnected || !delivery.Player.IsAdmin || delivery.Player.userID != delivery.Owner ||
                    (delivery.Data != null && (!ReferenceEquals(delivery.Data, parkData) || !SameParkWorld(delivery.Data))) ||
                    item.parent != null || !ReferenceEquals(item.GetWorldEntity(), null) || !Coordinate(delivery.Position)) return false;
                item.Drop(delivery.Position, Vector3.zero, Quaternion.identity);
                return ParkDeliveryComplete(delivery);
            }
            finally { delivery.Delivering = false; }
        }

        private bool GiveParkItem(BasePlayer player, SkateParkKind kind, out string failure, ParkData expected = null)
        {
            failure = "Build-item delivery is unavailable; no replacement item will be minted automatically.";
            if (!ReferenceEquals(instance, this) || parkClosing || player == null || !player.IsAdmin ||
                !player.IsConnected || player.inventory == null || !Coordinate(player.transform.position) ||
                parkItemDeliveries.Count >= MaximumParkGroups ||
                (expected != null && (!ReferenceEquals(expected, parkData) || !SameParkWorld(expected)))) return false;
            ulong owner = player.userID;
            Item item;
            try { item = CreateParkItem(kind); }
            catch (Exception error) { failure = "Build-item creation failed; no second item was created. " + error.Message; return false; }
            if (item == null) return false;
            var delivery = new ParkItemDelivery { Item = item, Uid = item.uid, Kind = kind,
                Player = player, Owner = owner, Data = expected, Position = player.transform.position + Vector3.up };
            parkItemDeliveries.Add(delivery);
            try
            {
                if (!DeliverParkItem(delivery))
                { failure = "Build-item delivery was not verified. The same item remains tracked for retry; no second item was created."; return false; }
                parkItemDeliveries.Remove(delivery);
                failure = null;
                return true;
            }
            catch (Exception error)
            { failure = "Build-item delivery was not verified: " + error.Message + ". The same item remains tracked; no second item was created."; return false; }
        }

        [ConsoleCommand("skate.items")]
        private void GiveParkItems(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin || !Eligible(player))
            { arg.ReplyWith("Use this command as an alive, awake, unmounted in-game administrator on dry land."); return; }
            if (arg.Args == null || arg.Args.Length != 1)
            { arg.ReplyWith("Usage: skate.items <bank|kicker|quarterpipe|ledge-low|ledge-high|long-ledge|funbox|halfpipe|mini-quarterpipe|large-quarterpipe|mega-halfpipe|all>."); return; }
            if (parkBusy || parkClosing || parkNativeRequests.Count != 0 || parkNativeCleaning)
            { arg.ReplyWith("Park placement or removal is busy; try again shortly."); return; }
            SkateParkKind selected = SkateParkKind.Bank;
            bool all = string.Equals(arg.GetString(0), "all", StringComparison.OrdinalIgnoreCase);
            if (!all && !SkateParkLayout.TryKind(arg.GetString(0), out selected))
            { arg.ReplyWith("Unknown park build item."); return; }
            parkBusy = true;
            try
            {
                foreach (ParkItemDelivery delivery in parkItemDeliveries.ToArray())
                {
                    if (player.userID != delivery.Owner) continue;
                    if (delivery.Delivering)
                    { ReplyParkCommand(arg, all ? SkateParkKind.MegaHalfpipe : selected, "An earlier build-item delivery is still unverified; no new items were issued."); return; }
                    Vector3 position = player.transform.position + Vector3.up;
                    if (player.inventory == null || !Coordinate(position))
                    { ReplyParkCommand(arg, all ? SkateParkKind.MegaHalfpipe : selected, "The earlier build-item delivery cannot be retried from the current inventory or position; no new items were issued."); return; }
                    delivery.Player = player;
                    delivery.Position = position;
                    if (!DeliverParkItem(delivery))
                    { ReplyParkCommand(arg, all ? SkateParkKind.MegaHalfpipe : selected, "An earlier build-item delivery is still unverified; no new items were issued."); return; }
                    parkItemDeliveries.Remove(delivery);
                }
                foreach (SkateParkKind kind in Enum.GetValues(typeof(SkateParkKind)))
                {
                    if (!all && kind != selected) continue;
                    string failure;
                    if (!GiveParkItem(player, kind, out failure)) { ReplyParkCommand(arg, all ? SkateParkKind.MegaHalfpipe : selected, failure); return; }
                }
                ReplyParkCommand(arg, all ? SkateParkKind.MegaHalfpipe : selected, all ? "Issued one of each park build item. Hold an item to select that park piece." :
                    "Issued " + SkateParkBuildItems.Name(selected) + ". Hold it to select this park piece.");
            }
            catch (Exception error) { ReplyParkCommand(arg, all ? SkateParkKind.MegaHalfpipe : selected, "Build-item issue stopped: " + error.Message); }
            finally { parkBusy = false; }
        }

        private void CaptureParkSupport(ParkPlacementRequest request, BuildingBlock support)
        {
            if (support == null || support.IsDestroyed || support.net == null || support.GetParentEntity() != null ||
                ParkId(support) == 0 || parkData.Groups.Any(group => group.Members.Any(member => member.Id == ParkId(support)))) return;
            request.Support = support;
            request.SupportId = ParkId(support);
            request.SupportNetwork = support.net;
            request.SupportPrefab = support.prefabID;
            request.SupportFrame = support.creationFrame;
            request.SupportSnapshot = CaptureParkAnchor(support);
        }

        private bool CurrentParkSupport(ParkPlacementRequest request)
        {
            if (ReferenceEquals(request.Support, null)) return true;
            BuildingBlock support = request.Support;
            ParkAnchorSnapshot snapshot = request.SupportSnapshot;
            return parkData != null && parkData.Groups != null && support != null && !support.IsDestroyed && snapshot != null &&
                ReferenceEquals(support.net, request.SupportNetwork) && ReferenceEquals(FindParkBlock(request.SupportId), support) &&
                support.prefabID == request.SupportPrefab && support.creationFrame == request.SupportFrame &&
                support.GetParentEntity() == null && support.OwnerID == snapshot.Owner && support.buildingID == snapshot.Building &&
                support.transform.position == snapshot.Position && Quaternion.Angle(support.transform.rotation, snapshot.Rotation) < 0.01f &&
                !parkData.Groups.Any(group => group.Members.Any(member => member.Id == request.SupportId));
        }

        private bool CurrentParkRequest(ParkPlacementRequest request)
        {
            if (request == null || !ReferenceEquals(instance, this) || parkClosing || !parkReady || !CurrentParkSupport(request) ||
                SaveRestore.IsSaving || !ReferenceEquals(parkData, request.Data) || !SameParkWorld(request.Data) ||
                request.Player == null || request.Player.userID != request.Owner || !request.Player.IsAdmin || !Eligible(request.Player)) return false;
            if (request.Wall != null)
            {
                SkateParkTransform wallAnchor;
                if (!ReferenceEquals(FindParkBlock(request.Wall.Id), request.WallBlock) ||
                    !WallMatches(request.WallBlock, request.Wall) || !WallPermission(request.Player, request.WallBlock) ||
                    request.Player.eyes == null || !Coordinate(request.Player.eyes.position) ||
                    !TryWallAnchor(request.Wall, out wallAnchor) || Vector3.Distance(request.Player.eyes.position,
                        new Vector3((float)wallAnchor.X, (float)wallAnchor.Y, (float)wallAnchor.Z)) > MaximumParkPlacementDistance)
                    return false;
            }
            if (request.SnapSnapshot != null && !ParkSnapCurrent(request)) return false;
            if (request.BuildItem && request.ItemSpent)
                return SameParkPaymentItem(request) && request.PlannerItem.amount == 0;
            if (request.Planner == null || request.Planner.IsDestroyed ||
                !ReferenceEquals(request.Planner.GetOwnerPlayer(), request.Player)) return false;
            if (request.BuildItem)
            {
                SkateParkKind kind;
                return SameParkPaymentItem(request) &&
                    ParkItemIdentity(request.PlannerItem, out kind) && kind == request.ItemKind &&
                    ParkPlannerItem(request.Player, request.Belt, request.PlannerItem, request.Planner) &&
                    ReferenceEquals(request.Player.GetActiveItem(), request.PlannerItem) &&
                    ReferenceEquals(request.Player.GetHeldEntity(), request.Planner);
            }
            if (request.Selection != null)
                return CurrentParkSelection(request.Player, request.Selection) &&
                    ParkFoundationMatches(request.Foundation, request.FoundationId, request.Snapshot);
            return ParkPlannerItem(request.Player, request.Belt, request.PlannerItem, request.Planner);
        }

        private bool CurrentParkPhase(ParkPlacementRequest request)
        {
            return CurrentParkRequest(request) && (request.Supports == null || ParkSupportsCurrent(request));
        }

        [ConsoleCommand("skate.placeitem")]
        private void PlaceParkBuildItem(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            Planner planner = player == null ? null : player.GetHeldEntity() as Planner;
            Item item = player == null ? null : player.GetActiveItem();
            SkateParkKind kind;
            if (player == null || !player.IsAdmin || planner == null || item == null ||
                !ReferenceEquals(item.GetHeldEntity(), planner) || !ParkItemIdentity(item, out kind))
            { arg.ReplyWith("Hold an unchanged park build item as an in-game administrator."); return; }
            if (arg.Args != null && arg.Args.Length != 0 && arg.Args.Length != 4)
            { arg.ReplyWith("Usage: skate.placeitem [x y z yaw]."); return; }
            var arguments = new string[(arg.Args == null ? 0 : arg.Args.Length) + 1];
            arguments[0] = SkateParkLayout.Name(kind);
            for (int i = 1; i < arguments.Length; i++) arguments[i] = arg.GetString(i - 1);
            PlaceParkRequest(arg, arguments, true);
        }

        [ConsoleCommand("skate.place")]
        private void PlaceParkPiece(ConsoleSystem.Arg arg)
        {
            string[] arguments = null;
            if (arg.Args != null && (arg.Args.Length == 1 || arg.Args.Length == 5))
            {
                arguments = new string[arg.Args.Length];
                for (int i = 0; i < arguments.Length; i++) arguments[i] = arg.GetString(i);
            }
            PlaceParkRequest(arg, arguments, false);
        }

        private void PlaceParkRequest(ConsoleSystem.Arg arg, string[] arguments, bool itemOnly)
        {
            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin)
            { arg.ReplyWith("Use this command as an in-game administrator."); return; }
            SkateParkKind kind;
            bool explicitAnchor;
            double x, y, z, yaw;
            string failure;
            if (!SkateParkPlacement.TryRequest(arguments, out kind, out explicitAnchor, out x, out y, out z, out yaw, out failure))
            { ReplyParkCommand(arg, kind, failure); return; }
            try
            {
                if (!Eligible(player) || player.eyes == null)
                { ReplyParkCommand(arg, kind, "Park placement is unavailable; be alive, awake, unmounted and on dry land."); return; }
                if (parkBusy || parkRefreshing || parkClosing || parkNativeRequests.Count != 0 || parkNativeCleaning || SaveRestore.IsSaving)
                { ReplyParkCommand(arg, kind, "Park placement is busy or the world is being saved; try again shortly."); return; }
                if (!RefreshParkWorld(out failure)) { ReplyParkCommand(arg, kind, failure); return; }
                ItemContainer belt;
                Item item;
                Planner planner;
                Planner held = player.GetHeldEntity() as Planner;
                Item heldItem = player.GetActiveItem();
                bool buildItem = TaggedParkItem(heldItem);
                if (itemOnly && !buildItem)
                { ReplyParkCommand(arg, kind, "Keep the selected park build item active until placement completes."); return; }
                if (buildItem)
                {
                    SkateParkKind observed;
                    belt = player.inventory == null ? null : player.inventory.containerBelt;
                    item = heldItem; planner = held;
                    if (!ParkItemIdentity(item, out observed) || observed != kind || !ParkPlannerItem(player, belt, item, planner))
                    { ReplyParkCommand(arg, kind, "The active park build item identity, amount, kind or owner changed; placement refused."); return; }
                }
                else if (!FindParkPlanner(player, out belt, out item, out planner) || TaggedParkItem(item))
                { ReplyParkCommand(arg, kind, "Park placement needs an existing building plan in your belt for native building-hook checks. Hold the plan once if it has not initialized."); return; }
                Vector3 eye = player.eyes.position;
                if (!Coordinate(eye)) { ReplyParkCommand(arg, kind, "The server eye position is unavailable."); return; }
                var failures = new List<string>();
                ParkNativePart[] definitions;
                ParkPrefabs(kind, failures, out definitions);
                if (failures.Count != 0) { ReplyParkCommand(arg, kind, string.Join("\n", failures)); return; }
                ParkGroup wallGroup = null;
                var wallRequest = new ParkPlacementRequest { Player = player, Planner = planner, PlannerItem = item,
                    Belt = belt, Data = parkData, BuildItem = buildItem, ItemKind = kind, ItemUid = item.uid,
                    ItemDefinition = item.info, ItemData = item.instanceData, Owner = player.userID };
                if (kind == SkateParkKind.HalfWallKicker)
                {
                    if (explicitAnchor) { ReplyParkCommand(arg, kind, "Half-wall kickers require an aimed placed half wall; free anchors are not supported."); return; }
                    if (!TryWallPlacement(wallRequest, new Ray(eye, player.eyes.HeadForward()), null, definitions, out wallGroup, out failure))
                    { ReplyParkCommand(arg, kind, failure); return; }
                    x = wallGroup.AnchorX; y = wallGroup.AnchorY; z = wallGroup.AnchorZ; yaw = wallGroup.AnchorYaw;
                }
                else if (!explicitAnchor)
                {
                    Vector3 direction = player.eyes.HeadForward();
                    int mask = LayerMask.GetMask("World", "Terrain", "Construction", "Deployed", "Player (Server)");
                    RaycastHit hit;
                    if (!Coordinate(direction) || direction.sqrMagnitude < 0.0001f || mask == 0 ||
                        LayerMask.NameToLayer("Terrain") < 0 || LayerMask.NameToLayer("Construction") < 0 ||
                        !Physics.Raycast(eye, direction.normalized, out hit, MaximumParkPlacementDistance, mask, QueryTriggerInteraction.Ignore) ||
                        !Coordinate(hit.point) || !Coordinate(hit.normal) || hit.normal.y <= 0)
                    { ReplyParkCommand(arg, kind, "Aim at an upward-facing terrain or construction surface within 25 metres."); return; }
                    Vector3 viewAngles = player.eyes.rotation.eulerAngles;
                    if (!Coordinate(viewAngles))
                    { ReplyParkCommand(arg, kind, "The server view heading is unavailable."); return; }
                    Bounds bounds = definitions[0].Construction.bounds;
                    if (!SkateParkLayout.TrySupportedAnchor(kind, hit.point.y, bounds.center.y, bounds.center.z,
                        bounds.size.y, bounds.size.z, out y))
                    { ReplyParkCommand(arg, kind, "The aim surface or supported native layout height is outside world-coordinate bounds."); return; }
                    CaptureParkSupport(wallRequest, hit.collider == null ? null : hit.collider.ToBaseEntity() as BuildingBlock);
                    x = hit.point.x; z = hit.point.z;
                    yaw = (viewAngles.y % 360 + 360) % 360;
                    BuildingBlock host = hit.collider == null ? null : hit.collider.ToBaseEntity() as BuildingBlock;
                    if (host != null && (HorizontalParkFoundation(host) || parkMembers.ContainsKey(ParkId(host))) &&
                        !TryParkTargetFrame(wallRequest, host, hit, yaw, false, definitions, out x, out z, out yaw, out failure))
                    { ReplyParkCommand(arg, kind, failure); return; }
                }
                if (kind != SkateParkKind.HalfWallKicker && wallRequest.SnapSnapshot == null &&
                    !SkateParkLayout.TrySnapYaw(yaw, 0, ParkRotationStep(), out yaw))
                { ReplyParkCommand(arg, kind, "The native foundation rotation step is unavailable."); return; }
                Vector3 anchor = new Vector3((float)x, (float)y, (float)z);
                if (!Coordinate(anchor) || Vector3.Distance(eye, anchor) > MaximumParkPlacementDistance)
                { ReplyParkCommand(arg, kind, "The park anchor must be within 25 metres of your server eye position."); return; }
                var request = wallRequest;
                var group = wallGroup ?? new ParkGroup { Id = Guid.NewGuid().ToString("N"), Owner = player.userID,
                    Kind = SkateParkLayout.Name(kind), AnchorX = x, AnchorY = y, AnchorZ = z, AnchorYaw = yaw,
                    Members = new List<ParkMember>(), BuildItemState = buildItem ? SkateParkBuildItemState.Preparing : SkateParkBuildItemState.None };
                if (kind != SkateParkKind.HalfWallKicker &&
                    !ResolveParkSupport(request, kind, definitions, group, out failure))
                { ReplyParkCommand(arg, kind, failure); return; }
                if (!BuildParkAssembly(request, kind, definitions, group, out failure))
                { ReplyParkCommand(arg, kind, "Park " + group.Kind + " placement refused: " + failure); return; }
                ReplyParkCommand(arg, kind, ParkPlacementReply(group));
            }
            catch (Exception error) { ReplyParkCommand(arg, kind, "Park placement refused: " + error.Message); }
        }

        private void ReplyParkCommand(ConsoleSystem.Arg arg, SkateParkKind kind, string message)
        {
            if (kind == SkateParkKind.MegaHalfpipe) PrintWarning("Park kind=mega-halfpipe part=command: " + ParkSafeDiagnostic(message));
            arg.ReplyWith(message);
        }

        private void ReplyParkTarget(ParkPlacementRequest request, string message)
        {
            if (request.ItemKind == SkateParkKind.MegaHalfpipe)
                PrintWarning("Park kind=mega-halfpipe part=target: " + ParkSafeDiagnostic(message));
            if (request.Player != null && request.Player.IsConnected) request.Player.ConsoleMessage(message);
        }

        private void ParkPlacementFeedback(BasePlayer player, ParkGroup group, string suffix = "")
        {
            string message = ParkPlacementReply(group) + suffix;
            if (group.Kind == "mega-halfpipe") PrintWarning("Park kind=mega-halfpipe part=assembly: " + ParkSafeDiagnostic(message));
            if (player != null && player.IsConnected) player.ConsoleMessage(message);
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
                if (ParkOwnedHalfpipe(kind))
                {
                    ParkProfileCollision collision;
                    bool healthy = false;
                    try
                    {
                        healthy = parkProfileCollisions.TryGetValue(group, out collision) &&
                            ParkMegaCollisionHealthy(group, collision);
                    }
                    catch (Exception) { }
                    text.Append("; collision=").Append(healthy ? "smooth-verified" : "native-or-unverified");
                }
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
            ParkNativeRequest native = PendingNativeParkRequest(player, planner);
            if (native != null && native.Admitted && ReferenceEquals(native.Placement.Planner, planner))
            {
                CaptureNativeParkCarrier(native, gameObject);
                return;
            }
            if (settings != null && settings.GroundedBuilding && gameObject != null)
            {
                try { GroundBuiltEntity(player, gameObject.GetComponent<StabilityEntity>()); }
                catch (Exception error) { PrintError("Grounded building registration was unavailable. " + error.Message); }
            }
            if (player == null) return;
            if (TaggedParkItem(ParkPlannerOwnerItem(planner))) return;
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

        private void CaptureNativeParkCarrier(ParkNativeRequest pending, GameObject gameObject)
        {
            BaseEntity observed = null;
            ulong observedId = 0;
            string observationFailure = null;
            try
            {
                observed = gameObject == null ? null : gameObject.GetComponent<BaseEntity>();
                observedId = ParkId(observed);
            }
            catch (Exception error) { observationFailure = "Native carrier observation failed: " + error.Message; }
            bool added, primary;
            ParkNativeRequest receipt = RecordNativeParkObservation(pending, gameObject, observed, observedId, out added, out primary);
            if (!added) return;
            if (!primary)
            {
                pending.Failure = "More than one native carrier observation was reported for the pending request.";
                receipt.Failure = observationFailure;
                QuarantineNativeParkCarrier(receipt);
                return;
            }
            if (observationFailure != null)
            {
                pending.Failure = observationFailure;
                QuarantineNativeParkCarrier(pending);
                return;
            }
            try
            {
                BuildingBlock carrier = gameObject == null ? null : gameObject.GetComponent<BuildingBlock>();
                ulong id = ParkId(carrier);
                if (carrier == null || carrier.IsDestroyed || id == 0 || carrier.PrefabName != ParkFoundationPrefab ||
                    carrier.prefabID != StringPool.Get(ParkFoundationPrefab) || !ReferenceEquals(FindParkBlock(id), carrier))
                {
                    pending.Failure = "The spawned native square-foundation carrier reference, network ID or prefab could not be verified.";
                    QuarantineNativeParkCarrier(pending);
                    return;
                }
                pending.Carrier = carrier;
                pending.CarrierId = id;
                pending.CarrierPrefab = carrier.prefabID;
                pending.CarrierOwner = carrier.OwnerID;
                pending.CarrierConstruction = carrier.blockDefinition;
                pending.Parent = carrier.GetParentEntity();
                pending.ParentId = ParkId(pending.Parent);
                pending.ParentOwner = pending.Parent == null ? 0 : pending.Parent.OwnerID;
                pending.ParentPrefab = pending.Parent == null ? 0 : pending.Parent.prefabID;
                pending.ParentBone = carrier.parentBone;
                pending.Position = carrier.transform.position;
                pending.Rotation = carrier.transform.rotation;
                pending.Captured = true;
                parkNativeCleanup.Add(pending);
                carrier.EnableSaving(false);
                pending.SavingDisabled = NativeParkSavingDisabled(carrier) && NativeParkCarrierMatches(pending);
                if (!pending.SavingDisabled) pending.Failure = "Native carrier save registration could not be disabled or its identity changed.";
                else if (pending.CarrierOwner != pending.Placement.Owner || !ReferenceEquals(pending.CarrierConstruction, pending.Construction) ||
                    pending.Parent != null || !Coordinate(pending.Position) || !Coordinate(pending.Rotation.eulerAngles) ||
                    !(Vector3.Dot(carrier.transform.up, Vector3.up) >= (1f - 1e-5f)))
                    pending.Failure = "The observed native carrier owner, construction or final unparented horizontal pose is not eligible for a park assembly.";
            }
            catch (Exception error)
            {
                pending.Failure = "Native carrier capture failed: " + error.Message;
                if (!pending.Captured) QuarantineNativeParkCarrier(pending);
            }
        }

        private static ParkNativeRequest RecordNativeParkObservation(ParkNativeRequest pending, GameObject gameObject,
            BaseEntity entity, ulong id, out bool added, out bool primary)
        {
            primary = !pending.Observed;
            if (!primary)
            {
                if (SameNativeParkObservation(pending, gameObject, entity, id))
                { added = false; return pending; }
                if (pending.AdditionalObservations != null)
                    foreach (ParkNativeRequest previous in pending.AdditionalObservations)
                        if (SameNativeParkObservation(previous, gameObject, entity, id))
                        { added = false; return previous; }
            }
            ParkNativeRequest receipt = primary ? pending : new ParkNativeRequest
                { Placement = pending.Placement, ObservationOrigin = pending };
            receipt.Observed = true;
            receipt.UnverifiedObject = gameObject;
            receipt.UnverifiedEntity = entity;
            receipt.UnverifiedId = id;
            if (!primary)
            {
                if (pending.AdditionalObservations == null) pending.AdditionalObservations = new List<ParkNativeRequest>();
                pending.AdditionalObservations.Add(receipt);
            }
            added = true;
            return receipt;
        }

        private static bool SameNativeParkObservation(ParkNativeRequest receipt, GameObject gameObject, BaseEntity entity, ulong id)
        {
            return ReferenceEquals(receipt.UnverifiedObject, gameObject) &&
                ReferenceEquals(receipt.UnverifiedEntity, entity) && receipt.UnverifiedId == id;
        }

        private static bool NativeParkCleanupReady(ParkNativeRequest pending)
        {
            // Extra receipts share the native callback's bookkeeping boundary, even after the
            // primary carrier has been removed from the cleanup list.
            return pending.CleanupReady || (!ReferenceEquals(pending.ObservationOrigin, null) && pending.ObservationOrigin.CleanupReady);
        }

        private void QuarantineNativeParkCarrier(ParkNativeRequest pending)
        {
            BaseEntity observed = pending.UnverifiedEntity;
            if (ReferenceEquals(pending.UnverifiedObject, null) && ReferenceEquals(observed, null))
            { pending.Removed = true; return; }
            pending.Unverified = true;
            if (!parkNativeCleanup.Contains(pending)) parkNativeCleanup.Add(pending);
            pending.Failure += " The unverified carrier was left untouched and quarantined; new park placement is disabled until its removal is proved.";
        }

        private bool NativeParkCarrierMatches(ParkNativeRequest pending)
        {
            BuildingBlock carrier = pending.Carrier;
            return pending.Captured && !pending.Removed && carrier != null && !carrier.IsDestroyed &&
                ReferenceEquals(pending.Placement.Data, parkData) && SameParkWorld(pending.Placement.Data) &&
                ParkId(carrier) == pending.CarrierId && ReferenceEquals(FindParkBlock(pending.CarrierId), carrier) &&
                carrier.prefabID == pending.CarrierPrefab && carrier.PrefabName == ParkFoundationPrefab &&
                carrier.OwnerID == pending.CarrierOwner && ReferenceEquals(carrier.blockDefinition, pending.CarrierConstruction) &&
                ReferenceEquals(carrier.GetParentEntity(), pending.Parent) && carrier.parentBone == pending.ParentBone &&
                (pending.Parent == null || (pending.ParentId != 0 && ParkId(pending.Parent) == pending.ParentId &&
                    pending.Parent.OwnerID == pending.ParentOwner && pending.Parent.prefabID == pending.ParentPrefab &&
                    ReferenceEquals(BaseNetworkable.serverEntities.Find(new NetworkableId(pending.ParentId)), pending.Parent))) &&
                (carrier.transform.position - pending.Position).sqrMagnitude <= 0.000001f &&
                Quaternion.Angle(carrier.transform.rotation, pending.Rotation) <= 0.01f;
        }

        private static bool NativeParkSavingDisabled(BuildingBlock carrier)
        { return carrier != null && !carrier.enableSaving && BaseEntity.saveList != null && !BaseEntity.saveList.Contains(carrier); }

        private bool RemoveNativeParkCarrier(ParkNativeRequest pending)
        {
            if (pending.Removed) return true;
            if (!NativeParkCleanupReady(pending) || !ReferenceEquals(pending.Placement.Data, parkData) ||
                !SameParkWorld(pending.Placement.Data)) return false;
            if (pending.Unverified)
            {
                BaseEntity observed = pending.UnverifiedEntity;
                if (pending.UnverifiedId == 0 || ReferenceEquals(observed, null) ||
                    (observed != null && (!observed.IsDestroyed || (ParkId(observed) != 0 && ParkId(observed) != pending.UnverifiedId))) ||
                    BaseNetworkable.serverEntities.Find(new NetworkableId(pending.UnverifiedId)) != null ||
                    BaseEntity.saveList == null || BaseEntity.saveList.Contains(observed)) return false;
                pending.Removed = true;
                parkNativeCleanup.Remove(pending);
                return true;
            }
            if (!pending.Captured) return false;
            BuildingBlock carrier = pending.Carrier;
            if (BaseNetworkable.serverEntities.Find(new NetworkableId(pending.CarrierId)) == null && (carrier == null || carrier.IsDestroyed) &&
                BaseEntity.saveList != null && !BaseEntity.saveList.Contains(carrier))
            { pending.Removed = true; parkNativeCleanup.Remove(pending); return true; }
            if (!NativeParkCarrierMatches(pending)) return false;
            carrier.EnableSaving(false);
            if (carrier.enableSaving || !NativeParkCarrierMatches(pending)) return false;
            carrier.Kill();
            // Inspect the immediate destruction receipt, before a pooled reference can be reused.
            if (BaseNetworkable.serverEntities.Find(new NetworkableId(pending.CarrierId)) != null || (carrier != null && !carrier.IsDestroyed) ||
                BaseEntity.saveList == null || BaseEntity.saveList.Contains(carrier)) return false;
            pending.Removed = true;
            parkNativeCleanup.Remove(pending);
            return true;
        }

        private void CleanupNativeParkCarriers()
        {
            if (parkNativeCleaning) return;
            parkNativeCleaning = true;
            try
            {
                foreach (ParkNativeRequest pending in parkNativeCleanup.ToArray())
                {
                    if (!NativeParkCleanupReady(pending)) continue;
                    try
                    {
                        if (!RemoveNativeParkCarrier(pending))
                            PrintError("Native park carrier cleanup was refused or its identity is unverified; it remains tracked and unknown identities are left untouched.");
                    }
                    catch (Exception error) { PrintError("Native park carrier cleanup failed; it remains tracked. " + error.Message); }
                }
            }
            finally { parkNativeCleaning = false; }
        }

        private void FinishNativeParkRequest(ParkNativeRequest pending)
        {
            ParkNativeRequest current;
            ParkPlacementRequest request = pending.Placement;
            if (!ReferenceEquals(instance, this) || parkClosing ||
                !parkNativeRequests.TryGetValue(request.Owner, out current) || !ReferenceEquals(current, pending)) return;
            pending.CleanupReady = true;
            string failure = pending.Failure;
            bool placed = false;
            try
            {
                if (!pending.Admitted)
                { failure = failure ?? "Native park confirmation was not admitted; the build item was retained."; return; }
                bool valid = failure == null && pending.Captured && pending.SavingDisabled && pending.PaymentSuppressed &&
                    NativeParkCarrierMatches(pending) && NativeParkSavingDisabled(pending.Carrier) &&
                    CurrentParkRequest(request) && !parkBusy && !parkRefreshing && !SaveRestore.IsSaving;
                if (!pending.Captured)
                {
                    if (pending.Unverified && RemoveNativeParkCarrier(pending))
                        failure = "The unverified carrier is gone; no park was created and the build item was retained.";
                    else failure = failure ?? "No native foundation was confirmed. The build item was retained.";
                    return;
                }
                if (!RemoveNativeParkCarrier(pending))
                { failure = "Native carrier cleanup is incomplete; no park assembly was created and the build item was retained."; return; }
                if (!valid || pending.Failure != null || !CurrentParkRequest(request))
                {
                    failure = pending.Failure ?? failure ?? (!pending.PaymentSuppressed ?
                        "The original native material bill suppression was not witnessed; the build item was retained." :
                        "The administrator, build item, carrier saving state or world changed; the build item was retained.");
                    return;
                }
                var failures = new List<string>();
                ParkNativePart[] definitions;
                ParkPrefabs(request.ItemKind, failures, out definitions);
                if (failures.Count != 0) { failure = string.Join("\n", failures); return; }
                Vector3 eye = request.Player.eyes == null ? new Vector3(float.NaN, 0, 0) : request.Player.eyes.position;
                if (!Coordinate(eye) || !Coordinate(pending.Position) ||
                    Vector3.Distance(eye, pending.Position) > MaximumParkPlacementDistance)
                { failure = "The resolved native park frame is outside the 25-metre placement range; the build item was retained."; return; }
                double x, z;
                if (!SkateParkLayout.TrySupportGridAnchor(request.ItemKind, pending.Position.x, pending.Position.z,
                    pending.Rotation.eulerAngles.y, out x, out z))
                { failure = "The native foundation grid anchor is outside world bounds."; return; }
                var group = new ParkGroup { Id = Guid.NewGuid().ToString("N"), Owner = request.Owner,
                    Kind = SkateParkLayout.Name(request.ItemKind), AnchorX = x, AnchorY = pending.Position.y,
                    AnchorZ = z, AnchorYaw = pending.Rotation.eulerAngles.y,
                    Members = new List<ParkMember>(), BuildItemState = SkateParkBuildItemState.Preparing };
                if (!ResolveParkSupport(request, request.ItemKind, definitions, group, out failure)) return;
                if (!BuildParkAssembly(request, request.ItemKind, definitions, group, out failure)) return;
                placed = true;
                ParkPlacementFeedback(request.Player, group);
            }
            catch (Exception error) { failure = placed ? "Park assembly committed, but confirmation feedback failed: " + error.Message :
                "Native park placement refused: " + error.Message; }
            finally
            {
                parkNativeRequests.Remove(request.Owner);
                if (failure != null) ReportParkFailures(request.Player, new[] { failure });
            }
        }

        private static SkateParkBox ParkBox(Bounds bounds)
        {
            return new SkateParkBox { X = bounds.center.x, Y = bounds.center.y, Z = bounds.center.z,
                SizeX = bounds.size.x, SizeY = bounds.size.y, SizeZ = bounds.size.z };
        }

        private static bool SameParkBox(SkateParkBox a, SkateParkBox b)
        {
            return Near(a.X, b.X) && Near(a.Y, b.Y) && Near(a.Z, b.Z) &&
                Near(a.SizeX, b.SizeX) && Near(a.SizeY, b.SizeY) && Near(a.SizeZ, b.SizeZ);
        }

        private static bool TryWallAnchor(ParkWallAttachment wall, out SkateParkTransform anchor)
        {
            anchor = new SkateParkTransform();
            return wall != null && SkateParkLayout.TryHalfWallAnchor(new SkateParkBlock(SkateParkLayout.HalfWallPrefab,
                wall.X, wall.Y, wall.Z, wall.Yaw, 0), wall.WallBounds, wall.FloorBounds, wall.Side, out anchor);
        }

        private static bool ValidWallAttachment(ParkWallAttachment wall, out SkateParkTransform anchor)
        {
            anchor = new SkateParkTransform();
            return wall != null && wall.Id != 0 && wall.Owner != 0 && wall.Building != 0 &&
                wall.Grade >= (int)BuildingGrade.Enum.Twigs && wall.Grade <= (int)BuildingGrade.Enum.TopTier &&
                wall.Yaw >= 0 && wall.Yaw < 360 && TryWallAnchor(wall, out anchor);
        }

        private static bool WallMatches(BuildingBlock block, ParkWallAttachment wall)
        {
            SkateParkTransform anchor;
            if (!ValidWallAttachment(wall, out anchor) || block == null || block.IsDestroyed || block.net == null ||
                !ReferenceEquals(FindParkBlock(wall.Id), block) || ParkId(block) != wall.Id ||
                block.PrefabName != SkateParkLayout.HalfWallPrefab || block.prefabID != StringPool.Get(SkateParkLayout.HalfWallPrefab) ||
                block.OwnerID != wall.Owner || block.buildingID != wall.Building || !block.enableSaving ||
                block.GetParentEntity() != null || block.skinID != 0 || (int)block.grade != wall.Grade ||
                block.blockDefinition == null || !SameParkBox(ParkBox(block.blockDefinition.bounds), wall.WallBounds) ||
                (block.transform.localScale - Vector3.one).sqrMagnitude > 1e-8f ||
                (block.transform.position - new Vector3((float)wall.X, (float)wall.Y, (float)wall.Z)).sqrMagnitude > 1e-6f ||
                Quaternion.Angle(block.transform.rotation, Quaternion.Euler(0, (float)wall.Yaw, 0)) > 0.01f) return false;
            uint floorId = StringPool.Get(SkateParkLayout.FloorPrefab);
            Construction floor = PrefabAttribute.server == null ? null : PrefabAttribute.server.Find<Construction>(floorId);
            return floor != null && SameParkBox(ParkBox(floor.bounds), wall.FloorBounds);
        }

        private static bool WallPermission(BasePlayer player, BuildingBlock wall)
        {
            if (player == null || wall == null || wall.blockDefinition == null ||
                !player.CanBuild(wall.transform.position, wall.transform.rotation, wall.blockDefinition.bounds, false)) return false;
            var building = wall.GetBuilding();
            if (building == null) return false;
            var privilege = building.GetDominatingBuildingPrivilege();
            return privilege == null ? wall.OwnerID == player.userID : privilege.CanBuild(player);
        }

        private static bool RecoverableWall(ParkGroup group, BasePlayer player)
        {
            return group.Wall == null || (WallMatches(FindParkBlock(group.Wall.Id), group.Wall) &&
                WallPermission(player, FindParkBlock(group.Wall.Id)));
        }

        private bool WallSocketOccupied(ParkWallAttachment wall, ParkGroup except = null)
        {
            return parkData == null || parkMembers.ContainsKey(wall.Id) || parkData.Groups.Any(g =>
                !ReferenceEquals(g, except) && g.Wall != null && g.Wall.Id == wall.Id && g.Wall.Side == wall.Side);
        }

        private bool TryWallPlacement(ParkPlacementRequest request, Ray ray, BuildingBlock expected,
            ParkNativePart[] definitions, out ParkGroup group, out string failure)
        {
            group = null;
            failure = "Aim at the broad side of a saved, upright, unparented half wall within 25 metres.";
            BasePlayer player = request.Player;
            if (player.eyes == null || !Coordinate(ray.origin) || !Coordinate(ray.direction) ||
                ray.direction.sqrMagnitude < 0.0001f || Vector3.Distance(ray.origin, player.eyes.position) > 0.5f) return false;
            RaycastHit hit;
            int mask = LayerMask.GetMask("World", "Terrain", "Construction", "Deployed", "Player (Server)");
            if (mask == 0 || !Physics.Raycast(ray.origin, ray.direction.normalized, out hit,
                MaximumParkPlacementDistance, mask, QueryTriggerInteraction.Ignore) || hit.collider == null ||
                !Coordinate(hit.point) || !Coordinate(hit.normal)) return false;
            BuildingBlock wall = hit.collider.ToBaseEntity() as BuildingBlock;
            if (wall == null || (expected != null && !ReferenceEquals(wall, expected)) || wall.blockDefinition == null ||
                wall.net == null || wall.OwnerID == 0 || wall.buildingID == 0 || definitions == null || definitions.Length != 1) return false;
            float dot = Vector3.Dot(hit.normal, wall.transform.right);
            if (Math.Abs(dot) < 0.98f) return false;
            var attachment = new ParkWallAttachment { Id = ParkId(wall), Owner = wall.OwnerID, Building = wall.buildingID,
                Grade = (int)wall.grade, Side = dot > 0 ? 1 : -1, X = wall.transform.position.x,
                Y = wall.transform.position.y, Z = wall.transform.position.z, Yaw = wall.transform.rotation.eulerAngles.y,
                WallBounds = ParkBox(wall.blockDefinition.bounds), FloorBounds = ParkBox(definitions[0].Construction.bounds) };
            SkateParkTransform anchor;
            if (!WallMatches(wall, attachment) || !TryWallAnchor(attachment, out anchor) ||
                Vector3.Dot(ray.origin - wall.transform.position, wall.transform.right) * attachment.Side <= 0 ||
                Vector3.Distance(player.eyes.position, new Vector3((float)anchor.X, (float)anchor.Y, (float)anchor.Z)) > MaximumParkPlacementDistance)
                return false;
            failure = "The half wall is a park member, its side is occupied, or its building privilege is unavailable.";
            if (WallSocketOccupied(attachment) || !WallPermission(player, wall)) return false;
            request.WallBlock = wall;
            request.Wall = attachment;
            request.Building = wall.buildingID;
            group = new ParkGroup { Id = Guid.NewGuid().ToString("N"), Owner = request.Owner,
                Kind = SkateParkLayout.Name(SkateParkKind.HalfWallKicker), AnchorX = anchor.X, AnchorY = anchor.Y,
                AnchorZ = anchor.Z, AnchorYaw = anchor.Yaw, Members = new List<ParkMember>(), Wall = attachment,
                BuildItemState = request.BuildItem ? SkateParkBuildItemState.Preparing : SkateParkBuildItemState.None };
            failure = null;
            return true;
        }

        private void PlaceNativeWallKicker(ParkPlacementRequest request, Construction construction, Construction.Target target)
        {
            try
            {
                string failure;
                if (!target.valid || !ReferenceEquals(target.player, request.Player) || target.shouldParent ||
                    !(target.entity is BuildingBlock) || !CurrentParkRequest(request))
                { request.Player.ConsoleMessage("Half-wall attachment needs an unchanged native wall target; the item was retained."); return; }
                var failures = new List<string>();
                ParkNativePart[] definitions;
                ParkPrefabs(SkateParkKind.HalfWallKicker, failures, out definitions);
                if (failures.Count != 0) { ReportParkFailures(request.Player, failures); return; }
                ParkGroup group;
                if (!TryWallPlacement(request, target.ray, target.entity as BuildingBlock, definitions, out group, out failure))
                { request.Player.ConsoleMessage(failure); return; }
                // Finish the original hook dispatch before invoking any creation or payment callbacks.
                NextTick(() =>
                {
                    if (!CurrentParkRequest(request) || parkBusy || parkRefreshing || parkNativeRequests.Count != 0) return;
                    try
                    {
                        bool permitted;
                        parkBusy = true;
                        try { permitted = ParkBuildingHook(request, construction, target); }
                        finally { parkBusy = false; }
                        if (!permitted || !CurrentParkRequest(request))
                        { request.Player.ConsoleMessage("A native wall-target building hook refused the attachment; the item was retained."); return; }
                        string refused;
                        if (!BuildParkAssembly(request, SkateParkKind.HalfWallKicker, definitions, group, out refused))
                        { request.Player.ConsoleMessage(refused); return; }
                        ParkPlacementFeedback(request.Player, group);
                    }
                    catch (Exception error) { PrintWarning("Deferred half-wall attachment refused: " + error.Message); }
                });
            }
            catch (Exception error) { PrintWarning("Half-wall attachment refused: " + error.Message); }
        }

        private static bool SavedWallMatches(ParkWallAttachment wall,
            Dictionary<ulong, KeyValuePair<BaseEntity, ProtoBuf.Entity>> saved)
        {
            KeyValuePair<BaseEntity, ProtoBuf.Entity> pair;
            SkateParkTransform anchor;
            if (!ValidWallAttachment(wall, out anchor) || !saved.TryGetValue(wall.Id, out pair)) return false;
            ProtoBuf.Entity proto = pair.Value;
            return pair.Key is BuildingBlock && pair.Key.PrefabName == SkateParkLayout.HalfWallPrefab &&
                proto != null && proto.baseNetworkable != null && proto.baseEntity != null && proto.ownerInfo != null &&
                proto.baseNetworkable.uid.Value == wall.Id && proto.baseNetworkable.prefabID == StringPool.Get(SkateParkLayout.HalfWallPrefab) &&
                proto.ownerInfo.steamid == wall.Owner && proto.buildingBlock != null && proto.buildingBlock.grade == wall.Grade &&
                proto.decayEntity != null && proto.decayEntity.buildingID == wall.Building &&
                (proto.parent == null || proto.parent.uid.Value == 0) &&
                (proto.baseEntity.pos - new Vector3((float)wall.X, (float)wall.Y, (float)wall.Z)).sqrMagnitude <= 1e-6f &&
                Quaternion.Angle(Quaternion.Euler(proto.baseEntity.rot), Quaternion.Euler(0, (float)wall.Yaw, 0)) <= 0.01f;
        }

        private bool CurrentWallCleanup(ParkData data)
        {
            return ReferenceEquals(instance, this) && !parkClosing && !parkRefreshing && !SaveRestore.IsSaving &&
                !parkDataBroken && !parkStorageFailed && ReferenceEquals(parkData, data) && SameParkWorld(data);
        }

        private void CleanupMegaProfileCollisionHealth()
        {
            // The caller owns parkBusy. Collision safety does not depend on writable park metadata.
            foreach (var pair in parkProfileCollisions.ToArray())
            {
                if (!parkBusy || !ReferenceEquals(instance, this) || parkClosing || parkRefreshing || SaveRestore.IsSaving) return;
                ParkProfileCollision current;
                if (!ParkOwnedHalfpipe(pair.Key.Kind) || !parkProfileCollisions.TryGetValue(pair.Key, out current) ||
                    !ReferenceEquals(current, pair.Value)) continue;
                bool healthy;
                try { healthy = ParkMegaCollisionHealthy(pair.Key, current); }
                catch (Exception) { healthy = false; }
                if (!healthy)
                {
                    RemoveParkProfileCollision(pair.Key);
                    PrintWarning("Halfpipe profile collision was invalidated; native floor collision restoration was requested.");
                }
            }
        }

        private void CleanupWallAttachments()
        {
            if (!ReferenceEquals(instance, this) || parkClosing || parkBusy || parkRefreshing || SaveRestore.IsSaving) return;
            parkBusy = true;
            try
            {
                CleanupMegaProfileCollisionHealth();
                if (parkProfileRestoreRefreshNeeded && parkRollback.Count == 0 && parkData != null &&
                    CurrentWallCleanup(parkData) && !parkData.Groups.Any(ParkProfileRestorePending))
                    InitializeParks();
                if (parkData == null || !CurrentWallCleanup(parkData)) return;
                ParkData data = parkData;
                foreach (ParkGroup group in data.Groups.ToArray())
                {
                    if (!CurrentWallCleanup(data)) return;
                    if (group.Wall == null) continue;
                    if (WallMatches(FindParkBlock(group.Wall.Id), group.Wall))
                    {
                        PruneDestroyedParkMembers(group);
                        continue;
                    }
                    // Dependency loss is destruction, never a paid-group recovery operation.
                    if (!CurrentWallCleanup(data) || !data.Groups.Contains(group)) return;
                    group.BuildItemState = SkateParkBuildItemState.RecoveryClaimed;
                    SaveParkData();
                    foreach (ParkMember member in group.Members.ToArray())
                    {
                        if (!CurrentWallCleanup(data) || !data.Groups.Contains(group)) return;
                        BuildingBlock block = FindParkBlock(member.Id);
                        if (!ParkMatches(block, group, member)) continue;
                        block.grounded = false;
                        block.Kill(BaseNetworkable.DestroyMode.Gib);
                        if (!CurrentWallCleanup(data)) return;
                    }
                    if (!CurrentWallCleanup(data)) return;
                    PruneDestroyedParkMembers(group);
                }
            }
            catch (Exception error) { PrintWarning("Half-wall dependency cleanup remains pending: " + error.Message); }
            finally { parkBusy = false; }
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

        private static double ParkRotationStep()
        {
            uint id = StringPool.Get(ParkFoundationPrefab);
            Construction construction = PrefabAttribute.server == null ? null : PrefabAttribute.server.Find<Construction>(id);
            return construction == null ? double.NaN : construction.rotationAmount.y;
        }

        private static bool ParkSupportFoundationCurrent(ParkSupport support)
        {
            BuildingBlock block = support.Block;
            return block != null && ReferenceEquals(FindParkBlock(support.Id), block) &&
                ParkFoundationMatches(block, support.Id, support.Snapshot) && block.grade == support.Grade &&
                ReferenceEquals(block.blockDefinition, support.Construction) && support.Collider != null &&
                support.Collider.enabled && ReferenceEquals(support.Collider.ToBaseEntity(), block);
        }

        private static bool ParkSupportBlock(ParkPlacementRequest request, BuildingBlock block)
        {
            return request != null && request.Supports != null && block != null && request.Supports.Any(s =>
                ReferenceEquals(s.Block, block) && ParkSupportFoundationCurrent(s));
        }

        private static bool ParkSupportCollider(ParkPlacementRequest request, Collider collider)
        {
            return request != null && collider != null && ParkSupportBlock(request, collider.ToBaseEntity() as BuildingBlock) &&
                collider.bounds.max.y <= request.SupportTop + SkateParkLayout.SupportTolerance;
        }

        private static bool ParkSupportHeight(double height, double plane)
        {
            return SkateSessionBounds.Finite(height) && SkateSessionBounds.Finite(plane) &&
                Math.Abs(height - plane) <= SkateParkLayout.SupportTolerance;
        }

        private static bool ParkSupportsCurrent(ParkPlacementRequest request)
        {
            request.SupportFailure = null;
            double low = double.PositiveInfinity, high = double.NegativeInfinity;
            foreach (ParkSupport support in request.Supports)
            {
                double height;
                request.SupportFailure = support.Cell + ": support changed, disappeared or is uneven.";
                if (support.Block != null)
                {
                    height = ParkFoundationTop(support.Block, support.Construction.bounds);
                    if (!ParkSupportFoundationCurrent(support) || !WallPermission(request.Player, support.Block) ||
                        !ParkSupportHeight(height, request.SupportTop)) return false;
                }
                else
                {
                    height = TerrainMeta.HeightMap == null ? double.NaN : TerrainMeta.HeightMap.GetHeight(support.Point);
                    if (!ReferenceEquals(support.Block, null) || support.Collider == null || !support.Collider.enabled ||
                        support.Collider.gameObject.layer != LayerMask.NameToLayer("Terrain") || TerrainMeta.HeightMap == null ||
                        !ParkSupportHeight(height, support.Point.y) || !ParkSupportHeight(height, request.SupportTop) ||
                        WaterLevel.Test(support.Point + Vector3.up * 0.1f, true, false)) return false;
                }
                low = Math.Min(low, height); high = Math.Max(high, height);
                if (high - low > SkateParkLayout.SupportTolerance) return false;
            }
            request.SupportFailure = null;
            return request.Supports.Count != 0;
        }

        private bool ReadParkSupport(ParkPlacementRequest request, Vector3 probe, int mask,
            SkateParkTransform? cell, string label, out ParkSupport support, out double height, out string failure)
        {
            support = null; height = 0;
            failure = label + ": missing, blocked or uneven support; use a level existing square foundation or dry terrain.";
            RaycastHit hit;
            if (!Coordinate(probe) || !Physics.Raycast(probe, Vector3.down, out hit,
                MaximumParkPlacementDistance + 2, mask, QueryTriggerInteraction.Ignore) || hit.collider == null ||
                !Coordinate(hit.point) || !Coordinate(hit.normal) || hit.normal.y < 0.5f) return false;
            BaseEntity entity = hit.collider.ToBaseEntity();
            BuildingBlock block = entity as BuildingBlock;
            height = hit.point.y;
            support = new ParkSupport { Point = hit.point, Collider = hit.collider, Cell = label };
            if (block != null)
            {
                if (!HorizontalParkFoundation(block) || !NativeParkConstruction(block.blockDefinition) ||
                    !ReferenceEquals(FindParkBlock(ParkId(block)), block) || parkMembers.ContainsKey(ParkId(block)) ||
                    !WallPermission(request.Player, block) || hit.normal.y < 0.98f) return false;
                Bounds bounds = block.blockDefinition.bounds;
                if (cell.HasValue && (!SkateParkLayout.FitsSupportCell(cell.Value, block.transform.position.x,
                    block.transform.position.z, block.transform.rotation.eulerAngles.y) || bounds.size.x < 2.9f ||
                    bounds.size.x > 3.1f || bounds.size.z < 2.9f || bounds.size.z > 3.1f))
                { failure = label + ": foundation is off the selected 3 m grid or rotation."; return false; }
                height = ParkFoundationTop(block, bounds);
                if (!ParkSupportHeight(height, hit.point.y)) return false;
                support.Block = block; support.Id = ParkId(block); support.Snapshot = CaptureParkAnchor(block);
                support.Grade = block.grade; support.Construction = block.blockDefinition;
            }
            else if (entity != null || hit.collider.gameObject.layer != LayerMask.NameToLayer("Terrain") ||
                TerrainMeta.HeightMap == null || !ParkSupportHeight(TerrainMeta.HeightMap.GetHeight(hit.point), height)) return false;
            if (WaterLevel.Test(hit.point + Vector3.up * 0.1f, true, false)) return false;
            failure = null;
            return true;
        }

        private bool ResolveParkSupport(ParkPlacementRequest request, SkateParkKind kind, ParkNativePart[] definitions,
            ParkGroup group, out string failure)
        {
            failure = "No valid support: the lowest park surfaces must meet dry terrain or horizontal square foundations on one plane.";
            request.Supports = null;
            Bounds floor = definitions[0].Construction.bounds;
            var supports = new List<ParkSupport>();
            int mask = LayerMask.GetMask("Terrain", "World", "Construction", "Deployed");
            if (mask == 0) return false;
            SkateParkSupportReader read = delegate(SkateParkSupportProbe probe, out SkateParkSupportHit hit, out string reason)
            {
                hit = new SkateParkSupportHit();
                ParkSupport support;
                double height;
                if (!ReadParkSupport(request, new Vector3((float)probe.Point.X, (float)probe.Point.Y, (float)probe.Point.Z),
                    mask, probe.RequireCell ? (SkateParkTransform?)probe.Cell : null, SkateParkLayout.SupportLabel(probe),
                    out support, out height, out reason)) return false;
                hit = new SkateParkSupportHit { Height = height, FoundationId = support.Id };
                if (support.Block == null || (probe.RequireCell && probe.Sample == 0)) supports.Add(support);
                return true;
            };
            SkateParkTransform anchor;
            double top;
            if (!SkateParkLayout.TryGhostAnchor(kind, ParkBox(floor),
                new SkateParkTransform { X = group.AnchorX, Y = group.AnchorY, Z = group.AnchorZ, Yaw = group.AnchorYaw },
                request.SnapSnapshot != null ? (double?)request.SnapSupportTop : null, read,
                out anchor, out top, out failure)) return false;
            double baseline = anchor.Y;
            if (request.Player.eyes == null || Vector3.Distance(request.Player.eyes.position,
                new Vector3((float)anchor.X, (float)anchor.Y, (float)anchor.Z)) > MaximumParkPlacementDistance)
            { failure = "The supported assembly anchor is outside the placement range or world bounds."; return false; }
            group.AnchorY = baseline;
            request.SupportTop = top; request.Supports = supports;
            if (!ParkSupportsCurrent(request)) { failure = request.SupportFailure; request.Supports = null; return false; }
            failure = null;
            return true;
        }

        private static bool ParkMeasuredTwigCollider(BuildingBlock block, Collider collider)
        {
            BoxCollider box = collider as BoxCollider;
            if (block == null || block.IsDestroyed || block.PrefabName != SkateParkLayout.FloorPrefab || box == null ||
                !ReferenceEquals(collider.ToBaseEntity(), block)) return false;
            Vector3 scale = box.transform.lossyScale;
            if (!Coordinate(scale) || Math.Abs(scale.x - 1) > SkateParkLayout.NativeBoundsTolerance ||
                Math.Abs(scale.y - 1) > SkateParkLayout.NativeBoundsTolerance ||
                Math.Abs(scale.z - 1) > SkateParkLayout.NativeBoundsTolerance) return false;
            Bounds bounds = new Bounds(box.center, box.size);
            Matrix4x4 toBlock = Matrix4x4.identity;
            Transform frame = box.transform;
            while (frame != block.transform)
            {
                if (frame == null) return false;
                toBlock = Matrix4x4.TRS(frame.localPosition, frame.localRotation, frame.localScale) * toBlock;
                frame = frame.parent;
            }
            Bounds physical = new Bounds();
            bool first = true;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 point = toBlock.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z)));
                if (!Coordinate(point)) return false;
                if (first) { physical = new Bounds(point, Vector3.zero); first = false; }
                else physical.Encapsulate(point);
            }
            return SkateParkLayout.MatchesMeasuredTwigCollider(collider.name, ParkBox(bounds), ParkBox(physical));
        }

        private static bool ParkMeasuredTwigFloor(BuildingBlock block)
        {
            if (block == null || block.IsDestroyed || block.PrefabName != SkateParkLayout.FloorPrefab) return false;
            Collider[] colliders = block.GetComponentsInChildren<Collider>(true);
            if (colliders == null) return false;
            int physical = 0;
            foreach (Collider collider in colliders)
            {
                if (collider == null) return false;
                if (!collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy) continue;
                if (!ParkMeasuredTwigCollider(block, collider)) return false;
                physical++;
            }
            return physical != 0;
        }

        private static bool ParkAdjacentContact(ParkPlacementRequest request, Vector3 position,
            Quaternion rotation, Bounds bounds, BuildingBlock neighbour)
        {
            if (request == null || neighbour == null) return false;
            bool selected = request.SnapSnapshot != null && ReferenceEquals(request.SnapBlock, neighbour);
            if (selected ? !ParkSnapCurrent(request) : request.Supports == null || !ParkMeasuredTwigFloor(neighbour) ||
                !ReferenceEquals(FindParkBlock(ParkId(neighbour)), neighbour)) return false;
            return Coordinate(neighbour.transform.position) && WallPermission(request.Player, neighbour) &&
                neighbour.PrefabName == SkateParkLayout.FloorPrefab &&
                Quaternion.Angle(neighbour.transform.rotation, Quaternion.Euler(0, neighbour.transform.rotation.eulerAngles.y, 0)) < 0.1f &&
                neighbour.blockDefinition != null && ParkSupportHeight(neighbour.transform.position.y +
                    neighbour.blockDefinition.bounds.min.y, request.SupportTop) &&
                !ParkSpacesOverlap(new ParkSpace { Position = position, Rotation = rotation, Bounds = bounds },
                    new ParkSpace { Position = neighbour.transform.position, Rotation = neighbour.transform.rotation,
                        Bounds = neighbour.blockDefinition.bounds }, SkateParkLayout.NativeBoundsTolerance) &&
                (selected || ParkSpacesShareEdge(new ParkSpace { Position = position, Rotation = rotation, Bounds = bounds },
                    new ParkSpace { Position = neighbour.transform.position, Rotation = neighbour.transform.rotation,
                        Bounds = neighbour.blockDefinition.bounds }));
        }

        private static bool ParkSupportedDeployVolume(ParkPlacementRequest request, Vector3 position,
            Quaternion rotation, Bounds bounds, DeployVolume[] volumes, BuildingBlock anchor,
            List<BuildingBlock> transactionParts, int mask, out Collider blocker, out string unavailable)
        {
            blocker = null;
            unavailable = "The verified support collider changed or extends above the support plane";
            var disabled = new List<Collider>();
            try
            {
                if (request.Supports != null)
                foreach (BuildingBlock block in request.Supports.Select(s => s.Block).Where(b => b != null).Distinct())
                {
                    if (!ParkSupportBlock(request, block)) return true;
                    foreach (Collider collider in block.GetComponentsInChildren<Collider>(true))
                    {
                        if (collider == null || !collider.enabled || !ReferenceEquals(collider.ToBaseEntity(), block)) continue;
                        if (collider.bounds.max.y > request.SupportTop + SkateParkLayout.SupportTolerance) return true;
                        disabled.Add(collider); collider.enabled = false;
                    }
                }
                BuildingBlock neighbour = request.SnapBlock;
                if (ParkAdjacentContact(request, position, rotation, bounds, neighbour))
                    foreach (Collider collider in neighbour.GetComponentsInChildren<Collider>(true))
                        if (collider != null && collider.enabled && ReferenceEquals(collider.ToBaseEntity(), neighbour))
                        { disabled.Add(collider); collider.enabled = false; }
                return ParkDeployVolumeBlocked(request, position, rotation, bounds, volumes, request.Player, anchor,
                    transactionParts, mask, out blocker, out unavailable);
            }
            finally { foreach (Collider collider in disabled) if (collider != null) collider.enabled = true; }
        }

        private static bool ParkSnapCurrent(ParkPlacementRequest request)
        {
            BuildingBlock block = request.SnapBlock;
            ParkAnchorSnapshot snapshot = request.SnapSnapshot;
            return block != null && !block.IsDestroyed && block.enableSaving &&
                ReferenceEquals(FindParkBlock(request.SnapId), block) && block.OwnerID == snapshot.Owner &&
                block.buildingID == snapshot.Building && block.GetParentEntity() == null &&
                (block.transform.position - snapshot.Position).sqrMagnitude <= 0.000001f &&
                Quaternion.Angle(block.transform.rotation, snapshot.Rotation) <= 0.01f;
        }

        private bool TryParkTargetFrame(ParkPlacementRequest request, BuildingBlock host, RaycastHit hit,
            double requestedYaw, bool relativeYaw, ParkNativePart[] definitions,
            out double x, out double z, out double yaw, out string failure)
        {
            x = z = yaw = 0;
            failure = "This surface has no compatible foundation or flat ground-level park socket; the build item was retained.";
            if (host == null || host.IsDestroyed || !host.enableSaving || host.GetParentEntity() != null || hit.normal.y < 0.98f)
                return false;
            double px = host.transform.position.x, pz = host.transform.position.z;
            double frame = host.transform.rotation.eulerAngles.y;
            ParkGroup existing;
            if (parkMembers.TryGetValue(ParkId(host), out existing))
            {
                ParkMember member = existing.Members.FirstOrDefault(m => m.Id == ParkId(host));
                double resting;
                SkateParkKind hostKind;
                Bounds floor = definitions[0].Construction.bounds;
                if (member == null || !SkateParkLayout.TryKind(existing.Kind, out hostKind) ||
                    !ParkMatches(host, existing, member) || Math.Abs(member.Pitch) > 0.01 ||
                    member.Prefab != StringPool.Get(SkateParkLayout.FloorPrefab) ||
                    !SkateParkLayout.TrySupportedAnchor(hostKind, 0, floor.center.y, floor.center.z,
                        floor.size.y, floor.size.z, out resting) ||
                    Math.Abs(member.Y - existing.AnchorY + floor.min.y + resting) > 0.001) return false;
            }
            else if (!HorizontalParkFoundation(host) || !WallPermission(request.Player, host)) return false;
            SkateParkTransform targetFrame;
            if (!SkateParkLayout.TryGhostFrame(request.ItemKind, existing != null, px, pz, frame,
                hit.point.x, hit.point.z, requestedYaw, relativeYaw, ParkRotationStep(), out targetFrame)) return false;
            request.SnapBlock = host; request.SnapId = ParkId(host); request.SnapSnapshot = CaptureParkAnchor(host);
            request.SnapSupportTop = existing != null ? host.transform.position.y + host.blockDefinition.bounds.min.y :
                ParkFoundationTop(host, host.blockDefinition.bounds);
            if (!ParkSnapCurrent(request)) return false;
            x = targetFrame.X; z = targetFrame.Z; yaw = targetFrame.Yaw;
            failure = null;
            return true;
        }

        private bool TrySupportedNativeTarget(ParkPlacementRequest request, Construction construction, Construction.Target target)
        {
            BuildingBlock host = target.entity as BuildingBlock;
            bool park = host != null && parkMembers.ContainsKey(ParkId(host));
            if (!park && !HorizontalParkFoundation(host)) return false;
            try
            {
                RaycastHit hit;
                int mask = LayerMask.GetMask("World", "Terrain", "Construction", "Deployed", "Player (Server)");
                if (request.Player.eyes == null || !Coordinate(target.ray.origin) || !Coordinate(target.ray.direction) ||
                    Vector3.Distance(request.Player.eyes.position, target.ray.origin) > 0.5f ||
                    target.ray.direction.sqrMagnitude < 0.0001f || mask == 0 ||
                    !Physics.Raycast(target.ray.origin, target.ray.direction.normalized, out hit,
                        MaximumParkPlacementDistance, mask, QueryTriggerInteraction.Ignore) ||
                    hit.collider == null || !ReferenceEquals(hit.collider.ToBaseEntity(), host) || hit.normal.y < 0.98f)
                { ReplyParkTarget(request, "Aim at a foundation top or the edge of a flat park floor; the build item was retained."); return true; }
                var failures = new List<string>();
                ParkNativePart[] definitions;
                ParkPrefabs(request.ItemKind, failures, out definitions);
                if (failures.Count != 0) { ReportParkFailures(request.Player, failures); return true; }
                double x, z, yaw;
                string failure;
                if (!TryParkTargetFrame(request, host, hit, target.rotation.y, true, definitions,
                    out x, out z, out yaw, out failure))
                { ReplyParkTarget(request, failure); return true; }
                var group = new ParkGroup { Id = Guid.NewGuid().ToString("N"), Owner = request.Owner,
                    Kind = SkateParkLayout.Name(request.ItemKind), AnchorX = x, AnchorY = hit.point.y,
                    AnchorZ = z, AnchorYaw = yaw, Members = new List<ParkMember>(), BuildItemState = SkateParkBuildItemState.Preparing };
                if (!ResolveParkSupport(request, request.ItemKind, definitions, group, out failure))
                { ReplyParkTarget(request, failure + " The build item was retained."); return true; }
                NextTick(() =>
                {
                    try
                    {
                        if (!CurrentParkRequest(request) || parkBusy || parkRefreshing || parkNativeRequests.Count != 0)
                        { ReplyParkTarget(request, (request.SupportFailure ?? "The support, item or world changed.") + " The build item was retained."); return; }
                        bool permitted;
                        parkBusy = true;
                        try { permitted = ParkBuildingHook(request, construction, target); }
                        finally { parkBusy = false; }
                        if (!permitted || !CurrentParkRequest(request))
                        { ReplyParkTarget(request, "A native building hook refused the supported placement; the build item was retained."); return; }
                        if (!BuildParkAssembly(request, request.ItemKind, definitions, group, out string refused))
                        { ReplyParkTarget(request, refused); return; }
                        ParkPlacementFeedback(request.Player, group);
                    }
                    catch (Exception error) { ReplyParkTarget(request, "Supported park placement refused: " + error.Message); }
                });
                return true;
            }
            catch (Exception error)
            { ReplyParkTarget(request, "Supported park placement refused: " + error.Message); return true; }
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
                CheckParkBound(failures, role, "bounds.center.x", bounds.center.x, -0.05, 0.05);
                if (path == SkateParkLayout.FloorPrefab)
                {
                    CheckParkBound(failures, role, "bounds.size.x", bounds.size.x, 2.9, 3.1);
                    CheckParkBound(failures, role, "bounds.size.z", bounds.size.z, 2.9, 3.1);
                    CheckParkBound(failures, role, "bounds.size.y", bounds.size.y, 0, 1, true);
                    CheckParkBound(failures, role, "bounds.center.z", bounds.center.z, -0.05, 0.05);
                    CheckParkBound(failures, role, "bounds.center.y", bounds.center.y, -0.5, 0.5);
                }
                else if (path == SkateParkLayout.LowWallPrefab || path == SkateParkLayout.HalfWallPrefab)
                {
                    double height = path == SkateParkLayout.LowWallPrefab ? 1 : 1.5;
                    CheckParkBound(failures, role, "bounds.size.y", bounds.size.y, height - 0.1, height + 0.1);
                    CheckParkBound(failures, role, "bounds.size.x", bounds.size.x, 0, 0.6, true);
                    CheckParkBound(failures, role, "bounds.size.z", bounds.size.z, 2.9, 3.1);
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
                if (definition != null && SkateParkLayout.CellCount(kind) != 0)
                {
                    Bounds bounds = definition.Construction.bounds;
                    SkateParkQuarterProfile profile;
                    SkateParkLayout.TryQuarterProfile(kind, out profile);
                    if (!SkateParkLayout.ContainsNativeBounds(kind, i, bounds.center.x, bounds.center.z,
                        bounds.size.x, bounds.size.z) || (part.Prefab != SkateParkLayout.FloorPrefab &&
                        (definitions[0] == null || bounds.max.y > definitions[0].Construction.bounds.max.y + profile.Height + SkateParkLayout.NativeBoundsTolerance)))
                        failures.Add("Park module part[" + i.ToString(CultureInfo.InvariantCulture) + "]: native bounds exceed its cell or riding envelope.");
                }
            }
            return failures.Count == 0;
        }

        private sealed class ParkProfileCollision
        {
            public ParkGroup Identity;
            public GameObject Root;
            public Mesh Mesh;
            public float Datum;
            public readonly List<ParkProfilePatch> Patches = new List<ParkProfilePatch>();
            public readonly List<Collider> Disabled = new List<Collider>();
            public readonly List<BuildingBlock> Members = new List<BuildingBlock>();
            public readonly List<ParkProfileColliderState> OriginalShapes = new List<ParkProfileColliderState>();
        }

        private sealed class ParkProfilePatch
        {
            public int Index;
            public GameObject Root;
            public Mesh Mesh;
            public MeshCollider Shell;
            public ParkProfileMeshState Shape;
        }

        private sealed class ParkProfileChangedException : Exception
        {
            public ParkProfileChangedException(string message) : base(message) { }
        }

        private sealed class ParkProfileActivationOccupiedException : Exception
        {
            public ParkProfileActivationOccupiedException() : base("Leave the regular halfpipe transition volume before its smooth collision can be installed.") { }
        }

        private sealed class ParkProfileRestorePendingException : Exception
        {
            public ParkProfileRestorePendingException() : base("MEGA native collision restoration is pending; held members must not be removed or replaced.") { }
        }

        private sealed class ParkProfileMeshState
        {
            private Mesh mesh;
            private Bounds bounds;
            private Vector3[] vertices;
            private int[][] triangles;
            private readonly List<Vector3> vertexScratch = new List<Vector3>();
            private readonly List<int> triangleScratch = new List<int>();

            public static ParkProfileMeshState Capture(Mesh value)
            {
                if (value == null || value.vertexCount == 0 || value.vertexCount > 65536 || value.subMeshCount < 1 || value.subMeshCount > 32)
                    throw new InvalidDataException("MEGA collision has an unavailable or unsupported mesh shape.");
                var state = new ParkProfileMeshState { mesh = value, bounds = value.bounds, triangles = new int[value.subMeshCount][] };
                value.GetVertices(state.vertexScratch);
                if (state.vertexScratch.Count != value.vertexCount || state.vertexScratch.Any(vertex => !Coordinate(vertex)) ||
                    !Coordinate(state.bounds.center) || !Coordinate(state.bounds.size))
                    throw new InvalidDataException("MEGA collision mesh has an invalid vertex.");
                state.vertices = state.vertexScratch.ToArray();
                int indexCount = 0;
                for (int i = 0; i < state.triangles.Length; i++)
                {
                    value.GetTriangles(state.triangleScratch, i);
                    if (state.triangleScratch.Count > 196608)
                        throw new InvalidDataException("MEGA collision mesh has too many indices.");
                    state.triangles[i] = state.triangleScratch.ToArray();
                    indexCount += state.triangleScratch.Count;
                }
                if (indexCount == 0) throw new InvalidDataException("MEGA collision mesh has no triangles.");
                return state;
            }

            public bool Matches(Mesh value)
            {
                if (value == null || value != mesh || value.vertexCount != vertices.Length || value.subMeshCount != triangles.Length ||
                    !value.bounds.center.Equals(bounds.center) || !value.bounds.size.Equals(bounds.size)) return false;
                value.GetVertices(vertexScratch);
                if (vertexScratch.Count != vertices.Length) return false;
                for (int i = 0; i < vertices.Length; i++) if (!vertexScratch[i].Equals(vertices[i])) return false;
                for (int i = 0; i < triangles.Length; i++)
                {
                    value.GetTriangles(triangleScratch, i);
                    if (triangleScratch.Count != triangles[i].Length) return false;
                    for (int j = 0; j < triangleScratch.Count; j++) if (triangleScratch[j] != triangles[i][j]) return false;
                }
                return true;
            }
        }

        private sealed class ParkProfileFrameState
        {
            public Transform Frame, Parent;
            public Vector3 Position, Scale;
            public Quaternion Rotation;

            public bool Matches()
            {
                return Frame != null && Frame.parent == Parent &&
                    (Frame.localPosition - Position).sqrMagnitude <= 1e-8f &&
                    (Frame.localScale - Scale).sqrMagnitude <= 1e-8f &&
                    Quaternion.Angle(Frame.localRotation, Rotation) <= 0.01f;
            }
        }

        private sealed class ParkProfileColliderState
        {
            public Collider Collider;
            public BuildingBlock Owner;
            public int Layer;
            public Vector3 Center, Size;
            public float ContactOffset;
            public ParkProfileMeshState MeshShape;
            public bool Convex;
            public readonly List<ParkProfileFrameState> Frames = new List<ParkProfileFrameState>();

            public static ParkProfileColliderState Capture(Collider collider, BuildingBlock owner)
            {
                var state = new ParkProfileColliderState { Collider = collider, Owner = owner,
                    Layer = collider.gameObject.layer, ContactOffset = collider.contactOffset };
                BoxCollider box = collider as BoxCollider;
                MeshCollider mesh = collider as MeshCollider;
                if (box != null)
                {
                    state.Center = box.center; state.Size = box.size;
                    if (!Coordinate(state.Center) || !Coordinate(state.Size) || state.Size.x <= 0 || state.Size.y <= 0 || state.Size.z <= 0)
                        throw new InvalidDataException("MEGA collision floor has invalid box geometry.");
                }
                else if (mesh != null)
                { state.MeshShape = ParkProfileMeshState.Capture(mesh.sharedMesh); state.Convex = mesh.convex; }
                else throw new InvalidDataException("MEGA collision requires supported native box or mesh floor colliders.");
                Transform frame = collider.transform;
                while (frame != owner.transform)
                {
                    if (frame == null || state.Frames.Count >= 32)
                        throw new InvalidDataException("MEGA floor collider has an unsupported owner-relative frame.");
                    state.Frames.Add(new ParkProfileFrameState { Frame = frame, Parent = frame.parent,
                        Position = frame.localPosition, Rotation = frame.localRotation, Scale = frame.localScale });
                    frame = frame.parent;
                }
                return state;
            }

            public bool Matches()
            {
                if (Collider == null || Owner == null || Collider.enabled || Collider.isTrigger || !Collider.gameObject.activeInHierarchy ||
                    Collider.gameObject.layer != Layer || !(Math.Abs(Collider.contactOffset - ContactOffset) <= 1e-6f) ||
                    !ReferenceEquals(Collider.ToBaseEntity(), Owner) || Frames.Any(frame => !frame.Matches())) return false;
                BoxCollider box = Collider as BoxCollider;
                if (box != null) return MeshShape == null &&
                    (box.center - Center).sqrMagnitude <= 1e-8f && (box.size - Size).sqrMagnitude <= 1e-8f;
                MeshCollider mesh = Collider as MeshCollider;
                return mesh != null && MeshShape != null && mesh.convex == Convex && MeshShape.Matches(mesh.sharedMesh);
            }
        }

        private static bool ParkOwnedHalfpipe(SkateParkKind kind)
        { return kind == SkateParkKind.MegaHalfpipe || kind == SkateParkKind.Halfpipe; }

        private static bool ParkOwnedHalfpipe(string kind)
        { return kind == "mega-halfpipe" || kind == "halfpipe"; }

        private static int ParkProfileFloorStart(SkateParkKind kind)
        { return kind == SkateParkKind.Halfpipe ? 4 : 0; }

        private static int ParkProfileFloorCount(SkateParkKind kind)
        { return kind == SkateParkKind.Halfpipe ? 8 : 64; }

        private static BuildingGrade.Enum ParkProfileGrade(SkateParkKind kind, int index)
        {
            if (kind == SkateParkKind.Halfpipe)
                return index < 4 ? BuildingGrade.Enum.Stone : index >= 16 && index < 20 ? BuildingGrade.Enum.Wood : BuildingGrade.Enum.Metal;
            return index < 16 ? BuildingGrade.Enum.Stone : index < 72 ? BuildingGrade.Enum.Metal : BuildingGrade.Enum.Wood;
        }

        private bool ParkMegaCollisionHealthy(ParkGroup group, ParkProfileCollision collision,
            ParkMember[] parts = null, IList<BuildingBlock> verifiedBlocks = null)
        {
            SkateParkKind kind;
            if (group == null || !SkateParkLayout.TryKind(group.Kind, out kind) || !ParkOwnedHalfpipe(kind) || collision == null ||
                collision.Identity == null || collision.Identity.Id != group.Id || collision.Identity.Kind != group.Kind ||
                collision.Identity.Members.Count != SkateParkLayout.PartCount(kind) ||
                collision.Members.Count != SkateParkLayout.PartCount(kind) ||
                collision.OriginalShapes.Count < ParkProfileFloorCount(kind) || collision.Patches.Count != ParkProfileFloorCount(kind)) return false;
            if (parts == null) parts = group.Members.OrderBy(member => member.Index).ToArray();
            if (parts.Length != SkateParkLayout.PartCount(kind) || (verifiedBlocks != null && verifiedBlocks.Count != parts.Length)) return false;
            for (int i = 0; i < parts.Length; i++)
            {
                BuildingBlock block = verifiedBlocks == null ? FindParkBlock(parts[i].Id) : verifiedBlocks[i];
                if (parts[i].Index != i || !ParkMatches(block, group, parts[i]) || !ReferenceEquals(block, collision.Members[i]) ||
                    !ParkMatches(block, collision.Identity, collision.Identity.Members[i]) ||
                    block.grade != ParkProfileGrade(kind, i) || block.skinID != 0 || block.transform.lossyScale != Vector3.one) return false;
            }
            BuildingBlock first = collision.Members[0];
            float datum = first.bounds.max.y;
            if (float.IsNaN(datum) || float.IsInfinity(datum) || datum <= 0 || datum >= 0.5f || Math.Abs(datum - collision.Datum) > 1e-5f ||
                collision.OriginalShapes.Any(shape => !shape.Matches())) return false;
            Vector3 origin = new Vector3((float)group.AnchorX, (float)group.AnchorY + datum, (float)group.AnchorZ);
            Quaternion rotation = Quaternion.Euler(0, (float)group.AnchorYaw, 0);
            int layer = LayerMask.NameToLayer("Construction");
            for (int i = 0; i < collision.Patches.Count; i++)
            {
                int ownerIndex = ParkProfileFloorStart(kind) + i;
                ParkProfilePatch patch = collision.Patches[i];
                if (patch.Index != ownerIndex || patch.Root == null || !patch.Root.activeInHierarchy || patch.Mesh == null ||
                    patch.Shell == null || !patch.Shell.enabled || patch.Shell.isTrigger || patch.Shell.convex ||
                    patch.Shell.sharedMesh != patch.Mesh || patch.Shape == null || !patch.Shape.Matches(patch.Mesh) ||
                    !ReferenceEquals(patch.Shell.ToBaseEntity(), collision.Members[ownerIndex]) ||
                    patch.Root.transform.parent != collision.Members[ownerIndex].transform || patch.Root.transform.lossyScale != Vector3.one ||
                    patch.Root.layer != layer || !((patch.Root.transform.position - origin).sqrMagnitude <= 4e-6f) ||
                    !(Quaternion.Angle(patch.Root.transform.rotation, rotation) <= 0.01f)) return false;
                Collider[] shellColliders = patch.Root.GetComponentsInChildren<Collider>(true);
                if (shellColliders.Length != 1 || !ReferenceEquals(shellColliders[0], patch.Shell)) return false;
            }
            foreach (BuildingBlock block in collision.Members.Skip(ParkProfileFloorStart(kind)).Take(ParkProfileFloorCount(kind)))
                foreach (Collider collider in block.GetComponentsInChildren<Collider>(true))
                    if (collider != null && collider.enabled && !collider.isTrigger && collider.gameObject.activeInHierarchy &&
                        !ParkProfileOwnsCollider(collision, collider) && ReferenceEquals(collider.ToBaseEntity(), block)) return false;
            return true;
        }

        private static bool ParkProfileOwnsCollider(ParkProfileCollision collision, Collider collider)
        {
            if (collision.Root != null && collider.transform.IsChildOf(collision.Root.transform)) return true;
            foreach (ParkProfilePatch patch in collision.Patches)
                if (patch.Root != null && collider.transform.IsChildOf(patch.Root.transform)) return true;
            return false;
        }

        private const string ParkRestoreMarkerPrefix = "ShortcutSkate pending MEGA restoration ";

        private static bool ParkRestoreMarkerExists(string marker)
        {
            return Resources.FindObjectsOfTypeAll<GameObject>().Any(value => value != null && value.name == marker);
        }

        private static bool ParkProfileRestorePending(ParkGroup group)
        {
            return group != null && ParkRestoreMarkerExists(ParkRestoreMarkerPrefix + group.Id);
        }

        private bool ParkProfileRestoreHeld(BaseEntity entity)
        {
            if (entity == null || entity.IsDestroyed) return false;
            string marker; ulong id = ParkId(entity);
            if (parkProfileRestoreHolds.TryGetValue(id, out marker))
            {
                if (ParkRestoreMarkerExists(marker)) return true;
                parkProfileRestoreHolds.Remove(id);
            }
            // A fresh plugin assembly has no old instance cache. Retained metadata plus the
            // assembly-neutral Unity marker still protects held members after a reload.
            if (id != 0 && parkData != null && parkData.Groups != null && parkData.Groups.Any(group =>
                group.Members.Any(member => member.Id == id) && ParkProfileRestorePending(group))) return true;
            if (id != 0 && parkData != null && !parkDataBroken && parkData.Groups != null &&
                parkData.Groups.Any(group => group.Members.Any(member => member.Id == id))) return false;
            GameObject[] pending = Resources.FindObjectsOfTypeAll<GameObject>().Where(value => value != null &&
                value.activeInHierarchy && value.name.StartsWith(ParkRestoreMarkerPrefix, StringComparison.Ordinal)).ToArray();
            if (pending.Length == 0) return false;
            // Missing group metadata grants denial only, including unsaved placements.
            // A bounded marker region never grants
            // ownership, adoption, restoration or deletion authority over this entity.
            try
            {
                Bounds body = entity.WorldSpaceBounds().ToBounds();
                if (!Coordinate(body.center) || !Coordinate(body.size) || body.size.x <= 0 || body.size.y <= 0 || body.size.z <= 0) return true;
                foreach (GameObject value in pending)
                {
                    Vector3 center = value.transform.position, size = value.transform.localScale;
                    if (!Coordinate(center) || !Coordinate(size) || size.x <= 0 || size.y <= 0 || size.z <= 0 ||
                        new Bounds(center, size).Intersects(body)) return true;
                }
            }
            catch (Exception) { return true; }
            return false;
        }

        private static ParkGroup ParkRestoreIdentity(ParkGroup group, IEnumerable<ParkMember> members)
        {
            return new ParkGroup { Id = group.Id, Kind = group.Kind, Owner = group.Owner,
                AnchorX = group.AnchorX, AnchorY = group.AnchorY, AnchorZ = group.AnchorZ, AnchorYaw = group.AnchorYaw,
                Members = members.Select(part => new ParkMember { Index = part.Index, Id = part.Id, Prefab = part.Prefab,
                    X = part.X, Y = part.Y, Z = part.Z, Yaw = part.Yaw, Pitch = part.Pitch }).ToList() };
        }

        private static bool ParkRestoreLocalBounds(Collider collider, out Bounds bounds)
        {
            bounds = new Bounds();
            BoxCollider box = collider as BoxCollider;
            MeshCollider mesh = collider as MeshCollider;
            if (box != null) bounds = new Bounds(box.center, box.size);
            else if (mesh != null && mesh.sharedMesh != null) bounds = mesh.sharedMesh.bounds;
            else return false;
            return Coordinate(bounds.center) && Coordinate(bounds.size) &&
                bounds.size.x > 0 && bounds.size.y > 0 && bounds.size.z > 0;
        }

        private static bool ParkRestoreOriginalOwned(ParkProfileCollision collision, Collider collider)
        {
            ParkProfileColliderState state = collision.OriginalShapes.FirstOrDefault(value => ReferenceEquals(value.Collider, collider));
            if (state == null || state.Owner == null || collision.Identity == null ||
                !ReferenceEquals(collider.ToBaseEntity(), state.Owner)) return false;
            int index = collision.Members.FindIndex(value => ReferenceEquals(value, state.Owner));
            return index >= 0 && index < collision.Identity.Members.Count &&
                ParkMatches(state.Owner, collision.Identity, collision.Identity.Members[index]);
        }

        private static bool ParkProspectiveBounds(ParkProfileCollision collision, out List<Bounds> values)
        {
            values = new List<Bounds>();
            foreach (Collider collider in collision.Disabled)
            {
                if (collider == null)
                {
                    if (!ParkRestoreMissingOriginalSafe(collision, collider)) return false;
                    continue;
                }
                if (collider.enabled) continue;
                if (!ParkRestoreOriginalOwned(collision, collider)) return false;
                Bounds local;
                if (!ParkRestoreLocalBounds(collider, out local)) return false;
                Bounds world = new Bounds(collider.transform.TransformPoint(local.min), Vector3.zero);
                for (int i = 1; i < 8; i++)
                {
                    Vector3 point = collider.transform.TransformPoint(new Vector3((i & 1) == 0 ? local.min.x : local.max.x,
                        (i & 2) == 0 ? local.min.y : local.max.y, (i & 4) == 0 ? local.min.z : local.max.z));
                    if (!Coordinate(point)) return false;
                    world.Encapsulate(point);
                }
                if (!Coordinate(world.center) || !Coordinate(world.size)) return false;
                world.Expand(0.02f);
                values.Add(world);
            }
            return true;
        }

        private static bool ParkRestoreMissingOriginalSafe(ParkProfileCollision collision, Collider collider)
        {
            ParkProfileColliderState captured = collision.OriginalShapes.FirstOrDefault(value => ReferenceEquals(value.Collider, collider));
            // A live floor losing its collider is not proof of restored native support.
            // A destroyed member has already lost its surface independently of cleanup.
            return captured != null && (captured.Owner == null || captured.Owner.IsDestroyed);
        }

        private static BasePlayer[] ParkRestorePlayers()
        {
            return BasePlayer.activePlayerList.Concat(BasePlayer.sleepingPlayerList)
                .Where(player => player != null && !player.IsDestroyed).Distinct().ToArray();
        }

        private static bool ParkRestoreBody(BasePlayer player, out Bounds body)
        {
            body = player.WorldSpaceBounds().ToBounds();
            body.Encapsulate(player.GetBounds(false));
            body.Encapsulate(new Bounds(player.transform.position + Vector3.up * 0.6f, Vector3.one * 0.68f));
            body.Expand(0.02f);
            return Coordinate(body.center) && Coordinate(body.size) && body.size.x > 0 && body.size.y > 0 && body.size.z > 0;
        }

        private static bool ParkRestoreOccupied(List<Bounds> originals, IEnumerable<BasePlayer> players)
        {
            foreach (BasePlayer player in players)
            {
                Bounds body;
                if (!ParkRestoreBody(player, out body) || originals.Any(value => value.Intersects(body))) return true;
            }
            return false;
        }

        private static Bounds ParkRestoreEnvelope(ParkProfileCollision collision)
        {
            ParkGroup identity = collision.Identity;
            Bounds result = new Bounds(new Vector3((float)identity.AnchorX, (float)identity.AnchorY, (float)identity.AnchorZ), Vector3.zero);
            foreach (ParkMember part in identity.Members)
                result.Encapsulate(new Bounds(ParkPosition(part), Vector3.one * 4));
            result.Expand(2f);
            return result;
        }

        private static bool ParkRestoreHorizontalOverlap(Bounds body, Bounds envelope)
        {
            return body.min.x <= envelope.max.x && body.max.x >= envelope.min.x &&
                body.min.z <= envelope.max.z && body.max.z >= envelope.min.z;
        }

        private static bool ParkRetainedMegaProfileHealthy(ParkProfileCollision collision)
        {
            ParkGroup identity = collision.Identity;
            SkateParkKind kind;
            if (identity == null || !SkateParkLayout.TryKind(identity.Kind, out kind) || !ParkOwnedHalfpipe(kind) ||
                identity.Members.Count != SkateParkLayout.PartCount(kind) || collision.Members.Count != SkateParkLayout.PartCount(kind) ||
                collision.Patches.Count != ParkProfileFloorCount(kind)) return false;
            for (int i = 0; i < identity.Members.Count; i++)
                if (!ParkMatches(collision.Members[i], identity, identity.Members[i]) ||
                    collision.Members[i].grade != ParkProfileGrade(kind, i) || collision.Members[i].skinID != 0 ||
                    collision.Members[i].transform.lossyScale != Vector3.one) return false;
            Vector3 origin = new Vector3((float)identity.AnchorX, (float)identity.AnchorY + collision.Datum, (float)identity.AnchorZ);
            Quaternion rotation = Quaternion.Euler(0, (float)identity.AnchorYaw, 0);
            for (int i = 0; i < collision.Patches.Count; i++)
            {
                int ownerIndex = ParkProfileFloorStart(kind) + i;
                ParkProfilePatch patch = collision.Patches[i];
                if (patch.Index != ownerIndex || patch.Root == null || !patch.Root.activeInHierarchy || patch.Shell == null ||
                    !patch.Shell.enabled || patch.Shell.isTrigger || patch.Shell.convex || patch.Mesh == null ||
                    patch.Shell.sharedMesh != patch.Mesh || patch.Shape == null || !patch.Shape.Matches(patch.Mesh) ||
                    !ReferenceEquals(patch.Shell.ToBaseEntity(), collision.Members[ownerIndex]) ||
                    patch.Root.transform.parent != collision.Members[ownerIndex].transform || patch.Root.transform.lossyScale != Vector3.one ||
                    patch.Root.layer != LayerMask.NameToLayer("Construction") ||
                    !((patch.Root.transform.position - origin).sqrMagnitude <= 4e-6f) || !(Quaternion.Angle(patch.Root.transform.rotation, rotation) <= 0.01f)) return false;
                Collider[] owned = patch.Root.GetComponentsInChildren<Collider>(true);
                if (owned.Length != 1 || !ReferenceEquals(owned[0], patch.Shell)) return false;
            }
            return true;
        }

        private sealed class ParkRestoreExit
        {
            public BasePlayer Player;
            public Vector3 Position;
            public Bounds Body;
            public Collider Ground;
            public Vector3 GroundPoint;
        }

        private object OnStructureUpgrade(BuildingBlock block, BasePlayer player, BuildingGrade.Enum grade, ulong skin)
        {
            if (block == null) return null;
            ParkGroup group = parkProfileCollisions.FirstOrDefault(pair => ParkOwnedHalfpipe(pair.Key.Kind) &&
                pair.Value.Members.Any(member => ReferenceEquals(member, block))).Key;
            bool held = ParkProfileRestoreHeld(block);
            if (group == null && !held) return null;
            bool occupied = held;
            if (!occupied)
            {
                Bounds envelope = ParkRestoreEnvelope(parkProfileCollisions[group]);
                foreach (BasePlayer bodyPlayer in ParkRestorePlayers())
                {
                    Bounds body;
                    if (!ParkRestoreBody(bodyPlayer, out body) || body.Intersects(envelope)) { occupied = true; break; }
                }
            }
            if (occupied || !RemoveParkProfileCollision(group))
            {
                if (player != null) player.ConsoleMessage("Leave the MEGA park and retry the upgrade after pending collision restoration completes.");
                return false;
            }
            // Rust can still refuse this pre-cost hook's upgrade after we return.
            // Reconcile unchanged groups too, so a rejected upgrade cannot remove their riding surface.
            parkProfileRestoreRefreshNeeded = true;
            return null;
        }

        private static bool ParkRestoreExitCandidate(BasePlayer player, Bounds body, Vector3 hint, List<Bounds> originals,
            List<Bounds> exclusions, BasePlayer[] players, List<ParkRestoreExit> reserved, Collider[] scratch, out ParkRestoreExit exit)
        {
            exit = null;
            if (!Coordinate(hint)) return false;
            var hits = new RaycastHit[128];
            int hitCount = Physics.RaycastNonAlloc(hint + Vector3.up * 12, Vector3.down, hits, 40f, ~0, QueryTriggerInteraction.Ignore);
            if (hitCount >= hits.Length) return false;
            RaycastHit hit = new RaycastHit();
            bool found = false;
            for (int i = 0; i < hitCount; i++)
            {
                Collider collider = hits[i].collider;
                if (collider == null || collider.transform.IsChildOf(player.transform) || ReferenceEquals(collider.ToBaseEntity(), player)) continue;
                if (!found || hits[i].distance < hit.distance) { hit = hits[i]; found = true; }
            }
            if (!found ||
                hit.collider == null || !(hit.normal.y >= 0.98f) || !Coordinate(hit.point) ||
                WaterLevel.Test(hit.point + Vector3.up * 0.05f, true, true, player)) return false;
            BuildingBlock block = hit.collider.ToBaseEntity() as BuildingBlock;
            if (!(hit.collider is TerrainCollider) && (block == null || block.IsDestroyed || block.GetParentEntity() != null ||
                (block.ShortPrefabName != "foundation" && block.ShortPrefabName != "foundation.triangle" && block.ShortPrefabName != "floor") ||
                hit.collider.gameObject.name.StartsWith("ShortcutSkate", StringComparison.Ordinal))) return false;
            Vector3 position = hit.point + Vector3.up * (0.25f - (body.min.y - player.transform.position.y));
            Bounds proposed = new Bounds(body.center + position - player.transform.position, body.size);
            if (!Coordinate(position) || originals.Any(value => value.Intersects(proposed)) ||
                exclusions.Any(value => ParkRestoreHorizontalOverlap(proposed, value)) || reserved.Any(value => value.Body.Intersects(proposed))) return false;
            foreach (BasePlayer other in players)
            {
                if (ReferenceEquals(other, player)) continue;
                Bounds otherBody;
                if (!ParkRestoreBody(other, out otherBody) || otherBody.Intersects(proposed)) return false;
            }
            int count = Physics.OverlapBoxNonAlloc(proposed.center, proposed.extents, scratch, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            if (count >= scratch.Length) return false;
            for (int i = 0; i < count; i++)
                if (scratch[i] != null && !scratch[i].transform.IsChildOf(player.transform)) return false;
            exit = new ParkRestoreExit { Player = player, Position = position, Body = proposed, Ground = hit.collider, GroundPoint = hit.point };
            return true;
        }

        private bool ParkTryRestoreExits(ParkProfileCollision collision, List<Bounds> originals, BasePlayer[] players)
        {
            var affected = new List<BasePlayer>();
            foreach (BasePlayer player in players)
            {
                Bounds body;
                if (!ParkRestoreBody(player, out body)) return false;
                if (originals.Any(value => value.Intersects(body))) affected.Add(player);
            }
            if (affected.Count == 0) return true;
            if (affected.Count > 128) return false;
            var exclusions = new List<Bounds>(); var allOriginals = new List<Bounds>(originals);
            foreach (ParkProfileCollision current in parkProfileCollisions.Values.Where(value => value.Identity != null))
            {
                exclusions.Add(ParkRestoreEnvelope(current));
                List<Bounds> otherBounds;
                if (!ParkProspectiveBounds(current, out otherBounds)) return false;
                allOriginals.AddRange(otherBounds);
            }
            foreach (GameObject marker in Resources.FindObjectsOfTypeAll<GameObject>())
                if (marker != null && marker.name.StartsWith(ParkRestoreMarkerPrefix, StringComparison.Ordinal))
                    exclusions.Add(new Bounds(marker.transform.position, marker.transform.localScale));
            Bounds envelope = ParkRestoreEnvelope(collision);
            var hints = new List<Vector3>();
            foreach (float offset in new[] { 2f, 5f, 10f })
                for (int i = 0; i < 16; i++)
                {
                    float angle = i * Mathf.PI / 8;
                    hints.Add(new Vector3(envelope.center.x + (envelope.extents.x + offset) * Mathf.Cos(angle),
                        envelope.max.y, envelope.center.z + (envelope.extents.z + offset) * Mathf.Sin(angle)));
                }
            // Pose sampling only: never Respawn, ResetPlayerState or an inventory/progress operation.
            for (int i = 0; i < 2; i++)
            {
                BasePlayer.SpawnPoint point = SpawnHandler.GetSpawnPoint();
                if (point == null || !Coordinate(point.pos)) continue;
                for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
                    hints.Add(point.pos + new Vector3(x * 2, 0, z * 2));
            }
            var exits = new List<ParkRestoreExit>(); var scratch = new Collider[128];
            foreach (BasePlayer player in affected)
            {
                if (!player.IsAlive() || player.isMounted || player.GetParentEntity() != null) return false;
                Bounds body;
                if (!ParkRestoreBody(player, out body)) return false;
                ParkRestoreExit selected = null;
                foreach (Vector3 hint in hints)
                    if (ParkRestoreExitCandidate(player, body, hint, allOriginals, exclusions, players, exits, scratch, out selected)) break;
                if (selected == null) return false;
                exits.Add(selected);
            }
            // Every destination is reserved and verified before the first body moves.
            foreach (ParkRestoreExit exit in exits)
            {
                if (exit.Player == null || exit.Player.IsDestroyed || exit.Player.isMounted || exit.Player.GetParentEntity() != null ||
                    exit.Ground == null || !exit.Ground.enabled || !exit.Ground.gameObject.activeInHierarchy) return false;
                Bounds body; ParkRestoreExit rechecked;
                if (!ParkRestoreBody(exit.Player, out body) || !ParkRestoreExitCandidate(exit.Player, body, exit.GroundPoint,
                    allOriginals, exclusions, ParkRestorePlayers(), exits.Where(value => !ReferenceEquals(value, exit)).ToList(), scratch, out rechecked) ||
                    !ReferenceEquals(rechecked.Ground, exit.Ground) || (rechecked.Position - exit.Position).sqrMagnitude > 4e-6f) return false;
                Stop(exit.Player, SkateSessionReason.Unload);
                exit.Player.Teleport(exit.Position);
            }
            Physics.SyncTransforms();
            foreach (ParkRestoreExit exit in exits)
            {
                Bounds body; ParkRestoreExit rechecked;
                if (exit.Player == null || !ParkRestoreBody(exit.Player, out body) ||
                    !ParkRestoreExitCandidate(exit.Player, body, exit.GroundPoint, allOriginals, exclusions, ParkRestorePlayers(),
                        exits.Where(value => !ReferenceEquals(value, exit)).ToList(), scratch, out rechecked) ||
                    !ReferenceEquals(rechecked.Ground, exit.Ground) ||
                    (rechecked.Position - exit.Player.transform.position).sqrMagnitude > 4e-6f) return false;
            }
            return !ParkRestoreOccupied(originals, ParkRestorePlayers());
        }

        private static bool ParkRestoreComplete(ParkProfileCollision collision)
        {
            // The retained list never grants authority over a collider reparented elsewhere.
            foreach (Collider collider in collision.Disabled)
            {
                if (collider == null)
                {
                    if (!ParkRestoreMissingOriginalSafe(collision, collider)) return false;
                    continue;
                }
                if (collider.enabled) continue;
                if (!ParkRestoreOriginalOwned(collision, collider)) return false;
            }
            foreach (Collider collider in collision.Disabled) if (collider != null && !collider.enabled) collider.enabled = true;
            Physics.SyncTransforms();
            foreach (ParkProfilePatch patch in collision.Patches) if (patch.Root != null) patch.Root.SetActive(false);
            if (collision.Root != null) collision.Root.SetActive(false);
            Physics.SyncTransforms();
            foreach (ParkProfilePatch patch in collision.Patches)
            {
                if (patch.Root != null) UnityEngine.Object.Destroy(patch.Root);
                if (patch.Mesh != null) UnityEngine.Object.Destroy(patch.Mesh);
            }
            if (collision.Root != null) UnityEngine.Object.Destroy(collision.Root);
            if (collision.Mesh != null) UnityEngine.Object.Destroy(collision.Mesh);
            return true;
        }

        private bool ParkDeferProfileRestore(ParkGroup group, ParkProfileCollision collision)
        {
            string marker = ParkRestoreMarkerPrefix + group.Id;
            bool verified;
            try { verified = ParkRetainedMegaProfileHealthy(collision); }
            catch (Exception) { verified = false; }
            GameObject owner = new GameObject(marker);
            owner.SetActive(false);
            owner.AddComponent<ParkProfileRestoreOwner>().Initialize(group, collision, verified);
            foreach (BuildingBlock member in collision.Members)
                if (member != null && ParkId(member) != 0) parkProfileRestoreHolds[ParkId(member)] = marker;
            parkProfileCollisions.Remove(group);
            owner.SetActive(true);
            PrintWarning("MEGA native floor restoration is pending; an independent Unity owner retains the collision record until body clearance is verified.");
            return false;
        }

        private sealed class ParkProfileRestoreOwner : MonoBehaviour
        {
            private ParkProfileCollision collision;
            private float nextCheck;
            private bool complete, quitting, warned;

            public void Initialize(ParkGroup group, ParkProfileCollision value, bool verified)
            {
                collision = value;
                gameObject.name = ParkRestoreMarkerPrefix + group.Id;
                Bounds envelope = ParkRestoreEnvelope(value);
                transform.position = envelope.center; transform.localScale = envelope.size;
                if (!verified) WarnIntegrity();
            }

            private void WarnIntegrity()
            {
                if (warned) return;
                warned = true;
                UnityEngine.Debug.LogError("ShortcutSkate MEGA restoration is pending with unverified replacement integrity; no occupied native colliders were force-enabled.");
            }

            private void Update()
            {
                if (complete || collision == null || UnityEngine.Time.realtimeSinceStartup < nextCheck) return;
                nextCheck = UnityEngine.Time.realtimeSinceStartup + 0.25f;
                try
                {
                    BasePlayer[] players = ParkRestorePlayers(); List<Bounds> originals;
                    if (players.Length != 0 && (!ParkProspectiveBounds(collision, out originals) || ParkRestoreOccupied(originals, players)))
                    {
                        if (!ParkRetainedMegaProfileHealthy(collision)) WarnIntegrity();
                        return;
                    }
                    if (!ParkRestoreComplete(collision)) { WarnIntegrity(); return; }
                    complete = true; collision = null;
                    UnityEngine.Debug.Log("ShortcutSkate pending MEGA native floor restoration completed after verified body clearance.");
                    UnityEngine.Object.Destroy(gameObject);
                }
                catch (Exception) { WarnIntegrity(); }
            }

            private void OnApplicationQuit() { quitting = true; }

            private void OnDestroy()
            {
                if (complete || quitting || collision == null) return;
                // An independent Unity owner must not silently lose its held record on plugin unload.
                GameObject replacement = new GameObject(gameObject.name);
                replacement.SetActive(false);
                replacement.AddComponent<ParkProfileRestoreOwner>().Initialize(collision.Identity, collision, false);
                replacement.SetActive(true);
            }
        }

        private bool RemoveParkProfileCollision(ParkGroup group)
        {
            ParkProfileCollision collision;
            if (!parkProfileCollisions.TryGetValue(group, out collision)) return !ParkProfileRestorePending(group);
            if (ParkOwnedHalfpipe(group.Kind))
            {
                // Partial activation may have left every native collider enabled. Removing
                // unused replacement objects cannot introduce a new native-body overlap.
                bool clear = !collision.Disabled.Any(collider => collider != null && !collider.enabled);
                try
                {
                    BasePlayer[] players = ParkRestorePlayers(); List<Bounds> originals;
                    if (!clear) clear = players.Length == 0 || (ParkProspectiveBounds(collision, out originals) &&
                        (!ParkRestoreOccupied(originals, players) || ParkTryRestoreExits(collision, originals, players)));
                    // Re-read every current body immediately before changing native collision.
                    players = ParkRestorePlayers();
                    if (clear && players.Length != 0 && collision.Disabled.Any(collider => collider != null && !collider.enabled))
                        clear = ParkProspectiveBounds(collision, out originals) && !ParkRestoreOccupied(originals, players);
                }
                catch (Exception) { clear = false; }
                if (!clear) return ParkDeferProfileRestore(group, collision);
                if (!ParkRestoreComplete(collision)) return ParkDeferProfileRestore(group, collision);
                parkProfileCollisions.Remove(group);
                return true;
            }
            parkProfileCollisions.Remove(group);
            foreach (Collider collider in collision.Disabled)
                if (collider != null) collider.enabled = true;
            foreach (ParkProfilePatch patch in collision.Patches)
                if (patch.Root != null) patch.Root.SetActive(false);
            if (collision.Root != null) collision.Root.SetActive(false);
            foreach (ParkProfilePatch patch in collision.Patches)
            {
                if (patch.Root != null) UnityEngine.Object.Destroy(patch.Root);
                if (patch.Mesh != null) UnityEngine.Object.Destroy(patch.Mesh);
            }
            if (collision.Root != null) UnityEngine.Object.Destroy(collision.Root);
            if (collision.Mesh != null) UnityEngine.Object.Destroy(collision.Mesh);
            return true;
        }

        private static void ParkQuarterQuad(List<Vector3> vertices, List<int> triangles,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2);
            triangles.Add(i); triangles.Add(i + 2); triangles.Add(i + 3);
        }

        private static Mesh ParkQuarterMesh(SkateParkKind kind, SkateParkQuarterProfile profile)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            float w = (float)profile.HalfWidth, bottom = (float)-profile.Skin;
            double z0, y0;
            if (!SkateParkLayout.TryQuarterPoint(kind, 0, out z0, out y0))
                throw new InvalidDataException("Quarterpipe profile start is unavailable.");
            for (int i = 1; i < SkateParkLayout.QuarterPointCount(kind); i++)
            {
                double z1, y1;
                if (!SkateParkLayout.TryQuarterPoint(kind, i, out z1, out y1))
                    throw new InvalidDataException("Quarterpipe profile sample is unavailable.");
                Vector3 a = new Vector3(-w, (float)y0, (float)z0), b = new Vector3(w, (float)y0, (float)z0);
                Vector3 c = new Vector3(-w, (float)y1, (float)z1), d = new Vector3(w, (float)y1, (float)z1);
                ParkQuarterQuad(vertices, triangles, a, c, d, b);
                if (z1 > z0)
                {
                    ParkQuarterQuad(vertices, triangles, a, new Vector3(-w, bottom, (float)z0),
                        new Vector3(-w, bottom, (float)z1), c);
                    ParkQuarterQuad(vertices, triangles, b, d, new Vector3(w, bottom, (float)z1),
                        new Vector3(w, bottom, (float)z0));
                }
                z0 = z1; y0 = y1;
            }
            float start = (float)profile.StartForward, end = (float)profile.EndForward, top = (float)profile.Height;
            ParkQuarterQuad(vertices, triangles, new Vector3(-w, bottom, start), new Vector3(w, bottom, start),
                new Vector3(w, bottom, end), new Vector3(-w, bottom, end));
            ParkQuarterQuad(vertices, triangles, new Vector3(-w, top, end), new Vector3(-w, bottom, end),
                new Vector3(w, bottom, end), new Vector3(w, top, end));
            ParkQuarterQuad(vertices, triangles, new Vector3(-w, 0, start), new Vector3(w, 0, start),
                new Vector3(w, bottom, start), new Vector3(-w, bottom, start));
            var mesh = new Mesh { name = "ShortcutSkate quarterpipe closed shell" };
            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh ParkMegaMesh(int floorIndex)
        {
            if (floorIndex < 0 || floorIndex >= 64) throw new ArgumentOutOfRangeException("floorIndex");
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var points = new List<Vector2>();
            int lane = floorIndex % SkateMegaHalfpipe.Lanes;
            float left = lane * 3 - 1.5f, right = left + 3, bottom = -0.1f;
            if (floorIndex < 16)
            {
                float forward = (float)(-SkateMegaHalfpipe.FlatLength / 2 + floorIndex / SkateMegaHalfpipe.Lanes * SkateMegaHalfpipe.Tile);
                points.Add(new Vector2(forward, (float)SkateMegaHalfpipe.RideLift));
                points.Add(new Vector2(forward + 3, (float)SkateMegaHalfpipe.RideLift));
            }
            else
            {
                int first, last;
                if (floorIndex < 56)
                {
                    int segment = (floorIndex - 16) % 20 / SkateMegaHalfpipe.Lanes;
                    int steps = SkateMegaHalfpipe.CurveSteps / SkateMegaHalfpipe.TransitionSegments;
                    first = floorIndex < 36 ? SkateMegaHalfpipe.CurveSteps + 1 - (segment + 1) * steps :
                        SkateMegaHalfpipe.CurveSteps + 2 + segment * steps;
                    last = first + steps;
                }
                else { first = floorIndex < 60 ? 0 : SkateMegaHalfpipe.ProfilePointCount - 2; last = first + 1; }
                for (int i = first; i <= last; i++)
                {
                    double forward, height;
                    if (!SkateMegaHalfpipe.TryProfilePoint(i, out forward, out height))
                        throw new InvalidDataException("MEGA floor profile samples are unavailable.");
                    points.Add(new Vector2((float)forward, (float)height));
                }
            }
            float start = points[0].x, startTop = points[0].y;
            Vector2 previous = points[0];
            for (int i = 1; i < points.Count; i++)
            {
                Vector2 next = points[i];
                if (next.x <= previous.x) throw new InvalidDataException("MEGA floor profile samples are unordered.");
                Vector3 a = new Vector3(left, previous.y, previous.x), b = new Vector3(right, previous.y, previous.x);
                Vector3 c = new Vector3(left, next.y, next.x), d = new Vector3(right, next.y, next.x);
                Vector3 ab = new Vector3(left, bottom, previous.x), bb = new Vector3(right, bottom, previous.x);
                Vector3 cb = new Vector3(left, bottom, next.x), db = new Vector3(right, bottom, next.x);
                ParkQuarterQuad(vertices, triangles, a, c, d, b);
                // The complete union is closed. Internal floor/lane boundaries have
                // no vertical faces that could act as walls during a seam crossing.
                if (lane == 0) ParkQuarterQuad(vertices, triangles, a, ab, cb, c);
                if (lane == SkateMegaHalfpipe.Lanes - 1) ParkQuarterQuad(vertices, triangles, b, d, db, bb);
                ParkQuarterQuad(vertices, triangles, ab, bb, db, cb);
                previous = next;
            }
            if (floorIndex >= 56 && floorIndex < 60)
                ParkQuarterQuad(vertices, triangles, new Vector3(left, startTop, start), new Vector3(right, startTop, start),
                    new Vector3(right, bottom, start), new Vector3(left, bottom, start));
            if (floorIndex >= 60)
                ParkQuarterQuad(vertices, triangles, new Vector3(left, previous.y, previous.x), new Vector3(left, bottom, previous.x),
                    new Vector3(right, bottom, previous.x), new Vector3(right, previous.y, previous.x));
            var mesh = new Mesh { name = "ShortcutSkate MEGA floor profile " + floorIndex.ToString(CultureInfo.InvariantCulture) };
            try
            {
                mesh.vertices = vertices.ToArray();
                mesh.triangles = triangles.ToArray();
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
            catch
            {
                UnityEngine.Object.Destroy(mesh);
                throw;
            }
        }

        private static Mesh ParkHalfpipeMesh(int floorIndex)
        {
            if (floorIndex < 4 || floorIndex >= 12) throw new ArgumentOutOfRangeException("floorIndex");
            // Shared samples are the existing client 0.18.18 centre-of-flat profile,
            // before its 2 cm lift. Preserve every facet; new owner cuts interpolate
            // those facets, rather than resampling an ideal circle.
            if (SkateHalfpipe.ProfilePointCount != 40 || !(Math.Abs(SkateHalfpipe.Lift - 0.02) <= 1e-9))
                throw new InvalidDataException("Regular halfpipe profile contract is unavailable.");
            var profile = new Vector2[SkateHalfpipe.ProfilePointCount];
            for (int i = 0; i < profile.Length; i++)
            {
                double forward, height;
                if (!SkateHalfpipe.TryProfilePoint(i, out forward, out height) ||
                    !Coordinate(new Vector3(0, (float)height, (float)forward)))
                    throw new InvalidDataException("Regular halfpipe profile samples are unavailable.");
                profile[i] = new Vector2((float)forward, (float)height);
                if (i != 0 && !(profile[i].x > profile[i - 1].x))
                    throw new InvalidDataException("Regular halfpipe profile samples are unordered.");
            }
            bool back = floorIndex < 8;
            bool upper = floorIndex == 6 || floorIndex == 7 || floorIndex >= 10;
            float cut = (float)((back ? -1 : 1) * (3 + 3 * Math.Cos(15 * Math.PI / 180)));
            // Include the full rounded toe on the flat; the native flat/deck remain
            // solid. Only the true outer toe/lip boundaries receive vertical caps.
            float from = back ? profile[1].x : profile[20].x;
            float to = back ? profile[19].x : profile[38].x;
            if (back ? upper : !upper) to = cut;
            else from = cut;
            bool capStart = back ? upper : !upper;
            bool capEnd = !capStart;
            int lane = floorIndex % 2;
            float left = lane * 3 - 1.5f, right = left + 3, bottom = -0.1f;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            bool found = false;
            Vector2 first = new Vector2(), last = new Vector2();
            for (int i = 1; i < profile.Length; i++)
            {
                Vector2 previous = profile[i - 1], next = profile[i];
                float start = Math.Max(from, previous.x), end = Math.Min(to, next.x);
                if (!(end > start)) continue;
                // Arithmetic stays in the client's float profile domain. Both
                // neighbours use the same endpoints/cut and hence identical seams.
                float startHeight = start == previous.x ? previous.y : previous.y + (next.y - previous.y) * ((start - previous.x) / (next.x - previous.x));
                float endHeight = end == next.x ? next.y : previous.y + (next.y - previous.y) * ((end - previous.x) / (next.x - previous.x));
                Vector2 aPoint = new Vector2(start + 1.5f, startHeight + (float)SkateHalfpipe.Lift);
                Vector2 bPoint = new Vector2(end + 1.5f, endHeight + (float)SkateHalfpipe.Lift);
                if (!found) { first = aPoint; found = true; }
                last = bPoint;
                Vector3 a = new Vector3(left, aPoint.y, aPoint.x), b = new Vector3(right, aPoint.y, aPoint.x);
                Vector3 c = new Vector3(left, bPoint.y, bPoint.x), d = new Vector3(right, bPoint.y, bPoint.x);
                Vector3 ab = new Vector3(left, bottom, aPoint.x), bb = new Vector3(right, bottom, aPoint.x);
                Vector3 cb = new Vector3(left, bottom, bPoint.x), db = new Vector3(right, bottom, bPoint.x);
                ParkQuarterQuad(vertices, triangles, a, c, d, b);
                if (lane == 0) ParkQuarterQuad(vertices, triangles, a, ab, cb, c);
                if (lane == 1) ParkQuarterQuad(vertices, triangles, b, d, db, bb);
                ParkQuarterQuad(vertices, triangles, ab, bb, db, cb);
            }
            if (!found || !(last.x > first.x))
                throw new InvalidDataException("Regular halfpipe patch has no riding facets.");
            if (capStart)
                ParkQuarterQuad(vertices, triangles, new Vector3(left, first.y, first.x), new Vector3(right, first.y, first.x),
                    new Vector3(right, bottom, first.x), new Vector3(left, bottom, first.x));
            if (capEnd)
                ParkQuarterQuad(vertices, triangles, new Vector3(left, last.y, last.x), new Vector3(left, bottom, last.x),
                    new Vector3(right, bottom, last.x), new Vector3(right, last.y, last.x));
            var mesh = new Mesh { name = "ShortcutSkate regular halfpipe transition profile " + floorIndex.ToString(CultureInfo.InvariantCulture) };
            try
            {
                mesh.vertices = vertices.ToArray();
                mesh.triangles = triangles.ToArray();
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
            catch
            {
                UnityEngine.Object.Destroy(mesh);
                throw;
            }
        }

        private static bool ParkProfileReplacementBodiesClear(ParkProfileCollision collision)
        {
            var volumes = new List<Bounds>();
            foreach (ParkProfilePatch patch in collision.Patches)
            {
                if (patch.Root == null || patch.Mesh == null) return false;
                Bounds local = patch.Mesh.bounds;
                if (!Coordinate(local.center) || !Coordinate(local.size)) return false;
                Bounds world = new Bounds(patch.Root.transform.TransformPoint(local.min), Vector3.zero);
                for (int i = 1; i < 8; i++)
                {
                    Vector3 point = patch.Root.transform.TransformPoint(new Vector3((i & 1) == 0 ? local.min.x : local.max.x,
                        (i & 2) == 0 ? local.min.y : local.max.y, (i & 4) == 0 ? local.min.z : local.max.z));
                    if (!Coordinate(point)) return false;
                    world.Encapsulate(point);
                }
                if (!Coordinate(world.center) || !Coordinate(world.size)) return false;
                world.Expand(0.02f);
                volumes.Add(world);
            }
            if (volumes.Count != 8) return false;
            foreach (BasePlayer player in ParkRestorePlayers())
            {
                Bounds body;
                if (!ParkRestoreBody(player, out body) || volumes.Any(volume => volume.Intersects(body))) return false;
            }
            return true;
        }

        private void EnsureParkProfileCollision(SkateParkKind kind, ParkGroup group, IEnumerable<ParkMember> members, Bounds floor)
        {
            bool mega = kind == SkateParkKind.MegaHalfpipe;
            bool ownedHalfpipe = ParkOwnedHalfpipe(kind);
            // Older plugin assemblies can still own a deferred restore. Never capture their
            // replacement colliders as if they were the native floor or create a second owner.
            if (ownedHalfpipe && ParkProfileRestorePending(group)) throw new ParkProfileRestorePendingException();
            SkateParkQuarterProfile profile = new SkateParkQuarterProfile();
            if (!ownedHalfpipe && !SkateParkLayout.TryQuarterProfile(kind, out profile)) return;
            ParkMember[] parts = members.OrderBy(member => member.Index).ToArray();
            if (parts.Length != SkateParkLayout.PartCount(kind))
            { if (!RemoveParkProfileCollision(group)) throw new ParkProfileRestorePendingException(); return; }
            var blocks = new List<BuildingBlock>();
            for (int i = 0; i < parts.Length; i++)
            {
                BuildingBlock block = FindParkBlock(parts[i].Id);
                if (parts[i].Index != i || !ParkMatches(block, group, parts[i]))
                {
                    if (!RemoveParkProfileCollision(group)) throw new ParkProfileRestorePendingException();
                    throw new InvalidDataException("Park profile collision requires the complete verified native group.");
                }
                if (ownedHalfpipe && (block.grade != ParkProfileGrade(kind, i) ||
                    block.skinID != 0 || block.transform.lossyScale != Vector3.one))
                {
                    if (!RemoveParkProfileCollision(group)) throw new ParkProfileRestorePendingException();
                    throw new ParkProfileChangedException("Halfpipe collision requires the original native grades, skins and scale.");
                }
                blocks.Add(block);
            }
            ParkProfileCollision existing;
            if (parkProfileCollisions.TryGetValue(group, out existing))
            {
                if (!ownedHalfpipe && existing.Root != null && existing.Members.SequenceEqual(blocks)) return;
                if (ownedHalfpipe)
                {
                    bool healthy;
                    try { healthy = ParkMegaCollisionHealthy(group, existing, parts, blocks); }
                    catch (Exception) { healthy = false; }
                    if (healthy) return;
                    if (!RemoveParkProfileCollision(group)) throw new ParkProfileRestorePendingException();
                    throw new InvalidDataException("Halfpipe profile collision changed; native floor colliders were restored.");
                }
                if (!RemoveParkProfileCollision(group)) throw new ParkProfileRestorePendingException();
            }
            int layer = LayerMask.NameToLayer("Construction");
            if (layer < 0) throw new InvalidDataException("Park profile collision layer is unavailable.");
            // Both halfpipes use the first flat block's local top as their riding datum.
            // The mesh adds its kind's lift; never infer the datum from a tilted chord.
            float datum = ownedHalfpipe ? blocks[0].bounds.max.y : floor.max.y;
            if (ownedHalfpipe && (float.IsNaN(datum) || float.IsInfinity(datum) || datum <= 0 || datum >= 0.5f))
                throw new InvalidDataException("Halfpipe flat-floor riding datum is unavailable.");
            var collision = new ParkProfileCollision { Identity = ownedHalfpipe ? ParkRestoreIdentity(group, parts) : null };
            parkProfileCollisions.Add(group, collision);
            try
            {
                collision.Members.AddRange(blocks);
                if (ownedHalfpipe)
                {
                    collision.Datum = datum;
                    for (int i = ParkProfileFloorStart(kind); i < ParkProfileFloorStart(kind) + ParkProfileFloorCount(kind); i++)
                    {
                        var patch = new ParkProfilePatch { Index = i };
                        collision.Patches.Add(patch);
                        patch.Root = new GameObject("ShortcutSkate " + group.Kind + " floor collision " + i.ToString(CultureInfo.InvariantCulture));
                        patch.Root.SetActive(false);
                        patch.Root.layer = layer;
                        patch.Root.transform.position = new Vector3((float)group.AnchorX, (float)group.AnchorY + datum, (float)group.AnchorZ);
                        patch.Root.transform.rotation = Quaternion.Euler(0, (float)group.AnchorYaw, 0);
                        // Keep the common profile frame while preserving the actual native
                        // floor as the parent used by hit, hammer, grade and entity queries.
                        patch.Root.transform.SetParent(blocks[i].transform, true);
                        patch.Mesh = mega ? ParkMegaMesh(i) : ParkHalfpipeMesh(i);
                        patch.Shell = patch.Root.AddComponent<MeshCollider>();
                        patch.Shell.sharedMesh = patch.Mesh;
                        patch.Shell.convex = false;
                        patch.Shape = ParkProfileMeshState.Capture(patch.Mesh);
                    }
                }
                else
                {
                    collision.Root = new GameObject("ShortcutSkate quarterpipe collision");
                    collision.Root.SetActive(false);
                    collision.Root.layer = layer;
                    collision.Root.transform.position = new Vector3((float)group.AnchorX, (float)group.AnchorY + datum, (float)group.AnchorZ);
                    collision.Root.transform.rotation = Quaternion.Euler(0, (float)group.AnchorYaw, 0);
                    collision.Root.transform.SetParent(blocks[0].transform, true);
                    collision.Mesh = ParkQuarterMesh(kind, profile);
                    var shell = collision.Root.AddComponent<MeshCollider>();
                    shell.sharedMesh = collision.Mesh;
                    shell.convex = false;
                    var coping = collision.Root.AddComponent<CapsuleCollider>();
                    coping.direction = 0;
                    coping.radius = (float)profile.CopingRadius;
                    coping.height = (float)(2 * (profile.HalfWidth + profile.CopingRadius));
                    coping.center = new Vector3(0, (float)profile.Height,
                        (float)(profile.StartForward + profile.Entry + profile.Radius));
                    if (kind == SkateParkKind.LargeQuarterPipe)
                    {
                        var apron = collision.Root.AddComponent<BoxCollider>();
                        apron.center = new Vector3(2.9f, (float)-profile.Skin / 2, 0);
                        apron.size = new Vector3(3, (float)profile.Skin, 2.8f);
                    }
                }
                // Replace only the designated native floors: MEGA0..63 or regular4..11.
                // Regular flat/decks and both kinds' native guard walls stay solid.
                foreach (BuildingBlock block in ownedHalfpipe ? blocks.Skip(ParkProfileFloorStart(kind)).Take(ParkProfileFloorCount(kind)) : blocks)
                {
                    int before = collision.Disabled.Count;
                    foreach (Collider collider in block.GetComponentsInChildren<Collider>(true))
                    {
                        if (collider == null || collider.isTrigger || !collider.enabled ||
                            (ownedHalfpipe && !collider.gameObject.activeInHierarchy) ||
                            ParkProfileOwnsCollider(collision, collider) || !ReferenceEquals(collider.ToBaseEntity(), block)) continue;
                        if (ownedHalfpipe) collision.OriginalShapes.Add(ParkProfileColliderState.Capture(collider, block));
                        collision.Disabled.Add(collider);
                    }
                    if (ownedHalfpipe && collision.Disabled.Count == before)
                        throw new InvalidDataException("Halfpipe collision requires physical colliders on every verified transition floor.");
                }
                // A new closed regular ramp can occupy space beneath the old plates.
                // Observe all active/sleeping bodies before the first collision mutation.
                if (kind == SkateParkKind.Halfpipe && !ParkProfileReplacementBodiesClear(collision))
                    throw new ParkProfileActivationOccupiedException();
                // Retain solid collision throughout the exchange; failure restores the originals
                // before removing the replacement. No native anti-hack result is overridden.
                if (ownedHalfpipe)
                {
                    foreach (ParkProfilePatch patch in collision.Patches) patch.Root.SetActive(true);
                    Physics.SyncTransforms();
                }
                foreach (Collider collider in collision.Disabled) collider.enabled = false;
                if (!ownedHalfpipe) collision.Root.SetActive(true);
                Physics.SyncTransforms();
                if (ownedHalfpipe && !ParkMegaCollisionHealthy(group, collision, parts, blocks))
                    throw new InvalidDataException("Halfpipe profile collision activation could not be verified.");
            }
            catch
            {
                if (!RemoveParkProfileCollision(group)) throw new ParkProfileRestorePendingException();
                throw;
            }
        }

        private struct ParkSpace
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Bounds Bounds;
        }

        private static Bounds ParkCellBounds(SkateParkKind kind, Bounds floor)
        {
            SkateParkQuarterProfile profile;
            SkateParkLayout.TryQuarterProfile(kind, out profile);
            float bottom = Math.Min(floor.min.y, floor.max.y - (float)profile.Skin);
            float top = floor.max.y + (float)(profile.Height + profile.RailHeight + profile.RailRadius);
            return new Bounds(new Vector3(0, (bottom + top) / 2, 0), new Vector3(3, top - bottom, 3));
        }

        private static List<ParkSpace> ParkSpaces(SkateParkKind kind, ParkGroup group, Bounds floor)
        {
            var spaces = new List<ParkSpace>();
            Quaternion rotation = Quaternion.Euler(0, (float)group.AnchorYaw, 0);
            for (int i = 0; i < SkateParkLayout.CellCount(kind); i++)
            {
                int right, forward;
                SkateParkLayout.TryCell(kind, i, out right, out forward);
                spaces.Add(new ParkSpace { Position = new Vector3((float)group.AnchorX, (float)group.AnchorY, (float)group.AnchorZ) +
                    rotation * new Vector3(right * 3, 0, forward * 3), Rotation = rotation, Bounds = ParkCellBounds(kind, floor) });
            }
            return spaces;
        }

        private static bool ParkSpacesOverlap(ParkSpace first, ParkSpace second, double contactTolerance = 0)
        {
            Vector3 a = first.Position + first.Rotation * first.Bounds.center;
            Vector3 b = second.Position + second.Rotation * second.Bounds.center;
            Vector3 ae = first.Bounds.extents, be = second.Bounds.extents;
            // Project pitched native bounds onto their yaw frame before comparison with a cell.
            double pitch = first.Rotation.eulerAngles.x * Math.PI / 180;
            double vertical = ae.y * Math.Abs(Math.Cos(pitch)) + ae.z * Math.Abs(Math.Sin(pitch));
            double forward = ae.z * Math.Abs(Math.Cos(pitch)) + ae.y * Math.Abs(Math.Sin(pitch));
            return Math.Abs(a.y - b.y) < vertical + be.y - 1e-5 && SkateParkLayout.OverlapsBoxBeyondContact(
                a.x, a.z, first.Rotation.eulerAngles.y, ae.x, forward,
                b.x, b.z, second.Rotation.eulerAngles.y, be.x, be.z, contactTolerance);
        }

        private static bool ParkSpacesShareEdge(ParkSpace first, ParkSpace second)
        {
            return SkateParkLayout.SharesNominalEdge(
                new SkateParkTransform { X = first.Position.x, Z = first.Position.z, Yaw = first.Rotation.eulerAngles.y },
                ParkBox(first.Bounds),
                new SkateParkTransform { X = second.Position.x, Z = second.Position.z, Yaw = second.Rotation.eulerAngles.y },
                ParkBox(second.Bounds));
        }

        private bool ParkModuleSpace(ParkPlacementRequest request, SkateParkKind kind, ParkNativePart[] definitions,
            ParkGroup group, ParkMember[] parts, List<BuildingBlock> created, out string failure)
        {
            failure = "The reserved park cells need dry, clear space and building permission through the complete ramp/deck/rail height.";
            Bounds floor = definitions[0].Construction.bounds;
            List<ParkSpace> spaces = ParkSpaces(kind, group, floor);
            bool module = spaces.Count != 0;
            int mask = LayerMask.GetMask("World", "Construction", "Deployed", "Player (Server)");
            foreach (ParkSpace space in spaces)
            {
                if (!ParkReservedBounds(space.Position, space.Rotation, space.Bounds, out failure, group) ||
                    !request.Player.CanBuild(space.Position, space.Rotation, space.Bounds, false) ||
                    !ParkFootprint(space.Position, space.Rotation, space.Bounds, request.Foundation, created, mask, out failure, true, request.Player, request))
                    return false;
            }
            // Other park layouts also respect saved module reservations, even where no native collider exists.
            if (kind == SkateParkKind.MegaHalfpipe)
                spaces.Add(new ParkSpace { Position = new Vector3((float)group.AnchorX, (float)group.AnchorY, (float)group.AnchorZ),
                    Rotation = Quaternion.Euler(0, (float)group.AnchorYaw, 0), Bounds = MegaBounds() });
            else if (!module)
                foreach (ParkMember part in parts)
                    spaces.Add(new ParkSpace { Position = ParkPosition(part), Rotation = ParkRotation(part),
                        Bounds = definitions[part.Index].Construction.bounds });
            foreach (ParkGroup existing in request.Data.Groups)
            {
                SkateParkKind existingKind;
                if (ReferenceEquals(existing, group) || !SkateParkLayout.TryKind(existing.Kind, out existingKind) ||
                    SkateParkLayout.CellCount(existingKind) == 0) continue;
                foreach (ParkSpace reserved in ParkSpaces(existingKind, existing, floor))
                foreach (ParkSpace space in spaces)
                    if (ParkSpacesOverlap(space, reserved))
                    { failure = "The park assembly overlaps a saved module's reserved cells."; return false; }
            }
            if (module && created != null)
                for (int i = 0; i < created.Count; i++)
                    if (!ParkModuleColliders(kind, i, created[i], group, floor, out failure))
                    { PrintWarning(failure); return false; }
            return true;
        }

        private static string ParkSafeDiagnostic(string message, int maximum = 2048)
        {
            string name = message ?? "unavailable";
            var safe = new StringBuilder();
            for (int i = 0; i < name.Length && safe.Length < maximum; i++)
            {
                if (char.IsDigit(name[i]))
                {
                    int end = i;
                    while (end < name.Length && char.IsDigit(name[end])) end++;
                    if (end - i >= 8) safe.Append("[redacted]");
                    else safe.Append(name, i, end - i);
                    i = end - 1;
                }
                else safe.Append(char.IsControl(name[i]) ? ' ' : name[i]);
            }
            return safe.ToString();
        }

        private static string ParkColliderDescription(Collider collider)
        {
            if (collider == null) return "collider=unavailable layer=unavailable";
            string safe = ParkSafeDiagnostic(collider.name ?? "unnamed", 96);
            int layer = collider.gameObject.layer;
            return "collider=" + safe + " layer=" + layer.ToString(CultureInfo.InvariantCulture) +
                "(" + LayerMask.LayerToName(layer) + ")";
        }

        private static string ParkBoundsDescription(Bounds bounds)
        {
            return string.Format(CultureInfo.InvariantCulture, "min=({0:R},{1:R},{2:R}) max=({3:R},{4:R},{5:R})",
                bounds.min.x, bounds.min.y, bounds.min.z, bounds.max.x, bounds.max.y, bounds.max.z);
        }

        private bool ParkModuleColliders(SkateParkKind kind, int index, BuildingBlock block, ParkGroup group,
            Bounds floor, out string failure)
        {
            failure = "Park kind=" + SkateParkLayout.Name(kind) + " part=" + index.ToString(CultureInfo.InvariantCulture) +
                ": native module collider data is unavailable.";
            SkateParkQuarterProfile profile;
            if (!SkateParkLayout.TryQuarterProfile(kind, out profile)) return false;
            Bounds envelope = ParkCellBounds(kind, floor);
            Vector3 origin = new Vector3((float)group.AnchorX, (float)group.AnchorY, (float)group.AnchorZ);
            Quaternion inverse = Quaternion.Inverse(Quaternion.Euler(0, (float)group.AnchorYaw, 0));
            double tolerance = SkateParkLayout.Float32BoundsTolerance(origin.x, origin.y, origin.z);
            Collider[] colliders = block.GetComponentsInChildren<Collider>(true);
            if (colliders == null || colliders.Length == 0) return false;
            int physical = 0;
            foreach (Collider collider in colliders)
            {
                if (collider == null) return false;
                if (!collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy) continue;
                physical++;
                BoxCollider box = collider as BoxCollider;
                MeshCollider mesh = collider as MeshCollider;
                SphereCollider sphere = collider as SphereCollider;
                CapsuleCollider capsule = collider as CapsuleCollider;
                Bounds bounds;
                Transform frame = collider.transform;
                if (box != null) bounds = new Bounds(box.center, box.size);
                else if (mesh != null && mesh.sharedMesh != null) bounds = mesh.sharedMesh.bounds;
                else if (sphere != null) bounds = new Bounds(sphere.center, Vector3.one * sphere.radius * 2);
                else if (capsule != null)
                {
                    Vector3 size = Vector3.one * capsule.radius * 2;
                    size[capsule.direction] = Math.Max(capsule.height, capsule.radius * 2);
                    bounds = new Bounds(capsule.center, size);
                }
                else { bounds = collider.bounds; frame = null; }
                bool measuredTwig = ParkMeasuredTwigCollider(block, collider);
                Bounds measured = new Bounds();
                bool first = true, contained = true;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 point = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                    point = inverse * ((frame == null ? point : frame.TransformPoint(point)) - origin);
                    if (first) { measured = new Bounds(point, Vector3.zero); first = false; }
                    else measured.Encapsulate(point);
                    if (!SkateParkLayout.ContainsQuarterColliderPoint(kind, point.x, point.y, point.z,
                        envelope.min.y, floor.max.y + profile.Height + profile.CopingRadius, tolerance, measuredTwig)) contained = false;
                }
                if (contained) continue;
                failure = "Park kind=" + SkateParkLayout.Name(kind) + " part=" + index.ToString(CultureInfo.InvariantCulture) +
                    ": a native module collider exceeds its assigned cell or riding envelope; " +
                    ParkColliderDescription(collider) + " local " + ParkBoundsDescription(bounds) +
                    " module " + ParkBoundsDescription(measured) + " tolerance=" + tolerance.ToString("R", CultureInfo.InvariantCulture) + ".";
                return false;
            }
            if (physical == 0)
            {
                ParkProfileCollision replacement;
                if (!parkProfileCollisions.TryGetValue(group, out replacement) || replacement.Root == null ||
                    !replacement.Root.activeInHierarchy || !replacement.Members.Any(member => ReferenceEquals(member, block))) return false;
            }
            failure = null;
            return true;
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
            Quaternion rotation, BuildingBlock anchor, List<BuildingBlock> transactionParts = null, ParkPlacementRequest request = null)
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
                if (ParkSupportBlock(request, neighbour) || ParkAdjacentContact(request, position, rotation,
                    construction.bounds, neighbour)) continue;
                if (a.hit || b.hit)
                {
                    var line = a.sqrDist <= b.sqrDist ? a.line : b.line;
                    Vector3 delta = line.point1 - line.point0;
                    if (Math.Abs(delta.y) <= 1.49f && Math.Sqrt(delta.x * delta.x + delta.z * delta.z) <= 1.49) return false;
                }
            }
            return true;
        }

        private delegate bool ParkDeployFlagsCheck(List<Collider> candidates, DeployVolume volume,
            List<Type> types, DeployVolume.TypeFilterMode filterMode, BaseEntity ignoredEntity, bool ignoreChildren);

        private static readonly ParkDeployFlagsCheck ParkDeployFlags = FindParkDeployFlags();

        private static ParkDeployFlagsCheck FindParkDeployFlags()
        {
            try
            {
                MethodInfo method = typeof(DeployVolume).GetMethod("CheckFlags", BindingFlags.Static | BindingFlags.NonPublic,
                    null, new[] { typeof(List<Collider>), typeof(DeployVolume), typeof(List<Type>),
                        typeof(DeployVolume.TypeFilterMode), typeof(BaseEntity), typeof(bool) }, null);
                if (method == null || method.ReturnType != typeof(bool)) return null;
                return Delegate.CreateDelegate(typeof(ParkDeployFlagsCheck), method, false) as ParkDeployFlagsCheck;
            }
            catch { return null; }
        }

        private static bool CollectParkDeployCandidates(Vector3 position, Quaternion rotation, DeployVolume volume,
            int mask, List<Collider> candidates)
        {
            int layers = (int)volume.layers & mask;
            DeployVolumeOBB box = volume.GetType() == typeof(DeployVolumeOBB) ? (DeployVolumeOBB)volume : null;
            DeployVolumeCapsule capsule = volume.GetType() == typeof(DeployVolumeCapsule) ? (DeployVolumeCapsule)volume : null;
            DeployVolumeSphere sphere = volume.GetType() == typeof(DeployVolumeSphere) ? (DeployVolumeSphere)volume : null;
            DeployVolumeEntityBounds entityBounds = volume.GetType() == typeof(DeployVolumeEntityBounds) ? (DeployVolumeEntityBounds)volume : null;
            if (box != null)
            {
                Vector3 center = position + rotation * (box.worldRotation * box.bounds.center + box.worldPosition);
                var obb = new OBB(center, box.bounds.size, rotation * box.worldRotation);
                GamePhysics.OverlapOBB(obb, candidates, layers, QueryTriggerInteraction.Collide);
            }
            else if (capsule != null)
            {
                Vector3 center = position + rotation * (capsule.worldRotation * capsule.center + capsule.worldPosition);
                Vector3 halfAxis = rotation * capsule.worldRotation * Vector3.up * capsule.height * 0.5f;
                GamePhysics.OverlapCapsule(center + halfAxis, center - halfAxis, capsule.radius,
                    candidates, layers, QueryTriggerInteraction.Collide);
            }
            else if (sphere != null)
            {
                Vector3 center = position + rotation * (sphere.worldRotation * sphere.center + sphere.worldPosition);
                GamePhysics.OverlapSphere(center, sphere.radius, candidates, layers, QueryTriggerInteraction.Collide);
            }
            else if (entityBounds != null)
            {
                var obb = new OBB(position + rotation * entityBounds.bounds.center, entityBounds.bounds.size, rotation);
                GamePhysics.OverlapOBB(obb, candidates, layers, QueryTriggerInteraction.Collide);
            }
            else return false;
            return true;
        }

        private static bool ParkReverseDeployVolumeBlocked(ParkPlacementRequest request, Vector3 position, Quaternion rotation, Bounds bounds,
            DeployVolumeEntityBoundsReverse reverse, BasePlayer player, BuildingBlock anchor,
            List<BuildingBlock> transactionParts, int mask, out Collider blocker, out string unavailable)
        {
            blocker = null;
            unavailable = null;
            Vector3 partPosition = position;
            position += rotation * reverse.bounds.center;
            var query = new OBB(position, reverse.bounds.size, rotation);
            List<BaseEntity> nearby = null;
            List<DeployVolume> applicable = null;
            try
            {
                nearby = Facepunch.Pool.Get<List<BaseEntity>>();
                applicable = Facepunch.Pool.Get<List<DeployVolume>>();
                Vis.Entities(position, query.extents.magnitude, nearby, (int)reverse.layers & mask,
                    QueryTriggerInteraction.Collide);
                foreach (BaseEntity entity in nearby)
                {
                    if (entity == null)
                    { unavailable = "reverse native deploy-volume returned an unavailable entity"; return true; }
                    if (!SkateParkLayout.BlocksMegaSpace(entity, player, anchor,
                        transactionParts != null && transactionParts.Any(candidatePart => ReferenceEquals(candidatePart, entity)))) continue;
                    BuildingBlock support = entity as BuildingBlock;
                    if (ParkSupportBlock(request, support) || ParkAdjacentContact(request, partPosition, rotation, bounds, support)) continue;
                    if (PrefabAttribute.server == null)
                    { unavailable = "reverse native deploy-volume metadata API is unavailable"; return true; }
                    DeployVolume[] entityVolumes = PrefabAttribute.server.FindAll<DeployVolume>(entity.prefabID);
                    if (entityVolumes == null)
                    { unavailable = "reverse native deploy-volume entity metadata is unavailable"; return true; }
                    applicable.Clear();
                    foreach (DeployVolume other in entityVolumes)
                    {
                        if (other == null)
                        { unavailable = "reverse native deploy-volume entity metadata contains an unavailable volume"; return true; }
                        if (DeployVolume.ShouldApplyVolumeForEntity(other, entity)) applicable.Add(other);
                    }
                    if (!DeployVolume.Check(entity.transform.position, entity.transform.rotation, applicable,
                        query, 1 << (reverse.layer & 31))) continue;
                    Collider[] representatives = entity.GetComponentsInChildren<Collider>(true);
                    if (representatives != null)
                        foreach (Collider representative in representatives)
                            if (representative != null && representative.enabled &&
                                representative.gameObject.activeInHierarchy && !representative.isTrigger)
                            { blocker = representative; break; }
                    unavailable = "reverse native volume-to-volume obstruction; collider is representative only";
                    return true;
                }
                return false;
            }
            finally
            {
                if (applicable != null) Facepunch.Pool.FreeUnmanaged(ref applicable);
                if (nearby != null) Facepunch.Pool.FreeUnmanaged(ref nearby);
            }
        }

        private static bool ParkDeployVolumeBlocked(ParkPlacementRequest request, Vector3 position, Quaternion rotation,
            Bounds bounds, DeployVolume[] volumes,
            BasePlayer player, BuildingBlock anchor, List<BuildingBlock> transactionParts, int mask,
            out Collider blocker, out string unavailable)
        {
            blocker = null;
            unavailable = null;
            if (volumes == null || ParkDeployFlags == null)
            { unavailable = "native deploy-volume query API is unavailable"; return true; }
            List<Collider> candidates = null;
            try
            {
                candidates = Facepunch.Pool.Get<List<Collider>>();
                foreach (DeployVolume volume in volumes)
                {
                    if (volume == null)
                    { unavailable = "native deploy-volume metadata is unavailable"; return true; }
                    DeployVolumeEntityBoundsReverse reverse = volume as DeployVolumeEntityBoundsReverse;
                    if (reverse != null)
                    {
                        if (ParkReverseDeployVolumeBlocked(request, position, rotation, bounds, reverse, player, anchor,
                            transactionParts, mask, out blocker, out unavailable)) return true;
                        continue;
                    }
                    candidates.Clear();
                    if (!CollectParkDeployCandidates(position, rotation, volume, mask, candidates))
                    {
                        DeployVolume.LastDeployHit = null;
                        if (!DeployVolume.Check(position, rotation, new[] { volume }, null,
                            DeployVolume.TypeFilterMode.Ignore, anchor, mask, false)) continue;
                        blocker = DeployVolume.LastDeployHit;
                        unavailable = "native special deploy-volume rejected: " + volume.GetType().Name;
                        return true;
                    }
                    for (int i = candidates.Count - 1; i >= 0; i--)
                    {
                        Collider candidate = candidates[i];
                        if (candidate == null)
                        { unavailable = "native deploy-volume returned an unavailable collider"; return true; }
                        BaseEntity entity = candidate.ToBaseEntity();
                        if (!SkateParkLayout.BlocksMegaSpace(entity, player, anchor,
                            transactionParts != null && transactionParts.Any(candidatePart => ReferenceEquals(candidatePart, entity))) ||
                            ParkAdjacentContact(request, position, rotation, bounds, entity as BuildingBlock))
                            candidates.RemoveAt(i);
                    }
                    DeployVolume.LastDeployHit = null;
                    if (ParkDeployFlags(candidates, volume, null, DeployVolume.TypeFilterMode.Ignore, null, false))
                    {
                        blocker = DeployVolume.LastDeployHit;
                        if (blocker == null) unavailable = "native deploy-volume rejected without a collider";
                        return true;
                    }
                }
                return false;
            }
            catch (Exception error)
            {
                unavailable = "native deploy-volume query failed: " + error.GetType().Name;
                blocker = null;
                return true;
            }
            finally
            {
                if (candidates != null) Facepunch.Pool.FreeUnmanaged(ref candidates);
            }
        }

        private bool ParkPreflight(ParkPlacementRequest request, SkateParkKind kind, BuildingBlock anchor,
            ParkNativePart[] definitions, ParkMember[] parts, out string failure)
        {
            BasePlayer player = request.Player;
            failure = "The complete park assembly needs dry, clear space and building permission.";
            int mask = LayerMask.GetMask("World", "Construction", "Deployed", "Player (Server)");
            if (mask == 0 || TerrainMeta.HeightMap == null) return false;
            if (!ParkMegaSpace(kind, parts, player, anchor, null, out failure, null, request)) return false;
            foreach (ParkMember member in parts)
            {
                Construction construction = definitions[member.Index].Construction;
                if (!ParkReservedSpace(member, construction.bounds, out failure)) return false;
                DeployVolume[] volumes = definitions[member.Index].Volumes;
                Vector3 position = ParkPosition(member);
                Quaternion rotation = ParkRotation(member);
                Bounds bounds = construction.bounds;
                if (!player.CanBuild(position, rotation, bounds, false))
                { failure = "You do not have building permission across the complete park assembly."; return false; }
                var target = new Construction.Target { valid = true, player = player, position = position,
                    rotation = rotation.eulerAngles, normal = Vector3.up, onTerrain = true };
                if (!ParkBuildingHook(request, construction, target))
                { failure = "A building hook rejected part of the park assembly."; return false; }
                if (!ParkProximity(player, construction, position, rotation, anchor, null, request))
                { failure = "The park assembly conflicts with a neighbouring building or its permission."; return false; }
                Collider blocker;
                string unavailable;
                if (ParkSupportedDeployVolume(request, position, rotation, bounds, volumes, anchor, null, mask, out blocker, out unavailable))
                {
                    failure = ParkDeployFailure(kind, member.Index, blocker, unavailable);
                    PrintWarning(failure);
                    return false;
                }
                if (!ParkFootprint(position, rotation, bounds, anchor, null, mask, out failure, false, player, request)) return false;
            }
            return true;
        }

        private static string ParkDeployFailure(SkateParkKind kind, int index, Collider blocker, string unavailable)
        {
            return "Park kind=" + SkateParkLayout.Name(kind) + " part=" + index.ToString(CultureInfo.InvariantCulture) +
                ": a native deploy volume is occupied across the park assembly; " + ParkColliderDescription(blocker) +
                (unavailable == null ? "." : "; " + unavailable + ".");
        }

        private bool ParkFinalDeployVolumes(ParkPlacementRequest request, SkateParkKind kind, ParkMember[] parts, ParkNativePart[] definitions,
            BuildingBlock anchor, List<BuildingBlock> created, out string failure)
        {
            failure = null;
            int mask = LayerMask.GetMask("World", "Construction", "Deployed", "Player (Server)");
            foreach (ParkMember part in parts)
            {
                Collider blocker;
                string unavailable;
                if (!ParkSupportedDeployVolume(request, ParkPosition(part), ParkRotation(part), definitions[part.Index].Construction.bounds,
                    definitions[part.Index].Volumes, anchor, created, mask, out blocker, out unavailable)) continue;
                failure = ParkDeployFailure(kind, part.Index, blocker, unavailable);
                PrintWarning(failure);
                return false;
            }
            return true;
        }

        private static Bounds MegaBounds()
        {
            double top = SkateMegaHalfpipe.Radius + SkateMegaHalfpipe.RideLift + SkateMegaHalfpipe.AirClearance;
            return new Bounds(new Vector3((float)SkateMegaHalfpipe.CenterRight, (float)(top / 2), 0),
                new Vector3((float)(SkateMegaHalfpipe.Width + 2 * SkateMegaHalfpipe.EnvelopeMargin), (float)top,
                    (float)(SkateMegaHalfpipe.Length + 2 * SkateMegaHalfpipe.EnvelopeMargin)));
        }

        private bool ParkReservedSpace(ParkMember member, Bounds nativeBounds, out string failure)
        {
            return ParkReservedBounds(ParkPosition(member), ParkRotation(member), nativeBounds, out failure);
        }

        private bool ParkReservedBounds(Vector3 position, Quaternion rotation, Bounds nativeBounds, out string failure, ParkGroup current = null)
        {
            failure = "The park assembly overlaps a reserved MEGA riding envelope.";
            foreach (ParkGroup existing in parkData.Groups)
            {
                if (ReferenceEquals(existing, current) || existing.Kind != "mega-halfpipe") continue;
                Vector3 origin = new Vector3((float)existing.AnchorX, (float)existing.AnchorY, (float)existing.AnchorZ);
                Quaternion inverse = Quaternion.Inverse(Quaternion.Euler(0, (float)existing.AnchorYaw, 0));
                Bounds candidate = new Bounds(inverse * (position + rotation * nativeBounds.center - origin), Vector3.zero);
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                        for (int z = -1; z <= 1; z += 2)
                            candidate.Encapsulate(inverse * (position + rotation * (nativeBounds.center +
                                Vector3.Scale(nativeBounds.extents, new Vector3(x, y, z))) - origin));
                if (MegaBounds().Intersects(candidate)) return false;
            }
            return true;
        }

        private bool ParkMegaSpace(SkateParkKind kind, ParkMember[] parts, BasePlayer player,
            BuildingBlock anchor, List<BuildingBlock> created, out string failure, ParkGroup current = null,
            ParkPlacementRequest request = null)
        {
            failure = null;
            if (kind != SkateParkKind.MegaHalfpipe) return true;
            failure = "The complete MEGA riding envelope needs clear space and building permission.";
            double x, y, z, yaw;
            ParkMember first = parts[0];
            if (!SkateParkLayout.TryAnchor(SkateParkKind.MegaHalfpipe, 0, first.X, first.Y, first.Z, first.Yaw,
                out x, out y, out z, out yaw)) return false;
            Vector3 origin = new Vector3((float)x, (float)y, (float)z);
            Quaternion rotation = Quaternion.Euler(0, (float)yaw, 0);
            Bounds bounds = MegaBounds();
            int mask = LayerMask.GetMask("World", "Construction", "Deployed", "Player (Server)");
            if (mask == 0 || !player.CanBuild(origin, rotation, bounds, false) ||
                !ParkReservedBounds(origin, rotation, bounds, out failure, current)) return false;
            failure = "The complete MEGA riding envelope is occupied.";
            foreach (Collider collider in Physics.OverlapBox(origin + rotation * bounds.center, bounds.extents,
                rotation, mask, QueryTriggerInteraction.Ignore))
            {
                if (collider == null) continue;
                BaseEntity entity = collider.ToBaseEntity();
                if (!ParkSupportCollider(request, collider) && SkateParkLayout.BlocksMegaSpace(entity, player, anchor,
                    created != null && created.Any(candidatePart => ReferenceEquals(candidatePart, entity))))
                {
                    failure += " kind=mega-halfpipe part=assembly " + ParkColliderDescription(collider) + ".";
                    PrintWarning(failure);
                    return false;
                }
            }
            failure = null;
            return true;
        }

        private static bool ParkFootprint(Vector3 position, Quaternion rotation, Bounds bounds, BuildingBlock anchor,
            List<BuildingBlock> transactionParts, int mask, out string failure, bool reservedCell = false, BasePlayer placingPlayer = null, ParkPlacementRequest request = null)
        {
            failure = "The complete park assembly needs dry, clear space.";
            Vector3 extents = bounds.extents;
            float inset = reservedCell ? 0.00001f : 0.015f;
            extents.y = Math.Max(0.001f, extents.y - 0.002f);
            extents.x = Math.Max(0.01f, extents.x - inset);
            extents.z = Math.Max(0.01f, extents.z - inset);
            foreach (Collider collider in Physics.OverlapBox(position + rotation * bounds.center, extents, rotation,
                mask, QueryTriggerInteraction.Ignore))
            {
                if (collider == null) continue;
                BaseEntity entity = collider.ToBaseEntity();
                if (!SkateParkLayout.BlocksMegaSpace(entity, placingPlayer, anchor,
                    transactionParts != null && transactionParts.Any(candidatePart => ReferenceEquals(candidatePart, entity)))) continue;
                BuildingBlock support = entity as BuildingBlock;
                if ((ParkSupportBlock(request, support) && collider.bounds.max.y <= request.SupportTop + SkateParkLayout.SupportTolerance) ||
                    ParkAdjacentContact(request, position, rotation, bounds, support)) continue;
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
            if (selection.Kind == SkateParkKind.HalfWallKicker)
            { player.ConsoleMessage("Use the Half-wall Kicker build item and aim at an existing half wall."); return; }
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
            double x, z;
            if (!SkateParkLayout.TrySupportGridAnchor(selection.Kind, snapshot.Position.x, snapshot.Position.z,
                snapshot.Rotation.eulerAngles.y, out x, out z))
            { ReportParkFailures(player, new[] { "The foundation grid anchor is outside world bounds." }); return; }
            var group = new ParkGroup { Id = Guid.NewGuid().ToString("N"), Owner = player.userID,
                Kind = SkateParkLayout.Name(selection.Kind), AnchorX = x, AnchorY = baseline,
                AnchorZ = z, AnchorYaw = snapshot.Rotation.eulerAngles.y, Members = new List<ParkMember>() };
            var request = new ParkPlacementRequest { Player = player, Planner = planner, Data = parkData,
                Foundation = anchor, FoundationId = anchorId, Snapshot = snapshot, Selection = selection,
                Building = snapshot.Building, Owner = player.userID };
            if (!BuildParkAssembly(request, selection.Kind, definitions, group, out failure))
            { ReportParkFailures(player, failure.Split('\n')); return; }
            ParkPlacementFeedback(player, group, " The original foundation remains a separate normal building piece.");
        }

        private bool BuildParkAssembly(ParkPlacementRequest request, SkateParkKind kind,
            ParkNativePart[] definitions, ParkGroup group, out string failure)
        {
            failure = "Park placement is busy, unavailable or the world is being saved.";
            ParkNativeRequest native = PendingNativeParkRequest(request == null ? null : request.Player,
                request == null ? null : request.Planner);
            if (parkBusy || parkRefreshing || parkNativeCleaning || !CurrentParkRequest(request) ||
                (parkNativeRequests.Count != 0 && (native == null || !ReferenceEquals(native.Placement, request) ||
                    !native.Removed || native.Failure != null)))
            { failure = request == null ? failure : request.SupportFailure ?? failure; return false; }
            CleanupNativeParkCarriers();
            if (parkNativeCleanup.Count != 0)
            { failure = "Previous native carrier cleanup is incomplete; new park placement is disabled until removal is verified."; return false; }
            // Kill hooks may reenter placement while a previous rollback is being retried.
            if (parkRollback.Count != 0)
            {
                parkBusy = true;
                try { CleanupParkRollback(); }
                finally { parkBusy = false; }
            }
            if (parkRollback.Count != 0)
            { failure = "Previous unsaved park cleanup is incomplete; new placement is disabled until those parts are removed."; return false; }
            if (!CurrentParkPhase(request)) { failure = request.SupportFailure ?? failure; return false; }
            if (request.Data.Groups.Count >= MaximumParkGroups)
            { failure = "Park group limit reached: 256."; return false; }
            BasePlayer player = request.Player;
            Planner planner = request.Planner;
            BuildingBlock anchor = request.WallBlock ?? request.Foundation ?? request.Support;
            if (kind != SkateParkKind.HalfWallKicker && request.Supports == null &&
                !ResolveParkSupport(request, kind, definitions, group, out failure)) return false;
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
            var rollbackReceipts = new List<ParkRollbackReceipt>();
            bool recorded = false, committed = false;
            parkBusy = true;
            try
            {
                if (kind == SkateParkKind.HalfWallKicker && (request.Wall == null ||
                    !ReferenceEquals(group.Wall, request.Wall) || WallSocketOccupied(request.Wall, group)))
                    throw new InvalidDataException("The half-wall attachment is missing or its side is occupied.");
                if (!ParkPreflight(request, kind, anchor, definitions, parts, out failure) ||
                    !ParkModuleSpace(request, kind, definitions, group, parts, null, out failure))
                    throw new InvalidDataException(failure);
                // Full support scans belong at phase boundaries, outside per-part callback guards.
                if (!CurrentParkPhase(request))
                    throw new InvalidDataException("The administrator, building plan, save/world or placement request changed during preflight.");
                for (int i = 0; i < parts.Length; i++)
                {
                    if (!CurrentParkRequest(request))
                        throw new InvalidDataException("The administrator, building plan, save/world or placement request changed during creation.");
                    BaseEntity entity = GameManager.server.CreateEntity(definitions[i].Prefab,
                        ParkPosition(parts[i]), ParkRotation(parts[i]), true);
                    if (entity == null) throw new InvalidDataException("A park part could not be created: " + definitions[i].Prefab + ".");
                    var receipt = new ParkRollbackReceipt(entity, request.Data);
                    parkRollback.Add(receipt);
                    rollbackReceipts.Add(receipt);
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
                    if (!receipt.Capture() || !ParkRollbackMatches(receipt))
                        throw new InvalidDataException("A new park part has an unverified rollback lifetime.");
                    block.skinID = 0;
                    block.SetGrade(definitions[i].Grade);
                    block.SetHealthToMax();
                    if (request.Selection == null && request.Wall == null && i == 0)
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
                if (!CurrentParkPhase(request))
                    throw new InvalidDataException(request.SupportFailure ?? "The placement support changed during creation.");
                foreach (ParkMember part in parts)
                {
                    if (!CurrentParkRequest(request)) throw new InvalidDataException("The placement request changed before final building-hook checks.");
                    Construction construction = definitions[part.Index].Construction;
                    var target = new Construction.Target { valid = true, player = player, onTerrain = true,
                        position = ParkPosition(part), rotation = ParkRotation(part).eulerAngles,
                        normal = Vector3.up };
                    if (!ParkBuildingHook(request, construction, target))
                        throw new InvalidDataException("A building hook rejected final park registration.");
                }
                if (!CurrentParkPhase(request))
                    throw new InvalidDataException(request.SupportFailure ?? "The placement support changed during building-hook checks.");
                foreach (ParkMember part in parts)
                {
                    BuildingBlock block = created[part.Index];
                    ParkNativePart definition = definitions[part.Index];
                    Construction construction = definition.Construction;
                    if (!CurrentParkRequest(request) || !ParkMatches(block, group, part) ||
                        block.grade != definition.Grade || block.skinID != 0 || !ReferenceEquals(block.blockDefinition, construction) ||
                        !block.grounded || block.enableSaving || block.buildingID != request.Building ||
                        !player.CanBuild(ParkPosition(part), ParkRotation(part), construction.bounds, false) ||
                        !ParkProximity(player, construction, ParkPosition(part), ParkRotation(part), anchor, created, request) ||
                        !ParkFootprint(ParkPosition(part), ParkRotation(part), construction.bounds, anchor, created,
                            LayerMask.GetMask("World", "Construction", "Deployed", "Player (Server)"), out failure, false, player, request))
                        throw new InvalidDataException("A placement request or park member changed during final permission verification.");
                }
                if (!ParkFinalDeployVolumes(request, kind, parts, definitions, anchor, created, out failure) ||
                    !ParkMegaSpace(kind, parts, player, anchor, created, out failure, null, request) ||
                    !ParkModuleSpace(request, kind, definitions, group, parts, created, out failure))
                    throw new InvalidDataException(failure);
                EnsureParkProfileCollision(kind, group, parts, definitions[0].Construction.bounds);
                if (!CurrentParkPhase(request))
                    throw new InvalidDataException(request.SupportFailure ?? "The placement support changed during final permission verification.");
                group.Members.AddRange(parts);
                request.Data.Groups.Add(group);
                recorded = true;
                // Persist metadata before registering the spawned parts in the native save list.
                SaveParkData();
                foreach (ParkMember part in parts)
                {
                    BuildingBlock block = created[part.Index];
                    if (!ParkRollbackMatches(rollbackReceipts[part.Index]))
                        throw new InvalidDataException("A park part's validated lifetime changed before save registration.");
                    block.EnableSaving(true);
                    if (!CurrentParkRequest(request) || !ParkMatches(block, group, part) ||
                        block.grade != definitions[part.Index].Grade || block.skinID != 0 ||
                        !ReferenceEquals(block.blockDefinition, definitions[part.Index].Construction) ||
                        !block.enableSaving || !block.grounded || block.buildingID != request.Building)
                        throw new InvalidDataException("A placement request or park member changed during save registration.");
                }
                if (!CurrentParkPhase(request))
                    throw new InvalidDataException(request.SupportFailure ?? "The placement support changed during save registration.");
                if (request.BuildItem)
                {
                    if (!ConsumeParkItem(request))
                        throw new InvalidDataException("Build-item payment was refused or its exact one-item debit could not be verified.");
                    if (!CurrentParkPhase(request))
                        throw new InvalidDataException(request.SupportFailure ?? "The administrator, item receipt or save/world changed during build-item payment.");
                    // Preparing is deliberately not recoverable if interrupted before a verified native debit.
                    group.BuildItemState = SkateParkBuildItemState.Paid;
                    SaveParkData();
                    foreach (ParkMember part in parts)
                    {
                        BuildingBlock block = created[part.Index];
                        if (!CurrentParkRequest(request) || !ParkMatches(block, group, part) ||
                            block.grade != definitions[part.Index].Grade || block.skinID != 0 || !block.enableSaving ||
                            !block.grounded || block.buildingID != request.Building ||
                            !ReferenceEquals(block.blockDefinition, definitions[part.Index].Construction))
                            throw new InvalidDataException("A paid park member changed before completion.");
                    }
                }
                SkateParkSelectionState consumed = new SkateParkSelectionState();
                if (!CurrentParkPhase(request) || rollbackReceipts.Any(receipt => !ParkRollbackMatches(receipt)) || (request.Selection != null &&
                    !SkateParkSelections.TryApply(request.Selection.State, SkateParkSelectionAction.PlacementSucceeded,
                        player.IsAdmin, parkReady, clock.Elapsed.TotalSeconds, null, out consumed)))
                    throw new InvalidDataException("The placement request expired or changed before completion.");
                if (!ParkFinalDeployVolumes(request, kind, parts, definitions, anchor, created, out failure) ||
                    !ParkMegaSpace(kind, parts, player, anchor, created, out failure, group, request) ||
                    !ParkModuleSpace(request, kind, definitions, group, parts, created, out failure) || !CurrentParkPhase(request))
                    throw new InvalidDataException(failure ?? "The placement request changed during final park-space verification.");
                foreach (ParkMember part in parts) parkMembers.Add(part.Id, group);
                foreach (ParkRollbackReceipt receipt in rollbackReceipts) parkRollback.Remove(receipt);
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
                // Do not delete held entities, erase their metadata or mint a refund
                // while a restoration owner is protecting overlapping player bodies.
                if (!RemoveParkProfileCollision(group))
                {
                    parkReady = false;
                    if (request.ItemSpent) request.RefundClaimed = true;
                    failure = error.Message + " Collision restoration is pending; park parts remain tracked and no replacement item was issued.";
                    return false;
                }
                if (recorded)
                {
                    request.Data.Groups.Remove(group);
                    foreach (ParkMember part in parts) parkMembers.Remove(part.Id);
                }
                CleanupParkRollback();
                bool rollbackSaved = !recorded;
                if (recorded)
                {
                    try { SaveParkData(); rollbackSaved = true; }
                    catch (Exception storage) { PrintError("Park rollback metadata could not be verified. " + storage.Message); }
                }
                string recovery = "";
                if (request.BuildItem && request.ItemSpent && !request.RefundClaimed)
                {
                    request.RefundClaimed = true;
                    if (parkRollback.Count == 0 && rollbackSaved && ReferenceEquals(instance, this) &&
                        !parkClosing && SameParkWorld(request.Data) && ReferenceEquals(request.Data, parkData))
                    {
                        string deliveryFailure;
                        if (!GiveParkItem(player, request.ItemKind, out deliveryFailure, request.Data))
                            recovery = " Verified debit recovery is unverified: " + deliveryFailure;
                        else recovery = " The verified spent build item was returned once.";
                    }
                    else recovery = " Verified debit recovery is withheld until part cleanup, metadata and world identity are safe; no replacement item was minted.";
                }
                failure = (request.SupportFailure == null ? error.Message : request.SupportFailure) + recovery + (parkRollback.Count == 0 ? "" :
                    " Part cleanup is incomplete; new placement is disabled until cleanup succeeds.");
                return false;
            }
            finally
            {
                parkBusy = false;
                if (!committed && parkClosing) CleanupParkRollback();
            }
        }

        private bool ConsumeParkItem(ParkPlacementRequest request)
        {
            if (request == null || !request.BuildItem || request.ItemSpent || !CurrentParkRequest(request)) return false;
            Item item = request.PlannerItem;
            int before = item.amount;
            try { item.UseItem(1); }
            finally
            {
                // Do not refund an exception by assumption: callbacks can remove, pool or replace this object.
                request.ItemSpent = SkateParkBuildItems.VerifiedDebit(before, item.amount, SameParkPaymentItem(request));
            }
            return request.ItemSpent;
        }

        private static bool SameParkPaymentItem(ParkPlacementRequest request)
        {
            Item item = request == null ? null : request.PlannerItem;
            return item != null && item.isServer && item.uid.Equals(request.ItemUid) &&
                ReferenceEquals(item.info, request.ItemDefinition) && item.info != null && item.info.shortname == SkateParkBuildItems.Carrier &&
                ReferenceEquals(item.instanceData, request.ItemData) && item.instanceData != null &&
                item.instanceData.dataInt == SkateParkBuildItems.Marker(request.ItemKind) && item.skin == 0 &&
                item.name == SkateParkBuildItems.Name(request.ItemKind);
        }

        private bool ParkRollbackWorld(ParkRollbackReceipt receipt)
        {
            return receipt != null && ReferenceEquals(receipt.Data, parkData) && SameParkWorld(receipt.World) &&
                SameParkGeneration(receipt.Data, receipt.World) && receipt.Data.Checksum == receipt.World.Checksum;
        }

        private bool ParkRollbackMatches(ParkRollbackReceipt receipt)
        {
            if (!ParkRollbackWorld(receipt) || receipt.Lifetime == null) return false;
            BaseEntity entity = receipt.Entity;
            ParkRollbackLifetime lifetime = receipt.Lifetime;
            return entity != null && !entity.IsDestroyed && lifetime.Id != 0 &&
                ReferenceEquals(entity.net, lifetime.Network) && ParkId(entity) == lifetime.Id &&
                entity.creationFrame == lifetime.Frame && entity.prefabID == lifetime.Prefab && entity.OwnerID == lifetime.Owner &&
                ReferenceEquals(BaseNetworkable.serverEntities.Find(new NetworkableId(lifetime.Id)), entity);
        }

        private bool ParkRollbackRemoved(ParkRollbackReceipt receipt)
        {
            if (!ParkRollbackWorld(receipt) || receipt.Lifetime == null || receipt.Lifetime.Id == 0) return false;
            BaseEntity entity = receipt.Entity;
            // Absence is a receipt for the original ID, never permission to destroy its replacement.
            return ReferenceEquals(BaseNetworkable.serverEntities.Find(new NetworkableId(receipt.Lifetime.Id)), null) &&
                (entity == null || entity.IsDestroyed) && (ParkId(entity) == 0 || ParkId(entity) == receipt.Lifetime.Id) &&
                BaseEntity.saveList != null && !BaseEntity.saveList.Contains(entity);
        }

        private bool ParkRollbackOwnsGroup(ParkGroup group)
        {
            return group != null && parkRollback.Any(receipt => receipt.Lifetime != null &&
                group.Members.Any(member => member.Id == receipt.Lifetime.Id));
        }

        private void CleanupParkRollback()
        {
            // Kill hooks can unload the plugin and reenter this queue before destruction completes.
            if (parkRollbackCleaning) return;
            parkRollbackCleaning = true;
            try
            {
                for (int i = parkRollback.Count - 1; i >= 0; i--)
                {
                    ParkRollbackReceipt receipt = parkRollback[i];
                    try
                    {
                        if (ParkProfileRestoreHeld(receipt.Entity)) continue;
                        if (!ParkRollbackRemoved(receipt))
                        {
                            // Unknown/partially spawned objects are retained; do not guess a lifetime.
                            if (!ParkRollbackMatches(receipt) || BaseEntity.saveList == null)
                            { PrintError("Unsaved park rollback identity is unverified; the part remains tracked and untouched."); continue; }
                            BaseEntity entity = receipt.Entity;
                            entity.EnableSaving(false);
                            if (!ParkRollbackMatches(receipt) || entity.enableSaving ||
                                BaseEntity.saveList == null || BaseEntity.saveList.Contains(entity))
                            { PrintError("Unsaved park rollback saving removal is unverified; the part remains tracked."); continue; }
                            entity.Kill();
                        }
                        if (!ParkRollbackRemoved(receipt))
                        { PrintError("Unsaved park rollback part removal was refused; it remains tracked."); continue; }
                        parkRollback.RemoveAt(i);
                    }
                    catch (Exception error) { PrintError("Unsaved park rollback cleanup failed. " + error.Message); }
                }
            }
            finally { parkRollbackCleaning = false; }
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
            if (parkData != null && SameParkGeneration(parkData) && parkData.Groups.Any(g =>
                g.Wall != null && g.Wall.Id == ParkId(block) && WallMatches(block, g.Wall)))
            {
                if (player != null) player.ConsoleMessage("Remove attached half-wall kickers before rotating this wall.");
                return false;
            }
            if (VerifiedParkGroup(block) == null) return null;
            if (player != null) player.ConsoleMessage("Park assembly rotation is fixed. Demolish the group and place a new selected piece to turn it.");
            return false;
        }

        private object OnStructureDemolish(DecayEntity entity, BasePlayer player, bool immediate)
        {
            if (ParkProfileRestoreHeld(entity))
            {
                if (player != null) player.ConsoleMessage("Park collision restoration is pending. Leave the park and retry demolition once it is clear.");
                return false;
            }
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
            ParkData data = parkData;
            SkateParkKind itemKind;
            bool recover = SkateParkLayout.TryKind(group.Kind, out itemKind) &&
                SkateParkBuildItems.Recoverable(group.BuildItemState, group.Members.Count, itemKind) &&
                player.IsAdmin && player.userID == group.Owner && Eligible(player) && RecoverableWall(group, player);
            bool removalStarted = false, recoveryClaimed = false, recoverySaved = false;
            string recoveryMessage = null;
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
                if (!RemoveParkProfileCollision(group))
                    throw new InvalidDataException("Park collision restoration is pending. No recovery was claimed and no park member was removed.");
                if (recover)
                {
                    if (!ReferenceEquals(instance, this) || !ReferenceEquals(data, parkData) || !SameParkWorld(data) ||
                        SaveRestore.IsSaving || !player.IsAdmin || player.userID != group.Owner || !Eligible(player) ||
                        !data.Groups.Contains(group) || !RecoverableWall(group, player) ||
                        !SkateParkBuildItems.Recoverable(group.BuildItemState, blocks.Count, itemKind))
                        throw new InvalidDataException("Paid park recovery authority, identity or complete-group state changed.");
                    // Persist the once-only claim before removing anything. Partial removal never yields an item.
                    group.BuildItemState = SkateParkBuildItemState.RecoveryClaimed;
                    SaveParkData();
                    recoveryClaimed = true;
                }
                for (int i = 0; i < blocks.Count; i++)
                {
                    if (parkClosing || !player.IsConnected || !player.CanInteract() || (immediate && !player.IsAdmin) ||
                        !ParkMatches(blocks[i], group, group.Members[i]))
                        throw new InvalidDataException("A remaining park member or actor changed during removal; remaining parts were retained.");
                    if (recover && (!ReferenceEquals(instance, this) || !ReferenceEquals(data, parkData) ||
                        !SameParkWorld(data) || SaveRestore.IsSaving || !player.IsAdmin || player.userID != group.Owner || !Eligible(player) ||
                        !RecoverableWall(group, player)))
                        throw new InvalidDataException("Paid park recovery authority or save/world changed during removal.");
                    removalStarted = true;
                    blocks[i].Kill(BaseNetworkable.DestroyMode.Gib);
                    if (!blocks[i].IsDestroyed)
                        throw new InvalidDataException("Park removal was refused by a kill hook; remaining parts were retained.");
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
                if (recoveryClaimed && ReferenceEquals(instance, this) && !parkClosing &&
                    ReferenceEquals(data, parkData) && SameParkWorld(data))
                {
                    try
                    {
                        if (blocks.Count == SkateParkLayout.PartCount(itemKind) &&
                            data.Groups.Contains(group) && group.Members.Count == blocks.Count &&
                            blocks.Select((b, i) => ParkMatches(b, group, group.Members[i])).All(matches => matches))
                        {
                            // A veto before any actual destruction may be retried, without having returned an item.
                            group.BuildItemState = SkateParkBuildItemState.Paid;
                            SaveParkData();
                        }
                        else if (blocks.Count == SkateParkLayout.PartCount(itemKind) && blocks.All(b => b == null || b.IsDestroyed) &&
                            group.Members.Count == 0 && !data.Groups.Contains(group) && !parkStorageFailed && !SaveRestore.IsSaving)
                        {
                            SaveParkData();
                            recoverySaved = true;
                        }
                        else recoveryMessage = "Partial or uncertain park removal returned no build item.";
                    }
                    catch (Exception error)
                    {
                        // Never retry a minted item because metadata saving or an inventory callback failed.
                        recoveryMessage = "Park recovery metadata is unverified; no build item was returned. " + error.Message;
                    }
                }
            }
            if (recover && recoverySaved && RecoverableWall(group, player))
            {
                string failure;
                if (GiveParkItem(player, itemKind, out failure, data)) recoveryMessage = "Park build item returned once.";
                else recoveryMessage = "Park removal finished, but build-item delivery is unverified: " + failure;
            }
            if (player != null && player.IsConnected)
                player.ConsoleMessage(recoveryMessage ?? (group.Members.Count == 0 ? "Park group demolished." : removalStarted ?
                    "Park demolition is incomplete; surviving parts remain grouped. A kill hook or server error prevented removal." :
                    "Park group remains; check the preceding refusal. Surviving parts stay grouped."));
        }

        private void PruneDestroyedParkMembers(ParkGroup group)
        {
            if (ParkProfileRestorePending(group)) return;
            if (group.Members.Any(member => !ParkMatches(FindParkBlock(member.Id), group, member)))
                if (!RemoveParkProfileCollision(group)) return;
            if (group.Wall != null && (!CurrentWallCleanup(parkData) || !parkData.Groups.Contains(group))) return;
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
            if (parkData != null && parkData.Groups.Any(g => g.Wall != null && g.Wall.Id == id))
                NextTick(() => { if (entity == null || entity.IsDestroyed) CleanupWallAttachments(); });
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
            ParkGroup group = VerifiedParkGroup(block);
            if (group != null) block.grounded = group.Wall == null || WallMatches(FindParkBlock(group.Wall.Id), group.Wall);
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
                    if (allowed && dt > 0 && horizontal > RidingSpeed * dt) session.RidingAt = now;
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
                            supported = supported && Supported(Vector3.Lerp(previous, point, (float)sample / samples));
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

        private bool StallMovementAllowed(TickInterpolatorCache.ReadOnlyState cache, int index, PlayerTick tick,
            double elapsed, out double horizontal, out double seconds, out SkateSessionReason reason)
        {
            horizontal = 0;
            // Stall returns do not finalize ticks, so the native interval can keep accumulating.
            // Cap its budget without changing the interval later native validation receives.
            seconds = Math.Min(elapsed, SkateSessionBounds.MaximumBatchSeconds);
            if (!SkateSessionBounds.Finite(elapsed) || elapsed <= 0)
            { reason = SkateSessionReason.InvalidDelta; return false; }
            bool allowed;
            if (!SkateMovementPolicy.TryAllow(seconds, 0, settings.MaximumHorizontalSpeed, out allowed, out reason)) return false;
            int count = cache.Infos[index].Count;
            if (count < 0 || !SkateMovementPolicy.ValidTickCount(count + 1, cache.BufferSize))
            { reason = SkateSessionReason.InvalidTickCount; return false; }
            int offset = checked(index * cache.BufferSize);
            Vector3 previous = cache.Segments[offset].point;
            double vertical = 0;
            for (int s = 1; s <= count + 1; s++)
            {
                Vector3 point = s <= count ? cache.Segments[offset + s].point : tick.position;
                double nextHorizontal, nextVertical;
                if (!SkateMovementPolicy.TryAccumulate(new SkateMovementPoint(previous.x, previous.y, previous.z),
                    new SkateMovementPoint(point.x, point.y, point.z), horizontal, vertical, out nextHorizontal, out nextVertical))
                { reason = SkateSessionReason.InvalidCoordinate; return false; }
                horizontal = nextHorizontal; vertical = nextVertical; previous = point;
            }
            return SkateMovementPolicy.TryAllow(seconds, horizontal, settings.MaximumHorizontalSpeed, out allowed, out reason) && allowed;
        }

        private bool ExemptNativeStall(BasePlayer player, PlayerTick tick)
        {
            if (settings == null || !Eligible(player) || !Permitted(player) || OptedOut(player) ||
                player.IsReceivingSnapshot || player.IsLoadingAfterTransfer() || tick == null ||
                tick.position.IsNaNOrInfinity() || tick.eyePos.IsNaNOrInfinity() || tick.parentID != player.parentEntity.uid ||
                (player.modelState != null && player.modelState.mounted) ||
                (player.modelStateTick != null && player.modelStateTick.mounted)) return false;
            int index = player.ActivePlayerInd;
            var states = BasePlayer.PlayerStates;
            if (index < 0 || index >= states.PlayerCache.Objects.Length || !ReferenceEquals(states.PlayerCache.Objects[index], player) ||
                index >= states.TickDeltaTime.Length || index >= AntiHack.PlayerStates.Length || states.TickCache == null) return false;
            var cache = states.TickCache.ReadOnly;
            if (index >= cache.Infos.Length || cache.BufferSize < 2) return false;
            int count = cache.Infos[index].Count;
            if (count < 0 || count >= cache.BufferSize) return false;
            int endpointIndex = checked(index * cache.BufferSize + count);
            if (endpointIndex < 0 || endpointIndex >= cache.Segments.Length) return false;
            Vector3 endpoint = cache.Segments[endpointIndex].point;
            float distance = Vector3.Distance(tick.position, endpoint);
            if (!SkateSessionBounds.Finite(distance)) return false;

            double now = clock.Elapsed.TotalSeconds, horizontal = double.NaN, seconds = double.NaN;
            StallStartup startup;
            double age = stallStartups.TryGetValue(player.userID, out startup) && ReferenceEquals(startup.Player, player) ?
                now - startup.Started : double.NaN;
            SkateSessionReason reason = !ready ? unavailableReason : SkateSessionReason.Duration;
            bool exempted = ready && SkateSessionBounds.Finite(age) && age >= 0 && age < NativeStallStartupSeconds &&
                StallMovementAllowed(cache, index, tick, states.TickDeltaTime[index], out horizontal, out seconds, out reason);
            if (distance > 0.5f)
            {
                if (nativeStallCorrections < long.MaxValue) nativeStallCorrections++;
                if (exempted && nativeStallExemptions < long.MaxValue) nativeStallExemptions++;
                var history = diagnostics.GetOrCreate(player.userID, player);
                if (history != null) history.RecordNativeStall(exempted);
                if (now - nativeStallSaidAt >= 2)
                {
                    nativeStallSaidAt = now;
                    Puts("Native stall would-correct=" + nativeStallCorrections.ToString(CultureInfo.InvariantCulture) +
                        "; exempted=" + nativeStallExemptions.ToString(CultureInfo.InvariantCulture) +
                        "; this-exempted=" + exempted + "; reason=" + reason +
                        "; startup-age=" + SkateStatusReport.Number(age) + "s/5s; distance=" + SkateStatusReport.Number(distance) +
                        "m/0.5m; horizontal=" + SkateStatusReport.Number(horizontal) + "m; budget-dt=" + SkateStatusReport.Number(seconds) + "s.");
                }
            }
            return exempted;
        }

        [AutoPatch]
        [HarmonyPatch(typeof(BasePlayer), "UpdatePositionFromTick", new[] { typeof(PlayerTick), typeof(bool) })]
        private static class StallPatch
        {
            private static void Prefix(BasePlayer __instance, PlayerTick __0, ref bool __1)
            {
                if (instance == null || !__1) return;
                try { if (instance.ExemptNativeStall(__instance, __0)) __1 = false; }
                catch (Exception)
                { instance.ready = false; instance.unavailableReason = SkateSessionReason.StallPatchException; }
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
