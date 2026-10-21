// The authored park pieces (the ramp pack in assets/authored) drawn over the server's own park
// assemblies. The server builds a piece from native floor tiles and walls (ShortcutSkate's
// SkateParkLayout); this finds those assemblies among the building blocks the client knows,
// recognises the kind from the tiles' pitches and spacing, and puts the matching model over them:
// the model's Visual scene drawn, its CollisionProxies scene as static colliders for the ride,
// 2 cm above the native tiles so the two surfaces never fight. Nothing is sent or received: the
// native blocks are the server's collision and persistence, the model is what this client sees.
using System;
using System.Collections.Generic;
using UnityEngine;
using BepInEx;
using Shortcut.RustMod;

public static class SkateParks
{
    public const float ScanSeconds = 3f, Lift = 0.02f, PositionTolerance = 0.15f, AngleTolerance = 6f;
    public const string FloorPrefab = "assets/prefabs/building core/floor/floor.prefab";
    public const string LowWallPrefab = "assets/prefabs/building core/wall.low/wall.low.prefab";
    public const string HalfWallPrefab = "assets/prefabs/building core/wall.half/wall.half.prefab";
    public const string FoundationPrefab = "assets/prefabs/building core/foundation/foundation.prefab";
    public static bool Enabled = true;
    // How far a native floor tile's bottom lies below its own origin, read from the last floor seen
    // (the picker lifts an explicit anchor by it so the first tile clears the ground); a guess until then.
    public static float FloorBottom = -0.1f;
    // How far its top lies above its origin, read the same way (the riding datum of a flat floor).
    public static float FloorTop = 0.1f;
    public static int Blocks, Assemblies, Drawn, Colliders, HiddenRenderers, Scans;
    public static string Status = "", Error = "";
    private static float nextScan;
    private static readonly Dictionary<string, Loaded> models = new Dictionary<string, Loaded>();
    private static readonly List<Placed> placed = new List<Placed>();
    private static Material plain;
    private static void Say(string m) { Out.Say("PARK " + m); }

    // The kinds, by their native layout: the floor tiles (right, forward, height, pitch), the walls
    // of a halfpipe, and where the model's origin sits on the tiles. Mirrors ShortcutSkate's
    // SkateParkLayout until the shared SkatePark module lands; the numbers are the plugin's.
    public enum Kind { Bank = 0, Kicker = 1, QuarterPipe = 2, LedgeLow = 3, LedgeHigh = 4, LongLedge = 5, Funbox = 6, Halfpipe = 7, MiniQuarterPipe = 8, LargeQuarterPipe = 9, HalfWallKicker = 10, MegaHalfpipe = 11 }
    public const int KindCount = 12;
    // The owner's quarterpipes on the 3 m grid (#408, Codex's shared SkatePark contract): flat native
    // floors under them, the curve made here.
    public static bool IsGridQuarter(Kind k) { return k == Kind.MiniQuarterPipe || k == Kind.LargeQuarterPipe; }
    // All three quarterpipes share one shape (#470, docs/RUST-SKATE-QUARTERPIPES.md): their native
    // blocks only carry the piece; the riding surface, coping and closed shell are drawn here.
    public static bool IsQuarter(Kind k) { return k == Kind.QuarterPipe || IsGridQuarter(k); }
    public static bool IsTransition(Kind k) { return k == Kind.QuarterPipe || k == Kind.Halfpipe || IsGridQuarter(k) || k == Kind.HalfWallKicker || k == Kind.MegaHalfpipe; }
    private struct Part { public string Prefab; public double Right, Forward, Height, Pitch, Yaw; }
    private const double HalfLength = 1.5, Degrees = Math.PI / 180;
    private static readonly string[] fileStems = { "skate_bank", "skate_kicker", "skate_quarterpipe", "skate_ledge_low", "skate_ledge_high", "skate_long_ledge", "skate_funbox", "skate_halfpipe", "skate_mini_quarterpipe", "skate_large_quarterpipe", "skate_half_wall_kicker", "skate_mega_halfpipe" };
    private static Part[][] layouts;

    private static Part[] Layout(Kind kind)
    {
        if (layouts == null) { layouts = new Part[KindCount][]; for (var k = 0; k < KindCount; k++) layouts[k] = BuildLayout((Kind)k); }
        return layouts[(int)kind];
    }

    private static Part[] BuildLayout(Kind kind)
    {
        double low = HalfLength * Math.Sin(15 * Degrees);
        var parts = new List<Part>();
        Part p;
        switch (kind)
        {
            case Kind.Bank: parts.Add(new Part { Prefab = FloorPrefab, Height = HalfLength * Math.Sin(12 * Degrees), Pitch = -12 }); break;
            case Kind.Kicker: parts.Add(new Part { Prefab = FloorPrefab, Height = HalfLength * Math.Sin(25 * Degrees), Pitch = -25 }); break;
            case Kind.QuarterPipe:
                parts.Add(new Part { Prefab = FloorPrefab, Height = low, Pitch = -15 });
                parts.Add(new Part { Prefab = FloorPrefab, Height = 2 * low + HalfLength * Math.Sin(35 * Degrees), Forward = HalfLength * (Math.Cos(15 * Degrees) + Math.Cos(35 * Degrees)), Pitch = -35 });
                break;
            case Kind.LedgeLow: parts.Add(new Part { Prefab = FloorPrefab, Height = 0.4 }); break;
            case Kind.LedgeHigh: parts.Add(new Part { Prefab = FloorPrefab, Height = 0.8 }); break;
            case Kind.LongLedge:
                for (var i = 0; i < 3; i++) parts.Add(new Part { Prefab = FloorPrefab, Height = 0.4, Right = i == 0 ? 0 : i == 1 ? -3 : 3 });
                break;
            case Kind.Funbox:
                for (var i = 0; i < 3; i++)
                    parts.Add(new Part { Prefab = FloorPrefab, Height = i == 0 ? 2 * low : low,
                        Forward = i == 0 ? 0 : (i == 1 ? -1 : 1) * HalfLength * (1 + Math.Cos(15 * Degrees)), Pitch = i == 0 ? 0 : i == 1 ? -15 : 15 });
                break;
            case Kind.Halfpipe:
                for (var i = 0; i < 36; i++) { HalfpipePart(i, out p); parts.Add(p); }
                break;
            case Kind.MiniQuarterPipe:
                parts.Add(new Part { Prefab = FloorPrefab });
                parts.Add(new Part { Prefab = LowWallPrefab, Forward = 1.15, Yaw = 90 });
                break;
            case Kind.LargeQuarterPipe:
                parts.Add(new Part { Prefab = FloorPrefab });
                parts.Add(new Part { Prefab = FloorPrefab, Right = 3 });
                parts.Add(new Part { Prefab = FloorPrefab, Forward = 3 });
                break;
            // One floor at 30 degrees; its wall is the owner's, not part of the piece (KickerWall).
            case Kind.HalfWallKicker: parts.Add(new Part { Prefab = FloorPrefab, Pitch = -30 }); break;
            case Kind.MegaHalfpipe:
                for (var i = 0; i < MegaParts; i++) parts.Add(MegaPart(i));
                break;
        }
        return parts.ToArray();
    }

