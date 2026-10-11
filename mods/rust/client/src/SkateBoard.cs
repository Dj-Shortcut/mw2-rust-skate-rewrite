// Rust/Standard is the game's own lit shader: a plugin-made mesh drawn with it takes the world's
// light, casts a shadow and goes dark at night. It reads colours from a texture, not from the
// vertices. Hidden/Internal-Colored draws vertex colours without light and is the fallback.
// The mesh setters can be missing from a client build; primitives are the fallback for those.
using System;
using Il2CppInterop.Runtime;
using UnityEngine;

public static class SkateBoard
{
    public const float TipDegrees = 15f, Axle = 0.23f;
    public const string LitShader = "Rust/Standard", PlainShader = "Hidden/Internal-Colored";
    public const float Gloss = 0.2f, BailTurn = 0.5f, BailBehind = 0.9f, BailRest = 0.045f, PopMost = 0.22f, LieSeconds = 6f;
    // The wheels of the shared mesh, and how far a truck turns per degree the board leans.
    public const float WheelRadius = 0.027f, TruckTurn = 0.8f;
    public static string Drawn = "";
    public static float ShowLift, Tip, Pop, WheelSpin;
    // Held: the board is where the rider's feet hold it (a handplant), at HeldAt with its grip toward HeldUp.
    public static bool Held;
    public static Vector3 HeldAt, HeldNose = Vector3.forward, HeldUp = Vector3.up;
    private static Transform[] trucks = new Transform[0], wheels = new Transform[0];
    private static Vector3[] truckAxes = new Vector3[0], wheelAxes = new Vector3[0];
    private static float[] truckSides = new float[0];
    private static float followedAt = -1f;
    private static Vector3 bailFrom, bailTravel, bailNose, bailUp;
    private static float bailStamp = -1f, bailRoll, lieUntil = -1f;
    private static bool lying;
    private static void Say(string m) { Out.Say("SKATE " + m); }

    public static GameObject Build()
    {
        trucks = new Transform[0]; wheels = new Transform[0];
        try { var made = FromParts(); Say("BOARD from the shared mesh in parts: " + trucks.Length + " trucks, " + wheels.Length + " wheels"); return made; }
        catch (Exception e) { Say("board in parts unavailable (" + e.GetType().Name + ": " + e.Message + "); using the one-piece mesh"); trucks = new Transform[0]; wheels = new Transform[0]; }
        try { var made = FromMesh(); Say("BOARD from the shared mesh"); return made; }
        catch (Exception e) { Say("shared mesh unavailable (" + e.GetType().Name + ": " + e.Message + "); using primitives"); return FromPrimitives(); }
    }

    public static Vector3 Contact(Transform playerT) { return playerT.position + Vector3.up * (ShowLift + Pop); }

    // A manual lifts the end that leads: the board, and the rider's footing with it, pitches about
    // the ground under the trailing axle. A nose manual (Tip below zero) lifts the end that trails,
    // about the leading axle.
    // A grab brings the board up to the rider and tweaks it: each grab tilts it its own way, the
    // edge or end in the hand coming up (toe edge up for an indy, heel edge for a melon, the nose for
    // a nose grab), more as the grab is held. Degrees: toe edge up, nose up.
    public const float GrabRise = 0.3f;
    // A seatbelt takes the tail's toe corner (toe edge up, nose down), a crail the nose's (nose up);
    // a nosebone pushes the nose away with the front leg (nose down), a tailbone the tail (nose up).
    private static readonly float[] grabRoll = { 0f, -15f, -20f, 25f, 0f, 0f, 0f, -20f, 15f, 15f, -30f, -40f, 0f, 0f };
    private static readonly float[] grabPitch = { 0f, 0f, 0f, 0f, 0f, -15f, 15f, -8f, 12f, -12f, -6f, 8f, -18f, 18f };
    public static float GrabFold;
    // A no comply or a boneless: the front foot is down beside the board and the back foot kicks
    // the tail, so the nose comes up about the back foot and the board rises with it. Plant (0..1)
    // is set by the rider's pose each frame.
    public const float PlantPitch = 38f, PlantLift = 0.12f, PlantPivot = 0.2f;
    public static float Plant;
    // The board's own tweak in a grab, about the middle of the deck; the rider's frame stays upright
    // (the feet are near the middle, where the tweak moves the deck least).
    public static Quaternion GrabTweak(Vector3 up)
    {
        var g = (int)SkateRide.Grab;
        if (SkateRide.Mode != RideMode.Air || g <= 0 || g >= grabRoll.Length || GrabFold <= 0.001f) return Quaternion.identity;
        var nose = SkateRide.Dir(SkateRide.NoseYaw); nose = nose - up * Vector3.Dot(nose, up);
        if (nose.sqrMagnitude < 0.0001f) return Quaternion.identity;
        nose = nose.normalized;
        var toe = Vector3.Cross(up, nose);
        return SkateRide.Turn(-grabRoll[g] * GrabFold, nose) * SkateRide.Turn(-grabPitch[g] * GrabFold, toe);
    }

