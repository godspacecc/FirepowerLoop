"""基地专用木屋，米制；保留可编辑 blend，导出项目自有 FBX，不依赖外部贴图。"""
import bpy, math, random, os, json, sys
from mathutils import Vector
random.seed(1011)
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '../..'))
OUT = os.path.join(ROOT, 'Client/Assets/Game/Art/Environment/BaseCamp/Lakeside')
# 单独迭代房屋时不重导远山、湖岸与调色板；暂存目录便于等待 Unity 测试空闲后再导入。
CABIN_ONLY = '--cabin-only' in sys.argv
if '--output' in sys.argv: OUT = os.path.abspath(sys.argv[sys.argv.index('--output')+1])
os.makedirs(OUT, exist_ok=True)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
def mat(n, c):
    m=bpy.data.materials.new(n); m.diffuse_color=(*c,1); m.use_nodes=True
    m.node_tree.nodes.clear()
    bsdf=m.node_tree.nodes.new('ShaderNodeBsdfPrincipled')
    output=m.node_tree.nodes.new('ShaderNodeOutputMaterial')
    m.node_tree.links.new(bsdf.outputs[0],output.inputs[0])
    bsdf.inputs['Base Color'].default_value=(*c,1)
    bsdf.inputs['Roughness'].default_value=.8
    return m
wood=[mat('CabinWood'+str(i),c) for i,c in enumerate([(0.28,.16,.075),(.35,.205,.11),(.40,.25,.14),(.46,.29,.16)])]
trim=mat('CabinTimber',(.20,.115,.06)); cut=mat('CabinEndgrain',(.59,.40,.23))
roof=[mat('CabinSlate'+str(i),c) for i,c in enumerate([(.14,.19,.23),(.18,.23,.27),(.22,.27,.30),(.25,.29,.32)])]
stone=[mat('CabinStone'+str(i),c) for i,c in enumerate([(.35,.37,.36),(.42,.41,.37),(.48,.46,.40)])]
glass=mat('CabinWindow',(.95,.51,.13)); metal=mat('CabinIron',(.08,.10,.11))
window_glass=mat('CabinGlass',(.72,.84,.88))
window_glass.diffuse_color=(.72,.84,.88,.12)
objects=[]
def cube(n,p,s,m,rot=None,bevel=0):
    bpy.ops.mesh.primitive_cube_add(size=1,location=p); o=bpy.context.object; o.name=n; o.dimensions=s
    if rot:o.rotation_euler=rot
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    o.data.materials.append(m)
    if bevel:
        md=o.modifiers.new('HandHewnEdges','BEVEL');md.width=bevel;md.segments=1
        bpy.ops.object.modifier_apply(modifier=md.name)
    objects.append(o);return o
def beam(n,a,b,width,depth,m):
    d=Vector(b)-Vector(a);o=cube(n,(Vector(a)+Vector(b))/2,(width,depth,d.length),m)
    o.rotation_euler=d.to_track_quat('Z','Y').to_euler();return o
def cyl(n,p,r,length,m,axis='Z'):
    bpy.ops.mesh.primitive_cylinder_add(vertices=10,radius=r,depth=length,location=p)
    o=bpy.context.object;o.name=n;o.data.materials.append(m)
    if axis=='X':o.rotation_euler[1]=math.pi/2
    if axis=='Y':o.rotation_euler[0]=math.pi/2
    objects.append(o);return o

def front_panel(n,x,y,z,w,d,h,m,bevel=0):
    """前墙板按两扇窗洞分段；内衬与外板共用洞口，边缘跨洞的木板也切齐。"""
    left,right=x-w/2,x+w/2; bottom,top=z-h/2,z+h/2
    cuts=sorted({left,right,*[v for v in [-3.45,-2.05,2.05,3.45] if left<v<right]})
    for a,b in zip(cuts,cuts[1:]):
        inside=2.05<abs((a+b)/2)<3.45
        spans=[(bottom,top)] if not inside else [(bottom,min(top,1.455)),(max(bottom,2.905),top)]
        for lo,hi in spans:
            if hi>lo:
                cube(n,((a+b)/2,y,(lo+hi)/2),(b-a,d,hi-lo),m,bevel=bevel)
