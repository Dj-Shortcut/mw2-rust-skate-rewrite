# Physics-driven skate components

Engine-free C#7.3 source for [#335](https://github.com/Dj-Shortcut/mw2-rust-rust-rewrite/issues/335) and [#337](https://github.com/Dj-Shortcut/mw2-rust-rust-rewrite/issues/337), namespace `Shortcut.RustMod`. These are client integration building blocks; no Unity/Oxide types occur here. Build `SkateMotion.csproj` with a genuine `RustManagedPath` as for the existing shared motion library. The separate [server acceptance plugin](../plugins/SKATE.md) handles scoped movement bounds; [client architecture](../../../docs/RUST-SKATE-CLIENT.md) records reported native observations and remaining gates.

## Ride physics API

`SkateRidePhysics` provides pure value inputs/results for the reported client's transition pumping, ramp exit and landing calculations. It does not run a controller or query the world. The [API contract and numbered client review](../../../docs/RUST-SKATE-RIDE-PHYSICS.md) describe the adapter; the client's `SkateRide` calls `TrySlope`, `TryPump`, `TryLaunch` and `TryLand` (see the [client README](../client/README.md#sources)), checked natively only on the flat so far. Existing `SkateTricks`, motion and drive APIs retain their behavior.

`TrySlope(input, dt, out result, out error)` accepts an upward unit surface normal, horizontal nose heading, signed speed along its surface tangent and explicit gravity. It preserves horizontal heading, applies gravity along the unit tangent and returns full velocity, signed tangent speed, horizontal speed and travel-relative slope. `TryLaunch(input, out result, out error)` retains the full surface velocity and adds only the requested vertical pop; passive exit uses zero pop. The adapter applies subsequent air gravity once.

`TryPump(state, input, dt, out result, out error)` observes support, crouch, slope along actual travel, nonnegative tangent-speed magnitude and actual horizontal travel direction. Crouching during descent charges up to 0.4 seconds; release in the bottom window grants slope-dependent energy, capped at 12 J/kg per traversal. Flat-only or constant-steep button spam grants none. A completed ascent/new descent can rearm. Air, stopping and a physical turn/reversal clear charge; visual nose/stance changes must not be substituted for actual travel. Retain `result.State` only on success and consume the returned speed.

`TryLand(input, out result, out error)` accepts incoming velocity, contact normal, final horizontal deck nose, impact threshold and vertical-to-roll weight. It computes impact from the whole incoming velocity against the normal, preserves surface-tangent movement and weights only the extra closing vertical fall. Its retained plane velocity stays on the segment from zero to the original surface projection, so damping cannot boost, reverse or rotate plane travel. Board-axis projection retains straight/180-degree travel and scrubs sideways speed; a pure vertical downhill fall converts to weighted roll. `Bailed` is an accepted physical result, distinct from invalid input. On bail the adapter takes its bail path instead of applying roll velocity. Pass `NormalImpact` and the same configured maximum impact to the existing explicit-limit `SkateTricks.TryStep` overload; no scoring API is changed.

Defaults are gravity 16 m/s², vertical pop 6 m/s, maximum impact 12 m/s and fall-to-roll 0.8. Gravity 16 is a retained design value, not a measured native gravity. `SkateMotion`/`SkateDrive` remain separate alternatives with their existing constants. Steps accept `(0, 0.05]` seconds; invalid inputs return a static English error and preserve supplied pump state. No per-call allocation or native type is introduced.

## Drive API

Create `new SkateDriveState(initialYawDegrees)` and keep each successful returned state.
Construct `SkateDriveInput(push, brake, jump, steer, lookYawDegrees)`; Steer is [-1,1], +Z is forward, positive yaw turns toward +X.

```csharp
bool ok = SkateDrive.TryStep(state, input, actualVelocity, grounded,
    groundNormal, dt, maxSpeed, out SkateDriveResult result, out string error);
```

All vectors are `SkateVector`, velocities are world m/s and `dt` is seconds in (0,0.05]. `maxSpeed` is horizontal m/s in (0,14]. Grounded normals must be finite, unit and walkable; airborne may use a zero normal.
`result.State` is the next value state; `DesiredVelocity` drives the body. `BoardYaw`, `BoardPitch`, `BoardRoll` are degrees suitable for Unity `Quaternion.Euler(pitch,yaw,roll)`. `Events` uses existing `SkateEvents.Jumped`, `Landed`, `Blocked`. Rejection returns the supplied state, no events and a static English error. There are no per-call allocations.

Supply actual achieved velocity and reliable support/normal observations once per physics step. Drive applies `SkateMotion.Gravity` and a custom fresh jump to its full `DesiredVelocity`; select one gravity/jump owner. **The reported native client uses its own SkateRide instead of Drive**, preserving downhill reversal, and owns full mounted velocity/ollie/gravity after the game's 32 Hz step while PlayerWalkMovement stays enabled. Drive remains an alternative building block, not the integrated native ride model. An adapter choosing game-owned vertical motion must pass Jump=false, consume only desired X/Z and preserve achieved Y/game jump; an adapter choosing full XYZ must prevent competing gravity/jump writes and restore owned state on exit. Neither Drive adaptation has been verified natively. The step moves no transform and performs no collision/ground query. Default server eligibility is automatic; native server acceptance still requires installation and verification.

Ground motion projects the achieved horizontal velocity along the heading, applies push, rolling loss, brake and tangent gravity, then returns ground-tangent velocity. A wall's lost velocity is not kept as stored propulsion. `Blocked` signals an achieved-speed shortfall exceeding both 0.1 m/s and 25% of the previous request. Upward slopes stall without reversing; downhill motion accelerates up to the horizontal cap.
LookYaw follows the shortest angular path; look and steer each contribute at most `90 * min(1, observed horizontal speed / 4)` degrees/s, including in air. Both turn rates are zero at rest. Air horizontal velocity remains the actual observed velocity, capped to maxSpeed; vertical velocity receives gravity with a downward fall cap. Air board pitch/roll are zero.
Jump starts only on a fresh grounded press. `TakeoffPending` preserves rising velocity when the support sensor remains true briefly after jumping. `Grounded` records the supplied support observation; `Landed` requires its false-to-true edge after a successful step. Held Jump cannot retrigger on landing.

## Park layout API

`SkateParkLayout` defines the eight native park assemblies in metres, +X right, +Y up and +Z forward. The existing part counts, materials, prefab paths and `TryPart`, `TryTransform`, `TrySupportedAnchor`, `Name` and `TryKind` contracts are preserved. `SkatePark.cs` is canonical; the self-contained server plugin embeds its identical marked block, checked by `publish-check`.

Halfpipe walls use native local-Z length (3 m) and local-X thickness (the owner reported approximately 0.2 m). End rails have local yaw 270/90 degrees; left/right side panels have 180/0 degrees. The sixteen ride floors and all floor-only pieces keep their existing transforms. This corrects the wall-axis assumption in new layouts; `TryMatch` now expects those corrected headings, so an older halfpipe with the preceding wall headings is not recognized as the current complete layout. Stored server group records use their recorded poses and are not migrated by this helper; the server parser also accepts the preceding halfpipe wall headings. The client’s separate layout copy needs the corresponding heading change before recognizing a newly placed halfpipe. The existing approved layout scenarios check wall footprints, adjoining rail ends and rotated anchors against both actual shared and plugin DLLs. Native collision/contact and visible full-halfpipe acceptance remain pending.

`TryAnchor(kind, index, partX, partY, partZ, partYaw, out anchorX, out anchorY, out anchorZ, out anchorYaw)` reverses a known part's translation and yaw. It does not validate an observed prefab or pitch. Coordinates must be finite within ±100,000 m; yaw must be finite within ±1,000,000 degrees and is returned normalized to [0,360).

`SkateParkBlock` holds readonly Prefab, X/Y/Z, Yaw and Pitch fields. `TryMatch(kind, blocks, tolerance, out anchorX, out anchorY, out anchorZ, out anchorYaw)` checks a known candidate kind against exactly its complete part count, up to 36. The caller retains its stable `IReadOnlyList`; the helper allocates no collections. Observed coordinates have the same bounds, and yaw/pitch must be finite within ±1,000,000 degrees. Prefab paths match exactly, position uses Euclidean distance, and yaw/pitch use shortest wrapped differences. One finite tolerance in [0,0.1] applies in metres and degrees; zero means exact floating-point comparison.

The anchor follows a deterministically selected observation, ordered by prefab, X/Y/Z and normalized yaw/pitch, paired with the known layout's parts. Tolerance is measured from that observed-part-derived anchor; this does not search for any possible best-fit anchor. Opposing ±0.009 m errors can therefore fail at tolerance 0.01 even when an ideal common anchor exists. Choose the allowance for relative native rounding. Missing, extra, reused or ambiguously matching observations fail, as do valid anchors separated by more than that tolerance. Reordering a fixed input set does not change the selected anchor. All failed calls leave four zero outputs.

This verifies geometric consistency, not park registration, ownership, grade or build permission. A single floor can fit both ledge heights with different anchor heights; a symmetric reflected pose set identical to the original cannot be distinguished. The adapter must validate zero roll, positive unit scale and any native parent constraints before creating this yaw/pitch-only observation tuple.

`VisualOrigin(kind, out right, out forward, out height, out yaw)` returns the nominal local authored-asset pivot offset: bank/kicker/quarter use the first floor's low edge, halfpipe uses the flat centre (1.5,1.5), and ledges/funbox use the layout baseline. Rotate right/forward by the anchor yaw before translating the visual node. All returned heights and local yaws are zero. This relation does not determine native floor-top thickness: the adapter adds the measured surface offset in the correctly rotated local normal. An origin mapping does not make different asset and native collider profiles coincide.

## Park build-item API

`SkateParkBuildItems` provides the eight exact English names and authored metadata tags for stock `building.planner` items with skin 0 and amount 1. `Name`, `Marker`, `TryKind` and `TryIdentity` share stable `SkateParkKind` order; `Tagged` reserves the complete marker range, including unrecognized tags that must not become ordinary building plans. Invalid lookup outputs are atomic. The plugin embeds the identical marked helper.

`VerifiedDebit(before, after, sameItem)` accepts only the original identity changing 1→0. The native adapter must verify its full UID/definition/metadata/name/skin fingerprint and execute actual item consumption. `Recoverable(state, members, kind)` accepts only a complete `Paid` group; `None`, `Preparing`, `RecoveryClaimed` and partial groups refuse. These predicates do not implement inventory, persistence or exactly-once delivery. The [building-asset contract](../../../docs/RUST-SKATE-BUILD-ASSETS.md) records native/client acceptance and crash/recovery limits.

## Board mesh API

`SkateBoardMesh.Create()` returns fresh arrays: `Positions` and `Normals` are xyz floats, `Triangles` are zero-based indices, `Colours` are rgba floats per vertex. Readonly fields protect references; each caller can edit its own array elements. Create once and cache the mesh; generation allocates.
Metres, +Z forward, +Y up, origin at ground contact centre. The deck is 0.80 x 0.20 m with raised nose/tail and dark grip on top; two metal trucks carry four 0.055 m diameter wheels touching y=0. Overall bounds: x +/-0.102, y 0–0.142, z +/-0.40 m. There are 1,524 vertices and 508 triangles, with flat outward normals and [Unity clockwise front-face winding](https://docs.unity.com/en-us/engine/6000.7/manual/assets-and-media/asset-types/mesh/get-started-with-meshes/anatomyofa/index-data).
Client integration converts triples to engine vectors and quadruples to colours, then assigns the mesh arrays. Use a material/shader that displays vertex colours to see the grip/wood/metal/wheel colours; shader names alone do not prove support. No UVs, textures, submeshes, collider or engine material is supplied.

`SkateBoardMesh.CreateDetailed()` and `CreateDetailed(int seed)` add a deck with concave, curved kicks and rounded outline/edges, plus separately movable trucks/wheels. The seed changes the authored texture only; all int seeds are accepted. Generate once at setup and cache. Each result owns independent Positions, Normals, Triangles, Colours, `UVs` (two floats/vertex), `Parts`, `TextureRgba`, TextureWidth and TextureHeight. The original Create path retains identical four arrays; its new fields are empty with zero texture dimensions.

The detailed mesh has 2,175 vertices and 2,816 triangles, with bounds x +/-0.104, y 0–0.142 and z +/-0.402 m. The deck top centre is y=0.092; transverse concavity rises 8 mm to the sides, curved kicks begin at |z|=0.245, and the rounded wood edge extends the outline by 2 mm. Wheels have 0.027 m radius and 0.032 m width, with rounded shoulders, centres (x +/-0.082, y 0.027, z +/-0.245) and rest contact at y=0.

`SkateBoardPart` exposes Kind (Deck/Truck/Wheel), ParentIndex, VertexStart/VertexCount, IndexStart/IndexCount, PivotX/Y/Z and AxisX/Y/Z. Index ranges count **entries in Triangles**, in multiples of three. Seven non-overlapping complete ranges are ordered deck 0, trucks 1–2, wheels 3–6. Deck parent is -1; truck parent is 0; wheels parent their truck. The deck includes fixed baseplates/bolts; truck ranges contain steering hangers/axles. Pivots and unit axes are in the board's common coordinate frame; wheel axis is +X, truck axes follow their angled kingpins.

Split the ranges once into mesh objects: copy a part's vertices minus its pivot and rebase indices by VertexStart. Place its transform at Pivot relative to the board, or at child.Pivot-parent.Pivot relative to its parent. Copy matching normals/UVs. Wheel rotation uses its axle axis and travelled distance/radius, then inherits the parent truck's steering rotation; positive +X spin corresponds to forward +Z travel. Truck steering uses its kingpin axis with **opposite signed angles**: nose (+z, part 2) +theta, tail (-z, part 1) -theta. Equal signs would make both axles steer in the same direction. The client chooses theta from lean, elapsed distance and colliders; no engine transforms/materials are created here.

The texture is 256×256 row-major RGBA bytes, row 0 at v=0; create one texture and share it among the parts. UVs select seeded grip grain, an original underside graphic, wood edges, metal and wheel regions of that atlas. Keep the UVs and atlas together. There are no external logos or assets. Native lighting, texture upload/filtering and moving-part appearance require client verification.

The integrated genuine-reference net48/C#7.3 library builds with zero warnings/errors. A private consumer of that actual DLL passes 121,860 geometry/atlas checks: original array hashes, contiguous part coverage, unit axes/normals, outward nondegenerate faces, closed consistently wound solids, wheel contact/dimensions, curved deck shape, seed determinism, geometry/UV independence from seed and independent arrays. An independent consumer passes 50,781 API/shape checks, including the full rounded edge profile and counter-steering convention. A textured software preview uses those same arrays and pixels; it is a static numerical render, not a Rust scene. These checks do not verify native shaders, filtering, part animation or gameplay.

## Tricks and scoring API

Start with `new SkateTrickState(ridingSwitch)` (default means the unturned rider stance; regular/goofy is a separate adapter choice). Call `SkateTricks.TryStep(state, new SkateTrickInput(spin, flip, grab, airborne, grinding, impactSpeed), dt, out result, out error)` once per observation, with axes in [-1,1], seconds in (0,0.05], mutually exclusive airborne/grinding and a finite nonnegative downward impact speed. Capture velocity **before** the physics landing response removes it; ImpactSpeed is only used on the airborne-to-supported transition. Keep `result.State` only on success. Invalid calls retain it and produce no events.

`TryStep(state,input,dt,maximumLandingImpact,out result,out error)` allows a finite landing-impact limit in (0,100000] m/s. The original overload forwards with `SkateMotion.MaximumLandingFallSpeed` (8 m/s), preserving its default. Supply the actual pre-contact impact; do not clamp it to bypass the default. The chosen limit changes only the impact bail, not spin/flip tolerance, scoring or input/state validation. Invalid limits reject atomically even on a step without touchdown.

`BoardSpin`/`BoardFlip`/`BoardShove` are accumulated degrees relative to takeoff. Spin rotates rider and board at `RotationRate`; flip rotates the board about its long axis at `FlipRate`; shove rotates the board under the feet at `ShoveRate` (720 degrees/s). Shove is relative to the rider: world deck yaw adds Spin+Shove, so a Bigspin is rider 180 plus a same-direction relative shove 180. Supply Rider a support frame following Spin only, excluding board-only Flip and Shove. Keep the deck's nose heading separate from Drive's physical travel heading: Drive clamps negative along-heading speed to zero, so feeding a rotated nose back into Drive would erase forward travel on a valid switch landing.

Ground landing requires Flip within 25 degrees of a whole turn, and Spin/Shove within 25 degrees of half turns, at impact no greater than the chosen limit (8 m/s by default). A [Darkslide half-flip catch](DARKSLIDE.md) is the sole inverted-contact exception. A successful touchdown returns those **snapped** angles; commit them to persistent visual headings before the next step resets them. An odd rider half-spin toggles `result.Switch`; shove alone does not. The next input's mirror rule follows the resulting stance. The client supplies its continuously held analog Spin axis, rather than an automatic 180. Positive Spin/Shove is frontside, negative backside; positive Flip is kickflip, negative heelflip.

The additive input constructor is `SkateTrickInput(spin,flip,shove,grabKind,airborne,grinding,impactSpeed,manual,noseManual,rollingSpeed,popKind)`. Axes stay in [-1,1]; enum values must be defined. At takeoff `popKind` selects Ollie or Nollie (None defaults to Ollie); subsequent None preserves that takeoff kind. Names include Pop Shove-it, Varial Kickflip/Heelflip, 360 Flip, Laser Flip, Hardflip, Inward Heelflip, Frontside/Backside Flip and Bigspin, plus composed 540/720, double-flip, shove and named-grab variants with a Nollie prefix. Names are cached English strings: no per-step construction. The old constructors retain their original names/scoring and map bool grab to Legacy. The composed path supports rider rotations through 720, flips through two turns and relative shoves through 360; greater rotations use a cached `Multiple Rotation Trick` name. Rotation scoring caps rider half-spins at four on the composed path, and flips/shove half-turns at two.

Events: `Landed`, `Bailed`, `GrindEnded`, `ComboBanked`, `ManualEnded` (new bit 16; prior bits unchanged). `TrickName`/`Points` identify each event; `GrindSeconds` exposes live duration or the completed duration on exit (name `Grind`, 100 points/second, rounded, minimum 1). A supported grind capture can land an air trick and begin the grind in the same call. Each completed trick/grind/manual increases the combo multiplier, capped at 16. `ComboPoints` is open base points times multiplier, including provisional manual points while active; 1.5 seconds of plain rolling banks it into `TotalPoints` and reports `BankedPoints` with name `Combo`. Air, grind or an eligible manual suspends that rolling timer. Bail discards the open/provisional combo and retains the banked total. Call `TryBail` for collision/lifecycle bails outside touchdown; do not manufacture a landing. Totals saturate at 10^15. These value-state calls allocate no memory after static initialization.

The eight-argument input constructor adds trailing `manual,rollingSpeed`; the original six-argument constructor supplies false/zero. Supply a held flag and actual horizontal speed magnitude in [0,100000] m/s, including reverse travel. A manual starts/stays active only while supported, not grinding, speed >`MinimumManualSpeed` (0.01 m/s), and no touchdown bail. Ineligible holds are ignored and do not suspend plain-rolling banking. `State.Manualing`/`ManualSeconds` and `result.Manualing`/`ManualSeconds` expose activity/duration (cap 86400 s). The adapter supplies input, tipped-board visuals and pose changes.

Manual score is duration*`ManualPointsPerSecond` (100), rounded to nearest integer/ties-to-even, minimum one for positive duration. While active, event-free results show static name `Manual` and live Points; ComboPoints includes one provisional link without changing completed ComboBasePoints/ComboCount. Release, stopping or entering air/grind emits ManualEnded and finalizes exactly one duration/link; the exit result retains duration while state resets it to zero. Active/exit steps reset rolling timeout. Landing or grind-exit may start a manual in the same call; its completed event keeps name/Points priority. An explicit or touchdown bail drops the pending manual without scoring; banked totals survive. Invalid speed/state/step rejects atomically. Native manual input/pose/gameplay are not verified.

Nose manual uses `manual=false,noseManual=true` with the same eligibility/scoring. `State.Manualing` and `result.Manualing` cover either kind; `NoseManualing` distinguishes the nose. Switching kinds finishes the previous duration/link once and starts a fresh duration. Holding both flags is rejected. `SkateGrabKind` is None, Legacy, Backside, Frontside, Double, Tail, Nose, Stalefish, Crail, Seatbelt, Melon, Method, Nosebone or Tailbone. A held airborne grab kind is retained for naming after release; switching kinds retains the most recent kind.

Root's actual net48 DLL passes 509 focused manual transition/score/timeout/eligibility/bail/atomic/saturation assertions, plus the 2,439 existing trick scenarios and 920 maximum-impact/parity checks. Warm 100,000-call held-manual and invalid-manual loops allocate zero measured bytes on Mac net8. Independent source review passes; these tests do not prove native manual control or visuals.

Private execution of the actual compiled net48 DLL passes 2,439 assertions for all named spin/flip/grab variants, tolerance and impact boundaries, switch reversal, chaining/timeout, grind duration/capture, explicit and touchdown bails, atomic rejection, plus zero measured bytes across 100,000 valid steps on Mac net8. This does not establish Unity/IL2CPP behavior, controller feel or actual impact sensing.

### Grind vocabulary

The kind-aware `SkateTrickInput` constructor adds trailing `grindKind,grindFrontside`.
Supply the contact classification while grinding; the final supported frame's kind and side
name the completed grind. `GrindName(kind,frontside)` returns a cached English name;
`GrindRate(kind)` returns points per second, rounded to nearest integer/ties to even on exit.
`IsStall` identifies only values 11–15, which have a 100-point exit minimum. Other kinds
have a one-point minimum. Existing enum numbers and mappings remain unchanged.

| Appended kind | Value | Points/second | Entry for the client adapter |
| --- | --- | --- | --- |
| `Bluntslide` | 16 | 170 | Tail contact from a tail stall, with the deck pitched past vertical over the edge |
| `Nosebluntslide` | 17 | 170 | The corresponding nose contact and pitch |
| `Darkslide` | 18 | 190 | Flip onto the edge with griptape against it and wheels upward |

These values produce `Frontside`/`Backside` names without Switch/Fakie prefixes. Airborne
pop stance is tracked separately. An unknown input grind kind sanitizes to `None`/`Grind`
at 100 points/second; an unknown kind retained in state rejects the next state operation
atomically. This library scores an observed contact; it does not detect entries, rotate
the deck or enforce those contact shapes. Client detection, `GrindDeck` poses, native
contact and owner play acceptance remain separate work.

On an airborne-to-Darkslide transition only, Flip within 25 degrees of +/-180 snaps
to that half turn; other catch/impact guards still apply. `State.DarkslideCaught` exposes
the retained inverted half on the edge, independently of the kind's ordinary zero-flip
legacy behavior. The catch emits `Landed` with its directional Darkslide name and zero
air points; the grind duration scores on pop-out. The exit carries the half into the
next flight; the adapter drives the remaining half toward an upright whole turn. A
ground drop or contact-kind change while inverted bails without awarding the grind.
See [the complete carry/events and client contract](DARKSLIDE.md).

The owner-approved `skate_trick_grind_names` scenario passes 25,535 checks on the actual
genuine-reference net48 DLL, including the original 20,850 vocabulary/scoring checks,
signed half catches, inversion carry, actual exit completion/unwind, drop bails, all
orientation/impact guards, explicit Fakie/Switch/pop inputs and atomic invalid state/request refusal.
Warmed completion/lookup loops allocate zero measured bytes on Mac net8. The three
owner-recorded flick scenarios also pass. These results do not verify native capture,
deck poses, IL2CPP costs or gameplay.

## Stick composition API

Keep successful `SkateFlickResult.State` and call `SkateFlick.TryStep(state,new SkateFlickInput(x,y,airborne,mirrored),dt,out result,out error)`. Stick axes are [-1,1], +Y forward; dt is seconds in (0,0.1]. Each step describes the position held during that interval. For timestamped changes, replay the position that just ended, subdividing a long hold with the **same** position before advancing to the new one. This preserves flicks between render frames.

`result.Composition` is a `SkatePop` with Kind (None/Ollie/Nollie), signed FlipTurns, signed ShoveDegrees and Strength. `result.Pop`, Charge, Charging, Manual, NoseManual and the existing event bits/constructor remain available. A late flip has Kind=None and a signed flip turn; a held pop has Ollie/Nollie and strength in [0.6,1]. Consumers apply a composition once when emitted, then drive the trick axes until its requested rotations finish. Composition angles are targets, not additional accumulated orientation.

The convention is regular heel-left: back-to-forward-left gives kickflip, right gives heelflip; Mirrored reverses X for goofy/switch. Nollie reverses front/back. Held diagonal setups, signed rim sweeps and the settled forward diagonal distinguish varials, 360 flips/lasers, hardflips/inward heels and 180/360 shoves. Arrival angle alone is insufficient: the owner's straight and diagonal arrivals overlap. Manuals need 0.2 seconds in the manual band. Invalid axes, dt or state return the supplied state and no event with a static error.

The three exact owner-recorded timestamp sequences are permanent scenarios in [approved_tests/skate](../../../crates/approved_tests/skate/README.md): airborne pullback gives Ollie, grounded diagonal gives Ollie+Kickflip, and grounded roll/recoil gives Ollie without a manual. They test recognition and the composition contract; native controller feel remains a client verification step.

### Late flips and rider-only body varials

The additive flick input is `SkateFlickInput(x,y,airborne,mirrored,leftX,leftY,poppedInAir)`.
The original constructor supplies zero left-stick axes and `false` pop context. The adapter
sets `poppedInAir` after the current flight's real pop and clears it on support/bail; the
reader cannot infer whether a requested ground pop actually launched. While airborne with
that context, a subsequent held right-stick composition emits flip/shove targets with
`Kind=None`, `Pop=0`, `Strength=0` and `Composition.Late=true`. Existing lateral AirFlicks
also expose `Late`; their recognition and event bits are unchanged. A first pullback in an
already-airborne recorded gesture remains an Ollie with the original constructor. Neutral
rearms the right-stick reader; each emitted composition is applied once.

The left stick reads independently. A complete net 360-degree circle at radius at least
`HeldZone` within `BodyCircleSeconds` emits `BodyVarial` (bit 64) and signed
`BodyVarialDegrees=180`, mirrored to -180. Clockwise is positive using `atan2(leftX,leftY)`.
Adjacent angle changes greater than 90 degrees, reversal, center/radius interruption or
timeout discard partial progress. A completed circle latches until support, preventing
repeated body-varial events during one flight. Right-stick composition is separate and
can occur on the same frame. A partial circle or a held direction emits nothing.

The additive scorer input appends `late,bodySpin` after `grindKind,grindFrontside`.
`bodySpin` is a separate axis in [-1,1]; it integrates at `RotationRate`, capped at +/-180
degrees per flight. Drive it toward the emitted body-varial target, then supply zero.
Keep the original pop kind; a late target has no additional pop. Mark the board-only
flip/shove axes `late=true` while applying the late target. Old scorer constructors
supply `false,0`. `State.BodySpin` accumulates the rider-only angle and `result.BodySpin`
retains its snapped landing angle; `RiderSpin=BoardSpin+BodySpin`. Deck world yaw remains
`BoardSpin+BoardShove`, and its roll remains `BoardFlip`. The rider support frame follows
`RiderSpin`. Commit both separately on landing; body rotation must not turn the board.

The full overload can additionally append `tailLeads`. It captures the physical deck end
with `PopSwitch` at takeoff as `State.PopTailLeads`; later input changes do not rename that
flight. Explicit calls select Fakie for `PopSwitch && PopTailLeads`, Switch for
`PopSwitch && !PopTailLeads`, and no stance prefix otherwise. Nollie remains explicit,
including `Fakie Nollie` and `Switch Nollie`. Old constructor signatures retain their
historical naming and scoring. The new fields change struct layout: rebuild adapters.
See the [complete stance/pop/rotation table and normalized-axis contract](TRICK-NAMES.md).

Body catching uses the same 25-degree half-turn tolerance and impact/bail rules. An odd
body half-turn toggles riding stance in addition to the original rider/board half-turns;
two odd turns cancel that parity. A completed body half-turn adds 100 base points. Late
has no score bonus. Support/bail clears the new histories. Cached names include
`Late Kickflip`, `Late Pop Shove-it`, `Body Varial`, `Body Varial Kickflip` and combined
late/body variants, with existing stance/pop/grab prefixes. Late metadata labels the
final accumulated board composition, rather than naming each sequential flip separately;
zero completed board rotations never become `Late Ollie`.

Existing public methods/constructors remain; the added state/input/result fields change
struct layout, so rebuild adapters with the shared DLL. Three extra name tables and
fallback variants are created at static initialization; subsequent stepping/naming uses
cached strings. Native IL2CPP startup/memory cost, left-stick conflicts with steering,
input timing, body/deck application, catching and visuals remain unverified. These APIs
read and score authored input; they do not change the client adapter.

The approved `skate_late_flips_and_body_varials` scenario passes 3,703 checks on the
actual genuine-reference net48 DLL, including late no-boost targets, signed circles,
simultaneous events, interruption/timeout/latch, separate body/board angles, stance,
catch/bail/reset, old constructors, cached names and atomic invalid requests/state.
Warmed modified-name landings and left-stick steps allocate zero measured bytes on
Mac net8. The three original recorded flick scenarios also pass. Native acceptance
and performance remain unverified.

The integrated genuine-reference net48 DLL passes those three scenarios, 238,534 private gesture assertions and 205,900 private trick assertions across 6,300 compositions. Checks cover mirrored/nollie chart paths, ordinary pullbacks through 40 degrees remaining plain flips, settled diagonals, stale setup, snapped landing tolerance/impact, 540/720 generic grabs, rider-only stance parity, manual changes and atomic refusals. Warm gesture success/rejection and trick landing/rejection loops allocate zero measured bytes on Mac net8. These checks do not prove native input, catching or server acceptance.

## Rider joint API

`SkateRiderRig(pelvisHeight, hipWidth, thighLength, shinLength, footLength, spineLength, shoulderWidth, upperArmLength, forearmLength, neckToHeadLength)` stores finite metre measurements in [0.01,5]. Widths are full spans; foot is ankle-to-toe, spine pelvis-to-chest, head neck-to-head. Neck is 0.12*spine above chest. These require the real skeleton's measurements; no Rust dimensions are presumed.

`SkateRiderInput(boardPosition, boardForward, boardUp, stance, ridingSwitch, speed, leanDegrees, crouch, airborne, pushing, pushPhase, flipDegrees, grab, bailed, lookYawDegrees)` uses a caller-defined origin within +/-100,000 m, nose forward following rider Spin before board-only Shove, and **unflipped** support up (Y>0). The frame is near-unit/perpendicular (squared-length/dot tolerance 0.0001). Pass flip independently; a flipped visual BoardUp would rotate the rider incorrectly. Speed [0,14], lean [-20,20] degrees, crouch/phase [0,1], flip/look magnitude <=3,600,000 degrees. Yaw zero looks +Z; positive turns +X. Nominal ankle anchors are BoardPosition+BoardUp*SkatePose.FootLift, offset along the nose by +/-SkatePose.FootOffset.

The second constructor adds trailing `deckOffset`, finite metres in [-5,5]; the original supplies zero. It places the nominal grab deck at BoardPosition+BoardUp*deckOffset without moving ankle anchors. For the client adapter's ankle-frame origin (`deckTop+ankleHeight-FootLift`), deckOffset=FootLift-ankleHeight=-0.085 m for the reported rig. The separate horizontal heel-edge shift remains the adapter's responsibility.

The third constructor appends `groundDrop,bailPhase`; earlier constructors supply zero for both. GroundDrop is finite [0,5] m: the drop from the riding ankle plane to the planted-ground ankle plane, rather than sole clearance. A planted pushing/bail ankle uses BoardPosition+BoardUp*(FootLift-GroundDrop). BailPhase is finite [0,1] and advances once through a bail; it does not wrap. Inputs are validated even when a mode ignores them. The conservative joint floor is BoardPosition+BoardUp*(min(0,DeckOffset)-GroundDrop) during an effective push or bail, and BoardPosition+BoardUp*min(0,DeckOffset) otherwise. An effective push requires Pushing&&!Airborne&&!Bailed. The reported adapter passes GroundDrop=0.085 and DeckOffset=-0.085 from an origin 0.17 m above contact, so its bail floor is at contact.

`SkateRider.TryCreate(rig,input,out pose,out error)` returns world-space value-type `SkateRiderPose`: Pelvis; Left/RightHip, Knee, Ankle, Toe; Chest, Neck, Head; Left/RightShoulder, Elbow, Hand; unit PelvisForward/Up, ChestForward/Up and HeadForward. Failure returns default pose/static English error. All 18 joints stay on/above the conservative floor (within numerical tolerance); impossible proportions are refused. A tilted frame can put a joint lower in world Y while it remains above this plane. The adapter converts chains into bone rotations and must apply the returned torso twist.

The additive full constructor replaces `bool grab` with `SkateGrabKind`, retaining all other parameters including DeckOffset/GroundDrop/BailPhase. Old bool calls use None/Legacy with the same pose. Backside reaches the heel edge with the leading hand; Frontside the toe edge with the trailing hand; Double reaches opposite edges. Stalefish reaches the rear heel edge; Crail/Seatbelt cross toward the nose/tail. Tail/Nose target the literal frame -Z/+Z ends and choose the hand on that foot's side, including switch. Melon/Method change tuck and reach; Nosebone/Tailbone extend the named leg. These are geometric targets with reach clamping, not guaranteed mesh contact. Supply a rider frame including Spin but excluding under-foot Shove and Flip; the client owns grip/catch transitions while the visual deck rotates.

Regular puts the left ankle toward the nose; goofy mirrors it. Switch preserves nose-relative anchors, reverses travel and selects the rear pushing foot. Rolling pelvis/chest turn 12/28 degrees toward travel, with front/rear toes turned 25/5 degrees; arms hang lower and spread in air/carves. The closed push stroke returns exactly to its deck pose at phase 0/1. It shifts weight to the leading foot, turns the torso toward travel, plants/sweeps the rear foot, lifts its heel and counter-swings the arms. Toe direction/heel lift preserve exact ankle-to-toe length. Airborne/bail suppress push. Non-whole air flips lift the feet; whole flips return them.

A grab first tucks the pelvis, rolls the reaching shoulder down by 0.4 rad and uses the least reachable chest fold up to 1.0 rad. Its rolled-shoulder reach calculation is analytic, avoiding discrete search steps. An unreachable target remains reach-clamped; actual board contact is not guaranteed. BailPhase interpolates a stumble, a low hands-reaching pose and a recovering crouch through phases 0/.3/.62/1; its feet target the ground ankle plane. Entry/exit blending, board separation and body/ragdoll/lifecycle transitions remain adapter responsibilities.

Crouch lowers requested pelvis height; a common height interval keeps both legs reachable. Root-to-ankle distance uses the conservative shell sqrt(abs(thigh²-shin²))+1e-6 through thigh+shin-1e-6, avoiding folds through an endpoint. Impossible fixed-foot spans reject. Hands clamp into the arm triangle shell; exceptional proportions use a ground-safe horizontal balance target. Analytic two-bone IK preserves exact segment lengths and adjusts poles to keep knees/elbows above the floor. Every call is allocation-free after initialization.

The corrected look pass's actual net48 DLL passes 5,639,465 focused assertions and 4,057,609 existing geometry assertions on Mac net8. The focused sweep includes 32,032 accepted measured-rig push/bail poses across regular/goofy and switch, offset/drop cases, exact lengths, finite/unit/floor outputs, input rejection, closed push seams and grab reach. At phase step .001 the largest bail joint delta is 0.012566 m; at crouch step .0001 the largest grab delta is 0.003331 m. Twelve thousand 1e-8 input pairs remain continuous within the fixture thresholds. Warm 10,000-call bail and one-million-call valid/invalid loops each allocate zero measured bytes. These are geometric checks using reported/inferred fixture dimensions; they do not establish native appearance, mesh contact, transition feel or IL2CPP allocation.

The named-grab extension's integrated net48 DLL passes 34,098,041 private checks over 524,020 accepted geometry poses, including 6,912 bool poses bit-identical to the baseline and 624 mirrored stance poses. All 52 measured-rig kind/stance/switch hand targets have zero nominal grip gap; other proportions can still reach-clamp. Nosebone/Tailbone use a restrained relative leg extension with a low body and opposite-end grip; they do not presume a fully straight native leg. Warm 100,000-call valid/invalid loops allocate zero measured bytes. Actual animation, grip-to-mesh contact and adapter blending remain unverified.

The client adapter must also own the local held-item/arm visibility while using a chase camera, refresh that ownership on item replacement and restore saved visibility on first-person/dismount/teardown. A settled bail board needs an independent world anchor; textures/materials allocated for a failed lit-material attempt must be released before fallback. These client follow-ups are recorded in the [pinned visual-pass source review](https://github.com/Dj-Shortcut/mw2-rust-rust-rewrite/pull/342#pullrequestreview-5478791980). Native lighting, held-item transitions, torso deformation and entry/exit easing still need client verification.

## Rider secondary motion API

`SkateRiderMotion.TryStep(state,rig,target,dt,out shown,out next,out error)` adds bounded secondary motion after `SkateRider.TryCreate`. Pass the same explicit `SkateRiderRig` that produced the target; a pose does not contain its limb measurements. Begin with default `SkateRiderMotionState`, retain `next` after success, and use default state to deliberately reset a contact or mode transition. Existing rider APIs are unchanged.

The additive overload is `TryStep(state,rig,target,dt,options,out shown,out next,out error)`. `SkateRiderMotionOptions(disablePelvisSpring,pinLeftHand,pinRightHand)` controls those three parts independently; default options preserve the original behavior. Disable the pelvis spring when another animation supplies its vertical motion, while keeping chest/shoulder and free-hand lag. A pinned hand is copied exactly from the target before solving its arm; either or both hands can be pinned. Suppressed/pinned parts clear their own velocity history on every step, so releasing the control cannot revive an earlier stored impulse.

The enabled pelvis springs along target PelvisUp; the chest, both shoulders and free hands have Verlet history. Shoulder constraints reconstruct a coherent chest frame, and analytic two-bone IK re-solves knees/elbows from the moved roots/endpoints. Every returned joint is within 0.12 m of its target, checked after IK. Hip/shoulder spans, spine/neck/head and all limb lengths match the supplied rig. Both ankles and toes are copied exactly from the target: their ordinary movement follows that input, with no added secondary offset. Secondary movement is attenuated when a moved shoulder cannot reach an exact pinned hand; target fallback preserves the contact, lengths and bounds.

Finite dt is [0.0001,0.05] seconds, internally subdivided to at most 1/120 second. History is transported with the ankle-centre translation and pelvis-frame rotation, retaining lag during ordinary riding. A rig change, translation over 2 m or a pelvis forward/up change over 120 degrees resets to the target. Secondary speed is limited to 6 m/s independently of world riding speed. Constraint reduction or target fallback clears excess history so clipped motion cannot store a later impulse.

Target/state coordinates must be finite within +/-100,000 m, frames unit/perpendicular within 1e-6 and geometry consistent with the rig within 1e-6 m. Inverted valid poses are accepted. HeadForward is independently unit and need not be perpendicular to ChestUp. Invalid rig, target, state or dt returns default shown, the unchanged supplied state and a static English error, including on initialization/reset. Results are deterministic for identical inputs on the same runtime.

The genuine-reference net48/C#7.3 build has zero warnings/errors. The owner-approved `skate_rider_secondary_motion` runner executes that actual DLL and passes 492,774 checks, including all eight controls, all 14 grab targets across stance/switch combinations, fixed hands, free-arm/shoulder lag, control toggles/history, near-reach/reset, exact feet/lengths, bounds, transport, variable dt, inverted poses and atomic rejection. The original/default overloads give identical pose/history bits. Warm 20,000-call success and refusal loops for each option allocate zero measured bytes on Mac net8. This establishes the stated geometry/runtime contract, not native IL2CPP cost or visible rider quality.

The generic target has no ground/deck/hand contact flags. The adapter chooses which hands to pin and supplies targets already on the required grip or plant surface; an exact target does not establish mesh/collider contact. Preserve contact through the options before IK rather than filtering a solved hand afterward. Claude still owns its `RiderRig` integration, actual ground/grip constraints, transition tuning and native visual verification.

## Height-sampled edges API

`SkateHeightGrid(origin, forward, right, forwardSpacing, rightSpacing, forwardCount, rightCount, heights)` uses sample (0,0) origin and orthogonal unit horizontal axes. Heights are absolute world Y, row-major `row*rightCount+column`, with NaN for no hit; keep the caller-owned array stable during queries. Counts 3–65, spacing 0.01–10 m, coordinates/heights within +/-100,000 m. The implicit rider/query point is the grid centre; build samples around the rider.

`SkateEdges.TryFind(grid, travel, [minimumDrop,] out edge, out error)` returns true for valid sampling even when `edge.Found` is false. Travel is nonzero horizontal, normalized internally. Drop defaults to 0.2 m (explicit >0 through 100,000). Both axes seek a straight discontinuity over >=3 consecutive rows, requiring two finite plateau samples on each side. Adjacent slope estimates reject planar ramps; tops must agree within 0.08 m. The longest supported edge wins, then nearest centre. Flat ground, missing support and insufficiently sampled steps produce no edge.

`SkateEdge.Point` lies on the fitted line at top Y nearest the centre, clamped to observed support. `Direction` is unit horizontal signed along travel; `HighSide` points onto the high surface (`HighOnRight` is relative to Direction). `TopHeight`, `Ahead`, `Behind`, `Resolution` report top Y, observed metres from Point along/against Direction, and cell-diagonal scale. Endpoints are Point-Direction*Behind and Point+Direction*Ahead. Exact physical line/end positions between samples are unknown; this detector describes level ledges.

`TryContinue(previous, grid, travel, [minimumDrop,] out continues, out edge, out error)` tracks line/side/top and overlapping old/new observed intervals (half larger Resolution overlap tolerance). The centre must be within both endpoint bounds with support ahead. Missing samples, ending support, disjoint collinear ledges or an unobserved jump beyond the old segment conservatively return continues=false. A matching edge may still be returned when ended at the centre. Invalid calls return false, default edge and static error.

`TryCapture(edge, velocity, boardForward, recaptureSeconds, out allowed, out speed, out error)` validates a found edge, world m/s velocity, unit horizontal nose direction and cooldown 0–RailRecaptureDelay. Eligibility needs descending impact <=MaximumLandingFallSpeed, zero cooldown, absolute along-speed >=MinimumCaptureSpeed (signed speed may be negative) and absolute travel/nose alignment >=RailAlignment (25 degrees). Read velocity before contact removes descent. The adapter still must check actual contact, proximity, upright trick orientation and clearance; eligibility alone must never teleport a rider onto an edge.

`TryGrindStep(speed, dt, out nextSpeed, out grinding, out error)` accepts signed speed +/-MaximumGroundSpeed, dt (0,MaximumStep], applies RailDrag without reversal, and stays grinding at >=MinimumGrindingSpeed. `TryCooldown(remaining, released, dt, out next, out error)` starts RailRecaptureDelay on release or counts down to zero. Failure preserves speed/time and reports false with static error. All thresholds reuse SkateMotion; its existing RailAlignment constant is now public. All query/helper calls allocate no per-call memory.

Root's integrated net48 DLL passes 9,318 private assertions: 1,404 noisy ledge cases across angles/offsets, rotated grids/reverse travel, flat/steep smooth planes, stairs, finite ends, missing hits and disconnected continuation, invalid values and capture/drag/cooldown boundaries. 1,000 warm 25x25 queries and 100,000 valid/invalid calls for each helper allocate zero measured bytes on Mac net8. Grid detection/contact/raycast quality and native grind behavior remain unverified.

## Synthesised audio API

`SkateAudio.TryCreate(sound, sampleRate, [durationSeconds,] seed, out float[] samples, out string error)` creates caller-owned mono PCM floats for `SkateSound.Rolling`, `Grind`, `Push`, `Ollie`, `Landing`, `Bail`, `DeckSlide`. Existing values 0–5 remain; DeckSlide is appended as 6. Rates 8,000–192,000 Hz; explicit duration 0.02–10 seconds; count is round(rate*duration) away from zero. Seed is any uint, including zero. Defaults: Rolling/Grind/DeckSlide 1 s, Push 0.18 s, Ollie 0.14 s, Landing 0.28 s, Bail 0.65 s. Rejection returns null/static English error before allocation; insufficient-memory creation also returns false.

The three loops use dense periodic seeded oscillator bands below Nyquist, sampled without duplicating an endpoint. Opposite pairs give zero DC and equal discrete slopes across the wrap seam; play period samples.Length/sampleRate. Rolling models wheel/body rumble, Grind truck-contact metal rasp, DeckSlide wooden contact/scrape. These identify contact parts, not a detected rail material. Push is a shoe scrape; Ollie a fixed-resonance wooden pop; Landing staggered wheel/deck impacts; Bail repeated impacts and drag. One-shots start/end at zero and remove DC with an endpoint-zero weighting. Nominal peaks are Rolling 0.45, Grind 0.65, DeckSlide 0.55 and shots 0.8; mixed output still needs level checks. Identical inputs produce identical arrays on the same runtime; arrays are independent. Clip/coefficient generation allocates: create/cache clips during setup, **never** from a physics step. The source and generated waveforms are original project content under Apache-2.0; no external audio assets are used.

`SkateSfx` caches seven clips, crossfades separate Rolling/Grind/DeckSlide sources by ride mode and grind kind, and uses bounded speed-dependent pitch/volume and event counters. Cleanup stops/destroys only owned resources. Genuine-reference net48/C#7.3 shared builds pass with zero warnings/errors; the owner-approved `skate_audio_surfaces_and_events` runner consumes the actual DLL and passes 4,069 checks across 550 generated clips. It covers durations, finite samples, peaks/DC, independent deterministic arrays, seam slopes, endpoints/decay, contact roughness and rejected inputs. Independent source reviews pass. Full changed-client compilation, native playback/mixing/timing/lifecycle, heard quality and play-time performance remain unverified. [Audio contract and verification limits](../../../docs/RUST-SKATE-AUDIO.md).

## Verification boundary

Genuine Mono-reference net48/C#7.3 compilation passes with zero warnings/errors. Private Mac net8 execution of the actual net48 library passes 266,150 assertions: flat push to max, coasting distance, braking, downhill gain/uphill stall, wall block, jump/landing and stale support, steering rates, gravity, 360 headings, deterministic sampled steps and board-normal reconstruction. Valid and invalid 100,000-call loops each allocate zero measured bytes after warm-up.
The board passes 31,323 private assertions covering bounds, indices, normal length, deterministic independent arrays, wheel diameter/contact, truck attachment, manifold solids and every triangle facing outward relative to its own component. A software top/underside preview confirms projected clockwise front faces; this is no Rust render.
These checks do not prove Unity physics/materials, IL2CPP/Mono runtime allocation, controller feel, network movement acceptance or Rust client gameplay. Probes, logs and binaries stay in ignored `context/`.
