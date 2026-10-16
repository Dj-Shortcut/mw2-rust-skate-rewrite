// Player-facing text: English only.
// The score display is drawn with the engine's immediate GUI, which is all a plugin can rely on
// here. A client build keeps only the engine methods the game itself calls, so everything beyond a
// plain label is tried once and done without when it is missing: the flat panels and bars, and the
// condensed face of the game's own interface, which is taken from the fonts the game has loaded.
// What is left then is the same layout in the engine's default font.
// The layout follows the game's own: flat dark panels, condensed letters, one warm accent.
//   top right     the session's score, counting up when a combo is banked, and under it the
//                 session's bests: combo, top speed, longest grind and manual
//   bottom centre the open combo: its tricks in a row, their points and the multiplier, and a bar
//                 that runs out while the board rolls without a trick; a manual or grind as it
//                 runs sits in that row with its seconds and points
//   upper centre  the trick that just landed, coloured by its kind, a bail
//   bottom left   speed, the last tricks landed with their points (the feed), and the controls
//                 for the first seconds on the board
using System;
using System.Collections.Generic;
using System.Text;
using Il2CppInterop.Runtime;
using Shortcut.RustMod;
using UnityEngine;

public static class SkateHud
{
    public static float MountedAt = -99f;

    // What the display shows. The ride fills it every frame; the pose check shows made-up ones.
    public struct Shown
    {
        public bool Riding, Switch, Fakie, Pad, Hints;
        public float Speed, Cap, Window;
        public long Score, Base, LivePoints;
        public int Count;
        public string Live;
        public float LiveSeconds;
    }

    // The session's bests, kept while the plugin lives.
    public static long BestCombo;
    public static float TopSpeed, LongestGrind, LongestManual;
    private struct Fed { public string Name; public long Points; public float At; }
    private static readonly List<Fed> feed = new List<Fed>();
    private const int MostFed = 5;
    private const float FeedSeconds = 5f;
    public static bool Staged;
    public static Shown Stage;

    private const int MostInLine = 4;
    private const float PopSeconds = 1.8f, BankSeconds = 1.6f, BailSeconds = 2.2f, CountSeconds = 0.7f;
    private static readonly Color Ink = new Color(0.94f, 0.92f, 0.87f), Dim = new Color(0.72f, 0.70f, 0.65f), Accent = new Color(0.86f, 0.30f, 0.18f),
        Amber = new Color(0.98f, 0.73f, 0.22f), Good = new Color(0.62f, 0.83f, 0.30f), Bad = new Color(0.93f, 0.27f, 0.21f), Dark = new Color(0.05f, 0.05f, 0.05f);
    private const string AmberTag = "<color=#FABA38>", DimTag = "<color=#B8B2A6>", AccentTag = "<color=#DB4D2E>", End = "</color>";

    // The open combo's tricks and the moments of the last landing, banking and bail.
    private static readonly List<string> line = new List<string>();
    private static string popName = "";
    private static long popPoints, bankPoints, lostPoints, countFrom, countTo;
    private static float popAt = -99f, bankAt = -99f, bailAt = -99f, countAt = -99f;

    public static void Landed(string name, long points)
    {
        if (string.IsNullOrEmpty(name)) return;
        line.Add(name); popName = name; popPoints = points; popAt = Time.realtimeSinceStartup;
        feed.Add(new Fed { Name = name, Points = points, At = popAt });
        while (feed.Count > MostFed) feed.RemoveAt(0);
    }

    public static void Banked(long points, long total)
    {
        line.Clear(); bankPoints = points; bankAt = countAt = Time.realtimeSinceStartup; countFrom = total - points; countTo = total;
        if (points > BestCombo) BestCombo = points;
    }

    public static void Bailed(long lost) { line.Clear(); lostPoints = lost; bailAt = Time.realtimeSinceStartup; popAt = -99f; }

