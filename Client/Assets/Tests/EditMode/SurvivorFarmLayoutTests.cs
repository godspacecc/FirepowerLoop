using System;
using System.Collections;
using System.IO;
using System.Linq;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>采用版农场配置、实际图形阻挡足迹及真实收集的限定检查。</summary>
    public sealed class SurvivorFarmLayoutTests
    {
        private static Tables Tables()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))) ;
        private static SurvivorConfiguration Config()=>SurvivorConfiguration.FromTables(Tables());
        private static ArcadeRoundLayoutParameters Layout()
        {
            var property=typeof(SurvivorConfiguration).GetProperty("Layout");Assert.That(property,Is.Not.Null,"幸存者必须拥有独立的采用版地图布局");
            return (ArcadeRoundLayoutParameters)property.GetValue(Config());
        }
        [Test] public void ApprovedFarmUsesOwnLayoutAndEnvironment()
        {
            var p=typeof(SurvivorConfiguration).GetProperty("EnvironmentPrefabAddress");Assert.That(p,Is.Not.Null);
            Assert.That(p.GetValue(Config()),Is.EqualTo("Prefabs/SceneObjects/SurvivorFarmVisual"));
            Assert.That(Layout().IsWalkable(0,0,.5f,30.5f,30.5f),Is.True,"旧十字围墙必须从幸存者通路移除");
        }
        [Test] public void StoneWallsBlockMovementAndBulletsWhileGapsConnectFarm()
        {
            var layout=Layout();Assert.That(layout.FitsArena(30.5f,30.5f,.5f),Is.True);
            Assert.That(layout.IsWalkable(-24,5,.5f,30.5f,30.5f),Is.False);
            Assert.That(layout.Obstacles.Any(o=>o.BlocksProjectiles&&Mathf.Abs(o.X+24)<o.HalfWidth&&Mathf.Abs(o.Y-5)<o.HalfHeight),Is.True);
            Assert.That(layout.IsWalkable(-12,5,.5f,30.5f,30.5f),Is.True);
            var obstacles=layout.Obstacles.Select(o=>new ArcadeObstacleDefinition(o.X,o.Y,o.HalfWidth,o.HalfHeight,o.BlocksProjectiles)).ToArray();
            var nav=new SurvivorNavigation(30.5f,30.5f,.5f,obstacles);nav.SetTarget(new Vector2(layout.SpawnX,layout.SpawnY));
            foreach(var p in new[]{new Vector2(-12,12),new Vector2(12,12),new Vector2(-12,-12),new Vector2(12,-12)})Assert.That(nav.IsReachable(p),Is.True,p.ToString());
        }
        [Test] public void OrdinaryFarmRetainsOriginalCrossWalls()
        {
            var row=Tables().TbArcadeRoundLayout.Get("arena.22.round.1");var original=ArcadeRoundLayoutParameters.FromConfig(row);
            Assert.That(original.IsWalkable(0,0,.5f,30.5f,30.5f),Is.False);Assert.That(original.Obstacles.Count,Is.EqualTo(50));
        }
        [Test] public void FormalFarmIsStaticAndFootprintsMatchEveryAuthorityObstacle()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/SurvivorFarmVisual.prefab");Assert.That(prefab,Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<Collider>(true),Is.Empty);Assert.That(prefab.GetComponentsInChildren<MonoBehaviour>(true),Is.Empty);Assert.That(prefab.GetComponentsInChildren<Animator>(true),Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Renderer>(true).Length,Is.InRange(1,24),"静态几何按材质合并，不能逐块石头保留绘制对象");
            var footprints=prefab.transform.Find("BlockingFootprints");Assert.That(footprints,Is.Not.Null);var layout=Layout();Assert.That(footprints.childCount,Is.EqualTo(layout.Obstacles.Count));
            for(int i=0;i<footprints.childCount;i++){
                var point=footprints.GetChild(i);var obstacle=layout.Obstacles[i];
                Assert.That(point.localPosition.x,Is.EqualTo(obstacle.X).Within(.001f));Assert.That(point.localPosition.z,Is.EqualTo(obstacle.Y).Within(.001f));
                Assert.That(point.localScale.x*.5f,Is.EqualTo(obstacle.HalfWidth).Within(.001f));Assert.That(point.localScale.z*.5f,Is.EqualTo(obstacle.HalfHeight).Within(.001f));
            }
        }
        [Test] public void BarnsHayAndTreeTrunksHaveRealFootprints()
        {
            var p=Layout();foreach(var point in new[]{new Vector2(-23,20),new Vector2(20,21),new Vector2(-24,13),new Vector2(22,-15),new Vector2(-22,-24)})
                Assert.That(p.IsWalkable(point.x,point.y,.5f,30.5f,30.5f),Is.False,point.ToString());
        }
        /// <summary>采用版实际请求地址和新布局表必须进入真实收集，不能只凭AssetDatabase存在。</summary>
        [Test] public void FarmAddressAndLayoutTableAreCollectedForEntry()
        {
            var setting=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/AssetBundleCollectorSetting.asset");
            object Read(object o,string key)=>o.GetType().GetProperty(key).GetValue(o);
            var result=setting.GetType().GetMethod("BeginCollect").Invoke(setting,new object[]{"DefaultPackage",true,false});
            var assets=((IEnumerable)Read(result,"CollectAssets")).Cast<object>().ToArray();
            foreach(var pair in new[]{(Config().EnvironmentPrefabAddress,"arcade"),("tbsurvivormaplayout.bytes","startup")}){
                var asset=assets.SingleOrDefault(a=>(string)Read(a,"Address")==pair.Item1);Assert.That(asset,Is.Not.Null,pair.Item1);
                Assert.That(((IEnumerable)Read(asset,"AssetTags")).Cast<string>(),Does.Contain(pair.Item2));
            }
        }
        /// <summary>规则与实际CharacterController接触使用同一份已采用足迹，不能只改视觉。</summary>
        [Test] public void RuntimeObstacleCollidersUseDedicatedFarmFootprints()
        {
            var root=new GameObject("FarmCollisionCheck");var view=new ArcadeCombatViewController();var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            try{
                var provider=new LubanRuntimeConfigProvider(Tables);Assert.That(provider.TryGetArcadeCatalog(out var catalog,out var error),Is.True,error);
                typeof(ArcadeCombatViewController).GetField("root",flags).SetValue(view,root);typeof(ArcadeCombatViewController).GetField("earlyStage",flags).SetValue(view,catalog.EarlyStages.Single(p=>p.ArenaConfigNumber==22));
                var layout=Layout();var obstacles=layout.Obstacles.Select(o=>new ArcadeObstacleDefinition(o.X,o.Y,o.HalfWidth,o.HalfHeight,o.BlocksProjectiles)).ToArray();
                typeof(ArcadeCombatViewController).GetMethod("CreateArenaCollision",flags).Invoke(view,new object[]{22,30.5f,30.5f,obstacles});
                for(int i=0;i<obstacles.Length;i++){
                    var c=root.transform.Find("Obstacle_"+(i+1)).GetComponent<BoxCollider>();var o=obstacles[i];Assert.That(c.enabled,Is.True);
                    Assert.That(c.transform.localPosition.x,Is.EqualTo(o.Center.x));Assert.That(c.transform.localPosition.z,Is.EqualTo(o.Center.y));Assert.That(c.size.x,Is.EqualTo(o.HalfExtents.x*2));Assert.That(c.size.z,Is.EqualTo(o.HalfExtents.y*2));
                }
            }finally{UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
