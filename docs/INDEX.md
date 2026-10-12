# `docs/` — the one-page map
Short files (each ≤50 lines) on what lives where and how to poke the live
game. Keep them this short: nobody opens a long file twice.

Full goal: [RUST-MW2-SKATE.md](RUST-MW2-SKATE.md); [loadout/damage component](RUST-SERVER-MOD.md); standalone rewrite paused.
Historical standalone goals and verified status: [../TODO.md](../TODO.md).
The inherited run/import guides below document optional upstream modes.
| file | about | when to read |
|---|---|---|
| [`AUTONOMY.md`](AUTONOMY.md) | standing authorization, task ownership and verification | continuing work or coordinating agents |
| [`RUST-MW2-SKATE.md`](RUST-MW2-SKATE.md) / [client-loader gate](RUST-CLIENT-LOADER.md) | full existing-Rust MW2 gunplay/skate goal, exact client feasibility and complete acceptance | planning the finished mod and proving its client route |
| [`RUST-SERVER-PLUGIN.md`](RUST-SERVER-PLUGIN.md) | implemented loadout/PvP source, configuration contract and real-server acceptance | installing or changing the Oxide plugin |
| [`RUST-SERVER-HOSTING.md`](RUST-SERVER-HOSTING.md) / [`RUST-SERVER-LINUX.md`](RUST-SERVER-LINUX.md) | self-managed host capabilities, cost approval and real-host acceptance; guarded Linux installer, portable plan and real-host control gates | choosing an existing PC or a permitted rental; preparing a permitted Linux Rust/Oxide host |
| [`RUST-INPUT-FRESHNESS.md`](RUST-INPUT-FRESHNESS.md) / [`RUST-NATIVE-INPUT.md`](RUST-NATIVE-INPUT.md) | bounded held observations, admission fences and actual protocol results; genuine held player input, swallowed-control refusal and native decoding evidence | connecting packet callbacks to fixed core ticks; binding callbacks to the shared rider session |
| [`RUST-RIDER-CONTROLS.md`](RUST-RIDER-CONTROLS.md) | rider identity, input release, candidate and cleanup fences; source contract | coordinating gunplay/skate lifecycle |
| [`RUST-SKATE-CLIENT.md`](RUST-SKATE-CLIENT.md) / [building assets](RUST-SKATE-BUILD-ASSETS.md) | reported Rust PC body/input architecture, shared adapter contract and native gaps | integrating the real client skate mod |
| [`RUST-SKATE-LIVE-RAILS.md`](RUST-SKATE-LIVE-RAILS.md) | live server rail registration, freshness fencing and real-scene results | binding rails to the skate world |
| [`RUST-SKATE-PROBES.md`](RUST-SKATE-PROBES.md) | owned parked penetration probes, native results and cleanup obligations | creating probes for the genuine collision adapter |
| [`RUST-SKATE-NATIVE-MOTION.md`](RUST-SKATE-NATIVE-MOTION.md) / [`RUST-SKATE-WORLD.md`](RUST-SKATE-WORLD.md) | shared native collision backend and actual calculated skate flow; genuine server collision adapter design and native acceptance gaps | assessing core/scene evidence and remaining player-host gates; connecting skate motion to Rust terrain and obstacles |
| [`MULTIPLAYER.md`](MULTIPLAYER.md) | standalone shared-world milestone, authority boundaries and two-client acceptance; design only | working on core multiplayer |
| [`DIRECT-MULTIPLAYER.md`](DIRECT-MULTIPLAYER.md) / [`NATIVE-MULTIPLAYER.md`](NATIVE-MULTIPLAYER.md) | bounded direct TCP server/client runtime and connected verification scope; Bevy join controls, connected input/presentation and verification limits | implementing standalone transport; using or changing the native multiplayer client |
| [`CONNECTED-CRAFTING.md`](CONNECTED-CRAFTING.md) / [`CONNECTED-TRADING.md`](CONNECTED-TRADING.md) | Cloth gathering, actor-owned Bandage crafting, private inventory and verification limits; fixed Wood-for-Bandage player trading, controls and verified scope | extending connected survival; extending the connected economy |
| [`CONNECTED-BUILDING.md`](CONNECTED-BUILDING.md) | connected Wood walls, controls, verified bounded flows and integration limits | extending shared construction |
| [`CONNECTED-FLOORS.md`](CONNECTED-FLOORS.md) | planned connected overhead Wood Floor rules, controls and acceptance; design only | planning shared construction |
| [`SHARED-AUTHORITY.md`](SHARED-AUTHORITY.md) | first actor-owned shared survival core and its verification boundaries | implementing the shared server world |
| [`BUILDINGS.md`](BUILDINGS.md) | local host building, controller bindings, persistence and unfinished systems | using or changing construction |
| [`CRAFTING.md`](CRAFTING.md) | native queue controls, payment/refund, pause and verification scope | using or changing queued crafting |
| [`WATER.md`](WATER.md) | native sip/barrel controls, water rules and verification scope | using or changing standalone water |
| [`GARDENING.md`](GARDENING.md) | native berry-bed controls, authority rules and verification scope | using or changing standalone gardening |
| [`PARK-EDITOR.md`](PARK-EDITOR.md) | standalone prop placement, native move/rotate controls and verification limits | using or changing the authored park editor |
| [`RUST-MAPS.md`](RUST-MAPS.md) | bounded Rust.World SDK-v9 map reader; inspection limits and missing runtime installation | inspecting Rust map formats |
| [`SKATE.md`](SKATE.md) | Skate 3 mode: what you need, where `default.xex` comes from, setup, controls, how it works, building a release | playing or changing the skate mode |
| [`PERFORMANCE.md`](PERFORMANCE.md) | the frame-time work in this fork: before/after numbers and every change | "why is it faster", profiling |
| [`IW4L.md`](IW4L.md) | upstream IW4L's own README | what IW4L is |
| [`BUILD.md`](BUILD.md) | system packages per distro (Fedora / Debian / Arch), macOS, what the Windows cross build needs | before your first build |
| [`RUN.md`](RUN.md) | running (`make map`, `--cmds`), controls frozen until `Playing`, `force_match_start`, sync-by-default, the verb list and the traps | before your first live run |
| [`WINDOWS.md`](WINDOWS.md) | portable `iw4launcher.exe`: `.env`, shortcuts into CoD, writable `iw4l-artifacts/` | building and running on Windows |
| [`DEPLOY.md`](DEPLOY.md) | `make release` / `publish` / `deploy`: the play profile, hashed `.zst`, master by SHA, provision kept separate | shipping a release, "why is the player on an old version" |
| [`DUO.md`](DUO.md) | `make duo`: two windows, fresh lobby ID, per-client console commands | reproducing multiplayer bugs locally |
| [`MASTER.md`](MASTER.md) | your own master over ssh from the machine with the clone: `cargo xtask master install`, a self-signed certificate with no domain, what to hand players | standing up a relay for yourself or your friends |
| [`PERF.md`](PERF.md) / [`BENCH.md`](BENCH.md) | native `.pftrace` — the only runtime truth; picking a UUID, the manifest, `IW4L_PERF`, SQL; `make bench`: the map-load waterfall and stage `exclusive` time, frame time as a span tree, the render/GPU/work counters, and the run package (`manifest.json`, `summary.json`); in-process, no trace needed | traces, scenario SQL, why a frame took 80 ms; "where did this run spend its time" |
| [`FFI.md`](FFI.md) | native data ownership, lifetimes, nullability and Perfetto adapters | changing a native boundary or diagnosing trace failures |
| [`RENDER.md`](RENDER.md) | the nine crates of the island, the frame path `IR → cull → one drawsurf list → tess → material → SM3 → wgpu`, GPU-side ownership, the `d3d9_*` border | touching the picture, techsets, lighting |
| [`ANIM.md`](ANIM.md) | three floors: `anim_iw4` (facts and curves), `xmodel_runtime` (tree and pose), who picks the clip (`sim` / `render_frontend/adapters/anim/`) | viewmodel, skeleton, bone hits |
| [`MAP-LOAD.md`](MAP-LOAD.md) | map load: the `session` → `assets` → install transaction, the `load_prepared_match` walk, the lane by `ZoneGame`, the artifact cache | a zone won't load, an asset went missing, "why didn't the match come up" |
| [`ENTITIES.md`](ENTITIES.md) | the `TickInput → sim::step → Snapshot` funnel, the `entity_iw4` taxonomy (`EntityState` / `Centity` / `ET_*` / trajectories), what sits where in `sim` | gameplay, networking, replay |
| [`SIM-STEP.md`](SIM-STEP.md) | `sim::step`: one `TickInput` → `Snapshot` funnel for authority, prediction and replay; `StepReason`; what makes a step deterministic | touching the step, prediction or replay |
| [`GSC-RUNTIME.md`](GSC-RUNTIME.md) | GSC → executable IR → Bevy runtime; args, arrays and tables still share one `Runtime` | implementing gameplay or script execution |
| [`GSC-POSTFX.md`](GSC-POSTFX.md) | script vision, color correction, blur, DoF and bloom | authoring or debugging GSC post effects |
| [`BOTS.md`](BOTS.md) | host AI: the per-tick pipeline, what a probe that never ran may not claim, the shared query budget, resumable routes, fighting from a position | bot decisions, bot movement, "why is it standing there" |
