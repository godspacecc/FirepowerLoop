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
        private ArcadeCombatViewController view;
        private GameObject root;
        private IResourceLoader previousLoader;
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private ArcadeRoundLayoutParameters[] Layouts()=>new Tables(name=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",name+".bytes")))).TbArcadeRoundLayout.DataList.Select(ArcadeRoundLayoutParameters.FromConfig).ToArray();
        private object Field(string name)=>typeof(ArcadeCombatViewController).GetField(name,Hidden).GetValue(view);
        [SetUp] public void Setup(){previousLoader=ResourceLoader.Current;ResourceLoader.Use(new FormalAssetLoader());view=new ArcadeCombatViewController();root=new GameObject("RoundContentTest");typeof(ArcadeCombatViewController).GetField("root",Hidden).SetValue(view,root);view.ConfigureRoundBounds(19.5f,19.5f,.55f);}
        [TearDown] public void Cleanup(){typeof(ArcadeCombatViewController).GetMethod("ReleaseRoundContent",Hidden).Invoke(view,null);if(root!=null)UnityEngine.Object.DestroyImmediate(root);ResourceLoader.Use(previousLoader);}
        [Test] public void EveryLayoutBuildsDistinctEnvironmentWithoutActiveCollidersAndReleasesResources()
        {
            var tables=new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))));
            var stages=tables.TbArcadeEarlyStage.DataList.Select(ArcadeEarlyStageParameters.FromConfig).ToArray();
            foreach(var p in Layouts())
            {
                var stage=stages.FirstOrDefault(x=>x.ArenaConfigNumber==p.ArenaConfigNumber);typeof(ArcadeCombatViewController).GetField("earlyStage",Hidden).SetValue(view,stage);
                view.ConfigureRoundBounds(stage?.HalfWidth??19.5f,stage?.HalfHeight??19.5f,.55f);
                view.SetRoundLayout(p);Assert.That(view.IsRoundLayoutReady,Is.True,view.RoundLayoutLoadError+" "+p.Id);
                Assert.That(root.GetComponentsInChildren<Collider>(true).Length,Is.Zero,p.Id);
                if(stage!=null)
                {
                    Assert.That(root.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="ConfirmedEarlyEnvironment"),Is.True,p.Id);
                    Assert.That(root.GetComponentsInChildren<Renderer>(true).Length,Is.GreaterThan(0),p.Id);
                }
                else if(p.ArenaConfigNumber!=1)
                {
                    Assert.That(root.transform.Find("RoundContent_"+p.Id+"/Environment_"+p.ThemeKey+"/ThemeDecor/ThemeGround"),Is.Not.Null,p.Id);
                    Assert.That(root.GetComponentsInChildren<Renderer>(true).Length,Is.GreaterThan(p.Obstacles.Count),p.Id);
                    foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))Assert.That(renderer.sharedMaterial.shader,Is.Not.Null,p.Id);
                }
            }
            typeof(ArcadeCombatViewController).GetMethod("ReleaseRoundContent",Hidden).Invoke(view,null);
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
        [Test] public void SpecialEnemyProjectsAuthoritativePositionHealthAndRemoval()
        {
            view.SetRoundLayout(Layouts().First(p=>p.ArenaConfigNumber==3));
            view.RecordRoundEnemyPosition(7,new Vector2(3,-2));view.SetEnemyContent(7,6,0,true);
            Assert.That(view.HasRoundEnemyContent(7),Is.True);
            var enemy=root.GetComponentsInChildren<Transform>().Single(t=>t.name=="Spider_7");
            Assert.That(enemy.localPosition,Is.EqualTo(new Vector3(3,0,-2)));
            view.SetRoundEnemyHealth(7,25,100);
            Assert.That(enemy.Find("HealthFill").localScale.x,Is.EqualTo(.2875f).Within(.001f));
            view.RecordRoundEnemyPosition(7,new Vector2(5,1));Assert.That(enemy.localPosition,Is.EqualTo(new Vector3(5,0,1)));
            view.RemoveRoundEnemyContent(7);Assert.That(view.HasRoundEnemyContent(7),Is.False);
            Assert.That(((ICollection)Field("roundOwnedMeshes")).Count,Is.Zero);
        }
        [Test] public void AuthoritativeAreasReplaceWarningAndReleaseRemovedMeshes()
        {
            view.SetRoundLayout(Layouts().First(p=>p.ArenaConfigNumber==14));
            view.SetRoundContent(2,new[]{new ArcadeContentAreaSnapshot(1,4,3,5,2,true)});
            Assert.That(((ICollection)Field("roundOwnedMeshes")).Count,Is.EqualTo(1));
            var area=root.GetComponentsInChildren<Transform>().Single(t=>t.name=="Area_1_Kind_4");
            Assert.That(area.localPosition,Is.EqualTo(new Vector3(3,.03f,5)));
            view.SetRoundContent(3,new[]{new ArcadeContentAreaSnapshot(1,4,3,5,2,false)});
            Assert.That(((ICollection)Field("roundOwnedMeshes")).Count,Is.EqualTo(1));
            view.SetRoundContent(4,Array.Empty<ArcadeContentAreaSnapshot>());
            Assert.That(((ICollection)Field("roundOwnedMeshes")).Count,Is.Zero);
            Assert.That(((ICollection)Field("roundAreas")).Count,Is.Zero);
        }
        [Test] public void DiagonalSegmentsProjectExactCapsuleAndReplaceOwnedBoundaryWithoutLeaks()
        {
            view.SetRoundLayout(Layouts().First(p=>p.ArenaConfigNumber==14));
            view.SetRoundContent(1,new[]{new ArcadeContentAreaSnapshot(8,4,1,2,.75f,true)});
            var circle=root.GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="ExactDamageBoundary").sharedMesh;
            view.SetRoundContent(2,new[]{new ArcadeContentAreaSnapshot(8,4,1,2,.75f,true,7,10,true)});
            Assert.That(circle==null,Is.True,"旧圆区网格必须释放");
            var capsule=root.GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="ExactSegmentBoundary");
            Assert.That(capsule.transform.parent.localPosition,Is.EqualTo(new Vector3(4,.03f,6)));
            Assert.That(Vector3.Distance(capsule.transform.parent.forward,new Vector3(.6f,0,.8f)),Is.LessThan(.001f));
            Assert.That(capsule.sharedMesh.bounds.size.x,Is.EqualTo(1.5f).Within(.001f));
            Assert.That(capsule.sharedMesh.bounds.size.z,Is.EqualTo(11.5f).Within(.001f),"长度十米并保留两端各0.75米端帽");
            var oldMesh=capsule.sharedMesh;
            view.SetRoundContent(3,new[]{new ArcadeContentAreaSnapshot(8,4,2,4,.75f,true,8,12,true)});
            Assert.That(capsule.sharedMesh,Is.SameAs(oldMesh),"只平移不能重新分配网格");
            view.SetRoundContent(4,new[]{new ArcadeContentAreaSnapshot(8,4,0,0,.75f,false,0,4,true)});
            Assert.That(oldMesh==null,Is.True,"长度变化必须替换并释放旧网格");
            Assert.That(((ICollection)Field("roundOwnedMeshes")).Count,Is.EqualTo(1));
            view.SetRoundContent(5,Array.Empty<ArcadeContentAreaSnapshot>());
            Assert.That(((ICollection)Field("roundOwnedMeshes")).Count,Is.Zero);
            view.ConfigureRoundBounds(25,22,.55f);view.SetRoundLayout(Layouts().First(p=>p.ArenaConfigNumber==14));
            var ground=root.GetComponentsInChildren<Transform>().Single(t=>t.name=="ThemeGround");
            Assert.That(ground.lossyScale.x,Is.EqualTo(50).Within(.001f));
            Assert.That(ground.lossyScale.z,Is.EqualTo(44).Within(.001f));
        }
        [Test] public void BarrageRoofBarrelFallsOnAuthoritativeWarningClockAndReleasesAtImpact()
        {
            var layout=Layouts().First(p=>p.ArenaConfigNumber==3&&p.RoundNumber==1);
            Assert.That(layout.EventKind,Is.EqualTo(3),"正式Barrage落桶事件编号");
            view.SetRoundLayout(layout);
            var warning=new[]{new ArcadeContentAreaSnapshot(-100,3,3,-2,layout.EventRadius,true)};
            view.SetRoundContent(0,warning);
            var roof=root.GetComponentsInChildren<Transform>().Single(t=>t.name=="BuildingRoof");
            var barrel=root.GetComponentsInChildren<Transform>().SingleOrDefault(t=>t.name=="RoofFallingBarrel_-100");
            Assert.That(barrel,Is.Not.Null,"Barrage预警缺少从屋顶落向目标的可见桶体");
            var start=barrel.position;
            Assert.That(start.y,Is.GreaterThan(roof.GetComponent<Renderer>().bounds.max.y));
            Assert.That(start.z,Is.EqualTo(roof.position.z).Within(.001f),"落桶必须来自主题屋顶");
            Assert.That(barrel.GetComponentsInChildren<Renderer>().Length,Is.GreaterThanOrEqualTo(3),"桶身及上下金属箍构成专用轮廓");
            Assert.That(barrel.GetComponentsInChildren<Collider>(true).Length,Is.Zero,"表现不增加伤害碰撞体");
            view.SetRoundContent(layout.EventWarning*.5,warning);
            var middle=barrel.position;
            Assert.That(middle.y,Is.LessThan(start.y));
            Assert.That(Mathf.Abs(middle.x-3),Is.LessThan(Mathf.Abs(start.x-3)));
            Assert.That(Mathf.Abs(middle.z+2),Is.LessThan(Mathf.Abs(start.z+2)));
            view.SetRoundContent(layout.EventWarning*.5,warning);
            Assert.That(barrel.position,Is.EqualTo(middle),"权威时钟不变时暂停落桶");
            view.SetRoundContent(layout.EventWarning,new[]{new ArcadeContentAreaSnapshot(-100,3,3,-2,layout.EventRadius,false)});
            Assert.That(barrel==null,Is.True,"活动边界移除空中桶体");
            Assert.That(root.GetComponentsInChildren<Transform>().Any(t=>t.name=="DangerCore"),Is.True,"落点沿用现有爆炸危险范围");
            view.SetRoundContent(0,new[]{new ArcadeContentAreaSnapshot(7,3,3,-2,layout.EventRadius,true)});
            Assert.That(root.GetComponentsInChildren<Transform>().Any(t=>t.name.StartsWith("RoofFallingBarrel_")),Is.False,"敌人爆桶区域不能误触发屋顶落桶");
            view.SetRoundContent(0,warning);
            var replacement=root.GetComponentsInChildren<Transform>().Single(t=>t.name=="RoofFallingBarrel_-100");
            view.SetRoundLayout(Layouts().First(p=>p.ArenaConfigNumber==14));
            Assert.That(replacement==null,Is.True,"切图释放桶体及所属根");
        }
        [Test] public void DroppedVortexBarrelShowsPurpleSmokeAndReleasesWithAuthoritySnapshot()
        {
            view.SetRoundLayout(Layouts().First(p=>p.ArenaConfigNumber==3));
            var snapshot=new[]{new ArcadeContentAreaSnapshot(-10002,13,0,0,.45f,false)};
            view.SetRoundContent(0,snapshot);
            var barrel=root.GetComponentsInChildren<Transform>().SingleOrDefault(t=>t.name=="GroundBarrel_-10002");
            Assert.That(barrel,Is.Not.Null,"动态桶必须显示桶体，不能显示为普通伤害圈");
            var smoke=barrel.Find("VortexSmoke");Assert.That(smoke,Is.Not.Null);
            view.SetRoundContent(1,snapshot);var sample=smoke.localPosition;
            view.SetRoundContent(1,snapshot);Assert.That(smoke.localPosition,Is.EqualTo(sample),"暂停只采样同一权威时刻");
            Assert.That(root.GetComponentsInChildren<Collider>(true),Is.Empty);
            view.SetRoundContent(1,Array.Empty<ArcadeContentAreaSnapshot>());Assert.That(barrel==null,Is.True);
        }

        [Test] public void GroundBarrelsProjectOnlyRemainingAuthoritySnapshotsWithoutCollisionOrDangerRings()
        {
            var layout=Layouts().First(p=>p.ArenaConfigNumber==3&&p.RoundNumber==1);
            Assert.That(layout.Barrels.Count,Is.EqualTo(2));view.SetRoundLayout(layout);
            var areas=layout.Barrels.Select((p,index)=>new ArcadeContentAreaSnapshot(-10000-index,11,p.X,p.Y,.45f,false)).ToArray();
            view.SetRoundContent(0,areas);
            var barrels=root.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("GroundBarrel_")).ToArray();
            Assert.That(barrels.Length,Is.EqualTo(2),"只按服务器剩余桶快照显示专用桶体");
            var removed=barrels.Single(t=>t.name=="GroundBarrel_-10000");
            Assert.That(root.GetComponentsInChildren<Collider>(true).Length,Is.Zero,"可射桶不增加阻挡或客户端伤害碰撞体");
            Assert.That(root.GetComponentsInChildren<Transform>().Any(t=>t.name=="ExactDamageBoundary"||t.name=="DangerCore"),Is.False,"未引爆地面桶不能冒充危险范围");
            view.SetRoundContent(99,areas.Skip(1).ToArray());
            Assert.That(removed==null,Is.True,"权威移除立即释放桶体");
            Assert.That(root.GetComponentsInChildren<Transform>().Count(t=>t.name.StartsWith("GroundBarrel_")),Is.EqualTo(1));
            view.SetRoundContent(99,areas.Skip(1).ToArray());
            Assert.That(root.GetComponentsInChildren<Transform>().Count(t=>t.name.StartsWith("GroundBarrel_")),Is.EqualTo(1),"重复快照不能补发已移除的桶");
            view.SetRoundLayout(Layouts().First(p=>p.ArenaConfigNumber==14));
            Assert.That(root.GetComponentsInChildren<Transform>().Any(t=>t.name.StartsWith("GroundBarrel_")),Is.False,"切图释放旧桶表现");
        }
    }
}
