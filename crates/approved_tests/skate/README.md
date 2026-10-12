# Approved skate scenarios

## Server source installation

The owner approved `skate_server_plugin_install` by name on 11 October 2026.
`SkateServerPluginInstallScenario.py` executes the actual installer with real
pinned public downloads in an otherwise empty Linux container. It checks fresh
installation, source bytes/ownership/mode, previous-source backup, refusals and
temporary cleanup after real file-operation failures, including refusal to
read a root-only prior source through the unprivileged worker. It requires container
root and refuses an existing Rust installation or `rust` identity.

The CI job **Skate plugin source installation** supplies Ubuntu 24.04 and GNU
tools. Inside an equivalent disposable container with Bash, curl/CA certificates,
coreutils, passwd/util-linux and Python 3:

```sh
python3 crates/approved_tests/skate/SkateServerPluginInstallScenario.py
```

This scenario never starts Rust/Oxide, the Rust client or Shadow, opens game ports
or accesses the owner's server. Synthetic Oxide log lines also exercise the
selected-version verdict, stale/partial/copied records, log rotation/truncation,
failure after a long fresh tail, guarded rollback, a real 90-second timeout and
INT/TERM after source publication. Fresh log inodes are conservatively baselined
at first-observed EOF. No fixture establishes native Oxide compilation/loading
or filesystem-watcher behavior. Native plugin compile/load, patches, placement
and riding remain separate acceptance. INT/TERM cancellation is checked during
a real kernel-lease-blocked backup,
including caller status, worker exit before lease release and unchanged source
afterward. Power loss/SIGKILL are not tested.

## Recorded gestures

