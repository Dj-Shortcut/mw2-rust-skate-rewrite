using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Shortcut.RustMod;
using UnityEngine;

public static class SkatePhysicsDiagnostic
{
    public static bool Enabled;
    private const int MaximumReports = 12, NodesPerFrame = 128, NodesPerRoot = 256, MaximumDepth = 16, ChildrenPerNode = 64;
    private const int ComponentsPerNode = 8, NativeLimit = 1024, RiderLimit = 8, PairsPerFrame = 64;
    private const float ReportInterval = 2f, PeriodicInterval = 4f, JobSeconds = 5f;
    private struct Motion
    {
        internal float At, RawDt, Dt, Speed, Horizontal, Path, Vertical, Clearance, Normal, Tangent, Slope;
        internal double Charge, Bottom;
        internal SkatePumpPhase Pump;
        internal Vector3 Delta, Incoming, Previous, Command;
        internal RideMode Mode;
        internal bool Grounded, Ramp, Held, Push, Brake, Crouch, Pad, SawAscent;
        internal string Contact;
    }
    private struct Node { internal Transform Transform; internal int Depth; internal Node(Transform t, int depth) { Transform = t; Depth = depth; } }
    private static readonly Motion[] ring = new Motion[4];
    private static int ringNext, ringCount, reports, resets, walls, verts, pumps;
    private static bool hadPosition, hadCounters, lastHeld;
    private static Vector3 lastPosition;
    private static Motion incoming;
    private static string contact = "none", pending = "", pendingUtc;
    private static Motion[] pendingSamples;
    private static float pendingAt, nextReport, nextPeriodic, nextAttempt;
    private static Job job;

