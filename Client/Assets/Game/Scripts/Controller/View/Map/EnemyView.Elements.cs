namespace Game
{
    public sealed partial class EnemyView
    {
        /// <summary>可选序列化依赖仅存在于采用版丧尸，普通敌人不创建新特效。</summary>
        private EnemyElementFeedback elementFeedback;
        private bool elementFeedbackResolved;
        private EnemyElementFeedback ElementFeedback
        {
            get{if(!elementFeedbackResolved){elementFeedback=GetComponentInChildren<EnemyElementFeedback>(true);elementFeedbackResolved=true;}return elementFeedback;}
        }
        /// <summary>状态来自幸存者战斗裁决，红闪期间保留现有受击优先级。</summary>
        public void SetElementState(bool fire,bool frost){ElementFeedback?.SetState(fire,frost);ElementFeedback?.Tick(0,feedbackRemainingSeconds>0);}
        /// <summary>只由既有敌人控制器步进粒子，暂停传入零步长。</summary>
        public void TickElementFeedback(float seconds)=>ElementFeedback?.Tick(seconds,feedbackRemainingSeconds>0);
        /// <summary>实例换用途或死亡立即归还粒子和所有材质槽覆盖。</summary>
        public void ResetElementFeedback()=>ElementFeedback?.ResetForReuse();
        /// <summary>只有幸存者预载要求完整元素依赖，普通敌人继续沿原准备规则。</summary>
        public bool ValidateElementFeedback(out string error)
        {if(ElementFeedback!=null)return ElementFeedback.Validate(out error);error="幸存者丧尸缺少元素效果";return false;}
    }
}
