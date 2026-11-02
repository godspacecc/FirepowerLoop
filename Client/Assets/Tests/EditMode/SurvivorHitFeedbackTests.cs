using QFramework;using FirepowerLoop.Arcade;using System.Collections.Generic;using System;using System.Linq;using System.Reflection;using NUnit.Framework;using UnityEditor;using UnityEngine;using Object=UnityEngine.Object;
namespace Game.Tests
{
    /// <summary>正式演员受击方向、活动时钟及回收边界，不修改产品资源。</summary>
    public sealed class SurvivorHitFeedbackTests
    {
        private GameObject actor;private Component view;private Transform chest;
        private static void Call(object target,string name,params object[] args)
        {var m=target.GetType().GetMethods(BindingFlags.Public|BindingFlags.Instance).SingleOrDefault(m=>m.Name==name&&m.GetParameters().Length==args.Length);Assert.That(m,Is.Not.Null,name+"尚未接入");m.Invoke(target,args);}
        private void Spawn(bool player)
        {
            actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/"+(player?"GrayboxPlayer":"ArcadeZombieShambler")+".prefab"));
            view=player?(Component)actor.GetComponent<PlayerView>():actor.GetComponent<EnemyView>();
            if(player)actor.GetComponent<PlayerView>().SetArcadePresentationActive(true);
            Call(view,"SetSurvivorHitFeedbackActive",true);
            chest=actor.GetComponentsInChildren<Transform>(true).Single(x=>x.name==(player?"Spine_02":"Torso"));
        }
        private void Hit(bool player,Vector3 direction)=>Call(view,player?"PlaySurvivorHurtFeedback":"PlaySurvivorHitFeedback",direction);
        private void Step(bool player,float seconds)
        {if(!player)Call(view,"TickSurvivorHitFeedback",seconds);Call(view,"TickCombatFeedback",seconds);if(player)view.GetType().GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);else Call(view,"LateTickCombatFeedback");}
        [TearDown]public void Cleanup(){if(actor!=null)Object.DestroyImmediate(actor);}
        [TestCase(true,1,0)][TestCase(true,-1,0)][TestCase(true,0,1)][TestCase(true,0,-1)]
        [TestCase(false,1,0)][TestCase(false,-1,0)][TestCase(false,0,1)][TestCase(false,0,-1)]
        public void BodyLeansAlongWorldPushAtAnyFacing(bool player,int x,int z)
        {
            Spawn(player);actor.transform.rotation=Quaternion.Euler(0,137,0);var before=chest.rotation;var root=actor.transform.position;
            var push=new Vector3(x,0,z);Hit(player,push);Step(player,player?.06f:.075f);
            var displaced=(chest.rotation*Quaternion.Inverse(before))*Vector3.up;
            Assert.That(Vector3.Dot(displaced,push),Is.GreaterThan(player?.15f:.4f),"身体必须沿本次攻击推力倾倒");
            Assert.That(actor.transform.position,Is.EqualTo(root));
        }
        [TestCase(true)][TestCase(false)]public void ZeroTimeDoesNotAccumulateAndCompletionRestoresPose(bool player)
        {
            Spawn(player);var before=chest.localRotation;Hit(player,Vector3.right);Step(player,.06f);var pose=chest.localRotation;
            for(int i=0;i<8;i++)Step(player,0);
            Assert.That(Quaternion.Angle(pose,chest.localRotation),Is.LessThan(.02f));
            Step(player,.4f);Assert.That(Quaternion.Angle(before,chest.localRotation),Is.LessThan(.02f));
        }
        [TestCase(true)][TestCase(false)]public void RepeatedHitChangesDirectionWithoutSpawningOrMovingRoot(bool player)
        {
            Spawn(player);int count=actor.GetComponentsInChildren<Transform>(true).Length;var before=chest.rotation;var root=actor.transform.position;
            Hit(player,Vector3.right);Step(player,.06f);Hit(player,Vector3.left);Step(player,.06f);
            var displaced=(chest.rotation*Quaternion.Inverse(before))*Vector3.up;
            Assert.That(displaced.x,Is.LessThan(-.1f));Assert.That(actor.GetComponentsInChildren<Transform>(true).Length,Is.EqualTo(count));Assert.That(actor.transform.position,Is.EqualTo(root));
        }
        [TestCase(true)][TestCase(false)]public void DisablingModeRestoresPoseAndClearsTransientVisuals(bool player)
        {
            Spawn(player);var before=chest.localRotation;Hit(player,Vector3.right);Step(player,.06f);Call(view,"SetSurvivorHitFeedbackActive",false);
            Assert.That(Quaternion.Angle(before,chest.localRotation),Is.LessThan(.02f));
            var feedback=actor.GetComponentsInChildren<MonoBehaviour>(true).Single(c=>c.GetType().Name=="SurvivorHitFeedbackView");
            Assert.That(feedback.GetComponentsInChildren<Renderer>(true).All(r=>!r.gameObject.activeInHierarchy),Is.True);
        }
        [Test]public void RealDamageUsesSourceAndZeroDamageDoesNotReplay()
        {
            var f=new SurvivorRuntimeTests();f.Setup();try
            {
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                object Read(string field)=>typeof(SurvivorRuntimeTests).GetField(field,flags).GetValue(f);
                var sys=(SurvivorSystem)Read("system");var model=(SurvivorModel)Read("model");var port=Read("view");
                var architecture=(Game)Read("architecture");int hudHits=0;var subscription=architecture.RegisterEvent<SurvivorPlayerHurtEvent>(_=>hudHits++);
                try
                {
                    typeof(SurvivorSystem).GetField("player",flags).SetValue(sys,new Vector2(3,2));
                    var damage=typeof(SurvivorSystem).GetMethod("DamageSurvivorFrom",flags);
                    damage.Invoke(sys,new object[]{10,(Vector2?)new Vector2(1,2)});
                    Assert.That(port.GetType().GetField("PlayerHitPush").GetValue(port),Is.EqualTo(new Vector2(2,0)));Assert.That(hudHits,Is.EqualTo(1));
                    int hp=model.Health;damage.Invoke(sys,new object[]{0,(Vector2?)new Vector2(4,2)});Assert.That(model.Health,Is.EqualTo(hp));Assert.That(hudHits,Is.EqualTo(1));
                    var enemies=(Dictionary<int,ArcadeEnemyRuntime>)typeof(SurvivorSystem).GetField("enemies",flags).GetValue(sys);
                    var enemy=new ArcadeEnemyRuntime{Id=891,Position=new Vector2(3,1),Health=100,MaxHealth=100};enemies.Add(enemy.Id,enemy);
                    typeof(SurvivorSystem).GetMethod("ApplyModifierDamage",flags).Invoke(sys,new object[]{enemy,10,(Vector2?)new Vector2(3,4)});
                    Assert.That(port.GetType().GetField("EnemyHitPush").GetValue(port),Is.EqualTo(new Vector2(0,-3)));Assert.That(enemy.Health,Is.EqualTo(90));
                }finally{subscription.UnRegister();}
            }finally{f.Cleanup();}
        }
        [Test]public void FormalHudEdgesFreezeClearAndNeverBlockInput()
        {
            actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Survivor/SurvivorHud.prefab"));
            var ui=actor.GetComponent<SurvivorHudUI>();var field=typeof(SurvivorHudUI).GetField("HurtEdges");Assert.That(field,Is.Not.Null);
            var edge=(Component)field.GetValue(ui);Assert.That(edge,Is.Not.Null);var images=edge.GetComponentsInChildren<UnityEngine.UI.Image>(true);Assert.That(images.Length,Is.EqualTo(4));
            Assert.That(images.All(i=>!i.raycastTarget),Is.True);Assert.That(actor.GetComponent<ReferenceCollector>().data.Single(x=>x.key=="HurtEdges").gameObject,Is.EqualTo(edge));
            Call(edge,"Play");Call(edge,"Tick",.05f);float alpha=images[0].color.a;Call(edge,"Tick",0f);Assert.That(images[0].color.a,Is.EqualTo(alpha));
            Call(edge,"Clear");Assert.That(images.All(i=>i.color.a==0),Is.True);
        }
        [TestCase(true)][TestCase(false)]public void NewAnimationBaselineIsPreservedAndDeathClears(bool player)
        {
            Spawn(player);Hit(player,Vector3.right);Step(player,.06f);
            var animated=Quaternion.Euler(17,23,31);chest.localRotation=animated;Step(player,.4f);
            Assert.That(Quaternion.Angle(animated,chest.localRotation),Is.LessThan(.02f),"不能把新动画写回过期基线");
            Hit(player,Vector3.right);Step(player,.06f);
            if(player)Call(view,"SetAlivePresentation",false);else{actor.SetActive(false);view.GetType().GetMethod("OnDisable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);}
            var feedback=actor.GetComponentInChildren<SurvivorHitFeedbackView>(true);
            Assert.That(feedback.GetComponentsInChildren<Renderer>(true).All(r=>!r.gameObject.activeSelf),Is.True);
        }
        [Test]public void UpgradePauseFreezesEnemyFeedbackThroughRealControllerClock()
        {
            Spawn(false);var architecture=new Game();var survivor=new SurvivorModel();architecture.RegisterModel(survivor);survivor.Active=true;
            architecture.RegisterModel(new AppStateModel());architecture.GetModel<AppStateModel>().CurrentState.Value=AppState.Playing;
            architecture.RegisterUtility<IViewInstanceProvider>(new UnityViewInstanceProvider());var controller=new EnemyCtrl(architecture);
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(EnemyCtrl).GetNestedType("ArcadeEnemyPresentationEntry",BindingFlags.NonPublic);
            var entries=(System.Collections.IDictionary)typeof(EnemyCtrl).GetField("arcadeEnemies",flags).GetValue(controller);
            entries.Add(19,Activator.CreateInstance(type,19,"enemy.arcade.zombie.shambler",view,100,actor.transform.position,0,false));
            try
            {
                Hit(false,Vector3.right);Step(false,.06f);var pose=chest.localRotation;survivor.Paused=true;
                typeof(EnemyCtrl).GetMethod("TickArcadeFeedback",flags).Invoke(controller,new object[]{.5f,1f});Call(view,"LateTickCombatFeedback");
                Assert.That(Quaternion.Angle(pose,chest.localRotation),Is.LessThan(.02f),"升级暂停必须冻结新受击，不能只冻结元素");
                survivor.Paused=false;
                for(int i=0;i<8;i++){typeof(EnemyCtrl).GetMethod("TickArcadeFeedback",flags).Invoke(controller,new object[]{.05f,1f});Call(view,"LateTickCombatFeedback");}
                Assert.That(Quaternion.Angle(pose,chest.localRotation),Is.GreaterThan(5));
            }finally{entries.Clear();controller.ReleaseAll();}
        }
        [Test]public void HealthSyncWithoutBarCannotCancelHitAndExplicitResetClears()
        {
            Spawn(false);typeof(EnemyView).GetField("healthBarFill",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(view,null);
            Hit(false,Vector3.right);Step(false,.06f);var pose=chest.localRotation;
            Call(view,"SetHealthDisplay",90,100);Step(false,0);
            Assert.That(Quaternion.Angle(pose,chest.localRotation),Is.LessThan(.02f),"生命同步无血条时不能关闭本模式受击");
            Assert.That((bool)typeof(EnemyView).GetField("survivorHitActive",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view),Is.True);
            Call(view,"ResetPhysicalFeedback");
            Assert.That(actor.GetComponentInChildren<SurvivorHitFeedbackView>(true).GetComponentsInChildren<Renderer>(true).All(r=>!r.gameObject.activeSelf),Is.True);
        }
    }
}
