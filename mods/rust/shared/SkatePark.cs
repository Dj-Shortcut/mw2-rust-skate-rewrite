using System;
using System.Collections.Generic;

namespace Shortcut.RustMod
{
    // BEGIN SHARED SKATE PARK
    public enum SkateParkKind { Bank, Kicker, QuarterPipe, LedgeLow, LedgeHigh, LongLedge, Funbox, Halfpipe,
        MiniQuarterPipe, LargeQuarterPipe, MegaHalfpipe = 11 }
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
        // Metres above the measured native floor top. Curved lane runs along +Forward.
        public double Radius, Height, Entry, Deck, StartForward, HalfWidth, EndForward;
        public double Skin, RailRadius, RailHeight;
    }

    public static class SkateParkLayout
    {
        public const string FloorPrefab = "assets/prefabs/building core/floor/floor.prefab";
        public const string LowWallPrefab = "assets/prefabs/building core/wall.low/wall.low.prefab";
        public const string HalfWallPrefab = "assets/prefabs/building core/wall.half/wall.half.prefab";
        private const double HalfLength = 1.5;
        private const double Degrees = Math.PI / 180;
        public const int KindCount = 12;
        public const double CellSize = 3;
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
                case SkateParkKind.Bank: case SkateParkKind.Kicker:
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
        { return kind == SkateParkKind.MiniQuarterPipe ? 1 : kind == SkateParkKind.LargeQuarterPipe ? 3 : 0; }

        public static bool TryCell(SkateParkKind kind, int index, out int right, out int forward)
        {
            right = forward = 0;
            if (index < 0 || index >= CellCount(kind)) return false;
            right = index == 1 ? 1 : 0; forward = index == 2 ? 1 : 0;
            return true;
        }

        public static bool TryQuarterProfile(SkateParkKind kind, out SkateParkQuarterProfile profile)
        {
            profile = new SkateParkQuarterProfile();
            if (CellCount(kind) == 0) return false;
            bool mini = kind == SkateParkKind.MiniQuarterPipe;
            profile = new SkateParkQuarterProfile { Radius = mini ? 1.2 : 3, Height = mini ? 1.2 : 3,
                Entry = 0.2, Deck = mini ? 1.4 : 2.6, StartForward = -1.4, HalfWidth = 1.4,
                EndForward = mini ? 1.4 : 4.4, Skin = 0.08, RailRadius = 0.05, RailHeight = 0.8 };
            return true;
        }

        public static bool TryQuarterSurface(SkateParkKind kind, double right, double forward,
            out double height, out double normalUp, out double normalForward)
        {
            height = normalUp = normalForward = 0;
            SkateParkQuarterProfile profile;
            if (!TryQuarterProfile(kind, out profile) || !Finite(right) || !Finite(forward)) return false;
            // The large front-right staging apron meets only the flat entry, not the rising side.
            if (kind == SkateParkKind.LargeQuarterPipe && right > profile.HalfWidth && right <= 4.4 &&
                forward >= -1.4 && forward <= 1.4)
            { normalUp = 1; return true; }
            if (Math.Abs(right) > profile.HalfWidth || forward < profile.StartForward || forward > profile.EndForward)
                return false;
            double run = forward - profile.StartForward - profile.Entry;
            normalUp = 1;
            if (run <= 0) return true;
            if (run >= profile.Radius) { height = profile.Height; return true; }
            double vertical = Math.Sqrt(Math.Max(0, profile.Radius * profile.Radius - run * run));
            height = profile.Radius - vertical;
            normalUp = vertical / profile.Radius; normalForward = -run / profile.Radius;
            return true;
        }

        // Tests every horizontal corner in the part's assigned cell, including rotated wall thickness.
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
                if (Math.Abs(r) > 1.5 + 1e-9 || Math.Abs(f) > 1.5 + 1e-9) return false;
            }
            return true;
        }

        // Strict overlap of yaw-only boxes; touching cell edges are allowed. Inputs are world metres.
        public static bool OverlapsBox(double x, double z, double yaw, double halfRight, double halfForward,
            double otherX, double otherZ, double otherYaw, double otherHalfRight, double otherHalfForward)
        {
            if (!Coordinate(x) || !Coordinate(z) || !Angle(yaw) || !Coordinate(otherX) || !Coordinate(otherZ) ||
                !Angle(otherYaw) || !Finite(halfRight) || !Finite(halfForward) || !Finite(otherHalfRight) ||
                !Finite(otherHalfForward) || halfRight <= 0 || halfForward <= 0 || otherHalfRight <= 0 || otherHalfForward <= 0)
                return true;
            double a = yaw * Degrees, b = otherYaw * Degrees;
            double ax = Math.Cos(a), az = -Math.Sin(a), bx = Math.Cos(b), bz = -Math.Sin(b);
            double dx = otherX - x, dz = otherZ - z;
            double c = Math.Abs(ax * bx + az * bz), s = Math.Abs(ax * -bz + az * bx);
            return Math.Abs(dx * ax + dz * az) < halfRight + otherHalfRight * c + otherHalfForward * s - 1e-7 &&
                Math.Abs(dx * -az + dz * ax) < halfForward + otherHalfRight * s + otherHalfForward * c - 1e-7 &&
                Math.Abs(dx * bx + dz * bz) < otherHalfRight + halfRight * c + halfForward * s - 1e-7 &&
                Math.Abs(dx * -bz + dz * bx) < otherHalfForward + halfRight * s + halfForward * c - 1e-7;
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
                case SkateParkKind.Bank: case SkateParkKind.Kicker: case SkateParkKind.QuarterPipe:
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
