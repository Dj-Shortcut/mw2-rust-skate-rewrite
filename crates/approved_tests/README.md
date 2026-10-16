# approved_tests

APPROVED TEST POLICY

Only explicitly owner-approved end-to-end scenarios may exist in this crate.
Agents may create temporary tests and probes during development, but those
must be deleted before the final squash/push unless specifically approved. A
useful test is not automatically an approved test.

A new permanent scenario is added here only after the owner has approved that
scenario by name. The same holds for a unit test inside this crate: being
useful does not make it approved. `make publish-check` refuses `#[test]`,
`#[cfg(test)]`, `mod tests` and `tests/` directories anywhere else in the
workspace.

## Approved scenarios

| name | what it does |
| --- | --- |
| `heavy_gameplay_lifecycle` | cold load `mp_overgrown` with 16 players, three input scenes, production disconnect, a watched menu, a second map with three scenes, production quit |
| `skate_air_pullback_ollie` | owner-recorded airborne pull-back and forward flick produce one Ollie |
| `skate_ground_diagonal_kickflip` | owner-recorded grounded diagonal flick produces one Ollie with Kickflip |
| `skate_ground_roll_ollie_no_manual` | owner-recorded grounded rim roll and recoil produce one Ollie and no manual |
| `skate_late_flips_and_body_varials` | airborne late composition without a second pop, left-stick full-circle recognition, separate rider rotation and takeoff-end-aware cached landing names |
| `skate_transition_pumping` | travel-relative descent/bottom timing, bounded pump energy per traversal, reset and spam refusal |
| `skate_drop_in_and_launch` | slope gravity, reversal, full tangent drop-in/launch velocity and explicit vertical pop |
| `skate_surface_landing` | board-yaw roll retention, full surface-normal impact bail and downhill fall-to-roll conversion |
| `skate_server_movement_policy` | complete finite batch paths, horizontal cap, fresh authorization guards and independence from diagnostic airtime/support/rearm history |
| `skate_park_piece_layouts` | original eight layouts plus mini/large quarterpipe poses, profiles, cell masks, bounds, reservations and adjoining edges |
| `skate_park_anchor_yaw_and_snap` | translate and rotate each layout from an existing native snapped building anchor |
| `skate_park_gesture_selection` | admin-only configurable park-piece cycling, cancel, expiry and retained pending selection |
| `skate_park_build_items` | ten exact item identities with stable original IDs/markers, single verified debit and complete paid-group recovery eligibility; invalid and historical free groups refuse |
| `skate_audio_surfaces_and_events` | authored wheel, truck/deck contact and event PCM: duration, peak/DC, determinism, loop seams and invalid requests |
| `skate_rider_secondary_motion` | planted ankles and toes, exact limb lengths, bounded secondary pose motion, deterministic steps and atomic invalid-input rejection |
| `skate_server_plugin_install` | actual source installation, replacement/backup, guarded refusal and cleanup after real Linux file-operation failures |
| `skate_trick_grind_names` | appended grind vocabulary, cached directional names, scoring transitions, retained stance, Darkslide half-flip catch/carry and atomic invalid-state refusal |

