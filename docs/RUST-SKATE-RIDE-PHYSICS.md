# Skate ride physics and client integration

`SkateRidePhysics` supplies pure surface acceleration, bounded transition pumping,
surface-velocity launch and landing roll. It uses double-precision `SkateVector`
values and no Unity types, ground queries or transforms. The native client still
needs an adapter to consume these results; this document does not establish
native collision, controller feel or server acceptance.

## Units and contracts

Up is +Y; zero yaw faces +Z and positive yaw turns toward +X. A supplied nose is
a nonzero horizontal vector, normalized internally. Surface normals must be
upward, have Y greater than 0.3 before and after normalization, and have squared
length within `NormalLengthSquaredTolerance` (0.0001) of one. Horizontal vectors
allow an absolute Y tolerance of 0.00000001.

`SignedTangentSpeed` is speed along the surface, positive toward the supplied
nose and negative tail-first. `HorizontalSpeed` is the X/Z magnitude. They are
different on a slope: at 35 degrees, horizontal speed 13 corresponds to tangent
speed 15.87. Apply a horizontal server/HUD cap using the tangent's horizontal
length; do not impose the same numeric cap on both meanings.

`TrySlope` preserves the horizontal nose heading, solves the tangent's Y from
the plane normal, and projects explicit gravity along that tangent. Its returned
signed slope is positive uphill along the resulting actual travel. Gravity may
stall and reverse signed speed. Push, brake, rolling resistance and steering
remain adapter policy.

Pump slope and `TravelForward` refer to the same actual travel direction, not
the rider's nose, visual board yaw or stance. Supported descent at least 8 degrees
and speed at least 0.5 m/s can charge a crouch for up to 0.4 seconds. When descent
reaches -4 degrees or shallower, a 1.5-second release window opens through
+8 degrees; it ends beyond that ascent. Charge stops accumulating outside the descent.
A release gives at most one award, with descent steepness saturating at 30 degrees.
Flat-only charging and repeated releases on a constant steep slope give no award.

The award is capped at **12 J/kg per traversal**. Delta speed is computed from
kinetic energy, rather than adding a constant speed. At gravity 16 this adds at
most 0.75 m of ideal coast height. Starting at 6 m/s, one full pump raises the
ideal height from 1.125 to 1.875 m, below the authored 2.497 m halfpipe deck.
This depends on entry speed: 11 m/s already carries enough energy for that deck.

An award or a missed release leaves the pass spent. A genuine ascent followed
by a new descent rearms it. Loss of support, speed below 0.5, or physical travel
turning at least 90 degrees relative to the pass's captured direction resets it.
That comparison catches cumulative turns. A board-only switch/revert with
unchanged travel does not reset it. A reset may arm a new descending pass but
cannot award immediately. Air/grind should be supplied as unsupported to the
pump helper; mount/dismount/bail may discard the state. Flat button spam or a
flat timeout alone cannot rearm a spent pass. Separate consecutive one-way
downhill banks without ascent/reversal do not automatically receive new budgets.

`TryLaunch` preserves full surface velocity and adds an explicit nonnegative
vertical pop. Passive departure supplies zero pop. It applies no hidden gravity
or timestep compensation. The adapter applies airborne gravity once.

`TryLand` measures impact as the positive part of minus the dot product of the
complete pre-contact velocity and the normalized contact normal. For roll it
weights the vertical velocity by `FallToRoll`, projects onto the surface, then
onto the board's rolling axis. These operations cannot increase kinetic energy.
Zero and 180-degree alignment preserve the same travel with opposite signed
speed; a sideways landing on flat scrubs all rolling speed. Lateral slip is
reported separately. Impact strictly above the configured maximum sets `Bailed`;
no additional arbitrary yaw-bail threshold is introduced.

