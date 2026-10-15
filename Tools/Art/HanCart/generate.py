"""Blender 制作架空运输遗迹的破损木车；保留独立部件源工程，导出合并的游戏网格。
运行：blender --background --python-exit-code 1 --python Tools/Art/HanCart/generate.py
仅输出 HanCart 源工程及 HanStory/Models/HanBrokenCart.fbx、Textures/CartWood.png。
"""
import bpy
import math
import json
import random
import numpy as np
from pathlib import Path
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[3]
SOURCE = Path(__file__).resolve().parent
ART = ROOT / 'Client/Assets/Game/Art/Environment/CivilisationExpedition/HanStory'
(ART/'Models').mkdir(parents=True, exist_ok=True)
(ART/'Textures').mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.scale_length = 1
rng = random.Random(91)

# 长向木纹采用周期函数生成，边缘可平铺；不是照片或史料复原纹理。
n = 512
y, x = np.mgrid[0:n, 0:n].astype(np.float32)/n
warp = .006*np.sin(x*math.tau*2)+.003*np.sin(x*math.tau*5+y*math.tau*2)
grain = np.sin((y+warp)*math.tau*63)
fine = np.sin((y+warp*.6)*math.tau*157+x*math.tau*3)
grooves = np.maximum(0, grain-.82)**2*2.8
shade = .84+.08*grain+.025*fine-grooves
rgba = np.ones((n,n,4), dtype=np.float32)
for i,base in enumerate((.65,.51,.34)): rgba[:,:,i]=base*shade
texture = bpy.data.images.new('CartWood',width=n,height=n,alpha=True)
texture.pixels.foreach_set(rgba.ravel());texture.filepath_raw=str(ART/'Textures/CartWood.png')
texture.file_format='PNG';texture.save();texture.pack()
materials = {}
palette = {
    'CartWeatheredWood': ((.73,.64,.51,1),True,0),
    'CartDarkWood': ((.43,.36,.27,1),True,0),
    'CartSplitWood': ((.64,.47,.28,1),False,0),
    'CartForgedIron': ((.14,.16,.15,1),False,.65),
    'CartRope': ((.42,.33,.20,1),False,0),
    'CartCracks': ((.16,.11,.065,1),False,0),
}
for name,(color,textured,metallic) in palette.items():
    material=bpy.data.materials.new(name);material.diffuse_color=color;material.use_nodes=True
    material.node_tree.nodes.clear();bsdf=material.node_tree.nodes.new('ShaderNodeBsdfPrincipled');output=material.node_tree.nodes.new('ShaderNodeOutputMaterial');material.node_tree.links.new(bsdf.outputs['BSDF'],output.inputs['Surface']);bsdf.inputs['Base Color'].default_value=color
    bsdf.inputs['Roughness'].default_value=.86 if metallic==0 else .68
    bsdf.inputs['Metallic'].default_value=metallic
    if textured:
        node=material.node_tree.nodes.new('ShaderNodeTexImage');node.image=texture
        mix=material.node_tree.nodes.new('ShaderNodeMixRGB');mix.blend_type='MULTIPLY';mix.inputs[0].default_value=1;mix.inputs[2].default_value=color
        material.node_tree.links.new(node.outputs['Color'],mix.inputs[1]);material.node_tree.links.new(mix.outputs[0],bsdf.inputs['Base Color'])
    materials[name]=material

parts=[]
def mesh(name,verts,faces,mat):
    data=bpy.data.meshes.new(name);data.from_pydata(verts,[],faces);data.update()
    obj=bpy.data.objects.new(name,data);bpy.context.collection.objects.link(obj);data.materials.append(materials[mat]);parts.append(obj)
    return obj

def finish(obj,bevel=.004):
    if bevel:
        mod=obj.modifiers.new('Worn edge bevel','BEVEL');mod.width=bevel;mod.segments=2
        obj.modifiers.new('Weighted wood normals','WEIGHTED_NORMAL')
    return obj

