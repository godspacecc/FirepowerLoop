using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;

namespace Game
{
    public sealed partial class ValleyMapBuilder
    {
        /// <summary>以保存折线创建贴地泥路与四侧实体边界；可见网格不另设高度碰撞，均归候选句柄释放。</summary>
        private static void BuildOpenRoadsAndBoundary(GeneratedValleyContentHandle handle,ValleyGenerationKit kit,ValleyLayoutSaveData l)
        {
            var paths=new List<List<ValleyPosition>>{l.Route,l.AlternateRoute};paths.AddRange(l.BranchRoads);
            var serial=0;
            foreach(var path in paths)
            {
                var vertices=new List<Vector3>();var triangles=new List<int>();var uv=new List<Vector2>();
                for(int i=1;i<path.Count;i++)
                {
                    var a=path[i-1].ToVector();var b=path[i].ToVector();var dir=b-a;dir.y=0;
                    var normal=Vector3.Cross(Vector3.up,dir.normalized)*2.5f;
                    var count=Mathf.CeilToInt(dir.magnitude/2);
                    for(int j=0;j<=count;j++)
                    {
                        var center=Vector3.Lerp(a,b,(float)j/count);
                        foreach(var sign in new[]{-1,1})
                        {var p=center+normal*sign;p.y=l.HeightAt(p.x,p.z)+.025f;vertices.Add(p);uv.Add(new Vector2(sign<0?0:1,j*.25f));}
                        if(j==0)continue;
                        var end=vertices.Count-1;triangles.AddRange(new[]{end-3,end-1,end-2,end-2,end-1,end});
                    }
                }
                var mesh=new Mesh{name="ValleyRoad"};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();handle.Meshes.Add(mesh);
                var go=new GameObject("Road_"+serial++);go.transform.SetParent(handle.GeneratedRoot.transform,false);
                go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=kit.RiverbedMaterial;
            }
            for(int i=0;i<4;i++)
            {
                var alongX=i<2;var sign=i%2==0?-1:1;
                var go=new GameObject("ValleyBoundary_"+i);go.transform.SetParent(handle.GeneratedRoot.transform,false);
                go.transform.localPosition=alongX?new Vector3(sign*513,40,0):new Vector3(0,40,sign*513);
                var box=go.AddComponent<BoxCollider>();box.size=alongX?new Vector3(2,120,1028):new Vector3(1028,120,2);
                handle.Sources.Add(new NavMeshBuildSource{shape=NavMeshBuildSourceShape.Box,transform=go.transform.localToWorldMatrix,size=box.size,area=1});
            }
        }
        /// <summary>把实际内容的非触发碰撞写入导航来源，防止树干和现场布景创建后仍使用空地导航。</summary>
        private static void AddContentCollisionSources(GeneratedValleyContentHandle handle)
        {
            foreach(var collider in handle.GeneratedRoot.GetComponentsInChildren<Collider>(true))
            {
                if(collider.transform.IsChildOf(handle.KitInstance.transform)||collider.name.StartsWith("TerrainChunk_")||
                    collider.name.StartsWith("ValleyBoundary_")||collider.name=="SceneryRockBlocker")continue;
                if(collider.isTrigger||!collider.enabled||!collider.gameObject.activeSelf)continue;
                var source=new NavMeshBuildSource{area=1};
                if(collider is BoxCollider box)
                {source.shape=NavMeshBuildSourceShape.Box;source.size=box.size;source.transform=box.transform.localToWorldMatrix*Matrix4x4.Translate(box.center);}
                else if(collider is CapsuleCollider capsule)
                {source.shape=NavMeshBuildSourceShape.Box;source.size=Vector3.one*capsule.radius*2;source.size=new Vector3(source.size.x,capsule.height,source.size.z);source.transform=capsule.transform.localToWorldMatrix*Matrix4x4.Translate(capsule.center);}
                else if(collider is SphereCollider sphere)
                {source.shape=NavMeshBuildSourceShape.Box;source.size=Vector3.one*sphere.radius*2;source.transform=sphere.transform.localToWorldMatrix*Matrix4x4.Translate(sphere.center);}
                else if(collider is MeshCollider mesh && mesh.sharedMesh!=null)
                {source.shape=NavMeshBuildSourceShape.Box;source.size=mesh.sharedMesh.bounds.size;source.transform=mesh.transform.localToWorldMatrix*Matrix4x4.Translate(mesh.sharedMesh.bounds.center);}
                else throw new InvalidOperationException("不支持的河谷内容碰撞："+collider.name);
                // 用体积标记地表而不是再烘焙碰撞体上下表面，避免树干下方残留独立导航小面。
                source.shape=NavMeshBuildSourceShape.ModifierBox;
                source.size+=Vector3.up;
                handle.Sources.Add(source);
            }
        }
        /// <summary>内容装配后在隔离导航中检查道路双向直通、交互、敌人、出生、唯一撤离及全部陆地小区；失败拒绝入图。</summary>
        private static async UniTask ValidateOpenPassageAsync(GeneratedValleyContentHandle handle,CancellationToken token)
        {
            var l=handle.Layout;var offset=new Vector3(16384,0,0);
            void Check(Vector3 target,string label,float radius=2.25f)
            {
                if(!NavMesh.SamplePosition(l.Spawn.ToVector()+offset,out var start,1,NavMesh.AllAreas))throw new InvalidOperationException("出生缺少站位");
                var path=new NavMeshPath();
                // 采集身份位于树干中心，必须验收触达范围内的实际站位，不能把树干内的小孤立导航面当站位。
                for(int ring=0;ring<3;ring++)for(int turn=0;turn<(ring==0?1:12);turn++)
                {
                    var distance=ring==0?0:radius*(ring==1?.65f:.9f);var angle=turn*Mathf.PI/6;
                    var p=target+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*distance;p.y=l.HeightAt(p.x,p.z);
                    if(!NavMesh.SamplePosition(p+offset,out var end,.5f,NavMesh.AllAreas))continue;
                    var delta=end.position-offset-target;delta.y=0;if(delta.magnitude>radius)continue;
                    if(NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete)return;
                }
                NavMesh.SamplePosition(target+offset,out var nearest,8,NavMesh.AllAreas);
                var probe=target+Vector3.right*6;probe.y=l.HeightAt(probe.x,probe.z);NavMesh.SamplePosition(probe+offset,out var far,1,NavMesh.AllAreas);
                NavMesh.CalculatePath(start.position,far.position,NavMesh.AllAreas,path);
                var point=handle.GeneratedRoot.GetComponentsInChildren<CivilisationPointView>(true).FirstOrDefault(v=>(v.transform.position-target).sqrMagnitude<.1f);
                var collision=point==null?"":string.Join(";",point.GetComponentsInChildren<Collider>(true).Where(c=>!c.isTrigger).Select(c=>c.GetType().Name+" scale="+c.transform.lossyScale+" local="+c.transform.localPosition));
                throw new InvalidOperationException(label+"：范围内没有可达站位 target="+target+" start="+(start.position-offset)+" nearest="+(nearest.position-offset)+" far="+(far.position-offset)+" farStatus="+path.status+" "+collision);
            }
            if(ValleySwimmingRules.TrySample(l,l.Spawn.ToVector(),out _,out _))throw new InvalidOperationException("出生点不能落水");
            if(l.Enemies.Any(e=>Vector3.Distance(e.Pose.ToVector(),l.Spawn.ToVector())<50))throw new InvalidOperationException("出生点接近敌人");
            var paths=new List<List<ValleyPosition>>{l.Route,l.AlternateRoute};paths.AddRange(l.BranchRoads);
            foreach(var road in paths)
                for(int i=1;i<road.Count;i++)
                {
                    // 先逐段采样实际地形，再检查直通导航，不能用绕远路成功掩盖道路被布景堵死。
                    var a=road[i-1].ToVector();var b=road[i].ToVector();var count=Mathf.CeilToInt(Vector3.Distance(a,b)/4);
                    var previous=a;previous.y=l.HeightAt(a.x,a.z);
                    for(int j=1;j<=count;j++)
                    {
                        var p=Vector3.Lerp(a,b,(float)j/count);p.y=l.HeightAt(p.x,p.z);
                        if(!NavMesh.SamplePosition(previous+offset,out var start,1.5f,NavMesh.AllAreas)||!NavMesh.SamplePosition(p+offset,out var end,1.5f,NavMesh.AllAreas)||
                            NavMesh.Raycast(start.position,end.position,out _,NavMesh.AllAreas))throw new InvalidOperationException("道路或浅滩不能直接步行");
                        previous=p;
                    }
                }
            Check(l.Extraction.ToVector(),"撤离点");
            foreach(var p in l.Route.Concat(l.AlternateRoute))Check(p.ToVector(),"道路节点");
            foreach(var p in l.Points)Check(p.Pose.ToVector(),"交互点 "+p.Id+" "+p.TemplateId);
            foreach(var c in l.Clues)Check(c.Pose.ToVector(),"线索 "+c.Id);
            foreach(var e in l.Enemies)Check(e.Pose.ToVector(),"敌人 "+e.Id);
            // 16米格全量检查，碰撞占地允许3米接近站位；缓坡河岸在每个河段两侧单独验收。
            var scanned=0;
            for(int z=-496;z<=496;z+=16)for(int x=-496;x<=496;x+=16)
            {
                var p=new Vector3(x,l.HeightAt(x,z),z);
                // 敌人导航与玩家涉水不是同一可达集合：水域另由连续缓坡及无碰撞岸带检查，不能用敌人禁止区判玩家困水。
                if(ValleySwimmingRules.TrySample(l,p,out _,out var depth)&&depth>0)continue;
                if(!NavMesh.SamplePosition(p+offset,out var end,3,NavMesh.AllAreas))throw new InvalidOperationException("陆地小区缺少可通行站位 "+x+","+z);
                Check(p,"陆地小区 "+x+","+z,3);
                if(++scanned%64==0){await UniTask.Yield();token.ThrowIfCancellationRequested();}
            }
            for(int i=1;i<l.River.Count-1;i++)
                foreach(var sign in new[]{-1,1})
                {
                    var center=l.River[i].ToVector();if(Mathf.Max(Mathf.Abs(center.x),Mathf.Abs(center.z))>496)continue;
                    var tangent=l.River[i+1].ToVector()-l.River[i-1].ToVector();tangent.y=0;
                    var p=center+Vector3.Cross(Vector3.up,tangent.normalized)*(sign*32);p.y=l.HeightAt(p.x,p.z);
                    Check(p,"上岸位置 "+i+"/"+sign);
                }
        }
    }
}
