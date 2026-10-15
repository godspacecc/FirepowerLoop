"""街机支援无人机美术源：米制、独立旋翼，不包含玩法或 Unity 接入逻辑。"""
import bpy
import math
import json
from pathlib import Path
from mathutils import Vector

OUT = Path(__file__).resolve().parent
EXPORT = OUT.parents[2] / 'Client/Assets/Game/Art/Arcade/SupportDrone/Models'
# 不继承用户启动文件中的笔刷、链接库或摄影棚，保证美术源可独立搬迁。
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1

def material(name, color, metallic=0.0):
    """创建纯色材质，导出不依赖外部贴图。"""
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    bs = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bs.inputs['Base Color'].default_value = (*color, 1)
    bs.inputs['Roughness'].default_value = 0.48
    bs.inputs['Metallic'].default_value = metallic
    return m

white = material('Drone_Shell', (0.72, 0.79, 0.83), 0.15)
dark = material('Drone_Graphite', (0.025, 0.040, 0.060), 0.25)
yellow = material('Drone_Yellow', (1.0, 0.58, 0.025))
blue = material('Drone_Lens', (0.025, 0.48, 0.75), 0.4)
root = bpy.data.objects.new('SupportDrone', None)
scene.collection.objects.link(root)
static = []

def finish(obj, name, mat, parent=root, body=True):
    obj.name = name
    obj.data.materials.append(mat)
    obj.parent = parent
    if body:
        static.append(obj)
    return obj

def box(name, loc, size, mat, bevel=0.015):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    obj = bpy.context.object
    obj.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        mod = obj.modifiers.new('EdgeChamfer', 'BEVEL')
        mod.width = bevel
        mod.segments = 1
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return finish(obj, name, mat)

def cylinder(name, loc, radius, depth, mat, vertices=12):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=loc)
    return finish(bpy.context.object, name, mat)

def bar(name, start, end, width, height, mat):
    """沿端点生成机臂；长度由米制坐标计算。"""
    a, b = Vector(start), Vector(end)
    o = box(name, (a+b)/2, (width, height, (b-a).length), mat, 0.012)
    o.rotation_euler = (b-a).to_track_quat('Z', 'Y').to_euler()
    return o

# Blender 前方为 -Y；按 FBX 标准轴导出后，Unity 接入时仍需核对镜头朝向。
box('LowerChassis', (0,0,0), (0.34,0.48,0.13), dark, 0.035)
box('ArmouredShell', (0,-0.015,0.10), (0.40,0.56,0.16), white, 0.055)
box('Battery', (0,0.045,0.194), (0.20,0.29,0.048), dark, 0.018)
box('BatteryStripe', (0,0.045,0.221), (0.075,0.27,0.007), yellow, 0.002)
for x in (-0.13, 0.13):
    box('NoseMark', (x,-0.20,0.18), (0.058,0.08,0.012), yellow, 0.004)
    for y in (0.11,0.15,0.19):
        box('CoolingSlot', (x,y,0.184), (0.048,0.016,0.009), dark, 0.002)
box('CameraGimbal', (0,-0.235,-0.084), (0.17,0.12,0.10), dark, 0.018)
lens = cylinder('FrontLens', (0,-0.305,-0.078), 0.046, 0.025, blue)
lens.rotation_euler.x = math.pi/2
for x in (-0.16,0.16):
    bar('LandingStrut', (x,0.08,-0.045), (x,0.12,-0.18), 0.028,0.028,dark)
    box('LandingSkid', (x,0,-0.187), (0.045,0.38,0.028), dark, 0.01)

