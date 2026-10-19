# Client 0.18.29 candidate tools

Build and inspect the immutable client source `58f82909300f9b973b15b2031ddc397b4b79d1c6` before installing it. This package adds no client gameplay code and never launches Rust.

Use an exact reviewed package commit and its `pins.json` SHA256 from the PR handoff. The short handoff bootstrap downloads only this package into a fresh Downloads folder, checks `pins.json` before reading it, and verifies each executable tool before returning the folder. Do not substitute a mutable branch URL.

`pins.json` records the three tool hashes, this README's hash, the installed-client identity, eight model hashes and all 48 public source/model/manifest pins. Shared sources retain their independent commit pins. The bootstrap pins `pins.json` itself.

## Requirements

- Windows with the existing `Downloads\claude-loader-probe` SDK, BepInEx and launcher setup.
- Installed Rust Steam build `25903346` and its successful error-free `gen-25903346\gen.log`.
- Exactly 191 generated DLLs plus `MethodAddressToToken.db` and `MethodXrefScanCache.db` (193 generation files), and the exact 356 compiler references.
- The known client 0.18.27 DLL: 416768 bytes, SHA256 `840733f909ec53008bb247b4b60c590e026d87467ebfddbd39cf85910d4eca4a`.
- A server compatible with the wheel's `skate.builder` selection/placement protocol from PR #509.

## Build, inspect, install

Keep the package folder returned by the verified bootstrap in `$package`.

```powershell
& "$package\build.ps1"
& "$package\install.ps1" -Candidate '<exact candidate folder printed by the successful build>'
```

The build creates a fresh candidate only. It verifies all 48 downloaded inputs, hashes every compiler reference before and after compilation, and writes `build.json` and `compiler.log`. Its output folder contains only the DLL and eight models. Require `compiled: true`, compiler exit 0 and zero errors; read the recorded warnings. A printed candidate path alone is not success.

The installer defaults to verification only and rejects a running Rust process. Require `ready: true`, `applied: false` and inspect the proposed changes. Install that same candidate explicitly:

```powershell
& "$package\install.ps1" -Candidate '<same verified candidate folder>' -Apply
```

Apply first creates a protected per-user/SYSTEM backup and verifies every original. It updates the play DLL, one launcher steps literal and its displayed version; a known model is replaced only when its pinned hash differs. Unexpected play files cause refusal. Installation checks the exact successful receipt, live references, source/model hashes and the existing .27 DLL again. Nothing writes into the Steam Rust installation or starts/stops the game.

This candidate enables `physicsdiag,skate` in the existing desktop launcher for a controlled measurement. Record the returned backup path. It contains the original files, exact plan, verified build receipt and a self-contained `rollback.ps1`. With Rust closed, run that backup's rollback without arguments to verify, then with `-Apply` to restore this transaction. It refuses unrelated target changes; installation failure attempts the same guarded restoration.

## Validation limits

The pinned client source compiled against the current Windows reference inventory with 38 sources, 356 references, eight models, zero errors and zero warnings. The package scripts and bootstrap parse locally; exact source/tool pins and transport are checked. Atomic replacement and guarded rollback are exercised with real files in a local fixture, using an explicit CLR null string for the optional backup path. Windows installation, actual diagnostic pair reads, wheel input and in-game riding remain separate gates. A successful compiler or installer receipt does not prove those runtime behaviors. No permanent test harness, binary, generated interop, game data, credentials or operational log is included.
