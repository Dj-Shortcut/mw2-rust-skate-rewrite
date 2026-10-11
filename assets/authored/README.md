# From-scratch content

These models are authored by this project's Blender generator. No original
game files, imported meshes, texture packs, sound packs or model libraries
are inputs. Source code is Apache-2.0; the generated models are
[CC0-1.0](CC0-1.0.txt).

Regenerate the original four with Blender 4.3.2 and the ramps with Blender
5.2.0. Replace these example executable paths with the matching installations:

```bash
/path/to/blender-4.3.2/blender -b --factory-startup --python scripts/generate_authored_content.py -- --output assets/authored
/path/to/blender-5.2.0/blender -b --factory-startup --python scripts/generate_skate_ramp_assets.py -- --output assets/authored
```

The output includes editable `.blend` sources, self-contained `.glb` models
and a manifest of bounds, vertex/triangle counts and SHA-256 hashes.
Coordinates in Blender are Z-up, front toward -Y; glTF export converts to
its standard Y-up coordinates. Units are meters.

- `masked_operator`: articulated tactical character, 17-bone skeleton and
  authored idle, walk and aim animation clips.
- `carbine`: authored receiver, barrel, furniture, magazine, rail and sight.
- `skateboard`: curved deck, trucks, bearings and four wheels.
- `timber_construction`: foundation, planked floor and wall, using 120 game
  units at 0.0254 meters per unit.
- `skate_bank`, `skate_kicker`, `skate_quarterpipe`, `skate_ledge_low`,
  `skate_ledge_high`, `skate_long_ledge`, `skate_funbox`, `skate_halfpipe`:
  authored park structures with wood, concrete and steel materials, coping,
  solid sides/platforms, and separate static collision proxies.

Run them in that order: the first replaces the manifest with its four entries;
the ramp generator preserves other entries and replaces only its eight.
Regenerating ramps alone preserves other models and their records.
The ramp GLBs default to the `Visual` scene. `CollisionProxies` is a separate,
opt-in scene; the `.blend` keeps its matching collection hidden from the
viewport/render. There are no transparent collision materials. An engine
adapter must explicitly turn proxy meshes into colliders. The open halfpipe
must not be replaced by a single convex hull. Individual meshes are closed;
adjacent pieces intentionally remain distinct. Units/pivots, full visual and
collision bounds and the difference from the native park assembly are in
[the ramp guide](../../docs/RUST-SKATE-RAMP-ASSETS.md).
For actual Blender previews, add `--preview-dir /absolute/private/output`.
Editable sources include cameras/lights; exports exclude those. Materials use
authored colours and PBR values without external textures. The manifest records
authoring versions; another version can change export layout/triangulation.

The native frontend loads operator, carbine and skateboard scenes; animation
and full integration remain unfinished. The standalone rewrite is parked.
The ramp pack is not wired into that frontend or the active Rust PC mod yet.
