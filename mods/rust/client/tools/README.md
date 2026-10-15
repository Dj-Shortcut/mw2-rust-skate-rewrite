# Tools for the skate client

PowerShell scripts for the Windows PC that runs the Rust client. They generate what the mod loader
needs for the installed Rust build, compile the plugin, run a bounded session with the loader and
take it out again, show the logs, and install the owner's launcher.

They are written to be run by an agent that reaches the PC only through typed keys. Every script
is therefore fetched from this repository and run in the PowerShell session (`zz name`), names are
plain words, and settings are global variables that a script sets for the next one. Nothing here
is installed permanently except the launcher described below.

## What must be on the PC

All of it lives under the user's `Downloads` folder and none of it is in this repository.

| Path under `Downloads` | What it is |
| --- | --- |
| `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788+5b766a3.zip` | The official mod loader build ([identity](../../../../docs/RUST-CLIENT-LOADER.md)). |
| `codex-bep788-probe\` | What the build of the corrected interop generator ([`../generator`](../generator/README.md)) left behind: a .NET SDK, the generator with its own .NET host, and PowerShell 7 (`pwsh-diag\pwsh.exe`, used by `survey` only). |
| `claude-loader-probe\` | The staging folder of these tools. `setup` copies the SDK and the generator into it; the rest is created as needed. |

The staging folder holds `sdk\` and `generator\` (the copies made by `setup`), `bep788\` (the
unpacked loader), `unity-libs-<version>\` (Unity's base libraries), `gen-<build>\` (one interop set
per Rust build), `rt\` (the changed runtime library), `plugin\` (the last build), `play\` (the released
plugin), `launcher\` (the owner's launcher), `run-<tag>-<time>\` (one folder per session with its
logs and the loader files taken back out of the Rust folder) and `server.txt` (the address of the
private test server as `ip:port`; it is read by `ping` and `session` and is never committed).

## Starting

In a PowerShell window on the PC, once per window:

```powershell
iex (irm https://raw.githubusercontent.com/Dj-Shortcut/mw2-rust-skate-rewrite/main/mods/rust/client/tools/remote.ps1)
```

After that `zz name` runs `name.ps1` from this folder and `zg file` returns a file from it. Both
read the branch named in `$pref`; set it before or after loading `remote.ps1` to work from another
branch (`sv pref name`, or `zz ref` for a name with dashes). Script names are letters only and
settings take plain values, because letters, digits and spaces are all that can be typed into the
PC reliably from a distance.

## Scripts

| `zz` | What it does |
| --- | --- |
| `setup` | Once: copies the SDK and the interop generator into the staging folder. |
| `status` | Whether the window has administrator rights, Steam, the installed Rust build, whether an interop set exists for it, leftovers in the Rust folder, what is built and installed. Starts Steam when it is not running, except from an administrator's window. |
| `interop` | Generates the interop set for the installed Rust build in the background. Needed once after every Rust update. |
| `progress` | Progress of that generation; done when the log ends with `GEN DONE`. |
| `runtime` | Builds the changed `Il2CppInterop.Runtime.dll` from the pinned upstream source and [`rt/`](rt/README.md). Needed once. |
| `build` | Compiles the plugin from [`../sources.txt`](../sources.txt) at the head of the branch, or at `$pcommit`, and copies the park models of [`../models.txt`](../models.txt) next to it. |
| `session` | One bounded run of the client with the loader; see below. Normally started by one of the next five. |
| `play` | Build, then a session to play in, sound on, started through the owner's Steam shortcut so that the controller layout applies off the board too; the owner joins his server from the menu (QUICK JOIN), or gives the shortcut `+connect <address>` as its launch options in Steam once. `$pdirect=1` starts the client directly and joins the server in `server.txt` at start instead: keyboard and mouse only, since a client that Steam did not start gets no Steam Input. |
| `pad` | The same as `play` without its options. |
| `ride`, `pose`, `park`, `send`, `place`, `tricks`, `flick` | Build, then a muted session that runs one scripted check in the world and ends by itself. `flick` plays a controller into the plugin without one; `park` draws the authored park models for a look and a ride, on this client only; `place` sends `skate.place bank` to the server and reports whether a piece appeared and was drawn (server plugin 0.3.6 or later). |
| `tour` | Build, then a muted session on the owner's server that rides to each park piece the server placed near the spawn and across it, and logs per piece what came of it (`TOUR` lines: rise, air, grinds, bails, put-backs); ends by itself. |
| `feet` | Build, then a muted session on the owner's server that rides back and forth on the runway eight times (pushes, an ollie, a revert on landing, a stop) with the camera following from the rider's front, for screenshots of where the feet are; ends by itself. |
| `status` | Build, then a session on the owner's server that rides until the server puts the rider back, steps off and types `skate.status`, the server's speed and fly checks and `oxide.plugins` into the F1 console, one every eight seconds; the console stays open so each answer can be read from the screen. |
| `sticks` | Watches the controller from outside the game for `$pwatch` seconds and says what it saw in each slot. The same script is the reader that a session starts beside the client. |
| `runs` | The last 20 session folders (the owner's launcher sessions too) with their right-stick movements: how many, how many on the board, how many did nothing there. `$prun = <folder>` then `zz runs` makes that folder the one `log`, `logall`, `file` and `flicks` read. |
| `flicks` | The last session's right-stick movements, one row each: where the rider was, what came of it, and the shape of the movement. `$pshow` prints one of them point by point. |
| `log`, `logall` | The plugin's log of the last session, without or with its once-a-second status lines. `$pinc` filters. |
| `file` | Another file of the last session, by default the game's `Player.log`. |
| `more` | The next page of whatever was shown last. |
| `netfind` | The network library's types from the send check's listing (`console-net.txt`) that the console packet path is looked for in: the packet enum, every type with a method taking it or a string and a bool, every struct, every type with a static member, with their members. |
| `last` | Makes the newest session folder the one that `log`, `logall` and `file` show, and sums up its ride: seconds on the board, top speed, the lowest speed cap, how often the server put the rider back or held it back, tricks and bails, every trick name the scorer gave with its count, the presses in the air that were refused for want of air by what was asked, and why the rider stepped off. For a session of the owner's launcher, which runs in a window of its own. |
| `clean` | Moves a loader that these tools left in the Rust folder out of it. A file still held by a closing client or its crash handler is tried again for a minute; what is left is named. |
| `survey` | Lists what the interop set of the installed build offers for given type names. |
| `launcher` | Installs or updates the owner's launcher from the commit in [`release.txt`](release.txt). |
| `launchertest` | Starts the installed launcher, kills it while Rust runs, closes Rust and reports whether the Rust folder was cleaned all the same. |
| `pack` | Zips the installed launcher for a friend: the loader, the interop set of the installed build, the changed runtime, the released plugin with its models, the launcher's scripts and (unless `$pserver` is 0) `server.txt`, with `install.ps1` of the branch as its installer. Refuses without a launcher prepared for the installed build. [What the friend does](../FRIEND.md). Not yet run on the owner's PC. |
| `install` | Not a `zz` script: the friend's installer, carried inside the pack and started there by `Install Rust Skate.cmd`. Checks Steam, Rust's default library, the matching Rust build, free space and the `RustClient` shortcut; copies the pack to `Downloads\claude-loader-probe` and writes the two desktop files. |
| `ping` | Whether the private test server answers on its game port. |
| `user` | Opens a PowerShell window without administrator rights, with `zz` loaded, for when the window at hand is an administrator's. |
| `ref` | Points `zz` and `zg` at another branch: `$pto` holds its name without the dashes and slashes, which cannot be typed from a distance (`sv pto claudeloaderprobe`). |

## A session

`session` copies the loader, the interop set of the installed build, the changed runtime and the
plugin into the Rust folder, starts the client, waits, closes the client and moves every file it
added out again into the session's own folder. The last part runs in a `finally` block: the loader
must not stay in the Rust folder, because any later start of Rust would load it too, also on a
server with anti-cheat. A file of the loader can still be held for a while after the client has
exited (its `ErrorLog.log` was seen held once, which left the whole loader in place when the first
failed move ended the block): a move that fails is tried again for a minute, with a lingering client
or crash handler ended in between, and whatever is still there at the end is named, with `clean`
as the next step. It refuses to start when the loader is already there, when Rust is running
or when there is no interop set for the installed build; a set generated for another build crashes
the client.

The client is started directly (`RustClient.exe`, which does not start the anti-cheat) or through
the owner's non-Steam shortcut `RustClient` for the same executable. With `$pconnect` a directly
started client joins the server in `server.txt`; nothing has to be typed in the game. Only a client
that Steam started gets Steam Input: started directly, the controller works on the board (the
plugin reads it through its own reader) but not on foot, which is what the owner saw in a `play`
session before `play` went through the shortcut.

While its loader is in the Rust folder, `session` keeps a note in the staging folder that names two
of the loader's files by their hash. `clean` moves a loader out only when that note is there and
those files, where present and readable, still read the same: another mod loader in the Rust folder
is left alone. The loader's archive is unpacked beside the stage and renamed when complete.

A client that comes up behind another window never gets the keyboard, so `session` brings the
game's window to the front once it exists. A scripted check keeps it there (`$pkeep`) and, when it
ends, puts the window that was in front when it began back there: the one it was typed into.

Before the client, `session` starts [`sticks.ps1`](sticks.ps1) as a hidden process of its own: the
reader that shares the controller with the plugin
([why](../README.md#the-controller)). Steam must not have started it, or it would see no more of
the controller than the game does. It ends by itself once the client has come and gone, or when no
client came within ten minutes, and writes one line to `sticks.log` in the staging folder. Only one
reader shares the controller at a time.

The plugin reads its steps from `plugins\steps.txt`, which `session` writes from `$psteps`; they are
listed in the [client's README](../README.md#steps). A scripted check ends the session by writing
`plugins\skate.done`; otherwise the session ends when Rust is closed or after `$pdone` seconds.

## After a Rust update

1. `zz status` shows the new build and `interop for this build=False`.
2. `zz interop`, then `zz progress` until `GEN DONE` (about ten minutes).
3. `zz ride` in a world. If the plugin no longer compiles or the check fails, the game changed
   something the client relies on; `survey` shows what the new build offers.
4. `zz launcher`, so that the owner's plugin is rebuilt against the new set.

`interop.ps1` and `Gen.cs` name the Unity version of the client (6000.3.15). A Rust update that
moves to another Unity version needs both changed, and the base libraries for that version.

## Releasing to the owner

1. Verify the head of the branch in the game.
2. Put that commit in `release.txt` and push.
3. `zz launcher` builds exactly that commit, keeps the plugin in `play\`, saves `skate.ps1`,
   `session.ps1`, `watch.ps1`, `clean.ps1` and `sticks.ps1` of that commit in `launcher\` and writes
   two files on the desktop. The launcher uses only these local copies, so later changes in the repository do
   not reach the owner until the next `zz launcher`.
4. `zz launchertest`.

## Settings

Set by the scripts for one another; `sv name value` sets one by hand.

| Variable | Used by | Meaning |
| --- | --- | --- |
| `$pref` | `zz`, `zg`, `build` | Branch to work from (default `main`). |
| `$pto` | `ref` | The branch to switch to, written without its dashes and slashes. |
| `$pcommit` | `build` | Build this commit instead of the head of the branch. |
| `$pnext` | `build`, `runtime` | Script to run after a successful build; used once, also when the build stops early. |
| `$ptag` | `session` | Label in the session folder's name. |
| `$psteps` | `session` | Steps for the plugin. |
| `$pplug`, `$prt` | `session` | Folder with the plugin, folder with the changed runtime. |
| `$pdone` | `session` | Seconds after which a session in a world is ended. |
| `$plimit`, `$pquiet`, `$pdirect`, `$plisten` | `play` | End the session that many seconds after its start; turn the sound down; start the client directly with `+connect` instead of through the Steam shortcut; with `1`, keep `UnityLogListening` on (the inbox probe needs it; `sv plisten 0` turns it off again). |
| `$pgentest` | `interop` | Generate into a folder of its own instead of the one sessions use. |
| `$psteam`, `$pshortcut` | `session` | Start through the Steam shortcut of that name. |
| `$pconnect` | `session` | Join the server in `server.txt` at start (direct start only). |
| `$pkeep` | `session` | Keep the game's window in front for the whole session; without it the window is brought to the front once. |
| `$psticks` | `session` | Path of the controller reader to start; without it the reader is fetched from the branch. |
| `$pwatch`, `$pserve` | `sticks` | Seconds to watch; or the name of the process to share the controller with until it has come and gone. |
| `$pshow` | `flicks` | Number of the movement to print point by point instead of the table. |
| `$pserver` | `pack` | With `0`, leave `server.txt` out of the friend's pack. |
| `$plast`, `$pfile` | `log`, `file`, `more`, `last` | The last session's folder (`last` sets it to the newest one on the PC), the file being paged. |
| `$pinc`, `$pname` | `log`, `file` | Filter expression, file name. |
| `$pt`, `$pp`, `$pfind` | `survey` | Type names, member filter, type-name search. |
