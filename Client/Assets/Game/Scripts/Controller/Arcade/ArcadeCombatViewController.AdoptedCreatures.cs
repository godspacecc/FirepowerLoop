using System;
using FirepowerLoop.Arcade;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>本地图的表现采样缓存，生命周期随RoundEnemyVisual；没有独立资源所有权。</summary>
        private sealed class AdoptedCreatureVisual
        {
            public ArcadeAdoptedCreatureView Actor;
            public float Age=-1,Distance;
            public bool Moving;
        }
        private static bool UsesAdoptedCreature(int special,int boss)=>boss==2||boss==0&&(special==6||special==10||special==17);
        private ArcadeAdoptedCreatureView AdoptedTemplate(int special,int boss)
        {
            var name=boss==2?"ZRex":special==6?"Spider":special==10?"Ghoul":"RedMangler";
            var actor=contentTemplates?.Find(name)?.GetComponent<ArcadeAdoptedCreatureView>();return actor!=null&&actor.IsReady?actor:null;
        }
        private bool AdoptedDependenciesReady=>roundLayout==null||
            (!(roundLayout.SpecialKind==6||roundLayout.SpecialKind==10||roundLayout.SpecialKind==17)||AdoptedTemplate(roundLayout.SpecialKind,0)!=null)&&
            (roundLayout.BossKind!=2||AdoptedTemplate(0,2)!=null);
        private AdoptedCreatureVisual CreateAdoptedCreature(Transform holder,int special,int boss)
        {
            var source=AdoptedTemplate(special,boss);if(source==null)throw new InvalidOperationException("采用版演员模板未准备");
            var actor=UnityEngine.Object.Instantiate(source,holder,false);actor.name="AdoptedCreatureActor";actor.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);
            actor.gameObject.SetActive(true);actor.SamplePose("Idle",0);return new AdoptedCreatureVisual{Actor=actor};
        }
        /// <summary>仅消费真实平面位移；活动年龄未增加时不重新采样，重复/暂停快照保持原姿势。</summary>
        private void RecordAdoptedTravel(RoundEnemyVisual visual,Vector2 delta)
        {var c=visual.Adopted;if(c==null)return;c.Moving=delta.sqrMagnitude>.000001f;if(c.Moving)c.Distance+=delta.magnitude;}
        /// <summary>单人及联机共同投影已有年龄、朝向与近战窗口，不增加技能、伤害或根位移。</summary>
        public void SetAdoptedCreatureState(int id,float age,Vector2 facing,float meleeDuration,float meleeRemaining,float meleeRecovery)
        {
            if(!roundEnemies.TryGetValue(id,out var visual)||visual.Adopted==null||roundLayout==null||!CreatureFinite(age)||age<0||
                !CreatureFinite(facing.x)||!CreatureFinite(facing.y)||!CreatureFinite(meleeDuration)||!CreatureFinite(meleeRemaining)||!CreatureFinite(meleeRecovery)||
                meleeDuration<0||meleeRemaining<0||meleeRecovery<0||age<=visual.Adopted.Age)return;
            var c=visual.Adopted;c.Age=age;
            if(facing.sqrMagnitude>.000001f)visual.Root.transform.localRotation=Quaternion.LookRotation(new Vector3(facing.x,0,facing.y));
            float stride=visual.BossKind==2?1.2f:visual.SpecialKind==6?.65f:.8f;
            c.Actor.SampleProgress(c.Moving?"Move":"Idle",c.Moving?Mathf.Repeat(c.Distance/stride,1):0);
            float phase=age%roundLayout.SkillPeriod,warning=roundLayout.SkillWarning;
            if(visual.BossKind==2)
            {
                int attack=ArcadeRoundContentRules.BossAttack(2,age,roundLayout.SkillPeriod);
                if(phase<warning)c.Actor.SampleProgress(attack==0?"Windup":"Action",phase/warning);
                else if(attack==0)c.Actor.SampleProgress("Rush",.5f);
                else if(phase-warning<.5f)c.Actor.SamplePose("Recovery",phase-warning);
                return;
            }
            int kind=visual.SpecialKind;
            // 蜘蛛没有周期技能；食尸鬼的真实近战优先于活动冲进姿态。
            if(kind==6||kind==10)
            {
                if(meleeDuration>0&&meleeRemaining>0)
                {c.Actor.SampleProgress(kind==6?"Windup":"Action",1-meleeRemaining/meleeDuration);return;}
                if(meleeRecovery>0)
                {c.Actor.SampleProgress("Recovery",1-meleeRecovery/ArcadeMeleeRules.RecoverySeconds);return;}
            }
            if(kind==10)c.Actor.SampleProgress(phase<warning?"Windup":"Rush",phase<warning?phase/warning:.5f);
            else if(kind==17)
            {
                if(phase<warning)c.Actor.SampleProgress("Windup",phase/warning);
                else if(phase-warning<.17f)c.Actor.SamplePose("Action",phase-warning);
                else if(phase-warning<.72f)c.Actor.SamplePose("Recovery",phase-warning-.17f);
                else if(c.Moving)c.Actor.SampleProgress("Rush",.5f);
            }
        }
    }
}
