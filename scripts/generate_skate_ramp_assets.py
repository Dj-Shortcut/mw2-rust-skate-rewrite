#!/usr/bin/env python3
"""SPDX-License-Identifier: Apache-2.0

Author the original CC0 skate park model pack with Blender 4.3 or later.
Run with Blender, passing --output and optionally --preview-dir after --.
"""

import argparse
import hashlib
import json
import math
import struct
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector


NAMES = (
    "skate_bank", "skate_kicker", "skate_quarterpipe", "skate_ledge_low",
    "skate_ledge_high", "skate_long_ledge", "skate_funbox", "skate_halfpipe",
)
RADIUS = 2.5
CURVE_STEPS = 64
SKIN = 0.14
COPING_RADIUS = 0.03
SOURCE = "scripts/generate_skate_ramp_assets.py"


def point(right, forward, height):
    return (right, -forward, height)


def material(name, color, metallic=0.0, roughness=0.6):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1.0)
    mat.use_nodes = True
    shader = mat.node_tree.nodes["Principled BSDF"]
    shader.inputs["Base Color"].default_value = (*color, 1.0)
    shader.inputs["Metallic"].default_value = metallic
    shader.inputs["Roughness"].default_value = roughness
    return mat


def clear():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for collection in list(bpy.data.collections):
        bpy.data.collections.remove(collection)
    for data in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for item in list(data):
            if item.users == 0:
                data.remove(item)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    for name in ("Visual", "CollisionProxies"):
        collection = bpy.data.collections.new(name)
        scene.collection.children.link(collection)
    return {
        "concrete": material("Warm concrete", (0.48, 0.51, 0.49), roughness=0.82),
        "wood": material("Birch ride panels", (0.53, 0.32, 0.14), roughness=0.66),
        "wood_light": material("Plywood edge", (0.72, 0.50, 0.26), roughness=0.74),
        "wood_dark": material("Panel alternate", (0.39, 0.22, 0.10), roughness=0.72),
        "metal": material("Brushed steel", (0.36, 0.43, 0.47), metallic=0.88, roughness=0.32),
        "frame": material("Painted teal frame", (0.035, 0.23, 0.25), metallic=0.55, roughness=0.5),
        "dark": material("Steel underside", (0.045, 0.065, 0.075), metallic=0.7, roughness=0.52),
        "mark": material("Safety ochre", (0.90, 0.48, 0.065), roughness=0.64),
    }


def finish(obj, name, mat, collision=False):
    obj.name = ("COLLISION__" if collision else "VISUAL__") + name
    obj["role"] = "collision" if collision else "visual"
    obj["collision_kind"] = "static_concave" if collision else "none"
    obj["license"] = "CC0-1.0"
    for collection in list(obj.users_collection):
        collection.objects.unlink(obj)
    bpy.data.collections["CollisionProxies" if collision else "Visual"].objects.link(obj)
    if mat is not None:
        obj.data.materials.append(mat)
    return obj


def mesh(name, vertices, faces, mat, collision=False, smooth_faces=()):
    data = bpy.data.meshes.new(name)
    data.from_pydata(vertices, [], faces)
    data.update()
    bm = bmesh.new()
    bm.from_mesh(data)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    if bm.calc_volume(signed=True) < 0:
        bmesh.ops.reverse_faces(bm, faces=list(bm.faces))
    bm.to_mesh(data)
    bm.free()
    obj = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(obj)
    finish(obj, name, mat, collision)
    for index in smooth_faces:
        data.polygons[index].use_smooth = True
    return obj


def box(name, center, size, mat, bevel=0.0, collision=False):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=point(*center))
    obj = bpy.context.object
    obj.scale = (size[0], size[1], size[2])
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        modifier = obj.modifiers.new("Edge radius", "BEVEL")
        modifier.width = min(bevel, min(size) / 4)
        modifier.segments = 3
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    return finish(obj, name, mat, collision)