    public static void Clear()
    {
        line.Clear(); popAt = bankAt = bailAt = countAt = -99f;
        // Getting on is a moment to look for the game's font again: at the first look, while the
        // game was still loading, it may not have been there, and one that was can be gone.
        if (!ReferenceEquals(font, null) && font == null) { font = null; styles.Clear(); }
        if (ReferenceEquals(font, null) && fontTries > 0 && fontTries < MostFontTries) FindFont();
    }

    // For the pose check: a combo's tricks without riding them.
    public static void StageLine(string[] names) { line.Clear(); for (var i = 0; i < names.Length; i++) line.Add(names[i]); }

    // --- what the engine offers here
    private static readonly Dictionary<int, GUIStyle> styles = new Dictionary<int, GUIStyle>();
    private static readonly Dictionary<int, Texture2D> fills = new Dictionary<int, Texture2D>();
    private static readonly Dictionary<int, GUIStyle> fillStyles = new Dictionary<int, GUIStyle>();
    // Looks in a row that may come back without the game's font before the looking ends.
    private const int MostFontTries = 4;
    // Compared the engine's way, the font is null once the game has let it go.
    private static Font font;
    private static bool fontBold, anchors = true, drawTexture = true, background = true, texture = true;
    private static int fontTries;
    private static string fontSaid = "";

