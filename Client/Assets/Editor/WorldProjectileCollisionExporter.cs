using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Editor
{
    /// <summary>从正式场景和碰撞资源导出服务器几何，不保存场景、不执行打包；遇到未知形状明确失败。</summary>
    public static class WorldProjectileCollisionExporter
    {
        /// <summary>正式资源导出入口，双端产物从同一数据复制，独立于 Player Build。</summary>
        public static string ExportOnlineMaps()
        {
            var repository=Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
            var output=Path.Combine(repository,"Config/WorldCollision");
            var result=new List<string>();
            foreach(var pair in new[]{("map.another-day.survival-sample","SurvivalSampleMap"),("map.another-day.mission-sample","TaskSampleMap"),("map.hejia-village","GeneratedMaps/HeJiaVillageLocalPreview/HeJiaVillageLocalPreview")})
                result.Add(ExportMap(pair.Item1,"Assets/Game/Scenes/"+pair.Item2+".unity",output));
            var server=Path.Combine(repository,"Server/GameServer/Config/WorldCollision");Directory.CreateDirectory(server);
            foreach(var file in Directory.GetFiles(output,"*.json"))File.Copy(file,Path.Combine(server,Path.GetFileName(file)),true);
            AssetDatabase.Refresh();return string.Join("\n",result);
        }
        public static WorldProjectileShape[] CaptureCollider(Collider collider,Transform root)=>Capture(collider,root,false);
        private static WorldCollisionVector V(Vector3 p)=>new WorldCollisionVector(p.x,p.y,p.z);
        private static WorldProjectileShape[] Capture(Collider collider,Transform root,bool includeTriggers)
        {
            if(!collider.enabled||(!includeTriggers&&collider.isTrigger)||!EnabledWithinRoot(collider.transform,root))return Array.Empty<WorldProjectileShape>();
            var matrix=root.worldToLocalMatrix*collider.transform.localToWorldMatrix;
            WorldCollisionVector Point(Vector3 p)=>V(matrix.MultiplyPoint3x4(p));
            WorldCollisionVector Axis(Vector3 p)=>V(matrix.MultiplyVector(p));
            if(collider is BoxCollider box)return new[]{new WorldProjectileShape{Kind=WorldCollisionShapeKind.Box,Center=Point(box.center),AxisX=Axis(Vector3.right*box.size.x*.5f),AxisY=Axis(Vector3.up*box.size.y*.5f),AxisZ=Axis(Vector3.forward*box.size.z*.5f)}};
            var rootScale=root.lossyScale;
            if((collider is SphereCollider||collider is CapsuleCollider||collider is CharacterController)&&
               (Mathf.Abs(Mathf.Abs(rootScale.x)-Mathf.Abs(rootScale.y))>.0001f||Mathf.Abs(Mathf.Abs(rootScale.y)-Mathf.Abs(rootScale.z))>.0001f))
                throw new NotSupportedException("球/胶囊在非等比地图根下不能精确导出："+collider.name);
            float sx=Axis(Vector3.right).Length,sy=Axis(Vector3.up).Length,sz=Axis(Vector3.forward).Length;
            if(collider is SphereCollider sphere)return new[]{new WorldProjectileShape{Kind=WorldCollisionShapeKind.Sphere,Center=Point(sphere.center),Radius=sphere.radius*Math.Max(sx,Math.Max(sy,sz))}};
            if(collider is CapsuleCollider capsule)return new[]{Capsule(capsule.center,capsule.direction,capsule.height,capsule.radius,Point,Axis,sx,sy,sz)};
            if(collider is CharacterController controller)return new[]{Capsule(controller.center,1,controller.height,controller.radius,Point,Axis,sx,sy,sz)};
            var result=new List<WorldProjectileShape>();
            void Triangle(Vector3 a,Vector3 b,Vector3 c){result.Add(new WorldProjectileShape{Kind=WorldCollisionShapeKind.Triangle,A=Point(a),B=Point(b),C=Point(c)});}
            if(collider is MeshCollider mesh)
            {
                if(mesh.convex)throw new NotSupportedException("Convex MeshCollider 必须先导出实际凸包，不能用原网格替代："+collider.name);
                if(mesh.sharedMesh==null)throw new InvalidOperationException("MeshCollider 缺少 Mesh："+collider.name);
                var vertices=mesh.sharedMesh.vertices;var indices=mesh.sharedMesh.triangles;
                for(int i=0;i<indices.Length;i+=3)Triangle(vertices[indices[i]],vertices[indices[i+1]],vertices[indices[i+2]]);
                return result.ToArray();
            }
            if(collider is TerrainCollider terrain)
            {
                var data=terrain.terrainData;if(data==null)throw new InvalidOperationException("缺少 TerrainData");int n=data.heightmapResolution;var heights=data.GetHeights(0,0,n,n);var size=data.size;
                var samples=new float[n*n];var holes=new List<int>();
                for(int z=0;z<n;z++)for(int x=0;x<n;x++)samples[z*n+x]=heights[z,x];
                for(int z=0;z<n-1;z++)for(int x=0;x<n-1;x++)if(data.IsHole(x,z))holes.Add(z*(n-1)+x);
                return new[]{new WorldProjectileShape{Kind=WorldCollisionShapeKind.Heightfield,A=Point(Vector3.zero),AxisX=Axis(Vector3.right*size.x),AxisY=Axis(Vector3.up*size.y),AxisZ=Axis(Vector3.forward*size.z),HeightmapResolution=n,Heights=samples,HoleCells=holes.ToArray()}};
            }
            throw new NotSupportedException("未支持的场景碰撞体："+collider.GetType().Name+" "+collider.name);
        }
        private static WorldProjectileShape Capsule(Vector3 center,int direction,float height,float radius,Func<Vector3,WorldCollisionVector> point,Func<Vector3,WorldCollisionVector> axis,float sx,float sy,float sz)
        {
            var unit=direction==0?Vector3.right:direction==1?Vector3.up:Vector3.forward;
            float scale=direction==0?sx:direction==1?sy:sz;
            float r=radius*(direction==0?Math.Max(sy,sz):direction==1?Math.Max(sx,sz):Math.Max(sx,sy));
            var c=point(center);var offset=axis(unit).Normalized*Math.Max(0,height*scale*.5f-r);
            return new WorldProjectileShape{Kind=WorldCollisionShapeKind.Capsule,Center=c,A=c-offset,B=c+offset,Radius=r};
        }
        /// <summary>临时加法打开磁盘场景，读取完立即关闭；同场景已打开时拒绝，避免误用或丢弃用户编辑状态。</summary>
        public static string ExportMap(string mapId,string sceneAssetPath,string outputDirectory)
        {
            var loaded=SceneManager.GetSceneByPath(sceneAssetPath);if(loaded.IsValid()&&loaded.isLoaded)throw new InvalidOperationException("请先关闭待导出地图的编辑场景："+sceneAssetPath);
            Scene active=SceneManager.GetActiveScene();var scene=EditorSceneManager.OpenScene(sceneAssetPath,OpenSceneMode.Additive);
            try
            {
                var contexts=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MapContext>(true)).ToArray();
                if(contexts.Length!=1)throw new InvalidOperationException("导出地图必须有一个 MapContext："+sceneAssetPath);
                var context=contexts[0];if(!context.Validate(out var error))throw new InvalidOperationException(error);
                var root=context.MapContentRoot.transform;var shapes=new List<WorldProjectileShape>();
                // 部分正式地图的地表与 MapContentRoot 同属场景根，必须读整场景再统一换算坐标。
                foreach(var c in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Collider>(true)))
                {
                    if(c.transform.IsChildOf(context.DynamicObjectRoot))continue;
                    var captured=CaptureCollider(c,root);var source=GlobalObjectId.GetGlobalObjectIdSlow(c).ToString();
                    for(int i=0;i<captured.Length;i++)captured[i].SourceIdentity=source+":"+i;
                    shapes.AddRange(captured);
                }
                if(shapes.Count==0)throw new InvalidOperationException("地图没有可验证的静态碰撞几何："+mapId);
                long id=1;foreach(var shape in shapes)shape.Id=id++;
                var profiles=new List<WorldProjectileCollisionProfile>();var dependencyPaths=new List<string>{sceneAssetPath};
                foreach(var pair in new[]{("player.graybox.default","Characters/GrayboxPlayer"),("enemy.graybox.basic","Characters/GrayboxEnemy"),("enemy.graybox.archer","Characters/GrayboxArcher"),("projectile.arrow.basic","Combat/GrayboxArrow")})
                {
                    var path="Assets/Game/Prefabs/"+pair.Item2+".prefab";dependencyPaths.Add(path);
                    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(prefab==null)throw new InvalidOperationException("缺少碰撞资源："+path);
                    var colliders=prefab.GetComponentsInChildren<Collider>(true).Where(c=>c is BoxCollider||c is CharacterController).ToArray();
                    var captured=colliders.SelectMany(c=>Capture(c,prefab.transform,true)).ToArray();
                    if(captured.Length==0)throw new InvalidOperationException("缺少角色/箭碰撞体："+path);
                    // Prefab 根自身的缩放需要保留，场景坐标转换则已经消除了地图根的缩放。
                    var scale=prefab.transform.localScale;foreach(var shape in captured){shape.Center=Scale(shape.Center,scale);shape.AxisX=Scale(shape.AxisX,scale);shape.AxisY=Scale(shape.AxisY,scale);shape.AxisZ=Scale(shape.AxisZ,scale);shape.A=Scale(shape.A,scale);shape.B=Scale(shape.B,scale);shape.C=Scale(shape.C,scale);shape.Radius*=Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z));}
                    profiles.Add(new WorldProjectileCollisionProfile{ConfigId=pair.Item1,Shapes=captured});
                }
                var body=profiles[0].Shapes[0];body.Bounds(out var min,out var max);
                // 角色包围体对角线界定近身发射空间，配合从身体中心到枪口的遮挡检查，不能越墙出生。
                var data=new WorldProjectileCollisionData{MapId=mapId,Shapes=shapes.ToArray(),Profiles=profiles.ToArray(),MaxOriginDistance=Math.Max(min.Length,max.Length)};
                var deps=string.Join("\n",dependencyPaths.Select(p=>p+"="+AssetDatabase.GetAssetDependencyHash(p)));
                using(var sha=SHA256.Create())data.ContentHash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(data)+deps))).Replace("-","").ToLowerInvariant();
                Directory.CreateDirectory(outputDirectory);File.WriteAllText(Path.Combine(outputDirectory,mapId+".json"),JsonUtility.ToJson(data),new UTF8Encoding(false));
                var index="Assets/Game/Config/WorldCollision";Directory.CreateDirectory(index);
                File.WriteAllText(Path.Combine(index,mapId+".json"),JsonUtility.ToJson(new WorldProjectileCollisionIndex{Version=data.Version,MapId=mapId,ContentHash=data.ContentHash}),new UTF8Encoding(false));
                return mapId+": shapes="+data.Shapes.Length+", hash="+data.ContentHash;
            }
            finally{EditorSceneManager.CloseScene(scene,true);if(active.IsValid()&&active.isLoaded)SceneManager.SetActiveScene(active);}
        }
        private static WorldCollisionVector Scale(WorldCollisionVector p,Vector3 s)=>new WorldCollisionVector(p.X*s.x,p.Y*s.y,p.Z*s.z);
        private static bool EnabledWithinRoot(Transform target,Transform root)
        {
            // 地图内容根由地图加载事务激活，磁盘上的根停用不能把正式地形当作不存在。
            for(var t=target;t!=null&&t!=root;t=t.parent)if(!t.gameObject.activeSelf)return false;
            return true;
        }
    }
}
