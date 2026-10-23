using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    /// <summary>以真实正式资源验证三关投影、阶段采样及异步句柄回收，不启动业务时钟。</summary>
    public sealed class NextStageViewTests
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        ArcadeCombatViewController view;GameObject root;Loader loader;IResourceLoader previous;ArcadeConfigCatalogDefinition catalog;
        [SetUp] public void Setup()
        {
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog,out var error),Is.True,error);
            previous=ResourceLoader.Current;loader=new Loader();ResourceLoader.Use(loader);view=new ArcadeCombatViewController();root=new GameObject("next-stage-view-test");Set("root",root);
        }
        [TearDown] public void Cleanup(){Release();if(root!=null)UnityEngine.Object.DestroyImmediate(root);ResourceLoader.Use(previous);}
        void Set(string name,object value)=>typeof(ArcadeCombatViewController).GetField(name,Hidden).SetValue(view,value);
        void Release()=>typeof(ArcadeCombatViewController).GetMethod("ReleaseRoundContent",Hidden).Invoke(view,null);
        void Start(int arena)
        {
            var stage=catalog.EarlyStages.Single(p=>p.ArenaConfigNumber==arena);Set("earlyStage",stage);view.ConfigureRoundBounds(stage.HalfWidth,stage.HalfHeight,.55f);
            view.SetRoundLayout(catalog.RoundLayouts.First(p=>p.ArenaConfigNumber==arena));
        }
        void State(int id,int kind,int phase,float age,float x,float y,int revives=1)
        {
            var method=typeof(ArcadeCombatViewController).GetMethod("SetNextStageEnemyState");Assert.That(method,Is.Not.Null,"缺少正式阶段只读投影入口");
            var state=Activator.CreateInstance(method.GetParameters()[2].ParameterType);
            foreach(var pair in new[]{("Phase",(object)phase),("Age",age),("OriginX",x),("OriginY",y),("RevivesRemaining",revives)})state.GetType().GetField(pair.Item1).SetValue(state,pair.Item2);
            method.Invoke(view,new[]{(object)id,kind,state});
        }
        Transform Node(string name)=>root.GetComponentsInChildren<Transform>(true).SingleOrDefault(t=>t.name==name);
        void Ready(int arena){Start(arena);loader.FinishAll();Assert.That(view.IsRoundLayoutReady,Is.True,view.RoundLayoutLoadError);}

        [Test] public void TempleWaitsForAllFormalTemplatesBeforePublishingReady()
        {
            Start(24);Assert.That(loader.Pending.Count,Is.EqualTo(4),"环境/正式暗影丧尸/乌鸦/蓝影须归同一代次预载");
            loader.Finish(0);Assert.That(view.IsRoundLayoutReady,Is.False,"环境先完成不能提前放行");loader.FinishAll();Assert.That(view.IsRoundLayoutReady,Is.True,view.RoundLayoutLoadError);
            Assert.That(Node("ConfirmedEarlyEnvironment"),Is.Not.Null);Assert.That(Node("ThemeDecor"),Is.Null);
        }
        [Test] public void GraveUsesRealHumanoidAndSamplesRiseThenCrossesRimWithoutHud()
        {
            Ready(23);view.RecordRoundEnemyPosition(7,new Vector2(8,-10.85f));State(7,18,1,0,8,-8.35f,0);
            var humanoid=Node("StageHumanoid_7");Assert.That(humanoid,Is.Not.Null);Assert.That(humanoid.GetComponentsInChildren<SkinnedMeshRenderer>(true),Is.Not.Empty);
            Assert.That(humanoid.localPosition,Is.EqualTo(new Vector3(8,-1.65f,-8.35f)));
            State(7,18,1,1.2f,8,-8.35f,0);Assert.That(humanoid.localPosition.y,Is.EqualTo(.4f).Within(.001f));
            State(7,18,1,2.1f,8,-8.35f,0);Assert.That(Vector3.Distance(humanoid.localPosition,new Vector3(8,0,-10.85f)),Is.LessThan(.001f));
            Assert.That(root.GetComponentsInChildren<Canvas>(true).Any(c=>c.enabled),Is.False);Assert.That(Node("SkillWarning"),Is.Null);Assert.That(Node("HealthFill"),Is.Null);
            view.SetEnemyContent(7,18,0,true);view.SetEnemy(7,"unused",new Vector2(8,-11),9,10);Assert.That(Node("StageHumanoid_7"),Is.SameAs(humanoid));
        }
        [Test] public void ShadowArrivalFlapsRealCrowAndTravelSamplesOnlySnapshotAge()
        {
            Ready(24);view.RecordRoundEnemyPosition(9,new Vector2(-7,-1));State(9,19,2,0,-7,-1);
            var crow=Node("StageCrow_9");Assert.That(crow.localPosition,Is.EqualTo(new Vector3(-12,5,1)));Assert.That(Node("StageHumanoid_9").gameObject.activeSelf,Is.False);
            State(9,19,2,.5f,-7,-1);Assert.That(Quaternion.Angle(crow.Find("WingLeft").localRotation,Quaternion.identity),Is.GreaterThan(1));
            view.RecordRoundEnemyPosition(9,new Vector2(2,-2));State(9,19,3,.675f,-5,-4,0);
            var travel=Node("StageTravel_9");Assert.That(travel.localPosition,Is.EqualTo(new Vector3(-1.5f,.65f,-3)));var position=travel.localPosition;
            view.TickPlayerFeedback(.2f);Assert.That(travel.localPosition,Is.EqualTo(position),"表现帧不能自行推进换位业务");Assert.That(crow.gameObject.activeSelf,Is.False);
        }
        [Test] public void OlderPhaseSnapshotsCannotRewindShadowAndIdentityChangeCleansIt()
        {
            Ready(25);view.RecordRoundEnemyPosition(2,new Vector2(2,-2));State(2,19,3,1,-5,-4,0);var travel=Node("StageTravel_2");var position=travel.localPosition;
            State(2,19,2,.2f,-7,-1,1);Assert.That(travel.localPosition,Is.EqualTo(position));Assert.That(Node("StageCrow_2").gameObject.activeSelf,Is.False);
            State(2,0,0,0,0,0,0);Assert.That(Node("StageHumanoid_2"),Is.Null);Assert.That(Node("StageCrow_2"),Is.Null);Assert.That(Node("StageTravel_2"),Is.Null);
        }
        [Test] public void LateTemplateCallbacksAfterReleaseCannotAttachAndHandlesReleaseOnce()
        {
            Start(24);Assert.That(loader.Pending.Count,Is.EqualTo(4));Release();loader.FinishAll();Assert.That(root.transform.childCount,Is.Zero);Assert.That(view.IsRoundLayoutReady,Is.False);
            Assert.That(loader.Released.Count,Is.EqualTo(4));Release();Assert.That(loader.Released.Count,Is.EqualTo(4));
        }
        [Test] public void MissingRequiredTemplateRollsBackAndSameLayoutCanRetry()
        {
            Start(24);Assert.That(loader.Pending.Count,Is.EqualTo(4));LogAssert.Expect(LogType.Error,new System.Text.RegularExpressions.Regex(".*已确认场地准备失败.*"));
            loader.Finish(1,true);loader.FinishAll();Assert.That(view.IsRoundLayoutReady,Is.False);Assert.That(view.RoundLayoutLoadError,Is.Not.Empty);Assert.That(root.transform.childCount,Is.Zero);Assert.That(loader.Released.Count,Is.EqualTo(4));
            Start(24);loader.FinishAll();Assert.That(view.IsRoundLayoutReady,Is.True,view.RoundLayoutLoadError);
        }
        [Test] public void FinalShadowDeathKeepsBriefBlueDissolveAndMapReleaseCleansImmediately()
        {
            Ready(24);view.RecordRoundEnemyPosition(6,new Vector2(2,-2));State(6,19,0,5,-5,-4,0);view.RemoveEnemy(6,true);
            Assert.That(Node("StageHumanoid_6"),Is.Null);var death=Node("StageShadowDeath_6");Assert.That(death,Is.Not.Null);view.RemoveEnemy(6,true);Assert.That(Node("StageShadowDeath_6"),Is.SameAs(death));
            view.TickPlayerFeedback(.35f);Assert.That(death.Find("Core").localScale.x,Is.LessThan(.5f));view.TickPlayerFeedback(.4f);Assert.That(Node("StageShadowDeath_6"),Is.Null);
            view.RecordRoundEnemyPosition(8,Vector2.zero);State(8,19,0,5,0,0,0);view.RemoveEnemy(8,true);Release();Assert.That(root.transform.childCount,Is.Zero);
        }
        [Test] public void FormalStageHumanoidsKeepExistingFlameFeedbackAndClearOnHiddenOrRemoved()
        {
            Ready(23);view.RecordRoundEnemyPosition(50,new Vector2(8,-10.85f));State(50,18,0,0,8,-8.35f,0);
            var flame=Node("StageHumanoid_50").GetComponentInChildren<EnemyFlameFeedback>(true);Assert.That(flame,Is.Not.Null,"复用正式Prefab已有火焰");
            view.SetEnemyBurning(50,.65f);Assert.That(flame.IsBurning,Is.True,"18号敌人不能因专用投影而丢失既有喷火命中反馈");
            var body=flame.GetComponentsInChildren<ParticleSystem>(true).Single(p=>p.name=="BodyFlame");Assert.That(body.isEmitting,Is.True);
            view.TickPlayerFeedback(0);Assert.That(flame.IsBurning,Is.True);view.TickPlayerFeedback(.4f);Assert.That(flame.IsBurning,Is.True,"既有表现帧只能推进一次");
            view.TickPlayerFeedback(.3f);Assert.That(flame.IsBurning,Is.False);Assert.That(body.isEmitting,Is.False);
            view.SetEnemyBurning(50,.65f);view.RemoveEnemy(50);Assert.That(flame==null,Is.True);Release();
            Ready(24);view.RecordRoundEnemyPosition(51,new Vector2(2,-2));State(51,19,0,0,-5,-4,1);flame=Node("StageHumanoid_51").GetComponentInChildren<EnemyFlameFeedback>(true);
            view.SetEnemyBurning(51,.65f);Assert.That(flame.IsBurning,Is.True,"19号敌人同样复用原火焰");
            State(51,19,3,0,-5,-4,0);Assert.That(flame.IsBurning,Is.False,"蓝影换位隐藏实体时立即清空旧火焰");
            Assert.That(flame.GetComponentsInChildren<ParticleSystem>(true).All(p=>p.particleCount==0&&!p.isEmitting),Is.True);
            view.SetEnemyBurning(51,.65f);Assert.That(flame.IsBurning,Is.False,"隐藏态不把旧命中反馈叠到蓝影上");
            State(51,19,0,0,-5,-4,0);Assert.That(flame.IsBurning,Is.False);view.SetEnemyBurning(51,.65f);Assert.That(flame.IsBurning,Is.True);
            view.RemoveEnemy(51,true);Assert.That(flame==null,Is.True);Assert.That(Node("StageShadowDeath_51"),Is.Not.Null);Assert.That(root.GetComponentsInChildren<EnemyFlameFeedback>(true),Is.Empty);
            Release();Assert.That(root.transform.childCount,Is.Zero);
        }
        /// <summary>墓穴的正式丧尸演员消费前摇/收招，命中序列不能重播旧片段。</summary>
        [Test] public void GraveActorUsesAuthorityGrabWithoutPostHitReplay()
        {
            Ready(23);Set("mapRoot",root.transform);view.RecordRoundEnemyPosition(61,Vector2.zero);State(61,18,0,3,0,0,0);
            var actor=Node("StageHumanoid_61");var hand=actor.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Middle1.R");
            view.SetEnemyMelee(61,.4f,.1f,0,Vector2.right);var rear=hand.position;
            view.SetEnemyMelee(61,.4f,0,.55f,Vector2.right);var contact=hand.position;
            Assert.That(Vector3.Distance(rear,contact),Is.GreaterThan(.55f));
            Assert.That(Vector3.Angle(actor.forward,Vector3.right),Is.LessThan(.1f));
            view.SetEnemy(61,"unused",Vector2.zero,10,10,attackSequence:1);view.TickPlayerFeedback(.1f);
            Assert.That(Vector3.Distance(hand.position,contact),Is.LessThan(.01f),"允许待机呼吸微动，但不能从旧Attack片段重新起手");
            State(61,18,0,3,0,0,0);view.SetEnemyMelee(61,.4f,0,.55f,Vector2.right);
            Assert.That(Vector3.Distance(hand.position,contact),Is.LessThan(.01f),"允许待机呼吸微动，重复阶段和快照不得累积旋转");
            Release();Assert.That(actor==null,Is.True);
        }
        [Test] public void FormalStageAttackSequenceSamplesOriginalClipOnceWithoutLateReplay()
        {
            Ready(24);view.SetEnemyContent(60,19,0,false);view.SetEnemy(60,"unused",new Vector2(2,-2),10,10,attackSequence:5);State(60,19,0,0,-5,-4,1);
            var actor=Node("StageHumanoid_60");var animator=actor.GetComponentInChildren<Animator>(true);var clips=animator.runtimeAnimatorController.animationClips;
            var idle=clips.First(c=>c.name.Contains("Idle")&&!c.name.Contains("Attack"));var attack=clips.First(c=>c.name.Contains("Attack"));
            var referenceParent=new GameObject("inactive-animation-reference");referenceParent.transform.SetParent(root.transform,false);referenceParent.SetActive(false);
            var reference=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeShadowZombie.prefab"),referenceParent.transform,false);
            var referenceAnimator=reference.GetComponentInChildren<Animator>(true);var arm=actor.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="LowerArm.R");var referenceArm=reference.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="LowerArm.R");float elapsed=0;
            void Tick(float seconds){elapsed+=seconds;view.TickPlayerFeedback(seconds);}
            void SamePose(AnimationClip clip,float time,string message){clip.SampleAnimation(referenceAnimator.gameObject,time);Assert.That(Quaternion.Angle(arm.localRotation,referenceArm.localRotation),Is.LessThan(.05f),message);}
            Tick(.1f);SamePose(idle,elapsed,"晚到的首个序列只建立基线，不补播历史挥击");
            view.SetEnemy(60,"unused",new Vector2(2,-2),10,10,attackSequence:6);Tick(.2f);SamePose(attack,.2f,"实际攻击序列增加必须播放正式攻击动作");
            view.SetEnemy(60,"unused",new Vector2(2,-2),10,10,attackSequence:6);State(60,19,0,0,-5,-4,1);Tick(.1f);SamePose(attack,.3f,"重复序列和位置快照不能重播或被待机覆盖");
            Tick(attack.length+.1f);SamePose(idle,elapsed,"攻击片段结束才恢复待机");
            State(60,19,3,.1f,-5,-4,0);view.SetEnemy(60,"unused",new Vector2(2,-2),10,10,attackSequence:7);State(60,19,0,0,-5,-4,0);Tick(.1f);SamePose(idle,elapsed,"未现身阶段仅更新序列基线，重生不能补播挥击");
            view.SetEnemy(60,"unused",new Vector2(2,-2),10,10,attackSequence:8);Tick(.2f);SamePose(attack,.2f,"重生后的下一次真实攻击仍可播放");
            view.RemoveEnemy(60);Assert.That(actor==null,Is.True);
        }
        [Test] public void GraveDeathsTransferToExistingCorpsePipelineAndRejectForeignOwnershipSafely()
        {
            var architecture=new global::Game.Game();architecture.RegisterUtility<IViewInstanceProvider>(new UnityViewInstanceProvider());var enemies=new EnemyCtrl(architecture);
            typeof(EnemyCtrl).GetField("arcadeRoot",Hidden).SetValue(enemies,root.transform);typeof(EnemyCtrl).GetField("arcadeMapInstanceId",Hidden).SetValue(enemies,1L);
            typeof(EnemyCtrl).GetMethod("CreateRagdollFloor",Hidden).Invoke(enemies,null);Set("enemyPresentation",enemies);
            var dying=(System.Collections.IList)typeof(EnemyCtrl).GetField("dyingArcadeEnemies",Hidden).GetValue(enemies);
            try
            {
                Ready(23);
                for(int mode=0;mode<3;mode++)
                {
                    int id=70+mode;view.SetEnemyContent(id,18,0,false);view.SetEnemy(id,"unused",new Vector2(8,-10.85f),10,10);State(id,18,0,0,8,-8.35f,0);
                    var actor=Node("StageHumanoid_"+id);var enemy=actor.GetComponent<EnemyView>();var reaction=actor.GetComponent<EnemyPhysicalReaction>();
                    view.RemoveEnemy(id,true,Vector2.right,mode==2,mode==1);
                    Assert.That(actor!=null,Is.True,"墓穴普通丧尸须复用既有死亡保留，不能直接销毁");Assert.That(reaction.IsRagdoll,Is.True);Assert.That(dying.Count,Is.EqualTo(1));
                    Assert.That(enemy.GetComponentInChildren<EnemyFlameFeedback>(true).IsBurning,Is.EqualTo(mode==1));
                    Assert.That(actor.parent,Is.EqualTo(root.transform));Assert.That(Node("NextStageEnemy_"+id),Is.Null);
                    if(mode==2)Assert.That(actor.GetComponentsInChildren<Rigidbody>(true).Any(b=>!b.isKinematic),Is.True,"爆炸沿原物理管线");
                    typeof(EnemyCtrl).GetMethod("TickArcadeFeedback",Hidden).Invoke(enemies,new object[]{12f,Time.time+12});Assert.That(actor==null,Is.True,"到原尸体寿命后销毁外部实例");
                    Assert.That(((System.Collections.IDictionary)typeof(EnemyCtrl).GetField("arcadeEnemyPool",Hidden).GetValue(enemies)).Count,Is.Zero,"外部演员不得混进原Prefab池");
                }
                view.SetEnemyContent(75,18,0,false);view.SetEnemy(75,"unused",Vector2.zero,10,10);State(75,18,0,0,0,0,0);var last=Node("StageHumanoid_75");view.RemoveEnemy(75,true);
                var foreignRoot=new GameObject("foreign-corpse-owner");
                try
                {
                    var foreign=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab"),foreignRoot.transform,false).GetComponent<EnemyView>();
                    var method=typeof(EnemyCtrl).GetMethod("TryPlayExternalEnemyDeath");Assert.That(method,Is.Not.Null);Assert.That((bool)method.Invoke(enemies,new object[]{999,foreign,Vector3.right,false,false,0}),Is.False);Assert.That(foreign.transform.parent,Is.EqualTo(foreignRoot.transform));
                }
                finally{UnityEngine.Object.DestroyImmediate(foreignRoot);}
                enemies.ReleaseCurrentMap();Assert.That(last==null,Is.True,"换代清理立即释放已接管尸体");
                view.SetEnemyContent(76,18,0,false);view.SetEnemy(76,"unused",Vector2.zero,10,10);State(76,18,0,0,0,0,0);var refused=Node("StageHumanoid_76");view.RemoveEnemy(76,true);Assert.That(refused==null,Is.True,"接管拒绝后原拥有者仍安全销毁");
            }
            finally{enemies.ReleaseAll();Set("enemyPresentation",null);}
        }
        [Test] public void StageHealthLossUsesExistingHitFlashAndPhysicalResponseWithoutLateOrHiddenReplay()
        {
            Ready(23);view.SetEnemyContent(80,18,0,false);view.SetEnemy(80,"unused",Vector2.zero,20,20);State(80,18,0,0,0,0,0);
            var actor=Node("StageHumanoid_80");var renderer=actor.GetComponentsInChildren<Renderer>(true).First(r=>!(r is ParticleSystemRenderer));var block=new MaterialPropertyBlock();
            view.SetEnemy(80,"unused",Vector2.zero,15,20);renderer.GetPropertyBlock(block);Assert.That(block.GetColor("_BaseColor"),Is.EqualTo(Color.red),"实际掉血应沿既有EnemyView闪色");
            var reaction=actor.GetComponent<EnemyPhysicalReaction>();var chest=(Transform)typeof(EnemyPhysicalReaction).GetField("chest",Hidden).GetValue(reaction);var animator=actor.GetComponentInChildren<Animator>(true);var idle=animator.runtimeAnimatorController.animationClips.First(c=>c.name.Contains("Idle")&&!c.name.Contains("Attack"));
            var referenceParent=new GameObject("inactive-hit-reference");referenceParent.transform.SetParent(root.transform,false);referenceParent.SetActive(false);var reference=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab"),referenceParent.transform,false);var referenceAnimator=reference.GetComponentInChildren<Animator>(true);var referenceChest=reference.GetComponentsInChildren<Transform>(true).Single(t=>t.name==chest.name);
            view.TickPlayerFeedback(.05f);idle.SampleAnimation(referenceAnimator.gameObject,.05f);Assert.That(Quaternion.Angle(chest.localRotation,referenceChest.localRotation),Is.GreaterThan(.1f),"既有胸部受击动作仍叠加在正式动画上");
            view.TickPlayerFeedback(.2f);renderer.GetPropertyBlock(block);Assert.That(block.GetColor("_BaseColor"),Is.Not.EqualTo(Color.red));idle.SampleAnimation(referenceAnimator.gameObject,.25f);Assert.That(Quaternion.Angle(chest.localRotation,referenceChest.localRotation),Is.LessThan(.05f));
            view.SetEnemy(80,"unused",Vector2.zero,15,20);renderer.GetPropertyBlock(block);Assert.That(block.GetColor("_BaseColor"),Is.Not.EqualTo(Color.red),"重复健康快照不重播受击");Release();
            Ready(24);view.SetEnemyContent(81,19,0,false);view.SetEnemy(81,"unused",Vector2.zero,20,20);State(81,19,2,.5f,0,0,1);view.SetEnemy(81,"unused",Vector2.zero,10,20);State(81,19,0,0,0,0,1);
            renderer=Node("StageHumanoid_81").GetComponentsInChildren<Renderer>(true).First(r=>!(r is ParticleSystemRenderer));renderer.GetPropertyBlock(block);Assert.That(block.GetColor("_BaseColor"),Is.Not.EqualTo(Color.red),"隐藏期只更新基线，现身不补播旧受击");
            view.SetEnemy(81,"unused",Vector2.zero,9,20);renderer.GetPropertyBlock(block);Assert.That(block.GetColor("_BaseColor"),Is.EqualTo(Color.red));Release();Assert.That(Node("StageHumanoid_81"),Is.Null);
        }
        sealed class Loader:IResourceLoader
        {
            public readonly List<Action<bool>> Pending=new();public readonly HashSet<object> Released=new();readonly HashSet<int> finished=new();
            public T LoadAsset<T>(string path)where T:UnityEngine.Object=>throw new InvalidOperationException("不可同步加载");
            public ResourceLoadHandle LoadAssetAsync<T>(string path,Action<T> callback)where T:UnityEngine.Object
            {var token=new object();Pending.Add(fail=>callback(fail?null:AssetDatabase.LoadAssetAtPath<T>("Assets/Game/"+path+".prefab")));return new ResourceLoadHandle(token);}
            public void Release(ResourceLoadHandle handle)=>Assert.That(Released.Add(handle.RawHandle),Is.True,"句柄只能归还一次");
            public void Finish(int index,bool fail=false){if(finished.Add(index))Pending[index](fail);}
            public void FinishAll(){for(int i=0;i<Pending.Count;i++)Finish(i);}
        }
    }
}
