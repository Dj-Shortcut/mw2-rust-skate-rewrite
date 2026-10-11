using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using Shortcut.RustMod;

internal static class SkateParkLayoutScenarios
{
    private const double Tolerance = 1e-10;
    private const string FloorPrefab = "assets/prefabs/building core/floor/floor.prefab";
    private const string LowWallPrefab = "assets/prefabs/building core/wall.low/wall.low.prefab";
    private const string HalfWallPrefab = "assets/prefabs/building core/wall.half/wall.half.prefab";
    private static int checks;

    private struct ExpectedPart
    {
        public readonly double Right, Forward, Height, Pitch, Yaw;
        public readonly string Prefab;
        public readonly SkateParkMaterial Material;
        public ExpectedPart(double right, double forward, double height, double pitch,
                            SkateParkMaterial material = SkateParkMaterial.Stone, string prefab = FloorPrefab, double yaw = 0)
        { Right = right; Forward = forward; Height = height; Pitch = pitch; Material = material; Prefab = prefab; Yaw = yaw; }
    }

    private sealed class Piece
    {
        public readonly SkateParkKind Kind;
        public readonly string Name;
        public readonly ExpectedPart[] Parts;
        public Piece(SkateParkKind kind, string name, params ExpectedPart[] parts)
        { Kind = kind; Name = name; Parts = parts; }
    }

    private struct Point
    {
        public double X, Y, Z;
        public Point(double x, double y, double z) { X = x; Y = y; Z = z; }
    }

    private struct Angle
    {
        public readonly double Input, Normalized, Cos, Sin;
        public Angle(double input, double normalized, double cos, double sin)
        { Input = input; Normalized = normalized; Cos = cos; Sin = sin; }
    }

