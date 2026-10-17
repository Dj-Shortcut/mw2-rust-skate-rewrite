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
| `ollie.wav` | Skateboard takeoff: deck pop and shoe scrape |
| `land.wav` | Skateboard landing: wheels, deck impact and brief rattle |
| `gather.wav` | Gathering strike: resonant impact and debris texture |
| `ui_click.wav` | Accepted interface or editing action: short soft pluck |

The sounds are original stylized game cues. Gameplay integration determines
when they play and their final volume.
