using System;

namespace Shortcut.RustMod
{
    public struct SkateScanState
    {
        public readonly bool Unavailable;
        public readonly int Failures;
        public readonly double RetryRemaining;
        public bool CanScan { get { return !Unavailable && RetryRemaining == 0; } }

        internal SkateScanState(bool unavailable, int failures, double retryRemaining)
        { Unavailable = unavailable; Failures = failures; RetryRemaining = retryRemaining; }
    }

    public static class SkateScanRecovery
    {
        public const double FirstRetry = 0.5;
        public const double LongestRetry = 10;

        public static SkateScanState Reset(SkateScanState state)
        { return new SkateScanState(state.Unavailable, 0, 0); }

        public static SkateScanState Succeeded(SkateScanState state)
        { return Reset(state); }

        public static bool TryTick(SkateScanState state, double dt, out SkateScanState next, out string error)
        {
            next = state;
            error = null;
            if (double.IsNaN(dt) || double.IsInfinity(dt) || dt <= 0 || dt > SkateMotion.MaximumStep)
            { error = "Invalid scan recovery step."; return false; }
            next = new SkateScanState(state.Unavailable, state.Failures, Math.Max(0, state.RetryRemaining - dt));
            return true;
        }

        public static bool TryFail(SkateScanState state, Exception failure, out SkateScanState next, out string error)
        {
            next = state;
            error = null;
            if (failure == null) { error = "Missing scan failure."; return false; }
            if (state.Unavailable) return true;
            while ((failure is System.Reflection.TargetInvocationException || failure is TypeInitializationException) &&
                   failure.InnerException != null) failure = failure.InnerException;
            bool unavailable = failure is MissingMemberException || failure is TypeLoadException ||
                failure is EntryPointNotFoundException || failure is DllNotFoundException || failure is InvalidProgramException;
            int failures = state.Failures < int.MaxValue ? state.Failures + 1 : int.MaxValue;
            double wait = unavailable ? 0 : Math.Min(LongestRetry, FirstRetry * Math.Pow(2, Math.Min(5, failures - 1)));
            next = new SkateScanState(unavailable, failures, wait);
            return true;
        }
    }

    public struct SkateHeightGrid
    {
        public readonly SkateVector Origin;
        public readonly SkateVector Forward;
        public readonly SkateVector Right;
        public readonly double ForwardSpacing;
        public readonly double RightSpacing;
        public readonly int ForwardCount;
        public readonly int RightCount;
        public readonly double[] Heights;
        public SkateHeightGrid(SkateVector origin, SkateVector forward, SkateVector right,
                               double forwardSpacing, double rightSpacing, int forwardCount, int rightCount,
                               double[] heights)
        { Origin = origin; Forward = forward; Right = right; ForwardSpacing = forwardSpacing;
          RightSpacing = rightSpacing; ForwardCount = forwardCount; RightCount = rightCount; Heights = heights; }
    }

    public struct SkateEdge
    {
        public readonly bool Found;
        public readonly SkateVector Point;
        public readonly SkateVector Direction;
        public readonly SkateVector HighSide;
        public readonly double TopHeight;
        public readonly double Ahead;
        public readonly double Behind;
        public readonly double Resolution;
        internal SkateEdge(SkateVector point, SkateVector direction, SkateVector highSide,
                           double topHeight, double ahead, double behind, double resolution)
        { Found = true; Point = point; Direction = direction; HighSide = highSide;
          TopHeight = topHeight; Ahead = ahead; Behind = behind; Resolution = resolution; }
        public bool HighOnRight { get { return SkateVector.Dot(HighSide, new SkateVector(Direction.Z, 0, -Direction.X)) > 0; } }
    }

    public static class SkateEdges
    {
        public const double DefaultMinimumDrop = 0.2;
        public const double HeightTolerance = 0.08;
        public const int MaximumCount = 65;

        public static bool TryFind(SkateHeightGrid grid, SkateVector travel, out SkateEdge edge, out string error)
        { return TryFind(grid, travel, DefaultMinimumDrop, out edge, out error); }

