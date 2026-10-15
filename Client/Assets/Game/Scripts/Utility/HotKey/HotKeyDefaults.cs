using System.Collections.Generic;

namespace Game
{
    /// <summary>正式街机的通用热键动作；战斗输入由街机自己维护。</summary>
    public static class HotKeyActionIds
    {
        public const string TogglePause = "game.pause.toggle";
        public const string ToggleRuntimeLog = "runtime-log.toggle";
    }

    /// <summary>设置首次创建和清洗时采用的默认绑定。</summary>
    public static class HotKeyDefaults
    {
        public const string Escape = "<Keyboard>/escape";
        public const string F1 = "<Keyboard>/f1";
        public static List<HotKeyBinding> Create() => new List<HotKeyBinding>
        {
            new HotKeyBinding
            {
                ActionId = HotKeyActionIds.TogglePause,
                Context = HotKeyContext.Gameplay,
                BindingPath = Escape
            },
            new HotKeyBinding
            {
                ActionId = HotKeyActionIds.ToggleRuntimeLog,
                Context = HotKeyContext.Global,
                BindingPath = F1
            }
        };
    }
}
