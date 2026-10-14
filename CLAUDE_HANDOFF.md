# Project brief and current handoff

**Historical handoff:** this document records the earlier standalone rewrite
and its former Codex/Claude task split. That rewrite is parked. The human project
owner decides current assignments and any resumption; the roles, branches and
implementation instructions below are historical, not an active delegation.
Follow [docs/AUTONOMY.md](docs/AUTONOMY.md) and the owner's latest direction.

Build a standalone PC game in Rust and Bevy combining Rust-inspired survival,
building and an island world, MW2-inspired FPS gunplay and original operators,
and Skate 3-inspired skating and a park editor. The user explicitly clarified
that this is inspiration, with original assets made from scratch. They want
the breadth of Rust's systems. This is a substantial unfinished game project.

Every player-facing game string must be English and remain English: HUDs,
menus, items, control hints, feedback and displayed errors. Follow this rule in
the save/load task, including new validation and migration messages.

Do the implementation and verification yourself. Do not ask the user to test
incremental builds. Maintain TODO.md with honest progress. The user has
explicitly authorized committing and pushing the current development source
to GitHub. This is a source handoff, not a finished or playable release.
Do not revive the canceled Xbox 360–PC crossplay project.

## Repository and access

Cloud working directory: `/workspace/mw2-rust-rust-rewrite`.
Remote: https://github.com/Dj-Shortcut/mw2-rust-rust-rewrite .
The development tree includes the source imported from the public mashup
engine and the subsequent standalone-game implementation. Use the current
repository revision for the source handoff; TODO.md describes its unfinished
state and verification limits.

The user's intended Mac directory is
`/Users/Mac/Documents/Codex/2026-10-02-kan-je-een-nieuw-project-beginnen`.
This cloud cannot access it or control the Claude app on that Mac. For local
development, clone or update the GitHub repository at the intended directory.
Ignored `context/artifacts/` evidence is not included in the source handoff;
transfer it separately if developer context is needed. This document has not
been sent to Claude through an app connection.

Read AGENT.md, CONTEXT.md, docs/INDEX.md and TODO.md before editing.

## Historical Codex and Claude coordination

Claude's implementation task: [issue #6](https://github.com/Dj-Shortcut/mw2-rust-rust-rewrite/issues/6).

Codex owns the native gameplay presentation and graphical/input/audio run
verification in `crates/bootstrap/src/native.rs`, plus authored content and its
generators. Claude owns complete validated local-player save/load for the existing
authored survival loop. This is implementation work, not a starter skeleton or
Minecraft/CI cleanup task. The task is delegated through a GitHub issue and PR;
there is no direct connection to an external Claude session here.

After Codex publishes `codex/authored-survival-loop`, fetch that branch and create
`claude/survival-save-load` from it. Open a stacked PR with
`codex/authored-survival-loop` as its base. After that branch lands on `main`,
update the PR base to `main`. Do not merge either branch yourself. Keep all work
on the Claude branch and report dependencies or ownership conflicts in the issue.

Claude may add `crates/survival/src/persistence.rs`, edit save/load and restoration
integration in `lib.rs` and the authored restore entry in `rules.rs`, and add
validated skate-state serialization in `skate.rs` if needed. Preserve the public
`Session::save`/`Session::load` interface used by the frontend. Do not edit native
presentation, assets, CI or unrelated gameplay. If a simulation API change is
needed outside these files, describe the required API in the issue for Codex.

Acceptance criteria:

- Round-trip local-player health, position, velocity/view, weapon, clip/reserve
  ammunition, inventory, canonical building resources, needs, resource-node
  depletion, buildings and editor objects together. Preserve mounted skate state
  and score consistently, with no duplication or free resource refill.
- Keep the authored GSC dead/alive lifecycle consistent with restored health.
  After loading, living players can move/fire/take damage; dead players remain
  action-gated and can use the existing respawn flow. Do not restore only the
  visible health field while leaving the script player state stale.
- Queue restoration script work and execute it during an authority tick when
  `StepRequest` exists. Preserve authority preflight, scheduler ordering and
  monotonic time; do not invoke frame-dependent natives directly from file load.
- Bound file size, collections, numbers, IDs, ammo and resource balances. Reject
  malformed/unsupported saves, invalid seeds and restored player/world collision
  without changing the active session. Validate and commit a complete candidate
  state; a failed restoration must leave the old session usable.
- Version the new format and migrate scene-v2 saves with explicit defaults for
  missing player fields. Keep their inventory, needs, depletion and scene data.
  Retain atomic file replacement; report precisely which state is persisted.
- Verify meaningful live/dead, mounted/walking and damaged/ammo-depleted
  round-trips; invalid/oversized/colliding saves; migration; and gameplay after
  restoration. Run the affected compiler and publish checks. Keep temporary
  probes/evidence under ignored `context/`; permanent scenarios still require
  owner approval under `crates/approved_tests` policy.

