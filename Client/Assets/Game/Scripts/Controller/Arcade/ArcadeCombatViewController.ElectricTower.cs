using System;
using System.Collections.Generic;
using System.Linq;
using FirepowerLoop.Arcade;
using QFramework;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>本机玩家坐标决定响度；俯视相机高度不参与近远判断。</summary>
        private Vector2 electricListener;
        private bool electricListenerVisible, electricHasListener;
        private AudioSystem electricAudio;
        /// <summary>保留本地图最近机关定义，让同帧清波的最后一击仍能结束短反馈；离场即清空。</summary>
        private IReadOnlyList<ArcadeHazardParameters> electricHitDefinitions=Array.Empty<ArcadeHazardParameters>();
        private readonly Dictionary<int, ElectricSound> electricSounds = new Dictionary<int, ElectricSound>();
        private readonly List<ElectricShot> electricShots = new List<ElectricShot>();
        private readonly List<ElectricImpact> electricImpacts = new List<ElectricImpact>();
        /// <summary>一台塔只持有当前权威相位的一条环境循环。</summary>
        private sealed class ElectricSound
        {
            public int Phase;
            public double Time;
            public AudioPlaybackHandle Loop;
        }
        /// <summary>短声保留自身地点与增益，移动时继续按本机位置衰减。</summary>
        private sealed class ElectricShot
        {
            public AudioPlaybackHandle Handle;
            public Vector2 Position;
            public float Gain;
        }
        /// <summary>每次已确认命中的短暂折线，仅借用模板材质，不拥有伤害或目标状态。</summary>
        private sealed class ElectricImpact
        {
            public GameObject Root;
            public LineRenderer[] Lines;
            public Vector3 Top, Target;
            public float Age;
        }

        /// <summary>近处最大28%增益，1.5米后平滑衰减到14米静音。</summary>
        private float ElectricGain(Vector2 position)
        {
            if (!electricHasListener) return 0f;
            float fade = 1f - Mathf.Clamp01((Vector2.Distance(position, electricListener) - 1.5f) / 12.5f);
            return .28f * fade * fade;
        }

        /// <summary>权威相位切换声段；重复快照不叠循环，加载失败或被抢占后可在下一次同步恢复。</summary>
        private void SyncElectricTowerAudio()
        {
            if (root == null || mapRoot == null || contentArena != 21)
            { StopElectricTowerAudio(); return; }
            if(!electricListenerVisible || contentHazards == null || contentHazards.Count == 0)
            {
                foreach(var sound in electricSounds.Values)electricAudio?.Stop(sound.Loop);
                electricSounds.Clear();UpdateElectricShots();return;
            }
            electricAudio ??= Game.Interface.GetSystem<AudioSystem>();
            var keep = new HashSet<int>();
            foreach (var p in contentHazards)
            {
                if (p.Kind != 1) continue;
                keep.Add(p.Id);
                ArcadeHazardRules.Evaluate(p, contentHazardElapsed, out int phase, out _);
                if (!electricSounds.TryGetValue(p.Id, out var state))
                { state = new ElectricSound { Phase = phase, Time = contentHazardElapsed }; electricSounds.Add(p.Id, state); }
                var position = new Vector2(p.X, p.Y);
                float gain = ElectricGain(position);
                bool changed = state.Phase != phase || contentHazardElapsed < state.Time ||
                    Math.Floor(contentHazardElapsed / p.CycleSeconds) != Math.Floor(state.Time / p.CycleSeconds);
                if (changed || phase == 0 || gain <= 0f)
                { electricAudio.Stop(state.Loop); state.Loop = AudioPlaybackHandle.Empty; }
                if (phase == 2 && state.Phase == 1 && contentHazardElapsed >= state.Time && contentHazardElapsed - state.Time < .5)
                    PlayElectricShot("ArcadeElectricPowerOn", position, .6f);
                if (phase != 0 && gain > 0f)
                {
                    if (phase == 1) gain *= .6f;
                    var world = mapRoot.TransformPoint(new Vector3(p.X, 1.6f, p.Y));
                    if (!electricAudio.IsSoundActive(state.Loop))
                        state.Loop = electricAudio.PlayLoopAt(phase == 1 ? "ArcadeElectricCharge" : "ArcadeElectricDischarge", world, 100f, 5,
                            new SoundPlaybackOptions(gain, 100f, 0f));
                    electricAudio.SetSoundGain(state.Loop, gain);
                    electricAudio.SetSoundPosition(state.Loop, world);
                }
                state.Phase = phase; state.Time = contentHazardElapsed;
            }
            foreach (int id in electricSounds.Keys.Where(id => !keep.Contains(id)).ToArray())
            { electricAudio.Stop(electricSounds[id].Loop); electricSounds.Remove(id); }
            UpdateElectricShots();

        }

        /// <summary>倒地后让本次短命中声自然结束，继续保留接触时的本机听觉位置。</summary>
        private void UpdateElectricShots()
        {
            for (int i = electricShots.Count - 1; i >= 0; i--)
            {
                var shot = electricShots[i]; float gain = ElectricGain(shot.Position) * shot.Gain;
                if (gain <= 0f) electricAudio.Stop(shot.Handle);
                if (!electricAudio.IsSoundActive(shot.Handle)) electricShots.RemoveAt(i);
                else electricAudio.SetSoundGain(shot.Handle, gain);
            }
        }

        /// <summary>命中或开始放电的短促电流声，冷加载晚到超过期限即取消。</summary>
        private void PlayElectricShot(string clip, Vector2 position, float scale)
        {
            float gain = ElectricGain(position) * scale;
            if (gain <= 0f || mapRoot == null) return;
            electricAudio ??= Game.Interface.GetSystem<AudioSystem>();
            var handle = electricAudio.PlaySoundAt(clip, mapRoot.TransformPoint(new Vector3(position.x, .9f, position.y)), 100f, 6,
                new SoundPlaybackOptions(gain, 100f, .18f));
            electricShots.Add(new ElectricShot { Handle = handle, Position = position, Gain = scale });
        }

        /// <summary>暂停、隐藏、换场或离场显式取消，包括尚未完成的资源申请。</summary>
        private void StopElectricTowerAudio()
        {
            foreach (var sound in electricSounds.Values) electricAudio?.Stop(sound.Loop);
            foreach (var shot in electricShots) electricAudio?.Stop(shot.Handle);
            electricSounds.Clear(); electricShots.Clear(); electricAudio = null;
        }

        /// <summary>只消费权威命中，沿用已采用预览的连接电弧、放射火花和贴身电弧；不改目标规则。</summary>
        public void PlayElectricTowerHit(int hazardId, Vector2 target)
        {
            if (root == null || contentArena != 21) return;
            int index = -1;
            for (int i = 0; i < electricHitDefinitions.Count; i++) if (electricHitDefinitions[i].Id == hazardId && electricHitDefinitions[i].Kind == 1) { index = i; break; }
            if (index < 0) return;
            PlayElectricShot("ArcadeElectricHit", target, .8f);
            var template = contentTemplates?.Find("DocksElectricTower");
            if (template == null) return; // 资源未就绪不积压历史命中，声音仍可正常反馈。
            var cyan = template.GetComponentsInChildren<LineRenderer>(true).FirstOrDefault(l => l.name == "向上聚电")?.sharedMaterial;
            var white = template.GetComponentsInChildren<LineRenderer>(true).FirstOrDefault(l => l.name == "塔顶聚电火花")?.sharedMaterial;
            if (cyan == null || white == null) return;
            if (electricImpacts.Count >= 24) { DestroyObject(electricImpacts[0].Root); electricImpacts.RemoveAt(0); }
            var p = electricHitDefinitions[index];
            var impact = new ElectricImpact { Root = new GameObject("ElectricTowerHit"), Lines = new LineRenderer[16],
                Top = new Vector3(p.X, 2.48f, p.Y), Target = new Vector3(target.x, 0, target.y) };
            impact.Root.transform.SetParent(root.transform, false);impact.Root.layer=root.layer;
            for (int i = 0; i < 16; i++)
            {
                var line = new GameObject("电击命中" + i).AddComponent<LineRenderer>(); line.transform.SetParent(impact.Root.transform, false);line.gameObject.layer=root.layer;
                line.useWorldSpace = false; line.sharedMaterial = i == 1 ? white : cyan;
                line.positionCount = i < 2 ? 12 : i < 12 ? 3 : 18;
                line.numCapVertices = 2; line.numCornerVertices = 2; impact.Lines[i] = line;
            }
            electricImpacts.Add(impact); DrawElectricImpact(impact);
        }

        /// <summary>复用既有战场表现帧，销毁自然结束的短弧并更新听觉距离。</summary>
        private void TickElectricTowerFeedback(float seconds)
        {
            SyncElectricTowerAudio();
            for (int i = electricImpacts.Count - 1; i >= 0; i--)
            {
                var impact = electricImpacts[i]; impact.Age += Mathf.Max(0, seconds);
                if (impact.Age >= .32f) { DestroyObject(impact.Root); electricImpacts.RemoveAt(i); }
                else DrawElectricImpact(impact);
            }
        }

        /// <summary>地图离场清除短弧、声音、监听位置与最近定义，不让历史状态跨轮。</summary>
        private void ReleaseElectricTowerFeedback()
        {
            StopElectricTowerAudio(); electricListenerVisible = electricHasListener = false;electricHitDefinitions=Array.Empty<ArcadeHazardParameters>();
            foreach (var impact in electricImpacts) DestroyObject(impact.Root);
            electricImpacts.Clear();
        }

        /// <summary>与用户采用的临时视频同源采样；只推进短暂表现年龄，没有额外麻痹或击退。</summary>
        private static void DrawElectricImpact(ElectricImpact impact)
        {
            float age = impact.Age, fade = Mathf.Clamp01(1 - age / .32f); int tick = (int)(age * 36);
            var point = impact.Target + Vector3.up * .9f;
            float Noise(int i, int j) => Mathf.Sin(tick * 17.137f + i * 31.73f + j * 11.53f);
            for (int i = 0; i < impact.Lines.Length; i++)
            {
                var line = impact.Lines[i]; line.enabled = i < 2 ? age < .16f : i < 12 || age < .24f;
                if (i < 2)
                {
                    line.widthMultiplier = (i == 0 ? .075f : .026f) * Mathf.Sqrt(fade);
                    for (int j = 0; j < line.positionCount; j++)
                    {
                        var v = Vector3.Lerp(impact.Top, point, j / (float)(line.positionCount - 1));
                        if (j > 0 && j < line.positionCount - 1) v += new Vector3(Noise(1,j)*.10f,Noise(3,j)*.08f,Noise(7,j)*.10f);
                        line.SetPosition(j,v);
                    }
                }
                else if (i < 12)
                {
                    float a = (i - 2) * Mathf.PI * 2 / 10;
                    var direction = new Vector3(Mathf.Cos(a), Mathf.Sin(a)*.85f, Mathf.Cos(a*2)*.45f);
                    float length = (.18f + i % 3 * .07f) * (1 + age * 3);
                    line.widthMultiplier = .045f * fade;
                    line.SetPosition(0,point+direction*.045f);line.SetPosition(1,point+direction*length*.65f+Vector3.up*Noise(i,1)*.05f);line.SetPosition(2,point+direction*length);
                }
                else
                {
                    line.widthMultiplier = .026f * fade;
                    for (int j = 0; j < line.positionCount; j++)
                    {
                        float u = j / (float)(line.positionCount - 1), a = u * Mathf.PI * 3 + (i-12)*Mathf.PI*.5f + tick*.5f;
                        float radius = .23f + Noise(i,j)*.045f;
                        line.SetPosition(j,impact.Target+new Vector3(Mathf.Cos(a)*radius,.30f+u*1.10f,Mathf.Sin(a)*radius));
                    }
                }
            }
        }
    }
}
