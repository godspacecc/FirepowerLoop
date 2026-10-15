namespace Game
{
    /// <summary>
    /// 手机 HUD 的表现扩展；只控制可见性与摇杆归零，不决定玩法资格，也不修改生成的 View 引用。
    /// </summary>
    public static class MobileJoystickPresentation
    {
        /// <summary>菜单或加载状态切换时隐藏并归零摇杆，保留虚拟设备，避免重建正在使用的输入绑定。</summary>
        public static void SetContentVisible(this MobileJoystickUI ui, bool visible)
        {
            if (!visible && ui.ContentVisibility.interactable)
                ui.JoystickSurface.OnPointerUp(null);
            ui.ContentVisibility.alpha = visible ? 1f : 0f;
            ui.ContentVisibility.interactable = visible;
            ui.ContentVisibility.blocksRaycasts = visible;
        }
    }
}
