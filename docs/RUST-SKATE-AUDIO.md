# Authored skateboard audio

The Rust client caches seeded mono floats from `SkateAudio` and `SkateFoley` during setup.
It does **not** load the WAV pack. [EA's skate. is the owner's reference](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/issues/408);
these are original procedural sounds, with no recordings, commercial presets or game audio.
C# source/generated waveforms follow Apache-2.0; the separate authored WAV pack is CC0-1.0.

| Generator sound | Contact/event and synthesis | Default seconds | Individual peak |
| --- | --- | --- | --- |
| Rolling | Granular urethane friction, low wheel load and irregular, dry truck rattle | 1 | 0.45 |
| Grind | Broadband metal rasp, stick-slip catches and short inharmonic truck chatter | 1 | 0.65 |
| DeckSlide | Lower dusty wooden scrape, uneven friction grains and short deck modes | 1 | 0.55 |
| Push | Rubber sole contact followed by a brief shoe scuff | 0.18 | 0.80 |
| Ollie | Sharp wooden tail pop, short tail/grip scrape and a tiny hardware tick | 0.14 | 0.80 |
| Landing | Solid wheel thump, two axles 11 ms apart, deck flex and fading truck clatter | 0.28 | 0.80 |
| Bail | Irregular board/wheel impacts followed by friction drag and loose hardware | 0.65 | 0.80 |
| Foley.Hollow | Wooden ramp rolling: dense soft friction and briefly excited, detuned panel knocks | 2 | 0.50 |

The client keeps its current playback contract: ground roll pitch 0.75–1.30, wood ×0.9;
metal/deck pitch 0.90–1.25, wood layer 0.85–1.15. Speed changes gain and the pitch-scaled
grain/rattle cadence. Wood layers Hollow under Rolling; hard ground uses Rolling alone.
Surface selection is the existing coarse layer raycast, not native material recognition.
Other Foley events, dirt and airborne wheel loops retain their synthesis and playback.
No `SkateSfx`, counters, crossfades, clip ownership, release pins or server changes.
`SkateSound` IDs 0–6, both `TryCreate` overloads and default durations remain stable.
Accept 8,000–192,000 Hz, 0.02–10 seconds and any uint seed; length is round(rate × seconds),
away from zero. Invalid requests return false/null samples with an English error.
Arrays belong to the caller; equal requests reproduce float samples on the same runtime.
Cross-platform math is not promised byte-identical. Generate/cache only during setup.
Shared loops use warmed filtered noise, wrapped friction grains and damped modes.
Wrapped contacts accumulate their full seven-decay envelope modulo the buffer,
including multiple revolutions for short loops; one-shots stop at the buffer end.
A smooth join window of at most 2 ms per edge gives silent, equal-slope boundaries;
weighted DC removal preserves them. The fixed one-second pattern still repeats.
Metal has an additional low-pass stage to reduce the high-frequency tail when pitched.
One-shots have silent endpoints, damped fixed-frequency modes and endpoint-zero DC removal.
Peak normalization retains existing limits; combined sources still require mix-level checks.

`scripts/generate_authored_audio.py` regenerates [the seven-file WAV pack](../assets/authored/audio/README.md).
That generator, its manifest and all WAVs remain unchanged: this pack serves the
parked standalone runtime and is separate from the existing-Rust client synthesis.

The [existing approved audio scenario](../crates/approved_tests/skate/README.md#authored-audio)
checks the actual shared DLL and actual Foley source: 4,516 checks / 588 generated clips.
Genuine-reference C#7.3/net48 shared and approved runner: zero warnings/errors.
The earlier full 34-source cached-interop/.NET6 client compile passed; it was not
repeated after the bounded wrapped-contact correction.
It covers durations, finite/peak/DC properties, seeds, seams, decay, contact separation and
source energy above Nyquist/max-pitch below 1% at 44.1 kHz. This measures bandwidth risk.
Short-loop contacts equal complete longer events folded into 20–30 ms buffers at
8,000, 44,100 and 192,000 Hz; non-wrapped contacts retain their endpoint truncation.
Asset and publication gates pass; previews stay in ignored `context/`.

Native sample upload, mixing, surface detection, speed/impact/event timing, teardown,
performance, repeated-loop artifacts and heard quality versus skate. remain unverified.
No native client/server/Shadow session, deployment or merge is part of this source change.
