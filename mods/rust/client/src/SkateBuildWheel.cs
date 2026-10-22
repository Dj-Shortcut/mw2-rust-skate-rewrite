using System;
using System.Text;
using UnityEngine;
using Shortcut.RustMod;

public static class SkateWheelChoice
{
    public const int Count = 12;
    public static int Sector(double x, double y, double deadzone)
    {
        if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y) ||
            double.IsNaN(deadzone) || double.IsInfinity(deadzone) || deadzone < 0 || x * x + y * y <= deadzone * deadzone) return -1;
        double angle = Math.Atan2(x, y) * 180 / Math.PI;
        return (int)Math.Floor(((angle + 375) % 360) / 30) % Count;
    }
}

public static class SkateWheelArtwork
{
    private const uint Cream = 0xf2eee3f5, Red = 0xc43f32ff, White = 0xfffbf3ff;

    public static uint[] Pixels(int size, int selected)
    {
        if (size < 64 || size > 1024 || selected < -1 || selected >= SkateWheelChoice.Count) throw new ArgumentOutOfRangeException();
        var pixels = new uint[size * size];
        double middle = (size - 1) * 0.5, outer = size * 0.48, inner = size * 0.285;
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            double dx = x - middle, dy = y - middle, length = Math.Sqrt(dx * dx + dy * dy);
            if (length < inner || length > outer) continue;
            int sector = SkateWheelChoice.Sector(dx, dy, 0);
            pixels[y * size + x] = sector == selected ? Red : Cream;
        }
        for (int kind = 0; kind < SkateWheelChoice.Count; kind++)
        {
            double angle = kind * Math.PI / 6;
            double cx = middle + Math.Sin(angle) * size * 0.382;
            double cy = middle + Math.Cos(angle) * size * 0.382;
            Icon(pixels, size, kind, cx, cy, size * 0.103, kind == selected ? White : Red);
        }
        return pixels;
    }

    private static void Icon(uint[] p, int size, int kind, double cx, double cy, double width, uint ink)
    {
        double[] profile;
        switch (kind)
        {
            case 0: profile = new[] { -0.5, -0.25, 0.5, 0.25, 0.5, -0.25, -0.5, -0.25 }; break;
            case 1: profile = new[] { -0.5, -0.3, 0.4, 0.35, 0.5, 0.35, 0.5, -0.3, -0.5, -0.3 }; break;
            case 2: case 8: case 9: case 10:
                profile = Quarter(kind == 8 ? 0.6 : kind == 9 ? 1.1 : kind == 10 ? 0.75 : 0.9); break;
            case 3: case 4: case 5:
                double h = kind == 4 ? 0.3 : 0.03;
                profile = new[] { -0.5, -0.25, -0.5, h, 0.5, h, 0.5, -0.25, -0.5, -0.25 }; break;
            case 6: profile = new[] { -0.55, -0.3, -0.25, 0.2, 0.25, 0.2, 0.55, -0.3, -0.55, -0.3 }; break;
            default: profile = Half(kind == 11 ? 0.55 : 0.4); break;
        }
        double stretch = kind == 5 ? 1.12 : kind == 8 ? 0.8 : 1;
        double rise = kind == 9 ? 1.05 : 1;
        double ox = width * 0.18, oy = width * 0.15;
        double thick = Math.Max(1.1, size / 240.0);
        for (int i = 2; i < profile.Length; i += 2)
        {
            double x0 = cx + profile[i - 2] * width * stretch - ox / 2;
            double y0 = cy + profile[i - 1] * width * rise - oy / 2;
            double x1 = cx + profile[i] * width * stretch - ox / 2;
            double y1 = cy + profile[i + 1] * width * rise - oy / 2;
            Stroke(p, size, x0, y0, x1, y1, thick, ink);
            Stroke(p, size, x0 + ox, y0 + oy, x1 + ox, y1 + oy, thick, ink);
        }
        for (int i = 0; i < profile.Length; i += Math.Max(2, (profile.Length / 8) * 2))
        {
            double x = cx + profile[i] * width * stretch - ox / 2, y = cy + profile[i + 1] * width * rise - oy / 2;
            Stroke(p, size, x, y, x + ox, y + oy, thick, ink);
        }
        if (kind == 5) Stroke(p, size, cx - width * 0.3, cy + width * 0.17, cx + width * 0.3, cy + width * 0.17, thick, ink);
        if (kind == 10) Stroke(p, size, cx + width * 0.43, cy - width * 0.25, cx + width * 0.43, cy + width * 0.5, thick, ink);
        if (kind == 11)
        {
            Stroke(p, size, cx - width * 0.22, cy - width * 0.49, cx + width * 0.22, cy - width * 0.49, thick, ink);
            Stroke(p, size, cx, cy - width * 0.55, cx, cy - width * 0.42, thick, ink);
        }
    }

    private static double[] Quarter(double scale)
    {
        var points = new double[26];
        for (int i = 0; i <= 10; i++)
        {
            double a = i * Math.PI / 20;
            points[i * 2] = -0.5 + Math.Sin(a) * 0.85;
            points[i * 2 + 1] = -0.3 + (1 - Math.Cos(a)) * scale * 0.7;
        }
        points[22] = 0.5; points[23] = points[21]; points[24] = 0.5; points[25] = -0.3;
        return points;
    }

    private static double[] Half(double height)
    {
        var points = new double[38];
        for (int i = 0; i <= 18; i++)
        {
            double x = (i - 9) / 18.0, t = Math.Max(0, (Math.Abs(x) - 0.13) / 0.37);
            points[i * 2] = x; points[i * 2 + 1] = -0.25 + (1 - Math.Sqrt(Math.Max(0, 1 - t * t))) * height;
        }
        return points;
    }

    private static void Stroke(uint[] p, int size, double ax, double ay, double bx, double by, double width, uint ink)
    {
        int minX = Math.Max(0, (int)Math.Floor(Math.Min(ax, bx) - width)), maxX = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(ax, bx) + width));
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(ay, by) - width)), maxY = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(ay, by) + width));
        double dx = bx - ax, dy = by - ay, denominator = dx * dx + dy * dy;
        for (int y = minY; y <= maxY; y++) for (int x = minX; x <= maxX; x++)
        {
            double t = denominator <= 0 ? 0 : Math.Max(0, Math.Min(1, ((x - ax) * dx + (y - ay) * dy) / denominator));
            double rx = x - ax - t * dx, ry = y - ay - t * dy;
            if (rx * rx + ry * ry <= width * width) p[y * size + x] = ink;
        }
    }
}

