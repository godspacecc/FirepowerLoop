using UnityEngine;
namespace Game
{
    public sealed partial class EnemyView
    {
        /// <summary>预载于独立特殊敌人Prefab的地面红圈，只有权威前摇才显示。</summary>
        [SerializeField] private LineRenderer survivorWarning;
        /// <summary>仅记住上一帧抬手显示，以便取消时撤手；不是业务施法状态。</summary>
        private bool survivorCasting;
        /// <summary>首次使用缓存正式钢板与两条十字装饰；不包含身体Renderer。</summary>
        private Renderer[] survivorShieldRenderers;
        /// <summary>仅应用装备可见性，身体与受击组件保持原状。</summary>
        public void SetSurvivorShield(bool intact)
        {
            if(survivorShieldRenderers==null)
                survivorShieldRenderers=System.Array.FindAll(GetComponentsInChildren<Renderer>(true),r=>r.name=="ShieldSteel"||r.name=="ShieldCross");
            foreach(var renderer in survivorShieldRenderers)if(renderer!=null)renderer.enabled=intact;
        }
        /// <summary>消费完整权威快照；红圈不计时、不判定伤害，朝向覆盖追踪。</summary>
        public void SetSurvivorSpecial(string kind,Vector3 direction,bool warning,float progress,float radius)
        {
            if(direction.sqrMagnitude>.0001f)transform.rotation=Quaternion.LookRotation(direction.normalized,transform.up);
            if(survivorWarning!=null)
            {
                survivorWarning.enabled=warning&&kind=="Exploder";
                survivorWarning.transform.localScale=Vector3.one*Mathf.Max(0,radius);
                var c=new Color(1,.15f,.03f,Mathf.Lerp(.45f,1,Mathf.Clamp01(progress)));
                survivorWarning.startColor=survivorWarning.endColor=c;
            }
            // 复用现有按权威阶段采样的抬手动作，不从Animator反向裁决攻击。
            if(kind=="Caster")
            {
                if(warning)SetMeleeAttack(1,1-Mathf.Clamp01(progress),0,direction);
                else if(survivorCasting)SetMeleeAttack(0,0,0,direction);
                survivorCasting=warning;
            }
        }
        /// <summary>死亡、禁用与回池只清前摇；装备复原限定在新用途或显式回池。</summary>
        private void ResetSurvivorSpecial()
        { ResetSurvivorEnchantment();if(survivorWarning!=null)survivorWarning.enabled=false;survivorCasting=false; }
    }
}
