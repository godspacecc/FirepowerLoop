using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>验证正式冲刺表现的方向、独立角色与生命周期，不改变战斗规则。</summary>
    public sealed class ArcadeDashVisualTests
    {
        private GameObject root;
        private object effect;
        private Type type;
        [SetUp] public void Setup()
        {
            type=typeof(ArcadeCombatViewController).Assembly.GetType("Game.ArcadeDashPresentation");
            Assert.NotNull(type,"缺少独立冲刺表现");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/Arcade/VFX/Dash/Dash.prefab");
            Assert.NotNull(prefab);
            root=new GameObject("DashTestRoot");
            effect=Activator.CreateInstance(type,root.transform,prefab.transform);
        }
        private void Call(string method,params object[] args)=>type.GetMethod(method,BindingFlags.Public|BindingFlags.Instance).Invoke(effect,args);
        private void Set(long id,Vector2 position,Vector2 direction,float remaining,bool visible=true)=>Call("Set",id,position,direction,remaining,visible);
        [TearDown] public void Cleanup(){(effect as IDisposable)?.Dispose();if(root!=null)Object.DestroyImmediate(root);}
        [Test] public void DirectionStaysLockedAndRepeatedSnapshotsDoNotRestartBurst()
        {
            Set(0,new Vector2(2,3),Vector2.right,.28f);Call("Tick",.1f);
            var instance=root.transform.GetChild(0);
            Assert.Less(Vector3.Distance(instance.forward,Vector3.right),.001f);
            Set(0,new Vector2(3,3),Vector2.up,.18f);
            Assert.False(instance.Find("Burst").gameObject.activeSelf);
            Assert.Less(Vector3.Distance(instance.forward,Vector3.right),.001f);
            Assert.AreEqual(new Vector3(3,.12f,3),instance.localPosition);
        }
        [Test] public void TwoPlayersHaveIndependentEndAndHiddenPlayerClearsImmediately()
        {
            Set(0,Vector2.zero,Vector2.right,.28f);Set(9,Vector2.one,Vector2.left,.28f);
            Call("Tick",.1f);Set(0,Vector2.zero,Vector2.right,0f);Call("Tick",.18f);
            Assert.False(root.transform.GetChild(0).gameObject.activeSelf);
            Set(9,Vector2.one,Vector2.left,.1f,false);
            Assert.False(root.transform.GetChild(1).gameObject.activeSelf);
        }
        [Test] public void PausedTimeFreezesAndMissingSnapshotsCannotLeavePermanentTrail()
        {
            Set(0,Vector2.zero,Vector2.up,.28f);Call("Tick",.04f);
            var burst=root.transform.GetChild(0).Find("Burst");var scale=burst.localScale;
            Call("Tick",0f);Assert.AreEqual(scale,burst.localScale);
            Call("Tick",1f);Assert.False(root.transform.GetChild(0).gameObject.activeSelf);
            Set(0,Vector2.zero,Vector2.up,.28f);
            Assert.False(root.transform.GetChild(0).gameObject.activeSelf,"相同旧状态不能复活已结束的尾迹");
        }
        [Test] public void NewDashReusesSlotAndRemovalDisposesOwnedObjects()
        {
            Set(0,Vector2.zero,Vector2.up,.28f);Call("Tick",.3f);Set(0,Vector2.zero,Vector2.up,0f);
            Set(0,Vector2.one,Vector2.right,.28f);
            Assert.AreEqual(1,root.transform.childCount);Assert.True(root.transform.GetChild(0).Find("Burst").gameObject.activeSelf);
            Call("Remove",0L);Assert.AreEqual(0,root.transform.childCount);
        }
        [Test] public void EndingLeavesDetachedFragmentsInsteadOfFullSpeedBands()
        {
            Set(0,Vector2.zero,Vector2.up,.28f);Call("Tick",.34f);
            var instance=root.transform.GetChild(0);
            Assert.False(instance.Find("Ribbons").gameObject.activeSelf,"结束后不应保留完整速度带");
            Assert.True(instance.Find("Fragments").gameObject.activeSelf);
            Call("Tick",.2f);Assert.False(instance.gameObject.activeSelf);
        }
        [Test] public void FormalTemplateHasNoGameplayOrAutomaticUpdateComponents()
        {
            var kit=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab");
            var template=kit.transform.Find("Content/Dash");Assert.NotNull(template);
            Assert.IsEmpty(template.GetComponentsInChildren<Collider>(true));Assert.IsEmpty(template.GetComponentsInChildren<MonoBehaviour>(true));
            foreach(var r in template.GetComponentsInChildren<Renderer>(true))foreach(var m in r.sharedMaterials){Assert.NotNull(m);Assert.NotNull(m.shader);}
        }
        [Test] public void RemoteDashFollowsRenderedPlayerInsteadOfSnapshotTarget()
        {
            var controller=new ArcadeCombatViewController();
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(ArcadeCombatViewController).GetField("dashPresentation",flags).SetValue(controller,effect);
            var actor=new GameObject("RemoteActor");actor.transform.SetParent(root.transform,false);
            using var remote=new OnlineRemotePlayerViewCtrl(actor);
            try
            {
                var entryType=typeof(ArcadeCombatViewController).GetNestedType("RemotePlayerEntry",BindingFlags.NonPublic);
                var entry=Activator.CreateInstance(entryType);
                entryType.GetField("PlayerId").SetValue(entry,9L);entryType.GetField("Visible").SetValue(entry,true);entryType.GetField("View").SetValue(entry,remote);
                var entries=(System.Collections.IDictionary)typeof(ArcadeCombatViewController).GetField("remotePlayers",flags).GetValue(controller);entries.Add(9L,entry);
                remote.Snap(Vector3.zero,0);remote.SetTarget(new Vector3(2,0,0),0);
                controller.SetPlayerDash(9,new Vector2(2,0),Vector2.right,.28f,true);
                var dash=root.transform.Find("Dash_9");Assert.AreEqual(0,dash.localPosition.x,"起步应落在显示位置而非尚未到达的快照目标");
                controller.TickPlayerFeedback(.05f);
                Assert.AreEqual(actor.transform.localPosition.x,dash.localPosition.x);
                Assert.Greater(dash.localPosition.x,0);Assert.Less(dash.localPosition.x,2);
            }
            finally{Object.DestroyImmediate(actor);}
        }
    }
}
