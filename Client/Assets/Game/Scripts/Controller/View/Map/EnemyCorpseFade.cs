using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game
{
    /// <summary>实例持有的尸体淡出材质缓存；只在淡出阶段切换透明材质，回池完整还原，销毁时释放。</summary>
    internal sealed class EnemyCorpseFade : IDisposable
    {
        public const float LifetimeSeconds = 8f;
        public const float FadeSeconds = 1.2f;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private readonly List<Binding> bindings = new List<Binding>();
        private bool applied;

        /// <summary>绑定实例身体网格，跳过粒子与界面；材质副本仅在第一次进入淡出时创建。</summary>
        public EnemyCorpseFade(Transform root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                    bindings.Add(new Binding(renderer));
        }

        /// <summary>按剩余寿命淡出，保留每个槽位原有颜色及覆盖块，不污染共享材质。</summary>
        public void Apply(float remainingSeconds)
        {
            float opacity = Mathf.Clamp01(remainingSeconds / FadeSeconds);
            if (opacity >= 1f) return;
            foreach (var binding in bindings)
            {
                if (binding.Renderer == null) continue;
                if (!applied) binding.Begin();
                for (int i = 0; i < binding.Fading.Length; i++)
                {
                    var material = binding.Original[i];
                    if (material == null) continue;
                    var block = binding.WorkingBlocks[i];
                    Color color = binding.Colors[i]; color.a *= opacity;
                    block.SetColor(BaseColor, color); block.SetColor(ColorId, color);
                    binding.Renderer.SetPropertyBlock(block, i);
                }
            }
            applied = true;
        }

        /// <summary>切图、回池与复用统一还原；缓存继续由实例持有，避免每次死亡重复创建材质。</summary>
        public void Reset()
        {
            if (!applied) return;
            foreach (var binding in bindings)
            {
                if (binding.Renderer == null) continue;
                binding.Renderer.sharedMaterials = binding.Original;
                for (int i = 0; i < binding.Original.Length; i++)
                    binding.Renderer.SetPropertyBlock(binding.Blocks[i].isEmpty ? null : binding.Blocks[i], i);
            }
            applied = false;
        }

        public void Dispose()
        {
            Reset();
            foreach (var binding in bindings)
                if (binding.Fading != null)
                    foreach (var material in binding.Fading)
                        if (material != null)
                        {
                            if (Application.isPlaying) UnityEngine.Object.Destroy(material);
                            else UnityEngine.Object.DestroyImmediate(material);
                        }
            bindings.Clear();
        }

        /// <summary>单个网格的共享材质、独立淡出副本与每槽覆盖块；不写回任何项目资源。</summary>
        private sealed class Binding
        {
            public readonly Renderer Renderer;
            public readonly Material[] Original;
            public Material[] Fading;
            public readonly MaterialPropertyBlock[] Blocks;
            public readonly MaterialPropertyBlock[] WorkingBlocks;
            public readonly Color[] Colors;

            public Binding(Renderer renderer)
            {
                Renderer = renderer; Original = renderer.sharedMaterials;
                Blocks = new MaterialPropertyBlock[Original.Length];
                WorkingBlocks = new MaterialPropertyBlock[Original.Length]; Colors = new Color[Original.Length];
                for (int i = 0; i < Blocks.Length; i++)
                { Blocks[i] = new MaterialPropertyBlock(); WorkingBlocks[i] = new MaterialPropertyBlock(); }
            }

            public void Begin()
            {
                if (Fading == null)
                {
                    Fading = new Material[Original.Length];
                    for (int i = 0; i < Original.Length; i++)
                    {
                        if (Original[i] == null) continue;
                        var material = new Material(Original[i])
                        { name = Original[i].name + " (CorpseFade)", hideFlags = HideFlags.HideAndDontSave };
                        material.SetFloat("_Surface", 1f);
                        material.SetFloat("_ZWrite", 0f);
                        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                        material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                        material.SetOverrideTag("RenderType", "Transparent");
                        material.renderQueue = (int)RenderQueue.Transparent;
                        material.SetShaderPassEnabled("ShadowCaster", false);
                        material.SetShaderPassEnabled("DepthOnly", false);
                        Fading[i] = material;
                    }
                }
                for (int i = 0; i < Original.Length; i++)
                {
                    Renderer.GetPropertyBlock(Blocks[i], i);
                    var material = Original[i];
                    Colors[i] = material != null && material.HasProperty(BaseColor) ? material.GetColor(BaseColor) : Color.white;
                    var source = WorkingBlocks[i];
                    Renderer.GetPropertyBlock(source, i);
                    if (source.isEmpty) Renderer.GetPropertyBlock(source);
                    if (source.HasColor(BaseColor)) Colors[i] = source.GetColor(BaseColor);
                    else if (source.HasColor(ColorId)) Colors[i] = source.GetColor(ColorId);
                }
                Renderer.sharedMaterials = Fading;
            }
        }
    }
}
