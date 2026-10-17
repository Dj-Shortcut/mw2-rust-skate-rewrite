# Original skate park assets

Eight editable Blender models and self-contained GLBs live in
[`assets/authored/`](../assets/authored/README.md): original CC0-1.0 geometry/materials,
Apache-2.0 generator, no game assets as inputs. Anchor offsets below use (right,up,forward).

| file stem / `SkateParkKind` value | riding dimensions, metres | origin; offset from native first tile |
| --- | --- | --- |
| `skate_bank` / `Bank=0` | width 3; 12°, rise 0.624, run 2.934; no platform | entry; (0,0,−1.467221401) |
| `skate_kicker` / `Kicker=1` | width 3; 25°, rise 1.268, run 2.719; no platform | entry; (0,0,−1.359461681) |
| `skate_quarterpipe` / `QuarterPipe=2` | width 3; smoothed 15°/35°, rise 2.497, run 5.355; no platform | entry; (0,0,−1.448888739) |
| `skate_ledge_low` / `LedgeLow=3` | 3 × 3; height 0.4 | base centre; (0,0,0) |
| `skate_ledge_high` / `LedgeHigh=4` | 3 × 3; height 0.8 | base centre; (0,0,0) |
| `skate_long_ledge` / `LongLedge=5` | 9 across × 3; height 0.4 | base centre; (0,0,0) |
| `skate_funbox` / `Funbox=6` | width 3; flat 3, run 8.796, height 0.776 | base below deck; (0,0,0) |
| `skate_halfpipe` / `Halfpipe=7` | width 6; flat 6 × 6; rise 2.497, decks 3, run 22.710 | flat centre; (1.5,0,1.5) |

Metres: Blender +X right/+Z up/−Y forward; GLB +X right/+Y up/+Z forward.
Manifest `bounds`/`collision.bounds` are Blender-space visual/proxy bounds including
their separate features. Origins have height zero; skins can extend below it.
Rotate the table's offsets only by placement yaw before adding them to the native anchor.

Quarter reference (forward,height): (0,0), (2.897777479,0.776457135),
(5.355233612,2.497186444), two 3 m sloped tiles. C1 joins soften entry/corner;
the quarter ends at 35° without a deck/backrail. Half transitions flatten into
3 m decks outside the unchanged X/forward [−3,3] flat. Each curved window has
16 intervals: quarter35 knots, half51 per transition; analytic top normals, flat sides/caps.
Heights stay monotone; nominal facet deviation bounds are 0.010807/0.020747 m.
A separate 0.02 m lift keeps the riding profile within 0.05 m; it is not baked in.
One-sided joins briefly exceed native slopes to preserve endpoint heights/flat extents.
Coping/cladding/backrails are separate features; halfpipe backrails rise 0.86 m.

Saved sources have visible `Visual`, hidden `CollisionProxies`, preview camera/lights.
GLB defaults to `Visual`, with opt-in `CollisionProxies`; `role` extras and
`VISUAL__`/`COLLISION__` names identify nodes; visual ride parts have `ride_surface=true`.
Build static colliders explicitly from closed proxies. One convex halfpipe hull fills
its riding opening; moving-body use needs convex decomposition. No external textures
or transparent collision materials; preview camera/lights are excluded from GLB.

The gesture fallback retains its foundation; marked items remove their temporary carrier.
[Supported placement](RUST-SKATE-PLACEMENT.md) determines the final anchor and ghost offset;
`GroundedBuilding` is separate opt-in.
Reference heights are nominal floor centre planes, not measured prefab tops. Native
thickness/support offsets prevent claiming exact top separation or full collider-volume
identity. Authored backrails differ from native low walls/sixteen Metal side panels.
Client GLB reader/finder/check-command source exists. Codex has not verified its execution,
native overlay/contact, network placement, riding, Unity import or performance.

Generate with [the authored commands](../assets/authored/README.md). The generator checks
finite geometry, closed outward solids, triangle areas, transforms/budgets and records hashes.
Original four: Blender4.3.2; ramps:5.2.0. Cross-version export equivalence is unproven.
