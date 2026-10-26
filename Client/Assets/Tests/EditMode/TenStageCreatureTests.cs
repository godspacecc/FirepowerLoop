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
    /// <summary>四种采用版演员的实际资源、活动时钟与地图释放限定验收。</summary>
    public sealed class TenStageCreatureTests
    {
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private const string Aggregate="Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab";
        private GameObject root;
        private ArcadeCombatViewController view;
        private ArcadeRoundLayoutParameters layout;
        private static string Name(int kind)=>kind==6?"Spider":kind==10?"Ghoul":kind==17?"RedMangler":"ZRex";
        private Transform Actor=>root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="AdoptedCreatureActor");
        private void Ready(int kind)
        {
            root=new GameObject("TenStageCreatureContract");view=new ArcadeCombatViewController();
            typeof(ArcadeCombatViewController).GetField("root",Hidden).SetValue(view,root);
            typeof(ArcadeCombatViewController).GetField("mapRoot",Hidden).SetValue(view,root.transform);
            typeof(ArcadeCombatViewController).GetField("contentTemplates",Hidden).SetValue(view,AssetDatabase.LoadAssetAtPath<GameObject>(Aggregate).transform.Find("Content"));
            var tables=new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))));
            layout=tables.TbArcadeRoundLayout.DataList.Select(ArcadeRoundLayoutParameters.FromConfig).First(p=>kind==2?p.BossKind==2&&p.ArenaConfigNumber==10:p.SpecialKind==kind);
            view.ConfigureRoundBounds(19.5f,19.5f,.55f);view.SetRoundLayout(layout);
        }
        private void Spawn(int kind)
        {Ready(kind);Assert.That(view.IsRoundLayoutReady,Is.True,view.RoundLayoutLoadError);view.RecordRoundEnemyPosition(7,new Vector2(2,3));view.SetEnemyContent(7,kind==2?0:kind,kind==2?2:0,false);Assert.That(Actor,Is.Not.Null,"已采用演员仍未接入");}
        private void State(float age,float windup=0,float remaining=0,float recovery=0)
        {
            var method=typeof(ArcadeCombatViewController).GetMethod("SetAdoptedCreatureState");Assert.That(method,Is.Not.Null,"缺少权威姿态入口");
            method.Invoke(view,new object[]{7,age,Vector2.right,windup,remaining,recovery});
        }
        private Vector3 Foot(int kind)=>Actor.Find(kind==6?"Body/Leg-1_2/Joint":"Body/LegL/Foot").localPosition;
        [TearDown] public void Cleanup()
        {if(view!=null)typeof(ArcadeCombatViewController).GetMethod("ReleaseRoundContent",Hidden).Invoke(view,null);if(root!=null)UnityEngine.Object.DestroyImmediate(root);root=null;view=null;}

        [TestCase(6)][TestCase(10)][TestCase(17)][TestCase(2)]
        public void AdoptedGeometryAndClipsArePersistentAndNeverAnimateActorRoot(int kind)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/Arcade"+Name(kind)+".prefab");Assert.That(prefab,Is.Not.Null,"缺少采用版正式Prefab");
            Assert.That(prefab.GetComponentsInChildren<Collider>(true),Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<MeshFilter>(true).Length,Is.GreaterThan(30));
            foreach(var f in prefab.GetComponentsInChildren<MeshFilter>(true))Assert.That(EditorUtility.IsPersistent(f.sharedMesh),Is.True);
            foreach(var r in prefab.GetComponentsInChildren<Renderer>(true))Assert.That(EditorUtility.IsPersistent(r.sharedMaterial)&&r.sharedMaterial.shader!=null,Is.True);
            var behaviour=prefab.GetComponents<MonoBehaviour>().FirstOrDefault(b=>b.GetType().Name=="ArcadeAdoptedCreatureView");Assert.That(behaviour,Is.Not.Null);
            var clips=new SerializedObject(behaviour).FindProperty("poses");Assert.That(clips.arraySize,Is.GreaterThanOrEqualTo(5));
            for(int i=0;i<clips.arraySize;i++){var c=(AnimationClip)clips.GetArrayElementAtIndex(i).objectReferenceValue;Assert.That(c,Is.Not.Null);foreach(var b in AnimationUtility.GetCurveBindings(c))Assert.That(b.path,Is.Not.Empty);}
        }
        [TestCase(6)][TestCase(10)][TestCase(17)][TestCase(2)]
        public void ActualTravelDrivesGaitWhileStationaryAuthorityStopsFeet(int kind)
        {
            Spawn(kind);float age=layout.SkillWarning+.1f;State(age);
            view.RecordRoundEnemyPosition(7,new Vector2(2,3.13f));State(age+.1f);var first=Foot(kind);
            view.RecordRoundEnemyPosition(7,new Vector2(2,3.39f));State(age+.2f);Assert.That(Vector3.Distance(first,Foot(kind)),Is.GreaterThan(.005f));
            view.RecordRoundEnemyPosition(7,new Vector2(2,3.39f));State(age+.3f);var stopped=Foot(kind);
            view.RecordRoundEnemyPosition(7,new Vector2(2,3.39f));State(age+.4f);Assert.That(Foot(kind),Is.EqualTo(stopped));
            Assert.That(Actor.localPosition,Is.EqualTo(Vector3.zero));Assert.That(Actor.parent.localPosition,Is.EqualTo(new Vector3(2,0,3.39f)));
        }
        [TestCase(6)][TestCase(10)][TestCase(17)][TestCase(2)]
        public void LateDuplicatePausedAndOlderSnapshotsDoNotAdvanceOrRewind(int kind)
        {
            Spawn(kind);float age=layout.SkillWarning+.1f;State(age);view.RecordRoundEnemyPosition(7,new Vector2(2,3.24f));State(age+.2f,.5f,.15f,0);
            var nodes=Actor.GetComponentsInChildren<Transform>(true);var positions=nodes.Select(t=>t.localPosition).ToArray();var rotations=nodes.Select(t=>t.localRotation).ToArray();
            view.TickPlayerFeedback(1);view.RecordRoundEnemyPosition(7,new Vector2(2,3.24f));State(age+.2f,.5f,.15f,0);State(.01f);
            for(int i=0;i<nodes.Length;i++){Assert.That(nodes[i].localPosition,Is.EqualTo(positions[i]));Assert.That(nodes[i].localRotation,Is.EqualTo(rotations[i]));}
            Assert.That(Vector3.Distance(Actor.parent.forward,Vector3.right),Is.LessThan(.001f));
        }
        [TestCase(6)][TestCase(10)]
        public void RealMeleeWindupAndRecoveryUseAcceptedAttack(int kind)
        {
            Spawn(kind);float age=layout.SkillWarning+.3f;State(age);var hand=Actor.Find(kind==6?"Body/Head/FangR":"Body/ArmR/Hand");var idle=hand.localRotation;var idlePosition=hand.localPosition;
            State(age+.1f,.5f,.05f,0);Assert.That(Quaternion.Angle(idle,hand.localRotation)+Vector3.Distance(idlePosition,hand.localPosition),Is.GreaterThan(.03f));
            var strike=hand.localPosition;var strikeRotation=hand.localRotation;State(age+.2f,.5f,0,.1f);Assert.That(Quaternion.Angle(strikeRotation,hand.localRotation)+Vector3.Distance(strike,hand.localPosition),Is.GreaterThan(.03f));
        }
        [Test] public void RedManglerCannonRecoilOccursAtRealSkillBoundary()
        {
            Spawn(17);State(layout.SkillWarning-.01f);var recoil=Actor.Find("Body/ArmR/Hand/BarrelRecoil");Assert.That(recoil.localPosition.z,Is.EqualTo(0).Within(.001f));
            State(layout.SkillWarning+.04f);Assert.That(recoil.localPosition.z,Is.LessThan(-.035f));State(layout.SkillWarning+.8f);Assert.That(recoil.localPosition.z,Is.EqualTo(0).Within(.001f));
        }
        [Test] public void RexThreeRuleCyclesIncludeChargeAndBothExpelActions()
        {
            Spawn(2);State(layout.SkillWarning*.7f);var jaw=Actor.Find("Body/Core/Head/Jaw");var warning=jaw.localRotation;
            State(layout.SkillWarning+.2f);Assert.That(Quaternion.Angle(warning,jaw.localRotation),Is.GreaterThan(3));
            State(layout.SkillPeriod+layout.SkillWarning+.1f);Assert.That(Mathf.DeltaAngle(0,jaw.localEulerAngles.x),Is.GreaterThan(20));
            State(2*layout.SkillPeriod+layout.SkillWarning+.1f);Assert.That(Mathf.DeltaAngle(0,jaw.localEulerAngles.x),Is.GreaterThan(20));
        }
        [TestCase(17)][TestCase(2)]
        public void MovingUpperActionsKeepBothArmsAttachedToCoreShoulders(int kind)
        {
            Spawn(kind);var core=Actor.Find("Body/Core");
            for(int i=0;i<160;i++)
            {
                float age=i/20f,travel=Mathf.Min(age,2);
                view.RecordRoundEnemyPosition(7,new Vector2(2,3+travel));State(age);
                foreach(int side in new[]{-1,1})
                {
                    var arm=Actor.Find("Body/Arm"+(side<0?"L":"R")+"/Upper");
                    var start=arm.localPosition-arm.localRotation*Vector3.up*arm.localScale.y*.5f;
                    var anchor=kind==17?new Vector3(side*.47f,.50f,0):new Vector3(side*.59f,.34f,.73f);
                    var shoulder=core.localPosition+core.localRotation*anchor;
                    Assert.That(Vector3.Distance(start,shoulder),Is.LessThan(.0003f),"移动与上身动作组合后肩关节断开："+kind+" phase="+i+" side="+side);
                }
            }
        }
        [TestCase(6)][TestCase(10)][TestCase(17)][TestCase(2)]
        public void MissingTemplateRecoveryRemovalAndMapReleaseKeepSharedArtAlive(int kind)
        {
            Ready(kind);var field=typeof(ArcadeCombatViewController).GetField("contentTemplates",Hidden);var template=field.GetValue(view);field.SetValue(view,null);Assert.That(view.IsRoundLayoutReady,Is.False);
            view.RecordRoundEnemyPosition(7,new Vector2(2,3));view.SetEnemyContent(7,kind==2?0:kind,kind==2?2:0,false);Assert.That(Actor,Is.Null);
            field.SetValue(view,template);view.SetEnemyContent(7,kind==2?0:kind,kind==2?2:0,false);Assert.That(Actor,Is.Not.Null);var mesh=Actor.GetComponentInChildren<MeshFilter>().sharedMesh;
            State(3);view.RemoveRoundEnemyContent(7);Assert.That(Actor,Is.Null);view.SetEnemyContent(7,kind==2?0:kind,kind==2?2:0,false);State(.1f);Assert.That(Actor,Is.Not.Null);
            typeof(ArcadeCombatViewController).GetMethod("ReleaseRoundContent",Hidden).Invoke(view,null);Assert.That(root.transform.childCount,Is.Zero);Assert.That(mesh!=null&&EditorUtility.IsPersistent(mesh),Is.True);
        }
    }
}
