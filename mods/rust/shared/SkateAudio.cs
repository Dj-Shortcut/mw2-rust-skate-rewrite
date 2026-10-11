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
            var oscillators = new Oscillator[384];
            double peak;
            switch (sound)
            {
                case SkateSound.Rolling:
                    Band(oscillators, 0, 128, rate, samples.Length, 20, 180, 1, 0.3, ref random);
                    Band(oscillators, 128, 256, rate, samples.Length, 120, 1800, 0.7, 0.65, ref random);
                    peak = 0.45;
                    break;
                case SkateSound.Grind:
                    Band(oscillators, 0, 128, rate, samples.Length, 120, 1100, 0.85, 0.15, ref random);
                    Band(oscillators, 128, 256, rate, samples.Length, 650, 7000, 1, 0.1, ref random);
                    peak = 0.65;
                    break;
                default:
                    Band(oscillators, 0, 128, rate, samples.Length, 65, 700, 1, 0.3, ref random);
                    Band(oscillators, 128, 256, rate, samples.Length, 350, 2400, 0.6, 0.6, ref random);
                    peak = 0.55;
                    break;
            }
            for (int i = 1; i <= (samples.Length - 1) / 2; i++)
            {
                double value = 0;
                for (int k = 0; k < oscillators.Length; k++) value += oscillators[k].Advance();
                samples[i] = (float)value;
                samples[samples.Length - i] = -samples[i];
            }
            Normalise(samples, peak);
        }

        private static void Band(Oscillator[] oscillators, int first, int count, int rate, int length,
                                 double minimum, double maximum, double level, double falloff, ref uint random)
        {
            int high = Math.Min((length - 1) / 2,
                Math.Max(1, (int)Math.Floor(Math.Min(maximum, rate * 0.35) * length / rate)));
            int low = Math.Min(high, Math.Max(1, (int)Math.Ceiling(minimum * length / rate)));
            int span = high - low + 1;
            for (int k = 0; k < count; k++)
            {
                int start = low + span * k / count;
                int end = Math.Max(start, low + span * (k + 1) / count - 1);
                int bin = start + (int)(Next(ref random) % (uint)(end - start + 1));
                double noise = Noise(ref random);
                double weight = level * (0.5 + 0.5 * Math.Abs(noise)) * (noise < 0 ? -1 : 1) *
                    Math.Pow(low / (double)bin, falloff) / Math.Sqrt(count);
                oscillators[first + k] = new Oscillator(2 * Math.PI * bin / length, weight, 1);
            }
        }

        private static void Shot(SkateSound sound, int rate, float[] samples, ref uint random)
        {
            double duration, fastCutoff, slowCutoff, noiseGain, noiseDecay, drag;
            var resonators = new Oscillator[3];
            Impact[] impacts;
            double tuning = 0.94 + 0.12 * ((Noise(ref random) + 1) * 0.5);
            switch (sound)
            {
                case SkateSound.Push:
                    duration = 0.18; fastCutoff = 2000; slowCutoff = 150;
                    noiseGain = 0.85; noiseDecay = 0.027; drag = 0;
                    impacts = new[] { new Impact(1, 1) };
                    Resonator(resonators, 0, 140, 0.018, 0.14, tuning, rate, samples.Length, duration);
                    Resonator(resonators, 1, 420, 0.009, 0.08, tuning, rate, samples.Length, duration);
                    Resonator(resonators, 2, 900, 0.005, 0.04, tuning, rate, samples.Length, duration);
                    break;
                case SkateSound.Ollie:
                    duration = 0.14; fastCutoff = 4200; slowCutoff = 250;
                    noiseGain = 0.7; noiseDecay = 0.006; drag = 0;
                    impacts = new[] { new Impact(1, 1) };
                    Resonator(resonators, 0, 180, 0.028, 0.3, tuning, rate, samples.Length, duration);
                    Resonator(resonators, 1, 480, 0.012, 0.24, tuning, rate, samples.Length, duration);
                    Resonator(resonators, 2, 1180, 0.006, 0.16, tuning, rate, samples.Length, duration);
                    break;
                case SkateSound.Landing:
                    duration = 0.28; fastCutoff = 2800; slowCutoff = 80;
                    noiseGain = 0.36; noiseDecay = 0.009; drag = 0;
                    impacts = new[] { new Impact(1, 1), Impact.At(samples.Length, 0.026, 0.72),
                        Impact.At(samples.Length, 0.061, 0.46) };
                    Resonator(resonators, 0, 75, 0.05, 0.75, tuning, rate, samples.Length, duration);
                    Resonator(resonators, 1, 165, 0.025, 0.3, tuning, rate, samples.Length, duration);
                    Resonator(resonators, 2, 430, 0.012, 0.15, tuning, rate, samples.Length, duration);
                    break;
                default:
                    duration = 0.65; fastCutoff = 2500; slowCutoff = 50;
                    noiseGain = 0.7; noiseDecay = 0.02; drag = 0.18;
                    impacts = new[] { new Impact(1, 1), Impact.At(samples.Length, 0.19, 0.7),
                        Impact.At(samples.Length, 0.43, 0.45), Impact.At(samples.Length, 0.69, 0.25) };
                    Resonator(resonators, 0, 60, 0.07, 0.45, tuning, rate, samples.Length, duration);
                    Resonator(resonators, 1, 145, 0.035, 0.28, tuning, rate, samples.Length, duration);
                    Resonator(resonators, 2, 380, 0.02, 0.12, tuning, rate, samples.Length, duration);
                    break;
            }
            double scale = (samples.Length - 1) / (rate * duration);
            double fast = Math.Exp(-2 * Math.PI * Math.Min(fastCutoff, rate * 0.3) / rate);
            double slow = Math.Exp(-2 * Math.PI * slowCutoff / rate);
            double decay = Math.Exp(-1 / (rate * noiseDecay * scale));
            double fastNoise = 0, slowNoise = 0, contact = 0;
            int nextImpact = 0;
            for (int i = 1; i < samples.Length - 1; i++)
            {
                while (nextImpact < impacts.Length && impacts[nextImpact].Index <= i)
                {
                    double strength = impacts[nextImpact++].Strength;
                    contact += strength;
                    for (int k = 0; k < resonators.Length; k++) resonators[k].Excite(strength);
                }
                double progress = i / (double)(samples.Length - 1);
                double white = Noise(ref random);
                fastNoise = fast * fastNoise + (1 - fast) * white;
                slowNoise = slow * slowNoise + (1 - slow) * white;
                double value = noiseGain * (contact + drag * Math.Exp(-5 * progress)) * (fastNoise - slowNoise);
                for (int k = 0; k < resonators.Length; k++) value += resonators[k].Advance();
                double attack = 1 - Math.Exp(-300 * progress);
                double tail = Math.Min(1, (1 - progress) / 0.12);
                samples[i] = (float)(value * attack * tail * tail * (3 - 2 * tail));
                contact *= decay;
            }
            RemoveDc(samples);
            Normalise(samples, 0.8);
        }

        private static void Resonator(Oscillator[] resonators, int index, double frequency, double decay,
                                      double gain, double tuning, int rate, int length, double duration)
        {
            double scale = (length - 1) / (rate * duration);
            resonators[index] = new Oscillator(2 * Math.PI * Math.Min(frequency * tuning, rate * 0.3) / rate,
                gain, Math.Exp(-1 / (rate * decay * scale)));
            resonators[index].Cosine = 0;
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
            public double Cosine;

            public Oscillator(double angle, double weight, double decay)
            {
                stepSine = Math.Sin(angle); stepCosine = Math.Cos(angle);
                this.weight = weight; this.decay = decay; sine = 0; Cosine = 1;
            }

            public void Excite(double strength) { Cosine += strength; }

            public double Advance()
            {
                double previous = sine;
                sine = (sine * stepCosine + Cosine * stepSine) * decay;
                Cosine = (Cosine * stepCosine - previous * stepSine) * decay;
                return sine * weight;
            }
        }

        private struct Impact
        {
            public readonly int Index;
            public readonly double Strength;
            public Impact(int index, double strength) { Index = index; Strength = strength; }
            public static Impact At(int length, double progress, double strength)
            { return new Impact(1 + (int)Math.Round((length - 3) * progress), strength); }
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
