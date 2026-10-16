# Half-wall kicker contract

The server/shared addition for #408 attaches one Stone native square floor to an existing native half wall. The wall is an external dependency, with its own owner, building and save lifetime; it is never a park member, consumed item, rollback part or demolition/refund member. Client 0.18.6 includes recognition, riding geometry and a wall-target preview; the client build request and complete native acceptance remain open.

## Identity and building flow

`HalfWallKicker = 10`, layout name `half-wall-kicker`, item name `Skate Half-wall Kicker`, marker `0x534B500B`. The carrier is one stock `building.planner`, skin 0, with genuine item UID and the existing exact metadata/name checks. IDs 0–7, their markers and layouts remain unchanged. The integrated catalogue assigns mini/large quarterpipes to IDs 8/9 and MEGA to ID 11. `PieceCount` is twelve, with stable IDs 0–11 and markers `0x534B5001`–`0x534B500C`.

An administrator can obtain the item through `skate.items half-wall-kicker`. The build-menu frontend selects a stock square foundation with that item active, aims at a broad half-wall face and confirms. The server intercepts its original affordability/build request, refuses native carrier creation/payment and schedules the attachment after the original hook returns. It replays the complete original wall-target building hook, then checks every actual ramp part through the normal park transaction. A refused or canceled request retains the item; one committed ramp verifies one debit. No native foundation is created or removed by this route.

Client work must emit the genuine native build request even when its ordinary foundation ghost would be invalid: original planner/item identity, square-foundation construction, actual wall target, world-space aim ray and no parent request. Do not send a fabricated wall entity, a nonzero unrelated socket, arbitrary coordinates or a console placement in the normal controller flow. The native dispatcher can reach `CanBuild` before its own socket/placement validation; this does not prove the current client emits that request.

The administrative aim-only `skate.placeitem` and `skate.place half-wall-kicker` routes resolve the same real wall. Explicit coordinates and the legacy `skate.piece`/gesture foundation-conversion frontend refuse this kind.

## Target, side and exact pose

The target must be the first server ray hit within 25 m, on a broad face aligned with local ±X, with its saved native half-wall prefab, nonzero identity/owner/building, skin 0, upright yaw-only rotation, unit local scale and no parent. The server independently raycasts from the supplied ray within 0.5 m of the current eyes. The requested native target must be that exact hit entity. The actor must be on the selected side. There is one attachment per wall side; a wall belonging to a park group cannot be a host.

The host's full bounds require building permission, plus its building's dominating privilege. Without a privilege, the actor must own the wall. Complete ramp bounds also require permission, neighboring-building checks, all native building hooks, deploy-volume clearance, dry terrain and occupancy. Only this validated wall is excluded from collision/proximity checks; its foundation and all other building members remain obstructions.

`SkateParkLayout.TryHalfWallAnchor(wall, wallBounds, floorBounds, side, out anchor)` is the shared pose authority. `wall` is a `SkateParkBlock` with the exact half-wall prefab, world origin/yaw and zero pitch. Each `SkateParkBox` holds native local bounds centre `X/Y/Z` and `SizeX/SizeY/SizeZ`, in metres. `side` is exactly -1 or +1 along wall local X. Failure leaves a zero transform. The server separately checks roll, scale, parent, identity and authority, which the engine-free helper cannot attest.

The ramp is one floor, pitch -30°; its anchor is the floor's native origin, with no added layout height. Side +1 uses wall yaw +270°; side -1 uses +90°, normalized. The floor's upper **top-surface** edge meets the selected wall face at the wall's top, including floor thickness and local bounds offsets. Floor width cannot exceed the host span. Use the returned anchor with `TryTransform(HalfWallKicker, 0, ...)`; do not call `TrySupportedAnchor` for this kind, reuse the ordinary kicker's pivot or add a second thickness offset.

For a zero-offset 3 m long floor and a 1.5 m wall, top-surface rise is 1.5 m and horizontal run is approximately 2.598 m. The lower slab extends below its lower top edge by its thickness. An adjoining foundation/floor or terrain can therefore refuse occupancy, including ordinary in-cell approaches. This contract does not exempt that obstruction or promise a terrain-level entry. A preview must show/refuse the complete slab envelope; entry, seam crossing and any client-authored approach profile require native agreement before acceptance.

Client preview, visible mesh and riding profile must use that same measured pose and surface. Show a preview only for a genuine owned item and eligible wall/side; clear it on stale identity, blocked ray, missing wall, cancel, rotation/grade change or refusal. Rotation chooses the opposite wall face by a fresh target; it must not free-rotate the ramp away from its host. Server confirmation owns payment/history; a ghost is not placement success. The existing released client has no implementation of this appended kind.

## Persistence, removal and recovery

Version 1 park manifests gain optional `Wall` attachment metadata only for this kind: host ID/owner/building, grade, side, origin/yaw and measured wall/floor bounds. The ramp remains its sole `Members` entry. Old records omit `Wall` and retain their preceding interpretation; old plugin versions cannot read the new kind/field, so downgrading a manifest containing attachments is unsupported.

Parsing rejects missing/invalid attachment data, inconsistent join anchors, duplicate wall-side sockets and hosts appearing anywhere in park members. Early world loading checks the incoming saved host prefab/identity/owner/grade/building/pose/parent before restoring ramp support. Initialization then checks the live host and current native bounds. It never recreates an absent wall or ramp, or adopts another world generation.

Host rotation is refused while an attachment is registered. Host destruction, movement, grade/skin/owner/building/parent/bounds change invalidates the attachment. Confirmed destruction schedules cleanup; a periodic check also detects changes and retries a vetoed removal. Cleanup first persists a nonrefundable recovery claim, then removes only verified ramp members; failures remain tracked. Instance/world/save guards are rechecked across destruction callbacks. A canceled host kill leaves its valid attachment intact.

Hammer demolition of an intact paid ramp can return its single build item once under the existing complete-group/owner/authority/delivery rules, with host validity and privilege rechecked. Free, interrupted, damaged/incomplete or dependency-lost assemblies return no item. The host is never removed, refunded, regrounded or adopted as a park member by attachment cleanup or demolition. Unload retains normal saved park records; reload revalidates their dependency.

## Acceptance limits

The merged [SkateParks.cs](../mods/rust/client/src/SkateParks.cs) recognizes the tilted floor with its supporting wall and implements `HalfWallAim`; [SkatePieces.cs](../mods/rust/client/src/SkatePieces.cs) uses that pose for the held-item ghost. [PR #456](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/pull/456) reports an in-game preview, while explicitly leaving the kicker's client build request unfinished. A visible ghost does not prove emission of the original wall-target request, successful item payment or server placement.

The approved existing layout/build-item scenarios exercise the compiled shared/plugin helpers. The genuine-reference build checks source/API compatibility. Neither establishes native ray/RPC emission, current prefab collider surfaces, hook timing, actual inventory debit/refund, filesystem/world reload callbacks, collision seams, entry/drop-in, riding, unload or two-client observation. These require the paired client implementation and native verification; no live server operation is part of this source change.
