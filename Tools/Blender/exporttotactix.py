bl_info = {
    "name": "Export to TACTIX (OBJ into Imports)",
    "author": "TACTIX",
    "version": (1, 0, 0),
    "blender": (3, 6, 0),
    "location": "File > Export > TACTIX (.obj into Imports)",
    "description": "Exports selected objects as OBJ into a TACTIX project's Imports folder",
    "category": "Import-Export",
}

import bpy
from bpy_extras.io_utils import ExportHelper
from bpy.props import StringProperty
import os


class ExportTACTIX(bpy.types.Operator, ExportHelper):
    bl_idname = "export_scene.tactix_obj"
    bl_label = "Export TACTIX OBJ"

    filename_ext = ""

    tactix_root: StringProperty(
        name="TACTIX Project Root",
        description="Path to the TACTIX folder (contains Imports/)",
        subtype='DIR_PATH'
    )

    def execute(self, context):
        root = bpy.path.abspath(self.tactix_root)
        if not root or not os.path.isdir(root):
            self.report({'ERROR'}, "Invalid TACTIX root directory")
            return {'CANCELLED'}

        imports_dir = os.path.join(root, "Imports")
        os.makedirs(imports_dir, exist_ok=True)

        blend_name = os.path.splitext(os.path.basename(bpy.data.filepath or "Scene"))[0]
        out_path = os.path.join(imports_dir, f"{blend_name}_export.obj")

        bpy.ops.wm.obj_export(
            filepath=out_path,
            export_selected_objects=True,
            forward_axis='-Z',
            up_axis='Y',
            global_scale=1.0,
            export_materials=False,
        )

        self.report({'INFO'}, f"Exported OBJ to {out_path}")
        return {'FINISHED'}


def menu_func_export(self, context):
    self.layout.operator(ExportTACTIX.bl_idname, text="TACTIX (.obj into Imports)")


def register():
    bpy.utils.register_class(ExportTACTIX)
    bpy.types.TOPBAR_MT_file_export.append(menu_func_export)


def unregister():
    bpy.types.TOPBAR_MT_file_export.remove(menu_func_export)
    bpy.utils.unregister_class(ExportTACTIX)


if __name__ == "__main__":
    register()
