# Rust park building assets

The owner requested both park pieces in the building-plan menu and individual
placeable hotbar items. ShortcutSkate source **0.3.10** supplies the server/item
contract; Claude owns the client menu, authored preview and input integration.
Neither route has native acceptance yet. Commands are admin setup/fallback tools.

An in-game eligible administrator can request `skate.items all`, or one of
`bank`, `kicker`, `quarterpipe`, `ledge-low`, `ledge-high`, `long-ledge`, `funbox`,
`halfpipe`. Each named item contains one piece. No material costs or crafting
recipes are added. Ordinary park-building permissions remain admin-only.

Client placement chooses a piece, aims/rotates its preview and confirms once.
Cancel submits nothing. The menu uses the existing authenticated placement
backend; a held park item uses `skate.placeitem [x y z yaw]`. It must suppress
ordinary foundation confirmation. This is a frontend contract, not a verified UI.

The stock carrier is `building.planner`, skin 0, amount 1. Authored
`instanceData.dataInt` values `0x534B5001`–`0x534B5008` follow `SkateParkKind` order;
the [shared catalogue](../mods/rust/shared/SkateParkBuildItems.cs) supplies exact
English names. Reserved tagged plans do not stack or build ordinary foundations.
Other plans retain normal behavior. No Workshop skin or external asset is needed.

The server derives kind from the unchanged active owned item. `skate.place` also
requires that item's matching kind and payment when it is active. Full native
metadata, building privilege, terrain/water, occupancy, range, save identity and
assembly checks remain. Explicit coordinates use the exact layout origin within
25 metres; the client accounts for floor thickness. See [layouts](RUST-SKATE-RAMP-ASSETS.md).

Payment occurs after complete assembly registration checks. Only the same item's
verified native quantity change 1→0 counts as payment. Earlier refusals do not
spend it. A later failure returns a verified debit only after complete cleanup
and verified rollback metadata; ambiguous state never manufactures a replacement.

Version 1 groups have optional `BuildItemState`: historical/free `None`,
unconfirmed `Preparing`, verified `Paid`, or `RecoveryClaimed`. Only the eligible
admin owner of a complete paid group can recover one item after all parts are
demolished and removal metadata is verified. Partial groups return none. A claim
is saved before destruction; an intact veto can restore payment eligibility.

Inventory and plugin JSON have no atomic crash commit. Interrupted preparing or
claimed states refuse automatic minting. Exceptional delivery retries track the
same created UID only during this plugin instance; reload may need private
operator recovery. These limits are not crash-proof inventory conservation.
Older 0.3.9 refuses nonzero provenance fields; retain matching private park-data and source backups before a downgrade.

The approved `skate_park_build_items` exercises the actual compiled pure catalogue,
identity/debit and recovery predicates. Native inventory, hooks, save/reload,
hammer delivery, client menu/preview and visible placement remain unverified.
Codex runs no Rust client, Shadow PC or server; owner/Claude provide those gates.