        public static bool TryFind(SkateHeightGrid grid, SkateVector travel, double minimumDrop,
                                   out SkateEdge edge, out string error)
        {
            edge = default(SkateEdge);
            error = null;
            if (!Valid(grid, travel, minimumDrop)) { error = "Invalid edge samples."; return false; }
            Find(grid, Unit(travel), minimumDrop, false, default(SkateEdge), out edge);
            return true;
        }

        public static bool TryContinue(SkateEdge previous, SkateHeightGrid grid, SkateVector travel,
                                       out bool continues, out SkateEdge edge, out string error)
        { return TryContinue(previous, grid, travel, DefaultMinimumDrop, out continues, out edge, out error); }

        public static bool TryContinue(SkateEdge previous, SkateHeightGrid grid, SkateVector travel,
                                       double minimumDrop, out bool continues, out SkateEdge edge, out string error)
        {
            continues = false;
            edge = default(SkateEdge);
            error = null;
            if (!Valid(previous) || !Valid(grid, travel, minimumDrop))
            { error = "Invalid edge continuation."; return false; }
            Find(grid, Unit(travel), minimumDrop, true, previous, out edge);
            if (edge.Found)
            {
                var delta = Centre(grid) - edge.Point;
                double progress = SkateVector.Dot(delta, edge.Direction);
                continues = progress >= -edge.Behind && progress <= edge.Ahead && edge.Ahead > 0;
            }
            return true;
        }

        public static bool TryCapture(SkateEdge edge, SkateVector velocity, SkateVector boardForward,
                                      double recaptureSeconds, out bool allowed, out double speed, out string error)
        {
            allowed = false;
            speed = 0;
            error = null;
            if (!Valid(edge) || !Coordinate(velocity) || Math.Abs(velocity.Y) > SkateMotion.MaximumFallSpeed ||
                !HorizontalUnit(boardForward) || !Cooldown(recaptureSeconds))
            { error = "Invalid edge capture request."; return false; }
            var horizontal = new SkateVector(velocity.X, 0, velocity.Z);
            double horizontalSpeed = horizontal.Length;
            if (horizontalSpeed > SkateMotion.MaximumGroundSpeed + 0.000001)
            { error = "Invalid edge capture request."; return false; }
            double along = SkateVector.Dot(horizontal, edge.Direction);
            if (recaptureSeconds > 0 || velocity.Y >= 0 || -velocity.Y > SkateMotion.MaximumLandingFallSpeed ||
                Math.Abs(along) < SkateMotion.MinimumCaptureSpeed ||
                Math.Abs(along) < horizontalSpeed * SkateMotion.RailAlignment ||
                Math.Abs(SkateVector.Dot(boardForward, edge.Direction)) < SkateMotion.RailAlignment) return true;
            allowed = true;
            speed = Math.Sign(along) * Math.Min(SkateMotion.MaximumGroundSpeed, Math.Abs(along));
            return true;
        }

        public static bool TryGrindStep(double speed, double dt, out double nextSpeed,
                                       out bool grinding, out string error)
        {
            nextSpeed = speed;
            grinding = false;
            error = null;
            if (!Finite(speed) || Math.Abs(speed) > SkateMotion.MaximumGroundSpeed || !Step(dt))
            { error = "Invalid edge grind step."; return false; }
            double magnitude = Math.Max(0, Math.Abs(speed) - SkateMotion.RailDrag * dt);
            nextSpeed = Math.Sign(speed) * magnitude;
            grinding = magnitude >= SkateMotion.MinimumGrindingSpeed;
            return true;
        }

        public static bool TryCooldown(double remaining, bool released, double dt,
                                       out double next, out string error)
        {
            next = remaining;
            error = null;
            if (!Cooldown(remaining) || !Step(dt))
            { error = "Invalid edge cooldown step."; return false; }
            next = released ? SkateMotion.RailRecaptureDelay : Math.Max(0, remaining - dt);
            return true;
        }