# 木屋前面位于 Blender -Y；FBX 导出后用 Unity 实际网格验证朝向。
for y in [-3,3]:
    for i in range(11): cube('Body_StoneFooting',(-4.5+i*.9,y,.3),(.85,.6,.6),random.choice(stone),bevel=.07)
for x in [-4.7,4.7]:
    for i in range(7):cube('Body_StoneSide',(x,-2.6+i*.85,.3),(.6,.8,.6),random.choice(stone),bevel=.06)
# 地板与门廊齐平；墙体保持真实厚度，屋内不再有遮光实心块。
for i in range(23):cube('Body_InteriorFloor',(-4.73+i*.43,0,.53),(.43,6.1,.20),wood[i%4])
cube('Body_Ceiling',(0,0,3.65),(9.45,6.10,.16),wood[1])
for y in [-2,0,2]:cube('Body_CeilingBeam',(0,y,3.48),(9.4,.22,.26),trim)
for i in range(13):
    h=.68+i*.245
    for y in [-3.07,3.07]:
        if y<0 and h<3.25:
            for side in [-1,1]:front_panel('Body_FrontCladding',side*2.89,y,h,3.77,.24,.245,random.choice(wood),bevel=.008)
        else:cube('Body_HorizontalCladding',(0,y,h),(9.55,.24,.245),random.choice(wood),bevel=.008)
    for x in [-4.76,4.76]:cube('Body_SideCladding',(x,0,h),(.19,6.25,.225),random.choice(wood),bevel=.024)
for x in [-4.5,0,4.5]:
    for y in [-3.2,3.2]:
        if x==0 and y<0:continue
        cube('Body_StructuralPost',(x,y,2.15),(.26,.28,3.7),trim,bevel=.028)
# 平整内衬封住板缝漏光；前墙保留与外墙一致的门洞。
for x in [-4.60,4.60]:cube('Body_InteriorSide',(x,0,2.13),(.10,6.02,3.02),wood[1])
cube('Body_InteriorBack',(0,2.95,2.13),(9.2,.10,3.02),wood[2])
for side in [-1,1]:front_panel('Body_InteriorFront',side*2.80,-2.95,2.13,3.60,.10,3.02,wood[2])
for y in [-3.18,3.18]:
    for i in range(11):
        h=3.9+i*.24;w=9.3*(1-(h-3.8)/2.6)
        if w>0:cube('Body_GableBoard',(0,y,h),(w,.2,.22),random.choice(wood),bevel=.015)
# 主屋顶逐排错缝，独立 Roof 命名便于后续遮挡配置。
angle=math.atan2(2.6,5.15)
for side in [-1,1]:
    for row in range(10):
        x=side*(row+.5)*.535;z=6.45-abs(x)*math.tan(angle)
        for col in range(13):
            y=-3.7+col*.61+(.12 if row%2 else 0)
            cube('Roof_Slate',(x,y,z),(.63,.60,.095),random.choice(roof),(0,side*angle,0),bevel=.015)
    beam('Roof_Fascia',(0,-3.91,6.51),(side*5.5,-3.91,3.73),.20,.23,trim)
    beam('Roof_Fascia',(0,3.99,6.51),(side*5.5,3.99,3.73),.20,.23,trim)
beam('Roof_Ridge',(0,-4,6.51),(0,4,6.51),.23,.22,roof[1])
# 门廊、木台阶、支撑斜撑。
for i in range(23):cube('Body_PorchPlank',(-4.8+i*.43,-4.30,.55),(.405,2.45,.16),random.choice(wood),bevel=.015)
for i in range(3):cube('Body_EntranceStep',(-11/7,-6.15+i*.40,.09+i*.18),(1.42,.46,.18),wood[2],bevel=.02)
for x in [-4.7,-2.8,1.75,4.7]:
    cube('Body_PorchPost',(x,-5.35,1.91),(.20,.20,2.82),trim,bevel=.02)
    for side in [-1,1]:beam('Body_PorchBrace',(x,-5.35,2.55),(x+side*.52,-5.35,3.22),.14,.15,wood[2])