The PR must state implemented behavior, how to run it, verification results and
remaining limitations. A task document or issue does not mean Claude has received
the task or started work; only a Claude response or PR establishes that.

The imported engine is public upstream source, not newly authored project
code. The Apache licence and NOTICE are retained. Separate licence scope for
the in-tree SK8/MinecraftOSS modules and provenance of legacy numerical data
remain part of the pending review in TODO.md. Do not claim independently
verified clean-room provenance or that every imported datum is from scratch.

## Reference requested by the user

https://github.com/rehan-remade/universal-modder

Its MIT repository provides AI-agent skills, a Python `um` CLI, modding and
reverse-engineering playbooks, asset workflows and game automation. It supports
Claude Code and Codex. It is not a ready-made game engine or this game's source.
The fal asset-generation route requires an API key and may incur charges.
Review useful workflows; do not assume universal compatibility, install tools
blindly, upload project data or publish external contributions automatically.

## Implemented code and evidence

- `crates/rust_building`: bounded building placement, materials, costs, owner,
  doors, stability/collapse, JSON storage and collision.
- Shared simulation/network integration and authoritative bullet/melee damage
  to exact building IDs. Five damage probes passed; prediction does not damage
  buildings. Network protocol version is now 95.
- `crates/rust_maps`: bounded Facepunch SDK-v9/LZ4/protobuf inspection tool.
  It is an optional reader, not a generator or runtime replacement terrain.
- Original procedural building materials in WGSL; Naga validation passed.
  Downloaded textures were removed from the product asset path.
- `scripts/generate_authored_content.py`: Blender generator for original masked
  operator, carbine, skateboard and timber pieces. Generated GLB and editable
  blend files under assets/authored. Operator includes a rig and three clips.
  Props have no animations. Asset preview renders are not game screenshots.
- `crates/survival`: authored session and embedded original authority scripts,
  weapon data, island mesh/triangle-prism collision, six editor object geometries,
  edit history, bounded inventory, two recipes, consumables, hunger/thirst,
  30 finite seeded resource nodes and a shared-world skate controller.
