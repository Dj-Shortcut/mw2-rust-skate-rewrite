# Rust Skate for a friend

How someone without the development tools gets the skate mod on his own PC and rides on the
owner's server. The owner makes one zip from the launcher that is installed and played on his PC
([`zz pack`](tools/README.md#scripts)); the friend unpacks it and runs its installer. Nothing is
downloaded or built on the friend's PC, and the zip plays exactly the plugin the owner plays.

**Not yet done on any PC.** The pack and its installer exist as scripts in this repository and
were run through a dry run with stand-in files only. Making a real pack on the owner's PC,
installing it on a second PC and playing from it are still open; see the [status](#status).

## What the friend needs

- Windows, Rust bought on Steam and installed in Steam's own library on `C:` (the launcher knows
  only `C:\Program Files (x86)\Steam\steamapps\common\Rust`), Steam signed in.
- The same Rust build as the owner. The pack is made for one Steam build; after a Rust update
  the launcher refuses to start and the owner makes a new pack.
- An Xbox controller (XInput) for the controls as in the skate. games. The keyboard works too
  ([keys](README.md#keys)).
- Free space on `C:` for the unpacked pack, mostly the interop set for the game build; the
  installer measures it and refuses when it is short.
- The owner's server address. The mod is for that server only: it runs without anti-cheat. Rust
  started by the launcher cannot join servers with anti-cheat, and Rust started the normal way from
  Steam is unchanged, because the mod is in the Rust folder only while the launcher's window is open.

## The owner: making the pack

On the PC that plays the mod, with the launcher installed and prepared for the installed Rust
build (`zz launcher` after every Rust update):

```powershell
zz pack
```

It writes `Downloads\RustSkate-<commit>-build<build>.zip` from `Downloads\claude-loader-probe`:
the unpacked loader (`bep788\`), the interop set of the installed build (`gen-<build>\out`, its
log), the changed runtime (`rt\`), the released plugin and its park models (`play\`), the
launcher's own scripts and `version.txt` (`launcher\`), and `server.txt` when it is there
(`sv pserver 0` leaves it out). Next to them: `install.ps1` from the branch the tools were loaded
from, `Install Rust Skate.cmd`, `pack.txt` (what is inside) and `README.txt` with the steps below
in plain text. It refuses when the launcher is missing, was prepared for another build than the
installed one, or the interop set is incomplete.

The pack holds files made from the owner's own installed game (the interop set) and the loader.
It is handed to the friends it was made for and is not published; nothing of it belongs in this
repository.

## The friend: installing and playing

1. Unpack the zip anywhere (right-click, Extract All) and open the unpacked folder.
2. In Steam: Games > Add a Non-Steam Game to My Library > Browse, pick
   `C:\Program Files (x86)\Steam\steamapps\common\Rust\RustClient.exe`, add it and keep its name,
   `RustClient`. On its page in the library, under the controller icon, choose the layout template
   "Keyboard (WASD) and Mouse". The launcher starts Rust through this shortcut so that the
   controller works on foot as well as on the board ([why](README.md#the-controller)); with the
   "Gamepad" layout the controller does nothing in Rust.
3. Double-click `Install Rust Skate.cmd`. It checks Steam, Rust, the Rust build, free space and
   the shortcut, copies the pack to `Downloads\claude-loader-probe` and puts two files on the
   desktop: **Rust Skate** and **Rust Skate - remove mod files**. It refuses with a plain
   sentence when something is missing, and installs nothing then.
4. Start **Rust Skate** from the desktop and keep its window open while playing. Steam is started
   when it is not running. In Rust's main menu press `F1` and type `connect <address>` (the
   window shows the address when it was packed in), then `Enter`.
5. On the server: `Y` on the controller, `K`, or two quick jumps gets you on the board; `Y` or `K`
   again gets you off. `A` pushes, the left stick steers, `B` brakes, the right stick held back and
   flicked forward is an ollie, flicked to a forward diagonal a kickflip or heelflip, `LT`/`RT` held
   in the air a grab. The first seconds on the board show the controls; the whole table is in the
   [client README](README.md#controls).
6. Quit Rust the normal way. The window ends with "Done. The Rust folder is back to normal." If it
   does not, or the PC went off while playing, run **Rust Skate - remove mod files** before
   starting Rust normally: a loader left in the Rust folder would be loaded by a normal start too.

To remove everything: first run **Rust Skate - remove mod files** and read that it found no
loader files in the Rust folder (`clean.ps1` and its note in the staging folder are the only way a
loader left behind by a cut-off session is taken out again), then delete the two desktop files and
the folder `Downloads\claude-loader-probe`. A new pack from the owner is installed over the old one
the same way; it must be unpacked into a folder of its own, not straight into `Downloads`.

## Status

- Code present: `tools/pack.ps1`, `tools/install.ps1`, the launcher's messages for a friend
  (`tools/skate.ps1`). The three scripts parse; `pack.ps1` and `install.ps1` were run on Linux
  against a stand-in folder tree with the Windows paths swapped: the zip holds the expected files
  and nothing else from `play\`, `pack.txt` carries the build, the installer copies the tree,
  writes the desktop files, reports a missing Steam shortcut and refuses another Rust build.
- Not verified: no real pack has been made on the owner's PC; nothing has been installed on a
  second PC; no friend has started Rust through the installed launcher, joined the server or
  ridden. The size of a real pack, Windows PowerShell 5.1 running these two scripts, Steam Input
  on the friend's shortcut and two clients on the owner's server are all still to be seen.
- Known limits: Rust in a Steam library other than `C:` is refused, the Steam shortcut is made by
  hand, and the owner's server must accept the second rider (its ShortcutSkate plugin, or its
  speed checks turned off; see [playing it](README.md#playing-it-the-owners-launcher)).