The owner approved `skate_transition_pumping`, `skate_drop_in_and_launch` and `skate_surface_landing` by name on 11 October 2026 for [Task 5](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/issues/337#issuecomment-6106747983). They execute the actual shared C# library. Native adapter integration, contact sensing and ride feel remain separate validation.

The owner approved `skate_server_movement_policy` by name on 12 October 2026 in the continuation chat for [the owner regression report](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/issues/337#issuecomment-6117276716). It executes actual compiled plugin helpers without Rust/Oxide, client or Shadow; native batch/permission flags, patch execution and movement acceptance remain separate validation.

The three recorded-gesture C# skate scenarios are specified in [issue #337](https://github.com/Dj-Shortcut/mw2-rust-rust-rewrite/issues/337#issuecomment-6098063930). Their separate runner and exact trace data live in [skate/](skate/README.md); they do not start the parked standalone runtime.

The owner approved `skate_late_flips_and_body_varials` by name on 12 October 2026 in the continuation chat for [round 2 Task 3](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/issues/337#issuecomment-6109434907). It executes the actual shared C# DLL; native input, catching, visual poses and controller feel remain separate validation.

The two park-layout scenarios were approved by name by the owner for [Task 4](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/issues/337#issuecomment-6099499139). They execute the actual server plugin's engine-free layout helpers; native placement, save/load and demolition remain separate verification. The owner subsequently authorized adding the complete halfpipe to the same two scenarios.

The owner approved `skate_audio_surfaces_and_events` by name on 11 October 2026 in the handover chat for Task 8. It executes the actual shared C# DLL; waveform properties do not establish native playback or perceived sound quality.

The owner approved `skate_park_gesture_selection` by name on 11 October 2026 for [Task 6](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/issues/337#issuecomment-6106613104). It executes the plugin's pure selection policy; native gesture dispatch and admin chat remain separate verification.

The owner approved `skate_park_build_items` by name on 12 October 2026 in the continuation chat. It executes the actual shared/plugin DLL's item identity, verified-debit and recovery predicates. Native item consumption/refund, save/reload, hammer removal and both building frontends remain separate acceptance. No Rust/client/server or Shadow starts.

The owner approved `skate_rider_secondary_motion` by name on 11 October 2026 in the continuation chat for [the rider-motion handover](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/issues/337#issuecomment-6108821492). It executes the actual engine-free shared C# DLL. Native rig integration, hand contact, rendering and visual quality remain separate verification.

The owner approved `skate_server_plugin_install` by name on 11 October 2026 in the continuation chat for [PR #361](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/pull/361). It runs the actual Bash installer and genuine pinned public source in a disposable Linux container. It never starts Rust/Oxide or accesses a live server; source installation is separate from plugin loading and gameplay.

The owner approved `skate_trick_grind_names` by name on 12 October 2026 in the continuation chat for [round 2 Task 2](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/issues/337#issuecomment-6109434907). It executes the actual shared C# DLL; contact classification, deck poses and native gameplay are separate validation.

The scenario's steps are in `src/scenarios/heavy_gameplay_lifecycle.rs` and
nowhere else: maps, seed rule, scenes, positions, angles, weapons, durations
and input. To pin a scene to an owner-chosen position, replace its `Place`
there; the runner does not change.

The final `quit` phase sends `finish_run`, which uses the normal process-exit
boundary after the scenario's pending screenshots finish writing. Interactive
`quit` intentionally has only a short write grace period and can leave a
scenario-owned PNG incomplete under load.

## Running

```sh
make approved                                  # cold cache, fresh seed
make approved ARGS='--seed 1234'               # fixed seed
make approved ARGS='--replay iw4l-artifacts/approved-tests/<run>/run.json'
make approved ARGS='--cache shared'            # reuse the repo cache (not cold)
```

The runner builds nothing; `make approved` builds `target/play/iw4l` first.
Each run owns one directory, `iw4l-artifacts/approved-tests/<run-id>/`. The
game is started with that directory as its working directory, so everything
IW4L writes (`iw4l-artifacts/cache`, logs, perf run, dumps, screenshots) lands
under it. On Windows the launcher enters the folder holding its executable, so
the runner hard-links (or copies) the binary into the run directory and starts
that one. With `--cache cold` that cache starts empty; with `--cache shared`
it is a symlink to the repository's cache.

A seed resolves to maps and spawn picks once. `run.json` keeps the resolved
set — the positions and angles the game actually used — and `--replay` runs
that set again rather than the seed, so a change in catalog order or spawn
selection cannot move a replayed scene.

## What a run leaves

- `run.json` — build, platform, render, content, maps, seed, the resolved
  scenes and the exact command script, cache mode, bot target and actual,
  lifecycle timestamps, phases with start/end or a failure reason, the GSC
  program each map installed (or the refusal that stopped it), liveness
  assertions, and paths to the large artifacts.
- `00_cold_load.png`, `01_overgrown_gameplay.{png,dump.txt}`,
  `02_after_disconnect.{png,dump.txt}`, `03_second_map_gameplay.{png,dump.txt}`
  when the run got that far.
- `child_stdout.txt`, `child_stderr.txt`, and the game's own
  `iw4l-artifacts/logs/latest.log` and `iw4l-artifacts/runs/<id>/` perf run.

Assertions are liveness only: the map loaded, its GSC program installed, the
players exist, simulation advanced, the authority applied the scene's movement
commands to the local player, the player covered a path of at least 32 units or
died, the lifecycle boundaries happened in order, no script fault ended the
process, and `quit` ended the process on its own.

Every match runs a GSC program, so the game announces it on stdout the way it
announces lifecycle boundaries: `gsc: installed map=… gametype=… fingerprint=…
modules=… functions=… natives=… entries=…` when the world is published, or
`gsc: refused map=… stage=compile|install|entry fault="…"` when the match load
stopped at the script. `run.json` keeps both under `gsc.a` / `gsc.b` and asserts
`a.gsc_installed`, `b.gsc_installed` and `gsc.no_execution_fault`. A phase that
fails after a refusal carries the refusal as its `failure_reason`, and a script
fault that ends the process (`GSC execution failed: …` in `child_stderr.txt`)
becomes the run's `failure`.

Movement is three facts, not one. `mark` reports the local player's input
receipt — commands the authority applied, how many of them carried a move, and
the summed length of every applied move — so a scene that turns while it walks
is judged by the path it walked, not by where it ended up. Each scene records an
`outcome`: `moved`, `died`, `died_before_input`, `held_in_place` (commands
applied, alive, no path: collision or a frozen player) or `input_not_applied`
(the break the scenario exists to catch). There
is no frame-time assertion. A run the controller had to kill is a failure,
never a quit.
