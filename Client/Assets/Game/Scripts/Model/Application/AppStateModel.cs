using QFramework;

namespace Game
{
    /// <summary>
    /// 定义应用运行状态机使用的稳定状态枚举。无地图开始界面也是 Paused。
    /// </summary>
    public enum AppState
    {
        Loading,
        Playing,
        Paused
    }

    /// <summary>
    /// 保存当前稳定应用状态，供 AppStateSystem 写入并让 UI 或流程代码观察状态变化。
    /// </summary>
    public sealed class AppStateModel : AbstractModel
    {
        public BindableProperty<AppState> CurrentState { get; } = new BindableProperty<AppState>(AppState.Paused);

        protected override void OnInit()
        {
        }
    }

    /// <summary>
    /// 应用状态切换后的通知载体，只描述前后状态，不承担迁移校验或时间缩放处理。
    /// </summary>
    public readonly struct AppStateChangedEvent
    {
        public AppStateChangedEvent(AppState previous, AppState current)
        {
            Previous = previous;
            Current = current;
        }

        public AppState Previous { get; }
        public AppState Current { get; }
    }
}
