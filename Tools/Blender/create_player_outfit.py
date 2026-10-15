"""派生当前 City 主角的穿戴样例；保留原始文件，输出可编辑骨骼与独立衣裤。"""
import bpy, bmesh, os, json
from mathutils import Vector
ROOT=os.path.abspath(os.path.join(os.path.dirname(__file__),'../..'))
OUT=os.path.join(ROOT,'Client/Assets/Game/Art/Player/OutfitPrototype')
os.makedirs(OUT,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.join(ROOT,'Client/Assets/Game/Art/Player/Humanoid/PlayerCityCharacters.fbx'))
source=bpy.data.objects['Character_Male_Hoodie']; rig=next(o for o in bpy.data.objects if o.type=='ARMATURE')
for ob in list(bpy.data.objects):
    if ob not in (source,rig): bpy.data.objects.remove(ob,do_unlink=True)
tex=bpy.data.images.load(os.path.join(ROOT,'Client/Assets/Game/Art/Player/Humanoid/PlayerCityTexture.png'))
pixels=list(tex.pixels); w,h=tex.size
def color(uv):
    i=(min(h-1,int(uv.y*h))*w+min(w-1,int(uv.x*w)))*4
    return tuple(round(c,2) for c in pixels[i:i+3])
def category(p):
    r,g,b=color(source.data.uv_layers.active.data[p.loop_start].uv)
    if abs(r-.24)<.02 and abs(g-.36)<.02: return 'Pants'
    if (r>.6 and g<.3) or abs(r-.73)<.01 or abs(r-.57)<.01 or abs(r-.46)<.01 or abs(r-.27)<.01: return 'Top'
    return 'Base'
face_groups={p.index:category(p) for p in source.data.polygons}
mat=bpy.data.materials.new('PlayerCityPalette'); mat.use_nodes=True
node=mat.node_tree.nodes.new('ShaderNodeTexImage'); node.image=tex
mat.node_tree.links.new(node.outputs['Color'],mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'])
def part(name,kind):
    ob=source.copy(); ob.data=source.data.copy(); ob.name=name; bpy.context.collection.objects.link(ob)
    bm=bmesh.new(); bm.from_mesh(ob.data); bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm,geom=[f for f in bm.faces if face_groups[f.index]!=kind],context='FACES')
    bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS')
    bm.to_mesh(ob.data); bm.free(); ob.data.materials.clear(); ob.data.materials.append(mat)
    return ob
base=part('Body_HeadHandsShoes','Base'); top=part('Top_RedHoodie','Top'); pants=part('Pants_BlueDenim','Pants')
bpy.data.objects.remove(source,do_unlink=True)
def copy_part(ob,name):
    new=ob.copy(); new.data=ob.data.copy(); new.name=name; bpy.context.collection.objects.link(new); return new
field=copy_part(top,'Top_OliveUtility'); cargo=copy_part(pants,'Pants_TanCargo')
# 直接复用原色板；不引入与原包不一致的写实贴图。
def palette_uv(target):
    best=None
    for y in range(2,h,4):
        for x in range(2,w,4):
            i=(y*w+x)*4; d=sum((pixels[i+k]-target[k])**2 for k in range(3))
            if best is None or d<best[0]: best=(d,((x+.5)/w,(y+.5)/h))
    return best[1]
olive=palette_uv((.36,.39,.22)); tan=palette_uv((.48,.37,.25)); dark=palette_uv((.23,.25,.19))
for loop in field.data.uv_layers.active.data:
    r,g,b=color(loop.uv)
    if r>.6 and g<.3: loop.uv=olive
for loop in cargo.data.uv_layers.active.data: loop.uv=tan
# 轮廓变化避开腰线、手腕、脚口，两个裤装都与相同上衣边界吻合。
for v in field.data.vertices:
    if abs(v.co.x)<22 and 98<v.co.y<134: v.co.x*=1.07; v.co.z*=1.06
for v in cargo.data.vertices:
    if 20<v.co.y<79:
        center=11 if v.co.x>0 else -11
        v.co.x=center+(v.co.x-center)*1.12; v.co.z*=1.09
