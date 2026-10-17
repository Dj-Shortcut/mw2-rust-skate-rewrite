using System;

namespace Shortcut.RustMod
{
    public enum SkateSound { Rolling = 0, Grind = 1, Push = 2, Ollie = 3, Landing = 4, Bail = 5, DeckSlide = 6 }

    public static class SkateAudio
    {
        public const int MinimumSampleRate = 8000;
        public const int MaximumSampleRate = 192000;
        public const double MinimumDuration = 0.02;
        public const double MaximumDuration = 10;

        public static bool TryCreate(SkateSound sound, int sampleRate, uint seed,
                                     out float[] samples, out string error)
        {
            double duration;
            switch (sound)
            {
                case SkateSound.Rolling: case SkateSound.Grind: case SkateSound.DeckSlide: duration = 1; break;
                case SkateSound.Push: duration = 0.18; break;
                case SkateSound.Ollie: duration = 0.14; break;
                case SkateSound.Landing: duration = 0.28; break;
                case SkateSound.Bail: duration = 0.65; break;
                default: samples = null; error = "Invalid skate sound request."; return false;
            }
            return TryCreate(sound, sampleRate, duration, seed, out samples, out error);
        }

        public static bool TryCreate(SkateSound sound, int sampleRate, double durationSeconds, uint seed,
                                     out float[] samples, out string error)
        {
            samples = null;
            error = null;
            if ((int)sound < 0 || (int)sound > (int)SkateSound.DeckSlide || sampleRate < MinimumSampleRate ||
                sampleRate > MaximumSampleRate || double.IsNaN(durationSeconds) || double.IsInfinity(durationSeconds) ||
                durationSeconds < MinimumDuration || durationSeconds > MaximumDuration)
            { error = "Invalid skate sound request."; return false; }
            try
            {
                var candidate = new float[(int)Math.Round(sampleRate * durationSeconds, MidpointRounding.AwayFromZero)];
                uint random = unchecked(seed ^ (0x9e3779b9u * ((uint)sound + 1)));
                if (random == 0) random = 0x6d2b79f5u;
                if (sound == SkateSound.Rolling || sound == SkateSound.Grind || sound == SkateSound.DeckSlide)
                    Loop(sound, sampleRate, candidate, ref random);
                else Shot(sound, sampleRate, candidate, ref random);
                samples = candidate;
                return true;
            }
            catch (OutOfMemoryException)
            { error = "Not enough memory to create skate sound."; return false; }
        }

        private static void Loop(SkateSound sound, int rate, float[] samples, ref uint random)
        {
            double peak;
            switch (sound)
            {
                case SkateSound.Rolling:
                    Texture(samples, rate, 35, 260, 0.55, ref random);
                    Texture(samples, rate, 180, 2800, 1, ref random);
                    Texture(samples, rate, 2100, 6200, 0.13, ref random);
                    Contacts(samples, rate, 115, 0.20, false, ref random);
                    Rattle(samples, rate, 17, 0.13, ref random);
                    peak = 0.45;
                    break;
                case SkateSound.Grind:
                    Texture(samples, rate, 280, 1500, 0.32, ref random);
                    Texture(samples, rate, 900, 7400, 1.15, ref random);
                    Contacts(samples, rate, 73, 0.48, true, ref random);
                    Rattle(samples, rate, 31, 0.34, ref random);
                    Soften(samples, rate, Math.Min(6500, rate * 0.22));
                    peak = 0.65;
                    break;
                default:
                    Texture(samples, rate, 70, 1150, 0.95, ref random);
                    Texture(samples, rate, 650, 3400, 0.35, ref random);
                    Contacts(samples, rate, 47, 0.60, false, ref random);
                    peak = 0.55;
                    break;
            }
            double sum = 0, weight = 0;
            int fade = Math.Max(1, Math.Min(rate / 500, samples.Length / 8));
            for (int i = 0; i < samples.Length; i++)
            {
                double window = JoinWindow(i, samples.Length, fade);
                samples[i] = (float)(samples[i] * window);
                sum += samples[i]; weight += window;
            }
            for (int i = 0; i < samples.Length; i++)
                samples[i] = (float)(samples[i] - sum / weight * JoinWindow(i, samples.Length, fade));
            Normalise(samples, peak);
        }

