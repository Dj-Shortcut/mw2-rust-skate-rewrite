using System;
using System.Collections.Generic;

namespace Shortcut.RustMod
{
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

        public static bool TryGhostFrame(SkateParkKind kind, bool adjacent, double hostX, double hostZ,
            double hostYaw, double hitX, double hitZ, double requestedYaw, bool relativeYaw, double step,
            out SkateParkTransform frame)
        {
            frame = new SkateParkTransform();
            double x = hostX, z = hostZ, yaw;
            if (adjacent && !TryAdjacentCell(hostX, hostZ, hostYaw, hitX, hitZ, out x, out z)) return false;
            if (!TrySnapYaw(relativeYaw ? hostYaw + requestedYaw : requestedYaw, hostYaw, step, out yaw) ||
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
}
