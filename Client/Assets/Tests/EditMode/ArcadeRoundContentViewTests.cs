using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
namespace Game.Tests
{
    /// <summary>隔离正式视图验证纯投影和独占资源；不启动战斗、场景加载或玩家档。</summary>
    public sealed class ArcadeRoundContentViewTests
    {
        private SurvivorCombatViewController view;
        private GameObject root;
        private IResourceLoader previousLoader;
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private ArcadeRoundLayoutParameters[] Layouts()=>new Tables(name=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",name+".bytes")))).TbArcadeRoundLayout.DataList.Select(ArcadeRoundLayoutParameters.FromConfig).ToArray();
        private object Field(string name)=>typeof(SurvivorCombatViewController).GetField(name,Hidden).GetValue(view);
        [SetUp] public void Setup(){previousLoader=ResourceLoader.Current;ResourceLoader.Use(new FormalAssetLoader());view=new SurvivorCombatViewController();root=new GameObject("RoundContentTest");typeof(SurvivorCombatViewController).GetField("root",Hidden).SetValue(view,root);typeof(SurvivorCombatViewController).GetField("contentTemplates",Hidden).SetValue(view,AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab").transform.Find("Content"));view.ConfigureRoundBounds(19.5f,19.5f,.55f);}
        [TearDown] public void Cleanup(){typeof(SurvivorCombatViewController).GetMethod("ReleaseRoundContent",Hidden).Invoke(view,null);if(root!=null)UnityEngine.Object.DestroyImmediate(root);ResourceLoader.Use(previousLoader);}
        [Test] public void EveryLayoutBuildsDistinctEnvironmentWithoutActiveCollidersAndReleasesResources()
        {
            var tables=new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))));
            var stages=tables.TbArcadeEarlyStage.DataList.Select(ArcadeEarlyStageParameters.FromConfig).ToArray();
            foreach(var p in Layouts())
            {
                var stage=stages.FirstOrDefault(x=>x.ArenaConfigNumber==p.ArenaConfigNumber);typeof(SurvivorCombatViewController).GetField("earlyStage",Hidden).SetValue(view,stage);
                view.ConfigureRoundBounds(stage?.HalfWidth??19.5f,stage?.HalfHeight??19.5f,.55f);
                view.SetRoundLayout(p);Assert.That(view.IsRoundLayoutReady,Is.True,view.RoundLayoutLoadError+" "+p.Id);
                Assert.That(root.GetComponentsInChildren<Collider>(true).Length,Is.Zero,p.Id);
                if(stage!=null)
                {
                    Assert.That(root.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="ConfirmedEarlyEnvironment"),Is.True,p.Id);
                    Assert.That(root.GetComponentsInChildren<Renderer>(true).Length,Is.GreaterThan(0),p.Id);
                }
                else
                {
                    Assert.That(root.transform.Find("RoundContent_"+p.Id+"/Environment_"+p.ThemeKey+"/ThemeDecor/ThemeGround"),Is.Not.Null,p.Id);
                    Assert.That(root.GetComponentsInChildren<Renderer>(true).Length,Is.GreaterThan(p.Obstacles.Count),p.Id);
                    foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))Assert.That(renderer.sharedMaterial.shader,Is.Not.Null,p.Id);
                }
            }
            typeof(SurvivorCombatViewController).GetMethod("ReleaseRoundContent",Hidden).Invoke(view,null);
            Assert.That(((ICollection)Field("roundOwnedMeshes")).Count,Is.Zero);
            Assert.That(((ICollection)Field("roundOwnedMaterials")).Count,Is.Zero);
            Assert.That(root.transform.childCount,Is.Zero);
        }
        private sealed class FormalAssetLoader:IResourceLoader
        {
            public T LoadAsset<T>(string path) where T:UnityEngine.Object=>AssetDatabase.LoadAssetAtPath<T>("Assets/Game/"+path+".prefab");
            public ResourceLoadHandle LoadAssetAsync<T>(string path,Action<T> loaded) where T:UnityEngine.Object{loaded(LoadAsset<T>(path));return ResourceLoadHandle.Empty;}
            public void Release(ResourceLoadHandle handle){}
        }
    }
}
