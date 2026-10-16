using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>隔离正式视图验证纯投影和独占资源；不启动战斗、场景加载或玩家档。</summary>
    public sealed class ArcadeRoundContentViewTests
    {
        private ArcadeCombatViewController view;
        private GameObject root;
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private ArcadeRoundLayoutParameters[] Layouts()=>new Tables(name=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",name+".bytes")))).TbArcadeRoundLayout.DataList.Select(ArcadeRoundLayoutParameters.FromConfig).ToArray();
        private object Field(string name)=>typeof(ArcadeCombatViewController).GetField(name,Hidden).GetValue(view);
        [SetUp] public void Setup(){view=new ArcadeCombatViewController();root=new GameObject("RoundContentTest");typeof(ArcadeCombatViewController).GetField("root",Hidden).SetValue(view,root);view.ConfigureRoundBounds(19.5f,19.5f,.55f);}
        [TearDown] public void Cleanup(){typeof(ArcadeCombatViewController).GetMethod("ReleaseRoundContent",Hidden).Invoke(view,null);if(root!=null)UnityEngine.Object.DestroyImmediate(root);}
        [Test] public void EveryLayoutBuildsDistinctEnvironmentWithoutActiveCollidersAndReleasesResources()
        {
            foreach(var p in Layouts())
            {
                view.SetRoundLayout(p);Assert.That(view.IsRoundLayoutReady,Is.True,view.RoundLayoutLoadError+" "+p.Id);
                Assert.That(root.GetComponentsInChildren<Collider>(true).Length,Is.Zero,p.Id);
                if(p.ArenaConfigNumber!=1)
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
    }
}
