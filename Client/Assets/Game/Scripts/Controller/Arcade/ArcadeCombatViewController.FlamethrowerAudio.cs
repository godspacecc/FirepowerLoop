using QFramework;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>喷火声归属当前本机角色；一次点火与一条循环成对释放，不随每发叠加。</summary>
        private AudioPlaybackHandle flameIgnitionSound, flameLoopSound;
        /// <summary>保留句柄所属音频系统，避免离场时再次访问架构创建新系统。</summary>
        private AudioSystem flameAudioOwner;
        /// <summary>由表现帧推进的停火余量；不跨局保存。</summary>
        private float flameSoundRemaining;
        /// <summary>现有喷火器每秒十发，给序列更新留下六十毫秒余量。</summary>
        private const float FlameSoundReleaseSeconds = .16f;

        /// <summary>权威开火序列续期循环；异步加载失败或音源被抢占后允许下次攻击重新申请。</summary>
        private void PlayFlamethrowerSound(AudioSystem audio, Vector3 position)
        {
            if (audio == null) return;
            if (flameAudioOwner != audio || !audio.IsSoundActive(flameLoopSound))
            {
                StopFlamethrowerAudio();
                flameAudioOwner = audio;
                flameIgnitionSound = audio.PlaySoundAt("ArcadeFireFlameStart", position, 18f, 10,
                    new SoundPlaybackOptions(.65f, 3f, FlameSoundReleaseSeconds));
                flameLoopSound = audio.PlayLoopAt("ArcadeFireFlameLoop", position, 18f, 10,
                    new SoundPlaybackOptions(.65f, 3f, 0f));
            }
            flameSoundRemaining = FlameSoundReleaseSeconds;
            audio.SetSoundPosition(flameIgnitionSound, position);
            audio.SetSoundPosition(flameLoopSound, position);
        }

        /// <summary>复用战场表现帧；暂停零步长不会误停，停火后最多等待一个正常发射间隔加余量。</summary>
        private void TickFlamethrowerAudio(float deltaTime)
        {
            if (flameAudioOwner == null) return;
            flameSoundRemaining -= Mathf.Max(0f, deltaTime);
            if (flameSoundRemaining <= 0f) { StopFlamethrowerAudio(); return; }
            if (playerMotor == null || !playerMotor.IsValid) return;
            var position = playerMotor.TryGetProjectileOrigin(Vector3.forward, out var muzzle) ? muzzle : playerMotor.Position;
            flameAudioOwner.SetSoundPosition(flameIgnitionSound, position);
            flameAudioOwner.SetSoundPosition(flameLoopSound, position);
        }

        /// <summary>停火、换枪、倒地、离场共用幂等释放；迟到资源回调由 AudioSystem 取消。</summary>
        private void StopFlamethrowerAudio()
        {
            flameAudioOwner?.Stop(flameIgnitionSound);
            flameAudioOwner?.Stop(flameLoopSound);
            flameIgnitionSound = flameLoopSound = AudioPlaybackHandle.Empty;
            flameAudioOwner = null;
            flameSoundRemaining = 0f;
        }

        /// <summary>联机断线暂停或世界表现暂挂时显式停声，不依赖被挂起的表现帧倒计时。</summary>
        public void StopPlayerFireAudio() => StopFlamethrowerAudio();
    }
}
