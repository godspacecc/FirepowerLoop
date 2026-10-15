using System;
using UnityEngine;

namespace Game
{
    /// <summary>随场景对象启停的环境声绑定；通过共享生命周期更新河道最近点，不写地图或建筑权威状态。</summary>
    public sealed class MapAmbientSoundView : MonoBehaviour
    {
        [SerializeField] private string soundName;
        [SerializeField] private string ignitionName;
        [SerializeField, Range(0f, 1f)] private float gain = 0.2f;
        /// <summary>空间衰减距离，单位米；近距包含俯视相机高度，避免地面声音过弱。</summary>
        [SerializeField] private float minDistance = 16f, maxDistance = 38f;
        /// <summary>所属对象本地坐标河道折线；空数组表示固定点声源，整条河只占一个声音通道。</summary>
        [SerializeField] private Vector3[] pathPoints = Array.Empty<Vector3>();
        private MapLoopSoundPresenter presenter;
        private AudioListener listener;
        private bool updating;

        /// <summary>生成器在禁用候选根内完成绑定；不能改变已经开始播放的对象配置。</summary>
        public void Configure(string sound, string ignition, float volume, float near, float far, Vector3[] path = null)
        {
            if (presenter != null) throw new InvalidOperationException("环境声必须在候选对象激活前配置");
            soundName = sound; ignitionName = ignition; gain = Mathf.Clamp01(volume);
            minDistance = Mathf.Max(0f, near); maxDistance = Mathf.Max(minDistance, far);
            pathPoints = path ?? Array.Empty<Vector3>();
        }
        private void OnEnable()
        {
            if (!Application.isPlaying || string.IsNullOrEmpty(soundName)) return;
            presenter = new MapLoopSoundPresenter(transform, Game.Interface, soundName, ignitionName,
                new SoundPlaybackOptions(gain, minDistance, 0f), maxDistance);
            if (pathPoints.Length < 2) return;
            Tick();
            LifeCycle.RegisterUpdate(Tick); updating = true;
        }
        private void OnDisable()
        {
            if (updating) LifeCycle.UnregisterUpdate(Tick);
            updating = false; presenter?.Dispose(); presenter = null; listener = null;
        }
        private void Tick()
        {
            if (presenter == null) return;
            if (listener == null || !listener.isActiveAndEnabled)
            {
                listener = null;
                foreach (var candidate in FindObjectsOfType<AudioListener>())
                    if (candidate.isActiveAndEnabled) { listener = candidate; break; }
            }
            if (listener != null)
                presenter.SetPosition(ClosestPoint(transform, pathPoints, listener.transform.position));
        }
        /// <summary>按真实世界线段求听者最近点；支持父节点平移、旋转及缩放，不按河段叠加声音。</summary>
        public static Vector3 ClosestPoint(Transform owner, Vector3[] points, Vector3 listenerPosition)
        {
            if (points == null || points.Length == 0) return owner.position;
            var closest = owner.TransformPoint(points[0]); var distance = float.PositiveInfinity;
            for (int i = 1; i < points.Length; i++)
            {
                var a = owner.TransformPoint(points[i - 1]); var b = owner.TransformPoint(points[i]);
                var segment = b - a; var length = segment.sqrMagnitude;
                var t = length > 0.000001f ? Mathf.Clamp01(Vector3.Dot(listenerPosition - a, segment) / length) : 0f;
                var candidate = a + segment * t; var d = (candidate - listenerPosition).sqrMagnitude;
                if (d < distance) { distance = d; closest = candidate; }
            }
            return closest;
        }
    }
}