def pocket(ob,name,center,size,uv):
    """新增低面数口袋，按邻近衣物顶点转移权重，与衣物合并为同一部位。"""
    bpy.ops.mesh.primitive_cube_add(size=1)
    box=bpy.context.object; box.name=name; box.matrix_world=ob.matrix_world.copy()
    for v in box.data.vertices: v.co=Vector(center)+Vector((v.co.x*size[0],v.co.y*size[1],v.co.z*size[2]))
    box.data.materials.append(mat)
    box.data.uv_layers.active.name=ob.data.uv_layers.active.name
    for loop in box.data.uv_layers.active.data: loop.uv=uv
    for g in ob.vertex_groups: box.vertex_groups.new(name=g.name)
    for v in box.data.vertices:
        nearest=min(ob.data.vertices,key=lambda p:(p.co-v.co).length_squared)
        for weight in nearest.groups: box.vertex_groups[weight.group].add([v.index],weight.weight,'REPLACE')
    bpy.ops.object.select_all(action='DESELECT'); box.select_set(True); ob.select_set(True); bpy.context.view_layer.objects.active=ob; bpy.ops.object.join()
for side in (-1,1):
    pocket(field,'ChestPocket',(side*10,125,10.6),(8,10,2.2),olive)
    pocket(field,'PocketFlap',(side*10,129.4,12),(8.8,2.2,1.3),dark)
    pocket(cargo,'ThighPocket',(side*18,65,0),(4,13,11),tan)
    pocket(cargo,'ThighFlap',(side*18.5,70.5,0),(4.4,2.5,11.8),dark)
inner_top=copy_part(top,'Base_Torso'); inner_pants=copy_part(pants,'Base_Legs')
# 基础长内衣保留封闭的腰、腕、踝边界；压平帽领并统一亚麻色，不代表库存物品。
linen=palette_uv((.65,.62,.52)); charcoal=palette_uv((.31,.31,.30))
for loop in inner_top.data.uv_layers.active.data: loop.uv=linen
for v in inner_top.data.vertices:
    if v.co.y>143:
        v.co.y=143+(v.co.y-143)*.08
        v.co.z=max(-5,min(5,v.co.z)); v.co.x=max(-10,min(10,v.co.x))
for loop in inner_pants.data.uv_layers.active.data: loop.uv=charcoal
# 保留旧衣裤轮廓；将原固定身体拆成头、裸手和基础鞋，穿戴时只遮挡对应区域。
def split_base(name,predicate):
    ob=copy_part(base,name)
    bm=bmesh.new();bm.from_mesh(ob.data);bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm,geom=[f for f in bm.faces if not predicate(f.calc_center_median())],context='FACES')
    bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS')
    bm.to_mesh(ob.data);bm.free();return ob
head=split_base('Body_Head',lambda c:c.y>150 or (c.y>110 and abs(c.x)<30))
# 原主角头部的橄榄色兜帽独立保留；戴便帽时隐藏，脸、胡须、眼睛仍为同一网格。
head_cover=copy_part(head,'Base_HeadCover')
def keep_head_material(ob,is_cover):
    bm=bmesh.new();bm.from_mesh(ob.data);layer=bm.loops.layers.uv.active
    remove=[]
    for face in bm.faces:
        r,g,b=color(face.loops[0][layer].uv)
        cover=abs(r-.44)<.02 and abs(g-.45)<.02 and abs(b-.22)<.02
        if cover!=is_cover:remove.append(face)
    bmesh.ops.delete(bm,geom=remove,context='FACES')
    bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS');bm.to_mesh(ob.data);bm.free()
keep_head_material(head,False);keep_head_material(head_cover,True)
bare_hands=split_base('Base_Hands',lambda c:c.y>110 and abs(c.x)>30)
base_feet=split_base('Base_Feet',lambda c:c.y<30)
gloves=copy_part(bare_hands,'Hands_WorkGloves');boots=copy_part(base_feet,'Feet_BrownBoots')
leather=palette_uv((.24,.19,.14));sole=palette_uv((.10,.10,.10))
for loop in gloves.data.uv_layers.active.data:loop.uv=dark
for v in gloves.data.vertices:
    v.co.y=136+(v.co.y-136)*1.045;v.co.z=-3.5+(v.co.z+3.5)*1.045
for loop in boots.data.uv_layers.active.data:loop.uv=leather
for v in boots.data.vertices:
    v.co.z=-3+(v.co.z+3)*1.025
