# Approved skate scenarios

## Server source installation

The owner approved `skate_server_plugin_install` by name on 11 October 2026.
`SkateServerPluginInstallScenario.py` executes the actual installer with real
pinned public downloads in an otherwise empty Linux container. It checks fresh
installation, source bytes/ownership/mode, previous-source backup, refusals and
temporary cleanup after real file-operation failures, including refusal to
read a root-only prior source through the unprivileged worker. It requires container
root and refuses an existing Rust installation or `rust` identity.

The reviewed delivery pins ShortcutSkate **0.3.11**; the fixture supplies matching
new monitored load records and ignores an older 0.3.10 record. Its prior-source
fixture and exact backup/rollback checks remain unchanged. Deferred mode checks
backup/source/cleanup without logs. The exact checksum-verified one-liner in
[LINUX.md](../../../mods/rust/server/LINUX.md) installs deferred source then invokes
a private service-command stand-in; this verifies command ordering/failure, not
systemd or native startup. Native load verification and restoration after deferred
installation remain owner steps.

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

## Server movement policy

The owner approved `skate_server_movement_policy` by name on 12 October 2026. It executes
the actual plugin's pure helpers: every segment's coordinate/path validation, total XZ
distance, cap/tick bounds, deterministic accumulation, atomic refusals, all fresh filter
guards and player eligibility. Existing compiled Bounds/Rearm helpers reproduce the
two-second airtime refusal and unsupported/repeated-reset starvation; the fresh policy
accepts the same in-cap paths independently of that diagnostic history.
The same approved scenario separately checks trusted fly authorization, full absolute Y
budget/delta boundaries, world-spanning and out-and-back refusals, grant/revocation inputs,
steep tangent pop, short-step lip lift and long capped falls. Untrusted movement never
gets the fly exception. These helpers do not attest a board or run native permission changes.

```sh
dotnet build mods/rust/plugins/ShortcutSkate.csproj -p:RustManagedPath=/absolute/path/to/managed
dotnet build mods/rust/shared/SkateMotion.csproj -p:RustManagedPath=/absolute/path/to/managed
dotnet run --project crates/approved_tests/skate/SkateServerMovementPolicyScenarios.csproj -p:ActualPluginPath=/absolute/path/to/ShortcutSkate.dll -p:SkateLibraryPath=/absolute/path/to/SkateMotion.dll -- EXPECTED_PLUGIN_SHA256 EXPECTED_SHARED_SHA256
```

The optional arguments pin the actual plugin/shared DLL hashes; paths/hashes are always printed.
The same scenario checks authored float32 cap-boundary endpoints against the actual
shared limiter and unchanged server policy, including mirrored travel, large coordinates,
lower caps, full out-and-back paths, float step boundaries and atomic rejection.
These are separate float multiply/add endpoints, not captured native motion or timing.
Warmed helper loops measure allocation on the executing .NET runtime. Guard fixtures
exercise production boolean policy, rather than constructing fake native players.
No Rust/Oxide process, client, Shadow or live server starts. Real cache data, authority
flags, Harmony invocation, native noclip and correction-free gameplay remain unverified.

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

## Late flips and body varials

The owner approved `skate_late_flips_and_body_varials` by name on 12 October 2026.
The actual-DLL scenario checks second airborne held flips/shoves without another pop,
existing side AirFlick/old constructors, complete left-stick circles and incomplete,
interrupted, reversed, stale or discontinuous paths. It covers mirrored input, one body
event per flight, support rearming, body/deck angle independence, catching/stance/score,
cached names, bail/reset and atomic invalid input/state refusals.

The same approved scenario checks explicit takeoff `tailLeads` naming across Ollie/Nollie,
regular/Fakie/Switch, signed snapped rotations, late/body/grab modifiers and plants.
It verifies takeoff latching, later flag changes, old constructor behavior, resets,
atomic invalid-state refusals and cached-name allocation. Input directions are normalized;
this is not a native regular/goofy controller or physical-pop/flip presentation test.

```sh
dotnet run --project crates/approved_tests/skate/SkateLateFlipsBodyVarialsScenarios.csproj -p:SkateLibraryPath=/absolute/path/to/SkateMotion.dll -- EXPECTED_SHA256
```

