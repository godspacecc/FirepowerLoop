using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>命中粒子归当前战场所有；密集散射最多复用十二组，不按命中数无限创建。</summary>
        private const int BulletImpactCapacity = 12;
        private readonly List<ParticleSystem> bulletImpactPool = new();
        private int bulletImpactCursor;
        /// <summary>由当前 Content 资源租约持有，独立于前三场环境是否启用。</summary>
        private ParticleSystem bulletImpactTemplate;

        /// <summary>只消费规则已确认的命中位置；模板未准备好时略过该次反馈，不排队补播。</summary>
        private void PlayBulletImpact(Vector2 position)
        {
            if (root == null || bulletImpactTemplate == null) return;
            ParticleSystem particles;
            if (bulletImpactPool.Count < BulletImpactCapacity)
            {
                var instance = UnityEngine.Object.Instantiate(bulletImpactTemplate.gameObject, root.transform, false);
                particles = instance.GetComponent<ParticleSystem>();
                instance.SetActive(true);
                bulletImpactPool.Add(particles);
            }
            else particles = bulletImpactPool[bulletImpactCursor];
            bulletImpactCursor = (bulletImpactCursor + 1) % BulletImpactCapacity;
            // 复用先清空旧粒子再移动，避免上一击的火星拖到新位置；仅使用地图局部坐标。
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.transform.localPosition = new Vector3(position.x, .75f, position.y);
            particles.Play(true);
        }

        /// <summary>离场同步释放所有命中实例；异步模板由既有 Content 租约清理。</summary>
        private void ClearBulletImpacts()
        {
            foreach (var particles in bulletImpactPool)
                if (particles != null) DestroyObject(particles.gameObject);
            bulletImpactPool.Clear();
            bulletImpactCursor = 0;
        }
    }
}
