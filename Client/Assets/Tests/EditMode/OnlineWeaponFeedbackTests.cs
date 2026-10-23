using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;

namespace Game.Tests
{
    /// <summary>联机瞬时反馈的消费契约，不由动画或掉血推断命中。</summary>
    public sealed class OnlineWeaponFeedbackTests
    {
        private object state;
        private readonly List<OnlineArcadeWeaponFeedbackSnapshot> played=new();
        [SetUp] public void Setup()
        {
            var type=typeof(OnlineArcadePresentationSystem).Assembly.GetType("Game.OnlineWeaponFeedbackPresentationState");
            Assert.That(type,Is.Not.Null,"缺少权威武器反馈的客户端消费状态");state=Activator.CreateInstance(type,true);played.Clear();
        }
        private OnlineArcadeWeaponFeedbackSnapshot Hit(long seq,int kind=1,int round=1,long time=100000,float x=2,float radius=2.8f)=>new(seq,kind,x,3,4,5,radius,round,time);
        private void Observe(OnlineArcadeWeaponFeedbackSnapshot[] hits,bool baseline=false,int round=1,bool visible=true,long server=100100,double received=10.1)
            =>state.GetType().GetMethod("Observe").Invoke(state,new object[]{hits,baseline,round,visible,server,received,(Action<OnlineArcadeWeaponFeedbackSnapshot>)(h=>played.Add(h))});
        [Test] public void BaselineAndDuplicateSnapshotDoNotReplay()
        {Observe(new[]{Hit(1)},baseline:true);Observe(new[]{Hit(1),Hit(2)});Observe(new[]{Hit(1),Hit(2)});Assert.That(played.Count,Is.EqualTo(1));Assert.That(played[0].Sequence,Is.EqualTo(2));}
        [Test] public void HiddenAndPausedHitsAreConsumedWithoutLaterReplay()
        {Observe(new[]{Hit(1)},visible:false);Observe(new[]{Hit(1),Hit(2)});Assert.That(played.Count,Is.EqualTo(1));Assert.That(played[0].Sequence,Is.EqualTo(2));}
        [Test] public void NewRoundAcceptsNewSequenceAndRejectsPreviousRound()
        {Observe(new[]{Hit(20)});Observe(new[]{Hit(1,round:2)},round:2);Observe(new[]{Hit(21)},round:1);Assert.That(played.Count,Is.EqualTo(2));}
        [Test] public void DelayedPacketAndFrozenBattleClockCannotReviveOldFeedback()
        {Observe(Array.Empty<OnlineArcadeWeaponFeedbackSnapshot>(),server:100000,received:10);Observe(new[]{Hit(1,time:100100)},server:100100,received:11.1);Assert.That(played,Is.Empty);Observe(new[]{Hit(2,time:101200)},server:101200,received:11.2);Assert.That(played.Count,Is.EqualTo(1));}
        [Test] public void StaleFutureAndMalformedRecordsAreConsumed()
        {Observe(new[]{Hit(1,time:99000),Hit(2,time:101000),Hit(3,x:float.NaN),Hit(4,kind:99)});Assert.That(played,Is.Empty);Observe(new[]{Hit(4),Hit(5)});Assert.That(played.Count,Is.EqualTo(1));}
        [Test] public void AllThreeKindsKeepAuthoritativePositionDirectionAndRadius()
        {Observe(new[]{Hit(1,1),Hit(2,2),Hit(3,3)});Assert.That(played.Count,Is.EqualTo(3));Assert.That(played[2].X,Is.EqualTo(2));Assert.That(played[2].Z,Is.EqualTo(3));Assert.That(played[2].DirectionX,Is.EqualTo(4));Assert.That(played[2].DirectionZ,Is.EqualTo(5));Assert.That(played[2].Radius,Is.EqualTo(2.8f));}
        [Test] public void ClearAllowsNextRunToEstablishNewBaseline()
        {Observe(new[]{Hit(8)},round:4);state.GetType().GetMethod("Clear").Invoke(state,null);Observe(new[]{Hit(1)},baseline:true);Observe(new[]{Hit(2)});Assert.That(played.Count,Is.EqualTo(1));}
        [Test] public void InvalidExplosionRadiusDoesNotSuppressFollowingValidHit()
        {Observe(new[]{Hit(1,3,radius:0),Hit(2,3,radius:float.PositiveInfinity),Hit(3)});Assert.That(played.Count,Is.EqualTo(1));Assert.That(played[0].Sequence,Is.EqualTo(3));}
        [Test] public void ProtocolProjectionCopiesFeedbackValues()
        {
            var source=new ET.OnlineArcadeBattleInfo();
            source.WeaponFeedback.Add(new ET.OnlineArcadeWeaponFeedbackInfo{Sequence=8,Kind=2,PositionX=2,PositionZ=3,DirectionX=4,DirectionZ=5,RoundGeneration=7,ServerTime=100000});
            var snapshot=(OnlineArcadeBattleSnapshot)typeof(OnlineArcadeRunSystem).GetMethod("CopyBattle",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{source});
            source.WeaponFeedback[0].PositionX=99;source.WeaponFeedback.Clear();
            Assert.That(snapshot.WeaponFeedback.Count,Is.EqualTo(1));var hit=snapshot.WeaponFeedback[0];
            Assert.That(hit.X,Is.EqualTo(2));Assert.That(hit.DirectionX,Is.EqualTo(4));Assert.That(hit.RoundGeneration,Is.EqualTo(7));Assert.That(hit.Sequence,Is.EqualTo(8));
        }
        [Test] public void PresentationRoutesToExistingViewPorts()
        {
            var system=new OnlineArcadePresentationSystem();var view=new Recorder();
            typeof(OnlineArcadePresentationSystem).GetField("view",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(system,view);
            var method=typeof(OnlineArcadePresentationSystem).GetMethod("PlayWeaponFeedback",BindingFlags.Instance|BindingFlags.NonPublic);
            Assert.That(method,Is.Not.Null,"需要把权威反馈接入现有表现端口");
            foreach(int kind in new[]{1,2,3})method.Invoke(system,new object[]{Hit(kind,kind)});
            Assert.That(view.Body,Is.EqualTo(1));Assert.That(view.Surface,Is.EqualTo(1));Assert.That(view.Explosion,Is.EqualTo(1));
        }
        [Test] public void OrdinaryExplosionOmitsOuterSparksAndPoolReuseKeepsEnergyFeedback()
        {
            var root=new GameObject("feedback-test");
            try
            {
                var view=new ArcadeCombatViewController();var type=view.GetType();const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
                type.GetField("root",flags).SetValue(view,root);
                var template=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab").transform.Find("Content/ProjectileBlast");
                type.GetField("projectileBlastTemplate",flags).SetValue(view,template);
                var play=type.GetMethod("PlayProjectileBlast",flags);
                for(int i=0;i<12;i++)play.Invoke(view,new object[]{Vector2.zero,1.4f,true});
                play.Invoke(view,new object[]{Vector2.zero,2.8f,false});
                var reused=root.transform.GetChild(0);var sparks=reused.Find("Sparks").GetComponent<ParticleSystem>();
                Assert.That(sparks.isPlaying,Is.False,"普通爆炸关闭外围Sparks，不能残留前次能量火星");
                Assert.That(reused.Find("Burst").GetComponent<ParticleSystem>().isPlaying,Is.True,"橙色爆炸主体保留");
                for(int i=0;i<12;i++)play.Invoke(view,new object[]{Vector2.zero,1.4f,true});
                Assert.That(sparks.isPlaying,Is.False,"新采用的能量爆炸同样收掉放射火星，复用不能恢复旧火星");
                Assert.That(root.transform.childCount,Is.EqualTo(12),"沿用有界池，不按命中无限创建");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        /// <summary>记录既有表现端口参数，不创建资源或裁决战斗。</summary>
        private sealed class Recorder:IArcadeCombatViewPort
        {
            public int Body,Surface,Explosion;
            public bool IsActive=>true;
            public void PlayImpactSound(Vector2 p,bool melee){Assert.That(p,Is.EqualTo(new Vector2(2,3)));Assert.That(melee,Is.False);Body++;}
            public void PlayProjectileSurfaceImpact(Vector2 p,Vector2 direction){Assert.That(p,Is.EqualTo(new Vector2(2,3)));Assert.That(direction,Is.EqualTo(new Vector2(4,5)));Surface++;}
            public void PlayProjectileExplosion(Vector2 p,float radius){Assert.That(p,Is.EqualTo(new Vector2(2,3)));Assert.That(radius,Is.EqualTo(2.8f));Explosion++;}
            public void Begin(int n,float w,float h,IReadOnlyList<ArcadeObstacleDefinition> o){}
            public void End(){}
            public bool TryScreenToArena(Vector2 s,out Vector2 p){p=default;return false;}
            public void SetPlayer(Vector2 p,Vector2 f,bool v,bool i){}
            public void SetPlayerHealth(int c,int m){}
            public void SetEnemy(int id,string config,Vector2 p,int c,int m,bool telegraph=false,int attackSequence=0,bool persistentHealthBar=false){}
            public void RemoveEnemy(int id,bool playDeath=false){}
            public void SetProjectile(int id,bool hostile,Vector2 p,Vector2 d){}
            public void RemoveProjectile(int id){}
            public void SetDrop(int id,ArcadeDropDefinition d,Vector2 p){}
            public void RemoveDrop(int id){}
            public void SetRemotePlayer(long id,Vector2 p,Vector2 f,bool v,bool i,int c,int m){}
            public void RemoveRemotePlayer(long id){}
            public void TickPlayerFeedback(float s){}
        }
    }
}