        private static void Find(SkateHeightGrid grid, SkateVector travel, double minimumDrop,
                                 bool following, SkateEdge previous, out SkateEdge best)
        {
            best = default(SkateEdge);
            double bestDistance = double.MaxValue;
            for (int axis = 0; axis < 2; axis++)
            {
                int rows = axis == 0 ? grid.ForwardCount : grid.RightCount;
                int columns = axis == 0 ? grid.RightCount : grid.ForwardCount;
                double du = axis == 0 ? grid.ForwardSpacing : grid.RightSpacing;
                double dv = axis == 0 ? grid.RightSpacing : grid.ForwardSpacing;
                for (int row = 0; row < rows - 2; row++)
                    for (int column = 1; column < columns - 2; column++)
                    {
                        int side;
                        double top;
                        if (!Transition(grid, axis, row, column, minimumDrop, out side, out top)) continue;
                        for (int span = 2; row + span < rows; span *= 2)
                        for (int next = Math.Max(1, column - (int)Math.Ceiling(span * du / dv) - 1);
                            next <= Math.Min(columns - 3, column + (int)Math.Ceiling(span * du / dv) + 1); next++)
                        {
                            int secondSide;
                            double secondTop;
                            if (!Transition(grid, axis, row + span, next, minimumDrop, out secondSide, out secondTop) ||
                                side != secondSide || Math.Abs(secondTop - top) > HeightTolerance) continue;
                            double slope = (next - column) * dv / (span * du);
                            double offset = V(column, columns, dv) - slope * U(row, rows, du);
                            SkateEdge candidate;
                            if (!Track(grid, axis, row, row + span, minimumDrop, side, top, slope, offset, out candidate)) continue;
                            if (SkateVector.Dot(candidate.Direction, travel) < 0)
                                candidate = new SkateEdge(candidate.Point, candidate.Direction * -1, candidate.HighSide,
                                    candidate.TopHeight, candidate.Behind, candidate.Ahead, candidate.Resolution);
                            if (following && !Same(previous, candidate)) continue;
                            var delta = candidate.Point - Centre(grid);
                            double distance = delta.X * delta.X + delta.Z * delta.Z;
                            double length = candidate.Ahead + candidate.Behind;
                            double bestLength = best.Ahead + best.Behind;
                            if (length > bestLength + 0.000001 ||
                                (Math.Abs(length - bestLength) <= 0.000001 && distance < bestDistance))
                            { bestDistance = distance; best = candidate; }
                        }
                    }
            }
        }

        private static bool Track(SkateHeightGrid grid, int axis, int seed, int seedEnd, double minimumDrop, int side,
                                  double top, double slope, double offset, out SkateEdge edge)
        {
            edge = default(SkateEdge);
            int rows = axis == 0 ? grid.ForwardCount : grid.RightCount;
            int columns = axis == 0 ? grid.RightCount : grid.ForwardCount;
            double du = axis == 0 ? grid.ForwardSpacing : grid.RightSpacing;
            double dv = axis == 0 ? grid.RightSpacing : grid.ForwardSpacing;
            double sumU = 0, sumV = 0, sumUU = 0, sumUV = 0, sumTop = 0;
            int first = seed, last = seed, count = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                first = seed;
                last = seed;
                sumU = 0; sumV = 0; sumUU = 0; sumUV = 0; sumTop = 0; count = 0;
                for (int row = seed; row >= 0; row--)
                {
                    double v, height;
                    if (!Match(grid, axis, row, minimumDrop, side, top, slope, offset, out v, out height)) break;
                    first = row;
                }
                for (int row = first; row < rows; row++)
                {
                    double v, height;
                    if (!Match(grid, axis, row, minimumDrop, side, top, slope, offset, out v, out height)) break;
                    double u = U(row, rows, du);
                    sumU += u; sumV += v; sumUU += u * u; sumUV += u * v; sumTop += height;
                    last = row; count++;
                }
                if (count < 3 || seedEnd > last) return false;
                double denominator = count * sumUU - sumU * sumU;
                if (denominator <= 0) return false;
                slope = (count * sumUV - sumU * sumV) / denominator;
                offset = (sumV - slope * sumU) / count;
                top = sumTop / count;
            }
            var along = axis == 0 ? grid.Forward : grid.Right;
            var across = axis == 0 ? grid.Right : grid.Forward;
            var direction = Unit(along + across * slope);
            var highSide = Unit(across - direction * SkateVector.Dot(across, direction)) * side;
            double uPoint = Math.Max(U(first, rows, du), Math.Min(U(last, rows, du), -slope * offset / (1 + slope * slope)));
            double vPoint = slope * uPoint + offset;
            var horizontal = Centre(grid) + along * uPoint + across * vPoint;
            var point = new SkateVector(horizontal.X, top, horizontal.Z);
            double scale = Math.Sqrt(1 + slope * slope);
            edge = new SkateEdge(point, direction, highSide, top,
                (U(last, rows, du) - uPoint) * scale, (uPoint - U(first, rows, du)) * scale, Math.Sqrt(du * du + dv * dv));
            return true;
        }

