using System;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>地图内小龙的只读表现缓存；资源仍由既有聚合模板事务拥有。</summary>
        private sealed class FinalDragonVisual
        {
            public ArcadeFinalDragonView Actor;
            public float Age=-1,Distance;
            /// <summary>最近一次射线结束的权威年龄；仅决定上身收招，不延迟AI。</summary>
            public float RayEndsAt=-1;
            public bool Moving;
        }
        private ArcadeFinalDragonView FinalDragonTemplate=>contentTemplates?.Find("FinalDragon")?.GetComponent<ArcadeFinalDragonView>();
        private bool FinalDragonDependenciesReady=>roundLayout?.BossKind!=4||(FinalDragonTemplate!=null&&FinalDragonTemplate.IsReady);
        private FinalDragonVisual CreateFinalDragon(Transform holder)
        {
            var source=FinalDragonTemplate;
            if(source==null||!source.IsReady)throw new InvalidOperationException("终局小龙模板未准备");
            var actor=UnityEngine.Object.Instantiate(source,holder,false);actor.name="FinalDragonActor";
            actor.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);actor.gameObject.SetActive(true);actor.SamplePose("Idle",0);
            return new FinalDragonVisual{Actor=actor};
        }
        /// <summary>按原Boss配置窗口映射采用版动作；世界位置只接收原权威位置入口。</summary>
        public void SetFinalDragonState(int id,float age,ArcadeBossPhase phase,float remaining,ArcadeBossDefinition boss,Vector2 facing)
        {
            if(!roundEnemies.TryGetValue(id,out var visual)||visual.Dragon==null||!CreatureFinite(age)||age<0||
                !CreatureFinite(remaining)||remaining<0||!CreatureFinite(facing.x)||!CreatureFinite(facing.y)||age<=visual.Dragon.Age)return;
            var d=visual.Dragon;d.Age=age;
            if(facing.sqrMagnitude>.000001f)visual.Root.transform.localRotation=Quaternion.LookRotation(new Vector3(facing.x,0,facing.y));
            if(phase==ArcadeBossPhase.Telegraph){d.RayEndsAt=-1;d.Actor.SamplePose("Windup",DragonProgress(remaining,boss.TelegraphSeconds)*.9f);}
            else if(phase==ArcadeBossPhase.Charging){d.RayEndsAt=-1;d.Actor.SamplePose("Charge",DragonProgress(remaining,boss.ChargeDurationSeconds)*.76f);}
            else if(phase==ArcadeBossPhase.RayTelegraph){d.RayEndsAt=age+remaining;d.Actor.SamplePose("Roar",DragonProgress(remaining,boss.RayTelegraphSeconds)*.9f);}
            else
            {
                if(d.Moving)d.Actor.SamplePose("Move",Mathf.Repeat(d.Distance/.52f,1.05f));
                else d.Actor.SamplePose("Idle",Mathf.PingPong(age,1.5f));
                // 收招只覆盖颈头、下颌和翼，四足继续按真实追击位移行走。
                float recovery=age-d.RayEndsAt;
                if(d.RayEndsAt>=0&&recovery>=0&&recovery<1.2f)d.Actor.SamplePose("RoarUpper",.9f+recovery);
                else if(d.RayEndsAt>=0&&recovery>=1.2f&&recovery<3.4f)d.Actor.SamplePose("WingsUpper",recovery-1.2f);
            }
        }
        private static float DragonProgress(float remaining,float duration)=>CreatureFinite(duration)&&duration>0?Mathf.Clamp01(1-remaining/duration):0;
    }
}
