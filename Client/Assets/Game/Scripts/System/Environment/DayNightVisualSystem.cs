using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 提供由基地存档时刻或远征游戏分钟推导出的昼夜表现快照，不持有 Unity 表现对象。
    /// </summary>
    public readonly struct DayNightVisualSnapshot
    {
        public DayNightVisualSnapshot(float dayProgress, bool followsExpeditionTime)
        {
            DayProgress = Mathf.Repeat(dayProgress, 1f);
            FollowsExpeditionTime = followsExpeditionTime;
        }

        public float DayProgress { get; }
        public bool FollowsExpeditionTime { get; }
    }

    public readonly struct DayNightVisualChangedEvent
    {
        public DayNightVisualChangedEvent(DayNightVisualSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public DayNightVisualSnapshot Snapshot { get; }
    }

    /// <summary>
    /// 推进基地 Model 的昼夜时刻，并把既有远征时间翻译为昼夜快照。
    /// 基地时刻通过 SaveSystem 保存；不写远征耗时、不参与基地恢复结算，联机地图保持正午。
    /// </summary>
    public sealed class DayNightVisualSystem : AbstractSystem
    {
        internal const float FixedDayProgress = 0.5f;
        private bool hasPublishedContinuousProgress;
        private float lastPublishedContinuousProgress;

        protected override void OnInit()
        {
            if (!this.GetSystem<SaveSystem>().RegisterSection(this.GetModel<BaseDayNightModel>(), out var error))
                throw new System.InvalidOperationException("注册基地昼夜存档分区失败：" + error);
            this.RegisterEvent<ExpeditionChangedEvent>(_ => Publish());
            this.RegisterEvent<MapLoadingCompletedEvent>(_ => Publish());
            this.RegisterEvent<MapCurrentReleasedEvent>(_ => Publish());
            LifeCycle.RegisterUpdate(OnUpdate);
            LifeCycle.RegisterLateUpdate(OnLateUpdate);
            LifeCycle.RegisterApplicationPause(OnApplicationPause);
        }

        protected override void OnDeinit()
        {
            LifeCycle.UnregisterUpdate(OnUpdate);
            LifeCycle.UnregisterLateUpdate(OnLateUpdate);
            LifeCycle.UnregisterApplicationPause(OnApplicationPause);
        }

        public DayNightVisualSnapshot GetSnapshot()
        {
            var map = this.GetModel<MapSceneModel>();
            var expedition = this.GetModel<ExpeditionModel>();
            if (IsOfflineBase(map))
            {
                return new DayNightVisualSnapshot(CalculateDayProgress(this.GetModel<BaseDayNightModel>().MinuteOfDay), false);
            }
            var followsExpeditionTime = map.Status.Value == MapLoadStatus.Loaded &&
                                        map.CurrentMode == MapPreparationMode.OfflineFull &&
                                        expedition.IsActive &&
                                        string.Equals(map.CurrentMapId, expedition.CurrentMapId, System.StringComparison.Ordinal) &&
                                        MapCatalog.TryGet(map.CurrentMapId, out var definition) &&
                                        (definition.Kind == MapKind.Survival || definition.Kind == MapKind.Task);

            return followsExpeditionTime
                ? new DayNightVisualSnapshot(CalculateDayProgress(expedition.ClockGameMinutes), true)
                : new DayNightVisualSnapshot(FixedDayProgress, false);
        }

        internal static float CalculateDayProgress(float elapsedGameMinutes)
        {
            if (float.IsNaN(elapsedGameMinutes) || float.IsInfinity(elapsedGameMinutes))
            {
                return 0f;
            }

            return Mathf.Repeat(elapsedGameMinutes, PlayerConditionRules.GameMinutesPerDay) /
                   PlayerConditionRules.GameMinutesPerDay;
        }

        private void Publish()
        {
            var snapshot = GetSnapshot();
            RememberContinuousProgress(snapshot);
            this.SendEvent(new DayNightVisualChangedEvent(snapshot));
        }

        private void OnLateUpdate()
        {
            var snapshot = GetSnapshot();
            if (!snapshot.FollowsExpeditionTime && !IsOfflineBase(this.GetModel<MapSceneModel>()))
            {
                hasPublishedContinuousProgress = false;
                return;
            }

            if (hasPublishedContinuousProgress &&
                Mathf.Approximately(lastPublishedContinuousProgress, snapshot.DayProgress))
            {
                return;
            }

            RememberContinuousProgress(snapshot);
            this.SendEvent(new DayNightVisualChangedEvent(snapshot));
        }

        private void RememberContinuousProgress(DayNightVisualSnapshot snapshot)
        {
            hasPublishedContinuousProgress = snapshot.FollowsExpeditionTime || IsOfflineBase(this.GetModel<MapSceneModel>());
            lastPublishedContinuousProgress = snapshot.DayProgress;
        }

        private void OnUpdate() => TickBaseClock(Time.deltaTime);

        /// <summary>
        /// 共享 Update 推进基地时刻；暂停、加载、离开基地和非离线模式保留原值。
        /// 仅影响视觉，绝不提交远征时间或基地恢复结算。
        /// </summary>
        internal void TickBaseClock(float realDeltaSeconds)
        {
            if (float.IsNaN(realDeltaSeconds) || float.IsInfinity(realDeltaSeconds) || realDeltaSeconds <= 0f ||
                !IsOfflineBase(this.GetModel<MapSceneModel>()) ||
                this.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing)
            {
                return;
            }

            // 先折算为一天内增量，避免长时间运行使累计浮点数持续变大。
            var minutes = Mathf.Repeat(realDeltaSeconds * PlayerConditionRules.GameMinutesPerRealSecond,
                PlayerConditionRules.GameMinutesPerDay);
            var clock = this.GetModel<BaseDayNightModel>();
            var previousMinute = Mathf.FloorToInt(clock.MinuteOfDay);
            clock.Advance(minutes);
            // 每游戏分钟标脏，给既有合并保存留出窗口；退出与挂起另行保存最后的小数分钟。
            if (previousMinute != Mathf.FloorToInt(clock.MinuteOfDay))
                this.GetSystem<SaveSystem>().MarkDirty("base day-night minute changed");
        }

        /// <summary>移动端挂起时同步保存当前基地时刻，防止后台终止跳过退出回调；不推进时钟。</summary>
        private void OnApplicationPause(bool paused)
        {
            if (!paused || this.GetModel<SaveModel>().CurrentDomain != OfflineSaveDomain.Base) return;
            var save = this.GetSystem<SaveSystem>();
            save.MarkDirty("base day-night before application pause");
            if (!save.Flush(out var error)) Log.Error("[DayNightVisualSystem] 挂起前基地存档刷新失败：" + error);
        }

        private static bool IsOfflineBase(MapSceneModel map) =>
            map.Status.Value == MapLoadStatus.Loaded && map.CurrentMode == MapPreparationMode.OfflineFull &&
            string.Equals(map.CurrentMapId, MapIds.Base, System.StringComparison.Ordinal);
    }
}
