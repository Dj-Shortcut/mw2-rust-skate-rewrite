# First reliable Rust skatepark demo

[Issue #408](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/issues/408) orders the work under [the full mod goal](RUST-MW2-SKATE.md). The existing Rust PC game supplies survival, building and TC rules. The standalone rewrite remains parked. New `skate.` is the primary skating reference; Skate 3 is secondary.

## Completion means one repeatable player flow

- [ ] Establish the exact installed game, client/shared/server source and release pins together.
- [ ] With the controller, choose/equip a genuine owned kicker or quarterpipe item, preview/rotate/place it, then return to riding. Handle the eight-item/six-belt-slot inventory flow; normal play needs no admin console.
- [ ] Mount, push/carve into a transition, perform one deliberate trick, land, dismount, restart the attempt with the controller and repeat. Verify input rearming and presentation-history recovery; preserve the calibrated direction convention and natural fakie momentum. Releasing a flick must not trigger another flip.
- [ ] Confirm that placed geometry agrees with the preview and native transform. Invalid targets/identity clear the preview; cancel/refusal preserves correct item accounting.
- [ ] Demonstrate reconnect/unload and duplicate-callback recovery without lost items, untracked carriers or replacement while cleanup is unresolved. Retain existing server ownership, build privilege and payment/refund rules.
- [ ] Record actual integrated in-game controller evidence for this flow, including a failed placement and recovery. English contextual hints, readable rider/board/landing view and audible movement/landing belong to acceptance.

These boxes remain open until their stated evidence exists. Compiler checks, headless fixtures, source screenshots and upstream test assertions cannot complete native acceptance. Claude owns client integration/build/native checks; Codex owns scoped shared/server work. Claims remain in the issues; the owner retains merge/install.

The half-wall kicker attachment, one-cell mini quarterpipe, three-cell L quarterpipe and modular mega-halfpipe remain in #408 after the coherent baseline. This milestone does not add a mapping, network protocol, undo feature or deadline.

## ReSkate lessons to apply

Source snapshot: [ReSkate `28118ef`](https://github.com/Dingo-Shenanigans/ReSkate/commit/28118efc2735a71a0c38bda2e7955d35cb2e7d24). It extends the installed Frostbite `skate.` game; native skating, collision and animation are not a complete reusable engine in this source.

| Source-backed pattern | Application to this demo |
| --- | --- |
| [Generation/revision, preview token and increasing sample sequence](https://github.com/Dingo-Shenanigans/ReSkate/blob/28118efc2735a71a0c38bda2e7955d35cb2e7d24/Extension/Objects/ParkEditor/park_editor_preview.cpp#L98) | Reject stale previews/confirmations after selection, reconnect or world changes; bind them to genuine owned item identity. |
| [Native pose readback](https://github.com/Dingo-Shenanigans/ReSkate/blob/28118efc2735a71a0c38bda2e7955d35cb2e7d24/Extension/Objects/ParkEditor/park_editor_runtime.cpp#L210) and [completion before document/history commit](https://github.com/Dingo-Shenanigans/ReSkate/blob/28118efc2735a71a0c38bda2e7955d35cb2e7d24/Extension/Objects/ParkEditor/park_editor_runtime.cpp#L477) | A creation callback is insufficient: verify actual transform and members before reporting placement/payment completion. Logical commit discipline does not prove atomic native rollback. |
| [Retirement includes committed rows, pending steps and late creation](https://github.com/Dingo-Shenanigans/ReSkate/blob/28118efc2735a71a0c38bda2e7955d35cb2e7d24/Extension/Objects/ParkEditor/park_editor_lobby.cpp#L33) | For #413, retain a separate receipt for every distinct observed carrier until removal is proved; never overwrite the first receipt. |
| [Claimed trigger lifetime and release hysteresis](https://github.com/Dingo-Shenanigans/ReSkate/blob/28118efc2735a71a0c38bda2e7955d35cb2e7d24/Extension/Skater/grind_exit_control.cpp#L227) | Give a controller gesture one owner across editing/riding transitions; consume/release it before another action. Preserve #406/#407's calibrated flick behavior rather than copying thresholds. |
| [Horizontal travel-aware boost](https://github.com/Dingo-Shenanigans/ReSkate/blob/28118efc2735a71a0c38bda2e7955d35cb2e7d24/Engine/Game/Skater/velocity_boost.h#L9) | Keep facing, stance and meaningful travel direction separate when verifying fakie/push (#409). This helper is not the native push algorithm. |
| [Camera reset and frame-start sampling](https://github.com/Dingo-Shenanigans/ReSkate/blob/28118efc2735a71a0c38bda2e7955d35cb2e7d24/Engine/Game/Skater/first_person_spring.h#L223) | Reset presentation history across discontinuities; multiple callbacks must not integrate smoothing repeatedly in one frame. Adapt only if needed by the owned client flow. |
| [Accepted build identity kept with reviewed native contracts](https://github.com/Dingo-Shenanigans/ReSkate/blob/28118efc2735a71a0c38bda2e7955d35cb2e7d24/Engine/Game/Build/supported_build.h#L7) | Validate exact runtime compatibility before enabling features; #410 recovery must distinguish transient failure from unavailable API. Existing Rust pins remain authoritative. |

ReSkate's [grind-exit stance uncertainty](https://github.com/Dingo-Shenanigans/ReSkate/blob/28118efc2735a71a0c38bda2e7955d35cb2e7d24/Extension/Skater/GRIND_EXITS.md#L56) is not a Rust stance contract. Its timing values, native IDs, addresses and tuning assets are game-specific. Study the patterns; author the Rust implementation and verify its own behavior. ReSkate-authored code is [GPL-3.0](https://github.com/Dingo-Shenanigans/ReSkate/blob/28118efc2735a71a0c38bda2e7955d35cb2e7d24/LICENSE); this note imports no code or assets.

## Where REA helps

Source snapshot: [Reverse Engineer Anything `7e3a198`](https://github.com/morluto/rea/commit/7e3a19863835215491b55fba7127a78bad6a7709). Its [static managed provider](https://github.com/morluto/rea/blob/7e3a19863835215491b55fba7127a78bad6a7709/src/dotnet/ManagedStaticProvider.ts#L49) reads PE/CLI metadata and CIL without executing the assembly. It can help inspect genuine exact-build Rust/Oxide references and [compare member changes](https://github.com/morluto/rea/blob/7e3a19863835215491b55fba7127a78bad6a7709/docs/managed-code-analysis.md#L181). Keep hashes/MVIDs with findings; names alone or tokens from another build do not establish identity.

It does not establish callback invocation/order, runtime loading or gameplay, decode IL2CPP metadata, or prove native parameter/packing/marshalling compatibility. Its [native match limitations](https://github.com/morluto/rea/blob/7e3a19863835215491b55fba7127a78bad6a7709/src/domain/managed/managedNativeVerificationMatch.ts#L388) explicitly separate symbol correlation from ABI validation. Open ReSkate source can be read directly without REA.

REA-authored code is [MIT](https://github.com/morluto/rea/blob/7e3a19863835215491b55fba7127a78bad6a7709/LICENSE). Use remains optional: check the [installed package versus repository-main capabilities](https://github.com/morluto/rea/blob/7e3a19863835215491b55fba7127a78bad6a7709/docs/installation.md#L202) before selecting a version. This research performed no REA setup, tool registration, package installation or binary analysis. Genuine game/reference files and any future private inspection evidence stay outside GitHub.

## Integration order and evidence

Follow existing scoped work: #405 proves only the proposed diagnostic sender until native receipt exists; #406/#407 cover flick rearming; Claude's #409/#410 cover rollback momentum/scanner recovery; #411/#412 guard pending-item transfers; #413 tracks duplicate-carrier cleanup. None alone delivers the demo. Avoid edits to another active owner's source; update #408 with integrated native evidence before checking off this goal in [TODO](../TODO.md).
