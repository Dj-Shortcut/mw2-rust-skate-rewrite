# Supported park placement

Marked plans use native square-foundation confirmation. The server owns the final
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
  X/Z are the foundation centre. There is no duplicate foundation or material bill.
- **Park adjacency:** use the same unsocketed entity/ray target on a registered,
  flat floor at its assembly's lowest native-floor level. In that floor's yaw
  frame, take the dominant component of the hit offset: ±Right or ±Forward.
  Ties choose Right, an exact centre chooses +Right. The candidate centre is
  exactly 3m along that direction. `TryAdjacentCell` implements this rule.
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
Only captured foundations below the plane, and the exact selected flat park floor
when its native bounds have no positive overlap, are exempted from proximity/deploy
contact rejection. Permission checks still run; physical footprint overlap still
rejects the park neighbour. Unrelated entities remain blockers.
Captured support identities/poses/grades and terrain heights are rechecked through
final registration, including after payment. Support removal after commit retains
the existing park demolition/stability lifecycle; no new persistent dependency is added.

## Ghost and placed visual contract

Resolve X/Z/yaw and support samples before showing a valid ghost. Missing bounds,
unknown support or identity, blocked cells, stale targets and incompatible edges
clear validity. Use `TryTransform` for every native preview member at the final
anchor. Never add foundation height, the former0.025m gap, a guessed floor thickness
or another support correction. Send the real native target, not `skate.place`.

The authored model's ghost must use the **same origin routine as its placed model**,
computed from those predicted final members. For floor0 pose P/Q and measured
local top t: bank/kicker/quarterpipe use P+Q*(0,t,-1.5); mini/large use P+(0,t,0);
halfpipe uses P+Yaw*(1.5,t,1.5); MEGA uses P+Yaw*(4.5,t,4.5); funbox uses
P+(0,t-3*sin(15deg),0); low/long ledge use P+(0,t-0.4,0), high uses t-0.8.
Any intentional visual skin/lift must be identical in ghost and placed rendering
and must not change the authoritative native anchor or support validity.

Approved layout/anchor scenarios verify the actual shared/plugin scalar helpers.
Native ghost submission, collider queries, hook timing, payment/refund delivery,
save/reload, support demolition and riding still require in-game acceptance.
