# Regular halfpipe collision

The regular `halfpipe` has 36 native members. The smooth server adapter in
ShortcutSkate 0.3.28 replaces only the physical transition floors, using the
profile ridden by client 0.18.18. The layout, native entities, saved identities,
hammer targets, grades and security policy stay under Rust's ownership.

| Native indices | Role | Physical collider |
| --- | --- | --- |
| 0–3 | Four Stone flat floors | Retained |
| 4–7 | Two lanes of rear 15°/35° Metal transitions | Replaced by four owned patches |
| 8–11 | Two lanes of front 15°/35° Metal transitions | Replaced by four owned patches |
| 12–15 | Four Metal deck floors | Retained |
| 16–19 | Four Wood low walls | Retained |
| 20–35 | Sixteen Metal half walls | Retained |

## Profile and ownership

The adapter consumes `SkateHalfpipe.ProfilePointCount == 40`,
`SkateHalfpipe.Lift == 0.02` and
`SkateHalfpipe.TryProfilePoint(int, out double forward, out double height)`.
Samples are the client's existing rounded 15°/35° profile, before its 2 cm lift,
in the frame centred on the flat. The server applies the lift once. Its common
mesh frame is the native anchor plus the first flat floor's local bounds top;
profile forward coordinates receive the 1.5 m centre offset.

The eight child patches belong to actual native indices 4–11. Each root is a
child of that BuildingBlock, and `ToBaseEntity()` must resolve to that same
verified member. Native metal plates remain the persistent entities and are
never repositioned. The two lanes have identical seams. Internal lane and
lower/upper boundaries have no vertical faces; each combined ramp has closed
outer sides, toe/lip ends and underside.

The rounded toe extends onto the retained flat. Clipping begins/ends at the
client's toe tangent samples, rather than at the nominal native plate edge.
Cuts between the two transition owners interpolate the existing client facets;
they do not replace those facets with a new circular approximation. The flat
between the two ramps and the native decks keep their original physical
colliders. Only the transition original colliders are disabled.

## Safe exchange and restoration

The full 36-member identity, poses, grades, skins and scale must match before
activation. Mesh, parent, frame and captured native-collider checks follow the
MEGA adapter's lifecycle. The replacement is solid before any original is
disabled. Invalid or partial kits retain native collision.

Before installing a new regular ramp, all active and sleeping player bounds
are checked against only the eight prospective patch volumes, with 2 cm of
bounds slack. A body inside a new fill blocks activation. A player outside
those volumes, including the centre of the retained flat, does not block it.
A saved kit retries through the controlled cleanup pass without repeated
warnings; initial placement can fail its normal transaction. Activation never
moves a player.

Removal, failure, upgrades, save/world changes and unload use the existing
occupied-body restoration path. If native collision cannot yet be restored,
the independent Unity owner retains the record until clearance is verified.
The assembly-neutral pending marker remains compatible with 0.3.27. Metadata
faults continue to permit registry-based health/restoration, while placement
and metadata writes remain blocked. No noclip exemption or global collision
layer change is introduced.

## Client handoff and acceptance

The client must isolate only native regular transition floor colliders 4–11
from the rider and its queries. Adding all 36 matched members to `BackingIds`
would incorrectly remove the retained flat, decks and guard walls. Existing
pair restoration must still wait until rider and backing no longer overlap.

Source, build, deployment and riding remain separate checks. Acceptance is one
climb on a server-placed regular halfpipe with zero client put-backs and no
paired native noclip rejection/correction in the bounded server trace. The
shared helper integration, genuine build, native geometry and manual climb
must be verified before describing that outcome as complete.