beam('Roof_PorchBeam',(-5,-5.35,3.3),(5,-5.35,3.3),.24,.24,trim)
for row in range(5):
    y=-3.10-row*.57;z=4.00-row*.18
    for col in range(20):cube('Roof_PorchSlates',(-5+col*.525,y,z),(.51,.67,.08),random.choice(roof),(math.atan(.18/.57),0,0),bevel=.01)
for x,w in [(-3.75,1.65),(3.25,2.3)]:
    cube('Body_PorchRailing',(x,-5.35,1.4),(w,.16,.16),trim,bevel=.02)
    for j in range(6):cube('Body_PorchBaluster',(x-w/2+j*w/5,-5.35,1.0),(.10,.10,.70),wood[1])
for x in [-4.87,4.87]:
    cube('Body_SideRailing',(x,-4.28,1.4),(.16,2.3,.16),trim)
    for j in range(6):cube('Body_SideBaluster',(x,-5.30+j*.41,1.0),(.10,.10,.70),wood[1])
# 门窗使用实体窗框与暗边，非平面贴片。
for x in [-1.07,1.07]:cube('Body_DoorJamb',(x,-3.15,1.96),(.16,.40,2.66),trim,bevel=.025)
cube('Body_DoorHeader',(0,-3.15,3.30),(2.3,.40,.18),trim,bevel=.025)
# 木门固定向外打开 90 度，门叶沿右门框伸向门廊，让左侧台阶通路保持畅通。
for i in range(8):cube('Body_OpenDoor',(1.08,-3.26-(i+.5)*.245,1.94),(.12,.235,2.58),wood[i%4],bevel=.012)
for z in [1.08,2.68]:cube('Body_OpenDoorIron',(1,-4.24,z),(.05,1.88,.09),metal)
cyl('Body_DoorHandle',(.94,-5.00,1.80),.055,.10,metal,'X')
# 两盏固定暖光灯的实体外壳；光源由 Unity 场景持有并随地图停用。
for x in [-3.8,3.8]:
    cube('Body_LanternBack',(x,2.78,2.5),(.30,.15,.55),metal)
    cube('Windows_LanternGlass',(x,2.60,2.5),(.23,.22,.37),glass)
    for z in [2.28,2.72]:cube('Body_LanternCap',(x,2.60,z),(.36,.34,.09),metal)
def window(x,y,z,w=1.4,h=1.45,through=False):
    """房间窗贯穿墙厚、内外都有窗框；阁楼窗保留原装饰外观。"""
    if through:
        # 洞口四边封住墙板与内衬间隙，不使用遮住整个窗口的实心底板。
        for dx in [-w/2-.04,w/2+.04]:cube('Body_WindowReveal',(x+dx,-3.06,z),(.08,.44,h+.16),trim)
        for dz in [-h/2-.04,h/2+.04]:cube('Body_WindowReveal',(x,-3.06,z+dz),(w,.44,.08),trim)
        cube('Glass_WindowPane',(x,-3.08,z),(w,.025,h),window_glass)
        frame_depths=[y-.18,-2.87]
    else:
        cube('Body_WindowRecess',(x,y,z),(w+.27,.15,h+.24),trim,bevel=.02)
        cube('Windows_WarmGlass',(x,y-.11,z),(w,.08,h),glass)
        frame_depths=[y-.18]
    for frame_y in frame_depths:
        for dx in [-w/2,0,w/2]:cube('Body_WindowMullion',(x+dx,frame_y,z),(.075,.12,h+.1),wood[0])
        for dz in [-h/2,0,h/2]:cube('Body_WindowCrossbar',(x,frame_y,z+dz),(w+.1,.12,.07),wood[0])
        sill_y=frame_y-.05 if frame_y< -3 else frame_y+.05
        cube('Body_WindowSill',(x,sill_y,z-h/2-.08),(w+.35,.37,.13),wood[2],bevel=.02)
    for side in [-1,1]:
        for k in range(5):cube('Body_Shutter',(x+side*(w/2+.24),y-.10,z-h/2+.13+k*.29),(.34,.1,.26),wood[1])
