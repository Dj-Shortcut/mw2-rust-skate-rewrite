# Rust PC skate client

A plugin for the existing Rust PC client that puts the local player on a skateboard: pushing,
braking and carving, rolling on slopes, ollies and nollies, flips, shove-its and their
combinations, spins that end in switch stance, named grabs, manuals and nose manuals, grinding
along edges, bails, a score with combos, a rider pose on the game's own player model, a view from
behind, sounds and a score display. It is ridden with a
controller, laid out as in the skate. games, or with keys. It belongs to
[#337](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/issues/337) and the
[complete flow](../../../docs/RUST-MW2-SKATE.md); the calculations it shares with the server
plugin are in [`../shared`](../shared/README.md).

The plugin is loaded by BepInEx (IL2CPP). On the client that runs it, it changes the velocity of
the local player's movement body, the pose in which the local player's model is drawn and the
position of the main camera, and it adds a board, sounds and a score display of its own. It sends
nothing to the server: to the server the rider is a player who moves.

## Development verification

The handover corrections retain tangent-speed physics with horizontal server/HUD limits,
process complete landing impact before accepted roll, preserve grind travel independently of
visual board angle, and own one rig and its audio resources. The client menu matches the server's
square-foundation trigger. These are development source changes; `tools/release.txt` still pins
the owner's earlier client.

- **Verified:** independent source reviews and publish checks; genuine C# 7.3/net48 shared
  builds pass without warnings/errors. Actual-DLL runners pass 11,683 owner-approved physics
  checks and 4,069 audio checks across 550 generated clips.
- **Compiles but unverified:** the shared landing policy and authored waveform generator in
  native gameplay.
- **Still missing:** compilation of the complete changed client against its generated IL2CPP
  references, native ramp/seam/grind/revert/lifecycle checks and audible playback. Codex has run
  no Rust client, Shadow PC or live-server commands for this handover.

`SkateSfx` caches original generated roll, metal-truck and wooden-deck loops plus push, pop,
landing and bail shots. It crossfades truck/deck contact by grind kind, scales bounded
pitch/volume by speed, gives a new bail priority over concurrent events and owns its cleanup.
These are development source behaviors; native event delivery, mixed levels and audible quality
still need validation. [Sound profiles, licence and checks](../../../docs/RUST-SKATE-AUDIO.md).

## Rules

- It is used only on the owner's own server, which runs without anti-cheat. Rust is started for it
  through `RustClient.exe`, the executable that does not start the anti-cheat.
- The mod loader is in the Rust folder only while one session runs. Every way of starting a
  session here puts it there and takes it out again; see [the tools](tools/README.md#a-session).
  Rust must not be started normally while the loader is in its folder.
- The server address, game files, built binaries and logs stay out of this repository.

## Controls

### Controller

The layout of the skate. games. The plugin gets the controller itself from a reader outside the
game; see [The controller](#the-controller).

| Control | On the ground | In the air |
| --- | --- | --- |
| `Y` | Get on; again gets off. | |
| Left stick | Steer, by how far it is pushed. Far and straight forward or back: push or brake. | Held more than half way to a side: the rider turns that way for as long as it is held. |
| `A` | Push with the back foot. Rolling tail first, the push drives on backward (a fakie push). | |
| `X` | Push with the front foot (mongo). | |
| `A` tapped while the right stick is held back, then the flick | No comply: the front foot steps down and the board pops off the back foot, a little lower than an ollie ("No Comply 180" with the left stick). | |
| `LT` + `A`, `LT` + `X` or `RT` + `X`, `RT` + `A` | Foot plants: boneless, fastplant, beanplant (the front foot planted for a moment, a little higher than an ollie). | |
| `B` | Brake | |
| Right stick | The board: see below. Kept part of the way back or forward for 0.2 s: manual or nose manual. | Flicked to a side or a forward diagonal: a flip. Pulled back: set for the pop after the landing. |
| `LT`, `RT` | Held going down a transition and let go through its bottom: a pump (speed, by how long it was held and how steep the descent, once a pass; nothing on the flat or let go on the slope itself). | Held: grab; with the right stick held, another grab. |
| Right stick click | View from behind or the game's first person | |
| Left stick, right after landing a trick | Revert: the board turns round under the rider, who rolls on switch; it joins the combo. | |
| Right stick back or forward, on an edge | 5-0 or nosegrind instead of the 50-50. | |
| Coming down on an edge square to it, or slowly along it | A stall: the board stops on the edge. Square: a board stall, with the right stick back a tail stall, forward a nose stall, with `LB` or `RB` held a frontside or backside handplant; slowly along it a 50-50 stall. A pop leaves it; after 2.5 s the board rolls off. Out of a stall the rider goes back the way he came, tail first; out of a handplant he comes round and rolls back nose first. | |
| In a grind or a stall | The deck sits on the edge the way that grind does: a 5-0 on the back truck with the nose up, a nosegrind the other way round, a smith with the nose dipped past the edge, a feeble over it, a crooked on its edge; a tail stall hangs the tail over the coping. The rider stands over the low end with the arms out. | |

The right stick pops the board, and how it moves says what the board does in the jump. The sides
are those of a regular rider going nose first, whose heels are on the stick's left; riding tail
first they are the other way round.

| Right stick | Trick |
| --- | --- |
| Held back, flicked forward | Ollie, stronger the longer it was held, up to 0.3 s |
| Held forward, flicked back | Nollie |
| Held back, flicked to the forward left or right | Kickflip, heelflip |
| Held back, rolled a quarter round the rim to the left or right | Pop shove-it, frontside pop shove-it |
| Held back, rolled half way round | 360 shove-it |
| Held at the back right or back left, flicked straight across to the opposite front | Varial kickflip, varial heelflip |
| From the right or left rolled round to the back, then flicked to the opposite front | 360 flip, laser flip |
| Held back, rolled a little to the left or right, flicked straight forward | Hardflip, inward heelflip |
| Held back, rolled half way round backside | Impossible |

An edge is taken when the rider comes down on it from an ollie: the way of travel within 60° of
the edge, at least 1 m/s along it, the feet from 0.15 m below its top to 0.5 m above, the edge
within 0.45 m to the side and 0.3 m ahead; the board's own angle is free. When an edge is near but
not taken, the log says why (`GRIND edge near, not taken (...)`). A trick popped riding switch is named Switch (a nollie then is a fakie ollie). A grind is named by
how the board meets the edge: along it a 50-50 (or a 5-0 / nosegrind with the stick), across it a
boardslide, lipslide, noseslide or tailslide by which part of the board is on the edge, at an angle
a crooked, smith or feeble grind; frontside when the edge is on the side the rider's chest faces.
Landing on a slope, the part of the fall that runs along the slope becomes roll.

A turn of the rider goes on for as long as the left stick is held and the next half turn can
still be reached before the ground; let go, the rider is brought round to the nearest half turn.
A turn with a shove the same way is a bigspin. Each rotation is only started when the jump lasts
long enough to finish it.

| Trigger | Right stick | Grab |
| --- | --- | --- |
| `RT` | at rest, up, down, left, right | Backside, seatbelt, nose, melon, method |
| `LT` | at rest, up, down, right | Frontside, tail, crail, stalefish |
| both | at rest, up, down | Double, nosebone, tailbone |

While a trigger is held in the air the right stick picks the grab and does not flip the board.
The steering turns the board at a rate; the view does not steer it.

**Park pieces on foot.** Server ShortcutSkate 0.3.3 lets an administrator select the next piece
with the stock wheel's "I need wood" gesture (halfpipe first); thumbs up cancels. Both gesture
names and the piece order are configurable. Chat names the selection and `skate.status` shows
it. Place one square foundation with the building plan within two minutes. A refused placement
keeps the selection for a retry; floor tiles do not trigger placement.

On foot, holding `Back` (the view button) shows the eight-piece menu. The D-pad selects, `A`
queues `skate.piece <name>` and `B` queues cancellation. On Windows `SkateConsole` sends the
F1/command/Enter/F1 sequence; if typing fails it attempts a clipboard fallback. A keyboard bind
also works (`bind f7 "skate.piece quarterpipe"`). `Y` in the menu does not mount the board.
Native gesture delivery and a standing park piece still need owner validation. Menu typing does
not reach the game: in the game the typed keys are reported sent but the F1 console never opens
(injected input appears to be ignored), so the menu's lines have to be typed by hand for now.

### Keys

| Key | On the ground | In the air |
| --- | --- | --- |
| `K`, or `Space` twice quickly | Get on. `K` again gets off. | |
| `W` | Push; rolling tail first, a fakie push | Tap: kickflip |
| `S` | Brake | Tap: heelflip |
| `A` / `D` | Carve | Held: the rider turns to the left / right |
| Mouse | The board turns toward the view | |
| `Space` | Ollie; on a grind or a stall, ollie off. With `Q` held: no comply; with `Z` held: boneless. `Ctrl` held when the board meets an edge square: a handplant | |
| `Shift` | Held while rolling: manual | |
| `Ctrl` or `C` | Held for a moment: get off | Held: grab |
| `L` | View from behind or the game's first person | |

Coming down over an edge while moving along it starts a grind. A turn is called frontside when it
brings the rider's chest round to the way he travels: to the left going nose first, to the right
going tail first. Tricks score when landed, grinds and manuals by the second; tricks
in a row multiply, and the combo is banked after a moment of plain rolling. Coming down faster than 12 m/s, or with a
rotation unfinished, is a bail: the combo is lost and the rider steps off. Water, death, a seat or
a vehicle also end the ride. Nothing is read from keyboard or controller while the cursor is free
(console, chat, inventory).

### The controller

Inside the game a controller is keys and a mouse: Steam Input stands between it and the game, and
no stick can be read there. A process that Steam did not start sees the controller itself.
[`tools/sticks.ps1`](tools/sticks.ps1) is such a process: every session starts it beside the
client, and it shares the controller's state (XInput) with the plugin in a named block of memory,
every change with the reader's own clock, so that a flick between two frames of the game is not
lost. The plugin feeds the right stick to the shared flick module change by change.

- The controller rides from the moment it is touched. The keyboard rides again when a riding key
  goes down while the controller has been at rest for half a second.
- Steam's layout for the owner's `RustClient` shortcut keeps sending keys and mouse for the same
  controller. Off the board that is what walks, looks and uses things, so the layout has to be one
  that sends keys and mouse (Steam's "Keyboard (WASD) and Mouse" template): with the layout
  "Gamepad" the controller does nothing in Rust. On the board the plugin ignores those keys while
  the controller rides; the game still gets them.
- While the camera is behind the rider, the item in the player's hands is kept out of its way.
- The reader's block is named `ShortcutSkatePad`; its layout is in `SkatePad.cs` and in
  `sticks.ps1` and has to be the same in both.

## The score display

Drawn with the engine's immediate GUI: the session's score at the top right, counting up when a
combo is banked; the open combo at the bottom centre, with its tricks in a row, their points, the
multiplier and a bar that runs out while the board rolls without a trick; the trick that just
landed (coloured by its kind: grinds cool, manuals and reverts warm, grabs green), a running manual
or grind by its name, or a bail in the upper middle; under the score the session's best combo, top
speed, longest grind and manual; at the bottom left the speed, above it the last five tricks landed
with their points (newest largest, gone five seconds after the last), and the controls for the first
seconds on the board. A client build keeps only the engine methods the
game itself calls, so the condensed system font, the flat panels and the bars are each tried once
and done without when they are missing; the plugin's log says which (`HUD ...`).

## Sources

[`sources.txt`](sources.txt) lists what is compiled into `ShortcutSkateClient.dll`. A bare path is
taken from the commit that is built. A line with a commit and a SHA-256 takes shared code from
that commit and must match the hash; that is for shared code that is ahead on another branch.

| File | Part |
| --- | --- |
| `Plugin.cs` | Entry point, steps, log. |
| `SkateRig.cs` | Finds the local player and its movement body, getting on and off, the per-frame order. |
| `SkateKeys.cs` | Keys and controller as one set of riding inputs; whose hands are on the ride. |
| `SkatePad.cs` | The controller's state from the reader outside the game; the right stick through the shared flick module. |
| `SkatePadLog.cs` | What a ride with a controller leaves in the log: every movement of the right stick, the keys Steam's layout sends, whether the stick turns the game's view. |
| `SkateRide.cs` | Ground, air, grind and bail movement; calls the shared trick module and, for the slope, the pump, the take-off and the landing, the shared ride physics (`mods/rust/shared/SkateRidePhysics.cs`, described in [`docs/RUST-SKATE-RIDE-PHYSICS.md`](../../../docs/RUST-SKATE-RIDE-PHYSICS.md)). |
| `SkateGrind.cs` | Samples the ground around the rider and asks the shared edge module for an edge. |
| `SkateBoard.cs` | The board, what it is drawn with and where it sits. |
| `RiderRig.cs` | Writes the shared rider pose onto the player model's skeleton; what is drawn in which view, the held item included. |
| `SkateCamera.cs` | View from behind. |
| `SkateHud.cs`, `SkateSfx.cs` | The score display and the sounds. |
| `SkatePieces.cs`, `SkateConsole.cs` | The park-piece menu on the controller and the console line it sends to the server (`SkateConsole.Send`, a console packet on the client's connection found by name in the network library) or, failing that, types for the player. |
| `Scenarios.cs` | Scripted checks in a world. |
| `SkateParks.cs` | Draws the authored park models over the server's park pieces (below). |

[`models.txt`](models.txt) lists the park models (`assets/authored/skate_*.glb`) the build copies
next to the plugin from the commit it builds; a commit without them builds without models.

## Park models

The server builds a park piece from native floor tiles and walls (the ShortcutSkate plugin's
layout, placed from the building plan). Within 80 m of the player, every three seconds, the
client looks at the building blocks on the construction layer and recognises those assemblies by
their tiles' prefabs, pitches and spacing: a bank is one tile at 12°, a kicker one at 25°, a
quarterpipe a 15° tile and a 35° tile, a funbox a flat tile between a 15° tile either side, a
halfpipe its 36 parts; a ledge is a tile 0.4 or 0.8 m above a foundation with nothing else of a
building (ordinary floors never sit that close over a foundation), a long ledge three of them.
Over each assembly the matching model from the ramp pack is drawn (its `Visual` scene) and its
`CollisionProxies` scene becomes static colliders, so the ride runs on the model's smooth
surface; the model sits 2 cm above the native tiles. The native tiles' renderers are switched
off under it. The server keeps its own blocks: they are its collision and what unmodded clients
see; nothing is sent. A demolished piece takes its model with it. The bank, kicker, ledges and
funbox models match the native assemblies; the quarterpipe and halfpipe models are circular
(2.5 m) and shorter than the native 15°/35° facets, so they are placed by their entry (the
quarterpipe) and their flat (the halfpipe) and do not yet cover the native tiles. Without the
`.glb` files next to the plugin the pieces are found and nothing is drawn; the log says which
file is missing. Unverified in game.

### Steps

The plugin reads a comma-separated list from `BepInEx\plugins\steps.txt` when it loads; without the
file it runs `skate`. Runtime steps share one rig; the last scenario step selects the scenario.

| Step | Effect |
| --- | --- |
| `skate` | The mod as the owner plays it. |
| `ride` | Scripted: ride out, brake, turn round, ride back, get off. |
| `pose` | Scripted: rider and board in each state, standing still, from fixed cameras, then the score display in nine made-up states; `K` steps on. |
| `trick` | Scripted: ollie, kickflip, heelflip, both spins, a grab, two presses that come too late and a manual. |
| `pad` | Scripted: the ride with a controller, played into the plugin without one. |
| `status` | Scripted: a short ride, then the server's own view of the rider typed into the F1 console (`skate.status`, its speed and fly checks, its plugins), the console left open. |
| `park` | Scripted: the authored park models one at a time 6 m ahead of the rider, 20 s each, seen from behind the rider, then a ride up the bank; nothing is placed on the server. |
| `send` | Scripted: a chat line and `skate.status` sent to the server as console packets on the client's own connection (the way the game's console sends them), not typed; the chat line shows on the screen when it arrives. |
| `mute` | Turns the whole client down until nothing can be heard. |
| `nogrind` | No grinding. |

A scripted check writes what happened to `BepInEx\plugins\skate.log` and ends the session.

## What it relies on in the game

The client's own code is obfuscated and renamed with every build. The plugin uses only Unity's
engine API, the type names of game components (`BasePlayer`, `PlayerWalkMovement`, `PlayerModel`)
and Unity's message names. Each of the following was established in the running game and can
change with a Rust update:

- The local player is moved by a separate object with the walk component, a dynamic Rigidbody and
  a capsule. Velocity written after the game's own fixed step moves the player; the walk component
  has to stay enabled.
- The player model is a humanoid; its bones are taken from the Animator. A pose written after the
  game's animation in `LateUpdate` is what gets drawn.
- The local player's full body is set to cast shadows only, and a second set of meshes whose names
  start with `leg-` is what first person shows. The view from behind swaps the two.
- Only the main camera carries the game's image effects, so the view from behind moves it.
- `Rust/Standard` is the game's lit shader and can be found by name. A mesh the plugin makes and
  draws with it takes the world's light and casts a shadow; its colours come from a texture
  (`_MainTex`), its gloss from `_Glossiness`. `Hidden/Internal-Colored` draws vertex colours without
  light and is what the board falls back to.
- The board is the shared mesh's detailed form (`SkateBoardMesh.CreateDetailed`): a deck, two
  trucks hanging from it and four wheels hanging from the trucks, each an object of its own about
  its pivot, with the 256 × 256 texture the mesh brings (grip tape, a graphic underneath, metal,
  wheels). The trucks turn with the lean, the front one into the turn and the rear one out of it,
  0.8° per degree; the wheels roll with the speed. Without the parts the one-piece mesh is drawn,
  without a mesh at all a board of primitives.
- A controller is not visible to the plugin as a gamepad; keys and mouse buttons are read from the
  Input System, the controller from outside the game.
- The item in the player's hands is drawn by an object with a `BaseViewModel` component. Switching
  its renderers off does not hide it; scaling that object down every frame does.
- On a server without `ShortcutSkate`, in the scripted checks, where nothing touches mouse or
  keyboard, the first ride after waking is put back several times during its first two to three
  seconds, also at walking pace and also when it starts eight seconds after waking. Checks whose
  first movement came forty seconds or more after waking were not put back, nor was a second ride.
  The cause is not known.
- Engine methods the game never calls can be missing from the client and throw when called.

## Playing it: the owner's launcher

[`tools/launcher.ps1`](tools/launcher.ps1) builds the commit named in
[`tools/release.txt`](tools/release.txt) and puts two files on the desktop of the PC:

- **Rust Skate** starts Steam when it is not running, puts the loader and the plugin into the Rust
  folder, starts Rust through the owner's Steam shortcut, and takes everything out again when Rust
  is closed. Its window has to
  stay open while playing. A hidden helper does the same clean-up half a minute after Rust closes
  if that window was closed first.
- **Rust Skate - remove mod files** takes the loader out of the Rust folder by hand, for example
  after a power cut.

The launcher downloads nothing and builds nothing; it plays the plugin that was built when it was
installed. After a Rust update it refuses to start and says so, until the mod has been
[prepared for the new build](tools/README.md#after-a-rust-update). To remove everything, delete
the two desktop files and the folder `Downloads\claude-loader-probe`.

On the server, [`ShortcutSkate`](../plugins/SKATE.md) accepts the rider's speed and supplies a
practice area. The client asks it for nothing: the plugin accepts a rider by itself after a moment
of ordinary movement. Without it the server allows a rider the pace of a player who walks, about
10 km/h and a little more downhill, and puts a faster one back. The client never slows the board
for that: a put-back is only counted and logged. Only a wall right in front takes speed off. So on
a server that refuses skate speed the rider is pulled back instead of braked; the server has to
allow it (the plugin, or its own speed checks turned off by the admin).
