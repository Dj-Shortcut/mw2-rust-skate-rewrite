using System;
using UnityEngine;

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
        if (failed || !SkatePieces.Open) return;
        var old = GUI.color;
        try
        {
            float diameter = PointerRadius * 2, cx = Screen.width * 0.5f, cy = Screen.height * 0.48f;
            int selected = SkatePieces.Hover;
            var texture = Texture(selected);
            GUI.DrawTexture(new Rect(cx - diameter / 2, cy - diameter / 2, diameter, diameter), texture);
            EnsureStyles(diameter);
            float hole = diameter * 0.52f;
            GUI.color = new Color(0.98f, 0.97f, 0.94f);
            GUI.Label(new Rect(cx - hole / 2, cy - 63, hole, 58), selected < 0 ? "SKATE PARK" : Labels[selected].ToUpperInvariant(), title);
            GUI.Label(new Rect(cx - hole / 2 + 10, cy - 7, hole - 20, 74), SkatePieces.Status.Length > 0 ? SkatePieces.Status :
                selected < 0 ? "Move the mouse or right stick to choose a piece." : Descriptions[selected], body);
            GUI.Label(new Rect(cx - hole / 2, cy + 79, hole, 28), "10 WOOD", footer);
            GUI.Label(new Rect(cx - diameter / 2, cy + diameter / 2 + 14, diameter, 66),
                "Release right click / Back to choose   |   A / Enter: choose\nEsc / B: Rust building   |   Shift + right click: Rust wheel", footer);
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
        title = new GUIStyle(); title.alignment = TextAnchor.MiddleCenter; title.wordWrap = true; title.fontSize = scale + 9; title.fontStyle = FontStyle.Bold;
        body = new GUIStyle(); body.alignment = TextAnchor.UpperCenter; body.wordWrap = true; body.fontSize = scale;
        footer = new GUIStyle(); footer.alignment = TextAnchor.UpperCenter; footer.wordWrap = true; footer.fontSize = scale - 2;
        title.normal.textColor = body.normal.textColor = footer.normal.textColor = new Color(0.98f, 0.97f, 0.94f);
    }

    public static void Clear()
    {
        for (int i = 0; i < wheel.Length; i++) if (wheel[i] != null) { UnityEngine.Object.Destroy(wheel[i]); wheel[i] = null; }
        if (dot != null) { UnityEngine.Object.Destroy(dot); dot = null; }
        title = body = footer = null;
    }
}
