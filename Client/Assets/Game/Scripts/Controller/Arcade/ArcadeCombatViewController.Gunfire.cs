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
            PlayBulletImpactBurst(position, Vector2.zero, false);
        }

        /// <summary>只消费权威障碍接触点；向来弹反方向散开，与敌人命中共用有界池，不发身体命中声音。</summary>
        public void PlayProjectileSurfaceImpact(Vector2 position, Vector2 incomingDirection)
        {
            PlayBulletImpactBurst(position, incomingDirection, true);
        }

        /// <summary>复用时同时覆盖形状、寿命、朝向和发射量，防止墙面火星污染后续敌人命中。</summary>
        private void PlayBulletImpactBurst(Vector2 position, Vector2 incomingDirection, bool surface)
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
            particles.transform.localRotation = surface && incomingDirection.sqrMagnitude > .0001f
                ? Quaternion.LookRotation(new Vector3(-incomingDirection.x, .35f, -incomingDirection.y))
                : Quaternion.identity;
            var main = particles.main;
            main.startLifetime = surface ? new ParticleSystem.MinMaxCurve(.07f, .13f) : new ParticleSystem.MinMaxCurve(.09f, .18f);
            main.startSpeed = surface ? new ParticleSystem.MinMaxCurve(2f, 4f) : new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            main.startSize = surface ? new ParticleSystem.MinMaxCurve(.04f, .07f) : new ParticleSystem.MinMaxCurve(.05f, .09f);
            var shape = particles.shape;
            shape.shapeType = surface ? ParticleSystemShapeType.Cone : ParticleSystemShapeType.Sphere;
            if (surface) shape.angle = 55f;
            var emission = particles.emission;
            emission.SetBurst(0, new ParticleSystem.Burst(0f, (short)(surface ? 4 : 6)));
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
