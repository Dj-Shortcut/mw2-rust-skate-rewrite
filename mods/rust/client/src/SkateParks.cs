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
    public enum Kind { Bank = 0, Kicker = 1, QuarterPipe = 2, LedgeLow = 3, LedgeHigh = 4, LongLedge = 5, Funbox = 6, Halfpipe = 7 }
    private struct Part { public string Prefab; public double Right, Forward, Height, Pitch, Yaw; }
    private const double HalfLength = 1.5, Degrees = Math.PI / 180;
    private static readonly string[] fileStems = { "skate_bank", "skate_kicker", "skate_quarterpipe", "skate_ledge_low", "skate_ledge_high", "skate_long_ledge", "skate_funbox", "skate_halfpipe" };
    private static Part[][] layouts;

    private static Part[] Layout(Kind kind)
    {
        if (layouts == null) { layouts = new Part[8][]; for (var k = 0; k < 8; k++) layouts[k] = BuildLayout((Kind)k); }
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

    // A building block as the client sees it.
    private struct Block
    {
        public BuildingBlock Entity; public string Prefab; public Vector3 Position; public Quaternion Rotation;
        public float Yaw, Pitch, Top, Bottom; public int Id;
    }

    private sealed class Loaded { public SkateModel Model; public Mesh[] Meshes; public Material[] Materials; public string Error; }
    private sealed class Placed
    {
        public Kind Kind; public int[] Ids; public GameObject Root; public Vector3 Origin; public float Yaw;
        public readonly List<Renderer> Hidden = new List<Renderer>();
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
            blocks.Add(new Block { Entity = b, Prefab = prefab, Position = t.position, Rotation = t.rotation, Yaw = e.y, Pitch = Wrap(e.x), Top = top, Bottom = bottom, Id = b.GetInstanceID() });
        }
        Blocks = blocks.Count;
        // Assemblies that are still complete stay as they are; the rest go.
        var present = new HashSet<int>(); foreach (var b in blocks) present.Add(b.Id);
        for (var i = placed.Count - 1; i >= 0; i--)
        {
            var keep = true;
            foreach (var id in placed[i].Ids) if (!present.Contains(id)) { keep = false; break; }
            if (!keep) { Remove(placed[i]); placed.RemoveAt(i); }
        }
        var taken = new HashSet<int>(); foreach (var p in placed) foreach (var id in p.Ids) taken.Add(id);
        // Cells of a metre, for the neighbourhood searches.
        var cells = new Dictionary<long, List<int>>();
        for (var i = 0; i < blocks.Count; i++) { var key = Cell(blocks[i].Position); List<int> l; if (!cells.TryGetValue(key, out l)) cells[key] = l = new List<int>(); l.Add(i); }
        var matched = new List<int>();
        // Pitched kinds first (the most distinctive), the halfpipe before the quarterpipe it contains.
        var order = new[] { Kind.Halfpipe, Kind.QuarterPipe, Kind.Funbox, Kind.Kicker, Kind.Bank, Kind.LongLedge, Kind.LedgeHigh, Kind.LedgeLow };
        foreach (var kind in order)
        {
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
                var ids = new int[matched.Count];
                for (var m = 0; m < matched.Count; m++) { ids[m] = blocks[matched[m]].Id; taken.Add(ids[m]); }
                var p = new Placed { Kind = kind, Ids = ids };
                Place(p, kind, blocks, matched, anchorYaw);
                placed.Add(p);
                Say("found a " + fileStems[(int)kind].Substring(6) + " at " + Out.V1(p.Origin) + " yaw " + p.Yaw.ToString("F0") + (p.Root != null ? ", drawn" : ", not drawn: " + Status));
            }
        }
        Assemblies = placed.Count;
        var drawn = 0; foreach (var p in placed) if (p.Root != null) drawn++;
        Drawn = drawn;
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

    private static void Place(Placed p, Kind kind, List<Block> blocks, List<int> matched, float anchorYaw)
    {
        var first = blocks[matched[0]];
        var yawRot = Quaternion.Euler(0f, anchorYaw, 0f);
        Vector3 origin;
        switch (kind)
        {
            case Kind.Bank: case Kind.Kicker: case Kind.QuarterPipe:
                // The model's origin is the entry midpoint on the riding surface: the first tile's
                // low edge, on its top.
                origin = first.Position + first.Rotation * new Vector3(0f, first.Top, -(float)HalfLength);
                break;
            case Kind.Funbox:
                // The base below the deck's centre, the deck 0.776 above it.
                origin = first.Position + Vector3.up * (first.Top - (float)(2 * HalfLength * Math.Sin(15 * Degrees)));
                break;
            case Kind.Halfpipe:
                // The flat's centre: the four flat tiles are at right 0/3, forward 0/3 from the first.
                origin = first.Position + yawRot * new Vector3((float)HalfLength, first.Top, (float)HalfLength);
                break;
            default:
                // A ledge's base centre, its top 0.4 or 0.8 above.
                origin = first.Position + Vector3.up * (first.Top - (kind == Kind.LedgeHigh ? 0.8f : 0.4f));
                break;
        }
        origin += Vector3.up * Lift;
        p.Origin = origin; p.Yaw = anchorYaw;
        int colliders;
        var root = Build(kind, origin, yawRot, true, out colliders);
        if (root == null) return;
        Colliders += colliders;
        p.Root = root;
        if (kind == Kind.Bank || kind == Kind.Kicker || kind == Kind.QuarterPipe) Colliders += Entry(root, kind == Kind.QuarterPipe ? -1.2f : 0f, -1f);
        // A funbox is entered from both ends.
        if (kind == Kind.Funbox) { Colliders += Entry(root, -FunboxEnd, -1f); Colliders += Entry(root, FunboxEnd, 1f); }
        // The native tiles and walls under the model are not drawn; the foundation stays.
        foreach (var m in matched)
        {
            var entity = blocks[m].Entity;
            try
            {
                var renderers = entity.GetComponentsInChildren<Renderer>(true);
                for (var r = 0; r < renderers.Length; r++)
                {
                    var rend = renderers[r];
                    if (rend == null || !rend.enabled) continue;
                    rend.enabled = false; p.Hidden.Add(rend); HiddenRenderers++;
                }
            }
            catch (Exception e) { Status = "native renderers not hidden: " + e.Message; }
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
        if (kind == Kind.QuarterPipe || kind == Kind.Halfpipe) return Transition(kind, origin, yawRot, solid, out colliders);
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

    // The quarterpipe and the halfpipe follow the server's own blocks: a 15 degree tile and a 35
    // degree tile up from the flat (a halfpipe has that on both ends of its 6 m flat, with a 3 m
    // deck on top). The authored models are circular and shorter, so they would leave the tiles
    // standing out of them; these are made here instead, on the tiles' tops, with the bends
    // rounded (radius Bend) so the board rolls through them, and the coping edge left sharp.
    public const double Bend = 3.0, VertBend = 1.6, VertAngle = 82, VertLength = 0.9;
    private const int BendSteps = 8;

    // The riding profile, (forward, height) points in the piece's frame. The quarterpipe starts at
    // its entry (the first tile's low edge); the halfpipe's frame is the middle of its flat.
    public static List<Vector2> TransitionProfile(Kind kind)
    {
        double run1 = 2 * HalfLength * Math.Cos(15 * Degrees), rise1 = 2 * HalfLength * Math.Sin(15 * Degrees);
        double run2 = 2 * HalfLength * Math.Cos(35 * Degrees), rise2 = 2 * HalfLength * Math.Sin(35 * Degrees);
        var corners = new List<Vector2>(); var radii = new List<double>();
        if (kind == Kind.QuarterPipe)
        {
            corners.Add(new Vector2(0f, -1.2f)); radii.Add(0);
            corners.Add(new Vector2(0f, 0f)); radii.Add(Bend);
            corners.Add(new Vector2((float)run1, (float)rise1)); radii.Add(Bend);
            // Past the top tile the wall goes on up to near vertical, so the board leaves it upward
            // and comes back down into it, as from a real quarterpipe: seen live, a run up the
            // 35 degree top tile launched the board 12 m on over the deck.
            corners.Add(new Vector2((float)(run1 + run2), (float)(rise1 + rise2))); radii.Add(VertBend);
            corners.Add(new Vector2((float)(run1 + run2 + VertLength * Math.Cos(VertAngle * Degrees)), (float)(rise1 + rise2 + VertLength * Math.Sin(VertAngle * Degrees)))); radii.Add(0);
            // Points are (forward, height); the first is a run-in on the ground.
            corners[0] = new Vector2(-1.2f, 0f);
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

    private static GameObject Transition(Kind kind, Vector3 origin, Quaternion yawRot, bool solid, out int colliders)
    {
        colliders = 0;
        if (plain == null) plain = MaterialFor(new SkateModelMaterial());
        var material = plain;
        try { var loaded = Model(kind); if (loaded != null && loaded.Materials != null && loaded.Materials.Length > 0 && loaded.Materials[0] != null) material = loaded.Materials[0]; }
        catch (Exception) { }
        var profile = TransitionProfile(kind);
        var halfWidth = (float)(kind == Kind.QuarterPipe ? HalfLength : 2 * HalfLength);
        var root = new GameObject("skate_park_" + fileStems[(int)kind]);
        root.transform.position = origin; root.transform.rotation = yawRot;
        var surface = SurfaceMesh(profile, halfWidth);
        var visual = new GameObject("VISUAL__transition"); visual.transform.SetParent(root.transform, false);
        visual.AddComponent<MeshFilter>().sharedMesh = SolidMesh(profile, halfWidth);
        visual.AddComponent<MeshRenderer>().sharedMaterial = material;
        if (solid)
        {
            var proxy = new GameObject("COLLISION__transition"); proxy.transform.SetParent(root.transform, false);
            proxy.AddComponent<MeshCollider>().sharedMesh = surface; proxy.layer = 0; colliders++;
        }
        return root;
    }

    // The server rests a piece so that its lowest tile corner clears the ground, which leaves the
    // entry (the first tile's top, where the piece is drawn from) a tile's thickness above it: an
    // edge the board stops at (a funbox at both its ends). A short wedge from the ground up to the entry takes it away. Returns
    // the colliders added (0 when the entry is on the ground or the ground is not found).
    public const float EntrySlope = 8f, EntryMost = 0.6f, EntryLook = 0.4f;
    public static float LastEntryDrop = -1f;
    public const float FunboxEnd = 4.4f;
    private static int Entry(GameObject root, float entryForward, float outward)
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
        var surface = SurfaceMesh(profile, (float)HalfLength);
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
    private static Mesh SolidMesh(List<Vector2> profile, float halfWidth)
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
                verts.Add(new Vector3(side * halfWidth, 0f, profile[i].x)); uv.Add(new Vector2(profile[i].x / 2f, 0f));
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
            var p = profile[end]; if (p.y < 0.01f) continue;
            var start = verts.Count; var facing = end == 0 ? -1f : 1f;
            verts.Add(new Vector3(-halfWidth, p.y, p.x)); verts.Add(new Vector3(halfWidth, p.y, p.x));
            verts.Add(new Vector3(-halfWidth, 0f, p.x)); verts.Add(new Vector3(halfWidth, 0f, p.x));
            uv.Add(new Vector2(0f, p.y)); uv.Add(new Vector2(halfWidth, p.y)); uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(halfWidth, 0f));
            if (facing > 0) { tris.Add(start); tris.Add(start + 1); tris.Add(start + 2); tris.Add(start + 1); tris.Add(start + 3); tris.Add(start + 2); }
            else { tris.Add(start); tris.Add(start + 2); tris.Add(start + 1); tris.Add(start + 1); tris.Add(start + 2); tris.Add(start + 3); }
        }
        var cols = new Color[verts.Count]; for (var i = 0; i < cols.Length; i++) cols[i] = Color.white;
        var mesh = new Mesh(); mesh.name = "transition";
        mesh.vertices = verts.ToArray(); mesh.uv = uv.ToArray(); mesh.colors = cols; mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    private static void Remove(Placed p)
    {
        foreach (var r in p.Hidden) { try { if (r != null) r.enabled = true; } catch (Exception) { } HiddenRenderers--; }
        p.Hidden.Clear();
        if (p.Root != null) { try { UnityEngine.Object.Destroy(p.Root); } catch (Exception) { } p.Root = null; }
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
            for (var i = 0; i < model.Meshes.Length; i++) loaded.Meshes[i] = Convert(model.Meshes[i], model);
            loaded.Materials = new Material[model.Materials.Length];
            for (var i = 0; i < model.Materials.Length; i++) loaded.Materials[i] = MaterialFor(model.Materials[i]);
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
