using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 抽象时间缩放写入边界，便于 AppStateSystem 在测试和 Unity Player 中使用不同实现。
    /// </summary>
    public interface ITimeScaleController
    {
        float TimeScale { get; set; }
    }

    /// <summary>
    /// 将应用暂停状态落到 Unity Time.timeScale，是 AppStateSystem 的默认 Player 适配器。
    /// </summary>
    public sealed class UnityTimeScaleController : ITimeScaleController
    {
        public float TimeScale
        {
            get => Time.timeScale;
            set => Time.timeScale = value;
        }
    }

    /// <summary>
    /// 校验应用状态迁移并在暂停、恢复时控制时间缩放，非法迁移只记录诊断并保持原状态。
    /// </summary>
    public sealed class AppStateSystem : AbstractSystem
    {
        private ITimeScaleController timeScaleController = new UnityTimeScaleController();
        private float previousTimeScale = 1f;

        protected override void OnInit()
        {
            // 启动稳定在无地图 Paused；不能依赖场景或 Inspector 的时间缩放值。
            timeScaleController.TimeScale = 0f;
        }

        public void SetTimeScaleControllerForTests(ITimeScaleController controller)
        {
            timeScaleController = controller ?? new UnityTimeScaleController();
        }

        public bool TryTransition(AppState target)
        {
            var model = this.GetModel<AppStateModel>();
            var previous = model.CurrentState.Value;
            if (previous == target)
            {
                return true;
            }

            if (!CanTransition(previous, target))
            {
                Log.Warning($"[AppStateSystem] 非法状态切换：{previous} -> {target}");
                return false;
            }

            ApplyPauseSideEffects(previous, target);
            model.CurrentState.Value = target;
            this.SendEvent(new AppStateChangedEvent(previous, target));
            return true;
        }

        public bool TryPause()
        {
            return this.GetModel<AppStateModel>().CurrentState.Value == AppState.Playing &&
                   TryTransition(AppState.Paused);
        }

        public bool TryResume()
        {
            return this.GetModel<AppStateModel>().CurrentState.Value == AppState.Paused &&
                   TryTransition(AppState.Playing);
        }

        public bool TryRestoreStableStateAfterLoading(AppState stableState)
        {
            var model = this.GetModel<AppStateModel>();
            if (model.CurrentState.Value != AppState.Loading)
            {
                if (model.CurrentState.Value == stableState)
                {
                    return true;
                }

                if (!TryTransition(AppState.Loading))
                {
                    return false;
                }
            }

            switch (stableState)
            {
                case AppState.Playing:
                    return TryTransition(AppState.Playing);
                case AppState.Paused:
                    return TryTransition(AppState.Playing) && TryTransition(AppState.Paused);
                default:
                    return false;
            }
        }

        private static bool CanTransition(AppState from, AppState to)
        {
            switch (from)
            {
                case AppState.Loading:
                    return to == AppState.Playing || to == AppState.Paused;
                case AppState.Playing:
                    return to == AppState.Paused || to == AppState.Loading;
                case AppState.Paused:
                    return to == AppState.Playing || to == AppState.Loading;
                default:
                    return false;
            }
        }

        private void ApplyPauseSideEffects(AppState from, AppState to)
        {
            if (to == AppState.Paused)
            {
                previousTimeScale = timeScaleController.TimeScale;
                timeScaleController.TimeScale = 0f;
                return;
            }

            if (from == AppState.Paused)
            {
                timeScaleController.TimeScale = previousTimeScale;
                return;
            }

            if (to == AppState.Playing && timeScaleController.TimeScale <= 0f)
            {
                timeScaleController.TimeScale = 1f;
            }
        }
    }
}
