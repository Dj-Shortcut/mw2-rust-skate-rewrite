# Skate grab pose data

This is a proposed animation specification for the existing Rust skate mod. The grip identities below use public skateboarding teaching references. Every angle, distance, knee range and timing target marked **proposal** is original authored data for this project, not a measurement of a skater or an animation extracted from a game. These values have not been applied or visually verified in the Rust client.

The source audit refers to revision `d07f0a6`. The relevant files are [the grab enum](../mods/rust/shared/SkateTricks.cs), [shared rider poses](../mods/rust/shared/SkateRider.cs), [the board mesh](../mods/rust/shared/SkateBoardMesh.cs), [board rendering](../mods/rust/client/src/SkateBoard.cs), and [the rider adapter](../mods/rust/client/src/RiderRig.cs). This document changes none of those files.

## Hands, stance and board ends

Front hand/foot (**F**) and back hand/foot (**B**) are roles in the resolved stance. Regular puts the left foot forward; goofy puts the right foot forward. Switch uses the opposite stance, while fakie is backwards travel without that stance change. Consequently, the sign of velocity is not sufficient to choose the grab hand. [CCS stance guide](https://shop.ccs.com/pages/a-beginners-guide-to-skateboarding-tricks-part-1).

| Resolved stance | F hand and foot | B hand and foot |
|---|---|---|
| Regular, normal | Left | Right |
| Goofy, normal | Right | Left |
| Regular, switch | Right | Left |
| Goofy, switch | Left | Right |

The proposed mapping assigns the named nose grip to F and tail grip to B after resolving stance and the named board ends. Bind the grip to that end when acquired and transform it with the displayed board; do not exchange hands or grip points halfway through a shove, spin or velocity reversal. A physical mesh's `+Z` end is not automatically the gameplay nose after a board reversal. The adapter must explicitly map named nose/tail to the displayed mesh ends.

This is a proposed mapping, not a description of complete switch support today. The current shared `Grip` uses Switch-aware F/B roles for side grabs and Crail/Seatbelt, but stance-only hands for Nose/Tail/Nosebone/Tailbone. The current adapter supplies `SkateStance.Regular` and uses `SkateRide.TailFirst` as its Switch input. Those inputs distinguish motion within the current controller; they do not establish all four real stance cases above.

EA documents left/right triggers grabbing with the corresponding hand, and identifies its Classic control lineage with Skate 1–3. That is useful input vocabulary for this mod; its public guide supplies no bone transforms, grip coordinates or tweak degrees. The proposal here uses no game animation files. [EA, Get Control](https://www.ea.com/games/skate/skate/news/get-control).

## Coordinates and actual rotation signs

All distances below are metres and all proposed angles are degrees. Let `U` be normalized board/support up, `N` the normalized `NoseYaw` direction projected onto its plane, and `R = Cross(U, N)`. Neutral `U=(0,1,0)` and `N=(0,0,1)` give `R=(1,0,0)`. Let `T` be the rider's anatomical toe-side direction after resolving stance; heel side is `-T`. Keep those names separate from the direction of travel.

The current board expression is:

```csharp
Turn(-grabRoll * GrabFold, N) * Turn(-grabPitch * GrabFold, R)
```

It is multiplied onto the displayed board rotation. The pitch applies first, then the roll. With the actual `Turn` quaternion and Unity's quaternion-vector rule, positive raw `grabRoll` lowers the `+R` edge; negative raw roll raises it. Positive raw `grabPitch` raises the `+N` end. In the neutral regular frame, a point `aR + bN` gains height `-a sin(roll) + b sin(pitch) cos(roll)`. This is source algebra, not an observed client result. [Unity public Quaternion implementation](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Runtime/Export/Math/Quaternion.cs).

The proposal table uses **toe-up** as a semantic angle: positive raises the anatomical toe edge, negative raises the heel edge. Convert it to the current raw roll as `-toeUp * sign(Dot(T, R))` when those axes are aligned. Neutral regular gives `rawRoll=-toeUp`; its mirrored goofy frame gives `rawRoll=+toeUp`. Resolve the switch/end frame first rather than applying an extra sign change based on `TailFirst`. The nose-up proposal likewise needs the named-end-to-`N` mapping; reversing velocity alone does not reverse pitch.

The current comment describes raw roll as toe-up, but the expression has the sign above. Also, `GrabTweak` uses `NoseYaw`, whereas the base rendered board uses `DeckYaw`, which includes under-foot shove and reversal. A future contact solver should derive the hand and foot targets from the same final board transform that rendering uses.

## Grip definitions

The SKD judge manual distinguishes the core front/rear hand and edge/end families. The following table paraphrases those identities and adds the separately linked teaching definitions where needed. `Backside` and `Frontside` remain the project's enum names; they do not force a body spin. In real air terminology the Indy grip during a frontside air is named a frontside air. [SKD judge manual, pages 24–25](https://skateboarddeutschland.de/wp-content/uploads/2020/07/Judge-Manual-SKD-05-07-2020-lowres.pdf#page=24), [Indy teaching definition](https://www.skateboardhere.com/indy-grab.html).

| Enum value | Proposed hand identity | Board grip and recognizable feature | Basis |
|---|---|---|---|
| `None = 0` | Neither | No hand contact; normal balance or landing pose | Project state |
| `Legacy = 1` | B | Preserve the existing generic toe-side grab and old bool-call behaviour | Project compatibility, not a separate real trick name |
| `Backside = 2` | F | Heel edge between the feet; neutral heel-side air | SKD melon/heel grip family; neutral style is a project choice |
| `Frontside = 3` | B | Indy: toe edge, rear arm outside the rear leg | SKD and [Indy](https://www.skateboardhere.com/indy-grab.html) |
| `Double = 4` | F and B | Both opposite side edges; proposed F heel/B toe assignment | [Double grab](https://www.skateboardhere.com/double-grab.html); exact hand assignment is this proposal |
| `Tail = 5` | B | Centre of the tail end | SKD and [tail grab](https://www.skateboardhere.com/tail-grab.html) |
| `Nose = 6` | F | Centre of the nose end | SKD and [nose grab](https://www.skateboardhere.com/nose-grab.html) |
| `Stalefish = 7` | B | Heel edge; arm reaches around the rear leg, not through both legs | SKD and [Stalefish](https://www.skateboardhere.com/stalefish-grab.html) |
| `Crail = 8` | B | Toe-side corner of the nose; hand crosses toward the front | [Crail](https://www.skateboardhere.com/crail-grab.html) |
| `Seatbelt = 9` | F | Toe-side corner of the tail; hand crosses toward the back | [Seatbelt](https://www.skateboardhere.com/seatbelt-grab.html) |
| `Melon = 10` | F | Heel edge between feet, with a stronger authored leg/board tweak than Backside | [Melon](https://www.skateboardhere.com/melancholy-grab.html); pose distinction is proposed |
| `Method = 11` | F | Heel edge between feet; knees bend and the board comes behind the rider as the body arches | [Method](https://www.skateboardhere.com/method-air.html) |
| `Nosebone = 12` | B | Selected variant: tail grip, front leg extends | [Nosebone](https://www.skateboardhere.com/nosebone.html) |
| `Tailbone = 13` | F | Selected variant: nose grip, rear leg extends | [Tailbone](https://www.skateboardhere.com/tailbone.html) |

The chosen Nosebone/Tailbone variants follow the cited definitions. Their names describe the extended-leg tweak, not which end is grabbed. They are not intended to exclude every other bone variation. Double here means opposite side edges, not a two-end Cannonball/rocket pose.

## Proposed board and contact data

The authored detailed shared mesh has nominal outline half-width `0.102`, half-length `0.400`, and kicks beginning at `|z|=0.245`. Its rounded lip expands the outer bounds to roughly `0.104`/`0.402`; these are distinct from the top/bottom outline. The nose/tail outline narrows beyond `|z|=0.288`; a point at `x=0.100,z=0.360` is outside that outline. Use the actual emitted edge at the requested end position, not the straight side width. The simpler fallback board has different geometry, so it needs its own contact points.

For the proposed contact table, `(a,b)` means anatomical toe-axis offset and named nose-axis offset from the board centre. Resolve it into the mesh's actual axes before transforming to world space. Side grips use the emitted side edge near `a=±0.102`; end-corner grips at `|b|=0.360` use the curved edge, approximately `|a|=0.078`. Tip grips use `a=0,|b|=0.395`. These coordinates select a grip zone; project onto the actual mesh edge and include its height rather than treating those approximations as an independent board shape.

For the current mesh the top surface is `h(x,z)=0.092 + 0.05 max(0,(|z|-0.245)/0.155)^2 + 0.008(x/0.102)^2`; its bottom is `h-0.009`. A proposed palm contact at the edge's middle thickness uses `h-0.0045`, with the fingers under the edge and thumb over it. These are project mesh coordinates, not real hand anatomy measurements. Transform the contact with the final board rotation and its centre-preserving translation.

Every numeric entry in this table is a **proposal** except Legacy's intentionally retained roll/rise. Board rise is a target displacement toward the rider along `U`; it must move the deck and attached feet together. A held hand has zero intended separation from its grip zone.

| Kind | Toe-up angle | Nose-up angle | Rise | Grip `(a,b)` |
|---|---:|---:|---:|---|
| None | 0 | 0 | 0 | None |
| Legacy | +15 | 0 | 0.30 | Existing compatibility target |
| Backside | -20 | 0 | 0.30 | Heel side `(-edge,+0.04)` |
| Frontside / Indy | +25 | 0 | 0.30 | Toe side `(+edge,-0.08)` |
| Double | 0 | 0 | 0.30 | F heel `(-edge,+0.04)`, B toe `(+edge,-0.06)` |
| Tail | 0 | -15 | 0.30 | Tail centre `(0,-0.395)` |
| Nose | 0 | +15 | 0.30 | Nose centre `(0,+0.395)` |
| Stalefish | -20 | -8 | 0.30 | Heel side `(-edge,-0.15)` |
| Crail | +15 | +12 | 0.30 | Nose toe corner `(+curvedEdge,+0.360)` |
| Seatbelt | +15 | -12 | 0.30 | Tail toe corner `(+curvedEdge,-0.360)` |
| Melon | -30 | -6 | 0.30 | Heel side `(-edge,-0.06)` |
| Method | -40 | 0 | 0.35 | Heel side `(-edge,+0.04)` |
| Nosebone | 0 | -18 | 0.25 | Tail centre `(0,-0.395)` |
| Tailbone | 0 | +18 | 0.25 | Nose centre `(0,+0.395)` |

For Method, also propose moving the deck/feet `0.10` toward the heel side relative to the hips while raising them. This is a coherent rider/board relationship to solve, not an independent shove of the deck through the attached feet. For the other named grabs, begin with no extra horizontal deck translation.

## Proposed rider shape

Bring the board and knees toward the hand rather than leaving the board low and only stretching an arm down. [Skateboardhere grab fundamentals](https://www.skateboardhere.com/skateboard-grabs.html).

The table supplies **authored starting targets**, with F/B roles resolved as above. Knee bend is measured from a straight leg: `0°` is straight, so a bone target still keeps `10–25°` of bend rather than locking a joint. Chest turn is an anatomical turn toward the named nose or tail, not a world yaw sign. Forward fold is toward the toe side; negative fold denotes a small arch. Free-hand lift is above that arm's neutral shoulder-level balancing target; start its lateral reach at `0.15` outward from its own shoulder. Keep elbow clearance from the leg, torso and opposite arm.

| Kind | F/B knee targets | Tuck or bone form | Chest turn / fold | Free hand lift |
|---|---|---|---|---|
| None | Balance pose | No added tuck | Normal balance | Normal balance |
| Legacy | Existing solver | Preserve old pose | Existing solver | Existing solver |
| Backside | `65° / 70°` | Both knees in, heel edge drawn up | Noseward `10°` / forward `20°` | B `0.25` |
| Frontside / Indy | `65° / 75°` | Rear elbow outside rear knee | Tailward `10°` / forward `20°` | F `0.25` |
| Double | `75° / 75°` | Symmetric compact tuck | `0° / 15°` | None; both contact |
| Tail | `45° / 80°` | Rear knee folds under held tail | Tailward `20°` / forward `15°` | F `0.30` |
| Nose | `80° / 45°` | Front knee folds under held nose | Noseward `20°` / forward `15°` | B `0.30` |
| Stalefish | `65° / 80°` | Back arm routes behind rear leg | Tailward `15°` / forward `15°` | F `0.40` |
| Crail | `80° / 45°` | Rear hand crosses to nose without elbow passing through front knee | Noseward `45°` / forward `20°` | F `0.20` |
| Seatbelt | `45° / 80°` | Front hand crosses to tail without elbow passing through rear knee | Tailward `45°` / forward `20°` | B `0.35` |
| Melon | `30° / 80°` | Longer front leg, rear knee tucked | Noseward `20°` / forward `15°` | B `0.35` |
| Method | `80° / 85°` | Both heels/board drawn behind hips | Noseward `10°` / arch `10°` | B `0.45` |
| Nosebone | `15° / 80°` | Front leg extends; rear stays tucked under tail grip | Tailward `20°` / forward `10°` | F `0.30` |
| Tailbone | `80° / 15°` | Rear leg extends; front stays tucked under nose grip | Noseward `20°` / forward `10°` | B `0.30` |

These joint targets do not override measured limb lengths. Solve hand contact, feet on the rotated deck and reachable hips/chest together; reduce the authored tweak or rise when necessary. Keep the Method arch visible instead of forcing the torso into the same forward fold as every other grab. Clamp a free hand to its measured arm reach rather than treating the listed lift as an absolute world point.

## Current values and integration limits

The following is an audit of current project-authored data, **not the proposal**. Raw roll/pitch are exactly the arrays in `SkateBoard`; `GrabRise=0.30` and the shared `GrabIn=0.18` seconds apply today.

| Kind | Current raw roll | Current raw pitch |
|---|---:|---:|
| None | 0 | 0 |
| Legacy | -15 | 0 |
| Backside | -20 | 0 |
| Frontside | +25 | 0 |
| Double | 0 | 0 |
| Tail | 0 | -15 |
| Nose | 0 | +15 |
| Stalefish | -20 | -8 |
| Crail | +15 | -12 |
| Seatbelt | -15 | +12 |
| Melon | -30 | -6 |
| Method | -40 | +8 |
| Nosebone | 0 | +18 |
| Tailbone | 0 | -18 |

The current shared `Grip` assigns side-grab offsets around `±0.10`, central nose/tail contacts at `±0.36`, and Crail/Seatbelt contacts at `±0.30`. Nosebone currently contacts near the toe-side tail at `-0.25`; Tailbone near the heel-side nose at `+0.30`. Its named tuck, extension, body-shift and chest-turn constants are authored solver inputs. They are not the grip-zone, knee or chest targets proposed above; chest-turn inputs there use radians.

Current hand contacts are made from an upright rider frame and are not themselves transformed by `GrabTweak`. Consequently, increasing a render-only tweak does not establish that a hand remains on the board edge. The rollout should first resolve stance/end roles and a shared final board frame, then constrain contact/feet/limbs, then tune the authored values against actual visible poses. `SkateRiderMotion` adds secondary hand lag and has no contact flags: bypass/reset it for constrained modes or solve held-hand contact after secondary motion. Filtering an already solved grip afterward cannot maintain zero hand separation. Preserve Legacy until old callers deliberately migrate. Source review establishes these contracts; client contact, reach, clipping and visual quality remain unverified.
