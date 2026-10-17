"""SPDX-License-Identifier: Apache-2.0"""

import argparse
import hashlib
import json
import math
import random
import struct
import wave
from pathlib import Path


SAMPLE_RATE = 48000
SEED = 731031
TAU = math.tau


def blank(seconds):
    return [0.0] * round(seconds * SAMPLE_RATE)


def noise(rng, length, cutoff):
    alpha = 1.0 - math.exp(-TAU * cutoff / SAMPLE_RATE)
    state = 0.0
    result = []
    for _ in range(length):
        state += alpha * (rng.uniform(-1.0, 1.0) - state)
        result.append(state)
    return result


def burst(samples, rng, start, duration, gain, cutoff, decay, attack=0.001):
    count = round(duration * SAMPLE_RATE)
    filtered = noise(rng, count, cutoff)
    offset = round(start * SAMPLE_RATE)
    for index, value in enumerate(filtered):
        target = offset + index
        if target >= len(samples):
            break
        elapsed = index / SAMPLE_RATE
        fade = min(1.0, elapsed / attack)
        end_fade = min(1.0, (duration - elapsed) / 0.003)
        samples[target] += gain * value * fade * end_fade * math.exp(-elapsed / decay)


def tone(samples, start, duration, gain, frequency, decay, end_frequency=None):
    count = round(duration * SAMPLE_RATE)
    offset = round(start * SAMPLE_RATE)
    phase = 0.0
    target_frequency = frequency if end_frequency is None else end_frequency
    for index in range(count):
        target = offset + index
        if target >= len(samples):
            break
        elapsed = index / SAMPLE_RATE
        progress = elapsed / duration
        hz = frequency + (target_frequency - frequency) * progress
        phase += TAU * hz / SAMPLE_RATE
        envelope = min(1.0, elapsed / 0.0015) * math.exp(-elapsed / decay)
        envelope *= min(1.0, (duration - elapsed) / 0.004)
        samples[target] += gain * math.sin(phase) * envelope


def click(samples, rng, start, gain, frequency=1500.0):
    burst(samples, rng, start, 0.05, gain, 7500.0, 0.008)
    tone(samples, start, 0.07, gain * 0.25, frequency, 0.011)
    tone(samples, start, 0.065, gain * 0.15, frequency * 1.63, 0.007)


def carbine_shot(rng):
    samples = blank(0.36)
    burst(samples, rng, 0.0, 0.055, 1.4, 9800.0, 0.008, attack=0.0002)
    burst(samples, rng, 0.001, 0.16, 1.1, 1400.0, 0.026)
    burst(samples, rng, 0.006, 0.30, 0.55, 3400.0, 0.05)
    tone(samples, 0.002, 0.21, 0.64, 140.0, 0.033, 48.0)
    tone(samples, 0.001, 0.09, 0.15, 310.0, 0.018, 120.0)
    click(samples, rng, 0.048, 0.13, 1800.0)
    for start, gain in ((0.056, 0.07), (0.098, 0.035), (0.16, 0.016)):
        burst(samples, rng, start, 0.10, gain, 1800.0, 0.031)
    return samples, 0.78


def reload(rng):
    samples = blank(1.15)
    click(samples, rng, 0.01, 0.48, 980.0)
    burst(samples, rng, 0.035, 0.15, 0.36, 1800.0, 0.061, attack=0.009)
    tone(samples, 0.035, 0.11, 0.12, 190.0, 0.036)
    burst(samples, rng, 0.34, 0.16, 0.48, 2200.0, 0.053, attack=0.015)
    click(samples, rng, 0.49, 0.8, 1150.0)
    tone(samples, 0.49, 0.12, 0.33, 150.0, 0.027)
    burst(samples, rng, 0.78, 0.10, 0.45, 3900.0, 0.04, attack=0.006)
    click(samples, rng, 0.9, 0.77, 2300.0)
    click(samples, rng, 0.936, 0.36, 1670.0)
    tone(samples, 0.9, 0.15, 0.13, 480.0, 0.037)
    return samples, 0.52


def footstep(rng):
    samples = blank(0.25)
    burst(samples, rng, 0.0, 0.10, 0.8, 1250.0, 0.026, attack=0.003)
    burst(samples, rng, 0.025, 0.11, 0.28, 4400.0, 0.043, attack=0.015)
    tone(samples, 0.002, 0.13, 0.5, 92.0, 0.021, 52.0)
    burst(samples, rng, 0.09, 0.10, 0.33, 1800.0, 0.027, attack=0.004)
    tone(samples, 0.09, 0.10, 0.11, 135.0, 0.02, 78.0)
    return samples, 0.32


