// The shared edge module only judges the approach. Whether the rider is actually at the edge is
// checked here, and a captured rider is never placed on the edge: the ride steers the body there.
using System;
using Shortcut.RustMod;
using UnityEngine;

public static class SkateGrind
{
    public const int Rows = 9, Cols = 11;
    public const float RowStep = 0.2f, ColStep = 0.1f, Inset = 0.1f, Reach = 0.45f;
    // The capture rule of the client: the way of travel within AlongCos of the edge (60 degrees), at
    // least AlongSpeed along it, the feet from a little below the edge's top to FeetAbove above it.
    // The board's own angle to the edge is free: it says what kind of grind it is. The shared
    // module's rule, which also wants the board's nose along the edge, would never let a slide on.
    public const float AlongCos = 0.5f, AlongSpeed = 1.0f, FeetBelow = 0.15f, FeetAbove = 0.5f;
    public static bool Enabled = true;
    public static float Heading, Speed;
    public static Vector3 Target;
    public static int Scans, Finds, Captures;
    public static string Last = "";
    private static readonly double[] heights = new double[Rows * Cols];
    private static SkateEdge edge;
    private static double cooldown;
    public const float FirstRetry = (float)SkateScanRecovery.FirstRetry, LongestRetry = (float)SkateScanRecovery.LongestRetry;
    public static bool Unavailable { get { return scan.Unavailable; } }
    public static int Failures { get { return scan.Failures; } }
    private static SkateScanState scan;
    private static void Fail(Exception e, string where)
    {
        while (e is System.Reflection.TargetInvocationException && e.InnerException != null) e = e.InnerException;
        var name = e.GetType().Name;
        // The caller needs Heading and Speed to leave the grind with its existing momentum.
        edge = stallEdge = default(SkateEdge); Target = default(Vector3); StallCandidate = StallSquare = false; NearAt = nearSaidAt = -99f;
        string error;
        bool wasUnavailable = Unavailable;
        SkateScanRecovery.TryFail(scan, e, out scan, out error);
        if (Unavailable)
        {
            if (!wasUnavailable) Out.Say("GRIND " + where + " threw " + name + ": " + e.Message + " - the native binding is unavailable; grinding stays off");
            Last = "scanner unavailable"; return;
        }
        var wait = scan.RetryRemaining;
        Last = "scanner retry in " + wait.ToString("F1") + " s";
        Out.Say("GRIND " + where + " threw " + name + ": " + e.Message + " - tried again in " + wait.ToString("F1") + " s");
    }
    private static float nearSaidAt = -99f;
    // An edge met too square or too slowly for a grind is a stall: the board stops on it.
    public static bool StallCandidate, StallSquare;
    // When an edge was last found under the way the board falls, taken or not.
    public static float NearAt = -99f;
    private static SkateEdge stallEdge;

    public static void Reset()
    {
        cooldown = 0; edge = stallEdge = default(SkateEdge); Speed = Heading = 0f; Target = default(Vector3);
        StallCandidate = StallSquare = false; NearAt = nearSaidAt = -99f;
        scan = SkateScanRecovery.Reset(scan); Last = Unavailable ? "scanner unavailable" : "";
    }

    private static float YawOf(double x, double z) { return (float)(Math.Atan2(x, z) * 180.0 / Math.PI); }

    // The float boundary rounds above the shared double limit.
    private static double StepSeconds(float dt)
    { return dt == (float)SkateMotion.MaximumStep ? SkateMotion.MaximumStep : dt; }

    private static SkateHeightGrid Sample(Vector3 pos, float yaw)
    {
        var rad = yaw * Math.PI / 180.0;
        double fx = Math.Sin(rad), fz = Math.Cos(rad), rx = fz, rz = -fx;
        double ox = pos.x - fx * ((Rows - 1) * 0.5 * RowStep) - rx * ((Cols - 1) * 0.5 * ColStep);
        double oz = pos.z - fz * ((Rows - 1) * 0.5 * RowStep) - rz * ((Cols - 1) * 0.5 * ColStep);
        var top = pos.y + 0.7f;
        RaycastHit hit;
        for (var row = 0; row < Rows; row++)
            for (var col = 0; col < Cols; col++)
            {
                var from = new Vector3((float)(ox + fx * row * RowStep + rx * col * ColStep), top, (float)(oz + fz * row * RowStep + rz * col * ColStep));
                heights[row * Cols + col] = SkateParks.Ray(from, Vector3.down, out hit, 2.3f, SkateRide.GroundMask) ? hit.m_Point.y : double.NaN;
            }
        Scans++;
        return new SkateHeightGrid(new SkateVector(ox, pos.y, oz), new SkateVector(fx, 0, fz), new SkateVector(rx, 0, rz), RowStep, ColStep, Rows, Cols, heights);
    }

    private static float Aim(Vector3 pos)
    {
        double dx = pos.x - edge.Point.X, dz = pos.z - edge.Point.Z;
        var along = dx * edge.Direction.X + dz * edge.Direction.Z;
        if (along > edge.Ahead) along = edge.Ahead;
        if (along < -edge.Behind) along = -edge.Behind;
        double px = edge.Point.X + edge.Direction.X * along, pz = edge.Point.Z + edge.Direction.Z * along;
        Target = new Vector3((float)(px + edge.HighSide.X * Inset), (float)edge.TopHeight, (float)(pz + edge.HighSide.Z * Inset));
        double lx = pos.x - px, lz = pos.z - pz;
        return (float)Math.Sqrt(lx * lx + lz * lz);
    }

