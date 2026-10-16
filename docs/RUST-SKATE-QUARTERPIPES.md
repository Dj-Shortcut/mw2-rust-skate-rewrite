# Quarterpipe geometry contract

Kinds `2` (quarterpipe), `8` (mini) and `9` (large) share one riding and closed-solid contract in `mods/rust/shared/SkatePark.cs`.
Coordinates are metres: `x=Right`, `y=Up`, `z=Forward`; the ramp rises along `+z`. Apply the group yaw and anchor once.
The riding datum `y=0` is `group.AnchorY + FloorPrefab Construction.bounds.max.y`, measured before pitching a carrier.
| Kind | Radius r | Height h | Vert | Start z | Curve foot z | Lip z | Back z | Deck depth | Half width | Reserved cells (right,forward) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| 2 original | 3 | 3 | 0 | -1.4 | -1.2 | 1.8 | 4.4 | 2.6 | 1.4 | (0,0), (0,1) |
| 8 mini | 1.2 | 1.2 | 0 | -1.4 | -1.2 | 0 | 1.4 | 1.4 | 1.4 | (0,0) |
| 9 large | 3 | 3.2 | 0.2 | -1.4 | -1.2 | 1.8 | 4.4 | 2.6 | 1.4 | (0,0), (1,0), (0,1) |

Each cell is a 3 m square centered at `(3*right,3*forward)`. The missing large `(1,1)` cell stays empty.
The large staging apron is a separate closed box: `x=[1.4,4.4]`, `z=[-1.4,1.4]`, `y=[-0.08,0]`.

The entry is level from `z=-1.4` to `-1.2`. The circular transition is exactly
`z=-1.2+r*sin(theta), y=r*(1-cos(theta))`, `theta=0..pi/2`.
Its tangent is horizontal at the foot and vertical at `(lip,r)`; its normal is `(0,cos(theta),-sin(theta))`.
Only the large continues vertically at `z=lip` from `y=r` to `h`. The deck is horizontal at `y=h`, `z=lip..back`, across the full `x=[-1.4,1.4]` width.

`QuarterPointCount` / `TryQuarterPoint` return the entry start, 31 arc points at 3 degree intervals, the optional vert top, then the deck back.
Counts are 33 for original/mini and 34 for large. Both arc endpoints are exact. Use these same vertices for rendering and collision.
`TryQuarterSurface` returns the arc top and vertical normal at exactly `z=lip`; its single-valued height query cannot represent the large vertical face.
`QuarterShellPointCount` / `TryQuarterShellPoint` append `(back,-0.08)` and `(start,-0.08)`; close the polygon to its first point.

Extrude the shell across `x=[-1.4,1.4]`: riding surface, full-height closed side faces, back, entry front and bottom all exist.
Triangulate each side strip beneath consecutive riding points down to `y=-0.08`; a fan across the complete concave polygon fills riding air incorrectly.
Use outward-facing triangles for the closed solid; the riding triangles face the stated normals. Server and client meshes use the same winding.
The deck has a visible closed back down to `-0.08`; the lip never continues upward as a taper or spike.

Coping is a round cylinder of radius `0.05`, axis `+x`, from `(-1.4,h,lip)` to `(1.4,h,lip)`, with round end caps.
Use a capsule collider (`direction=0`, `radius=0.05`, total end-to-end height `2.9`) centered at `(0,h,lip)`; the cylindrical segment is 2.8 m long.
Render at least 16 radial segments and hemispherical caps. The lip projects 0.05 m above the deck and toward the transition; mini clear deck depth remains 1.35 m.
There is no required elevated guard rail; `RailHeight=0`. The closed back/sides describe the structure below the riding surface.

Native carriers retain their public identities and transforms (`Right,Forward,Height,Pitch,Yaw`):

| Kind | Carrier parts in index order |
|---|---|
| 2 | Floor `(0,0,1.5*sin15,-15,0)`; floor `(0,1.5*(cos15+cos35),3*sin15+1.5*sin35,-35,0)` |
| 8 | Floor `(0,0,0,0,0)`; low wall `(0,1.15,0,0,90)` |
| 9 | Floor `(0,0,0,0,0)`; floor `(3,0,0,0,0)`; floor `(0,3,0,0,0)` |

Angles in carrier formulas are degrees. These native blocks are saved/networked recognition carriers; their slab/wall colliders do not define the riding shape.
For each validated quarter group, disable the original non-trigger carrier colliders and install the closed shell, coping and optional apron colliders in the group frame.
Claude's client must recognize all three kinds with the same stable carrier arrays, hide carrier rendering, disable carrier collision, and build this exact shared mesh/collider contract.
The original visual origin is the group anchor, with no pitched-floor toe offset. Transform custom children back into their carrier parent's frame if parented there.
Restore original collider state when removing generated geometry or unloading; regenerate only for validated complete saved groups.

Bounds checks use the complete reserved mask and measured vertical envelope, with at least 1 mm tolerance and additional float32-scale world-coordinate tolerance.
`ContainsQuarterPoint` accepts mask corners; `Float32BoundsTolerance` returns `max(0.001,max(abs(world coordinates))/4194304)`.