rotors = []
for x,y,label in [(-0.46,-0.41,'FL'),(0.46,-0.41,'FR'),(-0.46,0.41,'RL'),(0.46,0.41,'RR')]:
    bar('Arm_'+label, (x*0.22,y*0.25,0.025), (x,y,0.025),0.085,0.068,dark)
    bar('ArmAccent_'+label, (x*0.55,y*0.55,0.065), (x*0.88,y*0.88,0.065),0.035,0.015,yellow)
    cylinder('Motor_'+label, (x,y,0.055),0.059,0.09,dark)
    cylinder('MotorCollar_'+label, (x,y,0.10),0.064,0.022,yellow)
    bpy.ops.mesh.primitive_torus_add(major_segments=24,minor_segments=4,location=(x,y,0.09),major_radius=0.235,minor_radius=0.010)
    finish(bpy.context.object,'Guard_'+label,dark)
    for angle in (0,math.pi/2,math.pi,3*math.pi/2):
        bar('GuardBrace', (x+0.06*math.cos(angle),y+0.06*math.sin(angle),0.048),
            (x+0.231*math.cos(angle),y+0.231*math.sin(angle),0.075),0.015,0.012,dark)
    rotor = bpy.data.objects.new('Rotor_'+label,None)
    scene.collection.objects.link(rotor)
    rotor.parent = root
    rotor.location = (x,y,0.127)
    rotors.append(rotor)
    parts = []
    bpy.ops.mesh.primitive_cylinder_add(vertices=12,radius=0.038,depth=0.022)
    hub = finish(bpy.context.object,'RotorHub_'+label,dark,rotor,False)
    hub.location = (0,0,0)
    parts.append(hub)
    for n in range(3):
        angle = n*math.tau/3 + (0.2 if x*y>0 else -0.2)
        points = [(0.025,-0.012),(0.10,-0.027),(0.208,-0.018),(0.215,0.009),(0.11,0.028),(0.025,0.016)]
        verts = [(px*math.cos(angle)-py*math.sin(angle),px*math.sin(angle)+py*math.cos(angle),z) for z in (-0.004,0.004) for px,py in points]
        faces = [tuple(reversed(range(6))),tuple(range(6,12))]+[(i,(i+1)%6,(i+1)%6+6,i+6) for i in range(6)]
        mesh = bpy.data.meshes.new('BladeMesh')
        mesh.from_pydata(verts,[],faces)
        mesh.update()
        obj = bpy.data.objects.new('Blade',mesh)
        scene.collection.objects.link(obj)
        finish(obj,'Blade',dark,rotor,False)
        parts.append(obj)
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts: p.select_set(True)
    bpy.context.view_layer.objects.active = hub
    bpy.ops.object.join()
    hub.name = 'Propeller_'+label

# 静态部件合并；四个旋翼保持独立网格及中心轴，不添加自动播放动画。
bpy.ops.object.select_all(action='DESELECT')
for obj in static: obj.select_set(True)
bpy.context.view_layer.objects.active = static[0]
bpy.ops.object.join()
body = bpy.context.object
body.name = 'DroneBody'
scene.cursor.location = (0,0,0)
bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
meshes = [o for o in scene.objects if o.type=='MESH']
for obj in meshes:
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(island_margin=0.02)
    bpy.ops.object.mode_set(mode='OBJECT')

EXPORT.mkdir(parents=True,exist_ok=True)
bpy.ops.object.select_all(action='DESELECT')
for obj in [root,body]+rotors+[o for o in meshes if o!=body]: obj.select_set(True)
bpy.ops.export_scene.fbx(filepath=str(EXPORT/'ArcadeSupportDrone.fbx'),use_selection=True,object_types={'EMPTY','MESH'},
    apply_unit_scale=True,axis_forward='-Z',axis_up='Y',bake_anim=False,add_leaf_bones=False,path_mode='AUTO')

# 摄影棚仅存在于源文件，不导出到游戏模型。
world = bpy.data.worlds.new('StudioWorld')
scene.world = world
world.use_nodes = True
background = next(n for n in world.node_tree.nodes if n.type == 'BACKGROUND')
background.inputs[0].default_value = (0.09,0.12,0.18,1)
background.inputs[1].default_value = 0.5
for name,loc,power,size in [('Key',(2,-3,4),450,3),('Fill',(-3,-1,2),300,3),('Rim',(1,3,3),550,2)]:
    data = bpy.data.lights.new(name,'AREA')
    data.energy = power
    data.shape = 'DISK'
    data.size = size
    obj = bpy.data.objects.new(name,data)
    scene.collection.objects.link(obj)
    obj.location = loc
    obj.rotation_euler = (-obj.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(1.8,-2.5,1.8))
camera = bpy.context.object
camera.name = 'PreviewCamera'
camera.rotation_euler = (Vector((0,0,0.04))-camera.location).to_track_quat('-Z','Y').to_euler()
camera.data.type = 'ORTHO'
camera.data.ortho_scale = 2.05
scene.camera = camera
scene.render.engine = 'CYCLES'
scene.cycles.samples = 48
scene.cycles.use_denoising = True
scene.render.resolution_x = 1200
scene.render.resolution_y = 1000
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.render.film_transparent = False
scene.view_settings.view_transform = 'AgX'
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'ArcadeSupportDrone.blend'))
scene.render.filepath = str(OUT/'preview.png')
bpy.ops.render.render(write_still=True)
camera.location = (0,0,3)
camera.rotation_euler = (0,0,0)
camera.data.ortho_scale = 1.8
scene.render.filepath = str(OUT/'preview_top.png')
bpy.ops.render.render(write_still=True)
report = {'mesh_count':len(meshes),'rotors':[r.name for r in rotors],
    'triangles':sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in meshes),
    'vertices':sum(len(o.data.vertices) for o in meshes),'fbx':str(EXPORT/'ArcadeSupportDrone.fbx')}
(OUT/'model_report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('DRONE_REPORT '+json.dumps(report))
