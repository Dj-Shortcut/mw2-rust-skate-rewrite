# Supported park placement

In source **0.3.20**, marked plans use native square-foundation confirmation. The server owns the final
pose and item debit. The [item contract](RUST-SKATE-BUILD-ASSETS.md) and
[shared helpers](../mods/rust/shared/SkatePark.cs) apply to both client frontends.
Half-wall kickers retain their separate wall/side/join contract.

## Target and rotation contract

- **Native socket / terrain:** submit the genuine native foundation target and
  normal rotation input. Keep the native socket, not its world socket point as an
  assembly origin. The resulting carrier supplies final X/Z/yaw. After verified
  carrier removal, resolve remaining terrain/foundation support as below.
- **Existing square foundation top:** submit that actual entity, `socket=null`,
  `valid=true`, `shouldParent=false` and the genuine eye ray. The first ray hit
  must be this saved, horizontal, unparented foundation's top (`normal.y>=0.98`).
  The foundation centre is support cell `(0,0)`. Resolve the assembly anchor with
  `TrySupportGridAnchor` after rotation; MEGA adds 4.5m along Forward so its first
  flat floor, rather than its assembly centre, lands on that foundation. Other
  kinds retain the foundation centre as their anchor. There is no duplicate
  foundation or material bill.
- **Park adjacency:** use the same unsocketed entity/ray target on a registered,
  flat floor at its assembly's lowest native-floor level. In that floor's yaw
  frame, take the dominant component of the hit offset: ±Right or ±Forward.
  Ties choose Right, an exact centre chooses +Right. The candidate centre is
  exactly 3m along that direction. `TryAdjacentCell` implements this rule; this
  is the new support cell `(0,0)`, then apply `TrySupportGridAnchor`.
  Elevated decks, slopes, walls and arbitrary unregistered floors offer no socket.
  Adjacent pieces require their own terrain/foundation support; this is no stacking.
- For the two unsocketed targets, `target.rotation.y` is the ordinary rotation
  delta: `TrySnapYaw(hostYaw+delta, hostYaw, foundation.rotationAmount.y)`.
  Read the actual native step; do not introduce free-angle preview controls.
  Admin aimed placement rounds its world view yaw relative to the host instead.
  Free/explicit admin poses round relative to world yaw0. Native socket rotations
  remain the native result, including socket-imposed headings.

The server recasts within 25m against World/Terrain/Construction/Deployed/Player
and replays the original target's building hooks after dispatch before creation.
Changing the target, support, owned item or world refuses the transaction.

## Complete foundation grid contract

Before checking the native floor contacts below, enumerate `SupportCellCount`,
`TrySupportCell` and `TrySupportCellPose`. Coordinates are integer Right/Forward
relative to the selected support cell; rotation applies to the entire mask.
`CellCount`/`TryCell` retain their separate mini/large module meaning.

| Kind | Required support cells |
| --- | --- |
| bank, kicker, low/high ledge, mini | `(0,0)` |
| quarterpipe | `(0,0)`, `(0,1)` |
| long ledge | `(-1,0)`, `(0,0)`, `(1,0)` |
| funbox | `(0,-1)`, `(0,0)`, `(0,1)` |
| large L quarterpipe | `(0,0)`, `(1,0)`, `(0,1)`; `(1,1)` is excluded |
| halfpipe | Right `-1..2`, Forward `-3..4`: 32 cells including side/rear guards |
| MEGA halfpipe | Right `-1..4`, Forward `-5..8`: 84 cells covering the entire existing riding reservation, including its 0.65m safety margin |

MEGA's local grid centres are `(3*r, -4.5+3*f)`, preserving every authored native
part and saved assembly transform. Other kinds use `(3*r,3*f)`. The added support
footprint does not change native layouts, item identities, occupied L mask, saved
schema or existing occupancy checks. Half-wall kickers keep their wall contract.
Explicit admin coordinates remain assembly anchors; they must still pass every
cell check, including grid alignment when they meet foundations.

For **each cell**, cast nine downward probes at local Right/Forward `-1.49,0,1.49`
metres from its centre, from candidate assembly Y+2m for 27m, using the same first-hit
layers and trigger exclusion as native contact probes. All nine must hit one and
the same saved, unparented, permitted, horizontal square foundation, with measured
native top matching each hit, or all nine must hit dry heightmap-matching terrain.
No mixture of foundation and terrain, two foundations within a cell, park members,
floors, triangles or other entities can substitute. An elevated platform's missing
foundation cannot be filled by lower ground: the whole plane must agree.

`FitsSupportCell` requires the foundation centre within 0.02m on each world X/Z
axis of its cell centre and its yaw equal to the grid modulo 90 degrees within
0.01 degrees. Native foundation bounds must be 2.9–3.1m in X/Z; horizontal pose
and top-hit checks remain mandatory. Different cells therefore cannot reuse one
foundation. All probe heights, across **all** cells and lowest native contacts,
must have total max-minus-min <=0.02m; choose the maximum as supportTop. Top/edge
placement also retains the selected host plane. This is contact tolerance, not a
height adjustment for an uneven foundation grid.

