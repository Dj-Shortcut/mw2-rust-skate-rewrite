using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Text;
using System.Linq;
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
                !SkateSessionBounds.Finite(anchorYaw) || Math.Abs(anchorYaw) > 1000000) return false;
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
        private static bool Coordinate(double value)
        { return SkateSessionBounds.Finite(value) && Math.Abs(value) <= 100000; }
    }

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

namespace Oxide.Plugins
{
    [Info("ShortcutSkate", "Dj-Shortcut", "0.3.1")]
    [Description("Player-scoped skateboard movement bounds and saved building-plan park assemblies.")]
    public sealed class ShortcutSkate : RustPlugin
    {
        private const string UsePermission = "shortcutskate.use";
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
        private readonly Dictionary<ulong, ParkSelection> parkSelections = new Dictionary<ulong, ParkSelection>();
        private readonly Dictionary<ulong, ParkGroup> parkMembers = new Dictionary<ulong, ParkGroup>();
        private readonly List<BaseEntity> parkRollback = new List<BaseEntity>();
        private ParkData parkData;
        private bool parkDataBroken;
        private bool parkReady;
        private bool parkBusy;
        private bool parkClosing;
        private readonly HashSet<string> parkDemolitionPending = new HashSet<string>(StringComparer.Ordinal);
        private BuildingBlock parkDemolitionHookBlock;
        private BasePlayer parkDemolitionHookPlayer;
        private bool parkDemolitionHookImmediate;
        private string ParkFilename { get { return Path.Combine(Interface.Oxide.DataFileSystem.Directory, "ShortcutSkate.Parks.json"); } }

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
            public SkateParkKind Kind;
            public double Expires;
        }

