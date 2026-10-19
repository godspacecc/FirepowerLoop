using System.Linq;
using System.Reflection;
using Game;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>真实敌人资源的燃烧、击杀和回池回归；不启动战场或改变生命。</summary>
    public sealed class ArcadeEnemyFlameTests
    {
        private static GameObject Create(string name="ArcadeEnemyBasic")
        {
            var instance=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/"+name+".prefab"));
            instance.GetComponent<EnemyView>().SetNavigationEnabled(false);return instance;
        }

        [TestCase("ArcadeEnemyBasic")]
        [TestCase("ArcadeEnemyArcher")]
        [TestCase("ArcadeEnemyBrute")]
        [TestCase("ArcadeEnemyBossArcher")]
        public void FormalEnemyBurnsOnlyAfterHitAndResetsForReuse(string name)
        {
            var instance=Create(name);
            try
            {
                var view=instance.GetComponent<EnemyView>();var feedback=instance.GetComponentInChildren<EnemyFlameFeedback>(true);
                Assert.That(feedback,Is.Not.Null);Assert.That(feedback.IsBurning,Is.False);
                Assert.That(feedback.GetComponentsInChildren<ParticleSystem>().All(p=>p.particleCount==0&&!p.isEmitting),Is.True);
                view.SetFlameRemaining(.65f);Assert.That(feedback.IsBurning,Is.True);
                var body=feedback.GetComponentsInChildren<ParticleSystem>().Single(p=>p.name=="BodyFlame");
                var sparks=feedback.GetComponentsInChildren<ParticleSystem>().Single(p=>p.name=="HitEmbers");
                Assert.That(body.isEmitting,Is.True);Assert.That(sparks.particleCount,Is.GreaterThan(0));
                view.TickCombatFeedback(0);Assert.That(feedback.IsBurning,Is.True);
                view.TickCombatFeedback(.7f);Assert.That(feedback.IsBurning,Is.False);Assert.That(body.isEmitting,Is.False);
                view.SetFlameRemaining(.65f);view.ResetPhysicalFeedback();
                Assert.That(feedback.IsBurning,Is.False);Assert.That(feedback.GetComponentsInChildren<ParticleSystem>().All(p=>p.particleCount==0&&!p.isEmitting),Is.True);
            }
            finally{Object.DestroyImmediate(instance);}
        }

        [Test]
        public void FlameKillBurstsAndSmokesOnceButOrdinaryDeathDoesNot()
        {
            var instance=Create();
            try
            {
                var view=instance.GetComponent<EnemyView>();var feedback=instance.GetComponentInChildren<EnemyFlameFeedback>();
                var particles=feedback.GetComponentsInChildren<ParticleSystem>();
                var sparks=particles.Single(p=>p.name=="HitEmbers");var smoke=particles.Single(p=>p.name=="DeathSmoke");
                view.PlayFlameDeath(false);Assert.That(particles.All(p=>p.particleCount==0&&!p.isEmitting),Is.True);
                view.PlayFlameDeath(true);Assert.That(feedback.IsBurning,Is.True);Assert.That(sparks.particleCount,Is.GreaterThanOrEqualTo(36));Assert.That(smoke.isPlaying,Is.True);
                int count=sparks.particleCount;view.PlayFlameDeath(true);Assert.That(sparks.particleCount,Is.EqualTo(count));
                view.TickCombatFeedback(.9f);Assert.That(feedback.IsBurning,Is.False);
                view.ResetPhysicalFeedback();Assert.That(particles.All(p=>p.particleCount==0&&!p.isEmitting),Is.True);
            }
            finally{Object.DestroyImmediate(instance);}
        }

        [Test]
        public void AttachedFlamesFollowTorsoAndAreExcludedFromRedHitFlash()
        {
            var instance=Create();
            try
            {
                var view=instance.GetComponent<EnemyView>();var feedback=instance.GetComponentInChildren<EnemyFlameFeedback>();
                Assert.That(feedback.transform.parent,Is.EqualTo(view.Animator.GetBoneTransform(HumanBodyBones.Spine)));
                var before=feedback.transform.position;instance.transform.position+=Vector3.right*3;
                Assert.That(Vector3.Distance(feedback.transform.position,before+Vector3.right*3),Is.LessThan(.001f));
                var renderer=feedback.GetComponentsInChildren<ParticleSystemRenderer>().First();
                var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",Color.green);renderer.SetPropertyBlock(block);
                view.PlayHitFeedback();var after=new MaterialPropertyBlock();renderer.GetPropertyBlock(after);
                Assert.That(after.GetColor("_BaseColor"),Is.EqualTo(Color.green),"普通受击闪色不能污染火焰材质");
            }
            finally{Object.DestroyImmediate(instance);}
        }

        [Test]
        public void DedicatedEnemyFlameDeathUsesSameTemplateAndReleasesWithMap()
        {
            var owner=new GameObject("DedicatedEnemyFlameTest");var controller=new ArcadeCombatViewController();
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            void Set(string name,object value)=>typeof(ArcadeCombatViewController).GetField(name,flags).SetValue(controller,value);
            try
            {
                Set("root",owner);Set("roundEnemyRoot",owner);Set("roundLayoutReady",true);
                Set("enemyPresentation",new FlameTemplatePort());
                controller.SetEnemyContent(10,1,0,false);controller.SetEnemyBurning(10,.65f);
                var feedback=owner.GetComponentInChildren<EnemyFlameFeedback>();Assert.That(feedback,Is.Not.Null);Assert.That(feedback.IsBurning,Is.True);
                controller.RemoveEnemy(10,true,Vector2.up,false,true);
                Assert.That(controller.HasRoundEnemyContent(10),Is.False);Assert.That(feedback.IsBurning,Is.True);
                controller.TickPlayerFeedback(3);
                Assert.That(feedback==null,Is.True,"专属尸体和火焰必须在既有保留时长后释放");
            }
            finally
            {
                typeof(ArcadeCombatViewController).GetMethod("ReleaseRoundContent",flags).Invoke(controller,null);
                Object.DestroyImmediate(owner);
            }
        }

        private sealed class FlameTemplatePort:IArcadeEnemyPresentationPort
        {
            public bool TryCreateFlameFeedback(Transform parent,out EnemyFlameFeedback feedback)
            {
                var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeEnemyBasic.prefab").GetComponentInChildren<EnemyFlameFeedback>(true);
                feedback=Object.Instantiate(source,parent,false);feedback.ResetForReuse();return true;
            }
            public bool TrySetEnemy(int id,string config,Vector3 position,Vector3 facing,int health,int maximum,out string error,bool telegraph=false,int sequence=0,bool persistentHealthBar=false)
            {error=null;return true;}
            public void RemoveEnemy(int id,bool playDeath=false){}
            public void ClearArcadeEnemies(){}
        }
    }
}
