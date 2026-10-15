using System;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>持有一个场景循环声及可选点火声；只负责表现句柄，不判定建造、地图加载或燃料规则。</summary>
    public sealed class MapLoopSoundPresenter : IDisposable
    {
        private readonly IArchitecture architecture;
        private readonly string soundName;
        private readonly SoundPlaybackOptions options;
        private readonly float maxDistance;
        private readonly IUnRegister stateSubscription;
        /// <summary>本表现持有的循环声和点火声播放身份；停止后置空，资源由 AudioSystem 统一释放。</summary>
        private AudioPlaybackHandle loop, ignition;
        private Vector3 position;
        private bool disposed;
        public MapLoopSoundPresenter(Transform owner, IArchitecture architecture, string soundName,
            string ignitionName, SoundPlaybackOptions options, float maxDistance)
        {
            this.architecture = architecture; this.soundName = soundName;
            this.options = options; this.maxDistance = maxDistance;
            position = owner != null ? owner.position : Vector3.zero;
            stateSubscription = architecture.RegisterEvent<AppStateChangedEvent>(e =>
            {
                if (e.Current == AppState.Playing) Start(); else Stop();
            });
            if (architecture.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing) return;
            Start();
            // 加载地图或恢复暂停只恢复循环，只有运行中激活的新火堆才播放点火。
            if (!string.IsNullOrEmpty(ignitionName))
                ignition = Audio.PlaySoundAt(ignitionName, position, maxDistance, -15,
                    new SoundPlaybackOptions(options.Gain, options.MinDistance, 0.25f));
        }
        private AudioSystem Audio => architecture.GetSystem<AudioSystem>();
        private void Start()
        {
            if (!disposed && !loop.IsValid)
                loop = Audio.PlayLoopAt(soundName, position, maxDistance, -20, options);
        }
        /// <summary>河流最近点更新可早于资源加载完成；迟到回调仍使用最新位置。</summary>
        public void SetPosition(Vector3 value) { position = value; Audio.SetSoundPosition(loop, value); }
        /// <summary>对象停用或状态退出时清理声音；允许架构先释放，重复停止无副作用。</summary>
        private void Stop()
        {
            // 架构反初始化可能早于场景 OnDisable；此时 AudioSystem 已清理声音并从容器移除。
            var audio = Audio;
            audio?.Stop(loop); audio?.Stop(ignition);
            loop = ignition = AudioPlaybackHandle.Empty;
        }
        /// <summary>解除状态订阅并停止声音；重复释放不再访问架构或播放句柄。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; stateSubscription.UnRegister(); Stop();
        }
    }
}
