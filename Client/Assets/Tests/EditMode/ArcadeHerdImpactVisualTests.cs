using System.Reflection;
using FirepowerLoop.Arcade;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Game.Tests
{
    /// <summary>正式模型的可见离地、落地停顿和复位，确保碰撞根不随视觉抛升。</summary>
    public sealed class ArcadeHerdImpactVisualTests
    {
        private GameObject instance;
        [TearDown] public void Cleanup(){if(instance!=null)Object.DestroyImmediate(instance);}
        [Test] public void CowKilledZombieFliesAndStaysDeadOnGroundThenResets()
        {
            instance=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab"));
            var view=instance.GetComponent<EnemyView>();view.BeginPresentationUse();view.SetNavigationEnabled(false);view.Animator.Rebind();view.Animator.Update(0);
            var root=instance.transform.position;var before=view.Animator.transform.localPosition;var rotation=view.Animator.transform.localRotation;
            Assert.That(view.TryPlayPhysicalDeathWithImpact(Vector3.right,false,ArcadeDeathImpactRules.Herd,out _),Is.True);
            view.TickCombatFeedback(.475f);view.LateTickCombatFeedback();
            Assert.That(view.Animator.transform.position.y-instance.transform.position.y,Is.GreaterThan(2.4));Assert.That(instance.transform.position,Is.EqualTo(root));
            view.TickCombatFeedback(.475f);view.LateTickCombatFeedback();var landed=view.Animator.transform.localPosition;
            view.TickCombatFeedback(.5f);view.LateTickCombatFeedback();Assert.That(view.Animator.transform.localPosition,Is.EqualTo(landed));Assert.That(view.Animator.enabled,Is.False);
            view.ResetPhysicalFeedback();Assert.That(view.Animator.transform.localPosition,Is.EqualTo(before));Assert.That(Quaternion.Angle(view.Animator.transform.localRotation,rotation),Is.LessThan(.01));Assert.That(view.Animator.enabled,Is.True);
        }
        [Test] public void LivingZombieGroundHoldDoesNotAccumulateOffset()
        {
            instance=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab"));
            var view=instance.GetComponent<EnemyView>();view.BeginPresentationUse();view.SetNavigationEnabled(false);view.Animator.Rebind();view.Animator.Update(0);var before=view.Animator.transform.localPosition;
            var state=new ArcadeHerdImpactState();ArcadeHerdImpactRules.Begin(ref state,1,0);ArcadeHerdImpactRules.Step(ref state,.95f);view.SetHerdImpact(state,Vector3.right);
            view.TickCombatFeedback(0);view.LateTickCombatFeedback();var landed=view.Animator.transform.localPosition;var rotation=view.Animator.transform.localRotation;
            for(int i=0;i<10;i++){ArcadeHerdImpactRules.Step(ref state,.049f);view.SetHerdImpact(state,Vector3.right);view.TickCombatFeedback(.049f);view.LateTickCombatFeedback();}
            Assert.That(view.Animator.transform.localPosition,Is.EqualTo(landed));Assert.That(Quaternion.Angle(view.Animator.transform.localRotation,rotation),Is.LessThan(.01));
            view.SetHerdImpact(default,Vector3.right);Assert.That(view.Animator.transform.localPosition,Is.EqualTo(before));
        }
        [Test] public void PlayerUsesBodyChildAndClearsWhenLeavingArcade()
        {
            instance=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            var view=instance.GetComponent<PlayerView>();var body=(Animator)typeof(PlayerView).GetField("animator",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
            var before=body.transform.localPosition;var root=instance.transform.position;var state=new ArcadeHerdImpactState();ArcadeHerdImpactRules.Begin(ref state,1,0);ArcadeHerdImpactRules.Step(ref state,.475f);
            view.SetHerdImpact(state,Vector3.right);typeof(PlayerView).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);
            Assert.That(body.transform.position.y-instance.transform.position.y,Is.GreaterThan(2.4));Assert.That(instance.transform.position,Is.EqualTo(root));
            view.SetArcadePresentationActive(false);Assert.That(body.transform.localPosition,Is.EqualTo(before));
        }
        [Test] public void DeathDuringFlightContinuesCurrentHeightInsteadOfRestarting()
        {
            instance=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab"));
            var view=instance.GetComponent<EnemyView>();view.BeginPresentationUse();view.SetNavigationEnabled(false);view.Animator.Rebind();view.Animator.Update(0);
            var state=new ArcadeHerdImpactState();ArcadeHerdImpactRules.Begin(ref state,1,0);ArcadeHerdImpactRules.Step(ref state,.475f);
            view.SetHerdImpact(state,Vector3.right);view.TickCombatFeedback(0);view.LateTickCombatFeedback();var before=view.Animator.transform.position;
            Assert.That(view.TryPlayPhysicalDeathWithImpact(Vector3.right,false,ArcadeDeathImpactRules.Herd,out _),Is.True);
            view.TickCombatFeedback(0);view.LateTickCombatFeedback();Assert.That(Vector3.Distance(view.Animator.transform.position,before),Is.LessThan(.01),"不能从空中跳回地面再起飞");
            view.TickCombatFeedback(.475f);view.LateTickCombatFeedback();Assert.That(view.Animator.transform.position.y-instance.transform.position.y,Is.LessThan(.2),"应在原定落地时刻落地");
        }
        [Test] public void NetworkPauseFreezesHerdCorpseAndItsReleaseDeadline()
        {
            instance=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab"));
            var view=instance.GetComponent<EnemyView>();view.BeginPresentationUse();view.SetNavigationEnabled(false);view.Animator.Rebind();view.Animator.Update(0);
            var app=new Game();app.RegisterUtility<IViewInstanceProvider>(new UnityViewInstanceProvider());var ctrl=new EnemyCtrl(app);const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(EnemyCtrl).GetField("arcadeRoot",hidden).SetValue(ctrl,instance.transform);
            var entries=(System.Collections.IDictionary)typeof(EnemyCtrl).GetField("arcadeEnemies",hidden).GetValue(ctrl);var type=typeof(EnemyCtrl).GetNestedType("ArcadeEnemyPresentationEntry",BindingFlags.NonPublic);
            entries.Add(1,System.Activator.CreateInstance(type,1,"enemy.arcade.basic",view,10,Vector3.zero,0,false));
            try
            {
                ctrl.RemoveEnemy(1,true,Vector3.right,false,false,ArcadeDeathImpactRules.Herd);
                var pause=typeof(EnemyCtrl).GetMethod("SetHerdActivityPaused");Assert.That(pause,Is.Not.Null,"断线暂停尚未接入尸体推进");pause.Invoke(ctrl,new object[]{true});
                var corpses=(System.Collections.IList)typeof(EnemyCtrl).GetField("dyingArcadeEnemies",hidden).GetValue(ctrl);var corpse=corpses[0];var release=type.GetProperty("ReleaseTime");float deadline=(float)release.GetValue(corpse);
                var tick=typeof(EnemyCtrl).GetMethod("TickArcadeFeedback",hidden);Assert.That(tick,Is.Not.Null);var before=view.Animator.transform.position;tick.Invoke(ctrl,new object[]{.5f,Time.time+.5f});
                Assert.That(view.Animator.transform.position,Is.EqualTo(before));Assert.That((float)release.GetValue(corpse),Is.EqualTo(deadline+.5f).Within(.001));
                pause.Invoke(ctrl,new object[]{false});tick.Invoke(ctrl,new object[]{.475f,Time.time+.975f});Assert.That(view.Animator.transform.position.y,Is.GreaterThan(2.4));
            }
            finally{ctrl.ReleaseAll();}
        }
    }
}
