using System;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>本轮演员的只读采样缓存；技能年龄、近战进度均来自权威快照。</summary>
        private sealed class CreatureVisual
        {
            public ArcadeCreatureView Actor;
            public float Age=-1,Distance;
            public bool Moving;
            public float MeleeDuration,MeleeRemaining,MeleeRecovery;
        }

        /// <summary>场地与聚合演员模板都到达后才公开就绪；聚合事务继续拥有失败与重试。</summary>
        private bool CreatureDependenciesReady => !NeedsCreatureTemplate || GetCreatureTemplate(roundLayout.SpecialKind)!=null;
        private bool NeedsCreatureTemplate => roundLayout!=null&&(roundLayout.SpecialKind==4||roundLayout.SpecialKind==5);
        private string CreatureDependencyError => NeedsCreatureTemplate&&contentTemplates!=null&&!CreatureDependenciesReady?"采用版四足演员模板缺失或不完整":null;
        private ArcadeCreatureView GetCreatureTemplate(int kind)
        {
            var template=contentTemplates?.Find(kind==4?"Burrower":"Hound");var actor=template==null?null:template.GetComponent<ArcadeCreatureView>();
            return actor!=null&&actor.IsReady?actor:null;
        }

        /// <summary>沿已加载Content模板实例化，无新异步请求或独立资源句柄。</summary>
        private CreatureVisual CreateCreature(Transform holder,int kind)
        {
            var source=GetCreatureTemplate(kind);
            if(source==null||!source.IsReady)throw new InvalidOperationException("采用版四足演员模板未准备："+kind);
            var actor=UnityEngine.Object.Instantiate(source,holder,false);actor.name="CreatureActor";
            actor.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);actor.gameObject.SetActive(true);
            actor.SamplePose("Idle",0);return new CreatureVisual{Actor=actor};
        }

        /// <summary>消费实际位移更新步态距离；只改姿态，根坐标仍由原位置入口投影。</summary>
        private void RecordCreatureTravel(RoundEnemyVisual visual,Vector2 delta)
        {
            if(visual.Creature==null)return;
            visual.Creature.Moving=delta.sqrMagnitude>.000001f;
            if(visual.Creature.Moving)visual.Creature.Distance+=delta.magnitude;
        }

        /// <summary>单人与双人共用的权威年龄／朝向入口；迟到首次直接采样，旧年龄不倒播。</summary>
        public void SetCreatureState(int id,float age,Vector2 facing)
        {
            if(!roundEnemies.TryGetValue(id,out var visual)||visual.Creature==null||!CreatureFinite(age)||age<0||
                !CreatureFinite(facing.x)||!CreatureFinite(facing.y)||age<visual.Creature.Age)return;
            visual.Creature.Age=age;
            if(facing.sqrMagnitude>.000001f)visual.Root.transform.localRotation=Quaternion.LookRotation(new Vector3(facing.x,0,facing.y));
            SampleCreature(visual);
        }

        private static bool CreatureFinite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);

        /// <summary>已有近战权威前摇和收招直接驱动口部；不产生额外攻击或位移。</summary>
        private bool SetCreatureMelee(int id,float duration,float remaining,float recovery,Vector2 facing)
        {
            if(!roundEnemies.TryGetValue(id,out var visual)||visual.Creature==null)return false;
            if(!CreatureFinite(duration)||!CreatureFinite(remaining)||!CreatureFinite(recovery)||duration<0||remaining<0||recovery<0)return true;
            var c=visual.Creature;c.MeleeDuration=duration;c.MeleeRemaining=remaining;c.MeleeRecovery=recovery;
            SampleCreature(visual);return true;
        }

        /// <summary>只按当前配置阶段选取已烘焙片段；演示时长压入真实窗口，不修改技能规则。</summary>
        private void SampleCreature(RoundEnemyVisual visual)
        {
            var c=visual.Creature;if(c==null||c.Actor==null||roundLayout==null||c.Age<0)return;
            float phase=c.Age%roundLayout.SkillPeriod,warning=roundLayout.SkillWarning;
            if(visual.SpecialKind==4&&phase<warning)
            {
                float p=phase/warning;
                if(p<.27f)c.Actor.SamplePose("Sink",p/.27f*1.35f);
                else if(p<.63f)c.Actor.SamplePose("Underground",(p-.27f)/.36f*1.65f);
                else c.Actor.SamplePose("Emerge",(p-.63f)/.37f*1.65f);
                return;
            }
            if(visual.SpecialKind==5)
            {
                if(phase<warning)
                {
                    if(phase<warning*.5f&&c.Moving)c.Actor.SamplePose("Move",Mathf.Repeat(c.Distance/.9f,.5f));
                    else c.Actor.SamplePose("Windup",Mathf.Clamp01((phase/warning-.5f)*2)*1.01f);
                    SampleCreatureMouth(c);return;
                }
                if(phase-warning<1.74f)c.Actor.SamplePose("Pounce",phase-warning);
                else if(c.Moving)c.Actor.SamplePose("Move",Mathf.Repeat(c.Distance/.9f,.5f));
                else c.Actor.SamplePose("Idle",0);
                SampleCreatureMouth(c);return;
            }
            if(c.MeleeDuration>0&&c.MeleeRemaining>0)
            {c.Actor.SamplePose("Bite",Mathf.Clamp01(1-c.MeleeRemaining/c.MeleeDuration)*.65f);return;}
            if(c.MeleeRecovery>0)
            {c.Actor.SamplePose("Bite",.8f+Mathf.Clamp01(1-c.MeleeRecovery/.4f)*.8f);return;}
            if(c.Moving)c.Actor.SamplePose("Move",visual.SpecialKind==4?Mathf.Repeat(c.Distance/.23f,1f):Mathf.Repeat(c.Distance/.9f,.5f));
            else c.Actor.SamplePose("Idle",0);
        }
        /// <summary>口颈近战覆盖技能身体姿态，贴身/撞墙仍显示真实前摇和收招。</summary>
        private void SampleCreatureMouth(CreatureVisual c)
        {
            if(c.MeleeDuration>0&&c.MeleeRemaining>0)c.Actor.SamplePose("BiteFace",Mathf.Clamp01(1-c.MeleeRemaining/c.MeleeDuration)*.65f);
            else if(c.MeleeRecovery>0)c.Actor.SamplePose("BiteFace",.8f+Mathf.Clamp01(1-c.MeleeRecovery/.4f)*.8f);
        }
    }
}
