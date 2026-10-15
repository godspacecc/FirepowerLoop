using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 抽象设置保存的时间来源，用于延迟写盘节流和测试中的可控时间推进。
    /// </summary>
    public interface ISettingsClock
    {
        float Now { get; }
    }

    /// <summary>
    /// 设置系统默认时间源，使用 Unity 实时时间驱动延迟保存。
    /// </summary>
    public sealed class UnityRealtimeSettingsClock : ISettingsClock
    {
        public float Now => Time.realtimeSinceStartup;
    }

    /// <summary>
    /// 在资源与本地化配置就绪后显式加载并应用用户设置，运行期合并频繁改动并延迟写盘，反初始化时执行最终 Flush。
    /// </summary>
    public sealed class SettingsSystem : AbstractSystem
    {
        private const float SaveDelaySeconds = 0.5f;
        private ISettingsClock clock = new UnityRealtimeSettingsClock();
        private bool dirty;
        private float dirtyAt;
        private SettingsLoadResult lastLoadResult;

        public SettingsLoadResult LastLoadResult => lastLoadResult;

        protected override void OnInit()
        {
            LifeCycle.RegisterUpdate(Update);
        }

        protected override void OnDeinit()
        {
            Flush();
            LifeCycle.UnregisterUpdate(Update);
        }

        public void SetClockForTests(ISettingsClock clock)
        {
            this.clock = clock ?? new UnityRealtimeSettingsClock();
        }

        /// <summary>
        /// 启动阶段读取设置文件并应用到模型、显示和热键；文件缺失或损坏时由存储层回退为默认设置。
        /// </summary>
        public void LoadAndApply()
        {
            var data = this.GetUtility<ISettingsFileStore>().LoadOrDefault(out lastLoadResult);
            Apply(data);

            var displayModel = this.GetModel<DisplaySettingsModel>();
            this.GetUtility<IDisplayResolutionApplier>().Apply(
                displayModel.GetCurrentResolution(),
                displayModel.CurrentWindowMode.Value);
        }

        public void Apply(SettingsData data)
        {
            data = (data ?? SettingsData.CreateDefault()).Sanitize();
            this.GetModel<LocalizationModel>().SetLanguageCode(data.LanguageCode);
            this.GetModel<DisplaySettingsModel>().Apply(data.Display);
            ApplyMobileFrameRate();
            this.GetModel<AudioSettingsModel>().Apply(data.Audio);
            this.GetModel<HotKeyModel>().Apply(data.HotKeys);
            this.GetModel<TestToolSettingsModel>().Apply(data.TestTools);
            this.GetModel<MobileControlLayoutModel>().Apply(data.MobileControlLayout);
            this.SendEvent(new MobileControlLayoutChangedEvent());
            this.GetSystem<HotKeySystem>().RebuildActions();
            this.SendEvent(new HotKeyBindingsChangedEvent());
        }

        public void MarkDirty()
        {
            dirty = true;
            dirtyAt = clock.Now;
        }

        /// <summary>用户选择时提交帧率偏好；非法值不修改，同值不重复标脏，成功后进入延迟保存队列。</summary>
        public bool SetMobileFrameRate(int framesPerSecond)
        {
            if (framesPerSecond != 30 && framesPerSecond != 60) return false;
            var model = this.GetModel<DisplaySettingsModel>();
            if (model.CurrentMobileFrameRate.Value == framesPerSecond) return true;
            model.CurrentMobileFrameRate.Value = framesPerSecond;
            ApplyMobileFrameRate();
            MarkDirty();
            return true;
        }

        /// <summary>加载、重置和用户选择后立即应用安卓目标帧率；桌面沿用既有帧率策略。</summary>
        private void ApplyMobileFrameRate()
        {
            if (Application.platform == RuntimePlatform.Android)
                Application.targetFrameRate = this.GetModel<DisplaySettingsModel>().CurrentMobileFrameRate.Value;
        }

        public bool Flush()
        {
            if (!dirty)
            {
                return true;
            }

            var ok = SaveCurrent();
            if (ok)
            {
                dirty = false;
            }

            return ok;
        }

        public bool ResetAllToDefault()
        {
            Apply(SettingsData.CreateDefault());

            var display = this.GetModel<DisplaySettingsModel>();
            this.GetUtility<IDisplayResolutionApplier>().Apply(
                display.GetCurrentResolution(),
                display.CurrentWindowMode.Value);
            this.GetSystem<AudioSystem>().ApplyVolumes();

            MarkDirty();
            return Flush();
        }

        /// <summary>仅恢复通用页设置，并立即应用语言、显示参数和安卓帧率。</summary>
        public bool ResetGeneralToDefault()
        {
            var defaults = SettingsData.CreateDefault();
            var localization = this.GetModel<LocalizationModel>();
            var previousLanguage = localization.CurrentLanguageCode.Value;
            localization.SetLanguageCode(defaults.LanguageCode);
            this.SendEvent(new LanguageChangedEvent(previousLanguage, localization.CurrentLanguageCode.Value));
            this.GetModel<DisplaySettingsModel>().Apply(defaults.Display);
            ApplyMobileFrameRate();
            var display = this.GetModel<DisplaySettingsModel>();
            this.GetUtility<IDisplayResolutionApplier>().Apply(display.GetCurrentResolution(), display.CurrentWindowMode.Value);
            MarkDirty();
            return Flush();
        }

        /// <summary>仅恢复音频页设置，并立即同步到混音器。</summary>
        public bool ResetAudioToDefault()
        {
            this.GetModel<AudioSettingsModel>().Apply(SettingsData.CreateDefault().Audio);
            this.GetSystem<AudioSystem>().ApplyVolumes();
            MarkDirty();
            return Flush();
        }

        /// <summary>仅恢复操作页热键，并重建输入动作。</summary>
        public bool ResetHotKeysToDefault()
        {
            this.GetModel<HotKeyModel>().Apply(SettingsData.CreateDefault().HotKeys);
            this.GetSystem<HotKeySystem>().RebuildActions();
            this.SendEvent(new HotKeyBindingsChangedEvent());
            MarkDirty();
            return Flush();
        }

        /// <summary>保存编辑草稿；写盘失败回滚权威布局，界面保留草稿供重试，不发送已保存事件。</summary>
        public bool SaveMobileControlLayout(MobileControlLayoutData draft)
        {
            var model = this.GetModel<MobileControlLayoutModel>();
            var previous = model.ToData();
            model.Apply(draft);
            if (!SaveCurrent()) { model.Apply(previous); return false; }
            this.SendEvent(new MobileControlLayoutChangedEvent());
            return true;
        }

        private void Update()
        {
            if (dirty && clock.Now - dirtyAt >= SaveDelaySeconds)
            {
                Flush();
            }
        }

        /// <summary>
        /// 将当前模型中的设置快照写入本地文件；保存失败只返回 false 并记录诊断，不改变脏标记的后续重试语义。
        /// </summary>
        public bool SaveCurrent()
        {
            var data = new SettingsData
            {
                LanguageCode = this.GetModel<LocalizationModel>().CurrentLanguageCode.Value,
                Display = this.GetModel<DisplaySettingsModel>().ToData(),
                Audio = this.GetModel<AudioSettingsModel>().ToData(),
                HotKeys = this.GetModel<HotKeyModel>().ToData(),
                TestTools = this.GetModel<TestToolSettingsModel>().ToData(),
                MobileControlLayout = this.GetModel<MobileControlLayoutModel>().ToData()
            }.Sanitize();

            if (!this.GetUtility<ISettingsFileStore>().Save(data, out var error))
            {
                Log.Error($"[SettingsSystem] 保存设置失败：{error}");
                return false;
            }

            return true;
        }
    }
}