def cylinder(name, start, end, radius, mat, collision=False, sides=20):
    a, b = Vector(point(*start)), Vector(point(*end))
    direction = b - a
    bpy.ops.mesh.primitive_cylinder_add(vertices=sides, radius=radius,
        depth=direction.length, location=(a + b) / 2)
    obj = bpy.context.object
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = direction.to_track_quat("Z", "Y")
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    for polygon in obj.data.polygons:
        polygon.use_smooth = len(polygon.vertices) == 4
    return finish(obj, name, mat, collision)


def solid_profile(name, profile, width, bottom, mat, collision=False, smooth=False):
    # Each strip is closed independently; a whole-bowl convex hull would fill its opening.
    outline = profile + [(profile[-1][0], bottom), (profile[0][0], bottom)]
    count = len(outline)
    vertices = [point(side, forward, height)
        for side in (-width / 2, width / 2) for forward, height in outline]
    faces = [tuple(range(count - 1, -1, -1)), tuple(range(count, 2 * count))]
    for i in range(count):
        j = (i + 1) % count
        faces.append((i, j, count + j, count + i))
    top = range(2, 2 + len(profile) - 1) if smooth else ()
    return mesh(name, vertices, faces, mat, collision, top)


def strip(name, profile, width, thickness, mat, smooth=False):
    count = len(profile)
    vertices = [point(x, forward, height - depth)
        for depth in (0.0, thickness) for x in (-width / 2, width / 2)
        for forward, height in profile]
    faces = []
    for i in range(count - 1):
        j = i + 1
        faces.extend(((i, j, count + j, count + i),
            (2 * count + i, 3 * count + i, 3 * count + j, 2 * count + j),
            (i, 2 * count + i, 2 * count + j, j),
            (count + i, count + j, 3 * count + j, 3 * count + i)))
    faces.extend(((0, count, 3 * count, 2 * count),
        (count - 1, 2 * count - 1, 4 * count - 1, 3 * count - 1)))
    obj = mesh(name, vertices, faces, mat, smooth_faces=range(0, 4 * (count - 1), 4) if smooth else ())
    obj["ride_surface"] = True
    return obj