    private static readonly Piece[] Pieces =
    {
        new Piece(SkateParkKind.Bank, "bank", new ExpectedPart(0, 0, 0.311867536226639, -12)),
        new Piece(SkateParkKind.Kicker, "kicker", new ExpectedPart(0, 0, 0.6339273926110491, -25)),
        new Piece(SkateParkKind.QuarterPipe, "quarterpipe",
            new ExpectedPart(0, 0, 0.3882285676537811, -15),
            new ExpectedPart(0, 2.67761680586709, 1.6368217898341313, -35)),
        new Piece(SkateParkKind.LedgeLow, "ledge-low", new ExpectedPart(0, 0, 0.4, 0)),
        new Piece(SkateParkKind.LedgeHigh, "ledge-high", new ExpectedPart(0, 0, 0.8, 0)),
        new Piece(SkateParkKind.LongLedge, "long-ledge",
            new ExpectedPart(0, 0, 0.4, 0), new ExpectedPart(-3, 0, 0.4, 0), new ExpectedPart(3, 0, 0.4, 0)),
        new Piece(SkateParkKind.Funbox, "funbox",
            new ExpectedPart(0, 0, 0.7764571353075622, 0),
            new ExpectedPart(0, -2.9488887394336025, 0.3882285676537811, -15),
            new ExpectedPart(0, 2.9488887394336025, 0.3882285676537811, 15)),
        new Piece(SkateParkKind.Halfpipe, "halfpipe",
            new ExpectedPart(0, 0, 0, 0), new ExpectedPart(3, 0, 0, 0),
            new ExpectedPart(0, 3, 0, 0), new ExpectedPart(3, 3, 0, 0),
            new ExpectedPart(0, -2.9488887394336025, 0.3882285676537811, 15, SkateParkMaterial.Metal),
            new ExpectedPart(3, -2.9488887394336025, 0.3882285676537811, 15, SkateParkMaterial.Metal),
            new ExpectedPart(0, -5.626505545300693, 1.6368217898341313, 35, SkateParkMaterial.Metal),
            new ExpectedPart(3, -5.626505545300693, 1.6368217898341313, 35, SkateParkMaterial.Metal),
            new ExpectedPart(0, 5.9488887394336025, 0.3882285676537811, -15, SkateParkMaterial.Metal),
            new ExpectedPart(3, 5.9488887394336025, 0.3882285676537811, -15, SkateParkMaterial.Metal),
            new ExpectedPart(0, 8.626505545300694, 1.6368217898341313, -35, SkateParkMaterial.Metal),
            new ExpectedPart(3, 8.626505545300694, 1.6368217898341313, -35, SkateParkMaterial.Metal),
            new ExpectedPart(0, -8.35523361173418, 2.4971864443607004, 0, SkateParkMaterial.Metal),
            new ExpectedPart(3, -8.35523361173418, 2.4971864443607004, 0, SkateParkMaterial.Metal),
            new ExpectedPart(0, 11.35523361173418, 2.4971864443607004, 0, SkateParkMaterial.Metal),
            new ExpectedPart(3, 11.35523361173418, 2.4971864443607004, 0, SkateParkMaterial.Metal),
            new ExpectedPart(0, -9.85523361173418, 2.4971864443607004, 0, SkateParkMaterial.Wood, LowWallPrefab, 180),
            new ExpectedPart(3, -9.85523361173418, 2.4971864443607004, 0, SkateParkMaterial.Wood, LowWallPrefab, 180),
            new ExpectedPart(0, 12.85523361173418, 2.4971864443607004, 0, SkateParkMaterial.Wood, LowWallPrefab),
            new ExpectedPart(3, 12.85523361173418, 2.4971864443607004, 0, SkateParkMaterial.Wood, LowWallPrefab),
            new ExpectedPart(-1.85, 0, 0, 0, SkateParkMaterial.Metal, HalfWallPrefab, 90),
            new ExpectedPart(4.85, 0, 0, 0, SkateParkMaterial.Metal, HalfWallPrefab, 270),
            new ExpectedPart(-1.85, 3, 0, 0, SkateParkMaterial.Metal, HalfWallPrefab, 90),
            new ExpectedPart(4.85, 3, 0, 0, SkateParkMaterial.Metal, HalfWallPrefab, 270),
            new ExpectedPart(-1.85, -2.9488887394336025, 0.3882285676537811, 0, SkateParkMaterial.Metal, HalfWallPrefab, 90),
            new ExpectedPart(4.85, -2.9488887394336025, 0.3882285676537811, 0, SkateParkMaterial.Metal, HalfWallPrefab, 270),
            new ExpectedPart(-1.85, -5.626505545300693, 1.6368217898341313, 0, SkateParkMaterial.Metal, HalfWallPrefab, 90),
            new ExpectedPart(4.85, -5.626505545300693, 1.6368217898341313, 0, SkateParkMaterial.Metal, HalfWallPrefab, 270),
            new ExpectedPart(-1.85, 5.9488887394336025, 0.3882285676537811, 0, SkateParkMaterial.Metal, HalfWallPrefab, 90),
            new ExpectedPart(4.85, 5.9488887394336025, 0.3882285676537811, 0, SkateParkMaterial.Metal, HalfWallPrefab, 270),
            new ExpectedPart(-1.85, 8.626505545300694, 1.6368217898341313, 0, SkateParkMaterial.Metal, HalfWallPrefab, 90),
            new ExpectedPart(4.85, 8.626505545300694, 1.6368217898341313, 0, SkateParkMaterial.Metal, HalfWallPrefab, 270),
            new ExpectedPart(-1.85, -8.35523361173418, 2.4971864443607004, 0, SkateParkMaterial.Metal, HalfWallPrefab, 90),
            new ExpectedPart(4.85, -8.35523361173418, 2.4971864443607004, 0, SkateParkMaterial.Metal, HalfWallPrefab, 270),
            new ExpectedPart(-1.85, 11.35523361173418, 2.4971864443607004, 0, SkateParkMaterial.Metal, HalfWallPrefab, 90),
            new ExpectedPart(4.85, 11.35523361173418, 2.4971864443607004, 0, SkateParkMaterial.Metal, HalfWallPrefab, 270))
    };