public static class SkateBuildWheel
{
    public static readonly string[] Labels = { "Bank", "Kicker", "Quarterpipe", "Low Ledge", "High Ledge", "Long Ledge", "Funbox", "Halfpipe", "Mini Quarterpipe", "Large Quarterpipe", "Half-wall Kicker", "MEGA Halfpipe" };
    private static readonly string[] Descriptions = {
        "A mellow slope for lines and transfers.", "A steep take-off for quick airtime.", "A curved transition with a lip.",
        "A low ledge for manuals and grinds.", "A raised ledge for taller tricks.", "A long ledge for extended grinds.",
        "Banks and a flat deck for flowing lines.", "Two transitions for pumping and fakie returns.", "A compact transition for a small park.",
        "A wider, taller quarterpipe.", "A curved kicker attached to a half wall.", "The full-size halfpipe with tall transitions." };
    private static readonly Texture2D[] wheel = new Texture2D[13];
    private static GUIStyle title, body, footer;
    private static readonly SkateWheelText titleText = new SkateWheelText(), bodyText = new SkateWheelText(), footerText = new SkateWheelText();
    private static Texture2D dot;
    private static bool failed;
    public static bool Available { get { return !failed; } }
    public static float PointerRadius { get { return Math.Min(Screen.height * 0.37f, Screen.width * 0.3f); } }

    public static Vector2 PointerFor(int sector)
    {
        double angle = sector * Math.PI / 6;
        return new Vector2((float)Math.Sin(angle), -(float)Math.Cos(angle)) * PointerRadius * 0.8f;
    }

