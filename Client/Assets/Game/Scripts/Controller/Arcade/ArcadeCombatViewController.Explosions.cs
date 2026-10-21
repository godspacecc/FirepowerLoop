using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>地图持有最多十二组爆炸；密集命中复用旧组，不按弹数无限创建。</summary>
        private const int ProjectileBlastCapacity = 12;
        /// <summary>当前地图拥有的爆炸实例缓存。</summary>
        private readonly List<ProjectileBlastEntry> projectileBlastPool = new();
        /// <summary>容量满后按顺序复用的槽位。</summary>
        private int projectileBlastCursor;
        /// <summary>与枪弹命中共用当前Content租约，离场后不接受晚到表现。</summary>
        private Transform projectileBlastTemplate;
        /// <summary>桶专属紫色爆炸复用当前Content租约，独立于普通武器的十二组缓存。</summary>
        private Transform barrelBlastTemplate;
        /// <summary>当前地图拥有的桶爆炸实例，普通武器不占用这些槽位。</summary>
        private readonly List<ProjectileBlastEntry> barrelBlastPool=new();
        /// <summary>满容量后顺序复用桶爆炸的槽位。</summary>
        private int barrelBlastCursor;
        /// <summary>桶爆炸独立的短声音限流时刻，不抑制粒子播放。</summary>
        private float lastBarrelExplosionSoundTime=float.NegativeInfinity;

        /// <summary>只接收已结算的桶爆炸；密集爆炸限音但不抑制视觉，使用已确认的能量音效。</summary>
        public void PlayBarrelExplosion(Vector2 position,float radius)
        {
            if(root==null||barrelBlastTemplate==null||radius<=0||float.IsNaN(radius)||float.IsInfinity(radius)||
                float.IsNaN(position.x)||float.IsNaN(position.y)||float.IsInfinity(position.x)||float.IsInfinity(position.y))return;
            ProjectileBlastEntry entry;
            if(barrelBlastPool.Count<ProjectileBlastCapacity)
            {
                var instance=UnityEngine.Object.Instantiate(barrelBlastTemplate.gameObject,root.transform,false);
                entry=new ProjectileBlastEntry{Root=instance,Particles=instance.GetComponentsInChildren<ParticleSystem>(true)};
                barrelBlastPool.Add(entry);instance.SetActive(true);
            }
            else entry=barrelBlastPool[barrelBlastCursor];
            barrelBlastCursor=(barrelBlastCursor+1)%ProjectileBlastCapacity;
            foreach(var particles in entry.Particles)particles.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            entry.Root.transform.localPosition=new Vector3(position.x,.75f,position.y);
            entry.Root.transform.localScale=Vector3.one*Mathf.Clamp(radius/2.8f,.35f,2f);
            foreach(var particles in entry.Particles)particles.Play(false);
            if(mapRoot==null)return;
            float now=Time.unscaledTime;if(now-lastBarrelExplosionSoundTime<.15f)return;
            lastBarrelExplosionSoundTime=now;
            Game.Interface.GetSystem<AudioSystem>()?.PlaySoundAt("ArcadeExplosionSciFi",mapRoot.TransformPoint(new Vector3(position.x,.75f,position.y)),18f,20);
        }

        /// <summary>切轮／离场同步回收桶专属粒子；共享模板与材质由Content句柄持有。</summary>
        private void ClearBarrelBlasts()
        {
            foreach(var entry in barrelBlastPool)if(entry.Root!=null)DestroyObject(entry.Root);
            barrelBlastPool.Clear();barrelBlastCursor=0;lastBarrelExplosionSoundTime=float.NegativeInfinity;
        }

        /// <summary>缓存原配色和粒子引用，避免每次爆炸分配数组或修改共享材质。</summary>
        private sealed class ProjectileBlastEntry
        {
            /// <summary>归当前地图释放的实例根。</summary>
            public GameObject Root;
            /// <summary>创建时缓存的粒子引用，命中时不重新查询数组。</summary>
            public ParticleSystem[] Particles;
            /// <summary>普通爆炸原配色，用于清除上次能量爆炸的覆盖。</summary>
            public ParticleSystem.MinMaxGradient[] OriginalColors;
        }

        /// <summary>只消费已确认引爆事件；半径只缩放视觉，不参与伤害或碰撞。</summary>
        private void PlayProjectileBlast(Vector2 position, float radius, bool energy)
        {
            if (root == null || projectileBlastTemplate == null) return;
            ProjectileBlastEntry entry;
            if (projectileBlastPool.Count < ProjectileBlastCapacity)
            {
                var instance = UnityEngine.Object.Instantiate(projectileBlastTemplate.gameObject, root.transform, false);
                var particles = instance.GetComponentsInChildren<ParticleSystem>(true);
                var colors = new ParticleSystem.MinMaxGradient[particles.Length];
                for (int i = 0; i < particles.Length; i++) colors[i] = particles[i].main.startColor;
                entry = new ProjectileBlastEntry { Root = instance, Particles = particles, OriginalColors = colors };
                projectileBlastPool.Add(entry);
                instance.SetActive(true);
            }
            else entry = projectileBlastPool[projectileBlastCursor];
            projectileBlastCursor = (projectileBlastCursor + 1) % ProjectileBlastCapacity;
            // World模拟粒子必须先清空再移动，池复用不能留下跨地图的旧爆心。
            foreach (var particles in entry.Particles) particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            entry.Root.transform.localPosition = new Vector3(position.x, .75f, position.y);
            entry.Root.transform.localScale = Vector3.one * Mathf.Clamp(radius / 2.8f, .35f, 2f);
            for (int i = 0; i < entry.Particles.Length; i++)
            {
                var particles = entry.Particles[i];
                var main = particles.main;
                main.startColor = energy ? new ParticleSystem.MinMaxGradient(new Color(.3f, 2.5f, 4f, 1)) : entry.OriginalColors[i];
                // 能量弹只保留短光晕和径向火星；再次用作火箭/榴弹时完整恢复原模板。
                if (energy && (particles.name == "Burst" || particles.name == "Smoke")) continue;
                particles.Play(false);
            }
        }

        /// <summary>地图End同步释放实例；模板的资源句柄仍归既有Content加载状态所有。</summary>
        private void ClearProjectileBlasts()
        {
            foreach (var entry in projectileBlastPool) if (entry.Root != null) DestroyObject(entry.Root);
            projectileBlastPool.Clear(); projectileBlastCursor = 0;
        }
    }
}
