using UnityEngine;

namespace Game
{
    /// <summary>燃烧弹火区只采样权威年龄；没有 Update，不自行计时或裁决伤害。</summary>
    public sealed class SurvivorFireZoneVisual : MonoBehaviour
    {
        public ParticleSystem Flames;
        public ParticleSystem[] ImpactParticles;
        public Transform ImpactRoot;
        public Transform Scorch;
        /// <summary>上次采样年龄；暂停时没有新的采样，因此粒子也保持静止。</summary>
        private float sampledAge;
        /// <summary>Stop 清空后首次采样必须重启模拟，后续只推进增量。</summary>
        private bool restartSimulation = true;

        public bool IsValid => Flames != null && ImpactRoot != null && Scorch != null &&
            ImpactParticles != null && ImpactParticles.Length > 0 &&
            System.Array.TrueForAll(ImpactParticles, p => p != null);

        /// <summary>回池时清空所有粒子，下一次复用不会留下旧火焰。</summary>
        public void ResetVisual()
        {
            sampledAge = 0;
            restartSimulation = true;
            Flames.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var particle in ImpactParticles)
                particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            ImpactRoot.gameObject.SetActive(true);
        }

        /// <summary>半径对应真实伤害范围；粒子数量有固定上限，不随等级无限增长。</summary>
        public void Sample(float radius, float age)
        {
            if (age < sampledAge) ResetVisual();
            float delta = Mathf.Max(0, age - sampledAge);
            var shape = Flames.shape; shape.radius = radius;
            var emission = Flames.emission;
            emission.rateOverTime = Mathf.Lerp(55, 130, Mathf.InverseLerp(1.2f, 2.1f, radius));
            Scorch.localScale = new Vector3(radius * 2, radius * 2, 1);
            ImpactRoot.localScale = Vector3.one * Mathf.Lerp(.65f, 1, Mathf.InverseLerp(1.2f, 2.1f, radius));
            Flames.Simulate(delta, false, restartSimulation, false);
            if (age < .45f)
                foreach (var particle in ImpactParticles) particle.Simulate(delta, false, restartSimulation, false);
            else ImpactRoot.gameObject.SetActive(false);
            sampledAge = age;
            restartSimulation = false;
        }
    }
}
