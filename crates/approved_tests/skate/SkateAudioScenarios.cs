using System;
using System.IO;
using System.Security.Cryptography;
using Shortcut.RustMod;

internal static class SkateAudioScenarios
{
    private static int checks, clips;
    private static readonly double[] defaults = { 1, 1, 0.18, 0.14, 0.28, 0.65, 1 };

    private static void Check(bool value, string name)
    {
        checks++;
        if (!value) throw new InvalidOperationException(name);
    }

    private static float[] Create(SkateSound sound, int rate, double duration, uint seed)
    {
        float[] result; string error;
        Check(SkateAudio.TryCreate(sound, rate, duration, seed, out result, out error), "create " + sound);
        Check(error == null && result != null, "successful output " + sound);
        Check(result.Length == (int)Math.Round(rate * duration, MidpointRounding.AwayFromZero), "quantized duration " + sound);
        clips++;
        return result;
    }

    private static bool Loop(SkateSound sound)
    {
        return sound == SkateSound.Rolling || sound == SkateSound.Grind || sound == SkateSound.DeckSlide;
    }

    private static void Signal(SkateSound sound, float[] data)
    {
        double sum = 0, square = 0, peak = 0, front = 0, tail = 0;
        bool finite = true;
        for (var i = 0; i < data.Length; i++)
        {
            var x = data[i]; finite &= !float.IsNaN(x) && !float.IsInfinity(x);
            sum += x; square += x * (double)x; peak = Math.Max(peak, Math.Abs(x));
            if (i < data.Length / 3) front += x * (double)x;
            if (i >= data.Length * 2 / 3) tail += x * (double)x;
        }
        Check(finite, "finite samples " + sound);
        Check(peak > 0.05 && peak < 0.9, "nonzero unclipped peak " + sound);
        Check(square / data.Length > 0.00001, "nonzero energy " + sound);
        Check(Math.Abs(sum / data.Length) < 0.0000001, "zero DC " + sound);
        if (Loop(sound))
        {
            Check(data[0] == 0f, "periodic origin " + sound);
            Check(data[1] - data[0] == data[0] - data[data.Length - 1], "loop seam slope " + sound);
            Check(data[2] - data[1] == data[data.Length - 1] - data[data.Length - 2], "loop adjacent seam slopes " + sound);
            double worst = 0;
            for (var i = 1; i < data.Length; i++) worst = Math.Max(worst, Math.Abs(data[i] - data[i - 1]));
            Check(Math.Abs(data[0] - data[data.Length - 1]) <= worst + 0.0000001, "loop join is an ordinary step " + sound);
        }
        else
        {
            Check(data[0] == 0f && data[data.Length - 1] == 0f, "shot silent endpoints " + sound);
            Check(front > tail, "shot decays " + sound);
        }
    }

    private static bool Equal(float[] a, float[] b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
            if (BitConverter.SingleToInt32Bits(a[i]) != BitConverter.SingleToInt32Bits(b[i])) return false;
        return true;
    }

    private static double Roughness(float[] data)
    {
        double difference = 0, energy = 0;
        for (var i = 0; i < data.Length; i++)
        {
            var d = data[(i + 1) % data.Length] - data[i];
            difference += d * (double)d; energy += data[i] * (double)data[i];
        }
        return Math.Sqrt(difference / energy);
    }

    private static void Reject(SkateSound sound, int rate, double duration)
    {
        float[] samples; string error;
        Check(!SkateAudio.TryCreate(sound, rate, duration, 11, out samples, out error), "invalid request refused");
        Check(samples == null && !string.IsNullOrEmpty(error), "invalid request has no samples");
    }

    private static void Preview(string directory, SkateSound sound, float[] data)
    {
        Directory.CreateDirectory(directory);
        using (var w = new BinaryWriter(File.Create(Path.Combine(directory, sound + ".wav"))))
        {
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + data.Length * 2);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(44100); w.Write(88200); w.Write((short)2); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(data.Length * 2);
            foreach (var x in data) w.Write((short)Math.Round(x * 32767));
        }
    }

    public static int Main(string[] args)
    {
        try
        {
            var library = typeof(SkateAudio).Assembly.Location;
            using (var hash = SHA256.Create())
                Console.WriteLine("Actual library: " + library + "\nSHA-256: " + BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(library))).Replace("-", "").ToLowerInvariant());
            Check((int)SkateSound.Rolling == 0 && (int)SkateSound.Grind == 1 && (int)SkateSound.Push == 2 &&
                  (int)SkateSound.Ollie == 3 && (int)SkateSound.Landing == 4 && (int)SkateSound.Bail == 5 && (int)SkateSound.DeckSlide == 6,
                  "additive enum compatibility");
            for (var kind = 0; kind < defaults.Length; kind++)
            {
                var sound = (SkateSound)kind;
                float[] standard; string error;
                Check(SkateAudio.TryCreate(sound, 44100, 11, out standard, out error) && error == null, "default overload " + sound);
                clips++;
                Check(Equal(standard, Create(sound, 44100, defaults[kind], 11)), "default duration contract " + sound);
                Signal(sound, standard);
                if (args.Length == 1) Preview(args[0], sound, standard);
                foreach (var rate in new[] { 8000, 44100, 48000 })
                    foreach (var duration in new[] { 0.02, 0.0200625, 0.17, 1.0 })
                        foreach (var seed in new[] { 0u, 11u, uint.MaxValue })
                        {
                            var a = Create(sound, rate, duration, seed); Signal(sound, a);
                            var b = Create(sound, rate, duration, seed); Check(!ReferenceEquals(a, b), "independent caller-owned arrays " + sound); Check(Equal(a, b), "bit-identical seeded reproduction " + sound);
                        }
                Signal(sound, Create(sound, 192000, 0.02, 11));
                Signal(sound, Create(sound, 8000, 10, 11));
                Check(!Equal(Create(sound, 44100, defaults[kind], 11), Create(sound, 44100, defaults[kind], 12)), "seed changes texture " + sound);
            }
            var rolling = Create(SkateSound.Rolling, 44100, 1, 11);
            var metal = Create(SkateSound.Grind, 44100, 1, 11);
            var wood = Create(SkateSound.DeckSlide, 44100, 1, 11);
            Check(Roughness(metal) > Roughness(wood), "metal contact has greater sample roughness than deck contact");
            Check(Roughness(metal) > Roughness(rolling), "metal rasp differs from wheels");
            Check(!Equal(metal, wood) && !Equal(rolling, wood), "distinct surface/contact profiles");
            Console.WriteLine("Contact roughness: wheels=" + Roughness(rolling).ToString("F4") + " metal=" + Roughness(metal).ToString("F4") + " deck=" + Roughness(wood).ToString("F4"));
            Signal(SkateSound.Rolling, Create(SkateSound.Rolling, 192000, 10, 0));
            foreach (var sound in new[] { (SkateSound)(-1), (SkateSound)7, (SkateSound)int.MaxValue }) Reject(sound, 44100, 1);
            foreach (var rate in new[] { int.MinValue, 0, 7999, 192001, int.MaxValue }) Reject(SkateSound.Rolling, rate, 1);
            foreach (var duration in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity, -1, 0, 0.019999, 10.000001 }) Reject(SkateSound.Ollie, 44100, duration);
            float[] invalid; string invalidError;
            Check(!SkateAudio.TryCreate((SkateSound)7, 44100, 0, out invalid, out invalidError) && invalid == null && invalidError != null, "default invalid kind");
            Console.WriteLine("skate_audio_surfaces_and_events: " + checks + " checks, " + clips + " generated clips passed");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
