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
    /// <summary>已采用终局小龙资源、权威姿态投影与地图生命周期的限定验收。</summary>
    public sealed class FinalDragonPresentationTests
    {
        private const string Prefab="Assets/Game/Prefabs/Characters/ArcadeFinalDragon.prefab";
        private const string Aggregate="Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab";
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private GameObject root;
        private ArcadeCombatViewController view;
        private ArcadeBossDefinition boss=new ArcadeBossDefinition("test", "",4,1,.8f,2,1,1,5,1.2f,5,1);
        private void Ready()
        {
            root=new GameObject("FinalDragonContract");view=new ArcadeCombatViewController();
            typeof(ArcadeCombatViewController).GetField("root",Hidden).SetValue(view,root);
            typeof(ArcadeCombatViewController).GetField("mapRoot",Hidden).SetValue(view,root.transform);
            typeof(ArcadeCombatViewController).GetField("contentTemplates",Hidden).SetValue(view,AssetDatabase.LoadAssetAtPath<GameObject>(Aggregate).transform.Find("Content"));
            var tables=new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))));
            var layout=tables.TbArcadeRoundLayout.DataList.Select(ArcadeRoundLayoutParameters.FromConfig).First(p=>p.BossKind==4);
            view.ConfigureRoundBounds(19.5f,19.5f,.55f);view.SetRoundLayout(layout);
        }
        private Transform Actor=>root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="FinalDragonActor");
        private void Spawn(){Ready();Assert.That(view.IsRoundLayoutReady,Is.True,view.RoundLayoutLoadError);view.RecordRoundEnemyPosition(7,new Vector2(2,3));view.SetEnemyContent(7,0,4,false);Assert.That(Actor,Is.Not.Null,"终局仍是旧轮廓");}
        private void State(float age,ArcadeBossPhase phase,float remaining,Vector2 facing)
        {var method=typeof(ArcadeCombatViewController).GetMethod("SetFinalDragonState");Assert.That(method,Is.Not.Null);method.Invoke(view,new object[]{7,age,phase,remaining,boss,facing});}
        [TearDown] public void Cleanup(){if(view!=null)typeof(ArcadeCombatViewController).GetMethod("ReleaseRoundContent",Hidden).Invoke(view,null);if(root!=null)UnityEngine.Object.DestroyImmediate(root);view=null;root=null;}
        [Test] public void FormalModelKeepsApprovedGeometryAndPersistentReferences()
        {
            var p=AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);Assert.That(p,Is.Not.Null,"已采用小龙尚未正式保存");
            Assert.That(p.GetComponentsInChildren<Collider>(true),Is.Empty);Assert.That(p.transform.Find("Body/WingL"),Is.Not.Null);Assert.That(p.transform.Find("Body/Neck/Head/Jaw"),Is.Not.Null);
            var filters=p.GetComponentsInChildren<MeshFilter>(true);Assert.That(filters.Sum(f=>f.sharedMesh.triangles.Length/3),Is.EqualTo(3808));
            foreach(var f in filters)Assert.That(EditorUtility.IsPersistent(f.sharedMesh),Is.True);
            foreach(var r in p.GetComponentsInChildren<Renderer>(true))Assert.That(EditorUtility.IsPersistent(r.sharedMaterial)&&r.sharedMaterial.shader!=null,Is.True);
        }
        [Test] public void FormalCurvesNeverAnimateCombatOrActorRoot()
        {
            var clips=AssetDatabase.FindAssets("t:AnimationClip",new[]{"Assets/Game/Art/Enemies/Creatures/FinalDragon"}).Select(g=>AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
            Assert.That(clips.Length,Is.EqualTo(8));foreach(var clip in clips)foreach(var b in AnimationUtility.GetCurveBindings(clip))Assert.That(b.path,Is.Not.Empty);
        }
        [Test] public void AuthorityStagesSampleImmediatelyAndNeverMoveRoot()
        {
            Spawn();var actor=Actor;var jaw=actor.Find("Body/Neck/Head/Jaw");State(2,ArcadeBossPhase.Telegraph,.5f,Vector2.right);var neck=actor.Find("Body/Neck").localRotation;
            State(3,ArcadeBossPhase.RayTelegraph,.3f,Vector2.right);Assert.That(Quaternion.Angle(neck,actor.Find("Body/Neck").localRotation),Is.GreaterThan(10));Assert.That(jaw.localEulerAngles.x,Is.GreaterThan(15));
            State(4,ArcadeBossPhase.Charging,.4f,Vector2.right);Assert.That(actor.localPosition,Is.EqualTo(Vector3.zero));Assert.That(actor.parent.localPosition,Is.EqualTo(new Vector3(2,0,3)));Assert.That(Vector3.Distance(actor.parent.forward,Vector3.right),Is.LessThan(.001f));
        }
        [Test] public void DuplicatePausedAndOldSnapshotsCannotAdvanceOrRewind()
        {
            Spawn();State(4,ArcadeBossPhase.Charging,.4f,Vector2.right);var body=Actor.Find("Body");var p=body.localPosition;var q=Actor.Find("Body/Neck").localRotation;
            view.TickPlayerFeedback(1);State(4,ArcadeBossPhase.Charging,.4f,Vector2.right);Assert.That(body.localPosition,Is.EqualTo(p));State(1,ArcadeBossPhase.Telegraph,1,Vector2.left);Assert.That(body.localPosition,Is.EqualTo(p));Assert.That(Actor.Find("Body/Neck").localRotation,Is.EqualTo(q));
        }
        [Test] public void WalkingUsesActualTravelAndStoppedAuthorityStopsGait()
        {
            Spawn();State(2,ArcadeBossPhase.None,0,Vector2.up);view.RecordRoundEnemyPosition(7,new Vector2(2,3.12f));State(2.1f,ArcadeBossPhase.None,0,Vector2.up);var foot=Actor.Find("ForeLFoot");var p=foot.localPosition;
            view.RecordRoundEnemyPosition(7,new Vector2(2,3.3f));State(2.2f,ArcadeBossPhase.None,0,Vector2.up);Assert.That(Vector3.Distance(p,foot.localPosition),Is.GreaterThan(.01f));
            view.RecordRoundEnemyPosition(7,new Vector2(2,3.3f));State(2.3f,ArcadeBossPhase.None,0,Vector2.up);Assert.That(foot.localPosition.y,Is.EqualTo(.13f).Within(.002f));
        }
        [Test] public void MissingTemplateWaitsAndRecoversWithoutOrphans()
        {
            Ready();var field=typeof(ArcadeCombatViewController).GetField("contentTemplates",Hidden);var formal=field.GetValue(view);field.SetValue(view,null);Assert.That(view.IsRoundLayoutReady,Is.False);view.RecordRoundEnemyPosition(7,new Vector2(2,3));view.SetEnemyContent(7,0,4,false);Assert.That(Actor,Is.Null);
            field.SetValue(view,formal);Assert.That(view.IsRoundLayoutReady,Is.True);view.SetEnemyContent(7,0,4,false);Assert.That(Actor.parent.localPosition,Is.EqualTo(new Vector3(2,0,3)));
        }
        [Test] public void RepeatedMovingSnapshotKeepsFrozenWalkingPose()
        {
            Spawn();State(2,ArcadeBossPhase.None,0,Vector2.up);view.RecordRoundEnemyPosition(7,new Vector2(2,3.18f));State(2.2f,ArcadeBossPhase.None,0,Vector2.up);
            var foot=Actor.Find("ForeLFoot");var p=foot.localPosition;var body=Actor.Find("Body").localRotation;
            view.RecordRoundEnemyPosition(7,new Vector2(2,3.18f));State(2.2f,ArcadeBossPhase.None,0,Vector2.up);
            Assert.That(foot.localPosition,Is.EqualTo(p));Assert.That(Actor.Find("Body").localRotation,Is.EqualTo(body));
        }
        [Test] public void RayRecoveryClosesJawAndSpreadsWingsWhileTravelContinues()
        {
            Spawn();State(5,ArcadeBossPhase.RayTelegraph,.1f,Vector2.up);
            view.RecordRoundEnemyPosition(7,new Vector2(2,3.1f));State(5.3f,ArcadeBossPhase.None,0,Vector2.up);
            var jaw=Actor.Find("Body/Neck/Head/Jaw");Assert.That(jaw.localEulerAngles.x,Is.GreaterThan(20),"射线后不能立即跳回闭口待机");
            view.RecordRoundEnemyPosition(7,new Vector2(2,3.6f));State(6.3f,ArcadeBossPhase.None,0,Vector2.up);Assert.That(jaw.localEulerAngles.x,Is.LessThan(7));
            view.RecordRoundEnemyPosition(7,new Vector2(2,3.8f));State(7.2f,ArcadeBossPhase.None,0,Vector2.up);
            Assert.That(Actor.Find("Body/Neck").localEulerAngles.y,Is.GreaterThan(10),"追击期间仍应完成已采用展翼恢复");
            Assert.That(Actor.parent.localPosition,Is.EqualTo(new Vector3(2,0,3.8f)));
        }
        [Test] public void RemovalAndMapReleaseKeepPersistentArtAlive()
        {
            Spawn();var mesh=Actor.GetComponentInChildren<MeshFilter>().sharedMesh;view.RemoveRoundEnemyContent(7);Assert.That(Actor,Is.Null);Assert.That(mesh!=null,Is.True);
            view.SetEnemyContent(7,0,4,false);typeof(ArcadeCombatViewController).GetMethod("ReleaseRoundContent",Hidden).Invoke(view,null);Assert.That(root.transform.childCount,Is.Zero);Assert.That(mesh!=null&&EditorUtility.IsPersistent(mesh),Is.True);
        }
    }
}
