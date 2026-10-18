using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game
{
    /// <summary>角色独占的金白色分段保护环；只呈现有效状态，不计时、不参与碰撞或伤害。</summary>
    internal sealed class ArcadeProtectionPresentation : IDisposable
    {
        // 脚边留空，低于武器和准星；分段细线与拾取护盾的蓝色护罩区分。
        private readonly GameObject root;
        private readonly Material material;

        /// <summary>把四段细线挂到角色子树，共享本实例独占材质，随角色移动与隐藏。</summary>
        public ArcadeProtectionPresentation(Transform owner, Shader shader)
        {
            // 先验证显式依赖，禁止失败后留下未归属的保护环对象。
            if (shader == null) throw new ArgumentNullException(nameof(shader));
            root = new GameObject("ArcadeProtection");
            root.SetActive(false);
            root.transform.SetParent(owner, false);
            material = new Material(shader);
            material.name = "ArcadeProtectionGoldWhite";
            material.SetColor("_BaseColor", new Color(1f, .88f, .48f, 1f));
            for (int segment = 0; segment < 4; segment++)
            {
                var arc = new GameObject("ProtectionArc");
                arc.layer = owner.gameObject.layer;
                arc.transform.SetParent(root.transform, false);
                var line = arc.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.sharedMaterial = material;
                if (segment % 2 != 0)
                {
                    var properties = new MaterialPropertyBlock();
                    properties.SetColor("_BaseColor", new Color(1f, .98f, .88f, 1f));
                    line.SetPropertyBlock(properties);
                }
                line.widthMultiplier = .04f;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.positionCount = 17;
                for (int point = 0; point < line.positionCount; point++)
                {
                    float angle = (segment * 90f + point * 72f / 16f) * Mathf.Deg2Rad;
                    line.SetPosition(point, new Vector3(Mathf.Cos(angle) * 1.02f, .09f, Mathf.Sin(angle) * 1.02f));
                }
            }
        }

        /// <summary>每次同步直接采用当前有效状态，不排队播放，不设置延迟消失。</summary>
        public void SetActive(bool active)
        {
            if (root != null) root.SetActive(active);
        }

        /// <summary>离开街机或角色销毁时释放实例及独占材质，重复释放安全。</summary>
        public void Dispose()
        {
            if (root != null) root.SetActive(false);
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(root);
                UnityEngine.Object.Destroy(material);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }
    }
}