    private static int Main(string[] args)
    {
        try
        {
            string path = typeof(SkateParkLayout).Assembly.Location;
            string hash;
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            Console.WriteLine("Plugin: " + path);
            Console.WriteLine("SHA-256: " + hash);
            if (args.Length > 1 || (args.Length == 1 && !string.Equals(args[0], hash, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Expected one optional SHA-256 matching the loaded plugin DLL.");
            PieceLayouts();
            AnchorYawAndSnap();
            Console.WriteLine("PASS: both approved park layout scenarios (" + checks.ToString(CultureInfo.InvariantCulture) + " checks).");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static void PieceLayouts()
    {
        int start = checks;
        foreach (Piece piece in Pieces)
        {
            Require(SkateParkLayout.Name(piece.Kind) == piece.Name, piece.Name + ": canonical name.");
            SkateParkKind parsed;
            Require(SkateParkLayout.TryKind(piece.Name.ToUpperInvariant(), out parsed) && parsed == piece.Kind,
                    piece.Name + ": command name does not select the requested piece.");
            Require(SkateParkLayout.PartCount(piece.Kind) == piece.Parts.Length, piece.Name + ": floor count.");
            for (int i = 0; i < piece.Parts.Length; i++)
            {
                SkateParkPart part;
                Require(SkateParkLayout.TryPart(piece.Kind, i, out part), piece.Name + ": missing floor " + i + ".");
                ExpectedPart expected = piece.Parts[i];
                Near(part.Right, expected.Right, piece.Name + ": right.");
                Near(part.Forward, expected.Forward, piece.Name + ": forward.");
                Near(part.Height, expected.Height, piece.Name + ": height.");
                Near(part.Pitch, expected.Pitch, piece.Name + ": pitch.");
                Near(part.Yaw, expected.Yaw, piece.Name + ": part yaw.");
                Require(part.Prefab == expected.Prefab, piece.Name + ": prefab.");
                Require(part.Material == expected.Material, piece.Name + ": material.");
            }
        }

        Point bankEntry = LocalEdge(SkateParkKind.Bank, 0, 0, -1.5);
        Point bankExit = LocalEdge(SkateParkKind.Bank, 0, 0, 1.5);
        Point kickerEntry = LocalEdge(SkateParkKind.Kicker, 0, 0, -1.5);
        Point kickerExit = LocalEdge(SkateParkKind.Kicker, 0, 0, 1.5);
        Near(bankEntry.Y, 0, "bank: entrance meets anchor plane.");
        Near(bankExit.Y, 0.623735072453278, "bank: exit rise.");
        Near(kickerEntry.Y, 0, "kicker: entrance meets anchor plane.");
        Near(kickerExit.Y, 1.2678547852220982, "kicker: exit rise.");
        Near(LocalEdge(SkateParkKind.QuarterPipe, 0, 0, -1.5).Y, 0, "quarterpipe: entrance.");
        Near(LocalEdge(SkateParkKind.QuarterPipe, 1, 0, 1.5).Y, 2.4971864443607004, "quarterpipe: exit rise.");
        Near(LocalEdge(SkateParkKind.QuarterPipe, 1, 0, 1.5).Z -
             LocalEdge(SkateParkKind.QuarterPipe, 0, 0, -1.5).Z,
             5.35523361173418, "quarterpipe: projected run.");
        Near(LocalEdge(SkateParkKind.Funbox, 1, 0, -1.5).Y, 0, "funbox: leading entrance.");
        Near(LocalEdge(SkateParkKind.Funbox, 2, 0, 1.5).Y, 0, "funbox: trailing entrance.");
        Near(LocalEdge(SkateParkKind.Funbox, 2, 0, 1.5).Z -
             LocalEdge(SkateParkKind.Funbox, 1, 0, -1.5).Z,
             8.79555495773441, "funbox: projected run.");
        Near(LocalEdge(SkateParkKind.LongLedge, 2, 1.5, 0).X -
             LocalEdge(SkateParkKind.LongLedge, 1, -1.5, 0).X, 9, "long-ledge: total width.");
        CheckSeams(0, 0, 0, 0);
        HalfpipeBounds();
        Console.WriteLine("PASS: skate_park_piece_layouts (8 pieces, 48 parts, " +
                          (checks - start).ToString(CultureInfo.InvariantCulture) + " checks).");
    }

    private static void AnchorYawAndSnap()
    {
        int start = checks;
        Angle[] angles =
        {
            new Angle(0, 0, 1, 0), new Angle(90, 90, 0, 1), new Angle(180, 180, -1, 0),
            new Angle(270, 270, 0, -1), new Angle(-90, 270, 0, -1), new Angle(450, 90, 0, 1),
            new Angle(720, 0, 1, 0), new Angle(-720, 0, 1, 0),
            new Angle(37.25, 37.25, 0.796002002534622, 0.6052939880428944),
            new Angle(123.75, 123.75, -0.5555702330196023, 0.8314696123025451)
        };
        Point[] anchors = { new Point(17.375, 2.625, -8.0625), new Point(-23.8125, -4.375, 31.1875) };
        Point translation = new Point(0.34375, 1.125, -0.21875);
        foreach (Point anchor in anchors)
        foreach (Angle angle in angles)
        {
            foreach (Piece piece in Pieces)
            for (int i = 0; i < piece.Parts.Length; i++)
            {
                ExpectedPart expected = piece.Parts[i];
                SkateParkTransform value = Transform(piece.Kind, i, anchor.X, anchor.Y, anchor.Z, angle.Input);
                Near(value.X, anchor.X + expected.Right * angle.Cos + expected.Forward * angle.Sin, piece.Name + ": rotated X.");
                Near(value.Y, anchor.Y + expected.Height, piece.Name + ": translated Y.");
                Near(value.Z, anchor.Z - expected.Right * angle.Sin + expected.Forward * angle.Cos, piece.Name + ": rotated Z.");
                double expectedYaw = angle.Normalized + expected.Yaw;
                if (expectedYaw >= 360) expectedYaw -= 360;
                Near(value.Yaw, expectedYaw, piece.Name + ": normalized part yaw.");
                Near(value.Pitch, expected.Pitch, piece.Name + ": authored pitch preserved.");
                Near((value.X - anchor.X) * angle.Cos - (value.Z - anchor.Z) * angle.Sin,
                     expected.Right, piece.Name + ": recovered local right.");
                Near((value.X - anchor.X) * angle.Sin + (value.Z - anchor.Z) * angle.Cos,
                     expected.Forward, piece.Name + ": recovered local forward.");
                SkateParkTransform shifted = Transform(piece.Kind, i, anchor.X + translation.X,
                    anchor.Y + translation.Y, anchor.Z + translation.Z, angle.Input);
                Near(shifted.X - value.X, translation.X, piece.Name + ": fractional translation X.");
                Near(shifted.Y - value.Y, translation.Y, piece.Name + ": fractional translation Y.");
                Near(shifted.Z - value.Z, translation.Z, piece.Name + ": fractional translation Z.");
            }
            CheckSeams(anchor.X, anchor.Y, anchor.Z, angle.Input);
        }
        Console.WriteLine("PASS: skate_park_anchor_yaw_and_snap (20 translated/yaw anchors, " +
                          (checks - start).ToString(CultureInfo.InvariantCulture) + " checks).");
    }

    private static void CheckSeams(double x, double y, double z, double yaw)
    {
        foreach (double side in new[] { -1.5, 1.5 })
        {
            Same(WorldEdge(SkateParkKind.QuarterPipe, 0, side, 1.5, x, y, z, yaw),
                 WorldEdge(SkateParkKind.QuarterPipe, 1, side, -1.5, x, y, z, yaw), "quarterpipe: joined slope corners.");
            Same(WorldEdge(SkateParkKind.Funbox, 1, side, 1.5, x, y, z, yaw),
                 WorldEdge(SkateParkKind.Funbox, 0, side, -1.5, x, y, z, yaw), "funbox: leading deck corners.");
            Same(WorldEdge(SkateParkKind.Funbox, 2, side, -1.5, x, y, z, yaw),
                 WorldEdge(SkateParkKind.Funbox, 0, side, 1.5, x, y, z, yaw), "funbox: trailing deck corners.");
            Same(WorldEdge(SkateParkKind.LongLedge, 1, 1.5, side, x, y, z, yaw),
                 WorldEdge(SkateParkKind.LongLedge, 0, -1.5, side, x, y, z, yaw), "long-ledge: left seam corners.");
            Same(WorldEdge(SkateParkKind.LongLedge, 2, -1.5, side, x, y, z, yaw),
                 WorldEdge(SkateParkKind.LongLedge, 0, 1.5, side, x, y, z, yaw), "long-ledge: right seam corners.");
        }
        HalfpipeSeams(x, y, z, yaw);
    }

    private static void HalfpipeSeams(double x, double y, double z, double yaw)
    {
        SkateParkKind kind = SkateParkKind.Halfpipe;
        Same(Centre(kind, 0, x, y, z, yaw), new Point(x, y, z), "halfpipe: native first-floor anchor.");
        foreach (double edge in new[] { -1.5, 1.5 })
        {
            foreach (int station in new[] { 0, 2, 4, 6, 8, 10, 12, 14 })
                Same(WorldEdge(kind, station, 1.5, edge, x, y, z, yaw),
                     WorldEdge(kind, station + 1, -1.5, edge, x, y, z, yaw), "halfpipe: joined floor lanes.");
            for (int lane = 0; lane < 2; lane++)
            {
                Same(WorldEdge(kind, lane, edge, 1.5, x, y, z, yaw),
                     WorldEdge(kind, lane + 2, edge, -1.5, x, y, z, yaw), "halfpipe: central flat seam.");
                Same(WorldEdge(kind, lane, edge, -1.5, x, y, z, yaw),
                     WorldEdge(kind, lane + 4, edge, 1.5, x, y, z, yaw), "halfpipe: back flat-transition seam.");
                Same(WorldEdge(kind, lane + 4, edge, -1.5, x, y, z, yaw),
                     WorldEdge(kind, lane + 6, edge, 1.5, x, y, z, yaw), "halfpipe: back transition seam.");
                Same(WorldEdge(kind, lane + 6, edge, -1.5, x, y, z, yaw),
                     WorldEdge(kind, lane + 12, edge, 1.5, x, y, z, yaw), "halfpipe: back deck seam.");
                Same(WorldEdge(kind, lane + 2, edge, 1.5, x, y, z, yaw),
                     WorldEdge(kind, lane + 8, edge, -1.5, x, y, z, yaw), "halfpipe: front flat-transition seam.");
                Same(WorldEdge(kind, lane + 8, edge, 1.5, x, y, z, yaw),
                     WorldEdge(kind, lane + 10, edge, -1.5, x, y, z, yaw), "halfpipe: front transition seam.");
                Same(WorldEdge(kind, lane + 10, edge, 1.5, x, y, z, yaw),
                     WorldEdge(kind, lane + 14, edge, -1.5, x, y, z, yaw), "halfpipe: front deck seam.");
            }
        }
        for (int lane = 0; lane < 2; lane++)
        {
            Same(Centre(kind, 16 + lane, x, y, z, yaw), WorldEdge(kind, 12 + lane, 0, -1.5, x, y, z, yaw),
                 "halfpipe: wood back rail at deck edge.");
            Same(Centre(kind, 18 + lane, x, y, z, yaw), WorldEdge(kind, 14 + lane, 0, 1.5, x, y, z, yaw),
                 "halfpipe: wood front rail at deck edge.");
        }
        Same(WorldEdge(kind, 16, -1.5, 0, x, y, z, yaw), WorldEdge(kind, 17, 1.5, 0, x, y, z, yaw),
             "halfpipe: joined back rail widths.");
        Same(WorldEdge(kind, 18, 1.5, 0, x, y, z, yaw), WorldEdge(kind, 19, -1.5, 0, x, y, z, yaw),
             "halfpipe: joined front rail widths.");
        for (int station = 0; station < 8; station++)
        {
            Same(Centre(kind, 20 + station * 2, x, y, z, yaw),
                 WorldEdge(kind, station * 2, -1.85, 0, x, y, z, yaw), "halfpipe: exterior left side panel.");
            Same(Centre(kind, 21 + station * 2, x, y, z, yaw),
                 WorldEdge(kind, station * 2 + 1, 1.85, 0, x, y, z, yaw), "halfpipe: exterior right side panel.");
        }
    }

    private static void HalfpipeBounds()
    {
        int stone = 0, metal = 0, wood = 0, floors = 0, rails = 0, sides = 0;
        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity, maxY = double.NegativeInfinity;
        for (int i = 0; i < 36; i++)
        {
            SkateParkPart part;
            Require(SkateParkLayout.TryPart(SkateParkKind.Halfpipe, i, out part), "halfpipe: missing part.");
            if (part.Material == SkateParkMaterial.Stone) stone++;
            if (part.Material == SkateParkMaterial.Metal) metal++;
            if (part.Material == SkateParkMaterial.Wood) wood++;
            if (part.Prefab == FloorPrefab) floors++;
            if (part.Prefab == LowWallPrefab) rails++;
            if (part.Prefab == HalfWallPrefab) sides++;
            if (i >= 16) continue;
            foreach (double right in new[] { -1.5, 1.5 })
            foreach (double forward in new[] { -1.5, 1.5 })
            {
                Point vertex = LocalEdge(SkateParkKind.Halfpipe, i, right, forward);
                Require(vertex.Y >= -Tolerance, "halfpipe: ride floor below the native flat plane.");
                minX = Math.Min(minX, vertex.X); maxX = Math.Max(maxX, vertex.X);
                minZ = Math.Min(minZ, vertex.Z); maxZ = Math.Max(maxZ, vertex.Z); maxY = Math.Max(maxY, vertex.Y);
            }
        }
        Require(stone == 4 && metal == 28 && wood == 4, "halfpipe: material counts.");
        Require(floors == 16 && rails == 4 && sides == 16, "halfpipe: native prefab counts.");
        Near(minX, -1.5, "halfpipe: left ride edge."); Near(maxX, 4.5, "halfpipe: right ride edge.");
        Near(minZ, -9.85523361173418, "halfpipe: back ride edge.");
        Near(maxZ, 12.85523361173418, "halfpipe: front ride edge.");
        Near(maxX - minX, 6, "halfpipe: ride width."); Near(maxZ - minZ, 22.71046722346836, "halfpipe: ride length.");
        Near(maxY, 2.4971864443607004, "halfpipe: deck rise.");
        Near(LocalEdge(SkateParkKind.Halfpipe, 0, -1.5, -1.5).Z, -1.5, "halfpipe: flat begins at snapped tile edge.");
        Near(LocalEdge(SkateParkKind.Halfpipe, 3, 1.5, 1.5).Z, 4.5, "halfpipe: central flat has 6m length.");
    }

    private static Point Centre(SkateParkKind kind, int index, double x, double y, double z, double yaw)
    { SkateParkTransform value = Transform(kind, index, x, y, z, yaw); return new Point(value.X, value.Y, value.Z); }

    private static Point LocalEdge(SkateParkKind kind, int index, double right, double forward)
    { return WorldEdge(kind, index, right, forward, 0, 0, 0, 0); }

    private static Point WorldEdge(SkateParkKind kind, int index, double right, double forward,
                                   double x, double y, double z, double yaw)
    {
        SkateParkTransform value = Transform(kind, index, x, y, z, yaw);
        double pitch = value.Pitch * Math.PI / 180, heading = value.Yaw * Math.PI / 180;
        double horizontal = forward * Math.Cos(pitch);
        return new Point(value.X + right * Math.Cos(heading) + horizontal * Math.Sin(heading),
                         value.Y - forward * Math.Sin(pitch),
                         value.Z - right * Math.Sin(heading) + horizontal * Math.Cos(heading));
    }

    private static SkateParkTransform Transform(SkateParkKind kind, int index, double x, double y, double z, double yaw)
    {
        SkateParkTransform value;
        Require(SkateParkLayout.TryTransform(kind, index, x, y, z, yaw, out value), kind + ": valid anchor refused.");
        return value;
    }

    private static void Same(Point first, Point second, string message)
    { Near(first.X, second.X, message + " X"); Near(first.Y, second.Y, message + " Y"); Near(first.Z, second.Z, message + " Z"); }

    private static void Near(double actual, double expected, string message)
    { Require(!double.IsNaN(actual) && !double.IsInfinity(actual) && Math.Abs(actual - expected) <= Tolerance, message); }

    private static void Require(bool condition, string message)
    { checks++; if (!condition) throw new InvalidOperationException(message); }
}