        private static bool Match(SkateHeightGrid grid, int axis, int row, double minimumDrop, int side,
                                  double top, double slope, double offset, out double v, out double height)
        {
            v = 0; height = 0;
            int rows = axis == 0 ? grid.ForwardCount : grid.RightCount;
            int columns = axis == 0 ? grid.RightCount : grid.ForwardCount;
            double du = axis == 0 ? grid.ForwardSpacing : grid.RightSpacing;
            double dv = axis == 0 ? grid.RightSpacing : grid.ForwardSpacing;
            double predicted = slope * U(row, rows, du) + offset;
            int column = (int)Math.Floor(predicted / dv + (columns - 1) * 0.5);
            double nearest = dv * 0.8;
            bool found = false;
            for (int j = Math.Max(1, column - 1); j <= Math.Min(columns - 3, column + 1); j++)
            {
                int actualSide;
                double actualTop;
                if (!Transition(grid, axis, row, j, minimumDrop, out actualSide, out actualTop) ||
                    actualSide != side || Math.Abs(actualTop - top) > HeightTolerance) continue;
                double candidate = V(j, columns, dv);
                double distance = Math.Abs(candidate - predicted);
                if (distance > nearest) continue;
                nearest = distance; v = candidate; height = actualTop; found = true;
            }
            return found;
        }

        private static bool Transition(SkateHeightGrid grid, int axis, int row, int column,
                                       double minimumDrop, out int side, out double top)
        {
            side = 0; top = 0;
            double a = Height(grid, axis, row, column - 1), b = Height(grid, axis, row, column);
            double c = Height(grid, axis, row, column + 1), d = Height(grid, axis, row, column + 2);
            if (!Finite(a) || !Finite(b) || !Finite(c) || !Finite(d)) return false;
            double leftSlope = b - a, rightSlope = d - c;
            double drop = b - c + (leftSlope + rightSlope) * 0.5;
            if (Math.Abs(drop) < minimumDrop ||
                Math.Abs(leftSlope) > Math.Abs(drop) * 0.25 + 0.04 ||
                Math.Abs(rightSlope) > Math.Abs(drop) * 0.25 + 0.04) return false;
            side = drop > 0 ? -1 : 1;
            top = side < 0 ? (a + b) * 0.5 : (c + d) * 0.5;
            return true;
        }

