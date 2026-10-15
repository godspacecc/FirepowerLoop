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

        private int projectileRuntimeId;
        private Action<int, Collider> collisionHandler;

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
            if (direction.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(direction.normalized, Mathf.Abs(direction.normalized.y)>.999f?Vector3.forward:Vector3.up);
            }
        }

        /// <summary>按配置资源名切换纯视觉子物体；敌方弹和未配置资源保持原弹体。</summary>
        public void SetArcadePresentation(string visualResourceName, bool hostile)
        {
            var matched = false;
            for (var index = 0; index < arcadeVisuals.Length; index++)
            {
                var binding = arcadeVisuals[index];
                var active = !matched && !hostile && !string.IsNullOrWhiteSpace(visualResourceName) &&
                             string.Equals(binding.ResourceName, visualResourceName, StringComparison.Ordinal);
                SetActive(binding.VisualRoot, active);
                if (active) matched = true;
            }
            if (defaultArcadeRenderer != null) defaultArcadeRenderer.enabled = !matched;
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

        public void CollectMovementHits(Vector3 from, Vector3 to, List<ProjectileMovementHit> results)
        {
            results.Clear();
            EnsureComponents();
            if (BoxCollider == null)
            {
                return;
            }

            var delta = to - from;
            var distance = delta.magnitude;
            if (distance <= 0.0001f)
            {
                return;
            }

            var direction = delta / distance;
            var scale = transform.lossyScale;
            var halfExtents = new Vector3(
                Mathf.Abs(BoxCollider.size.x * scale.x) * 0.5f,
                Mathf.Abs(BoxCollider.size.y * scale.y) * 0.5f,
                Mathf.Abs(BoxCollider.size.z * scale.z) * 0.5f);
            var center = from + transform.rotation * Vector3.Scale(BoxCollider.center, scale);
            var hits = Physics.BoxCastAll(
                center,
                halfExtents,
                direction,
                transform.rotation,
                distance,
                ~0,
                QueryTriggerInteraction.Collide);

            for (var i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (hit.collider == null || hit.collider == BoxCollider)
                {
                    continue;
                }

                results.Add(new ProjectileMovementHit(hit.collider, hit.distance));
            }

            results.Sort((a, b) => a.Distance.CompareTo(b.Distance));
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