The optional argument pins the loaded DLL hash; path/hash are always printed. Run the
three existing recorded gesture scenarios separately. No Rust client/server or Shadow
starts; native input bindings, animation/contact, IL2CPP cost and controller feel remain
unverified. See the [API](../../../mods/rust/shared/README.md#late-flips-and-rider-only-body-varials).

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

- `skate_park_piece_layouts`: the twelve pieces' block positions, orientations, native materials and adjoining nominal square-floor edges, including the complete halfpipe's flat, transitions, decks and railings, plus nominal authored-asset pivot offsets. Appended mini/large quarterpipe cases check literal cell masks/rotations, quarter-circle surfaces, missing-cell/apron boundaries, native bound containment and reservation-box overlap/touching. Native halfpipe walls use thin local X and three-metre local Z; checks cover all twenty wall footprints, joined rail ends and their placement under rotated anchors.
- `skate_park_anchor_yaw_and_snap`: translated and rotated layouts preserve the supplied native snapped anchor, including anchors away from the world origin. Inverse and complete known-kind matching cover unordered/wrapped observations, relative noise, ambiguous single-floor kinds, invalid values/counts/prefabs, reflection and atomic finite bounds. The foundation extension supplies abstract construction bounds and independently checks exact lowest-corner contact, all other corners above support, native yaw steps, adjacent 3m cells, lowest-only support samples and complete support-plane agreement. Relative layouts remain stable; ledge anchors compensate for their absent legs.

Build the actual shared library and server plugin using genuine dedicated-server references, then execute the same engine-free scenarios separately against each actual DLL on .NET 8:

```sh
dotnet build mods/rust/shared/SkateMotion.csproj -p:RustManagedPath=/absolute/path/to/managed
dotnet build mods/rust/plugins/ShortcutSkate.csproj -p:RustManagedPath=/absolute/path/to/managed
dotnet build crates/approved_tests/skate/SkateParkLayoutScenarios.csproj -p:SkateParkLibraryPath=/absolute/path/to/SkateMotion.dll
dotnet context/skate-approved-park/bin/SkateMotion/net8.0/SkateParkLayoutScenarios.dll
dotnet build crates/approved_tests/skate/SkateParkLayoutScenarios.csproj -p:SkateParkLibraryPath=/absolute/path/to/ShortcutSkate.dll
dotnet context/skate-approved-park/bin/ShortcutSkate/net8.0/SkateParkLayoutScenarios.dll
```

The same anchor scenario checks the complete foundation-support masks, the L hole,
rotated grid poses and square symmetries, MEGA's first-flat-floor grid phase and
full reservation coverage, fit tolerances, and scalar missing/uneven all-cell
planes. These compose actual production helpers; they do not simulate native
raycasts, support labels, privilege checks or transaction callbacks.

The legacy `ActualPluginPath` remains a fallback when `SkateParkLibraryPath` is omitted. Reference only one DLL in each run. Intermediate/output directories under ignored `context/` are separated by the selected DLL's filename. Execute the emitted runner directly: SDK `dotnet run` can choose its default target despite the library property passed to its build. The runner prints its loaded DLL path and SHA-256 and accepts that expected hash as an optional argument.

The source 0.3.20 quarterpipe profile cases cover original, mini and large: independent
circular coordinates, the exact vertical lip tangent and `h == r` / `Vert=0` for all three kinds,
full-width flat decks at least 1.2 m deep, 0.05 m round coping dimensions and
closed back/underside shell samples. Original native carrier transforms remain
literal compatibility fixtures; they no longer define the riding surface.

Native-bound cases include the installed floor's float32 depth `3.0000005`
and low wall's width `0.2`, plus authored float32 center/size noise. The
Construction.bounds cell check allows one millimetre per horizontal edge;
combined center/size excursions beyond that budget and genuinely oversized
parts still refuse. Spawned-collider corner cases call the production
`ContainsQuarterPoint` helper with independent exact floor and rotated wall
corners, Y limits and the missing L cell. A legal distant rotated anchor's
float32 world round trip reproduces refusal with a fixed millimetre and passes
the world-scale tolerance; a five-centimetre true overhang still refuses.

MEGA cases call the same reference identity policy used by the server space
and per-part native deploy filters: the placing player, support and all 80
current transaction members are allowed; other players, buildings, deployables,
other modules and static World colliders remain blockers. A value-equal foreign
identity and null entity/anchor identities cannot inherit an exemption. These
fixtures exercise scalar corners and reference tokens, without executing Unity's
native overlap query, CheckFlags, collider-to-entity resolution or rollback.
The World/Construction/Deployed/Player mask and trigger exclusion remain in
effect; terrain/world colliders gain no broad exemption.

These checks use the layout helpers in the selected DLL, with independent expected positions and seams. Matching tolerance is relative to the deterministic observed-part-derived anchor, rather than an ideal common-anchor fit; the opposing ±0.004/±0.009 m quarter fixtures distinguish acceptance and refusal at tolerance 0.01. Matching does not prove registered park identity or infer the kind of a single floor. The scenarios do not verify native floor dimensions/colliders, building privilege, stability, save/load callbacks, demolition or gameplay, and they do not start the Rust client or server.

The existing park layout/anchor scenarios also cover the 3.0000005 m native floor seam at 3 m spacing, rotated/translated float32 centres, symmetric contact, real 2 mm intrusion and invalid contact tolerances. Saved reservations retain strict overlap. These are geometry checks; native collider queries and placement timing remain unverified.

The existing layout scenarios also cover the reported #480 twig-box min/max,
its pitched quarterpipe corner, translated cell rotations, unchanged vertical
bounds, wrong prefab names, altered geometry and refusal beyond the measured
horizontal allowance. The fixture uses the reported numbers; it does not create
native colliders or prove live placement.

## Park build items

The existing item scenario also compiles the production `SkateHeld.cs` adapter
against the selected actual shared/plugin DLL. Scalar observation objects supply
current main/belt membership, positions and active item; its logging boundary is
silent. All twelve kinds are selected across all six belt slots, then invalid
carrier/skin/quantity/name/marker/slot and unequip states are refused. These are
controlled reflection observations, not native inventory moves or Planner
recreation. Full client compilation and actual game acceptance remain separate.

The same approved layout/build-item scenarios also cover appended half-wall kicker kind 10, both-side measured wall joins and item accounting. For the actual plugin parser extension, set `SKATE_PARK_PARSER_REFERENCES=/absolute/path/to/genuine/managed` when executing the plugin build-item runner. It uses the plugin's own JSON attribute assembly for round-trips of old records and rejection of malformed/conflicting wall dependencies. Without that variable, the runner explicitly skips parser checks; native callbacks and gameplay remain separate.

The owner approved `skate_park_build_items` by name on 12 October 2026. It checks
the twelve independently specified names/tags, undefined marker refusals, exact stock
carrier/skin/quantity identity, atomic invalid outputs, verified single debit,
complete paid-group recovery eligibility, historical/free/interrupted/claimed
refusals, determinism and warmed allocation. It invokes the actual helper in each
separately selected compiled shared and plugin DLL; it does not copy the helper.

```sh
dotnet build crates/approved_tests/skate/SkateParkBuildItemScenarios.csproj -p:RustManagedPath=/absolute/path/to/managed -p:ActualLibraryPath=/absolute/path/to/SkateMotion.dll
dotnet context/skate-approved-park-build-items/bin/SkateMotion/net8.0/SkateParkBuildItemScenarios.dll EXPECTED_SHA256
dotnet build crates/approved_tests/skate/SkateParkBuildItemScenarios.csproj -p:RustManagedPath=/absolute/path/to/managed -p:ActualLibraryPath=/absolute/path/to/ShortcutSkate.dll
dotnet context/skate-approved-park-build-items/bin/ShortcutSkate/net8.0/SkateParkBuildItemScenarios.dll EXPECTED_SHA256
```

Build both actual libraries against genuine references as above. Each runner
prints its loaded DLL path/hash; the optional argument pins that exact hash.
Outputs/intermediates are isolated by DLL filename. Execute the emitted runner
directly after each build. `SkateLibraryPath` remains
an alias. Outputs stay in ignored `context/`. No native inventory/UID mutation,
save/claim atomicity, refund delivery, hammer or client UI is exercised; no Rust
client/server, Oxide or Shadow starts. See [integration limits](../../../docs/RUST-SKATE-BUILD-ASSETS.md).

## Park gesture selection

The owner approved `skate_park_gesture_selection` by name on 11 October 2026. It exercises the actual plugin's pure selection policy: legacy eight-kind and appended ten-kind halfpipe-first cycling, a configured order, cancellation, inclusive two-minute expiry, renewal, failed-placement retention, successful consumption and silent refusal for non-admins. Invalid states, clocks, orders and gesture-name catalog matches are checked atomically. It distinguishes an unknown gesture from invalid names, unavailable catalogs and ambiguous aliases, preserves the original lookup overload, and checks one-line catalog names with English/convar fallback, native ordering and case-insensitive deduplication.

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
odd/even counts and the ten-second maximum are covered. The shared DLL supplies those samples.
The same approved scenario compiles the actual engine-free client `SkateFoley` generator
and checks its rebuilt two-second Hollow loop, seed reproduction, peak/DC and ordinary wrap.
FFT checks bound source energy above Nyquist/max-pitch to one percent for the three shared
loops (including client seeds) and wood layer at 44.1 kHz; wheel texture decorrelates at 20 ms.
Wrapped friction/ring contacts in 20–30 ms buffers must equal the full longer event
folded modulo that buffer, including seed consumption; non-wrapped events stay truncated.

```sh
dotnet run --project crates/approved_tests/skate/SkateAudioScenarios.csproj -p:SkateLibraryPath=/absolute/path/to/SkateMotion.dll
```

An optional single runner argument names a local directory for eight original WAV previews.
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

The same scenario covers all eight `SkateRiderMotionOptions` combinations: suppressed
pelvis spring, exact left/right/both hand targets before IK, retained free-hand/shoulder
lag, warmed control toggles/history reset, near-reach fallback, transformed/reset poses
and every grab/stance/switch/inverted plant. The original/default overloads must give
identical pose/history bits. Each option has warmed success/refusal allocation checks.

Build the genuine-reference shared DLL first, then run the approved scenario:

```sh
dotnet build mods/rust/shared/SkateMotion.csproj -p:RustManagedPath=/absolute/path/to/managed
dotnet run --project crates/approved_tests/skate/SkateRiderMotionScenarios.csproj -p:SkateLibraryPath=/absolute/path/to/SkateMotion.dll -- EXPECTED_SHA256
```

The optional single argument verifies the loaded DLL's SHA-256; its path/hash are always printed.
Outputs stay in ignored `context/`. The integrated genuine net48 DLL passes 492,774 checks on
Mac net8, with zero measured bytes in 20,000-call warmed success/refusal loops. These are
secondary-motion geometry checks. The runner starts no Rust client/server or Shadow PC and
does not establish hand/surface contact, native rendering, adapter integration or IL2CPP cost.
See the [API and integration limits](../../../mods/rust/shared/README.md#rider-secondary-motion-api).

## Grind vocabulary and scoring

The owner approved `skate_trick_grind_names` by name on 12 October 2026. It exercises
the actual shared DLL's append-only grind values, rates, Frontside/Backside names,
cached references, entry/update/exit scoring, switch/fakie context, bail and banking,
duration/rounding limits and atomic invalid-state refusal. Unknown input kinds retain
their existing sanitization behavior. Warmed calls measure allocation on the executing runtime.

The same approved scenario covers Darkslide catches within 25 degrees of +/-180, retained
inversion, first-frame pop-out integration and actual completion/unwind of the other half.
It checks zero air-award at catch, ordinary duration/landing/banking, ground-drop/kind-change
bail without a restored combo, all existing orientation/impact guards, legacy zero-flip
Darkslides, explicit Fakie/Switch/Ollie/Nollie feeds, atomic malformed histories and warmed
catch/exit/landing allocations.

```sh
dotnet run --project crates/approved_tests/skate/SkateTrickGrindNamesScenarios.csproj -p:SkateLibraryPath=/absolute/path/to/SkateMotion.dll -- EXPECTED_SHA256
```

The optional argument verifies the loaded DLL hash; its path/hash are always printed.
No client/server or Shadow PC starts. Native contact, entry recognition, poses and IL2CPP
cost remain unverified. See the [grind contract](../../../mods/rust/shared/README.md#grind-vocabulary).

## Direct park placement requests


The existing `skate_park_anchor_yaw_and_snap` also exercises the server's direct-placement request helper: aim versus explicit anchors, invariant numeric parsing, finite coordinate/yaw bounds, yaw normalization and atomic refusal outputs across cultures. It invokes the actual public helper by its exact reflected signature and requires that helper in `ShortcutSkate.dll`. The shared `SkateMotion.dll` contains geometry helpers only, so its layout run omits the server-request extension. These scalar checks do not execute native aim rays, building hooks, placement or gameplay.