Inputs must be finite. Scalar and vector-magnitude limits are 100000; gravity
and maximum impact must be positive, pop/speed magnitudes nonnegative, pump slope
strictly between -90 and 90 degrees, and `FallToRoll` in [0,1]. Steps accept
seconds in (0,0.05]. Heading/travel vectors are normalized; a stopped or
unsupported pump input may use zero travel. Derived slope speed and launch
velocity beyond the bound reject rather than clamp. Derived projection magnitude
allows a relative squared-length tolerance of 0.000000000001 for rounding.
Refusals return static errors and default results; pump refusals retain the
supplied state with zero award and no reset event. A valid impact bail returns
success with `Bailed=true`; take the bail path rather than treating it as a
supported roll. Calls allocate no objects after initialization.

## Constants

| Behavior | Choice |
| --- | --- |
| Explicit gravity | Default 16 m/s²; retained design value, not a measurement of walking-player gravity. |
| Vertical pop | Default 6 m/s; passive exit uses zero. A flat ideal apex is 1.125 m at gravity 16. |
| Landing impact | Default 12 m/s, applied to full surface-normal impact; equality is accepted. |
| Vertical-to-roll weighting | Default 0.8. |
| Pump charge / start / release / full steepness | 0.4 s / 8° / 4° / 30°, relative to actual travel. |
| Pump budget / bottom window | 12 J/kg per traversal / 1.5 s. |
| Fall cap and other handling | Native adapter retains its 30 m/s fall cap, push/brake/roll/reverse/steering/revert choices. |
| Existing shared controllers and tricks | SkateMotion/SkateDrive constants and SkateTricks scoring/stance APIs remain unchanged. |

## Verification scope

The integrated C# 7.3/net48 library compiled against both genuine cached 7815 and
official 7820 composite reference sets with zero warnings and errors. The cached
build's actual DLL was consumed on Mac with .NET 8, without native game execution.

The approved scenarios passed 2,532 assertions: 2,308 for transition pumping,
133 for drop-in and launch, and 91 for surface landing. Three existing recorded
gesture scenarios also passed. An independent boundary and state probe passed
10,573 assertions; each of eight success/refusal paths allocated zero bytes over
200,000 warmed calls. These checks cover the pure arithmetic and managed-call
contracts. Native adapter integration, contact timing, server acceptance and
gameplay remain unverified.

## Numbered client review

The following review is pinned to `SkateRide.cs` at
[`338fb0eb90197e61d65a504ae9477c16dfdf864a`](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/blob/338fb0eb90197e61d65a504ae9477c16dfdf864a/mods/rust/client/src/SkateRide.cs),
merged through PR #352. Its SHA-256 is
`c11f103b0b8df5c31bb7e065ae729f1cfda7ab3f026d4f6c724f7534c0da3e1c`.
This is a read-only review; the helper does not modify those client steps.

1. **Use one speed meaning and one tangent.** GroundStep computes a unit tangent
   at lines 220–222 and gravity against it at 232, but writes horizontal
   `dir * Speed` plus `tangent.y * Speed` at 254–255. That vector is not tangent
   to a forward slope. Use tangent velocity throughout the new helper and
   explicitly convert the client's horizontal speed/cap. If retaining horizontal
   speed, vertical velocity requires the tangent Y divided by its horizontal
   length, rather than tangent Y alone.
2. **Retain the candidate landing normal.** Sense casts far enough for predicted
   air contact at 201–208, but retains the normal only within FeetProbe 0.3 m.
   AirStep at 343–346 can land at larger predicted clearance: downward 20 m/s
   and dt 0.03125 give a 0.645 m threshold. Keep the validated hit normal
   separately from immediate foot support so that such a contact is not treated
   as flat. The adapter must select the actual contacting surface.
3. **Use the pass budget for pumping.** Pump at 282–292 already charges and adds
   speed on release. It uses total surface tilt rather than along-travel slope
   and has no traversal limit. Replace that bonus with the pump state; do not
   run both. Charge only during descent, release through the bottom, and use
   actual travel for direction/reset decisions.
4. **Give gravity one explicit owner.** AirStep uses 16 at 317; GroundStep uses
   engine gravity times SlopeGain 1.6 at 232. At an assumed engine 9.81 that is
   15.696, but the actual engine value was not measured. Use the chosen explicit
   gravity for both surface and air motion. Preserve the late-driver ordering
   and enabled walking component described by the client; do not change walking
   gravity or replace the existing alternative SkateDrive controller.