    public static void Draw()
    {
        if (failed || (!SkatePieces.Open && !SkatePieces.RustModeHint)) return;
        if (SkatePieces.Open && !SkatePieces.CanDrawWheel) return;
        var old = GUI.color;
        try
        {
            float diameter = PointerRadius * 2, cx = Screen.width * 0.5f, cy = Screen.height * 0.48f;
            EnsureStyles(diameter);
            if (SkatePieces.RustModeHint)
            {
                var rustControls = footerText.Get("RUST BUILDING  |  X / Shift + P: Skate building\nRelease, then hold Back / right click for the Rust wheel.", footer, diameter);
                float y = Screen.height - Math.Max(110, footerText.Height + 56);
                ModeButton(new Rect(cx - 110, y, 220, 34), "Skate building");
                GUI.Label(new Rect(cx - diameter / 2, y + 38, diameter, Math.Max(52, footerText.Height)), rustControls, footer);
                return;
            }
            int selected = SkatePieces.Hover;
            var texture = Texture(selected);
            GUI.DrawTexture(new Rect(cx - diameter / 2, cy - diameter / 2, diameter, diameter), texture);
            EnsureStyles(diameter);
            float hole = diameter * 0.52f;
            GUI.color = new Color(0.98f, 0.97f, 0.94f);
            var heading = titleText.Get(selected < 0 ? "SKATE PARK" : Labels[selected].ToUpperInvariant(), title, hole);
            var description = bodyText.Get(SkatePieces.Status.Length > 0 ? SkatePieces.Status :
                selected < 0 ? "Move the mouse or right stick to choose a piece." : Descriptions[selected], body, hole - 20);
            var bodyY = Math.Max(cy - 7, cy - 63 + titleText.Height + 4);
            GUI.Label(new Rect(cx - hole / 2, cy - 63, hole, Math.Max(58, titleText.Height)), heading, title);
            GUI.Label(new Rect(cx - hole / 2 + 10, bodyY, hole - 20, Math.Max(74, bodyText.Height)), description, body);
            var priceY = Math.Max(cy + 79, bodyY + bodyText.Height + 8);
            GUI.Label(new Rect(cx - hole / 2, priceY, hole, 28), "10 WOOD", footer);
            var controls = footerText.Get("SKATE BUILDING  |  X / Shift + P: Rust building\nArrows / D-pad: select  |  Release right click / Back, A / Enter: choose  |  B / Esc: close", footer, diameter);
            float footerY = Math.Min(Screen.height - 110, Math.Max(cy + diameter / 2 + 14, priceY + 42));
            ModeButton(new Rect(cx - 110, footerY, 220, 34), "Rust building");
            GUI.Label(new Rect(cx - diameter / 2, footerY + 38, diameter, Math.Max(66, footerText.Height)), controls, footer);
            if (dot == null)
            {
                dot = Out.Keep(new Texture2D(2, 2));
                for (int i = 0; i < 4; i++) dot.SetPixel(i % 2, i / 2, Color.white);
                dot.Apply();
            }
            Vector2 pointer = SkatePieces.Pointer;
            GUI.DrawTexture(new Rect(cx + pointer.x - 3, cy + pointer.y - 3, 6, 6), dot);
        }
        catch (Exception e)
        {
            failed = true;
            Out.Say("PIECES wheel rendering unavailable: " + e.GetType().Name);
            SkatePieces.CancelFocus();
        }
        finally { GUI.color = old; }
    }

    private static void ModeButton(Rect area, string label)
    { SkatePieces.ModeButton(area, GUI.Button(area, label)); }

