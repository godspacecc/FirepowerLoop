using UnityEngine;
using UnityEngine.UI;
namespace Game
{
    /// <summary>整卡按钮的边框与底板反馈；只读取Selectable状态，不参与选择事务。</summary>
    public sealed class SurvivorUpgradeCardFeedback : Button
    {
        /// <summary>稳定内底板由Prefab绑定，边框沿用Button的targetGraphic。</summary>
        public Graphic Face;
        /// <summary>采用版默认、悬停与按下底色。</summary>
        public Color NormalFace=new Color(.105f,.16f,.19f), HighlightFace=new Color(.13f,.195f,.215f), PressedFace=new Color(.16f,.22f,.23f);
        /// <summary>跟随原生按钮的鼠标、键盘焦点与禁用状态同步底板。</summary>
        protected override void DoStateTransition(SelectionState state,bool instant)
        {
            base.DoStateTransition(state,instant);
            if(Face!=null)Face.color=state==SelectionState.Highlighted||state==SelectionState.Selected?HighlightFace:state==SelectionState.Pressed?PressedFace:NormalFace;
        }
    }
}
