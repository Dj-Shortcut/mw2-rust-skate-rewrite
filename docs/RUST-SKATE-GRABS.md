# Skate grab pose data

This is a proposed animation specification for the existing Rust skate mod. The grip identities below use public skateboarding teaching references. Every angle, distance, knee range and timing target marked **proposal** is original authored data for this project, not a measurement of a skater or an animation extracted from a game. These values have not been applied or visually verified in the Rust client.

The grab source audit refers to revision `d07f0a6`; its audited values are unchanged at `9f8569d`. The stall/plant audit below includes Claude's subsequent `6f54d2f` changes through `9f8569d`. The relevant files are [the grab enum](../mods/rust/shared/SkateTricks.cs), [shared rider poses](../mods/rust/shared/SkateRider.cs), [the board mesh](../mods/rust/shared/SkateBoardMesh.cs), [board rendering](../mods/rust/client/src/SkateBoard.cs), and [the rider adapter](../mods/rust/client/src/RiderRig.cs). This document changes none of those files.

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


## Coping stalls: contact before pose

This section proposes original contact, board and rider data for `TailStall`, `NoseStall`, `BoardStall` and `FiftyFiftyStall`. Public teaching references establish which part of the board bears on the coping. The numerical targets below are authored starting values, not measured game animation data or validated native collision behaviour.

