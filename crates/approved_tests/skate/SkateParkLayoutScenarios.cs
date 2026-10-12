using System;
using System.Globalization;
using System.IO;
using System.Reflection;
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
            new ExpectedPart(0, -9.85523361173418, 2.4971864443607004, 0, SkateParkMaterial.Wood, LowWallPrefab, 270),
            new ExpectedPart(3, -9.85523361173418, 2.4971864443607004, 0, SkateParkMaterial.Wood, LowWallPrefab, 270),
            new ExpectedPart(0, 12.85523361173418, 2.4971864443607004, 0, SkateParkMaterial.Wood, LowWallPrefab, 90),
            new ExpectedPart(3, 12.85523361173418, 2.4971864443607004, 0, SkateParkMaterial.Wood, LowWallPrefab, 90),
            new ExpectedPart(-1.85, 0, 0, 0, SkateParkMaterial.Metal, HalfWallPrefab, 180),
            new ExpectedPart(4.85, 0, 0, 0, SkateParkMaterial.Metal, HalfWallPrefab, 0),
            new ExpectedPart(-1.85, 3, 0, 0, SkateParkMaterial.Metal, HalfWallPrefab, 180),
            new ExpectedPart(4.85, 3, 0, 0, SkateParkMaterial.Metal, HalfWallPrefab, 0),
            new ExpectedPart(-1.85, -2.9488887394336025, 0.3882285676537811, 0, SkateParkMaterial.Metal, HalfWallPrefab, 180),
            new ExpectedPart(4.85, -2.9488887394336025, 0.3882285676537811, 0, SkateParkMaterial.Metal, HalfWallPrefab, 0),
            new ExpectedPart(-1.85, -5.626505545300693, 1.6368217898341313, 0, SkateParkMaterial.Metal, HalfWallPrefab, 180),
            new ExpectedPart(4.85, -5.626505545300693, 1.6368217898341313, 0, SkateParkMaterial.Metal, HalfWallPrefab, 0),
            new ExpectedPart(-1.85, 5.9488887394336025, 0.3882285676537811, 0, SkateParkMaterial.Metal, HalfWallPrefab, 180),
            new ExpectedPart(4.85, 5.9488887394336025, 0.3882285676537811, 0, SkateParkMaterial.Metal, HalfWallPrefab, 0),
            new ExpectedPart(-1.85, 8.626505545300694, 1.6368217898341313, 0, SkateParkMaterial.Metal, HalfWallPrefab, 180),
            new ExpectedPart(4.85, 8.626505545300694, 1.6368217898341313, 0, SkateParkMaterial.Metal, HalfWallPrefab, 0),
            new ExpectedPart(-1.85, -8.35523361173418, 2.4971864443607004, 0, SkateParkMaterial.Metal, HalfWallPrefab, 180),
            new ExpectedPart(4.85, -8.35523361173418, 2.4971864443607004, 0, SkateParkMaterial.Metal, HalfWallPrefab, 0),
            new ExpectedPart(-1.85, 11.35523361173418, 2.4971864443607004, 0, SkateParkMaterial.Metal, HalfWallPrefab, 180),
            new ExpectedPart(4.85, 11.35523361173418, 2.4971864443607004, 0, SkateParkMaterial.Metal, HalfWallPrefab, 0))
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
            Console.WriteLine("Park library: " + path);
            Console.WriteLine("SHA-256: " + hash);
            if (args.Length > 1 || (args.Length == 1 && !string.Equals(args[0], hash, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Expected one optional SHA-256 matching the loaded park library DLL.");
            PieceLayouts();
            AnchorYawAndSnap();
            FoundationSupport();
            Console.WriteLine("PASS: both approved park layout scenarios (" + checks.ToString(CultureInfo.InvariantCulture) + " checks).");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static SkateParkBlock[] Observations(Piece piece, Point anchor, Angle angle)
    {
        var blocks = new SkateParkBlock[piece.Parts.Length];
        for (int i = 0; i < blocks.Length; i++)
        {
            ExpectedPart part = piece.Parts[i];
            double yaw = angle.Normalized + part.Yaw;
            if (yaw >= 360) yaw -= 360;
            blocks[i] = new SkateParkBlock(part.Prefab,
                anchor.X + part.Right * angle.Cos + part.Forward * angle.Sin,
                anchor.Y + part.Height,
                anchor.Z - part.Right * angle.Sin + part.Forward * angle.Cos,
                yaw, part.Pitch);
        }
        return blocks;
    }

    private static void AnchorAndMatch()
    {
        Angle[] angles =
        {
            new Angle(0, 0, 1, 0), new Angle(90, 90, 0, 1), new Angle(180, 180, -1, 0),
            new Angle(270, 270, 0, -1), new Angle(-90, 270, 0, -1), new Angle(450, 90, 0, 1),
            new Angle(720, 0, 1, 0), new Angle(-720, 0, 1, 0),
            new Angle(37.25, 37.25, 0.796002002534622, 0.6052939880428944),
            new Angle(123.75, 123.75, -0.5555702330196023, 0.8314696123025451)
        };
        Point[] anchors = { new Point(17.375, 2.625, -8.0625), new Point(-23.8125, -4.375, 31.1875) };
        foreach (Piece piece in Pieces)
        foreach (Point anchor in anchors)
        foreach (Angle angle in angles)
        {
            SkateParkBlock[] blocks = Observations(piece, anchor, angle);
            double x, y, z, yaw;
            for (int i = 0; i < blocks.Length; i++)
            {
                SkateParkBlock block = blocks[i];
                Require(SkateParkLayout.TryAnchor(piece.Kind, i, block.X, block.Y, block.Z,
                    block.Yaw + 360, out x, out y, out z, out yaw), piece.Name + ": inverse refused.");
                Near(x, anchor.X, piece.Name + ": inverse X.");
                Near(y, anchor.Y, piece.Name + ": inverse Y.");
                Near(z, anchor.Z, piece.Name + ": inverse Z.");
                Near(yaw, angle.Normalized, piece.Name + ": inverse yaw.");
            }
            Require(SkateParkLayout.TryMatch(piece.Kind, blocks, 0.01, out x, out y, out z, out yaw),
                piece.Name + ": independently authored pose set refused.");
            Near(x, anchor.X, piece.Name + ": matched X."); Near(y, anchor.Y, piece.Name + ": matched Y.");
            Near(z, anchor.Z, piece.Name + ": matched Z."); Near(yaw, angle.Normalized, piece.Name + ": matched yaw.");
            double originalX = x, originalY = y, originalZ = z, originalYaw = yaw;
            for (int i = 0; i < blocks.Length; i++)
            {
                SkateParkBlock b = blocks[i];
                blocks[i] = new SkateParkBlock(b.Prefab, b.X, b.Y, b.Z, b.Yaw - 720, b.Pitch + 360);
            }
            Array.Reverse(blocks);
            Require(SkateParkLayout.TryMatch(piece.Kind, blocks, 0.01, out x, out y, out z, out yaw),
                piece.Name + ": reversed/wrapped set refused.");
            Require(x == originalX && y == originalY && z == originalZ && yaw == originalYaw,
                piece.Name + ": ordering or equivalent angle wraps changed deterministic anchor.");
            for (int rotation = 1; rotation < blocks.Length; rotation++)
            {
                SkateParkBlock first = blocks[0];
                Array.Copy(blocks, 1, blocks, 0, blocks.Length - 1);
                blocks[blocks.Length - 1] = first;
                Require(SkateParkLayout.TryMatch(piece.Kind, blocks, 0.01, out x, out y, out z, out yaw) &&
                    x == originalX && y == originalY && z == originalZ && yaw == originalYaw,
                    piece.Name + ": cyclic reorder changed deterministic anchor.");
            }
        }

        Point zero = new Point(0, 0, 0);
        Angle flat = new Angle(0, 0, 1, 0);
        foreach (Piece piece in Pieces)
        {
            SkateParkBlock[] blocks = Observations(piece, zero, flat);
            double x, y, z, yaw;
            Require(SkateParkLayout.TryMatch(piece.Kind, blocks, 0.1, out x, out y, out z, out yaw),
                piece.Name + ": maximum allowed tolerance refused.");
            SkateParkBlock old = blocks[0];
            blocks[0] = new SkateParkBlock("wrong.prefab", old.X, old.Y, old.Z, old.Yaw, old.Pitch);
            Refused(piece.Kind, blocks, 0.01, piece.Name + ": wrong prefab accepted.");
            blocks[0] = new SkateParkBlock(old.Prefab, old.X, old.Y, old.Z, old.Yaw, old.Pitch + 0.02);
            Refused(piece.Kind, blocks, 0.01, piece.Name + ": wrong pitch accepted.");
            blocks[0] = new SkateParkBlock(old.Prefab, double.NaN, old.Y, old.Z, old.Yaw, old.Pitch);
            Refused(piece.Kind, blocks, 0.01, piece.Name + ": NaN position accepted.");
            blocks[0] = new SkateParkBlock(old.Prefab, old.X, old.Y, old.Z, double.PositiveInfinity, old.Pitch);
            Refused(piece.Kind, blocks, 0.01, piece.Name + ": infinite yaw accepted.");
            blocks[0] = old;
            var extra = new SkateParkBlock[blocks.Length + 1];
            Array.Copy(blocks, extra, blocks.Length);
            extra[extra.Length - 1] = old;
            Refused(piece.Kind, extra, 0.01, piece.Name + ": extra part accepted.");
            var missing = new SkateParkBlock[blocks.Length - 1];
            Array.Copy(blocks, missing, missing.Length);
            Refused(piece.Kind, missing, 0.01, piece.Name + ": missing part accepted.");
            if (blocks.Length > 1)
            {
                SkateParkBlock last = blocks[blocks.Length - 1];
                blocks[blocks.Length - 1] = old;
                Refused(piece.Kind, blocks, 0.01, piece.Name + ": duplicate replacing missing part accepted.");
                blocks[blocks.Length - 1] = new SkateParkBlock(last.Prefab, last.X + 0.03, last.Y,
                    last.Z, last.Yaw, last.Pitch);
                Refused(piece.Kind, blocks, 0.01, piece.Name + ": position outside tolerance accepted.");
                blocks[blocks.Length - 1] = new SkateParkBlock(last.Prefab, last.X, last.Y,
                    last.Z, last.Yaw + 0.03, last.Pitch);
                Refused(piece.Kind, blocks, 0.01, piece.Name + ": yaw outside tolerance accepted.");
                blocks[blocks.Length - 1] = last;
            }
            Refused(piece.Kind, blocks, -0.001, piece.Name + ": negative tolerance accepted.");
            Refused(piece.Kind, blocks, 0.100001, piece.Name + ": broad tolerance accepted.");
            Refused(piece.Kind, blocks, double.NaN, piece.Name + ": NaN tolerance accepted.");
            Refused(piece.Kind, blocks, double.PositiveInfinity, piece.Name + ": infinite tolerance accepted.");
        }

        SkateParkBlock[] quarter = Observations(Pieces[2], zero, flat);
        for (int i = 0; i < quarter.Length; i++)
        {
            SkateParkBlock b = quarter[i];
            quarter[i] = new SkateParkBlock(b.Prefab, b.X, b.Y, -b.Z, b.Yaw, b.Pitch);
        }
        Refused(SkateParkKind.QuarterPipe, quarter, 0.01, "quarterpipe: reflected assembly accepted.");
        double ax, ay, az, aw;
        SkateParkBlock[] low = Observations(Pieces[3], zero, flat);
        Require(SkateParkLayout.TryMatch(SkateParkKind.LedgeLow, low, 0, out ax, out ay, out az, out aw),
            "ledge-low: exact zero-tolerance pose refused.");
        Require(SkateParkLayout.TryMatch(SkateParkKind.LedgeHigh, low, 0, out ax, out ay, out az, out aw),
            "single-floor geometric consistency incorrectly inferred registration/kind.");
        Near(ay, -0.4, "single floor: high-ledge candidate requires a different anchor.");
        Refused((SkateParkKind)999, low, 0.01, "unknown kind accepted.");
        Refused(SkateParkKind.Bank, null, 0.01, "null input accepted.");
        ObservedAnchorTolerance();
        InverseBounds();
        Require(!SkateParkLayout.TryAnchor(SkateParkKind.Bank, -1, 0, 0, 0, 0, out ax, out ay, out az, out aw) &&
            ax == 0 && ay == 0 && az == 0 && aw == 0, "invalid inverse index mutated outputs.");
        Require(!SkateParkLayout.TryAnchor(SkateParkKind.Bank, 0, double.NaN, 0, 0, 0, out ax, out ay, out az, out aw) &&
            ax == 0 && ay == 0 && az == 0 && aw == 0, "invalid inverse position mutated outputs.");
        Require(!SkateParkLayout.TryAnchor(SkateParkKind.Bank, 0, 0, 0, 0, double.PositiveInfinity, out ax, out ay, out az, out aw) &&
            ax == 0 && ay == 0 && az == 0 && aw == 0, "invalid inverse yaw mutated outputs.");
    }

    private static void ObservedAnchorTolerance()
    {
        Point zero = new Point(0, 0, 0);
        Angle flat = new Angle(0, 0, 1, 0);
        foreach (double noise in new[] { 0.004, 0.009 })
        {
            SkateParkBlock[] blocks = Observations(Pieces[2], zero, flat);
            SkateParkBlock lower = blocks[0], upper = blocks[1];
            blocks[0] = new SkateParkBlock(lower.Prefab, lower.X, lower.Y + noise,
                lower.Z, lower.Yaw, lower.Pitch);
            blocks[1] = new SkateParkBlock(upper.Prefab, upper.X, upper.Y - noise,
                upper.Z, upper.Yaw, upper.Pitch);
            for (int order = 0; order < 2; order++)
            {
                double x, y, z, yaw;
                if (noise == 0.004)
                {
                    Require(SkateParkLayout.TryMatch(SkateParkKind.QuarterPipe, blocks, 0.01,
                        out x, out y, out z, out yaw), "quarter: relative noise inside tolerance refused.");
                    Near(x, 0, "quarter: noisy anchor X.");
                    Near(y, noise, "quarter: anchor follows the deterministic lower observation.");
                    Near(z, 0, "quarter: noisy anchor Z.");
                    Near(yaw, 0, "quarter: noisy anchor yaw.");
                }
                else Refused(SkateParkKind.QuarterPipe, blocks, 0.01,
                    "quarter: common ideal anchor was incorrectly fitted instead of using the observed anchor.");
                Array.Reverse(blocks);
            }
        }
    }

    private static void InverseBounds()
    {
        double x, y, z, yaw;
        foreach (double bound in new[] { -100000.0, 100000.0 })
        {
            Require(SkateParkLayout.TryAnchor(SkateParkKind.Halfpipe, 0, bound, 0, 0, 0,
                out x, out y, out z, out yaw) && x == bound && y == 0 && z == 0 && yaw == 0,
                "inverse: exact X bound refused.");
            Require(SkateParkLayout.TryAnchor(SkateParkKind.Halfpipe, 0, 0, bound, 0, 0,
                out x, out y, out z, out yaw) && x == 0 && y == bound && z == 0 && yaw == 0,
                "inverse: exact Y bound refused.");
            Require(SkateParkLayout.TryAnchor(SkateParkKind.Halfpipe, 0, 0, 0, bound, 0,
                out x, out y, out z, out yaw) && x == 0 && y == 0 && z == bound && yaw == 0,
                "inverse: exact Z bound refused.");
        }
        foreach (double bound in new[] { -1000000.0, 1000000.0 })
            Require(SkateParkLayout.TryAnchor(SkateParkKind.Halfpipe, 0, 0, 0, 0, bound,
                out x, out y, out z, out yaw) && x == 0 && y == 0 && z == 0 && yaw == (bound < 0 ? 80 : 280),
                "inverse: exact yaw bound refused or incorrectly normalized.");
        foreach (double angle in new[] { -double.Epsilon, -1e-15, -0.0 })
            Require(SkateParkLayout.TryAnchor(SkateParkKind.Halfpipe, 0, 0, 0, 0, angle,
                out x, out y, out z, out yaw) && yaw == 0 &&
                BitConverter.DoubleToInt64Bits(yaw) == 0, "inverse: tiny negative yaw was not canonical positive zero.");
        foreach (double bad in new[] { -100000.000001, 100000.000001 })
        {
            Require(!SkateParkLayout.TryAnchor(SkateParkKind.Halfpipe, 0, bad, 0, 0, 0,
                out x, out y, out z, out yaw) && x == 0 && y == 0 && z == 0 && yaw == 0,
                "inverse: finite out-of-range X mutated outputs.");
            Require(!SkateParkLayout.TryAnchor(SkateParkKind.Halfpipe, 0, 0, bad, 0, 0,
                out x, out y, out z, out yaw) && x == 0 && y == 0 && z == 0 && yaw == 0,
                "inverse: finite out-of-range Y mutated outputs.");
            Require(!SkateParkLayout.TryAnchor(SkateParkKind.Halfpipe, 0, 0, 0, bad, 0,
                out x, out y, out z, out yaw) && x == 0 && y == 0 && z == 0 && yaw == 0,
                "inverse: finite out-of-range Z mutated outputs.");
        }
        foreach (double bad in new[] { -1000000.000001, 1000000.000001 })
            Require(!SkateParkLayout.TryAnchor(SkateParkKind.Halfpipe, 0, 0, 0, 0, bad,
                out x, out y, out z, out yaw) && x == 0 && y == 0 && z == 0 && yaw == 0,
                "inverse: finite out-of-range yaw mutated outputs.");
        Require(!SkateParkLayout.TryAnchor(SkateParkKind.QuarterPipe, 0, 0, -100000, 0, 0,
            out x, out y, out z, out yaw) && x == 0 && y == 0 && z == 0 && yaw == 0,
            "inverse: derived Y outside bounds mutated outputs.");
        Require(!SkateParkLayout.TryAnchor(SkateParkKind.Halfpipe, 1, -100000, 0, 0, 0,
            out x, out y, out z, out yaw) && x == 0 && y == 0 && z == 0 && yaw == 0,
            "inverse: derived X outside bounds mutated outputs.");
        Require(!SkateParkLayout.TryAnchor(SkateParkKind.Halfpipe, 1, 0, 0, 100000, 90,
            out x, out y, out z, out yaw) && x == 0 && y == 0 && z == 0 && yaw == 0,
            "inverse: derived Z outside bounds mutated outputs.");
    }

    private static void Refused(SkateParkKind kind, SkateParkBlock[] blocks, double tolerance, string message)
    {
        double x, y, z, yaw;
        Require(!SkateParkLayout.TryMatch(kind, blocks, tolerance, out x, out y, out z, out yaw), message);
        Require(x == 0 && y == 0 && z == 0 && yaw == 0, "rejected match mutated outputs.");
    }

    private static void VisualOrigins()
    {
        double[] expectedForward = { -1.4672214011007085, -1.3594616805549748,
            -1.4488887394336025, 0, 0, 0, 0, 1.5 };
        foreach (Piece piece in Pieces)
        {
            double right, forward, height, yaw;
            Require(SkateParkLayout.VisualOrigin(piece.Kind, out right, out forward, out height, out yaw),
                piece.Name + ": visual origin refused.");
            Near(right, piece.Kind == SkateParkKind.Halfpipe ? 1.5 : 0, piece.Name + ": visual right.");
            Near(forward, expectedForward[(int)piece.Kind], piece.Name + ": visual forward.");
            Near(height, 0, piece.Name + ": nominal visual origin height.");
            Near(yaw, 0, piece.Name + ": local visual yaw.");
        }
        double r, f, h, a;
        Require(!SkateParkLayout.VisualOrigin((SkateParkKind)999, out r, out f, out h, out a) &&
            r == 0 && f == 0 && h == 0 && a == 0, "unknown visual kind changed outputs.");
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
        VisualOrigins();
        Console.WriteLine("PASS: skate_park_piece_layouts (8 pieces, 48 parts, " +
                          (checks - start).ToString(CultureInfo.InvariantCulture) + " checks).");
    }

    private static void AnchorYawAndSnap()
    {
        int start = checks;
        AnchorAndMatch();
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
        PlacementRequests();
        Console.WriteLine("PASS: skate_park_anchor_yaw_and_snap (20 translated/yaw anchors, " +
                          (checks - start).ToString(CultureInfo.InvariantCulture) + " checks).");
    }

    private static void PlacementRequests()
    {
        Assembly library = typeof(SkateParkLayout).Assembly;
        Type placement = library.GetType("Shortcut.RustMod.SkateParkPlacement", false);
        if (placement == null)
        {
            Require(library.GetName().Name == "SkateMotion", "Actual server plugin must include the placement request helper.");
            Console.WriteLine("Shared SkateMotion DLL has no server placement request helper; existing layout checks continue.");
            return;
        }
        MethodInfo request = placement.GetMethod("TryRequest", BindingFlags.Public | BindingFlags.Static, null,
            new[] { typeof(string[]), typeof(SkateParkKind).MakeByRefType(), typeof(bool).MakeByRefType(),
                typeof(double).MakeByRefType(), typeof(double).MakeByRefType(), typeof(double).MakeByRefType(),
                typeof(double).MakeByRefType(), typeof(string).MakeByRefType() }, null);
        Require(request != null && request.ReturnType == typeof(bool), "Exact public placement request API is required.");
        ParameterInfo[] parameters = request.GetParameters();
        for (int i = 1; i < parameters.Length; i++) Require(parameters[i].IsOut, "Placement request output must use out parameters.");

        CultureInfo previous = CultureInfo.CurrentCulture, previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo custom = (CultureInfo)new CultureInfo("en-US").Clone();
            custom.NumberFormat.NumberDecimalSeparator = ",";
            custom.NumberFormat.NegativeSign = "~";
            foreach (CultureInfo culture in new[] { CultureInfo.InvariantCulture, new CultureInfo("fr-FR"), custom })
            {
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                foreach (Piece piece in Pieces)
                {
                    RequestGood(request, new[] { piece.Name }, piece.Kind, false, 0, 0, 0, 0);
                    RequestGood(request, new[] { piece.Name.ToUpperInvariant(), "17.375", "-2.625", "-8.0625", "-90" },
                        piece.Kind, true, 17.375, -2.625, -8.0625, 270);
                }
                RequestGood(request, new[] { "halfpipe", "-100000", "100000", "0", "1000000" },
                    SkateParkKind.Halfpipe, true, -100000, 100000, 0, 280);
                RequestGood(request, new[] { "bank", "1e5", "-1E5", "-0", "-1000000" },
                    SkateParkKind.Bank, true, 100000, -100000, 0, 80);
                foreach (Angle angle in new[] { new Angle(0, 0, 0, 0), new Angle(360, 0, 0, 0),
                    new Angle(-360, 0, 0, 0), new Angle(720, 0, 0, 0), new Angle(-720, 0, 0, 0),
                    new Angle(-37.25, 322.75, 0, 0), new Angle(450, 90, 0, 0), new Angle(359.999, 359.999, 0, 0) })
                    RequestGood(request, new[] { "quarterpipe", "0.125", "-0.25", "0.0625",
                        angle.Input.ToString("R", CultureInfo.InvariantCulture) }, SkateParkKind.QuarterPipe,
                        true, 0.125, -0.25, 0.0625, angle.Normalized);
                RequestGood(request, new[] { "funbox", "0", "0", "0", "-5E-324" }, SkateParkKind.Funbox, true, 0, 0, 0, 0);
                foreach (string value in new[] { "1,25", "1,000", "~1" })
                {
                    RequestBad(request, new[] { "bank", value, "0", "0", "0" });
                    RequestBad(request, new[] { "bank", "0", "0", "0", value });
                }
            }
        }
        finally { CultureInfo.CurrentCulture = previous; CultureInfo.CurrentUICulture = previousUi; }

        foreach (string[] args in new[] { null, new string[0], new string[] { null }, new[] { "" },
            new[] { "cancel" }, new[] { "unknown" }, new[] { "bank", "0" }, new[] { "bank", "0", "0" },
            new[] { "bank", "0", "0", "0" }, new[] { "bank", "0", "0", "0", "0", "extra" },
            new[] { "cancel", "0", "0", "0", "0" }, new[] { "unknown", "0", "0", "0", "0" } })
            RequestBad(request, args);
        for (int component = 1; component <= 4; component++)
        {
            foreach (string invalid in new[] { null, "", "NaN", "Infinity", "+Infinity", "-Infinity", "1e309", "-1e309",
                "word", "0x1", "1.2.3", "1/2" })
            {
                string[] args = { "halfpipe", "17.375", "-2.625", "-8.0625", "37.25" };
                args[component] = invalid;
                RequestBad(request, args);
            }
            foreach (string invalid in component == 4 ? new[] { "1000000.000000001", "-1000000.000000001" } :
                new[] { "100000.0000000001", "-100000.0000000001" })
            {
                string[] args = { "halfpipe", "17.375", "-2.625", "-8.0625", "37.25" };
                args[component] = invalid;
                RequestBad(request, args);
            }
        }
    }

    private static object[] Request(MethodInfo request, string[] args, bool expected)
    {
        object[] outputs = { args, (SkateParkKind)999, true, 91d, 92d, 93d, 94d, "unchanged" };
        Require((bool)request.Invoke(null, outputs) == expected, "Placement request acceptance differs from its contract.");
        return outputs;
    }

    private static void RequestGood(MethodInfo request, string[] args, SkateParkKind kind, bool explicitAnchor,
                                    double x, double y, double z, double yaw)
    {
        object[] outputs = Request(request, args, true);
        Require((SkateParkKind)outputs[1] == kind && (bool)outputs[2] == explicitAnchor, "Placement request kind/mode changed.");
        Near((double)outputs[3], x, "Placement request X.");
        Near((double)outputs[4], y, "Placement request Y.");
        Near((double)outputs[5], z, "Placement request Z.");
        Near((double)outputs[6], yaw, "Placement request normalized yaw.");
        Require((double)outputs[6] >= 0 && (double)outputs[6] < 360, "Placement request yaw must be canonical.");
        Require(string.IsNullOrEmpty(outputs[7] as string), "Accepted placement request must not report failure.");
    }

    private static void RequestBad(MethodInfo request, string[] args)
    {
        object[] outputs = Request(request, args, false);
        Require((SkateParkKind)outputs[1] == default(SkateParkKind) && !(bool)outputs[2], "Refused placement request must reset kind/mode atomically.");
        for (int i = 3; i <= 6; i++) Require((double)outputs[i] == 0, "Refused placement request must reset every numeric output atomically.");
        Require(!string.IsNullOrEmpty(outputs[7] as string), "Refused placement request needs a failure reason.");
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
        HalfpipeWallSpans(x, y, z, yaw);
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
        Same(WorldEdge(kind, 16, 0, -1.5, x, y, z, yaw), WorldEdge(kind, 17, 0, 1.5, x, y, z, yaw),
             "halfpipe: joined back rail widths.");
        Same(WorldEdge(kind, 18, 0, 1.5, x, y, z, yaw), WorldEdge(kind, 19, 0, -1.5, x, y, z, yaw),
             "halfpipe: joined front rail widths.");
        for (int station = 0; station < 8; station++)
        {
            Same(Centre(kind, 20 + station * 2, x, y, z, yaw),
                 WorldEdge(kind, station * 2, -1.85, 0, x, y, z, yaw), "halfpipe: exterior left side panel.");
            Same(Centre(kind, 21 + station * 2, x, y, z, yaw),
                 WorldEdge(kind, station * 2 + 1, 1.85, 0, x, y, z, yaw), "halfpipe: exterior right side panel.");
        }
    }

    private static void HalfpipeWallSpans(double x, double y, double z, double yaw)
    {
        double angle = yaw * Math.PI / 180, cos = Math.Cos(angle), sin = Math.Sin(angle);
        for (int i = 16; i < 36; i++)
        {
            Point centre = Centre(SkateParkKind.Halfpipe, i, x, y, z, yaw);
            foreach (double thickness in new[] { -0.10000000149011612, 0.10000000149011612 })
            foreach (double length in new[] { -1.5, 1.5 })
            {
                double right, forward;
                if (i < 20)
                {
                    right = i < 18 ? -length : length;
                    forward = i < 18 ? thickness : -thickness;
                }
                else
                {
                    right = i % 2 == 0 ? -thickness : thickness;
                    forward = i % 2 == 0 ? -length : length;
                }
                Same(WorldEdge(SkateParkKind.Halfpipe, i, thickness, length, x, y, z, yaw),
                    new Point(centre.X + right * cos + forward * sin, centre.Y,
                              centre.Z - right * sin + forward * cos),
                    "halfpipe: native thin-X long-Z wall footprint.");
            }
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

    private static void FoundationSupport()
    {
        int start = checks;
        foreach (Piece piece in Pieces)
        foreach (double top in new[] { -17.25, 0.5, 123.875 })
        foreach (double centerY in new[] { -0.05, 0.1 })
        foreach (double centerZ in new[] { -0.025, 0.025 })
        foreach (double thickness in new[] { 0.1, 0.25 })
        foreach (double depth in new[] { 2.9, 3.0, 3.1 })
        {
            double baseY;
            Require(SkateParkLayout.TrySupportedAnchor(piece.Kind, top, centerY, centerZ, thickness, depth, out baseY),
                    piece.Name + ": foundation bounds refused.");
            Require(baseY >= top - centerY + thickness / 2 + 0.025 - Tolerance,
                    piece.Name + ": upright floor base is below support.");
            foreach (ExpectedPart part in piece.Parts)
            {
                if (part.Prefab != FloorPrefab) continue;
                double pitch = part.Pitch * Math.PI / 180;
                foreach (double localY in new[] { centerY - thickness / 2, centerY + thickness / 2 })
                foreach (double localZ in new[] { centerZ - depth / 2, centerZ + depth / 2 })
                {
                    double worldY = baseY + part.Height + localY * Math.Cos(pitch) - localZ * Math.Sin(pitch);
                    Require(worldY >= top + 0.025 - Tolerance, piece.Name + ": tilted floor corner intersects support.");
                }
            }
            double shifted;
            Require(SkateParkLayout.TrySupportedAnchor(piece.Kind, top + 7.25, centerY, centerZ, thickness, depth, out shifted),
                    piece.Name + ": translated support refused.");
            Near(shifted - baseY, 7.25, piece.Name + ": support height translation.");
        }
        double y;
        Require(!SkateParkLayout.TrySupportedAnchor((SkateParkKind)99, 0, 0, 0, 0.2, 3, out y), "unknown support kind.");
        foreach (double bad in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity, 100001 })
            Require(!SkateParkLayout.TrySupportedAnchor(SkateParkKind.Halfpipe, bad, 0, 0, 0.2, 3, out y), "invalid support top.");
        Require(!SkateParkLayout.TrySupportedAnchor(SkateParkKind.Bank, 0, 0, 0, 0, 3, out y), "zero floor thickness.");
        Require(!SkateParkLayout.TrySupportedAnchor(SkateParkKind.Bank, 0, 0, 0, 0.2, -3, out y), "negative floor depth.");
        Console.WriteLine("PASS: skate_park_anchor_yaw_and_snap foundation-bound extension (" +
                          (checks - start).ToString(CultureInfo.InvariantCulture) + " checks).");
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