- `Session` uses finite starting resources: wood 600, stone 100, metal 60,
  one bandage and two food/two water items. Crafting debits the same canonical
  building-resource balance. Full-health bandage use is rejected without loss.
  Gathering checks reach/occlusion, commits yields transactionally and removes
  depleted resource collision. Save format 3 (`crates/survival/src/persistence.rs`,
  PR #10) stores buildings, editor objects, inventory, vitals and resource-node
  depletion plus the local player: alive/dead, position, velocity, view, health,
  weapon, clip/reserve ammo, kills/deaths/score and mounted skate state. Loading
  validates the whole save, restores the player through one authority tick on a
  copy of the world and commits only on success; format 2 saves migrate with
  fresh-spawn player defaults. The NPC, queued script work and editor undo/redo
  history are not saved.
- Integrated headless flow passed: 15 terrain traces, movement, firing/ammo
  consumption and NPC kill; six editor meshes, invalid/near placement rejection,
  collision, undo/redo, save/load/delete; canonical crafting costs, healing and
  reserve-ammo increase; reachable tree harvesting and saved depletion; death
  action gates, authored-script respawn and second-life damage; mounted skating,
  push and walking handoff. The spawned NPC is a damage target, not verified AI.
- Independent inventory/vitals/gathering/skating probes check validation,
  deterministic finite state, capacity/atomic failures, visibility and shared
  terrain/ramps/thin-wall collision. The skate controller supports push, steer,
  brake, ollie, air spins/flips, scored landings and bails; grinding and manuals
  are not implemented.
- `scripts/generate_authored_audio.py`: seven original CC0 mono 48 kHz PCM WAV
  cues under `assets/authored/audio`. Header/hash/amplitude checks and exact
  byte-for-byte regeneration passed. This does not prove runtime playback or
  final listening quality.
- `crates/bootstrap/src/native.rs`: Bevy native frontend, first-person controls,
  authored GLB scenes, terrain and editor rendering, HUD, pause and save input.
  `cargo check -p bootstrap` passed after Bevy 0.19 API corrections.
  Controller, ADS/recoil, inventory/crafting/gathering, skating camera/board,
  resource rendering, respawn and sound-cue integration are now present in code.
  The WAV dependency has been fetched and the expanded frontend compiler check
  passed. Optimized builds passed for the initial, WAV and reflection feature
  configurations. The first GPU run exposed scene type-registration and missing
  tonemap-LUT errors; `reflect_auto_register` and `AcesFitted` address them in code.
  The corrected graphical run starts and displays the terrain and authored GLBs.
  Keyboard/mouse inventory pause, both recipes/resource costs, ammo transfer,
  firing/NPC damage and kill, reload/ADS, skate camera/push/ollie/landed score and
  dismount were observed on Xvfb/Mesa software Vulkan. English HUD, inventory,
  editor messages and pause are also observed in a rebuilt executable. Editor
  placement/undo/redo and remaining native flows are still being verified.
- Launcher default/`game` route now calls `bootstrap::run_native()` before the
  optional legacy game-import path. The earlier launcher/mapreader compiler check passed.

## Current verification priority

The previous GSC preflight failure is resolved. `rules.rs` installs and starts
three embedded original script modules through the existing authority scheduler.
Damage commits through the existing damage/death hooks. Do not reintroduce a
requirement for original scripts or bypass preflight. Bootstrap resets the script
runtime, so install/start must stay after bootstrap. Environmental damage is
queued to the next authority frame; callbacks require that frame context.

The integrated headless flow, full workspace check and optimized builds pass.
The corrected GPU run passes startup and the keyboard/mouse flows listed above.
Continue verifying editor/build/gather/respawn and audio flows. Rendering uses
Xvfb/Mesa software Vulkan, so it is not a hardware performance check. The cloud
has no audio device or Xbox controller; audible output and controller hardware
remain untested. Keep those facts separate from backend evidence and release
readiness.

## Development controls in the new frontend

The following bindings are implemented in `native.rs`. Some keyboard/mouse
flows have been observed as listed above; controller hardware is untested.

| Mode | Keyboard/mouse | Xbox controller on PC |
|---|---|---|
| Walking/FPS | WASD, mouse look, Shift sprint, Space jump, Ctrl crouch; LMB shoot, RMB ADS, R reload, F gather | LS move, RS look, RT fire, LT ADS, A jump, LS-click sprint, B crouch, X reload, Y gather |
| Inventory (pauses world) | Tab open; 1/2 recipe, C craft; H bandage, J food, K water, U ammunition to reserve | Dpad Down in FPS opens; Dpad slot, LB/RB recipe, X craft, A use, B close |
| Building | B toggles and dismounts; 1–4 piece, R rotate, LMB place, RMB door | Back cycles FPS/build/editor; Dpad selection, RT place, X door |
| Editor | E toggles on foot; 1–6 object, Q/R rotate 15°, LMB place, RMB delete, Ctrl-Z/Y undo/redo | Back cycles modes; Dpad selection, RT place, X delete |
| Skating | V mount/dismount; W push, A/D steer, S brake, Space ollie, Q/E spin, R flip | LB/RB + Y toggle; LS push/steer, RT push, LT brake, A ollie, LB/RB spin, X flip |
| Session | Escape pause, Enter respawn when dead, F5 save/F9 load | Start pause |

The skate camera uses the authored operator and board in third person. Audio
hooks respond to actual ammo use, reload, movement, harvest and skate events;
audio device output remains unverified. F5/F9 save and load the complete local
session (headless verified); the frontend still has to adopt the restored view
angles after F9, and the native flow is not yet graphically verified.

## Remaining work

TODO.md is the full roadmap and separates code, headless, graphical and release
status. Missing systems include grinds/manuals/advanced skating, editor ghosts
and moving existing props, inventory drag/drop/hotbar/containers, larger crafting
progression, complete survival effects, animals/NPC AI, monuments, electrical
and fluid systems, farming, vehicles, a full weapon/animation/audio catalogue,
authoritative multiplayer, complete world persistence, menus/settings and
releases. The small development island is not a full Rust-scale world. Existing
finite resources, two recipes and simple needs are an initial implementation,
not parity with Rust's systems. Respawn now works in the backend; native UI
verification and final death/loot policy remain.

No executable release exists. Do not label this finished, fully rewritten,
Rust-equivalent or playable based on compiler checks.

## Cloud toolchain and evidence

Rust executable: `/workspace/toolchains/cargo/bin/cargo`.
Environment: CARGO_HOME=/workspace/toolchains/cargo,
RUSTUP_HOME=/workspace/toolchains/rustup,
CARGO_TARGET_DIR=/workspace/rust-mw2-skate/target,
PKG_CONFIG_PATH=/workspace/native/pkgconfig.
Bevy 0.19 PBR/glTF/scene/animation dependencies have been fetched.
The WAV decoding dependency has also been fetched.

Earlier compiler check: `cargo check --offline -p launcher -p rust_maps`.
The expanded frontend compiler check and optimized builds passed separately;
graphical flow verification remains open.
Current native probe manifest:
`context/artifacts/2026-10-02-native-world/2-AUTHORED-RUNTIME-PART/probe/Cargo.toml`.
Integrated passing evidence:
`context/artifacts/2026-10-02-native-world/2-AUTHORED-RUNTIME-PART/gameplay-flow-corrected.log`.
Keep temporary probe code/evidence under ignored context; only approved named
test scenarios belong in crates/approved_tests. Format only touched files.

The workspace-only graphics environment is under `/workspace/graphics-validation`.
It supported the first GPU launch; verify the corrected native run before claiming
a successful graphics result. Do not ask the user to perform incremental tests.
