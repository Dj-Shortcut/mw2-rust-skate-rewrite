# First reliable Rust skatepark demo

[Issue #408](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/issues/408) orders the work under [the full mod goal](RUST-MW2-SKATE.md). The existing Rust PC game supplies survival, building and TC rules. The standalone rewrite remains parked. New `skate.` is the primary skating reference; Skate 3 is secondary.

## Completion means one repeatable player flow

- [ ] Establish the exact installed game, client/shared/server source and release pins together.
- [ ] With the controller, choose/equip a genuine owned kicker or quarterpipe item, preview/rotate/place it, then return to riding. Handle the eight-item/six-belt-slot inventory flow; normal play needs no admin console.
- [ ] Mount, push/carve into a transition, perform one deliberate trick, land, dismount, restart the attempt with the controller and repeat. Verify input rearming and presentation-history recovery; preserve the calibrated direction convention and natural fakie momentum. Releasing a flick must not trigger another flip.
- [ ] Confirm that placed geometry agrees with the preview and native transform. Invalid targets/identity clear the preview; cancel/refusal preserves correct item accounting.
- [ ] Demonstrate reconnect/unload and duplicate-callback recovery without lost items, untracked carriers or replacement while cleanup is unresolved. Retain existing server ownership, build privilege and payment/refund rules.
- [ ] Record actual integrated in-game controller evidence for this flow, including a failed placement and recovery. English contextual hints, readable rider/board/landing view and audible movement/landing belong to acceptance.

These boxes remain open until their stated evidence exists. Compiler checks, headless fixtures, source screenshots and upstream test assertions cannot complete native acceptance. Before taking implementation, build, validation or integration work, read the current claims and merge/install instructions in [#337](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/issues/337), #408 and the relevant open issue/PR, following [AUTONOMY](AUTONOMY.md).

The half-wall kicker attachment, one-cell mini quarterpipe, three-cell L quarterpipe and modular mega-halfpipe remain in #408 after the coherent baseline. Preserve the calibrated mapping. A new lower-level transport and undo feature are outside this baseline; no deadline is set.

The held-item application identity protocol is part of this demo: first demonstrate the native receipt route, then implement the version/session/sequence/UID/kind, explicit-clear and expiry contract in [the inbox gate](RUST-SKATE-INBOX.md). Picker choice or arbitrary log text cannot establish identity; native placement/payment checks remain authoritative.

## Placement and input requirements

| Rule | Acceptance for this demo |
| --- | --- |
| Version each preview lifetime and reject stale samples. | Selection, reconnect or world changes cannot confirm a previous placement; genuine owned item identity remains required. |
| Verify actual placement before committing it. | Read back native transform and verify all members before reporting placement/payment completion. A creation callback or accepted preview alone is insufficient; failed partial edits need cleanup/recovery. |
| Keep cleanup receipts for committed, pending and late-created objects. | Retain every distinct observed carrier until removal is proved (#413), including additional callbacks; never overwrite the first receipt or release unresolved cleanup. |
| Give each controller gesture an explicit owner and release rule. | Editing/riding transitions consume and release input before another action. Preserve the calibrated flick convention (#406); tune thresholds against actual controller evidence. |
| Keep facing, stance and meaningful horizontal travel separate. | Verify fakie/push (#409) without treating small drift or vertical velocity as a new travel direction. |
| Reset presentation history across discontinuities. | Recovery/restart clears stale camera state; multiple callbacks cannot integrate smoothing repeatedly in one frame. |
| Validate exact runtime compatibility before enabling features. | Distinguish transient failure from an unavailable API (#410); preserve current Rust build/release compatibility checks. |

Design references include [ReSkate](https://github.com/Dingo-Shenanigans/ReSkate), which extends the installed Frostbite `skate.` game. Its source does not supply the complete native skating, collision and animation engine as reusable code. Its stance assumptions, timing values and native integration are game-specific; author and verify the Rust implementation against its own rules. ReSkate-authored code is GPL-3.0; no upstream code or assets are included in this guide.

## Optional REA inspection

[Reverse Engineer Anything](https://github.com/morluto/rea) can help inspect genuine exact-build Rust/Oxide PE/CLI metadata and CIL statically, and compare managed member changes. Keep artifact hashes and module identity with private findings; names or tokens from another build do not establish identity. Check the installed package's capabilities before selecting an inspection workflow.

Static inspection does not establish callback invocation/order, runtime loading or gameplay, decode IL2CPP metadata, or prove native parameter/packing/marshalling compatibility. Correlating native symbols is not ABI validation. Open ReSkate source can be studied directly.

REA-authored code is MIT. Preserve applicable licences for any future imported material. Genuine game/reference files, detailed source-location evidence and inspection journals belong outside the published repository.

## Integration and evidence

Follow current claims in the linked tasks: [the inbox gate](RUST-SKATE-INBOX.md) needs native receipt before held-item transport; #406 covers flick rearming; #409/#410 cover rollback momentum/scanner recovery; #411 guards pending-item transfers; #413 tracks duplicate-carrier cleanup. No single component completes the demo. Update #408 with integrated native evidence before checking off this goal in [TODO](../TODO.md).
