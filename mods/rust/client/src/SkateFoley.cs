using System;

// The board's smaller sounds, made here from seeded noise and damped tones: the truck clank as a
// grind locks on, the clack coming off it, a foundation seam under the wheels, a slam into a wall,
// the board set down or stepped off, the tail scraping round in a revert. Three loops sit under the
// rolling sound: the hollow boom of a wooden ramp, the crunch of dirt and grass, and the wheels
// spinning free in the air. Engine-free, so a plain compiler can render and check every clip.
public enum SkateFoleySound { GrindIn, SlideIn, GrindOut, Seam, Slam, Drop, Scrape, Hollow, Grit, Whirr }

public static class SkateFoley
{
    public const int Count = 10;

    public static bool IsLoop(SkateFoleySound s) { return s == SkateFoleySound.Hollow || s == SkateFoleySound.Grit || s == SkateFoleySound.Whirr; }

    public static double Seconds(SkateFoleySound s)
    {
        switch (s)
        {
            case SkateFoleySound.GrindIn: return 0.45;
            case SkateFoleySound.SlideIn: return 0.22;
            case SkateFoleySound.GrindOut: return 0.18;
            case SkateFoleySound.Seam: return 0.12;
            case SkateFoleySound.Slam: return 0.5;
            case SkateFoleySound.Drop: return 0.2;
            case SkateFoleySound.Scrape: return 0.4;
            default: return 2.0;
        }
    }

    public static float[] Create(SkateFoleySound sound, int rate, uint seed)
    {
        var s = new float[(int)Math.Round(rate * Seconds(sound))];
        uint r = seed * 2654435761u + (uint)sound * 40503u + 1u;
        if (r == 0) r = 1;
        var loop = IsLoop(sound);
        switch (sound)
        {
            case SkateFoleySound.GrindIn:
                // Steel truck onto steel: a bright inharmonic ring over a short scrape.
                Burst(s, rate, 0, 0.03, 1800, 9000, 0.5, ref r, false);
                Hit(s, rate, 0, 523, 0.16, 0.30, false); Hit(s, rate, 0, 1347, 0.11, 0.34, false);
                Hit(s, rate, 0, 2871, 0.07, 0.22, false); Hit(s, rate, 0, 4130, 0.05, 0.12, false);
                Hit(s, rate, Ms(rate, 9), 211, 0.05, 0.35, false);
                break;
            case SkateFoleySound.SlideIn:
                // The deck slapping down on the edge: wood, low and short.
                Burst(s, rate, 0, 0.05, 250, 1800, 0.6, ref r, false);
                Hit(s, rate, 0, 148, 0.05, 0.6, false); Hit(s, rate, 0, 390, 0.03, 0.3, false); Hit(s, rate, 0, 870, 0.015, 0.15, false);
                break;
            case SkateFoleySound.GrindOut:
                // Trucks leaving the edge: a dry clack, the wheels touching after.
                Burst(s, rate, 0, 0.015, 1200, 7000, 0.45, ref r, false);
                Hit(s, rate, 0, 760, 0.03, 0.3, false); Hit(s, rate, 0, 1910, 0.02, 0.18, false);
                Hit(s, rate, Ms(rate, 38), 120, 0.03, 0.4, false); Burst(s, rate, Ms(rate, 38), 0.02, 200, 2500, 0.3, ref r, false);
                break;
            case SkateFoleySound.Seam:
                // Front wheels then back wheels over a joint: two small ticks.
                Tick(s, rate, 0, 1.0, ref r); Tick(s, rate, Ms(rate, 55), 0.8, ref r);
                break;
            case SkateFoleySound.Slam:
                // Board and body into a wall: a heavy thud and the board rattling after it.
                Burst(s, rate, 0, 0.06, 80, 1800, 0.9, ref r, false);
                Hit(s, rate, 0, 58, 0.12, 0.9, false); Hit(s, rate, 0, 131, 0.06, 0.4, false);
                for (var i = 0; i < 4; i++) Hit(s, rate, Ms(rate, 90 + 70 * i + (int)(Next(ref r) % 25u)), 300 + 140 * i, 0.03, 0.22 / (i + 1), false);
                break;
            case SkateFoleySound.Drop:
                // The board set down: tail wheels, then the nose a moment later.
                Hit(s, rate, 0, 140, 0.04, 0.6, false); Burst(s, rate, 0, 0.02, 400, 5000, 0.4, ref r, false);
                Hit(s, rate, Ms(rate, 45), 165, 0.04, 0.45, false); Burst(s, rate, Ms(rate, 45), 0.02, 400, 5000, 0.3, ref r, false);
                Hit(s, rate, Ms(rate, 45), 980, 0.012, 0.12, false);
                break;
            case SkateFoleySound.Scrape:
                // The tail dragged round: grit under wood, swelling and fading.
                for (var i = 0; i < s.Length; i += Ms(rate, 6))
                {
                    var p = i / (double)s.Length;
                    Burst(s, rate, i, 0.012, 250, 2500, 0.5 * Math.Sin(Math.PI * p), ref r, false);
                }
                break;
            case SkateFoleySound.Hollow:
                // A wooden ramp is a box: rolling over it booms in a few low modes, with soft knocks
                // from the plies.
                Tones(s, rate, 55, 420, 90, new[] { 96.0, 171.0, 268.0 }, ref r);
                for (var i = 0; i < 26; i++) Hit(s, rate, (int)(Next(ref r) % (uint)s.Length), 120 + Next(ref r) % 160u, 0.035, 0.25 + 0.2 * Unit(ref r), true);
                break;
            case SkateFoleySound.Grit:
                // Dirt and grass: many small crunches, no tone.
                for (var i = 0; i < 520; i++) Tick(s, rate, (int)(Next(ref r) % (uint)s.Length), 0.2 + 0.8 * Unit(ref r), ref r, true);
                Tones(s, rate, 40, 300, 60, null, ref r, 0.35);
                break;
            case SkateFoleySound.Whirr:
                // Wheels spinning free: a bearing hum and its partials, gently beating, and a thin hiss.
                Tones(s, rate, 2500, 7000, 50, null, ref r, 0.12);
                foreach (var f in new[] { 186.0, 187.5, 372.0, 559.0, 745.5, 1120.0 })
                {
                    var phase = Unit(ref r) * 2 * Math.PI; var a = 0.5 / (1 + f / 300.0);
                    Sine(s, 2 * Math.PI * Math.Round(f * s.Length / rate) / s.Length, phase, a);
                }
                break;
        }
        RemoveDc(s, loop);
        if (!loop) Fade(s, rate);
        Normalise(s, loop ? 0.5 : 0.8);
        return s;
    }

