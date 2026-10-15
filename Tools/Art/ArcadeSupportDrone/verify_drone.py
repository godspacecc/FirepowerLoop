"""独立回读导出 FBX，验证交付结构；不调用 Unity 或构建流程。"""
import bpy
import json
from pathlib import Path
from mathutils import Vector

folder = Path(__file__).resolve().parent
fbx = folder.parents[2] / 'Client/Assets/Game/Art/Arcade/SupportDrone/Models/ArcadeSupportDrone.fbx'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(fbx))
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
assert len(meshes) == 5, '应为机身加四个旋翼'
assert all(len(o.data.uv_layers) > 0 for o in meshes), '所有网格应具有 UV'
assert all(len(o.data.materials) > 0 for o in meshes), '所有网格应具有材质'
assert {o.name for o in meshes} == {'DroneBody','Propeller_FL','Propeller_FR','Propeller_RL','Propeller_RR'}
assert all(bpy.data.objects.get('Rotor_'+suffix) for suffix in ('FL','FR','RL','RR'))
points = [o.matrix_world @ Vector(v) for o in meshes for v in o.bound_box]
dimensions = [max(p[i] for p in points)-min(p[i] for p in points) for i in range(3)]
assert max(dimensions) < 1.6 and max(dimensions) > 1.2, '尺寸应为米制的小型无人机'
triangles = sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in meshes)
assert triangles <= 4000, '低多边形预算为 4000 个三角面'
assert not bpy.data.images, '导出模型不应依赖外部贴图'
result = {'fbx_roundtrip':'PASS','meshes':len(meshes),'triangles':triangles,'dimensions_m':dimensions,
          'rotor_centres':{o.name:list(o.matrix_world.translation) for o in meshes if o.name.startswith('Propeller_')}}
bpy.ops.wm.open_mainfile(filepath=str(folder/'ArcadeSupportDrone.blend'))
assert len(bpy.data.libraries) == 0, '源文件不得依赖本机链接资源库'
result['blend_external_libraries'] = len(bpy.data.libraries)
(folder/'verification.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print('DRONE_VERIFIED '+json.dumps(result))
