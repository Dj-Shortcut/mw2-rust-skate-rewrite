# Skate ride physics and client integration

`SkateRidePhysics` supplies pure surface acceleration, bounded transition pumping,
surface-velocity launch and landing roll. It uses double-precision `SkateVector`
values and no Unity types, ground queries or transforms. The client adapter in
[PR #357](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/pull/357) consumes
these results. Claude reported native flat-trick checks for its earlier client
pin; the landing correction here has no native validation. This document does
not establish ramp collision, controller feel or server acceptance.

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
complete pre-contact velocity and the normalized contact normal. It first
preserves the original surface projection `p`. The extra closing fall is the
downward vertical residual relative to a surface-following vector with the same
X/Z velocity: `min(0, dot(velocity, normal) / normal.Y)`. `FallToRoll` weights
only that residual. The resulting candidate is retained on the velocity segment
from zero to `p`, before projection onto the board's rolling axis. This prevents
fall damping from boosting, reversing or rotating original plane travel, and
cannot increase kinetic energy. Tangent motion is unchanged for every weight;
pure vertical fall converts to weighted roll, while pure normal impact produces
no roll. An opposing fall is not damped if doing so would boost plane speed.
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
| Extra vertical-fall retention | Default 0.8; original tangent motion is preserved. |
| Pump charge / start / release / full steepness | 0.4 s / 8° / 4° / 30°, relative to actual travel. |
| Pump budget / bottom window | 12 J/kg per traversal / 1.5 s. |
| Fall cap and other handling | Native adapter retains its 30 m/s fall cap, push/brake/roll/reverse/steering/revert choices. |
| Existing shared controllers and tricks | SkateMotion/SkateDrive constants and SkateTricks scoring/stance APIs remain unchanged. |

## Verification scope

The integrated C# 7.3/net48 library compiled against both genuine cached 7815 and
official 7820 composite reference sets with zero warnings and errors. The cached
build's actual DLL was consumed on Mac with .NET 8, without native game execution.

The approved scenarios passed 11,683 assertions: 2,308 for transition pumping,
133 for drop-in and launch, and 9,242 for surface landing. Landing coverage
includes repeated seam recontact at weights 0, 0.8 and 1, normal-only and separating
motion, cross-slope lateral motion, and the unchanged full impact threshold.
The new seam scenario fails against the previous DLL and passes against the
corrected one. These checks cover pure arithmetic and managed-call contracts.
The corrected landing policy, ramp contact/timing, server acceptance and ride
feel remain unverified in the game.

## Numbered client integration review

The handover review covers the four client files at
[`d4ab18f`](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/commit/d4ab18f8bf30d56e70feb89c40adf960de4e3ab6)
and the corrections in the current source. These conclusions are source review, not native
execution. The complete client requires generated IL2CPP references that are unavailable in
this Mac checkout; it has not been compiled here.

1. **Keep tangent energy and horizontal feedback separate.** Ground `Speed` is signed tangent
   speed. Ground gravity, pump gains and rolling resistance use it. `HorizontalSpeed`, HUD,
   top speed, push/reverse caps and server limits use X/Z pace, converting through the current
   tangent's horizontal length. A new slope changes the tangent without dividing the old pace
   by that new slope's cosine. Measurement retains the previous emitted X/Z direction and
   magnitude, including grind correction, rather than the board's visual yaw.
2. **Keep physical and visual grind headings separate.** Travel follows the rail heading.
   The captured deck and rider angles are retained separately, without adding Switch/shove
   parity twice. A pop carries rail speed into the air and retains that visual pose; it does
   not launch sideways along a boardslide's deck. Native deck alignment remains unseen.
3. **Apply the cap to emitted velocity.** Landing roll is reconstructed from the capped
   tangent speed. Grind continuation also limits its cached speed, and the final X/Z guard
   includes the pull toward the rail. Airborne vertical fall and ground clearance correction
   are independent of the horizontal cap.
4. **Process every genuine landing contact.** Incoming full velocity determines normal impact,
   including a horizontal approach at zero vertical speed. Impact and unfinished-trick bails
   are resolved before applying roll. Accepted Switch/shove parity determines the rolling axis.
   The stuck recovery path is explicit, discards inferred vertical fall and suppresses impact;
   native contact timing and that recovery policy remain unverified.
5. **Keep revert travel on the surface.** The board can turn while physical travel follows the
   retained revert direction. Gravity, slope, launch and pump travel use that physical tangent.
   Leaving Ground discards pump charge. The face-ray gate keeps a smooth ramp out of step-over
   treatment. Native seams, transitions and revert feel remain unverified.
6. **Own one runtime driver chain.** Comma-separated runtime steps reuse one rig. The last
   scenario selection determines what it runs; it does not create a second Update/late driver.
   Late-driver ordering and the enabled game walking component remain necessary native gates.
7. **Contain logging failures.** A failed file append disables that output and attempts one
   loader warning. Loader logging is best effort too; diagnostics cannot abort movement or cleanup.
8. **Own authored audio resources.** Setup and playback failures and rig destruction dispose
   only the audio object and clips created by the mod. A false sample upload refuses readiness.
   Native creation, upload, playback and teardown still require validation. No game clip inventory
   or game sound sourcing is part of the runtime.
9. **Keep placement instructions consistent.** The client menu asks for a square foundation,
   matching the current server trigger. The actual native piece, save identity, bounds/material
   diagnostics, retry and persistence remain separate server validation.

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
