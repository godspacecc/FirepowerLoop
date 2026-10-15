using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace Game
{
    /// <summary>在隐藏河谷中装配静态布景及大石块阻挡；派生导航源，不修改保存布局、交互身份或科技。</summary>
    internal static class ValleySceneryBuilder
    {
        /// <summary>地形完成、首次导航烘焙前调用一次；碰撞与网格归地图句柄所有，失败由原构建事务回收。</summary>
        internal static void Build(GeneratedValleyContentHandle handle,ValleyGenerationKit kit)
        {
            if(kit.SceneryRockPrefab==null)return;
            var layout=handle.Layout;var random=new System.Random(unchecked(layout.Seed*397+layout.AttemptIndex*17+61012));
            var root=new GameObject("ValleyScenery");root.transform.SetParent(handle.GeneratedRoot.transform,false);
            var groups=new Dictionary<(int x,int z,bool grass,Material material),List<CombineInstance>>();
            float R(float min,float max)=>min+(max-min)*(float)random.NextDouble();
            var clearings=layout.Points.Where(p=>!p.TemplateId.StartsWith("tree.")).Select(p=>p.Pose.ToVector()).Concat(layout.Clues.Select(c=>c.Pose.ToVector())).ToArray();
            var spawnApproach=layout.GenerationVersion==3?ValleyLayoutGenerator.SpawnApproach(layout):null;
            bool Clear(Vector3 p,float distance)
            {
                if(Mathf.Abs(p.x)>470||Mathf.Abs(p.z)>470||Horizontal(p,layout.Spawn.ToVector())<8||Horizontal(p,layout.Camp.ToVector())<7)return false;
                if(layout.GenerationVersion==3 && (ValleyLayoutGenerator.RoadDistance(layout,p)<10 ||
                    ValleyLayoutGenerator.Distance(p,layout.River,out _)<35 || Horizontal(p,layout.Extraction.ToVector())<14 ||
                    ValleyLayoutGenerator.Distance(p,spawnApproach,out _)<8))return false;
                return !layout.Fords.Any(f=>Horizontal(p,f.ToVector())<15)&&!clearings.Any(c=>Horizontal(p,c)<distance)&&ValleyLayoutGenerator.Distance(p,layout.Shortcut,out _)>8;
            }
            void Add(Vector3 p,bool grass,float scale,bool water=false)
            {
                if(!Clear(p,grass?4:7))return;
                float distance=ValleyLayoutGenerator.Distance(p,layout.River,out var level);
                if(grass&&distance<8)return;
                if(!grass&&!water&&(distance<8||ValleyLayoutGenerator.Distance(p,layout.Route,out _)<7||ValleyLayoutGenerator.Distance(p,layout.AlternateRoute,out _)<7))return;
                var dx=(layout.HeightAt(p.x+4,p.z)-layout.HeightAt(p.x-4,p.z))/8;var dz=(layout.HeightAt(p.x,p.z+4)-layout.HeightAt(p.x,p.z-4))/8;
                if(!water&&dx*dx+dz*dz>.22f)return;
                p.y=water?level:layout.HeightAt(p.x,p.z);
                var prefab=grass?kit.SceneryGrassPrefab:kit.SceneryRockPrefab;
                var filters=prefab.GetComponentsInChildren<MeshFilter>(true);var yaw=Quaternion.Euler(0,R(0,360),0);
                var size=grass?Vector3.one*scale:new Vector3(scale*R(.8f,1.2f),scale*R(.8f,1.1f),scale*R(.8f,1.2f));
                if(!grass)
                {
                    var bounds=filters[0].sharedMesh.bounds;
                    p.y+=water?Mathf.Clamp(scale*.22f,.1f,1.4f)-bounds.max.y*size.y:-bounds.min.y*size.y-scale*.04f;
                }
                int x=Mathf.FloorToInt((p.x+512)/64),z=Mathf.FloorToInt((p.z+512)/64);var origin=new Vector3(x*64-512,0,z*64-512);
                var matrix=Matrix4x4.TRS(p-origin,yaw,size);
                foreach(var filter in filters)
                {
                    var mesh=filter.sharedMesh;if(!mesh.isReadable)throw new InvalidOperationException("河谷布景网格不可读取："+mesh.name);
                    var materials=filter.GetComponent<Renderer>().sharedMaterials;
                    for(int sub=0;sub<mesh.subMeshCount;sub++)
                    {
                        var key=(x,z,grass,materials[sub]);if(!groups.TryGetValue(key,out var list))groups[key]=list=new List<CombineInstance>();
                        list.Add(new CombineInstance{mesh=mesh,subMeshIndex=sub,transform=matrix*prefab.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix});
                    }
                }
                if(!grass)AddRockBlocker(handle,root.transform,p,Matrix4x4.TRS(Vector3.zero,yaw,size),prefab,filters);
            }
            for(int i=1;i<layout.River.Count;i++)
            {
                var a=layout.River[i-1].ToVector();var b=layout.River[i].ToVector();var direction=b-a;direction.y=0;var normal=Vector3.Cross(Vector3.up,direction.normalized);
                int count=Mathf.CeilToInt(direction.magnitude/24);
                for(int j=0;j<count;j++)
                {
                    var center=Vector3.Lerp(a,b,(j+.5f)/count);
                    for(int side=-1;side<=1;side+=2)
                    {
                        var shore=center+normal*(side*R(8.5f,12));Add(shore,false,R(1.1f,3));
                        Add(shore+normal*(side*R(2,5)),true,R(.8f,1.3f));
                        Add(shore+direction.normalized*R(-3,3),false,R(.4f,1));
                    }
                    if(j%3==0)
                    {
                        var p=center+normal*R(-3,3);Add(p,false,R(2.5f,4.5f),true);Add(p+direction.normalized*2,false,R(.8f,1.7f),true);
                    }
                }
            }
            foreach(var center in new[]{layout.Camp.ToVector(),layout.Spawn.ToVector()})
            {
                for(int i=0;i<95;i++)
                {
                    var angle=R(0,Mathf.PI*2);var radius=R(8,42);var p=center+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*radius;
                    Add(p,true,R(.85f,1.3f));if(i%5==0)Add(p+new Vector3(1,0,1),false,R(.5f,2.1f));
                }
            }
            foreach(var entry in groups)
            {
                var mesh=new Mesh{name=(entry.Key.grass?"SceneryGrass_":"SceneryRocks_")+entry.Key.x+"_"+entry.Key.z,indexFormat=IndexFormat.UInt32};
                handle.Meshes.Add(mesh);mesh.CombineMeshes(entry.Value.ToArray(),true,true);mesh.RecalculateBounds();
                // 正式运行只保留GPU副本；编辑态保留顶点，便于比较读档一致性。
                mesh.UploadMeshData(Application.isPlaying);
                var go=new GameObject(mesh.name);go.transform.SetParent(root.transform,false);go.transform.localPosition=new Vector3(entry.Key.x*64-512,0,entry.Key.z*64-512);
                go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=entry.Key.material;
                renderer.shadowCastingMode=entry.Key.grass?ShadowCastingMode.Off:ShadowCastingMode.On;
            }
            for(int i=0;i<4;i++)
            {
                var angle=i*90f;var direction=Quaternion.Euler(0,angle,0)*Vector3.forward;
                var ridge=UnityEngine.Object.Instantiate(kit.RidgeVisualPrefab,root.transform);ridge.name="ValleyBackdrop_"+i;ridge.transform.localPosition=direction*860+Vector3.up*35;
                ridge.transform.localScale=new Vector3(1.5f,2.8f,1.5f);
                ridge.transform.localRotation=Quaternion.Euler(0,angle,0);
            }
        }
        /// <summary>按最终模型高度筛选大石块；使用同一贴形网格提供实体碰撞和不可行走区域，保留可读数据供捷径重烘焙。</summary>
        private static void AddRockBlocker(GeneratedValleyContentHandle handle,Transform root,Vector3 position,Matrix4x4 pose,GameObject prefab,MeshFilter[] filters)
        {
            var parts=new List<CombineInstance>();
            float minY=float.PositiveInfinity,maxY=float.NegativeInfinity;
            foreach(var filter in filters)
            {
                var matrix=pose*prefab.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix;
                var bounds=filter.sharedMesh.bounds;
                for(int corner=0;corner<8;corner++)
                {
                    var p=matrix.MultiplyPoint3x4(bounds.center+Vector3.Scale(bounds.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1)));
                    minY=Mathf.Min(minY,p.y);maxY=Mathf.Max(maxY,p.y);
                }
                for(int sub=0;sub<filter.sharedMesh.subMeshCount;sub++)
                    parts.Add(new CombineInstance{mesh=filter.sharedMesh,subMeshIndex=sub,transform=matrix});
            }
            // 以米为单位；模型总高不超过60厘米的碎石保持可穿过，大石块包含部分埋入河床的石体。
            if(maxY-minY<=.6f)return;
            var mesh=new Mesh{name="SceneryRockCollision",indexFormat=IndexFormat.UInt32};
            handle.Meshes.Add(mesh);mesh.CombineMeshes(parts.ToArray(),true,true);mesh.RecalculateBounds();
            var go=new GameObject("SceneryRockBlocker");go.transform.SetParent(root,false);go.transform.localPosition=position;
            go.AddComponent<MeshCollider>().sharedMesh=mesh;
            handle.Sources.Add(new NavMeshBuildSource{shape=NavMeshBuildSourceShape.Mesh,sourceObject=mesh,transform=go.transform.localToWorldMatrix,area=1});
        }
        private static float Horizontal(Vector3 a,Vector3 b){a.y=b.y=0;return Vector3.Distance(a,b);}
    }
}
