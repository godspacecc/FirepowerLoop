using UnityEngine;

namespace Game
{
    /// <summary>火堆的纯表现组件；正式建造后由效果根节点启用，不持有建筑业务状态。</summary>
    [DisallowMultipleComponent]
    public sealed class CampfireVisualEffect : MonoBehaviour
    {
        [SerializeField] private Light fireLight;
        [SerializeField] private ParticleSystem flameParticles;
        [SerializeField] private Material flameMaterial;
        [SerializeField, Min(0f)] private float baseIntensity = 2.2f;
        [SerializeField, Min(0f)] private float flickerAmplitude = 0.45f;
        [SerializeField, Min(0.01f)] private float flickerSpeed = 7f;

        private float flickerSeed;
        private bool updateRegistered;

        public bool ValidateReferences(out string error)
        {
            if (fireLight == null || fireLight.type != LightType.Point || !fireLight.transform.IsChildOf(transform.parent))
            {
                error = "火堆缺少有效的点光源";
                return false;
            }

            if (flameParticles == null || !flameParticles.transform.IsChildOf(transform.parent))
            {
                error = "火堆缺少有效的火焰粒子";
                return false;
            }

            if (flameMaterial == null)
            {
                error = "火堆缺少火焰粒子材质";
                return false;
            }

            error = null;
            return true;
        }

        private void OnEnable()
        {
            if (!Application.isPlaying || !ValidateReferences(out _))
            {
                return;
            }

            ConfigureParticles(flameParticles, flameMaterial);
            flickerSeed = Mathf.Abs(GetInstanceID() % 997) * 0.013f;
            fireLight.enabled = true;
            fireLight.intensity = baseIntensity;
            flameParticles.Play(true);
            if (!updateRegistered)
            {
                LifeCycle.RegisterUpdate(Tick);
                updateRegistered = true;
            }
        }

        private void OnDisable()
        {
            if (updateRegistered)
            {
                LifeCycle.UnregisterUpdate(Tick);
                updateRegistered = false;
            }

            if (fireLight != null)
            {
                fireLight.intensity = baseIntensity;
            }

            if (Application.isPlaying && flameParticles != null)
            {
                flameParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void Tick()
        {
            if (fireLight == null)
            {
                return;
            }

            var noise = Mathf.PerlinNoise(flickerSeed, Time.time * flickerSpeed) * 2f - 1f;
            fireLight.intensity = Mathf.Max(0f, baseIntensity + noise * flickerAmplitude);
        }

        internal static void ConfigureParticles(ParticleSystem particles, Material material)
        {
            if (particles == null)
            {
                return;
            }

            // 粒子节点启用时可能已因 playOnAwake 自动播放；先彻底停止，才能安全修改 duration 等模块参数。
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.32f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.25f, 0.03f, 0.9f),
                new Color(1f, 0.82f, 0.18f, 1f));
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 48;

            var emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 22f;

            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = 0.12f;
            shape.length = 0.2f;

            var colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(new Gradient
            {
                colorKeys = new[]
                {
                    new GradientColorKey(new Color(1f, 0.9f, 0.35f), 0f),
                    new GradientColorKey(new Color(1f, 0.18f, 0.02f), 1f)
                },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.12f),
                    new GradientAlphaKey(0f, 1f)
                }
            });

            var noise = particles.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.strength = 0.12f;
            noise.frequency = 0.8f;
            noise.scrollSpeed = 0.35f;

            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                ConfigureParticleMaterial(material);
                renderer.sharedMaterial = material;
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.alignment = ParticleSystemRenderSpace.View;
                renderer.sortingOrder = 2;
            }
        }

        internal static void ConfigureParticleMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            material.SetColor("_BaseColor", Color.white);
            material.SetColor("_Color", Color.white);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
    }
}
