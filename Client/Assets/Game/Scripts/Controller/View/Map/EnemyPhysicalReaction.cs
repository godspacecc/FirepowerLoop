using UnityEngine;

namespace Game
{
    /// <summary>只控制可见骨骼：存活时叠加胸部回弹，死亡后短时启用预制物理；由EnemyCtrl统一推进。</summary>
    [DisallowMultipleComponent]
    public sealed class EnemyPhysicalReaction : MonoBehaviour
    {
        public const float CorpseSeconds = 2.2f;
        public const string BodyLayerName = "EnemyRagdoll";
        public const string FloorLayerName = "EnemyRagdollFloor";
        /// <summary>制作阶段固定的动画源与回弹骨骼，不在命中时遍历层级。</summary>
        [SerializeField] private Animator animator;
        [SerializeField] private Transform chest;
        /// <summary>预制的简化刚体与对应局部静止姿态，用于每次归还时完整复位。</summary>
        [SerializeField] private Rigidbody[] bodies = new Rigidbody[0];
        [SerializeField] private Vector3[] restPositions = new Vector3[0];
        [SerializeField] private Quaternion[] restRotations = new Quaternion[0];
        /// <summary>当前用途的物理阶段与上一帧叠加状态。</summary>
        private bool dead, simulating, offsetApplied;
        private float hitRemaining, deathAge, simulationSeconds;
        private Quaternion chestBeforeOffset;
        private Vector3 hitAxis;
        private const float HitSeconds = .18f;
        public bool IsRagdoll => dead;
        public bool IsSimulating => simulating;
        public bool IsReady => animator != null && chest != null && bodies.Length == 11 &&
            restPositions.Length == bodies.Length && restRotations.Length == bodies.Length;

        // detectCollisions不随Prefab序列化，实例创建时显式恢复存活休眠态。
        private void Awake() => ResetForReuse();

        /// <summary>专用碰撞层只允许尸体接触平面，不干扰活体、射线、弹丸或其它尸体。</summary>
        public static bool ConfigureCollisionLayers()
        {
            int body = LayerMask.NameToLayer(BodyLayerName), floor = LayerMask.NameToLayer(FloorLayerName);
            if (body < 0 || floor < 0) return false;
            for (int i = 0; i < 32; i++)
            {
                Physics.IgnoreLayerCollision(body, i, i != floor);
                Physics.IgnoreLayerCollision(floor, i, i != body);
            }
            return true;
        }

        /// <summary>命中只刷新短时偏移，不切换物理，也不累积修改上一帧的动画基线。</summary>
        public void PlayHit(Vector3 worldDirection)
        {
            if (!IsReady || dead) return;
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude < .0001f) worldDirection = -transform.forward;
            hitAxis = chest.parent.InverseTransformDirection(Vector3.Cross(Vector3.up, worldDirection.normalized));
            hitRemaining = HitSeconds;
        }

        /// <summary>Update阶段先撤销上帧叠加；活动时间为零时不消耗回弹或死亡寿命。</summary>
        public void Tick(float deltaTime)
        {
            RemoveOffset();
            float seconds = Mathf.Max(0f, deltaTime);
            if (!dead) { hitRemaining = Mathf.Max(0f, hitRemaining - seconds); return; }
            deathAge += seconds;
            if (simulating && deathAge >= simulationSeconds) Freeze();
        }

        /// <summary>Animator求值后只叠加胸部旋转；不修改战斗根的位置与朝向。</summary>
        public void LateTick()
        {
            if (!IsReady || dead || hitRemaining <= 0f || offsetApplied) return;
            float progress = 1f - hitRemaining / HitSeconds;
            float angle = 16f * Mathf.Sin(progress * Mathf.PI) * Mathf.Exp(-progress * 1.5f);
            chestBeforeOffset = chest.localRotation;
            chest.localRotation = Quaternion.AngleAxis(angle, hitAxis) * chestBeforeOffset;
            offsetApplied = true;
        }

        /// <summary>只接受一次死亡；速度按表现设置，伤害和击退仍由战斗权威决定。</summary>
        public bool BeginDeath(Vector3 worldDirection, bool explosive)
        {
            if (!IsReady || dead) return false;
            RemoveOffset(); hitRemaining = 0f; deathAge = 0f; dead = simulating = true;
            simulationSeconds = explosive ? 1.8f : 1.4f;
            animator.enabled = false;
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude < .0001f) worldDirection = -transform.forward;
            Vector3 velocity = worldDirection.normalized * (explosive ? 5f : 1.3f) + Vector3.up * (explosive ? 4f : .65f);
            for (int i = 0; i < bodies.Length; i++)
            {
                var body = bodies[i];
                body.GetComponent<Collider>().enabled = true;
                body.detectCollisions = true; body.isKinematic = false;
                body.velocity = velocity;
                body.angularVelocity = Vector3.Cross(Vector3.up, worldDirection.normalized) * (explosive ? 4f : 2.8f);
                body.WakeUp();
            }
            return true;
        }

        /// <summary>短时模拟后关闭求解和碰撞，爆炸多留落地时间，保留最后可见姿态直到回池。</summary>
        private void Freeze()
        {
            for (int i = 0; i < bodies.Length; i++)
            {
                var body = bodies[i];
                if (!body.isKinematic) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
                body.isKinematic = true; body.detectCollisions = false;
                body.GetComponent<Collider>().enabled = false;
            }
            simulating = false;
        }

        /// <summary>禁用、切图和新用途统一清除速度、回弹及骨骼位姿，下一次激活可立即播放动画。</summary>
        public void ResetForReuse()
        {
            RemoveOffset();
            if (IsReady)
            {
                Freeze();
                for (int i = 0; i < bodies.Length; i++)
                {
                    bodies[i].transform.localPosition = restPositions[i];
                    bodies[i].transform.localRotation = restRotations[i];
                }
                animator.enabled = true;
            }
            dead = simulating = false; hitRemaining = deathAge = 0f;
        }

        private void RemoveOffset()
        {
            if (offsetApplied && chest != null) chest.localRotation = chestBeforeOffset;
            offsetApplied = false;
        }
    }
}
