using System;
using Game.Network;
using UnityEngine;

namespace Game
{
    internal enum PauseState
    {
        Paused,
        Unpaused
    }

    internal interface IOnlineHeartbeatLifecycle
    {
        void UpdateHeartbeat();
        void NotifyApplicationResumed();
        void StopHeartbeatForNetworkLifecycle();
    }

    internal interface IOnlineApplicationLifecycleSource : IDisposable
    {
        event Action<bool> FocusChanged;
        event Action<PauseState> PauseStateChanged;
    }

    public sealed class OnlineAccountLifecycleController : IDisposable
    {
        private readonly IOnlineApplicationLifecycleSource applicationLifecycle;
        private INetworkRuntime runtime;
        private IOnlineHeartbeatLifecycle heartbeatLifecycle;
        private bool registered;
        private bool backgrounded;
        /// <summary>分别记忆焦点和暂停信号，二者都恢复后才开始恢复心跳期限。</summary>
        private bool hasFocus = true;
        private bool isPaused;
        private bool disposed;

        public OnlineAccountLifecycleController()
            : this(new UnityOnlineApplicationLifecycleSource())
        {
        }

        internal OnlineAccountLifecycleController(IOnlineApplicationLifecycleSource applicationLifecycle)
        {
            this.applicationLifecycle = applicationLifecycle ?? throw new ArgumentNullException(nameof(applicationLifecycle));
        }

        internal void Initialize(INetworkRuntime networkRuntime, IOnlineHeartbeatLifecycle heartbeatLifecycle)
        {
            if (networkRuntime == null)
            {
                throw new ArgumentNullException(nameof(networkRuntime));
            }
            if (heartbeatLifecycle == null)
            {
                throw new ArgumentNullException(nameof(heartbeatLifecycle));
            }

            Unbind();
            disposed = false;
            runtime = networkRuntime;
            this.heartbeatLifecycle = heartbeatLifecycle;
            applicationLifecycle.FocusChanged += OnFocusChanged;
            applicationLifecycle.PauseStateChanged += OnPauseStateChanged;
            LifeCycle.RegisterUpdate(Update);
            registered = true;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Unbind();
            applicationLifecycle.Dispose();
        }

        private void Unbind()
        {
            heartbeatLifecycle?.StopHeartbeatForNetworkLifecycle();
            if (registered)
            {
                LifeCycle.UnregisterUpdate(Update);
                registered = false;
            }

            applicationLifecycle.FocusChanged -= OnFocusChanged;
            applicationLifecycle.PauseStateChanged -= OnPauseStateChanged;
            runtime = null;
            heartbeatLifecycle = null;
            backgrounded = false;
            hasFocus = true;
            isPaused = false;
        }

        private void Update()
        {
            runtime?.Update();
            heartbeatLifecycle?.UpdateHeartbeat();
        }

        private void OnFocusChanged(bool focused)
        {
            hasFocus = focused;
            if (!focused)
            {
                backgrounded = true;
                return;
            }

            ResumeIfNeeded();
        }

        private void OnPauseStateChanged(PauseState state)
        {
            isPaused = state == PauseState.Paused;
            if (state == PauseState.Paused)
            {
                backgrounded = true;
                return;
            }

            ResumeIfNeeded();
        }

        private void ResumeIfNeeded()
        {
            if (!backgrounded || !hasFocus || isPaused)
            {
                return;
            }

            backgrounded = false;
            heartbeatLifecycle?.NotifyApplicationResumed();
        }

        private sealed class UnityOnlineApplicationLifecycleSource : IOnlineApplicationLifecycleSource
        {
            public event Action<bool> FocusChanged;
            public event Action<PauseState> PauseStateChanged;

            public UnityOnlineApplicationLifecycleSource()
            {
                Application.focusChanged += OnFocusChanged;
                LifeCycle.RegisterApplicationPause(OnApplicationPause);
            }

            public void Dispose()
            {
                Application.focusChanged -= OnFocusChanged;
                LifeCycle.UnregisterApplicationPause(OnApplicationPause);
            }

            private void OnFocusChanged(bool focused)
            {
                FocusChanged?.Invoke(focused);
            }

            private void OnApplicationPause(bool paused)
            {
                PauseStateChanged?.Invoke(paused ? PauseState.Paused : PauseState.Unpaused);
            }
        }
    }
}