The three scenarios requested by the owner through [issue #337](https://github.com/Dj-Shortcut/mw2-rust-rust-rewrite/issues/337#issuecomment-6098063930) replay the exact recorded stick positions and timestamps. Each position is held until the next timestamp; long holds are subdivided at the module's maximum step without interpolating new positions.

- `skate_air_pullback_ollie`: a pull back and pop in the air produces one Ollie, rather than being consumed as a late flip.
- `skate_ground_diagonal_kickflip`: the recorded diagonal flick produces one Ollie with one Kickflip.
- `skate_ground_roll_ollie_no_manual`: the recorded roll and recoil produce one plain Ollie and no manual.

Build the shared library using genuine dedicated-server framework references, then run its actual DLL on .NET 8:

```sh
dotnet build mods/rust/shared/SkateMotion.csproj -p:RustManagedPath=/absolute/path/to/managed
dotnet run --project crates/approved_tests/skate/SkateGestureScenarios.csproj
```

An alternate built DLL can be supplied with `-p:SkateLibraryPath=/absolute/path/to/SkateMotion.dll`. Outputs stay in ignored `context/`.

These scenarios check gesture recognition and its compositional/legacy outputs. They do not start the Rust client or server and do not establish controller feel, animation or a landed native trick.

## Ride physics

The owner approved three scenarios by name on 11 October 2026 for [Task 5](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/issues/337#issuecomment-6106747983):

- `skate_transition_pumping`: descent-to-bottom crouch/release timing, slope-dependent energy capped per traversal, a six-metre flat crossing, spam refusal, rearm, timeout, loss of support and physical reversal.
- `skate_drop_in_and_launch`: uphill/downhill gravity, stall/reversal, heading-preserving tangent velocity, mechanical energy, inherited ramp velocity and vertical pop.
- `skate_surface_landing`: straight/backwards/sideways roll retention, full velocity projected against the surface for bail impact, and downhill fall converted to roll without creating energy.

```sh
dotnet build mods/rust/shared/SkateMotion.csproj -p:RustManagedPath=/absolute/path/to/managed
dotnet run --project crates/approved_tests/skate/SkateRidePhysicsScenarios.csproj -p:SkateLibraryPath=/absolute/path/to/SkateMotion.dll
```

The runner uses the actual compiled shared DLL, prints its path/SHA-256 and compares independent analytical fixtures. Outputs stay in ignored `context/`. Its ideal halfpipe-energy fixture brackets the authored deck after one/two full pumps at six m/s, without drag or collisions; it does not prove a first-pump limit for every entry speed or native rideability. These tests never start the Rust client, server or Shadow PC. See the [API and numbered adapter review](../../../docs/RUST-SKATE-RIDE-PHYSICS.md) for integration work still missing.

## Park layouts

The owner approved two layout scenarios by name for [Task 4](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/issues/337#issuecomment-6099499139):

- `skate_park_piece_layouts`: the eight pieces' block positions, orientations, native materials and adjoining nominal square-floor edges, including the complete halfpipe's flat, transitions, decks and railings.
- `skate_park_anchor_yaw_and_snap`: translated and rotated layouts preserve the snapped anchor supplied by ordinary native construction, including anchors away from the world origin. The foundation extension supplies abstract construction bounds and independently checks every tilted floor corner above the support, without changing the authored ledge/deck heights.

Build the actual server plugin using genuine dedicated-server references, then execute its engine-free helpers on .NET 8:

```sh
dotnet build mods/rust/plugins/ShortcutSkate.csproj -p:RustManagedPath=/absolute/path/to/managed
dotnet run --project crates/approved_tests/skate/SkateParkLayoutScenarios.csproj
```

Supply `-p:ActualPluginPath=/absolute/path/to/ShortcutSkate.dll` for another built DLL. Outputs stay in ignored `context/`; the runner prints its loaded DLL path and SHA-256.

These checks use the layout helpers in that DLL, with independent expected positions and seams. They do not verify native floor dimensions/colliders, building privilege, stability, save/load callbacks, demolition or gameplay, and they do not start the Rust client or server.

## Park gesture selection

The owner approved `skate_park_gesture_selection` by name on 11 October 2026. It exercises the actual plugin's pure selection policy: halfpipe-first cycling, a configured order, cancellation, inclusive two-minute expiry, renewal, failed-placement retention, successful consumption and silent refusal for non-admins. Invalid states, clocks, orders and gesture-name catalog matches are checked atomically. It distinguishes an unknown gesture from invalid names, unavailable catalogs and ambiguous aliases, preserves the original lookup overload, and checks one-line catalog names with English/convar fallback, native ordering and case-insensitive deduplication.

```sh
dotnet run --project crates/approved_tests/skate/SkateParkGestureScenarios.csproj -p:ActualPluginPath=/absolute/path/to/ShortcutSkate.dll
```

Build that DLL using the genuine references as above. The runner prints its loaded DLL path and SHA-256; outputs stay in ignored `context/`. This scenario does not invoke a native gesture, install a Harmony patch, send chat or place a park. Those runtime behaviors remain owner validation.

## Authored audio

The owner approved `skate_audio_surfaces_and_events` by name on 11 October 2026. It covers the
seven generated wheel/truck/deck and event profiles, preserving the original six enum values,
both overloads and default durations. It checks quantized lengths, finite/nonzero unclipped
samples, DC, exact seed reproduction, different seeds, periodic seam slopes, silent/decaying
one-shots, contact-profile roughness and invalid requests. Rates include 8,000 and 192,000 Hz;
odd/even counts and the ten-second maximum are covered. The shared DLL supplies every sample.

```sh
dotnet run --project crates/approved_tests/skate/SkateAudioScenarios.csproj -p:SkateLibraryPath=/absolute/path/to/SkateMotion.dll
```

An optional single runner argument names a local directory for seven original WAV previews.
Keep exports in ignored `context/`. These are waveform/source checks; the runner uses no Rust
client/server or Shadow PC, and does not test native sound mixing, audibility or listener preference.
See the [audio contract](../../../docs/RUST-SKATE-AUDIO.md).

## Rider secondary motion

The owner approved `skate_rider_secondary_motion` by name on 11 October 2026. It checks exact
target ankles/toes, measured limb lengths and spans, all 18 post-IK displacement bounds,
pelvis/shoulder/hand lag and overshoot, deterministic histories, unequal rigs, near reach,
world translation/rotation, isolated reset boundaries, variable dt, inverted handplants and
all 14 grab targets across regular/goofy and switch. Malformed rig/pose/state/history and dt
refusals are atomic; warmed success/refusal loops check allocation on the executing runtime.

Build the genuine-reference shared DLL first, then run the approved scenario:

```sh
dotnet build mods/rust/shared/SkateMotion.csproj -p:RustManagedPath=/absolute/path/to/managed
dotnet run --project crates/approved_tests/skate/SkateRiderMotionScenarios.csproj -p:SkateLibraryPath=/absolute/path/to/SkateMotion.dll -- EXPECTED_SHA256
```

The optional single argument verifies the loaded DLL's SHA-256; its path/hash are always printed.
Outputs stay in ignored `context/`. The integrated genuine net48 DLL passes 180,614 checks on
Mac net8, with zero measured bytes in 20,000-call warmed success/refusal loops. These are
secondary-motion geometry checks. The runner starts no Rust client/server or Shadow PC and
does not establish hand/surface contact, native rendering, adapter integration or IL2CPP cost.
See the [API and integration limits](../../../mods/rust/shared/README.md#rider-secondary-motion-api).

## Direct park placement requests

The existing `skate_park_anchor_yaw_and_snap` also exercises the server's direct-placement request helper: aim versus explicit anchors, invariant numeric parsing, finite coordinate/yaw bounds, yaw normalization and atomic refusal outputs across cultures. It invokes the actual public helper by its exact reflected signature and requires that helper in `ShortcutSkate.dll`. The shared `SkateMotion.dll` contains geometry helpers only, so its layout run omits the server-request extension. These scalar checks do not execute native aim rays, building hooks, placement or gameplay.