    // A grind or a stall holds the deck its own way (SkateRider.GrindDeck): eased toward it while
    // grinding, back to flat after. Degrees, the leading end up and the toe edge up.
    public static float GrindPitch, GrindRoll;

    // The deck's pitch, degrees, the leading end up: a manual's and a grind's together.
    public static float PitchDegrees { get { return Tip * TipDegrees + GrindPitch; } }

    public static void Tilt(ref Vector3 at, ref Vector3 up)
    {
        var pitch = PitchDegrees;
        if (Math.Abs(pitch) > 0.01f)
        {
            var lead = SkateRide.Dir(SkateRide.Yaw);
            lead = lead - up * Vector3.Dot(lead, up);
            if (lead.sqrMagnitude > 0.0001f)
            {
                lead = SkateRide.Speed >= 0f ? lead.normalized : -lead.normalized;
                var turn = SkateRide.Turn(pitch, Vector3.Cross(lead, up));
                // A manual and a stall on one end pitch about the axle that stays down; a grind on a
                // truck about that truck, which is where the edge is.
                var pivot = at - lead * (pitch > 0f ? Axle : -Axle);
                at = pivot + turn * (at - pivot);
                up = turn * up;
            }
        }
        if (Math.Abs(GrindRoll) > 0.01f)
        {
            var nose = SkateRide.Dir(SkateRide.NoseYaw); nose = nose - up * Vector3.Dot(nose, up);
            if (nose.sqrMagnitude > 0.0001f) up = SkateRide.Turn(-GrindRoll, nose.normalized) * up;
        }
    }

