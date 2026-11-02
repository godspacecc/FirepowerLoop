using System;using UnityEngine;using UnityEngine.UI;
namespace Game
{
    /// <summary>幸存者HUD短红边，只由可见期的活动时间推进，不阻挡输入。</summary>
    public sealed class SurvivorHurtEdgeView : MonoBehaviour
    {
        /// <summary>正式HUD四条边缘图像，只改颜色透明度，不接管输入。</summary>
        [SerializeField]private Image[] edges=Array.Empty<Image>();
        /// <summary>可见期活动秒数，暂停时保持原有透明度。</summary>
        private float age=1;
        /// <summary>本次真实生命损失立即刷新短红边。</summary>
        public void Play(){age=0;Render();}
        /// <summary>使用HUD已有回调推进，零步长冻结。</summary>
        public void Tick(float seconds){age+=Mathf.Max(0,seconds);Render();}
        /// <summary>隐藏、卸载或角色离场时清除历史红边。</summary>
        public void Clear(){age=1;Render();}
        /// <summary>按采用版0.2秒线性淡出，不修改生命或无敌状态。</summary>
        private void Render(){foreach(var edge in edges)if(edge!=null)edge.color=new Color(.8f,.05f,.06f,age<.2f?.28f*(1-age/.2f):0);}
        private void OnDisable()=>Clear();
    }
}
