using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>设备输入模式只属于本次会话，不进入设置或玩家存档。</summary>
    public readonly struct MobileInputModeChangedEvent
    {
        public MobileInputModeChangedEvent(bool enabled)
        {
            Enabled = enabled;
        }

        public bool Enabled { get; }
    }

    /// <summary>街机共用的设备模式选择；攻击和摇杆由街机输入处理。</summary>
    public sealed class MobileInputSystem : AbstractSystem
    {
        private bool editorSimulationEnabled;
        public bool IsMobileInputMode => Application.platform == RuntimePlatform.Android || Application.isEditor && editorSimulationEnabled;

        protected override void OnInit()
        {
        }

        protected override void OnDeinit()
        {
            editorSimulationEnabled = false;
        }

        /// <summary>只切换当前 Editor 会话，Android 正式输入模式保持设备决定。</summary>
        public bool TrySetEditorSimulation(bool enabled)
        {
            if (!Application.isEditor)
                return false;
            if (editorSimulationEnabled == enabled)
                return true;
            editorSimulationEnabled = enabled;
            this.SendEvent(new MobileInputModeChangedEvent(IsMobileInputMode));
            return true;
        }
    }
}