for x in [-2.75,2.75]:window(x,-3.23,2.18,through=True)
window(0,-3.26,4.70,1.15,1.22)
# 石砌烟囱。
for row in range(2,15):
    z=3.35+row*.26
    for col in range(3):
        for side in [-1,1]:cube('Body_ChimneyStone',(2.65+(col-1)*.38,1.6+side*.44,z),(.355,.32,.235),random.choice(stone),bevel=.03)
    for side in [-1,1]:cube('Body_ChimneyStone',(2.65+side*.54,1.6,z),(.3,.63,.24),random.choice(stone),bevel=.025)
cube('Body_ChimneyCap',(2.65,1.6,7.22),(1.55,1.3,.18),stone[0],bevel=.04)
cube('Body_ChimneyOpening',(2.65,1.6,7.32),(.72,.56,.025),metal)
# 门廊旁柴垛。
for row in range(4):
    for col in range(5-row):
        x=3.25+(col-(4-row)/2)*.29;z=.73+row*.25
        cyl('Body_StackedFirewood',(x,-4.6,z),.145,.85,wood[0],'Y')
        cyl('Body_LogEnd',(x,-5.035,z),.125,.014,cut,'Y')
# 按材质与屋顶/主体分组合并，避免每块木板一个运行时 Renderer。
for prefix in ['Body','Roof','Windows','Glass']:
    subset=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.name.startswith(prefix)]
    bpy.ops.object.select_all(action='DESELECT')
    for o in subset:o.select_set(True)
    bpy.context.view_layer.objects.active=subset[0];bpy.ops.object.join();bpy.context.object.name=prefix
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
# 单独导出木屋时合并其调色板，不重新生成景观或丢失湖岸材质。
if CABIN_ONLY:
    palette_path=os.path.join(OUT,'LakesidePalette.json')
    with open(palette_path,encoding='utf-8') as stream:palette=json.load(stream)
    colors={entry['name']:entry for entry in palette['colors']}
    for m in bpy.data.materials:
        if m.name.startswith('Cabin'):colors[m.name]={'name':m.name,'rgba':list(m.diffuse_color)}
    palette['colors']=list(colors.values())
    with open(palette_path,'w',encoding='utf-8') as stream:json.dump(palette,stream,indent=2)
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT,'Tools/Blender/BaseLakesideCabin.blend'))
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT,'BaseLakesideCabin.fbx'),use_selection=False,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_anim=False)
print('CABIN_EXPORT_COMPLETE',sum(len(o.data.polygons) for o in bpy.context.scene.objects if o.type=='MESH'))
if CABIN_ONLY:
    if '--level-cabin-ground' in sys.argv:
        # 使用已保存景观原网格，仅压低木屋覆盖区域；不重抽随机数或改动雪山、水面。
        landscape_path=os.path.join(ROOT,'Tools/Blender/BaseLakesideLandscape.blend')
        bpy.ops.wm.open_mainfile(filepath=landscape_path)
        terrain=bpy.data.objects['LakesideTerrain']
        for v in terrain.data.vertices:
            x,y,z=v.co
            if -22<x<4 and 8<y<33:v.co.z=min(z,.03)
        terrain.data.update()
        bpy.ops.wm.save_as_mainfile(filepath=landscape_path)
        bpy.ops.export_scene.fbx(filepath=os.path.join(OUT,'BaseLakesideLandscape.fbx'),use_selection=False,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_anim=False)
    sys.exit(0)

# 基地平地外延伸为湖岸与远山；可行走区的高度仍由现有地图碰撞决定。
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
earth=[mat('ShoreEarth'+str(i),c) for i,c in enumerate([(.24,.28,.17),(.28,.31,.19),(.31,.32,.20),(.35,.34,.24),(.38,.36,.27)])]
cliff=[mat('ShoreRock'+str(i),c) for i,c in enumerate([(.29,.33,.33),(.35,.39,.39),(.40,.42,.39)])]
water=[mat('LakeWater'+str(i),c) for i,c in enumerate([(.16,.34,.40),(.18,.37,.43),(.20,.39,.45),(.22,.41,.46)])]
ridge=[mat('Mountain'+str(i),c) for i,c in enumerate([(.28,.37,.42),(.33,.42,.47),(.38,.45,.49),(.43,.50,.53)])]
snow=[mat('MountainSnow'+str(i),c) for i,c in enumerate([(.61,.68,.70),(.73,.77,.76),(.84,.85,.80)])]
def mesh(n,v,f,mats,indices):
    me=bpy.data.meshes.new(n);me.from_pydata(v,[],f);me.update();ob=bpy.data.objects.new(n,me);bpy.context.collection.objects.link(ob)
    for m in mats:me.materials.append(m)
    for p,idx in zip(me.polygons,indices):p.material_index=idx
    return ob
