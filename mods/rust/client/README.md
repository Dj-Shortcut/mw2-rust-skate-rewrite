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

Client 0.18.22 retries complete larger park assemblies before retaining smaller matches. A
MEGA that first streamed as a bank or kicker can become the full round halfpipe when all its
members arrive. Replacement prepares its surface first, preserves renderer/backing ownership,
and waits if the rider intersects the new surface. Regular-halfpipe backing isolation selects
only transition floors 4–11, preserving native flat, deck and wall collision.
Partial MEGA recognition also withdraws old, wholly contained provisional overlays after the
rider clears their surface and native parts; occupied overlays remain until a safe retry.
A matched kit whose preparation fails keeps its members reserved against smaller matches.

Rising contact on a verified ramp face uses its contact normal for wall classification,
landing and gap closure. Side edges, undersides, coping and ordinary world walls remain
obstructions. The held item is restored during rig teardown as well as normal dismount.
Cached genuine IL2CPP-reference compilation and temporary exact-source regression checks pass;
deployment, the complete visible server-placed MEGA and paired native riding acceptance remain
open. These checks do not establish the actual capsule/network-root relationship or Skate feel.

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
- **Still missing:** the installed-game build and visible/native ramp, seam, grind, revert,
  lifecycle and audible-playback checks. The cached-reference client compile does not replace
  those runtime checks.

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
| Left stick | Steer, by how far it is pushed. Far and straight forward or back: push or brake. | Held still more than half way to a side (for at least 0.06 s): the rider turns that way for as long as it is held. One full circle round the rim, after a pop, within a second: a body varial, the rider turns a half round and the board does not; the landing is switch. A stick travelling round the rim (faster than 180° a second) is never read as a held side, so the circle asks for no turn however easy its pace. |
| `A` | Push with the back foot (pushing takes the board to 11 m/s; slopes take it on to 25 m/s, 90 km/h, over the ground, the limit the server plugin 0.3.7 is configured for): it comes down about 0.19 m beside the board on the toe side, the leg nearly straight, and sweeps back while the body dips a little; the foot stays where it came down while the board rolls on. Rolling tail first (a rollback, after a revert or a 180), the back foot is at the leading end and pushes from there, a fakie push, and the push drives on the way the board rolls. | |
| `X` | Push with the front foot (mongo). | |
| `A` tapped while the right stick is held back, then the flick | No comply: the front foot steps down and the board pops off the back foot, a little lower than an ollie ("No Comply 180" with the left stick). | |
| `LT` + `A`, `LT` + `X` or `RT` + `X`, `RT` + `A` | Foot plants: boneless, fastplant, beanplant (the front foot planted for a moment, a little higher than an ollie). | |
| `B` | Brake; with the left stick held to a side at more than walking pace, a powerslide: the board swings across the way of travel and scrubs speed until one of them is let go | |
| Right stick | The board: see below. Kept part of the way back or forward for 0.2 s: manual or nose manual. | Flicked to a side or a forward diagonal: a flip; after a pop, a whole composition (held back then flicked, or rolled) is a late flip or late shove on top of what is under way, when the jump still lasts long enough. Pulled back: set for the pop after the landing. |
| `LT`, `RT` | Held going down a transition and let go through its bottom: a pump (speed, by how long it was held and how steep the descent, once a pass; nothing on the flat or let go on the slope itself). | Held: grab; with the right stick held, another grab. |
| Right stick click | View from behind or the game's first person | |
| `Back` held on the board | The controls panel at the bottom left, as in the first seconds on the board, for as long as it is held. On foot `Back` opens the picker. Not yet seen in game. | |
| Left stick swept half round its rim, within 0.6 s of landing a trick (as in the skate. games; a hard steer alone is a carve) | Revert: the rider and the board pivot a half turn together on the ground and roll on the same way, the other end of the deck leading (fakie after a regular landing, regular again after a fakie one); it joins the combo. | |
| Right stick back or forward, on an edge | Along the edge: 5-0 or nosegrind instead of the 50-50. Across it on the tail, held back: bluntslide instead of the tailslide; on the nose, held forward: nosebluntslide. Let go, the slide goes back to the plain one. | |
| A flip asked for coming down onto an edge, with room for half of it | Darkslide: the board is caught upside down on the edge, griptape on it; a pop finishes the flip the same way round (a kickflip lands as a kickflip); rolled off without a pop, the board comes down upside down and it is a bail. | |
| Coming down on an edge square to it, or slowly along it | A stall: the board stops on the edge. Square: a board stall, with the right stick back a tail stall, forward a nose stall, with `LB` or `RB` held a frontside or backside handplant (the board goes from where it rode to the feet as the body goes up, and back as it comes down); slowly along it a 50-50 stall. A pop leaves it; after 2.5 s the board rolls off. Out of a stall the rider goes back the way he came, tail first; out of a handplant he comes round and rolls back nose first. | |
| In a grind or a stall | The deck sits on the edge the way that grind does: a 5-0 on the back truck with the nose up, a nosegrind the other way round, a smith with the nose dipped past the edge, a feeble over it, a crooked on its edge, a bluntslide stands the deck up steeply on the tail (a nosebluntslide on the nose); a tail stall hangs the tail over the coping. The rider stands over the low end with the arms out. | |

