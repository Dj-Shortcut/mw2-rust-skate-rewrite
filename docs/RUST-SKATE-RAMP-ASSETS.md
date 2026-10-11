# Original skate park assets

Eight editable Blender models and self-contained GLBs live in
[`assets/authored/`](../assets/authored/README.md). They are original CC0-1.0
geometry/materials; the generator is Apache-2.0. No game assets are inputs.

| file stem / `SkateParkKind` value | authored riding dimensions, metres | origin |
| --- | --- | --- |
| `skate_bank` / `Bank=0` | width 3; 12° slope, rise 0.624; top deck 0.75 | entry midpoint |
| `skate_kicker` / `Kicker=1` | width 3; 25° slope, rise 1.268; top deck 0.75 | entry midpoint |
| `skate_quarterpipe` / `QuarterPipe=2` | width 3; circular radius/rise 2.5; top deck 1.5; run 4 | entry midpoint |
| `skate_ledge_low` / `LedgeLow=3` | 3 × 3; height 0.4 | base centre |
| `skate_ledge_high` / `LedgeHigh=4` | 3 × 3; height 0.8 | base centre |
| `skate_long_ledge` / `LongLedge=5` | 9 across × 3; height 0.4 | base centre |
| `skate_funbox` / `Funbox=6` | width 3; flat deck 3; total run 8.796; height 0.776 | base below deck centre |
| `skate_halfpipe` / `Halfpipe=7` | width 6; flat 6 × 6; radius/rise 2.5; decks 1.5; total run 14 | flat riding-plane centre |

Blender uses metres, +X right, +Z up, forward −Y. GLB uses +X right,
+Y up, forward +Z. Manifest `bounds` are Blender-space visual bounds including
cladding, coping and rails; `collision.bounds` describes proxies in the same
basis. Origins are at height zero; skins can extend below that plane.
The halfpipe flat spans X/forward [−3,3]; both transitions/platforms are outside.
The circular surface has 64 segments; its normals are smooth but geometry is
piecewise planar. Quarter/halfpipe wood backrails rise 0.86 above their decks.

The `.blend` has visible `Visual` and hidden `CollisionProxies` collections,
a camera and lights. The GLB defaults to scene `Visual`; its separate
`CollisionProxies` scene is opt-in. Nodes carry `role` extras and `VISUAL__` /
`COLLISION__` names; visual ride parts also carry `ride_surface=true`.
Load only Visual for display. Explicitly build static collision from the closed
proxy meshes; their names do not create engine colliders. Do not make one convex
hull of the halfpipe: it would fill the open riding area. Moving-body collision
needs a separate convex decomposition. The export contains no transparent
collision materials or external textures.

Rust's existing build-plan route uses a pending park selection and a square
foundation through `OnEntityBuilt`; the foundation remains ordinary support.
It creates native floors/walls with 15°/35° facets, a 22.71 m halfpipe and
first-tile anchor. An adapter must use this pack's pivots/profiles and place
matching server collision and client rendering together. No plugin or client
loads this pack; network placement, riding and Unity import are unverified.

Generate with the [authored README commands](../assets/authored/README.md).
The generator checks finite geometry, closed outward solids, triangle areas,
positive transforms and per-model budgets, then records bounds/counts/hashes.
Generation and render evidence uses actual Blender 5.2.0. Local ramp regeneration
is checked separately. CI pins Blender 4.3.2 for the original four and 5.2.0 for
the ramps; cross-version export equivalence and native performance are unproven.