| Existing kind | Source-backed identity | Entry and release feature |
|---|---|---|
| `TailStall` | Tail bears on coping; rear leg loads while front leg bends | Typical entry is fakie, followed by dropping back in. [Tail stall](https://www.skateboardhere.com/tail-stall.html). |
| `NoseStall` | Nose bears on coping; front leg loads while rear leg bends | Enter nose first and return fakie. [Nose stall](https://www.skateboardhere.com/nose-stall.html). |
| `BoardStall` | Selected baseline is rock-to-fakie: front truck goes beyond the coping and the deck underside rocks on it | Lift the front truck clear before returning. A disaster needs its additional entry rotation and is not assumed here. [Rock to fakie](https://www.skateboardhere.com/rock-to-fakie.html). |
| `FiftyFiftyStall` | Both truck hangers bear on coping; deck length runs along the coping | Pivot to bring the front truck back into the transition. [Axle stall](https://www.skateboardhere.com/axle-stall.html). |

Capture a world contact `C`, a normalized coping tangent `K`, platform/support up `U`, and deckward direction `A` perpendicular to the coping. In a square approach, nose-first `N` points deckward along `A`; in a 50-50 hold it lies along either `+K` or `-K`. Freeze these contact descriptors during hold. They must come from the selected surface/coping, not from a moving player origin or an invented flat ground height.

For these proposed poses, rotate about the actual contact rather than the board centre. If local board point `p` must stay at `C`, and `Q` is the desired final board orientation, its mesh origin is `C-Q*p`. A two-truck hold needs both transformed hanger points on the coping; a single midpoint target does not establish that constraint. Keep feet attached to the transformed deck and solve the rider above them. No grab rise applies while the board is supported in a stall.

The authored mesh has truck centres at `z=±0.245`, while the current board's manual approximation uses `Axle=0.23`. Its hanger centre is `y=0.034` with radius `0.007`; the axle centre is `y=0.027` with radius `0.0035`. The proposal therefore uses the lowest central emitted metal surface around `(0,0.0235,±0.245)`, or the actual truck surface when another board representation is selected. The hanger bottom at `0.027` intersects the axle geometry and must not be used as the complete truck underside. Nose/tail and centre deck contacts use the deck underside `h(x,z)-0.009` from the mesh formula above. These are source mesh coordinates; the stall angles and selected contact stations are proposals.

| Kind | Proposed held contact in board coordinates | Board direction / pitch | Sole centres on deck `(a,b)` | Rider shape proposal |
|---|---|---|---|---|
| Tail stall | Tail underside at `(0,h(0,-0.360)-0.009,-0.360)` fixed to `C` | Nose away from coping into transition; nose-up `+22°` relative to platform frame | F `(0,+0.16)`, B `(0,-0.31)` | B knee `30°`, F `70°`; hips over tail contact, modest `10°` forward fold, free arms balance |
| Nose stall | Nose underside at `(0,h(0,+0.360)-0.009,+0.360)` fixed to `C` | Nose toward deck; nose-up `-22°` | F `(0,+0.31)`, B `(0,-0.16)` | F knee `30°`, B `70°`; hips over nose contact, `10°` fold, free arms balance |
| Board stall | Centre underside `(0,0.083,0)` fixed to `C` | Nose toward deck; hold nose-up `+8°` as a starting target | F `(0,+0.21)`, B `(0,-0.21)` | F/B knees `45°/55°`; centre weight over deck contact, `10°` fold |
| 50-50 stall | Both truck underside contacts around `(0,0.0235,±0.245)` | Length parallel to coping, hold pitch `0°` relative to local coping support plane | F `(0,+0.21)`, B `(0,-0.21)` | F/B knees `45°/45°`; hips between trucks, free arms opposite for balance |

Sole heights follow the final top surface, with the actual foot's sole-to-ankle offset. The `(a,b)` entries do not mean that an ankle should be placed directly on the deck. The exact held angle may be reduced to maintain reachable limbs or adjusted for measured coping geometry; it is not a permission to penetrate a ramp collider.

Proposed timing uses contact acquisition as `s=0`: blend into the held pose over `0.12s`, then keep the contact unchanged until release or the current hold timeout. Tail/nose release unloads the held end and transitions toward the measured outgoing surface; BoardStall first raises the front truck, beginning with nose-up `+18°` around the retained deck contact, before travelling back across the lip. A 50-50 release lifts the front truck, begins a `90°` yaw back toward the transition around the rear hanger contact, then ends that pivot only after the front truck is clear. These are authored sequencing targets. Clearance and attainable exit geometry must determine when the contact can actually release; no arbitrary timer proves that clearance.

The source at `9f8569d` selects these stall kinds and holds a movement target. Tail/NoseStall also set the generic manual flags. Since `6f54d2f`, their rendered Tip target is `±1.8` times `TipDegrees=15`, a nominal `±27°` approached by smoothing. The board's generic `Tilt` still rotates around its approximate trailing/leading axle rather than the coping-end/deck/truck pivot specified here. Rider stall crouch targets are Board `0.55`, 50-50 `0.45`, Handplant `0.38` and otherwise `0.32`; non-handplant stalls widen the balancing arms with `StallArms=1.6`. These source pose parameters are distinct from the proposed knee angles above. The controller uses `StallMax=2.5s`, then reverses travel for non-handplant exits. Those are existing controller/rendering rules, not evidence that the four contact shapes or their exits have been visually solved.

## Footplants: different feet and different launch sequences

Front/back roles use the stance mapping above and stay fixed for the whole action. No-comply plants F and pops with B; boneless plants F with a B-hand Indy grip; fastplant plants B with an Indy grip; the selected beanplant plants F to the heel side with an F-hand nose grip. These identities come from the linked teaching pages. The foot position, pitch and timing columns below are proposals.

| `SkatePopKind` | Grounded foot | Foot retained on board | Hand and board action | Reference |
|---|---|---|---|---|
| `NoComply = 3` | F | B pops tail and guides/catches the board | No required hand grab | [No-comply](https://www.skateboardhere.com/no-comply.html) |
| `Boneless = 4` | F | B | B-hand Indy acquired before the jump; board is pulled up while the planted leg launches | [Boneless](https://www.skateboardhere.com/boneless.html) |
| `Fastplant = 5` | B | F | B-hand Indy, with the rear foot planted rather than the front | [Fastplant](https://www.skateboardhere.com/fastplant.html) |
| `Beanplant = 6` | F | B | Selected nose-grab variant: F hand holds nose, F foot steps off heel side, B stays on deck | [Beanplant](https://www.skateboardhere.com/bean-plant.html) |

EA also describes no-comply as the front foot leaving while the back foot pops. Its settings guide does not give the offsets or animation timings below. [EA skate. settings guide](https://help.ea.com/en/articles/skate/skate/settings-guide/).

Choose a reachable ground patch before a plant starts. Let `O` be the board centre at that acquisition, `N` its resolved nose direction, and `T` the anatomical toe direction. The proposed ground sole contact is the surface projection of `O + a*T + b*N`. Once acquired, store that world point and its normal: the planted foot stays there until lift-off, while the board and hips can rise. On a moving support, retain the contact in that support's local frame and resolve its world transform each frame. An airborne body origin plus a downward offset is not a substitute for a stored ground contact.

| Plant | Proposed ground sole `(a,b)` | Retained board sole at acquisition | Proposed preload shape |
|---|---|---|---|
| No-comply | `(-0.28,+0.16)` | B near tail `(0,-0.31)` | F knee `45°`; B bends to pop, torso initially `15°` forward |
| Boneless | `(-0.30,+0.18)` | B `(0,-0.21)` | F knee `40°`; B `60°`; Indy hand secured before loading the plant |
| Fastplant | `(-0.30,-0.18)` | F `(0,+0.21)` | B knee `40°`; F `60°`; Indy contact carries the freed rear end |
| Beanplant | `(-0.30,+0.18)` | B `(0,-0.21)` | F knee `40°`; B `60°`; front arm/nose contact clear of front knee |

The heel-side location for Beanplant is source-backed; choosing heel-side patches for the other three is an authored starting choice, not a claim that every real variation plants on that side. Resolve L/R using the stance table: NoComply/Boneless/Beanplant plant L in regular and R in goofy, reversing those roles in switch; Fastplant plants the other foot. Translate the sole contact into ankle and toe targets with the measured foot shape and surface normal. The retained foot follows the displayed deck and the named hand follows its actual grip zone.

### Proposed launch-relative timing and board pitch

Here `t=0` is **lift-off from the planted foot**, when the launch velocity is committed. Negative time is preparation while support contact still exists; positive time is aerial return. These are original timing targets for a new plant action, not a restatement of the current post-launch timer. Interpolate pose samples smoothly without overshooting pitch or passing through the contact plane.

| Plant | `t=-0.18s`: begin | `t=-0.10s`: acquire/load contact | `t=0`: extend and lift off | `t=+0.12s`: return | By `t=+0.30s`, or earlier landing |
|---|---|---|---|---|---|
| No-comply | Board pitch `0°`; F begins stepping away | F sole fixed; B loads tail; pitch `+15°` | Planted F leg extends toward `15°` knee bend; tail pop lifts nose toward `+35°`; B leg guides board | F returns toward deck; pitch falls toward `+12°`, B guides its catch | Both soles back on deck, pitch approaches `0°` relative to the landing surface |
| Boneless | Crouch and acquire Indy; pitch `0°` | F fixed; B and hand lift nose; pitch `+15°` | F leg extends toward `15°`; hold Indy, board pitch peaks near `+30°` | F retracts to deck; Indy can release; pitch returns toward `+10°` | Both soles supported by deck; release hand and prepare landing |
| Fastplant | Acquire Indy; B begins stepping away; pitch `0°` | B fixed; F remains on deck; pitch `+8°` | B leg extends toward `15°`; Indy lifts board with F attached; pitch near `+15°` | B retracts to rear deck; pitch returns toward `+5°` | Both soles back; hand releases and normal landing pose resumes |
| Beanplant | F hand acquires nose; F steps heelward; pitch `+5°` | F fixed; B stays on deck; nose held at about `+20°` | F leg extends toward `15°`; nose-hand lift peaks near `+30°` | F retracts; B and nose hand control return toward `+12°` | Both soles back; nose hand releases before landing |

All pitches above are semantic nose-up in the resolved board frame. They use the actual quaternion sign conversion above; they are not raw `grabPitch` assignments that can be layered independently over a competing grab/manual rotation. Define one resulting board orientation and one set of contact targets. For plant hand contact, use the grip zone established above; do not use the generic air-grab rise while that hand is already constraining the board during preload.

During the airborne return, propose moving the freed foot through an arc with up to `0.10m` extra sole clearance above its final deck position, then returning it to the measured deck surface. Its ground-contact weight reaches zero at `t=0`; its deck-contact weight reaches one by `t=+0.30s`, shortened to the actual landing deadline. When that interval is too short for a safe reachable return, reduce or refuse the action rather than leaving a foot fixed below a descending board. Visual motion must not add a second physical launch or pump-energy award. Launch physics and board pitch are separate outputs.

### Current plant audit and implementation boundary

At `9f8569d`, the controller starts `PlantedUntil = now + PlantSeconds` when a plant launches, with `PlantSeconds=0.25s`; `FootPlanted` is active in Air. The rider adapter passes a scalar blend to the shared rider. That shared path always moves the effective F ankle toward `BoardPosition + facing*0.20 + travel*0.22`, lowering it by a blend of `GroundDrop+0.30`. It does not choose B for Fastplant or store a plant-specific ground contact.

Since `6f54d2f`, the same scalar also drives the displayed board: `PlantPitch=38°`, `PlantLift=0.12m` and `PlantPivot=0.20m`. It rotates about a rearward pivot chosen along the nose or its opposite according to travel, after the grab/flip rotation, then raises the board. This generic render transform applies whenever Plant is active outside Bail; it is not a per-kind preload, fixed world-foot contact or the launch-relative pitch sequence above. The rider's feet frame receives Contact and Tilt but not this Plant kick. Board Follow also reads the cached Plant scalar before RiderRig.Frame refreshes it, so it represents the previous frame's pose value. Its hand/foot contact relationship still needs to be solved with the final displayed deck transform.

Current pop-height multipliers are NoComply `0.85`, Boneless `1.12`, Fastplant `1.10` and Beanplant `1.05`. They are existing authored controller choices, not evidence of the correct grounded foot, contact duration, board pitch or native animation. The proposal preserves the distinction between pop physics and pose data; changing those height factors is outside this document.

Implementation should acquire and validate the selected support contact and stance roles, run preparation with the correct foot and hand, commit one launch at plant release, then return the freed foot in time for landing. Cancelled actions, bails, dismounts and unsupported contact must release all pose/contact constraints. The data here establishes intended identities and starting shapes; actual support detection, contact collision, limb reach and visible client motion remain unverified.
