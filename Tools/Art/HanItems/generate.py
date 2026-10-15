"""Blender 离线制作首章六件道具。米制单位，握点为原点，导出 Unity FBX。
仅修改本脚本拥有的源工程和导出文件，不读取或改写第三方资源。
运行：blender --background --python Tools/Art/HanItems/generate.py
"""
import bpy
import math
import random
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'Client/Assets/Game/Art/Civilisation/HanItems/Models'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.scale_length = 1
MATS = {}
for name, color, metallic in [
    ('HanIron', (.15,.18,.20,1), .65), ('HanEdge', (.57,.64,.67,1), .8),
    ('HanWood', (.27,.12,.052,1), 0), ('HanWrap', (.16,.075,.032,1), 0),
    ('HanCord', (.65,.48,.27,1), 0), ('HanClay', (.25,.29,.27,1), 0),
    ('HanCloth', (.48,.35,.19,1), 0), ('HanGrain', (.83,.66,.35,1), 0)]:
    m = bpy.data.materials.new(name); m.diffuse_color = color; m.use_nodes = True
    m.node_tree.nodes.clear()
    bsdf = m.node_tree.nodes.new('ShaderNodeBsdfPrincipled')
    output = m.node_tree.nodes.new('ShaderNodeOutputMaterial')
    m.node_tree.links.new(bsdf.outputs['BSDF'], output.inputs['Surface'])
    bsdf.inputs['Base Color'].default_value = color
    bsdf.inputs['Metallic'].default_value = metallic
    bsdf.inputs['Roughness'].default_value = .38 if metallic else .8
    MATS[name] = m

def mesh(name, verts, faces, mat):
    data = bpy.data.meshes.new(name); data.from_pydata(verts, [], faces); data.update()
    obj = bpy.data.objects.new(name, data); bpy.context.collection.objects.link(obj)
    obj.data.materials.append(MATS[mat]); return obj

def lathe(name, profile, mat, sides=16, wobble=0):
    verts=[]; faces=[]
    for j,(z,r) in enumerate(profile):
        for i in range(sides):
            a=i*math.tau/sides; rr=r*(1+wobble*math.cos(a*5+j*.6))
            verts.append((math.cos(a)*rr,math.sin(a)*rr,z))
    for j in range(len(profile)-1):
        for i in range(sides):
            n=j*sides+i; k=j*sides+(i+1)%sides
            faces.append((n,k,k+sides,n+sides))
    faces += [tuple(reversed(range(sides))),tuple((len(profile)-1)*sides+i for i in range(sides))]
    return mesh(name,verts,faces,mat)

def tube(name, points, radius, mat, sides=8):
    curve=bpy.data.curves.new(name,'CURVE');curve.dimensions='3D';curve.bevel_depth=radius;curve.bevel_resolution=1;curve.resolution_u=1
    s=curve.splines.new('POLY');s.points.add(len(points)-1)
    for p,v in zip(s.points,points):p.co=(*v,1)
    obj=bpy.data.objects.new(name,curve);bpy.context.collection.objects.link(obj);obj.data.materials.append(MATS[mat]);return obj

