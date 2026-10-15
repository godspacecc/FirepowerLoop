namespace Game
{
    /// <summary>
    /// 定义玩家可聚焦并按键确认的基地交互对象最小接口。
    /// </summary>
    public interface IPlayerInteractable
    {
        string DisplayName { get; }
        bool CanInteract { get; }
        void SetFocused(bool focused);
        void Interact();
    }

    public interface IPlayerSearchable
    {
        bool CanSearch { get; }
        void Search();
    }
}
