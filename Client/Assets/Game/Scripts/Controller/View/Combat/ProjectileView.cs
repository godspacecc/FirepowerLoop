using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 投射物灰盒表现组件，只转发碰撞，不保存阵营、伤害或命中结论。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class ProjectileView : MonoBehaviour
    {
        [Serializable]
        private struct ArcadeVisualBinding
        {
            [SerializeField] private string resourceName;
            [SerializeField] private GameObject visualRoot;
            public string ResourceName => resourceName;
            public GameObject VisualRoot => visualRoot;
        }

        [Header("Arcade Presentation")]
        [SerializeField]
        private Renderer defaultArcadeRenderer;

        [SerializeField]
        private ArcadeVisualBinding[] arcadeVisuals = Array.Empty<ArcadeVisualBinding>();
        /// <summary>中性亮度光束遮罩，让红紫强化覆盖完整弹尾，不与原黄色纹理相乘失色。</summary>
        [SerializeField] private Texture2D weaponPowerBeamTexture;
        /// <summary>已确认的紫火箭/射线短尾，使用显式材质；只从本次权威姿态采样。</summary>
        [SerializeField] private LineRenderer purpleWeaponTail;
        private float purpleTailLength;

        /// <summary>缓存视觉子节点原始比例；只缩放外观，不影响根节点与碰撞盒。</summary>
        private Vector3[] originalVisualScales;

        private int projectileRuntimeId;
        private Action<int, Collider> collisionHandler;
        /// <summary>只缓存本实例的原始颜色块和粒子渐变；换色不创建材质，回池复用可完整恢复。</summary>
        private Renderer[] powerRenderers;
        private MaterialPropertyBlock[] originalPowerBlocks;
        private ParticleSystem[] powerParticles;
        private ParticleSystem.MinMaxGradient[] originalStartColors,originalLifeColors;
        private int appliedPowerLevel=-1;

        public Collider TriggerCollider { get; private set; }
        public BoxCollider BoxCollider { get; private set; }
        public Rigidbody Rigidbody { get; private set; }

        private void Awake()
        {
            EnsureComponents();
        }

        public void Bind(int runtimeId, Action<int, Collider> onCollision)
        {
            projectileRuntimeId = runtimeId;
            collisionHandler = onCollision;
            EnsureComponents();
        }

        /// <summary>由投射物端口应用权威位置与完整三维飞行方向，使弹体和扫描碰撞盒保持一致。</summary>
        public void ApplyPose(Vector3 position, Vector3 direction)
        {
            transform.position = position;
            if(purpleWeaponTail!=null&&purpleWeaponTail.enabled&&direction.sqrMagnitude>.0001f)
            {purpleWeaponTail.SetPosition(0,position);purpleWeaponTail.SetPosition(1,position-direction.normalized*purpleTailLength);}
            if (direction.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(direction.normalized, Mathf.Abs(direction.normalized.y)>.999f?Vector3.forward:Vector3.up);
            }
        }

        /// <summary>按配置资源名切换纯视觉子物体；敌方弹和未配置资源保持原弹体。</summary>
        public void SetArcadePresentation(string visualResourceName, bool hostile, float visualScale = 1f)
        {
            visualResourceName=ArcadeWeaponPowerVisual.Split(visualResourceName,out var powerLevel);
            ApplyPowerColor(hostile?0:powerLevel);
            bool rocket=visualResourceName=="ArcadePanzerschreckRocket",ray=visualResourceName=="ArcadeRaygunBolt";
            purpleTailLength=rocket?.8f:.45f;
            if(purpleWeaponTail!=null){purpleWeaponTail.enabled=!hostile&&powerLevel>=2&&(rocket||ray);purpleWeaponTail.startWidth=rocket?.07f:.05f;}
            if (originalVisualScales == null)
            {
                originalVisualScales = new Vector3[arcadeVisuals.Length];
                for (int i = 0; i < arcadeVisuals.Length; i++)
                    if (arcadeVisuals[i].VisualRoot != null)
                        originalVisualScales[i] = arcadeVisuals[i].VisualRoot.transform.localScale;
            }
            var matched = false;
            for (var index = 0; index < arcadeVisuals.Length; index++)
            {
                var binding = arcadeVisuals[index];
                var active = !matched && !hostile && !string.IsNullOrWhiteSpace(visualResourceName) &&
                             string.Equals(binding.ResourceName, visualResourceName, StringComparison.Ordinal);
                // 本次只调整已预览的普通步枪弹；其他武器、敌弹及池复用恢复原比例。
                if (binding.VisualRoot != null)
                    binding.VisualRoot.transform.localScale = originalVisualScales[index] *
                        (active && visualResourceName == "ArcadeBulletBasic" ? visualScale : 1f);
                SetActive(binding.VisualRoot, active);
                if (active) matched = true;
            }
            if (defaultArcadeRenderer != null) defaultArcadeRenderer.enabled = !matched;
        }

        /// <summary>三种普通枪的视觉变体共用短弹载体；弓箭、敌弹和特殊模型仍使用既有载体。</summary>
        internal static bool UsesOrdinaryArcadeBullet(string visualResourceName)
        {
            visualResourceName=ArcadeWeaponPowerVisual.Split(visualResourceName,out _);
            return visualResourceName == "ArcadeBulletBasic" || visualResourceName == "ArcadeBulletRapid" ||
                   visualResourceName == "ArcadeBulletSpread";
        }

        /// <summary>保留原弹体形状，只改本实例的强化色；敌弹和普通弹恢复缓存，避免对象池串色。</summary>
        private void ApplyPowerColor(int level)
        {
            if(level==appliedPowerLevel)return;
            if(powerRenderers==null)
            {
                powerRenderers=GetComponentsInChildren<Renderer>(true);originalPowerBlocks=new MaterialPropertyBlock[powerRenderers.Length];
                for(int i=0;i<powerRenderers.Length;i++){var block=new MaterialPropertyBlock();powerRenderers[i].GetPropertyBlock(block);originalPowerBlocks[i]=block;}
                powerParticles=GetComponentsInChildren<ParticleSystem>(true);
                originalStartColors=new ParticleSystem.MinMaxGradient[powerParticles.Length];originalLifeColors=new ParticleSystem.MinMaxGradient[powerParticles.Length];
                for(int i=0;i<powerParticles.Length;i++){originalStartColors[i]=powerParticles[i].main.startColor;originalLifeColors[i]=powerParticles[i].colorOverLifetime.color;}
            }
            var color=level>=2?new Color(.8f,.12f,1f):new Color(1f,.07f,.04f);
            for(int i=0;i<powerRenderers.Length;i++)
            {
                if(level==0){powerRenderers[i].SetPropertyBlock(originalPowerBlocks[i]);continue;}
                var block=new MaterialPropertyBlock();powerRenderers[i].GetPropertyBlock(block);
                block.SetColor("_BaseColor",color);block.SetColor("_Color",color);block.SetColor("_TintColor",color);block.SetColor("_EmissionColor",color*2);
                if(weaponPowerBeamTexture!=null&&powerRenderers[i].sharedMaterial!=null&&powerRenderers[i].sharedMaterial.name.Contains("BulletStreak"))
                    block.SetTexture("_BaseMap",weaponPowerBeamTexture);
                powerRenderers[i].SetPropertyBlock(block);
            }
            for(int i=0;i<powerParticles.Length;i++)
            {
                var main=powerParticles[i].main;var life=powerParticles[i].colorOverLifetime;
                main.startColor=level==0?originalStartColors[i]:new ParticleSystem.MinMaxGradient(color);
                if(level==0)life.color=originalLifeColors[i];
                else if(life.enabled)
                {
                    var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                        originalLifeColors[i].gradient?.alphaKeys??new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(0,1)});
                    life.color=new ParticleSystem.MinMaxGradient(gradient);
                }
            }
            appliedPowerLevel=level;
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active) target.SetActive(active);
        }

        public bool Validate(out string error)
        {
            EnsureComponents();
            if (BoxCollider == null)
            {
                error = "投射物 Prefab 缺少 BoxCollider";
                return false;
            }

            if (!TriggerCollider.isTrigger)
            {
                error = "投射物 Collider 必须是 Trigger";
                return false;
            }

            if (Rigidbody == null)
            {
                error = "投射物 Prefab 缺少 Rigidbody";
                return false;
            }

            if (!Rigidbody.isKinematic)
            {
                error = "投射物 Rigidbody 必须是 kinematic";
                return false;
            }

            error = null;
            return true;
        }

        private void OnTriggerEnter(Collider other)
        {
            collisionHandler?.Invoke(projectileRuntimeId, other);
        }

        private void EnsureComponents()
        {
            if (TriggerCollider == null)
            {
                TriggerCollider = GetComponent<Collider>();
            }

            if (BoxCollider == null)
            {
                BoxCollider = GetComponent<BoxCollider>();
            }

            if (Rigidbody == null)
            {
                Rigidbody = GetComponent<Rigidbody>();
            }
        }
    }
}
