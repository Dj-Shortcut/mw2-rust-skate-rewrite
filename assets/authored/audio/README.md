# Authored sound cues

These seven cues are synthesized from seeded noise, filtered envelopes and
authored oscillator patterns. There are no recordings, downloaded samples,
retail game files or other external inputs. The generated WAV files are
[CC0-1.0](../CC0-1.0.txt); the generator source is Apache-2.0.

Generate with Python 3.10 or later, using only its standard library:

```bash
python3 scripts/generate_authored_audio.py
```

All files use 48 kHz mono signed 16-bit PCM. The generator emits identical files
and a manifest on repeated runs, without timestamps. The manifest lists the
seed, SHA-256, frame count, duration, peak and RMS amplitude of each cue.

| File | Intended event |
| --- | --- |
| `carbine_shot.wav` | Fired carbine round: short crack and low body |
| `reload.wav` | Reload: magazine handling, insertion and action clicks |
| `footstep.wav` | Footstep: low heel impact and granular sole texture |
| `ollie.wav` | Sharp dry wooden tail pop, brief tail/grip scrape and a tiny truck tick |
| `land.wav` | Solid urethane wheel thump, staggered axles, deck flex and fading truck clatter |
| `gather.wav` | Gathering strike: resonant impact and debris texture |
| `ui_click.wav` | Accepted interface or editing action: short soft pluck |

Algorithm 2 rebuilds only the skate cues. Their names, lengths (0.34/0.40 s)
and peak targets (0.50/0.55) remain; the other five cues regenerate unchanged.
These WAVs serve the parked standalone runtime. The existing-Rust client creates
its clips from the C# generators described in [the audio contract](../../../docs/RUST-SKATE-AUDIO.md).
Gameplay integration determines timing and mixed volume; heard quality remains unverified.