        private sealed class ParkAnchorSnapshot
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public BuildingGrade.Enum Grade;
            public float Health;
            public ulong Owner, Skin;
            public bool Grounded, Saving;
            public uint Building;
            public float Stability;
            public int Distance;
            public float TimePlaced;
            public uint Colour;
        }

        private sealed class ParkNativePart
        {
            public string Prefab;
            public Construction Construction;
            public DeployVolume[] Volumes;
            public BuildingGrade.Enum Grade;
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
            public Vector3 LastEnd;
            public double LastObserved;
            public SkateSessionHistory History;
            public SkateSessionObservation Observation;
        }

        protected override void LoadDefaultConfig()
        {
            Config.WriteObject(new Settings { Version = 1, AllowEveryone = true,
                MaximumHorizontalSpeed = 14, MaximumAirtime = 1 }, true);
        }

        protected override void LoadConfig()
        {
            ready = false;
            unavailableReason = SkateSessionReason.Unavailable;
            settings = null;
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
            return value;
        }

        private void Init()
        {
            permission.RegisterPermission(UsePermission, this);
            instance = this;
            groundMask = LayerMask.GetMask("Terrain", "World", "Construction", "Deployed");
            ReadParkData();
        }

        private void OnServerInitialized()
        {
            InitializeParks();
            ready = settings != null && groundMask != 0 && Patched("ValidateMoves", typeof(MovePatch), false) &&
                Patched("AreSpeeding", typeof(SpeedPatch), true) && Patched("AreFlying", typeof(FlyPatch), true);
            if (!ready) PrintError("Skating is disabled: matching movement patches and valid config are required.");
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

        private bool Permitted(BasePlayer player)
        { return settings != null && (settings.AllowEveryone || permission.UserHasPermission(player.UserIDString, UsePermission)); }

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
            string state = candidate ? (session.Bounds.Active ? "Skating on." : "Skating waiting for supported movement.") :
                (OptedOut(player) ? "Skating off; automatic skating is disabled for this connection." : "Skating off.");
            SkateSessionHistory history;
            if (!diagnostics.TryGet(player.userID, player, out history))
                history = diagnostics.GetOrCreate(player.userID, player);
            if (candidate) session.History = history;
            arg.ReplyWith(state + "\nSteam ID " + target.ToString(CultureInfo.InvariantCulture) +
                "; ready=" + ready + "; unavailable-reason=" + (ready ? SkateSessionReason.None : unavailableReason) + "; mode=" + (candidate ? (session.Manual ? "manual" : "automatic") : "inactive") +
                "; last-batch-allowed=" + (candidate && session.Allowed) +
                "; speed-limit=" + SkateStatusReport.Number(settings == null ? double.NaN : settings.MaximumHorizontalSpeed) +
                "m/s; air-limit=" + SkateStatusReport.Number(settings == null ? double.NaN : settings.MaximumAirtime) +
                "s; automatic=" + (settings != null && settings.AutomaticEligiblePlayers) +
                "; rearm-required=" + SkateStatusReport.Number(settings == null ? double.NaN : settings.AutomaticRearmSeconds) +
                "s.\n" + SkateStatusReport.Format(history, clock.Elapsed.TotalSeconds));
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
            if (!Eligible(player) || !Supported(player.transform.position))
            { arg.ReplyWith("Stand on dry ground, alive and awake, to skate."); return; }
            optedOut.Remove(player.userID);
            if (candidate && session.Bounds.Active) { session.Manual = true; arg.ReplyWith("Skating on."); return; }
            Stop(player, SkateSessionReason.ManualRestart);
            session = new Session { Player = player, Manual = true, HasEnd = true, LastEnd = player.transform.position,
                History = diagnostics.GetOrCreate(player.userID, player) };
            session.LastObserved = clock.Elapsed.TotalSeconds;
            if (!session.Bounds.Begin(session.LastObserved, true)) return;
            sessions[player.userID] = session;
            SkateSessionObservation activation = session.Bounds.LastObservation;
            activation.Reason = SkateSessionReason.ManualOn;
            activation.MaximumSpeed = settings.MaximumHorizontalSpeed;
            activation.MaximumAirtime = settings.MaximumAirtime;
            activation.RearmSeconds = 0; activation.RequiredRearmSeconds = settings.AutomaticRearmSeconds;
            session.Observation = activation;
            if (session.History != null) { session.History.Sample(activation); session.History.Activate(activation); }
            arg.ReplyWith("Skating on.");
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
            parkClosing = true;
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

        private static bool SameParkWorld(ParkData data)
        {
            return data != null && !string.IsNullOrEmpty(SaveRestore.WipeId) &&
                data.WipeId == SaveRestore.WipeId && data.SaveCreatedTicks == ParkSaveTicks() &&
                data.Seed == World.Seed && data.Size == World.Size && data.Checksum == (World.Checksum ?? "");
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

        // Void is intentional: a bool from this hook would abort the game's entire normal save-load loop.
        // OwnerID has not loaded yet; validate the saved protobuf owner, ID, prefab and pose instead.
        private void OnSaveLoad(Dictionary<BaseEntity, ProtoBuf.Entity> entities)
        {
            if (parkDataBroken || parkData == null || !SameParkWorld(parkData) || entities == null) return;
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
            if (parkDataBroken || parkClosing) return;
            try
            {
                if (string.IsNullOrEmpty(SaveRestore.WipeId) || SaveRestore.SaveCreatedTime.Ticks <= 0)
                    throw new InvalidDataException("A loaded native save identity is required.");
                if (parkData == null || !SameParkWorld(parkData))
                {
                    // Never adopt a reused network ID from a different save/world or recreate old pieces.
                    if (parkData != null) PrintWarning("Old park grouping belongs to another world/save; no old entity IDs were adopted.");
                    parkData = NewParkData();
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
                SaveParkData();
                parkReady = true;
            }
            catch (Exception error)
            { PrintError("Park creation is unavailable; verified existing support is retained. " + error.Message); }
        }

        private void SaveParkData()
        {
            if (parkDataBroken || parkData == null || !SameParkWorld(parkData))
                throw new InvalidDataException("Park metadata has no matching native save identity.");
            string json = JsonConvert.SerializeObject(parkData, Formatting.Indented);
            ParseParkData(json);
            string filename = ParkFilename;
            string temporary = filename + ".writing";
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

        private void SaveParksSafely()
        {
            try { SaveParkData(); }
            catch (Exception error)
            {
                parkReady = false;
                PrintError("Park metadata could not be saved; new park placement is disabled. " + error.Message);
            }
        }

        private void OnServerSave() { if (!parkBusy && !parkDataBroken && parkData != null) SaveParksSafely(); }

        [ConsoleCommand("skate.piece")]
        private void SelectParkPiece(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !player.IsAdmin)
            { arg.ReplyWith("Use this command as an in-game administrator."); return; }
            if (arg.Args == null || arg.Args.Length != 1)
            { arg.ReplyWith("Usage: skate.piece <bank|kicker|quarterpipe|ledge-low|ledge-high|long-ledge|funbox|halfpipe|cancel>."); return; }
            if (string.Equals(arg.GetString(0), "cancel", StringComparison.OrdinalIgnoreCase))
            { parkSelections.Remove(player.userID); arg.ReplyWith("Park selection cancelled."); return; }
            SkateParkKind kind;
            if (!SkateParkLayout.TryKind(arg.GetString(0), out kind))
            { arg.ReplyWith("Unknown park piece. Use bank, kicker, quarterpipe, ledge-low, ledge-high, long-ledge, funbox or halfpipe."); return; }
            if (!parkReady || parkClosing || parkBusy || !Eligible(player))
            { arg.ReplyWith("Park placement is unavailable; be alive, awake, unmounted and on dry land."); return; }
            PruneParkSelections();
            if (parkData.Groups.Count >= MaximumParkGroups ||
                (!parkSelections.ContainsKey(player.userID) && parkSelections.Count >= MaximumParkSelections))
            { arg.ReplyWith("The park group or pending-selection limit has been reached."); return; }
            parkSelections[player.userID] = new ParkSelection { Player = player, Kind = kind,
                Expires = clock.Elapsed.TotalSeconds + 120 };
            arg.ReplyWith("Selected " + SkateParkLayout.Name(kind) + ". Place one normal horizontal square floor with a building plan within 120 seconds. The next placement consumes this selection; " +
                (kind == SkateParkKind.Halfpipe ? "the halfpipe uses a Stone flat, Metal transitions/decks/sides and Wood back rails." : "the assembly will be Stone."));
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
            if (parkDataBroken || parkData == null || !SameParkWorld(parkData))
            { arg.ReplyWith("Park grouping has no verified data for this world/save. Check the server log."); return; }
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

        private void OnEntityBuilt(Planner planner, GameObject gameObject)
        {
            BasePlayer player = planner == null ? null : planner.GetOwnerPlayer();
            if (player == null) return;
            ParkSelection selection;
            if (!parkSelections.TryGetValue(player.userID, out selection)) return;
            parkSelections.Remove(player.userID);
            if (!ReferenceEquals(selection.Player, player) || clock.Elapsed.TotalSeconds > selection.Expires) return;
            BuildingBlock anchor = gameObject == null ? null : gameObject.GetComponent<BuildingBlock>();
            ulong id = ParkId(anchor);
            // Planner still has normal item/material bookkeeping to do after this hook.
            NextTick(() => BuildParkPiece(player, planner, anchor, id, selection.Kind));
        }

        private static bool HorizontalParkAnchor(BuildingBlock anchor)
        {
            return anchor != null && !anchor.IsDestroyed && anchor.net != null && anchor.enableSaving &&
                anchor.PrefabName == SkatePracticeLayout.FloorPrefab && anchor.GetParentEntity() == null &&
                Coordinate(anchor.transform.position) && Vector3.Dot(anchor.transform.up, Vector3.up) >= (1f - 1e-5f);
        }

        private static bool ParkPrefab(SkateParkPart part, out ParkNativePart definition)
        {
            string path = part.Prefab;
            definition = null;
            var manifest = GameManifest.Current;
            var prefab = GameManager.server.FindPrefab(path);
            if (manifest == null || manifest.entities == null ||
                !manifest.entities.Any(p => string.Equals(p, path, StringComparison.Ordinal)) ||
                prefab == null || prefab.GetComponent<BuildingBlock>() == null) return false;
            uint id = StringPool.Get(path);
            Construction construction = PrefabAttribute.server.Find<Construction>(id);
            BuildingGrade.Enum grade;
            switch (part.Material)
            {
                case SkateParkMaterial.Stone: grade = BuildingGrade.Enum.Stone; break;
                case SkateParkMaterial.Metal: grade = BuildingGrade.Enum.Metal; break;
                case SkateParkMaterial.Wood: grade = BuildingGrade.Enum.Wood; break;
                default: return false;
            }
            // Grade lookup can fall back; require the exact requested material and default skin.
            var selected = construction == null ? null : construction.GetGrade(grade, 0);
            if (selected == null || selected.gradeBase == null || selected.gradeBase.type != grade || selected.gradeBase.skin != 0) return false;
            Bounds bounds = construction.bounds;
            if (!Coordinate(bounds.center) || !Coordinate(bounds.size) || bounds.size.x < 2.9f || bounds.size.x > 3.1f ||
                Math.Abs(bounds.center.x) > 0.05f) return false;
            if (path == SkateParkLayout.FloorPrefab)
            {
                if (bounds.size.z < 2.9f || bounds.size.z > 3.1f || bounds.size.y <= 0 || bounds.size.y > 1 ||
                    Math.Abs(bounds.center.z) > 0.05f || Math.Abs(bounds.center.y) > 0.5f) return false;
            }
            else if (path == SkateParkLayout.LowWallPrefab || path == SkateParkLayout.HalfWallPrefab)
            {
                double height = path == SkateParkLayout.LowWallPrefab ? 1 : 1.5;
                if (Math.Abs(bounds.size.y - height) > 0.1 || bounds.size.z <= 0 || bounds.size.z > 0.6f ||
                    Math.Abs(bounds.center.z) > 0.05f || Math.Abs(bounds.min.y) > 0.05f) return false;
            }
            else return false;
            DeployVolume[] volumes = PrefabAttribute.server.FindAll<DeployVolume>(id);
            if (volumes == null || TerrainMeta.HeightMap == null) return false;
            definition = new ParkNativePart { Prefab = path, Construction = construction, Volumes = volumes, Grade = grade };
            return true;
        }

        private static bool ParkPrefabs(SkateParkKind kind, out ParkNativePart[] definitions)
        {
            definitions = new ParkNativePart[SkateParkLayout.PartCount(kind)];
            if (definitions.Length == 0) return false;
            var cache = new Dictionary<string, ParkNativePart>(StringComparer.Ordinal);
            for (int i = 0; i < definitions.Length; i++)
            {
                SkateParkPart part;
                if (!SkateParkLayout.TryPart(kind, i, out part)) return false;
                string key = part.Prefab + ":" + ((int)part.Material).ToString(CultureInfo.InvariantCulture);
                ParkNativePart definition;
                if (!cache.TryGetValue(key, out definition))
                {
                    if (!ParkPrefab(part, out definition)) return false;
                    cache.Add(key, definition);
                }
                definitions[i] = definition;
            }
            return true;
        }

        private static bool ParkProximity(BasePlayer player, Construction construction, Vector3 position,
            Quaternion rotation, BuildingBlock anchor, List<BuildingBlock> transactionParts = null)
        {
            // Native BuildingProximity.Check has no ignored-entity argument. Retain its exact
            // neighbour rule while skipping only the normally placed anchor being converted.
            var obb = new OBB(position, rotation, construction.bounds);
            var neighbours = new List<BuildingBlock>();
            Vis.Entities(obb.position, obb.extents.magnitude + 2, neighbours, 2097152, QueryTriggerInteraction.Collide);
            uint buildingId = 0;
            foreach (BuildingBlock neighbour in neighbours)
            {
                if (ReferenceEquals(neighbour, anchor) || (transactionParts != null && transactionParts.Contains(neighbour))) continue;
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
                if (ReferenceEquals(entity, anchor) || (transactionParts != null && transactionParts.Contains(entity as BuildingBlock))) continue;
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
                Grade = anchor.grade, Health = anchor.Health(), Owner = anchor.OwnerID, Skin = anchor.skinID,
                Grounded = anchor.grounded, Saving = anchor.enableSaving, Building = anchor.buildingID,
                Stability = anchor.cachedStability, Distance = anchor.cachedDistanceFromGround,
                TimePlaced = anchor.timePlaced, Colour = anchor.customColour };
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

        private void RestoreParkAnchor(BuildingBlock anchor, ParkAnchorSnapshot snapshot, ulong id)
        {
            if (anchor == null || anchor.IsDestroyed || ParkId(anchor) != id)
                throw new InvalidDataException("The original placed anchor no longer exists; it cannot be restored.");
            Bounds oldBounds = ParkWorldBounds(anchor);
            anchor.ServerWorldPosition = snapshot.Position;
            anchor.ServerRotation = snapshot.Rotation;
            anchor.OwnerID = snapshot.Owner;
            anchor.skinID = snapshot.Skin;
            anchor.grounded = snapshot.Grounded;
            anchor.SetGrade(snapshot.Grade);
            anchor.SetHealth(snapshot.Health);
            anchor.AttachToBuilding(snapshot.Building);
            anchor.cachedStability = snapshot.Stability;
            anchor.cachedDistanceFromGround = snapshot.Distance;
            anchor.EnableSaving(snapshot.Saving);
            RefreshParkBlock(anchor, oldBounds);
            anchor.SetCustomColour(snapshot.Colour);
            anchor.timePlaced = snapshot.TimePlaced;
            anchor.SetHealth(snapshot.Health);
            if (anchor.IsDestroyed || ParkId(anchor) != id || anchor.OwnerID != snapshot.Owner ||
                anchor.grade != snapshot.Grade || anchor.skinID != snapshot.Skin || anchor.grounded != snapshot.Grounded ||
                anchor.enableSaving != snapshot.Saving || anchor.buildingID != snapshot.Building ||
                anchor.timePlaced != snapshot.TimePlaced || anchor.customColour != snapshot.Colour ||
                (anchor.transform.position - snapshot.Position).sqrMagnitude > 0.000001f ||
                Quaternion.Angle(anchor.transform.rotation, snapshot.Rotation) > 0.01f ||
                Math.Abs(anchor.Health() - snapshot.Health) > 0.01f)
                throw new InvalidDataException("The original anchor did not retain its rollback state.");
        }

        private void BuildParkPiece(BasePlayer player, Planner planner, BuildingBlock anchor, ulong anchorId, SkateParkKind kind)
        {
            if (parkClosing) return;
            if (!parkReady || parkBusy || SaveRestore.IsSaving || player == null || !player.IsAdmin || !Eligible(player) || planner == null ||
                !ReferenceEquals(planner.GetOwnerPlayer(), player) || !HorizontalParkAnchor(anchor) ||
                ParkId(anchor) != anchorId || anchor.OwnerID != (ulong)player.userID || parkMembers.ContainsKey(anchorId))
            { if (player != null && player.IsConnected) player.ConsoleMessage("Park selection consumed; place a new normal horizontal square floor after selecting again."); return; }
            ParkNativePart[] definitions;
            if (parkData.Groups.Count >= MaximumParkGroups || !SameParkWorld(parkData) || !ParkPrefabs(kind, out definitions))
            { player.ConsoleMessage("Park selection consumed; save identity, group capacity or required native prefab/material/bounds data is unavailable."); return; }
            ParkAnchorSnapshot snapshot = CaptureParkAnchor(anchor);
            var group = new ParkGroup { Id = Guid.NewGuid().ToString("N"), Owner = player.userID,
                Kind = SkateParkLayout.Name(kind), AnchorX = snapshot.Position.x, AnchorY = snapshot.Position.y,
                AnchorZ = snapshot.Position.z, AnchorYaw = snapshot.Rotation.eulerAngles.y, Members = new List<ParkMember>() };
            var parts = new ParkMember[SkateParkLayout.PartCount(kind)];
            for (int i = 0; i < parts.Length; i++)
            {
                SkateParkTransform transform;
                if (!SkateParkLayout.TryTransform(kind, i, group.AnchorX, group.AnchorY, group.AnchorZ, group.AnchorYaw, out transform)) return;
                parts[i] = new ParkMember { Index = i, Prefab = StringPool.Get(definitions[i].Prefab), X = transform.X, Y = transform.Y,
                    Z = transform.Z, Yaw = transform.Yaw, Pitch = transform.Pitch };
            }
            var created = new List<BuildingBlock>();
            bool mutated = false, recorded = false, committed = false;
            parkBusy = true;
            try
            {
                string failure;
                if (!ParkPreflight(player, planner, anchor, definitions, parts, out failure))
                    throw new InvalidDataException(failure);
                // A CanBuild hook may reenter or mutate the original anchor. Confirm the snapshot after all hooks.
                if (parkClosing || !Eligible(player) || !player.IsAdmin || !HorizontalParkAnchor(anchor) ||
                    ParkId(anchor) != anchorId || anchor.OwnerID != group.Owner ||
                    (anchor.transform.position - snapshot.Position).sqrMagnitude > 0.000001f ||
                    Quaternion.Angle(anchor.transform.rotation, snapshot.Rotation) > 0.01f)
                    throw new InvalidDataException("The placed anchor or administrator changed during preflight.");
                snapshot = CaptureParkAnchor(anchor);
                anchor.EnableSaving(false);
                mutated = true;
                for (int i = 1; i < parts.Length; i++)
                {
                    BaseEntity entity = GameManager.server.CreateEntity(definitions[i].Prefab,
                        ParkPosition(parts[i]), ParkRotation(parts[i]), true);
                    if (entity == null) throw new InvalidDataException("A park part could not be created.");
                    parkRollback.Add(entity); // Track before cast, Spawn and all reentrant hooks.
                    BuildingBlock block = entity as BuildingBlock;
                    if (block == null) throw new InvalidDataException("A park prefab is not a native building block.");
                    created.Add(block);
                    block.enableSaving = false;
                    block.CullBushes = false;
                    block.grounded = true;
                    block.OwnerID = group.Owner;
                    block.Spawn();
                    if (block.IsDestroyed || block.net == null || block.blockDefinition == null)
                        throw new InvalidDataException("A park part failed to initialize.");
                    block.skinID = 0;
                    block.SetGrade(definitions[i].Grade);
                    block.SetHealthToMax();
                    block.AttachToBuilding(snapshot.Building);
                    block.StartBeingDemolishable();
                    parts[i].Id = ParkId(block);
                    RefreshParkBlock(block, ParkWorldBounds(block));
                }
                Bounds oldBounds = ParkWorldBounds(anchor);
                anchor.ServerWorldPosition = ParkPosition(parts[0]);
                anchor.ServerRotation = ParkRotation(parts[0]);
                anchor.grounded = true;
                anchor.skinID = 0;
                anchor.SetGrade(definitions[0].Grade);
                anchor.SetHealthToMax();
                anchor.AttachToBuilding(snapshot.Building);
                parts[0].Id = anchorId;
                RefreshParkBlock(anchor, oldBounds);
                foreach (ParkMember part in parts)
                {
                    Construction construction = definitions[part.Index].Construction;
                    var target = new Construction.Target { valid = true, player = player, onTerrain = true,
                        position = ParkPosition(part), rotation = ParkRotation(part).eulerAngles,
                        normal = Vector3.up };
                    if (Interface.CallHook("CanBuild", planner, construction, target) != null)
                        throw new InvalidDataException("A building hook rejected final park registration.");
                }
                foreach (ParkMember part in parts)
                {
                    BuildingBlock block = part.Index == 0 ? anchor : created[part.Index - 1];
                    ParkNativePart definition = definitions[part.Index];
                    Construction construction = definition.Construction;
                    if (parkClosing || !player.IsAdmin || !Eligible(player) || !ParkMatches(block, group, part) ||
                        block.grade != definition.Grade || block.skinID != 0 || !ReferenceEquals(block.blockDefinition, construction) ||
                        !block.grounded || block.enableSaving || block.buildingID != snapshot.Building ||
                        !player.CanBuild(ParkPosition(part), ParkRotation(part), construction.bounds, false) ||
                        !ParkProximity(player, construction, ParkPosition(part), ParkRotation(part), anchor, created) ||
                        !ParkFootprint(ParkPosition(part), ParkRotation(part), construction.bounds, anchor, created,
                            LayerMask.GetMask("World", "Construction", "Deployed", "Player (Server)"), out failure))
                        throw new InvalidDataException("A park member changed during creation or final permission verification.");
                }
                group.Members.AddRange(parts);
                parkData.Groups.Add(group);
                recorded = true;
                // Durable manifest first; EnableSaving also inserts spawned extras into native saveList.
                SaveParkData();
                foreach (ParkMember part in parts)
                {
                    BuildingBlock block = part.Index == 0 ? anchor : created[part.Index - 1];
                    block.EnableSaving(true);
                    if (parkClosing || !Eligible(player) || !player.IsAdmin || !ParkMatches(block, group, part) ||
                        block.grade != definitions[part.Index].Grade || block.skinID != 0 ||
                        !ReferenceEquals(block.blockDefinition, definitions[part.Index].Construction) ||
                        !block.enableSaving || !block.grounded || block.buildingID != snapshot.Building)
                        throw new InvalidDataException("A park member changed during save registration.");
                }
                foreach (ParkMember part in parts) parkMembers.Add(part.Id, group);
                foreach (BuildingBlock block in created) parkRollback.Remove(block);
                committed = true;
                player.ConsoleMessage("Park " + group.Kind + " created as a saved native building assembly (" +
                    parts.Length.ToString(CultureInfo.InvariantCulture) + " parts). Use a hammer to demolish the group.");
            }
            catch (Exception error)
            {
                if (recorded)
                {
                    parkData.Groups.Remove(group);
                    foreach (ParkMember part in parts) parkMembers.Remove(part.Id);
                }
                bool restored = !mutated && anchor != null && !anchor.IsDestroyed && ParkId(anchor) == anchorId;
                if (mutated)
                {
                    try { RestoreParkAnchor(anchor, snapshot, anchorId); restored = true; }
                    catch (Exception rollbackError) { PrintError("Park anchor rollback failed. " + rollbackError.Message); }
                }
                CleanupParkRollback();
                if (recorded) SaveParksSafely();
                PrintWarning("Park creation rejected. " + error.Message);
                if (player != null && player.IsConnected)
                    player.ConsoleMessage("Park selection consumed. " + error.Message +
                        (restored ? " The original floor is retained." : " Original-floor restoration is incomplete; check the server log.") +
                        (parkRollback.Count == 0 ? "" : " Extra-part cleanup is incomplete; check the server log."));
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
            if (block == null || parkDataBroken || parkData == null || !SameParkWorld(parkData) ||
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
            if (id == 0 || !parkMembers.TryGetValue(id, out group)) return;
            // Kill hooks can cancel destruction. Never delete metadata or cascade while the origin is alive.
            NextTick(() =>
            {
                if (!parkClosing && (entity == null || entity.IsDestroyed) && parkData != null && SameParkWorld(parkData))
                    PruneDestroyedParkMembers(group);
            });
        }

        private void OnEntityStabilityCheck(StabilityEntity entity)
        {
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
                    if (!settings.AutomaticEligiblePlayers) continue;
                    session = new Session { Player = player, History = diagnostics.GetOrCreate(player.userID, player),
                        Observation = SkateSessionObservation.Empty(now, SkateSessionReason.None) };
                    sessions[player.userID] = session;
                }
                if (session.History == null) session.History = diagnostics.GetOrCreate(player.userID, player);
                session.Allowed = false;
                session.Batch = batch;
                if (!session.Manual && !settings.AutomaticEligiblePlayers) { Stop(player, SkateSessionReason.AutomaticDisabled); continue; }
                SkateSessionObservation value = Snapshot(session, SkateSessionReason.None, now);
                value.Batch = batch; value.DeltaSeconds = value.HorizontalPath = value.VerticalPath = double.NaN;
                value.SegmentCount = value.BufferSize = -1; value.PositionGap = double.NaN; value.SupportKnown = false;
                try
                {
                    var cache = states.TickCache;
                    int count = cache.Infos[index].Count;
                    value.SegmentCount = count; value.BufferSize = cache.BufferSize;
                    if (count < 1 || count >= cache.BufferSize)
                    { value.Reason = SkateSessionReason.InvalidTickCount; Disarm(session, value); continue; }
                    int offset = checked(index * cache.BufferSize);
                    double dt = states.TickDeltaTime[index];
                    value.DeltaSeconds = dt;
                    if (!SkateSessionBounds.Finite(dt) || dt <= 0 || dt > SkateSessionBounds.MaximumBatchSeconds)
                    { value.Reason = SkateSessionReason.InvalidDelta; Disarm(session, value); continue; }
                    Vector3 previous = cache.Segments[offset].point;
                    if (!Coordinate(previous))
                    { value.Reason = SkateSessionReason.InvalidCoordinate; Disarm(session, value); continue; }
                    if (session.HasEnd) value.PositionGap = Math.Sqrt((previous - session.LastEnd).sqrMagnitude);
                    if (session.HasEnd && (previous - session.LastEnd).sqrMagnitude > 0.0001f)
                    { value.Reason = SkateSessionReason.PositionDiscontinuity; Disarm(session, value); continue; }
                    bool supported = Supported(previous);
                    value.FullySupported = supported; value.SupportKnown = true;
                    double horizontal = 0, vertical = 0;
                    bool invalidCoordinate = false, supportComplete = true;
                    for (int s = 1; s <= count; s++)
                    {
                        Vector3 point = cache.Segments[offset + s].point;
                        if (!Coordinate(point)) { horizontal = double.NaN; invalidCoordinate = true; supportComplete = false; break; }
                        double x = (double)point.x - previous.x, z = (double)point.z - previous.z;
                        horizontal += Math.Sqrt(x * x + z * z);
                        vertical += Math.Abs((double)point.y - previous.y);
                        value.HorizontalPath = horizontal; value.VerticalPath = vertical;
                        if (horizontal > settings.MaximumHorizontalSpeed * dt || vertical > 40 * dt) { supportComplete = false; break; }
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
                        if (invalidCoordinate && value.Reason == SkateSessionReason.InvalidHorizontalPath)
                            value.Reason = SkateSessionReason.InvalidCoordinate;
                        if (!session.Allowed) { Disarm(session, value); continue; }
                    }
                    else
                    {
                        bool rearmed = session.Rearm.Observe(now, dt, horizontal, vertical,
                            supported, settings.MaximumHorizontalSpeed, settings.AutomaticRearmSeconds);
                        value = Context(session.Rearm.LastObservation, value);
                        value.SupportKnown = supportComplete;
                        value.AirSeconds = session.Bounds.AirSeconds;
                        if (invalidCoordinate && value.Reason == SkateSessionReason.InvalidHorizontalPath)
                            value.Reason = SkateSessionReason.InvalidCoordinate;
                        if (session.Rearm.Seconds == 0) { Disarm(session, value); continue; }
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
                            }
                            session.Rearm.Reset();
                            if (!session.Allowed) { Disarm(session, value); continue; }
                        }
                    }
                    session.Observation = value;
                    if (session.History != null) session.History.Sample(value);
                    session.HasEnd = true;
                    session.LastEnd = previous;
                    session.LastObserved = now;
                }
                catch (Exception)
                { value.Reason = SkateSessionReason.BatchException; value.SupportKnown = false; Disarm(session, value); }
            }
        }

        private static bool Coordinate(Vector3 value)
        {
            return SkateSessionBounds.Finite(value.x) && SkateSessionBounds.Finite(value.y) &&
                SkateSessionBounds.Finite(value.z) && Math.Abs(value.x) <= 100000 &&
                Math.Abs(value.y) <= 100000 && Math.Abs(value.z) <= 100000;
        }

        private void Filter(ref BasePlayer.PlayerServerStates.ReadOnly states,
                            NativeArray<int>.ReadOnly indices, NativeArray<bool> results)
        {
            if (!ready || !validating) return;
            for (int n = 0; n < indices.Length; n++)
            {
                int index = indices[n];
                BasePlayer player = states.PlayerCache.Objects[index];
                Session session;
                if (player != null && player.ActivePlayerInd == index &&
                    sessions.TryGetValue(player.userID, out session) && ReferenceEquals(player, session.Player) &&
                    session.Batch == batch && session.Allowed && session.Bounds.Active &&
                    !OptedOut(player) && Eligible(player) && Permitted(player)) results[n] = false;
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
                try { instance.Filter(ref __0, __2, __3); }
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
                try { instance.Filter(ref __0, __3, __4); }
                catch (Exception) { instance.validating = false; instance.ready = false; instance.unavailableReason = SkateSessionReason.FlyPatchException; }
            }
        }
    }
}
