using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// 集中定义项目内置热键动作 ID，避免设置文件和运行时代码使用散落字符串。
    /// </summary>
    public static class HotKeyActionIds
    {
        public const string TogglePause = "game.pause.toggle";
        public const string ToggleInventory = "inventory.toggle";
        public const string ToggleRuntimeLog = "runtime-log.toggle";
        public const string ToggleMap = "map.toggle";
        public const string ToggleFlashlight = "player.flashlight.toggle";
        /// <summary>常规离线电脑操作提示的显隐动作；可重绑，不负责整张 HUD 的生命周期。</summary>
        public const string ToggleGameplayGuide = "gameplay-guide.toggle";
    }

    /// <summary>
    /// 提供默认热键绑定集合，是设置清洗和首次启动时补齐绑定的来源。
    /// </summary>
    public static class HotKeyDefaults
    {
        public const string Escape = "<Keyboard>/escape";
        public const string Tab = "<Keyboard>/tab";
        public const string F1 = "<Keyboard>/f1";
        public const string Map = "<Keyboard>/m";
        public const string MapFallbackN = "<Keyboard>/n";
        public const string MapFallbackF2 = "<Keyboard>/f2";
        public const string MapFallbackF3 = "<Keyboard>/f3";
        public const string Flashlight = "<Keyboard>/f";
        public const string FlashlightFallbackL = "<Keyboard>/l";
        public const string FlashlightFallbackF4 = "<Keyboard>/f4";
        public const string GameplayGuide = "<Keyboard>/f5";

        public static List<HotKeyBinding> Create()
        {
            return new List<HotKeyBinding>
            {
                new HotKeyBinding
                {
                    ActionId = HotKeyActionIds.TogglePause,
                    Context = HotKeyContext.Gameplay,
                    BindingPath = Escape
                },
                new HotKeyBinding
                {
                    ActionId = HotKeyActionIds.ToggleInventory,
                    Context = HotKeyContext.Gameplay,
                    BindingPath = Tab
                },
                new HotKeyBinding
                {
                    ActionId = HotKeyActionIds.ToggleRuntimeLog,
                    Context = HotKeyContext.Global,
                    BindingPath = F1
                },
                new HotKeyBinding
                {
                    ActionId = HotKeyActionIds.ToggleMap,
                    Context = HotKeyContext.Gameplay,
                    BindingPath = Map
                },
                new HotKeyBinding
                {
                    ActionId = HotKeyActionIds.ToggleFlashlight,
                    Context = HotKeyContext.Gameplay,
                    BindingPath = Flashlight
                },
                new HotKeyBinding
                {
                    ActionId = HotKeyActionIds.ToggleGameplayGuide,
                    Context = HotKeyContext.Gameplay,
                    BindingPath = GameplayGuide
                },
            };
        }
    }
}