    public static void Follow(GameObject board, Transform playerT)
    {
        var now = Time.realtimeSinceStartup;
        var riding = SkateRide.On && playerT != null;
        // A bail ends on foot: the board stays where it came to rest for a while, or until the
        // rider gets on again.
        if (riding) lying = SkateRide.Mode == RideMode.Bail;
        else if (lying) { lying = false; lieUntil = now + LieSeconds; }
        var show = riding || now < lieUntil;
        if (board.activeSelf != show) board.SetActive(show);
        if (!riding) return;
        lieUntil = -1f;
        var n = SkateRide.ViewNormal;
        var at = Contact(playerT);
        Tilt(ref at, ref n);
        var travel = SkateRide.Dir(SkateRide.Yaw);
        travel = travel - n * Vector3.Dot(travel, n);
        travel = travel.sqrMagnitude > 0.0001f ? travel.normalized : SkateRide.Dir(SkateRide.Yaw);
        // The lean rolls the board about its direction of travel, right side down in a right turn.
        var up = SkateRide.Turn(-SkateRide.Lean, travel) * n;
        // The board's own nose: the rider's stance, and what the board has turned under his feet.
        var nose = SkateRide.Dir(SkateRide.DeckYaw);
        nose = nose - up * Vector3.Dot(nose, up);
        nose = nose.sqrMagnitude > 0.0001f ? nose.normalized : travel;
        var flip = (float)SkateRide.FlipDeg; var askew = 0f;
        if (SkateRide.Mode == RideMode.Bail)
        {
            // The rider goes on and down. The board is on its own from the moment of the bail: it
            // turns over once and comes to rest behind where the rider will stop, on its deck.
            if (bailStamp != SkateRide.ModeAt)
            {
                bailStamp = SkateRide.ModeAt; bailFrom = at; bailTravel = travel; bailNose = nose; bailUp = up;
                var speed = Math.Abs(SkateRide.Speed);
                bailRoll = (SkateRide.Speed < 0f ? -1f : 1f) * (speed * speed / (2f * SkateRide.BailDecel)) - BailBehind;
            }
            var t = now - SkateRide.ModeAt;
            var over = SkateRide.Clamp(t / BailTurn, 0f, 1f); over = over * over * (3f - 2f * over);
            flip += 180f * over; askew = 35f * over;
            travel = bailTravel; nose = bailNose; up = bailUp;
            at = bailFrom + travel * (bailRoll * over) + Vector3.up * (0.22f * (float)Math.Sin(over * Math.PI)) - up * ((RiderRig.DeckTop - BailRest) * over);
        }
        if (Held && SkateRide.Handplant)
        {
            var heldUp = HeldUp.sqrMagnitude > 0.5f ? HeldUp : Vector3.up;
            var heldNose = HeldNose - heldUp * Vector3.Dot(HeldNose, heldUp);
            if (heldNose.sqrMagnitude < 0.0001f) heldNose = Vector3.Cross(heldUp, Vector3.right);
            board.transform.rotation = Quaternion.LookRotation(heldNose.normalized, heldUp);
            board.transform.position = HeldAt;
            Parts(now, travel, heldNose.normalized);
            return;
        }
        var rot = SkateRide.Turn(askew, up) * Quaternion.LookRotation(nose, up) * SkateRide.Turn(flip, Vector3.forward);
        rot = GrabTweak(up) * rot;
        // A flip turns the board about the middle of the deck, not about the wheels.
        var centre = at + up * RiderRig.DeckTop - (rot * Vector3.up) * RiderRig.DeckTop;
        if (Plant > 0.001f && SkateRide.Mode != RideMode.Bail)
        {
            var lead = Vector3.Dot(travel, nose) >= 0f ? nose : -nose;
            var kick = SkateRide.Turn(-PlantPitch * Plant, Vector3.Cross(up, lead));
            var pivot = centre - lead * PlantPivot;
            centre = pivot + kick * (centre - pivot) + up * (PlantLift * Plant);
            rot = kick * rot;
        }
        board.transform.rotation = rot;
        board.transform.position = centre;
        Parts(now, travel, nose);
    }

    // The trucks steer with the lean, the front one into the turn and the rear one out of it, each
    // about its own kingpin; the wheels roll with the speed, the way the deck's nose is pointing.
    private static void Parts(float now, Vector3 travel, Vector3 nose)
    {
        var dt = followedAt < 0f ? 0f : SkateRide.Clamp(now - followedAt, 0f, 0.1f);
        followedAt = now;
        if (SkateRide.Mode != RideMode.Bail)
        {
            var ahead = Vector3.Dot(travel, nose) < 0f ? -1f : 1f;
            WheelSpin = (WheelSpin + ahead * SkateRide.Speed / WheelRadius * 57.29578f * dt) % 360f;
        }
        var turn = SkateRide.Mode == RideMode.Ground ? SkateRide.Lean * TruckTurn : 0f;
        for (var i = 0; i < trucks.Length; i++) trucks[i].localRotation = Quaternion.AngleAxis(truckSides[i] * turn, truckAxes[i]);
        for (var i = 0; i < wheels.Length; i++) wheels[i].localRotation = Quaternion.AngleAxis(WheelSpin, wheelAxes[i]);
    }