# 帽子和背包采用低面数切角轮廓，并直接绑定已存在的骨骼。
def rigid(name,verts,faces,bone,uv):
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    ob=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(ob)
    ob.matrix_world=base.matrix_world.copy();ob.parent=base.parent
    ob.data.materials.append(mat);layer=ob.data.uv_layers.new(name=base.data.uv_layers.active.name)
    for loop in layer.data:loop.uv=uv
    for group in base.vertex_groups:ob.vertex_groups.new(name=group.name)
    ob.vertex_groups[bone].add(list(range(len(verts))),1,'REPLACE')
    mod=ob.modifiers.new('Armature','ARMATURE');mod.object=rig
    return ob
def box(name,center,size,bone,uv):
    verts=[(center[0]+x*size[0]/2,center[1]+y*size[1]/2,center[2]+z*size[2]/2) for x,y,z in [(-1,-1,-1),(-1,-1,1),(-1,1,-1),(-1,1,1),(1,-1,-1),(1,-1,1),(1,1,-1),(1,1,1)]]
    return rigid(name,verts,[(0,4,6,2),(1,3,7,5),(0,1,5,4),(2,6,7,3),(0,2,3,1),(4,5,7,6)],bone,uv)
def merge(target,others):
    bpy.ops.object.select_all(action='DESELECT');target.select_set(True)
    for ob in others:ob.select_set(True)
    bpy.context.view_layer.objects.active=target;bpy.ops.object.join();return target
import math
verts=[]
for y,rx,rz in [(175.5,12.2,13),(181,11.9,12.5),(185.5,7.5,8),(187,0.1,0.1)]:
    for i in range(12):
        angle=i*math.tau/12;verts.append((math.cos(angle)*rx,y,math.sin(angle)*rz-1))
faces=[tuple(range(11,-1,-1))]
for ring in range(3):
    for i in range(12):faces.append((ring*12+i,ring*12+(i+1)%12,(ring+1)*12+(i+1)%12,(ring+1)*12+i))
cap=rigid('Head_OliveCap',verts,faces,'Head',olive)
merge(cap,[box('CapBrim',(0,175.5,14),(22,1.7,14),'Head',olive),box('CapBand',(0,177,11.3),(18,2,1.2),'Head',dark)])
pack=box('Back_CanvasPack',(0,122,-22),(27,34,14),'Spine_02',tan)
bevel=pack.modifiers.new('SoftCorners','BEVEL');bevel.width=2.5;bevel.segments=1
bpy.context.view_layer.objects.active=pack
bpy.ops.object.modifier_apply(modifier=bevel.name)
extras=[box('PackFrontPocket',(0,113,-30),(20,14,4),'Spine_02',olive),box('PackLid',(0,136,-23),(29,4,16),'Spine_02',dark)]
for side in [-1,1]:
    extras.extend([box('ShoulderStrap',(side*12.5,126,13.6),(3.3,28,2.2),'Spine_03',tan),box('StrapTop',(side*12.5,142,-2),(3.3,2.3,31),'Spine_03',tan),box('StrapBuckle',(side*12.5,115,15),(4.2,4.5,1.3),'Spine_02',dark)])
merge(pack,extras)
vest=copy_part(inner_top,'Armor_UtilityVest')
bm=bmesh.new();bm.from_mesh(vest.data)
bmesh.ops.delete(bm,geom=[f for f in bm.faces if abs(f.calc_center_median().x)>21 or not 108<f.calc_center_median().y<144],context='FACES')
bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS');bm.to_mesh(vest.data);bm.free()
for v in vest.data.vertices:v.co.x*=1.13;v.co.z*=1.26
for loop in vest.data.uv_layers.active.data:loop.uv=dark
for side in [-1,1]:
    pocket(vest,'UtilityPocket',(side*10,117,14),(8,12,3),olive)
    pocket(vest,'UtilityPocketFlap',(side*10,122,16),(8.8,2.2,1.3),tan)
parts=[head,head_cover,bare_hands,base_feet,top,pants,field,cargo,inner_top,inner_pants,cap,gloves,boots,pack,vest]
bpy.data.objects.remove(base,do_unlink=True)
bpy.ops.object.select_all(action='DESELECT'); rig.select_set(True)
for ob in parts: ob.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT,'PlayerModularOutfit.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True)
field.hide_render=True; cargo.hide_render=True; field.hide_set(True); cargo.hide_set(True)
tex.pack()
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT,'Tools/Blender/PlayerModularOutfit.blend'))
print('OUTFIT_CREATED='+json.dumps({o.name:{'vertices':len(o.data.vertices),'faces':len(o.data.polygons),'unweighted':sum(not v.groups for v in o.data.vertices)} for o in parts}))