def cladding(name, profile, width, mats):
    for side in (-1, 1):
        obj = solid_profile(name + ("_left" if side < 0 else "_right"), profile,
            0.10, -SKIN, mats["wood_light"])
        obj.location.x = side * (width / 2 + 0.05)
    for forward, height in profile[::max(1, len(profile) // 8)]:
        if height > 0.12:
            cylinder(name + "_rivet", (-width / 2 - 0.106, forward, height * 0.55),
                (-width / 2 - 0.098, forward, height * 0.55), 0.012, mats["metal"], sides=12)
            cylinder(name + "_rivet", (width / 2 + 0.098, forward, height * 0.55),
                (width / 2 + 0.106, forward, height * 0.55), 0.012, mats["metal"], sides=12)


def coping(name, width, forward, height, mats):
    start, end = (-width / 2, forward, height), (width / 2, forward, height)
    cylinder(name, start, end, COPING_RADIUS, mats["metal"])
    cylinder(name, start, end, COPING_RADIUS, None, collision=True, sides=16)


def deck(name, width, start, end, height, mats):
    length = abs(end - start)
    forward = (start + end) / 2
    box(name + "_solid", (0, forward, height - (SKIN + 0.014) / 2),
        (width, length, SKIN - 0.014), mats["dark"])
    box(name, (0, forward, height - SKIN / 2), (width, length, SKIN), None, collision=True)
    panels = max(1, round(length / 0.3))
    for i in range(panels):
        f = min(start, end) + length * (i + 0.5) / panels
        plank = box(name + "_plank", (0, f, height - 0.007), (width, length / panels - 0.004, 0.014),
            mats["wood"] if i % 3 else mats["wood_dark"])
        plank["ride_surface"] = True
    for right in (-width / 2 + 0.14, width / 2 - 0.14):
        box(name + "_support", (right, forward, (height - SKIN) / 2),
            (0.16, max(0.05, length - 0.08), max(0.01, height - SKIN)), mats["frame"], bevel=0.008)


def backrail(name, width, forward, height, mats):
    for x in (-width / 2 + 0.08, 0.0, width / 2 - 0.08):
        box(name + "_post", (x, forward, height + 0.43), (0.09, 0.09, 0.86), mats["wood_light"], bevel=0.008)
        box(name + "_post", (x, forward, height + 0.43), (0.09, 0.09, 0.86), None, collision=True)
    for level in (0.35, 0.79):
        box(name + "_rail", (0, forward, height + level), (width, 0.085, 0.12), mats["wood"], bevel=0.012)
        box(name + "_rail", (0, forward, height + level), (width, 0.085, 0.12), None, collision=True)


def curved_profile(start, direction=1):
    return [(start + direction * RADIUS * math.sin(i * math.pi / (2 * CURVE_STEPS)),
        RADIUS * (1 - math.cos(i * math.pi / (2 * CURVE_STEPS)))) for i in range(CURVE_STEPS + 1)]


def transition(name, width, start, direction, mats):
    profile = curved_profile(start, direction)
    if direction < 0:
        profile.reverse()
    solid_profile(name + "_structure", [(f, z - 0.025) for f, z in profile],
        width, -SKIN, mats["dark"])
    solid_profile(name, profile, width, -SKIN, None, collision=True)
    strip(name + "_ride", profile, width, 0.025, mats["metal"], smooth=True)
    cladding(name + "_sides", profile, width, mats)
    end = start + direction * RADIUS
    coping(name + "_coping", width, end, RADIUS, mats)
    seam_profile = [(f - direction * 0.0005 * abs(f - start) / RADIUS,
        z + 0.0005 * (1 - z / RADIUS)) for f, z in profile]
    for fraction in (0.25, 0.5, 0.75):
        x = (fraction - 0.5) * width
        edge = strip(name + "_panel_seam", seam_profile, 0.006, 0.026, mats["dark"], smooth=True)
        edge.location.x = x


def ramp(mats, angle, name):
    radians = math.radians(angle)
    run, rise, width = 3 * math.cos(radians), 3 * math.sin(radians), 3.0
    profile = [(0.0, 0.0), (run, rise)]
    solid_profile(name + "_structure", [(f, z - 0.025) for f, z in profile],
        width, -SKIN, mats["dark"])
    solid_profile(name, profile, width, -SKIN, None, collision=True)
    strip(name + "_ride", profile, width, 0.025, mats["wood"])
    cladding(name + "_sides", profile, width, mats)
    coping(name + "_lip", width, run, rise, mats)
    deck(name + "_platform", width, run, run + 0.75, rise, mats)
    box(name + "_entry", (0, 0.04, -0.012), (width, 0.08, 0.018), mats["metal"])
    return {"kind": name, "pivot": "low-entry midpoint on riding plane", "ride_width": width,
        "slope_degrees": angle, "rise": rise, "slope_run": run, "platform_length": 0.75}


def ledge(mats, width, height, kind):
    depth = 3.0
    body = box("concrete_body", (0, 0, height / 2), (width, depth, height), mats["concrete"], bevel=0.016)
    body["ride_surface"] = True
    box("ride", (0, 0, height / 2), (width, depth, height), None, collision=True)
    for f in (-depth / 2, depth / 2):
        box("grind_edge", (0, f, height - 0.015), (width, 0.055, 0.030), mats["metal"], bevel=0.004)
    for x in (-width / 2, width / 2):
        box("grind_edge", (x, 0, height - 0.015), (0.055, depth - 0.10, 0.030), mats["metal"], bevel=0.004)
    for x in (-width / 2 + 0.2, width / 2 - 0.2):
        box("height_marker", (x, -depth / 2 - 0.001, height * 0.45),
            (0.06, 0.004, height * 0.60), mats["mark"])
    return {"kind": kind, "pivot": "base centre", "ride_width": width,
        "ride_length": depth, "rise": height}


def quarterpipe(mats):
    width = 3.0
    transition("quarter", width, 0.0, 1, mats)
    deck("top_deck", width, RADIUS, RADIUS + 1.5, RADIUS, mats)
    backrail("back", width, RADIUS + 1.45, RADIUS, mats)
    return {"kind": "quarterpipe", "pivot": "low-entry midpoint on riding plane", "ride_width": width,
        "transition_radius": RADIUS, "rise": RADIUS, "transition_run": RADIUS, "platform_length": 1.5}


def funbox(mats):
    width, height, half = 3.0, 3 * math.sin(math.radians(15)), 1.5
    run = 3 * math.cos(math.radians(15))
    box("core", (0, 0, (height - 0.02) / 2), (width, 3.0, height - 0.02), mats["frame"])
    box("core", (0, 0, height / 2), (width, 3.0, height), None, collision=True)
    for direction in (-1, 1):
        profile = [(half, height), (half + run, 0)]
        if direction < 0:
            profile = [(-forward, z) for forward, z in profile][::-1]
        solid_profile("bank", [(f, z - 0.025) for f, z in profile], width, -SKIN, mats["dark"])
        solid_profile("bank", profile, width, -SKIN, None, collision=True)
        strip("bank_ride", profile, width, 0.025, mats["wood"])
        cladding("bank_sides", profile, width, mats)
        coping("deck_edge", width, direction * half, height, mats)
    top = box("top", (0, 0, height - 0.010), (width, 3.0, 0.020), mats["wood"])
    top["ride_surface"] = True
    for f in (-0.9, -0.3, 0.3, 0.9):
        box("deck_seam", (0, f, height - 0.001), (width, 0.007, 0.003), mats["wood_dark"])
    return {"kind": "funbox", "pivot": "base centre below flat deck", "ride_width": width,
        "rise": height, "flat_length": 3.0, "ride_length": 3.0 + 2 * run, "slope_degrees": 15}


def halfpipe(mats):
    width, half, platform = 6.0, 3.0, 1.5
    flat = box("flat", (0, 0, -SKIN / 2), (width, 6.0, SKIN), mats["concrete"])
    flat["ride_surface"] = True
    box("flat", (0, 0, -SKIN / 2), (width, 6.0, SKIN), None, collision=True)
    for direction in (-1, 1):
        name = "front" if direction > 0 else "back"
        transition(name, width, direction * half, direction, mats)
        edge = direction * (half + RADIUS)
        end = edge + direction * platform
        deck(name + "_deck", width, edge, end, RADIUS, mats)
        backrail(name + "_rail", width, end - direction * 0.05, RADIUS, mats)
    box("flat_joint", (0, 0, -0.0005), (width, 0.006, 0.002), mats["dark"])
    for direction in (-1, 1):
        box("flat_joint", (0, direction * (half + 0.003) / 2, -0.0005),
            (0.006, half - 0.003, 0.002), mats["dark"])
    return {"kind": "halfpipe", "pivot": "centre of the 6 x 6 m flat riding plane",
        "ride_width": width, "flat_width": 6.0, "flat_length": 6.0,
        "transition_radius": RADIUS, "rise": RADIUS, "platform_length": platform,
        "ride_length": 2 * half + 2 * RADIUS + 2 * platform}


GENERATORS = {
    "skate_bank": lambda mats: ramp(mats, 12, "bank"),
    "skate_kicker": lambda mats: ramp(mats, 25, "kicker"),
    "skate_quarterpipe": quarterpipe,
    "skate_ledge_low": lambda mats: ledge(mats, 3.0, 0.4, "ledge-low"),
    "skate_ledge_high": lambda mats: ledge(mats, 3.0, 0.8, "ledge-high"),
    "skate_long_ledge": lambda mats: ledge(mats, 9.0, 0.4, "long-ledge"),
    "skate_funbox": funbox,
    "skate_halfpipe": halfpipe,
}


def mesh_report(objects):
    total_vertices = total_triangles = 0
    points = []
    for obj in objects:
        data = obj.data
        data.calc_loop_triangles()
        if any(not math.isfinite(value) for vertex in data.vertices for value in vertex.co):
            raise ValueError(obj.name + ": nonfinite vertex")
        if any(triangle.area <= 1e-12 for triangle in data.loop_triangles):
            raise ValueError(obj.name + ": degenerate triangle")
        bm = bmesh.new()
        bm.from_mesh(data)
        if any(not edge.is_manifold for edge in bm.edges) or bm.calc_volume(signed=True) <= 1e-12:
            bm.free()
            raise ValueError(obj.name + ": not a closed outward solid")
        bm.free()
        if abs(obj.matrix_world.determinant()) < 1e-8 or obj.matrix_world.determinant() < 0:
            raise ValueError(obj.name + ": invalid mirrored transform")
        total_vertices += len(data.vertices)
        total_triangles += len(data.loop_triangles)
        points.extend(obj.matrix_world @ vertex.co for vertex in data.vertices)
    if not points:
        raise ValueError("No meshes generated")
    lo = [min(p[i] for p in points) for i in range(3)]
    hi = [max(p[i] for p in points) for i in range(3)]
    return {"vertices": total_vertices, "triangles": total_triangles, "bounds": [lo, hi]}


def separate_scenes(path):
    raw = path.read_bytes()
    magic, version, length = struct.unpack_from("<III", raw)
    if magic != 0x46546C67 or version != 2 or length != len(raw):
        raise ValueError("Invalid Blender GLB export")
    json_size, chunk_type = struct.unpack_from("<II", raw, 12)
    if chunk_type != 0x4E4F534A:
        raise ValueError("Missing GLB JSON chunk")
    data = json.loads(raw[20:20 + json_size])
    roots = data["scenes"][data.get("scene", 0)]["nodes"]
    collision = [i for i in roots if data["nodes"][i].get("extras", {}).get("role") == "collision"]
    visual = [i for i in roots if i not in collision]
    if not collision or not visual or any("children" in data["nodes"][i] for i in roots):
        raise ValueError("Unexpected GLB root hierarchy")
    data["scenes"] = [{"name": "Visual", "nodes": visual},
        {"name": "CollisionProxies", "nodes": collision}]
    data["scene"] = 0
    encoded = json.dumps(data, separators=(",", ":"), ensure_ascii=True).encode("utf-8")
    encoded += b" " * ((-len(encoded)) % 4)
    tail = raw[20 + json_size:]
    output = struct.pack("<III", magic, version, 20 + len(encoded) + len(tail))
    output += struct.pack("<II", len(encoded), chunk_type) + encoded + tail
    path.write_bytes(output)


def prepare_preview(visual):
    scene = bpy.context.scene
    report = mesh_report(visual)
    lo, hi = (Vector(bound) for bound in report["bounds"])
    centre = (lo + hi) / 2
    radius = max((hi - lo).length / 2, 1.0)
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 24
    scene.cycles.use_denoising = True
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.16, 0.20, 0.24, 1.0)
    scene.world.node_tree.nodes["Background"].inputs[1].default_value = 0.7
    for offset, power, size in (((-3, -4, 5), 500, 4), ((3, -1, 2), 180, 3), ((0, 4, 5), 550, 3)):
        bpy.ops.object.light_add(type="AREA", location=centre + Vector(offset) * radius)
        light = bpy.context.object
        light.data.energy = power * radius * radius
        light.data.shape = "DISK"
        light.data.size = size * radius
        light.rotation_euler = (centre - light.location).to_track_quat("-Z", "Y").to_euler()
    bpy.ops.object.camera_add(location=centre + Vector((1.8, 2.7, 1.9)) * radius)
    camera = bpy.context.object
    camera.rotation_euler = (centre - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.type = "ORTHO"
    rotation = camera.rotation_euler.to_quaternion()
    right, up = rotation @ Vector((1, 0, 0)), rotation @ Vector((0, 1, 0))
    vertices = [obj.matrix_world @ v.co - centre for obj in visual for v in obj.data.vertices]
    horizontal = [v.dot(right) for v in vertices]
    vertical = [v.dot(up) for v in vertices]
    screen_centre = centre + right * ((min(horizontal) + max(horizontal)) / 2)
    screen_centre += up * ((min(vertical) + max(vertical)) / 2)
    camera.location += screen_centre - centre
    camera.data.ortho_scale = max(max(horizontal) - min(horizontal),
        (max(vertical) - min(vertical)) * (1000 / 760)) * 1.24
    scene.camera = camera
    scene.render.resolution_x, scene.render.resolution_y = 1000, 760
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = True
    scene.view_settings.view_transform = "AgX"
    return scene


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--preview-dir", type=Path)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    args.output.mkdir(parents=True, exist_ok=True)
    if args.preview_dir:
        args.preview_dir.mkdir(parents=True, exist_ok=True)
    bpy.context.preferences.filepaths.save_version = 0
    manifest_path = args.output / "manifest.json"
    manifest = json.loads(manifest_path.read_text()) if manifest_path.is_file() else {
        "schema": 1, "license": "CC0-1.0", "units": "meters", "source": SOURCE,
        "generator_blender": bpy.app.version_string, "assets": []}
    if not isinstance(manifest.get("assets"), list):
        raise ValueError("Existing asset manifest has no assets list")
    preserved = [entry for entry in manifest["assets"] if entry.get("name") not in NAMES]
    entries = []
    for name in NAMES:
        mats = clear()
        dimensions = GENERATORS[name](mats)
        bpy.context.view_layer.update()
        visual = list(bpy.data.collections["Visual"].objects)
        collision = list(bpy.data.collections["CollisionProxies"].objects)
        visual_report, collision_report = mesh_report(visual), mesh_report(collision)
        if visual_report["triangles"] > 12000 or collision_report["triangles"] > 3000:
            raise ValueError(name + ": mesh budget exceeded")
        bpy.ops.object.select_all(action="DESELECT")
        for obj in visual + collision:
            obj.select_set(True)
        destination = args.output / (name + ".glb")
        bpy.ops.export_scene.gltf(filepath=str(destination.resolve()), export_format="GLB",
            use_selection=True, export_animations=False, export_skins=False, export_cameras=False,
            export_lights=False, export_extras=True)
        separate_scenes(destination)
        for obj in collision:
            obj.hide_render = True
            obj.hide_set(True)
        scene = prepare_preview(visual)
        scene.render.filepath = "//" + name + "-preview.png"
        scene["asset_license"] = "CC0-1.0"
        scene["asset_source"] = SOURCE
        scene["coordinate_contract"] = "metres; Z up; forward -Y; export Y up, forward +Z"
        bpy.ops.wm.save_as_mainfile(filepath=str((args.output / (name + ".blend")).resolve()))
        if args.preview_dir:
            scene.render.filepath = str((args.preview_dir / (name + ".png")).resolve())
            bpy.ops.render.render(write_still=True)
        entry = {"name": name, "file": name + ".glb", **visual_report,
            "bounds_space": "Blender Z-up, forward -Y", "animations": [], "license": "CC0-1.0",
            "source": SOURCE, "generator_blender": bpy.app.version_string,
            "default_scene": "Visual", "collision_scene": "CollisionProxies",
            "collision": collision_report, "dimensions": dimensions,
            "sha256": hashlib.sha256(destination.read_bytes()).hexdigest(),
            "bytes": destination.stat().st_size}
        entries.append(entry)
        print("SKATE_ASSET", json.dumps(entry, sort_keys=True))
    manifest["assets"] = preserved + entries
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    print("Generated eight original skate park assets:", args.output.resolve())


if __name__ == "__main__":
    main()