def shore_height(x,y):
    edge=min(26+5*math.sin(y*.05)-x,44+6*math.sin(x*.07)-y)
    if edge<0:return -3.7
    beyond=max(abs(x)-15,abs(y)-15,0)
    h=(math.sin(x*.13)*math.cos(y*.1)*1.3 + .9*math.sin(x*.06+y*.13)) * min(1,beyond/18)
    height=min(1,edge/5)*(h+.1)-1.2*(1-min(1,edge/5)) if beyond>0 else -.01
    return min(height,.03) if -22<x<4 and 8<y<33 else height
v=[];f=[];mi=[];nx=51;ny=45
for j in range(ny):
    for i in range(nx):
        x=-130+i*6;y=-130+j*6
        if i not in [0,nx-1]:x+=random.uniform(-1.2,1.2)
        if j not in [0,ny-1]:y+=random.uniform(-1.2,1.2)
        v.append((x,y,shore_height(x,y)))
for j in range(ny-1):
    for i in range(nx-1):
        a=j*nx+i
        for tri in [(a,a+1,a+nx),(a+1,a+nx+1,a+nx)]:
            f.append(tri);z=sum(v[k][2] for k in tri)/3;mi.append(random.randrange(5) if z>-.7 else 5+random.randrange(3))
mesh('LakesideTerrain',v,f,earth+cliff,mi)
# 水面为真实平面网格，使用少量长条色差表达静水波纹。
v=[];f=[];mi=[];nx=53;ny=79
for j in range(ny):
    for i in range(nx):v.append((-150+i*8,-100+j*5,-1.45+random.uniform(-.017,.017)))
for j in range(ny-1):
    for i in range(nx-1):
        a=j*nx+i;f.extend([(a,a+1,a+nx),(a+1,a+nx+1,a+nx)]);mi.extend([random.randrange(4)]*2)
mesh('LakeSurface',v,f,water,mi)
# 非对称山脊与雪线，保留河谷开口，避免用重复圆锥冒充山体。
v=[];f=[];mi=[];nx=113;ny=35
peaks=[(-115,170,76,34),(-75,155,64,22),(-42,187,98,32),(0,183,60,23),(48,205,73,29),(107,213,83,35),(157,180,65,30),(225,197,92,38)]
for j in range(ny):
    for i in range(nx):
        x=-190+i*4+random.uniform(-1,1);y=96+j*5+random.uniform(-1,1)
        h=max(amp*max(0,1-math.sqrt(((x-px)/w)**2+((y-py)/(w*1.3))**2)) for px,py,amp,w in peaks)
        h+=max(0,math.sin(x*.032+y*.043)*9+9)+math.sin(x*.2+y*.15)*2.4+random.uniform(-2,2)
        if j==0:h=max(0,h*.22)
        v.append((x,y,h-2))
for j in range(ny-1):
    for i in range(nx-1):
        a=j*nx+i
        for tri in [(a,a+1,a+nx),(a+1,a+nx+1,a+nx)]:
            f.append(tri);h=sum(v[k][2] for k in tri)/3
            mi.append(4+random.randrange(3) if h>48+random.uniform(-7,7) else random.randrange(4))
mesh('DistantMountainRidge',v,f,ridge+snow,mi)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT,'Tools/Blender/BaseLakesideLandscape.blend'))
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT,'BaseLakesideLandscape.fbx'),use_selection=False,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_anim=False)
print('LANDSCAPE_EXPORT_COMPLETE')
with open(os.path.join(OUT,'LakesidePalette.json'),'w',encoding='utf-8') as stream:
    json.dump({'colors':[{'name':m.name,'rgba':list(m.diffuse_color)} for m in bpy.data.materials if m.name.startswith(('Cabin','Shore','Lake','Mountain'))]},stream,indent=2)
