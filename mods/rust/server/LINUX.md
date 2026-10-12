# Linux Rust/Oxide installation preparation

Source for [issue #266](https://github.com/Dj-Shortcut/mw2-rust-rust-rewrite/issues/266). A native Linux installation is verified; server/client acceptance remains incomplete. The standalone rewrite remains parked.

## Preview without installing

Python3.9+ standard library; the plan uses a platform-independent Linux path and makes no network/file changes:

```sh
python3 mods/rust/server/Install-LinuxServer.py --root /srv/rust-new --plan
```

Use an absolute Linux destination without spaces, traversal or shell punctuation. The plan prints intentions only; it does not establish that the target path, resources or packages exist on the chosen host.
The owner selected and created a DigitalOcean Linux test host on 8 October 2026 after approving concrete costs. Future rentals still require separate owner choice and cost approval; see the [hosting gate](../../../docs/RUST-SERVER-HOSTING.md).

## Install later on a permitted Linux host

Actual installation requires **Linux x86_64/glibc**, a dedicated **nonroot user**, at least **12 GiB available RAM**, **20 GiB free disk**, and an existing writable/trusted parent. Keep that parent exclusive during installation. Existing destinations, Git checkouts and symlink components are refused. This is not protection against arbitrary concurrent changes by the same account.
RAM is bounded by host `MemAvailable` and active v1/v2 cgroup limits minus usage across the full ancestor view, including finite v1 memory/swap bounds. Unreadable/ambiguous controls or clipped/namespaced views with unknown ancestors are refused before writes/downloads. This snapshot reserves no future RAM.
SteamCMD needs its 32-bit runtime dependencies. Exact distro/package preparation is a chosen-host step; this source never runs sudo, a package manager or services. [Ubuntu24.04 SteamCMD](https://packages.ubuntu.com/noble/steamcmd) and [32-bit GCC runtime](https://packages.ubuntu.com/noble/lib32gcc-s1) describe relevant packages, not complete Rust compatibility.

```sh
python3 mods/rust/server/Install-LinuxServer.py --root /srv/rust-new --oxide-release 2.0.7815
```

SteamCMD install/validation allows 14400 seconds (4 hours) by default; `--steam-timeout-seconds` accepts 60–86400 for the chosen host. Exceeding it stops the process group and retains partial files; existing-root refusal still applies, with no resume mode.
Use a **new** directory whose parent is writable by that user. The installer does not update, delete or reset an old installation/world. Failure retains partial files; inspect `install-manifest.json` and `.mod-bootstrap/steamcmd-install.log` before choosing another empty root. Do not publish game files, logs with account details, saves or credentials.
Fresh-root/staging creation is guarded. Catchable interruptions record failure in the owned root even without staging; racing/existing destinations remain untouched. Root-local unique atomic temporaries permit retry. Recording is best effort if storage becomes unwritable; SIGKILL/power loss cannot be recovered automatically.
It downloads official SteamCMD, installs app258550/public anonymously with failed-command/password-prompt guards, checks installed manifest/executable, then overlays official stable Linux Oxide **at server root**. SteamCMD may also write its ordinary per-user Steam cache/logs outside that root.
The selected Oxide asset must have the expected release/name/URL/size and publisher SHA256. Download limits, bounded TAR decompression/metadata, unsafe-path/link/special/duplicate/ancestor-collision refusal, ZIP layout checks and protected data namespaces bound extraction. SteamCMD's recorded measured hash has no independently published comparison. Every launcher exit, caught interruption and timeout cleans the tracked installer group before overlay; an unreaped leader protects group identity during signaling. Cleanup is bounded and refuses continuation if group disappearance cannot be established, including un-reaped orphan zombies. Partial evidence remains.
`installed-unverified` records files, Steam build and Oxide identity only. The pinned Oxide tag does not pin Steam's evolving public build or prove compatibility. No server, world, plugin, port, RCON credential or service is started/configured by this installer.

## Control and actual acceptance

Linux server console input is noninteractive: screen/tmux show output. Real `oxide.version`, permissions, save and `quit` need authenticated RCON/private access or separately verified in-game administrator control. That access/credential/startup design is a separate selected-host task; no public RCON is required.
For Shadow's EAC-disabled client, later startup needs explicit current `-insecure` plus a reviewed **UDP game-port** route for F1 direct join. Query exposure is only for separately required discovery. The Windows loopback launcher is not a Linux/remote-client launcher.
After a real install, record actual server startup, matching framework, listeners, intended-client join, save/restart and the [plugin acceptance](../../../docs/RUST-SERVER-PLUGIN.md). No playable/ready claim before those results.

## Update the skate plugin

The [owner's 12 October session](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/issues/337#issuecomment-6125730635)
identified an Oxide `Newline in constant` compile error in the 0.3.6/0.3.7
separator literals and reported disabled automatic plugin reload. The pinned
0.3.8 source uses numeric char values for those exclusions and separates trusted
fly authority from the horizontal speed grant ([PR #387](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/pull/387)).
The explicit deferred-reload command below installs verified source with a backup,
then restarts the existing service. It does not claim native loading without a verdict or roll back while waiting
for a watcher that is disabled. Loading,
placement and movement remain owner verification; Codex changed no live server.

### Private-server alternative

For the owner's isolated private server, a separate owner-applied alternative is to
disable these native server checks in the active identity's `cfg/server.cfg` (read at
startup, as described in [Facepunch's server.cfg documentation](https://wiki.facepunch.com/rust/creating-a-server#server.cfg)):

```text
antihack.speedhack_protection 0
antihack.flyhack_protection 0
antihack.noclip_protection 0
server.stability false
```

Back up that configuration first and retain its prior values for restoration. These
settings apply server-wide, including other players; they are not the plugin's scoped
movement policy. Noclip is included because client park colliders can differ from native
server blocks. `server.stability false` disables collapse for all buildings; the plugin's
`GroundedBuilding` option only manages its verified registered members. Choose this
alternative deliberately; the installer never writes these settings or executes them.
The owner applies/reloads or restarts the server as appropriate, then checks current
convar values in the authenticated server console and tests movement/building. No such
application, restart, native physics or gameplay check was performed by Codex.

### Plugin load diagnostic

After the owner runs `oxide.reload ShortcutSkate` in the authenticated server/admin game
console, run this read-only line in the Linux shell to collect plugin-related compiler/load
lines from the current Oxide logs:

```sh
sudo -u rust grep -h -Ei 'ShortcutSkate|Skating ready:|Skating is disabled:' /srv/rust/server/oxide/logs/oxide_*.txt | tail -n 40
```

Keep the output private and share the relevant compiler error in the owner's private
chat after removing identifiers, addresses or unrelated operational details. No matches
does not prove successful loading; preserve the actual console verdict and check
`oxide.plugins`/`skate.status`. This command does not reload, install or change configuration.

### Existing pinned installer

`add-skate.sh` selects ShortcutSkate **0.3.11**, commit
`fa0ffa78448cbfdd8a572d8cf029e41d5514d899`, source SHA-256
`8e7525b090468dd10a7cdab27e53ed5bfac693e6fb75d0d2bbeb7669bf68078c`.
It targets the existing `/srv/rust/server/oxide/plugins` installation and its
nonroot `rust` user/group. It needs Bash, Python 3, curl, sha256sum, GNU file tools,
runuser and setsid. The plugins folder must be writable by `rust`; any prior
source must be a regular nonsymlink file readable by that account.

The selected source is the building-item backend in [PR #398](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/pull/398); merge that source PR before this delivery PR. The source now uses native square-foundation input; its actual hooks/inventory and both requested building frontends remain unverified. Current0.3.10 relies on the failed client-command route and should not be installed.
Keep a private backup of `oxide/data/ShortcutSkate.Parks.json` with its matching world/source before upgrading. Older 0.3.9 rejects nonzero build-item provenance; source-only downgrade is not park-data rollback. Never remove paid provenance to force a downgrade.

Preview the pinned public bytes without root or a server:

```sh
bash mods/rust/server/add-skate.sh --check
```

For the existing server without automatic reload, run the following **as root**
in its Linux shell. This installs source and **restarts `rust-server`**, briefly
disconnecting players. Use it only for that existing service, with no concurrent
plugin update. Keep the prior source backup until native loading and riding pass.
The immutable installer and its hash are verified before execution:

```sh
(set -eu; skate_update_dir=$(mktemp -d /tmp/ShortcutSkate-update.XXXXXX); trap 'rm -rf -- "$skate_update_dir"' EXIT; curl -fsSL --connect-timeout 15 --max-time 60 https://raw.githubusercontent.com/Dj-Shortcut/mw2-rust-skate-rewrite/199bc61e82e5629ae769c8eacb1db8ce65a305e3/mods/rust/server/add-skate.sh -o "$skate_update_dir/add-skate.sh"; printf '%s  %s\n' 221c2df74093f4ff8dc91d1cb4306eea0c834ed98ed04f0dcf20f6965f5f80a3 "$skate_update_dir/add-skate.sh" | sha256sum -c -; bash "$skate_update_dir/add-skate.sh" --defer-reload; systemctl restart rust-server)
```

`--defer-reload` deliberately returns **source-installed/native-unverified**
without reading Oxide logs or waiting for automatic reload. It preserves the prior
source as `ShortcutSkate.cs.bak` and atomically installs the checked replacement.
A successful service command only confirms that systemd accepted/completed that
operation, not plugin loading. If the restart command fails, the new source and
backup remain for inspection; this command does not automatically restore them.

After startup, check the private [load diagnostic](#plugin-load-diagnostic) for a
fresh `Loaded plugin ShortcutSkate v0.3.11` and readiness/patch errors. Confirm
`oxide.plugins` and `skate.status` in the authenticated game/server console, then
placement and riding. Do not infer native success from `source installed` or
`systemctl restart`. If corrected source fails to load, stop this service, verify
that the active source still matches the 0.3.11 hash above and the backup is the
intended prior source, restore that captured backup as `rust`, then restart and
check the prior version's actual load verdict. Changed files require inspection
before overwriting them. Keep raw logs, credentials and account details private.

Back up `oxide/config/ShortcutSkate.json` before an owner-selected config change.
Saved `MaximumHorizontalSpeed` values remain: an old `14` cap still rejects a
`16.5` m/s rider. Set the existing field to `25` to adopt the stock cap, retaining
`Version: 1` and every other field. The installer does not migrate configuration.
Diagnostic airtime/rearm settings do not withdraw the fresh horizontal grant.
Server admins already have the trusted fly privilege; `AllowEveryone` and ordinary
use grants do not give other players `shortcutskate.fly`. See the
[permission contract](../plugins/SKATE.md).

With automatic reload or a separate administrator console, plain invocation
instead waits up to **90 seconds** for a new matching 0.3.11 load line, and performs
guarded restoration on compiler failure, timeout, observation error or caught
cancellation. Run `oxide.reload ShortcutSkate` in that console during the wait if
needed. These are game-console commands; the script sends no RCON command.
Only complete lines appended after the post-replacement observation cut count.
Stale, partial and wrong-version lines are ignored. Newly discovered log inodes
are baselined at first-observed EOF; copied content cannot confirm a load, and an
early genuine verdict can conservatively be missed. Log truncation, unreadable
or irregular logs, oversized lines/tails and compiler failures refuse success.
The log directory is required for this monitored mode, not deferred mode.

In both modes, root downloads/checks source in private temporary storage; the
tracked worker performs all plugin writes as `rust`. A same-directory rename
publishes mode0644 source after saving the existing source bytes. A prior
`rust:rust`-owned source retains its owner, group, mode and modification time;
nonroot copying does not guarantee a different prior owner/group is preserved. An old `.bak` is overwritten at that stage;
retain any separately needed backup before updating. Download, checksum,
privilege-drop or backup failures leave active source untouched. Root-only source
access is refused. Until the monitored mode confirms loading or deferred mode
explicitly settles the verified source, failures/cancellation restore the prior
source, or remove a fresh unconfirmed install. Restoration requires captured
active/backup identities and hashes; changed files are refused. INT/TERM stops
and waits for the tracked worker; catchable exits clean staging. SIGKILL/power
loss can leave staging. These are guards, not a concurrent-update lock.

The existing owner-approved `skate_server_plugin_install` scenario exercises the
actual script with immutable public downloads and real GNU/Linux file operations.
Synthetic log records cover the monitored verdict, failure/timeout, copied/stale
content, changed-file guards and cancellation including a real blocked file lease.
Deferred fixtures cover source/backup/cleanup without a log directory and execute
the exact documented one-liner using a private service-command stand-in. They
check its ordering and explicit failure, not real systemd or Rust startup.
No Rust/Oxide process, live server, Rust client or Shadow PC is started by these
checks. The selected source compiles as net48/C#7.3 against genuine cached server
references with zero errors/warnings and passes the approved movement/gesture
scenarios. Claude reported the owner’s 0.3.8 native load/readiness and visible long-ledge
after a local manifest patch; 0.3.11 full-halfpipe, permissions, restart and riding
remain owner acceptance.

## Native installation evidence

On 8 October 2026, the unchanged reviewed installer completed on Ubuntu 24.04.5 LTS x86_64/Python 3.12.3 as a dedicated nonroot `rust` user in a fresh directory outside Git. Preflight measured approximately 15.1 GiB available RAM and 307 GiB free disk. Official Ubuntu packages supplied the 32-bit GCC/C++ runtime.

SteamCMD reported app258550/public fully installed, build 25797215. Installer exit 0, final `installed-unverified` manifest and no remaining installer processes were read back. Official stable Oxide 2.0.7815 Linux archive matched its publisher SHA256 `05bb6955ce40071f83bf82142dd7bb9cfeb033d569a3eb33fb730d6f2f31c713`; framework files were overlaid successfully. This verifies this successful native installation, not all Linux interruption/cleanup failure paths.

The CLI explicitly selected `--oxide-release 2.0.7815`; the source default remains 2.0.7801. Steam public builds evolve independently: choose a matching official framework when repeating installation. Private game files, manifests, credentials and operational probes remain outside the published repository. Installation itself proves no runtime; subsequent startup/control evidence is recorded separately below.

## Native startup and control evidence

On 8 October 2026, the selected nonroot Linux server reached
`Server startup complete`: Rust protocol 2634.289.1, changeset 167231,
Unity 6000.3.15x1-13 and Steam build 25797215. Authenticated loopback WebRCON
returned Oxide.Rust 2.0.7815. An intentionally wrong credential failed its
handshake; the server logged an incorrect-password attempt, and the valid
credential subsequently worked.

Measured listeners: game 127.0.0.1:28015/UDP,
RCON 127.0.0.1:28016/TCP and query 0.0.0.0:28017/UDP.
The active host firewall denied inbound traffic except SSH, including IPv6.
The query listener therefore was not claimed to bind loopback.
`app.port` returned -1 and `app.info` confirmed Rust+ disabled.
No Shadow/direct-client route was opened or verified in that 8 October check.

Private startup used explicit `-insecure`, identity `mod-demo`,
Procedural Map/seed 12345/size 1500 and four-player limit. Password only in
`server.cfg` did not enable RCON on the initial attempt. The successful
startup passed the credential to Rust at process launch through a private
wrapper that read a restricted credential file; the secret was never entered
in a shell command or committed. Startup arguments may be visible to the
same host account/root: protect that account and log/config directories.
The wrapper and selected-host service are private operational probes,
not a published/reusable Linux launcher.

Authenticated `server.save` reported Saving complete for 3394 entities and
produced a real save file. `quit` saved again; process and game/query/RCON
listeners disappeared. Systemd recorded SIGKILL, not normal exit 0; its cause
has not been established. A separate start with the same identity/build
loaded 3394 entities and the existing navmesh, reached startup complete and
answered authenticated RCON with the plugin loaded again. This establishes
the observed save/reload sequence, not a clean-exit guarantee, player-state
persistence or general crash recovery.

Unchanged ShortcutLoadouts 0.1.0 compiled/loaded on this host. Its generated
default config, permission registration, rejected unknown-field config byte
preservation, restored original bytes, reload and temporary unload/load
were checked. See [plugin runtime scope](../../../docs/RUST-SERVER-PLUGIN.md).
At the 8 October checkpoint, client joins, player inventory/cooldown/PvP and
full MW2/skate acceptance remained open. The later Shadow join and stopped,
mistyped loadout attempt are recorded below. These server checks do not establish a
playtest-ready plugin.

## Bounded Shadow route on 9 October 2026

The owner authorized a first existing-host loadout player check. The unchanged
plugin and configuration were retained while the same world was saved and
restarted as the existing nonroot account with explicit `-insecure`. Actual
listeners during the window showed game UDP 28015 on the assigned public IPv4, RCON TCP 28016
on 127.0.0.1 and query UDP 28017 on that public IPv4. Rust+ remains disabled;
no additional public TCP or IPv6 listener appeared. The query port is still
blocked by active default-deny UFW. The launch/status security discrepancy
remains unresolved; see the [hosting qualification](../../../docs/RUST-SERVER-HOSTING.md#shadow-player-check--9-october-2026).
The owner later reported spawn and authenticated status confirmed that active
account; the meaning of the security-status string is still unresolved.

Only UDP 28015 to the assigned server IPv4 was allowed from the currently
observed Shadow source `/32`. Source stability is unproved, so access is limited
to this test window. The persistent absolute-time closure timer was confirmed
active with its future trigger at 03:24:21 UTC on 9 October. Its private
handler removes the owned rule, attempts a save, stops only the tracked test
instances and verifies rule/listener removal; overdue execution is caught up
after boot. Actual manual post-test closure was verified at 03:15:11 UTC:
the owned rule was removed, the save succeeded and fresh game/RCON/query
listeners were absent with default inbound denial still active. The owned timer
was disabled after this readback.

Authenticated console confirmed both plugin grants on the owner's account
only, no group grants, and retention across this preparation restart. The owner
reported spawn and authenticated status confirmed the sole active owner account.
A later private recording also confirms in-world spawn and corrects the reported
chat failure: the entered command is `/loudaout carbinew` and the reply is
`Unknown command: loudaout`. The exact requested `/loadout carbine` was not run;
no plugin registration failure is established. Severe Shadow lag was reported
and live screen control returned `timeoutReached`; the later recording was
inspected locally. Delivery, cooldown/inventory refusal, permission denial and
player/item persistence remain unverified. Follow the ordered
[player check record](../../../docs/RUST-SERVER-PLUGIN.md#first-shadow-player-check).
A join without the loadout checks is not a playtest; two-client PvP and full
MW2/skate acceptance remain open. No plugin behavior or paid resources changed.
Private operational scripts, addresses, identifiers and raw logs are not shipped.

## Return-session operation on 9 October 2026

The owner's later playtest uses the unchanged world, public-game `-insecure`
launcher and ShortcutLoadouts source/config. The first supplied source address
was corrected to the sole actually observed inbound game UDP source, removing
the wrong owned rule before adding the correct `/32`. A transient runtime-limit
change failed and cleanup stopped that instance. The first closure acknowledged
the save and verified rule/listener removal; the later timer fired after stop
and its save returned exit 1, with closure still confirmed. These records do not
prove persistence of the subsequently issued player items. The same world was
restarted as the nonroot account with a
fresh 4,500-second runtime limit, without altering the launcher or plugin.

Actual startup/plugin checks and an independent current readback confirmed only
public game UDP 28015, public query UDP 28017 blocked externally, loopback RCON
and its existing internal loopback TCP listener. No unintended Rust listener
appeared. During the window, only UDP 28015 from the observed source `/32` was allowed;
Rust+ was disabled via the observed `app.port=-1`. A fresh persistent absolute-time timer was
verified for 15:34:57 UTC on 9 October, before the runtime backstop. Its handler
removes the owned rule, attempts a save, always stops only the pinned instance,
and verifies closure; failed closure retries. Independent readback at 15:35:34 UTC
confirmed the actual completion: save acknowledged with exit 0, owned rule
removed, pinned instance inactive, no Rust listeners and an active default-deny
firewall. The previous return timer is disabled and inactive. The save alone
does not verify player/item persistence after a restart/rejoin.

Before closure, authenticated status at 14:45:44 UTC confirmed the sole active
owner and matching source.
The owner reports one Assault Rifle and 120 Rifle Ammo after `/loadout carbine`.
This is owner-reported delivery evidence; cooldown, full-inventory safety,
permission denial/restoration and save/restart/rejoin of player/items remain
open in the [ordered record](../../../docs/RUST-SERVER-PLUGIN.md#return-session--9-october-2026).
Neither a client-crash cause nor Shadow lag resolution is established. Root
leaves native input to the owner. No behavior or paid-resource change was made;
private operational sources/reports remain ignored.

## Source evidence

On macOS arm64/Python3.14.6: source compiles; actual CLI plan and Mac install refusal pass. 82 regression and 93 cgroup/timeout/manifest assertions pass, plus 59 initialization/signal/manifest/group checks using owned IO/processes. Background helpers stop after success/nonzero/signal/timeout exits and unrelated groups survive. Darwin zombie-only signaling denial is a real rejected path; one observation-failure check uses an explicit syscall seam. These checks do not prove native Linux cleanup. These are narrower than native Linux/SteamCMD execution.
Actual current official SteamCMD archive and publisher-hash-verified Oxide2.0.7801 archive pass production inspection/unpacking in ignored local folders. In those earlier Mac archive probes, no SteamCMD, Rust or plugin process was run. Dependencies, binaries, own fixtures and probes stay ignored; no permanent tests added.
See [design and remaining gates](../../../docs/RUST-SERVER-LINUX.md).

Sources: [Facepunch server setup and Linux control](https://wiki.facepunch.com/rust/Creating-a-server), [Oxide installation](https://docs.oxidemod.com/guides/owners/install-oxide), [official pinned Oxide release](https://github.com/OxideMod/Oxide.Rust/releases/tag/2.0.7801), [kernel cgroup v2](https://docs.kernel.org/admin-guide/cgroup-v2.html), [kernel memory v1](https://docs.kernel.org/admin-guide/cgroup-v1/memory.html).
