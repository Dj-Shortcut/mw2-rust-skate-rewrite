# Rust park building assets

The owner chose an own build picker and hotbar items; Claude owns the client.
ShortcutSkate source **0.3.17** has twelve kinds: the eight original types, mini/large quarterpipes, the [half-wall kicker](RUST-SKATE-HALF-WALL-KICKER.md) and the [MEGA kit](RUST-SKATE-MEGA-HALFPIPE.md). The attached kicker targets an existing half-wall; the other kinds use native square foundations.
The stock six belt slots cannot hold the complete catalogue: use an owned item, move/equip it
natively and wait for actual active-item/planner state. Report missing items or
full inventory. Native acceptance is open; commands are admin setup/fallback.
An eligible administrator can request `skate.items all` or `bank`, `kicker`,
`quarterpipe`, `ledge-low`, `ledge-high`, `long-ledge`, `funbox`, `halfpipe`,
`mini-quarterpipe`, `large-quarterpipe`, `half-wall-kicker`, `mega-halfpipe` ([profile contract](../mods/rust/shared/README.md#one-cell-mini-and-three-cell-l-quarterpipes)).
Hold the matching item and select the stock square foundation in the build menu. Aim at the existing half-wall for the attached kicker; use native aim, rotation and confirmation for the selected piece. No client console-command submission is required.
Native client ghost validity and woodless submission still need verification.

The stock carrier is `building.planner`, skin0, amount1. Authored
`instanceData.dataInt` markers `0x534B5001`–`0x534B500C` follow `SkateParkKind`;
the [catalogue](../mods/rust/shared/SkateParkBuildItems.cs) supplies exact names.
Tagged plans do not stack. Other plans retain ordinary placement and payment.
The server captures item/planner/world before waiving native affordability.
CanBuild consumes that receipt once; changed identity refuses placement. Only
its native material bill is suppressed. The spawned foundation is unsaved and
removed next tick, with exact captured-identity/destruction checks, before the
park transaction. A kill veto or uncertain cleanup refuses the park/item debit
and blocks further park creation. Each distinct object/entity/network-ID observation
retains its own receipt; exact repeats preserve the first. Additional or unknown
carriers stay untouched and block placement until every removal is proved.
Tracking is instance-local, so interrupted callbacks/unload may need private cleanup.
The temporary carrier can be visible. Only one marked transaction is admitted;
no crafting recipe, material price or global building override is added.
The final native foundation origin supplies X/Z/yaw and its Y is the support
plane. Actual first-floor bounds determine the park baseline; no guessed top
height is added. Client ghost agreement remains unverified. Full prefab/material,
privilege, terrain/water, occupancy, range25m, save identity and assembly guards
remain. Typed `skate.placeitem [x y z yaw]` / `skate.place <kind> [pose]` are admin
fallbacks; explicit coordinates remain exact [layout origins](RUST-SKATE-RAMP-ASSETS.md).

Only the original item's verified quantity1→0 pays after complete registration.
Earlier refusals retain it. Post-debit rollback returns it only after complete
cleanup and verified metadata; ambiguous state never mints a replacement.
Version1 `BuildItemState` records `None`, `Preparing`, `Paid`, `RecoveryClaimed`.
Only the eligible admin owner of a complete paid group recovers one item after
all parts and metadata are removed. Partial/free groups return none; the claim
is saved before destruction, and an intact veto can restore payment eligibility.

Inventory/JSON have no atomic crash commit; interrupted preparing/claimed states refuse automatic minting.
Item-owned marker data is non-pooled. Retries rebind same-owner admins, retain
UID/kind/world and block new issue on failure. A pending delivery must be detached
before another transfer attempt; an unverified container/world association stays
untouched and tracked. Non-null world references refuse even if their Unity
objects compare as destroyed. Completed owner-inventory/live-drop receipts remain valid.
Reload needs private recovery. Existing updater pins still select 0.3.11.
Older0.3.9 refuses nonzero provenance: retain matching park-data/source backups.

Approved `skate_park_build_items` checks the actual compiled pure catalogue,
identity/debit/recovery predicates. Native construction/payment/cleanup, inventory,
reload/hammer delivery, ghost and placement remain unverified. Codex runs no Rust
client, Shadow PC or server; owner/Claude provide native acceptance.