    private static Texture2D Texture(int selected)
    {
        int index = selected + 1;
        if (wheel[index] != null) return wheel[index];
        const int size = 512;
        var rgba = SkateWheelArtwork.Pixels(size, selected);
        var pixels = new Color32[rgba.Length];
        for (int i = 0; i < pixels.Length; i++)
        { uint c = rgba[i]; pixels[i] = new Color32((byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c); }
        var t = Out.Keep(new Texture2D(size, size, TextureFormat.RGBA32, false));
        t.SetPixels32(pixels); t.Apply();
        wheel[index] = t;
        return t;
    }

    private static void EnsureStyles(float diameter)
    {
        int scale = (int)Math.Max(16, Math.Min(22, diameter / 28));
        if (title != null && title.fontSize == scale + 9) return;
        // This client's wordWrap setter is an unstripping stub. Insert measured line breaks instead.
        title = new GUIStyle(GUI.skin.label); title.alignment = TextAnchor.MiddleCenter; title.fontSize = scale + 9; title.fontStyle = FontStyle.Bold;
        body = new GUIStyle(GUI.skin.label); body.alignment = TextAnchor.UpperCenter; body.fontSize = scale;
        footer = new GUIStyle(GUI.skin.label); footer.alignment = TextAnchor.UpperCenter; footer.fontSize = scale - 2;
        title.normal.textColor = body.normal.textColor = footer.normal.textColor = new Color(0.98f, 0.97f, 0.94f);
    }

    public static void Clear()
    {
        for (int i = 0; i < wheel.Length; i++) if (wheel[i] != null) { UnityEngine.Object.Destroy(wheel[i]); wheel[i] = null; }
        if (dot != null) { UnityEngine.Object.Destroy(dot); dot = null; }
        title = body = footer = null;
        titleText.Clear(); bodyText.Clear(); footerText.Clear();
    }
}

// Cached, measured wrapping avoids the stripped GUIStyle.wordWrap setter. Only changed
// text, style size or width performs native measurement; stable repaint frames reuse it.
public sealed class SkateWheelText
{
    private string source = "", shown = "";
    private GUIStyle measuredStyle;
    private GUIStyle measuringStyle;
    private GUIContent content;
    private float measuredWidth;
    private int measuredSize;
    public float Height { get; private set; }
    public string Get(string text, GUIStyle style, float width)
    {
        if (ReferenceEquals(measuredStyle, style) && measuredSize == style.fontSize && measuredWidth == width && source == text) return shown;
        if (content == null) content = new GUIContent();
        measuringStyle = style;
        var wrapped = Wrap(text, width, Measure);
        content.text = wrapped; var height = style.CalcSize(content).y;
        if (float.IsNaN(height) || float.IsInfinity(height) || height < 0) throw new InvalidOperationException("Text height is unavailable.");
        measuredStyle = style; measuredSize = style.fontSize; measuredWidth = width; source = text; shown = wrapped; Height = height;
        return shown;
    }
    private float Measure(string text)
    {
        content.text = text; var width = measuringStyle.CalcSize(content).x;
        if (float.IsNaN(width) || float.IsInfinity(width) || width < 0) throw new InvalidOperationException("Text width is unavailable.");
        return width;
    }
    public void Clear() { measuredStyle = measuringStyle = null; content = null; source = shown = ""; Height = 0; }
    public static string Wrap(string text, float width, Func<string, float> measure)
    {
        if (text == null) return "";
        if (float.IsNaN(width) || float.IsInfinity(width) || width <= 0) throw new ArgumentOutOfRangeException("width");
        var result = new StringBuilder(); var paragraphs = text.Replace("\r", "").Split('\n');
        for (var p = 0; p < paragraphs.Length; p++)
        {
            if (p != 0) result.Append('\n');
            var line = "";
            foreach (var word in paragraphs[p].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length != 0 && measure(candidate) > width) { result.Append(line).Append('\n'); line = ""; }
                var rest = word;
                while (rest.Length > 1 && measure(rest) > width)
                {
                    var low = 1; var high = rest.Length;
                    while (low + 1 < high) { var middle = (low + high) / 2; if (measure(rest.Substring(0, middle)) <= width) low = middle; else high = middle; }
                    result.Append(rest.Substring(0, low)).Append('\n'); rest = rest.Substring(low);
                }
                line = line.Length == 0 ? rest : line + " " + rest;
            }
            result.Append(line);
        }
        return result.ToString();
    }
}

// Required support, not a permission to build: all cells share one neutral outline. The server
// still checks support, ownership, clearance and payment. No physics/network query is added here.
public static class SkateSupportFootprint
{
    private const int MaximumCells = 84;
    private const float Width = 0.04f;
    private static readonly Vector2[] cells = new Vector2[MaximumCells];
    private static int kind = -1, count;
    private static float minX, maxX, minZ, maxZ;
    private static bool visible, clientFits, worldFailed, worldShown, poseSet, saidWorld, saidGui;
    private static SkateParkTransform shownFrame;
    private static string title = "";
    private static GameObject root;
    private static Mesh mesh;
    private static Material material;
    private static Texture2D pixel;
    private static GUIStyle label;
    private static readonly SkateWheelText titleText = new SkateWheelText(), statusText = new SkateWheelText(), helpText = new SkateWheelText();
    private static readonly Color Amber = new Color(1f, 0.71f, 0.25f, 0.85f);