The right stick pops the board, and how it moves says what the board does in the jump, laid out as
in the skate. games: the sides below are those of a regular rider going nose first; riding tail
first, or goofy, they are the other way round.

| Right stick | Trick |
| --- | --- |
| Held back, flicked forward | Ollie, stronger the longer it was held, up to 0.3 s |
| Held forward, flicked back | Nollie |
| Held back, flicked to the forward right or left | Kickflip, heelflip |
| Held back, rolled a quarter round the rim to the right or left | Pop shove-it, frontside pop shove-it |
| Held back, rolled half way round | 360 shove-it |
| Held at the back left or back right, flicked straight across to the opposite front | Varial kickflip, varial heelflip |
| From the left or right rolled round to the back, then flicked to the opposite front | 360 flip, laser flip |
| Held back, rolled a little to the right or left, flicked straight forward | Hardflip, inward heelflip |
| Held back, rolled half way round backside | Impossible |

An edge is taken when the rider comes down on it from an ollie: the way of travel within 60° of
the edge, at least 1 m/s along it, the feet from 0.15 m below its top to 0.5 m above, the edge
within 0.45 m to the side and 0.3 m ahead; the board's own angle is free. When an edge is near but
not taken, the log says why (`GRIND edge near, not taken (...)`). A trick popped after a half turn is named by which end of the deck leads: Fakie when the tail does (after a plain 180, or a revert), Switch when the nose does (a 180 and a half shove, which turns the deck round under the feet; a body varial too); a nollie keeps its name (Fakie Nollie, Switch Nollie). The speed meter says FAKIE or SWITCH to match. A grind is named by
how the board meets the edge: along it a 50-50 (or a 5-0 / nosegrind with the stick), across it a
boardslide, lipslide, noseslide or tailslide by which part of the board is on the edge (a bluntslide
or nosebluntslide with the stick held back or forward), at an angle a crooked, smith or feeble grind; frontside when the edge is on the side the rider's chest faces. On the edge the board keeps the angle it came down with (squared to the edge for a 50-50), and off the end it comes round to the way of travel over a moment (540° a second), not in a jump; a stall stops the board on the edge as it came, square across it.
Landing on a slope, the part of the fall that runs along the slope becomes roll.

A turn of the rider goes on for as long as the left stick is held and the next half turn can
still be reached before the ground; let go, the rider is brought round to the nearest half turn.
A turn with a shove the same way is a bigspin. As in Skate, a flip, shove-it, body varial or turn
asked for too late is started all the same and does not come round before the ground: a bail (the
log keeps each as `SKATE late ...`, which `zz last` sums up by kind); so is a grab still held as
the wheels touch down. Late flips and shoves and body varials are named by the shared scorer
("Late Kickflip", "Late Pop Shove-it", "Body Varial"); a body varial of a half round leaves the
deck the other way round under the feet, as a shove does. Unverified in game.