    private static int Ms(int rate, int ms) { return rate * ms / 1000; }

    // A damped sine from sample at on; wrap: it runs on round the end into the start, so a loop has
    // no seam.
    private static void Hit(float[] s, int rate, int at, double freq, double decay, double gain, bool wrap)
    {
        var w = 2 * Math.PI * freq / rate; var k = Math.Exp(-1.0 / (rate * decay));
        var n = Math.Min(s.Length, (int)(rate * decay * 7));
        var e = gain;
        for (var i = 0; i < n; i++, e *= k)
        {
            var j = at + i; if (j >= s.Length) { if (!wrap) break; j -= s.Length; }
            // A two-sample rise keeps the start from clicking.
            s[j] += (float)(e * Math.Sin(w * i) * Math.Min(1.0, i / 2.0));
        }
    }

    // Band-limited noise falling away over decay seconds.
    private static void Burst(float[] s, int rate, int at, double decay, double low, double high, double gain, ref uint r, bool wrap)
    {
        var a = Math.Exp(-2 * Math.PI * low / rate); var b = Math.Exp(-2 * Math.PI * Math.Min(high, rate * 0.45) / rate);
        var k = Math.Exp(-1.0 / (rate * decay));
        double slow = 0, fast = 0, e = gain;
        var n = Math.Min(s.Length, (int)(rate * decay * 7));
        for (var i = 0; i < n; i++, e *= k)
        {
            var j = at + i; if (j >= s.Length) { if (!wrap) break; j -= s.Length; }
            var x = Noise(ref r);
            fast = b * fast + (1 - b) * x; slow = a * slow + (1 - a) * x;
            s[j] += (float)(e * (fast - slow) * 3);
        }
    }

    private static void Tick(float[] s, int rate, int at, double gain, ref uint r, bool wrap = false)
    {
        Burst(s, rate, at, 0.0025, 1500, 9000, 0.6 * gain, ref r, wrap);
        Hit(s, rate, at, 900 + Next(ref r) % 1400u, 0.004, 0.25 * gain, wrap);
    }

    // A periodic noise band from summed sines on whole cycles of the clip, so it loops without a
    // seam; peaks: modes boosted on top of it.
    private static void Tones(float[] s, int rate, double low, double high, int count, double[] peaks, ref uint r, double gain = 1)
    {
        var len = s.Length;
        for (var c = 0; c < count; c++)
        {
            var f = low * Math.Pow(high / low, (c + Unit(ref r)) / count);
            var a = gain / Math.Sqrt(count) * (0.5 + Unit(ref r)) * Math.Pow(low / f, 0.4);
            if (peaks != null) foreach (var p in peaks) a *= 1 + 3 * Math.Exp(-Math.Pow((f - p) / (p * 0.08), 2));
            Sine(s, 2 * Math.PI * Math.Max(1, Math.Round(f * len / rate)) / len, Unit(ref r) * 2 * Math.PI, a);
        }
    }

    // A sine by rotation, which is cheaper than calling Sin per sample; renormalised every so often
    // so the rounding does not let it drift.
    private static void Sine(float[] s, double w, double phase, double a)
    {
        double c = Math.Cos(w), sn = Math.Sin(w), x = Math.Cos(phase), y = Math.Sin(phase);
        for (var i = 0; i < s.Length; i++)
        {
            s[i] += (float)(a * y);
            var nx = x * c - y * sn; y = x * sn + y * c; x = nx;
            if ((i & 4095) == 4095) { var m = Math.Sqrt(x * x + y * y); x /= m; y /= m; }
        }
    }

    private static void RemoveDc(float[] s, bool loop)
    {
        double sum = 0; foreach (var v in s) sum += v;
        var m = (float)(sum / s.Length);
        if (loop || Math.Abs(m) > 1e-6) for (var i = 0; i < s.Length; i++) s[i] -= m;
    }

    // One-shots end on silence and start from it.
    private static void Fade(float[] s, int rate)
    {
        var n = Math.Min(s.Length / 4, Ms(rate, 20));
        for (var i = 0; i < n; i++) s[s.Length - 1 - i] *= i / (float)n;
        s[0] = 0f;
    }

    private static void Normalise(float[] s, double peak)
    {
        double m = 0; foreach (var v in s) m = Math.Max(m, Math.Abs(v));
        if (m <= 0) return;
        var g = (float)(peak / m);
        for (var i = 0; i < s.Length; i++) s[i] *= g;
    }

    private static uint Next(ref uint x) { x ^= x << 13; x ^= x >> 17; x ^= x << 5; return x; }
    private static double Unit(ref uint x) { return (Next(ref x) >> 8) / 16777216.0; }
    private static double Noise(ref uint x) { return Unit(ref x) * 2 - 1; }
}