def ollie(rng):
    samples = blank(0.34)
    burst(samples, rng, 0.0, 0.08, 0.8, 5900.0, 0.011)
    tone(samples, 0.002, 0.13, 0.48, 235.0, 0.025)
    tone(samples, 0.002, 0.11, 0.16, 690.0, 0.016)
    burst(samples, rng, 0.025, 0.18, 0.35, 2400.0, 0.065, attack=0.02)
    click(samples, rng, 0.058, 0.15, 3200.0)
    return samples, 0.5


def land(rng):
    samples = blank(0.40)
    burst(samples, rng, 0.0, 0.14, 0.85, 2300.0, 0.02)
    tone(samples, 0.001, 0.19, 0.53, 135.0, 0.032, 72.0)
    tone(samples, 0.003, 0.13, 0.22, 440.0, 0.017)
    for start, gain in ((0.026, 0.46), (0.052, 0.25), (0.097, 0.12), (0.145, 0.05)):
        click(samples, rng, start, gain, 1450.0)
    burst(samples, rng, 0.03, 0.27, 0.18, 3800.0, 0.08, attack=0.004)
    return samples, 0.55


def gather(rng):
    samples = blank(0.34)
    burst(samples, rng, 0.0, 0.07, 0.73, 7600.0, 0.01)
    tone(samples, 0.001, 0.23, 0.50, 275.0, 0.039)
    tone(samples, 0.001, 0.16, 0.20, 740.0, 0.03)
    tone(samples, 0.001, 0.12, 0.11, 1120.0, 0.023)
    burst(samples, rng, 0.018, 0.24, 0.26, 2400.0, 0.066, attack=0.005)
    return samples, 0.50


def ui_click(rng):
    samples = blank(0.085)
    tone(samples, 0.001, 0.07, 0.48, 1150.0, 0.012)
    tone(samples, 0.001, 0.06, 0.18, 1725.0, 0.009)
    burst(samples, rng, 0.0, 0.025, 0.13, 2400.0, 0.006)
    return samples, 0.22


def pcm(samples, peak):
    average = sum(samples) / len(samples)
    prepared = []
    fade_frames = round(0.001 * SAMPLE_RATE)
    for index, value in enumerate(samples):
        fade = min(1.0, index / fade_frames, (len(samples) - 1 - index) / fade_frames)
        prepared.append((value - average) * fade)
    largest = max(abs(value) for value in prepared)
    scale = peak * 32767.0 / largest if largest else 0.0
    encoded = [round(value * scale) for value in prepared]
    return struct.pack(f"<{len(encoded)}h", *encoded), encoded


def generate(output):
    output.mkdir(parents=True, exist_ok=True)
    cues = (carbine_shot, reload, footstep, ollie, land, gather, ui_click)
    records = []
    for index, synthesize in enumerate(cues):
        seed = SEED + index * 101
        samples, peak = synthesize(random.Random(seed))
        data, values = pcm(samples, peak)
        filename = f"{synthesize.__name__}.wav"
        path = output / filename
        with wave.open(str(path), "wb") as sound:
            sound.setnchannels(1)
            sound.setsampwidth(2)
            sound.setframerate(SAMPLE_RATE)
            sound.writeframes(data)
        records.append({
            "file": filename,
            "seed": seed,
            "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            "frames": len(samples),
            "sample_rate_hz": SAMPLE_RATE,
            "channels": 1,
            "bits_per_sample": 16,
            "duration_seconds": len(samples) / SAMPLE_RATE,
            "peak_amplitude": round(max(abs(value) for value in values) / 32768.0, 6),
            "rms_amplitude": round(math.sqrt(sum(value * value for value in values) / len(values)) / 32768.0, 6),
        })
    manifest = {
        "format_version": 1,
        "generator": "scripts/generate_authored_audio.py",
        "algorithm_version": 1,
        "base_seed": SEED,
        "license": "CC0-1.0",
        "encoding": "signed 16-bit little-endian PCM WAV",
        "external_inputs": [],
        "sounds": records,
    }
    (output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description="Synthesize the game's original short sound cues.")
    parser.add_argument("--output", type=Path, default=Path("assets/authored/audio"))
    arguments = parser.parse_args()
    generate(arguments.output)


if __name__ == "__main__":
    main()
