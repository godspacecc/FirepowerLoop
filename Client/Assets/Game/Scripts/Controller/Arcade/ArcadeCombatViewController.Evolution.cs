using System.Linq;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>本场最多保留十二次集束爆炸的烟尘，复用异步资源租约和离场清理。</summary>
        private int evolutionBlastCursor;

        public void SetWeaponBurnArea(int id, Vector2 position, float radius, float remainingSeconds) =>
            SetEvolutionVisual($"evolution.ground:{id}", "ArcadeWeaponBurnGround", position, .06f, Vector2.up, 1,
                radius, remainingSeconds, false, 0);

        public void SetEvolvedPiercing(int id, Vector2 position, Vector2 direction, float radius) =>
            SetEvolutionVisual($"evolution.projectile:{id}", "ArcadeWeaponPiercingEvolved", position, 1.15f,
                direction, 2, radius, 0, false, 0);

        public void SetEvolvedGrenade(int id, Vector2 position, Vector2 direction, bool child, float phase) =>
            SetEvolutionVisual($"evolution.projectile:{id}", "ArcadeWeaponGrenadeEvolved", position,
                .45f + Mathf.Sin(Mathf.Clamp01(phase) * Mathf.PI) * (child ? .85f : 2.1f),
                direction, 3, 0, 0, child, phase);

        public void PlayEvolvedGrenadeExplosion(Vector2 position, float radius, bool child)
        {
            string key = $"evolution.blast:{evolutionBlastCursor++ % 12}";
            ReleaseEffectVisual(key);
            SetEvolutionVisual(key, child ? "ArcadeWeaponGrenadeChildBlast" : "ArcadeWeaponGrenadePrimaryBlast",
                position, .55f, Vector2.up, child ? 5 : 4, radius, 0, child, 0);
            if (effectVisuals.TryGetValue(key, out var entry)) entry.EvolutionTailSeconds = 1.65f;
            // 声音仍沿用正式榴弹反馈限频，爆炸体量只由已采用的模板控制。
            if (mapRoot != null && Time.unscaledTime - lastExplosionSoundTime >= .15f)
            {
                Game.Interface.GetSystem<AudioSystem>()?.PlaySoundAt("ArcadeExplosion",
                    mapRoot.TransformPoint(new Vector3(position.x, .75f, position.y)), 18f, 20);
                lastExplosionSoundTime = Time.unscaledTime;
            }
        }

        /// <summary>加载前后都保存最新权威状态；表现不参与命中或计时裁决。</summary>
        private void SetEvolutionVisual(string key, string prefab, Vector2 position, float height,
            Vector2 direction, int mode, float radius, float remaining, bool child, float phase)
        {
            SetEffectVisual(key, prefab, "Prefabs/SceneObjects/" + prefab, position, new Vector3(0, height, 0));
            if (!effectVisuals.TryGetValue(key, out var entry)) return;
            entry.EvolutionMode = mode; entry.EvolutionRadius = radius; entry.EvolutionRemaining = remaining;
            entry.EvolutionChild = child; entry.EvolutionPhase = phase;
            if (mode < 4) entry.EvolutionTailSeconds = 0;
            entry.HasShotDirection = true;
            entry.LocalRotation = direction.sqrMagnitude > .0001f
                ? Quaternion.LookRotation(new Vector3(direction.x, 0, direction.y)) : Quaternion.identity;
            ApplyWeaponEvolution(entry);
        }

        private static void ApplyWeaponEvolution(EffectVisualEntry entry)
        {
            if (entry.EvolutionMode == 0 || entry.Instance == null) return;
            entry.Instance.transform.localRotation = entry.LocalRotation;
            var visual=entry.Instance.GetComponent<ArcadeWeaponEvolutionVisual>();
            if(visual==null)return;
            visual.Apply(entry.EvolutionRadius,entry.EvolutionRemaining,entry.EvolutionChild,entry.EvolutionPhase);
            // 加载期间同样推进活动年龄，避免迟到首爆从零重播。
            if(entry.EvolutionAge>visual.ElapsedSeconds)visual.Tick(entry.EvolutionAge-visual.ElapsedSeconds);
        }

        /// <summary>只有已经显示的残火保留烟尾；移除中的异步加载不得复活。</summary>
        private static bool KeepWeaponEvolutionTail(EffectVisualEntry entry)
        {
            if (entry.EvolutionMode == 1 && entry.Instance != null)
            {
                if (entry.EvolutionTailSeconds <= 0) entry.EvolutionTailSeconds = 1.1f;
                entry.EvolutionRemaining = 0; ApplyWeaponEvolution(entry);
                return true;
            }
            return entry.EvolutionMode >= 4 && entry.EvolutionTailSeconds > 0;
        }

        private void TickWeaponEvolutionVisuals(float seconds)
        {
            if (seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            foreach (var entry in effectVisuals.Values.ToArray())
            {
                if (entry.EvolutionMode == 0) continue;
                entry.EvolutionAge += seconds;
                entry.Instance?.GetComponent<ArcadeWeaponEvolutionVisual>()?.Tick(seconds);
                if (entry.EvolutionTailSeconds <= 0) continue;
                entry.EvolutionTailSeconds -= seconds;
                if (entry.EvolutionTailSeconds <= 0) ReleaseEffectVisual(entry);
            }
        }
    }
}
