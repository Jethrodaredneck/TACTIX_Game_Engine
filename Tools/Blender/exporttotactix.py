bl_info = {
    "name": "TACTIX Asset Bridge",
    "author": "TACTIX",
    "version": (2, 0, 0),
    "blender": (3, 6, 0),
    "location": "File > Export > TACTIX Asset Bridge (.glb)",
    "description": "Exports Blender content as GLB plus TACTIX metadata for the universal asset pipeline",
    "category": "Import-Export",
}

import argparse
import datetime
import json
import os
import re
import sys

import bpy
from bpy_extras.io_utils import ExportHelper
from bpy.props import StringProperty


def _metadata_for_object(obj):
    custom = {}
    for key in obj.keys():
        if key.startswith("tactix."):
            value = obj[key]
            if isinstance(value, (str, int, float, bool)):
                custom[key] = value

    return {
        "name": obj.name,
        "type": obj.type,
        "parent": obj.parent.name if obj.parent else None,
        "custom": custom,
    }


def _safe_asset_name(name):
    name = re.sub(r"[^A-Za-z0-9_. -]+", "_", name).strip(" .")
    return name or "Scene"


def _default_asset_name(selected_only):
    objects = list(bpy.context.selected_objects) if selected_only else []
    if selected_only and len(objects) == 1:
        return _safe_asset_name(objects[0].name)

    blend_path = bpy.data.filepath or ""
    if blend_path:
        return _safe_asset_name(os.path.splitext(os.path.basename(blend_path))[0])

    if selected_only and objects:
        return _safe_asset_name(objects[0].name)

    return "Scene"


def export_tactix_glb(output_path, selected_only=True, source_asset_path=None):
    output_path = os.path.abspath(output_path)
    os.makedirs(os.path.dirname(output_path), exist_ok=True)

    objects = list(bpy.context.selected_objects) if selected_only else list(bpy.context.scene.objects)
    if selected_only and not objects:
        raise RuntimeError("No Blender objects are selected for TACTIX export.")

    bpy.ops.export_scene.gltf(
        filepath=output_path,
        export_format='GLB',
        use_selection=selected_only,
        export_apply=True,
        export_yup=True,
        export_materials='EXPORT',
        export_texcoords=True,
        export_normals=True,
        export_tangents=True,
        export_animations=True,
        export_skins=True,
        export_morph=True,
    )

    source_blend = bpy.data.filepath or ""
    source_asset = source_asset_path or os.environ.get("TACTIX_SOURCE_ASSET", "")
    if source_asset:
        source_asset = os.path.abspath(bpy.path.abspath(source_asset))

    metadata = {
        "schema": "tactix.blender.bridge",
        "version": 2,
        "sourceBlend": source_blend,
        "sourceAsset": source_asset,
        "authoritativeSource": source_blend or source_asset,
        "sourceScene": bpy.context.scene.name if bpy.context.scene else "",
        "selectionOnly": selected_only,
        "exportedAtUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "glb": os.path.basename(output_path),
        "objects": [_metadata_for_object(obj) for obj in objects],
    }

    sidecar = os.path.splitext(output_path)[0] + ".tactiximport.json"
    with open(sidecar, "w", encoding="utf-8") as handle:
        json.dump(metadata, handle, indent=2)

    return output_path, sidecar


class ExportTACTIX(bpy.types.Operator, ExportHelper):
    bl_idname = "export_scene.tactix_glb"
    bl_label = "Send Selection to TACTIX"
    filename_ext = ""

    tactix_root: StringProperty(
        name="TACTIX Project Root",
        description="Path to the TACTIX project (contains Imports/)",
        subtype='DIR_PATH',
        default=os.environ.get("TACTIX_PROJECT_ROOT", "")
    )

    def execute(self, context):
        root = bpy.path.abspath(self.tactix_root)
        if not root or not os.path.isdir(root):
            self.report({'ERROR'}, "Invalid TACTIX project root")
            return {'CANCELLED'}

        imports_dir = os.path.join(root, "Imports")
        asset_name = _default_asset_name(selected_only=True)
        output = os.path.join(imports_dir, f"{asset_name}.glb")

        try:
            glb, sidecar = export_tactix_glb(output, selected_only=True)
        except Exception as exc:
            self.report({'ERROR'}, f"TACTIX export failed: {exc}")
            return {'CANCELLED'}

        self.report({'INFO'}, f"TACTIX GLB: {glb} | metadata: {sidecar}")
        return {'FINISHED'}


def menu_func_export(self, context):
    self.layout.operator(ExportTACTIX.bl_idname, text="Send Selection to TACTIX (.glb)")


def register():
    bpy.utils.register_class(ExportTACTIX)
    bpy.types.TOPBAR_MT_file_export.append(menu_func_export)


def unregister():
    bpy.types.TOPBAR_MT_file_export.remove(menu_func_export)
    bpy.utils.unregister_class(ExportTACTIX)


def _run_headless_bridge(argv):
    parser = argparse.ArgumentParser(description="TACTIX Blender source converter")
    parser.add_argument("--output", required=True)
    parser.add_argument("--all", action="store_true", help="Export the whole Blender scene instead of selection")
    parser.add_argument("--source", default="", help="Original source asset when Blender is acting as a converter for an imported file")
    args = parser.parse_args(argv)
    glb, sidecar = export_tactix_glb(args.output, selected_only=not args.all, source_asset_path=args.source)
    print(f"TACTIX_GLTF_OUTPUT={glb}")
    print(f"TACTIX_METADATA_OUTPUT={sidecar}")


if __name__ == "__main__":
    # Blender passes script arguments after a standalone --. When present we are
    # running as TACTIX's headless source converter; otherwise register as an addon.
    if "--" in sys.argv:
        _run_headless_bridge(sys.argv[sys.argv.index("--") + 1:])
    else:
        register()