    // The detailed shared mesh: a deck, two trucks that hang from it and four wheels that hang
    // from the trucks, each its own object about its own pivot, all drawn with one texture.
    private static GameObject FromParts()
    {
        var data = Shortcut.RustMod.SkateBoardMesh.CreateDetailed();
        if (data.Parts.Length == 0 || data.TextureWidth == 0) throw new InvalidOperationException("the shared mesh has no parts");
        var tex = new Texture2D(data.TextureWidth, data.TextureHeight);
        for (var y = 0; y < data.TextureHeight; y++)
            for (var x = 0; x < data.TextureWidth; x++)
            {
                var o = (y * data.TextureWidth + x) * 4;
                tex.SetPixel(x, y, new Color(data.TextureRgba[o] / 255f, data.TextureRgba[o + 1] / 255f, data.TextureRgba[o + 2] / 255f, data.TextureRgba[o + 3] / 255f));
            }
        tex.filterMode = FilterMode.Bilinear;
        tex.Apply();
        string how; var lit = Find(LitShader, out how); Material mat;
        if (lit != null)
        {
            mat = new Material(lit); mat.mainTexture = tex; mat.color = Color.white;
            mat.SetFloat("_Glossiness", Gloss); mat.SetFloat("_Metallic", 0f);
            Drawn = LitShader; Say("BOARD lit by " + LitShader + " (found " + how + "), textured");
        }
        else
        {
            var sh = Shader.Find(PlainShader);
            if (sh == null) throw new InvalidOperationException("no vertex-colour shader");
            mat = new Material(sh); mat.color = Color.white; Drawn = PlainShader;
            Say("no " + LitShader + " shader in this client; the board is drawn without light");
        }
        var root = new GameObject("skate_board");
        var objects = new Transform[data.Parts.Length];
        var pivots = new Vector3[data.Parts.Length];
        var truckList = new System.Collections.Generic.List<Transform>(); var truckAxis = new System.Collections.Generic.List<Vector3>(); var truckSide = new System.Collections.Generic.List<float>();
        var wheelList = new System.Collections.Generic.List<Transform>(); var wheelAxis = new System.Collections.Generic.List<Vector3>();
        for (var p = 0; p < data.Parts.Length; p++)
        {
            var part = data.Parts[p];
            var pivot = new Vector3(part.PivotX, part.PivotY, part.PivotZ);
            var axis = new Vector3(part.AxisX, part.AxisY, part.AxisZ);
            var verts = new Vector3[part.VertexCount]; var norms = new Vector3[part.VertexCount]; var cols = new Color[part.VertexCount]; var uv = new Vector2[part.VertexCount];
            for (var i = 0; i < part.VertexCount; i++)
            {
                var v = part.VertexStart + i;
                verts[i] = new Vector3(data.Positions[v * 3], data.Positions[v * 3 + 1], data.Positions[v * 3 + 2]) - pivot;
                norms[i] = new Vector3(data.Normals[v * 3], data.Normals[v * 3 + 1], data.Normals[v * 3 + 2]);
                cols[i] = new Color(data.Colours[v * 4], data.Colours[v * 4 + 1], data.Colours[v * 4 + 2], data.Colours[v * 4 + 3]);
                uv[i] = new Vector2(data.UVs[v * 2], data.UVs[v * 2 + 1]);
            }
            var tris = new int[part.IndexCount];
            for (var i = 0; i < part.IndexCount; i++) tris[i] = data.Triangles[part.IndexStart + i] - part.VertexStart;
            var mesh = new Mesh();
            mesh.vertices = verts; mesh.normals = norms; mesh.colors = cols; mesh.uv = uv; mesh.triangles = tris;
            mesh.RecalculateBounds();
            var go = new GameObject(part.Kind == Shortcut.RustMod.SkateBoardPartKind.Deck ? "deck" : part.Kind == Shortcut.RustMod.SkateBoardPartKind.Truck ? "truck" : "wheel");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().material = mat;
            var parent = part.ParentIndex >= 0 && part.ParentIndex < p ? objects[part.ParentIndex] : root.transform;
            var parentPivot = part.ParentIndex >= 0 && part.ParentIndex < p ? pivots[part.ParentIndex] : Vector3.zero;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pivot - parentPivot; go.transform.localRotation = Quaternion.identity;
            objects[p] = go.transform; pivots[p] = pivot;
            if (part.Kind == Shortcut.RustMod.SkateBoardPartKind.Truck) { truckList.Add(go.transform); truckAxis.Add(axis); truckSide.Add(part.PivotZ < 0f ? -1f : 1f); }
            if (part.Kind == Shortcut.RustMod.SkateBoardPartKind.Wheel) { wheelList.Add(go.transform); wheelAxis.Add(axis); }
        }
        trucks = truckList.ToArray(); truckAxes = truckAxis.ToArray(); truckSides = truckSide.ToArray();
        wheels = wheelList.ToArray(); wheelAxes = wheelAxis.ToArray();
        UnityEngine.Object.DontDestroyOnLoad(root);
        return root;
    }

