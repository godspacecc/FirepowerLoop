using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using FirepowerLoop.Arcade;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>只缓存当前轮的权威阶段和演员；所有阶段时间由快照提供。</summary>
        private sealed class StageEnemyVisual
        {
            public int Kind;public bool HasState,HasPosition;
            public ArcadeStageEnemyState State;
            public Vector2 Position,LastPosition;
            public GameObject Owner,Humanoid,Crow,Travel;
            public Animator Animator;
            public bool Moving;
            /// <summary>既有命中火焰由本帧入口推进一次；攻击序列只播放新观察到的真实挥击。</summary>
            public EnemyFlameFeedback Flame;
            public EnemyView View;
            public bool HasHealthBaseline;
            public int LastHealth;
            public bool HasAttackBaseline,AttackPlaying;
            public int LastAttackSequence;
            public float AttackElapsed;
            /// <summary>抓挠阶段按权威采样，跨阶段隐藏时清理，异步演员就绪后应用缓存。</summary>
            public bool MeleeDriven;
            public float MeleeDuration,MeleeRemaining,MeleeRecovery;
            public Vector3 MeleeDirection;
        }
        private sealed class StageShadowDeath
        {
            public GameObject Root;public float Age;
        }
        private readonly Dictionary<int,StageEnemyVisual> nextStageEnemies=new();
        private readonly Dictionary<int,int> nextStageAttackSequences=new();
        private readonly Dictionary<int,int> nextStageHealthSnapshots=new();
        private readonly List<StageShadowDeath> nextStageDeaths=new();
        /// <summary>普通行走动画共享表现帧，不能据此推进起身、现身或换位阶段。</summary>
        private float nextStageAnimationTime;

        /// <summary>所有依赖先发起；仅在同一轮代次仍有效且正式动画/节点完整时提交模板。</summary>
        private async UniTask LoadNextStageTemplates(EarlyStageLoad state,int arena)
        {
            if(arena<23||arena>25)return;
            var body=new UniTaskCompletionSource<GameObject>();var crow=new UniTaskCompletionSource<GameObject>();var travel=new UniTaskCompletionSource<GameObject>();
            state.StageBodyHandle=ResourceLoader.LoadAssetAsync<GameObject>(arena==23?"Prefabs/Characters/ArcadeZombieShambler":"Prefabs/Characters/ArcadeShadowZombie",p=>body.TrySetResult(p));
            if(arena!=23)
            {
                state.StageCrowHandle=ResourceLoader.LoadAssetAsync<GameObject>("Prefabs/SceneObjects/ArcadeShadowCrowVisual",p=>crow.TrySetResult(p));
                state.StageTravelHandle=ResourceLoader.LoadAssetAsync<GameObject>("Prefabs/SceneObjects/ArcadeShadowTravelVisual",p=>travel.TrySetResult(p));
            }
            var actor=await body.Task;var bird=arena==23?null:await crow.Task;var shadow=arena==23?null:await travel.Task;
            if(!EarlyStageLoadIsCurrent(state))return;
            var animator=actor!=null?actor.GetComponentInChildren<Animator>(true):null;
            var clips=animator?.runtimeAnimatorController?.animationClips;
            var idle=clips?.FirstOrDefault(c=>c!=null&&c.name.IndexOf("Idle",StringComparison.OrdinalIgnoreCase)>=0);
            // 当前正式普通丧尸使用通用骨架Walk；旧Humanoid仍以Run命名，均只采样该Prefab自己的片段。
            var run=clips?.FirstOrDefault(c=>c!=null&&(c.name.IndexOf("Run",StringComparison.OrdinalIgnoreCase)>=0||c.name.IndexOf("Walk",StringComparison.OrdinalIgnoreCase)>=0));
            var attack=clips?.FirstOrDefault(c=>c!=null&&c.name.IndexOf("Attack",StringComparison.OrdinalIgnoreCase)>=0);
            if(actor==null||actor.GetComponent<EnemyView>()==null||actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length==0||animator==null||idle==null||run==null||attack==null||actor.GetComponentInChildren<EnemyFlameFeedback>(true)==null)
                throw new InvalidOperationException("三关正式丧尸、待机/移动/攻击动画或既有火焰缺失");
            if(arena!=23&&(bird==null||bird.transform.Find("WingLeft")==null||bird.transform.Find("WingRight")==null||shadow==null||shadow.transform.Find("Core")==null||Enumerable.Range(0,6).Any(i=>shadow.transform.Find("Mote"+i)==null)))
                throw new InvalidOperationException("暗影乌鸦或蓝影模板节点缺失");
            foreach(var prefab in new[]{actor,bird,shadow}.Where(p=>p!=null))
                if(prefab.GetComponentsInChildren<Renderer>(true).Any(r=>r.sharedMaterials.Any(m=>m==null||m.shader==null)))throw new InvalidOperationException("三关演员材质缺失");
            state.StageBody=actor;state.StageCrow=bird;state.StageTravel=shadow;state.StageIdle=idle;state.StageRun=run;state.StageAttack=attack;
        }

        /// <summary>专用身份先抑制旧替身和普通EnemyCtrl，等阶段/模板齐备再显示。</summary>
        private void SetNextStageIdentity(int id,int kind)
        {
            if(nextStageEnemies.TryGetValue(id,out var old)&&old.Kind!=kind)RemoveNextStageEnemy(id,false);
            if(!nextStageEnemies.ContainsKey(id))
            {
                var visual=new StageEnemyVisual{Kind=kind};
                if(nextStageAttackSequences.TryGetValue(id,out var sequence)){visual.HasAttackBaseline=true;visual.LastAttackSequence=sequence;}
                if(nextStageHealthSnapshots.TryGetValue(id,out var health)){visual.HasHealthBaseline=true;visual.LastHealth=health;}
                nextStageEnemies.Add(id,visual);
            }
            enemyPresentation?.RemoveEnemy(id,false);
        }

        /// <summary>只投影已采用阶段；忽略旧阶段和旧年龄，重连直接采样当前位置而不补播。</summary>
        public void SetNextStageEnemyState(int id,int kind,ArcadeStageEnemyState state)
        {
            if(kind!=18&&kind!=19){RemoveNextStageEnemy(id,false);return;}
            if(roundContentRoot==null||earlyStage==null||earlyStage.ArenaConfigNumber<23||earlyStage.ArenaConfigNumber>25||
                !FiniteStage(state.Age)||!FiniteStage(state.OriginX)||!FiniteStage(state.OriginY)||state.Age<0||state.Phase<0||state.Phase>3)return;
            SetNextStageIdentity(id,kind);var visual=nextStageEnemies[id];
            if(visual.HasState&&OlderStageState(visual.Kind,visual.State,state))return;
            visual.HasState=true;visual.State=state;ProjectNextStageEnemy(id);
        }
        private static bool FiniteStage(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        private static int StageOrder(int kind,ArcadeStageEnemyState state)
            =>kind==18?(state.Phase==1?0:1):state.RevivesRemaining>0?(state.Phase==2?0:1):(state.Phase==3?2:3);
        private static bool OlderStageState(int kind,ArcadeStageEnemyState old,ArcadeStageEnemyState incoming)
        {
            if(incoming.RevivesRemaining>old.RevivesRemaining)return true;
            if(incoming.RevivesRemaining<old.RevivesRemaining)return false;
            int before=StageOrder(kind,old),after=StageOrder(kind,incoming);
            return after<before||after==before&&incoming.Phase==old.Phase&&incoming.Age<old.Age;
        }
        private void ProjectNextStageEnemies(){foreach(var id in nextStageEnemies.Keys.ToArray())ProjectNextStageEnemy(id);}
        private void RecordNextStageEnemyPosition(int id,Vector2 position)
        {
            if(!nextStageEnemies.TryGetValue(id,out var visual))return;
            visual.Moving=visual.HasPosition&&(position-visual.Position).sqrMagnitude>.00001f;
            visual.LastPosition=visual.Position;visual.Position=position;visual.HasPosition=true;
            ProjectNextStageEnemy(id);
        }
        /// <summary>首份/旧份序列仅作基线；只有实体已现身后的新序列才触发原有攻击片段。</summary>
        private void RecordNextStageEnemyAttack(int id,int sequence)
        {
            if(nextStageAttackSequences.TryGetValue(id,out var latest)&&sequence<latest)return;
            nextStageAttackSequences[id]=sequence;
            if(!nextStageEnemies.TryGetValue(id,out var visual))return;
            bool increased=visual.HasAttackBaseline&&sequence>visual.LastAttackSequence;
            visual.HasAttackBaseline=true;visual.LastAttackSequence=sequence;
            if(visual.MeleeDriven||!increased||!visual.HasState||visual.State.Phase!=0||visual.Humanoid==null||!visual.Humanoid.activeInHierarchy)return;
            visual.AttackPlaying=true;visual.AttackElapsed=0;SampleNextStageActor(visual,false,0);
        }
        /// <summary>健康值仅用于比较新受击；首次、治疗、重复和隐藏态不补播旧反馈，也不创建健康条。</summary>
        private void RecordNextStageEnemyHealth(int id,int health)
        {
            nextStageHealthSnapshots[id]=health;
            if(!nextStageEnemies.TryGetValue(id,out var visual))return;
            if(visual.HasHealthBaseline&&health<visual.LastHealth&&visual.View!=null&&visual.Humanoid.activeInHierarchy&&visual.State.Phase!=2&&visual.State.Phase!=3)
                visual.View.PlayHitFeedback();
            visual.HasHealthBaseline=true;visual.LastHealth=health;
        }
        /// <summary>沿用Prefab自带受击火焰；隐藏态不把附着火焰转移到乌鸦或蓝影上。</summary>
        private bool SetNextStageEnemyBurning(int id,float seconds)
        {
            if(!nextStageEnemies.TryGetValue(id,out var visual))return false;
            if(visual.Flame!=null)
            {
                if(visual.Humanoid!=null&&visual.Humanoid.activeInHierarchy)visual.Flame.SetRemaining(seconds);
                else visual.Flame.ResetForReuse();
            }
            return true;
        }

        /// <summary>演员始终在失活父节点中装配，关闭所有战斗脚本、碰撞和HUD，再交给本轮投影。</summary>
        private GameObject CreateStageHumanoid(GameObject prefab,Transform parent,int id,out Animator animator)
        {
            var go=UnityEngine.Object.Instantiate(prefab,parent,false);go.name="StageHumanoid_"+id;
            // EnemyFlameFeedback没有Update，保留OnDisable清理；其Tick仅由本Controller已有帧入口调用。
            foreach(var script in go.GetComponentsInChildren<MonoBehaviour>(true))script.enabled=script is EnemyFlameFeedback;
            foreach(var collider in go.GetComponentsInChildren<Collider>(true))collider.enabled=false;
            foreach(var agent in go.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true))agent.enabled=false;
            foreach(var canvas in go.GetComponentsInChildren<Canvas>(true))canvas.enabled=false;
            animator=go.GetComponentInChildren<Animator>(true);animator.applyRootMotion=false;animator.enabled=false;
            return go;
        }
        private void ProjectNextStageEnemy(int id)
        {
            if(!nextStageEnemies.TryGetValue(id,out var visual)||!visual.HasState||!IsRoundLayoutReady||earlyStageLoad?.StageBody==null||roundEnemyRoot==null)return;
            if(!visual.HasPosition&&roundEnemyPositions.TryGetValue(id,out var position))
            {
                visual.LastPosition=visual.Position;visual.Position=position;visual.HasPosition=true;
            }
            if(!visual.HasPosition)return;
            if(visual.Owner==null)
            {
                var holder=RoundGroup(roundEnemyRoot.transform,"NextStageEnemy_"+id);holder.SetActive(false);visual.Owner=holder;
                visual.Humanoid=CreateStageHumanoid(earlyStageLoad.StageBody,holder.transform,id,out visual.Animator);
                visual.View=visual.Humanoid.GetComponent<EnemyView>();visual.View.ResetPhysicalFeedback();
                visual.Flame=visual.Humanoid.GetComponentInChildren<EnemyFlameFeedback>(true);visual.Flame.ResetForReuse();
                if(visual.Kind==19)
                {
                    visual.Crow=UnityEngine.Object.Instantiate(earlyStageLoad.StageCrow,holder.transform,false);visual.Crow.name="StageCrow_"+id;
                    visual.Travel=UnityEngine.Object.Instantiate(earlyStageLoad.StageTravel,holder.transform,false);visual.Travel.name="StageTravel_"+id;
                }
                holder.SetActive(true);
            }
            var state=visual.State;float age=state.Age;var origin=new Vector3(state.OriginX,0,state.OriginY);var target=new Vector3(visual.Position.x,0,visual.Position.y);
            bool grave=visual.Kind==18&&state.Phase==1,arrival=visual.Kind==19&&state.Phase==2,travel=visual.Kind==19&&state.Phase==3;
            if(state.Phase!=0){visual.AttackPlaying=false;visual.AttackElapsed=0;}
            if((arrival||travel)&&(visual.Humanoid.activeSelf||visual.Flame.IsBurning)){visual.View.ClearHitFeedback();visual.View.ResetPhysicalFeedback();}
            visual.Humanoid.SetActive(!arrival&&!travel);if(visual.Crow!=null)visual.Crow.SetActive(arrival);if(visual.Travel!=null)visual.Travel.SetActive(arrival||travel);
            ApplyNextStageMelee(visual);
            SampleNextStageActor(visual,grave,age);
            visual.Humanoid.transform.localPosition=target;
            if(grave)
            {
                if(age<=roundLayout.SkillWarning)visual.Humanoid.transform.localPosition=origin+Vector3.up*Mathf.Lerp(-1.65f,.4f,Mathf.Clamp01(age/roundLayout.SkillWarning));
                else
                {
                    float u=Mathf.Clamp01((age-roundLayout.SkillWarning)/(roundLayout.SkillPeriod-roundLayout.SkillWarning));
                    visual.Humanoid.transform.localPosition=Vector3.Lerp(origin+Vector3.up*.4f,target,u)+Vector3.up*Mathf.Sin(u*Mathf.PI)*.23f;
                }
                visual.Humanoid.transform.localRotation=Quaternion.Euler(0,180,0);
            }
            else if(visual.Moving)
            {var delta=visual.Position-visual.LastPosition;visual.Humanoid.transform.localRotation=Quaternion.LookRotation(new Vector3(delta.x,0,delta.y));}
            else if(!visual.HasPosition||visual.Humanoid.transform.localRotation==Quaternion.identity)visual.Humanoid.transform.localRotation=Quaternion.Euler(0,180,0);
            ApplyNextStageMelee(visual);
            if(arrival)
            {
                visual.Crow.transform.localPosition=Vector3.Lerp(origin+new Vector3(-5,5,2),origin+Vector3.up*.5f,Mathf.Clamp01(age/roundLayout.SkillWarning));
                visual.Crow.transform.Find("WingLeft").localRotation=Quaternion.Euler(0,0,Mathf.Sin(age*25)*38);
                visual.Crow.transform.Find("WingRight").localRotation=Quaternion.Euler(0,0,-Mathf.Sin(age*25)*38);
            }
            if(arrival||travel)
            {
                visual.Travel.transform.localPosition=(arrival?origin:Vector3.Lerp(origin,target,Mathf.Clamp01(age/roundLayout.EventWarning)))+Vector3.up*.65f;
                SampleStageBlue(visual.Travel.transform,age,arrival ? .2f+.25f*Mathf.Sin(age*18)*Mathf.Sin(age*18) : .38f);
            }
        }
        /// <summary>独立墓穴/暗影演员也消费同一抓挠阶段，保留自己的现身与换位表现。</summary>
        private bool SetNextStageEnemyMelee(int id,float duration,float remaining,float recovery,Vector3 direction)
        {
            if(!nextStageEnemies.TryGetValue(id,out var visual))return false;
            visual.MeleeDriven=true;visual.AttackPlaying=false;visual.MeleeDuration=duration;
            visual.MeleeRemaining=remaining;visual.MeleeRecovery=recovery;visual.MeleeDirection=direction;
            ApplyNextStageMelee(visual);
            if(visual.View!=null&&visual.HasState)SampleNextStageActor(visual,visual.Kind==18&&visual.State.Phase==1,visual.State.Age);
            return true;
        }
        private static void ApplyNextStageMelee(StageEnemyVisual visual)
        {
            if(!visual.MeleeDriven||visual.View==null)return;
            bool active=visual.HasState&&visual.State.Phase==0;
            visual.View.SetMeleeAttack(visual.MeleeDuration,active?visual.MeleeRemaining:0,active?visual.MeleeRecovery:0,visual.MeleeDirection);
        }
        /// <summary>挥击期间位置/阶段快照不以待机覆盖；只采样正式角色自身动画。</summary>
        private void SampleNextStageActor(StageEnemyVisual visual,bool grave,float age)
        {
            // ResetPhysicalFeedback会恢复Animator，专用活体仍由此处手工采样，死亡接管后才交回原动画生命周期。
            visual.Animator.enabled=false;
            var clip=visual.MeleeDriven&&(visual.MeleeRemaining>0||visual.MeleeRecovery>0)&&visual.State.Phase==0?earlyStageLoad.StageIdle:
                visual.AttackPlaying&&visual.State.Phase==0?earlyStageLoad.StageAttack:
                grave&&age<=roundLayout.SkillWarning||!visual.Moving&&!grave?earlyStageLoad.StageIdle:earlyStageLoad.StageRun;
            var position=visual.Humanoid.transform.localPosition;var rotation=visual.Humanoid.transform.localRotation;
            visual.View.TickCombatFeedback(0);
            clip.SampleAnimation(visual.Animator.gameObject,visual.AttackPlaying?visual.AttackElapsed:grave?age:nextStageAnimationTime);
            visual.Humanoid.transform.localPosition=position;visual.Humanoid.transform.localRotation=rotation;
            visual.View.LateTickCombatFeedback();
        }
        /// <summary>同源蓝影中心与六光点姿态，根的移动仅消费快照端点。</summary>
        private static void SampleStageBlue(Transform root,float age,float scale)
        {
            root.Find("Core").localScale=Vector3.one*scale;
            for(int i=0;i<6;i++)
            {
                var mote=root.Find("Mote"+i);float angle=age*7+i*Mathf.PI/3;
                mote.localPosition=new Vector3(Mathf.Cos(angle),Mathf.Sin(angle)*.45f,Mathf.Sin(angle))*(.3f+i*.045f);
                mote.localScale=Vector3.one*Mathf.Min(.1f,scale*.35f);mote.gameObject.SetActive(scale>.015f);
            }
        }
        /// <summary>最终死亡仅保留短蓝影；重复移除或重连没有历史死亡可补播。</summary>
        private void RemoveNextStageEnemy(int id,bool playDeath,Vector3 direction=default,bool explosive=false,bool flameKill=false,int impactKind=0)
        {
            if(!nextStageEnemies.TryGetValue(id,out var visual))return;
            nextStageEnemies.Remove(id);nextStageAttackSequences.Remove(id);nextStageHealthSnapshots.Remove(id);
            bool transferred=playDeath&&visual.Kind==18&&visual.View!=null&&enemyPresentation!=null&&
                enemyPresentation.TryPlayExternalEnemyDeath(id,visual.View,direction,explosive,flameKill,impactKind);
            if(transferred){visual.Humanoid=null;visual.View=null;visual.Flame=null;}
            else visual.Flame?.ResetForReuse();
            if(playDeath&&visual.Kind==19&&visual.Owner!=null&&earlyStageLoad?.StageTravel!=null&&roundEnemyRoot!=null)
            {
                while(nextStageDeaths.Count>=16){DestroyRoundVisual(nextStageDeaths[0].Root);nextStageDeaths.RemoveAt(0);}
                var go=UnityEngine.Object.Instantiate(earlyStageLoad.StageTravel,roundEnemyRoot.transform,false);go.name="StageShadowDeath_"+id;
                go.transform.localPosition=new Vector3(visual.Position.x,.65f,visual.Position.y);SampleStageBlue(go.transform,0,.5f);nextStageDeaths.Add(new StageShadowDeath{Root=go});
            }
            DestroyRoundVisual(visual.Owner);
        }
        private void TickNextStageFeedback(float seconds)
        {
            if(seconds<=0)return;nextStageAnimationTime+=seconds;
            foreach(var visual in nextStageEnemies.Values)
            {
                visual.View?.TickCombatFeedback(seconds);
                if(visual.Humanoid==null||!visual.Humanoid.activeSelf||visual.State.Phase!=0)continue;
                if(visual.AttackPlaying){visual.AttackElapsed+=seconds;if(visual.AttackElapsed>=earlyStageLoad.StageAttack.length)visual.AttackPlaying=false;}
                SampleNextStageActor(visual,false,0);
            }
            for(int i=nextStageDeaths.Count-1;i>=0;i--)
            {
                var death=nextStageDeaths[i];death.Age+=seconds;
                if(death.Root==null||death.Age>=.7f){DestroyRoundVisual(death.Root);nextStageDeaths.RemoveAt(i);continue;}
                SampleStageBlue(death.Root.transform,death.Age,Mathf.Max(0,.5f-death.Age*.7f));
            }
        }
        /// <summary>轮根/模板释放前立即清掉活演员和死亡蓝影，取消缓存不影响下一代。</summary>
        private void ClearNextStageEnemies()
        {
            foreach(var visual in nextStageEnemies.Values){visual.Flame?.ResetForReuse();visual.View?.ClearHitFeedback();DestroyRoundVisual(visual.Owner);}nextStageEnemies.Clear();nextStageAttackSequences.Clear();nextStageHealthSnapshots.Clear();
            foreach(var death in nextStageDeaths)DestroyRoundVisual(death.Root);nextStageDeaths.Clear();nextStageAnimationTime=0;
        }
    }
}
