using System;
using System.Collections.Generic;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>一个角色的脚步表现和播放句柄拥有者；由既有控制器驱动，不写领域状态或自行 Update。</summary>
    public sealed class FootstepPresenter : IDisposable
    {
        private readonly PlayerView view;
        private readonly IArchitecture architecture;
        private readonly FootstepProfile profile;
        private readonly FootstepCadence cadence;
        private readonly FootstepGroundProbe ground = new FootstepGroundProbe();
        private readonly Queue<AudioPlaybackHandle> playing = new Queue<AudioPlaybackHandle>();
        private readonly Dictionary<string[], int> lastSelections = new Dictionary<string[], int>();
        private readonly IUnRegister stateSubscription;
        private bool alive = true, disposed;
        private Transform parent;
        private Matrix4x4 parentMatrix;

        public FootstepPresenter(PlayerView view, IArchitecture architecture)
        {
            this.view = view;
            this.architecture = architecture;
            profile = view != null ? view.Footsteps : null;
            if (profile == null) return;
            cadence = new FootstepCadence(profile.WalkStride, profile.RunStride, profile.MinimumInterval, profile.FastSpeed);
            stateSubscription = architecture.RegisterEvent<AppStateChangedEvent>(e => { if (e.Current != AppState.Playing) Reset(); });
            CaptureParent();
        }

        public float CorrectionDistance => profile != null ? profile.CorrectionDistance : 2f;

        /// <summary>存活同值通知是幂等的，避免远端每次快照清零计步。</summary>
        public void SetAlive(bool value) { if (alive == value) return; alive = value; Reset(); }

        /// <summary>移动完成后传入真实世界位移；远端落地由窄查询判断，本机额外要求 Controller 落地。</summary>
        public void Tick(Vector3 actualMotion, float deltaTime, bool eligible, bool requireControllerGrounded)
        {
            if (disposed || cadence == null || view == null) return;
            var currentParent = view.transform.parent;
            var matrix = currentParent != null ? currentParent.localToWorldMatrix : Matrix4x4.identity;
            if (currentParent != parent || matrix != parentMatrix) { Reset(); CaptureParent(); return; }
            bool allowed = eligible && alive && view.gameObject.activeInHierarchy &&
                architecture.GetModel<AppStateModel>().CurrentState.Value == AppState.Playing;
            actualMotion.y = 0f;
            if (!allowed || actualMotion.sqrMagnitude <= 0.00000001f) { Reset(); return; }
            bool supported = ground.TryGet(view.transform, view.CharacterController, out var surface);
            if (requireControllerGrounded) supported &= view.CharacterController != null && view.CharacterController.isGrounded;
            if (!cadence.Step(actualMotion.magnitude, deltaTime, allowed, supported)) return;
            var candidates = profile.GetSounds(surface, cadence.IsFast);
            if (candidates == null || candidates.Length == 0) return;
            int index = UnityEngine.Random.Range(0, candidates.Length);
            if (candidates.Length > 1 && lastSelections.TryGetValue(candidates, out var last) && index == last)
                index = (index + UnityEngine.Random.Range(1, candidates.Length)) % candidates.Length;
            lastSelections[candidates] = index;
            var audio = architecture.GetSystem<AudioSystem>();
            if (audio == null) return;
            while (playing.Count >= 2) audio.Stop(playing.Dequeue());
            // 复用本次实际速度判定，跑转走立即恢复轻步，不读取或修改冲刺权威。
            float gain = cadence.IsFast ? profile.RunGain : profile.Gain;
            var handle = audio.PlaySoundAt(candidates[index], view.transform.position, profile.MaxDistance, -10,
                new SoundPlaybackOptions(gain, profile.MinDistance, profile.MaximumStartDelay));
            if (handle.IsValid) playing.Enqueue(handle);
        }

        /// <summary>停步、离散定位和停用取消本角色请求；重复清理不重复释放资源。</summary>
        public void Reset()
        {
            cadence?.Reset();
            if (playing.Count == 0) return;
            var audio = architecture.GetSystem<AudioSystem>();
            while (playing.Count > 0)
            {
                var handle = playing.Dequeue();
                audio?.Stop(handle);
            }
        }

        private void CaptureParent()
        {
            parent = view != null ? view.transform.parent : null;
            parentMatrix = parent != null ? parent.localToWorldMatrix : Matrix4x4.identity;
        }

        public void Dispose() { if (disposed) return; Reset(); disposed = true; stateSubscription?.UnRegister(); }
    }
}