Refusals identify the local cell, for example `Park cell (0,1): uneven support`
or `Park cell (1,0): missing, blocked or uneven support`. Foundation identity,
pose, grade, native construction, captured collider, permission and top, plus
terrain collider/height/dryness and complete plane span, revalidate through the
existing request lifecycle, including after payment. A changed support reports its
cell and uses the existing rollback/refund guards. Support does not become a paid
park member or a persistent demolition dependency. Post-commit support demolition
and restored saved groups retain the existing lifecycle.

## Support and vertical offset contract

Carrier Y, foundation origin Y and explicit command Y are search hints, never the
support plane. This also applies to a command containing a literal Y such as `3.1`.
No `3.1` placement literal exists in the current server source; the former explicit
command path accepted any supplied layout Y without support adjustment.

Read actual native floor Construction bounds `(cx,cy,cz,sx,sy,sz)`. For every floor
part with authored height `h` and pitch `p`, compute its lowest bound:

```text
bottom = h + cy*cos(p) - cz*sin(p) - sy/2*abs(cos(p)) - sz/2*abs(sin(p))
anchorY = supportTop - min(bottom over actual floor parts)
```

`TrySupportedAnchor` implements this equation with zero added clearance. It does
not introduce an imaginary unraised floor. Old IDs, markers, relative part poses,
counts and saved anchors retain their meaning; existing saved pieces do not move.
This seats the native recognition carriers; the three quarterpipe kinds retain
their separate yaw-only [closed shell and coping](RUST-SKATE-QUARTERPIPES.md),
whose riding datum is the final anchor plus the measured unpitched floor top.
Do not pitch that shell with the original quarterpipe's carrier floors.
Ledges currently contain floors without legs: new supported placements lower their
whole anchor so the floor underside meets support. Keeping their old unsupported
0.4/0.8m elevation would require additional geometry; low/high remain distinct IDs.

Use `TrySupportSample(kind,index,0..8,floorBox,x,z,yaw)` to enumerate each underside
3×3 grid's samples at the global minimum (within 1e-6m): world X/Z and local Y.
Other samples return false.
For ray probes only, inset local Right by at most0.01m toward the part centre;
flat floors also inset Forward by at most0.01m. Keep an inclined floor's low edge.
Cast down from candidateY+2m, length27m, first obstruction, ignoring triggers,
with Terrain/World/Construction/Deployed layers (no Player layer).
Accept only dry terrain matching its heightmap, or saved horizontal square
foundations with matching measured collider/construction top. Foundation permission
must allow building. No park member or unrelated entity can act as support.
`TrySupportPlane` requires every probe present and total max-min<=0.02m; choose
max as supportTop. For top/park targets it must also match the selected foundation
top / neighbouring floor underside within0.02m; otherwise refuse the socket height.
This tolerance covers native float/contact error, not a lift.
The final anchor and all parts retain coordinate/range/privilege/occupancy checks.
Only captured foundation colliders whose maximum Y is at/below the support plane
plus contact tolerance, and the exact selected flat park floor
when its native bounds have no positive overlap, are exempted from proximity/deploy
contact rejection. Permission checks still run; physical footprint overlap still
rejects the park neighbour. Unrelated entities remain blockers.
Captured support identities/poses/grades and terrain heights are rechecked through
final registration, including after payment. Support removal after commit retains
the existing park demolition/stability lifecycle; no new persistent dependency is added.

## Ghost and placed visual contract

Resolve the native target, native rotation step, support-grid anchor, **every
required support cell**, and lowest native contact samples before showing a valid
ghost. Display the entire rotated support mask; the L hole is excluded. MEGA must
show the full 84-cell foundation requirement. Clear validity and show the failing
local cell when a foundation is absent, uneven, off-grid, unauthorized or changed.
Do not show green from a valid first cell alone. Missing bounds,
unknown support or identity, blocked cells, stale targets and incompatible edges
clear validity. Use `TryTransform` for every native preview member at the final
anchor. Never add foundation height, the former0.025m gap, a guessed floor thickness
or another support correction. Send the real native target, not `skate.place`.

The authored model's ghost must use the **same origin routine as its placed model**,
computed from those predicted final members. All three quarterpipe kinds use
`Anchor + Yaw*(0,t,0)`, where t is the measured unpitched floor top; their
`VisualOrigin` offsets are zero. In particular, the original quarterpipe does not
use a pitched-floor toe or inherit that carrier's pitch. For the other kinds,
floor0 pose P/Q and measured local top t give: bank/kicker P+Q*(0,t,-1.5);
halfpipe P+Yaw*(1.5,t,1.5); MEGA P+Yaw*(4.5,t,4.5); funbox
P+(0,t-3*sin(15deg),0); low/long ledge use P+(0,t-0.4,0), high uses t-0.8.
Any intentional visual skin/lift must be identical in ghost and placed rendering
and must not change the authoritative native anchor or support validity.

Approved layout/anchor scenarios verify the actual shared/plugin scalar helpers,
including the complete support masks, rotated cell poses, grid phase, fit tolerance
and plane refusals. They do not execute the server's Unity raycasts or transactions.
Native ghost submission, collider queries, hook timing, payment/refund delivery,
save/reload, support demolition and riding still require in-game acceptance.