def prism(name, outline, thickness, mat):
    n=len(outline);v=[(x,y,z) for y in [-thickness/2,thickness/2] for x,z in outline]
    f=[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    obj=mesh(name,v,f,mat)
    mod=obj.modifiers.new('Small forged bevel','BEVEL');mod.width=.003;mod.segments=1
    obj.modifiers.new('Weighted normals','WEIGHTED_NORMAL');return obj

def wraps(z0,z1,radius,mat='HanWrap',count=9):
    for j in range(count):
        z=z0+(z1-z0)*j/max(1,count-1)
        tube('Grip winding',[(math.cos(i*math.tau/16)*radius,math.sin(i*math.tau/16)*radius,z+.004*i/16) for i in range(17)],.004,mat)

def handle(length=.75):
    lathe('Shaped hardwood haft',[(-.17,.018),(-.13,.021),(.1,.018),(length-.25,.023),(length-.17,.019)],'HanWood',12)
    wraps(-.12,.08,.021)

def axe():
    handle(.78)
    prism('Forged axe eye and cheek',[(-.055,.39),(.09,.385),(.225,.33),(.24,.50),(.085,.55),(-.055,.535)],.062,'HanIron')
    prism('Honed axe edge',[(.225,.33),(.252,.335),(.265,.485),(.24,.50)],.018,'HanEdge')
    wraps(.35,.39,.024,'HanCord',3)

def pick():
    handle(.86)
    prism('Forged pointed pick',[(-.32,.40),(-.24,.48),(-.075,.56),(.055,.56),(.22,.48),(.30,.43),(.25,.435),(.055,.49),(-.07,.49),(-.22,.445)],.044,'HanIron')
    prism('Polished working point',[(-.32,.40),(-.24,.48),(-.22,.445)],.017,'HanEdge')
    wraps(.42,.48,.024,'HanCord',4)

def blade():
    lathe('Wrapped knife grip',[(-.14,.021),(.04,.022),(.07,.019)],'HanWood',12);wraps(-.12,.035,.022)
    prism('Straight blade',[(-.025,.06),(.027,.06),(.029,.72),(.005,.79),(-.025,.74)],.013,'HanIron')
    prism('Single cutting edge',[(.027,.06),(.037,.06),(.038,.71),(.005,.79),(.029,.72)],.004,'HanEdge')
    lathe('Grip ferrule',[(.043,.028),(.065,.028)],'HanIron',12)
    tube('Ring pommel',[(.035*math.cos(i*math.tau/24),0,-.178+.035*math.sin(i*math.tau/24)) for i in range(25)],.006,'HanIron')

def bow():
    pts=[(.17,0,-.61),(.21,0,-.54),(.14,0,-.42),(.055,0,-.25),(0,0,-.09),(0,0,.09),(.055,0,.25),(.14,0,.42),(.21,0,.54),(.17,0,.61)]
    tube('Curved wood limbs',pts,.018,'HanWood')
    tube('Backing strip',[(x-.015,y,z) for x,y,z in pts],.007,'HanCord')
    tube('Bowstring',[(.17,0,-.61),(.17,0,0),(.17,0,.61)],.005,'HanCord')
    wraps(-.09,.09,.019,'HanWrap',12)
    for sign in [-1,1]:
        tube('Tip lashing',[(.17,-.016,sign*.605),(.18,.016,sign*.595),(.16,-.016,sign*.58)],.004,'HanWrap')

def grain():
    lathe('Cloth ration pouch',[(0,.08),(.035,.13),(.12,.145),(.22,.12),(.26,.065),(.29,.08),(.305,.075)],'HanCloth',20,.08)
    wraps(.25,.268,.067,'HanCord',3)
    tube('Tie ends',[(.065,0,.26),(.11,-.02,.23),(.12,-.025,.16)],.006,'HanCord')
    tube('Cloth seam',[(.01,-r-.002,z) for z,r in [(.02,.11),(.08,.14),(.16,.137),(.23,.1)]],.002,'HanCord')
    rng=random.Random(32)
    for i in range(24):
        a=rng.random()*math.tau;r=math.sqrt(rng.random())*.063
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1,radius=1,location=(r*math.cos(a),r*math.sin(a),.301+rng.random()*.009))
        obj=bpy.context.object;obj.name='Prepared grain';obj.scale=(.011,.005,.005);obj.rotation_euler.z=a;obj.data.materials.append(MATS['HanGrain'])

def pot():
    lathe('Grey pottery body',[(0,.08),(.025,.105),(.09,.145),(.20,.16),(.28,.13),(.33,.065),(.37,.058),(.385,.067)],'HanClay',24)
    lathe('Sealing lid',[(.38,.07),(.395,.075),(.407,.055),(.415,.02)],'HanClay',24)
    wraps(.344,.365,.061,'HanCord',3)
    for sign in [-1,1]:
        tube('Small shoulder lug',[(sign*.12,0,.285),(sign*.157,0,.31),(sign*.16,0,.26),(sign*.143,0,.235)],.011,'HanClay')
    tube('Lid securing cord',[(0,-.07,.385),(0,0,.42),(0,.07,.385)],.004,'HanCord')

for name,builder in [('HanForgedAxe',axe),('HanForgedPick',pick),('HanSteelBlade',blade),('HanBow',bow),('HanGrainPouch',grain),('HanWaterPot',pot)]:
    before=set(bpy.data.objects);builder();objects=list(set(bpy.data.objects)-before)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:o.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    bpy.ops.object.convert(target='MESH');bpy.ops.object.join();obj=bpy.context.object;obj.name=name
    bpy.context.scene.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_anim=False,add_leaf_bones=False)
    obj.hide_set(True)
bpy.data.orphans_purge(do_recursive=True)
# 源工程按列展开，打开即可选取编辑；FBX 已按原点握柄导出，不受展示位置影响。
for index, obj in enumerate(sorted(bpy.data.objects, key=lambda item: item.name)):
    obj.hide_set(False)
    obj.location.x += index * 1.5
bpy.ops.object.select_all(action='SELECT')
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(Path(__file__).with_name('HanItems.blend')))
print('HAN_ITEMS_EXPORT_COMPLETE',str(OUT))
