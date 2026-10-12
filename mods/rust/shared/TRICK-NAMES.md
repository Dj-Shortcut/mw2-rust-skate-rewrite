# Trick naming contract

Use the full `SkateTrickInput` overload with trailing `late,bodySpin,tailLeads`.
Capture the physical leading end while still rolling: `tailLeads = TailFirst != DeckReversed`.
The scorer latches it with `State.Switch` at the first airborne step; subsequent flags,
pop-kind changes or snapped landing stance do not rename the flight. Support/bail clears
the latch. Old overloads retain historical names; they cannot distinguish the deck end.

This mod's prefix is chosen from the captured scorer half-turn flag and physical end:

| PopSwitch | Leading deck end | Prefix |
|---|---|---|
| false | nose or tail | none |
| true | nose | Switch |
| true | tail | Fakie |

Pass the actual pop end: tail is `Ollie`, nose is `Nollie`, independently of travel.
These tables cover regular and goofy equally after the adapter normalizes the axes.

| Prefix | Pop end | no rotation | Spin +180 | Spin -180 | Spin +360 | Spin -360 |
|---|---|---|---|---|---|---|
| none | tail | Ollie | Frontside 180 | Backside 180 | Frontside 360 | Backside 360 |
| none | nose | Nollie | Nollie Frontside 180 | Nollie Backside 180 | Nollie Frontside 360 | Nollie Backside 360 |
| Switch | tail | Switch Ollie | Switch Frontside 180 | Switch Backside 180 | Switch Frontside 360 | Switch Backside 360 |
| Switch | nose | Switch Nollie | Switch Nollie Frontside 180 | Switch Nollie Backside 180 | Switch Nollie Frontside 360 | Switch Nollie Backside 360 |
| Fakie | tail | Fakie Ollie | Fakie Frontside 180 | Fakie Backside 180 | Fakie Frontside 360 | Fakie Backside 360 |
| Fakie | nose | Fakie Nollie | Fakie Nollie Frontside 180 | Fakie Nollie Backside 180 | Fakie Nollie Frontside 360 | Fakie Nollie Backside 360 |

Axes are semantic: positive Spin/Shove is frontside, negative is backside; positive Flip
is kickflip, negative is heelflip. Flip direction follows toes/heels, not travel. The
adapter supplies grind frontside when the edge is on the toe side on approach. The scorer
does not infer world-space directions from `tailLeads` or apply a second goofy mirror.

For the current client, let `Q=Switch`, `R=(Speed < -0.3)`, `D=DeckReversed`, `G=Goofy`.
Rider-relative `T=Q xor R`; physical tail-leading `L=T xor D` chooses the prefix above.
Normalize with `K=(-1)^(G xor T)`; world yaw is `-normalizedAngle*K` (positive yaw turns right).
`D` does not reverse this rider/chest direction: a shove turns the deck under unchanged feet.

| Q | R | D | T | L | Prefix | Frontside yaw, regular | Frontside yaw, goofy |
|---|---|---|---|---|---|---|---|
| 0 | 0 | 0 | 0 | 0 | none | negative / left | positive / right |
| 0 | 0 | 1 | 0 | 1 | none | negative / left | positive / right |
| 0 | 1 | 0 | 1 | 1 | none | positive / right | negative / left |
| 0 | 1 | 1 | 1 | 0 | none | positive / right | negative / left |
| 1 | 0 | 0 | 1 | 1 | Fakie | positive / right | negative / left |
| 1 | 0 | 1 | 1 | 0 | Switch | positive / right | negative / left |
| 1 | 1 | 0 | 0 | 0 | Switch | negative / left | positive / right |
| 1 | 1 | 1 | 0 | 1 | Fakie | negative / left | positive / right |

Backside yaw is the opposite sign; reader `Mirrored=G xor T`. Without yaw/shove, Flip +360
names Kickflip and -360 Heelflip, with the same stance/pop prefix. A half Spin plus Kickflip
uses Frontside/Backside Flip. At rest or slower reverse drift, R is the client's fallback bit.

Examples: a plain 180 makes the next tail-leading pop Fakie; an additional half shove or
a body varial can make the nose lead with `Switch=true`, producing Switch. A shove alone
keeps `Switch=false` and has no stance prefix under this mod's agreed convention.
Late/body/grab modifiers retain the captured prefix: `Fakie Late Kickflip`,
`Fakie Nollie Body Varial Kickflip`, `Switch Backside 180 Grab`. Catch tolerance remains
25 degrees around snapped half Spin/Shove/body turns and whole Flip turns. Names are cached;
scores, angle outputs and stance parity are unchanged. Scorer checks do not verify native
controller direction, physical pop/foot contact, HUD display or gameplay acceptance.
Client source still needs the explicit overload and presentation integration: visual roll
does not yet mirror goofy/deck reversal, and the ascent pop pose does not distinguish Kind.