def beam(name,a,b,width,depth,mat='CartWeatheredWood',bevel=.006):
    a,b=Vector(a),Vector(b);length=(b-a).length
    w,d=width/2,depth/2
    verts=[(xx,yy,zz) for zz in (-length/2,length/2) for yy in (-d,d) for xx in (-w,w)]
    faces=[(0,2,3,1),(4,5,7,6),(0,1,5,4),(2,6,7,3),(0,4,6,2),(1,3,7,5)]
    obj=mesh(name,verts,faces,mat);obj.location=(a+b)/2;obj.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
    uv=obj.data.uv_layers.new(name='WoodAlongLength');offset=rng.random()
    for face in obj.data.polygons:
        for loop in face.loop_indices:
            co=obj.data.vertices[obj.data.loops[loop].vertex_index].co
            transverse=co.x if abs(face.normal.y)>.5 else co.y
            uv.data[loop].uv=(co.z/1.7+offset,transverse/.22+offset)
    return finish(obj,bevel)

def cylinder(name,a,b,radius,mat='CartDarkWood',sides=12):
    a,b=Vector(a),Vector(b)
    bpy.ops.mesh.primitive_cylinder_add(vertices=sides,radius=radius,depth=(b-a).length,location=(a+b)/2)
    obj=bpy.context.object;obj.name=name;obj.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler();obj.data.materials.append(materials[mat]);parts.append(obj)
    return finish(obj,.002)

def rope(name,points,radius=.012):
    curve=bpy.data.curves.new(name,'CURVE');curve.dimensions='3D';curve.bevel_depth=radius;curve.bevel_resolution=1
    spline=curve.splines.new('POLY');spline.points.add(len(points)-1)
    for point,co in zip(spline.points,points):point.co=(*co,1)
    obj=bpy.data.objects.new(name,curve);bpy.context.collection.objects.link(obj);curve.materials.append(materials['CartRope']);parts.append(obj);return obj

def splinter(name,a,b,width=.045):
    a,b=Vector(a),Vector(b);length=(b-a).length
    obj=mesh(name,[(-width,0,0),(width,0,0),(width*.65,width,0),(-width*.7,width,0),(.006,width*.3,length)],[(0,1,2,3),(0,4,1),(1,4,2),(2,4,3),(3,4,0)],'CartSplitWood')
    obj.location=a;obj.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler();return obj

def wheel(name,x,y,z,broken=False):
    start=len(parts);radius=.65
    for segment in range(12):
        # 单块轮辋的两端留细接缝，损坏轮缺一块并露出连接断口。
        if broken and segment==3:continue
        lo=segment*math.tau/12+.006;hi=(segment+1)*math.tau/12-.006
        verts=[]
        for xx in (-.067,.067):
            for rr in (.55,radius):
                for j in range(4):
                    angle=lo+(hi-lo)*j/3;verts.append((x+xx,y+math.cos(angle)*rr,z+math.sin(angle)*rr))
        faces=[]
        for j in range(3):faces.extend([(j,j+1,j+5,j+4),(j+8,j+12,j+13,j+9),(j,j+8,j+9,j+1),(j+4,j+5,j+13,j+12)])
        faces.extend([(0,4,12,8),(3,11,15,7)])
        finish(mesh(name+'_Felloe_%02d'%segment,verts,faces,'CartDarkWood' if segment%3==0 else 'CartWeatheredWood'),.003)
        # 轮辋小连接销，靠形体与阴影表现连接，不加夸张现代螺栓。
        angle=(lo+hi)/2
        cylinder(name+'_JoinPeg',(x-.08,y+math.cos(angle)*.605,z+math.sin(angle)*.605),(x+.08,y+math.cos(angle)*.605,z+math.sin(angle)*.605),.016,'CartSplitWood',8)
    for j in range(12):
        angle=(j+.5)*math.tau/12
        if broken and j==3:
            splinter(name+'_BrokenSpoke',(x,y+.10*math.cos(angle),z+.10*math.sin(angle)),(x,y+.39*math.cos(angle),z+.39*math.sin(angle)),.026)
        else:
            beam(name+'_Spoke_%02d'%j,(x,y+.12*math.cos(angle),z+.12*math.sin(angle)),(x,y+.575*math.cos(angle),z+.575*math.sin(angle)),.052,.066,bevel=.003)
    cylinder(name+'_Hub',(x-.145,y,z),(x+.145,y,z),.125,'CartDarkWood',20)
    cylinder(name+'_AxleCap',(x-.16,y,z),(x-.18,y,z),.081,'CartForgedIron',16)
    # 两道细箍约束木毂，而非给整车包覆现代钢构。
    for xx in (x-.11,x+.1):
        for j in range(24):
            a=j*math.tau/24;b=(j+1)*math.tau/24
            beam(name+'_HubBand',(xx,y+math.cos(a)*.128,z+math.sin(a)*.128),(xx,y+math.cos(b)*.128,z+math.sin(b)*.128),.022,.012,'CartForgedIron',0)
    return parts[start:]