    private static void HalfpipePart(int index, out Part part)
    {
        part = new Part { Prefab = FloorPrefab };
        if (index < 4) { part.Right = (index % 2) * 3; part.Forward = (index / 2) * 3; return; }
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
            part.Prefab = LowWallPrefab; part.Right = (index % 2) * 3; part.Height = rise;
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

    // The MEGA halfpipe (Codex's SkateMegaHalfpipe, #460): four 3 m lanes, a 12 m flat of stone floors,
    // two transitions of five metal chords each (9 to 81 degrees) on a circle of radius MegaRadius,
    // 3 m metal decks, low walls behind the coping and at the back. Right, forward and height from
    // the first flat floor's lane, the anchor frame's +X across the lanes.
    public const int MegaParts = 80;
    public static readonly double MegaRadius = 3.0 / (2 * Math.Sin(Math.PI / 20));
    public const double MegaFlat = 12, MegaDeck = 3, MegaCentre = 4.5, MegaLift = 0.05;
    private static Part MegaPart(int index)
    {
        var part = new Part { Prefab = FloorPrefab, Right = index % 4 * 3.0 };
        double r = MegaRadius, lip = MegaFlat / 2 + r;
        if (index < 16) { part.Forward = -4.5 + index / 4 * 3.0; return part; }
        if (index < 56)
        {
            int segment = (index - 16) % 20 / 4, side = index < 36 ? -1 : 1;
            double a = segment * Math.PI / 10, b = (segment + 1) * Math.PI / 10;
            part.Forward = side * (MegaFlat / 2 + r * (Math.Sin(a) + Math.Sin(b)) / 2);
            part.Height = r * (1 - (Math.Cos(a) + Math.Cos(b)) / 2);
            part.Pitch = -side * (segment * 18 + 9);
            return part;
        }
        int deckSide = (index / 4) % 2 == 0 ? -1 : 1;
        part.Height = r; part.Forward = deckSide * (lip + MegaDeck / 2);
        if (index >= 64)
        {
            part.Prefab = LowWallPrefab; part.Yaw = deckSide < 0 ? 270 : 90;
            part.Forward = deckSide * (lip + (index < 72 ? 0 : MegaDeck) + 0.3);
            if (index < 72) part.Height -= 1;
        }
        return part;
    }

    // A building block as the client sees it.
    private struct Block
    {
        public BuildingBlock Entity; public string Prefab; public Vector3 Position; public Quaternion Rotation;
        public float Yaw, Pitch, Top, Bottom; public int Id;
    }

    private sealed class Loaded { public SkateModel Model; public Mesh[] Meshes; public Material[] Materials; public string Error; }
    private sealed class Placed
    {
        public Kind Kind; public int[] Ids; public BuildingBlock[] Entities; public GameObject Root; public Vector3 Origin; public float Yaw; public int ColliderCount;
        public readonly List<Renderer> Hidden = new List<Renderer>();
        public readonly List<Collider> Backing = new List<Collider>();
        public readonly List<int> Ramp = new List<int>();
        public readonly List<int> Coping = new List<int>();
        public readonly List<Mesh> Meshes = new List<Mesh>();
    }

    public static void Frame(float now)
    {
        if (!Enabled || now < nextScan) return;
        nextScan = now + ScanSeconds;
        try { Scan(); }
        catch (Exception e) { Error = e.GetType().Name + ": " + e.Message; Say("scan threw " + Error); nextScan = now + 15f; }
    }

    // The pieces found, for the tour check: kind, model origin and yaw.
    public static int PieceCount { get { return placed.Count; } }
    public static bool Piece(int i, out Kind kind, out Vector3 origin, out float yaw)
    {
        kind = Kind.Bank; origin = Vector3.zero; yaw = 0f;
        if (i < 0 || i >= placed.Count) return false;
        kind = placed[i].Kind; origin = placed[i].Origin; yaw = placed[i].Yaw;
        return true;
    }

    internal sealed class PhysicsOwnership
    {
        internal Kind Kind;
        internal GameObject Root;
        internal BuildingBlock[] Members;
        internal bool[] Backed;
        internal Collider[] Captured, Rider;
        internal int Version, Isolated;
    }
    internal static bool ReadPhysicsOwnership(Vector3 position, out PhysicsOwnership view)
    {
        view = null;
        if (placed.Count > 256) return false;
        Placed nearest = null; var distance = Reach * Reach;
        foreach (var p in placed)
        {
            if (!IsTransition(p.Kind) || p.Root == null || !p.Root.activeInHierarchy) continue;
            var d = (p.Origin - position).sqrMagnitude;
            if (d < distance) { distance = d; nearest = p; }
        }
        if (nearest == null || nearest.Entities == null || nearest.Entities.Length > MegaParts ||
            nearest.Backing.Count > 1024 || (riderColliders != null && riderColliders.Length > 8)) return false;
        var backed = new bool[nearest.Entities.Length];
        for (var i = 0; i < backed.Length; i++)
            backed[i] = nearest.Kind == Kind.MegaHalfpipe || IsQuarter(nearest.Kind) || (nearest.Kind == Kind.Halfpipe && i >= 4 && i < 12);
        view = new PhysicsOwnership { Kind = nearest.Kind, Root = nearest.Root,
            Members = (BuildingBlock[])nearest.Entities.Clone(), Backed = backed, Captured = nearest.Backing.ToArray(),
            Rider = riderColliders == null ? new Collider[0] : (Collider[])riderColliders.Clone(), Version = backingVersion, Isolated = isolated.Count };
        return true;
    }
    internal static bool PhysicsOwnershipCurrent(PhysicsOwnership view)
    {
        return view != null && view.Root != null && view.Root.activeInHierarchy && view.Version == backingVersion;
    }

    public static string Line()
    {
        return "blocks=" + Blocks + " assemblies=" + Assemblies + " drawn=" + Drawn + " colliders=" + Colliders + " hidden=" + HiddenRenderers +
            (Status.Length > 0 ? " | " + Status : "") + (Error.Length > 0 ? " | " + Error : "");
    }

    // The building blocks within Reach of the player, by their colliders on the construction layer:
    // no search of the whole scene.
    public const float Reach = 80f;
    private static int constructionLayer = -2;
    private static void Scan()
    {
        var local = SkateRig.LocalT;
        if (local == null) return;
        Scans++;
        if (!SkateGestures.Done) SkateGestures.Report();
        if (!SkateHeld.Reported) SkateHeld.Report(SkateRig.Local);
        if (constructionLayer == -2) { try { constructionLayer = LayerMask.NameToLayer("Construction"); } catch (Exception) { constructionLayer = -1; } }
        var mask = constructionLayer >= 0 ? 1 << constructionLayer : ~0;
        var colliders = Physics.OverlapSphere(local.position, Reach, mask, QueryTriggerInteraction.Ignore);
        var blocks = new List<Block>();
        var seen = new HashSet<int>();
        for (var i = 0; i < colliders.Length; i++)
        {
            var c = colliders[i];
            if (c == null) continue;
            BuildingBlock b;
            try { b = c.GetComponentInParent<BuildingBlock>(); } catch (Exception) { continue; }
            if (b == null || !seen.Add(b.GetInstanceID())) continue;
            // The game names an entity's object after its prefab.
            string prefab;
            try { prefab = b.gameObject.name; } catch (Exception) { continue; }
            if (prefab == null) continue;
            if (prefab.EndsWith(" (Clone)")) prefab = prefab.Substring(0, prefab.Length - 8);
            if (prefab != FloorPrefab && prefab != LowWallPrefab && prefab != HalfWallPrefab && prefab != FoundationPrefab) continue;
            var t = b.transform; var e = t.rotation.eulerAngles;
            float top = 0f, bottom = 0f;
            try { var bounds = b.bounds; top = bounds.center.y + bounds.extents.y; bottom = bounds.center.y - bounds.extents.y; } catch (Exception) { }
            if (prefab == FloorPrefab && bottom < 0f && bottom > -0.5f) FloorBottom = bottom;
            if (prefab == FloorPrefab && top > 0f && top < 0.5f) FloorTop = top;
            if (prefab == FloorPrefab && Math.Abs(e.x) < 1f && top > 0f && bottom < 0f)
                try { var fb = b.bounds; FloorBox = new SkateParkBox { X = fb.center.x, Y = fb.center.y, Z = fb.center.z, SizeX = fb.size.x, SizeY = fb.size.y, SizeZ = fb.size.z }; } catch (Exception) { }
            blocks.Add(new Block { Entity = b, Prefab = prefab, Position = t.position, Rotation = t.rotation, Yaw = e.y, Pitch = Wrap(e.x), Top = top, Bottom = bottom, Id = b.GetInstanceID() });
        }
        Blocks = blocks.Count;
        // Assemblies that are still complete stay as they are; the rest go. A block that is out of
        // Reach but still exists keeps its assembly: riding away from a MEGA used to drop it once
        // its far end left the sphere, and its near chords were then drawn as banks and kickers.
        var present = new HashSet<int>(); foreach (var b in blocks) present.Add(b.Id);
        for (var i = placed.Count - 1; i >= 0; i--)
        {
            var keep = true;
            var ids = placed[i].Ids; var entities = placed[i].Entities;
            for (var k = 0; k < ids.Length; k++)
            {
                if (present.Contains(ids[k])) continue;
                if (entities != null && k < entities.Length && Alive(entities[k])) continue;
                keep = false; break;
            }
            if (!keep) { Remove(placed[i]); placed.RemoveAt(i); }
        }
        var taken = new HashSet<int>();
        var reserved = new HashSet<int>();
        // Cells of a metre, for the neighbourhood searches.
        var cells = new Dictionary<long, List<int>>();
        for (var i = 0; i < blocks.Count; i++) { var key = Cell(blocks[i].Position); List<int> l; if (!cells.TryGetValue(key, out l)) cells[key] = l = new List<int>(); l.Add(i); }
        var matched = new List<int>();
        // Pitched kinds first (the most distinctive), the halfpipe before the quarterpipe it contains.
        var order = new[] { Kind.MegaHalfpipe, Kind.Halfpipe, Kind.LargeQuarterPipe, Kind.MiniQuarterPipe, Kind.HalfWallKicker, Kind.QuarterPipe, Kind.Funbox, Kind.Kicker, Kind.Bank, Kind.LongLedge, Kind.LedgeHigh, Kind.LedgeLow };
        for (var priority = 0; priority < order.Length; priority++)
        {
            var kind = order[priority];
            taken.Clear(); taken.UnionWith(reserved);
            foreach (var existing in placed)
                if (Array.IndexOf(order, existing.Kind) <= priority)
                    foreach (var id in existing.Ids) taken.Add(id);
            var layout = Layout(kind);
            for (var i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                if (taken.Contains(b.Id) || b.Prefab != layout[0].Prefab) continue;
                if (Math.Abs(Delta((float)layout[0].Pitch, b.Pitch)) > AngleTolerance) continue;
                var anchorYaw = b.Yaw - (float)layout[0].Yaw;
                matched.Clear();
                if (!Match(kind, layout, blocks, cells, taken, b, anchorYaw, matched)) continue;
                if ((kind == Kind.LedgeLow || kind == Kind.LedgeHigh || kind == Kind.LongLedge) && !OverFoundation(b, blocks, cells, kind == Kind.LedgeHigh) && !OverGround(b, kind == Kind.LedgeHigh)) continue;
                if (IsGridQuarter(kind) && !GridQuarter(kind, b, blocks, cells, anchorYaw)) continue;
                if (kind == Kind.HalfWallKicker && !KickerWall(b, blocks, cells)) continue;
                // A plain kicker is 25 degrees; 30 is the half-wall kicker's, even when its wall is out of reach.
                if (kind == Kind.Kicker && Math.Abs(Delta(-30f, b.Pitch)) < 2.5f) continue;
                var ids = new int[matched.Count];
                for (var m = 0; m < matched.Count; m++) ids[m] = blocks[matched[m]].Id;
                var candidate = new HashSet<int>(ids);
                var displaced = new List<Placed>();
                var overlap = false;
                foreach (var existing in placed)
                {
                    var touches = false;
                    foreach (var id in existing.Ids) if (candidate.Contains(id)) { touches = true; break; }
                    if (!touches) continue;
                    if (Array.IndexOf(order, existing.Kind) <= priority) { overlap = true; break; }
                    foreach (var id in existing.Ids) if (!candidate.Contains(id)) { overlap = true; break; }
                    if (overlap) break;
                    displaced.Add(existing);
                }
                if (overlap) continue;
                var entities = new BuildingBlock[matched.Count];
                for (var m = 0; m < matched.Count; m++) entities[m] = blocks[matched[m]].Entity;
                var p = new Placed { Kind = kind, Ids = ids, Entities = entities };
                if (!Place(p, kind, blocks, matched, anchorYaw, displaced))
                {
                    // Geometry has identified this kit even when its model or safe activation
                    // is unavailable. Do not reinterpret its members as a smaller obstacle.
                    reserved.UnionWith(ids); taken.UnionWith(ids);
                    continue;
                }
                foreach (var id in ids) taken.Add(id);
                Say("found a " + fileStems[(int)kind].Substring(6) + " at " + Out.V1(p.Origin) + " yaw " + p.Yaw.ToString("F0") + (p.Root != null ? ", drawn" : ", not drawn: " + Status));
            }
            if (kind == Kind.MegaHalfpipe)
            {
                MegaNearMiss(blocks, cells, taken);
                reserved.UnionWith(taken);
            }
        }
        Assemblies = placed.Count;
        var drawn = 0; foreach (var p in placed) if (p.Root != null) drawn++;
        Drawn = drawn;
    }

    // A MEGA kit the client can't match whole: its 9 and 27 degree chords would otherwise be taken
    // for a bank and a kicker on one side (#495). The best partial match from any flat floor, at
    // least half the kit, keeps its blocks from the smaller kinds; the first part missing is logged
    // with the nearest block of its prefab, once for each new outcome.
    private static string megaMiss = "";
    private static void MegaNearMiss(List<Block> blocks, Dictionary<long, List<int>> cells, HashSet<int> taken)
    {
        var layout = Layout(Kind.MegaHalfpipe);
        var best = new List<int>(); var bestMissing = -1; var bestYaw = 0f; var bestAt = Vector3.zero;
        var found = new List<int>();
        for (var i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            if (taken.Contains(b.Id) || b.Prefab != FloorPrefab || Math.Abs(b.Pitch) > AngleTolerance) continue;
            var anchorYaw = b.Yaw;
            found.Clear();
            var missing = -1;
            var sin = (float)Math.Sin(anchorYaw * Degrees); var cos = (float)Math.Cos(anchorYaw * Degrees);
            for (var k = 0; k < layout.Length; k++)
            {
                var hit = MegaPartAt(layout, k, blocks, cells, taken, b, sin, cos, anchorYaw, found);
                if (hit >= 0) found.Add(hit); else if (missing < 0) missing = k;
            }
            if (found.Count > best.Count) { best = new List<int>(found); bestMissing = missing; bestYaw = anchorYaw; bestAt = b.Position; }
        }
        if (best.Count < layout.Length / 2 || bestMissing < 0)
        {
            if (megaMiss.Length > 0 && best.Count < layout.Length / 2) megaMiss = "";
            return;
        }
        foreach (var m in best) taken.Add(blocks[m].Id);
        WithdrawPartialMega(best, blocks);
        var part = layout[bestMissing];
        var first = blocks[best[0]];
        var s = (float)Math.Sin(bestYaw * Degrees); var c = (float)Math.Cos(bestYaw * Degrees);
        var dr = (float)(part.Right - layout[0].Right); var df = (float)(part.Forward - layout[0].Forward); var dh = (float)(part.Height - layout[0].Height);
        var want = first.Position + new Vector3(dr * c + df * s, dh, -dr * s + df * c);
        var near = -1; var nearD = float.MaxValue;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Prefab != part.Prefab) continue;
            var d = Vector3.Distance(blocks[i].Position, want);
            if (d < nearD) { nearD = d; near = i; }
        }
        var line = "MEGA kit at " + Out.V1(bestAt) + " not whole: " + best.Count + "/" + layout.Length + " parts, first missing #" + bestMissing +
            " (" + (part.Prefab == FloorPrefab ? "floor" : "low wall") + " pitch " + part.Pitch.ToString("F0") + " yaw " + (bestYaw + part.Yaw).ToString("F0") + " at " + Out.V1(want) + ")" +
            (near < 0 ? ", no block of that prefab in reach" : ", nearest " + nearD.ToString("F2") + " m away, pitch " + blocks[near].Pitch.ToString("F1") + " yaw " + blocks[near].Yaw.ToString("F1") + " at " + Out.V1(blocks[near].Position)) +
            "; its blocks are kept from the smaller kinds";
        var key = best.Count + "/" + bestMissing;
        if (key == megaMiss) return;
        megaMiss = key;
        Say(line);
    }

    // A partial MEGA can become recognizable after an earlier scan drew a bank or kicker.
    // Withdraw only overlays wholly owned by that partial kit, and only while the actual
    // rider is clear of both the old ride surface and the native geometry it will reveal.
    private static void WithdrawPartialMega(List<int> matched, List<Block> blocks)
    {
        var ids = new HashSet<int>(); foreach (var index in matched) ids.Add(blocks[index].Id);
        var withdrawn = new List<Placed>();
        foreach (var existing in placed)
        {
            if (existing.Kind == Kind.MegaHalfpipe || existing.Ids.Length == 0) continue;
            var contained = true;
            foreach (var id in existing.Ids) if (!ids.Contains(id)) { contained = false; break; }
            if (contained) withdrawn.Add(existing);
        }
        if (withdrawn.Count == 0) return;
        var nextBacking = new HashSet<int>();
        try
        {
            foreach (var existing in withdrawn)
            {
                if (existing.Root == null || !ReplacementClear(existing.Root))
                    throw new InvalidOperationException("Step clear of the partial MEGA to remove its provisional overlays.");
                foreach (var id in existing.Ids)
                {
                    var found = false;
                    foreach (var block in blocks)
                    {
                        if (block.Id != id) continue;
                        if (block.Entity == null || !ReplacementClear(block.Entity.gameObject))
                            throw new InvalidOperationException("Step clear of the partial MEGA's native parts to remove its provisional overlays.");
                        found = true; break;
                    }
                    if (!found) throw new InvalidOperationException("A partial MEGA member is unavailable.");
                }
            }
            foreach (var remaining in placed)
                if (!withdrawn.Contains(remaining))
                    foreach (var collider in remaining.Backing) if (collider != null) nextBacking.Add(collider.GetInstanceID());
        }
        catch (Exception error)
        {
            Status = "partial MEGA overlay removal deferred: " + error.Message;
            return;
        }
        foreach (var existing in withdrawn) { placed.Remove(existing); Remove(existing); }
        BackingIds.Clear(); BackingIds.UnionWith(nextBacking); backingVersion++;
        try { Isolate(SkateRide.On, SkateRig.Body, Time.realtimeSinceStartup); }
        catch (Exception error) { Status = "backing isolation will retry: " + error.Message; }
    }

    private static int MegaPartAt(Part[] layout, int k, List<Block> blocks, Dictionary<long, List<int>> cells, HashSet<int> taken, Block first, float sin, float cos, float anchorYaw, List<int> found)
    {
        var part = layout[k];
        var dr = (float)(part.Right - layout[0].Right); var df = (float)(part.Forward - layout[0].Forward); var dh = (float)(part.Height - layout[0].Height);
        var want = first.Position + new Vector3(dr * cos + df * sin, dh, -dr * sin + df * cos);
        var wantYaw = anchorYaw + (float)part.Yaw;
        var best = -1; var bestDistance = PositionTolerance;
        foreach (var i in Near(cells, want))
        {
            var b = blocks[i];
            if (b.Prefab != part.Prefab || taken.Contains(b.Id) || found.Contains(i)) continue;
            var d = Vector3.Distance(b.Position, want);
            if (d > bestDistance) continue;
            var yawOff = Math.Abs(Delta(wantYaw, b.Yaw));
            if (part.Prefab != FloorPrefab) { yawOff %= 90f; yawOff = Math.Min(yawOff, 90f - yawOff); }
            if (yawOff > AngleTolerance || Math.Abs(Delta((float)part.Pitch, b.Pitch)) > AngleTolerance) continue;
            best = i; bestDistance = d;
        }
        return best;
    }

    // Every part of the layout has a block of its prefab within the tolerances, taking the given
    // block as part 0; the matched block indices are collected.
    private static bool Match(Kind kind, Part[] layout, List<Block> blocks, Dictionary<long, List<int>> cells, HashSet<int> taken, Block first, float anchorYaw, List<int> matched)
    {
        var sin = (float)Math.Sin(anchorYaw * Degrees); var cos = (float)Math.Cos(anchorYaw * Degrees);
        for (var k = 0; k < layout.Length; k++)
        {
            var part = layout[k];
            var dr = (float)(part.Right - layout[0].Right); var df = (float)(part.Forward - layout[0].Forward); var dh = (float)(part.Height - layout[0].Height);
            var want = first.Position + new Vector3(dr * cos + df * sin, dh, -dr * sin + df * cos);
            var wantYaw = anchorYaw + (float)part.Yaw;
            var best = -1; var bestDistance = PositionTolerance;
            foreach (var i in Near(cells, want))
            {
                var b = blocks[i];
                if (b.Prefab != part.Prefab || taken.Contains(b.Id) || matched.Contains(i)) continue;
                var d = Vector3.Distance(b.Position, want);
                if (d > bestDistance) continue;
                // A wall is matched by its line, whichever quarter it is turned to: the native wall lies
                // along its own z, which the server's layout may turn it by 90 degrees for (#337).
                var yawOff = Math.Abs(Delta(wantYaw, b.Yaw));
                if (part.Prefab != FloorPrefab) { yawOff %= 90f; yawOff = Math.Min(yawOff, 90f - yawOff); }
                if (yawOff > AngleTolerance || Math.Abs(Delta((float)part.Pitch, b.Pitch)) > AngleTolerance) continue;
                best = i; bestDistance = d;
            }
            if (best < 0) return false;
            matched.Add(best);
        }
        return true;
    }

    // A ledge is a floor tile 0.4 or 0.8 above a foundation and nothing else (pivot to pivot that is
    // 0.4 or 0.8 plus the tile's thickness, about 0.6 or 1.0 in the game: seen live, a long ledge on
    // the owner's platform was taken for a high ledge at 0.62): an ordinary floor never
    // sits that close above a foundation (half walls put one 1.5 above). The gap is pivot to pivot.
    public static float LedgeGap = -1f;
    private static bool OverFoundation(Block floor, List<Block> blocks, Dictionary<long, List<int>> cells, bool high)
    {
        foreach (var i in Near(cells, floor.Position - Vector3.up * 0.6f))
        {
            var b = blocks[i];
            if (b.Prefab != FoundationPrefab) continue;
            var flat = b.Position - floor.Position; flat.y = 0f;
            if (flat.magnitude > 1.6f) continue;
            var gap = floor.Position.y - b.Position.y;
            if (gap < 0.15f || gap > 1.3f) continue;
            LedgeGap = gap;
            return high ? gap >= LedgeSplit : gap < LedgeSplit;
        }
        return false;
    }

    // Or a floor tile standing free that high above the ground, as the server's own placement
    // (`skate.place`, 0.3.8) leaves a ledge: its anchor rests on the ground and the tile is 0.4 or
    // 0.8 above that, with nothing under it. A building's floor stands on walls, a storey up.
    public static float LedgeGroundGap = -1f;
    // Measured from the tile's pivot, which is its top in the game (the tile's own bounds are not
    // always readable on the client): 0.4 plus the tile's thickness for a low ledge, 0.8 plus it for
    // a high one. Seen live: a long ledge's tiles 0.6 up were taken for high ledges when measured from
    // a bottom the client could not read.
    public const float LedgeSplit = 0.8f;
    private static bool OverGround(Block floor, bool high)
    {
        if (Math.Abs(floor.Pitch) > AngleTolerance) return false;
        var mask = SkateRide.GroundMask;
        if (constructionLayer >= 0) mask &= ~(1 << constructionLayer);
        RaycastHit hit;
        if (!Physics.Raycast(floor.Position, Vector3.down, out hit, 1.5f, mask, QueryTriggerInteraction.Ignore)) return false;
        var gap = hit.distance;
        if (gap < 0.3f || gap > 1.3f) return false;
        LedgeGroundGap = gap;
        return high ? gap >= LedgeSplit : gap < LedgeSplit;
    }

    // Flat floors in an L (or a floor and a low wall) are common in any base, a storey up on walls.
    // The server's grid quarterpipes rest their floors on the ground or a foundation, which an ordinary
    // floor never does; and the L's fourth cell is empty, or it is just a floor.
    public const float RestGap = 0.35f;
    private static bool GridQuarter(Kind kind, Block first, List<Block> blocks, Dictionary<long, List<int>> cells, float anchorYaw)
    {
        if (Math.Abs(first.Pitch) > AngleTolerance) return false;
        RaycastHit hit;
        var under = first.Position + Vector3.up * (first.Bottom - 0.02f);
        if (!Physics.Raycast(under, Vector3.down, out hit, RestGap, SkateRide.GroundMask, QueryTriggerInteraction.Ignore)) return false;
        if (kind != Kind.LargeQuarterPipe) return true;
        var sin = (float)Math.Sin(anchorYaw * Degrees); var cos = (float)Math.Cos(anchorYaw * Degrees);
        var fourth = first.Position + new Vector3(3f * cos + 3f * sin, 0f, -3f * sin + 3f * cos);
        foreach (var i in Near(cells, fourth))
            if (blocks[i].Prefab == FloorPrefab && Vector3.Distance(blocks[i].Position, fourth) < 1f) return false;
        return true;
    }

    // The half-wall kicker's top edge meets the top of a half wall (Codex's TryHalfWallAnchor, #461):
    // a half wall whose top is level with the floor's upper edge, beside it.
    private static bool KickerWall(Block floor, List<Block> blocks, Dictionary<long, List<int>> cells)
    {
        if (Math.Abs(Delta(-30f, floor.Pitch)) > 2.5f) return false;
        var edge = floor.Position + floor.Rotation * new Vector3(0f, floor.Top, (float)HalfLength);
        foreach (var i in Near(cells, edge - Vector3.up * 0.75f))
        {
            var w = blocks[i];
            if (w.Prefab != HalfWallPrefab) continue;
            var flat = w.Position - edge; flat.y = 0f;
            if (flat.magnitude > 0.8f || Math.Abs(w.Position.y + w.Top - edge.y) > 0.3f) continue;
            return true;
        }
        return false;
    }

    // The half-wall kicker's ghost, from the aim (#461's TryHalfWallAnchor): the first hit within
    // HalfWallReach must be a broad face of a native half wall, on the player's side. The floor
    // faces the wall (side +1: wall yaw + 270, -1: + 90), pitched 30 degrees, its upper top edge on
    // the face at the wall's top; the model origin is its low top edge, as Place reads a built one.
    public const float HalfWallReach = 25f;
    public static bool HalfWallAim(Vector3 from, Vector3 dir, Vector3 player, out Vector3 origin, out float yaw, out string why)
    {
        origin = Vector3.zero; yaw = 0f;
        RaycastHit hit;
        if (!Physics.Raycast(from, dir, out hit, HalfWallReach, SkateRide.GroundMask, QueryTriggerInteraction.Ignore)) { why = "aim at a half wall"; return false; }
        BuildingBlock wall = null; string prefab = null;
        try { wall = hit.collider.GetComponentInParent<BuildingBlock>(); prefab = wall != null ? wall.gameObject.name : null; } catch (Exception) { }
        if (prefab != null && prefab.EndsWith(" (Clone)")) prefab = prefab.Substring(0, prefab.Length - 8);
        if (prefab != HalfWallPrefab) { why = "aim at a half wall"; return false; }
        var t = wall.transform;
        var right = t.rotation * Vector3.right; right.y = 0f; right.Normalize();
        if (Math.Abs(Vector3.Dot(hit.normal, right)) < 0.9f) { why = "aim at the broad face of the half wall"; return false; }
        var side = Vector3.Dot(player - t.position, right) >= 0f ? 1 : -1;
        if (Vector3.Dot(hit.normal, right) * side < 0f) { why = "stand on the side you aim at"; return false; }
        Bounds b;
        try { b = wall.bounds; } catch (Exception) { why = "the wall cannot be measured"; return false; }
        var wallYaw = t.rotation.eulerAngles.y;
        yaw = (wallYaw + (side > 0 ? 270f : 90f)) % 360f;
        var top = t.position + Quaternion.Euler(0f, wallYaw, 0f) * new Vector3(b.center.x + side * b.extents.x, b.center.y + b.extents.y, 0f);
        var run = (float)(2 * HalfLength * Math.Cos(30 * Degrees)); var rise = (float)(2 * HalfLength * Math.Sin(30 * Degrees));
        origin = top - Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, rise, run);
        why = "";
        return true;
    }

    private static bool Place(Placed p, Kind kind, List<Block> blocks, List<int> matched, float anchorYaw, List<Placed> displaced)
    {
        var newlyHidden = new List<Renderer>();
        var inherited = new HashSet<Renderer>();
        var nextBacking = new HashSet<int>();
        try
        {
            var first = blocks[matched[0]];
            var yawRot = Quaternion.Euler(0f, anchorYaw, 0f);
            var origin = Origin(kind, first.Position, first.Rotation, yawRot, first.Top) + Vector3.up * Lift;
            p.Origin = origin; p.Yaw = anchorYaw;
            int colliders;
            p.Root = IsTransition(kind) ? Transition(kind, origin, yawRot, true, out colliders, false, p.Meshes) : Build(kind, origin, yawRot, true, out colliders);
            if (p.Root == null) return false;
            p.Root.SetActive(false); p.ColliderCount = colliders;
            if (IsTransition(kind)) ReadRamp(p, p.Root);
            if (kind == Kind.Bank || kind == Kind.Kicker) p.ColliderCount += Entry(p.Root, 0f, -1f, p.Meshes);
            if (IsQuarter(kind)) p.ColliderCount += Entry(p.Root, (float)QuarterStart, -1f, p.Meshes);
            if (kind == Kind.HalfWallKicker) p.ColliderCount += Entry(p.Root, (float)KickerRunIn, -1f, p.Meshes);
            if (kind == Kind.Funbox) { p.ColliderCount += Entry(p.Root, -FunboxEnd, -1f, p.Meshes); p.ColliderCount += Entry(p.Root, FunboxEnd, 1f, p.Meshes); }
            foreach (var old in displaced)
                foreach (var renderer in old.Hidden)
                    if (renderer != null && inherited.Add(renderer)) p.Hidden.Add(renderer);
            for (var part = 0; part < matched.Count; part++)
            {
                var entity = blocks[matched[part]].Entity;
                foreach (var renderer in entity.GetComponentsInChildren<Renderer>(true))
                    if (renderer != null && renderer.enabled && !p.Hidden.Contains(renderer)) p.Hidden.Add(renderer);
                if (kind == Kind.MegaHalfpipe || IsQuarter(kind) || (kind == Kind.Halfpipe && part >= 4 && part < 12))
                    foreach (var collider in entity.GetComponentsInChildren<Collider>(true))
                        if (collider != null) p.Backing.Add(collider);
            }
            foreach (var remaining in placed)
                if (!displaced.Contains(remaining))
                    foreach (var collider in remaining.Backing) if (collider != null) nextBacking.Add(collider.GetInstanceID());
            foreach (var collider in p.Backing) nextBacking.Add(collider.GetInstanceID());
            if (displaced.Count > 0 && !ReplacementClear(p.Root))
                throw new InvalidOperationException("Step clear of the ramp to finish recognizing the complete park piece.");
            foreach (var renderer in p.Hidden)
            {
                if (!renderer.enabled) continue;
                newlyHidden.Add(renderer); renderer.enabled = false;
            }
            p.Root.SetActive(true);
        }
        catch (Exception error)
        {
            foreach (var renderer in newlyHidden) try { if (renderer != null) renderer.enabled = true; } catch (Exception) { }
            if (p.Root != null) { p.Root.SetActive(false); UnityEngine.Object.Destroy(p.Root); p.Root = null; }
            foreach (var mesh in p.Meshes) if (mesh != null) UnityEngine.Object.Destroy(mesh);
            p.Meshes.Clear();
            Status = "park replacement deferred: " + error.Message;
            return false;
        }
        // Transfer restore ownership before removing old overlays; their native renderers
        // and backing can also belong to the complete assembly.
        foreach (var old in displaced) old.Hidden.Clear();
        HiddenRenderers += newlyHidden.Count;
        foreach (var id in p.Ramp) RampIds.Add(id);
        foreach (var id in p.Coping) CopingIds.Add(id);
        Colliders += p.ColliderCount;
        placed.Add(p);
        foreach (var old in displaced) { placed.Remove(old); Remove(old); }
        BackingIds.Clear(); BackingIds.UnionWith(nextBacking); backingVersion++;
        try { Isolate(SkateRide.On, SkateRig.Body, Time.realtimeSinceStartup); }
        catch (Exception error) { Status = "backing isolation will retry: " + error.Message; }
        if (p.Backing.Count > 0) Say(KindName(kind) + " backing: " + p.Backing.Count + " native colliders kept from the ride");
        return true;
    }

    private static bool ReplacementClear(GameObject replacement)
    {
        var body = SkateRig.Body;
        if (body == null) return false;
        var bodies = body.GetComponentsInChildren<Collider>(true);
        var surfaces = replacement.GetComponentsInChildren<Collider>(true);
        var observed = false;
        foreach (var rider in bodies)
        {
            if (rider == null || rider.isTrigger || !rider.enabled || !rider.gameObject.activeInHierarchy) continue;
            if (!PrimitiveBody(rider)) return false;
            var bounds = rider.bounds;
            if (!FiniteBounds(bounds) || !(bounds.size.x > 0f && bounds.size.y > 0f && bounds.size.z > 0f)) return false;
            observed = true;
            foreach (var surface in surfaces)
            {
                if (surface == null || surface.isTrigger || !surface.enabled) continue;
                var meshCollider = surface.GetComponent<MeshCollider>();
                if (meshCollider != null && meshCollider.GetInstanceID() == surface.GetInstanceID())
                {
                    var mesh = meshCollider.sharedMesh;
                    if (mesh == null) return false;
                    var vertices = mesh.vertices; var triangles = mesh.triangles;
                    if (triangles.Length == 0 || triangles.Length % 3 != 0) return false;
                    // Bounds provide a conservative guard even for backfaced triangles or
                    // a staged mesh that has not entered the active physics scene.
                    for (var i = 0; i < triangles.Length; i += 3)
                    {
                        var triangle = new Bounds(surface.transform.TransformPoint(vertices[triangles[i]]), Vector3.zero);
                        triangle.Encapsulate(surface.transform.TransformPoint(vertices[triangles[i + 1]]));
                        triangle.Encapsulate(surface.transform.TransformPoint(vertices[triangles[i + 2]]));
                        triangle.Expand(0.02f);
                        if (!FiniteBounds(triangle) || bounds.Intersects(triangle)) return false;
                    }
                }
                else
                {
                    Vector3 direction; float distance;
                    if (Physics.ComputePenetration(rider, rider.transform.position, rider.transform.rotation,
                        surface, surface.transform.position, surface.transform.rotation, out direction, out distance)) return false;
                }
            }
        }
        return observed;
    }

    private static bool PrimitiveBody(Collider collider)
    {
        var id = collider.GetInstanceID();
        var capsule = collider.GetComponent<CapsuleCollider>();
        if (capsule != null && capsule.GetInstanceID() == id) return true;
        var box = collider.GetComponent<BoxCollider>();
        if (box != null && box.GetInstanceID() == id) return true;
        var sphere = collider.GetComponent<SphereCollider>();
        if (sphere != null && sphere.GetInstanceID() == id) return true;
        var mesh = collider.GetComponent<MeshCollider>();
        return mesh != null && mesh.GetInstanceID() == id && mesh.convex;
    }

    private static bool FiniteBounds(Bounds bounds)
    {
        var centre = bounds.center; var size = bounds.size;
        return !(float.IsNaN(centre.x) || float.IsNaN(centre.y) || float.IsNaN(centre.z) ||
            float.IsInfinity(centre.x) || float.IsInfinity(centre.y) || float.IsInfinity(centre.z) ||
            float.IsNaN(size.x) || float.IsNaN(size.y) || float.IsNaN(size.z) ||
            float.IsInfinity(size.x) || float.IsInfinity(size.y) || float.IsInfinity(size.z)) &&
            size.x >= 0f && size.y >= 0f && size.z >= 0f;
    }

    // Where a kind's model stands, from its first native block: at (its position), rotation (its full
    // rotation), the group's yaw and that block's local top. The same for a placed piece and for the
    // ghost of a held park item (#471: one model-origin routine for both).
    private static Vector3 Origin(Kind kind, Vector3 at, Quaternion rotation, Quaternion yawRot, float top)
    {
        switch (kind)
        {
            case Kind.Bank: case Kind.Kicker: case Kind.HalfWallKicker:
                // The model's origin is the entry midpoint on the riding surface: the first tile's
                // low edge, on its top.
                return at + rotation * new Vector3(0f, top, -(float)HalfLength);
            case Kind.Funbox:
                // The base below the deck's centre, the deck 0.776 above it.
                return at + Vector3.up * (top - (float)(2 * HalfLength * Math.Sin(15 * Degrees)));
            case Kind.MiniQuarterPipe: case Kind.LargeQuarterPipe:
                // The first floor's pivot, at the riding datum: its top.
                return at + Vector3.up * top;
            case Kind.QuarterPipe:
                // The group anchor at the riding datum, as for the grid quarters: the first carrier
                // floor stands 1.5*sin15 above the anchor, pitched; the datum is an unpitched floor's top.
                return at + Vector3.up * (top - (float)(HalfLength * Math.Sin(15 * Degrees)));
            case Kind.MegaHalfpipe:
                // The first flat floor is lane 0, 4.5 m back from the flat's middle; the model's origin is
                // the middle of the flat across all four lanes, on the floors' top.
                return at + yawRot * new Vector3((float)MegaCentre, top, 4.5f);
            case Kind.Halfpipe:
                // The flat's centre: the four flat tiles are at right 0/3, forward 0/3 from the first.
                return at + yawRot * new Vector3((float)HalfLength, top, (float)HalfLength);
            default:
                // A ledge's base centre, its top 0.4 or 0.8 above.
                return at + Vector3.up * (top - (kind == Kind.LedgeHigh ? 0.8f : 0.4f));
        }
    }

    // A preview without any server piece: the model of a kind at a place, for a look and a ride
    // (solid), or as a ghost without colliders for the picker, whose preview follows the aim and
    // must never stand in the player's way.
    private static GameObject preview;
    public static string PreviewAt(Kind kind, Vector3 origin, float yaw, bool solid = true)
    {
        PreviewClear();
        int colliders;
        preview = Build(kind, origin + Vector3.up * Lift, Quaternion.Euler(0f, yaw, 0f), solid, out colliders);
        if (preview == null) return "not drawn: " + Status;
        Colliders += colliders;
        return "drawn at " + Out.V1(origin) + " yaw " + yaw.ToString("F0") + " with " + colliders + " colliders";
    }
    // Where a kind's model stands on a support plane (#471's seating): the group anchor lies so that
    // the lowest underside of its native floors meets the support, with no added clearance; the first
    // floor then stands where the server puts it, and the model is drawn from it as a placed one is.
    public static Vector3 ModelOriginForAnchor(Kind kind, Vector3 support, float yaw)
    {
        var yawRot = Quaternion.Euler(0f, yaw, 0f);
        var layout = Layout(kind);
        if (layout.Length == 0) return support;
        float cy = (FloorTop + FloorBottom) / 2f, sy = FloorTop - FloorBottom;
        var lowest = double.MaxValue;
        foreach (var part in layout)
        {
            if (part.Prefab != FloorPrefab) continue;
            var pitch = part.Pitch * Degrees;
            lowest = Math.Min(lowest, part.Height + cy * Math.Cos(pitch) - sy / 2 * Math.Abs(Math.Cos(pitch)) - HalfLength * Math.Abs(Math.Sin(pitch)));
        }
        if (lowest == double.MaxValue) lowest = FloorBottom;
        var anchor = support - Vector3.up * (float)lowest;
        var first = layout[0];
        var at = anchor + yawRot * new Vector3((float)first.Right, (float)first.Height, (float)first.Forward);
        var rotation = yawRot * Quaternion.Euler((float)first.Pitch, (float)first.Yaw, 0f);
        return Origin(kind, at, rotation, yawRot, FloorTop);
    }

    // The held park item's ghost, decided by the server's own shared rules (#476: SkateParkLayout.
    // TryGhostFrame and TryGhostAnchor, the source the server's ResolveParkSupport calls): aimed at a
    // level square foundation's top, or at a flat floor of a park piece for the next 3 m cell, the
    // frame is that host's, turned by the planner's own rotation (R) in the native steps; otherwise
    // it is the aim point and the look. Every support cell and every lowest contact is then read
    // with a ray down, as the server reads it. Origin is where the model is drawn; Valid only when
    // the whole support is found, and Why says what is missing when it is not.
    public const float GhostStep = 90f;
    // The halfpipe is placed across the view, not back over the player: the server adds the same
    // quarter turn in SkateParkLayout.PlacementYaw. The shared file this client compiles is pinned
    // (sources.txt) at a commit without that function, so the turn is added here; when the pin
    // moves to a commit that has it, this goes and TryGhostFrame adds it itself.
    public static float PlacementYaw(SkateParkKind kind) { return kind == SkateParkKind.Halfpipe ? 90f : 0f; }
    public static bool Ghost(Kind kind, RaycastHit aim, float lookYaw, float turn, out Vector3 origin, out float yaw, out bool onHost, out string why, bool snapTerrain = false)
    {
        onHost = false; why = "";
        var shared = (SkateParkKind)(int)kind;
        var hint = new SkateParkTransform { X = aim.point.x, Y = aim.point.y, Z = aim.point.z, Yaw = Wrap(lookYaw + turn) };
        double terrainYaw;
        if (snapTerrain && SkateParkLayout.TrySnapYaw(hint.Yaw, 0, GhostStep, out terrainYaw)) hint.Yaw = terrainYaw;
        double? selected = null;
        try
        {
            BuildingBlock host = null; var prefab = "";
            if (aim.collider != null && aim.normal.y >= 0.98f) host = aim.collider.GetComponentInParent<BuildingBlock>();
            if (host != null) { prefab = host.gameObject.name ?? ""; if (prefab.EndsWith(" (Clone)")) prefab = prefab.Substring(0, prefab.Length - 8); }
            var level = host != null && Math.Abs(Wrap(host.transform.eulerAngles.x)) < 1f && Math.Abs(Wrap(host.transform.eulerAngles.z)) < 1f;
            var foundation = level && prefab == FoundationPrefab;
            var adjacent = level && prefab == FloorPrefab && IsParkFloor(host);
            if (foundation || adjacent)
            {
                var t = host.transform;
                SkateParkTransform frame;
                if (SkateParkLayout.TryGhostFrame(shared, adjacent, t.position.x, t.position.z, t.eulerAngles.y, aim.point.x, aim.point.z, turn + PlacementYaw(shared), true, GhostStep, out frame))
                {
                    onHost = true;
                    hint = new SkateParkTransform { X = frame.X, Y = aim.point.y, Z = frame.Z, Yaw = frame.Yaw };
                    // A foundation's top, or the neighbouring park floor's underside.
                    selected = adjacent ? t.position.y + FloorBox.Y - FloorBox.SizeY / 2 : aim.point.y;
                }
            }
        }
        catch (Exception e) { why = "target not read: " + e.GetType().Name; }
        SkateParkTransform anchor; double top; string failure;
        var valid = SkateParkLayout.TryGhostAnchor(shared, FloorBox, hint, selected, ReadSupport, out anchor, out top, out failure);
        if (!valid)
        {
            // Not confirmed: drawn where the aim is, seated on it, and the reason shown.
            why = failure ?? "no valid support";
            yaw = (float)hint.Yaw;
            origin = ModelOriginForAnchor(kind, new Vector3((float)hint.X, (float)hint.Y, (float)hint.Z), yaw);
            return false;
        }
        yaw = (float)anchor.Yaw;
        origin = ModelOriginForFinal(kind, anchor);
        return true;
    }

    // The measured native floor box (local centre and size), as the server measures it; the
    // prefab's own numbers until a floor has been seen (#464: centre 0, size 3 x 0.2 x 3).
    public static SkateParkBox FloorBox = new SkateParkBox { X = 0, Y = 0, Z = 0, SizeX = 3, SizeY = 0.2, SizeZ = 3 };

    // Where the model stands for a final assembly anchor (already seated: nothing is added): from its
    // first native member's predicted pose, by the same routine as a placed piece.
    public static Vector3 ModelOriginForFinal(Kind kind, SkateParkTransform anchor)
    {
        SkateParkTransform pose;
        if (!SkateParkLayout.TryTransform((SkateParkKind)(int)kind, 0, anchor.X, anchor.Y, anchor.Z, anchor.Yaw, out pose))
            return new Vector3((float)anchor.X, (float)anchor.Y, (float)anchor.Z);
        var yawRot = Quaternion.Euler(0f, (float)anchor.Yaw, 0f);
        var rotation = Quaternion.Euler((float)pose.Pitch, (float)pose.Yaw, 0f);
        return Origin(kind, new Vector3((float)pose.X, (float)pose.Y, (float)pose.Z), rotation, yawRot, (float)(FloorBox.Y + FloorBox.SizeY / 2));
    }

    // One support probe, as the server's adapter reads it: a ray down 27 m on terrain, world,
    // construction and deployables, no triggers; the first hit must be a level square foundation
    // (on the expected cell, for a whole-cell probe) or the terrain. Anything else is no support.
    public const float SupportReach = 27f;
    private static int supportMask = -1;
    private static bool ReadSupport(SkateParkSupportProbe probe, out SkateParkSupportHit hit, out string failure)
    {
        hit = new SkateParkSupportHit(); failure = null;
        try
        {
            if (supportMask == -1) supportMask = LayerMask.GetMask("Terrain", "World", "Construction", "Deployed");
            RaycastHit h;
            var from = new Vector3((float)probe.Point.X, (float)probe.Point.Y, (float)probe.Point.Z);
            if (!Physics.Raycast(from, Vector3.down, out h, SupportReach, supportMask, QueryTriggerInteraction.Ignore) || h.collider == null)
            { failure = SkateParkLayout.SupportLabel(probe) + ": nothing under it"; return false; }
            if (h.collider is TerrainCollider) { hit.Height = h.point.y; hit.FoundationId = 0; return true; }
            var b = h.collider.GetComponentInParent<BuildingBlock>();
            var prefab = b == null ? "" : (b.gameObject.name ?? "");
            if (prefab.EndsWith(" (Clone)")) prefab = prefab.Substring(0, prefab.Length - 8);
            if (prefab != FoundationPrefab || h.normal.y < 0.98f)
            { failure = SkateParkLayout.SupportLabel(probe) + ": on " + (b != null ? "a " + KindOfBlock(prefab) : "something that is not a foundation or ground"); return false; }
            var t = b.transform;
            if (probe.RequireCell && !SkateParkLayout.FitsSupportCell(probe.Cell, t.position.x, t.position.z, t.eulerAngles.y))
            { failure = SkateParkLayout.SupportLabel(probe) + ": the foundation is not on this piece's grid"; return false; }
            hit.Height = h.point.y;
            hit.FoundationId = unchecked((ulong)(uint)b.GetInstanceID()) + 1UL;
            return true;
        }
        catch (Exception e) { failure = SkateParkLayout.SupportLabel(probe) + ": not read (" + e.GetType().Name + ")"; return false; }
    }
    private static string KindOfBlock(string prefab) { var i = prefab.LastIndexOf('/'); var name = i >= 0 ? prefab.Substring(i + 1) : prefab; return name.Replace(".prefab", ""); }

    // A flat floor of a park piece the client has recognised: the next piece can snap beside it.
    public static bool IsParkFloor(BuildingBlock b)
    {
        if (b == null) return false;
        var id = b.GetInstanceID();
        foreach (var p in placed) if (p.Ids != null && Array.IndexOf(p.Ids, id) >= 0) return true;
        return false;
    }

    // The preview moved to another place, as the aim moves.
    public static void PreviewMove(Vector3 origin, float yaw)
    {
        if (preview == null) return;
        preview.transform.position = origin + Vector3.up * Lift; preview.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }
    public static void PreviewClear()
    {
        if (preview == null) return;
        try { UnityEngine.Object.Destroy(preview); } catch (Exception) { }
        preview = null;
    }

    // The model's objects under a root at a place: the Visual scene drawn, the CollisionProxies
    // scene as static colliders on the default layer, which the ride's ground mask includes; not
    // solid, the colliders are left out.
    private static GameObject Build(Kind kind, Vector3 origin, Quaternion yawRot, bool solid, out int colliders)
    {
        colliders = 0;
        if (IsTransition(kind)) return Transition(kind, origin, yawRot, solid, out colliders);
        var loaded = Model(kind);
        if (loaded == null || loaded.Model == null) return null;
        var root = new GameObject("skate_park_" + fileStems[(int)kind]);
        root.transform.position = origin; root.transform.rotation = yawRot;
        var model = loaded.Model;
        var visual = model.SceneIndex("Visual"); if (visual < 0) visual = model.DefaultScene;
        // -2: no node is in it (a node in no scene has -1, which a missing scene would match).
        var collision = solid ? model.SceneIndex("CollisionProxies") : -2; if (collision < 0) collision = -2;
        var objects = new GameObject[model.Nodes.Length];
        for (var n = 0; n < model.Nodes.Length; n++)
        {
            var node = model.Nodes[n];
            if (node.Scene != visual && node.Scene != collision) continue;
            var go = new GameObject(node.Name);
            objects[n] = go;
            var parent = node.Parent >= 0 && node.Parent < n && objects[node.Parent] != null ? objects[node.Parent].transform : root.transform;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(-node.X, node.Y, node.Z);
            go.transform.localRotation = new Quaternion(node.QX, -node.QY, -node.QZ, node.QW);
            go.transform.localScale = new Vector3(node.SX, node.SY, node.SZ);
            if (node.Mesh < 0) continue;
            if (node.Scene == collision)
            {
                var collider = go.AddComponent<MeshCollider>();
                collider.sharedMesh = loaded.Meshes[node.Mesh];
                go.layer = 0;
                colliders++;
            }
            else
            {
                go.AddComponent<MeshFilter>().sharedMesh = loaded.Meshes[node.Mesh];
                var mesh = model.Meshes[node.Mesh];
                go.AddComponent<MeshRenderer>().sharedMaterial = mesh.Material >= 0 && mesh.Material < loaded.Materials.Length ? loaded.Materials[mesh.Material] : plain;
            }
        }
        return root;
    }

    // The halfpipe follows the server's own blocks: a 15 degree tile and a 35
    // degree tile up from the flat (a halfpipe has that on both ends of its 6 m flat, with a 3 m
    // deck on top). The authored models are circular and shorter, so they would leave the tiles
    // standing out of them; these are made here instead, on the tiles' tops, with the bends
    // rounded (radius Bend) so the board rolls through them, and the coping edge left sharp.
    public const double Bend = 3.0;
    private const int BendSteps = 8;

    // The riding profile, (forward, height) points in the piece's frame. The quarterpipe starts at
    // its entry (the first tile's low edge); the halfpipe's frame is the middle of its flat.
    // The quarterpipes' profile (Codex's TryQuarterPoint, #470): level from QuarterStart to the foot
    // at -1.2, a quarter circle of radius r in 30 equal 3 degree faces up to the lip, then the level
    // deck to the back. The same points make the drawn
    // surface and the collider; the closed shell goes down to -QuarterSkin.
    public const double QuarterStart = -1.4, QuarterFoot = -1.2, QuarterHalfWidth = 1.4, QuarterSkin = 0.08, CopingRadius = 0.05, RailRadius = 0.05;
    public const int QuarterSteps = 30;
    public static void QuarterSize(Kind kind, out double radius, out double height, out double back)
    {
        if (kind == Kind.MiniQuarterPipe) { radius = 1.2; height = 1.2; back = 1.4; }
        // No straight vert anywhere (owner, 2026-10-16): the large one has the plain one's 3 m
        // circle (#470, c7d9b3c), with its apron beside it.
        else { radius = 3; height = 3; back = 4.4; }
    }
    public static List<Vector2> QuarterProfile(Kind kind)
    {
        double r, h, back; QuarterSize(kind, out r, out h, out back);
        var pts = new List<Vector2> { new Vector2((float)QuarterStart, 0f) };
        for (var k = 0; k <= QuarterSteps; k++)
        {
            var th = Math.PI / 2 * k / QuarterSteps;
            pts.Add(k == QuarterSteps ? new Vector2((float)(QuarterFoot + r), (float)r) : new Vector2((float)(QuarterFoot + r * Math.Sin(th)), (float)(r * (1 - Math.Cos(th)))));
        }
        if (h > r) pts.Add(new Vector2((float)(QuarterFoot + r), (float)h));
        pts.Add(new Vector2((float)back, (float)h));
        return pts;
    }

    // The half-wall kicker: a short run-in, a rounded foot and the 30 degree face up to the wall's top
    // (1.5 m up over 2.598 m). The rounding lies above the native slab, so the slab never shows through.
    public const double KickerRunIn = -0.8, KickerBend = 1.2;

    // The MEGA halfpipe, from the flat's middle, as SkateMegaHalfpipe.TryProfilePoint (#460): 124
    // samples, deck ends at +-(6+R+3), 60 equal steps per transition on the chords' circle, every
    // height MegaLift above the flat. The straight native chords stand up to 0.12 m inside that
    // circle; their colliders are kept from the rider and the ride's queries (BackingIds), so this
    // surface is the only one the board and the body meet.
    public const int MegaSteps = 60;
    public static List<Vector2> MegaProfile()
    {
        double r = MegaRadius, half = MegaFlat / 2, lift = MegaLift - Lift, deck = r + lift;
        var pts = new List<Vector2>();
        pts.Add(new Vector2((float)-(half + r + MegaDeck), (float)deck));
        for (var k = MegaSteps; k >= 0; k--)
        {
            var th = Math.PI / 2 * k / MegaSteps;
            pts.Add(new Vector2((float)-(half + r * Math.Sin(th)), (float)(r * (1 - Math.Cos(th)) + lift)));
        }
        for (var k = 0; k <= MegaSteps; k++)
        {
            var th = Math.PI / 2 * k / MegaSteps;
            pts.Add(new Vector2((float)(half + r * Math.Sin(th)), (float)(r * (1 - Math.Cos(th)) + lift)));
        }
        pts.Add(new Vector2((float)(half + r + MegaDeck), (float)deck));
        return pts;
    }

    public static List<Vector2> TransitionProfile(Kind kind)
    {
        if (IsQuarter(kind)) return QuarterProfile(kind);
        if (kind == Kind.MegaHalfpipe) return MegaProfile();
        double run1 = 2 * HalfLength * Math.Cos(15 * Degrees), rise1 = 2 * HalfLength * Math.Sin(15 * Degrees);
        double run2 = 2 * HalfLength * Math.Cos(35 * Degrees), rise2 = 2 * HalfLength * Math.Sin(35 * Degrees);
        var corners = new List<Vector2>(); var radii = new List<double>();
        if (kind == Kind.HalfWallKicker)
        {
            corners.Add(new Vector2((float)KickerRunIn, 0f)); radii.Add(0);
            corners.Add(new Vector2(0f, 0f)); radii.Add(KickerBend);
            corners.Add(new Vector2((float)(2 * HalfLength * Math.Cos(30 * Degrees)), (float)(2 * HalfLength * Math.Sin(30 * Degrees)))); radii.Add(0);
        }
        else
        {
            double half = 2 * HalfLength, deck = 2 * HalfLength, top = rise1 + rise2;
            foreach (var side in new[] { -1, 1 })
            {
                var pts = new[] {
                    new Vector2((float)(side * (half + run1 + run2 + deck)), (float)top),
                    new Vector2((float)(side * (half + run1 + run2)), (float)top),
                    new Vector2((float)(side * (half + run1)), (float)rise1),
                    new Vector2((float)(side * half), 0f) };
                var rs = new[] { 0.0, 0.0, Bend, Bend };
                if (side < 0) for (var i = 0; i < 4; i++) { corners.Add(pts[i]); radii.Add(rs[i]); }
                else for (var i = 3; i >= 0; i--) { corners.Add(pts[i]); radii.Add(rs[i]); }
            }
        }
        // Plain arithmetic on the coordinates: the engine's vector operators are not counted on.
        var outPts = new List<Vector2>();
        for (var i = 0; i < corners.Count; i++)
        {
            if (i == 0 || i == corners.Count - 1 || radii[i] <= 0) { outPts.Add(corners[i]); continue; }
            double ax = corners[i - 1].x, ay = corners[i - 1].y, bx = corners[i].x, by = corners[i].y, cx = corners[i + 1].x, cy = corners[i + 1].y;
            double l1 = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)), l2 = Math.Sqrt((cx - bx) * (cx - bx) + (cy - by) * (cy - by));
            double d1x = (bx - ax) / l1, d1y = (by - ay) / l1, d2x = (cx - bx) / l2, d2y = (cy - by) / l2;
            var turn = Math.Acos(Math.Max(-1.0, Math.Min(1.0, d1x * d2x + d1y * d2y)));
            if (turn < 0.001) { outPts.Add(corners[i]); continue; }
            var t = Math.Min(radii[i] * Math.Tan(turn / 2), Math.Min(l1, l2) * 0.45);
            var r = t / Math.Tan(turn / 2);
            double sx = bx - d1x * t, sy = by - d1y * t;
            // The centre is on the inside of the bend: to the left of the way when it bends up.
            var cross = d1x * d2y - d1y * d2x; var sign = cross > 0 ? 1.0 : -1.0;
            double ox = sx - d1y * r * sign, oy = sy + d1x * r * sign;
            double fx = sx - ox, fy = sy - oy;
            // A step at most every 4 degrees, so the board meets no kink in a sharp bend either.
            var steps = Math.Max(BendSteps, (int)Math.Ceiling(turn * 180.0 / Math.PI / 4.0));
            for (var k = 0; k <= steps; k++)
            {
                var ang = turn * k / steps * sign;
                double cs = Math.Cos(ang), sn = Math.Sin(ang);
                outPts.Add(new Vector2((float)(ox + fx * cs - fy * sn), (float)(oy + fx * sn + fy * cs)));
            }
        }
        return outPts;
    }

    private static GameObject Transition(Kind kind, Vector3 origin, Quaternion yawRot, bool solid, out int colliders, bool active = true, List<Mesh> owned = null)
    {
        colliders = 0;
        if (plain == null) plain = Out.Keep(MaterialFor(new SkateModelMaterial()));
        var material = plain;
        try { var loaded = Model(IsQuarter(kind) ? Kind.QuarterPipe : kind); if (loaded != null && loaded.Materials != null && loaded.Materials.Length > 0 && loaded.Materials[0] != null) material = loaded.Materials[0]; }
        catch (Exception) { }
        var profile = TransitionProfile(kind);
        var halfWidth = (float)(IsQuarter(kind) ? QuarterHalfWidth : kind == Kind.MegaHalfpipe ? 2 * 3.0 : kind == Kind.HalfWallKicker ? HalfLength : 2 * HalfLength);
        var root = new GameObject("skate_park_" + fileStems[(int)kind]);
        try
        {
            root.SetActive(active);
            root.transform.position = origin; root.transform.rotation = yawRot;
            var surface = Own(SurfaceMesh(profile, halfWidth), owned);
            var visual = new GameObject("VISUAL__transition"); visual.transform.SetParent(root.transform, false);
            visual.AddComponent<MeshFilter>().sharedMesh = Own(SolidMesh(profile, halfWidth, IsQuarter(kind) ? -(float)QuarterSkin : 0f), owned);
            visual.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (solid)
            {
                var proxy = new GameObject("COLLISION__transition"); proxy.transform.SetParent(root.transform, false);
                proxy.AddComponent<MeshCollider>().sharedMesh = surface; proxy.layer = 0; colliders++;
            }
            if (IsQuarter(kind)) colliders += Fittings(kind, root, material, solid, owned);
            if (kind == Kind.MegaHalfpipe) MegaFittings(root, material, owned);
            return root;
        }
        catch { root.SetActive(false); UnityEngine.Object.Destroy(root); throw; }
    }

    // The coping, a round metal pipe along the lip with round ends and a capsule collider to grind
    // and stall on; the large one's apron, a level closed plate beside its entry. No guard rail: the
    // deck stays open.
    private static int Fittings(Kind kind, GameObject root, Material material, bool solid, List<Mesh> owned = null)
    {
        double r, h, back; QuarterSize(kind, out r, out h, out back);
        float w = (float)QuarterHalfWidth, lip = (float)(QuarterFoot + r), radius = (float)CopingRadius;
        var added = 0;
        var coping = new GameObject("VISUAL__coping"); coping.transform.SetParent(root.transform, false);
        coping.transform.localPosition = new Vector3(0f, (float)h, lip);
        coping.AddComponent<MeshFilter>().sharedMesh = Own(Pipe(w, radius, 16), owned);
        coping.AddComponent<MeshRenderer>().sharedMaterial = material;
        if (solid)
        {
            var grip = new GameObject("COLLISION__coping"); grip.transform.SetParent(coping.transform, false); grip.layer = 0;
            var capsule = grip.AddComponent<CapsuleCollider>();
            capsule.direction = 0; capsule.radius = radius; capsule.height = 2 * (w + radius);
            added++;
        }
        if (kind != Kind.LargeQuarterPipe) return added;
        var apron = new List<Vector2> { new Vector2(-1.4f, 0f), new Vector2(1.4f, 0f) };
        var plate = new GameObject("VISUAL__apron"); plate.transform.SetParent(root.transform, false);
        plate.transform.localPosition = new Vector3(2.9f, 0f, 0f);
        plate.AddComponent<MeshFilter>().sharedMesh = Own(SolidMesh(apron, 1.5f, -(float)QuarterSkin), owned);
        plate.AddComponent<MeshRenderer>().sharedMaterial = material;
        if (!solid) return added;
        var floor = new GameObject("COLLISION__apron"); floor.transform.SetParent(plate.transform, false);
        floor.AddComponent<MeshCollider>().sharedMesh = Own(SurfaceMesh(apron, 1.5f), owned); floor.layer = 0;
        return added + 1;
    }

    // A round pipe along x from -half to half, with hemispherical ends: rings of the given number of
    // sides, the caps closed in quarter-circle steps.
    private static Mesh Pipe(float half, float radius, int sides)
    {
        var verts = new List<Vector3>(); var tris = new List<int>();
        const int capSteps = 4;
        // Rings from the left pole to the right pole: x and ring radius of each.
        var rings = new List<Vector2>();
        for (var k = 0; k <= capSteps; k++) { var a = Math.PI / 2 * k / capSteps; rings.Add(new Vector2(-half - radius * (float)Math.Cos(a), radius * (float)Math.Sin(a))); }
        for (var k = 0; k <= capSteps; k++) { var a = Math.PI / 2 * k / capSteps; rings.Add(new Vector2(half + radius * (float)Math.Sin(a), radius * (float)Math.Cos(a))); }
        foreach (var ring in rings)
            for (var j = 0; j < sides; j++)
            {
                var a = 2 * Math.PI * j / sides;
                verts.Add(new Vector3(ring.x, ring.y * (float)Math.Cos(a), ring.y * (float)Math.Sin(a)));
            }
        for (var i = 0; i < rings.Count - 1; i++)
            for (var j = 0; j < sides; j++)
            {
                int a = i * sides + j, b = i * sides + (j + 1) % sides, c = a + sides, d = b + sides;
                tris.Add(a); tris.Add(b); tris.Add(c); tris.Add(b); tris.Add(d); tris.Add(c);
            }
        var mesh = new Mesh(); mesh.name = "coping";
        mesh.vertices = verts.ToArray(); mesh.triangles = tris.ToArray(); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    // Coping at both lips, and a guard rail 1 m high along the back of each deck; the sides stay open.
    private static void MegaFittings(GameObject root, Material material, List<Mesh> owned = null)
    {
        float w = 6f, deck = (float)(MegaRadius + MegaLift - Lift), lip = (float)(MegaFlat / 2 + MegaRadius), back = (float)(MegaFlat / 2 + MegaRadius + MegaDeck) - 0.05f;
        var bars = new List<Vector3[]>();
        foreach (var side in new[] { -1f, 1f })
        {
            bars.Add(new[] { new Vector3(-w, deck, side * lip), new Vector3(w, deck, side * lip) });
            bars.Add(new[] { new Vector3(-w, deck + 1f, side * back), new Vector3(w, deck + 1f, side * back) });
            for (var x = -w; x <= w + 0.01f; x += 3f) bars.Add(new[] { new Vector3(x, deck, side * back), new Vector3(x, deck + 1f, side * back) });
        }
        var metal = new GameObject("VISUAL__rails"); metal.transform.SetParent(root.transform, false);
        metal.AddComponent<MeshFilter>().sharedMesh = Own(Bars(bars, (float)RailRadius), owned);
        metal.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    // Square tubes along each pair of points.
    private static Mesh Bars(List<Vector3[]> bars, float radius)
    {
        var verts = new List<Vector3>(); var tris = new List<int>();
        foreach (var bar in bars)
        {
            Vector3 a = bar[0], b = bar[1];
            var along = new Vector3(b.x - a.x, b.y - a.y, b.z - a.z);
            var side = Math.Abs(along.y) > Math.Abs(along.x) + Math.Abs(along.z) ? new Vector3(radius, 0f, 0f) : new Vector3(0f, radius, 0f);
            var other = Vector3.Cross(along, side); var m = other.magnitude; if (m < 1e-6f) continue;
            other = new Vector3(other.x / m * radius, other.y / m * radius, other.z / m * radius);
            var start = verts.Count;
            var corners = new[] { side + other, other - side, Vector3.zero - side - other, side - other };
            foreach (var c in corners) { verts.Add(a + c); verts.Add(b + c); }
            for (var k = 0; k < 4; k++)
            {
                int i0 = start + k * 2, i1 = start + ((k + 1) % 4) * 2;
                tris.Add(i0); tris.Add(i1); tris.Add(i0 + 1); tris.Add(i1); tris.Add(i1 + 1); tris.Add(i0 + 1);
                tris.Add(i0); tris.Add(i0 + 1); tris.Add(i1); tris.Add(i1); tris.Add(i0 + 1); tris.Add(i1 + 1);
            }
        }
        var mesh = new Mesh(); mesh.name = "rails";
        mesh.vertices = verts.ToArray(); mesh.triangles = tris.ToArray(); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    // The server rests a piece so that its lowest tile corner clears the ground, which leaves the
    // entry (the first tile's top, where the piece is drawn from) a tile's thickness above it: an
    // edge the board stops at (a funbox at both its ends). A short wedge from the ground up to the entry takes it away. Returns
    // the colliders added (0 when the entry is on the ground or the ground is not found).
    public const float EntrySlope = 8f, EntryMost = 0.6f, EntryLook = 0.4f;
    public static float LastEntryDrop = -1f;
    public const float FunboxEnd = 4.4f;
    private static int Entry(GameObject root, float entryForward, float outward, List<Mesh> owned = null)
    {
        var t = root.transform;
        var at = t.position + t.rotation * new Vector3(0f, 0f, entryForward);
        // The ground is looked for a little before the entry, clear of the piece's own tiles, and may
        // be a building's floor: seen live, the owner's park stands on his foundations, and a ray that
        // left construction out found no ground, so no wedge was made and the board stopped at the lip.
        var before = at - t.rotation * new Vector3(0f, 0f, outward < 0f ? EntryLook : -EntryLook);
        RaycastHit hit;
        if (!Physics.Raycast(before + Vector3.up * 0.3f, Vector3.down, out hit, 0.3f + EntryMost, SkateRide.GroundMask, QueryTriggerInteraction.Ignore)) return 0;
        var drop = at.y - hit.point.y;
        LastEntryDrop = drop;
        if (drop < Lift + 0.03f || drop > EntryMost) return 0;
        var length = Math.Min(drop / (float)Math.Tan(EntrySlope * Degrees), 3f);
        // A little below the ground at its start, so no lip is left there either.
        // Points in rising forward order, so the strip faces up either way.
        var profile = outward < 0f
            ? new List<Vector2> { new Vector2(entryForward - length, -drop - 0.03f), new Vector2(entryForward, 0f) }
            : new List<Vector2> { new Vector2(entryForward, 0f), new Vector2(entryForward + length, -drop - 0.03f) };
        var surface = Own(SurfaceMesh(profile, (float)HalfLength), owned);
        var wedge = new GameObject("COLLISION__entry"); wedge.transform.SetParent(t, false);
        wedge.AddComponent<MeshCollider>().sharedMesh = surface; wedge.layer = 0;
        if (plain == null) plain = MaterialFor(new SkateModelMaterial());
        var look = new GameObject("VISUAL__entry"); look.transform.SetParent(t, false);
        look.AddComponent<MeshFilter>().sharedMesh = surface;
        look.AddComponent<MeshRenderer>().sharedMaterial = plain;
        return 1;
    }

    // The riding surface alone, for the collider: a strip across the width along the profile.
    private static Mesh SurfaceMesh(List<Vector2> profile, float halfWidth)
    {
        var n = profile.Count; var verts = new Vector3[n * 2]; var tris = new int[(n - 1) * 6];
        for (var i = 0; i < n; i++) { verts[i * 2] = new Vector3(-halfWidth, profile[i].y, profile[i].x); verts[i * 2 + 1] = new Vector3(halfWidth, profile[i].y, profile[i].x); }
        for (var i = 0; i < n - 1; i++)
        {
            int a = i * 2, b = a + 1, c = a + 2, d = a + 3, t = i * 6;
            tris[t] = a; tris[t + 1] = c; tris[t + 2] = b; tris[t + 3] = b; tris[t + 4] = c; tris[t + 5] = d;
        }
        var mesh = new Mesh(); mesh.name = "transition surface";
        mesh.vertices = verts; mesh.triangles = tris; mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    // The drawn piece: the surface, the two sides down to the ground and the ends down to it, each
    // face with vertices of its own so its edges stay sharp.
    private static Mesh SolidMesh(List<Vector2> profile, float halfWidth, float bottom = 0f)
    {
        var verts = new List<Vector3>(); var tris = new List<int>(); var uv = new List<Vector2>();
        var n = profile.Count;
        // The surface.
        var along = 0f;
        for (var i = 0; i < n; i++)
        {
            if (i > 0) along += (float)Math.Sqrt((profile[i].x - profile[i - 1].x) * (profile[i].x - profile[i - 1].x) + (profile[i].y - profile[i - 1].y) * (profile[i].y - profile[i - 1].y));
            verts.Add(new Vector3(-halfWidth, profile[i].y, profile[i].x)); uv.Add(new Vector2(0f, along / 2f));
            verts.Add(new Vector3(halfWidth, profile[i].y, profile[i].x)); uv.Add(new Vector2(halfWidth, along / 2f));
        }
        for (var i = 0; i < n - 1; i++) { int a = i * 2; tris.Add(a); tris.Add(a + 2); tris.Add(a + 1); tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3); }
        // The sides: each surface point down to the ground.
        foreach (var side in new[] { -1f, 1f })
        {
            var start = verts.Count;
            for (var i = 0; i < n; i++)
            {
                verts.Add(new Vector3(side * halfWidth, profile[i].y, profile[i].x)); uv.Add(new Vector2(profile[i].x / 2f, profile[i].y / 2f));
                verts.Add(new Vector3(side * halfWidth, bottom, profile[i].x)); uv.Add(new Vector2(profile[i].x / 2f, bottom / 2f));
            }
            for (var i = 0; i < n - 1; i++)
            {
                int a = start + i * 2;
                if (side < 0) { tris.Add(a); tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 2); tris.Add(a + 1); tris.Add(a + 3); }
                else { tris.Add(a); tris.Add(a + 2); tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3); tris.Add(a + 1); }
            }
        }
        // The ends: from the first and the last point down to the ground, where they stand up.
        foreach (var end in new[] { 0, n - 1 })
        {
            var p = profile[end]; if (p.y - bottom < 0.01f) continue;
            var start = verts.Count; var facing = end == 0 ? -1f : 1f;
            verts.Add(new Vector3(-halfWidth, p.y, p.x)); verts.Add(new Vector3(halfWidth, p.y, p.x));
            verts.Add(new Vector3(-halfWidth, bottom, p.x)); verts.Add(new Vector3(halfWidth, bottom, p.x));
            uv.Add(new Vector2(0f, p.y)); uv.Add(new Vector2(halfWidth, p.y)); uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(halfWidth, 0f));
            if (facing > 0) { tris.Add(start); tris.Add(start + 1); tris.Add(start + 2); tris.Add(start + 1); tris.Add(start + 3); tris.Add(start + 2); }
            else { tris.Add(start); tris.Add(start + 2); tris.Add(start + 1); tris.Add(start + 1); tris.Add(start + 2); tris.Add(start + 3); }
        }
        // A closed shell (the quarterpipes) also has its underside, facing down.
        if (bottom < 0f)
        {
            var start = verts.Count; float x0 = profile[0].x, x1 = profile[n - 1].x;
            verts.Add(new Vector3(-halfWidth, bottom, x0)); verts.Add(new Vector3(halfWidth, bottom, x0));
            verts.Add(new Vector3(-halfWidth, bottom, x1)); verts.Add(new Vector3(halfWidth, bottom, x1));
            uv.Add(new Vector2(0f, x0)); uv.Add(new Vector2(halfWidth, x0)); uv.Add(new Vector2(0f, x1)); uv.Add(new Vector2(halfWidth, x1));
            tris.Add(start); tris.Add(start + 1); tris.Add(start + 2); tris.Add(start + 1); tris.Add(start + 3); tris.Add(start + 2);
        }
        var cols = new Color[verts.Count]; for (var i = 0; i < cols.Length; i++) cols[i] = Color.white;
        var mesh = new Mesh(); mesh.name = "transition";
        mesh.vertices = verts.ToArray(); mesh.uv = uv.ToArray(); mesh.colors = cols; mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    // The native colliders under a MEGA halfpipe, by instance ID (#460's contract): the ride's ground,
    // wall, step and grind queries pass through them to the replacement surface or the world, and
    // the rider's own colliders ignore them while on the board. Ordinary world collisions, other
    // players and the game's layers are untouched.
    public static readonly HashSet<int> BackingIds = new HashSet<int>();
    private static int backingVersion;

    // A ray as Physics.Raycast, but the nearest hit that is not MEGA backing.
    public static bool Ray(Vector3 from, Vector3 dir, out RaycastHit hit, float distance, int mask)
    {
        if (BackingIds.Count == 0) return Physics.Raycast(from, dir, out hit, distance, mask, QueryTriggerInteraction.Ignore);
        var all = Physics.RaycastAll(from, dir, distance, mask, QueryTriggerInteraction.Ignore);
        hit = default(RaycastHit); var found = false; var best = float.MaxValue;
        for (var i = 0; i < all.Length; i++) found |= Take(all[i], false, ref best, ref hit);
        return found;
    }
    public static bool Sphere(Vector3 from, float radius, Vector3 dir, out RaycastHit hit, float distance, int mask)
    {
        if (BackingIds.Count == 0) return Physics.SphereCast(from, radius, dir, out hit, distance, mask, QueryTriggerInteraction.Ignore);
        var all = Physics.SphereCastAll(from, radius, dir, distance, mask, QueryTriggerInteraction.Ignore);
        hit = default(RaycastHit); var found = false; var best = float.MaxValue;
        for (var i = 0; i < all.Length; i++) found |= Take(all[i], true, ref best, ref hit);
        return found;
    }
    // Passing a riding face must not hide a different wall farther along the same sweep.
    // Keep the nearest obstruction among all hits, with the same backing/initial-overlap policy.
    public static bool SphereWall(Vector3 from, float radius, Vector3 dir, out RaycastHit hit, float distance, int mask,
                                  Func<Vector3, Vector3, float, RaycastHit, bool> ridingFace)
    {
        if (RampIds.Count == 0)
            return Sphere(from, radius, dir, out hit, distance, mask) && hit.m_Normal.y < 0.5f;
        var all = Physics.SphereCastAll(from, radius, dir, distance, mask, QueryTriggerInteraction.Ignore);
        // A dense sweep keeps every steep hit as a wall rather than running many face queries.
        var testFaces = all.Length <= 256;
        hit = default(RaycastHit); var found = false; var best = float.MaxValue;
        for (var i = 0; i < all.Length; i++)
        {
            var candidate = default(RaycastHit); var any = float.MaxValue;
            if (!Take(all[i], true, ref any, ref candidate) || candidate.distance >= best ||
                !(candidate.m_Normal.y < 0.5f) || (testFaces && ridingFace(from, dir, distance, candidate))) continue;
            hit = candidate; best = candidate.distance; found = true;
        }
        return found;
    }
    // A sweep that starts inside a collider reports it at distance 0 with no point, which a plain
    // SphereCast leaves out: so does this.
    private static bool Take(RaycastHit h, bool sweep, ref float best, ref RaycastHit hit)
    {
        if (sweep && h.distance <= 0f && h.point == Vector3.zero) return false;
        int id;
        try { var c = h.collider; if (c == null) return false; id = c.GetInstanceID(); } catch (Exception) { return false; }
        if (BackingIds.Contains(id) || h.distance >= best) return false;
        best = h.distance; hit = h;
        return true;
    }

    // Pair isolation between the rider's colliders and the backing, while on the board. Off the
    // board each pair is restored only once the two no longer overlap, so the body is never left
    // inside a chord; a pair whose backing has gone (piece removed, mismatch) is restored the same way.
    private static readonly List<Collider> isolated = new List<Collider>();
    private static readonly HashSet<int> isolatedIds = new HashSet<int>();
    private static Collider[] riderColliders;
    private static int riderBody, isolatedVersion = -1, restoreWaitSaid;
    private static float nextRestore;
    public static int Isolated { get { return isolated.Count; } }
    public static void Isolate(bool riding, Rigidbody body, float now)
    {
        if (body == null)
        {
            // The body is gone with its colliders: nothing is left to restore.
            if (isolated.Count > 0) { isolated.Clear(); isolatedIds.Clear(); }
            riderColliders = null; riderBody = 0; return;
        }
        if (riderColliders == null || body.GetInstanceID() != riderBody)
        {
            isolated.Clear(); isolatedIds.Clear(); isolatedVersion = -1;
            riderBody = body.GetInstanceID();
            var cols = body.GetComponentsInChildren<Collider>(true);
            var list = new List<Collider>();
            for (var i = 0; i < cols.Length; i++) if (cols[i] != null && !cols[i].isTrigger) list.Add(cols[i]);
            riderColliders = list.ToArray();
        }
        if (riding && BackingIds.Count > 0)
        {
            if (isolatedVersion != backingVersion)
            {
                var added = 0;
                foreach (var p in placed)
                    foreach (var c in p.Backing)
                    {
                        int id;
                        try { if (c == null) continue; id = c.GetInstanceID(); } catch (Exception) { continue; }
                        if (isolatedIds.Add(id)) { isolated.Add(c); added++; }
                        foreach (var r in riderColliders) if (r != null) Physics.IgnoreCollision(r, c, true);
                    }
                isolatedVersion = backingVersion;
                if (added > 0) Say("MEGA isolation: " + added + " backing colliders ignored by the rider's " + riderColliders.Length);
            }
            if (isolated.Count == 0) return;
        }
        if (isolated.Count == 0 || now < nextRestore) return;
        nextRestore = now + 0.1f;
        var waiting = 0;
        for (var i = isolated.Count - 1; i >= 0; i--)
        {
            var c = isolated[i];
            try
            {
                if (c == null) { isolated.RemoveAt(i); continue; }
                var id = c.GetInstanceID();
                // Still backing a MEGA while riding: kept.
                if (riding && BackingIds.Contains(id)) continue;
                if (Overlaps(c)) { waiting++; continue; }
                foreach (var r in riderColliders) Physics.IgnoreCollision(r, c, false);
                isolated.RemoveAt(i); isolatedIds.Remove(id);
            }
            catch (Exception) { isolated.RemoveAt(i); }
        }
        if (waiting > 0 && waiting != restoreWaitSaid) Say("MEGA isolation: " + waiting + " pairs wait, the rider still overlaps their backing");
        restoreWaitSaid = waiting;
        if (isolated.Count == 0) { isolatedVersion = -1; Say("MEGA isolation: all pairs restored"); }
    }
    private static bool Overlaps(Collider c)
    {
        foreach (var r in riderColliders)
        {
            Vector3 dir; float dist;
            if (r != null && Physics.ComputePenetration(r, r.transform.position, r.transform.rotation, c, c.transform.position, c.transform.rotation, out dir, out dist)) return true;
        }
        return false;
    }

    // The drawn riding surface and coping of a transition, by collider ID: the ride takes them as
    // ground up to near vertical and never as a wall, so a quarterpipe is ridden up to its lip.
    public static readonly HashSet<int> RampIds = new HashSet<int>();
    // The coping alone (a subset of RampIds): the ride's contact on a face (SkateRide.RampContact)
    // is the surface, never the pipe on its lip; the coping stays for grinds and stalls.
    public static readonly HashSet<int> CopingIds = new HashSet<int>();
    public static bool IsRamp(RaycastHit hit)
    {
        if (RampIds.Count == 0) return false;
        try { var c = hit.collider; return c != null && RampIds.Contains(c.GetInstanceID()); } catch (Exception) { return false; }
    }
    public static bool IsCoping(RaycastHit hit)
    {
        if (CopingIds.Count == 0) return false;
        try { var c = hit.collider; return c != null && CopingIds.Contains(c.GetInstanceID()); } catch (Exception) { return false; }
    }
    private static readonly List<Vector2> megaLipProfile = MegaProfile();
    // Only the two internal curve/deck creases of our generated, open MEGA surface. A sphere's
    // edge normal can agree with the curve while the ray to that same point selects the deck.
    // Prove the owned frame, interior width, exact crease and the adjacent faces' upper normal
    // cone; side/end edges, undersides, coping and every foreign collider remain obstructions.
    public static bool MegaLipFace(Vector3 from, Vector3 dir, float radius, float distance, RaycastHit hit)
    {
        if (!(radius > 0f && radius <= 1f)) return false;
        try
        {
            var collider = hit.collider;
            if (collider == null) return false;
            var id = collider.GetInstanceID();
            if (!RampIds.Contains(id) || CopingIds.Contains(id)) return false;
            foreach (var p in placed)
            {
                if (p.Kind != Kind.MegaHalfpipe || !p.Ramp.Contains(id) || p.Root == null || !p.Root.activeInHierarchy) continue;
                var t = p.Root.transform;
                if (t.parent != null || t.position != p.Origin || t.localScale != Vector3.one ||
                    t.rotation != Quaternion.Euler(0f, p.Yaw, 0f)) return false;
                var sin = (float)Math.Sin(p.Yaw * Degrees); var cos = (float)Math.Cos(p.Yaw * Degrees);
                var point = hit.m_Point - p.Origin; var centre = from + dir * hit.distance - p.Origin;
                var start = from - p.Origin; var finish = from + dir * distance - p.Origin;
                var n = hit.m_Normal;
                point = new Vector3(point.x * cos - point.z * sin, point.y, point.x * sin + point.z * cos);
                centre = new Vector3(centre.x * cos - centre.z * sin, centre.y, centre.x * sin + centre.z * cos);
                start = new Vector3(start.x * cos - start.z * sin, start.y, start.x * sin + start.z * cos);
                finish = new Vector3(finish.x * cos - finish.z * sin, finish.y, finish.x * sin + finish.z * cos);
                n = new Vector3(n.x * cos - n.z * sin, n.y, n.x * sin + n.z * cos);
                const float tolerance = 0.01f;
                var end0 = megaLipProfile[0].x + radius + tolerance;
                var end1 = megaLipProfile[megaLipProfile.Count - 1].x - radius - tolerance;
                if (!(Math.Abs(start.x) < 6f - radius - tolerance && Math.Abs(finish.x) < 6f - radius - tolerance &&
                      start.z > end0 && start.z < end1 && finish.z > end0 && finish.z < end1 &&
                      Math.Abs(n.x) < tolerance && n.y >= 0f && Math.Abs(n.sqrMagnitude - 1f) < tolerance &&
                      (centre - point - n * radius).sqrMagnitude <= tolerance * tolerance)) return false;
                for (var end = 0; end < 2; end++)
                {
                    var i = end == 0 ? 1 : megaLipProfile.Count - 2;
                    var a = megaLipProfile[i - 1]; var edge = megaLipProfile[i]; var b = megaLipProfile[i + 1];
                    if (!(Math.Abs(point.z - edge.x) <= tolerance && Math.Abs(point.y - edge.y) <= tolerance)) continue;
                    var before = new Vector3(0f, edge.x - a.x, a.y - edge.y).normalized;
                    var after = new Vector3(0f, b.x - edge.x, edge.y - b.y).normalized;
                    return before.y * n.z - before.z * n.y >= -tolerance &&
                           n.y * after.z - n.z * after.y >= -tolerance;
                }
                return false;
            }
        }
        catch (Exception) { }
        return false;
    }
    private static Mesh Own(Mesh mesh, List<Mesh> owned)
    {
        if (owned != null) owned.Add(mesh);
        return mesh;
    }

    private static void ReadRamp(Placed p, GameObject root)
    {
        foreach (var collider in root.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null) continue;
            var name = collider.gameObject.name;
            if (name != "COLLISION__transition" && name != "COLLISION__coping") continue;
            var id = collider.GetInstanceID(); p.Ramp.Add(id);
            if (name == "COLLISION__coping") p.Coping.Add(id);
        }
        if (p.Ramp.Count == 0) throw new InvalidOperationException("The park riding surface is unavailable.");
    }

    private static void Remove(Placed p)
    {
        if (p.Root != null) p.Root.SetActive(false);
        Colliders -= p.ColliderCount; p.ColliderCount = 0;
        foreach (var id in p.Ramp) { RampIds.Remove(id); CopingIds.Remove(id); }
        p.Ramp.Clear(); p.Coping.Clear();
        if (p.Backing.Count > 0)
        {
            foreach (var c in p.Backing) { try { if (c != null) BackingIds.Remove(c.GetInstanceID()); } catch (Exception) { } }
            p.Backing.Clear(); backingVersion++;
        }
        foreach (var r in p.Hidden) { try { if (r != null) r.enabled = true; } catch (Exception) { } HiddenRenderers--; }
        p.Hidden.Clear();
        if (p.Root != null) { try { UnityEngine.Object.Destroy(p.Root); } catch (Exception) { } p.Root = null; }
        foreach (var mesh in p.Meshes) if (mesh != null) UnityEngine.Object.Destroy(mesh);
        p.Meshes.Clear();
    }

    public static void Clear()
    {
        foreach (var p in placed) Remove(p);
        placed.Clear(); Assemblies = Drawn = 0;
        PreviewClear();
    }
    public static string KindName(Kind kind) { return fileStems[(int)kind].Substring(6); }

    // The model of a kind, read once from the plugin's folder (skate_<kind>.glb next to the plugin).
    private static Loaded Model(Kind kind)
    {
        var stem = fileStems[(int)kind];
        Loaded loaded;
        if (models.TryGetValue(stem, out loaded)) return loaded;
        loaded = new Loaded(); models[stem] = loaded;
        var path = System.IO.Path.Combine(Paths.PluginPath, stem + ".glb");
        try
        {
            if (!System.IO.File.Exists(path)) { loaded.Error = "no " + stem + ".glb next to the plugin"; Status = loaded.Error; return loaded; }
            var bytes = System.IO.File.ReadAllBytes(path);
            SkateModel model; string error;
            if (!SkateModel.TryParse(bytes, out model, out error)) { loaded.Error = stem + ": " + error; Status = loaded.Error; Say(loaded.Error); return loaded; }
            loaded.Meshes = new Mesh[model.Meshes.Length];
            for (var i = 0; i < model.Meshes.Length; i++) loaded.Meshes[i] = Out.Keep(Convert(model.Meshes[i], model));
            loaded.Materials = new Material[model.Materials.Length];
            for (var i = 0; i < model.Materials.Length; i++) loaded.Materials[i] = Out.Keep(MaterialFor(model.Materials[i]));
            if (plain == null) plain = MaterialFor(new SkateModelMaterial());
            loaded.Model = model;
            var tris = 0; foreach (var m in model.Meshes) tris += m.TriangleCount;
            Say(stem + ".glb read: " + model.Nodes.Length + " nodes, " + model.Meshes.Length + " meshes, " + tris + " triangles, " + model.Materials.Length + " materials, " + bytes.Length + " bytes");
        }
        catch (Exception e) { loaded.Error = stem + ": " + e.GetType().Name + ": " + e.Message; Status = loaded.Error; Say(loaded.Error); }
        return loaded;
    }

    // glTF is right-handed with +Y up and +Z forward; the engine is left-handed with the same up
    // and forward, so x is mirrored, the winding turned round and the texture's v flipped.
    private static Mesh Convert(SkateModelMesh data, SkateModel model)
    {
        var n = data.VertexCount;
        var verts = new Vector3[n]; var norms = new Vector3[n]; var uv = new Vector2[n]; var cols = new Color[n];
        var colour = Color.white;
        if (data.Material >= 0 && data.Material < model.Materials.Length)
        { var m = model.Materials[data.Material]; colour = new Color(m.Red, m.Green, m.Blue, m.Alpha); }
        for (var i = 0; i < n; i++)
        {
            verts[i] = new Vector3(-data.Positions[i * 3], data.Positions[i * 3 + 1], data.Positions[i * 3 + 2]);
            norms[i] = new Vector3(-data.Normals[i * 3], data.Normals[i * 3 + 1], data.Normals[i * 3 + 2]);
            uv[i] = data.UVs.Length > 0 ? new Vector2(data.UVs[i * 2], 1f - data.UVs[i * 2 + 1]) : new Vector2(0f, 0f);
            cols[i] = colour;
        }
        var tris = new int[data.Triangles.Length];
        for (var i = 0; i < tris.Length; i += 3) { tris[i] = data.Triangles[i]; tris[i + 1] = data.Triangles[i + 2]; tris[i + 2] = data.Triangles[i + 1]; }
        var mesh = new Mesh();
        mesh.name = data.Name;
        mesh.vertices = verts; mesh.normals = norms; mesh.uv = uv; mesh.colors = cols; mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Material MaterialFor(SkateModelMaterial m)
    {
        var colour = m.Name == null ? Color.white : new Color(m.Red, m.Green, m.Blue, m.Alpha);
        var lit = Shader.Find(SkateBoard.LitShader);
        if (lit != null)
        {
            var mat = new Material(lit); mat.color = colour;
            mat.SetFloat("_Metallic", m.Name == null ? 0f : m.Metallic); mat.SetFloat("_Glossiness", m.Name == null ? 0.3f : 1f - m.Roughness);
            return mat;
        }
        var sh = Shader.Find(SkateBoard.PlainShader);
        if (sh == null) throw new InvalidOperationException("no shader for the park models");
        var flat = new Material(sh); flat.color = Color.white;
        return flat;
    }

    private static bool Alive(BuildingBlock b)
    {
        try { return b != null && b.gameObject != null && b.gameObject.activeInHierarchy; } catch (Exception) { return false; }
    }
    private static long Cell(Vector3 p) { return ((long)Math.Floor(p.x) & 0xFFFFF) | (((long)Math.Floor(p.y) & 0xFFFFF) << 20) | (((long)Math.Floor(p.z) & 0xFFFFF) << 40); }
    private static IEnumerable<int> Near(Dictionary<long, List<int>> cells, Vector3 at)
    {
        for (var dx = -1; dx <= 1; dx++) for (var dy = -1; dy <= 1; dy++) for (var dz = -1; dz <= 1; dz++)
        {
            List<int> l;
            if (cells.TryGetValue(Cell(at + new Vector3(dx, dy, dz)), out l)) foreach (var i in l) yield return i;
        }
    }
    private static float Wrap(float degrees) { degrees %= 360f; if (degrees > 180f) degrees -= 360f; if (degrees < -180f) degrees += 360f; return degrees; }
    private static float Delta(float a, float b) { return Wrap(b - a); }
}