        private static double Height(SkateHeightGrid grid, int axis, int row, int column)
        { return axis == 0 ? grid.Heights[row * grid.RightCount + column] : grid.Heights[column * grid.RightCount + row]; }
        private static double U(int row, int rows, double spacing) { return (row - (rows - 1) * 0.5) * spacing; }
        private static double V(int column, int columns, double spacing) { return (column + 0.5 - (columns - 1) * 0.5) * spacing; }
        private static SkateVector Centre(SkateHeightGrid grid)
        { return grid.Origin + grid.Forward * ((grid.ForwardCount - 1) * grid.ForwardSpacing * 0.5) +
            grid.Right * ((grid.RightCount - 1) * grid.RightSpacing * 0.5); }
        private static bool Same(SkateEdge a, SkateEdge b)
        {
            var delta = b.Point - a.Point;
            var perpendicular = delta - a.Direction * SkateVector.Dot(delta, a.Direction);
            double first = SkateVector.Dot(b.Point - b.Direction * b.Behind - a.Point, a.Direction);
            double last = SkateVector.Dot(b.Point + b.Direction * b.Ahead - a.Point, a.Direction);
            double overlapTolerance = Math.Max(a.Resolution, b.Resolution) * 0.5;
            return Math.Abs(SkateVector.Dot(a.Direction, b.Direction)) >= 0.98 &&
                SkateVector.Dot(a.HighSide, b.HighSide) >= 0.98 &&
                Math.Max(first, last) >= -a.Behind - overlapTolerance &&
                Math.Min(first, last) <= a.Ahead + overlapTolerance &&
                perpendicular.X * perpendicular.X + perpendicular.Z * perpendicular.Z <=
                    Math.Max(a.Resolution, b.Resolution) * Math.Max(a.Resolution, b.Resolution) &&
                Math.Abs(a.TopHeight - b.TopHeight) <= HeightTolerance;
        }
        private static bool Valid(SkateHeightGrid grid, SkateVector travel, double minimumDrop)
        {
            if (!Coordinate(grid.Origin) || !HorizontalUnit(grid.Forward) || !HorizontalUnit(grid.Right) ||
                Math.Abs(SkateVector.Dot(grid.Forward, grid.Right)) > 0.000001 ||
                !Finite(grid.ForwardSpacing) || grid.ForwardSpacing < 0.01 || grid.ForwardSpacing > 10 ||
                !Finite(grid.RightSpacing) || grid.RightSpacing < 0.01 || grid.RightSpacing > 10 ||
                grid.ForwardCount < 3 || grid.ForwardCount > MaximumCount || grid.RightCount < 3 || grid.RightCount > MaximumCount ||
                grid.Heights == null || grid.Heights.Length != grid.ForwardCount * grid.RightCount ||
                !Coordinate(travel) || Math.Abs(travel.Y) > 0.000001 || travel.LengthSquared <= 0.000001 ||
                !Finite(minimumDrop) || minimumDrop <= 0 || minimumDrop > 100000 ||
                !Coordinate(grid.Origin + grid.Forward * ((grid.ForwardCount - 1) * grid.ForwardSpacing)) ||
                !Coordinate(grid.Origin + grid.Right * ((grid.RightCount - 1) * grid.RightSpacing)) ||
                !Coordinate(grid.Origin + grid.Forward * ((grid.ForwardCount - 1) * grid.ForwardSpacing) +
                    grid.Right * ((grid.RightCount - 1) * grid.RightSpacing))) return false;
            for (int i = 0; i < grid.Heights.Length; i++)
                if (!double.IsNaN(grid.Heights[i]) && (!Finite(grid.Heights[i]) || Math.Abs(grid.Heights[i]) > 100000)) return false;
            return true;
        }
        private static bool Valid(SkateEdge edge)
        { return edge.Found && Coordinate(edge.Point) && HorizontalUnit(edge.Direction) && HorizontalUnit(edge.HighSide) &&
            Math.Abs(SkateVector.Dot(edge.Direction, edge.HighSide)) < 0.000001 && Finite(edge.TopHeight) &&
            edge.Point.Y == edge.TopHeight && Finite(edge.Ahead) && edge.Ahead >= 0 && Finite(edge.Behind) && edge.Behind >= 0 &&
            Finite(edge.Resolution) && edge.Resolution > 0; }
        private static bool HorizontalUnit(SkateVector value)
        { return Coordinate(value) && Math.Abs(value.Y) < 0.000001 && Math.Abs(value.LengthSquared - 1) < 0.000001; }
        private static bool Coordinate(SkateVector value)
        { return Finite(value.X) && Finite(value.Y) && Finite(value.Z) && Math.Abs(value.X) <= 100000 && Math.Abs(value.Y) <= 100000 && Math.Abs(value.Z) <= 100000; }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static bool Step(double value) { return Finite(value) && value > 0 && value <= SkateMotion.MaximumStep; }
        private static bool Cooldown(double value) { return Finite(value) && value >= 0 && value <= SkateMotion.RailRecaptureDelay; }
        private static SkateVector Unit(SkateVector value) { return value * (1 / value.Length); }
    }
}
