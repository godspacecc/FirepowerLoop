using System.Collections.Generic;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>街机已经接受的挥动或命中所对应的声音表现，不提交伤害。</summary>
    public enum CombatSoundCueKind
    {
        Swing,
        MeleeBody,
        BulletBody
    }

    /// <summary>街机表现提交的声音通知；序列用于同一地图内去重。</summary>
    public readonly struct CombatSoundCueEvent
    {
        public CombatSoundCueEvent(CombatSoundCueKind kind, int weaponItemId, Vector3 position, long sequence = 0)
        {
            Kind = kind;
            WeaponItemId = weaponItemId;
            Position = position;
            Sequence = sequence;
        }

        public CombatSoundCueKind Kind { get; }
        public int WeaponItemId { get; }
        public Vector3 Position { get; }
        public long Sequence { get; }
    }

    /// <summary>只订阅街机声音表现通知，拥有播放句柄、去重及地图释放责任。</summary>
    public sealed class CombatSoundSystem : AbstractSystem
    {
        /// <summary>子弹命中只作轻量反馈；近距增益及玩家世界距离范围不受俯视相机高度影响。</summary>
        private const float BulletHitGain = 0.18f;
        private const float BulletHitNearDistance = 2f;
        private const float BulletHitSilentDistance = 18f;
        private readonly Queue<AudioPlaybackHandle> impacts = new Queue<AudioPlaybackHandle>();
        private readonly HashSet<(CombatSoundCueKind, Vector3)> sequenceCues = new HashSet<(CombatSoundCueKind, Vector3)>();
        private long sequence;
        private int lastSwing = -1, lastPunch = -1, lastBody = -1;
        private AudioSystem Audio => this.GetSystem<AudioSystem>();

        protected override void OnInit()
        {
            this.RegisterEvent<CombatSoundCueEvent>(OnCue);
            this.RegisterEvent<AppStateChangedEvent>(evt =>
            {
                if (evt.Current != AppState.Playing)
                    StopSounds();
            });
            this.RegisterEvent<MapLoadingStartedEvent>(_ => ResetMap());
            this.RegisterEvent<MapCurrentReleasedEvent>(_ => ResetMap());
        }

        protected override void OnDeinit() => ResetMap();
        /// <summary>沿用现有两段声音交替，避免连续重复同一声音。</summary>
        private static string Variant(string prefix, ref int previous)
        {
            previous = previous < 0 ? Random.Range(0, 2) : 1 - previous;
            return prefix + (previous + 1).ToString("00");
        }

        private void OnCue(CombatSoundCueEvent evt)
        {
            if (this.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing)
                return;
            if (evt.Sequence > 0)
            {
                if (evt.Sequence < sequence)
                    return;
                if (evt.Sequence > sequence)
                {
                    sequence = evt.Sequence;
                    sequenceCues.Clear();
                }

                if (!sequenceCues.Add((evt.Kind, evt.Position)))
                    return;
            }

            var gain = 0.35f;
            var minDistance = 16f;
            if (evt.Kind == CombatSoundCueKind.BulletBody)
            {
                gain = CalculateBulletHitGain(evt.Position);
                if (gain <= 0f) return;
                // 与开火声使用相同的近距尺度，取消原先为相机高度补偿的 16 米放大范围。
                minDistance = 1f;
            }

            string name;
            switch (evt.Kind)
            {
                case CombatSoundCueKind.Swing:
                    name = Variant("CombatSwing", ref lastSwing);
                    break;
                case CombatSoundCueKind.MeleeBody:
                    name = Variant("CombatPunchHit", ref lastPunch);
                    break;
                case CombatSoundCueKind.BulletBody:
                    name = Variant("CombatBulletBody", ref lastBody);
                    break;
                default:
                    return;
            }

            while (impacts.Count >= 12)
                Audio.Stop(impacts.Dequeue());
            var handle = Audio.PlaySoundAt(name, evt.Position, 36f, 10, new SoundPlaybackOptions(gain, minDistance, 0.2f));
            if (handle.IsValid)
                impacts.Enqueue(handle);
        }

        /// <summary>按命中瞬间本机玩家到命中点的世界距离平滑衰减；玩家未绑定时不播放错误的满音量反馈。</summary>
        private float CalculateBulletHitGain(Vector3 hitPosition)
        {
            var provider = this.GetUtility<IPlayerRuntimePortProvider>();
            if (provider == null || !provider.TryGetCurrentPlayerPose(out var player, out _) ||
                !player.IsActive || player.MapInstanceId <= 0 || player.UnitId != PlayerModel.LocalPlayerUnitId)
                return 0f;

            var distance = Vector3.Distance(player.Position, hitPosition);
            var attenuation = 1f - Mathf.InverseLerp(BulletHitNearDistance, BulletHitSilentDistance, distance);
            return BulletHitGain * attenuation * attenuation;
        }

        /// <summary>暂停及卸载时主动回收本系统拥有的声音句柄。</summary>
        private void StopSounds()
        {
            while (impacts.Count > 0)
                Audio.Stop(impacts.Dequeue());
        }

        private void ResetMap()
        {
            StopSounds();
            sequence = 0;
            sequenceCues.Clear();
        }
    }
}
