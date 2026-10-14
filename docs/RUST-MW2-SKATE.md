# Complete Rust skateboard mod

On 14 October 2026 the owner removed MW2 from the active goal and prioritized skate feel and controls. New skate. is the primary reference, Skate 3 secondary; the standalone rewrite stays parked.
The filename is retained for existing links. Earlier gunplay/operator/loadout plans are historical work, not current delivery criteria; preserve their source and credits.
Rust supplies the world, survival, building and TC rules. Player text stays English.
The [first repeatable skatepark demo](RUST-SKATE-DEMO.md), [#408](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/issues/408) and [TODO](../TODO.md) track implementation and evidence separately.

## Complete player flow

1. Join the intended server/client mode on compatible recorded versions and use the documented controller mapping.
2. Choose/equip a genuine owned park item, preview/rotate/place it and return to riding; preserve inventory, build privileges, item identity and payment/refund rules.
3. Mount a visible skateboard, push at a controllable pace, carve, brake/powerslide and use deliberate flick gestures.
   Preserve continuous momentum through ramps, natural fakie rollback, take-off and landing, with intentional friction/braking and bounded server movement.
4. Perform a trick and a grind, land or bail safely, dismount, restart the attempt and repeat through normal controls.
   Record actual controller input, readable rider/board/camera presentation and audible skateboard movement/landing; compare observed handling with the selected skate. reference.
5. Return to ordinary Rust movement, survival and building with the existing TC rules intact. A second client must observe the intended rider, board and movement consistently.
6. Verify death, reconnect, invalid input, plugin reload and removal without stuck controls, orphan objects or inventory/world damage.
   Any required service or host restart is a separate operation requiring the owner's applicable authorization and verified private backup.

## Server/client feasibility gate

[Oxide](https://docs.oxidemod.com/guides/developers/server-lifecycle) modifies server behavior and does not support player client modification.
[Player input](https://docs.oxidemod.com/hooks/player/OnPlayerInput) provides a server entry point; it does not establish client animation, camera or predicted board physics.
[Facepunch entities](https://wiki.facepunch.com/rust/Entities) reference native game prefabs. Arbitrary authored client assets are not a proven server-plugin capability.
Prove the actual visible board, rider pose, controls and movement through the compatible [client route](RUST-SKATE-CLIENT.md).
Headless physics, a renamed native vehicle, static binary inspection or a server field change cannot establish skate feel.
If a client extension is required, verify its compatible architecture; do not resume the parked rewrite or reduce the skateboard goal to a server-only component.
Use native game content or original authored content through a verified route. Keep game files, binaries, credentials, server data and operational logs outside GitHub; research evidence belongs in private artifacts.

## Definition of ready

Verify the complete repeatable flow, controller behavior, presentation/audio and failure/lifecycle cases on exact recorded Rust/framework/client versions before asking the owner to play an improved build.
Source compilation, synthetic input and helper/CI checks prove only their stated scope; they cannot check off native or physical-controller acceptance.
The client/server skate source and reported local riding do not complete the product. Keep installed builds, source changes and uninstalled candidates distinct in [TODO](../TODO.md).
Preserve the existing world, Oxide plugins, configuration and private data; verify a full private backup before replacing update files.
The owner decides task assignments and merge/install according to current issue/PR instructions. Select/rent nothing without the owner's choice and concrete cost approval; see [hosting](RUST-SERVER-HOSTING.md).