    public static void Tick(float dt)
    {
        string error;
        double step = StepSeconds(dt);
        SkateScanRecovery.TryTick(scan, step, out scan, out error);
        if (cooldown > 0) SkateEdges.TryCooldown(cooldown, false, step, out cooldown, out error);
    }

    public static void Release(float dt)
    {
        string error;
        if (!SkateEdges.TryCooldown(cooldown, true, StepSeconds(dt), out cooldown, out error)) cooldown = SkateMotion.RailRecaptureDelay;
    }

    public static bool TryCapture(Vector3 pos, Vector3 velocity, float noseYaw, float dt)
    {
        StallCandidate = false;
        if (!Enabled || !scan.CanScan || cooldown > 0) return false;
        try
        {
            double vx = velocity.x, vz = velocity.z;
            if (vx * vx + vz * vz < AlongSpeed * AlongSpeed) return false;
            var grid = Sample(pos, YawOf(vx, vz));
            scan = SkateScanRecovery.Succeeded(scan);
            SkateEdge found; string error;
            if (!SkateEdges.TryFind(grid, new SkateVector(vx, 0, vz), out found, out error)) { Last = "find refused: " + error; return false; }
            if (!found.Found) return false;
            Finds++; NearAt = Time.realtimeSinceStartup;
            var previous = edge; edge = found;
            var off = Aim(pos);
            var feet = pos.y - (float)found.TopHeight;
            var flat = Math.Sqrt(vx * vx + vz * vz);
            var along = vx * found.Direction.X + vz * found.Direction.Z;
            var cos = flat > 0.001 ? Math.Abs(along) / flat : 0;
            var why = off > Reach ? "too far to the side" : feet < -FeetBelow ? "feet below the edge" : feet > FeetAbove ? "feet too high" : found.Ahead < 0.3 ? "edge too short ahead"
                : velocity.y >= 0f ? "still rising" : cos < AlongCos ? "crossing the edge too square" : Math.Abs(along) < AlongSpeed ? "too slow along it" : "";
            if (why == "crossing the edge too square" || why == "too slow along it")
            { StallCandidate = true; StallSquare = why == "crossing the edge too square"; stallEdge = found; }
            if (why != "")
            {
                Last = "edge near, not taken (" + why + "): off=" + off.ToString("F2") + " feet=" + feet.ToString("F2") + " ahead=" + found.Ahead.ToString("F2") + " along=" + along.ToString("F1") + " cos=" + cos.ToString("F2");
                if (Time.realtimeSinceStartup - nearSaidAt > 0.5f) { nearSaidAt = Time.realtimeSinceStartup; Out.Say("GRIND " + Last); }
                edge = previous; return false;
            }
            var speed = Math.Sign(along) * Math.Min(SkateMotion.MaximumGroundSpeed, Math.Abs(along));
            Speed = (float)speed; Heading = YawOf(found.Direction.X, found.Direction.Z); Captures++;
            Last = "captured top=" + found.TopHeight.ToString("F2") + " speed=" + Speed.ToString("F1") + " ahead=" + found.Ahead.ToString("F2") + " heading=" + Heading.ToString("F0");
            Out.Say("GRIND " + Last);
            return true;
        }
        catch (Exception e) { Fail(e, "scan"); return false; }
    }

    // The board stops on the edge last met too square or too slowly; HoldStall keeps it there.
    public static void BeginStall(Vector3 pos)
    {
        edge = stallEdge; Speed = 0f; Heading = YawOf(edge.Direction.X, edge.Direction.Z); Captures++;
        var off = Aim(pos);
        Last = "stall top=" + edge.TopHeight.ToString("F2") + " off=" + off.ToString("F2") + (StallSquare ? " square" : " along");
        Out.Say("GRIND " + Last);
    }

    public static void HoldStall(Vector3 pos) { Aim(pos); }

    public static bool TryContinue(Vector3 pos, float dt, out bool ended)
    {
        ended = false;
        if (!Enabled || !scan.CanScan) { ended = true; return false; }
        try
        {
            var grid = Sample(pos, Heading);
            scan = SkateScanRecovery.Succeeded(scan);
            bool continues; SkateEdge next; string error;
            if (!SkateEdges.TryContinue(edge, grid, edge.Direction, out continues, out next, out error)) { Last = "continue refused: " + error; return false; }
            if (!continues) { Last = "edge ended"; ended = true; return true; }
            if (next.Found) edge = next;
            double speed; bool grinding;
            if (!SkateEdges.TryGrindStep(Speed, StepSeconds(dt), out speed, out grinding, out error)) { Last = "grind step refused: " + error; return false; }
            Speed = (float)speed;
            if (!grinding) { Last = "too slow"; ended = true; return true; }
            Heading = YawOf(edge.Direction.X, edge.Direction.Z);
            var off = Aim(pos);
            if (off > 0.8f || Math.Abs(pos.y - (float)edge.TopHeight) > 0.6f) { Last = "left the edge: off=" + off.ToString("F2"); ended = true; }
            return true;
        }
        catch (Exception e) { Fail(e, "step"); return false; }
    }
}
