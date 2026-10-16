# Modular MEGA halfpipe contract

`mega-halfpipe` is one atomic kit, kind **11**, marked item **0x534B500C**, exact name `Skate MEGA Halfpipe` (stock carrier/skin/quantity unchanged). IDs 0–7, their native parts and saved layouts are unchanged; 8–10 remain available for concurrent owner modules. This is shared/server source plus a required client contract, not a completed native ride.

## Configuration and modules

Metres; anchor +X right, +Y up, +Z along travel, yaw rotates +Z toward +X. The anchor is the flat-bottom plane at X0/Z0; visual centre X4.5/Z0. Parameters in `SkateMegaHalfpipe`: width12 (four 3m lanes centred X0/3/6/9), bottom12 (Z−6…6), two quarter-circle transitions R=3/(2 sin9°)=9.588679832m, deck3 each. Total length37.177359664m; nominal rise9.588679832m. These are authored design parameters, not owner measurements. No L-module three-cell restriction applies.

| Module | Native indices | Assembly contract |
| --- | --- | --- |
| Bottom | 0–15 | Stone, 4×4 floors, centres Z−4.5/−1.5/1.5/4.5 |
| Back/front transition | 16–35 / 36–55 | Metal, five rows of four 3m chords; slopes 9/27/45/63/81°, mirrored |
| Back/front deck | 56–59 / 60–63 | Metal, four floors, centres Z=±(6+R+1.5), Y=R |
| Back/front coping backing | 64–67 / 68–71 | Metal low walls, base Y=R−1, Z=±(6+R+0.3); client coping is specified below |
| Back/front rear guard backing | 72–75 / 76–79 | Wood low walls, base Y=R, Z=±(6+R+3.3) |

All rows share X centres. Wall local long Z rotates to width: yaw270 back /90 front. Transition vertices are `(Z,Y)=(±(6+R sinθ), R(1−cosθ))`, θ=0/18/36/54/72/90°. Adjacent native chord endpoints and lane edges coincide mathematically; actual skin thickness/contact is not proved. The five modules are assembled in one transaction; independent moving, resizing or partial kits are unsupported.

## Server lifetime

Native marked square-foundation confirmation supplies origin/yaw, followed by existing supported-anchor elevation using genuine floor bounds. Admin `skate.items mega-halfpipe`, `skate.piece mega-halfpipe` and typed placement are setup/fallbacks; the normal client route must use owned item identity. Default gesture order stays unchanged; a configured unique order may include the new name. The 25m limit is eye-to-anchor, not eye-to-every-part.

Every native part retains prefab/grade bounds validation, privilege/build hooks, proximity, dry terrain/water and deploy-volume/entity occupancy checks. MEGA additionally checks a whole oriented envelope X−2.15…11.15, Z±(6+R+3.65), Y0…R+0.05+3 for permission and world/construction/deployed/player obstructions, before spawn, before registration and after payment callbacks immediately before commit. Only the verified temporary support and current transaction members are excluded. Existing MEGA metadata reserves this envelope against later park placements, including partial groups; arbitrary vanilla construction and future moving obstructions are not prevented by this reservation. Air above the extra3m remains outside it.

The existing transaction owns all80 native members: complete registration precedes one verified original item debit1→0 and `Paid`; pre-debit refusal retains it. Post-debit rollback refunds only after verified complete cleanup and safe metadata. No external wall is a member. Hammer removal claims recovery before destruction and returns one item only for complete paid groups after all members/metadata are gone. Partial/free/uncertain groups mint none; interrupted states require private recovery.

Version1 metadata stores canonical kind, anchor, indexed native identities/poses and payment state under existing world/checksum, group/file-size guards. Reload reconciles actual native members; it never fills missing modules or automatically mints an item. A partial group retains its reservation until removed. Older source cannot read the new kind: keep matching private world/metadata/source backups. Release pins and live installations are unchanged.

## Exact client riding contract

The client must recognize all80 members before contained small kinds, validate identity/pose/grade and clear the whole replacement on mismatch/removal/reconnect/unload. `TryMatch` supports80 with distinct-member tracking. Preview must use the same origin, yaw, module positions and envelope; IDs8–10 are not automatically valid. Current eight-kind picker/recognition does not implement this.

Use `SkateMegaHalfpipe.TryProfilePoint`: **124** ordered `(forward,height)` samples, extruded X−1.5…10.5 in the anchor frame. If using `VisualOrigin` (anchor+X4.5), subtract4.5 from every mesh/coping X, giving mesh X−6…6; do not apply that centre twice. The 60 equal angular steps per transition (1.5°) include both lips and both bottom joins once; decks end at Z±(6+R+3). Heights include `RideLift=0.05`. The central bottom is Y0.05; upper decks Y=R+0.05. Apply the same transform to visual, ride mesh and normals. Analytic transition normals are `(0,cosθ,−side·sinθ)`; normals remain upward until the vertical lip. Bottom joins share a horizontal tangent; adjacent lane/module vertices must be welded with no duplicate collision lips. The lip/deck corner intentionally meets coping rather than interpolating a diagonal shortcut.

Native chords and low-wall backing are not this smooth riding surface. **Before enabling the replacement**, isolate their colliders from the mounted rider using exact owned collider/body pairs, with verified restoration on exit, mismatch and unload; never change global collision layers. Probe the required native API first. Retain ordinary world collisions and other players. If pair isolation/restoration cannot be proved, leave MEGA riding unavailable; a visual replacement alone is insufficient. No fixed vertical offset guarantees clearance of thick native chords near vertical.

Coping: two cylinders/edge segments, radius0.05, axis +X from−1.5 to10.5, centres `(forward,height)=(±(6+R),R+0.05)`. Ends stay within the reserved margin. Grind/stall capture is intentional and uses actual signed travel/alignment; a backing wall is never an automatic grind. Client guards run along the rear deck edges, height1 above the deck; side exits are open and are not coping. Visual/collision/edge geometry must coincide.

Drop-in must retain the near-vertical normal and signed full tangent velocity, then apply gravity once. Current `Sense` substitutes up for normal.y≤0.3 and wall probes treat normal.y<0.5 as a wall: profile-aware support/wall handling must change before acceptance. Pump uses actual travel-relative descent/bottom timing and existing bounded energy; seams never reset/recharge it. Passive air exit preserves XYZ tangent velocity with zero pop; deliberate pop adds vertical impulse once. Landing uses the actual surface normal and whole incoming velocity with existing impact/bail limits. Uphill stall reverses physical velocity into fakie without a forced nose turn or reverse-speed penalty. Air/grind/bail clear pump state; coping leave/re-entry must not double-land or duplicate trick awards.

Native acceptance remains open: rotated placement/refusal, occupancy in empty riding air, debit/rollback/removal/reload, collider restoration, visible seams/guards/coping, then deck drop-in→pump→opposite air→landing→fakie→coping→exit with real controller input. Managed compilation and geometry/accounting checks cannot establish these results.