    private static GameObject FromMesh()
    {
        var data = Shortcut.RustMod.SkateBoardMesh.Create();
        var n = data.Positions.Length / 3;
        var verts = new Vector3[n]; var norms = new Vector3[n]; var cols = new Color[n];
        for (var i = 0; i < n; i++)
        {
            verts[i] = new Vector3(data.Positions[i * 3], data.Positions[i * 3 + 1], data.Positions[i * 3 + 2]);
            norms[i] = new Vector3(data.Normals[i * 3], data.Normals[i * 3 + 1], data.Normals[i * 3 + 2]);
            cols[i] = new Color(data.Colours[i * 4], data.Colours[i * 4 + 1], data.Colours[i * 4 + 2], data.Colours[i * 4 + 3]);
        }
        var mesh = new Mesh();
        mesh.vertices = verts; mesh.normals = norms; mesh.colors = cols; mesh.triangles = data.Triangles;
        mesh.RecalculateBounds();
        var mat = Lit(mesh, cols);
        if (mat == null)
        {
            var sh = Shader.Find(PlainShader);
            if (sh == null) throw new InvalidOperationException("no vertex-colour shader");
            mat = new Material(sh); mat.color = Color.white; Drawn = PlainShader;
        }
        var root = new GameObject("skate_board");
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        root.AddComponent<MeshRenderer>().material = mat;
        UnityEngine.Object.DontDestroyOnLoad(root);
        return root;
    }

