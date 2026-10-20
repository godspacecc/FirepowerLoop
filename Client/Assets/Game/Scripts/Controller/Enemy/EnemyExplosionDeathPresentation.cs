using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    /// <summary>地图独占的爆炸死亡粒子池；只消费死亡事实，不改变伤害、尸体物理或保存状态。</summary>
    internal sealed class EnemyExplosionDeathPresentation : IDisposable
    {
        /// <summary>最慢的地面尘雾在0.85秒内消散，留少量回收余量。</summary>
        private const float LifetimeSeconds = .9f;
        private readonly Transform parent;
        private readonly GameObject template;
        private readonly int capacity;
        private readonly List<Entry> entries = new();
        /// <summary>表现随机不消耗战斗或全局Unity随机序列。</summary>
        private readonly System.Random random = new();
        /// <summary>满池时覆盖最旧槽位，限制一帧清场的透明粒子和实例数量。</summary>
        private int cursor;

        private sealed class Entry
        {
            public GameObject Root;
            public ParticleSystem Mist, Fragments, Dust;
            public float Age;
        }

        public EnemyExplosionDeathPresentation(Transform parent, GameObject template, int capacity)
        {
            this.parent = parent;
            this.template = template;
            this.capacity = Mathf.Max(1, capacity);
        }

        /// <summary>固定在死亡时的世界坐标；后续炸飞和尸体回池都不牵动已喷出的颗粒。</summary>
        public void Play(Vector3 position, Vector3 direction)
        {
            if (parent == null || template == null) return;
            Entry entry = null;
            foreach (var candidate in entries)
                if (!candidate.Root.activeSelf) { entry = candidate; break; }
            if (entry == null && entries.Count < capacity)
            {
                var root = UnityEngine.Object.Instantiate(template, parent, false);
                root.name = "EnemyExplosionDeathInstance";
                entry = new Entry { Root = root,
                    Mist = root.transform.Find("ChestMist").GetComponent<ParticleSystem>(),
                    Fragments = root.transform.Find("SmallFragments").GetComponent<ParticleSystem>(),
                    Dust = root.transform.Find("GroundDust").GetComponent<ParticleSystem>() };
                entries.Add(entry);
            }
            if (entry == null)
            {
                entry = entries[cursor];
                cursor = (cursor + 1) % capacity;
            }
            Clear(entry);
            direction.y = 0;
            var rotation = direction.sqrMagnitude > .0001f ? Quaternion.LookRotation(direction) : Quaternion.identity;
            entry.Root.transform.SetPositionAndRotation(position, rotation);
            entry.Root.SetActive(true);
            entry.Age = 0;
            Emit(entry.Mist, rotation, 12, new Color(.5f, .035f, .025f, .82f), .55f, 1.05f, .32f, .65f, 2.1f, 1.3f);
            Emit(entry.Fragments, rotation, 22, new Color(.3f, .015f, .011f, 1), .07f, .14f, .3f, .65f, 3.7f, 2.3f);
            Emit(entry.Dust, rotation, 8, new Color(.45f, .39f, .3f, .4f), .45f, .7f, .45f, .85f, 1.5f, .28f);
        }

        /// <summary>粒子手动推进，和已有控制器的缩放时间一致；零步长暂停且不新增Update。</summary>
        public void Tick(float seconds)
        {
            if (seconds <= 0) return;
            foreach (var entry in entries)
            {
                if (!entry.Root.activeSelf) continue;
                entry.Age += seconds;
                if (entry.Age >= LifetimeSeconds) { Clear(entry); continue; }
                entry.Mist.Simulate(seconds, false, false, false);
                entry.Fragments.Simulate(seconds, false, false, false);
                entry.Dust.Simulate(seconds, false, false, false);
            }
        }

        /// <summary>一组只喷42粒；碎屑由粒子重力推进，不创建刚体或启用粒子碰撞。</summary>
        private void Emit(ParticleSystem particles, Quaternion rotation, int count, Color color,
            float minSize, float maxSize, float minLife, float maxLife, float speed, float up)
        {
            // 已保存的停止态粒子先初始化，再暂停交给手动时钟，避免Emit被忽略或自动双倍推进。
            particles.Play(false);
            particles.Pause(false);
            for (int i = 0; i < count; i++)
            {
                float angle = Range(0, Mathf.PI * 2);
                float velocity = Range(speed * .35f, speed);
                var burst = new ParticleSystem.EmitParams
                {
                    position = particles.transform.position + rotation * new Vector3(
                        Range(-.15f, .15f), Range(-.08f, .08f), Range(-.12f, .12f)),
                    velocity = rotation * new Vector3(Mathf.Cos(angle) * velocity,
                        Range(up * .3f, up), Mathf.Sin(angle) * velocity + speed * .25f),
                    startLifetime = Range(minLife, maxLife),
                    startSize = Range(minSize, maxSize), startColor = color,
                    rotation3D = new Vector3(Range(0, 360f), Range(0, 360f), Range(0, 360f))
                };
                particles.Emit(burst, 1);
            }
        }

        private float Range(float minimum, float maximum) => minimum + (maximum - minimum) * (float)random.NextDouble();

        /// <summary>地图隐藏、清场或返回奖励房时立即收起余烟，槽位可供同地图复用。</summary>
        public void Clear()
        {
            foreach (var entry in entries) Clear(entry);
            cursor = 0;
        }

        private static void Clear(Entry entry)
        {
            entry.Mist.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            entry.Fragments.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            entry.Dust.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            entry.Root.SetActive(false);
        }

        /// <summary>仅销毁池的实例，预制资源依赖随敌人预载句柄一起释放。</summary>
        public void Dispose()
        {
            Clear();
            foreach (var entry in entries)
                if (Application.isPlaying) UnityEngine.Object.Destroy(entry.Root);
                else UnityEngine.Object.DestroyImmediate(entry.Root);
            entries.Clear();
        }
    }
}
