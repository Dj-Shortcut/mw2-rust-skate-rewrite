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
            new ExpectedPart(4.85, 11.35523361173418, 2.4971864443607004, 0, SkateParkMaterial.Metal, HalfWallPrefab, 0)),
        new Piece(SkateParkKind.MiniQuarterPipe, "mini-quarterpipe",
            new ExpectedPart(0, 0, 0, 0),
            new ExpectedPart(0, 1.15, 0, 0, SkateParkMaterial.Stone, LowWallPrefab, 90)),
        new Piece(SkateParkKind.LargeQuarterPipe, "large-quarterpipe",
            new ExpectedPart(0, 0, 0, 0), new ExpectedPart(3, 0, 0, 0), new ExpectedPart(0, 3, 0, 0))
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
            MegaConfiguration();
            Console.WriteLine("PASS: both approved park layout scenarios (" + checks.ToString(CultureInfo.InvariantCulture) + " checks).");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static void MegaConfiguration()
    {
        const double radius = 9.588679832249493;
        double[] run = { 0, 2.963065021785413, 5.636084594350517, 7.757404937910159, 9.119376437128798, radius };
        double[] rise = { 0, 0.46930339512069297, 1.8312748943393327, 3.9525952378989753, 6.625614810464081, radius };
        var expected = new ExpectedPart[80];
        for (int i = 0; i < 16; i++) expected[i] = new ExpectedPart(i % 4 * 3, -4.5 + i / 4 * 3, 0, 0);
        for (int i = 16; i < 56; i++)
        {
            int segment = (i - 16) % 20 / 4, side = i < 36 ? -1 : 1;
            expected[i] = new ExpectedPart(i % 4 * 3, side * (6 + (run[segment] + run[segment + 1]) / 2),
                (rise[segment] + rise[segment + 1]) / 2, -side * (9 + 18 * segment), SkateParkMaterial.Metal);
        }
        for (int i = 56; i < 80; i++)
        {
            int side = i / 4 % 2 == 0 ? -1 : 1;
            expected[i] = i < 64 ? new ExpectedPart(i % 4 * 3, side * (7.5 + radius), radius, 0, SkateParkMaterial.Metal) :
                new ExpectedPart(i % 4 * 3, side * (6 + radius + (i < 72 ? 0 : 3) + 0.3),
                    radius - (i < 72 ? 1 : 0), 0, i < 72 ? SkateParkMaterial.Metal : SkateParkMaterial.Wood,
                    LowWallPrefab, side < 0 ? 270 : 90);
        }
        var piece = new Piece(SkateParkKind.MegaHalfpipe, "mega-halfpipe", expected);
        Require((int)piece.Kind == 11 && SkateParkLayout.Name(piece.Kind) == piece.Name, "MEGA stable appended identity.");
        Require(SkateParkLayout.PartCount(piece.Kind) == 80, "MEGA complete assembly count.");
        double visualRight, visualForward, visualHeight, visualYaw;
        Require(SkateParkLayout.VisualOrigin(piece.Kind, out visualRight, out visualForward, out visualHeight, out visualYaw), "MEGA visual origin.");
        Near(visualRight, 4.5, "MEGA model centre offset."); Near(visualForward, 0, "MEGA model forward centre.");
        Near(visualHeight, 0, "MEGA model height centre."); Near(visualYaw, 0, "MEGA model yaw.");
        SkateParkKind parsed;
        Require(SkateParkLayout.TryKind("MEGA-HALFPIPE", out parsed) && parsed == piece.Kind, "MEGA name parser.");
        foreach (double yaw in new[] { 0.0, 90, 180, 270, 37.25 })
        {
            double radians = yaw * Math.PI / 180;
            var angle = new Angle(yaw, yaw, Math.Cos(radians), Math.Sin(radians));
            var anchor = new Point(17.375, 2.625, -8.0625);
            var blocks = Observations(piece, anchor, angle);
            for (int i = 0; i < 80; i++)
            {
                SkateParkPart part; SkateParkTransform transform;
                Require(SkateParkLayout.TryPart(piece.Kind, i, out part), "MEGA native part.");
                Near(part.Right, expected[i].Right, "MEGA module width join.");
                Near(part.Forward, expected[i].Forward, "MEGA module station.");
                Near(part.Height, expected[i].Height, "MEGA module rise.");
                Near(part.Pitch, expected[i].Pitch, "MEGA module pitch.");
                Require(part.Prefab == expected[i].Prefab && part.Material == expected[i].Material, "MEGA module identity.");
                Require(SkateParkLayout.TryTransform(piece.Kind, i, anchor.X, anchor.Y, anchor.Z, yaw, out transform), "MEGA transform.");
                Near(transform.X, blocks[i].X, "MEGA world X."); Near(transform.Y, blocks[i].Y, "MEGA world Y.");
                Near(transform.Z, blocks[i].Z, "MEGA world Z.");
            }
            Array.Reverse(blocks);
            double x, y, z, matchedYaw;
            Require(SkateParkLayout.TryMatch(piece.Kind, blocks, 0.01, out x, out y, out z, out matchedYaw), "MEGA 80-member reordered recognition.");
            Near(x, anchor.X, "MEGA recovered X."); Near(y, anchor.Y, "MEGA recovered Y.");
            Near(z, anchor.Z, "MEGA recovered Z."); Near(matchedYaw, yaw, "MEGA recovered yaw.");
            for (int i = 0; i < blocks.Length; i++)
            {
                var original = blocks[i];
                blocks[i] = new SkateParkBlock(original.Prefab, original.X, original.Y + 0.11, original.Z, original.Yaw, original.Pitch);
                Refused(piece.Kind, blocks, 0.01, "MEGA shifted member accepted.");
                blocks[i] = original;
            }
            var duplicate = blocks[79]; blocks[79] = blocks[0];
            Refused(piece.Kind, blocks, 0.01, "MEGA duplicate after bit 64 accepted."); blocks[79] = duplicate;
            Array.Resize(ref blocks, 79);
            Refused(piece.Kind, blocks, 0.01, "MEGA incomplete paid assembly accepted.");
        }
        for (int i = 0; i < 5; i++)
        {
            Near(Math.Sqrt(Math.Pow(run[i + 1] - run[i], 2) + Math.Pow(rise[i + 1] - rise[i], 2)), 3, "MEGA 3m chord seam.");
            SkateParkPart a;
            Require(SkateParkLayout.TryPart(piece.Kind, 36 + i * 4, out a), "MEGA front transition.");
            double pitch = -a.Pitch * Math.PI / 180;
            Near(a.Forward - 1.5 * Math.Cos(pitch), 6 + run[i], "MEGA lower native seam station.");
            Near(a.Height - 1.5 * Math.Sin(pitch), rise[i], "MEGA lower native seam height.");
            Near(a.Forward + 1.5 * Math.Cos(pitch), 6 + run[i + 1], "MEGA upper native seam station.");
            Near(a.Height + 1.5 * Math.Sin(pitch), rise[i + 1], "MEGA upper native seam height.");
        }
        double previous = double.NegativeInfinity;
        for (int i = 0; i < 124; i++)
        {
            double f, h, mirrorF, mirrorH;
            Require(SkateMegaHalfpipe.TryProfilePoint(i, out f, out h), "MEGA client profile sample.");
            Require(f > previous, "MEGA profile ordered without duplicated seam."); previous = f;
            Require(SkateMegaHalfpipe.TryProfilePoint(123 - i, out mirrorF, out mirrorH), "MEGA mirrored profile sample.");
            Near(f, -mirrorF, "MEGA opposing station symmetry."); Near(h, mirrorH, "MEGA opposing height symmetry.");
            Require(h >= 0.05 && h <= radius + 0.050000001, "MEGA height envelope.");
        }
        double invalidF, invalidH;
        Require(!SkateMegaHalfpipe.TryProfilePoint(-1, out invalidF, out invalidH) && invalidF == 0 && invalidH == 0, "MEGA invalid profile atomic refusal.");
        Require(!SkateMegaHalfpipe.TryProfilePoint(124, out invalidF, out invalidH), "MEGA past-end profile refused.");
        Near(SkateMegaHalfpipe.Width, 12, "MEGA width parameter."); Near(SkateMegaHalfpipe.FlatLength, 12, "MEGA bottom parameter.");
        Near(SkateMegaHalfpipe.Length, 18 + 2 * radius, "MEGA full envelope length.");
        MegaOccupancy();
        Console.WriteLine("PASS: MEGA configuration within approved park layout/anchor scenarios.");
    }

    private static void MegaOccupancy()
    {
        object placer = new object(), anchor = new object(), part = new object();
        Require(!SkateParkLayout.BlocksMegaSpace(placer, placer, null, false),
            "MEGA: placing player inside otherwise empty riding envelope must not obstruct it.");
        Require(!SkateParkLayout.BlocksMegaSpace(placer, placer, anchor, false),
            "MEGA: snapped placement also excludes the placing player.");
        Require(!SkateParkLayout.BlocksMegaSpace(anchor, placer, anchor, false),
            "MEGA: this placement's support is allowed.");
        Require(!SkateParkLayout.BlocksMegaSpace(part, placer, anchor, true),
            "MEGA: this transaction's created part is allowed during revalidation.");
        foreach (string obstruction in new[] { "other player", "building", "deployable", "other park module", "World entity" })
            Require(SkateParkLayout.BlocksMegaSpace(new object(), placer, anchor, false),
                "MEGA: " + obstruction + " inside riding envelope must still block.");
        Require(SkateParkLayout.BlocksMegaSpace(part, placer, anchor, false),
            "MEGA: a part outside the current transaction must block.");
        Require(SkateParkLayout.BlocksMegaSpace(null, placer, null, false),
            "MEGA: static World collider with no entity must block even without an anchor.");
        Require(SkateParkLayout.BlocksMegaSpace(null, null, null, true),
            "MEGA: unknown World collider cannot inherit a null identity exemption.");
        Require(SkateParkLayout.BlocksMegaSpace(new object(), null, null, false),
            "MEGA: missing player and anchor do not exempt another entity.");
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
            -1.4488887394336025, 0, 0, 0, 0, 1.5, 0, 0 };
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
        OwnerParkContracts();
        HalfWallPieceLayout();
        Console.WriteLine("PASS: skate_park_piece_layouts (original, owner-quarterpipe, half-wall and MEGA fixtures, " +
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
        HalfWallSnap();
        NativeRotationAndAdjacentCells();
        Console.WriteLine("PASS: skate_park_anchor_yaw_and_snap (20 translated/yaw anchors, native socket/rotation steps and half-wall attachment, " +
                          (checks - start).ToString(CultureInfo.InvariantCulture) + " checks).");
    }

    private static void NativeRotationAndAdjacentCells()
    {
        double[] offsets = { -136, -135, -134, -46, -45, -44, 0, 44, 45, 46, 134, 135, 136 };
        double[] snapped = { -180, -180, -90, -90, -90, 0, 0, 0, 90, 90, 90, 180, 180 };
        foreach (double reference in new[] { 0d, 90, 180, 270, 37.25, -90, 450, -720 })
        for (int i = 0; i < offsets.Length; i++)
        {
            double yaw;
            Require(SkateParkLayout.TrySnapYaw(reference + offsets[i], reference, 90, out yaw),
                "native rotation: valid relative heading refused.");
            double expected = (reference + snapped[i]) % 360;
            if (expected < 0) expected += 360;
            Near(yaw, expected, "native rotation: nearest quarter turn in support frame, ties away from zero.");
            Require(yaw >= 0 && yaw < 360, "native rotation: normalized result.");
        }
        foreach (double step in new[] { 30d, 45, 60, 120, 180, 360 })
        {
            double yaw;
            Require(SkateParkLayout.TrySnapYaw(37.25 + step, 37.25, step, out yaw),
                "native rotation: integral full-turn subdivision refused.");
            Near(yaw, (37.25 + step) % 360, "native rotation: configured step retained.");
        }
        foreach (double bad in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity, -1000001d, 1000001 })
        {
            SnapYawRefused(bad, 0, 90, "native rotation: invalid requested heading.");
            SnapYawRefused(0, bad, 90, "native rotation: invalid reference heading.");
        }
        foreach (double bad in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity,
            -90d, 0, 361, 70, double.Epsilon })
            SnapYawRefused(45, 0, bad, "native rotation: invalid step.");
        SnapYawRefused(1000000, -1000000, 1e-305,
            "native rotation: overflowing relative step count refuses a nonfinite result.");

        Near(SkateParkLayout.SupportTolerance, 0.02, "support: shared two-centimetre contact tolerance.");
        Angle[] angles = { new Angle(0, 0, 1, 0), new Angle(90, 90, 0, 1), new Angle(180, 180, -1, 0),
            new Angle(270, 270, 0, -1), new Angle(37.25, 37.25, 0.796002002534622, 0.6052939880428944) };
        Point[] hits = { new Point(1.49, 0, 0.2), new Point(-1.49, 0, 0.2),
            new Point(0.2, 0, 1.49), new Point(0.2, 0, -1.49), new Point(0, 0, 0),
            new Point(1.519999, 0, 0), new Point(0, 0, -1.519999) };
        Point[] cells = { new Point(3, 0, 0), new Point(-3, 0, 0), new Point(0, 0, 3),
            new Point(0, 0, -3), new Point(3, 0, 0), new Point(3, 0, 0), new Point(0, 0, -3) };
        foreach (Angle angle in angles)
        for (int i = 0; i < hits.Length; i++)
        {
            const double rx = 17.375, rz = -8.0625;
            Point hit = hits[i], cell = cells[i];
            double x, z;
            Require(SkateParkLayout.TryAdjacentCell(rx, rz, angle.Input,
                rx + hit.X * angle.Cos + hit.Z * angle.Sin,
                rz - hit.X * angle.Sin + hit.Z * angle.Cos, out x, out z),
                "foundation socket: valid hit within the reference cell refused.");
            Near(x, rx + cell.X * angle.Cos + cell.Z * angle.Sin, "foundation socket: one cell in local X.");
            Near(z, rz - cell.X * angle.Sin + cell.Z * angle.Cos, "foundation socket: one cell in local Z.");
            Near(Math.Sqrt((x - rx) * (x - rx) + (z - rz) * (z - rz)), 3,
                "foundation socket: exactly three metres from reference centre.");
        }
        foreach (double side in new[] { -1d, 1 })
        {
            double x, z;
            Require(SkateParkLayout.TryAdjacentCell(0, 0, 0, side, side, out x, out z),
                "foundation socket: diagonal tie accepted.");
            Near(x, side * 3, "foundation socket: equal local axes choose right.");
            Near(z, 0, "foundation socket: tie is not diagonal adjacency.");
            Require(SkateParkLayout.TryAdjacentCell(0, 0, 0, side * 1.52, 0, out x, out z),
                "foundation socket: contact tolerance boundary accepted.");
            Near(x, side * 3, "foundation socket: boundary keeps nearest side.");
        }
        foreach (Angle angle in angles)
        foreach (Point hit in new[] { new Point(1.520001, 0, 0), new Point(-1.520001, 0, 0),
            new Point(0, 0, 1.520001), new Point(0, 0, -1.520001), new Point(3, 0, 3) })
            AdjacentCellRefused(0, 0, angle.Input, hit.X * angle.Cos + hit.Z * angle.Sin,
                -hit.X * angle.Sin + hit.Z * angle.Cos, "foundation socket: hit beyond the reference cell.");
        foreach (double bad in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity, -100001d, 100001 })
        {
            AdjacentCellRefused(bad, 0, 0, 0, 0, "foundation socket: invalid reference X.");
            AdjacentCellRefused(0, bad, 0, 0, 0, "foundation socket: invalid reference Z.");
            AdjacentCellRefused(0, 0, 0, bad, 0, "foundation socket: invalid hit X.");
            AdjacentCellRefused(0, 0, 0, 0, bad, "foundation socket: invalid hit Z.");
        }
        foreach (double bad in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity, -1000001d, 1000001 })
            AdjacentCellRefused(0, 0, bad, 0, 0, "foundation socket: invalid reference yaw.");
        AdjacentCellRefused(99999, 0, 0, 99999, 0, "foundation socket: derived X outside world bounds.");
        AdjacentCellRefused(0, 99999, 0, 0, 100000, "foundation socket: derived Z outside world bounds.");
        double edgeX, edgeZ;
        Require(SkateParkLayout.TryAdjacentCell(99997, 0, 0, 99998, 0, out edgeX, out edgeZ),
            "foundation socket: exact world boundary remains valid.");
        Near(edgeX, 100000, "foundation socket: exact world boundary X.");
        Near(edgeZ, 0, "foundation socket: exact world boundary Z.");
    }

    private static void SnapYawRefused(double requested, double reference, double step, string message)
    {
        double yaw = 123;
        Require(!SkateParkLayout.TrySnapYaw(requested, reference, step, out yaw), message);
        Require(yaw == 0, "native rotation: refusal resets output atomically.");
    }

    private static void AdjacentCellRefused(double rx, double rz, double yaw, double hx, double hz, string message)
    {
        double x = 91, z = 93;
        Require(!SkateParkLayout.TryAdjacentCell(rx, rz, yaw, hx, hz, out x, out z), message);
        Require(x == 0 && z == 0, "foundation socket: refusal resets both outputs atomically.");
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

    // These extend the approved layout scenario with independent owner-park fixtures.
    // Native metadata, Physics queries and the client's collider replacement remain separate checks.
    private static void OwnerParkContracts()
    {
        Require(SkateParkLayout.KindCount == 12, "all original and appended kind IDs.");
        Near(SkateParkLayout.CellSize, 3, "owner park cell is exactly three metres.");
        for (int i = 0; i < Pieces.Length; i++)
            Require((int)Pieces[i].Kind == i, "persisted kind ID remains stable " + i);
        Require((int)SkateParkKind.MiniQuarterPipe == 8 && (int)SkateParkKind.LargeQuarterPipe == 9,
            "owner quarterpipes append IDs 8 and 9.");
        foreach (int raw in new[] { -1, 0, 1, 2, 3, 4, 5, 6, 7, 10, int.MaxValue })
        {
            var kind = (SkateParkKind)raw;
            Require(SkateParkLayout.CellCount(kind) == 0, "only appended parks use the cell contract " + raw);
            int right = 91, forward = 92;
            Require(!SkateParkLayout.TryCell(kind, 0, out right, out forward) && right == 0 && forward == 0,
                "unsupported cell lookup resets outputs " + raw);
            SkateParkQuarterProfile profile;
            Require(!SkateParkLayout.TryQuarterProfile(kind, out profile) && EmptyProfile(profile),
                "unsupported quarter profile resets every field " + raw);
            SurfaceRefused(kind, 0, 0, "unsupported quarter surface " + raw);
            Require(!SkateParkLayout.ContainsNativeBounds(kind, 0, 0, 0, 3, 3),
                "unsupported native-bound contract " + raw);
        }
        OwnerParkCells();
        OwnerQuarterProfiles();
        OwnerNativeBounds();
        ReservedBoxOverlaps();
    }

    private static bool EmptyProfile(SkateParkQuarterProfile value)
    {
        return value.Radius == 0 && value.Height == 0 && value.Entry == 0 && value.Deck == 0 &&
            value.StartForward == 0 && value.HalfWidth == 0 && value.EndForward == 0 &&
            value.Skin == 0 && value.RailRadius == 0 && value.RailHeight == 0;
    }

    private static Point QuarterTurn(double right, double forward, int turn)
    {
        switch (turn)
        {
            case 0: return new Point(right, 0, forward);
            case 1: return new Point(forward, 0, -right);
            case 2: return new Point(-right, 0, -forward);
            default: return new Point(-forward, 0, right);
        }
    }

    private static void OwnerParkCells()
    {
        foreach (var kind in new[] { SkateParkKind.MiniQuarterPipe, SkateParkKind.LargeQuarterPipe })
        {
            int count = kind == SkateParkKind.MiniQuarterPipe ? 1 : 3;
            Require(SkateParkLayout.CellCount(kind) == count, kind + ": exact occupied cell count.");
            int[] rights = { 0, 1, 0 }, forwards = { 0, 0, 1 };
            for (int i = 0; i < count; i++)
            {
                int right, forward;
                Require(SkateParkLayout.TryCell(kind, i, out right, out forward), kind + ": cell exists.");
                Require(right == rights[i] && forward == forwards[i], kind + ": literal local mask.");
                Require(!(right == 1 && forward == 1), kind + ": upper-right cell remains empty.");
                for (int turn = 0; turn < 4; turn++)
                {
                    Point expected = QuarterTurn(rights[i] * 3, forwards[i] * 3, turn);
                    SkateParkTransform actual = Transform(kind, i, 17.25, 2.5, -9.75, turn * 90);
                    Near(actual.X, 17.25 + expected.X, kind + ": cell quarter-turn X.");
                    Near(actual.Z, -9.75 + expected.Z, kind + ": cell quarter-turn Z.");
                    Point empty = QuarterTurn(3, 3, turn);
                    Require(!SkateParkLayout.OverlapsBox(actual.X, actual.Z, turn * 90, 1.5, 1.5,
                        17.25 + empty.X, -9.75 + empty.Z, turn * 90, 1.5, 1.5),
                        kind + ": complete missing cell is available at every quarter-turn.");
                    Require(!SkateParkLayout.OverlapsBox(actual.X, actual.Z, turn * 90, 1.5, 1.5,
                        17.25 + empty.X, -9.75 + empty.Z, turn * 90 + 37.25, 0.25, 0.25),
                        kind + ": rotated small obstruction wholly inside missing cell is available.");
                }
            }
            foreach (int invalid in new[] { -1, count, count + 1, int.MaxValue })
            {
                int right = 91, forward = 92;
                Require(!SkateParkLayout.TryCell(kind, invalid, out right, out forward) && right == 0 && forward == 0,
                    kind + ": invalid cell index resets both outputs.");
            }
        }
        // A shifted third tile in the missing cell must not be mistaken for the declared L.
        SkateParkBlock[] wrongL = Observations(Pieces[9], new Point(0, 0, 0), new Angle(0, 0, 1, 0));
        wrongL[2] = new SkateParkBlock(FloorPrefab, 3, 0, 3, 0, 0);
        Refused(SkateParkKind.LargeQuarterPipe, wrongL, 0.01, "different three-cell mask accepted.");
        SkateParkBlock[] mini = Observations(Pieces[8], new Point(0, 0, 0), new Angle(0, 0, 1, 0));
        Refused(SkateParkKind.QuarterPipe, mini, 0.01, "new mini mistaken for old quarterpipe.");
    }

    private static void OwnerQuarterProfiles()
    {
        foreach (var kind in new[] { SkateParkKind.MiniQuarterPipe, SkateParkKind.LargeQuarterPipe })
        {
            bool mini = kind == SkateParkKind.MiniQuarterPipe;
            double radius = mini ? 1.2 : 3, end = mini ? 1.4 : 4.4;
            SkateParkQuarterProfile profile;
            Require(SkateParkLayout.TryQuarterProfile(kind, out profile), kind + ": client profile exists.");
            Near(profile.Radius, radius, kind + ": transition radius.");
            Near(profile.Height, radius, kind + ": transition height.");
            Near(profile.Entry, 0.2, kind + ": flat entry length.");
            Near(profile.Deck, mini ? 1.4 : 2.6, kind + ": deck length.");
            Near(profile.StartForward, -1.4, kind + ": entry origin.");
            Near(profile.EndForward, end, kind + ": complete run ends inside its final cell.");
            Near(profile.HalfWidth, 1.4, kind + ": transition half width.");
            Near(profile.Skin, 0.08, kind + ": maximum skin/collider overhang.");
            Near(profile.RailRadius, 0.05, kind + ": rail radius.");
            Near(profile.RailHeight, 0.8, kind + ": rail height above deck.");
            Near(profile.StartForward + profile.Entry + profile.Radius + profile.Deck, end,
                kind + ": entry, curve and deck consume the declared length.");
            Require(profile.HalfWidth + profile.Skin <= 1.5 && profile.HalfWidth + profile.RailRadius <= 1.5 &&
                profile.StartForward - profile.Skin >= -1.5 && end + profile.Skin <= (mini ? 1.5 : 4.5),
                kind + ": authored skin, rail and collider horizontal limits fit claimed cells.");

            foreach (double right in new[] { -1.4, 0, 1.4 })
            {
                Surface(kind, right, -1.4, 0, 1, 0, "entry start");
                Surface(kind, right, -1.3, 0, 1, 0, "flat entry");
                Surface(kind, right, -1.2, 0, 1, 0, "transition start");
                Surface(kind, right, mini ? -0.48 : 0.6, mini ? 0.24 : 0.6, 0.8, -0.6,
                    "independent 3-4-5 circular point and unit normal");
                Surface(kind, right, mini ? 0 : 1.8, radius, 1, 0, "deck entry");
                Surface(kind, right, end, radius, 1, 0, "deck back edge");
            }
            SurfaceRefused(kind, -1.400001, 0, "left outer edge");
            SurfaceRefused(kind, 0, -1.400001, "before entry");
            SurfaceRefused(kind, 0, end + 0.000001, "behind deck");
            SurfaceRefused(kind, 1.400001, mini ? 0 : 3, "outside curved lane");
            foreach (double bad in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
            {
                SurfaceRefused(kind, bad, 0, "nonfinite right");
                SurfaceRefused(kind, 0, bad, "nonfinite forward");
            }
        }
        Surface(SkateParkKind.LargeQuarterPipe, 1.400001, -1.4, 0, 1, 0, "apron inner edge");
        Surface(SkateParkKind.LargeQuarterPipe, 3, 0, 0, 1, 0, "front-right apron center");
        Surface(SkateParkKind.LargeQuarterPipe, 4.4, 1.4, 0, 1, 0, "apron outer corner");
        SurfaceRefused(SkateParkKind.LargeQuarterPipe, 4.400001, 0, "outside apron width");
        SurfaceRefused(SkateParkKind.LargeQuarterPipe, 3, 1.400001, "beyond apron back edge");
        SurfaceRefused(SkateParkKind.LargeQuarterPipe, 3, 3, "missing L cell has no surface");
    }

    private static void Surface(SkateParkKind kind, double right, double forward, double expectedHeight,
        double expectedUp, double expectedForward, string label)
    {
        double height, up, normalForward;
        Require(SkateParkLayout.TryQuarterSurface(kind, right, forward, out height, out up, out normalForward),
            kind + ": surface refused " + label);
        Near(height, expectedHeight, kind + ": height " + label);
        Near(up, expectedUp, kind + ": normal up " + label);
        Near(normalForward, expectedForward, kind + ": normal forward " + label);
        Near(up * up + normalForward * normalForward, 1, kind + ": unit surface normal " + label);
    }

    private static void SurfaceRefused(SkateParkKind kind, double right, double forward, string label)
    {
        double height = 91, up = 92, normalForward = 93;
        Require(!SkateParkLayout.TryQuarterSurface(kind, right, forward, out height, out up, out normalForward),
            kind + ": invalid surface accepted " + label);
        Require(height == 0 && up == 0 && normalForward == 0, kind + ": surface refuses atomically " + label);
    }

    private static void OwnerNativeBounds()
    {
        foreach (var kind in new[] { SkateParkKind.MiniQuarterPipe, SkateParkKind.LargeQuarterPipe })
        {
            int floors = kind == SkateParkKind.MiniQuarterPipe ? 1 : 3;
            for (int index = 0; index < floors; index++)
            {
                Require(SkateParkLayout.ContainsNativeBounds(kind, index, 0, 0, 3, 3), kind + ": exact native floor fits.");
                Require(SkateParkLayout.ContainsNativeBounds(kind, index, 0.025, -0.025, 2.9, 2.9),
                    kind + ": inset native bounds with authored center offset fit.");
                Require(SkateParkLayout.ContainsNativeBounds(kind, index, 0, 0, 3f, 3.000000476837158203125f),
                    kind + ": installed floor float32 bounds fit.");
                foreach (float offset in new[] { -1e-7f, 0f, 1e-7f })
                    Require(SkateParkLayout.ContainsNativeBounds(kind, index, offset, -offset,
                        3.0000002384185791015625f, 3.000000476837158203125f),
                        kind + ": float32 size and center rounding fit.");
                Require(SkateParkLayout.ContainsNativeBounds(kind, index, 0.0005, -0.0005, 3.001, 3.001),
                    kind + ": combined size and center at one millimetre edge tolerance fit.");
                Require(!SkateParkLayout.ContainsNativeBounds(kind, index, 0.0006, 0, 3.001, 3),
                    kind + ": combined size and center beyond tolerance refused.");
                Require(!SkateParkLayout.ContainsNativeBounds(kind, index, 0, -0.0006, 3, 3.001),
                    kind + ": combined depth and center beyond tolerance refused.");
                foreach (double size in new[] { 3.0021, 3.1 })
                {
                    Require(!SkateParkLayout.ContainsNativeBounds(kind, index, 0, 0, size, 3),
                        kind + ": native floor width cannot overhang claimed cell.");
                    Require(!SkateParkLayout.ContainsNativeBounds(kind, index, 0, 0, 3, size),
                        kind + ": native floor length cannot overhang claimed cell.");
                }
                foreach (double offset in new[] { -0.0011, 0.0011 })
                {
                    Require(!SkateParkLayout.ContainsNativeBounds(kind, index, offset, 0, 3, 3),
                        kind + ": native center-right overhang refused.");
                    Require(!SkateParkLayout.ContainsNativeBounds(kind, index, 0, offset, 3, 3),
                        kind + ": native center-forward overhang refused.");
                }
            }
            foreach (int index in new[] { -1, SkateParkLayout.PartCount(kind), int.MaxValue })
                Require(!SkateParkLayout.ContainsNativeBounds(kind, index, 0, 0, 3, 3), kind + ": invalid native part.");
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                Require(!SkateParkLayout.ContainsNativeBounds(kind, 0, invalid, 0, 3, 3), "nonfinite native center-right.");
                Require(!SkateParkLayout.ContainsNativeBounds(kind, 0, 0, invalid, 3, 3), "nonfinite native center-forward.");
                Require(!SkateParkLayout.ContainsNativeBounds(kind, 0, 0, 0, invalid, 3), "nonfinite native width.");
                Require(!SkateParkLayout.ContainsNativeBounds(kind, 0, 0, 0, 3, invalid), "nonfinite native depth.");
            }
            foreach (double invalid in new[] { -3d, 0 })
            {
                Require(!SkateParkLayout.ContainsNativeBounds(kind, 0, 0, 0, invalid, 3), "nonpositive native width.");
                Require(!SkateParkLayout.ContainsNativeBounds(kind, 0, 0, 0, 3, invalid), "nonpositive native depth.");
            }
        }
        Require(SkateParkLayout.ContainsNativeBounds(SkateParkKind.MiniQuarterPipe, 1, 0, 0,
            0.20000000298023223876953125f, 3f), "installed mini low-wall float32 bounds fit.");
        Require(SkateParkLayout.ContainsNativeBounds(SkateParkKind.MiniQuarterPipe, 1, 0, 0, 0.6, 3),
            "mini rear wall rotates three-metre local-Z length across width; thickness remains in cell.");
        Require(!SkateParkLayout.ContainsNativeBounds(SkateParkKind.MiniQuarterPipe, 1, 0, 0, 0.8, 3),
            "mini rotated rear wall includes thickness overhang.");
        Require(!SkateParkLayout.ContainsNativeBounds(SkateParkKind.MiniQuarterPipe, 1, -0.1, 0, 0.6, 3),
            "mini rotated rear wall includes native local-X center offset.");
        Require(!SkateParkLayout.ContainsNativeBounds(SkateParkKind.MiniQuarterPipe, 1, 0, 0.0011, 0.6, 3),
            "mini rotated rear wall includes native local-Z center offset.");
    }

    private static void BoxOverlap(bool expected, double x, double z, double yaw, double halfRight, double halfForward,
        double otherX, double otherZ, double otherYaw, double otherHalfRight, double otherHalfForward, string label)
    {
        Require(SkateParkLayout.OverlapsBox(x, z, yaw, halfRight, halfForward,
            otherX, otherZ, otherYaw, otherHalfRight, otherHalfForward) == expected, "box overlap " + label);
        Require(SkateParkLayout.OverlapsBox(otherX, otherZ, otherYaw, otherHalfRight, otherHalfForward,
            x, z, yaw, halfRight, halfForward) == expected, "box overlap symmetric " + label);
    }

    private static void ReservedBoxOverlaps()
    {
        BoxOverlap(true, 0, 0, 0, 1.5, 1.5, 0, 0, 0, 1.5, 1.5, "same cell");
        BoxOverlap(true, 0, 0, 0, 1.5, 1.5, 2.999, 0, 0, 1.5, 1.5, "small positive overlap");
        BoxOverlap(false, 0, 0, 0, 1.5, 1.5, 3, 0, 0, 1.5, 1.5, "touching edge");
        BoxOverlap(false, 0, 0, 0, 1.5, 1.5, 3, 3, 0, 1.5, 1.5, "touching corner");
        BoxOverlap(false, 0, 0, 0, 1.5, 1.5, 0, 3.001, 0, 1.5, 1.5, "forward gap");
        BoxOverlap(true, 0, 0, 0, 1.5, 1.5, 0, 0, 37.25, 0.25, 0.5, "rotated contained obstruction");
        BoxOverlap(true, 0, 0, 0, 1.5, 1.5, 2, 0, 45, 0.5, 0.5, "rotated corner intrudes");
        BoxOverlap(false, 0, 0, 0, 1.5, 1.5, 2.3, 0, 45, 0.5, 0.5, "rotated corner clears");
        // Separation exists only on a rotated box's own axis; world-axis AABBs overlap.
        BoxOverlap(false, 0, 0, 45, 2, 0.1, 0, 0.4, 45, 2, 0.1, "parallel diagonal narrow boxes");
        for (int turn = 0; turn < 4; turn++)
        {
            Point near = QuarterTurn(2, 0, turn), far = QuarterTurn(2.3, 0, turn);
            BoxOverlap(true, 17.125, -8.5, turn * 90, 1.5, 1.5,
                17.125 + near.X, -8.5 + near.Z, 45 + turn * 90, 0.5, 0.5, "rotated/transformed overlap");
            BoxOverlap(false, 17.125, -8.5, turn * 90, 1.5, 1.5,
                17.125 + far.X, -8.5 + far.Z, 45 + turn * 90, 0.5, 0.5, "rotated/transformed gap");
        }
        for (int component = 0; component < 10; component++)
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            double[] values = { 0, 0, 0, 1.5, 1.5, 10, 10, 0, 1.5, 1.5 };
            values[component] = invalid;
            BoxOverlap(true, values[0], values[1], values[2], values[3], values[4],
                values[5], values[6], values[7], values[8], values[9], "nonfinite input fails closed " + component);
        }
        foreach (double invalid in new[] { -1d, 0 })
        {
            BoxOverlap(true, 0, 0, 0, invalid, 1.5, 10, 10, 0, 1.5, 1.5, "nonpositive width fails closed");
            BoxOverlap(true, 0, 0, 0, 1.5, invalid, 10, 10, 0, 1.5, 1.5, "nonpositive depth fails closed");
        }
        BoxOverlap(true, 100001, 0, 0, 1.5, 1.5, 0, 0, 0, 1.5, 1.5, "out-of-world coordinate fails closed");
        BoxOverlap(true, 0, 0, 1000001, 1.5, 1.5, 10, 10, 0, 1.5, 1.5, "out-of-range heading fails closed");
    }


    // Extension of the two existing named scenarios. The original eight fixtures above
    // remain literal compatibility evidence; attachment joins are checked from independently
    // transformed collider surface points rather than by reproducing the anchor solver.
    private static void HalfWallPieceLayout()
    {
        Require((int)SkateParkKind.HalfWallKicker == 10, "half-wall: stable appended kind ID.");
        Require(SkateParkLayout.Name(SkateParkKind.HalfWallKicker) == "half-wall-kicker", "half-wall: canonical name.");
        SkateParkKind parsed;
        Require(SkateParkLayout.TryKind("HALF-WALL-KICKER", out parsed) && parsed == SkateParkKind.HalfWallKicker,
            "half-wall: case-insensitive kind selection.");
        Require(SkateParkLayout.PartCount(SkateParkKind.HalfWallKicker) == 1,
            "half-wall: only the added floor belongs to the park assembly.");
        SkateParkPart part;
        Require(SkateParkLayout.TryPart(SkateParkKind.HalfWallKicker, 0, out part), "half-wall: missing floor.");
        Near(part.Right, 0, "half-wall: no local right offset.");
        Near(part.Forward, 0, "half-wall: no local forward offset.");
        Near(part.Height, 0, "half-wall: solved anchor is the floor origin.");
        Near(part.Pitch, -30, "half-wall: independent 30 degree riding slope.");
        Near(part.Yaw, 0, "half-wall: solved heading is preserved.");
        Require(part.Prefab == FloorPrefab && part.Material == SkateParkMaterial.Stone,
            "half-wall: original native floor material/prefab contract.");
        foreach (int raw in new[] { 12, 13 })
        {
            var reserved = (SkateParkKind)raw;
            Require(SkateParkLayout.Name(reserved) == null && SkateParkLayout.PartCount(reserved) == 0,
                "half-wall: other module kind IDs remain reserved.");
            Require(!SkateParkLayout.TryPart(reserved, 0, out part) && part.Right == 0 && part.Forward == 0 &&
                part.Height == 0 && part.Pitch == 0 && part.Yaw == 0 && part.Prefab == null,
                "half-wall: reserved kind rejects part atomically.");
        }
        foreach (int index in new[] { -1, 1, 2, int.MinValue, int.MaxValue })
            Require(!SkateParkLayout.TryPart(SkateParkKind.HalfWallKicker, index, out part) &&
                part.Right == 0 && part.Forward == 0 && part.Height == 0 && part.Pitch == 0 &&
                part.Yaw == 0 && part.Prefab == null, "half-wall: invalid part index rejects atomically.");
        double baseY = 123;
        Require(!SkateParkLayout.TrySupportedAnchor(SkateParkKind.HalfWallKicker, 0, 0, 0, 0.2, 3, out baseY) && baseY == 0,
            "half-wall: a generic terrain/foundation anchor cannot bypass the required wall target.");
    }

    private static SkateParkBox Box(double x, double y, double z, double sx, double sy, double sz)
    { return new SkateParkBox { X = x, Y = y, Z = z, SizeX = sx, SizeY = sy, SizeZ = sz }; }

    private static Point SurfacePoint(SkateParkTransform pose, double right, double up, double forward)
    {
        double pitch = pose.Pitch * Math.PI / 180, yaw = pose.Yaw * Math.PI / 180;
        double vertical = up * Math.Cos(pitch) - forward * Math.Sin(pitch);
        double horizontal = up * Math.Sin(pitch) + forward * Math.Cos(pitch);
        return new Point(pose.X + right * Math.Cos(yaw) + horizontal * Math.Sin(yaw),
            pose.Y + vertical, pose.Z - right * Math.Sin(yaw) + horizontal * Math.Cos(yaw));
    }

    private static void HalfWallSnap()
    {
        SkateParkBox wallBox = Box(0, 0.75, 0, 0.2, 1.5, 3);
        SkateParkBox floorBox = Box(0, 0, 0, 3, 0.2, 3);
        Point[] positions = { new Point(0, 0, 0), new Point(17.375, 2.625, -8.0625),
            new Point(-23.8125, -4.375, 31.1875) };
        foreach (Point position in positions)
        foreach (double yaw in new[] { 0d, 37.25, 90, 180, 270, -90, 450, -720, 999999 })
        foreach (int side in new[] { -1, 1 })
        foreach (SkateParkBox nativeWall in new[] { wallBox, Box(0.03, 0.73, -0.02, 0.4, 1.5, 3.1) })
        foreach (SkateParkBox nativeFloor in new[] { floorBox, Box(0.025, 0.1, 0.025, 2.9, 0.25, 2.9),
            Box(-0.025, -0.05, -0.025, 2.9, 0.1, 3.1) })
        {
            var wall = new SkateParkBlock(HalfWallPrefab, position.X, position.Y, position.Z, yaw, 0);
            SkateParkTransform anchor;
            Require(SkateParkLayout.TryHalfWallAnchor(wall, nativeWall, nativeFloor, side, out anchor),
                "half-wall: supported native target refused.");
            Near(anchor.Pitch, -30, "half-wall: solved 30 degree pitch.");
            Require(anchor.Yaw >= 0 && anchor.Yaw < 360, "half-wall: canonical heading.");
            SkateParkTransform floor = Transform(SkateParkKind.HalfWallKicker, 0,
                anchor.X, anchor.Y, anchor.Z, anchor.Yaw);
            Near(floor.Pitch, anchor.Pitch, "half-wall: layout agrees with attachment pitch.");
            var wallPose = new SkateParkTransform { X = wall.X, Y = wall.Y, Z = wall.Z, Yaw = wall.Yaw };
            foreach (double across in new[] { -nativeFloor.SizeX / 2, 0, nativeFloor.SizeX / 2 })
            {
                Point exit = SurfacePoint(floor, nativeFloor.X + across, nativeFloor.Y + nativeFloor.SizeY / 2,
                    nativeFloor.Z + nativeFloor.SizeZ / 2);
                Point join = SurfacePoint(wallPose, nativeWall.X + side * nativeWall.SizeX / 2,
                    nativeWall.Y + nativeWall.SizeY / 2, nativeWall.Z + side * across);
                Same(exit, join, "half-wall: complete high riding edge meets the selected wall face/top.");
                Point entry = SurfacePoint(floor, nativeFloor.X + across, nativeFloor.Y + nativeFloor.SizeY / 2,
                    nativeFloor.Z - nativeFloor.SizeZ / 2);
                Near(exit.Y - entry.Y, nativeFloor.SizeZ / 2,
                    "half-wall: independent half-length rise at 30 degrees.");
                Near(Math.Sqrt((exit.X - entry.X) * (exit.X - entry.X) +
                    (exit.Z - entry.Z) * (exit.Z - entry.Z)), nativeFloor.SizeZ * Math.Sqrt(3) / 2,
                    "half-wall: independent projected horizontal run.");
                double wallAngle = yaw * Math.PI / 180;
                double entryRight = (entry.X - wall.X) * Math.Cos(wallAngle) -
                    (entry.Z - wall.Z) * Math.Sin(wallAngle);
                Require(side * (entryRight - nativeWall.X) > nativeWall.SizeX / 2,
                    "half-wall: entry extends outward on the selected side.");
            }
            SkateParkTransform again;
            Require(SkateParkLayout.TryHalfWallAnchor(wall, nativeWall, nativeFloor, side, out again) &&
                anchor.X == again.X && anchor.Y == again.Y && anchor.Z == again.Z &&
                anchor.Yaw == again.Yaw && anchor.Pitch == again.Pitch, "half-wall: deterministic repeated snap.");
            var observed = new[] { new SkateParkBlock(FloorPrefab, floor.X, floor.Y, floor.Z, floor.Yaw, floor.Pitch) };
            double x, y, z, heading;
            Require(SkateParkLayout.TryMatch(SkateParkKind.HalfWallKicker, observed, 0.01,
                out x, out y, out z, out heading), "half-wall: placed floor round-trips through matching.");
            Near(x, anchor.X, "half-wall: recovered anchor X."); Near(y, anchor.Y, "half-wall: recovered anchor Y.");
            Near(z, anchor.Z, "half-wall: recovered anchor Z."); Near(heading, anchor.Yaw, "half-wall: recovered heading.");
        }
        HalfWallInvalidTargets(wallBox, floorBox);
    }

    private static void HalfWallRefused(SkateParkBlock wall, SkateParkBox wallBox, SkateParkBox floorBox,
        int side, string reason)
    {
        var anchor = new SkateParkTransform { X = 91, Y = 92, Z = 93, Yaw = 94, Pitch = 95 };
        Require(!SkateParkLayout.TryHalfWallAnchor(wall, wallBox, floorBox, side, out anchor), reason);
        Require(anchor.X == 0 && anchor.Y == 0 && anchor.Z == 0 && anchor.Yaw == 0 && anchor.Pitch == 0,
            "half-wall: refusal resets the whole output atomically.");
    }

    private static SkateParkBox ChangedBox(SkateParkBox box, int field, double value)
    {
        switch (field)
        {
            case 0: box.X = value; break; case 1: box.Y = value; break; case 2: box.Z = value; break;
            case 3: box.SizeX = value; break; case 4: box.SizeY = value; break; case 5: box.SizeZ = value; break;
        }
        return box;
    }

    private static void HalfWallInvalidTargets(SkateParkBox wallBox, SkateParkBox floorBox)
    {
        var wall = new SkateParkBlock(HalfWallPrefab, 0, 0, 0, 0, 0);
        foreach (int side in new[] { int.MinValue, -2, 0, 2, int.MaxValue })
            HalfWallRefused(wall, wallBox, floorBox, side, "half-wall: side must be exactly minus or plus one.");
        foreach (string prefab in new[] { null, "", FloorPrefab, LowWallPrefab, HalfWallPrefab.ToUpperInvariant(), HalfWallPrefab + " " })
            HalfWallRefused(new SkateParkBlock(prefab, 0, 0, 0, 0, 0), wallBox, floorBox, 1,
                "half-wall: target must have the exact half-wall prefab.");
        foreach (double bad in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
        {
            HalfWallRefused(new SkateParkBlock(HalfWallPrefab, bad, 0, 0, 0, 0), wallBox, floorBox, 1, "half-wall: invalid target X.");
            HalfWallRefused(new SkateParkBlock(HalfWallPrefab, 0, bad, 0, 0, 0), wallBox, floorBox, 1, "half-wall: invalid target Y.");
            HalfWallRefused(new SkateParkBlock(HalfWallPrefab, 0, 0, bad, 0, 0), wallBox, floorBox, 1, "half-wall: invalid target Z.");
            HalfWallRefused(new SkateParkBlock(HalfWallPrefab, 0, 0, 0, bad, 0), wallBox, floorBox, 1, "half-wall: invalid target yaw.");
            HalfWallRefused(new SkateParkBlock(HalfWallPrefab, 0, 0, 0, 0, bad), wallBox, floorBox, 1, "half-wall: invalid target pitch.");
            for (int field = 0; field < 6; field++)
            {
                HalfWallRefused(wall, ChangedBox(wallBox, field, bad), floorBox, 1, "half-wall: invalid wall bound.");
                HalfWallRefused(wall, wallBox, ChangedBox(floorBox, field, bad), 1, "half-wall: invalid floor bound.");
            }
        }
        foreach (double bad in new[] { -100000.001, 100000.001 })
        {
            HalfWallRefused(new SkateParkBlock(HalfWallPrefab, bad, 0, 0, 0, 0), wallBox, floorBox, 1, "half-wall: out-of-range target X.");
            HalfWallRefused(new SkateParkBlock(HalfWallPrefab, 0, bad, 0, 0, 0), wallBox, floorBox, 1, "half-wall: out-of-range target Y.");
            HalfWallRefused(new SkateParkBlock(HalfWallPrefab, 0, 0, bad, 0, 0), wallBox, floorBox, 1, "half-wall: out-of-range target Z.");
        }
        foreach (double bad in new[] { -1000000.001, 1000000.001 })
            HalfWallRefused(new SkateParkBlock(HalfWallPrefab, 0, 0, 0, bad, 0), wallBox, floorBox, 1, "half-wall: out-of-range target yaw.");
        foreach (double bad in new[] { -30d, -0.0101, 0.0101, 90 })
            HalfWallRefused(new SkateParkBlock(HalfWallPrefab, 0, 0, 0, 0, bad), wallBox, floorBox, 1, "half-wall: tilted target refused.");
        for (int field = 3; field < 6; field++)
        foreach (double bad in new[] { -1d, 0, 100001 })
        {
            HalfWallRefused(wall, ChangedBox(wallBox, field, bad), floorBox, 1, "half-wall: invalid wall extent.");
            HalfWallRefused(wall, wallBox, ChangedBox(floorBox, field, bad), 1, "half-wall: invalid floor extent.");
        }
        foreach (SkateParkBox bad in new[] { Box(0.051, 0.75, 0, 0.2, 1.5, 3), Box(0, 0.75, 0.051, 0.2, 1.5, 3),
            Box(0, 0.801, 0, 0.2, 1.5, 3), Box(0, 0.75, 0, 0.601, 1.5, 3), Box(0, 0.75, 0, 0.2, 1.399, 3),
            Box(0, 0.75, 0, 0.2, 1.601, 3), Box(0, 0.75, 0, 0.2, 1.5, 2.899), Box(0, 0.75, 0, 0.2, 1.5, 3.101) })
            HalfWallRefused(wall, bad, floorBox, 1, "half-wall: unsupported host geometry.");
        foreach (SkateParkBox bad in new[] { Box(0.051, 0, 0, 3, 0.2, 3), Box(0, 0, 0.051, 3, 0.2, 3),
            Box(0, 0.501, 0, 3, 0.2, 3), Box(0, 0, 0, 2.899, 0.2, 3), Box(0, 0, 0, 3.003, 0.2, 3),
            Box(0, 0, 0, 3, 1.001, 3), Box(0, 0, 0, 3, 0.2, 2.899), Box(0, 0, 0, 3, 0.2, 3.101) })
            HalfWallRefused(wall, wallBox, bad, 1, "half-wall: unsupported floor geometry or wall-width overhang.");
        HalfWallRefused(new SkateParkBlock(HalfWallPrefab, 100000, 0, 0, 0, 0), wallBox, floorBox, 1,
            "half-wall: derived anchor X exceeds world bounds.");
        HalfWallRefused(new SkateParkBlock(HalfWallPrefab, -100000, 0, 0, 0, 0), wallBox, floorBox, -1,
            "half-wall: derived negative anchor X exceeds world bounds.");
        HalfWallRefused(new SkateParkBlock(HalfWallPrefab, 0, 100000, 0, 0, 0), wallBox, floorBox, 1,
            "half-wall: derived anchor Y exceeds world bounds.");
    }

    private static void FoundationSupport()
    {
        int start = checks;
        foreach (Piece piece in SupportedPieces())
        foreach (double top in new[] { -17.25, 0.5, 123.875 })
        foreach (double centerY in new[] { -0.05, 0.1 })
        foreach (double centerZ in new[] { -0.025, 0.025 })
        foreach (double thickness in new[] { 0.1, 0.25 })
        foreach (double depth in new[] { 2.9, 3.0, 3.1 })
        {
            double baseY;
            Require(SkateParkLayout.TrySupportedAnchor(piece.Kind, top, centerY, centerZ, thickness, depth, out baseY),
                    piece.Name + ": foundation bounds refused.");
            double lowest = double.PositiveInfinity;
            foreach (ExpectedPart part in piece.Parts)
            {
                if (part.Prefab != FloorPrefab) continue;
                double pitch = part.Pitch * Math.PI / 180;
                foreach (double localY in new[] { centerY - thickness / 2, centerY + thickness / 2 })
                foreach (double localZ in new[] { centerZ - depth / 2, centerZ + depth / 2 })
                {
                    double worldY = baseY + part.Height + localY * Math.Cos(pitch) - localZ * Math.Sin(pitch);
                    Require(worldY >= top - Tolerance, piece.Name + ": actual floor corner intersects support.");
                    lowest = Math.Min(lowest, worldY);
                }
            }
            Near(lowest, top, piece.Name + ": lowest actual floor corner meets support exactly, with no hover gap.");
            double shifted;
            Require(SkateParkLayout.TrySupportedAnchor(piece.Kind, top + 7.25, centerY, centerZ, thickness, depth, out shifted),
                    piece.Name + ": translated support refused.");
            Near(shifted - baseY, 7.25, piece.Name + ": support height translation.");
        }
        foreach (SkateParkKind kind in new[] { SkateParkKind.LedgeLow, SkateParkKind.LedgeHigh, SkateParkKind.LongLedge })
        {
            double y;
            Require(SkateParkLayout.TrySupportedAnchor(kind, 0, 0, 0, 0.2, 3, out y), "ledge: nominal support refused.");
            Near(y, kind == SkateParkKind.LedgeHigh ? -0.7 : -0.3,
                "ledge: authored 0.4/0.8m height is compensated instead of hovering over a phantom upright floor.");
        }
        SupportedAnchorRefused((SkateParkKind)99, 0, 0, 0, 0.2, 3, "unknown support kind.");
        SupportedAnchorRefused(SkateParkKind.HalfWallKicker, 0, 0, 0, 0.2, 3,
            "half-wall: generic support cannot bypass wall attachment.");
        foreach (double bad in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity, -100001d, 100001 })
        {
            SupportedAnchorRefused(SkateParkKind.Halfpipe, bad, 0, 0, 0.2, 3, "invalid support top.");
            SupportedAnchorRefused(SkateParkKind.Halfpipe, 0, bad, 0, 0.2, 3, "invalid native floor centre Y.");
            SupportedAnchorRefused(SkateParkKind.Halfpipe, 0, 0, bad, 0.2, 3, "invalid native floor centre Z.");
            SupportedAnchorRefused(SkateParkKind.Halfpipe, 0, 0, 0, bad, 3, "invalid native floor thickness.");
            SupportedAnchorRefused(SkateParkKind.Halfpipe, 0, 0, 0, 0.2, bad, "invalid native floor depth.");
        }
        foreach (double bad in new[] { 0d, -0.2 })
        {
            SupportedAnchorRefused(SkateParkKind.Bank, 0, 0, 0, bad, 3, "nonpositive floor thickness.");
            SupportedAnchorRefused(SkateParkKind.Bank, 0, 0, 0, 0.2, bad, "nonpositive floor depth.");
        }
        SupportedAnchorRefused(SkateParkKind.Bank, 100000, 0, 0, 0.2, 3,
            "derived support anchor above world range.");
        SupportedAnchorRefused(SkateParkKind.LedgeLow, -100000, 0, 0, 0.2, 3,
            "derived ledge anchor below world range.");
        SupportPlanes();
        SupportSamples();
        Console.WriteLine("PASS: skate_park_anchor_yaw_and_snap foundation-bound extension (" +
                          (checks - start).ToString(CultureInfo.InvariantCulture) + " checks).");
    }

    private static Piece[] SupportedPieces()
    {
        // The MEGA fixture lists its 64 actual floor members. Its sixteen wall/rail
        // members are not horizontal supports and are separately refused below.
        double[] run = { 0, 2.963065021785413, 5.636084594350517, 7.757404937910159, 9.119376437128798, 9.588679832249493 };
        double[] rise = { 0, 0.46930339512069297, 1.8312748943393327, 3.9525952378989753, 6.625614810464081, 9.588679832249493 };
        var floors = new ExpectedPart[64];
        for (int i = 0; i < 16; i++) floors[i] = new ExpectedPart(i % 4 * 3, -4.5 + i / 4 * 3, 0, 0);
        for (int i = 16; i < 56; i++)
        {
            int segment = (i - 16) % 20 / 4, side = i < 36 ? -1 : 1;
            floors[i] = new ExpectedPart(i % 4 * 3, side * (6 + (run[segment] + run[segment + 1]) / 2),
                (rise[segment] + rise[segment + 1]) / 2, -side * (9 + 18 * segment), SkateParkMaterial.Metal);
        }
        for (int i = 56; i < 64; i++)
            floors[i] = new ExpectedPart(i % 4 * 3, (i < 60 ? -1 : 1) * 17.088679832249493,
                9.588679832249493, 0, SkateParkMaterial.Metal);
        var pieces = new Piece[Pieces.Length + 1];
        Array.Copy(Pieces, pieces, Pieces.Length);
        pieces[Pieces.Length] = new Piece(SkateParkKind.MegaHalfpipe, "mega-halfpipe", floors);
        return pieces;
    }

    private static void SupportedAnchorRefused(SkateParkKind kind, double top, double cy, double cz,
        double sy, double sz, string message)
    {
        double y = 92;
        Require(!SkateParkLayout.TrySupportedAnchor(kind, top, cy, cz, sy, sz, out y), message);
        Require(y == 0, "support: invalid input resets anchor atomically.");
    }

    private static void SupportPlanes()
    {
        foreach (double translation in new[] { -17.25, 0, 123.875 })
        foreach (double[] offsets in new[] { new[] { 0d }, new[] { 0d, 0, 0 },
            new[] { 0d, 0.009, 0.019 }, new[] { 0.019, 0d, 0.009 } })
        {
            var heights = new double[offsets.Length];
            double expectedTop = double.NegativeInfinity;
            for (int i = 0; i < heights.Length; i++)
            {
                heights[i] = translation + offsets[i];
                expectedTop = Math.Max(expectedTop, heights[i]);
            }
            double top;
            Require(SkateParkLayout.TrySupportPlane(heights, out top), "support plane: coplanar contacts refused.");
            Near(top, expectedTop, "support plane: highest actual contact determines top independently of order.");
            Array.Reverse(heights);
            double reversed;
            Require(SkateParkLayout.TrySupportPlane(heights, out reversed), "support plane: reversed contacts refused.");
            Near(reversed, top, "support plane: permutation leaves top unchanged.");
        }
        double boundary;
        Require(SkateParkLayout.TrySupportPlane(new[] { 0d, 0.02 }, out boundary),
            "support plane: exact two-centimetre span accepted.");
        Near(boundary, 0.02, "support plane: boundary chooses upper contact.");
        foreach (double bound in new[] { -100000d, 100000 })
        {
            Require(SkateParkLayout.TrySupportPlane(new[] { bound }, out boundary),
                "support plane: exact world boundary contact accepted.");
            Near(boundary, bound, "support plane: world boundary contact preserved.");
        }
        SupportPlaneRefused(null, "support plane: missing contact collection.");
        SupportPlaneRefused(new double[0], "support plane: no contacts cannot support an assembly.");
        foreach (double[] heights in new[] { new[] { 0d, 0.019, 0.038 }, new[] { 0.038, 0.019, 0d },
            new[] { 0.019, 0d, 0.038 }, new[] { 0d, 0.020001 }, new[] { -0.020001, 0d },
            new[] { 0d, 0.4 }, new[] { 0d, 3.1 } })
            SupportPlaneRefused(heights,
                "support plane: total height span fails even when each consecutive step could pass.");
        foreach (double bad in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity, -100001d, 100001 })
        {
            SupportPlaneRefused(new[] { bad }, "support plane: invalid single contact.");
            SupportPlaneRefused(new[] { 0d, bad, 0d }, "support plane: missing or invalid contact among valid supports.");
        }
    }

    private static void SupportPlaneRefused(double[] heights, string message)
    {
        double top = 92;
        Require(!SkateParkLayout.TrySupportPlane(heights, out top), message);
        Require(top == 0, "support plane: refusal resets output atomically.");
    }

    private static void SupportSamples()
    {
        SkateParkBox nominal = Box(0, 0, 0, 3, 0.2, 3);
        foreach (Piece piece in SupportedPieces())
        foreach (SkateParkBox floor in new[] { nominal, Box(0.025, 0.1, -0.025, 2.9, 0.25, 3.1),
            Box(-0.025, -0.05, 0.025, 3.1, 0.1, 2.9) })
        foreach (double yaw in new[] { 0d, 90, 180, 270, 37.25 })
        {
            double minimum = double.PositiveInfinity;
            foreach (ExpectedPart part in piece.Parts)
            {
                if (part.Prefab != FloorPrefab) continue;
                var localPose = new SkateParkTransform { Y = part.Height, Pitch = part.Pitch };
                foreach (double up in new[] { floor.Y - floor.SizeY / 2, floor.Y + floor.SizeY / 2 })
                foreach (double forward in new[] { floor.Z - floor.SizeZ / 2, floor.Z + floor.SizeZ / 2 })
                    minimum = Math.Min(minimum, SurfacePoint(localPose, floor.X, up, forward).Y);
            }
            double radians = yaw * Math.PI / 180, sin = Math.Sin(radians), cos = Math.Cos(radians);
            const double ax = 17.375, az = -8.0625, supportTop = 2.625;
            double anchorY;
            Require(SkateParkLayout.TrySupportedAnchor(piece.Kind, supportTop, floor.Y, floor.Z,
                floor.SizeY, floor.SizeZ, out anchorY), piece.Name + ": sample fixture support refused.");
            int contacts = 0;
            for (int i = 0; i < piece.Parts.Length; i++)
            for (int sample = 0; sample < 9; sample++)
            {
                ExpectedPart part = piece.Parts[i];
                var pose = new SkateParkTransform {
                    X = ax + part.Right * cos + part.Forward * sin,
                    Y = part.Height, Z = az - part.Right * sin + part.Forward * cos,
                    Yaw = yaw + part.Yaw, Pitch = part.Pitch };
                Point expected = SurfacePoint(pose, floor.X + (sample % 3 - 1) * floor.SizeX / 2,
                    floor.Y - floor.SizeY / 2, floor.Z + (sample / 3 - 1) * floor.SizeZ / 2);
                bool contact = part.Prefab == FloorPrefab && Math.Abs(expected.Y - minimum) <= 1e-6;
                SkateParkTransform point = new SkateParkTransform { X = 91, Y = 92, Z = 93, Yaw = 94, Pitch = 95 };
                bool accepted = SkateParkLayout.TrySupportSample(piece.Kind, i, sample, floor, ax, az, yaw, out point);
                Require(accepted == contact, piece.Name + ": only actual global-lowest floor samples can claim contact.");
                if (!contact)
                {
                    Require(EmptyTransform(point), piece.Name + ": noncontact sample refuses atomically.");
                    continue;
                }
                contacts++;
                Same(new Point(point.X, point.Y, point.Z), expected,
                    piece.Name + ": support sample world XZ and local underside Y match the independent geometry.");
                Near(anchorY + point.Y, supportTop, piece.Name + ": returned local underside meets support after translation.");
                Require(point.Yaw == 0 && point.Pitch == 0, piece.Name + ": a support sample is a point, without orientation.");
            }
            Require(contacts > 0, piece.Name + ": every supported assembly exposes at least one real contact sample.");
        }
        foreach (int index in new[] { 64, 65, 70, 71, 72, 79 })
            SupportSampleRefused(SkateParkKind.MegaHalfpipe, index, 4, nominal, 0, 0, 0,
                "MEGA walls and rails cannot masquerade as floor supports.");
        foreach (SkateParkKind kind in new[] { (SkateParkKind)(-1), (SkateParkKind)99, SkateParkKind.HalfWallKicker })
            SupportSampleRefused(kind, 0, 0, nominal, 0, 0, 0, "unsupported generic sample kind.");
        foreach (int index in new[] { -1, 1, int.MinValue, int.MaxValue })
            SupportSampleRefused(SkateParkKind.LedgeLow, index, 0, nominal, 0, 0, 0, "invalid support part index.");
        foreach (int sample in new[] { -1, 9, int.MinValue, int.MaxValue })
            SupportSampleRefused(SkateParkKind.LedgeLow, 0, sample, nominal, 0, 0, 0, "invalid support grid index.");
        for (int field = 0; field < 6; field++)
        foreach (double bad in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
            SupportSampleRefused(SkateParkKind.LedgeLow, 0, 4, ChangedBox(nominal, field, bad), 0, 0, 0,
                "nonfinite support sample bounds field " + field);
        for (int field = 0; field < 3; field++)
        foreach (double bad in new[] { -100001d, 100001 })
            SupportSampleRefused(SkateParkKind.LedgeLow, 0, 4, ChangedBox(nominal, field, bad), 0, 0, 0,
                "out-of-range support sample bounds centre " + field);
        for (int field = 3; field < 6; field++)
        foreach (double bad in new[] { 0d, -0.1 })
            SupportSampleRefused(SkateParkKind.LedgeLow, 0, 4, ChangedBox(nominal, field, bad), 0, 0, 0,
                "nonpositive support sample bounds size " + field);
        foreach (double bad in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity, -100001d, 100001 })
        {
            SupportSampleRefused(SkateParkKind.LedgeLow, 0, 4, nominal, bad, 0, 0, "invalid support sample X.");
            SupportSampleRefused(SkateParkKind.LedgeLow, 0, 4, nominal, 0, bad, 0, "invalid support sample Z.");
        }
        foreach (double bad in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity, -1000001d, 1000001 })
            SupportSampleRefused(SkateParkKind.LedgeLow, 0, 4, nominal, 0, 0, bad, "invalid support sample yaw.");
        SupportSampleRefused(SkateParkKind.LedgeLow, 0, 8, nominal, 100000, 0, 0,
            "support sample derived X exceeds world range.");
        SupportSampleRefused(SkateParkKind.LedgeLow, 0, 8, nominal, 0, 100000, 0,
            "support sample derived Z exceeds world range.");
    }

    private static bool EmptyTransform(SkateParkTransform value)
    { return value.X == 0 && value.Y == 0 && value.Z == 0 && value.Yaw == 0 && value.Pitch == 0; }

    private static void SupportSampleRefused(SkateParkKind kind, int index, int sample, SkateParkBox floor,
        double x, double z, double yaw, string message)
    {
        var point = new SkateParkTransform { X = 91, Y = 92, Z = 93, Yaw = 94, Pitch = 95 };
        Require(!SkateParkLayout.TrySupportSample(kind, index, sample, floor, x, z, yaw, out point), message);
        Require(EmptyTransform(point), "support: refusal resets the whole sample atomically.");
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
