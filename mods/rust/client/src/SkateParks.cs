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
                if ((kind == Kind.LedgeLow || kind == Kind.LedgeHigh || kind == Kind.LongLedge) && !OverFoundation(b, blocks, cells, kind == Kind.LedgeHigh)) continue;
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
                if (Math.Abs(Delta(wantYaw, b.Yaw)) > AngleTolerance || Math.Abs(Delta((float)part.Pitch, b.Pitch)) > AngleTolerance) continue;
                best = i; bestDistance = d;
            }
            if (best < 0) return false;
            matched.Add(best);
        }
        return true;
    }

    // A ledge is a floor tile 0.4 or 0.8 above a foundation and nothing else: an ordinary floor never
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
            return high ? gap >= 0.62f : gap < 0.62f;
        }
        return false;
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
        var root = Build(kind, origin, yawRot, out colliders);
        if (root == null) return;
        Colliders += colliders;
        p.Root = root;
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

    // A preview without any server piece: the model of a kind at a place, for a look and a ride.
    private static GameObject preview;
    public static string PreviewAt(Kind kind, Vector3 origin, float yaw)
    {
        PreviewClear();
        int colliders;
        preview = Build(kind, origin + Vector3.up * Lift, Quaternion.Euler(0f, yaw, 0f), out colliders);
        if (preview == null) return "not drawn: " + Status;
        Colliders += colliders;
        return "drawn at " + Out.V1(origin) + " yaw " + yaw.ToString("F0") + " with " + colliders + " colliders";
    }
    public static void PreviewClear()
    {
        if (preview == null) return;
        try { UnityEngine.Object.Destroy(preview); } catch (Exception) { }
        preview = null;
    }

    // The model's objects under a root at a place: the Visual scene drawn, the CollisionProxies
    // scene as static colliders on the default layer, which the ride's ground mask includes.
    private static GameObject Build(Kind kind, Vector3 origin, Quaternion yawRot, out int colliders)
    {
        colliders = 0;
        var loaded = Model(kind);
        if (loaded == null || loaded.Model == null) return null;
        var root = new GameObject("skate_park_" + fileStems[(int)kind]);
        root.transform.position = origin; root.transform.rotation = yawRot;
        var model = loaded.Model;
        var visual = model.SceneIndex("Visual"); if (visual < 0) visual = model.DefaultScene;
        var collision = model.SceneIndex("CollisionProxies");
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