    public static string Enable()
    {
        Enabled = true; reports = 0; nextReport = nextPeriodic = 0f; Reset();
        return "read-only physics diagnostics enabled; at most 12 bounded reports; no physics changes";
    }
    public static void Reset()
    {
        job = null; pending = ""; pendingSamples = null; hadPosition = hadCounters = false; ringCount = ringNext = 0;
    }
    public static void NewRide() { if (Enabled) Reset(); }
    public static void Contact(RaycastHit hit, bool found)
    {
        if (!Enabled || reports >= MaximumReports) return;
        try
        {
            contact = !found ? "none" : SkateParks.IsCoping(hit) ? "own-coping" : SkateParks.IsRamp(hit) ? "own-ramp" :
                hit.collider != null && hit.collider.GetComponentInParent<BuildingBlock>() != null ? "native-building" : "world";
        }
        catch { contact = "unavailable"; }
    }
    public static void BeginStep(Vector3 position, Vector3 nativeVelocity, Vector3 previousCommand, float rawDt, float dt)
    {
        if (!Enabled || reports >= MaximumReports) return;
        incoming = new Motion { At = Time.realtimeSinceStartup, RawDt = rawDt, Dt = dt, Incoming = nativeVelocity,
            Previous = previousCommand, Delta = hadPosition ? position - lastPosition : Vector3.zero };
        lastPosition = position; hadPosition = true;
    }
    public static void EndStep(Vector3 command)
    {
        if (!Enabled || reports >= MaximumReports) return;
        try
        {
            var m = incoming; m.Command = command; m.Mode = SkateRide.Mode; m.Speed = SkateRide.Speed;
            m.Horizontal = SkateRide.HorizontalSpeed; m.Path = SkateRide.PathSpeed; m.Vertical = SkateRide.ActualV;
            m.Clearance = SkateRide.Clearance; m.Normal = SkateRide.Normal.y; m.Tangent = SkateRide.Tangent.y; m.Slope = SkateRide.SlopeDegrees;
            m.Grounded = SkateRide.Grounded; m.Ramp = SkateRide.OnRamp; m.Held = SkateRide.Held; m.Contact = contact;
            m.Push = SkateKeys.Push; m.Brake = SkateKeys.Brake; m.Crouch = SkateKeys.Crouch; m.Pad = SkateKeys.Pad;
            m.Pump = SkateRide.Pump.Phase; m.Charge = SkateRide.Pump.ChargeSeconds; m.Bottom = SkateRide.Pump.BottomSeconds; m.SawAscent = SkateRide.Pump.SawAscent;
            ring[ringNext] = m; ringNext = (ringNext + 1) % ring.Length; ringCount = Math.Min(ring.Length, ringCount + 1);
            var reason = !hadCounters ? "mount" : SkateRide.Resets != resets ? "reverse-step" :
                m.Held && !lastHeld ? "held" : SkateRide.WallStops != walls ? "wall-stop" :
                SkateRide.Verts != verts ? "lip-exit" : SkateRide.Pumps != pumps ? "pump-award" : "";
            resets = SkateRide.Resets; walls = SkateRide.WallStops; verts = SkateRide.Verts; pumps = SkateRide.Pumps;
            hadCounters = true; lastHeld = m.Held;
            if (reason.Length > 0) Request(reason, m.At);
        }
        catch (Exception e) { Fail(e); }
    }
    private static void Request(string reason, float now)
    {
        if (reports >= MaximumReports || now < nextReport || job != null || pending.Length > 0) return;
        pending = reason; pendingAt = now; pendingUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        pendingSamples = new Motion[ringCount];
        for (var i = 0; i < ringCount; i++) pendingSamples[i] = ring[(ringNext - ringCount + i + ring.Length) % ring.Length];
    }
    public static void Frame(float now)
    {
        if (!Enabled || reports >= MaximumReports) return;
        try
        {
            if (SkateRig.Body == null) { job = null; return; }
            if (job == null && pending.Length == 0 && SkateRide.On && now >= nextPeriodic) Request("periodic", now);
            if (job == null && pending.Length > 0 && now >= nextAttempt)
            {
                SkateParks.PhysicsOwnership owner;
                if (SkateParks.ReadPhysicsOwnership(SkateRig.Body.position, out owner))
                { job = new Job(owner, SkateRig.Body, pending, pendingUtc, pendingSamples, now); pending = ""; pendingSamples = null; }
                else if (now - pendingAt > JobSeconds)
                { Out.Say("PHYS owner unavailable; reason=" + pending + "; utc=" + pendingUtc); pending = ""; reports++; nextReport = now + ReportInterval; }
                nextAttempt = now + 0.25f;
            }
            if (job == null) return;
            if (job.Advance(now))
            { job.Report(now); job = null; reports++; nextReport = now + ReportInterval; nextPeriodic = now + PeriodicInterval; }
        }
        catch (Exception e) { Fail(e); }
    }
    private static void Fail(Exception e) { Enabled = false; Reset(); Out.Say("PHYS disabled after read failure: " + e.GetType().Name); }
    private static string F(float x) { return x.ToString("F3", CultureInfo.InvariantCulture); }
    private static string V(Vector3 v) { return F(v.x) + "," + F(v.y) + "," + F(v.z); }
    private static bool Physical(Collider c) { return c != null && !c.isTrigger; }

