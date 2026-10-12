# Darkslide half-flip contract

The scorer accepts the client-observed `Darkslide` contact, not an inferred edge ray.
On air-to-grind contact, Flip must be within 25 degrees of +180 or -180 to use the
inverted catch. Spin/Shove/body half-turn tolerances, finite inputs, dt and impact
limits still apply. Half flips to the ground or any other kind still bail.

| Phase | State/result | Score/event |
|---|---|---|
| Half-flip catch | Airborne=false, Grinding=true, Flip=BoardFlip=+/-180, DarkslideCaught=true | Landed; directional Darkslide name; no air points or new combo link |
| Holding the edge | Retained Flip=+/-180; duration accumulates | Existing combo remains; no repeated air award |
| Airborne pop-out | The retained half is the initial Flip before the new axis integrates; caught flag becomes false | GrindEnded; ordinary duration*190 score/link, directional name |
| Actual remaining half | Drive signed Flip axis until BoardFlip is an upright whole | Normal landing/flip name and points only on touchdown |
| Drop flat/change kind while caught | Inversion and open combo cleared; banked total retained | Bail only, zero grind-end points |

`DarkslideCaught` is a computed property from the retained exact half-turn on a
supported Darkslide; it adds no storage, constructor or struct-layout change. Ordinary
zero-flip Darkslide inputs retain their existing behavior and do not set this flag.
Other non-airborne nonzero flip histories are invalid and reject atomically.

An exit with zero flip input remains at 180 and bails on ordinary touchdown. For an
unturned Ollie exit, +180 plus another +180 finishes +360 (Kickflip); -180 plus another
-180 finishes -360 (Heelflip). Reversing the exit half back to 0 is upright, without a
full-flip award. Completion and unwind retain the captured pop/stance name, including
Nollie, Fakie and Switch prefixes when supplied through the explicit naming input.
The scorer never auto-credits an unfinished turn. No additional pop is inferred while
holding the edge. Takeoff pop/stance and rider/shove/body parity use their ordinary rules.

Claude's adapter must capture a real flipped edge contact and show the retained inversion.
Before pop-out, capture `State.DarkslideCaught` and the sign of `State.Flip`; the flag
becomes false in air while the angle carries through. Apply only the actual remaining target.
Air-trick and grind names stay separate: a completed Kickflip into an ordinary edge and
the final grind remain two events; the incomplete half catch names only Darkslide.
No combined-name feature is added. Shared scalar checks do not verify actual ray capture,
coping/deck contact, pose, controller direction, native cost or playable gameplay.