# 未损坏车体坐标：X 为车宽，Y 为纵向，Z 为高度；货台离地约 0.85 米。
for x in (-.46,.46):beam('ChassisRunner',(x,-.92,.74),(x,1,.74),.13,.16,'CartDarkWood')
for y in (-.68,.1,.78):beam('CrossBrace',(-.62,y,.74),(.62,y,.74),.13,.13,'CartDarkWood')
for i in range(7):
    x=-.51+i*.17
    if i==5:
        beam('SplitFloorRear',(x,-.86,.87),(x,-.05,.87),.162,.065)
        beam('SplitFloorFront',(x,.18,.85),(x,.91,.85),.162,.065)
        for k in range(4):splinter('FloorSplinter',(x-.055+k*.03,-.05,.88),(x-.05+k*.03,.04+k*.023,.872),.012)
    else:beam('FloorPlank_%02d'%i,(x,-.88,.86+rng.uniform(-.008,.008)),(x,.94,.86),.161,.07)
for side in (-1,1):
    for y in (-.83,-.05,.84):
        beam('SideStake',(side*.60,y,.8),(side*.60,y,1.43),.07,.075,'CartDarkWood')
        cylinder('StakePeg',(side*.55,y,.94),(side*.65,y,.94),.017,'CartSplitWood',8)
    for row in range(3):
        z=1.02+row*.145
        if side==1 and row>0:
            beam('BrokenSideBoardRear',(side*.60,-.82,z),(side*.60,-.24,z),.105,.045)
            for k in range(3):splinter('SideBoardSplinter',(side*.60,-.24,z-.035+k*.03),(side*.60,-.04+k*.065,z-.03+k*.027),.015)
        else:beam('SideBoard',(side*.60,-.83,z),(side*.60,.86,z),.105,.045)
for z in (1.02,1.17,1.32):beam('RearGate',(-.59,-.86,z),(.59,-.86,z),.11,.052)
for side in (-1,1):
    # 一根车辕保留，另一根在前段折断；横向束带及麻绳固定点可在近景读出。
    end=-2.38 if side<0 else -1.63
    beam('LongShaft' if side<0 else 'SnappedShaft',(side*.45,-.38,.71),(side*.39,end,.60),.085,.105,'CartDarkWood')
    if side>0:
        for k in range(5):splinter('ShaftEndSplinter',(side*.39-.03+k*.014,end,.6),(side*.39-.02+k*.012,end-.08-rng.random()*.12,.6+rng.uniform(-.03,.03)),.012)
    for y in (-.65,.68):
        for j in range(4):
            x=side*.60
            rope('FrameLashing',[(x-.055,y-.034+j*.018,1.06),(x+.055,y-.034+j*.018,1.06),(x+.055,y-.034+j*.018,1.18),(x-.055,y-.034+j*.018,1.18),(x-.055,y-.034+j*.018,1.06)],.009)
cylinder('WoodenAxle',(-.85,.12,.66),(.71,.12,.66),.079,'CartDarkWood',12)
for k in range(5):splinter('SnappedAxleEnd',(.71,.10+(k%2)*.035,.64+k*.014),(.79+rng.random()*.10,.1+(k%2)*.035,.64+k*.014),.013)
wheel('AttachedWheel',-.79,.12,.66)

# 车体右侧下沉；破损轮放在原轴旁而非让模型整体垂直悬起。
body=list(parts)
bpy.context.view_layer.update()  # 属性赋值后先求值，避免读取尚未更新的 matrix_world。
tilt=Matrix.Rotation(math.radians(19),4,'Y')
for obj in body:obj.matrix_world=tilt@obj.matrix_world
loose=wheel('BrokenWheel',.82,.23,.58,True)
bpy.context.view_layer.update()
center=Vector((.82,.23,.58));rot=Matrix.Translation(center)@Matrix.Rotation(math.radians(-24),4,'Y')@Matrix.Translation(-center)
for obj in loose:obj.matrix_world=rot@obj.matrix_world
beam('LooseSidePlank',(.87,-.75,.08),(1.02,.29,.07),.11,.042)
for k in range(4):splinter('LooseBoardSplinter',(1.02,.29,.08),(1.02+rng.uniform(-.04,.04),.37+k*.022,.075),.016)