    public static void Show(SkateParkKind choice, SkateParkTransform frame, bool fits)
    {
        visible = false;
        if (!Finite(frame.X) || !Finite(frame.Y) || !Finite(frame.Z) || !Finite(frame.Yaw)) { Hide(); return; }
        var wanted = SkateParkLayout.SupportCellCount(choice);
        if (wanted <= 0 || wanted > MaximumCells) { Hide(); return; }
        try
        {
            if (kind != (int)choice || count != wanted)
            {
                Hide(); count = 0; minX = minZ = float.MaxValue; maxX = maxZ = float.MinValue;
                for (var i = 0; i < wanted; i++)
                {
                    SkateParkTransform cell;
                    if (!SkateParkLayout.TrySupportCellPose(choice, i, 0, 0, 0, out cell)) return;
                    cells[i] = new Vector2((float)cell.X, (float)cell.Z);
                    var half = (float)SkateParkLayout.CellSize / 2f;
                    minX = Math.Min(minX, cells[i].x - half); maxX = Math.Max(maxX, cells[i].x + half);
                    minZ = Math.Min(minZ, cells[i].y - half); maxZ = Math.Max(maxZ, cells[i].y + half);
                }
                count = wanted; kind = (int)choice;
                title = SkateBuildWheel.Labels[kind] + " - " + count + " support cells";
                RebuildWorld();
            }
            clientFits = fits; visible = true;
            if (root != null && !worldFailed)
            {
                if (!poseSet || frame.X != shownFrame.X || frame.Y != shownFrame.Y || frame.Z != shownFrame.Z || frame.Yaw != shownFrame.Yaw)
                {
                    root.transform.position = new Vector3((float)frame.X, (float)frame.Y + 0.04f, (float)frame.Z);
                    root.transform.rotation = Quaternion.Euler(0, (float)frame.Yaw, 0); shownFrame = frame; poseSet = true;
                }
                if (!worldShown) { root.SetActive(true); worldShown = true; }
            }
        }
        catch (Exception e)
        {
            // Rendering is optional; a failed outline must not block the building plan or its input.
            worldFailed = true; Deactivate();
            if (!saidWorld) { saidWorld = true; Out.Say("PIECES support outline unavailable: " + e.GetType().Name); }
            visible = count == wanted;
        }
    }
    private static bool Finite(double n) { return !double.IsNaN(n) && !double.IsInfinity(n); }

    private static void RebuildWorld()
    {
        Deactivate(); poseSet = false;
        if (mesh != null) { UnityEngine.Object.Destroy(mesh); mesh = null; }
        if (worldFailed) return;
        if (material == null)
        {
            var shader = Shader.Find(SkateBoard.PlainShader);
            if (shader == null) throw new InvalidOperationException("No support outline shader.");
            material = Out.Keep(new Material(shader)); material.color = Amber;
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            material.SetFloat("_ZWrite", 0);
            material.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
        }
        var vertices = new Vector3[count * 16 + 8]; var triangles = new int[count * 24 + 12];
        var colors = new Color[vertices.Length]; var v = 0; var t = 0;
        for (var i = 0; i < count; i++)
        {
            var half = (float)SkateParkLayout.CellSize / 2f;
            float x = cells[i].x - half, z = cells[i].y - half, size = half * 2f;
            Quad(vertices, triangles, ref v, ref t, x, z, size, Width);
            Quad(vertices, triangles, ref v, ref t, x, z + size - Width, size, Width);
            Quad(vertices, triangles, ref v, ref t, x, z, Width, size);
            Quad(vertices, triangles, ref v, ref t, x + size - Width, z, Width, size);
        }
        // The same anchor marked in the inset, without changing the ghost/model's own origin.
        Quad(vertices, triangles, ref v, ref t, -0.3f, -Width, 0.6f, Width * 2);
        Quad(vertices, triangles, ref v, ref t, -Width, -0.3f, Width * 2, 0.6f);
        for (var i = 0; i < colors.Length; i++) colors[i] = Color.white;
        mesh = Out.Keep(new Mesh()); mesh.name = "skate required support";
        mesh.vertices = vertices; mesh.triangles = triangles; mesh.colors = colors; mesh.RecalculateBounds();
        if (root == null)
        {
            root = new GameObject("skate required support"); root.SetActive(false);
            root.AddComponent<MeshFilter>(); root.AddComponent<MeshRenderer>().sharedMaterial = material;
        }
        root.GetComponent<MeshFilter>().sharedMesh = mesh;
    }
    private static void Quad(Vector3[] vertices, int[] triangles, ref int v, ref int t, float x, float z, float width, float depth)
    {
        vertices[v] = new Vector3(x, 0, z); vertices[v + 1] = new Vector3(x + width, 0, z);
        vertices[v + 2] = new Vector3(x + width, 0, z + depth); vertices[v + 3] = new Vector3(x, 0, z + depth);
        triangles[t++] = v; triangles[t++] = v + 2; triangles[t++] = v + 1;
        triangles[t++] = v; triangles[t++] = v + 3; triangles[t++] = v + 2; v += 4;
    }

