using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>通过正式角色验证受击、倒地、救援及复活表现，不执行战斗规则。</summary>
    public sealed class ArcadePlayerDamageTests
    {
        private GameObject actor;
        private PlayerView view;
        private Transform fx;
        [SetUp] public void Setup()
        {
            actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            view=actor.GetComponent<PlayerView>();view.SetArcadePresentationActive(true);
            fx=actor.transform.Find("ArcadeDamage");Assert.NotNull(fx,"正式玩家尚未绑定受击与倒地效果");
        }
        [TearDown] public void Cleanup(){if(actor!=null)Object.DestroyImmediate(actor);}
        private void Rescue(bool active)
        {
            var method=typeof(PlayerView).GetMethod("SetArcadeRescueActive");Assert.NotNull(method);
            method.Invoke(view,new object[]{active});
        }
        [Test] public void HurtFlashExpiresWithoutMovingPlayerOrInterruptingCombat()
        {
            var position=actor.transform.position;view.PlayHurtFeedback();
            Assert.True(fx.Find("Hit").gameObject.activeSelf);
            Assert.True(view.CharacterController.enabled);Assert.False(view.Animator.GetBool("Dead"));
            view.TickCombatFeedback(.3f);Assert.False(fx.Find("Hit").gameObject.activeSelf);
            Assert.AreEqual(position,actor.transform.position);
        }
        [Test] public void DeathLandsOnceAndRepeatedDeadStateDoesNotReplayDust()
        {
            view.SetAlivePresentation(false);Assert.False(fx.Find("Landing").gameObject.activeSelf);
            view.TickCombatFeedback(.4f);Assert.True(fx.Find("Landing").gameObject.activeSelf);
            view.TickCombatFeedback(1);Assert.False(fx.Find("Landing").gameObject.activeSelf);
            view.SetAlivePresentation(false);view.TickCombatFeedback(.4f);Assert.False(fx.Find("Landing").gameObject.activeSelf);
            Assert.True(view.Animator.GetBool("Dead"));
        }
        [Test] public void RescueOnlyAppearsForAuthoritativeDownedStateAndReviveClearsEverything()
        {
            Rescue(true);Assert.False(fx.Find("Rescue").gameObject.activeSelf,"存活角色不能显示救援标记");
            view.SetAlivePresentation(false);Rescue(false);Assert.False(fx.Find("Rescue").gameObject.activeSelf);
            Rescue(true);Assert.True(fx.Find("Rescue").gameObject.activeSelf);
            view.TickCombatFeedback(.4f);view.SetAlivePresentation(true);
            Assert.False(fx.Find("Landing").gameObject.activeSelf);Assert.False(fx.Find("Hit").gameObject.activeSelf);
            Assert.False(fx.Find("Rescue").gameObject.activeSelf);Assert.False(view.Animator.GetBool("Dead"));
        }
        [Test] public void PauseFreezesAndDisablingClearsOwnedEffects()
        {
            view.PlayHurtFeedback();view.TickCombatFeedback(.04f);
            var scale=fx.Find("Hit").localScale;view.TickCombatFeedback(0);Assert.AreEqual(scale,fx.Find("Hit").localScale);
            actor.SetActive(false);Assert.False(fx.gameObject.activeInHierarchy);
            // 普通 MonoBehaviour 在 EditMode 不自动派发 OnDisable，显式验证运行时清理回调。
            typeof(PlayerView).GetMethod("OnDisable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);
            Assert.False(fx.gameObject.activeSelf);
        }
        [Test] public void RemoteDeathStaysAtRenderedLocationUntilRevive()
        {
            var controller=new ArcadeCombatViewController();const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var map=new GameObject("RemoteTestMap");actor.transform.SetParent(map.transform,false);
            using var remote=new OnlineRemotePlayerViewCtrl(actor);
            try
            {
                typeof(ArcadeCombatViewController).GetField("mapRoot",flags).SetValue(controller,map.transform);
                var type=typeof(ArcadeCombatViewController).GetNestedType("RemotePlayerEntry",BindingFlags.NonPublic);var entry=Activator.CreateInstance(type);
                type.GetField("PlayerId").SetValue(entry,9L);type.GetField("Visible").SetValue(entry,true);type.GetField("View").SetValue(entry,remote);
                ((IDictionary)typeof(ArcadeCombatViewController).GetField("remotePlayers",flags).GetValue(controller)).Add(9L,entry);
                remote.Snap(new Vector3(4,0,3),0);
                controller.SetRemotePlayer(9,Vector2.zero,Vector2.up,false,false,0,100);controller.TickPlayerFeedback(.1f);
                Assert.AreEqual(new Vector3(4,0,3),actor.transform.localPosition,"死亡不能追向预备复活位置");
                controller.SetRemotePlayer(9,Vector2.one,Vector2.up,true,false,100,100);controller.TickPlayerFeedback(.1f);
                Assert.AreEqual(new Vector3(1,0,1),actor.transform.localPosition);
            }
            finally{actor.transform.SetParent(null);Object.DestroyImmediate(map);}
        }
        [Test] public void JoiningDeadPlayerBuildsBaselineWithoutHistoricalLanding()
        {
            var method=typeof(PlayerView).GetMethod("SetAlivePresentation",new[]{typeof(bool),typeof(bool)});Assert.NotNull(method);
            method.Invoke(view,new object[]{false,false});view.TickCombatFeedback(.4f);
            Assert.False(fx.Find("Hit").gameObject.activeSelf);Assert.False(fx.Find("Landing").gameObject.activeSelf);
            view.Animator.Update(0);var state=view.Animator.GetCurrentAnimatorStateInfo(0);
            Assert.True(state.IsName("Death")&&state.normalizedTime>=.99f,"首次已倒地需直接建立倒地终态");
            Rescue(true);Assert.True(fx.Find("Rescue").gameObject.activeSelf);
        }
        [Test] public void LocalInitialDeadSnapshotDoesNotReplayAndLaterDeathStillPlays()
        {
            var controller=new ArcadeCombatViewController();const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            using var motor=new PlayerViewCtrl(view,1,1,new ViewCtrlRegistry(),new UnityViewInstanceProvider());
            var map=new GameObject("LocalDamageTestMap");
            typeof(ArcadeCombatViewController).GetField("playerMotor",flags).SetValue(controller,motor);
            typeof(ArcadeCombatViewController).GetField("mapRoot",flags).SetValue(controller,map.transform);
            try
            {
            controller.SetPlayer(new Vector2(3,4),Vector2.up,false,false);
            view.TickCombatFeedback(.4f);
            Assert.False(fx.Find("Landing").gameObject.activeSelf,"首次已倒地状态不能补播历史落尘");
            Assert.AreEqual(new Vector3(3,0,4),actor.transform.position,"初始倒地仍需使用权威位置");
            controller.SetPlayer(Vector2.zero,Vector2.up,true,false);
            controller.SetPlayer(Vector2.zero,Vector2.up,false,false);view.TickCombatFeedback(.4f);
            Assert.True(fx.Find("Landing").gameObject.activeSelf,"建立基线后真实死亡必须播放落尘");
            }
            finally{Object.DestroyImmediate(map);}
        }
        [Test] public void HurtChestRecoilDoesNotAccumulateAndRestoresWhenFeedbackEnds()
        {
            view.Animator.Rebind();view.Animator.Update(0);
            var chest=view.Animator.GetBoneTransform(HumanBodyBones.Chest);Assert.NotNull(chest);
            var before=chest.localRotation;view.PlayHurtFeedback();view.TickCombatFeedback(.06f);
            var helper=typeof(PlayerView).GetField("arcadeDamage",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
            var apply=helper.GetType().GetMethod("ApplyHurtPose");apply.Invoke(helper,new object[]{view.Animator});
            var first=chest.localRotation;Assert.Greater(Quaternion.Angle(before,first),1f);
            apply.Invoke(helper,new object[]{view.Animator});Assert.Less(Quaternion.Angle(first,chest.localRotation),.01f);
            view.TickCombatFeedback(.3f);apply.Invoke(helper,new object[]{view.Animator});
            Assert.Less(Quaternion.Angle(before,chest.localRotation),.01f);
        }
        [Test] public void FormalEffectsHaveNoGameplayComponentsAndTwoActorsStayIndependent()
        {
            Assert.IsEmpty(fx.GetComponentsInChildren<Collider>(true));Assert.IsEmpty(fx.GetComponentsInChildren<MonoBehaviour>(true));
            var other=Object.Instantiate(actor);
            try
            {
                view.PlayHurtFeedback();view.SetAlivePresentation(false);view.TickCombatFeedback(.4f);
                var otherFx=other.transform.Find("ArcadeDamage");Assert.False(otherFx.Find("Landing").gameObject.activeSelf);
                Assert.False(other.GetComponent<PlayerView>().Animator.GetBool("Dead"));
            }
            finally{Object.DestroyImmediate(other);}
        }
    }
}
