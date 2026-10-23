using System.Collections.Generic;
using FirepowerLoop.Arcade;
using QFramework;
using UnityEngine;
namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>牛群反馈归属当前轮；听觉位置使用本机角色平面坐标，避免俯视相机高度影响响度。</summary>
        private ArcadeHerdFeedback herdFeedback;
        private AudioSystem herdAudio;
        private Vector2 herdListener;
        private bool herdListenerVisible, herdHasListener, herdFeedbackPaused;
        private readonly List<HerdShot> herdShots = new List<HerdShot>();
        private sealed class HerdShot { public AudioPlaybackHandle Handle; public Vector2 Position; public float Scale; public bool Impact; }
        private void SyncHerdListener(Vector2 position, bool visible)
        {
            if (visible) {herdListener = position;herdHasListener=true;}
            herdListenerVisible = visible;
            if(!visible)
                for(int i=herdShots.Count-1;i>=0;i--)if(!herdShots[i].Impact){herdAudio?.Stop(herdShots[i].Handle);herdShots.RemoveAt(i);}
        }
        private void SyncHerdFeedback()
        {
            if (roundContentRoot == null || earlyHerdTemplate == null) return;
            if (herdFeedback == null)
            {
                herdFeedback = new ArcadeHerdFeedback(roundContentRoot.transform, earlyHerdTemplate.GetComponent<ArcadeHerdVisual>().DustMaterial,
                    PlayHerdSound, StopHerdSounds);
                herdFeedback.SetEnabled(!herdFeedbackPaused);
            }
            herdFeedback.Sync(herdListenerVisible?herdSnapshots:System.Array.Empty<ArcadeHerdSnapshot>());
        }
        private float HerdGain(Vector2 position)
        {
            float distance = Vector2.Distance(position, herdListener);
            return Mathf.Max(0, 1 - distance / 24) / (1 + distance * distance / 81);
        }
        /// <summary>短声有开始期限，最多16条待播／播放句柄；冷加载晚到不会补出旧蹄声。</summary>
        private void PlayHerdSound(string name, Vector2 position, float scale)
        {
            bool impact=name=="ArcadeHerdImpact";
            if (root == null || !root.activeInHierarchy || mapRoot == null || !herdHasListener || !herdListenerVisible&&!impact || herdFeedbackPaused) return;
            float gain = HerdGain(position) * scale; if (gain <= 0) return;
            herdAudio ??= Game.Interface.GetSystem<AudioSystem>();
            if (herdShots.Count >= 16) { herdAudio.Stop(herdShots[0].Handle); herdShots.RemoveAt(0); }
            var handle = herdAudio.PlaySoundAt(name, mapRoot.TransformPoint(new Vector3(position.x, .09f, position.y)), 100, 4,
                new SoundPlaybackOptions(gain, 100, .15f));
            herdShots.Add(new HerdShot { Handle = handle, Position = position, Scale = scale,Impact=impact });
        }
        private void StopHerdSounds()
        {
            foreach (var shot in herdShots) herdAudio?.Stop(shot.Handle);
            herdShots.Clear(); herdAudio = null;
        }
        private void TickHerdFeedback(float seconds)
        {
            herdFeedback?.Tick(seconds);
            for (int i = herdShots.Count - 1; i >= 0; i--)
            {
                var shot = herdShots[i];
                if (!herdAudio.IsSoundActive(shot.Handle)) herdShots.RemoveAt(i);
                else herdAudio.SetSoundGain(shot.Handle, HerdGain(shot.Position) * shot.Scale);
            }
        }
        private void PauseHerdFeedback(bool paused)
        {
            herdFeedbackPaused = paused; herdFeedback?.SetEnabled(!paused);
            if (paused) StopHerdSounds();
        }
        private void ReleaseHerdFeedback()
        {
            herdFeedback?.Dispose(); herdFeedback = null; StopHerdSounds(); herdFeedbackPaused = false;
        }
        public void PlayHerdContact(Vector2 position) { herdFeedback?.Impact(position); }
        private void ObserveEnemyHerdImpact(int id, ArcadeHerdImpactState state)
        { if (roundEnemyPositions.TryGetValue(id, out var position)) herdFeedback?.ObserveImpact(id, state, position); }
        /// <summary>死亡类别由单双人已有权威入口传入；取移除前的已见位置，不推测未见目标接触。</summary>
        private void RemoveHerdEnemyFeedback(int id, bool death, int impactKind)
        {
            if (death && impactKind == ArcadeDeathImpactRules.Herd && roundEnemyPositions.TryGetValue(id, out var position)) herdFeedback?.Impact(position);
            herdFeedback?.ForgetImpact(id);
        }
    }
}