    public static void Hide()
    {
        visible = false; Deactivate();
    }
    private static void Deactivate()
    { try { if (root != null && worldShown) root.SetActive(false); } catch (Exception) { worldFailed = true; } worldShown = false; }
    private static void DestroyOwned(UnityEngine.Object value)
    { try { if (value != null) UnityEngine.Object.Destroy(value); } catch (Exception) { } }
    public static void Reset()
    {
        Hide();
        DestroyOwned(root); DestroyOwned(mesh); DestroyOwned(material); DestroyOwned(pixel);
        root = null; mesh = null; material = null; pixel = null; label = null;
        titleText.Clear(); statusText.Clear(); helpText.Clear();
        kind = -1; count = 0; worldFailed = poseSet = saidWorld = saidGui = false;
    }

    public static void Draw()
    {
        if (!visible || count == 0 || SkateRide.On || SkatePieces.Open) return;
        var old = GUI.color;
        try
        {
            if (pixel == null) { pixel = Out.Keep(new Texture2D(1, 1)); pixel.SetPixel(0, 0, Color.white); pixel.Apply(); }
            if (label == null) { label = new GUIStyle(GUI.skin.label); label.normal.textColor = Color.white; }
            var k = Math.Max(0.6f, Math.Min(Screen.height / 1080f, Screen.width / 1600f));
            float w = 280f * k, x = Screen.width - w - 24f * k, y = 155f * k, textWidth = w - 24f * k;
            label.fontSize = (int)(15f * k); var heading = titleText.Get(title, label, textWidth);
            var titleH = Math.Max(38f * k, titleText.Height);
            label.fontSize = (int)(13f * k); var status = statusText.Get(clientFits ? "Client support found. Server must still confirm." : "Support unverified. Outline shows all required cells.", label, textWidth);
            var statusH = Math.Max(35f * k, statusText.Height); var statusY = y + 9f * k + titleH + 4f * k;
            float chartY = statusY + statusH + 8f * k, chartH = 165f * k;
            label.fontSize = (int)(12f * k); var help = helpText.Get(worldFailed ? "World outline unavailable. Full required grid shown above."
                : "Top view: front is up, + marks the anchor. Cells are requirements, not verified foundations.", label, textWidth);
            var helpY = chartY + chartH + 9f * k; var helpH = Math.Max(46f * k, helpText.Height);
            Box(new Rect(x, y, w, helpY + helpH + 9f * k - y), new Color(0.06f, 0.07f, 0.09f, 0.9f));
            label.fontSize = (int)(15f * k); GUI.color = Color.white;
            GUI.Label(new Rect(x + 12f * k, y + 9f * k, textWidth, titleH), heading, label);
            label.fontSize = (int)(13f * k);
            GUI.Label(new Rect(x + 12f * k, statusY, textWidth, statusH), status, label);
            float chartX = x + 16f * k, chartW = w - 32f * k;
            float unit = Math.Min(chartW / (maxX - minX), chartH / (maxZ - minZ));
            chartX += (chartW - (maxX - minX) * unit) / 2f;
            chartY += (chartH - (maxZ - minZ) * unit) / 2f;
            var half = (float)SkateParkLayout.CellSize / 2f;
            for (var i = 0; i < count; i++)
            {
                var cell = new Rect(chartX + (cells[i].x - half - minX) * unit,
                    chartY + (maxZ - cells[i].y - half) * unit, half * 2f * unit, half * 2f * unit);
                Box(new Rect(cell.x + 1, cell.y + 1, cell.width - 2, cell.height - 2), new Color(1f, 0.71f, 0.25f, 0.12f));
                Box(new Rect(cell.x, cell.y, cell.width, 1), Amber); Box(new Rect(cell.x, cell.yMax - 1, cell.width, 1), Amber);
                Box(new Rect(cell.x, cell.y, 1, cell.height), Amber); Box(new Rect(cell.xMax - 1, cell.y, 1, cell.height), Amber);
            }
            float ax = chartX - minX * unit, ay = chartY + maxZ * unit;
            Box(new Rect(ax - 4 * k, ay - k, 8 * k, 2 * k), Color.white);
            Box(new Rect(ax - k, ay - 4 * k, 2 * k, 8 * k), Color.white);
            GUI.color = Color.white; label.fontSize = (int)(12f * k);
            GUI.Label(new Rect(x + 12f * k, helpY, textWidth, helpH), help, label);
        }
        catch (Exception e) { if (!saidGui) { saidGui = true; Out.Say("PIECES support inset unavailable: " + e.GetType().Name); } }
        finally { GUI.color = old; }
    }
    private static void Box(Rect r, Color color) { GUI.color = color; GUI.DrawTexture(r, pixel); }
}
