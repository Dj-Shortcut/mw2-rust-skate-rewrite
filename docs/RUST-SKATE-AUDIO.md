# Authored skateboard audio

`SkateAudio` generates original seeded mono PCM; `SkateSfx` caches it as engine clips.
No audio asset download, game-clip inventory or commercial sample/preset is involved.
The source and generated original waveforms follow the project's Apache-2.0 licence
recorded in [NOTICE](../NOTICE).

| Sound | Contact/event | Default seconds | Playback |
| --- | --- | --- | --- |
| Rolling | Wheel/body rumble and textured ground contact | 1 | Loop while rolling on Ground |
| Grind | Truck-contact metal rasp/resonance | 1 | Loop for truck grinds |
| DeckSlide | Lower wooden deck contact/scrape | 1 | Loop for boardslide, lipslide, noseslide, tailslide |
| Push | Shoe scrape/contact | 0.18 | Push counter |
| Ollie | Short wooden pop/click | 0.14 | Pop counter, including nollie |
| Landing | Staggered wheel/deck impacts | 0.28 | Land counter, scaled by normal impact |
| Bail | Repeated impacts and drag | 0.65 | Bail counter |

Truck/deck profiles identify the board part in contact, not the rail's material.
There is no native surface-material detection. Existing enum values 0–5 and both
`TryCreate` overloads remain; `DeckSlide` is appended as value 6.

Requests accept 8,000–192,000 Hz, 0.02–10 seconds and any uint seed. Length is
round(rate × seconds), away from zero. Invalid requests return false with null samples
and an English error. Returned arrays belong to the caller and are separate allocations.
Identical requests reproduce the same float samples on the same runtime; different
platform math implementations are not promised byte-identical waveforms.

Integer-bin periodic oscillators and mirrored float pairs give DC-free loops with
continuous discrete slopes at the wrap. Their period is length/rate; an endpoint is
not duplicated. Contact texture and resonant bands differ across the three loops.
The loop frequency ceiling reserves headroom for the adapter's maximum grind pitch
of 1.25. One-shots use filtered noise and fixed damped resonators, staggered impacts,
silent endpoints and endpoint-zero DC removal. Linear peak normalization bounds
individual PCM. That is not a guarantee that multiple mixed sources cannot clip.

The adapter creates seven clips once during setup and checks sample-upload success.
Roll, metal and deck loops run as separate initially muted sources. Independent
time-based gains crossfade contact changes; no clip is generated or swapped in the
frame/physics step. Speed controls bounded pitch/volume. Off state fades loops and
syncs counters; a new bail suppresses a concurrent landing/pop/push sound. Failure
and rig teardown stop and destroy only owned audio resources.

## Verification and remaining gates

The owner-approved [audio scenario](../crates/approved_tests/skate/README.md#authored-audio)
consumes the actual shared DLL built with genuine framework references. It checks
duration, finite/peak/DC properties, seed reproduction, periodic seam slopes,
one-shot endpoints/decay, contact-profile separation and rejected inputs. Optional
local WAV exports preview those actual generated samples.

- **Verified:** genuine-reference C# 7.3/net48 shared builds for both cached reference sets
  pass with zero warnings/errors. The actual built DLL passes 4,069 approved audio checks
  across 550 generated clips and retains 11,683 approved ride-physics checks. Independent
  synthesis/adapter/source reviews and the public-tree check pass.
- **Compiles but unverified:** the shared waveform generator's behavior in the native host.
- **Still missing:** a complete changed-client build and native playback/quality/lifecycle
  validation. The approved runner does not execute `SkateSfx` or the engine.

Waveform checks do not prove that the engine accepts or mixes the clips, that output
is audible, or that the sound resembles a skateboard to the listener. Compilation
of the complete changed client requires its generated IL2CPP references, unavailable
in this Mac checkout. No Rust client, Shadow PC or live-server commands were run by
Codex for these changes. Native sample upload, contact/event timing, mixed levels,
heard quality, teardown and allocation/performance during play remain unverified.
The owner's launcher pin is unchanged.