| Trigger | Right stick | Grab |
| --- | --- | --- |
| `RT` | at rest, up, down, left, right | Backside, seatbelt, nose, melon, method |
| `LT` | at rest, up, down, right | Frontside, tail, crail, stalefish |
| both | at rest, up, down | Double, nosebone, tailbone |

While a trigger is held in the air the right stick picks the grab and does not flip the board.
The steering turns the board at a rate; the view does not steer it.

**Park pieces on foot.** Server ShortcutSkate 0.3.3+ lets an administrator select the next piece
with a stock-wheel gesture ("I need wood" by default; the owner's build does not have that name,
so `skate.piece <name>` in F1 is the working route there) and place a square foundation within
two minutes; thumbs up cancels. Gesture names and the piece order are configurable.

**Park items and the picker** (owner's choice, 2026-10-13: own build picker and hotbar items).
The server (Codex's ShortcutSkate 0.3.11) gives eight park items, building plans marked with their
kind ("Skate Bank" … "Skate Halfpipe"); one is placed with the game's own building controls as a
square foundation (left click, R turns it) and the server builds the piece in its place. The
client sends nothing for this: it cannot (no console packet path in this build; typed keys never
reach the console). While a park item is held, its piece is drawn as a ghost where the player aims,
facing the view. The picker (on foot: hold `Back` on the controller, D-pad chooses, `A` takes, `B`
closes; on the keyboard `P` opens and steps, `O` takes, `Backspace` closes) puts the chosen piece's
item in the player's hand through the game's own belt (`SelectSlot`, found by shape and logged as a
`HELD` line); without one in the belt it says so, and where the belt cannot be selected it names the
number key to press. Seen in game: the client has no readable held item, so no ghost is drawn
(an unidentified building plan is never shown as a park piece). Not yet seen in game: the belt
selection, the ghost against the native foundation ghost, and the server building the piece.

A server line can reach the client without a hook only as text: `SkateInbox` reads the BepInEx log
(`LogOutput.log`, written with `UnityLogListening` on: `sv plisten 1` before `zz play`) and logs every
line carrying `SKATE-PROBE` as an `INBOX` line. It acts on nothing; it shows whether a server
`echo` to this client arrives at all.

### Keys

| Key | On the ground | In the air |
| --- | --- | --- |
| `K`, or `Space` twice quickly | Get on. `K` again gets off; stepped off while rolling, the board rolls on by itself and comes to rest ahead, where it lies for a while. | |
| `W` | Push with the back foot, whichever end leads: rolling tail first (a rollback, after a revert or a 180) it is at the leading end and pushes from there, a fakie push | Tap: kickflip |
| `S` | Brake; with `A` or `D` held at more than walking pace, a powerslide: the board swings across the way of travel and scrubs speed until the keys are let go (to the left: frontside, the chest to the front; to the right: backside) | Tap: heelflip |
| `A` / `D` | Carve | Held: the rider turns to the left / right |
| Mouse | The board turns toward the view | |
| `Space` | Ollie; on a grind or a stall, ollie off. With `Q` held: no comply; with `Z` held: boneless. `Ctrl` held when the board meets an edge square: a handplant | |
| `Shift` | Held while rolling: manual | |
| `X` | | Tap: body varial, the rider's half round without the board; the landing is switch |
| `Ctrl` or `C` | Held for a moment: get off | Held: grab |
| `L` | View from behind or the game's first person | |
| `H` held | The controls: on the board the panel at the bottom left, on foot the line that says how to get on and which stance is set. Held, never tapped, so a stray press leaves nothing on the screen. Not yet seen in game. | |
| `F2` held for a second (on foot; on a Mac keyboard `fn`+`F2`; `Back`+`Y` held on the controller) | Stance: regular (left foot forward, the right foot pushes) or goofy, the mirror image. A tap changes nothing: a stray press of Y in the menu, or of J, used to switch the stance unseen; J is the game's own clan key, whose screen frees the cursor, so the switch has its own key. Kept in `skate-stance.txt` in the game's own data folder (`AppData\LocalLow\Facepunch Studios LTD\Rust`) for the next start; the hint on foot names the current one. | |

Coming down over an edge while moving along it starts a grind. A turn is called frontside when it
brings the rider's chest round to the way he travels: to the left going nose first, to the right
going tail first, and the other way round for a goofy rider; the controller's flick and grab
directions mirror with the stance as they do when the tail leads. Tricks score when landed, grinds and manuals by the second; tricks
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

If the controller or reader is lost, or the active controller slot changes, pending gestures and
the previous left-stick sweep are discarded. Recovery takes the latest held state as a baseline without replaying buffered button
presses or time before that baseline. A right stick held away from center during recovery must
return through its rearm zone before it can start another gesture. Fresh presses and sweeps
then use the normal controls.
The reader and plugin must both use protocol 2 (`ShortcutSkatePad2`); update/restart both
when upgrading from protocol 1. The separate map name prevents mixing old/new adapters.
The header has magic/version at 0/4, count/slot at 8/12, microsecond heartbeat at 16,
controller ownership generation at 24 and publication generation at 28. The writer marks
publication odd before changing the ring/header and even after a memory barrier. The reader
accepts only matching even generations around its copy; a stalled publication expires by the
normal heartbeat timeout. Ownership changes re-prime even when the slot returns to the same
number between polls. A restarted reader clock also discards the old baseline.
Actual device/reader reconnection and controller feel still require in-game verification.

- The controller rides from the moment it is touched. The keyboard rides again when a riding key
  goes down while the controller has been at rest for half a second.
- Steam's layout for the owner's `RustClient` shortcut keeps sending keys and mouse for the same
  controller. Off the board that is what walks, looks and uses things, so the layout has to be one
  that sends keys and mouse (Steam's "Keyboard (WASD) and Mouse" template): with the layout
  "Gamepad" the controller does nothing in Rust. On the board the plugin ignores those keys while
  the controller rides; the game still gets them.
- The right stick reaches the game as the mouse, so a flick turns the game's own view. While the
  controller rides, the plugin puts that view back every frame, level and the way the camera
  looks (behind the rider: the way the chase camera looks; otherwise the way the board goes), so
  the game's compass follows the board rather than the flicks, and the view is not left far up
  or down for the step-off. The keyboard's view is left alone: there the mouse steers. Whether the
  compass follows has not been seen in the game yet; the log's `the game's view turned …` line
  is the measure.
- While the camera is behind the rider, the item in the player's hands is kept out of its way.
- The reader's block is named `ShortcutSkatePad2`; its layout is in `SkatePad.cs` and in
  `sticks.ps1` and has to be the same in both.

## The score display

Drawn with the engine's immediate GUI: the session's score at the top right, counting up when a
combo is banked; the open combo at the bottom centre, with its tricks in a row, their points, the
multiplier and a bar that runs out while the board rolls without a trick; the trick that just
landed (coloured by its kind: grinds cool, manuals and reverts warm, grabs green), a running manual
or grind by its name, or a bail in the upper middle; under the score the session's best combo, top
speed, longest grind and manual; at the bottom left the speed, above it the last five tricks landed
with their points (newest largest, gone five seconds after the last), and the controls for the first
seconds on the board, and again for as long as `H` (or `Back` on the controller) is held; their
last line says so. A client build keeps only the engine methods the
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
| `SkateWatch.cs` | A watch on what is drawn: a step of the board's angles or place, or of the rider's feet, hands or head, larger than a frame could carry is logged as `SKATE snap ...` with what was going on (at most 300 a session); `zz last` counts them by situation. |
| `SkateCamera.cs` | View from behind: 3.4 m back, up to 1.2 m further at top speed and in again as the board slows; with a controller it swings in behind the way the board goes. It comes in at once to this side of anything between it and the rider, and goes back out gradually once the way is clear. |
| `SkateHud.cs`, `SkateSfx.cs` | The score display and the sounds. |
| `SkatePieces.cs`, `SkateConsole.cs` | The park-piece menu on the controller and the keyboard, its ghost preview, and the console line it sends to the server (`SkateConsole.Send`, a console packet on the client's connection found by name in the network library); without that, the screen shows the line to type. `SkateConsole` also keeps the console typist for the scripted checks. |
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
funbox models match the native assemblies. The quarterpipe and the halfpipe are not drawn from
their authored models (circular, 2.5 m, shorter than the native tiles, which stood out of them):
they are made in the client on the tiles' own tops, a 15° and a 35° face from the flat (a
halfpipe both ways from its 6 m flat, with its 3 m decks), the two bends rounded with a 3 m radius
so the board rolls through them, the coping edge left sharp, nowhere under a tile and at most
about 10 cm above one; the riding surface is their collider, and their colour is the authored
model's when it is there. Without the `.glb` files next to the plugin the other pieces are found
and not drawn; the log says which file is missing. Unverified in game.

The rider on the vertical (0.18.27): the rider stands square to the deck all the way up a
transition, as the board does. Before, the rider's frame went back to the world's up from about 78
degrees on while the board kept following the face, so on the vertical part of a halfpipe the body
stuck out sideways from a board standing upright. The nose's way on a steep face is now laid on
the face with the ride's own tangent rule (`SkateBoard.OnFace` uses `SkateRide.SurfaceTangent`),
for the board, its pitch and the rider alike. The part into the face is weighted as movement is,
so a diagonal up a steep face keeps the drawn board aligned with the ride. A nose pointed straight
into a vertical wall points up the wall instead of having no way at all. This retains the MEGA,
contact, held-item and tour changes in 0.18.26. Native compilation and a real ride straight and
diagonally up a vertical face remain to be verified for 0.18.27.

Riding a transition (0.18.18, the client side of #487): on a transition's own collider the ground
is read along the face's normal, from the centre of the body's bottom half sphere, rather than
straight down. Straight down, the ground under the body was lost from about 53 degrees on (the gap
below grows with the slope), and the ride flapped between ground and air up every transition; and
at 68 degrees the board was turned straight up, a 22-degree kink in its way. Now the board keeps
the face to the lip, where it stands vertical, with gravity along the face the whole way (past 72
degrees by the client's own rule, since the shared physics takes no ground steeper than that), and
leaves the lip with the pace it had along the face: up as much as the face stood up, the pace
along the coping kept on a diagonal line (past 72 degrees the shared frame's rise factor is held
where it was, so a carve up the wall is not bent into a vertical launch), a pop added only when
one was asked for (also one asked for in the last moment on the face), and comes back down onto
the face, rolling back fakie (a face steeper than 72 degrees is landed by the client's own rule:
the fall's part into the face is the impact, the rest rolls), or onto the deck when the way led
over it. Over a transition the air step reads its gap to the face along the face's normal. The
coping on the lip is not the face: met by the contact ray, the face has ended. The log says
`SKATE vert: off the lip at … deg, … m/s up, … along`. Only the transitions the client makes
itself (quarterpipes, halfpipe, MEGA, half-wall kicker) are read this way; banks, kickers, ledges
and the world are as before. Checked offline in a 2D model of the rule on a 1.2, 3 and 9.59 m
circle (the ground kept to 88–90 degrees, a 7 m/s board on the 3 m quarterpipe stalling at 66
degrees and rolling back) and the tangent rule against the shared formula up to 72.5 degrees; not
compiled, not seen in the game.

### Steps

The plugin reads a comma-separated list from `BepInEx\plugins\steps.txt` when it loads; without the
file it runs `skate`. Runtime steps share one rig; the last scenario step selects the scenario.

| Step | Effect |
| --- | --- |
| `skate` | The mod as the owner plays it. |
| `ride` | Scripted: ride out, brake, turn round, ride back, get off. |
| `pose` | Scripted: rider and board in each state, standing still, from fixed cameras, then the score display in nine made-up states; `K` steps on. |
| `trick` | Scripted: ollie, kickflip, heelflip, both spins, a grab let go before the landing, a manual, shove-its, varial and 360 flips, an impossible, a revert, a fakie nollie, a no comply, a boneless and a body varial; every name is logged as the scorer gives it, for the screen check of the names. |
| `pad` | Scripted: the ride with a controller, played into the plugin without one. |
| `status` | Scripted: a short ride, then the server's own view of the rider typed into the F1 console (`skate.status`, its speed and fly checks, its plugins), the console left open. |
| `park` | Scripted: the authored park models one at a time 6 m ahead of the rider, 20 s each, seen from behind the rider, then a ride up the bank; nothing is placed on the server. |
| `send` | Scripted: a chat line and `skate.status` sent to the server as console packets on the client's own connection (the way the game's console sends them), not typed; the chat line shows on the screen when it arrives. |
| `place` | Scripted: `skate.place bank` sent to the server the same way, where the player looks; then the server's blocks and the model drawn over them are waited for (20 s) and reported. Needs ShortcutSkate 0.3.6 or later and open ground in view; says so when nothing came. |
| `tour` | Scripted: rides to each server park piece the client found near the player and across it (ledges from beside them, with an ollie), and logs a `TOUR` line per piece: rise, air, grinds, bails, put-backs. |
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
  game's animation in `LateUpdate` is what gets drawn. At binding the rig measures limb handedness
  from mesh bind poses and the renderer's transform determinant, independently of the current
  animation. A mirrored mapping swaps the limb bones. Missing, degenerate or inaccessible bind
  geometry retains the baseline humanoid mapping and reports that fallback during binding.
  Per-push diagnostics are not written. Installed-client bind-pose availability and visual
  regular/goofy correctness remain unverified.
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
for that: a put-back is only counted and logged. What does take speed off is being held back: when
for three physics steps in a row the board got less than a third of what it rode (a wall, a post, a
fence the rays miss), on the ground or along an edge, its speed comes down to what it actually did
(measured along the whole path, so that a steep transition, where little of a step is horizontal,
is not taken for a wall)
plus a little, the meter with it, and a grind held back that way ends with the board coming down
at that pace; a wall seen right in front stops it at once. The owner saw the meter run on against a
wall and on a blocked grind before this (12 Oct); unverified in game since. So on
a server that refuses skate speed the rider is pulled back instead of braked; the server has to
allow it (the plugin, or its own speed checks turned off by the admin).

The owner still saw rubber-banding with ShortcutSkate 0.3.5 live (12 Oct). The plugin lets a rider
through only while its session is armed, and any server correction, more than two seconds without
ground under the path, or a steep slope disarms it, after which the next fast move is refused and
the correction disarms it again: that loop is the likeliest cause, and is with Codex (#337). On a
private server the admin's own setting ends it without the plugin: `antihack.speedhack_protection 0`,
`antihack.flyhack_protection 0` and `antihack.noclip_protection 0` in the server's configuration
(noclip as well, because the client's park colliders are not the server's native blocks).

Only a client that Steam started gets the controller through Steam Input. `play` and the launcher
start through the owner's Steam shortcut for that reason; a client started directly (`$pdirect`,
the scripted checks) rides with the controller through the plugin's own reader but walks with the
keyboard only, which the owner saw as "the controls stop working when I step off".

## A friend's PC

The owner's direction of 14 October: his friends should be able to try the mod. `zz pack` on the
owner's PC zips the installed launcher (loader, interop set, runtime, plugin, models, scripts,
server address) with an installer; a friend unpacks it, makes the `RustClient` Steam shortcut and
runs `Install Rust Skate.cmd`, and plays from the same two desktop files as the owner. Steps,
limits and what has and has not been seen are in [FRIEND.md](FRIEND.md). Code present and dry-run
on stand-in files only; no pack has been made on the owner's PC or installed on a second PC yet.
