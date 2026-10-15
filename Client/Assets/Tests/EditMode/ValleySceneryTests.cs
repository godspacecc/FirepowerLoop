using System;
using System.Linq;
using System.Reflection;
using Game;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>验证河谷布景保持保存布局，大石块同时提供实体与导航阻挡，并由地图句柄回收。</summary>
    public sealed class ValleySceneryTests
    {
        private const string KitPath="Assets/Game/Prefabs/Environment/CivilisationValleyGenerationKit.prefab";
        [Test] public void RiverMesh_ContainsShaderTriangleNeighbours()
        {
            var root=new GameObject("RiverShaderContract");root.SetActive(false);var context=root.AddComponent<MapContext>();
            var layout=new ValleyLayoutSaveData();layout.River.Add(ValleyPosition.From(Vector3.zero));layout.River.Add(ValleyPosition.From(new Vector3(0,1,20)));
            var handle=new GeneratedValleyContentHandle(new Original(context),null,layout);
            typeof(GeneratedValleyContentHandle).GetField("GeneratedRoot",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(handle,root);
            try
            {
                typeof(ValleyMapBuilder).GetMethod("BuildRiver",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{handle,AssetDatabase.LoadAssetAtPath<GameObject>(KitPath).GetComponent<ValleyGenerationKit>(),layout});
                var mesh=root.GetComponentsInChildren<MeshFilter>(true).Single(m=>m.name.StartsWith("RiverWater_")).sharedMesh;
                Assert.That(mesh.vertexCount,Is.EqualTo(6),"水面Shader需要独立三角形邻点，不接受普通四点UV平面");
                var uv=new System.Collections.Generic.List<Vector4>();mesh.GetUVs(0,uv);
                Assert.That(uv.Count,Is.EqualTo(mesh.vertexCount));Assert.That(mesh.colors.Length,Is.EqualTo(mesh.vertexCount));
                for(int i=0;i<6;i++)Assert.That(new Vector3(uv[i].x,uv[i].y,uv[i].z),Is.EqualTo(mesh.vertices[(i/3)*3+(i+1)%3]));
            }
            finally{handle.ReleaseGeneratedAsync();if(root!=null)UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void SceneryPalette_IsFormalAndHasNoGameplayComponents()
        {
            var data=new SerializedObject(AssetDatabase.LoadAssetAtPath<GameObject>(KitPath).GetComponent<ValleyGenerationKit>());
            foreach(var name in new[]{"sceneryRockPrefab","sceneryGrassPrefab","sceneryShelterPrefab","coniferVisualPrefab","ridgeVisualPrefab"})
            {
                var property=data.FindProperty(name);Assert.That(property,Is.Not.Null,name);
                var prefab=property.objectReferenceValue as GameObject;Assert.That(prefab,Is.Not.Null,name);
                Assert.That(prefab.GetComponentsInChildren<Collider>(true),Is.Empty,name);
                Assert.That(prefab.GetComponentsInChildren<MonoBehaviour>(true),Is.Empty,name);
                if(name=="sceneryRockPrefab"||name=="sceneryGrassPrefab")Assert.That(prefab.GetComponentsInChildren<MeshFilter>(true).All(m=>m.sharedMesh.isReadable),Is.True,"运行时合批源必须可读："+name);
                Assert.That(AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(prefab),true).Any(p=>p.StartsWith("Assets/Art/")||p.Contains("/ArtSample/")),Is.False,name);
            }
        }
        [Test] public void PartialSceneryPalette_IsRejectedBeforeVisibleMutation()
        {
            var root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(KitPath));
            try{var data=new SerializedObject(root.GetComponent<ValleyGenerationKit>());var property=data.FindProperty("sceneryRockPrefab");Assert.That(property,Is.Not.Null);property.objectReferenceValue=null;data.ApplyModifiedPropertiesWithoutUndo();Assert.That(root.GetComponent<ValleyGenerationKit>().Validate(out _),Is.False);}
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void Dressing_IsDeterministicPreservesLayoutAndReleasesMeshes()
        {
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/"+n+".bytes").bytes)));
            Assert.That(provider.TryGetCivilisationCatalog(out var catalog,out _),Is.True);Assert.That(provider.TryGetValleyGenerationProfile(MapIds.CivilisationValley,out var profile,out _),Is.True);
            Assert.That(ValleyLayoutGenerator.TryGenerate(37,0,profile,catalog,out var layout,out var error),Is.True,error);
            var before=Newtonsoft.Json.JsonConvert.SerializeObject(layout);var kit=AssetDatabase.LoadAssetAtPath<GameObject>(KitPath).GetComponent<ValleyGenerationKit>();
            var type=typeof(ValleyMapBuilder).Assembly.GetType("Game.ValleySceneryBuilder");Assert.That(type,Is.Not.Null,"尚未实现样片布景装配");
            var roots=new[]{new GameObject("SceneryTestA"),new GameObject("SceneryTestB")};var handles=roots.Select(r=>new GeneratedValleyContentHandle(new Original(r.AddComponent<MapContext>()),null,layout)).ToArray();
            var generatedField=typeof(GeneratedValleyContentHandle).GetField("GeneratedRoot",BindingFlags.Instance|BindingFlags.NonPublic);
            try
            {
                for(int i=0;i<roots.Length;i++){roots[i].SetActive(false);generatedField.SetValue(handles[i],roots[i]);type.GetMethod("Build",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{handles[i],kit});}
                var meshes=roots.Select(r=>r.GetComponentsInChildren<MeshFilter>(true).Select(m=>m.sharedMesh).Where(m=>!AssetDatabase.Contains(m)).ToArray()).ToArray();
                Assert.That(meshes[0].Length,Is.GreaterThan(0));Assert.That(meshes[0].Length,Is.EqualTo(meshes[1].Length));
                for(int i=0;i<meshes[0].Length;i++)Assert.That(meshes[0][i].vertices,Is.EqualTo(meshes[1][i].vertices));
                var blockers=roots[0].GetComponentsInChildren<MeshCollider>(true);
                Assert.That(blockers.Length,Is.GreaterThan(0),"大石块必须产生实体阻挡");
                var otherBlockers=roots[1].GetComponentsInChildren<MeshCollider>(true);
                Assert.That(otherBlockers.Length,Is.EqualTo(blockers.Length));
                var sources=(System.Collections.Generic.List<UnityEngine.AI.NavMeshBuildSource>)typeof(GeneratedValleyContentHandle).GetField("Sources",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(handles[0]);
                Assert.That(sources.Count,Is.EqualTo(blockers.Length),"每个实体石块都必须参与寻路烘焙");
                for(int i=0;i<blockers.Length;i++)
                {
                    Assert.That(blockers[i].isTrigger,Is.False);
                    Assert.That(blockers[i].sharedMesh.bounds.size.y,Is.GreaterThan(.6f),"小碎石不阻挡");
                    Assert.That(blockers[i].sharedMesh.vertices,Is.EqualTo(otherBlockers[i].sharedMesh.vertices));
                    Assert.That(sources[i].sourceObject,Is.SameAs(blockers[i].sharedMesh));
                    Assert.That(sources[i].area,Is.EqualTo(1));
                    Assert.That(blockers[i].transform.localPosition,Is.EqualTo(otherBlockers[i].transform.localPosition));
                }
                roots[0].SetActive(true);Physics.SyncTransforms();
                foreach(var blocker in blockers)
                {
                    var bounds=blocker.bounds;
                    Assert.That(blocker.Raycast(new Ray(bounds.center+Vector3.up*(bounds.extents.y+1),Vector3.down),out _,bounds.size.y+2),Is.True,"实体碰撞必须命中石体");
                }
                var collisionMeshes=blockers.Select(c=>c.sharedMesh).ToArray();
                Assert.That(roots[0].GetComponentsInChildren<CivilisationPointView>(true),Is.Empty);
                Assert.That(Newtonsoft.Json.JsonConvert.SerializeObject(layout),Is.EqualTo(before));
                handles[0].ReleaseGeneratedAsync();Assert.That(meshes[0].All(m=>m==null),Is.True,"运行时网格必须随候选释放");
                Assert.That(collisionMeshes.All(m=>m==null),Is.True,"碰撞网格必须随候选释放");
            }
            finally{foreach(var handle in handles)handle.ReleaseGeneratedAsync();foreach(var root in roots)if(root!=null)UnityEngine.Object.DestroyImmediate(root);}
        }
        private sealed class Original:IMapContentHandle
        {public Original(MapContext context){Context=context;}public string MapId=>MapIds.CivilisationValley;public MapContext Context{get;}public bool IsValid=>Context!=null;}
    }
}
