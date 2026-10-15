namespace Game
{
    /// <summary>测试外观的会话编号发生变化；消费者重新读模型，事件不携带 Unity 对象。</summary>
    public readonly struct PlayerTestAppearanceChangedEvent { }
    public readonly struct UnitHealthBarVisibilityChangedEvent
    {
        public UnitHealthBarVisibilityChangedEvent(bool visible)
        {
            Visible = visible;
        }

        public bool Visible { get; }
    }
}
