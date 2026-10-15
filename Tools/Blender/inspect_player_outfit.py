"""只读检查当前主角 FBX 的网格、骨架与连通片，供拆分穿戴样例使用。"""
import bpy, os, json
from mathutils import Vector
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '../..'))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.join(ROOT, 'Client/Assets/Game/Art/Player/Humanoid/PlayerCityCharacters.fbx'))
report = []
for ob in bpy.data.objects:
    if ob.type == 'ARMATURE':
        report.append({'name':ob.name,'bones':[(b.name, list(b.head_local)) for b in ob.data.bones]})
    if ob.type != 'MESH' or 'Hoodie' not in ob.name: continue
    verts = ob.data.vertices
    groups = {}
    for v in verts:
        key = tuple(round(c, 4) for c in v.co)
        groups.setdefault(key, []).append(v.index)
    adj = {v.index:set() for v in verts}
    for ids in groups.values():
        for i in ids: adj[i].update(ids)
    for p in ob.data.polygons:
        for i in p.vertices: adj[i].update(p.vertices)
    unseen = set(adj); comps=[]
    while unseen:
        todo=[unseen.pop()]; found=set(todo)
        while todo:
            for i in adj[todo.pop()]:
                if i in unseen: unseen.remove(i); found.add(i); todo.append(i)
        co=[ob.matrix_world @ verts[i].co for i in found]
        comps.append({'n':len(found),'min':[min(v[k] for v in co) for k in range(3)],'max':[max(v[k] for v in co) for k in range(3)]})
    report.append({'name':ob.name,'matrix':[list(r) for r in ob.matrix_world], 'materials':[m.name for m in ob.data.materials], 'components':sorted(comps,key=lambda c:-c['n'])})
print('OUTFIT_INSPECT='+json.dumps(report))
ob=bpy.data.objects['Character_Male_Hoodie']
for other in list(bpy.data.objects):
    if other.type=='MESH' and other!=ob: bpy.data.objects.remove(other,do_unlink=True)
mat=bpy.data.materials.new('PreviewPalette'); mat.use_nodes=True
tex=mat.node_tree.nodes.new('ShaderNodeTexImage'); tex.image=bpy.data.images.load(os.path.join(ROOT,'Client/Assets/Game/Art/Player/Humanoid/PlayerCityTexture.png'))
mat.node_tree.links.new(tex.outputs['Color'],mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'])
ob.data.materials.clear(); ob.data.materials.append(mat)
bpy.ops.object.camera_add(location=(2,-5,2.3)); camera=bpy.context.object
camera.rotation_euler=(Vector((0,0,.95))-camera.location).to_track_quat('-Z','Y').to_euler(); camera.data.type='ORTHO'; camera.data.ortho_scale=2.6
bpy.context.scene.camera=camera
bpy.ops.object.light_add(type='AREA',location=(1,-3,4)); bpy.context.object.data.energy=450; bpy.context.object.data.shape='DISK'; bpy.context.object.data.size=5
scene=bpy.context.scene; scene.render.engine='CYCLES'; scene.cycles.samples=16; scene.world=bpy.data.worlds.new('World'); scene.world.color=(.25,.25,.25)
scene.render.resolution_x=900; scene.render.resolution_y=900; scene.render.resolution_percentage=100
scene.render.filepath=os.path.join(ROOT,'Tools/Blender/PlayerOutfitSource.png'); bpy.ops.render.render(write_still=True)
print('RINGS='+json.dumps(sorted(set(round(v.co.y,2) for v in ob.data.vertices if 65<v.co.y<105))))
palette={}
pixels=list(tex.image.pixels); w,h=tex.image.size
for p in ob.data.polygons:
    uv=ob.data.uv_layers.active.data[p.loop_start].uv
    idx=(min(h-1,int(uv.y*h))*w+min(w-1,int(uv.x*w)))*4
    rgb=tuple(round(c,2) for c in pixels[idx:idx+3])
    coords=[ob.data.vertices[i].co for i in p.vertices]
    entry=palette.setdefault(str(rgb),[])
    entry.append([round(sum(c[k] for c in coords)/len(coords),1) for k in range(3)])
print('PALETTE='+json.dumps({k:{'n':len(v),'y':[min(c[1] for c in v),max(c[1] for c in v)],'x':[min(c[0] for c in v),max(c[0] for c in v)]} for k,v in palette.items()}))