# 应用造型修饰并合并导出副本；源工程仍保留每块车板与车轮部件。
bpy.ops.object.select_all(action='DESELECT')
for obj in parts:obj.select_set(True)
bpy.context.view_layer.objects.active=parts[0]
bpy.ops.object.convert(target='MESH')
parts=list(bpy.context.selected_objects)
for obj in parts:
    for mod in list(obj.modifiers):
        bpy.context.view_layer.objects.active=obj
        bpy.ops.object.modifier_apply(modifier=mod.name)
bpy.context.view_layer.update()
min_z=min((obj.matrix_world@Vector(c)).z for obj in parts for c in obj.bound_box)
for obj in parts:obj.location.z-=min_z
bpy.context.view_layer.update()
collection=bpy.data.collections.new('Editable broken cart parts');bpy.context.scene.collection.children.link(collection)
for prefix in ('AttachedWheel','BrokenWheel'):
    hub=next(o for o in parts if o.name==prefix+'_Hub')
    for band in (o for o in parts if o.name.startswith(prefix+'_HubBand')):
        assert (band.matrix_world.translation-hub.matrix_world.translation).length<.22, '轮毂箍件脱离木毂：'+band.name
for obj in parts:
    for parent in list(obj.users_collection):parent.objects.unlink(obj)
    collection.objects.link(obj)
bpy.ops.object.select_all(action='DESELECT')
copies=[]
for obj in parts:
    duplicate=obj.copy();duplicate.data=obj.data.copy();bpy.context.scene.collection.objects.link(duplicate);duplicate.select_set(True);copies.append(duplicate)
bpy.context.view_layer.objects.active=copies[0];bpy.ops.object.join();export=bpy.context.object;export.name='HanBrokenCart'
bpy.context.scene.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR');bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
# 同名材质槽合并，控制为五个子网格。
original=list(export.data.materials);unique=[];indices={}
for material in original:
    if material.name not in indices:indices[material.name]=len(unique);unique.append(material)
mapping=[indices[material.name] for material in original]
face_materials=[mapping[p.material_index] for p in export.data.polygons]
export.data.materials.clear()
for material in unique:export.data.materials.append(material)
for polygon,index in zip(export.data.polygons,face_materials):polygon.material_index=index
export.data.calc_loop_triangles()
report={'triangles':len(export.data.loop_triangles),'vertices':len(export.data.vertices),'materials':[m.name for m in unique],'editable_parts':len(parts),'size_meters':list(export.dimensions)}
assert report['triangles']<40000,report
bpy.ops.export_scene.fbx(filepath=str(ART/'Models/HanBrokenCart.fbx'),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_anim=False,add_leaf_bones=False,path_mode='STRIP')
bpy.data.objects.remove(export,do_unlink=True)
(SOURCE/'model_report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')

# 源文件的柔和工作室预览仅用于检查造型，最终效果以同机位 Unity 预览为准。
scene=bpy.context.scene;scene.world.color=(.15,.15,.15)
for name,location,power,size in [('Key',(3,-4,6),700,4),('Fill',(-4,-1,3),450,4),('Rim',(1,4,5),650,3)]:
    data=bpy.data.lights.new(name,'AREA');data.energy=power;data.shape='DISK';data.size=size
    light=bpy.data.objects.new(name,data);scene.collection.objects.link(light);light.location=location;light.rotation_euler=(Vector((0,-.4,.6))-light.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(4,-5,3.2));camera=bpy.context.object;camera.rotation_euler=(Vector((0,-.5,.7))-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=4.4;scene.camera=camera
scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True
scene.render.resolution_x=1100;scene.render.resolution_y=900;scene.render.resolution_percentage=100;scene.render.film_transparent=True
scene.view_settings.view_transform='AgX';scene.render.image_settings.file_format='PNG'
bpy.context.preferences.filepaths.save_version=0
bpy.data.orphans_purge(do_recursive=True)
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'HanBrokenCart.blend'))
scene.render.filepath=str(SOURCE/'preview.png');bpy.ops.render.render(write_still=True)
print('HAN_CART_COMPLETE '+json.dumps(report))
