using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>采用版四足演员的真实资源和快照投影；不启动整局或自行裁决战斗。</summary>
    public sealed class CreaturePresentationTests
    {
        private GameObject root;
        private ArcadeCombatViewController view;
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private static string Address(int kind)=>"Assets/Game/Prefabs/Characters/Arcade"+(kind==4?"Burrower":"Hound")+".prefab";
        private static ArcadeRoundLayoutParameters Layout(int kind)=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))).TbArcadeRoundLayout.DataList.Select(ArcadeRoundLayoutParameters.FromConfig).First(p=>p.ArenaConfigNumber==kind+1&&p.RoundNumber==1);
        private void Ready(int kind)
        {
            root=new GameObject("CreatureContract");view=new ArcadeCombatViewController();
            typeof(ArcadeCombatViewController).GetField("root",Hidden).SetValue(view,root);
            typeof(ArcadeCombatViewController).GetField("mapRoot",Hidden).SetValue(view,root.transform);
            typeof(ArcadeCombatViewController).GetField("contentTemplates",Hidden).SetValue(view,AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab").transform.Find("Content"));
            view.ConfigureRoundBounds(19.5f,19.5f,.55f);view.SetRoundLayout(Layout(kind));Assert.That(view.IsRoundLayoutReady,Is.True,view.RoundLayoutLoadError);
        }
        private void State(int id,float age,Vector2 facing)
        {
            var m=typeof(ArcadeCombatViewController).GetMethod("SetCreatureState");Assert.That(m,Is.Not.Null,"正式表现缺少权威年龄入口");m.Invoke(view,new object[]{id,age,facing});
        }
        private Transform Node(string name)=>root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==name);
        private Transform Spawn(int kind)
        {Ready(kind);view.RecordRoundEnemyPosition(7,Vector2.zero);view.SetEnemyContent(7,kind,0,false);return Node(kind==4?"Burrower_7":"Hound_7");}
        [TearDown] public void Cleanup()
        {if(view!=null)typeof(ArcadeCombatViewController).GetMethod("ReleaseRoundContent",Hidden).Invoke(view,null);if(root!=null)UnityEngine.Object.DestroyImmediate(root);view=null;root=null;}

        [TestCase(4)][TestCase(5)] public void FormalActorsHaveArticulatedPawsJawAndPersistentSharedResources(int kind)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Address(kind));Assert.That(prefab,Is.Not.Null,"缺少已采用四足正式Prefab");
            Assert.That(prefab.GetComponentsInChildren<Collider>(true),Is.Empty);
            Assert.That(prefab.GetComponents<MonoBehaviour>().Any(v=>v.GetType().Name=="ArcadeCreatureView"),Is.True);
            for(int i=0;i<4;i++)foreach(string part in new[]{"Upper","Lower","Ankle","Paw"})Assert.That(prefab.transform.Find("Visual/Leg"+i+"/"+part),Is.Not.Null);
            Assert.That(prefab.transform.Find("Visual/Body/Neck/Head/Jaw"),Is.Not.Null);
            foreach(var mf in prefab.GetComponentsInChildren<MeshFilter>(true))Assert.That(EditorUtility.IsPersistent(mf.sharedMesh),Is.True,"不能遗留临时网格");
            foreach(var r in prefab.GetComponentsInChildren<Renderer>(true))foreach(var m in r.sharedMaterials)Assert.That(EditorUtility.IsPersistent(m)&&m.shader!=null,Is.True);
        }
        [TestCase(4)][TestCase(5)] public void LateAgeDirectlySamplesAndDuplicateOrOlderAgeCannotRewind(int kind)
        {
            var actor=Spawn(kind);Assert.That(actor.Find("CreatureActor"),Is.Not.Null,"仍在使用旧几何替身");
            State(7,kind==4?.55f:1.6f,Vector2.up);var visual=Node("Visual");var p=visual.localPosition;var head=Node("Head").localRotation;
            State(7,kind==4?.55f:1.6f,Vector2.up);Assert.That(visual.localPosition,Is.EqualTo(p));
            view.TickPlayerFeedback(1f);Assert.That(visual.localPosition,Is.EqualTo(p),"表现Tick不得推进技能年龄");
            State(7,.1f,Vector2.left);Assert.That(visual.localPosition,Is.EqualTo(p));Assert.That(Node("Head").localRotation,Is.EqualTo(head));
            Assert.That(actor.localPosition,Is.EqualTo(Vector3.zero),"技能采样不得写战斗根位置");
        }
        [TestCase(4)][TestCase(5)] public void ActualTravelMovesFourLegGaitAndStoppedSnapshotsStopRunning(int kind)
        {
            var actor=Spawn(kind);State(7,3.5f,Vector2.up);view.RecordRoundEnemyPosition(7,new Vector2(0,.07f));State(7,3.55f,Vector2.up);var paw=Node("Leg0").Find("Paw");var first=paw.localPosition;
            view.RecordRoundEnemyPosition(7,new Vector2(0,.18f));State(7,3.6f,Vector2.up);Assert.That(Vector3.Distance(first,paw.localPosition),Is.GreaterThan(.005f));
            view.RecordRoundEnemyPosition(7,new Vector2(0,.18f));State(7,3.65f,Vector2.up);var stopped=paw.localPosition;
            view.RecordRoundEnemyPosition(7,new Vector2(0,.18f));State(7,4.1f,Vector2.up);Assert.That(paw.localPosition,Is.EqualTo(stopped));
            Assert.That(actor.localPosition,Is.EqualTo(new Vector3(0,0,.18f)));Assert.That(root.GetComponentsInChildren<Collider>(true),Is.Empty);
        }
        [Test] public void HoundWarningAndPounceKeepAuthoritativeFacingInsteadOfLocalPlayer()
        {
            var actor=Spawn(5);State(7,1.0f,Vector2.right);Assert.That(Vector3.Distance(actor.forward,Vector3.right),Is.LessThan(.001f));
            view.RecordRoundEnemyPosition(7,new Vector2(0,1));State(7,1.6f,Vector2.right);Assert.That(Vector3.Distance(actor.forward,Vector3.right),Is.LessThan(.001f));
            Assert.That(Node("Visual").localPosition.y,Is.GreaterThan(.05f),"活动段应直接采样扑出高度");
        }
        [Test] public void BurrowerUndergroundOnlyShowsEarthAndEmergesBeforeActiveWindow()
        {
            var actor=Spawn(4);State(7,.55f,Vector2.up);Assert.That(Node("Visual").localPosition.y,Is.LessThan(-.7f));Assert.That(Node("Earth"),Is.Not.Null);
            State(7,1.2f,Vector2.up);Assert.That(Node("Visual").localPosition.y,Is.EqualTo(0).Within(.001f));Assert.That(actor.localPosition,Is.EqualTo(Vector3.zero));
        }
        [TestCase(4)][TestCase(5)] public void MeleeWindupAndRecoveryDriveJawWithoutReplacingAuthorityRoot(int kind)
        {
            var actor=Spawn(kind);State(7,kind==4?3.2f:1.6f,Vector2.up);var jaw=Node("Jaw");var before=jaw.localRotation;
            view.SetEnemyMelee(7,.4f,.3f,0,Vector2.up);Assert.That(Quaternion.Angle(before,jaw.localRotation),Is.GreaterThan(4));
            var p=actor.localPosition;view.SetEnemyMelee(7,.4f,0,.15f,Vector2.up);Assert.That(actor.localPosition,Is.EqualTo(p));
        }
        [Test] public void MissingCreatureTemplatesWaitWithoutOrphanNodesAndRecoverFromCurrentSnapshot()
        {
            Ready(4);var field=typeof(ArcadeCombatViewController).GetField("contentTemplates",Hidden);var formal=field.GetValue(view);
            field.SetValue(view,null);Assert.That(view.IsRoundLayoutReady,Is.False,"场地不能早于演员依赖报告就绪");
            view.RecordRoundEnemyPosition(7,new Vector2(2,3));view.SetEnemyContent(7,4,0,true);view.SetEnemyContent(7,4,0,true);Assert.That(Node("Burrower_7"),Is.Null);
            var missing=new GameObject("MissingCreatureContent");
            try
            {
                field.SetValue(view,missing.transform);Assert.That(view.IsRoundLayoutReady,Is.False);Assert.That(view.RoundLayoutLoadError,Does.Contain("四足"));
                view.SetEnemyContent(7,4,0,true);Assert.That(Node("Burrower_7"),Is.Null);
            }
            finally{UnityEngine.Object.DestroyImmediate(missing);field.SetValue(view,formal);}
            Assert.That(view.IsRoundLayoutReady,Is.True);view.SetEnemyContent(7,4,0,true);State(7,.55f,Vector2.up);
            Assert.That(Node("Burrower_7").localPosition,Is.EqualTo(new Vector3(2,0,3)));Assert.That(Node("Visual").localPosition.y,Is.LessThan(-.7f));
        }
        [Test] public void KindReplacementRemovalAndMapReleaseDestroyActorsButKeepSharedMeshes()
        {
            Spawn(4);Assert.That(Node("CreatureActor"),Is.Not.Null,"正式演员未生成");var body=Node("CreatureActor").gameObject;var mesh=body.GetComponentInChildren<MeshFilter>().sharedMesh;
            view.SetEnemyContent(7,5,0,false);Assert.That(body==null,Is.True);Assert.That(Node("Hound_7").Find("CreatureActor"),Is.Not.Null);
            view.RemoveRoundEnemyContent(7);Assert.That(Node("CreatureActor"),Is.Null);Assert.That(mesh!=null&&EditorUtility.IsPersistent(mesh),Is.True);
            view.RecordRoundEnemyPosition(8,Vector2.zero);view.SetEnemyContent(8,5,0,false);typeof(ArcadeCombatViewController).GetMethod("ReleaseRoundContent",Hidden).Invoke(view,null);Assert.That(root.transform.childCount,Is.Zero);Assert.That(mesh!=null,Is.True);
        }
        [Test] public void FormalAnimationDoesNotMoveActorRootAndFeetKeepGroundContact()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Address(5));Assert.That(prefab,Is.Not.Null);var actor=UnityEngine.Object.Instantiate(prefab);actor.transform.position=new Vector3(3,0,-2);
            try
            {
                var component=actor.GetComponents<MonoBehaviour>().First(c=>c.GetType().Name=="ArcadeCreatureView");var sample=component.GetType().GetMethod("SamplePose");
                for(int i=0;i<24;i++)
                {sample.Invoke(component,new object[]{"Move",i/48f});Assert.That(actor.transform.position,Is.EqualTo(new Vector3(3,0,-2)));Assert.That(actor.transform.Find("Visual/Leg0/Paw").GetComponent<Renderer>().bounds.min.y,Is.GreaterThanOrEqualTo(-.003f));}
            }
            finally{UnityEngine.Object.DestroyImmediate(actor);}
        }
        [Test] public void ClientSnapshotKeepsAuthoritativeAgeAndLockedFacing()
        {
            var wire=new ET.OnlineArcadeBattleInfo();var enemy=new ET.OnlineArcadeEnemyInfo{RuntimeId=77,SpecialKind=5,CurrentHealth=25,MaxHealth=100};
            foreach(var entry in new[]{("ContentAge",3.4f),("ContentFacingX",.6f),("ContentFacingZ",.8f)})
            {var property=enemy.GetType().GetProperty(entry.Item1);Assert.That(property,Is.Not.Null,"协议缺少"+entry.Item1);property.SetValue(enemy,entry.Item2);}
            wire.Enemies.Add(enemy);var bytes=MemoryPack.MemoryPackSerializer.Serialize(wire);var decoded=MemoryPack.MemoryPackSerializer.Deserialize<ET.OnlineArcadeBattleInfo>(bytes);
            var copy=(OnlineArcadeBattleSnapshot)typeof(OnlineArcadeRunSystem).GetMethod("CopyBattle",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{decoded});
            var snapshot=copy.Enemies.Single();
            foreach(var entry in new[]{("ContentAge",3.4f),("ContentFacingX",.6f),("ContentFacingZ",.8f)})
            {var property=typeof(OnlineArcadeEnemySnapshot).GetProperty(entry.Item1);Assert.That(property,Is.Not.Null,"快照缺少"+entry.Item1);Assert.That((float)property.GetValue(snapshot),Is.EqualTo(entry.Item2).Within(.00001f));}
        }
    }
}
