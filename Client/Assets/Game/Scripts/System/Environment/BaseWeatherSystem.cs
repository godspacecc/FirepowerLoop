using System;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>基地天气不可变表现快照；三个强度均为 0～1，不携带场景对象或存档身份。</summary>
    public readonly struct BaseWeatherSnapshot
    {
        public BaseWeatherSnapshot(float rain, float cloud, float wetness, string stage, bool active = true)
        { RainAmount = rain; CloudAmount = cloud; Wetness = wetness; Stage = stage; IsActive = active; }
        public float RainAmount { get; }
        public float CloudAmount { get; }
        public float Wetness { get; }
        public string Stage { get; }
        public bool IsActive { get; }
    }
    public readonly struct BaseWeatherChangedEvent
    {
        public BaseWeatherChangedEvent(BaseWeatherSnapshot snapshot) { Snapshot = snapshot; }
        public BaseWeatherSnapshot Snapshot { get; }
    }

    /// <summary>纯天气样板时序；秒数为实际运行秒，不推进游戏日历，不持有 Unity 对象。</summary>
    public sealed class BaseWeatherTimeline
    {
        private string mode = "dry";
        // 本次 demo 或干燥的有效运行秒；暂停时由调用者停止推进。
        private float elapsed;
        private float startingWetness;
        private BaseWeatherSnapshot snapshot = new BaseWeatherSnapshot(0, 0, 0, "晴天");
        public BaseWeatherSnapshot GetSnapshot() => snapshot;
        /// <summary>先验证再切换；重复放晴保留原干燥进度，无效模式不影响当前 demo。</summary>
        public bool SetMode(string value)
        {
            value = value?.ToLowerInvariant();
            if (value != "rain" && value != "clear" && value != "dry" && value != "demo") return false;
            if (value == "clear" && mode == "clear") return true;
            startingWetness = snapshot.Wetness; elapsed = 0; mode = value;
            snapshot = value == "rain" ? new BaseWeatherSnapshot(1, 1, 1, "降雨") :
                new BaseWeatherSnapshot(0, 0, value == "clear" ? startingWetness : 0, value == "clear" ? "地面干燥" : "晴天");
            return true;
        }
        public void Advance(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0) return;
            elapsed = Mathf.Min(220, elapsed + seconds);
            if (mode == "clear") snapshot = new BaseWeatherSnapshot(0, 0, startingWetness * (1 - Mathf.Clamp01(elapsed / 90)), "地面干燥");
            else if (mode == "demo")
            {
                if (elapsed <= 30) snapshot = new BaseWeatherSnapshot(0, 0, 0, "晴天");
                else if (elapsed <= 50) { var t = (elapsed - 30) / 20; snapshot = new BaseWeatherSnapshot(t, t, t, "转阴起雨"); }
                else if (elapsed <= 110) snapshot = new BaseWeatherSnapshot(1, 1, 1, "持续降雨");
                else if (elapsed <= 130) { var t = 1 - (elapsed - 110) / 20; snapshot = new BaseWeatherSnapshot(t, t, 1, "雨停放晴"); }
                else snapshot = new BaseWeatherSnapshot(0, 0, 1 - (elapsed - 130) / 90, "地面干燥");
            }
            if (snapshot.RainAmount == 0 && snapshot.Wetness == 0 && elapsed >= 90 && mode != "rain")
            { mode = "dry"; snapshot = new BaseWeatherSnapshot(0, 0, 0, "晴天"); }
        }
    }

    /// <summary>持有基地会话天气权威并通过共享生命周期推进；不保存、不控制日历、灯光或生存数值。</summary>
    public sealed class BaseWeatherSystem : AbstractSystem
    {
        private readonly BaseWeatherTimeline timeline = new BaseWeatherTimeline();
        protected override void OnInit()
        {
            this.RegisterEvent<MapLoadingStartedEvent>(_ => Reset());
            this.RegisterEvent<MapCurrentReleasedEvent>(_ => Reset());
            this.RegisterEvent<MapLoadingCompletedEvent>(_ => Publish());
            LifeCycle.RegisterUpdate(OnUpdate);
        }
        protected override void OnDeinit() { LifeCycle.UnregisterUpdate(OnUpdate); }
        private bool IsBase()
        {
            var map = this.GetModel<MapSceneModel>();
            return map.Status.Value == MapLoadStatus.Loaded && map.CurrentMode == MapPreparationMode.OfflineFull && map.CurrentMapId == MapIds.Base;
        }
        public BaseWeatherSnapshot GetSnapshot() => IsBase() ? timeline.GetSnapshot() : new BaseWeatherSnapshot(0, 0, 0, "天气样板未激活", false);
        /// <summary>测试入口；开发版、地图与状态校验完成后才提交，失败无副作用。</summary>
        public bool TrySetMode(string mode, out string message)
        {
            if ((!Application.isEditor && !Debug.isDebugBuild) || !IsBase() || this.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing)
            { message = "天气测试仅在编辑器或开发版的就绪离线基地开放。"; return false; }
            if (!timeline.SetMode(mode)) { message = "用法：#weather rain | clear | dry | demo | status"; return false; }
            Publish(); message = Describe(); return true;
        }
        public string Describe()
        {
            var s = GetSnapshot();
            return s.IsActive ? $"基地天气：{s.Stage}；雨量={s.RainAmount:F2} 云量={s.CloudAmount:F2} 湿润度={s.Wetness:F2}" : s.Stage;
        }
        private void OnUpdate() => Tick(Time.deltaTime);
        internal void Tick(float seconds)
        {
            if (IsBase() && this.GetModel<AppStateModel>().CurrentState.Value == AppState.Playing) timeline.Advance(seconds);
            // 同时通知表现暂停/恢复；粒子不另立业务 Update 或天气时钟。
            if (IsBase()) Publish();
        }
        private void Publish() => this.SendEvent(new BaseWeatherChangedEvent(GetSnapshot()));
        private void Reset() { timeline.SetMode("dry"); Publish(); }
    }
}
