# ShortcutLoadouts for Rust PC / Oxide

The separate skateboard movement component is documented in [SKATE.md](SKATE.md).
The bounded admin-only client echo diagnostic is planned in
[RUST-SKATE-INBOX.md](../../../docs/RUST-SKATE-INBOX.md).

Source for [issue #266](https://github.com/Dj-Shortcut/mw2-rust-rust-rewrite/issues/266), in the same repository as the parked rewrite.
Development code with genuine-reference compilation, source review and native Oxide compile/load/config checks; **player behavior and a playable mod remain unverified**.
Stock Rust supplies the world, survival, building and TC behavior. This plugin adds personal firearm kits and bounded Bullet PvP scaling; MW2 assets/animations and skating are outside its scope.

## Install on a prepared test server

Use a genuine Rust PC dedicated server with matching Oxide; see [server preparation](../server/README.md) and the [hosting gate](../../../docs/RUST-SERVER-HOSTING.md). No server purchase is required for source compilation. The owner requires source work before rental and approves any concrete rental costs separately.
Copy only `ShortcutLoadouts.cs` into the server's `oxide/plugins/` folder. Oxide compiles it; watch the server console for errors and confirm `oxide.plugins`. Native compile/load is verified for the recorded Linux host; player behavior remains a separate gate.
On first load it creates `oxide/config/ShortcutLoadouts.json` if absent. Existing malformed/invalid config is preserved and disables both features.
Grant permissions to a chosen test account in the server console:

```text
oxide.grant user <SteamID64> shortcutloadouts.use
oxide.grant user <SteamID64> shortcutloadouts.damage
```

In chat, `/loadout carbine` requests the default AK plus 120 rifle rounds. It requires permission, a connected/alive/awake/non-wounded human, enough empty main/belt slots and a 60-second successful-grant cooldown. It never clears inventory, stacks, swaps or deliberately drops kit items. Requests do not target another player.
Failure cleans up tracked newly created items; other plugins' independent side effects cannot be globally undone. A detected item-removal veto/error disables both features and logs the created UID for the administrator. Entity-destruction vetoes may leave orphan entities and require real-server conflict checks. Reload clears cooldowns; disconnect does not clear an active cooldown.

## Configuration and removal

Default configuration:

```json
{"Version":1,"CooldownSeconds":60,"BulletDamageFactor":1,
 "Loadouts":{"carbine":[{"Shortname":"rifle.ak","Amount":1},
                         {"Shortname":"ammo.rifle","Amount":120}]}}
```

Cooldown: 1–3600 seconds. Factor: finite 0.1–4. Configure 1–16 lowercase kit names, each with 1–24 firearm/cartridge entries, bounded by the actual item stack size and a hard amount limit of 2048. Unknown fields, including JSON metadata fields, unparseable values, invalid names/items and unsupported ammunition disable both features. Items use stock creation defaults; attachments, skins and weapon tuning are not configured.
Only a permitted attacker's recorded firearm hit against a different connected human has its positive Bullet component scaled. Factor 1 passes through. NPCs, buildings/TCs, raids, self-hits and other damage components retain stock rules; other plugins may independently affect damage.
Edit config, then `oxide.reload ShortcutLoadouts`.
For temporary unloading, use `oxide.unload ShortcutLoadouts`; stored permission grants remain and take effect again on reload.
For permanent removal, while loaded, list `oxide.show perm shortcutloadouts.use` and `oxide.show perm shortcutloadouts.damage`. Revoke both permissions from every previously granted user/group with `oxide.revoke user <SteamID64> <permission>` and `oxide.revoke group <group> <permission>`; repeat both listings until no grants remain.
Then `oxide.unload ShortcutLoadouts` and remove its `.cs` from `oxide/plugins/`. Previously granted stock items remain; no world/save migration is added.

## Reproduce source verification

Use .NET SDK 8 and an external genuine `RustDedicated_Data/Managed` folder with its matching Oxide DLL overlay. Keep all game/framework DLLs, generated binaries and probes out of GitHub.

```sh
dotnet build mods/rust/plugins/ShortcutLoadouts.csproj -p:RustManagedPath=/absolute/server/RustDedicated_Data/Managed
```

The project uses those genuine Mono/framework references, C# 7.3 and `net48`; it excludes duplicate Newtonsoft types already in `Oxide.References`. Outputs are ignored under `context/rust-plugin-build/`. No fabricated Rust API or permanent test is included.
Verified on macOS arm64 with SDK 8.0.425, Steam app 258550/depot 258552 public manifest 8588463972864888654 and official Oxide 2.0.7801: genuine-reference build (0 warnings/errors), 93 actual compiled-helper/schema regressions and 43 actual compiled-parser assertions, plus independent source review.
Subsequent [native Linux server evidence](../../../docs/RUST-SERVER-PLUGIN.md#native-server-evidence) confirms compile/load, actual default config, registered permissions, unknown-field byte preservation, restoration, reload/unload/load and plugin load after save/restart on the recorded Steam public build/Oxide 2.0.7815. No clients joined; these checks prove neither runtime hook ordering/inventory cleanup nor actual PvP. Complete the [real-server acceptance](../../../docs/RUST-SERVER-PLUGIN.md) before any playable/ready claim.
