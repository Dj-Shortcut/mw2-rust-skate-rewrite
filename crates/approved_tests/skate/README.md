# Approved skate scenarios

## Recorded gestures

The three scenarios requested by the owner through [issue #337](https://github.com/Dj-Shortcut/mw2-rust-rust-rewrite/issues/337#issuecomment-6098063930) replay the exact recorded stick positions and timestamps. Each position is held until the next timestamp; long holds are subdivided at the module's maximum step without interpolating new positions.

- `skate_air_pullback_ollie`: a pull back and pop in the air produces one Ollie, rather than being consumed as a late flip.
- `skate_ground_diagonal_kickflip`: the recorded diagonal flick produces one Ollie with one Kickflip.
- `skate_ground_roll_ollie_no_manual`: the recorded roll and recoil produce one plain Ollie and no manual.

Build the shared library using genuine dedicated-server framework references, then run its actual DLL on .NET 8:

```sh
dotnet build mods/rust/shared/SkateMotion.csproj -p:RustManagedPath=/absolute/path/to/managed
dotnet run --project crates/approved_tests/skate/SkateGestureScenarios.csproj
```

An alternate built DLL can be supplied with `-p:SkateLibraryPath=/absolute/path/to/SkateMotion.dll`. Outputs stay in ignored `context/`.

These scenarios check gesture recognition and its compositional/legacy outputs. They do not start the Rust client or server and do not establish controller feel, animation or a landed native trick.

## Park layouts

The owner approved two layout scenarios by name for [Task 4](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/issues/337#issuecomment-6099499139):

- `skate_park_piece_layouts`: the eight pieces' block positions, orientations, native materials and adjoining nominal square-floor edges, including the complete halfpipe's flat, transitions, decks and railings.
- `skate_park_anchor_yaw_and_snap`: translated and rotated layouts preserve the snapped anchor supplied by ordinary native construction, including anchors away from the world origin.

Build the actual server plugin using genuine dedicated-server references, then execute its engine-free helpers on .NET 8:

```sh
dotnet build mods/rust/plugins/ShortcutSkate.csproj -p:RustManagedPath=/absolute/path/to/managed
dotnet run --project crates/approved_tests/skate/SkateParkLayoutScenarios.csproj
```

Supply `-p:ActualPluginPath=/absolute/path/to/ShortcutSkate.dll` for another built DLL. Outputs stay in ignored `context/`; the runner prints its loaded DLL path and SHA-256.

These checks use the layout helpers in that DLL, with independent expected positions and seams. They do not verify native floor dimensions/colliders, building privilege, stability, save/load callbacks, demolition or gameplay, and they do not start the Rust client or server.
