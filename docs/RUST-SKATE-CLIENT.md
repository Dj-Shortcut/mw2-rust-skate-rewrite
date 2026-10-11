# Rust PC skate client architecture

Scope: existing Rust PC game; standalone rewrite parked. Claude/the owner reported these observations in [#337, run R](https://github.com/Dj-Shortcut/mw2-rust-rust-rewrite/issues/337#issuecomment-6094262459), [integrated runs R2/R3/T/P](https://github.com/Dj-Shortcut/mw2-rust-rust-rewrite/issues/337#issuecomment-6094749999) and, on the source of [PR #340](https://github.com/Dj-Shortcut/mw2-rust-rust-rewrite/pull/340), in further runs on 10 October 2026.
Codex did not rerun them. Client Steam build25824447. The owner's launcher plays client commit `0f3098a4b15eadcd644b247c184a2fdbf8332027` (plugin 0.15.0) with the shared modules pinned in its `sources.txt`; `claude-skate-next` is ahead of it (controller, trick vocabulary, score display) and is checked in the game before it is released.
Source, controls and tools: [client](../mods/rust/client/README.md), [tools](../mods/rust/client/tools/README.md).

## Reported client route

BepInEx IL2CPP loads through the owner's Steam shortcut. InputSystem Keyboard works;
the owner reported controller keys through Steam Input. Direct executable launch found no Gamepad.
A press that begins and ends between two frames shows only as `wasPressedThisFrame`.
The kinematic BasePlayer follows a separate dynamic movement body; PlayerWalkMovement stays
enabled. The client uses its own **SkateRide**, allowing downhill reversal, rather than SkateDrive.
While mounted, it owns full velocity, including ollie 6 m/s and air gravity 16 m/s², writing
after the game's 32 Hz fixed step. Preserve this exclusive ownership when adapting shared APIs.
The measured movement capsule is 1.8 m high/0.5 m radius; those are not skeleton dimensions.
Rig metres: thigh .422, shin .399, ankle-to-toe .162, upper arm .290, forearm .253, hip span .200, hips .048 below pelvis bone, pelvis-to-neck .491, neck-to-head .139;
standing hips about .87 above soles, ankle joints about .095. Measure missing dimensions.
Rider BoardPosition is the ankle plane: deck top + ankle height − FootLift (.01 m); the
adapter lowers toes and owns the separate .055 m heel-edge shift. DeckOffset is passed as
FootLift−ankleHeight (−.085 m for that rig); it does not shift ankle anchors.

## What the runs showed

The local model has 27 full-body skinned meshes normally shadows-only and eight `leg-` meshes
normally visible in first person. For the view from behind, full-body drawing is switched on and the leg set off.
Late bone writes rendered stance/push/air-flip/grab/bail/switch/grind/manual **poses** across 40 keyed views
(the push in three places, flip and grab apart, six grab kinds, the board a quarter round under the feet, the
bail at four moments, nose manual, nine views of the score display), stepped with key presses sent from a
distance; on `a79f293` the rider module refused nothing in 897 frames and the grabbing hand ended .037 m short. With the rider module of items 8–10 TryCreate refused nothing and joints met their targets within
.001 m, except the grabbing hand, which ends .06 m short of the deck edge: the adapter tips the pelvis about the
hip line so that the spine root meets the folded chest, and lifts neck and head when the chest folds more than 20°.
In a manual the board tips 15° about its rear axle and the footing follows. Bones changed before the next frame, after the rendered picture.
The **board** is drawn with the game's own `Rust/Standard` shader, found by name. Until `73474a8` it was the
one-piece shared mesh with a texture that gave every vertex colour a block of texels: in daylight it took the
world's light and cast a shadow; night was not looked at. Since then it is the detailed shared mesh in parts
(deck, trucks that steer with the lean, wheels that roll) with the mesh's own 256 × 256 texture; its deck top is
.092 m, which the rider's `DeckTop` follows. Not yet seen in the game.
In daylight the stance reads as hands on hips, the push as a step off sideways, and during a bail the rider holds
a squat at deck height while the board leaves; [#337](https://github.com/Dj-Shortcut/mw2-rust-rust-rewrite/issues/337#issuecomment-6096570455) lists what the look still needs.
Moving the **main camera** late gives a lit/skinned view; a second CopyFrom camera rendered white.
The **score display** is IMGUI text: the game has `RobotoCondensed-Bold` loaded and the display draws in it, but
`GUIStyle.font`'s setter is stripped from this client and throws; the style's font is set through the engine call
`set_font_Injected` that the interop lists. `GUI.DrawTexture`, rich text sizes and colours, alignment and a texture
built with `SetPixel` work; `Font.GetOSInstalledFontNames` is stripped.
Scripted tricks: .72 s air, about 1 m peak, 5 m/s impact; Ollie, Kickflip, Frontside 180, Heelflip,
Backside 180 Grab, switch changes, a held manual (+159) and a banked combo (+418) gave score 1268, the offline total.
Two late presses were refused as plain airs; native bail gameplay remains unverified.
Keys: K mount, W push, Space ollie, L camera, Shift manual, held Ctrl/C dismount; K, L and Space were pressed as real keys.
Sound: the engine's output per source, read back at a muted listener and scaled to full volume, was
.044–.132 for the rolling loop (walking pace to 8 m/s), .082–.205 for the grind loop and .278–.550 for the one-shots.
At a listener of .001 the rolling loop read .000, so a muted check uses .004. Nobody has listened yet.

## The server's limit without ShortcutSkate

Two kinds of pull-back were seen on the owner's server, which runs without the skate plugin.
**After waking:** five to seven during the first 2–2.5 s of the first ride, about .7 m back every .37 s, at
walking pace too and also when the ride began 8 s after waking. None when the first movement came 40 s or
150 s after waking, none on a second ride. Unexplained; the scripted runs touch neither mouse nor keyboard,
and the owner has not mentioned it from his own play.
**Speed:** rolling downhill at 4.4 m/s was pulled back four times in consecutive ticks after a few seconds, and a
ride pushed to 8 m/s twenty times on its way out, the after-waking ones among them. Rides at 2.1–2.8 m/s were not.
That fits the pace the server allows a walking player, with slack after a rest; the game does not report a rider as sprinting.
The client lowers its cap on two pull-backs in a row (to 2.1 m/s in that run), shows `server limit`,
rode back at the cap without a pull-back, and tries a little more later. ShortcutSkate is what lifts this.
With ShortcutSkate on the server the owner still saw the board held to 6 km/h once, during a session that
ran at 5 fps: a 200 ms frame sends the server a move it refuses at any speed. Since then two pull-backs
during a frame longer than 0.1 s leave the cap alone, a raise is tried every 3 s (a refused one doubles
the wait), and getting on more than 30 s after the last pull-back starts without a cap. Unverified in game.

## Integration and remaining acceptance

[Shared APIs](../mods/rust/shared/README.md) supply Tricks/Rider/Edges/Audio/BoardMesh and an alternative
Drive model; the client takes them from the commit it is built from.
Native grinding has not run: the stand-in adapter captured a .4 m ledge at 5 m/s, ground 11.8 m
in 2.9 s and exited, but the game beach had no ledge. A grind pose is not grind gameplay.
Reported server: Oxide 2.0.7820, Rust 126/2634.289.1; only ShortcutLoadouts 0.1.0 was installed.
[ShortcutSkate](../mods/rust/plugins/SKATE.md) installation/practice/patch/load/reload/cleanup remain open;
console-field submit/end-edit calls did nothing, so client command submission is not established.
The launcher was installed from `c5098ae` and tested: Rust started through the Steam shortcut, and with the
launcher killed the helper removed all six loader roots 35 s after Rust closed. Complete
update/crash/lifecycle recovery remains open. Death/respawn/reconnect, a lost late driver, clothing changes and
sound failure are covered only offline, against a stand-in engine outside this repository.
Interop regeneration after Rust updates and [loader gates](RUST-CLIENT-LOADER.md) remain separate.
One client/account was available; second-client observation, the owner's control feel and
[full acceptance](RUST-MW2-SKATE.md) remain open. [TODO](../TODO.md) tracks progress;
[Codex item 12](RUST-SKATE-CLIENT-REVIEW.md) reviewed the client source.