    private sealed class Job
    {
        private readonly SkateParks.PhysicsOwnership owner;
        private readonly Rigidbody body;
        private readonly string reason, utc;
        private readonly Motion[] samples;
        private readonly float start;
        private readonly Stack<Node> stack = new Stack<Node>();
        private readonly List<Collider> native = new List<Collider>(), rider = new List<Collider>();
        private readonly HashSet<int> seen = new HashSet<int>(), captured = new HashSet<int>(), cachedRider = new HashSet<int>();
        private int root = -1, nodes, rootNodes, overflows, foreign, missing, capturedDead, capturedLive, capturedEnabled, observed, enabled, uncaptured, enabledUncaptured, newRider;
        private int pairIndex, pairCount, ignored, notIgnored, uncapturedNotIgnored, pairUnknown;
        private int capturedIndex, cachedRiderIndex;
        private float maximumFrameMs;
        private bool collecting = true, pairFailed, timedOut;
        private string pairFailure = "none";
        internal Job(SkateParks.PhysicsOwnership view, Rigidbody rigidbody, string why, string when, Motion[] context, float now)
        {
            owner = view; body = rigidbody; reason = why; utc = when; samples = context; start = now;
            if (body != null) stack.Push(new Node(body.transform, 0));
        }
        internal bool Advance(float now)
        {
            var tick = Stopwatch.GetTimestamp(); var deadline = tick + Stopwatch.Frequency / 500;
            try
            {
                if (body == null || SkateRig.Body != body || now - start > JobSeconds) { timedOut = true; return true; }
                var budget = NodesPerFrame;
                while (capturedIndex < owner.Captured.Length && budget-- > 0 && Stopwatch.GetTimestamp() < deadline)
                {
                    var c = owner.Captured[capturedIndex++];
                    if (c == null) { capturedDead++; continue; }
                    capturedLive++; captured.Add(c.GetInstanceID()); if (c.enabled && c.gameObject.activeInHierarchy) capturedEnabled++;
                }
                while (cachedRiderIndex < owner.Rider.Length && budget-- > 0 && Stopwatch.GetTimestamp() < deadline)
                { var c = owner.Rider[cachedRiderIndex++]; if (c != null) cachedRider.Add(c.GetInstanceID()); }
                if (capturedIndex < owner.Captured.Length || cachedRiderIndex < owner.Rider.Length) return false;
                while (collecting && budget-- > 0 && Stopwatch.GetTimestamp() < deadline)
                {
                    if (stack.Count == 0)
                    {
                        root++; rootNodes = 0;
                        if (root >= owner.Members.Length) { collecting = false; PreparePairs(); break; }
                        var member = owner.Members[root];
                        if (!owner.Backed[root]) continue;
                        if (member == null) { missing++; continue; }
                        stack.Push(new Node(member.transform, 0));
                    }
                    var node = stack.Pop(); if (node.Transform == null) continue;
                    if (++rootNodes > NodesPerRoot || node.Depth > MaximumDepth) { overflows++; stack.Clear(); continue; }
                    nodes++;
                    if (root >= 0 && node.Transform.GetComponentInParent<BaseEntity>() != owner.Members[root]) { foreign++; continue; }
                    var components = node.Transform.GetComponents<Collider>();
                    if (components.Length > ComponentsPerNode) { overflows++; stack.Clear(); continue; }
                    foreach (var c in components)
                    {
                        if (!Physical(c) || !seen.Add(c.GetInstanceID())) continue;
                        if (root < 0)
                        {
                            if (c.attachedRigidbody != body) { foreign++; continue; }
                            if (rider.Count >= RiderLimit) { overflows++; continue; }
                            rider.Add(c); if (!cachedRider.Contains(c.GetInstanceID())) newRider++;
                        }
                        else
                        {
                            var member = owner.Members[root];
                            if (c.GetComponentInParent<BuildingBlock>() != member || c.GetComponentInParent<BaseEntity>() != member) { foreign++; continue; }
                            if (native.Count >= NativeLimit) { overflows++; continue; }
                            native.Add(c);
                        }
                    }
                    var children = node.Transform.childCount;
                    if (children > ChildrenPerNode) { overflows++; continue; }
                    for (var i = children - 1; i >= 0; i--) stack.Push(new Node(node.Transform.GetChild(i), node.Depth + 1));
                }
                if (!collecting)
                {
                    var budgetPairs = PairsPerFrame;
                    while (pairIndex < pairCount && budgetPairs-- > 0 && Stopwatch.GetTimestamp() < deadline)
                    {
                        var n = native[pairIndex / rider.Count]; var r = rider[pairIndex % rider.Count]; pairIndex++;
                        if (n == null || r == null || !n.enabled || !r.enabled || !n.gameObject.activeInHierarchy || !r.gameObject.activeInHierarchy) { pairUnknown++; continue; }
                        if (pairFailed) { pairUnknown++; continue; }
                        try
                        {
                            // The injected Unity binding takes native Unity object pointers,
                            // not IL2CPP wrapper pointers. Both engine getters are read-only.
                            var np = n.GetCachedPtr(); var rp = r.GetCachedPtr();
                            if (np == IntPtr.Zero || rp == IntPtr.Zero) { pairUnknown++; continue; }
                            if (Physics.GetIgnoreCollision_Injected(np, rp)) ignored++;
                            else { notIgnored++; if (!captured.Contains(n.GetInstanceID())) uncapturedNotIgnored++; }
                        }
                        catch (Exception e) { pairFailed = true; pairFailure = e.GetType().Name; pairUnknown++; }
                    }
                    return pairIndex >= pairCount;
                }
                return false;
            }
            finally { maximumFrameMs = Math.Max(maximumFrameMs, (float)((Stopwatch.GetTimestamp() - tick) * 1000.0 / Stopwatch.Frequency)); }
        }
        private void PreparePairs()
        {
            observed = native.Count;
            for (var i = native.Count - 1; i >= 0; i--)
            {
                var c = native[i]; var active = c != null && c.enabled && c.gameObject.activeInHierarchy;
                if (active) enabled++;
                if (c != null && !captured.Contains(c.GetInstanceID())) { uncaptured++; if (active) enabledUncaptured++; }
                if (!active) native.RemoveAt(i);
            }
            for (var i = rider.Count - 1; i >= 0; i--) if (rider[i] == null || !rider[i].enabled || !rider[i].gameObject.activeInHierarchy) rider.RemoveAt(i);
            pairCount = native.Count * rider.Count;
        }
        internal void Report(float now)
        {
            var current = SkateParks.PhysicsOwnershipCurrent(owner) && body != null && body == SkateRig.Body;
            Out.Say("PHYS snapshot utc=" + utc + " reason=" + reason + " kind=" + SkateParks.KindName(owner.Kind) + " duration=" + F(now - start) +
                " current=" + current + " complete=" + (!timedOut && overflows == 0 && missing == 0 && current && pairIndex == pairCount && pairUnknown == 0 && !pairFailed && !collecting) + " nodes=" + nodes + " overflow=" + overflows + " missing=" + missing + " foreign=" + foreign + " max-frame-ms=" + F(maximumFrameMs));
            Out.Say("PHYS ownership captured=" + owner.Captured.Length + " live=" + capturedLive + " dead=" + capturedDead + " enabled=" + capturedEnabled +
                " current-total=" + observed + " current-enabled=" + enabled + " uncaptured=" + uncaptured + " uncaptured-enabled=" + enabledUncaptured + " rider-active=" + rider.Count + " rider-cached=" + owner.Rider.Length +
                " rider-new=" + newRider + " isolated-records=" + owner.Isolated + " pairs-ignored=" + ignored + " pairs-not-ignored=" + notIgnored + " uncaptured-not-ignored=" + uncapturedNotIgnored +
                " pairs-unknown=" + (pairUnknown + pairCount - pairIndex) + " pair-read=injected/" + pairFailure);
            if (samples == null) return;
            foreach (var m in samples)
                Out.Say("PHYS motion t=" + F(m.At) + " dt=" + F(m.RawDt) + "/" + F(m.Dt) + " mode=" + m.Mode + " source=" + m.Contact +
                    " delta=" + V(m.Delta) + " native=" + V(m.Incoming) + " prior=" + V(m.Previous) + " command=" + V(m.Command) +
                    " tangent-speed=" + F(m.Speed) + " horizontal=" + F(m.Horizontal) + " path=" + F(m.Path) + " actual-y=" + F(m.Vertical) +
                    " ground=" + m.Grounded + " ramp=" + m.Ramp + " held=" + m.Held + " clearance=" + F(m.Clearance) + " ny=" + F(m.Normal) + " ty=" + F(m.Tangent) + " slope=" + F(m.Slope) +
                    " input=" + (m.Pad ? "pad" : "keys") + " push=" + m.Push + " brake=" + m.Brake + " crouch=" + m.Crouch +
                    " pump=" + m.Pump + " charge=" + m.Charge.ToString("F2", CultureInfo.InvariantCulture) + " bottom=" + m.Bottom.ToString("F2", CultureInfo.InvariantCulture) + " saw-ascent=" + m.SawAscent);
        }
    }
}