    private static Shader Find(string name, out string how)
    {
        how = "by name";
        var sh = Shader.Find(name);
        if (sh != null) return sh;
        // A shader that came with an asset bundle is loaded without being findable by name.
        how = "among the loaded shaders";
        var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Shader>());
        for (var i = 0; i < all.Length; i++)
        {
            var one = all[i] == null ? null : all[i].TryCast<Shader>();
            if (one != null && one.name == name) return one;
        }
        return null;
    }

    // One block of texels per vertex colour; every vertex looks its own colour up.
    private static Material Lit(Mesh mesh, Color[] cols)
    {
        try
        {
            string how;
            var sh = Find(LitShader, out how);
            if (sh == null) { Say("no " + LitShader + " shader in this client; the board is drawn without light"); return null; }
            var unique = new System.Collections.Generic.List<Color>(); var index = new int[cols.Length];
            for (var i = 0; i < cols.Length; i++)
            {
                var c = cols[i]; var found = -1;
                for (var u = 0; u < unique.Count && found < 0; u++)
                    if (Math.Abs(unique[u].r - c.r) + Math.Abs(unique[u].g - c.g) + Math.Abs(unique[u].b - c.b) < 0.002f) found = u;
                if (found < 0) { found = unique.Count; unique.Add(c); }
                index[i] = found;
            }
            const int cell = 4;
            var across = 1; while (across * across < unique.Count) across++;
            var tex = new Texture2D(cell * across, cell * across);
            for (var u = 0; u < across * across; u++)
            {
                var c = u < unique.Count ? unique[u] : Color.white;
                for (var y = 0; y < cell; y++) for (var x = 0; x < cell; x++) tex.SetPixel((u % across) * cell + x, (u / across) * cell + y, c);
            }
            tex.filterMode = FilterMode.Point;
            tex.Apply();
            var uv = new Vector2[cols.Length];
            for (var i = 0; i < uv.Length; i++) uv[i] = new Vector2(((index[i] % across) + 0.5f) / across, ((index[i] / across) + 0.5f) / across);
            mesh.uv = uv;
            var m = new Material(sh);
            m.mainTexture = tex; m.color = Color.white;
            m.SetFloat("_Glossiness", Gloss); m.SetFloat("_Metallic", 0f);
            Drawn = LitShader;
            Say("BOARD lit by " + LitShader + " (found " + how + "), " + unique.Count + " colours");
            return m;
        }
        catch (Exception e) { Say("lit board unavailable (" + e.GetType().Name + ": " + e.Message + "); it is drawn without light"); return null; }
    }

    private static GameObject FromPrimitives()
    {
        var root = new GameObject("skate_board");
        var sh = Shader.Find(PlainShader); Drawn = PlainShader + ", primitives";
        var grip = new Color(0.07f, 0.07f, 0.08f); var kick = new Color(0.16f, 0.16f, 0.18f); var under = new Color(1f, 0.1f, 0.6f);
        var metal = new Color(0.55f, 0.57f, 0.6f); var wheel = new Color(0.95f, 0.93f, 0.85f);
        Part(root, PrimitiveType.Cube, new Vector3(0f, 0.078f, 0f), new Vector3(0.20f, 0.010f, 0.60f), Quaternion.identity, grip, sh);
        Part(root, PrimitiveType.Cube, new Vector3(0f, 0.069f, 0f), new Vector3(0.20f, 0.008f, 0.60f), Quaternion.identity, under, sh);
        Part(root, PrimitiveType.Cube, new Vector3(0f, 0.084f, 0f), new Vector3(0.025f, 0.003f, 0.56f), Quaternion.identity, under, sh);
        Part(root, PrimitiveType.Cube, new Vector3(0f, 0.097f, 0.365f), new Vector3(0.20f, 0.012f, 0.15f), Quaternion.Euler(-17f, 0f, 0f), kick, sh);
        Part(root, PrimitiveType.Cube, new Vector3(0f, 0.097f, -0.365f), new Vector3(0.20f, 0.012f, 0.15f), Quaternion.Euler(17f, 0f, 0f), kick, sh);
        for (var z = -1; z <= 1; z += 2)
        {
            Part(root, PrimitiveType.Cube, new Vector3(0f, 0.046f, z * 0.23f), new Vector3(0.15f, 0.034f, 0.05f), Quaternion.identity, metal, sh);
            for (var x = -1; x <= 1; x += 2)
                Part(root, PrimitiveType.Cylinder, new Vector3(x * 0.097f, 0.0275f, z * 0.23f), new Vector3(0.055f, 0.017f, 0.055f), Quaternion.Euler(0f, 0f, 90f), wheel, sh);
        }
        UnityEngine.Object.DontDestroyOnLoad(root);
        return root;
    }

    private static void Part(GameObject root, PrimitiveType type, Vector3 pos, Vector3 scale, Quaternion rot, Color color, Shader sh)
    {
        var go = GameObject.CreatePrimitive(type);
        var col = go.GetComponent<Collider>();
        if (col != null) UnityEngine.Object.Destroy(col);
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = pos; go.transform.localRotation = rot; go.transform.localScale = scale;
        if (sh != null) { var m = new Material(sh); m.color = color; go.GetComponent<Renderer>().material = m; }
    }
}