5. **Carry surface velocity into launch.** Passive support loss at 213 and pop
   at 228 pass vertical fields, while air velocity at 355 is rebuilt from
   horizontal heading/speed. Carry full outgoing tangent velocity into the air
   and add an explicit vertical pop only when requested. Remove the current
   `+ AirGravity * dt` compensation only when the immediate AirStep subtraction
   is also accounted for; gravity must apply once. Retaining flat pop 6 does
   not promise identical ramp arcs if the adapter previously scaled it by Normal.Y.
6. **Measure complete normal impact.** Land at 418–421 projects vertical fall
   alone. Horizontal approach contributes to an uphill impact and reduces a
   downhill impact. Incoming horizontal 6/downward 8 at 35 degrees produces
   normal impact about 9.995 uphill and 3.112 downhill, rather than vertical-only
   6.553 in both cases. Capture velocity before support response and forward
   NormalImpact unchanged to the existing maximum-impact SkateTricks overload,
   explicitly passing the same MaximumImpact used by TryLand. The legacy
   SkateTricks overload defaults to 8, while TryLand defaults to 12.
7. **Use the accepted landing deck orientation once.** NoseYaw/DeckYaw at 78–81
   already distinguish travel, rider spin, board shove and persistent reversal.
   Landing roll at 413–422 uses physical Yaw and does not scrub lateral velocity.
   Build the final rolling axis from the accepted snapped rider/shove result.
   TrickTick clears visual angles at 445 before Take applies new Switch and
   BoardShove parity at 451–454, so retain the pre-landing basis and apply each
   rotation/parity once. Do not combine old accumulated angles with the new
   stance/reversal again. Odd rider-spin switch parity stays in SkateTricks;
   board-only shove does not invent rider switch parity.
8. **Consume landing roll immediately.** After Land, AirStep at 349–350 writes
   a horizontal velocity and clearance correction for the first supported
   frame. Use the full returned surface roll there. Keep support-gap correction
   separate from kinetic speed and impact. The stuck fallback at 342–346 forces
   impact zero; classify that as adapter recovery rather than helper physics.
9. **Separate time, contact and display smoothing.** Step at 129–130 substitutes
   0.03125 for an out-of-range dt; ViewNormal at 153–154 smooths by a fixed 0.3
   each step. Supply a valid fixed tick or bounded substeps to the helper, which
   rejects invalid elapsed time. Use contact normals for physics and time-based
   smoothing for display. Server pull-back/cap feedback at 160–190 is separate
   from transition energy and should not be treated as a gravity clamp.

## Integration order

1. Capture actual pre-response velocity, validated candidate contact normal and
   current physical travel. Preserve the takeoff/deck orientation basis needed
   to interpret any accepted trick snap.
2. For supported ground motion, obtain the unit tangent and explicit gravity
   result. Apply retained steering/push/brake/resistance policy consistently in
   tangent-speed units; keep the final server/HUD cap horizontal.
3. Feed pump slope and travel from the same physical direction and store state
   only on success. Apply its energy gain once; remove the separate release
   bonus. Reset on lifecycle exits and mark air/grind unsupported.
4. On departure, carry the final surface velocity into TryLaunch; use zero pop
   for passive exit. Apply one subsequent air-gravity step and the retained fall
   cap. Do not reconstruct X/Z from a visually rotated board nose.
5. At touchdown, normal impact can be computed before the trick snap because
   it is independent of board yaw. Pass it and the same configured MaximumImpact
   to SkateTricks' maximum-impact overload, then use the accepted snapped deck
   orientation exactly once for TryLand with the captured incoming velocity.
   Respect both impact and unfinished-trick bails. Use TryBail for
   lifecycle/collision bails outside a touchdown.
6. Consume full RollVelocity in the first supported frame and keep physical
   travel, signed along-nose speed, rider Switch and deck reversal distinct.
   Validate native contact, control timing, server acceptance and ride feel in
   the adapter before describing gameplay as verified.