        private static double JoinWindow(int index, int length, int fade)
        {
            double x = Math.Max(0, Math.Min(1, Math.Min(index - 2, length - 3 - index) / (double)fade));
            return x * x * (3 - 2 * x);
        }

        private static void Soften(float[] samples, int rate, double cutoff)
        {
            double angle = 2 * Math.PI * cutoff / rate;
            double cosine = Math.Cos(angle), alpha = Math.Sin(angle) / Math.Sqrt(2);
            double divisor = 1 + alpha;
            double b0 = (1 - cosine) / (2 * divisor), b1 = 2 * b0;
            double a1 = -2 * cosine / divisor, a2 = (1 - alpha) / divisor;
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            var input = (float[])samples.Clone();
            for (int pass = 0; pass < 3; pass++)
                for (int i = 0; i < input.Length; i++)
                {
                    double x = input[i];
                    double y = b0 * x + b1 * x1 + b0 * x2 - a1 * y1 - a2 * y2;
                    x2 = x1; x1 = x; y2 = y1; y1 = y;
                    if (pass == 2) samples[i] = (float)y;
                }
        }

        private static void Texture(float[] samples, int rate, double low, double high, double gain, ref uint random)
        {
            var white = new float[samples.Length];
            for (int i = 0; i < white.Length; i++) white[i] = (float)Noise(ref random);
            double a = 1 - Math.Exp(-2 * Math.PI * low / rate);
            double b = 1 - Math.Exp(-2 * Math.PI * Math.Min(high, rate * 0.32) / rate);
            double slow = 0, fast = 0, slow2 = 0, fast2 = 0, envelope = 0;
            double flutter = 1 - Math.Exp(-2 * Math.PI * 23 / rate);
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 0; i < white.Length; i++)
                {
                    slow += a * (white[i] - slow); fast += b * (white[i] - fast);
                    slow2 += a * (slow - slow2); fast2 += b * (fast - fast2);
                    envelope += flutter * (white[(i + white.Length / 3) % white.Length] - envelope);
                    if (pass == 2) samples[i] += (float)(gain * (fast2 - slow2) * (0.8 + 3 * Math.Abs(envelope)));
                }
            }
        }

        private static void Contacts(float[] samples, int rate, double density, double gain, bool metal, ref uint random)
        {
            int count = Math.Max(1, (int)Math.Round(density * samples.Length / rate));
            for (int k = 0; k < count; k++)
            {
                int at = (int)(Next(ref random) % (uint)samples.Length);
                double strength = gain * (0.25 + 0.75 * Math.Abs(Noise(ref random)));
                Friction(samples, rate, at, metal ? 1200 : 200, metal ? 7300 : 2700,
                    metal ? 0.004 : 0.009, strength, true, ref random);
                Ring(samples, rate, at, metal ? 1370 : 185, metal ? 0.009 : 0.012, strength * 0.25, true);
                Ring(samples, rate, at, metal ? 3250 : 510, 0.005, strength * 0.16, true);
            }
        }

        private static void Rattle(float[] samples, int rate, double density, double gain, ref uint random)
        {
            int count = Math.Max(1, (int)Math.Round(density * samples.Length / rate));
            for (int k = 0; k < count; k++)
            {
                int at = (int)(Next(ref random) % (uint)samples.Length);
                double strength = gain * (0.35 + 0.65 * Math.Abs(Noise(ref random)));
                Friction(samples, rate, at, 1100, 6900, 0.002, strength, true, ref random);
                Ring(samples, rate, at, 630, 0.007, strength * 0.6, true);
                Ring(samples, rate, at, 1870, 0.004, strength * 0.3, true);
                Ring(samples, rate, (at + Math.Max(1, rate / 250)) % samples.Length,
                    3110, 0.003, strength * 0.18, true);
            }
        }

        private static void Shot(SkateSound sound, int rate, float[] samples, ref uint random)
        {
            double duration;
            switch (sound)
            {
                case SkateSound.Push: duration = 0.18; break;
                case SkateSound.Ollie: duration = 0.14; break;
                case SkateSound.Landing: duration = 0.28; break;
                default: duration = 0.65; break;
            }
            double scale = (samples.Length - 1) / (rate * duration);
            double tuning = 0.94 + 0.12 * ((Noise(ref random) + 1) * 0.5);
            switch (sound)
            {
                case SkateSound.Push:
                    Friction(samples, rate, 1, 100, 2200, 0.024 * scale, 1.1, false, ref random);
                    Ring(samples, rate, 1, 105 * tuning, 0.014 * scale, 0.09, false);
                    Friction(samples, rate, (int)(rate * 0.025 * scale), 600, 3900,
                        0.018 * scale, 0.24, false, ref random);
                    break;
                case SkateSound.Ollie:
                    Friction(samples, rate, 1, 650, 6300, 0.005 * scale, 0.52, false, ref random);
                    WoodHit(samples, rate, (int)(rate * 0.003 * scale), scale, tuning, 1, ref random);
                    Friction(samples, rate, (int)(rate * 0.010 * scale), 450, 3700,
                        0.015 * scale, 0.30, false, ref random);
                    Ring(samples, rate, (int)(rate * 0.023 * scale), 1870 * tuning, 0.004 * scale, 0.025, false);
                    break;
                case SkateSound.Landing:
                    WheelHit(samples, rate, 1, scale, tuning, 1, ref random);
                    WheelHit(samples, rate, (int)(rate * 0.011 * scale), scale, tuning, 0.72, ref random);
                    WoodHit(samples, rate, (int)(rate * 0.002 * scale), scale, tuning, 0.45, ref random);
                    Clatter(samples, rate, scale, tuning, ref random);
                    break;
                default:
                    WheelHit(samples, rate, 1, scale, tuning, 1, ref random);
                    WoodHit(samples, rate, (int)(rate * 0.038 * scale), scale, tuning, 0.60, ref random);
                    WoodHit(samples, rate, (int)(rate * 0.13 * scale), scale, tuning, 0.34, ref random);
                    WheelHit(samples, rate, (int)(rate * 0.26 * scale), scale, tuning, 0.19, ref random);
                    Friction(samples, rate, (int)(rate * 0.04 * scale), 200, 3200,
                        0.085 * scale, 0.26, false, ref random);
                    Clatter(samples, rate, scale, tuning, ref random);
                    break;
            }
            for (int i = 1; i < samples.Length - 1; i++)
            {
                double progress = i / (double)(samples.Length - 1);
                double tail = Math.Min(1, (1 - progress) / 0.12);
                samples[i] *= (float)(tail * tail * (3 - 2 * tail));
            }
            samples[0] = samples[samples.Length - 1] = 0;
            RemoveDc(samples);
            Normalise(samples, 0.8);
        }

        private static void WoodHit(float[] samples, int rate, int at, double scale, double tuning,
                                    double gain, ref uint random)
        {
            Friction(samples, rate, at, 800, 7600, 0.0035 * scale, gain * 1.1, false, ref random);
            Friction(samples, rate, at, 130, 1700, 0.009 * scale, gain * 0.55, false, ref random);
            Ring(samples, rate, at, 185 * tuning, 0.020 * scale, gain * 0.22, false);
            Ring(samples, rate, at, 487 * tuning, 0.010 * scale, gain * 0.20, false);
            Ring(samples, rate, at, 1093 * tuning, 0.006 * scale, gain * 0.12, false);
            Ring(samples, rate, at, 2311 * tuning, 0.003 * scale, gain * 0.08, false);
        }

        private static void WheelHit(float[] samples, int rate, int at, double scale, double tuning,
                                     double gain, ref uint random)
        {
            Friction(samples, rate, at, 45, 1800, 0.012 * scale, gain * 1.1, false, ref random);
            Friction(samples, rate, at, 1600, 6600, 0.002 * scale, gain * 0.35, false, ref random);
            Ring(samples, rate, at, 82 * tuning, 0.026 * scale, gain * 0.38, false);
            Ring(samples, rate, at, 217 * tuning, 0.012 * scale, gain * 0.16, false);
        }

        private static void Clatter(float[] samples, int rate, double scale, double tuning, ref uint random)
        {
            for (int k = 0; k < 6; k++)
            {
                int at = (int)(rate * (0.018 + k * 0.016 + 0.006 * Math.Abs(Noise(ref random))) * scale);
                double gain = 0.20 * Math.Exp(-k * 0.52);
                Friction(samples, rate, at, 1200, 6700, 0.002 * scale, gain, false, ref random);
                Ring(samples, rate, at, (k % 2 == 0 ? 690 : 1910) * tuning, 0.005 * scale, gain, false);
            }
        }

        private static void Friction(float[] samples, int rate, int at, double low, double high,
                                     double decay, double gain, bool wrap, ref uint random)
        {
            int count = Math.Max(2, (int)(rate * decay * 7));
            if (!wrap) count = Math.Min(samples.Length, count);
            double a = 1 - Math.Exp(-2 * Math.PI * Math.Min(low, rate * 0.2) / rate);
            double b = 1 - Math.Exp(-2 * Math.PI * Math.Min(high, rate * 0.32) / rate);
            double slow = 0, fast = 0, slow2 = 0, fast2 = 0, envelope = gain;
            double fall = Math.Exp(-1 / (rate * decay));
            for (int i = 0; i < count; i++, envelope *= fall)
            {
                int target = at + i;
                if (target >= samples.Length) { if (!wrap) break; target %= samples.Length; }
                double white = Noise(ref random);
                slow += a * (white - slow); fast += b * (white - fast);
                slow2 += a * (slow - slow2); fast2 += b * (fast - fast2);
                samples[target] += (float)((fast2 - slow2) * envelope * Math.Min(1, i / 2.0));
            }
        }

        private static void Ring(float[] samples, int rate, int at, double frequency, double decay, double gain, bool wrap)
        {
            var oscillator = new Oscillator(2 * Math.PI * Math.Min(frequency, rate * 0.32) / rate,
                gain, Math.Exp(-1 / (rate * decay)));
            int count = Math.Max(2, (int)(rate * decay * 7));
            if (!wrap) count = Math.Min(samples.Length, count);
            for (int i = 0; i < count; i++)
            {
                int target = at + i;
                if (target >= samples.Length) { if (!wrap) break; target %= samples.Length; }
                samples[target] += (float)(oscillator.Advance() * Math.Min(1, i / 2.0));
            }
        }

        private static void RemoveDc(float[] samples)
        {
            double sum = 0, weight = 0;
            for (int i = 1; i < samples.Length - 1; i++)
            {
                double value = Math.Sin(Math.PI * i / (samples.Length - 1));
                sum += samples[i];
                weight += value * value;
            }
            double correction = sum / weight;
            for (int i = 1; i < samples.Length - 1; i++)
            {
                double value = Math.Sin(Math.PI * i / (samples.Length - 1));
                samples[i] = (float)(samples[i] - correction * value * value);
            }
        }

        private struct Oscillator
        {
            private readonly double stepSine, stepCosine, weight, decay;
            private double sine;
            private double cosine;

            public Oscillator(double angle, double weight, double decay)
            {
                stepSine = Math.Sin(angle); stepCosine = Math.Cos(angle);
                this.weight = weight; this.decay = decay; sine = 0; cosine = 1;
            }

            public double Advance()
            {
                double previous = sine;
                sine = (sine * stepCosine + cosine * stepSine) * decay;
                cosine = (cosine * stepCosine - previous * stepSine) * decay;
                return sine * weight;
            }
        }

        private static void Normalise(float[] samples, double peak)
        {
            double maximum = 0;
            for (int i = 0; i < samples.Length; i++) maximum = Math.Max(maximum, Math.Abs(samples[i]));
            if (maximum == 0) return;
            double gain = peak / maximum;
            for (int i = 0; i < samples.Length; i++) samples[i] = (float)(samples[i] * gain);
        }
        private static uint Next(ref uint state)
        { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return state; }
        private static double Noise(ref uint state) { return (Next(ref state) >> 8) / 8388608.0 - 1; }
    }
}