    // The fonts the game has in memory are told apart by name: nothing here names a type of the game.
    private static void FindFont()
    {
        fontTries++;
        string said;
        try
        {
            var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Font>());
            var names = new StringBuilder(); Font best = null; var rank = 0;
            for (var i = 0; i < all.Length; i++)
            {
                var f = all[i] == null ? null : all[i].TryCast<Font>();
                if (f == null) continue;
                var name = f.name ?? ""; var sizable = Sizable(f);
                if (names.Length < 240) names.Append(names.Length > 0 ? ", " : "").Append(name).Append(sizable ? "" : " (one size)");
                var low = name.ToLowerInvariant();
                var r = low.IndexOf("condensed", StringComparison.Ordinal) < 0 ? 0 : low.IndexOf("bold", StringComparison.Ordinal) >= 0 ? 2 : 1;
                if (r > rank && sizable) { rank = r; best = f; }
            }
            if (best != null) { font = best; fontBold = rank == 2; fontTries = 1; styles.Clear(); said = best.name + ", the game's own"; }
            else said = "the default one; the game has no condensed one loaded (" + (names.Length > 0 ? names.ToString() : "none at all") + ")";
        }
        catch (Exception e) { fontTries = MostFontTries; said = "the default one; the game's fonts cannot be listed here (" + e.GetType().Name + ")"; }
        if (said != fontSaid) { fontSaid = said; Out.Say("HUD font: " + said); }
    }

    // A font that was imported at one size ignores the sizes asked of it.
    private static bool Sizable(Font f) { try { return f.dynamic; } catch (Exception) { return true; } }

    private static GUIStyle Style(int size, int anchor)
    {
        GUIStyle s;
        var key = size * 8 + anchor;
        if (styles.TryGetValue(key, out s)) return s;
        s = new GUIStyle(GUI.skin.label);
        s.fontSize = size; s.fontStyle = FontStyle.Bold;
        if (font != null)
        {
            // The property's setter is stripped from this client and throws; the engine call behind it
            // is there. A face that is bold already is not thickened once more.
            try { GUIStyle.set_font_Injected(s.Pointer, font.Pointer); if (fontBold) s.fontStyle = FontStyle.Normal; }
            catch (Exception e) { font = null; fontTries = MostFontTries; Out.Say("HUD font: the default one; a style takes no other here (" + e.GetType().Name + ")"); }
        }
        if (anchor != 0 && anchors)
        {
            try { s.alignment = anchor == 1 ? TextAnchor.UpperCenter : TextAnchor.UpperRight; }
            catch (Exception e) { anchors = false; Out.Say("HUD centred text unavailable: " + e.GetType().Name); }
        }
        styles[key] = s;
        return s;
    }

    // The text without its tags. A shadow keeps the sizes inside its text and loses only the colours.
    private static string Plain(string rich, bool keepSizes)
    {
        if (rich.IndexOf('<') < 0) return rich;
        var b = new StringBuilder(rich.Length);
        for (var i = 0; i < rich.Length; i++)
        {
            var end = rich[i] == '<' ? rich.IndexOf('>', i) : -1;
            if (end < 0) { b.Append(rich[i]); continue; }
            var colour = string.CompareOrdinal(rich, i, "<color", 0, 6) == 0 || string.CompareOrdinal(rich, i, "</color", 0, 7) == 0;
            if (keepSizes && !colour) b.Append(rich, i, end - i + 1);
            i = end;
        }
        return b.ToString();
    }

    // A colour named inside the text does not follow the label's own: it is given the same fading.
    private static string Faded(string rich, float alpha)
    {
        const string tag = "<color=#";
        if (alpha >= 0.99f || rich.IndexOf(tag, StringComparison.Ordinal) < 0) return rich;
        var hex = ((int)(alpha * 255f)).ToString("X2");
        var b = new StringBuilder(rich.Length + 16);
        var at = 0;
        while (at < rich.Length)
        {
            var i = rich.IndexOf(tag, at, StringComparison.Ordinal);
            var close = i < 0 ? -1 : i + tag.Length + 6;
            if (i < 0 || close >= rich.Length || rich[close] != '>') { b.Append(rich, at, rich.Length - at); break; }
            b.Append(rich, at, close - at).Append(hex);
            at = close;
        }
        return b.ToString();
    }

    // anchor: 0 from the left edge of the box, 1 centred in it, 2 against its right edge.
    private static void Text(float x, float y, float width, string text, int size, Color colour, int anchor, float alpha)
    {
        if (alpha <= 0.01f) return;
        var s = Style(size, anchor);
        var height = size * 1.7f;
        if (anchor != 0 && !anchors)
        {
            // Without alignment the text is placed by a guess at its width.
            var guess = Plain(text, false).Length * size * 0.5f;
            x = anchor == 1 ? x + (width - guess) * 0.5f : x + width - guess; width = guess + 40f;
        }
        var shade = Math.Max(1f, size * 0.06f);
        s.normal.textColor = new Color(0f, 0f, 0f, 0.8f * alpha);
        GUI.Label(new Rect(x + shade, y + shade, width, height), Plain(text, true), s);
        s.normal.textColor = new Color(colour.r, colour.g, colour.b, alpha);
        GUI.Label(new Rect(x, y, width, height), Faded(text, alpha), s);
    }

    private static Texture2D Fill(Color c)
    {
        var key = ((int)(c.r * 31f) << 15) | ((int)(c.g * 31f) << 10) | ((int)(c.b * 31f) << 5) | (int)(c.a * 31f);
        Texture2D t;
        if (fills.TryGetValue(key, out t)) return t;
        t = new Texture2D(2, 2);
        for (var i = 0; i < 4; i++) t.SetPixel(i % 2, i / 2, c);
        t.Apply();
        fills[key] = t;
        return t;
    }

    // A flat rectangle: drawn as a texture, or as an empty label that has it for a background.
    private static void Box(float x, float y, float width, float height, Color colour)
    {
        if (!texture || width < 1f || height < 1f || colour.a <= 0.01f) return;
        try
        {
            var t = Fill(colour);
            var r = new Rect(x, y, width, height);
            if (drawTexture)
            {
                try { GUI.DrawTexture(r, t); return; }
                catch (Exception e) { drawTexture = false; Out.Say("HUD panels: textures cannot be drawn directly here (" + e.GetType().Name + "); labels carry them instead"); }
            }
            if (!background) return;
            GUIStyle s;
            var key = t.GetHashCode();
            if (!fillStyles.TryGetValue(key, out s))
            {
                s = new GUIStyle();
                try { s.normal.background = t; }
                catch (Exception e) { background = false; Out.Say("HUD panels unavailable: a style takes no background here (" + e.GetType().Name + ")"); return; }
                fillStyles[key] = s;
            }
            GUI.Label(r, "", s);
        }
        catch (Exception e) { texture = false; Out.Say("HUD panels unavailable: " + e.GetType().Name + ": " + e.Message); }
    }

    private static Color Fade(Color c, float alpha) { return new Color(c.r, c.g, c.b, c.a * alpha); }

    private static float Ease(float t) { t = SkateRide.Clamp(t, 0f, 1f); return 1f - (1f - t) * (1f - t); }

    private static Shown Sample(float now)
    {
        var t = SkateRide.Trick;
        var s = new Shown();
        // Switch with the tail leading is fakie, as the scorer names the tricks.
        s.Riding = SkateRide.On; s.Switch = t.Switch; s.Fakie = t.Switch && SkateRide.TailFirst != SkateRide.DeckReversed; s.Pad = SkateKeys.Pad;
        s.Hints = (now - MountedAt < 9f || SkateKeys.Help) && !Scenarios.Active;
        s.Speed = Math.Abs(SkateRide.HorizontalSpeed); s.Cap = SkateRide.Cap;
        s.Score = t.TotalPoints; s.Base = t.ComboBasePoints; s.Count = t.ComboCount;
        s.Window = (float)(t.RollingSeconds / SkateTricks.ComboRollingTimeout);
        var grinding = SkateRide.Mode == RideMode.Grind;
        s.Live = grinding ? SkateTricks.GrindName(SkateRide.GrindKind, SkateRide.GrindFrontside).ToUpperInvariant() : !t.Manualing ? "" : t.NoseManualing ? "NOSE MANUAL" : "MANUAL";
        s.LiveSeconds = grinding ? (float)t.GrindSeconds : (float)t.ManualSeconds;
        s.LivePoints = (long)Math.Round(s.LiveSeconds * (grinding ? SkateTricks.GrindRate(SkateRide.GrindKind) : SkateTricks.ManualPointsPerSecond));
        if (s.Speed > TopSpeed) TopSpeed = s.Speed;
        if (grinding && s.LiveSeconds > LongestGrind) LongestGrind = s.LiveSeconds;
        if (t.Manualing && s.LiveSeconds > LongestManual) LongestManual = s.LiveSeconds;
        return s;
    }

    public static void Draw()
    {
        var now = Time.realtimeSinceStartup;
        var v = Staged ? Stage : Sample(now);
        var onFoot = !Scenarios.Active && !v.Riding && (!SkateRig.Ready || now - SkateRig.AwakeAt < 12f || SkateKeys.Help);
        var recent = now - popAt < PopSeconds || now - bankAt < BankSeconds || now - bailAt < BailSeconds;
        var pieces = v.Riding ? "" : SkatePieces.Line(now);
        if (!Scenarios.Active && !v.Riding && !recent && !onFoot && pieces == "") return;
        float w = Screen.width, h = Screen.height;
        var k = h / 1080f; if (k < 0.75f) k = 0.75f;
        if (fontTries == 0) FindFont();

        if (Scenarios.Active) Text(40f * k, 110f * k, w - 80f * k, "SKATE TEST  " + Scenarios.Phase, (int)(19 * k), Ink, 0, 1f);
        if (onFoot)
        {
            var hint = !SkateRig.Ready ? "Skate mod " + SkatePlugin.Version + " loaded"
                : (SkatePad.Shared ? "Skate mod: press Y on the controller, or K, or jump twice to get on the board" : "Skate mod: press K or jump twice to get on the board") + "   (stance " + SkateRide.StanceName + ": hold F2" + (SkatePad.Shared ? " or Back+Y" : "") + " to switch)   hold H to see this again";
            Text(40f * k, 110f * k, w - 80f * k, hint, (int)(19 * k), Ink, 0, 1f);
        }

        if (pieces != "")
        {
            Box(40f * k, h - 120f * k, w - 80f * k, 40f * k, Fade(Dark, 0.62f));
            Text(56f * k, h - 112f * k, w - 112f * k, pieces, (int)(19 * k), SkatePieces.Open ? Ink : Amber, 0, 1f);
        }

        if (v.Riding || Staged)
        {
            Score(v, now, w, k);
            Speed(v, h, k);
            Feed(now, h, k);
            // Said for a moment when the cap drops, not for as long as it stands.
            var capSince = now - SkateRide.CapLoweredAt;
            if (v.Cap < SkateRide.MaxSpeed - 0.25f && capSince < 3f)
                Text(40f * k, h - 336f * k, w - 80f * k, "The server put the board back; held to " + (v.Cap * 3.6f).ToString("F0") + " km/h for now", (int)(18 * k), Amber, 0, capSince > 2.5f ? (3f - capSince) / 0.5f : 1f);
            Combo(v, now, w, h, k);
            if (v.Hints) Hints(v, w, h, k);
        }
        Middle(v, now, w, h, k);
    }

    private static void Score(Shown v, float now, float w, float k)
    {
        float width = 290f * k, height = 74f * k, x = w - width - 40f * k, y = 40f * k;
        var counting = now - countAt < CountSeconds;
        var shown = counting ? countFrom + (long)((countTo - countFrom) * Ease((now - countAt) / CountSeconds)) : v.Score;
        Box(x, y, width, height, Fade(Dark, 0.62f));
        Box(x, y, 6f * k, height, Accent);
        Text(x + 22f * k, y + 7f * k, width, "SCORE", (int)(15 * k), Dim, 0, 1f);
        Text(x + 22f * k, y + 23f * k, width - 30f * k, shown.ToString("N0"), (int)(38 * k), counting ? Good : Ink, 0, 1f);
        if (BestCombo > 0 || TopSpeed > 1f)
        {
            Box(x, y + height + 4f * k, width, 50f * k, Fade(Dark, 0.5f));
            Text(x + 22f * k, y + height + 9f * k, width - 30f * k, DimTag + "BEST COMBO " + End + BestCombo.ToString("N0") + DimTag + "   TOP " + End + (TopSpeed * 3.6f).ToString("F0") + DimTag + " km/h" + End, (int)(15 * k), Ink, 0, 1f);
            Text(x + 22f * k, y + height + 28f * k, width - 30f * k, DimTag + "GRIND " + End + LongestGrind.ToString("F1") + DimTag + " s   MANUAL " + End + LongestManual.ToString("F1") + DimTag + " s" + End, (int)(15 * k), Ink, 0, 1f);
        }
        var since = now - bankAt;
        if (since < BankSeconds)
        {
            var out_ = Ease(since / BankSeconds);
            Text(x - 320f * k, y + (26f - 22f * out_) * k, 300f * k, "+" + bankPoints.ToString("N0"), (int)(30 * k), Good, 2, since > BankSeconds - 0.5f ? (BankSeconds - since) / 0.5f : 1f);
        }
    }

    // The last tricks landed, newest at the bottom and brightest, gone a few seconds after the last.
    private static void Feed(float now, float h, float k)
    {
        if (feed.Count == 0) return;
        var since = now - feed[feed.Count - 1].At;
        if (since > FeedSeconds) { feed.Clear(); return; }
        var a = since > FeedSeconds - 0.6f ? (FeedSeconds - since) / 0.6f : 1f;
        float x = 40f * k, y = h - 316f * k;
        for (var i = feed.Count - 1; i >= 0; i--)
        {
            var newest = i == feed.Count - 1;
            var age = now - feed[i].At; var slide = newest && age < 0.15f ? (1f - age / 0.15f) * 14f * k : 0f;
            var size = (int)((newest ? 24 : 18) * k);
            Text(x + slide, y, 520f * k, feed[i].Name + "  " + AmberTag + "+" + feed[i].Points.ToString("N0") + End, size, newest ? Ink : Dim, 0, a * (newest ? 1f : 0.8f));
            y -= (newest ? 30f : 23f) * k;
        }
    }

    private static void Speed(Shown v, float h, float k)
    {
        float x = 40f * k, y = h - 300f * k;
        var kmh = (v.Speed * 3.6f).ToString("F0");
        Text(x, y, 400f * k, kmh + "<size=" + (int)(20 * k) + "> km/h</size>" + (v.Switch ? "<size=" + (int)(20 * k) + ">   " + AmberTag + (v.Fakie ? "FAKIE" : "SWITCH") + End + "</size>" : ""), (int)(44 * k), Ink, 0, 1f);
        float track = 210f * k, part = SkateRide.Clamp(v.Speed / SkateRide.MaxSpeed, 0f, 1f);
        Box(x, y + 62f * k, track, 6f * k, Fade(Dark, 0.62f));
        Box(x, y + 62f * k, track * part, 6f * k, v.Speed > SkateRide.MaxPush + 0.2f ? Accent : Amber);
    }

    // The tricks of the open combo in a row, the last ones if there are many; what they are worth
    // and how often it counts; and the time left to add to it.
    private static void Combo(Shown v, float now, float w, float h, float k)
    {
        var live = !string.IsNullOrEmpty(v.Live);
        var count = v.Count + (live ? 1 : 0);
        if (count <= 0 || (line.Count == 0 && !live)) return;
        var row = new StringBuilder();
        var first = Math.Max(0, line.Count - (live ? MostInLine - 1 : MostInLine));
        if (first > 0) row.Append(DimTag).Append("\u2026  ").Append(End);
        for (var i = first; i < line.Count; i++)
        {
            var last = i == line.Count - 1 && !live;
            if (i > first) row.Append(AccentTag).Append("  +  ").Append(End);
            row.Append(last ? "" : DimTag).Append(line[i]).Append(last ? "" : End);
        }
        if (live)
        {
            if (line.Count > first) row.Append(AccentTag).Append("  +  ").Append(End);
            row.Append(Title(v.Live)).Append(' ').Append(v.LiveSeconds.ToString("F1")).Append(" s");
        }
        var basePoints = v.Base + (live ? v.LivePoints : 0);
        // The newest trick makes the row jump: a little larger for a moment.
        var pop = now - popAt; var bump = pop < 0.12f ? 1f + 0.25f * (1f - pop / 0.12f) : 1f;
        Text(100f * k, h - 424f * k, w - 200f * k, row.ToString(), (int)(23 * k * bump), Ink, 1, 1f);
        Text(100f * k, h - 392f * k, w - 200f * k, basePoints.ToString("N0") + "  " + AmberTag + "\u00D7" + count + End, (int)(50 * k), Ink, 1, 1f);
        float track = 340f * k, x = (w - track) * 0.5f, y = h - 318f * k, left = live ? 1f : 1f - SkateRide.Clamp(v.Window, 0f, 1f);
        Box(x, y, track, 7f * k, Fade(Dark, 0.62f));
        Box(x + track * (1f - left) * 0.5f, y, track * left, 7f * k, left < 0.34f ? Accent : Amber);
    }

    private static void Middle(Shown v, float now, float w, float h, float k)
    {
        var y = h * 0.24f;
        var bail = now - bailAt;
        if (bail < BailSeconds)
        {
            var a = bail > BailSeconds - 0.4f ? (BailSeconds - bail) / 0.4f : 1f;
            var shake = bail < 0.3f ? (float)Math.Sin(bail * 90f) * 6f * k * (1f - bail / 0.3f) : 0f;
            Text(shake, y, w, "BAIL", (int)(64 * k), Bad, 1, a);
            if (lostPoints > 0) Text(0f, y + 84f * k, w, "combo lost   " + DimTag + "\u2212" + lostPoints.ToString("N0") + End, (int)(24 * k), Ink, 1, a);
            return;
        }
        if (!string.IsNullOrEmpty(v.Live) && now - popAt > 0.6f)
        {
            Text(0f, y, w, v.Live, (int)(40 * k), Kind(v.Live), 1, 1f);
            Text(0f, y + 54f * k, w, v.LiveSeconds.ToString("F1") + " s   " + AmberTag + "+" + v.LivePoints.ToString("N0") + End, (int)(24 * k), Ink, 1, 1f);
            return;
        }
        var bank = now - bankAt;
        if (bank < BankSeconds && bankPoints > 0)
        {
            var a = bank > BankSeconds - 0.4f ? (BankSeconds - bank) / 0.4f : 1f;
            Text(0f, y, w, "BANKED", (int)(26 * k), Dim, 1, a);
            Text(0f, y + 34f * k, w, "+" + bankPoints.ToString("N0"), (int)(54 * k), Good, 1, a);
            return;
        }
        var pop = now - popAt;
        if (pop < PopSeconds && popName != "")
        {
            var a = pop > PopSeconds - 0.4f ? (PopSeconds - pop) / 0.4f : 1f;
            var bump = pop < 0.14f ? 1f + 0.3f * (1f - pop / 0.14f) : 1f;
            Text(0f, y, w, popName, (int)(46 * k * bump), Kind(popName), 1, a);
            if (popPoints > 0) Text(0f, y + 64f * k, w, "+" + popPoints.ToString("N0"), (int)(28 * k), Amber, 1, a);
        }
    }

    // A colour by the kind of trick: grinds and slides cool, manuals and reverts warm, flips and
    // spins the plain ink, grabs green.
    private static readonly Color Cool = new Color(0.45f, 0.78f, 0.95f), Warm = new Color(0.98f, 0.60f, 0.30f);
    private static Color Kind(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("grind") || n.Contains("slide") || n.Contains("50-50") || n.Contains("5-0")) return Cool;
        if (n.Contains("manual") || n.Contains("revert")) return Warm;
        if (n.Contains("grab") || n.Contains("stalefish") || n.Contains("melon") || n.Contains("method") || n.Contains("crail") || n.Contains("seatbelt") || n.Contains("bone")) return Good;
        return Ink;
    }

    private static void Hints(Shown v, float w, float h, float k)
    {
        float x = 40f * k, y = h - 220f * k; var size = (int)(17 * k);
        Box(x - 12f * k, y - 8f * k, Math.Min(w - 80f * k, 1240f * k), 96f * k, Fade(Dark, 0.5f));
        if (v.Pad)
        {
            Text(x, y, w - 80f * k, Key("LEFT STICK") + " steer, hold to spin in the air   " + Key("A / X") + " push   " + Key("B") + " brake   " + Key("LT / RT") + " grab, with the right stick: other grabs   " + Key("Y") + " get off   " + Key("R3") + " camera", size, Ink, 0, 1f);
            Text(x, y + 28f * k, w - 80f * k, Key("RIGHT STICK") + "  back, then flick forward: ollie   to a diagonal: kickflip / heelflip   rolled round to a side: shove-it   part way back or forward: manual", size, Ink, 0, 1f);
        }
        else
        {
            Text(x, y, w - 80f * k, Key("W") + " push   " + Key("S") + " brake   " + Key("A / D") + " carve   " + Key("SPACE") + " ollie   hold " + Key("SHIFT") + " manual   " + Key("K") + " or hold " + Key("CTRL") + " get off   " + Key("L") + " camera", size, Ink, 0, 1f);
            Text(x, y + 28f * k, w - 80f * k, "In the air: tap " + Key("W") + " kickflip   tap " + Key("S") + " heelflip   " + Key("A / D") + " spin   hold " + Key("CTRL") + " grab", size, Ink, 0, 1f);
        }
        Text(x, y + 56f * k, w - 80f * k, v.Pad ? "Hold " + Key("BACK") + " on the board to see these controls again" : "Hold " + Key("H") + " to see these controls again", (int)(15 * k), Dim, 0, 1f);
    }

    private static string Key(string name) { return AmberTag + name + End; }

    // "FRONTSIDE 50-50" as "Frontside 50-50".
    private static string Title(string upper)
    {
        var b = new StringBuilder(upper.Length); var start = true;
        for (var i = 0; i < upper.Length; i++)
        {
            var c = upper[i];
            b.Append(start ? c : char.ToLowerInvariant(c));
            start = c == ' ' || c == '-';
        }
        return b.ToString();
    }
}
